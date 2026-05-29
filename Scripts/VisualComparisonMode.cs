using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    /// <summary>
    /// VISUAL COMPARISON MODE (FIXED VERSION v2)
    /// 
    /// Fixes:
    /// 1. Uses correct global shader properties: _GlobalClipXEnabled, _GlobalClipXPosition, _GlobalClipXDirection
    /// 2. Renders directly to screen (no RawImage blocking) for superimpose mode
    /// 3. Moves existing models instead of reloading
    /// 
    /// How Superimpose Rendering Works:
    /// - SuperimposeCamera renders DIRECTLY to screen (no render texture)
    /// - UI panels overlay on top of the 3D view
    /// - No RawImage needed = no input blocking
    /// </summary>
    public class VisualComparisonMode : MonoBehaviour
    {
        public enum ComparisonViewMode
        {
            SideBySide,
            Superimpose,
            Dual3D
        }

        [Header("═══ MODE TOGGLE ═══")]
        //public Button sideBySideButton;
        //  public Button superimposeButton;

        // public Button _switchModeButton;
        // public Toggle sideBySideToggle;
        // public Toggle superimposeToggle;

        public Button SideBysideButton, SectionOverlayButton, Dual3Dbutton;

        //Move selector

        // Positioning "pill" selector under/behind the active tab
        [Header("═══ MODE SELECTOR (UI) ═══")]
        [SerializeField] private RectTransform modeSelector;   // assign "Selector" Image here
        [SerializeField] private float selectorAnimDuration = 0.18f;
        [SerializeField] private AnimationCurve selectorEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        // Target X positions for each tab (anchoredPosition.x)
        [SerializeField] private float xSideBySide = -170f;
        [SerializeField] private float xSectionOverlay = 0f;
        [SerializeField] private float xDual3D = 170f;

        // Internal
        private Coroutine _selectorMoveCo;

        [Header("═══ VIEW CONTAINERS (UI) ═══")]
        [Tooltip("UI container for side-by-side view (hides when superimpose)")]
        public GameObject sideBySideContainer;
        [Tooltip("UI container for superimpose controls (shows when superimpose)")]
        public GameObject superimposeControlsContainer;

        [Header("═══ PREVIEW RENDERERS ═══")]
        [Tooltip("VehiclePreviewRenderer for Vehicle A - we get the loaded model from here")]
        public VehiclePreviewRenderer vehicleAPreview;
        [Tooltip("VehiclePreviewRenderer for Vehicle B - we get the loaded model from here")]
        public VehiclePreviewRenderer vehicleBPreview;

        [Header("═══ 3D CONTAINERS ═══")]
        [Tooltip("Container for superimpose mode (models move here)")]
        public Transform superimposeModelContainer;

        [Header("═══ SUPERIMPOSE CAMERA ═══")]
        [Tooltip("Camera for superimpose view - renders DIRECTLY to screen (no render texture)")]
        public Camera superimposeCamera;
        [Tooltip("Orbit controller for superimpose camera rotation")]
        public OrbitCameraController orbitController;


        [Header("═══ DUAL 3D (Side‑by‑Side 3D) ═══")]
        [Tooltip("Parent for the two models in Dual3D mode")]
        public Transform dual3DModelContainer;

        [Tooltip("Camera that renders the Dual3D view (direct to screen)")]
        public Camera dual3DCamera;

        [Tooltip("Primary layer used for the two vehicles while in Dual3D")]
        public int dual3DLayer = 23;

        [Tooltip("Culling mask used by the Dual3D camera. If 0, we default to just dual3DLayer.")]
        public LayerMask dual3DCullingMask = 0;

        [Header("Ground Alignment (Dual3D)")]
        [Tooltip("Optional ground Transform; if set, its world Y is used as the ground level for vehicle placement.")]
        public Transform dual3DGround;
        [Tooltip("Fallback ground Y (in Dual3D container local-space) if no Transform is set.")]
        public float dual3DGroundY = 0f;

        [Header("Gap Control (Dual3D)")]
        [Tooltip("Gap between vehicle bounds (meters)")]
        [Range(0f, 5f)]
        public float dual3DGap = 0.8f;

        [Tooltip("UI Slider to change gap at runtime (optional)")]
        public UnityEngine.UI.Slider dual3DGapSlider;

        [Tooltip("UI label to show gap value (optional)")]
        public TMPro.TMP_Text dual3DGapText;




        [Header("═══ LAYERS ═══")]
        public int vehicleALayer = 20;
        public int vehicleBLayer = 21;
        public int superimposeLayer = 22;

        [Header("═══ CLIP CONTROLS ═══")]
        public GameObject clipControlsPanel;
       // public Toggle axisXToggle;
      //  public Toggle axisYToggle;
      //  public Toggle axisZToggle;
        public Slider clipPositionSlider;
        public TMP_Text clipValueText;
        public TMP_Text clipMinText;
        public TMP_Text clipMaxText;
        public Toggle invertClipToggle;
        public Toggle showPlaneToggle;
        public Button resetClipButton;


        [Header("═══ AXIS SELECTION (Buttons) ═══")]
        [SerializeField] private Button axisXButton;
        [SerializeField] private Button axisYButton;
        [SerializeField] private Button axisZButton;

        private Button _currentAxisButton;

        [Header("Button Sprites")]
        public Sprite axisNormalSprite;
        public Sprite axisSelectedSprite;



        [Header("═══ OPACITY CONTROLS ═══")]
        public Slider vehicleAOpacitySlider;
        public Slider vehicleBOpacitySlider;
        public TMP_Text vehicleAOpacityText;
        public TMP_Text vehicleBOpacityText;
        public Toggle vehicleAVisibleToggle;
        public Toggle vehicleBVisibleToggle;

        [Header("═══ ALIGNMENT CONTROLS ═══")]

        public Button alignToBOF;
        public Button alignToSGRP;
        public Button alignToVCS;
        public Button alignToGroundPlaneButton;
        public Button alignToFWheelCenterButton;
        public Button resetAlignmentButton;

        public Sprite _alignmentDefaultSprite;
        public Sprite _alignmentSelectedSprite;

        [Header("═══ VIEW CONTROLS ═══")]
        public Button frontViewButton;
        public Button leftSideViewButton;
        public Button topViewButton;
        public Button rightSideViewButton;
        public Button rearViewButton;


        [Header("═══ VEHICLE COLORS ═══")]
        public Color vehicleAColor = new Color(0.2f, 0.6f, 1f, 1f);
        public Color vehicleBColor = new Color(1f, 0.5f, 0.2f, 1f);
        [Tooltip("Apply color tint to vehicles in superimpose mode")]
        public bool applyColorTint = true;

        [Header("═══ CLIP PLANE VISUAL ═══")]
        public bool showClipPlane = true;
        public Color clipPlaneColor = new Color(1f, 0.9f, 0f, 0.3f);

        // State
        private ComparisonViewMode _currentMode = ComparisonViewMode.SideBySide;
        public ComparisonViewMode CurrentMode => _currentMode;

        // Model references
        private GameObject _vehicleAModel;
        private GameObject _vehicleBModel;

        // Original state (to restore when exiting superimpose)
        private Transform _vehicleAOriginalParent;
        private Transform _vehicleBOriginalParent;
        private Vector3 _vehicleAOriginalLocalPos;
        private Vector3 _vehicleBOriginalLocalPos;
        private Quaternion _vehicleAOriginalLocalRot;
        private Quaternion _vehicleBOriginalLocalRot;

        // Original materials (to restore)
        private Dictionary<Renderer, Material[]> _originalMaterialsA = new Dictionary<Renderer, Material[]>();
        private Dictionary<Renderer, Material[]> _originalMaterialsB = new Dictionary<Renderer, Material[]>();
        private List<Material> _tintedMaterialsA = new List<Material>();
        private List<Material> _tintedMaterialsB = new List<Material>();

        // Clipping state
        private enum ClipAxis { X, Y, Z }
        private ClipAxis _currentAxis = ClipAxis.X;
        private float _clipPosition = 0f;
        private float _clipDirection = 1f; // 1 or -1
        private Bounds _combinedBounds;

        // Clip plane visual
        private GameObject _clipPlaneVisual;

        [SerializeField] private GameObject _clipSectionUIPanel;
        [SerializeField] private Button _showArrow;
        [SerializeField] private Button _hideArrow;


        [SerializeField] private TMP_Text _vehicleAName,_vehicleBName;
        [SerializeField] private GameObject _generateReport;
        [SerializeField]private GameObject _dummyPanel;
        // [SerializeField] private TMP_Text _vehicleBName;
        // Put this near your other UI text fields
        [SerializeField] private TMP_Text anchorStatusText;


        // Cache components once
        private Image _imgVCS,_imgBOF, _imgSGRP, _imgWheel;
        private TMP_Text _txtVCS,_txtBOF, _txtSGRP, _txtWheel;

        // === Anchor labels ===
        [Header("═══ ANCHOR LABELS ═══")]
        [SerializeField] private GameObject anchorLabelPrefab; // Assign AnchorLabelSphere prefab in Inspector
        [SerializeField] private bool showAnchorLabels = true;
        [SerializeField] private Color vehicleALabelColor = new Color(0.1f, 0.8f, 1f, 1f);
        [SerializeField] private Color vehicleBLabelColor = new Color(1f, 0.8f, 0.1f, 1f);

        // runtime lists for cleanup/show/hide
        private readonly List<GameObject> _vehicleAAnchorLabels = new();
        private readonly List<GameObject> _vehicleBAnchorLabels = new();



        #region Unity Lifecycle

        private void Start()
        {
            SetupUI();

            //if (vehicleAPreview != null) vehicleAPreview.OnVehicleChanged += _ => UpdateUIForAvailableReferences();
           // if (vehicleBPreview != null) vehicleBPreview.OnVehicleChanged += _ => UpdateUIForAvailableReferences();


            // Disable superimpose camera at start
            if (superimposeCamera != null)
                superimposeCamera.enabled = false;

            // Reset global clip properties
            ResetGlobalClipProperties();

            SetMode(ComparisonViewMode.SideBySide);

            if (modeSelector != null)
            {
                var p = modeSelector.anchoredPosition;
                modeSelector.anchoredPosition = new Vector2(xSideBySide, p.y);
            }

            UpdateAnchorButtonVisibility(AlignmentAnchor.VCS);
        }


        private void Awake()
        {
            _imgVCS = alignToVCS.GetComponent<Image>();
            _txtVCS = alignToVCS.GetComponentInChildren<TMP_Text>(true);

            _imgBOF = alignToBOF.GetComponent<Image>();
            _txtBOF = alignToBOF.GetComponentInChildren<TMP_Text>(true);

            _imgSGRP = alignToSGRP.GetComponent<Image>();
            _txtSGRP = alignToSGRP.GetComponentInChildren<TMP_Text>(true);

            _imgWheel = alignToFWheelCenterButton.GetComponent<Image>();
            _txtWheel = alignToFWheelCenterButton.GetComponentInChildren<TMP_Text>(true);
        }


        private void OnDestroy()
        {
            if (_currentMode == ComparisonViewMode.Superimpose)
            {
                ExitSuperimposeMode();
            }

            ResetGlobalClipProperties();
            CleanupTintedMaterials();
            DestroyClipPlaneVisual();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Initialize with vehicle data (called by ComparisonController)
        /// </summary>
        public void Initialize(SavedVehicleMeasurement vehicleA, SavedVehicleMeasurement vehicleB)
        {
            Debug.Log($"[VisualComparison] Initialize: {vehicleA?.vehicleName} vs {vehicleB?.vehicleName}");

            // If already in superimpose mode, refresh
            if (_currentMode == ComparisonViewMode.Superimpose)
            {
                ExitSuperimposeMode();
                EnterSuperimposeMode();
                UpdateUIForAvailableReferences();


                ClearAnchorLabels();
                CreateAnchorLabelsForVehicles();
                SetAnchorLabelsVisible(showAnchorLabels);

            }
        }

        /// <summary>
        /// Set the comparison view mode
        /// </summary>

        public void SetMode(ComparisonViewMode mode)
        {
            if (_currentMode == mode) return;

            // Cleanly exit whichever mode we're currently in
            switch (_currentMode)
            {
                case ComparisonViewMode.Superimpose: ExitSuperimposeMode(); break;
                case ComparisonViewMode.Dual3D: ExitDual3DMode(); break;
                    // SideBySide renders via RawImages (no special enter/exit)
            }

            _currentMode = mode;

            MoveSelectorToMode(mode);

            // Show/Hide relevant UI roots
            if (sideBySideContainer) sideBySideContainer.SetActive(mode == ComparisonViewMode.SideBySide);
            if (superimposeControlsContainer) superimposeControlsContainer.SetActive(mode == ComparisonViewMode.Superimpose);
            // (Optional) Show a dedicated Dual3D controls panel if you add one

            // Enter the new mode
            switch (mode)
            {
                case ComparisonViewMode.Superimpose: EnterSuperimposeMode(); break;
                case ComparisonViewMode.Dual3D: EnterDual3DMode(); break;
            }
        }

        // If you want your single toggle button to cycle all three:
        public void ToggleMode()
        {
            var next = _currentMode == ComparisonViewMode.SideBySide
                ? ComparisonViewMode.Superimpose
                : _currentMode == ComparisonViewMode.Superimpose
                    ? ComparisonViewMode.Dual3D
                    : ComparisonViewMode.SideBySide;
            SetMode(next);
        }


        #endregion

        #region UI Setup

        private void SetupUI()
        {
            // Mode buttons
            // sideBySideButton?.onClick.AddListener(() => SetMode(ComparisonViewMode.SideBySide));
            // superimposeButton?.onClick.AddListener(() => SetMode(ComparisonViewMode.Superimpose));

            // Mode toggles
            //  sideBySideToggle?.onValueChanged.AddListener(on => { if (on) SetMode(ComparisonViewMode.SideBySide); });
            //  superimposeToggle?.onValueChanged.AddListener(on => { if (on) SetMode(ComparisonViewMode.Superimpose); });
           // _switchModeButton.onClick.AddListener(OnSwitchModeButtonClicked);

            axisXButton.onClick.AddListener(() => OnAxisButtonClicked(axisXButton, ClipAxis.X));
            axisYButton.onClick.AddListener(() => OnAxisButtonClicked(axisYButton, ClipAxis.Y));
            axisZButton.onClick.AddListener(() => OnAxisButtonClicked(axisZButton, ClipAxis.Z));

            SelectAxisButton(axisXButton, ClipAxis.X);
            // Axis toggles
            //axisXToggle?.onValueChanged.AddListener(on => { if (on) SetClipAxis(ClipAxis.X); });
            // axisYToggle?.onValueChanged.AddListener(on => { if (on) SetClipAxis(ClipAxis.Y); });
            // axisZToggle?.onValueChanged.AddListener(on => { if (on) SetClipAxis(ClipAxis.Z); });
            _showArrow.onClick.AddListener(() => ShowHideClipPanel(true));
            _hideArrow.onClick.AddListener(() => ShowHideClipPanel(false));
            // Clip slider
            clipPositionSlider?.onValueChanged.AddListener(OnClipPositionChanged);

            // Invert toggle
            invertClipToggle?.onValueChanged.AddListener(OnInvertChanged);

            // Show plane toggle
            showPlaneToggle?.onValueChanged.AddListener(on => {
                showClipPlane = on;
                if (_clipPlaneVisual != null) _clipPlaneVisual.SetActive(on);
            });

            // Reset clip button
            resetClipButton?.onClick.AddListener(ResetClip);

            // Opacity sliders
            vehicleAOpacitySlider?.onValueChanged.AddListener(OnVehicleAOpacityChanged);
            vehicleBOpacitySlider?.onValueChanged.AddListener(OnVehicleBOpacityChanged);

            // Visibility toggles
            vehicleAVisibleToggle?.onValueChanged.AddListener(on => SetVehicleVisible(_vehicleAModel, on));
            vehicleBVisibleToggle?.onValueChanged.AddListener(on => SetVehicleVisible(_vehicleBModel, on));

            // Alignment buttons
            alignToBOF.onClick.AddListener(() => AlignByAnchor(AlignmentAnchor.BOF));
            alignToSGRP.onClick.AddListener(()=> AlignByAnchor(AlignmentAnchor.SGRP));
            alignToFWheelCenterButton.onClick.AddListener(()=>AlignByAnchor(AlignmentAnchor.WheelCenter));
           // alignToGroundPlaneButton.onClick.AddListener(() => AlignByAnchor(AlignmentAnchor.WheelbaseCenter));
            alignToVCS.onClick.AddListener(() => ResetAlignment());


            // View buttons
            frontViewButton?.onClick.AddListener(() => orbitController?.SetFrontView());
            rightSideViewButton?.onClick.AddListener(() => orbitController?.SetRightSideView());
            topViewButton?.onClick.AddListener(() => orbitController?.SetTopView());
            leftSideViewButton?.onClick.AddListener(() => orbitController?.SetLeftSideView());
            rearViewButton?.onClick.AddListener(() => orbitController?.SetRearView());

            // Initialize slider values
            if (vehicleAOpacitySlider != null) vehicleAOpacitySlider.value = 1f;
            if (vehicleBOpacitySlider != null) vehicleBOpacitySlider.value = 1f;
            if (vehicleAVisibleToggle != null) vehicleAVisibleToggle.isOn = true;
            if (vehicleBVisibleToggle != null) vehicleBVisibleToggle.isOn = true;
            // if (axisXToggle != null) axisXToggle.isOn = true;


            if (dual3DGapSlider != null)
            {
                dual3DGapSlider.minValue = 0f;
                dual3DGapSlider.maxValue = 5f;
                dual3DGapSlider.SetValueWithoutNotify(dual3DGap);
                dual3DGapSlider.onValueChanged.AddListener(OnDual3DGapChanged);
            }
            SideBysideButton.onClick.AddListener(()=>SetMode(ComparisonViewMode.SideBySide));
            SectionOverlayButton.onClick.AddListener(() => SetMode(ComparisonViewMode.Superimpose));
            Dual3Dbutton.onClick.AddListener(() => SetMode(ComparisonViewMode.Dual3D));

            UpdateDual3DGapLabel();



        }

        private void OnDual3DGapChanged(float value)
        {
            dual3DGap = value;
            // Re-lay out the cars only if we're currently in Dual3D
            if (_currentMode == ComparisonViewMode.Dual3D)
            {
                PositionVehiclesSideBySide_Local();
                CalculateCombinedBounds();
                // Optional: keep camera centered on both cars as the gap changes
                FocusDual3DCamera();
            }
            UpdateDual3DGapLabel();
        }

        private void UpdateDual3DGapLabel()
        {
            if (dual3DGapText != null)
                dual3DGapText.text = $"{dual3DGap:F2} m";
        }
        // --- Add near your other fields ---
        private string _labelAlignBOF;
        private string _labelAlignSGRP;
        private string _labelAlignWheelCenter;

        // Formats the "(missing on NAME)" suffix in small red text using TMP rich-text tags
        private static string FormatMissingSuffix(string who)
        {
            // Choose your red: FF0000 (pure red) or FF3B30 (iOS red). Keeping bold off to avoid layout shift.
            return $" \n<size=70%><color=#FF0000>(VAL Data {who})</color></size>";
        }

        // Utility: safely get the TMP_Text inside a Button
        private static TMP_Text GetButtonLabel(Button btn)
        {
            return btn != null ? btn.GetComponentInChildren<TMP_Text>(true) : null;
        }

        // One‑time lazy init of the original button labels
        private void EnsureButtonLabelsCached()
        {
            if (string.IsNullOrEmpty(_labelAlignBOF) && alignToBOF != null)
            {
                var t = GetButtonLabel(alignToBOF);
                _labelAlignBOF = t != null ? t.text : "Align BOF";
            }
            if (string.IsNullOrEmpty(_labelAlignSGRP) && alignToSGRP != null)
            {
                var t = GetButtonLabel(alignToSGRP);
                _labelAlignSGRP = t != null ? t.text : "Align SGRP";
            }
            if (string.IsNullOrEmpty(_labelAlignWheelCenter) && alignToFWheelCenterButton != null)
            {
                var t = GetButtonLabel(alignToFWheelCenterButton);
                _labelAlignWheelCenter = t != null ? t.text : "Align Wheel Center";
            }
        }

        // Helper: build a friendly "who is missing" string using A/B names
        private static string MissingWho(bool aMissing, bool bMissing, string nameA, string nameB)
        {
            return (aMissing && bMissing) ? $"{nameA} & {nameB}" : aMissing ? nameA : nameB;
        }


        // Updated method with only UI-text changes
        private void UpdateUIForAvailableReferences()
        {
            Debug.Log("[VisualComparison] UpdateUIForAvailableReferences()");
            EnsureButtonLabelsCached();

            var txtBOF = GetButtonLabel(alignToBOF);
            var txtSGRP = GetButtonLabel(alignToSGRP);
            var txtWheel = GetButtonLabel(alignToFWheelCenterButton);

            // Resolve display names for A/B (UI label -> prefab name -> fallback "A/B")
            string nameA = !string.IsNullOrWhiteSpace(_vehicleAName?.text) ? _vehicleAName.text
                         : _vehicleAModel != null ? _vehicleAModel.name : "A";

            string nameB = !string.IsNullOrWhiteSpace(_vehicleBName?.text) ? _vehicleBName.text
                         : _vehicleBModel != null ? _vehicleBModel.name : "B";

            // If models not ready yet
            if (_vehicleAModel == null || _vehicleBModel == null)
            {
                if (alignToBOF) alignToBOF.interactable = false;
                if (alignToSGRP) alignToSGRP.interactable = false;
                if (alignToFWheelCenterButton) alignToFWheelCenterButton.interactable = false;

                if (txtBOF != null && !string.IsNullOrEmpty(_labelAlignBOF))
                    txtBOF.text = _labelAlignBOF + FormatMissingSuffix("vehicles not ready");
                if (txtSGRP != null && !string.IsNullOrEmpty(_labelAlignSGRP))
                    txtSGRP.text = _labelAlignSGRP + FormatMissingSuffix("vehicles not ready");
                if (txtWheel != null && !string.IsNullOrEmpty(_labelAlignWheelCenter))
                    txtWheel.text = _labelAlignWheelCenter + FormatMissingSuffix("vehicles not ready");
                return;
            }

            var dataA = _vehicleAModel.GetComponent<VehiclePrefabData>();
            var dataB = _vehicleBModel.GetComponent<VehiclePrefabData>();

            // If prefab data is missing on either vehicle
            if (dataA == null || dataB == null)
            {
                string who = (dataA == null && dataB == null) ? $"{nameA} & {nameB}" : (dataA == null ? nameA : nameB);

                if (alignToBOF) alignToBOF.interactable = false;
                if (alignToSGRP) alignToSGRP.interactable = false;
                if (alignToFWheelCenterButton) alignToFWheelCenterButton.interactable = false;

                if (txtBOF != null && !string.IsNullOrEmpty(_labelAlignBOF))
                    txtBOF.text = _labelAlignBOF + FormatMissingSuffix($"data missing on {who}");
                if (txtSGRP != null && !string.IsNullOrEmpty(_labelAlignSGRP))
                    txtSGRP.text = _labelAlignSGRP + FormatMissingSuffix($"data missing on {who}");
                if (txtWheel != null && !string.IsNullOrEmpty(_labelAlignWheelCenter))
                    txtWheel.text = _labelAlignWheelCenter + FormatMissingSuffix($"data missing on {who}");
                return;
            }

            // Determine availability per anchor
            bool aBOFMissing = dataA.refBOF == null;
            bool bBOFMissing = dataB.refBOF == null;
            bool aSGRPMissing = dataA.refSGRP == null;
            bool bSGRPMissing = dataB.refSGRP == null;
            bool aWCMissing = dataA.refWheelCenter == null;
            bool bWCMissing = dataB.refWheelCenter == null;

            bool hasBOF = !aBOFMissing && !bBOFMissing;
            bool hasSGRP = !aSGRPMissing && !bSGRPMissing;
            bool hasWheelCenter = !aWCMissing && !bWCMissing;

            // BOF button
            if (alignToBOF) alignToBOF.interactable = hasBOF;
            if (txtBOF != null && !string.IsNullOrEmpty(_labelAlignBOF))
            {
                if (hasBOF)
                    txtBOF.text = _labelAlignBOF;
                else
                    // Only show "(missing on <vehicle>)" — no extra verbiage
                    txtBOF.text = _labelAlignBOF + FormatMissingSuffix($"missing on {MissingWho(aBOFMissing, bBOFMissing, nameA, nameB)}");
            }

            // SGRP button
            if (alignToSGRP) alignToSGRP.interactable = hasSGRP;
            if (txtSGRP != null && !string.IsNullOrEmpty(_labelAlignSGRP))
            {
                if (hasSGRP)
                    txtSGRP.text = _labelAlignSGRP;
                else
                    txtSGRP.text = _labelAlignSGRP + FormatMissingSuffix($"missing on {MissingWho(aSGRPMissing, bSGRPMissing, nameA, nameB)}");
            }

            // Wheel Center button
            if (alignToFWheelCenterButton) alignToFWheelCenterButton.interactable = hasWheelCenter;
            if (txtWheel != null && !string.IsNullOrEmpty(_labelAlignWheelCenter))
            {
                if (hasWheelCenter)
                    txtWheel.text = _labelAlignWheelCenter;
                else
                    txtWheel.text = _labelAlignWheelCenter + FormatMissingSuffix($"missing on {MissingWho(aWCMissing, bWCMissing, nameA, nameB)}");
            }
        }

        private void MoveSelectorToMode(ComparisonViewMode mode)
        {
            if (modeSelector == null) return;

            float targetX = mode switch
            {
                ComparisonViewMode.SideBySide => xSideBySide,
                ComparisonViewMode.Superimpose => xSectionOverlay,
                ComparisonViewMode.Dual3D => xDual3D,
                _ => 0f
            };

            // Stop any in-flight animation
            if (_selectorMoveCo != null) StopCoroutine(_selectorMoveCo);
            _selectorMoveCo = StartCoroutine(AnimateSelectorX(targetX, selectorAnimDuration, selectorEase));
        }

        private IEnumerator AnimateSelectorX(float targetX, float duration, AnimationCurve ease)
        {
            Vector2 start = modeSelector.anchoredPosition;
            Vector2 end = new Vector2(targetX, start.y);
            if (duration <= 0f)
            {
                modeSelector.anchoredPosition = end;
                yield break;
            }

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration; // unscaled so UI anim isn’t slowed by timescale
                float e = ease != null ? ease.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
                modeSelector.anchoredPosition = Vector2.LerpUnclamped(start, end, e);
                yield return null;
            }
            modeSelector.anchoredPosition = end;
            _selectorMoveCo = null;
        }

        public void ShowHideClipPanel(bool _activate)
        {
            if (_clipSectionUIPanel != null)
            {
                _clipSectionUIPanel.SetActive(_activate);
            }


        }
        public TMP_Text _carAvisibilityText;
        public TMP_Text _carBvisibilityText;
        public void UpdateVehicleNames(int index,string _name)
        {
            if (index == 0)
            {
                _vehicleAName.text = _name;
                _carAvisibilityText.text= _name;
            }else if (index == 1)
            {
                _vehicleBName.text= _name;
                _carBvisibilityText.text = _name;
            }
            else
            {
                _vehicleAName.text = "";

            }
        }

       /* private bool isOnSidebySide = true;
        private void OnSwitchModeButtonClicked()
        {
            Animator anim= _switchModeButton.GetComponent<Animator>();
            isOnSidebySide = !isOnSidebySide;
            if (!isOnSidebySide)
            {
                SetMode(ComparisonViewMode.Superimpose);
            }
            else
            {
                SetMode(ComparisonViewMode.SideBySide);
            }
                anim.SetTrigger("Switch");

        }*/


        private void OnAxisButtonClicked(Button clickedBtn, ClipAxis axis)
        {
            if (_currentAxisButton == clickedBtn)
                return; // already selected

            SelectAxisButton(clickedBtn, axis);
        }
        private void OnAxisChanged(ClipAxis axis)
        {
            SetClipAxis(axis);
            _currentAxis = axis;
            UpdateSliderRange();
            UpdateClipPlaneVisual();
            UpdateClipPlaneVisual();
        }
        private void SelectAxisButton(Button btn, ClipAxis axis)
        {
            // Un-select old one
            if (_currentAxisButton != null)
            {
                var oldImg = _currentAxisButton.GetComponent<Image>();
                if (oldImg != null)
                    oldImg.sprite = axisNormalSprite;

                _currentAxisButton.interactable = true;
            }

            // Select new one
            var newImg = btn.GetComponent<Image>();
            if (newImg != null)
                newImg.sprite = axisSelectedSprite;

            btn.interactable = false;
            _currentAxisButton = btn;

            // Notify your existing code
            OnAxisChanged(axis);
        }
       /* private void UpdateModeButtonStates()
        {
            bool isSideBySide = _currentMode == ComparisonViewMode.SideBySide;

           // if (sideBySideToggle != null) sideBySideToggle.SetIsOnWithoutNotify(isSideBySide);
           // if (superimposeToggle != null) superimposeToggle.SetIsOnWithoutNotify(!isSideBySide);
        }*/

        #endregion

        #region Superimpose Mode

        private void EnterSuperimposeMode()
        {
            Debug.Log("[VisualComparison] === ENTERING SUPERIMPOSE MODE ===");

            // Get models from preview renderers
            _vehicleAModel = vehicleAPreview?.CurrentVehicle;
            _vehicleBModel = vehicleBPreview?.CurrentVehicle;

            if (_vehicleAModel == null && _vehicleBModel == null)
            {
                Debug.LogWarning("[VisualComparison] No models loaded!");
                return;
            }

            Debug.Log($"[VisualComparison] Vehicle A: {(_vehicleAModel != null ? _vehicleAModel.name : "NULL")}");
            Debug.Log($"[VisualComparison] Vehicle B: {(_vehicleBModel != null ? _vehicleBModel.name : "NULL")}");

            StoreOriginalState();


            MoveModelsToSuperimpose();



            _originalMaterialsA.Clear();
            _originalMaterialsB.Clear();
            _tintedMaterialsA.Clear();
            _tintedMaterialsB.Clear();

            if (_vehicleAModel != null)
                ApplyClipShaderTinted(_vehicleAModel, _originalMaterialsA, _tintedMaterialsA, vehicleAColor, applyColorTint);

            if (_vehicleBModel != null)
                ApplyClipShaderTinted(_vehicleBModel, _originalMaterialsB, _tintedMaterialsB, vehicleBColor, applyColorTint);


            // After ApplyClipShaderTinted(...) for both A and B:
            if (vehicleAOpacitySlider != null) SetOpacity(_tintedMaterialsA, vehicleAOpacitySlider.value);
            if (vehicleBOpacitySlider != null) SetOpacity(_tintedMaterialsB, vehicleBOpacitySlider.value);

       
            CalculateCombinedBounds();

      
            UpdateSliderRange();

            PositionCamera();

            CreateClipPlaneVisual();
            CreateAnchorLabelsForVehicles();
            SetAnchorLabelsVisible(showAnchorLabels);

            if (superimposeCamera != null)
            {
                superimposeCamera.enabled = true;
                superimposeCamera.targetTexture = null;
            }
            if (vehicleAPreview?.previewCamera != null)
                vehicleAPreview.previewCamera.enabled = false;
            if (vehicleBPreview?.previewCamera != null)
                vehicleBPreview.previewCamera.enabled = false;

            // Apply initial clipping
            ApplyGlobalClipping();
            _generateReport.SetActive(false);
            _dummyPanel.SetActive(false);

            Debug.Log("[VisualComparison] Superimpose mode ACTIVE");
            UpdateUIForAvailableReferences();
        }
        // VisualComparisonMode.cs  (inside class)
        
        private void AlignByAnchor(AlignmentAnchor anchor)
        {
           // Debug.Log("BOF CALLED");
            if (_vehicleAModel == null || _vehicleBModel == null)
            {
                Debug.LogWarning("[VisualComparison] AlignByAnchor: A or B is null.");
                return;
            }

            var dataA = _vehicleAModel.GetComponent<VehiclePrefabData>();
            var dataB = _vehicleBModel.GetComponent<VehiclePrefabData>();
            if (dataA == null || dataB == null)
            {
                Debug.LogWarning("[VisualComparison] VehiclePrefabData missing. Falling back to AlignCenters().");
                AlignCenters();
                return;
            }

            // Get anchor transforms (prefers real locators; falls back to a runtime anchor located at the computed point)
            Transform aAnchor = dataA.GetAnchorTransform(anchor);
            Transform bAnchor = dataB.GetAnchorTransform(anchor);

            // Check whether we actually had the requested locator vs a fallback
            bool aHadLocator = (anchor == AlignmentAnchor.BOF && dataA.refBOF != null)
                            || (anchor == AlignmentAnchor.SGRP && dataA.refSGRP != null)
                            || (anchor == AlignmentAnchor.WheelCenter && dataA.refWheelCenter != null);
            bool bHadLocator = (anchor == AlignmentAnchor.BOF && dataB.refBOF != null)
                            || (anchor == AlignmentAnchor.SGRP && dataB.refSGRP != null)
                            || (anchor == AlignmentAnchor.WheelCenter && dataB.refWheelCenter != null);

            if (!aHadLocator || !bHadLocator)
                Debug.LogWarning($"[VisualComparison] '{anchor}' used fallback on: A={(!aHadLocator ? "fallback" : "locator")} B={(!bHadLocator ? "fallback" : "locator")}");
           
            // Parent space for consistent math
            var parent = superimposeModelContainer != null ? superimposeModelContainer : _vehicleBModel.transform.parent;

            // Convert WORLD anchor positions -> PARENT LOCAL space
            Vector3 aLocal = parent.InverseTransformPoint(aAnchor.position);
            Vector3 bLocal = parent.InverseTransformPoint(bAnchor.position);
            Vector3 deltaLocal = aLocal - bLocal;

            // Move ONLY Vehicle B
            _vehicleBModel.transform.localPosition += deltaLocal;

            // OPTIONAL: Also match orientation if both are true locators (skip if either is a fallback)
            // Comment this block out if you want translation-only alignment.
            if (aHadLocator && bHadLocator)
            {
                // Bring both anchor rotations into parent local space
                Quaternion aRotLocal = Quaternion.Inverse(parent.rotation) * aAnchor.rotation;
                Quaternion bRotLocal = Quaternion.Inverse(parent.rotation) * bAnchor.rotation;
                Quaternion rotDeltaLocal = aRotLocal * Quaternion.Inverse(bRotLocal);

                // Rotate B around its anchor to match A's anchor orientation
                // 1) Move B so its anchor is at the parent origin
                Vector3 bRootToAnchor = _vehicleBModel.transform.localToWorldMatrix.inverse.MultiplyPoint3x4(bAnchor.position);
                // Simpler: rotate around world pos of bAnchor
                _vehicleBModel.transform.RotateAround(bAnchor.position, parent.right, 0f); // no-op to ensure cached transforms
                _vehicleBModel.transform.rotation = rotDeltaLocal * _vehicleBModel.transform.rotation;

                // After rotation, re-do the translation to re-snap anchors (rotation can slightly change positions)
                aLocal = parent.InverseTransformPoint(aAnchor.position);
                bLocal = parent.InverseTransformPoint(bAnchor.position);
                deltaLocal = aLocal - bLocal;
                _vehicleBModel.transform.localPosition += deltaLocal;
            }

            // Recompute bounds & sliders and apply clipping like your other alignments
            CalculateCombinedBounds();
            UpdateSliderRange();
            ApplyGlobalClipping();
            UpdateAnchorButtonVisibility(anchor);
            // UpdateUIForAvailableReferences();
            Debug.Log($"[VisualComparison] AlignByAnchor {anchor} -> Δ(local)={deltaLocal}");
        }


        private void SetButtonState(Image img, TMP_Text label, bool selected)
        {
            img.sprite = selected ? _alignmentSelectedSprite : _alignmentDefaultSprite;
            label.color = selected ? Color.black : Color.white;
        }

        private void UpdateAnchorButtonVisibility(AlignmentAnchor anchor)
        {
            // Reset all to default first
            SetButtonState(_imgBOF, _txtBOF, false);
            SetButtonState(_imgSGRP, _txtSGRP, false);
            SetButtonState(_imgWheel, _txtWheel, false);
            SetButtonState(_imgVCS, _txtVCS, false);
            // Enable the selected one
            switch (anchor)
            {
                case AlignmentAnchor.BOF:
                    SetButtonState(_imgBOF, _txtBOF, true);
                    break;

                case AlignmentAnchor.SGRP:
                    SetButtonState(_imgSGRP, _txtSGRP, true);
                    break;

                case AlignmentAnchor.WheelCenter:
                    SetButtonState(_imgWheel, _txtWheel, true);
                    break;

                case AlignmentAnchor.VCS:
                    SetButtonState(_imgVCS,_txtVCS, true);
                    break;
            }
        }


        [SerializeField] private ComparisonController _comparisonController;

        private void ExitSuperimposeMode()
        {
            Debug.Log("[VisualComparison] === EXITING SUPERIMPOSE MODE ===");

            // Reset global clip properties FIRST
            ResetGlobalClipProperties();

            // Restore original materials
            RestoreOriginalMaterials();

            // Move models back
            RestoreModelsToOriginal();

            // Destroy clip plane visual
            DestroyClipPlaneVisual();

            // Cleanup tinted materials
            CleanupTintedMaterials();

            ClearAnchorLabels();

            // Disable superimpose camera
            if (superimposeCamera != null)
                superimposeCamera.enabled = false;

            // Re-enable preview cameras
            if (vehicleAPreview?.previewCamera != null)
                vehicleAPreview.previewCamera.enabled = true;
            if (vehicleBPreview?.previewCamera != null)
                vehicleBPreview.previewCamera.enabled = true;

            // Tell preview renderers to re-render
            vehicleAPreview?.RequestRender();
            vehicleBPreview?.RequestRender();

            if(_generateReport!=null)
            _generateReport.SetActive(true);
            if(_dummyPanel!=null)
            _dummyPanel.SetActive(true);
            _comparisonController?.ResetBothViews();
            Debug.Log("[VisualComparison] Superimpose mode DISABLED");
        }

        private void StoreOriginalState()
        {
            if (_vehicleAModel != null)
            {
                _vehicleAOriginalParent = _vehicleAModel.transform.parent;
                _vehicleAOriginalLocalPos = _vehicleAModel.transform.localPosition;
                _vehicleAOriginalLocalRot = _vehicleAModel.transform.localRotation;
            }

            if (_vehicleBModel != null)
            {
                _vehicleBOriginalParent = _vehicleBModel.transform.parent;
                _vehicleBOriginalLocalPos = _vehicleBModel.transform.localPosition;
                _vehicleBOriginalLocalRot = _vehicleBModel.transform.localRotation;
            }
        }

        private void MoveModelsToSuperimpose()
        {
            if (superimposeModelContainer == null)
            {
                Debug.LogError("[VisualComparison] superimposeModelContainer not assigned!");
                return;
            }

            if (_vehicleAModel != null)
            {
                _vehicleAModel.transform.SetParent(superimposeModelContainer);
                _vehicleAModel.transform.localPosition = Vector3.zero;
                _vehicleAModel.transform.localRotation = Quaternion.identity;
                SetLayerRecursively(_vehicleAModel, superimposeLayer);
                Debug.Log($"[VisualComparison] Moved {_vehicleAModel.name} to superimpose (Layer {superimposeLayer})");
            }

            if (_vehicleBModel != null)
            {
                _vehicleBModel.transform.SetParent(superimposeModelContainer);
                _vehicleBModel.transform.localPosition = Vector3.zero;
                _vehicleBModel.transform.localRotation = Quaternion.identity;
                SetLayerRecursively(_vehicleBModel, superimposeLayer);
                Debug.Log($"[VisualComparison] Moved {_vehicleBModel.name} to superimpose (Layer {superimposeLayer})");
            }
        }

        private void RestoreModelsToOriginal()
        {
            if (_vehicleAModel != null && _vehicleAOriginalParent != null)
            {
                _vehicleAModel.transform.SetParent(_vehicleAOriginalParent);
                _vehicleAModel.transform.localPosition = _vehicleAOriginalLocalPos;
                _vehicleAModel.transform.localRotation = _vehicleAOriginalLocalRot;
                SetLayerRecursively(_vehicleAModel, vehicleALayer);
            }

            if (_vehicleBModel != null && _vehicleBOriginalParent != null)
            {
                _vehicleBModel.transform.SetParent(_vehicleBOriginalParent);
                _vehicleBModel.transform.localPosition = _vehicleBOriginalLocalPos;
                _vehicleBModel.transform.localRotation = _vehicleBOriginalLocalRot;
                SetLayerRecursively(_vehicleBModel, vehicleBLayer);
            }
        }

        #endregion

        #region Color Tint

        private void ApplyColorTint()
        {
            // Store and tint Vehicle A materials
            if (_vehicleAModel != null)
            {
                _originalMaterialsA.Clear();
                foreach (var renderer in _vehicleAModel.GetComponentsInChildren<Renderer>())
                {
                    _originalMaterialsA[renderer] = renderer.sharedMaterials;

                    Material[] tintedMats = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var original = renderer.sharedMaterials[i];
                        var tinted = original != null ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));

                        // Apply blue tint
                        if (tinted.HasProperty("_BaseColor"))
                            tinted.SetColor("_BaseColor", vehicleAColor);
                        else if (tinted.HasProperty("_Color"))
                            tinted.SetColor("_Color", vehicleAColor);

                        tintedMats[i] = tinted;
                        _tintedMaterialsA.Add(tinted);
                    }
                    renderer.materials = tintedMats;
                }
            }

            // Store and tint Vehicle B materials
            if (_vehicleBModel != null)
            {
                _originalMaterialsB.Clear();
                foreach (var renderer in _vehicleBModel.GetComponentsInChildren<Renderer>())
                {
                    _originalMaterialsB[renderer] = renderer.sharedMaterials;

                    Material[] tintedMats = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var original = renderer.sharedMaterials[i];
                        var tinted = original != null ? new Material(original) : new Material(Shader.Find("Universal Render Pipeline/Lit"));

                        // Apply orange tint
                        if (tinted.HasProperty("_BaseColor"))
                            tinted.SetColor("_BaseColor", vehicleBColor);
                        else if (tinted.HasProperty("_Color"))
                            tinted.SetColor("_Color", vehicleBColor);

                        tintedMats[i] = tinted;
                        _tintedMaterialsB.Add(tinted);
                    }
                    renderer.materials = tintedMats;
                }
            }
        }

        private void RestoreOriginalMaterials()
        {
            foreach (var kvp in _originalMaterialsA)
            {
                if (kvp.Key != null)
                    kvp.Key.sharedMaterials = kvp.Value;
            }
            _originalMaterialsA.Clear();

            foreach (var kvp in _originalMaterialsB)
            {
                if (kvp.Key != null)
                    kvp.Key.sharedMaterials = kvp.Value;
            }
            _originalMaterialsB.Clear();
        }

        private void CleanupTintedMaterials()
        {
            foreach (var mat in _tintedMaterialsA)
                if (mat != null) Destroy(mat);
            _tintedMaterialsA.Clear();

            foreach (var mat in _tintedMaterialsB)
                if (mat != null) Destroy(mat);
            _tintedMaterialsB.Clear();
        }

        #endregion

        #region Clip Shader Integration (Drop-in)

        // Reuse your existing dictionaries/lists in this class:
        // _originalMaterialsA, _originalMaterialsB, _tintedMaterialsA, _tintedMaterialsB

        private Shader FindClipShader()
        {
            // URP detection like in your other components
            bool isURP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            var s = Shader.Find(isURP
                ? "VehicleMeasurement/ClipSection_URP"
                : "VehicleMeasurement/ClipSection");
            if (s == null)
            {
                // Fallback: try the other name just in case
                s = Shader.Find("VehicleMeasurement/ClipSection");
            }
            if (s == null)
            {
                Debug.LogError("[VisualComparison] ClipSection shader not found! Ensure it's in the project.");
            }
            return s;
        }

        /// <summary>
        /// Make sure we have the original shared-material arrays stored for later restore.
        /// </summary>
        private void StoreOriginalMaterialsIfMissing(GameObject root, Dictionary<Renderer, Material[]> dict)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!dict.ContainsKey(r))
                {
                    // Keep the exact sharedMaterials for faithful restore
                    dict[r] = r.sharedMaterials;
                }
            }
        }

        /// <summary>
        /// Apply the ClipSection shader to all renderers under 'root', preserving maps & colors,
        /// and (optionally) applying A/B tint. Also tracks the new materials in _tintedMaterials*
        /// so your opacity sliders keep working as-is.
        /// </summary>

        // === REPLACE your previous ApplyClipShaderTinted with THIS version ===
        private void ApplyClipShaderTinted(
            GameObject root,
            Dictionary<Renderer, Material[]> originalsDict,
            List<Material> outNewMaterials,
            Color tint,
            bool alsoTint)
        {
            if (root == null) return;

            var clipShader = FindClipShader();
            if (clipShader == null) return;

            // Make sure we can restore later
            StoreOriginalMaterialsIfMissing(root, originalsDict);

            int replacedCount = 0;

            // Include inactive children just in case
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0) continue;

                var replaced = new Material[shared.Length];

                for (int i = 0; i < shared.Length; i++)
                {
                    var src = shared[i];

                    // 1) Create a brand-new material WITH the clip shader.
                    //    Never set m.shader after constructing from 'src' to avoid Variant errors.
                    var m = new Material(clipShader);

                    // 2) Try to copy common properties from the source (safe across shaders).
                    if (src != null)
                    {
                        try { m.CopyPropertiesFromMaterial(src); }
                        catch { /* Some shaders throw; we'll do explicit copies below */ }

                        // Explicit texture copies
                        if (src.HasProperty("_BaseMap") && m.HasProperty("_BaseMap"))
                            m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                        if (src.HasProperty("_MainTex") && m.HasProperty("_MainTex"))
                            m.SetTexture("_MainTex", src.GetTexture("_MainTex"));

                        // Metallic & smoothness
                        if (src.HasProperty("_Metallic") && m.HasProperty("_Metallic"))
                            m.SetFloat("_Metallic", src.GetFloat("_Metallic"));

                        float smooth = 0.5f;
                        if (src.HasProperty("_Smoothness")) smooth = src.GetFloat("_Smoothness");
                        else if (src.HasProperty("_Glossiness")) smooth = src.GetFloat("_Glossiness");
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);

                        // Base color (preserve or tint)
                        if (alsoTint)
                        {
                            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                            else if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                        }
                        else
                        {
                            if (src.HasProperty("_BaseColor") && m.HasProperty("_BaseColor"))
                                m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                            else if (src.HasProperty("_Color") && m.HasProperty("_Color"))
                                m.SetColor("_Color", src.GetColor("_Color"));
                        }
                    }
                    else
                    {
                        // No source material; just apply tint or default white
                        var c = alsoTint ? tint : Color.white;
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                        else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                    }

                    replaced[i] = m;
                    outNewMaterials.Add(m);
                    replacedCount++;
                }

                // Assign runtime instances
                renderer.materials = replaced;
            }

            Debug.Log($"[VisualComparison] Applied clip shader to {replacedCount} materials under {root.name}");
        }


        /// <summary>
        /// Remove any runtime materials we created for superimpose mode.
        /// (Your existing CleanupTintedMaterials already does this; this is optional)
        /// </summary>
        private void CleanupClipMaterialsLists()
        {
            // No-op because you already call CleanupTintedMaterials();
            // This method is left here for clarity and future tweaks if needed.
        }

        #endregion


        #region Clipping - USING CORRECT GLOBAL SHADER PROPERTIES

        private void SetClipAxis(ClipAxis axis)
        {
            _currentAxis = axis;
            UpdateSliderRange();
            ApplyGlobalClipping();
            UpdateClipPlaneVisual();
        }

        private void OnClipPositionChanged(float value)
        {
            _clipPosition = value;
            ApplyGlobalClipping();
            UpdateClipPlaneVisual();

            if (clipValueText != null)
                clipValueText.text = $"Position: {value:F1} mm";
        }

        private void OnInvertChanged(bool invert)
        {
            _clipDirection = invert ? -1f : 1f;
            ApplyGlobalClipping();
        }

        private void ResetClip()
        {
            // Reset to center
            float center = 0f;
            switch (_currentAxis)
            {
                case ClipAxis.X: center = _combinedBounds.center.x; break;
                case ClipAxis.Y: center = _combinedBounds.center.y; break;
                case ClipAxis.Z: center = _combinedBounds.center.z; break;
            }

            _clipPosition = center;
            _clipDirection = 1f;

            if (clipPositionSlider != null)
                clipPositionSlider.SetValueWithoutNotify(center);
            if (invertClipToggle != null)
                invertClipToggle.SetIsOnWithoutNotify(false);

            ApplyGlobalClipping();
            UpdateClipPlaneVisual();
        }

        /// <summary>
        /// Apply clipping using GLOBAL shader properties that match your shader:
        /// _GlobalClipXEnabled, _GlobalClipXPosition, _GlobalClipXDirection
        /// _GlobalClipYEnabled, _GlobalClipYPosition, _GlobalClipYDirection
        /// _GlobalClipZEnabled, _GlobalClipZPosition, _GlobalClipZDirection
        /// </summary>
        private void ApplyGlobalClipping()
        {
            // Reset all axes first
            Shader.SetGlobalFloat("_GlobalClipXEnabled", 0f);
            Shader.SetGlobalFloat("_GlobalClipYEnabled", 0f);
            Shader.SetGlobalFloat("_GlobalClipZEnabled", 0f);

            // Enable only the active axis
            switch (_currentAxis)
            {
                case ClipAxis.X:
                    Shader.SetGlobalFloat("_GlobalClipXEnabled", 1f);
                    Shader.SetGlobalFloat("_GlobalClipXPosition", _clipPosition);
                    Shader.SetGlobalFloat("_GlobalClipXDirection", _clipDirection);
                    Debug.Log($"[VisualComparison] Clip X: pos={_clipPosition:F1}, dir={_clipDirection}");
                    break;

                case ClipAxis.Y:
                    Shader.SetGlobalFloat("_GlobalClipYEnabled", 1f);
                    Shader.SetGlobalFloat("_GlobalClipYPosition", _clipPosition);
                    Shader.SetGlobalFloat("_GlobalClipYDirection", _clipDirection);
                    Debug.Log($"[VisualComparison] Clip Y: pos={_clipPosition:F1}, dir={_clipDirection}");
                    break;

                case ClipAxis.Z:
                    Shader.SetGlobalFloat("_GlobalClipZEnabled", 1f);
                    Shader.SetGlobalFloat("_GlobalClipZPosition", _clipPosition);
                    Shader.SetGlobalFloat("_GlobalClipZDirection", _clipDirection);
                    Debug.Log($"[VisualComparison] Clip Z: pos={_clipPosition:F1}, dir={_clipDirection}");
                    break;
            }
        }

        /// <summary>
        /// Reset all global clip properties (disable clipping)
        /// </summary>
        private void ResetGlobalClipProperties()
        {
            Shader.SetGlobalFloat("_GlobalClipXEnabled", 0f);
            Shader.SetGlobalFloat("_GlobalClipYEnabled", 0f);
            Shader.SetGlobalFloat("_GlobalClipZEnabled", 0f);
            Shader.SetGlobalFloat("_GlobalClipXPosition", 0f);
            Shader.SetGlobalFloat("_GlobalClipYPosition", 0f);
            Shader.SetGlobalFloat("_GlobalClipZPosition", 0f);
            Shader.SetGlobalFloat("_GlobalClipXDirection", 1f);
            Shader.SetGlobalFloat("_GlobalClipYDirection", 1f);
            Shader.SetGlobalFloat("_GlobalClipZDirection", 1f);
        }

        private void UpdateSliderRange()
        {
            if (clipPositionSlider == null) return;

            float min = 0, max = 0, center = 0;

            switch (_currentAxis)
            {
                case ClipAxis.X:
                    min = _combinedBounds.min.x;
                    max = _combinedBounds.max.x;
                    center = _combinedBounds.center.x;
                    break;
                case ClipAxis.Y:
                    min = _combinedBounds.min.y;
                    max = _combinedBounds.max.y;
                    center = _combinedBounds.center.y;
                    break;
                case ClipAxis.Z:
                    min = _combinedBounds.min.z;
                    max = _combinedBounds.max.z;
                    center = _combinedBounds.center.z;
                    break;
            }

            // Add margin
            float margin = (max - min) * 0.1f;
            clipPositionSlider.minValue = min - margin;
            clipPositionSlider.maxValue = max + margin;
            clipPositionSlider.SetValueWithoutNotify(center);
            _clipPosition = center;

            if (clipMinText != null) clipMinText.text = $"{min:F0}";
            if (clipMaxText != null) clipMaxText.text = $"{max:F0}";
            if (clipValueText != null) clipValueText.text = $"Position: {center:F1} mm";

            Debug.Log($"[VisualComparison] Slider range: {min:F0} to {max:F0}, center: {center:F0}");
        }

        #endregion

        #region Clip Plane Visual

        private void CreateClipPlaneVisual()
        {
            if (_clipPlaneVisual != null) return;

            _clipPlaneVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _clipPlaneVisual.name = "ClipPlaneVisual";
            _clipPlaneVisual.transform.SetParent(superimposeModelContainer);
            _clipPlaneVisual.layer = superimposeLayer;

            // Remove collider
            var col = _clipPlaneVisual.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Transparent material
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = clipPlaneColor;
            _clipPlaneVisual.GetComponent<Renderer>().material = mat;

            // Size
            float size = _combinedBounds.size.magnitude * 1.2f;
            _clipPlaneVisual.transform.localScale = new Vector3(size, size, 1);

            _clipPlaneVisual.SetActive(showClipPlane);
            UpdateClipPlaneVisual();
        }

        private void DestroyClipPlaneVisual()
        {
            if (_clipPlaneVisual != null)
            {
                Destroy(_clipPlaneVisual);
                _clipPlaneVisual = null;
            }
        }

        private void UpdateClipPlaneVisual()
        {
            if (_clipPlaneVisual == null) return;

            Vector3 pos = _combinedBounds.center;
            Quaternion rot = Quaternion.identity;

            switch (_currentAxis)
            {
                case ClipAxis.X:
                    pos.x = _clipPosition;
                    rot = Quaternion.Euler(0, 90, 0);
                    break;
                case ClipAxis.Y:
                    pos.y = _clipPosition;
                    rot = Quaternion.Euler(90, 0, 0);
                    break;
                case ClipAxis.Z:
                    pos.z = _clipPosition;
                    rot = Quaternion.identity;
                    break;
            }

            _clipPlaneVisual.transform.position = pos;
            _clipPlaneVisual.transform.rotation = rot;
        }

        #endregion

        #region Bounds & Camera

        private void CalculateCombinedBounds()
        {
            _combinedBounds = new Bounds(Vector3.zero, Vector3.one * 4f); // Default
            bool hasAny = false;

            if (_vehicleAModel != null)
            {
                foreach (var r in _vehicleAModel.GetComponentsInChildren<Renderer>())
                {
                    if (!hasAny)
                    {
                        _combinedBounds = r.bounds;
                        hasAny = true;
                    }
                    else
                    {
                        _combinedBounds.Encapsulate(r.bounds);
                    }
                }
            }

            if (_vehicleBModel != null)
            {
                foreach (var r in _vehicleBModel.GetComponentsInChildren<Renderer>())
                {
                    if (!hasAny)
                    {
                        _combinedBounds = r.bounds;
                        hasAny = true;
                    }
                    else
                    {
                        _combinedBounds.Encapsulate(r.bounds);
                    }
                }
            }

            Debug.Log($"[VisualComparison] Bounds: center={_combinedBounds.center}, size={_combinedBounds.size}");
        }

        private void PositionCamera()
        {
            if (orbitController != null)
            {
               // orbitController.SetTarget(_combinedBounds.center);
                orbitController.FocusOnBounds(_combinedBounds);
                orbitController.ResetView();
            }
            else if (superimposeCamera != null)
            {
                float dist = _combinedBounds.size.magnitude * 1.5f;
                superimposeCamera.transform.position = _combinedBounds.center + new Vector3(dist * 0.5f, dist * 0.3f, dist * 0.5f);
                superimposeCamera.transform.LookAt(_combinedBounds.center);
            }
        }

        #endregion

        // VisualComparisonMode.cs  (inside the class)

        private void EnterDual3DMode()
        {
            Debug.Log("[VisualComparison] === ENTERING DUAL3D MODE ===");
            _vehicleAModel = vehicleAPreview?.CurrentVehicle;
            _vehicleBModel = vehicleBPreview?.CurrentVehicle;

            if (_vehicleAModel == null && _vehicleBModel == null)
            {
                Debug.LogWarning("[VisualComparison] Dual3D: no models loaded.");
                return;
            }
            if (dual3DModelContainer == null || dual3DCamera == null)
            {
                Debug.LogError("[VisualComparison] Dual3D: assign dual3DModelContainer and dual3DCamera in Inspector.");
                return;
            }

            StoreOriginalState(); // already in your script
                                  // Move under Dual3D container and set Dual3D layer
            if (_vehicleAModel)
            {
                _vehicleAModel.transform.SetParent(dual3DModelContainer, worldPositionStays: true);
                _vehicleAModel.transform.localRotation = Quaternion.identity;
                SetLayerRecursively(_vehicleAModel, dual3DLayer);
            }
            if (_vehicleBModel)
            {
                _vehicleBModel.transform.SetParent(dual3DModelContainer, worldPositionStays: true);
                _vehicleBModel.transform.localRotation = Quaternion.identity;
                SetLayerRecursively(_vehicleBModel, dual3DLayer);
            }

            // Place side-by-side in PARENT-LOCAL space (prevents sinking)
            PositionVehiclesSideBySide_Local();

            // Camera setup + culling
            dual3DCamera.enabled = true;
            dual3DCamera.targetTexture = null;
          

            // Disable preview and superimpose cameras while in Dual3D
            if (vehicleAPreview?.previewCamera) vehicleAPreview.previewCamera.enabled = false;
            if (vehicleBPreview?.previewCamera) vehicleBPreview.previewCamera.enabled = false;
            if (superimposeCamera) superimposeCamera.enabled = false;

            CalculateCombinedBounds();
            FocusDual3DCamera();
            _dummyPanel.SetActive(false);
            dual3DGapSlider.gameObject.SetActive(true);
            // Hide other UI roots (you can show a dedicated Dual3D panel here if you make one)
            if (sideBySideContainer) sideBySideContainer.SetActive(false);
            if (superimposeControlsContainer) superimposeControlsContainer.SetActive(false);

            UpdateUIForAvailableReferences();
            Debug.Log("[VisualComparison] Dual3D mode ACTIVE");
        }

        private void ExitDual3DMode()
        {
            Debug.Log("[VisualComparison] === EXITING DUAL3D MODE ===");

            // Restore original materials (if you ever tinted; safe to call either way)
            RestoreOriginalMaterials();

            // Re-parent + restore layers for preview mode
            RestoreModelsToOriginal(); // your method sets vehicleALayer/vehicleBLayer back

            // Disable dual3D camera, re-enable preview cameras
            if (dual3DCamera) dual3DCamera.enabled = false;
            if (vehicleAPreview?.previewCamera) vehicleAPreview.previewCamera.enabled = true;
            if (vehicleBPreview?.previewCamera) vehicleBPreview.previewCamera.enabled = true;

            // Force RawImages to redraw when we go back
            vehicleAPreview?.RequestRender();
            vehicleBPreview?.RequestRender();
            _dummyPanel.SetActive(true);
            dual3DGapSlider.gameObject.SetActive(false);
            Debug.Log("[VisualComparison] Dual3D mode DISABLED");
        }

        // Compute a 'minY' in Dual3D parent local space
        private float GetLocalBottomY(GameObject go, Transform parent)
        {
            if (!go) return 0f;
            float minY = float.PositiveInfinity;
            var rends = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in rends)
            {
                var b = r.bounds; // world-space AABB
                                  // sample the 8 corners; convert to parent-local; keep the minimum Y
                Vector3 c0 = parent.InverseTransformPoint(new Vector3(b.min.x, b.min.y, b.min.z));
                Vector3 c1 = parent.InverseTransformPoint(new Vector3(b.min.x, b.min.y, b.max.z));
                Vector3 c2 = parent.InverseTransformPoint(new Vector3(b.min.x, b.max.y, b.min.z));
                Vector3 c3 = parent.InverseTransformPoint(new Vector3(b.min.x, b.max.y, b.max.z));
                Vector3 c4 = parent.InverseTransformPoint(new Vector3(b.max.x, b.min.y, b.min.z));
                Vector3 c5 = parent.InverseTransformPoint(new Vector3(b.max.x, b.min.y, b.max.z));
                Vector3 c6 = parent.InverseTransformPoint(new Vector3(b.max.x, b.max.y, b.min.z));
                Vector3 c7 = parent.InverseTransformPoint(new Vector3(b.max.x, b.max.y, b.max.z));
                minY = Mathf.Min(minY, c0.y, c1.y, c2.y, c3.y, c4.y, c5.y, c6.y, c7.y);
            }
            return float.IsInfinity(minY) ? 0f : minY;
        }

        // Compute local extents.x (half-width along X) in parent local space
        private float GetLocalHalfWidthX(GameObject go, Transform parent)
        {
            if (!go) return 0f;
            // approximate with renderer.bounds extents.x (world) projected into parent:
            // safer approach: measure two corners in local and take half of size.x
            Bounds worldB = GetBounds(go); // your existing helper (world)
            Vector3 localMin = parent.InverseTransformPoint(worldB.min);
            Vector3 localMax = parent.InverseTransformPoint(worldB.max);
            float localWidth = Mathf.Abs(localMax.x - localMin.x);
            return localWidth * 0.5f;
        }

        private void PositionVehiclesSideBySide_Local()
        {
            var parent = dual3DModelContainer;
            if (parent == null) return;

            // Determine ground Y in parent-local space
            float groundYLocal = dual3DGround
                ? parent.InverseTransformPoint(new Vector3(0f, dual3DGround.position.y, 0f)).y
                : dual3DGroundY;

            // 1) Vertical alignment (bottoms to ground)
            if (_vehicleAModel)
            {
                float aBottom = GetLocalBottomY(_vehicleAModel, parent);
                var lp = _vehicleAModel.transform.localPosition;
                lp.y += (groundYLocal - aBottom);
                _vehicleAModel.transform.localPosition = lp;
            }
            if (_vehicleBModel)
            {
                float bBottom = GetLocalBottomY(_vehicleBModel, parent);
                var lp = _vehicleBModel.transform.localPosition;
                lp.y += (groundYLocal - bBottom);
                _vehicleBModel.transform.localPosition = lp;
            }

            // 2) Horizontal spacing along local X, centered around origin
            float halfA = GetLocalHalfWidthX(_vehicleAModel, parent);
            float halfB = GetLocalHalfWidthX(_vehicleBModel, parent);
            float gap = Mathf.Max(dual3DGap, 0f);

            float leftX = -(halfA + gap * 0.5f);
            float rightX = +(halfB + gap * 0.5f);

            if (_vehicleAModel)
            {
                var lp = _vehicleAModel.transform.localPosition;
                _vehicleAModel.transform.localPosition = new Vector3(leftX, lp.y, 0f);
            }
            if (_vehicleBModel)
            {
                var lp = _vehicleBModel.transform.localPosition;
                _vehicleBModel.transform.localPosition = new Vector3(rightX, lp.y, 0f);
            }
        }

        private void FocusDual3DCamera()
        {
            if (orbitController != null)
            {
                orbitController.FocusOnBounds(_combinedBounds);
                // keep your preferred starting view
            }
            else if (dual3DCamera != null)
            {
                float dist = _combinedBounds.size.magnitude * 1.5f;
                dual3DCamera.transform.position = _combinedBounds.center + new Vector3(dist * 0.5f, dist * 0.3f, dist * 0.5f);
                dual3DCamera.transform.LookAt(_combinedBounds.center);
            }
        }



        #region Alignment

        public void AlignCenters()
        {
            if (_vehicleAModel == null || _vehicleBModel == null) return;

            Bounds boundsA = GetBounds(_vehicleAModel);
            Bounds boundsB = GetBounds(_vehicleBModel);

            Vector3 offset = boundsA.center - boundsB.center;
            _vehicleBModel.transform.position += offset;

            CalculateCombinedBounds();
            UpdateSliderRange();
            ApplyGlobalClipping();
        }

        public void AlignWheelbases()
        {
            if (_vehicleAModel == null || _vehicleBModel == null) return;

            var dataA = _vehicleAModel.GetComponent<VehiclePrefabData>();
            var dataB = _vehicleBModel.GetComponent<VehiclePrefabData>();

            if (dataA?.wheelFL != null && dataA?.wheelFR != null &&
                dataB?.wheelFL != null && dataB?.wheelFR != null)
            {
                Vector3 axleA = (dataA.wheelFL.position + dataA.wheelFR.position) / 2f;
                Vector3 axleB = (dataB.wheelFL.position + dataB.wheelFR.position) / 2f;
                _vehicleBModel.transform.position += axleA - axleB;
            }
            else
            {
                AlignCenters();
            }

            CalculateCombinedBounds();
            UpdateSliderRange();
            ApplyGlobalClipping();
        }
        private void CheckPrfabData()
        {
            var vehicleA = _vehicleAModel.GetComponent<VehiclePrefabData>();
            if (vehicleA == null) { Debug.Log("Prefab Data is null"); }
        }

        public void ResetAlignment()
        {
            if (_vehicleBModel != null)
                _vehicleBModel.transform.localPosition = Vector3.zero;
            UpdateAnchorButtonVisibility(AlignmentAnchor.VCS);
            CalculateCombinedBounds();
            UpdateSliderRange();
            ApplyGlobalClipping();
        }

        private Bounds GetBounds(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.one);

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        #endregion

        #region Opacity & Visibility

        private void OnVehicleAOpacityChanged(float value)
        {
            SetOpacity(_tintedMaterialsA, value);
            if (vehicleAOpacityText != null)
                vehicleAOpacityText.text = $"{value * 100f:F0}%";
        }

        private void OnVehicleBOpacityChanged(float value)
        {
            SetOpacity(_tintedMaterialsB, value);
            if (vehicleBOpacityText != null)
                vehicleBOpacityText.text = $"{value * 100f:F0}%";
        }


        // === REPLACE your existing SetOpacity(...) with THIS ===
        private void SetOpacity(List<Material> materials, float opacity)
        {
            foreach (var mat in materials)
            {
                if (mat == null) continue;

                // 1) Preferred: the ClipSection shader's own opacity control
                if (mat.HasProperty("_Opacity"))
                {
                    mat.SetFloat("_Opacity", opacity);
                }

                // 2) Also keep color alpha in sync (helps if your ClipSection reads the color alpha too)
                Color c =
                    mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") :
                    mat.HasProperty("_Color") ? mat.GetColor("_Color") :
                    Color.white;

                c.a = opacity;

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

                // 3) Configure blending so partial opacity actually renders as transparent
                //    (URP-friendly settings; harmless for built-in if properties exist)
                bool makeTransparent = opacity < 0.999f;

                // URP Lit-like controls (many custom shaders expose the same props)
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", makeTransparent ? 1f : 0f); // 0 Opaque, 1 Transparent
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // 0 Alpha
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", makeTransparent ? 0f : 1f);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 2f); // Back by default; tweak if you need two-sided

                // Fallback classic blend states (some shaders expose as floats)
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                // Common URP keyword for transparency
                if (makeTransparent)
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else
                    mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

                // Queue for transparency so mixed objects sort better
                mat.renderQueue = makeTransparent ? 3000 : -1; // -1 lets shader pick its default queue
            }
        }
        private GameObject SpawnAnchorLabel(Transform anchor, string label, Color color, Transform parentForLayer, int layer)
        {
            if (anchor == null || anchorLabelPrefab == null) return null;

            var go = Instantiate(anchorLabelPrefab, anchor.position, anchor.rotation, anchor);
            go.name = $"AnchorLabel_{label}";
            go.layer = layer;

            // Ensure all children use same layer for correct camera culling
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            // If your superimpose uses a specific layer, also set here:
            if (parentForLayer != null)
                go.transform.SetParent(anchor, worldPositionStays: true);

            // Set TMP text
            var tmp = FindTMP(go);
            if (tmp != null)
            {
                tmp.text = label;
                tmp.color = color;
                // Optional: add outline to keep readable
              /*  tmp.enableWordWrapping = false;
                tmp.enableAutoSizing = true;
                tmp.fontSizeMin = 0.5f;
                tmp.fontSizeMax = 3.0f;
                tmp.outlineColor = Color.black;
                tmp.outlineWidth = 0.2f;*/
             
            }

            // Optional: keep the sphere small
            if (go.transform.localScale.magnitude > 0f)
                go.transform.localScale = Vector3.one * 0.1f;

            go.GetComponent<MeshRenderer>().material.color = color;

            return go;
        }
        private void CreateAnchorLabelsForVehicles()
        {
            ClearAnchorLabels();

            if (!showAnchorLabels || anchorLabelPrefab == null) return;

            // Vehicle A
            if (_vehicleAModel != null)
            {
                var dataA = _vehicleAModel.GetComponent<VehiclePrefabData>();
                if (dataA != null)
                {
                    // BOF
                    var tBOF = dataA.GetAnchorTransform(AlignmentAnchor.BOF);   // uses fallback if null inside
                    if (tBOF != null && dataA.refBOF != null) // only label real locators; remove check to label fallback too
                    {
                        var label = SpawnAnchorLabel(
                            tBOF, "A: BOF", vehicleALabelColor, superimposeModelContainer, superimposeLayer);
                        if (label) _vehicleAAnchorLabels.Add(label);
                    }

                    // SGRP
                    var tSGRP = dataA.GetAnchorTransform(AlignmentAnchor.SGRP);
                    if (tSGRP != null && dataA.refSGRP != null)
                    {
                        var label = SpawnAnchorLabel(
                            tSGRP, "A: SGRP", vehicleALabelColor, superimposeModelContainer, superimposeLayer);
                        if (label) _vehicleAAnchorLabels.Add(label);
                    }
                }
            }

            // Vehicle B
            if (_vehicleBModel != null)
            {
                var dataB = _vehicleBModel.GetComponent<VehiclePrefabData>();
                if (dataB != null)
                {
                    // BOF
                    var tBOF = dataB.GetAnchorTransform(AlignmentAnchor.BOF);
                    if (tBOF != null && dataB.refBOF != null)
                    {
                        var label = SpawnAnchorLabel(
                            tBOF, "B: BOF", vehicleBLabelColor, superimposeModelContainer, superimposeLayer);
                        if (label) _vehicleBAnchorLabels.Add(label);
                    }

                    // SGRP
                    var tSGRP = dataB.GetAnchorTransform(AlignmentAnchor.SGRP);
                    if (tSGRP != null && dataB.refSGRP != null)
                    {
                        var label = SpawnAnchorLabel(
                            tSGRP, "B: SGRP", vehicleBLabelColor, superimposeModelContainer, superimposeLayer);
                        if (label) _vehicleBAnchorLabels.Add(label);
                    }
                }
            }
        }
        private void SetAnchorLabelsVisible(bool visible)
        {
            foreach (var go in _vehicleAAnchorLabels) if (go) go.SetActive(visible);
            foreach (var go in _vehicleBAnchorLabels) if (go) go.SetActive(visible);
        }

        private void ClearAnchorLabels()
        {
            foreach (var go in _vehicleAAnchorLabels) if (go) Destroy(go);
            foreach (var go in _vehicleBAnchorLabels) if (go) Destroy(go);
            _vehicleAAnchorLabels.Clear();
            _vehicleBAnchorLabels.Clear();
        }

        private void SetVehicleVisible(GameObject vehicle, bool visible)
        {
            if (vehicle == null) return;
            foreach (var r in vehicle.GetComponentsInChildren<Renderer>())
                r.enabled = visible;
        }

        #endregion

        #region Utilities
        private TMP_Text FindTMP(GameObject root)
        {
            if (!root) return null;
            var tmp = root.GetComponent<TMP_Text>();
            if (tmp) return tmp;
            return root.GetComponentInChildren<TMP_Text>(true);
        }
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        #endregion
    }
}
