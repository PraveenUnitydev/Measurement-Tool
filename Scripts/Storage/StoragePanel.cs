namespace VehicleMeasurement.Storage
{
    /// <summary>
    /// The separate Storage screen is now part of the Vehicles screen (VehicleLibraryPanel): the "On this PC" tab lists
    /// what is downloaded, with Remove per vehicle or for a selection, and the bottom strip shows disk use, free space,
    /// the download folder (change / open / move here / default) and "Remove older versions".
    /// Kept so existing buttons and code that call StoragePanel.Open() keep working.
    /// </summary>
    public static class StoragePanel
    {
        public static void Open() { VehicleLibraryPanel.Open(LibraryFilter.OnThisPc); }
    }
}

namespace VehicleMeasurement.Storage
{
    /// <summary>Ctrl+Shift+S opens Vehicles on the "On this PC" tab from any screen.</summary>
    public class StorageHotkey : UnityEngine.MonoBehaviour
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new UnityEngine.GameObject("StorageHotkey");
            go.hideFlags = UnityEngine.HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<StorageHotkey>();
        }

        private void Update()
        {
            bool ctrl = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftControl) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightControl);
            bool shift = UnityEngine.Input.GetKey(UnityEngine.KeyCode.LeftShift) || UnityEngine.Input.GetKey(UnityEngine.KeyCode.RightShift);
            if (ctrl && shift && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.S)) VehicleLibraryPanel.Open(LibraryFilter.OnThisPc);
        }
    }
}
