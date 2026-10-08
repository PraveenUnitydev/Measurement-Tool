using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VehicleMeasurement.EditorTools
{
    /// <summary>
    /// DAS > Clip Section > Install clipping into vehicle Shader Graphs
    ///
    /// Replaces the project's vehicle Shader Graphs (SG_Car_Paint_Material, SG_Glass_Material, ...) with versions that
    /// clip at the clip-section plane themselves (Scripts/Shaders/ClippableGraphs~, made by Tools~/patch_shadergraphs.py).
    /// Each graph gets one Custom Function node (DASClip.hlsl) on Alpha Clip Threshold, alpha clipping on, both faces
    /// rendered and a hidden _DASClipSupport property; nothing else in the graph changes, so materials keep their values.
    ///
    /// With these graphs the app leaves vehicle materials alone in clip mode: real car paint, clear coat, reflections and
    /// see-through glass while cutting. Vehicles already uploaded keep the old graphs inside their bundles until their
    /// Addressables are rebuilt and uploaded again - until then the app uses the clip stand-in (ClipSection_Lit) for them.
    ///
    /// The originals are backed up outside Assets (ShaderGraphBackups/&lt;date&gt;/) before anything is replaced.
    /// </summary>
    public static class ClipShaderGraphInstaller
    {
        private const string SourceFolderName = "ClippableGraphs~";
        private const string DasClipIncludeGuid = "7d3a5c19e2b84f60a1c4d8e9f0b26a51";
        private const string Marker = "_DASClipSupport";

        [MenuItem("DAS/Clip Section/Install clipping into vehicle Shader Graphs")]
        public static void Install()
        {
            string include = AssetDatabase.GUIDToAssetPath(DasClipIncludeGuid);
            if (string.IsNullOrEmpty(include))
            {
                EditorUtility.DisplayDialog("Clip section",
                    "DASClip.hlsl was not found (its .meta GUID must stay " + DasClipIncludeGuid + ").\n\n" +
                    "Make sure Scripts/Shaders/Resources/DAS/DASClip.hlsl and its .meta are in the project.", "OK");
                return;
            }

            string source = FindSourceFolder();
            if (source == null)
            {
                EditorUtility.DisplayDialog("Clip section", "Folder '" + SourceFolderName + "' with the clippable graphs was not found under Assets.", "OK");
                return;
            }

            var plan = new List<KeyValuePair<string, string>>();   // source file -> project file
            var report = new StringBuilder();
            foreach (string src in Directory.GetFiles(source, "*.shadergraph"))
            {
                string name = Path.GetFileName(src);
                string[] targets = FindProjectFiles(name);
                if (targets.Length == 0) { report.AppendLine("  not in project: " + name); continue; }
                foreach (string t in targets)
                {
                    if (File.ReadAllText(t).Contains(Marker)) { report.AppendLine("  already clips: " + Rel(t)); continue; }
                    plan.Add(new KeyValuePair<string, string>(src, t));
                }
            }

            if (plan.Count == 0)
            {
                EditorUtility.DisplayDialog("Clip section", "Nothing to install.\n\n" + report, "OK");
                return;
            }

            var msg = new StringBuilder();
            msg.AppendLine("These Shader Graphs will be replaced by versions that clip in the clip section:");
            foreach (var p in plan) msg.AppendLine("  " + Rel(p.Value));
            if (report.Length > 0) { msg.AppendLine(); msg.Append(report); }
            msg.AppendLine();
            msg.AppendLine("Originals are backed up to ShaderGraphBackups/ (outside Assets).");
            msg.AppendLine("Vehicles already uploaded need their Addressables rebuilt and uploaded to get this.");
            if (!EditorUtility.DisplayDialog("Clip section", msg.ToString(), "Install", "Cancel")) return;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string backup = Path.Combine(projectRoot, "ShaderGraphBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            int done = 0;
            foreach (var p in plan)
            {
                try
                {
                    string rel = Rel(p.Value);
                    string bak = Path.Combine(backup, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(bak));
                    File.Copy(p.Value, bak, true);
                    File.Copy(p.Key, p.Value, true);   // the .meta (GUID) stays, so materials keep pointing at it
                    done++;
                }
                catch (Exception e)
                {
                    Debug.LogError("[ClipSection] Could not replace " + p.Value + ": " + e.Message);
                }
            }
            AssetDatabase.Refresh();
            Debug.Log("[ClipSection] Installed clipping into " + done + " Shader Graph(s). Backup: " + backup);
            EditorUtility.DisplayDialog("Clip section",
                done + " Shader Graph(s) now clip by themselves.\n\nBackup: " + backup +
                "\n\nRebuild and upload the vehicle Addressables so downloaded vehicles get them too.", "OK");
        }

        [MenuItem("DAS/Clip Section/Report which vehicle shaders clip natively")]
        public static void Report()
        {
            var sb = new StringBuilder();
            int yes = 0, no = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) || !path.StartsWith("Assets/")) continue;
                bool clips = File.ReadAllText(path).Contains(Marker);
                if (clips) yes++; else no++;
                sb.AppendLine((clips ? "  clips:      " : "  stand-in:   ") + path);
            }
            Debug.Log("[ClipSection] Shader Graphs - " + yes + " clip by themselves, " + no + " use the clip stand-in:\n" + sb);
            EditorUtility.DisplayDialog("Clip section", yes + " Shader Graph(s) clip by themselves, " + no + " use the stand-in.\nDetails in the Console.", "OK");
        }

        private static string FindSourceFolder()
        {
            try
            {
                return Directory.GetDirectories(Application.dataPath, SourceFolderName, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ClipSection] Folder search failed: " + e.Message);
                return null;
            }
        }

        private static string[] FindProjectFiles(string fileName)
        {
            return Directory.GetFiles(Application.dataPath, fileName, SearchOption.AllDirectories)
                .Where(p => p.IndexOf("~", StringComparison.Ordinal) < 0)
                .ToArray();
        }

        private static string Rel(string full)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string f = Path.GetFullPath(full);
            return f.StartsWith(root) ? f.Substring(root.Length).TrimStart('/', '\\') : f;
        }
    }
}
