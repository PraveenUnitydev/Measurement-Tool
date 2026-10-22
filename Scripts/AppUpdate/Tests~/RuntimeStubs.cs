// Compile-check stand-ins for the Unity runtime API used by AppUpdater.cs (Unity 6 signatures). Nothing here really runs
// Unity; the tests only call AppUpdater's plain functions.
using System; using System.Collections;
namespace UnityEngine {
  public class Object { public static void DontDestroyOnLoad(Object o) {} public static void Destroy(Object o) {} public static implicit operator bool(Object o) { return o != null; } }
  public enum HideFlags { None, HideInHierarchy }
  public class GameObject : Object { public HideFlags hideFlags; public GameObject(string n) {} public T AddComponent<T>() where T : Component, new() { return new T(); } }
  public class Component : Object { public GameObject gameObject; }
  public class Behaviour : Component {}
  public class Coroutine {}
  public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(IEnumerator e) { return null; } }
  public enum RuntimeInitializeLoadType { AfterSceneLoad }
  public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
  public static class Application { public static string dataPath = "/x/DAS_Data", productName = "DAS", persistentDataPath = "/tmp", version = "1.0.0"; public static event Action quitting; public static void Quit() { if (quitting != null) quitting(); } }
  public static class PlayerPrefs { public static string GetString(string k, string d) { return d; } }
  public enum KeyCode { U, LeftControl, RightControl, LeftShift, RightShift }
  public static class Input { public static bool GetKey(KeyCode k) { return false; } public static bool GetKeyDown(KeyCode k) { return false; } }
  public static class Time { public static float unscaledTime; }
  public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} }
  public static class JsonUtility { public static string ToJson(object o) { return ""; } public static T FromJson<T>(string s) { return default(T); } }
  public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float s) {} }
  public class AsyncOperation { public bool isDone; }
}
namespace UnityEngine.Networking {
  public class UploadHandler {} public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] b) {} }
  public class DownloadHandler { public string text; } public class DownloadHandlerBuffer : DownloadHandler {}
  public class DownloadHandlerFile : DownloadHandler { public DownloadHandlerFile(string p) {} public bool removeFileOnAbort; }
  public class UnityWebRequestAsyncOperation : UnityEngine.AsyncOperation {}
  public class UnityWebRequest : IDisposable {
    public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
    public UnityWebRequest(string u, string m) {} public static UnityWebRequest Get(string u) { return new UnityWebRequest(u, "GET"); } public static string EscapeURL(string s) { return s; }
    public UploadHandler uploadHandler; public DownloadHandler downloadHandler; public int timeout; public long responseCode; public Result result; public string error; public float downloadProgress;
    public void SetRequestHeader(string k, string v) {} public UnityWebRequestAsyncOperation SendWebRequest() { return null; } public void Abort() {} public void Dispose() {} }
}
namespace VehicleMeasurement {
  public class DasToast { public enum Tone { Info, Good, Warn, Error }
    public static void Show(string title, string message, Tone tone = Tone.Info, string actionLabel = null, Action action = null, string key = null, float seconds = 0f) {}
    public static void Dismiss(string key) {} }
  public class DasDialog { public static DasDialog Confirm(string t, string m, string y, string n, Action onYes, Action onNo = null) { return null; }
    public static DasDialog Progress(string t, string m, string b = null, Action onB = null) { return null; } public static DasDialog Info(string t, string m, Action ok = null) { return null; }
    public void SetProgress(float p, string d) {} public void SetMessage(string m) {} public void Close() {} }
  public static class DasServer { public const string ApiBase = "https://x/api/das"; }
  public static class DasHttp { public static IEnumerator Send(Func<UnityEngine.Networking.UnityWebRequest> make, Action<UnityEngine.Networking.UnityWebRequest> done, int attempts = 2) { yield break; } }
}
