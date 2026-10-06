using System;
using System.Collections.Generic;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// One AssetBundle file in Unity's cache, identified the same way Unity's own cache
    /// identifies it: bundle name + hash. The same name with a different hash is a
    /// DIFFERENT file (a different version of that bundle).
    /// </summary>
    [Serializable]
    public class BundleRef
    {
        public string name;
        public string hash;
        public long size;     // bytes, as reported by the catalog

        public BundleRef() { }
        public BundleRef(string name, string hash, long size)
        {
            this.name = name; this.hash = hash; this.size = size;
        }

        /// <summary>Identity used everywhere bundles are compared or counted.</summary>
        public string Key { get { return (name ?? "") + "|" + (hash ?? ""); } }
    }

    /// <summary>
    /// Everything the app knows about ONE downloaded vehicle. This is the single local
    /// record that Home, the measurement scene and the loader should all read, replacing
    /// the old downloaded_vehicles.json list (which stored no sizes or file lists).
    /// </summary>
    [Serializable]
    public class VehicleRecord
    {
        public string vehicleId;
        public string vehicleName;
        public string addressableKey;

        /// <summary>The catalog.json "version" text at download time. Display only: whether an
        /// update exists is decided from the actual files (bundles), never from this text.</summary>
        public string installedVersion;

        /// <summary>Which catalog snapshot the files were downloaded with ("default" for installs
        /// adopted from the old tracker). A vehicle keeps loading from this snapshot until the user
        /// updates it.</summary>
        public string catalogId = "default";

        public long downloadedAtUtcTicks;
        public long lastOpenedUtcTicks;

        /// <summary>False for installs adopted from the old tracker, whose files have not been
        /// scanned yet. Their status is "Unknown" and they are never used to decide what is
        /// safe to delete.</summary>
        public bool bundlesKnown;

        /// <summary>True if the old tracker stored a file path instead of a vehicle id, so the
        /// real id still has to be matched up from the catalog.</summary>
        public bool needsIdReconcile;

        public List<BundleRef> bundles = new List<BundleRef>();
        public string thumbnailFile;

        /// <summary>An independent copy, so code that reasons about records can't alter the originals by accident.</summary>
        public VehicleRecord Clone()
        {
            var copy = (VehicleRecord)MemberwiseClone();
            copy.bundles = new List<BundleRef>();
            if (bundles != null)
                foreach (var b in bundles)
                    if (b != null) copy.bundles.Add(new BundleRef(b.name, b.hash, b.size));
            return copy;
        }

        public long TotalBytes()
        {
            long sum = 0;
            var seen = new HashSet<string>();
            if (bundles != null)
                foreach (var b in bundles)
                    if (b != null && seen.Add(b.Key)) sum += Math.Max(0, b.size);
            return sum;
        }

        public DateTime? DownloadedAtUtc
        {
            get { return downloadedAtUtcTicks > 0 ? (DateTime?)new DateTime(downloadedAtUtcTicks, DateTimeKind.Utc) : null; }
        }

        public DateTime? LastOpenedUtc
        {
            get { return lastOpenedUtcTicks > 0 ? (DateTime?)new DateTime(lastOpenedUtcTicks, DateTimeKind.Utc) : null; }
        }
    }

    /// <summary>What the server's newest catalog says about one vehicle.</summary>
    public class LatestVehicleInfo
    {
        public string version;
        /// <summary>False if the newest catalog no longer lists this vehicle at all.</summary>
        public bool existsInCatalog = true;
        public List<BundleRef> bundles = new List<BundleRef>();
    }
}
