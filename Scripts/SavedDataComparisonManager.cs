using System;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Compares two SAVED vehicle measurements
    /// No live measurement - uses stored JSON data only
    /// </summary>
    public class SavedDataComparisonManager : MonoBehaviour
    {
        [Header("═══ SELECTED VEHICLES ═══")]
        public string vehicleAId;
        public string vehicleBId;

        [Header("═══ LOADED DATA ═══")]
        [SerializeField] private SavedVehicleMeasurement _vehicleA;
        [SerializeField] private SavedVehicleMeasurement _vehicleB;

        [Header("═══ COMPARISON RESULT ═══")]
        public SavedComparisonResult comparison;

        // Events for UI
        public event Action OnDataLoaded;
        public event Action OnComparisonComplete;

        /// <summary>
        /// Load saved data for Vehicle A
        /// </summary>
        public bool LoadVehicleA(string vehicleId)
        {
            vehicleAId = vehicleId;
            _vehicleA = VehicleMeasurementStorage.Load(vehicleId);

            if (_vehicleA != null)
            {
                Debug.Log($"[Comparison] Loaded Vehicle A: {_vehicleA.vehicleName}");
                OnDataLoaded?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Load saved data for Vehicle B
        /// </summary>
        public bool LoadVehicleB(string vehicleId)
        {
            vehicleBId = vehicleId;
            _vehicleB = VehicleMeasurementStorage.Load(vehicleId);

            if (_vehicleB != null)
            {
                Debug.Log($"[Comparison] Loaded Vehicle B: {_vehicleB.vehicleName}");
                OnDataLoaded?.Invoke();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get Vehicle A data
        /// </summary>
        public SavedVehicleMeasurement VehicleA => _vehicleA;

        /// <summary>
        /// Get Vehicle B data
        /// </summary>
        public SavedVehicleMeasurement VehicleB => _vehicleB;

        /// <summary>
        /// Run comparison between loaded vehicles
        /// </summary>
        [ContextMenu("Compare")]
        public SavedComparisonResult Compare()
        {
            comparison = new SavedComparisonResult();

            if (_vehicleA == null || _vehicleB == null)
            {
                Debug.LogError("[Comparison] Load both vehicles first!");
                return comparison;
            }

            comparison.VehicleAId = vehicleAId;
            comparison.VehicleBId = vehicleBId;
            comparison.VehicleAName = _vehicleA.vehicleName;
            comparison.VehicleBName = _vehicleB.vehicleName;
            comparison.ComparedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Compare all measurements
            comparison.Length = CreateComparison("Length", "L103", _vehicleA.L103_OverallLength, _vehicleB.L103_OverallLength);
            comparison.Width = CreateComparison("Width", "W103", _vehicleA.W103_OverallWidth, _vehicleB.W103_OverallWidth);
            comparison.Height = CreateComparison("Height", "H100", _vehicleA.H100_OverallHeight, _vehicleB.H100_OverallHeight);
            comparison.Wheelbase = CreateComparison("Wheelbase", "L101", _vehicleA.L101_Wheelbase, _vehicleB.L101_Wheelbase);
            comparison.FrontOverhang = CreateComparison("Front Overhang", "L104", _vehicleA.L104_FrontOverhang, _vehicleB.L104_FrontOverhang);
            comparison.RearOverhang = CreateComparison("Rear Overhang", "L105", _vehicleA.L105_RearOverhang, _vehicleB.L105_RearOverhang);
            comparison.FrontTrack = CreateComparison("Front Track", "W144", _vehicleA.W144_FrontTrack, _vehicleB.W144_FrontTrack);
            comparison.RearTrack = CreateComparison("Rear Track", "W145", _vehicleA.W145_RearTrack, _vehicleB.W145_RearTrack);
            comparison.GroundClearance = CreateComparison("Ground Clearance", "H101", _vehicleA.H101_GroundClearance, _vehicleB.H101_GroundClearance);
            comparison.FrontWheelDia = CreateComparison("Front Wheel Dia", "TD_F", _vehicleA.TD_F_FrontDiameter, _vehicleB.TD_F_FrontDiameter);
            comparison.RearWheelDia = CreateComparison("Rear Wheel Dia", "TD_R", _vehicleA.TD_R_RearDiameter, _vehicleB.TD_R_RearDiameter);

            LogComparison();
            OnComparisonComplete?.Invoke();

            return comparison;
        }

        private SavedMeasurementComparison CreateComparison(string name, string code, float valueA, float valueB)
        {
            float diff = valueA - valueB;
            float percentDiff = valueB != 0 ? (diff / valueB) * 100f : 0f;

            return new SavedMeasurementComparison
            {
                Name = name,
                Code = code,
                ValueA = valueA,
                ValueB = valueB,
                Difference = diff,
                PercentDifference = percentDiff
            };
        }

        /// <summary>
        /// Get all comparisons as array (for UI)
        /// </summary>
        public SavedMeasurementComparison[] GetAllComparisons()
        {
            if (comparison == null) return new SavedMeasurementComparison[0];

            return new SavedMeasurementComparison[]
            {
                comparison.Length,
                comparison.Width,
                comparison.Height,
                comparison.Wheelbase,
                comparison.FrontOverhang,
                comparison.RearOverhang,
                comparison.FrontTrack,
                comparison.RearTrack,
                comparison.GroundClearance,
                comparison.FrontWheelDia,
                comparison.RearWheelDia
            };
        }

        /// <summary>
        /// Get comparison by code
        /// </summary>
        public SavedMeasurementComparison GetComparison(string code)
        {
            if (comparison == null) return null;

            foreach (var c in GetAllComparisons())
            {
                if (c != null && c.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        /// <summary>
        /// Swap Vehicle A and B
        /// </summary>
        public void SwapVehicles()
        {
            var tempData = _vehicleA;
            _vehicleA = _vehicleB;
            _vehicleB = tempData;

            var tempId = vehicleAId;
            vehicleAId = vehicleBId;
            vehicleBId = tempId;

            if (_vehicleA != null && _vehicleB != null)
                Compare();
        }

        private void LogComparison()
        {
            Debug.Log($@"
╔══════════════════════════════════════════════════════════════════════════════════╗
║                    SAVED VEHICLE COMPARISON                                       ║
╠══════════════════════════════════════════════════════════════════════════════════╣
║  {comparison.VehicleAName,-25} vs {comparison.VehicleBName,-25}              ║
╠══════════════════════════════════════════════════════════════════════════════════╣
║  Parameter          │  Vehicle A    │  Vehicle B    │  Difference               ║
╠══════════════════════════════════════════════════════════════════════════════════╣
║  Length             │  {comparison.Length.ValueA,8:F1} mm │  {comparison.Length.ValueB,8:F1} mm │  {FormatDiff(comparison.Length.Difference),10}      ║
║  Width              │  {comparison.Width.ValueA,8:F1} mm │  {comparison.Width.ValueB,8:F1} mm │  {FormatDiff(comparison.Width.Difference),10}      ║
║  Height             │  {comparison.Height.ValueA,8:F1} mm │  {comparison.Height.ValueB,8:F1} mm │  {FormatDiff(comparison.Height.Difference),10}      ║
║  Wheelbase          │  {comparison.Wheelbase.ValueA,8:F1} mm │  {comparison.Wheelbase.ValueB,8:F1} mm │  {FormatDiff(comparison.Wheelbase.Difference),10}      ║
║  Front Track        │  {comparison.FrontTrack.ValueA,8:F1} mm │  {comparison.FrontTrack.ValueB,8:F1} mm │  {FormatDiff(comparison.FrontTrack.Difference),10}      ║
║  Rear Track         │  {comparison.RearTrack.ValueA,8:F1} mm │  {comparison.RearTrack.ValueB,8:F1} mm │  {FormatDiff(comparison.RearTrack.Difference),10}      ║
║  Ground Clearance   │  {comparison.GroundClearance.ValueA,8:F1} mm │  {comparison.GroundClearance.ValueB,8:F1} mm │  {FormatDiff(comparison.GroundClearance.Difference),10}      ║
╚══════════════════════════════════════════════════════════════════════════════════╝");
        }

        private string FormatDiff(float diff)
        {
            if (Mathf.Abs(diff) < 0.5f) return "—";
            string sign = diff > 0 ? "+" : "";
            return $"{sign}{diff:F0} mm";
        }
    }

    /// <summary>
    /// Comparison result for saved data
    /// </summary>
    [Serializable]
    public class SavedComparisonResult
    {
        public string VehicleAId;
        public string VehicleBId;
        public string VehicleAName;
        public string VehicleBName;
        public string ComparedDate;

        public SavedMeasurementComparison Length;
        public SavedMeasurementComparison Width;
        public SavedMeasurementComparison Height;
        public SavedMeasurementComparison Wheelbase;
        public SavedMeasurementComparison FrontOverhang;
        public SavedMeasurementComparison RearOverhang;
        public SavedMeasurementComparison FrontTrack;
        public SavedMeasurementComparison RearTrack;
        public SavedMeasurementComparison GroundClearance;
        public SavedMeasurementComparison FrontWheelDia;
        public SavedMeasurementComparison RearWheelDia;
    }

    /// <summary>
    /// Single measurement comparison
    /// </summary>
    [Serializable]
    public class SavedMeasurementComparison
    {
        public string Name;
        public string Code;
        public float ValueA;
        public float ValueB;
        public float Difference;
        public float PercentDifference;

        public bool AIsLarger => Difference > 0.5f;
        public bool BIsLarger => Difference < -0.5f;
        public bool AreEqual => Mathf.Abs(Difference) <= 0.5f;
    }
}
