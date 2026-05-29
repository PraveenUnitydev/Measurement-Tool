using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static UnityEngine.EventSystems.EventTrigger;

namespace VehicleMeasurement
{
    /// <summary>
    /// Tracks which vehicles have been downloaded (cached) by Addressables
    /// This allows us to show downloaded vehicles in the home screen even without measurements
    /// Supports both RemoteAddressableVehicleLoader and AddressableVehicleLoader
    /// </summary>
    public static class DownloadedVehiclesTracker
    {
        private const string TRACKER_FILE = "downloaded_vehicles.json";

        private static string TrackerPath => Path.Combine(Application.persistentDataPath, TRACKER_FILE);

        [Serializable]
        private class TrackerData
        {
            public List<DownloadedVehicleEntry> vehicles = new List<DownloadedVehicleEntry>();
        }

        [Serializable]
        private class DownloadedVehicleEntry
        {
            public string vehicleId;
            public string vehicleName;
            public string addressableKey;
            public string manufacturer;
            public string thumbnailUrl;
            public string category;
            public string downloadedDate;
            public string loaderType; // "Remote" or "Local"

            public string version;
            public bool hasVALData;

        }

        /// <summary>
        /// Mark a vehicle as downloaded from RemoteAddressableVehicleLoader
        /// </summary>
        public static void MarkAsDownloaded(RemoteVehicleInfo vehicleInfo)
        {
            if (vehicleInfo == null)
            {
                Debug.LogWarning("[DownloadTracker] Cannot mark as downloaded - vehicleInfo is null");
                return;
            }

            if (string.IsNullOrEmpty(vehicleInfo.vehicleId))
            {
                Debug.LogWarning("[DownloadTracker] Cannot mark as downloaded - vehicleId is null or empty");
                return;
            }

            try
            {
                var data = LoadTrackerData();

                // Remove existing entry if present
                data.vehicles.RemoveAll(v => v.vehicleId == vehicleInfo.vehicleId);

                // Add new entry
                data.vehicles.Add(new DownloadedVehicleEntry
                {
                    vehicleId = vehicleInfo.vehicleId,
                    vehicleName = vehicleInfo.vehicleName ?? vehicleInfo.vehicleId,
                    addressableKey = vehicleInfo.addressableKey ?? "",
                    manufacturer = vehicleInfo.manufacturer ?? "",
                    thumbnailUrl = vehicleInfo.thumbnailUrl ?? "",
                    category = vehicleInfo.category ?? "",
                    downloadedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    loaderType = "Remote",

                    version = vehicleInfo.version,
                    hasVALData = vehicleInfo.hasVALData

                });

                SaveTrackerData(data);
                Debug.Log($"[DownloadTracker] Marked as downloaded (Remote): {vehicleInfo.vehicleName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to mark as downloaded: {e.Message}");
            }
        }

        /// <summary>
        /// Mark a vehicle as downloaded from AddressableVehicleLoader
        /// </summary>
        public static void MarkAsDownloaded(VehicleAddressableInfo vehicleInfo)
        {
            if (vehicleInfo == null)
            {
                Debug.LogWarning("[DownloadTracker] Cannot mark as downloaded - vehicleInfo is null");
                return;
            }

            if (string.IsNullOrEmpty(vehicleInfo.vehicleId))
            {
                Debug.LogWarning("[DownloadTracker] Cannot mark as downloaded - vehicleId is null or empty");
                return;
            }

            try
            {
                var data = LoadTrackerData();

                // Remove existing entry if present
                data.vehicles.RemoveAll(v => v.vehicleId == vehicleInfo.vehicleId);

                // Add new entry
                data.vehicles.Add(new DownloadedVehicleEntry
                {
                    vehicleId = vehicleInfo.vehicleId,
                    vehicleName = vehicleInfo.vehicleName ?? vehicleInfo.vehicleId,
                    addressableKey = vehicleInfo.addressableKey ?? "",
                    manufacturer = vehicleInfo.manufacturer ?? "",
                    thumbnailUrl = null,
                    category = vehicleInfo.category ?? "",
                    downloadedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    loaderType = "Local",

                    version = null,
                    hasVALData = false

                });

                SaveTrackerData(data);
                Debug.Log($"[DownloadTracker] Marked as downloaded (Local): {vehicleInfo.vehicleName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to mark as downloaded: {e.Message}");
            }
        }

        /// <summary>
        /// Get all downloaded vehicles as RemoteVehicleInfo (universal format)
        /// </summary>
        public static List<RemoteVehicleInfo> GetDownloadedVehicles()
        {
            try
            {
                var data = LoadTrackerData();
                var result = new List<RemoteVehicleInfo>();

                foreach (var entry in data.vehicles)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.vehicleId))
                        continue;

                    result.Add(new RemoteVehicleInfo
                    {
                        vehicleId = entry.vehicleId,
                        vehicleName = entry.vehicleName ?? entry.vehicleId,
                        addressableKey = entry.addressableKey ?? "",
                        manufacturer = entry.manufacturer ?? "",
                        thumbnailUrl = entry.thumbnailUrl ?? "",
                        category = entry.category ?? ""
                    });
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to get downloaded vehicles: {e.Message}");
                return new List<RemoteVehicleInfo>();
            }
        }

        /// <summary>
        /// Get all downloaded vehicles as VehicleAddressableInfo (for local loader compatibility)
        /// </summary>
        public static List<VehicleAddressableInfo> GetDownloadedVehiclesLocal()
        {
            try
            {
                var data = LoadTrackerData();
                var result = new List<VehicleAddressableInfo>();

                foreach (var entry in data.vehicles)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.vehicleId))
                        continue;

                    result.Add(new VehicleAddressableInfo
                    {
                        vehicleId = entry.vehicleId,
                        vehicleName = entry.vehicleName ?? entry.vehicleId,
                        addressableKey = entry.addressableKey ?? "",
                        manufacturer = entry.manufacturer ?? "",
                        category = entry.category ?? "",
                        thumbnail = null // Will be loaded from loader if needed
                    });
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to get downloaded vehicles (local): {e.Message}");
                return new List<VehicleAddressableInfo>();
            }
        }

        /// <summary>
        /// Check if a vehicle has been downloaded
        /// </summary>
        public static bool IsDownloaded(string vehicleId)
        {
            var data = LoadTrackerData();
            return data.vehicles.Exists(v => v.vehicleId == vehicleId);
        }

        /// <summary>
        /// Remove a vehicle from the downloaded list
        /// </summary>
        /// 
        public static bool IsUpToDate(string vehicleId, string catalogVersion)
        {
            var data = LoadTrackerData();
            var entry = data.vehicles.Find(v => v.vehicleId == vehicleId);

            if (entry == null)
                return false;

            // If version missing, treat as outdated
            if (string.IsNullOrEmpty(entry.version))
                return false;

            return entry.version == catalogVersion;
        }
        public static bool TryGetDownloaded(
    string vehicleId,
    out string localVersion,
    out bool hasVALData
)
        {
            localVersion = null;
            hasVALData = false;

            var data = LoadTrackerData();
            var entry = data.vehicles.Find(v => v.vehicleId == vehicleId);

            if (entry == null)
                return false;

            localVersion = entry.version;
            hasVALData = entry.hasVALData;
            return true;
        }
        public static void RemoveDownloaded(string vehicleId)
        {
            var data = LoadTrackerData();
            data.vehicles.RemoveAll(v => v.vehicleId == vehicleId);
            SaveTrackerData(data);
        }

        public static void SetThumbnailPath(string vehicleId, string thumbnailPath)
        {
            try
            {
                var data = LoadTrackerData();

                var entry = data.vehicles.Find(v => v.vehicleId == vehicleId);
                if (entry != null)
                {
                    entry.thumbnailUrl = thumbnailPath;  // Reuse this field for local path
                    SaveTrackerData(data);
                    Debug.Log($"[DownloadTracker] Updated thumbnail path for: {vehicleId}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to set thumbnail path: {e.Message}");
            }
        }
        private static TrackerData LoadTrackerData()
        {
            if (!File.Exists(TrackerPath))
            {
                return new TrackerData();
            }

            try
            {
                string json = File.ReadAllText(TrackerPath);
                return JsonUtility.FromJson<TrackerData>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to load: {e.Message}");
                return new TrackerData();
            }
        }

        private static void SaveTrackerData(TrackerData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(TrackerPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DownloadTracker] Failed to save: {e.Message}");
            }
        }
    }
}
