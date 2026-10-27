using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace VehicleMeasurement
{
    /// <summary>
    /// MANUAL MEASURE - click two points on the vehicle and see the distance between them.
    ///
    ///  * Measure button (or the M key) switches it on/off.
    ///  * Click a point, then a second point. A line with the distance (mm) and its length / width / height parts
    ///    stays on screen. Drag still orbits / pans the camera; only a click (no drag) places a point.
    ///  * In the 2D view the distance is measured in the view (side view: length and height), so depth doesn't count.
    ///  * Hold Ctrl to snap to the nearest corner of the face under the cursor - or, with the clip section on, to the
    ///    nearest point on the cut outline (worked out from the meshes, since the cut itself is only drawn by shaders).
    ///  * Esc cancels the first point (or switches measuring off), Backspace/Delete removes the last measurement.
    ///  * With the clip section on, parts that are cut away can't be picked, and a click on a part the cut goes
    ///    through gives the point on the cut face (where it's seen), not a surface somewhere inside the part.
    ///
    /// Lines and labels are drawn on a screen overlay, so they're always visible and the same size at any zoom.
    /// The vehicle gets MeshColliders (only where it has none) when measuring is first switched on for it; they are
    /// added a few at a time so big models don't freeze, and removed when the vehicle changes.
    /// </summary>
    public class ManualMeasureTool : MonoBehaviour
    {
        [Header("Scene")]
        public Camera cam;
        public OrbitCameraController orbit;
        [Tooltip("What can be measured. Empty: the orbit camera's target.")]
        public Transform vehicleRoot;

        [Header("UI (optional)")]
        public Button toggleButton;
        public TMP_Text toggleButtonText;
        public Button clearButton;
        public Button undoButton;
        public string offLabel = "Measure";
        public string onLabel = "Stop Measuring";
        public KeyCode toggleKey = KeyCode.M;
        [Tooltip("Sorting order of the overlay with the lines (below the app's own UI at 0, so panels stay on top).")]
        public int overlaySortingOrder = -1;

        [Header("Look")]
        public Color lineColor = new Color(1f, 0.76f, 0.1f, 1f);
        public Color pendingColor = new Color(0.35f, 0.85f, 1f, 1f);
        public float lineWidthPx = 3f;
        public float dotSizePx = 12f;
        public float labelFontSize = 18f;
        [Range(0, 3)] public int decimals = 1;
        public int maxMeasurements = 20;

        [Header("Picking")]
        [Tooltip("A press that moves less than this is a click (more is a camera drag).")]
        public float clickMaxMovePx = 6f;
        public float maxRayDistance = 1000f;

        /// <summary>A finished measurement (world space, metres). viewNormal is the 2D view's direction, or zero in 3D.</summary>
        public class Measurement
        {
            public Vector3 a, b, viewNormal;
            public Vector3 Delta { get { return InView(b - a); } }
            public float Distance { get { return Delta.magnitude; } }
            public bool In2D { get { return viewNormal != Vector3.zero; } }
            public Vector3 InView(Vector3 d) { return In2D ? d - viewNormal * Vector3.Dot(d, viewNormal) : d; }
        }

        public bool IsActive { get; private set; }
        public IList<Measurement> Measurements { get { return _done.AsReadOnly(); } }
        public event Action<Measurement> MeasurementAdded;

        // ── state ──
        private readonly List<Measurement> _done = new List<Measurement>();
        private bool _hasFirst;
        private Vector3 _first;
        private bool _hasHover;
        private Vector3 _hover;
        private Vector2 _downPos;
        private bool _downOverUI, _mouseDown;
        private bool _doubleClickWas = true;
        private Transform _preparedFor;
        private readonly List<MeshCollider> _addedColliders = new List<MeshCollider>();
        private Coroutine _preparing;
        private int _preparingLeft;
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly Dictionary<Mesh, KeyValuePair<Vector3[], int[]>> _meshCache = new Dictionary<Mesh, KeyValuePair<Vector3[], int[]>>();

        // ── overlay ──
        private Canvas _overlay, _hintCanvas;
        private RectTransform _overlayRoot;
        private TextMeshProUGUI _hint;
        private Image _hintBg;
        private readonly List<Visual> _visuals = new List<Visual>();
        private Visual _pendingVisual;
        private Image _hoverDot;
        private static Sprite _dotSprite;

        private class Visual
        {
            public Image line, dotA, dotB, labelBg;
            public TextMeshProUGUI label;
            public GameObject root;
        }

        private static readonly int ClipXOn = Shader.PropertyToID("_GlobalClipXEnabled"), ClipXPos = Shader.PropertyToID("_GlobalClipXPosition"), ClipXDir = Shader.PropertyToID("_GlobalClipXDirection");
        private static readonly int ClipYOn = Shader.PropertyToID("_GlobalClipYEnabled"), ClipYPos = Shader.PropertyToID("_GlobalClipYPosition"), ClipYDir = Shader.PropertyToID("_GlobalClipYDirection");
        private static readonly int ClipZOn = Shader.PropertyToID("_GlobalClipZEnabled"), ClipZPos = Shader.PropertyToID("_GlobalClipZPosition"), ClipZDir = Shader.PropertyToID("_GlobalClipZDirection");

        // ═════════════════════════════════════════════════════════════════
        #region Lifecycle

        private void Start()
        {
            if (cam == null) cam = orbit != null ? orbit.GetComponent<Camera>() : Camera.main;
            if (orbit == null && cam != null) orbit = cam.GetComponent<OrbitCameraController>();
            BuildOverlay();
            ConnectButtons();
        }

        /// <summary>Hooks up the optional buttons (safe to call again after assigning them from code).</summary>
        public void ConnectButtons()
        {
            if (toggleButton != null) { toggleButton.onClick.RemoveListener(Toggle); toggleButton.onClick.AddListener(Toggle); }
            if (clearButton != null) { clearButton.onClick.RemoveListener(ClearAll); clearButton.onClick.AddListener(ClearAll); }
            if (undoButton != null) { undoButton.onClick.RemoveListener(UndoLast); undoButton.onClick.AddListener(UndoLast); }
            RefreshButtons();
        }

        private void OnDisable()
        {
            if (IsActive) SetActive(false);
        }

        private void OnDestroy()
        {
            RemoveAddedColliders();
            if (_overlay != null) Destroy(_overlay.gameObject);
            if (_hintCanvas != null) Destroy(_hintCanvas.gameObject);
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey) && !TypingInField()) Toggle();

            // The vehicle was unloaded or replaced
            if (_preparedFor != null && _preparedFor != Root) ResetForVehicle();

            if (IsActive)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (_hasFirst) _hasFirst = false; else SetActive(false);
                }
                if ((Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) && !TypingInField()) UndoLast();
                if (IsActive) HandlePointer();
            }
            else
            {
                _hasHover = false;
                _hasFirst = false;
            }
            DrawOverlay();
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Public API

        /// <summary>The vehicle to measure (call when a vehicle is loaded). Clears the measurements of the previous one.</summary>
        public void SetVehicle(Transform root)
        {
            vehicleRoot = root;
            ResetForVehicle();
        }

        public void Toggle() { SetActive(!IsActive); }

        public void SetActive(bool on)
        {
            if (on == IsActive) return;
            IsActive = on;
            _hasFirst = false;
            _hasHover = false;
            if (orbit != null)
            {
                if (on) { _doubleClickWas = orbit.doubleClickFocus; orbit.doubleClickFocus = false; }   // a double-click shouldn't move the camera while picking
                else orbit.doubleClickFocus = _doubleClickWas;
            }
            if (on) PrepareVehicle();
            RefreshButtons();
        }

        public void UndoLast()
        {
            if (_hasFirst) { _hasFirst = false; return; }
            if (_done.Count == 0) return;
            _done.RemoveAt(_done.Count - 1);
        }

        public void ClearAll()
        {
            _done.Clear();
            _hasFirst = false;
        }

        /// <summary>"1234.5 mm" style text for a length in metres.</summary>
        public string Mm(float metres) { return (metres * 1000f).ToString("F" + decimals) + " mm"; }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Picking

        private Transform Root { get { return vehicleRoot != null ? vehicleRoot : (orbit != null ? orbit.target : null); } }

        private void HandlePointer()
        {
            Vector2 pos = Input.mousePosition;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            _hasHover = !overUI && _preparingLeft == 0 && Pick(pos, SnapHeld(), out _hover);

            if (Input.GetMouseButtonDown(0)) { _mouseDown = true; _downPos = pos; _downOverUI = overUI; }
            if (Input.GetMouseButtonUp(0) && _mouseDown)
            {
                _mouseDown = false;
                bool click = !_downOverUI && !overUI && (pos - _downPos).magnitude <= clickMaxMovePx;
                if (click && _hasHover) PlacePoint(_hover);
            }
        }

        private void PlacePoint(Vector3 p)
        {
            if (!_hasFirst) { _first = p; _hasFirst = true; return; }
            var m = new Measurement { a = _first, b = p, viewNormal = ViewNormal() };
            _hasFirst = false;
            if ((m.b - m.a).sqrMagnitude < 1e-10f) return;   // same point twice
            _done.Add(m);
            while (_done.Count > Mathf.Max(1, maxMeasurements)) _done.RemoveAt(0);
            Debug.Log("[ManualMeasure] " + Mm(m.Distance) + (m.In2D ? " (in the 2D view)" : "") + "  from " + m.a.ToString("F4") + " to " + m.b.ToString("F4"));
            if (MeasurementAdded != null) MeasurementAdded(m);
        }

        /// <summary>The 2D view's direction (an exact axis) when the camera is in 2D, else zero.</summary>
        private Vector3 ViewNormal()
        {
            if (orbit == null || !orbit.Is2D || cam == null) return Vector3.zero;
            Vector3 f = cam.transform.forward;
            Vector3 a = new Vector3(Mathf.Abs(f.x), Mathf.Abs(f.y), Mathf.Abs(f.z));
            return a.x >= a.y && a.x >= a.z ? Vector3.right : a.y >= a.z ? Vector3.up : Vector3.forward;
        }

        /// <summary>
        /// The point under the cursor: the nearest visible vehicle surface - or, with the clip section on, the cut face
        /// itself when the click lands on a part that the cut goes through (instead of a surface somewhere inside it).
        /// </summary>
        private bool Pick(Vector2 screen, bool snap, out Vector3 point)
        {
            point = Vector3.zero;
            if (!CameraUsable(screen)) return false;
            Transform root = Root;
            Ray ray = cam.ScreenPointToRay(screen);
            List<ClipPlane> planes = ActiveClipPlanes();

            // With a cut, the clip materials draw both sides of every face, so inside surfaces count as hits too
            bool backfaces = Physics.queriesHitBackfaces;
            int n;
            try
            {
                if (planes.Count > 0) Physics.queriesHitBackfaces = true;
                n = Physics.RaycastNonAlloc(ray, _hits, maxRayDistance, ~0, QueryTriggerInteraction.Ignore);
            }
            finally { Physics.queriesHitBackfaces = backfaces; }

            int best = -1;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = _hits[i];
                if (best >= 0 && h.distance >= _hits[best].distance) continue;
                if (root != null && !h.transform.IsChildOf(root)) continue;
                Renderer r = h.collider.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                if (IsClipped(h.point, planes)) continue;
                best = i;
            }

            // Ctrl with a cut: the nearest point on the cut outline (the edges you see where the cut goes through
            // parts). The cut is only drawn by the shaders - the meshes aren't really cut - so the outline is worked
            // out from the mesh triangles where they cross the cut plane.
            Vector3 edge;
            if (snap && planes.Count > 0 && SnapToCutEdge(ray, screen, planes, root, best >= 0 ? _hits[best].distance : maxRayDistance, out edge))
            {
                point = edge;
                return true;
            }
            if (best < 0) return false;

            // The ray came through the cut-away part and crossed the cut plane before reaching this surface. If that
            // surface is the inside of a part (we see its back), the cut plane went through that part: the point the
            // person sees and means is on the cut face.
            Vector3 entry; float tEntry;
            if (planes.Count > 0 && SectionEntry(ray, _hits[best].distance, planes, out entry, out tEntry) && FacesAway(_hits[best], ray.direction))
            {
                point = entry;
                return true;
            }
            point = _hits[best].point;
            if (snap) point = SnapToCorner(_hits[best], point);
            return true;
        }

        // ── snapping to the cut outline ──
        [Tooltip("Ctrl snaps to the cut outline within this many pixels of the cursor.")]
        public float edgeSnapPixels = 18f;
        private readonly Collider[] _near = new Collider[256];
        private readonly Dictionary<MeshCollider, KeyValuePair<string, List<Vector3>>> _sectionCache = new Dictionary<MeshCollider, KeyValuePair<string, List<Vector3>>>();

        /// <summary>
        /// Nearest point on the cut outline to where the cursor ray meets the cut plane, if one is within
        /// <see cref="edgeSnapPixels"/>. Only parts near that spot are looked at; each part's outline is worked out once
        /// per cut position.
        /// </summary>
        private bool SnapToCutEdge(Ray ray, Vector2 screen, List<ClipPlane> planes, Transform root, float maxDistance, out Vector3 edge)
        {
            edge = Vector3.zero;
            Vector3 onPlane; float t;
            if (!SectionEntry(ray, maxDistance + 1e-3f, planes, out onPlane, out t)) return false;
            ClipPlane plane = planes[0];
            for (int i = 0; i < planes.Count; i++) if (Mathf.Abs(Axis(onPlane, planes[i].axis) - planes[i].position) < 1e-3f) { plane = planes[i]; break; }

            float radius = WorldPerPixel(onPlane) * edgeSnapPixels;
            int n = Physics.OverlapSphereNonAlloc(onPlane, radius, _near, ~0, QueryTriggerInteraction.Ignore);
            float bestD = radius; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var mc = _near[i] as MeshCollider;
                if (mc == null || mc.sharedMesh == null) continue;
                if (root != null && !mc.transform.IsChildOf(root)) continue;
                Renderer r = mc.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                List<Vector3> segs = SectionOutline(mc, plane);
                Vector3 q; float d;
                if (segs != null && NearestOnSegments(segs, onPlane, out q, out d) && d <= bestD && !IsClipped(q, planes.FindAll(p => p.axis != plane.axis)))
                {
                    bestD = d; edge = q; found = true;
                }
            }
            return found;
        }

        /// <summary>World size of one screen pixel at a point (for "within N pixels").</summary>
        private float WorldPerPixel(Vector3 at)
        {
            float h = Mathf.Max(1f, cam.pixelRect.height);
            if (cam.orthographic) return 2f * cam.orthographicSize / h;
            float depth = Mathf.Max(0.01f, Vector3.Dot(at - cam.transform.position, cam.transform.forward));
            return 2f * depth * Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.01f, Mathf.Cos(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / h;
        }

        /// <summary>A part's outline on a cut plane (pairs of points), worked out once per cut position.</summary>
        private List<Vector3> SectionOutline(MeshCollider mc, ClipPlane plane)
        {
            string key = plane.axis + ":" + plane.position.ToString("R");
            KeyValuePair<string, List<Vector3>> cached;
            if (_sectionCache.TryGetValue(mc, out cached) && cached.Key == key) return cached.Value;
            KeyValuePair<Vector3[], int[]> data = MeshData(mc.sharedMesh);
            List<Vector3> segs = null;
            if (data.Key != null && data.Value != null)
            {
                var world = new Vector3[data.Key.Length];
                Transform tr = mc.transform;
                for (int i = 0; i < world.Length; i++) world[i] = tr.TransformPoint(data.Key[i]);
                segs = new List<Vector3>();
                PlaneSegments(world, data.Value, plane.axis, plane.position, segs);
            }
            _sectionCache[mc] = new KeyValuePair<string, List<Vector3>>(key, segs);
            return segs;
        }

        /// <summary>Where the triangles cross the plane "axis = position": one segment (two points) per crossing triangle.</summary>
        public static void PlaneSegments(Vector3[] verts, int[] tris, int axis, float position, List<Vector3> segments)
        {
            var pts = new Vector3[2];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                float da = Axis(a, axis) - position, db = Axis(b, axis) - position, dc = Axis(c, axis) - position;
                if ((da > 0f && db > 0f && dc > 0f) || (da < 0f && db < 0f && dc < 0f)) continue;   // all on one side
                int k = 0;
                Cross(a, b, da, db, pts, ref k); Cross(b, c, db, dc, pts, ref k); Cross(c, a, dc, da, pts, ref k);
                if (k == 2) { segments.Add(pts[0]); segments.Add(pts[1]); }
            }
        }

        private static void Cross(Vector3 p, Vector3 q, float dp, float dq, Vector3[] pts, ref int k)
        {
            if (k >= 2) return;
            if ((dp > 0f) == (dq > 0f) && dp != 0f && dq != 0f) return;        // same side, no crossing
            if (dp == dq) return;                                              // edge lying in the plane: its ends come from the other edges
            float s = dp / (dp - dq);
            Vector3 x = p + (q - p) * s;
            if (k == 1 && (x - pts[0]).sqrMagnitude < 1e-14f) return;           // a vertex on the plane counted twice
            pts[k++] = x;
        }

        /// <summary>The closest point to p on any of the segments (pairs of points).</summary>
        public static bool NearestOnSegments(List<Vector3> segments, Vector3 p, out Vector3 nearest, out float distance)
        {
            nearest = Vector3.zero; distance = float.MaxValue;
            for (int i = 0; i + 1 < segments.Count; i += 2)
            {
                Vector3 a = segments[i], ab = segments[i + 1] - a;
                float len2 = ab.sqrMagnitude;
                float s = len2 > 1e-14f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                Vector3 q = a + ab * s;
                float d = (q - p).magnitude;
                if (d < distance) { distance = d; nearest = q; }
            }
            return distance < float.MaxValue;
        }

        /// <summary>The camera can turn this screen point into a ray (avoids Unity's "Screen position out of view frustum").</summary>
        private bool CameraUsable(Vector2 screen)
        {
            if (cam == null || !cam.isActiveAndEnabled) return false;
            if (cam.orthographic ? !(cam.orthographicSize > 1e-4f) : !(cam.fieldOfView > 0.01f)) return false;
            if (!(cam.farClipPlane > cam.nearClipPlane)) return false;
            Rect r = cam.pixelRect;
            return r.width > 1f && r.height > 1f && r.Contains(screen);
        }

        /// <summary>A clip plane of the clip section: points with (position[axis] - position) * direction > 0 are cut away.</summary>
        public struct ClipPlane
        {
            public int axis; public float position, direction;
            public ClipPlane(int axis, float position, float direction) { this.axis = axis; this.position = position; this.direction = direction; }
            public bool Cuts(Vector3 p) { return (Axis(p, axis) - position) * direction > 0f; }
        }

        private static float Axis(Vector3 v, int a) { return a == 0 ? v.x : a == 1 ? v.y : v.z; }

        /// <summary>The cut planes that are on now (same globals and rule as the clip shaders, DASClip.hlsl).</summary>
        private readonly List<ClipPlane> _planes = new List<ClipPlane>(3);
        private List<ClipPlane> ActiveClipPlanes()
        {
            _planes.Clear();
            if (Shader.GetGlobalFloat(ClipXOn) > 0.5f) _planes.Add(new ClipPlane(0, Shader.GetGlobalFloat(ClipXPos), Shader.GetGlobalFloat(ClipXDir)));
            if (Shader.GetGlobalFloat(ClipYOn) > 0.5f) _planes.Add(new ClipPlane(1, Shader.GetGlobalFloat(ClipYPos), Shader.GetGlobalFloat(ClipYDir)));
            if (Shader.GetGlobalFloat(ClipZOn) > 0.5f) _planes.Add(new ClipPlane(2, Shader.GetGlobalFloat(ClipZPos), Shader.GetGlobalFloat(ClipZDir)));
            return _planes;
        }

        public static bool IsClipped(Vector3 p, List<ClipPlane> planes)
        {
            for (int i = 0; i < planes.Count; i++) if (planes[i].Cuts(p)) return true;
            return false;
        }

        /// <summary>
        /// Where the ray leaves the cut-away space and enters the kept part through a cut plane, before
        /// <paramref name="maxDistance"/>. False when the ray starts in the kept part or doesn't get there in time.
        /// </summary>
        public static bool SectionEntry(Ray ray, float maxDistance, List<ClipPlane> planes, out Vector3 point, out float distance)
        {
            point = Vector3.zero; distance = -1f;
            for (int i = 0; i < planes.Count; i++)
            {
                ClipPlane pl = planes[i];
                float o = Axis(ray.origin, pl.axis), d = Axis(ray.direction, pl.axis);
                if (!pl.Cuts(ray.origin) || Mathf.Abs(d) < 1e-6f) continue;     // only rays coming from the cut-away side
                float t = (pl.position - o) / d;
                if (t > 0f && t > distance) distance = t;                        // the last plane crossed is where it enters
            }
            if (distance <= 0f || distance >= maxDistance) return false;
            point = ray.GetPoint(distance);
            // Exactly on the plane (no float drift along the cut axis), and not cut away by another plane
            for (int i = 0; i < planes.Count; i++)
            {
                ClipPlane pl = planes[i];
                if (Mathf.Abs(Axis(point, pl.axis) - pl.position) < 1e-3f) point = SetAxis(point, pl.axis, pl.position);
            }
            Vector3 justInside = ray.GetPoint(distance + 1e-4f);
            return !IsClipped(justInside, planes);
        }

        private static Vector3 SetAxis(Vector3 v, int a, float value) { if (a == 0) v.x = value; else if (a == 1) v.y = value; else v.z = value; return v; }

        /// <summary>True when the hit surface faces away from the viewer (we're looking at the inside of a part).</summary>
        private bool FacesAway(RaycastHit hit, Vector3 rayDir)
        {
            Vector3 n;
            if (!TriangleNormal(hit, out n)) n = hit.normal;
            return Vector3.Dot(n, rayDir) > 0f;
        }

        /// <summary>The front-face normal of the hit triangle from the mesh itself (Unity's winding: Cross(b - a, c - a)).</summary>
        private bool TriangleNormal(RaycastHit hit, out Vector3 normal)
        {
            normal = Vector3.zero;
            var mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return false;
            KeyValuePair<Vector3[], int[]> data = MeshData(mc.sharedMesh);
            if (data.Key == null || data.Value == null || hit.triangleIndex * 3 + 2 >= data.Value.Length) return false;
            int i0 = data.Value[hit.triangleIndex * 3], i1 = data.Value[hit.triangleIndex * 3 + 1], i2 = data.Value[hit.triangleIndex * 3 + 2];
            if (i0 >= data.Key.Length || i1 >= data.Key.Length || i2 >= data.Key.Length) return false;
            Transform t = mc.transform;
            Vector3 a = t.TransformPoint(data.Key[i0]), b = t.TransformPoint(data.Key[i1]), c = t.TransformPoint(data.Key[i2]);
            normal = Vector3.Cross(b - a, c - a);
            Vector3 s = t.lossyScale;
            if (s.x * s.y * s.z < 0f) normal = -normal;                          // mirrored parts flip their winding
            return normal.sqrMagnitude > 1e-20f;
        }

        private KeyValuePair<Vector3[], int[]> MeshData(Mesh mesh)
        {
            KeyValuePair<Vector3[], int[]> data;
            if (!_meshCache.TryGetValue(mesh, out data))
            {
                try { data = new KeyValuePair<Vector3[], int[]>(mesh.vertices, mesh.triangles); }
                catch (Exception) { data = new KeyValuePair<Vector3[], int[]>(null, null); }       // mesh not readable
                _meshCache[mesh] = data;
            }
            return data;
        }

        private static bool SnapHeld() { return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl); }

        /// <summary>Nearest corner of the triangle that was hit (Ctrl).</summary>
        private Vector3 SnapToCorner(RaycastHit hit, Vector3 fallback)
        {
            var mc = hit.collider as MeshCollider;
            if (mc == null || mc.sharedMesh == null || hit.triangleIndex < 0) return fallback;
            KeyValuePair<Vector3[], int[]> data = MeshData(mc.sharedMesh);
            if (data.Key == null || data.Value == null || hit.triangleIndex * 3 + 2 >= data.Value.Length) return fallback;
            Transform t = mc.transform;
            Vector3 best = fallback; float bestD = float.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                int vi = data.Value[hit.triangleIndex * 3 + k];
                if (vi < 0 || vi >= data.Key.Length) continue;
                Vector3 w = t.TransformPoint(data.Key[vi]);
                float d = (w - hit.point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = w; }
            }
            return best;
        }

        private static bool TypingInField()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var tmp = es.currentSelectedGameObject.GetComponent<TMP_InputField>();
            if (tmp != null && tmp.isFocused) return true;
            var legacy = es.currentSelectedGameObject.GetComponent<InputField>();
            return legacy != null && legacy.isFocused;
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Colliders for picking

        private void ResetForVehicle()
        {
            if (_preparing != null) { StopCoroutine(_preparing); _preparing = null; }
            _preparingLeft = 0;
            RemoveAddedColliders();
            _meshCache.Clear();
            _sectionCache.Clear();
            _preparedFor = null;
            ClearAll();
            if (IsActive) PrepareVehicle();
        }

        private void PrepareVehicle()
        {
            Transform root = Root;
            if (root == null || _preparedFor == root || _preparing != null) return;
            _preparing = StartCoroutine(AddColliders(root));
        }

        /// <summary>MeshColliders on the vehicle's meshes that have no collider, a few milliseconds' worth per frame.</summary>
        private IEnumerator AddColliders(Transform root)
        {
            var todo = new List<MeshFilter>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null && mf.GetComponent<MeshRenderer>() != null) todo.Add(mf);
            _preparingLeft = todo.Count;
            var sw = Stopwatch.StartNew();
            int failed = 0;
            foreach (MeshFilter mf in todo)
            {
                if (mf != null && mf.GetComponent<Collider>() == null)
                {
                    try
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;          // exact surface hits
                        _addedColliders.Add(mc);
                    }
                    catch (Exception) { failed++; }
                }
                _preparingLeft--;
                if (sw.ElapsedMilliseconds > 8) { yield return null; sw.Reset(); sw.Start(); }
                if (root == null) break;
            }
            _preparingLeft = 0;
            _preparing = null;
            _preparedFor = root;
            Debug.Log("[ManualMeasure] Ready to measure " + (root != null ? root.name : "?") + ": " + _addedColliders.Count + " collider(s) added" + (failed > 0 ? ", " + failed + " part(s) skipped" : ""));
        }

        private void RemoveAddedColliders()
        {
            foreach (MeshCollider mc in _addedColliders) if (mc != null) Destroy(mc);
            _addedColliders.Clear();
        }

        #endregion

        // ═════════════════════════════════════════════════════════════════
        #region Overlay drawing

        private void BuildOverlay()
        {
            _overlay = NewCanvas("ManualMeasureOverlay", overlaySortingOrder);
            _overlayRoot = (RectTransform)_overlay.transform;
            _hoverDot = NewImage(_overlayRoot, "Hover", Dot(), pendingColor);
            _hoverDot.rectTransform.sizeDelta = Vector2.one * dotSizePx;
            _pendingVisual = NewVisual(pendingColor);

            _hintCanvas = NewCanvas("ManualMeasureHint", 500);
            _hintBg = NewImage((RectTransform)_hintCanvas.transform, "HintBg", null, new Color(0.06f, 0.06f, 0.08f, 0.88f));
            var bgRt = _hintBg.rectTransform;
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0f);
            bgRt.pivot = new Vector2(0.5f, 0f);
            bgRt.anchoredPosition = new Vector2(0f, 96f);
            _hint = NewText(bgRt, "Hint", 15f, new Color(0.92f, 0.95f, 1f));
            _hint.alignment = TextAlignmentOptions.Center;
            _hintCanvas.gameObject.SetActive(false);
        }

        private static Canvas NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.overrideSorting = true;
            // No GraphicRaycaster: the overlay never takes clicks (the camera's "over UI" check ignores it)
            return c;
        }

        private static Image NewImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;   // positions are screen pixels from the bottom-left
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        private static TextMeshProUGUI NewText(RectTransform parent, string name, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = true;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12f, 6f); rt.offsetMax = new Vector2(-12f, -6f);
            return t;
        }

        private Visual NewVisual(Color color)
        {
            var v = new Visual();
            var go = new GameObject("Measurement", typeof(RectTransform));
            go.transform.SetParent(_overlayRoot, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            v.root = go;
            v.line = NewImage(rt, "Line", null, color);
            v.line.rectTransform.pivot = new Vector2(0f, 0.5f);
            v.dotA = NewImage(rt, "A", Dot(), color);
            v.dotB = NewImage(rt, "B", Dot(), color);
            v.dotA.rectTransform.sizeDelta = v.dotB.rectTransform.sizeDelta = Vector2.one * dotSizePx;
            v.labelBg = NewImage(rt, "Label", null, new Color(0.06f, 0.06f, 0.08f, 0.85f));
            v.label = NewText(v.labelBg.rectTransform, "Text", labelFontSize, Color.white);
            v.label.alignment = TextAlignmentOptions.Center;
            go.SetActive(false);
            return v;
        }

        private void DrawOverlay()
        {
            if (_overlay == null) return;
            while (_visuals.Count < _done.Count) _visuals.Add(NewVisual(lineColor));
            for (int i = 0; i < _visuals.Count; i++)
            {
                if (i < _done.Count) Place(_visuals[i], _done[i].a, _done[i].b, _done[i], lineColor);
                else _visuals[i].root.SetActive(false);
            }

            // First point placed: a line from it to the cursor with the distance so far
            if (IsActive && _hasFirst)
            {
                Vector3 end = _hasHover ? _hover : _first;
                var live = new Measurement { a = _first, b = end, viewNormal = ViewNormal() };
                Place(_pendingVisual, _first, end, _hasHover ? live : null, pendingColor);
            }
            else _pendingVisual.root.SetActive(false);

            Vector2 hs;
            bool showHover = IsActive && _hasHover && ToScreen(_hover, out hs);
            _hoverDot.gameObject.SetActive(showHover);
            if (showHover) _hoverDot.rectTransform.anchoredPosition = ToScreen(_hover);

            UpdateHint();
        }

        private void Place(Visual v, Vector3 a, Vector3 b, Measurement m, Color color)
        {
            Vector2 sa, sb;
            if (!ToScreen(a, out sa) || !ToScreen(b, out sb)) { v.root.SetActive(false); return; }
            v.root.SetActive(true);
            v.line.color = v.dotA.color = v.dotB.color = color;
            Vector2 d = sb - sa;
            var lr = v.line.rectTransform;
            lr.anchoredPosition = sa;
            lr.sizeDelta = new Vector2(d.magnitude, lineWidthPx);
            lr.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            v.dotA.rectTransform.anchoredPosition = sa;
            v.dotB.rectTransform.anchoredPosition = sb;
            v.dotB.gameObject.SetActive(m != null);

            v.labelBg.gameObject.SetActive(m != null);
            if (m == null) return;
            v.label.text = LabelText(m);
            Vector2 pref = v.label.GetPreferredValues(v.label.text);
            var br = v.labelBg.rectTransform;
            br.sizeDelta = new Vector2(pref.x + 24f, pref.y + 12f);
            // Beside the middle of the line, off to the side so it doesn't cover it
            Vector2 mid = (sa + sb) * 0.5f;
            Vector2 side = d.sqrMagnitude > 1f ? new Vector2(-d.y, d.x).normalized : Vector2.up;
            if (side.y < 0f) side = -side;
            br.anchoredPosition = mid + side * (br.sizeDelta.y * 0.5f + 10f);
        }

        /// <summary>Distance, then its parts along the vehicle's length (Unity Z), width (X) and height (Y).</summary>
        private string LabelText(Measurement m)
        {
            Vector3 d = m.Delta;
            var parts = new List<string>();
            if (!m.In2D || m.viewNormal != Vector3.forward) parts.Add("L " + Num(Mathf.Abs(d.z)));
            if (!m.In2D || m.viewNormal != Vector3.right) parts.Add("W " + Num(Mathf.Abs(d.x)));
            if (!m.In2D || m.viewNormal != Vector3.up) parts.Add("H " + Num(Mathf.Abs(d.y)));
            return "<b>" + Mm(m.Distance) + "</b>" + (m.In2D ? " <size=75%>(in view)</size>" : "") +
                   "\n<size=72%><color=#C8D2E0>" + string.Join("   ", parts.ToArray()) + "</color></size>";
        }

        private string Num(float metres) { return (metres * 1000f).ToString("F" + decimals); }

        private void UpdateHint()
        {
            string text = null;
            if (IsActive)
            {
                if (_preparingLeft > 0) text = "Getting the vehicle ready for measuring...";
                else if (Root == null) text = "Load a vehicle to measure.";
                else
                {
                    string ctrl = ActiveClipPlanes().Count > 0 ? "Ctrl: snap to the cut edge" : "Ctrl: snap to corner";
                    text = !_hasFirst ? "<b>Measure:</b> click the first point   ·   " + ctrl + "   ·   Esc: stop"
                                      : "<b>Measure:</b> click the second point   ·   " + ctrl + "   ·   Esc: cancel";
                }
                if (_done.Count > 0 && _preparingLeft == 0) text += "   ·   Backspace: remove last";
            }
            _hintCanvas.gameObject.SetActive(text != null);
            if (text == null) return;
            if (_hint.text != text)
            {
                _hint.text = text;
                Vector2 pref = _hint.GetPreferredValues(text);
                _hintBg.rectTransform.sizeDelta = new Vector2(pref.x + 28f, pref.y + 14f);
            }
        }

        private bool ToScreen(Vector3 world, out Vector2 screen)
        {
            screen = Vector2.zero;
            if (cam == null) return false;
            Vector3 s = cam.WorldToScreenPoint(world);
            if (s.z <= 0f) return false;            // behind the camera
            screen = new Vector2(s.x, s.y);
            return true;
        }

        private Vector2 ToScreen(Vector3 world) { Vector2 s; ToScreen(world, out s); return s; }

        private void RefreshButtons()
        {
            if (toggleButtonText != null) toggleButtonText.text = IsActive ? onLabel : offLabel;
        }

        /// <summary>A round dot, made once (no texture asset needed).</summary>
        private static Sprite Dot()
        {
            if (_dotSprite != null) return _dotSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - r) * (x + 0.5f - r) + (y + 0.5f - r) * (y + 0.5f - r));
                    float inner = Mathf.Clamp01(r - 6f - d + 0.5f);            // filled centre
                    float ring = Mathf.Clamp01(r - 1f - d + 0.5f);             // edge (dark outline for contrast)
                    byte c = (byte)(255 * inner + 30 * (1f - inner));
                    px[y * size + x] = new Color32(c, c, c, (byte)(255 * ring));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _dotSprite;
        }

        #endregion
    }
}
