using System; using System.Collections; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using UnityEngine.ResourceManagement.AsyncOperations; using VehicleMeasurement; using VehicleMeasurement.Storage;
public static class DT { static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  static void Frames(int n) { for (int i = 0; i < n; i++) Sched.Step(); }
  static Op Last(string kind, string key) { return Addressables.Ops.LastOrDefault(o => o.kind == kind && o.key == key); }
  static void Finish(string key, bool ok = true, string err = null) { var o = Last("download", key); o.dl = o.total; o.done = true; o.status = ok ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed; if (!ok) o.ex = new Exception(err); }
  static bool Under(GameObject g, GameObject root) { var t = g.transform; while (t != null) { if (t.gameObject == root) return true; t = t.parent; } return false; }
  public static void Main() {
    var loader = new RemoteAddressableVehicleLoader();
    typeof(RemoteAddressableVehicleLoader).GetProperty("Instance").SetValue(null, loader);
    var cat = (System.Collections.Generic.List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(loader);
    foreach (var k in new[] { "Creta", "Thar", "Kylaq", "Huge", "Bad" }) cat.Add(new RemoteVehicleInfo { vehicleId = k, vehicleName = k, addressableKey = k });
    Addressables.Sizes["Creta"] = 400L << 20; Addressables.Sizes["Thar"] = 200L << 20; Addressables.Sizes["Kylaq"] = 0; Addressables.Sizes["Huge"] = 1L << 55; Addressables.Sizes["Bad"] = 10L << 20;

    Console.WriteLine("=== Explicit downloads ===");
    long m = -2; Sched.Start(VehicleDownloads.MissingBytes("Creta", x => m = x)); Frames(1);
    Check("Missing bytes reported before downloading", m == 400L << 20);
    bool? ok = null; string msg = null; float lastFrac = 0;
    Sched.Start(VehicleDownloads.Download("Creta", p => lastFrac = p.fraction, (o, s) => { ok = o; msg = s; })); Frames(2);
    Check("Downloading", VehicleDownloads.IsDownloading("Creta") && ok == null);
    bool? ok2 = null; Sched.Start(VehicleDownloads.Download("Creta", null, (o, s) => ok2 = o)); Frames(2);
    Check("A second request for the same vehicle joins the first (one download)", Addressables.Ops.Count(o => o.kind == "download" && o.key == "Creta") == 1);
    var op = Last("download", "Creta"); op.dl = op.total / 2; Frames(2);
    Check("Real progress (50%)", Math.Abs(lastFrac - 0.5f) < 0.01f, "" + lastFrac);
    Finish("Creta"); Frames(4);
    Check("Done: both callers told, vehicle recorded, handle released", ok == true && ok2 == true && DownloadedVehiclesTracker.Marked.Contains("Creta") && op.released && !VehicleDownloads.IsDownloading("Creta"));
    bool? ok3 = null; Sched.Start(VehicleDownloads.Download("Kylaq", null, (o, s) => { ok3 = o; msg = s; })); Frames(2);
    Check("Already on disk: success without downloading, and recorded", ok3 == true && Last("download", "Kylaq") == null && DownloadedVehiclesTracker.Marked.Contains("Kylaq"));
    bool? ok4 = null; Sched.Start(VehicleDownloads.Download("Huge", null, (o, s) => { ok4 = o; msg = s; })); Frames(2);
    Check("Not enough space: refused before starting, clear message", ok4 == false && msg.StartsWith("Not enough free space") && Last("download", "Huge") == null, msg);
    bool? ok5 = null; Sched.Start(VehicleDownloads.Download("Bad", null, (o, s) => { ok5 = o; msg = s; })); Frames(2); Finish("Bad", false, "Curl error 52: Empty reply from server"); Frames(4);
    Check("A failed download reports it in plain words", ok5 == false && msg.StartsWith("The download was interrupted"), msg);
    bool hide = false; bool? ok6 = null; DownloadedVehiclesTracker.Marked.Clear();
    Sched.Start(VehicleDownloads.Download("Thar", null, (o, s) => ok6 = o, () => hide)); Frames(2); hide = true; Frames(2);
    Check("'Continue in background': the screen stops waiting...", ok6 == null && Sched.Running >= 1);
    Finish("Thar"); Frames(4);
    Check("...and the download still completes and is recorded", DownloadedVehiclesTracker.Marked.Contains("Thar") && !VehicleDownloads.IsDownloading("Thar"));

    Console.WriteLine("=== Dialogs ===");
    int yes = 0, no = 0;
    var d = DasDialog.Confirm("Download Creta?", "402 MB", "Download", "Not now", () => yes++, () => no++);
    var buttons = GameObject.All.Where(g => !g.destroyed).SelectMany(g => g.comps.OfType<UnityEngine.UI.Button>()).ToList();
    Check("Confirm shows two buttons", buttons.Count(b => b.gameObject.activeSelf) == 2);
    buttons.First(b => b.gameObject.name == "Primary").onClick.Invoke();
    Check("'Download' runs only its action and closes the dialog", yes == 1 && no == 0 && d.gameObject.destroyed);
    var d2 = DasDialog.Confirm("Q", "M", "Yes", "No", () => yes++, () => no++);
    GameObject.All.Where(g => Under(g, d2.gameObject)).SelectMany(g => g.comps.OfType<UnityEngine.UI.Button>()).First(b => b.gameObject.name == "Secondary").onClick.Invoke();
    Check("'Not now' runs only its action and closes", no == 1 && yes == 1 && d2.gameObject.destroyed);
    var pd = DasDialog.Progress("Downloading", "x", "Continue in background", () => hide = true); pd.SetProgress(0.4f, "160 MB of 400 MB");

    Check("Progress dialog shows the detail text", GameObject.All.Where(g => !g.destroyed).SelectMany(g => g.comps.OfType<TMPro.TextMeshProUGUI>()).Any(t => t.text == "160 MB of 400 MB"));
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
  } }
