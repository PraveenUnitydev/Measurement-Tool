
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT
using TMPro;
#endif

[ExecuteAlways]
public class MeasurementLine : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LineRenderer line;
    [SerializeField] private Transform labelTransform;
#if TMP_PRESENT
    [SerializeField] private TextMeshPro labelTMP;
#else
    [SerializeField] private Text labelUI;
#endif
    [SerializeField] private Transform endpointA;
    [SerializeField] private Transform endpointB;

    [Header("Appearance")]
    [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private float lineWidth = 0.02f;
    [SerializeField] private float labelOffset = 0.05f;      // meters above the line
    [SerializeField] private float endpointRadius = 0.015f;  // sphere scale

    [Header("Units")]
    [SerializeField] private bool outputInMillimeters = true;
    [SerializeField] private int decimals = 1;
    [SerializeField] private string prefix = ""; // e.g., "W103"

    [Header("Billboarding")]
    [SerializeField] private bool billboardLabel = true;
    [SerializeField] private Camera targetCamera; // if null, uses Camera.main or Camera.current

    // Runtime state
    private Vector3 pointA;
    private Vector3 pointB;
    private bool initialized;

    private void Awake()
    {
        EnsureComponents();
        ApplyAppearance();
    }

    private void OnEnable()
    {
        ApplyAppearance();
        UpdateLine();
    }

    private void Update()
    {
        // Keep label facing camera in gameplay & editor
        if (billboardLabel && labelTransform)
        {
            var cam = targetCamera ?? (Application.isPlaying ? Camera.main : Camera.current);
            if (cam)
            {
                labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cam.transform.position, Vector3.up);
            }
        }
    }

    public void SetPoints(Vector3 aWS, Vector3 bWS)
    {
        pointA = aWS;
        pointB = bWS;
        UpdateLine();
    }

    public void SetLabel(string textPrefix)
    {
        prefix = textPrefix ?? "";
        UpdateLabel();
    }

    public void SetActive(bool active)
    {
        if (line) line.enabled = active;
        if (labelTransform) labelTransform.gameObject.SetActive(active);
        if (endpointA) endpointA.gameObject.SetActive(active);
        if (endpointB) endpointB.gameObject.SetActive(active);
    }

    private void EnsureComponents()
    {
        if (!line)
        {
            line = gameObject.GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
        }
       /* if (!labelTransform)
        {
            // Create label object if missing
            var go = new GameObject("Label");
            go.transform.SetParent(transform, false);
            labelTransform = go.transform;

#if TMP_PRESENT
            labelTMP = go.AddComponent<TextMeshPro>();
            labelTMP.fontSize = 2.0f;
            labelTMP.alignment = TextAlignmentOptions.Center;
#else
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var labelGO = new GameObject("Text");
            labelGO.transform.SetParent(go.transform, false);
            labelUI = labelGO.AddComponent<Text>();
            labelUI.fontSize = 24;
            labelUI.alignment = TextAnchor.MiddleCenter;
            // Add a CanvasScaler if you need DPI scaling
#endif
        }*/
        if (!endpointA)
        {
            endpointA = CreateMarker("EndpointA");
        }
        if (!endpointB)
        {
            endpointB = CreateMarker("EndpointB");
        }

        initialized = true;
    }

    private Transform CreateMarker(string name)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(transform, false);
        var col = go.GetComponent<Collider>();
        if (col) DestroyImmediate(col); // visual only
        go.transform.localScale = Vector3.one * endpointRadius;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr) mr.sharedMaterial = new Material(Shader.Find("Standard"));
        return go.transform;
    }

    private void ApplyAppearance()
    {
        if (!initialized) EnsureComponents();

        // Line
        if (line)
        {
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;

            // Material & color
            if (!line.sharedMaterial)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = lineColor;
                line.sharedMaterial = mat;
            }
            else
            {
                line.sharedMaterial.color = lineColor;
            }
        }

        // Endpoints
        if (endpointA)
        {
            endpointA.localScale = Vector3.one * endpointRadius;
            var rend = endpointA.GetComponent<MeshRenderer>();
            if (rend) rend.sharedMaterial.color = lineColor;
        }
        if (endpointB)
        {
            endpointB.localScale = Vector3.one * endpointRadius;
            var rend = endpointB.GetComponent<MeshRenderer>();
            if (rend) rend.sharedMaterial.color = lineColor;
        }

        // Label color
#if TMP_PRESENT
        if (labelTMP) labelTMP.color = Color.white;
#else
        if (labelUI) labelUI.color = Color.white;
#endif
    }

    private void UpdateLine()
    {
        if (!line) return;

        line.SetPosition(0, pointA);
        line.SetPosition(1, pointB);

        if (endpointA) endpointA.position = pointA;
        if (endpointB) endpointB.position = pointB;

        UpdateLabel();
    }

    private void UpdateLabel()
    {
        var mid = (pointA + pointB) * 0.5f;
        if (labelTransform) labelTransform.position = mid + Vector3.up * labelOffset;

        float meters = Vector3.Distance(pointA, pointB);
        float value = outputInMillimeters ? meters * 1000f : meters;
        string units = outputInMillimeters ? "mm" : "m";


        string valStr = value.ToString($"F{decimals}");

        string text = string.IsNullOrEmpty(prefix)
            ? $"{valStr} {units}"
            : $"{prefix}: {valStr} {units}";


#if TMP_PRESENT
        if (labelTMP) labelTMP.text = text;
#else
        if (labelUI) labelUI.text = text;
#endif
    }
}
