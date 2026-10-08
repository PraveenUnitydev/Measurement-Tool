using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    /// <summary>
    /// Clip plane axis
    /// </summary>
    public enum ClipAxis
    {
        X,  // Left/Right
        Y,  // Up/Down
        Z   // Front/Back
    }

    /// <summary>
    /// CLIP SECTION MODE
    /// 
    /// Integrates clip section functionality into Measurement Scene.
    /// Toggle on/off with a button, shows clip controls when active.
    /// 
    /// SETUP:
    /// 1. Add this component to your MeasurementScene
    /// 2. Create UI panel with sliders for X, Y, Z
    /// 3. Connect toggle button and UI references
    /// 4. Call Initialize() when vehicle is loaded
    /// 
    /// UI STRUCTURE:
    /// ┌─────────────────────────────────────────┐
    /// │  [Toggle Clip Section]                  │
    /// ├─────────────────────────────────────────┤
    /// │  Clip Section Panel (shown when ON)     │
    /// │  ┌─────────────────────────────────┐    │
    /// │  │ Axis: [X] [Y] [Z]               │    │
    /// │  │ Position: ═══════●═══════ 1500  │    │
    /// │  │ [Invert] [Reset]                │    │
    /// │  └─────────────────────────────────┘    │
    /// └─────────────────────────────────────────┘
    /// </summary>
    public class ClipSectionMode : MonoBehaviour
    {
        [Header("═══ MODE TOGGLE ═══")]
        [Tooltip("Button to toggle clip section mode on/off")]
        public Button toggleButton;
        public TMP_Text toggleButtonText;
        [Tooltip("Panel containing all clip section controls")]
        public GameObject clipSectionPanel;

        [SerializeField] private GameObject _clipSectionUIPanel;
       // [SerializeField] private Button _showArrow;
        [SerializeField] private Button _hideArrow;
        // [Header("═══ AXIS SELECTION ═══")]
        //  public Toggle axisXToggle;
        // public Toggle axisYToggle;
        // public Toggle axisZToggle;
        // public ToggleGroup axisToggleGroup;

        [Header("═══ AXIS SELECTION (Buttons) ═══")]
        [SerializeField] private Button axisXButton;
        [SerializeField] private Button axisYButton;
        [SerializeField] private Button axisZButton;

        private Button _currentAxisButton;

        [Header("Button Sprites")]
        public Sprite axisNormalSprite;
        public Sprite axisSelectedSprite;


        [Header("═══ POSITION SLIDER ═══")]
        public Slider positionSlider;
        public TMP_Text positionValueText;
        public TMP_Text positionMinText;
        public TMP_Text positionMaxText;

        [Header("═══ OPTIONS ═══")]
        public Button invertToggle;
        public Toggle showPlaneToggle;
        public Button resetButton;

        [Header("═══ VISUAL PLANE ═══")]
        public bool showClipPlane = true;
        public Color clipPlaneColor = new Color(1f, 0.8f, 0f, 0.3f);

        [Header("═══ COLORS ═══")]
        public Color activeButtonColor = new Color(0.2f, 0.6f, 1f);
        public Color inactiveButtonColor = new Color(0.3f, 0.3f, 0.3f);

        // State
        private bool _isActive = false;
        private ClipAxis _currentAxis = ClipAxis.X;
        private float _clipPosition = 0f;
        private bool _invertDirection = false;

        // Vehicle
        private GameObject _targetVehicle;
        private Bounds _vehicleBounds;
        // The vehicle's own materials (the real assets - not copies), put back when clip mode ends.
        private Dictionary<Renderer, Material[]> _originalMaterialsPerRenderer = new Dictionary<Renderer, Material[]>();
        // Clip stand-ins, one per original material, reused each time clip mode is switched on (see ClipMaterials).
        private Dictionary<Material, Material> _standIns = new Dictionary<Material, Material>();
        private bool _materialsSwapped;

        // Visual plane
        private GameObject _clipPlaneVisual;

        // Shader names
        private const string PROP_CLIP_X_ENABLED = "_GlobalClipXEnabled";
        private const string PROP_CLIP_X_POSITION = "_GlobalClipXPosition";
        private const string PROP_CLIP_X_DIRECTION = "_GlobalClipXDirection";
        private const string PROP_CLIP_Y_ENABLED = "_GlobalClipYEnabled";
        private const string PROP_CLIP_Y_POSITION = "_GlobalClipYPosition";
        private const string PROP_CLIP_Y_DIRECTION = "_GlobalClipYDirection";
        private const string PROP_CLIP_Z_ENABLED = "_GlobalClipZEnabled";
        private const string PROP_CLIP_Z_POSITION = "_GlobalClipZPosition";
        private const string PROP_CLIP_Z_DIRECTION = "_GlobalClipZDirection";


        #region Unity Lifecycle

        private void Start()
        {
            SetupUI();

            // Start with clip section disabled
            SetClipSectionActive(false);
        }

        private void OnDestroy()
        {
            // Restore original materials when destroyed
            RestoreOriginalMaterials();
            ClipMaterials.DestroyAll(_standIns);
            ResetGlobalClipProperties();

            if (_clipPlaneVisual != null)
                Destroy(_clipPlaneVisual);
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize clip section for a loaded vehicle
        /// Call this after vehicle model is loaded
        /// </summary>
        public void Initialize(GameObject vehicle)
        {
            if (vehicle == null)
            {
                Debug.LogWarning("[ClipSection] No vehicle provided");
                return;
            }

            // A different vehicle: put the old one's materials back and drop its stand-ins
            if (_targetVehicle != vehicle)
            {
                RestoreOriginalMaterials();
                ClipMaterials.DestroyAll(_standIns);
            }

            _targetVehicle = vehicle;

            // Calculate bounds
            CalculateVehicleBounds();

            // Store original materials for restoration
            StoreOriginalMaterials();

            // Setup slider range
            UpdateSliderRange();

            // Create visual plane
            if (showClipPlane)
                CreateClipPlaneVisual();

            // Re-initialised while clip mode is on (vehicle reloaded): clip the new parts too
            if (_isActive)
            {
                ApplyClipShader();
                UpdateClipPlane();
            }

            Debug.Log($"[ClipSection] Initialized for {vehicle.name}, bounds: {_vehicleBounds.size}");
        }

        /// <summary>
        /// Call when vehicle is unloaded
        /// </summary>
        public void OnVehicleUnloaded()
        {
            RestoreOriginalMaterials();
            ResetGlobalClipProperties();

            _targetVehicle = null;
            _originalMaterialsPerRenderer.Clear();
            ClipMaterials.DestroyAll(_standIns);

            if (_clipPlaneVisual != null)
                _clipPlaneVisual.SetActive(false);
        }

        private void CalculateVehicleBounds()
        {
            if (_targetVehicle == null) return;

            var renderers = _targetVehicle.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                _vehicleBounds = new Bounds(_targetVehicle.transform.position, Vector3.one * 5f);
                return;
            }

            _vehicleBounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                _vehicleBounds.Encapsulate(r.bounds);
            }
        }

        private void StoreOriginalMaterials()
        {
            // Keep references to the real materials. (Before: every material was copied with new Material(...) and the
            // copies were assigned back on restore, so each switch leaked materials, broke batching and the vehicle never
            // got its own assets back.)
            if (_materialsSwapped) RestoreOriginalMaterials();
            _originalMaterialsPerRenderer.Clear();

            if (_targetVehicle == null) return;

            foreach (var renderer in _targetVehicle.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is ParticleSystemRenderer) continue;
                _originalMaterialsPerRenderer[renderer] = renderer.sharedMaterials;
            }

            Debug.Log($"[ClipSection] Stored materials from {_originalMaterialsPerRenderer.Count} renderers");
        }

        private void RestoreOriginalMaterials()
        {
            if (!_materialsSwapped) return;
            ClipMaterials.Restore(_originalMaterialsPerRenderer);
            _materialsSwapped = false;
            Debug.Log("[ClipSection] Restored original materials");
        }


        #endregion

        #region UI Setup

        private Animator _invertButtonAnim;
        
        private void SetupUI()
        {
            // Toggle button
            if (toggleButton != null)
                toggleButton.onClick.AddListener(OnToggleButtonClick);

            if (axisXButton != null) axisXButton.onClick.AddListener(() => OnAxisButtonClicked(axisXButton, ClipAxis.X));
            if (axisYButton != null) axisYButton.onClick.AddListener(() => OnAxisButtonClicked(axisYButton, ClipAxis.Y));
            if (axisZButton != null) axisZButton.onClick.AddListener(() => OnAxisButtonClicked(axisZButton, ClipAxis.Z));

            if (axisXButton != null) SelectAxisButton(axisXButton, ClipAxis.X);

            // Position slider
            if (positionSlider != null)
                positionSlider.onValueChanged.AddListener(OnPositionChanged);

            // Options
            if (invertToggle != null)
            {
                invertToggle.onClick.AddListener(OnInvertToggleClicked);
                _invertButtonAnim = invertToggle.GetComponent<Animator>();
            }
            if (showPlaneToggle != null)
                showPlaneToggle.onValueChanged.AddListener(OnShowPlaneChanged);
            if (resetButton != null)
                resetButton.onClick.AddListener(OnResetClick);

            // Initial UI state
            if (clipSectionPanel != null)
                clipSectionPanel.SetActive(false);
            //_showArrow.onClick.AddListener(()=>ShowHideClipPanel(true));
            if (_hideArrow != null) _hideArrow.onClick.AddListener(() => ShowHideClipPanel(false));

            UpdateToggleButtonAppearance();
        }

        private void OnAxisButtonClicked(Button clickedBtn, ClipAxis axis)
        {
            if (_currentAxisButton == clickedBtn)
                return; // already selected

            SelectAxisButton(clickedBtn, axis);
        }

        private void SelectAxisButton(Button btn, ClipAxis axis)
        {
            // Un-select old one
            if (_currentAxisButton != null)
            {
                var oldImg = _currentAxisButton.GetComponent<Image>();
                if (oldImg != null)
                    oldImg.color = inactiveButtonColor;

                _currentAxisButton.interactable = true;
            }

            // Select new one
            if (btn == null) { OnAxisChanged(axis); return; }
            var newImg = btn.GetComponent<Image>();
            if (newImg != null)
                newImg.color = activeButtonColor;

            btn.interactable = false;
            _currentAxisButton = btn;

            // Notify your existing code
            OnAxisChanged(axis);
        }

        bool _invertButtonCheck = false;
        private void OnInvertToggleClicked()
        {
            _invertButtonCheck = !_invertButtonCheck;
            if (_invertButtonAnim != null) _invertButtonAnim.SetBool("Invert", _invertButtonCheck);
            OnInvertChanged(_invertButtonCheck);
        }
        private void UpdateSliderRange()
        {
            if (positionSlider == null) return;

            float min = 0, max = 0, center = 0;
            string axisName = "";

            switch (_currentAxis)
            {
                case ClipAxis.X:
                    min = _vehicleBounds.min.x;
                    max = _vehicleBounds.max.x;
                    center = _vehicleBounds.center.x;
                    axisName = "X";
                    break;
                case ClipAxis.Y:
                    min = _vehicleBounds.min.y;
                    max = _vehicleBounds.max.y;
                    center = _vehicleBounds.center.y;
                    axisName = "Y";
                    break;
                case ClipAxis.Z:
                    min = _vehicleBounds.min.z;
                    max = _vehicleBounds.max.z;
                    center = _vehicleBounds.center.z;
                    axisName = "Z";
                    break;
            }

            // Add padding
            float padding = (max - min) * 0.1f;
            positionSlider.minValue = min - padding;
            positionSlider.maxValue = max + padding;
            positionSlider.value = center;
            _clipPosition = center;

            // Update labels
            if (positionMinText != null)
                positionMinText.text = $"{min:F0}";
            if (positionMaxText != null)
                positionMaxText.text = $"{max:F0}";

            UpdatePositionText();
        }

        private void UpdatePositionText()
        {
            if (positionValueText != null)
            {
                string axisName = _currentAxis.ToString();
                positionValueText.text = $"{axisName}: {_clipPosition:F1}";
            }
        }

        private void UpdateToggleButtonAppearance()
        {
            if (toggleButtonText != null)
                toggleButtonText.text = _isActive ? "Clip Section On" : "Clip Section Off";


            if (toggleButton != null)
            {
                var img = toggleButton.GetComponent<Image>();
                if (img != null)
                    img.color = _isActive ? activeButtonColor : inactiveButtonColor;
            }

        }

        #endregion

        #region UI Callbacks

        private void OnToggleButtonClick()
        {
            SetClipSectionActive(!_isActive);
        }
     

        public void ShowHideClipPanel(bool _activate)
        {
            if (_clipSectionUIPanel != null)
            {
                _clipSectionUIPanel.SetActive(_activate);
            }
          

        }

        private void OnAxisChanged(ClipAxis axis)
        {
            _currentAxis = axis;
            UpdateSliderRange();
            UpdateClipPlane();
            UpdateClipPlaneVisual();
        }

        private void OnPositionChanged(float value)
        {
            _clipPosition = value;
            UpdatePositionText();
            UpdateClipPlane();
            UpdateClipPlaneVisual();
        }

        private void OnInvertChanged(bool inverted)
        {
            _invertDirection = inverted;
            UpdateClipPlane();
        }

        private void OnShowPlaneChanged(bool show)
        {
            showClipPlane = show;
            if (_clipPlaneVisual != null)
                _clipPlaneVisual.SetActive(show && _isActive);
        }

        private void OnResetClick()
        {
            // Reset to center
            _clipPosition = GetAxisCenter(_currentAxis);
            _invertDirection = false;
            _invertButtonCheck = false;
            if (_invertButtonAnim != null) _invertButtonAnim.SetBool("Invert", false);

            if (positionSlider != null)
                positionSlider.value = _clipPosition;
           /* if (invertToggle != null)
                invertToggle.isOn = false;*/

            UpdateClipPlane();
            UpdateClipPlaneVisual();
        }

        private float GetAxisCenter(ClipAxis axis)
        {
            switch (axis)
            {
                case ClipAxis.X: return _vehicleBounds.center.x;
                case ClipAxis.Y: return _vehicleBounds.center.y;
                case ClipAxis.Z: return _vehicleBounds.center.z;
                default: return 0;
            }
        }

        #endregion

        #region Clip Section Control

        /// <summary>
        /// Enable or disable clip section mode
        /// </summary>
        public void SetClipSectionActive(bool active)
        {
            _isActive = active;

            // Show/hide UI panel
            if (clipSectionPanel != null)
                clipSectionPanel.SetActive(active);

            // Update button appearance
            UpdateToggleButtonAppearance();

            if (active)
            {
                // Apply clip shader to vehicle
                ApplyClipShader();
                UpdateClipPlane();

                if (_clipPlaneVisual != null)
                    _clipPlaneVisual.SetActive(showClipPlane);
                ShowHideClipPanel(true);
            }
            else
            {
                // Restore original materials
                RestoreOriginalMaterials();
                ResetGlobalClipProperties();

                if (_clipPlaneVisual != null)
                    _clipPlaneVisual.SetActive(false);
            }

            Debug.Log($"[ClipSection] Mode: {(active ? "ON" : "OFF")}");
        }

        private void ApplyClipShader()
        {
            if (_targetVehicle == null) return;

            // Parts whose Shader Graph has the DAS clip node keep their own material (exact look). The rest get a
            // stand-in that copies their colours, finish, clear coat and transparency (ClipMaterials).
            int native;
            int swapped = ClipMaterials.Apply(_targetVehicle, _originalMaterialsPerRenderer, _standIns, out native);
            _materialsSwapped = true;

            Debug.Log($"[ClipSection] Clipping: {native} material slot(s) clip with their own shader, {swapped} use the clip stand-in " +
                      $"({_standIns.Count} stand-in material(s)).");
        }

        private void UpdateClipPlane()
        {
            if (!_isActive) return;

            float direction = _invertDirection ? -1f : 1f;

            // Reset all planes
            Shader.SetGlobalFloat(PROP_CLIP_X_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Y_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Z_ENABLED, 0f);

            // Set active plane
            switch (_currentAxis)
            {
                case ClipAxis.X:
                    Shader.SetGlobalFloat(PROP_CLIP_X_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_X_POSITION, _clipPosition);
                    Shader.SetGlobalFloat(PROP_CLIP_X_DIRECTION, direction);
                    break;
                case ClipAxis.Y:
                    Shader.SetGlobalFloat(PROP_CLIP_Y_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_Y_POSITION, _clipPosition);
                    Shader.SetGlobalFloat(PROP_CLIP_Y_DIRECTION, direction);
                    break;
                case ClipAxis.Z:
                    Shader.SetGlobalFloat(PROP_CLIP_Z_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_Z_POSITION, _clipPosition);
                    Shader.SetGlobalFloat(PROP_CLIP_Z_DIRECTION, direction);
                    break;
            }
        }

        private void ResetGlobalClipProperties()
        {
            Shader.SetGlobalFloat(PROP_CLIP_X_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Y_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Z_ENABLED, 0f);
        }

        #endregion

        #region Visual Clip Plane

        private void CreateClipPlaneVisual()
        {
            if (_clipPlaneVisual != null)
                Destroy(_clipPlaneVisual);

            _clipPlaneVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _clipPlaneVisual.name = "ClipPlaneVisual";

            // Remove collider
            var collider = _clipPlaneVisual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Setup transparent material - URP compatible
            var renderer = _clipPlaneVisual.GetComponent<Renderer>();

            // Try URP shader first, fallback to Standard
            Shader transparentShader = Shader.Find("Universal Render Pipeline/Lit");
            if (transparentShader == null)
                transparentShader = Shader.Find("Standard");

            var mat = new Material(transparentShader);

            // Check if URP
            bool isURP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

            if (isURP)
            {
                // URP Lit shader transparency settings
                mat.SetFloat("_Surface", 1); // 0 = Opaque, 1 = Transparent
                mat.SetFloat("_Blend", 0);   // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
                mat.SetFloat("_AlphaClip", 0);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0);
                mat.SetFloat("_Cull", 0); // 0 = Off (two-sided)
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = 3000;
                mat.SetColor("_BaseColor", clipPlaneColor);
            }
            else
            {
                // Standard shader transparency settings
                mat.SetFloat("_Mode", 3); // Transparent
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.renderQueue = 3000;
                mat.color = clipPlaneColor;
            }

            renderer.material = mat;

            // Scale to vehicle size
            float size = Mathf.Max(_vehicleBounds.size.x, _vehicleBounds.size.y, _vehicleBounds.size.z) * 1.5f;
            _clipPlaneVisual.transform.localScale = new Vector3(size, size, 1f);

            _clipPlaneVisual.SetActive(false);

            UpdateClipPlaneVisual();
        }

        private void UpdateClipPlaneVisual()
        {
            if (_clipPlaneVisual == null) return;

            Vector3 position = _vehicleBounds.center;
            Quaternion rotation = Quaternion.identity;

            switch (_currentAxis)
            {
                case ClipAxis.X:
                    position.x = _clipPosition;
                    rotation = Quaternion.Euler(0, 90, 0);
                    break;
                case ClipAxis.Y:
                    position.y = _clipPosition;
                    rotation = Quaternion.Euler(90, 0, 0);
                    break;
                case ClipAxis.Z:
                    position.z = _clipPosition;
                    rotation = Quaternion.identity;
                    break;
            }

            _clipPlaneVisual.transform.position = position;
            _clipPlaneVisual.transform.rotation = rotation;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Check if clip section mode is active
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// Get current clip position
        /// </summary>
        public float ClipPosition => _clipPosition;

        /// <summary>
        /// Get current clip axis
        /// </summary>
        public ClipAxis CurrentAxis => _currentAxis;

        /// <summary>
        /// Set clip position programmatically
        /// </summary>
        public void SetClipPosition(float position)
        {
            _clipPosition = position;
            if (positionSlider != null)
                positionSlider.value = position;
            UpdatePositionText();
            UpdateClipPlane();
            UpdateClipPlaneVisual();
        }

        /// <summary>
        /// Set clip axis programmatically
        /// </summary>
        public void SetClipAxis(ClipAxis axis)
        {
            _currentAxis = axis;


            switch (axis)
            {
                case ClipAxis.X:
                    if (axisXButton != null) SelectAxisButton(axisXButton, ClipAxis.X);
                    break;
                case ClipAxis.Y:
                    if (axisYButton != null) SelectAxisButton(axisYButton, ClipAxis.Y);
                    break;
                case ClipAxis.Z:
                    if (axisZButton != null) SelectAxisButton(axisZButton, ClipAxis.Z);
                    break;
            }


            UpdateSliderRange();
            UpdateClipPlane();
            UpdateClipPlaneVisual();
        }

        #endregion
    }
}
