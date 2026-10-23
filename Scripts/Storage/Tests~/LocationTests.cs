using System; using System.IO; using System.Linq; using UnityEngine; using VehicleMeasurement.Storage;
public static class LocationTests { static int pass, fail;
  static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  public static void Main() {
    string root = Path.Combine(Path.GetTempPath(), "dl_" + Guid.NewGuid().ToString("N"));
    string def = Path.Combine(root, "default"), d = Path.Combine(root, "D_Downloads");
    Directory.CreateDirectory(def);
    var defCache = new Cache { valid = true, path = def }; Caching.Caches.Clear(); Caching.Caches.Add(defCache); Caching.currentCacheForWriting = defCache; Caching.DefaultPath = def;

    Console.WriteLine("=== Where downloads go ===");
    Check("By default downloads go to Unity's folder", DownloadLocation.CurrentFolder == def && !DownloadLocation.IsCustom);
    // a vehicle already downloaded in the default folder
    Directory.CreateDirectory(Path.Combine(def, "vehicleremote_assets_creta", "abc")); File.WriteAllBytes(Path.Combine(def, "vehicleremote_assets_creta", "abc", "__data"), new byte[5000]);
    string err = DownloadLocation.SetFolder(d);
    Check("Choosing another folder works, new downloads go there", err == null && DownloadLocation.CurrentFolder == Path.GetFullPath(d) && Directory.Exists(d), err);
    Check("...it is remembered for the next start", PlayerPrefs.GetString("das.downloadFolder") == Path.GetFullPath(d) && DownloadLocation.IsCustom);
    Check("...and it is read first", Caching.Caches[0].path == Path.GetFullPath(d));
    Check("...the old folder stays readable (its vehicles still open)", Caching.Caches.Any(c => c.path == def));
    Check("Files elsewhere are counted for 'Move downloads here'", DownloadLocation.BytesElsewhere() == 5000, "" + DownloadLocation.BytesElsewhere());

    Console.WriteLine("=== Moving existing downloads ===");
    float lastP = -1; bool ok = false; string msg = null;
    MonoBehaviour.Run(DownloadLocation.MoveDownloadsHere((p, s) => lastP = p, (o, m) => { ok = o; msg = m; }));
    Check("Moved: success message asks for a restart", ok && msg.Contains("Restart DAS"), msg);
    Check("...the file is in the new folder, byte for byte", File.Exists(Path.Combine(d, "vehicleremote_assets_creta", "abc", "__data")) && new FileInfo(Path.Combine(d, "vehicleremote_assets_creta", "abc", "__data")).Length == 5000);
    Check("...and gone from the old one", !Directory.Exists(Path.Combine(def, "vehicleremote_assets_creta")));
    Check("...progress reached 100%", lastP >= 0.999f);
    MonoBehaviour.Run(DownloadLocation.MoveDownloadsHere((p, s) => { }, (o, m) => { ok = o; msg = m; }));
    Check("Moving again: nothing to move", ok && (msg.Contains("nothing") || msg.Contains("already")), msg);

    Console.WriteLine("=== Bad choices are refused ===");
    string blocker = Path.Combine(root, "afile"); File.WriteAllText(blocker, "x");
    string bad = DownloadLocation.SetFolder(Path.Combine(blocker, "sub"));       // a FILE in the way: can't create the folder
    Check("A folder that can't be created is refused with a reason, and nothing changes", bad != null && DownloadLocation.CurrentFolder == Path.GetFullPath(d), bad);
    Check("A path with invalid characters is refused", DownloadLocation.SetFolder("\0bad") != null);

    Console.WriteLine("=== Back to the default ===");
    err = DownloadLocation.SetFolder(null);
    Check("Reset: downloads go to the default again, choice forgotten", err == null && DownloadLocation.CurrentFolder == def && !DownloadLocation.IsCustom, err);
    Check("...the folder used before is remembered so its vehicles keep opening", PlayerPrefs.GetString("das.downloadFolder.previous").Contains(Path.GetFullPath(d)));

    Console.WriteLine("=== Disk space ===");
    Check("A normal download fits", DiskSpace.ProblemFor(10L << 20) == null);
    string p2 = DiskSpace.ProblemFor(1L << 55);
    Check("An impossible download is refused with need / free / what to do", p2 != null && p2.StartsWith("Not enough free space") && p2.Contains("needs") && p2.Contains("Storage"), p2);
    Check("Free space is readable", DiskSpace.FreeOnDownloadDrive() > 0);
    try { Directory.Delete(root, true); } catch { }
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
  } }
