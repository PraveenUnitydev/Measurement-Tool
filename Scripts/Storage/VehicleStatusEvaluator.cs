using System;
using System.Collections.Generic;

namespace VehicleMeasurement.Storage
{
    public enum VehicleStatus
    {
        /// <summary>Nothing downloaded for this vehicle.</summary>
        NotDownloaded,
        /// <summary>Downloaded, all files present, and nothing newer is known.</summary>
        UpToDate,
        /// <summary>Downloaded and usable as-is; a newer version exists on the server.</summary>
        UpdateAvailable,
        /// <summary>Some downloaded files are gone AND a newer version exists: the old copy can no
        /// longer open, so the vehicle must be updated before it can be used.</summary>
        UpdateRequired,
        /// <summary>Some downloaded files are gone (cache cleared, disk cleanup...). Same version
        /// can be downloaded again.</summary>
        FilesMissing,
        /// <summary>Installed before files were tracked; needs a one-time scan before we can judge.</summary>
        Unknown,
    }

    public class VehicleStatusInfo
    {
        public VehicleStatus status;
        /// <summary>Bytes still to download for the update / repair / first download.</summary>
        public long downloadBytes;
        public string installedVersion;
        public string availableVersion;
        /// <summary>False when the server could not be asked (offline): no update information.</summary>
        public bool updateCheckKnown;
        /// <summary>The newest catalog no longer lists this vehicle.</summary>
        public bool removedFromServer;

        /// <summary>Can the user open this vehicle right now?</summary>
        public bool CanOpen
        {
            get
            {
                return status == VehicleStatus.UpToDate
                    || status == VehicleStatus.UpdateAvailable
                    || status == VehicleStatus.Unknown;
            }
        }
    }

    /// <summary>
    /// One place that answers "is this vehicle downloaded, and is it current?".
    /// The answer comes from comparing the actual bundle files the vehicle was installed with
    /// against the files the newest catalog wants, plus whether they are really in the cache.
    /// It never relies on the catalog.json "version" text, which is why vehicles no longer show
    /// a permanent false "update available".
    /// </summary>
    public static class VehicleStatusEvaluator
    {
        /// <param name="installed">The local record, or null if never downloaded.</param>
        /// <param name="latest">What the newest catalog says, or null if the server could not be asked.</param>
        /// <param name="isCached">Whether a bundle (name+hash) is physically in the cache.</param>
        public static VehicleStatusInfo Evaluate(VehicleRecord installed, LatestVehicleInfo latest, Func<BundleRef, bool> isCached)
        {
            if (isCached == null) throw new ArgumentNullException("isCached");

            var info = new VehicleStatusInfo
            {
                availableVersion = latest != null ? latest.version : null,
                updateCheckKnown = latest != null,
                removedFromServer = latest != null && !latest.existsInCatalog,
            };

            if (installed == null)
            {
                info.status = VehicleStatus.NotDownloaded;
                if (latest != null && latest.existsInCatalog)
                    info.downloadBytes = UncachedBytes(latest.bundles, isCached);
                return info;
            }

            info.installedVersion = installed.installedVersion;

            if (!installed.bundlesKnown)
            {
                info.status = VehicleStatus.Unknown;
                return info;
            }

            bool filesMissing = AnyUncached(installed.bundles, isCached);
            bool latestListsVehicle = latest != null && latest.existsInCatalog;
            bool sameFiles = latestListsVehicle && SameBundles(installed.bundles, latest.bundles);

            if (filesMissing)
            {
                if (latestListsVehicle && !sameFiles)
                {
                    // The old copy is broken and a newer one exists: go straight to the newer one.
                    info.status = VehicleStatus.UpdateRequired;
                    info.downloadBytes = UncachedBytes(latest.bundles, isCached);
                }
                else
                {
                    info.status = VehicleStatus.FilesMissing;
                    info.downloadBytes = UncachedBytes(installed.bundles, isCached);
                }
                return info;
            }

            if (latestListsVehicle && !sameFiles)
            {
                info.status = VehicleStatus.UpdateAvailable;
                info.downloadBytes = UncachedBytes(latest.bundles, isCached);
                return info;
            }

            info.status = VehicleStatus.UpToDate;
            return info;
        }

        /// <summary>True when both lists describe exactly the same set of files (order and duplicates ignored).</summary>
        public static bool SameBundles(IList<BundleRef> a, IList<BundleRef> b)
        {
            var sa = KeySet(a);
            var sb = KeySet(b);
            return sa.SetEquals(sb);
        }

        private static HashSet<string> KeySet(IList<BundleRef> list)
        {
            var set = new HashSet<string>();
            if (list != null)
                foreach (var x in list)
                    if (x != null) set.Add(x.Key);
            return set;
        }

        private static long UncachedBytes(IList<BundleRef> list, Func<BundleRef, bool> isCached)
        {
            long sum = 0;
            var seen = new HashSet<string>();
            if (list == null) return 0;
            foreach (var b in list)
            {
                if (b == null || !seen.Add(b.Key)) continue;
                if (!isCached(b)) sum += Math.Max(0, b.size);
            }
            return sum;
        }

        private static bool AnyUncached(IList<BundleRef> list, Func<BundleRef, bool> isCached)
        {
            if (list == null) return false;
            foreach (var b in list)
                if (b != null && !isCached(b)) return true;
            return false;
        }
    }
}
