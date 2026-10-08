// More stand-ins for the code-built screens (Download & update, update notice). Compile checks only.
using System; using System.Collections.Generic;
namespace UnityEngine.UI {
  public class GridLayoutGroup : LayoutGroup { public enum Corner { UpperLeft, UpperRight } public UnityEngine.Vector2 cellSize, spacing; public Corner startCorner; }
  public class Toggle : Selectable { public bool isOn; public bool interactable; public Graphic targetGraphic, graphic; public ToggleEvent onValueChanged = new ToggleEvent(); public void SetIsOnWithoutNotify(bool v) { isOn = v; }
    public class ToggleEvent { public void AddListener(UnityEngine.Events.UnityAction<bool> a) {} } }
}
namespace UnityEngine.Events { public delegate void UnityAction<T>(T a); }
namespace TMPro {
  public class TMP_InputField : UnityEngine.Behaviour { public enum LineType { SingleLine } public UnityEngine.RectTransform textViewport; public TextMeshProUGUI textComponent; public UnityEngine.UI.Graphic placeholder, targetGraphic; public LineType lineType;
    public SubmitEvent onValueChanged = new SubmitEvent(); public class SubmitEvent { public void AddListener(UnityEngine.Events.UnityAction<string> a) {} } }
}
namespace UnityEngine { public static class Mathf { public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; } public static int RoundToInt(float v) { return (int)Math.Round(v); } } }
namespace VehicleMeasurement { public class HomeController : UnityEngine.MonoBehaviour {}
  public partial class RemoteAddressableVehicleLoader { public bool IsCatalogFailed; public event Action<string, UnityEngine.Sprite> ThumbnailUpdated; public UnityEngine.Events.UnityEvent<int> OnCatalogLoaded; public bool IsLoading; public void RefreshCatalog() {} public UnityEngine.Sprite GetThumbnail(string id) { return null; } void Touch() { ThumbnailUpdated?.Invoke("", null); } }
  public static class DasDialog { public static object Info(string t, string m, Action ok = null) { return null; } public static object Confirm(string t, string m, string y, string n, Action a, Action b = null) { return null; } }
  public class VehicleDataManager { public static VehicleDataManager Instance; public void GoToMeasurement(string id) {} public void GoToMeasurementNew() {} public void SetSelectedModel(string a, string b) {} public void SetSelectedLocalModel(string p) {} }
  public static class VehicleMeasurementStorage { public static bool Exists(string id) { return false; } } }

namespace UnityEngine { public class TextAsset : Object { public string text; } public static class Resources { public static T Load<T>(string p) where T : class { return null; } public static void UnloadAsset(Object o) {} } }
