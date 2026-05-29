using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    /// <summary>
    /// CLIP SECTION CONTROLLER
    /// 
    /// Controls clipping planes for CAD section views.
    /// Supports X, Y, Z axis-aligned planes with UI sliders.
    /// 
    /// SETUP:
    /// 1. Add this to your scene
    /// 2. Assign vehicle model to 'targetModel'
    /// 3. Connect UI sliders for X, Y, Z control
    /// 4. Vehicle materials should use "VehicleMeasurement/ClipSection" shader
    /// 
    /// FEATURES:
    /// - 3 independent clip planes (X, Y, Z)
    /// - UI slider control
    /// - Visual plane indicators (optional)
    /// - Invert clip direction
    /// - Auto-detect model bounds
    /// </summary>
    public class ClipSectionController : MonoBehaviour
    {
        [Header("═══ TARGET ═══")]
        [Tooltip("The vehicle model to clip")]
        public GameObject targetModel;
        
        [Tooltip("Auto-apply ClipSection shader to all materials")]
        public bool autoApplyShader = true;
        
        [Header("═══ CLIP PLANE X (Left/Right) ═══")]
        public bool enableClipX = false;
        [Range(-10f, 10f)]
        public float clipXPosition = 0f;
        public bool invertClipX = false;
        public Slider clipXSlider;
        public Toggle clipXToggle;
        public TMP_Text clipXValueText;
        
        [Header("═══ CLIP PLANE Y (Up/Down) ═══")]
        public bool enableClipY = false;
        [Range(-10f, 10f)]
        public float clipYPosition = 0f;
        public bool invertClipY = false;
        public Slider clipYSlider;
        public Toggle clipYToggle;
        public TMP_Text clipYValueText;
        
        [Header("═══ CLIP PLANE Z (Front/Back) ═══")]
        public bool enableClipZ = false;
        [Range(-10f, 10f)]
        public float clipZPosition = 0f;
        public bool invertClipZ = false;
        public Slider clipZSlider;
        public Toggle clipZToggle;
        public TMP_Text clipZValueText;
        
        [Header("═══ CROSS SECTION ═══")]
        public bool showCrossSection = true;
        public Color crossSectionColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        [Range(0f, 0.05f)]
        public float crossSectionWidth = 0.002f;
        
        [Header("═══ VISUAL PLANES ═══")]
        [Tooltip("Show semi-transparent plane indicators")]
        public bool showPlaneIndicators = true;
        public Color planeIndicatorColor = new Color(1f, 0.5f, 0f, 0.3f);
        public Material planeIndicatorMaterial;
        
        [Header("═══ PRESETS ═══")]
        public Button presetFrontHalfButton;
        public Button presetSideHalfButton;
        public Button presetTopHalfButton;
        public Button presetResetButton;
        
        // Private
        private List<Material> _clippedMaterials = new List<Material>();
        private Bounds _modelBounds;
        private bool _boundsCalculated = false;
        
        // Plane indicator objects
        private GameObject _planeIndicatorX;
        private GameObject _planeIndicatorY;
        private GameObject _planeIndicatorZ;
        
        // Shader property IDs (cached for performance)
        private static readonly int PropClipXEnabled = Shader.PropertyToID("_ClipPlaneXEnabled");
        private static readonly int PropClipXPosition = Shader.PropertyToID("_ClipPlaneXPosition");
        private static readonly int PropClipXDirection = Shader.PropertyToID("_ClipPlaneXDirection");
        private static readonly int PropClipYEnabled = Shader.PropertyToID("_ClipPlaneYEnabled");
        private static readonly int PropClipYPosition = Shader.PropertyToID("_ClipPlaneYPosition");
        private static readonly int PropClipYDirection = Shader.PropertyToID("_ClipPlaneYDirection");
        private static readonly int PropClipZEnabled = Shader.PropertyToID("_ClipPlaneZEnabled");
        private static readonly int PropClipZPosition = Shader.PropertyToID("_ClipPlaneZPosition");
        private static readonly int PropClipZDirection = Shader.PropertyToID("_ClipPlaneZDirection");
        private static readonly int PropShowCrossSection = Shader.PropertyToID("_ShowCrossSection");
        private static readonly int PropCrossSectionColor = Shader.PropertyToID("_CrossSectionColor");
        private static readonly int PropCrossSectionWidth = Shader.PropertyToID("_CrossSectionWidth");
        
        #region Unity Lifecycle
        
        private void Start()
        {
            SetupUI();
            
            if (targetModel != null)
            {
                Initialize(targetModel);
            }
        }
        
        private void OnValidate()
        {
            // Update shader when values change in Inspector
            if (Application.isPlaying && _clippedMaterials.Count > 0)
            {
                UpdateAllShaderProperties();
                UpdatePlaneIndicators();
            }
        }
        
        private void OnDestroy()
        {
            // Cleanup plane indicators
            if (_planeIndicatorX != null) Destroy(_planeIndicatorX);
            if (_planeIndicatorY != null) Destroy(_planeIndicatorY);
            if (_planeIndicatorZ != null) Destroy(_planeIndicatorZ);
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initialize clipping for a model
        /// </summary>
        public void Initialize(GameObject model)
        {
            targetModel = model;
            _clippedMaterials.Clear();
            
            // Calculate model bounds
            CalculateModelBounds();
            
            // Apply clip shader to all materials
            if (autoApplyShader)
            {
                ApplyClipShaderToModel();
            }
            else
            {
                CollectExistingClipMaterials();
            }
            
            // Setup slider ranges based on bounds
            SetupSliderRanges();
            
            // Create plane indicators
            if (showPlaneIndicators)
            {
                CreatePlaneIndicators();
            }
            
            // Initial update
            UpdateAllShaderProperties();
            
            Debug.Log($"[ClipSection] Initialized for {model.name} with {_clippedMaterials.Count} materials");
        }
        
        private void CalculateModelBounds()
        {
            if (targetModel == null) return;
            
            var renderers = targetModel.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                _modelBounds = new Bounds(targetModel.transform.position, Vector3.one * 5f);
                return;
            }
            
            _modelBounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                _modelBounds.Encapsulate(r.bounds);
            }
            
            _boundsCalculated = true;
            Debug.Log($"[ClipSection] Model bounds: Center={_modelBounds.center}, Size={_modelBounds.size}");
        }
        
        private void ApplyClipShaderToModel()
        {
            if (targetModel == null) return;
            
            Shader clipShader = Shader.Find("VehicleMeasurement/ClipSection");
            if (clipShader == null)
            {
                Debug.LogError("[ClipSection] ClipSection shader not found! Make sure it's in your project.");
                return;
            }
            
            var renderers = targetModel.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                var materials = renderer.materials; // Creates instances
                for (int i = 0; i < materials.Length; i++)
                {
                    // Preserve original color/texture
                    Color originalColor = Color.white;
                    Texture originalTex = null;
                    float originalMetallic = 0f;
                    float originalSmoothness = 0.5f;
                    
                    if (materials[i].HasProperty("_Color"))
                        originalColor = materials[i].color;
                    if (materials[i].HasProperty("_MainTex"))
                        originalTex = materials[i].mainTexture;
                    if (materials[i].HasProperty("_Metallic"))
                        originalMetallic = materials[i].GetFloat("_Metallic");
                    if (materials[i].HasProperty("_Smoothness") || materials[i].HasProperty("_Glossiness"))
                    {
                        if (materials[i].HasProperty("_Smoothness"))
                            originalSmoothness = materials[i].GetFloat("_Smoothness");
                        else
                            originalSmoothness = materials[i].GetFloat("_Glossiness");
                    }
                    
                    // Apply clip shader
                    materials[i].shader = clipShader;
                    
                    // Restore properties
                    materials[i].color = originalColor;
                    if (originalTex != null)
                        materials[i].mainTexture = originalTex;
                    materials[i].SetFloat("_Metallic", originalMetallic);
                    materials[i].SetFloat("_Smoothness", originalSmoothness);
                    
                    _clippedMaterials.Add(materials[i]);
                }
                renderer.materials = materials;
            }
        }
        
        private void CollectExistingClipMaterials()
        {
            if (targetModel == null) return;
            
            var renderers = targetModel.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.HasProperty(PropClipXEnabled))
                    {
                        _clippedMaterials.Add(mat);
                    }
                }
            }
        }
        
        #endregion
        
        #region UI Setup
        
        private void SetupUI()
        {
            // X Slider
            if (clipXSlider != null)
            {
                clipXSlider.onValueChanged.AddListener(OnClipXSliderChanged);
            }
            if (clipXToggle != null)
            {
                clipXToggle.onValueChanged.AddListener(OnClipXToggleChanged);
            }
            
            // Y Slider
            if (clipYSlider != null)
            {
                clipYSlider.onValueChanged.AddListener(OnClipYSliderChanged);
            }
            if (clipYToggle != null)
            {
                clipYToggle.onValueChanged.AddListener(OnClipYToggleChanged);
            }
            
            // Z Slider
            if (clipZSlider != null)
            {
                clipZSlider.onValueChanged.AddListener(OnClipZSliderChanged);
            }
            if (clipZToggle != null)
            {
                clipZToggle.onValueChanged.AddListener(OnClipZToggleChanged);
            }
            
            // Preset buttons
            if (presetFrontHalfButton != null)
                presetFrontHalfButton.onClick.AddListener(PresetFrontHalf);
            if (presetSideHalfButton != null)
                presetSideHalfButton.onClick.AddListener(PresetSideHalf);
            if (presetTopHalfButton != null)
                presetTopHalfButton.onClick.AddListener(PresetTopHalf);
            if (presetResetButton != null)
                presetResetButton.onClick.AddListener(ResetClipping);
        }
        
        private void SetupSliderRanges()
        {
            if (!_boundsCalculated) return;
            
            float padding = 0.1f; // 10% padding
            
            if (clipXSlider != null)
            {
                float range = _modelBounds.size.x * (1f + padding);
                clipXSlider.minValue = _modelBounds.center.x - range / 2f;
                clipXSlider.maxValue = _modelBounds.center.x + range / 2f;
                clipXSlider.value = clipXSlider.maxValue; // Start at max (no clip)
            }
            
            if (clipYSlider != null)
            {
                float range = _modelBounds.size.y * (1f + padding);
                clipYSlider.minValue = _modelBounds.center.y - range / 2f;
                clipYSlider.maxValue = _modelBounds.center.y + range / 2f;
                clipYSlider.value = clipYSlider.maxValue;
            }
            
            if (clipZSlider != null)
            {
                float range = _modelBounds.size.z * (1f + padding);
                clipZSlider.minValue = _modelBounds.center.z - range / 2f;
                clipZSlider.maxValue = _modelBounds.center.z + range / 2f;
                clipZSlider.value = clipZSlider.maxValue;
            }
        }
        
        #endregion
        
        #region UI Callbacks
        
        private void OnClipXSliderChanged(float value)
        {
            clipXPosition = value;
            UpdateClipPlaneX();
            UpdateValueText(clipXValueText, value);
        }
        
        private void OnClipXToggleChanged(bool enabled)
        {
            enableClipX = enabled;
            UpdateClipPlaneX();
            UpdatePlaneIndicatorX();
        }
        
        private void OnClipYSliderChanged(float value)
        {
            clipYPosition = value;
            UpdateClipPlaneY();
            UpdateValueText(clipYValueText, value);
        }
        
        private void OnClipYToggleChanged(bool enabled)
        {
            enableClipY = enabled;
            UpdateClipPlaneY();
            UpdatePlaneIndicatorY();
        }
        
        private void OnClipZSliderChanged(float value)
        {
            clipZPosition = value;
            UpdateClipPlaneZ();
            UpdateValueText(clipZValueText, value);
        }
        
        private void OnClipZToggleChanged(bool enabled)
        {
            enableClipZ = enabled;
            UpdateClipPlaneZ();
            UpdatePlaneIndicatorZ();
        }
        
        private void UpdateValueText(TMP_Text text, float value)
        {
            if (text != null)
                text.text = $"{value:F2}";
        }
        
        #endregion
        
        #region Shader Updates
        
        private void UpdateAllShaderProperties()
        {
            UpdateClipPlaneX();
            UpdateClipPlaneY();
            UpdateClipPlaneZ();
            UpdateCrossSectionProperties();
        }
        
        private void UpdateClipPlaneX()
        {
            float direction = invertClipX ? -1f : 1f;
            
            foreach (var mat in _clippedMaterials)
            {
                mat.SetFloat(PropClipXEnabled, enableClipX ? 1f : 0f);
                mat.SetFloat(PropClipXPosition, clipXPosition);
                mat.SetFloat(PropClipXDirection, direction);
            }
            
            UpdatePlaneIndicatorX();
        }
        
        private void UpdateClipPlaneY()
        {
            float direction = invertClipY ? -1f : 1f;
            
            foreach (var mat in _clippedMaterials)
            {
                mat.SetFloat(PropClipYEnabled, enableClipY ? 1f : 0f);
                mat.SetFloat(PropClipYPosition, clipYPosition);
                mat.SetFloat(PropClipYDirection, direction);
            }
            
            UpdatePlaneIndicatorY();
        }
        
        private void UpdateClipPlaneZ()
        {
            float direction = invertClipZ ? -1f : 1f;
            
            foreach (var mat in _clippedMaterials)
            {
                mat.SetFloat(PropClipZEnabled, enableClipZ ? 1f : 0f);
                mat.SetFloat(PropClipZPosition, clipZPosition);
                mat.SetFloat(PropClipZDirection, direction);
            }
            
            UpdatePlaneIndicatorZ();
        }
        
        private void UpdateCrossSectionProperties()
        {
            foreach (var mat in _clippedMaterials)
            {
                mat.SetFloat(PropShowCrossSection, showCrossSection ? 1f : 0f);
                mat.SetColor(PropCrossSectionColor, crossSectionColor);
                mat.SetFloat(PropCrossSectionWidth, crossSectionWidth);
            }
        }
        
        #endregion
        
        #region Plane Indicators
        
        private void CreatePlaneIndicators()
        {
            if (planeIndicatorMaterial == null)
            {
                // Create default transparent material
                planeIndicatorMaterial = new Material(Shader.Find("Standard"));
                planeIndicatorMaterial.SetFloat("_Mode", 3); // Transparent
                planeIndicatorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                planeIndicatorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                planeIndicatorMaterial.SetInt("_ZWrite", 0);
                planeIndicatorMaterial.DisableKeyword("_ALPHATEST_ON");
                planeIndicatorMaterial.EnableKeyword("_ALPHABLEND_ON");
                planeIndicatorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                planeIndicatorMaterial.renderQueue = 3000;
                planeIndicatorMaterial.color = planeIndicatorColor;
            }
            
            _planeIndicatorX = CreatePlaneQuad("ClipPlane_X", new Color(1f, 0.3f, 0.3f, 0.3f));
            _planeIndicatorY = CreatePlaneQuad("ClipPlane_Y", new Color(0.3f, 1f, 0.3f, 0.3f));
            _planeIndicatorZ = CreatePlaneQuad("ClipPlane_Z", new Color(0.3f, 0.3f, 1f, 0.3f));
            
            UpdatePlaneIndicators();
        }
        
        private GameObject CreatePlaneQuad(string name, Color color)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = name;
            
            // Remove collider
            var collider = plane.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            
            // Setup material
            var renderer = plane.GetComponent<Renderer>();
            var mat = new Material(planeIndicatorMaterial);
            mat.color = color;
            renderer.material = mat;
            
            // Scale to model size
            float size = Mathf.Max(_modelBounds.size.x, _modelBounds.size.y, _modelBounds.size.z) * 1.5f;
            plane.transform.localScale = new Vector3(size, size, 1f);
            
            plane.SetActive(false);
            return plane;
        }
        
        private void UpdatePlaneIndicators()
        {
            UpdatePlaneIndicatorX();
            UpdatePlaneIndicatorY();
            UpdatePlaneIndicatorZ();
        }
        
        private void UpdatePlaneIndicatorX()
        {
            if (_planeIndicatorX == null) return;
            
            _planeIndicatorX.SetActive(enableClipX && showPlaneIndicators);
            if (enableClipX)
            {
                _planeIndicatorX.transform.position = new Vector3(clipXPosition, _modelBounds.center.y, _modelBounds.center.z);
                _planeIndicatorX.transform.rotation = Quaternion.Euler(0, 90, 0); // Face X axis
            }
        }
        
        private void UpdatePlaneIndicatorY()
        {
            if (_planeIndicatorY == null) return;
            
            _planeIndicatorY.SetActive(enableClipY && showPlaneIndicators);
            if (enableClipY)
            {
                _planeIndicatorY.transform.position = new Vector3(_modelBounds.center.x, clipYPosition, _modelBounds.center.z);
                _planeIndicatorY.transform.rotation = Quaternion.Euler(90, 0, 0); // Face Y axis
            }
        }
        
        private void UpdatePlaneIndicatorZ()
        {
            if (_planeIndicatorZ == null) return;
            
            _planeIndicatorZ.SetActive(enableClipZ && showPlaneIndicators);
            if (enableClipZ)
            {
                _planeIndicatorZ.transform.position = new Vector3(_modelBounds.center.x, _modelBounds.center.y, clipZPosition);
                _planeIndicatorZ.transform.rotation = Quaternion.Euler(0, 0, 0); // Face Z axis
            }
        }
        
        #endregion
        
        #region Presets
        
        /// <summary>
        /// Cut front half of vehicle (show rear)
        /// </summary>
        public void PresetFrontHalf()
        {
            enableClipX = false;
            enableClipY = false;
            enableClipZ = true;
            invertClipZ = false;
            
            clipZPosition = _modelBounds.center.z;
            
            if (clipZSlider != null) clipZSlider.value = clipZPosition;
            if (clipZToggle != null) clipZToggle.isOn = true;
            if (clipXToggle != null) clipXToggle.isOn = false;
            if (clipYToggle != null) clipYToggle.isOn = false;
            
            UpdateAllShaderProperties();
            UpdatePlaneIndicators();
        }
        
        /// <summary>
        /// Cut right half of vehicle (show left side)
        /// </summary>
        public void PresetSideHalf()
        {
            enableClipX = true;
            enableClipY = false;
            enableClipZ = false;
            invertClipX = false;
            
            clipXPosition = _modelBounds.center.x;
            
            if (clipXSlider != null) clipXSlider.value = clipXPosition;
            if (clipXToggle != null) clipXToggle.isOn = true;
            if (clipYToggle != null) clipYToggle.isOn = false;
            if (clipZToggle != null) clipZToggle.isOn = false;
            
            UpdateAllShaderProperties();
            UpdatePlaneIndicators();
        }
        
        /// <summary>
        /// Cut top half of vehicle (show bottom)
        /// </summary>
        public void PresetTopHalf()
        {
            enableClipX = false;
            enableClipY = true;
            enableClipZ = false;
            invertClipY = false;
            
            clipYPosition = _modelBounds.center.y;
            
            if (clipYSlider != null) clipYSlider.value = clipYPosition;
            if (clipYToggle != null) clipYToggle.isOn = true;
            if (clipXToggle != null) clipXToggle.isOn = false;
            if (clipZToggle != null) clipZToggle.isOn = false;
            
            UpdateAllShaderProperties();
            UpdatePlaneIndicators();
        }
        
        /// <summary>
        /// Reset all clipping (show full model)
        /// </summary>
        public void ResetClipping()
        {
            enableClipX = false;
            enableClipY = false;
            enableClipZ = false;
            
            if (clipXToggle != null) clipXToggle.isOn = false;
            if (clipYToggle != null) clipYToggle.isOn = false;
            if (clipZToggle != null) clipZToggle.isOn = false;
            
            // Reset sliders to max (no clip)
            if (clipXSlider != null) clipXSlider.value = clipXSlider.maxValue;
            if (clipYSlider != null) clipYSlider.value = clipYSlider.maxValue;
            if (clipZSlider != null) clipZSlider.value = clipZSlider.maxValue;
            
            UpdateAllShaderProperties();
            UpdatePlaneIndicators();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Set clip plane position programmatically
        /// </summary>
        public void SetClipPlane(ClipAxis axis, float position, bool enabled = true)
        {
            switch (axis)
            {
                case ClipAxis.X:
                    enableClipX = enabled;
                    clipXPosition = position;
                    if (clipXSlider != null) clipXSlider.value = position;
                    if (clipXToggle != null) clipXToggle.isOn = enabled;
                    UpdateClipPlaneX();
                    break;
                    
                case ClipAxis.Y:
                    enableClipY = enabled;
                    clipYPosition = position;
                    if (clipYSlider != null) clipYSlider.value = position;
                    if (clipYToggle != null) clipYToggle.isOn = enabled;
                    UpdateClipPlaneY();
                    break;
                    
                case ClipAxis.Z:
                    enableClipZ = enabled;
                    clipZPosition = position;
                    if (clipZSlider != null) clipZSlider.value = position;
                    if (clipZToggle != null) clipZToggle.isOn = enabled;
                    UpdateClipPlaneZ();
                    break;
            }
        }
        
        /// <summary>
        /// Get model bounds (useful for UI)
        /// </summary>
        public Bounds GetModelBounds() => _modelBounds;
        
        /// <summary>
        /// Set cross-section color
        /// </summary>
        public void SetCrossSectionColor(Color color)
        {
            crossSectionColor = color;
            UpdateCrossSectionProperties();
        }
        
        #endregion
    }
}
