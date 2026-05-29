using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// ASYNC MEASUREMENT SYSTEM
    /// 
    /// Processes heavy mesh analysis without freezing the UI.
    /// Uses a combination of:
    /// - Coroutines for frame-spread processing
    /// - Background threads for heavy calculations
    /// - Progress callbacks for UI updates
    /// 
    /// USAGE:
    /// 
    /// // Instead of:
    /// measurementSystem.Analyze();  // This freezes UI
    /// 
    /// // Use:
    /// AsyncMeasurement.AnalyzeAsync(
    ///     measurementSystem,
    ///     onProgress: (progress, message) => UpdateProgressBar(progress, message),
    ///     onComplete: (results) => OnAnalysisComplete(results)
    /// );
    /// 
    /// </summary>
    public static class AsyncMeasurement
    {
        #region Public API
        
        /// <summary>
        /// Analyze vehicle asynchronously without freezing UI
        /// </summary>
        /// <param name="system">The measurement system to analyze</param>
        /// <param name="onProgress">Progress callback (0-1 progress, message)</param>
        /// <param name="onComplete">Completion callback with results</param>
        /// <param name="onError">Error callback</param>
        public static void AnalyzeAsync(
            VehicleMeasurementSystem system,
            Action<float, string> onProgress = null,
            Action<MeasurementResults> onComplete = null,
            Action<string> onError = null)
        {
            if (system == null)
            {
                onError?.Invoke("Measurement system is null");
                return;
            }
            
            AsyncMeasurementHelper.Instance.StartCoroutine(
                AnalyzeCoroutine(system, onProgress, onComplete, onError)
            );
        }
        
        /// <summary>
        /// Cancel any ongoing analysis
        /// </summary>
        public static void CancelAnalysis()
        {
            _cancellationRequested = true;
        }
        
        private static bool _cancellationRequested = false;
        
        #endregion
        
        #region Main Analysis Coroutine
        
        private static IEnumerator AnalyzeCoroutine(
            VehicleMeasurementSystem system,
            Action<float, string> onProgress,
            Action<MeasurementResults> onComplete,
            Action<string> onError)
        {
            _cancellationRequested = false;
            
            // Validate inputs
            onProgress?.Invoke(0f, "Validating...");
            yield return null;
            
            if (system.vehicleRoot == null)
            {
                onError?.Invoke("Vehicle root not assigned!");
                yield break;
            }
            
            // Auto-find tyres mesh if not set
            if (system.tyresMesh == null)
            {
                onProgress?.Invoke(0.05f, "Finding tyres mesh...");
                system.tyresMesh = FindTyresMesh(system.vehicleRoot);
                yield return null;
            }
            
            if (system.tyresMesh == null)
            {
                onError?.Invoke("Tyres mesh not found!");
                yield break;
            }
            
            // ════════════════════════════════════════════════════════════════
            // STEP 1: Collect mesh data (must be on main thread)
            // ════════════════════════════════════════════════════════════════
            onProgress?.Invoke(0.1f, "Collecting mesh data...");
            yield return null;
            
            var meshData = new MeshDataCollection();
            yield return CollectMeshDataCoroutine(system, meshData, onProgress);
            
            if (_cancellationRequested) yield break;
            
            // ════════════════════════════════════════════════════════════════
            // STEP 2: Calculate bounds (spread across frames)
            // ════════════════════════════════════════════════════════════════
            onProgress?.Invoke(0.3f, "Calculating bounds...");
            yield return null;
            
            BoundsResult boundsResult = new BoundsResult();
            yield return CalculateBoundsCoroutine(meshData, system.excludeFromBounds, boundsResult, onProgress);
            
            if (_cancellationRequested) yield break;
            
            // ════════════════════════════════════════════════════════════════
            // STEP 3: Detect wheels (spread across frames)
            // ════════════════════════════════════════════════════════════════
            onProgress?.Invoke(0.6f, "Detecting wheels...");
            yield return null;
            
            List<WheelData> wheels = new List<WheelData>();
            yield return DetectWheelsCoroutine(meshData, system, wheels, onProgress);
            
            if (_cancellationRequested) yield break;
            
            // ════════════════════════════════════════════════════════════════
            // STEP 4: Calculate measurements (fast, no spread needed)
            // ════════════════════════════════════════════════════════════════
            onProgress?.Invoke(0.9f, "Calculating measurements...");
            yield return null;
            
            MeasurementResults results = CalculateMeasurements(system, boundsResult, wheels);
            
            // ════════════════════════════════════════════════════════════════
            // STEP 5: Apply results to system
            // ════════════════════════════════════════════════════════════════
            onProgress?.Invoke(0.95f, "Finalizing...");
            ApplyResultsToSystem(system, boundsResult, wheels, results);
            
            onProgress?.Invoke(1f, "Complete!");
            yield return null;
            
            onComplete?.Invoke(results);
        }
        
        #endregion
        
        #region Mesh Data Collection
        
        private class MeshDataCollection
        {
            public List<MeshVertexData> AllMeshes = new List<MeshVertexData>();
            public MeshVertexData TyresMesh;
        }
        
        private class MeshVertexData
        {
            public Transform Transform;
            public Vector3[] LocalVertices;
            public string Name;
            public bool ShouldExclude;
        }
        
        private static IEnumerator CollectMeshDataCoroutine(
            VehicleMeasurementSystem system,
            MeshDataCollection collection,
            Action<float, string> onProgress)
        {
            var meshFilters = system.vehicleRoot.GetComponentsInChildren<MeshFilter>(true);
            int total = meshFilters.Length;
            int processed = 0;
            
            foreach (var mf in meshFilters)
            {
                if (_cancellationRequested) yield break;
                
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                
                // Check if should exclude
                bool exclude = false;
                if (system.excludeFromBounds != null)
                {
                    foreach (var ex in system.excludeFromBounds)
                    {
                        if (ex != null && (mf.transform == ex || mf.transform.IsChildOf(ex)))
                        {
                            exclude = true;
                            break;
                        }
                    }
                }
                
                var data = new MeshVertexData
                {
                    Transform = mf.transform,
                    LocalVertices = mf.sharedMesh.vertices,
                    Name = mf.name,
                    ShouldExclude = exclude
                };
                
                collection.AllMeshes.Add(data);
                
                // Check if this is the tyres mesh
                if (mf.transform == system.tyresMesh || mf.transform.IsChildOf(system.tyresMesh))
                {
                    collection.TyresMesh = data;
                }
                
                processed++;
                
                // Yield every few meshes to keep UI responsive
                if (processed % 5 == 0)
                {
                    float progress = 0.1f + (0.2f * processed / total);
                    onProgress?.Invoke(progress, $"Collecting mesh {processed}/{total}...");
                    yield return null;
                }
            }
        }
        
        #endregion
        
        #region Bounds Calculation
        
        private class BoundsResult
        {
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Center;
        }
        
        private static IEnumerator CalculateBoundsCoroutine(
            MeshDataCollection meshData,
            Transform[] excludeFromBounds,
            BoundsResult result,
            Action<float, string> onProgress)
        {
            bool first = true;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            
            int totalMeshes = meshData.AllMeshes.Count;
            int processedMeshes = 0;
            int verticesProcessed = 0;
            const int VERTICES_PER_FRAME = 5000; // Process this many vertices per frame
            
            foreach (var mesh in meshData.AllMeshes)
            {
                if (_cancellationRequested) yield break;
                if (mesh.ShouldExclude) continue;
                
                for (int i = 0; i < mesh.LocalVertices.Length; i++)
                {
                    Vector3 world = mesh.Transform.TransformPoint(mesh.LocalVertices[i]);
                    
                    if (first)
                    {
                        min = max = world;
                        first = false;
                    }
                    else
                    {
                        min = Vector3.Min(min, world);
                        max = Vector3.Max(max, world);
                    }
                    
                    verticesProcessed++;
                    
                    // Yield periodically
                    if (verticesProcessed % VERTICES_PER_FRAME == 0)
                    {
                        float progress = 0.3f + (0.3f * processedMeshes / totalMeshes);
                        onProgress?.Invoke(progress, $"Processing vertices... ({verticesProcessed})");
                        yield return null;
                    }
                }
                
                processedMeshes++;
            }
            
            result.Min = min;
            result.Max = max;
            result.Center = (min + max) / 2f;
        }
        
        #endregion
        
        #region Wheel Detection
        
        private class WheelData
        {
            public string Name;
            public Vector3 Center;
            public float Radius;
        }
        
        private static IEnumerator DetectWheelsCoroutine(
            MeshDataCollection meshData,
            VehicleMeasurementSystem system,
            List<WheelData> wheels,
            Action<float, string> onProgress)
        {
            if (meshData.TyresMesh == null)
            {
                Debug.LogWarning("[AsyncMeasurement] Tyres mesh data not found");
                yield break;
            }
            
            onProgress?.Invoke(0.6f, "Analyzing tyre vertices...");
            yield return null;
            
            // Transform vertices to world space (spread across frames)
            List<Vector3> tyreVerts = new List<Vector3>();
            var localVerts = meshData.TyresMesh.LocalVertices;
            var transform = meshData.TyresMesh.Transform;
            
            const int BATCH_SIZE = 3000;
            for (int i = 0; i < localVerts.Length; i += BATCH_SIZE)
            {
                if (_cancellationRequested) yield break;
                
                int end = Mathf.Min(i + BATCH_SIZE, localVerts.Length);
                for (int j = i; j < end; j++)
                {
                    tyreVerts.Add(transform.TransformPoint(localVerts[j]));
                }
                
                float progress = 0.6f + (0.15f * i / localVerts.Length);
                onProgress?.Invoke(progress, $"Processing tyre vertices... {i}/{localVerts.Length}");
                yield return null;
            }
            
            if (tyreVerts.Count < 100)
            {
                Debug.LogWarning("[AsyncMeasurement] Not enough tyre vertices");
                yield break;
            }
            
            onProgress?.Invoke(0.75f, "Detecting wheel positions...");
            yield return null;
            
            // Find tyre bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            
            foreach (var v in tyreVerts)
            {
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.z < minZ) minZ = v.z;
                if (v.z > maxZ) maxZ = v.z;
            }
            
            float centerX = (minX + maxX) / 2f;
            float centerZ = (minZ + maxZ) / 2f;
            
            // Split into quadrants
            var FL = tyreVerts.Where(v => v.x < centerX && v.z > centerZ).ToList();
            var FR = tyreVerts.Where(v => v.x > centerX && v.z > centerZ).ToList();
            var RL = tyreVerts.Where(v => v.x < centerX && v.z < centerZ).ToList();
            var RR = tyreVerts.Where(v => v.x > centerX && v.z < centerZ).ToList();
            
            yield return null;
            
            // Fit wheels
            onProgress?.Invoke(0.8f, "Fitting wheel circles...");
            
            FitWheel(FL, "FL", system, wheels);
            yield return null;
            
            FitWheel(FR, "FR", system, wheels);
            yield return null;
            
            FitWheel(RL, "RL", system, wheels);
            yield return null;
            
            FitWheel(RR, "RR", system, wheels);
            yield return null;
            
            onProgress?.Invoke(0.85f, $"Detected {wheels.Count} wheels");
        }
        
        private static void FitWheel(List<Vector3> verts, string name, VehicleMeasurementSystem system, List<WheelData> wheels)
        {
            if (verts.Count < 20) return;
            
            float minX = verts.Min(v => v.x);
            float maxX = verts.Max(v => v.x);
            float minY = verts.Min(v => v.y);
            float maxY = verts.Max(v => v.y);
            float minZ = verts.Min(v => v.z);
            float maxZ = verts.Max(v => v.z);
            
            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;
            float centerZ = (minZ + maxZ) / 2f;
            
            float radius = (maxY - minY) / 2f;
            
            // Validate radius
            float minR = system.minWheelRadius;
            float maxR = system.maxWheelRadius;
            
            if (radius < minR || radius > maxR)
            {
                Debug.LogWarning($"[AsyncMeasurement] {name} wheel radius {radius} out of range [{minR}, {maxR}]");
                return;
            }
            
            wheels.Add(new WheelData
            {
                Name = name,
                Center = new Vector3(centerX, centerY, centerZ),
                Radius = radius
            });
        }

        #endregion

        #region Measurements Calculation



        // AsyncMeasurementSystem.cs

        private static MeasurementResults CalculateMeasurements(
            VehicleMeasurementSystem system,
            BoundsResult bounds,
            List<WheelData> wheels)
        {
            // Contract:
            // - Results.* (positions + scalar lengths) -> METERS
            // - System's public fields will be filled in MM inside ApplyResultsToSystem

            bool modelIsMm = system.unitsAreMillimeters;

            // Explicit helpers to avoid overload confusion
            float ToMetersF(float v) => modelIsMm ? v / 1000f : v;
            Vector3 ToMetersV3(Vector3 v) => modelIsMm ? v / 1000f : v;

            var results = new MeasurementResults();

            // ---- Positions (meters)
            Vector3 bMinM = ToMetersV3(bounds.Min);
            Vector3 bMaxM = ToMetersV3(bounds.Max);
            Vector3 bCtrM = ToMetersV3(bounds.Center);

            results.BoundingMin = bMinM;
            results.BoundingMax = bMaxM;
            results.BoundingCenter = bCtrM;
            results.GroundLevel = bMinM.y;

            // ---- Bounding-based scalars (meters)
            float lengthM = ToMetersF(bounds.Max.z - bounds.Min.z);
            float widthM = ToMetersF(bounds.Max.x - bounds.Min.x);
            float heightM = ToMetersF(bounds.Max.y - bounds.Min.y);

            results.L103_OverallLength = lengthM;
            results.W103_OverallWidth = widthM;
            results.H100_OverallHeight = heightM;

            // ---- Wheel-based (meters)
            var FL = wheels.FirstOrDefault(w => w.Name == "FL");
            var FR = wheels.FirstOrDefault(w => w.Name == "FR");
            var RL = wheels.FirstOrDefault(w => w.Name == "RL");
            var RR = wheels.FirstOrDefault(w => w.Name == "RR");

            if (FL != null && FR != null && RL != null && RR != null)
            {
                // Centers & radii -> meters
                Vector3 cFL = ToMetersV3(FL.Center);
                Vector3 cFR = ToMetersV3(FR.Center);
                Vector3 cRL = ToMetersV3(RL.Center);
                Vector3 cRR = ToMetersV3(RR.Center);

                float rFL = ToMetersF(FL.Radius);
                float rFR = ToMetersF(FR.Radius);
                float rRL = ToMetersF(RL.Radius);
                float rRR = ToMetersF(RR.Radius);

                float frontAxleZ = (cFL.z + cFR.z) / 2f;
                float rearAxleZ = (cRL.z + cRR.z) / 2f;

                // Lengths (meters)
                results.L101_Wheelbase = Mathf.Abs(frontAxleZ - rearAxleZ);
                results.L104_FrontOverhang = (bMaxM.z - frontAxleZ);
                results.L105_RearOverhang = (rearAxleZ - bMinM.z);

                // Tracks (meters)
                results.W144_FrontTrackWidth = Mathf.Abs(cFL.x - cFR.x);
                results.W145_RearTrackWidth = Mathf.Abs(cRL.x - cRR.x);

                // Radii (meters)
                results.FrontWheelRadius = rFL;
                results.RearWheelRadius = rRL;

                // Ground-clearance (meters)
                float lowestWheelY = Mathf.Min(
                    cFL.y - rFL,
                    cFR.y - rFR,
                    cRL.y - rRL,
                    cRR.y - rRR
                );
                float gcM = bMinM.y - lowestWheelY;
                if (gcM < 0f) gcM = bMinM.y * 0.05f; // simple estimate
                results.H101_GroundClearance = gcM;

                // Wheel centers (meters)
                results.WheelCenter_FL = cFL;
                results.WheelCenter_FR = cFR;
                results.WheelCenter_RL = cRL;
                results.WheelCenter_RR = cRR;
            }

            return results;
        }




        // AsyncMeasurementSystem.cs

        private static void ApplyResultsToSystem(
            VehicleMeasurementSystem system,
            BoundsResult bounds,
            List<WheelData> wheels,
            MeasurementResults results)
        {
            // Convert meters -> millimeters for system public fields
            float MetersToMillimetersF(float m) => m * 1000f;
            Vector3 MetersToMillimetersV3(Vector3 m) => m * 1000f;

            // Scalars (mm)
            system.L103_OverallLength = MetersToMillimetersF(results.L103_OverallLength);
            system.L101_Wheelbase = MetersToMillimetersF(results.L101_Wheelbase);
            system.L104_FrontOverhang = MetersToMillimetersF(results.L104_FrontOverhang);
            system.L105_RearOverhang = MetersToMillimetersF(results.L105_RearOverhang);
            system.W103_OverallWidth = MetersToMillimetersF(results.W103_OverallWidth);
            system.W144_FrontTrack = MetersToMillimetersF(results.W144_FrontTrackWidth);
            system.W145_RearTrack = MetersToMillimetersF(results.W145_RearTrackWidth);
            system.H100_OverallHeight = MetersToMillimetersF(results.H100_OverallHeight);
            system.H101_GroundClearance = MetersToMillimetersF(results.H101_GroundClearance);

            // Wheel diameters (mm)
            system.TD_F_FrontDiameter = MetersToMillimetersF(results.FrontWheelRadius * 2f);
            system.TD_R_RearDiameter = MetersToMillimetersF(results.RearWheelRadius * 2f);

            // Wheel centers (mm)
            system.WheelFL = MetersToMillimetersV3(results.WheelCenter_FL);
            system.WheelFR = MetersToMillimetersV3(results.WheelCenter_FR);
            system.WheelRL = MetersToMillimetersV3(results.WheelCenter_RL);
            system.WheelRR = MetersToMillimetersV3(results.WheelCenter_RR);

            // Set Results (meters) back onto the system
            var resultsProp = system.GetType().GetProperty("Results");
            if (resultsProp != null && resultsProp.CanWrite)
            {
                resultsProp.SetValue(system, results);
            }
        }



        #endregion

        #region Helpers

        private static Transform FindTyresMesh(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToUpper();
                if (n == "TYRES" || n == "TIRES" || n == "TYRE" || n == "TIRE" || n.Contains("WHEEL"))
                {
                    var mf = t.GetComponent<MeshFilter>() ?? t.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
                        return mf.transform;
                }
            }
            return null;
        }
        
        #endregion
    }
    
    #region Helper MonoBehaviour
    
    internal class AsyncMeasurementHelper : MonoBehaviour
    {
        private static AsyncMeasurementHelper _instance;
        
        public static AsyncMeasurementHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("AsyncMeasurementHelper");
                    _instance = go.AddComponent<AsyncMeasurementHelper>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
    }
    
    #endregion
}
