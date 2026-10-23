using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace VehicleMeasurement.EditorTools
{
    /// <summary>
    /// Finds and fixes project problems that live outside the scripts in git, so they can't be fixed by a code change
    /// alone. Runs once each time the editor opens (quietly when everything is fine) and from the menu
    /// DAS > Project Doctor.
    ///
    /// 1. "Cache expiration may not be higher than 12960000": something sets Unity's cache expiration above its
    ///    150-day maximum. Causes looked for: the retired DasCacheSettings scripts (removed from git; deleted here if
    ///    still in the project), any other script that sets expirationDelay (listed), and Addressables' Cache
    ///    Initialization Settings asset (corrected to 150 days).
    /// 2. Addressables groups with download Retry Count 0: one dropped connection fails a vehicle download. Set to 3.
    /// 3. Scenes and prefabs that still hold a retired server address (listed; the app replaces them at runtime).
    /// </summary>
    [InitializeOnLoad]
    public static class DasProjectDoctor
    {
        public const int MaxExpirationSeconds = 12960000;               // Unity's limit: 150 days
        private const string SessionKey = "DasProjectDoctor.RanThisSession";
        private static readonly string[] RetiredScriptNames = { "DasCacheSettings.cs", "DasCacheSettingsRunner.cs" };
        private static readonly Regex SetsExpiration = new Regex(@"\.\s*expirationDelay\s*=(?!=)");
        private static readonly Regex ExpirationField = new Regex(@"m_ExpirationDelay:\s*(\d+)");
        private static readonly Regex RetryField = new Regex(@"m_RetryCount:\s*(\d+)");
        private static readonly Regex RetiredUrl = new Regex(
            @"https?://(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|mrws\d+|your-server[^/:\s""']*)(:\d+)?[^\s""']*|http://vrc\.mahindra\.com[^\s""']*",
            RegexOptions.IgnoreCase);

        static DasProjectDoctor()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("DAS/Project Doctor (check and fix)")]
        public static void RunFromMenu() { Run(true); }

        public static void Run(bool interactive)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var fixedItems = new List<string>();
            var problems = new List<string>();
            try
            {
                FixRetiredScripts(fixedItems, problems);
                FixCacheInitialization(fixedItems, problems);
                FixRetryCounts(fixedItems, problems);
                FindRetiredUrls(problems);
            }
            catch (Exception e)
            {
                Debug.LogError("[DAS Project Doctor] Check failed: " + e);
                return;
            }

            if (fixedItems.Count > 0) AssetDatabase.SaveAssets();

            var report = new StringBuilder();
            if (fixedItems.Count > 0) report.AppendLine("Fixed:\n- " + string.Join("\n- ", fixedItems));
            if (problems.Count > 0) report.AppendLine((report.Length > 0 ? "\n" : "") + "Needs your attention:\n- " + string.Join("\n- ", problems));

            if (report.Length == 0)
            {
                if (interactive) EditorUtility.DisplayDialog("DAS Project Doctor", "Everything checked is fine.", "OK");
                return;
            }
            string text = report.ToString().TrimEnd();
            if (problems.Count > 0) Debug.LogWarning("[DAS Project Doctor]\n" + text);
            else Debug.Log("[DAS Project Doctor]\n" + text);
            if (interactive) EditorUtility.DisplayDialog("DAS Project Doctor", text.Length > 1500 ? text.Substring(0, 1500) + "\n… (full list in the Console)" : text, "OK");
        }

        // ── 1a. scripts ────────────────────────────────────────────────────
        private static void FixRetiredScripts(List<string> fixedItems, List<string> problems)
        {
            string self = Path.GetFileName(GetThisFilePath() ?? "DasProjectDoctor.cs");
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(path);
                if (string.Equals(name, self, StringComparison.OrdinalIgnoreCase)) continue;

                if (RetiredScriptNames.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (AssetDatabase.DeleteAsset(path)) fixedItems.Add("Deleted " + path + " (retired: it set the cache expiration above Unity's limit and caused the console error).");
                    else problems.Add("Delete " + path + " by hand: it sets the cache expiration above Unity's limit.");
                    continue;
                }

                string text;
                try { text = File.ReadAllText(path); } catch (Exception) { continue; }
                if (SetsExpiration.IsMatch(text) && !path.Replace('\\', '/').Contains("/Tests~/"))
                    problems.Add(path + " sets a cache expirationDelay. Unity's maximum is " + MaxExpirationSeconds + " (150 days); anything higher logs 'Cache expiration may not be higher than 12960000'. Remove that line.");
            }
        }

        // ── 1b. Addressables Cache Initialization Settings ─────────────────
        private static void FixCacheInitialization(List<string> fixedItems, List<string> problems)
        {
            foreach (string path in AssetFilesContaining("m_ExpirationDelay:"))
            {
                string text = File.ReadAllText(path);
                bool tooHigh = ExpirationField.Matches(text).Cast<Match>().Any(m => long.Parse(m.Groups[1].Value) > MaxExpirationSeconds);
                if (!tooHigh) continue;

                bool changed = false;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null) continue;
                    var so = new SerializedObject(asset);
                    SerializedProperty it = so.GetIterator();
                    while (it.Next(true))
                    {
                        if (it.propertyType == SerializedPropertyType.Integer && it.name == "m_ExpirationDelay" && it.longValue > MaxExpirationSeconds)
                        {
                            long was = it.longValue;
                            it.longValue = MaxExpirationSeconds;
                            changed = true;
                            fixedItems.Add(path + ": cache expiration was " + was + " s (" + (was / 86400) + " days), above Unity's limit; set to 150 days. This removes the 'Cache expiration may not be higher than 12960000' error.");
                        }
                    }
                    if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(asset); }
                }
                if (!changed) problems.Add(path + " has a cache expiration above 150 days and could not be changed automatically. Open it and set Expiration Delay to " + MaxExpirationSeconds + ".");
            }
        }

        // ── 2. download retries ────────────────────────────────────────────
        private static void FixRetryCounts(List<string> fixedItems, List<string> problems)
        {
            foreach (string path in AssetFilesContaining("m_RetryCount:"))
            {
                string text = File.ReadAllText(path);
                if (!RetryField.Matches(text).Cast<Match>().Any(m => m.Groups[1].Value == "0")) continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null) continue;
                    var so = new SerializedObject(asset);
                    SerializedProperty p = so.FindProperty("m_RetryCount");
                    if (p == null || p.propertyType != SerializedPropertyType.Integer || p.intValue != 0) continue;
                    p.intValue = 3;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                    fixedItems.Add(path + ": Addressables download Retry Count was 0 (a single dropped connection failed the whole vehicle download); set to 3. Takes effect in the next content build.");
                }
            }
        }

        // ── 3. retired addresses in scenes and prefabs ─────────────────────
        private static void FindRetiredUrls(List<string> problems)
        {
            foreach (string path in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                if (!(path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))) continue;
                string text;
                try { text = File.ReadAllText(path); } catch (Exception) { continue; }
                var found = RetiredUrl.Matches(text).Cast<Match>().Select(m => m.Value).Distinct().ToList();
                if (found.Count > 0)
                    problems.Add(path.Replace('\\', '/') + " still holds " + string.Join(", ", found) + ". The app now uses https://vrc.mahindra.com/api/das instead at runtime; update the field in the Inspector to remove the warning.");
            }
        }

        // ── helpers ────────────────────────────────────────────────────────
        private static IEnumerable<string> AssetFilesContaining(string marker)
        {
            foreach (string path in Directory.GetFiles("Assets", "*.asset", SearchOption.AllDirectories))
            {
                bool hit = false;
                try
                {
                    using (var reader = new StreamReader(path))
                    {
                        // binary-serialized assets can't be scanned as text and are skipped
                        char[] head = new char[5];
                        if (reader.Read(head, 0, 5) == 5 && new string(head) != "%YAML") continue;
                        string line;
                        while ((line = reader.ReadLine()) != null) if (line.Contains(marker)) { hit = true; break; }
                    }
                }
                catch (Exception) { continue; }
                if (hit) yield return path.Replace('\\', '/');
            }
        }

        private static string GetThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = null) { return path; }
    }
}
