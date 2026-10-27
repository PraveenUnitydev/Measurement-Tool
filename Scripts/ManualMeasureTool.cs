using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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
    ///  * Hold Ctrl to snap to the nearest corner of the face under the cursor.
    ///  * With the clip section on (it's only drawn by shaders - the meshes aren't really cut), the cut section is worked
    ///    out from the meshes (SectionIndex): clicks near a cut edge snap onto it (green), clicks on a part's cut face
    ///    land on the cut plane (blue); Ctrl widens the edge snap.
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
                KeepSectionsCurrent();
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
                if (_exact.Count > 0 && !_exact.Contains(h.collider)) continue;   // boxes / convex hulls aren't the real surface
                Renderer r = h.collider.GetComponent<Renderer>();
                if (r != null && !r.enabled) continue;
                if (IsClipped(h.point, planes)) continue;
                best = i;
            }

            // The cut is only drawn by shaders, so nothing physical marks the cut faces and edges. The ray enters the
            // kept part of the vehicle through the cut plane at 'entry'; the section worked out from the meshes says
            // whether that is on a cut edge (snap to it), inside a part's cut face (the point is on the cut), or empty
            // space (then the surface behind it, as normal).
            Vector3 entry; float tEntry;
            if (planes.Count > 0 && SectionEntry(ray, best >= 0 ? _hits[best].distance + 1e-3f : maxRayDistance, planes, out entry, out tEntry))
            {
                SectionIndex section = ReadySection(entry, planes);
                if (section != null)
                {
                    float wpp = WorldPerPixel(entry);
                    Vector3 q; int part;
                    if (section.NearestEdge(entry, wpp * (snap ? ctrlEdgeSnapPixels : edgeSnapPixels), PartVisible, out q, out part))
                    { point = q; _hoverKind = HoverKind.CutEdge; return true; }
                    if (section.InsideSolid(entry, PartVisible, out part))
                    { point = entry; _hoverKind = HoverKind.CutFace; return true; }
                }
                else if (best >= 0 && FacesAway(_hits[best], ray.direction))
                {
                    // Outline still being worked out: looking at the inside of a part means the cut goes through it
                    point = entry; _hoverKind = HoverKind.CutFace; return true;
                }
            }
            if (best < 0) return false;
            _hoverKind = HoverKind.Surface;
            point = _hits[best].point;
            if (snap) point = SnapToCorner(_hits[best], point);
            return true;
        }

        // ── the cut section, worked out from the meshes ──
        [Tooltip("With a cut on, clicks this close to a cut edge snap onto it (pixels).")]
        public float edgeSnapPixels = 10f;
        [Tooltip("With Ctrl held, the cut-edge snap reaches this far (pixels).")]
        public float ctrlEdgeSnapPixels = 30f;

        private enum HoverKind { Surface, CutEdge, CutFace }
        private HoverKind _hoverKind = HoverKind.Surface;
        public Color cutEdgeColor = new Color(0.3f, 1f, 0.45f, 1f);
        public Color cutFaceColor = new Color(0.25f, 0.9f, 1f, 1f);

        private class PartSource { public int id; public Renderer renderer; public Mesh mesh; }
        private readonly List<PartSource> _partSources = new List<PartSource>();
        private readonly Dictionary<int, Renderer> _partRenderers = new Dictionary<int, Renderer>();
        private Transform _partsFor;
        private int _unreadable;
        private readonly Dictionary<int, SectionIndex> _sections = new Dictionary<int, SectionIndex>();   // by cut axis
        private string _sectionWanted = "", _sectionBuilt = "";
        private float _sectionWantedSince;
        private Coroutine _sectionBuild;
        private bool _sectionBusy;
        private readonly HashSet<Collider> _exact = new HashSet<Collider>();

        private bool PartVisible(int id)
        {
            Renderer r;
            return _partRenderers.TryGetValue(id, out r) && r != null && r.enabled && r.gameObject.activeInHierarchy;
        }

        /// <summary>The worked-out section for the plane a point lies on, if it's ready and up to date.</summary>
        private SectionIndex ReadySection(Vector3 onPlane, List<ClipPlane> planes)
        {
            foreach (ClipPlane pl in planes)
            {
                if (Mathf.Abs(Axis(onPlane, pl.axis) - pl.position) > 1e-3f) continue;
                SectionIndex s;
                if (_sections.TryGetValue(pl.axis, out s) && s != null && s.position == pl.position) return s;
            }
            return null;
        }

        private static string PlanesKey(List<ClipPlane> planes)
        {
            var sb = new System.Text.StringBuilder();
            foreach (ClipPlane p in planes) sb.Append(p.axis).Append(':').Append(p.position.ToString("R")).Append(';');
            return sb.ToString();
        }

        /// <summary>Re-works out the section once the cut has stopped moving for a moment (dragging the slider doesn't stall).</summary>
        private void KeepSectionsCurrent()
        {
            List<ClipPlane> planes = ActiveClipPlanes();
            if (planes.Count == 0 || Root == null) return;
            string key = PlanesKey(planes);
            if (key != _sectionWanted) { _sectionWanted = key; _sectionWantedSince = Time.unscaledTime; }
            if (key == _sectionBuilt || _sectionBusy || Time.unscaledTime - _sectionWantedSince < 0.25f) return;
            _sectionBuild = StartCoroutine(BuildSections(new List<ClipPlane>(planes), key, Root));
        }

        private IEnumerator BuildSections(List<ClipPlane> planes, string key, Transform root)
        {
            _sectionBusy = true;
            var sw = Stopwatch.StartNew();
            if (_partsFor != root) CollectParts(root);
            foreach (ClipPlane plane in planes)
            {
                // Parts the plane goes through (by their bounds), with their mesh data - read on the main thread, a few ms per frame
                var parts = new List<SectionIndex.Part>();
                foreach (PartSource src in _partSources)
                {
                    if (src.renderer == null || src.mesh == null) continue;
                    Bounds b = src.renderer.bounds;
                    if (Axis(b.min, plane.axis) > plane.position || Axis(b.max, plane.axis) < plane.position) continue;
                    KeyValuePair<Vector3[], int[]> data = MeshData(src.mesh);
                    if (data.Key == null || data.Value == null) continue;
                    Matrix4x4 m = src.renderer.localToWorldMatrix;
                    parts.Add(new SectionIndex.Part { id = src.id, vertices = data.Key, triangles = data.Value,
                        matrix = new[] { m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23 } });
                    if (sw.ElapsedMilliseconds > 8) { yield return null; sw.Reset(); sw.Start(); if (root == null) { _sectionBusy = false; yield break; } }
                }
                // The maths on a worker thread
                int axis = plane.axis; float pos = plane.position;
                Task<SectionIndex> task = Task.Run(() => SectionIndex.Build(parts, axis, pos));
                while (!task.IsCompleted) yield return null;
                if (task.IsFaulted) { Debug.LogWarning("[ManualMeasure] Couldn't work out the cut section: " + task.Exception); continue; }
                _sections[axis] = task.Result;
                Debug.Log("[ManualMeasure] Cut " + "XYZ"[axis] + " = " + pos.ToString("F4") + ": " + task.Result.PartCount + " part(s) cut (" +
                          task.Result.ClosedPartCount + " solid), " + task.Result.SegmentCount + " outline segments" +
                          (_unreadable > 0 ? "; " + _unreadable + " part(s) can't be read (enable Read/Write on their meshes)" : ""));
            }
            _sectionBuilt = key;
            _sectionBusy = false;
            _sectionBuild = null;
        }

        /// <summary>The parts that are drawn: mesh renderers with a mesh, only the most detailed level of an LOD group.</summary>
        private void CollectParts(Transform root)
        {
            _partSources.Clear(); _partRenderers.Clear(); _unreadable = 0;
            _partsFor = root;
            if (root == null) return;
            var lowerLods = new HashSet<Renderer>();
            foreach (LODGroup g in root.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = g.GetLODs();
                var top = new HashSet<Renderer>();
                if (lods.Length > 0) foreach (Renderer r in lods[0].renderers) if (r != null) top.Add(r);
                for (int i = 1; i < lods.Length; i++) foreach (Renderer r in lods[i].renderers) if (r != null && !top.Contains(r)) lowerLods.Add(r);
            }
            int id = 0;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (lowerLods.Contains(r)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (!mf.sharedMesh.isReadable) { _unreadable++; continue; }
                _partSources.Add(new PartSource { id = id, renderer = r, mesh = mf.sharedMesh });
                _partRenderers[id] = r;
                id++;
            }
        }

        /// <summary>World size of one screen pixel at a point (for "within N pixels").</summary>
        private float WorldPerPixel(Vector3 at)
        {
            float h = Mathf.Max(1f, cam.pixelRect.height);
            if (cam.orthographic) return 2f * cam.orthographicSize / h;
            float depth = Mathf.Max(0.01f, Vector3.Dot(at - cam.transform.position, cam.transform.forward));
            return 2f * depth * Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(0.01f, Mathf.Cos(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / h;
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
            if (_sectionBuild != null) { StopCoroutine(_sectionBuild); _sectionBuild = null; }
            _sectionBusy = false;
            _sections.Clear(); _sectionBuilt = ""; _sectionWanted = "";
            _partSources.Clear(); _partRenderers.Clear(); _partsFor = null;
            _exact.Clear();
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
            // Every drawn mesh needs an exact (non-convex) MeshCollider of its own mesh. Parts that come with a box or a
            // convex collider get one too; only exact colliders count when picking.
            var todo = new List<MeshFilter>();
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<MeshRenderer>() == null) continue;
                MeshCollider exact = null;
                foreach (MeshCollider mc in mf.GetComponents<MeshCollider>()) if (!mc.convex && mc.sharedMesh == mf.sharedMesh) { exact = mc; break; }
                if (exact != null) _exact.Add(exact); else todo.Add(mf);
            }
            _preparingLeft = todo.Count;
            var sw = Stopwatch.StartNew();
            int failed = 0;
            foreach (MeshFilter mf in todo)
            {
                if (mf != null)
                {
                    try
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;          // exact surface hits
                        _addedColliders.Add(mc);
                        _exact.Add(mc);
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
            Debug.Log("[ManualMeasure] Ready to measure " + (root != null ? root.name : "?") + ": " + _addedColliders.Count + " collider(s) added, " + _exact.Count + " exact" + (failed > 0 ? ", " + failed + " part(s) skipped" : ""));
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
            if (showHover)
            {
                _hoverDot.rectTransform.anchoredPosition = ToScreen(_hover);
                _hoverDot.color = _hoverKind == HoverKind.CutEdge ? cutEdgeColor : _hoverKind == HoverKind.CutFace ? cutFaceColor : pendingColor;
                _hoverDot.rectTransform.sizeDelta = Vector2.one * (_hoverKind == HoverKind.Surface ? dotSizePx : dotSizePx * 1.35f);
            }

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
                    bool cut = ActiveClipPlanes().Count > 0;
                    string ctrl = !cut ? "Ctrl: snap to corner"
                                : _sectionBusy || _sectionBuilt != _sectionWanted ? "working out the cut outline..."
                                : "<color=#4DFF73>green</color>: cut edge   ·   <color=#40E6FF>blue</color>: cut face   ·   Ctrl: wider edge snap";
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
