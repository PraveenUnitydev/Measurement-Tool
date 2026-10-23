using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VehicleMeasurement.EditorTools.Publish
{
    /// <summary>Checks a vehicle prefab before it is published, and captures its thumbnail.</summary>
    public static class DasVehicleChecks
    {
        public enum Level { Ok, Warning, Error }

        public class Finding
        {
            public Level level;
            public string text;
            public Finding(Level l, string t) { level = l; text = t; }
        }

        public static List<Finding> Run(GameObject prefab)
        {
            var list = new List<Finding>();
            if (prefab == null) { list.Add(new Finding(Level.Error, "Pick the vehicle prefab.")); return list; }
            string path = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(new Finding(Level.Error, "This isn't a prefab asset. Drag the prefab from the Project window."));
                return list;
            }

            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);

                int missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                if (missingScripts > 0) list.Add(new Finding(Level.Error, missingScripts + " missing script(s) in the prefab. Remove them (they break loading in the app)."));

                var data = root.GetComponentInChildren<VehiclePrefabData>(true);
                if (data == null) list.Add(new Finding(Level.Warning, "No VehiclePrefabData on the prefab. Measurements work better with it (wheels, body, reference points)."));

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) { list.Add(new Finding(Level.Error, "The prefab has no meshes.")); return list; }

                long tris = 0;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
                list.Add(new Finding(Level.Ok, renderers.Length + " meshes, " + (tris / 1000) + "k triangles."));
                if (tris > 15000000) list.Add(new Finding(Level.Warning, "Very heavy model (" + (tris / 1000000) + "M triangles) - it may load slowly on lab PCs."));

                int noMaterial = 0, broken = 0, notUrp = 0;
                var notUrpNames = new HashSet<string>();
                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { noMaterial++; continue; }
                        if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader") { broken++; continue; }
                        string n = m.shader.name;
                        if (!(n.StartsWith("Universal Render Pipeline/") || n.StartsWith("Shader Graphs/") || n.StartsWith("VehicleMeasurement/")))
                        { notUrp++; notUrpNames.Add(n); }
                    }
                if (noMaterial > 0) list.Add(new Finding(Level.Error, noMaterial + " empty material slot(s) - they show pink in the app. Assign materials."));
                if (broken > 0) list.Add(new Finding(Level.Error, broken + " material(s) with a broken shader (pink)."));
                if (notUrp > 0) list.Add(new Finding(Level.Warning, notUrp + " material slot(s) use shaders that may not render in the app: " + string.Join(", ", notUrpNames.Take(4)) + ". Use the DAS Shader Graph materials."));
                if (noMaterial == 0 && broken == 0 && notUrp == 0) list.Add(new Finding(Level.Ok, "All materials use URP / DAS shaders."));

                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (longest > 20f) list.Add(new Finding(Level.Warning, "The vehicle is " + longest.ToString("0.#") + " m long - it looks like millimetres or centimetres. Scale it to metres (or tick 'units are millimeters' in VehiclePrefabData)."));
                else if (longest < 1f) list.Add(new Finding(Level.Warning, "The vehicle is only " + longest.ToString("0.##") + " m long - check the scale."));
                else list.Add(new Finding(Level.Ok, "Size " + b.size.x.ToString("0.00") + " x " + b.size.y.ToString("0.00") + " x " + b.size.z.ToString("0.00") + " m."));
            }
            catch (Exception e) { list.Add(new Finding(Level.Error, "Couldn't check the prefab: " + e.Message)); }
            finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
            return list;
        }

        /// <summary>Render what the Scene view shows into a 1280x720 PNG. Frame the vehicle in the Scene view first.</summary>
        public static byte[] CaptureSceneView(out string error)
        {
            error = null;
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) { error = "Open a Scene view and frame the vehicle first."; return null; }
            return Render(view.camera, out error);
        }

        public static byte[] Render(Camera cam, out string error)
        {
            error = null;
            const int w = 1280, h = 720;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            float prevAspect = cam.aspect;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.aspect = (float)w / h;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            catch (Exception e) { error = "Couldn't capture: " + e.Message; return null; }
            finally
            {
                cam.targetTexture = prevTarget;
                cam.aspect = prevAspect;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>Read a PNG/JPG chosen by the user (max 8 MB).</summary>
        public static byte[] LoadImageFile(string path, out string error)
        {
            error = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) { error = "File not found."; return null; }
                if (info.Length > 8L * 1024 * 1024) { error = "The picture is larger than 8 MB - use a smaller one (1280x720 is plenty)."; return null; }
                byte[] bytes = File.ReadAllBytes(path);
                var t = new Texture2D(2, 2);
                bool ok = t.LoadImage(bytes);
                UnityEngine.Object.DestroyImmediate(t);
                if (!ok) { error = "Not a PNG or JPG picture."; return null; }
                return bytes;
            }
            catch (Exception e) { error = e.Message; return null; }
        }
    }
}
