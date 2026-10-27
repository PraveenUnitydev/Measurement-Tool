using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>
    /// A search box at the top of a TMP_Dropdown's list: type to narrow the list, Enter picks the first match, Esc
    /// closes. It only hides list items - the dropdown's options, their order and indexes don't change, so the code that
    /// reads <c>dropdown.value</c> works as before.
    ///
    /// Add with <see cref="Attach"/> (e.g. in a controller's Start) or as a component on the dropdown.
    /// Works with the standard TMP_Dropdown template (items positioned by the dropdown) and with templates whose
    /// Content has a layout group.
    /// </summary>
    [RequireComponent(typeof(TMP_Dropdown))]
    public class DropdownSearch : MonoBehaviour
    {
        public string placeholder = "Search vehicles...";
        [Tooltip("Option 0 is a placeholder like '-- Select Vehicle --': hide it while searching.")]
        public bool firstOptionIsPlaceholder = true;
        public float searchHeight = 38f;

        private TMP_Dropdown _dropdown;
        private RectTransform _list;            // the open list ("Dropdown List"), made by the dropdown each time it opens
        private TMP_InputField _input;
        private TextMeshProUGUI _empty;
        private readonly List<RectTransform> _items = new List<RectTransform>();
        private readonly List<string> _labels = new List<string>();
        private readonly List<float> _originalY = new List<float>();
        private RectTransform _content;
        private bool _contentHasLayout;
        private float _step, _bottom, _extra;

        /// <summary>Adds a search box to a dropdown's list (once).</summary>
        public static DropdownSearch Attach(TMP_Dropdown dropdown, string placeholderText = "Search vehicles...")
        {
            if (dropdown == null) return null;
            DropdownSearch s = dropdown.GetComponent<DropdownSearch>();
            if (s == null) s = dropdown.gameObject.AddComponent<DropdownSearch>();
            s.placeholder = placeholderText;
            return s;
        }

        private void Awake() { _dropdown = GetComponent<TMP_Dropdown>(); }

        private void LateUpdate()
        {
            if (_dropdown == null) return;
            if (!_dropdown.IsExpanded) { _list = null; return; }
            if (_list != null) { HandleKeys(); return; }
            RectTransform list = FindList();
            if (list != null) Decorate(list);
        }

        private RectTransform FindList()
        {
            if (_dropdown.template == null) return null;
            Transform parent = _dropdown.template.parent;
            Transform t = parent != null ? parent.Find("Dropdown List") : null;
            return t != null && t.gameObject.activeInHierarchy ? (RectTransform)t : null;
        }

        // ── building the search box into the open list ──
        private void Decorate(RectTransform list)
        {
            _list = list;
            var scroll = list.GetComponent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            _content = scroll.content;
            _contentHasLayout = _content.GetComponent<LayoutGroup>() != null;

            // The items, in option order (their order under Content); the inactive one is the template copy
            _items.Clear(); _labels.Clear(); _originalY.Clear();
            foreach (Transform child in _content)
            {
                if (!child.gameObject.activeSelf || child.GetComponent<Toggle>() == null) continue;
                var rt = (RectTransform)child;
                TMP_Text label = child.GetComponentInChildren<TMP_Text>(true);
                _items.Add(rt);
                _labels.Add(Plain(label != null ? label.text : child.name));
                _originalY.Add(rt.anchoredPosition.y);
            }
            if (_items.Count == 0) return;
            _step = _items.Count > 1 ? Mathf.Abs(_originalY[0] - _originalY[1]) : _items[0].rect.height;
            if (_step < 1f) _step = Mathf.Max(1f, _items[0].rect.height);
            _bottom = float.MaxValue;
            foreach (float y in _originalY) _bottom = Mathf.Min(_bottom, y);
            _extra = _content.sizeDelta.y - _step * _items.Count;

            // Make room at the top of the list for the box
            list.sizeDelta = new Vector2(list.sizeDelta.x, list.sizeDelta.y + searchHeight);
            if (scroll.viewport != null) scroll.viewport.offsetMax = new Vector2(scroll.viewport.offsetMax.x, scroll.viewport.offsetMax.y - searchHeight);
            if (scroll.verticalScrollbar != null)
            {
                var sb = (RectTransform)scroll.verticalScrollbar.transform;
                sb.offsetMax = new Vector2(sb.offsetMax.x, sb.offsetMax.y - searchHeight);
            }

            _input = BuildInput(list);
            _input.onValueChanged.AddListener(Filter);
            _input.onSubmit.AddListener(_ => PickFirst());
            _empty = BuildLabel(scroll.viewport != null ? scroll.viewport : list, "No matches", 0.55f);
            _empty.gameObject.SetActive(false);

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_input.gameObject);
            _input.ActivateInputField();
        }

        private TMP_InputField BuildInput(RectTransform list)
        {
            TMP_Text caption = _dropdown.captionText;
            var go = new GameObject("Search", typeof(RectTransform));
            go.SetActive(false);                                  // set it all up before TMP_InputField wakes up
            var rt = (RectTransform)go.transform;
            rt.SetParent(list, false);
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(6f, -searchHeight + 4f); rt.offsetMax = new Vector2(-6f, -4f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.08f);

            var area = new GameObject("Text Area", typeof(RectTransform));
            var art = (RectTransform)area.transform;
            art.SetParent(rt, false);
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = new Vector2(10f, 2f); art.offsetMax = new Vector2(-10f, -2f);
            area.AddComponent<RectMask2D>();

            TextMeshProUGUI ph = NewText(art, "Placeholder", caption);
            ph.text = placeholder;
            ph.fontStyle = FontStyles.Italic;
            ph.color = WithAlpha(ph.color, 0.5f);
            TextMeshProUGUI txt = NewText(art, "Text", caption);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = txt;
            input.placeholder = ph;
            input.targetGraphic = bg;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            if (caption != null) input.fontAsset = caption.font;
            go.SetActive(true);
            return input;
        }

        private static TextMeshProUGUI NewText(RectTransform parent, string name, TMP_Text style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            if (style != null) { t.font = style.font; t.fontSize = Mathf.Max(12f, style.fontSize * 0.9f); t.color = style.color; }
            else t.fontSize = 16f;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        private TextMeshProUGUI BuildLabel(RectTransform parent, string text, float alpha)
        {
            TextMeshProUGUI t = NewText(parent, "NoMatches", _dropdown.captionText);
            t.text = text;
            t.alignment = TextAlignmentOptions.Center;
            t.color = WithAlpha(t.color, alpha);
            return t;
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        // ── filtering ──
        private void Filter(string query)
        {
            string[] words = Words(query);
            int shown = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                bool placeholderRow = firstOptionIsPlaceholder && i == 0;
                bool show = words.Length == 0 || (!placeholderRow && Matches(_labels[i], words));
                _items[i].gameObject.SetActive(show);
                if (show) shown++;
            }
            if (!_contentHasLayout) Restack();
            _empty.gameObject.SetActive(shown == 0);
            var scroll = _list != null ? _list.GetComponent<ScrollRect>() : null;
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;     // back to the top
        }

        /// <summary>The dropdown places items itself (from the bottom of Content): stack the shown ones without gaps.</summary>
        private void Restack()
        {
            var shown = new List<RectTransform>();
            foreach (RectTransform it in _items) if (it.gameObject.activeSelf) shown.Add(it);
            for (int j = 0; j < shown.Count; j++)
                shown[j].anchoredPosition = new Vector2(shown[j].anchoredPosition.x, _bottom + _step * (shown.Count - 1 - j));
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, _step * shown.Count + _extra);
        }

        private void PickFirst()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (!_items[i].gameObject.activeSelf || (firstOptionIsPlaceholder && i == 0 && _input.text.Length > 0)) continue;
                _dropdown.value = i;            // the dropdown's own change event runs, as if clicked
                _dropdown.Hide();
                return;
            }
        }

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) _dropdown.Hide();
        }

        /// <summary>Every word of the query appears in the label (any order, any case).</summary>
        public static bool Matches(string label, string[] words)
        {
            string l = (label ?? "").ToLowerInvariant();
            foreach (string w in words) if (l.IndexOf(w, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        public static string[] Words(string query)
        {
            return (query ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Label text without rich-text tags.</summary>
        public static string Plain(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('<') < 0) return s ?? "";
            var sb = new System.Text.StringBuilder(s.Length);
            bool tag = false;
            foreach (char c in s) { if (c == '<') tag = true; else if (c == '>') tag = false; else if (!tag) sb.Append(c); }
            return sb.ToString();
        }
    }
}
