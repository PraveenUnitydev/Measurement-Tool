using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VehicleMeasurement.Storage
{
    public enum LibraryKind
    {
        Server,     // in the server catalog
        Local,      // built into the app (Resources), always available
        Orphan,     // files on this PC for a vehicle the server no longer lists
    }

    /// <summary>One vehicle as the Vehicles screen and the update notice see it.</summary>
    public class LibraryItem
    {
        public LibraryKind kind = LibraryKind.Server;
        public bool hasMeasurements;      // measurements saved on this PC
        public string localPath = "";     // Resources path for built-in vehicles
        public string vehicleId = "", name = "", manufacturer = "", addressableKey = "", version = "";
        public bool known;                 // the download state is known (storage service ready)
        public bool downloaded;            // current files on this PC
        public bool needsUpdate;           // an older version is on this PC (or files are missing)
        public bool isNew;                 // appeared on the server since this PC last looked
        public bool changedOnServer;       // its server version changed since this PC last looked (downloaded or not)
        public long downloadBytes;         // what downloading / updating it would take
        public long totalBytes;
        public string label = "";
        public LabelTone tone = LabelTone.None;

        /// <summary>Can be selected for download or update.</summary>
        public bool Actionable { get { return kind == LibraryKind.Server && (!known || !downloaded || needsUpdate); } }

        /// <summary>Has files on this PC that can be removed.</summary>
        public bool Removable { get { return (kind == LibraryKind.Server && known && (downloaded || needsUpdate)) || kind == LibraryKind.Orphan; } }

        /// <summary>Can be opened for measuring (a server vehicle downloads first if needed).</summary>
        public bool Openable { get { return kind != LibraryKind.Orphan; } }
    }

    public enum LibraryFilter { All, Updates, New, NotDownloaded, OnThisPc }

    /// <summary>What the server's catalog says about a vehicle (plain data, so this file has no Unity types).</summary>
    public class CatalogEntry { public string vehicleId, name, manufacturer, addressableKey, version; }

    /// <summary>What is on this PC for a vehicle (null = not known yet).</summary>
    public class FileState
    {
        public bool downloaded, needsUpdate;
        public long downloadBytes, totalBytes;
        public string label = "";
        public LabelTone tone = LabelTone.None;
    }

    /// <summary>Counts for the notice and the filter tabs.</summary>
    public class LibrarySummary
    {
        public int total, downloaded, updates, isNew, notDownloaded, changedOnServer;
        public long updateBytes;

        /// <summary>"2 new vehicles · 3 updates for vehicles on this PC (1.2 GB)" or "" when there's nothing to say.</summary>
        public string NoticeText()
        {
            var parts = new List<string>();
            if (isNew > 0) parts.Add(isNew + " new vehicle" + (isNew == 1 ? "" : "s"));
            if (updates > 0) parts.Add(updates + " update" + (updates == 1 ? "" : "s") + " for vehicles on this PC" + (updateBytes > 0 ? " (" + ByteFormat.Format(updateBytes) + ")" : ""));
            int otherChanged = changedOnServer;
            if (otherChanged > 0 && parts.Count == 0) parts.Add(otherChanged + " vehicle" + (otherChanged == 1 ? " was" : "s were") + " updated on the server");
            return string.Join(" · ", parts);
        }

        public bool HasNotice { get { return isNew > 0 || updates > 0 || changedOnServer > 0; } }
    }

    public static class VehicleLibrary
    {
        public static List<LibraryItem> Build(IEnumerable<CatalogEntry> catalog, Func<CatalogEntry, FileState> files, CatalogSeen seen)
        {
            var list = new List<LibraryItem>();
            if (catalog == null) return list;
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CatalogEntry c in catalog)
            {
                if (c == null || string.IsNullOrEmpty(c.vehicleId) || !ids.Add(c.vehicleId)) continue;
                var item = new LibraryItem
                {
                    vehicleId = c.vehicleId,
                    name = string.IsNullOrEmpty(c.name) ? c.vehicleId : c.name,
                    manufacturer = c.manufacturer ?? "",
                    addressableKey = c.addressableKey ?? "",
                    version = c.version ?? "",
                };
                FileState f = files != null ? files(c) : null;
                if (f != null)
                {
                    item.known = true;
                    item.downloaded = f.downloaded;
                    item.needsUpdate = f.needsUpdate;
                    item.downloadBytes = f.downloadBytes;
                    item.totalBytes = f.totalBytes;
                    item.label = f.label ?? "";
                    item.tone = f.tone;
                }
                if (seen != null && seen.HasBaseline)
                {
                    string seenVersion;
                    if (!seen.TryGet(c.vehicleId, out seenVersion)) item.isNew = true;
                    else if (!string.Equals(seenVersion ?? "", item.version, StringComparison.Ordinal)) item.changedOnServer = true;
                }
                list.Add(item);
            }
            return list;
        }

        public static LibrarySummary Summarize(IEnumerable<LibraryItem> items)
        {
            var s = new LibrarySummary();
            foreach (LibraryItem i in items ?? Enumerable.Empty<LibraryItem>())
            {
                s.total++;
                if (i.kind != LibraryKind.Server) { s.downloaded++; continue; }      // built-in / left-over: on this PC
                if (i.isNew) s.isNew++;
                if (i.changedOnServer) s.changedOnServer++;
                if (!i.known) continue;
                if (i.needsUpdate) { s.updates++; s.updateBytes += Math.Max(0, i.downloadBytes); }
                else if (i.downloaded) s.downloaded++;
                else s.notDownloaded++;
            }
            return s;
        }

        public static bool Matches(LibraryItem i, LibraryFilter filter, string search)
        {
            if (i == null) return false;
            switch (filter)
            {
                case LibraryFilter.Updates: if (!(i.kind == LibraryKind.Server && i.known && i.needsUpdate)) return false; break;
                case LibraryFilter.New: if (!i.isNew) return false; break;
                case LibraryFilter.NotDownloaded: if (!(i.kind == LibraryKind.Server && i.known && !i.downloaded && !i.needsUpdate)) return false; break;
                case LibraryFilter.OnThisPc: if (!(i.kind != LibraryKind.Server || (i.known && (i.downloaded || i.needsUpdate)))) return false; break;
            }
            if (string.IsNullOrEmpty(search)) return true;
            string q = search.Trim();
            if (q.Length == 0) return true;
            return Contains(i.name, q) || Contains(i.manufacturer, q) || Contains(i.vehicleId, q) || Contains(i.addressableKey, q);
        }

        /// <summary>Sort: updates first, then new, then not downloaded, then on this PC; by name inside each.</summary>
        public static List<LibraryItem> Sorted(IEnumerable<LibraryItem> items)
        {
            return (items ?? Enumerable.Empty<LibraryItem>())
                .OrderBy(i => i.kind != LibraryKind.Server ? 4 : i.known && i.needsUpdate ? 0 : i.isNew ? 1 : (!i.known || !i.downloaded) ? 2 : 3)
                .ThenBy(i => i.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Bytes needed for a selection (not-yet-known sizes count as 0).</summary>
        public static long BytesFor(IEnumerable<LibraryItem> items)
        {
            long total = 0;
            foreach (LibraryItem i in items ?? Enumerable.Empty<LibraryItem>())
                if (i != null && i.Actionable) total += Math.Max(0, i.downloadBytes > 0 ? i.downloadBytes : (i.downloaded ? 0 : i.totalBytes));
            return total;
        }

        private static bool Contains(string s, string q) { return !string.IsNullOrEmpty(s) && s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }
    }

    /// <summary>
    /// The server catalog this PC last showed the user (vehicle id + version), to tell what is new or changed since.
    /// The first time (no file yet) everything is taken as already seen, so a new install isn't told "59 new vehicles".
    /// </summary>
    public class CatalogSeen
    {
        private readonly string _file;
        private readonly Dictionary<string, string> _seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasBaseline { get; private set; }

        public CatalogSeen(string file)
        {
            _file = file;
            try
            {
                if (!string.IsNullOrEmpty(file) && File.Exists(file))
                {
                    foreach (string line in File.ReadAllLines(file))
                    {
                        int bar = line.IndexOf('|');
                        if (bar <= 0) continue;
                        _seen[line.Substring(0, bar)] = line.Substring(bar + 1);
                    }
                    HasBaseline = true;
                }
            }
            catch (Exception) { HasBaseline = false; }
        }

        public bool TryGet(string vehicleId, out string version) { return _seen.TryGetValue(vehicleId ?? "", out version); }

        /// <summary>Remember this catalog as seen (after the user looked, or as the first-run baseline).</summary>
        public void MarkSeen(IEnumerable<CatalogEntry> catalog)
        {
            _seen.Clear();
            foreach (CatalogEntry c in catalog ?? Enumerable.Empty<CatalogEntry>())
                if (c != null && !string.IsNullOrEmpty(c.vehicleId) && c.vehicleId.IndexOf('|') < 0)
                    _seen[c.vehicleId] = (c.version ?? "").Replace("\n", "").Replace("\r", "");
            HasBaseline = true;
            Save();
        }

        /// <summary>Mark single vehicles as seen (e.g. after downloading them) without touching the rest.</summary>
        public void MarkSeen(CatalogEntry c)
        {
            if (c == null || string.IsNullOrEmpty(c.vehicleId) || c.vehicleId.IndexOf('|') >= 0) return;
            _seen[c.vehicleId] = c.version ?? "";
            if (HasBaseline) Save();
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_file)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file));
                var sb = new StringBuilder();
                foreach (var kv in _seen) sb.Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
                string tmp = _file + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(_file)) File.Delete(_file);
                File.Move(tmp, _file);
            }
            catch (Exception) { /* best effort: worst case the notice repeats */ }
        }
    }
}
