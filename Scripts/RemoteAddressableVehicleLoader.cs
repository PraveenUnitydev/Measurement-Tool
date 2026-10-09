using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace VehicleMeasurement
{
    /// <summary>
    /// REMOTE ADDRESSABLE VEHICLE LOADER
    /// 
    /// Enhanced version that supports:
    /// - Remote catalog from server (vehicle list loaded at runtime)
    /// - Remote asset bundles (3D models downloaded on-demand)
    /// - Catalog updates (check for new vehicles without app update)
    /// - Offline fallback (use cached data when offline)
    /// 
    /// SERVER SETUP:
    /// 1. Host catalog.json on your server
    /// 2. Host Addressable bundles in same location
    /// 3. Configure RemoteLoadPath in Addressables Profile
    /// 
    /// CATALOG FORMAT (catalog.json):
    /// {
    ///   "version": "1.0.0",
    ///   "lastUpdated": "2024-01-15",
    ///   "vehicles": [
    ///     {
    ///       "vehicleId": "sonet_2024",
    ///       "vehicleName": "Kia Sonet 2024",
    ///       "addressableKey": "Sonet",
    ///       "thumbnailUrl": "https://server.com/thumbnails/sonet.png",
    ///       "category": "SUV",
    ///       "manufacturer": "Kia",
    ///       "approximateSize": "1.2 GB",
    ///       "description": "Compact SUV"
    ///     }
    ///   ]
    /// }
    /// </summary>
    public class RemoteAddressableVehicleLoader : MonoBehaviour
    {
        public static RemoteAddressableVehicleLoader Instance { get; private set; }

        [Header("═══ SERVER SETTINGS ═══")]
        [Tooltip("URL to the vehicle catalog JSON")]
        public string catalogUrl = DasServer.ApiBase + "/catalog";

        [Tooltip("Base URL for thumbnail images")]
        public string thumbnailBaseUrl = DasServer.ApiBase + "/thumbnails/";

        [Tooltip("Check for catalog updates on start")]
        public bool checkUpdatesOnStart = true;

        [Tooltip("Cache catalog locally for offline use")]
        public bool enableOfflineCache = true;

        [Header("═══ LOCAL FALLBACK ═══")]
        [Tooltip("Fallback catalog if server is unreachable (assign in inspector)")]
        public List<VehicleAddressableInfo> fallbackCatalog = new List<VehicleAddressableInfo>();

        [Header("═══ CONTAINER ═══")]
        [Tooltip("Parent transform for instantiated vehicles")]
        public Transform vehicleContainer;

        [Tooltip("Auto-unload previous vehicle when loading new one")]
        public bool autoUnloadPrevious = true;

        [Header("═══ EVENTS ═══")]
        public UnityEvent OnCatalogLoading;
        public UnityEvent<int> OnCatalogLoaded;              // vehicleCount
        public UnityEvent<string> OnCatalogError;            // error message
        public UnityEvent<string> OnDownloadStarted;         // vehicleName
        public UnityEvent<float, long, long> OnDownloadProgress;  // progress, downloaded, total
        public UnityEvent<string> OnDownloadCompleted;       // vehicleName
        public UnityEvent<string, string> OnDownloadFailed;  // vehicleName, error
        public UnityEvent<GameObject> OnVehicleLoaded;
        public UnityEvent OnVehicleUnloaded;

        // Catalog data
        private List<RemoteVehicleInfo> _remoteCatalog = new List<RemoteVehicleInfo>();
        private Dictionary<string, Sprite> _thumbnailCache = new Dictionary<string, Sprite>();
        private string _catalogVersion = "";
        private bool _catalogLoaded = false;

        // Current state
        private GameObject _currentVehicle;
        private AsyncOperationHandle<GameObject> _currentHandle;
        private string _currentVehicleId;
        private bool _isLoading;

        // Download tracking
        private float _lastProgressTime;
        private long _lastDownloadedBytes;
        private float _currentSpeed;

        #region Singleton

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // A scene may still hold a retired or placeholder address: use the real one
            catalogUrl = DasServer.ResolveEndpoint(catalogUrl, "/catalog", "RemoteAddressableVehicleLoader.catalogUrl");
            thumbnailBaseUrl = DasServer.ResolveEndpoint(thumbnailBaseUrl, "/thumbnails/", "RemoteAddressableVehicleLoader.thumbnailBaseUrl");
            bundlesBaseUrl = DasServer.ResolveEndpoint(bundlesBaseUrl, "/bundles", "RemoteAddressableVehicleLoader.bundlesBaseUrl");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Initialization

        private void Start()
        {
            if (checkUpdatesOnStart)
            {
                StartCoroutine(InitializeCatalog());
            }
        }
        private void Update()
        {
            // Was: plain R - which fired while typing an R in the search box. Now Ctrl+Shift+R, and not while a text field has focus.
            if (Input.GetKeyDown(KeyCode.R) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && !IsTypingInTextField())
                ForceRefreshAddressablesCatalogs();
        }

        private static bool IsTypingInTextField()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            return go != null && (go.GetComponent<TMPro.TMP_InputField>() != null || go.GetComponent<UnityEngine.UI.InputField>() != null);
        }

        public void ForceRefreshAddressablesCatalogs()
        {
            StartCoroutine(ForceRefreshCatalogsCoroutine());
        }

        private IEnumerator ForceRefreshCatalogsCoroutine()
        {
            var init = Addressables.InitializeAsync();
            yield return init;

            var check = Addressables.CheckForCatalogUpdates(false);
            yield return check;
            var list = check.Result;
            Addressables.Release(check);

            if (list != null && list.Count > 0)
            {
                var update = Addressables.UpdateCatalogs(list);
                yield return update;
                Addressables.Release(update);
                Debug.Log("[QA] Catalogs refreshed.");
            }
            else
            {
                Debug.Log("[QA] No catalog updates available.");
            }
            RefreshThumbnails();
        }

        /// <summary>
        /// Initialize catalog from server or cache
        /// </summary>
        public IEnumerator InitializeCatalog()
        {
            IsCatalogFailed = false;
            OnCatalogLoading?.Invoke();


            var initHandle = Addressables.InitializeAsync();
            yield return initHandle;

            yield return StartCoroutine(UpdateUnityAddressablesCatalogs());
            // Try to load from server first
            bool serverSuccess = false;
            yield return StartCoroutine(LoadCatalogFromServer((success) => serverSuccess = success));

            if (!serverSuccess)
            {
                Debug.LogWarning("[RemoteLoader] Server unreachable, trying cached catalog...");

                // Try cached catalog
                if (enableOfflineCache && LoadCatalogFromCache())
                {
                    Debug.Log("[RemoteLoader] Loaded catalog from cache");
                }
                else if (fallbackCatalog.Count > 0)
                {
                    // Use inspector fallback
                    Debug.Log("[RemoteLoader] Using fallback catalog");
                    ConvertFallbackCatalog();
                }
                else
                {
                    IsCatalogFailed = true;
                    OnCatalogError?.Invoke("Failed to load vehicle catalog");
                    yield break;
                }
            }

            // Vehicles published from Unity have their own small Addressables catalog: load those before anyone
            // asks for the vehicle (download sizes, storage state, opening)
            yield return LoadVehicleContentCatalogs();

            _catalogLoaded = true;
            OnCatalogLoaded?.Invoke(_remoteCatalog.Count);

            Debug.Log($"[RemoteLoader] Catalog loaded: {_remoteCatalog.Count} vehicles (v{_catalogVersion})");

            // Thumbnails: disk copies first, then checked with the server (see RefreshThumbnail)
            _thumbnailFailed.Clear();
            StartCoroutine(PreloadThumbnails());
        }

        /// <summary>
        /// Force refresh catalog from server
        /// </summary>
        public void RefreshCatalog()
        {
            StartCoroutine(InitializeCatalog());
        }


        private IEnumerator UpdateUnityAddressablesCatalogs()
        {
            // Ask Addressables service if there are new catalogs available
            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            yield return checkHandle;

            var catalogsNeedingUpdate = checkHandle.Result; // IList<string>
            Addressables.Release(checkHandle);

            if (catalogsNeedingUpdate != null && catalogsNeedingUpdate.Count > 0)
            {
                Debug.Log($"[RemoteLoader] Updating Addressables catalogs: {string.Join(", ", catalogsNeedingUpdate)}");
                var updateHandle = Addressables.UpdateCatalogs(catalogsNeedingUpdate);
                yield return updateHandle;

                if (updateHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogWarning("[RemoteLoader] Catalog update failed (will continue with existing catalog).");
                }
                else
                {
                    Debug.Log("[RemoteLoader] Addressables catalogs updated.");
                }
                Addressables.Release(updateHandle);
            }
            else
            {
                Debug.Log("[RemoteLoader] Addressables catalogs already up to date.");
            }
        }


        #endregion

        #region Catalog Loading

        // ── Per-vehicle catalogs (vehicles published by engineers from Unity) ──
        // Each published vehicle is built on its own, with its own Addressables catalog in its own version folder on the
        // server (catalog.json: contentCatalogPath). Loading those catalogs here makes their keys ("das/<vehicleId>")
        // known to Addressables, so downloading, storage state and opening work exactly like the original vehicles.
        // The app no longer has to be built on the PC that built the vehicles.
        [Tooltip("Base URL the per-vehicle catalogs and files are served from")]
        public string bundlesBaseUrl = DasServer.ApiBase + "/bundles";
        private readonly HashSet<string> _contentCatalogsLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _contentCatalogsFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int ContentCatalogParallel = 6;

        /// <summary>True when this vehicle's own catalog couldn't be loaded (it can't be downloaded or opened right now).</summary>
        public bool IsVehicleContentUnavailable(string vehicleIdOrKey)
        {
            var v = GetVehicleInfo(vehicleIdOrKey);
            return v != null && !string.IsNullOrEmpty(v.contentCatalogPath) && _contentCatalogsFailed.Contains(v.contentCatalogPath);
        }

        public string ContentCatalogUrl(RemoteVehicleInfo v)
        {
            if (v == null || string.IsNullOrEmpty(v.contentCatalogPath)) return null;
            if (v.contentCatalogPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return DasServer.RewriteContentUrl(v.contentCatalogPath);
            return DasServer.RewriteContentUrl(bundlesBaseUrl.TrimEnd('/') + "/" + v.contentCatalogPath.TrimStart('/'));
        }

        private IEnumerator LoadVehicleContentCatalogs()
        {
            var todo = new List<RemoteVehicleInfo>();
            foreach (var v in _remoteCatalog)
                if (v != null && !string.IsNullOrEmpty(v.contentCatalogPath) && !_contentCatalogsLoaded.Contains(v.contentCatalogPath))
                    todo.Add(v);
            if (todo.Count == 0) yield break;

            int running = 0, ok = 0;
            foreach (var v in todo)
            {
                while (running >= ContentCatalogParallel) yield return null;
                running++;
                StartCoroutine(RunThen(LoadOneContentCatalog(v, success => { if (success) ok++; }), () => running--));
            }
            while (running > 0) yield return null;
            Debug.Log("[RemoteLoader] Vehicle catalogs loaded: " + ok + " of " + todo.Count + ".");
        }

        private IEnumerator LoadOneContentCatalog(RemoteVehicleInfo v, Action<bool> done)
        {
            string url = ContentCatalogUrl(v);
            AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> h;
            try { h = Addressables.LoadContentCatalogAsync(url, false); }
            catch (Exception e)
            {
                _contentCatalogsFailed.Add(v.contentCatalogPath);
                Debug.LogWarning("[RemoteLoader] Could not load the catalog of " + v.vehicleName + ": " + e.Message);
                done(false);
                yield break;
            }
            yield return h;
            bool success = h.Status == AsyncOperationStatus.Succeeded;
            if (success) { _contentCatalogsLoaded.Add(v.contentCatalogPath); _contentCatalogsFailed.Remove(v.contentCatalogPath); }
            else
            {
                _contentCatalogsFailed.Add(v.contentCatalogPath);
                Debug.LogWarning("[RemoteLoader] Could not load the catalog of " + v.vehicleName + " (" + DasServer.ForLog(url) + "): "
                                 + (h.OperationException != null ? h.OperationException.Message : "unknown error") + ". It can't be downloaded until this works.");
            }
            Addressables.Release(h);
            done(success);
        }

        private IEnumerator LoadCatalogFromServer(Action<bool> onComplete)
        {
            Debug.Log($"[RemoteLoader] Loading catalog from: {catalogUrl}");

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = UnityWebRequest.Get(catalogUrl);
                    __req.timeout = 10; // 10 second timeout
                    return __req;
                }, r => request = r);
                using (request)
                {

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            string json = request.downloadHandler.text;
                            ParseCatalogJson(json);

                            // Cache for offline use
                            if (enableOfflineCache)
                            {
                                SaveCatalogToCache(json);
                            }

                            onComplete?.Invoke(true);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"[RemoteLoader] Failed to parse catalog: {e.Message}");
                            onComplete?.Invoke(false);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[RemoteLoader] Server request failed: {request.error}");
                        onComplete?.Invoke(false);
                    }
                }
            }
        }

        private void ParseCatalogJson(string json)
        {
            var catalog = JsonUtility.FromJson<RemoteCatalogData>(json);

            _catalogVersion = catalog.version;
            _remoteCatalog.Clear();

            if (catalog.vehicles != null)
            {
                _remoteCatalog.AddRange(catalog.vehicles);
            }
        }

        private void SaveCatalogToCache(string json)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "vehicle_catalog_cache.json");
                System.IO.File.WriteAllText(path, json);
                Debug.Log($"[RemoteLoader] Catalog cached to: {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteLoader] Failed to cache catalog: {e.Message}");
            }
        }

        private bool LoadCatalogFromCache()
        {
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "vehicle_catalog_cache.json");

                if (!System.IO.File.Exists(path))
                    return false;

                string json = System.IO.File.ReadAllText(path);
                ParseCatalogJson(json);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RemoteLoader] Failed to load cached catalog: {e.Message}");
                return false;
            }
        }

        private void ConvertFallbackCatalog()
        {
            _remoteCatalog.Clear();
            _catalogVersion = "fallback";

            foreach (var info in fallbackCatalog)
            {
                _remoteCatalog.Add(new RemoteVehicleInfo
                {
                    vehicleId = info.vehicleId,
                    vehicleName = info.vehicleName,
                    addressableKey = info.addressableKey,
                    thumbnailUrl = "", // Use local sprite
                    category = info.category,
                    manufacturer = info.manufacturer,
                    approximateSize = info.approximateSize,
                    description = info.description
                });

                // Cache the local thumbnail
                if (info.thumbnail != null)
                {
                    _thumbnailCache[info.vehicleId] = info.thumbnail;
                }
            }
        }

        #endregion

        #region Thumbnail Loading

        // ── Thumbnails ────────────────────────────────────────────────────
        // The server is the only source. Each picture is kept on disk (<persistentDataPath>/DAS/thumbnails) with what the
        // server said about it, shown from disk at once, and checked against the server once per session (see
        // VehicleThumbnailStore): a picture replaced on the server shows up on the next start without a version change.
        // Screens listen to ThumbnailUpdated, so a picture that arrives (or changes) after a screen was built still appears.
        private static string ThumbnailFolder => System.IO.Path.Combine(Application.persistentDataPath, "DAS", "thumbnails");

        private VehicleThumbnailStore _thumbStore;
        private VehicleThumbnailStore ThumbStore
        {
            get
            {
                if (_thumbStore == null)
                {
                    _thumbStore = new VehicleThumbnailStore(ThumbnailFolder);
                    try
                    {
                        int removed = _thumbStore.RemoveOldScheme();
                        if (removed > 0) Debug.Log("[RemoteLoader] Removed " + removed + " thumbnail copies from the old cache (they were never refreshed).");
                    }
                    catch (Exception e) { Debug.LogWarning("[RemoteLoader] Could not tidy the thumbnail folder: " + e.Message); }
                }
                return _thumbStore;
            }
        }

        private readonly HashSet<string> _thumbnailChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _thumbnailFailed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _thumbnailsRunning;
        private bool _thumbnailsAgain;
        private const int ThumbnailParallel = 4;

        /// <summary>A vehicle's thumbnail was loaded or changed (vehicleId, new picture). Screens update their cards.</summary>
        public event Action<string, Sprite> ThumbnailUpdated;

        /// <summary>Thumbnails done (cached or failed) out of those the catalog lists, for progress displays.</summary>
        public int ThumbnailsDone { get; private set; }
        public int ThumbnailsTotal { get; private set; }

        /// <summary>The thumbnail file on this PC for a vehicle (latest from the server), or null.</summary>
        public string GetThumbnailFile(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            var e = ThumbStore.Get(vehicleId);
            return e != null ? e.imageFile : null;
        }

        /// <summary>Check every thumbnail against the server again (e.g. after uploading new pictures).</summary>
        public void RefreshThumbnails()
        {
            _thumbnailChecked.Clear();
            _thumbnailFailed.Clear();
            StartCoroutine(PreloadThumbnails());
        }

        private IEnumerator PreloadThumbnails()
        {
            if (_thumbnailsRunning) { _thumbnailsAgain = true; yield break; }
            _thumbnailsRunning = true;
            try
            {
                do
                {
                    _thumbnailsAgain = false;
                    var todo = new List<RemoteVehicleInfo>();
                    foreach (var vehicle in _remoteCatalog)
                        if (vehicle != null && !string.IsNullOrEmpty(vehicle.vehicleId)) todo.Add(vehicle);
                    ThumbnailsTotal = todo.Count;
                    ThumbnailsDone = 0;

                    // 1) pictures already on this PC: on screen at once
                    foreach (var vehicle in todo)
                        if (!_thumbnailCache.ContainsKey(vehicle.vehicleId))
                            yield return ShowFromDisk(vehicle.vehicleId);

                    // 2) check with the server, a few at a time
                    int running = 0;
                    foreach (var vehicle in todo)
                    {
                        while (running >= ThumbnailParallel) yield return null;
                        running++;
                        StartCoroutine(RunThen(RefreshThumbnail(vehicle), () => { running--; ThumbnailsDone++; }));
                    }
                    while (running > 0) yield return null;
                }
                while (_thumbnailsAgain);
            }
            finally { _thumbnailsRunning = false; }
        }

        private static IEnumerator RunThen(IEnumerator routine, Action then)
        {
            try { yield return routine; }
            finally { then(); }
        }

        private IEnumerator ShowFromDisk(string vehicleId)
        {
            var entry = ThumbStore.Get(vehicleId);
            if (entry == null) yield break;
            string file = entry.imageFile;
            var read = System.Threading.Tasks.Task.Run(() => System.IO.File.ReadAllBytes(file));
            while (!read.IsCompleted) yield return null;
            if (read.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || !SetThumbnail(vehicleId, read.Result))
            {
                ThumbStore.Delete(vehicleId);                                     // unreadable copy: fetch it again
                yield break;
            }
            yield return null;                                                    // one decode per frame
        }

        private IEnumerator RefreshThumbnail(RemoteVehicleInfo vehicle)
        {
            string id = vehicle.vehicleId;
            var entry = ThumbStore.Get(id);
            var step = VehicleThumbnailStore.Decide(entry, vehicle.thumbnailVersion, _thumbnailChecked.Contains(id));
            if (step == VehicleThumbnailStore.Step.UseDisk || _thumbnailFailed.Contains(id)) { _thumbnailChecked.Add(id); yield break; }

            List<string> urls = VehicleThumbnailStore.Candidates(vehicle.thumbnailUrl, id, vehicle.addressableKey, thumbnailBaseUrl);
            bool sawNetworkError = false;
            foreach (string candidate in urls)
            {
                string url = DasServer.RewriteContentUrl(candidate);
                var headers = step == VehicleThumbnailStore.Step.Revalidate ? VehicleThumbnailStore.ConditionalHeaders(entry) : null;
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var r = UnityWebRequest.Get(url);
                    r.timeout = 20;
                    if (headers != null) foreach (var kv in headers) r.SetRequestHeader(kv.Key, kv.Value);
                    return r;
                }, r => request = r);
                if (request == null) { sawNetworkError = true; break; }
                using (request)
                {
                    long code = request.responseCode;
                    string etag = request.GetResponseHeader("ETag");
                    string lastModified = request.GetResponseHeader("Last-Modified");
                    if (code == 304 && entry != null)
                    {
                        ThumbStore.Confirm(entry, vehicle.thumbnailVersion, etag, lastModified);
                        _thumbnailChecked.Add(id);
                        yield break;
                    }
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        byte[] bytes = request.downloadHandler != null ? request.downloadHandler.data : null;
                        if (bytes != null && bytes.Length > 0 && SetThumbnail(id, bytes))
                        {
                            try { ThumbStore.Save(id, bytes, vehicle.thumbnailVersion, etag, lastModified, candidate.Split('?')[0]); }
                            catch (Exception e) { Debug.LogWarning("[RemoteLoader] Could not keep the thumbnail on disk: " + e.Message); }
                            _thumbnailChecked.Add(id);
                            yield return null;                                    // one decode per frame
                            yield break;
                        }
                        continue;                                                 // not a picture: try the next link
                    }
                    if (request.result == UnityWebRequest.Result.ProtocolError) continue;   // 404/403/400: next link
                    sawNetworkError = true;                                       // offline / timeout: keep what we have
                    break;
                }
            }

            if (!sawNetworkError)
            {
                _thumbnailFailed.Add(id);
                _thumbnailChecked.Add(id);
                if (entry == null)
                    Debug.Log("[RemoteLoader] No thumbnail on the server for " + id + " (tried " + urls.Count + " link(s)). Upload one as " +
                              id + ".png to the Thumbnail folder or set thumbnailPath in the catalog.");
            }
        }

        private bool SetThumbnail(string vehicleId, byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, true)) { Destroy(texture); return false; }
            Sprite old;
            if (_thumbnailCache.TryGetValue(vehicleId, out old) && old != null && old.texture != null)
                Destroy(old.texture, 5f);                                         // cards switch to the new one first
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            _thumbnailCache[vehicleId] = sprite;
            var handler = ThumbnailUpdated;
            if (handler != null)
            {
                try { handler(vehicleId, sprite); }
                catch (Exception e) { Debug.LogWarning("[RemoteLoader] A screen failed to show a thumbnail: " + e.Message); }
            }
            return true;
        }

        /// </summary>
        public Sprite GetThumbnail(string vehicleId)
        {
            _thumbnailCache.TryGetValue(vehicleId, out Sprite sprite);
            return sprite;
        }

        #endregion

        #region Vehicle Loading

        // ── Loading a vehicle ─────────────────────────────────────────────
        // Each load is a request with its own identity. A screen that is left (Back) or a new load cancels the old
        // request: its result is never handed to a screen that no longer exists, the vehicle isn't created into a
        // destroyed parent, and the loader is free again at once. Before, the "loading" flag was only cleared at the end
        // of a successful load, so a callback that threw (into the destroyed measurement screen) left it set and every
        // later load failed with "Another vehicle is currently loading".
        private class LoadRequest
        {
            public int id;
            public string key, vehicleName;
            public bool cancelled;
            public bool hadContainer;
            public Transform container;
            public Action<GameObject> onComplete;
            public Action<string> onError;
        }

        private LoadRequest _activeLoad;
        private int _nextLoadId;

        /// <summary>True while a vehicle is being downloaded or created.</summary>
        public bool IsLoading => _activeLoad != null;
        /// <summary>The key of the vehicle being loaded, or null.</summary>
        public string LoadingKey => _activeLoad != null ? _activeLoad.key : null;
        /// <summary>Raised when a load is cancelled (Back, or a new load replaced it).</summary>
        public event Action<string> LoadCancelled;

        /// <summary>
        /// Load vehicle by ID or addressable key. A load already running is cancelled and replaced (its download keeps
        /// going in the background and is kept on disk, so nothing is wasted).
        /// </summary>
        public void LoadVehicle(string vehicleIdOrKey, Action<GameObject> onComplete = null, Action<string> onError = null, Transform container = null)
        {
            var info = GetVehicleInfo(vehicleIdOrKey);
            string addressableKey = info?.addressableKey ?? vehicleIdOrKey;
            string vehicleName = info?.vehicleName ?? vehicleIdOrKey;
            if (string.IsNullOrEmpty(addressableKey)) { SafeInvoke(onError, "No vehicle was given to load."); return; }

            if (_activeLoad != null) CancelLoad();

            Transform target = container ?? vehicleContainer;
            var req = new LoadRequest
            {
                id = ++_nextLoadId, key = addressableKey, vehicleName = vehicleName,
                container = target, hadContainer = target != null, onComplete = onComplete, onError = onError,
            };
            _activeLoad = req;
            StartCoroutine(LoadVehicleCoroutine(req));
        }

        /// <summary>
        /// Stop waiting for the current load (call it when the screen that asked is left). The vehicle is not created and
        /// no callback runs. A download already in progress finishes in the background and stays on disk.
        /// </summary>
        public void CancelLoad()
        {
            var req = _activeLoad;
            if (req == null) return;
            req.cancelled = true;
            _activeLoad = null;
            _isLoading = false;
            Debug.Log("[RemoteLoader] Load of " + req.vehicleName + " cancelled.");
            var handler = LoadCancelled;
            if (handler != null) { try { handler(req.key); } catch (Exception e) { Debug.LogException(e); } }
        }

        // The asking screen is gone (its scene was unloaded) or its container was destroyed
        private static bool Gone(LoadRequest req)
        {
            if (req.cancelled) return true;
            if (req.hadContainer && req.container == null) return true;
            var owner = req.onComplete != null ? req.onComplete.Target as UnityEngine.Object : null;
            return owner is UnityEngine.Object && owner == null;
        }

        private void Finish(LoadRequest req)
        {
            if (_activeLoad == req) { _activeLoad = null; _isLoading = false; }
        }

        private static void SafeInvoke<T>(Action<T> callback, T value)
        {
            if (callback == null) return;
            var owner = callback.Target as UnityEngine.Object;
            if (owner is UnityEngine.Object && owner == null) return;            // the screen was destroyed
            try { callback(value); }
            catch (Exception e) { Debug.LogException(e); }                       // a screen's error must not break the loader
        }

        private IEnumerator LoadVehicleCoroutine(LoadRequest req)
        {
            _isLoading = true;
            string addressableKey = req.key, vehicleName = req.vehicleName;
            Debug.Log($"[RemoteLoader] Loading vehicle: {vehicleName} ({addressableKey})");
            OnDownloadStarted?.Invoke(vehicleName);

            if (autoUnloadPrevious && _currentVehicle != null) UnloadCurrentVehicle();

            // 1) How much must be downloaded
            var sizeHandle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;
            long downloadSize = sizeHandle.Status == AsyncOperationStatus.Succeeded ? sizeHandle.Result : 0;
            string sizeError = sizeHandle.Status == AsyncOperationStatus.Succeeded ? null : (sizeHandle.OperationException?.Message ?? "This vehicle is not in the catalog.");
            Addressables.Release(sizeHandle);
            if (Gone(req)) { Finish(req); yield break; }
            if (sizeError != null) { Fail(req, sizeError); yield break; }

            // 2) Enough disk space?
            if (downloadSize > 0)
            {
                string spaceProblem = VehicleMeasurement.Storage.DiskSpace.ProblemFor(downloadSize);
                if (spaceProblem != null) { Fail(req, spaceProblem); yield break; }
            }

            // 3) Download with real-byte progress
            if (downloadSize > 0)
            {
                Debug.Log($"[RemoteLoader] Downloading: {FormatBytes(downloadSize)}");
                var downloadHandle = Addressables.DownloadDependenciesAsync(addressableKey);
                _lastProgressTime = Time.realtimeSinceStartup;
                _lastDownloadedBytes = 0;

                while (!downloadHandle.IsDone)
                {
                    if (Gone(req))
                    {
                        // Keep the download going without us: when it finishes, record it so the vehicle shows on Home
                        StartCoroutine(FinishInBackground(downloadHandle, addressableKey, vehicleName));
                        Finish(req);
                        yield break;
                    }
                    var status = downloadHandle.GetDownloadStatus();
                    long downloaded = (long)status.DownloadedBytes;
                    long totalBytes = (long)status.TotalBytes;
                    float progress = totalBytes > 0 ? (float)downloaded / totalBytes : downloadHandle.PercentComplete;
                    float now = Time.realtimeSinceStartup;
                    float dt = now - _lastProgressTime;
                    if (dt > 0.1f)
                    {
                        _currentSpeed = (downloaded - _lastDownloadedBytes) / dt;
                        _lastProgressTime = now;
                        _lastDownloadedBytes = downloaded;
                    }
                    try { OnDownloadProgress?.Invoke(progress, downloaded, totalBytes > 0 ? totalBytes : downloadSize); }
                    catch (Exception e) { Debug.LogException(e); }
                    yield return null;
                }

                bool ok = downloadHandle.Status == AsyncOperationStatus.Succeeded;
                string error = ok ? null : FriendlyDownloadError(downloadHandle.OperationException);
                Addressables.Release(downloadHandle);
                if (!ok) { Fail(req, error); yield break; }
                RecordDownloaded(addressableKey);                    // on disk now, whatever happens next
                try { OnDownloadCompleted?.Invoke(vehicleName); } catch (Exception e) { Debug.LogException(e); }
                if (Gone(req)) { Finish(req); yield break; }
            }

            // 4) Create the vehicle
            if (Gone(req)) { Finish(req); yield break; }
            Debug.Log($"[RemoteLoader] Instantiating: {addressableKey}");
            var instantiateHandle = Addressables.InstantiateAsync(addressableKey, req.container);
            yield return instantiateHandle;

            if (instantiateHandle.Status != AsyncOperationStatus.Succeeded)
            {
                string error = instantiateHandle.OperationException?.Message ?? "The vehicle could not be created.";
                if (instantiateHandle.IsValid()) Addressables.Release(instantiateHandle);
                Fail(req, error);
                yield break;
            }

            if (Gone(req))
            {
                // Left while it was being created: don't leave a stray vehicle in the next scene
                Addressables.ReleaseInstance(instantiateHandle.Result);
                Finish(req);
                yield break;
            }

            if (autoUnloadPrevious && _currentVehicle != null && _currentVehicle != instantiateHandle.Result) UnloadCurrentVehicle();
            _currentVehicle = instantiateHandle.Result;
            _currentHandle = instantiateHandle;
            _currentVehicleId = addressableKey;
            RecordDownloaded(addressableKey);
            Debug.Log($"[RemoteLoader] Loaded: {_currentVehicle.name}");
            Finish(req);                                              // free before the callbacks: they may start another load
            try { OnVehicleLoaded?.Invoke(_currentVehicle); } catch (Exception e) { Debug.LogException(e); }
            SafeInvoke(req.onComplete, _currentVehicle);
        }

        private void Fail(LoadRequest req, string error)
        {
            bool wasActive = _activeLoad == req && !req.cancelled;
            Finish(req);
            if (!wasActive) return;                                   // nobody is waiting for this any more
            Debug.LogWarning($"[RemoteLoader] Loading {req.vehicleName} failed: {error}");
            try { OnDownloadFailed?.Invoke(req.vehicleName, error); } catch (Exception e) { Debug.LogException(e); }
            SafeInvoke(req.onError, error);
        }

        private IEnumerator FinishInBackground(AsyncOperationHandle downloadHandle, string key, string vehicleName)
        {
            while (!downloadHandle.IsDone) yield return null;
            bool ok = downloadHandle.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(downloadHandle);
            if (ok) { RecordDownloaded(key); Debug.Log("[RemoteLoader] " + vehicleName + " finished downloading in the background."); }
        }

        private void RecordDownloaded(string key)
        {
            var catalogInfo = GetVehicleInfo(key);
            if (catalogInfo != null) VehicleMeasurement.DownloadedVehiclesTracker.MarkAsDownloaded(catalogInfo);
        }

        /// <summary>A download error in plain words (disk full, no connection, server problem).</summary>
        public static string FriendlyDownloadError(Exception e)
        {
            string raw = e != null ? (e.InnerException != null ? e.InnerException.Message + " " : "") + e.Message : "";
            // Addressables nests the real cause (e.g. a 404 on one bundle) several exceptions deep
            var all = new System.Text.StringBuilder();
            for (var x = e; x != null && all.Length < 8000; x = x.InnerException) all.Append(x.Message).Append(' ');
            string r = all.ToString().ToLowerInvariant();
            if (r.Contains("disk full") || r.Contains("not enough space") || r.Contains("no space") || r.Contains("insufficient"))
                return "There isn't enough free disk space for this download. Free some space (Storage screen) or choose another download folder.";
            if (r.Contains("cannot resolve") || r.Contains("cannot connect") || r.Contains("timed out") || r.Contains("timeout") || r.Contains("error 52") || r.Contains("connection"))
                return "The download was interrupted (network). Check the connection and try again - what was already downloaded is kept.";
            if (r.Contains("403") || r.Contains("401")) return "The server refused the download. Sign in again and retry.";
            if (r.Contains("404") || r.Contains("not found"))
                return "Some of this vehicle's files are missing on the server. If it was just published, the publisher should publish it again with the latest DAS project.";
            return string.IsNullOrEmpty(raw) ? "Download failed." : "Download failed: " + raw;
        }

        // The storage service asks before deleting files: Unity can't delete a vehicle's files while it is loaded.
        private void OnStorageReleaseRequested(string vehicleId, string addressableKey)
        {
            if (_currentVehicle == null) return;
            bool everything = string.IsNullOrEmpty(vehicleId) && string.IsNullOrEmpty(addressableKey);
            var current = GetVehicleInfo(_currentVehicleId);
            bool match = everything
                || (!string.IsNullOrEmpty(addressableKey) && string.Equals(_currentVehicleId, addressableKey, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(vehicleId) && string.Equals(_currentVehicleId, vehicleId, StringComparison.OrdinalIgnoreCase))
                || (current != null && !string.IsNullOrEmpty(vehicleId) && string.Equals(current.vehicleId, vehicleId, StringComparison.OrdinalIgnoreCase));
            if (match) UnloadCurrentVehicle();
        }

        private void OnEnable()
        {
            VehicleMeasurement.Storage.VehicleStorageService.ReleaseRequested -= OnStorageReleaseRequested;
            VehicleMeasurement.Storage.VehicleStorageService.ReleaseRequested += OnStorageReleaseRequested;
        }

        private void OnDisable()
        {
            VehicleMeasurement.Storage.VehicleStorageService.ReleaseRequested -= OnStorageReleaseRequested;
        }

        /// <summary>
        /// Unload current vehicle and release memory
        /// </summary>
        public void UnloadCurrentVehicle()
        {
            bool hadVehicle = _currentVehicle != null || _currentHandle.IsValid();
            if (_currentVehicle != null) Addressables.ReleaseInstance(_currentVehicle);
            // Other code (e.g. MeasurementController.ClearExistingModels) may already have destroyed the instance. Its
            // handle must still be released: otherwise Addressables keeps the vehicle's files open until the app restarts,
            // and Unity refuses to delete them ("in use").
            if (_currentHandle.IsValid()) Addressables.Release(_currentHandle);
            _currentHandle = default(AsyncOperationHandle<GameObject>);
            _currentVehicle = null;
            _currentVehicleId = null;
            if (hadVehicle)
            {
                OnVehicleUnloaded?.Invoke();
                Debug.Log("[RemoteLoader] Vehicle unloaded");
            }
        }





        public IEnumerator ClearVehicleCacheByLocations(string keyOrLabel, System.Action<bool, string, long> onComplete)
        {
            if (string.IsNullOrEmpty(keyOrLabel))
            {
                onComplete?.Invoke(false, "Empty key/label", -1);
                yield break;
            }

            // 0) Resolve resource locations for the key (validates key)
            var locsH = Addressables.LoadResourceLocationsAsync(keyOrLabel);
            yield return locsH;

            if (locsH.Status != AsyncOperationStatus.Succeeded || locsH.Result == null || locsH.Result.Count == 0)
            {
                Addressables.Release(locsH);
                Debug.LogWarning($"[RemoteLoader] Key/label '{keyOrLabel}' not found in catalog; nothing to clear.");
                onComplete?.Invoke(true, null, 0);
                yield break;
            }

            IList<IResourceLocation> allLocs = locsH.Result;

            // (Optional) Filter to bundle-backed locations (helps avoid non-bundle providers)
            var bundleLocs = new List<IResourceLocation>(allLocs.Count);
            foreach (var loc in allLocs)
            {
                // AssetBundle-backed locations usually have ProviderId containing "AssetBundle"
                // or ResourceType == typeof(IAssetBundleResource).
                if (loc.ProviderId != null && loc.ProviderId.IndexOf("AssetBundle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    bundleLocs.Add(loc);
                else if (loc.ResourceType == typeof(IAssetBundleResource))
                    bundleLocs.Add(loc);
            }

            // If none matched the filter, fall back to all locations
            if (bundleLocs.Count == 0) bundleLocs.AddRange(allLocs);

            // (Optional) check how much would be downloaded if we reloaded this key
            long sizeBefore = -1;
            var sizeH = Addressables.GetDownloadSizeAsync(keyOrLabel);
            yield return sizeH;
            if (sizeH.Status == AsyncOperationStatus.Succeeded) sizeBefore = sizeH.Result;
            Addressables.Release(sizeH);

            Debug.Log($"[RemoteLoader] Clearing {bundleLocs.Count} locations for '{keyOrLabel}' (pre-size: {FormatBytes(sizeBefore)})");

            // 1) Clear the cache for these locations (manual release pattern; read then release)
            var clearH = Addressables.ClearDependencyCacheAsync(bundleLocs, false);
            yield return clearH;

            bool ok = (clearH.Status == AsyncOperationStatus.Succeeded);
            string err = ok ? null : (clearH.OperationException?.Message ?? "ClearDependencyCacheAsync failed");
            Addressables.Release(clearH);

            // 2) Verify: size after clear should now be > 0 if bundles were truly evicted
            long sizeAfter = 0;
            var sizeAfterH = Addressables.GetDownloadSizeAsync(keyOrLabel);
            yield return sizeAfterH;
            if (sizeAfterH.Status == AsyncOperationStatus.Succeeded) sizeAfter = sizeAfterH.Result;
            Addressables.Release(sizeAfterH);

            Debug.Log($"[RemoteLoader] After clear: '{keyOrLabel}' download size = {FormatBytes(sizeAfter)}");

            // Extra visibility: if still 0, list the InternalIds that are still satisfying this key
            if (sizeAfter == 0)
            {
                var probe = Addressables.LoadResourceLocationsAsync(keyOrLabel);
                yield return probe;
                if (probe.Status == AsyncOperationStatus.Succeeded && probe.Result != null)
                {
                    foreach (IResourceLocation loc in probe.Result)
                        Debug.Log($"[RemoteLoader][STILL] {keyOrLabel} → {loc.InternalId} (Provider:{loc.ProviderId})");
                }
                Addressables.Release(probe);
            }

            onComplete?.Invoke(ok, err, sizeAfter);
            Addressables.Release(locsH);
        }



        /// <summary>
        /// Check if a vehicle is already downloaded/cached
        /// </summary>
        public void CheckDownloadStatus(string vehicleIdOrKey, Action<bool, long> onResult)
        {
            var info = GetVehicleInfo(vehicleIdOrKey);
            string key = info?.addressableKey ?? vehicleIdOrKey;

            StartCoroutine(CheckDownloadStatusCoroutine(key, onResult));
        }

        private IEnumerator CheckDownloadStatusCoroutine(string addressableKey, Action<bool, long> onResult)
        {
            var handle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return handle;

            long size = handle.Result;
            bool isCached = (size == 0);

            Addressables.Release(handle);
            onResult?.Invoke(isCached, size);
        }

        #endregion

        #region Catalog Access

        /// <summary>
        /// Get list of all available vehicles
        /// </summary>
        public List<RemoteVehicleInfo> GetAvailableVehicles()
        {
            return new List<RemoteVehicleInfo>(_remoteCatalog);
        }

        /// <summary>
        /// Get vehicle info by ID or key
        /// </summary>
        public RemoteVehicleInfo GetVehicleInfo(string vehicleIdOrKey)
        {
            return _remoteCatalog.Find(v =>
                v.vehicleId == vehicleIdOrKey ||
                v.addressableKey == vehicleIdOrKey);
        }

        /// <summary>
        /// Check if catalog is loaded
        /// </summary>
        public bool IsCatalogLoaded => _catalogLoaded;
        /// <summary>True when the last catalog load failed (no server, no offline copy). Screens stop waiting then.</summary>
        public bool IsCatalogFailed { get; private set; }

        /// <summary>
        /// Get catalog version
        /// </summary>
        public string CatalogVersion => _catalogVersion;

        /// <summary>
        /// Get current download speed (bytes/sec)
        /// </summary>
        public float GetCurrentDownloadSpeed() => _currentSpeed;

        /// <summary>
        /// Check if currently loading
        /// </summary>

        #endregion

        #region Utilities

        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024f * 1024f):F1} MB";
            else
                return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Remote catalog JSON structure
    /// </summary>
    [Serializable]
    public class RemoteCatalogData
    {
        public string version;
        public string lastUpdated;
        public List<RemoteVehicleInfo> vehicles;
    }

    /// <summary>
    /// Vehicle info from remote catalog
    /// </summary>
    [Serializable]
    public class RemoteVehicleInfo
    {
        public string vehicleId;
        public string vehicleName;
        public string addressableKey;
        public string thumbnailUrl;
        /// <summary>Server's version of the thumbnail picture (changes when it is replaced); empty from older servers.</summary>
        public string thumbnailVersion;
        /// <summary>Published from Unity: the vehicle's own Addressables catalog, relative to the bundles URL (or a full URL).</summary>
        public string contentCatalogPath;
        public string category;
        public string manufacturer;
        public string modelYear;
        public string approximateSize;
        public string description;
        public bool hasVALData;
        public string version;
    }

    #endregion
}
