using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace VehicleMeasurement.EditorTools.AppUpdates
{
    /// <summary>
    /// Windows code signing (Authenticode) of DAS's own programs - the DAS exe and DASUpdater.exe - in every Windows
    /// build, with the company's code-signing certificate and Microsoft's signtool. Signed programs show a known
    /// publisher, so security software (e.g. SentinelOne) can allow them by signer. Unity's own files (UnityPlayer.dll,
    /// UnityCrashHandler64.exe) are already signed by Unity.
    /// Settings are per PC (EditorPrefs): the certificate is in the Windows certificate store (thumbprint; usual for
    /// company certificates and USB tokens) or a .pfx file whose password comes from the DAS_SIGN_PFX_PASSWORD
    /// environment variable (never saved). DAS > App Updates > Code Signing...
    /// </summary>
    public static class DasCodeSigning
    {
        private const string Pref = "DAS.CodeSign.";
        public const string PasswordEnv = "DAS_SIGN_PFX_PASSWORD";
        public const string DefaultTimestamp = "http://timestamp.digicert.com";

        public static string Thumbprint { get { return EditorPrefs.GetString(Pref + "Thumbprint", ""); } set { EditorPrefs.SetString(Pref + "Thumbprint", CleanThumbprint(value)); } }
        public static string PfxPath { get { return EditorPrefs.GetString(Pref + "Pfx", ""); } set { EditorPrefs.SetString(Pref + "Pfx", (value ?? "").Trim()); } }
        public static string TimestampUrl { get { return EditorPrefs.GetString(Pref + "Timestamp", DefaultTimestamp); } set { EditorPrefs.SetString(Pref + "Timestamp", (value ?? "").Trim()); } }
        public static string SignToolOverride { get { return EditorPrefs.GetString(Pref + "SignTool", ""); } set { EditorPrefs.SetString(Pref + "SignTool", (value ?? "").Trim()); } }
        /// <summary>Stop the build when it can't be signed (switch on once the certificate is set up).</summary>
        public static bool Required { get { return EditorPrefs.GetString(Pref + "Required", "") == "1"; } set { EditorPrefs.SetString(Pref + "Required", value ? "1" : ""); } }

        public static bool IsConfigured { get { return Thumbprint.Length > 0 || PfxPath.Length > 0; } }

        public static string Describe()
        {
            if (Thumbprint.Length > 0) return "certificate " + Thumbprint.Substring(0, Math.Min(8, Thumbprint.Length)) + "… (Windows certificate store)";
            if (PfxPath.Length > 0) return "certificate file " + Path.GetFileName(PfxPath);
            return "not set up";
        }

        public static string CleanThumbprint(string t)
        {
            return new string((t ?? "").Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();   // pasted from certmgr it often has spaces / hidden characters
        }

        /// <summary>The programs of a build folder that are ours to sign: top-level .exe files except Unity's crash handler.</summary>
        public static List<string> FilesToSign(string buildDir)
        {
            return Directory.GetFiles(buildDir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>signtool's arguments (the password, if any, is passed in but never logged).</summary>
        public static string SignArguments(IEnumerable<string> files, string thumbprint, string pfx, string password, string timestamp)
        {
            var a = new List<string> { "sign", "/fd", "SHA256" };
            if (!string.IsNullOrEmpty(timestamp)) { a.Add("/tr"); a.Add(Quote(timestamp)); a.Add("/td"); a.Add("SHA256"); }
            if (!string.IsNullOrEmpty(thumbprint)) { a.Add("/sha1"); a.Add(thumbprint); }
            else
            {
                a.Add("/f"); a.Add(Quote(pfx));
                if (!string.IsNullOrEmpty(password)) { a.Add("/p"); a.Add(Quote(password)); }
            }
            a.AddRange(files.Select(Quote));
            return string.Join(" ", a.ToArray());
        }

        private static string Quote(string s) { return "\"" + (s ?? "").Replace("\"", "") + "\""; }

        /// <summary>signtool.exe: the path set in Code Signing, else the newest Windows SDK's.</summary>
        public static string FindSignTool()
        {
            if (SignToolOverride.Length > 0) return File.Exists(SignToolOverride) ? SignToolOverride : null;
            var roots = new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") }
                .Where(r => !string.IsNullOrEmpty(r)).Select(r => Path.Combine(r, "Windows Kits", "10", "bin"));
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                string best = Directory.GetDirectories(root)
                    .Select(d => Path.Combine(d, "x64", "signtool.exe")).Where(File.Exists)
                    .OrderByDescending(p => VersionOf(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(p))))).FirstOrDefault();
                if (best != null) return best;
                string flat = Path.Combine(root, "x64", "signtool.exe");
                if (File.Exists(flat)) return flat;
            }
            return null;
        }

        private static Version VersionOf(string folder) { Version v; return Version.TryParse(folder, out v) ? v : new Version(0, 0); }

        /// <summary>
        /// Signs the given programs and checks the signatures. Returns null when signed, else what went wrong.
        /// </summary>
        public static string Sign(IList<string> files)
        {
            if (files.Count == 0) return null;
            if (Application.platform != RuntimePlatform.WindowsEditor) return "Code signing needs the Windows editor (signtool).";
            string tool = FindSignTool();
            if (tool == null) return "signtool.exe wasn't found. Install the Windows SDK (Signing Tools for Desktop Apps) or set its path in DAS > App Updates > Code Signing.";
            string password = null;
            if (Thumbprint.Length == 0)
            {
                if (!File.Exists(PfxPath)) return "The certificate file wasn't found: " + PfxPath;
                password = Environment.GetEnvironmentVariable(PasswordEnv);
            }
            string output;
            int code = Run(tool, SignArguments(files, Thumbprint, PfxPath, password, TimestampUrl), out output);
            if (code != 0) return "signtool couldn't sign (exit " + code + "): " + Tail(output);
            code = Run(tool, "verify /pa /q " + string.Join(" ", files.Select(Quote).ToArray()), out output);
            if (code != 0) return "The signatures didn't verify (exit " + code + "): " + Tail(output);
            return null;
        }

        private static int Run(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(5 * 60 * 1000)) { try { p.Kill(); } catch (Exception) { } output = "signtool didn't finish in 5 minutes"; return -1; }
                output = (o + "\n" + e).Trim();
                return p.ExitCode;
            }
        }

        private static string Tail(string s) { s = (s ?? "").Replace("\r", "").Trim(); return s.Length > 600 ? "…" + s.Substring(s.Length - 600) : s; }

        /// <summary>Called by the build step. Throws (stops the build) when signing is set up or required but fails.</summary>
        public static void SignBuild(string buildDir)
        {
            List<string> files = FilesToSign(buildDir);
            if (!IsConfigured)
            {
                if (Required) throw new UnityEditor.Build.BuildFailedException("DAS: code signing is required but no certificate is set up (DAS > App Updates > Code Signing).");
                Debug.LogWarning("[DAS App Updates] This build isn't code signed, so security software may flag DASUpdater.exe. Set up the company certificate: DAS > App Updates > Code Signing.");
                return;
            }
            string problem = Sign(files);
            if (problem != null) throw new UnityEditor.Build.BuildFailedException("DAS: code signing failed - " + problem);
            Debug.Log("[DAS App Updates] Code signed (" + Describe() + "): " + string.Join(", ", files.Select(Path.GetFileName).ToArray()));
        }
    }

    public class DasCodeSigningWindow : EditorWindow
    {
        private string _status = "";
        private MessageType _statusType = MessageType.None;

        [MenuItem("DAS/App Updates/Code Signing...", false, 62)]
        public static void ShowWindow()
        {
            var w = GetWindow<DasCodeSigningWindow>("DAS Code Signing");
            w.minSize = new Vector2(520, 380);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Every Windows build signs the DAS exe and DASUpdater.exe with the company's code-signing certificate, so " +
                "security software sees a known publisher. Get the certificate from IT, then ask them to allow DAS by signer " +
                "in SentinelOne. These settings are for this PC only and aren't saved in the project.", MessageType.Info);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Certificate", EditorStyles.boldLabel);
            DasCodeSigning.Thumbprint = EditorGUILayout.TextField(new GUIContent("Thumbprint", "In the Windows certificate store (certmgr.msc > Personal > Certificates > the certificate > Details > Thumbprint). Usual for company certificates and USB tokens."), DasCodeSigning.Thumbprint);
            EditorGUILayout.BeginHorizontal();
            DasCodeSigning.PfxPath = EditorGUILayout.TextField(new GUIContent("or .pfx file", "Only if there's no thumbprint. The password is read from the " + DasCodeSigning.PasswordEnv + " environment variable, never saved."), DasCodeSigning.PfxPath);
            if (GUILayout.Button("Choose...", GUILayout.Width(80)))
            {
                string f = EditorUtility.OpenFilePanel("Code-signing certificate", "", "pfx");
                if (!string.IsNullOrEmpty(f)) DasCodeSigning.PfxPath = f;
            }
            EditorGUILayout.EndHorizontal();
            DasCodeSigning.TimestampUrl = EditorGUILayout.TextField(new GUIContent("Timestamp server", "Keeps signatures valid after the certificate expires. IT may give a different one."), DasCodeSigning.TimestampUrl);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("signtool", EditorStyles.boldLabel);
            DasCodeSigning.SignToolOverride = EditorGUILayout.TextField(new GUIContent("Path (optional)", "Empty: the newest Windows SDK's signtool.exe."), DasCodeSigning.SignToolOverride);
            string tool = DasCodeSigning.FindSignTool();
            EditorGUILayout.LabelField(" ", tool ?? "not found - install the Windows SDK (Signing Tools for Desktop Apps)", EditorStyles.miniLabel);
            EditorGUILayout.Space(6);
            DasCodeSigning.Required = EditorGUILayout.Toggle(new GUIContent("Required for every build", "Stop the build if it can't be signed. Switch on once signing works."), DasCodeSigning.Required);
            EditorGUILayout.Space(6);
            if (GUILayout.Button("Test: sign a copy of DASUpdater.exe", GUILayout.Height(26))) Test();
            if (_status.Length > 0) EditorGUILayout.HelpBox(_status, _statusType);
        }

        private void Test()
        {
            string src = DasAppSigningKey.UpdaterExePath();
            if (src == null || !File.Exists(src)) { _status = "Scripts/AppUpdate/Updater~/DASUpdater.exe wasn't found."; _statusType = MessageType.Error; return; }
            if (!DasCodeSigning.IsConfigured) { _status = "Enter a thumbprint or choose a .pfx file first."; _statusType = MessageType.Warning; return; }
            string dir = Path.Combine(Path.GetTempPath(), "das-sign-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "DASUpdater.exe");
            File.Copy(src, copy, true);
            string problem = DasCodeSigning.Sign(new List<string> { copy });
            try { Directory.Delete(dir, true); } catch (Exception) { }
            _status = problem == null ? "Signed and verified with " + DasCodeSigning.Describe() + ". Builds are signed from now on." : problem;
            _statusType = problem == null ? MessageType.Info : MessageType.Error;
        }
    }
}
