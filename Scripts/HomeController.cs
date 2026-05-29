using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    public class HomeController : MonoBehaviour
    {
        [Header("═══ SAVED VEHICLE CARDS ═══")]
        [Tooltip("Container for saved/measured vehicle cards")]
        public Transform vehicleCardsContainer;
        [Tooltip("Prefab for saved vehicle card")]
        public GameObject vehicleCardPrefab;
        [Tooltip("Separate 'Add New' button (not instantiated, already in scene)")]
        public Button addNewButton;

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
            compareButton?.onClick.AddListener(OnCompareClick);
            addNewButton?.onClick.AddListener(OnAddNewClick);
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
            // Refresh UI whenever the home screen is enabled (e.g., returning from measurement)
            if (_dataManager != null)
            {
                Debug.Log("[HomeController] OnEnable - Refreshing UI");
                RefreshUI();
            }
            _exitButton.onClick.AddListener(OnExitClick);
            _yesQuitButton.onClick.AddListener(YesQuit);
            _noQuitButton.onClick.AddListener(NoDontQuit);

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
                    var fullData = VehicleMeasurementStorage.Load(vehicle.vehicleId);

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
                var fullData = VehicleMeasurementStorage.Load(vehicle.vehicleId);
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

            // Add all saved vehicles
            foreach (var savedInfo in _savedVehicles)
            {
                var fullData = VehicleMeasurementStorage.Load(savedInfo.vehicleId);


                bool hasVALData = fullData != null ? fullData.hasVALData : true;



                // 1️⃣ Authoritative source: REMOTE catalog (server JSON)

                if (_useRemoteLoader && remoteLoader != null && remoteLoader.IsCatalogLoaded)
                {
                    var remoteInfo = remoteLoader.GetAvailableVehicles()
                        .Find(v => v.vehicleId == savedInfo.vehicleId);

                    if (remoteInfo != null)
                    {
                        hasVALData = remoteInfo.hasVALData;
                    }
                }


                Debug.Log($"[VAL CHECK] {savedInfo.vehicleId} → hasVALData={hasVALData}");

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
                    bool hasSavedData = _savedVehicles.Exists(s =>
                    {
                        var data = VehicleMeasurementStorage.Load(s.vehicleId);
                        return data != null && data.modelPath == downloadedInfo.addressableKey;
                    });

                    if (!hasSavedData)
                    {
                        _unifiedVehicleList.Add(VehicleCardInfo.FromRemoteVehicle(downloadedInfo));
                        Debug.Log($"[HomeController] Added downloaded-only vehicle: {downloadedInfo.vehicleName}");
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
                    bool hasSavedData = _savedVehicles.Exists(s =>
                    {
                        var data = VehicleMeasurementStorage.Load(s.vehicleId);
                        return data != null && data.modelPath == downloadedInfo.addressableKey;
                    });

                    if (!hasSavedData)
                    {
                        _unifiedVehicleList.Add(VehicleCardInfo.FromLocalVehicle(downloadedInfo));
                        Debug.Log($"[HomeController] Added downloaded-only vehicle: {downloadedInfo.vehicleName}");
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
            foreach (var savedInfo in _filteredVehicles)
            {
                // Find the unified info
                var unifiedInfo = _unifiedVehicleList.Find(u => u.vehicleId == savedInfo.vehicleId);
                if (unifiedInfo == null) continue;

                GameObject card = Instantiate(vehicleCardPrefab, vehicleCardsContainer);

                // Setup card data
                SetCardTexts(card, savedInfo, unifiedInfo);
                SetCardThumbnailUnified(card, unifiedInfo);
                SetVALWarning(card, unifiedInfo.hasVALData);
                // Setup click handler WITH unified info
                Debug.Log($"[HOME SOURCE] vehicleId={savedInfo.vehicleId} " + $"SelectedVehicleId={VehicleDataManager.Instance?.SelectedVehicleId}");
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
                fullData = VehicleMeasurementStorage.Load(info.vehicleId);
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
                        t.text = "⏳ Ready to measure";
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
                        t.text = "✓ Measured";
                        t.color = Color.green;
                    }
                    else
                    {
                        t.text = "⚠ No Measurements";
                        t.color = new Color(1f, 0.6f, 0f);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // NEW METHOD - Set thumbnail for unified cards
        // ═══════════════════════════════════════════════════════════════════════════


        private void SetCardThumbnailUnified(GameObject card, VehicleCardInfo cardInfo)
        {
            var images = card.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                string n = img.gameObject.name.ToLower();
                if (n.Contains("thumb") || n.Contains("preview") || n.Contains("image") || n.Contains("icon"))
                {
                    Sprite thumbnail = null;

                    // For saved vehicles with measurements
                    if (cardInfo.hasMeasurements)
                    {
                        var fullData = VehicleMeasurementStorage.Load(cardInfo.vehicleId);

                        // Try saved thumbnail path
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

                        // Try vehicle ID thumbnail
                        if (VehicleMeasurementStorage.ThumbnailExists(cardInfo.vehicleId))
                        {
                            thumbnail = VehicleMeasurementStorage.LoadThumbnail(cardInfo.vehicleId);
                            if (thumbnail != null)
                            {
                                img.sprite = thumbnail;
                                img.color = Color.white;
                                return;
                            }
                        }
                    }
                    else
                    {
                        // ▼▼▼ FOR DOWNLOADED-ONLY VEHICLES ▼▼▼

                        // Try loading from thumbnailUrl (which stores local path for local loader)
                        if (!string.IsNullOrEmpty(cardInfo.thumbnailUrl))
                        {
                            thumbnail = VehicleMeasurementStorage.LoadThumbnailFromPath(cardInfo.thumbnailUrl);
                            if (thumbnail != null)
                            {
                                img.sprite = thumbnail;
                                img.color = Color.white;
                                return;
                            }
                        }

                        // Try standard thumbnail path
                        if (VehicleMeasurementStorage.ThumbnailExists(cardInfo.vehicleId))
                        {
                            thumbnail = VehicleMeasurementStorage.LoadThumbnail(cardInfo.vehicleId);
                            if (thumbnail != null)
                            {
                                img.sprite = thumbnail;
                                img.color = Color.white;
                                return;
                            }
                        }

                        // ▲▲▲ END DOWNLOADED-ONLY SECTION ▲▲▲
                    }

                    // Fallback: Try loader thumbnail (won't work for downloaded but worth trying)
                    if (localLoader != null)
                    {
                        var vehicles = localLoader.GetAvailableVehicles();
                        var vehicleInfo = vehicles.Find(v => v.vehicleId == cardInfo.vehicleId);
                        if (vehicleInfo != null && vehicleInfo.thumbnail != null)
                        {
                            thumbnail = vehicleInfo.thumbnail;
                        }
                    }

                    if (thumbnail != null)
                    {
                        img.sprite = thumbnail;
                        img.color = Color.white;
                    }

                    return;
                }
            }
        }



        private void CreateSavedVehicleCard(SavedVehicleInfo info)
        {
            if (vehicleCardPrefab == null) return;

            Debug.Log($"[HomeController] CreateSavedVehicleCard - Name: {info.vehicleName}, ID: {info.vehicleId}");

            var card = Instantiate(vehicleCardPrefab, vehicleCardsContainer);

            // IMPORTANT: Make sure card is active
            card.SetActive(true);

            // Load full data for details
            var fullData = VehicleMeasurementStorage.Load(info.vehicleId);
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
                    t.text = "✓ Measured";
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
        private void OnAddNewClick()
        {
            Debug.Log("[HomeController] Adding new vehicle");
            _dataManager.GoToMeasurementNew();
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
