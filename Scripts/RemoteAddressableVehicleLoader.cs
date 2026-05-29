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
        public string catalogUrl = "https://your-server.com/vehicles/catalog.json";

        [Tooltip("Base URL for thumbnail images")]
        public string thumbnailBaseUrl = "https://your-server.com/vehicles/thumbnails/";

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
            if (Input.GetKeyDown(KeyCode.R)) { ForceRefreshAddressablesCatalogs(); }
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
        }

        /// <summary>
        /// Initialize catalog from server or cache
        /// </summary>
        public IEnumerator InitializeCatalog()
        {
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
                    OnCatalogError?.Invoke("Failed to load vehicle catalog");
                    yield break;
                }
            }

            _catalogLoaded = true;
            OnCatalogLoaded?.Invoke(_remoteCatalog.Count);

            Debug.Log($"[RemoteLoader] Catalog loaded: {_remoteCatalog.Count} vehicles (v{_catalogVersion})");

            // Preload thumbnails
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

        private IEnumerator LoadCatalogFromServer(Action<bool> onComplete)
        {
            Debug.Log($"[RemoteLoader] Loading catalog from: {catalogUrl}");

            using (UnityWebRequest request = UnityWebRequest.Get(catalogUrl))
            {
                request.timeout = 10; // 10 second timeout

                yield return request.SendWebRequest();

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

        private IEnumerator PreloadThumbnails()
        {
            foreach (var vehicle in _remoteCatalog)
            {
                if (string.IsNullOrEmpty(vehicle.thumbnailUrl))
                    continue;

                if (_thumbnailCache.ContainsKey(vehicle.vehicleId))
                    continue;

                yield return StartCoroutine(LoadThumbnail(vehicle));
            }
        }

        private IEnumerator LoadThumbnail(RemoteVehicleInfo vehicle)
        {
            string url = vehicle.thumbnailUrl;

            // If relative URL, prepend base URL
            if (!url.StartsWith("http"))
            {
                url = thumbnailBaseUrl + url;
            }

            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f)
                    );

                    _thumbnailCache[vehicle.vehicleId] = sprite;
                }
            }
        }

        /// <summary>
        /// Get thumbnail for a vehicle (may return null if not loaded yet)
        /// </summary>
        public Sprite GetThumbnail(string vehicleId)
        {
            _thumbnailCache.TryGetValue(vehicleId, out Sprite sprite);
            return sprite;
        }

        #endregion

        #region Vehicle Loading

        /// <summary>
        /// Load vehicle by ID or addressable key
        /// </summary>
        public void LoadVehicle(string vehicleIdOrKey, Action<GameObject> onComplete = null, Action<string> onError = null, Transform container = null)
        {
            if (_isLoading)
            {
                onError?.Invoke("Another vehicle is currently loading");
                return;
            }

            // Find vehicle info
            var info = GetVehicleInfo(vehicleIdOrKey);
            string addressableKey = info?.addressableKey ?? vehicleIdOrKey;
            string vehicleName = info?.vehicleName ?? vehicleIdOrKey;

            Transform targetContainer = container ?? vehicleContainer;

            StartCoroutine(LoadVehicleCoroutine(addressableKey, vehicleName, onComplete, onError, targetContainer));
        }

        // RemoteAddressableVehicleLoader.cs
        // Replace your existing coroutine with this version (signature unchanged)
        private IEnumerator LoadVehicleCoroutine(
            string addressableKey,
            string vehicleName,
            Action<GameObject> onComplete,
            Action<string> onError,
            Transform container)
        {
            _isLoading = true;
            Debug.Log($"[RemoteLoader] Loading vehicle: {vehicleName} ({addressableKey})");
            OnDownloadStarted?.Invoke(vehicleName);

            // Unload previous instance if requested
            if (autoUnloadPrevious && _currentVehicle != null)
            {
                UnloadCurrentVehicle(); // your existing method
            }

            // 1) Ask the (already-updated) catalog how many bytes are needed for this key
            var sizeHandle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;

            long downloadSize = sizeHandle.Result;
            Addressables.Release(sizeHandle);

            // 2) If bytes are needed, clear cache for this key to force latest content
            if (downloadSize > 0)
            {
                // autoRelease=true, so no Addressables.Release(...) call needed
                var clearHandle = Addressables.ClearDependencyCacheAsync(addressableKey, true);
                yield return clearHandle;

                // (Optional) recompute size post-clear so UI reflects the true download size
                var sizeAfterClearHandle = Addressables.GetDownloadSizeAsync(addressableKey);
                yield return sizeAfterClearHandle;
                downloadSize = sizeAfterClearHandle.Result;
                Addressables.Release(sizeAfterClearHandle);
            }

            // 3) Download with real-byte progress if needed
            if (downloadSize > 0)
            {
                Debug.Log($"[RemoteLoader] Downloading: {FormatBytes(downloadSize)}");

                var downloadHandle = Addressables.DownloadDependenciesAsync(addressableKey);

                _lastProgressTime = Time.realtimeSinceStartup;
                _lastDownloadedBytes = 0;

                while (!downloadHandle.IsDone)
                {
                    // Real counters from Addressables
                    var status = downloadHandle.GetDownloadStatus();
                    long downloaded = (long)status.DownloadedBytes;
                    long totalBytes = (long)status.TotalBytes;

                    float progress = totalBytes > 0
                        ? (float)downloaded / totalBytes
                        : downloadHandle.PercentComplete;

                    // Compute instantaneous speed (bytes/sec)
                    float now = Time.realtimeSinceStartup;
                    float dt = now - _lastProgressTime;
                    if (dt > 0.1f)
                    {
                        long deltaBytes = downloaded - _lastDownloadedBytes;
                        _currentSpeed = deltaBytes / dt; // bytes/sec (use GetCurrentDownloadSpeed() elsewhere)
                        _lastProgressTime = now;
                        _lastDownloadedBytes = downloaded;

                        // Optional debugger line:
                        // Debug.Log($"[DL] {FormatBytes(downloaded)} / {FormatBytes(totalBytes)}  @ {_currentSpeed / (1024f*1024f):F2} MB/s");
                    }

                    OnDownloadProgress?.Invoke(progress, downloaded, totalBytes > 0 ? totalBytes : downloadSize);
                    yield return null;
                }

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    string error = downloadHandle.OperationException?.Message ?? "Download failed";
                    Debug.LogError($"[RemoteLoader] Download failed: {error}");
                    OnDownloadFailed?.Invoke(vehicleName, error);
                    onError?.Invoke(error);
                    _isLoading = false;
                    Addressables.Release(downloadHandle);
                    yield break;
                }

                Addressables.Release(downloadHandle);
                OnDownloadCompleted?.Invoke(vehicleName);
            }

            // 4) Instantiate
            Debug.Log($"[RemoteLoader] Instantiating: {addressableKey}");
            var instantiateHandle = Addressables.InstantiateAsync(addressableKey, container);
            yield return instantiateHandle;

            if (instantiateHandle.Status == AsyncOperationStatus.Succeeded)
            {
                _currentVehicle = instantiateHandle.Result;
                _currentHandle = instantiateHandle;   // you already store this in your class
                _currentVehicleId = addressableKey;

                Debug.Log($"[RemoteLoader] ✓ Loaded: {_currentVehicle.name}");
                OnVehicleLoaded?.Invoke(_currentVehicle);
                onComplete?.Invoke(_currentVehicle);
            }
            else
            {
                string error = instantiateHandle.OperationException?.Message ?? "Instantiation failed";
                Debug.LogError($"[RemoteLoader] Instantiation failed: {error}");
                OnDownloadFailed?.Invoke(vehicleName, error);
                onError?.Invoke(error);
            }

            _isLoading = false;
        }

        /// <summary>
        /// Unload current vehicle and release memory
        /// </summary>
        public void UnloadCurrentVehicle()
        {
            if (_currentVehicle != null)
            {
                Addressables.ReleaseInstance(_currentVehicle);
                _currentVehicle = null;
                _currentVehicleId = null;
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
        public bool IsLoading => _isLoading;

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
