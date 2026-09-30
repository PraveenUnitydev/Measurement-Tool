using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using VehicleMeasurement;
using UnityEngine.UI;

public class ReviewController : MonoBehaviour
{
    public TMP_Dropdown vehicleADropdown;

    private VehicleDataManager _dataManager;
    private List<SavedVehicleInfo> _savedVehicles;

    [SerializeField] private Toggle mahindraOnlyToggle;
    [SerializeField] private string filterManufacturer = "Mahindra and Mahindra";

    // index 0 is placeholder ("-- Select Vehicle --")
    private readonly List<SavedVehicleInfo> _indexToVehicle = new List<SavedVehicleInfo>();

    private void Start()
    {
        _dataManager = VehicleDataManager.Instance;
        PopulateDropdowns();

        if (vehicleADropdown != null)
            vehicleADropdown.onValueChanged.AddListener(OnVehicleDropdownChanged);

        if (mahindraOnlyToggle != null)
            mahindraOnlyToggle.onValueChanged.AddListener(_ => PopulateDropdowns());

    }

    private void PopulateDropdowns()
    {
        // Load the saved list (lightweight)
        _savedVehicles = VehicleMeasurementStorage.GetSavedVehicleList();  

        // Apply filter only if toggle is ON
        var filtered = new List<SavedVehicleInfo>();
        bool doFilter = mahindraOnlyToggle != null && mahindraOnlyToggle.isOn;

        foreach (var v in _savedVehicles)
        {
            if (!doFilter)
            {
                filtered.Add(v);
                continue;
            }

            // Prefer manufacturer from lightweight list; if missing, open the full save once
            string mfg = v.manufacturer;
            if (string.IsNullOrEmpty(mfg))
            {
                var full = VehicleMeasurementStorage.Load(v.vehicleId);       
                mfg = full != null ? full.manufacturer : null;
            }

            if (!string.IsNullOrEmpty(mfg))
            {
                bool isMahindra =
                    mfg.Equals(filterManufacturer, System.StringComparison.OrdinalIgnoreCase) ||
                    mfg.Equals("Mahindra & Mahindra", System.StringComparison.OrdinalIgnoreCase);

                if (isMahindra) filtered.Add(v);
            }
        }

        // Build dropdown options from filtered list
        var options = new List<TMP_Dropdown.OptionData>();
        options.Add(new TMP_Dropdown.OptionData("-- Select Vehicle --"));

        _indexToVehicle.Clear();
        _indexToVehicle.Add(null); // placeholder for index 0

        foreach (var v in filtered)
        {
            options.Add(new TMP_Dropdown.OptionData(v.vehicleName));
            _indexToVehicle.Add(v);
        }

        if (vehicleADropdown != null)
        {
            vehicleADropdown.ClearOptions();
            vehicleADropdown.AddOptions(options);
            vehicleADropdown.value = 0;
            vehicleADropdown.RefreshShownValue();
        }
    }


    private void OnVehicleDropdownChanged(int index)
    {
        if (index <= 0 || index >= _indexToVehicle.Count) return;

        var sel = _indexToVehicle[index];
        if (sel == null) return;

        // 1) Load the full saved record to get the model source info
        var data = VehicleMeasurementStorage.Load(sel.vehicleId);
        if (data == null)
        {
            Debug.LogError($"[ReviewController] No saved data for '{sel.vehicleId}'.");
            return;
        }

        string displayName = string.IsNullOrEmpty(data.vehicleName) ? sel.vehicleName : data.vehicleName;

        // 2) If the record has a model source, we can switch models in-place
        if (data.HasModelSource())
        {
            string path = data.modelPath;                     // Addressables key or Resources path
            ModelLoadType loadType = data.GetModelLoadType(); // Parses enum from string
            InvokeMeasurementSelection(displayName, path, loadType);
        }
        else
        {
            // Optional: If no model source was saved, you can still drive the controller to load saved
            // measurements by vehicleId (also private). If you want that, uncomment below and add
            // a reflection call to LoadExistingVehicle(sel.vehicleId). Otherwise, warn:
            Debug.LogWarning($"[ReviewController] '{displayName}' has no model source saved.");
        }
    }

    private void InvokeMeasurementSelection(string displayName, string path, ModelLoadType loadType)
    {
        // Find the existing controller in this scene
        var measurement = FindObjectOfType<MeasurementController>(includeInactive: true);
        if (measurement == null)
        {
            Debug.LogError("[ReviewController] MeasurementController not found in scene.");
            return;
        }

        var mi = typeof(MeasurementController)
                 .GetMethod("OnModelSelectedFromList", BindingFlags.Instance | BindingFlags.NonPublic);

        if (mi == null)
        {
            Debug.LogError("[ReviewController] OnModelSelectedFromList not found. Did the method name/signature change?");
            return;
        }

        object[] args = new object[] { displayName, path, loadType, null, null };

        try
        {
            mi.Invoke(measurement, args);
        }
        catch (TargetInvocationException tie)
        {
            Debug.LogError($"[ReviewController] Invoke failed (inner): {tie.InnerException?.Message}\n{tie.InnerException?.StackTrace}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ReviewController] Invoke failed: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
