using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace VehicleMeasurement
{
    /// <summary>
    /// CONTROLLED MEASUREMENT STORAGE
    /// 
    /// Flow:
    /// 1. Always CHECK SERVER first for existing data
    /// 2. If found on server → Load as READ-ONLY (users can't modify)
    /// 3. If not found → Allow measurement, save based on user role
    /// 
    /// Access Levels:
    /// - Viewer: Can only view server data, cannot save
    /// - User: Can measure new vehicles, save to LOCAL only
    /// - Admin: Can measure and save to SERVER
    /// </summary>
    public class ControlledMeasurementStorage : MonoBehaviour
    {
        public static ControlledMeasurementStorage Instance { get; private set; }

        [Header("═══ SERVER SETTINGS ═══")]
        public string serverBaseUrl = "http://your-server:8080/api";
        public float requestTimeout = 15f;

        [Header("═══ ACCESS CONTROL ═══")]
        public UserAccessLevel accessLevel = UserAccessLevel.User;

        [Tooltip("Password required to save to server (Admin only)")]
        public string adminPassword = "";

        [Header("═══ BEHAVIOR ═══")]
        [Tooltip("Always check server first before allowing new measurements")]
        public bool checkServerFirst = true;

        [Tooltip("Allow local saves when server data doesn't exist")]
        public bool allowLocalSave = true;

        [Header("═══ STATUS (Runtime) ═══")]
        [SerializeField] private bool _isServerAvailable = false;
        [SerializeField] private bool _isChecking = false;
        [SerializeField] private string _lastError = "";

        // Properties
        public bool IsServerAvailable => _isServerAvailable;
        public bool IsChecking => _isChecking;
        public string LastError => _lastError;

        // Events
        public event Action<string, MeasurementSource, SavedVehicleMeasurement> OnDataFound;
        public event Action<string> OnDataNotFound;
        public event Action<string, bool> OnSaveComplete;
        public event Action<string> OnError;
        public event Action<bool> OnServerStatusChanged;

        #region Singleton

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            StartCoroutine(CheckServerConnection());
        }

        #endregion

        #region Server Connection

        private IEnumerator CheckServerConnection()
        {
            string url = $"{serverBaseUrl}/health";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                bool wasAvailable = _isServerAvailable;
                _isServerAvailable = request.result == UnityWebRequest.Result.Success;

                if (wasAvailable != _isServerAvailable)
                    OnServerStatusChanged?.Invoke(_isServerAvailable);

                Debug.Log($"[Storage] Server {(_isServerAvailable ? "ONLINE" : "OFFLINE")}");
            }
        }

        public void RefreshServerStatus(Action<bool> callback = null)
        {
            StartCoroutine(RefreshServerStatusCoroutine(callback));
        }

        private IEnumerator RefreshServerStatusCoroutine(Action<bool> callback)
        {
            yield return CheckServerConnection();
            callback?.Invoke(_isServerAvailable);
        }

        #endregion

        #region Check & Load Methods

        /// <summary>
        /// Check if measurement data exists (server first, then local)
        /// Returns source and data if found
        /// </summary>
        public void CheckAndLoad(string vehicleId, Action<MeasurementCheckResult> callback)
        {
            StartCoroutine(CheckAndLoadCoroutine(vehicleId, callback));
        }

        private IEnumerator CheckAndLoadCoroutine(string vehicleId, Action<MeasurementCheckResult> callback)
        {
            _isChecking = true;
            var result = new MeasurementCheckResult();
            result.VehicleId = vehicleId;

            // 1. Check SERVER first
            if (checkServerFirst && _isServerAvailable)
            {
                Debug.Log($"[Storage] Checking server for: {vehicleId}");

                bool serverCheckDone = false;
                yield return CheckServerCoroutine(vehicleId, (serverResult) => {
                    if (serverResult != null)
                    {
                        result.Found = true;
                        result.Source = MeasurementSource.Server;
                        result.Data = serverResult;
                        result.IsReadOnly = true; // Server data is read-only for non-admins

                        if (accessLevel == UserAccessLevel.Admin)
                            result.IsReadOnly = false;
                    }
                    serverCheckDone = true;
                });

                while (!serverCheckDone) yield return null;

                if (result.Found)
                {
                    Debug.Log($"[Storage] Found on SERVER: {vehicleId} (ReadOnly: {result.IsReadOnly})");
                    _isChecking = false;
                    OnDataFound?.Invoke(vehicleId, MeasurementSource.Server, result.Data);
                    callback?.Invoke(result);
                    yield break;
                }
            }

            // 2. Check LOCAL
            Debug.Log($"[Storage] Checking local for: {vehicleId}");
            var localData = VehicleMeasurementStorage.Load(vehicleId);

            if (localData != null)
            {
                result.Found = true;
                result.Source = MeasurementSource.Local;
                result.Data = localData;
                result.IsReadOnly = false; // Local data can be modified

                Debug.Log($"[Storage] Found LOCALLY: {vehicleId}");
                _isChecking = false;
                OnDataFound?.Invoke(vehicleId, MeasurementSource.Local, result.Data);
                callback?.Invoke(result);
                yield break;
            }

            // 3. Not found anywhere
            Debug.Log($"[Storage] NOT FOUND: {vehicleId} - New measurement allowed");
            result.Found = false;
            result.Source = MeasurementSource.None;
            result.Data = null;
            result.IsReadOnly = false;
            result.CanMeasure = (accessLevel != UserAccessLevel.Viewer);

            _isChecking = false;
            OnDataNotFound?.Invoke(vehicleId);
            callback?.Invoke(result);
        }

        private IEnumerator CheckServerCoroutine(string vehicleId, Action<SavedVehicleMeasurement> callback)
        {
            string url = $"{serverBaseUrl}/measurements/{vehicleId}";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)requestTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var data = JsonUtility.FromJson<SavedVehicleMeasurement>(request.downloadHandler.text);
                        callback?.Invoke(data);
                    }
                    catch (Exception e)
                    {
                        _lastError = e.Message;
                        callback?.Invoke(null);
                    }
                }
                else
                {
                    // 404 = not found (expected), other errors = problem
                    if (request.responseCode != 404)
                        _lastError = request.error;

                    callback?.Invoke(null);
                }
            }
        }

        /// <summary>
        /// Quick check if data exists (without loading full data)
        /// </summary>
        public void Exists(string vehicleId, Action<bool, MeasurementSource> callback)
        {
            StartCoroutine(ExistsCoroutine(vehicleId, callback));
        }

        private IEnumerator ExistsCoroutine(string vehicleId, Action<bool, MeasurementSource> callback)
        {
            // Check server
            if (checkServerFirst && _isServerAvailable)
            {
                string url = $"{serverBaseUrl}/measurements/{vehicleId}/exists";

                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    request.timeout = 5;
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        callback?.Invoke(true, MeasurementSource.Server);
                        yield break;
                    }
                }
            }

            // Check local
            if (VehicleMeasurementStorage.Exists(vehicleId))
            {
                callback?.Invoke(true, MeasurementSource.Local);
                yield break;
            }

            callback?.Invoke(false, MeasurementSource.None);
        }

        #endregion

        #region Save Methods

        /// <summary>
        /// Save measurement data based on access level
        /// </summary>
        public void Save(SavedVehicleMeasurement data, string vehicleId, Action<bool, string> callback = null)
        {
            StartCoroutine(SaveCoroutine(data, vehicleId, callback));
        }

        private IEnumerator SaveCoroutine(SavedVehicleMeasurement data, string vehicleId, Action<bool, string> callback)
        {
            bool success = false;
            string message = "";

            switch (accessLevel)
            {
                case UserAccessLevel.Viewer:
                    // Viewers cannot save
                    success = false;
                    message = "You don't have permission to save measurements.";
                    Debug.LogWarning($"[Storage] Save denied - Viewer access level");
                    break;

                case UserAccessLevel.User:
                    // Users save to LOCAL only
                    if (allowLocalSave)
                    {
                        success = VehicleMeasurementStorage.SaveData(data, vehicleId);
                        message = success ? "Saved locally" : "Local save failed";
                        Debug.Log($"[Storage] Saved to LOCAL: {vehicleId} ({success})");
                    }
                    else
                    {
                        success = false;
                        message = "Local saving is disabled.";
                    }
                    break;

                case UserAccessLevel.Admin:
                    // Admins save to SERVER
                    if (_isServerAvailable)
                    {
                        yield return SaveToServerCoroutine(data, vehicleId, (serverSuccess) => {
                            success = serverSuccess;
                        });

                        if (success)
                        {
                            message = "Saved to server";
                            Debug.Log($"[Storage] Saved to SERVER: {vehicleId}");

                            // Also cache locally
                            VehicleMeasurementStorage.SaveData(data, vehicleId);
                        }
                        else
                        {
                            // Fallback to local
                            success = VehicleMeasurementStorage.SaveData(data, vehicleId);
                            message = success ? "Server unavailable, saved locally" : "Save failed";
                        }
                    }
                    else
                    {
                        // Server down, save locally
                        success = VehicleMeasurementStorage.SaveData(data, vehicleId);
                        message = success ? "Server offline, saved locally" : "Save failed";
                    }
                    break;
            }

            OnSaveComplete?.Invoke(vehicleId, success);
            callback?.Invoke(success, message);
        }


        private IEnumerator SaveToServerCoroutine(SavedVehicleMeasurement data, string vehicleId, Action<bool> callback)
        {
            string url = $"{serverBaseUrl}/measurements/{vehicleId}";
            string json = JsonUtility.ToJson(data, true);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                // *** Add the session token from SignInManager ***
                SignInManager.AttachAuthHeader(request);  // <— NEW

                // Keep optional admin key if you use it elsewhere
                if (!string.IsNullOrEmpty(adminPassword))
                    request.SetRequestHeader("X-Admin-Key", adminPassword);

                request.timeout = (int)requestTimeout;
                yield return request.SendWebRequest();

                // Helpful logging to see real HTTP status instead of generic fallback
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[Storage] Server save failed: HTTP {request.responseCode}, {request.error}, body: {request.downloadHandler.text}");
                }

                callback?.Invoke(request.result == UnityWebRequest.Result.Success);
            }
        }


        #endregion

        #region List Methods

        /// <summary>
        /// Get combined list from server and local
        /// </summary>
        public void GetAllVehicles(Action<List<VehicleListItem>> callback)
        {
            StartCoroutine(GetAllVehiclesCoroutine(callback));
        }

        private IEnumerator GetAllVehiclesCoroutine(Action<List<VehicleListItem>> callback)
        {
            var combined = new Dictionary<string, VehicleListItem>();

            // 1. Get from server
            if (_isServerAvailable)
            {
                yield return GetServerListCoroutine((serverList) => {
                    if (serverList != null)
                    {
                        foreach (var item in serverList)
                        {
                            combined[item.vehicleId] = new VehicleListItem
                            {
                                vehicleId = item.vehicleId,
                                vehicleName = item.vehicleName,
                                manufacturer = item.manufacturer,
                                source = MeasurementSource.Server,
                                isReadOnly = (accessLevel != UserAccessLevel.Admin)
                            };
                        }
                    }
                });
            }

            // 2. Get from local (add if not already from server)
            var localList = VehicleMeasurementStorage.GetSavedVehicleList();
            foreach (var item in localList)
            {
                if (!combined.ContainsKey(item.vehicleId))
                {
                    combined[item.vehicleId] = new VehicleListItem
                    {
                        vehicleId = item.vehicleId,
                        vehicleName = item.vehicleName,
                        manufacturer = item.manufacturer,
                        source = MeasurementSource.Local,
                        isReadOnly = false
                    };
                }
            }

            callback?.Invoke(new List<VehicleListItem>(combined.Values));
        }

        private IEnumerator GetServerListCoroutine(Action<List<SavedVehicleInfo>> callback)
        {
            string url = $"{serverBaseUrl}/measurements";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)requestTimeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var wrapper = JsonUtility.FromJson<VehicleListWrapper>(request.downloadHandler.text);
                        callback?.Invoke(wrapper.vehicles);
                    }
                    catch
                    {
                        callback?.Invoke(null);
                    }
                }
                else
                {
                    callback?.Invoke(null);
                }
            }
        }

        #endregion

        #region Admin Methods

        /// <summary>
        /// Set access level (for runtime switching)
        /// </summary>
        public void SetAccessLevel(UserAccessLevel level)
        {
            accessLevel = level;
            Debug.Log($"[Storage] Access level set to: {level}");
        }

        /// <summary>
        /// Authenticate as admin
        /// </summary>
        public void AuthenticateAdmin(string password, Action<bool> callback)
        {
            StartCoroutine(AuthenticateAdminCoroutine(password, callback));
        }

        private IEnumerator AuthenticateAdminCoroutine(string password, Action<bool> callback)
        {
            string url = $"{serverBaseUrl}/auth/verify";

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-Admin-Key", password);
                request.timeout = 5;
                yield return request.SendWebRequest();

                bool success = request.result == UnityWebRequest.Result.Success;

                if (success)
                {
                    adminPassword = password;
                    accessLevel = UserAccessLevel.Admin;
                    Debug.Log("[Storage] Admin authenticated");
                }

                callback?.Invoke(success);
            }
        }

        /// <summary>
        /// Upload local data to server (Admin only)
        /// </summary>
        public void UploadToServer(string vehicleId, Action<bool> callback = null)
        {
            if (accessLevel != UserAccessLevel.Admin)
            {
                Debug.LogWarning("[Storage] Upload denied - Admin access required");
                callback?.Invoke(false);
                return;
            }

            var data = VehicleMeasurementStorage.Load(vehicleId);
            if (data != null)
            {
                StartCoroutine(SaveToServerCoroutine(data, vehicleId, callback));
            }
            else
            {
                callback?.Invoke(false);
            }
        }

        #endregion

        #region Delete Methods

        /// <summary>
        /// Delete measurement (respects access level)
        /// </summary>
        public void Delete(string vehicleId, MeasurementSource source, Action<bool> callback = null)
        {
            if (accessLevel == UserAccessLevel.Viewer)
            {
                Debug.LogWarning("[Storage] Delete denied - Viewer access");
                callback?.Invoke(false);
                return;
            }

            if (source == MeasurementSource.Server && accessLevel != UserAccessLevel.Admin)
            {
                Debug.LogWarning("[Storage] Delete server data denied - Admin access required");
                callback?.Invoke(false);
                return;
            }

            StartCoroutine(DeleteCoroutine(vehicleId, source, callback));
        }

        private IEnumerator DeleteCoroutine(string vehicleId, MeasurementSource source, Action<bool> callback)
        {
            bool success = false;

            if (source == MeasurementSource.Server && _isServerAvailable)
            {
                string url = $"{serverBaseUrl}/measurements/{vehicleId}";

                using (UnityWebRequest request = UnityWebRequest.Delete(url))
                {
                    if (!string.IsNullOrEmpty(adminPassword))
                        request.SetRequestHeader("X-Admin-Key", adminPassword);

                    request.timeout = (int)requestTimeout;
                    yield return request.SendWebRequest();

                    success = request.result == UnityWebRequest.Result.Success;
                }
            }
            else
            {
                success = VehicleMeasurementStorage.Delete(vehicleId);
            }

            callback?.Invoke(success);
        }

        #endregion
    }

    #region Enums & Data Classes

    public enum UserAccessLevel
    {
        Viewer,  // Can only view server data
        User,    // Can measure new, save locally
        Admin    // Can measure and save to server
    }

    public enum MeasurementSource
    {
        None,
        Local,
        Server
    }

    [Serializable]
    public class MeasurementCheckResult
    {
        public string VehicleId;
        public bool Found;
        public MeasurementSource Source;
        public SavedVehicleMeasurement Data;
        public bool IsReadOnly;
        public bool CanMeasure = true;
    }

    [Serializable]
    public class VehicleListItem
    {
        public string vehicleId;
        public string vehicleName;
        public string manufacturer;
        public MeasurementSource source;
        public bool isReadOnly;
    }

    /// <summary>
    /// Wrapper for JSON array deserialization from server
    /// </summary>
    [Serializable]
    public class VehicleListWrapper
    {
        public List<SavedVehicleInfo> vehicles = new List<SavedVehicleInfo>();
    }

    #endregion
}
