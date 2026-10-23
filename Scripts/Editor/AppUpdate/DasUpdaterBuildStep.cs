using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VehicleMeasurement.AppUpdate;

namespace VehicleMeasurement.EditorTools.AppUpdates
{
    /// <summary>
    /// Every Windows build of DAS gets DASUpdater.exe and das-release.txt (what this build is), so any copy of DAS -
    /// even one built and shared by hand - can update itself later.
    /// </summary>
    public class DasUpdaterBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 100; } }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 && report.summary.platform != BuildTarget.StandaloneWindows) return;
            string dir = Path.GetDirectoryName(report.summary.outputPath);
            string updater = DasAppSigningKey.UpdaterExePath();
            if (updater == null || !File.Exists(updater))
                throw new BuildFailedException("DAS: Scripts/AppUpdate/Updater~/DASUpdater.exe is missing, so this build couldn't update itself. Get it from Git (or build it: Scripts/AppUpdate/Tests~/run.sh).");
            File.Copy(updater, Path.Combine(dir, "DASUpdater.exe"), true);
            DasCodeSigning.SignBuild(dir);      // before das-release.txt: its checksums are of the signed files

            string version = PlayerSettings.bundleVersion;
            if (!AppVersion.IsValid(version))
                Debug.LogWarning("[DAS App Updates] Version '" + version + "' isn't like 1.4.0 (Player Settings > Version), so this build can't be compared with updates.");
            File.WriteAllText(Path.Combine(dir, Manifest.InstalledFile), Manifest.Canonical(version, Manifest.FromFolder(dir)));
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml))
                Debug.LogWarning("[DAS App Updates] This build has no signing key, so it will never update itself. Create one: DAS > App Updates > Create Signing Key.");
            Debug.Log("[DAS App Updates] Added DASUpdater.exe and das-release.txt (version " + version + ") to " + dir);
        }
    }
}
