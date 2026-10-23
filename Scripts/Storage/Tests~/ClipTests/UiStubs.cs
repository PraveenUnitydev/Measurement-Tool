// Extra stand-ins so ClipSectionMode.cs compiles outside Unity (compile check only).
using System; using System.Collections.Generic;
namespace UnityEngine.Events { public class UnityEvent { public void AddListener(Action a) {} public void RemoveListener(Action a) {} } public class UnityEvent<T> { public void AddListener(Action<T> a) {} } }
namespace UnityEngine {
  public class MonoBehaviour : Component { public bool isActiveAndEnabled = true; public static T Instantiate<T>(T o) where T : Object { return o; } }
  public class Animator : Component { public void SetBool(string n, bool v) {} }
  public class SerializeField : Attribute {} public class HeaderAttribute : Attribute { public HeaderAttribute(string s) {} } public class TooltipAttribute : Attribute { public TooltipAttribute(string s) {} }
  public static class Mathf { public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) if (x > m) m = x; return m; } }
}
namespace UnityEngine.Rendering { public class RenderPipelineAsset {} public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; } public enum BlendMode { SrcAlpha = 5, OneMinusSrcAlpha = 10 } }
namespace UnityEngine.UI {
  public class Selectable : MonoBehaviour { public bool interactable; }
  public class Button : Selectable { public Events.UnityEvent onClick = new Events.UnityEvent(); }
  public class Slider : Selectable { public float minValue, maxValue, value; public Events.UnityEvent<float> onValueChanged = new Events.UnityEvent<float>(); }
  public class Toggle : Selectable { public bool isOn; public Events.UnityEvent<bool> onValueChanged = new Events.UnityEvent<bool>(); }
  public class Image : MonoBehaviour { public Color color; public Sprite sprite; }
}
namespace UnityEngine { public class Sprite : Object {} }
namespace TMPro { public class TMP_Text : UnityEngine.MonoBehaviour { public string text; } }
