
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Visual gizmos/overlays for AutoVehicleScanner to debug W103/W144/W145/W106.
/// Attach to the same GameObject as AutoVehicleScanner.
/// </summary>
[ExecuteAlways]
public class VehicleScannerGizmos : MonoBehaviour
{
    [Header("References")]
    public VehicleDimensionCalculator scanner;  // drag the existing scanner here

    [Header("Gizmo Toggles")]
    public bool drawAxes = true;
    public bool drawAABB = true;
    public bool drawScanGrid = true;
    public bool drawRayHits = true;
    public bool drawExtents = true;
    public bool drawLabels = true;
    public bool includeMirrorsForDebug = false; // visualize what W144 would scan
    public bool showW106Plane = false;

    [Header("Appearance")]
    public float axisLength = 2.0f;
    public float rayPreviewLength = 1.5f;       // length of arrows showing scan direction
    public float hitSphereRadius = 0.02f;
    public Color xColor = new Color(1f, 0.2f, 0.2f);
    public Color yColor = new Color(0.2f, 1f, 0.2f);
    public Color zColor = new Color(0.2f, 0.4f, 1f);
    public Color aabbColor = new Color(1f, 1f, 0f, 0.25f);
    public Color rayStartColor = new Color(1f, 0.5f, 0f, 0.9f);
    public Color rayDirColor = new Color(1f, 0.9f, 0.2f, 0.9f);
    public Color hitColor = new Color(0f, 0.9f, 1f, 1f);
    public Color extentColor = new Color(0.9f, 0f, 0.9f, 0.9f);
    public Color w106Color = new Color(0f, 1f, 1f, 0.75f);

    // Cache of last scan results to display
    private List<Vector3> lastHits = new List<Vector3>();
    private float lastMinY, lastMaxY, lastWidth;

    private void OnEnable()
    {
        if (!scanner) scanner = GetComponent<VehicleDimensionCalculator>();
    }

    private void Update()
    {
        // Refresh scan results even in Edit mode (thanks to ExecuteAlways)
        if (!scanner) return;
        if (!Application.isPlaying)
        {
            // Force scanner Prepare without changing runtime state
            // Private fields are internal; so perform a debug scan that mirrors W103/W144 view
            DebugScan(includeMirrorsForDebug);
        }
    }

    public void DebugScan(bool includeMirrors)
    {
        if (!scanner) return;

        // Build a mini debug layerMask and perform a reduced “grid only” scan, collecting hits.
        // We’ll replicate the grid iteration from scanner.ScanGrid, but capture hit points.

        // Ensure it’s prepared
        var prepMethod = scanner.GetType().GetMethod("Prepare", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        prepMethod?.Invoke(scanner, null);

        // Read private fields via reflection for gizmo display
        var originWS = (Vector3)scanner.GetType().GetField("originWS", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var xHat = (Vector3)scanner.GetType().GetField("xHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var yHat = (Vector3)scanner.GetType().GetField("yHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var zHat = (Vector3)scanner.GetType().GetField("zHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var vehicleAABB = (Bounds)scanner.GetType().GetField("vehicleAABB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);

        int samplesX = scanner.samplesX;
        int samplesZ = scanner.samplesZ;
        float lateralMargin = scanner.lateralMargin;
        float rayDistance = scanner.rayDistance;

        lastHits.Clear();
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;

        float xSpan = Vector3.Dot(vehicleAABB.size, xHat);
        float zSpan = Vector3.Dot(vehicleAABB.size, zHat);

        int layerMask = ~0; // Everything (same as scanner BuildLayerMask; you can specialize this)

        for (int i = 0; i < samplesX; i++)
        {
            float tX = (samplesX == 1) ? 0.5f : (float)i / (samplesX - 1);
            Vector3 xBase = originWS + xHat * ((tX - 0.5f) * xSpan);

            for (int j = 0; j < samplesZ; j++)
            {
                float tZ = (samplesZ == 1) ? 0.5f : (float)j / (samplesZ - 1);
                Vector3 basePoint = xBase + zHat * ((tZ - 0.5f) * zSpan);

                Vector3 leftStart = basePoint - yHat * (Mathf.Max(lateralMargin, vehicleAABB.extents.y + 0.5f));
                Vector3 rightStart = basePoint + yHat * (Mathf.Max(lateralMargin, vehicleAABB.extents.y + 0.5f));

                if (Physics.Raycast(leftStart, yHat, out var hitL, rayDistance, layerMask, QueryTriggerInteraction.Ignore))
                {
                    lastHits.Add(hitL.point);
                    float y = Vector3.Dot(hitL.point - originWS, yHat);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if (Physics.Raycast(rightStart, -yHat, out var hitR, rayDistance, layerMask, QueryTriggerInteraction.Ignore))
                {
                    lastHits.Add(hitR.point);
                    float y = Vector3.Dot(hitR.point - originWS, yHat);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        lastMinY = float.IsInfinity(minY) ? 0f : minY;
        lastMaxY = float.IsInfinity(maxY) ? 0f : maxY;
        lastWidth = (lastMaxY - lastMinY) * (scanner.outputInMillimeters ? 1000f : 1f);
    }

    private void OnDrawGizmos()
    {
        if (!scanner) return;

        // Fetch private fields for drawing
        var originWS = (Vector3)scanner.GetType().GetField("originWS", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var xHat = (Vector3)scanner.GetType().GetField("xHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var yHat = (Vector3)scanner.GetType().GetField("yHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var zHat = (Vector3)scanner.GetType().GetField("zHat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);
        var vehicleAABB = (Bounds)scanner.GetType().GetField("vehicleAABB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scanner);

        // 1) Axes
        if (drawAxes)
        {
            Gizmos.color = xColor; Gizmos.DrawLine(originWS, originWS + xHat * axisLength);
            Gizmos.color = yColor; Gizmos.DrawLine(originWS, originWS + yHat * axisLength);
            Gizmos.color = zColor; Gizmos.DrawLine(originWS, originWS + zHat * axisLength);
        }

        // 2) AABB
        if (drawAABB)
        {
            Gizmos.color = aabbColor;
            Gizmos.DrawWireCube(vehicleAABB.center, vehicleAABB.size);
        }

        // 3) Scan grid start arrows
        if (drawScanGrid)
        {
            Gizmos.color = rayStartColor;
            int samplesX = scanner.samplesX;
            int samplesZ = scanner.samplesZ;

            float xSpan = Vector3.Dot(vehicleAABB.size, xHat);
            float zSpan = Vector3.Dot(vehicleAABB.size, zHat);
            float lateral = Mathf.Max(scanner.lateralMargin, vehicleAABB.extents.y + 0.5f);

            for (int i = 0; i < samplesX; i++)
            {
                float tX = (samplesX == 1) ? 0.5f : (float)i / (samplesX - 1);
                Vector3 xBase = originWS + xHat * ((tX - 0.5f) * xSpan);

                for (int j = 0; j < samplesZ; j++)
                {
                    float tZ = (samplesZ == 1) ? 0.5f : (float)j / (samplesZ - 1);
                    Vector3 basePoint = xBase + zHat * ((tZ - 0.5f) * zSpan);

                    Vector3 leftStart = basePoint - yHat * lateral;
                    Vector3 rightStart = basePoint + yHat * lateral;

                    // Draw small arrows indicating direction
                    Gizmos.DrawLine(leftStart, leftStart + yHat * rayPreviewLength);
                    Gizmos.DrawLine(rightStart, rightStart - yHat * rayPreviewLength);
                }
            }
        }


        // Draw A & B points clearly:
        if (drawExtents && lastHits.Count > 0)
        {
            Gizmos.color = Color.magenta;

            Vector3 A = originWS + yHat * lastMinY;
            Vector3 B = originWS + yHat * lastMaxY;

            // Draw spheres
            Gizmos.DrawSphere(A, hitSphereRadius * 2f);
            Gizmos.DrawSphere(B, hitSphereRadius * 2f);

#if UNITY_EDITOR
            Handles.color = Color.yellow;
            Handles.Label(A + zHat * 0.1f, "A (Leftmost)");
            Handles.Label(B + zHat * 0.1f, "B (Rightmost)");
#endif
        }


        // 4) Hit points
        if (drawRayHits && lastHits.Count > 0)
        {
            Gizmos.color = hitColor;
            foreach (var p in lastHits)
                Gizmos.DrawSphere(p, hitSphereRadius);
        }

        // 5) Extent lines and label
        if (drawExtents && lastHits.Count > 0)
        {
            Gizmos.color = extentColor;
            // Build points at min/max lateral along the scan originWS for visualization
            Vector3 minPt = originWS + yHat * lastMinY;
            Vector3 maxPt = originWS + yHat * lastMaxY;

            // Draw a thick-ish line (two parallel lines) for readability
            Gizmos.DrawLine(minPt + zHat * 0.05f, maxPt + zHat * 0.05f);
            Gizmos.DrawLine(minPt - zHat * 0.05f, maxPt - zHat * 0.05f);

#if UNITY_EDITOR
            if (drawLabels)
            {
                string units = scanner.outputInMillimeters ? "mm" : "m";
                Handles.color = Color.white;
                Handles.Label((minPt + maxPt) * 0.5f + zHat * 0.15f,
                    $"W{(includeMirrorsForDebug ? "144" : "103")} ≈ {lastWidth:F1} {units}\n" +
                    $"minY: {lastMinY:F3}, maxY: {lastMaxY:F3}");


                //string units = scanner.outputInMillimeters ? "mm" : "m";
                string measurementName = includeMirrorsForDebug ? "W144 (mirrors)" : "W103 (body)";

                // Place the label slightly above the extent line
                Vector3 labelPos = (minPt + maxPt) * 0.5f + zHat * 0.25f;

                // Handles.Label works only in the Editor with this directive
                Handles.color = Color.white;
                Handles.Label(labelPos, $"{measurementName}\nWidth = {lastWidth:F1} {units}");

            }
#endif
        }



        // 6) (Optional) W106 front axle plane visualization
        if (showW106Plane)
        {
            // Find and draw plane line segments at front axle midpoint (if we can compute them)
            var wheelsRoot = scanner.wheelsRoot ? scanner.wheelsRoot : scanner.transform;
            var wheelCenters = new List<Vector3>();
            foreach (var col in wheelsRoot.GetComponentsInChildren<Collider>(true))
                wheelCenters.Add(col.bounds.center);
            foreach (var r in wheelsRoot.GetComponentsInChildren<Renderer>(true))
                wheelCenters.Add(r.bounds.center);

            if (wheelCenters.Count >= 2)
            {
                // project onto x̂, split into front/rear groups
                var proj = new List<(Vector3 p, float x)>();
                foreach (var p in wheelCenters) proj.Add((p, Vector3.Dot(p - originWS, xHat)));
                proj.Sort((a, b) => a.x.CompareTo(b.x));
                int half = Mathf.Max(1, proj.Count / 2);
                var rearGroup = proj.GetRange(0, half);
                var frontGroup = proj.GetRange(half, proj.Count - half);

                Vector3 rearMid = AverageWorld(rearGroup);
                Vector3 frontMid = AverageWorld(frontGroup);
                if (Vector3.Dot(frontMid - originWS, xHat) < Vector3.Dot(rearMid - originWS, xHat))
                { var t = frontMid; frontMid = rearMid; rearMid = t; }

                // Draw plane indicator (two cross lines)
                Gizmos.color = w106Color;
                Gizmos.DrawLine(frontMid - yHat * 1.0f, frontMid + yHat * 1.0f);
                Gizmos.DrawLine(frontMid - zHat * 1.0f, frontMid + zHat * 1.0f);

#if UNITY_EDITOR
                if (drawLabels)
                    Handles.Label(frontMid + zHat * 0.2f, "W106 plane (front axle X‑plane)");
#endif
            }
        }
    }

    private Vector3 AverageWorld(List<(Vector3 p, float x)> list)
    {
        Vector3 s = Vector3.zero; foreach (var it in list) s += it.p; return s / list.Count;
    }
}
