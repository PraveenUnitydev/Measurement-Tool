using System.Collections;
using UnityEngine;

public class GuideController : MonoBehaviour
{
    [Header("Guides Setup")]
    [SerializeField] private GameObject[] guides;
    [SerializeField] private GameObject[] _dots;
    [SerializeField] private float guideDurationSeconds = 3.5f;
    [SerializeField] private bool playOnStart = false;  // optional
    [SerializeField] private GameObject _BGPanel;

    [Header("Player Prefs")]
    [Tooltip("Unique key used to track whether guides have been shown or skipped.")]
    [SerializeField] private string playerPrefsKey = "GuidesShown_v1";
    [Tooltip("Optional suffix if your app has multiple local profiles on one device.")]
    [SerializeField] private string optionalUserIdSuffix = ""; // e.g., "user123"

    private static GuideController _instance;
    private Coroutine _playRoutine;
    private bool _isInitialized;

    // ---- Singleton Accessor ----
    public static GuideController Instance
    {
        get
        {
            if (_instance == null)
            {
                // Try to find existing in scene
                _instance = FindObjectOfType<GuideController>();

                // If not found, create a new one
                if (_instance == null)
                {
                    var go = new GameObject("[GuideController]");
                    _instance = go.AddComponent<GuideController>();
                }

                _instance.EnsureInitialized();
            }
            return _instance;
        }
    }

    // Call this from anywhere: GuideController.ShowGuides();
    // Plays only if not already shown/skipped previously.
    public static void ShowGuides()
    {
        Instance.PlayOnceIfNeeded();
    }

    // Optional: Stop and hide any guide (does NOT set the 'shown' flag)
    public static void StopGuides()
    {
        if (_instance != null)
            _instance.Stop();
    }

    // ---- Lifecycle ----
    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        DontDestroyOnLoad(gameObject);

        // Turn everything off initially
        if (guides != null)
        {
            foreach (var g in guides)
                if (g) g.SetActive(false);
        }

        if (_BGPanel != null)
            _BGPanel.SetActive(false);
    }

    private void Start()
    {
        if (playOnStart)
            PlayOnceIfNeeded();
    }

    private void OnDisable()
    {
        if (_instance == this)
            Stop();
    }

    // ---- Public Controls ----

    /// <summary>
    /// Play only if the user has not seen/skipped the guides before.
    /// </summary>
    public void PlayOnceIfNeeded()
    {
        if (HasShownGuides())
            return;

        PlayInternal();
    }

    /// <summary>
    /// Force play regardless of PlayerPrefs (useful for testing).
    /// </summary>
    public void PlayForce()
    {
        PlayInternal(force: true);
    }

    /// <summary>
    /// Skip button handler: immediately stop, hide, and mark as shown so it won't appear again.
    /// Wire this to your UI Button OnClick.
    /// </summary>
    public void Skip()
    {
        // Stop and hide everything
        Stop();

        // Mark as shown so it won't show again
        MarkGuidesAsShown();
    }

    /// <summary>
    /// Stop playback and hide all guides (does NOT mark as shown).
    /// </summary>
    public void Stop()
    {
        if (_playRoutine != null)
        {
            StopCoroutine(_playRoutine);
            _playRoutine = null;
        }

        if (guides != null)
        {
            foreach (var g in guides)
                if (g) g.SetActive(false);
        }

        if (_BGPanel != null)
            _BGPanel.SetActive(false);
    }

    // ---- Core Playback ----
    private void PlayInternal(bool force = false)
    {
        if (!force && HasShownGuides())
            return;

        if (_BGPanel != null)
            _BGPanel.SetActive(true);

        // Avoid overlap
        if (_playRoutine != null) StopCoroutine(_playRoutine);
        _playRoutine = StartCoroutine(PlayGuidesOnceAndMarkSeen());
    }

    private IEnumerator PlayGuidesOnceAndMarkSeen()
    {
        yield return PlayGuidesOnce();

        // After successful (non-skipped) completion, mark as shown
        // Note: If user tapped Skip(), we already set the flag and stopped.
        if (!HasShownGuides())
            MarkGuidesAsShown();
    }

    private IEnumerator PlayGuidesOnce()
    {
        if (guides == null || guides.Length == 0)
        {
            _playRoutine = null;
            if (_BGPanel != null) _BGPanel.SetActive(false);
            yield break;
        }

        // Make sure all are off
        foreach (var g in guides)
            if (g) g.SetActive(false);

        for (int i = 0; i < guides.Length; i++)
        {
            var current = guides[i];
            if (current == null) continue;

            current.SetActive(true);

            var cDot = _dots[i];
            if (cDot == null) continue;
            cDot.SetActive(true);
            yield return new WaitForSeconds(guideDurationSeconds);
            current.SetActive(false);
            cDot.SetActive(false);
        }

        _playRoutine = null;
        if (_BGPanel != null)
            _BGPanel.SetActive(false);
    }

    // ---- PlayerPrefs Helpers ----
    private string EffectivePrefsKey
    {
        get
        {
            return string.IsNullOrEmpty(optionalUserIdSuffix)
                ? playerPrefsKey
                : $"{playerPrefsKey}_{optionalUserIdSuffix}";
        }
    }

    private bool HasShownGuides()
    {
        return PlayerPrefs.GetInt(EffectivePrefsKey, 0) == 1;
    }

    private void MarkGuidesAsShown()
    {
        PlayerPrefs.SetInt(EffectivePrefsKey, 1);
        PlayerPrefs.Save();
    }

    // ---- Optional: QA Utilities ----
    [ContextMenu("DEBUG: Reset Guides Shown Flag")]
    public void Debug_ResetShownFlag()
    {
        PlayerPrefs.DeleteKey(EffectivePrefsKey);
        PlayerPrefs.Save();
        Debug.Log($"[GuideController] Deleted PlayerPrefs key: {EffectivePrefsKey}");
    }
}
