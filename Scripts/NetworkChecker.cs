using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using VehicleMeasurement;

/// <summary>
/// Checks once at start (and on request) whether the DAS server can be reached, and tells the rest of the app.
/// - The URL is checked: a retired address left in the scene is replaced by the real server (see DasServer).
/// - A short GET of the health endpoint (some proxies don't answer HEAD properly).
/// - A failed first try is retried once before the "can't connect" message is shown, so a single dropped
///   connection ("Curl error 52: Empty reply from server") no longer shows a false offline warning.
/// - The loading panel is optional.
/// </summary>
public class NetworkChecker : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Health endpoint of the DAS server: https://vrc.mahindra.com/api/das/auth/health. A retired address (10.204.12.44:8000 etc.) is replaced automatically.")]
    public string pingUrl = DasServer.ApiBase + "/auth/health";

    [Tooltip("Timeout in seconds")]
    public float timeoutSeconds = 8f;

    [Tooltip("Check on start")]
    public bool checkOnStart = true;

    [Header("Events")]
    public UnityEngine.Events.UnityEvent OnOnline;
    public UnityEngine.Events.UnityEvent OnOffline;

    [Tooltip("Optional: shown while checking")]
    [SerializeField] private GameObject loadingPanel;

    public bool IsOnline { get; private set; }
    public bool IsChecking { get; private set; }
    /// <summary>True once the first check has finished (online or not).</summary>
    public bool HasChecked { get; private set; }

    public static NetworkChecker Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        pingUrl = DasServer.ResolveEndpoint(pingUrl, "/auth/health", "NetworkChecker.pingUrl");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        if (checkOnStart) CheckNow();
    }

    /// <summary>Check now (ignored while a check is running).</summary>
    public void CheckNow()
    {
        if (!IsChecking) StartCoroutine(CheckConnectivity());
    }

    private IEnumerator CheckConnectivity()
    {
        IsChecking = true;
        if (loadingPanel != null) loadingPanel.SetActive(true);

        bool online = false;
        string error = "";
        for (int attempt = 1; attempt <= 2 && !online; attempt++)
        {
            UnityWebRequest request = null;
            yield return DasHttp.Send(() =>
            {
                var r = UnityWebRequest.Get(pingUrl);
                r.timeout = Mathf.Max(1, Mathf.RoundToInt(timeoutSeconds));
                return r;
            }, r => request = r);
            using (request)
            {
                online = request.result == UnityWebRequest.Result.Success;
                error = request.error;
            }
            if (!online && attempt == 1) yield return new WaitForSecondsRealtime(1f);
        }

        IsOnline = online;
        HasChecked = true;
        if (loadingPanel != null) loadingPanel.SetActive(false);
        IsChecking = false;

        if (online)
        {
            Debug.Log("[NetworkChecker] Online - " + pingUrl);
            if (OnOnline != null) OnOnline.Invoke();
        }
        else
        {
            Debug.LogWarning("[NetworkChecker] Offline - " + pingUrl + ": " + error);
            PopupManager.ShowWarning("Can't reach the DAS server. Downloaded vehicles still work; downloading new ones and syncing measurements won't until the connection is back.");
            if (OnOffline != null) OnOffline.Invoke();
        }
    }

    /// <summary>Kept for existing button bindings.</summary>
    public void HideOfflineMessage() { }
}
