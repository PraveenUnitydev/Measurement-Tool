using System.Collections.Generic;
using UnityEngine;
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
