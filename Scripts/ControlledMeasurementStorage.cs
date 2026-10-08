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
        public string serverBaseUrl = DasServer.ApiBase;
        public float requestTimeout = 15f;

        [Header("═══ ACCESS CONTROL ═══")]
        public UserAccessLevel accessLevel = UserAccessLevel.User;

        [Tooltip("No longer used. Roles come from the DAS sign-in (VRSP dashboard > DAS tab).")]
        [HideInInspector] public string adminPassword = "";

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
                // A scene may still hold a retired or placeholder address: use the real one
                serverBaseUrl = DasServer.ResolveApiBase(serverBaseUrl, "ControlledMeasurementStorage.serverBaseUrl");
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // Restore the role from the saved DAS session (set by SignInManager)
            var savedRole = PlayerPrefs.GetString("session.role", "");
            if (!string.IsNullOrEmpty(savedRole))
                accessLevel = SignInManager.MapRoleToAccessLevel(savedRole);

            StartCoroutine(CheckServerConnection());
        }

        #endregion

        #region Server Connection

        private IEnumerator CheckServerConnection()
        {
            string url = $"{serverBaseUrl}/health";

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = UnityWebRequest.Get(url);
                    __req.timeout = 5;
                    return __req;
                }, r => request = r);
                using (request)
                {

                    bool wasAvailable = _isServerAvailable;
                    _isServerAvailable = request.result == UnityWebRequest.Result.Success;

                    if (wasAvailable != _isServerAvailable)
                        OnServerStatusChanged?.Invoke(_isServerAvailable);

                    Debug.Log($"[Storage] Server {(_isServerAvailable ? "ONLINE" : "OFFLINE")}");
                }
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

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = UnityWebRequest.Get(url);
                    SignInManager.AttachAuthHeader(__req);
                    __req.timeout = (int)requestTimeout;
                    return __req;
                }, r => request = r);
                using (request)
                {
                    if (SignInManager.HandleUnauthorized(request)) yield break;

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

                {
                    UnityWebRequest request = null;
                    yield return DasHttp.Send(() =>
                    {
                        var __req = UnityWebRequest.Get(url);
                        SignInManager.AttachAuthHeader(__req);
                        __req.timeout = 5;
                        return __req;
                    }, r => request = r);
                    using (request)
                    {
                        if (SignInManager.HandleUnauthorized(request)) yield break;

                        if (request.result == UnityWebRequest.Result.Success)
                        {
                            callback?.Invoke(true, MeasurementSource.Server);
                            yield break;
                        }
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

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = new UnityWebRequest(url, "POST");
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
                    __req.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    __req.downloadHandler = new DownloadHandlerBuffer();
                    __req.SetRequestHeader("Content-Type", "application/json");

                    SignInManager.AttachAuthHeader(__req);

                    __req.timeout = (int)requestTimeout;
                    return __req;
                }, r => request = r);
                using (request)
                {
                    if (SignInManager.HandleUnauthorized(request)) yield break;

                    // Helpful logging to see real HTTP status instead of generic fallback
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[Storage] Server save failed: HTTP {request.responseCode}, {request.error}, body: {request.downloadHandler.text}");
                    }

                    callback?.Invoke(request.result == UnityWebRequest.Result.Success);
                }
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

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = UnityWebRequest.Get(url);
                    SignInManager.AttachAuthHeader(__req);
                    __req.timeout = (int)requestTimeout;
                    return __req;
                }, r => request = r);
                using (request)
                {
                    if (SignInManager.HandleUnauthorized(request)) yield break;

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
        /// Re-checks the signed-in person's DAS role with the server.
        /// The password is ignored: roles are set by a VRSP admin in the DAS tab
        /// and come from the DAS sign-in, not from a shared admin password.
        /// </summary>
        [Obsolete("Roles come from the DAS sign-in (VRSP dashboard > DAS tab). The password is ignored.")]
        public void AuthenticateAdmin(string password, Action<bool> callback)
        {
            StartCoroutine(AuthenticateAdminCoroutine(password, callback));
        }

        [Serializable] private class RoleResponse { public string role; }

        private IEnumerator AuthenticateAdminCoroutine(string password, Action<bool> callback)
        {
            string url = $"{serverBaseUrl}/auth/verify";

            {
                UnityWebRequest request = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = UnityWebRequest.Get(url);
                    SignInManager.AttachAuthHeader(__req);
                    __req.timeout = 5;
                    return __req;
                }, r => request = r);
                using (request)
                {
                    if (SignInManager.HandleUnauthorized(request)) yield break;

                    bool success = false;
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var role = JsonUtility.FromJson<RoleResponse>(request.downloadHandler.text)?.role;
                        accessLevel = SignInManager.MapRoleToAccessLevel(role);
                        success = accessLevel == UserAccessLevel.Admin;
                    }

                    callback?.Invoke(success);
                }
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

                {
                    UnityWebRequest request = null;
                    yield return DasHttp.Send(() =>
                    {
                        var __req = UnityWebRequest.Delete(url);
                        __req.downloadHandler = new DownloadHandlerBuffer(); // so the server's reason can be read
                        SignInManager.AttachAuthHeader(__req);
                        __req.timeout = (int)requestTimeout;
                        return __req;
                    }, r => request = r);
                    using (request)
                    {
                        if (SignInManager.HandleUnauthorized(request)) yield break;

                        success = request.result == UnityWebRequest.Result.Success;
                    }
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
