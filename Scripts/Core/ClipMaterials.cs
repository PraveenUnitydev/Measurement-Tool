using System.Collections.Generic;
using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>
    /// Materials for the clip section (Measurement clip mode and the comparison screens).
    ///
    /// Two ways a vehicle part can be clipped:
    ///  1. Its own Shader Graph clips (it has the DAS clip node - see Shaders/Resources/DAS/DASClip.hlsl and
    ///     Editor/ShaderGraphClipPatcher.cs; such shaders expose "_DASClipSupport"). Nothing is swapped: the part keeps its
    ///     exact look - car paint, clear coat, reflections, glass.
    ///  2. Otherwise it is drawn with the stand-in "VehicleMeasurement/ClipSection_Lit": URP PBR lighting (reflection
    ///     probes, specular, clear coat, shadows), transparency for glass, and the colours/maps read from the original
    ///     material - including the DAS Shader Graph property names (see ClipMaterialMapping).
    /// </summary>
    public static class ClipMaterials
    {
        public const string StandInShaderName = "VehicleMeasurement/ClipSection_Lit";
        public const string StandInResource = "DAS/ClipSection_Lit";
        /// <summary>A shader exposing this property clips by itself (DAS clip node in the graph).</summary>
        public const string NativeClipProperty = "_DASClipSupport";

        private static Shader _shader;
        private static bool _warned;

        /// <summary>The stand-in clip shader (bundled in Resources so it is always in the build).</summary>
        public static Shader StandInShader
        {
            get
            {
                if (_shader != null) return _shader;
                _shader = Resources.Load<Shader>(StandInResource);
                if (_shader == null) _shader = Shader.Find(StandInShaderName);
                if (_shader == null)
                {
                    // last resort: the old shaders (look wrong, but still clip)
                    _shader = Shader.Find("VehicleMeasurement/ClipSection_URP");
                    if (_shader == null) _shader = Shader.Find("VehicleMeasurement/ClipSection");
                    if (!_warned)
                    {
                        _warned = true;
                        Debug.LogError("[ClipSection] Shader '" + StandInShaderName + "' is missing (expected at Resources/" + StandInResource +
                                       ".shader). Using " + (_shader != null ? _shader.name : "nothing") + " instead.");
                    }
                }
                return _shader;
            }
        }

        /// <summary>True if this material's own shader cuts at the clip plane, so it can be left as it is.</summary>
        public static bool ClipsNatively(Material m)
        {
            return m != null && m.shader != null && m.HasProperty(NativeClipProperty);
        }

        /// <summary>True for a material made by <see cref="CreateFor"/>.</summary>
        public static bool IsStandIn(Material m)
        {
            return m != null && m.shader != null && m.shader.name == StandInShaderName;
        }

        /// <summary>A new stand-in material that looks like <paramref name="source"/> and clips. Caller owns (destroys) it.</summary>
        public static Material CreateFor(Material source, float opacity = 1f)
        {
            Shader shader = StandInShader;
            if (shader == null) return null;
            var mat = new Material(shader);
            mat.name = (source != null ? source.name : "Missing") + " (Clip)";
            if (shader.name == StandInShaderName)
            {
                ClipLook look = source != null ? ClipMaterialMapping.Describe(new Reader(source)) : new ClipLook();
                ClipMaterialMapping.Apply(look, new Writer(mat), opacity);
            }
            else if (source != null)
            {
                // old shader: copy what it understands
                if (source.HasProperty("_BaseColor") && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                else if (source.HasProperty("_Color") && mat.HasProperty("_Color")) mat.SetColor("_Color", source.GetColor("_Color"));
            }
            return mat;
        }

        /// <summary>Fade a stand-in (comparison screens). Glass stays see-through at full opacity.</summary>
        public static void SetOpacity(Material m, float opacity)
        {
            if (m == null) return;
            if (!IsStandIn(m))
            {
                if (m.HasProperty("_Opacity")) m.SetFloat("_Opacity", opacity);
                return;
            }
            bool keep = m.HasProperty("_DASKeepTransparent") && m.GetFloat("_DASKeepTransparent") > 0.5f;
            float surfaceAlpha = m.HasProperty("_DASSurfaceAlpha") ? m.GetFloat("_DASSurfaceAlpha") : 1f;
            ClipMaterialMapping.SetOpacity(new Writer(m), opacity, keep, surfaceAlpha);
        }

        /// <summary>Colour a stand-in (comparison A/B tint). Textures stay, multiplied by the tint.</summary>
        public static void Tint(Material m, Color tint)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            else if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
        }

        /// <summary>
        /// Swap the materials of every renderer under <paramref name="root"/> for clipping ones.
        /// Materials that clip natively are kept. Stand-ins are shared per original material (cache) so a material used by
        /// 500 parts makes one stand-in, and they are reused when clip mode is toggled. Returns how many were swapped.
        /// </summary>
        public static int Apply(GameObject root, Dictionary<Renderer, Material[]> originals, Dictionary<Material, Material> cache,
                                out int native, bool forceStandIn = false)
        {
            native = 0;
            int swapped = 0;
            if (root == null) return 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                Material[] shared;
                if (!originals.TryGetValue(r, out shared))
                {
                    shared = r.sharedMaterials;
                    originals[r] = shared;
                }
                if (shared == null || shared.Length == 0) continue;

                var next = new Material[shared.Length];
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    Material src = shared[i];
                    if (src == null) { next[i] = null; continue; }
                    if (!forceStandIn && ClipsNatively(src)) { next[i] = src; native++; continue; }
                    Material standIn = null;
                    cache.TryGetValue(src, out standIn);
                    if (standIn == null)
                    {
                        standIn = CreateFor(src);
                        if (standIn == null) { next[i] = src; continue; }
                        cache[src] = standIn;
                    }
                    next[i] = standIn;
                    changed = true;
                    swapped++;
                }
                if (changed) r.sharedMaterials = next;
            }
            return swapped;
        }

        /// <summary>Put the original materials back.</summary>
        public static void Restore(Dictionary<Renderer, Material[]> originals)
        {
            foreach (var kv in originals)
                if (kv.Key != null && kv.Value != null) kv.Key.sharedMaterials = kv.Value;
        }

        /// <summary>Destroy the stand-ins made for a vehicle.</summary>
        public static void DestroyAll(Dictionary<Material, Material> cache)
        {
            foreach (var m in cache.Values)
                if (m != null) Object.Destroy(m);
            cache.Clear();
        }

        // ── adapters ─────────────────────────────────────────────────────

        private sealed class Reader : IClipMaterialReader
        {
            private readonly Material _m;
            public Reader(Material m) { _m = m; }
            public string ShaderName { get { return _m.shader != null ? _m.shader.name : ""; } }
            public int RenderQueue { get { return _m.renderQueue; } }
            public string RenderTypeTag { get { return _m.GetTag("RenderType", false, ""); } }
            public bool HasFloat(string n) { return _m.HasFloat(n); }
            public float GetFloat(string n) { return _m.GetFloat(n); }
            public bool HasColor(string n) { return _m.HasColor(n); }
            public Color GetColor(string n) { return _m.GetColor(n); }
            public bool HasTexture(string n) { return _m.HasTexture(n); }
            public Texture GetTexture(string n) { return _m.GetTexture(n); }
            public Vector2 GetTextureScale(string n) { return _m.GetTextureScale(n); }
            public Vector2 GetTextureOffset(string n) { return _m.GetTextureOffset(n); }
            public bool IsKeywordEnabled(string k) { return _m.IsKeywordEnabled(k); }
        }

        private sealed class Writer : IClipMaterialWriter
        {
            private readonly Material _m;
            public Writer(Material m) { _m = m; }
            public void SetFloat(string n, float v) { if (_m.HasProperty(n)) _m.SetFloat(n, v); }
            public void SetColor(string n, Color v) { if (_m.HasProperty(n)) _m.SetColor(n, v); }
            public void SetTexture(string n, Texture v) { if (_m.HasProperty(n)) _m.SetTexture(n, v); }
            public void SetTextureScaleOffset(string n, Vector2 s, Vector2 o)
            {
                if (!_m.HasProperty(n)) return;
                _m.SetTextureScale(n, s);
                _m.SetTextureOffset(n, o);
            }
            public void SetKeyword(string k, bool on) { if (on) _m.EnableKeyword(k); else _m.DisableKeyword(k); }
            public void SetRenderQueue(int q) { _m.renderQueue = q; }
            public void SetTag(string t, string v) { _m.SetOverrideTag(t, v); }
        }
    }
}
