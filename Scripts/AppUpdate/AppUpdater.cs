using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace VehicleMeasurement.AppUpdate
{
    /// <summary>
    /// Keeps DAS itself up to date (APP-UPDATES.md). Runs by itself in the Windows build; nothing to set up in a scene.
    ///  * After sign-in, and every <see cref="CheckHours"/> hours, asks VRSP whether a newer DAS is out.
    ///  * Offers it with a notification ("DAS 1.4.0 is available" - Update / later). Updates are optional.
    ///  * Update: checks the release's signature against the key built into DAS, works out which files differ from the
    ///    DAS folder, downloads only those (each checked against its checksum), then asks to restart. DASUpdater.exe
    ///    puts the files in place while DAS is closed, and starts DAS again. "Later" installs it when DAS is closed.
    ///  * At the next start: "DAS was updated to 1.4.0" - or, if it failed, why (DAS stays on the old version).
    /// Ctrl+Shift+U checks now.
    /// </summary>
    public class AppUpdater : MonoBehaviour
    {
        public static float CheckHours = 3f;
        public static float FirstCheckSeconds = 20f;

        private static AppUpdater _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (_instance != null) return;
            var go = new GameObject("DAS AppUpdater");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AppUpdater>();
#endif
        }

        // ── where things are ─────────────────────────────────────────────
        public static string InstallDir { get { return Directory.GetParent(Application.dataPath).FullName; } }
        /// <summary>The program: Unity's data folder is "&lt;program&gt;_Data".</summary>
        public static string ExeName
        {
            get
            {
                string data = Path.GetFileName(Application.dataPath);
                return (data.EndsWith("_Data", StringComparison.OrdinalIgnoreCase) ? data.Substring(0, data.Length - 5) : Application.productName) + ".exe";
            }
        }
        private static string Root { get { return Path.Combine(Application.persistentDataPath, "AppUpdate"); } }
        private static string ResultFile { get { return Path.Combine(Root, "last-result.txt"); } }
        public static string InstalledVersion { get { return Application.version; } }

        // ── server replies (JsonUtility) ─────────────────────────────────
        [Serializable] private class ReleaseDto { public string version, notes, exe, signature, committedAt; public long totalBytes; public List<ReleaseFile> files = new List<ReleaseFile>(); }
        [Serializable] private class LatestDto { public string kind, installed, error; public ReleaseDto release; }
        [Serializable] private class LinksBody { public string version; public List<string> files = new List<string>(); }

        private enum Stage { Idle, Checking, Offered, Preparing, Downloading, Ready, Installing }
        private Stage _stage = Stage.Idle;
        private ReleaseDto _offer;
        private string _offerKind;
        private UpdatePlan _plan;
        private string _planFile;
        private bool _installOnQuit;
        private float _nextCheck;
        private readonly HashSet<string> _toldThisRun = new HashSet<string>();
        private DasDialog _progress;
        private bool _cancel;

        private void Start()
        {
            _nextCheck = Time.unscaledTime + FirstCheckSeconds;
            try { ReportLastUpdate(); } catch (Exception e) { Debug.LogWarning("[AppUpdate] " + e.Message); }
            try { CleanOldDownloads(); } catch (Exception) { }
            Application.quitting += OnQuitting;
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml)) Debug.Log("[AppUpdate] No signing key in this build: app updates are off.");
        }

        private void OnDestroy() { Application.quitting -= OnQuitting; if (_instance == this) _instance = null; }

        private void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift && Input.GetKeyDown(KeyCode.U)) CheckNow();

            if (_stage == Stage.Idle && Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + CheckHours * 3600f;
                if (!string.IsNullOrEmpty(Token) && !string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml)) StartCoroutine(Check(false));
                else _nextCheck = Time.unscaledTime + 60f;                           // not signed in yet: look again in a minute
            }
        }

        private static string Token { get { return PlayerPrefs.GetString("session.token", ""); } }

        /// <summary>Check now and say the result (Ctrl+Shift+U).</summary>
        public static void CheckNow()
        {
            if (_instance == null) { DasToast.Show("App updates", "Updates work in the installed DAS (not in the Unity editor).", DasToast.Tone.Info); return; }
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml)) { DasToast.Show("App updates are off", "This DAS build has no update key.", DasToast.Tone.Warn, key: "app-update"); return; }
            if (string.IsNullOrEmpty(Token)) { DasToast.Show("Sign in first", "Sign in to DAS to check for updates.", DasToast.Tone.Info, key: "app-update"); return; }
            if (_instance._stage == Stage.Offered && _instance._offer != null) { _instance.Offer(true); return; }
            if (_instance._stage != Stage.Idle) { DasToast.Show("Update in progress", "DAS is already getting an update.", DasToast.Tone.Info, key: "app-update"); return; }
            _instance.StartCoroutine(_instance.Check(true));
        }

        // ── 1. is there a newer version? ─────────────────────────────────
        private IEnumerator Check(bool userAsked)
        {
            _stage = Stage.Checking;
            LatestDto latest = null;
            string problem = null;
            yield return DasHttp.Send(() =>
            {
                var r = UnityWebRequest.Get(DasServer.ApiBase + "/app/latest?version=" + UnityWebRequest.EscapeURL(InstalledVersion));
                r.SetRequestHeader("Authorization", "Bearer " + Token);
                return r;
            }, req =>
            {
                using (req)
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        try { latest = JsonUtility.FromJson<LatestDto>(req.downloadHandler.text); } catch (Exception e) { problem = "Unreadable reply: " + e.Message; }
                    }
                    else problem = req.responseCode == 401 ? "Sign in to DAS again." : (req.error ?? ("HTTP " + req.responseCode));
                }
            });
            _stage = Stage.Idle;

            if (latest == null || problem != null)
            {
                Debug.Log("[AppUpdate] Check failed: " + problem);
                if (userAsked) DasToast.Show("Couldn't check for updates", problem ?? "No reply from the server.", DasToast.Tone.Warn, key: "app-update");
                yield break;
            }
            if (latest.kind != "update" && latest.kind != "rollback" || latest.release == null)
            {
                if (userAsked) DasToast.Show("DAS is up to date", "You have the latest version (" + InstalledVersion + ").", DasToast.Tone.Good, key: "app-update");
                yield break;
            }
            // Only a release signed with the DAS key is ever offered
            if (!ReleaseSignature.Verify(DasAppUpdateKey.PublicKeyXml, latest.release.version, latest.release.files, latest.release.signature))
            {
                Debug.LogError("[AppUpdate] DAS " + latest.release.version + " is not signed with this build's key: ignored.");
                if (userAsked) DasToast.Show("Update can't be trusted", "The update on the server isn't signed with the DAS key, so it wasn't installed. Tell your DAS admin.", DasToast.Tone.Error, key: "app-update");
                yield break;
            }
            _offer = latest.release;
            _offerKind = latest.kind;
            _stage = Stage.Offered;
            Offer(userAsked || _toldThisRun.Add(_offer.version));
        }

        private void Offer(bool show)
        {
            if (!show || _offer == null) return;
            string first = FirstLine(_offer.notes);
            if (_offerKind == "rollback")
                DasToast.Show("Please switch to DAS " + _offer.version, "The version you have (" + InstalledVersion + ") was taken back by the DAS admin." + (first.Length > 0 ? " " + first : ""),
                    DasToast.Tone.Warn, "Switch", AskToUpdate, "app-update", 60f);
            else
                DasToast.Show("DAS " + _offer.version + " is available", first.Length > 0 ? first : "A new version of DAS is ready to install.",
                    DasToast.Tone.Info, "Update", AskToUpdate, "app-update", 30f);
        }

        private void AskToUpdate()
        {
            if (_offer == null || _stage != Stage.Offered) return;
            if (!CanWrite(InstallDir))
            {
                DasDialog.Info("DAS can't update itself here",
                    "DAS is in a folder it isn't allowed to change:\n" + InstallDir + "\n\nMove the DAS folder to one you can change (for example D:\\DAS), start it from there, and update again.");
                return;
            }
            string notes = string.IsNullOrEmpty(_offer.notes) ? "" : "\n\n" + Truncate(_offer.notes, 900);
            DasDialog.Confirm((_offerKind == "rollback" ? "Switch to DAS " : "Update to DAS ") + _offer.version + "?",
                "You have " + InstalledVersion + "." + notes + "\n\nOnly the changed files are downloaded. DAS restarts to finish.",
                "Download", "Not now", () => StartCoroutine(Prepare()));
        }

        // ── 2. which files, download, check ──────────────────────────────
        private IEnumerator Prepare()
        {
            ReleaseDto rel = _offer;
            _stage = Stage.Preparing;
            _cancel = false;
            _progress = DasDialog.Progress("Getting DAS " + rel.version, "Comparing with the DAS on this PC...", "Cancel", () => _cancel = true);

            // What is installed (das-release.txt), and which files differ - checksums on a background thread
            List<ReleaseFile> installed = null;
            string iv; List<ReleaseFile> ifiles;
            string record = Path.Combine(InstallDir, Manifest.InstalledFile);
            if (File.Exists(record) && Manifest.TryParse(File.ReadAllText(record), out iv, out ifiles)) installed = ifiles;
            UpdatePlan plan = null; Exception planError = null;
            string installDir = InstallDir;
            var t = new Thread(() => { try { plan = UpdatePlan.Compute(installDir, rel.version, rel.files, installed); } catch (Exception e) { planError = e; } }) { IsBackground = true };
            t.Start();
            while (t.IsAlive) { if (_cancel) break; yield return null; }
            if (_cancel) { Abort(null); yield break; }
            if (planError != null) { Abort("Couldn't compare with the DAS folder: " + planError.Message); yield break; }
            foreach (ReleaseFile w in plan.writes) if (!UpdatePlan.IsSafeRelative(w.path)) { Abort("The update has a file name DAS won't accept."); yield break; }

            plan.fromVersion = InstalledVersion;
            plan.stagingDir = Path.Combine(Root, rel.version);
            plan.exe = string.IsNullOrEmpty(rel.exe) ? ExeName : rel.exe;
            plan.resultFile = ResultFile;
            Directory.CreateDirectory(Path.Combine(plan.stagingDir, "blobs"));

            // Already downloaded earlier (an interrupted update) and still intact: keep
            var need = plan.writes.GroupBy(w => w.sha256).Select(g => g.First()).Where(w => !BlobOk(plan, w)).ToList();
            long bytes = need.Sum(w => w.size);
            string space = SpaceProblem(plan.stagingDir, bytes * 2 + 50L * 1024 * 1024);
            if (space != null) { Abort(space); yield break; }
            Debug.Log("[AppUpdate] DAS " + rel.version + ": " + plan.writes.Count + " files differ, " + need.Count + " to download (" + bytes + " bytes), " + plan.deletes.Count + " to remove");

            _stage = Stage.Downloading;
            long done = 0;
            for (int i = 0; i < need.Count; i += 400)
            {
                var batch = need.Skip(i).Take(400).ToList();
                Dictionary<string, string> links = null; string linkError = null;
                yield return Links(rel.version, batch.Select(b => b.sha256).ToList(), (l, e) => { links = l; linkError = e; });
                if (linkError != null) { Abort(linkError); yield break; }
                foreach (ReleaseFile f in batch)
                {
                    if (_cancel) { Abort(null); yield break; }
                    string url;
                    if (!links.TryGetValue(f.sha256, out url)) { Abort("The server didn't give a link for " + f.path + "."); yield break; }
                    string err = null;
                    long before = done;
                    yield return Download(url, plan.BlobPath(f.sha256), f, p => SetProgress(rel, before + (long)(p * f.size), bytes), e => err = e);
                    if (err != null) { Abort("Couldn't download " + f.path + ": " + err); yield break; }
                    done += f.size;
                    SetProgress(rel, done, bytes);
                }
            }

            // The updater runs from the update's own folder, so the one in the DAS folder can be replaced too
            ReleaseFile newUpdater = rel.files.FirstOrDefault(f => string.Equals(f.path, "DASUpdater.exe", StringComparison.OrdinalIgnoreCase));
            string updaterCopy = Path.Combine(plan.stagingDir, "DASUpdater.exe");
            try
            {
                if (newUpdater != null && File.Exists(plan.BlobPath(newUpdater.sha256))) File.Copy(plan.BlobPath(newUpdater.sha256), updaterCopy, true);
                else File.Copy(Path.Combine(InstallDir, "DASUpdater.exe"), updaterCopy, true);
            }
            catch (Exception e) { Abort("DASUpdater.exe is missing from the DAS folder (" + e.Message + "). Ask your DAS admin for a fresh copy of DAS."); yield break; }

            plan.pid = Process.GetCurrentProcess().Id;
            _plan = plan;
            _planFile = Path.Combine(plan.stagingDir, "plan.txt");
            File.WriteAllText(_planFile, plan.Serialize());
            if (_progress != null) _progress.Close();
            _stage = Stage.Ready;
            DasToast.Dismiss("app-update");
            DasDialog.Confirm("DAS " + rel.version + " is ready",
                "Restart DAS now to finish the update? It takes a few seconds.\n\nIf you choose Later, it is installed when you close DAS.",
                "Restart now", "Later", RestartNow, () =>
                {
                    _installOnQuit = true;
                    DasToast.Show("DAS " + rel.version + " installs when you close DAS", "Or restart now to get it straight away.", DasToast.Tone.Info, "Restart now", RestartNow, "app-update", 20f);
                });
        }

        private void SetProgress(ReleaseDto rel, long done, long total)
        {
            if (_progress == null) return;
            _progress.SetMessage("Downloading DAS " + rel.version + " (only the changed files)...");
            _progress.SetProgress(total > 0 ? (float)done / total : 1f, MB(done) + " of " + MB(total));
        }

        private void Abort(string message)
        {
            if (_progress != null) _progress.Close();
            _progress = null;
            _stage = _offer != null ? Stage.Offered : Stage.Idle;
            if (message != null)
            {
                Debug.LogWarning("[AppUpdate] " + message);
                DasToast.Show("Update stopped", message, DasToast.Tone.Warn, "Try again", AskToUpdate, "app-update", 30f);
            }
        }

        private IEnumerator Links(string version, List<string> shas, Action<Dictionary<string, string>, string> done)
        {
            Dictionary<string, string> links = null; string error = null;
            string body = JsonUtility.ToJson(new LinksBody { version = version, files = shas });
            yield return DasHttp.Send(() =>
            {
                var r = new UnityWebRequest(DasServer.ApiBase + "/app/download-links", "POST");
                r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                r.downloadHandler = new DownloadHandlerBuffer();
                r.SetRequestHeader("Content-Type", "application/json");
                r.SetRequestHeader("Authorization", "Bearer " + Token);
                r.timeout = 60;
                return r;
            }, req =>
            {
                using (req)
                {
                    if (req.result != UnityWebRequest.Result.Success) { error = req.responseCode == 401 ? "Sign in to DAS again." : "The server refused the download (" + (req.error ?? "HTTP " + req.responseCode) + ")."; return; }
                    links = ParseLinks(req.downloadHandler.text);
                    if (links == null) error = "Unreadable reply from the server.";
                }
            });
            done(links, error);
        }

        /// <summary>{"links":{"<sha>":"<url>",…}} - a map, which JsonUtility can't read, so by hand.</summary>
        public static Dictionary<string, string> ParseLinks(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var map = new Dictionary<string, string>();
            int i = json.IndexOf("\"links\"", StringComparison.Ordinal);
            if (i < 0) return null;
            var m = System.Text.RegularExpressions.Regex.Matches(json.Substring(i), "\"([a-f0-9]{64})\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (System.Text.RegularExpressions.Match x in m) map[x.Groups[1].Value] = System.Text.RegularExpressions.Regex.Unescape(x.Groups[2].Value);
            return map;
        }

        private IEnumerator Download(string url, string target, ReleaseFile f, Action<float> progress, Action<string> failed)
        {
            string part = target + ".part";
            string last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using (var req = new UnityWebRequest(url, "GET"))
                {
                    req.downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true };
                    req.timeout = 0;
                    var op = req.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (_cancel) { req.Abort(); failed("cancelled"); yield break; }
                        progress(req.downloadProgress);
                        yield return null;
                    }
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        // The checksum, off the main thread
                        string sha = null; Exception hashError = null;
                        var t = new Thread(() => { try { sha = Manifest.HashFile(part); } catch (Exception e) { hashError = e; } }) { IsBackground = true };
                        t.Start();
                        while (t.IsAlive) yield return null;
                        if (hashError == null && string.Equals(sha, f.sha256, StringComparison.OrdinalIgnoreCase) && new FileInfo(part).Length == f.size)
                        {
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(part, target);
                            yield break;
                        }
                        last = "the file arrived damaged";
                    }
                    else last = req.error ?? ("HTTP " + req.responseCode);
                }
                try { if (File.Exists(part)) File.Delete(part); } catch (Exception) { }
                yield return new WaitForSecondsRealtime(attempt * 2f);
            }
            failed(last);
        }

        private static bool BlobOk(UpdatePlan plan, ReleaseFile f)
        {
            string p = plan.BlobPath(f.sha256);
            try { return File.Exists(p) && new FileInfo(p).Length == f.size && string.Equals(Manifest.HashFile(p), f.sha256, StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // ── 3. install ───────────────────────────────────────────────────
        private void RestartNow()
        {
            if (_stage != Stage.Ready || _plan == null) return;
            if (LaunchUpdater(false)) { _stage = Stage.Installing; Application.Quit(); }
        }

        private void OnQuitting()
        {
            // "Later": put the update in place once DAS is closed, without opening it again
            if (_installOnQuit && _stage == Stage.Ready && _plan != null) LaunchUpdater(true);
        }

        private bool LaunchUpdater(bool noRelaunch)
        {
            try
            {
                string exe = Path.Combine(_plan.stagingDir, "DASUpdater.exe");
                var psi = new ProcessStartInfo(exe, "--plan \"" + _planFile + "\"" + (noRelaunch ? " --no-relaunch" : "")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _plan.stagingDir };
                Process.Start(psi);
                Debug.Log("[AppUpdate] Started the updater for DAS " + _plan.version);
                _installOnQuit = false;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[AppUpdate] Couldn't start the updater: " + e.Message);
                DasToast.Show("Couldn't start the update", e.Message, DasToast.Tone.Error, key: "app-update");
                return false;
            }
        }

        // ── at start: how the last update went; tidy up ──────────────────
        private static void ReportLastUpdate()
        {
            string[] r = UpdateResult.Read(ResultFile);
            if (r == null) return;
            try { File.Delete(ResultFile); } catch (Exception) { }
            if (r[0] == "ok")
                DasToast.Show("DAS was updated to " + r[1], AppVersion.Compare(r[1], InstalledVersion) == 0 ? "You're on the latest version." : "", DasToast.Tone.Good, key: "app-update", seconds: 12f);
            else
                DasToast.Show("DAS couldn't be updated to " + r[1], (string.IsNullOrEmpty(r[3]) ? "It stays on " + InstalledVersion + "." : r[3]), DasToast.Tone.Warn, "Try again", CheckNow, "app-update", 30f);
        }

        /// <summary>Keep only the newest update folder (its backup holds the previous version's changed files).</summary>
        private static void CleanOldDownloads()
        {
            if (!Directory.Exists(Root)) return;
            var dirs = Directory.GetDirectories(Root).Select(d => new { d, v = Path.GetFileName(d) }).Where(x => AppVersion.IsValid(x.v))
                .OrderByDescending(x => x.v, Comparer<string>.Create(AppVersion.Compare)).ToList();
            foreach (var x in dirs.Skip(1)) try { Directory.Delete(x.d, true); } catch (Exception) { }
        }

        // ── helpers ──────────────────────────────────────────────────────
        public static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".das-write-test-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static string SpaceProblem(string dir, long need)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)));
                if (drive.AvailableFreeSpace < need) return "Not enough disk space for the update: " + MB(need) + " needed, " + MB(drive.AvailableFreeSpace) + " free on " + drive.Name + ".";
            }
            catch (Exception) { }
            return null;
        }

        private static string MB(long b) { return (b / 1048576.0).ToString(b < 10 * 1048576 ? "0.0" : "0") + " MB"; }
        private static string FirstLine(string s) { if (string.IsNullOrEmpty(s)) return ""; string l = s.Replace("\r", "").Split(new[] { '\n' })[0].Trim(); return Truncate(l, 140); }
        private static string Truncate(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
    }
}
