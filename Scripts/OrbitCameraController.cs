using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Professional orbit camera controller that behaves like Maya / Blender / Unity Editor.
///
/// Controls:
///   Left Mouse Button drag       — Orbit (tumble) around the pivot
///   Right Mouse Button drag      — Pan (truck) the pivot in the view plane
///   Scroll Wheel                 — Dolly zoom toward the 3D point under the cursor
///   Double-click (LMB)           — Frame / focus the target object
///   F key                        — Frame / focus the target object
///
/// Touch:
///   1-finger drag                — Orbit
///   2-finger drag (same dir)     — Pan
///   2-finger pinch               — Zoom
/// </summary>
public class OrbitCameraController : MonoBehaviour
{    // =========================================================================
    // Inspector
    // =========================================================================

    [Header("Target")]
    [Tooltip("Root transform of the object to view. Drag your vehicle root here.")]
    public Transform target;

    [Header("Auto Fit")]
    [Tooltip("Derive zoom range and initial distance from target Renderer bounds on Start.")]
    public bool autoFitOnStart = true;
    [Tooltip("Extra breathing room around the fitted bounds. 1.5 = 50% extra.")]
    [Range(1.1f, 3f)] public float fitPadding = 1.5f;
    [Tooltip("How close the user can zoom (fraction of fitted distance).")]
    [Range(0.05f, 0.5f)] public float minDistFraction = 0.15f;
    [Tooltip("How far the user can zoom out (multiple of fitted distance).")]
    [Range(2f, 10f)] public float maxDistFraction = 4f;

    [Header("Orbit")]
    [Range(0.5f, 15f)] public float orbitSensitivity = 5f;
    [Tooltip("Inertia after releasing mouse. 0 = instant stop, 0.9 = lots of coast.")]
    [Range(0f, 0.97f)] public float orbitInertia = 0.82f;
    [Tooltip("Minimum pitch. Keep ≥ 1 so camera stays above the object floor.")]
    [Range(1f, 30f)] public float minPitch = 5f;
    [Tooltip("Maximum pitch. 89 = almost directly overhead.")]
    [Range(30f, 89f)] public float maxPitch = 85f;
    [Tooltip("Initial yaw angle in degrees (0 = rear of object facing camera).")]
    public float startYaw = 45f;
    [Tooltip("Initial pitch angle in degrees.")]
    [Range(1f, 89f)] public float startPitch = 25f;

    [Header("Pan")]
    [Range(0.1f, 5f)] public float panSensitivity = 1f;
    [Tooltip("Max pan radius as a fraction of zoom distance. Prevents panning off to infinity.")]
    [Range(0.1f, 2f)] public float panLimit = 0.8f;

    [Header("Zoom")]
    [Range(0.05f, 0.5f)]
    [Tooltip("Fraction of remaining distance consumed per scroll tick. Exponential — never overshoots.")]
    public float zoomStep = 0.12f;
    [Range(0.5f, 5f)] public float zoomSensitivity = 1f;
    [Range(0.005f, 0.1f)] public float touchZoomSensitivity = 0.025f;

    [Header("Smoothing")]
    [Range(0.02f, 0.25f)] public float smoothTime = 0.08f;

    [Header("Fallback (when Auto Fit is off or no renderers found)")]
    public float fallbackDistance = 10f;
    public float fallbackMinDistance = 1f;
    public float fallbackMaxDistance = 50f;

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
    // Private — misc
    // =========================================================================

    private float minDist, maxDist;
    private Vector3 boundsCenter;    // XZ centre of target bounds — pan anchor
    private float floorY;          // min pivot Y  (bounds.min.y)
    private float ceilingY;        // max pivot Y  (bounds.max.y)

    // Inertia
    private Vector2 inertiaVel;      // px/frame remaining after mouse release

    // Input
    private bool isOrbiting, isPanning;
    private Vector2 lastOrbitPos, lastPanPos;

    // Double-click
    private float lastLmbDown = -99f;
    private const float DblClickSec = 0.22f;

    // Touch pinch
    private float pinchStartDist, pinchStartZoom;

    // UI
    private PointerEventData ptrData;
    private List<RaycastResult> rayResults = new List<RaycastResult>();

    // Saved for ResetView
    private float savedDist;
    private Vector3 savedPivot;



    // ==============================
    // Orthographic / 2D View
    // ==============================
    // Flat 2D View (no rotation)
    // ==============================
    [Header("Flat 2D Settings")]
    [Tooltip("When ON, camera is orthographic and rotation is locked. No orbit input is processed.")]
    public bool useOrthographic2D = false;

    public enum OrthoPreset { Top, Front, Right, Left, Back }

    [Tooltip("Which flat 2D view to use.")]
    public OrthoPreset orthoView = OrthoPreset.Top;

    [Range(1.0f, 3.0f)] public float orthoPadding = 1.2f;
    public float orthoMinSize = 0.1f;
    public float orthoMaxSize = 500f;

    // Internal tracked size for smoothing
    private float orthoSizeTarget;
    private float orthoSizeCurrent;
    private float orthoSizeVel;

    // A safe fixed camera distance used only for positioning (scale comes from orthographicSize)
    [SerializeField] private float orthoDepth = 50f;


    [Header("2D Toggle UI")]
    [Tooltip("Optional: assign a TMP_Text to show 'Enter 2D View Mode' / 'Exit 2D View Mode'.")]
    public TMP_Text twoDModeButtonLabel;

    // =========================================================================
    // Unity lifecycle
    // =========================================================================

    private bool isDriverView = false;
    void Start()
    {
        ptrData = new PointerEventData(EventSystem.current);

        // Set angles to the inspector defaults first so they're always sane
        yaw = startYaw;
        pitch = startPitch;

        if (autoFitOnStart && target != null)
            FitToTarget();
        else
            ApplyFallback();

        // Initialise "current" to "target" so nothing animates on frame 0
        distCurrent = distTarget;
        pivotCurrent = pivotTarget;
        yawCurrent = yaw;
        pitchCurrent = pitch;

        savedDist = distTarget;
        savedPivot = pivotTarget;

        CommitTransform();
        InitDriverPresetDropdown();
        InitDriverViewPresetRuntime();
        ApplyProjectionMode(snap: true);
        Update2DButtonLabel();
    }

    void Update()
    {
        // If our target was destroyed at runtime, avoid touching it and switch to fallback
        if (target == null)
        {
            // Reset bounds-related state safely once
            ApplyFallback();
        }

        if (Input.touchCount > 0) HandleTouch();
        else HandleMouse();

        if (useOrthographic2D)
        {
            // Ensure nothing can orbit while in 2D
            isOrbiting = false;
            inertiaVel = Vector2.zero;

            // Keep yaw/pitch rigid (no smoothing drift)
            yawCurrent = yaw;
            pitchCurrent = pitch;
        }

        TickInertia();
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

        // Bounding sphere radius
        float radius = b.extents.magnitude;

        // Distance so the sphere fills half the vertical FOV
        float fovRad = cam.fieldOfView * Mathf.Deg2Rad;
        float fitDist = radius / Mathf.Sin(fovRad * 0.5f);

        distTarget = fitDist * fitPadding;
        minDist = Mathf.Max(0.05f, distTarget * minDistFraction);
        maxDist = distTarget * maxDistFraction;

        // Pivot at bounds centre
        pivotTarget = b.center;
        boundsCenter = new Vector3(b.center.x, 0f, b.center.z);

        // Vertical pan clamp
        floorY = b.min.y;
        ceilingY = b.max.y;


        if (useOrthographic2D)
        {
            ApplyOrthographicFacing_Locked();
            ComputeOrthoSizeFromBounds(snap: true);
        }

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
    // Mouse input
    // =========================================================================

    void HandleMouse()
    {
        if (useOrthographic2D)
        {
            isOrbiting = false;
        }
        bool overUI = OverUI(Input.mousePosition);
        if (isDriverView)
        {
            isPanning = false;
        }

        // ── LMB : Orbit (FIXED VERSION) ─────────────────────────────
        if (Input.GetMouseButtonDown(0) && !overUI)
        {
            float now = Time.unscaledTime;

            // Double click (keep this logic)
            if (lastLmbDown > 0f && now - lastLmbDown < DblClickSec)
            {
                ZoomToClickPoint(Input.mousePosition);
                lastLmbDown = -99f;
            }
            else
            {
                lastLmbDown = now;
            }

            // ✅ DO NOT start orbit yet
            lastOrbitPos = Input.mousePosition;
            isOrbiting = false;
            inertiaVel = Vector2.zero;
        }

        if (Input.GetMouseButton(0) && !useOrthographic2D)
        {
            Vector2 pos = Input.mousePosition;
            Vector2 delta = pos - lastOrbitPos;

            // ✅ Start orbit only if actual drag
            if (!isOrbiting && delta.sqrMagnitude > 4f) // ~2px threshold
            {
                isOrbiting = true;
            }

            if (isOrbiting)
            {
                OrbitBy(delta);
                inertiaVel = delta;
            }

            lastOrbitPos = pos;
        }

        if (Input.GetMouseButtonUp(0))
        {
            isOrbiting = false;
        }


        if (!useOrthographic2D && Input.GetMouseButtonUp(0)) isOrbiting = false;
        if (!useOrthographic2D && isOrbiting)
        {
            Vector2 pos = Input.mousePosition;
            Vector2 delta = pos - lastOrbitPos;
            lastOrbitPos = pos;
            OrbitBy(delta);
            inertiaVel = delta;
        }

        if (Input.GetMouseButtonUp(0)) isOrbiting = false;



        // ── RMB : Pan ─────────────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(1) && !overUI)
        {
            isPanning = true;
            lastPanPos = Input.mousePosition;
        }
        if (Input.GetMouseButtonUp(1)) isPanning = false;

        if (isPanning)
        {
            Vector2 pos = Input.mousePosition;
            Vector2 delta = pos - lastPanPos;
            lastPanPos = pos;
            PanBy(delta);
        }


        // ── Scroll : Zoom ─────────────────────────────────────────────────────
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


    void OrthoZoomBy(float scrollSign)
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        float step = zoomStep * zoomSensitivity; // reuse your feel
        float factor = scrollSign > 0f ? (1f - step) : (1f + step);
        orthoSizeTarget = Mathf.Clamp(cam.orthographicSize * factor, orthoMinSize, orthoMaxSize);
    }



    void OrthoPinchTo(float scale)
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        float newSize = cam.orthographicSize / Mathf.Max(0.001f, scale);
        orthoSizeTarget = Mathf.Clamp(newSize, orthoMinSize, orthoMaxSize);
    }


    void HandleTouch()
    {
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);

            if (t.phase == TouchPhase.Began && !OverUI(t.position))
            {
                isOrbiting = true;
                lastOrbitPos = t.position;
                inertiaVel = Vector2.zero;
            }
            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                isOrbiting = false;

            if (isOrbiting && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
            {
                Vector2 delta = t.deltaPosition;
                OrbitBy(delta);
                inertiaVel = delta;
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
                // Pinch zoom
                float pinchNow = Vector2.Distance(t0.position, t1.position);
                float scale = pinchStartDist > 0.001f ? pinchNow / pinchStartDist : 1f;
                distTarget = Mathf.Clamp(pinchStartZoom / scale, minDist, maxDist);

                // Two-finger pan from midpoint movement
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
    /// Rotates the orbit by a screen-pixel delta.
    /// Yaw always stays horizontal (world Y axis) so the horizon never tilts.
    /// Pitch is clamped between minPitch and maxPitch.
    /// </summary>

    void OrbitBy(Vector2 delta)
    {
        if (delta.sqrMagnitude < 0.0001f) return; // ✅ prevents micro jitter

        yaw += delta.x * orbitSensitivity * 0.1f;
        pitch -= delta.y * orbitSensitivity * 0.1f;

        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }


    /// <summary>
    /// Moves the pivot in the camera's view plane.
    /// Horizontal pan is projected onto the world XZ plane (no Y component)
    /// so the floor never tilts. Vertical pan is pure world Y, clamped to
    /// the object's bounding box so the object stays in frame.
    /// </summary>
    void PanBy(Vector2 screenDelta)
    {
        if (isDriverView) return;
        // 1 px = (dist / screenHeight) world units at the pivot plane
        float scale = (distCurrent / Screen.height) * panSensitivity;

        // Camera right = 90° clockwise from the yaw direction, flat on XZ
        float yawRad = yaw * Mathf.Deg2Rad;
        Vector3 rightFlat = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

        // screenDelta.y positive = mouse moved up = scene should pan down
        Vector3 move = rightFlat * screenDelta.x * scale
                     + Vector3.down * screenDelta.y * scale;

        Vector3 proposed = pivotTarget + move;

        // Y clamp — keeps pivot between floor and ceiling of the object bounds
        proposed.y = Mathf.Clamp(proposed.y, floorY, ceilingY);

        // XZ clamp — keeps pivot within panLimit * distance of bounds centre
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

    /// <summary>
    /// Exponential dolly-zoom toward the 3D world point under the cursor.
    /// Each scroll notch removes a fixed fraction of remaining distance so
    /// zooming never overshoots and feels equally responsive at any scale.
    /// </summary>
    void ZoomBy(float scrollSign, Vector2 screenPos)
    {
        if (isDriverView) return;

        // Scroll up (positive) = zoom in = distance shrinks
        float factor = scrollSign > 0f
                        ? 1f - zoomStep * zoomSensitivity
                        : 1f + zoomStep * zoomSensitivity;

        float newDist = Mathf.Clamp(distTarget * factor, minDist, maxDist);

        // Zoom toward cursor: shift pivot so the world point under the cursor
        // stays stationary as the camera moves in/out
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

    // =========================================================================
    // Inertia
    // =========================================================================
    public void ExitDriverView(bool snap = false)
    {
        if (!isDriverView) return;

        isDriverView = false;

        // Restore normal orbit behaviour
        FitToTarget();

        inertiaVel = Vector2.zero;

        if (snap)
        {
            distCurrent = distTarget;
            pivotCurrent = pivotTarget;
            yawCurrent = yaw;
            pitchCurrent = pitch;
            CommitTransform();
        }
    }
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
    // Transform — the heart of the whole thing
    // =========================================================================

    /// <summary>
    /// Builds the camera rotation from yaw + pitch, then positions the camera
    /// BEHIND the pivot along that rotation's +Z axis.
    ///
    /// OrbitRotation(yaw, pitch) produces a quaternion that LOOKS AT the pivot,
    /// so the camera at (pivot + rotation * (0,0,+dist)) is always outside —
    /// regardless of yaw/pitch values. It is geometrically impossible for
    /// this formula to place the camera inside the target.
    /// </summary>
    void SmoothAndCommit()
    {
        if (useOrthographic2D)
        {
            pivotCurrent = Vector3.SmoothDamp(pivotCurrent, pivotTarget, ref pivotVel, smoothTime);

            var cam = GetComponent<Camera>();

            if (cam != null && cam.orthographic)
            {
                orthoSizeCurrent = Mathf.SmoothDamp(orthoSizeCurrent, orthoSizeTarget, ref orthoSizeVel, smoothTime);
                cam.orthographicSize = orthoSizeCurrent;
            }

            // ✅ Use fixed rotation (NO spherical math)
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

            // ✅ Always place camera directly along its forward axis
            Vector3 forward = rot * Vector3.forward;
            transform.position = pivotCurrent - forward * orthoDepth;

            transform.rotation = rot;

            return;
        }

        // Perspective path (unchanged)
        distCurrent = Mathf.SmoothDamp(distCurrent, distTarget, ref distVel, smoothTime);
        pivotCurrent = Vector3.SmoothDamp(pivotCurrent, pivotTarget, ref pivotVel, smoothTime);
        yawCurrent = Mathf.SmoothDampAngle(yawCurrent, yaw, ref yawVel, smoothTime);
        pitchCurrent = Mathf.SmoothDamp(pitchCurrent, pitch, ref pitchVel, smoothTime);
        CommitTransform();
    }

    static bool IsScreenPointInside(Vector2 p)
    {
        return p.x >= 0f && p.y >= 0f && p.x <= Screen.width && p.y <= Screen.height;
    }
    void CommitTransform()
    {
        // ── Spherical coordinate camera placement ─────────────────────────────
        //
        //  yaw   = horizontal angle around world Y (0° = camera in front of object on +Z)
        //  pitch = vertical elevation angle        (0° = horizon, 90° = directly above)
        //
        //  Convert to a direction vector on the unit sphere, then scale by distance.
        //  The camera sits AT that offset FROM the pivot, looking BACK at the pivot.
        //  This is exactly how every 3D object viewer works. It is impossible for
        //  this formula to place the camera inside the target.

        float yawRad = yawCurrent * Mathf.Deg2Rad;
        float pitchRad = pitchCurrent * Mathf.Deg2Rad;

        // Unit vector pointing FROM pivot TO camera (outward on the sphere)
        Vector3 dir = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),   // X
            Mathf.Sin(pitchRad),                           // Y  (height above pivot)
            Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)    // Z
        );

        // Camera position: outside the sphere, at distance from pivot
        Vector3 camPos = pivotCurrent + dir * distCurrent;

        // Rotation: look from camera position back toward pivot
        // Use world up unless we're nearly directly overhead
        Vector3 up = Mathf.Abs(pitchCurrent - 90f) < 1f ? transform.forward : Vector3.up;
        Quaternion rot = Quaternion.LookRotation(pivotCurrent - camPos, up);

        transform.position = camPos;
        transform.rotation = rot;
    }

    // =========================================================================
    // Frame / Focus
    // =========================================================================

    /// <summary>
    /// Full frame reset — flies to the default framed view of the target.
    /// Called by pressing F key.
    /// </summary>
    public void FrameTarget()
    {
        ExitDriverView();
        if (target == null) return;
        FitToTarget();
        yaw = startYaw;
        pitch = startPitch;
        inertiaVel = Vector2.zero;
    }

    /// <summary>
    /// Double-click zoom: move the pivot toward the 3D point under the cursor
    /// and zoom in by a comfortable step — like double-click in Sketchfab / Marmoset.
    /// Does NOT reset yaw/pitch/distance so the user stays oriented.
    /// </summary>
    void ZoomToClickPoint(Vector2 screenPos)
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null) return;

        // Cast a ray and try to hit something real first, then fall back to pivot plane
        Ray ray = cam.ScreenPointToRay(screenPos);

        Vector3 targetPoint = pivotTarget; // fallback = current pivot
        bool hit = false;

        // Try physics raycast against the actual object geometry
        RaycastHit physHit;
        if (Physics.Raycast(ray, out physHit, maxDist * 2f))
        {
            targetPoint = physHit.point;
            hit = true;
        }

        // If no collider, intersect the pivot plane (works without colliders)
        if (!hit)
        {
            Plane plane = new Plane(-ray.direction, pivotTarget);
            float enter;
            if (plane.Raycast(ray, out enter))
                targetPoint = ray.GetPoint(enter);
        }

        // Move pivot toward the hit point
        pivotTarget = targetPoint;
        pivotTarget.y = Mathf.Clamp(pivotTarget.y, floorY, ceilingY);

        // Zoom in by ~40% of current distance, but never past minDist
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

    /// <summary>
    /// Set orbit angles. Yaw = horizontal (0 = front), Pitch = vertical elevation.
    /// </summary>
    public void SetAngles(float yawDeg, float pitchDeg)
    {
        yaw = yawDeg;

        if (useOrthographic2D)
            pitch = pitchDeg; // ✅ NO clamp in 2D
        else
            pitch = Mathf.Clamp(pitchDeg, minPitch, maxPitch);

        inertiaVel = Vector2.zero;
    }
    public void SetDistance(float d)
    {
        distTarget = Mathf.Clamp(d, minDist, maxDist);
    }

    public void ResetView()
    {
        ExitDriverView();
        distTarget = savedDist;
        pivotTarget = savedPivot;
        yaw = startYaw;
        pitch = startPitch;
        inertiaVel = Vector2.zero;
        ApplyProjectionMode(snap: false);
    }

    public void Enter2DViewMode(bool snap = false)
    {
        useOrthographic2D = true;
        ApplyProjectionMode(snap);           // switches Camera.orthographic = true and frames content
    }

    public void Exit2DViewMode(bool snap = false)
    {
        useOrthographic2D = false;
        ApplyProjectionMode(snap);           // switches back to perspective
    }

    public void Toggle2DViewMode(bool snap = true)
    {
        // ✅ stop all motion first
        isOrbiting = false;
        isPanning = false;
        inertiaVel = Vector2.zero;

        useOrthographic2D = !useOrthographic2D;

        if (useOrthographic2D)
        {
            ApplyProjectionMode(true);
        }
        else
        {
            // ✅ IMPORTANT: don't call ResetView() first
            ApplyProjectionMode(true);
        }

        Update2DButtonLabel();
    }


    void Update2DButtonLabel()
    {
        if (twoDModeButtonLabel == null) return;
        twoDModeButtonLabel.text = useOrthographic2D ? "Exit 2D View Mode" : "Enter 2D View Mode";
    }


    void ApplyProjectionMode(bool snap = false)
    {
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        if (useOrthographic2D)
        {
            cam.orthographic = true;

            ApplyOrthographicFacing_Locked();
            ComputeOrthoSizeFromBounds(snap);

            if (snap)
            {
                pivotCurrent = pivotTarget;
                yawCurrent = yaw;
                pitchCurrent = pitch;
            }
        }
        else
        {
            cam.orthographic = false;

            // ✅ FULL reset back to 3D camera state
            yaw = yawCurrent;
            pitch = pitchCurrent;

            distCurrent = distTarget;
            pivotCurrent = pivotTarget;

            inertiaVel = Vector2.zero;

            // ✅ CRITICAL: force immediate transform update
            CommitTransform();
        }

    }

    void ApplyOrthographicFacing_Locked()
    {
        // Always reset pivot to center
        if (target != null)
        {
            var b = CombinedBounds(target);
            pivotTarget = b.center;
        }

        // Exact axis-aligned rotation
        Quaternion rot = Quaternion.identity;

        switch (orthoView)
        {
            case OrthoPreset.Top:
                rot = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                break;

            case OrthoPreset.Front:
                rot = Quaternion.LookRotation(Vector3.back, Vector3.up);
                break;

            case OrthoPreset.Back:
                rot = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                break;

            case OrthoPreset.Right:
                rot = Quaternion.LookRotation(Vector3.left, Vector3.up);
                break;

            case OrthoPreset.Left:
                rot = Quaternion.LookRotation(Vector3.right, Vector3.up);
                break;
        }

        // Extract exact yaw/pitch from rotation
        Vector3 euler = rot.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;

        inertiaVel = Vector2.zero;
    }

    void ComputeOrthoSizeFromBounds(bool snap)
    {
        if (target == null) return;
        var cam = GetComponent<Camera>();
        if (cam == null) return;

        Bounds b = CombinedBounds(target);
        if (b.size == Vector3.zero) return;

        // Build locked right/up axis from chosen facing
        Quaternion look = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 right = look * Vector3.right;
        Vector3 up = look * Vector3.up;

        Vector3 min = b.min, max = b.max;

        // Project 8 corners to camera's 2D plane axes (right/up)
        Vector3[] corners = new Vector3[8];
        int i = 0;
        for (int xi = 0; xi <= 1; xi++)
            for (int yi = 0; yi <= 1; yi++)
                for (int zi = 0; zi <= 1; zi++)
                    corners[i++] = new Vector3(
                        xi == 0 ? min.x : max.x,
                        yi == 0 ? min.y : max.y,
                        zi == 0 ? min.z : max.z
                    );

        float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
        float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
        foreach (var c in corners)
        {
            float r = Vector3.Dot(c, right);
            float u = Vector3.Dot(c, up);
            if (r < minR) minR = r; if (r > maxR) maxR = r;
            if (u < minU) minU = u; if (u > maxU) maxU = u;
        }

        float widthWorld = (maxR - minR) * orthoPadding;
        float heightWorld = (maxU - minU) * orthoPadding;

        // orthographicSize is half the vertical size; ensure width fits via aspect
        float aspect = Mathf.Max(0.0001f, cam.aspect);
        float sizeByHeight = heightWorld * 0.5f;
        float sizeByWidth = (widthWorld * 0.5f) / aspect;

        float finalSize = Mathf.Max(sizeByHeight, sizeByWidth);
        orthoSizeTarget = Mathf.Clamp(finalSize, orthoMinSize, orthoMaxSize);

        if (snap)
        {
            cam.orthographicSize = orthoSizeTarget;
            orthoSizeCurrent = orthoSizeTarget;
        }

        // Update pivot to the bounds center (keeps 2D framing predictable)
        pivotTarget = b.center;
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
            ApplyOrthographicFacing_Locked();
            ComputeOrthoSizeFromBounds(snap: false);
        }

    }




    bool OverUI(Vector2 pos)
    {
        if (EventSystem.current == null) return false;
        ptrData.position = pos;
        rayResults.Clear();
        EventSystem.current.RaycastAll(ptrData, rayResults);
        return rayResults.Count > 0;
    }



    public void SetFrontView()
    {
        if (useOrthographic2D)
        {
            orthoView = OrthoPreset.Front;
            ApplyProjectionMode(true);
        }
        else
        {
            ResetView();
            SetAngles(180f, 10f);
        }
    }

    public void SetRearView()
    {
        if (useOrthographic2D)
        {
            orthoView = OrthoPreset.Back;
            ApplyProjectionMode(true);
        }
        else
        {
            ResetView();
            SetAngles(0f, 10f);
        }
    }

    public void SetRightSideView()
    {
        if (useOrthographic2D)
        {
            orthoView = OrthoPreset.Right;
            ApplyProjectionMode(true);
        }
        else
        {
            ResetView();
            SetAngles(90f, 10f);
        }
    }

    public void SetLeftSideView()
    {
        if (useOrthographic2D)
        {
            orthoView = OrthoPreset.Left;
            ApplyProjectionMode(true);
        }
        else
        {
            ResetView();
            SetAngles(-90f, 10f);
        }
    }

    public void SetTopView()
    {
        if (useOrthographic2D)
        {
            orthoView = OrthoPreset.Top;
            ApplyProjectionMode(true);
        }
        else
        {
            ResetView();
            SetAngles(90f, 85f);
        }
    }
    /// <summary>
  
    /// <summary>
    /// Set camera to 3/4 front view (common presentation angle)
    /// </summary>
    public void SetThreeQuarterView()
    {
        SetAngles(135f, 25f);
        // targetHorizontalAngle = 135f;  // 3/4 front angle
        /// targetVerticalAngle = 25f;     // Good elevation for perspective
    }




    // ==============================
    // NEW: Presets / UI
    // ==============================
    [Header("Driver View — Presets")]
    [Tooltip("Reference transform for the driver's seat/eye anchor (e.g., seat group root).")]
    public Transform driverAnchor; // your _sgrpRef

    [Tooltip("TMP_Dropdown to switch driver height presets at runtime.")]
    public TMP_Dropdown driverPresetDropdown;

    public enum DriverHeightPreset
    {
        Five_00,   // 5'0"
        Five_06,   // 5'6"
        Five_11,   // 5'11" (your current default)
        Six_03     // 6'3"
    }

    // Expose per-vehicle offsets (in local vehicle space) for each preset.
    // These are the same quantity as your existing _driverViewDifference.
    [Tooltip("Local-space offset from driverAnchor to camera for a 5'0\" driver.")]
    public Vector3 driverOffset_5_0 ;

    [Tooltip("Local-space offset from driverAnchor to camera for a 5'6\" driver.")]
    public Vector3 driverOffset_5_6  ;

    [Tooltip("Local-space offset from driverAnchor to camera for a 5'11\" driver.")]
    public Vector3 driverOffset_5_11; // your current baseline

    [Tooltip("Local-space offset from driverAnchor to camera for a 6'3\" driver.")]
    public Vector3 driverOffset_6_3;

    // Internal map (enum → offset)
    private readonly Dictionary<DriverHeightPreset, Vector3> _driverOffsets
        = new Dictionary<DriverHeightPreset, Vector3>();

    // Track current preset (serialized so you can choose a default in Inspector)
    [SerializeField] private DriverHeightPreset currentDriverPreset = DriverHeightPreset.Five_11;

    // Helper to (re)build dictionary
    private void BuildDriverOffsets()
    {
        _driverOffsets.Clear();
        _driverOffsets[DriverHeightPreset.Five_00] = driverOffset_5_0;
        _driverOffsets[DriverHeightPreset.Five_06] = driverOffset_5_6;
        _driverOffsets[DriverHeightPreset.Five_11] = driverOffset_5_11;
        _driverOffsets[DriverHeightPreset.Six_03] = driverOffset_6_3;
    }

    // Populate dropdown and hook event
    private void InitDriverPresetDropdown()
    {
        if (driverPresetDropdown == null) return;

        driverPresetDropdown.ClearOptions();
        var options = new List<TMP_Dropdown.OptionData>
    {
        new TMP_Dropdown.OptionData("5'0\""),
        new TMP_Dropdown.OptionData("5'6\""),
        new TMP_Dropdown.OptionData("5'11\""),
        new TMP_Dropdown.OptionData("6'3\"")
    };
        driverPresetDropdown.AddOptions(options);

        // Set UI to match currentDriverPreset
        driverPresetDropdown.SetValueWithoutNotify((int)currentDriverPreset);

        // Register callback (avoid double-registering)
        driverPresetDropdown.onValueChanged.RemoveListener(OnDriverPresetChanged);
        driverPresetDropdown.onValueChanged.AddListener(OnDriverPresetChanged);
    }

    // Editor-time sync
    private void OnValidate()
    {
        BuildDriverOffsets();
    }

    // Runtime init — call from Start() end, or place inside your existing Start()

    private void InitDriverViewPresetRuntime()
    {
        BuildDriverOffsets();
        InitDriverPresetDropdown();

        // Will no-op if driverAnchor is not yet set.
        ApplyDriverPreset(currentDriverPreset, snap: false);
    }


    public void SetDriverAnchor(Transform anchor, bool applyCurrentPreset = true, bool snap = false)
    {
        driverAnchor = anchor;

        // If we already know the anchor, we can safely apply the current preset now
        if (applyCurrentPreset && driverAnchor != null)
        {
            ApplyDriverPreset(currentDriverPreset, snap);
        }
    }

    // Dropdown callback
    public void OnDriverPresetChanged(int index)
    {
        var preset = (DriverHeightPreset)Mathf.Clamp(index, 0, (int)DriverHeightPreset.Six_03);
        ApplyDriverPreset(preset, snap: false);
    }

    // Core apply method (optionally snap without smoothing)

    public void ApplyDriverPreset(DriverHeightPreset preset, bool snap = false)
    {
        currentDriverPreset = preset;

        if (!_driverOffsets.TryGetValue(preset, out var localOffset))
            return;

        _driverViewDifference = localOffset;

        // If anchor not ready yet, just cache state silently and return.
        if (driverAnchor == null)
        {
            // No warning — this is a valid runtime order. We'll apply when anchor arrives.
            return;
        }

        // With anchor present, position camera
        SetDriverView(driverAnchor);

        if (snap)
        {
            pivotCurrent = pivotTarget;
            distCurrent = distTarget;
            yawCurrent = yaw;
            pitchCurrent = pitch;
            CommitTransform();
        }

        if (driverPresetDropdown != null)
            driverPresetDropdown.SetValueWithoutNotify((int)currentDriverPreset);
    }

    public Vector3 _driverViewDifference;
 

    private static void DirectionToYawPitch(Vector3 dir, out float yawDeg, out float pitchDeg)
    {
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;

        yawDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        
        float y = Mathf.Clamp(dir.y, -1f, 1f);
        pitchDeg = Mathf.Asin(y) * Mathf.Rad2Deg;
    }
    public void SetDriverView(Transform _sgrpRef)
    {
        if (_sgrpRef == null) return;

        isDriverView = true;

        // Camera eye position
        Vector3 camPos = _sgrpRef.position + _driverViewDifference;

        // Determine forward look direction (driver looking forward)
        Vector3 lookDir = _sgrpRef.forward.normalized;

        // ✅ Pivot AT camera position (not in front of it)
        Vector3 newPivot = camPos;

        // Very small distance to preserve math stability
        float driverViewDistance = 0.01f;

        // Compute yaw/pitch from look direction
        Vector3 orbitDir = (-lookDir).normalized;
        DirectionToYawPitch(orbitDir, out float newYaw, out float newPitch);
        newPitch = Mathf.Clamp(newPitch, minPitch, maxPitch);

        // Apply
        pivotTarget = newPivot;
        distTarget = driverViewDistance;
        yaw = newYaw;
        pitch = newPitch;

        inertiaVel = Vector2.zero;
    }


    #endregion
}
