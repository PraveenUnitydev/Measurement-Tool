//using System.Collections;
//using System.IO;
//using TMPro;
//using UnityEditor.PixyzPlugin4Unity.LODs;
//using UnityEngine;
//// Correct Asset Transformer Toolkit namespaces
//using Unity.AssetTransformer;
//using UnityEngine.UI;

//public class CATIAFileImporter : MonoBehaviour
//{
//    [Header("UI References")]
//    public Button importButton;
//    public GameObject loadingPanel;
//    public Slider progressBar;
//    public TextMeshProUGUI statusText;
//    public TextMeshProUGUI progressText;
//    public Button cancelButton;

//    [Header("Error UI")]
//    public GameObject errorPanel;
//    public TextMeshProUGUI errorMessageText;
//    public Button errorCloseButton;

//    [Header("Scene References")]
//    public GameObject holder3D;

//    [Header("Import Settings")]
//    public bool orientToUnity = true;
//    public float scaleFactor = 0.001f; // CATIA mm to Unity m
//    public bool clearPreviousImports = true;

//    [Header("Advanced Settings")]
//    public bool importHiddenObjects = false;
//    public bool importLines = false;
//    public bool importPoints = false;
//    public bool importPatchBorders = false;
//    public bool importFreeEdges = false;
//    public int maxPolygonCount = 1000000; // Limit for performance

//    [Header("Tessellation Quality")]
//    [Range(0.01f, 10f)]
//    public float maxSag = 0.1f; // Lower = higher quality (in mm)
//    [Range(5f, 45f)]
//    public float maxAngle = 15f; // Lower = smoother curves (in degrees)

//    [Header("LOD Settings")]
//    public bool generateLODs = false;
//    public int lodLevels = 3;

//    private bool isImporting = false;
//    private bool cancelRequested = false;
//    private GameObject currentImportedObject;

//    private void Start()
//    {
//        InitializeUI();
//        ValidateReferences();
//    }

//    private void InitializeUI()
//    {
//        if (importButton != null)
//            importButton.onClick.AddListener(OnImportButtonClicked);

//        if (cancelButton != null)
//            cancelButton.onClick.AddListener(OnCancelButtonClicked);

//        if (errorCloseButton != null)
//            errorCloseButton.onClick.AddListener(CloseErrorPanel);

//        // Hide panels initially
//        if (loadingPanel != null)
//            loadingPanel.SetActive(false);

//        if (errorPanel != null)
//            errorPanel.SetActive(false);
//    }

//    private void ValidateReferences()
//    {
//        if (holder3D == null)
//        {
//            Debug.LogError("3DHolder GameObject reference is missing!");
//            ShowError("Setup Error", "3DHolder reference is not assigned!");
//        }

//        if (importButton == null)
//        {
//            Debug.LogError("Import Button reference is missing!");
//        }
//    }

//    private void OnImportButtonClicked()
//    {
//        if (isImporting)
//        {
//            ShowError("Import in Progress", "Please wait for the current import to complete.");
//            return;
//        }

//        OpenFileDialog();
//    }

//    private void OnCancelButtonClicked()
//    {
//        cancelRequested = true;
//        UpdateStatus("Cancelling import...");
//    }

//    private void OpenFileDialog()
//    {
//#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
//        try
//        {
//            string[] paths = StandaloneFileBrowser.OpenFilePanel(
//                "Select CATIA Part File",
//                "",
//                new[] {
//                    new StandaloneFileBrowser.ExtensionFilter("CATIA Files", "CATPart", "CATProduct"),
//                    new StandaloneFileBrowser.ExtensionFilter("All CAD Files", "CATPart", "CATProduct", "stp", "step", "iges", "igs"),
//                    new StandaloneFileBrowser.ExtensionFilter("All Files", "*")
//                },
//                false
//            );

//            if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
//            {
//                StartCoroutine(ImportCATIAFileAsync(paths[0]));
//            }
//        }
//        catch (System.Exception e)
//        {
//            ShowError("File Dialog Error", $"Failed to open file dialog:\n{e.Message}");
//        }
//#else
//        ShowError("Platform Not Supported", "File browser is only supported on Windows builds.");
//#endif
//    }

//    private IEnumerator ImportCATIAFileAsync(string filePath)
//    {
//        isImporting = true;
//        cancelRequested = false;

//        // Show loading panel
//        ShowLoadingPanel(true);
//        UpdateProgress(0f, "Preparing import...");

//        // Validate file
//        yield return StartCoroutine(ValidateFile(filePath));

//        if (cancelRequested)
//        {
//            FinishImport(false, "Import cancelled by user");
//            yield break;
//        }

//        // Clear previous imports if needed
//        if (clearPreviousImports && holder3D != null)
//        {
//            UpdateProgress(0.1f, "Clearing previous imports...");
//            ClearPreviousImports();
//            yield return new WaitForSeconds(0.2f);
//        }

//        if (cancelRequested)
//        {
//            FinishImport(false, "Import cancelled by user");
//            yield break;
//        }

//        // Start import
//        UpdateProgress(0.2f, "Loading file...");
//        yield return new WaitForSeconds(0.1f);

//        bool importSuccess = false;
//        string errorMessage = "";

//        try
//        {
//            // Simulate progress
//            StartCoroutine(SimulateProgress(0.2f, 0.8f, 5f));

//            // Configure import parameters
//            ImportParameters importParams = CreateImportParameters();

//            // Perform the import
//            GameObject importedModel = null;

//            try
//            {
//                UpdateStatus("Importing geometry...");

//                // Use the Importer class
//                Importer importer = new Importer();
//                importedModel = importer.Import(filePath, importParams, holder3D.transform);

//            }
//            catch (System.Exception importEx)
//            {
//                errorMessage = $"Pixyz import failed: {importEx.Message}";
//                Debug.LogError($"{errorMessage}\n{importEx.StackTrace}");
//            }

//            yield return new WaitForSeconds(0.5f);

//            if (cancelRequested)
//            {
//                if (importedModel != null)
//                    Destroy(importedModel);

//                FinishImport(false, "Import cancelled by user");
//                yield break;
//            }

//            // Process imported model
//            if (importedModel != null)
//            {
//                UpdateProgress(0.85f, "Processing model...");
//                yield return StartCoroutine(ProcessImportedModel(importedModel, filePath));

//                currentImportedObject = importedModel;
//                importSuccess = true;
//            }
//            else
//            {
//                errorMessage = "Import returned null. The file may be corrupted or unsupported.";
//            }
//        }
//        catch (System.Exception e)
//        {
//            errorMessage = $"Unexpected error during import:\n{e.Message}";
//            Debug.LogError($"{errorMessage}\n{e.StackTrace}");
//        }

//        // Finish
//        UpdateProgress(1f, importSuccess ? "Import complete!" : "Import failed");
//        yield return new WaitForSeconds(0.5f);

//        if (importSuccess)
//        {
//            FinishImport(true, $"Successfully imported: {Path.GetFileName(filePath)}");
//        }
//        else
//        {
//            FinishImport(false, errorMessage);
//        }
//    }

//    private IEnumerator ValidateFile(string filePath)
//    {
//        UpdateStatus("Validating file...");

//        if (string.IsNullOrEmpty(filePath))
//        {
//            ShowError("Invalid File", "No file path provided.");
//            isImporting = false;
//            ShowLoadingPanel(false);
//            yield break;
//        }

//        if (!File.Exists(filePath))
//        {
//            ShowError("File Not Found", $"The selected file does not exist:\n{filePath}");
//            isImporting = false;
//            ShowLoadingPanel(false);
//            yield break;
//        }

//        // Check file size
//        FileInfo fileInfo = new FileInfo(filePath);
//        float fileSizeMB = fileInfo.Length / (1024f * 1024f);

//        Debug.Log($"File size: {fileSizeMB:F2} MB");

//        if (fileSizeMB > 500f)
//        {
//            Debug.LogWarning($"Large file detected ({fileSizeMB:F2} MB). Import may take several minutes.");
//        }

//        // Check extension
//        string extension = Path.GetExtension(filePath).ToLower();
//        if (extension != ".catpart" && extension != ".catproduct")
//        {
//            Debug.LogWarning($"Unexpected file extension: {extension}. Attempting import anyway.");
//        }

//        yield return null;
//    }

//    private ImportParameters CreateImportParameters()
//    {
//        ImportParameters importParams = new ImportParameters();

//        // Scale settings
//        importParams.scaleFactor = scaleFactor;
//        importParams.isRightHanded = orientToUnity;

//        // Geometry import settings
//        importParams.importLines = importLines;
//        importParams.importPoints = importPoints;
//        importParams.importHiddenObjects = importHiddenObjects;
//        importParams.importPatchBorders = importPatchBorders;
//        importParams.importFreeEdges = importFreeEdges;

//        // Tessellation quality
//        importParams.maxSag = maxSag;
//        importParams.maxAngle = maxAngle;

//        // LOD settings
//        if (generateLODs)
//        {
//            importParams.generateLODs = true;
//            importParams.lodCount = lodLevels;
//        }

//        return importParams;
//    }

//    private IEnumerator ProcessImportedModel(GameObject model, string filePath)
//    {
//        if (model == null) yield break;

//        UpdateStatus("Processing model...");

//        // Model is already parented by Importer.Import() method
//        // Just reset the transform
//        model.transform.localPosition = Vector3.zero;
//        model.transform.localRotation = Quaternion.identity;
//        model.transform.localScale = Vector3.one;

//        yield return null;

//        // Rename
//        model.name = Path.GetFileNameWithoutExtension(filePath);

//        // Count polygons
//        UpdateStatus("Analyzing geometry...");
//        int totalPolygons = CountPolygons(model);
//        Debug.Log($"Total polygons: {totalPolygons:N0}");

//        if (totalPolygons > maxPolygonCount)
//        {
//            Debug.LogWarning($"High polygon count detected ({totalPolygons:N0}). Performance may be affected.");
//        }

//        yield return null;

//        // Center model
//        UpdateStatus("Finalizing...");
//        CenterModel(model);

//        yield return null;
//    }

//    private void ClearPreviousImports()
//    {
//        if (holder3D == null) return;

//        int childCount = holder3D.transform.childCount;

//        for (int i = childCount - 1; i >= 0; i--)
//        {
//            Transform child = holder3D.transform.GetChild(i);
//            Destroy(child.gameObject);
//        }

//        Debug.Log($"Cleared {childCount} previous import(s)");
//    }

//    private int CountPolygons(GameObject obj)
//    {
//        int totalPolygons = 0;
//        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>();

//        foreach (MeshFilter mf in meshFilters)
//        {
//            if (mf.sharedMesh != null)
//            {
//                totalPolygons += mf.sharedMesh.triangles.Length / 3;
//            }
//        }

//        return totalPolygons;
//    }

//    private void CenterModel(GameObject model)
//    {
//        // Calculate bounds
//        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
//        if (renderers.Length == 0) return;

//        Bounds bounds = renderers[0].bounds;
//        foreach (Renderer renderer in renderers)
//        {
//            bounds.Encapsulate(renderer.bounds);
//        }

//        // Center the model
//        Vector3 offset = bounds.center - model.transform.position;
//        model.transform.position -= offset;

//        Debug.Log($"Model centered. Bounds size: {bounds.size}");
//    }

//    private IEnumerator SimulateProgress(float startProgress, float endProgress, float duration)
//    {
//        float elapsed = 0f;

//        while (elapsed < duration && isImporting && !cancelRequested)
//        {
//            elapsed += Time.deltaTime;
//            float progress = Mathf.Lerp(startProgress, endProgress, elapsed / duration);
//            UpdateProgress(progress, "Importing...");
//            yield return null;
//        }
//    }

//    private void UpdateProgress(float progress, string status)
//    {
//        if (progressBar != null)
//        {
//            progressBar.value = progress;
//        }

//        if (progressText != null)
//        {
//            progressText.text = $"{(progress * 100):F0}%";
//        }

//        UpdateStatus(status);
//    }

//    private void UpdateStatus(string status)
//    {
//        if (statusText != null)
//        {
//            statusText.text = status;
//        }

//        Debug.Log($"[Import Status] {status}");
//    }

//    private void ShowLoadingPanel(bool show)
//    {
//        if (loadingPanel != null)
//        {
//            loadingPanel.SetActive(show);
//        }

//        if (importButton != null)
//        {
//            importButton.interactable = !show;
//        }
//    }

//    private void FinishImport(bool success, string message)
//    {
//        isImporting = false;
//        ShowLoadingPanel(false);

//        if (success)
//        {
//            Debug.Log($"<color=green>{message}</color>");
//        }
//        else
//        {
//            ShowError("Import Failed", message);
//        }
//    }

//    private void ShowError(string title, string message)
//    {
//        Debug.LogError($"{title}: {message}");

//        if (errorPanel != null)
//        {
//            errorPanel.SetActive(true);

//            if (errorMessageText != null)
//            {
//                errorMessageText.text = $"<b>{title}</b>\n\n{message}";
//            }
//        }
//    }

//    private void CloseErrorPanel()
//    {
//        if (errorPanel != null)
//        {
//            errorPanel.SetActive(false);
//        }
//    }

//    // Public methods for external control
//    public void ClearCurrentImport()
//    {
//        if (currentImportedObject != null)
//        {
//            Destroy(currentImportedObject);
//            currentImportedObject = null;
//            Debug.Log("Current import cleared");
//        }
//    }

//    public GameObject GetCurrentImport()
//    {
//        return currentImportedObject;
//    }

//    private void OnDestroy()
//    {
//        if (importButton != null)
//            importButton.onClick.RemoveListener(OnImportButtonClicked);

//        if (cancelButton != null)
//            cancelButton.onClick.RemoveListener(OnCancelButtonClicked);

//        if (errorCloseButton != null)
//            errorCloseButton.onClick.RemoveListener(CloseErrorPanel);
//    }
//}
