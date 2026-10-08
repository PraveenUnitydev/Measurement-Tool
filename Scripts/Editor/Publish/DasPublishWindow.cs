using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace VehicleMeasurement.EditorTools.Publish
{
    /// <summary>
    /// DAS > Publish Vehicle - engineers publish their own vehicles to every DAS PC:
    ///   1. Sign in (Microsoft account; you must be a DAS publisher for the vehicle)
    ///   2. Pick the vehicle (yours, or a new one if you may add vehicles) and its prefab
    ///   3. Fill in the details, frame the vehicle in the Scene view and capture the thumbnail
    ///   4. Checks run (materials, size, missing scripts); fix errors
    ///   5. Publish: builds only this vehicle, uploads it to its own version folder, and makes it live
    /// DAS users get the "update available" notice; the previous version is kept and an admin can switch back.
    /// Materials and look are set up by hand in the prefab, as before.
    /// </summary>
    public class DasPublishWindow : EditorWindow
    {
        [MenuItem("DAS/Publish Vehicle...", false, 0)]
        public static void ShowWindow()
        {
            var w = GetWindow<DasPublishWindow>("Publish Vehicle");
            w.minSize = new Vector2(520, 640);
            w.Show();
        }

        private readonly DasPublishClient _client = new DasPublishClient();
        private DasPublishClient.Me _me;
        private DasPublishClient.StartSignIn _signIn;
        private string _signInStatus = "";
        private double _nextPoll;

        private int _vehicleIndex;
        private bool _newVehicle;
        private string _newId = "";
        private GameObject _prefab;
        private string _name = "", _maker = "", _year = "", _category = "", _description = "", _notes = "";
        private byte[] _thumbBytes;
        private Texture2D _thumbPreview;
        private List<DasVehicleChecks.Finding> _findings = new List<DasVehicleChecks.Finding>();

        private bool _busy;
        private string _step = "";
        private float _progress;
        private readonly List<string> _log = new List<string>();
        private Vector2 _scroll, _logScroll;
        private DasPublishClient.History _history;
        private bool _showHistory;

        private void OnEnable()
        {
            if (_client.IsSignedIn) _ = LoadMe();
        }

        private void OnDisable()
        {
            if (_thumbPreview != null) DestroyImmediate(_thumbPreview);
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

        // ── UI ───────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Publish a vehicle to DAS", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Builds only this vehicle and publishes it to every DAS PC. Use the shared DAS project (same Unity, " +
                                    "Addressables, URP, scripts and shaders as the app). Set materials and check the look in the prefab first.", MessageType.None);

            DrawSignIn();
            if (_client.IsSignedIn && _me != null && string.IsNullOrEmpty(_me.error))
            {
                using (new EditorGUI.DisabledScope(_busy))
                {
                    DrawVehicle();
                    DrawDetails();
                    DrawThumbnail();
                    DrawChecks();
                }
                DrawPublish();
                DrawHistory();
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
                if (GUILayout.Button("Refresh", GUILayout.Width(80))) _ = LoadMe();
                if (GUILayout.Button("Sign out", GUILayout.Width(80))) { _client.SignOut(); _me = null; }
                EditorGUILayout.EndHorizontal();
                if (_me != null && !string.IsNullOrEmpty(_me.error)) EditorGUILayout.HelpBox(_me.error, MessageType.Error);
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

        private void DrawVehicle()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. Vehicle", EditorStyles.boldLabel);
            var names = _me.vehicles.Select(v => v.vehicleName + "  (" + v.vehicleId + ")" + (string.IsNullOrEmpty(v.version) ? "" : "  - live: " + v.version)).ToList();
            if (_me.canCreate) names.Add("+ New vehicle...");
            if (names.Count == 0) { EditorGUILayout.HelpBox("No vehicles are assigned to you. Ask a VRSP admin to add you as a publisher for your vehicles.", MessageType.Warning); return; }

            int idx = _newVehicle ? names.Count - 1 : Mathf.Clamp(_vehicleIndex, 0, Math.Max(0, _me.vehicles.Count - 1));
            int picked = EditorGUILayout.Popup("Vehicle", idx, names.ToArray());
            if (picked != idx || (!_newVehicle && picked != _vehicleIndex))
            {
                _newVehicle = _me.canCreate && picked == names.Count - 1;
                if (!_newVehicle) { _vehicleIndex = picked; FillFrom(_me.vehicles[picked]); }
                _history = null;
            }
            if (_newVehicle)
                _newId = EditorGUILayout.TextField(new GUIContent("New vehicle id", "lower case letters, digits, - or _ (e.g. xuv700). Can't be changed later."), _newId).Trim().ToLowerInvariant();

            var prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);
            if (prefab != _prefab)
            {
                _prefab = prefab;
                _findings = DasVehicleChecks.Run(_prefab);
                var data = _prefab != null ? _prefab.GetComponentInChildren<VehiclePrefabData>(true) : null;
                if (data != null)
                {
                    if (string.IsNullOrEmpty(_name)) _name = data.vehicleName ?? "";
                    if (string.IsNullOrEmpty(_maker)) _maker = data.manufacturer ?? "";
                    if (string.IsNullOrEmpty(_year)) _year = data.modelYear ?? "";
                    if (string.IsNullOrEmpty(_category)) _category = data.category ?? "";
                }
            }
        }

        private void FillFrom(DasPublishClient.MyVehicle v)
        {
            _name = v.vehicleName ?? ""; _maker = v.manufacturer ?? ""; _year = v.modelYear ?? ""; _category = v.category ?? "";
        }

        private void DrawDetails()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3. Details", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField("Name", _name);
            _maker = EditorGUILayout.TextField("Manufacturer", _maker);
            _year = EditorGUILayout.TextField("Model year", _year);
            _category = EditorGUILayout.TextField("Category", _category);
            _description = EditorGUILayout.TextField("Description", _description);
            EditorGUILayout.LabelField("What changed in this version");
            _notes = EditorGUILayout.TextArea(_notes, GUILayout.MinHeight(44));
        }

        private void DrawThumbnail()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("4. Thumbnail", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Capture Scene view", "Frame the vehicle in the Scene view (3/4 front view, plain background), then click"))) SetThumb(DasVehicleChecks.CaptureSceneView(out var e1), e1);
            if (GUILayout.Button("Choose picture..."))
            {
                string p = EditorUtility.OpenFilePanel("Thumbnail picture", "", "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(p)) SetThumb(DasVehicleChecks.LoadImageFile(p, out var e2), e2);
            }
            if (GUILayout.Button("Keep current", GUILayout.Width(100))) SetThumb(null, null);
            EditorGUILayout.EndHorizontal();
            if (_thumbPreview != null)
            {
                Rect r = GUILayoutUtility.GetRect(320, 180, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, _thumbPreview, null, ScaleMode.ScaleToFit);
            }
            else EditorGUILayout.HelpBox(_newVehicle ? "A new vehicle needs a thumbnail." : "No new thumbnail: the current one stays.", _newVehicle ? MessageType.Warning : MessageType.None);
        }

        private void SetThumb(byte[] bytes, string error)
        {
            if (!string.IsNullOrEmpty(error)) { Log("Thumbnail: " + error); return; }
            _thumbBytes = bytes;
            if (_thumbPreview != null) DestroyImmediate(_thumbPreview);
            _thumbPreview = null;
            if (bytes != null) { _thumbPreview = new Texture2D(2, 2); _thumbPreview.LoadImage(bytes); }
        }

        private void DrawChecks()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("5. Checks", EditorStyles.boldLabel);
            if (GUILayout.Button("Check again", GUILayout.Width(100))) _findings = DasVehicleChecks.Run(_prefab);
            EditorGUILayout.EndHorizontal();
            foreach (var f in _findings)
                EditorGUILayout.HelpBox(f.text, f.level == DasVehicleChecks.Level.Error ? MessageType.Error : f.level == DasVehicleChecks.Level.Warning ? MessageType.Warning : MessageType.Info);
        }

        private void DrawPublish()
        {
            EditorGUILayout.Space(10);
            string problem = WhyNot();
            if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.None);
            using (new EditorGUI.DisabledScope(_busy || problem != null))
            {
                var style = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                if (GUILayout.Button(_busy ? "Publishing..." : "Publish " + VehicleId(), style, GUILayout.Height(36))) _ = Publish();
            }
            if (_busy || !string.IsNullOrEmpty(_step))
            {
                Rect r = GUILayoutUtility.GetRect(18, 18, "TextField");
                EditorGUI.ProgressBar(r, _progress, _step);
            }
        }

        private void DrawHistory()
        {
            if (_newVehicle || _me == null || _me.vehicles.Count == 0) return;
            EditorGUILayout.Space(8);
            bool show = EditorGUILayout.Foldout(_showHistory, "Published versions", true);
            if (show && !_showHistory && _history == null) _ = LoadHistory();
            _showHistory = show;
            if (!_showHistory || _history == null) return;
            if (!string.IsNullOrEmpty(_history.error)) { EditorGUILayout.HelpBox(_history.error, MessageType.Warning); return; }
            EditorGUILayout.LabelField("Live now: " + (_history.current ?? "-"));
            foreach (var h in _history.history)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(h.version + "   " + h.by + "   " + h.size + (string.IsNullOrEmpty(h.notes) ? "" : "   - " + h.notes));
                bool canSwitch = _me.role == "Admin" && h.version != _history.current && _history.kept.Contains(h.version);
                using (new EditorGUI.DisabledScope(!canSwitch || _busy))
                    if (GUILayout.Button(new GUIContent("Switch back", _me.role == "Admin" ? "Make this version live again" : "Only a DAS Admin can switch back"), GUILayout.Width(90)))
                        if (EditorUtility.DisplayDialog("Switch back", "Make " + h.version + " live again for " + VehicleId() + "?", "Switch back", "Cancel")) _ = SwitchBack(h.version);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawLog()
        {
            if (_log.Count == 0) return;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(110));
            EditorGUILayout.HelpBox(string.Join("\n", _log), MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // ── logic ────────────────────────────────────────────────────────

        private string VehicleId()
        {
            if (_newVehicle) return _newId;
            return _me != null && _vehicleIndex < _me.vehicles.Count ? _me.vehicles[_vehicleIndex].vehicleId : "";
        }

        private string WhyNot()
        {
            if (string.IsNullOrEmpty(VehicleId())) return "Pick the vehicle (or type the new vehicle id).";
            if (_newVehicle && !System.Text.RegularExpressions.Regex.IsMatch(_newId, "^[a-z0-9][a-z0-9_-]{1,47}$")) return "Vehicle id: 2-48 characters, lower case letters, digits, - or _.";
            if (_prefab == null) return "Pick the prefab.";
            if (_findings.Any(f => f.level == DasVehicleChecks.Level.Error)) return "Fix the errors in the checks first.";
            if (_newVehicle && _thumbBytes == null) return "Capture or choose a thumbnail.";
            if (string.IsNullOrWhiteSpace(_name)) return "Give the vehicle a name.";
            return null;
        }

        private async Task BeginSignIn()
        {
            _signInStatus = "";
            var s = await _client.BeginSignIn();
            if (s == null || !string.IsNullOrEmpty(s.error) || string.IsNullOrEmpty(s.pairingId)) { _signInStatus = s != null && s.error != null ? s.error : "Couldn't start signing in."; Repaint(); return; }
            _signIn = s;
            _nextPoll = EditorApplication.timeSinceStartup + 3;
            Repaint();
        }

        private bool _polling;
        private async Task PollSignIn()
        {
            if (_polling || _signIn == null) return;
            _polling = true;
            try
            {
                var r = await _client.Poll(_signIn);
                if (r.status == "approved") { _signIn = null; _signInStatus = ""; Log("Signed in as " + _client.SignedInAs); await LoadMe(); }
                else if (r.status == "denied" || r.status == "expired") { _signIn = null; _signInStatus = r.message ?? r.status; }
            }
            finally { _polling = false; Repaint(); }
        }

        private async Task LoadMe()
        {
            _me = await _client.GetMe();
            if (_me != null && string.IsNullOrEmpty(_me.error) && _me.vehicles.Count > 0 && !_newVehicle)
                FillFrom(_me.vehicles[Mathf.Clamp(_vehicleIndex, 0, _me.vehicles.Count - 1)]);
            if (_me == null || (!_client.IsSignedIn)) _me = null;
            Repaint();
        }

        private async Task LoadHistory()
        {
            _history = await _client.GetHistory(VehicleId());
            Repaint();
        }

        private async Task SwitchBack(string version)
        {
            _busy = true;
            try
            {
                var r = await _client.Rollback(VehicleId(), version);
                Log(r.ok ? "Switched " + VehicleId() + " back to " + version + "." : "Switch back failed: " + r.error);
                _history = null; await LoadHistory(); await LoadMe();
            }
            finally { _busy = false; Repaint(); }
        }

        private void Log(string s)
        {
            _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + s);
            if (_log.Count > 200) _log.RemoveAt(0);
            _logScroll.y = float.MaxValue;
            Debug.Log("[Publish] " + s);
        }

        private void Step(string s, float p) { _step = s; _progress = p; Repaint(); }

        private async Task Publish()
        {
            string id = VehicleId();
            if (!EditorUtility.DisplayDialog("Publish " + id,
                    "Build " + (_prefab != null ? _prefab.name : id) + " and publish it to every DAS PC?\n\nPeople get an update notice. The current version is kept and can be switched back.",
                    "Publish", "Cancel")) return;
            _busy = true;
            _findings = DasVehicleChecks.Run(_prefab);
            try
            {
                if (_findings.Any(f => f.level == DasVehicleChecks.Level.Error)) { Log("Fix the errors in the checks first."); return; }

                Step("Asking the server for a new version...", 0.02f);
                var start = await _client.Start(new DasPublishClient.StartBody
                {
                    vehicleId = id, vehicleName = _name, manufacturer = _maker, modelYear = _year,
                    category = _category, description = _description, notes = _notes,
                });
                if (!string.IsNullOrEmpty(start.error) || string.IsNullOrEmpty(start.publishId)) { Log("Couldn't start: " + (start.error ?? "no answer")); return; }
                Log("New version " + start.version + " for " + id + ".");

                Step("Building " + id + "...", 0.08f);
                await Task.Yield();
                var build = DasVehicleBuilder.Build(_prefab, id, start.addressableKey, start.loadPath, start.version);
                if (!build.ok) { Log(build.error); return; }
                Log("Built " + build.files.Count + " file(s), " + Mb(build.totalBytes) + ".");

                string thumbName = null;
                if (_thumbBytes != null)
                {
                    thumbName = "thumbnail.png";
                    File.WriteAllBytes(Path.Combine(build.outputFolder, thumbName), _thumbBytes);
                    build.files.Add(thumbName);
                }

                var entries = build.files.Select(f => new DasPublishClient.FileEntry { path = f, size = new FileInfo(Path.Combine(build.outputFolder, f)).Length }).ToList();
                Step("Preparing the upload...", 0.15f);
                var up = await _client.RequestUploads(start.publishId, entries);
                if (!string.IsNullOrEmpty(up.error) || up.uploads.Count != entries.Count) { Log("Couldn't prepare the upload: " + (up.error ?? "wrong file list")); return; }

                long total = entries.Sum(e => e.size), done = 0;
                foreach (var u in up.uploads)
                {
                    string local = Path.Combine(build.outputFolder, u.path);
                    long size = new FileInfo(local).Length;
                    long before = done;
                    string err = await DasPublishClient.UploadFile(local, u, p =>
                        Step("Uploading " + u.path + "  (" + Mb(before + (long)(size * p)) + " of " + Mb(total) + ")", 0.15f + 0.8f * (total > 0 ? (before + size * p) / total : 1f)));
                    if (err != null) { Log("Upload of " + u.path + " failed: " + err + ". Nothing was published - try again."); return; }
                    done += size;
                }
                Log("Uploaded " + Mb(total) + ".");

                Step("Making it live...", 0.97f);
                var commit = await _client.Commit(start.publishId, build.catalogFile, thumbName);
                if (!commit.ok) { Log("Couldn't make it live: " + commit.error); return; }
                Step("Live: " + id + " " + start.version, 1f);
                Log(id + " " + start.version + " is live. DAS PCs see the update when they next check (at start, or within 30 minutes on Home).");
                _notes = "";
                _history = null;
                await LoadMe();
                EditorUtility.DisplayDialog("Published", id + " " + start.version + " is live.", "OK");
            }
            catch (Exception e)
            {
                Log("Publishing failed: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                if (_progress < 1f) _step = "";
                Repaint();
            }
        }

        private static string Mb(long bytes) { return (bytes / (1024f * 1024f)).ToString("0.0") + " MB"; }
    }
}
