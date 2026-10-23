using System;
using System.Collections.Generic;
using System.Linq;

namespace VehicleMeasurement.Storage
{
    /// <summary>One vehicle as the newest catalog describes it, plus the files it needs.</summary>
    public class CatalogVehicle
    {
        public string vehicleId;
        public string vehicleName;
        public string addressableKey;
        public string version;
        /// <summary>False if the newest catalog has no entry for this vehicle's key.</summary>
        public bool existsInCatalog = true;
        public List<BundleRef> bundles = new List<BundleRef>();
    }

    public class ReconcileResult
    {
        public List<VehicleRecord> records = new List<VehicleRecord>();
        /// <summary>Vehicles found on disk that the registry didn't know about.</summary>
        public List<string> adopted = new List<string>();
        /// <summary>Records removed because their main file is no longer on disk.</summary>
        public List<string> dropped = new List<string>();
        /// <summary>Records whose id or file list was corrected.</summary>
        public List<string> repaired = new List<string>();

        public bool Changed { get { return adopted.Count > 0 || dropped.Count > 0 || repaired.Count > 0; } }
    }

    /// <summary>
    /// Makes the registry match reality. The Unity cache is the truth: a vehicle counts as downloaded only if its
    /// MAIN (largest) file is physically there. The old downloaded_vehicles.json is used only to carry over the
    /// download date and version text for vehicles that really are on disk - never to decide that one is.
    ///
    /// This is what fixes Home showing vehicles as downloaded after the cache was cleared (the old list was left
    /// behind): such a vehicle is simply not downloaded.
    /// </summary>
    public static class StorageReconciler
    {
        public static ReconcileResult Reconcile(
            IList<VehicleRecord> current,
            IList<CatalogVehicle> catalog,
            IDictionary<string, LegacyTrackerEntry> legacy,
            Func<BundleRef, bool> isCached)
        {
            if (isCached == null) throw new ArgumentNullException("isCached");
            var result = new ReconcileResult();
            // Work on copies: deciding what to keep must never rewrite the caller's records
            current = (current ?? new List<VehicleRecord>()).Where(r => r != null).Select(r => r.Clone()).ToList();
            catalog = catalog ?? new List<CatalogVehicle>();

            // Records that have been dealt with, so leftovers can be handled at the end
            var handled = new HashSet<VehicleRecord>();
            var outputIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in catalog)
            {
                if (c == null || string.IsNullOrWhiteSpace(c.vehicleId)) continue;
                string id = c.vehicleId.Trim();
                if (!outputIds.Add(id)) continue;                      // duplicate catalog id: first one wins

                VehicleRecord rec = FindById(current, id);
                if (rec == null)
                {
                    // The old list sometimes stored a file path instead of the vehicle id: match on the addressable key
                    rec = current.FirstOrDefault(r => r != null && r.needsIdReconcile && !handled.Contains(r)
                        && !string.IsNullOrEmpty(r.addressableKey)
                        && string.Equals(r.addressableKey, c.addressableKey, StringComparison.OrdinalIgnoreCase));
                    if (rec != null) { rec.vehicleId = id; rec.needsIdReconcile = false; result.repaired.Add(id); }
                }
                if (rec != null) handled.Add(rec);

                BundleRef latestMain = c.existsInCatalog ? MainFile(c.bundles) : null;
                bool latestMainCached = latestMain != null && isCached(latestMain);

                if (rec != null && rec.bundlesKnown)
                {
                    BundleRef recordedMain = MainFile(rec.bundles);
                    if (recordedMain != null && isCached(recordedMain))
                    {
                        Refresh(rec, c);                               // still there: keep as is
                    }
                    else if (latestMainCached)
                    {
                        rec.bundles = Clone(c.bundles);                // the files that are there are the newest ones
                        Refresh(rec, c);
                        result.repaired.Add(id);
                    }
                    else
                    {
                        result.dropped.Add(id);                        // files are gone: not downloaded any more
                        outputIds.Remove(id);
                        continue;
                    }
                    result.records.Add(rec);
                }
                else if (latestMainCached)
                {
                    result.records.Add(Adopt(rec, c, legacy));         // on disk but not in the registry yet (or unscanned)
                    result.adopted.Add(id);
                }
                else
                {
                    if (rec != null) result.dropped.Add(id);           // an old entry whose files are gone
                    outputIds.Remove(id);
                }
            }

            // Records for vehicles the newest catalog doesn't list: keep them only while their files really exist,
            // so they can still be seen and removed on the Storage screen.
            foreach (var r in current)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.vehicleId) || handled.Contains(r)) continue;
                BundleRef main = MainFile(r.bundles);
                if (r.bundlesKnown && main != null && isCached(main)) result.records.Add(r);
                else result.dropped.Add(r.vehicleId.Trim());
            }
            return result;
        }

        /// <summary>The vehicle's main file: its largest. The small ones are shared scripts and the like.</summary>
        public static BundleRef MainFile(IList<BundleRef> bundles)
        {
            BundleRef best = null;
            if (bundles == null) return null;
            foreach (var b in bundles)
                if (b != null && (best == null || b.size > best.size)) best = b;
            return best;
        }

        private static VehicleRecord Adopt(VehicleRecord existing, CatalogVehicle c, IDictionary<string, LegacyTrackerEntry> legacy)
        {
            var rec = existing ?? new VehicleRecord { vehicleId = c.vehicleId.Trim() };
            LegacyTrackerEntry old = null;
            if (legacy != null && !legacy.TryGetValue(rec.vehicleId, out old)) old = null;

            rec.vehicleName = !string.IsNullOrEmpty(c.vehicleName) ? c.vehicleName : (rec.vehicleName ?? rec.vehicleId);
            rec.addressableKey = c.addressableKey;
            rec.catalogId = string.IsNullOrEmpty(rec.catalogId) ? "default" : rec.catalogId;
            rec.bundles = Clone(c.bundles);
            rec.bundlesKnown = true;
            rec.needsIdReconcile = false;
            if (rec.downloadedAtUtcTicks <= 0 && old != null) rec.downloadedAtUtcTicks = LegacyTrackerMigration.ParseLocalDate(old.downloadedDate);
            if (string.IsNullOrEmpty(rec.installedVersion))
                rec.installedVersion = old != null && !string.IsNullOrEmpty(old.version) ? old.version : (c.version ?? "");
            return rec;
        }

        private static void Refresh(VehicleRecord rec, CatalogVehicle c)
        {
            if (!string.IsNullOrEmpty(c.vehicleName)) rec.vehicleName = c.vehicleName;
            if (!string.IsNullOrEmpty(c.addressableKey)) rec.addressableKey = c.addressableKey;
        }

        private static VehicleRecord FindById(IList<VehicleRecord> list, string id)
        {
            VehicleRecord found = null;
            foreach (var r in list)
                if (r != null && string.Equals((r.vehicleId ?? "").Trim(), id, StringComparison.OrdinalIgnoreCase)) found = r;
            return found;
        }

        private static List<BundleRef> Clone(IList<BundleRef> src)
        {
            var list = new List<BundleRef>();
            if (src != null) foreach (var b in src) if (b != null) list.Add(new BundleRef(b.name, b.hash, b.size));
            return list;
        }
    }
}
