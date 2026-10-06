using System; using System.Collections; using System.Collections.Generic; using System.Diagnostics; using System.IO; using System.Reflection; using System.Text;
using UnityEngine; using VehicleMeasurement; using VehicleMeasurement.Storage;
class ReportRuntimeTest {
  static int pass, fail;
  static void Check(string n, bool ok, string x = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + n + (x != "" ? "  " + x : "")); }
  static void Touch(string root, string name, string hash, int bytes) { var d = Path.Combine(root, name, hash); Directory.CreateDirectory(d); File.WriteAllBytes(Path.Combine(d, "__data"), new byte[bytes]); File.WriteAllText(Path.Combine(d, "__info"), "x"); }
  static void Main() {
    string root = Path.Combine(Path.GetTempPath(), "vrcache_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
    string pdp = Path.Combine(Path.GetTempPath(), "vrpdp_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(pdp);
    Application.persistentDataPath = pdp; Application.unityVersion = "6000.0.63f1";
    Touch(root, "car_alpha", "h1", 3000);        // current file of alpha
    Touch(root, "shared_mat", "s1", 800);        // shared by alpha and beta
    Touch(root, "car_alpha", "oldhash", 500);    // OLDER copy of a file alpha uses
    Touch(root, "removed_truck", "zz", 700);     // used by no current vehicle
    Caching.Caches.Add(new Cache { valid = true, path = root, spaceOccupied = 5000, spaceFree = 9000000000L, expirationDelay = 157680000, maximumAvailableDiskSpace = 0 });
    Caching.IsCachedFunc = b => (b.name == "car_alpha" && b.hash.Value == "h1") || (b.name == "shared_mat" && b.hash.Value == "s1");
    File.WriteAllText(Path.Combine(pdp, "downloaded_vehicles.json"),
      "{\"vehicles\":[{\"vehicleId\":\"alpha\",\"downloadedDate\":\"2026-09-01 10:00:00\",\"version\":\"1.2\"},{\"vehicleId\":\"beta\",\"downloadedDate\":\"2026-09-02 11:00:00\"},{\"vehicleId\":\"gone\",\"version\":\"1.0\"}]}");

    var tDiag = typeof(StorageDiagnostics);
    var tScan = tDiag.GetNestedType("VehicleScan", BindingFlags.NonPublic);
    Func<string, string, bool, BundleRef[], object> mkScan = (id, ver, found, bs) => {
      var o = Activator.CreateInstance(tScan);
      tScan.GetField("info").SetValue(o, new RemoteVehicleInfo { vehicleId = id, version = ver, addressableKey = id });
      tScan.GetField("keyFound").SetValue(o, found); tScan.GetField("bundles").SetValue(o, new List<BundleRef>(bs)); return o; };
    var alphaA = new BundleRef("car_alpha", "h1", 3000); var sharedB = new BundleRef("shared_mat", "s1", 800);
    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(tScan));
    list.Add(mkScan("alpha", "1.2", true, new[] { alphaA, sharedB }));
    list.Add(mkScan("beta", "2.0", true, new[] { sharedB, new BundleRef("car_beta", "b1", 4000) }));   // car_beta not downloaded
    list.Add(mkScan("ghost", "1.0", false, new BundleRef[0]));
    string text = null; Exception err = null;
    try { text = ((StringBuilder)tDiag.GetMethod("BuildReport", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { list, Stopwatch.StartNew() })).ToString(); }
    catch (TargetInvocationException e) { err = e.InnerException; }
    Check("Report builds without throwing", err == null, err == null ? "" : err.ToString());
    if (text == null) { Console.WriteLine("Passed " + pass + " | Failed " + fail); Environment.Exit(1); }
    if (Environment.GetEnvironmentVariable("SHOW_REPORT") == "1") Console.WriteLine("-------- report text --------\n" + text + "-----------------------------");
    Check("Reads the cache settings", text.Contains("keeps unused bundles for 1825 days") && text.Contains("size limit none"));
    Check("alpha: fully on disk, 2 of 2 files", text.Contains("alpha | v1.2 | 2 | 3 KB | 2/2 | fully on disk") || text.Contains("alpha | v1.2 | 2 | 4 KB | 2/2 | fully on disk"));
    Check("beta: PARTLY on disk, with the missing size", text.Contains("beta | v2.0 | 2 | 5 KB | 1/2 | PARTLY on disk (4 KB missing)"));
    Check("ghost: key not found is called out", text.Contains("ghost") && text.Contains("KEY NOT FOUND"));
    Check("Summary counts", text.Contains("1 fully on disk, 1 partly, 0 not downloaded, 1 with no matching key"));
    Check("Shared files: 3 distinct, 1 shared", text.Contains("3 distinct files in total. Used by more than one vehicle: 1 (sharing saves 800 B)"));
    Check("Disk scan finds all 4 folders", text.Contains("cached files found: 4"));
    Check("Current files: 2", text.Contains("used by a current vehicle:                  2 file(s)"));
    Check("Older copy of a used file is separated out: 1 file, 500 B", text.Contains("OLDER copy of a file a vehicle still uses:  1 file(s), 501 B"));
    Check("Unused leftover: 1 file, 700 B", text.Contains("not used by any current vehicle:            1 file(s), 701 B"));
    Check("Old tracker: 3 listed, 1 without a saved version", text.Contains("vehicles listed: 3. Listed WITHOUT a saved version (these show a false 'update available'): 1."));
    Check("Old tracker id not in catalog is listed", text.Contains("listed under an id that isn't in the catalog: 1 -> gone"));
    Check("Shows the tracker's own date and version for alpha", text.Contains("2026-09-01 10:00:00, version 1.2"));
    Check("Example folder layout is printed", text.Contains("example folder layout"));
    // empty cache + no tracker must not crash
    Caching.Caches.Clear(); File.Delete(Path.Combine(pdp, "downloaded_vehicles.json")); Caching.IsCachedFunc = null;
    try { var t2 = ((StringBuilder)tDiag.GetMethod("BuildReport", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { list, Stopwatch.StartNew() })).ToString();
      Check("Empty cache and no old tracker file: still reports", t2.Contains("cached files found: 0") && t2.Contains("vehicles listed: 0")); }
    catch (Exception e) { Check("Empty cache and no old tracker file: still reports", false, e.ToString()); }
    Console.WriteLine("\nPassed " + pass + " | Failed " + fail); Environment.Exit(fail == 0 ? 0 : 1);
  }
}
