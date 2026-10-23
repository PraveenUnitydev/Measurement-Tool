// Published vehicles keep the app's original key (DasKeys). Run: sh run.sh
using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using VehicleMeasurement;
public static class KeyT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  public static void Main() {
    Check("original vehicle unchanged", DasKeys.AppKeyFor("Creta", "Creta", null) == "Creta");
    Check("published, no legacyKey -> vehicle id", DasKeys.AppKeyFor("3XO", "das/3xo", null) == "3XO");
    Check("published, legacyKey wins (id differs from old key)", DasKeys.AppKeyFor("3008", "das/3008", "Peugeot3008") == "Peugeot3008");
    Check("legacyKey that is itself das/ ignored", DasKeys.AppKeyFor("x1", "das/x1", "das/x1") == "x1");

    var list = new List<RemoteVehicleInfo> {
      new RemoteVehicleInfo { vehicleId = "Creta", addressableKey = "Creta" },
      new RemoteVehicleInfo { vehicleId = "3XO", addressableKey = "das/3xo", contentCatalogPath = "v/3xo/a/catalog_3xo.json" },
      new RemoteVehicleInfo { vehicleId = "3008", addressableKey = "das/3008", legacyKey = "Peugeot3008" },
    };
    RemoteAddressableVehicleLoader.ApplyAppKeys(list);
    Check("app sees the original key", list[1].addressableKey == "3XO" && list[2].addressableKey == "Peugeot3008" && list[0].addressableKey == "Creta");
    Check("real address kept", list[1].address == "das/3xo" && list[0].address == "Creta");
    Check("Addressables calls translate, any casing", DasKeys.Real("3XO") == "das/3xo" && DasKeys.Real("3xo") == "das/3xo" && DasKeys.Real("Peugeot3008") == "das/3008");
    Check("unknown keys, labels and real addresses pass through", DasKeys.Real("Creta") == "Creta" && DasKeys.Real("vehicles") == "vehicles" && DasKeys.Real("das/3xo") == "das/3xo" && DasKeys.Real(null) == null);
    RemoteAddressableVehicleLoader.ApplyAppKeys(new List<RemoteVehicleInfo> { new RemoteVehicleInfo { vehicleId = "3XO", addressableKey = "3XO" } });
    Check("switched back to the original build (rollback): mapping gone", DasKeys.Real("3XO") == "3XO");

    // loader lookups by id, key, address or casing all find the vehicle and its thumbnail
    RemoteAddressableVehicleLoader.ApplyAppKeys(list = new List<RemoteVehicleInfo> {
      new RemoteVehicleInfo { vehicleId = "3XO", addressableKey = "das/3xo" },
      new RemoteVehicleInfo { vehicleId = "3008", addressableKey = "das/3008", legacyKey = "Peugeot3008" } });
    var l = new RemoteAddressableVehicleLoader();
    var cat = (List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(l);
    cat.AddRange(list);
    Check("GetVehicleInfo by id/key/address/casing", l.GetVehicleInfo("3XO") == list[0] && l.GetVehicleInfo("das/3xo") == list[0] && l.GetVehicleInfo("3xo") == list[0] && l.GetVehicleInfo("Peugeot3008") == list[1]);
    var set = typeof(RemoteAddressableVehicleLoader).GetMethod("SetThumbnail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    var told = new List<string>();
    l.ThumbnailUpdated += (id, s) => told.Add(id);
    set.Invoke(l, new object[] { "3008", new byte[] { 1, 2, 3 } });
    set.Invoke(l, new object[] { "3XO", new byte[] { 1, 2, 3 } });
    Check("thumbnail found by vehicle id, app key and real address", l.GetThumbnail("3008") != null && l.GetThumbnail("Peugeot3008") != null && l.GetThumbnail("das/3008") != null && l.GetThumbnail("das/3xo") != null && l.GetThumbnail("3xo") != null);
    Check("screens listing by key are told too", told.Contains("3008") && told.Contains("Peugeot3008") && told.Contains("3XO"));
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
