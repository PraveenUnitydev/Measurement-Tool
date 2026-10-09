// Stand-ins for the measurement types VehicleMeasurementStorage needs (compile + migration tests only).
using System;
namespace UnityEngine { public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public static Vector3 operator *(Vector3 a, float f) { return new Vector3(a.x * f, a.y * f, a.z * f); } } }
namespace VehicleMeasurement {
  public enum ModelLoadType { Resources, Addressables, StreamingAssets, SceneReference }
  public class VehicleBoundsResults { public UnityEngine.Vector3 BoundingMin, BoundingMax, BoundingCenter; }
  public class VehicleMeasurementSystem : UnityEngine.MonoBehaviour {
    public bool IsAnalyzed; public float H100_OverallHeight, H101_GroundClearance, L101_Wheelbase, L103_OverallLength, L104_FrontOverhang, L105_RearOverhang, TD_F_FrontDiameter, TD_R_RearDiameter, W103_OverallWidth, W144_FrontTrack, W145_RearTrack;
    public UnityEngine.Vector3 WheelFL, WheelFR, WheelRL, WheelRR; public VehicleBoundsResults Results; }
  public class VehiclePrefabData : UnityEngine.MonoBehaviour { public bool hasVALData; }
}
namespace UnityEngine {
  public static partial class Application { public static void OpenURL(string u) {} }
  public partial class Texture2D { public bool isReadable = true; public Texture2D(int w, int h) {} public void ReadPixels(Rect r, int x, int y) {} public void Apply() {} public byte[] EncodeToPNG() { return new byte[] { 1 }; } public bool LoadImage(byte[] b) { return true; } }
  public class RenderTexture : Object { public int width, height; public static RenderTexture active; public static RenderTexture GetTemporary(int w, int h) { return new RenderTexture(); } public static void ReleaseTemporary(RenderTexture r) {} }
  public enum RuntimeInitializeLoadType { BeforeSceneLoad, AfterSceneLoad } public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() {} public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
  public static class Graphics { public static void Blit(Texture2D t, RenderTexture r) {} }
}
