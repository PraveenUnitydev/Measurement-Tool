using System;
using System.Collections.Generic;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// The cross-section of the vehicle on one cut plane ("axis = position"), worked out from the mesh triangles - the
    /// clip section itself is only drawn by shaders, so nothing in the scene (colliders, depth) knows where the cut
    /// faces and edges are. Used by <see cref="ManualMeasureTool"/> to put points exactly on the cut.
    ///
    ///  * Outline: for each part, where its triangles cross the plane (segments).
    ///  * Edge snap: nearest outline point within a radius (a grid over the plane keeps it fast).
    ///  * Cut face: whether a point on the plane is inside a part's cross-section - counted crossings of the part's
    ///    outline (point-in-polygon), so it doesn't depend on face orientation. Only for parts whose outline is closed
    ///    (a solid, not a single open surface).
    ///
    /// Pure maths on arrays (no Unity objects), so <see cref="Build"/> can run on a worker thread.
    /// </summary>
    public class SectionIndex
    {
        /// <summary>A part to cut: its mesh in its own space and where it is (localToWorld, row-major 3x4).</summary>
        public class Part
        {
            public int id;
            public Vector3[] vertices;
            public int[] triangles;
            public float[] matrix;      // 12 floats: rows of localToWorld (rotation/scale | translation)
        }

        public readonly int axis;
        public readonly float position;

        // Segments of every part: (a, b) as plane coordinates (u, v), and the part they belong to
        private readonly List<Vector2> _a = new List<Vector2>(), _b = new List<Vector2>();
        private readonly List<int> _segPart = new List<int>();
        // Per part: its segment range, its outline box, closed or not
        private readonly Dictionary<int, PartInfo> _parts = new Dictionary<int, PartInfo>();
        // Grid over the plane: cell -> segment indices
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        private float _cell = 0.01f;
        private Vector2 _min;

        private class PartInfo { public int first, count; public Vector2 min, max; public bool closed; }

        public int SegmentCount { get { return _a.Count; } }
        public int PartCount { get { return _parts.Count; } }
        public int ClosedPartCount { get { int n = 0; foreach (var p in _parts.Values) if (p.closed) n++; return n; } }

        private SectionIndex(int axis, float position) { this.axis = axis; this.position = position; }

        // ── plane coordinates: the two axes other than the cut axis ──
        public Vector2 ToPlane(Vector3 p) { return axis == 0 ? new Vector2(p.y, p.z) : axis == 1 ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y); }
        public Vector3 FromPlane(Vector2 uv) { return axis == 0 ? new Vector3(position, uv.x, uv.y) : axis == 1 ? new Vector3(uv.x, position, uv.y) : new Vector3(uv.x, uv.y, position); }

        /// <summary>Works out the section of all the parts on "axis = position" (safe on a worker thread).</summary>
        public static SectionIndex Build(IList<Part> parts, int axis, float position)
        {
            var s = new SectionIndex(axis, position);
            foreach (Part p in parts) s.AddPart(p);
            s.BuildGrid();
            return s;
        }

        private void AddPart(Part part)
        {
            if (part.vertices == null || part.triangles == null || part.matrix == null) return;
            float[] m = part.matrix;
            int first = _a.Count;
            Vector3[] lv = part.vertices;
            int[] t = part.triangles;
            // Signed distance of each vertex to the plane, in world space (computed once per vertex)
            var world = new Vector3[lv.Length];
            var dist = new float[lv.Length];
            for (int i = 0; i < lv.Length; i++)
            {
                Vector3 v = lv[i];
                Vector3 w = new Vector3(m[0] * v.x + m[1] * v.y + m[2] * v.z + m[3], m[4] * v.x + m[5] * v.y + m[6] * v.z + m[7], m[8] * v.x + m[9] * v.y + m[10] * v.z + m[11]);
                world[i] = w;
                dist[i] = (axis == 0 ? w.x : axis == 1 ? w.y : w.z) - position;
            }
            var pts = new Vector3[2];
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                int ia = t[i], ib = t[i + 1], ic = t[i + 2];
                if ((uint)ia >= (uint)lv.Length || (uint)ib >= (uint)lv.Length || (uint)ic >= (uint)lv.Length) continue;
                float da = dist[ia], db = dist[ib], dc = dist[ic];
                if ((da > 0f && db > 0f && dc > 0f) || (da < 0f && db < 0f && dc < 0f)) continue;
                if (da == 0f && db == 0f && dc == 0f) continue;                 // triangle lying in the plane: its neighbours give the outline
                int k = 0;
                Cross(world[ia], world[ib], da, db, pts, ref k);
                Cross(world[ib], world[ic], db, dc, pts, ref k);
                Cross(world[ic], world[ia], dc, da, pts, ref k);
                if (k != 2) continue;
                Vector2 a = ToPlane(pts[0]), b = ToPlane(pts[1]);
                if ((a - b).sqrMagnitude < 1e-14f) continue;
                _a.Add(a); _b.Add(b); _segPart.Add(part.id);
            }
            int count = _a.Count - first;
            if (count == 0) return;
            var info = new PartInfo { first = first, count = count, min = _a[first], max = _a[first] };
            for (int i = first; i < _a.Count; i++) { Grow(ref info.min, ref info.max, _a[i]); Grow(ref info.min, ref info.max, _b[i]); }
            info.closed = IsClosed(first, count);
            if (_parts.ContainsKey(part.id)) return;   // same id twice: keep the first
            _parts[part.id] = info;
        }

        private static void Cross(Vector3 p, Vector3 q, float dp, float dq, Vector3[] pts, ref int k)
        {
            if (k >= 2) return;
            if (dp == 0f && dq == 0f) return;                                  // edge in the plane: ends come from the other edges
            if ((dp > 0f && dq > 0f) || (dp < 0f && dq < 0f)) return;          // no crossing
            float s = dp / (dp - dq);
            Vector3 x = p + (q - p) * s;
            if (k == 1 && (x - pts[0]).sqrMagnitude < 1e-14f) return;          // vertex on the plane, met from both edges
            pts[k++] = x;
        }

        private static void Grow(ref Vector2 min, ref Vector2 max, Vector2 p)
        {
            if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y;
            if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y;
        }

        /// <summary>The outline is closed when (almost) every segment end meets another segment's end - a solid part.</summary>
        private bool IsClosed(int first, int count)
        {
            var ends = new Dictionary<long, int>();
            for (int i = first; i < first + count; i++) { Bump(ends, Key(_a[i])); Bump(ends, Key(_b[i])); }
            int open = 0;
            foreach (int c in ends.Values) if ((c & 1) == 1) open++;
            // A loop has no loose ends. Big outlines may have a few from CAD seams; a small open line (a single
            // surface) always has two, so the allowance only applies to large outlines.
            return open == 0 || (count >= 100 && open <= count / 50);
        }

        private static void Bump(Dictionary<long, int> d, long k) { int c; d.TryGetValue(k, out c); d[k] = c + 1; }

        private static long Key(Vector2 p)
        {
            // 0.01 mm grid: the same point from two triangles lands in the same key
            long x = (long)Math.Round(p.x * 100000.0), y = (long)Math.Round(p.y * 100000.0);
            return (x * 73856093L) ^ (y * 19349663L);
        }

        private void BuildGrid()
        {
            if (_a.Count == 0) return;
            Vector2 min = _a[0], max = _a[0];
            for (int i = 0; i < _a.Count; i++) { Grow(ref min, ref max, _a[i]); Grow(ref min, ref max, _b[i]); }
            _min = min;
            float extent = Math.Max(max.x - min.x, max.y - min.y);
            _cell = Math.Max(0.002f, extent / 256f);                             // at most ~256 x 256 cells
            for (int i = 0; i < _a.Count; i++)
            {
                int x0 = CellX(Math.Min(_a[i].x, _b[i].x)), x1 = CellX(Math.Max(_a[i].x, _b[i].x));
                int y0 = CellY(Math.Min(_a[i].y, _b[i].y)), y1 = CellY(Math.Max(_a[i].y, _b[i].y));
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        List<int> list;
                        long k = ((long)x << 32) ^ (uint)y;
                        if (!_grid.TryGetValue(k, out list)) _grid[k] = list = new List<int>(4);
                        list.Add(i);
                    }
            }
        }

        private int CellX(float u) { return (int)Math.Floor((u - _min.x) / _cell); }
        private int CellY(float v) { return (int)Math.Floor((v - _min.y) / _cell); }

        /// <summary>
        /// The nearest outline point to <paramref name="p"/> (a point on the plane) within <paramref name="radius"/>.
        /// <paramref name="partVisible"/> leaves out hidden parts.
        /// </summary>
        public bool NearestEdge(Vector3 p, float radius, Func<int, bool> partVisible, out Vector3 nearest, out int part)
        {
            nearest = Vector3.zero; part = -1;
            if (_a.Count == 0 || radius <= 0f) return false;
            Vector2 q = ToPlane(p);
            int x0 = CellX(q.x - radius), x1 = CellX(q.x + radius), y0 = CellY(q.y - radius), y1 = CellY(q.y + radius);
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 40000) return false;     // radius far larger than the vehicle's section
            float best = radius * radius; Vector2 bestP = Vector2.zero;
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    List<int> list;
                    if (!_grid.TryGetValue(((long)x << 32) ^ (uint)y, out list)) continue;
                    foreach (int i in list)
                    {
                        Vector2 c = Closest(_a[i], _b[i], q);
                        float d = (c - q).sqrMagnitude;
                        if (d > best) continue;
                        if (partVisible != null && !partVisible(_segPart[i])) continue;
                        best = d; bestP = c; part = _segPart[i];
                    }
                }
            if (part < 0) return false;
            nearest = FromPlane(bestP);
            return true;
        }

        private static Vector2 Closest(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float s = len2 > 1e-14f ? Mathf.Clamp01(((p.x - a.x) * ab.x + (p.y - a.y) * ab.y) / len2) : 0f;
            return a + ab * s;
        }

        /// <summary>
        /// Whether a point on the plane lies inside a solid part's cross-section (the cut face), and which part. When
        /// parts are nested (e.g. a bolt in a bracket), the one with the smallest outline box wins.
        /// </summary>
        public bool InsideSolid(Vector3 p, Func<int, bool> partVisible, out int part)
        {
            part = -1;
            Vector2 q = ToPlane(p);
            float bestArea = float.MaxValue;
            foreach (KeyValuePair<int, PartInfo> kv in _parts)
            {
                PartInfo info = kv.Value;
                if (!info.closed || q.x < info.min.x || q.x > info.max.x || q.y < info.min.y || q.y > info.max.y) continue;
                float area = (info.max.x - info.min.x) * (info.max.y - info.min.y);
                if (area >= bestArea) continue;
                if (partVisible != null && !partVisible(kv.Key)) continue;
                // Even-odd rule: count outline crossings of a ray from q towards +u
                bool inside = false;
                for (int i = info.first; i < info.first + info.count; i++)
                {
                    Vector2 a = _a[i], b = _b[i];
                    if ((a.y > q.y) == (b.y > q.y)) continue;
                    float u = a.x + (q.y - a.y) * (b.x - a.x) / (b.y - a.y);
                    if (u > q.x) inside = !inside;
                }
                if (inside) { bestArea = area; part = kv.Key; }
            }
            return part >= 0;
        }

        /// <summary>The outline of one part as pairs of world points (to draw it).</summary>
        public List<Vector3> Outline(int part)
        {
            var list = new List<Vector3>();
            PartInfo info;
            if (!_parts.TryGetValue(part, out info)) return list;
            for (int i = info.first; i < info.first + info.count; i++) { list.Add(FromPlane(_a[i])); list.Add(FromPlane(_b[i])); }
            return list;
        }
    }
}
