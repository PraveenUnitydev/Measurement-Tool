using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VehicleMeasurement.AppUpdate;

namespace VehicleMeasurement.EditorTools.AppUpdates
{
    /// <summary>
    /// The DAS app signing key (APP-UPDATES.md). The private half stays with the DAS admin, outside the project and out
    /// of Git; it signs every release. The public half is built into DAS (Scripts/AppUpdate/DasAppUpdateKey.cs) and set
    /// on VRSP (DAS_APP_PUBLIC_KEY), and both refuse anything not signed with the private half.
    /// </summary>
    public static class DasAppSigningKey
    {
        public const string PrivateKeyPathPref = "DAS.AppUpdate.PrivateKeyPath";

        public static string PrivateKeyPath
        {
            get { return EditorPrefs.GetString(PrivateKeyPathPref, ""); }
            set { EditorPrefs.SetString(PrivateKeyPathPref, value ?? ""); }
        }

        /// <summary>The DAS scripts folder that holds AppReleaseCore.cs (Scripts/AppUpdate).</summary>
        public static string AppUpdateFolder()
        {
            foreach (string guid in AssetDatabase.FindAssets("AppReleaseCore t:MonoScript"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("/AppReleaseCore.cs", StringComparison.Ordinal)) return Path.GetDirectoryName(p).Replace('\\', '/');
            }
            return null;
        }

        public static string UpdaterExePath()
        {
            string f = AppUpdateFolder();
            return f == null ? null : Path.GetFullPath(Path.Combine(f, "Updater~", "DASUpdater.exe"));
        }

        /// <summary>The modulus of an XML key (public or private), to tell whether two keys belong together.</summary>
        public static string Modulus(string xml)
        {
            var m = Regex.Match(xml ?? "", "<Modulus>([^<]+)</Modulus>");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>Reads the private key and checks it belongs to the key built into DAS. Returns null + error if not.</summary>
        public static string LoadPrivateKey(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml)) { error = "There is no signing key yet. Create one: DAS > App Updates > Create Signing Key."; return null; }
            string path = PrivateKeyPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { error = "Choose the signing key file (das-app-signing-key.xml) below."; return null; }
            string xml = File.ReadAllText(path);
            if (!xml.Contains("<D>")) { error = "That file is not a private signing key."; return null; }
            if (Modulus(xml) != Modulus(DasAppUpdateKey.PublicKeyXml)) { error = "That signing key doesn't match the key built into DAS (Scripts/AppUpdate/DasAppUpdateKey.cs)."; return null; }
            return xml;
        }

        public static string ServerLine() { return "DAS_APP_PUBLIC_KEY=" + DasAppUpdateKey.PublicKeyXml; }

        [MenuItem("DAS/App Updates/Create Signing Key...", false, 60)]
        public static void Create()
        {
            if (!string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml) &&
                !EditorUtility.DisplayDialog("Replace the signing key?",
                    "DAS already has a signing key. DAS PCs that have the current key will NOT accept updates signed with a new one - they would need a new DAS copied by hand.\n\nOnly replace the key if the old private key was lost or leaked.",
                    "Replace it", "Cancel")) return;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string file = EditorUtility.SaveFilePanel("Save the private signing key (keep it safe, outside the project)", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "das-app-signing-key", "xml");
            if (string.IsNullOrEmpty(file)) return;
            if (Path.GetFullPath(file).StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Not inside the project", "Save the private key outside the Unity project, so it never ends up in Git or in a build.", "OK");
                return;
            }
            string folder = AppUpdateFolder();
            if (folder == null) { EditorUtility.DisplayDialog("Not found", "Scripts/AppUpdate/AppReleaseCore.cs was not found in the project.", "OK"); return; }

            var pair = ReleaseSignature.NewKeyPair(3072);
            File.WriteAllText(file, pair.Key);
            PrivateKeyPath = file;
            File.WriteAllText(Path.Combine(folder, "DasAppUpdateKey.cs"),
                "// The public half of the DAS app signing key - see the notes in APP-UPDATES.md. Written by DAS > App Updates > Create Signing Key.\n" +
                "namespace VehicleMeasurement.AppUpdate\n{\n    public static class DasAppUpdateKey\n    {\n        public const string PublicKeyXml = \"" + pair.Value + "\";\n    }\n}\n");
            GUIUtility.systemCopyBuffer = "DAS_APP_PUBLIC_KEY=" + pair.Value;
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Signing key created",
                "Private key saved to:\n" + file + "\nKeep a backup somewhere safe (a password manager or an encrypted drive). Anyone with it can publish DAS updates.\n\n" +
                "The public key is now in Scripts/AppUpdate/DasAppUpdateKey.cs - commit that file.\n\n" +
                "The line for VRSP's backend/.env is on the clipboard (DAS_APP_PUBLIC_KEY=...). Add it there and restart VRSP.", "OK");
        }

        [MenuItem("DAS/App Updates/Copy VRSP Key Line", false, 61)]
        public static void CopyServerLine()
        {
            if (string.IsNullOrEmpty(DasAppUpdateKey.PublicKeyXml)) { EditorUtility.DisplayDialog("No key yet", "Create one first: DAS > App Updates > Create Signing Key.", "OK"); return; }
            GUIUtility.systemCopyBuffer = ServerLine();
            Debug.Log("[DAS App Updates] Copied the line for VRSP's backend/.env (DAS_APP_PUBLIC_KEY=...).");
        }
    }
}
