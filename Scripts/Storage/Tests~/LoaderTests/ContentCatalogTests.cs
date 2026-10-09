// Per-vehicle catalogs (vehicles published from Unity). Run: sh run.sh
using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using UnityEngine.Networking; using VehicleMeasurement;
public static class CatT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  static System.Reflection.BindingFlags NP = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
  static void Run(RemoteAddressableVehicleLoader l, string method, params object[] args) {
    var m = typeof(RemoteAddressableVehicleLoader).GetMethod(method, NP);
    var r = m.Invoke(l, args);
    if (r is System.Collections.IEnumerator) { Sched.Start((System.Collections.IEnumerator)r); for (int i = 0; i < 400 && Sched.Running > 0; i++) Sched.Step(); }
  }
  public static void Main() {
    string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "das-cat-test-" + Guid.NewGuid().ToString("N"));
    Application.persistentDataPath = root;
    var serverDown = new HashSet<string>();
    UnityWebRequest.Server = r => { if (serverDown.Any(d => r.url.Contains(d))) { r.result = UnityWebRequest.Result.ConnectionError; r.error = "Cannot connect"; } else r.downloadHandler.data = System.Text.Encoding.UTF8.GetBytes("{\"catalog\":\"" + r.url + "\"}"); };

    var l = new RemoteAddressableVehicleLoader();
    var list = new List<RemoteVehicleInfo>();
    list.Add(new RemoteVehicleInfo { vehicleId = "creta", addressableKey = "Creta" });                                                           // original vehicle
    for (int i = 0; i < 9; i++) list.Add(new RemoteVehicleInfo { vehicleId = "v" + i, vehicleName = "V" + i, addressableKey = "das/v" + i, contentCatalogPath = "v/v" + i + "/2026.10.08-1342/catalog_v" + i + ".json" });
    serverDown.Add("/v/v3/");
    Run(l, "LoadVehicleContentCatalogs", list);
    Run(l, "CommitCatalog", list, "1");
    Check("8 published catalogs downloaded from the bundles route", UnityWebRequest.Sent.Count(r => r.url.StartsWith(DasServer.ApiBase + "/bundles/v/")) == 9, UnityWebRequest.Sent.FirstOrDefault()?.url);
    Check("loaded from the copy kept on this PC", Addressables.CatalogUrls.Count == 8 && Addressables.CatalogUrls.All(u => u.StartsWith(root)), Addressables.CatalogUrls.FirstOrDefault());
    Check("one copy per vehicle on disk", System.IO.Directory.GetFiles(System.IO.Path.Combine(root, "DAS", "vehicle-catalogs"), "*.json", System.IO.SearchOption.AllDirectories).Length == 8);
    Check("a failing catalog is reported, the rest still load", l.IsVehicleContentUnavailable("v3") && !l.IsVehicleContentUnavailable("v4") && !l.IsVehicleContentUnavailable("creta"));
    Check("handles released", Addressables.Ops.Where(o => o.kind == "catalog").All(o => o.released));

    // next start: offline, catalogs come from this PC
    serverDown.Add("/bundles/"); UnityWebRequest.Sent.Clear(); Addressables.CatalogUrls.Clear();
    var l2 = new RemoteAddressableVehicleLoader();
    Run(l2, "LoadVehicleContentCatalogs", list);
    Check("offline start: kept copies load without the server", Addressables.CatalogUrls.Count == 8 && UnityWebRequest.Sent.Count == 1, Addressables.CatalogUrls.Count + "/" + UnityWebRequest.Sent.Count);

    // server back: a vehicle that failed is tried again on demand (download / open)
    serverDown.Clear(); Addressables.CatalogUrls.Clear();
    Sched.Start(l.EnsureVehicleContent("V3")); for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("failed catalog loads on demand later", !l.IsVehicleContentUnavailable("v3") && Addressables.CatalogUrls.Count == 1);
    Sched.Start(l.EnsureVehicleContent("v4")); for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("already loaded: nothing again", Addressables.CatalogUrls.Count == 1);

    // republished: the new version replaces the old one (old locator removed, old copy deleted)
    Addressables.RemovedLocators.Clear();
    var v5 = list.First(v => v.vehicleId == "v5");
    string oldCopy = RemoteAddressableVehicleLoader.LocalContentCatalogPath(v5);
    v5.contentCatalogPath = "v/v5/2026.10.09-0900/catalog_v5.json";
    Run(l, "LoadVehicleContentCatalogs", list);
    Run(l, "CommitCatalog", list, "2");
    Check("republish: old catalog of the vehicle unloaded", Addressables.RemovedLocators.Count == 1 && ((UnityEngine.AddressableAssets.ResourceLocators.StubLocator)Addressables.RemovedLocators[0]).id == oldCopy, Addressables.RemovedLocators.Count.ToString());
    Check("republish: old copy deleted, new one kept", !System.IO.File.Exists(oldCopy) && System.IO.File.Exists(RemoteAddressableVehicleLoader.LocalContentCatalogPath(v5)));
    // switched back to the original build: its published catalog is unloaded
    var v6 = list.First(v => v.vehicleId == "v6"); v6.contentCatalogPath = ""; v6.addressableKey = "V6";
    Addressables.RemovedLocators.Clear();
    Run(l, "CommitCatalog", list, "3");
    Check("switched back: published catalog unloaded", Addressables.RemovedLocators.Count == 1);
    // the same catalog asked for twice at once (list load + opening the vehicle): loaded once
    var v7 = list.First(v => v.vehicleId == "v7"); v7.contentCatalogPath = "v/v7/2026.10.09-1000/catalog_v7.bin";
    Addressables.CatalogUrls.Clear(); UnityWebRequest.Sent.Clear();
    var m = typeof(RemoteAddressableVehicleLoader).GetMethod("LoadOneContentCatalog", NP);
    int okCount = 0;
    Sched.Start((System.Collections.IEnumerator)m.Invoke(l, new object[] { v7, (Action<bool>)(b => { if (b) okCount++; }) }));
    Sched.Start((System.Collections.IEnumerator)m.Invoke(l, new object[] { v7, (Action<bool>)(b => { if (b) okCount++; }) }));
    for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("loaded once, both callers told it worked", okCount == 2 && Addressables.CatalogUrls.Count == 1 && UnityWebRequest.Sent.Count == 1, okCount + "/" + Addressables.CatalogUrls.Count + "/" + UnityWebRequest.Sent.Count);
    Check("binary catalog kept byte for byte", System.IO.File.ReadAllBytes(RemoteAddressableVehicleLoader.LocalContentCatalogPath(v7)).Length > 0 && RemoteAddressableVehicleLoader.LocalContentCatalogPath(v7).EndsWith(".bin"));
    // a damaged kept copy: the server's copy is used instead
    var v8 = list.First(v => v.vehicleId == "v8"); v8.contentCatalogPath = "v/v8/2026.10.09-1100/catalog_v8.json";
    string bad = RemoteAddressableVehicleLoader.LocalContentCatalogPath(v8);
    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(bad)); System.IO.File.WriteAllText(bad, "garbage");
    Addressables.CatalogOk = u => u != bad;
    Addressables.CatalogUrls.Clear();
    bool v8ok = false;
    Sched.Start((System.Collections.IEnumerator)m.Invoke(l, new object[] { v8, (Action<bool>)(b => v8ok = b) })); for (int i = 0; i < 200 && Sched.Running > 0; i++) Sched.Step();
    Check("damaged kept copy: server copy used, damaged one removed", v8ok && Addressables.CatalogUrls.Count == 2 && Addressables.CatalogUrls[1].StartsWith(DasServer.ApiBase) && !System.IO.File.Exists(bad));
    Addressables.CatalogOk = u => true;
    Check("full URL accepted as is", l.ContentCatalogUrl(new RemoteVehicleInfo { contentCatalogPath = "https://cdn.example.com/x/catalog_x.json" }) == "https://cdn.example.com/x/catalog_x.json");

    // list validation: broken answers never replace the list or the offline copy
    string ver;
    bool threw = false; try { RemoteAddressableVehicleLoader.ValidateCatalog(new RemoteCatalogData(), out ver); } catch (FormatException) { threw = true; }
    Check("empty list refused", threw);
    threw = false; try { RemoteAddressableVehicleLoader.ValidateCatalog(new RemoteCatalogData { vehicles = new List<RemoteVehicleInfo> { null, new RemoteVehicleInfo { vehicleId = " " } } }, out ver); } catch (FormatException) { threw = true; }
    Check("list without usable vehicles refused", threw);
    var ok = RemoteAddressableVehicleLoader.ValidateCatalog(new RemoteCatalogData { version = "9", vehicles = new List<RemoteVehicleInfo> {
      new RemoteVehicleInfo { vehicleId = " xuv " }, new RemoteVehicleInfo { vehicleId = "XUV", vehicleName = "dup" }, null, new RemoteVehicleInfo { vehicleId = "thar", addressableKey = "Thar" } } }, out ver);
    Check("ids trimmed, duplicates and empty entries dropped, key/name filled", ok.Count == 2 && ok[0].vehicleId == "xuv" && ok[0].addressableKey == "xuv" && ok[0].vehicleName == "xuv" && ver == "9");
    // update check: only real changes count (signed picture links differ on every request)
    var a1 = new List<RemoteVehicleInfo> { new RemoteVehicleInfo { vehicleId = "b", version = "1", thumbnailUrl = "https://x?sig=1", thumbnailVersion = "7" }, new RemoteVehicleInfo { vehicleId = "a", version = "1" } };
    var a2 = new List<RemoteVehicleInfo> { new RemoteVehicleInfo { vehicleId = "a", version = "1" }, new RemoteVehicleInfo { vehicleId = "b", version = "1", thumbnailUrl = "https://x?sig=2", thumbnailVersion = "7" } };
    Check("same list, other order and links: no change", RemoteAddressableVehicleLoader.Signature(a1) == RemoteAddressableVehicleLoader.Signature(a2));
    a2[1].version = "2";
    Check("new version: change", RemoteAddressableVehicleLoader.Signature(a1) != RemoteAddressableVehicleLoader.Signature(a2));
    a2[1].version = "1"; a2[1].thumbnailVersion = "8";
    Check("new picture: change", RemoteAddressableVehicleLoader.Signature(a1) != RemoteAddressableVehicleLoader.Signature(a2));
    try { System.IO.Directory.Delete(root, true); } catch (Exception) { }
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
