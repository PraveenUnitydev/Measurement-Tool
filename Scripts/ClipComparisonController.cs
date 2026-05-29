using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    /// <summary>
    /// CLIP COMPARISON CONTROLLER
    /// 
    /// For superimposing two vehicle CAD models and clipping through both
    /// to compare surface differences.
    /// 
    /// USE CASE:
    /// - Load Vehicle A (reference) and Vehicle B (test)
    /// - Superimpose at same position
    /// - Clip through both with same plane
    /// - Visually compare surface gaps/differences
    /// 
    /// FEATURES:
    /// - Global clip planes (affect both vehicles simultaneously)
    /// - Different colors for each vehicle
    /// - Transparency control to see through
    /// - Alignment tools
    /// - Clip plane with slider or draggable 3D handle
    /// </summary>
    public class ClipComparisonController : MonoBehaviour
    {
        [Header("═══ VEHICLES ═══")]
        [Tooltip("Reference vehicle (e.g., target design)")]
        public GameObject vehicleA;
        public Color vehicleAColor = new Color(0.2f, 0.6f, 1f, 1f);  // Blue
        [Range(0f, 1f)]
        public float vehicleAOpacity = 1f;

        [Tooltip("Comparison vehicle (e.g., current design)")]
        public GameObject vehicleB;
        public Color vehicleBColor = new Color(1f, 0.4f, 0.2f, 1f);  // Orange
        [Range(0f, 1f)]
        public float vehicleBOpacity = 0.7f;

        [Header("═══ CLIP PLANE ═══")]
        public ClipAxis activeAxis = ClipAxis.X;
        [Range(-10f, 10f)]
        public float clipPosition = 0f;
        public bool clipEnabled = false;
        public bool invertDirection = false;

        [Header("═══ UI - CLIP CONTROLS ═══")]
        public Slider clipSlider;
        public TMP_Text clipValueText;
        public Toggle clipEnableToggle;
        public TMP_Dropdown axisDropdown;
        public Toggle invertToggle;

        [Header("═══ UI - VEHICLE CONTROLS ═══")]
        public Slider vehicleAOpacitySlider;
        public Slider vehicleBOpacitySlider;
        public Toggle vehicleAVisibleToggle;
        public Toggle vehicleBVisibleToggle;

        [Header("═══ UI - PRESETS ═══")]
        public Button presetFrontButton;
        public Button presetCenterButton;
        public Button presetRearButton;
        public Button presetResetButton;

        [Header("═══ 3D CLIP PLANE HANDLE ═══")]
        public bool showClipPlaneHandle = true;
        public GameObject clipPlaneHandlePrefab;
        public Color clipPlaneHandleColor = new Color(1f, 1f, 0f, 0.5f);

        [Header("═══ ALIGNMENT ═══")]
        public Button alignCentersButton;
        public Button alignWheelbasesButton;
        public Vector3 vehicleBOffset = Vector3.zero;

        // Private
        private List<Material> _vehicleAMaterials = new List<Material>();
        private List<Material> _vehicleBMaterials = new List<Material>();
        private Bounds _combinedBounds;
        private GameObject _clipPlaneHandle;
        private bool _isDraggingHandle;

        // Shader property names for global properties
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

            if (vehicleA != null || vehicleB != null)
            {
                Initialize();
            }
        }

        private void Update()
        {
            // Handle dragging the 3D clip plane
            if (_isDraggingHandle)
            {
                UpdateHandleDrag();
            }
        }

        private void OnDestroy()
        {
            // Reset global shader properties
            ResetGlobalClipProperties();

            if (_clipPlaneHandle != null)
                Destroy(_clipPlaneHandle);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                UpdateGlobalClipProperties();
                UpdateVehicleAppearance();
                UpdateClipPlaneHandle();
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize comparison with current vehicles
        /// </summary>
        public void Initialize()
        {
            // Apply shader and collect materials
            if (vehicleA != null)
            {
                ApplyClipShader(vehicleA, _vehicleAMaterials, vehicleAColor, vehicleAOpacity);
            }

            if (vehicleB != null)
            {
                ApplyClipShader(vehicleB, _vehicleBMaterials, vehicleBColor, vehicleBOpacity);
            }

            // Calculate combined bounds
            CalculateCombinedBounds();

            // Setup slider range
            SetupSliderRange();

            // Create 3D handle
            if (showClipPlaneHandle)
            {
                CreateClipPlaneHandle();
            }

            // Initial update
            UpdateGlobalClipProperties();

            Debug.Log($"[ClipComparison] Initialized - A: {_vehicleAMaterials.Count} mats, B: {_vehicleBMaterials.Count} mats");
        }

        /// <summary>
        /// Set vehicles for comparison
        /// </summary>
        public void SetVehicles(GameObject refVehicle, GameObject compareVehicle)
        {
            vehicleA = refVehicle;
            vehicleB = compareVehicle;

            _vehicleAMaterials.Clear();
            _vehicleBMaterials.Clear();

            Initialize();
        }

        private void ApplyClipShader(GameObject vehicle, List<Material> materialList, Color color, float opacity)
        {
            // Auto-detect render pipeline and use correct shader
            Shader clipShader = null;
            bool isURP = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;

            if (isURP)
            {
                clipShader = Shader.Find("VehicleMeasurement/ClipSection_URP");
                if (clipShader == null)
                {
                    Debug.LogWarning("[ClipComparison] URP ClipSection shader not found, trying built-in...");
                    clipShader = Shader.Find("VehicleMeasurement/ClipSection");
                }
            }
            else
            {
                clipShader = Shader.Find("VehicleMeasurement/ClipSection");
            }

            if (clipShader == null)
            {
                Debug.LogError("[ClipComparison] No ClipSection shader found! Make sure shader files are in your project.");
                return;
            }

            Debug.Log($"[ClipComparison] Using shader: {clipShader.name} (URP: {isURP})");

            var renderers = vehicle.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var materials = renderer.materials;
                for (int i = 0; i < materials.Length; i++)
                {
                    // Store original texture
                    Texture originalTex = null;
                    if (materials[i].HasProperty("_MainTex"))
                        originalTex = materials[i].mainTexture;
                    else if (materials[i].HasProperty("_BaseMap"))
                        originalTex = materials[i].GetTexture("_BaseMap");

                    // Apply clip shader
                    materials[i].shader = clipShader;

                    // Set color - handle both Built-in and URP property names
                    if (materials[i].HasProperty("_BaseColor"))
                        materials[i].SetColor("_BaseColor", color);
                    if (materials[i].HasProperty("_Color"))
                        materials[i].SetColor("_Color", color);

                    // Set opacity
                    if (materials[i].HasProperty("_Opacity"))
                        materials[i].SetFloat("_Opacity", opacity);

                    // Restore texture - handle both property names
                    if (originalTex != null)
                    {
                        if (materials[i].HasProperty("_BaseMap"))
                            materials[i].SetTexture("_BaseMap", originalTex);
                        if (materials[i].HasProperty("_MainTex"))
                            materials[i].SetTexture("_MainTex", originalTex);
                    }

                    materialList.Add(materials[i]);
                }
                renderer.materials = materials;
            }
        }

        private void CalculateCombinedBounds()
        {
            bool hasBounds = false;
            _combinedBounds = new Bounds();

            if (vehicleA != null)
            {
                var renderers = vehicleA.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (!hasBounds)
                    {
                        _combinedBounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        _combinedBounds.Encapsulate(r.bounds);
                    }
                }
            }

            if (vehicleB != null)
            {
                var renderers = vehicleB.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                {
                    if (!hasBounds)
                    {
                        _combinedBounds = r.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        _combinedBounds.Encapsulate(r.bounds);
                    }
                }
            }

            if (!hasBounds)
            {
                _combinedBounds = new Bounds(Vector3.zero, Vector3.one * 5f);
            }
        }

        #endregion

        #region UI Setup

        private void SetupUI()
        {
            // Clip controls
            if (clipSlider != null)
                clipSlider.onValueChanged.AddListener(OnClipSliderChanged);

            if (clipEnableToggle != null)
                clipEnableToggle.onValueChanged.AddListener(OnClipEnableChanged);

            if (axisDropdown != null)
            {
                axisDropdown.ClearOptions();
                axisDropdown.AddOptions(new List<string> { "X (Left/Right)", "Y (Up/Down)", "Z (Front/Back)" });
                axisDropdown.onValueChanged.AddListener(OnAxisChanged);
            }

            if (invertToggle != null)
                invertToggle.onValueChanged.AddListener(OnInvertChanged);

            // Vehicle controls
            if (vehicleAOpacitySlider != null)
                vehicleAOpacitySlider.onValueChanged.AddListener(OnVehicleAOpacityChanged);

            if (vehicleBOpacitySlider != null)
                vehicleBOpacitySlider.onValueChanged.AddListener(OnVehicleBOpacityChanged);

            if (vehicleAVisibleToggle != null)
                vehicleAVisibleToggle.onValueChanged.AddListener(OnVehicleAVisibleChanged);

            if (vehicleBVisibleToggle != null)
                vehicleBVisibleToggle.onValueChanged.AddListener(OnVehicleBVisibleChanged);

            // Presets
            if (presetFrontButton != null)
                presetFrontButton.onClick.AddListener(PresetFront);

            if (presetCenterButton != null)
                presetCenterButton.onClick.AddListener(PresetCenter);

            if (presetRearButton != null)
                presetRearButton.onClick.AddListener(PresetRear);

            if (presetResetButton != null)
                presetResetButton.onClick.AddListener(ResetClip);

            // Alignment
            if (alignCentersButton != null)
                alignCentersButton.onClick.AddListener(AlignCenters);

            if (alignWheelbasesButton != null)
                alignWheelbasesButton.onClick.AddListener(AlignWheelbases);
        }

        private void SetupSliderRange()
        {
            if (clipSlider == null) return;

            float min = 0, max = 0;

            switch (activeAxis)
            {
                case ClipAxis.X:
                    min = _combinedBounds.min.x - 0.5f;
                    max = _combinedBounds.max.x + 0.5f;
                    break;
                case ClipAxis.Y:
                    min = _combinedBounds.min.y - 0.5f;
                    max = _combinedBounds.max.y + 0.5f;
                    break;
                case ClipAxis.Z:
                    min = _combinedBounds.min.z - 0.5f;
                    max = _combinedBounds.max.z + 0.5f;
                    break;
            }

            clipSlider.minValue = min;
            clipSlider.maxValue = max;
            clipSlider.value = (min + max) / 2f;
            clipPosition = clipSlider.value;
        }

        #endregion

        #region UI Callbacks

        private void OnClipSliderChanged(float value)
        {
            clipPosition = value;
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
            UpdateValueText();
        }

        private void OnClipEnableChanged(bool enabled)
        {
            clipEnabled = enabled;
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        private void OnAxisChanged(int index)
        {
            activeAxis = (ClipAxis)index;
            SetupSliderRange();
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        private void OnInvertChanged(bool inverted)
        {
            invertDirection = inverted;
            UpdateGlobalClipProperties();
        }

        private void OnVehicleAOpacityChanged(float value)
        {
            vehicleAOpacity = value;
            foreach (var mat in _vehicleAMaterials)
            {
                mat.SetFloat("_Opacity", value);
            }
        }

        private void OnVehicleBOpacityChanged(float value)
        {
            vehicleBOpacity = value;
            foreach (var mat in _vehicleBMaterials)
            {
                mat.SetFloat("_Opacity", value);
            }
        }

        private void OnVehicleAVisibleChanged(bool visible)
        {
            if (vehicleA != null)
                vehicleA.SetActive(visible);
        }

        private void OnVehicleBVisibleChanged(bool visible)
        {
            if (vehicleB != null)
                vehicleB.SetActive(visible);
        }

        private void UpdateValueText()
        {
            if (clipValueText != null)
            {
                string axisName = activeAxis.ToString();
                clipValueText.text = $"{axisName}: {clipPosition:F1} mm";
            }
        }

        #endregion

        #region Global Shader Properties

        private void UpdateGlobalClipProperties()
        {
            float direction = invertDirection ? -1f : 1f;

            // Reset all planes first
            Shader.SetGlobalFloat(PROP_CLIP_X_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Y_ENABLED, 0f);
            Shader.SetGlobalFloat(PROP_CLIP_Z_ENABLED, 0f);

            if (!clipEnabled) return;

            // Set active plane
            switch (activeAxis)
            {
                case ClipAxis.X:
                    Shader.SetGlobalFloat(PROP_CLIP_X_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_X_POSITION, clipPosition);
                    Shader.SetGlobalFloat(PROP_CLIP_X_DIRECTION, direction);
                    break;

                case ClipAxis.Y:
                    Shader.SetGlobalFloat(PROP_CLIP_Y_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_Y_POSITION, clipPosition);
                    Shader.SetGlobalFloat(PROP_CLIP_Y_DIRECTION, direction);
                    break;

                case ClipAxis.Z:
                    Shader.SetGlobalFloat(PROP_CLIP_Z_ENABLED, 1f);
                    Shader.SetGlobalFloat(PROP_CLIP_Z_POSITION, clipPosition);
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

        private void UpdateVehicleAppearance()
        {
            foreach (var mat in _vehicleAMaterials)
            {
                mat.color = vehicleAColor;
                mat.SetFloat("_Opacity", vehicleAOpacity);
            }

            foreach (var mat in _vehicleBMaterials)
            {
                mat.color = vehicleBColor;
                mat.SetFloat("_Opacity", vehicleBOpacity);
            }
        }

        #endregion

        #region 3D Clip Plane Handle

        private void CreateClipPlaneHandle()
        {
            if (_clipPlaneHandle != null)
                Destroy(_clipPlaneHandle);

            // Create a quad as the clip plane visual
            _clipPlaneHandle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _clipPlaneHandle.name = "ClipPlaneHandle";

            // Remove collider and add trigger for interaction
            var collider = _clipPlaneHandle.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Add box collider for raycasting
            var boxCollider = _clipPlaneHandle.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(1, 1, 0.1f);

            // Setup material
            var renderer = _clipPlaneHandle.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            mat.color = clipPlaneHandleColor;
            renderer.material = mat;

            // Scale to bounds
            float size = Mathf.Max(_combinedBounds.size.x, _combinedBounds.size.y, _combinedBounds.size.z) * 1.2f;
            _clipPlaneHandle.transform.localScale = new Vector3(size, size, 1f);

            UpdateClipPlaneHandle();
        }

        private void UpdateClipPlaneHandle()
        {
            if (_clipPlaneHandle == null) return;

            _clipPlaneHandle.SetActive(clipEnabled && showClipPlaneHandle);

            if (!clipEnabled) return;

            Vector3 position = _combinedBounds.center;
            Quaternion rotation = Quaternion.identity;

            switch (activeAxis)
            {
                case ClipAxis.X:
                    position.x = clipPosition;
                    rotation = Quaternion.Euler(0, 90, 0);
                    break;
                case ClipAxis.Y:
                    position.y = clipPosition;
                    rotation = Quaternion.Euler(90, 0, 0);
                    break;
                case ClipAxis.Z:
                    position.z = clipPosition;
                    rotation = Quaternion.identity;
                    break;
            }

            _clipPlaneHandle.transform.position = position;
            _clipPlaneHandle.transform.rotation = rotation;
        }

        private void UpdateHandleDrag()
        {
            if (!Input.GetMouseButton(0))
            {
                _isDraggingHandle = false;
                return;
            }

            // Raycast to get world position
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane dragPlane = new Plane();

            switch (activeAxis)
            {
                case ClipAxis.X:
                    dragPlane = new Plane(Vector3.forward, _combinedBounds.center);
                    break;
                case ClipAxis.Y:
                    dragPlane = new Plane(Vector3.forward, _combinedBounds.center);
                    break;
                case ClipAxis.Z:
                    dragPlane = new Plane(Vector3.up, _combinedBounds.center);
                    break;
            }

            if (dragPlane.Raycast(ray, out float distance))
            {
                Vector3 hitPoint = ray.GetPoint(distance);

                switch (activeAxis)
                {
                    case ClipAxis.X:
                        clipPosition = Mathf.Clamp(hitPoint.x, clipSlider.minValue, clipSlider.maxValue);
                        break;
                    case ClipAxis.Y:
                        clipPosition = Mathf.Clamp(hitPoint.y, clipSlider.minValue, clipSlider.maxValue);
                        break;
                    case ClipAxis.Z:
                        clipPosition = Mathf.Clamp(hitPoint.z, clipSlider.minValue, clipSlider.maxValue);
                        break;
                }

                if (clipSlider != null)
                    clipSlider.value = clipPosition;

                UpdateGlobalClipProperties();
                UpdateClipPlaneHandle();
                UpdateValueText();
            }
        }

        /// <summary>
        /// Call this from a pointer down event on the handle
        /// </summary>
        public void StartHandleDrag()
        {
            _isDraggingHandle = true;
        }

        #endregion

        #region Presets

        public void PresetFront()
        {
            activeAxis = ClipAxis.Z;
            invertDirection = false;
            clipEnabled = true;

            clipPosition = _combinedBounds.max.z - _combinedBounds.size.z * 0.3f;

            if (axisDropdown != null) axisDropdown.value = 2;
            if (clipEnableToggle != null) clipEnableToggle.isOn = true;
            if (clipSlider != null) clipSlider.value = clipPosition;

            SetupSliderRange();
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        public void PresetCenter()
        {
            clipEnabled = true;
            clipPosition = GetAxisCenter(activeAxis);

            if (clipEnableToggle != null) clipEnableToggle.isOn = true;
            if (clipSlider != null) clipSlider.value = clipPosition;

            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        public void PresetRear()
        {
            activeAxis = ClipAxis.Z;
            invertDirection = true;
            clipEnabled = true;

            clipPosition = _combinedBounds.min.z + _combinedBounds.size.z * 0.3f;

            if (axisDropdown != null) axisDropdown.value = 2;
            if (clipEnableToggle != null) clipEnableToggle.isOn = true;
            if (invertToggle != null) invertToggle.isOn = true;
            if (clipSlider != null) clipSlider.value = clipPosition;

            SetupSliderRange();
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        public void ResetClip()
        {
            clipEnabled = false;
            invertDirection = false;

            if (clipEnableToggle != null) clipEnableToggle.isOn = false;
            if (invertToggle != null) invertToggle.isOn = false;

            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        private float GetAxisCenter(ClipAxis axis)
        {
            switch (axis)
            {
                case ClipAxis.X: return _combinedBounds.center.x;
                case ClipAxis.Y: return _combinedBounds.center.y;
                case ClipAxis.Z: return _combinedBounds.center.z;
                default: return 0;
            }
        }

        #endregion

        #region Alignment

        /// <summary>
        /// Align Vehicle B center to Vehicle A center
        /// </summary>
        public void AlignCenters()
        {
            if (vehicleA == null || vehicleB == null) return;

            var boundsA = CalculateBounds(vehicleA);
            var boundsB = CalculateBounds(vehicleB);

            Vector3 offset = boundsA.center - boundsB.center;
            vehicleB.transform.position += offset;
            vehicleBOffset = offset;

            CalculateCombinedBounds();
            SetupSliderRange();
            UpdateClipPlaneHandle();

            Debug.Log($"[ClipComparison] Aligned centers, offset: {offset}");
        }

        /// <summary>
        /// Align based on front axle position (if VehiclePrefabData available)
        /// </summary>
        public void AlignWheelbases()
        {
            if (vehicleA == null || vehicleB == null) return;

            var dataA = vehicleA.GetComponentInChildren<VehiclePrefabData>();
            var dataB = vehicleB.GetComponentInChildren<VehiclePrefabData>();

            if (dataA == null || dataB == null)
            {
                Debug.LogWarning("[ClipComparison] VehiclePrefabData not found, using center alignment");
                AlignCenters();
                return;
            }

            // Align front axle centers
            Vector3 frontAxleA = (dataA.wheelFL.position + dataA.wheelFR.position) / 2f;
            Vector3 frontAxleB = (dataB.wheelFL.position + dataB.wheelFR.position) / 2f;

            Vector3 offset = frontAxleA - frontAxleB;
            vehicleB.transform.position += offset;
            vehicleBOffset = offset;

            CalculateCombinedBounds();
            SetupSliderRange();
            UpdateClipPlaneHandle();

            Debug.Log($"[ClipComparison] Aligned wheelbases, offset: {offset}");
        }

        /// <summary>
        /// Set custom offset for Vehicle B
        /// </summary>
        public void SetVehicleBOffset(Vector3 offset)
        {
            if (vehicleB == null) return;

            // Remove old offset, apply new
            vehicleB.transform.position -= vehicleBOffset;
            vehicleB.transform.position += offset;
            vehicleBOffset = offset;

            CalculateCombinedBounds();
            SetupSliderRange();
            UpdateClipPlaneHandle();
        }

        private Bounds CalculateBounds(GameObject vehicle)
        {
            var renderers = vehicle.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(vehicle.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            return bounds;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set clip position programmatically
        /// </summary>
        public void SetClipPosition(float position)
        {
            clipPosition = position;
            if (clipSlider != null) clipSlider.value = position;
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
            UpdateValueText();
        }

        /// <summary>
        /// Set clip axis programmatically
        /// </summary>
        public void SetClipAxis(ClipAxis axis)
        {
            activeAxis = axis;
            if (axisDropdown != null) axisDropdown.value = (int)axis;
            SetupSliderRange();
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        /// <summary>
        /// Enable/disable clipping
        /// </summary>
        public void SetClipEnabled(bool enabled)
        {
            clipEnabled = enabled;
            if (clipEnableToggle != null) clipEnableToggle.isOn = enabled;
            UpdateGlobalClipProperties();
            UpdateClipPlaneHandle();
        }

        /// <summary>
        /// Get current combined bounds
        /// </summary>
        public Bounds GetCombinedBounds() => _combinedBounds;

        /// <summary>
        /// Swap vehicle colors
        /// </summary>
        public void SwapColors()
        {
            Color temp = vehicleAColor;
            vehicleAColor = vehicleBColor;
            vehicleBColor = temp;
            UpdateVehicleAppearance();
        }

        #endregion
    }
}
