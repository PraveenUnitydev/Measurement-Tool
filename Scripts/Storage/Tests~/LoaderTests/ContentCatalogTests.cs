// Per-vehicle catalogs (vehicles published from Unity). Run: sh run.sh
using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using VehicleMeasurement;
public static class CatT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  public static void Main() {
    var l = new RemoteAddressableVehicleLoader();
    var cat = (List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(l);
    cat.Add(new RemoteVehicleInfo { vehicleId = "creta", addressableKey = "Creta" });                                                           // original vehicle
    for (int i = 0; i < 9; i++) cat.Add(new RemoteVehicleInfo { vehicleId = "v" + i, vehicleName = "V" + i, addressableKey = "das/v" + i, contentCatalogPath = "v/v" + i + "/2026.10.08-1342/catalog_v" + i + ".json" });
    Addressables.CatalogOk = u => !u.Contains("/v/v3/");
    var m = typeof(RemoteAddressableVehicleLoader).GetMethod("LoadVehicleContentCatalogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Sched.Start((System.Collections.IEnumerator)m.Invoke(l, null));
    for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("all 9 published vehicles' catalogs loaded, the original one needs none", Addressables.CatalogUrls.Count == 9);
    Check("from the bundles route", Addressables.CatalogUrls.All(u => u.StartsWith(DasServer.ApiBase + "/bundles/v/")), Addressables.CatalogUrls.FirstOrDefault());
    Check("a failing catalog is reported, the rest still load", l.IsVehicleContentUnavailable("das/v3") && !l.IsVehicleContentUnavailable("v4") && !l.IsVehicleContentUnavailable("creta"));
    Check("handles released", Addressables.Ops.Where(o => o.kind == "catalog").All(o => o.released));
    Addressables.CatalogUrls.Clear();
    Sched.Start((System.Collections.IEnumerator)m.Invoke(l, null));
    for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("next catalog refresh: only the failed one is tried again", Addressables.CatalogUrls.Count == 1 && Addressables.CatalogUrls[0].Contains("/v/v3/"));
    Check("full URL accepted as is", l.ContentCatalogUrl(new RemoteVehicleInfo { contentCatalogPath = "https://cdn.example.com/x/catalog_x.json" }) == "https://cdn.example.com/x/catalog_x.json");
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
