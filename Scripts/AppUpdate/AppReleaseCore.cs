using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// No UnityEngine here: this file is shared by the DAS app, the Unity editor (publishing) and DASUpdater.exe
// (Scripts/AppUpdate/Updater~), and is tested outside Unity (Scripts/AppUpdate/Tests~).
namespace VehicleMeasurement.AppUpdate
{
    /// <summary>One file of a DAS build: its path inside the DAS folder ("/" separators), SHA-256 (hex) and size.</summary>
    [Serializable]
    public class ReleaseFile
    {
        public string path;
        public string sha256;
        public long size;

        public ReleaseFile() { }
        public ReleaseFile(string path, string sha256, long size) { this.path = path; this.sha256 = sha256; this.size = size; }
    }

    /// <summary>Versions like 1.4.0 (major.minor.patch).</summary>
    public static class AppVersion
    {
        public static bool TryParse(string v, out int[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(v)) return false;
            string[] s = v.Trim().Split('.');
            if (s.Length != 3) return false;
            var p = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (s[i].Length == 0 || s[i].Length > 4 || (s[i].Length > 1 && s[i][0] == '0')) return false;
                if (!int.TryParse(s[i], NumberStyles.None, CultureInfo.InvariantCulture, out p[i])) return false;
            }
            parts = p;
            return true;
        }

        public static bool IsValid(string v) { int[] p; return TryParse(v, out p); }

        /// <summary>-1 / 0 / 1. An invalid version sorts below every valid one.</summary>
        public static int Compare(string a, string b)
        {
            int[] x, y;
            bool okA = TryParse(a, out x), okB = TryParse(b, out y);
            if (!okA && !okB) return 0;
            if (!okA) return -1;
            if (!okB) return 1;
            for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i] < y[i] ? -1 : 1;
            return 0;
        }

        /// <summary>1.4.0 -> 1.4.1 (an invalid version gives 1.0.0).</summary>
        public static string NextPatch(string v)
        {
            int[] p;
            if (!TryParse(v, out p)) return "1.0.0";
            return p[0] + "." + p[1] + "." + (p[2] + 1);
        }
    }

    /// <summary>
    /// The list of files of a release, the text that is signed (the same in VRSP's utils/dasAppRelease.js), and the
    /// file that records what is installed (das-release.txt in the DAS folder).
    /// </summary>
    public static class Manifest
    {
        public const string Header = "DAS-APP-RELEASE v1";
        /// <summary>Written into the DAS folder after each install/update: the version and files that are there.</summary>
        public const string InstalledFile = "das-release.txt";

        /// <summary>Not part of a release: Unity's debug folders, and the record of what is installed.</summary>
        public static bool IsExcluded(string relPath)
        {
            string p = relPath.Replace('\\', '/');
            string first = p.Split('/')[0];
            if (first.EndsWith("_BurstDebugInformation_DoNotShip", StringComparison.OrdinalIgnoreCase)) return true;
            if (first.EndsWith("_BackUpThisFolder_ButDontShipItWithYourGame", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(p, InstalledFile, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<ReleaseFile> Sorted(IEnumerable<ReleaseFile> files)
        {
            var list = files.ToList();
            list.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
            return list;
        }

        /// <summary>The exact text that is signed.</summary>
        public static string Canonical(string version, IEnumerable<ReleaseFile> files)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n').Append("version=").Append(version).Append('\n');
            foreach (ReleaseFile f in Sorted(files))
                sb.Append(f.sha256).Append(' ').Append(f.size.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(f.path).Append('\n');
            return sb.ToString();
        }

        /// <summary>Read text written by <see cref="Canonical"/>. Returns false if it isn't one.</summary>
        public static bool TryParse(string text, out string version, out List<ReleaseFile> files)
        {
            version = null;
            files = new List<ReleaseFile>();
            if (string.IsNullOrEmpty(text)) return false;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length < 2 || lines[0] != Header || !lines[1].StartsWith("version=", StringComparison.Ordinal)) return false;
            version = lines[1].Substring(8);
            for (int i = 2; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Length == 0) continue;
                int a = l.IndexOf(' '), b = a < 0 ? -1 : l.IndexOf(' ', a + 1);
                long size;
                if (a != 64 || b < 0 || !long.TryParse(l.Substring(a + 1, b - a - 1), NumberStyles.None, CultureInfo.InvariantCulture, out size)) return false;
                files.Add(new ReleaseFile(l.Substring(b + 1), l.Substring(0, 64), size));
            }
            return true;
        }

        public static string HashFile(string fullPath)
        {
            using (var sha = SHA256.Create())
            using (var s = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20))
                return ToHex(sha.ComputeHash(s));
        }

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte x in bytes) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Every file of a build folder (for publishing), with its checksum. Excluded folders are left out.</summary>
        public static List<ReleaseFile> FromFolder(string root, Action<int, int> progress = null)
        {
            root = Path.GetFullPath(root);
            var all = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => new { full = f, rel = Rel(root, f) })
                .Where(x => !IsExcluded(x.rel)).ToList();
            var list = new List<ReleaseFile>();
            for (int i = 0; i < all.Count; i++)
            {
                if (progress != null) progress(i, all.Count);
                list.Add(new ReleaseFile(all[i].rel, HashFile(all[i].full), new FileInfo(all[i].full).Length));
            }
            return Sorted(list);
        }

        public static string Rel(string root, string full)
        {
            string r = full.Substring(root.Length).TrimStart('\\', '/');
            return r.Replace('\\', '/');
        }
    }

    /// <summary>RSA (PKCS#1 v1.5, SHA-256) signatures of a release, with keys in .NET XML form.</summary>
    public static class ReleaseSignature
    {
        public static string Sign(string privateKeyXml, string version, IEnumerable<ReleaseFile> files)
        {
            byte[] data = Encoding.UTF8.GetBytes(Manifest.Canonical(version, files));
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(privateKeyXml);
                return Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }

        public static bool Verify(string publicKeyXml, string version, IEnumerable<ReleaseFile> files, string signatureB64)
        {
            if (string.IsNullOrEmpty(publicKeyXml) || string.IsNullOrEmpty(signatureB64)) return false;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(Manifest.Canonical(version, files));
                using (var rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(data, Convert.FromBase64String(signatureB64), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>A new key pair: (private XML, public XML).</summary>
        public static KeyValuePair<string, string> NewKeyPair(int bits = 3072)
        {
            using (var rsa = RSA.Create())
            {
                rsa.KeySize = bits;
                return new KeyValuePair<string, string>(rsa.ToXmlString(true), rsa.ToXmlString(false));
            }
        }
    }

    /// <summary>
    /// What an update has to do in a DAS folder: which files to write (only those that differ from what is there) and
    /// which to remove (files of the installed version that the new one no longer has - never anything else).
    /// </summary>
    public class UpdatePlan
    {
        public const string Header = "DAS-UPDATE-PLAN v1";

        public string version;            // the version being installed
        public string fromVersion;        // the version on the PC now
        public string installDir;         // the DAS folder
        public string stagingDir;         // downloaded files: <stagingDir>/blobs/<sha256>
        public string exe;                // the program to start afterwards, e.g. DAS.exe
        public int pid;                   // the running DAS, to wait for
        public string resultFile;         // where the updater writes how it went (DAS reads it at the next start)
        public List<ReleaseFile> manifest = new List<ReleaseFile>();   // the whole new version
        public List<ReleaseFile> writes = new List<ReleaseFile>();     // files to put in place
        public List<string> deletes = new List<string>();              // files to remove

        public long DownloadBytes { get { return writes.GroupBy(w => w.sha256).Sum(g => g.First().size); } }

        public string BlobPath(string sha256) { return Path.Combine(Path.Combine(stagingDir, "blobs"), sha256); }

        /// <summary>
        /// Compare the new version with the DAS folder. <paramref name="installed"/> is what das-release.txt says is
        /// there (null if there is no record, e.g. the first build shared by hand: then nothing is removed).
        /// A file is written only if it is missing or its size or checksum differs.
        /// </summary>
        public static UpdatePlan Compute(string installDir, string version, List<ReleaseFile> release, List<ReleaseFile> installed, Func<string, string> hash = null)
        {
            if (hash == null) hash = Manifest.HashFile;
            var plan = new UpdatePlan { installDir = installDir, version = version, manifest = Manifest.Sorted(release) };
            foreach (ReleaseFile f in plan.manifest)
            {
                string full = Path.Combine(installDir, f.path.Replace('/', Path.DirectorySeparatorChar));
                bool same = false;
                if (File.Exists(full) && new FileInfo(full).Length == f.size)
                {
                    try { same = string.Equals(hash(full), f.sha256, StringComparison.OrdinalIgnoreCase); } catch (Exception) { same = false; }
                }
                if (!same) plan.writes.Add(f);
            }
            if (installed != null)
            {
                var keep = new HashSet<string>(release.Select(f => f.path), StringComparer.OrdinalIgnoreCase);
                foreach (ReleaseFile f in installed)
                    if (!keep.Contains(f.path) && !Manifest.IsExcluded(f.path) && File.Exists(Path.Combine(installDir, f.path.Replace('/', Path.DirectorySeparatorChar))))
                        plan.deletes.Add(f.path);
            }
            return plan;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            Line(sb, "version", version); Line(sb, "from", fromVersion); Line(sb, "install", installDir); Line(sb, "staging", stagingDir);
            Line(sb, "exe", exe); Line(sb, "pid", pid.ToString(CultureInfo.InvariantCulture)); Line(sb, "result", resultFile);
            foreach (ReleaseFile f in manifest) sb.Append("file ").Append(f.sha256).Append(' ').Append(f.size.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(f.path).Append('\n');
            foreach (ReleaseFile f in writes) sb.Append("write ").Append(f.sha256).Append(' ').Append(f.size.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(f.path).Append('\n');
            foreach (string d in deletes) sb.Append("delete ").Append(d).Append('\n');
            return sb.ToString();
        }

        private static void Line(StringBuilder sb, string k, string v) { sb.Append(k).Append('=').Append((v ?? "").Replace("\n", " ")).Append('\n'); }

        public static UpdatePlan Parse(string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0] != Header) throw new FormatException("Not an update plan.");
            var p = new UpdatePlan();
            for (int i = 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Length == 0) continue;
                if (l.StartsWith("file ", StringComparison.Ordinal)) p.manifest.Add(ParseFile(l.Substring(5)));
                else if (l.StartsWith("write ", StringComparison.Ordinal)) p.writes.Add(ParseFile(l.Substring(6)));
                else if (l.StartsWith("delete ", StringComparison.Ordinal)) p.deletes.Add(l.Substring(7));
                else
                {
                    int eq = l.IndexOf('=');
                    if (eq < 0) throw new FormatException("Bad plan line: " + l);
                    string k = l.Substring(0, eq), v = l.Substring(eq + 1);
                    switch (k)
                    {
                        case "version": p.version = v; break;
                        case "from": p.fromVersion = v; break;
                        case "install": p.installDir = v; break;
                        case "staging": p.stagingDir = v; break;
                        case "exe": p.exe = v; break;
                        case "pid": int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out p.pid); break;
                        case "result": p.resultFile = v; break;
                    }
                }
            }
            return p;
        }

        private static ReleaseFile ParseFile(string s)
        {
            int a = s.IndexOf(' '), b = a < 0 ? -1 : s.IndexOf(' ', a + 1);
            if (a != 64 || b < 0) throw new FormatException("Bad file line: " + s);
            return new ReleaseFile(s.Substring(b + 1), s.Substring(0, 64), long.Parse(s.Substring(a + 1, b - a - 1), CultureInfo.InvariantCulture));
        }

        /// <summary>A path from the server is used only if it stays inside the DAS folder.</summary>
        public static bool IsSafeRelative(string rel)
        {
            if (string.IsNullOrEmpty(rel) || rel.Length > 260 || rel.StartsWith("/") || rel.Contains("\\") || rel.Contains(":")) return false;
            foreach (string part in rel.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Trim() != part) return false;
            return true;
        }
    }

    /// <summary>How the last update went: written by DASUpdater.exe, shown by DAS at the next start.</summary>
    public static class UpdateResult
    {
        public static void Write(string file, bool ok, string version, string fromVersion, string message)
        {
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, (ok ? "ok" : "failed") + "\nversion=" + version + "\nfrom=" + fromVersion + "\nat=" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\nmessage=" + (message ?? "").Replace("\n", " ") + "\n");
            }
            catch (Exception) { }
        }

        /// <summary>(ok, version, from, message) or null.</summary>
        public static string[] Read(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                string[] l = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n');
                Func<string, string> get = k => { var x = l.FirstOrDefault(s => s.StartsWith(k + "=", StringComparison.Ordinal)); return x == null ? "" : x.Substring(k.Length + 1); };
                return new[] { l[0], get("version"), get("from"), get("message") };
            }
            catch (Exception) { return null; }
        }
    }
}
