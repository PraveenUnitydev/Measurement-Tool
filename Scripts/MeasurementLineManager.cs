
using UnityEngine;

public class MeasurementLineManager : MonoBehaviour
{
    [SerializeField] private VehicleDimensionCalculator calculator;
    [SerializeField] private MeasurementLine lineW103;
    [SerializeField] private MeasurementLine lineW144;
    [SerializeField] private MeasurementLine lineW145;
    [SerializeField] private MeasurementLine lineW106;

    // Optional colors per code
    [SerializeField] private Color colorW103 = new Color(0.9f, 0.5f, 0.1f);
    [SerializeField] private Color colorW144 = new Color(0.2f, 0.8f, 1f);
    [SerializeField] private Color colorW145 = new Color(0.6f, 0.6f, 1f);
    [SerializeField] private Color colorW106 = new Color(0f, 1f, 1f);

    // Provide A/B points from your scanner (you can store them in calculator when it computes)
    public void ShowW103(Vector3 A, Vector3 B)
    {
        if (!lineW103) return;
        lineW103.SetPoints(A, B);
        lineW103.SetLabel("W103");
        lineW103.SetActive(true);
        // optional: tint
        // lineW103.GetComponent<LineRenderer>().sharedMaterial.color = colorW103;
    }

    public void ShowW144(Vector3 A, Vector3 B)
    {
        if (!lineW144) return;
        lineW144.SetPoints(A, B);
        lineW144.SetLabel("W144");
        lineW144.SetActive(true);
    }

    public void ShowW145(Vector3 A, Vector3 B)
    {
        if (!lineW145) return;
        lineW145.SetPoints(A, B);
        lineW145.SetLabel("W145");
        lineW145.SetActive(true);
    }

    public void ShowW106(Vector3 A, Vector3 B)
    {
        if (!lineW106) return;
        lineW106.SetPoints(A, B);
        lineW106.SetLabel("W106");
        lineW106.SetActive(true);
    }

    public void HideAll()
    {
        if (lineW103) lineW103.SetActive(false);
        if (lineW144) lineW144.SetActive(false);
        if (lineW145) lineW145.SetActive(false);
        if (lineW106) lineW106.SetActive(false);
    }
}
