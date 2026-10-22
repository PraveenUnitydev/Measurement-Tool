using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using VehicleMeasurement.AppUpdate;

// Tests for app updates outside Unity: run.sh builds DASUpdater.exe and runs these (needs Mono and Node).
public static class AppUpdateTests
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail = "") { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + name + (detail.Length > 0 ? "   [" + detail + "]" : "")); } }

    static string Tmp() { string d = Path.Combine(Path.GetTempPath(), "das-upd-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(d); return d; }
    static void Write(string root, string rel, string content) { string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, content); }
    static string Read(string root, string rel) { string p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); return File.Exists(p) ? File.ReadAllText(p) : null; }
    static string Sha(string s) { using (var h = System.Security.Cryptography.SHA256.Create()) return Manifest.ToHex(h.ComputeHash(Encoding.UTF8.GetBytes(s))); }
    static ReleaseFile F(string path, string content) { return new ReleaseFile(path, Sha(content), Encoding.UTF8.GetByteCount(content)); }

    public static int Main(string[] args)
    {
        string updaterExe = args.Length > 0 ? args[0] : "DASUpdater.exe";
        string crossDir = args.Length > 1 ? args[1] : null;

        // ── versions ──
        Check("versions", AppVersion.IsValid("1.4.0") && AppVersion.IsValid("0.0.1") && !AppVersion.IsValid("1.4") && !AppVersion.IsValid("01.2.3") && !AppVersion.IsValid("1.2.3-b") && !AppVersion.IsValid(null));
        Check("compare", AppVersion.Compare("1.10.0", "1.9.9") == 1 && AppVersion.Compare("1.2.3", "1.2.3") == 0 && AppVersion.Compare("2.0.0", "10.0.0") == -1 && AppVersion.Compare("bad", "0.0.1") == -1);
        Check("next patch", AppVersion.NextPatch("1.4.9") == "1.4.10" && AppVersion.NextPatch("0.1") == "1.0.0");

        // ── manifest ──
        var files = new List<ReleaseFile> { new ReleaseFile("b", "2", 2), new ReleaseFile("B", "1", 1), new ReleaseFile("a/c", "3", 3) };
        Check("canonical text = server's (sorted ordinal)", Manifest.Canonical("1.0.0", files) == "DAS-APP-RELEASE v1\nversion=1.0.0\n1 1 B\n3 3 a/c\n2 2 b\n");
        var m = new List<ReleaseFile> { F("DAS.exe", "exe"), F("Data/x y.dll", "x") };
        string v; List<ReleaseFile> back;
        Check("installed record round trip (paths with spaces)", Manifest.TryParse(Manifest.Canonical("1.2.0", m), out v, out back) && v == "1.2.0" && back.Count == 2 && back[1].path == "Data/x y.dll" && back[1].sha256 == m[1].sha256);
        Check("not a record", !Manifest.TryParse("hello", out v, out back));

        string build = Tmp();
        Write(build, "DAS.exe", "exe"); Write(build, "DAS_Data/Managed/A.dll", "code"); Write(build, "DAS_BurstDebugInformation_DoNotShip/x.pdb", "dbg");
        Write(build, "DAS_BackUpThisFolder_ButDontShipItWithYourGame/y", "bk"); Write(build, "das-release.txt", "old");
        var fromFolder = Manifest.FromFolder(build);
        Check("build folder: debug folders and the install record left out", fromFolder.Count == 2 && fromFolder.Any(f => f.path == "DAS_Data/Managed/A.dll" && f.sha256 == Sha("code")), string.Join(",", fromFolder.Select(f => f.path)));

        // ── signatures ──
        var keys = ReleaseSignature.NewKeyPair(2048);
        string sig = ReleaseSignature.Sign(keys.Key, "1.0.0", m);
        Check("sign + verify", ReleaseSignature.Verify(keys.Value, "1.0.0", m, sig));
        Check("other version / changed file / other key fail",
            !ReleaseSignature.Verify(keys.Value, "1.0.1", m, sig) &&
            !ReleaseSignature.Verify(keys.Value, "1.0.0", new List<ReleaseFile> { m[0], F("Data/x y.dll", "y") }, sig) &&
            !ReleaseSignature.Verify(ReleaseSignature.NewKeyPair(2048).Value, "1.0.0", m, sig) &&
            !ReleaseSignature.Verify(keys.Value, "1.0.0", m, "not base64!"));
        if (crossDir != null)
        {
            // for run.sh: Node checks this signature, and we check Node's
            File.WriteAllText(Path.Combine(crossDir, "cs-pub.xml"), keys.Value);
            File.WriteAllText(Path.Combine(crossDir, "cs-sig.txt"), sig);
            File.WriteAllText(Path.Combine(crossDir, "cs-files.txt"), Manifest.Canonical("1.0.0", m));
            string nodeSigFile = Path.Combine(crossDir, "node-sig.txt");
            if (File.Exists(nodeSigFile))
                Check("a release signed by Node (VRSP's rules) verifies here", ReleaseSignature.Verify(File.ReadAllText(Path.Combine(crossDir, "node-pub.xml")), "1.0.0", m, File.ReadAllText(nodeSigFile).Trim()));
        }

        // ── plan ──
        string inst = Tmp();
        Write(inst, "DAS.exe", "exe v1"); Write(inst, "UnityPlayer.dll", "player"); Write(inst, "DAS_Data/A.dll", "code v1"); Write(inst, "DAS_Data/gone.dll", "old only");
        Write(inst, "DAS_Data/same-size.dll", "aaaa"); Write(inst, "user-notes.txt", "mine");
        var v1 = new List<ReleaseFile> { F("DAS.exe", "exe v1"), F("UnityPlayer.dll", "player"), F("DAS_Data/A.dll", "code v1"), F("DAS_Data/gone.dll", "old only"), F("DAS_Data/same-size.dll", "aaaa") };
        var v2 = new List<ReleaseFile> { F("DAS.exe", "exe v2"), F("UnityPlayer.dll", "player"), F("DAS_Data/A.dll", "code v2 longer"), F("DAS_Data/same-size.dll", "bbbb"), F("DAS_Data/new/B.dll", "new"), F("DASUpdater.exe", "upd") };
        var plan = UpdatePlan.Compute(inst, "1.1.0", v2, v1);
        var w = plan.writes.Select(x => x.path).ToList();
        Check("plan: changed, same-size-different and new files written; unchanged not", w.Count == 5 && !w.Contains("UnityPlayer.dll") && w.Contains("DAS_Data/same-size.dll") && w.Contains("DAS_Data/new/B.dll"), string.Join(",", w));
        Check("plan: removes only files of the old version that the new one dropped (never user files)", plan.deletes.SequenceEqual(new[] { "DAS_Data/gone.dll" }), string.Join(",", plan.deletes));
        Check("plan: without a record of what's installed nothing is removed", UpdatePlan.Compute(inst, "1.1.0", v2, null).deletes.Count == 0);
        Check("plan: download size counts each file once", plan.DownloadBytes == plan.writes.Sum(x => x.size));
        plan.fromVersion = "1.0.0"; plan.stagingDir = Tmp(); plan.exe = "DAS.exe"; plan.pid = 0; plan.resultFile = Path.Combine(plan.stagingDir, "result.txt");
        var parsed = UpdatePlan.Parse(plan.Serialize());
        Check("plan file round trip", parsed.version == "1.1.0" && parsed.fromVersion == "1.0.0" && parsed.installDir == inst && parsed.writes.Count == 5 && parsed.deletes.Count == 1 && parsed.manifest.Count == 6 && parsed.exe == "DAS.exe");
        Check("safe paths", UpdatePlan.IsSafeRelative("DAS_Data/A.dll") && !UpdatePlan.IsSafeRelative("../x") && !UpdatePlan.IsSafeRelative("/x") && !UpdatePlan.IsSafeRelative("C:/x") && !UpdatePlan.IsSafeRelative("a\\b") && !UpdatePlan.IsSafeRelative("a//b"));

        // ── the updater, for real ──
        var content = new Dictionary<string, string> { { "DAS.exe", "exe v2" }, { "DAS_Data/A.dll", "code v2 longer" }, { "DAS_Data/same-size.dll", "bbbb" }, { "DAS_Data/new/B.dll", "new" }, { "DASUpdater.exe", "upd" } };
        Action<UpdatePlan> stage = p => { foreach (var x in p.writes) Write(Path.Combine(p.stagingDir, "blobs"), x.sha256, content[x.path]); };
        stage(plan);
        string planFile = Path.Combine(plan.stagingDir, "plan.txt");
        File.WriteAllText(planFile, plan.Serialize());
        int code = Run(updaterExe, planFile);
        Check("updater: exit 0", code == 0, "exit " + code + " " + Read(plan.stagingDir, "update.log"));
        Check("updater: new files in place", Read(inst, "DAS.exe") == "exe v2" && Read(inst, "DAS_Data/A.dll") == "code v2 longer" && Read(inst, "DAS_Data/same-size.dll") == "bbbb" && Read(inst, "DAS_Data/new/B.dll") == "new");
        Check("updater: unchanged and user files untouched; dropped file removed", Read(inst, "UnityPlayer.dll") == "player" && Read(inst, "user-notes.txt") == "mine" && Read(inst, "DAS_Data/gone.dll") == null);
        string rv; List<ReleaseFile> rf;
        Check("updater: das-release.txt records 1.1.0", Manifest.TryParse(Read(inst, "das-release.txt"), out rv, out rf) && rv == "1.1.0" && rf.Count == 6);
        Check("updater: old copies cleaned up", !Directory.GetFiles(inst, "*.das-old", SearchOption.AllDirectories).Any());
        var res = UpdateResult.Read(plan.resultFile);
        Check("updater: result says ok", res != null && res[0] == "ok" && res[1] == "1.1.0" && res[2] == "1.0.0");
        Check("updater: no leftovers", !Directory.GetFiles(inst, "*.das-new", SearchOption.AllDirectories).Any());

        // Read-only files are replaced; old copies an earlier update couldn't delete are cleaned up
        string instRo = Tmp(); Write(instRo, "DAS.exe", "exe v1"); Write(instRo, "DAS.exe.das-old", "left from last time"); Write(instRo, "DAS_Data/ro.dll", "ro v1");
        File.SetAttributes(Path.Combine(instRo, "DAS_Data/ro.dll"), FileAttributes.ReadOnly);
        var pRo = UpdatePlan.Compute(instRo, "1.1.0", new List<ReleaseFile> { F("DAS.exe", "exe v2"), F("DAS_Data/ro.dll", "ro v2") }, null);
        pRo.stagingDir = Tmp(); pRo.resultFile = Path.Combine(pRo.stagingDir, "result.txt"); pRo.fromVersion = "1.0.0";
        foreach (var x in pRo.writes) Write(Path.Combine(pRo.stagingDir, "blobs"), x.sha256, x.path == "DAS.exe" ? "exe v2" : "ro v2");
        File.WriteAllText(Path.Combine(pRo.stagingDir, "plan.txt"), pRo.Serialize());
        code = Run(updaterExe, Path.Combine(pRo.stagingDir, "plan.txt"));
        Check("read-only file replaced, last time's leftover removed", code == 0 && Read(instRo, "DAS_Data/ro.dll") == "ro v2" && Read(instRo, "DAS.exe") == "exe v2" && Read(instRo, "DAS.exe.das-old") == null && !Directory.GetFiles(instRo, "*.das-*", SearchOption.AllDirectories).Any(),
            string.Join(",", Directory.GetFiles(instRo, "*", SearchOption.AllDirectories).Select(Path.GetFileName)) + " " + Read(pRo.stagingDir, "update.log"));
        Check("build folder: the updater's temporary files are never part of a release", Manifest.IsExcluded("DAS.exe.das-old") && Manifest.IsExcluded("DAS_Data/x.dll.das-new") && !Manifest.IsExcluded("DAS_Data/x.dll"));

        // A damaged download: nothing in the DAS folder changes
        string inst2 = Tmp(); Write(inst2, "DAS.exe", "exe v1"); Write(inst2, "DAS_Data/A.dll", "code v1");
        var p2 = UpdatePlan.Compute(inst2, "1.1.0", new List<ReleaseFile> { F("DAS.exe", "exe v2"), F("DAS_Data/A.dll", "code v2") }, null);
        p2.stagingDir = Tmp(); p2.resultFile = Path.Combine(p2.stagingDir, "result.txt"); p2.fromVersion = "1.0.0";
        Write(Path.Combine(p2.stagingDir, "blobs"), p2.writes[0].sha256, "exe v2");
        Write(Path.Combine(p2.stagingDir, "blobs"), p2.writes[1].sha256, "tampered!");
        File.WriteAllText(Path.Combine(p2.stagingDir, "plan.txt"), p2.Serialize());
        code = Run(updaterExe, Path.Combine(p2.stagingDir, "plan.txt"));
        res = UpdateResult.Read(p2.resultFile);
        Check("damaged download: refused before touching anything", code == 1 && Read(inst2, "DAS.exe") == "exe v1" && Read(inst2, "DAS_Data/A.dll") == "code v1" && res[0] == "failed" && res[3].Contains("damaged"), res == null ? "" : res[3]);

        // Failing half way: everything already changed is put back
        string inst3 = Tmp(); Write(inst3, "DAS.exe", "exe v1"); Write(inst3, "aaa.dll", "a v1"); Write(inst3, "blocker", "I am a file, not a folder");
        var p3 = UpdatePlan.Compute(inst3, "1.1.0", new List<ReleaseFile> { F("DAS.exe", "exe v2"), F("aaa.dll", "a v2"), F("blocker/new.dll", "n"), F("zzz-new.dll", "z") }, null);
        p3.stagingDir = Tmp(); p3.resultFile = Path.Combine(p3.stagingDir, "result.txt"); p3.fromVersion = "1.0.0";
        foreach (var x in p3.writes) Write(Path.Combine(p3.stagingDir, "blobs"), x.sha256, x.path == "DAS.exe" ? "exe v2" : x.path == "aaa.dll" ? "a v2" : x.path == "zzz-new.dll" ? "z" : "n");
        File.WriteAllText(Path.Combine(p3.stagingDir, "plan.txt"), p3.Serialize());
        code = Run(updaterExe, Path.Combine(p3.stagingDir, "plan.txt"));
        Check("failure half way: every file back as it was, nothing new left", code == 1 && Read(inst3, "DAS.exe") == "exe v1" && Read(inst3, "aaa.dll") == "a v1" && Read(inst3, "blocker") == "I am a file, not a folder" && Read(inst3, "zzz-new.dll") == null && Read(inst3, "das-release.txt") == null,
            string.Join(",", Directory.GetFiles(inst3).Select(Path.GetFileName)));

        // Waits for DAS to close first
        string inst4 = Tmp(); Write(inst4, "DAS.exe", "exe v1");
        var p4 = UpdatePlan.Compute(inst4, "1.1.0", new List<ReleaseFile> { F("DAS.exe", "exe v2") }, null);
        p4.stagingDir = Tmp(); p4.resultFile = Path.Combine(p4.stagingDir, "result.txt"); p4.exe = "sleep";   // the stand-in for DAS
        Write(Path.Combine(p4.stagingDir, "blobs"), p4.writes[0].sha256, "exe v2");
        var sleeper = Process.Start(new ProcessStartInfo("sleep", "2") { UseShellExecute = false });
        p4.pid = sleeper.Id;
        File.WriteAllText(Path.Combine(p4.stagingDir, "plan.txt"), p4.Serialize());
        var sw = Stopwatch.StartNew();
        code = Run(updaterExe, Path.Combine(p4.stagingDir, "plan.txt"));
        Check("waits for DAS to close, then updates", code == 0 && sw.Elapsed.TotalSeconds >= 1.5 && Read(inst4, "DAS.exe") == "exe v2", sw.Elapsed.TotalSeconds.ToString("0.0") + "s exit " + code);
        var sleeper2 = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false });
        p4.pid = sleeper2.Id; p4.writes.Clear();
        File.WriteAllText(Path.Combine(p4.stagingDir, "plan.txt"), p4.Serialize());
        code = Run(updaterExe, Path.Combine(p4.stagingDir, "plan.txt"), "--wait 1");
        try { sleeper2.Kill(); } catch (Exception) { }
        Check("DAS doesn't close: gives up with a message", code == 1 && UpdateResult.Read(p4.resultFile)[3].Contains("didn't close"));
        var other = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false });
        p4.pid = other.Id; p4.exe = "DAS.exe";
        File.WriteAllText(Path.Combine(p4.stagingDir, "plan.txt"), p4.Serialize());
        sw = Stopwatch.StartNew();
        code = Run(updaterExe, Path.Combine(p4.stagingDir, "plan.txt"), "--wait 10");
        try { other.Kill(); } catch (Exception) { }
        Check("a process id now used by another program counts as closed (no wait)", code == 0 && sw.Elapsed.TotalSeconds < 5, sw.Elapsed.TotalSeconds.ToString("0.0") + "s");

        // A plan with a path outside the DAS folder is refused
        var p5 = new UpdatePlan { installDir = inst4, stagingDir = Tmp(), version = "9.9.9" };
        p5.writes.Add(new ReleaseFile("../escape.dll", new string('a', 64), 1));
        p5.resultFile = Path.Combine(p5.stagingDir, "result.txt");
        File.WriteAllText(Path.Combine(p5.stagingDir, "plan.txt"), p5.Serialize());
        code = Run(updaterExe, Path.Combine(p5.stagingDir, "plan.txt"));
        Check("a path outside the DAS folder is refused", code == 1 && !File.Exists(Path.Combine(Path.GetDirectoryName(inst4), "escape.dll")) && UpdateResult.Read(p5.resultFile)[3].Contains("Unsafe"));

        Console.WriteLine("Passed " + pass + " | Failed " + fail);
        return fail == 0 ? 0 : 1;
    }

    static int Run(string exe, string plan, string extra = "")
    {
        var p = Process.Start(new ProcessStartInfo("mono", "\"" + exe + "\" --plan \"" + plan + "\" --no-relaunch --no-dialog " + extra) { UseShellExecute = false });
        p.WaitForExit();
        return p.ExitCode;
    }
}
