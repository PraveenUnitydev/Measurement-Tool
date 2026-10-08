using System;
using System.IO;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Free-space checks before downloads. A vehicle is 30 MB to 1 GB; downloading onto a nearly full drive used to
    /// fail half-way with an unclear error (or fill the drive Windows runs on). Now a download is refused up front with
    /// a message that says how much is needed, how much is free, and what to do.
    /// </summary>
    public static class DiskSpace
    {
        /// <summary>Always left free on the download drive (Windows and other apps need room too).</summary>
        public const long KeepFreeBytes = 1024L * 1024 * 1024;

        /// <summary>Folder vehicle downloads are written to.</summary>
        public static string DownloadFolder
        {
            get
            {
                try { Cache c = Caching.currentCacheForWriting; if (c.valid) return c.path; } catch (Exception) { }
                return null;
            }
        }

        /// <summary>Free bytes on the drive holding <paramref name="path"/>, or -1 if it can't be read.</summary>
        public static long FreeBytes(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return -1;
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root)) return -1;
                var drive = new DriveInfo(root);
                return drive.IsReady ? drive.AvailableFreeSpace : -1;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>The drive letter/name of a path, for messages ("C:").</summary>
        public static string DriveName(string path)
        {
            try { return Path.GetPathRoot(Path.GetFullPath(path)).TrimEnd('\\', '/'); } catch (Exception) { return "the download drive"; }
        }

        /// <summary>Free bytes on the download drive, or -1.</summary>
        public static long FreeOnDownloadDrive() { return FreeBytes(DownloadFolder); }

        /// <summary>
        /// Null when <paramref name="bytesNeeded"/> fits (keeping <see cref="KeepFreeBytes"/> free); otherwise a message
        /// for the user. When the free space can't be read the download is allowed (Unity reports a real failure).
        /// </summary>
        public static string ProblemFor(long bytesNeeded)
        {
            string folder = DownloadFolder;
            long free = FreeBytes(folder);
            if (free < 0 || bytesNeeded <= 0) return null;
            if (free - bytesNeeded >= KeepFreeBytes) return null;
            string drive = DriveName(folder);
            return "Not enough free space on " + drive + ". This vehicle needs " + ByteFormat.Format(bytesNeeded) + " and "
                 + ByteFormat.Format(Math.Max(0, free)) + " is free (" + ByteFormat.Format(KeepFreeBytes) + " is always kept free for Windows). "
                 + "Free up space in Storage (remove vehicles or older versions), or choose a download folder on another drive.";
        }
    }
}
