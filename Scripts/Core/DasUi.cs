using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>Small helpers for screens built in code (Storage, Download &amp; Update, update notice). Same look everywhere.</summary>
    public static class DasUi
    {
        public static readonly Color PanelColor = new Color(0.10f, 0.11f, 0.14f, 1f);
        public static readonly Color RowColorA = new Color(0.14f, 0.15f, 0.19f, 1f);
        public static readonly Color RowColorB = new Color(0.12f, 0.13f, 0.16f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.66f, 0.74f, 1f);
        public static readonly Color ButtonColor = new Color(0.22f, 0.25f, 0.32f, 1f);
        public static readonly Color AccentColor = new Color(0.55f, 0.08f, 0.10f, 1f);     // app red
        public static readonly Color TabOnColor = new Color(0.55f, 0.08f, 0.10f, 1f);
        public static readonly Color GoodColor = new Color(0.45f, 0.85f, 0.50f, 1f);
        public static readonly Color WarnColor = new Color(1f, 0.62f, 0.20f, 1f);
        public static readonly Color InfoColor = new Color(0.45f, 0.70f, 1f, 1f);

        public static Canvas NewOverlayCanvas(GameObject host, int sortingOrder)
        {
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            host.AddComponent<GraphicRaycaster>();
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static Button NewButton(string name, Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick, float fontSize = 22f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var button = go.AddComponent<Button>();
            button.targetGraphic = img;
            if (onClick != null) button.onClick.AddListener(onClick);
            var text = NewText("Label", go.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        public static void SetLabel(Button b, string label)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null) t.text = label;
        }

        /// <summary>A check box: dark square with a red tick square inside.</summary>
        public static Toggle NewToggle(string name, Transform parent, bool on, UnityEngine.Events.UnityAction<bool> onChanged, float size = 30f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            var check = NewImage("Check", go.transform, AccentColor);
            var crt = check.rectTransform;
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(5f, 5f); crt.offsetMax = new Vector2(-5f, -5f);
            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.isOn = on;
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            SetSize(go, size, size);
            return toggle;
        }

        /// <summary>A one-line text field with a placeholder.</summary>
        public static TMP_InputField NewInput(string name, Transform parent, string placeholder, UnityEngine.Events.UnityAction<string> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);                       // wire everything up before the field wakes up
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.09f, 1f);

            var area = new GameObject("Text Area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(14f, 6f); art.offsetMax = new Vector2(-14f, -6f);
            area.AddComponent<RectMask2D>();

            var ph = NewText("Placeholder", area.transform, placeholder, 22f, Muted, TextAlignmentOptions.MidlineLeft);
            Stretch(ph.rectTransform);
            ph.fontStyle = FontStyles.Italic;
            var text = NewText("Text", area.transform, "", 22f, Color.white, TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (onChanged != null) input.onValueChanged.AddListener(onChanged);
            go.SetActive(true);
            return input;
        }

        /// <summary>A thin progress bar; returns the fill image (set fillAmount).</summary>
        public static Image NewBar(string name, Transform parent, Color fillColor)
        {
            var bg = NewImage(name, parent, new Color(0.05f, 0.06f, 0.08f, 1f));
            var fill = NewImage("Fill", bg.transform, fillColor);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            return fill;
        }

        /// <summary>A vertical scrolling list; returns the content transform rows go into.</summary>
        public static RectTransform NewScrollList(string name, Transform parent, float spacing = 3f)
        {
            var scrollGo = new GameObject(name, typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch((RectTransform)viewport.transform);
            viewport.AddComponent<RectMask2D>();
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var rt = (RectTransform)content.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var v = content.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = rt;
            return rt;
        }

        public static HorizontalLayoutGroup Row(GameObject go, int padL, int padR, float spacing)
        {
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(padL, padR, 4, 4);
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            return h;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        public static void SetWidth(GameObject go, float width)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0f;
        }

        public static void SetSize(GameObject go, float width, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.flexibleWidth = 0f;
            le.preferredHeight = height; le.minHeight = height; le.flexibleHeight = 0f;
        }

        public static void SetFlexible(GameObject go, float minWidth = 100f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f; le.minWidth = minWidth;
        }

        public static void SetPreferredHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
        }

        public static Color ToneColor(VehicleMeasurement.Storage.LabelTone tone)
        {
            switch (tone)
            {
                case VehicleMeasurement.Storage.LabelTone.Good: return GoodColor;
                case VehicleMeasurement.Storage.LabelTone.Warn: return WarnColor;
                case VehicleMeasurement.Storage.LabelTone.Info: return InfoColor;
                default: return Muted;
            }
        }
    }
}
