using System; using System.Collections.Generic; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using UnityEngine.ResourceManagement.AsyncOperations; using VehicleMeasurement;
class Screen : MonoBehaviour { public List<string> loaded = new List<string>(), errors = new List<string>(); public bool throwOnLoad;
  public void OnLoaded(GameObject g) { if (throwOnLoad) throw new NullReferenceException("SetupMeasurementSystem blew up"); loaded.Add(g.name); }
  public void OnError(string e) { errors.Add(e); } }
public static class T { static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  static Op Last(string kind, string key) { return Addressables.Ops.LastOrDefault(o => o.kind == kind && o.key == key); }
  static void Frames(int n) { for (int i = 0; i < n; i++) Sched.Step(); }
  static void FinishDownload(string key) { var o = Last("download", key); o.dl = o.total; o.done = true; o.status = AsyncOperationStatus.Succeeded; }
  static void FinishInstantiate(string key) { var o = Last("instantiate", key); var g = new GameObject(key + "(Clone)"); o.result = g; Addressables.Instances.Add(g); o.done = true; o.status = AsyncOperationStatus.Succeeded; }
  public static void Main() {
    var loader = new RemoteAddressableVehicleLoader();
    var cat = (List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(loader);
    foreach (var k in new[] { "Creta", "Thar", "Kylaq", "Huge" }) cat.Add(new RemoteVehicleInfo { vehicleId = k, vehicleName = k, addressableKey = k });
    Addressables.Sizes["Creta"] = 400L << 20; Addressables.Sizes["Thar"] = 500L << 20; Addressables.Sizes["Kylaq"] = 0; Addressables.Sizes["Huge"] = 1L << 55;

    Console.WriteLine("=== 1. Back while downloading (the reported crash) ===");
    var screenA = new Screen(); var containerA = new Transform();
    loader.LoadVehicle("Creta", screenA.OnLoaded, screenA.OnError, containerA);
    Frames(3);
    Check("Downloading: loader busy", loader.IsLoading && Last("download", "Creta") != null && !Last("download", "Creta").done);
    screenA.destroyed = true; containerA.destroyed = true; loader.CancelLoad();          // Back: scene unloaded, OnDestroy cancels
    Check("After Back the loader is free at once", !loader.IsLoading);
    FinishDownload("Creta"); Frames(5);
    Check("The finished download never calls the destroyed screen (no NullReferenceException)", screenA.loaded.Count == 0 && screenA.errors.Count == 0);
    Check("...no vehicle is created into the destroyed screen / next scene", Last("instantiate", "Creta") == null);
    Check("...but the download is recorded, so the vehicle shows on Home", DownloadedVehiclesTracker.Marked.Contains("Creta"));

    Console.WriteLine("=== 2. Screen destroyed without cancelling (safety net) ===");
    var screenB = new Screen(); var containerB = new Transform();
    loader.LoadVehicle("Thar", screenB.OnLoaded, screenB.OnError, containerB); Frames(2);
    screenB.destroyed = true; containerB.destroyed = true;
    Frames(2);
    Check("Loader notices the screen is gone and frees itself", !loader.IsLoading);
    FinishDownload("Thar"); Frames(5);
    Check("...no callback into the dead screen, no stray vehicle", screenB.loaded.Count == 0 && Last("instantiate", "Thar") == null);

    Console.WriteLine("=== 3. Load the same vehicle again after Back ===");
    var screenC = new Screen(); var containerC = new Transform();
    Addressables.Sizes["Creta"] = 0;                                                   // files are on disk now
    loader.LoadVehicle("Creta", screenC.OnLoaded, screenC.OnError, containerC); Frames(2);
    Check("No 'another vehicle is loading' error", screenC.errors.Count == 0, string.Join("|", screenC.errors));
    FinishInstantiate("Creta"); Frames(2);
    Check("It loads and the new screen gets it", screenC.loaded.SequenceEqual(new[] { "Creta(Clone)" }) && !loader.IsLoading);
    Check("...created under the new screen's container", Last("instantiate", "Creta").parent == containerC);

    Console.WriteLine("=== 4. Load another vehicle while one is downloading ===");
    Addressables.Sizes["Thar"] = 500L << 20;
    var screenD = new Screen(); var containerD = new Transform();
    loader.LoadVehicle("Thar", screenD.OnLoaded, screenD.OnError, containerD); Frames(2);
    loader.LoadVehicle("Kylaq", screenD.OnLoaded, screenD.OnError, containerD); Frames(2);
    Check("The new vehicle is accepted (old one replaced), no error", screenD.errors.Count == 0 && loader.LoadingKey == "Kylaq", loader.LoadingKey + " " + string.Join("|", screenD.errors));
    FinishInstantiate("Kylaq"); FinishDownload("Thar"); Frames(5);
    Check("Only the vehicle asked for last is shown", screenD.loaded.SequenceEqual(new[] { "Kylaq(Clone)" }), string.Join(",", screenD.loaded));
    Check("...the replaced download still finished in the background and was recorded", DownloadedVehiclesTracker.Marked.Contains("Thar"));

    Console.WriteLine("=== 5. A screen callback that throws can't jam the loader ===");
    var screenE = new Screen { throwOnLoad = true }; var containerE = new Transform(); Addressables.Sizes["Thar"] = 0;
    loader.LoadVehicle("Thar", screenE.OnLoaded, screenE.OnError, containerE); Frames(2); FinishInstantiate("Thar"); Frames(2);
    Check("Exception is logged, loader free afterwards", !loader.IsLoading && UnityEngine.Debug.Logs.Any(l => l.Contains("SetupMeasurementSystem blew up")));
    var screenF = new Screen(); loader.LoadVehicle("Creta", screenF.OnLoaded, screenF.OnError, new Transform()); Frames(2); FinishInstantiate("Creta"); Frames(2);
    Check("...and the next load works", screenF.loaded.Count == 1 && screenF.errors.Count == 0);
    Check("Loading a new vehicle released the previous one (no leak)", Addressables.ReleasedInstances.Any(g => g.name == "Thar(Clone)"));

    Console.WriteLine("=== 6. Not enough disk space ===");
    var screenG = new Screen(); loader.LoadVehicle("Huge", screenG.OnLoaded, screenG.OnError, new Transform()); Frames(3);
    Check("Refused before downloading, with a clear message", screenG.errors.Count == 1 && screenG.errors[0].StartsWith("Not enough free space") && Last("download", "Huge") == null, string.Join("|", screenG.errors));
    Check("...loader free", !loader.IsLoading);

    Console.WriteLine("=== 7. Left while the vehicle was being created ===");
    var screenH = new Screen(); var containerH = new Transform(); Addressables.Sizes["Kylaq"] = 0;
    loader.LoadVehicle("Kylaq", screenH.OnLoaded, screenH.OnError, containerH); Frames(2);
    containerH.destroyed = true; screenH.destroyed = true; FinishInstantiate("Kylaq"); Frames(3);
    var made = (GameObject)Last("instantiate", "Kylaq").result;
    Check("The just-created vehicle is released, not left in the next scene", made.destroyed && Addressables.ReleasedInstances.Contains(made) && screenH.loaded.Count == 0 && !loader.IsLoading);

    Console.WriteLine("=== 8. Error messages in plain words ===");
    Check("Disk full", RemoteAddressableVehicleLoader.FriendlyDownloadError(new Exception("Write failed: Disk full")).StartsWith("There isn't enough free disk space"));
    Check("Network", RemoteAddressableVehicleLoader.FriendlyDownloadError(new Exception("Curl error 52: Empty reply from server")).StartsWith("The download was interrupted"));
    Check("Not found", RemoteAddressableVehicleLoader.FriendlyDownloadError(new Exception("HTTP/1.1 404 Not Found")).Contains("missing on the server"));
    Check("404 nested deep", RemoteAddressableVehicleLoader.FriendlyDownloadError(new Exception("ProvideResources failed", new Exception("ChainOperation failed", new Exception("Unable to load asset bundle: HTTP/1.1 404 Not Found")))).Contains("publish it again"));
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
  } }
