using UnityEngine;

public class ModelHandler : MonoBehaviour
{

    [SerializeField] private float _idleRotationSpeed=5f;
    [SerializeField] private float _dragRotationSpeed=10f;
    [SerializeField] private float _magnifyTranslationSpeed=10f;
    public bool _canRotate = false;

    private void Update()
    {
        if (!_canRotate)
        {
            return; 
        }
        transform.Rotate(Vector3.up * _idleRotationSpeed * Time.deltaTime);
    }
}
