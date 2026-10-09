using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>
    /// Notifications that slide in at the bottom-right of any screen: "XUV700 is ready - Open", "2 vehicles were
    /// updated on the server - View", "Download failed - Retry". Up to four at once, newest at the bottom; each closes
    /// by itself after a while (longer when it has a button) or with its x. The same notification (same key) is
    /// replaced instead of stacked. Survives scene changes. Built in code; nothing to set up.
    /// </summary>
    public class DasToast : MonoBehaviour
    {
        public enum Tone { Info, Good, Warn, Error }

        private class Entry
        {
            public string key;
            public GameObject go;
            public float closeAt;
            public TextMeshProUGUI title, body;
        }

        private const int MaxShown = 4;
        private const float Width = 520f, Gap = 10f;
        private static DasToast _host;
        private readonly List<Entry> _entries = new List<Entry>();
        private RectTransform _stack;

        /// <summary>Show a notification. <paramref name="action"/> (optional) adds a button; <paramref name="key"/> replaces an
        /// earlier notification with the same key (e.g. one per vehicle). Safe to call from anywhere on the main thread.</summary>
        public static void Show(string title, string message, Tone tone = Tone.Info, string actionLabel = null, Action action = null,
                                string key = null, float seconds = 0f)
        {
            try { Host().Add(title, message, tone, actionLabel, action, key, seconds); }
            catch (Exception e) { Debug.LogWarning("[Toast] " + title + " - " + message + " (" + e.Message + ")"); }
        }

        public static void Dismiss(string key)
        {
            if (_host == null || string.IsNullOrEmpty(key)) return;
            Entry e = _host._entries.Find(x => x.key == key);
            if (e != null) _host.Remove(e);
        }

        private static DasToast Host()
        {
            if (_host != null) return _host;
            var go = new GameObject("DAS Notifications");
            DontDestroyOnLoad(go);
            _host = go.AddComponent<DasToast>();
            DasUi.NewOverlayCanvas(go, 5000);                  // above every DAS screen and dialog
            var stack = new GameObject("Stack", typeof(RectTransform));
            stack.transform.SetParent(go.transform, false);
            _host._stack = (RectTransform)stack.transform;
            _host._stack.anchorMin = _host._stack.anchorMax = _host._stack.pivot = new Vector2(1f, 0f);
            _host._stack.anchoredPosition = new Vector2(-24f, 24f);
            _host._stack.sizeDelta = new Vector2(Width, 10f);
            var layout = stack.AddComponent<VerticalLayoutGroup>();
            layout.spacing = Gap;
            layout.childAlignment = TextAnchor.LowerRight;
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            var fit = stack.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return _host;
        }

        private void OnDestroy() { if (_host == this) _host = null; }

        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].go == null || now >= _entries[i].closeAt) Remove(_entries[i]);
        }

        private static Color ToneColor(Tone t)
        {
            switch (t)
            {
                case Tone.Good: return DasUi.GoodColor;
                case Tone.Warn: return DasUi.WarnColor;
                case Tone.Error: return new Color(0.95f, 0.35f, 0.35f, 1f);
                default: return DasUi.InfoColor;
            }
        }

        private void Add(string title, string message, Tone tone, string actionLabel, Action action, string key, float seconds)
        {
            if (seconds <= 0f) seconds = action != null ? 15f : 7f;
            if (!string.IsNullOrEmpty(key))
            {
                Entry same = _entries.Find(x => x.key == key);
                if (same != null) Remove(same);
            }
            while (_entries.Count >= MaxShown) Remove(_entries[0]);

            var card = DasUi.NewImage("Toast", _stack, new Color(0.12f, 0.13f, 0.17f, 0.98f));
            var entry = new Entry { key = key, go = card.gameObject, closeAt = Time.unscaledTime + seconds };
            var le = card.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = action != null ? 118f : 84f;
            le.preferredWidth = Width;

            var bar = DasUi.NewImage("Tone", card.transform, ToneColor(tone));
            DasUi.Place(bar.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(6f, 0f));

            entry.title = DasUi.NewText("Title", card.transform, title ?? "", 20f, Color.white, TextAlignmentOptions.TopLeft);
            entry.title.fontStyle = FontStyles.Bold;
            DasUi.Place(entry.title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(22f, -38f), new Vector2(-48f, -10f));
            entry.body = DasUi.NewText("Message", card.transform, message ?? "", 17f, DasUi.Muted, TextAlignmentOptions.TopLeft);
            entry.body.overflowMode = TextOverflowModes.Ellipsis;
            DasUi.Place(entry.body.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(22f, action != null ? 52f : 8f), new Vector2(-18f, -40f));

            Button close = DasUi.NewButton("Close", card.transform, "x", new Color(0f, 0f, 0f, 0f), () => Remove(entry), 20f);
            DasUi.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-42f, -40f), new Vector2(-8f, -6f));

            if (action != null)
            {
                Button act = DasUi.NewButton("Action", card.transform, actionLabel ?? "Open", DasUi.AccentColor, () =>
                {
                    Remove(entry);
                    try { action(); }
                    catch (Exception e) { Debug.LogWarning("[Toast] The notification's action failed: " + e.Message); }
                }, 18f);
                DasUi.Place((RectTransform)act.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-170f, 10f), new Vector2(-14f, 48f));
            }
            _entries.Add(entry);
            Debug.Log("[Notification] " + title + (string.IsNullOrEmpty(message) ? "" : " - " + message));
        }

        private void Remove(Entry e)
        {
            _entries.Remove(e);
            if (e.go != null) Destroy(e.go);
        }
    }
}
