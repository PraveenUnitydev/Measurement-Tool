using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VehicleMeasurement
{
    /// <summary>
    /// VEHICLE PREVIEW RENDERER
    /// 
    /// Renders a 3D vehicle to a RenderTexture for UI display.
    /// Supports rotation, zoom, and on-demand rendering for optimization.
    /// 
    /// Setup:
    /// 1. Create a dedicated camera (set Culling Mask to specific layer)
    /// 2. Create a RenderTexture asset (1024x1024 recommended)
    /// 3. Assign camera's Target Texture to the RenderTexture
    /// 4. Assign RenderTexture to a RawImage in UI
    /// 5. Set vehicle layer to match camera's Culling Mask
    /// </summary>
    public class VehiclePreviewRenderer : MonoBehaviour
    {
        [Header("═══ REFERENCES ═══")]
        [Tooltip("Camera that renders this vehicle")]
        public Camera previewCamera;

        [Tooltip("RawImage in UI that displays the render")]
        public RawImage displayImage;

        [Tooltip("Container transform for the vehicle model")]
        public Transform vehicleContainer;

        [Tooltip("Light for this vehicle (optional, for independent lighting)")]
        public Light vehicleLight;

        [Header("═══ RENDER SETTINGS ═══")]
        [Tooltip("Resolution of the RenderTexture")]
        public Vector2Int renderResolution = new Vector2Int(1024, 1024);

        [Tooltip("Layer for this vehicle (use unique layer per preview)")]
        public int vehicleLayer = 20; // User Layer 20

        [Tooltip("Only re-render when interacting (saves performance)")]
        public bool onDemandRendering = true;

        [Tooltip("Frames to continue rendering after interaction stops")]
        public int renderCooldownFrames = 10;

        [Header("═══ CAMERA SETTINGS ═══")]
        [Tooltip("Distance from vehicle center")]
        public float cameraDistance = 6f;

        [Tooltip("Camera orbit angle (horizontal)")]
        public float orbitAngle = 45f;

        [Tooltip("Camera pitch angle (vertical)")]
        public float pitchAngle = 15f;

        [Tooltip("Camera field of view")]
        public float fieldOfView = 35f;

        [Header("═══ INTERACTION ═══")]
        [Tooltip("Enable mouse/touch rotation")]
        public bool enableRotation = true;

        [Tooltip("Enable mouse/touch zoom")]
        public bool enableZoom = true;

        [Tooltip("Rotation sensitivity")]
        public float rotationSensitivity = 0.5f;

        [Tooltip("Zoom sensitivity")]
        public float zoomSensitivity = 0.5f;

        [Tooltip("Minimum zoom distance")]
        public float minZoomDistance = 3f;

        [Tooltip("Maximum zoom distance")]
        public float maxZoomDistance = 15f;

        // Runtime
        private RenderTexture _renderTexture;
        private GameObject _currentVehicle;
        private Bounds _vehicleBounds;
        private Vector3 _vehicleCenter;
        private bool _isDragging;
        private Vector2 _lastMousePos;
        private int _renderCooldown;
        private bool _needsRender = true;
        private UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> _currentAddressableHandle;
        private bool _hasAddressableHandle = false;

        // Events
        public event Action<GameObject> OnVehicleLoaded;
        public event Action OnVehicleUnloaded;
        public event Action<float, float, float> OnViewChanged;

        #region Initialization

        private void Awake()
        {
            CreateRenderTexture();
            SetupCamera();
        }

        private void OnDestroy()
        {
            UnloadVehicle();
            CleanupRenderTexture();
        }

        private void CreateRenderTexture()
        {
            if (_renderTexture != null)
                CleanupRenderTexture();

            _renderTexture = new RenderTexture(renderResolution.x, renderResolution.y, 24, RenderTextureFormat.ARGB32);
            _renderTexture.antiAliasing = 4;
            _renderTexture.Create();

            if (previewCamera != null)
                previewCamera.targetTexture = _renderTexture;

            if (displayImage != null)
                displayImage.texture = _renderTexture;

            Debug.Log($"[VehiclePreview] Created RenderTexture {renderResolution.x}x{renderResolution.y}");
        }

        private void CleanupRenderTexture()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }
        }

        private void SetupCamera()
        {
            if (previewCamera == null) return;

            // Only render the vehicle layer
            previewCamera.cullingMask = 1 << vehicleLayer;
            previewCamera.fieldOfView = fieldOfView;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(1f, 1f, 1f, 1f);

            // Disable by default if using on-demand rendering
            if (onDemandRendering)
                previewCamera.enabled = false;

            UpdateCameraPosition();
        }

        #endregion

        #region Vehicle Loading

        /// <summary>
        /// Load a vehicle from saved data (uses Addressables if available)
        /// </summary>
        public void LoadVehicle(SavedVehicleMeasurement savedData)
        {
            if (savedData == null)
            {
                Debug.LogWarning("[VehiclePreview] No saved data provided");
                return;
            }

            // Clear existing
            UnloadVehicle();

            // Check if model source is available
            if (savedData.HasModelSource())
            {
                var loadType = savedData.GetModelLoadType();
                string path = savedData.modelPath;

                Debug.Log($"[VehiclePreview] Loading model: {path} ({loadType})");

                if (loadType == ModelLoadType.Addressables)
                {
                    LoadFromAddressables(path);
                }
                else if (loadType == ModelLoadType.Resources)
                {
                    LoadFromResources(path);
                }
            }
            else
            {
                Debug.LogWarning("[VehiclePreview] Saved data has no model source, creating placeholder");
                CreatePlaceholderVehicle(savedData);
            }
        }

        /// <summary>
        /// Load directly from Addressables key
        /// </summary>
        public void LoadFromAddressables(string addressableKey)
        {
            Debug.Log($"[VehiclePreview] {gameObject.name} loading from Addressables: {addressableKey}");

            // Use Addressables directly instead of going through the singleton loader
            // This prevents the singleton from unloading our vehicle when another preview loads
            StartCoroutine(LoadFromAddressablesCoroutine(addressableKey));
        }

        private System.Collections.IEnumerator LoadFromAddressablesCoroutine(string addressableKey)
        {
            // First check if we need to download
            var sizeHandle = UnityEngine.AddressableAssets.Addressables.GetDownloadSizeAsync(addressableKey);
            yield return sizeHandle;

            long downloadSize = sizeHandle.Result;
            UnityEngine.AddressableAssets.Addressables.Release(sizeHandle);

            if (downloadSize > 0)
            {
                Debug.Log($"[VehiclePreview] Downloading {addressableKey}: {downloadSize} bytes");

                var downloadHandle = UnityEngine.AddressableAssets.Addressables.DownloadDependenciesAsync(addressableKey);
                while (!downloadHandle.IsDone)
                {
                    yield return null;
                }
                UnityEngine.AddressableAssets.Addressables.Release(downloadHandle);
            }

            // Now instantiate
            Debug.Log($"[VehiclePreview] Instantiating {addressableKey} into {vehicleContainer.name}");

            var instantiateHandle = UnityEngine.AddressableAssets.Addressables.InstantiateAsync(addressableKey, vehicleContainer);
            yield return instantiateHandle;

            if (instantiateHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
            {
                var vehicle = instantiateHandle.Result;
                Debug.Log($"[VehiclePreview] ✓ Successfully loaded: {vehicle.name} for {gameObject.name}");

                // Store the handle so we can release it later
                _currentAddressableHandle = instantiateHandle;
                _hasAddressableHandle = true;

                OnVehicleModelLoaded(vehicle);
            }
            else
            {
                Debug.LogError($"[VehiclePreview] Failed to load {addressableKey}: {instantiateHandle.OperationException}");
            }
        }

        /// <summary>
        /// Load from Resources folder
        /// </summary>
        public void LoadFromResources(string resourcePath)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
            {
                var instance = Instantiate(prefab, vehicleContainer);
                OnVehicleModelLoaded(instance);
            }
            else
            {
                Debug.LogError($"[VehiclePreview] Resource not found: {resourcePath}");
            }
        }

        /// <summary>
        /// Load an already instantiated vehicle
        /// </summary>
        public void SetVehicle(GameObject vehicle)
        {
            UnloadVehicle();

            if (vehicle != null)
            {
                _currentVehicle = vehicle;
                vehicle.transform.SetParent(vehicleContainer);
                vehicle.transform.localPosition = Vector3.zero;
                vehicle.transform.localRotation = Quaternion.identity;

                SetupLoadedVehicle();
            }
        }

        private void OnVehicleModelLoaded(GameObject vehicle)
        {
            _currentVehicle = vehicle;
            SetupLoadedVehicle();
        }

        private void SetupLoadedVehicle()
        {
            if (_currentVehicle == null) return;

            // Set layer recursively
            SetLayerRecursively(_currentVehicle, vehicleLayer);

            // Calculate bounds
            _vehicleBounds = CalculateBounds(_currentVehicle);
            _vehicleCenter = _vehicleBounds.center;

            // Position vehicle at origin
            _currentVehicle.transform.localPosition = -_vehicleCenter;

            // Auto-adjust camera distance based on vehicle size
            float maxDimension = Mathf.Max(_vehicleBounds.size.x, _vehicleBounds.size.y, _vehicleBounds.size.z);
            cameraDistance = maxDimension * 1.5f;
            cameraDistance = Mathf.Clamp(cameraDistance, minZoomDistance, maxZoomDistance);

            UpdateCameraPosition();
            RequestRender();

            OnVehicleLoaded?.Invoke(_currentVehicle);

            Debug.Log($"[VehiclePreview] Vehicle loaded. Bounds: {_vehicleBounds.size}, Distance: {cameraDistance}");
        }

        /// <summary>
        /// Create a simple placeholder box when no model is available
        /// </summary>
        private void CreatePlaceholderVehicle(SavedVehicleMeasurement data)
        {
            // Create a box with approximate dimensions
            float length = data.L103_OverallLength / 1000f;
            float width = data.W103_OverallWidth / 1000f;
            float height = data.H100_OverallHeight / 1000f;

            if (length <= 0) length = 4.5f;
            if (width <= 0) width = 1.8f;
            if (height <= 0) height = 1.5f;

            var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = "VehiclePlaceholder";
            placeholder.transform.SetParent(vehicleContainer);
            placeholder.transform.localScale = new Vector3(width, height, length);
            placeholder.transform.localPosition = new Vector3(0, height / 2f, 0);

            // Semi-transparent material
            var renderer = placeholder.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
            renderer.material = mat;

            _currentVehicle = placeholder;
            SetupLoadedVehicle();
        }

        /// <summary>
        /// Unload current vehicle
        /// </summary>
        public void UnloadVehicle()
        {
            if (_currentVehicle != null)
            {
                // If loaded via Addressables, release the handle properly
                if (_hasAddressableHandle)
                {
                    UnityEngine.AddressableAssets.Addressables.ReleaseInstance(_currentVehicle);
                    _hasAddressableHandle = false;
                }
                else
                {
                    Destroy(_currentVehicle);
                }

                _currentVehicle = null;
                OnVehicleUnloaded?.Invoke();

                Debug.Log($"[VehiclePreview] {gameObject.name} unloaded vehicle");
            }
        }

        #endregion

        #region Camera Control

        private void UpdateCameraPosition()
        {
            if (previewCamera == null) return;

            // Calculate camera position using orbit angles
            float radOrbit = orbitAngle * Mathf.Deg2Rad;
            float radPitch = pitchAngle * Mathf.Deg2Rad;

            Vector3 offset = new Vector3(
                Mathf.Sin(radOrbit) * Mathf.Cos(radPitch) * cameraDistance,
                Mathf.Sin(radPitch) * cameraDistance,
                Mathf.Cos(radOrbit) * Mathf.Cos(radPitch) * cameraDistance
            );

            previewCamera.transform.position = vehicleContainer.position + offset;
            previewCamera.transform.LookAt(vehicleContainer.position);
        }

        /// <summary>
        /// Set camera orbit angle (0-360 degrees around vehicle)
        /// </summary>
        public void SetOrbitAngle(float angle)
        {
            orbitAngle = angle;
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        /// <summary>
        /// Set camera pitch angle (-89 to 89 degrees)
        /// </summary>
        public void SetPitchAngle(float angle)
        {
            pitchAngle = Mathf.Clamp(angle, -89f, 89f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        /// <summary>
        /// Set zoom distance
        /// </summary>
        public void SetZoomDistance(float distance)
        {
            cameraDistance = Mathf.Clamp(distance, minZoomDistance, maxZoomDistance);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        private void NotifyViewChanged()
        {
            OnViewChanged?.Invoke(orbitAngle, pitchAngle, cameraDistance);
        }


        /// <summary>
        /// Reset camera to default view
        /// </summary>
        public void ResetView()
        {
            orbitAngle = 45f;
            pitchAngle = 15f;

            if (_currentVehicle != null)
            {
                float maxDimension = Mathf.Max(_vehicleBounds.size.x, _vehicleBounds.size.y, _vehicleBounds.size.z);
                cameraDistance = maxDimension * 1.5f;
            }
            else
            {
                cameraDistance = 6f;
            }

            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        /// <summary>
        /// Set view to front
        /// </summary>
        public void SetBackView()
        {
            orbitAngle = 180f;
            pitchAngle = 0f;
            SetZoomDistance(5.25f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }
        public void SetFrontView()
        {
            orbitAngle = 0f;
            pitchAngle = 0f;
            SetZoomDistance(5.25f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        /// <summary>
        /// Set view to side
        /// </summary>
        public void SetRightSideView()
        {
            orbitAngle = 90f;
            pitchAngle = 0f;
            SetZoomDistance(5f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }
        public void SetLeftSideView()
        {
            orbitAngle = -90f;
            pitchAngle = 0f;
            SetZoomDistance(5f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        /// <summary>
        /// Set view to top
        /// </summary>
        public void SetTopView()
        {
            orbitAngle = 90f;
            pitchAngle = 89f;
            SetZoomDistance(5.5f);
            UpdateCameraPosition();
            RequestRender();
            NotifyViewChanged();
        }

        #endregion

        #region Interaction

        private void Update()
        {
            HandleInteraction();
            HandleOnDemandRendering();
        }

        private void HandleInteraction()
        {
            if (displayImage == null) return;

            // Check if mouse button was just released - reset drag state
            if (Input.GetMouseButtonUp(0))
            {
                _isDragging = false;
            }

            // Check if pointer is over THIS preview's RawImage
            bool isOverThisPreview = IsPointerOverPreview();

            // Only start new drag if mouse down happens over this preview
            if (enableRotation && Input.GetMouseButtonDown(0) && isOverThisPreview)
            {
                _isDragging = true;
                _lastMousePos = Input.mousePosition;
                Debug.Log($"[VehiclePreview] Started dragging on {gameObject.name}");
            }

            // Continue drag only if we started it on this preview
            if (_isDragging && Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _lastMousePos;
                _lastMousePos = Input.mousePosition;

                if (delta.sqrMagnitude > 0.01f)
                {
                    orbitAngle -= delta.x * rotationSensitivity;
                    pitchAngle += delta.y * rotationSensitivity;
                    pitchAngle = Mathf.Clamp(pitchAngle, -89f, 89f);

                    UpdateCameraPosition();
                    RequestRender();
                    NotifyViewChanged();
                }
            }

            // Zoom only if pointer is over this preview
            if (enableZoom && isOverThisPreview)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    cameraDistance -= scroll * zoomSensitivity * cameraDistance;
                    cameraDistance = Mathf.Clamp(cameraDistance, minZoomDistance, maxZoomDistance);
                    UpdateCameraPosition();
                    RequestRender();
                    NotifyViewChanged();
                }
            }
        }

        public void SetInteractionEnabled(bool enabled)
        {
            enableRotation = enabled;
            enableZoom = enabled;
        }


        private bool IsPointerOverPreview()
        {
            if (displayImage == null) return false;

            // Get the canvas
            Canvas canvas = displayImage.canvas;
            Camera eventCamera = null;

            if (canvas != null)
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    eventCamera = null;
                }
                else
                {
                    eventCamera = canvas.worldCamera;
                }
            }

            RectTransform rect = displayImage.rectTransform;

            // Check if pointer is within this RawImage's rect
            if (!RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eventCamera))
                return false;

            // Additional check: make sure no other UI element is blocking
            // Use EventSystem to check what's actually under the pointer
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
                {
                    position = Input.mousePosition
                };

                var raycastResults = new List<UnityEngine.EventSystems.RaycastResult>();
                UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, raycastResults);

                // Check if the first (topmost) UI element hit is our displayImage or a child of it
                if (raycastResults.Count > 0)
                {
                    GameObject hitObject = raycastResults[0].gameObject;

                    // Check if hit object is displayImage or a child of it
                    if (hitObject == displayImage.gameObject)
                        return true;

                    // Check if hit object is a child of displayImage
                    Transform current = hitObject.transform;
                    while (current != null)
                    {
                        if (current == displayImage.transform)
                            return true;
                        current = current.parent;
                    }

                    // Hit something else that's on top
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region On-Demand Rendering

        /// <summary>
        /// Request a render (for on-demand mode)
        /// </summary>
        public void RequestRender()
        {
            _needsRender = true;
            _renderCooldown = renderCooldownFrames;
        }

        /// <summary>
        /// Force immediate render
        /// </summary>
        public void ForceRender()
        {
            if (previewCamera != null && _renderTexture != null)
            {
                previewCamera.Render();
            }
        }

        private void HandleOnDemandRendering()
        {
            if (!onDemandRendering)
            {
                // Continuous rendering
                if (previewCamera != null)
                    previewCamera.enabled = true;
                return;
            }

            // On-demand rendering
            if (_needsRender || _renderCooldown > 0)
            {
                if (previewCamera != null)
                {
                    previewCamera.Render();
                }

                _needsRender = false;
                _renderCooldown--;
            }
        }

        #endregion

        #region Utilities

        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private Bounds CalculateBounds(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(obj.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        /// <summary>
        /// Get current vehicle bounds (for measurement overlay)
        /// </summary>
        public Bounds GetVehicleBounds()
        {
            return _vehicleBounds;
        }

        /// <summary>
        /// Check if a vehicle is loaded
        /// </summary>
        public bool HasVehicle => _currentVehicle != null;

        /// <summary>
        /// Get current vehicle GameObject
        /// </summary>
        public GameObject CurrentVehicle => _currentVehicle;

        #endregion
        // VehiclePreviewRenderer.cs (add inside class)

        public RenderTexture PreviewRenderTexture => _renderTexture;

        /// <summary>
        /// Capture the current preview RenderTexture into a Texture2D.
        /// Works with on-demand rendering by forcing a render first.
        /// </summary>
        public IEnumerator CapturePreviewTexture(Action<Texture2D> onCaptured, bool forceRender = true)
        {
            // Wait until end of frame to ensure everything is drawn
            yield return new WaitForEndOfFrame();

            if (_renderTexture == null)
            {
                onCaptured?.Invoke(null);
                yield break;
            }

            if (forceRender)
                ForceRender(); // already exists in your script [3](https://mahindraonline-my.sharepoint.com/personal/25033899_mahindra_com/Documents/Microsoft%20Copilot%20Chat%20Files/VehiclePreviewRenderer.cs)

            var prevActive = RenderTexture.active;
            RenderTexture.active = _renderTexture;

            Texture2D tex = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
            tex.Apply();

            RenderTexture.active = prevActive;

            onCaptured?.Invoke(tex);
        }
    }
}
