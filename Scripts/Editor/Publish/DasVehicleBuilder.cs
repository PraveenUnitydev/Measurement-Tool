using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace VehicleMeasurement.EditorTools.Publish
{
    /// <summary>
    /// Builds ONE vehicle on its own: its own Addressables catalog and files, in its own folder, loading from its own
    /// version folder on the server. Nothing else in the project is built, and every Addressables setting touched is
    /// put back afterwards.
    ///
    /// Why per vehicle: the old way built all vehicles into one catalog that had to match the app build exactly (same
    /// PC, same project state). A vehicle built here only has to be built with the same Unity / Addressables / URP
    /// versions and the same scripts and shaders as the app (use the shared DAS project), on any PC.
    /// </summary>
    public static class DasVehicleBuilder
    {
        public class Result
        {
            public bool ok;
            public string error = "";
            public string outputFolder = "";          // ServerData/DASPublish/<id>/<version>
            public string catalogFile = "";           // relative to outputFolder, e.g. catalog_xuv700.json
            public List<string> files = new List<string>();   // relative paths of everything to upload
            public long totalBytes;
        }

        private const string BuildVar = "DASPublish.BuildPath";
        private const string LoadVar = "DASPublish.LoadPath";
        private const string CatalogBuildVar = "DASPublish.CatalogBuildPath";
        private const string CatalogLoadVar = "DASPublish.CatalogLoadPath";

        /// <param name="prefab">The vehicle prefab asset.</param>
        /// <param name="vehicleId">Server id (lower case), also used for the catalog and group names.</param>
        /// <param name="address">Addressables key the app opens it by ("das/&lt;id&gt;").</param>
        /// <param name="loadPath">Server folder of this version (from /publish/start).</param>
        /// <param name="version">Version label from the server.</param>
        public static Result Build(GameObject prefab, string vehicleId, string address, string loadPath, string version)
        {
            var result = new Result();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { result.error = "Addressables isn't set up in this project (Window > Asset Management > Addressables > Groups > Create)."; return result; }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                result.error = "Switch the build target to Windows (File > Build Profiles > Windows) - the DAS app runs on Windows 64-bit.";
                return result;
            }
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);
            if (string.IsNullOrEmpty(guid) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                result.error = "Pick the vehicle's prefab asset (from the Project window), not an object in the scene.";
                return result;
            }

            string outRel = "ServerData/DASPublish/" + vehicleId + "/" + version;
            string outAbs = Path.GetFullPath(outRel);
            try { if (Directory.Exists(outAbs)) Directory.Delete(outAbs, true); } catch (Exception) { }

            // ── remember everything we change ───────────────────────────
            string profileId = settings.activeProfileId;
            var ps = settings.profileSettings;
            bool prevRemoteCatalog = settings.BuildRemoteCatalog;
            string prevCatBuildId = settings.RemoteCatalogBuildPath.Id;
            string prevCatLoadId = settings.RemoteCatalogLoadPath.Id;
            string prevPlayerVersion = settings.OverridePlayerVersion;
            var prevMonoNaming = settings.MonoScriptBundleNaming;
            string prevMonoCustom = settings.MonoScriptBundleCustomNaming;
            var prevBuiltInNaming = settings.BuiltInBundleNaming;
            string prevBuiltInCustom = settings.BuiltInBundleCustomNaming;
            var includeBefore = new Dictionary<AddressableAssetGroupSchema, bool>();
            var playerDataBefore = new Dictionary<PlayerDataGroupSchema, KeyValuePair<bool, bool>>();
            AddressableAssetGroup tempGroup = null;
            AddressableAssetEntry previousEntry = settings.FindAssetEntry(guid);
            AddressableAssetGroup previousGroup = previousEntry != null ? previousEntry.parentGroup : null;
            string previousAddress = previousEntry != null ? previousEntry.address : null;
            var previousLabels = previousEntry != null ? new List<string>(previousEntry.labels) : null;

            try
            {
                EditorUtility.DisplayProgressBar("Publish vehicle", "Preparing the build of " + vehicleId + "...", 0.1f);

                // profile values for this build
                foreach (var name in new[] { BuildVar, LoadVar, CatalogBuildVar, CatalogLoadVar })
                    if (!ps.GetVariableNames().Contains(name)) ps.CreateValue(name, "");
                ps.SetValue(profileId, BuildVar, outRel + "/[BuildTarget]");
                ps.SetValue(profileId, LoadVar, loadPath.TrimEnd('/') + "/[BuildTarget]");
                ps.SetValue(profileId, CatalogBuildVar, outRel);
                ps.SetValue(profileId, CatalogLoadVar, loadPath.TrimEnd('/'));

                // only this vehicle goes into the build
                foreach (var g in settings.groups)
                {
                    if (g == null) continue;
                    var b = g.GetSchema<BundledAssetGroupSchema>();
                    if (b != null) { includeBefore[b] = b.IncludeInBuild; b.IncludeInBuild = false; }
                    var pd = g.GetSchema<PlayerDataGroupSchema>();
                    if (pd != null)
                    {
                        playerDataBefore[pd] = new KeyValuePair<bool, bool>(pd.IncludeResourcesFolders, pd.IncludeBuildSettingsScenes);
                        pd.IncludeResourcesFolders = false; pd.IncludeBuildSettingsScenes = false;
                    }
                }

                tempGroup = settings.CreateGroup("DAS_Publish_" + vehicleId, false, false, false, null, typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = tempGroup.GetSchema<BundledAssetGroupSchema>();
                schema.IncludeInBuild = true;
                schema.BuildPath.SetVariableByName(settings, BuildVar);
                schema.LoadPath.SetVariableByName(settings, LoadVar);
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
                schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;

                var entry = settings.CreateOrMoveEntry(guid, tempGroup, false, false);
                entry.address = address;

                settings.BuildRemoteCatalog = true;
                settings.RemoteCatalogBuildPath.SetVariableByName(settings, CatalogBuildVar);
                settings.RemoteCatalogLoadPath.SetVariableByName(settings, CatalogLoadVar);
                settings.OverridePlayerVersion = vehicleId;                            // -> catalog_<id>.json/.bin
                settings.MonoScriptBundleNaming = MonoScriptBundleNaming.Custom;       // unique per vehicle
                settings.MonoScriptBundleCustomNaming = "das_" + vehicleId;
                settings.BuiltInBundleNaming = BuiltInBundleNaming.Custom;
                settings.BuiltInBundleCustomNaming = "das_" + vehicleId;

                EditorUtility.DisplayProgressBar("Publish vehicle", "Building " + vehicleId + " (this can take a few minutes)...", 0.3f);
                AddressablesPlayerBuildResult build;
                AddressableAssetSettings.BuildPlayerContent(out build);
                if (build == null || !string.IsNullOrEmpty(build.Error))
                {
                    result.error = "The Addressables build failed: " + (build != null ? build.Error : "no result") + " (see the Console).";
                    return result;
                }
            }
            catch (Exception e)
            {
                result.error = "The build failed: " + e.Message;
                Debug.LogException(e);
                return result;
            }
            finally
            {
                // ── put everything back ─────────────────────────────────
                try
                {
                    if (previousGroup != null)
                    {
                        var back = settings.CreateOrMoveEntry(guid, previousGroup, false, false);
                        back.address = previousAddress;
                        if (previousLabels != null) foreach (var l in previousLabels) back.SetLabel(l, true, false, false);
                    }
                    if (tempGroup != null) settings.RemoveGroup(tempGroup);
                    foreach (var kv in includeBefore) if (kv.Key != null) kv.Key.IncludeInBuild = kv.Value;
                    foreach (var kv in playerDataBefore) if (kv.Key != null) { kv.Key.IncludeResourcesFolders = kv.Value.Key; kv.Key.IncludeBuildSettingsScenes = kv.Value.Value; }
                    settings.BuildRemoteCatalog = prevRemoteCatalog;
                    if (!string.IsNullOrEmpty(prevCatBuildId)) settings.RemoteCatalogBuildPath.SetVariableById(settings, prevCatBuildId);
                    if (!string.IsNullOrEmpty(prevCatLoadId)) settings.RemoteCatalogLoadPath.SetVariableById(settings, prevCatLoadId);
                    settings.OverridePlayerVersion = prevPlayerVersion;
                    settings.MonoScriptBundleNaming = prevMonoNaming;
                    settings.MonoScriptBundleCustomNaming = prevMonoCustom;
                    settings.BuiltInBundleNaming = prevBuiltInNaming;
                    settings.BuiltInBundleCustomNaming = prevBuiltInCustom;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception e) { Debug.LogError("[Publish] Could not restore every Addressables setting - check the Addressables Groups window: " + e.Message); }
                EditorUtility.ClearProgressBar();
            }

            // ── collect what to upload ──────────────────────────────────
            if (!Directory.Exists(outAbs)) { result.error = "The build produced no files in " + outRel + "."; return result; }
            foreach (string f in Directory.GetFiles(outAbs, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(outAbs.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                result.files.Add(rel);
                result.totalBytes += new FileInfo(f).Length;
                string name = Path.GetFileName(rel);
                if (!rel.Contains("/") && name.StartsWith("catalog_", StringComparison.OrdinalIgnoreCase)
                    && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                    result.catalogFile = rel;
            }
            if (string.IsNullOrEmpty(result.catalogFile)) { result.error = "The build made no catalog_" + vehicleId + " file in " + outRel + "."; return result; }
            if (!result.files.Any(f => f.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))) { result.error = "The build made no .bundle files."; return result; }
            result.outputFolder = outAbs;
            result.ok = true;
            return result;
        }
    }
}
