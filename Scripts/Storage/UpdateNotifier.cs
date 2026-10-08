using System;
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
        public static float RecheckMinutes = 30f;

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
            _nextRecheck = Time.unscaledTime + RecheckMinutes * 60f;
        }

        private void OnDestroy()
        {
            VehicleStorageService.Changed -= OnSomethingChanged;
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

            // Ask the server again now and then while Home is open and nothing is downloading
            if (Time.unscaledTime >= _nextRecheck)
            {
                _nextRecheck = Time.unscaledTime + RecheckMinutes * 60f;
                if (loader != null && loader.IsCatalogLoaded && OnHome() && !loader.IsLoading && !BatchDownloads.Running)
                {
                    Debug.Log("[Updates] Checking the server for new or updated vehicles...");
                    loader.RefreshCatalog();
                }
            }
        }

        private void Evaluate()
        {
            _evaluatePending = false;
            var loader = RemoteAddressableVehicleLoader.Instance;
            var service = VehicleStorageService.Instance;
            if (loader == null || !loader.IsCatalogLoaded) return;
            if (service == null || !service.IsReady) { _evaluatePending = true; _nextEvaluate = Time.unscaledTime + 1f; return; }

            List<LibraryItem> items = BuildItems(true);
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
