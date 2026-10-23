using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VehicleMeasurement.Storage;

namespace VehicleMeasurement
{
    /// <summary>
    /// HOME CONTROLLER
    /// Dashboard with vehicle cards and quick compare
    /// 
    /// Shows two types of vehicles:
    /// 1. SAVED VEHICLES - From JSON storage (already measured)
    /// 2. ADDRESSABLE VEHICLES - From Addressables catalog (available to measure)
    /// 
    /// UI Structure:
    /// ┌─────────────────────────────────────────────────────────┐
    /// │                    HOME SCREEN                          │
    /// ├─────────────────────────────────────────────────────────┤
    /// │  MY VEHICLES (Saved/Measured)                           │
    /// │  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐       │
    /// │  │ BMW X5  │ │ Audi Q7 │ │Mercedes │ │  + Add  │       │
    /// │  │ [thumb] │ │ [thumb] │ │ [thumb] │ │   New   │       │
    /// │  │ 4500mm  │ │ 4700mm  │ │ 4800mm  │ │         │       │
    /// │  │ ✓ Done  │ │ ✓ Done  │ │ ✓ Done  │ │         │       │
    /// │  └─────────┘ └─────────┘ └─────────┘ └─────────┘       │
    /// ├─────────────────────────────────────────────────────────┤
    /// │  AVAILABLE VEHICLES (Addressables - Not Yet Measured)   │
    /// │  ┌─────────┐ ┌─────────┐ ┌─────────┐                   │
    /// │  │ Tesla Y │ │ Ford F  │ │ Porsche │                   │
    /// │  │ [thumb] │ │ [thumb] │ │ [thumb] │                   │
    /// │  │ 1.2 GB  │ │ 1.5 GB  │ │ 0.9 GB  │                   │
    /// │  │ ⬇ Ready │ │ ⬇ Downl │ │ ✓ Ready │                   │
    /// │  └─────────┘ └─────────┘ └─────────┘                   │
    /// └─────────────────────────────────────────────────────────┘
    /// </summary>
    public class HomeController : MonoBehaviour, HomeLoadingOverlay.IProgressSource
    {
        [Header("═══ SAVED VEHICLE CARDS ═══")]
        [Tooltip("Container for saved/measured vehicle cards")]
        public Transform vehicleCardsContainer;
        [Tooltip("Prefab for saved vehicle card")]
        public GameObject vehicleCardPrefab;
        [Tooltip("Separate 'Add New' button (not instantiated, already in scene)")]
        public Button addNewButton;
        [Tooltip("Label for the Add New Vehicle button, which opens the Vehicles screen (open, download, update, remove). Empty = keep the scene's label.")]
        public string vehiclesButtonLabel = "Vehicle Library";
        [Tooltip("Optional extra button that also opens the Vehicles screen.")]
        public Button downloadManagerButton;

        [Header("═══ QUICK COMPARE ═══")]
        public TMP_Dropdown vehicleADropdown;
        public TMP_Dropdown vehicleBDropdown;
        // public Dropdown vehicleADropdownLegacy;
        //  public Dropdown vehicleBDropdownLegacy;
        public Button compareButton;

        [Header("═══ UI ═══")]
        public TMP_Text titleText;
        public Text titleTextLegacy;
        public GameObject emptyStatePanel;
        public GameObject loadingPanel;
        public TMP_Text loadingText;
        public Button _exitButton;
        [SerializeField] private Button _yesQuitButton;
        [SerializeField] private Button _noQuitButton;

        [Header("═══ LOADER (For Thumbnails) ═══")]
        [Tooltip("Optional: RemoteAddressableVehicleLoader for thumbnails")]
        public RemoteAddressableVehicleLoader remoteLoader;
        [Tooltip("Optional: AddressableVehicleLoader for thumbnails")]
        public AddressableVehicleLoader localLoader;

        // Data
        private VehicleDataManager _dataManager;
        private List<SavedVehicleInfo> _savedVehicles;
        private List<SavedVehicleInfo> _filteredVehicles;
        private List<RemoteVehicleInfo> _remoteVehicles;
        private List<VehicleAddressableInfo> _localVehicles;
        private bool _useRemoteLoader = false;

        [Header("═══ FILTER ═══")]
        [Tooltip("Toggle to enable/disable manufacturer filter")]
        public Toggle manufacturerFilterToggle;
        [Tooltip("Dropdown to select manufacturer (optional - alternative to toggle)")]
        public TMP_Dropdown manufacturerDropdown;
        [Tooltip("Default manufacturer to filter when toggle is ON")]
        public string defaultFilterManufacturer = "Mahindra and Mahindra";
        private bool _filterEnabled = false;
        private string _currentFilterManufacturer = "";


        [Header("═══ SEARCH ═══")]
        [SerializeField] private TMP_InputField searchInput;
        [SerializeField] private Button clearSearchButton;

        private string _searchQuery = "";


        [SerializeField] private TMP_Text vehicleCountText;

        [SerializeField] private Button _filterButtonToggle;

        private List<VehicleCardInfo> _unifiedVehicleList;

        [Header("═══ HORIZONTAL SCROLL ═══")]
        [SerializeField] private ScrollRect vehicleScrollRect;
        [SerializeField] private Button scrollLeftButton;
        [SerializeField] private Button scrollRightButton;
        [SerializeField, Range(0.05f, 1f)] private float scrollStep = 0.25f; // 25% per click
        [SerializeField, Range(4f, 20f)] private float scrollLerpSpeed = 10f; // smoothness
        [SerializeField] private bool snapToCardWidth = false; // optional, see ComputeStep()

        private Coroutine _scrollRoutine;
        #region Unity Lifecycle

        private void Start()
        {
            _dataManager = VehicleDataManager.Instance;
            // Loading screen while the catalog, the downloaded-vehicle check and the pictures get ready
            HomeLoadingOverlay.Show(this);
            // The chosen download folder couldn't be used at start (drive unplugged, no permission): say so once
            if (!string.IsNullOrEmpty(VehicleMeasurement.Storage.DownloadLocation.StartupProblem) && !_toldAboutFolder)
            {
                _toldAboutFolder = true;
                PopupManager.ShowWarning(VehicleMeasurement.Storage.DownloadLocation.StartupProblem);
            }
            UseLiveLoader();
            var liveLoader = RemoteAddressableVehicleLoader.Instance;
            if (liveLoader != null)
            {
                // The list can change when the catalog arrives (VAL flags, names): rebuild once then
                liveLoader.OnCatalogLoaded?.RemoveListener(OnCatalogLoaded);
                liveLoader.OnCatalogLoaded?.AddListener(OnCatalogLoaded);
            }
            compareButton?.onClick.AddListener(OnCompareClick);
            addNewButton?.onClick.AddListener(OnAddNewClick);
            SetupDownloadManagerButton();
            // Setup filter toggle
            if (_filterButtonToggle != null)
            {
                _filterButtonToggle.onClick.AddListener(OnFilterToggleClicked);
                // manufacturerFilterToggle.onValueChanged.AddListener(OnFilterToggleChanged);
                // _filterEnabled = manufacturerFilterToggle.isOn;
            }

            // Setup manufacturer dropdown
            if (manufacturerDropdown != null)
            {
                manufacturerDropdown.onValueChanged.AddListener(OnManufacturerDropdownChanged);
            }

            // Determine which loader to use
            if (remoteLoader != null)
            {
                _useRemoteLoader = true;
            }
            else if (localLoader == null)
            {
                localLoader = AddressableVehicleLoader.Instance;
            }

            _currentFilterManufacturer = defaultFilterManufacturer;

            // Determine which loader to use
            if (remoteLoader != null)
            {
                _useRemoteLoader = true;
                Debug.Log("[HomeController] Using RemoteAddressableVehicleLoader");
            }
            else if (localLoader == null)
            {
                localLoader = AddressableVehicleLoader.Instance;
            }
            // Setup search input
            if (searchInput != null)
            {
                searchInput.onValueChanged.AddListener(OnSearchChanged);
            }

            if (clearSearchButton != null)
            {
                clearSearchButton.onClick.AddListener(ClearSearch);
            }

            RefreshUI();

            if (scrollLeftButton != null) scrollLeftButton.onClick.AddListener(ScrollLeft);
            if (scrollRightButton != null) scrollRightButton.onClick.AddListener(ScrollRight);

            // Keep arrow state in sync if user drags by hand
            if (vehicleScrollRect != null)
                vehicleScrollRect.onValueChanged.AddListener(_ => UpdateArrowButtons());
           /* if (remoteLoader != null)
            {
                remoteLoader.OnCatalogLoaded.AddListener(_ =>
                {
                    Debug.Log("[HomeController] Remote catalog ready → RefreshUI()");
                    RefreshUI();
                });
            }
            Debug.Log(Application.persistentDataPath + " Path");*/
        }

        private void OnEnable()
        {
            VehicleStorageService.Changed -= OnStorageChanged;
            VehicleStorageService.Changed += OnStorageChanged;
            BatchDownloads.Changed -= OnDownloadsChanged;
            BatchDownloads.Changed += OnDownloadsChanged;
            // Refresh UI whenever the home screen is enabled (e.g., returning from measurement)
            if (_dataManager != null)
            {
                Debug.Log("[HomeController] OnEnable - Refreshing UI");
                RefreshUI();
            }
            if (_exitButton != null) { _exitButton.onClick.RemoveListener(OnExitClick); _exitButton.onClick.AddListener(OnExitClick); }
            if (_yesQuitButton != null) { _yesQuitButton.onClick.RemoveListener(YesQuit); _yesQuitButton.onClick.AddListener(YesQuit); }
            if (_noQuitButton != null) { _noQuitButton.onClick.RemoveListener(NoDontQuit); _noQuitButton.onClick.AddListener(NoDontQuit); }
            HookThumbnails();

        }

        private void OnDisable()
        {
            VehicleStorageService.Changed -= OnStorageChanged;
            BatchDownloads.Changed -= OnDownloadsChanged;
            UnhookThumbnails();
        }

        // The set of downloaded vehicles changed (the first scan finished, or one was downloaded or removed)
        private static bool _toldAboutFolder;
        private void OnCatalogLoaded(int count) { OnStorageChanged(); }

        /// <summary>
        /// The loader object survives scene changes (DontDestroyOnLoad). When Home is opened again, the copy placed in
        /// the Home scene destroys itself as a duplicate, so the Inspector field became empty and Home fell back to the
        /// LOCAL download list - vehicles downloaded from the server then didn't appear. Always use the live loader.
        /// </summary>
        private void UseLiveLoader()
        {
            if (remoteLoader == null && RemoteAddressableVehicleLoader.Instance != null) remoteLoader = RemoteAddressableVehicleLoader.Instance;
            _useRemoteLoader = remoteLoader != null;
        }

        private void OnDestroy()
        {
            var liveLoader = RemoteAddressableVehicleLoader.Instance;
            if (liveLoader != null) liveLoader.OnCatalogLoaded?.RemoveListener(OnCatalogLoaded);
            UnhookThumbnails();
        }

        private void OnStorageChanged()
        {
            // Several storage events can arrive together (scan done, download recorded...): rebuild the list once
            if (_dataManager != null && isActiveAndEnabled && !_refreshQueued) { _refreshQueued = true; StartCoroutine(RefreshNextFrame()); }
        }

        private bool _refreshQueued;

        private System.Collections.IEnumerator RefreshNextFrame()
        {
            yield return null;
            _refreshQueued = false;
            if (_dataManager != null && isActiveAndEnabled) RefreshUI();
            UpdateDownloadManagerLabel();
        }

        #endregion

        bool isFiltered = false;
        private void OnFilterToggleClicked()
        {
            Animator anim = _filterButtonToggle.GetComponent<Animator>();
            isFiltered = !isFiltered;
            OnFilterToggleChanged(isFiltered);
            _filterEnabled = isFiltered;
            /* if (!isFiltered)
             {
                 //onfil
                // SetMode(ComparisonViewMode.Superimpose);
             }
             else
             {
               //  SetMode(ComparisonViewMode.SideBySide);
             }*/
            anim.SetTrigger("Switch");
        }

        private void ScrollLeft()
        {
            if (vehicleScrollRect == null) return;
            float target = Mathf.Clamp01(vehicleScrollRect.horizontalNormalizedPosition - ComputeStep());
            SmoothScrollTo(target);
        }

        private void ScrollRight()
        {
            if (vehicleScrollRect == null) return;
            float target = Mathf.Clamp01(vehicleScrollRect.horizontalNormalizedPosition + ComputeStep());
            SmoothScrollTo(target);
        }

        private float ComputeStep()
        {
            // Simple fixed step unless you enable snapping
            if (!snapToCardWidth || vehicleScrollRect == null || vehicleCardsContainer == null)
                return scrollStep;

            // Estimate "one card" worth in normalized units
            var content = vehicleScrollRect.content;
            var viewport = vehicleScrollRect.viewport != null ? vehicleScrollRect.viewport : vehicleScrollRect.GetComponent<RectTransform>();
            if (content == null || viewport == null || content.rect.width <= 0f)
                return scrollStep;

            float spacing = 0f;
            var hlg = vehicleCardsContainer.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null) spacing = hlg.spacing;

            int count = vehicleCardsContainer.childCount;
            if (count == 0) return scrollStep;

            // Use first card width as representative
            var first = vehicleCardsContainer.GetChild(0) as RectTransform;
            float cardW = first != null ? first.rect.width : 0f;

            float totalCardWidth = Mathf.Max(1f, cardW + spacing);
            float scrollableWidth = Mathf.Max(1f, content.rect.width - viewport.rect.width);

            // How much normalized movement corresponds to ~one card
            float normalizedPerCard = Mathf.Clamp01(totalCardWidth / scrollableWidth);
            return Mathf.Max(0.05f, normalizedPerCard);
        }

        private void SmoothScrollTo(float target)
        {
            if (_scrollRoutine != null) StopCoroutine(_scrollRoutine);
            _scrollRoutine = StartCoroutine(SmoothScrollCoroutine(target));
        }

        private IEnumerator SmoothScrollCoroutine(float target)
        {
            if (vehicleScrollRect == null) yield break;

            float t = 0f;
            float start = vehicleScrollRect.horizontalNormalizedPosition;

            while (Mathf.Abs(vehicleScrollRect.horizontalNormalizedPosition - target) > 0.0005f)
            {
                t += Time.unscaledDeltaTime * scrollLerpSpeed;
                float newPos = Mathf.Lerp(start, target, 1f - Mathf.Exp(-t)); // smooth-in curve
                vehicleScrollRect.horizontalNormalizedPosition = newPos;
                UpdateArrowButtons();
                yield return null;
            }

            vehicleScrollRect.horizontalNormalizedPosition = target;
            UpdateArrowButtons();
            _scrollRoutine = null;
        }

        private void UpdateArrowButtons()
        {
            if (vehicleScrollRect == null)
            {
                if (scrollLeftButton) scrollLeftButton.interactable = false;
                if (scrollRightButton) scrollRightButton.interactable = false;
                return;
            }

            var content = vehicleScrollRect.content;
            var viewport = vehicleScrollRect.viewport != null ? vehicleScrollRect.viewport : vehicleScrollRect.GetComponent<RectTransform>();
            bool canScroll = content != null && viewport != null && content.rect.width > viewport.rect.width + 1f;

            float pos = vehicleScrollRect.horizontalNormalizedPosition;
            bool atStart = pos <= 0.0001f || !canScroll;
            bool atEnd = pos >= 0.9999f || !canScroll;

            if (scrollLeftButton) scrollLeftButton.interactable = !atStart;
            if (scrollRightButton) scrollRightButton.interactable = !atEnd;
            if (scrollLeftButton) scrollLeftButton.gameObject.SetActive(!atStart);
            if (scrollRightButton) scrollRightButton.gameObject.SetActive(!atEnd);
        }

        #region Refresh UI

        public void RefreshUI()
        {
            UseLiveLoader();
            Debug.Log("[HomeController] RefreshUI called");

            // Load saved vehicles from JSON
            _savedVehicles = VehicleMeasurementStorage.GetSavedVehicleList();
            Debug.Log($"[HomeController] Found {_savedVehicles.Count} saved vehicles");
            PopulateManufacturerDropdown();
            ApplyFilter();
            // Load vehicle info from loaders (for thumbnails only)
            if (_useRemoteLoader && remoteLoader != null)
            {
                _remoteVehicles = remoteLoader.GetAvailableVehicles();
            }
            else if (localLoader != null)
            {
                _localVehicles = localLoader.GetAvailableVehicles();
            }

            LoadSavedVehicleCards();
            PopulateCompareDropdowns();
            UpdateArrowButtons();
            // ApplyFilter();
            // Show empty state if no saved vehicles
            if (emptyStatePanel != null)
                emptyStatePanel.SetActive(_savedVehicles.Count == 0);
        }

        #endregion

        #region Filter

        private void OnFilterToggleChanged(bool isOn)
        {
            _filterEnabled = isOn;
            Debug.Log($"[HomeController] Filter {(_filterEnabled ? "ENABLED" : "DISABLED")} - Manufacturer: {_currentFilterManufacturer}");
            RefreshUI();
        }

        private void OnManufacturerDropdownChanged(int index)
        {
            if (manufacturerDropdown == null) return;

            if (index == 0)
            {
                // "All Manufacturers" selected
                _filterEnabled = false;
                _currentFilterManufacturer = "";
            }
            else
            {
                _filterEnabled = true;
                _currentFilterManufacturer = manufacturerDropdown.options[index].text;
            }

            // Sync toggle if exists
            if (manufacturerFilterToggle != null)
            {
                manufacturerFilterToggle.SetIsOnWithoutNotify(_filterEnabled);
            }

            Debug.Log($"[HomeController] Filter changed - Manufacturer: {_currentFilterManufacturer}");
            // LoadSavedVehicleCards();
            //PopulateCompareDropdowns();
            ApplyUnifiedFiltersAndRefresh();
        }

        /// <summary>
        /// Set filter programmatically
        /// </summary>
        public void SetManufacturerFilter(string manufacturer, bool enable)
        {
            _currentFilterManufacturer = manufacturer;
            _filterEnabled = enable;

            if (manufacturerFilterToggle != null)
                manufacturerFilterToggle.SetIsOnWithoutNotify(enable);

            RefreshUI();
        }

        private void ApplyFilter()
        {
            if (_savedVehicles == null)
            {
                _filteredVehicles = new List<SavedVehicleInfo>();
                return;
            }

            if (!_filterEnabled || string.IsNullOrEmpty(_currentFilterManufacturer))
            {
                // No filter - show all
                _filteredVehicles = new List<SavedVehicleInfo>(_savedVehicles);
            }
            else
            {
                // Apply filter
                _filteredVehicles = new List<SavedVehicleInfo>();

                foreach (var vehicle in _savedVehicles)
                {
                    // Load full data to check manufacturer
                    var fullData = VehicleMeasurementStorage.LoadForReading(vehicle.vehicleId);

                    if (fullData != null && !string.IsNullOrEmpty(fullData.manufacturer))
                    {
                        if (fullData.manufacturer.Equals(_currentFilterManufacturer, System.StringComparison.OrdinalIgnoreCase))
                        {
                            _filteredVehicles.Add(vehicle);
                        }
                    }
                }

                Debug.Log($"[HomeController] Filter applied: {_filteredVehicles.Count}/{_savedVehicles.Count} vehicles match '{_currentFilterManufacturer}'");
            }
        }

        private void PopulateManufacturerDropdown()
        {
            if (manufacturerDropdown == null) return;

            // Collect unique manufacturers
            HashSet<string> manufacturers = new HashSet<string>();

            foreach (var vehicle in _savedVehicles)
            {
                var fullData = VehicleMeasurementStorage.LoadForReading(vehicle.vehicleId);
                if (fullData != null && !string.IsNullOrEmpty(fullData.manufacturer))
                {
                    manufacturers.Add(fullData.manufacturer);
                }
            }

            // Populate dropdown
            manufacturerDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>();
            options.Add(new TMP_Dropdown.OptionData("All Manufacturers"));

            foreach (var m in manufacturers)
            {
                options.Add(new TMP_Dropdown.OptionData(m));
            }

            manufacturerDropdown.AddOptions(options);

            // Select current filter if exists
            if (_filterEnabled && !string.IsNullOrEmpty(_currentFilterManufacturer))
            {
                for (int i = 0; i < manufacturerDropdown.options.Count; i++)
                {
                    if (manufacturerDropdown.options[i].text.Equals(_currentFilterManufacturer, System.StringComparison.OrdinalIgnoreCase))
                    {
                        manufacturerDropdown.SetValueWithoutNotify(i);
                        break;
                    }
                }
            }
        }
        private void ApplyUnifiedFiltersAndRefresh()
        {
            ApplyFilterToUnifiedList();

            // Update vehicle count label
            if (vehicleCountText != null)
            {
                if (_filterEnabled || !string.IsNullOrEmpty(_searchQuery))
                    vehicleCountText.text = $"Showing {_filteredVehicles.Count} of {_unifiedVehicleList.Count} vehicles";
                else
                    vehicleCountText.text = $"{_unifiedVehicleList.Count} vehicles";
            }

            // Clear and recreate cards
            if (vehicleCardsContainer != null)
            {
                foreach (Transform child in vehicleCardsContainer)
                    Destroy(child.gameObject);
            }

            if (_filteredVehicles == null || _filteredVehicles.Count == 0)
            {
                if (emptyStatePanel != null) emptyStatePanel.SetActive(true);
            }
            else
            {
                if (emptyStatePanel != null) emptyStatePanel.SetActive(false);
                CreateVehicleCards();
            }

            PopulateCompareDropdowns();
            UpdateArrowButtons();
        }
        private void ApplyFilterToUnifiedList()
        {
            if (_unifiedVehicleList == null)
            {
                _filteredVehicles = new List<SavedVehicleInfo>();
                return;
            }

            _filteredVehicles = new List<SavedVehicleInfo>();

            foreach (var cardInfo in _unifiedVehicleList)
            {
                // 1) Manufacturer filter (existing behavior)
                if (_filterEnabled && !string.IsNullOrEmpty(_currentFilterManufacturer))
                {
                    if (string.IsNullOrEmpty(cardInfo.manufacturer) ||
                        !cardInfo.manufacturer.Equals(_currentFilterManufacturer, System.StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // 2) Search filter (NEW)
                if (!PassesSearchFilter(cardInfo))
                    continue;

                // Convert to SavedVehicleInfo for existing card creation pipeline
                _filteredVehicles.Add(new SavedVehicleInfo
                {
                    vehicleId = cardInfo.vehicleId,
                    vehicleName = cardInfo.vehicleName,
                    savedDate = cardInfo.savedDate,
                    lastModified = cardInfo.lastModified
                });
            }

            Debug.Log($"[HomeController] Filters applied: {_filteredVehicles.Count}/{_unifiedVehicleList.Count} vehicles (search='{_searchQuery}')");
        }
        private void OnSearchChanged(string query)
        {
            _searchQuery = (query ?? "").Trim().ToLowerInvariant();

            // Re-apply filtering and refresh cards
            ApplyUnifiedFiltersAndRefresh();
        }

        private void ClearSearch()
        {
            _searchQuery = "";
            if (searchInput != null)
                searchInput.text = "";

            ApplyUnifiedFiltersAndRefresh();
        }
        private bool PassesSearchFilter(VehicleCardInfo info)
        {
            if (string.IsNullOrEmpty(_searchQuery))
                return true;

            string name = (info.vehicleName ?? "").ToLowerInvariant();
            string mfg = (info.manufacturer ?? "").ToLowerInvariant();

            return name.Contains(_searchQuery) || mfg.Contains(_searchQuery);
        }


        #endregion
        #region Saved Vehicle Cards

        /* private void LoadSavedVehicleCards()
         {
             Debug.Log($"[HomeController] LoadSavedVehicleCards - vehicleCardsContainer: {(vehicleCardsContainer != null ? "exists" : "NULL")}");
             Debug.Log($"[HomeController] LoadSavedVehicleCards - vehicleCardPrefab: {(vehicleCardPrefab != null ? "exists" : "NULL")}");
             Debug.Log($"[HomeController] LoadSavedVehicleCards - _savedVehicles count: {_savedVehicles?.Count ?? 0}");

             if (vehicleCardsContainer == null) return;

             // Clear existing
             foreach (Transform child in vehicleCardsContainer)
                 Destroy(child.gameObject);

             // Create cards for saved vehicles
             foreach (var vehicle in _filteredVehicles)
             {
                 CreateSavedVehicleCard(vehicle);
             }
         }*/

        private void LoadSavedVehicleCards()
        {
            Debug.Log("[HomeController] Loading vehicle cards...");

            // Clear existing cards
            if (vehicleCardsContainer != null)
            {
                foreach (Transform child in vehicleCardsContainer)
                {
                    Destroy(child.gameObject);
                }
            }

            // Get saved vehicles (with measurements)
            _savedVehicles = VehicleMeasurementStorage.GetSavedVehicleList();
            Debug.Log($"[HomeController] Found {_savedVehicles.Count} saved vehicles");

            // Create unified list combining saved and downloaded vehicles
            _unifiedVehicleList = new List<VehicleCardInfo>();

            // Which vehicles already have saved measurements, looked up ONCE (it used to re-read every saved file for
            // every downloaded vehicle, on every refresh: the cause of Home freezing)
            // Every name the measured vehicles go by (model key, file id, and the catalog's id for it), any casing: a
            // downloaded vehicle that already has measurements is shown once, not twice
            var savedModelPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            bool catalogReady = remoteLoader != null && remoteLoader.IsCatalogLoaded;
            foreach (var savedEntry in _savedVehicles)
            {
                var savedData = VehicleMeasurementStorage.LoadForReading(savedEntry.vehicleId);
                savedModelPaths.Add(savedEntry.vehicleId);
                if (savedData != null && !string.IsNullOrEmpty(savedData.modelPath)) savedModelPaths.Add(savedData.modelPath);
                if (catalogReady)
                {
                    var known = remoteLoader.GetVehicleInfo(savedData != null && !string.IsNullOrEmpty(savedData.modelPath) ? savedData.modelPath : savedEntry.vehicleId)
                                ?? remoteLoader.GetVehicleInfo(savedEntry.vehicleId);
                    if (known != null) { savedModelPaths.Add(known.vehicleId); if (!string.IsNullOrEmpty(known.addressableKey)) savedModelPaths.Add(known.addressableKey); }
                }
            }

            // Add all saved vehicles
            foreach (var savedInfo in _savedVehicles)
            {
                var fullData = VehicleMeasurementStorage.LoadForReading(savedInfo.vehicleId);


                bool hasVALData = fullData != null ? fullData.hasVALData : true;



                // 1️⃣ Authoritative source: REMOTE catalog (server JSON)

                if (_useRemoteLoader && remoteLoader != null && remoteLoader.IsCatalogLoaded)
                {
                    var remoteInfo = (fullData != null && !string.IsNullOrEmpty(fullData.modelPath) ? remoteLoader.GetVehicleInfo(fullData.modelPath) : null)
                                     ?? remoteLoader.GetVehicleInfo(savedInfo.vehicleId);

                    if (remoteInfo != null)
                    {
                        hasVALData = remoteInfo.hasVALData;
                    }
                }



                _unifiedVehicleList.Add(
                    VehicleCardInfo.FromSavedVehicle(
                        savedInfo,
                        fullData,
                        hasVALData
                    )
                );

               
            }
            // Get downloaded vehicles and add those without saved measurements
            if (_useRemoteLoader && remoteLoader != null)
            {
                // Using remote loader
                var downloadedVehicles = DownloadedVehiclesTracker.GetDownloadedVehicles();
                Debug.Log($"[HomeController] Found {downloadedVehicles.Count} downloaded vehicles (remote)");

                foreach (var downloadedInfo in downloadedVehicles)
                {
                    // Check if this vehicle already has saved measurements
                    bool hasSavedData = savedModelPaths.Contains(downloadedInfo.addressableKey ?? "") || savedModelPaths.Contains(downloadedInfo.vehicleId ?? "");

                    if (!hasSavedData)
                    {
                        _unifiedVehicleList.Add(VehicleCardInfo.FromRemoteVehicle(downloadedInfo));
                    }
                }
            }
            else if (localLoader != null)
            {
                // Using local loader
                var downloadedVehicles = DownloadedVehiclesTracker.GetDownloadedVehiclesLocal();
                Debug.Log($"[HomeController] Found {downloadedVehicles.Count} downloaded vehicles (local)");

                foreach (var downloadedInfo in downloadedVehicles)
                {
                    // Check if this vehicle already has saved measurements
                    bool hasSavedData = savedModelPaths.Contains(downloadedInfo.addressableKey ?? "");

                    if (!hasSavedData)
                    {
                        _unifiedVehicleList.Add(VehicleCardInfo.FromLocalVehicle(downloadedInfo));
                    }
                }
            }

            Debug.Log($"[HomeController] Total unified vehicles: {_unifiedVehicleList.Count}");

            // Apply filter
            ApplyFilterToUnifiedList();

            // Populate manufacturer dropdown
            PopulateManufacturerDropdown();

            // Update vehicle count text
            if (vehicleCountText != null)
            {
                if (_filterEnabled)
                    vehicleCountText.text = $"Showing {_filteredVehicles.Count} of {_unifiedVehicleList.Count} vehicles";
                else
                    vehicleCountText.text = $"{_unifiedVehicleList.Count} vehicles";
            }

            // Show empty state or cards
            if (_filteredVehicles == null || _filteredVehicles.Count == 0)
            {
                if (emptyStatePanel != null)
                    emptyStatePanel.SetActive(true);
            }
            else
            {
                if (emptyStatePanel != null)
                    emptyStatePanel.SetActive(false);

                // Create cards

                CreateVehicleCards();
            }

            // Update compare dropdowns
            PopulateCompareDropdowns();
        }
        private void SetVALWarning(GameObject card, bool hasVALData)
        {
            var images = card.GetComponentsInChildren<Image>(true);

            foreach (var img in images)
            {
                if (img.gameObject.name.ToLower().Contains("valwarning"))
                {
                    img.gameObject.SetActive(!hasVALData);
                    return;
                }
            }
        }


        private void CreateVehicleCards()
        {
            HookThumbnails();
            _cardThumbs.Clear();                       // the old cards are gone
            _cardIdsFor.Clear();
            _cardBadges.Clear();
            _dimmedThumbs.Clear();
            _totalThumbnails = _pendingThumbnails;     // loads still running from the previous build keep counting
            foreach (var savedInfo in _filteredVehicles)
            {
                // Find the unified info
                var unifiedInfo = _unifiedVehicleList.Find(u => u.vehicleId == savedInfo.vehicleId);
                if (unifiedInfo == null) continue;

                GameObject card = Instantiate(vehicleCardPrefab, vehicleCardsContainer);

                // Setup card data
                SetCardTexts(card, savedInfo, unifiedInfo);
                // state first: a vehicle that isn't on this PC gets a dimmed picture and an orange label
                StorageBadge.Kind kind = StorageBadge.Apply(card, unifiedInfo.vehicleId, unifiedInfo.addressableKey);
                Image thumbImg = FindThumbnailImage(card);
                if (thumbImg != null && kind == StorageBadge.Kind.DownloadNeeded) _dimmedThumbs.Add(thumbImg);
                _cardBadges.Add(new CardBadge { card = card, vehicleId = unifiedInfo.vehicleId, key = unifiedInfo.addressableKey, thumb = thumbImg, kind = kind });
                SetCardThumbnailUnified(card, unifiedInfo);
                SetVALWarning(card, unifiedInfo.hasVALData);
                // Setup click handler WITH unified info
                var button = card.GetComponent<Button>();
                if (button != null)
                {
                    // Capture both IDs
                    string vehicleId = savedInfo.vehicleId;
                    string addressableKey = unifiedInfo.addressableKey;
                    bool hasMeasurements = unifiedInfo.hasMeasurements;

                    button.onClick.AddListener(() => OnVehicleCardClick(vehicleId));
                }
            }
        }

        /*  private void OnVehicleCardClickEnhanced(string vehicleId, string addressableKey, bool hasMeasurements)
          {
              if (hasMeasurements)
              {
                  // Has measurements → load existing
                  Debug.Log($"[HomeController] Opening saved vehicle: {vehicleId}");
                  _dataManager.GoToMeasurement(vehicleId);
              }
              else
              {
                  // No measurements → open as new with this model
                  Debug.Log($"[HomeController] Opening downloaded vehicle (no measurements): {vehicleId}");
                  Debug.Log($"[HomeController] Will load model: {addressableKey}");

                  // Set the model and go to measurement
                  _dataManager.SetSelectedModel(addressableKey, ModelLoadType.Addressables);
                  _dataManager.GoToMeasurementNew();
              }
          }*/

        private void SetCardTexts(GameObject card, SavedVehicleInfo info, VehicleCardInfo unifiedInfo)
        {
            // Load full data for vehicles with measurements
            SavedVehicleMeasurement fullData = null;
            if (unifiedInfo.hasMeasurements)
            {
                fullData = VehicleMeasurementStorage.LoadForReading(info.vehicleId);
            }

            // TMP Text components
            var tmpTexts = card.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmpTexts)
            {
                string n = t.gameObject.name.ToLower();

                if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
                {
                    t.text = info.vehicleName;
                }
                else if (n.Contains("manufacturer") || n.Contains("make") || n.Contains("brand"))
                {
                    if (!string.IsNullOrEmpty(unifiedInfo.manufacturer))
                        t.text = unifiedInfo.manufacturer;
                    else
                        t.text = "---";
                }
                else if (n.Contains("model"))
                {
                    if (fullData != null && !string.IsNullOrEmpty(fullData.vehicleModel))
                        t.text = fullData.vehicleModel;
                    else
                        t.text = "---";
                }
                else if (n.Contains("date") || n.Contains("modified") || n.Contains("saved"))
                {
                    t.text = info.lastModified ?? info.savedDate ?? "";
                }
                else if (n.Contains("status"))
                {
                    if (!unifiedInfo.hasVALData)
                    {
                        t.text = "VAL data not available";
                        t.color = new Color(1f, 0.6f, 0f); // orange
                    }
                    else if (unifiedInfo.hasMeasurements)
                    {
                        t.text = "Measurements Available";
                        t.color = Color.green;
                    }
                    else
                    {
                        t.text = "Ready to measure";
                        t.color = Color.white;
                    }
                }
            }

            // Legacy Text components
            var legacyTexts = card.GetComponentsInChildren<Text>(true);
            foreach (var t in legacyTexts)
            {
                string n = t.gameObject.name.ToLower();

                if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
                {
                    t.text = info.vehicleName;
                }
                else if (n.Contains("manufacturer") || n.Contains("make") || n.Contains("brand"))
                {
                    if (!string.IsNullOrEmpty(unifiedInfo.manufacturer))
                        t.text = unifiedInfo.manufacturer;
                }
                else if (n.Contains("status") || n.Contains("warning"))
                {
                    if (unifiedInfo.hasMeasurements)
                    {
                        t.text = "Measured";
                        t.color = Color.green;
                    }
                    else
                    {
                        t.text = "No Measurements";
                        t.color = new Color(1f, 0.6f, 0f);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NEW METHOD - Set thumbnail for unified cards
        // ═══════════════════════════════════════════════════════════════════════════


        // ── Thumbnails (loaded in the background; see ThumbnailCache) ────────────
        private int _pendingThumbnails;
        private int _totalThumbnails;
        public int PendingThumbnails { get { return _pendingThumbnails; } }
        public int TotalThumbnails { get { return _totalThumbnails; } }

        private static Image FindThumbnailImage(GameObject card)
        {
            // best name first: an object called "...Thumb..." wins over a "BackgroundImage" or an "Icon" found earlier
            var all = card.GetComponentsInChildren<Image>(true);
            foreach (string word in new[] { "thumb", "preview", "image", "icon" })
                foreach (var img in all)
                    if (img.gameObject != card && img.gameObject.name.ToLower().Contains(word)) return img;
            return null;
        }

        // Cards listen for their vehicle's server thumbnail: a picture that arrives - or is replaced on the server - after
        // the cards were built still shows up. (Before, a card looked once; if the picture wasn't loaded yet it stayed
        // empty, and a copy saved on this PC long ago always won over the server's current picture.)
        private readonly Dictionary<string, List<Image>> _cardThumbs = new Dictionary<string, List<Image>>(System.StringComparer.OrdinalIgnoreCase);
        private RemoteAddressableVehicleLoader _thumbSource;

        private void HookThumbnails()
        {
            var live = RemoteAddressableVehicleLoader.Instance;
            if (live == _thumbSource) return;
            UnhookThumbnails();
            _thumbSource = live;
            if (_thumbSource != null)
            {
                _thumbSource.ThumbnailUpdated += OnServerThumbnail;
                _thumbSource.ThumbnailsFinished += RebindCardThumbnails;
                if (_thumbSource.OnCatalogLoaded != null) _thumbSource.OnCatalogLoaded.AddListener(OnCatalogForThumbnails);
                RebindCardThumbnails();                  // cards built before the loader was ready
            }
            else if (isActiveAndEnabled && !_waitingForLoader) StartCoroutine(HookWhenLoaderReady());
        }

        private bool _waitingForLoader;
        private System.Collections.IEnumerator HookWhenLoaderReady()
        {
            _waitingForLoader = true;
            while (RemoteAddressableVehicleLoader.Instance == null) yield return null;
            _waitingForLoader = false;
            HookThumbnails();
        }

        private void OnCatalogForThumbnails(int count) { RebindCardThumbnails(); }

        private void UnhookThumbnails()
        {
            if (_thumbSource != null)
            {
                _thumbSource.ThumbnailUpdated -= OnServerThumbnail;
                _thumbSource.ThumbnailsFinished -= RebindCardThumbnails;
                if (_thumbSource.OnCatalogLoaded != null) _thumbSource.OnCatalogLoaded.RemoveListener(OnCatalogForThumbnails);
            }
            _thumbSource = null;
        }

        /// <summary>
        /// Give every card the server's current picture: cards built before the vehicle list or the pictures were
        /// ready, cards whose id is a saved file id, and cards whose picture was replaced. Cheap: in-memory lookups only.
        /// </summary>
        private void RebindCardThumbnails()
        {
            var live = RemoteAddressableVehicleLoader.Instance;
            if (live == null) return;
            // cards registered before the vehicle list was loaded: also listen under the catalog's id now
            foreach (var pair in new List<KeyValuePair<string, List<Image>>>(_cardThumbs))
            {
                var info = live.GetVehicleInfo(pair.Key);
                if (info == null || string.IsNullOrEmpty(info.vehicleId) || string.Equals(info.vehicleId, pair.Key, System.StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var img in pair.Value.ToArray()) if (img != null) AddWatch(info.vehicleId, img);
                HashSet<string> ids;
                if (!_cardIdsFor.TryGetValue(info.vehicleId, out ids)) _cardIdsFor[info.vehicleId] = ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                ids.Add(pair.Key);
            }
            foreach (var pair in _cardThumbs)
            {
                Sprite sprite = live.GetThumbnail(pair.Key);
                if (sprite == null) continue;
                pair.Value.RemoveAll(i => i == null);
                foreach (var img in pair.Value)
                    if (img.sprite != sprite) { img.sprite = sprite; img.color = ThumbColor(img); }
            }
        }

        // ── card state labels (ON THIS PC / DOWNLOAD NEEDED / UPDATE AVAILABLE / DOWNLOADING n%) ──
        private class CardBadge { public GameObject card; public string vehicleId, key; public Image thumb; public StorageBadge.Kind kind; }
        private readonly List<CardBadge> _cardBadges = new List<CardBadge>();
        private readonly HashSet<Image> _dimmedThumbs = new HashSet<Image>();
        private float _nextBadgeRefresh;
        private bool _badgeRefreshQueued;

        /// <summary>Pictures of vehicles that must be downloaded first are shown dimmed.</summary>
        private Color ThumbColor(Image img)
        {
            return img != null && _dimmedThumbs.Contains(img) ? new Color(0.45f, 0.45f, 0.48f, 1f) : Color.white;
        }

        // Downloads report progress many times a second: labels are refreshed at most twice a second, without rebuilding cards
        private void OnDownloadsChanged() { _badgeRefreshQueued = true; }

        private void LateUpdate()
        {
            if (!_badgeRefreshQueued || Time.unscaledTime < _nextBadgeRefresh) return;
            _badgeRefreshQueued = false;
            _nextBadgeRefresh = Time.unscaledTime + 0.5f;
            RefreshCardBadges();
        }

        private void RefreshCardBadges()
        {
            _cardBadges.RemoveAll(b => b == null || b.card == null);
            foreach (var b in _cardBadges)
            {
                b.kind = StorageBadge.Apply(b.card, b.vehicleId, b.key);
                if (b.thumb == null) continue;
                bool dim = b.kind == StorageBadge.Kind.DownloadNeeded;
                if (dim) _dimmedThumbs.Add(b.thumb); else _dimmedThumbs.Remove(b.thumb);
                if (b.thumb.sprite != null) b.thumb.color = ThumbColor(b.thumb);
            }
        }

        // card id (a saved file id such as "Thar_Roxx" can differ from the catalog id) -> catalog id, so the server's
        // picture (announced by catalog id) reaches every card of that vehicle
        private readonly Dictionary<string, HashSet<string>> _cardIdsFor = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);

        private void WatchThumbnail(string vehicleId, Image img)
        {
            if (string.IsNullOrEmpty(vehicleId) || img == null) return;
            AddWatch(vehicleId, img);
            var live = RemoteAddressableVehicleLoader.Instance;
            var info = live != null ? live.GetVehicleInfo(vehicleId) : null;
            if (info != null && !string.IsNullOrEmpty(info.vehicleId) && !string.Equals(info.vehicleId, vehicleId, System.StringComparison.OrdinalIgnoreCase))
            {
                AddWatch(info.vehicleId, img);
                HashSet<string> ids;
                if (!_cardIdsFor.TryGetValue(info.vehicleId, out ids)) _cardIdsFor[info.vehicleId] = ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                ids.Add(vehicleId);
            }
        }

        private void AddWatch(string id, Image img)
        {
            List<Image> list;
            if (!_cardThumbs.TryGetValue(id, out list)) _cardThumbs[id] = list = new List<Image>();
            if (!list.Contains(img)) list.Add(img);
        }

        private void OnServerThumbnail(string vehicleId, Sprite sprite)
        {
            if (string.IsNullOrEmpty(vehicleId)) return;
            List<Image> list;
            if (sprite != null && _cardThumbs.TryGetValue(vehicleId, out list))
            {
                list.RemoveAll(i => i == null);
                foreach (var img in list) { img.sprite = sprite; img.color = ThumbColor(img); }
            }
            RefreshSavedCopy(vehicleId);
            HashSet<string> cardIds;
            if (_cardIdsFor.TryGetValue(vehicleId, out cardIds))
                foreach (string id in cardIds) RefreshSavedCopy(id);
        }

        /// <summary>The copy kept with saved measurements (used for reports) follows the server's picture too.</summary>
        private void RefreshSavedCopy(string vehicleId)
        {
            try
            {
                var live = RemoteAddressableVehicleLoader.Instance;
                string src = live != null ? live.GetThumbnailFile(vehicleId) : null;
                string dst = VehicleMeasurementStorage.GetThumbnailPath(vehicleId);
                if (src == null || !System.IO.File.Exists(src) || !System.IO.File.Exists(dst)) return;
                var a = new System.IO.FileInfo(src); var b = new System.IO.FileInfo(dst);
                if (a.Length == b.Length && a.LastWriteTimeUtc <= b.LastWriteTimeUtc) return;
                System.IO.File.Copy(src, dst, true);
                ThumbnailCache.Forget(dst);
            }
            catch (System.Exception e) { Debug.LogWarning("[HomeController] Could not update the saved thumbnail copy: " + e.Message); }
        }

        /// <summary>
        /// The picture file for a card when the server's picture isn't in memory yet: the server's copy on this PC, else
        /// (vehicles the server doesn't list, or never fetched) the one saved with the measurements or by the old list.
        /// </summary>
        private static string ThumbnailFileFor(VehicleCardInfo cardInfo)
        {
            var live = RemoteAddressableVehicleLoader.Instance;
            string server = live != null ? live.GetThumbnailFile(cardInfo.vehicleId) : null;
            if (server != null && System.IO.File.Exists(server)) return server;
            if (cardInfo.hasMeasurements)
            {
                var fullData = VehicleMeasurementStorage.LoadForReading(cardInfo.vehicleId);
                if (fullData != null && !string.IsNullOrEmpty(fullData.thumbnailPath) && System.IO.File.Exists(fullData.thumbnailPath)) return fullData.thumbnailPath;
            }
            else if (!string.IsNullOrEmpty(cardInfo.thumbnailUrl) && !cardInfo.thumbnailUrl.StartsWith("http", System.StringComparison.OrdinalIgnoreCase)
                     && System.IO.File.Exists(cardInfo.thumbnailUrl))
                return cardInfo.thumbnailUrl;
            string saved = VehicleMeasurementStorage.GetThumbnailPath(cardInfo.vehicleId);
            return System.IO.File.Exists(saved) ? saved : null;
        }

        private void SetCardThumbnailUnified(GameObject card, VehicleCardInfo cardInfo)
        {
            Image img = FindThumbnailImage(card);
            if (img == null) return;
            WatchThumbnail(cardInfo.vehicleId, img);

            // 1) the server's picture, already in memory
            var live = RemoteAddressableVehicleLoader.Instance;
            Sprite current = live != null ? (live.GetThumbnail(cardInfo.vehicleId) ?? live.GetThumbnail(cardInfo.addressableKey)) : null;
            if (!string.IsNullOrEmpty(cardInfo.addressableKey) && !string.Equals(cardInfo.addressableKey, cardInfo.vehicleId, System.StringComparison.OrdinalIgnoreCase))
                WatchThumbnail(cardInfo.addressableKey, img);
            if (current != null) { img.sprite = current; img.color = ThumbColor(img); return; }

            // 2) a file on this PC meanwhile (replaced when the server's picture arrives)
            string file = ThumbnailFileFor(cardInfo);
            if (file != null)
            {
                Sprite cached = ThumbnailCache.TryGet(file);
                if (cached != null) { img.sprite = cached; img.color = ThumbColor(img); return; }
                _pendingThumbnails++;
                _totalThumbnails++;
                StartCoroutine(ThumbnailCache.Load(file, sprite =>
                {
                    _pendingThumbnails = Mathf.Max(0, _pendingThumbnails - 1);
                    if (img == null) return;                      // the card was rebuilt meanwhile
                    var now = RemoteAddressableVehicleLoader.Instance;
                    if (now != null && (now.GetThumbnail(cardInfo.vehicleId) ?? now.GetThumbnail(cardInfo.addressableKey)) != null) return;   // server picture won the race
                    if (sprite != null) { img.sprite = sprite; img.color = ThumbColor(img); }
                    else ApplyFallbackThumbnail(img, cardInfo);
                }));
                return;
            }
            ApplyFallbackThumbnail(img, cardInfo);
        }

        /// <summary>No picture file on this PC: use the server catalog's thumbnail (cached on disk by the loader), or the local loader's.</summary>
        private void ApplyFallbackThumbnail(Image img, VehicleCardInfo cardInfo)
        {
            Sprite sprite = null;
            var live = RemoteAddressableVehicleLoader.Instance;
            if (live != null) sprite = live.GetThumbnail(cardInfo.vehicleId);
            if (sprite == null && localLoader != null)
            {
                var info = localLoader.GetAvailableVehicles().Find(v => v.vehicleId == cardInfo.vehicleId);
                if (info != null) sprite = info.thumbnail;
            }
            if (sprite != null) { img.sprite = sprite; img.color = ThumbColor(img); }
        }



        private void CreateSavedVehicleCard(SavedVehicleInfo info)
        {
            if (vehicleCardPrefab == null) return;

            Debug.Log($"[HomeController] CreateSavedVehicleCard - Name: {info.vehicleName}, ID: {info.vehicleId}");

            var card = Instantiate(vehicleCardPrefab, vehicleCardsContainer);

            // IMPORTANT: Make sure card is active
            card.SetActive(true);

            // Load full data for details
            var fullData = VehicleMeasurementStorage.LoadForReading(info.vehicleId);
            Debug.Log($"[HomeController] Loaded fullData: {(fullData != null ? fullData.vehicleName : "NULL")}");

            // Set texts
            SetCardTexts(card, info, fullData);

            // Set thumbnail
            SetCardThumbnail(card, info, fullData);

            // Set click handler
            var button = card.GetComponent<Button>();
            if (button != null)
            {
                string id = info.vehicleId;
                button.onClick.AddListener(() => OnVehicleCardClick(id));
            }

            Debug.Log($"[HomeController] Card created and activated for: {info.vehicleName}");
        }

        private void SetCardTexts(GameObject card, SavedVehicleInfo info, SavedVehicleMeasurement fullData)
        {
            // TMP_Text components
            var tmpTexts = card.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmpTexts)
            {
                string n = t.gameObject.name.ToLower();

                if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
                {
                    t.text = info.vehicleName;
                }
                else if (n.Contains("dim") || n.Contains("size") || n.Contains("measurement"))
                {
                    if (fullData != null)
                        t.text = $"{fullData.L103_OverallLength:F0} × {fullData.W103_OverallWidth:F0} × {fullData.H100_OverallHeight:F0} mm";
                    else
                        t.text = "---";
                }
                else if (n.Contains("date") || n.Contains("time") || n.Contains("saved"))
                {
                    t.text = info.lastModified ?? info.savedDate ?? "---";
                }
                else if (n.Contains("status"))
                {
                    t.text = "Measured";
                }
            }

            // Legacy Text components
            var texts = card.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                string n = t.gameObject.name.ToLower();

                if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
                {
                    t.text = info.vehicleName;
                }
                else if (n.Contains("dim") || n.Contains("size") || n.Contains("measurement"))
                {
                    if (fullData != null)
                        t.text = $"{fullData.L103_OverallLength:F0} × {fullData.W103_OverallWidth:F0} × {fullData.H100_OverallHeight:F0} mm";
                    else
                        t.text = "---";
                }
                else if (n.Contains("date") || n.Contains("time") || n.Contains("saved"))
                {
                    t.text = info.lastModified ?? info.savedDate ?? "---";
                }
            }
        }

        private void SetCardThumbnail(GameObject card, SavedVehicleInfo info, SavedVehicleMeasurement fullData)
        {
            var images = card.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                string n = img.name.ToLower();
                if (n.Contains("thumb") || n.Contains("icon") || n.Contains("preview") || n.Contains("image"))
                {
                    Sprite thumbnail = null;

                    // Priority 0: the server's current picture (and follow it if it changes)
                    WatchThumbnail(info.vehicleId, img);
                    var live = RemoteAddressableVehicleLoader.Instance;
                    thumbnail = live != null ? live.GetThumbnail(info.vehicleId) : null;
                    if (thumbnail != null)
                    {
                        img.sprite = thumbnail;
                        img.color = Color.white;
                        return;
                    }

                    // Priority 1: Load from saved thumbnail path
                    if (fullData != null && !string.IsNullOrEmpty(fullData.thumbnailPath))
                    {
                        thumbnail = VehicleMeasurementStorage.LoadThumbnailFromPath(fullData.thumbnailPath);
                        if (thumbnail != null)
                        {
                            img.sprite = thumbnail;
                            img.color = Color.white;
                            return;
                        }
                    }

                    // Priority 2: Load by vehicle ID
                    if (VehicleMeasurementStorage.ThumbnailExists(info.vehicleId))
                    {
                        thumbnail = VehicleMeasurementStorage.LoadThumbnail(info.vehicleId);
                        if (thumbnail != null)
                        {
                            img.sprite = thumbnail;
                            img.color = Color.white;
                            return;
                        }
                    }

                    // Priority 3: Try to get from loader (fallback)
                    if (fullData != null && !string.IsNullOrEmpty(fullData.modelPath))
                    {
                        if (_useRemoteLoader && remoteLoader != null)
                        {
                            var remoteInfo = GetRemoteInfoByKey(fullData.modelPath);
                            if (remoteInfo != null)
                            {
                                thumbnail = remoteLoader.GetThumbnail(remoteInfo.vehicleId);
                            }
                        }
                        else if (localLoader != null)
                        {
                            var localInfo = GetLocalInfoByKey(fullData.modelPath);
                            if (localInfo != null)
                            {
                                thumbnail = localInfo.thumbnail;
                            }
                        }

                        if (thumbnail != null)
                        {
                            img.sprite = thumbnail;
                            img.color = Color.white;
                            return;
                        }
                    }

                    // Fallback: keep default sprite
                }
            }
        }

        #endregion

        #region Compare Dropdowns

        private void PopulateCompareDropdowns()
        {
            // TMP Dropdowns
            if (vehicleADropdown != null)
            {
                vehicleADropdown.ClearOptions();
                var options = new List<TMP_Dropdown.OptionData>();
                options.Add(new TMP_Dropdown.OptionData("-- Select Vehicle --"));
                foreach (var v in _savedVehicles)
                    options.Add(new TMP_Dropdown.OptionData(v.vehicleName));
                vehicleADropdown.AddOptions(options);
            }

            if (vehicleBDropdown != null)
            {
                vehicleBDropdown.ClearOptions();
                var options = new List<TMP_Dropdown.OptionData>();
                options.Add(new TMP_Dropdown.OptionData("-- Select Vehicle --"));
                foreach (var v in _savedVehicles)
                    options.Add(new TMP_Dropdown.OptionData(v.vehicleName));
                vehicleBDropdown.AddOptions(options);
            }

            /* // Legacy Dropdowns
             if (vehicleADropdownLegacy != null)
             {
                 vehicleADropdownLegacy.ClearOptions();
                 var options = new List<Dropdown.OptionData>();
                 options.Add(new Dropdown.OptionData("-- Select Vehicle --"));
                 foreach (var v in _savedVehicles)
                     options.Add(new Dropdown.OptionData(v.vehicleName));
                 vehicleADropdownLegacy.AddOptions(options);
             }

             if (vehicleBDropdownLegacy != null)
             {
                 vehicleBDropdownLegacy.ClearOptions();
                 var options = new List<Dropdown.OptionData>();
                 options.Add(new Dropdown.OptionData("-- Select Vehicle --"));
                 foreach (var v in _savedVehicles)
                     options.Add(new Dropdown.OptionData(v.vehicleName));
                 vehicleBDropdownLegacy.AddOptions(options);
             }*/

            if (compareButton != null)
                compareButton.interactable = _savedVehicles.Count >= 2;
        }

        #endregion

        #region Click Handlers


        private void OnVehicleCardClick(string vehicleId)
        {
            // Not on this PC / update available: say so and what it costs before starting a download
            var card = _unifiedVehicleList != null ? _unifiedVehicleList.Find(v => v.vehicleId == vehicleId) : null;
            StorageBadge.Kind kind; string label;
            StorageBadge.Describe(vehicleId, card != null ? card.addressableKey : null, out kind, out label);
            if (kind == StorageBadge.Kind.DownloadNeeded || kind == StorageBadge.Kind.Update)
            {
                string name = card != null && !string.IsNullOrEmpty(card.vehicleName) ? card.vehicleName : vehicleId;
                bool update = kind == StorageBadge.Kind.Update;
                int dot = label.IndexOf('·');
                string size = dot >= 0 ? label.Substring(dot + 1).Trim() : "";
                DasDialog.Confirm((update ? "Update and open " : "Download and open ") + name + "?",
                    (update ? "A newer version is on the server." : "This vehicle isn't on this PC.") +
                    (size != "" ? " About " + size + " to download." : "") + " It opens as soon as the download finishes.",
                    update ? "Update and open" : "Download and open", "Cancel", () => OpenCard(vehicleId));
                return;
            }
            OpenCard(vehicleId);
        }

        private void OpenCard(string vehicleId)
        {
            // Check if this vehicle has saved measurements
            bool hasMeasurements = VehicleMeasurementStorage.Exists(vehicleId);
            Debug.Log($"[DEBUG] OnVehicleCardClick: vehicleId={vehicleId}, hasMeasurements={hasMeasurements}");
            if (hasMeasurements)
            {
                // Has saved measurements → load existing vehicle
                Debug.Log($"[HomeController] Opening saved vehicle: {vehicleId}");
                _dataManager.GoToMeasurement(vehicleId);
            }
            else
            {
                // No saved measurements → it's a downloaded-only vehicle
                Debug.Log($"[HomeController] Opening downloaded vehicle (no measurements): {vehicleId}");

                // Find the vehicle info to get the addressable key
                var vehicleCard = _unifiedVehicleList.Find(v => v.vehicleId == vehicleId);

                if (vehicleCard != null && !string.IsNullOrEmpty(vehicleCard.addressableKey))
                {
                    // Set the model to load
                    _dataManager.SetSelectedModel(vehicleCard.addressableKey, vehicleCard.addressableKey);
                    _dataManager.GoToMeasurementNew();
                }
                else
                {
                    Debug.LogWarning($"[HomeController] Cannot find addressable key for: {vehicleId}");
                    _dataManager.GoToMeasurementNew();
                }
            }
        }
        // ── Vehicles (one screen for opening, downloading, updating and removing vehicles) ──
        private TMP_Text _vehiclesLabel;

        private void SetupDownloadManagerButton()
        {
            // "Add New Vehicle" now opens the Vehicles screen: pick a vehicle to open, download or update several,
            // remove what you don't need, see disk use - all in one place (was: three different screens).
            if (addNewButton != null)
            {
                _vehiclesLabel = null;
                foreach (var t in addNewButton.GetComponentsInChildren<TMP_Text>(true))
                    if (_vehiclesLabel == null || (t.text ?? "").Length > (_vehiclesLabel.text ?? "").Length) _vehiclesLabel = t;
            }
            // One way in: an extra button wired here is hidden (it would do the same as Add New Vehicle)
            if (downloadManagerButton != null && downloadManagerButton != addNewButton) downloadManagerButton.gameObject.SetActive(false);
            UpdateDownloadManagerLabel();
        }

        /// <summary>"Vehicles" / "Vehicles (3 updates)".</summary>
        private void UpdateDownloadManagerLabel()
        {
            if (_vehiclesLabel == null || string.IsNullOrEmpty(vehiclesButtonLabel)) return;
            int updates = 0;
            var service = VehicleStorageService.Instance;
            var live = RemoteAddressableVehicleLoader.Instance;
            if (service != null && service.IsReady && live != null && live.IsCatalogLoaded)
                foreach (var v in live.GetAvailableVehicles())
                {
                    if (v == null) continue;
                    var st = service.GetState(v.vehicleId, v.addressableKey);
                    if (st != null && st.NeedsUpdate) updates++;
                }
            _vehiclesLabel.text = updates > 0 ? vehiclesButtonLabel + " (" + updates + " update" + (updates == 1 ? "" : "s") + ")" : vehiclesButtonLabel;
        }

        private void OnAddNewClick()
        {
            VehicleLibraryPanel.Open();
        }
        [SerializeField] private GameObject _quitPanel;

        private void OnExitClick()
        {
            if (_quitPanel != null) { _quitPanel.SetActive(true); }

        }
        private void YesQuit()
        {
            Application.Quit();
        }
        private void NoDontQuit()
        {
            if (_quitPanel != null) { _quitPanel.SetActive(false); }
        }

        private void OnCompareClick()
        {
            int indexA = (vehicleADropdown != null) ? vehicleADropdown.value - 1 :
                         -1;
            int indexB = (vehicleBDropdown != null) ? vehicleBDropdown.value - 1 :
                         -1;

            if (indexA < 0 || indexB < 0 || indexA >= _savedVehicles.Count || indexB >= _savedVehicles.Count)
            {
                Debug.LogWarning("[HomeController] Please select both vehicles");
                return;
            }

            if (indexA == indexB)
            {
                Debug.LogWarning("[HomeController] Please select different vehicles");
                return;
            }

            string vehicleAId = _savedVehicles[indexA].vehicleId;
            string vehicleBId = _savedVehicles[indexB].vehicleId;
            _dataManager.GoToComparison(vehicleAId, vehicleBId);
        }

        #endregion

        #region Helpers

        private RemoteVehicleInfo GetRemoteInfoByKey(string addressableKey)
        {
            if (_remoteVehicles == null) return null;

            foreach (var info in _remoteVehicles)
            {
                if (info.addressableKey == addressableKey)
                    return info;
            }
            return null;
        }

        private VehicleAddressableInfo GetLocalInfoByKey(string addressableKey)
        {
            if (_localVehicles == null) return null;

            foreach (var info in _localVehicles)
            {
                if (info.addressableKey == addressableKey)
                    return info;
            }
            return null;
        }

        private void ShowLoading(bool show, string message = "Loading...")
        {
            if (loadingPanel != null)
                loadingPanel.SetActive(show);
            if (loadingText != null)
                loadingText.text = message;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024f * 1024f):F1} MB";
            else
                return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        #endregion

    }

}
