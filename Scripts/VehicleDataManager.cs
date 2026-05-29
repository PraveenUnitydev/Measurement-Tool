using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VehicleMeasurement
{
    /// <summary>
    /// VEHICLE DATA MANAGER (3-Screen Flow)
    /// 
    /// Singleton that persists across scenes.
    /// Manages navigation and data passing between screens.
    /// 
    /// ════════════════════════════════════════════════════════════════
    /// 
    /// 3-SCREEN FLOW:
    /// 
    ///     ┌──────────────────────────────────────────────────────┐
    ///     │                      HOME                            │
    ///     │   • Vehicle Cards (from saved JSON)                  │
    ///     │   • Quick Compare dropdowns                          │
    ///     │   • "+ Add New" button                               │
    ///     └──────────────────────┬───────────────────────────────┘
    ///                            │
    ///          ┌─────────────────┼─────────────────┐
    ///          │                 │                 │
    ///          ▼                 ▼                 ▼
    ///     Click Card       + Add New         Quick Compare
    ///          │                 │                 │
    ///          ▼                 ▼                 ▼
    ///     ┌─────────────────────────┐      ┌─────────────┐
    ///     │   MEASUREMENT SCREEN    │      │ COMPARISON  │
    ///     │   • View/Edit saved     │      │   SCREEN    │
    ///     │   • Analyze new model   │      │             │
    ///     │   • Save to JSON        │      │ Load JSON   │
    ///     └─────────────────────────┘      └─────────────┘
    /// 
    /// ════════════════════════════════════════════════════════════════
    /// 
    /// USAGE:
    /// 
    ///     // Navigate to measurement (existing vehicle)
    ///     VehicleDataManager.Instance.GoToMeasurement("U171_SUV");
    ///     
    ///     // Navigate to measurement (new vehicle)
    ///     VehicleDataManager.Instance.GoToMeasurementNew();
    ///     
    ///     // Navigate to measurement with specific model
    ///     VehicleDataManager.Instance.GoToMeasurementWithModel("Vehicles/NewCar", ModelLoadType.Resources);
    ///     
    ///     // Navigate to comparison
    ///     VehicleDataManager.Instance.GoToComparison("U171_SUV", "CompetitorA");
    ///     
    ///     // Go back to home
    ///     VehicleDataManager.Instance.GoToHome();
    ///     
    ///     // Get selected vehicle in measurement screen
    ///     string id = VehicleDataManager.Instance.SelectedVehicleId;
    ///     bool isNew = VehicleDataManager.Instance.IsNewVehicle;
    /// 
    /// ════════════════════════════════════════════════════════════════
    /// </summary>
    public class VehicleDataManager : MonoBehaviour
    {
        #region Singleton

        private static VehicleDataManager _instance;
        public static VehicleDataManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<VehicleDataManager>();

                    if (_instance == null)
                    {
                        GameObject go = new GameObject("VehicleDataManager");
                        _instance = go.AddComponent<VehicleDataManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Scene Names

        [Header("═══ SCENE CONFIGURATION ═══")]
        [Tooltip("Name of home/dashboard scene")]
        public string homeSceneName = "HomeScene";

        [Tooltip("Name of measurement scene")]
        public string measurementSceneName = "MeasurementScene";

        [Tooltip("Name of comparison scene")]
        public string comparisonSceneName = "ComparisonScene";

        #endregion

        #region State

        [Header("═══ CURRENT STATE ═══")]
        [SerializeField] private string _selectedVehicleId;
        [SerializeField] private string _selectedModelPath;
        [SerializeField] private ModelLoadType _selectedModelLoadType;
        [SerializeField] private string _compareVehicleAId;
        [SerializeField] private string _compareVehicleBId;
        [SerializeField] private bool _isNewVehicle;

        #endregion

        #region Properties

        /// <summary>
        /// Vehicle ID selected for measurement screen.
        /// null if new vehicle.
        /// </summary>
        public string SelectedVehicleId => _selectedVehicleId;

        /// <summary>
        /// Model path for loading (Resources path, Addressable key, etc.)
        /// </summary>

        public string SelectedModelPath { get; private set; }
        public ModelLoadType SelectedModelLoadType { get; private set; }

        public string SelectedModelKey { get; private set; }


        public void SetSelectedModel(string modelPath, string modelKey)
        {
            SelectedModelPath = modelPath;
            SelectedModelKey = modelKey;
            Debug.Log($"[DataManager] Set model to load: path={modelPath}, key={modelKey}");
        }

        public void ClearSelectedModel()
        {
            SelectedModelPath = null;
            SelectedModelLoadType = ModelLoadType.Resources;
        }

        /// <summary>
        /// Vehicle A for comparison screen
        /// </summary>
        public string CompareVehicleAId => _compareVehicleAId;

        /// <summary>
        /// Vehicle B for comparison screen
        /// </summary>
        public string CompareVehicleBId => _compareVehicleBId;

        /// <summary>
        /// True if measurement screen should show model picker (new vehicle)
        /// </summary>
        public bool IsNewVehicle => _isNewVehicle;

        /// <summary>
        /// Number of saved vehicles
        /// </summary>
        public int SavedVehicleCount => VehicleMeasurementStorage.GetSavedVehicleIds().Length;

        #endregion

        #region Events

        public event Action OnNavigateToHome;
        public event Action OnNavigateToMeasurement;
        public event Action OnNavigateToComparison;
        public event Action<string> OnVehicleSelected;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        #endregion

        #region Navigation - Home

        /// <summary>
        /// Go to Home/Dashboard screen
        /// </summary>
        public void GoToHome()
        {
            ClearSelection();
            OnNavigateToHome?.Invoke();
            LoadScene(homeSceneName);
        }

        #endregion

        #region Navigation - Measurement

        /// <summary>
        /// Go to Measurement screen with existing saved vehicle
        /// </summary>
        /// <param name="vehicleId">Vehicle ID to load</param>
        public void GoToMeasurement(string vehicleId)
        {
            _selectedVehicleId = vehicleId;
            _isNewVehicle = false;
            _selectedModelPath = null;
            ClearSelectedModel();
            // Try to get model path from saved data
            var savedData = VehicleMeasurementStorage.Load(vehicleId);
            if (savedData != null)
            {
                // Model path could be stored in saved data if needed
                Debug.Log($"[VehicleDataManager] Loading existing vehicle: {vehicleId}");
            }

            OnVehicleSelected?.Invoke(vehicleId);
            OnNavigateToMeasurement?.Invoke();
            LoadScene(measurementSceneName);
        }

        /// <summary>
        /// Go to Measurement screen for new vehicle (will show model picker)
        /// </summary>
        public void GoToMeasurementNew()
        {
            _selectedVehicleId = null;
            _isNewVehicle = true;
            _selectedModelPath = null;

            Debug.Log("[VehicleDataManager] New vehicle - will show model picker");

            OnNavigateToMeasurement?.Invoke();
            LoadScene(measurementSceneName);
        }

        /// <summary>
        /// Go to Measurement screen with specific model
        /// </summary>
        /// <param name="modelPath">Path to model</param>
        /// <param name="loadType">How to load the model</param>
        public void GoToMeasurementWithModel(string modelPath, ModelLoadType loadType = ModelLoadType.Resources)
        {
            _selectedVehicleId = null;
            _isNewVehicle = true;
            _selectedModelPath = modelPath;
            _selectedModelLoadType = loadType;

            Debug.Log($"[VehicleDataManager] New vehicle with model: {modelPath}");

            OnNavigateToMeasurement?.Invoke();
            LoadScene(measurementSceneName);
        }

        /// <summary>
        /// Go to Measurement screen with Addressable benchmarking vehicle
        /// </summary>
        /// <param name="addressableKey">The Addressable key for the vehicle</param>
        public void GoToMeasurementWithAddressable(string addressableKey)
        {
            _selectedVehicleId = null;
            _isNewVehicle = true;
            _selectedModelPath = addressableKey;
            _selectedModelLoadType = ModelLoadType.Addressables;

            Debug.Log($"[VehicleDataManager] New vehicle from Addressables: {addressableKey}");

            OnNavigateToMeasurement?.Invoke();
            LoadScene(measurementSceneName);
        }

        #endregion

        #region Navigation - Comparison

        /// <summary>
        /// Go to Comparison screen
        /// </summary>
        /// <param name="vehicleAId">Pre-select Vehicle A (optional)</param>
        /// <param name="vehicleBId">Pre-select Vehicle B (optional)</param>
        public void GoToComparison(string vehicleAId = null, string vehicleBId = null)
        {
            _compareVehicleAId = vehicleAId;
            _compareVehicleBId = vehicleBId;

            Debug.Log($"[VehicleDataManager] Compare: {vehicleAId ?? "?"} vs {vehicleBId ?? "?"}");

            OnNavigateToComparison?.Invoke();
            LoadScene(comparisonSceneName);
        }

        /// <summary>
        /// Go to Comparison with current vehicle as Vehicle A
        /// (Called from Measurement screen "Compare With..." button)
        /// </summary>
        public void GoToComparisonWithCurrent()
        {
            GoToComparison(_selectedVehicleId, null);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Clear all selection state
        /// </summary>
        public void ClearSelection()
        {
            _selectedVehicleId = null;
            _selectedModelPath = null;
            _isNewVehicle = false;
            _compareVehicleAId = null;
            _compareVehicleBId = null;
        }

        /// <summary>
        /// Get list of all saved vehicles
        /// </summary>
        public List<SavedVehicleInfo> GetSavedVehicles()
        {
            return VehicleMeasurementStorage.GetSavedVehicleList();
        }

        /// <summary>
        /// Check if vehicle has saved measurements
        /// </summary>
        public bool HasSavedMeasurements(string vehicleId)
        {
            return VehicleMeasurementStorage.Exists(vehicleId);
        }

        /// <summary>
        /// Delete a saved vehicle
        /// </summary>
        public bool DeleteVehicle(string vehicleId)
        {
            bool result = VehicleMeasurementStorage.Delete(vehicleId);
            if (result)
            {
                Debug.Log($"[VehicleDataManager] Deleted: {vehicleId}");
            }
            return result;
        }

        /// <summary>
        /// Set the selected vehicle ID after saving new vehicle
        /// </summary>
        public void SetSelectedVehicleId(string vehicleId)
        {
            _selectedVehicleId = vehicleId;
            _isNewVehicle = false;
        }

        private void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("[VehicleDataManager] Scene name is empty!");
                return;
            }

            Debug.Log($"[VehicleDataManager] Loading scene: {sceneName}");
            SceneManager.LoadScene(sceneName);
        }

        #endregion
    }

    /// <summary>
    /// How to load the vehicle model
    /// </summary>
    public enum ModelLoadType
    {
        Resources,       // Load from Resources folder: Resources.Load<GameObject>(path)
        Addressables,    // Load via Addressables: Addressables.LoadAssetAsync<GameObject>(key)
        StreamingAssets, // Load from StreamingAssets (requires runtime importer like TriLib)
        SceneReference   // Model is already in scene, just find by name
    }
}
