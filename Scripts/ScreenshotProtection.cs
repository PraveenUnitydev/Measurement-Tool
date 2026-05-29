using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Prevents screenshots and screen recording on Windows 10+ by marking the window as protected content.
/// Attach this script to any GameObject in your first/startup scene.
/// </summary>
public class ScreenshotProtection : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Show debug messages in console")]
    public bool showDebugLogs = true;

    [Tooltip("Number of retry attempts if protection fails initially")]
    public int maxRetries = 5;

    [Tooltip("Delay in seconds before first attempt")]
    public float initialDelay = 0.5f;

    // Windows API imports
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    // Constants
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_NONE = 0x00000000;

    void Awake()
    {
        // Singleton pattern - ensure only one instance exists
        if (FindObjectsOfType<ScreenshotProtection>().Length > 1)
        {
            if (showDebugLogs)
            {
                Debug.Log("Screenshot protection already exists, destroying duplicate.");
            }
            Destroy(gameObject);
            return;
        }

        // Keep this GameObject alive across all scenes
        DontDestroyOnLoad(gameObject);

        if (showDebugLogs)
        {
            Debug.Log("Screenshot protection GameObject will persist across scenes.");
        }
    }

    void Start()
    {
#if UNITY_STANDALONE_WIN
        StartCoroutine(ApplyProtectionWithRetry());
#else
        if (showDebugLogs)
        {
            Debug.Log("Screenshot protection is only available on Windows builds.");
        }
#endif
    }

    /// <summary>
    /// Attempts to apply screenshot protection with retry logic
    /// </summary>
    IEnumerator ApplyProtectionWithRetry()
    {
        // Initial delay to ensure window is fully initialized
        yield return new WaitForSeconds(initialDelay);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            IntPtr hwnd = GetActiveWindow();

            if (hwnd != IntPtr.Zero)
            {
                bool success = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);

                if (success)
                {
                    if (showDebugLogs)
                    {
                        Debug.Log($"✓ Screenshot protection enabled successfully (attempt {attempt})");
                    }
                    yield break; // Success, exit coroutine
                }
                else
                {
                    if (showDebugLogs)
                    {
                        Debug.LogWarning($"Failed to apply protection (attempt {attempt}/{maxRetries})");
                    }
                }
            }
            else
            {
                if (showDebugLogs)
                {
                    Debug.LogWarning($"Window handle not found (attempt {attempt}/{maxRetries})");
                }
            }

            // Wait before next retry
            if (attempt < maxRetries)
            {
                yield return new WaitForSeconds(0.2f);
            }
        }

        // All attempts failed
        Debug.LogError("Screenshot protection could not be enabled. App will continue without protection.");
    }

    /// <summary>
    /// Disable protection (useful for debugging or if you need to allow screenshots temporarily)
    /// </summary>
    public void DisableProtection()
    {
#if UNITY_STANDALONE_WIN
        IntPtr hwnd = GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(hwnd, WDA_NONE);
            if (showDebugLogs)
            {
                Debug.Log("Screenshot protection disabled");
            }
        }
#endif
    }

    /// <summary>
    /// Re-enable protection
    /// </summary>
    public void EnableProtection()
    {
#if UNITY_STANDALONE_WIN
        IntPtr hwnd = GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            if (showDebugLogs)
            {
                Debug.Log("Screenshot protection enabled");
            }
        }
#endif
    }

    void OnApplicationQuit()
    {
        // Clean up - remove protection before closing
        DisableProtection();
    }
}
