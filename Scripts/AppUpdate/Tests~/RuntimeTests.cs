using System;
using VehicleMeasurement.AppUpdate;
// AppUpdater's plain parts, with the runtime stand-ins
public static class RuntimeTests
{
    public static int Main()
    {
        int pass = 0, fail = 0;
        Action<string, bool> check = (n, ok) => { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + n); } };
        string a = new string('a', 64), b = new string('b', 64);
        // exactly what VRSP's /app/download-links sends (signed GCS links have escaped characters)
        string json = "{\"links\":{\"" + a + "\":\"https://storage.googleapis.com/b/AssesmentSystem/das-app/blobs/" + a + "?X-Goog-Algorithm=GOOG4-RSA-SHA256&X-Goog-Signature=ab%2Fcd\",\"" + b + "\":\"https://x/y?\\u0026z=1\"}}";
        var links = AppUpdater.ParseLinks(json);
        check("download links read", links != null && links.Count == 2 && links[a].EndsWith("X-Goog-Signature=ab%2Fcd") && links[b] == "https://x/y?&z=1");
        check("not links", AppUpdater.ParseLinks("{\"error\":\"x\"}") == null && AppUpdater.ParseLinks("") == null);
        check("program name from the data folder", AppUpdater.ExeName == "DAS.exe");
        check("a writable folder", AppUpdater.CanWrite(System.IO.Path.GetTempPath()) && !AppUpdater.CanWrite("/proc/definitely-not"));
        Console.WriteLine("AppUpdater: Passed " + pass + " | Failed " + fail);
        return fail == 0 ? 0 : 1;
    }
}
