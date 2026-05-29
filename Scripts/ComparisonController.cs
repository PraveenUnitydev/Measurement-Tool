using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>
    /// COMPARISON SCENE CONTROLLER (Updated with PDF Export)
    /// 
    /// Changes:
    /// - OnExportClick() now uses VehiclePDFExporter with file dialog
    /// - Added export camera field
    /// </summary>
    public class ComparisonController : MonoBehaviour
    {
        [Header("═══ VEHICLE SELECTION ═══")]
        public TMP_Dropdown vehicleADropdown;
        public TMP_Dropdown vehicleBDropdown;
        public Button swapButton;

        [Header("═══ VEHICLE HEADERS ═══")]
        public TMP_Text vehicleATitle;
        public TMP_Text vehicleBTitle;
        public TMP_Text vehicleATHeader;
        public TMP_Text vehicleBTHeader;

        [Header("═══ 3D PREVIEW (Optional) ═══")]
        [Tooltip("Preview renderer for Vehicle A")]
        public VehiclePreviewRenderer vehicleAPreview;
        [Tooltip("Preview renderer for Vehicle B")]
        public VehiclePreviewRenderer vehicleBPreview;

        [Header("═══ VISUAL COMPARISON MODE ═══")]
        [Tooltip("Visual comparison mode for superimpose + clip")]
        public VisualComparisonMode visualComparisonMode;

        [Header("═══ 3D VIEW CONTROLS ═══")]
        public Button vehicleAFrontBtn;
        public Button vehicleABackBtn;
        public Button vehicleALeftSideBtn;
        public Button vehicleATopBtn;
        public Button vehicleARightSideBtn;
        public Button vehicleBFrontBtn;
        public Button vehicleBBackBtn;
        public Button vehicleBLeftSideBtn;
        public Button vehicleBTopBtn;
        public Button vehicleBRightSideBtn;
        public Toggle linkRotationToggle;
       // public Button syncViewsButton;

        [Header("═══ COMPARISON TABLE ═══")]
        public Transform tableContent;           // Parent with VerticalLayoutGroup
        public GameObject categoryHeaderPrefab;  // "LENGTH", "WIDTH", etc.
        public GameObject comparisonRowPrefab;   // Individual measurement row

        [Header("═══ BUTTONS ═══")]
        public Button backButton;
       // public Button homeButton;
        public Button exportButton;
      //  public Button sectionModeButton;

        [Header("═══ EXPORT (NEW) ═══")]
        [Tooltip("Camera used for PDF screenshot (defaults to Camera.main if not set)")]
        public Camera exportCamera;

        [Header("═══ DIFF MODE ═══")]
        public Toggle absoluteToggle;
        public Toggle percentageToggle;

        [Header("═══ SESSION INFO ═══")]
        public TMP_Text sessionTimeText;

        [Header("═══ LOADING ═══")]
        public GameObject loadingPanel;
        public TMP_Text loadingText;

        [Header("═══ COLORS ═══")]
        public Color positiveColor = new Color(0.2f, 0.8f, 0.4f);   // Green - A is larger
        public Color negativeColor = new Color(0.9f, 0.5f, 0.2f);   // Orange - B is larger
        public Color neutralColor = new Color(0.5f, 0.5f, 0.5f);    // Gray - Equal
        public Color categoryColor = new Color(0.3f, 0.6f, 0.9f);   // Blue - Category headers

        // Data
        private VehicleDataManager _dataManager;
        private List<SavedVehicleInfo> _savedVehicles;
        private SavedVehicleMeasurement _vehicleAData;
        private SavedVehicleMeasurement _vehicleBData;
        private List<GameObject> _tableRows = new List<GameObject>();
        private float _sessionStartTime;
        private bool _linkRotation = true;

        [Tooltip("Loading spinner/animation (optional)")]
        public GameObject loadingSpinner;

        [Tooltip("Progress bar (optional)")]
        public Slider loadingProgressBar;

        // Measurement definitions
        private readonly MeasurementCategory[] _categories = new MeasurementCategory[]
        {
            new MeasurementCategory("LENGTH", new MeasurementDef[]
            {
                new MeasurementDef("L103", "Overall Length", "mm"),
                new MeasurementDef("L101", "Wheelbase", "mm"),
                new MeasurementDef("L104", "Front Overhang", "mm"),
                new MeasurementDef("L105", "Rear Overhang", "mm"),
            }),
            new MeasurementCategory("WIDTH", new MeasurementDef[]
            {
                new MeasurementDef("W103", "Overall Width", "mm"),
                new MeasurementDef("W144", "Front Track", "mm"),
                new MeasurementDef("W145", "Rear Track", "mm"),
            }),
            new MeasurementCategory("HEIGHT", new MeasurementDef[]
            {
                new MeasurementDef("H100", "Overall Height", "mm"),
                new MeasurementDef("H101", "Ground Clearance", "mm"),
            }),
            new MeasurementCategory("WHEELS", new MeasurementDef[]
            {
                new MeasurementDef("TD_F", "Front Diameter", "mm"),
                new MeasurementDef("TD_R", "Rear Diameter", "mm"),
            }),
        };

        #region Unity Lifecycle

        private void Start()
        {
            _dataManager = VehicleDataManager.Instance;
            _sessionStartTime = Time.time;

            SetupListeners();
            PopulateDropdowns();

            // Pre-select if provided
            if (!string.IsNullOrEmpty(_dataManager.CompareVehicleAId))
                SelectInDropdown(vehicleADropdown, _dataManager.CompareVehicleAId);
            if (!string.IsNullOrEmpty(_dataManager.CompareVehicleBId))
                SelectInDropdown(vehicleBDropdown, _dataManager.CompareVehicleBId);
        }

        private void Update()
        {
            UpdateSessionTime();
        }

        private void SetupListeners()
        {
            backButton?.onClick.AddListener(OnBackClick);
            swapButton?.onClick.AddListener(OnSwapClick);
            exportButton?.onClick.AddListener(OnExportClick);
          //  sectionModeButton?.onClick.AddListener(OnSectionModeClick);

            vehicleADropdown?.onValueChanged.AddListener(OnVehicleAChanged);
            vehicleBDropdown?.onValueChanged.AddListener(OnVehicleBChanged);

            absoluteToggle?.onValueChanged.AddListener(_ => RefreshTable());
            percentageToggle?.onValueChanged.AddListener(_ => RefreshTable());

            // 3D View Controls - Vehicle A
            vehicleAFrontBtn?.onClick.AddListener(() => SetView("VehicleA",CameraViews.Front));
            vehicleABackBtn?.onClick.AddListener(() => SetView("VehicleA",CameraViews.Rear));
            vehicleALeftSideBtn?.onClick.AddListener(() => SetView("VehicleA", CameraViews.Left));
            vehicleATopBtn?.onClick.AddListener(() => SetView("VehicleA", CameraViews.Top));
            vehicleARightSideBtn?.onClick.AddListener(() => SetView("VehicleA", CameraViews.Right));

            // 3D View Controls - Vehicle B
            vehicleBFrontBtn?.onClick.AddListener(() => SetView("VehicleB",CameraViews.Front));
            vehicleBBackBtn?.onClick.AddListener(() => SetView("VehicleB",CameraViews.Rear));
            vehicleBLeftSideBtn?.onClick.AddListener(() => SetView("VehicleB",CameraViews.Left));
            vehicleBTopBtn?.onClick.AddListener(() =>SetView("VehicleB",CameraViews.Top));
            vehicleBRightSideBtn?.onClick.AddListener(() =>SetView("VehicleB",CameraViews.Right));

            // Sync Controls
            linkRotationToggle?.onValueChanged.AddListener(OnLinkRotationChanged);
            // syncViewsButton?.onClick.AddListener(SyncViews);

            if (vehicleAPreview != null)
            {
                vehicleAPreview.OnViewChanged -= OnAVehicleViewChanged;
                vehicleAPreview.OnViewChanged += OnAVehicleViewChanged;
            }

            // Initial state
            if (linkRotationToggle != null)
                _linkRotation = linkRotationToggle.isOn;

            ShowLoading(false);
        }

        #endregion

        private void SetView(string vehicle, CameraViews view)
        {
            if (vehicle == "VehicleA")
            {
                switch (view)
                {
                    case CameraViews.Front:
                        vehicleAPreview.SetFrontView();
                        break;
                    case CameraViews.Rear:
                        vehicleAPreview.SetBackView();
                        break;
                    case CameraViews.Right:
                        vehicleAPreview.SetRightSideView();
                        break;
                    case CameraViews.Top: vehicleAPreview.SetTopView(); break;
                    case CameraViews.Reset:vehicleAPreview.ResetView(); break;
                    case CameraViews.Left: vehicleAPreview.SetLeftSideView(); break;
                }
            }
            else if(vehicle=="VehicleB")
            {
                switch (view)
                {
                    case CameraViews.Front:
                        vehicleBPreview.SetFrontView();
                        break;
                    case CameraViews.Rear:
                        vehicleBPreview.SetBackView();
                        break;
                    case CameraViews.Right:
                        vehicleBPreview.SetRightSideView();
                        break;
                    case CameraViews.Top: vehicleBPreview.SetTopView(); break;
                    case CameraViews.Reset: vehicleBPreview.ResetView(); break;
                    case CameraViews.Left: vehicleBPreview.SetLeftSideView(); break;

                }
            }
            if (_linkRotation)
            {
                SyncViews();
            }
       
        }

        #region Dropdown Population

        private void PopulateDropdowns()
        {
            _savedVehicles = VehicleMeasurementStorage.GetSavedVehicleList();

            var options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("-- Select Vehicle --"));

            foreach (var v in _savedVehicles)
                options.Add(new TMP_Dropdown.OptionData(v.vehicleName));

            if (vehicleADropdown != null)
            {
                vehicleADropdown.ClearOptions();
                vehicleADropdown.AddOptions(options);
            }

            if (vehicleBDropdown != null)
            {
                vehicleBDropdown.ClearOptions();
                vehicleBDropdown.AddOptions(options);
            }
        }

        private void SelectInDropdown(TMP_Dropdown dropdown, string vehicleId)
        {
            if (dropdown == null || _savedVehicles == null) return;

            for (int i = 0; i < _savedVehicles.Count; i++)
            {
                if (_savedVehicles[i].vehicleId == vehicleId)
                {
                    dropdown.value = i + 1; // +1 for "Select Vehicle" option
                    return;
                }
            }
        }

        #endregion

        #region Vehicle Selection

        private void OnVehicleAChanged(int index)
        {
            if (index == 0)
            {
                _vehicleAData = null;
                SetText(vehicleATitle, "Vehicle A");
                SetText(vehicleATHeader, "Vehicle A");
                vehicleAPreview?.UnloadVehicle();
                visualComparisonMode?.Initialize(null, _vehicleBData);
                return;
            }
            else
            {
                var vehicleInfo = _savedVehicles[index - 1];
                _vehicleAData = VehicleMeasurementStorage.Load(vehicleInfo.vehicleId);
                SetText(vehicleATitle, vehicleInfo.vehicleName);
                SetText(vehicleATHeader, vehicleInfo.vehicleName);
                visualComparisonMode?.UpdateVehicleNames(0, vehicleInfo.vehicleName);

                if (vehicleAPreview != null && _vehicleAData != null)
                {
                    StartCoroutine(LoadVehicleAsync(vehicleAPreview, _vehicleAData, "Loading Vehicle A..", () => { TryCompare(); }));
                }
                else
                {
                    TryCompare();
                }

                // Load 3D preview if available
               // LoadVehiclePreview(vehicleAPreview, _vehicleAData);

                // Update visual comparison mode
                //UpdateVisualComparisonMode();
            }

            RefreshTable();
        }

        private void OnVehicleBChanged(int index)
        {
            if (index == 0)
            {
                _vehicleBData = null;
                SetText(vehicleBTitle, "Vehicle B");
                SetText(vehicleBTHeader, "Vehicle B");
                vehicleBPreview?.UnloadVehicle();
                visualComparisonMode.Initialize(_vehicleAData, null);
                return;
            }
            else
            {
                var vehicleInfo = _savedVehicles[index - 1];
                _vehicleBData = VehicleMeasurementStorage.Load(vehicleInfo.vehicleId);
                SetText(vehicleBTitle, vehicleInfo.vehicleName);
                SetText(vehicleBTHeader, vehicleInfo.vehicleName);
                visualComparisonMode?.UpdateVehicleNames(1, vehicleInfo.vehicleName);
                if (vehicleBPreview != null && _vehicleBData != null)
                {
                    StartCoroutine(LoadVehicleAsync(vehicleBPreview, _vehicleBData, "Loading Vehicle B..", () => { TryCompare(); }));
                }
                else
                {
                    TryCompare();
                }
                // Load 3D preview if available
                //LoadVehiclePreview(vehicleBPreview, _vehicleBData);

                // Update visual comparison mode
               // UpdateVisualComparisonMode();
            }

            RefreshTable();
        }
        private void OnAVehicleViewChanged(float orbit, float pitch, float distance)
        {
            if (!_linkRotation || vehicleBPreview == null) return;

            // If you want rotation+zoom:
            vehicleBPreview.SetOrbitAngle(orbit);
            vehicleBPreview.SetPitchAngle(pitch);
            vehicleBPreview.SetZoomDistance(distance);

            // If you strictly want rotation only, comment the zoom line above.
            // vehicleBPreview.SetZoomDistance(distance);
        }

        private void TryCompare()
        {
            if (_vehicleAData == null || _vehicleBData == null)
                return;
            RefreshTable();
            if(visualComparisonMode!=null)
                visualComparisonMode.Initialize(_vehicleAData, _vehicleBData);
          
        }
        private void ClearTable()
        {
            foreach(var row in _tableRows)
            {
                if(row != null) 
                    Destroy(row);
            }
            _tableRows.Clear();
        }

        private void LoadVehiclePreview(VehiclePreviewRenderer preview, SavedVehicleMeasurement data)
        {
            if (preview == null || data == null) return;

            // LoadVehicle takes SavedVehicleMeasurement directly
            preview.LoadVehicle(data);
        }

        /// <summary>
        /// Update visual comparison mode when either vehicle changes
        /// </summary>
        private void UpdateVisualComparisonMode()
        {
            if (visualComparisonMode != null && _vehicleAData != null && _vehicleBData != null)
            {
                visualComparisonMode.Initialize(_vehicleAData, _vehicleBData);
            }
        }

        #endregion

        #region Comparison Table

        private void RefreshTable()
        {
            // Clear existing rows
            foreach (var row in _tableRows)
            {
                if (row != null) Destroy(row);
            }
            _tableRows.Clear();

            if (_vehicleAData == null || _vehicleBData == null)
            {
                // Show placeholder message
                return;
            }

            // Build table
            foreach (var category in _categories)
            {
                // Category header
                if (categoryHeaderPrefab != null && tableContent != null)
                {
                    var header = Instantiate(categoryHeaderPrefab, tableContent);
                    var headerText = header.GetComponentInChildren<TMP_Text>();
                    if (headerText != null)
                    {
                        headerText.text = category.Name;
                        headerText.color = categoryColor;
                    }
                    _tableRows.Add(header);
                }

                // Measurement rows
                foreach (var measurement in category.Measurements)
                {
                    CreateComparisonRow(measurement);
                }
            }
        }

        private void CreateComparisonRow(MeasurementDef measurement)
        {
            if (comparisonRowPrefab == null || tableContent == null) return;

            var row = Instantiate(comparisonRowPrefab, tableContent);
            _tableRows.Add(row);

            float valueA = GetMeasurementValue(_vehicleAData, measurement.Code);
            float valueB = GetMeasurementValue(_vehicleBData, measurement.Code);
            float diff = valueA - valueB;
            float percentDiff = valueB > 0 ? (diff / valueB) * 100f : 0f;

            bool isEqual = Mathf.Abs(diff) < 0.1f;
            bool aIsLarger = diff > 0;
            Color diffColor = isEqual ? neutralColor : (aIsLarger ? positiveColor : negativeColor);

            // Find and populate text fields
            var texts = row.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in texts)
            {
                string n = t.name.ToLower();

                if (n.Contains("code") || n.Contains("param"))
                {
                    t.text = measurement.Code;
                }
                else if (n.Contains("name") || n.Contains("desc"))
                {
                    t.text = measurement.Name+$" ({measurement.Code})";
                }
                else if (n.Contains("valuea") || n.Contains("value_a") || n.Contains("vehiclea"))
                {
                    t.text = FormatValue(valueA, measurement.Unit);
                }
                else if (n.Contains("valueb") || n.Contains("value_b") || n.Contains("vehicleb"))
                {
                    t.text = FormatValue(valueB, measurement.Unit);
                }
                else if (n.Contains("diff") && !n.Contains("percent"))
                {
                    if (isEqual)
                    {
                        t.text = "—";
                        t.color = neutralColor;
                    }
                    else
                    {
                        t.text = $"{(diff > 0 ? "+" : "")}{diff:F0} {measurement.Unit}";
                        t.color = diffColor;
                    }
                }
                else if (n.Contains("percent"))
                {
                    if (isEqual)
                    {
                        t.text = "—";
                        t.color = neutralColor;
                    }
                    else
                    {
                        t.text = $"{(percentDiff > 0 ? "+" : "")}{percentDiff:F1}%";
                        t.color = diffColor;
                    }
                }
                else if (n.Contains("status") || n.Contains("arrow") || n.Contains("indicator"))
                {
                    if (isEqual)
                        t.text = "=";
                    else
                        t.text = aIsLarger ? "▲" : "▼";
                    t.color = diffColor;
                }
            }

            // Set color indicator image if exists
            var images = row.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (img.name.ToLower().Contains("indicator") || img.name.ToLower().Contains("color") || img.name.ToLower().Contains("status"))
                {
                    img.color = diffColor;
                }
            }
        }

        private float GetMeasurementValue(SavedVehicleMeasurement data, string code)
        {
            if (data == null) return 0;

            switch (code)
            {
                case "L103": return data.L103_OverallLength;
                case "L101": return data.L101_Wheelbase;
                case "L104": return data.L104_FrontOverhang;
                case "L105": return data.L105_RearOverhang;
                case "W103": return data.W103_OverallWidth;
                case "W144": return data.W144_FrontTrack;
                case "W145": return data.W145_RearTrack;
                case "H100": return data.H100_OverallHeight;
                case "H101": return data.H101_GroundClearance;
                case "TD_F": return data.TD_F_FrontDiameter;
                case "TD_R": return data.TD_R_RearDiameter;
                default: return 0;
            }
        }

        private string FormatValue(float value, string unit)
        {
            if (value <= 0)
                return "—";

            if (unit == "mm")
                return $"{value:F0} mm";
            else if (unit == "m²")
                return $"{value:F2} m²";
            else
                return $"{value:F1} {unit}";
        }

        #endregion

        #region Button Handlers

        private void OnBackClick()
        {
            // Cleanup 3D previews before leaving
            vehicleAPreview?.UnloadVehicle();
            vehicleBPreview?.UnloadVehicle();

            _dataManager.GoToHome();
        }

        private void OnSwapClick()
        {
            if (vehicleADropdown == null || vehicleBDropdown == null)
                return;

            int tempA = vehicleADropdown.value;
            vehicleADropdown.value = vehicleBDropdown.value;
            vehicleBDropdown.value = tempA;
        }

       // public ComparisonPDFExporter PDFExporter;
        // ═══════════════════════════════════════════════════════════════════════════
        //                    UPDATED: PDF EXPORT WITH FILE DIALOG
        // ═══════════════════════════════════════════════════════════════════════════
        private void OnExportClick()
        {
            // Validate that both vehicles are selected
            if (_vehicleAData == null || _vehicleBData == null)
            {
                ShowLoading(true, "Please select both vehicles to compare");
                StartCoroutine(HideLoadingAfterDelay(2f));
                return;
            }

            ShowLoading(true, "Preparing comparison export...");

            // ═══ USE PDF EXPORTER WITH FILE DIALOG ═══

           /* PDFExporter.ExportPDF(_vehicleAData, _vehicleBData, (success, result) =>
            {
                ShowLoading(false);

                if (success)
                {
                    string fileName = System.IO.Path.GetFileName(result);
                    ShowLoading(true, $"✓ Exported: {fileName}");
                    StartCoroutine(HideLoadingAfterDelay(2f));

                    Debug.Log($"[ComparisonController] PDF exported to: {result}");
                }
                else
                {
                    // User cancelled - no message needed
                    Debug.Log("[ComparisonController] Export cancelled by user");
                }
            });*/
            
            VehiclePDFExporter.ExportComparison(
                _vehicleAData,
                _vehicleBData,
               vehicleAPreview,
               vehicleBPreview,
                (success, filePath) =>
                {
                    ShowLoading(false);

                    if (success)
                    {
                        string fileName = System.IO.Path.GetFileName(filePath);
                        ShowLoading(true, $"✓ Exported: {fileName}");
                        StartCoroutine(HideLoadingAfterDelay(2f));

                        Debug.Log($"[ComparisonController] PDF exported to: {filePath}");
                    }
                    else
                    {
                        // User cancelled - no message needed
                        Debug.Log("[ComparisonController] Export cancelled by user");
                    }
                }
            );
        }

        private System.Collections.IEnumerator HideLoadingAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ShowLoading(false);
        }

        // ComparisonController.cs
        private void OnSectionModeClick()
        {
            if (visualComparisonMode == null) return;

            var current = visualComparisonMode.CurrentMode;
            var next = current == VisualComparisonMode.ComparisonViewMode.SideBySide
                ? VisualComparisonMode.ComparisonViewMode.Superimpose
                : current == VisualComparisonMode.ComparisonViewMode.Superimpose
                    ? VisualComparisonMode.ComparisonViewMode.Dual3D
                    : VisualComparisonMode.ComparisonViewMode.SideBySide;

            visualComparisonMode.SetMode(next);
        }
        public void OnDual3DClick()
        {
            visualComparisonMode?.SetMode(VisualComparisonMode.ComparisonViewMode.Dual3D);
        }

        #endregion

        #region 3D View Controls

        private void OnLinkRotationChanged(bool linked)
        {
            _linkRotation = linked;

            // Optional: prevent user from fighting the follow on Vehicle B
            if (vehicleBPreview != null)
                vehicleBPreview.SetInteractionEnabled(!linked); // requires Patch 1.5

            if (linked)
                SyncViews(); // one-time snap so both start aligned


        }

        /// <summary>
        /// Sync Vehicle B's view to match Vehicle A
        /// </summary>
        public void SyncViews()
        {
            if (vehicleAPreview == null || vehicleBPreview == null) return;

            vehicleBPreview.SetOrbitAngle(vehicleAPreview.orbitAngle);
            vehicleBPreview.SetPitchAngle(vehicleAPreview.pitchAngle);
            vehicleBPreview.SetZoomDistance(vehicleAPreview.cameraDistance);
        }

        /// <summary>
        /// Reset both views to default
        /// </summary>
        public void ResetBothViews()
        {
            vehicleAPreview?.ResetView();
            vehicleBPreview?.ResetView();
        }

        #endregion

        #region Loading

        private void ShowLoading(bool show, string message = "Loading...")
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(show);

            if (loadingText != null)
                loadingText.text = message;

            if (loadingSpinner != null)
                loadingSpinner.SetActive(show);

            if (loadingProgressBar != null)
            {
                loadingProgressBar.gameObject.SetActive(show);
                loadingProgressBar.value = 0f;
            }
        }

        #endregion
        private IEnumerator LoadVehicleAsync(VehiclePreviewRenderer preview, SavedVehicleMeasurement data, string message, Action onComplete)
        {
            // Show loading UI
            ShowLoading(true, message);

            // Start loading
            preview.LoadVehicle(data);

            // Wait for load with timeout
            float timeout = 30f;
            float elapsed = 0f;
            float dotTimer = 0f;
            int dotCount = 0;

            while (!preview.HasVehicle && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                dotTimer += Time.deltaTime;

                // Animate loading text
                if (dotTimer > 0.3f)
                {
                    dotTimer = 0f;
                    dotCount = (dotCount + 1) % 4;
                    string dots = new string('.', dotCount);

                    if (loadingText != null)
                        loadingText.text = message + dots;
                }

                // Update progress bar (fake progress based on time)
                if (loadingProgressBar != null)
                {
                    float fakeProgress = Mathf.Clamp01(elapsed / 5f) * 0.9f; // 90% over 5 seconds
                    loadingProgressBar.value = fakeProgress;
                }

                yield return null;
            }

            // Complete progress bar
            if (loadingProgressBar != null)
                loadingProgressBar.value = 1f;

            // Extra frame for render
            yield return null;

            // Hide loading
            ShowLoading(false);

            // Callback
            onComplete?.Invoke();
        }
        #region Helpers

        private void SetText(TMP_Text textField, string value)
        {
            if (textField != null)
                textField.text = value;
        }

        private void UpdateSessionTime()
        {
            if (sessionTimeText == null)
                return;

            float elapsed = Time.time - _sessionStartTime;
            int minutes = (int)(elapsed / 60f);
            int seconds = (int)(elapsed % 60f);
            sessionTimeText.text = $"Session Active: {minutes:00}:{seconds:00}";
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            // Cleanup 3D previews
          //  vehicleAPreview?.OnViewChanged -= OnAVehicleViewChanged; // NEW
            vehicleAPreview?.UnloadVehicle();
            vehicleBPreview?.UnloadVehicle();
        }

        #endregion
    }

    #region Data Classes (Keep existing)

    [Serializable]
    public class MeasurementCategory
    {
        public string Name;
        public MeasurementDef[] Measurements;

        public MeasurementCategory(string name, MeasurementDef[] measurements)
        {
            Name = name;
            Measurements = measurements;
        }
    }

    [Serializable]
    public class MeasurementDef
    {
        public string Code;
        public string Name;
        public string Unit;

        public MeasurementDef(string code, string name, string unit)
        {
            Code = code;
            Name = name;
            Unit = unit;
        }
    }

    public enum CameraViews 
    {
        Front,Rear,Right,Left,Top,Reset
    }

    #endregion
}
