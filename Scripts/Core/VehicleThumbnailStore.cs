using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VehicleMeasurement
{
    /// <summary>
    /// Vehicle thumbnails kept on this PC, and when to ask the server again.
    ///
    /// Before: a thumbnail was saved once per vehicle + catalog version and never checked again, and Home preferred an
    /// even older copy saved in the measurements folder - so a thumbnail replaced on the server never showed up unless
    /// the vehicle's version changed. Now each copy remembers what the server said about it (the server's thumbnail
    /// version, or the storage ETag / Last-Modified) and is checked against the server once per session:
    ///  * server sends a thumbnail version (VRSP catalog "thumbnailVersion"): same -> keep, different -> download;
    ///  * no version from the server: a conditional request (If-None-Match / If-Modified-Since) -> "304 not changed"
    ///    costs nothing, a changed picture comes back in full.
    /// Plain file IO only (no Unity types), so it is tested outside Unity.
    /// </summary>
    public class VehicleThumbnailStore
    {
        public class Entry
        {
            public string vehicleId = "";
            public string version = "";        // server thumbnail version it was downloaded for ("" = unknown)
            public string etag = "";
            public string lastModified = "";
            public string source = "";         // storage path it came from, for logs
            public long savedUtcTicks;
            public string imageFile = "";      // full path of the picture
        }

        public enum Step { UseDisk, Revalidate, Download }

        public readonly string Folder;
        private const string ImageExt = ".thumb";
        private const string MetaExt = ".thumb.txt";

        public VehicleThumbnailStore(string folder) { Folder = folder; }

        /// <summary>What to do for a vehicle this session.</summary>
        public static Step Decide(Entry onDisk, string serverVersion, bool checkedThisSession)
        {
            if (onDisk == null) return Step.Download;
            if (!string.IsNullOrEmpty(serverVersion))
                return string.Equals(onDisk.version, serverVersion, StringComparison.Ordinal) ? Step.UseDisk : Step.Download;
            if (checkedThisSession) return Step.UseDisk;
            return (string.IsNullOrEmpty(onDisk.etag) && string.IsNullOrEmpty(onDisk.lastModified)) ? Step.Download : Step.Revalidate;
        }

        public static string SafeName(string vehicleId)
        {
            string id = string.IsNullOrEmpty(vehicleId) ? "unknown" : vehicleId;
            return Regex.Replace(id, @"[^A-Za-z0-9._-]", "_");
        }

        public string ImagePath(string vehicleId) { return Path.Combine(Folder, SafeName(vehicleId) + ImageExt); }
        private string MetaPath(string vehicleId) { return Path.Combine(Folder, SafeName(vehicleId) + MetaExt); }

        /// <summary>The copy on disk, or null.</summary>
        public Entry Get(string vehicleId)
        {
            string img = ImagePath(vehicleId);
            if (!File.Exists(img)) return null;
            var e = new Entry { vehicleId = vehicleId, imageFile = img };
            string meta = MetaPath(vehicleId);
            if (File.Exists(meta))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(meta))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i), v = line.Substring(i + 1);
                        switch (k)
                        {
                            case "version": e.version = v; break;
                            case "etag": e.etag = v; break;
                            case "lastModified": e.lastModified = v; break;
                            case "source": e.source = v; break;
                            case "saved": long.TryParse(v, out e.savedUtcTicks); break;
                        }
                    }
                }
                catch (Exception) { /* unreadable notes: treated as unknown, so it is checked */ }
            }
            return e;
        }

        /// <summary>Keep a downloaded picture with what the server said about it (written atomically).</summary>
        public Entry Save(string vehicleId, byte[] bytes, string version, string etag, string lastModified, string source)
        {
            Directory.CreateDirectory(Folder);
            string img = ImagePath(vehicleId);
            WriteAtomic(img, bytes);
            var e = new Entry
            {
                vehicleId = vehicleId, imageFile = img, version = version ?? "", etag = etag ?? "",
                lastModified = lastModified ?? "", source = source ?? "", savedUtcTicks = DateTime.UtcNow.Ticks,
            };
            WriteMeta(e);
            return e;
        }

        /// <summary>The server confirmed the copy is current: remember the version/validators it gave.</summary>
        public void Confirm(Entry e, string version, string etag, string lastModified)
        {
            if (e == null) return;
            if (!string.IsNullOrEmpty(version)) e.version = version;
            if (!string.IsNullOrEmpty(etag)) e.etag = etag;
            if (!string.IsNullOrEmpty(lastModified)) e.lastModified = lastModified;
            try { WriteMeta(e); } catch (Exception) { }
        }

        public void Delete(string vehicleId)
        {
            TryDelete(ImagePath(vehicleId));
            TryDelete(MetaPath(vehicleId));
        }

        /// <summary>Remove copies from the old naming scheme (&lt;id&gt;_&lt;version&gt;.img), which were never checked again.</summary>
        public int RemoveOldScheme()
        {
            int n = 0;
            if (!Directory.Exists(Folder)) return 0;
            foreach (string f in Directory.GetFiles(Folder, "*.img"))
                if (TryDelete(f)) n++;
            return n;
        }

        /// <summary>Headers for a conditional request ("send it only if it changed").</summary>
        public static Dictionary<string, string> ConditionalHeaders(Entry e)
        {
            var h = new Dictionary<string, string>();
            if (e == null) return h;
            if (!string.IsNullOrEmpty(e.etag)) h["If-None-Match"] = e.etag;
            if (!string.IsNullOrEmpty(e.lastModified)) h["If-Modified-Since"] = e.lastModified;
            return h;
        }

        /// <summary>
        /// The storage path inside a thumbnail link ("AssesmentSystem/Thumbnail/creta.png" -> "creta.png"), so an
        /// expired signed link (offline catalog copy) can be asked for again through the server's /thumbnails/ route.
        /// Returns null when the link isn't a thumbnail link.
        /// </summary>
        public static string RelativeThumbnailPath(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string path = url;
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);
            foreach (string marker in new[] { "/Thumbnail/", "/thumbnails/" })
            {
                int i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (i >= 0)
                {
                    string rel = Uri.UnescapeDataString(path.Substring(i + marker.Length));
                    return IsSafeRelative(rel) ? rel : null;
                }
            }
            return null;
        }

        /// <summary>Same rule as the server's /thumbnails/* route: letters, digits, . _ - and / between parts.</summary>
        public static bool IsSafeRelative(string rel)
        {
            if (string.IsNullOrEmpty(rel) || !Regex.IsMatch(rel, @"^[a-zA-Z0-9._-]+(/[a-zA-Z0-9._-]+)*$")) return false;
            foreach (string seg in rel.Split('/')) if (seg == "." || seg == "..") return false;
            return true;
        }

        /// <summary>
        /// Links to try for a vehicle, best first: the catalog's link, the same file through the server route (works when
        /// the signed link expired), then files named after the vehicle (for catalog entries without a thumbnail link).
        /// </summary>
        public static List<string> Candidates(string catalogUrl, string vehicleId, string addressableKey, string thumbnailBaseUrl)
        {
            var list = new List<string>();
            string baseUrl = (thumbnailBaseUrl ?? "").TrimEnd('/') + "/";
            if (!string.IsNullOrEmpty(catalogUrl))
            {
                if (catalogUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) list.Add(catalogUrl);
                else if (IsSafeRelative(catalogUrl.TrimStart('/'))) list.Add(baseUrl + catalogUrl.TrimStart('/'));
                string rel = RelativeThumbnailPath(catalogUrl);
                if (rel != null) Add(list, baseUrl + rel);
            }
            if (list.Count == 0)
            {
                foreach (string name in new[] { vehicleId, addressableKey })
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    foreach (string ext in new[] { ".png", ".jpg" })
                        if (IsSafeRelative(name + ext)) Add(list, baseUrl + name + ext);
                }
            }
            return list;
        }

        private static void Add(List<string> list, string url)
        {
            foreach (string s in list) if (string.Equals(s, url, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(url);
        }

        private void WriteMeta(Entry e)
        {
            var sb = new StringBuilder();
            sb.Append("version=").Append(Clean(e.version)).Append('\n');
            sb.Append("etag=").Append(Clean(e.etag)).Append('\n');
            sb.Append("lastModified=").Append(Clean(e.lastModified)).Append('\n');
            sb.Append("source=").Append(Clean(e.source)).Append('\n');
            sb.Append("saved=").Append(e.savedUtcTicks).Append('\n');
            Directory.CreateDirectory(Folder);
            WriteAtomic(MetaPath(e.vehicleId), Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static string Clean(string s) { return (s ?? "").Replace("\r", "").Replace("\n", ""); }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static bool TryDelete(string f)
        {
            try { if (File.Exists(f)) { File.Delete(f); return true; } } catch (Exception) { }
            return false;
        }
    }
}
