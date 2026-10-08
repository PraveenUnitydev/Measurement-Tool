using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VehicleMeasurement.EditorTools
{
    /// <summary>
    /// Writes Assets/Resources/DAS/LocalVehicleIndex.json - the names of the vehicles bundled under Resources - so the
    /// app can list them without loading them (see LocalVehicleIndex). Runs before Play, before every build, and from
    /// the menu DAS > Rebuild Local Vehicle Index. Only reads the asset list; no vehicle is loaded.
    /// </summary>
    [InitializeOnLoad]
    public class LocalVehicleIndexBuilder : IPreprocessBuildWithReport
    {
        private const string IndexAssetPath = "Assets/Resources/DAS/LocalVehicleIndex.json";

        private static readonly HashSet<string> ModelExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".prefab", ".fbx", ".obj", ".blend", ".dae", ".3ds", ".dxf", ".max", ".ma", ".mb", ".glb", ".gltf" };

        private static readonly Regex ModelYearLine = new Regex(@"^\s*modelYear:\s*(.*?)\s*$", RegexOptions.Multiline);

        public int callbackOrder { get { return 0; } }

        static LocalVehicleIndexBuilder()
        {
            // Play mode in the editor reads the folder directly, so a vehicle added a second ago is listed.
            LocalVehicleIndex.EditorScan = folder => LocalVehicleIndex.Filter(Scan(), folder);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Write(false);
                if (state == PlayModeStateChange.EnteredPlayMode) LocalVehicleIndex.Invalidate();
            };
        }

        public void OnPreprocessBuild(BuildReport report) { Write(true); }

        [MenuItem("DAS/Rebuild Local Vehicle Index")]
        private static void RebuildFromMenu()
        {
            int n = Write(true);
            EditorUtility.DisplayDialog("Local vehicle index", n + " bundled model(s) indexed.\n\n" + IndexAssetPath, "OK");
        }

        /// <summary>Every model/prefab under any Resources folder, as Resources paths.</summary>
        public static List<LocalVehicleIndex.Entry> Scan()
        {
            var list = new List<LocalVehicleIndex.Entry>();
            foreach (string assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                string ext = Path.GetExtension(assetPath);
                if (!ModelExtensions.Contains(ext)) continue;
                int r = assetPath.LastIndexOf("/Resources/", StringComparison.Ordinal);
                if (r < 0) continue;
                if (assetPath.Contains("/Editor/")) continue;

                string rel = assetPath.Substring(r + "/Resources/".Length);
                rel = rel.Substring(0, rel.Length - ext.Length);
                list.Add(new LocalVehicleIndex.Entry
                {
                    name = Path.GetFileNameWithoutExtension(assetPath),
                    resourcePath = rel,
                    modelYear = ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ? ReadModelYear(assetPath) : ""
                });
            }
            return list;
        }

        /// <summary>The VehiclePrefabData.modelYear written in a text prefab, without loading it ("" if not found).</summary>
        private static string ReadModelYear(string assetPath)
        {
            try
            {
                string full = Path.GetFullPath(assetPath);
                var info = new FileInfo(full);
                if (!info.Exists || info.Length > 64L * 1024 * 1024) return "";
                string text = File.ReadAllText(full);
                var m = ModelYearLine.Match(text);
                if (!m.Success) return "";
                string v = m.Groups[1].Value.Trim().Trim('\'', '"');
                return v;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Write the index if it changed. Returns the number of entries.</summary>
        public static int Write(bool log)
        {
            var entries = LocalVehicleIndex.Filter(Scan(), "");
            string json = LocalVehicleIndex.ToJson(entries);
            try
            {
                string full = Path.GetFullPath(IndexAssetPath);
                string old = File.Exists(full) ? File.ReadAllText(full) : null;
                if (old != json)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllText(full, json);
                    AssetDatabase.ImportAsset(IndexAssetPath, ImportAssetOptions.ForceSynchronousImport);
                    if (log) Debug.Log("[LocalVehicles] Index updated: " + entries.Count + " model(s) -> " + IndexAssetPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[LocalVehicles] Could not write " + IndexAssetPath + ": " + e.Message);
            }
            LocalVehicleIndex.Invalidate();
            return entries.Count;
        }
    }
}
