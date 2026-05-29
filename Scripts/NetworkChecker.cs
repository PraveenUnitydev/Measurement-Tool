using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using VehicleMeasurement;

public class NetworkChecker : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("URL to ping for connectivity check (your catalog server)")]
    public string pingUrl = "http://10.204.12.44:8000/catalog.json";

    [Tooltip("Timeout in seconds")]
    public float timeoutSeconds = 5f;

    [Tooltip("Check on start")]
    public bool checkOnStart = true;/*

    [Header("UI")]
    [Tooltip("Offline message panel (optional)")]
    public GameObject offlineMessagePanel;

    [Tooltip("Status text (optional)")]
    public TMP_Text statusText;

    [Tooltip("Message to show when offline")]
    public string offlineMessage = "⚠ You are offline\n\nSome features may be limited.";
*/
  //  [Tooltip("Message to show when online")]
  //  public string onlineMessage = "✓ Connected";

    [Header("Events")]
    public UnityEngine.Events.UnityEvent OnOnline;
    public UnityEngine.Events.UnityEvent OnOffline;

    [SerializeField] private GameObject loadingPanel;
    // Public properties
    public bool IsOnline { get; private set; }
    public bool IsChecking { get; private set; }

    private void Start()
    {
        if (checkOnStart)
        {
            StartCoroutine(CheckConnectivity());
        }
    }

    /// <summary>
    /// Check if online
    /// </summary>
    public void CheckNow()
    {
        if (!IsChecking)
        {
            StartCoroutine(CheckConnectivity());
        }
    }

    /// <summary>
    /// Main connectivity check
    /// </summary>
    private IEnumerator CheckConnectivity()
    {
        IsChecking = true;

        loadingPanel.SetActive(true);

        Debug.Log($"[NetworkChecker] Checking connectivity to: {pingUrl}");

        // Method 1: Try to reach your server
        using (UnityWebRequest request = UnityWebRequest.Head(pingUrl))
        {
            request.timeout = Mathf.RoundToInt(timeoutSeconds);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                // Successfully reached server
                IsOnline = true;
                Debug.Log("[NetworkChecker] ✓ Online - Server reachable");
                OnConnected();
            }
            else
            {
                // Failed to reach server
                IsOnline = false;
                Debug.LogWarning($"[NetworkChecker] ✗ Offline - {request.error}");
                OnDisconnected();
            }
        }
        loadingPanel.SetActive(false);
        IsChecking = false;
       
    }

    /// <summary>
    /// Called when online
    /// </summary>
    private void OnConnected()
    {
        /* if (offlineMessagePanel != null)
         {
             offlineMessagePanel.SetActive(false);
         }

         if (statusText != null)
         {
             statusText.text = onlineMessage;
             statusText.color = Color.green;
         }*/


       
        OnOnline?.Invoke();
    }

    /// <summary>
    /// Called when offline
    /// </summary>
    private void OnDisconnected()
    {
        /*  if (offlineMessagePanel != null)
          {
              offlineMessagePanel.SetActive(true);
          }

          if (statusText != null)
          {
              statusText.text = offlineMessage;
              statusText.color = new Color(1f, 0.5f, 0f); // Orange
          }*/
        PopupManager.ShowWarning("Can't connect to network some features will not work properly!");
        OnOffline?.Invoke();
    }

    /// <summary>
    /// Hide offline message manually
    /// </summary>
    public void HideOfflineMessage()
    {
       /* if (offlineMessagePanel != null)
        {
            offlineMessagePanel.SetActive(false);
        }*/
    }
}