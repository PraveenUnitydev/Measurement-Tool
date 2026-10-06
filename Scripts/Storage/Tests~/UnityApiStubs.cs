// APPROXIMATE stand-ins for the Unity / Addressables API used by the Unity-facing storage scripts.
// They encode how the API is *believed* to look (Addressables 2.7, Unity 6), so a clean compile here catches
// typos and type mistakes but is NOT proof the real API matches - the real check is Unity's own compile.
using System; using System.Collections; using System.Collections.Generic;
namespace UnityEngine {
  public class Object { public static T FindFirstObjectByType<T>() where T : Object { return null; } public static implicit operator bool(Object o) { return o != null; } }
  public class Component : Object {} public class Behaviour : Component {}
  public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) { return null; } }
  public class Coroutine {}
  public enum KeyCode { F9 }
  public static class Input { public static bool GetKeyDown(KeyCode k) { return false; } }
  public enum RuntimePlatform { WindowsPlayer }
  public static class Application { public static string unityVersion; public static RuntimePlatform platform; public static string persistentDataPath; }
  public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
  public static class GUIUtility { public static string systemCopyBuffer { get; set; } }
  public class ContextMenu : Attribute { public ContextMenu(string n) {} }
  public class Tooltip : Attribute { public Tooltip(string t) {} }
  public enum RuntimeInitializeLoadType { BeforeSceneLoad }
  public class RuntimeInitializeOnLoadMethod : Attribute { public RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType t) {} }
  public struct Hash128 { public string Value; public static Hash128 Parse(string s) { return new Hash128 { Value = s }; } public bool isValid { get { return !string.IsNullOrEmpty(Value); } } }
  public struct CachedAssetBundle { public string name; public Hash128 hash; public CachedAssetBundle(string name, Hash128 hash) { this.name = name; this.hash = hash; } }
  public struct Cache { public bool valid; public string path; public long spaceOccupied, spaceFree, maximumAvailableDiskSpace; public int expirationDelay; }
  public static class Caching { public static Cache defaultCache; public static Cache currentCacheForWriting; public static List<Cache> Caches = new List<Cache>(); public static Func<CachedAssetBundle, bool> IsCachedFunc;
    public static int cacheCount { get { return Caches.Count; } } public static Cache GetCacheAt(int i) { return Caches[i]; } public static bool IsVersionCached(CachedAssetBundle b) { return IsCachedFunc != null && IsCachedFunc(b); } }
}
namespace UnityEngine.ResourceManagement.ResourceLocations {
  public interface IResourceLocation { string PrimaryKey { get; } string InternalId { get; } Type ResourceType { get; } object Data { get; } bool HasDependencies { get; } IList<IResourceLocation> Dependencies { get; } }
}
namespace UnityEngine.ResourceManagement.ResourceProviders { public class AssetBundleRequestOptions { public string Hash; public string BundleName; public long BundleSize; } }
namespace UnityEngine.ResourceManagement.Util { public static class ResourceManagerConfig { public static bool IsPathRemote(string p) { return true; } } }
namespace UnityEngine.ResourceManagement.AsyncOperations {
  public enum AsyncOperationStatus { None, Succeeded, Failed }
  public struct AsyncOperationHandle<T> : IEnumerator { public AsyncOperationStatus Status { get { return AsyncOperationStatus.Succeeded; } } public T Result { get { return default(T); } } public bool MoveNext() { return false; } public void Reset() {} public object Current { get { return null; } } }
}
namespace UnityEngine.AddressableAssets.ResourceLocators { public interface IResourceLocator { string LocatorId { get; } } }
namespace UnityEngine.AddressableAssets {
  public static class Addressables {
    public static IEnumerable<ResourceLocators.IResourceLocator> ResourceLocators { get { return new List<ResourceLocators.IResourceLocator>(); } }
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>> LoadResourceLocationsAsync(object key) { return default(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation>>); }
    public static void Release<T>(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> h) {}
  }
}
namespace VehicleMeasurement { public class RemoteVehicleInfo { public string vehicleId, addressableKey, version; }
  public class RemoteAddressableVehicleLoader : UnityEngine.MonoBehaviour { public static RemoteAddressableVehicleLoader Instance { get; private set; } public bool IsCatalogLoaded { get { return true; } } public List<RemoteVehicleInfo> GetAvailableVehicles() { return null; } } }
