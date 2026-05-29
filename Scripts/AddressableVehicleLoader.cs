using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Events;

namespace VehicleMeasurement
{
    /// <summary>
    /// ADDRESSABLE VEHICLE LOADER
    /// 
    /// Manages loading large vehicle models via Addressables.
    /// Supports:
    /// - On-demand downloading
    /// - Progress tracking
    /// - Caching (downloaded once, loaded from cache after)
    /// - Memory management (unload when not needed)
    /// 
    /// SETUP:
    /// 1. Install Addressables package (Window > Package Manager > Addressables)
    /// 2. Mark vehicle prefabs as Addressable
    /// 3. Create Addressable Groups (one per vehicle recommended for large files)
    /// 4. Build Addressables (Window > Asset Management > Addressables > Groups > Build)
    /// 5. For remote hosting: Configure Remote Load Path in AddressableAssetSettings
    /// 
    /// USAGE:
    /// - Call LoadVehicle(addressKey) to download/load a vehicle
    /// - Subscribe to OnDownloadProgress for UI updates
    /// - Call UnloadVehicle() when done to free memory
    /// </summary>
    public class AddressableVehicleLoader : MonoBehaviour
    {
        public static AddressableVehicleLoader Instance { get; private set; }

        [Header("═══ SETTINGS ═══")]
        [Tooltip("Parent transform for instantiated vehicles")]
        public Transform vehicleContainer;

        [Tooltip("Auto-unload previous vehicle when loading new one")]
        public bool autoUnloadPrevious = true;

        [Header("═══ VEHICLE CATALOG ═══")]
        [Tooltip("List of available vehicles with their Addressable keys")]
        public List<VehicleAddressableInfo> vehicleCatalog = new List<VehicleAddressableInfo>();

        [Header("═══ EVENTS ═══")]
        public UnityEvent<string> OnDownloadStarted;
        public UnityEvent<float, long, long> OnDownloadProgress;  // progress, downloadedBytes, totalBytes
        public UnityEvent<string> OnDownloadCompleted;
        public UnityEvent<string, string> OnDownloadFailed;       // vehicleId, error
        public UnityEvent<GameObject> OnVehicleLoaded;
        public UnityEvent OnVehicleUnloaded;

        // Current state
        private GameObject _currentVehicle;
        private AsyncOperationHandle<GameObject> _currentHandle;
        private string _currentVehicleId;
        private string _currentAddressableKey;
        private bool _isLoading;

        // Download tracking for speed calculation
        private float _lastProgressTime;
        private long _lastDownloadedBytes;
        private float _currentSpeed;
        private Queue<float> _speedSamples = new Queue<float>();
        private const int SPEED_SAMPLE_COUNT = 5;

        // Cache for download sizes
        private Dictionary<string, long> _downloadSizeCache = new Dictionary<string, long>();

        private void Awake()
        {
            // Check if Instance exists and is still valid (not destroyed)
            if (Instance != null && Instance != this)
            {
                // Check if the existing instance is actually valid (not destroyed)
                try
                {
                    // This will throw if the object is destroyed
                    var _ = Instance.gameObject.name;

                    // Instance is valid, destroy this duplicate
                    Debug.Log("[AddressableLoader] Instance already exists, destroying duplicate");
                    Destroy(gameObject);
                    return;
                }
                catch
                {
                    // Instance was destroyed, we'll become the new instance
                    Debug.Log("[AddressableLoader] Old instance was destroyed, taking over");
                    Instance = null;
                }
            }

            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[AddressableLoader] Initialized as singleton");
            }
        }

        private void OnDestroy()
        {
            // Clear instance if we're being destroyed
            if (Instance == this)
            {
                Debug.Log("[AddressableLoader] Singleton being destroyed, clearing Instance");
                Instance = null;
            }

            UnloadCurrentVehicle();
        }

        #region Public API

        /// <summary>
        /// Get list of all available vehicles
        /// </summary>
        public List<VehicleAddressableInfo> GetAvailableVehicles()
        {
            return vehicleCatalog;
        }

        /// <summary>
        /// Get current download speed in bytes per second
        /// </summary>
        public float GetCurrentDownloadSpeed()
        {
            return _currentSpeed;
        }

        /// <summary>
        /// Check if a vehicle is already downloaded/cached
        /// </summary>
        public void CheckDownloadStatus(string vehicleId, Action<bool, long> callback)
        {
            var info = GetVehicleInfo(vehicleId);
            if (info == null)
            {
                callback?.Invoke(false, 0);
                return;
            }

            StartCoroutine(CheckDownloadStatusCoroutine(info.addressableKey, callback));
        }

        /// <summary>
        /// Check download status by addressable key
        /// </summary>
        public void CheckDownloadStatusByKey(string addressableKey, Action<bool, long> callback)
        {
            StartCoroutine(CheckDownloadStatusCoroutine(addressableKey, callback));
        }

        /// <summary>
        /// Get download size for a vehicle (0 if already cached)
        /// </summary>
        public void GetDownloadSize(string vehicleId, Action<long> callback)
        {
            var info = GetVehicleInfo(vehicleId);
            if (info == null)
            {
                callback?.Invoke(0);
                return;
            }

            StartCoroutine(GetDownloadSizeCoroutine(info.addressableKey, callback));
        }

        /// <summary>
        /// Load a vehicle by ID (downloads if needed)
        /// </summary>
        /// <param name="vehicleIdOrKey">Vehicle ID from catalog or direct addressable key</param>
        /// <param name="onComplete">Called when vehicle is loaded</param>
        /// <param name="onError">Called on error</param>
        /// <param name="container">Optional container transform (overrides vehicleContainer)</param>
        public void LoadVehicle(string vehicleIdOrKey, Action<GameObject> onComplete = null, Action<string> onError = null, Transform container = null)
        {
            if (_isLoading)
            {
                onError?.Invoke("Already loading a vehicle");
                return;
            }

            // Use provided container or fall back to vehicleContainer
            Transform targetContainer = container ?? vehicleContainer;

            // Try to find by ID first
            var info = GetVehicleInfo(vehicleIdOrKey);

            // If not found by ID, try by addressable key
            if (info == null)
            {
                info = GetVehicleInfoByKey(vehicleIdOrKey);
            }

            // If still not found, treat the input as a direct addressable key
            if (info == null)
            {
                Debug.Log($"[AddressableLoader] Loading by direct key: {vehicleIdOrKey}");
                StartCoroutine(LoadVehicleByKeyCoroutine(vehicleIdOrKey, onComplete, onError, targetContainer));
                return;
            }

            StartCoroutine(LoadVehicleCoroutine(info, onComplete, onError, targetContainer));
        }

        /// <summary>
        /// Load a vehicle directly by addressable key (without catalog lookup)
        /// </summary>
        public void LoadVehicleByKey(string addressableKey, Action<GameObject> onComplete = null, Action<string> onError = null, Transform container = null)
        {
            if (_isLoading)
            {
                onError?.Invoke("Already loading a vehicle");
                return;
            }

            Transform targetContainer = container ?? vehicleContainer;
            StartCoroutine(LoadVehicleByKeyCoroutine(addressableKey, onComplete, onError, targetContainer));
        }

        /// <summary>
        /// Preload/download a vehicle without instantiating
        /// </summary>
        public void PreloadVehicle(string vehicleId, Action onComplete = null, Action<string> onError = null)
        {
            var info = GetVehicleInfo(vehicleId);
            if (info == null)
            {
                onError?.Invoke($"Vehicle not found: {vehicleId}");
                return;
            }

            StartCoroutine(PreloadVehicleCoroutine(info, onComplete, onError));
        }

        /// <summary>
        /// Unload current vehicle and free memory
        /// </summary>
        public void UnloadCurrentVehicle()
        {
            if (_currentVehicle != null)
            {
                Destroy(_currentVehicle);
                _currentVehicle = null;
            }

            if (_currentHandle.IsValid())
            {
                Addressables.Release(_currentHandle);
            }

            _currentVehicleId = null;
            _currentAddressableKey = null;
            OnVehicleUnloaded?.Invoke();

            // Force garbage collection for large assets
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        /// <summary>
        /// Get currently loaded vehicle
        /// </summary>
        public GameObject GetCurrentVehicle()
        {
            return _currentVehicle;
        }

        /// <summary>
        /// Get current vehicle ID
        /// </summary>
        public string GetCurrentVehicleId()
        {
            return _currentVehicleId;
        }

        /// <summary>
        /// Get current addressable key
        /// </summary>
        public string GetCurrentAddressableKey()
        {
            return _currentAddressableKey;
        }

        /// <summary>
        /// Check if currently loading
        /// </summary>
        public bool IsLoading()
        {
            return _isLoading;
        }

        /// <summary>
        /// Clear all cached downloads (use carefully!)
        /// </summary>

        public void ClearCache()
        {
            Caching.ClearCache();
            _downloadSizeCache.Clear();
        }


        #endregion

        #region Coroutines

        private IEnumerator CheckDownloadStatusCoroutine(string addressableKey, Action<bool, long> callback)
        {
            var sizeHandle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;

            if (sizeHandle.Status == AsyncOperationStatus.Succeeded)
            {
                long size = sizeHandle.Result;
                bool isCached = size == 0;
                callback?.Invoke(isCached, size);
            }
            else
            {
                callback?.Invoke(false, 0);
            }

            Addressables.Release(sizeHandle);
        }

        private IEnumerator GetDownloadSizeCoroutine(string addressableKey, Action<long> callback)
        {
            // Check cache first
            if (_downloadSizeCache.TryGetValue(addressableKey, out long cachedSize))
            {
                callback?.Invoke(cachedSize);
                yield break;
            }

            var sizeHandle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;

            if (sizeHandle.Status == AsyncOperationStatus.Succeeded)
            {
                _downloadSizeCache[addressableKey] = sizeHandle.Result;
                callback?.Invoke(sizeHandle.Result);
            }
            else
            {
                callback?.Invoke(-1);
            }

            Addressables.Release(sizeHandle);
        }

        private IEnumerator LoadVehicleCoroutine(VehicleAddressableInfo info, Action<GameObject> onComplete, Action<string> onError, Transform container)
        {
            _isLoading = true;
            _lastProgressTime = Time.time;
            _lastDownloadedBytes = 0;
            _currentSpeed = 0;

            OnDownloadStarted?.Invoke(info.vehicleId);

            // Unload previous if needed
            if (autoUnloadPrevious && _currentVehicle != null)
            {
                UnloadCurrentVehicle();
                yield return null; // Wait a frame
            }

            // Check download size first
            var sizeHandle = Addressables.GetDownloadSizeAsync(info.addressableKey);
            yield return sizeHandle;

            long totalSize = 0;
            if (sizeHandle.Status == AsyncOperationStatus.Succeeded)
            {
                totalSize = sizeHandle.Result;
            }
            Addressables.Release(sizeHandle);

            // Download dependencies if needed
            if (totalSize > 0)
            {
                Debug.Log($"[AddressableLoader] Downloading {info.vehicleName}: {FormatBytes(totalSize)}");

                var downloadHandle = Addressables.DownloadDependenciesAsync(info.addressableKey);

                while (!downloadHandle.IsDone)
                {
                    float progress = downloadHandle.PercentComplete;
                    long downloaded = (long)(totalSize * progress);

                    // Calculate speed
                    float currentTime = Time.time;
                    float deltaTime = currentTime - _lastProgressTime;
                    if (deltaTime > 0.5f)
                    {
                        long deltaBytes = downloaded - _lastDownloadedBytes;
                        _currentSpeed = deltaBytes / deltaTime;
                        _lastProgressTime = currentTime;
                        _lastDownloadedBytes = downloaded;
                    }

                    OnDownloadProgress?.Invoke(progress, downloaded, totalSize);
                    yield return null;
                }

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    _isLoading = false;
                    string error = downloadHandle.OperationException?.Message ?? "Download failed";
                    OnDownloadFailed?.Invoke(info.vehicleId, error);
                    onError?.Invoke(error);
                    Addressables.Release(downloadHandle);
                    yield break;
                }

                Addressables.Release(downloadHandle);
            }

            OnDownloadCompleted?.Invoke(info.vehicleId);

            // Now instantiate
            Debug.Log($"[AddressableLoader] Instantiating {info.vehicleName}...");

            _currentHandle = Addressables.InstantiateAsync(info.addressableKey, container);

            while (!_currentHandle.IsDone)
            {
                yield return null;
            }

            if (_currentHandle.Status == AsyncOperationStatus.Succeeded)
            {
                _currentVehicle = _currentHandle.Result;
                _currentVehicle.name = info.vehicleName;
                _currentVehicleId = info.vehicleId;
                _currentAddressableKey = info.addressableKey;

                Debug.Log($"[AddressableLoader] Loaded: {info.vehicleName}");

                OnVehicleLoaded?.Invoke(_currentVehicle);
                onComplete?.Invoke(_currentVehicle);
            }
            else
            {
                string error = _currentHandle.OperationException?.Message ?? "Instantiation failed";
                OnDownloadFailed?.Invoke(info.vehicleId, error);
                onError?.Invoke(error);
            }

            _isLoading = false;
        }

        /// <summary>
        /// Load by direct addressable key (not using catalog)
        /// </summary>
        private IEnumerator LoadVehicleByKeyCoroutine(string addressableKey, Action<GameObject> onComplete, Action<string> onError, Transform container)
        {
            _isLoading = true;
            _lastProgressTime = Time.time;
            _lastDownloadedBytes = 0;
            _currentSpeed = 0;

            Debug.Log($"[AddressableLoader] Starting load by key: {addressableKey}");
            OnDownloadStarted?.Invoke(addressableKey);

            // Unload previous if needed
            if (autoUnloadPrevious && _currentVehicle != null)
            {
                UnloadCurrentVehicle();
                yield return null;
            }

            // Check download size
            Debug.Log($"[AddressableLoader] Checking download size for: {addressableKey}");
            var sizeHandle = Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;

            long totalSize = 0;
            if (sizeHandle.Status == AsyncOperationStatus.Succeeded)
            {
                totalSize = sizeHandle.Result;
                Debug.Log($"[AddressableLoader] Download size: {FormatBytes(totalSize)} (0 = cached)");
            }
            else
            {
                Debug.LogWarning($"[AddressableLoader] Failed to get download size: {sizeHandle.OperationException?.Message}");
            }
            Addressables.Release(sizeHandle);

            // Download if needed
            if (totalSize > 0)
            {
                Debug.Log($"[AddressableLoader] Downloading {addressableKey}: {FormatBytes(totalSize)}");

                var downloadHandle = Addressables.DownloadDependenciesAsync(addressableKey);

                while (!downloadHandle.IsDone)
                {
                    float progress = downloadHandle.PercentComplete;
                    long downloaded = (long)(totalSize * progress);

                    // Calculate speed
                    float currentTime = Time.time;
                    float deltaTime = currentTime - _lastProgressTime;
                    if (deltaTime > 0.5f)
                    {
                        long deltaBytes = downloaded - _lastDownloadedBytes;
                        _currentSpeed = deltaBytes / deltaTime;
                        _lastProgressTime = currentTime;
                        _lastDownloadedBytes = downloaded;
                    }

                    OnDownloadProgress?.Invoke(progress, downloaded, totalSize);
                    yield return null;
                }

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    _isLoading = false;
                    string error = downloadHandle.OperationException?.Message ?? "Download failed";
                    Debug.LogError($"[AddressableLoader] Download failed: {error}");
                    OnDownloadFailed?.Invoke(addressableKey, error);
                    onError?.Invoke(error);
                    Addressables.Release(downloadHandle);
                    yield break;
                }

                Addressables.Release(downloadHandle);
            }
            else
            {
                Debug.Log($"[AddressableLoader] Model already cached, skipping download");
            }

            OnDownloadCompleted?.Invoke(addressableKey);

            // Instantiate
            Debug.Log($"[AddressableLoader] Instantiating {addressableKey}...");

            if (container == null)
            {
                Debug.LogWarning($"[AddressableLoader] container is null, instantiating at root");
            }

            _currentHandle = Addressables.InstantiateAsync(addressableKey, container);

            float instantiateTimeout = 30f;
            float startTime = Time.time;

            while (!_currentHandle.IsDone)
            {
                // Add timeout check
                if (Time.time - startTime > instantiateTimeout)
                {
                    _isLoading = false;
                    string error = "Instantiation timed out after 30 seconds";
                    Debug.LogError($"[AddressableLoader] {error}");
                    OnDownloadFailed?.Invoke(addressableKey, error);
                    onError?.Invoke(error);
                    yield break;
                }
                yield return null;
            }

            if (_currentHandle.Status == AsyncOperationStatus.Succeeded)
            {
                _currentVehicle = _currentHandle.Result;
                _currentVehicleId = null; // No catalog ID
                _currentAddressableKey = addressableKey;

                Debug.Log($"[AddressableLoader] ✓ Successfully loaded: {_currentVehicle.name}");

                OnVehicleLoaded?.Invoke(_currentVehicle);
                onComplete?.Invoke(_currentVehicle);
            }
            else
            {
                string error = _currentHandle.OperationException?.Message ?? "Instantiation failed";
                Debug.LogError($"[AddressableLoader] Instantiation failed: {error}");
                OnDownloadFailed?.Invoke(addressableKey, error);
                onError?.Invoke(error);
            }

            _isLoading = false;
        }

        private IEnumerator PreloadVehicleCoroutine(VehicleAddressableInfo info, Action onComplete, Action<string> onError)
        {
            OnDownloadStarted?.Invoke(info.vehicleId);

            // Check download size
            var sizeHandle = Addressables.GetDownloadSizeAsync(info.addressableKey);
            yield return sizeHandle;

            long totalSize = 0;
            if (sizeHandle.Status == AsyncOperationStatus.Succeeded)
            {
                totalSize = sizeHandle.Result;
            }
            Addressables.Release(sizeHandle);

            if (totalSize == 0)
            {
                Debug.Log($"[AddressableLoader] {info.vehicleName} already cached");
                OnDownloadCompleted?.Invoke(info.vehicleId);
                onComplete?.Invoke();
                yield break;
            }

            // Download
            _lastProgressTime = Time.time;
            _lastDownloadedBytes = 0;

            var downloadHandle = Addressables.DownloadDependenciesAsync(info.addressableKey);

            while (!downloadHandle.IsDone)
            {
                float progress = downloadHandle.PercentComplete;
                long downloaded = (long)(totalSize * progress);

                // Calculate speed
                float currentTime = Time.time;
                float deltaTime = currentTime - _lastProgressTime;
                if (deltaTime > 0.5f)
                {
                    long deltaBytes = downloaded - _lastDownloadedBytes;
                    _currentSpeed = deltaBytes / deltaTime;
                    _lastProgressTime = currentTime;
                    _lastDownloadedBytes = downloaded;
                }

                OnDownloadProgress?.Invoke(progress, downloaded, totalSize);
                yield return null;
            }

            if (downloadHandle.Status == AsyncOperationStatus.Succeeded)
            {
                OnDownloadCompleted?.Invoke(info.vehicleId);
                onComplete?.Invoke();
            }
            else
            {
                string error = downloadHandle.OperationException?.Message ?? "Download failed";
                OnDownloadFailed?.Invoke(info.vehicleId, error);
                onError?.Invoke(error);
            }

            Addressables.Release(downloadHandle);
        }

        #endregion

        #region Helpers

        public VehicleAddressableInfo GetVehicleInfo(string vehicleId)
        {
            return vehicleCatalog.Find(v => v.vehicleId == vehicleId);
        }

        public VehicleAddressableInfo GetVehicleInfoByKey(string addressableKey)
        {
            return vehicleCatalog.Find(v => v.addressableKey == addressableKey);
        }

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

        public static string FormatSpeed(float bytesPerSecond)
        {
            return $"{FormatBytes((long)bytesPerSecond)}/s";
        }

        public static string FormatETA(long remainingBytes, float bytesPerSecond)
        {
            if (bytesPerSecond <= 0) return "Calculating...";

            float seconds = remainingBytes / bytesPerSecond;

            if (seconds < 60)
                return $"{seconds:F0}s";
            else if (seconds < 3600)
                return $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s";
            else
                return $"{(int)(seconds / 3600)}h {(int)((seconds % 3600) / 60)}m";
        }

        #endregion
    }

    /// <summary>
    /// Info about an addressable vehicle
    /// </summary>
    [Serializable]
    public class VehicleAddressableInfo
    {
        [Tooltip("Unique ID for this vehicle")]
        public string vehicleId;

        [Tooltip("Display name")]
        public string vehicleName;

        [Tooltip("Addressable key/address for this vehicle prefab")]
        public string addressableKey;

        [Tooltip("Thumbnail for UI (can be a small local sprite)")]
        public Sprite thumbnail;

        [Tooltip("Description or specs")]
        [TextArea(2, 4)]
        public string description;

        [Tooltip("Approximate file size for display (actual size checked at runtime)")]
        public string approximateSize;
        public string modelYear;

        [Tooltip("Category/Type (e.g., SUV, Sedan, Truck)")]
        public string category;

        [Tooltip("Manufacturer")]
        public string manufacturer;


        public bool hasVALData;
        public string version;

    }
}
