using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>
    /// Simple modal dialogs built in code (nothing to set up in a scene): a question with two buttons, and a progress
    /// dialog. Used where the app must ask before doing something big (downloading a vehicle) or show real progress.
    /// </summary>
    public class DasDialog : MonoBehaviour
    {
        private TextMeshProUGUI _title, _message, _detail;
        private RectTransform _fill;
        private GameObject _bar;
        private Button _primary, _secondary;
        private Action _onPrimary, _onSecondary;

        // ── public API ───────────────────────────────────────────────────
        /// <summary>Ask a question. Exactly one of the callbacks runs, then the dialog closes.</summary>
        public static DasDialog Confirm(string title, string message, string yes, string no, Action onYes, Action onNo = null)
        {
            DasDialog d = Create();
            d._title.text = title; d._message.text = message; d._detail.text = "";
            d._bar.SetActive(false);
            d.SetButton(d._primary, yes, () => { d.Close(); onYes?.Invoke(); });
            d.SetButton(d._secondary, no, () => { d.Close(); onNo?.Invoke(); });
            return d;
        }

        /// <summary>Show progress. Update it with <see cref="SetProgress"/>; close it with <see cref="Close"/>.</summary>
        public static DasDialog Progress(string title, string message, string buttonLabel = null, Action onButton = null)
        {
            DasDialog d = Create();
            d._title.text = title; d._message.text = message; d._detail.text = "";
            d._bar.SetActive(true);
            d.SetProgress(0f, "");
            if (string.IsNullOrEmpty(buttonLabel)) d._primary.gameObject.SetActive(false);
            else d.SetButton(d._primary, buttonLabel, () => { d.Close(); onButton?.Invoke(); });
            d._secondary.gameObject.SetActive(false);
            return d;
        }

        /// <summary>Just a message with an OK button.</summary>
        public static DasDialog Info(string title, string message, Action onOk = null)
        {
            DasDialog d = Confirm(title, message, "OK", null, onOk);
            d._secondary.gameObject.SetActive(false);
            return d;
        }

        public void SetProgress(float progress01, string detail)
        {
            if (this == null) return;
            _fill.anchorMax = new Vector2(Mathf.Clamp(progress01, 0.02f, 1f), 1f);
            _detail.text = detail ?? "";
        }

        public void SetMessage(string message) { if (this != null) _message.text = message ?? ""; }

        public void Close() { if (this != null) Destroy(gameObject); }

        // ── building ─────────────────────────────────────────────────────
        private static readonly Color Panel = new Color(0.10f, 0.11f, 0.14f, 1f);
        private static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f, 1f);
        private static readonly Color Primary = new Color(0.86f, 0.18f, 0.20f, 1f);
        private static readonly Color Secondary = new Color(0.22f, 0.25f, 0.32f, 1f);

        private static DasDialog Create()
        {
            var go = new GameObject("DasDialog");
            var d = go.AddComponent<DasDialog>();
            d.Build();
            return d;
        }

        private void SetButton(Button b, string label, Action onClick)
        {
            if (string.IsNullOrEmpty(label)) { b.gameObject.SetActive(false); return; }
            b.gameObject.SetActive(true);
            b.GetComponentInChildren<TextMeshProUGUI>().text = label;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => onClick());
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000;                                    // above everything, incl. the loading screen
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            if (EventSystem.current == null && FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            Image dim = NewImage("Dim", transform, new Color(0f, 0f, 0f, 0.65f));
            Stretch(dim.rectTransform);
            Image box = NewImage("Box", dim.transform, Panel);
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = box.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(780f, 340f);

            _title = NewText("Title", box.transform, "", 32f, Color.white, TextAlignmentOptions.TopLeft);
            _title.fontStyle = FontStyles.Bold;
            Place(_title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -76), new Vector2(-36, -28));
            _message = NewText("Message", box.transform, "", 22f, Muted, TextAlignmentOptions.TopLeft);
            Place(_message.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -200), new Vector2(-36, -88));

            Image track = NewImage("Track", box.transform, new Color(0.20f, 0.22f, 0.28f, 1f));
            Place(track.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -224), new Vector2(-36, -212));
            Image fill = NewImage("Fill", track.transform, Primary);
            _fill = fill.rectTransform;
            _fill.anchorMin = Vector2.zero; _fill.anchorMax = new Vector2(0.02f, 1f); _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            _bar = track.gameObject;
            _detail = NewText("Detail", box.transform, "", 19f, Muted, TextAlignmentOptions.TopLeft);
            Place(_detail.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(36, -264), new Vector2(-36, -232));

            _primary = NewButton("Primary", box.transform, Primary);
            Place((RectTransform)_primary.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-276, 26), new Vector2(-36, 76));
            _secondary = NewButton("Secondary", box.transform, Secondary);
            Place((RectTransform)_secondary.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-516, 26), new Vector2(-292, 76));
        }

        private static Image NewImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = c; return img;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            return t;
        }

        private static Button NewButton(string name, Transform parent, Color c)
        {
            Image img = NewImage(name, parent, c);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var label = NewText("Label", img.transform, "", 22f, Color.white, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            return b;
        }

        private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax) { rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = offMin; rt.offsetMax = offMax; }
    }
}
