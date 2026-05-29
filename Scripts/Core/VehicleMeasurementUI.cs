/*using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    public class VehicleMeasurementUI : MonoBehaviour
    {
        [Header("References")]
        public VehicleMeasurementSystem measurementSystem;

        [Header("UI Settings")]
        public KeyCode toggleKey = KeyCode.Tab;
        public bool createUIOnStart = true;
        public bool startVisible = true;

        private GameObject _panelRoot;
        private Dictionary<string, Text> _valueTexts = new Dictionary<string, Text>();
        private bool _isVisible = true;

        private void Start()
        {
            _isVisible = startVisible;
            if (createUIOnStart) CreateUI();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) ToggleVisibility();
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            if (_panelRoot != null) _panelRoot.SetActive(_isVisible);
        }

        [ContextMenu("Create UI")]
        public void CreateUI()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("MeasurementCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasObj.transform);
            }

            if (_panelRoot != null) DestroyImmediate(_panelRoot);

            _panelRoot = CreatePanel("Panel", transform, new Color(0.1f, 0.1f, 0.15f, 0.95f));
            RectTransform panelRect = _panelRoot.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0.5f);
            panelRect.anchorMax = new Vector2(0, 0.5f);
            panelRect.pivot = new Vector2(0, 0.5f);
            panelRect.anchoredPosition = new Vector2(10, 0);
            panelRect.sizeDelta = new Vector2(320, 500);

            VerticalLayoutGroup layout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 5;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateLabel(_panelRoot.transform, "📐 Vehicle Measurements", 16, FontStyle.Bold, Color.white, 35);
            CreateLabel(_panelRoot.transform, $"Press {toggleKey} to toggle", 10, FontStyle.Normal, Color.gray, 20);

            // LENGTH
            CreateSection(_panelRoot.transform, "LENGTH", new Color(0.3f, 0.6f, 1f), new[] {
                ("L103", "Overall Length"),
                ("L101", "Wheelbase"),
                ("L104", "Front Overhang"),
                ("L105", "Rear Overhang")
            });

            // WIDTH
            CreateSection(_panelRoot.transform, "WIDTH", new Color(0.3f, 1f, 0.5f), new[] {
                ("W103", "Overall Width"),
                ("W144", "Front Track"),
                ("W145", "Rear Track")
            });

            // HEIGHT
            CreateSection(_panelRoot.transform, "HEIGHT", new Color(1f, 0.5f, 0.3f), new[] {
                ("H100", "Overall Height"),
                ("H101", "Ground Clearance")
            });

            // WHEELS
            CreateSection(_panelRoot.transform, "WHEELS", new Color(1f, 0.8f, 0.3f), new[] {
                ("TD_F", "Front Diameter"),
                ("TD_R", "Rear Diameter")
            });

            CreateButton(_panelRoot.transform, "Analyze Vehicle", OnAnalyzeClicked);

            _panelRoot.SetActive(_isVisible);
        }

        private void CreateSection(Transform parent, string title, Color color, (string code, string name)[] items)
        {
            CreateLabel(parent, title, 12, FontStyle.Bold, color, 22);
            foreach (var (code, name) in items) CreateRow(parent, code, name, color);
        }

        private void CreateRow(Transform parent, string code, string name, Color color)
        {
            GameObject row = new GameObject($"Row_{code}");
            row.transform.SetParent(parent, false);
            HorizontalLayoutGroup hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 5;
            row.AddComponent<LayoutElement>().minHeight = 22;

            CreateLabel(row.transform, code, 11, FontStyle.Bold, color, 0, 45);
            CreateLabel(row.transform, name, 10, FontStyle.Normal, Color.gray, 0, 0, true);

            GameObject valueObj = new GameObject("Value");
            valueObj.transform.SetParent(row.transform, false);
            Text valueText = valueObj.AddComponent<Text>();
            valueText.text = "---";
            valueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            valueText.fontSize = 11;
            valueText.fontStyle = FontStyle.Bold;
            valueText.color = Color.white;
            valueText.alignment = TextAnchor.MiddleRight;
            valueObj.AddComponent<LayoutElement>().minWidth = 80;

            _valueTexts[code] = valueText;
        }

        private void CreateLabel(Transform parent, string text, int fontSize, FontStyle style, Color color, float height = 0, float width = 0, bool flex = false)
        {
            GameObject obj = new GameObject("Label");
            obj.transform.SetParent(parent, false);
            Text t = obj.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize;
            t.fontStyle = style;
            t.color = color;
            LayoutElement le = obj.AddComponent<LayoutElement>();
            if (height > 0) le.minHeight = height;
            if (width > 0) le.minWidth = width;
            if (flex) le.flexibleWidth = 1;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            panel.AddComponent<Image>().color = color;
            return panel;
        }

        private void CreateButton(Transform parent, string text, Action onClick)
        {
            GameObject btnObj = new GameObject("Button");
            btnObj.transform.SetParent(parent, false);
            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.5f, 0.3f);
            Button btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            btnObj.AddComponent<LayoutElement>().minHeight = 35;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            Text t = textObj.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 14;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            RectTransform rt = textObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        private void OnAnalyzeClicked()
        {
            if (measurementSystem != null)
            {
                measurementSystem.Analyze();
                RefreshValues();
            }
            else
            {
                Debug.LogError("[MeasurementUI] measurementSystem not assigned!");
            }
        }

        public void RefreshValues()
        {
            // Use Results (V3) instead of Measurements
            if (measurementSystem?.Results == null) return;

            var r = measurementSystem.Results;

            SetValue("L103", r.L103_OverallLength);
            SetValue("L101", r.L101_Wheelbase);
            SetValue("L104", r.L104_FrontOverhang);
            SetValue("L105", r.L105_RearOverhang);
            SetValue("W103", r.W103_OverallWidth);
            SetValue("W144", r.W144_FrontTrackWidth);
            SetValue("W145", r.W145_RearTrackWidth);
            SetValue("H100", r.H100_OverallHeight);
            SetValue("H101", r.H101_GroundClearance);
            SetValue("TD_F", r.FrontWheelRadius * 2f);  // Diameter = Radius * 2
            SetValue("TD_R", r.RearWheelRadius * 2f);
        }

        private void SetValue(string code, float value)
        {
            if (_valueTexts.TryGetValue(code, out Text t))
                t.text = value > 0 ? $"{value * 1000:F1} mm" : "---";
        }
    }
}
*/

using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Collections;   // Use TextMeshPro

namespace VehicleMeasurement
{
    public class VehicleMeasurementUI : MonoBehaviour
    {
        [Header("References")]
        public VehicleMeasurementSystem measurementSystem;

        [Header("UI Elements")]
        public GameObject panelRoot;   // Assign your existing panel
        public KeyCode toggleKey = KeyCode.Tab;
        public bool startVisible = true;

        // Direct TMP references for each measurement field
        [Header("Measurement Texts")]
        public TMP_Text L103_OverallLength;
        public TMP_Text L101_Wheelbase;
        public TMP_Text L104_FrontOverhang;
        public TMP_Text L105_RearOverhang;
        public TMP_Text W103_OverallWidth;
        public TMP_Text W144_FrontTrack;
        public TMP_Text W145_RearTrack;
        public TMP_Text H100_OverallHeight;
        public TMP_Text H101_GroundClearance;
        public TMP_Text TD_FrontDiameter;
        public TMP_Text TD_RearDiameter;

        private bool _isVisible;


        [SerializeField] private GameObject _measuringBuffer;


        private IEnumerator StartMeasuring()
        {
            _measuringBuffer.SetActive(true);
           
            if (measurementSystem != null)
            {
                measurementSystem.Analyze();
                RefreshValues();
            }
            else
            {
                Debug.LogError("[MeasurementUI] measurementSystem not assigned!");
            }
            yield return new WaitForSeconds(1.5f);
            _measuringBuffer.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) ToggleVisibility();
        }

        public void ToggleVisibility()
        {
            _isVisible = !_isVisible;
            if (panelRoot != null) panelRoot.SetActive(_isVisible);
        }

        public void OnAnalyzeClicked()
        {
            StartCoroutine(StartMeasuring());
        }

        public void RefreshValues()
        {
            if (measurementSystem?.Results == null) return;

            var r = measurementSystem.Results;

            SetValue(L103_OverallLength, r.L103_OverallLength);
            SetValue(L101_Wheelbase, r.L101_Wheelbase);
            SetValue(L104_FrontOverhang, r.L104_FrontOverhang);
            SetValue(L105_RearOverhang, r.L105_RearOverhang);
            SetValue(W103_OverallWidth, r.W103_OverallWidth);
            SetValue(W144_FrontTrack, r.W144_FrontTrackWidth);
            SetValue(W145_RearTrack, r.W145_RearTrackWidth);
            SetValue(H100_OverallHeight, r.H100_OverallHeight);
            SetValue(H101_GroundClearance, r.H101_GroundClearance);
            SetValue(TD_FrontDiameter, r.FrontWheelRadius * 2f);
            SetValue(TD_RearDiameter, r.RearWheelRadius * 2f);
        }

        private void SetValue(TMP_Text textField, float value)
        {
           TMP_Text _childText= GetChildText(textField);
            if (_childText != null)
                _childText.text = value > 0 ? $"{value * 1000:F1} mm" : "---";
        }
        private TMP_Text GetChildText(TMP_Text _text)
        {
            return _text.transform.GetChild(0).GetComponent<TMP_Text>();
        }
    }
}
