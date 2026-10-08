using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Where vehicle downloads are stored, and changing it.
    ///
    /// By default Unity keeps downloads in %USERPROFILE%\AppData\LocalLow\Unity\&lt;company&gt;_&lt;product&gt; on drive C:.
    /// The user can choose another folder (another drive, say) in the Storage screen. New downloads go there at once;
    /// vehicles already downloaded stay usable where they are (every folder ever used is still read), and can be moved
    /// to the new folder with "Move downloads here". The choice is remembered across starts.
    /// </summary>
    public static class DownloadLocation
    {
        private const string FolderKey = "das.downloadFolder";
        private const string PreviousKey = "das.downloadFolder.previous";
        private const char Sep = '|';

        /// <summary>Set when the chosen folder couldn't be used at start (drive missing, no permission); shown to the user once.</summary>
        public static string StartupProblem { get; private set; }

        /// <summary>Unity's own download folder (on C:).</summary>
        public static string DefaultFolder
        {
            get { try { Cache c = Caching.defaultCache; return c.valid ? c.path : null; } catch (Exception) { return null; } }
        }

        /// <summary>The folder new downloads go to.</summary>
        public static string CurrentFolder { get { return DiskSpace.DownloadFolder; } }

        /// <summary>True when the user chose a folder.</summary>
        public static bool IsCustom { get { return !string.IsNullOrEmpty(PlayerPrefs.GetString(FolderKey, "")); } }

        // Runs before the first scene, so Addressables never starts a download into the wrong folder
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySavedChoice()
        {
            // Folders used before stay readable, so their vehicles still open
            foreach (string old in Previous())
                if (Directory.Exists(old)) TryGetOrAddCache(old);

            string chosen = PlayerPrefs.GetString(FolderKey, "");
            if (string.IsNullOrEmpty(chosen)) return;
            string error = Activate(chosen);
            if (error != null)
            {
                StartupProblem = "Your download folder (" + chosen + ") can't be used right now: " + error + " New downloads go to the default folder until it is available again.";
                Debug.LogWarning("[DownloadLocation] " + StartupProblem);
            }
        }

        /// <summary>
        /// Use <paramref name="folder"/> for new downloads, now and at every start. Null when it worked, otherwise why not.
        /// Pass null or the default folder to go back to the default.
        /// </summary>
        public static string SetFolder(string folder)
        {
            string current = CurrentFolder;
            if (string.IsNullOrEmpty(folder) || SamePath(folder, DefaultFolder))
            {
                string def = DefaultFolder;
                if (def == null) return "The default folder isn't available.";
                string err = Activate(def);
                if (err != null) return err;
                Remember(current);
                PlayerPrefs.DeleteKey(FolderKey);
                PlayerPrefs.Save();
                return null;
            }

            string full;
            try { full = Path.GetFullPath(folder.Trim()); } catch (Exception) { return "That isn't a valid folder path."; }
            string problem = CheckWritable(full);
            if (problem != null) return problem;
            string error = Activate(full);
            if (error != null) return error;
            Remember(current);
            PlayerPrefs.SetString(FolderKey, full);
            PlayerPrefs.Save();
            return null;
        }

        /// <summary>Null if downloads can be written to <paramref name="folder"/>, else why not.</summary>
        public static string CheckWritable(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string probe = Path.Combine(folder, ".das_write_test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch (UnauthorizedAccessException) { return "You don't have permission to write to that folder."; }
            catch (Exception e) { return "That folder can't be used (" + e.Message + ")."; }
            long free = DiskSpace.FreeBytes(folder);
            if (free >= 0 && free < DiskSpace.KeepFreeBytes)
                return "That drive has only " + ByteFormat.Format(free) + " free.";
            return null;
        }

        private static string Activate(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                Cache cache = TryGetOrAddCache(folder);
                if (!cache.valid) return "Unity could not open it as a download folder.";
                Caching.currentCacheForWriting = cache;
                // read from it first too
                if (Caching.cacheCount > 1 && !SamePath(Caching.GetCacheAt(0).path, cache.path))
                    Caching.MoveCacheBefore(cache, Caching.GetCacheAt(0));
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        private static Cache TryGetOrAddCache(string folder)
        {
            try
            {
                Cache c = Caching.GetCacheByPath(folder);
                if (c.valid) return c;
                return Caching.AddCache(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DownloadLocation] Can't use " + folder + ": " + e.Message);
                return default(Cache);
            }
        }

        private static IEnumerable<string> Previous()
        {
            return PlayerPrefs.GetString(PreviousKey, "").Split(Sep).Where(p => !string.IsNullOrEmpty(p));
        }

        private static void Remember(string folder)
        {
            if (string.IsNullOrEmpty(folder) || SamePath(folder, DefaultFolder)) return;
            var list = Previous().Where(p => !SamePath(p, folder)).ToList();
            list.Add(folder);
            PlayerPrefs.SetString(PreviousKey, string.Join(Sep.ToString(), list));
        }

        private static void Forget(string folder)
        {
            PlayerPrefs.SetString(PreviousKey, string.Join(Sep.ToString(), Previous().Where(p => !SamePath(p, folder))));
            PlayerPrefs.Save();
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // ── Moving existing downloads ───────────────────────────────────────

        /// <summary>Bytes of vehicle files in folders other than the current one (what "Move downloads here" would move).</summary>
        public static long BytesElsewhere()
        {
            long sum = 0;
            foreach (string f in OtherFolders()) sum += FolderBytes(f);
            return sum;
        }

        private static List<string> OtherFolders()
        {
            var list = new List<string>();
            string current = CurrentFolder;
            try
            {
                for (int i = 0; i < Caching.cacheCount; i++)
                {
                    Cache c = Caching.GetCacheAt(i);
                    if (c.valid && !SamePath(c.path, current) && Directory.Exists(c.path)) list.Add(c.path);
                }
            }
            catch (Exception) { }
            return list;
        }

        private static long FolderBytes(string folder)
        {
            try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Copy every downloaded vehicle file from the other download folders into the current one, then delete the
        /// originals. Copying runs on a worker thread; <paramref name="progress"/> gets 0..1 and a status line.
        /// Nothing is deleted unless every file copied and checked out. Restart DAS afterwards (Unity reads the moved
        /// files from the new folder at the next start).
        /// </summary>
        public static IEnumerator MoveDownloadsHere(Action<float, string> progress, Action<bool, string> done)
        {
            string target = CurrentFolder;
            List<string> sources = OtherFolders();
            if (target == null) { done(false, "No download folder is set."); yield break; }
            if (sources.Count == 0) { done(true, "Everything is already in this folder."); yield break; }

            long total = sources.Sum(FolderBytes);
            if (total == 0) { done(true, "There was nothing to move."); yield break; }
            long free = DiskSpace.FreeBytes(target);
            if (free >= 0 && free - total < DiskSpace.KeepFreeBytes)
            {
                done(false, "Not enough free space on " + DiskSpace.DriveName(target) + ": moving needs " + ByteFormat.Format(total) + ", " + ByteFormat.Format(free) + " is free.");
                yield break;
            }

            long copied = 0;
            string failure = null;
            object gate = new object();
            Task work = Task.Run(() =>
            {
                foreach (string src in sources)
                {
                    foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(src.Length).TrimStart('\\', '/');
                        string dest = Path.Combine(target, rel);
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            long len = new FileInfo(file).Length;
                            if (!File.Exists(dest) || new FileInfo(dest).Length != len) File.Copy(file, dest, true);
                            if (new FileInfo(dest).Length != len) throw new IOException("copy of " + rel + " is incomplete");
                            lock (gate) copied += len;
                        }
                        catch (Exception e) { lock (gate) failure = e.Message; return; }
                    }
                }
            });

            while (!work.IsCompleted)
            {
                long now; lock (gate) now = copied;
                progress((float)now / total, "Moving downloads... " + ByteFormat.Format(now) + " of " + ByteFormat.Format(total));
                yield return null;
            }

            if (failure != null || work.IsFaulted)
            {
                done(false, "Moving stopped: " + (failure ?? work.Exception?.GetBaseException().Message) + ". Nothing was deleted; the vehicles still work from where they were.");
                yield break;
            }

            // all copied: delete the originals (on a worker thread too)
            progress(1f, "Removing the old copies...");
            Task cleanup = Task.Run(() =>
            {
                foreach (string src in sources)
                    foreach (string dir in Directory.GetDirectories(src))
                        try { Directory.Delete(dir, true); } catch (Exception) { }
            });
            while (!cleanup.IsCompleted) yield return null;
            foreach (string src in sources) Forget(src);
            done(true, "Moved " + ByteFormat.Format(total) + " to " + target + ". Restart DAS to finish.");
        }
    }

    /// <summary>Windows "Browse for folder" dialog (no third-party plugin).</summary>
    public static class WindowsFolderPicker
    {
        /// <summary>The folder the user picked, or null if cancelled or not available on this platform.</summary>
        public static string Pick(string title)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var info = new BROWSEINFO
            {
                hwndOwner = GetActiveWindow(),
                lpszTitle = title,
                ulFlags = 0x0001 | 0x0040 | 0x0010,      // file-system folders only, new dialog style, edit box
                pszDisplayName = new string('\0', 260),
            };
            IntPtr pidl = IntPtr.Zero;
            try
            {
                pidl = SHBrowseForFolder(ref info);
                if (pidl == IntPtr.Zero) return null;
                var path = new System.Text.StringBuilder(1024);
                return SHGetPathFromIDList(pidl, path) ? path.ToString() : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FolderPicker] " + e.Message);
                return null;
            }
            finally
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }
#else
            return null;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public string pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool SHGetPathFromIDList(IntPtr pidl, System.Text.StringBuilder pszPath);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
#endif
    }
}
