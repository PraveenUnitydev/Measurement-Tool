using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;          // Object to orbit around

    [Header("Orbit Settings")]
    public float distance = 8f;       // Default distance
    public float xSpeed = 120f;       // Horizontal sensitivity
    public float ySpeed = 80f;        // Vertical sensitivity
    public float yMinLimit = -20f;    // Clamp vertical angle
    public float yMaxLimit = 80f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 2f;
    public float minDistance = 3f;
    public float maxDistance = 15f;

    private float x = 0f;
    private float y = 0f;

    void Start()
    {
        if (target == null)
        {
            Debug.LogWarning("OrbitCamera: No target assigned!");
            return;
        }

        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Only rotate when RMB is held
        if (Input.GetMouseButton(1))
        {
            x += Input.GetAxis("Mouse X") * xSpeed * Time.deltaTime;
            y -= Input.GetAxis("Mouse Y") * ySpeed * Time.deltaTime;
            y = Mathf.Clamp(y, yMinLimit, yMaxLimit);
        }

        // Zoom with scroll wheel
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        // Apply rotation and position
        Quaternion rotation = Quaternion.Euler(y, x, 0);
        Vector3 negDistance = new Vector3(0.0f, 0.0f, -distance);
        Vector3 position = rotation * negDistance + target.position;

        transform.rotation = rotation;
        transform.position = position;
    }
}
