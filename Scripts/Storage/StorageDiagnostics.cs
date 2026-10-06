using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// READ-ONLY diagnostics. Add this component to any object in the Home scene, wait for the vehicle
    /// list to appear, then press F9 (or use the component's context menu "Write storage report").
    ///
    /// It writes a text report (and copies it to the clipboard) showing, for every vehicle in the catalog,
    /// the exact cache files it needs, their sizes, how many are really on disk, which files are shared,
    /// and what is sitting in the cache that no current vehicle uses. It never downloads, deletes or
    /// changes anything - it exists to check the storage assumptions against real data before any
    /// delete / update feature is switched on.
    /// </summary>
    public class StorageDiagnostics : MonoBehaviour
    {
        [Tooltip("Press this key to write the report")]
        public KeyCode reportKey = KeyCode.F9;

        private bool _running;

        private void Update()
        {
            if (Input.GetKeyDown(reportKey)) WriteReport();
        }

        [ContextMenu("Write storage report")]
        public void WriteReport()
        {
            if (_running) return;
            StartCoroutine(ReportRoutine());
        }

        private class VehicleScan
        {
            public RemoteVehicleInfo info;
            public bool keyFound;
            public List<BundleRef> bundles = new List<BundleRef>();
        }

        private class DiskEntry
        {
            public string bundleName, hashDir, path;
            public long bytes;
            public DateTime lastWriteUtc;
        }

        private IEnumerator ReportRoutine()
        {
            _running = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();

            var loader = RemoteAddressableVehicleLoader.Instance;
            if (loader == null || !loader.IsCatalogLoaded)
            {
                var msg = new StringBuilder();
                Line(msg, "The vehicle list isn't loaded yet. Open the Home scene, wait for the vehicles to appear, then press the key again.");
                SaveReport(msg);
                yield break;
            }

            // Ask Addressables what files each vehicle needs (one frame per vehicle at most)
            var scans = new List<VehicleScan>();
            foreach (var v in loader.GetAvailableVehicles())
            {
                var scan = new VehicleScan { info = v };
                if (!string.IsNullOrEmpty(v.addressableKey))
                {
                    var h = Addressables.LoadResourceLocationsAsync(v.addressableKey);
                    yield return h;
                    if (h.Status == AsyncOperationStatus.Succeeded && h.Result != null && h.Result.Count > 0)
                    {
                        scan.keyFound = true;
                        scan.bundles = AddressablesBundleResolver.CollectBundles(h.Result);
                    }
                    Addressables.Release(h);
                }
                scans.Add(scan);
            }

            StringBuilder report = null;
            string failure = null;
            try { report = BuildReport(scans, clock); }
            catch (Exception e) { failure = e.ToString(); }

            if (report == null)
            {
                Debug.LogError("[Storage] Could not build the report: " + failure);
                _running = false;
                yield break;
            }
            SaveReport(report);
        }

        private static StringBuilder BuildReport(List<VehicleScan> scans, System.Diagnostics.Stopwatch clock)
        {
            // ── Section 1: environment ───────────────────────────────────
            var report = new StringBuilder();
            Line(report, "DAS STORAGE REPORT   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Line(report, "Unity " + Application.unityVersion + "   " + Application.platform);
            Line(report, "persistentDataPath: " + Application.persistentDataPath);
            Line(report, "");
            Line(report, "1. UNITY CACHE");
            int cacheCount = Caching.cacheCount;
            Line(report, "   caches: " + cacheCount + "   current for writing: " + SafePath(Caching.currentCacheForWriting));
            var cachePaths = new List<string>();
            for (int i = 0; i < cacheCount; i++)
            {
                Cache c = Caching.GetCacheAt(i);
                cachePaths.Add(c.path);
                // Unity reports "no limit" as a gigantic number; show that as "none"
                bool noLimit = c.maximumAvailableStorageSpace <= 0 || c.maximumAvailableStorageSpace >= (1L << 50);
                Line(report, string.Format(CultureInfo.InvariantCulture,
                    "   [{0}] {1}\n       occupied {2}   size limit {3}   keeps unused bundles for {4} days",
                    i, c.path, ByteFormat.Format(c.spaceOccupied),
                    noLimit ? "none" : ByteFormat.Format(c.maximumAvailableStorageSpace) + " (free " + ByteFormat.Format(c.spaceFree) + ")",
                    (c.expirationDelay / 86400.0).ToString("0", CultureInfo.InvariantCulture)));
            }
            var storageService = VehicleStorageService.Instance;
            Line(report, "   150 days is Unity's maximum and can't be raised. At each start the app marks every downloaded vehicle's files as just used, "
                + "so they only expire if the app isn't started for 150 days. Refreshed this session: "
                + (storageService != null && storageService.IsReady ? storageService.LastRefreshedFiles + " file(s)" : "not done yet"));
            Line(report, "");
            Line(report, "   catalogs loaded by Addressables:");
            foreach (var locator in Addressables.ResourceLocators)
                Line(report, "     - " + locator.LocatorId);
            Line(report, "");

            // ── Section 2: per vehicle ───────────────────────────────────
            var tracker = ReadLegacyTracker();
            Line(report, "2. VEHICLES (" + scans.Count + " in the catalog)");
            Line(report, "   id | catalog version | files | size | cached | state | old tracker says");
            long sumAll = 0;
            int fullyCached = 0, partlyCached = 0, notCached = 0, keyMissing = 0;
            foreach (var s in scans.OrderBy(x => x.info.vehicleId, StringComparer.OrdinalIgnoreCase))
            {
                int cachedFiles = s.bundles.Count(AddressablesBundleResolver.IsCached);
                bool mainCached = MainFileCached(s);
                long total = s.bundles.Sum(b => Math.Max(0, b.size));
                long cachedBytes = s.bundles.Where(AddressablesBundleResolver.IsCached).Sum(b => Math.Max(0, b.size));
                sumAll += total;
                string state;
                if (!s.keyFound) { state = "KEY NOT FOUND in Addressables catalog"; keyMissing++; }
                else if (s.bundles.Count == 0) { state = "no remote files"; }
                else if (cachedFiles == s.bundles.Count) { state = "fully on disk"; fullyCached++; }
                else if (!mainCached) { state = cachedFiles == 0 ? "not downloaded" : "NOT downloaded - only a shared file is on disk"; notCached++; }
                else { state = "PARTLY on disk (" + ByteFormat.Format(total - cachedBytes) + " missing)"; partlyCached++; }

                LegacyTrackerEntry t;
                tracker.TryGetValue(s.info.vehicleId ?? "", out t);
                string trackerText = t == null ? "-" : ((string.IsNullOrEmpty(t.downloadedDate) ? "?" : t.downloadedDate) + ", version " + (string.IsNullOrEmpty(t.version) ? "NONE" : t.version));

                Line(report, string.Format(CultureInfo.InvariantCulture, "   {0} | v{1} | {2} | {3} | {4}/{2} | {5} | {6}",
                    s.info.vehicleId, string.IsNullOrEmpty(s.info.version) ? "?" : s.info.version,
                    s.bundles.Count, ByteFormat.Format(total), cachedFiles, state, trackerText));
            }
            Line(report, "");
            Line(report, string.Format(CultureInfo.InvariantCulture,
                "   summary: {0} fully on disk, {1} partly, {2} not downloaded, {3} with no matching key. Everything together would be {4} if shared files counted once per vehicle.",
                fullyCached, partlyCached, notCached, keyMissing, ByteFormat.Format(sumAll)));
            Line(report, "");

            // ── Section 3: shared files ──────────────────────────────────
            var users = new Dictionary<string, List<string>>();
            var byKey = new Dictionary<string, BundleRef>();
            foreach (var s in scans)
                foreach (var b in s.bundles)
                {
                    List<string> list;
                    if (!users.TryGetValue(b.Key, out list)) { list = new List<string>(); users[b.Key] = list; byKey[b.Key] = b; }
                    list.Add(s.info.vehicleId);
                }
            var shared = users.Where(kv => kv.Value.Count > 1).OrderByDescending(kv => byKey[kv.Key].size * (kv.Value.Count - 1)).ToList();
            long saved = shared.Sum(kv => byKey[kv.Key].size * (kv.Value.Count - 1));
            Line(report, "3. SHARED FILES");
            Line(report, "   " + users.Count + " distinct files in total. Used by more than one vehicle: " + shared.Count + " (sharing saves " + ByteFormat.Format(saved) + ").");
            foreach (var kv in shared.Take(10))
                Line(report, "   " + ByteFormat.Format(byKey[kv.Key].size) + "  used by " + kv.Value.Count + " vehicles  " + Short(byKey[kv.Key].name));
            Line(report, "");

            // ── Section 4: what is physically on disk ────────────────────
            Line(report, "4. FILES ON DISK");
            var needed = new HashSet<string>(users.Keys.Select(k => k.ToLowerInvariant()));
            var neededNames = new HashSet<string>(byKey.Values.Select(b => (b.name ?? "").ToLowerInvariant()));
            var onDisk = new List<DiskEntry>();
            foreach (string cp in cachePaths) ScanCacheFolder(cp, onDisk, report);
            long diskTotal = onDisk.Sum(e => e.bytes);
            var current = onDisk.Where(e => needed.Contains((e.bundleName + "|" + e.hashDir).ToLowerInvariant())).ToList();
            var outdated = onDisk.Where(e => !needed.Contains((e.bundleName + "|" + e.hashDir).ToLowerInvariant()) && neededNames.Contains(e.bundleName.ToLowerInvariant())).ToList();
            var unknown = onDisk.Where(e => !neededNames.Contains(e.bundleName.ToLowerInvariant())).ToList();
            Line(report, "   cached files found: " + onDisk.Count + ", " + ByteFormat.Format(diskTotal) + " on disk");
            Line(report, "   - used by a current vehicle:                  " + current.Count + " file(s), " + ByteFormat.Format(current.Sum(e => e.bytes)));
            Line(report, "   - OLDER copy of a file a vehicle still uses:  " + outdated.Count + " file(s), " + ByteFormat.Format(outdated.Sum(e => e.bytes)));
            Line(report, "   - not used by any current vehicle:            " + unknown.Count + " file(s), " + ByteFormat.Format(unknown.Sum(e => e.bytes)));
            Line(report, "   (the last two are leftovers from earlier versions and removed vehicles)");
            if (onDisk.Count > 0)
            {
                Line(report, "   newest download: " + onDisk.Max(e => e.lastWriteUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    + "   oldest: " + onDisk.Min(e => e.lastWriteUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                Line(report, "   example folder layout (so the scan can be checked):");
                foreach (var e in onDisk.Take(3)) Line(report, "     " + e.path);
            }
            Line(report, "");

            // ── Section 5: old tracker ───────────────────────────────────
            Line(report, "5. OLD DOWNLOAD LIST (downloaded_vehicles.json)");
            var ids = new HashSet<string>(scans.Select(s => s.info.vehicleId ?? ""), StringComparer.OrdinalIgnoreCase);
            int noVersion = tracker.Values.Count(t => string.IsNullOrEmpty(t.version));
            var notInCatalog = tracker.Keys.Where(k => !ids.Contains(k)).ToList();
            Line(report, "   vehicles listed: " + tracker.Count + ". Listed WITHOUT a saved version (these show a false 'update available'): " + noVersion + ".");
            Line(report, "   listed under an id that isn't in the catalog: " + notInCatalog.Count + (notInCatalog.Count > 0 ? " -> " + string.Join(", ", notInCatalog.Take(8).ToArray()) + (notInCatalog.Count > 8 ? ", ..." : "") : ""));
            int listedButMissing = tracker.Keys.Count(k => scans.Any(s => string.Equals(s.info.vehicleId, k, StringComparison.OrdinalIgnoreCase) && s.bundles.Count > 0 && !MainFileCached(s)));
            Line(report, "   listed as downloaded, but the vehicle's main file is NOT on disk: " + listedButMissing + "  (Home shows these as downloaded)");
            Line(report, "");

            // ── Section 6: where Addressables keeps its catalog ──────────
            Line(report, "6. ADDRESSABLES CATALOG FILES");
            AppendCatalogInfo(report);
            Line(report, "");
            Line(report, "report took " + clock.ElapsedMilliseconds + " ms");
            return report;
        }

        private string SaveReport(StringBuilder sb)
        {
            string path = null;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "DAS");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "storage_report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(path, sb.ToString());
                GUIUtility.systemCopyBuffer = sb.ToString();
                Debug.Log("[Storage] Report written to " + path + " (also copied to the clipboard)");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Storage] Could not save the report: " + e.Message);
            }
            _running = false;
            return path;
        }

        private static void ScanCacheFolder(string cachePath, List<DiskEntry> into, StringBuilder report)
        {
            try
            {
                if (string.IsNullOrEmpty(cachePath) || !Directory.Exists(cachePath)) return;
                foreach (string nameDir in Directory.GetDirectories(cachePath))
                {
                    string bundleName = Path.GetFileName(nameDir);
                    foreach (string hashDir in Directory.GetDirectories(nameDir))
                    {
                        long bytes = 0;
                        DateTime newest = DateTime.MinValue;
                        foreach (string f in Directory.GetFiles(hashDir, "*", SearchOption.AllDirectories))
                        {
                            var fi = new FileInfo(f);
                            bytes += fi.Length;
                            if (fi.LastWriteTimeUtc > newest) newest = fi.LastWriteTimeUtc;
                        }
                        into.Add(new DiskEntry { bundleName = bundleName, hashDir = Path.GetFileName(hashDir), path = hashDir, bytes = bytes, lastWriteUtc = newest });
                    }
                }
            }
            catch (Exception e)
            {
                Line(report, "   (could not fully scan " + cachePath + ": " + e.Message + ")");
            }
        }

        /// <summary>
        /// Where the app was told to fetch its catalog (from the build's own settings file) and which catalog files are
        /// saved on this PC. Needed to decide how an older version of a vehicle could be kept.
        /// </summary>
        private static void AppendCatalogInfo(StringBuilder report)
        {
            try
            {
                string settings = Path.Combine(Application.streamingAssetsPath, "aa", "settings.json");
                if (File.Exists(settings))
                {
                    var found = new List<string>();
                    foreach (Match m in Regex.Matches(File.ReadAllText(settings), "\"m_InternalId\"\\s*:\\s*\"([^\"]*catalog[^\"]*)\"", RegexOptions.IgnoreCase))
                        if (!found.Contains(m.Groups[1].Value)) found.Add(m.Groups[1].Value);
                    Line(report, "   the build is told to fetch its catalog from: " + (found.Count == 0 ? "(no catalog address found in settings.json)" : ""));
                    foreach (string f in found) Line(report, "     " + f);
                }
                else Line(report, "   (no Addressables settings.json at " + settings + ")");
            }
            catch (Exception e) { Line(report, "   (could not read the build's catalog address: " + e.Message + ")"); }

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "com.unity.addressables");
                if (!Directory.Exists(dir)) { Line(report, "   catalog files saved on this PC: none (" + dir + ")"); return; }
                string[] files = Directory.GetFiles(dir);
                Line(report, "   catalog files saved on this PC (" + dir + "): " + files.Length);
                foreach (string f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Take(12))
                {
                    var fi = new FileInfo(f);
                    Line(report, "     " + fi.Name + "   " + ByteFormat.Format(fi.Length) + "   " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception e) { Line(report, "   (could not list the saved catalog files: " + e.Message + ")"); }
        }

        private static Dictionary<string, LegacyTrackerEntry> ReadLegacyTracker()
        {
            var result = new Dictionary<string, LegacyTrackerEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "downloaded_vehicles.json");
                if (!File.Exists(path)) return result;
                var data = JsonUtility.FromJson<LegacyTrackerData>(File.ReadAllText(path));
                if (data != null && data.vehicles != null)
                    foreach (var e in data.vehicles)
                        if (e != null && !string.IsNullOrWhiteSpace(e.vehicleId)) result[e.vehicleId.Trim()] = e;
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>The vehicle's main file is its largest one; the small ones are shared scripts and the like.</summary>
        private static bool MainFileCached(VehicleScan s)
        {
            BundleRef main = s.bundles.OrderByDescending(b => b.size).FirstOrDefault();
            return main != null && AddressablesBundleResolver.IsCached(main);
        }

        private static string SafePath(Cache c) { try { return c.path; } catch (Exception) { return "?"; } }
        private static string Short(string s) { return string.IsNullOrEmpty(s) ? "(unnamed)" : (s.Length > 70 ? s.Substring(0, 70) + "..." : s); }
        private static void Line(StringBuilder sb, string text) { sb.AppendLine(text); }
    }
}
