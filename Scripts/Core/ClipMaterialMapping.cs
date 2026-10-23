using UnityEngine;

namespace VehicleMeasurement
{
    /// <summary>Read access to a material (implemented over UnityEngine.Material in ClipMaterials; faked in tests).</summary>
    public interface IClipMaterialReader
    {
        string ShaderName { get; }
        int RenderQueue { get; }
        string RenderTypeTag { get; }
        bool HasFloat(string name);
        float GetFloat(string name);
        bool HasColor(string name);
        Color GetColor(string name);
        bool HasTexture(string name);
        Texture GetTexture(string name);
        Vector2 GetTextureScale(string name);
        Vector2 GetTextureOffset(string name);
        bool IsKeywordEnabled(string keyword);
    }

    /// <summary>Write access to the clip stand-in material.</summary>
    public interface IClipMaterialWriter
    {
        void SetFloat(string name, float value);
        void SetColor(string name, Color value);
        void SetTexture(string name, Texture value);
        void SetTextureScaleOffset(string name, Vector2 scale, Vector2 offset);
        void SetKeyword(string keyword, bool on);
        void SetRenderQueue(int queue);
        void SetTag(string tag, string value);
    }

    /// <summary>What the clip stand-in needs to look like a given vehicle material.</summary>
    public class ClipLook
    {
        public Color baseColor = Color.white;          // rgb only; alpha is always 1 here
        public Texture baseMap;
        public bool useBaseMap;
        public float baseMapLevel = 1f;
        public Vector2 baseMapScale = new Vector2(1, 1), baseMapOffset = new Vector2(0, 0);

        public float metallic;
        public Texture metallicMap;
        public bool useMetallicMap;

        public float smoothness = 0.5f;
        public Texture smoothnessMap;
        public int smoothnessMapMode;                  // 0 none, 1 red channel, 2 alpha channel

        public Texture normalMap;
        public bool useNormalMap;
        public float normalStrength = 1f;

        public float clearCoat;
        public float clearCoatSmoothness = 1f;
        public Color emission = new Color(0, 0, 0, 1);

        public bool triplanar;                         // DAS Shader Graphs texture in world space
        public float triplanarTiling = 1f;

        public float surfaceAlpha = 1f;                // e.g. glass 0.2
        public bool transparent;

        public string source = "";                     // which family it was read as (for logs/tests)
    }

    /// <summary>
    /// Reads a vehicle material - a DAS Shader Graph (car paint, alloy, chrome, glass, leather/plastic/rubber/seat, lights),
    /// URP Lit, or the built-in Standard shader - and works out how the clip stand-in should look.
    ///
    /// The old code only copied _Color/_BaseColor/_MainTex/_BaseMap/_Metallic/_Smoothness. The DAS graphs keep their
    /// colour in _Base_Map (a Color), metallic in _Metallic_Map/_MetallicValues, smoothness in _RoughnessValues/_RoughnessLevel,
    /// colours with alpha 0, glass alpha in _Alpha and textures in world space (_Tiling_UV) - so everything came out
    /// white, grey or black, and glass became solid.
    /// </summary>
    public static class ClipMaterialMapping
    {
        public static ClipLook Describe(IClipMaterialReader m)
        {
            var look = new ClipLook();
            if (m == null) return look;

            // ── colour ─────────────────────────────────────────────
            Color color = Color.white;
            if (m.HasColor("_Base_Map"))                                    // DAS Alloy / Chrome / Light
            {
                color = m.GetColor("_Base_Map");
                look.source = "das-color";
            }
            else if (m.HasFloat("_Use_BaseMap"))                            // DAS Leather / Plastic / Rubber / Seat
            {
                Texture tex = m.HasTexture("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                bool useTex = m.GetFloat("_Use_BaseMap") > 0.5f && tex != null;
                if (useTex)
                {
                    look.baseMap = tex;
                    look.useBaseMap = true;
                    look.baseMapLevel = m.HasFloat("_BaseColorLevel") ? m.GetFloat("_BaseColorLevel") : 1f;
                    color = Color.white;
                }
                else color = m.HasColor("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                look.source = "das-textured";
            }
            else if (m.HasColor("_BaseColor"))                              // DAS Car Paint, URP Lit
            {
                color = m.GetColor("_BaseColor");
                Texture tex = m.HasTexture("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                if (tex != null)
                {
                    look.baseMap = tex;
                    look.useBaseMap = true;
                    look.baseMapScale = m.GetTextureScale("_BaseMap");
                    look.baseMapOffset = m.GetTextureOffset("_BaseMap");
                }
                look.source = m.HasFloat("_Tiling_UV") ? "das-paint" : "urp";
            }
            else if (m.HasColor("_Color"))                                  // DAS Glass, built-in Standard
            {
                color = m.GetColor("_Color");
                Texture tex = m.HasTexture("_MainTex") ? m.GetTexture("_MainTex") : null;
                if (tex != null)
                {
                    look.baseMap = tex;
                    look.useBaseMap = true;
                    look.baseMapScale = m.GetTextureScale("_MainTex");
                    look.baseMapOffset = m.GetTextureOffset("_MainTex");
                }
                look.source = m.HasFloat("_Alpha") ? "das-glass" : "standard";
            }
            else look.source = "unknown";

            // ── transparency ───────────────────────────────────────
            bool transparent =
                m.HasFloat("_Alpha") ||
                (m.HasFloat("_Surface") && m.GetFloat("_Surface") > 0.5f) ||
                (m.HasFloat("_Mode") && m.GetFloat("_Mode") >= 2f) ||
                (m.RenderQueue >= 2900 && string.Equals(m.RenderTypeTag, "Transparent", System.StringComparison.OrdinalIgnoreCase));
            look.transparent = transparent;
            if (m.HasFloat("_Alpha")) look.surfaceAlpha = Clamp01(m.GetFloat("_Alpha"));
            else if (transparent) look.surfaceAlpha = Clamp01(color.a);
            else look.surfaceAlpha = 1f;
            look.baseColor = new Color(color.r, color.g, color.b, 1f);   // DAS graph colours carry alpha 0

            // ── metallic ───────────────────────────────────────────
            Texture metallicTex = null;
            if (m.HasTexture("_MetallicMap")) metallicTex = m.GetTexture("_MetallicMap");             // DAS textured (world space)
            if (metallicTex != null)
            {
                look.metallic = 1f;
                look.metallicMap = metallicTex;
                look.useMetallicMap = true;
            }
            else if (m.HasFloat("_Metallic_Map")) look.metallic = m.GetFloat("_Metallic_Map");         // DAS Alloy / Chrome
            else if (m.HasFloat("_MetallicValues")) look.metallic = m.GetFloat("_MetallicValues");     // DAS Glass / Light
            else if (m.HasFloat("_Metallic")) look.metallic = m.GetFloat("_Metallic");                 // Car paint, URP, Standard
            else look.metallic = 0f;

            // ── smoothness ─────────────────────────────────────────
            if (m.HasFloat("_RoughnessLevel"))                            // DAS textured: Smoothness = level * map
            {
                look.smoothness = m.GetFloat("_RoughnessLevel");
                Texture r = m.HasTexture("_RoughnessMap") ? m.GetTexture("_RoughnessMap") : null;
                if (r != null) { look.smoothnessMap = r; look.smoothnessMapMode = 1; }
            }
            else if (m.HasFloat("_RoughnessValues")) look.smoothness = m.GetFloat("_RoughnessValues"); // graphs feed it straight in
            else if (m.HasFloat("_Smoothness")) look.smoothness = m.GetFloat("_Smoothness");
            else if (m.HasFloat("_Glossiness")) look.smoothness = m.GetFloat("_Glossiness");

            // URP Lit / Standard metallic-gloss map: metallic in R, smoothness in A
            Texture mg = m.HasTexture("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
            if (mg != null && metallicTex == null)
            {
                look.metallic = 1f;
                look.metallicMap = mg;
                look.useMetallicMap = true;
                look.smoothnessMap = mg;
                look.smoothnessMapMode = 2;
                if (m.HasFloat("_GlossMapScale") && !m.HasFloat("_Smoothness")) look.smoothness = m.GetFloat("_GlossMapScale");
            }

            // ── normal map ─────────────────────────────────────────
            if (m.HasTexture("_NormalMap") && m.GetTexture("_NormalMap") != null)                      // DAS (world space)
            {
                look.normalMap = m.GetTexture("_NormalMap");
                look.useNormalMap = true;
                look.normalStrength = m.HasFloat("_NormalStrength") ? m.GetFloat("_NormalStrength")
                                    : m.HasFloat("_NormalStrenght") ? m.GetFloat("_NormalStrenght") : 1f;
            }
            else if (m.HasTexture("_BumpMap") && m.GetTexture("_BumpMap") != null)                     // URP / Standard
            {
                look.normalMap = m.GetTexture("_BumpMap");
                look.useNormalMap = true;
                look.normalStrength = m.HasFloat("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
            }

            // ── clear coat ─────────────────────────────────────────
            if (m.HasFloat("_ClearCoat")) look.clearCoat = m.GetFloat("_ClearCoat");                   // DAS paint / glass
            else if (m.HasFloat("_ClearCoatMask") && (m.IsKeywordEnabled("_CLEARCOAT") || m.IsKeywordEnabled("_CLEARCOATMAP")))
                look.clearCoat = m.GetFloat("_ClearCoatMask");                                       // URP Complex Lit
            if (m.HasFloat("_ClearCoatSmoothness")) look.clearCoatSmoothness = m.GetFloat("_ClearCoatSmoothness");

            // ── emission ───────────────────────────────────────────
            if (m.HasColor("_Emission_Map")) look.emission = m.GetColor("_Emission_Map");             // DAS Light
            else if (m.HasColor("_Emission_color")) look.emission = m.GetColor("_Emission_color");    // DAS Glass
            else if (m.HasColor("_EmissionColor") && m.IsKeywordEnabled("_EMISSION"))
                look.emission = m.GetColor("_EmissionColor");
            look.emission = new Color(look.emission.r, look.emission.g, look.emission.b, 1f);

            // ── world-space texturing (DAS graphs) ─────────────────
            if (m.HasFloat("_Tiling_UV"))
            {
                look.triplanar = true;
                look.triplanarTiling = m.GetFloat("_Tiling_UV");
                if (look.triplanarTiling == 0f) look.triplanarTiling = 1f;
            }

            return look;
        }

        /// <summary>Write a look into the clip stand-in material.</summary>
        public static void Apply(ClipLook look, IClipMaterialWriter w, float opacity = 1f)
        {
            w.SetColor("_BaseColor", look.baseColor);
            w.SetTexture("_BaseMap", look.baseMap);
            w.SetTextureScaleOffset("_BaseMap", look.baseMapScale, look.baseMapOffset);
            w.SetFloat("_UseBaseMap", look.useBaseMap ? 1f : 0f);
            w.SetFloat("_BaseMapLevel", look.baseMapLevel);

            w.SetFloat("_Metallic", Clamp01(look.metallic));
            w.SetTexture("_MetallicMap", look.metallicMap);
            w.SetFloat("_UseMetallicMap", look.useMetallicMap ? 1f : 0f);

            w.SetFloat("_Smoothness", Clamp01(look.smoothness));
            w.SetTexture("_SmoothnessMap", look.smoothnessMap);
            w.SetFloat("_UseSmoothnessMap", look.smoothnessMap != null ? look.smoothnessMapMode : 0);

            w.SetTexture("_BumpMap", look.normalMap);
            w.SetFloat("_BumpScale", look.normalStrength);
            w.SetFloat("_UseBumpMap", look.useNormalMap ? 1f : 0f);

            w.SetFloat("_ClearCoatMask", Clamp01(look.clearCoat));
            w.SetFloat("_ClearCoatSmoothness", Clamp01(look.clearCoatSmoothness));
            w.SetKeyword("_CLEARCOAT", look.clearCoat > 0.001f);

            w.SetColor("_EmissionColor", look.emission);

            w.SetFloat("_Triplanar", look.triplanar ? 1f : 0f);
            w.SetFloat("_TriplanarTiling", look.triplanarTiling);

            w.SetFloat("_DASSurfaceAlpha", Clamp01(look.surfaceAlpha));
            w.SetFloat("_DASKeepTransparent", look.transparent ? 1f : 0f);
            SetOpacity(w, opacity, look.transparent, look.surfaceAlpha);
        }

        /// <summary>
        /// Opacity for the comparison screens (1 = solid). The material turns transparent when it is see-through
        /// (glass) or faded; it always stays two-sided so a cut part shows its inside.
        /// </summary>
        public static void SetOpacity(IClipMaterialWriter w, float opacity, bool materialIsTransparent, float surfaceAlpha)
        {
            opacity = Clamp01(opacity);
            w.SetFloat("_Opacity", opacity);
            bool transparent = materialIsTransparent || surfaceAlpha < 0.999f || opacity < 0.999f;
            w.SetFloat("_Surface", transparent ? 1f : 0f);
            // premultiplied alpha: reflections and highlights stay visible on glass
            w.SetFloat("_SrcBlend", transparent ? 1f : 1f);      // One
            w.SetFloat("_DstBlend", transparent ? 10f : 0f);     // OneMinusSrcAlpha : Zero
            w.SetFloat("_ZWrite", transparent ? 0f : 1f);
            w.SetFloat("_Cull", 0f);                             // Off
            w.SetKeyword("_SURFACE_TYPE_TRANSPARENT", transparent);
            w.SetKeyword("_ALPHAPREMULTIPLY_ON", transparent);
            w.SetRenderQueue(transparent ? 3000 : 2000);
            w.SetTag("RenderType", transparent ? "Transparent" : "Opaque");
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    }
}
