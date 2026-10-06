using System; using System.Collections.Generic; using System.IO; using System.Linq;
using VehicleMeasurement.Storage;
class T {
  static int pass, fail;
  static void Check(string name, bool ok, string extra = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "✅ " : "❌ ") + name + (extra != "" ? "  " + extra : "")); }
  static BundleRef B(string n, string h, long s) { return new BundleRef(n, h, s); }
  static VehicleRecord Rec(string id, params BundleRef[] bs) { return new VehicleRecord { vehicleId = id, vehicleName = id.ToUpper(), bundlesKnown = true, bundles = bs.ToList(), installedVersion = "1.0" }; }
  static LatestVehicleInfo Latest(params BundleRef[] bs) { return new LatestVehicleInfo { version = "2.0", bundles = bs.ToList() }; }
  static string Tmp() { var d = Path.Combine(Path.GetTempPath(), "vr_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }

  static void Main() {
    var all = new HashSet<string>();
    Func<BundleRef, bool> cached = b => all.Contains(b.Key);
    BundleRef car = B("car_body", "h1", 300), shared = B("shared_mat", "s1", 80), wheels = B("wheels", "w1", 50);

    Console.WriteLine("=== Status: is this vehicle downloaded and current? ===");
    all.Clear(); all.Add(car.Key); all.Add(shared.Key);
    var r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car, shared), cached);
    Check("Same files as the newest catalog -> UpToDate", r.status == VehicleStatus.UpToDate && r.downloadBytes == 0);
    Check("  ...even if the catalog.json version TEXT differs (files decide, not text)", r.status == VehicleStatus.UpToDate && r.availableVersion == "2.0" && r.installedVersion == "1.0");
    r = VehicleStatusEvaluator.Evaluate(Rec("a", shared, car), Latest(car, shared, car), cached);
    Check("Order and duplicates in the lists don't matter", r.status == VehicleStatus.UpToDate);
    var car2 = B("car_body", "h2", 320);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car2, shared), cached);
    Check("Same bundle name, new hash -> UpdateAvailable, still openable", r.status == VehicleStatus.UpdateAvailable && r.CanOpen);
    Check("  ...download size counts only the changed file (320, not 400)", r.downloadBytes == 320, "got " + r.downloadBytes);
    all.Add(car2.Key);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car2, shared), cached);
    Check("New file already cached via another vehicle -> update costs 0 bytes", r.status == VehicleStatus.UpdateAvailable && r.downloadBytes == 0);
    all.Clear(); all.Add(car.Key); all.Add(shared.Key);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car, shared, wheels), cached);
    Check("Newest catalog adds a file -> UpdateAvailable (50 bytes)", r.status == VehicleStatus.UpdateAvailable && r.downloadBytes == 50);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car), cached);
    Check("Newest catalog drops a file -> UpdateAvailable (nothing to download)", r.status == VehicleStatus.UpdateAvailable && r.downloadBytes == 0);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), null, cached);
    Check("Server unreachable -> UpToDate but update check unknown", r.status == VehicleStatus.UpToDate && !r.updateCheckKnown && r.CanOpen);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), new LatestVehicleInfo { existsInCatalog = false }, cached);
    Check("Vehicle removed from the server -> still usable, flagged", r.status == VehicleStatus.UpToDate && r.removedFromServer && r.CanOpen);
    all.Remove(shared.Key);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car, shared), cached);
    Check("Downloaded file missing from cache, same version -> FilesMissing (repair 80)", r.status == VehicleStatus.FilesMissing && r.downloadBytes == 80 && !r.CanOpen);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), Latest(car2, shared), cached);
    Check("File missing AND newer exists -> UpdateRequired (old copy can't open)", r.status == VehicleStatus.UpdateRequired && !r.CanOpen);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car, shared), null, cached);
    Check("File missing while offline -> FilesMissing", r.status == VehicleStatus.FilesMissing);
    all.Clear(); all.Add(car.Key); all.Add(shared.Key);
    r = VehicleStatusEvaluator.Evaluate(null, Latest(car, shared, wheels), cached);
    Check("Never downloaded -> NotDownloaded, size = uncached files only (50)", r.status == VehicleStatus.NotDownloaded && r.downloadBytes == 50);
    r = VehicleStatusEvaluator.Evaluate(null, null, cached);
    Check("Never downloaded and offline -> NotDownloaded", r.status == VehicleStatus.NotDownloaded);
    var legacy = Rec("a"); legacy.bundlesKnown = false;
    r = VehicleStatusEvaluator.Evaluate(legacy, Latest(car2), cached);
    Check("Adopted from old tracker (unscanned) -> Unknown, never a false update", r.status == VehicleStatus.Unknown && r.CanOpen);
    r = VehicleStatusEvaluator.Evaluate(Rec("a", car2, shared), Latest(car2, shared), cached);
    Check("Hash-only difference can't be confused with a different file", VehicleStatusEvaluator.SameBundles(new[] { car }, new[] { car2 }) == false);
    try { VehicleStatusEvaluator.Evaluate(Rec("a"), null, null); Check("Null cache checker rejected", false); } catch (ArgumentNullException) { Check("Null cache checker rejected", true); }

    Console.WriteLine("\n=== Storage: how much space, and what is safe to delete? ===");
    VehicleRecord A = Rec("a", car, shared), Bv = Rec("b", B("truck", "t1", 500), shared), C = Rec("c", B("van", "v1", 200));
    var recs = new List<VehicleRecord> { A, Bv, C };
    var usage = StoragePlanner.Usage(recs).ToDictionary(u => u.vehicleId);
    Check("A: total 380, exclusive 300, shared 80", usage["a"].totalBytes == 380 && usage["a"].exclusiveBytes == 300 && usage["a"].sharedBytes == 80);
    Check("A shares files with vehicle B (by name)", usage["a"].sharedWithVehicles.SequenceEqual(new[] { "B" }));
    Check("C shares nothing", usage["c"].sharedBytes == 0 && usage["c"].sharedWithVehicles.Count == 0);
    Check("Real disk total counts the shared file once (300+500+80+200)", StoragePlanner.TotalUniqueBytes(recs) == 1080, StoragePlanner.TotalUniqueBytes(recs).ToString());
    var plan = StoragePlanner.PlanRemoval(recs, "a");
    Check("Removing A frees only its own file (300) and KEEPS the shared one", plan.freedBytes == 300 && plan.delete.Count == 1 && plan.delete[0].name == "car_body" && plan.keep.Count == 1 && plan.keep[0].name == "shared_mat");
    Check("  ...and says which vehicle still needs the kept file", plan.sharedWithVehicles.SequenceEqual(new[] { "B" }) && plan.keptSharedBytes == 80);
    plan = StoragePlanner.PlanRemoval(recs, "c");
    Check("Removing C (no sharing) frees all 200", plan.freedBytes == 200 && plan.keep.Count == 0);
    var two = new List<VehicleRecord> { Rec("x", shared), Rec("y", shared) };
    Check("Two vehicles on identical files: removing one frees nothing", StoragePlanner.PlanRemoval(two, "x").freedBytes == 0);
    Check("Removing the LAST user of a shared file does free it", StoragePlanner.PlanRemoval(new List<VehicleRecord> { Rec("x", shared) }, "x").freedBytes == 80);
    Check("Removing an unknown vehicle -> not found, nothing planned", !StoragePlanner.PlanRemoval(recs, "zzz").vehicleFound && StoragePlanner.PlanRemoval(recs, "zzz").delete.Count == 0);
    Check("Vehicle id matching ignores case and spaces", StoragePlanner.PlanRemoval(recs, "  A ").vehicleFound);
    var unscanned = Rec("u"); unscanned.bundlesKnown = false;
    var withUnscanned = new List<VehicleRecord> { A, Bv, unscanned };
    plan = StoragePlanner.PlanRemoval(withUnscanned, "a");
    Check("An unscanned vehicle exists -> removal BLOCKED, deletes nothing (it might use the shared file)", plan.blocked && plan.delete.Count == 0 && plan.blockedReason != null);
    plan = StoragePlanner.PlanRemoval(withUnscanned, "u");
    Check("Removing an unscanned vehicle itself is blocked too", plan.blocked);
    var carNew = B("car_body", "h2", 320);
    plan = StoragePlanner.PlanReplace(recs, "a", new[] { carNew, shared });
    Check("Updating A: old car file (300) can go, shared file stays", plan.freedBytes == 300 && plan.delete[0].Key == car.Key && plan.keep.Count == 0 && !plan.blocked);
    plan = StoragePlanner.PlanReplace(recs, "a", new[] { car, shared });
    Check("'Update' to the same files deletes nothing", plan.freedBytes == 0 && plan.delete.Count == 0);
    var shareOld = new List<VehicleRecord> { Rec("p", car), Rec("q", car) };
    Check("Updating P while Q still uses the old file keeps it", StoragePlanner.PlanReplace(shareOld, "p", new[] { carNew }).delete.Count == 0);
    plan = StoragePlanner.PlanRemoveAll(recs);
    Check("Remove everything: every distinct file once (1080)", plan.freedBytes == 1080 && plan.delete.Count == 4);
    Check("Usage handles a record with duplicate bundle entries without double counting", StoragePlanner.Usage(new List<VehicleRecord> { Rec("d", car, car) })[0].totalBytes == 300);
    Check("Negative / zero sizes never reduce totals", StoragePlanner.TotalUniqueBytes(new List<VehicleRecord> { Rec("n", B("bad", "x", -50), B("ok", "y", 10)) }) == 10);

    Console.WriteLine("\n=== Registry: saved safely, recovered when damaged ===");
    string dir = Tmp(); string path = Path.Combine(dir, "sub", "vehicle_registry.json");
    var reg = new VehicleRegistry(path);
    Check("First run: no file -> Fresh and empty", reg.Load() == RegistryLoadOutcome.Fresh && reg.Count == 0);
    var rec = Rec("alpha", car, shared); rec.catalogId = "cat42"; rec.downloadedAtUtcTicks = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc).Ticks;
    Check("Save creates the folder and file", reg.Upsert(rec) && File.Exists(path));
    Check("No leftover temp file after saving", !File.Exists(path + ".tmp"));
    var reg2 = new VehicleRegistry(path); var oc = reg2.Load(); var back = reg2.Get("ALPHA");
    Check("Reload round-trips everything", oc == RegistryLoadOutcome.Loaded && back != null && back.catalogId == "cat42" && back.bundles.Count == 2 && back.bundles[0].size == 300 && back.TotalBytes() == 380 && back.DownloadedAtUtc.Value.Year == 2026);
    Check("Upsert with the same id replaces, not duplicates", reg2.Upsert(Rec("Alpha", car)) && reg2.Count == 1 && reg2.Get("alpha").bundles.Count == 1);
    Check("Blank vehicle id rejected", !reg2.Upsert(new VehicleRecord { vehicleId = "  " }) && !reg2.Upsert(null));
    reg2.Upsert(Rec("beta", wheels));
    Check("Backup copy exists after the second save", File.Exists(path + ".bak"));
    File.WriteAllText(path, "{ this is not json ");
    var reg3 = new VehicleRegistry(path); oc = reg3.Load();
    Check("Main file corrupted -> recovered from the backup", oc == RegistryLoadOutcome.RecoveredFromBackup && reg3.Count >= 1, "count=" + reg3.Count);
    Check("  ...bad file kept as *.corrupt for inspection", File.Exists(path + ".corrupt"));
    File.WriteAllText(path, ""); File.WriteAllText(path + ".bak", "garbage");
    var reg4 = new VehicleRegistry(path);
    Check("Both files unreadable -> starts empty, doesn't crash", reg4.Load() == RegistryLoadOutcome.StartedEmptyAfterCorruption && reg4.Count == 0);
    Check("  ...and can save again afterwards", reg4.Upsert(Rec("gamma", car)) && new VehicleRegistry(path).Load() == RegistryLoadOutcome.Loaded);
    File.Delete(path); File.Delete(path + ".bak");
    File.WriteAllText(path + ".bak", File.Exists(path) ? "" : "{\"schema\":1,\"vehicles\":[{\"vehicleId\":\"fromBak\",\"bundles\":[]}]}");
    var reg5 = new VehicleRegistry(path);
    Check("Main file deleted but backup present -> recovered", reg5.Load() == RegistryLoadOutcome.RecoveredFromBackup && reg5.Get("fromBak") != null);
    string newer = Path.Combine(dir, "newer.json");
    File.WriteAllText(newer, "{\"schema\":99,\"vehicles\":[{\"vehicleId\":\"future\",\"bundles\":[]}]}");
    var reg6 = new VehicleRegistry(newer);
    Check("File from a NEWER app version -> readable but never overwritten", reg6.Load() == RegistryLoadOutcome.NewerVersionReadOnly && reg6.ReadOnly && reg6.Get("future") != null);
    Check("  ...writes are refused and the file is untouched", !reg6.Upsert(Rec("x", car)) && reg6.LastSaveError != null && File.ReadAllText(newer).Contains("\"schema\":99"));
    string hand = Path.Combine(dir, "hand.json");
    File.WriteAllText(hand, "{\"schema\":1,\"vehicles\":[{\"vehicleId\":\"ok\"},{\"vehicleId\":\"\"}]}");
    var reg7 = new VehicleRegistry(hand); reg7.Load();
    Check("Hand-edited file: blank ids dropped, missing bundle lists repaired", reg7.Count == 1 && reg7.Get("ok").bundles != null);
    bool removed = reg2.Remove("alpha"); var fresh = new VehicleRegistry(path); fresh.Load();
    Check("Remove works, persists to disk, and removing twice is harmless", removed && reg2.Get("alpha") == null && fresh.Get("alpha") == null && !reg2.Remove("alpha"));
    reg2.Upsert(Rec("t", car));
    Check("Touch records when a vehicle was last opened", reg2.Touch("t", new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc)) && reg2.Get("t").LastOpenedUtc.Value.Day == 7 && !reg2.Touch("nope", DateTime.UtcNow));
    Check("Unwritable location -> Save returns false with a reason, no crash", !new VehicleRegistry("/proc/nope/registry.json").Upsert(Rec("z", car)));

    Console.WriteLine("\n=== Migration from the old downloaded_vehicles.json ===");
    string old = "{\"vehicles\":[" +
      "{\"vehicleId\":\"thar\",\"vehicleName\":\"Thar\",\"addressableKey\":\"vehicles/thar\",\"downloadedDate\":\"2026-09-01 10:30:00\",\"version\":\"1.2\"}," +
      "{\"vehicleId\":\"xuv\",\"vehicleName\":\"XUV\",\"downloadedDate\":\"not a date\"}," +
      "{\"vehicleId\":\"thar\",\"vehicleName\":\"Thar NEW\",\"version\":\"1.3\"}," +
      "{\"vehicleId\":\"\",\"vehicleName\":\"ghost\"}," +
      "{\"vehicleId\":\"Assets/Vehicles/scorpio.prefab\",\"vehicleName\":\"Scorpio\"}]}";
    var mreg = new VehicleRegistry(Path.Combine(Tmp(), "m.json")); mreg.Load();
    var rep = LegacyTrackerMigration.Import(old, mreg);
    Check("Imports 3 vehicles (duplicate merged, blank id skipped)", rep.imported == 3 && rep.duplicatesMerged == 1 && rep.skippedEmptyId == 1 && mreg.Count == 3);
    var thar = mreg.Get("thar");
    Check("Last duplicate wins; null version becomes empty text", thar.vehicleName == "Thar NEW" && thar.installedVersion == "1.3");
    Check("Imported vehicles are 'unscanned' and pinned to the default catalog", !thar.bundlesKnown && thar.catalogId == "default" && thar.bundles.Count == 0);
    Check("Bad date -> unknown (0), no crash", mreg.Get("xuv").downloadedAtUtcTicks == 0 && mreg.Get("xuv").DownloadedAtUtc == null);
    Check("Path-style ids flagged for reconciliation", mreg.Get("Assets/Vehicles/scorpio.prefab").needsIdReconcile && rep.needReconcile.Count == 1);
    Check("Imported vehicles evaluate as Unknown (never a false update)", VehicleStatusEvaluator.Evaluate(thar, Latest(car), cached).status == VehicleStatus.Unknown);
    var again = LegacyTrackerMigration.Import(old, mreg);
    Check("Running the migration twice adds nothing and overwrites nothing", again.imported == 0 && mreg.Count == 3);
    var scanned = Rec("thar", car); mreg.Upsert(scanned); LegacyTrackerMigration.Import(old, mreg);
    Check("A scanned record is never replaced by old tracker data", mreg.Get("thar").bundlesKnown);
    Check("Empty / garbage tracker file imports nothing", LegacyTrackerMigration.Import("", mreg).imported == 0 && LegacyTrackerMigration.Import("%%%", mreg).imported == 0 && LegacyTrackerMigration.Import(null, mreg).imported == 0);

    Console.WriteLine("\n=== Size text ===");
    Check("Formatting", ByteFormat.Format(0) == "0 B" && ByteFormat.Format(512) == "512 B" && ByteFormat.Format(340 * 1024) == "340 KB" && ByteFormat.Format((long)(8.4 * 1048576)) == "8.4 MB" && ByteFormat.Format(412L * 1048576) == "412 MB" && ByteFormat.Format((long)(1.3 * 1073741824)) == "1.3 GB" && ByteFormat.Format(-5) == "0 B");

    Console.WriteLine("\nPassed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
