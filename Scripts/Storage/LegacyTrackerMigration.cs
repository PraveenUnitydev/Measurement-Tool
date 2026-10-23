using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    [Serializable] public class LegacyTrackerEntry
    {
        public string vehicleId, vehicleName, addressableKey, manufacturer, thumbnailUrl, category, downloadedDate, loaderType, version;
        public bool hasVALData;
    }
    [Serializable] public class LegacyTrackerData { public List<LegacyTrackerEntry> vehicles = new List<LegacyTrackerEntry>(); }

    public class MigrationReport
    {
        public int imported;
        public int skippedEmptyId;
        public int duplicatesMerged;
        public List<string> needReconcile = new List<string>();
    }

    /// <summary>
    /// Brings vehicles recorded by the old downloaded_vehicles.json into the registry, once.
    /// Their files are not known yet, so they are imported as "unscanned" (status Unknown) and
    /// are never used to decide what is safe to delete until a scan fills in their file lists.
    /// </summary>
    public static class LegacyTrackerMigration
    {
        public static MigrationReport Import(string trackerJson, VehicleRegistry registry)
        {
            var report = new MigrationReport();
            if (string.IsNullOrWhiteSpace(trackerJson)) return report;

            LegacyTrackerData data;
            try { data = JsonUtility.FromJson<LegacyTrackerData>(trackerJson); }
            catch { return report; }
            if (data == null || data.vehicles == null) return report;

            // The old file could hold the same vehicle twice; the last entry wins.
            var byId = new Dictionary<string, LegacyTrackerEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in data.vehicles)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.vehicleId)) { report.skippedEmptyId++; continue; }
                string id = e.vehicleId.Trim();
                if (byId.ContainsKey(id)) report.duplicatesMerged++;
                byId[id] = e;
            }

            foreach (var kv in byId)
            {
                if (registry.Get(kv.Key) != null) continue;      // never overwrite something already known
                var e = kv.Value;
                bool pathLike = kv.Key.IndexOf('/') >= 0 || kv.Key.IndexOf('\\') >= 0;
                var rec = new VehicleRecord
                {
                    vehicleId = kv.Key,
                    vehicleName = string.IsNullOrEmpty(e.vehicleName) ? kv.Key : e.vehicleName,
                    addressableKey = e.addressableKey,
                    installedVersion = e.version ?? "",
                    catalogId = "default",
                    downloadedAtUtcTicks = ParseLocalDate(e.downloadedDate),
                    bundlesKnown = false,
                    needsIdReconcile = pathLike,
                };
                if (pathLike) report.needReconcile.Add(kv.Key);
                if (registry.Upsert(rec)) report.imported++;
            }
            return report;
        }

        public static long ParseLocalDate(string s)
        {
            DateTime dt;
            if (!string.IsNullOrEmpty(s) &&
                DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt.ToUniversalTime().Ticks;
            return 0;
        }
    }
}
