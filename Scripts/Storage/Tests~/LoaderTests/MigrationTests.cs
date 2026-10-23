// Saved measurements of published vehicles (das_<id>.json) moved back to the vehicle's own key; crash-safe saves.
using System; using System.Collections.Generic; using System.IO; using System.Linq; using UnityEngine; using VehicleMeasurement;
public static class MigT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  // JSON stand-in: keeps a copy of the object and writes a token
  static readonly Dictionary<string, object> store = new Dictionary<string, object>();
  static object Clone(object o) { return typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(o, null); }
  static SavedVehicleMeasurement Saved(string id, string modelPath, string addrId, string name) {
    var d = new SavedVehicleMeasurement { vehicleId = id, vehicleName = name, modelPath = modelPath, addressableVehicleId = addrId, modelLoadType = "Addressables", savedDate = "2026-10-09" };
    VehicleMeasurementStorage.SaveData(d, id); return d;
  }
  public static void Main() {
    JsonUtility.ToHook = o => { string t = "#" + Guid.NewGuid().ToString("N"); store[t] = Clone(o); return t; };
    JsonUtility.FromHook = (s, t) => { object o; return s != null && store.TryGetValue(s.Trim(), out o) ? Clone(o) : null; };
    string root = Path.Combine(Path.GetTempPath(), "das-mig-test-" + Guid.NewGuid().ToString("N"));
    Application.persistentDataPath = root;

    var l = new RemoteAddressableVehicleLoader();
    var list = new List<RemoteVehicleInfo> {
      new RemoteVehicleInfo { vehicleId = "3XO", vehicleName = "3XO", addressableKey = "das/3xo", contentCatalogPath = "v/3xo/a/catalog_3xo.json" },
      new RemoteVehicleInfo { vehicleId = "M210", vehicleName = "M210", addressableKey = "das/m210", contentCatalogPath = "v/m210/a/catalog_m210.json" },
      new RemoteVehicleInfo { vehicleId = "3008", vehicleName = "Peugeot 3008", addressableKey = "das/3008", legacyKey = "Peugeot3008" },
      new RemoteVehicleInfo { vehicleId = "Creta", vehicleName = "Creta", addressableKey = "Creta" } };
    RemoteAddressableVehicleLoader.ApplyAppKeys(list);
    var cat = (List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(l);
    cat.AddRange(list);

    Saved("das_3xo", "das/3xo", "das/3xo", "3XO");                    // measured while published: moved to 3XO
    File.WriteAllBytes(VehicleMeasurementStorage.GetThumbnailPath("das_3xo"), new byte[] { 7 });
    Saved("M210", "M210", "M210", "M210 old"); Saved("das_m210", "das/m210", "das/m210", "M210 new");   // both: both kept
    Saved("das_3008", "das/3008", "das/3008", "3008");                // id differs from the old key: goes to Peugeot3008
    Saved("Creta", "Creta", "Creta", "Creta");                        // untouched
    Saved("das_ghost", "das/ghost", "das/ghost", "Ghost");             // not in the list: untouched

    int n = MeasurementKeyMigration.Run(l);
    var ids = VehicleMeasurementStorage.GetSavedVehicleIds().OrderBy(x => x).ToArray();
    Check("das_3xo moved to 3XO", VehicleMeasurementStorage.Exists("3XO") && !VehicleMeasurementStorage.Exists("das_3xo"), string.Join(",", ids));
    var x3 = VehicleMeasurementStorage.Load("3XO");
    Check("moved file opens the vehicle by its key", x3 != null && x3.modelPath == "3XO" && x3.addressableVehicleId == "3XO" && x3.vehicleId == "3XO");
    Check("its thumbnail copy moved too", File.Exists(VehicleMeasurementStorage.GetThumbnailPath("3XO")) && !File.Exists(VehicleMeasurementStorage.GetThumbnailPath("das_3xo")));
    Check("both exist: nothing deleted, the published one repaired", VehicleMeasurementStorage.Exists("M210") && VehicleMeasurementStorage.Exists("das_m210")
      && VehicleMeasurementStorage.Load("das_m210").modelPath == "M210" && VehicleMeasurementStorage.Load("M210").vehicleName == "M210 old");
    Check("legacy key used as the file name", VehicleMeasurementStorage.Exists("Peugeot3008") && VehicleMeasurementStorage.Load("Peugeot3008").modelPath == "Peugeot3008");
    Check("other files untouched", VehicleMeasurementStorage.Load("Creta").modelPath == "Creta" && VehicleMeasurementStorage.Load("das_ghost").modelPath == "das/ghost");
    Check("count reported", n == 3, n.ToString());
    Check("second run changes nothing (no rewrite on every start)", MeasurementKeyMigration.Run(l) == 0);

    // crash-safe save: previous save kept as .bak and used when the file is damaged
    Saved("Thar", "Thar", "Thar", "v1"); Saved("Thar", "Thar", "Thar", "v2");
    string file = Path.Combine(VehicleMeasurementStorage.StoragePath, "Thar.json");
    Check("previous save kept", File.Exists(file + ".bak") && !File.Exists(file + ".tmp"));
    File.WriteAllText(file, "");                                       // power cut mid-write with an older version
    var rec = VehicleMeasurementStorage.Load("Thar");
    Check("damaged file: previous save used", rec != null && rec.vehicleName == "v1");
    Check(".bak and .tmp files are not listed as vehicles", !VehicleMeasurementStorage.GetSavedVehicleIds().Any(i => i.Contains(".")));
    VehicleMeasurementStorage.Delete("Thar");
    Check("delete removes the backup too", !File.Exists(file) && !File.Exists(file + ".bak"));

    // identity rules
    Check("FileId", VehicleIdentity.FileId("das/3xo") == "das_3xo" && VehicleIdentity.FileId("Thar Roxx") == "Thar_Roxx" && VehicleIdentity.FileId("RAV 4 - 2026") == "RAV_4_-_2026");
    Check("Matches any name of a vehicle", VehicleIdentity.Matches("das_3008", "3008", "Peugeot3008", "das/3008", "Peugeot3008") && VehicleIdentity.Matches("peugeot3008", "3008", "Peugeot3008", null, null)
      && VehicleIdentity.Matches("Thar_Roxx", "Thar Roxx", "TharRoxx", null, null) && !VehicleIdentity.Matches("xuv", "xuv700", "XUV700", null, null) && !VehicleIdentity.Matches("", "a", "a", null, null));
    Check("loader finds a vehicle by its saved file id", l.GetVehicleInfo("das_3xo") == list[0] && l.GetVehicleInfo("Peugeot3008") == list[2] && l.GetVehicleInfo("das_3008") == list[2] && l.GetVehicleInfo("xuv") == null);
    try { Directory.Delete(root, true); } catch (Exception) { }
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
