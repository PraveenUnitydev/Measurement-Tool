using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// The Storage screen: one row per vehicle on this PC with its size, version and download date, a Remove button
    /// per vehicle, and Remove All. Built entirely in code (no prefab), opened with <see cref="Open"/>.
    ///
    /// Only the downloaded model files are ever removed here. Saved measurements and thumbnails are never touched,
    /// because a measurement saved on this PC may not exist anywhere else.
    /// </summary>
    public class StoragePanel : MonoBehaviour
    {
        private static StoragePanel _open;

        public static void Open()
        {
            if (_open != null) return;
            var go = new GameObject("StoragePanel");
            go.AddComponent<StoragePanel>();
        }

        // ── look ─────────────────────────────────────────────────────────
        private static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.14f, 1f);
        private static readonly Color RowColorA = new Color(0.14f, 0.15f, 0.19f, 1f);
        private static readonly Color RowColorB = new Color(0.12f, 0.13f, 0.16f, 1f);
        private static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f, 1f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.25f, 0.32f, 1f);
        private static readonly Color DangerColor = new Color(0.62f, 0.20f, 0.20f, 1f);

        private const float SizeW = 130f, VersionW = 190f, DateW = 150f, ActionW = 130f;

        // ── state ────────────────────────────────────────────────────────
        private enum SortBy { Size, Name, Date }
        private SortBy _sort = SortBy.Size;
        private bool _busy;

        private RectTransform _content;
        private TextMeshProUGUI _summaryText, _statusText, _footerPath;
        private Button _removeAllButton;
        private GameObject _dim;

        private void Awake()
        {
            _open = this;
            BuildUi();
            VehicleStorageService.Changed += OnStorageChanged;
            Rebuild();
        }

        private void OnDestroy()
        {
            VehicleStorageService.Changed -= OnStorageChanged;
            if (_open == this) _open = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !_busy) Close();
        }

        private void OnStorageChanged() { if (!_busy) Rebuild(); }

        private void Close() { Destroy(gameObject); }

        // ── content ──────────────────────────────────────────────────────

        private void Rebuild()
        {
            var service = VehicleStorageService.Instance;
            foreach (Transform child in _content) Destroy(child.gameObject);

            if (service == null || !service.IsReady)
            {
                _summaryText.text = "The list of downloaded vehicles isn't ready yet.";
                SetText(_footerPath, "");
                if (_removeAllButton != null) _removeAllButton.interactable = false;
                return;
            }

            StorageSummary sum = service.GetSummary();
            _summaryText.text = sum.downloadedCount + " vehicle(s) on this PC · " + ByteFormat.Format(sum.downloadedBytes)
                + "      " + sum.notDownloadedCount + " not downloaded (" + ByteFormat.Format(sum.notDownloadedBytes) + " to download them all)";
            SetText(_footerPath, string.IsNullOrEmpty(sum.cachePath) ? "" : "Files are stored in: " + sum.cachePath);
            if (_removeAllButton != null) _removeAllButton.interactable = !_busy && sum.downloadedCount > 0;

            List<StorageRow> rows = service.GetRows();
            switch (_sort)
            {
                case SortBy.Name: rows = rows.OrderBy(r => r.name, StringComparer.OrdinalIgnoreCase).ToList(); break;
                case SortBy.Date: rows = rows.OrderByDescending(r => r.downloadedAtUtc.HasValue ? r.downloadedAtUtc.Value.Ticks : 0L).ToList(); break;
                default: rows = rows.OrderByDescending(r => r.bytes).ToList(); break;
            }

            if (rows.Count == 0)
            {
                var empty = NewText("Empty", _content, "No vehicles are downloaded on this PC.", 24f, Muted, TextAlignmentOptions.Center);
                SetPreferredHeight(empty.gameObject, 120f);
                return;
            }

            for (int i = 0; i < rows.Count; i++) BuildRow(rows[i], i % 2 == 0 ? RowColorA : RowColorB);
        }

        private void BuildRow(StorageRow row, Color background)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var bg = go.AddComponent<Image>();
            bg.color = background;
            SetPreferredHeight(go, 54f);

            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 6, 6);
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            string name = row.name + (string.IsNullOrEmpty(row.statusText) ? "" : "   —  " + row.statusText);
            var nameText = NewText("Name", go.transform, name, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            if (!string.IsNullOrEmpty(row.statusText)) nameText.color = StorageBadge.ColorFor(row.tone == LabelTone.None ? LabelTone.Warn : row.tone);
            else nameText.color = Color.white;
            SetFlexible(nameText.gameObject);

            SetWidth(NewText("Size", go.transform, ByteFormat.Format(row.bytes), 22f, Color.white, TextAlignmentOptions.MidlineRight).gameObject, SizeW);
            SetWidth(NewText("Version", go.transform, string.IsNullOrEmpty(row.versionText) ? "—" : row.versionText, 20f, Muted, TextAlignmentOptions.MidlineLeft).gameObject, VersionW);
            string date = row.downloadedAtUtc.HasValue ? row.downloadedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : "—";
            SetWidth(NewText("Date", go.transform, date, 20f, Muted, TextAlignmentOptions.MidlineLeft).gameObject, DateW);

            string id = row.vehicleId, display = row.name;
            long bytes = row.bytes;
            Button remove = NewButton("Remove", go.transform, "Remove", ButtonColor, () => AskRemove(id, display, bytes));
            SetWidth(remove.gameObject, ActionW);
        }

        // ── actions ──────────────────────────────────────────────────────

        private void AskRemove(string vehicleId, string name, long bytes)
        {
            if (_busy) return;
            ShowConfirm("Remove " + name + "?",
                "This deletes the downloaded model files for " + name + " (about " + ByteFormat.Format(bytes) + ") from this PC. "
                + "Files that other vehicles share are kept. Your saved measurements are not touched. You can download the vehicle again any time.",
                "Remove", () => StartCoroutine(RemoveOne(vehicleId)));
        }

        private void AskRemoveAll()
        {
            var service = VehicleStorageService.Instance;
            if (_busy || service == null || !service.IsReady) return;
            StorageSummary sum = service.GetSummary();
            ShowConfirm("Remove all downloaded vehicles?",
                "This deletes the model files for all " + sum.downloadedCount + " vehicle(s) (" + ByteFormat.Format(sum.downloadedBytes) + ") from this PC. "
                + "Your saved measurements are not touched. Each vehicle downloads again the next time you open it.",
                "Remove all", () => StartCoroutine(RemoveEverything()));
        }

        private IEnumerator RemoveOne(string vehicleId)
        {
            SetBusy(true, "Removing…");
            RemoveOutcome outcome = null;
            yield return VehicleStorageService.Instance.RemoveVehicleRoutine(vehicleId, o => outcome = o);
            SetBusy(false, outcome != null ? outcome.message : "Done.");
            Rebuild();
        }

        private IEnumerator RemoveEverything()
        {
            SetBusy(true, "Removing all vehicles…");
            RemoveOutcome outcome = null;
            yield return VehicleStorageService.Instance.RemoveAllRoutine(o => outcome = o);
            SetBusy(false, outcome != null ? outcome.message : "Done.");
            Rebuild();
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            SetText(_statusText, status);
            if (_removeAllButton != null) _removeAllButton.interactable = !busy;
        }

        // ── building the screen ──────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();

            // Dark backdrop that also stops clicks reaching Home underneath
            _dim = NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.7f)).gameObject;
            Stretch((RectTransform)_dim.transform);

            var panel = NewImage("Panel", _dim.transform, PanelColor);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1280f, 860f);

            var title = NewText("Title", panel.transform, "Storage on this PC", 40f, Color.white, TextAlignmentOptions.TopLeft);
            Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -76f), new Vector2(-32f, -24f));
            title.fontStyle = FontStyles.Bold;

            _summaryText = NewText("Summary", panel.transform, "", 22f, Muted, TextAlignmentOptions.TopLeft);
            Place(_summaryText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -122f), new Vector2(-32f, -82f));

            // Column headers (click to sort)
            var head = new GameObject("Headers", typeof(RectTransform));
            head.transform.SetParent(panel.transform, false);
            Place((RectTransform)head.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -172f), new Vector2(-32f, -132f));
            var hh = head.AddComponent<HorizontalLayoutGroup>();
            hh.padding = new RectOffset(16, 16, 0, 0);
            hh.spacing = 10f;
            hh.childAlignment = TextAnchor.MiddleLeft;
            hh.childControlWidth = true; hh.childControlHeight = true;
            hh.childForceExpandWidth = false; hh.childForceExpandHeight = true;
            var nameHead = HeaderButton(head.transform, "Vehicle", SortBy.Name, TextAlignmentOptions.MidlineLeft); SetFlexible(nameHead);
            SetWidth(HeaderButton(head.transform, "Size", SortBy.Size, TextAlignmentOptions.MidlineRight), SizeW);
            SetWidth(NewText("VersionHead", head.transform, "Version", 20f, Muted, TextAlignmentOptions.MidlineLeft).gameObject, VersionW);
            SetWidth(HeaderButton(head.transform, "Downloaded", SortBy.Date, TextAlignmentOptions.MidlineLeft), DateW);
            SetWidth(NewText("ActionHead", head.transform, "", 20f, Muted, TextAlignmentOptions.MidlineLeft).gameObject, ActionW);

            // Scrolling list
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(panel.transform, false);
            Place((RectTransform)scrollGo.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 150f), new Vector2(-32f, -176f));
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch((RectTransform)viewport.transform);
            viewport.AddComponent<RectMask2D>();
            var vpImage = viewport.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _content = (RectTransform)content.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = new Vector2(0f, 0f);
            _content.offsetMax = new Vector2(0f, 0f);
            var v = content.AddComponent<VerticalLayoutGroup>();
            v.spacing = 3f;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = _content;

            // Footer
            _statusText = NewText("Status", panel.transform, "", 22f, Color.white, TextAlignmentOptions.TopLeft);
            Place(_statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 108f), new Vector2(-32f, 144f));
            _footerPath = NewText("Path", panel.transform, "", 16f, Muted, TextAlignmentOptions.TopLeft);
            Place(_footerPath.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 76f), new Vector2(-32f, 104f));

            _removeAllButton = NewButton("RemoveAll", panel.transform, "Remove all downloaded vehicles", DangerColor, AskRemoveAll);
            Place((RectTransform)_removeAllButton.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 20f), new Vector2(412f, 68f));
            Button close = NewButton("Close", panel.transform, "Close", ButtonColor, Close);
            Place((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-192f, 20f), new Vector2(-32f, 68f));
        }

        private GameObject HeaderButton(Transform parent, string label, SortBy sort, TextAlignmentOptions align)
        {
            Button b = NewButton("Head_" + label, parent, label, new Color(0f, 0f, 0f, 0f), () => { _sort = sort; Rebuild(); });
            var text = b.GetComponentInChildren<TextMeshProUGUI>();
            text.color = Muted; text.fontSize = 20f; text.alignment = align;
            return b.gameObject;
        }

        private void ShowConfirm(string title, string message, string yesLabel, Action onYes)
        {
            var overlay = NewImage("Confirm", _dim.transform, new Color(0f, 0f, 0f, 0.55f));
            Stretch(overlay.rectTransform);

            var box = NewImage("Box", overlay.transform, new Color(0.16f, 0.17f, 0.21f, 1f));
            var brt = box.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(780f, 340f);

            var t = NewText("T", box.transform, title, 32f, Color.white, TextAlignmentOptions.TopLeft);
            t.fontStyle = FontStyles.Bold;
            Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -76f), new Vector2(-32f, -24f));
            var m = NewText("M", box.transform, message, 22f, Muted, TextAlignmentOptions.TopLeft);
            Place(m.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 96f), new Vector2(-32f, -88f));

            var overlayGo = overlay.gameObject;
            Button cancel = NewButton("Cancel", box.transform, "Cancel", ButtonColor, () => Destroy(overlayGo));
            Place((RectTransform)cancel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-352f, 24f), new Vector2(-192f, 72f));
            Button yes = NewButton("Yes", box.transform, yesLabel, DangerColor, () => { Destroy(overlayGo); if (onYes != null) onYes(); });
            Place((RectTransform)yes.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-172f, 24f), new Vector2(-32f, 72f));
        }

        // ── small uGUI helpers ───────────────────────────────────────────

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
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
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        private static Button NewButton(string name, Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            var text = NewText("Label", go.transform, label, 22f, Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        private static void SetWidth(GameObject go, float width)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0f;
        }

        private static void SetFlexible(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minWidth = 100f;
        }

        private static void SetPreferredHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
        }

        private static void SetText(TextMeshProUGUI t, string text) { if (t != null) t.text = text ?? ""; }
    }
}
