using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class OrbitCinemachineController : MonoBehaviour
{
    [Header("Axis Controllers")]
    public CinemachineInputAxisController xAxisController;
    public CinemachineInputAxisController yAxisController;

    [Header("Zoom Settings")]
    public CinemachineOrbitalFollow orbitalFollow; // assign OrbitalFollow component
    public float zoomSpeed = 5f;       // how fast scroll input changes target radius
    public float zoomSmooth = 10f;     // how quickly camera interpolates to target radius
    public float minRadius = 3f;
    public float maxRadius = 8f;

    private float targetRadius;
    private float currentRadius;

    void Start()
    {
        if (orbitalFollow != null)
        {
            currentRadius = orbitalFollow.Radius;
            targetRadius = currentRadius;
        }
    }

    void Update()
    {
        if (EventSystem.current.IsPointerOverGameObject())
            return;
        bool rmbHeld = Mouse.current.leftButton.isPressed;
       
            // Enable/disable axis controllers based on RMB
            if (xAxisController != null) xAxisController.enabled = rmbHeld;
        if (yAxisController != null) yAxisController.enabled = rmbHeld;

        // Zoom input (old input system)
       
            if (orbitalFollow != null)
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    targetRadius -= scroll * zoomSpeed;
                    targetRadius = Mathf.Clamp(targetRadius, minRadius, maxRadius);
                }

                // Smoothly interpolate current radius toward target
                currentRadius = Mathf.Lerp(currentRadius, targetRadius, Time.deltaTime * zoomSmooth);
                orbitalFollow.Radius = currentRadius;
            }
       // }
    }
}
