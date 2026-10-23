using System;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Saved measurements written while a published vehicle was known by its Addressables address ("das/3xo", saved as
    /// das_3xo.json) are moved back to the vehicle's own key (3XO.json), so the vehicle has one set of measurements and
    /// they open it. Runs whenever the vehicle list goes live; does nothing when there's nothing to move.
    /// Never loses data: when both files exist, both are kept and the older-style one is only repaired so it opens.
    /// </summary>
    public static class MeasurementKeyMigration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Hook()
        {
            RemoteAddressableVehicleLoader.CatalogCommitted -= OnCommitted;
            RemoteAddressableVehicleLoader.CatalogCommitted += OnCommitted;
        }

        private static void OnCommitted(RemoteAddressableVehicleLoader loader) { Run(loader); }

        public static int Run(RemoteAddressableVehicleLoader loader)
        {
            if (loader == null) return 0;
            int changed = 0;
            string[] ids;
            try { ids = VehicleMeasurementStorage.GetSavedVehicleIds(); }
            catch (Exception e) { Debug.LogWarning("[Measurements] Could not list saved measurements: " + e.Message); return 0; }

            foreach (string fileId in ids)
            {
                try { if (MigrateOne(loader, fileId)) changed++; }
                catch (Exception e) { Debug.LogWarning("[Measurements] Could not check " + fileId + ": " + e.Message); }
            }
            if (changed > 0) Debug.Log("[Measurements] Repaired " + changed + " saved measurement file(s) of published vehicles.");
            return changed;
        }

        private static bool MigrateOne(RemoteAddressableVehicleLoader loader, string fileId)
        {
            bool fromAddress = fileId.StartsWith("das_", StringComparison.OrdinalIgnoreCase);
            var peek = VehicleMeasurementStorage.LoadForReading(fileId);
            if (peek == null) return false;
            bool pathIsAddress = DasKeys.IsPublishedAddress(peek.modelPath) || DasKeys.IsPublishedAddress(peek.addressableVehicleId);
            if (!fromAddress && !pathIsAddress) return false;

            RemoteVehicleInfo info = loader.GetVehicleInfo(peek.addressableVehicleId) ?? loader.GetVehicleInfo(peek.modelPath) ?? loader.GetVehicleInfo(fileId);
            if (info == null || string.IsNullOrEmpty(info.addressableKey) || DasKeys.IsPublishedAddress(info.addressableKey)) return false;

            string appKey = info.addressableKey;
            string target = VehicleIdentity.FileId(appKey);
            var data = VehicleMeasurementStorage.Load(fileId);           // a copy we may change
            if (data == null) return false;
            bool repaired = false;
            if (DasKeys.IsPublishedAddress(data.modelPath)) { data.modelPath = appKey; repaired = true; }
            if (DasKeys.IsPublishedAddress(data.addressableVehicleId)) { data.addressableVehicleId = appKey; repaired = true; }

            if (fromAddress && !string.Equals(target, fileId, StringComparison.OrdinalIgnoreCase) && !VehicleMeasurementStorage.Exists(target))
            {
                data.vehicleId = target;
                string oldThumb = VehicleMeasurementStorage.GetThumbnailPath(fileId);
                string newThumb = VehicleMeasurementStorage.GetThumbnailPath(target);
                if (!string.IsNullOrEmpty(data.thumbnailPath) && string.Equals(data.thumbnailPath, oldThumb, StringComparison.OrdinalIgnoreCase)) data.thumbnailPath = newThumb;
                if (!VehicleMeasurementStorage.SaveData(data, target)) return false;          // written safely first...
                try { if (System.IO.File.Exists(oldThumb) && !System.IO.File.Exists(newThumb)) System.IO.File.Move(oldThumb, newThumb); } catch (Exception) { }
                VehicleMeasurementStorage.Delete(fileId);                                         // ...then the old one goes
                Debug.Log("[Measurements] " + fileId + " -> " + target + " (" + info.vehicleName + ").");
                return true;
            }
            // Both exist (measured again meanwhile), or the name is already right: keep the file, make it open the vehicle
            return repaired && VehicleMeasurementStorage.SaveData(data, fileId);
        }
    }
}
