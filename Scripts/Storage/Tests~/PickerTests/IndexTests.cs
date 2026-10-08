// Local vehicle index (Add-vehicle picker no longer loads every bundled vehicle). Run: sh run.sh
using System; using System.Collections.Generic; using VehicleMeasurement;
namespace UnityEngine {
  public class TextAsset : Object { public string text; }
  public static class Resources {
    public static string IndexJson;
    public static int Loads;
    public static T Load<T>(string path) where T : class { Loads++; if (path != LocalVehicleIndex.IndexResource || IndexJson == null) return null; return new TextAsset { text = IndexJson } as T; }
    public static void UnloadAsset(Object o) {}
  }
}
static class IndexTests {
  static int pass, fail;
  static void Check(bool ok, string what) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + what); } }
  static LocalVehicleIndex.Entry E(string n, string p, string y = "") { return new LocalVehicleIndex.Entry { name = n, resourcePath = p, modelYear = y }; }
  static int Main() {
    var all = new List<LocalVehicleIndex.Entry> { E("Thar", "Vehicles/Thar", "2024"), E("XUV700", "Vehicles/SUV/XUV700"), E("Popup", "UI/Popup"), E("thar", "vehicles/Thar"), E("Bolero", "Vehicles/Bolero") };
    var f = LocalVehicleIndex.Filter(all, "Vehicles");
    Check(f.Count == 3, "folder filter keeps Vehicles + subfolders, drops other folders and case duplicates: " + f.Count);
    Check(f[0].name == "Bolero" && f[1].name == "Thar" && f[2].name == "XUV700", "sorted by name");
    Check(LocalVehicleIndex.Filter(all, "/Vehicles/").Count == 3, "slashes around folder are ignored");
    Check(LocalVehicleIndex.Filter(all, "").Count == 4, "empty folder = everything (duplicates removed)");
    Check(LocalVehicleIndex.Filter(null, "Vehicles").Count == 0, "null list");
    Check(LocalVehicleIndex.Filter(all, "Vehicle").Count == 0, "prefix must be a whole folder name");

    string json = LocalVehicleIndex.ToJson(all);
    var back = LocalVehicleIndex.Parse(json);
    Check(back.Count == 5 && back[0].modelYear == "2024" && back[1].resourcePath == "Vehicles/SUV/XUV700", "round trip");
    Check(LocalVehicleIndex.Parse("not json").Count == 0, "damaged file -> empty list, no exception");
    Check(LocalVehicleIndex.Parse("").Count == 0, "empty file");
    Check(LocalVehicleIndex.Parse("{\"vehicles\":[{\"resourcePath\":\"Vehicles/A/Scorpio\"}]}")[0].name == "Scorpio", "missing name -> file name");

    // runtime path: reads the index file once, then cached
    UnityEngine.Resources.IndexJson = json; UnityEngine.Resources.Loads = 0;
    LocalVehicleIndex.EditorScan = null; LocalVehicleIndex.Invalidate();
    var g1 = LocalVehicleIndex.Get("Vehicles"); var g2 = LocalVehicleIndex.Get("Vehicles");
    Check(g1.Count == 3 && ReferenceEquals(g1, g2) && UnityEngine.Resources.Loads == 1, "index read once and cached");
    LocalVehicleIndex.Get("UI");
    Check(UnityEngine.Resources.Loads == 2, "other folder re-reads");

    // missing index in a build -> empty list, never throws
    UnityEngine.Resources.IndexJson = null; LocalVehicleIndex.Invalidate();
    Check(LocalVehicleIndex.Get("Vehicles").Count == 0, "no index -> empty list");

    // editor scan wins and a failing scan falls back to the file
    UnityEngine.Resources.IndexJson = json; LocalVehicleIndex.Invalidate();
    LocalVehicleIndex.EditorScan = folder => LocalVehicleIndex.Filter(new List<LocalVehicleIndex.Entry> { E("New", "Vehicles/New") }, folder);
    Check(LocalVehicleIndex.Get("Vehicles").Count == 1, "editor scan used in Play mode");
    LocalVehicleIndex.Invalidate(); LocalVehicleIndex.EditorScan = folder => { throw new Exception("boom"); };
    Check(LocalVehicleIndex.Get("Vehicles").Count == 3, "scan failure falls back to index file");

    Console.WriteLine("Passed " + pass + ", failed " + fail);
    return fail == 0 ? 0 : 1;
  }
}
