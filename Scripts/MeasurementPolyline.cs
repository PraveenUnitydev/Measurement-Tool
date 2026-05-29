
using UnityEngine;

public class MeasurementPolyline : MonoBehaviour
{
    public LineRenderer line;

    public void Ensure()
    {
        if (!line)
        {
            line = gameObject.GetComponent<LineRenderer>();
            if (!line) line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            line.startWidth = 0.02f;
            line.endWidth = 0.02f;
            line.numCapVertices = 6;  // rounded caps
        }
    }

    public void SetPoints(Vector3[] pts, Color color)
    {
        Ensure();
        line.positionCount = pts.Length;
        line.SetPositions(pts);
        line.sharedMaterial.color = color;
    }
}
