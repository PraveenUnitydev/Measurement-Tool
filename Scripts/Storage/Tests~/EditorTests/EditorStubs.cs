// Compile-check stand-ins for the UnityEditor / Addressables-editor APIs used by Editor/Publish (signatures copied from
// Unity 6 and the Addressables 2.7.6 source). Compile only - nothing here runs.
using System; using System.Collections.Generic; using System.Threading.Tasks;
namespace UnityEngine {
  public class Object { public string name; public static void DestroyImmediate(Object o) {} public static implicit operator bool(Object o) { return o != null; } }
  public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponentInChildren<T>(bool inactive) { return default(T); } public T[] GetComponentsInChildren<T>(bool inactive) { return new T[0]; } }
  public class Transform : Component {}
  public class GameObject : Object { public T GetComponentInChildren<T>(bool inactive) { return default(T); } public T[] GetComponentsInChildren<T>(bool inactive) { return new T[0]; } }
  public struct Vector3 { public float x, y, z; }
  public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
  public struct Bounds { public Vector3 size; public void Encapsulate(Bounds b) {} }
  public class Renderer : Component { public Bounds bounds; public Material[] sharedMaterials; }
  public class Mesh : Object { public int subMeshCount; public uint GetIndexCount(int s) { return 0; } }
  public class MeshFilter : Component { public Mesh sharedMesh; }
  public class Shader : Object {} public class Material : Object { public Shader shader; }
  public class Texture : Object {} public enum TextureFormat { RGB24 } public class Texture2D : Texture { public Texture2D(int w, int h) {} public Texture2D(int w, int h, TextureFormat f, bool m) {} public void ReadPixels(Rect r, int x, int y) {} public void Apply() {} public byte[] EncodeToPNG() { return null; } public bool LoadImage(byte[] b) { return true; } }
  public static class ImageConversion { }
  public enum RenderTextureFormat { ARGB32 } public class RenderTexture : Texture { public static RenderTexture active; public static RenderTexture GetTemporary(int w, int h, int d, RenderTextureFormat f) { return null; } public static void ReleaseTemporary(RenderTexture t) {} }
  public class Camera : Component { public RenderTexture targetTexture; public float aspect; public void Render() {} }
  public struct Rect { public Rect(float x, float y, float w, float h) {} }
  public static class Mathf { public static float Max(params float[] v) { return 0; } public static int Clamp(int v, int a, int b) { return v; } }
  public static class Debug { public static void Log(object o) {} public static void LogWarning(object o) {} public static void LogError(object o) {} public static void LogException(Exception e) {} }
  public static class Application { public static void OpenURL(string u) {} }
  public static class SystemInfo { public static string deviceName = "", deviceUniqueIdentifier = ""; }
  public static class JsonUtility { public static string ToJson(object o) { return ""; } public static T FromJson<T>(string s) { return default(T); } }
  public class GUIContent { public GUIContent(string t) {} public GUIContent(string t, string tip) {} }
  public enum FontStyle { Bold } public class GUIStyle { public GUIStyle(GUIStyle o) {} public FontStyle fontStyle; }
  public class GUISkin { public GUIStyle button; } public static class GUI { public static GUISkin skin; }
  public class GUILayoutOption {} public enum ScaleMode { ScaleToFit }
  public static class GUILayout { public static bool Button(string t, params GUILayoutOption[] o) { return false; } public static bool Button(GUIContent t, params GUILayoutOption[] o) { return false; } public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) { return false; }
    public static GUILayoutOption Width(float w) { return null; } public static GUILayoutOption Height(float h) { return null; } public static GUILayoutOption MinHeight(float h) { return null; } public static GUILayoutOption ExpandWidth(bool b) { return null; } }
  public static class GUILayoutUtility { public static Rect GetRect(float w, float h, params GUILayoutOption[] o) { return new Rect(); } public static Rect GetRect(float w, float h, string style) { return new Rect(); } }
  public class AsyncOperation { public bool isDone; }
  public class ScriptableObject : Object {}
}
namespace UnityEngine.Networking {
  public class UploadHandler {} public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] b) {} } public class UploadHandlerFile : UploadHandler { public UploadHandlerFile(string p) {} }
  public class DownloadHandler { public string text; } public class DownloadHandlerBuffer : DownloadHandler {}
  public class UnityWebRequestAsyncOperation : UnityEngine.AsyncOperation {}
  public class UnityWebRequest : IDisposable { public enum Result { Success, ConnectionError } public UnityWebRequest(string u, string m) {} public UploadHandler uploadHandler; public DownloadHandler downloadHandler; public int timeout; public long responseCode; public Result result; public string error; public float uploadProgress;
    public void SetRequestHeader(string k, string v) {} public UnityWebRequestAsyncOperation SendWebRequest() { return null; } public void Dispose() {} }
}
namespace UnityEditor {
  using UnityEngine;
  public class InitializeOnLoadMethodAttribute : Attribute {}
  public class MenuItem : Attribute { public MenuItem(string p, bool v, int prio) {} public MenuItem(string p) {} }
  public enum MessageType { None, Info, Warning, Error }
  public class EditorWindow : ScriptableObject { public Vector2 minSize; public static T GetWindow<T>(string title) where T : EditorWindow { return null; } public void Show() {} public void Repaint() {} }
  public static class EditorStyles { public static GUIStyle boldLabel; }
  public static class EditorGUILayout { public static void Space(float f) {} public static void LabelField(string a, params GUILayoutOption[] o) {} public static void LabelField(string a, GUIStyle s, params GUILayoutOption[] o) {} public static void HelpBox(string m, MessageType t) {}
    public static void BeginHorizontal(params GUILayoutOption[] o) {} public static void EndHorizontal() {} public static Vector2 BeginScrollView(Vector2 p, params GUILayoutOption[] o) { return p; } public static void EndScrollView() {}
    public static string TextField(string l, string v, params GUILayoutOption[] o) { return v; } public static string TextField(GUIContent l, string v, params GUILayoutOption[] o) { return v; } public static string TextArea(string v, params GUILayoutOption[] o) { return v; }
    public static int Popup(string l, int i, string[] o, params GUILayoutOption[] op) { return i; } public static Object ObjectField(string l, Object o, Type t, bool scene, params GUILayoutOption[] op) { return o; } public static bool Foldout(bool f, string c, bool toggle) { return f; } }
  public static class EditorGUI { public class DisabledScope : IDisposable { public DisabledScope(bool d) {} public void Dispose() {} } public static void ProgressBar(Rect r, float v, string t) {} public static void DrawPreviewTexture(Rect r, Texture t, Material m, ScaleMode s) {} }
  public static class EditorUtility { public static bool DisplayDialog(string a, string b, string c) { return false; } public static bool DisplayDialog(string a, string b, string c, string d) { return false; } public static string OpenFilePanel(string a, string b, string c) { return ""; }
    public static void DisplayProgressBar(string a, string b, float p) {} public static void ClearProgressBar() {} public static void SetDirty(Object o) {} }
  public static class EditorApplication { public static double timeSinceStartup; }
  public static class EditorPrefs { public static string GetString(string k, string d) { return d; } public static void SetString(string k, string v) {} }
  public static class AssetDatabase { public static string GetAssetPath(Object o) { return ""; } public static string AssetPathToGUID(string p) { return ""; } public static void SaveAssets() {} }
  public static class PrefabUtility { public static GameObject LoadPrefabContents(string p) { return null; } public static void UnloadPrefabContents(GameObject g) {} }
  public static class GameObjectUtility { public static int GetMonoBehavioursWithMissingScriptCount(GameObject g) { return 0; } }
  public class SceneView { public static SceneView lastActiveSceneView; public Camera camera; }
  public enum BuildTarget { StandaloneWindows64 } public static class EditorUserBuildSettings { public static BuildTarget activeBuildTarget; }
}
namespace UnityEngine.AddressableAssets { public static class Addressables { public static string BuildPath { get { return ""; } } } }
namespace UnityEditor.Build.Reporting { public class BuildReport {} }
namespace UnityEditor.Build { public interface IOrderedCallback { int callbackOrder { get; } } public interface IPreprocessBuildWithReport : IOrderedCallback { void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report); } public class BuildFailedException : System.Exception { public BuildFailedException(string m) : base(m) {} } }
namespace UnityEditor.AddressableAssets.Build {
  public enum MonoScriptBundleNaming { ProjectName, DefaultGroupGuid, Custom } public enum BuiltInBundleNaming { ProjectName, DefaultGroupGuid, Custom }
  public class AddressableAssetBuildResult { public string Error { get; set; } }
  public static class BuildScript { public static Action<AddressableAssetBuildResult> buildCompleted; }
  public static class ContentUpdateScript { public static string GetContentStateDataPath(bool browse) { return ""; } public static string GetContentStateDataPath(bool browse, UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings) { return ""; } } public class AddressablesPlayerBuildResult : AddressableAssetBuildResult {}
}
namespace UnityEditor.AddressableAssets.Settings {
  using UnityEditor.AddressableAssets.Build;
  public class AddressableAssetGroupSchema : UnityEngine.ScriptableObject {}
  public class AddressableAssetGroup : UnityEngine.ScriptableObject { public T GetSchema<T>() where T : AddressableAssetGroupSchema { return null; } }
  public class AddressableAssetEntry { public string address { get; set; } public HashSet<string> labels { get; set; } public AddressableAssetGroup parentGroup { get { return null; } } public bool SetLabel(string l, bool e, bool force = false, bool postEvent = true) { return true; } }
  public class ProfileValueReference { public string Id { get { return ""; } } public bool SetVariableById(AddressableAssetSettings s, string id) { return true; } public bool SetVariableByName(AddressableAssetSettings s, string n) { return true; } }
  public class AddressableAssetProfileSettings { public List<string> GetVariableNames() { return null; } public string CreateValue(string n, string d) { return ""; } public void SetValue(string p, string n, string v) {} }
  public class AddressableAssetSettings : UnityEngine.ScriptableObject {
    public string activeProfileId { get { return ""; } } public AddressableAssetGroup DefaultGroup { get; set; } public AddressableAssetProfileSettings profileSettings { get { return null; } } public bool BuildRemoteCatalog { get; set; }
    public ProfileValueReference RemoteCatalogBuildPath { get { return null; } } public ProfileValueReference RemoteCatalogLoadPath { get { return null; } } public string OverridePlayerVersion { get; set; }
    public MonoScriptBundleNaming MonoScriptBundleNaming { get; set; } public string MonoScriptBundleCustomNaming { get; set; } public BuiltInBundleNaming BuiltInBundleNaming { get; set; } public string BuiltInBundleCustomNaming { get; set; }
    public List<AddressableAssetGroup> groups { get { return null; } } public AddressableAssetEntry FindAssetEntry(string guid) { return null; }
    public AddressableAssetEntry CreateOrMoveEntry(string guid, AddressableAssetGroup g, bool readOnly = false, bool postEvent = true) { return null; }
    public AddressableAssetGroup CreateGroup(string n, bool d, bool r, bool p, List<AddressableAssetGroupSchema> s, params Type[] t) { return null; } public void RemoveGroup(AddressableAssetGroup g) {}
    public static void BuildPlayerContent(out AddressablesPlayerBuildResult r) { r = null; } }
}
namespace UnityEditor.AddressableAssets.Settings.GroupSchemas {
  using UnityEditor.AddressableAssets.Settings;
  public class BundledAssetGroupSchema : AddressableAssetGroupSchema { public enum BundlePackingMode { PackTogether } public enum BundleNamingStyle { AppendHash } public enum BundleCompressionMode { Uncompressed, LZ4 }
    public bool IncludeInBuild { get; set; } public ProfileValueReference BuildPath { get { return null; } } public ProfileValueReference LoadPath { get { return null; } } public BundlePackingMode BundleMode { get; set; } public BundleNamingStyle BundleNaming { get; set; } public BundleCompressionMode Compression { get; set; } }
  public class ContentUpdateGroupSchema : AddressableAssetGroupSchema {}
  public class PlayerDataGroupSchema : AddressableAssetGroupSchema { public bool IncludeResourcesFolders { get; set; } public bool IncludeBuildSettingsScenes { get; set; } }
}
namespace UnityEditor.AddressableAssets { public static class AddressableAssetSettingsDefaultObject { public static Settings.AddressableAssetSettings Settings { get { return null; } } } }
namespace VehicleMeasurement { public class VehiclePrefabData : UnityEngine.Component { public string vehicleName, manufacturer, modelYear, category; } public static class DasServer { public const string ApiBase = "https://x/api/das"; } }
