// The DAS side of the end-to-end run, using the real AppReleaseCore.cs (what Unity and the DAS app run).
//   keypair <dir>                                         -> dir/priv.xml, dir/pub.xml
//   publish <buildDir> <version> <priv.xml> <out.txt>     -> out: line 1 signature, then "sha size path" lines (what the editor window sends)
//   plan <installDir> <releaseFile> <pub.xml> <staging> <result> <fromVersion> <exe>
//        releaseFile: line 1 version, line 2 signature, then "sha size path" (what /app/latest returned)
//        -> verifies the signature like DAS does, writes <staging>/plan.txt, prints the sha256 of each file to download
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VehicleMeasurement.AppUpdate;

public static class E2eTool
{
    public static int Main(string[] a)
    {
        try
        {
            switch (a[0])
            {
                case "keypair":
                {
                    var kp = ReleaseSignature.NewKeyPair(2048);
                    File.WriteAllText(Path.Combine(a[1], "priv.xml"), kp.Key);
                    File.WriteAllText(Path.Combine(a[1], "pub.xml"), kp.Value);
                    return 0;
                }
                case "publish":
                {
                    List<ReleaseFile> files = Manifest.FromFolder(a[1]);
                    string sig = ReleaseSignature.Sign(File.ReadAllText(a[3]), a[2], files);
                    File.WriteAllLines(a[4], new[] { sig }.Concat(Manifest.Sorted(files).Select(f => f.sha256 + " " + f.size + " " + f.path)));
                    return 0;
                }
                case "plan":
                {
                    string installDir = a[1];
                    string[] lines = File.ReadAllLines(a[2]);
                    string version = lines[0], sig = lines[1];
                    var files = lines.Skip(2).Where(l => l.Length > 0).Select(l =>
                    {
                        int x = l.IndexOf(' '), y = l.IndexOf(' ', x + 1);
                        return new ReleaseFile { sha256 = l.Substring(0, x), size = long.Parse(l.Substring(x + 1, y - x - 1)), path = l.Substring(y + 1) };
                    }).ToList();
                    if (!ReleaseSignature.Verify(File.ReadAllText(a[3]), version, files, sig)) { Console.Error.WriteLine("SIGNATURE REJECTED"); return 3; }

                    List<ReleaseFile> installed = null; string iv; List<ReleaseFile> ifiles;
                    string record = Path.Combine(installDir, Manifest.InstalledFile);
                    if (File.Exists(record) && Manifest.TryParse(File.ReadAllText(record), out iv, out ifiles)) installed = ifiles;
                    UpdatePlan plan = UpdatePlan.Compute(installDir, version, files, installed);
                    if (plan.writes.Any(w => !UpdatePlan.IsSafeRelative(w.path))) { Console.Error.WriteLine("UNSAFE PATH"); return 4; }
                    plan.stagingDir = a[4]; plan.resultFile = a[5]; plan.fromVersion = a[6]; plan.exe = a[7]; plan.pid = a.Length > 8 ? int.Parse(a[8]) : 0;
                    Directory.CreateDirectory(Path.Combine(plan.stagingDir, "blobs"));
                    File.WriteAllText(Path.Combine(plan.stagingDir, "plan.txt"), plan.Serialize());
                    foreach (string s in plan.writes.Select(w => w.sha256).Distinct()) Console.WriteLine(s);
                    Console.Error.WriteLine("writes=" + plan.writes.Count + " deletes=" + plan.deletes.Count);
                    return 0;
                }
            }
            Console.Error.WriteLine("unknown command");
            return 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
