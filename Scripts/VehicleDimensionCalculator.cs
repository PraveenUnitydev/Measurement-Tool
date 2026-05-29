
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VehicleDimensionCalculator : MonoBehaviour
{
    [Header("Group Roots (optional, auto-discovery by name if null)")]
    public Transform bodyRoot;
    public Transform wheelsRoot;
    public Transform exteriorTrimsRoot;   // mirrors often live here
    public Transform electricalRoot;
    public Transform lampsRoot;
    public Transform gsmRoot;

    [Header("Mirror Identification")]
    [Tooltip("Name keywords to detect mirror parts inside exterior trims/body.")]
    public string[] mirrorNameKeywords = new[] { "mirror", "wingmirror", "orvm" };

    [Header("Scanner Settings")]
    [Tooltip("Samples along vehicle length (x̂).")]
    public int samplesX = 24;
    [Tooltip("Samples along vehicle height (ẑ).")]
    public int samplesZ = 24;
    [Tooltip("Extra margin (m) from vehicle lateral sides to start rays.")]
    public float lateralMargin = 1.5f;
    [Tooltip("Max raycast distance (m).")]
    public float rayDistance = 10f;
    [Tooltip("Slice tolerance (m) around X-plane for W106).")]
    [Range(0.001f, 0.05f)] public float xSliceTolerance = 0.01f;
    [Tooltip("Output in millimeters (true) or meters (false).")]
    public bool outputInMillimeters = true;

    [Header("Colliders")]
    [Tooltip("Create temporary MeshColliders on renderers that lack any collider.")]
    public bool createTemporaryMeshColliders = true;

    [Header("Mirror Poses (optional for W145)")]
    public Transform[] mirrorJointsDrivingPose;
    public Transform[] mirrorJointsFoldedPose;

    // --- Internals ---
    private bool frameReady;
    private Vector3 originWS; // convenience origin (front axle midpoint)
    private Vector3 xHat, yHat, zHat;
    private Bounds vehicleAABB;
    private List<Renderer> bodyRenderers = new List<Renderer>();
    private List<Renderer> mirrorRenderers = new List<Renderer>();
    private List<Renderer> allRenderers = new List<Renderer>();
    private List<Collider> tempColliders = new List<Collider>();

    [SerializeField] private MeasurementLineManager measurementLineManager;


    private struct WidthDebugInfo
    {
        public Vector3 minPointWS;
        public Vector3 maxPointWS;
        public Collider minCollider;
        public Collider maxCollider;
        public float minY;
        public float maxY;
    }

    // ---------- PUBLIC API ----------

    private void Start()
    {
       //Debug.Log("W103 "+ComputeW103());

        Debug.Log("W144 " + ComputeW106_Diagram());

        //Debug.Log("W103 " + ComputeW145());
    }


    public float ComputeW103()
    {
        Prepare();

        // Use the DETAILED scan so we get A/B hit points and collider info.
        var (minY, maxY, info) = ScanGridFilteredWithDetails(includeMirrors: false);

        // Safety: show the line only if we have valid points and the manager is assigned.
        if (measurementLineManager != null && info.minCollider != null && info.maxCollider != null)
        {
            measurementLineManager.ShowW103(info.minPointWS, info.maxPointWS);
        }
        else
        {
            Debug.LogWarning("W103: MeasurementLineManager is null or A/B points not found.");
        }

        return FormatWidth(maxY - minY);
    }





    public float ComputeW144()
    {
        Prepare();
        var (minY, maxY, info) = ScanGridFilteredWithDetails(includeMirrors: true);

        if (measurementLineManager != null && info.minCollider != null && info.maxCollider != null)
            measurementLineManager.ShowW144(info.minPointWS, info.maxPointWS);

        return FormatWidth(maxY - minY);
    }

    public float ComputeW145()
    {
        Prepare();

        Pose[] backup = null;
        if (mirrorJointsDrivingPose != null && mirrorJointsFoldedPose != null &&
            mirrorJointsDrivingPose.Length == mirrorJointsFoldedPose.Length &&
            mirrorJointsDrivingPose.Length > 0)
        {
            backup = BackupPoses(mirrorJointsDrivingPose);
            ApplyPoses(mirrorJointsDrivingPose, mirrorJointsFoldedPose);
        }

        var (minY, maxY, info) = ScanGridFilteredWithDetails(includeMirrors: true);

        if (measurementLineManager != null && info.minCollider != null && info.maxCollider != null)
            measurementLineManager.ShowW145(info.minPointWS, info.maxPointWS);

        if (backup != null) RestorePoses(mirrorJointsDrivingPose, backup);

        return FormatWidth(maxY - minY);
    }

    public float QuickW103_BoundsOnly()
    {
        Prepare();
        // exclude mirrors
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        foreach (var r in bodyRenderers)
        {
            var b = r.bounds;
            foreach (var c in BoundsCorners(b))
            {
                float y = Vector3.Dot(c - originWS, yHat);
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return FormatWidth(maxY - minY);
    }




    public float ComputeW106_Diagram()
    {
        Prepare();

        if (wheelsRoot == null)
        {
            Debug.LogError("W106 requires wheelsRoot.");
            return 0f;
        }

        var wheelCenters = CollectWheelCenters(wheelsRoot);
        if (wheelCenters.Count < 2)
        {
            Debug.LogError("W106: need at least two wheel elements.");
            return 0f;
        }

        var (frontMid, rearMid) = ClusterAxles(wheelCenters);
        float xPlaneCoord = Vector3.Dot(frontMid - originWS, xHat);

        // Build a single "synthetic row" by sampling heights at this fixed x plane
        var eligible = BuildEligibleColliderSet(includeMirrors: false);
        var row = new WidthRowInfo { rowIndex = -1, xCoord = xPlaneCoord, minY = float.PositiveInfinity, maxY = float.NegativeInfinity };

        float zSpan = Vector3.Dot(vehicleAABB.size, zHat);
        float lateralStart = Mathf.Max(lateralMargin, vehicleAABB.extents.y + 0.5f);
        Vector3 xBase = originWS + xHat * xPlaneCoord;

        for (int j = 0; j < samplesZ; j++)
        {
            float tZ = (samplesZ == 1) ? 0.5f : (float)j / (samplesZ - 1);
            Vector3 basePoint = xBase + zHat * ((tZ - 0.5f) * zSpan);

            Vector3 leftStart = basePoint - yHat * lateralStart;
            Vector3 rightStart = basePoint + yHat * lateralStart;

            var hitsL = Physics.RaycastAll(leftStart, yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hitsL != null && hitsL.Length > 0)
            {
                System.Array.Sort(hitsL, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hitsL)
                {
                    if (eligible.Contains(h.collider))
                    {
                        float y = Vector3.Dot(h.point - originWS, yHat);
                        row.leftHitsWS.Add(h.point);
                        if (y < row.minY) row.minY = y;
                        if (y > row.maxY) row.maxY = y;
                        break;
                    }
                }
            }

            var hitsR = Physics.RaycastAll(rightStart, -yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hitsR != null && hitsR.Length > 0)
            {
                System.Array.Sort(hitsR, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hitsR)
                {
                    if (eligible.Contains(h.collider))
                    {
                        float y = Vector3.Dot(h.point - originWS, yHat);
                        row.rightHitsWS.Add(h.point);
                        if (y < row.minY) row.minY = y;
                        if (y > row.maxY) row.maxY = y;
                        break;
                    }
                }
            }
        }

        if (!row.valid)
        {
            Debug.LogWarning("W106: No valid hits on the front axle plane.");
            return 0f;
        }

        DrawMeasurementDiagram("W106", row, includeMirrors: false);
        return FormatWidth(row.maxY - row.minY);
    }

    private void LogWidthContributors(string label, WidthDebugInfo info)
    {
        string units = outputInMillimeters ? "mm" : "m";
        float width = (info.maxY - info.minY) * (outputInMillimeters ? 1000f : 1f);

        Debug.Log($"{label} → Width: {width:F1} {units}\n" +
                  $"A (leftmost) collider: {Path(info.minCollider)} at {info.minPointWS}\n" +
                  $"B (rightmost) collider: {Path(info.maxCollider)} at {info.maxPointWS}");
    }

    private string Path(Collider c)
    {
        if (!c) return "<none>";
        var t = c.transform;
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    // ---------- PREP / FRAME ----------
    private void Prepare()
    {
        if (frameReady) return;

        // Auto-discover common group names if not assigned
        if (!bodyRoot) bodyRoot = FindChildByNames(transform, "Body");
        if (!wheelsRoot) wheelsRoot = FindChildByNames(transform, "Wheels", "Wheel", "Tyres", "Tires");
        if (!exteriorTrimsRoot) exteriorTrimsRoot = FindChildByNames(transform, "Exterior trims", "Exterior", "Trim", "Trims");
        if (!electricalRoot) electricalRoot = FindChildByNames(transform, "Electrical");
        if (!lampsRoot) lampsRoot = FindChildByNames(transform, "Lamps", "Lighting");

        // Collect renderers
        bodyRenderers = CollectRenderers(bodyRoot);
        var trimsRends = CollectRenderers(exteriorTrimsRoot);
        var electricalR = CollectRenderers(electricalRoot);
        var lampsR = CollectRenderers(lampsRoot);

        // Mirror renderers by name keywords from all possible groups
        mirrorRenderers = new List<Renderer>();
        foreach (var r in trimsRends.Concat(bodyRenderers).Concat(lampsR))
            if (IsMirror(r)) mirrorRenderers.Add(r);

        // all renderers (for collider creation / bounds)
        allRenderers = bodyRenderers.Concat(trimsRends).Concat(electricalR).Concat(lampsR).Distinct().ToList();

        // Create temporary MeshColliders (if needed)
        if (createTemporaryMeshColliders) EnsureColliders(allRenderers);

        // Vehicle AABB from all renderers
        vehicleAABB = ComputeAABB(allRenderers);

        // Build reference frame via PCA-lite on body renderers
        BuildReferenceFrameFromBounds(bodyRenderers);

        frameReady = true;
    }

    private void BuildReferenceFrameFromBounds(List<Renderer> renderers)
    {
        var pts = new List<Vector3>();
        foreach (var r in renderers) pts.AddRange(BoundsCorners(r.bounds));
        if (pts.Count == 0)
        {
            Debug.LogError("No body renderers found to build reference frame.");
            xHat = Vector3.forward; yHat = Vector3.right; zHat = Vector3.up;
            originWS = vehicleAABB.center;
            return;
        }

        Vector3 centroid = Vector3.zero;
        foreach (var p in pts) centroid += p; centroid /= pts.Count;

        // Covariance accumulation
        float cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
        foreach (var p in pts)
        {
            var d = p - centroid;
            cxx += d.x * d.x; cxy += d.x * d.y; cxz += d.x * d.z;
            cyy += d.y * d.y; cyz += d.y * d.z; czz += d.z * d.z;
        }
        // Power iteration for principal axis
        Vector3 v = new Vector3(1, 0, 0);
        for (int i = 0; i < 8; i++)
        {
            Vector3 nv = new Vector3(
                cxx * v.x + cxy * v.y + cxz * v.z,
                cxy * v.x + cyy * v.y + cyz * v.z,
                cxz * v.x + cyz * v.y + czz * v.z
            ).normalized;
            v = nv;
        }
        xHat = v.normalized;                // longest axis ~ length
        zHat = Vector3.up;                  // seed vertical
        yHat = Vector3.Cross(zHat, xHat).normalized;
        zHat = Vector3.Cross(xHat, yHat).normalized; // orthonormalize

        // Choose origin at front axle mid to simplify W106 later; fallback to AABB center
        originWS = vehicleAABB.center;
    }

    // ---------- SCAN ----------



    private (float minY, float maxY, WidthDebugInfo info) ScanGridFilteredWithDetails(bool includeMirrors)
    {
        var eligible = BuildEligibleColliderSet(includeMirrors);

        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
        WidthDebugInfo info = new WidthDebugInfo { minY = float.PositiveInfinity, maxY = float.NegativeInfinity };

        float xSpan = Vector3.Dot(vehicleAABB.size, xHat);
        float zSpan = Vector3.Dot(vehicleAABB.size, zHat);
        float lateralStart = Mathf.Max(lateralMargin, vehicleAABB.extents.y + 0.5f);

        for (int i = 0; i < samplesX; i++)
        {
            float tX = (samplesX == 1) ? 0.5f : (float)i / (samplesX - 1);
            Vector3 xBase = originWS + xHat * ((tX - 0.5f) * xSpan);

            for (int j = 0; j < samplesZ; j++)
            {
                float tZ = (samplesZ == 1) ? 0.5f : (float)j / (samplesZ - 1);
                Vector3 basePoint = xBase + zHat * ((tZ - 0.5f) * zSpan);

                Vector3 leftStart = basePoint - yHat * lateralStart;
                Vector3 rightStart = basePoint + yHat * lateralStart;

                // LEFT → +ŷ
                var hitsL = Physics.RaycastAll(leftStart, yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
                if (hitsL != null && hitsL.Length > 0)
                {
                    System.Array.Sort(hitsL, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hitsL)
                    {
                        if (eligible.Contains(h.collider))
                        {
                            float y = Vector3.Dot(h.point - originWS, yHat);
                            if (y < minY)
                            {
                                minY = y;
                                info.minY = y;
                                info.minPointWS = h.point;
                                info.minCollider = h.collider;
                            }
                            if (y > maxY)
                            {
                                maxY = y;
                                info.maxY = y;
                                info.maxPointWS = h.point;
                                info.maxCollider = h.collider;
                            }
                            break;
                        }
                    }
                }

                // RIGHT → −ŷ
                var hitsR = Physics.RaycastAll(rightStart, -yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
                if (hitsR != null && hitsR.Length > 0)
                {
                    System.Array.Sort(hitsR, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hitsR)
                    {
                        if (eligible.Contains(h.collider))
                        {
                            float y = Vector3.Dot(h.point - originWS, yHat);
                            if (y < minY)
                            {
                                minY = y;
                                info.minY = y;
                                info.minPointWS = h.point;
                                info.minCollider = h.collider;
                            }
                            if (y > maxY)
                            {
                                maxY = y;
                                info.maxY = y;
                                info.maxPointWS = h.point;
                                info.maxCollider = h.collider;
                            }
                            break;
                        }
                    }
                }
            }
        }

        if (float.IsInfinity(minY) || float.IsInfinity(maxY))
            return (0f, 0f, info);

        return (minY, maxY, info);
    }

    public float ComputeW103_Diagram()
    {
        Prepare();
        var rows = ScanRows(includeMirrors: false);

        // choose row: prefer frontmost within epsilon of max (configurable)
        var row = SelectRowForDiagram(rows, preferFront: preferFrontRowForW103, epsilonMm: 5f);

        // Draw a measurement diagram
        DrawMeasurementDiagram("W103", row, includeMirrors: false);

        // Return the numeric width from the selected row
        return FormatWidth(row != null ? (row.maxY - row.minY) : 0f);
    }

    [SerializeField] private MeasurementPolyline leftExtension;
    [SerializeField] private MeasurementPolyline rightExtension;
    [SerializeField] private MeasurementPolyline dimensionLine;
    [SerializeField] private float diagramOffset = 0.08f;   // offset outward from surface along ±ŷ
    [SerializeField] private float extensionLength = 0.15f; // perpendicular extension length
    [SerializeField] private bool preferFrontRowForW103 = true;

    private void DrawMeasurementDiagram(string codeLabel, WidthRowInfo row, bool includeMirrors)
    {
        if (row == null || row.leftHitsWS.Count == 0 || row.rightHitsWS.Count == 0) return;

        // Build extension polylines by slightly offsetting hits outward from surface
        // Left side outward = -ŷ; Right side outward = +ŷ (assuming ŷ is left→right)
        Vector3 outwardLeft = -yHat * diagramOffset;
        Vector3 outwardRight = yHat * diagramOffset;

        // Sort hits by height to keep polyline ordered (low→high)
        var leftOrdered = row.leftHitsWS.OrderBy(p => Vector3.Dot(p - originWS, zHat)).Select(p => p + outwardLeft).ToArray();
        var rightOrdered = row.rightHitsWS.OrderBy(p => Vector3.Dot(p - originWS, zHat)).Select(p => p + outwardRight).ToArray();

        // Draw left/right extension around the vehicle skin
        if (leftExtension) leftExtension.SetPoints(leftOrdered, new Color(0.9f, 0.4f, 0.1f));   // orange
        if (rightExtension) rightExtension.SetPoints(rightOrdered, new Color(0.2f, 0.8f, 1f));    // cyan

        // Dimension line: take lowest and highest heights in this row and connect the two extension lines with short perpendicular "ticks",
        // then draw the main dimension line at mid-height, offset outward.
        float zLow = Vector3.Dot(leftOrdered.First() - originWS, zHat);
        float zHigh = Vector3.Dot(leftOrdered.Last() - originWS, zHat);
        float midZ = (zLow + zHigh) * 0.5f;

        // Find points at midZ on each side (closest by height)
        Vector3 leftMid = leftOrdered.OrderBy(p => Mathf.Abs(Vector3.Dot(p - originWS, zHat) - midZ)).First();
        Vector3 rightMid = rightOrdered.OrderBy(p => Mathf.Abs(Vector3.Dot(p - originWS, zHat) - midZ)).First();

        // Build small perpendicular “ticks” at A/B
        Vector3 leftTickStart = leftMid - zHat * (extensionLength * 0.5f);
        Vector3 leftTickEnd = leftMid + zHat * (extensionLength * 0.5f);

        Vector3 rightTickStart = rightMid - zHat * (extensionLength * 0.5f);
        Vector3 rightTickEnd = rightMid + zHat * (extensionLength * 0.5f);

        // Main dimension line: from leftMid to rightMid (already offset outward), with arrowheads (optional)
        var dimPts = new[] { leftMid, rightMid };
        if (dimensionLine) dimensionLine.SetPoints(dimPts, Color.white);

        // Label at mid
        float meters = (row.maxY - row.minY);
        float val = outputInMillimeters ? meters * 1000f : meters;
        string units = outputInMillimeters ? "mm" : "m";
        string text = $"{codeLabel}: {val:F1} {units}";

#if TMP_PRESENT
    // If you have a TMP label, place it near (leftMid + rightMid)/2 + small z offset
#endif
    }


    private WidthRowInfo SelectRowForDiagram(List<WidthRowInfo> rows, bool preferFront, float epsilonMm = 5f)
    {
        if (rows == null || rows.Count == 0) return null;

        // Compute width per row
        var rowsWithWidth = rows.Select(r => new { r, widthMeters = (r.maxY - r.minY) }).ToList();
        float maxMeters = rowsWithWidth.Max(x => x.widthMeters);

        if (!preferFront)
        {
            // absolute max row
            return rowsWithWidth.OrderByDescending(x => x.widthMeters).First().r;
        }
        else
        {
            float epsMeters = epsilonMm / 1000f;
            var nearMax = rowsWithWidth.Where(x => (maxMeters - x.widthMeters) <= epsMeters).ToList();
            if (nearMax.Count == 0) nearMax = rowsWithWidth; // fallback

            // frontmost = smallest xCoord (assuming x̂ increases from front→rear; flip if needed)
            return nearMax.OrderBy(x => x.r.xCoord).First().r;
        }
    }


    private class WidthRowInfo
    {
        public int rowIndex;
        public float xCoord;             // (p - originWS) · x̂
        public float minY, maxY;         // extrema over all heights in this row
        public List<Vector3> leftHitsWS = new List<Vector3>();   // one per height sample
        public List<Vector3> rightHitsWS = new List<Vector3>();  // one per height sample
        public bool valid => leftHitsWS.Count > 0 && rightHitsWS.Count > 0;
    }

    /// <summary>
    /// Scan all rows along x̂; for each row gather left/right hits per height,
    /// compute row width, and return all rows.
    /// Mirrors can be included/excluded via includeMirrors.
    /// </summary>
    private List<WidthRowInfo> ScanRows(bool includeMirrors)
    {
        var eligible = BuildEligibleColliderSet(includeMirrors);
        var rows = new List<WidthRowInfo>();

        float xSpan = Vector3.Dot(vehicleAABB.size, xHat);
        float zSpan = Vector3.Dot(vehicleAABB.size, zHat);
        float lateralStart = Mathf.Max(lateralMargin, vehicleAABB.extents.y + 0.5f);

        for (int i = 0; i < samplesX; i++)
        {
            float tX = (samplesX == 1) ? 0.5f : (float)i / (samplesX - 1);
            Vector3 xBase = originWS + xHat * ((tX - 0.5f) * xSpan);

            var row = new WidthRowInfo { rowIndex = i, xCoord = Vector3.Dot(xBase - originWS, xHat), minY = float.PositiveInfinity, maxY = float.NegativeInfinity };

            for (int j = 0; j < samplesZ; j++)
            {
                float tZ = (samplesZ == 1) ? 0.5f : (float)j / (samplesZ - 1);
                Vector3 basePoint = xBase + zHat * ((tZ - 0.5f) * zSpan);

                Vector3 leftStart = basePoint - yHat * lateralStart;
                Vector3 rightStart = basePoint + yHat * lateralStart;

                // LEFT → +ŷ
                var hitsL = Physics.RaycastAll(leftStart, yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
                if (hitsL != null && hitsL.Length > 0)
                {
                    System.Array.Sort(hitsL, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hitsL)
                    {
                        if (eligible.Contains(h.collider))
                        {
                            float y = Vector3.Dot(h.point - originWS, yHat);
                            row.leftHitsWS.Add(h.point);
                            if (y < row.minY) row.minY = y;
                            if (y > row.maxY) row.maxY = y;
                            break;
                        }
                    }
                }

                // RIGHT → −ŷ
                var hitsR = Physics.RaycastAll(rightStart, -yHat, rayDistance, ~0, QueryTriggerInteraction.Ignore);
                if (hitsR != null && hitsR.Length > 0)
                {
                    System.Array.Sort(hitsR, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hitsR)
                    {
                        if (eligible.Contains(h.collider))
                        {
                            float y = Vector3.Dot(h.point - originWS, yHat);
                            row.rightHitsWS.Add(h.point);
                            if (y < row.minY) row.minY = y;
                            if (y > row.maxY) row.maxY = y;
                            break;
                        }
                    }
                }
            }

            if (row.valid) rows.Add(row);
        }

        return rows;
    }

   /* private (float minY, float maxY) ScanGrid(int layerMask)
    {
        float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;

        // Span along x̂ from min to max in vehicle bounds, along ẑ likewise
        float xSpan = Vector3.Dot(vehicleAABB.size, xHat);
        float zSpan = Vector3.Dot(vehicleAABB.size, zHat);

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
                    float y = Vector3.Dot(hitL.point - originWS, yHat);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if (Physics.Raycast(rightStart, -yHat, out var hitR, rayDistance, layerMask, QueryTriggerInteraction.Ignore))
                {
                    float y = Vector3.Dot(hitR.point - originWS, yHat);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (float.IsInfinity(minY) || float.IsInfinity(maxY))
            return (0f, 0f);

        return (minY, maxY);
    }*/

    // ---------- WHEELS / AXLES ----------
    private List<Vector3> CollectWheelCenters(Transform root)
    {
        var centers = new List<Vector3>();
        if (!root) return centers;

        // Prefer colliders; fallback to renderer bounds
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
            centers.Add(col.bounds.center);

        if (centers.Count == 0)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                centers.Add(r.bounds.center);
        }
        return centers;
    }

    private (Vector3 frontMid, Vector3 rearMid) ClusterAxles(List<Vector3> wheelCenters)
    {
        // Project wheel centers onto x̂ and group by K=2 clusters
        var proj = wheelCenters.Select(p => (p, x: Vector3.Dot(p - originWS, xHat))).OrderBy(t => t.x).ToList();

        int half = Mathf.Max(1, proj.Count / 2);
        var rearGroup = proj.Take(half).Select(t => t.p).ToList();
        var frontGroup = proj.Skip(half).Select(t => t.p).ToList();

        Vector3 rearMid = Average(rearGroup);
        Vector3 frontMid = Average(frontGroup);

        // If the grouping is inverted (e.g., odd counts), swap to keep front having larger x̂
        if (Vector3.Dot(frontMid - originWS, xHat) < Vector3.Dot(rearMid - originWS, xHat))
        {
            var tmp = frontMid; frontMid = rearMid; rearMid = tmp;
        }
        return (frontMid, rearMid);
    }

    // ---------- UTIL ----------
    private Transform FindChildByNames(Transform root, params string[] names)
    {
        if (!root) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            foreach (var n in names)
                if (t.name.ToLowerInvariant().Contains(n.ToLowerInvariant()))
                    return t;
        return null;
    }

    // Build a lookup set of all colliders under given renderers (and their children)
    private HashSet<Collider> CollectCollidersFromRenderers(IEnumerable<Renderer> renderers)
    {
        var set = new HashSet<Collider>();
        foreach (var r in renderers)
        {
            if (!r) continue;
            foreach (var col in r.GetComponentsInChildren<Collider>(true))
                set.Add(col);
            // If a collider isn’t directly on the renderer, catch sibling colliders:
            var parent = r.transform;
            foreach (var col in parent.GetComponentsInChildren<Collider>(true))
                set.Add(col);
        }
        return set;
    }

    // Decide if a collider belongs to a "mirror" part by name keywords
    private bool IsMirrorCollider(Collider col)
    {
        if (!col) return false;
        Transform t = col.transform;
        string name = t.name.ToLowerInvariant();

        // direct name match
        foreach (var k in mirrorNameKeywords)
        {
            if (name.Contains(k.ToLowerInvariant()))
                return true;
        }

        // check ancestors
        while (t != null)
        {
            string n = t.name.ToLowerInvariant();
            foreach (var k in mirrorNameKeywords)
            {
                if (n.Contains(k.ToLowerInvariant()))
                    return true;
            }
            t = t.parent;
        }
        return false;
    }

    // Build the set of eligible colliders depending on includeMirrors
    private HashSet<Collider> BuildEligibleColliderSet(bool includeMirrors)
    {
        // Gather all colliders under the whole vehicle (allRenderers already collected in Prepare)
        var allCols = new HashSet<Collider>();
        foreach (var r in allRenderers)
        {
            if (!r) continue;
            foreach (var c in r.GetComponentsInChildren<Collider>(true))
                allCols.Add(c);
        }

        if (includeMirrors)
        {
            // All colliders are eligible
            return allCols;
        }
        else
        {
            // Exclude mirror colliders
            var eligible = new HashSet<Collider>();
            foreach (var c in allCols)
            {
                if (!IsMirrorCollider(c))
                    eligible.Add(c);
            }
            return eligible;
        }
    }

    private List<Renderer> CollectRenderers(Transform root)
    {
        var list = new List<Renderer>();
        if (!root) return list;
        list.AddRange(root.GetComponentsInChildren<Renderer>(true));
        return list;
    }

    private bool IsMirror(Renderer r)
    {
        var n = r.name.ToLowerInvariant();
        return mirrorNameKeywords.Any(k => n.Contains(k.ToLowerInvariant()));
    }

    private void EnsureColliders(List<Renderer> renderers)
    {
        foreach (var r in renderers)
        {
            if (!r) continue;
            var hasCol = r.GetComponent<Collider>() != null;
            if (!hasCol && r is MeshRenderer)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh)
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false; // we want exact surface hits
                    tempColliders.Add(mc);
                }
            }
        }
    }

    private Bounds ComputeAABB(List<Renderer> renderers)
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool init = false;
        foreach (var r in renderers)
        {
            if (!r) continue;
            if (!init) { b = r.bounds; init = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!init) b = new Bounds(transform.position, Vector3.one);
        return b;
    }

    private IEnumerable<Vector3> BoundsCorners(Bounds b)
    {
        var c = b.center; var e = b.extents;
        yield return new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
        yield return new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
        yield return new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
        yield return new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
        yield return new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
        yield return new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
        yield return new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
        yield return new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);
    }

    private Vector3 Average(List<Vector3> pts)
    {
        if (pts.Count == 0) return vehicleAABB.center;
        Vector3 s = Vector3.zero; foreach (var p in pts) s += p; return s / pts.Count;
    }

    private Vector3 LerpBoundsZ(float t)
    {
        // Interpolate along zHat across vehicle bounds
        float zMin = Vector3.Dot(vehicleAABB.min - originWS, zHat);
        float zMax = Vector3.Dot(vehicleAABB.max - originWS, zHat);
        return originWS + zHat * Mathf.Lerp(zMin, zMax, t);
    }

    private int BuildLayerMask(bool includeMirrors)
    {
        // Easiest approach: use Everything, but filter hits by renderer set.
        // For finer control, you can map groups to layers and return LayerMask.GetMask(...)
        return ~0; // Everything
    }

    private float FormatWidth(float meters) => outputInMillimeters ? meters * 1000f : meters;

    private struct Pose { public Vector3 pos; public Quaternion rot; }
    private Pose[] BackupPoses(Transform[] joints)
    {
        var arr = new Pose[joints.Length];
        for (int i = 0; i < joints.Length; i++)
            arr[i] = new Pose { pos = joints[i].localPosition, rot = joints[i].localRotation };
        return arr;
    }
    private void ApplyPoses(Transform[] targets, Transform[] sources)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].localPosition = sources[i].localPosition;
            targets[i].localRotation = sources[i].localRotation;
        }
    }
    private void RestorePoses(Transform[] targets, Pose[] backup)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].localPosition = backup[i].pos;
            targets[i].localRotation = backup[i].rot;
        }
    }

/*#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!frameReady) return;
        // Axes
        Gizmos.color = Color.red; Gizmos.DrawLine(originWS, originWS + xHat * 2f);
        Gizmos.color = Color.green; Gizmos.DrawLine(originWS, originWS + yHat * 2f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(originWS, originWS + zHat * 2f);
    }
#endif*/

}
