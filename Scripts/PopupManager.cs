using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace VehicleMeasurement
{
    public class PopupManager : MonoBehaviour
    {
        #region Resources Prefab Config
        // Put prefab at: Assets/Resources/UI/PopupManagerPrefab.prefab
        private const string RESOURCES_PREFAB_PATH = "UI/PopupCanvas";

        private static PopupManager _instance;
        public static PopupManager Instance
        {
            get
            {
                EnsureInstanceLoaded();
                return _instance;
            }
        }

        private static void EnsureInstanceLoaded()
        {
            if (_instance != null) return;

            // Try find in scene first (in case already placed)
            _instance = FindFirstObjectByType<PopupManager>();
            if (_instance != null) return;

            // Load prefab from Resources
            var prefab = Resources.Load<GameObject>(RESOURCES_PREFAB_PATH);
            if (prefab == null)
            {
                Debug.LogError($"[PopupManager] Resources prefab not found at Resources/{RESOURCES_PREFAB_PATH}. " +
                               $"Create prefab and place it under Assets/Resources/{RESOURCES_PREFAB_PATH}.prefab");
                return;
            }

            var go = Instantiate(prefab);
            go.name = prefab.name; // cleaner hierarchy
            _instance = go.GetComponent<PopupManager>();

            if (_instance == null)
            {
                Debug.LogError("[PopupManager] Prefab does not contain PopupManager component!");
                Destroy(go);
                return;
            }

            DontDestroyOnLoad(go);
        }
        #endregion

        #region Manual UI References (Assign in Prefab Inspector)
        [Header("Popup Root")]
        [SerializeField] private GameObject popupPanel;     // Dim background root OR popup root
        [SerializeField] private TMP_Text popupTitle;
        [SerializeField] private TMP_Text popupMessage;

        [Header("Popup Buttons")]
        [SerializeField] private Button okButton;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;

        [Header("Optional Icon (if used)")]
        [SerializeField] private Image popupIcon;

        [Header("Toast Root")]
        [SerializeField] private GameObject toastPanel;
        [SerializeField] private TMP_Text toastText;
        [SerializeField] private CanvasGroup toastCanvasGroup; // optional
        #endregion

        #region Colors
        [Header("Style Colors")]
        [SerializeField] private Color infoColor = new Color(0.2f, 0.6f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0.2f);
        [SerializeField] private Color errorColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color successColor = new Color(0.3f, 0.9f, 0.4f);
        #endregion

        #region State
        private readonly Queue<PopupData> _popupQueue = new Queue<PopupData>();
        private bool _isShowingPopup = false;

        private Action _currentYesCallback;
        private Action _currentNoCallback;

        private Coroutine _autoCloseRoutine;
        private Coroutine _toastRoutine;
        #endregion

        #region Unity Lifecycle
        private void Awake()
        {
            // Singleton protection
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Validate refs + bind listeners
            if (!ValidateUIReferences())
                Debug.LogError("[PopupManager] UI references are missing in prefab. Assign them in Inspector!");

            BindButtonListeners();

            // Hide initially
            HideAllImmediate();
        }
        #endregion

        #region Public Static API
        public static void ShowMessage(string message, float duration = 0f)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Info,
                Title = "Information",
                Message = message,
                Duration = duration
            });
        }

        public static void ShowWarning(string message, float duration = 0f)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Warning,
                Title = "Warning",
                Message = message,
                Duration = duration
            });
        }

        public static void ShowError(string message, float duration = 0f)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Error,
                Title = "Error",
                Message = message,
                Duration = duration
            });
        }

        public static void ShowSuccess(string message, float duration = 0f)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Success,
                Title = "Success",
                Message = message,
                Duration = duration
            });
        }

        public static void ShowConfirm(string message, Action onYes, Action onNo = null)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Confirm,
                Title = "Confirm",
                Message = message,
                OnYes = onYes,
                OnNo = onNo
            });
        }

        public static void ShowConfirm(string title, string message, Action onYes, Action onNo = null)
        {
            Instance?.QueuePopup(new PopupData
            {
                Type = PopupType.Confirm,
                Title = title,
                Message = message,
                OnYes = onYes,
                OnNo = onNo
            });
        }

        public static void Toast(string message, float duration = 2f)
        {
            Instance?.ShowToast(message, duration);
        }

        public static void Close()
        {
            Instance?.HidePopup();
        }

        public static bool IsShowing => Instance != null && Instance._isShowingPopup;
        #endregion

        #region Queue / Popup Flow
        private void QueuePopup(PopupData data)
        {
            if (!ValidateUIReferences())
            {
                Debug.LogError("[PopupManager] Cannot show popup. Missing UI references.");
                return;
            }

            _popupQueue.Enqueue(data);
            if (!_isShowingPopup)
                ShowNextPopup();
        }

        private void ShowNextPopup()
        {
            if (_popupQueue.Count == 0)
            {
                _isShowingPopup = false;
                return;
            }

            _isShowingPopup = true;
            var data = _popupQueue.Dequeue();

            if (_autoCloseRoutine != null)
            {
                StopCoroutine(_autoCloseRoutine);
                _autoCloseRoutine = null;
            }

            popupTitle.text = data.Title;
            popupMessage.text = data.Message;

            ApplyStyle(data.Type);

            bool isConfirm = data.Type == PopupType.Confirm;
            okButton.gameObject.SetActive(!isConfirm);
            yesButton.gameObject.SetActive(isConfirm);
            noButton.gameObject.SetActive(isConfirm);

            _currentYesCallback = data.OnYes;
            _currentNoCallback = data.OnNo;

            popupPanel.SetActive(true);

            if (data.Duration > 0f && !isConfirm)
                _autoCloseRoutine = StartCoroutine(AutoClosePopup(data.Duration));
        }

        private IEnumerator AutoClosePopup(float duration)
        {
            yield return new WaitForSeconds(duration);
            HidePopup();
        }

        private void HidePopup()
        {
            if (_autoCloseRoutine != null)
            {
                StopCoroutine(_autoCloseRoutine);
                _autoCloseRoutine = null;
            }

            popupPanel.SetActive(false);
            _isShowingPopup = false;

            if (_popupQueue.Count > 0)
                ShowNextPopup();
        }
        #endregion

        #region Button Handlers
        private void BindButtonListeners()
        {
            if (okButton != null)
            {
                okButton.onClick.RemoveListener(OnOkClicked);
                okButton.onClick.AddListener(OnOkClicked);
            }

            if (yesButton != null)
            {
                yesButton.onClick.RemoveListener(OnYesClicked);
                yesButton.onClick.AddListener(OnYesClicked);
            }

            if (noButton != null)
            {
                noButton.onClick.RemoveListener(OnNoClicked);
                noButton.onClick.AddListener(OnNoClicked);
            }
        }

        private void OnOkClicked() => HidePopup();

        private void OnYesClicked()
        {
            HidePopup();
            _currentYesCallback?.Invoke();
        }

        private void OnNoClicked()
        {
            HidePopup();
            _currentNoCallback?.Invoke();
        }
        #endregion

        #region Toast
        private void ShowToast(string message, float duration)
        {
            if (!ValidateUIReferences())
            {
                Debug.LogError("[PopupManager] Cannot show toast. Missing UI references.");
                return;
            }

            if (_toastRoutine != null)
                StopCoroutine(_toastRoutine);

            _toastRoutine = StartCoroutine(ShowToastCoroutine(message, duration));
        }

        private IEnumerator ShowToastCoroutine(string message, float duration)
        {
            toastText.text = message;
            toastPanel.SetActive(true);

            var cg = toastCanvasGroup != null ? toastCanvasGroup : toastPanel.GetComponent<CanvasGroup>();

            if (cg != null)
            {
                cg.alpha = 0f;
                float fadeTime = 0.2f;
                float elapsed = 0f;

                while (elapsed < fadeTime)
                {
                    elapsed += Time.deltaTime;
                    cg.alpha = Mathf.Clamp01(elapsed / fadeTime);
                    yield return null;
                }

                cg.alpha = 1f;
            }

            yield return new WaitForSeconds(duration);

            if (cg != null)
            {
                float fadeTime = 0.2f;
                float elapsed = 0f;

                while (elapsed < fadeTime)
                {
                    elapsed += Time.deltaTime;
                    cg.alpha = 1f - Mathf.Clamp01(elapsed / fadeTime);
                    yield return null;
                }
            }

            toastPanel.SetActive(false);
            _toastRoutine = null;
        }
        #endregion

        #region Styling / Validation
        private void ApplyStyle(PopupType type)
        {
            Color color = type switch
            {
                PopupType.Warning => warningColor,
                PopupType.Error => errorColor,
                PopupType.Success => successColor,
                PopupType.Confirm => warningColor,
                _ => infoColor
            };

            popupTitle.color = color;
            if (popupIcon != null) popupIcon.color = color;
        }

        private bool ValidateUIReferences()
        {
            return popupPanel != null
                   && popupTitle != null
                   && popupMessage != null
                   && okButton != null
                   && yesButton != null
                   && noButton != null
                   && toastPanel != null
                   && toastText != null;
        }

        private void HideAllImmediate()
        {
            if (popupPanel != null) popupPanel.SetActive(false);
            if (toastPanel != null) toastPanel.SetActive(false);
            _isShowingPopup = false;
        }
        #endregion
    }

    #region Data Classes
    public enum PopupType
    {
        Info,
        Warning,
        Error,
        Success,
        Confirm
    }

    public class PopupData
    {
        public PopupType Type;
        public string Title;
        public string Message;
        public float Duration;
        public Action OnYes;
        public Action OnNo;
    }
    #endregion
}
