using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// Tells the user when vehicles were added or updated on the server:
    ///  * after the server list loads (and again every <see cref="RecheckMinutes"/> minutes while Home is open), it
    ///    compares the list with what this PC last showed: new vehicles, newer versions of vehicles on this PC
    ///    ("update needed"), and newer versions of vehicles not downloaded;
    ///  * on Home a notice slides in - "2 new vehicles · 3 updates for vehicles on this PC (1.2 GB)" - with
    ///    "View" (opens Download &amp; update on the right tab, where everything can be updated at once) and "Later".
    /// Ctrl+Shift+D opens Download &amp; update from any screen. Runs by itself; no scene setup needed.
    /// </summary>
    public class UpdateNotifier : MonoBehaviour
    {
        /// <summary>How often the server is asked whether vehicles changed (cheap when nothing did). Any screen.</summary>
        public static float RecheckMinutes = 5f;

        private static UpdateNotifier _instance;
        private static CatalogSeen _seen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("DAS UpdateNotifier");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<UpdateNotifier>();
        }

        // ── shared data for the notice and the Download & update screen ──

        private static CatalogSeen Seen
        {
            get
            {
                if (_seen == null) _seen = new CatalogSeen(Path.Combine(Application.persistentDataPath, "DAS", "catalog_seen.txt"));
                return _seen;
            }
        }

        public static List<CatalogEntry> CurrentCatalog()
        {
            var list = new List<CatalogEntry>();
            var loader = RemoteAddressableVehicleLoader.Instance;
            if (loader == null || !loader.IsCatalogLoaded) return list;
            foreach (RemoteVehicleInfo v in loader.GetAvailableVehicles())
                if (v != null && !string.IsNullOrEmpty(v.vehicleId))
                    list.Add(new CatalogEntry { vehicleId = v.vehicleId, name = v.vehicleName, manufacturer = v.manufacturer, addressableKey = v.addressableKey, version = v.version });
            return list;
        }

        private static FileState FilesFor(CatalogEntry c)
        {
            var service = VehicleStorageService.Instance;
            if (service == null || !service.IsReady) return null;
            VehicleStorageState s = null;
            try { s = service.GetState(c.vehicleId, c.addressableKey); } catch (Exception) { }
            if (s == null) return null;
            return new FileState
            {
                downloaded = s.IsDownloaded, needsUpdate = s.NeedsUpdate,
                downloadBytes = s.downloadBytes, totalBytes = s.totalBytes, label = s.label, tone = s.tone,
            };
        }

        /// <summary>The server's vehicles with their state on this PC and new/changed flags.</summary>
        public static List<LibraryItem> BuildItems(bool establishBaseline)
        {
            List<CatalogEntry> catalog = CurrentCatalog();
            var items = VehicleLibrary.Build(catalog, FilesFor, Seen);
            if (establishBaseline && !Seen.HasBaseline && catalog.Count > 0) Seen.MarkSeen(catalog);   // first run: nothing is "new"
            return items;
        }

        /// <summary>The user looked at the full list: what is on the server now is no longer news.</summary>
        public static void MarkSeenNow()
        {
            List<CatalogEntry> catalog = CurrentCatalog();
            if (catalog.Count > 0) Seen.MarkSeen(catalog);
            if (_instance != null) _instance.HideNotice();
        }

        public static void MarkVehicleSeen(LibraryItem i)
        {
            if (i == null) return;
            Seen.MarkSeen(new CatalogEntry { vehicleId = i.vehicleId, version = i.version });
        }

        // ── the notice ───────────────────────────────────────────────────

        private RemoteAddressableVehicleLoader _hooked;
        private bool _evaluatePending;
        private float _nextEvaluate;
        private float _nextRecheck;
        private string _dismissedText = "";
        private GameObject _notice;
        private TextMeshProUGUI _noticeText;
        private LibraryFilter _noticeFilter = LibraryFilter.Updates;
        private string _pendingText;

        private void Start()
        {
            VehicleStorageService.Changed += OnSomethingChanged;
            BatchDownloads.JobFinished += OnJobFinished;
            _nextRecheck = Time.unscaledTime + RecheckMinutes * 60f;
        }

        private void OnDestroy()
        {
            VehicleStorageService.Changed -= OnSomethingChanged;
            BatchDownloads.JobFinished -= OnJobFinished;
            if (_hooked != null && _hooked.OnCatalogLoaded != null) _hooked.OnCatalogLoaded.RemoveListener(OnCatalogLoaded);
            if (_instance == this) _instance = null;
        }

        private void OnCatalogLoaded(int count) { _evaluatePending = true; _nextEvaluate = Time.unscaledTime + 1f; }
        private void OnSomethingChanged() { _evaluatePending = true; if (_nextEvaluate < Time.unscaledTime + 0.5f) _nextEvaluate = Time.unscaledTime + 0.5f; }

        private void Update()
        {
            // Ctrl+Shift+D: Download & update
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift && Input.GetKeyDown(KeyCode.D)) VehicleLibraryPanel.Open();

            var loader = RemoteAddressableVehicleLoader.Instance;
            if (loader != _hooked)
            {
                if (_hooked != null && _hooked.OnCatalogLoaded != null) _hooked.OnCatalogLoaded.RemoveListener(OnCatalogLoaded);
                _hooked = loader;
                if (_hooked != null && _hooked.OnCatalogLoaded != null) _hooked.OnCatalogLoaded.AddListener(OnCatalogLoaded);
                if (_hooked != null && _hooked.IsCatalogLoaded) OnCatalogLoaded(0);
            }

            if (_evaluatePending && Time.unscaledTime >= _nextEvaluate) Evaluate();

            // A notice found while another screen was open is shown when Home is back
            if (_pendingText != null && OnHome()) { ShowNotice(_pendingText); _pendingText = null; }

            // Ask the server now and then (any screen) whether vehicles changed; only a changed list is loaded
            // (not while a vehicle is open or downloading: the check runs as soon as that ends)
            if (Time.unscaledTime >= _nextRecheck && loader != null && loader.IsCatalogLoaded && !loader.IsLoading
                && !loader.HasVehicleOpen && !BatchDownloads.Running && !_checking)
            {
                _nextRecheck = Time.unscaledTime + RecheckMinutes * 60f;
                StartCoroutine(Check(loader, false));
            }
        }

        // ── checking the server (automatic, or "Check for updates") ──────

        private bool _checking;
        public static bool IsChecking { get { return _instance != null && _instance._checking; } }

        /// <summary>Ask the server now (the Vehicle Library's "Check for updates"). Tells the user the result.</summary>
        public static void CheckNow()
        {
            var loader = RemoteAddressableVehicleLoader.Instance;
            if (_instance == null || loader == null) { DasToast.Show("Can't check right now", "The vehicle list isn't loaded yet.", DasToast.Tone.Warn); return; }
            if (_instance._checking) return;
            _instance.StartCoroutine(_instance.Check(loader, true));
        }

        private IEnumerator Check(RemoteAddressableVehicleLoader loader, bool userAsked)
        {
            _checking = true;
            bool changed = false, reachable = false;
            try { yield return loader.CheckForUpdates((c, r) => { changed = c; reachable = r; }); }
            finally { _checking = false; }
            _nextRecheck = Time.unscaledTime + RecheckMinutes * 60f;
            if (!userAsked) yield break;
            if (!reachable) DasToast.Show("Couldn't reach the server", "Check the connection and try again.", DasToast.Tone.Warn, key: "check");
            else if (!changed) DasToast.Show("Vehicles are up to date", "Nothing new on the server.", DasToast.Tone.Good, key: "check");
            else DasToast.Show("Vehicle list updated", "The latest list from the server is loaded.", DasToast.Tone.Good, key: "check");
            // new versions and new vehicles get their own notifications (Evaluate -> AnnounceChanges)
        }

        // ── notifications ────────────────────────────────────────────────

        // What was already announced this session (vehicle|version), so each change is told once
        private readonly HashSet<string> _announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _baselineTaken;
        private readonly List<string> _readyThisRun = new List<string>();

        private void AnnounceChanges(List<LibraryItem> items)
        {
            var fresh = new List<LibraryItem>();
            foreach (LibraryItem i in items)
            {
                if (i == null || i.kind != LibraryKind.Server) continue;
                bool news = (i.known && i.needsUpdate) || i.isNew;
                if (!news) continue;
                if (_announced.Add(i.vehicleId + "|" + i.version) && _baselineTaken) fresh.Add(i);
            }
            _baselineTaken = true;                    // at start the notice covers it; later changes get a notification
            if (fresh.Count == 0) return;

            var updates = fresh.Where(i => i.needsUpdate).ToList();
            var added = fresh.Where(i => !i.needsUpdate).ToList();
            if (updates.Count == 1)
            {
                LibraryItem u = updates[0];
                DasToast.Show("Update available: " + u.name, "A newer version is on the server" + (u.downloadBytes > 0 ? " (" + ByteFormat.Format(u.downloadBytes) + ")." : "."),
                    DasToast.Tone.Info, "Update now", () =>
                    {
                        string space = BatchDownloads.SpaceProblem(u.downloadBytes);
                        if (space != null) { DasToast.Show("Not enough disk space", space, DasToast.Tone.Warn); return; }
                        BatchDownloads.Enqueue(new[] { u });
                        MarkVehicleSeen(u);
                        DasToast.Show("Updating " + u.name, "It downloads in the background. You'll be told when it's ready.", DasToast.Tone.Info, key: "upd:" + u.vehicleId);
                    }, "upd:" + u.vehicleId, 20f);
            }
            else if (updates.Count > 1)
                DasToast.Show(updates.Count + " vehicle updates available", string.Join(", ", updates.Take(4).Select(i => i.name)) + (updates.Count > 4 ? "..." : ""),
                    DasToast.Tone.Info, "View", () => VehicleLibraryPanel.Open(LibraryFilter.Updates), "updates", 20f);
            if (added.Count == 1)
                DasToast.Show("New vehicle: " + added[0].name, "Now available to download.", DasToast.Tone.Info, "View", () => VehicleLibraryPanel.Open(LibraryFilter.New), "new:" + added[0].vehicleId, 15f);
            else if (added.Count > 1)
                DasToast.Show(added.Count + " new vehicles", string.Join(", ", added.Take(4).Select(i => i.name)) + (added.Count > 4 ? "..." : ""),
                    DasToast.Tone.Info, "View", () => VehicleLibraryPanel.Open(LibraryFilter.New), "new", 15f);
        }

        private void OnJobFinished(BatchDownloads.Job job)
        {
            if (job == null) return;
            if (job.state == BatchDownloads.JobState.Done)
            {
                VehicleLibraryPanel.MarkReady(job.vehicleId);
                _readyThisRun.Add(job.vehicleId);
            }
            else if (job.state == BatchDownloads.JobState.Failed)
                DasToast.Show("Couldn't download " + job.name, string.IsNullOrEmpty(job.message) ? "The download stopped." : job.message,
                    DasToast.Tone.Error, "Retry", () => BatchDownloads.Retry(job.vehicleId), "fail:" + job.vehicleId, 20f);

            if (BatchDownloads.WaitingCount > 0)
            {
                // more to come: one progress notification instead of one per vehicle
                if (_readyThisRun.Count > 0)
                    DasToast.Show("Downloading vehicles", _readyThisRun.Count + " ready, " + BatchDownloads.WaitingCount + " to go.", DasToast.Tone.Info,
                        "View", () => VehicleLibraryPanel.Open(), "queue", 30f);
                return;
            }
            DasToast.Dismiss("queue");
            if (_readyThisRun.Count == 1)
            {
                string id = _readyThisRun[0];
                DasToast.Show(job.vehicleId == id ? job.name + " is ready" : "Vehicle ready", "Downloaded and ready to open.", DasToast.Tone.Good,
                    "Open", () => VehicleLibraryPanel.OpenVehicle(id), "ready:" + id, 20f);
            }
            else if (_readyThisRun.Count > 1)
                DasToast.Show(_readyThisRun.Count + " vehicles are ready", "Downloaded and ready to open.", DasToast.Tone.Good,
                    "View", () => VehicleLibraryPanel.Open(LibraryFilter.OnThisPc), "ready", 20f);
            _readyThisRun.Clear();
        }

        private void Evaluate()
        {
            _evaluatePending = false;
            var loader = RemoteAddressableVehicleLoader.Instance;
            var service = VehicleStorageService.Instance;
            if (loader == null || !loader.IsCatalogLoaded) return;
            if (service == null || !service.IsReady) { _evaluatePending = true; _nextEvaluate = Time.unscaledTime + 1f; return; }

            List<LibraryItem> items = BuildItems(true);
            try { AnnounceChanges(items); } catch (Exception e) { Debug.LogWarning("[Updates] " + e.Message); }
            LibrarySummary sum = VehicleLibrary.Summarize(items);
            if (!sum.HasNotice) { HideNotice(); return; }

            string text = sum.NoticeText();
            _noticeFilter = sum.updates > 0 ? LibraryFilter.Updates : sum.isNew > 0 ? LibraryFilter.New : LibraryFilter.All;
            if (text == _dismissedText) return;                       // "Later" for exactly this: don't nag again this session
            Debug.Log("[Updates] " + text);
            if (OnHome()) ShowNotice(text); else _pendingText = text;
        }

        private static bool OnHome()
        {
            return UnityEngine.Object.FindFirstObjectByType<HomeController>() != null;
        }

        private void ShowNotice(string text)
        {
            if (_notice == null) BuildNotice();
            _noticeText.text = text;
            _notice.SetActive(true);
        }

        private void HideNotice()
        {
            if (_notice != null) _notice.SetActive(false);
        }

        private void BuildNotice()
        {
            _notice = new GameObject("DAS UpdateNotice");
            _notice.transform.SetParent(transform, false);
            DasUi.NewOverlayCanvas(_notice, 3500);

            var box = DasUi.NewImage("Box", _notice.transform, new Color(0.13f, 0.14f, 0.18f, 0.97f));
            var rt = box.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -18f);
            rt.sizeDelta = new Vector2(1060f, 76f);
            var accent = DasUi.NewImage("Accent", box.transform, DasUi.AccentColor);
            DasUi.Place(accent.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(8f, 0f));

            var title = DasUi.NewText("Title", box.transform, "Vehicle updates", 18f, DasUi.Muted, TextAlignmentOptions.TopLeft);
            DasUi.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(26f, -30f), new Vector2(-300f, -8f));
            _noticeText = DasUi.NewText("Text", box.transform, "", 22f, Color.white, TextAlignmentOptions.TopLeft);
            DasUi.Place(_noticeText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(26f, 8f), new Vector2(-300f, -32f));

            Button view = DasUi.NewButton("View", box.transform, "View", DasUi.AccentColor, () =>
            {
                HideNotice();
                VehicleLibraryPanel.Open(_noticeFilter);
            }, 20f);
            DasUi.Place((RectTransform)view.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-280f, -22f), new Vector2(-150f, 22f));
            Button later = DasUi.NewButton("Later", box.transform, "Later", DasUi.ButtonColor, () =>
            {
                _dismissedText = _noticeText.text;
                HideNotice();
            }, 20f);
            DasUi.Place((RectTransform)later.transform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-138f, -22f), new Vector2(-18f, 22f));
        }
    }
}
