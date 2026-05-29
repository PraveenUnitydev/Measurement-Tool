using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Handles saving and loading vehicle measurements to/from JSON files
    /// 
    /// Storage location: Application.persistentDataPath/VehicleMeasurements/
    /// </summary>
    public static class VehicleMeasurementStorage
    {
        private const string FOLDER_NAME = "VehicleMeasurements";
        private const string FILE_EXTENSION = ".json";

        /// <summary>
        /// Get the storage folder path
        /// </summary>
        public static string StoragePath
        {
            get
            {
                string path = Path.Combine(Application.persistentDataPath, FOLDER_NAME);
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }

        #region Save Methods

        /// <summary>
        /// Save measurements from a VehicleMeasurementSystem
        /// </summary>
        public static bool Save(VehicleMeasurementSystem system, string vehicleId, string vehicleName = "")
        {
            if (system == null || !system.IsAnalyzed)
            {
                Debug.LogError("[Storage] System is null or not analyzed!");
                return false;
            }

            var data = new SavedVehicleMeasurement();
            data.CopyFromSystem(system);
            data.vehicleId = vehicleId;
            data.vehicleName = string.IsNullOrEmpty(vehicleName) ? vehicleId : vehicleName;
            data.savedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            data.lastModified = data.savedDate;

            var prefabData = system.GetComponent<VehiclePrefabData>();
            data.hasVALData = prefabData != null ? prefabData.hasVALData : true;

            return SaveData(data, vehicleId);
        }

        /// <summary>
        /// Save measurement data directly
        /// </summary>
        public static bool SaveData(SavedVehicleMeasurement data, string vehicleId)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                string filePath = GetFilePath(vehicleId);
                File.WriteAllText(filePath, json);

                Debug.Log($"[Storage] Saved: {filePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Save failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Load Methods

        /// <summary>
        /// Load measurements for a vehicle
        /// </summary>
        public static SavedVehicleMeasurement Load(string vehicleId)
        {
            string filePath = GetFilePath(vehicleId);

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[Storage] File not found: {filePath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var data = JsonUtility.FromJson<SavedVehicleMeasurement>(json);
                Debug.Log($"[Storage] Loaded: {vehicleId}");
                return data;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Load failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Check if saved data exists for a vehicle
        /// </summary>
        public static bool Exists(string vehicleId)
        {
            return File.Exists(GetFilePath(vehicleId));
        }

        /// <summary>
        /// Get list of all saved vehicle IDs
        /// </summary>
        public static string[] GetSavedVehicleIds()
        {
            Debug.Log($"[Storage] GetSavedVehicleIds - Looking in: {StoragePath}");

            if (!Directory.Exists(StoragePath))
            {
                Debug.Log("[Storage] Storage directory does not exist");
                return new string[0];
            }

            var files = Directory.GetFiles(StoragePath, "*" + FILE_EXTENSION);
            Debug.Log($"[Storage] Found {files.Length} files with extension {FILE_EXTENSION}");

            var ids = new List<string>();

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                Debug.Log($"[Storage] Found file: {fileName}");
                ids.Add(fileName);
            }

            return ids.ToArray();
        }

        /// <summary>
        /// Get list of all saved vehicles with their names
        /// </summary>
        public static List<SavedVehicleInfo> GetSavedVehicleList()
        {
            var list = new List<SavedVehicleInfo>();
            var ids = GetSavedVehicleIds();

            Debug.Log($"[Storage] GetSavedVehicleList - Processing {ids.Length} IDs");

            foreach (var id in ids)
            {
                Debug.Log($"[Storage] Loading vehicle ID (filename): {id}");
                var data = Load(id);
                if (data != null)
                {
                    // Use filename as vehicleId (more reliable), but get name from data
                    string vehicleName = !string.IsNullOrEmpty(data.vehicleName) ? data.vehicleName : id;

                    Debug.Log($"[Storage] Loaded vehicle: {vehicleName} (FileID: {id}, DataID: {data.vehicleId})");

                    list.Add(new SavedVehicleInfo
                    {
                        vehicleId = id,  // Use filename, not data.vehicleId
                        vehicleName = vehicleName,
                        savedDate = data.savedDate,
                        lastModified = data.lastModified,
                        manufacturer = data.manufacturer
                    });
                }
                else
                {
                    Debug.LogWarning($"[Storage] Failed to load data for ID: {id}");
                }
            }

            Debug.Log($"[Storage] Returning {list.Count} saved vehicles");
            return list;
        }

        #endregion

        #region Delete Methods

        /// <summary>
        /// Delete saved measurement file
        /// </summary>
        public static bool Delete(string vehicleId)
        {
            string filePath = GetFilePath(vehicleId);

            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[Storage] File not found (already deleted?): {vehicleId}");

                // Still try to delete thumbnail
                string thumbPath = GetThumbnailPath(vehicleId);
                if (File.Exists(thumbPath))
                {
                    try { File.Delete(thumbPath); } catch { }
                }

                return true;  // ← Return true since not existing is the desired state
            }

            try
            {
                File.Delete(filePath);

                // Also delete thumbnail
                string thumbPath = GetThumbnailPath(vehicleId);
                if (File.Exists(thumbPath))
                {
                    File.Delete(thumbPath);
                }

                Debug.Log($"[Storage] Deleted: {vehicleId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Delete failed: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Utility

        private static string GetFilePath(string vehicleId)
        {
            // Sanitize vehicle ID for file name
            string safeId = SanitizeFileName(vehicleId);
            return Path.Combine(StoragePath, safeId + FILE_EXTENSION);
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        /// <summary>
        /// Open storage folder in file explorer
        /// </summary>
        public static void OpenStorageFolder()
        {
            Application.OpenURL("file://" + StoragePath);
        }

        #endregion

        #region Thumbnail Methods

        /// <summary>
        /// Save thumbnail sprite to disk and return the local path
        /// </summary>
        public static string SaveThumbnail(string vehicleId, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            try
            {
                string thumbnailPath = GetThumbnailPath(vehicleId);

                // Convert sprite to PNG bytes
                Texture2D texture = sprite.texture;

                // Create a readable copy if needed
                Texture2D readableTexture;
                if (!texture.isReadable)
                {
                    RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height);
                    Graphics.Blit(texture, rt);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    readableTexture = new Texture2D(texture.width, texture.height);
                    readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    readableTexture.Apply();
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
                else
                {
                    readableTexture = texture;
                }

                byte[] pngBytes = readableTexture.EncodeToPNG();
                File.WriteAllBytes(thumbnailPath, pngBytes);

                Debug.Log($"[Storage] Saved thumbnail: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Failed to save thumbnail: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save thumbnail from Texture2D
        /// </summary>
        public static string SaveThumbnail(string vehicleId, Texture2D texture)
        {
            if (texture == null) return null;

            try
            {
                string thumbnailPath = GetThumbnailPath(vehicleId);
                byte[] pngBytes = texture.EncodeToPNG();
                File.WriteAllBytes(thumbnailPath, pngBytes);

                Debug.Log($"[Storage] Saved thumbnail: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Failed to save thumbnail: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load thumbnail from disk
        /// </summary>
        public static Sprite LoadThumbnail(string vehicleId)
        {
            string thumbnailPath = GetThumbnailPath(vehicleId);
            return LoadThumbnailFromPath(thumbnailPath);
        }

        /// <summary>
        /// Load thumbnail from specific path
        /// </summary>
        public static Sprite LoadThumbnailFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                byte[] pngBytes = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(pngBytes);

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );

                return sprite;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Storage] Failed to load thumbnail: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get thumbnail file path for a vehicle
        /// </summary>
        public static string GetThumbnailPath(string vehicleId)
        {
            return Path.Combine(StoragePath, SanitizeFileName(vehicleId) + "_thumb.png");
        }

        /// <summary>
        /// Check if thumbnail exists
        /// </summary>
        public static bool ThumbnailExists(string vehicleId)
        {
            return File.Exists(GetThumbnailPath(vehicleId));
        }

        #endregion
    }

    /// <summary>
    /// Serializable measurement data for JSON storage
    /// </summary>
    [Serializable]
    public class SavedVehicleMeasurement
    {
        [Header("Vehicle Info")]
        public string vehicleId;
        public string vehicleName;
        public string vehicleModel;
        public string manufacturer;
        public string category;
        public string notes;
        public bool hasVALData = true;

        [Header("Model Source (for reloading 3D model)")]
        public string modelPath;           // Addressable key or Resources path
        public string modelLoadType;       // "Addressables", "Resources", "SceneReference"
        public string addressableVehicleId; // ID in AddressableVehicleLoader catalog

        [Header("Thumbnail")]
        public string thumbnailPath;       // Local path to saved thumbnail
        public string thumbnailUrl;        // Remote URL (for re-downloading if needed)

        [Header("Timestamps")]
        public string savedDate;
        public string lastModified;

        [Header("Length (mm)")]
        public float L103_OverallLength;
        public float L101_Wheelbase;
        public float L104_FrontOverhang;
        public float L105_RearOverhang;

        [Header("Width (mm)")]
        public float W103_OverallWidth;
        public float W144_FrontTrack;
        public float W145_RearTrack;

        [Header("Height (mm)")]
        public float H100_OverallHeight;
        public float H101_GroundClearance;

        [Header("Wheels (mm)")]
        public float TD_F_FrontDiameter;
        public float TD_R_RearDiameter;

        [Header("Wheel Centers (mm)")]
        public SerializableVector3 WheelFL;
        public SerializableVector3 WheelFR;
        public SerializableVector3 WheelRL;
        public SerializableVector3 WheelRR;

        [Header("Bounds (mm)")]
        public SerializableVector3 BoundsMin;
        public SerializableVector3 BoundsMax;
        public SerializableVector3 BoundsCenter;

        /// <summary>
        /// Copy data from measurement system
        /// </summary>
        public void CopyFromSystem(VehicleMeasurementSystem system)
        {
            L103_OverallLength = system.L103_OverallLength;
            L101_Wheelbase = system.L101_Wheelbase;
            L104_FrontOverhang = system.L104_FrontOverhang;
            L105_RearOverhang = system.L105_RearOverhang;

            W103_OverallWidth = system.W103_OverallWidth;
            W144_FrontTrack = system.W144_FrontTrack;
            W145_RearTrack = system.W145_RearTrack;

            H100_OverallHeight = system.H100_OverallHeight;
            H101_GroundClearance = system.H101_GroundClearance;

            TD_F_FrontDiameter = system.TD_F_FrontDiameter;
            TD_R_RearDiameter = system.TD_R_RearDiameter;

            WheelFL = new SerializableVector3(system.WheelFL);
            WheelFR = new SerializableVector3(system.WheelFR);
            WheelRL = new SerializableVector3(system.WheelRL);
            WheelRR = new SerializableVector3(system.WheelRR);

            if (system.Results != null)
            {
                BoundsMin = new SerializableVector3(system.Results.BoundingMin * 1000f);
                BoundsMax = new SerializableVector3(system.Results.BoundingMax * 1000f);
                BoundsCenter = new SerializableVector3(system.Results.BoundingCenter * 1000f);
            }
        }
        public void CopyFromRemoteInfo(RemoteVehicleInfo info)
        {
            if (info == null) return;
            manufacturer = info.manufacturer;
            category = info.category;
        }

        /// <summary>
        /// Copy vehicle info from local catalog entry
        /// </summary>
        public void CopyFromAddressableInfo(VehicleAddressableInfo info)
        {
            if (info == null) return;
            manufacturer = info.manufacturer;
            category = info.category;
        }
        /// <summary>
        /// Set model source info for reloading
        /// </summary>
        public void SetModelSource(string path, ModelLoadType loadType, string addressableId = null)
        {
            modelPath = path;
            modelLoadType = loadType.ToString();
            addressableVehicleId = addressableId;
        }

        /// <summary>
        /// Get the model load type enum
        /// </summary>
        public ModelLoadType GetModelLoadType()
        {
            if (string.IsNullOrEmpty(modelLoadType))
                return ModelLoadType.Resources;

            if (Enum.TryParse<ModelLoadType>(modelLoadType, out var result))
                return result;

            return ModelLoadType.Resources;
        }

        /// <summary>
        /// Check if model source is available for reloading
        /// </summary>
        public bool HasModelSource()
        {
            return !string.IsNullOrEmpty(modelPath);
        }

        /// <summary>
        /// Get measurement by code
        /// </summary>
        public float GetValue(string code)
        {
            switch (code.ToUpper())
            {
                case "L103": case "LENGTH": return L103_OverallLength;
                case "L101": case "WHEELBASE": return L101_Wheelbase;
                case "L104": case "FRONTOVERHANG": return L104_FrontOverhang;
                case "L105": case "REAROVERHANG": return L105_RearOverhang;
                case "W103": case "WIDTH": return W103_OverallWidth;
                case "W144": case "FRONTTRACK": return W144_FrontTrack;
                case "W145": case "REARTRACK": return W145_RearTrack;
                case "H100": case "HEIGHT": return H100_OverallHeight;
                case "H101": case "GROUNDCLEARANCE": return H101_GroundClearance;
                case "TD_F": return TD_F_FrontDiameter;
                case "TD_R": return TD_R_RearDiameter;
                default: return 0f;
            }
        }

        /// <summary>
        /// Set measurement by code (for editing)
        /// </summary>
        public void SetValue(string code, float value)
        {
            switch (code.ToUpper())
            {
                case "L103": case "LENGTH": L103_OverallLength = value; break;
                case "L101": case "WHEELBASE": L101_Wheelbase = value; break;
                case "L104": case "FRONTOVERHANG": L104_FrontOverhang = value; break;
                case "L105": case "REAROVERHANG": L105_RearOverhang = value; break;
                case "W103": case "WIDTH": W103_OverallWidth = value; break;
                case "W144": case "FRONTTRACK": W144_FrontTrack = value; break;
                case "W145": case "REARTRACK": W145_RearTrack = value; break;
                case "H100": case "HEIGHT": H100_OverallHeight = value; break;
                case "H101": case "GROUNDCLEARANCE": H101_GroundClearance = value; break;
                case "TD_F": TD_F_FrontDiameter = value; break;
                case "TD_R": TD_R_RearDiameter = value; break;
            }
        }
    }

    /// <summary>
    /// Basic info about a saved vehicle (for list display)
    /// </summary>
    [Serializable]
    public class SavedVehicleInfo
    {
        public string vehicleId;
        public string vehicleName;
        public string manufacturer;
        public string savedDate;
        public string lastModified;

    }

    /// <summary>
    /// Serializable Vector3 for JSON
    /// </summary>
    [Serializable]
    public struct SerializableVector3
    {
        public float x, y, z;

        public SerializableVector3(Vector3 v)
        {
            x = v.x;
            y = v.y;
            z = v.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }
}
