// APPROXIMATE stand-ins for the Unity / Addressables API used by the Unity-facing storage scripts.
// They encode how the API is *believed* to look (Addressables 2.7, Unity 6), so a clean compile here catches
// typos and type mistakes but is NOT proof the real API matches - the real check is Unity's own compile.
using System; using System.Collections; using System.Collections.Generic;
namespace UnityEngine {
  public static class Time { public static float realtimeSinceStartup, unscaledTime; }
  public static class PlayerPrefs { public static Dictionary<string, string> D = new Dictionary<string, string>(); public static string GetString(string k, string d = "") { string v; return D.TryGetValue(k, out v) ? v : d; } public static void SetString(string k, string v) { D[k] = v; } public static void DeleteKey(string k) { D.Remove(k); } public static void Save() {} }
  public class Object { public string name; public static T FindFirstObjectByType<T>() where T : Object { return null; } public static void DontDestroyOnLoad(Object o) {} public static void Destroy(Object o) {} public static implicit operator bool(Object o) { return o != null; } }
  public enum HideFlags { HideAndDontSave, HideInHierarchy }
  public class GameObject : Object { public bool activeSelf = true; public HideFlags hideFlags; public Transform transform = new RectTransform();
    public GameObject() {} public GameObject(string n) {} public GameObject(string n, params Type[] components) {}
    public T AddComponent<T>() where T : Component, new() { return new T(); } public T GetComponent<T>() { return default(T); } public void SetActive(bool b) {} }
  public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float s) {} }
  public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero { get { return new Vector2(0, 0); } } public static Vector2 one { get { return new Vector2(1, 1); } } }
  public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white { get { return new Color(1, 1, 1, 1); } } }
  public class Transform : Component, System.Collections.IEnumerable { public Transform parent; public Transform Find(string n) { return null; } public void SetParent(Transform p, bool worldPositionStays) {} public System.Collections.IEnumerator GetEnumerator() { return new List<object>().GetEnumerator(); } }
  public class RectTransform : Transform { public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax; }
  public enum RenderMode { ScreenSpaceOverlay }
  public class Canvas : Behaviour { public RenderMode renderMode; public int sortingOrder; }
  public class Sprite : Object {} public class Texture2D : Object {}
  public enum TextAnchor { MiddleLeft, MiddleRight }
  public class RectOffset { public RectOffset(int l, int r, int t, int b) {} }
  public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponent<T>() { return default(T); } public T GetComponentInChildren<T>() { return default(T); } public T GetComponentInChildren<T>(bool inactive) { return default(T); } }
  public class Behaviour : Component { public bool enabled; public bool isActiveAndEnabled { get { return true; } } }
  public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) { Run(e); return null; }
    public static void Run(IEnumerator e) { while (e.MoveNext()) { var inner = e.Current as IEnumerator; if (inner != null) Run(inner); } } }
  public class Coroutine {}
  public enum KeyCode { F9, Escape, S, R, D, LeftControl, RightControl, LeftShift, RightShift }
  public static class Input { public static bool GetKeyDown(KeyCode k) { return false; } public static bool GetKey(KeyCode k) { return false; } }
  public enum RuntimePlatform { WindowsPlayer }
  public static class Application { public static void OpenURL(string u) {} public static string unityVersion; public static RuntimePlatform platform; public static string persistentDataPath; public static string temporaryCachePath; public static string streamingAssetsPath; }
  public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} public static void LogException(Exception e) {} }
  public static class GUIUtility { public static string systemCopyBuffer { get; set; } }
  public class ContextMenu : Attribute { public ContextMenu(string n) {} }
  public class Tooltip : Attribute { public Tooltip(string t) {} }
  public enum RuntimeInitializeLoadType { BeforeSceneLoad, AfterSceneLoad }
  public class RuntimeInitializeOnLoadMethod : Attribute { public RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType t) {} }
  public struct Hash128 { public string Value; public static Hash128 Parse(string s) { return new Hash128 { Value = s }; } public bool isValid { get { return !string.IsNullOrEmpty(Value); } }
    public static bool operator ==(Hash128 a, Hash128 b) { return a.Value == b.Value; } public static bool operator !=(Hash128 a, Hash128 b) { return a.Value != b.Value; }
    public override bool Equals(object o) { return o is Hash128 && ((Hash128)o).Value == Value; } public override int GetHashCode() { return (Value ?? "").GetHashCode(); } public override string ToString() { return Value ?? ""; } }
  public struct CachedAssetBundle { public string name; public Hash128 hash; public CachedAssetBundle(string name, Hash128 hash) { this.name = name; this.hash = hash; } }
  public struct Cache { public bool valid; public string path; public long spaceOccupied, spaceFree, maximumAvailableStorageSpace;
    public int expirationDelay { get { int v; return Caching.Delays.TryGetValue(path ?? "", out v) ? v : 12960000; } set { if (!Caching.SetterIgnored) Caching.Delays[path ?? ""] = value; } } }
  public static class Caching { public static Dictionary<string, int> Delays = new Dictionary<string, int>(); public static bool SetterIgnored, IsReady = true, ThrowOnDefault;
    public static bool ready { get { return IsReady; } }
    public static string DefaultPath;   // like Unity: the default cache is a fixed folder, not "whichever is first"
    public static Cache defaultCache { get { if (ThrowOnDefault) throw new InvalidOperationException("boom"); if (DefaultPath != null) foreach (var c in Caches) if (c.path == DefaultPath) return c; return Caches.Count > 0 ? Caches[0] : new Cache(); } }
    public static Cache currentCacheForWriting; public static List<Cache> Caches = new List<Cache>(); public static Func<CachedAssetBundle, bool> IsCachedFunc; public static HashSet<string> FakeCache = new HashSet<string>(); public static HashSet<string> InUse = new HashSet<string>();
    public static Cache GetCacheByPath(string p) { foreach (var c in Caches) if (c.path == p) return c; return new Cache(); }
    public static Cache AddCache(string p) { var c = new Cache { valid = true, path = p }; Caches.Add(c); return c; }
    public static void MoveCacheBefore(Cache a, Cache b) { int i = Caches.FindIndex(c => c.path == a.path), j = Caches.FindIndex(c => c.path == b.path); if (i < 0 || j < 0) return; var x = Caches[i]; Caches.RemoveAt(i); Caches.Insert(Caches.FindIndex(c => c.path == b.path), x); }
    public static int cacheCount { get { return Caches.Count; } } public static Cache GetCacheAt(int i) { return Caches[i]; } public static bool IsVersionCached(CachedAssetBundle b) { if (IsCachedFunc != null) return IsCachedFunc(b); return FakeCache.Contains(b.name + "|" + b.hash.Value); }
    public static List<string> Marked = new List<string>(); public static bool MarkThrows;
    public static bool MarkAsUsed(CachedAssetBundle b) { if (MarkThrows) throw new InvalidOperationException("boom"); string k = b.name + "|" + b.hash.Value; Marked.Add(k); return FakeCache.Contains(k); }
    public static bool ClearCache() { if (InUse.Count > 0) return false; FakeCache.Clear(); return true; }
    public static bool ClearCachedVersion(string assetBundleName, Hash128 hash) { string k = assetBundleName + "|" + hash.Value; if (InUse.Contains(k)) return false; FakeCache.Remove(k); return true; }
    // versions are stored as "name|hash" in FakeCache
    public static void GetCachedVersions(string assetBundleName, List<Hash128> outVersions) { outVersions.Clear(); foreach (string k in FakeCache) if (k.StartsWith(assetBundleName + "|")) outVersions.Add(Hash128.Parse(k.Substring(assetBundleName.Length + 1))); }
    public static bool ClearAllCachedVersions(string assetBundleName) { foreach (string k in InUse) if (k.StartsWith(assetBundleName + "|")) return false; FakeCache.RemoveWhere(k => k.StartsWith(assetBundleName + "|")); return true; }
    public static bool ClearOtherCachedVersions(string assetBundleName, Hash128 hash) { string keep = assetBundleName + "|" + hash.Value; foreach (string k in InUse) if (k.StartsWith(assetBundleName + "|") && k != keep) return false; FakeCache.RemoveWhere(k => k.StartsWith(assetBundleName + "|") && k != keep); return true; } }
}
namespace UnityEngine.ResourceManagement.ResourceLocations {
  public interface IResourceLocation { string PrimaryKey { get; } string InternalId { get; } Type ResourceType { get; } object Data { get; } bool HasDependencies { get; } IList<IResourceLocation> Dependencies { get; } }
}
namespace UnityEngine.ResourceManagement.ResourceProviders { public class AssetBundleRequestOptions { public string Hash; public string BundleName; public long BundleSize; } }
namespace UnityEngine.ResourceManagement.Util { public static class ResourceManagerConfig { public static bool IsPathRemote(string p) { return true; } } }
namespace UnityEngine.ResourceManagement.AsyncOperations {
  public enum AsyncOperationStatus { None, Succeeded, Failed }
  public struct DownloadStatus { public long DownloadedBytes, TotalBytes; }
  public struct AsyncOperationHandle : IEnumerator { public AsyncOperationStatus Status; public bool IsDone; public Exception OperationException; public bool IsValid() { return true; } public DownloadStatus GetDownloadStatus() { return new DownloadStatus(); } public bool MoveNext() { return false; } public void Reset() {} public object Current { get { return null; } } }
  public struct AsyncOperationHandle<T> : IEnumerator { public AsyncOperationStatus Status; public T Result; public AsyncOperationHandle(T r, AsyncOperationStatus st) { Result = r; Status = st; } public bool MoveNext() { return false; } public void Reset() {} public object Current { get { return null; } } }
}
namespace UnityEngine.AddressableAssets.ResourceLocators { public interface IResourceLocator { string LocatorId { get; } bool Locate(object key, System.Type type, out System.Collections.Generic.IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> locations); } }
namespace UnityEngine.AddressableAssets {
  public static class Addressables {
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<long> GetDownloadSizeAsync(object key) { return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<long>(0, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded); }
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle DownloadDependenciesAsync(object key) { return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle { IsDone = true, Status = UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded }; }
    public static void Release(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle h) {}
    public static IEnumerable<ResourceLocators.IResourceLocator> ResourceLocators { get { return new List<ResourceLocators.IResourceLocator>(); } }
    public static Dictionary<string, IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> FakeLocations = new Dictionary<string, IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>>();
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> LoadResourceLocationsAsync(object key) {
      IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> l;
      if (!FakeLocations.TryGetValue((string)key, out l)) l = new List<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>();
      return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>>(l, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded); }
    public static void Release<T>(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> h) {}
  }
}
namespace VehicleMeasurement { public class RemoteVehicleInfo { public string vehicleId, vehicleName, addressableKey, version, manufacturer, thumbnailUrl, category; public bool hasVALData; }
  public class VehicleAddressableInfo { public string vehicleId, vehicleName, addressableKey, manufacturer, category; public UnityEngine.Sprite thumbnail; }
  public partial class RemoteAddressableVehicleLoader : UnityEngine.MonoBehaviour { public bool IsVehicleContentUnavailable(string k) { return false; } public System.Collections.IEnumerator EnsureVehicleContent(string k) { yield break; } public RemoteVehicleInfo GetVehicleInfoExact(string k) { return GetVehicleInfo(k); } public static string FriendlyDownloadError(Exception e) { return e == null ? "Download failed." : e.Message; } public RemoteVehicleInfo GetVehicleInfo(string k) { return GetAvailableVehicles().Find(v => v.vehicleId == k || v.addressableKey == k); } public static RemoteAddressableVehicleLoader Instance { get; set; } public bool Loaded = true; public List<RemoteVehicleInfo> Vehicles = new List<RemoteVehicleInfo>(); public bool IsCatalogLoaded { get { return Loaded; } } public List<RemoteVehicleInfo> GetAvailableVehicles() { return Vehicles; } } }

namespace UnityEngine.Events { public delegate void UnityAction(); public class UnityEvent<T> { public void AddListener(Action<T> a) {} public void RemoveListener(Action<T> a) {} } }
namespace UnityEngine.UI {
  public class Graphic : UnityEngine.Behaviour { public UnityEngine.Color color; public bool raycastTarget; public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform(); }
  public class Selectable : UnityEngine.Behaviour { public enum Transition { None, ColorTint } public Transition transition; }
  public class Image : Graphic { public enum Type { Simple, Filled } public enum FillMethod { Horizontal } public Type type; public FillMethod fillMethod; public float fillAmount; public bool preserveAspect; public UnityEngine.Sprite sprite; }
  public class Button : Selectable { public bool interactable; public Graphic targetGraphic; public ButtonClickedEvent onClick = new ButtonClickedEvent();
    public class ButtonClickedEvent { public void AddListener(UnityEngine.Events.UnityAction a) {} public void RemoveListener(UnityEngine.Events.UnityAction a) {} } }
  public class CanvasScaler : UnityEngine.Behaviour { public enum ScaleMode { ScaleWithScreenSize } public ScaleMode uiScaleMode; public UnityEngine.Vector2 referenceResolution; public float matchWidthOrHeight; }
  public class GraphicRaycaster : UnityEngine.Behaviour {}
  public class ScrollRect : UnityEngine.Behaviour { public enum MovementType { Clamped } public bool horizontal; public float scrollSensitivity; public MovementType movementType; public UnityEngine.RectTransform viewport, content; }
  public class RectMask2D : UnityEngine.Behaviour {}
  public class LayoutGroup : UnityEngine.Behaviour { public UnityEngine.RectOffset padding; public UnityEngine.TextAnchor childAlignment; }
  public class HorizontalOrVerticalLayoutGroup : LayoutGroup { public float spacing; public bool childControlWidth, childControlHeight, childForceExpandWidth, childForceExpandHeight; }
  public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup {}
  public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup {}
  public class ContentSizeFitter : UnityEngine.Behaviour { public enum FitMode { PreferredSize } public FitMode verticalFit; }
  public class LayoutElement : UnityEngine.Behaviour { public bool ignoreLayout; public float preferredWidth, minWidth, flexibleWidth, preferredHeight, minHeight, flexibleHeight; }
}
namespace UnityEngine.EventSystems { public class EventSystem : UnityEngine.Behaviour { public static EventSystem current; } public class StandaloneInputModule : UnityEngine.Behaviour {} public class EventTrigger {} }
namespace TMPro {
  public enum TextAlignmentOptions { TopLeft, Center, MidlineLeft, MidlineRight, BottomRight }
  public enum FontStyles { Normal, Bold, Italic }
  public enum TextOverflowModes { Overflow, Ellipsis }
  public class TextMeshProUGUI : UnityEngine.UI.Graphic { public TextOverflowModes overflowMode; public string text; public float fontSize; public TextAlignmentOptions alignment; public FontStyles fontStyle; }
}
