using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using VehicleMeasurement; // ControlledMeasurementStorage & UserAccessLevel

[Serializable]
public class SignInResultEvent : UnityEvent<string, string, string> { } // email, role, displayName

/// <summary>
/// DAS sign-in with the person's own Microsoft account, tied to their VRSP account.
///
/// Flow:
///  1. POST {serverBaseUrl}/auth/start  -> pairing + a short code (e.g. QSRA-F8NK)
///  2. Opens the browser for Microsoft sign-in and shows the code in the app
///  3. The browser shows the same code; the person confirms it there
///  4. POST {serverBaseUrl}/auth/poll until the server returns the session token
///
/// Access is granted/removed by a VRSP admin (VRSP dashboard > DAS tab).
/// One active DAS session per person: signing in on another PC ends this one.
///
/// serverBaseUrl must include /api/das, e.g. https://vrc.mahindra.com/api/das
/// </summary>
public class SignInManager : MonoBehaviour
{
    [Header("⎯⎯ BACKEND ⎯⎯")]
    [Tooltip("DAS API base URL without trailing slash, e.g. https://vrc.mahindra.com/api/das")]
    public string serverBaseUrl = "https://vrc.mahindra.com/api/das";

    [Header("⎯⎯ UI REFERENCES ⎯⎯")]
    [Tooltip("No longer used - sign-in is with Microsoft. Hidden automatically if assigned.")]
    public TMP_InputField emailInput;
    public TMP_Text statusText;
    [Tooltip("'Sign in with Microsoft' button")]
    public Button signInButton;
    [Tooltip("Container for the login panel (optional). Will be auto-hidden on auto-login.")]
    public GameObject loginPanel;

    [Header("⎯⎯ BROWSER SIGN-IN PANEL (optional) ⎯⎯")]
    [Tooltip("Shown while the person completes sign-in in the browser")]
    [SerializeField] private GameObject _requestSentPanel;
    [Tooltip("Shows the code the person must match in the browser")]
    public TMP_Text codeText;
    [Tooltip("Re-opens the sign-in page if the browser tab was closed")]
    public Button openBrowserAgainButton;
    [Tooltip("Cancels the sign-in in progress")]
    public Button cancelSignInButton;

    [Header("⎯⎯ OPTIONS ⎯⎯")]
    [Tooltip("If true, when a saved session exists, validate it and go straight to Home.")]
    public bool autoLoginOnStart = true;
    [Tooltip("Timeout for each HTTP call")]
    public float requestTimeout = 10f;
    [Tooltip("If true, after sign-in success, load Home automatically.")]
    public bool autoProceedOnSuccess = true;
    [Tooltip("Scene build index to load as Home (default 1).")]
    public int homeSceneBuildIndex = 1;

    [Header("⎯⎯ EVENTS ⎯⎯")]
    public UnityEvent OnSignInStarted;
    public SignInResultEvent OnSignInSucceeded;
    public UnityEvent<string> OnSignInFailed;

    public Button _exitButton;

    private const string EndedReasonKey = "session.endedReason";
    private Coroutine _signInRoutine;
    private string _loginUrl;

    // ===== DTOs (Serializable so JsonUtility works) =====
    [Serializable] private class StartRequestDto { public string deviceId; public string deviceName; }
    [Serializable] private class StartResponse { public string pairingId; public string pollToken; public string userCode; public string loginUrl; public float expiresInSeconds; public float pollIntervalSeconds; public string error; }
    [Serializable] private class PollRequestDto { public string pairingId; public string pollToken; }
    [Serializable] private class PollResponse { public string status; public string token; public string email; public string displayName; public string role; public string message; public string error; }
    [Serializable] private class ValidateResponse { public bool valid; public string email; public string role; public string displayName; public string error; public string code; }
    [Serializable] private class ErrorResponse { public string error; public string message; public string code; }

    private void Awake()
    {
        // A scene may still hold a retired server address: use the real one
        serverBaseUrl = DasServer.ResolveApiBase(serverBaseUrl, "SignInManager.serverBaseUrl");
        EnsureDeviceId();
        if (emailInput != null) emailInput.gameObject.SetActive(false);
        if (_requestSentPanel != null) _requestSentPanel.SetActive(false);

        if (signInButton != null) signInButton.onClick.AddListener(HandleSignInClicked);
        if (openBrowserAgainButton != null) openBrowserAgainButton.onClick.AddListener(OpenBrowserAgain);
        if (cancelSignInButton != null) cancelSignInButton.onClick.AddListener(CancelSignIn);
        if (_exitButton != null) _exitButton.onClick.AddListener(OnExitClicked);
    }

    private void OnExitClicked() => Application.Quit();

    private void Start()
    {
        // Explain why the person is back at the login screen (access removed,
        // signed in on another PC, ...) - set by HandleUnauthorized()
        var ended = PlayerPrefs.GetString(EndedReasonKey, "");
        if (!string.IsNullOrEmpty(ended))
        {
            PlayerPrefs.DeleteKey(EndedReasonKey);
            PlayerPrefs.Save();
            SetStatus(ended);
            return;
        }

        if (autoLoginOnStart && !string.IsNullOrEmpty(PlayerPrefs.GetString("session.token", "")))
            StartCoroutine(StartWithValidation());
    }

    private void OnDestroy()
    {
        if (signInButton != null) signInButton.onClick.RemoveListener(HandleSignInClicked);
        if (openBrowserAgainButton != null) openBrowserAgainButton.onClick.RemoveListener(OpenBrowserAgain);
        if (cancelSignInButton != null) cancelSignInButton.onClick.RemoveListener(CancelSignIn);
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log($"[SignIn] {msg}");
    }

    private void HandleSignInClicked()
    {
        if (_signInRoutine != null) return; // already signing in
        _signInRoutine = StartCoroutine(SignInFlow());
    }

    private void OpenBrowserAgain()
    {
        if (!string.IsNullOrEmpty(_loginUrl)) Application.OpenURL(_loginUrl);
    }

    public void CancelSignIn()
    {
        if (_signInRoutine != null) { StopCoroutine(_signInRoutine); _signInRoutine = null; }
        _loginUrl = null;
        ShowBrowserPanel(false, null);
        if (signInButton != null) signInButton.interactable = true;
        SetStatus("Sign-in cancelled.");
    }

    // ===== Device Id (stored once) =====
    private string EnsureDeviceId()
    {
        var id = PlayerPrefs.GetString("device.id", "");
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
            PlayerPrefs.SetString("device.id", id);
            PlayerPrefs.Save();
        }
        return id;
    }

    private void ShowBrowserPanel(bool show, string code)
    {
        if (_requestSentPanel != null) _requestSentPanel.SetActive(show);
        if (codeText != null) codeText.text = show ? code : "";
    }

    // ===== Sign-in flow: start -> browser -> poll =====
    private IEnumerator SignInFlow()
    {
        OnSignInStarted?.Invoke();
        if (signInButton != null) signInButton.interactable = false;
        SetStatus("Starting sign-in…");

        StartResponse start = null;
        var startBody = JsonUtility.ToJson(new StartRequestDto { deviceId = EnsureDeviceId(), deviceName = SystemInfo.deviceName });
        {
            UnityWebRequest req = null;
            yield return DasHttp.Send(() =>
            {
                var __req = PostJson($"{serverBaseUrl}/auth/start", startBody);
                return __req;
            }, r => req = r);
            using (req)
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Fail(ErrorFrom(req, "Can't reach the DAS server. Check your network and try again."));
                    yield break;
                }
                try { start = JsonUtility.FromJson<StartResponse>(req.downloadHandler.text); } catch { }
            }
        }
        if (start == null || string.IsNullOrEmpty(start.pairingId) || string.IsNullOrEmpty(start.loginUrl))
        {
            Fail("Sign-in could not start. Please try again.");
            yield break;
        }

        _loginUrl = start.loginUrl;
        ShowBrowserPanel(true, start.userCode);
        SetStatus($"Finish signing in with your Microsoft account in the browser. Confirm it shows the code {start.userCode}.");
        Application.OpenURL(start.loginUrl);

        float interval = Mathf.Max(2f, start.pollIntervalSeconds > 0 ? start.pollIntervalSeconds : 4f);
        float deadline = Time.unscaledTime + (start.expiresInSeconds > 0 ? start.expiresInSeconds : 600f);
        var pollBody = JsonUtility.ToJson(new PollRequestDto { pairingId = start.pairingId, pollToken = start.pollToken });

        while (Time.unscaledTime < deadline)
        {
            yield return new WaitForSecondsRealtime(interval);

            {
                UnityWebRequest req = null;
                yield return DasHttp.Send(() =>
                {
                    var __req = PostJson($"{serverBaseUrl}/auth/poll", pollBody);
                    return __req;
                }, r => req = r);
                using (req)
                {

                    // Network blips while waiting are expected - keep waiting
                    if (req.result == UnityWebRequest.Result.ConnectionError) continue;

                    PollResponse poll = null;
                    try { poll = JsonUtility.FromJson<PollResponse>(req.downloadHandler.text); } catch { }
                    var status = poll?.status ?? "";

                    if (status == "pending") continue;
                    if (status == "approved" && !string.IsNullOrEmpty(poll.token))
                    {
                        _signInRoutine = null;
                        ShowBrowserPanel(false, null);
                        yield return OnApproved(poll.token, poll.email, poll.role ?? "User", poll.displayName ?? "");
                        yield break;
                    }
                    if (status == "denied" || status == "expired")
                    {
                        Fail(poll.message ?? poll.error ?? "Sign-in did not complete. Please try again.");
                        yield break;
                    }
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        Fail(ErrorFrom(req, "Sign-in failed. Please try again."));
                        yield break;
                    }
                }
            }
        }
        Fail("Sign-in timed out. Please try again.");
    }

    private void Fail(string message)
    {
        _signInRoutine = null;
        _loginUrl = null;
        ShowBrowserPanel(false, null);
        if (signInButton != null) signInButton.interactable = true;
        SetStatus(message);
        OnSignInFailed?.Invoke(message);
    }

    private UnityWebRequest PostJson(string url, string json)
    {
        var req = new UnityWebRequest(url, "POST")
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.CeilToInt(requestTimeout),
        };
        req.SetRequestHeader("Content-Type", "application/json");
        return req;
    }

    private static string ErrorFrom(UnityWebRequest req, string fallback)
    {
        try
        {
            var e = JsonUtility.FromJson<ErrorResponse>(req.downloadHandler?.text ?? "");
            if (!string.IsNullOrEmpty(e?.error)) return e.error;
            if (!string.IsNullOrEmpty(e?.message)) return e.message;
        }
        catch { }
        return fallback;
    }

    // ===== Saved session on startup =====
    private IEnumerator StartWithValidation()
    {
        var token = PlayerPrefs.GetString("session.token", "");
        {
            UnityWebRequest req = null;
            yield return DasHttp.Send(() =>
            {
                var __req = UnityWebRequest.Get($"{serverBaseUrl}/auth/validate");
                __req.SetRequestHeader("Authorization", $"Bearer {token}");
                __req.timeout = Mathf.CeilToInt(requestTimeout);
                return __req;
            }, r => req = r);
            using (req)
            {

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    // Keep the session - the server may just be unreachable right now
                    if (loginPanel != null) loginPanel.SetActive(true);
                    SetStatus("Can't reach the DAS server. Check your network, then restart the app or sign in again.");
                    yield break;
                }

                ValidateResponse resp = null;
                try { resp = JsonUtility.FromJson<ValidateResponse>(req.downloadHandler.text); } catch { }

                if (req.result != UnityWebRequest.Result.Success || resp == null || !resp.valid)
                {
                    ClearSession();
                    if (loginPanel != null) loginPanel.SetActive(true);
                    SetStatus(!string.IsNullOrEmpty(resp?.error) ? resp.error : "Your session has ended. Please sign in.");
                    yield break;
                }

                // Apply the CURRENT role from the server - an admin may have changed it
                ApplySession(token, resp.email, resp.role, resp.displayName);
            }
        }
        LoadHome();
    }

    private IEnumerator OnApproved(string token, string email, string role, string displayName)
    {
        ApplySession(token, email, role, displayName);
        if (loginPanel != null) loginPanel.SetActive(false);
        if (signInButton != null) signInButton.interactable = true;

        var display = string.IsNullOrEmpty(displayName) ? email : displayName;
        SetStatus($"Welcome {display} ({role})");
        OnSignInSucceeded?.Invoke(email, role, display);

        if (autoProceedOnSuccess) LoadHome();
        yield break;
    }

    private static void ApplySession(string token, string email, string role, string displayName)
    {
        PlayerPrefs.SetString("session.token", token);
        PlayerPrefs.SetString("session.email", email ?? "");
        PlayerPrefs.SetString("session.displayName", displayName ?? "");
        PlayerPrefs.SetString("session.role", role ?? "User");
        PlayerPrefs.Save();

        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.SetAccessLevel(MapRoleToAccessLevel(role));
    }

    private void LoadHome()
    {
        SceneManager.LoadScene("HomeScene");
    }

    // ===== Public: Logout button handler =====
    public void OnLogoutButtonClicked()
    {
        SignOut(reloadLoginScene: true, clearDeviceId: false);
    }

    // ===== Public/Static: clear session keys (can be called from anywhere) =====
    public static void ClearSession(bool clearDeviceId = false)
    {
        PlayerPrefs.DeleteKey("session.token");
        PlayerPrefs.DeleteKey("session.email");
        PlayerPrefs.DeleteKey("session.displayName");
        PlayerPrefs.DeleteKey("session.role");
        if (clearDeviceId) PlayerPrefs.DeleteKey("device.id");
        PlayerPrefs.Save();
    }

    // ===== Instance: Sign out flow =====
    public void SignOut(bool reloadLoginScene = true, bool clearDeviceId = false)
    {
        ClearSession(clearDeviceId);
        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.SetAccessLevel(UserAccessLevel.Viewer);
        if (loginPanel != null) loginPanel.SetActive(true);

        if (reloadLoginScene) SceneManager.LoadScene(0);
        else SetStatus("Signed out.");
    }

    /// <summary>
    /// Call after any authenticated request. If the server says the session is
    /// no longer valid (access removed by an admin, VRSP account deactivated,
    /// or signed in on another PC), signs out and returns to the login screen
    /// with the server's explanation. Returns true if it did.
    /// </summary>
    public static bool HandleUnauthorized(UnityWebRequest req)
    {
        if (req == null || req.responseCode != 401) return false;
        var reason = ErrorFrom(req, "Your DAS session has ended. Please sign in again.");
        ClearSession();
        PlayerPrefs.SetString(EndedReasonKey, reason);
        PlayerPrefs.Save();
        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.SetAccessLevel(UserAccessLevel.Viewer);
        Debug.LogWarning($"[SignIn] Session ended by server: {reason}");
        SceneManager.LoadScene(0);
        return true;
    }

    public static UserAccessLevel MapRoleToAccessLevel(string role)
    {
        switch ((role ?? "").Trim().ToLowerInvariant())
        {
            case "viewer": return UserAccessLevel.Viewer;
            case "admin": return UserAccessLevel.Admin;
            default: return UserAccessLevel.User;
        }
    }

    // ===== Helper to attach the token to server calls =====
    public static void AttachAuthHeader(UnityWebRequest req)
    {
        var token = PlayerPrefs.GetString("session.token", "");
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", $"Bearer {token}");
    }
}
