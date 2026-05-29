using UnityEngine;

public class LookAtCamera : MonoBehaviour
{
    private void Update()
    {
        if (Camera.main != null)
        {
           transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
    }
}
