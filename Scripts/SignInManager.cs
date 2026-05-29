using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement; // <-- for SceneManager.LoadScene(1)
using VehicleMeasurement; // ControlledMeasurementStorage & UserAccessLevel

[Serializable]
public class SignInResultEvent : UnityEvent<string, string, string> { } // email, role, displayName

public class SignInManager : MonoBehaviour
{
    [Header("⎯⎯ BACKEND ⎯⎯")]
    [Tooltip("Base server URL without trailing slash, e.g., http://127.0.0.1:8000")]
    public string serverBaseUrl = "http://127.0.0.1:8000";

    [Header("⎯⎯ UI REFERENCES ⎯⎯")]
    public TMP_InputField emailInput;
    public TMP_Text statusText;
    public Button signInButton;
    [Tooltip("Container for the login panel (optional). Will be auto-hidden on auto-login.")]
    public GameObject loginPanel;

    [Header("⎯⎯ OPTIONS ⎯⎯")]
    [Tooltip("If true, when a valid token exists in PlayerPrefs, skip login and load Home (scene index 1).")]
    public bool autoLoginOnStart = true;
    [Tooltip("Timeout for each HTTP call")]
    public float requestTimeout = 10f;
    [Tooltip("How often to poll for admin approval (seconds)")]
    public float pollIntervalSeconds = 3f;
    [Tooltip("Total time to wait for admin approval (seconds)")]
    public float pollTimeoutSeconds = 120f;
    [Tooltip("If true, after manual sign-in success, load Home (scene index 1) automatically.")]
    public bool autoProceedOnSuccess = true;
    [Tooltip("Scene build index to load as Home (default 1).")]
    public int homeSceneBuildIndex = 1;

    [Header("⎯⎯ EVENTS ⎯⎯")]
    public UnityEvent OnSignInStarted;
    public SignInResultEvent OnSignInSucceeded;
    public UnityEvent<string> OnSignInFailed;

    public Button _exitButton;

    // ===== DTOs (Serializable so JsonUtility works) =====
    [Serializable]
    private class AuthRequestDto { public string email; public string deviceId; }

    [Serializable]
    private class AuthPollDto { public string requestId; }

    [Serializable]
    private class RequestResponse
    {
        public bool approved;      // when already approved (token ready)
        public bool requested;     // when pending created
        public string requestId;   // for polling
        public string token;       // on approved
        public string email;
        public string displayName;
        public string role;
    }

    [Serializable]
    private class PollResponse
    {
        public bool approved;
        public bool denied;
        public string token;
        public string email;
        public string displayName;
        public string role;
    }


    private void Awake()
    {
        EnsureDeviceId();

        if (signInButton != null)
            signInButton.onClick.AddListener(HandleSignInClicked);
        if (_exitButton != null)
            _exitButton.onClick.AddListener(OnExitClicked);

    }
    private void OnExitClicked()
    {
        Application.Quit();
    }


    private void Start()
    {
        if (autoLoginOnStart)
        {
            var token = PlayerPrefs.GetString("session.token", "");
            if (!string.IsNullOrEmpty(token))
            {
                // Validate with the server BEFORE proceeding.
                StartCoroutine(StartWithValidation());
                return;
            }
        }
        // No token → show login UI
    }



    private void OnDestroy()
    {
        if (signInButton != null)
            signInButton.onClick.RemoveListener(HandleSignInClicked);
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log($"[SignIn] {msg}");
    }

    private void HandleSignInClicked()
    {
        var email = (emailInput != null ? emailInput.text : "").Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
        {
            SetStatus("Please enter a valid email.");
            return;
        }
        StartCoroutine(SignInFlow(email));
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

    // ===== Sign-in flow (request -> poll -> approved) =====
    private IEnumerator SignInFlow(string email)
    {
        OnSignInStarted?.Invoke();
        SetStatus("Requesting access…");

        string url = $"{serverBaseUrl}/api/auth/request";
        var deviceId = EnsureDeviceId();

        var reqDto = new AuthRequestDto { email = email, deviceId = deviceId };
        var payload = JsonUtility.ToJson(reqDto);
        byte[] body = Encoding.UTF8.GetBytes(payload);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = Mathf.CeilToInt(requestTimeout);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                var err = $"Network error: {req.error}";
                SetStatus(err);
                OnSignInFailed?.Invoke(err);
                yield break;
            }

            RequestResponse resp = null;
            try { resp = JsonUtility.FromJson<RequestResponse>(req.downloadHandler.text); }
            catch (Exception e)
            {
                var err = $"Invalid response: {e.Message}";
                SetStatus(err);
                OnSignInFailed?.Invoke(err);
                yield break;
            }

            // Immediate approval (already approved + device ok)
            if (resp != null && resp.approved && !string.IsNullOrEmpty(resp.token))
            {
                yield return OnApproved(resp.token, resp.email ?? email, resp.role ?? "User", resp.displayName ?? "");
                yield break;
            }

            // Otherwise, wait for admin approval
            if (resp == null || !resp.requested || string.IsNullOrEmpty(resp.requestId))
            {
                SetStatus("Request failed. Please try again.");
                OnSignInFailed?.Invoke("Request failed");
                yield break;
            }

            SetStatus("Waiting for admin approval…");
            ShowRequestSentPanel();
            yield return StartCoroutine(PollForApproval(resp.requestId, email));
        }
    }
    [SerializeField] private GameObject _requestSentPanel;
    private void ShowRequestSentPanel()
    {
        _requestSentPanel.SetActive(true);
    }

    [Serializable] private class ValidateResponse { public bool valid; public string email; public string role; }

    private IEnumerator ValidateExistingSession(Action<bool> cb)
    {
        var token = PlayerPrefs.GetString("session.token", "");
        if (string.IsNullOrEmpty(token)) { cb?.Invoke(false); yield break; }

        string url = $"{serverBaseUrl}/api/auth/validate";
        using (var req = UnityWebRequest.Get(url))
        {
            AttachAuthHeader(req); // sets Authorization: Bearer <token>
            req.timeout = Mathf.CeilToInt(requestTimeout);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SignIn] Validate failed: {req.responseCode} {req.error} body={req.downloadHandler.text}");
                cb?.Invoke(false);
                yield break;
            }

            bool ok = false;
            try
            {
                var resp = JsonUtility.FromJson<ValidateResponse>(req.downloadHandler.text);
                ok = (resp != null && resp.valid);
            }
            catch { ok = false; }

            cb?.Invoke(ok);
        }
    }

    private IEnumerator StartWithValidation()
    {
        bool ok = false;
        yield return ValidateExistingSession(v => ok = v);

        if (!ok)
        {
            ClearSession();
            if (loginPanel != null) loginPanel.SetActive(true);
            SetStatus("Session expired or access revoked. Please sign in.");
            yield break;
        }

        // … proceed to Home as before …
        LoadHome();
    }


    private IEnumerator PollForApproval(string requestId, string email)
    {
        var start = Time.unscaledTime;

        while (Time.unscaledTime - start < pollTimeoutSeconds)
        {
            string url = $"{serverBaseUrl}/api/auth/poll";
            var pollDto = new AuthPollDto { requestId = requestId };
            var payload = JsonUtility.ToJson(pollDto);
            byte[] body = Encoding.UTF8.GetBytes(payload);

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(body);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = Mathf.CeilToInt(requestTimeout);

                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    PollResponse poll = null;
                    try { poll = JsonUtility.FromJson<PollResponse>(req.downloadHandler.text); } catch { }

                    if (poll != null)
                    {
                        if (poll.approved && !string.IsNullOrEmpty(poll.token))
                        {
                            yield return OnApproved(
                                poll.token,
                                poll.email ?? email,
                                poll.role ?? "User",
                                poll.displayName ?? ""
                            );
                            yield break;
                        }
                        if (poll.denied)
                        {
                            SetStatus("Access denied by admin.");
                            OnSignInFailed?.Invoke("Denied");
                            yield break;
                        }
                    }
                }

                // Not approved yet; wait before next poll
                yield return new WaitForSecondsRealtime(Mathf.Max(1f, pollIntervalSeconds));
            }
        }

        SetStatus("Approval timed out. Please try again or contact admin.");
        OnSignInFailed?.Invoke("Timeout");
    }

    private IEnumerator OnApproved(string token, string email, string role, string displayName)
    {
        // Persist session
        PlayerPrefs.SetString("session.token", token);
        PlayerPrefs.SetString("session.email", email);
        PlayerPrefs.SetString("session.displayName", displayName ?? "");
        PlayerPrefs.SetString("session.role", role ?? "User");
        PlayerPrefs.Save();

        // Map role to your access level enum and apply
        var level = MapRoleToAccessLevel(role);
        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.SetAccessLevel(level);

        // Hide login panel if present
        if (loginPanel != null) loginPanel.SetActive(false);

        var display = string.IsNullOrEmpty(displayName) ? email : displayName;
        SetStatus($"Welcome {display} ({role})");
        OnSignInSucceeded?.Invoke(email, role, display);

        if (autoProceedOnSuccess)
            LoadHome();

        yield break;
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

        if (clearDeviceId)
            PlayerPrefs.DeleteKey("device.id");

        PlayerPrefs.Save();
    }

    // ===== Instance: Sign out flow =====
    public void SignOut(bool reloadLoginScene = true, bool clearDeviceId = false)
    {
        // 1) Clear saved session
        ClearSession(clearDeviceId);

        // 2) If you keep a reference to a storage/access controller, reset it
        if (ControlledMeasurementStorage.Instance != null)
            ControlledMeasurementStorage.Instance.SetAccessLevel(UserAccessLevel.Viewer);

        // 3) Show the login panel again (if you have one wired in)
        if (loginPanel != null)
            loginPanel.SetActive(true);

        // 4) Option A: reload the current scene (your login scene)
        //    Option B: load a specific login scene by index/name
        if (reloadLoginScene)
        {
            // reload current scene (index)
            SceneManager.LoadScene(0);
        }
        else
        {
            // Stay on the same screen; status text can prompt for email again
            SetStatus("Signed out. Please enter your email to sign in.");
        }
    }

    private static UserAccessLevel MapRoleToAccessLevel(string role)
    {
        switch ((role ?? "").Trim().ToLowerInvariant())
        {
            case "viewer": return UserAccessLevel.Viewer;
            case "admin": return UserAccessLevel.Admin;
            default: return UserAccessLevel.User;
        }
    }

    // ===== Helper to attach the token to future server calls =====
    public static void AttachAuthHeader(UnityWebRequest req)
    {
        var token = PlayerPrefs.GetString("session.token", "");
        if (!string.IsNullOrEmpty(token))
            req.SetRequestHeader("Authorization", $"Bearer {token}");
    }
}