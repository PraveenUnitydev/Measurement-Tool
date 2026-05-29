using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// VEHICLE PREFAB DATA
    /// 
    /// Attach this to your vehicle prefab root to store all measurement configuration.
    /// When MeasurementController loads this vehicle, it will use this data automatically.
    /// 
    /// SETUP:
    /// 1. Import your vehicle model
    /// 2. Add this component to the ROOT of the vehicle
    /// 3. Assign the references (tyres mesh, body mesh, etc.)
    /// 4. Save as prefab
    /// 5. Now it will work automatically when loaded!
    /// 
    /// BENEFITS:
    /// - Configure once, use forever
    /// - No manual assignment needed at runtime
    /// - Works with Addressables, Resources, or any load method
    /// - Stores wheel positions if pre-configured
    /// </summary>
    [DisallowMultipleComponent]
    public class VehiclePrefabData : MonoBehaviour
    {
        [Header("═══ VEHICLE INFO ═══")]
        [Tooltip("Display name for this vehicle")]
        public string vehicleName;

        [Tooltip("Manufacturer (BMW, Audi, etc.)")]
        public string manufacturer;

        [Tooltip("Category (SUV, Sedan, Truck, etc.)")]
        public string category;

        [Tooltip("Model year")]
        public string modelYear;

        [Header("═══ REQUIRED REFERENCES ═══")]
        [Tooltip("The mesh containing the tyres/wheels (REQUIRED for wheel detection)")]
        public Transform tyresMesh;

        [Tooltip("The root transform containing all meshes to analyze (usually this object or a child)")]
        public Transform meshRoot;

        [Header("═══ OPTIONAL MESH FILTERS ═══")]
        [Tooltip("Body mesh for body-specific measurements")]
        public Transform bodyMesh;

        [Tooltip("List of meshes to EXCLUDE from bounding box calculation (mirrors, antennas, etc.)")]
        public List<Transform> excludeFromBounds = new List<Transform>();

        [Tooltip("List of meshes to INCLUDE in bounding box (if empty, uses all)")]
        public List<Transform> includeInBounds = new List<Transform>();

        [Header("═══ WHEEL CONFIGURATION ═══")]
        [Tooltip("If true, use manually assigned wheel transforms instead of auto-detection")]
        public bool useManualWheelPositions = false;

        [Tooltip("Front Left wheel center transform")]
        public Transform wheelFL;

        [Tooltip("Front Right wheel center transform")]
        public Transform wheelFR;

        [Tooltip("Rear Left wheel center transform")]
        public Transform wheelRL;

        [Tooltip("Rear Right wheel center transform")]
        public Transform wheelRR;

        [Header("═══ WHEEL DETECTION SETTINGS ═══")]
        [Tooltip("Minimum wheel radius for auto-detection (in model units)")]
        public float minWheelRadius = 0.25f;

        [Tooltip("Maximum wheel radius for auto-detection (in model units)")]
        public float maxWheelRadius = 0.45f;

        [Header("═══ UNIT SETTINGS ═══")]
        [Tooltip("Are the model units in millimeters? (CATIA exports are usually in mm)")]
        public bool unitsAreMillimeters = false;

        [Header("═══ PRE-CALCULATED VALUES (Optional) ═══")]
        [Tooltip("If you've already measured this vehicle, store values here")]
        public bool usePreCalculatedValues = false;

        [Header("═══ ARCHITECTURE DATA ═══")]
        [Tooltip("Indicates whether Vehicle Architecture Layout (VAL) data is available for this vehicle")]
        public bool hasVALData = true;

        [Header("─── REFERENCE POSITIONS (optional) ───")]
        [Tooltip("Body Origin Front reference locator on the vehicle (world-space taken from this transform)")]

        public Transform refBOF;

        [Tooltip("Seating Reference Point reference locator on the vehicle")]
        public Transform refSGRP;

        [Tooltip("Wheel Center reference locator (e.g., choose the specific wheel center you care about)")]
        public Transform refWheelCenter;



        public PreCalculatedMeasurements preCalculatedValues;

        [Header("═══ NOTES ═══")]
        [TextArea(3, 5)]
        public string notes;

        /// <summary>
        /// Get the mesh root (defaults to this transform if not set)
        /// </summary>
        public Transform GetMeshRoot()
        {
            return meshRoot != null ? meshRoot : transform;
        }

        /// <summary>
        /// Get the tyres mesh
        /// </summary>
        public Transform GetTyresMesh()
        {
            if (tyresMesh != null)
                return tyresMesh;

            // Fallback: try to find by tag
            var tagged = FindChildWithTag(transform, "Tyres");
            if (tagged != null)
                return tagged;

            // Fallback: try to find by name
            return FindChildByName(transform, new[] { "tyre", "tire", "wheel", "rim" });
        }
        // Returns a Transform representing the anchor. If a dedicated locator exists, we return it.
        // Otherwise, we create (or update) a hidden child "RuntimeAnchor_[Anchor]" under this vehicle
        // and place it at the computed fallback position. This gives us both position & orientation handles.
        public Transform GetAnchorTransform(AlignmentAnchor anchor)
        {
            Transform t = null;
            switch (anchor)
            {
                case AlignmentAnchor.BOF: t = refBOF; break;
                case AlignmentAnchor.SGRP: t = refSGRP; break;
                case AlignmentAnchor.WheelCenter: t = refWheelCenter; break;
            }

            if (t != null) return t;

            // Build or find a runtime child to host fallback positions
            string childName = $"RuntimeAnchor_{anchor}";
            var host = transform.Find(childName);
            if (host == null)
            {
                host = new GameObject(childName).transform;
                host.SetParent(transform, worldPositionStays: false);
            }

            // Position the runtime anchor using the same logic as TryGetAnchorPosition (but now we also set orientation)
            if (TryGetAnchorPosition(anchor, out var worldPos))
            {
                host.position = worldPos;
                // Orientation heuristic: face vehicle's forward; up stays world up
                // (You can change to match your project's convention.)
                host.rotation = transform.rotation;
                return host;
            }

            // Ultimate fallback: renderer bounds center on this vehicle
            host.position = transform.position;
            host.rotation = transform.rotation;
            return host;
        }


        // VehiclePrefabData.cs
        public bool TryGetAnchorPosition(AlignmentAnchor anchor, out Vector3 worldPos)
        {
            // Always initialize out parameter
            worldPos = default;

            switch (anchor)
            {
                case AlignmentAnchor.BOF:
                    if (refBOF != null)
                    {
                        worldPos = refBOF.position;
                        return true;
                    }
                    break;

                case AlignmentAnchor.SGRP:
                    if (refSGRP != null)
                    {
                        worldPos = refSGRP.position;
                        return true;
                    }
                    break;

                case AlignmentAnchor.WheelCenter:
                    // Prefer explicit locator if assigned
                    if (refWheelCenter != null)
                    {
                        worldPos = refWheelCenter.position;
                        return true;
                    }
                    // Fallback: front axle center if front wheels are known
                    if (useManualWheelPositions && wheelFL != null && wheelFR != null)
                    {
                        worldPos = (wheelFL.position + wheelFR.position) * 0.5f;
                        return true;
                    }
                    break;

                case AlignmentAnchor.FrontAxleCenter:
                    if (useManualWheelPositions && wheelFL != null && wheelFR != null)
                    {
                        worldPos = (wheelFL.position + wheelFR.position) * 0.5f;
                        return true;
                    }
                    break;

                case AlignmentAnchor.WheelbaseCenter:
                    if (useManualWheelPositions && wheelFL != null && wheelFR != null &&
                        wheelRL != null && wheelRR != null)
                    {
                        Vector3 front = (wheelFL.position + wheelFR.position) * 0.5f;
                        Vector3 rear = (wheelRL.position + wheelRR.position) * 0.5f;
                        worldPos = (front + rear) * 0.5f;
                        return true;
                    }
                    break;

                case AlignmentAnchor.BoundsCenter:
                    worldPos = GetRendererBoundsCenter();
                    return true;
            }

            // Final, safe fallback so we ALWAYS return a value
            worldPos = GetRendererBoundsCenter();
            return true;
        }

        /// <summary>
        /// Returns the combined renderers' bounds center in WORLD space.
        /// </summary>
        private Vector3 GetRendererBoundsCenter()
        {
            var root = GetMeshRoot() != null ? GetMeshRoot() : transform;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return root.position;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            return b.center;
        }

/*#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Quick visual to verify locators
            DrawLocatorGizmo(refBOF, Color.cyan, "BOF");
            DrawLocatorGizmo(refSGRP, new Color(1f, 0.8f, 0.2f), "SGRP");
            DrawLocatorGizmo(refWheelCenter, Color.magenta, "WheelCenter");
        }
        private void DrawLocatorGizmo(Transform t, Color c, string label)
        {
            if (t == null) return;
            Gizmos.color = c;
            Gizmos.DrawSphere(t.position, HandleUtility.GetHandleSize(t.position) * 0.02f);
            UnityEditor.Handles.color = c;
            UnityEditor.Handles.Label(t.position, $" {label}");
        }
#endif*/

 

        /// 

        public bool IsConfigured()
        {
            return tyresMesh != null || FindChildWithTag(transform, "Tyres") != null;
        }

        /// <summary>
        /// Get validation status message
        /// </summary>
        public string GetValidationStatus()
        {
            var issues = new List<string>();

            if (tyresMesh == null)
            {
                var tagged = FindChildWithTag(transform, "Tyres");
                if (tagged == null)
                    issues.Add("Tyres mesh not assigned");
            }

            if (useManualWheelPositions)
            {
                if (wheelFL == null) issues.Add("Wheel FL not assigned");
                if (wheelFR == null) issues.Add("Wheel FR not assigned");
                if (wheelRL == null) issues.Add("Wheel RL not assigned");
                if (wheelRR == null) issues.Add("Wheel RR not assigned");
            }

            if (issues.Count == 0)
                return "✓ Configured";

            return "⚠ " + string.Join(", ", issues);
        }

        #region Helper Methods

        private Transform FindChildWithTag(Transform parent, string tag)
        {
            foreach (Transform child in parent)
            {
                if (child.CompareTag(tag))
                    return child;

                var found = FindChildWithTag(child, tag);
                if (found != null)
                    return found;
            }
            return null;
        }

        private Transform FindChildByName(Transform parent, string[] keywords)
        {
            foreach (Transform child in parent)
            {
                string nameLower = child.name.ToLower();
                foreach (var keyword in keywords)
                {
                    if (nameLower.Contains(keyword))
                        return child;
                }

                var found = FindChildByName(child, keywords);
                if (found != null)
                    return found;
            }
            return null;
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        [ContextMenu("Auto-Find Tyres Mesh")]
        private void AutoFindTyresMesh()
        {
            // Try tag first
            tyresMesh = FindChildWithTag(transform, "Tyres");

            // Try name search
            if (tyresMesh == null)
                tyresMesh = FindChildByName(transform, new[] { "tyre", "tire", "wheel", "rim" });

            if (tyresMesh != null)
                Debug.Log($"[VehiclePrefabData] Found tyres mesh: {tyresMesh.name}");
            else
                Debug.LogWarning("[VehiclePrefabData] Could not auto-find tyres mesh. Please assign manually.");

            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Auto-Detect Units")]
        private void AutoDetectUnits()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[VehiclePrefabData] No mesh renderers found");
                return;
            }

            Bounds bounds = new Bounds(renderers[0].bounds.center, Vector3.zero);
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);

            float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

            if (maxSize > 100f)
            {
                unitsAreMillimeters = true;
                minWheelRadius = 250f;
                maxWheelRadius = 450f;
                Debug.Log($"[VehiclePrefabData] Detected MILLIMETERS (max size: {maxSize:F1}mm)");
            }
            else
            {
                unitsAreMillimeters = false;
                minWheelRadius = 0.25f;
                maxWheelRadius = 0.45f;
                Debug.Log($"[VehiclePrefabData] Detected METERS (max size: {maxSize:F2}m)");
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Set Mesh Root to This")]
        private void SetMeshRootToThis()
        {
            meshRoot = transform;
            UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Validate Configuration")]
        private void ValidateConfiguration()
        {
            Debug.Log($"[VehiclePrefabData] {vehicleName}: {GetValidationStatus()}");
        }

        private void OnValidate()
        {
            // Auto-set vehicle name from GameObject name if empty
            if (string.IsNullOrEmpty(vehicleName))
                vehicleName = gameObject.name;
        }
#endif

        #endregion
    }

    public enum AlignmentAnchor
    {
        BOF,
        SGRP,
        WheelCenter,
        BoundsCenter,
        FrontAxleCenter,
        WheelbaseCenter,
        VCS
    }

    /// <summary>
    /// Pre-calculated measurement values (optional)
    /// </summary>
    [Serializable]
    public class PreCalculatedMeasurements
    {
        [Header("Length (mm)")]
        public float L103_OverallLength;
        public float L101_Wheelbase;
        public float L104_FrontOverhang;
        public float L105_RearOverhang;

        [Header("Width (mm)")]
        public float W103_OverallWidth;
        public float W144_FrontTrack;
        public float W145_RearTrack;

        [Header("Height (mm)")]
        public float H100_OverallHeight;
        public float H101_GroundClearance;

        [Header("Wheels (mm)")]
        public float TD_F_FrontDiameter;
        public float TD_R_RearDiameter;

        [Header("Wheel Centers (mm)")]
        public Vector3 WheelFL;
        public Vector3 WheelFR;
        public Vector3 WheelRL;
        public Vector3 WheelRR;
    }
}
