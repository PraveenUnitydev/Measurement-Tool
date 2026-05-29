using System;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Unified info for vehicle cards that can represent:
    /// 1. Saved vehicles (with measurements)
    /// 2. Downloaded vehicles (without measurements yet)
    /// </summary>
    [Serializable]
    public class VehicleCardInfo
    {
        public string vehicleId;
        public string vehicleName;
        public string addressableKey;
        public bool hasMeasurements;
        public bool isDownloaded;
        public string savedDate;
        public string lastModified;
        public string manufacturer;

        // For downloaded-only vehicles
        public string thumbnailUrl;
        public string category;
        public string description;
        public bool hasVALData;

        // Create from saved measurement
        public static VehicleCardInfo FromSavedVehicle(SavedVehicleInfo savedInfo, SavedVehicleMeasurement fullData,bool hasVALDataFromSource)
        {
            return new VehicleCardInfo
            {
                vehicleId = savedInfo.vehicleId,
                vehicleName = savedInfo.vehicleName,
                addressableKey = fullData?.modelPath,
                hasMeasurements = true,
                isDownloaded = true,
                savedDate = savedInfo.savedDate,
                lastModified = savedInfo.lastModified,
                manufacturer = fullData?.manufacturer,
                hasVALData = hasVALDataFromSource
            };
        }

        // Create from remote vehicle (downloaded but no measurements)
        public static VehicleCardInfo FromRemoteVehicle(RemoteVehicleInfo remoteInfo)
        {
            return new VehicleCardInfo
            {
                vehicleId = remoteInfo.vehicleId,
                vehicleName = remoteInfo.vehicleName,
                addressableKey = remoteInfo.addressableKey,
                hasMeasurements = false,
                isDownloaded = true,
                manufacturer = remoteInfo.manufacturer,
                thumbnailUrl = remoteInfo.thumbnailUrl,
                category = remoteInfo.category,
                description = remoteInfo.description,
                hasVALData = remoteInfo.hasVALData
            };
        }

        // Create from local vehicle (downloaded but no measurements)
        public static VehicleCardInfo FromLocalVehicle(VehicleAddressableInfo localInfo)
        {
            return new VehicleCardInfo
            {
                vehicleId = localInfo.vehicleId,
                vehicleName = localInfo.vehicleName,
                addressableKey = localInfo.addressableKey,
                hasMeasurements = false,
                isDownloaded = true,
                manufacturer = localInfo.manufacturer,
                thumbnailUrl = null,
                category = localInfo.category,
                description = localInfo.description,

                hasVALData = localInfo.hasVALData


            };
        }
    }
}
