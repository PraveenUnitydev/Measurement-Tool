using System; using System.Collections; using System.Collections.Generic; using System.IO; using System.Linq; using System.Reflection;
using UnityEngine; using UnityEngine.AddressableAssets; using UnityEngine.ResourceManagement.ResourceLocations; using UnityEngine.ResourceManagement.ResourceProviders;
using VehicleMeasurement; using VehicleMeasurement.Storage;

class FakeLoc : IResourceLocation {
  public string PrimaryKey { get; set; } public string InternalId { get; set; } public Type ResourceType { get { return typeof(object); } }
  public object Data { get; set; } public bool HasDependencies { get { return Dependencies.Count > 0; } } public IList<IResourceLocation> Dependencies { get; set; }
  public FakeLoc() { Dependencies = new List<IResourceLocation>(); }
}
class ServiceTests {
  static int pass, fail;
  static void Check(string n, bool ok, string x = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + n + (x != "" ? "  [" + x + "]" : "")); }
  const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
  static int events;
  static FakeLoc Scripts;

  static string Hash(string v) { return v; }
  // A vehicle in the fake Addressables catalog: its main file + the shared scripts file
  static void AddVehicle(RemoteAddressableVehicleLoader loader, string id, long size, string hash = "h1", string version = "") {
    var main = new FakeLoc { PrimaryKey = id, InternalId = "https://server/" + id + ".bundle", Data = new AssetBundleRequestOptions { BundleName = "car_" + id, Hash = hash, BundleSize = size } };
    main.Dependencies.Add(Scripts);
    var asset = new FakeLoc { PrimaryKey = id, InternalId = "Assets/" + id + ".prefab" }; asset.Dependencies.Add(main);
    Addressables.FakeLocations[id] = new List<IResourceLocation> { asset };
    var existing = loader.Vehicles.FirstOrDefault(v => v.vehicleId == id);
    if (existing == null) loader.Vehicles.Add(new RemoteVehicleInfo { vehicleId = id, vehicleName = id.ToUpper(), addressableKey = id, version = version });
  }
  static void OnDisk(string id, string hash = "h1") { Caching.FakeCache.Add("car_" + id + "|" + hash); }

  static VehicleStorageService NewService(RemoteAddressableVehicleLoader loader) {
    typeof(VehicleStorageService).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
    RemoteAddressableVehicleLoader.Instance = loader;
    var svc = new GameObject().AddComponent<VehicleStorageService>();
    typeof(VehicleStorageService).GetMethod("Awake", NP).Invoke(svc, null);
    return svc;
  }
  static void Start(VehicleStorageService svc) { MonoBehaviour.Run((IEnumerator)typeof(VehicleStorageService).GetMethod("Start", NP).Invoke(svc, null)); }
  static T Do<T>(Func<Action<T>, IEnumerator> routine) where T : class { T result = null; MonoBehaviour.Run(routine(r => result = r)); return result; }
  static string Fresh() { var d = Path.Combine(Path.GetTempPath(), "vrsvc_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); Application.persistentDataPath = d; return d; }
  static void WriteTracker(string dir, params string[] entries) { File.WriteAllText(Path.Combine(dir, "downloaded_vehicles.json"), "{\"vehicles\":[" + string.Join(",", entries) + "]}"); }
  static string T(string id, string date, string ver = null) { return "{\"vehicleId\":\"" + id + "\",\"vehicleName\":\"" + id + "\",\"addressableKey\":\"" + id + "\",\"downloadedDate\":\"" + date + "\"" + (ver != null ? ",\"version\":\"" + ver + "\"" : "") + "}"; }

  static RemoteAddressableVehicleLoader Setup() {
    Caching.FakeCache.Clear(); Caching.InUse.Clear(); Caching.Marked.Clear(); Caching.MarkThrows = false; Caching.IsCachedFunc = null; Addressables.FakeLocations.Clear();
    Scripts = new FakeLoc { PrimaryKey = "scripts", InternalId = "https://server/scripts.bundle", Data = new AssetBundleRequestOptions { BundleName = "monoscripts", Hash = "m1", BundleSize = 2 } };
    var loader = new RemoteAddressableVehicleLoader();
    AddVehicle(loader, "a", 3000, version: "2026.03.25"); AddVehicle(loader, "b", 5000); AddVehicle(loader, "c", 4000, version: "2026.03.25"); AddVehicle(loader, "d", 1000);
    return loader;
  }

  static void Main() {
    // ───────────── the real situation: tracker lists everything, only some files are on disk ─────────────
    Console.WriteLine("=== Startup: the old list says all 4 are downloaded, but only a and b are on disk ===");
    var loader = Setup(); string dir = Fresh();
    WriteTracker(dir, T("a", "2026-10-06 10:00:00", "2026.03.25"), T("b", "2026-10-02 10:00:00"), T("c", "2026-06-15 10:00:00", "2026.03.25"), T("d", "2026-07-01 10:00:00"), T("m310", "2026-07-01 10:00:00"));
    OnDisk("a"); OnDisk("b"); Caching.FakeCache.Add("monoscripts|m1");
    var svc = NewService(loader); events = 0; Action handler = () => events++; VehicleStorageService.Changed += handler;
    Check("Not ready before the first scan", !svc.IsReady && svc.GetState("a") == null);
    Check("  ...and while the scan is pending, the old (untrustworthy) list is hidden, not shown", svc.IsPending && DownloadedVehiclesTracker.GetDownloadedVehicles().Count == 0);
    Start(svc);
    Check("Ready after the first scan, announced once", svc.IsReady && events == 1, "events=" + events);
    Check("  ...no longer pending", !svc.IsPending);
    Check("At start, every downloaded vehicle's files are marked as just used (keeps them clear of Unity's 150-day expiry)", svc.LastRefreshedFiles == 3 && Caching.Marked.OrderBy(x => x).SequenceEqual(new[] { "car_a|h1", "car_b|h1", "monoscripts|m1" }), string.Join(",", Caching.Marked));
    Check("  ...and vehicles that aren't downloaded are not touched", !Caching.Marked.Any(m => m.StartsWith("car_c") || m.StartsWith("car_d")));
    int marks = Caching.Marked.Count; MonoBehaviour.Run(svc.ReconcileRoutine(false));
    Check("  ...once per session, not on every re-scan", Caching.Marked.Count == marks);
    Check("Registry holds exactly the vehicles whose files are on disk (a, b)", svc.Registry.Count == 2 && svc.Registry.Get("a") != null && svc.Registry.Get("b") != null);
    Check("c and d (listed as downloaded, files gone) are NOT downloaded", !svc.GetState("c").IsDownloaded && !svc.GetState("d").IsDownloaded && svc.GetState("c").status == VehicleStatus.NotDownloaded);
    var dl = DownloadedVehiclesTracker.GetDownloadedVehicles().Select(v => v.vehicleId).OrderBy(x => x).ToList();
    Check("The old list, read through the tracker, now shows only a and b (and hides the id the catalog doesn't know)", dl.SequenceEqual(new[] { "a", "b" }), string.Join(",", dl));
    var a = svc.GetState("a");
    Check("a: downloaded, up to date, label 'On this PC · 3 KB', green", a.IsDownloaded && !a.NeedsUpdate && a.label == "On this PC · 3 KB" && a.tone == LabelTone.Good, a.label);
    Check("a keeps its date and version from the old list", a.downloadedAtUtc.HasValue && a.versionText == "2026.03.25");
    Check("b has no version anywhere -> a short content id instead of nothing", svc.GetState("b").versionText.StartsWith("id "), svc.GetState("b").versionText);
    var c = svc.GetState("c");
    Check("c: label says it must be downloaded and how much (just its own file, not the shared one)", c.label == "Not downloaded · 4 KB" && c.tone == LabelTone.Warn, c.label);
    Check("Lookup by addressable key works too; unknown vehicle -> null", svc.GetState("zzz", "c").vehicleId == "c" && svc.GetState("zzz", "nope") == null);
    Check("Saved-vehicle ids differing in case still match", svc.GetState("A").vehicleId == "a");
    Check("The measurement-scene check is now truthful: b (no saved version) is downloaded, NOT 'update available'", svc.GetState("b").IsDownloaded && !svc.GetState("b").NeedsUpdate);
    var sum = svc.GetSummary();
    Check("Summary: 2 downloaded (shared file counted once), 2 not (their own files only)", sum.downloadedCount == 2 && sum.downloadedBytes == 3000 + 5000 + 2 && sum.notDownloadedCount == 2 && sum.notDownloadedBytes == 4000 + 1000, sum.downloadedBytes + "/" + sum.notDownloadedBytes);
    var rows = svc.GetRows();
    Check("Rows: one per downloaded vehicle with size and version", rows.Count == 2 && rows.Single(r => r.vehicleId == "b").bytes == 5002);

    Console.WriteLine("\n=== A new download is recorded (both ways the old list can be called) ===");
    events = 0; OnDisk("c");
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c", version = "" });
    Check("c is now in the registry, with the catalog's version (the old call passed none)", svc.Registry.Get("c") != null && svc.Registry.Get("c").installedVersion == "2026.03.25" && svc.Registry.Get("c").DownloadedAtUtc.HasValue);
    Check("Announced once; c now reads as downloaded", events == 1 && svc.GetState("c").IsDownloaded, "events=" + events);
    OnDisk("d");
    DownloadedVehiclesTracker.MarkAsDownloaded(new VehicleAddressableInfo { vehicleId = "Assets/Vehicles/d.prefab", vehicleName = "d", addressableKey = "d" });
    Check("Called with a file path as the id: recorded under the real id, no duplicate", svc.Registry.Get("d") != null && svc.Registry.Get("Assets/Vehicles/d.prefab") == null && svc.Registry.Count == 4, "count=" + svc.Registry.Count);
    events = 0; OnDisk("zz"); DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "ghost", addressableKey = "no-such-key" });
    Check("A 'download' with files not on disk (or an unknown key) is ignored", svc.Registry.Get("ghost") == null && events == 0);
    Caching.FakeCache.Remove("car_d|h1"); svc.Registry.Remove("d"); DownloadedVehiclesTracker.RemoveDownloaded("d"); OnDisk("d");
    events = 0; DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "d", vehicleName = "D", addressableKey = "d" });
    long firstStamp = svc.Registry.Get("d").downloadedAtUtcTicks;
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "d", vehicleName = "D", addressableKey = "d" });
    Check("Marking the same files again keeps the original download time", svc.Registry.Get("d").downloadedAtUtcTicks == firstStamp && svc.Registry.Count == 4);

    Console.WriteLine("\n=== Removing one vehicle must not hurt the others ===");
    events = 0; Caching.FakeCache.Add("zz|x"); Caching.FakeCache.Remove("zz|x");
    var o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("b", done));
    Check("b removed: success, frees exactly its own file (5000), message says so", o.success && o.freedBytes == 5000 && o.message.StartsWith("Removed B"), o.message);
    Check("b's file is gone; the shared scripts file STAYS (a, c and d still need it)", !Caching.FakeCache.Contains("car_b|h1") && Caching.FakeCache.Contains("monoscripts|m1"));
    Check("Other vehicles are untouched", Caching.FakeCache.Contains("car_a|h1") && Caching.FakeCache.Contains("car_c|h1") && svc.GetState("a").IsDownloaded && svc.GetState("c").IsDownloaded);
    Check("b is no longer downloaded: registry, truth filter and the old list agree", svc.Registry.Get("b") == null && !svc.GetState("b").IsDownloaded && !DownloadedVehiclesTracker.GetDownloadedVehicles().Any(v => v.vehicleId == "b") && !DownloadedVehiclesTracker.IsDownloaded("b"));
    Check("Removal announced once", events == 1, "events=" + events);
    o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("b", done));
    Check("Removing it again is harmless", o.success && o.freedBytes == 0 && o.message.Contains("aren't on this PC"));

    Console.WriteLine("\n=== A file that is in use can't be removed, and nothing is lost ===");
    Caching.InUse.Add("car_a|h1");
    o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("a", done));
    Check("In use -> fails with a clear message", !o.success && o.message.Contains("in use"), o.message);
    Check("  ...a is still recorded and still on disk", svc.Registry.Get("a") != null && Caching.FakeCache.Contains("car_a|h1") && svc.GetState("a").IsDownloaded);
    Caching.InUse.Clear();

    Console.WriteLine("\n=== The last vehicle using the shared file takes it with it ===");
    svc.Registry.Remove("c"); svc.Registry.Remove("d"); Caching.FakeCache.Remove("car_c|h1"); Caching.FakeCache.Remove("car_d|h1");
    o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("a", done));
    Check("Removing the only remaining vehicle also removes the shared file", o.success && o.freedBytes == 3002 && !Caching.FakeCache.Contains("monoscripts|m1") && !Caching.FakeCache.Contains("car_a|h1"), "freed=" + o.freedBytes);

    Console.WriteLine("\n=== Remove all ===");
    Setup(); OnDisk("a"); OnDisk("b"); Caching.FakeCache.Add("monoscripts|m1"); Caching.FakeCache.Add("leftover|old");
    loader = new RemoteAddressableVehicleLoader(); Scripts = new FakeLoc { PrimaryKey = "scripts", InternalId = "https://server/scripts.bundle", Data = new AssetBundleRequestOptions { BundleName = "monoscripts", Hash = "m1", BundleSize = 2 } };
    AddVehicle(loader, "a", 3000); AddVehicle(loader, "b", 5000); dir = Fresh(); WriteTracker(dir, T("a", "2026-10-06 10:00:00"), T("b", "2026-10-06 10:00:00"));
    Caching.FakeCache.Clear(); OnDisk("a"); OnDisk("b"); Caching.FakeCache.Add("monoscripts|m1"); Caching.FakeCache.Add("leftover|old");
    svc = NewService(loader); Start(svc); events = 0;
    Caching.InUse.Add("car_a|h1");
    o = Do<RemoveOutcome>(done => svc.RemoveAllRoutine(done));
    Check("Something in use -> fails, and the list still tells the truth (nothing was removed)", !o.success && svc.Registry.Count == 2 && svc.GetState("a").IsDownloaded, o.message);
    Caching.InUse.Clear();
    o = Do<RemoveOutcome>(done => svc.RemoveAllRoutine(done));
    Check("Remove all: everything incl. leftovers goes, counts and bytes reported", o.success && o.vehicles == 2 && o.freedBytes == 8002 && Caching.FakeCache.Count == 0, o.message);
    Check("Registry and the old list are both emptied; every vehicle reads 'not downloaded'", svc.Registry.Count == 0 && DownloadedVehiclesTracker.GetDownloadedVehicles().Count == 0 && !svc.GetState("a").IsDownloaded && !svc.GetState("b").IsDownloaded);

    Console.WriteLine("\n=== Files vanish behind the app's back (disk cleanup, the old Clear Cache button) ===");
    OnDisk("a"); Caching.FakeCache.Add("monoscripts|m1"); MonoBehaviour.Run(svc.ReconcileRoutine(true));
    Check("A re-scan finds a on disk and records it", svc.Registry.Get("a") != null);
    Caching.FakeCache.Clear();
    var gone = svc.GetState("a");
    Check("Files wiped but not re-scanned yet: already reads as NOT downloaded ('Files missing'), never as downloaded", !gone.IsDownloaded && gone.status == VehicleStatus.FilesMissing && gone.label.StartsWith("Files missing"), gone.label);
    events = 0; MonoBehaviour.Run(svc.ReconcileRoutine(false));
    Check("Cache wiped -> next scan drops the record, announces the change", svc.Registry.Get("a") == null && events == 1 && !svc.GetState("a").IsDownloaded);

    Console.WriteLine("\n=== A failure while refreshing times must never stop the app ===");
    loader = Setup(); dir = Fresh(); WriteTracker(dir); OnDisk("a"); Caching.FakeCache.Add("monoscripts|m1"); Caching.MarkThrows = true;
    svc = NewService(loader); Start(svc);
    Check("MarkAsUsed throwing -> still ready, still truthful, nothing crashes", svc.IsReady && svc.GetState("a").IsDownloaded && svc.LastRefreshedFiles == 0);
    Caching.MarkThrows = false;

    Console.WriteLine("\n=== Addressables not ready: never conclude 'nothing is downloaded' ===");
    loader = Setup(); dir = Fresh(); WriteTracker(dir, T("a", "2026-10-06 10:00:00")); OnDisk("a"); Caching.FakeCache.Add("monoscripts|m1");
    Addressables.FakeLocations.Clear();                                   // every key fails to resolve
    svc = NewService(loader); events = 0; Start(svc);
    Check("Nothing resolves -> stays 'not ready', no records dropped, no false announcement", !svc.IsReady && events == 0 && DownloadedVehiclesTracker.GetDownloadedVehicles().Any(v => v.vehicleId == "a"));
    Check("  ...and once it has given up, the old list is shown again so Home is never left empty", !svc.IsPending);
    loader.Vehicles.Clear(); svc = NewService(loader); Start(svc);
    Check("Empty vehicle list -> stays not ready (Home keeps the old behaviour)", !svc.IsReady);
    loader.Loaded = false; svc = NewService(loader); events = 0; MonoBehaviour.Run(svc.ReconcileRoutine(true));
    Check("Catalog not loaded -> does nothing", !svc.IsReady && events == 0);

    Console.WriteLine("\n=== A content update: the files on disk are the old version ===");
    loader = Setup(); dir = Fresh(); WriteTracker(dir); OnDisk("a", "h1"); Caching.FakeCache.Add("monoscripts|m1");
    svc = NewService(loader); Start(svc);
    Check("a downloaded and current", svc.GetState("a").status == VehicleStatus.UpToDate);
    AddVehicle(loader, "a", 3500, hash: "h2");                            // the server now has a newer build of a
    loader.Vehicles.RemoveAll(v => v.vehicleId == "a"); loader.Vehicles.Add(new RemoteVehicleInfo { vehicleId = "a", vehicleName = "A", addressableKey = "a", version = "2026.03.25" });
    MonoBehaviour.Run(svc.ReconcileRoutine(false));
    var upd = svc.GetState("a");
    Check("Old files still on disk, newer on the server -> still 'downloaded' but NeedsUpdate, with the size to fetch", upd.IsDownloaded && upd.NeedsUpdate && upd.status == VehicleStatus.UpdateAvailable && upd.downloadBytes == 3500, upd.label);
    Check("Label is honest about what happens today: 'Update needed', not 'Update available'", upd.label == "Update needed · 3 KB" && !VehicleStorageService.CanKeepOldVersions, upd.label);
    Check("The record keeps the OLD files until the new ones are downloaded", svc.Registry.Get("a").bundles.Any(b => b.hash == "h1"));
    OnDisk("a", "h2"); Caching.FakeCache.Remove("car_a|h1");
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "a", vehicleName = "A", addressableKey = "a", version = "2026.03.25" });
    Check("After the update downloads, the record points at the new files and the update flag clears", svc.Registry.Get("a").bundles.Any(b => b.hash == "h2") && !svc.GetState("a").NeedsUpdate && svc.GetState("a").status == VehicleStatus.UpToDate);

    VehicleStorageService.Changed -= handler;

    Console.WriteLine("\n=== Older versions: removed for real, shown honestly ===");
    Caching.InUse.Clear();
    foreach (var k in new[] { "car_a|h1", "car_c|h1", "monoscripts|m1" }) Caching.FakeCache.Add(k);
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "a", vehicleName = "A", addressableKey = "a" });
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c" });
    Check("(setup) a and c are downloaded", svc.GetState("a") != null && svc.GetState("a").IsDownloaded && svc.GetState("c") != null && svc.GetState("c").IsDownloaded);

    Caching.FakeCache.Add("car_c|h0");                                   // an older version of c's file is on disk too
    o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("c", done));
    Check("Removing c deletes EVERY version of its own file, not only the current one", o.success && !Caching.FakeCache.Contains("car_c|h1") && !Caching.FakeCache.Contains("car_c|h0"), o.message);
    Check("...and keeps the file a still shares", Caching.FakeCache.Contains("monoscripts|m1") && svc.GetState("a").IsDownloaded);

    Caching.FakeCache.Add("car_c|h0");                                   // only an OLDER version of c is on disk
    MonoBehaviour.Run(svc.ReconcileRoutine(true));
    var cHome = svc.GetHomeVehicles().Find(h => h.vehicleId == "c");
    Check("A vehicle with only an older version stays on Home, marked as needing an update", cHome != null && cHome.needsUpdate);
    var cState = svc.GetState("c");
    Check("...it can't open as it is, and its label says opening it downloads the new version", cState != null && !cState.IsDownloaded && cState.NeedsUpdate && cState.label.StartsWith("Update needed"), cState == null ? "null" : cState.label);
    Check("...and Home's list has it exactly once", DownloadedVehiclesTracker.GetDownloadedVehicles().Count(v => v.vehicleId == "c") == 1);

    Caching.FakeCache.Add("car_a|h0");
    o = Do<RemoveOutcome>(done => svc.RemoveOlderCopiesRoutine(done));
    // (earlier in this suite a's catalog version became h2, so h1 and h0 are both older versions of a's file)
    Check("'Remove older versions' deletes older versions only, never current files", o.success && !Caching.FakeCache.Contains("car_a|h0") && !Caching.FakeCache.Contains("car_a|h1") && !Caching.FakeCache.Contains("car_c|h0") && Caching.FakeCache.Contains("car_a|h2") && Caching.FakeCache.Contains("monoscripts|m1"), string.Join(",", Caching.FakeCache.OrderBy(x => x)));
    Check("...a vehicle that had only an older version is no longer listed; a still is", svc.GetHomeVehicles().All(h => h.vehicleId != "c") && svc.GetState("a").IsDownloaded);

    DownloadedVehiclesTracker.RemoveDownloaded("c");                    // the old list forgot c completely
    Caching.FakeCache.Add("car_c|h1");                                   // ...then it was downloaded again
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c" });
    Check("Downloaded again after removal: back on Home, exactly once", svc.GetState("c").IsDownloaded && DownloadedVehiclesTracker.GetDownloadedVehicles().Count(v => v.vehicleId == "c") == 1);
    Caching.FakeCache.Add("car_c|h0");
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c" });
    Check("A recorded download frees older versions of the same file", !Caching.FakeCache.Contains("car_c|h0") && Caching.FakeCache.Contains("car_c|h1"));

    string released = null;
    Action<string, string> onRelease = (id, key) => released = id + "|" + key;
    VehicleStorageService.ReleaseRequested += onRelease;
    o = Do<RemoveOutcome>(done => svc.RemoveVehicleRoutine("c", done));
    VehicleStorageService.ReleaseRequested -= onRelease;
    Check("Before deleting, the loaders are asked to release that vehicle", released == "c|c" && o.success, released);


    Console.WriteLine("\n=== Delete from the measurement screen (its own generated id + the model key) ===");
    Caching.InUse.Clear();
    Caching.FakeCache.Add("car_c|h1");
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c" });
    Check("(setup) c is downloaded and on Home", svc.GetState("c").IsDownloaded && DownloadedVehiclesTracker.GetDownloadedVehicles().Any(v => v.vehicleId == "c"));
    o = Do<RemoveOutcome>(done => svc.RemoveByKeyRoutine("vehicle_20261008_101500", "c", done));
    Check("Delete with a generated id + the key removes c's files for real", o.success && !Caching.FakeCache.Contains("car_c|h1") && svc.Registry.Get("c") == null, o.message);
    Check("...c is gone from Home, a stays", !DownloadedVehiclesTracker.GetDownloadedVehicles().Any(v => v.vehicleId == "c") && svc.GetState("a").IsDownloaded);
    Caching.FakeCache.Add("car_c|h1");
    DownloadedVehiclesTracker.MarkAsDownloaded(new RemoteVehicleInfo { vehicleId = "c", vehicleName = "C", addressableKey = "c" });
    Check("Added again (downloaded): back on Home, once", DownloadedVehiclesTracker.GetDownloadedVehicles().Count(v => v.vehicleId == "c") == 1);
    o = Do<RemoveOutcome>(done => svc.RemoveByKeyRoutine(null, "c", done));
    Check("Delete with only the key (no id at all) also works", o.success && !Caching.FakeCache.Contains("car_c|h1") && svc.Registry.Get("c") == null, o.message);
    o = Do<RemoveOutcome>(done => svc.RemoveByKeyRoutine("vehicle_x", "c", done));
    Check("Deleting it again: harmless, says nothing is on this PC", o.success && o.message.Contains("aren't on this PC"), o.message);

    Console.WriteLine("\nPassed " + pass + " | Failed " + fail); Environment.Exit(fail == 0 ? 0 : 1);
  }
}
