using System; using System.Collections.Generic; using System.IO; using System.Linq;
using VehicleMeasurement.Storage;
class ReconcileTests {
  static int pass, fail;
  static void Check(string n, bool ok, string x = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + n + (x != "" ? "  [" + x + "]" : "")); }
  static BundleRef B(string n, string h, long s) { return new BundleRef(n, h, s); }
  static readonly BundleRef Scripts = B("monoscripts", "m1", 2);
  static CatalogVehicle Cat(string id, string main, long size, string key = null, string ver = "") {
    return new CatalogVehicle { vehicleId = id, vehicleName = id.ToUpper(), addressableKey = key ?? id, version = ver, bundles = new List<BundleRef> { B(main, "h1", size), Scripts } }; }
  static VehicleRecord Rec(string id, bool known, params BundleRef[] bs) { return new VehicleRecord { vehicleId = id, bundlesKnown = known, bundles = bs.ToList() }; }
  static Dictionary<string, LegacyTrackerEntry> Legacy(params LegacyTrackerEntry[] es) { var d = new Dictionary<string, LegacyTrackerEntry>(StringComparer.OrdinalIgnoreCase); foreach (var e in es) d[e.vehicleId] = e; return d; }

  static void Main() {
    var onDisk = new HashSet<string>();
    Func<BundleRef, bool> cached = b => onDisk.Contains(b.Key);

    Console.WriteLine("=== The situation in the real report ===");
    // 'syros' was re-downloaded; 'xuv' was downloaded in July but the cache was wiped; both are in the old list
    var catalog = new List<CatalogVehicle> { Cat("syros", "car_syros", 90000000, ver: "2026.03.25"), Cat("xuv", "car_xuv", 399000000, ver: "2026.03.25") };
    onDisk.Clear(); onDisk.Add("car_syros|h1"); onDisk.Add(Scripts.Key);          // xuv's files are gone; the shared scripts file remains
    var old = Legacy(new LegacyTrackerEntry { vehicleId = "syros", downloadedDate = "2026-10-06 17:08:03" },
                     new LegacyTrackerEntry { vehicleId = "xuv", downloadedDate = "2026-06-15 17:09:14", version = "2026.03.25" });
    var r = StorageReconciler.Reconcile(new List<VehicleRecord>(), catalog, old, cached);
    Check("Vehicle whose main file is on disk is adopted", r.records.Count == 1 && r.records[0].vehicleId == "syros" && r.adopted.SequenceEqual(new[] { "syros" }));
    Check("Vehicle listed in the old list but wiped from disk is NOT downloaded (only the shared file remains)", !r.records.Any(x => x.vehicleId == "xuv"));
    Check("Adopted record carries the real file list, marked as scanned", r.records[0].bundlesKnown && r.records[0].bundles.Count == 2 && r.records[0].TotalBytes() == 90000002);
    Check("Date comes from the old list; version falls back to the catalog's", r.records[0].DownloadedAtUtc.HasValue && r.records[0].installedVersion == "2026.03.25");
    Check("Old-list version wins over the catalog's when it has one", StorageReconciler.Reconcile(new List<VehicleRecord>(), new List<CatalogVehicle> { Cat("syros", "car_syros", 90, ver: "NEW") }, Legacy(new LegacyTrackerEntry { vehicleId = "syros", version = "OLD" }), cached).records[0].installedVersion == "OLD");
    Check("No old-list entry: still adopted, date unknown", StorageReconciler.Reconcile(new List<VehicleRecord>(), catalog, null, cached).records[0].DownloadedAtUtc == null);
    var before = new List<VehicleRecord> { Rec("syros", true, B("car_syros", "OLDHASH", 5)) };
    StorageReconciler.Reconcile(before, catalog, null, cached);
    Check("The records passed in are never modified (works on copies)", before[0].bundles[0].hash == "OLDHASH" && before[0].bundles.Count == 1);
    Check("Running it again changes nothing (stable)", !StorageReconciler.Reconcile(r.records, catalog, old, cached).Changed);

    Console.WriteLine("\n=== Keeping the registry honest over time ===");
    var recs = new List<VehicleRecord> { Rec("syros", true, B("car_syros", "h1", 90000000), Scripts) };
    onDisk.Clear(); onDisk.Add("car_syros|h1"); onDisk.Add(Scripts.Key);
    Check("Known record whose files are still there is kept", StorageReconciler.Reconcile(recs, catalog, null, cached).records.Count == 1);
    onDisk.Clear();
    r = StorageReconciler.Reconcile(recs, catalog, null, cached);
    Check("Cache cleared behind our back -> record dropped (Clear Cache button, disk cleanup...)", r.records.Count == 0 && r.dropped.SequenceEqual(new[] { "syros" }));
    onDisk.Add("car_syros|h1");
    recs = new List<VehicleRecord> { Rec("syros", true, B("car_syros", "OLDHASH", 90000000)) };
    r = StorageReconciler.Reconcile(recs, catalog, null, cached);
    Check("Recorded files are gone but the newest version is on disk -> repaired to the newest files", r.records.Count == 1 && r.records[0].bundles.Any(b => b.hash == "h1") && r.repaired.Count == 1);
    onDisk.Clear(); onDisk.Add("car_syros|OLDHASH");
    r = StorageReconciler.Reconcile(recs, catalog, null, cached);
    Check("Old version still on disk, newer in the catalog -> record kept (the status logic reports 'update needed')", r.records.Count == 1 && r.records[0].bundles[0].hash == "OLDHASH" && !r.Changed);
    Check("  ...and the status logic agrees: update, not 'not downloaded'", VehicleStatusEvaluator.Evaluate(r.records[0], new LatestVehicleInfo { bundles = catalog[0].bundles }, cached).status == VehicleStatus.UpdateAvailable);

    Console.WriteLine("\n=== Unusual data ===");
    onDisk.Clear(); onDisk.Add("car_syros|h1"); onDisk.Add(Scripts.Key);
    var unscanned = new VehicleRecord { vehicleId = "syros", bundlesKnown = false, installedVersion = "1.2" };
    r = StorageReconciler.Reconcile(new List<VehicleRecord> { unscanned }, catalog, null, cached);
    Check("Unscanned record + files on disk -> adopted, keeps its own version text", r.records.Count == 1 && r.records[0].bundlesKnown && r.records[0].installedVersion == "1.2");
    onDisk.Clear();
    r = StorageReconciler.Reconcile(new List<VehicleRecord> { unscanned }, catalog, null, cached);
    Check("Unscanned record + no files -> dropped", r.records.Count == 0 && r.dropped.Count == 1);
    onDisk.Add("car_syros|h1");
    var pathLike = new VehicleRecord { vehicleId = "Assets/Vehicles/syros.prefab", addressableKey = "syros", needsIdReconcile = true, bundlesKnown = false, installedVersion = "9.9", downloadedAtUtcTicks = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).Ticks };
    r = StorageReconciler.Reconcile(new List<VehicleRecord> { pathLike }, catalog, null, cached);
    Check("Path-style id from the old list is matched to the right vehicle by its addressable key", r.records.Count == 1 && r.records[0].vehicleId == "syros" && !r.records[0].needsIdReconcile);
    Check("  ...and keeps that record's own version and download date", r.records[0].installedVersion == "9.9" && r.records[0].DownloadedAtUtc.Value.Month == 7);
    Check("  ...and no stray record is left under the file-path id", !r.records.Any(x => x.vehicleId.Contains("/")));
    onDisk.Clear(); onDisk.Add("old_removed|h1");
    var orphanRec = Rec("removed_from_server", true, B("old_removed", "h1", 5000));
    r = StorageReconciler.Reconcile(new List<VehicleRecord> { orphanRec }, catalog, null, cached);
    Check("Vehicle no longer in the catalog but its files exist -> kept (so it can be seen and removed)", r.records.Count == 1 && r.records[0].vehicleId == "removed_from_server");
    onDisk.Clear();
    r = StorageReconciler.Reconcile(new List<VehicleRecord> { orphanRec }, catalog, null, cached);
    Check("  ...and dropped once its files are gone", r.records.Count == 0);
    Check("Unscanned record for a vehicle not in the catalog is dropped (cannot be verified)", StorageReconciler.Reconcile(new List<VehicleRecord> { new VehicleRecord { vehicleId = "mystery", bundlesKnown = false } }, catalog, null, cached).records.Count == 0);
    onDisk.Add("car_syros|h1");
    var dup = new List<CatalogVehicle> { catalog[0], Cat("SYROS", "other", 1) };
    Check("Duplicate catalog ids (different case): first wins, one record", StorageReconciler.Reconcile(new List<VehicleRecord>(), dup, null, cached).records.Count == 1);
    var gone = Cat("ghost", "car_ghost", 10); gone.existsInCatalog = false; gone.bundles.Clear();
    Check("Catalog entry whose key wasn't found: nothing to adopt", StorageReconciler.Reconcile(new List<VehicleRecord>(), new List<CatalogVehicle> { gone }, null, cached).records.Count == 0);
    Check("Blank / null entries are ignored", StorageReconciler.Reconcile(new List<VehicleRecord> { null, new VehicleRecord { vehicleId = " " } }, new List<CatalogVehicle> { null, new CatalogVehicle { vehicleId = "" } }, null, cached).records.Count == 0);
    Check("Null inputs don't crash", StorageReconciler.Reconcile(null, null, null, cached).records.Count == 0);
    try { StorageReconciler.Reconcile(null, null, null, null); Check("Missing cache checker rejected", false); } catch (ArgumentNullException) { Check("Missing cache checker rejected", true); }
    Check("A vehicle with no files in the catalog can't be 'downloaded'", StorageReconciler.Reconcile(new List<VehicleRecord>(), new List<CatalogVehicle> { new CatalogVehicle { vehicleId = "empty" } }, null, cached).records.Count == 0);

    Console.WriteLine("\n=== Registry: replace everything in one save ===");
    string path = Path.Combine(Path.GetTempPath(), "vr_" + Guid.NewGuid().ToString("N"), "reg.json");
    var reg = new VehicleRegistry(path); reg.Load(); reg.Upsert(Rec("old1", true, B("a", "1", 1)));
    Check("ReplaceAll swaps the whole set and persists it", reg.ReplaceAll(new List<VehicleRecord> { Rec("n1", true, B("b", "1", 2)), Rec(" n2 ", true), null, new VehicleRecord { vehicleId = "" } }) && reg.Count == 2 && reg.Get("old1") == null && reg.Get("n2") != null);
    var again = new VehicleRegistry(path); again.Load();
    Check("  ...and it survives a restart", again.Count == 2 && again.Get("n1").bundles.Count == 1);

    Console.WriteLine("\n=== Labels ===");
    Func<VehicleStatus, long, long, StorageLabel> L = (st, dl, total) => StorageLabels.For(new VehicleStatusInfo { status = st, downloadBytes = dl }, total, false);
    Check("Not downloaded: orange, shows the size to download", L(VehicleStatus.NotDownloaded, 299000000, 300000000).text == "Not downloaded · 285 MB" && L(VehicleStatus.NotDownloaded, 0, 300000000).tone == LabelTone.Warn);
    Check("Not downloaded with no download size known falls back to the total", L(VehicleStatus.NotDownloaded, 0, 5 * 1048576).text == "Not downloaded · 5.0 MB");
    Check("Downloaded: green, shows size on this PC", L(VehicleStatus.UpToDate, 0, 211L * 1048576).text == "On this PC · 211 MB" && L(VehicleStatus.UpToDate, 0, 1).tone == LabelTone.Good);
    Check("Out of date, can't keep old versions yet: says 'Update needed' (honest about what happens)", L(VehicleStatus.UpdateAvailable, 120L * 1048576, 1).text == "Update needed · 120 MB");
    Check("  ...once old versions can be kept it says 'Update available'", StorageLabels.For(new VehicleStatusInfo { status = VehicleStatus.UpdateAvailable, downloadBytes = 120L * 1048576 }, 1, true).text == "Update available · 120 MB" && StorageLabels.For(new VehicleStatusInfo { status = VehicleStatus.UpdateAvailable }, 1, true).tone == LabelTone.Info);
    Check("Update that costs 0 bytes shows no size", L(VehicleStatus.UpdateAvailable, 0, 1).text == "Update needed");
    Check("Files missing and update required are warnings", L(VehicleStatus.FilesMissing, 10, 1).text.StartsWith("Files missing") && L(VehicleStatus.UpdateRequired, 10, 1).text.StartsWith("Update needed"));
    Check("Unknown says nothing; null says nothing", L(VehicleStatus.Unknown, 0, 9).text == "" && StorageLabels.For(null, 1, false).text == "");
    Check("Removed from server but still on disk is mentioned", StorageLabels.For(new VehicleStatusInfo { status = VehicleStatus.UpToDate, removedFromServer = true }, 1048576, false).text.Contains("no longer on the server"));

    Console.WriteLine("\nPassed " + pass + " | Failed " + fail); Environment.Exit(fail == 0 ? 0 : 1);
  }
}
