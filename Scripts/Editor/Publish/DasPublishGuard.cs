using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VehicleMeasurement.EditorTools.Publish
{
    /// <summary>
    /// Keeps a vehicle publish from leaking into the next app build.
    ///
    /// A publish runs a real Addressables build of ONE vehicle. Addressables writes its local build data (main catalog,
    /// settings.json) to Library/com.unity.addressables/aa/&lt;platform&gt;, and the app build copies that folder into the
    /// exe. Left as is, the next exe would only know the published vehicle ("No Location found for Key=...").
    ///
    /// So the publish tool saves that folder (and the content state file) before its build and puts them back after.
    /// If that can't be done, a marker is left and app builds are stopped until Addressables is built normally again.
    /// </summary>
    public class DasPublishGuard : IPreprocessBuildWithReport
    {
        public static bool PublishBuildRunning;

        private const string Dir = "Library/DASPublish";
        private static string Marker { get { return Path.Combine(Dir, "addressables-needs-rebuild.txt"); } }
        private static string BackupDir { get { return Path.Combine(Dir, "aa-backup"); } }

        public class Saved
        {
            public string buildPath;          // Library/com.unity.addressables/aa/<platform>
            public bool hadBuild;
            public bool backedUp;
            public string statePath;          // addressables_content_state.bin
            public byte[] stateBytes;
        }

        public int callbackOrder { get { return -1000; } }

        // ── app build check ────────────────────────────────────────────
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!File.Exists(Marker)) return;
            throw new BuildFailedException(
                "DAS: a vehicle was published in this project and the Addressables build data couldn't be put back, so this "
                + "app build would only know that vehicle. Build Addressables first (Window > Asset Management > Addressables > "
                + "Groups > Build > New Build > Default Build Script), upload ServerData as usual, then build the app again.");
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            // A normal (non-publish) Addressables build makes the data good again.
            BuildScript.buildCompleted += r =>
            {
                if (PublishBuildRunning || r == null || !string.IsNullOrEmpty(r.Error)) return;
                try { if (File.Exists(Marker)) { File.Delete(Marker); Debug.Log("[DAS Publish] Addressables built - app builds are allowed again."); } } catch (Exception) { }
            };
        }

        [MenuItem("DAS/Publish/Allow app builds again (I rebuilt Addressables)", false, 120)]
        private static void ClearMarker()
        {
            try { if (File.Exists(Marker)) File.Delete(Marker); } catch (Exception) { }
            EditorUtility.DisplayDialog("DAS", "App builds are allowed again. Make sure Addressables was built normally after the last publish.", "OK");
        }

        // ── save / restore ─────────────────────────────────────────────
        public static Saved SaveAddressablesBuildData(AddressableAssetSettings settings)
        {
            var s = new Saved();
            try
            {
                s.buildPath = Path.GetFullPath(UnityEngine.AddressableAssets.Addressables.BuildPath);
                s.hadBuild = Directory.Exists(s.buildPath);
                string backup = Path.GetFullPath(BackupDir);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                if (s.hadBuild) { CopyDir(s.buildPath, backup); s.backedUp = true; }
            }
            catch (Exception e) { Debug.LogWarning("[DAS Publish] Couldn't save the Addressables build data: " + e.Message); }
            try
            {
                s.statePath = ContentUpdateScript.GetContentStateDataPath(false, settings);
                if (!string.IsNullOrEmpty(s.statePath) && File.Exists(s.statePath)) s.stateBytes = File.ReadAllBytes(s.statePath);
            }
            catch (Exception) { }
            return s;
        }

        public static void RestoreAddressablesBuildData(Saved s)
        {
            if (s == null) return;
            bool ok = false;
            try
            {
                if (!string.IsNullOrEmpty(s.buildPath))
                {
                    if (Directory.Exists(s.buildPath)) Directory.Delete(s.buildPath, true);
                    if (s.backedUp) { CopyDir(Path.GetFullPath(BackupDir), s.buildPath); ok = true; }
                    // No normal build existed before the publish: there's nothing good to put back.
                }
            }
            catch (Exception e) { Debug.LogError("[DAS Publish] Couldn't put the Addressables build data back: " + e.Message); ok = false; }
            try
            {
                if (!string.IsNullOrEmpty(s.statePath))
                {
                    if (s.stateBytes != null) File.WriteAllBytes(s.statePath, s.stateBytes);
                    else if (File.Exists(s.statePath)) File.Delete(s.statePath);
                }
            }
            catch (Exception e) { Debug.LogWarning("[DAS Publish] Couldn't put the content state file back: " + e.Message); }
            try { if (Directory.Exists(BackupDir)) Directory.Delete(BackupDir, true); } catch (Exception) { }

            try
            {
                Directory.CreateDirectory(Dir);
                if (ok) { if (File.Exists(Marker)) File.Delete(Marker); }
                else
                {
                    File.WriteAllText(Marker, DateTime.Now.ToString("u") + " - Addressables build data not restored after a vehicle publish.");
                    Debug.LogWarning("[DAS Publish] Build Addressables normally before the next app build (app builds are blocked until then).");
                }
            }
            catch (Exception) { }
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length).TrimStart('/', '\\')));
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, Path.Combine(to, f.Substring(from.Length).TrimStart('/', '\\')), true);
        }
    }
}
