using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VehicleMeasurement;

public class MainMenuController : MonoBehaviour
{
    [Header("═══ SAVED VEHICLE CARDS ═══")]
    [Tooltip("Container for saved/measured vehicle cards")]
    public Transform vehicleCardsContainer;
    [Tooltip("Prefab for saved vehicle card")]
    public GameObject vehicleCardPrefab;
    [Tooltip("Prefab for 'Add New' card")]
    public GameObject addNewCardPrefab;

    [Header("═══ ADDRESSABLE VEHICLES SECTION ═══")]
    [Tooltip("Container for addressable vehicle cards (vehicles available to download/measure)")]
    public Transform addressableVehiclesContainer;
    [Tooltip("Prefab for addressable vehicle card (shows download status)")]
    public GameObject addressableVehicleCardPrefab;
    [Tooltip("Section header for addressable vehicles")]
    public GameObject addressableSectionHeader;

    [Header("═══ QUICK COMPARE ═══")]
    public TMP_Dropdown vehicleADropdown;
    public TMP_Dropdown vehicleBDropdown;
    public Dropdown vehicleADropdownLegacy;
    public Dropdown vehicleBDropdownLegacy;
    public Button compareButton;

    [Header("═══ UI ═══")]
    public TMP_Text titleText;
    public Text titleTextLegacy;
    public GameObject emptyStatePanel;
    public GameObject loadingPanel;
    public TMP_Text loadingText;

    [Header("═══ ADDRESSABLE LOADER ═══")]
    [Tooltip("Reference to AddressableVehicleLoader (auto-finds if null)")]
    public AddressableVehicleLoader addressableLoader;

    // Data
    private VehicleDataManager _dataManager;
    private List<SavedVehicleInfo> _savedVehicles;
    private List<VehicleAddressableInfo> _addressableVehicles;

    #region Unity Lifecycle

    private void Start()
    {
        _dataManager = VehicleDataManager.Instance;
        compareButton?.onClick.AddListener(OnCompareClick);

        // Find AddressableVehicleLoader if not assigned
        if (addressableLoader == null)
            addressableLoader = AddressableVehicleLoader.Instance;

        RefreshUI();
    }

    private void OnEnable()
    {
        // Refresh UI whenever the home screen is enabled (e.g., returning from measurement)
        if (_dataManager != null)
        {
            Debug.Log("[HomeController] OnEnable - Refreshing UI");
            RefreshUI();
        }
    }

    #endregion

    #region Refresh UI

    public void RefreshUI()
    {
        Debug.Log("[HomeController] RefreshUI called");

        // Load saved vehicles from JSON
        _savedVehicles = VehicleMeasurementStorage.GetSavedVehicleList();
        Debug.Log($"[HomeController] Found {_savedVehicles.Count} saved vehicles");

        // Load addressable vehicles from catalog
        if (addressableLoader != null)
        {
            //_addressableVehicles = addressableLoader.GetVehicleCatalog();
            Debug.Log($"[HomeController] Found {_addressableVehicles?.Count ?? 0} addressable vehicles");
        }

        LoadSavedVehicleCards();
        LoadAddressableVehicleCards();
        PopulateCompareDropdowns();

        // Show empty state if no saved vehicles
        if (emptyStatePanel != null)
            emptyStatePanel.SetActive(_savedVehicles.Count == 0);
    }

    #endregion

    #region Saved Vehicle Cards

    private void LoadSavedVehicleCards()
    {
        if (vehicleCardsContainer == null) return;

        // Clear existing
        foreach (Transform child in vehicleCardsContainer)
            Destroy(child.gameObject);

        // Create cards for saved vehicles
        foreach (var vehicle in _savedVehicles)
            CreateSavedVehicleCard(vehicle);

        // Add "New Vehicle" card
        if (addNewCardPrefab != null)
        {
            var addCard = Instantiate(addNewCardPrefab, vehicleCardsContainer);
            var btn = addCard.GetComponent<Button>();
            btn?.onClick.AddListener(OnAddNewClick);
        }
    }

    private void CreateSavedVehicleCard(SavedVehicleInfo info)
    {
        if (vehicleCardPrefab == null) return;

        var card = Instantiate(vehicleCardPrefab, vehicleCardsContainer);

        // Load full data for details
        var fullData = VehicleMeasurementStorage.Load(info.vehicleId);

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
                // Try to get thumbnail from Addressables catalog if model source exists
                if (fullData != null && !string.IsNullOrEmpty(fullData.modelPath))
                {
                    var addressableInfo = GetAddressableInfoByKey(fullData.modelPath);
                    if (addressableInfo != null && addressableInfo.thumbnail != null)
                    {
                        img.sprite = addressableInfo.thumbnail;
                        img.color = Color.white;
                        return;
                    }
                }

                // Fallback: keep default sprite
            }
        }
    }

    #endregion

    #region Addressable Vehicle Cards

    private void LoadAddressableVehicleCards()
    {
        if (addressableVehiclesContainer == null || _addressableVehicles == null) return;

        // Clear existing
        foreach (Transform child in addressableVehiclesContainer)
            Destroy(child.gameObject);

        // Count vehicles that haven't been measured yet
        int availableCount = 0;
        foreach (var vehicle in _addressableVehicles)
        {
            if (!IsVehicleAlreadySaved(vehicle.addressableKey))
                availableCount++;
        }

        // Show/hide section header
        if (addressableSectionHeader != null)
            addressableSectionHeader.SetActive(availableCount > 0);

        // Create cards for each addressable vehicle
        foreach (var vehicle in _addressableVehicles)
        {
            // Skip if already saved (measured)
            if (IsVehicleAlreadySaved(vehicle.addressableKey))
                continue;

            CreateAddressableVehicleCard(vehicle);
        }
    }

    private bool IsVehicleAlreadySaved(string addressableKey)
    {
        foreach (var saved in _savedVehicles)
        {
            var fullData = VehicleMeasurementStorage.Load(saved.vehicleId);
            if (fullData != null && fullData.modelPath == addressableKey)
                return true;
        }
        return false;
    }

    private void CreateAddressableVehicleCard(VehicleAddressableInfo info)
    {
        GameObject prefab = addressableVehicleCardPrefab ?? vehicleCardPrefab;
        if (prefab == null) return;

        var card = Instantiate(prefab, addressableVehiclesContainer);

        // Set texts
        SetAddressableCardTexts(card, info);

        // Set thumbnail
        if (info.thumbnail != null)
        {
            var images = card.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                string n = img.name.ToLower();
                if (n.Contains("thumb") || n.Contains("icon") || n.Contains("preview") || n.Contains("image"))
                {
                    img.sprite = info.thumbnail;
                    img.color = Color.white;
                    break;
                }
            }
        }

        // Check download status and update UI
        StartCoroutine(UpdateAddressableCardStatus(card, info));

        // Set click handler
        var button = card.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(() => OnAddressableVehicleClick(info));
        }
    }

    private void SetAddressableCardTexts(GameObject card, VehicleAddressableInfo info)
    {
        // TMP_Text
        var tmpTexts = card.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmpTexts)
        {
            string n = t.gameObject.name.ToLower();

            if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
            {
                t.text = info.vehicleName;
            }
            else if (n.Contains("dim") || n.Contains("measurement"))
            {
                t.text = "Not measured yet";
            }
            else if (n.Contains("category") || n.Contains("type"))
            {
                t.text = info.category ?? "";
            }
            else if (n.Contains("manufacturer") || n.Contains("make"))
            {
                t.text = info.manufacturer ?? "";
            }
            else if (n.Contains("size") || n.Contains("download"))
            {
                t.text = info.approximateSize ?? "";
            }
            else if (n.Contains("status"))
            {
                t.text = "Tap to measure";
            }
            else if (n.Contains("description") || n.Contains("desc"))
            {
                t.text = info.description ?? "";
            }
        }

        // Legacy Text
        var texts = card.GetComponentsInChildren<Text>(true);
        foreach (var t in texts)
        {
            string n = t.gameObject.name.ToLower();

            if (n.Contains("name") || n.Contains("title") || n.Contains("vehicle"))
            {
                t.text = info.vehicleName;
            }
            else if (n.Contains("size") || n.Contains("download"))
            {
                t.text = info.approximateSize ?? "";
            }
        }
    }

    private IEnumerator UpdateAddressableCardStatus(GameObject card, VehicleAddressableInfo info)
    {
        if (addressableLoader == null) yield break;

        // Check if cached
        bool isCached = false;
        long downloadSize = 0;
        bool checkComplete = false;

        addressableLoader.CheckDownloadStatus(info.vehicleId, (cached, size) => {
            isCached = cached;
            downloadSize = size;
            checkComplete = true;
        });

        // Wait for check
        float timeout = 5f;
        float elapsed = 0f;
        while (!checkComplete && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (card == null) yield break; // Card may have been destroyed

        // Update status text
        var tmpTexts = card.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmpTexts)
        {
            string n = t.gameObject.name.ToLower();
            if (n.Contains("status"))
            {
                if (isCached)
                    t.text = "✓ Ready to measure";
                else
                    t.text = $"⬇ Download ({FormatBytes(downloadSize)})";
            }
        }

        // Legacy Text
        var texts = card.GetComponentsInChildren<Text>(true);
        foreach (var t in texts)
        {
            string n = t.gameObject.name.ToLower();
            if (n.Contains("status"))
            {
                if (isCached)
                    t.text = "✓ Ready";
                else
                    t.text = $"⬇ {FormatBytes(downloadSize)}";
            }
        }

        // Update download icon if exists
        var images = card.GetComponentsInChildren<Image>(true);
        foreach (var img in images)
        {
            string n = img.name.ToLower();
            if (n.Contains("downloadicon") || n.Contains("statusicon"))
            {
                img.color = isCached ? Color.green : Color.white;
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

        // Legacy Dropdowns
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
        }

        if (compareButton != null)
            compareButton.interactable = _savedVehicles.Count >= 2;
    }

    #endregion

    #region Click Handlers

    private void OnVehicleCardClick(string vehicleId)
    {
        Debug.Log($"[HomeController] Opening saved vehicle: {vehicleId}");
        _dataManager.GoToMeasurement(vehicleId);
    }

    private void OnAddressableVehicleClick(VehicleAddressableInfo info)
    {
        Debug.Log($"[HomeController] Opening addressable vehicle: {info.vehicleName} ({info.addressableKey})");

        // Go to measurement scene with this addressable vehicle
        _dataManager.GoToMeasurement(info.addressableKey);
    }

    private void OnAddNewClick()
    {
        Debug.Log("[HomeController] Adding new vehicle");
        _dataManager.GoToMeasurementNew();
    }

    private void OnCompareClick()
    {
        // Get selected indices (prefer TMP, fallback to legacy)
        int indexA = (vehicleADropdown != null) ? vehicleADropdown.value - 1 :
                     (vehicleADropdownLegacy != null) ? vehicleADropdownLegacy.value - 1 : -1;
        int indexB = (vehicleBDropdown != null) ? vehicleBDropdown.value - 1 :
                     (vehicleBDropdownLegacy != null) ? vehicleBDropdownLegacy.value - 1 : -1;

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

    private VehicleAddressableInfo GetAddressableInfoByKey(string addressableKey)
    {
        if (_addressableVehicles == null) return null;

        foreach (var info in _addressableVehicles)
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

