using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.UI;


namespace VehicleMeasurement
{
    /// <summary>
    /// MEASUREMENT CONTROLLER
    /// 
    /// All TMP_Text fields are assigned directly in the Inspector.
    /// No runtime UI creation - just map your existing UI elements.
    /// Supports loading via Resources or Addressables.
    /// </summary>
    public class MeasurementController : MonoBehaviour
    {
        [Header("═══ MEASUREMENT SYSTEM ═══")]
        public VehicleMeasurementSystem measurementSystem;
        public DimensionLineRenderer dimensionLines;

        [Header("═══ MODEL LOADING ═══")]
        public Transform modelContainer;
        [Tooltip("Optional: Manually assign the actual model root if auto-detection fails")]
        public Transform manualModelRoot;
        [Tooltip("Optional: Manually assign the tyres mesh")]
        public Transform manualTyresMesh;
        public string modelsResourcePath = "Vehicles";

        [Header("═══ ADDRESSABLES ═══")]
        [Tooltip("Reference to Addressable loader (optional - for large models)")]
        public AddressableVehicleLoader addressableLoader;
        public RemoteAddressableVehicleLoader remoteLoader;
        [Tooltip("Use Addressables for benchmarking vehicles")]
        public bool useAddressablesForBenchmark = true;

        [Header("═══ MODEL PICKER PANEL ═══")]
        public GameObject modelPickerPanel;
        public Transform modelListContainer;
        public GameObject modelListItemPrefab;

        [Header("Search & Filter")]
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private TMP_Dropdown manufacturerFilterDropdown;
        [SerializeField] private Button clearFiltersButton;
        [SerializeField] private Button _cancelOnImportButton;
        public TMP_Text resultsCountText;
        [SerializeField] private TMP_Text totalVehiclesText;

        // Private filter state
        private string _searchQuery = "";
        private string _selectedManufacturer = "";
        private List<VehicleListItem> _allVehicles =
            new List<VehicleListItem>();



        [Header("═══ LOADING/DOWNLOAD PANEL ═══")]
        public GameObject loadingPanel;
        public Slider loadingProgress;
        public TMP_Text loadingText;
        public TMP_Text downloadSpeedText;
        public TMP_Text downloadETAText;

        [Header("═══ ANALYSIS LOADING UI (NEW) ═══")]
        [Tooltip("Spinning loader icon - will rotate during analysis")]
        public Transform loadingSpinner;
        [Tooltip("Progress text showing percentage (optional, separate from loadingText)")]
        public TMP_Text analysisProgressText;

        public bool isAdmin = false;

        [Header("═══ HEADER UI ═══")]
        public TMP_Text vehicleNameText;
        public TMP_Text statusText;
        public TMP_InputField vehicleNameInput;

        [Header("═══ BUTTONS ═══")]
        public Button backButton;
        public Button analyzeButton;
        public Button saveButton;
        public Button deleteButton;
        public Button compareWithButton;
        public Button exportButton;

        [Header("═══ EXPORT (NEW) ═══")]
        [Tooltip("Camera used for PDF screenshot (defaults to Camera.main if not set)")]
        public Camera exportCamera;



        [Header("═══ CLIP SECTION ═══")]
        [Tooltip("Reference to ClipSectionMode component (optional)")]
        public ClipSectionMode clipSectionMode;

        [Header("═══ MEASUREMENT TEXTS - LENGTH ═══")]
        public TMP_Text L103_OverallLength;
        public TMP_Text L101_Wheelbase;
        public TMP_Text L104_FrontOverhang;
        public TMP_Text L105_RearOverhang;

        [Header("═══ MEASUREMENT TEXTS - WIDTH ═══")]
        public TMP_Text W103_OverallWidth;
        public TMP_Text W144_FrontTrack;
        public TMP_Text W145_RearTrack;

        [Header("═══ MEASUREMENT TEXTS - HEIGHT ═══")]
        public TMP_Text H100_OverallHeight;
        public TMP_Text H101_GroundClearance;

        [Header("═══ MEASUREMENT TEXTS - WHEELS ═══")]
        public TMP_Text TD_FrontDiameter;
        public TMP_Text TD_RearDiameter;

        [Header("═══ SETTINGS ═══")]
        public string emptyValueText = "---";

        // Private
        private VehicleDataManager _dataManager;
        private string _currentVehicleId;
        private SavedVehicleMeasurement _currentData;
        private GameObject _loadedModel;
        private bool _hasUnsavedChanges;
        private bool _isAnalyzing; // NEW: Track analysis state


        // Model source tracking (for saving)
        private string _currentModelPath;
        private ModelLoadType _currentModelLoadType;
        private string _currentAddressableId;

        // Thumbnail tracking
        private Sprite _currentThumbnail;
        private string _currentThumbnailUrl;

        private bool _isReadOnly = false;
        private MeasurementSource _dataSource = MeasurementSource.None;
        // public Texture2D myLogo;



        [Header("Axis Control")]
        public OrbitCameraController _orbitCam;
        public Button axisFront, axisBack, axisLeft, axisTop, axisRight, driverView;

        [Header("UnSaved Panel")]
        public GameObject _unsavedPanelPopup;
        public Button _unsavedSaveButton;
        public Button _unsavedCancelButton;
        public Button _unsavedNoButton;

        [Header("═══ MODEL INSPECTOR ═══")]
        public ModelHierarchyInspector modelInspector;

        private bool _waitingForRemoteCatalog = false;

        #region Unity Lifecycle

        private void Start()
        {
            _dataManager = VehicleDataManager.Instance;


            var remote = RemoteAddressableVehicleLoader.Instance;
            if (useAddressablesForBenchmark && remote != null)
            {
                // Avoid double-registering on scene reload
                remote.OnCatalogLoaded?.RemoveListener(OnRemoteCatalogLoaded);
                remote.OnCatalogLoaded?.AddListener(OnRemoteCatalogLoaded);

                if (!remote.IsCatalogLoaded)
                {
                    // Defer model list until catalog arrives
                    _waitingForRemoteCatalog = true;
                }
            }


            Debug.Log($"[DEBUG] MeasurementController.Start:");
            Debug.Log($"  SelectedVehicleId: {_dataManager.SelectedVehicleId}");
            Debug.Log($"  SelectedModelPath: {_dataManager.SelectedModelPath}");
            SetupButtonListeners();
            if (!string.IsNullOrEmpty(_dataManager.SelectedVehicleId))
            {
                // Loading existing saved vehicle
                Debug.Log($"[MeasurementController] Loading existing vehicle: {_dataManager.SelectedVehicleId}");
                LoadExistingVehicle(_dataManager.SelectedVehicleId);
            }
            else
            {
                // Setting up new vehicle
                Debug.Log("[MeasurementController] Setting up new vehicle");
                SetupNewVehicle();
            }

            SetupSearchAndFilters();
        }
        private void Update()
        {
            // NEW: Rotate loading spinner during analysis (uses unscaledDeltaTime for smooth animation)
            if (loadingSpinner != null && loadingPanel != null && loadingPanel.activeSelf)
            {
                loadingSpinner.Rotate(0, 0, -200f * Time.unscaledDeltaTime);
            }
        }

        private void OnRemoteCatalogLoaded(int vehicleCount)
        {
            _waitingForRemoteCatalog = false;

            Debug.Log($"[MeasurementController] Remote catalog ready ({vehicleCount} vehicles). Populating list.");
            PopulateModelList();    // uses RemoteAddressableVehicleLoader.GetAvailableVehicles()
        }

        private void SetupButtonListeners()
        {
            backButton?.onClick.AddListener(OnBackClick);
            analyzeButton?.onClick.AddListener(() => PopupManager.ShowConfirm("Dimension analysis is currently in Beta and may take additional time to complete. Do you want to continue?", OnAnalyzeClick));
            saveButton?.onClick.AddListener(OnSaveClick);
            deleteButton?.onClick.AddListener(OnDeleteClick);
            compareWithButton?.onClick.AddListener(OnCompareWithClick);
            exportButton?.onClick.AddListener(OnExportClick);

            axisFront?.onClick.AddListener(() => _orbitCam?.SetFrontView());
            axisBack?.onClick.AddListener(() => _orbitCam?.SetRearView());
            axisRight?.onClick.AddListener(() => _orbitCam?.SetRightSideView());
            axisTop?.onClick.AddListener(() => _orbitCam?.SetTopView());
            axisLeft?.onClick.AddListener(() => _orbitCam?.SetLeftSideView());
            // driverView?.onClick?.AddListener(() => _orbitCam?.SetDriverView(Vehicle));

            _unsavedSaveButton?.onClick.AddListener(OnUnsavedSaveClicked);
            _unsavedNoButton?.onClick.AddListener(() => GoBackToHome());
            _unsavedCancelButton?.onClick.AddListener(() => UnsavedPopup(false));
            _cancelOnImportButton?.onClick.AddListener(CancelButtonClickOnImport);
        }

        private void OnDestroy()
        {
            // Cancel any ongoing analysis
            AsyncMeasurement.CancelAnalysis();


            // Cleanup clip section
            if (clipSectionMode != null)
                clipSectionMode.OnVehicleUnloaded();

            // Cleanup Addressable listeners
            CleanupAddressableListeners();
            CleanupRemoteAddressableListeners();
            // Destroy loaded model
            if (_loadedModel != null)
                Destroy(_loadedModel);

            var remote = RemoteAddressableVehicleLoader.Instance;
            if (remote != null)
                remote.OnCatalogLoaded?.RemoveListener(OnRemoteCatalogLoaded);

        }

        private void DebugLogLocationsForKey(string addressableKey)
        {
            var handle = Addressables.LoadResourceLocationsAsync(addressableKey);
            handle.Completed += h =>
            {
                if (h.Status != AsyncOperationStatus.Succeeded || h.Result == null)
                {
                    Debug.LogWarning($"[ADDR] No locations for key: {addressableKey}");
                }
                else
                {
                    foreach (IResourceLocation loc in h.Result)
                    {
                        Debug.Log($"[ADDR] Key '{addressableKey}' → {loc.InternalId}");
                    }
                }
                Addressables.Release(h);
            };
        }

        #endregion

        #region Setup


        private bool _isLoadingExistingVehicle;

        private void SetupNewVehicle()
        {
            _isLoadingExistingVehicle = false;

            SetText(vehicleNameText, "New Vehicle");
            SetText(statusText, "Select a model to measure");
            ClearAllMeasurements();

            /* if (deleteButton != null)
                 deleteButton.gameObject.SetActive(false);*/

            // Check if a model was pre-selected from Home (for downloaded vehicles)
            if (!string.IsNullOrEmpty(_dataManager.SelectedModelPath))
            {
                Debug.Log($"[MeasurementController] Loading pre-selected model: {_dataManager.SelectedModelPath}");
                ClearExistingModels();
                LoadModel(_dataManager.SelectedModelPath, ModelLoadType.Addressables);
                _dataManager.ClearSelectedModel();
            }
            else
            {
                // No pre-selected model - show picker
                ShowModelPicker();
            }
        }

        private void LoadExistingVehicle(string vehicleId)
        {
            Debug.Log($"[MeasurementController] >>> LoadExistingVehicle ENTER vehicleId={vehicleId}");
            _currentVehicleId = vehicleId;

            // Show loading
            SetText(statusText, "Checking for existing data...");
            if (loadingPanel != null) loadingPanel.SetActive(true);


            // Use ControlledMeasurementStorage to check server first
            ControlledMeasurementStorage.Instance.CheckAndLoad(vehicleId, (result) =>
            {
                // Hide loading
                if (loadingPanel != null) loadingPanel.SetActive(false);
                Debug.Log($"[LoadExistingVehicle] requested vehicleId={vehicleId} Found={result.Found} Source={result.Source} DataNull={(result.Data == null)}");
                if (result.Data != null)
                    Debug.Log($"[LoadExistingVehicle] loadedData.vehicleId={result.Data.vehicleId} modelPath={result.Data.modelPath}");

                if (result.Found)
                {
                    // Data exists - load it
                    _currentData = result.Data;
                    _dataSource = result.Source;
                    // _isReadOnly = result.IsReadOnly;

                    if (result.Source == MeasurementSource.Server && result.Data != null)
                    {
                        CacheServerDataLocallyIfNeeded(vehicleId, result.Data);
                    }

                    SetText(vehicleNameText, _currentData.vehicleName);

                    // Show source indicator
                    string sourceText = result.Source == MeasurementSource.Server ? " Server" : " Local";
                    string readOnlyText = result.IsReadOnly ? " (Read-Only)" : "";
                    SetText(statusText, $"Loaded from {sourceText}{readOnlyText} - {_currentData.lastModified}");

                    if (vehicleNameInput != null)
                        vehicleNameInput.text = _currentData.vehicleName;

                    RefreshMeasurementDisplay();
                    SetupMeasurementSystemFromSavedData();

                    /*    // If read-only, disable editing
                        if (_isReadOnly)
                        {
                            DisableMeasurementEditing();
                        }
                        else
                        {
                            EnableMeasurementEditing();
                        }*/

                    // Try to reload the 3D model
                    if (_currentData.HasModelSource())
                    {
                        ReloadModelFromSavedData();
                    }
                    else
                    {
                        SetText(statusText, $"Loaded (no 3D model) - {_currentData.lastModified}");
                    }
                }
                else
                {
                    // No data found - allow new measurement
                    _currentData = null;
                    _dataSource = MeasurementSource.None;
                    // _isReadOnly = false;

                    SetText(vehicleNameText, vehicleId);
                    SetText(statusText, "No saved data - Ready for measurement");
                    ClearAllMeasurements();
                    //  EnableMeasurementEditing();
                }

                /* if (deleteButton != null)
                     deleteButton.gameObject.SetActive(!_isReadOnly);*/
            });
        }

        private void CacheServerDataLocallyIfNeeded(string requestedVehicleId, SavedVehicleMeasurement serverData)
        {
            if (serverData == null)
                return;

            // Use the requestedVehicleId as the filename key (your storage uses the passed vehicleId for file name). 
            string storageKey = requestedVehicleId;

            // Ensure model source exists so model reload works from local cache later.
            // Your SavedVehicleMeasurement supports modelPath/modelLoadType/addressableVehicleId.
            if (!string.IsNullOrEmpty(_currentModelPath))
            {
                serverData.modelPath = _currentModelPath;
                serverData.modelLoadType = _currentModelLoadType.ToString();
                serverData.addressableVehicleId = _currentAddressableId;
            }
            else
            {
                // If controller doesn't know model path yet, at least keep whatever came from server.
                // (No changes needed.)
            }

            // Always keep the file key aligned with what the controller uses.
            // (Your list uses filename as the reliable ID.) 
            serverData.vehicleId = storageKey;


            bool ok = VehicleMeasurementStorage.SaveData(serverData, storageKey);
            if (ok)
                Debug.Log($"[MeasurementController] Cached server measurements locally: {storageKey}");
            else
                Debug.LogWarning($"[MeasurementController] Failed to cache server measurements locally: {storageKey}");
        }
        /*     private void DisableMeasurementEditing()
             {
                 if (analyzeButton != null) analyzeButton.interactable = false;
                 if (saveButton != null) saveButton.interactable = false;
                 if (vehicleNameInput != null) vehicleNameInput.interactable = false;

                 Debug.Log("[MeasurementController] Editing disabled - data is read-only");
             }*/

        /* private void EnableMeasurementEditing()
         {
             if (analyzeButton != null) analyzeButton.interactable = true;
             if (saveButton != null) saveButton.interactable = true;
             if (vehicleNameInput != null) vehicleNameInput.interactable = true;

             Debug.Log("[MeasurementController] Editing enabled");
         }*/
        /// <summary>
        /// Reload 3D model from saved data source
        /// </summary>
        /// 

        private void ReloadModelFromSavedData()
        {
            if (_currentData == null || !_currentData.HasModelSource())
                return;

            var loadType = _currentData.GetModelLoadType();
            string path = _currentData.modelPath;

            Debug.Log($"[MeasurementController] Reloading model: {path} ({loadType})");

            // Store the current model info for the loading process
            _currentModelPath = path;
            _currentModelLoadType = loadType;

            LoadModel(path, loadType);
        }

        /// <summary>
        /// Populate measurement system with saved data so dimension lines can work
        /// </summary>
        private void SetupMeasurementSystemFromSavedData()
        {
            if (_currentData == null) return;

            // Ensure we have a measurement system
            if (measurementSystem == null)
            {
                var go = new GameObject("MeasurementSystemProxy");
                go.transform.SetParent(transform);
                measurementSystem = go.AddComponent<VehicleMeasurementSystem>();
            }

            // Copy values from saved data to measurement system (convert mm to meters for internal use)
            measurementSystem.L103_OverallLength = _currentData.L103_OverallLength;
            measurementSystem.L101_Wheelbase = _currentData.L101_Wheelbase;
            measurementSystem.L104_FrontOverhang = _currentData.L104_FrontOverhang;
            measurementSystem.L105_RearOverhang = _currentData.L105_RearOverhang;

            measurementSystem.W103_OverallWidth = _currentData.W103_OverallWidth;
            measurementSystem.W144_FrontTrack = _currentData.W144_FrontTrack;
            measurementSystem.W145_RearTrack = _currentData.W145_RearTrack;

            measurementSystem.H100_OverallHeight = _currentData.H100_OverallHeight;
            measurementSystem.H101_GroundClearance = _currentData.H101_GroundClearance;

            measurementSystem.TD_F_FrontDiameter = _currentData.TD_F_FrontDiameter;
            measurementSystem.TD_R_RearDiameter = _currentData.TD_R_RearDiameter;

            // Copy wheel centers (already in mm)
            if (_currentData.WheelFL.x != 0 || _currentData.WheelFL.y != 0 || _currentData.WheelFL.z != 0)
            {
                measurementSystem.WheelFL = _currentData.WheelFL.ToVector3();
                measurementSystem.WheelFR = _currentData.WheelFR.ToVector3();
                measurementSystem.WheelRL = _currentData.WheelRL.ToVector3();
                measurementSystem.WheelRR = _currentData.WheelRR.ToVector3();
            }

            // Create lresults from saved data for dimension lines
            measurementSystem.SetResultsFromSavedData(_currentData);

            dimensionLines.measurementSystem = measurementSystem;
            // Link dimension lines if available
            if (dimensionLines != null)
            {

                dimensionLines.ClearAll();
                dimensionLines.HideAll();
                //  Debug.LogWarning("Name " + dimensionLines.gameObject.name);
            }
            else
            {
                Debug.LogError("DimensionLines Missing");
            }
        }

        /// Build Results from saved data (mm -> meters for Results) and refresh lines.
        /// Safe to call multiple times. Requires: measurementSystem + dimensionLines + _currentData.
        private void ApplySavedDataForDimensionLines(string contextTag)
        {
            if (_currentData == null || measurementSystem == null || dimensionLines == null) return;

            // Populate a valid Results from saved mm values (does unit conversion + bounds fixups)
            measurementSystem.SetResultsFromSavedData(_currentData); // meters in Results, analyzed=true
                                                                     // (See VehicleMeasurementSystem.SetResultsFromSavedData)  // <-- your function
                                                                     // This also sets WheelFL/FR/RL/RR (mm) on the system for the renderer to use. 
                                                                     // It reconstructs bounds if saved bounds were 0.                     

            // Link and draw
            dimensionLines.measurementSystem = measurementSystem;
            // dimensionLines.ShowAll();
            dimensionLines.RefreshAll();

            Debug.Log($"[Controller] Dimension lines applied from saved data ({contextTag}). Results != null: {measurementSystem.Results != null}");
        }

        #endregion

        #region Model Picker

        private void SetupSearchAndFilters()
        {
            // Setup search input
            if (searchInput != null)
            {
                searchInput.onValueChanged.AddListener(OnSearchChanged);
            }

            // Setup manufacturer filter dropdown
            if (manufacturerFilterDropdown != null)
            {
                manufacturerFilterDropdown.onValueChanged.AddListener(OnManufacturerFilterChanged);
            }

            // Setup clear button
            if (clearFiltersButton != null)
            {
                clearFiltersButton.onClick.AddListener(ClearFilters);
            }
        }

        private void OnSearchChanged(string query)
        {
            _searchQuery = query.Trim().ToLower();
            RefreshModelList();
        }

        private void OnManufacturerFilterChanged(int index)
        {
            if (manufacturerFilterDropdown != null && index < manufacturerFilterDropdown.options.Count)
            {
                string selected = manufacturerFilterDropdown.options[index].text;
                _selectedManufacturer = (selected == "All Manufacturers") ? "" : selected;
                RefreshModelList();
            }
        }

        private void ClearFilters()
        {
            _searchQuery = "";
            _selectedManufacturer = "";

            if (searchInput != null)
                searchInput.text = "";

            if (manufacturerFilterDropdown != null)
                manufacturerFilterDropdown.value = 0; // "All Manufacturers"

            RefreshModelList();
        }


        private void RefreshModelList()
        {
            if (modelListContainer == null) return;

            // Clear existing display
            foreach (Transform child in modelListContainer) Destroy(child.gameObject);

            // Filter vehicles
            var filteredVehicles = _allVehicles.Where(v => PassesFilters(v)).ToList();

            // Create list items
            foreach (var vehicle in filteredVehicles)
            {
                CreateModelListItem(
                    vehicle.displayName,
                    vehicle.path,
                    vehicle.loadType,
                    vehicle.modelYear,
                    vehicle.sizeInfo,
                    vehicle.vehicleId,
                    vehicle.manufacturer,
                    vehicle.isDownloaded,
                    vehicle.hasUpdateAvailable
                );
            }

            // Update results count
            UpdateResultsCount(filteredVehicles.Count, _allVehicles.Count);
        }

        private bool PassesFilters(VehicleListItem vehicle)
        {
            // Check search query (search in name, manufacturer, category)
            if (!string.IsNullOrEmpty(_searchQuery))
            {
                string searchLower = _searchQuery.ToLower();
                string nameLower = vehicle.displayName?.ToLower() ?? "";
                string manufacturerLower = vehicle.manufacturer?.ToLower() ?? "";
                string categoryLower = vehicle.category?.ToLower() ?? "";

                bool matchesSearch = nameLower.Contains(searchLower) ||
                                   manufacturerLower.Contains(searchLower) ||
                                   categoryLower.Contains(searchLower);

                if (!matchesSearch) return false;
            }

            // Check manufacturer filter
            if (!string.IsNullOrEmpty(_selectedManufacturer))
            {
                if (string.IsNullOrEmpty(vehicle.manufacturer) ||
                    !vehicle.manufacturer.Equals(_selectedManufacturer, System.StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateResultsCount(int filtered, int total)
        {
            if (resultsCountText != null)
            {
                if (filtered == total)
                {
                    resultsCountText.text = $"Showing {total} vehicle{(total != 1 ? "s" : "")}";
                }
                else
                {
                    resultsCountText.text = $"Showing {filtered} of {total} vehicles";
                }
            }
        }



        private void PopulateManufacturerDropdown()
        {
            if (manufacturerFilterDropdown == null) return;

            // Get unique manufacturers
            var manufacturers = _allVehicles
                .Select(v => v.manufacturer)
                .Where(m => !string.IsNullOrEmpty(m))
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            // Clear and populate dropdown
            manufacturerFilterDropdown.ClearOptions();

            var options = new System.Collections.Generic.List<string> { "All Manufacturers" };
            options.AddRange(manufacturers);

            manufacturerFilterDropdown.AddOptions(options);
            manufacturerFilterDropdown.value = 0;
        }


        private void ShowModelPicker()
        {
            if (modelPickerPanel != null)
                modelPickerPanel.SetActive(true);

            var remote = RemoteAddressableVehicleLoader.Instance;
            if (useAddressablesForBenchmark && remote != null && !remote.IsCatalogLoaded)
            {
                SetText(statusText, "Loading vehicle list…"); // optional
                StartCoroutine(WaitForCatalogThenPopulate());
                return;
            }

            PopulateModelList();
        }

        private IEnumerator WaitForCatalogThenPopulate()
        {
            var remote = RemoteAddressableVehicleLoader.Instance;
            // Safety timeout (e.g., 10s) to avoid waiting forever if offline
            float timeout = 10f;
            float t = 0f;
            while (remote != null && !remote.IsCatalogLoaded && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            // If still not loaded, we can fall back to AddressableVehicleLoader / Resources
            PopulateModelList();
        }


        private void HideModelPicker()
        {
            if (modelPickerPanel != null)
                modelPickerPanel.SetActive(false);
        }


        private void PopulateModelList()
        {
            if (modelListContainer == null || modelListItemPrefab == null)
                return;

            // Clear UI
            foreach (Transform child in modelListContainer)
                Destroy(child.gameObject);

            _allVehicles.Clear();

            // ────────────────────────────────────────────
            // REMOTE VEHICLES (Addressables)
            // ────────────────────────────────────────────
            var remoteLoader = RemoteAddressableVehicleLoader.Instance;

            if (useAddressablesForBenchmark &&
                remoteLoader != null &&
                remoteLoader.IsCatalogLoaded)
            {
                foreach (var vehicle in remoteLoader.GetAvailableVehicles())
                {
                    bool isDownloaded = DownloadedVehiclesTracker.TryGetDownloaded(
                        vehicle.vehicleId,
                        out var localVersion,
                        out _
                    );

                    bool hasUpdateAvailable =
                        isDownloaded &&
                        (string.IsNullOrEmpty(localVersion) || localVersion != vehicle.version);

                    _allVehicles.Add(new VehicleListItem
                    {
                        displayName = vehicle.vehicleName,
                        path = vehicle.addressableKey,
                        loadType = ModelLoadType.Addressables,
                        vehicleId = vehicle.vehicleId,
                        manufacturer = vehicle.manufacturer ?? "Unknown",
                        category = vehicle.category ?? "",
                        sizeInfo = hasUpdateAvailable
                            ? "Update available"
                            : isDownloaded ? "Downloaded" : vehicle.approximateSize,

                        isDownloaded = isDownloaded,
                        hasUpdateAvailable = hasUpdateAvailable   // ✅ STORE IT
                    });
                }
            }

            // ────────────────────────────────────────────
            // RESOURCES (Local models, always available)
            // ────────────────────────────────────────────
            foreach (var model in Resources.LoadAll<GameObject>(modelsResourcePath))
            {
                var prefabData =
                    model.GetComponent<VehiclePrefabData>() ??
                    model.GetComponentInChildren<VehiclePrefabData>();

                _allVehicles.Add(new VehicleListItem
                {
                    displayName = model.name,
                    path = $"{modelsResourcePath}/{model.name}",
                    loadType = ModelLoadType.Resources,
                    sizeInfo = "Local",
                    modelYear = prefabData != null ? prefabData.modelYear : "",
                    vehicleId = null,
                    manufacturer = "Local",
                    category = "",
                    isDownloaded = true
                });
            }

            Resources.UnloadUnusedAssets();

            // ────────────────────────────────────────────
            // UI UPDATES
            // ────────────────────────────────────────────
            if (totalVehiclesText != null)
            {
                totalVehiclesText.text = $"Loaded: {_allVehicles.Count} Vehicles";
            }

            PopulateManufacturerDropdown();
            RefreshModelList();
        }

        private void CancelButtonClickOnImport()
        {
            _dataManager.GoToHome();
        }



        private void CreateModelListItem(string displayName, string path, ModelLoadType loadType, string modelYear,
                                         string sizeInfo, string addressableId, string manufacturer, bool isDownloaded, bool hasUpdateAvailable)
        {

            var item = Instantiate(modelListItemPrefab, modelListContainer);


            Image thumbnailImage = null;      // Icon/Vehicle (thumbnail)
            Image downloadIconImage = null;   // Icon/Image (download icon)

            var images = item.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                var n = img.gameObject.name.ToLower();

                if (n == "vehicle")
                    thumbnailImage = img;
                else if (n == "image")
                    downloadIconImage = img;
            }


            // Find text components
            var texts = item.GetComponentsInChildren<TMP_Text>(true);

            TMP_Text nameText = null;
            TMP_Text manufacturerText = null;
            TMP_Text statusText = null;

            foreach (var t in texts)
            {
                var n = t.gameObject.name.ToLower();

                if (n.Contains("name") || n.Contains("title"))
                    nameText = t;
                else if (n.Contains("manufacturer") || n.Contains("brand"))
                    manufacturerText = t;
                else if (n.Contains("status") || n.Contains("download"))
                    statusText = t;
            }

            // -------------------------
            // Vehicle name
            // -------------------------
            if (nameText != null)
            {
                string displayText = displayName;

                if (loadType == ModelLoadType.Addressables)
                {
                    displayText += !string.IsNullOrEmpty(modelYear)
                        ? $" ({modelYear})"
                        : " (--)";
                }
                else
                {
                    displayText += " [Local]";
                }

                nameText.text = displayText;
            }

            // -------------------------
            // Manufacturer
            // -------------------------
            if (manufacturerText != null && !string.IsNullOrEmpty(manufacturer))
            {
                manufacturerText.text = manufacturer;
                manufacturerText.fontSize = nameText.fontSize * 0.8f;
                manufacturerText.color = new Color(0.7f, 0.7f, 0.7f);
            }
            // -------------------------
            // Thumbnail assignment
            // -------------------------
            if (thumbnailImage != null)
            {
                Sprite thumbnail = null;

                // 1) Try remote catalog thumbnail
                var remoteLoader = RemoteAddressableVehicleLoader.Instance;
                if (remoteLoader != null)
                    thumbnail = remoteLoader.GetThumbnail(addressableId);

                // 2) Fallback: local cached thumbnail
                if (thumbnail == null)
                    thumbnail = VehicleMeasurementStorage.LoadThumbnail(addressableId);

                if (thumbnail != null)
                {
                    thumbnailImage.sprite = thumbnail;
                    thumbnailImage.color = Color.white;
                    thumbnailImage.preserveAspect = true;
                }
            }


            // -------------------------
            // Download status (⭐ NEW ⭐)
            // -------------------------
            if (statusText != null)
            {
                if (hasUpdateAvailable)
                {
                    statusText.text = "Update available";
                    statusText.color = new Color(1f, 0.65f, 0f); // orange
                }
                else if (isDownloaded)
                {
                    statusText.text = "";
                    statusText.color = Color.green;
                }
                else
                {
                    statusText.text = "Download";
                    statusText.color = Color.white;
                }

                statusText.gameObject.SetActive(true);
            }

            if (downloadIconImage != null)
            {
                // Show download icon ONLY if:
                // - Not downloaded OR
                // - Update is available
                downloadIconImage.gameObject.SetActive(!isDownloaded || hasUpdateAvailable);
            }


            // -------------------------
            // Button behavior
            // -------------------------
            var button = item.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() =>
                {
                    OnModelSelectedFromList(displayName, path, loadType);
                });
            }

        }

        private void OnModelSelectedFromList(string displayName, string path, ModelLoadType loadType, Sprite thumbnail = null, string thumbnailUrl = null)
        {
            HideModelPicker();

            // Track model source for saving
            _currentModelPath = path;
            _currentModelLoadType = loadType;

            // Track addressable ID (important for vehicle ID generation)
            if (loadType == ModelLoadType.Addressables)
            {
                _currentAddressableId = path; // The path IS the addressable key
            }
            else
            {
                _currentAddressableId = null;
            }

            // Track thumbnail for saving
            _currentThumbnail = thumbnail;
            _currentThumbnailUrl = thumbnailUrl;

            // Generate vehicle ID BEFORE loading (so we can check server)
            _currentVehicleId = GenerateVehicleId();

            // Set vehicle name in UI
            SetText(vehicleNameText, displayName);
            if (vehicleNameInput != null)
                vehicleNameInput.text = displayName;

            // Check server for existing measurements FIRST
            CheckServerForExistingMeasurements(_currentVehicleId, displayName, () =>
            {
                // THEN load the 3D model
                LoadModel(path, loadType);
            });
        }


        /*    private void OnModelSelectedFromList(string displayName, string path, ModelLoadType loadType, Sprite thumbnail = null, string thumbnailUrl = null)
            {
                HideModelPicker();

                // Track model source for saving
                _currentModelPath = path;
                _currentModelLoadType = loadType;

                // Track thumbnail for saving
                _currentThumbnail = thumbnail;
                _currentThumbnailUrl = thumbnailUrl;

                LoadModel(path, loadType);

                SetText(vehicleNameText, displayName);
                if (vehicleNameInput != null)
                    vehicleNameInput.text = displayName;
            }*/
        private void CheckServerForExistingMeasurements(string vehicleId, string displayName, System.Action onComplete)
        {
            SetText(statusText, "Checking for existing measurements...");

            ControlledMeasurementStorage.Instance.CheckAndLoad(vehicleId, (result) =>
            {
                if (result.Found)
                {
                    // Measurements exist on server/local - load them!
                    _currentData = result.Data;
                    _dataSource = result.Source;
                    _isReadOnly = result.IsReadOnly;

                    if (result.Source == MeasurementSource.Server && result.Data != null)
                    {
                        CacheServerDataLocallyIfNeeded(vehicleId, result.Data);
                    }

                    // Update vehicle name from saved data (in case it differs)
                    if (!string.IsNullOrEmpty(_currentData.vehicleName))
                    {
                        SetText(vehicleNameText, _currentData.vehicleName);
                        if (vehicleNameInput != null)
                            vehicleNameInput.text = _currentData.vehicleName;
                    }

                    // Show status
                    string sourceText = result.Source == MeasurementSource.Server ? "📡 Server" : "💾 Local";
                    string readOnlyText = result.IsReadOnly ? " (Read-Only)" : "";
                    SetText(statusText, $"Found measurements from {sourceText}{readOnlyText}");

                    // Handle read-only state
                    if (_isReadOnly)
                    {
                        //  DisableMeasurementEditing();
                    }
                    else
                    {
                        // EnableMeasurementEditing();
                    }

                    Debug.Log($"[MeasurementController] Found existing measurements for {vehicleId} from {result.Source}");
                }
                else
                {
                    // No measurements exist - ready for new measurement
                    _currentData = null;
                    _dataSource = MeasurementSource.None;
                    _isReadOnly = false;

                    // EnableMeasurementEditing();
                    SetText(statusText, "Ready for measurement");

                    Debug.Log($"[MeasurementController] No existing measurements for {vehicleId}");
                }

                // Continue with model loading
                onComplete?.Invoke();
            });
        }


        #endregion

        #region Model Loading

        private void LoadModel(string path, ModelLoadType loadType)
        {
            // Clear any existing models first
            ClearExistingModels();

            // Track model source
            _currentModelPath = path;
            _currentModelLoadType = loadType;

            ShowLoading(true, "Loading model...");

            switch (loadType)
            {
                case ModelLoadType.Resources:
                    StartCoroutine(LoadFromResources(path));
                    break;

                case ModelLoadType.Addressables:
                    LoadFromAddressables(path);
                    break;

                default:
                    ShowLoading(false);
                    SetText(statusText, $"Unsupported load type: {loadType}");
                    break;
            }
        }

        /// <summary>
        /// Clears all existing models from the model container
        /// </summary>
        private void ClearExistingModels()
        {
            // Unload via Addressables if that was used
            if (addressableLoader != null && addressableLoader.GetCurrentVehicle() != null)
            {
                addressableLoader.UnloadCurrentVehicle();
            }

            // Destroy tracked loaded model
            if (_loadedModel != null)
            {
                Destroy(_loadedModel);
                _loadedModel = null;
            }

            // Also destroy any children in the model container (default models, etc.)
            if (modelContainer != null)
            {
                for (int i = modelContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(modelContainer.GetChild(i).gameObject);
                }
            }

            // Clear measurement system reference
            measurementSystem = null;

            // Clear dimension lines (safe call)
            if (dimensionLines != null)
            {
                dimensionLines.HideAll();
                dimensionLines.measurementSystem = null;
            }

            // Clear manual references for new model
            manualModelRoot = null;
            manualTyresMesh = null;
        }

        #region Resources Loading

        private IEnumerator LoadFromResources(string path)
        {
            var request = Resources.LoadAsync<GameObject>(path);

            while (!request.isDone)
            {
                if (loadingProgress != null)
                    loadingProgress.value = request.progress;
                yield return null;
            }

            if (request.asset != null)
            {
                var prefab = request.asset as GameObject;
                _loadedModel = Instantiate(prefab, modelContainer);
                _loadedModel.name = prefab.name;

                SetupMeasurementSystem(_loadedModel);
                SetText(statusText, "Model loaded - Ready to analyze");
                ApplySavedDataForDimensionLines("ResourcesLoaded");
                if (modelInspector != null)
                {
                    modelInspector.InspectModel(_loadedModel);
                }
                if (GuideController.Instance != null)
                {
                    GuideController.ShowGuides();
                }
            }
            else
            {
                SetText(statusText, $"Failed to load: {path}");
            }

            ShowLoading(false);
        }

        #endregion

        #region Addressables Loading


        private void LoadFromAddressables(string addressableKey)
        {
            if (remoteLoader == null)
                remoteLoader = RemoteAddressableVehicleLoader.Instance;

            if (remoteLoader == null)
            {
                SetText(statusText, "Remote loader not found!");
                ShowLoading(false);
                return;
            }
            SetupRemoteAddressableListeners();
            Debug.Log($"[MC] Loading via RemoteAddressableVehicleLoader key='{addressableKey}'");

            // PRINT the actual bundle/file path Addressables will use (keeps our [ADDR] log)
            DebugLogLocationsForKey(addressableKey);

            ShowLoading(true, "Loading model...");
            remoteLoader.LoadVehicle(
                addressableKey,
                onComplete: OnAddressableVehicleLoaded,
                onError: OnAddressableLoadError,
                container: modelContainer
            );
        }


        private void SetupAddressableListeners()
        {
            if (addressableLoader == null) return;

            // Remove old listeners first (with null checks for events)
            if (addressableLoader.OnDownloadProgress != null)
                addressableLoader.OnDownloadProgress.RemoveListener(OnAddressableProgress);
            if (addressableLoader.OnDownloadStarted != null)
                addressableLoader.OnDownloadStarted.RemoveListener(OnAddressableDownloadStarted);
            if (addressableLoader.OnDownloadCompleted != null)
                addressableLoader.OnDownloadCompleted.RemoveListener(OnAddressableDownloadCompleted);

            // Add new listeners (with null checks for events)
            if (addressableLoader.OnDownloadProgress != null)
                addressableLoader.OnDownloadProgress.AddListener(OnAddressableProgress);
            if (addressableLoader.OnDownloadStarted != null)
                addressableLoader.OnDownloadStarted.AddListener(OnAddressableDownloadStarted);
            if (addressableLoader.OnDownloadCompleted != null)
                addressableLoader.OnDownloadCompleted.AddListener(OnAddressableDownloadCompleted);
        }
        // MeasurementController.cs

        private void SetupRemoteAddressableListeners()
        {
            if (remoteLoader == null) return;

            // Remove old listeners first to avoid duplicates on subsequent loads
            if (remoteLoader.OnDownloadProgress != null)
                remoteLoader.OnDownloadProgress.RemoveListener(OnAddressableProgress);

            if (remoteLoader.OnDownloadStarted != null)
                remoteLoader.OnDownloadStarted.RemoveListener(OnAddressableDownloadStarted);

            if (remoteLoader.OnDownloadCompleted != null)
                remoteLoader.OnDownloadCompleted.RemoveListener(OnAddressableDownloadCompleted);

            // Add the handlers you already use for UI updates
            if (remoteLoader.OnDownloadProgress != null)
                remoteLoader.OnDownloadProgress.AddListener(OnAddressableProgress);

            if (remoteLoader.OnDownloadStarted != null)
                remoteLoader.OnDownloadStarted.AddListener(OnAddressableDownloadStarted);

            if (remoteLoader.OnDownloadCompleted != null)
                remoteLoader.OnDownloadCompleted.AddListener(OnAddressableDownloadCompleted);
        }

        private void CleanupRemoteAddressableListeners()
        {
            if (remoteLoader == null) return;

            if (remoteLoader.OnDownloadProgress != null)
                remoteLoader.OnDownloadProgress.RemoveListener(OnAddressableProgress);

            if (remoteLoader.OnDownloadStarted != null)
                remoteLoader.OnDownloadStarted.RemoveListener(OnAddressableDownloadStarted);

            if (remoteLoader.OnDownloadCompleted != null)
                remoteLoader.OnDownloadCompleted.RemoveListener(OnAddressableDownloadCompleted);
        }
        private void OnAddressableDownloadStarted(string vehicleId)
        {
            ShowLoading(true, "Loading vehicle...");
            if (loadingProgress != null) loadingProgress.value = 0;
        }

        // MeasurementController.cs

        private void OnAddressableProgress(float progress, long downloaded, long total)
        {
            if (loadingProgress != null)
                loadingProgress.value = progress;

            // Prefer the remote loader for formatting and speed source
            var remote = remoteLoader ?? RemoteAddressableVehicleLoader.Instance;

            // 1) Progress text + bytes
            if (loadingText != null)
            {
                string downloadedStr = remote != null
                    ? RemoteAddressableVehicleLoader.FormatBytes(downloaded)
                    : FormatBytesLocal(downloaded); // fallback if needed

                string totalStr = remote != null
                    ? RemoteAddressableVehicleLoader.FormatBytes(total)
                    : FormatBytesLocal(total);

                loadingText.text = $"Downloading... {progress * 100f:F1}%\n{downloadedStr} / {totalStr}";
            }

            // 2) Speed
            if (downloadSpeedText != null && remote != null)
            {
                float speedBytesPerSec = remote.GetCurrentDownloadSpeed(); // bytes/sec (from Remote loader)
                if (speedBytesPerSec > 0f)
                    downloadSpeedText.text = FormatSpeedLocal(speedBytesPerSec);
            }

            // 3) ETA
            if (downloadETAText != null && remote != null)
            {
                float speedBytesPerSec = remote.GetCurrentDownloadSpeed();
                if (speedBytesPerSec > 0f)
                {
                    long remaining = total - downloaded;
                    downloadETAText.text = FormatETALocal(remaining, speedBytesPerSec);
                }
            }
        }

        private void OnAddressableDownloadCompleted(string vehicleId)
        {
            if (loadingText != null)
                loadingText.text = "Download complete! Loading model...";
            if (loadingProgress != null)
                loadingProgress.value = 1f;
        }
        private void OnAddressableVehicleLoaded(GameObject vehicle)
        {
            _loadedModel = vehicle;

            SetupMeasurementSystem(vehicle);

            SetText(statusText, "Model loaded - Ready to analyze");

            // Don't overwrite vehicle name if already set from OnModelSelectedFromList
            string currentName = vehicleNameInput != null ? vehicleNameInput.text :
                                 (vehicleNameText != null ? vehicleNameText.text : "");

            if (string.IsNullOrEmpty(currentName) || currentName == "New Vehicle")
            {
                string vehicleName = vehicle.name.Replace("(Clone)", "").Trim();
                SetText(vehicleNameText, vehicleName);
                if (vehicleNameInput != null)
                    vehicleNameInput.text = vehicleName;
            }
            if (modelInspector != null)
            {
                modelInspector.InspectModel(_loadedModel);
            }
            if (GuideController.Instance != null)
            {
                GuideController.ShowGuides();
            }
            ShowLoading(false);
            CleanupAddressableListeners();

            // ═══ NEW: Apply saved measurements if they exist ═══
            if (_currentData != null)
            {
                // Apply saved data to measurement system (for dimension lines)
                SetupMeasurementSystemFromSavedData();

                // Refresh UI display
                RefreshMeasurementDisplay();

                // Update status
                string sourceText = _dataSource == MeasurementSource.Server ? " Server" : " Local";
                string readOnlyText = _isReadOnly ? " (Read-Only)" : "";
                SetText(statusText, $"Model loaded - Measurements from {sourceText}{readOnlyText}");
            }
            else
            {
                SetText(statusText, "Model loaded - Ready to analyze");
            }
            ApplySavedDataForDimensionLines("AddressableLoaded");
            if (_currentData != null)
            {
                StartCoroutine(RefreshDimensionLinesDelayed());
            }

            if (addressableLoader != null)
            {
                var allVehicles = addressableLoader.GetAvailableVehicles();
                Debug.Log($"═══════════════════════════════════════════════");
                Debug.Log($"[DEBUG] Catalog has {allVehicles.Count} vehicles");
                Debug.Log($"[DEBUG] Looking for: '{_currentModelPath}'");
                Debug.Log($"═══════════════════════════════════════════════");

                if (allVehicles.Count == 0)
                {
                    Debug.LogWarning("[DEBUG] ⚠ CATALOG IS EMPTY! Add vehicles in Inspector!");
                }
                else
                {
                    for (int i = 0; i < allVehicles.Count; i++)
                    {
                        var v = allVehicles[i];
                        Debug.Log($"[DEBUG] [{i}] vehicleId='{v.vehicleId}' | addressableKey='{v.addressableKey}' | name='{v.vehicleName}' | hasThumbnail={v.thumbnail != null}");
                    }
                }
                Debug.Log($"═══════════════════════════════════════════════");
            }

            if (!string.IsNullOrEmpty(_currentModelPath))
            {
                bool tracked = false;

                // TRY REMOTE LOADER FIRST (access via Instance)
                var remoteLoader = RemoteAddressableVehicleLoader.Instance;

                if (remoteLoader != null)
                {
                    Debug.Log("[MeasurementController] Using RemoteVehicleLoader (singleton)");

                    var remoteVehicles = remoteLoader.GetAvailableVehicles();
                    Debug.Log($"[MeasurementController] RemoteLoader has {remoteVehicles.Count} vehicles");

                    if (remoteVehicles.Count > 0)
                    {
                        // Try exact match
                        var vehicleInfo = remoteVehicles.Find(v =>
                            v.vehicleId == _currentModelPath ||
                            v.addressableKey == _currentModelPath);

                        // Try case-insensitive
                        if (vehicleInfo == null)
                        {
                            vehicleInfo = remoteVehicles.Find(v =>
                                (v.vehicleId != null && v.vehicleId.Equals(_currentModelPath, System.StringComparison.OrdinalIgnoreCase)) ||
                                (v.addressableKey != null && v.addressableKey.Equals(_currentModelPath, System.StringComparison.OrdinalIgnoreCase)));
                        }

                        // Try partial match
                        if (vehicleInfo == null)
                        {
                            vehicleInfo = remoteVehicles.Find(v =>
                                (v.vehicleId != null && (v.vehicleId.Contains(_currentModelPath) || _currentModelPath.Contains(v.vehicleId))) ||
                                (v.addressableKey != null && (v.addressableKey.Contains(_currentModelPath) || _currentModelPath.Contains(v.addressableKey))));
                        }

                        if (vehicleInfo != null)
                        {
                            // ✓ FOUND IN REMOTE CATALOG
                            Debug.Log($"[MeasurementController] ✓ Found in REMOTE catalog: {vehicleInfo.vehicleName}");

                            // Track it
                            DownloadedVehiclesTracker.MarkAsDownloaded(vehicleInfo);

                            // Download thumbnail from URL if available
                            if (!string.IsNullOrEmpty(vehicleInfo.thumbnailUrl))
                            {
                                StartCoroutine(DownloadAndSaveThumbnail(vehicleInfo.vehicleId, vehicleInfo.thumbnailUrl));
                            }

                            tracked = true;
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[MeasurementController] RemoteLoader catalog is empty or not loaded yet");
                    }
                }
                else
                {
                    Debug.LogWarning("[MeasurementController] RemoteVehicleLoader.Instance is null");
                }

                // TRY LOCAL LOADER IF REMOTE DIDN'T WORK
                if (!tracked && addressableLoader != null)
                {
                    Debug.Log("[MeasurementController] Trying AddressableVehicleLoader (local)");

                    var localVehicles = addressableLoader.GetAvailableVehicles();
                    Debug.Log($"[MeasurementController] AddressableLoader has {localVehicles.Count} vehicles");

                    if (localVehicles.Count > 0)
                    {
                        var vehicleInfo = localVehicles.Find(v =>
                            v.vehicleId == _currentModelPath ||
                            v.addressableKey == _currentModelPath);

                        if (vehicleInfo == null)
                        {
                            vehicleInfo = localVehicles.Find(v =>
                                (v.vehicleId != null && v.vehicleId.Equals(_currentModelPath, System.StringComparison.OrdinalIgnoreCase)) ||
                                (v.addressableKey != null && v.addressableKey.Equals(_currentModelPath, System.StringComparison.OrdinalIgnoreCase)));
                        }

                        if (vehicleInfo != null)
                        {
                            // ✓ FOUND IN LOCAL CATALOG
                            Debug.Log($"[MeasurementController] ✓ Found in LOCAL catalog: {vehicleInfo.vehicleName}");

                            // Save thumbnail from sprite
                            if (vehicleInfo.thumbnail != null)
                            {
                                string thumbnailPath = SaveThumbnailFromSprite(vehicleInfo.vehicleId, vehicleInfo.thumbnail);
                                if (!string.IsNullOrEmpty(thumbnailPath))
                                {
                                    DownloadedVehiclesTracker.SetThumbnailPath(vehicleInfo.vehicleId, thumbnailPath);
                                }
                            }

                            DownloadedVehiclesTracker.MarkAsDownloaded(vehicleInfo);
                            tracked = true;
                        }
                    }
                }

                // FALLBACK IF NOT FOUND IN EITHER
                if (!tracked)
                {
                    Debug.LogWarning($"[MeasurementController] Not found in either catalog, creating fallback for: {_currentModelPath}");

                    string fallbackName = _loadedModel != null ?
                        _loadedModel.name.Replace("(Clone)", "").Trim() :
                        _currentModelPath;

                    // Check which loader is active and use appropriate type
                    if (remoteLoader != null)
                    {
                        var fallbackInfo = new RemoteVehicleInfo
                        {
                            vehicleId = _currentModelPath,
                            vehicleName = fallbackName,
                            addressableKey = _currentModelPath,
                            manufacturer = "Unknown",
                            category = "Vehicle",

                            hasVALData = true
                        };

                        DownloadedVehiclesTracker.MarkAsDownloaded(fallbackInfo);
                    }
                    else if (addressableLoader != null)
                    {
                        var fallbackInfo = new VehicleAddressableInfo
                        {
                            vehicleId = _currentModelPath,
                            vehicleName = fallbackName,
                            addressableKey = _currentModelPath,
                            manufacturer = "Unknown",
                            category = "Vehicle",
                            hasVALData = true
                        };

                        DownloadedVehiclesTracker.MarkAsDownloaded(fallbackInfo);
                    }

                    Debug.Log($"[MeasurementController] Created fallback entry: {fallbackName}");
                }
            }

        }


        private IEnumerator DownloadAndSaveThumbnail(string vehicleId, string thumbnailUrl)
        {
            Debug.Log($"[MeasurementController] Downloading thumbnail from: {thumbnailUrl}");

            using (UnityEngine.Networking.UnityWebRequest request = UnityEngine.Networking.UnityWebRequestTexture.GetTexture(thumbnailUrl))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    Texture2D texture = UnityEngine.Networking.DownloadHandlerTexture.GetContent(request);

                    if (texture != null)
                    {
                        // Save as PNG
                        string thumbnailPath = VehicleMeasurementStorage.GetThumbnailPath(vehicleId);
                        byte[] bytes = texture.EncodeToPNG();
                        System.IO.File.WriteAllBytes(thumbnailPath, bytes);

                        // Update tracker with path
                        DownloadedVehiclesTracker.SetThumbnailPath(vehicleId, thumbnailPath);

                        Debug.Log($"[MeasurementController] ✓ Downloaded and saved thumbnail: {thumbnailPath}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[MeasurementController] Failed to download thumbnail: {request.error}");
                }
            }
        }

        /* private void OnAddressableVehicleLoaded(GameObject vehicle)
         {
             _loadedModel = vehicle;

             SetupMeasurementSystem(vehicle);
             SetText(statusText, "Model loaded • Ready to analyze");
             ApplySavedDataForDimensionLines("AddressableLoaded");
             // Don't overwrite vehicle name if already set from OnModelSelectedFromList
             // Only set if still empty or showing default
             string currentName = vehicleNameInput != null ? vehicleNameInput.text :
                                  (vehicleNameText != null ? vehicleNameText.text : "");

             if (string.IsNullOrEmpty(currentName) || currentName == "New Vehicle")
             {
                 // Remove (Clone) suffix from instantiated name
                 string vehicleName = vehicle.name.Replace("(Clone)", "").Trim();
                 SetText(vehicleNameText, vehicleName);
                 if (vehicleNameInput != null)
                     vehicleNameInput.text = vehicleName;
             }

             ShowLoading(false);

             // Cleanup listeners
             CleanupAddressableListeners();
             if (_currentData != null)
             {
                 StartCoroutine(RefreshDimensionLinesDelayed());
             }
         }*/
        private IEnumerator RefreshDimensionLinesDelayed()
        {
            yield return null;
            if (dimensionLines != null && measurementSystem != null)
            {
                dimensionLines.measurementSystem = measurementSystem;
                dimensionLines.RefreshAll();
            }
        }

        private void OnAddressableLoadError(string error)
        {
            SetText(statusText, $"Failed to load: {error}");
            ShowLoading(false);
            CleanupAddressableListeners();
        }

        private void CleanupAddressableListeners()
        {
            if (addressableLoader == null) return;

            if (addressableLoader.OnDownloadProgress != null)
                addressableLoader.OnDownloadProgress.RemoveListener(OnAddressableProgress);
            if (addressableLoader.OnDownloadStarted != null)
                addressableLoader.OnDownloadStarted.RemoveListener(OnAddressableDownloadStarted);
            if (addressableLoader.OnDownloadCompleted != null)
                addressableLoader.OnDownloadCompleted.RemoveListener(OnAddressableDownloadCompleted);
        }

        #endregion
        [SerializeField] private GameObject _driverViewDropdown;
        private bool _currentHasVALData = true;
        private void SetupMeasurementSystem(GameObject model)
        {
            // Initialize clip section mode if available
            if (clipSectionMode != null)
            {
                clipSectionMode.Initialize(model);
            }

            // Check for VehiclePrefabData component first
            var prefabData = model.GetComponent<VehiclePrefabData>();
            if (prefabData == null)
                prefabData = model.GetComponentInChildren<VehiclePrefabData>();

            if (prefabData != null)
            {
                _currentHasVALData = prefabData.hasVALData;
                Debug.Log($"[VAL] Captured from prefab: {_currentHasVALData}");
            }
            else
            {
                _currentHasVALData = true; // Safe fallback
            }

            if (prefabData != null)
            {
                Debug.Log($"[MeasurementController] Found VehiclePrefabData on {model.name}");
                SetupFromPrefabData(model, prefabData);

                //  _currentHasVALData = prefabData.hasVALData;


                if (prefabData.refSGRP != null)
                {
                    // Inform the camera controller about the anchor as soon as you have it.
                    _orbitCam.SetDriverAnchor(prefabData.refSGRP, applyCurrentPreset: false, snap: false);

                    // Your existing button handler can keep calling SetDriverView if you want,
                    // but now you can also switch presets via dropdown without warnings.
                    driverView.interactable = true;
                    _driverViewDropdown.SetActive(true);
                    driverView?.onClick?.AddListener(() => _orbitCam?.SetDriverView(prefabData.refSGRP));
                }
                else
                {
                    driverView.interactable = false;
                    _driverViewDropdown.SetActive(false);
                }


                return;
            }

            // if(modelInspector != null) { modelInspector.InspectModel(model); }
            // Fallback to manual/auto detection
            Debug.Log($"[MeasurementController] No VehiclePrefabData found, using auto-detection");
            SetupFromAutoDetection(model);
        }

        /// <summary>
        /// Setup measurement system using VehiclePrefabData
        /// </summary>
        private void SetupFromPrefabData(GameObject model, VehiclePrefabData prefabData)
        {
            Debug.Log($"[MeasurementController] === Setting up from VehiclePrefabData ===");
            Debug.Log($"[MeasurementController] Vehicle: {prefabData.vehicleName}");
            Debug.Log($"[MeasurementController] Units: {(prefabData.unitsAreMillimeters ? "Millimeters" : "Meters")}");
            Debug.Log($"[MeasurementController] Configured: {prefabData.GetValidationStatus()}");

            // Get mesh root
            Transform actualModelRoot = prefabData.GetMeshRoot();
            Debug.Log($"[MeasurementController] Mesh Root: {actualModelRoot.name}");

            // Get or add measurement system
            measurementSystem = actualModelRoot.GetComponent<VehicleMeasurementSystem>();
            if (measurementSystem == null)
            {
                measurementSystem = actualModelRoot.gameObject.AddComponent<VehicleMeasurementSystem>();
                Debug.Log($"[MeasurementController] Created new VehicleMeasurementSystem");
            }
            else
            {
                Debug.Log($"[MeasurementController] Using existing VehicleMeasurementSystem");
            }

            // Set vehicle root
            measurementSystem.vehicleRoot = actualModelRoot;

            // Set tyres mesh from prefab data - THIS IS CRITICAL
            Transform tyresMesh = prefabData.GetTyresMesh();
            if (tyresMesh != null)
            {
                measurementSystem.tyresMesh = tyresMesh;
                Debug.Log($"[MeasurementController] ✓ Tyres mesh set: {tyresMesh.name}");
            }
            else
            {
                Debug.LogWarning("[MeasurementController] ✗ VehiclePrefabData has no tyres mesh!");
                SetText(statusText, "⚠ Tyres mesh not configured in prefab");
            }

            // Apply unit settings
            measurementSystem.unitsAreMillimeters = prefabData.unitsAreMillimeters;
            measurementSystem.minWheelRadius = prefabData.minWheelRadius;
            measurementSystem.maxWheelRadius = prefabData.maxWheelRadius;
            Debug.Log($"[MeasurementController] Units: {(prefabData.unitsAreMillimeters ? "mm" : "m")}, Wheel Radius: {prefabData.minWheelRadius} - {prefabData.maxWheelRadius}");

            // Apply exclusions from bounds
            if (prefabData.excludeFromBounds != null && prefabData.excludeFromBounds.Count > 0)
            {
                measurementSystem.excludeFromBounds = prefabData.excludeFromBounds.ToArray();
                Debug.Log($"[MeasurementController] Excluding {prefabData.excludeFromBounds.Count} objects from bounds");
            }

            // Apply manual wheel positions if configured
            if (prefabData.useManualWheelPositions)
            {
                Debug.Log($"[MeasurementController] Using manual wheel positions");
                if (prefabData.wheelFL != null)
                    measurementSystem.WheelFL = prefabData.wheelFL.position * (prefabData.unitsAreMillimeters ? 1f : 1000f);
                if (prefabData.wheelFR != null)
                    measurementSystem.WheelFR = prefabData.wheelFR.position * (prefabData.unitsAreMillimeters ? 1f : 1000f);
                if (prefabData.wheelRL != null)
                    measurementSystem.WheelRL = prefabData.wheelRL.position * (prefabData.unitsAreMillimeters ? 1f : 1000f);
                if (prefabData.wheelRR != null)
                    measurementSystem.WheelRR = prefabData.wheelRR.position * (prefabData.unitsAreMillimeters ? 1f : 1000f);
            }

            // Apply pre-calculated values if available
            if (prefabData.usePreCalculatedValues && prefabData.preCalculatedValues != null)
            {
                Debug.Log($"[MeasurementController] Applying pre-calculated values");
                ApplyPreCalculatedValues(prefabData.preCalculatedValues);
            }

            // Link dimension lines
            if (dimensionLines != null)
            {
                dimensionLines.measurementSystem = measurementSystem;
                Debug.Log($"[MeasurementController] Linked dimension lines");
            }

            // Set vehicle name
            if (!string.IsNullOrEmpty(prefabData.vehicleName))
            {
                SetText(vehicleNameText, prefabData.vehicleName);
                if (vehicleNameInput != null)
                    vehicleNameInput.text = prefabData.vehicleName;
            }

            Debug.Log($"[MeasurementController] === Setup complete ===");
            SetText(statusText, "Model loaded - Ready to analyze");
        }

        /// <summary>
        /// Apply pre-calculated values from prefab data
        /// </summary>
        private void ApplyPreCalculatedValues(PreCalculatedMeasurements values)
        {
            measurementSystem.L103_OverallLength = values.L103_OverallLength;
            measurementSystem.L101_Wheelbase = values.L101_Wheelbase;
            measurementSystem.L104_FrontOverhang = values.L104_FrontOverhang;
            measurementSystem.L105_RearOverhang = values.L105_RearOverhang;

            measurementSystem.W103_OverallWidth = values.W103_OverallWidth;
            measurementSystem.W144_FrontTrack = values.W144_FrontTrack;
            measurementSystem.W145_RearTrack = values.W145_RearTrack;

            measurementSystem.H100_OverallHeight = values.H100_OverallHeight;
            measurementSystem.H101_GroundClearance = values.H101_GroundClearance;

            measurementSystem.TD_F_FrontDiameter = values.TD_F_FrontDiameter;
            measurementSystem.TD_R_RearDiameter = values.TD_R_RearDiameter;

            measurementSystem.WheelFL = values.WheelFL;
            measurementSystem.WheelFR = values.WheelFR;
            measurementSystem.WheelRL = values.WheelRL;
            measurementSystem.WheelRR = values.WheelRR;

            // Update UI with pre-calculated values
            RefreshMeasurementDisplay();

            SetText(statusText, "Model loaded - Using pre-calculated values");
        }

        /// <summary>
        /// Fallback: Setup using auto-detection (original method)
        /// </summary>
        private void SetupFromAutoDetection(GameObject model)
        {
            // Find the actual model root - priority: manual > auto-detect
            Transform actualModelRoot;
            if (manualModelRoot != null)
            {
                actualModelRoot = manualModelRoot;
                Debug.Log($"[MeasurementController] Using manual model root: {actualModelRoot.name}");
            }
            else
            {
                actualModelRoot = FindActualModelRoot(model.transform);
                Debug.Log($"[MeasurementController] Auto-detected model root: {actualModelRoot.name}");
            }

            // Always get or add a fresh measurement system on the actual model
            measurementSystem = actualModelRoot.GetComponent<VehicleMeasurementSystem>();
            if (measurementSystem == null)
                measurementSystem = actualModelRoot.gameObject.AddComponent<VehicleMeasurementSystem>();

            // Set vehicle root
            measurementSystem.vehicleRoot = actualModelRoot;

            // Find tyres mesh - priority: manual > tag > name
            if (manualTyresMesh != null)
            {
                measurementSystem.tyresMesh = manualTyresMesh;
                Debug.Log($"[MeasurementController] Using manual tyres mesh: {manualTyresMesh.name}");
            }
            else
            {
                measurementSystem.tyresMesh = FindTyresMesh(actualModelRoot);

                if (measurementSystem.tyresMesh == null)
                {
                    Debug.LogWarning("[MeasurementController] Could not auto-find tyres mesh. Tag with 'Tyres' or assign manually.");
                    SetText(statusText, "Model loaded - Tyres mesh not found - assign manually");
                }
                else
                {
                    Debug.Log($"[MeasurementController] Found tyres mesh: {measurementSystem.tyresMesh.name}");
                }
            }

            // Auto-detect units based on model bounds
            AutoDetectUnits(actualModelRoot.gameObject);

            // Link dimension lines
            if (dimensionLines != null)
            {
                dimensionLines.measurementSystem = measurementSystem;
            }
        }

        /// <summary>
        /// Find the actual model root - handles nested hierarchy
        /// Looks for first child with MeshRenderer or MeshFilter
        /// </summary>
        private Transform FindActualModelRoot(Transform container)
        {
            // If container itself has meshes, it's the root
            if (container.GetComponent<MeshFilter>() != null ||
                container.GetComponent<MeshRenderer>() != null)
            {
                return container;
            }

            // Check if any direct child has meshes
            foreach (Transform child in container)
            {
                if (child.GetComponent<MeshFilter>() != null ||
                    child.GetComponent<MeshRenderer>() != null ||
                    child.GetComponentInChildren<MeshFilter>() != null)
                {
                    // This child or its children have meshes - likely the model root
                    return child;
                }
            }

            // If still not found, recurse into first child
            if (container.childCount > 0)
            {
                return FindActualModelRoot(container.GetChild(0));
            }

            // Fallback to container itself
            return container;
        }

        /// <summary>
        /// Auto-detect if model is in meters or millimeters based on bounds size
        /// - If bounds > 100 units = millimeters (car ~4000mm)
        /// - If bounds < 100 units = meters (car ~4m)
        /// </summary>
        private void AutoDetectUnits(GameObject model)
        {
            Bounds bounds = new Bounds(model.transform.position, Vector3.zero);

            // Get all renderers and encapsulate their bounds
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

            // If max dimension > 100, assume millimeters
            // Typical car: ~4000mm length or ~4m length
            if (maxSize > 100f)
            {
                measurementSystem.unitsAreMillimeters = true;
                measurementSystem.minWheelRadius = 250f;  // mm
                measurementSystem.maxWheelRadius = 450f;  // mm
                Debug.Log($"[MeasurementController] Detected MILLIMETERS (bounds: {maxSize:F1})");
            }
            else
            {
                measurementSystem.unitsAreMillimeters = false;
                measurementSystem.minWheelRadius = 0.25f;  // m
                measurementSystem.maxWheelRadius = 0.45f;  // m
                Debug.Log($"[MeasurementController] Detected METERS (bounds: {maxSize:F3})");
            }
        }


        /// <summary>
        /// Find tyres mesh - Priority:
        /// 1. Tag "Tyres" (most reliable)
        /// 2. Name contains tyre/tire/wheel
        /// </summary>
        private Transform FindTyresMesh(Transform parent)
        {
            // PRIORITY 1: Check for "Tyres" tag (most reliable)
            try
            {
                GameObject taggedObject = GameObject.FindGameObjectWithTag("Tyres");
                if (taggedObject != null && taggedObject.transform.IsChildOf(parent))
                {
                    Debug.Log($"[MeasurementController] Found tyres by tag: {taggedObject.name}");
                    return taggedObject.transform;
                }
            }
            catch
            {
                // Tag doesn't exist - that's fine, continue with name search
            }

            // PRIORITY 2: Search by name
            return FindTyresMeshByName(parent);
        }

        /// <summary>
        /// Recursively search for tyres/wheels mesh by name
        /// </summary>
        private Transform FindTyresMeshByName(Transform parent)
        {
            // Common names for tyre meshes
            string[] tyreNames = { "tyre", "tyres", "tire", "tires", "wheel", "wheels", "rim", "rims" };

            // First check direct children
            foreach (Transform child in parent)
            {
                string nameLower = child.name.ToLower();

                foreach (string tyreName in tyreNames)
                {
                    if (nameLower.Contains(tyreName))
                    {
                        // Make sure it has a mesh
                        if (child.GetComponent<MeshFilter>() != null || child.GetComponentInChildren<MeshFilter>() != null)
                        {
                            return child;
                        }
                    }
                }
            }

            // Recursively search children
            foreach (Transform child in parent)
            {
                Transform found = FindTyresMeshByName(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private void ShowLoading(bool show, string message = "")
        {
            if (loadingPanel != null)
            {
                loadingPanel.SetActive(show);
            }

            if (show)
            {
                if (loadingText != null)
                    loadingText.text = message;
                if (loadingProgress != null)
                    loadingProgress.value = 0f;
                if (analysisProgressText != null)
                    analysisProgressText.text = "0%";
            }
        }

        #endregion

        #region Measurement Display

        /// <summary>
        /// Refresh all measurement text fields from saved data
        /// NOTE: Saved data is already in MILLIMETERS - don't convert!
        /// </summary>
        public void RefreshMeasurementDisplay()
        {
            if (_currentData == null)
            {
                ClearAllMeasurements();
                return;
            }

            // Length (saved data is already in mm)
            SetValueDirect(L103_OverallLength, _currentData.L103_OverallLength);
            SetValueDirect(L101_Wheelbase, _currentData.L101_Wheelbase);
            SetValueDirect(L104_FrontOverhang, _currentData.L104_FrontOverhang);
            SetValueDirect(L105_RearOverhang, _currentData.L105_RearOverhang);

            // Width
            SetValueDirect(W103_OverallWidth, _currentData.W103_OverallWidth);
            SetValueDirect(W144_FrontTrack, _currentData.W144_FrontTrack);
            SetValueDirect(W145_RearTrack, _currentData.W145_RearTrack);

            // Height
            SetValueDirect(H100_OverallHeight, _currentData.H100_OverallHeight);
            SetValueDirect(H101_GroundClearance, _currentData.H101_GroundClearance);

            // Wheels
            SetValueDirect(TD_FrontDiameter, _currentData.TD_F_FrontDiameter);
            SetValueDirect(TD_RearDiameter, _currentData.TD_R_RearDiameter);
        }

        /// <summary>
        /// Refresh display from measurement system results (after analyze)
        /// NOTE: System results are in METERS - need to convert to mm!
        /// </summary>
        private void RefreshFromSystem()
        {
            if (measurementSystem?.Results == null)
            {
                ClearAllMeasurements();
                return;
            }

            var r = measurementSystem.Results;

            // Length (convert from meters to mm)
            SetValueFromMeters(L103_OverallLength, r.L103_OverallLength);
            SetValueFromMeters(L101_Wheelbase, r.L101_Wheelbase);
            SetValueFromMeters(L104_FrontOverhang, r.L104_FrontOverhang);
            SetValueFromMeters(L105_RearOverhang, r.L105_RearOverhang);

            // Width
            SetValueFromMeters(W103_OverallWidth, r.W103_OverallWidth);
            SetValueFromMeters(W144_FrontTrack, r.W144_FrontTrackWidth);
            SetValueFromMeters(W145_RearTrack, r.W145_RearTrackWidth);

            // Height
            SetValueFromMeters(H100_OverallHeight, r.H100_OverallHeight);
            SetValueFromMeters(H101_GroundClearance, r.H101_GroundClearance);

            // Wheels
            SetValueFromMeters(TD_FrontDiameter, r.FrontWheelRadius * 2f);
            SetValueFromMeters(TD_RearDiameter, r.RearWheelRadius * 2f);
        }

        /// <summary>
        /// Clear all measurement displays
        /// </summary>
        private void ClearAllMeasurements()
        {
            SetValueDirect(L103_OverallLength, 0);
            SetValueDirect(L101_Wheelbase, 0);
            SetValueDirect(L104_FrontOverhang, 0);
            SetValueDirect(L105_RearOverhang, 0);
            SetValueDirect(W103_OverallWidth, 0);
            SetValueDirect(W144_FrontTrack, 0);
            SetValueDirect(W145_RearTrack, 0);
            SetValueDirect(H100_OverallHeight, 0);
            SetValueDirect(H101_GroundClearance, 0);
            SetValueDirect(TD_FrontDiameter, 0);
            SetValueDirect(TD_RearDiameter, 0);
        }

        /// <summary>
        /// Set value directly (already in mm) - used for saved data
        /// </summary>
        private void SetValueDirect(TMP_Text textField, float valueInMm)
        {
            if (textField == null) return;

            TMP_Text _childText = GetChildText(textField);
            if (_childText == null) return;

            if (valueInMm <= 0)
                _childText.text = emptyValueText;
            else
                _childText.text = $"{valueInMm:F0}";
        }

        /// <summary>
        /// Set value from meters (convert to mm) - used for system results
        /// </summary>
        private void SetValueFromMeters(TMP_Text textField, float valueInMeters)
        {
            if (textField == null) return;

            TMP_Text _childText = GetChildText(textField);
            if (_childText == null) return;

            if (valueInMeters <= 0)
                _childText.text = emptyValueText;
            else
                _childText.text = $"{valueInMeters * 1000f:F0}";
        }

        /// <summary>
        /// Get child TMP_Text from parent (for UI hierarchy pattern)
        /// </summary>
        private TMP_Text GetChildText(TMP_Text _text)
        {
            if (_text == null) return null;

            // If has children, get first child's TMP_Text
            if (_text.transform.childCount > 0)
                return _text.transform.GetChild(0).GetComponent<TMP_Text>();

            // Otherwise return the text itself
            return _text;
        }

        /// <summary>
        /// Helper to set text safely
        /// </summary>
        private void SetText(TMP_Text textField, string value)
        {
            if (textField != null)
                textField.text = value;
        }

        #endregion

        #region Button Handlers

        private void OnBackClick()
        {
            if (!HasMeasurements())
            {
                UnsavedPopup(true);
            }
            else
            {
                GoBackToHome();
            }
        }
        private void GoBackToHome()
        {
            if (_loadedModel != null)
                Destroy(_loadedModel);
            if (modelInspector != null)
                modelInspector.ClearHierarchy();
            _dataManager.GoToHome();
        }
        private bool _fromUnsavedPanel = false;
        private void UnsavedPopup(bool _active)
        {
            _unsavedPanelPopup.SetActive(_active);
            _fromUnsavedPanel = _active;
        }
        // When true, Save will be triggered automatically after analysis completes
        private bool _saveAfterAnalyzeRequested = false;
        private void OnUnsavedSaveClicked()
        {
            // If analysis is already running, don't start another flow
            if (_isAnalyzing)
            {
                Debug.Log("[MeasurementController] Analyze+Save ignored: analysis already running");
                return;
            }

            // If already analyzed, just save immediately
            if (measurementSystem != null && measurementSystem.IsAnalyzed && measurementSystem.Results != null && HasMeasurements())
            {
                OnSaveClick();
                return;
            }

            // Otherwise: analyze first, then save (when analysis completes)
            _saveAfterAnalyzeRequested = true;
            UnsavedPopup(false);
            OnAnalyzeClick();


        }


        private void OnAnalyzeClick()
        {
            if (measurementSystem == null)
            {
                SetText(statusText, "No model loaded");
                return;
            }

            // Prevent double-click during analysis
            if (_isAnalyzing)
            {
                Debug.Log("[MeasurementController] Analysis already in progress");
                return;
            }

            _isAnalyzing = true;

            // Show loading panel with initial message
            ShowLoading(true, "Starting analysis...");

            // Disable analyze button during processing
            if (analyzeButton != null)
                analyzeButton.interactable = false;

            // ═══ USE ASYNC ANALYSIS ═══
            AsyncMeasurement.AnalyzeAsync(
                measurementSystem,

                // Progress callback - updates UI smoothly
                onProgress: (progress, message) =>
                {
                    if (loadingProgress != null)
                        loadingProgress.value = progress;

                    if (loadingText != null)
                        loadingText.text = message;

                    if (analysisProgressText != null)
                        analysisProgressText.text = $"{progress * 100f:F0}%";
                },

                // Complete callback
                onComplete: (results) =>
                {
                    _isAnalyzing = false;
                    ShowLoading(false);

                    // Re-enable analyze button
                    if (analyzeButton != null)
                        analyzeButton.interactable = true;

                    // Update display from system
                    RefreshFromSystem();

                    // Create/update saved data
                    if (_currentData == null)
                    {
                        _currentData = new SavedVehicleMeasurement();

                        if (string.IsNullOrEmpty(_currentVehicleId))
                        {
                            _currentVehicleId = GenerateVehicleId();
                        }
                    }

                    _currentData.CopyFromSystem(measurementSystem);
                    _currentData.vehicleId = _currentVehicleId;
                    _currentData.vehicleName = GetVehicleName();

                    _hasUnsavedChanges = true;
                    SetText(statusText, "Analyzed - Unsaved");

                    Debug.Log("[MeasurementController] Analysis complete!");
                    // If this analysis was triggered by the Unsaved Save flow, save now
                    if (_saveAfterAnalyzeRequested)
                    {
                        _saveAfterAnalyzeRequested = false;
                        OnSaveClick();
                    }
                },

                // Error callback
                onError: (error) =>
                {
                    _isAnalyzing = false;
                    ShowLoading(false);

                    // Re-enable analyze button
                    if (analyzeButton != null)
                        analyzeButton.interactable = true;

                    SetText(statusText, $"Analysis failed: {error}");
                    _saveAfterAnalyzeRequested = false;
                    Debug.LogError($"[MeasurementController] Analysis error: {error}");
                }
            );
            UpdateCompareWithButtonState();
        }

        private void OnSaveClick()
        {


            Debug.Log($"[MeasurementController] OnSaveClick called");
            Debug.Log($"[MeasurementController] _currentData: {(_currentData != null ? "exists" : "null")}");
            Debug.Log($"[MeasurementController] _currentVehicleId: {_currentVehicleId}");
            /*   if (_isReadOnly)
               {
                   SetText(statusText,"This data is read-only and cannot be modified.");
                   return;
               }*/


            if (_currentData == null)
            {
                if (measurementSystem?.Results != null || measurementSystem?.IsAnalyzed == true)
                {
                    _currentData = new SavedVehicleMeasurement();
                    _currentData.CopyFromSystem(measurementSystem);

                    // Generate ID based on model path/addressable key for consistency
                    // This ensures the same vehicle model always overwrites the same file
                    if (string.IsNullOrEmpty(_currentVehicleId))
                    {
                        _currentVehicleId = GenerateVehicleId();
                    }

                    Debug.Log($"[MeasurementController] Created new SavedVehicleMeasurement, ID: {_currentVehicleId}");
                }
                else
                {
                    Debug.LogWarning("[MeasurementController] No measurement results to save");
                    SetText(statusText, "Nothing to save - analyze first");
                    return;
                }
            }

            _currentData.vehicleName = GetVehicleName();
            _currentData.vehicleId = _currentVehicleId;
            _currentData.lastModified = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _currentData.hasVALData = _currentHasVALData;

            Debug.Log($"[MeasurementController] Saving vehicle: {_currentData.vehicleName} (ID: {_currentVehicleId})");

            // Store model source for reloading later
            if (!string.IsNullOrEmpty(_currentModelPath))
            {
                _currentData.SetModelSource(_currentModelPath, _currentModelLoadType, _currentAddressableId);
                CopyManufacturerFromCatalog();
                Debug.Log($"[MeasurementController] Model source: {_currentModelPath} ({_currentModelLoadType})");
            }

            // Save thumbnail
            if (_currentThumbnail != null)
            {
                string thumbnailPath = VehicleMeasurementStorage.SaveThumbnail(_currentVehicleId, _currentThumbnail);
                if (!string.IsNullOrEmpty(thumbnailPath))
                {
                    _currentData.thumbnailPath = thumbnailPath;
                    Debug.Log($"[MeasurementController] Saved thumbnail: {thumbnailPath}");
                }
            }

            // Store thumbnail URL for re-downloading if needed
            if (!string.IsNullOrEmpty(_currentThumbnailUrl))
            {
                _currentData.thumbnailUrl = _currentThumbnailUrl;
            }

            if (string.IsNullOrEmpty(_currentData.savedDate))
                _currentData.savedDate = _currentData.lastModified;

            if (isAdmin)
            {
                ControlledMeasurementStorage.Instance.Save(_currentData, _currentVehicleId, (success, message) =>
                {
                    if (success)
                    {
                        _hasUnsavedChanges = false;
                        SetText(statusText, $"✓ {message}");
                        _dataManager.SetSelectedVehicleId(_currentVehicleId);

                        if (_fromUnsavedPanel)
                        {
                            UnsavedPopup(false);
                            GoBackToHome();
                        }

                    }
                    else
                    {
                        SetText(statusText, $" Save failed: {message}");
                        // ShowPopup($"Save failed: {message}");
                        if (_fromUnsavedPanel)
                        {
                            UnsavedPopup(false);
                        }
                    }
                });
            }
            else
            {
                if (VehicleMeasurementStorage.SaveData(_currentData, _currentVehicleId))
                {
                    _hasUnsavedChanges = false;
                    SetText(statusText, $" Saved - {_currentData.lastModified}");
                    _dataManager.SetSelectedVehicleId(_currentVehicleId);

                    Debug.Log($"[MeasurementController] ✓ Save successful: {_currentData.vehicleName}");

                    if (deleteButton != null)
                        deleteButton.gameObject.SetActive(true);
                    if (_fromUnsavedPanel)
                    {
                        UnsavedPopup(false);
                        GoBackToHome();
                    }
                }
                else
                {
                    Debug.LogError("[MeasurementController] Save failed!");
                    SetText(statusText, "❌ Save failed!");
                    if (_fromUnsavedPanel)
                    {
                        UnsavedPopup(false);

                    }
                }
            }
            UpdateCompareWithButtonState();
        }

        /// <summary>
        /// Generate a consistent vehicle ID based on model source
        /// Same model = same ID = overwrite existing file
        /// </summary>
        private string GenerateVehicleId()
        {
            // Use addressable key if available (most reliable)
            if (!string.IsNullOrEmpty(_currentAddressableId))
            {
                return SanitizeFileName(_currentAddressableId);
            }

            // Use model path for Resources or other sources
            if (!string.IsNullOrEmpty(_currentModelPath))
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(_currentModelPath);
                return SanitizeFileName(fileName);
            }

            // Use vehicle name from UI
            string vehicleName = GetVehicleName();
            if (!string.IsNullOrEmpty(vehicleName) && vehicleName != "New Vehicle" && vehicleName != "Unnamed Vehicle")
            {
                return SanitizeFileName(vehicleName);
            }

            // Last resort: generate GUID
            return System.Guid.NewGuid().ToString().Substring(0, 8);
        }

        /// <summary>
        /// Remove invalid characters from filename
        /// </summary>
        private string SanitizeFileName(string name)
        {
            // Remove invalid file name characters
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }

            // Replace spaces with underscores
            name = name.Replace(' ', '_');

            // Remove (Clone) suffix
            name = name.Replace("(Clone)", "").Trim('_');

            return name;
        }

        private void OnDeleteClick()
        {
            bool hasSavedMeasurements =
                !string.IsNullOrEmpty(_currentVehicleId) &&
                VehicleMeasurementStorage.Exists(_currentVehicleId);

            bool hasLoadedModel = !string.IsNullOrEmpty(_currentModelPath);

            if (!hasSavedMeasurements && !hasLoadedModel)
            {
                Debug.LogWarning("[MeasurementController] Nothing to delete");
                _dataManager.GoToHome();
                return;
            }

            StartCoroutine(Co_DeleteCurrentVehicle());
        }

        /// Per-vehicle delete (HARD delete, Addressables-safe):
        /// 1) Unload instances & release memory
        /// 2) Evict Addressables cache by *resource locations*
        /// 3) Delete local saves (measurements + thumbnail)
        /// 4) Remove downloaded state (by vehicleId AND addressable key)
        /// 5) Force clean return to Home
        private IEnumerator Co_DeleteCurrentVehicle()
        {
            string vehicleId = _currentVehicleId;
            string addressableKey = _currentAddressableId;
            string modelPath = _currentModelPath;

            // -----------------------------------------
            // 1) Unload instances + MEMORY eviction
            // -----------------------------------------
            ClearExistingModels();

            var remote = remoteLoader ?? RemoteAddressableVehicleLoader.Instance;
            if (remote != null)
            {
                remote.UnloadCurrentVehicle();
            }

            // Addressables are stubborn — do this twice
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            yield return null;
            yield return Resources.UnloadUnusedAssets();
            System.GC.Collect();
            yield return null;

            // -----------------------------------------
            // 2) Clear Addressables cache by LOCATIONS
            // -----------------------------------------
            if (!string.IsNullOrEmpty(addressableKey))
            {
                var locHandle = Addressables.LoadResourceLocationsAsync(addressableKey);
                yield return locHandle;

                if (locHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded &&
                    locHandle.Result != null &&
                    locHandle.Result.Count > 0)
                {
                    yield return Addressables.ClearDependencyCacheAsync(locHandle.Result, true);
                }

                Addressables.Release(locHandle);
            }

            // -----------------------------------------
            // 3) Delete local saves (measurements + thumbnail)
            // -----------------------------------------
            try
            {
                if (!string.IsNullOrEmpty(vehicleId))
                {
                    if (VehicleMeasurementStorage.Exists(vehicleId))
                    {
                        _dataManager.DeleteVehicle(vehicleId);
                        Debug.Log($"[MeasurementController] ✓ Deleted measurements: {vehicleId}");
                    }

                    string thumbPath = VehicleMeasurementStorage.GetThumbnailPath(vehicleId);
                    if (!string.IsNullOrEmpty(thumbPath) && System.IO.File.Exists(thumbPath))
                    {
                        System.IO.File.Delete(thumbPath);
                        Debug.Log($"[MeasurementController] ✓ Deleted thumbnail: {thumbPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MeasurementController] Local cleanup error: {ex.Message}");
            }

            // -----------------------------------------
            // 4) Remove downloaded state (CRITICAL)
            // -----------------------------------------
            if (!string.IsNullOrEmpty(vehicleId))
            {
                DownloadedVehiclesTracker.RemoveDownloaded(vehicleId);
                Debug.Log($"[MeasurementController] ✓ Removed from downloads (vehicleId): {vehicleId}");
            }

            if (!string.IsNullOrEmpty(addressableKey))
            {
                DownloadedVehiclesTracker.RemoveDownloaded(addressableKey);
                Debug.Log($"[MeasurementController] ✓ Removed from downloads (addressableKey): {addressableKey}");
            }

            // -----------------------------------------
            // 5) Clear controller state
            // -----------------------------------------
            _currentModelPath = null;
            _currentAddressableId = null;
            _currentVehicleId = null;
            _currentData = null;

            if (statusText != null)
            {
                statusText.text = "Vehicle deleted locally";
            }

            // -----------------------------------------
            // 6) Go Home (forces fresh PopulateModelList)
            // -----------------------------------------
            _dataManager.GoToHome();
        }


        private void OnCompareWithClick()
        {
            if (string.IsNullOrEmpty(_currentVehicleId))
            {
                SetText(statusText, "No vehicle selected");
                return;
            }

            if (!HasMeasurements())
            {
                SetText(statusText, "Analyze or load measurements before comparing");
                return;
            }

            _dataManager.GoToComparison(_currentVehicleId, null);
        }

        private void UpdateCompareWithButtonState()
        {
            if (compareWithButton == null) return;

            bool canCompare = !string.IsNullOrEmpty(_currentVehicleId) && HasMeasurements();
            compareWithButton.interactable = canCompare;
        }


        private void OnExportClick()
        {
            // Create data from current state if not available
            if (_currentData == null)
            {
                if (measurementSystem != null && measurementSystem.IsAnalyzed)
                {
                    _currentData = new SavedVehicleMeasurement();
                    _currentData.CopyFromSystem(measurementSystem);
                    _currentData.vehicleName = GetVehicleName();
                    _currentData.savedDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                }
                else
                {
                    SetText(statusText, "Nothing to export - analyze first");
                    return;
                }
            }

            SetText(statusText, "Preparing export...");

            // ═══ USE PDF EXPORTER WITH FILE DIALOG ═══
            VehiclePDFExporter.ExportMeasurement(
                _currentData,
                exportCamera ?? Camera.main,
                (success, filePath) =>
                {
                    if (success)
                    {
                        string fileName = System.IO.Path.GetFileName(filePath);
                        SetText(statusText, $"✓ Exported: {fileName}");
                        Debug.Log($"[MeasurementController] PDF exported to: {filePath}");
                    }
                    else
                    {
                        // User cancelled or error
                        SetText(statusText, "Export cancelled");
                    }
                }
            );
        }
        public void OnMeasurementClick(string _code)
        {
            dimensionLines.HighlightOne(_code);
            Debug.Log("Highlight " + _code);
        }

        #endregion

        #region Helpers

        private string GetVehicleName()
        {
            string name = null;

            if (vehicleNameInput != null && !string.IsNullOrEmpty(vehicleNameInput.text))
                name = vehicleNameInput.text;
            else if (vehicleNameText != null && !string.IsNullOrEmpty(vehicleNameText.text))
                name = vehicleNameText.text;
            else
                name = "Unnamed Vehicle";

            // Remove (Clone) suffix if present
            if (name.Contains("(Clone)"))
                name = name.Replace("(Clone)", "").Trim();

            return name;
        }
        private string SaveThumbnailFromSprite(string vehicleId, Sprite thumbnail)
        {
            if (thumbnail == null || thumbnail.texture == null)
                return null;

            try
            {
                // Get the thumbnail path
                string thumbnailPath = VehicleMeasurementStorage.GetThumbnailPath(vehicleId);

                // Convert sprite to Texture2D
                Texture2D tex = thumbnail.texture;

                // Create a readable copy if needed
                Texture2D readableTexture;
                if (!tex.isReadable)
                {
                    // Create readable copy
                    RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height);
                    Graphics.Blit(tex, rt);
                    RenderTexture previous = RenderTexture.active;
                    RenderTexture.active = rt;

                    readableTexture = new Texture2D(tex.width, tex.height);
                    readableTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    readableTexture.Apply();

                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
                else
                {
                    readableTexture = tex;
                }

                // Encode to PNG
                byte[] bytes = readableTexture.EncodeToPNG();

                // Clean up if we created a copy
                if (readableTexture != tex)
                {
                    Destroy(readableTexture);
                }

                // Save to file
                System.IO.File.WriteAllBytes(thumbnailPath, bytes);

                Debug.Log($"[MeasurementController] Saved thumbnail to: {thumbnailPath}");
                return thumbnailPath;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MeasurementController] Failed to save thumbnail: {e.Message}");
                return null;
            }
        }
        private bool HasMeasurements()
        {
            // If saved/server data loaded
            if (_currentData != null)
            {
                return _currentData.L103_OverallLength > 0 ||
                       _currentData.W103_OverallWidth > 0 ||
                       _currentData.H100_OverallHeight > 0 ||
                       _currentData.L101_Wheelbase > 0;
            }

            // If analyzed in this session
            if (measurementSystem != null && measurementSystem.IsAnalyzed && measurementSystem.Results != null)
                return true;

            return false;
        }
        private void CopyManufacturerFromCatalog()
        {
            if (_currentData == null || string.IsNullOrEmpty(_currentModelPath)) return;

            // Try remote loader first
            var remoteLoader = RemoteAddressableVehicleLoader.Instance;
            if (remoteLoader != null && remoteLoader.IsCatalogLoaded)
            {
                var info = remoteLoader.GetVehicleInfo(_currentModelPath);
                if (info != null)
                {
                    _currentData.CopyFromRemoteInfo(info);
                    Debug.Log($"[MeasurementController] Copied manufacturer: {info.manufacturer}");
                    return;
                }
            }

            // Try local loader
            if (addressableLoader != null)
            {
                var vehicles = addressableLoader.GetAvailableVehicles();
                foreach (var v in vehicles)
                {
                    if (v.addressableKey == _currentModelPath || v.vehicleId == _currentModelPath)
                    {
                        _currentData.CopyFromAddressableInfo(v);
                        Debug.Log($"[MeasurementController] Copied manufacturer: {v.manufacturer}");
                        return;
                    }
                }
            }
        }
        // Human-readable bytes (fallback when remote.FormatBytes isn't available)
        private string FormatBytesLocal(long bytes)
        {
            const float KB = 1024f;
            const float MB = KB * 1024f;
            const float GB = MB * 1024f;

            if (bytes < KB) return $"{bytes} B";
            if (bytes < MB) return $"{bytes / KB:F1} KB";
            if (bytes < GB) return $"{bytes / MB:F1} MB";
            return $"{bytes / GB:F2} GB";
        }

        // MB/s text from bytes/sec
        private string FormatSpeedLocal(float bytesPerSec)
        {
            const float MB = 1024f * 1024f;
            if (bytesPerSec <= 0f) return "--";
            return $"{(bytesPerSec / MB):F2} MB/s";
        }

        // Simple ETA from remaining bytes + bytes/sec
        private string FormatETALocal(long remainingBytes, float bytesPerSec)
        {
            if (bytesPerSec <= 0f || remainingBytes <= 0) return "--";
            double seconds = remainingBytes / bytesPerSec;
            var ts = System.TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }
        #endregion

        private class VehicleListItem
        {
            public string displayName;
            public string path;
            public ModelLoadType loadType;
            public string sizeInfo;
            public string vehicleId;
            public string manufacturer;
            public string modelYear;
            public string category;

            public bool isDownloaded;
            public bool hasUpdateAvailable;
        }
    }



}
