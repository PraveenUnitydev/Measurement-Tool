// Plain parts of DasCodeSigning (run.sh): which files are signed, signtool's arguments, pasted thumbprints.
using System; using System.IO; using System.Linq; using VehicleMeasurement.EditorTools.AppUpdates;
public static class CodeSigningTests
{
    static int pass, fail;
    static void Check(string n, bool ok, string extra = "") { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + n + " " + extra); } }
    public static int Main()
    {
        string d = Path.Combine(Path.GetTempPath(), "das-sign-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(Path.Combine(d, "DAS_Data"));
        foreach (string f in new[] { "DigitalAssessmentSystem-AVA.exe", "DASUpdater.exe", "UnityCrashHandler64.exe", "UnityPlayer.dll", "DAS_Data/inner.exe" }) File.WriteAllText(Path.Combine(d, f), "x");
        var files = DasCodeSigning.FilesToSign(d).Select(Path.GetFileName).ToList();
        Check("signs our programs, not Unity's crash handler, DLLs or sub-folders", files.SequenceEqual(new[] { "DASUpdater.exe", "DigitalAssessmentSystem-AVA.exe" }), string.Join(",", files));
        Directory.Delete(d, true);

        Check("thumbprint pasted from certmgr (spaces, hidden mark) cleaned", DasCodeSigning.CleanThumbprint("\u200e a1 b2 c3 d4 ") == "A1B2C3D4");
        string a = DasCodeSigning.SignArguments(new[] { @"D:\DAS\DAS.exe", @"D:\My Build\DASUpdater.exe" }, "A1B2", "", null, "http://ts.example");
        Check("store certificate: sha256 digest, timestamp, thumbprint, quoted paths",
            a == "sign /fd SHA256 /tr \"http://ts.example\" /td SHA256 /sha1 A1B2 \"D:\\DAS\\DAS.exe\" \"D:\\My Build\\DASUpdater.exe\"", a);
        a = DasCodeSigning.SignArguments(new[] { "x.exe" }, "", @"C:\certs\company.pfx", "pw", "");
        Check("pfx certificate with password, no timestamp", a == "sign /fd SHA256 /f \"C:\\certs\\company.pfx\" /p \"pw\" \"x.exe\"", a);
        Console.WriteLine("Code signing: Passed " + pass + " | Failed " + fail);
        return fail == 0 ? 0 : 1;
    }
}
