using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VehicleMeasurement.Storage;

namespace VehicleMeasurement
{
    /// <summary>
    /// Loading screen shown over Home while it gets ready: connecting to the server, checking which vehicles are on
    /// this PC, and loading the vehicle pictures. A progress bar and a plain status line say what is happening.
    /// It closes itself as soon as everything is ready, and it can never trap the user: after a few seconds a
    /// Continue button appears, and after <see cref="MaxSeconds"/> it closes regardless.
    /// Built in code - nothing to set up in the scene.
    /// </summary>
    public class HomeLoadingOverlay : MonoBehaviour
    {
        /// <summary>What the overlay asks the screen behind it.</summary>
        public interface IProgressSource
        {
            /// <summary>Pictures still loading for the cards on screen.</summary>
            int PendingThumbnails { get; }
            /// <summary>Pictures the cards on screen needed when they were built.</summary>
            int TotalThumbnails { get; }
        }

        public const float ContinueAfterSeconds = 8f;
        public const float MaxSeconds = 45f;

        private static HomeLoadingOverlay _current;

        private IProgressSource _source;
        private float _startedAt;
        private float _shownProgress;
        private TextMeshProUGUI _status, _detail;
        private RectTransform _fill;
        private GameObject _continue;
        private CanvasGroup _group;
        private bool _closing;

        /// <summary>Show the overlay for this screen (does nothing if one is already open).</summary>
        public static HomeLoadingOverlay Show(IProgressSource source)
        {
            if (_current != null) { _current._source = source; return _current; }
            var go = new GameObject("HomeLoadingOverlay");
            var overlay = go.AddComponent<HomeLoadingOverlay>();
            overlay._source = source;
            return overlay;
        }

        /// <summary>Close it now (e.g. when Home is left).</summary>
        public static void Hide()
        {
            if (_current != null) Destroy(_current.gameObject);
        }

        // ── state the overlay reads ──────────────────────────────────────
        private struct Snapshot { public float progress; public string status, detail; public bool done; }

        private Snapshot Read()
        {
            var s = new Snapshot();
            var loader = RemoteAddressableVehicleLoader.Instance;
            var storage = VehicleStorageService.Instance;
            var net = NetworkChecker.Instance;

            bool catalogDone = loader == null || loader.IsCatalogLoaded || loader.IsCatalogFailed;
            bool offline = (loader != null && loader.IsCatalogFailed) || (net != null && net.HasChecked && !net.IsOnline);
            // the storage check needs the catalog; offline it can't finish, and Home already shows the last known list
            bool storageDone = storage == null || storage.IsReady || !storage.IsPending || offline;
            int pending = _source != null ? _source.PendingThumbnails : 0;
            int total = _source != null ? Mathf.Max(_source.TotalThumbnails, pending) : 0;
            bool picturesDone = pending == 0;

            float pictures = total > 0 ? 1f - (float)pending / total : 1f;
            s.progress = 0.05f + (catalogDone ? 0.45f : 0.15f * Mathf.Clamp01((Time.unscaledTime - _startedAt) / 10f))
                       + (storageDone ? 0.30f : 0f) + (catalogDone && storageDone ? 0.20f * pictures : 0f);

            if (!catalogDone) { s.status = "Connecting to the DAS server…"; s.detail = "Getting the list of vehicles."; }
            else if (!storageDone) { s.status = "Checking downloaded vehicles…"; s.detail = "Finding which vehicles are on this PC."; }
            else if (!picturesDone) { s.status = "Loading vehicle pictures…"; s.detail = (total - pending) + " of " + total; }
            else { s.status = "Ready"; s.detail = ""; }
            if (offline && s.status != "Ready") s.detail = "Working offline: showing the vehicles already on this PC.";

            s.done = catalogDone && storageDone && picturesDone;
            return s;
        }

        // ── lifecycle ────────────────────────────────────────────────────
        private void Awake()
        {
            if (_current != null && _current != this) { Destroy(gameObject); return; }
            _current = this;
            _startedAt = Time.unscaledTime;
            BuildUi();
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }

        private void Update()
        {
            if (_closing)
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, Time.unscaledDeltaTime * 4f);
                if (_group.alpha <= 0f) Destroy(gameObject);
                return;
            }

            Snapshot s = Read();
            float elapsed = Time.unscaledTime - _startedAt;
            _shownProgress = Mathf.MoveTowards(_shownProgress, Mathf.Clamp01(s.progress), Time.unscaledDeltaTime * 1.5f);
            _fill.anchorMax = new Vector2(Mathf.Max(0.02f, _shownProgress), 1f);
            _status.text = s.status;
            _detail.text = s.detail;
            if (!_continue.activeSelf && elapsed >= ContinueAfterSeconds && !s.done) _continue.SetActive(true);

            if ((s.done && _shownProgress >= 0.98f) || elapsed >= MaxSeconds)
            {
                if (elapsed >= MaxSeconds && !s.done) Debug.LogWarning("[Home] Still loading after " + MaxSeconds + " s (" + s.status + "); showing Home anyway.");
                Close();
            }
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            _group.blocksRaycasts = false;      // clicks go to Home at once while it fades
        }

        // ── look ─────────────────────────────────────────────────────────
        private static readonly Color Backdrop = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        private static readonly Color Panel = new Color(0.10f, 0.11f, 0.14f, 1f);
        private static readonly Color Track = new Color(0.20f, 0.22f, 0.28f, 1f);
        private static readonly Color Accent = new Color(0.86f, 0.18f, 0.20f, 1f);
        private static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f, 1f);

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;                       // above Home (and the Storage screen)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();
            if (EventSystem.current == null && FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            Image back = NewImage("Backdrop", transform, Backdrop);       // also stops clicks reaching Home while loading
            Stretch(back.rectTransform);

            Image panel = NewImage("Panel", back.transform, Panel);
            var p = panel.rectTransform;
            p.anchorMin = p.anchorMax = p.pivot = new Vector2(0.5f, 0.5f);
            p.sizeDelta = new Vector2(760f, 280f);

            var title = NewText("Title", panel.transform, "Getting your vehicles ready", 34f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -78f), new Vector2(-40f, -32f));

            _status = NewText("Status", panel.transform, "Starting…", 24f, Color.white, TextAlignmentOptions.TopLeft);
            Place(_status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -124f), new Vector2(-40f, -88f));

            Image track = NewImage("Track", panel.transform, Track);
            Place(track.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -150f), new Vector2(-40f, -138f));
            Image fill = NewImage("Fill", track.transform, Accent);
            _fill = fill.rectTransform;
            _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0.02f, 1f);
            _fill.offsetMin = Vector2.zero; _fill.offsetMax = Vector2.zero;

            _detail = NewText("Detail", panel.transform, "", 19f, Muted, TextAlignmentOptions.TopLeft);
            Place(_detail.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -196f), new Vector2(-40f, -162f));

            // Continue: appears after a few seconds so a slow server never blocks the user
            Image btn = NewImage("Continue", panel.transform, new Color(0.22f, 0.25f, 0.32f, 1f));
            Place(btn.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-240f, 28f), new Vector2(-40f, 76f));
            var button = btn.gameObject.AddComponent<Button>();
            button.targetGraphic = btn;
            button.onClick.AddListener(Close);
            var label = NewText("Label", btn.transform, "Continue", 22f, Color.white, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            _continue = btn.gameObject;
            _continue.SetActive(false);
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax;
        }
    }
}
