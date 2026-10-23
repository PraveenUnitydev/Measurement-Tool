// Clip section material tests. Run: sh run.sh
using System; using System.Collections.Generic; using UnityEngine; using VehicleMeasurement;
static class ClipTests {
  static int pass, fail;
  static void Check(bool ok, string what) { if (ok) pass++; else { fail++; Console.WriteLine("FAIL " + what); } }
  static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-3f; }
  static ClipLook D(Material m) { return ClipMaterialMapping.Describe(new R(m)); }
  class R : IClipMaterialReader { Material m; public R(Material m) { this.m = m; }
    public string ShaderName { get { return m.shader.name; } } public int RenderQueue { get { return m.renderQueue; } }
    public string RenderTypeTag { get { return m.GetTag("RenderType", false, ""); } }
    public bool HasFloat(string n) { return m.HasFloat(n); } public float GetFloat(string n) { return m.GetFloat(n); }
    public bool HasColor(string n) { return m.HasColor(n); } public Color GetColor(string n) { return m.GetColor(n); }
    public bool HasTexture(string n) { return m.HasTexture(n); } public Texture GetTexture(string n) { return m.GetTexture(n); }
    public Vector2 GetTextureScale(string n) { return new Vector2(1,1); } public Vector2 GetTextureOffset(string n) { return new Vector2(0,0); }
    public bool IsKeywordEnabled(string k) { return m.IsKeywordEnabled(k); } }

  static int Main() {
    Shader.Known[ClipMaterials.StandInShaderName] = GraphMaterials.StandIn();

    var chrome = D(GraphMaterials.Chrome());
    Check(chrome.source == "das-color" && Near(chrome.baseColor.r, 0.6887f) && chrome.baseColor.a == 1f, "chrome colour from _Base_Map, alpha forced to 1 (graph colours have alpha 0)");
    Check(Near(chrome.metallic, 1f) && Near(chrome.smoothness, 0.2f) && !chrome.transparent, "chrome metallic from _Metallic_Map, smoothness");

    var glass = D(GraphMaterials.Glass());
    Check(glass.transparent && Near(glass.surfaceAlpha, 0.2f), "glass is see-through with its _Alpha");
    Check(Near(glass.baseColor.r, 0.1604f) && Near(glass.clearCoat, 1f) && Near(glass.smoothness, 1f) && Near(glass.metallic, 0f), "glass colour, clear coat, smoothness (_RoughnessValues), metallic (_MetallicValues)");

    var paint = D(GraphMaterials.Car_Paint());
    Check(paint.source == "das-paint" && Near(paint.baseColor.r, 0.1132f) && Near(paint.clearCoat, 1f) && Near(paint.clearCoatSmoothness, 1f), "car paint colour + clear coat");
    Check(paint.triplanar && Near(paint.triplanarTiling, 30f) && Near(paint.smoothness, 1f) && !paint.transparent, "car paint world-space tiling, smoothness, opaque");
    var front = D(GraphMaterials.Car_Front_Paint());
    Check(Near(front.clearCoat, 1f) && front.source == "das-paint", "front paint");

    var alloy = D(GraphMaterials.ALLOY());
    Check(Near(alloy.metallic, 1f) && alloy.useNormalMap && Near(alloy.normalStrength, 0.8f) && alloy.triplanar, "alloy metallic + world-space normal map");
    Check(Near(alloy.baseColor.g, 0.6887f), "alloy colour from _Base_Map");

    var light = D(GraphMaterials.Light());
    Check(Near(light.emission.r, 1f) && light.emission.a == 1f && Near(light.baseColor.r, 0.1604f), "light emission + colour");

    var leather = D(GraphMaterials.LEATHER());
    Check(leather.source == "das-textured" && !leather.useBaseMap && Near(leather.baseColor.r, 0.4906f), "leather with _Use_BaseMap off uses _BaseColor");
    Check(leather.useMetallicMap && Near(leather.metallic, 1f) && leather.smoothnessMapMode == 1 && Near(leather.smoothness, 1f), "leather metallic map, smoothness = level x map");
    Check(leather.triplanar && Near(leather.triplanarTiling, 6f) && Near(leather.normalStrength, 0.6f), "leather tiling and normal strength");
    var lm = GraphMaterials.LEATHER(); lm.floats["_Use_BaseMap"] = 1f;
    var leatherTex = D(lm);
    Check(leatherTex.useBaseMap && leatherTex.baseMap != null && Near(leatherTex.baseMapLevel, 1.1f) && Near(leatherTex.baseColor.r, 1f), "leather with _Use_BaseMap on: texture x _BaseColorLevel");
    var noTex = D(GraphMaterials.LEATHER(false));
    Check(!noTex.useMetallicMap && Near(noTex.metallic, 0f) && noTex.smoothnessMapMode == 0 && !noTex.useNormalMap, "leather without textures assigned still works");

    var plastic = D(GraphMaterials.Plastic());
    Check(plastic.useNormalMap, "plastic normal map");
    foreach (var make in new Func<bool, Material>[] { GraphMaterials.Plastic_FrontSide, GraphMaterials.Rubber, GraphMaterials.Seat })
    { var l = D(make(true)); Check(l.source == "das-textured" && l.triplanar && !l.transparent, "textured family: " + make.Method.Name); }

    // URP Lit (transparent) and Standard
    var urp = new Material(new Shader { name = "Universal Render Pipeline/Lit" });
    urp.colors["_BaseColor"] = new Color(1, 0, 0, 0.5f); urp.texProps.Add("_BaseMap"); urp.textures["_BaseMap"] = new Texture();
    urp.floats["_Metallic"] = 0.3f; urp.floats["_Smoothness"] = 0.7f; urp.floats["_Surface"] = 1f; urp.texProps.Add("_BumpMap"); urp.floats["_BumpScale"] = 1f;
    var u = D(urp);
    Check(u.source == "urp" && u.useBaseMap && u.transparent && Near(u.surfaceAlpha, 0.5f) && !u.triplanar && !u.useNormalMap, "URP Lit: texture, alpha from colour when transparent, UV mapping");
    var std = new Material(new Shader { name = "Standard" }); std.colors["_Color"] = new Color(0, 1, 0, 1); std.floats["_Glossiness"] = 0.4f; std.floats["_Mode"] = 0;
    var st = D(std);
    Check(st.source == "standard" && Near(st.smoothness, 0.4f) && !st.transparent, "Standard shader");

    // Stand-in material from the real property list
    var gm = ClipMaterials.CreateFor(GraphMaterials.Glass());
    Check(gm != null && ClipMaterials.IsStandIn(gm), "stand-in created");
    Check(gm.GetFloat("_Surface") == 1f && gm.GetFloat("_DstBlend") == 10f && gm.GetFloat("_ZWrite") == 0f && gm.renderQueue == 3000 && gm.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"), "glass stand-in is transparent (premultiplied)");
    Check(gm.GetFloat("_Cull") == 0f && gm.IsKeywordEnabled("_CLEARCOAT") && Near(gm.GetFloat("_DASSurfaceAlpha"), 0.2f), "glass stand-in two-sided, clear coat on");
    ClipMaterials.SetOpacity(gm, 1f);
    Check(gm.GetFloat("_Surface") == 1f, "glass stays see-through at full opacity");

    var pm = ClipMaterials.CreateFor(GraphMaterials.Car_Paint());
    Check(pm.GetFloat("_Surface") == 0f && pm.GetFloat("_ZWrite") == 1f && pm.renderQueue == 2000 && pm.GetTag("RenderType", false, "") == "Opaque", "paint stand-in opaque");
    Check(pm.GetFloat("_Triplanar") == 1f && Near(pm.GetFloat("_TriplanarTiling"), 30f) && pm.IsKeywordEnabled("_CLEARCOAT"), "paint stand-in triplanar + clear coat");
    ClipMaterials.SetOpacity(pm, 0.4f);
    Check(pm.GetFloat("_Surface") == 1f && Near(pm.GetFloat("_Opacity"), 0.4f) && pm.renderQueue == 3000, "fading makes it transparent");
    ClipMaterials.SetOpacity(pm, 1f);
    Check(pm.GetFloat("_Surface") == 0f && pm.renderQueue == 2000 && !pm.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), "back to solid");
    ClipMaterials.Tint(pm, new Color(0, 0, 1, 0.3f));
    Check(pm.GetColor("_BaseColor").b == 1f && pm.GetColor("_BaseColor").a == 1f, "tint keeps alpha 1 (opacity is separate)");
    var chromeM = ClipMaterials.CreateFor(GraphMaterials.Chrome());
    Check(!chromeM.IsKeywordEnabled("_CLEARCOAT") && Near(chromeM.GetFloat("_Metallic"), 1f), "chrome stand-in: no clear coat, metallic 1");

    // Swapping a vehicle: natives kept, one stand-in per shared material, restore puts the originals back
    var native = new Material(new Shader { name = "Shader Graphs/SG_Car_Paint_Material" }); native.floats[ClipMaterials.NativeClipProperty] = 1f;
    var shared = GraphMaterials.Chrome();
    var car = new GameObject("car"); var body = new GameObject("body"); var wheel = new GameObject("wheel"); car.children.Add(body); car.children.Add(wheel);
    var r1 = body.AddComponent<MeshRenderer>(); r1.sharedMaterials = new[] { native, shared };
    var r2 = wheel.AddComponent<MeshRenderer>(); r2.sharedMaterials = new[] { shared, null };
    var originals = new Dictionary<Renderer, Material[]>(); var cache = new Dictionary<Material, Material>();
    int nat; int swapped = ClipMaterials.Apply(car, originals, cache, out nat);
    Check(nat == 1 && r1.sharedMaterials[0] == native, "material whose graph clips is left alone");
    Check(swapped == 2 && cache.Count == 1 && r1.sharedMaterials[1] == r2.sharedMaterials[0] && ClipMaterials.IsStandIn(r2.sharedMaterials[0]), "one stand-in shared by both parts");
    Check(r2.sharedMaterials[1] == null, "empty slot stays empty");
    ClipMaterials.Apply(car, originals, cache, out nat);
    Check(cache.Count == 1 && originals[r1][1] == shared, "re-applying reuses stand-ins and keeps the true originals");
    ClipMaterials.Restore(originals);
    Check(r1.sharedMaterials[0] == native && r1.sharedMaterials[1] == shared && r2.sharedMaterials[0] == shared && r2.sharedMaterials[1] == null, "restore");
    var standIn = cache[shared]; ClipMaterials.DestroyAll(cache);
    Check(cache.Count == 0 && standIn.destroyed, "stand-ins destroyed");
    int nat2; ClipMaterials.Apply(car, new Dictionary<Renderer, Material[]>(), new Dictionary<Material, Material>(), out nat2, true);
    Check(nat2 == 0 && ClipMaterials.IsStandIn(r1.sharedMaterials[0]), "comparison can force stand-ins (to tint) even for natively clipping parts");

    Console.WriteLine("Passed " + pass + ", failed " + fail);
    return fail == 0 ? 0 : 1;
  }
}
