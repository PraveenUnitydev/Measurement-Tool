using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VehicleMeasurement;
using VehicleMeasurement.Storage;

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
    [Tooltip("The old 'Clear Cache' button. It now opens the Storage screen (downloads, sizes, remove, download folder).")]
    public Button _clearCacheButton;
    [Tooltip("Optional: a dedicated 'Storage' / 'Manage downloads' button. Opens the Storage screen.")]
    public Button _storageButton;
    [Tooltip("Optional: a 'Download & update vehicles' button. Opens the batch download / update screen.")]
    public Button _downloadsButton;
    [Tooltip("Rename the old Clear Cache button's label to this (leave empty to keep the label set in the scene).")]
    public string clearCacheButtonLabel = "Storage & Downloads";
    private Animator _loadFromServerToggle;
    private void Awake()
    {
        if (signOutButton != null)
            signOutButton.onClick.AddListener(OnSignOutClicked);
        if (_settingsButton != null) _settingsButton.onClick.AddListener(SettingsPanel);
        if (_loadMeasurementsFromServer != null)
        {
            _loadMeasurementsFromServer.onClick.AddListener(OnLoadFromServerCliked);
            _loadFromServerToggle = _loadMeasurementsFromServer.GetComponent<Animator>();
        }

        // The Storage screen asks before it removes anything, so the button opens it straight away. (Before, it first
        // asked "Do you really want to clear all the cache data?" and the screen only opened after "Yes" - most people
        // pressed No and never found it.)
        if (_clearCacheButton != null)
        {
            _clearCacheButton.onClick.AddListener(OpenStorage);
            if (!string.IsNullOrEmpty(clearCacheButtonLabel))
            {
                var label = _clearCacheButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = clearCacheButtonLabel;
                else
                {
                    var legacy = _clearCacheButton.GetComponentInChildren<Text>(true);
                    if (legacy != null) legacy.text = clearCacheButtonLabel;
                }
            }
        }
        if (_storageButton != null) _storageButton.onClick.AddListener(OpenStorage);
        if (_downloadsButton != null) _downloadsButton.onClick.AddListener(OpenDownloads);
    }

    private void OpenDownloads()
    {
        if (_settingPanel != null && _act) { _act = false; _settingPanel.SetActive(false); }
        VehicleLibraryPanel.Open();
    }

    private void OpenStorage()
    {
        if (_settingPanel != null && _act) { _act = false; _settingPanel.SetActive(false); }
        StoragePanel.Open();
    }
    private bool _act = false;
    private void SettingsPanel()
    {
        _act = !_act;
        if (_settingPanel != null) _settingPanel.SetActive(_act);
        if (ControlledMeasurementStorage.Instance != null)
            UpdateToggleAnimation(ControlledMeasurementStorage.Instance.checkServerFirst);
    }

    private void OnDestroy()
    {
        if (signOutButton != null)
            signOutButton.onClick.RemoveListener(OnSignOutClicked);
        if (_clearCacheButton != null) _clearCacheButton.onClick.RemoveListener(OpenStorage);
        if (_storageButton != null) _storageButton.onClick.RemoveListener(OpenStorage);
        if (_downloadsButton != null) _downloadsButton.onClick.RemoveListener(OpenDownloads);
    }

    private bool isFromServer = true;
    private void OnLoadFromServerCliked()
    {
        isFromServer = !isFromServer;
       UpdateToggleAnimation(isFromServer);
        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.checkServerFirst = isFromServer;
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
        if (_loadFromServerToggle != null) _loadFromServerToggle.SetBool("Invert", markStatus);
    }
    /// <summary>Kept for buttons wired to it in the Inspector: opens the Storage screen.</summary>
    public void ClearCacheClicked()
    {
        // This used to wipe every downloaded vehicle at once and leave Home's list unchanged, so Home kept showing
        // vehicles as downloaded after their files were gone. The Storage screen shows what is on this PC, removes
        // vehicles one at a time (or all), asks first, and keeps Home in step.
        StoragePanel.Open();
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
