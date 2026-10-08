// Minimal stand-ins for the Unity types the clip-section scripts use (compile + logic checks outside Unity).
using System; using System.Collections.Generic;
namespace UnityEngine {
  public class Object { public string name = ""; public bool destroyed; public static void Destroy(Object o) { if (o != null) o.destroyed = true; } public static void Destroy(Object o, float t) { Destroy(o); }
    public static implicit operator bool(Object o) { return !ReferenceEquals(o, null) && !o.destroyed; } }
  public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
  public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 one { get { return new Vector3(1,1,1); } } public static Vector3 operator *(Vector3 a, float f) { return new Vector3(a.x*f,a.y*f,a.z*f); } public float magnitude { get { return (float)Math.Sqrt(x*x+y*y+z*z); } } }
  public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); } }
  public struct Bounds { public Vector3 center, size, min, max; public Bounds(Vector3 c, Vector3 s) { center = c; size = s; min = c; max = c; } public void Encapsulate(Bounds b) {} }
  public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white { get { return new Color(1,1,1,1); } } public override string ToString() { return $"({r},{g},{b},{a})"; } }
  public static class Debug { public static List<string> Logs = new List<string>(); public static void Log(object o) { Logs.Add("" + o); } public static void LogWarning(object o) { Logs.Add("W " + o); } public static void LogError(object o) { Logs.Add("E " + o); } }
  public class Texture : Object {}
  public class Shader : Object { public static Dictionary<string, Shader> Known = new Dictionary<string, Shader>(); public static Shader Find(string n) { Shader s; return Known.TryGetValue(n, out s) ? s : null; }
    public HashSet<string> props = new HashSet<string>(); public static void SetGlobalFloat(string n, float v) {} }
  public static class Resources { public static T Load<T>(string p) where T : Object { return null; } }
  public class Material : Object {
    public Shader shader; public int renderQueue = -1;
    public Dictionary<string, float> floats = new Dictionary<string, float>(); public Dictionary<string, Color> colors = new Dictionary<string, Color>();
    public Dictionary<string, Texture> textures = new Dictionary<string, Texture>(); public HashSet<string> texProps = new HashSet<string>();
    public HashSet<string> keywords = new HashSet<string>(); public Dictionary<string, string> tags = new Dictionary<string, string>();
    public Material(Shader s) { shader = s; if (s != null) foreach (var p in s.props) { if (p.StartsWith("f:")) floats[p.Substring(2)] = 0; else if (p.StartsWith("c:")) colors[p.Substring(2)] = Color.white; else if (p.StartsWith("t:")) texProps.Add(p.Substring(2)); } }
    public Material(Material m) { shader = m.shader; }
    public bool HasProperty(string n) { return floats.ContainsKey(n) || colors.ContainsKey(n) || texProps.Contains(n); }
    public bool HasFloat(string n) { return floats.ContainsKey(n); } public bool HasColor(string n) { return colors.ContainsKey(n); } public bool HasTexture(string n) { return texProps.Contains(n); }
    public float GetFloat(string n) { return floats[n]; } public void SetFloat(string n, float v) { floats[n] = v; } public void SetInt(string n, int v) { floats[n] = v; }
    public Color GetColor(string n) { return colors[n]; } public void SetColor(string n, Color v) { colors[n] = v; }
    public Texture GetTexture(string n) { Texture t; return textures.TryGetValue(n, out t) ? t : null; } public void SetTexture(string n, Texture t) { textures[n] = t; }
    public Vector2 GetTextureScale(string n) { return new Vector2(1,1); } public Vector2 GetTextureOffset(string n) { return new Vector2(0,0); }
    public void SetTextureScale(string n, Vector2 v) {} public void SetTextureOffset(string n, Vector2 v) {}
    public bool IsKeywordEnabled(string k) { return keywords.Contains(k); } public void EnableKeyword(string k) { keywords.Add(k); } public void DisableKeyword(string k) { keywords.Remove(k); }
    public string GetTag(string t, bool f, string d) { string v; return tags.TryGetValue(t, out v) ? v : d; } public void SetOverrideTag(string t, string v) { tags[t] = v; }
    public Color color { get { return colors["_BaseColor"]; } set { colors["_BaseColor"] = value; } }
  }
  public class Component : Object { public GameObject gameObject; public T GetComponent<T>() where T : class { return gameObject != null ? gameObject.GetComponent<T>() : null; } public Transform transform { get { return gameObject != null ? gameObject.transform : null; } } }
  public class Transform : Component { public Vector3 position; public Quaternion rotation; public Vector3 localScale; public Vector3 localPosition; }
  public class Renderer : Component { public Material[] sharedMaterials = new Material[0]; public Bounds bounds; public Material material; }
  public class MeshRenderer : Renderer {} public class ParticleSystemRenderer : Renderer {}
  public class Collider : Component {}
  public class GameObject : Object {
    public List<Component> comps = new List<Component>(); public List<GameObject> children = new List<GameObject>(); public Transform transform;
    public GameObject() { transform = new Transform { gameObject = this }; } public GameObject(string n) : this() { name = n; }
    public T AddComponent<T>() where T : Component, new() { var c = new T(); c.gameObject = this; comps.Add(c); return c; }
    public T GetComponent<T>() where T : class { foreach (var c in comps) if (c is T) return c as T; return null; }
    public T[] GetComponentsInChildren<T>(bool inactive = false) where T : class { var l = new List<T>(); Collect(l); return l.ToArray(); }
    void Collect<T>(List<T> l) where T : class { foreach (var c in comps) if (c is T) l.Add(c as T); foreach (var ch in children) ch.Collect(l); }
    public void SetActive(bool b) {} public bool activeSelf; public static GameObject CreatePrimitive(PrimitiveType t) { var g = new GameObject(); g.AddComponent<MeshRenderer>(); return g; }
  }
  public enum PrimitiveType { Quad }
}
