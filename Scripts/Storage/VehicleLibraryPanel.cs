using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// "Vehicles" - the one place for everything about vehicles:
    ///  * every vehicle (server, built into the app, and files left over from vehicles the server no longer lists) with
    ///    its thumbnail, maker, state on this PC and size;
    ///  * per row: Open (start measuring; downloads first if needed), Download / Update, Remove from this PC;
    ///  * tick several: Download / update selected, Remove selected;
    ///  * tabs All / Updates / New / Not downloaded / On this PC, and search;
    ///  * bottom: disk use and free space, download folder (change, open, move here, default), remove older versions;
    ///  * download progress for the whole queue (keeps going when the screen is closed).
    /// Replaces the separate Add New Vehicle list, Download &amp; update screen and Storage screen. Opened by the Home
    /// "Vehicles" button, the update notice, Settings > Storage, Ctrl+Shift+D / Ctrl+Shift+S. Built in code.
    /// </summary>
    public class VehicleLibraryPanel : MonoBehaviour
    {
        private static VehicleLibraryPanel _open;

        public static bool IsOpen { get { return _open != null; } }

        public static void Open() { Open(LibraryFilter.All); }

        public static void Open(LibraryFilter filter)
        {
            if (_open != null) { _open.SetFilter(filter); return; }
            var go = new GameObject("VehiclesPanel");
            var p = go.AddComponent<VehicleLibraryPanel>();
            p._filter = filter;
        }

        private class RowUi
        {
            public LibraryItem item;
            public Toggle toggle;
            public Image thumb;
            public TextMeshProUGUI status, size;
            public Button open, second;
        }

        private const float StatusW = 330f, SizeW = 110f, MakerW = 190f, OpenW = 104f, SecondW = 128f;
        private const long LowSpaceWarnBytes = 5L * 1024 * 1024 * 1024;

        private List<LibraryItem> _items = new List<LibraryItem>();
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RowUi> _rows = new Dictionary<string, RowUi>(StringComparer.OrdinalIgnoreCase);
        private LibraryFilter _filter = LibraryFilter.All;
        private string _search = "";
        private bool _dirty, _busy;
        private float _nextQueueRefresh;

        private RectTransform _content;
        private TextMeshProUGUI _summary, _selectionText, _queueText, _emptyText, _diskText, _statusText;
        private Image _queueBar;
        private Button _downloadButton, _removeSelectedButton, _cancelButton, _retryButton, _clearButton;
        private Button _olderButton, _moveHereButton, _defaultFolderButton;
        private readonly Dictionary<LibraryFilter, Button> _tabs = new Dictionary<LibraryFilter, Button>();
        private Toggle _allToggle;
        private bool _settingAll;
        private RemoteAddressableVehicleLoader _thumbSource;

        private void Awake()
        {
            _open = this;
            BuildUi();
            VehicleStorageService.Changed += MarkDirty;
            BatchDownloads.Changed += RefreshQueue;
            _thumbSource = RemoteAddressableVehicleLoader.Instance;
            if (_thumbSource != null)
            {
                _thumbSource.ThumbnailUpdated += OnThumbnail;
                if (_thumbSource.OnCatalogLoaded != null) _thumbSource.OnCatalogLoaded.AddListener(OnCatalogLoaded);
            }
        }

        private void Start()
        {
            Reload();
            UpdateNotifier.MarkSeenNow();     // the user is looking at the list now: "new" is no longer news next time
        }

        private void OnDestroy()
        {
            VehicleStorageService.Changed -= MarkDirty;
            BatchDownloads.Changed -= RefreshQueue;
            if (_thumbSource != null)
            {
                _thumbSource.ThumbnailUpdated -= OnThumbnail;
                if (_thumbSource.OnCatalogLoaded != null) _thumbSource.OnCatalogLoaded.RemoveListener(OnCatalogLoaded);
            }
            if (_open == this) _open = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !_busy) { Close(); return; }
            if (_dirty && !_busy) { _dirty = false; Reload(); }
            if (BatchDownloads.Running && Time.unscaledTime >= _nextQueueRefresh)
            {
                _nextQueueRefresh = Time.unscaledTime + 0.25f;
                RefreshQueue();
            }
        }

        private void MarkDirty() { _dirty = true; }
        private void OnCatalogLoaded(int count) { _dirty = true; }
        private void Close() { Destroy(gameObject); }

        // ── data ─────────────────────────────────────────────────────────

        private void Reload()
        {
            _items = UpdateNotifier.BuildItems(false);
            var ids = new HashSet<string>(_items.Select(i => i.vehicleId), StringComparer.OrdinalIgnoreCase);

            // Files on this PC for vehicles the server no longer lists: shown so they can be removed
            var service = VehicleStorageService.Instance;
            if (service != null && service.IsReady)
                foreach (StorageRow r in service.GetRows())
                    if (r != null && !string.IsNullOrEmpty(r.vehicleId) && ids.Add(r.vehicleId))
                        _items.Add(new LibraryItem
                        {
                            kind = LibraryKind.Orphan, vehicleId = r.vehicleId, name = r.name, known = true, downloaded = true,
                            totalBytes = r.bytes, label = "No longer on the server", tone = LabelTone.Warn,
                        });

            // Vehicles built into the app
            foreach (LocalVehicleIndex.Entry e in LocalVehicleIndex.Get("Vehicles"))
                if (e != null && ids.Add("local:" + e.resourcePath))
                    _items.Add(new LibraryItem
                    {
                        kind = LibraryKind.Local, vehicleId = "local:" + e.resourcePath, name = e.name, manufacturer = "Built in",
                        localPath = e.resourcePath, known = true, downloaded = true,
                    });

            foreach (LibraryItem i in _items)
                if (i.kind != LibraryKind.Local)
                {
                    try { i.hasMeasurements = VehicleMeasurementStorage.Exists(i.vehicleId); } catch (Exception) { }
                }

            var selectable = new HashSet<string>(_items.Where(i => i.Actionable || i.Removable).Select(i => i.vehicleId), StringComparer.OrdinalIgnoreCase);
            _selected.RemoveWhere(id => !selectable.Contains(id));
            RebuildList();
        }

        private void SetFilter(LibraryFilter f)
        {
            _filter = f;
            RebuildList();
        }

        private LibraryItem Item(string id) { return _items.FirstOrDefault(i => string.Equals(i.vehicleId, id, StringComparison.OrdinalIgnoreCase)); }

        // ── list ─────────────────────────────────────────────────────────

        private void RebuildList()
        {
            if (_content == null) return;
            foreach (Transform child in _content) Destroy(child.gameObject);
            _rows.Clear();

            LibrarySummary sum = VehicleLibrary.Summarize(_items);
            var loader = RemoteAddressableVehicleLoader.Instance;
            bool storageReady = VehicleStorageService.Instance != null && VehicleStorageService.Instance.IsReady;
            string serverPart = loader == null || !loader.IsCatalogLoaded
                ? (loader != null && loader.IsCatalogFailed ? "Server list not available (offline?)" : "Loading the server list...")
                : sum.updates + " need an update  ·  " + sum.notDownloaded + " not downloaded" + (sum.isNew > 0 ? "  ·  " + sum.isNew + " new" : "");
            _summary.text = sum.downloaded + " on this PC  ·  " + serverPart + (storageReady ? "" : "   (checking this PC...)");

            int onPc = _items.Count(i => VehicleLibrary.Matches(i, LibraryFilter.OnThisPc, ""));
            SetTab(LibraryFilter.All, "All (" + sum.total + ")");
            SetTab(LibraryFilter.Updates, "Updates (" + sum.updates + ")");
            SetTab(LibraryFilter.New, "New (" + sum.isNew + ")");
            SetTab(LibraryFilter.NotDownloaded, "Not downloaded (" + sum.notDownloaded + ")");
            SetTab(LibraryFilter.OnThisPc, "On this PC (" + onPc + ")");

            List<LibraryItem> visible = VehicleLibrary.Sorted(_items.Where(i => VehicleLibrary.Matches(i, _filter, _search)));
            _emptyText.gameObject.SetActive(visible.Count == 0);
            _emptyText.text = _items.Count == 0 ? "Loading vehicles..." :
                _filter == LibraryFilter.Updates ? "All vehicles on this PC are up to date." :
                _filter == LibraryFilter.New ? "No new vehicles since you last looked." :
                _filter == LibraryFilter.OnThisPc ? "No vehicles are downloaded on this PC." : "Nothing matches.";

            for (int i = 0; i < visible.Count; i++) BuildRow(visible[i], i % 2 == 0 ? DasUi.RowColorA : DasUi.RowColorB);
            RefreshSelection();
            RefreshQueue();
            RefreshDisk();
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
            DasUi.SetPreferredHeight(go, 60f);
            DasUi.Row(go, 14, 14, 12f);

            var row = new RowUi { item = item };
            string id = item.vehicleId;
            row.toggle = DasUi.NewToggle("Select", go.transform, _selected.Contains(id), on =>
            {
                if (on) _selected.Add(id); else _selected.Remove(id);
                RefreshSelection();
            });

            row.thumb = DasUi.NewImage("Thumb", go.transform, new Color(0f, 0f, 0f, 0.35f));
            row.thumb.preserveAspect = true;
            DasUi.SetSize(row.thumb.gameObject, 80f, 46f);
            var live = RemoteAddressableVehicleLoader.Instance;
            Sprite s = live != null && item.kind == LibraryKind.Server ? live.GetThumbnail(id) : null;
            if (s != null) { row.thumb.sprite = s; row.thumb.color = Color.white; }

            string extra = (item.isNew ? "   <color=#6FB8FF>NEW</color>" : "") + (item.hasMeasurements ? "   <color=#9AA3B5><size=80%>measured</size></color>" : "");
            var name = DasUi.NewText("Name", go.transform, item.name + extra, 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            DasUi.SetFlexible(name.gameObject, 150f);
            DasUi.SetWidth(DasUi.NewText("Maker", go.transform, item.manufacturer, 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, MakerW);

            row.status = DasUi.NewText("Status", go.transform, "", 19f, DasUi.Muted, TextAlignmentOptions.MidlineLeft);
            DasUi.SetWidth(row.status.gameObject, StatusW);
            row.size = DasUi.NewText("Size", go.transform, "", 19f, Color.white, TextAlignmentOptions.MidlineRight);
            DasUi.SetWidth(row.size.gameObject, SizeW);

            row.open = DasUi.NewButton("Open", go.transform, "Open", DasUi.AccentColor, () => AskOpen(id), 19f);
            DasUi.SetSize(row.open.gameObject, OpenW, 42f);
            row.second = DasUi.NewButton("Second", go.transform, "", DasUi.ButtonColor, () => SecondAction(id), 19f);
            DasUi.SetSize(row.second.gameObject, SecondW, 42f);

            // click on the row (not on a button) to tick it
            var click = go.AddComponent<Button>();
            click.transition = Selectable.Transition.None;
            click.onClick.AddListener(() => { if (row.toggle.interactable) row.toggle.isOn = !row.toggle.isOn; });

            _rows[id] = row;
            RefreshRow(row);
        }

        private void RefreshRow(RowUi row)
        {
            LibraryItem i = row.item;
            BatchDownloads.Job job = i.kind == LibraryKind.Server ? BatchDownloads.Find(i.vehicleId) : null;
            bool busyJob = job != null && (job.state == BatchDownloads.JobState.Downloading || job.state == BatchDownloads.JobState.Waiting);
            string text; Color color;
            if (i.kind == LibraryKind.Local) { text = "Built into the app"; color = DasUi.GoodColor; }
            else if (i.kind == LibraryKind.Orphan) { text = "No longer on the server"; color = DasUi.WarnColor; }
            else if (job != null && job.state == BatchDownloads.JobState.Downloading)
            {
                text = job.progress != null && job.progress.total > 0
                    ? "Downloading " + Mathf.RoundToInt(job.progress.fraction * 100f) + "%  (" + ByteFormat.Format(job.progress.downloaded) + ")"
                    : "Downloading...";
                color = DasUi.InfoColor;
            }
            else if (job != null && job.state == BatchDownloads.JobState.Waiting) { text = "Queued"; color = DasUi.InfoColor; }
            else if (job != null && job.state == BatchDownloads.JobState.Failed) { text = "Failed: " + job.message; color = DasUi.WarnColor; }
            else if (!i.known) { text = "Checking this PC..."; color = DasUi.Muted; }
            else if (i.needsUpdate) { text = "Update needed"; color = DasUi.WarnColor; }
            else if (i.downloaded) { text = "On this PC"; color = DasUi.GoodColor; }
            else { text = i.changedOnServer ? "Not downloaded (updated on server)" : "Not downloaded"; color = DasUi.Muted; }
            row.status.text = text;
            row.status.color = color;

            long size = i.kind == LibraryKind.Server && (i.needsUpdate || !i.downloaded) ? (i.downloadBytes > 0 ? i.downloadBytes : i.totalBytes) : i.totalBytes;
            row.size.text = size > 0 ? ByteFormat.Format(size) : "";

            // second button: Download / Update / Remove / (nothing)
            string second = null;
            if (i.kind == LibraryKind.Server && !busyJob && i.known)
                second = i.needsUpdate ? "Update" : !i.downloaded ? "Download" : "Remove";
            else if (i.kind == LibraryKind.Orphan) second = "Remove";
            else if (busyJob) second = "Queued";
            row.second.gameObject.SetActive(second != null);
            if (second != null)
            {
                DasUi.SetLabel(row.second, second);
                row.second.interactable = !_busy && !busyJob;
                row.second.GetComponent<Image>().color = second == "Update" || second == "Download" ? DasUi.InfoButtonColor : DasUi.ButtonColor;
            }
            row.open.gameObject.SetActive(i.Openable);
            row.open.interactable = !_busy;

            bool selectable = (i.Actionable || i.Removable) && !busyJob && !_busy;
            row.toggle.interactable = selectable;
            row.toggle.gameObject.SetActive(i.kind != LibraryKind.Local);
            if (!selectable && row.toggle.isOn) { _selected.Remove(i.vehicleId); row.toggle.SetIsOnWithoutNotify(false); }
        }

        private void OnThumbnail(string vehicleId, Sprite sprite)
        {
            RowUi row;
            if (sprite != null && _rows.TryGetValue(vehicleId, out row) && row.thumb != null) { row.thumb.sprite = sprite; row.thumb.color = Color.white; }
        }

        // ── per-vehicle actions ──────────────────────────────────────────

        private void AskOpen(string id)
        {
            LibraryItem i = Item(id);
            if (i == null || _busy) return;
            if (i.kind == LibraryKind.Server && i.known && (!i.downloaded || i.needsUpdate))
            {
                long bytes = i.downloadBytes > 0 ? i.downloadBytes : i.totalBytes;
                string space = DiskSpace.ProblemFor(bytes);
                if (space != null) { DasDialog.Info("Not enough disk space", space); return; }
                DasDialog.Confirm((i.needsUpdate ? "Update and open " : "Download and open ") + i.name + "?",
                    (i.needsUpdate ? "A newer version is on the server" : "This vehicle isn't on this PC yet") +
                    (bytes > 0 ? " - about " + ByteFormat.Format(bytes) + " to download. " : ". ") + "It opens as soon as the download finishes.",
                    i.needsUpdate ? "Update and open" : "Download and open", "Cancel", () => OpenNow(i));
                return;
            }
            OpenNow(i);
        }

        private void OpenNow(LibraryItem i)
        {
            var dm = VehicleDataManager.Instance;
            if (dm == null) { DasDialog.Info("Can't open", "The app isn't ready yet. Try again in a moment."); return; }
            UpdateNotifier.MarkVehicleSeen(i);
            Close();
            if (i.hasMeasurements)
            {
                dm.GoToMeasurement(i.vehicleId);                  // saved measurements: open them
            }
            else if (i.kind == LibraryKind.Local)
            {
                dm.SetSelectedLocalModel(i.localPath);
                dm.GoToMeasurementNew();
            }
            else
            {
                dm.SetSelectedModel(i.addressableKey, i.addressableKey);
                dm.GoToMeasurementNew();
            }
        }

        private void SecondAction(string id)
        {
            LibraryItem i = Item(id);
            if (i == null || _busy) return;
            if (i.kind == LibraryKind.Server && i.known && (!i.downloaded || i.needsUpdate)) { Queue(new List<LibraryItem> { i }); return; }
            if (i.Removable) AskRemove(new List<LibraryItem> { i });
        }

        // ── selection ────────────────────────────────────────────────────

        private List<LibraryItem> Selected() { return _items.Where(i => _selected.Contains(i.vehicleId)).ToList(); }

        private void RefreshSelection()
        {
            List<LibraryItem> chosen = Selected();
            var toGet = chosen.Where(i => i.Actionable).ToList();
            var toRemove = chosen.Where(i => i.Removable).ToList();
            long bytes = VehicleLibrary.BytesFor(toGet);
            long freed = toRemove.Sum(i => Math.Max(0, i.totalBytes));
            if (chosen.Count == 0) _selectionText.text = "Tick vehicles to download, update or remove several at once.";
            else
            {
                var parts = new List<string> { chosen.Count + " selected" };
                if (toGet.Count > 0) parts.Add(toGet.Count + " to download/update (" + ByteFormat.Format(bytes) + ")");
                if (toRemove.Count > 0) parts.Add(toRemove.Count + " on this PC (" + ByteFormat.Format(freed) + ")");
                _selectionText.text = string.Join("  ·  ", parts);
            }
            _downloadButton.interactable = toGet.Count > 0 && !_busy;
            DasUi.SetLabel(_downloadButton, toGet.Count > 0 ? "Download / update " + toGet.Count : "Download / update");
            _removeSelectedButton.interactable = toRemove.Count > 0 && !_busy;
            DasUi.SetLabel(_removeSelectedButton, toRemove.Count > 0 ? "Remove " + toRemove.Count : "Remove");

            var visibleSelectable = _rows.Values.Where(r => r.toggle.interactable && r.toggle.gameObject.activeSelf).ToList();
            _settingAll = true;
            if (_allToggle != null) _allToggle.SetIsOnWithoutNotify(visibleSelectable.Count > 0 && visibleSelectable.All(r => _selected.Contains(r.item.vehicleId)));
            _settingAll = false;
        }

        private void SelectVisible(bool on)
        {
            if (_settingAll) return;
            foreach (RowUi r in _rows.Values)
            {
                if (!r.toggle.interactable || !r.toggle.gameObject.activeSelf) continue;
                if (on) _selected.Add(r.item.vehicleId); else _selected.Remove(r.item.vehicleId);
                r.toggle.SetIsOnWithoutNotify(on);
            }
            RefreshSelection();
        }

        private void SelectAllUpdates()
        {
            _selected.Clear();
            foreach (LibraryItem i in _items.Where(i => i.kind == LibraryKind.Server && i.known && i.needsUpdate && !BatchDownloads.IsQueued(i.vehicleId)))
                _selected.Add(i.vehicleId);
            _filter = LibraryFilter.Updates;
            RebuildList();
        }

        // ── download / remove ────────────────────────────────────────────

        private void DownloadSelected() { Queue(Selected().Where(i => i.Actionable).ToList()); }

        private void Queue(List<LibraryItem> items)
        {
            if (items.Count == 0) return;
            long bytes = VehicleLibrary.BytesFor(items);
            string space = BatchDownloads.SpaceProblem(bytes);
            if (space != null)
            {
                DasDialog.Info("Not enough disk space", space + "\n\nNeeded: " + ByteFormat.Format(bytes) +
                               ". Choose fewer vehicles, remove some, or change the download folder (bottom of this screen).");
                return;
            }
            BatchDownloads.Enqueue(items);
            foreach (LibraryItem i in items) { UpdateNotifier.MarkVehicleSeen(i); _selected.Remove(i.vehicleId); }
            RebuildList();
        }

        private void RemoveSelected() { AskRemove(Selected().Where(i => i.Removable).ToList()); }

        private void AskRemove(List<LibraryItem> items)
        {
            if (items.Count == 0 || _busy) return;
            long bytes = items.Sum(i => Math.Max(0, i.totalBytes));
            string what = items.Count == 1 ? items[0].name : items.Count + " vehicles";
            DasDialog.Confirm("Remove " + what + " from this PC?",
                "Deletes the downloaded model files" + (bytes > 0 ? " (about " + ByteFormat.Format(bytes) + ")" : "") + ". " +
                "Files other vehicles share are kept. Saved measurements are NOT deleted. " +
                (items.Any(i => i.kind == LibraryKind.Orphan) ? "Vehicles no longer on the server can't be downloaded again." : "You can download again any time."),
                "Remove", "Cancel", () => StartCoroutine(RemoveRoutine(items)));
        }

        private IEnumerator RemoveRoutine(List<LibraryItem> items)
        {
            var service = VehicleStorageService.Instance;
            if (service == null) yield break;
            SetBusy(true, "Removing...");
            long freed = 0; int ok = 0; var problems = new List<string>();
            for (int n = 0; n < items.Count; n++)
            {
                LibraryItem i = items[n];
                SetStatus("Removing " + (n + 1) + " of " + items.Count + ": " + i.name + "...");
                RemoveOutcome outcome = null;
                yield return service.RemoveVehicleRoutine(i.vehicleId, o => outcome = o);
                if (outcome != null && outcome.success) { ok++; freed += outcome.freedBytes; }
                else problems.Add(i.name + (outcome != null && !string.IsNullOrEmpty(outcome.message) ? ": " + outcome.message : ""));
                _selected.Remove(i.vehicleId);
            }
            SetBusy(false, "Removed " + ok + " vehicle(s), freed " + ByteFormat.Format(freed) + "." +
                           (problems.Count > 0 ? "  Not removed: " + string.Join("; ", problems) : ""));
            Reload();
        }

        // ── queue ────────────────────────────────────────────────────────

        private void RefreshQueue()
        {
            if (_queueText == null) return;
            _queueText.text = BatchDownloads.Describe();
            bool any = BatchDownloads.Jobs.Count > 0;
            _queueBar.transform.parent.gameObject.SetActive(any);
            _queueBar.fillAmount = BatchDownloads.OverallFraction();
            _cancelButton.gameObject.SetActive(BatchDownloads.Running && BatchDownloads.WaitingCount > 0);
            _retryButton.gameObject.SetActive(!BatchDownloads.Running && BatchDownloads.Jobs.Any(j => j.state == BatchDownloads.JobState.Failed || j.state == BatchDownloads.JobState.Cancelled));
            _clearButton.gameObject.SetActive(!BatchDownloads.Running && any);
            foreach (RowUi r in _rows.Values) RefreshRow(r);
        }

        // ── disk & folder ────────────────────────────────────────────────

        private void RefreshDisk()
        {
            var service = VehicleStorageService.Instance;
            string folder = DownloadLocation.CurrentFolder ?? "(unknown)";
            long free = DiskSpace.FreeBytes(folder);
            string used = service != null && service.IsReady
                ? service.GetSummary().downloadedCount + " vehicle(s) use " + ByteFormat.Format(service.GetSummary().downloadedBytes)
                : "Checking this PC...";
            string freeText = free >= 0 ? ByteFormat.Format(free) + " free on " + DiskSpace.DriveName(folder) : "free space unknown";
            bool low = free >= 0 && free < LowSpaceWarnBytes;
            _diskText.text = used + "  ·  " + freeText + (low ? "  ·  LOW DISK SPACE" : "") + "\nDownload folder: " + folder + (DownloadLocation.IsCustom ? "  (your choice)" : "  (default)");
            _diskText.color = low ? DasUi.WarnColor : DasUi.Muted;

            long older = service != null ? service.OlderCopiesBytes : 0;
            _olderButton.gameObject.SetActive(older > 0);
            DasUi.SetLabel(_olderButton, "Remove older versions (" + ByteFormat.Format(older) + ")");
            long elsewhere = DownloadLocation.BytesElsewhere();
            _moveHereButton.gameObject.SetActive(elsewhere > 0);
            DasUi.SetLabel(_moveHereButton, "Move downloads here (" + ByteFormat.Format(elsewhere) + ")");
            _defaultFolderButton.gameObject.SetActive(DownloadLocation.IsCustom);
        }

        private void ChangeFolder()
        {
            if (_busy) return;
            string picked = WindowsFolderPicker.Pick("Choose where vehicle downloads are saved");
            if (string.IsNullOrEmpty(picked)) return;
            string error = DownloadLocation.SetFolder(picked);
            if (error != null) { SetStatus("Can't use that folder: " + error); return; }
            long elsewhere = DownloadLocation.BytesElsewhere();
            SetStatus("New downloads now go to " + DownloadLocation.CurrentFolder + "."
                      + (elsewhere > 0 ? " Vehicles already downloaded still work from where they are; 'Move downloads here' moves them." : ""));
            RefreshDisk();
        }

        private void UseDefaultFolder()
        {
            if (_busy) return;
            string error = DownloadLocation.SetFolder(null);
            SetStatus(error == null ? "New downloads go to the default folder again." : "Couldn't switch back: " + error);
            RefreshDisk();
        }

        private void OpenFolder()
        {
            string folder = DownloadLocation.CurrentFolder;
            if (!string.IsNullOrEmpty(folder)) Application.OpenURL("file:///" + folder.Replace('\\', '/'));
        }

        private void AskMoveHere()
        {
            if (_busy) return;
            DasDialog.Confirm("Move downloads here?",
                "Moves " + ByteFormat.Format(DownloadLocation.BytesElsewhere()) + " of downloaded vehicle files into " + DownloadLocation.CurrentFolder + ". "
                + "Nothing is deleted until everything is copied. Close any open vehicle first. Restart DAS afterwards.",
                "Move", "Cancel", () => StartCoroutine(MoveHere()));
        }

        private IEnumerator MoveHere()
        {
            SetBusy(true, "Moving downloads...");
            string message = null;
            yield return DownloadLocation.MoveDownloadsHere((p, status) => SetStatus(status), (o, m) => { message = m; });
            SetBusy(false, message);
            Reload();
        }

        private void AskRemoveOlder()
        {
            var service = VehicleStorageService.Instance;
            if (_busy || service == null || !service.IsReady) return;
            DasDialog.Confirm("Remove older versions?",
                "Earlier versions of vehicle files take " + ByteFormat.Format(service.OlderCopiesBytes) + ". The app always opens the current version, "
                + "so they only take up space. Current files and saved measurements are not touched.",
                "Remove older versions", "Cancel", () => StartCoroutine(RemoveOlder()));
        }

        private IEnumerator RemoveOlder()
        {
            SetBusy(true, "Removing older versions...");
            RemoveOutcome outcome = null;
            yield return VehicleStorageService.Instance.RemoveOlderCopiesRoutine(o => outcome = o);
            SetBusy(false, outcome != null ? outcome.message : "Done.");
            Reload();
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            SetStatus(status);
            foreach (RowUi r in _rows.Values) RefreshRow(r);
            RefreshSelection();
            _olderButton.interactable = !busy; _moveHereButton.interactable = !busy; _defaultFolderButton.interactable = !busy;
        }

        private void SetStatus(string s) { if (_statusText != null) _statusText.text = s ?? ""; }

        // ── building the screen ──────────────────────────────────────────

        private void BuildUi()
        {
            DasUi.NewOverlayCanvas(gameObject, 4100);

            var dim = DasUi.NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.7f));
            DasUi.Stretch(dim.rectTransform);

            var panel = DasUi.NewImage("Panel", dim.transform, DasUi.PanelColor);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(1720f, 1000f);
            Transform p = panel.transform;

            var title = DasUi.NewText("Title", p, "Vehicle Library   <size=55%><color=#9AA3B5>Open a vehicle to measure it  ·  download, update or remove vehicles</color></size>", 40f, Color.white, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            DasUi.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -74f), new Vector2(-200f, -20f));
            Button close = DasUi.NewButton("Close", p, "Close", DasUi.ButtonColor, Close);
            DasUi.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-172f, -70f), new Vector2(-32f, -24f));

            _summary = DasUi.NewText("Summary", p, "", 20f, DasUi.Muted, TextAlignmentOptions.TopLeft);
            DasUi.Place(_summary.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -110f), new Vector2(-32f, -78f));

            // Tabs + search
            var bar = new GameObject("Filters", typeof(RectTransform));
            bar.transform.SetParent(p, false);
            DasUi.Place((RectTransform)bar.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -168f), new Vector2(-32f, -120f));
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
            DasUi.SetWidth(search.gameObject, 360f);

            // Column headers
            var head = new GameObject("Headers", typeof(RectTransform));
            head.transform.SetParent(p, false);
            DasUi.Place((RectTransform)head.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -212f), new Vector2(-32f, -176f));
            DasUi.Row(head, 14, 14, 12f);
            _allToggle = DasUi.NewToggle("All", head.transform, false, on => SelectVisible(on));
            DasUi.SetWidth(DasUi.NewText("H_Thumb", head.transform, "", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, 80f);
            var hName = DasUi.NewText("H_Name", head.transform, "Vehicle", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft); DasUi.SetFlexible(hName.gameObject, 150f);
            DasUi.SetWidth(DasUi.NewText("H_Maker", head.transform, "Manufacturer", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, MakerW);
            DasUi.SetWidth(DasUi.NewText("H_Status", head.transform, "On this PC", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, StatusW);
            DasUi.SetWidth(DasUi.NewText("H_Size", head.transform, "Size", 18f, DasUi.Muted, TextAlignmentOptions.MidlineRight).gameObject, SizeW);
            DasUi.SetWidth(DasUi.NewText("H_Actions", head.transform, "", 18f, DasUi.Muted, TextAlignmentOptions.MidlineLeft).gameObject, OpenW + SecondW + 12f);

            // List
            var listHost = new GameObject("ListHost", typeof(RectTransform));
            listHost.transform.SetParent(p, false);
            DasUi.Place((RectTransform)listHost.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(32f, 300f), new Vector2(-32f, -218f));
            _content = DasUi.NewScrollList("List", listHost.transform);
            DasUi.Stretch((RectTransform)_content.parent.parent);
            _emptyText = DasUi.NewText("Empty", listHost.transform, "", 22f, DasUi.Muted, TextAlignmentOptions.Center);
            DasUi.Stretch(_emptyText.rectTransform);

            // Selection actions
            Button selShown = DasUi.NewButton("SelAll", p, "Select all shown", DasUi.ButtonColor, () => SelectVisible(true), 19f);
            DasUi.Place((RectTransform)selShown.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 240f), new Vector2(222f, 284f));
            Button selUpd = DasUi.NewButton("SelUpdates", p, "Select all updates", DasUi.ButtonColor, SelectAllUpdates, 19f);
            DasUi.Place((RectTransform)selUpd.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(232f, 240f), new Vector2(442f, 284f));
            Button selNone = DasUi.NewButton("SelNone", p, "Clear", DasUi.ButtonColor, () => { _selected.Clear(); RebuildList(); }, 19f);
            DasUi.Place((RectTransform)selNone.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(452f, 240f), new Vector2(552f, 284f));
            _selectionText = DasUi.NewText("Selection", p, "", 19f, Color.white, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(_selectionText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(568f, 240f), new Vector2(-480f, 284f));
            _removeSelectedButton = DasUi.NewButton("RemoveSel", p, "Remove", DasUi.ButtonColor, RemoveSelected, 19f);
            DasUi.Place((RectTransform)_removeSelectedButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-466f, 236f), new Vector2(-322f, 288f));
            _downloadButton = DasUi.NewButton("Download", p, "Download / update", DasUi.AccentColor, DownloadSelected, 20f);
            DasUi.Place((RectTransform)_downloadButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-310f, 236f), new Vector2(-32f, 288f));

            // Queue progress
            _queueBar = DasUi.NewBar("QueueBar", p, DasUi.AccentColor);
            DasUi.Place((RectTransform)_queueBar.transform.parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 218f), new Vector2(-32f, 226f));
            _queueText = DasUi.NewText("Queue", p, "", 19f, DasUi.Muted, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(_queueText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 166f), new Vector2(-520f, 210f));
            _cancelButton = DasUi.NewButton("Cancel", p, "Cancel remaining", DasUi.ButtonColor, BatchDownloads.CancelRemaining, 18f);
            DasUi.Place((RectTransform)_cancelButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-500f, 168f), new Vector2(-310f, 208f));
            _retryButton = DasUi.NewButton("Retry", p, "Retry failed", DasUi.ButtonColor, BatchDownloads.RetryFailed, 18f);
            DasUi.Place((RectTransform)_retryButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-300f, 168f), new Vector2(-170f, 208f));
            _clearButton = DasUi.NewButton("ClearDone", p, "Clear list", DasUi.ButtonColor, BatchDownloads.ClearFinished, 18f);
            DasUi.Place((RectTransform)_clearButton.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-160f, 168f), new Vector2(-32f, 208f));

            // Storage strip
            var strip = DasUi.NewImage("Storage", p, new Color(0.08f, 0.09f, 0.11f, 1f));
            DasUi.Place(strip.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 52f), new Vector2(-32f, 154f));
            _diskText = DasUi.NewText("Disk", strip.transform, "", 18f, DasUi.Muted, TextAlignmentOptions.TopLeft);
            DasUi.Place(_diskText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(18f, 10f), new Vector2(-700f, -10f));
            var sb = new GameObject("StorageButtons", typeof(RectTransform));
            sb.transform.SetParent(strip.transform, false);
            DasUi.Place((RectTransform)sb.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-690f, 8f), new Vector2(-14f, -8f));
            var grid = sb.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(220f, 38f);
            grid.spacing = new Vector2(8f, 8f);
            grid.childAlignment = TextAnchor.MiddleRight;
            grid.startCorner = GridLayoutGroup.Corner.UpperRight;
            DasUi.NewButton("ChangeFolder", sb.transform, "Change folder...", DasUi.ButtonColor, ChangeFolder, 17f);
            DasUi.NewButton("OpenFolder", sb.transform, "Open folder", DasUi.ButtonColor, OpenFolder, 17f);
            _defaultFolderButton = DasUi.NewButton("DefaultFolder", sb.transform, "Use default folder", DasUi.ButtonColor, UseDefaultFolder, 17f);
            _olderButton = DasUi.NewButton("Older", sb.transform, "Remove older versions", DasUi.ButtonColor, AskRemoveOlder, 17f);
            _moveHereButton = DasUi.NewButton("MoveHere", sb.transform, "Move downloads here", DasUi.ButtonColor, AskMoveHere, 17f);

            _statusText = DasUi.NewText("Status", p, "", 18f, Color.white, TextAlignmentOptions.MidlineLeft);
            DasUi.Place(_statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 12f), new Vector2(-860f, 46f));
            var hint = DasUi.NewText("Hint", p, "Removing a vehicle never deletes its saved measurements.  ·  Downloads continue if you close this screen.", 16f, DasUi.Muted, TextAlignmentOptions.MidlineRight);
            DasUi.Place(hint.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-840f, 12f), new Vector2(-32f, 46f));
        }
    }
}
