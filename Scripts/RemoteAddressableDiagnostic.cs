using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VehicleMeasurement
{
    public class RemoteAddressableDiagnostic : MonoBehaviour
    {
        [Header("SERVER SETTINGS")]
        public string serverBaseUrl = "http://mrws180550:8000/Bundles/StandaloneWindows64";

        [Header("TEST KEY")]
        public string testAddressableKey = "Kushaq";

        [Header("RESULTS")]
        [TextArea(10, 30)]
        public string diagnosticOutput = "";

        public bool serverReachable = false;
        public bool catalogFound = false;
        public bool keyExists = false;

        [ContextMenu("1. Run Full Diagnostic")]
        public void RunFullDiagnostic() { StartCoroutine(FullDiagnosticCoroutine()); }

        [ContextMenu("2. List All Available Keys")]
        public void ListAllAvailableKeys() { StartCoroutine(ListAllKeysCoroutine()); }

        [ContextMenu("3. Force Reload Catalog")]
        public void ForceReloadCatalog() { StartCoroutine(ForceReloadCatalogCoroutine()); }

        private IEnumerator FullDiagnosticCoroutine()
        {
            diagnosticOutput = "=== DIAGNOSTIC START ===\n";
            diagnosticOutput += $"Server: {serverBaseUrl}\nKey: {testAddressableKey}\n\n";

            // Test server
            diagnosticOutput += "--- Testing Server ---\n";
            using (var req = UnityWebRequest.Head(serverBaseUrl))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                serverReachable = req.result == UnityWebRequest.Result.Success;
                diagnosticOutput += serverReachable ? "Server: OK\n" : $"Server: FAILED ({req.error})\n";
            }

            // Test catalog
            diagnosticOutput += "\n--- Testing Catalog ---\n";
            string catalogUrl = serverBaseUrl.TrimEnd('/') + "/catalog.json";
            using (var req = UnityWebRequest.Head(catalogUrl))
            {
                req.timeout = 10;
                yield return req.SendWebRequest();
                catalogFound = req.result == UnityWebRequest.Result.Success;
                diagnosticOutput += catalogFound ? "catalog.json: OK\n" : "catalog.json: MISSING!\n";
            }

            // Initialize Addressables
            diagnosticOutput += "\n--- Addressables Init ---\n";
            var initHandle = Addressables.InitializeAsync();
            yield return initHandle;
            diagnosticOutput += initHandle.Status == AsyncOperationStatus.Succeeded ? "Init: OK\n" : $"Init: FAILED\n";

            // Check key
            diagnosticOutput += "\n--- Checking Key ---\n";
            var locHandle = Addressables.LoadResourceLocationsAsync(testAddressableKey);
            yield return locHandle;

            if (locHandle.Status == AsyncOperationStatus.Succeeded && locHandle.Result.Count > 0)
            {
                keyExists = true;
                diagnosticOutput += $"Key '{testAddressableKey}': FOUND\n";
                foreach (var loc in locHandle.Result)
                    diagnosticOutput += $"  Path: {loc.InternalId}\n";
            }
            else
            {
                keyExists = false;
                diagnosticOutput += $"Key '{testAddressableKey}': NOT FOUND!\n";
            }
            Addressables.Release(locHandle);

            diagnosticOutput += "\n=== SUMMARY ===\n";
            diagnosticOutput += $"Server OK: {serverReachable}\nCatalog OK: {catalogFound}\nKey OK: {keyExists}\n";

            if (!keyExists)
            {
                diagnosticOutput += "\nFIX: Run 'List All Available Keys' to see what exists.\n";
            }
        }

        private IEnumerator ListAllKeysCoroutine()
        {
            diagnosticOutput = "=== ALL AVAILABLE KEYS ===\n\n";

            var handle = Addressables.InitializeAsync();
            yield return handle;

            List<string> keys = new List<string>();
            foreach (var locator in Addressables.ResourceLocators)
            {
                foreach (var key in locator.Keys)
                {
                    if (key is string s && !s.StartsWith("com.unity") && !s.Contains("[") && !s.EndsWith(".bundle"))
                    {
                        if (!keys.Contains(s)) keys.Add(s);
                    }
                }
            }

            keys.Sort();
            diagnosticOutput += $"Found {keys.Count} keys:\n\n";
            foreach (var k in keys) diagnosticOutput += $"  {k}\n";

            if (keys.Count == 0)
                diagnosticOutput += "NO KEYS! Addressables not built or catalog missing.\n";
        }

        private IEnumerator ForceReloadCatalogCoroutine()
        {
            diagnosticOutput = "Reloading catalog...\n";
            Caching.ClearCache();

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            yield return checkHandle;

            if (checkHandle.Status == AsyncOperationStatus.Succeeded && checkHandle.Result.Count > 0)
            {
                var updateHandle = Addressables.UpdateCatalogs(checkHandle.Result, false);
                yield return updateHandle;
                diagnosticOutput += updateHandle.Status == AsyncOperationStatus.Succeeded ? "Catalog updated!\n" : "Update failed!\n";
                Addressables.Release(updateHandle);
            }
            else
            {
                diagnosticOutput += "No updates or check failed.\n";
            }
            Addressables.Release(checkHandle);
        }
    }
}
