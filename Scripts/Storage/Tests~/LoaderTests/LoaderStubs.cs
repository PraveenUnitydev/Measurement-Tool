using System; using System.Collections; using System.Collections.Generic; using System.Linq;
namespace UnityEngine {
  public class Object { public bool destroyed; public string name = "obj";
    public static void Destroy(Object o) { if ((object)o != null) { o.destroyed = true; var g = o as GameObject; if ((object)g != null) foreach (var c in g.comps) c.destroyed = true; } } public static void DontDestroyOnLoad(Object o) {} public static void Destroy(Object o, float t) { Destroy(o); }
    public static bool operator ==(Object a, Object b) { bool an = (object)a == null || a.destroyed, bn = (object)b == null || b.destroyed; if (an || bn) return an && bn; return ReferenceEquals(a, b); }
    public static bool operator !=(Object a, Object b) { return !(a == b); } public override bool Equals(object o) { return base.Equals(o); } public override int GetHashCode() { return base.GetHashCode(); }
    public static implicit operator bool(Object o) { return o != null; } }
  public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject != null ? gameObject.transform : null; } } public T GetComponent<T>() { return gameObject.GetComponent<T>(); }
    public T GetComponentInChildren<T>() { foreach (var go in GameObject.All) { var t = go.transform; while (t != null) { if (t.gameObject == gameObject) { var c = go.GetComponent<T>(); if (c != null) return c; break; } t = t.parent; } } return default(T); } }
  public class Behaviour : Component {} public class Transform : Component { public Transform parent; public new GameObject gameObject { get { return base.gameObject; } set { base.gameObject = value; } } public void SetParent(Transform p, bool w) { parent = p; } }
  public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, sizeDelta, offsetMin, offsetMax; }
  public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white { get { return new Color(1, 1, 1); } } }
  public enum RenderMode { ScreenSpaceOverlay } public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; }
  public static class Mathf { public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; } public static float Clamp01(float v) { return Clamp(v, 0, 1); } }
  public class GameObject : Object { public static List<GameObject> All = new List<GameObject>(); public List<Component> comps = new List<Component>(); public Transform transform; public HideFlags hideFlags; public bool activeSelf = true;
    public GameObject() : this("go") {} public GameObject(string n, params Type[] t) { name = n; All.Add(this); transform = new RectTransform(); transform.gameObject = this; comps.Add(transform); }
    public T GetComponent<T>() { return comps.OfType<T>().FirstOrDefault(); } public T AddComponent<T>() where T : Component, new() { var c = new T(); c.gameObject = this; comps.Add(c); return c; } public void SetActive(bool b) { activeSelf = b; } }
  public enum HideFlags { HideAndDontSave }
  public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) { Sched.Start(e); return null; } public static T FindFirstObjectByType<T>() where T : Object { return null; } }
  public class Coroutine {} public class WaitForSecondsRealtime { public float s; public WaitForSecondsRealtime(float s) { this.s = s; } }
  public static class Debug { public static List<string> Logs = new List<string>(); public static void Log(object o) { Logs.Add("LOG " + o); } public static void LogWarning(object o) { Logs.Add("WARN " + o); } public static void LogError(object o) { Logs.Add("ERR " + o); } public static void LogException(Exception e) { Logs.Add("EXC " + e.Message); } }
  public static class Time { public static float realtimeSinceStartup; }
  public static class Application { public static string persistentDataPath = System.IO.Path.GetTempPath(); }
  public enum KeyCode { R, LeftControl, RightControl, LeftShift, RightShift } public static class Input { public static bool GetKey(KeyCode k) { return false; } public static bool GetKeyDown(KeyCode k) { return false; } }
  public static class JsonUtility { public static T FromJson<T>(string s) { return default(T); } public static string ToJson(object o) { return "{}"; } }
  public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero { get { return new Vector2(0, 0); } } public static Vector2 one { get { return new Vector2(1, 1); } } } public struct Rect { public Rect(float a, float b, float c, float d) {} }
  public enum TextureFormat { RGBA32 } public class Texture2D : Object { public int width, height; public byte[] bytes; public Texture2D(int w, int h, TextureFormat f, bool m) {} public bool LoadImage(byte[] b, bool r) { bytes = b; return b != null && b.Length > 0 && b[0] != 0; } }
  public class Sprite : Object { public Texture2D texture; public static Sprite Create(Texture2D t, Rect r, Vector2 p) { return new Sprite { texture = t }; } }
  public struct Cache { public bool valid; public string path; }
  public static class Caching { public static Cache currentCacheForWriting = new Cache { valid = true, path = System.IO.Path.GetTempPath() }; }
  public class SerializeField : Attribute {} public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} } public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
}
// Scheduler: one Step() = one frame. Yielded IEnumerators run nested; yielded handles wait until done.
public static class Sched {
  class Co { public Stack<IEnumerator> stack = new Stack<IEnumerator>(); public UnityEngine.ResourceManagement.AsyncOperations.IWaitable wait; }
  static List<Co> cos = new List<Co>();
  public static void Start(IEnumerator e) { var c = new Co(); c.stack.Push(e); cos.Add(c); Advance(c); }
  static void Advance(Co c) {
    while (c.stack.Count > 0) {
      var top = c.stack.Peek();
      if (!top.MoveNext()) { c.stack.Pop(); continue; }
      var cur = top.Current;
      if (cur is IEnumerator) { c.stack.Push((IEnumerator)cur); continue; }
      var w = cur as UnityEngine.ResourceManagement.AsyncOperations.IWaitable; if (w != null && !w.Done) { c.wait = w; return; }
      return; } cos.Remove(c); }
  public static void Step() { UnityEngine.Time.realtimeSinceStartup += 0.05f; foreach (var c in cos.ToList()) { var w = c.wait; if (w != null && !w.Done) continue; c.wait = null; Advance(c); } }
  public static int Running { get { return cos.Count; } }
}
public partial class Sched_ { }
namespace UnityEngine.Events {
  public delegate void UnityAction();
  public class UnityEvent { public void Invoke() {} public void AddListener(Action a) {} public void RemoveListener(Action a) {} }
  public class UnityEvent<T> { public List<Action<T>> l = new List<Action<T>>(); public void Invoke(T a) { foreach (var x in l.ToList()) x(a); } public void AddListener(Action<T> a) { l.Add(a); } public void RemoveListener(Action<T> a) { l.Remove(a); } }
  public class UnityEvent<T1, T2> { public void Invoke(T1 a, T2 b) {} }
  public class UnityEvent<T1, T2, T3> { public void Invoke(T1 a, T2 b, T3 c) {} }
}
namespace UnityEngine.ResourceManagement.ResourceLocations { public interface IResourceLocation { string ProviderId { get; } System.Type ResourceType { get; } string InternalId { get; } string PrimaryKey { get; } bool HasDependencies { get; } IList<IResourceLocation> Dependencies { get; } object Data { get; } } }
namespace UnityEngine.ResourceManagement.ResourceProviders { public interface IAssetBundleResource {} }
namespace UnityEngine.ResourceManagement.AsyncOperations {
  public enum AsyncOperationStatus { None, Succeeded, Failed }
  public struct DownloadStatus { public long DownloadedBytes, TotalBytes; }
  public interface IWaitable { bool Done { get; } }
  public class Op : IWaitable { public bool done; public AsyncOperationStatus status; public object result; public Exception ex; public long dl, total; public bool released; public string kind, key; public UnityEngine.Transform parent; public bool Done { get { return done; } } }
  public struct AsyncOperationHandle : IWaitable { public Op op; public bool IsDone { get { return op.done; } } public bool Done { get { return op.done; } } public AsyncOperationStatus Status { get { return op.status; } }
    public Exception OperationException { get { return op.ex; } } public float PercentComplete { get { return 0; } } public DownloadStatus GetDownloadStatus() { return new DownloadStatus { DownloadedBytes = op.dl, TotalBytes = op.total }; } public bool IsValid() { return op != null && !op.released; } }
  public struct AsyncOperationHandle<T> : IWaitable { public Op op; public bool IsDone { get { return op.done; } } public bool Done { get { return op.done; } } public AsyncOperationStatus Status { get { return op.status; } }
    public T Result { get { return (T)op.result; } } public Exception OperationException { get { return op.ex; } } public bool IsValid() { return op != null && !op.released; } }
}
namespace UnityEngine.AddressableAssets {
  using UnityEngine.ResourceManagement.AsyncOperations;
  public static class Addressables {
    public static List<Op> Ops = new List<Op>(); public static Dictionary<string, long> Sizes = new Dictionary<string, long>(); public static List<GameObject> Instances = new List<GameObject>(); public static List<GameObject> ReleasedInstances = new List<GameObject>();
    static Op New(string kind, string key) { var o = new Op { kind = kind, key = key }; Ops.Add(o); return o; }
    public static AsyncOperationHandle<long> GetDownloadSizeAsync(object key) { var o = New("size", (string)key); o.done = true; long s; o.status = Sizes.TryGetValue((string)key, out s) ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed; o.result = s; return new AsyncOperationHandle<long> { op = o }; }
    public static AsyncOperationHandle DownloadDependenciesAsync(object key) { var o = New("download", (string)key); o.total = Sizes[(string)key]; return new AsyncOperationHandle { op = o }; }   // test completes it
    public static AsyncOperationHandle<GameObject> InstantiateAsync(object key, Transform parent) { var o = New("instantiate", (string)key); o.parent = parent; return new AsyncOperationHandle<GameObject> { op = o }; }
    public static void Release(AsyncOperationHandle h) { if (h.op != null) h.op.released = true; } public static void Release<T>(AsyncOperationHandle<T> h) { if (h.op != null) h.op.released = true; }
    public static bool ReleaseInstance(GameObject g) { ReleasedInstances.Add(g); g.destroyed = true; return true; }
    public static AsyncOperationHandle<object> InitializeAsync() { var o = New("init", null); o.done = true; o.status = AsyncOperationStatus.Succeeded; return new AsyncOperationHandle<object> { op = o }; }
    public static AsyncOperationHandle<List<string>> CheckForCatalogUpdates(bool a) { var o = New("check", null); o.done = true; o.result = new List<string>(); return new AsyncOperationHandle<List<string>> { op = o }; }
    public static AsyncOperationHandle<List<object>> UpdateCatalogs(IEnumerable<string> l, bool a = true) { var o = New("update", null); o.done = true; return new AsyncOperationHandle<List<object>> { op = o }; }
    public static AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> LoadResourceLocationsAsync(object k) { var o = New("locs", null); o.done = true; o.result = new List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>(); return new AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> { op = o }; }
    public static Func<string, bool> CatalogOk = u => true; public static List<string> CatalogUrls = new List<string>();
    public static AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> LoadContentCatalogAsync(string url, bool autoRelease) { var o = New("catalog", url); CatalogUrls.Add(url); o.done = true; o.status = CatalogOk(url) ? AsyncOperationStatus.Succeeded : AsyncOperationStatus.Failed; if (o.status == AsyncOperationStatus.Failed) o.ex = new Exception("404 Not Found"); return new AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> { op = o }; }
    public static AsyncOperationHandle<bool> ClearDependencyCacheAsync(object k, bool a) { var o = New("clear", null); o.done = true; return new AsyncOperationHandle<bool> { op = o }; }
  }
}

namespace UnityEngine.Networking {
  public class DownloadHandler { public byte[] data; public string text; }
  public class UnityWebRequest : IDisposable { public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError } public Result result; public string error, method = "GET", url; public int timeout; public long responseCode; public DownloadHandler downloadHandler = new DownloadHandler();
    public static UnityWebRequest Get(string u) { return new UnityWebRequest { url = u }; }
    public Dictionary<string, string> requestHeaders = new Dictionary<string, string>(), responseHeaders = new Dictionary<string, string>();
    public static Action<UnityWebRequest> Server; public static List<UnityWebRequest> Sent = new List<UnityWebRequest>();
    public object SendWebRequest() { result = Result.Success; Sent.Add(this); if (Server != null) Server(this); return null; }
    public void SetRequestHeader(string k, string v) { requestHeaders[k] = v; } public string GetResponseHeader(string k) { string v; return responseHeaders.TryGetValue(k, out v) ? v : null; }
    public void Dispose() {} }
}
namespace UnityEngine.EventSystems { public class EventSystem : UnityEngine.Behaviour { public static EventSystem current; public GameObject currentSelectedGameObject; } public class StandaloneInputModule : UnityEngine.Behaviour {} }
namespace UnityEngine.UI { public class InputField : UnityEngine.Behaviour {} public class Graphic : UnityEngine.Behaviour { public Color color; public RectTransform rectTransform { get { return (RectTransform)transform; } } } public class Image : Graphic {} public class GraphicRaycaster : UnityEngine.Behaviour {}
  public class CanvasScaler : UnityEngine.Behaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; }
  public class ButtonClickedEvent { public List<UnityEngine.Events.UnityAction> l = new List<UnityEngine.Events.UnityAction>(); public void AddListener(UnityEngine.Events.UnityAction a) { l.Add(a); } public void RemoveAllListeners() { l.Clear(); } public void Invoke() { foreach (var x in l.ToList()) x(); } }
  public class Button : UnityEngine.Behaviour { public Graphic targetGraphic; public ButtonClickedEvent onClick = new ButtonClickedEvent(); } }
namespace TMPro { public class TMP_InputField : UnityEngine.Behaviour {} public enum TextAlignmentOptions { TopLeft, Center } public enum FontStyles { Bold } public class TextMeshProUGUI : UnityEngine.UI.Graphic { public string text; public float fontSize; public TextAlignmentOptions alignment; public bool raycastTarget; public FontStyles fontStyle; } }
namespace VehicleMeasurement { public static class DownloadedVehiclesTracker { public static List<string> Marked = new List<string>(); public static void MarkAsDownloaded(RemoteVehicleInfo i) { Marked.Add(i.vehicleId); } } }
namespace VehicleMeasurement.Storage { public static class VehicleStorageService { public static event Action<string, string> ReleaseRequested; } }
namespace VehicleMeasurement { [System.Serializable] public class VehicleAddressableInfo { public string vehicleId, vehicleName, addressableKey, category, manufacturer, approximateSize, description; public UnityEngine.Sprite thumbnail; } }

namespace UnityEngine.AddressableAssets.ResourceLocators { public interface IResourceLocator {} }
