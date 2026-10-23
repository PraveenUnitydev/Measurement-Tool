using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// The list of vehicles that ship inside the app (Resources/Vehicles), without loading them.
    ///
    /// Before: the Add-vehicle picker called Resources.LoadAll on the Vehicles folder every time it opened. That loads
    /// every bundled vehicle - meshes, materials, textures - into memory on the main thread just to read their names,
    /// which is why the app went "Not Responding" on Add New Vehicle. Now the names come from a small index file
    /// (Resources/DAS/LocalVehicleIndex.json) that the editor writes before Play and before every build
    /// (see Editor/LocalVehicleIndexBuilder.cs). A vehicle is only loaded when the user picks it.
    /// </summary>
    public static class LocalVehicleIndex
    {
        /// <summary>Resources path of the index (Assets/Resources/DAS/LocalVehicleIndex.json).</summary>
        public const string IndexResource = "DAS/LocalVehicleIndex";

        [Serializable]
        public class Entry
        {
            public string name;          // shown in the list
            public string resourcePath;  // what Resources.LoadAsync needs, e.g. "Vehicles/Thar"
            public string modelYear;     // optional
        }

        [Serializable]
        private class Wrapper { public List<Entry> vehicles = new List<Entry>(); }

        /// <summary>Set by the editor so Play mode always sees the current folder contents (no stale index).</summary>
        public static Func<string, List<Entry>> EditorScan;

        private static List<Entry> _cached;
        private static string _cachedFolder;
        private static bool _warnedMissing;

        /// <summary>The bundled vehicles under Resources/<paramref name="folder"/>. Cheap after the first call.</summary>
        public static List<Entry> Get(string folder)
        {
            folder = Normalize(folder);
            if (_cached != null && _cachedFolder == folder) return _cached;

            List<Entry> all = null;
            if (EditorScan != null)
            {
                try { all = EditorScan(folder); }
                catch (Exception e) { Debug.LogWarning("[LocalVehicles] Editor scan failed, using the index file: " + e.Message); }
            }

            if (all == null)
            {
                var text = Resources.Load<TextAsset>(IndexResource);
                if (text != null)
                {
                    all = Filter(Parse(text.text), folder);
                    Resources.UnloadAsset(text);
                }
                else
                {
                    all = new List<Entry>();
                    if (!_warnedMissing)
                    {
                        _warnedMissing = true;
                        Debug.LogWarning("[LocalVehicles] No local vehicle index in this build, so bundled (Resources) vehicles are not listed. " +
                                         "It is written automatically on Play and on Build; run DAS > Rebuild Local Vehicle Index if needed.");
                    }
                }
            }

            _cached = all;
            _cachedFolder = folder;
            return all;
        }

        /// <summary>Forget the cached list (the editor calls this when the folder changes).</summary>
        public static void Invalidate() { _cached = null; _cachedFolder = null; }

        public static List<Entry> Parse(string json)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(json)) return list;
            try
            {
                var w = JsonUtility.FromJson<Wrapper>(json);
                if (w != null && w.vehicles != null)
                    foreach (var e in w.vehicles)
                        if (e != null && !string.IsNullOrEmpty(e.resourcePath))
                        {
                            if (string.IsNullOrEmpty(e.name)) e.name = LastSegment(e.resourcePath);
                            list.Add(e);
                        }
            }
            catch (Exception e) { Debug.LogWarning("[LocalVehicles] The index file is damaged: " + e.Message); }
            return list;
        }

        public static string ToJson(List<Entry> entries)
        {
            var w = new Wrapper();
            if (entries != null) w.vehicles.AddRange(entries);
            return JsonUtility.ToJson(w, true);
        }

        /// <summary>Entries under the folder (and its subfolders, like Resources.LoadAll), sorted by name, no duplicates.</summary>
        public static List<Entry> Filter(List<Entry> entries, string folder)
        {
            folder = Normalize(folder);
            string prefix = folder.Length == 0 ? "" : folder + "/";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Entry>();
            if (entries == null) return result;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.resourcePath)) continue;
                string p = e.resourcePath.Replace('\\', '/');
                if (prefix.Length > 0 && !p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(p)) continue;
                result.Add(e);
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static string Normalize(string folder)
        {
            return (folder ?? "").Replace('\\', '/').Trim('/');
        }

        public static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int i = path.Replace('\\', '/').LastIndexOf('/');
            return i >= 0 ? path.Substring(i + 1) : path;
        }
    }
}
