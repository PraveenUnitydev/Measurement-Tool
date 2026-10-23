using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// The only place that talks to Addressables / Unity's cache about individual bundle files.
    /// Everything else (status, planning, registry) works on plain BundleRef lists, which is what
    /// makes that logic testable outside Unity.
    /// </summary>
    public static class AddressablesBundleResolver
    {
        /// <summary>
        /// Every REMOTE bundle file needed to load these locations: the asset's own bundle plus all of
        /// its dependencies, followed recursively (this project builds with non-recursive dependency
        /// calculation, so a bundle lists only its direct dependencies).
        /// </summary>
        public static List<BundleRef> CollectBundles(IEnumerable<IResourceLocation> locations)
        {
            var found = new Dictionary<string, BundleRef>();
            var visited = new HashSet<string>();
            if (locations != null)
                foreach (var loc in locations) Visit(loc, found, visited);
            return new List<BundleRef>(found.Values);
        }

        private static void Visit(IResourceLocation loc, Dictionary<string, BundleRef> found, HashSet<string> visited)
        {
            if (loc == null) return;
            string id = loc.PrimaryKey + "|" + loc.InternalId + "|" + loc.ResourceType;
            if (!visited.Add(id)) return;

            var options = loc.Data as AssetBundleRequestOptions;
            if (options != null && ResourceManagerConfig.IsPathRemote(loc.InternalId))
            {
                var bundle = new BundleRef(options.BundleName, options.Hash, options.BundleSize);
                if (!found.ContainsKey(bundle.Key)) found[bundle.Key] = bundle;
            }

            if (loc.HasDependencies)
                foreach (var dep in loc.Dependencies) Visit(dep, found, visited);
        }

        /// <summary>
        /// Tells Unity's cache this file was "just used". Unity deletes any cached file that sits unused for 150 days
        /// (its maximum, and not changeable), so refreshing the time at each start keeps downloaded vehicles from
        /// quietly disappearing. Returns false if the file isn't cached.
        /// </summary>
        /// <summary>
        /// Find a key's bundles in the catalogs that are already loaded, synchronously: no async operation and no
        /// waiting for frames. Returns null when no loaded catalog knows the key (the caller can then ask the slow way).
        /// </summary>
        public static List<BundleRef> CollectBundlesNow(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            key = DasKeys.Real(key);
            var all = new List<IResourceLocation>();
            try
            {
                foreach (IResourceLocator locator in Addressables.ResourceLocators)
                {
                    if (locator == null) continue;
                    IList<IResourceLocation> locs;
                    if (locator.Locate(key, null, out locs) && locs != null) all.AddRange(locs);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Storage] Catalog lookup failed for " + key + ": " + e.Message);
                return null;
            }
            return all.Count > 0 ? CollectBundles(all) : null;
        }

        // ── Older versions ────────────────────────────────────────────────
        // Unity keeps each version of a file in its own folder: <cache>/<file name>/<version hash>/. Addressables only
        // ever opens the version the catalog names, so an older version on disk can't be opened again: it only takes space.

        public static string CacheRoot()
        {
            try { Cache c = Caching.currentCacheForWriting; return c.valid ? c.path : null; }
            catch (Exception) { return null; }
        }

        /// <summary>Bytes on disk for a file name: every version, or every version except <paramref name="exceptHash"/>.</summary>
        public static long BytesOnDisk(string bundleName, string exceptHash = null)
        {
            string root = CacheRoot();
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(bundleName)) return 0;
            long sum = 0;
            try
            {
                string dir = Path.Combine(root, bundleName);
                if (!Directory.Exists(dir)) return 0;
                foreach (string versionDir in Directory.GetDirectories(dir))
                {
                    if (exceptHash != null && string.Equals(Path.GetFileName(versionDir), exceptHash, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string f in Directory.GetFiles(versionDir, "*", SearchOption.AllDirectories)) sum += new FileInfo(f).Length;
                }
            }
            catch (Exception) { }
            return sum;
        }

        /// <summary>True when a version other than the catalog's current one is on disk.</summary>
        public static bool HasOlderVersions(BundleRef bundle)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.name)) return false;
            var versions = new List<Hash128>();
            try { Caching.GetCachedVersions(bundle.name, versions); } catch (Exception) { return false; }
            Hash128 current = string.IsNullOrEmpty(bundle.hash) ? default(Hash128) : Hash128.Parse(bundle.hash);
            foreach (Hash128 h in versions) if (h != current) return true;
            return false;
        }

        /// <summary>Delete every version of a file. False if Unity refused (for example because it is loaded).</summary>
        public static bool ClearAllVersions(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName)) return false;
            try { return Caching.ClearAllCachedVersions(bundleName); }
            catch (Exception e) { Debug.LogWarning("[Storage] Could not remove " + bundleName + ": " + e.Message); return false; }
        }

        /// <summary>Delete every version of a file except the catalog's current one.</summary>
        public static bool ClearOlderVersions(BundleRef bundle)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.name) || string.IsNullOrEmpty(bundle.hash)) return false;
            Hash128 hash = Hash128.Parse(bundle.hash);
            if (!hash.isValid) return false;
            try { return Caching.ClearOtherCachedVersions(bundle.name, hash); }
            catch (Exception e) { Debug.LogWarning("[Storage] Could not remove older versions of " + bundle.name + ": " + e.Message); return false; }
        }

        public static bool MarkUsed(BundleRef bundle)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.name) || string.IsNullOrEmpty(bundle.hash)) return false;
            Hash128 hash = Hash128.Parse(bundle.hash);
            if (!hash.isValid) return false;
            return Caching.MarkAsUsed(new CachedAssetBundle(bundle.name, hash));
        }

        /// <summary>
        /// Whether this exact file (name + hash) is in Unity's cache. Addressables caches a bundle under
        /// (BundleName, Hash), so this is the same test it uses to decide whether to download.
        /// </summary>
        public static bool IsCached(BundleRef bundle)
        {
            if (bundle == null || string.IsNullOrEmpty(bundle.name) || string.IsNullOrEmpty(bundle.hash)) return false;
            Hash128 hash = Hash128.Parse(bundle.hash);
            if (!hash.isValid) return false;
            return Caching.IsVersionCached(new CachedAssetBundle(bundle.name, hash));
        }
    }
}
