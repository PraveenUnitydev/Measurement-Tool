// The 2D view and manual measuring, outside Unity (run.sh). Private methods are reached by reflection.
using System; using System.Reflection; using UnityEngine; using VehicleMeasurement;
public static class CameraTests
{
    static int pass, fail;
    static void Check(string n, bool ok, string extra = "") { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + n + " " + extra); } }
    static bool Near(Vector3 a, Vector3 b, float e = 1e-3f) { return (a - b).magnitude < e; }
    static bool Near(float a, float b, float e = 1e-3f) { return Math.Abs(a - b) < e; }
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Call(object o, string m, params object[] a) { return o.GetType().GetMethod(m, F).Invoke(o, a); }
    static void Set(object o, string f, object v) { o.GetType().GetField(f, F).SetValue(o, v); }
    static void Set(Type t, string p, object v) { t.GetProperty(p, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).SetValue(null, v); }
    static T Get<T>(object o, string f) { return (T)o.GetType().GetField(f, F).GetValue(o); }

    public static int Main()
    {
        var cam = new OrbitCameraController();
        cam.gameObject.AddComponent<Camera>();

        // ── every 2D view looks from the same side as its 3D button ──
        // 3D views: camera direction from the pivot = (sin yaw cos pitch, sin pitch, cos yaw cos pitch)
        var views = new[] { new { v = OrbitCameraController.OrthoPreset.Front, yaw = 180f }, new { v = OrbitCameraController.OrthoPreset.Back, yaw = 0f },
                            new { v = OrbitCameraController.OrthoPreset.Right, yaw = 90f }, new { v = OrbitCameraController.OrthoPreset.Left, yaw = 270f } };
        foreach (var x in views)
        {
            cam.orthoView = x.v;
            var rot = (Quaternion)Call(cam, "OrthoRotation");
            Vector3 camSide = -(rot * Vector3.forward);                                       // where the 2D camera sits
            Vector3 side3D = new Vector3(Mathf.Sin(x.yaw * Mathf.Deg2Rad), 0, Mathf.Cos(x.yaw * Mathf.Deg2Rad));
            Check("2D " + x.v + " looks from the same side as the 3D " + x.v + " view", Near(camSide, side3D), camSide + " vs " + side3D);
            Check("2D " + x.v + " is level (world up is screen up)", Near(rot * Vector3.up, Vector3.up), (rot * Vector3.up).ToString());
            Call(cam, "ApplyOrthographicFacing");
            float yaw = Get<float>(cam, "yaw"), pitch = Get<float>(cam, "pitch");
            Check("2D " + x.v + " keeps the 3D yaw/pitch (for switching back)", Near(yaw, x.yaw) && Near(pitch, 0f), yaw + "/" + pitch);
        }
        cam.orthoView = OrbitCameraController.OrthoPreset.Top;
        var top = (Quaternion)Call(cam, "OrthoRotation");
        Check("2D top looks straight DOWN (it used to look up from underneath)", Near(top * Vector3.forward, Vector3.down), (top * Vector3.forward).ToString());
        Check("2D top: the 3D front view's side (-Z) is at the top of the screen", Near(top * Vector3.up, Vector3.back), (top * Vector3.up).ToString());

        // ── side view: pan follows the cursor, zoom keeps the point under the cursor ──
        cam.orthoView = OrbitCameraController.OrthoPreset.Left;
        Set(cam, "useOrthographic2D", true);
        Set(cam, "orthoSizeCurrent", 2f); Set(cam, "orthoSizeTarget", 2f); Set(cam, "pivotTarget", Vector3.zero);
        Call(cam, "OrthoPanBy", new Vector2(108f, 0f));                                         // 108 px of 1080 = 0.1 of the height = 0.4 m at size 2
        var rotL = (Quaternion)Call(cam, "OrthoRotation");
        Vector3 moved = Get<Vector3>(cam, "pivotTarget");
        Check("side view pan: 1:1 with the cursor (0.4 m for a tenth of the screen at size 2)", Near(moved.magnitude, 0.4f) && Near(moved, -(rotL * Vector3.right) * 0.4f), moved.ToString());
        cam.orthoView = OrbitCameraController.OrthoPreset.Top; Set(cam, "pivotTarget", Vector3.zero);
        Call(cam, "OrthoPanBy", new Vector2(0f, 108f));
        Check("top view: up/down drag pans along the vehicle (it used to do nothing)", Near(Math.Abs(Get<Vector3>(cam, "pivotTarget").z), 0.4f), Get<Vector3>(cam, "pivotTarget").ToString());

        cam.orthoView = OrbitCameraController.OrthoPreset.Left; Set(cam, "pivotTarget", new Vector3(0.1f, 1f, 2f));
        var screen = new Vector2(1500f, 300f);
        Func<Vector3> underCursor = () =>
        {
            float size = Get<float>(cam, "orthoSizeTarget"); var r = (Quaternion)Call(cam, "OrthoRotation");
            float dx = (screen.x - Screen.width * 0.5f) / (Screen.height * 0.5f), dy = (screen.y - Screen.height * 0.5f) / (Screen.height * 0.5f);
            return Get<Vector3>(cam, "pivotTarget") + r * Vector3.right * dx * size + r * Vector3.up * dy * size;
        };
        Vector3 before = underCursor();
        Call(cam, "OrthoZoomBy", 1f, screen);
        Check("2D zoom in: smaller view", Get<float>(cam, "orthoSizeTarget") < 2f);
        Check("2D zoom: the point under the cursor stays put", Near(underCursor(), before), underCursor() + " vs " + before);

        // ── camera stays outside the vehicle ──
        Set(cam, "orthoBounds", new Bounds(Vector3.zero, new Vector3(2f, 1.8f, 60f))); Set(cam, "hasOrthoBounds", true); Set(cam, "pivotCurrent", Vector3.zero);
        float depth = (float)Call(cam, "OrthoCameraDepth");
        Check("2D camera is outside even a 60 m long model", depth > 30f, depth.ToString());
        Set(cam, "orthoBounds", new Bounds(Vector3.zero, new Vector3(2f, 1.8f, 4.5f)));
        Check("2D camera depth for a car: the configured 50", Near((float)Call(cam, "OrthoCameraDepth"), 50f));
        Vector3 clamped = (Vector3)Call(cam, "ClampOrthoPivot", new Vector3(500f, 0f, 0f), 2f);
        Check("2D pan can't lose the vehicle", clamped.x < 10f, clamped.ToString());

        // ── 2D side view buttons ──
        cam.sideViewPreset = OrbitCameraController.OrthoPreset.Right;
        Set(cam, "useOrthographic2D", false);
        cam.Set2DSideView();
        Check("'2D side view' switches 2D on with the chosen side", cam.Is2D && cam.Current2DView == OrbitCameraController.OrthoPreset.Right);
        cam.Toggle2DSideView();
        Check("pressing it again goes back to 3D", !cam.Is2D);
        cam.Set2DTopView(); cam.Toggle2DSideView();
        Check("from 2D top, the side view button shows the side (not 3D)", cam.Is2D && cam.Current2DView == OrbitCameraController.OrthoPreset.Right);
        cam.doubleClickFocus = true;

        // ── measurements ──
        var tool = new ManualMeasureTool();
        var m3 = new ManualMeasureTool.Measurement { a = new Vector3(0, 0, 0), b = new Vector3(0.3f, 0.4f, 1.2f) };
        Check("3D distance: straight line between the points", Near(m3.Distance, 1.3f), m3.Distance.ToString());
        Check("mm text", tool.Mm(1.3f) == "1300.0 mm", tool.Mm(1.3f));
        string l3 = (string)Call(tool, "LabelText", m3);
        Check("3D label: distance and length/width/height parts", l3.Contains("1300.0 mm") && l3.Contains("L 1200.0") && l3.Contains("W 300.0") && l3.Contains("H 400.0") && !l3.Contains("in view"), l3);
        var side = new ManualMeasureTool.Measurement { a = m3.a, b = m3.b, viewNormal = Vector3.right };
        string l2 = (string)Call(tool, "LabelText", side);
        Check("2D side view: depth (width) doesn't count", Near(side.Distance, (float)Math.Sqrt(0.4 * 0.4 + 1.2 * 1.2)), side.Distance.ToString());
        Check("2D side label: length and height only, marked 'in view'", l2.Contains("L 1200.0") && l2.Contains("H 400.0") && !l2.Contains("W ") && l2.Contains("in view"), l2);
        var front = new ManualMeasureTool.Measurement { a = m3.a, b = m3.b, viewNormal = Vector3.forward };
        Check("2D front view: width and height", Near(front.Distance, 0.5f), front.Distance.ToString());
        var topM = new ManualMeasureTool.Measurement { a = m3.a, b = m3.b, viewNormal = Vector3.up };
        Check("2D top view: length and width", Near(topM.Distance, (float)Math.Sqrt(0.09 + 1.44)), topM.Distance.ToString());

        tool.decimals = 2;
        Check("decimals setting", tool.Mm(0.12345f) == "123.45 mm", tool.Mm(0.12345f));


        // ── cut section: the point is on the cut face, not inside the part ──
        var planes = new System.Collections.Generic.List<ManualMeasureTool.ClipPlane> { new ManualMeasureTool.ClipPlane(0, 0f, -1f) };   // x < 0 cut away
        Vector3 entry; float tEntry;
        Check("clip: cut-away side is clipped, kept side isn't", ManualMeasureTool.IsClipped(new Vector3(-1, 0, 0), planes) && !ManualMeasureTool.IsClipped(new Vector3(1, 0, 0), planes));
        var diag = new Ray(new Vector3(-10f, 5f, 3f), new Vector3(1f, -0.2f, 0.5f).normalized);
        Check("ray from the cut-away side: enters the kept part exactly on the cut plane",
            ManualMeasureTool.SectionEntry(diag, 50f, planes, out entry, out tEntry) && entry.x == 0f && Near(entry, diag.GetPoint(tEntry)), entry.ToString());
        Check("the surface it hit is nearer than the plane: no cut-face point", !ManualMeasureTool.SectionEntry(diag, tEntry - 0.01f, planes, out entry, out tEntry));
        Check("camera on the kept side: no cut-face point", !ManualMeasureTool.SectionEntry(new Ray(new Vector3(5f, 0, 0), new Vector3(-1f, 0, 0)), 50f, planes, out entry, out tEntry));
        Check("ray along the cut plane: no cut-face point", !ManualMeasureTool.SectionEntry(new Ray(new Vector3(-1f, 0, 0), new Vector3(0, 0, 1f)), 50f, planes, out entry, out tEntry));
        planes.Add(new ManualMeasureTool.ClipPlane(1, 1f, 1f));                                          // and y > 1 cut away
        var two = new Ray(new Vector3(-5f, 3f, 0f), new Vector3(1f, -1f, 0f).normalized);
        Check("two cuts: enters through the last plane crossed", ManualMeasureTool.SectionEntry(two, 50f, planes, out entry, out tEntry) && entry.x == 0f && Near(entry.y, -2f), entry.ToString());
        Check("two cuts: a point that's still cut by the other plane is refused",
            !ManualMeasureTool.SectionEntry(new Ray(new Vector3(-5f, 9f, 0f), new Vector3(1f, 0f, 0f)), 50f, planes, out entry, out tEntry));

        // The scene from the screenshot: two clicks through the cut both land on the cut plane, so the distance is
        // the one across the section (no sideways "W" part from points deep inside the parts)
        planes.RemoveAt(1);
        var eye = new Vector3(-6f, 2.5f, -3f);
        Vector3 seat = new Vector3(0f, 0.9f, -0.4f), dash = new Vector3(0f, 1.0f, 1.1f), p1, p2;
        ManualMeasureTool.SectionEntry(new Ray(eye, (seat - eye).normalized), 50f, planes, out p1, out tEntry);
        ManualMeasureTool.SectionEntry(new Ray(eye, (dash - eye).normalized), 50f, planes, out p2, out tEntry);
        var across = new ManualMeasureTool.Measurement { a = p1, b = p2 };
        Check("measurement across the cut: both points on the cut face, no width part", Near(p1, seat) && Near(p2, dash) && Near(Math.Abs(across.Delta.x), 0f), p1 + " " + p2);

        // ── the measure tool doesn't raycast with an unusable camera ──
        var tool2 = new ManualMeasureTool(); var c2 = tool2.gameObject.AddComponent<Camera>(); tool2.cam = c2;
        Func<Vector2, bool> usable = sp => (bool)Call(tool2, "CameraUsable", sp);
        Check("camera fine, cursor in the view: picks", usable(new Vector2(500, 500)));
        c2.orthographic = true; c2.orthographicSize = 0f;
        Check("2D camera of size 0: no raycast (Unity's 'out of view frustum')", !usable(new Vector2(500, 500)));
        c2.orthographicSize = 3f;
        Check("cursor outside the view: no raycast", !usable(new Vector2(-5, 500)) && !usable(new Vector2(500, 5000)));

        // ── the 2D camera is always usable ──
        var cam2 = new OrbitCameraController(); var cc = cam2.gameObject.AddComponent<Camera>();
        cam2.orthoMaxSize = 0f;                                                                            // set oddly in the Inspector
        cam2.Set2DSideView();                                                                              // no vehicle (no bounds) yet
        Check("2D with nothing to fit: a real size, not 0", cc.orthographicSize > 0.0005f, cc.orthographicSize.ToString());
        Set(cam2, "orthoSizeTarget", float.NaN); Set(cam2, "pivotTarget", new Vector3(float.NaN, 0, 0));
        Call(cam2, "SmoothAndCommit");
        Check("2D recovers from a NaN size / position", cc.orthographicSize > 0.0005f && !float.IsNaN(cam2.transform.position.x), cc.orthographicSize + " " + cam2.transform.position);
        var cam3 = new OrbitCameraController(); var c3 = cam3.gameObject.AddComponent<Camera>();
        cam3.FocusOnBounds(new Bounds(new Vector3(0, 0.9f, 0), new Vector3(1.9f, 1.8f, 4.6f)));         // the comparison scene: bounds, no target
        cam3.Set2DSideView();
        Check("2D without a target uses the FocusOnBounds box (comparison scene): 4.6 m side-on fits exactly", Near(c3.orthographicSize, 4.6f * 1.2f * 0.5f / (16f / 9f)), c3.orthographicSize.ToString());

        // ── the cut section worked out from the meshes (SectionIndex) ──
        Func<float, float[]> scaleOnly = k => new[] { k, 0, 0, 0, 0, k, 0, 0, 0, 0, k, 0 };
        Func<float[], Vector3, float[]> shifted = (m, t) => new[] { m[0], m[1], m[2], m[3] + t.x, m[4], m[5], m[6], m[7] + t.y, m[8], m[9], m[10], m[11] + t.z };
        var cubeV = new[] { new Vector3(-1,-1,-1), new Vector3(1,-1,-1), new Vector3(1,1,-1), new Vector3(-1,1,-1), new Vector3(-1,-1,1), new Vector3(1,-1,1), new Vector3(1,1,1), new Vector3(-1,1,1) };
        var cubeT = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,7,6, 3,6,2, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
        Func<int, float[], SectionIndex.Part> cube = (id, m) => new SectionIndex.Part { id = id, vertices = cubeV, triangles = cubeT, matrix = m };
        Vector3 qq; int part;

        var sec = SectionIndex.Build(new[] { cube(0, scaleOnly(1f)) }, 0, 0.25f);                    // 2 m cube, cut at x = 0.25
        Check("cube cut: 8 outline segments, closed (a solid)", sec.SegmentCount == 8 && sec.ClosedPartCount == 1, sec.SegmentCount + " / " + sec.ClosedPartCount);
        Check("click inside the cut face: on the cut face", sec.InsideSolid(new Vector3(0.25f, 0.2f, 0.3f), null, out part) && part == 0);
        Check("click outside the part on the plane: not a cut face", !sec.InsideSolid(new Vector3(0.25f, 1.5f, 0f), null, out part));
        Check("near the cut edge: snaps exactly onto it", sec.NearestEdge(new Vector3(0.25f, 0.97f, 0.3f), 0.05f, null, out qq, out part) && Near(qq, new Vector3(0.25f, 1f, 0.3f)), qq.ToString());
        Check("too far from any edge: no snap", !sec.NearestEdge(new Vector3(0.25f, 0.5f, 0.3f), 0.05f, null, out qq, out part));
        Check("near a corner: the corner", sec.NearestEdge(new Vector3(0.25f, 1.02f, 1.03f), 0.1f, null, out qq, out part) && Near(qq, new Vector3(0.25f, 1f, 1f)), qq.ToString());

        var far = SectionIndex.Build(new[] { cube(3, shifted(scaleOnly(0.5f), new Vector3(10f, 2f, -4f))) }, 0, 10.2f);    // half-size cube at (10, 2, -4)
        Check("part position and scale are applied", far.InsideSolid(new Vector3(10.2f, 2.3f, -4.4f), null, out part) && !far.InsideSolid(new Vector3(10.2f, 2.6f, -4f), null, out part) &&
              far.NearestEdge(new Vector3(10.2f, 2.49f, -4f), 0.05f, null, out qq, out part) && Near(qq, new Vector3(10.2f, 2.5f, -4f)), qq.ToString());

        // Hollow part (sheet-metal box: outer and inner skin): the wall is the cut face, the hollow isn't
        var hollowV = new Vector3[16]; var hollowT = new int[72];
        for (int i = 0; i < 8; i++) { hollowV[i] = cubeV[i]; hollowV[i + 8] = cubeV[i] * 0.9f; }
        for (int i = 0; i < 36; i++) { hollowT[i] = cubeT[i]; hollowT[36 + i] = cubeT[i - i % 3 + (2 - i % 3)] + 8; }   // inner skin faces inwards
        var hollow = SectionIndex.Build(new[] { new SectionIndex.Part { id = 1, vertices = hollowV, triangles = hollowT, matrix = scaleOnly(1f) } }, 0, 0f);
        Check("hollow part: a click in the wall is on the cut face", hollow.InsideSolid(new Vector3(0f, 0.95f, 0f), null, out part));
        Check("hollow part: a click in the hollow inside is not", !hollow.InsideSolid(new Vector3(0f, 0.5f, 0.2f), null, out part));

        // An open single surface (no thickness): an edge to snap to, but never a cut face
        var sheet = SectionIndex.Build(new[] { new SectionIndex.Part { id = 2, vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) }, triangles = new[] { 0, 1, 2, 0, 2, 3 }, matrix = scaleOnly(1f) } }, 0, 0f);
        Check("open surface: not a solid, so no cut face", sheet.ClosedPartCount == 0 && !sheet.InsideSolid(new Vector3(0, 0.2f, 0.001f), null, out part));
        Check("open surface: its cut line can be snapped to", sheet.NearestEdge(new Vector3(0, 0.3f, 0.004f), 0.01f, null, out qq, out part) && Near(qq, new Vector3(0, 0.3f, 0f)), qq.ToString());

        // Nested parts (a bolt in a bracket): the smaller one wins; hidden parts don't count
        var nested = SectionIndex.Build(new[] { cube(10, scaleOnly(1f)), cube(11, scaleOnly(0.2f)) }, 0, 0f);
        Check("nested parts: inside the small one -> the small one", nested.InsideSolid(new Vector3(0, 0.05f, 0.05f), null, out part) && part == 11, part.ToString());
        Check("nested parts: outside the small one -> the big one", nested.InsideSolid(new Vector3(0, 0.6f, 0.05f), null, out part) && part == 10, part.ToString());
        Check("hidden part: ignored", nested.InsideSolid(new Vector3(0, 0.05f, 0.05f), id => id != 11, out part) && part == 10 &&
              !nested.NearestEdge(new Vector3(0, 0.21f, 0f), 0.02f, id => id != 11, out qq, out part));
        Check("cut that misses every part: empty", SectionIndex.Build(new[] { cube(0, scaleOnly(1f)) }, 0, 3f).SegmentCount == 0);
        var onFace = SectionIndex.Build(new[] { cube(0, scaleOnly(1f)) }, 1, 1f);                         // exactly along the top face
        Check("cut exactly along a face: no crash, edges still snap", onFace.NearestEdge(new Vector3(0.98f, 1f, 0f), 0.05f, null, out qq, out part) && Near(qq, new Vector3(1f, 1f, 0f)), qq.ToString());

        // Speed: a 200k-triangle part (a detailed CAD body panel) on the worker thread
        int nGrid = 317; var bigV = new Vector3[nGrid * nGrid]; var bigT = new System.Collections.Generic.List<int>();
        for (int y = 0; y < nGrid; y++) for (int x = 0; x < nGrid; x++) bigV[y * nGrid + x] = new Vector3(x / (float)nGrid * 4f - 2f, (float)Math.Sin(x * 0.05) * 0.2f, y / (float)nGrid * 4f - 2f);
        for (int y = 0; y + 1 < nGrid; y++) for (int x = 0; x + 1 < nGrid; x++) { int a = y * nGrid + x; bigT.AddRange(new[] { a, a + nGrid, a + 1, a + 1, a + nGrid, a + nGrid + 1 }); }
        var swatch = System.Diagnostics.Stopwatch.StartNew();
        var big = SectionIndex.Build(new[] { new SectionIndex.Part { id = 5, vertices = bigV, triangles = bigT.ToArray(), matrix = scaleOnly(1f) } }, 0, 0.0123f);
        long ms = swatch.ElapsedMilliseconds;
        swatch.Restart(); for (int i = 0; i < 1000; i++) big.NearestEdge(new Vector3(0.0123f, 0.1f, (i % 100) * 0.03f - 1.5f), 0.02f, null, out qq, out part); long q1000 = swatch.ElapsedMilliseconds;
        Check("200k-triangle part: worked out in well under a second, 1000 snaps in a few ms", big.SegmentCount > 300 && ms < 1500 && q1000 < 100, ms + " ms build, " + q1000 + " ms for 1000 snaps, " + big.SegmentCount + " segments");


        // ── search in the vehicle lists ──
        Check("search: every word, any order, any case", DropdownSearch.Matches("Mahindra XUV700 AX7", DropdownSearch.Words("ax7 xuv")) && !DropdownSearch.Matches("Mahindra XUV700", DropdownSearch.Words("thar")));
        Check("search: empty query matches all", DropdownSearch.Words("   ").Length == 0 && DropdownSearch.Matches("Creta", DropdownSearch.Words("")));
        Check("search: matches the download note too ('not downloaded')", DropdownSearch.Matches("Creta  (not downloaded - 402 MB)", DropdownSearch.Words("not down")));
        Check("search: rich-text tags in labels don't get in the way", DropdownSearch.Plain("<b>Bolero</b> <color=#999>Classic</color>") == "Bolero Classic");

        // ── comparison scene: 2D of the superimposed vehicles ──
        var cmp = new OrbitCameraController(); var cmpCam = cmp.gameObject.AddComponent<Camera>();
        cmp.fit2DToFocusBounds = true;
        cmp.FocusOnBounds(new Bounds(new Vector3(0.3f, 0.9f, 0.2f), new Vector3(2.0f, 1.9f, 5.0f)));   // both vehicles
        cmp.Set2DSideView();
        Check("superimpose 2D: centred on both vehicles and fits them", Near(Get<Vector3>(cmp, "pivotTarget"), new Vector3(0.3f, 0.9f, 0.2f)) && Near(cmpCam.orthographicSize, 5.0f * 1.2f * 0.5f / (16f / 9f)), cmpCam.orthographicSize.ToString());
        Call(cmp, "OrthoPanBy", new Vector2(108f, 0f));
        Vector3 panned = Get<Vector3>(cmp, "pivotTarget");
        Call(cmp, "Update");
        Check("superimpose 2D with no target: a pan isn't undone the next frame", Near(Get<Vector3>(cmp, "pivotTarget"), panned) && !Near(panned, new Vector3(0.3f, 0.9f, 0.2f)), Get<Vector3>(cmp, "pivotTarget").ToString());

        // ── buttons and loading panel ──
        Set(typeof(ManualMeasureTool), "Instance", null);
        var tA = new ManualMeasureTool(); Call(tA, "Awake");
        var tB = new ManualMeasureTool(); Call(tB, "Awake");
        Check("a second ManualMeasureTool switches itself off (no double overlays / hint bars / M key)", ManualMeasureTool.Instance == tA && !tB.enabled && tA.enabled);
        Time.frameCount = 100;
        tA.Toggle(); tA.Toggle();                                                                            // button wired twice: one click = two calls
        Check("measure button wired twice: one click still switches measuring on (not on-then-off)", tA.IsActive);
        Time.frameCount = 101; tA.Toggle();
        Check("next click switches it off", !tA.IsActive);
        tA.clearButton = new UnityEngine.UI.Button(); tA.undoButton = new UnityEngine.UI.Button(); tA.toggleButtonText = new TMPro.TextMeshProUGUI();
        Call(tA, "RefreshButtons");
        Check("Clear / Undo not clickable with nothing to remove; label says Measure", !tA.clearButton.interactable && !tA.undoButton.interactable && tA.toggleButtonText.text == "Measure");
        Get<System.Collections.Generic.List<ManualMeasureTool.Measurement>>(tA, "_done").Add(new ManualMeasureTool.Measurement { a = Vector3.zero, b = Vector3.one });
        Time.frameCount = 102; tA.Toggle(); Call(tA, "RefreshButtons");
        Check("with a measurement: Clear / Undo clickable; label says Stop Measuring", tA.clearButton.interactable && tA.undoButton.interactable && tA.toggleButtonText.text == "Stop Measuring");
        Time.frameCount = 103; tA.UndoLast(); tA.UndoLast();
        Check("undo wired twice removes one measurement per click", tA.Measurements.Count == 0);
        tA.preparingPanel = new GameObject(); tA.preparingPanel.SetActive(false); tA.preparingText = new TMPro.TextMeshProUGUI();
        Set(tA, "_preparingTotal", 200); Set(tA, "_preparingLeft", 50);
        Call(tA, "UpdatePanel");
        Check("loading panel shown while the vehicle gets ready, with progress", tA.preparingPanel.activeSelf && tA.preparingText.text.EndsWith("75%"), tA.preparingText.text);
        Set(tA, "_preparingLeft", 0); Call(tA, "UpdatePanel");
        Check("loading panel hidden when ready", !tA.preparingPanel.activeSelf);
        var shared = new GameObject(); shared.SetActive(true); tA.preparingPanel = shared; Set(tA, "_panelShown", false);
        Call(tA, "UpdatePanel");
        Check("a panel shared with other loading isn't hidden by the tool when it never showed it", shared.activeSelf);
        Set(typeof(ManualMeasureTool), "Instance", null);
        Console.WriteLine("2D view & manual measure: Passed " + pass + " | Failed " + fail);
        return fail == 0 ? 0 : 1;
    }
}
