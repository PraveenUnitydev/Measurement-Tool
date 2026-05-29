using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// WINDOWS FILE DIALOG
    /// 
    /// Native Windows file dialogs for Save/Open operations.
    /// No external dependencies - uses Windows API directly.
    /// 
    /// USAGE:
    /// 
    /// // Save dialog
    /// string path = WindowsFileDialog.SaveFileDialog(
    ///     "Save PDF Report",
    ///     "PDF Files\0*.pdf\0",
    ///     "pdf",
    ///     "MyReport"
    /// );
    /// 
    /// // Open dialog
    /// string path = WindowsFileDialog.OpenFileDialog(
    ///     "Select Vehicle",
    ///     "JSON Files\0*.json\0"
    /// );
    /// 
    /// </summary>
    public static class WindowsFileDialog
    {
        #region Windows API Structures
        
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAME
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }
        
        // Dialog flags
        private const int OFN_OVERWRITEPROMPT = 0x00000002;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_EXPLORER = 0x00080000;
        
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetSaveFileNameW(ref OPENFILENAME lpofn);
        
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);
        
        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Show Windows Save File dialog
        /// </summary>
        /// <param name="title">Dialog title</param>
        /// <param name="filter">Filter string (e.g., "PDF Files\0*.pdf\0All Files\0*.*\0")</param>
        /// <param name="defaultExt">Default extension without dot (e.g., "pdf")</param>
        /// <param name="defaultFileName">Default file name without extension</param>
        /// <param name="initialDir">Initial directory (optional)</param>
        /// <returns>Selected file path, or null if cancelled</returns>
        public static string SaveFileDialog(
            string title = "Save File",
            string filter = "All Files\0*.*\0",
            string defaultExt = "",
            string defaultFileName = "",
            string initialDir = null)
        {
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            
            IntPtr fileBuffer = Marshal.AllocHGlobal(2048 * sizeof(char));
            
            try
            {
                // Initialize buffer with default filename
                string initialFile = string.IsNullOrEmpty(defaultFileName) ? "" : defaultFileName;
                byte[] initialBytes = System.Text.Encoding.Unicode.GetBytes(initialFile + "\0");
                Marshal.Copy(initialBytes, 0, fileBuffer, initialBytes.Length);
                
                OPENFILENAME ofn = new OPENFILENAME();
                ofn.lStructSize = Marshal.SizeOf(typeof(OPENFILENAME));
                ofn.hwndOwner = IntPtr.Zero;
                ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = 2048;
                ofn.lpstrTitle = title;
                ofn.lpstrDefExt = defaultExt;
                ofn.lpstrInitialDir = initialDir ?? GetDefaultDirectory();
                ofn.Flags = OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | OFN_EXPLORER;
                
                if (GetSaveFileNameW(ref ofn))
                {
                    string result = Marshal.PtrToStringUni(fileBuffer);
                    
                    // Ensure extension is added
                    if (!string.IsNullOrEmpty(defaultExt) && !result.EndsWith("." + defaultExt, StringComparison.OrdinalIgnoreCase))
                    {
                        result += "." + defaultExt;
                    }
                    
                    return result;
                }
                else
                {
                    int error = CommDlgExtendedError();
                    if (error != 0)
                        Debug.LogWarning($"[FileDialog] Error code: {error}");
                    return null;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
            
            #else
            Debug.LogWarning("[FileDialog] Only supported on Windows");
            return null;
            #endif
        }
        
        /// <summary>
        /// Show Windows Open File dialog
        /// </summary>
        public static string OpenFileDialog(
            string title = "Open File",
            string filter = "All Files\0*.*\0",
            string initialDir = null)
        {
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            
            IntPtr fileBuffer = Marshal.AllocHGlobal(2048 * sizeof(char));
            
            try
            {
                Marshal.Copy(new byte[2048 * sizeof(char)], 0, fileBuffer, 2048 * sizeof(char));
                
                OPENFILENAME ofn = new OPENFILENAME();
                ofn.lStructSize = Marshal.SizeOf(typeof(OPENFILENAME));
                ofn.hwndOwner = IntPtr.Zero;
                ofn.lpstrFilter = filter.Replace("|", "\0") + "\0";
                ofn.nFilterIndex = 1;
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = 2048;
                ofn.lpstrTitle = title;
                ofn.lpstrInitialDir = initialDir ?? GetDefaultDirectory();
                ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER;
                
                if (GetOpenFileNameW(ref ofn))
                {
                    return Marshal.PtrToStringUni(fileBuffer);
                }
                
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
            
            #else
            Debug.LogWarning("[FileDialog] Only supported on Windows");
            return null;
            #endif
        }
        
        #endregion
        
        #region Helpers
        
        private static string GetDefaultDirectory()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Helper to build file dialog filters
    /// </summary>
    public class FileDialogFilter
    {
        private System.Text.StringBuilder _sb = new System.Text.StringBuilder();
        
        public FileDialogFilter Add(string description, string extensions)
        {
            _sb.Append(description);
            _sb.Append('\0');
            _sb.Append(extensions);
            _sb.Append('\0');
            return this;
        }
        
        public FileDialogFilter AddAllFiles()
        {
            return Add("All Files", "*.*");
        }
        
        public override string ToString()
        {
            return _sb.ToString();
        }
        
        // Common presets
        public static string PDF => new FileDialogFilter().Add("PDF Files", "*.pdf").AddAllFiles().ToString();
        public static string JSON => new FileDialogFilter().Add("JSON Files", "*.json").AddAllFiles().ToString();
        public static string Images => new FileDialogFilter().Add("Image Files", "*.png;*.jpg;*.jpeg").AddAllFiles().ToString();
    }
}
