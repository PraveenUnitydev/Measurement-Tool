using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Professional orbit camera controller — Maya / Blender / Unity Editor style.
///
/// Mouse Controls:
///   LMB drag          — Orbit around pivot
///   RMB drag          — Pan pivot in view plane
///   Scroll wheel      — Zoom toward cursor
///   Double-click LMB  — Focus on clicked point
///   F key             — Frame / reset view
///
/// Touch Controls:
///   1-finger drag     — Orbit
///   2-finger drag     — Pan
///   2-finger pinch    — Zoom
/// </summary>
public class OrbitCameraController : MonoBehaviour
{
    // =========================================================================
    // Inspector fields
    // =========================================================================

    [Header("Target")]
    [Tooltip("Root transform of the object to view.")]
    public Transform target;

    [Header("Auto Fit")]
    [Tooltip("Fit zoom/distance to target bounds on Start.")]
    public bool autoFitOnStart = true;
    [Tooltip("Breathing room around fitted bounds. 1.5 = 50% extra.")]
    [Range(1.1f, 3f)] public float fitPadding = 1.5f;
    [Tooltip("Closest zoom as fraction of fitted distance.")]
    [Range(0.05f, 0.5f)] public float minDistFraction = 0.15f;
    [Tooltip("Farthest zoom as multiple of fitted distance.")]
    [Range(2f, 10f)] public float maxDistFraction = 4f;

    [Header("Orbit")]
    [Range(0.5f, 15f)] public float orbitSensitivity = 5f;
    [Tooltip("Inertia after releasing mouse. 0 = instant stop.")]
    [Range(0f, 0.97f)] public float orbitInertia = 0.82f;
    [Range(1f, 30f)] public float minPitch = 5f;
    [Range(30f, 89f)] public float maxPitch = 85f;
    [Tooltip("Initial yaw. 225 = 3/4 front-left view.")]
    public float startYaw = 225f;
    [Tooltip("Initial pitch.")]
    [Range(1f, 89f)] public float startPitch = 25f;

    [Header("Pan")]
    [Range(0.1f, 5f)] public float panSensitivity = 1f;
    [Tooltip("Max pan radius as fraction of zoom distance.")]
    [Range(0.1f, 2f)] public float panLimit = 0.8f;

    [Header("Zoom")]
    [Tooltip("Fraction of distance consumed per scroll tick.")]
    [Range(0.05f, 0.5f)] public float zoomStep = 0.12f;
    [Range(0.5f, 5f)] public float zoomSensitivity = 1f;
    [Range(0.005f, 0.1f)] public float touchZoomSensitivity = 0.025f;

    [Header("Smoothing")]
    [Range(0.02f, 0.25f)] public float smoothTime = 0.08f;

    [Header("Fallback (no target / no renderers)")]
    public float fallbackDistance = 10f;
    public float fallbackMinDistance = 1f;
    public float fallbackMaxDistance = 50f;

    // =========================================================================
    // 2D / Orthographic mode
    // =========================================================================

    [Header("Flat 2D Settings")]
    [Tooltip("Lock camera to orthographic, axis-aligned view.")]
    public bool useOrthographic2D = false;

    public enum OrthoPreset { Top, Front, Right, Left, Back }

    [Tooltip("Which flat view to show in 2D mode.")]
    public OrthoPreset orthoView = OrthoPreset.Front;

    [Range(1.0f, 3.0f)] public float orthoPadding = 1.2f;
    public float orthoMinSize = 0.1f;
    public float orthoMaxSize = 500f;

    [Tooltip("Camera depth offset for orthographic positioning.")]
    [SerializeField] private float orthoDepth = 50f;

    [Header("2D Toggle UI")]
    [Tooltip("Optional TMP_Text label — updated to 'Enter / Exit 2D View Mode'.")]
    public TMP_Text twoDModeButtonLabel;

    // =========================================================================
    // Driver view
    // =========================================================================

    [Header("Driver View — Presets")]
    public Transform driverAnchor;
    public TMP_Dropdown driverPresetDropdown;

    public enum DriverHeightPreset { Five_00, Five_06, Five_11, Six_03 }

    public Vector3 driverOffset_5_0;
    public Vector3 driverOffset_5_6;
    public Vector3 driverOffset_5_11;
    public Vector3 driverOffset_6_3;
    [SerializeField] private DriverHeightPreset currentDriverPreset = DriverHeightPreset.Five_11;
    public Vector3 _driverViewDifference;

    // =========================================================================
    // Private — desired (target) state
    // =========================================================================

    private float yaw;          // horizontal orbit angle, degrees
    private float pitch;        // vertical   orbit angle, degrees
    private float distTarget;   // desired camera-to-pivot distance
    private Vector3 pivotTarget;  // desired pivot world position

    // =========================================================================
    // Private — current (smoothed) state
    // =========================================================================

    private float distCurrent;
    private Vector3 pivotCurrent;
    private float yawCurrent;
    private float pitchCurrent;

    // SmoothDamp velocities
    private float distVel;
    private Vector3 pivotVel;
    private float yawVel;
    private float pitchVel;

    // =========================================================================
    // Private — ortho smooth
    // =========================================================================

    private float orthoSizeTarget;
    private float orthoSizeCurrent;
    private float orthoSizeVel;

    // =========================================================================
    // Private — misc
    // =========================================================================

    private float minDist, maxDist;
    private Vector3 boundsCenter;   // XZ centre of target bounds — pan anchor
    private float floorY;         // min pivot Y
    private float ceilingY;       // max pivot Y

    // Inertia
    private Vector2 inertiaVel;

    // Input
    private bool isOrbiting, isPanning;
    private Vector2 lastOrbitPos, lastPanPos;

    // Double-click
    private float lastLmbDown = -99f;
    private const float DblClickSec = 0.22f;

    // Touch pinch
    private float pinchStartDist, pinchStartZoom;

    // UI raycast
    private PointerEventData ptrData;
    private List<RaycastResult> rayResults = new List<RaycastResult>();

    // Saved for ResetView
    private float savedDist;
    private Vector3 savedPivot;

    // Saved 3D state — captured when entering 2D so we restore exactly on exit
    private float saved3DYaw;
    private float saved3DPitch;
    private float saved3DDist;
    private Vector3 saved3DPivot;
    private bool has3DSavedState = false;

    // Driver view flag
    private bool isDriverView = false;

    // Driver offsets dictionary
    private readonly Dictionary<DriverHeightPreset, Vector3> _driverOffsets
        = new Dictionary<DriverHeightPreset, Vector3>();

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    void Start()
    {
        ptrData = new PointerEventData(EventSystem.current);

        yaw = startYaw;
        pitch = startPitch;

        if (autoFitOnStart && target != null)
            FitToTarget();
        else
            ApplyFallback();

        // Re-apply start angles after FitToTarget (which doesn't touch angles)
        yaw = startYaw;
        pitch = startPitch;

        // Snap current == target — no animation on frame 0
        SnapCurrentToTarget();

        savedDist = distTarget;
        savedPivot = pivotTarget;

        // Driver view init
        BuildDriverOffsets();
        InitDriverPresetDropdown();
        ApplyDriverPreset(currentDriverPreset, snap: false);

        // Projection (3D by default, or ortho if inspector flag was on)
        ApplyProjectionMode(snap: true);
        Update2DButtonLabel();
    }

    void Update()
    {
        if (target == null) ApplyFallback();

        if (Input.touchCount > 0)
            HandleTouch();
        else
            HandleMouse();

        if (useOrthographic2D)
        {
            // Lock out orbit in 2D
            isOrbiting = false;
            inertiaVel = Vector2.zero;
        }
        else
        {
            TickInertia();
        }

        SmoothAndCommit();
    }

    // =========================================================================
    // Auto-fit
    // =========================================================================

    public void FitToTarget()
    {
        if (target == null) { ApplyFallback(); return; }

        Bounds b = CombinedBounds(target);
        Camera cam = GetComponent<Camera>();

        if (b.size == Vector3.zero || cam == null) { ApplyFallback(); return; }

        float radius = b.extents.magnitude;
        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float fitDist = radius / Mathf.Sin(fovRad * 0.5f);

        distTarget = fitDist * fitPadding;
        minDist = Mathf.Max(0.05f, distTarget * minDistFraction);
        maxDist = distTarget * maxDistFraction;

        pivotTarget = b.center;
        boundsCenter = new Vector3(b.center.x, 0f, b.center.z);
        floorY = b.min.y;
        ceilingY = b.max.y;
    }

    void ApplyFallback()
    {
        distTarget = fallbackDistance;
        minDist = fallbackMinDistance;
        maxDist = fallbackMaxDistance;

        Vector3 basePos = target != null ? target.position : Vector3.zero;
        pivotTarget = basePos;
        boundsCenter = new Vector3(basePos.x, 0f, basePos.z);
        floorY = basePos.y;
        ceilingY = basePos.y + fallbackDistance;
    }

    static Bounds CombinedBounds(Transform root)
    {
        Renderer[] rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    // =========================================================================
    // Mouse input  (clean — no duplicate blocks)
    // =========================================================================

    void HandleMouse()
    {
        bool overUI = OverUI(Input.mousePosition);

        // ── LMB : Orbit ──────────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            float now = Time.unscaledTime;
            if (lastLmbDown > 0f && now - lastLmbDown < DblClickSec)
            {
                ZoomToClickPoint(Input.mousePosition);
                lastLmbDown = -99f;
            }
            else
            {
                lastLmbDown = now;
            }

            lastOrbitPos = Input.mousePosition;
            isOrbiting = false;
            inertiaVel = Vector2.zero;
        }

        if (Input.GetMouseButton(0) && !useOrthographic2D && !overUI)
        {
            Vector2 pos = Input.mousePosition;
            Vector2 delta = pos - lastOrbitPos;

            if (!isOrbiting && delta.sqrMagnitude > 4f)
                isOrbiting = true;

            if (isOrbiting)
            {
                OrbitBy(delta);
                inertiaVel = delta;
            }

            lastOrbitPos = pos;
        }

        if (Input.GetMouseButtonUp(0))
            isOrbiting = false;

        // ── RMB : Pan ─────────────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(1) && !overUI)
        {
            isPanning = true;
            lastPanPos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1))
            isPanning = false;

        if (isPanning)
        {
            Vector2 pos = Input.mousePosition;
            Vector2 delta = pos - lastPanPos;
            lastPanPos = pos;
            PanBy(delta);
        }

        // ── Scroll : Zoom ─────────────────────────────────────────────────────
        if (!overUI)
        {
            float s = Input.mouseScrollDelta.y;
            if (Mathf.Abs(s) > 0.001f)
            {
                if (useOrthographic2D) OrthoZoomBy(s);
                else ZoomBy(s, Input.mousePosition);
            }
        }

        // ── F key : Frame ─────────────────────────────────────────────────────
        if (Input.GetKeyDown(KeyCode.F)) FrameTarget();
    }

    // =========================================================================
    // Touch input
    // =========================================================================

    void HandleTouch()
    {
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began && !OverUI(t.position))
            {
                isOrbiting = !useOrthographic2D;
                lastOrbitPos = t.position;
                inertiaVel = Vector2.zero;
            }
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                isOrbiting = false;

            if (isOrbiting && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
            {
                OrbitBy(t.deltaPosition);
                inertiaVel = t.deltaPosition;
            }
        }
        else if (Input.touchCount >= 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);
            isOrbiting = false;

            bool anyBegan = t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began;
            if (anyBegan)
            {
                pinchStartDist = Vector2.Distance(t0.position, t1.position);
                pinchStartZoom = distTarget;
                lastPanPos = (t0.position + t1.position) * 0.5f;
                inertiaVel = Vector2.zero;
            }

            if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
            {
                float pinchNow = Vector2.Distance(t0.position, t1.position);
                float scale = pinchStartDist > 0.001f ? pinchNow / pinchStartDist : 1f;

                if (useOrthographic2D)
                    OrthoPinchTo(scale);
                else
                    distTarget = Mathf.Clamp(pinchStartZoom / scale, minDist, maxDist);

                Vector2 mid = (t0.position + t1.position) * 0.5f;
                PanBy(mid - lastPanPos);
                lastPanPos = mid;
            }
        }
    }

    // =========================================================================
    // Core orbit / pan / zoom
    // =========================================================================

    /// <summary>
    /// Drag left  → yaw decreases → camera swings left → vehicle appears to rotate right. Natural.
    /// Drag right → yaw increases → camera swings right → vehicle appears to rotate left. Natural.
    /// FIX: subtract delta.x so dragging right orbits clockwise (matches every major 3D app).
    /// </summary>
    void OrbitBy(Vector2 delta)
    {
        if (delta.sqrMagnitude < 0.0001f) return;

        // Subtract delta.x → drag right moves camera right (natural, non-inverted)
        yaw += delta.x * orbitSensitivity * 0.1f;
        pitch -= delta.y * orbitSensitivity * 0.1f;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    /// <summary>
    /// Pan the pivot in the camera's view plane.
    /// Uses the camera's actual right/up vectors so panning is always screen-aligned.
    /// </summary>
    void PanBy(Vector2 screenDelta)
    {
        if (isDriverView) return;

        float scale = (distCurrent / Screen.height) * panSensitivity;

        // Use camera's actual right vector (accounts for yaw correctly at all angles)
        Vector3 right = transform.right;
        Vector3 up = Vector3.up; // keep vertical pan world-up only

        Vector3 move = -right * screenDelta.x * scale
                     + -up * screenDelta.y * scale;

        Vector3 proposed = pivotTarget + move;

        // Y clamp
        proposed.y = Mathf.Clamp(proposed.y, floorY, ceilingY);

        // XZ radius clamp
        if (panLimit > 0f)
        {
            float maxR = distTarget * panLimit;
            Vector2 xzOff = new Vector2(proposed.x - boundsCenter.x,
                                        proposed.z - boundsCenter.z);
            if (xzOff.magnitude > maxR)
            {
                xzOff = xzOff.normalized * maxR;
                proposed.x = boundsCenter.x + xzOff.x;
                proposed.z = boundsCenter.z + xzOff.y;
            }
        }

        pivotTarget = proposed;
    }

    void ZoomBy(float scrollSign, Vector2 screenPos)
    {
        if (isDriverView) return;

        float factor = scrollSign > 0f
                        ? 1f - zoomStep * zoomSensitivity
                        : 1f + zoomStep * zoomSensitivity;
        float newDist = Mathf.Clamp(distTarget * factor, minDist, maxDist);

        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            Plane pivPlane = new Plane(-transform.forward, pivotTarget);
            float enter;
            if (pivPlane.Raycast(ray, out enter))
            {
                Vector3 hitPt = ray.GetPoint(enter);
                float fraction = (newDist - distTarget) / Mathf.Max(distTarget, 0.001f);
                Vector3 nudge = (pivotTarget - hitPt) * fraction * 0.5f;

                Vector3 proposed = pivotTarget + nudge;
                proposed.y = Mathf.Clamp(proposed.y, floorY, ceilingY);

                if (panLimit > 0f)
                {
                    float maxR = newDist * panLimit;
                    Vector2 xzOff = new Vector2(proposed.x - boundsCenter.x,
                                                proposed.z - boundsCenter.z);
                    if (xzOff.magnitude > maxR)
                    {
                        xzOff = xzOff.normalized * maxR;
                        proposed.x = boundsCenter.x + xzOff.x;
                        proposed.z = boundsCenter.z + xzOff.y;
                    }
                }

                pivotTarget = proposed;
            }
        }

        distTarget = newDist;
    }

    void OrthoZoomBy(float scrollSign)
    {
        float step = zoomStep * zoomSensitivity;
        float factor = scrollSign > 0f ? (1f - step) : (1f + step);
        orthoSizeTarget = Mathf.Clamp(orthoSizeTarget * factor, orthoMinSize, orthoMaxSize);
    }

    void OrthoPinchTo(float scale)
    {
        float newSize = orthoSizeTarget / Mathf.Max(0.001f, scale);
        orthoSizeTarget = Mathf.Clamp(newSize, orthoMinSize, orthoMaxSize);
    }

    // =========================================================================
    // Inertia
    // =========================================================================

    void TickInertia()
    {
        if (isOrbiting || inertiaVel.sqrMagnitude < 0.01f)
        {
            if (!isOrbiting) inertiaVel = Vector2.zero;
            return;
        }
        OrbitBy(inertiaVel);
        inertiaVel *= orbitInertia;
        if (inertiaVel.sqrMagnitude < 0.01f) inertiaVel = Vector2.zero;
    }

    // =========================================================================
    // Transform commit
    // =========================================================================

    void SmoothAndCommit()
    {
        var cam = GetComponent<Camera>();

        if (useOrthographic2D)
        {
            // Smooth pivot pan in 2D
            pivotCurrent = Vector3.SmoothDamp(pivotCurrent, pivotTarget, ref pivotVel, smoothTime);

            // Smooth ortho size
            if (cam != null)
            {
                orthoSizeCurrent = Mathf.SmoothDamp(orthoSizeCurrent, orthoSizeTarget, ref orthoSizeVel, smoothTime);
                cam.orthographicSize = orthoSizeCurrent;
            }

            // Fixed axis-aligned rotation from locked yaw/pitch
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 forward = rot * Vector3.forward;
            transform.position = pivotCurrent - forward * orthoDepth;
            transform.rotation = rot;
            return;
        }

        // ── Perspective path ──────────────────────────────────────────────────
        distCurrent = Mathf.SmoothDamp(distCurrent, distTarget, ref distVel, smoothTime);
        pivotCurrent = Vector3.SmoothDamp(pivotCurrent, pivotTarget, ref pivotVel, smoothTime);
        yawCurrent = Mathf.SmoothDampAngle(yawCurrent, yaw, ref yawVel, smoothTime);
        pitchCurrent = Mathf.SmoothDamp(pitchCurrent, pitch, ref pitchVel, smoothTime);
        CommitTransform();
    }

    void CommitTransform()
    {
        float yawRad = yawCurrent * Mathf.Deg2Rad;
        float pitchRad = pitchCurrent * Mathf.Deg2Rad;

        // Unit vector from pivot to camera (spherical coordinates)
        Vector3 dir = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
            Mathf.Sin(pitchRad),
            Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
        );

        Vector3 camPos = pivotCurrent + dir * distCurrent;

        Vector3 up = Mathf.Abs(pitchCurrent - 90f) < 1f ? transform.forward : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(pivotCurrent - camPos, up);

        transform.position = camPos;
        transform.rotation = rot;
    }

    // =========================================================================
    // Frame / Focus
    // =========================================================================

    public void FrameTarget()
    {
        ExitDriverView();
        if (target == null) return;
        FitToTarget();
        yaw = startYaw;
        pitch = startPitch;
        inertiaVel = Vector2.zero;
    }

    void ZoomToClickPoint(Vector2 screenPos)
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        Vector3 targetPoint = pivotTarget;

        RaycastHit physHit;
        if (Physics.Raycast(ray, out physHit, maxDist * 2f))
        {
            targetPoint = physHit.point;
        }
        else
        {
            Plane plane = new Plane(-ray.direction, pivotTarget);
            float enter;
            if (plane.Raycast(ray, out enter))
                targetPoint = ray.GetPoint(enter);
        }

        pivotTarget = targetPoint;
        pivotTarget.y = Mathf.Clamp(pivotTarget.y, floorY, ceilingY);
        distTarget = Mathf.Max(distTarget * 0.5f, minDist);
        inertiaVel = Vector2.zero;
    }

    // =========================================================================
    // Public API
    // =========================================================================

    #region Public API

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        FitToTarget();
        savedDist = distTarget;
        savedPivot = pivotTarget;
    }

    public void SetAngles(float yawDeg, float pitchDeg)
    {
        yaw = yawDeg;
        pitch = useOrthographic2D
                ? pitchDeg
                : Mathf.Clamp(pitchDeg, minPitch, maxPitch);
        inertiaVel = Vector2.zero;
    }

    public void SetDistance(float d)
    {
        distTarget = Mathf.Clamp(d, minDist, maxDist);
    }

    /// <summary>Snaps both current and target to bounds center — call before any preset view.</summary>
    public void ResetPivotToCenter()
    {
        if (target == null) return;
        Bounds b = CombinedBounds(target);
        pivotTarget = b.center;
        pivotCurrent = b.center;
        boundsCenter = new Vector3(b.center.x, 0f, b.center.z);
        // Kill smoothing velocity so there's no leftover drift
        pivotVel = Vector3.zero;
    }

    public void ResetView()
    {
        ExitDriverView();
        if (target != null) FitToTarget();   // recompute dist for current bounds
        ResetPivotToCenter();
        distTarget = savedDist;
        yaw = startYaw;
        pitch = startPitch;
        inertiaVel = Vector2.zero;
    }

    // ── 2D toggle ─────────────────────────────────────────────────────────────

    public void Enter2DViewMode(bool snap = true)
    {
        if (useOrthographic2D) return;
        Save3DState();
        useOrthographic2D = true;
        ApplyProjectionMode(snap);
        Update2DButtonLabel();
    }

    public void Exit2DViewMode(bool snap = true)
    {
        if (!useOrthographic2D) return;
        useOrthographic2D = false;
        ApplyProjectionMode(snap);
        Update2DButtonLabel();
    }

    public void Toggle2DViewMode(bool snap = true)
    {
        if (useOrthographic2D) Exit2DViewMode(snap);
        else Enter2DViewMode(snap);
    }

    // ── View presets ──────────────────────────────────────────────────────────

    public void SetFrontView()
    {
        if (useOrthographic2D) { orthoView = OrthoPreset.Front; ApplyProjectionMode(true); }
        else { ResetPivotToCenter(); ResetView(); SetAngles(180f, 10f); }
    }

    public void SetRearView()
    {
        if (useOrthographic2D) { orthoView = OrthoPreset.Back; ApplyProjectionMode(true); }
        else { ResetPivotToCenter(); ResetView(); SetAngles(0f, 10f); }
    }

    public void SetRightSideView()
    {
        if (useOrthographic2D) { orthoView = OrthoPreset.Right; ApplyProjectionMode(true); }
        else { ResetPivotToCenter(); ResetView(); SetAngles(90f, 10f); }
    }

    public void SetLeftSideView()
    {
        if (useOrthographic2D) { orthoView = OrthoPreset.Left; ApplyProjectionMode(true); }
        else { ResetPivotToCenter(); ResetView(); SetAngles(270f, 10f); }
       /* else
        {
            ResetPivotToCenter();
            ResetView();
            // Use 270 (not -90) — avoids SmoothDampAngle taking the long 360° arc
            SetAngles(270f, 10f);
            // Snap yawCurrent immediately so smoothing starts from the right side
            yawCurrent = 270f;
            yawVel = 0f;
        }*/
    }

    public void SetTopView()
    {
        if (useOrthographic2D) { orthoView = OrthoPreset.Top; ApplyProjectionMode(true); }
        else { ResetPivotToCenter(); ResetView(); SetAngles(0f, 89f); }
    }

    /// <summary>3/4 front-left view — classic automotive hero angle.</summary>
    public void SetThreeQuarterView()
    {
        if (useOrthographic2D) Exit2DViewMode(false);
        ResetPivotToCenter();
        if (target != null) FitToTarget();
        SetAngles(225f, 25f);
        inertiaVel = Vector2.zero;
    }

    public void FocusOnBounds(Bounds bounds)
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        boundsCenter = new Vector3(bounds.center.x, 0f, bounds.center.z);
        floorY = bounds.min.y;
        ceilingY = bounds.max.y;

        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float radius = bounds.extents.magnitude;
        distTarget = Mathf.Clamp(radius / Mathf.Sin(fovRad * 0.5f) * fitPadding, minDist, maxDist);
        pivotTarget = bounds.center;

        yaw = startYaw;
        pitch = startPitch;
        inertiaVel = Vector2.zero;

        if (useOrthographic2D)
        {
            ApplyOrthographicFacing();
            ComputeOrthoSizeFromBounds(snap: false);
        }
    }

    #endregion

    // =========================================================================
    // 2D / Orthographic internals
    // =========================================================================

    void Save3DState()
    {
        saved3DYaw = yaw;
        saved3DPitch = pitch;
        saved3DDist = distTarget;
        saved3DPivot = pivotTarget;
        has3DSavedState = true;
    }

    void ApplyProjectionMode(bool snap)
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        if (useOrthographic2D)
        {
            cam.orthographic = true;

            ApplyOrthographicFacing();
            ComputeOrthoSizeFromBounds(snap);

            if (snap)
            {
                pivotCurrent = pivotTarget;
                yawCurrent = yaw;
                pitchCurrent = pitch;
                orthoSizeCurrent = orthoSizeTarget;
                cam.orthographicSize = orthoSizeTarget;
            }
        }
        else
        {
            // ── Restore perspective state ────────────────────────────────────
            cam.orthographic = false;

            if (has3DSavedState)
            {
                yaw = saved3DYaw;
                pitch = saved3DPitch;
                distTarget = saved3DDist;
                pivotTarget = saved3DPivot;
            }
            else
            {
                // First time (never entered 2D before) — just use start angles
                yaw = startYaw;
                pitch = startPitch;
                if (target != null) FitToTarget();
            }

            if (snap) SnapCurrentToTarget();

            inertiaVel = Vector2.zero;
            CommitTransform();
        }
    }

    void ApplyOrthographicFacing()
    {
        // Snap pivot to bounds center
        if (target != null)
            pivotTarget = CombinedBounds(target).center;

        // Exact axis-aligned yaw/pitch for each preset
        switch (orthoView)
        {
            case OrthoPreset.Top:
                // Looking straight down: pitch = -90
                yaw = 0f;
                pitch = -90f;
                break;

            case OrthoPreset.Front:
                // Looking at the front face: camera is in front (+Z side looking toward -Z)
                yaw = 180f;
                pitch = 0f;
                break;

            case OrthoPreset.Back:
                yaw = 0f;
                pitch = 0f;
                break;

            case OrthoPreset.Right:
                // Camera on right (+X side looking toward -X)
                yaw = 90f;
                pitch = 0f;
                break;

            case OrthoPreset.Left:
                // Camera on left (-X side looking toward +X)
                yaw = 270f;
                pitch = 0f;
                break;
        }

        inertiaVel = Vector2.zero;
    }

    void ComputeOrthoSizeFromBounds(bool snap)
    {
        if (target == null) return;
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        Bounds b = CombinedBounds(target);
        if (b.size == Vector3.zero) return;

        // Build the camera's right/up from the locked yaw/pitch
        Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 right = look * Vector3.right;
        Vector3 up = look * Vector3.up;

        Vector3 bMin = b.min, bMax = b.max;

        // Project 8 bounding box corners onto camera's 2D axes
        float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
        float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;

        for (int xi = 0; xi <= 1; xi++)
            for (int yi = 0; yi <= 1; yi++)
                for (int zi = 0; zi <= 1; zi++)
                {
                    Vector3 corner = new Vector3(
                        xi == 0 ? bMin.x : bMax.x,
                        yi == 0 ? bMin.y : bMax.y,
                        zi == 0 ? bMin.z : bMax.z
                    );
                    float r = Vector3.Dot(corner, right);
                    float u = Vector3.Dot(corner, up);
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    if (u < minU) minU = u;
                    if (u > maxU) maxU = u;
                }

        float widthWorld = (maxR - minR) * orthoPadding;
        float heightWorld = (maxU - minU) * orthoPadding;
        float aspect = Mathf.Max(0.0001f, cam.aspect);

        float sizeByHeight = heightWorld * 0.5f;
        float sizeByWidth = (widthWorld * 0.5f) / aspect;
        float finalSize = Mathf.Max(sizeByHeight, sizeByWidth);

        orthoSizeTarget = Mathf.Clamp(finalSize, orthoMinSize, orthoMaxSize);

        if (snap)
        {
            orthoSizeCurrent = orthoSizeTarget;
            cam.orthographicSize = orthoSizeTarget;
        }

        pivotTarget = b.center;
    }

    void Update2DButtonLabel()
    {
        if (twoDModeButtonLabel == null) return;
        twoDModeButtonLabel.text = useOrthographic2D ? "Exit 2D View Mode" : "Enter 2D View Mode";
    }

    // =========================================================================
    // Driver view
    // =========================================================================

    public void SetDriverView(Transform anchor)
    {
        if (anchor == null) return;
        isDriverView = true;

        Vector3 camPos = anchor.position + _driverViewDifference;
        Vector3 lookDir = anchor.forward.normalized;

        pivotTarget = camPos;
        distTarget = 0.01f;

        Vector3 orbitDir = (-lookDir).normalized;
        DirectionToYawPitch(orbitDir, out float newYaw, out float newPitch);
        yaw = newYaw;
        pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);

        inertiaVel = Vector2.zero;
    }

    public void ExitDriverView(bool snap = false)
    {
        if (!isDriverView) return;
        isDriverView = false;
        if (target != null) FitToTarget();
        inertiaVel = Vector2.zero;

        if (snap) SnapCurrentToTarget();
    }

    public void SetDriverAnchor(Transform anchor, bool applyCurrentPreset = true, bool snap = false)
    {
        driverAnchor = anchor;
        if (applyCurrentPreset && driverAnchor != null)
            ApplyDriverPreset(currentDriverPreset, snap);
    }

    public void ApplyDriverPreset(DriverHeightPreset preset, bool snap = false)
    {
        currentDriverPreset = preset;
        if (!_driverOffsets.TryGetValue(preset, out var localOffset)) return;
        _driverViewDifference = localOffset;
        if (driverAnchor == null) return;

        SetDriverView(driverAnchor);
        if (snap) SnapCurrentToTarget();
        if (driverPresetDropdown != null)
            driverPresetDropdown.SetValueWithoutNotify((int)currentDriverPreset);
    }

    public void OnDriverPresetChanged(int index)
    {
        var preset = (DriverHeightPreset)Mathf.Clamp(index, 0, (int)DriverHeightPreset.Six_03);
        ApplyDriverPreset(preset, snap: false);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    void SnapCurrentToTarget()
    {
        distCurrent = distTarget;
        pivotCurrent = pivotTarget;
        yawCurrent = yaw;
        pitchCurrent = pitch;
        distVel = 0f;
        pivotVel = Vector3.zero;
        yawVel = 0f;
        pitchVel = 0f;
    }

    bool OverUI(Vector2 pos)
    {
        if (EventSystem.current == null) return false;
        ptrData.position = pos;
        rayResults.Clear();
        EventSystem.current.RaycastAll(ptrData, rayResults);
        return rayResults.Count > 0;
    }

    static void DirectionToYawPitch(Vector3 dir, out float yawDeg, out float pitchDeg)
    {
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        pitchDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    void BuildDriverOffsets()
    {
        _driverOffsets.Clear();
        _driverOffsets[DriverHeightPreset.Five_00] = driverOffset_5_0;
        _driverOffsets[DriverHeightPreset.Five_06] = driverOffset_5_6;
        _driverOffsets[DriverHeightPreset.Five_11] = driverOffset_5_11;
        _driverOffsets[DriverHeightPreset.Six_03] = driverOffset_6_3;
    }

    void InitDriverPresetDropdown()
    {
        if (driverPresetDropdown == null) return;
        driverPresetDropdown.ClearOptions();
        driverPresetDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
        {
            new TMP_Dropdown.OptionData("5'0\""),
            new TMP_Dropdown.OptionData("5'6\""),
            new TMP_Dropdown.OptionData("5'11\""),
            new TMP_Dropdown.OptionData("6'3\"")
        });
        driverPresetDropdown.SetValueWithoutNotify((int)currentDriverPreset);
        driverPresetDropdown.onValueChanged.RemoveListener(OnDriverPresetChanged);
        driverPresetDropdown.onValueChanged.AddListener(OnDriverPresetChanged);
    }

    private void OnValidate()
    {
        BuildDriverOffsets();
    }
}
