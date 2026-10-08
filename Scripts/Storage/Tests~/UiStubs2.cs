// More stand-ins for the code-built screens (Download & update, update notice). Compile checks only.
using System; using System.Collections.Generic;
namespace UnityEngine.UI {
  public class Selectable : UnityEngine.Behaviour { public enum Transition { None, ColorTint } public Transition transition; }
  public class Toggle : Selectable { public bool isOn; public bool interactable; public Graphic targetGraphic, graphic; public ToggleEvent onValueChanged = new ToggleEvent(); public void SetIsOnWithoutNotify(bool v) { isOn = v; }
    public class ToggleEvent { public void AddListener(UnityEngine.Events.UnityAction<bool> a) {} } }
}
namespace UnityEngine.Events { public delegate void UnityAction<T>(T a); }
namespace TMPro {
  public enum TextOverflowModes { Overflow, Ellipsis }
  public class TMP_InputField : UnityEngine.Behaviour { public enum LineType { SingleLine } public UnityEngine.RectTransform textViewport; public TextMeshProUGUI textComponent; public UnityEngine.UI.Graphic placeholder, targetGraphic; public LineType lineType;
    public SubmitEvent onValueChanged = new SubmitEvent(); public class SubmitEvent { public void AddListener(UnityEngine.Events.UnityAction<string> a) {} } }
}
namespace UnityEngine { public static class Mathf { public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; } public static int RoundToInt(float v) { return (int)Math.Round(v); } } }
namespace VehicleMeasurement { public class HomeController : UnityEngine.MonoBehaviour {}
  public partial class RemoteAddressableVehicleLoader { public event Action<string, UnityEngine.Sprite> ThumbnailUpdated; public UnityEngine.Events.UnityEvent<int> OnCatalogLoaded; public bool IsLoading; public void RefreshCatalog() {} public UnityEngine.Sprite GetThumbnail(string id) { return null; } void Touch() { ThumbnailUpdated?.Invoke("", null); } }
  public static class DasDialog { public static object Info(string t, string m, Action ok = null) { return null; } } }
