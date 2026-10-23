using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
// Usage: DASUpdater.exe --plan <file> [--no-relaunch] [--no-dialog] [--wait <seconds>] [--no-elevate]
//   (--elevated --grant <SID>: used when it runs itself again with administrator permission)
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

            bool elevatedChild = args.Contains("--elevated");
            if (elevatedChild) Log("Running with administrator permission");
            if (!elevatedChild && !args.Contains("--no-elevate") && IsWindows && !IsElevated())
            {
                // Files this account may not change (e.g. the DAS folder was made by a program run as administrator):
                // ask Windows for administrator permission once, and let this account change the folder from then on.
                List<string> denied = null;
                try { WaitForExit(plan.pid, plan.exe, waitSeconds); denied = plan.AccessProblems(5); }
                catch (Exception e) { Log("Permission check skipped: " + e.Message); }
                if (denied != null && denied.Count > 0)
                {
                    Log("This account can't change: " + string.Join(" | ", denied.ToArray()));
                    int code = RunElevated(planFile, waitSeconds);
                    string[] r = UpdateResult.Read(plan.resultFile);
                    string elevatedError = code == 0 ? null
                        : code > 0 ? (r != null && r.Length > 3 && r[3].Length > 0 ? r[3] : "The update didn't finish (" + code + ").")
                        : NoPermissionMessage(plan, denied);
                    if (code < 0) UpdateResult.Write(plan.resultFile, false, plan.version, plan.fromVersion, elevatedError);
                    return Finish(plan, elevatedError, dialog, relaunch);
                }
            }

            string error = null;
            try
            {
                if (elevatedChild && Arg(args, "--grant") != null) GrantModify(plan.installDir, Arg(args, "--grant"));
                Apply(plan, waitSeconds);
            }
            catch (Exception e)
            {
                error = e.Message;
                Log("FAILED: " + e);
            }

            UpdateResult.Write(plan.resultFile, error == null, plan.version, plan.fromVersion, error);
            return Finish(plan, error, dialog, relaunch);
        }

        private static int Finish(UpdatePlan plan, string error, bool dialog, bool relaunch)
        {
            if (error != null)
                Show(dialog, "DAS update", "DAS could not be updated to " + plan.version + ", so it stays on " + (string.IsNullOrEmpty(plan.fromVersion) ? "the current version" : plan.fromVersion) + ".\n\n" + error);
            if (relaunch) Relaunch(plan);
            if (_log != null) _log.Dispose();
            return error == null ? 0 : 1;
        }

        // ── administrator permission (only when this account can't change the DAS folder) ──
        private static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        private static bool IsElevated()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Runs this updater again with administrator permission (Windows asks the person). It gives this account
        /// Modify permission on the DAS folder and does the update. Returns its exit code, or -1 if permission wasn't given.
        /// </summary>
        private static int RunElevated(string planFile, int waitSeconds)
        {
            string self, sid;
            try { self = Process.GetCurrentProcess().MainModule.FileName; sid = WindowsIdentity.GetCurrent().User.Value; }
            catch (Exception e) { Log("Can't ask for administrator permission: " + e.Message); return -1; }
            Log("Asking Windows for administrator permission");
            if (_log != null) { _log.Dispose(); _log = null; }             // the elevated updater writes to the same log
            int code;
            try
            {
                var psi = new ProcessStartInfo(self, "--plan \"" + planFile + "\" --elevated --no-relaunch --no-dialog --wait " + waitSeconds + " --grant " + sid)
                { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(self) };
                using (Process p = Process.Start(psi)) { p.WaitForExit(); code = p.ExitCode; }
            }
            catch (Win32Exception e) { code = -1; ReopenLog(planFile); Log(e.NativeErrorCode == 1223 ? "Administrator permission was not given" : "Couldn't get administrator permission: " + e.Message); return code; }
            catch (Exception e) { code = -1; ReopenLog(planFile); Log("Couldn't get administrator permission: " + e.Message); return code; }
            ReopenLog(planFile);
            Log("The updater with administrator permission finished: exit " + code);
            return code;
        }

        private static void ReopenLog(string planFile)
        {
            try { _log = new StreamWriter(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(planFile)), "update.log"), true) { AutoFlush = true }; } catch (Exception) { }
        }

        /// <summary>Lets the person's account change the DAS folder (Modify, inherited by everything in it), so later updates need no permission.</summary>
        private static void GrantModify(string installDir, string sid)
        {
            if (!IsWindows || string.IsNullOrEmpty(sid) || !sid.StartsWith("S-1-", StringComparison.Ordinal) || sid.Any(c => !(char.IsDigit(c) || c == '-' || c == 'S'))) return;
            try
            {
                string dir = Path.GetFullPath(TrimSlash(installDir));
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"), "\"" + dir + "\" /grant *" + sid + ":(OI)(CI)M /T /C /Q")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    Log("Gave this account Modify permission on " + dir + " (icacls exit " + p.ExitCode + ")" + (output.Trim().Length > 0 ? ": " + output.Trim().Replace("\r", "").Replace("\n", " | ") : ""));
                }
            }
            catch (Exception e) { Log("Couldn't change the folder's permissions (the update goes ahead): " + e.Message); }
        }

        private static string TrimSlash(string dir) { return dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }

        private static string NoPermissionMessage(UpdatePlan plan, List<string> denied)
        {
            return "Windows doesn't let your account change the DAS folder (" + denied[0] + "), and administrator permission wasn't given.\n\n" +
                   "Update again and choose Yes when Windows asks, or ask IT to give your account Modify permission on " + plan.installDir +
                   ". Or copy the DAS folder to one you create yourself (for example D:\\DAS) and start DAS from there.";
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

            // Unity's crash handler (and anything else started from the DAS folder) can outlive DAS by a few seconds
            WaitForFolderPrograms(plan.installDir, 15);
            // What an earlier update couldn't delete because Windows still had it open
            foreach (string rel in plan.manifest.Select(f => f.path).Concat(plan.deletes))
                foreach (string ext in new[] { OldExt, NewExt }) TryDelete(Full(plan.installDir, rel) + ext);

            // Each old file is renamed aside (Windows allows renaming a program or DLL that is still in use, but not
            // overwriting or deleting it), the new one is put in its place, and the old ones are deleted at the end.
            var done = new List<KeyValuePair<string, string>>();    // (path, old file renamed to - null if it is new) in the order changed
            try
            {
                foreach (ReleaseFile w in plan.writes)
                {
                    string target = Full(plan.installDir, w.path);
                    string tmp = target + NewExt;
                    Retry(() => { Directory.CreateDirectory(Path.GetDirectoryName(tmp)); MakeWritable(tmp); File.Copy(plan.BlobPath(w.sha256), tmp, true); }, "write " + w.path);
                    string aside = null;
                    if (File.Exists(target))
                    {
                        aside = target + OldExt;
                        Retry(() => { MakeWritable(target); File.Move(target, aside); }, "replace " + w.path);
                    }
                    done.Add(new KeyValuePair<string, string>(w.path, aside));
                    Retry(() => File.Move(tmp, target), "replace " + w.path);
                    Log("  wrote " + w.path);
                }
                foreach (string d in plan.deletes)
                {
                    string target = Full(plan.installDir, d);
                    if (!File.Exists(target)) continue;
                    string aside = target + OldExt;
                    Retry(() => { MakeWritable(target); File.Move(target, aside); }, "remove " + d);
                    done.Add(new KeyValuePair<string, string>(d, aside));
                    Log("  removed " + d);
                }
                string record = Path.Combine(plan.installDir, Manifest.InstalledFile);
                Retry(() => { MakeWritable(record); File.WriteAllText(record, Manifest.Canonical(plan.version, plan.manifest)); }, "write " + Manifest.InstalledFile);
            }
            catch (Exception e)
            {
                Log("Putting the previous version back: " + e.Message);
                for (int i = done.Count - 1; i >= 0; i--)
                {
                    string target = Full(plan.installDir, done[i].Key), aside = done[i].Value;
                    try
                    {
                        if (File.Exists(target)) { MakeWritable(target); File.Delete(target); }
                        if (aside != null && File.Exists(aside)) File.Move(aside, target);
                    }
                    catch (Exception r) { Log("  could not restore " + done[i].Key + ": " + r.Message); }
                }
                foreach (ReleaseFile w in plan.writes) TryDelete(Full(plan.installDir, w.path) + NewExt);
                throw new Exception(Friendly(e, plan));
            }

            int left = 0;
            foreach (var c in done) if (c.Value != null && !TryDelete(c.Value)) left++;
            if (left > 0) Log("  " + left + " old file(s) still in use; they are removed at the next update");
            Log("Done: DAS " + plan.version);
        }

        private const string OldExt = ".das-old", NewExt = ".das-new";

        private static void MakeWritable(string file)
        {
            try { if (File.Exists(file)) { var a = File.GetAttributes(file); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, a & ~FileAttributes.ReadOnly); } }
            catch (Exception) { }
        }

        private static bool TryDelete(string file)
        {
            try { if (File.Exists(file)) { MakeWritable(file); File.Delete(file); } return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Waits (up to a limit, never fails) for programs started from the DAS folder, e.g. UnityCrashHandler64.exe.</summary>
        private static void WaitForFolderPrograms(string installDir, int seconds)
        {
            string root = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
            bool logged = false;
            while (DateTime.UtcNow < deadline)
            {
                var running = new List<string>();
                foreach (Process p in SafeProcesses())
                {
                    try { string f = p.MainModule.FileName; if (f != null && f.StartsWith(root, StringComparison.OrdinalIgnoreCase)) running.Add(Path.GetFileName(f)); }
                    catch (Exception) { }                           // other users' or system programs: not ours
                    finally { p.Dispose(); }
                }
                if (running.Count == 0) return;
                if (!logged) { Log("Waiting for " + string.Join(", ", running.ToArray()) + " to close"); logged = true; }
                Thread.Sleep(500);
            }
            Log("Still running from the DAS folder after " + seconds + "s; going ahead (files in use are renamed, not overwritten)");
        }

        private static Process[] SafeProcesses() { try { return Process.GetProcesses(); } catch (Exception) { return new Process[0]; } }

        private static string Friendly(Exception e, UpdatePlan plan)
        {
            string log = Path.Combine(plan.stagingDir ?? "", "update.log");
            var step = e as StepFailed;
            Exception root = step != null ? step.InnerException : e;
            if (root is IOException && root.Message.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0) return "There isn't enough disk space. Free some space and update again.";
            if (step != null)
                return "Windows didn't let the updater " + step.What + " (" + root.Message + ").\n\nMake sure DAS is fully closed and update again. If it keeps happening, send this file to your DAS admin:\n" + log;
            return e.Message;
        }

        private class StepFailed : Exception
        {
            public readonly string What;
            public StepFailed(string what, Exception inner) : base("Could not " + what + ": " + inner.Message, inner) { What = what; }
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

        // A file can stay locked for a while after DAS closes (antivirus scanning it, the crash handler closing). On
        // Windows a file in use can also show up as "access denied", so both are retried for about 20 seconds.
        private static void Retry(Action a, string what)
        {
            for (int i = 1; ; i++)
            {
                try { a(); return; }
                catch (Exception e)
                {
                    if (!(e is IOException) && !(e is UnauthorizedAccessException)) throw new StepFailed(what, e);
                    if (i >= 12) { Log("  gave up: " + what + ": " + e.GetType().Name + ": " + e.Message); throw new StepFailed(what, e); }
                    if (i == 1) Log("  waiting to " + what + ": " + e.Message);
                    Thread.Sleep(Math.Min(250 * i, 2500));
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
