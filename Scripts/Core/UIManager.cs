using UnityEngine;
using UnityEngine.UI;
using VehicleMeasurement;


public class UIManager : MonoBehaviour
{
    [SerializeField] private Button _reAnalyze;
    [SerializeField] private Button _Export;
    [SerializeField] private Button _compareWith;
    [SerializeField] private Button _save;


    [SerializeField] private GameObject _bufferPanel;

    private void OnEnable()
    {
        _reAnalyze.onClick.AddListener(Analyze);
        _Export.onClick.AddListener(ExportData);
        _compareWith.onClick.AddListener(CompareWith);
        _save.onClick.AddListener(SaveToSystem);
    }


    private void Analyze()
    {
       VehicleMeasurementUI vehicleMeasurementUI = FindAnyObjectByType<VehicleMeasurementUI>();
        if (vehicleMeasurementUI != null)
        {
            vehicleMeasurementUI.OnAnalyzeClicked();
        }
    }
    private void SaveToSystem()
    {
        // Save data to local as jSOn
        //intialize save load system
    }
    private void ExportData()
    {

        //Open Export Dialog
    }
    private void CompareWith()
    {
        //Load CompareScene
    }
  
}


