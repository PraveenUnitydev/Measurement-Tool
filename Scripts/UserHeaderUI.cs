using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VehicleMeasurement;

public class UserHeaderUI : MonoBehaviour
{
    [Header("UI refs")]
    public TMP_Text displayNameText;
    public TMP_Text emailText;
    public TMP_Text roleText;
    public Button signOutButton;   // optional; wire in Inspector

    public GameObject _settingPanel;
    public Button _settingsButton;

    public Button _loadMeasurementsFromServer;
    public Button _clearCacheButton;
    private Animator _loadFromServerToggle;
    private void Awake()
    {
        if (signOutButton != null)
            signOutButton.onClick.AddListener(OnSignOutClicked);
        _settingsButton.onClick.AddListener(SettingsPanel);
        // closeButton.onClick.AddListener(() => SettingsPanel(false));    
        _loadMeasurementsFromServer.onClick.AddListener(OnLoadFromServerCliked);
        _loadFromServerToggle=_loadMeasurementsFromServer.GetComponent<Animator>();
       _clearCacheButton.onClick.AddListener(()=>PopupManager.ShowConfirm("Do you really want to clear all the cache data?",ClearCacheClicked));
       
    }
    private bool _act = false;
    private void SettingsPanel()
    {
        _act = !_act;
        _settingPanel.SetActive(_act);
        UpdateToggleAnimation(ControlledMeasurementStorage.Instance.checkServerFirst);
    }

    private void OnDestroy()
    {
        if (signOutButton != null)
            signOutButton.onClick.RemoveListener(OnSignOutClicked);
    }

    private bool isFromServer = true;
    private void OnLoadFromServerCliked()
    {
        isFromServer = !isFromServer;
       UpdateToggleAnimation(isFromServer);
        ControlledMeasurementStorage.Instance.checkServerFirst=isFromServer;
        Debug.Log("Server load "+ isFromServer);
        if (isFromServer)
        {
            PopupManager.ShowMessage("Measurements will be loaded from Server!", 2.5f);
        }
        else
        {
            PopupManager.ShowMessage("Measurements will be loader from Local!", 2.5f);
        }
           
    }
    private void UpdateToggleAnimation(bool markStatus)
    {
       _loadFromServerToggle.SetBool("Invert",markStatus);
    }
    private void ClearCacheClicked()
    {
        Caching.ClearCache();
       // AddressableVehicleLoader.Instance?.ClearCache();
       // RemoteAddressableVehicleLoader.Instance.ClearCache();
        PopupManager.ShowSuccess("Cache cleared!");
       
    }

    private void Start()
    {
        // Pull from PlayerPrefs
        var displayName = PlayerPrefs.GetString("session.displayName", "");
        var email = PlayerPrefs.GetString("session.email", "");
        var role = PlayerPrefs.GetString("session.role", "User");

        // Fallback to email if display name is empty
        if (string.IsNullOrWhiteSpace(displayName)) displayName = email;

        if (displayNameText) displayNameText.text = displayName;
        if (emailText) emailText.text = email;
        if (roleText) roleText.text = role;
    }
    public void OnSignOutClicked()
    {
        SignInManager.ClearSession(clearDeviceId: false);
        SceneManager.LoadScene(0);
        /*if (SignInManager.Instance != null)
        {
            SignInManager.Instance.SignOut(reloadLoginScene: true, clearDeviceId: false);
        }
        else
        {
            // Fallback (shouldn't happen once persistent): clear and go to login
            SignInManager.ClearSession(clearDeviceId: false);
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
        }*/
    }

}
