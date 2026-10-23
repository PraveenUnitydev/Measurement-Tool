// Thumbnails: the server is the source of truth; disk copies are re-checked once per session. Run: sh run.sh
using System; using System.Collections.Generic; using System.IO; using System.Linq; using UnityEngine; using UnityEngine.Networking; using VehicleMeasurement;
public static class ThumbT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  class Obj { public byte[] bytes; public string etag; }
  static Dictionary<string, Obj> bucket = new Dictionary<string, Obj>(StringComparer.OrdinalIgnoreCase);
  static bool offline;
  static string Key(string url) {
    string p = url.Split('?')[0]; int i = p.IndexOf("/Thumbnail/"); if (i >= 0) return p.Substring(i + 11);
    i = p.IndexOf("/thumbnails/"); return i >= 0 ? p.Substring(i + 12) : p; }
  static void Serve(UnityWebRequest r) {
    if (offline) { r.result = UnityWebRequest.Result.ConnectionError; r.error = "Cannot resolve destination host"; r.responseCode = 0; return; }
    if (r.url.Contains("expired")) { r.result = UnityWebRequest.Result.ProtocolError; r.responseCode = 400; return; }
    Obj o; if (!bucket.TryGetValue(Key(r.url), out o)) { r.result = UnityWebRequest.Result.ProtocolError; r.responseCode = 404; return; }
    string inm; if (r.requestHeaders.TryGetValue("If-None-Match", out inm) && inm == o.etag) { r.responseCode = 304; r.downloadHandler.data = new byte[0]; return; }
    r.responseCode = 200; r.downloadHandler.data = o.bytes; r.responseHeaders["ETag"] = o.etag; r.responseHeaders["Last-Modified"] = "Wed, 07 Oct 2026 10:00:00 GMT"; }
  static void RunAll() { for (int i = 0; i < 5000 && Sched.Running > 0; i++) { Sched.Step(); System.Threading.Thread.Sleep(1); } }
  static RemoteAddressableVehicleLoader NewSession(List<RemoteVehicleInfo> catalog, Dictionary<string, int> events) {
    var l = new RemoteAddressableVehicleLoader();
    var cat = (List<RemoteVehicleInfo>)typeof(RemoteAddressableVehicleLoader).GetField("_remoteCatalog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(l);
    cat.AddRange(catalog);
    l.ThumbnailUpdated += (id, s) => { events[id] = events.TryGetValue(id, out var n) ? n + 1 : 1; };
    return l; }
  static int Full(string key) { return UnityWebRequest.Sent.Count(r => Key(r.url) == key && r.responseCode == 200); }
  public static void Main() {
    string dir = Path.Combine(Path.GetTempPath(), "das-thumb-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    Application.persistentDataPath = dir;
    string folder = Path.Combine(dir, "DAS", "thumbnails"); Directory.CreateDirectory(folder);
    File.WriteAllBytes(Path.Combine(folder, "creta_1.0.img"), new byte[] { 1, 2 });                 // old scheme copy
    UnityWebRequest.Server = Serve;
    bucket["creta.png"] = new Obj { bytes = new byte[] { 1, 1, 1 }, etag = "\"e1\"" };
    bucket["thar.png"] = new Obj { bytes = new byte[] { 2, 2 }, etag = "\"t1\"" };
    bucket["xuv.png"] = new Obj { bytes = new byte[] { 3 }, etag = "\"x1\"" };
    var catalog = new List<RemoteVehicleInfo> {
      new RemoteVehicleInfo { vehicleId = "creta", addressableKey = "Creta", version = "1.0", thumbnailUrl = "https://storage.googleapis.com/b/AssesmentSystem/Thumbnail/creta.png?X-Goog-Signature=abc" },
      new RemoteVehicleInfo { vehicleId = "thar", addressableKey = "Thar", version = "1.0", thumbnailUrl = "" },                   // no link in the catalog
      new RemoteVehicleInfo { vehicleId = "xuv", addressableKey = "XUV", version = "1.0", thumbnailUrl = "xuv.png", thumbnailVersion = "g1" },
      new RemoteVehicleInfo { vehicleId = "ghost", addressableKey = "Ghost", version = "1.0", thumbnailUrl = "" } };

    Console.WriteLine("=== first start: everything downloaded, old cache removed ===");
    var ev = new Dictionary<string, int>(); var l1 = NewSession(catalog, ev); l1.RefreshThumbnails(); RunAll();
    Check("old-scheme copy removed", !File.Exists(Path.Combine(folder, "creta_1.0.img")));
    Check("creta from the catalog link", l1.GetThumbnail("creta") != null && ev.ContainsKey("creta") && File.Exists(l1.GetThumbnailFile("creta")));
    Check("thar found by its name although the catalog has no link", l1.GetThumbnail("thar") != null && UnityWebRequest.Sent.Any(r => r.url.EndsWith("/thumbnails/thar.png")));
    Check("xuv relative link via the server route", l1.GetThumbnail("xuv") != null);
    Check("vehicle with no thumbnail anywhere: no picture, logged once", l1.GetThumbnail("ghost") == null && UnityEngine.Debug.Logs.Count(x => x.Contains("No thumbnail on the server for ghost")) == 1);
    Check("ETag remembered", File.ReadAllText(Path.Combine(folder, "creta.thumb.txt")).Contains("etag=\"e1\""));

    Console.WriteLine("=== next start, nothing changed: shown from disk, server only says 'not changed' ===");
    int fullBefore = Full("creta.png"); UnityWebRequest.Sent.Clear();
    ev = new Dictionary<string, int>(); var l2 = NewSession(catalog, ev); l2.RefreshThumbnails(); RunAll();
    Check("creta shown from disk", l2.GetThumbnail("creta") != null && ev.ContainsKey("creta"));
    var creq = UnityWebRequest.Sent.Where(r => Key(r.url) == "creta.png").ToList();
    Check("creta re-checked with If-None-Match and got 304 (no download)", creq.Count == 1 && creq[0].requestHeaders.ContainsKey("If-None-Match") && creq[0].responseCode == 304);
    Check("xuv: same server thumbnailVersion -> no request at all", !UnityWebRequest.Sent.Any(r => Key(r.url) == "xuv.png"));

    Console.WriteLine("=== picture replaced on the server (same vehicle version) ===");
    bucket["creta.png"] = new Obj { bytes = new byte[] { 9, 9, 9, 9 }, etag = "\"e2\"" };
    bucket["xuv.png"] = new Obj { bytes = new byte[] { 8, 8 }, etag = "\"x2\"" };
    catalog[2].thumbnailVersion = "g2";
    UnityWebRequest.Sent.Clear(); ev = new Dictionary<string, int>(); var l3 = NewSession(catalog, ev); l3.RefreshThumbnails(); RunAll();
    Check("creta: new picture shown (disk copy, then the new one)", ev.TryGetValue("creta", out var n1) && n1 == 2 && l3.GetThumbnail("creta").texture.bytes.Length == 4);
    Check("creta: disk copy replaced", File.ReadAllBytes(l3.GetThumbnailFile("creta")).Length == 4);
    Check("xuv: version changed -> downloaded again", l3.GetThumbnail("xuv").texture.bytes.SequenceEqual(new byte[] { 8, 8 }));

    Console.WriteLine("=== same session: checked once only ===");
    UnityWebRequest.Sent.Clear(); var sentBefore = UnityWebRequest.Sent.Count;
    var pre = typeof(RemoteAddressableVehicleLoader).GetMethod("PreloadThumbnails", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    Sched.Start((System.Collections.IEnumerator)pre.Invoke(l3, null)); RunAll();
    Check("catalog reload in the same session sends no requests", UnityWebRequest.Sent.Count == 0, UnityWebRequest.Sent.Count + "");

    Console.WriteLine("=== offline start with an expired signed link ===");
    offline = true; UnityWebRequest.Sent.Clear(); ev = new Dictionary<string, int>(); var l4 = NewSession(catalog, ev); l4.RefreshThumbnails(); RunAll();
    Check("offline: pictures still shown from disk", l4.GetThumbnail("creta") != null && l4.GetThumbnail("thar") != null);
    Check("offline: disk copies kept", File.Exists(l4.GetThumbnailFile("creta")));
    offline = false;
    var expired = catalog.Select(c => new RemoteVehicleInfo { vehicleId = c.vehicleId, addressableKey = c.addressableKey, version = c.version, thumbnailVersion = c.thumbnailVersion, thumbnailUrl = c.thumbnailUrl.Replace("X-Goog", "expired") }).ToList();
    bucket["creta.png"] = new Obj { bytes = new byte[] { 7, 7, 7, 7, 7 }, etag = "\"e3\"" };
    UnityWebRequest.Sent.Clear(); ev = new Dictionary<string, int>(); var l5 = NewSession(expired, ev); l5.RefreshThumbnails(); RunAll();
    Check("expired signed link: same file asked through /thumbnails/ instead", l5.GetThumbnail("creta").texture.bytes.Length == 5 && UnityWebRequest.Sent.Any(r => r.url.EndsWith("/thumbnails/creta.png")));

    Console.WriteLine("=== a broken file on disk is fetched again ===");
    File.WriteAllBytes(l5.GetThumbnailFile("thar"), new byte[] { 0 });                                // LoadImage fails on it
    UnityWebRequest.Sent.Clear(); ev = new Dictionary<string, int>(); var l6 = NewSession(catalog, ev); l6.RefreshThumbnails(); RunAll();
    Check("unreadable copy replaced from the server", l6.GetThumbnail("thar") != null && File.ReadAllBytes(l6.GetThumbnailFile("thar")).Length == 2);

    // pure decisions
    var e = new VehicleThumbnailStore.Entry { version = "g1", etag = "x" };
    Check("Decide: nothing on disk -> download", VehicleThumbnailStore.Decide(null, "", false) == VehicleThumbnailStore.Step.Download);
    Check("Decide: same version -> disk", VehicleThumbnailStore.Decide(e, "g1", false) == VehicleThumbnailStore.Step.UseDisk);
    Check("Decide: other version -> download", VehicleThumbnailStore.Decide(e, "g2", true) == VehicleThumbnailStore.Step.Download);
    Check("Decide: no version, not checked -> revalidate", VehicleThumbnailStore.Decide(e, "", false) == VehicleThumbnailStore.Step.Revalidate);
    Check("Decide: no validators -> download", VehicleThumbnailStore.Decide(new VehicleThumbnailStore.Entry(), "", false) == VehicleThumbnailStore.Step.Download);
    Check("unsafe names are not requested", VehicleThumbnailStore.Candidates("", "../etc/passwd", "a b", "https://x/thumbnails/").Count == 0);
    Check("relative path from signed link", VehicleThumbnailStore.RelativeThumbnailPath("https://storage.googleapis.com/b/AssesmentSystem/Thumbnail/sub/My_Car.png?sig=1") == "sub/My_Car.png");
    try { Directory.Delete(dir, true); } catch (Exception) { }
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
