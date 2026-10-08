using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// "Download &amp; update vehicles": every vehicle on the server with a check box, its status on this PC (on this PC,
    /// update needed, not downloaded, new) and size. Tick several - or "All updates" - and download them in one go
    /// (BatchDownloads). Filters, search, overall progress, cancel and retry. Closing the screen doesn't stop the
    /// downloads; opening it again shows where they are. Built in code; open with <see cref="Open"/> (also Ctrl+Shift+D,
    /// the update notice, the Storage screen, or a button wired to it).
    /// </summary>
    public class VehicleLibraryPanel : MonoBehaviour
    {
        private static VehicleLibraryPanel _open;

        public static void Open() { Open(LibraryFilter.All); }

        public static void Open(LibraryFilter filter)
        {
            if (_open != null) { _open.SetFilter(filter); return; }
            var go = new GameObject("VehicleLibraryPanel");
            var p = go.AddComponent<VehicleLibraryPanel>();
            p._filter = filter;
        }

        private class RowUi
        {
            public LibraryItem item;
            public GameObject go;
            public Toggle toggle;
            public Image thumb;
            public TextMeshProUGUI status, size;
        }

        private List<LibraryItem> _items = new List<LibraryItem>();
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RowUi> _rows = new Dictionary<string, RowUi>(StringComparer.OrdinalIgnoreCase);
        private LibraryFilter _filter = LibraryFilter.All;
        private string _search = "";
        private bool _dirty;
        private float _nextQueueRefresh;

        private RectTransform _content;
        private TextMeshProUGUI _summary, _selectionText, _queueText, _emptyText;
        private Image _queueBar;
        private Button _downloadButton, _cancelButton, _retryButton, _clearButton;
        private readonly Dictionary<LibraryFilter, Button> _tabs = new Dictionary<LibraryFilter, Button>();
        private Toggle _allToggle;
        private bool _settingAll;
        private RemoteAddressableVehicleLoader _thumbSource;

        private void Awake()
        {
            _open = this;
            BuildUi();
            VehicleStorageService.Changed += MarkDirty;
            BatchDownloads.Changed += OnQueueChanged;
            _thumbSource = RemoteAddressableVehicleLoader.Instance;
            if (_thumbSource != null) _thumbSource.ThumbnailUpdated += OnThumbnail;
        }

        private void Start()
        {
            Reload();
            UpdateNotifier.MarkSeenNow();     // the user is looking at the list now: "new" is no longer news next time
        }

        private void OnDestroy()
        {
            VehicleStorageService.Changed -= MarkDirty;
            BatchDownloads.Changed -= OnQueueChanged;
            if (_thumbSource != null) _thumbSource.ThumbnailUpdated -= OnThumbnail;
            if (_open == this) _open = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if (_dirty) { _dirty = false; Reload(); }
            if (BatchDownloads.Running && Time.unscaledTime >= _nextQueueRefresh)
            {
                _nextQueueRefresh = Time.unscaledTime + 0.25f;
                RefreshQueue();
            }
        }

        private void MarkDirty() { _dirty = true; }

        private void OnQueueChanged()
        {
            // Finished vehicles change their status: re-read them (storage also raises Changed when it has recorded them)
            RefreshQueue();
        }

        private void Close() { Destroy(gameObject); }

        // ── data ─────────────────────────────────────────────────────────

        private void Reload()
        {
            _items = UpdateNotifier.BuildItems(false);
            // drop selections that no longer make sense (downloaded meanwhile, or gone from the server)
            var actionable = new HashSet<string>(_items.Where(i => i.Actionable).Select(i => i.vehicleId), StringComparer.OrdinalIgnoreCase);
            _selected.RemoveWhere(id => !actionable.Contains(id));
            RebuildList();
        }

        private void SetFilter(LibraryFilter f)
        {
            _filter = f;
            RebuildList();
        }

        private List<LibraryItem> Visible()
        {
            return VehicleLibrary.Sorted(_items.Where(i => VehicleLibrary.Matches(i, _filter, _search)));
        }

        // ── list ─────────────────────────────────────────────────────────

        private void RebuildList()
        {
            if (_content == null) return;
            foreach (Transform child in _content) Destroy(child.gameObject);
            _rows.Clear();

            LibrarySummary sum = VehicleLibrary.Summarize(_items);
            var loader = RemoteAddressableVehicleLoader.Instance;
            bool storageReady = VehicleStorageService.Instance != null && VehicleStorageService.Instance.IsReady;
            if (loader == null || !loader.IsCatalogLoaded)
                _summary.text = "The vehicle list from the server isn't loaded yet.";
            else
                _summary.text = sum.total + " vehicles on the server  ·  " + sum.downloaded + " on this PC  ·  " + sum.updates + " need an update  ·  "
                    + sum.notDownloaded + " not downloaded" + (sum.isNew > 0 ? "  ·  " + sum.isNew + " new" : "")
                    + (storageReady ? "" : "   (checking this PC...)");

            SetTab(LibraryFilter.All, "All (" + sum.total + ")");
            SetTab(LibraryFilter.Updates, "Updates (" + sum.updates + ")");
            SetTab(LibraryFilter.New, "New (" + sum.isNew + ")");
            SetTab(LibraryFilter.NotDownloaded, "Not downloaded (" + sum.notDownloaded + ")");
            SetTab(LibraryFilter.OnThisPc, "On this PC (" + (sum.downloaded + sum.updates) + ")");

            List<LibraryItem> visible = Visible();
            _emptyText.gameObject.SetActive(visible.Count == 0);
            _emptyText.text = _items.Count == 0 ? "No vehicles yet - the server list is still loading." :
                _filter == LibraryFilter.Updates ? "All vehicles on this PC are up to date." :
                _filter == LibraryFilter.New ? "No new vehicles since you last looked." : "Nothing matches.";

            for (int i = 0; i < visible.Count; i++) BuildRow(visible[i], i % 2 == 0 ? DasUi.RowColorA : DasUi.RowColorB);
            RefreshSelection();
            RefreshQueue();
        }

        private void SetTab(LibraryFilter f, string label)
        {
            Button b;
            if (!_tabs.TryGetValue(f, out b)) return;
            DasUi.SetLabel(b, label);
            b.GetComponent<Image>().color = f == _filter ? DasUi.TabOnColor : DasUi.ButtonColor;
        }

        private void BuildRow(LibraryItem item, Color background)
        {
            var go = new GameObject("Row_" + item.vehicleId, typeof(RectTransform));
            go.transform.SetParent(_content, false);
            go.AddComponent<Image>().color = background;
            DasUi.SetPreferredHeight(go, 58f);
            DasUi.Row(go, 14, 16, 14f).childAlignment = TextAnchor.MiddleLeft;

            var row = new RowUi { item = item, go = go };
            string id = item.vehicleId;
            row.toggle = DasUi.NewToggle("Select", go.transform, _selected.Contains(id), on =>
            {
                if (on) _selected.Add(id); else _selected.Remove(id);
                RefreshSelection();
            });
            row.toggle.interactable = item.Actionable;

            row.thumb = DasUi.NewImage("Thumb", go.transform, new Color(0f, 0f, 0f, 0.35f));
            row.thumb.preserveAspect = true;
            DasUi.SetSize(row.thumb.gameObject, 80f, 46f);
            var live = RemoteAddressableVehicleLoader.Instance;
            Sprite s = live != null ? live.GetThumbnail(id) : null;
            if (s != null) { row.thumb.sprite = s; row.thumb.color = Color.white; }

            var name = DasUi.NewText("Name", go.transform, item.name + (item.isNew ? "   <color=#6FB8FF>NEW</color>" : ""), 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            DasUi.SetFlexible(name.gameObject, 160f);
            DasUi.SetWidth(DasUi.NewText("Maker", go.transform, item.manufacturer, 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, 220f);

            row.status = DasUi.NewText("Status", go.transform, "", 19f, DasUi.Muted, TextAlignmentOptions.MidlineLeft);
            DasUi.SetWidth(row.status.gameObject, 360f);
            row.size = DasUi.NewText("Size", go.transform, "", 19f, Color.white, TextAlignmentOptions.MidlineRight);
            DasUi.SetWidth(row.size.gameObject, 120f);

            // click anywhere on the row to tick it
            var click = go.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => { if (row.toggle.interactable) row.toggle.isOn = !row.toggle.isOn; });

            _rows[id] = row;
            RefreshRowStatus(row);
        }

        private void RefreshRowStatus(RowUi row)
        {
            LibraryItem i = row.item;
            BatchDownloads.Job job = BatchDownloads.Find(i.vehicleId);
            string text; Color color;
            if (job != null && job.state == BatchDownloads.JobState.Downloading)
            {
                text = job.progress != null && job.progress.total > 0
                    ? "Downloading " + Mathf.RoundToInt(job.progress.fraction * 100f) + "%  (" + ByteFormat.Format(job.progress.downloaded) + ")"
                    : "Downloading...";
                color = DasUi.InfoColor;
            }
            else if (job != null && job.state == BatchDownloads.JobState.Waiting) { text = "Queued"; color = DasUi.InfoColor; }
            else if (job != null && job.state == BatchDownloads.JobState.Failed) { text = "Failed: " + job.message; color = DasUi.WarnColor; }
            else if (!i.known) { text = "Checking this PC..."; color = DasUi.Muted; }
            else if (i.needsUpdate) { text = i.changedOnServer ? "Update available (new version)" : "Update needed"; color = DasUi.WarnColor; }
            else if (i.downloaded) { text = "On this PC" + (job != null && job.state == BatchDownloads.JobState.Done ? " - just downloaded" : ""); color = DasUi.GoodColor; }
            else { text = i.changedOnServer ? "Not downloaded (updated on server)" : "Not downloaded"; color = DasUi.Muted; }
            row.status.text = text;
            row.status.color = color;
            long size = i.needsUpdate || !i.downloaded ? (i.downloadBytes > 0 ? i.downloadBytes : i.totalBytes) : i.totalBytes;
            row.size.text = size > 0 ? ByteFormat.Format(size) : "";
            bool busy = job != null && (job.state == BatchDownloads.JobState.Downloading || job.state == BatchDownloads.JobState.Waiting);
            row.toggle.interactable = i.Actionable && !busy;
            if (busy && row.toggle.isOn) { _selected.Remove(i.vehicleId); row.toggle.SetIsOnWithoutNotify(false); }
        }

        private void OnThumbnail(string vehicleId, Sprite sprite)
        {
            RowUi row;
            if (sprite != null && _rows.TryGetValue(vehicleId, out row) && row.thumb != null) { row.thumb.sprite = sprite; row.thumb.color = Color.white; }
        }

        // ── selection & actions ──────────────────────────────────────────

        private void RefreshSelection()
        {
            var chosen = _items.Where(i => _selected.Contains(i.vehicleId)).ToList();
            long bytes = VehicleLibrary.BytesFor(chosen);
            long free = DiskSpace.FreeOnDownloadDrive();
            string freeText = free > 0 ? "  ·  " + ByteFormat.Format(free) + " free" : "";
            _selectionText.text = chosen.Count == 0 ? "Tick vehicles to download or update them together." + freeText
                : chosen.Count + " selected  ·  " + ByteFormat.Format(bytes) + " to download" + freeText;
            _downloadButton.interactable = chosen.Count > 0;
            DasUi.SetLabel(_downloadButton, chosen.Count > 0 ? "Download / update " + chosen.Count : "Download / update selected");

            var visibleActionable = _rows.Values.Where(r => r.toggle.interactable).ToList();
            _settingAll = true;
            if (_allToggle != null) _allToggle.SetIsOnWithoutNotify(visibleActionable.Count > 0 && visibleActionable.All(r => _selected.Contains(r.item.vehicleId)));
            _settingAll = false;
        }

        private void SelectVisible(bool on)
        {
            if (_settingAll) return;
            foreach (RowUi r in _rows.Values)
            {
                if (!r.toggle.interactable) continue;
                if (on) _selected.Add(r.item.vehicleId); else _selected.Remove(r.item.vehicleId);
                r.toggle.SetIsOnWithoutNotify(on);
            }
            RefreshSelection();
        }

        private void SelectAllUpdates()
        {
            foreach (LibraryItem i in _items.Where(i => i.known && i.needsUpdate && !BatchDownloads.IsQueued(i.vehicleId))) _selected.Add(i.vehicleId);
            if (_filter != LibraryFilter.Updates && _filter != LibraryFilter.All && _filter != LibraryFilter.OnThisPc) _filter = LibraryFilter.Updates;
            RebuildList();
        }

        private void StartDownloads()
        {
            var chosen = _items.Where(i => _selected.Contains(i.vehicleId) && i.Actionable).ToList();
            if (chosen.Count == 0) return;
            long bytes = VehicleLibrary.BytesFor(chosen);
            string space = BatchDownloads.SpaceProblem(bytes);
            if (space != null)
            {
                DasDialog.Info("Not enough disk space", space + "\n\nSelected: " + ByteFormat.Format(bytes) +
                               ". Choose fewer vehicles, free some space, or move downloads to another drive (Storage screen).");
                return;
            }
            int added = BatchDownloads.Enqueue(chosen);
            foreach (LibraryItem i in chosen) UpdateNotifier.MarkVehicleSeen(i);
            _selected.Clear();
            Debug.Log("[Library] Queued " + added + " vehicle(s), about " + ByteFormat.Format(bytes) + ".");
            RebuildList();
        }

        private void RefreshQueue()
        {
            if (_queueText == null) return;
            string d = BatchDownloads.Describe();
            _queueText.text = d;
            bool any = BatchDownloads.Jobs.Count > 0;
            _queueBar.transform.parent.gameObject.SetActive(any);
            _queueBar.fillAmount = BatchDownloads.OverallFraction();
            _cancelButton.gameObject.SetActive(BatchDownloads.Running && BatchDownloads.WaitingCount > 0);
            _retryButton.gameObject.SetActive(!BatchDownloads.Running && BatchDownloads.Jobs.Any(j => j.state == BatchDownloads.JobState.Failed || j.state == BatchDownloads.JobState.Cancelled));
            _clearButton.gameObject.SetActive(!BatchDownloads.Running && any);
            foreach (RowUi r in _rows.Values) RefreshRowStatus(r);
        }

        // ── building the screen ──────────────────────────────────────────

        private void BuildUi()
        {
            DasUi.NewOverlayCanvas(gameObject, 4100);

            var dim = DasUi.NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.7f));
            DasUi.Stretch(dim.rectTransform);

            var panel = DasUi.NewImage("Panel", dim.transform, DasUi.PanelColor);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1500f, 940f);
            Transform p = panel.transform;

            var title = DasUi.NewText("Title", p, "Download & update vehicles", 38f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            DasUi.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -74f), new Vector2(-200f, -22f));
            Button close = DasUi.NewButton("Close", p, "Close", DasUi.ButtonColor, Close);
            DasUi.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-172f, -72f), new Vector2(-32f, -26f));

            _summary = DasUi.NewText("Summary", p, "", 20f, DasUi.Muted, TextAlignmentOptions.TopLeft);
            DasUi.Place(_summary.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -112f), new Vector2(-32f, -80f));

            // Filter tabs + search
            var bar = new GameObject("Filters", typeof(RectTransform));
            bar.transform.SetParent(p, false);
            DasUi.Place((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -170f), new Vector2(-32f, -122f));
            DasUi.Row(bar, 0, 0, 10f).childForceExpandHeight = true;
            foreach (LibraryFilter f in new[] { LibraryFilter.All, LibraryFilter.Updates, LibraryFilter.New, LibraryFilter.NotDownloaded, LibraryFilter.OnThisPc })
            {
                LibraryFilter captured = f;
                Button b = DasUi.NewButton("Tab_" + f, bar.transform, f.ToString(), DasUi.ButtonColor, () => SetFilter(captured), 20f);
                DasUi.SetWidth(b.gameObject, f == LibraryFilter.NotDownloaded ? 240f : f == LibraryFilter.OnThisPc ? 200f : 170f);
                _tabs[f] = b;
            }
            var spacer = new GameObject("Spacer", typeof(RectTransform)); spacer.transform.SetParent(bar.transform, false); DasUi.SetFlexible(spacer, 10f);
            var search = DasUi.NewInput("Search", bar.transform, "Search vehicles...", q => { _search = q ?? ""; RebuildList(); });
            DasUi.SetWidth(search.gameObject, 340f);

            // Column headers
            var head = new GameObject("Headers", typeof(RectTransform));
            head.transform.SetParent(p, false);
            DasUi.Place((RectTransform)head.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -216f), new Vector2(-32f, -178f));
            DasUi.Row(head, 14, 16, 14f);
            _allToggle = DasUi.NewToggle("All", head.transform, false, on => SelectVisible(on));
            DasUi.SetWidth(DasUi.NewText("H_Thumb", head.transform, "", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, 80f);
            var hName = DasUi.NewText("H_Name", head.transform, "Vehicle", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft); DasUi.SetFlexible(hName.gameObject, 160f);
            DasUi.SetWidth(DasUi.NewText("H_Maker", head.transform, "Manufacturer", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, 220f);
            DasUi.SetWidth(DasUi.NewText("H_Status", head.transform, "On this PC", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, 360f);
            DasUi.SetWidth(DasUi.NewText("H_Size", head.transform, "Size", 18f, DasUi.Muted, TextAlignmentOptions.MidlineRight).gameObject, 120f);

            // List
            var listHost = new GameObject("ListHost", typeof(RectTransform));
            listHost.transform.SetParent(p, false);
            DasUi.Place((RectTransform)listHost.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 196f), new Vector2(-32f, -222f));
            _content = DasUi.NewScrollList("List", listHost.transform);
            DasUi.Stretch((RectTransform)_content.parent.parent);
            _emptyText = DasUi.NewText("Empty", listHost.transform, "", 22f, DasUi.Muted, TextAlignmentOptions.Center);
            DasUi.Stretch(_emptyText.rectTransform);

            // Selection row
            Button selShown = DasUi.NewButton("SelAll", p, "Select all shown", DasUi.ButtonColor, () => SelectVisible(true), 19f);
            DasUi.Place((RectTransform)selShown.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 134f), new Vector2(232f, 178f));
            Button selUpd = DasUi.NewButton("SelUpdates", p, "Select all updates", DasUi.ButtonColor, SelectAllUpdates, 19f);
            DasUi.Place((RectTransform)selUpd.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(244f, 134f), new Vector2(464f, 178f));
            Button selNone = DasUi.NewButton("SelNone", p, "Clear", DasUi.ButtonColor, () => { _selected.Clear(); RebuildList(); }, 19f);
            DasUi.Place((RectTransform)selNone.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(476f, 134f), new Vector2(586f, 178f));
            _selectionText = DasUi.NewText("Selection", p, "", 20f, Color.white, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(_selectionText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(604f, 134f), new Vector2(-392f, 178f));
            _downloadButton = DasUi.NewButton("Download", p, "Download / update selected", DasUi.AccentColor, StartDownloads);
            DasUi.Place((RectTransform)_downloadButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-372f, 128f), new Vector2(-32f, 184f));

            // Queue progress
            var barBg = DasUi.NewBar("QueueBar", p, DasUi.AccentColor);
            _queueBar = barBg;
            DasUi.Place((RectTransform)barBg.transform.parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 104f), new Vector2(-32f, 114f));
            _queueText = DasUi.NewText("Queue", p, "", 20f, DasUi.Muted, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(_queueText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 40f), new Vector2(-560f, 92f));
            _cancelButton = DasUi.NewButton("Cancel", p, "Cancel remaining", DasUi.ButtonColor, BatchDownloads.CancelRemaining, 19f);
            DasUi.Place((RectTransform)_cancelButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-540f, 44f), new Vector2(-340f, 88f));
            _retryButton = DasUi.NewButton("Retry", p, "Retry failed", DasUi.ButtonColor, BatchDownloads.RetryFailed, 19f);
            DasUi.Place((RectTransform)_retryButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-330f, 44f), new Vector2(-180f, 88f));
            _clearButton = DasUi.NewButton("ClearDone", p, "Clear list", DasUi.ButtonColor, BatchDownloads.ClearFinished, 19f);
            DasUi.Place((RectTransform)_clearButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-170f, 44f), new Vector2(-32f, 88f));
            var hint = DasUi.NewText("Hint", p, "Downloads continue if you close this screen.", 16f, DasUi.Muted, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 12f), new Vector2(-32f, 38f));
        }
    }
}
