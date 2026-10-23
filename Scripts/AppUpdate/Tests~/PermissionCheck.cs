// run.sh (as root): a folder made by another account (like one made by a program run as administrator on Windows)
// must be reported by UpdatePlan.AccessProblems when checked as a normal account; one the account owns must not.
using System; using System.Collections.Generic; using System.IO; using System.Linq; using VehicleMeasurement.AppUpdate;
public static class PermissionCheck
{
    public static int Main(string[] a)
    {
        if (a[0] == "make")      // make <install>: the plan to check (written next to it)
        {
            var rel = new List<ReleaseFile> { new ReleaseFile { path = "DAS.exe", sha256 = new string('a', 64), size = 3 }, new ReleaseFile { path = "DAS_Data/boot.config", sha256 = new string('b', 64), size = 3 } };
            File.WriteAllText(a[1] + ".plan", UpdatePlan.Compute(a[1], "1.0.1", rel, null).Serialize());
            return 0;
        }
        var bad = UpdatePlan.Parse(File.ReadAllText(a[1] + ".plan")).AccessProblems();     // check <install> <expected count>
        int want = int.Parse(a[2]);
        bool ok = want == 0 ? bad.Count == 0 : bad.Any(b => b.EndsWith("DAS_Data"));
        Console.WriteLine((ok ? "" : "FAIL ") + "permission check (" + (want == 0 ? "own folder: nothing reported" : "folder of another account: reported") + "): " + bad.Count + " " + string.Join(" ", bad.ToArray()));
        return ok ? 0 : 1;
    }
}
