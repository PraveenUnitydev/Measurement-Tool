// Batch download queue + library/notice logic. Run: sh run.sh
using System; using System.Collections.Generic; using System.IO; using System.Linq; using UnityEngine; using UnityEngine.AddressableAssets; using UnityEngine.ResourceManagement.AsyncOperations; using VehicleMeasurement.Storage;
public static class BatchT {
  static int pass, fail; static void Check(string n, bool c, string x = "") { if (c) pass++; else { fail++; Console.WriteLine("FAIL " + n + (x != "" ? "  [" + x + "]" : "")); } }
  static Op Last(string kind, string key) { return Addressables.Ops.LastOrDefault(o => o.kind == kind && o.key == key); }
  static void Frames(int n) { for (int i = 0; i < n; i++) Sched.Step(); }
  static void Finish(string key, bool ok = true) { var o = Last("download", key); o.dl = o.total; o.done = true; o.status = ok ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed; if (!ok) o.ex = new Exception("Unable to load asset bundle from : https://x/b.bundle (Request timeout)"); }
  static LibraryItem I(string id, long bytes = 100, bool dl = false, bool upd = false) { return new LibraryItem { vehicleId = id, name = id, addressableKey = id, known = true, downloaded = dl, needsUpdate = upd, downloadBytes = bytes, totalBytes = bytes }; }
  public static void Main() {
    Console.WriteLine("=== queue ===");
    Addressables.Sizes["A"] = 100; Addressables.Sizes["B"] = 200; Addressables.Sizes["C"] = 300; Addressables.Sizes["D"] = 0;
    int changes = 0; BatchDownloads.Changed += () => changes++;
    int added = BatchDownloads.Enqueue(new[] { I("A"), I("B"), I("C"), I("D", 0, true, false) });
    Frames(2);
    Check("all queued, one downloads at a time", added == 4 && BatchDownloads.Running && Last("download", "A") != null && Last("download", "B") == null);
    Check("re-adding a queued vehicle is ignored", BatchDownloads.Enqueue(new[] { I("A"), I("B") }) == 0);
    Check("describe while running", BatchDownloads.Describe().StartsWith("Downloading 1 of 4: A"), BatchDownloads.Describe());
    Finish("A"); Frames(4);
    Check("A done, B started", BatchDownloads.Find("A").state == BatchDownloads.JobState.Done && Last("download", "B") != null);
    Finish("B", false); Frames(4);
    Check("a failure doesn't stop the rest", BatchDownloads.Find("B").state == BatchDownloads.JobState.Failed && Last("download", "C") != null);
    Check("failure message is readable", BatchDownloads.Find("B").message.Length > 0 && !BatchDownloads.Find("B").message.Contains("Exception"), BatchDownloads.Find("B").message);
    BatchDownloads.CancelRemaining(); Frames(2);
    Check("cancel: waiting ones cancelled, current continues", BatchDownloads.Find("D").state == BatchDownloads.JobState.Cancelled && BatchDownloads.Find("C").state == BatchDownloads.JobState.Downloading);
    Finish("C"); Frames(4);
    Check("current finished and kept; queue stopped", BatchDownloads.Find("C").state == BatchDownloads.JobState.Done && !BatchDownloads.Running);
    Check("summary", BatchDownloads.Describe() == "2 downloaded, 1 failed, 1 cancelled", BatchDownloads.Describe());
    Check("overall fraction counts done+failed", Math.Abs(BatchDownloads.OverallFraction() - 1f) < 0.01f);
    BatchDownloads.RetryFailed(); Frames(2);
    Check("retry requeues failed + cancelled", BatchDownloads.Running && Last("download", "B") != null && !Last("download", "B").done);
    Finish("B"); Frames(6);
    Check("D (already on disk) completes without a download", BatchDownloads.Find("D").state == BatchDownloads.JobState.Done && Last("download", "D") == null);
    BatchDownloads.ClearFinished();
    Check("clear finished", BatchDownloads.Jobs.Count == 0 && changes > 5);
    Addressables.Sizes["Huge"] = 1L << 55;
    Check("disk space checked for the whole selection", BatchDownloads.SpaceProblem(1L << 55) != null && BatchDownloads.SpaceProblem(1024) == null);

    Console.WriteLine("=== library + notice ===");
    string dir = Path.Combine(Path.GetTempPath(), "das-lib-" + Guid.NewGuid().ToString("N")); string file = Path.Combine(dir, "seen.txt");
    var cat = new List<CatalogEntry> { new CatalogEntry { vehicleId = "creta", name = "Creta", addressableKey = "Creta", version = "1" },
      new CatalogEntry { vehicleId = "thar", name = "Thar", addressableKey = "Thar", version = "1", manufacturer = "Mahindra" },
      new CatalogEntry { vehicleId = "xuv", name = "XUV700", addressableKey = "XUV", version = "1" } };
    Func<CatalogEntry, FileState> files = c => c.vehicleId == "creta" ? new FileState { downloaded = true } : c.vehicleId == "thar" ? new FileState { needsUpdate = true, downloadBytes = 50 } : new FileState { totalBytes = 300 };
    var seen = new CatalogSeen(file);
    var first = VehicleLibrary.Build(cat, files, seen);
    Check("first run: nothing flagged as new", !seen.HasBaseline && first.All(i => !i.isNew && !i.changedOnServer));
    var s1 = VehicleLibrary.Summarize(first);
    Check("first run: the real update still announced", s1.updates == 1 && s1.HasNotice && s1.NoticeText().Contains("1 update for vehicles on this PC"), s1.NoticeText());
    seen.MarkSeen(cat);
    cat.Add(new CatalogEntry { vehicleId = "be6", name = "BE 6", addressableKey = "BE6", version = "1" });
    cat[2].version = "2";
    var seen2 = new CatalogSeen(file);
    var items = VehicleLibrary.Build(cat, files, seen2);
    var s2 = VehicleLibrary.Summarize(items);
    Check("new vehicle detected after restart", items.First(i => i.vehicleId == "be6").isNew && s2.isNew == 1);
    Check("server version change detected for a vehicle not on this PC", items.First(i => i.vehicleId == "xuv").changedOnServer);
    Check("notice text", s2.NoticeText() == "1 new vehicle · 1 update for vehicles on this PC (50 B)", s2.NoticeText());
    Check("filters", items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.Updates, "")) == 1 && items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.New, "")) == 1
      && items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.OnThisPc, "")) == 2 && items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.NotDownloaded, "")) == 2);
    Check("search by manufacturer", items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.All, "mahin")) == 1);
    Check("sorted: updates, new, not downloaded, on PC", string.Join(",", VehicleLibrary.Sorted(items).Select(i => i.vehicleId)) == "thar,be6,xuv,creta");
    Check("bytes for selection", VehicleLibrary.BytesFor(items.Where(i => i.vehicleId != "creta")) == 50 + 300 + 300);
    Check("downloaded vehicle not actionable", !items.First(i => i.vehicleId == "creta").Actionable);
    seen2.MarkSeen(cat);
    Check("after looking, nothing new", VehicleLibrary.Summarize(VehicleLibrary.Build(cat, files, new CatalogSeen(file))).isNew == 0);
    Check("unknown state (storage not ready) is still selectable", new LibraryItem().Actionable);
    try { Directory.Delete(dir, true); } catch (Exception) { }
    Console.WriteLine("Passed " + pass + " | Failed " + fail);
    Environment.Exit(fail == 0 ? 0 : 1);
  }
}
