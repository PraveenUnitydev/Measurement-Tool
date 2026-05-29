using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Vehicle Measurement System - FINAL VERSION
    /// For CATIA models with combined tyre mesh
    /// 
    /// IMPORTANT: Set 'unitsAreMillimeters' based on your model:
    /// - If model shows as ~4000 units in Unity = millimeters (set TRUE)
    /// - If model shows as ~4 units in Unity = meters (set FALSE)
    /// </summary>
    public class VehicleMeasurementSystem : MonoBehaviour
    {
        [Header("═══ REFERENCES ═══")]
        [Tooltip("Root of the vehicle")]
        public Transform vehicleRoot;

        [Tooltip("The TYRES mesh (single mesh containing all 4 tyres)")]
        public Transform tyresMesh;

        [Header("═══ UNITS ═══")]
        [Tooltip("TRUE if your model coordinates are in millimeters (values ~4000)\nFALSE if in meters (values ~4)")]
        public bool unitsAreMillimeters = true;

        [Header("═══ WHEEL RADIUS (in same units as model) ═══")]
        [Tooltip("Minimum wheel radius - 250 for mm, 0.25 for m")]
        public float minWheelRadius = 250f;
        [Tooltip("Maximum wheel radius - 450 for mm, 0.45 for m")]
        public float maxWheelRadius = 450f;

        [Header("═══ EXCLUDE FROM BOUNDS ═══")]
        [Tooltip("Drag GameObjects/Meshes here to exclude from overall dimensions (e.g., spare wheel)")]
        public Transform[] excludeFromBounds;

        [Header("═══ RESULTS (always in mm) ═══")]
        public float L103_OverallLength;
        public float L101_Wheelbase;
        public float L104_FrontOverhang;
        public float L105_RearOverhang;
        public float W103_OverallWidth;
        public float W144_FrontTrack;
        public float W145_RearTrack;
        public float H100_OverallHeight;
        public float H101_GroundClearance;
        public float TD_F_FrontDiameter;
        public float TD_R_RearDiameter;

        [Header("═══ WHEEL CENTERS (in mm) ═══")]
        public Vector3 WheelFL;
        public Vector3 WheelFR;
        public Vector3 WheelRL;
        public Vector3 WheelRR;

        [Header("═══ DEBUG ═══")]
        public bool showGizmos = true;

        // Internal - stored in original units
        private Vector3 _boundsMin;
        private Vector3 _boundsMax;
        private Vector3 _boundsCenter;
        private List<WheelInfo> _wheels = new List<WheelInfo>();
        private bool _analyzed = false;

        // For UI compatibility
        public MeasurementResults Results { get; private set; }
        public bool IsAnalyzed => _analyzed;

        private float ToMM(float value) => unitsAreMillimeters ? value : value * 1000f;
        private Vector3 ToMM(Vector3 v) => unitsAreMillimeters ? v : v * 1000f;

        [ContextMenu("Analyze Vehicle")]
        public MeasurementResults Analyze()
        {
            _wheels.Clear();
            _analyzed = false;
            Results = new MeasurementResults();

            if (vehicleRoot == null)
            {
                Debug.LogError("[Measure] Assign vehicleRoot!");
                return Results;
            }

            // Auto-find tyres mesh
            if (tyresMesh == null)
            {
                tyresMesh = FindTyresMesh();
            }

            if (tyresMesh == null)
            {
                Debug.LogError("[Measure] Assign tyresMesh!");
                return Results;
            }

            // 1. Calculate vehicle bounds
            CalculateVehicleBounds();

            // 2. Detect 4 wheels
            DetectWheels();

            // 3. Calculate measurements
            CalculateMeasurements();

            // 4. Fill Results for UI
            FillResults();

            _analyzed = true;
            LogResults();

            return Results;
        }

        private Transform FindTyresMesh()
        {
            foreach (var t in vehicleRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToUpper();
                if (n == "TYRES" || n == "TIRES" || n == "TYRE" || n == "TIRE")
                {
                    var mf = t.GetComponent<MeshFilter>() ?? t.GetComponentInChildren<MeshFilter>();
                    if (mf != null) return mf.transform;
                }
            }
            return null;
        }

        private void CalculateVehicleBounds()
        {
            var meshFilters = vehicleRoot.GetComponentsInChildren<MeshFilter>(true);

            bool first = true;
            _boundsMin = Vector3.zero;
            _boundsMax = Vector3.zero;
            int excludedCount = 0;

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;

                // Check if this mesh should be excluded
                bool shouldExclude = false;
                if (excludeFromBounds != null)
                {
                    foreach (var excluded in excludeFromBounds)
                    {
                        if (excluded == null) continue;

                        // Check if mesh is the excluded object or a child of it
                        if (mf.transform == excluded || mf.transform.IsChildOf(excluded))
                        {
                            shouldExclude = true;
                            excludedCount++;
                            Debug.Log($"[Measure] Excluding: {mf.name}");
                            break;
                        }
                    }
                }

                if (shouldExclude) continue;

                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 world = mf.transform.TransformPoint(v);

                    if (first)
                    {
                        _boundsMin = _boundsMax = world;
                        first = false;
                    }
                    else
                    {
                        _boundsMin = Vector3.Min(_boundsMin, world);
                        _boundsMax = Vector3.Max(_boundsMax, world);
                    }
                }
            }

            _boundsCenter = (_boundsMin + _boundsMax) / 2f;

            Debug.Log($"[Measure] Vehicle bounds: " +
                $"L={ToMM(_boundsMax.z - _boundsMin.z):F0}mm, " +
                $"W={ToMM(_boundsMax.x - _boundsMin.x):F0}mm, " +
                $"H={ToMM(_boundsMax.y - _boundsMin.y):F0}mm" +
                (excludedCount > 0 ? $" (excluded {excludedCount} meshes)" : ""));
        }

        private void DetectWheels()
        {
            var mf = tyresMesh.GetComponent<MeshFilter>() ?? tyresMesh.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
            {
                Debug.LogError("[Measure] Tyres mesh not readable!");
                return;
            }

            // Get all tyre vertices in world space
            var tyreVerts = new List<Vector3>();
            foreach (var v in mf.sharedMesh.vertices)
            {
                tyreVerts.Add(mf.transform.TransformPoint(v));
            }

            Debug.Log($"[Measure] Tyre vertices: {tyreVerts.Count}");

            // Find tyre bounds
            float tyreMinX = tyreVerts.Min(v => v.x);
            float tyreMaxX = tyreVerts.Max(v => v.x);
            float tyreMinZ = tyreVerts.Min(v => v.z);
            float tyreMaxZ = tyreVerts.Max(v => v.z);

            float centerX = (tyreMinX + tyreMaxX) / 2f;
            float centerZ = (tyreMinZ + tyreMaxZ) / 2f;

            Debug.Log($"[Measure] Tyre X range: {ToMM(tyreMinX):F0} to {ToMM(tyreMaxX):F0}mm");
            Debug.Log($"[Measure] Tyre Z range: {ToMM(tyreMinZ):F0} to {ToMM(tyreMaxZ):F0}mm");
            Debug.Log($"[Measure] Split at: X={ToMM(centerX):F0}mm, Z={ToMM(centerZ):F0}mm");

            // Split into quadrants
            var FL = tyreVerts.Where(v => v.x < centerX && v.z > centerZ).ToList();
            var FR = tyreVerts.Where(v => v.x > centerX && v.z > centerZ).ToList();
            var RL = tyreVerts.Where(v => v.x < centerX && v.z < centerZ).ToList();
            var RR = tyreVerts.Where(v => v.x > centerX && v.z < centerZ).ToList();

            Debug.Log($"[Measure] Quadrants: FL={FL.Count}, FR={FR.Count}, RL={RL.Count}, RR={RR.Count}");

            // Fit wheels
            FitWheel(FL, "FL");
            FitWheel(FR, "FR");
            FitWheel(RL, "RL");
            FitWheel(RR, "RR");

            Debug.Log($"[Measure] Detected {_wheels.Count} wheels");
        }

        private void FitWheel(List<Vector3> verts, string name)
        {
            if (verts.Count < 20)
            {
                Debug.LogWarning($"[Measure] {name}: Only {verts.Count} vertices");
                return;
            }

            // Simple bounding box approach for center and radius
            float minX = verts.Min(v => v.x);
            float maxX = verts.Max(v => v.x);
            float minY = verts.Min(v => v.y);
            float maxY = verts.Max(v => v.y);
            float minZ = verts.Min(v => v.z);
            float maxZ = verts.Max(v => v.z);

            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;
            float centerZ = (minZ + maxZ) / 2f;

            // Radius from Y extent (height of tyre = diameter)
            float radiusFromY = (maxY - minY) / 2f;
            // Radius from Z extent
            float radiusFromZ = (maxZ - minZ) / 2f;

            // Use the larger one (should be similar)
            float radius = Mathf.Max(radiusFromY, radiusFromZ);

            Debug.Log($"[Measure] {name}: Center=({ToMM(centerX):F0}, {ToMM(centerY):F0}, {ToMM(centerZ):F0})mm, " +
                     $"RadiusY={ToMM(radiusFromY):F0}mm, RadiusZ={ToMM(radiusFromZ):F0}mm");

            // Validate radius
            if (radius < minWheelRadius || radius > maxWheelRadius)
            {
                Debug.LogWarning($"[Measure] {name}: Radius {ToMM(radius):F0}mm outside range [{ToMM(minWheelRadius):F0}-{ToMM(maxWheelRadius):F0}mm]");
            }

            _wheels.Add(new WheelInfo
            {
                Name = name,
                Center = new Vector3(centerX, centerY, centerZ),
                Radius = radius
            });
        }

        private void CalculateMeasurements()
        {
            // Overall dimensions (convert to mm)
            L103_OverallLength = ToMM(_boundsMax.z - _boundsMin.z);
            W103_OverallWidth = ToMM(_boundsMax.x - _boundsMin.x);
            H100_OverallHeight = ToMM(_boundsMax.y - _boundsMin.y);

            var fl = _wheels.FirstOrDefault(w => w.Name == "FL");
            var fr = _wheels.FirstOrDefault(w => w.Name == "FR");
            var rl = _wheels.FirstOrDefault(w => w.Name == "RL");
            var rr = _wheels.FirstOrDefault(w => w.Name == "RR");

            if (fl != null && fr != null && rl != null && rr != null)
            {
                float frontAxleZ = (fl.Center.z + fr.Center.z) / 2f;
                float rearAxleZ = (rl.Center.z + rr.Center.z) / 2f;

                L101_Wheelbase = ToMM(Mathf.Abs(frontAxleZ - rearAxleZ));
                L104_FrontOverhang = ToMM(_boundsMax.z - frontAxleZ);
                L105_RearOverhang = ToMM(rearAxleZ - _boundsMin.z);

                W144_FrontTrack = ToMM(Mathf.Abs(fr.Center.x - fl.Center.x));
                W145_RearTrack = ToMM(Mathf.Abs(rr.Center.x - rl.Center.x));

                TD_F_FrontDiameter = ToMM((fl.Radius + fr.Radius));
                TD_R_RearDiameter = ToMM((rl.Radius + rr.Radius));

                WheelFL = ToMM(fl.Center);
                WheelFR = ToMM(fr.Center);
                WheelRL = ToMM(rl.Center);
                WheelRR = ToMM(rr.Center);
            }

            // Ground clearance
            H101_GroundClearance = CalculateGroundClearance();
        }

        private float CalculateGroundClearance()
        {
            float groundY = _boundsMin.y;
            float lowestBody = _boundsMax.y;

            var meshFilters = vehicleRoot.GetComponentsInChildren<MeshFilter>(true);

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                if (mf.transform == tyresMesh || mf.transform.IsChildOf(tyresMesh)) continue;

                // Skip if name contains wheel-related terms
                string n = mf.name.ToUpper();
                if (n.Contains("TYRE") || n.Contains("TIRE") || n.Contains("WHEEL") || n.Contains("ALLOY")) continue;

                foreach (var v in mf.sharedMesh.vertices)
                {
                    Vector3 world = mf.transform.TransformPoint(v);

                    // Skip vertices in wheel areas
                    bool inWheelArea = false;
                    foreach (var w in _wheels)
                    {
                        float distXZ = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(w.Center.x, w.Center.z));
                        if (distXZ < w.Radius * 1.3f)
                        {
                            inWheelArea = true;
                            break;
                        }
                    }

                    if (!inWheelArea && world.y < lowestBody)
                        lowestBody = world.y;
                }
            }

            return ToMM(lowestBody - groundY);
        }

        private void FillResults()
        {
            Results = new MeasurementResults
            {
                L103_OverallLength = L103_OverallLength / 1000f,
                L101_Wheelbase = L101_Wheelbase / 1000f,
                L104_FrontOverhang = L104_FrontOverhang / 1000f,
                L105_RearOverhang = L105_RearOverhang / 1000f,
                W103_OverallWidth = W103_OverallWidth / 1000f,
                W144_FrontTrackWidth = W144_FrontTrack / 1000f,
                W145_RearTrackWidth = W145_RearTrack / 1000f,
                H100_OverallHeight = H100_OverallHeight / 1000f,
                H101_GroundClearance = H101_GroundClearance / 1000f,
                FrontWheelRadius = TD_F_FrontDiameter / 2000f,
                RearWheelRadius = TD_R_RearDiameter / 2000f,
                WheelCenter_FL = WheelFL / 1000f,
                WheelCenter_FR = WheelFR / 1000f,
                WheelCenter_RL = WheelRL / 1000f,
                WheelCenter_RR = WheelRR / 1000f,
                BoundingMin = ToMM(_boundsMin) / 1000f,
                BoundingMax = ToMM(_boundsMax) / 1000f,
                BoundingCenter = ToMM(_boundsCenter) / 1000f,
                GroundLevel = ToMM(_boundsMin.y) / 1000f
            };
        }

        private void LogResults()
        {
            Debug.Log($@"
════════════════════════════════════════════════════════
              VEHICLE MEASUREMENTS (mm)
════════════════════════════════════════════════════════
L103 Overall Length:    {L103_OverallLength,8:F1}
L101 Wheelbase:         {L101_Wheelbase,8:F1}
L104 Front Overhang:    {L104_FrontOverhang,8:F1}
L105 Rear Overhang:     {L105_RearOverhang,8:F1}

W103 Overall Width:     {W103_OverallWidth,8:F1}
W144 Front Track:       {W144_FrontTrack,8:F1}
W145 Rear Track:        {W145_RearTrack,8:F1}

H100 Overall Height:    {H100_OverallHeight,8:F1}
H101 Ground Clearance:  {H101_GroundClearance,8:F1}

TD_F Front Diameter:    {TD_F_FrontDiameter,8:F1}
TD_R Rear Diameter:     {TD_R_RearDiameter,8:F1}
════════════════════════════════════════════════════════");
        }

        /// <summary>
        /// Set results from saved data (for dimension line rendering without re-analyze)
        /// </summary>
        public void SetResultsFromSavedData(SavedVehicleMeasurement savedData)
        {
            if (savedData == null) return;

            // Create results object
            Results = new MeasurementResults();

            // Convert mm values to meters for Results (DimensionLineRenderer expects meters in Results)
            Results.L103_OverallLength = savedData.L103_OverallLength / 1000f;
            Results.L101_Wheelbase = savedData.L101_Wheelbase / 1000f;
            Results.L104_FrontOverhang = savedData.L104_FrontOverhang / 1000f;
            Results.L105_RearOverhang = savedData.L105_RearOverhang / 1000f;

            Results.W103_OverallWidth = savedData.W103_OverallWidth / 1000f;
            Results.W144_FrontTrackWidth = savedData.W144_FrontTrack / 1000f;
            Results.W145_RearTrackWidth = savedData.W145_RearTrack / 1000f;

            Results.H100_OverallHeight = savedData.H100_OverallHeight / 1000f;
            Results.H101_GroundClearance = savedData.H101_GroundClearance / 1000f;

            Results.FrontWheelRadius = savedData.TD_F_FrontDiameter / 2000f;
            Results.RearWheelRadius = savedData.TD_R_RearDiameter / 2000f;

            // Wheel centers in Results (convert mm to meters)
            Results.WheelCenter_FL = savedData.WheelFL.ToVector3() / 1000f;
            Results.WheelCenter_FR = savedData.WheelFR.ToVector3() / 1000f;
            Results.WheelCenter_RL = savedData.WheelRL.ToVector3() / 1000f;
            Results.WheelCenter_RR = savedData.WheelRR.ToVector3() / 1000f;

            // ALSO set wheel centers on the system itself (in mm - as DimensionLineRenderer expects)
            WheelFL = savedData.WheelFL.ToVector3();
            WheelFR = savedData.WheelFR.ToVector3();
            WheelRL = savedData.WheelRL.ToVector3();
            WheelRR = savedData.WheelRR.ToVector3();

            // Bounds in Results (convert mm to meters)
            Vector3 boundsMin = savedData.BoundsMin.ToVector3() / 1000f;
            Vector3 boundsMax = savedData.BoundsMax.ToVector3() / 1000f;
            Vector3 boundsCenter = savedData.BoundsCenter.ToVector3() / 1000f;

            // If bounds are zero, calculate from measurements
            if (boundsMin == Vector3.zero && boundsMax == Vector3.zero)
            {
                Debug.LogWarning("[MeasurementSystem] Saved bounds are zero, calculating from measurements...");

                // Calculate bounds from wheel positions and dimensions
                float halfWidth = savedData.W103_OverallWidth / 2000f;
                float halfLength = savedData.L103_OverallLength / 2000f;
                float height = savedData.H100_OverallHeight / 1000f;

                // Estimate center from wheel positions
                if (WheelFL != Vector3.zero)
                {
                    Vector3 wheelCenter = (WheelFL + WheelFR + WheelRL + WheelRR) / 4f / 1000f;
                    boundsCenter = new Vector3(wheelCenter.x, height / 2f, wheelCenter.z);
                }

                boundsMin = new Vector3(boundsCenter.x - halfWidth, 0, boundsCenter.z - halfLength);
                boundsMax = new Vector3(boundsCenter.x + halfWidth, height, boundsCenter.z + halfLength);

                Debug.Log($"[MeasurementSystem] Calculated bounds: {boundsMin} to {boundsMax}");
            }

            Results.BoundingMin = boundsMin;
            Results.BoundingMax = boundsMax;
            Results.BoundingCenter = boundsCenter;

            // Also set internal bounds (in original units - for gizmos)
            _boundsMin = boundsMin * 1000f;
            _boundsMax = boundsMax * 1000f;
            _boundsCenter = boundsCenter * 1000f;

            // Ground level
            Results.GroundLevel = Results.BoundingMin.y;

            // Mark as analyzed - THIS IS CRITICAL
            _analyzed = true;

            Debug.Log($"[MeasurementSystem] Results set from saved data. Analyzed: {_analyzed}");
            Debug.Log($"[MeasurementSystem] Bounds: {Results.BoundingMin} to {Results.BoundingMax}");
            Debug.Log($"[MeasurementSystem] WheelFL: {WheelFL}, WheelFR: {WheelFR}");


            L103_OverallLength = savedData.L103_OverallLength;
            L101_Wheelbase = savedData.L101_Wheelbase;
            L104_FrontOverhang = savedData.L104_FrontOverhang;
            L105_RearOverhang=savedData.L105_RearOverhang;
            W103_OverallWidth=savedData.W103_OverallWidth;
            W144_FrontTrack = savedData.W144_FrontTrack;
            W103_OverallWidth=savedData.W103_OverallWidth;
            W145_RearTrack =savedData.W145_RearTrack;
            H100_OverallHeight = savedData.H100_OverallHeight;
            H101_GroundClearance = savedData.H101_GroundClearance;
            TD_F_FrontDiameter=savedData.TD_F_FrontDiameter;
            TD_R_RearDiameter = savedData.TD_R_RearDiameter;

        }

        /// <summary>
        /// Create Results from direct measurement values (for dimension lines when no saved data)
        /// Called by DimensionLineRenderer when Results is null but direct values exist
        /// </summary>
        public void CreateResultsFromDirectValues()
        {
            Results = new MeasurementResults();

            // Copy values (convert mm to meters for Results)
            Results.L103_OverallLength = L103_OverallLength / 1000f;
            Results.L101_Wheelbase = L101_Wheelbase / 1000f;
            Results.L104_FrontOverhang = L104_FrontOverhang / 1000f;
            Results.L105_RearOverhang = L105_RearOverhang / 1000f;

            Results.W103_OverallWidth = W103_OverallWidth / 1000f;
            Results.W144_FrontTrackWidth = W144_FrontTrack / 1000f;
            Results.W145_RearTrackWidth = W145_RearTrack / 1000f;

            Results.H100_OverallHeight = H100_OverallHeight / 1000f;
            Results.H101_GroundClearance = H101_GroundClearance / 1000f;

            Results.FrontWheelRadius = TD_F_FrontDiameter / 2000f;
            Results.RearWheelRadius = TD_R_RearDiameter / 2000f;

            // Wheel centers (convert mm to meters)
            Results.WheelCenter_FL = WheelFL / 1000f;
            Results.WheelCenter_FR = WheelFR / 1000f;
            Results.WheelCenter_RL = WheelRL / 1000f;
            Results.WheelCenter_RR = WheelRR / 1000f;

            // Calculate bounds from measurements
            float halfLength = L103_OverallLength / 2000f;
            float halfWidth = W103_OverallWidth / 2000f;
            float height = H100_OverallHeight / 1000f;

            Results.BoundingMin = new Vector3(-halfWidth, 0, -halfLength);
            Results.BoundingMax = new Vector3(halfWidth, height, halfLength);
            Results.BoundingCenter = new Vector3(0, height / 2f, 0);
            Results.GroundLevel = 0;

            // Mark as analyzed
            _analyzed = true;

            Debug.Log($"[MeasurementSystem] Created Results from direct values: L={L103_OverallLength}mm, W={W103_OverallWidth}mm, H={H100_OverallHeight}mm");
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || !_analyzed) return;

            // Yellow bounding box
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(_boundsCenter, _boundsMax - _boundsMin);

            // Wheels
            foreach (var w in _wheels)
            {
                Gizmos.color = Color.green;
                DrawCircle(w.Center, w.Radius);

                Gizmos.color = Color.red;
                float sphereSize = unitsAreMillimeters ? 20f : 0.02f;
                Gizmos.DrawSphere(w.Center, sphereSize);
            }
        }

        private void DrawCircle(Vector3 center, float radius)
        {
            for (int i = 0; i < 36; i++)
            {
                float a1 = i * Mathf.PI * 2f / 36f;
                float a2 = (i + 1) * Mathf.PI * 2f / 36f;
                Vector3 p1 = center + new Vector3(0, Mathf.Sin(a1), Mathf.Cos(a1)) * radius;
                Vector3 p2 = center + new Vector3(0, Mathf.Sin(a2), Mathf.Cos(a2)) * radius;
                Gizmos.DrawLine(p1, p2);
            }
        }

        private class WheelInfo
        {
            public string Name;
            public Vector3 Center;
            public float Radius;
        }
    }

    [Serializable]
    public class MeasurementResults
    {
        public Vector3 BoundingMin, BoundingMax, BoundingCenter;
        public float GroundLevel;
        public float L103_OverallLength, L101_Wheelbase, L104_FrontOverhang, L105_RearOverhang;
        public float W103_OverallWidth, W144_FrontTrackWidth, W145_RearTrackWidth;
        public float H100_OverallHeight, H101_GroundClearance;
        public float FrontWheelRadius, RearWheelRadius;
        public Vector3 WheelCenter_FL, WheelCenter_FR, WheelCenter_RL, WheelCenter_RR;
        public DetectedWheel[] Wheels;
    }

    [Serializable]
    public class DetectedWheel
    {
        public WheelPosition Position;
        public Vector3 Center;
        public float Radius;
    }

    public enum WheelPosition { FrontLeft, FrontRight, RearLeft, RearRight }
}
