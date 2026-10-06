using System;
using System.Collections.Generic;
using System.Linq;

namespace VehicleMeasurement.Storage
{
    /// <summary>How much disk one vehicle accounts for.</summary>
    public class VehicleUsage
    {
        public string vehicleId;
        public string vehicleName;
        /// <summary>All of this vehicle's files.</summary>
        public long totalBytes;
        /// <summary>Files only this vehicle uses: what you get back by removing it.</summary>
        public long exclusiveBytes;
        /// <summary>Files other vehicles use too: removing this vehicle does not free them.</summary>
        public long sharedBytes;
        public List<string> sharedWithVehicles = new List<string>();
        public bool bundlesKnown;
    }

    /// <summary>
    /// A precise list of cache files that are safe to delete, and why the others are kept.
    /// Nothing is ever deleted unless no remaining vehicle still needs it.
    /// </summary>
    public class DeletionPlan
    {
        public bool vehicleFound;
        public string vehicleId;
        public List<BundleRef> delete = new List<BundleRef>();
        public List<BundleRef> keep = new List<BundleRef>();
        public long freedBytes;
        public long keptSharedBytes;
        public List<string> sharedWithVehicles = new List<string>();
        /// <summary>When true the plan deletes nothing: we cannot yet prove it is safe.</summary>
        public bool blocked;
        public string blockedReason;
    }

    public static class StoragePlanner
    {
        /// <summary>Per-vehicle disk usage, split into exclusive and shared.</summary>
        public static List<VehicleUsage> Usage(IList<VehicleRecord> records)
        {
            var refs = BuildRefs(records);
            var result = new List<VehicleUsage>();
            foreach (var rec in records)
            {
                if (rec == null) continue;
                var u = new VehicleUsage { vehicleId = rec.vehicleId, vehicleName = rec.vehicleName ?? rec.vehicleId, bundlesKnown = rec.bundlesKnown };
                var sharers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var b in Distinct(rec.bundles))
                {
                    long size = Math.Max(0, b.size);
                    u.totalBytes += size;
                    List<VehicleRecord> users = refs[b.Key];
                    if (users.Count <= 1) u.exclusiveBytes += size;
                    else
                    {
                        u.sharedBytes += size;
                        foreach (var other in users)
                            if (!SameId(other.vehicleId, rec.vehicleId)) sharers.Add(other.vehicleName ?? other.vehicleId);
                    }
                }
                u.sharedWithVehicles = sharers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                result.Add(u);
            }
            return result;
        }

        /// <summary>Real disk use of all downloaded vehicles: a file shared by ten vehicles counts once.</summary>
        public static long TotalUniqueBytes(IList<VehicleRecord> records)
        {
            var seen = new HashSet<string>();
            long sum = 0;
            foreach (var rec in records)
            {
                if (rec == null) continue;
                foreach (var b in Distinct(rec.bundles))
                    if (seen.Add(b.Key)) sum += Math.Max(0, b.size);
            }
            return sum;
        }

        /// <summary>What deleting this vehicle may remove from the cache.</summary>
        public static DeletionPlan PlanRemoval(IList<VehicleRecord> records, string vehicleId)
        {
            var plan = new DeletionPlan { vehicleId = vehicleId };
            var target = Find(records, vehicleId);
            if (target == null) return plan;
            plan.vehicleFound = true;

            if (!target.bundlesKnown)
                return Block(plan, "This vehicle's files haven't been scanned yet.");
            if (OthersUnscanned(records, vehicleId))
                return Block(plan, "Some other downloaded vehicles haven't been scanned yet, so shared files can't be checked safely.");

            Partition(records, vehicleId, target.bundles, plan);
            return plan;
        }

        /// <summary>
        /// After a vehicle is switched to a newer set of files, which of its OLD files can go?
        /// A file is only deletable if the new set doesn't use it and no other vehicle does.
        /// </summary>
        public static DeletionPlan PlanReplace(IList<VehicleRecord> records, string vehicleId, IList<BundleRef> newBundles)
        {
            var plan = new DeletionPlan { vehicleId = vehicleId };
            var target = Find(records, vehicleId);
            if (target == null) return plan;
            plan.vehicleFound = true;

            if (!target.bundlesKnown)
                return Block(plan, "This vehicle's files haven't been scanned yet.");
            if (OthersUnscanned(records, vehicleId))
                return Block(plan, "Some other downloaded vehicles haven't been scanned yet, so shared files can't be checked safely.");

            var newKeys = new HashSet<string>(Distinct(newBundles).Select(b => b.Key));
            var oldOnly = Distinct(target.bundles).Where(b => !newKeys.Contains(b.Key)).ToList();
            Partition(records, vehicleId, oldOnly, plan);
            return plan;
        }

        /// <summary>Everything downloaded, for a full fresh start.</summary>
        public static DeletionPlan PlanRemoveAll(IList<VehicleRecord> records)
        {
            var plan = new DeletionPlan { vehicleFound = true };
            var seen = new HashSet<string>();
            foreach (var rec in records)
            {
                if (rec == null) continue;
                foreach (var b in Distinct(rec.bundles))
                    if (seen.Add(b.Key)) { plan.delete.Add(b); plan.freedBytes += Math.Max(0, b.size); }
            }
            return plan;
        }

        // ── helpers ─────────────────────────────────────────────────────

        private static void Partition(IList<VehicleRecord> records, string vehicleId, IEnumerable<BundleRef> candidates, DeletionPlan plan)
        {
            var refs = BuildRefs(records);
            var sharers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Distinct(candidates))
            {
                var others = refs[b.Key].Where(r => !SameId(r.vehicleId, vehicleId)).ToList();
                if (others.Count == 0)
                {
                    plan.delete.Add(b);
                    plan.freedBytes += Math.Max(0, b.size);
                }
                else
                {
                    plan.keep.Add(b);
                    plan.keptSharedBytes += Math.Max(0, b.size);
                    foreach (var o in others) sharers.Add(o.vehicleName ?? o.vehicleId);
                }
            }
            plan.sharedWithVehicles = sharers.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static DeletionPlan Block(DeletionPlan plan, string reason)
        {
            plan.blocked = true;
            plan.blockedReason = reason;
            return plan;
        }

        private static bool OthersUnscanned(IList<VehicleRecord> records, string vehicleId)
        {
            foreach (var r in records)
                if (r != null && !SameId(r.vehicleId, vehicleId) && !r.bundlesKnown) return true;
            return false;
        }

        private static VehicleRecord Find(IList<VehicleRecord> records, string id)
        {
            foreach (var r in records)
                if (r != null && SameId(r.vehicleId, id)) return r;
            return null;
        }

        /// <summary>bundle key -> the vehicles that use that file.</summary>
        private static Dictionary<string, List<VehicleRecord>> BuildRefs(IList<VehicleRecord> records)
        {
            var refs = new Dictionary<string, List<VehicleRecord>>();
            foreach (var rec in records)
            {
                if (rec == null) continue;
                foreach (var b in Distinct(rec.bundles))
                {
                    List<VehicleRecord> list;
                    if (!refs.TryGetValue(b.Key, out list)) { list = new List<VehicleRecord>(); refs[b.Key] = list; }
                    list.Add(rec);
                }
            }
            return refs;
        }

        private static IEnumerable<BundleRef> Distinct(IEnumerable<BundleRef> list)
        {
            var seen = new HashSet<string>();
            if (list == null) yield break;
            foreach (var b in list)
                if (b != null && seen.Add(b.Key)) yield return b;
        }

        private static bool SameId(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
