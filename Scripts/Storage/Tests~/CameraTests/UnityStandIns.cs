// Stand-ins for the Unity APIs used by OrbitCameraController.cs and ManualMeasureTool.cs, so they compile outside
// Unity. Vector/quaternion maths is real (left-handed, as Unity); everything else only has to compile.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { public string name; public static void Destroy(Object o) {} public static void DestroyImmediate(Object o) {} public static implicit operator bool(Object o) { return o != null; } }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T[] GetComponentsInChildren<T>(bool all) { return new T[0]; } public T GetComponentInChildren<T>(bool all) where T : class { return null; } public T[] GetComponents<T>() { return new T[0]; } public T[] GetComponentsInChildren<T>() { return new T[0]; }
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class Coroutine {}
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) { return new Coroutine(); } public void StopCoroutine(Coroutine c) {} }
    public class GameObject : Object
    {
        private readonly List<object> _c = new List<object>();
        private Transform _t;
        public GameObject() {} public GameObject(string n, params Type[] t) { name = n; }
        public Transform transform { get { if (_t == null) { _t = new RectTransform(); _t.go = this; } return _t; } }
        public T AddComponent<T>() where T : new() { var c = new T(); _c.Add(c); var comp = c as Component; if (comp != null) comp.gameObject = this; return c; }
        public T GetComponent<T>() where T : class { foreach (var c in _c) if (c is T) return (T)c; return null; }
        public void SetActive(bool a) { activeSelf = a; } public bool activeSelf = true; public bool activeInHierarchy { get { return activeSelf; } }
    }
    public class Transform : Component
    {
        internal GameObject go;
        public Vector3 position, lossyScale = Vector3.one; public Quaternion rotation = Quaternion.identity; public Quaternion localRotation = Quaternion.identity; public Vector3 localScale = Vector3.one;
        public Vector3 forward { get { return rotation * Vector3.forward; } } public Vector3 right { get { return rotation * Vector3.right; } } public Vector3 up { get { return rotation * Vector3.up; } }
        public void SetParent(Transform p, bool w) {} public bool IsChildOf(Transform t) { return true; } public Vector3 TransformPoint(Vector3 p) { return p; }
        public int childCount { get { return 0; } } public Transform GetChild(int i) { return null; } public Transform parent; public Transform Find(string n) { return null; } public IEnumerator GetEnumerator() { yield break; }
    }
    public class RectTransform : Transform { public Rect rect; public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax; }

    public struct Vector2
    {
        public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } } public static Vector2 one { get { return new Vector2(1, 1); } } public static Vector2 up { get { return new Vector2(0, 1); } }
        public float sqrMagnitude { get { return x * x + y * y; } } public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector2 normalized { get { float m = magnitude; return m > 1e-6f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); } public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator -(Vector2 a) { return new Vector2(-a.x, -a.y); } public static Vector2 operator *(Vector2 a, float f) { return new Vector2(a.x * f, a.y * f); }
        public static Vector2 operator *(float f, Vector2 a) { return a * f; }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); } public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static float Distance(Vector2 a, Vector2 b) { return (a - b).magnitude; }
    }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } } public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } } public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } } public static Vector3 left { get { return new Vector3(-1, 0, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } } public static Vector3 back { get { return new Vector3(0, 0, -1); } }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } } public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { float m = magnitude; return m > 1e-6f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); } public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); } public static Vector3 operator *(Vector3 a, float f) { return new Vector3(a.x * f, a.y * f, a.z * f); }
        public static Vector3 operator *(float f, Vector3 a) { return a * f; } public static Vector3 operator /(Vector3 a, float f) { return new Vector3(a.x / f, a.y / f, a.z / f); }
        public static bool operator ==(Vector3 a, Vector3 b) { return (a - b).sqrMagnitude < 1e-10f; } public static bool operator !=(Vector3 a, Vector3 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector3 && this == (Vector3)o; } public override int GetHashCode() { return 0; }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static float Distance(Vector3 a, Vector3 b) { return (a - b).magnitude; }
        public static Vector3 SmoothDamp(Vector3 c, Vector3 t, ref Vector3 v, float s) { return t; }
        public string ToString(string f) { return "(" + x.ToString(f) + ", " + y.ToString(f) + ", " + z.ToString(f) + ")"; }
        public override string ToString() { return ToString("F3"); }
    }
    public struct Quaternion
    {
        public float x, y, z, w; public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity { get { return new Quaternion(0, 0, 0, 1); } }
        public static Quaternion operator *(Quaternion a, Quaternion b)
        {
            return new Quaternion(a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
                                  a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z); var t = 2f * Vector3.Cross(u, v);
            return v + q.w * t + Vector3.Cross(u, t);
        }
        static Quaternion AxisAngle(Vector3 a, float deg) { float r = deg * Mathf.Deg2Rad * 0.5f, s = (float)Math.Sin(r); return new Quaternion(a.x * s, a.y * s, a.z * s, (float)Math.Cos(r)); }
        /// <summary>Unity order: Z, then X, then Y.</summary>
        public static Quaternion Euler(float x, float y, float z) { return AxisAngle(Vector3.up, y) * AxisAngle(Vector3.right, x) * AxisAngle(Vector3.forward, z); }
        public static Quaternion LookRotation(Vector3 forward) { return LookRotation(forward, Vector3.up); }
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 f = forward.normalized, r = Vector3.Cross(up, f).normalized, u = Vector3.Cross(f, r);
            float m00 = r.x, m01 = u.x, m02 = f.x, m10 = r.y, m11 = u.y, m12 = f.y, m20 = r.z, m21 = u.z, m22 = f.z, tr = m00 + m11 + m22;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; return new Quaternion((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s); }
            if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; return new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s); }
            if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; return new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s); }
            { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; return new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s); }
        }
    }
    public struct Matrix4x4 { public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33; }
    public struct LOD { public Renderer[] renderers; }
    public class LODGroup : Component { public LOD[] GetLODs() { return new LOD[0]; } }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180), Rad2Deg = (float)(180 / Math.PI);
        public static float Abs(float v) { return Math.Abs(v); } public static float Sin(float v) { return (float)Math.Sin(v); } public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Asin(float v) { return (float)Math.Asin(v); } public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); } public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; } public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
        public static float Clamp01(float v) { return Clamp(v, 0, 1); } public static float Max(float a, float b) { return Math.Max(a, b); } public static int Max(int a, int b) { return Math.Max(a, b); } public static float Min(float a, float b) { return Math.Min(a, b); }
        public static float SmoothDamp(float c, float t, ref float v, float s) { return t; } public static float SmoothDampAngle(float c, float t, ref float v, float s) { return t; }
    }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white { get { return new Color(1, 1, 1); } } }
    public struct Color32 { public byte r, g, b, a; public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } public bool Contains(Vector2 p) { return p.x >= x && p.y >= y && p.x < x + width && p.y < y + height; } }
    public struct Bounds
    {
        public Vector3 center, extents; public Bounds(Vector3 c, Vector3 size) { center = c; extents = size * 0.5f; }
        public Vector3 size { get { return extents * 2; } } public Vector3 min { get { return center - extents; } } public Vector3 max { get { return center + extents; } }
        public void Encapsulate(Bounds b)
        {
            Vector3 mn = new Vector3(Math.Min(min.x, b.min.x), Math.Min(min.y, b.min.y), Math.Min(min.z, b.min.z)), mx = new Vector3(Math.Max(max.x, b.max.x), Math.Max(max.y, b.max.y), Math.Max(max.z, b.max.z));
            center = (mn + mx) * 0.5f; extents = (mx - mn) * 0.5f;
        }
    }
    public struct Ray { public Vector3 origin, direction; public Ray(Vector3 o, Vector3 d) { origin = o; direction = d; } public Vector3 GetPoint(float d) { return origin + direction * d; } }
    public struct Plane { public Plane(Vector3 n, Vector3 p) {} public bool Raycast(Ray r, out float e) { e = 0; return false; } }
    public struct RaycastHit { public Vector3 point, normal; public float distance; public int triangleIndex; public Collider collider; public Transform transform { get { return collider != null ? collider.transform : null; } } }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics
    {
        public static bool queriesHitBackfaces;
        public static bool Raycast(Ray r, out RaycastHit h, float d) { h = default(RaycastHit); return false; }
        public static int RaycastNonAlloc(Ray r, RaycastHit[] hits, float d, int mask, QueryTriggerInteraction q) { return 0; }
        public static int OverlapSphereNonAlloc(Vector3 p, float r, Collider[] results, int mask, QueryTriggerInteraction q) { return 0; }
    }
    public class Collider : Component { public Bounds bounds; } public class Mesh : Object { public Vector3[] vertices; public int[] triangles; public bool isReadable = true; }
    public class MeshCollider : Collider { public Mesh sharedMesh; public bool convex; }
    public class MeshFilter : Component { public Mesh sharedMesh; } public class Renderer : Component { public bool enabled = true; public Bounds bounds; public Matrix4x4 localToWorldMatrix; } public class MeshRenderer : Renderer {}
    public class Camera : Behaviour
    {
        public static Camera main; public bool orthographic; public float orthographicSize = 5, fieldOfView = 60, aspect = 16f / 9f, farClipPlane = 1000, nearClipPlane = 0.3f; public bool isActiveAndEnabled = true; public Rect pixelRect = new Rect(0, 0, 1920, 1080);
        public Ray ScreenPointToRay(Vector3 p) { return new Ray(transform.position, transform.forward); } public Vector3 WorldToScreenPoint(Vector3 p) { return new Vector3(0, 0, 1); }
    }
    public static class Shader { public static int PropertyToID(string n) { return 0; } public static float GetGlobalFloat(int id) { return 0; } }
    public enum TextureFormat { RGBA32 } public enum FilterMode { Bilinear } public enum TextureWrapMode { Clamp }
    public class Texture2D : Object { public FilterMode filterMode; public TextureWrapMode wrapMode; public Texture2D(int w, int h, TextureFormat f, bool m) {} public void SetPixels32(Color32[] p) {} public void Apply() {} }
    public class Sprite : Object { public static Sprite Create(Texture2D t, Rect r, Vector2 p, float ppu) { return new Sprite(); } }
    public enum RenderMode { ScreenSpaceOverlay } public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; public bool overrideSorting; }
    public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} }
    public static class Screen { public static int width = 1920, height = 1080; }
    public static class Time { public static float unscaledTime; }
    public enum KeyCode { None, M, F, Escape, Backspace, Delete, LeftControl, RightControl }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch { public TouchPhase phase; public Vector2 position, deltaPosition; }
    public static class Input
    {
        public static Vector3 mousePosition; public static Vector2 mouseScrollDelta; public static int touchCount;
        public static Touch GetTouch(int i) { return new Touch(); } public static bool GetMouseButton(int b) { return false; } public static bool GetMouseButtonDown(int b) { return false; } public static bool GetMouseButtonUp(int b) { return false; }
        public static bool GetKey(KeyCode k) { return false; } public static bool GetKeyDown(KeyCode k) { return false; }
    }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} } public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) {} } public class SerializeField : Attribute {} public class RequireComponent : Attribute { public RequireComponent(Type t) {} }
}
namespace UnityEngine.Events { public class UnityEvent { public void AddListener(Action a) {} public void RemoveListener(Action a) {} } public class UnityEvent<T> { public void AddListener(Action<T> a) {} public void RemoveListener(Action<T> a) {} } }
namespace UnityEngine.UI
{
    public class Graphic : Behaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform { get { return (RectTransform)transform; } } }
    public class Image : Graphic { public Sprite sprite; }
    public class Toggle : Behaviour {} public class Scrollbar : Behaviour {} public class LayoutGroup : Behaviour {} public class RectMask2D : Behaviour {}
    public class ScrollRect : Behaviour { public RectTransform content, viewport; public Scrollbar verticalScrollbar; public float verticalNormalizedPosition; }
    public class Button : Behaviour { public UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent(); public bool interactable; }
    public class InputField : Behaviour { public bool isFocused; }
}
namespace UnityEngine.EventSystems
{
    public class RaycastResult {} public class PointerEventData { public Vector2 position; public PointerEventData(EventSystem e) {} }
    public class EventSystem : Behaviour { public static EventSystem current; public void SetSelectedGameObject(GameObject g) {} public GameObject currentSelectedGameObject; public void RaycastAll(PointerEventData p, List<RaycastResult> r) {} public bool IsPointerOverGameObject() { return false; } }
}
namespace TMPro
{
    using UnityEngine;
    public enum TextAlignmentOptions { Center, MidlineLeft } public enum FontStyles { Normal, Italic } public class TMP_FontAsset : Object {} public enum TextWrappingModes { NoWrap, Normal }
    public class TMP_Text : UnityEngine.UI.Graphic { public string text; public TMP_FontAsset font; public FontStyles fontStyle; public float fontSize; public TextAlignmentOptions alignment; public TextWrappingModes textWrappingMode; public bool richText; public Vector2 GetPreferredValues(string t) { return new Vector2(t.Length * 8, 20); } }
    public class TextMeshProUGUI : TMP_Text {}
    public class TMP_InputField : Behaviour
    {
        public enum LineType { SingleLine } public bool isFocused; public string text = ""; public RectTransform textViewport; public TMP_Text textComponent; public UnityEngine.UI.Graphic placeholder, targetGraphic;
        public LineType lineType; public bool richText; public TMP_FontAsset fontAsset; public void ActivateInputField() {}
        public UnityEngine.Events.UnityEvent<string> onValueChanged = new UnityEngine.Events.UnityEvent<string>(), onSubmit = new UnityEngine.Events.UnityEvent<string>();
    }
    public class TMP_Dropdown : Behaviour
    {
        public class OptionData { public OptionData(string t) {} }
        public UnityEngine.Events.UnityEvent<int> onValueChanged = new UnityEngine.Events.UnityEvent<int>();
        public bool IsExpanded; public RectTransform template; public TMP_Text captionText; public int value; public void Hide() {} public void ClearOptions() {} public void AddOptions(List<OptionData> o) {} public void SetValueWithoutNotify(int v) {}
    }
}
