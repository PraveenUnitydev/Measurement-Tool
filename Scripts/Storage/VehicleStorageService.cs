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

        /// <summary>How many downloaded files had their "last used" time refreshed at this start (see RefreshLastUsed).</summary>
        public int LastRefreshedFiles { get; private set; }

        /// <summary>
        /// True from startup until the first scan has finished (or has given up). While pending, the old download list
        /// can't be trusted, so it is hidden instead of flashing vehicles that may not be on this PC.
        /// </summary>
        public bool IsPending { get { return !IsReady && !_gaveUp; } }

        public VehicleRegistry Registry { get; private set; }

        private readonly List<CatalogVehicle> _catalog = new List<CatalogVehicle>();
        private bool _busy;
        private bool _gaveUp;
        private bool _refreshedThisSession;

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
                    // Synchronous lookup in the loaded catalog: the whole scan takes one frame instead of one frame per vehicle
                    List<BundleRef> found = AddressablesBundleResolver.CollectBundlesNow(v.addressableKey);
                    if (found == null)
                    {
                        var handle = Addressables.LoadResourceLocationsAsync(DasKeys.Real(v.addressableKey));
                        yield return handle;
                        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0)
                            found = AddressablesBundleResolver.CollectBundles(handle.Result);
                        Addressables.Release(handle);
                    }
                    if (found != null && found.Count > 0) { cv.existsInCatalog = true; cv.bundles = found; }
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

            if (!_refreshedThisSession) { _refreshedThisSession = true; RefreshLastUsed(); }
            ComputeOlderCopies();
            if (announce || result.Changed) RaiseChanged();
        }

        /// <summary>
        /// Unity deletes cached files that haven't been used for 150 days, and that limit can't be raised. Marking every
        /// downloaded vehicle's files as just used at each start means they only expire if the app isn't started at all
        /// for 150 days.
        /// </summary>
        private void RefreshLastUsed()
        {
            int count = 0;
            try
            {
                var seen = new HashSet<string>();
                foreach (VehicleRecord rec in Registry.Vehicles)
                    foreach (BundleRef b in rec.bundles)
                        if (b != null && seen.Add(b.Key) && AddressablesBundleResolver.MarkUsed(b)) count++;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Storage] Could not refresh the cache's last-used times: " + e.Message);
            }
            LastRefreshedFiles = count;
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

            // Only an older version is on disk: it can't be opened, but the vehicle was downloaded before, so Home keeps
            // it and says what opening it will cost.
            if (rec == null && _olderCopyBytes.ContainsKey(c.vehicleId))
            {
                long missing = 0;
                var counted = new HashSet<string>();
                if (c.bundles != null)
                    foreach (BundleRef b in c.bundles)
                        if (b != null && counted.Add(b.Key) && !AddressablesBundleResolver.IsCached(b)) missing += Math.Max(0, b.size);
                return new VehicleStorageState
                {
                    vehicleId = c.vehicleId, vehicleName = c.vehicleName, addressableKey = c.addressableKey,
                    status = VehicleStatus.UpdateRequired, IsDownloaded = false, NeedsUpdate = true,
                    totalBytes = total, downloadBytes = missing, versionText = VersionText(null, c),
                    label = "Update needed: opening it downloads " + ByteFormat.Format(missing), tone = LabelTone.Warn,
                };
            }
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

            List<BundleRef> bundles = AddressablesBundleResolver.CollectBundlesNow(key);
            if (bundles == null)
            {
                var handle = Addressables.LoadResourceLocationsAsync(DasKeys.Real(key));
                yield return handle;
                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0)
                    bundles = AddressablesBundleResolver.CollectBundles(handle.Result);
                Addressables.Release(handle);
            }

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

            // The current version is on disk now. Older versions of the same files can never be opened again: free them.
            foreach (BundleRef b in bundles) AddressablesBundleResolver.ClearOlderVersions(b);
            _olderCopyBytes.Remove(vehicleId);

            RaiseChanged();
        }

        // ── Removing files ───────────────────────────────────────────────

        /// <summary>Remove one vehicle's files. Files another vehicle still uses are kept. Saved measurements are never touched.</summary>
        public IEnumerator RemoveVehicleRoutine(string vehicleId, Action<RemoveOutcome> done)
        {
            yield return RemoveCore(vehicleId, null, done);
        }

        /// <summary>
        /// Removal. Works without the server catalog too (offline): the vehicle's files are then taken from the last
        /// scan (registry) or from the Addressables catalog already loaded, found by <paramref name="keyHint"/>.
        /// </summary>
        private IEnumerator RemoveCore(string vehicleId, string keyHint, Action<RemoveOutcome> done)
        {
            var outcome = new RemoveOutcome();

            VehicleRecord early = Registry != null ? (Registry.Get(vehicleId) ?? FindRecordByKey(keyHint)) : null;
            if (early != null) vehicleId = early.vehicleId;
            // Wait briefly for the scan; without the server catalog it never finishes, and the last scan is enough
            float waited = 0f, limit = early != null || !string.IsNullOrEmpty(keyHint) ? 3f : 20f;
            while (!IsReady && waited < limit) { yield return new WaitForSecondsRealtime(0.25f); waited += 0.25f; }
            List<BundleRef> fromCatalog = !IsReady && early == null ? AddressablesBundleResolver.CollectBundlesNow(keyHint) : null;
            if (!IsReady && early == null && (fromCatalog == null || fromCatalog.Count == 0))
            { Finish(outcome, false, "The storage list isn't ready yet. Try again in a moment.", done); yield break; }

            VehicleRecord rec = Registry.Get(vehicleId);
            if (rec != null)
            {
                // the planner refuses while some vehicle's files are unknown: scan, then ask again
                DeletionPlan plan = StoragePlanner.PlanRemoval(Registry.Vehicles, vehicleId);
                if (plan.blocked)
                {
                    yield return ReconcileRoutine(false);
                    rec = Registry.Get(vehicleId);
                    if (rec != null) plan = StoragePlanner.PlanRemoval(Registry.Vehicles, vehicleId);
                }
                if (rec != null && plan.blocked) { Finish(outcome, false, plan.blockedReason, done); yield break; }
            }

            CatalogVehicle cat = FindCatalog(vehicleId, rec != null ? rec.addressableKey : keyHint);
            string key = rec != null ? rec.addressableKey : (cat != null ? cat.addressableKey : keyHint);
            string name = rec != null && !string.IsNullOrEmpty(rec.vehicleName) ? rec.vehicleName
                        : (cat != null && !string.IsNullOrEmpty(cat.vehicleName) ? cat.vehicleName : vehicleId);
            List<BundleRef> bundles = rec != null && rec.bundles != null && rec.bundles.Count > 0 ? rec.bundles
                                    : (cat != null && cat.bundles != null ? cat.bundles
                                    : (fromCatalog ?? AddressablesBundleResolver.CollectBundlesNow(key) ?? new List<BundleRef>()));

            // Unity refuses to delete files that are loaded: ask the loaders to release this vehicle first
            RaiseReleaseRequested(vehicleId, key);
            yield return null;

            // Files another downloaded vehicle still uses stay (only their older versions go); everything else of this
            // vehicle goes, every version, so older copies are freed too.
            var usedByOthers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VehicleRecord other in Registry.Vehicles)
                if (other != null && other.bundles != null && !string.Equals(other.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))
                    foreach (BundleRef b in other.bundles) if (b != null && !string.IsNullOrEmpty(b.name)) usedByOthers.Add(b.name);

            var distinct = new List<BundleRef>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BundleRef b in bundles) if (b != null && !string.IsNullOrEmpty(b.name) && names.Add(b.name)) distinct.Add(b);

            long before = 0, estimated = 0;
            foreach (BundleRef b in distinct)
            {
                before += AddressablesBundleResolver.BytesOnDisk(b.name);
                if (!usedByOthers.Contains(b.name) && AddressablesBundleResolver.IsCached(b)) estimated += Math.Max(0, b.size);
            }

            bool olderOnly = _olderCopyBytes.ContainsKey(vehicleId) || (cat != null && _olderCopyBytes.ContainsKey(cat.vehicleId));
            if (rec == null && before == 0 && estimated == 0 && !olderOnly)
            {
                // nothing of this vehicle is on disk: just make sure no list still claims it
                DownloadedVehiclesTracker.RemoveDownloaded(vehicleId);
                if (!string.IsNullOrEmpty(key)) DownloadedVehiclesTracker.RemoveDownloaded(key);
                Finish(outcome, true, "This vehicle's files aren't on this PC.", done);
                yield break;
            }

            int failed = 0;
            foreach (BundleRef b in distinct)
            {
                if (usedByOthers.Contains(b.name)) AddressablesBundleResolver.ClearOlderVersions(b);
                else
                {
                    // Addressables can take a moment to close a released vehicle's files: retry briefly before giving up
                    bool cleared = AddressablesBundleResolver.ClearAllVersions(b.name);
                    for (int attempt = 0; !cleared && attempt < 8; attempt++)
                    {
                        yield return new WaitForSecondsRealtime(0.25f);
                        cleared = AddressablesBundleResolver.ClearAllVersions(b.name);
                    }
                    if (!cleared && (AddressablesBundleResolver.IsCached(b) || AddressablesBundleResolver.BytesOnDisk(b.name) > 0)) failed++;
                }
                yield return null;
            }

            long after = 0;
            foreach (BundleRef b in distinct) after += AddressablesBundleResolver.BytesOnDisk(b.name);
            long freed = before > 0 ? Math.Max(0, before - after) : estimated;

            if (failed > 0)
            {
                yield return ReconcileRoutine(true);
                Finish(outcome, false, "Some of " + name + "'s files are in use and couldn't be removed. Close the vehicle, go back to Home, then try again.", done);
                yield break;
            }

            if (rec != null) Registry.Remove(vehicleId);
            _olderCopyBytes.Remove(vehicleId);
            if (cat != null) _olderCopyBytes.Remove(cat.vehicleId);
            DownloadedVehiclesTracker.RemoveDownloaded(vehicleId);
            if (cat != null) DownloadedVehiclesTracker.RemoveDownloaded(cat.vehicleId);
            if (!string.IsNullOrEmpty(key)) DownloadedVehiclesTracker.RemoveDownloaded(key);
            ComputeOlderCopies();

            outcome.freedBytes = freed;
            outcome.vehicles = 1;
            Finish(outcome, true, freed > 0 ? "Removed " + name + ". Freed " + ByteFormat.Format(freed) + "." : "Removed " + name + " from this PC.", done);
            RaiseChanged();
        }

        /// <summary>Same as <see cref="RemoveVehicleRoutine"/>, for callers that know the addressable key but not necessarily the catalog id.</summary>
        public IEnumerator RemoveByKeyRoutine(string vehicleId, string addressableKey)
        {
            yield return RemoveByKeyRoutine(vehicleId, addressableKey, null);
        }

        /// <summary>As above, reporting the outcome (so the caller can tell the user if files couldn't be removed).</summary>
        public IEnumerator RemoveByKeyRoutine(string vehicleId, string addressableKey, Action<RemoveOutcome> done)
        {
            // The caller's id may not be the catalog id (a new measurement gets a generated one): the key decides
            CatalogVehicle c = FindCatalog(vehicleId, addressableKey);
            VehicleRecord r = c == null ? FindRecordByKey(addressableKey) : null;
            string id = c != null ? c.vehicleId : (r != null ? r.vehicleId : vehicleId);
            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(addressableKey)) { if (done != null) done(new RemoveOutcome { success = true, message = "Nothing to remove." }); yield break; }
            yield return RemoveCore(string.IsNullOrEmpty(id) ? addressableKey : id, addressableKey, done);
        }

        /// <summary>Remove every downloaded vehicle's files from this PC (including leftovers).</summary>
        public IEnumerator RemoveAllRoutine(Action<RemoveOutcome> done)
        {
            var outcome = new RemoveOutcome();
            int count = Registry != null ? Registry.Count : 0;
            long bytes = Registry != null ? StoragePlanner.TotalUniqueBytes(Registry.Vehicles) : 0;

            RaiseReleaseRequested(null, null);           // loaded files can't be deleted: release everything first
            yield return null;

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
            _olderCopyBytes.Clear();
            OlderCopiesBytes = 0;
            outcome.vehicles = count;
            outcome.freedBytes = bytes;
            Finish(outcome, true, "Removed " + count + " vehicle(s). Freed " + ByteFormat.Format(bytes) + ".", done);
            RaiseChanged();
        }

        /// <summary>
        /// Delete every older version of the vehicles' files. The app can't open an older version (the catalog asks for
        /// the current one), so they only take up space. Current files are never touched. Vehicles that had ONLY an older
        /// version are then no longer on this PC.
        /// </summary>
        public IEnumerator RemoveOlderCopiesRoutine(Action<RemoveOutcome> done)
        {
            var outcome = new RemoveOutcome();
            if (!IsReady) { Finish(outcome, false, "The storage list isn't ready yet. Try again in a moment.", done); yield break; }

            RaiseReleaseRequested(null, null);
            yield return null;

            long before = OlderCopiesBytes;
            int vehicles = _olderCopyBytes.Count;
            int failed = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogVehicle c in _catalog.ToList())
            {
                if (c.bundles == null) continue;
                foreach (BundleRef b in c.bundles)
                {
                    if (b == null || string.IsNullOrEmpty(b.name) || !seen.Add(b.name)) continue;
                    if (AddressablesBundleResolver.HasOlderVersions(b) && !AddressablesBundleResolver.ClearOlderVersions(b)) failed++;
                }
                yield return null;
            }

            foreach (string id in _olderCopyBytes.Keys.ToList())
            {
                DownloadedVehiclesTracker.RemoveDownloaded(id);
                CatalogVehicle c = FindCatalog(id, null);
                if (c != null && !string.IsNullOrEmpty(c.addressableKey)) DownloadedVehiclesTracker.RemoveDownloaded(c.addressableKey);
            }
            ComputeOlderCopies();
            long freed = Math.Max(0, before - OlderCopiesBytes);
            outcome.freedBytes = freed;
            outcome.vehicles = vehicles;
            Finish(outcome, failed == 0,
                   failed == 0 ? "Removed older versions. Freed " + ByteFormat.Format(freed) + "."
                               : "Some older files are in use and were kept. Freed " + ByteFormat.Format(freed) + ".", done);
            RaiseChanged();
        }

        // ── Older copies (computed after each scan) ─────────────────────
        // Vehicles whose files on disk are only an OLDER version than the catalog's. Home keeps them as "Update needed".
        private readonly Dictionary<string, long> _olderCopyBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Space taken by older versions of the vehicles' files (all vehicles together).</summary>
        public long OlderCopiesBytes { get; private set; }

        private void ComputeOlderCopies()
        {
            _olderCopyBytes.Clear();
            long total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (CatalogVehicle c in _catalog)
                {
                    if (!c.existsInCatalog || c.bundles == null) continue;
                    foreach (BundleRef b in c.bundles)
                        if (b != null && !string.IsNullOrEmpty(b.name) && seen.Add(b.name)) total += AddressablesBundleResolver.BytesOnDisk(b.name, b.hash);
                    if (Registry.Get(c.vehicleId) != null) continue;
                    BundleRef main = StorageReconciler.MainFile(c.bundles);
                    if (main != null && AddressablesBundleResolver.HasOlderVersions(main))
                        _olderCopyBytes[c.vehicleId] = AddressablesBundleResolver.BytesOnDisk(main.name, main.hash);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Storage] Could not check for older copies: " + e.Message); }
            OlderCopiesBytes = total;
        }

        /// <summary>One vehicle for Home's list.</summary>
        public class HomeVehicle { public string vehicleId, vehicleName, addressableKey; public bool needsUpdate; }

        /// <summary>
        /// The vehicles Home lists: everything whose files are on this PC, plus vehicles that have only an older version
        /// ("Update needed"). Before the first scan of this session it returns the result of the last scan (saved on
        /// disk), so Home can show its list immediately instead of waiting for the server catalog.
        /// </summary>
        public List<HomeVehicle> GetHomeVehicles()
        {
            var list = new List<HomeVehicle>();
            if (Registry == null) return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (VehicleRecord rec in Registry.Vehicles)
            {
                if (rec == null || string.IsNullOrEmpty(rec.vehicleId) || !seen.Add(rec.vehicleId)) continue;
                list.Add(new HomeVehicle
                {
                    vehicleId = rec.vehicleId,
                    vehicleName = !string.IsNullOrEmpty(rec.vehicleName) ? rec.vehicleName : rec.vehicleId,
                    addressableKey = rec.addressableKey,
                });
            }
            if (IsReady)
                foreach (CatalogVehicle c in _catalog)
                    if (_olderCopyBytes.ContainsKey(c.vehicleId) && seen.Add(c.vehicleId))
                        list.Add(new HomeVehicle
                        {
                            vehicleId = c.vehicleId,
                            vehicleName = !string.IsNullOrEmpty(c.vehicleName) ? c.vehicleName : c.vehicleId,
                            addressableKey = c.addressableKey,
                            needsUpdate = true,
                        });
            return list;
        }

        /// <summary>Raised before files are deleted: loaders release this vehicle (both null = every vehicle).</summary>
        public static event Action<string, string> ReleaseRequested;

        private static void RaiseReleaseRequested(string vehicleId, string addressableKey)
        {
            var handler = ReleaseRequested;
            if (handler == null) return;
            try { handler(vehicleId, addressableKey); }
            catch (Exception e) { Debug.LogWarning("[Storage] A loader failed to release files: " + e.Message); }
        }

        // ── helpers ──────────────────────────────────────────────────────

        private VehicleRecord FindRecordByKey(string addressableKey)
        {
            if (Registry == null || string.IsNullOrEmpty(addressableKey)) return null;
            foreach (VehicleRecord r in Registry.Vehicles)
                if (r != null && string.Equals(r.addressableKey, addressableKey, StringComparison.OrdinalIgnoreCase)) return r;
            return null;
        }

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
