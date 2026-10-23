using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VehicleMeasurement.AppUpdate;
using VehicleMeasurement.EditorTools.Publish;

namespace VehicleMeasurement.EditorTools.AppUpdates
{
    /// <summary>
    /// DAS > Publish App Update: build DAS, sign it, upload only the files that changed since earlier versions, and make
    /// it available - to the pilot group first, or to everyone. DAS admins only (VRSP checks). APP-UPDATES.md.
    /// </summary>
    public class DasAppReleaseWindow : EditorWindow
    {
        [MenuItem("DAS/Publish App Update...", false, 1)]
        public static void ShowWindow()
        {
            var w = GetWindow<DasAppReleaseWindow>("Publish App Update");
            w.minSize = new Vector2(520, 600);
            w.Show();
        }

        private readonly DasPublishClient _client = new DasPublishClient();
        private DasPublishClient.StartSignIn _signIn;
        private string _signInStatus = "";
        private double _nextPoll;
        private bool _polling;
        private DasPublishClient.AppReleases _releases;

        private string _version = "";
        private string _notes = "";
        private int _rollout;                         // 0 = pilot group, 1 = everyone
        private string _outputDir = "";

        private bool _busy;
        private string _step = "";
        private float _progress;
        private readonly List<string> _log = new List<string>();
        private Vector2 _scroll, _logScroll;

        private void OnEnable()
        {
            if (_client.IsSignedIn) _ = LoadReleases();
            if (string.IsNullOrEmpty(_version)) _version = AppVersion.NextPatch(PlayerSettings.bundleVersion);
        }

        private void Update()
        {
            if (_signIn != null && EditorApplication.timeSinceStartup >= _nextPoll)
            {
                _nextPoll = EditorApplication.timeSinceStartup + Math.Max(2, _signIn.pollIntervalSeconds);
                _ = PollSignIn();
            }
            if (_busy) Repaint();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Publish a new version of the DAS app", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Builds DAS for Windows, signs it with the DAS signing key and uploads only the files that changed. " +
                                    "DAS PCs are offered the update; it's optional for them. DAS admins only.", MessageType.None);
            DrawSignIn();
            if (_client.IsSignedIn && _releases != null && string.IsNullOrEmpty(_releases.error))
            {
                using (new EditorGUI.DisabledScope(_busy))
                {
                    DrawKey();
                    DrawRelease();
                }
                DrawPublish();
                DrawReleases();
            }
            DrawLog();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSignIn()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("1. Sign in", EditorStyles.boldLabel);
            if (_client.IsSignedIn)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Signed in as " + _client.SignedInAs);
                if (GUILayout.Button("Refresh", GUILayout.Width(80))) _ = LoadReleases();
                if (GUILayout.Button("Sign out", GUILayout.Width(80))) { _client.SignOut(); _releases = null; }
                EditorGUILayout.EndHorizontal();
                if (_releases != null && !string.IsNullOrEmpty(_releases.error)) EditorGUILayout.HelpBox(_releases.error, MessageType.Error);
                return;
            }
            if (_signIn != null)
            {
                EditorGUILayout.HelpBox("Finish signing in in your browser. The browser shows this code - check it matches:\n\n" + _signIn.userCode, MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Open the sign-in page again")) Application.OpenURL(_signIn.loginUrl);
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) { _signIn = null; _signInStatus = ""; }
                EditorGUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("Sign in with Microsoft", GUILayout.Height(28))) _ = BeginSignIn();
            if (!string.IsNullOrEmpty(_signInStatus)) EditorGUILayout.HelpBox(_signInStatus, MessageType.Warning);
            DasPublishClient.ServerBase = EditorGUILayout.TextField(new GUIContent("Server", "DAS API on VRSP"), DasPublishClient.ServerBase);
        }

        private void DrawKey()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. Signing key", EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml))
            {
                EditorGUILayout.HelpBox("DAS has no signing key yet. Create one once; it is built into every DAS from then on.", MessageType.Warning);
                if (GUILayout.Button("Create Signing Key...")) DasAppSigningKey.Create();
                return;
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Private key file", "das-app-signing-key.xml - kept outside the project"), GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(DasAppSigningKey.PrivateKeyPath) ? "(not chosen)" : DasAppSigningKey.PrivateKeyPath, GUILayout.Height(18));
            if (GUILayout.Button("Choose...", GUILayout.Width(80)))
            {
                string f = EditorUtility.OpenFilePanel("The DAS app signing key", Path.GetDirectoryName(DasAppSigningKey.PrivateKeyPath) ?? "", "xml");
                if (!string.IsNullOrEmpty(f)) DasAppSigningKey.PrivateKeyPath = f;
            }
            EditorGUILayout.EndHorizontal();
            string err;
            if (DasAppSigningKey.LoadPrivateKey(out err) == null) EditorGUILayout.HelpBox(err, MessageType.Warning);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Code signing", "Windows signature on the DAS exe and DASUpdater.exe (company certificate)"), GUILayout.Width(110));
            EditorGUILayout.LabelField(DasCodeSigning.Describe(), EditorStyles.miniLabel);
            if (GUILayout.Button("Set up...", GUILayout.Width(80))) DasCodeSigningWindow.ShowWindow();
            EditorGUILayout.EndHorizontal();
            if (!DasCodeSigning.IsConfigured)
                EditorGUILayout.HelpBox("Builds aren't code signed, so security software (SentinelOne) may block DASUpdater.exe on the PCs. Get the company code-signing certificate from IT.", MessageType.Warning);
            if (_releases != null && !_releases.signingKeySet)
                EditorGUILayout.HelpBox("VRSP doesn't have the public key yet: add the line from DAS > App Updates > Copy VRSP Key Line to VRSP's backend/.env and restart VRSP.", MessageType.Warning);
        }

        private void DrawRelease()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3. The new version", EditorStyles.boldLabel);
            string latest = Latest();
            _version = EditorGUILayout.TextField(new GUIContent("Version", "Like 1.4.0. Must be higher than every version published before."), _version).Trim();
            EditorGUILayout.LabelField(" ", "Now in Player Settings: " + PlayerSettings.bundleVersion + (latest != null ? "   ·   newest published: " + latest : ""), EditorStyles.miniLabel);
            string vErr = VersionProblem();
            if (vErr != null) EditorGUILayout.HelpBox(vErr, MessageType.Warning);
            EditorGUILayout.LabelField("What's new (shown to people when they update)");
            _notes = EditorGUILayout.TextArea(_notes, GUILayout.MinHeight(70));
            _rollout = GUILayout.Toolbar(_rollout, new[] { "Pilot group first", "Everyone" });
            EditorGUILayout.LabelField(" ", _rollout == 0 ? "Only people in the pilot group (VRSP > DAS tab) are offered it. Make it available to everyone there later." : "Every DAS PC is offered it.", EditorStyles.miniLabel);
            if (string.IsNullOrEmpty(_outputDir)) _outputDir = DefaultOutput();
            EditorGUILayout.BeginHorizontal();
            _outputDir = EditorGUILayout.TextField(new GUIContent("Build folder", "Also a full copy to share by hand with a new PC"), _outputDir);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string d = EditorUtility.SaveFolderPanel("Build folder", Path.GetDirectoryName(_outputDir), "");
                if (!string.IsNullOrEmpty(d)) _outputDir = d;
            }
            EditorGUILayout.EndHorizontal();
        }

        private string DefaultOutput()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "DAS-" + (AppVersion.IsValid(_version) ? _version : "next")));
        }

        private string Latest()
        {
            if (_releases == null || _releases.releases.Count == 0) return null;
            return _releases.releases.Select(r => r.version).OrderByDescending(v => v, Comparer<string>.Create(AppVersion.Compare)).First();
        }

        private string VersionProblem()
        {
            if (!AppVersion.IsValid(_version)) return "Version must look like 1.4.0.";
            string latest = Latest();
            if (latest != null && AppVersion.Compare(_version, latest) <= 0) return "Version must be higher than " + latest + ".";
            return null;
        }

        private void DrawPublish()
        {
            EditorGUILayout.Space(10);
            string err;
            bool ready = VersionProblem() == null && DasAppSigningKey.LoadPrivateKey(out err) != null && _releases != null && _releases.signingKeySet;
            using (new EditorGUI.DisabledScope(_busy || !ready))
            {
                var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                if (GUILayout.Button(_busy ? "Publishing..." : "Build and publish DAS " + _version, style, GUILayout.Height(36))) _ = Publish();
            }
            if (_busy)
            {
                Rect r = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(r, _progress, _step);
            }
        }

        private void DrawReleases()
        {
            if (_releases == null || _releases.releases.Count == 0) return;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Published versions (change who gets them in VRSP > DAS tab > App releases)", EditorStyles.boldLabel);
            foreach (var r in _releases.releases.Take(10))
                EditorGUILayout.LabelField(r.version + "   " + r.state + "   " + Mb(r.totalBytes) + "   " + r.publishedBy + "   " + (r.committedAt ?? ""), EditorStyles.miniLabel);
        }

        private void DrawLog()
        {
            if (_log.Count == 0) return;
            EditorGUILayout.Space(8);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(140));
            foreach (string l in _log) EditorGUILayout.LabelField(l, EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Log(string s) { _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + s); Debug.Log("[DAS App Updates] " + s); Repaint(); }

        // ── sign-in ──────────────────────────────────────────────────────
        private async Task BeginSignIn()
        {
            _signInStatus = "";
            var s = await _client.BeginSignIn();
            if (s == null || !string.IsNullOrEmpty(s.error) || string.IsNullOrEmpty(s.pairingId)) { _signInStatus = s != null && s.error != null ? s.error : "Couldn't start signing in."; Repaint(); return; }
            _signIn = s;
            _nextPoll = EditorApplication.timeSinceStartup + 3;
            Repaint();
        }

        private async Task PollSignIn()
        {
            if (_polling || _signIn == null) return;
            _polling = true;
            try
            {
                var r = await _client.Poll(_signIn);
                if (r.status == "approved") { _signIn = null; _signInStatus = ""; Log("Signed in as " + _client.SignedInAs); await LoadReleases(); }
                else if (r.status == "denied" || r.status == "expired") { _signIn = null; _signInStatus = r.message ?? r.status; }
            }
            finally { _polling = false; Repaint(); }
        }

        private async Task LoadReleases()
        {
            _releases = await _client.GetAppReleases();
            if (_releases != null && _releases.code == "DAS_NOT_ADMIN") _releases.error = "Only DAS admins can publish app updates. Ask a VRSP admin to make you a DAS Admin.";
            if (!_client.IsSignedIn) _releases = null;
            if (_releases != null && string.IsNullOrEmpty(_releases.error))
            {
                string latest = Latest();
                if (latest != null && AppVersion.Compare(_version, latest) <= 0) { _version = AppVersion.NextPatch(latest); _outputDir = ""; }
            }
            Repaint();
        }

        // ── publish ──────────────────────────────────────────────────────
        private async Task Publish()
        {
            string keyError;
            string privateKey = DasAppSigningKey.LoadPrivateKey(out keyError);
            if (privateKey == null) { EditorUtility.DisplayDialog("Signing key", keyError, "OK"); return; }
            string version = _version;
            string rollout = _rollout == 1 ? "live" : "pilot";
            if (!EditorUtility.DisplayDialog("Publish DAS " + version,
                    "Build DAS " + version + " and offer it to " + (rollout == "live" ? "EVERY DAS PC" : "the pilot group") + "?\n\nPlayer Settings > Version becomes " + version + ".", "Build and publish", "Cancel")) return;

            _busy = true; _log.Clear();
            try
            {
                // 1. build
                _step = "Building DAS " + version + "..."; _progress = 0.05f; Log(_step);
                PlayerSettings.bundleVersion = version;
                AssetDatabase.SaveAssets();
                string dir = _outputDir;
                if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    if (!EditorUtility.DisplayDialog("Build folder not empty", dir + "\n\nEmpty it and build there?", "Empty it", "Cancel")) { Log("Cancelled."); return; }
                    Directory.Delete(dir, true);
                }
                Directory.CreateDirectory(dir);
                string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) throw new Exception("No scenes are ticked in File > Build Settings.");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    locationPathName = Path.Combine(dir, PlayerSettings.productName + ".exe"),
                    options = BuildOptions.None,
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("The build failed (" + report.summary.result + ", " + report.summary.totalErrors + " errors). See the Console.");
                Log("Built: " + Mb((long)report.summary.totalSize));

                // 2. list + sign
                _step = "Checking files..."; _progress = 0.35f;
                List<ReleaseFile> files = Manifest.FromFolder(dir, (i, n) => { _progress = 0.35f + 0.1f * i / Math.Max(1, n); });
                if (!files.Any(f => string.Equals(f.path, "DASUpdater.exe", StringComparison.OrdinalIgnoreCase))) throw new Exception("DASUpdater.exe is missing from the build.");
                string signature = ReleaseSignature.Sign(privateKey, version, files);
                if (!ReleaseSignature.Verify(DasAppUpdateKey.PublicKeyXml, version, files, signature)) throw new Exception("The signature doesn't verify with DAS's key.");
                Log(files.Count + " files, " + Mb(files.Sum(f => f.size)) + ", signed.");

                // 3. start: the server says which files it doesn't have yet
                _step = "Contacting VRSP..."; _progress = 0.45f;
                var start = await _client.StartAppRelease(new DasPublishClient.AppStartBody { version = version, notes = _notes, rollout = rollout, signature = signature, files = files });
                if (!string.IsNullOrEmpty(start.error)) throw new Exception(start.error);
                Log(start.uploads.Count + " changed files to upload (" + start.alreadyThere + " already on the server).");

                // 4. upload
                var bySha = files.GroupBy(f => f.sha256).ToDictionary(g => g.Key, g => g.First());
                long total = start.uploads.Sum(u => bySha[u.sha256].size), sent = 0;
                foreach (var u in start.uploads)
                {
                    ReleaseFile f = bySha[u.sha256];
                    _step = "Uploading " + f.path;
                    long before = sent;
                    string err = await DasPublishClient.UploadFile(Path.Combine(dir, f.path.Replace('/', Path.DirectorySeparatorChar)),
                        new DasPublishClient.Upload { path = f.path, url = u.url, contentType = u.contentType },
                        p => { _progress = 0.5f + 0.45f * (before + p * f.size) / Math.Max(1, total); });
                    if (err != null) throw new Exception("Upload of " + f.path + " failed: " + err + ". Publish again - files already sent aren't sent again.");
                    sent += f.size;
                }

                // 5. commit
                _step = "Finishing..."; _progress = 0.97f;
                var done = await _client.CommitAppRelease(version);
                if (!done.ok) throw new Exception(done.error ?? "VRSP didn't accept the release.");
                Log("DAS " + version + " is published (" + (done.state == "live" ? "everyone" : "pilot group") + ").");
                EditorUtility.DisplayDialog("Published", "DAS " + version + " is out to " + (done.state == "live" ? "every DAS PC" : "the pilot group") +
                    ". PCs are offered it at their next check (on start, or within a few hours).\n\nA full copy is in:\n" + dir + "\nShare that folder for a new PC.", "OK");
                await LoadReleases();
            }
            catch (Exception e)
            {
                Log("Stopped: " + e.Message);
                EditorUtility.DisplayDialog("Not published", e.Message, "OK");
            }
            finally { _busy = false; _step = ""; Repaint(); }
        }

        private static string Mb(long b) { return (b / 1048576.0).ToString("0.0") + " MB"; }
    }
}
