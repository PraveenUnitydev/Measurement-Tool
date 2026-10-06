using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VehicleMeasurement.Storage
{
    /// <summary>What the app knows about one vehicle's files on this PC, ready to show.</summary>
    public class VehicleStorageState
    {
        public string vehicleId;
        public string vehicleName;
        public string addressableKey;
        public VehicleStatus status;
        /// <summary>The vehicle's files are on this PC and it can be opened.</summary>
        public bool IsDownloaded;
        /// <summary>A newer version exists (or the old files are gone and a newer one is needed).</summary>
        public bool NeedsUpdate;
        public long totalBytes;
        public long downloadBytes;
        public string versionText = "";
        public DateTime? downloadedAtUtc;
        public string label = "";
        public LabelTone tone = LabelTone.None;
    }

    public class StorageRow
    {
        public string vehicleId, name, versionText, statusText;
        public long bytes;
        public DateTime? downloadedAtUtc;
        public LabelTone tone;
    }

    public class StorageSummary
    {
        public int downloadedCount;
        public long downloadedBytes;           // real disk use: a shared file counts once
        public int notDownloadedCount;
        public long notDownloadedBytes;        // what it would take to download them all
        public string cachePath = "";
    }

    public class RemoveOutcome
    {
        public bool success;
        public string message = "";
        public long freedBytes;
        public int vehicles;
    }

    /// <summary>
    /// The single answer to "which vehicles are really on this PC?". It keeps a registry of downloaded vehicles
    /// in line with what is physically in Unity's cache, so Home no longer shows a vehicle as downloaded after its
    /// files are gone, and removing a vehicle never deletes a file another vehicle still needs.
    ///
    /// Created automatically; nothing needs to be added to a scene.
    /// </summary>
    public class VehicleStorageService : MonoBehaviour
    {
        /// <summary>
        /// False until vehicles can be pinned to the version they were downloaded with. Until then the app switches to
        /// the newest catalog on its own, so an out-of-date vehicle must be updated before it opens, and the labels
        /// say "Update needed" instead of "Update available".
        /// </summary>
        public const bool CanKeepOldVersions = false;

        public static VehicleStorageService Instance { get; private set; }

        /// <summary>Raised when the set of downloaded vehicles changes (and once when the first scan finishes).</summary>
        public static event Action Changed;

        public bool IsReady { get; private set; }

        /// <summary>
        /// True from startup until the first scan has finished (or has given up). While pending, the old download list
        /// can't be trusted, so it is hidden instead of flashing vehicles that may not be on this PC.
        /// </summary>
        public bool IsPending { get { return !IsReady && !_gaveUp; } }

        public VehicleRegistry Registry { get; private set; }

        private readonly List<CatalogVehicle> _catalog = new List<CatalogVehicle>();
        private bool _busy;
        private bool _gaveUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("VehicleStorageService");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<VehicleStorageService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            try
            {
                Registry = new VehicleRegistry(Path.Combine(Application.persistentDataPath, "DAS", "vehicle_registry.json"));
                Registry.Load();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Storage] Could not open the vehicle registry: " + e.Message);
                Registry = new VehicleRegistry(Path.Combine(Application.temporaryCachePath, "vehicle_registry.json"));
            }
        }

        private IEnumerator Start()
        {
            // Wait for the vehicle list, then bring the registry in line with the cache. Retries a few times in case
            // Addressables isn't fully up yet; if it never works, Home simply keeps its old behaviour.
            for (int attempt = 0; attempt < 8 && !IsReady; attempt++)
            {
                float waited = 0f;
                while (waited < 120f)
                {
                    var loader = RemoteAddressableVehicleLoader.Instance;
                    if (loader != null && loader.IsCatalogLoaded) break;
                    yield return new WaitForSecondsRealtime(0.5f);
                    waited += 0.5f;
                }
                yield return ReconcileRoutine(true);
                if (!IsReady) yield return new WaitForSecondsRealtime(5f);
            }
            _gaveUp = true;       // never became ready: stop hiding the old list so Home isn't left empty
        }

        // ── Bringing the registry in line with the cache ─────────────────

        public IEnumerator ReconcileRoutine(bool announce)
        {
            while (_busy) yield return null;
            _busy = true;

            var loader = RemoteAddressableVehicleLoader.Instance;
            if (loader == null || !loader.IsCatalogLoaded) { _busy = false; yield break; }

            // Ask Addressables which files each vehicle needs (about a frame per vehicle)
            var catalog = new List<CatalogVehicle>();
            foreach (var v in loader.GetAvailableVehicles())
            {
                if (v == null || string.IsNullOrEmpty(v.vehicleId)) continue;
                var cv = new CatalogVehicle
                {
                    vehicleId = v.vehicleId, vehicleName = v.vehicleName, addressableKey = v.addressableKey,
                    version = v.version, existsInCatalog = false,
                };
                if (!string.IsNullOrEmpty(v.addressableKey))
                {
                    var handle = Addressables.LoadResourceLocationsAsync(v.addressableKey);
                    yield return handle;
                    if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0)
                    {
                        cv.existsInCatalog = true;
                        cv.bundles = AddressablesBundleResolver.CollectBundles(handle.Result);
                    }
                    Addressables.Release(handle);
                }
                catalog.Add(cv);
            }

            // If nothing could be resolved, Addressables isn't ready: don't conclude that nothing is downloaded
            if (catalog.Count == 0 || catalog.All(c => !c.existsInCatalog)) { _busy = false; yield break; }

            ReconcileResult result = null;
            try { result = StorageReconciler.Reconcile(Registry.Vehicles, catalog, ReadLegacyTracker(), AddressablesBundleResolver.IsCached); }
            catch (Exception e) { Debug.LogError("[Storage] Reconcile failed: " + e); }

            if (result == null) { _busy = false; yield break; }

            bool firstTime = !IsReady;
            _catalog.Clear();
            _catalog.AddRange(catalog);
            if (result.Changed || firstTime) Registry.ReplaceAll(result.records);
            IsReady = true;
            _busy = false;

            if (announce || result.Changed) RaiseChanged();
        }

        // ── Questions ────────────────────────────────────────────────────

        /// <summary>The state of one vehicle, or null if the service isn't ready or doesn't know the vehicle.</summary>
        public VehicleStorageState GetState(string vehicleId, string addressableKey = null)
        {
            if (!IsReady) return null;
            CatalogVehicle c = FindCatalog(vehicleId, addressableKey);
            if (c == null) return null;

            VehicleRecord rec = Registry.Get(c.vehicleId);
            var latest = new LatestVehicleInfo { version = c.version, existsInCatalog = c.existsInCatalog, bundles = c.bundles };
            VehicleStatusInfo info = VehicleStatusEvaluator.Evaluate(rec, latest, AddressablesBundleResolver.IsCached);

            long total = rec != null ? rec.TotalBytes() : SumDistinct(c.bundles);
            StorageLabel label = StorageLabels.For(info, total, CanKeepOldVersions);

            return new VehicleStorageState
            {
                vehicleId = c.vehicleId, vehicleName = c.vehicleName, addressableKey = c.addressableKey,
                status = info.status,
                IsDownloaded = rec != null && info.CanOpen,
                NeedsUpdate = info.status == VehicleStatus.UpdateAvailable || info.status == VehicleStatus.UpdateRequired,
                totalBytes = total, downloadBytes = info.downloadBytes,
                versionText = VersionText(rec, c),
                downloadedAtUtc = rec != null ? rec.DownloadedAtUtc : null,
                label = label.text, tone = label.tone,
            };
        }

        /// <summary>True when the vehicle's files are really on this PC. Used to keep Home's list honest.</summary>
        public bool IsActuallyDownloaded(string vehicleId, string addressableKey)
        {
            VehicleStorageState s = GetState(vehicleId, addressableKey);
            return s != null && s.IsDownloaded;
        }

        public List<StorageRow> GetRows()
        {
            var rows = new List<StorageRow>();
            if (!IsReady) return rows;
            foreach (VehicleRecord rec in Registry.Vehicles)
            {
                CatalogVehicle c = FindCatalog(rec.vehicleId, rec.addressableKey);
                VehicleStorageState st = c != null ? GetState(c.vehicleId, c.addressableKey) : null;
                rows.Add(new StorageRow
                {
                    vehicleId = rec.vehicleId,
                    name = !string.IsNullOrEmpty(rec.vehicleName) ? rec.vehicleName : rec.vehicleId,
                    bytes = rec.TotalBytes(),
                    versionText = VersionText(rec, c),
                    downloadedAtUtc = rec.DownloadedAtUtc,
                    statusText = st != null && st.NeedsUpdate ? st.label : (c == null ? "No longer on the server" : ""),
                    tone = st != null && st.NeedsUpdate ? st.tone : LabelTone.None,
                });
            }
            return rows;
        }

        public StorageSummary GetSummary()
        {
            var s = new StorageSummary();
            if (!IsReady) return s;
            s.downloadedCount = Registry.Count;
            s.downloadedBytes = StoragePlanner.TotalUniqueBytes(Registry.Vehicles);
            var seen = new HashSet<string>();
            foreach (CatalogVehicle c in _catalog)
            {
                if (!c.existsInCatalog || Registry.Get(c.vehicleId) != null) continue;
                s.notDownloadedCount++;
                foreach (BundleRef b in c.bundles)
                    if (b != null && !AddressablesBundleResolver.IsCached(b) && seen.Add(b.Key)) s.notDownloadedBytes += Math.Max(0, b.size);
            }
            try { s.cachePath = Caching.currentCacheForWriting.path; } catch (Exception) { }
            return s;
        }

        // ── Recording downloads ──────────────────────────────────────────

        /// <summary>Called by the old download list whenever a vehicle has just been downloaded. Safe to call at any time.</summary>
        public static void NotifyDownloaded(string vehicleId, string vehicleName, string addressableKey, string version)
        {
            var self = Instance;
            if (self == null || string.IsNullOrEmpty(vehicleId) || string.IsNullOrEmpty(addressableKey)) return;
            self.StartCoroutine(self.RecordDownloadedRoutine(vehicleId, vehicleName, addressableKey, version));
        }

        private IEnumerator RecordDownloadedRoutine(string vehicleId, string vehicleName, string key, string version)
        {
            while (_busy) yield return null;

            var handle = Addressables.LoadResourceLocationsAsync(key);
            yield return handle;
            List<BundleRef> bundles = null;
            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0)
                bundles = AddressablesBundleResolver.CollectBundles(handle.Result);
            Addressables.Release(handle);

            if (bundles == null || bundles.Count == 0) yield break;
            BundleRef main = StorageReconciler.MainFile(bundles);
            if (main == null || !AddressablesBundleResolver.IsCached(main)) yield break;   // not actually on disk

            // Use the catalog's own id, name and version when we know the vehicle. The old list's second "mark as
            // downloaded" method can pass a file path as the id and never had a version, which is how vehicles ended
            // up recorded twice or with no version at all.
            CatalogVehicle known = FindCatalog(vehicleId, key);
            if (known != null)
            {
                vehicleId = known.vehicleId;
                if (!string.IsNullOrEmpty(known.vehicleName)) vehicleName = known.vehicleName;
                if (string.IsNullOrEmpty(version)) version = known.version;
            }

            VehicleRecord rec = Registry.Get(vehicleId) ?? new VehicleRecord { vehicleId = vehicleId };
            bool filesChanged = rec.bundles == null || rec.bundles.Count == 0 || !VehicleStatusEvaluator.SameBundles(rec.bundles, bundles);
            rec.vehicleName = string.IsNullOrEmpty(vehicleName) ? vehicleId : vehicleName;
            rec.addressableKey = key;
            rec.installedVersion = version ?? "";
            rec.catalogId = "default";
            rec.bundles = bundles;
            rec.bundlesKnown = true;
            rec.needsIdReconcile = false;
            if (rec.downloadedAtUtcTicks <= 0 || filesChanged) rec.downloadedAtUtcTicks = DateTime.UtcNow.Ticks;
            Registry.Upsert(rec);

            // These files came from the current catalog, so they are also the newest ones
            CatalogVehicle c = _catalog.FirstOrDefault(x => string.Equals(x.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase));
            if (c != null) { c.existsInCatalog = true; c.bundles = bundles.Select(b => new BundleRef(b.name, b.hash, b.size)).ToList(); }

            RaiseChanged();
        }

        // ── Removing files ───────────────────────────────────────────────

        /// <summary>Remove one vehicle's files. Files another vehicle still uses are kept. Saved measurements are never touched.</summary>
        public IEnumerator RemoveVehicleRoutine(string vehicleId, Action<RemoveOutcome> done)
        {
            var outcome = new RemoveOutcome();

            float waited = 0f;
            while (!IsReady && waited < 20f) { yield return new WaitForSecondsRealtime(0.25f); waited += 0.25f; }
            if (!IsReady) { Finish(outcome, false, "The storage list isn't ready yet. Try again in a moment.", done); yield break; }

            VehicleRecord rec = Registry.Get(vehicleId);
            if (rec == null)
            {
                DownloadedVehiclesTracker.RemoveDownloaded(vehicleId);
                Finish(outcome, true, "This vehicle's files aren't on this PC.", done);
                yield break;
            }

            DeletionPlan plan = StoragePlanner.PlanRemoval(Registry.Vehicles, vehicleId);
            if (plan.blocked)
            {
                yield return ReconcileRoutine(false);            // scan anything unscanned, then plan again
                rec = Registry.Get(vehicleId);
                if (rec == null) { Finish(outcome, true, "This vehicle's files aren't on this PC.", done); yield break; }
                plan = StoragePlanner.PlanRemoval(Registry.Vehicles, vehicleId);
            }
            if (plan.blocked) { Finish(outcome, false, plan.blockedReason, done); yield break; }

            int failed = 0;
            long freed = 0;
            foreach (BundleRef b in plan.delete)
            {
                bool cleared = ClearBundle(b);
                if (!cleared && AddressablesBundleResolver.IsCached(b)) failed++;
                else freed += Math.Max(0, b.size);
                yield return null;
            }

            if (failed > 0)
            {
                yield return ReconcileRoutine(true);
                Finish(outcome, false, "Some files are in use and couldn't be removed. Go back to Home, then try again.", done);
                yield break;
            }

            string key = rec.addressableKey;
            Registry.Remove(vehicleId);
            DownloadedVehiclesTracker.RemoveDownloaded(vehicleId);
            if (!string.IsNullOrEmpty(key)) DownloadedVehiclesTracker.RemoveDownloaded(key);

            outcome.freedBytes = freed;
            outcome.vehicles = 1;
            string name = !string.IsNullOrEmpty(rec.vehicleName) ? rec.vehicleName : vehicleId;
            Finish(outcome, true, "Removed " + name + ". Freed " + ByteFormat.Format(freed) + ".", done);
            RaiseChanged();
        }

        /// <summary>Same as <see cref="RemoveVehicleRoutine"/>, for callers that know the addressable key but not necessarily the catalog id.</summary>
        public IEnumerator RemoveByKeyRoutine(string vehicleId, string addressableKey)
        {
            CatalogVehicle c = FindCatalog(vehicleId, addressableKey);
            string id = c != null ? c.vehicleId : vehicleId;
            if (string.IsNullOrEmpty(id)) yield break;
            yield return RemoveVehicleRoutine(id, null);
        }

        /// <summary>Remove every downloaded vehicle's files from this PC (including leftovers).</summary>
        public IEnumerator RemoveAllRoutine(Action<RemoveOutcome> done)
        {
            var outcome = new RemoveOutcome();
            int count = Registry != null ? Registry.Count : 0;
            long bytes = Registry != null ? StoragePlanner.TotalUniqueBytes(Registry.Vehicles) : 0;

            bool cleared = false;
            try { cleared = Caching.ClearCache(); }
            catch (Exception e) { Debug.LogWarning("[Storage] ClearCache failed: " + e.Message); }
            yield return null;

            if (!cleared)
            {
                yield return ReconcileRoutine(true);             // some files may have been removed: show the truth
                Finish(outcome, false, "Some files are in use and couldn't be removed. Go back to Home, then try again.", done);
                yield break;
            }

            Registry.Clear();
            DownloadedVehiclesTracker.ClearAll();
            outcome.vehicles = count;
            outcome.freedBytes = bytes;
            Finish(outcome, true, "Removed " + count + " vehicle(s). Freed " + ByteFormat.Format(bytes) + ".", done);
            RaiseChanged();
        }

        // ── helpers ──────────────────────────────────────────────────────

        private static bool ClearBundle(BundleRef b)
        {
            try
            {
                Hash128 hash = Hash128.Parse(b.hash);
                if (!hash.isValid) return false;
                return Caching.ClearCachedVersion(b.name, hash);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Storage] Could not remove " + b.name + ": " + e.Message);
                return false;
            }
        }

        private static void Finish(RemoveOutcome outcome, bool success, string message, Action<RemoveOutcome> done)
        {
            outcome.success = success;
            outcome.message = message ?? "";
            if (done != null) done(outcome);
        }

        private CatalogVehicle FindCatalog(string vehicleId, string addressableKey)
        {
            CatalogVehicle found = null;
            if (!string.IsNullOrEmpty(vehicleId))
                found = _catalog.FirstOrDefault(c => string.Equals(c.vehicleId, vehicleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found == null && !string.IsNullOrEmpty(addressableKey))
                found = _catalog.FirstOrDefault(c => string.Equals(c.addressableKey, addressableKey, StringComparison.OrdinalIgnoreCase));
            return found;
        }

        /// <summary>The catalog's version text if it has one; otherwise a short content id from the main file's hash.</summary>
        private static string VersionText(VehicleRecord rec, CatalogVehicle c)
        {
            if (rec != null && !string.IsNullOrEmpty(rec.installedVersion)) return rec.installedVersion;
            BundleRef main = StorageReconciler.MainFile(rec != null ? rec.bundles : (c != null ? c.bundles : null));
            if (main != null && !string.IsNullOrEmpty(main.hash)) return "id " + main.hash.Substring(0, Math.Min(8, main.hash.Length));
            return "";
        }

        private static long SumDistinct(IList<BundleRef> bundles)
        {
            long sum = 0;
            var seen = new HashSet<string>();
            if (bundles != null)
                foreach (var b in bundles)
                    if (b != null && seen.Add(b.Key)) sum += Math.Max(0, b.size);
            return sum;
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

        private static void RaiseChanged()
        {
            try { if (Changed != null) Changed(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
