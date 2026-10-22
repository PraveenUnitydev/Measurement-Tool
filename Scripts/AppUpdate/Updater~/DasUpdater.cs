using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using VehicleMeasurement.AppUpdate;

// DASUpdater.exe - puts a downloaded DAS update in place. DAS starts it (from a copy in the update's own folder, so the
// copy in the DAS folder can be replaced too) and closes; this program:
//   1. waits for DAS to close
//   2. checks every downloaded file against its checksum again
//   3. puts each changed file in place, keeping the old one in <staging>/backup; removes files the new version no
//      longer has (only files of the previous version)
//   4. writes das-release.txt (what is installed now)
//   5. starts DAS again
// If anything goes wrong it puts every file back as it was, starts the old DAS, and says why (DAS shows the message).
//
// Build (Windows has .NET Framework 4.x; any machine with Mono works too):
//   mcs -target:winexe -out:DASUpdater.exe -r:System.Core DasUpdater.cs ../AppReleaseCore.cs
// Usage: DASUpdater.exe --plan <file> [--no-relaunch] [--no-dialog] [--wait <seconds>]
namespace VehicleMeasurement.AppUpdate
{
    public static class DasUpdaterProgram
    {
        private static StreamWriter _log;

        public static int Main(string[] args)
        {
            string planFile = Arg(args, "--plan");
            bool relaunch = !args.Contains("--no-relaunch");
            bool dialog = !args.Contains("--no-dialog");
            int waitSeconds;
            if (!int.TryParse(Arg(args, "--wait") ?? "90", out waitSeconds)) waitSeconds = 90;

            UpdatePlan plan;
            try { plan = UpdatePlan.Parse(File.ReadAllText(planFile)); }
            catch (Exception e) { Show(dialog, "DAS update", "The update could not start: " + e.Message); return 2; }

            try { _log = new StreamWriter(Path.Combine(plan.stagingDir, "update.log"), true) { AutoFlush = true }; } catch (Exception) { }
            Log("Updating DAS " + plan.fromVersion + " -> " + plan.version + " in " + plan.installDir);

            string error = null;
            try
            {
                Apply(plan, waitSeconds);
            }
            catch (Exception e)
            {
                error = e.Message;
                Log("FAILED: " + e);
            }

            UpdateResult.Write(plan.resultFile, error == null, plan.version, plan.fromVersion, error);
            if (error != null)
                Show(dialog, "DAS update", "DAS could not be updated to " + plan.version + ", so it stays on " + (string.IsNullOrEmpty(plan.fromVersion) ? "the current version" : plan.fromVersion) + ".\n\n" + error);
            if (relaunch) Relaunch(plan);
            if (_log != null) _log.Dispose();
            return error == null ? 0 : 1;
        }

        /// <summary>Does the work; on any failure undoes what it did and throws with a message for the user.</summary>
        public static void Apply(UpdatePlan plan, int waitSeconds)
        {
            if (string.IsNullOrEmpty(plan.installDir) || !Directory.Exists(plan.installDir)) throw new Exception("The DAS folder was not found: " + plan.installDir);
            foreach (ReleaseFile w in plan.writes) if (!UpdatePlan.IsSafeRelative(w.path)) throw new Exception("Unsafe file name in the update: " + w.path);
            foreach (string d in plan.deletes) if (!UpdatePlan.IsSafeRelative(d)) throw new Exception("Unsafe file name in the update: " + d);

            WaitForExit(plan.pid, plan.exe, waitSeconds);

            // Every downloaded file again, before anything is touched
            foreach (ReleaseFile w in plan.writes)
            {
                string blob = plan.BlobPath(w.sha256);
                if (!File.Exists(blob)) throw new Exception("A downloaded file is missing (" + w.path + "). Download the update again.");
                if (new FileInfo(blob).Length != w.size || !string.Equals(Manifest.HashFile(blob), w.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("A downloaded file is damaged (" + w.path + "). Download the update again.");
            }

            string backup = Path.Combine(plan.stagingDir, "backup");
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
            var done = new List<KeyValuePair<string, bool>>();    // (path, existed before) in the order changed
            try
            {
                foreach (ReleaseFile w in plan.writes)
                {
                    string target = Full(plan.installDir, w.path);
                    bool existed = File.Exists(target);
                    if (existed) Retry(() => CopyFile(target, Full(backup, w.path)), "back up " + w.path);
                    done.Add(new KeyValuePair<string, bool>(w.path, existed));
                    string tmp = target + ".das-new";
                    CopyFile(plan.BlobPath(w.sha256), tmp);
                    Retry(() => { if (File.Exists(target)) File.Delete(target); File.Move(tmp, target); }, "replace " + w.path);
                    Log("  wrote " + w.path);
                }
                foreach (string d in plan.deletes)
                {
                    string target = Full(plan.installDir, d);
                    if (!File.Exists(target)) continue;
                    Retry(() => { CopyFile(target, Full(backup, d)); File.Delete(target); }, "remove " + d);
                    done.Add(new KeyValuePair<string, bool>(d, true));
                    Log("  removed " + d);
                }
                File.WriteAllText(Path.Combine(plan.installDir, Manifest.InstalledFile), Manifest.Canonical(plan.version, plan.manifest));
                Log("Done: DAS " + plan.version);
            }
            catch (Exception e)
            {
                Log("Putting the previous version back: " + e.Message);
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    string target = Full(plan.installDir, done[i].Key);
                    string saved = Full(backup, done[i].Key);
                    try
                    {
                        if (done[i].Value && File.Exists(saved)) { CopyFile(saved, target + ".das-restore"); if (File.Exists(target)) File.Delete(target); File.Move(target + ".das-restore", target); }
                        else if (!done[i].Value && File.Exists(target)) File.Delete(target);
                    }
                    catch (Exception r) { Log("  could not restore " + done[i].Key + ": " + r.Message); }
                }
                throw new Exception(Friendly(e));
            }
        }

        private static string Friendly(Exception e)
        {
            if (e is UnauthorizedAccessException) return "Windows didn't allow changing the DAS folder. Move the DAS folder to one you can change (for example D:\\DAS) and update again.";
            if (e is IOException && e.Message.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0) return "There isn't enough disk space. Free some space and update again.";
            return e.Message;
        }

        // Polls rather than Process.WaitForExit: that only works reliably for a program's own child processes on some
        // runtimes. A process id that now belongs to a different program (ids are reused) counts as closed.
        private static void WaitForExit(int pid, string exeName, int seconds)
        {
            if (pid <= 0) return;
            string name = Path.GetFileNameWithoutExtension(exeName ?? "");
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            bool logged = false;
            while (true)
            {
                bool running;
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                        running = !p.HasExited && (name.Length == 0 || IsSameProgram(p, name));
                }
                catch (Exception) { running = false; }               // gone
                if (!running) break;
                if (!logged) { Log("Waiting for DAS (process " + pid + ") to close"); logged = true; }
                if (DateTime.UtcNow > deadline) throw new Exception("DAS didn't close. Close DAS and update again.");
                Thread.Sleep(250);
            }
            Thread.Sleep(500);
        }

        private static bool IsSameProgram(Process p, string name)
        {
            try { return string.Equals(p.ProcessName, name, StringComparison.OrdinalIgnoreCase) || p.ProcessName.Length == 0; }
            catch (Exception) { return true; }                      // can't tell: assume it is DAS and keep waiting
        }

        // A file can stay locked for a moment after DAS closes (antivirus scanning it, say): try a few times
        private static void Retry(Action a, string what)
        {
            for (int i = 1; ; i++)
            {
                try { a(); return; }
                catch (IOException e)
                {
                    if (i >= 10) throw new IOException("Could not " + what + ": " + e.Message, e);
                    Thread.Sleep(500 * i);
                }
                catch (UnauthorizedAccessException)
                {
                    if (i >= 3) throw;
                    Thread.Sleep(1000);
                }
            }
        }

        private static void CopyFile(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        private static string Full(string root, string rel) { return Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); }

        private static void Relaunch(UpdatePlan plan)
        {
            try
            {
                string exe = Full(plan.installDir, string.IsNullOrEmpty(plan.exe) ? "DAS.exe" : plan.exe);
                if (!File.Exists(exe)) { Log("Not restarting: " + exe + " not found"); return; }
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = plan.installDir, UseShellExecute = true });
                Log("Started " + exe);
            }
            catch (Exception e) { Log("Could not start DAS: " + e.Message); }
        }

        private static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static void Log(string s)
        {
            if (_log != null) try { _log.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + s); } catch (Exception) { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private static void Show(bool dialog, string title, string text)
        {
            Log(text);
            if (!dialog) return;
            try { MessageBoxW(IntPtr.Zero, text, title, 0x30); } catch (Exception) { }   // MB_ICONWARNING; not on non-Windows
        }
    }
}
