// Builds Assets/Game/SnowPlague/SnowPlague.unity (설원 역병 아레나, snow-arena-spec.md) from the SnowPlague kit:
// materials and FBX remaps, a flat 112 x 88 m snow arena on a vertex-coloured ground mesh (gravel / wet soil / leaf
// litter / snow allowed), a solid border of dead forest, snowbanks and rocks (east/west/north, a cliff with a fog sea
// past the north edge), FG0 red pines and branches along the camera-side (south) edge with the dither shader, 6
// vignette clusters in the outer ring (stone-lantern yard, 거적 corpse spots, broken cart, jars/stakes) with the
// middle 70% left open, ~10k instanced scatter (no shadows), stone lanterns (2200 K spots with cookies, 3 with
// realtime shadows), height fog, fog sheets, purple boundary wall, falling snow, post-processing, camera at the
// RoomRun angle, invisible walls on the edge and the RoomRun dev room ("snow").
// No colliders inside the play area except the ground. Re-running rebuilds the scene.
// Driven without menus: SPRemote (ArtSource/SnowPlague/sp_cmd.txt).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SnowPlague;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static partial class SPBuilder
{
    // 11-11 redo 2 (parallel workers): each lives in its own SPBuilder.<Part>.cs; unimplemented hooks compile away.
    static partial void BuildArchitecture();                 // SPBuilder.Architecture.cs: bridge/walkway, gate, wall, tall lanterns, cliff ledge
    static partial void BuildVegetationPass();               // SPBuilder.Vegetation.cs: dense brush, dry grass, framing pines
    static partial void ConfigureGround(Material ground);    // SPBuilder.Ground.cs: ground textures/params
    static partial void BuildGroundClutter();                // SPBuilder.Ground.cs: gravel/rock/leaf/pebble scatter
    static partial void ConfigureLook(VolumeProfile profile, Light moon);   // SPBuilder.Look.cs: grade, lights, blood, embers
    static partial void BuildIce();                          // SPBuilder.Ice.cs: frozen river (ice + snow banks), SPIceReflect
    static partial void BuildMegascans();                    // SPBuilder.Megascans.cs: Megascans props, trees, shrubs, walls, cliffs placed in the scene

    public const string Root = "Assets/Game/SnowPlague";
    public const string ScenePath = Root + "/SnowPlague.unity";
    const string Models = Root + "/Models/";
    const string TexDir = Root + "/Textures/";
    const string PH = Root + "/ThirdParty/PolyHaven/";
    const string MatDir = Root + "/Materials";
    const string Gen = Root + "/Generated";
    public const string RoomId = "snow";

    // play area: 112 x 88 m (2x per side like the other fields), centred on the origin, camera side = south (-Z)
    public const float HX = 56f, HZ = 44f;
    const float CliffZ = 53f;   // the ground drops away north of this line into the fog sea
    public static bool InArena(float x, float z, float margin = 0f) => Mathf.Abs(x) < HX - margin && Mathf.Abs(z) < HZ - margin;

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static System.Random rng;
    static Transform root;
    static string LogPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "SnowPlague", "sp_log.txt");
    static void Log(string s) { Directory.CreateDirectory(Path.GetDirectoryName(LogPath)); File.AppendAllText(LogPath, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n"); }

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static float Rn() => (float)rng.NextDouble();

    // ------------------------------------------------------------------ ground height
    // Flat inside the arena. Outside it rises gently into snowbanks (up to ~1.6 m); north of the cliff line it drops
    // ~9 m to the fog sea.
    public static float Height(float x, float z)
    {
        float ox = Mathf.Max(0, Mathf.Abs(x) - HX), oz = Mathf.Max(0, Mathf.Abs(z) - HZ);
        float o = Mathf.Sqrt(ox * ox + oz * oz);
        float h = Mathf.Clamp01(o / 10f) * (0.6f + 1.0f * Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 11f));
        if (z > CliffZ) h -= Mathf.SmoothStep(0, 1, (z - CliffZ) / 5f) * 9.5f + Mathf.PerlinNoise(x * 0.2f, z * 0.2f) * 0.6f * Mathf.Clamp01(z - CliffZ);
        return h;
    }

    // ------------------------------------------------------------------ textures and materials
    static Texture2D Tex(string path, bool normalMap = false, bool linear = false, bool clamp = false, int max = 2048)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Log("MISSING texture " + path); return null; }
        bool dirty = false;
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (imp.textureType != type) { imp.textureType = type; dirty = true; }
        if (!normalMap && imp.sRGBTexture == linear) { imp.sRGBTexture = !linear; dirty = true; }
        if (imp.maxTextureSize != max) { imp.maxTextureSize = max; dirty = true; }
        var wrap = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        if (imp.wrapMode != wrap) { imp.wrapMode = wrap; dirty = true; }
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static Texture2D T(string file, bool normalMap = false, bool linear = false) => Tex(TexDir + file, normalMap, linear);

    static Material Mat(string name, string shader, System.Action<Material> init = null)
    {
        var path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find(shader);
        if (!sh) { Log("MISSING shader " + shader + " (for " + name + ")"); return null; }
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        init?.Invoke(m);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static Material Prop(string name, Texture2D albedo, Texture2D normal, Color tint, float smooth = 0.15f, float desat = 0.4f, bool twoSided = false)
        => Mat(name, "SnowPlague/Prop", m =>
        {
            m.SetTexture("_BaseMap", albedo ? albedo : Texture2D.whiteTexture);
            m.SetTexture("_BumpMap", normal);
            m.SetColor("_Tint", tint); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Desaturate", desat);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetColor("_EmissionColor", Color.black); m.SetFloat("_Flicker", 0f);
        });

    static Material Veg(string name, Texture2D albedo, Texture2D normal, Color tint, bool cards, float wither = 0f, float bend = 0.4f)
        => Mat(name, "SnowPlague/Vegetation", m =>
        {
            m.SetTexture("_BaseMap", albedo ? albedo : Texture2D.whiteTexture);
            m.SetFloat("_UseTex", albedo ? 1f : 0f);
            if (normal) { m.SetTexture("_BumpMap", normal); m.SetFloat("_UseNormal", 1f); } else m.SetFloat("_UseNormal", 0f);
            m.SetColor("_Tint", tint); m.SetFloat("_Wither", wither); m.SetFloat("_Bend", bend);
            m.SetFloat("_AlphaClip", cards ? 1f : 0f);
            if (cards) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Wrap", 0.35f);
        });

    static Material FG(string name, Texture2D tex, bool cards)
        => Mat(name, "SnowPlague/Foreground", m =>
        {
            m.SetTexture("_BaseMap", tex ? tex : Texture2D.whiteTexture);
            m.SetFloat("_UseTex", tex ? 1f : 0f);
            // near-black silhouettes (11-11 first look: #3b4651 top + bright atlas detail read as a flat grey cut-out)
            m.SetColor("_InkCol", Hex("#07090b")); m.SetColor("_TopCol", Hex("#1c232a"));
            m.SetFloat("_GradBase", -0.5f); m.SetFloat("_GradTop", 16f); m.SetFloat("_Detail", 0.12f);
            // frost off: seen from the top-down camera every needle card faces up, so 0.3 frost turned the whole crown
            // into a flat pale cut-out (11-11 first look)
            // 11-11 redo: snow-laden (Kido: snow on every branch), in clumps (shader noise), dimmer than the lit ground snow
            m.SetFloat("_Rim", 0.2f); m.SetFloat("_Frost", 0.85f); m.SetColor("_FrostCol", Hex("#6b7884")); m.SetFloat("_Bend", 0.12f);
            m.SetFloat("_AlphaClip", cards ? 1f : 0f);
            if (cards) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            // needle cards stand edge-on to the top-down camera, so the fresnel is ~1 on every card and the amber rim
            // lit the whole crown (11-11): rim only on trunks and branches
            m.SetFloat("_Rim", cards ? 0f : 0.12f);
        });

    static void BuildMaterials()
    {
        foreach (var d in new[] { MatDir, Gen })
            if (!AssetDatabase.IsValidFolder(d)) AssetDatabase.CreateFolder(Root, d.Substring(Root.Length + 1));
        var atlas = T("SP_FoliageAtlas.png");
        { var imp = (TextureImporter)AssetImporter.GetAtPath(TexDir + "SP_FoliageAtlas.png"); if (imp && !imp.alphaIsTransparency) { imp.alphaIsTransparency = true; imp.SaveAndReimport(); } }
        var barkA = T("bark_brown_02_diff_1k.jpg"); var barkN = T("bark_brown_02_nor_gl_1k.jpg", true);
        var pineA = T("pine_bark_diff_1k.jpg"); var pineN = T("pine_bark_nor_gl_1k.jpg", true);
        var rockA = T("dark_rock_02_diff_1k.jpg"); var rockN = T("dark_rock_02_nor_gl_1k.jpg", true);
        var woodA = T("wood_planks_grey_diff_1k.jpg"); var woodN = T("wood_planks_grey_nor_gl_1k.jpg", true);
        var strawA = T("ThatchedRoof002A_diff_1k.jpg"); var strawN = T("ThatchedRoof002A_nor_gl_1k.jpg", true);
        var clothA = T("Fabric030_diff_1k.jpg"); var clothN = T("Fabric030_nor_gl_1k.jpg", true);

        // kit material slots (BlenderSource~/build_kit*.py)
        Veg("SP_Bark", barkA, barkN, Hex("#8d8a86"), false, 0f, 0.05f);
        Veg("SP_PineBark", pineA, pineN, Hex("#9a7f70"), false, 0f, 0.05f);
        Veg("SP_Needles", atlas, null, Hex("#5d6a5c"), true, 0.15f, 0.35f);
        Veg("SP_GrassCard", atlas, null, Hex("#b9a45f"), true, 0.55f, 0.6f);   // dry #b9a45f toward grey
        Veg("SP_Grass", null, null, Hex("#b9a45f"), false, 0.55f, 0.6f);
        Veg("SP_Maple", atlas, null, Hex("#a8402e"), true, 0.1f, 0.3f);       // a few snow-covered maples
        Prop("SP_Wood", woodA, woodN, Hex("#8c8781"), 0.12f, 0.45f);
        Prop("SP_Straw", strawA, strawN, Hex("#5e5444"), 0.05f, 0.5f, true).SetFloat("_SnowBias", -0.35f);   // 거적: dull grey-brown straw (read white 11-11), snow only on top in patches
        Prop("SP_Rope", strawA, null, Hex("#6f6452"), 0.05f, 0.4f);
        Prop("SP_Cloth", clothA, clothN, Hex("#7d776d"), 0.08f, 0.55f, true).SetFloat("_SnowBias", -0.5f);
        Prop("SP_Iron", null, null, Hex("#2a2d31"), 0.42f, 0.2f);
        Prop("SP_Clay", null, rockN, Hex("#3a2620"), 0.5f, 0.15f);              // onggi jars
        Prop("SP_Stone", rockA, rockN, Hex("#9aa0a6"), 0.12f, 0.5f);
        Prop("SP_StoneLantern", rockA, rockN, Hex("#a5a9ad"), 0.12f, 0.5f);
        Prop("SP_Bone", null, null, Hex("#c9c2b2"), 0.2f, 0.3f);
        Prop("SP_Snow", null, null, Hex("#aeb8c2"), 0.35f, 0.2f);   // #e4eaee read as paper-white cut-outs on the graded ground
        var glow = Prop("SP_LanternGlow", null, null, Hex("#ffb36a"), 0.2f, 0f);
        glow.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.14f) * 6f); glow.SetFloat("_Flicker", 1f);
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        Mat("SP_LightCone", "SnowPlague/LightCone", m =>
        {
            m.SetTexture("_NoiseTex", Tex(TexDir + "SP_FogNoise.png", false, true));
            m.SetColor("_Col", new Color(1.6f, 0.75f, 0.3f)); m.SetFloat("_Intensity", 0.3f);
        });
        // foreground (FG0, camera-side edge): unlit gradient + dither hole
        FG("SP_FG_PineBark", pineA, false); FG("SP_FG_Bark", barkA, false); FG("SP_FG_Needles", atlas, true);

        // ground
        Mat("SP_Ground", "SnowPlague/Ground", m =>
        {
            void L(string slot, string n) { m.SetTexture(slot + "A", T($"SP_G_{n}_A.jpg")); m.SetTexture(slot + "N", T($"SP_G_{n}_N.jpg", false, true)); m.SetTexture(slot + "H", T($"SP_G_{n}_H.jpg", false, true)); }
            L("_Snow", "Snow"); L("_Gravel", "Gravel"); L("_Mud", "Mud"); L("_Leaf", "Leaves");
            m.SetVector("_Tiles", new Vector4(4, 5, 6, 4));
            m.SetColor("_SnowTint", new Color(0.83f, 0.86f, 0.89f));   // albedo stays in sRGB 200-235
            m.SetFloat("_RedLeaves", 0.3f); m.SetFloat("_Wet", 0.8f); m.SetFloat("_SnowFill", 1.4f);
            m.SetFloat("_BlendDepth", 0.3f);
            // 11-11 redo: placement comes from the painted masks (BuildMasks); hex-tiled layers, glints, icy and wet gloss
            m.SetFloat("_SnowBias", 0f); m.SetFloat("_SnowFill", 0.8f); m.SetFloat("_Calm", 0.5f);
            m.SetColor("_SoilTint", new Color(0.72f, 0.7f, 0.68f)); m.SetColor("_GravelTint", new Color(0.8f, 0.8f, 0.8f));
            m.SetColor("_ShadeTint", new Color(0.72f, 0.8f, 0.92f));
            m.SetFloat("_HexScale", 1.4f); m.SetFloat("_Sparkle", 6f); m.SetFloat("_SnowFlatten", 0.45f); m.SetFloat("_Macro", 1f);
            m.SetFloat("_IceSmooth", 0.88f); m.SetFloat("_SnowSmooth", 0.38f); m.SetFloat("_NormalStrength", 1f);
            ConfigureGround(m);
        });

        // fog, wall, decals, shadows
        var noise = Tex(TexDir + "SP_FogNoise.png", false, true);
        Mat("SP_FogSheetLow", "SnowPlague/FogSheet", m => { m.SetTexture("_NoiseTex", noise); m.SetColor("_Col", Hex("#9aabb8")); m.SetFloat("_Density", 0.16f); m.SetVector("_Speed", new Vector4(0.35f, 0.1f, -0.2f, 0.25f)); m.SetFloat("_ArenaClear", 0.6f); });
        Mat("SP_FogSheetMid", "SnowPlague/FogSheet", m => { m.SetTexture("_NoiseTex", noise); m.SetColor("_Col", Hex("#8e9fad")); m.SetFloat("_Density", 0.1f); m.SetVector("_Scale", new Vector4(26, 9, 0, 0)); m.SetVector("_Speed", new Vector4(-0.25f, 0.12f, 0.15f, -0.2f)); m.SetFloat("_ArenaClear", 0.7f); });
        Mat("SP_FogSheetHigh", "SnowPlague/FogSheet", m => { m.SetTexture("_NoiseTex", noise); m.SetColor("_Col", Hex("#7e8f9d")); m.SetFloat("_Density", 0.35f); m.SetVector("_Scale", new Vector4(30, 12, 0, 0)); m.SetFloat("_ArenaClear", 0.95f); });
        Mat("SP_FogSea", "SnowPlague/FogSheet", m => { m.SetTexture("_NoiseTex", noise); m.SetColor("_Col", Hex("#9aabb8")); m.SetFloat("_Density", 1.25f); m.SetVector("_Scale", new Vector4(40, 16, 0, 0)); m.SetVector("_Speed", new Vector4(0.12f, 0.03f, -0.08f, 0.05f)); m.SetFloat("_ArenaClear", 0f); m.SetVector("_CamFade", new Vector4(2, 6, 0, 0)); });
        Mat("SP_HeightFog", "SnowPlague/HeightFog", m =>
        {
            m.SetTexture("_NoiseTex", noise);
            m.SetColor("_NearCol", Hex("#9aabb8")); m.SetColor("_FarCol", Hex("#151d29"));
            m.SetFloat("_Falloff", 0.35f); m.SetFloat("_Density", 0.02f); m.SetFloat("_Start", 22f); m.SetFloat("_FarDist", 90f); m.SetFloat("_MaxFog", 0.6f);
        });
        Mat("SP_BoundaryWall", "SnowPlague/BoundaryWall", m =>
        {
            m.SetTexture("_NoiseTex", noise);
            float perim = 2 * (2 * HX + 2 * HZ);
            m.SetVector("_Tiling", new Vector4(perim / 6f, 1.5f, perim / 3.5f, 3f));
            m.SetFloat("_Intensity", 0.07f);   // tr21   // r8: the purple band hid the bridge and gate on the north edge (tr12: 0.15 -> 0.1)
        });
        Mat("SP_Blood", "SnowPlague/BloodDecal", m => m.SetTexture("_BloodTex", T("SP_Blood_MS2.png", false, true)));   // r10: atlas composed from the Megascans Blood Stain scan pieces (ArtSource/SnowPlague/gen_blood_atlas.py), linear data: R thick, G/B scan colour, A opacity
        Mat("SP_BlobShadow", "SnowPlague/BlobShadow", m => m.SetFloat("_Stretch", 0f));
        Mat("SP_BlobStretch", "SnowPlague/BlobShadow", m => { m.SetFloat("_Stretch", 1f); m.EnableKeyword("_STRETCH_ON"); });
        var sil = Shader.Find("SnowPlague/OccludedSilhouette");
        if (sil) Mat("SP_Silhouette", "SnowPlague/OccludedSilhouette");
        else Log("NOTE no SnowPlague/OccludedSilhouette shader: occluded-player silhouette off");
        Mat("SP_SnowFlake", "Universal Render Pipeline/Particles/Unlit", m =>
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/AbandonedVillage/Textures/AV_Glow.png"));
            m.SetColor("_BaseColor", new Color(0.92f, 0.95f, 1f, 0.85f));
            m.SetFloat("_Surface", 1f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent; m.SetOverrideTag("RenderType", "Transparent");
        });
        AssetDatabase.SaveAssets();
    }

    static readonly string[] KitSlots = { "SP_Bark", "SP_PineBark", "SP_Needles", "SP_GrassCard", "SP_Grass", "SP_Maple", "SP_Wood", "SP_Straw", "SP_Rope", "SP_Cloth", "SP_Iron", "SP_Clay", "SP_Stone", "SP_StoneLantern", "SP_Bone", "SP_Snow", "SP_LanternGlow", "SP_LightCone" };

    static void RemapModels()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; changed = true; }
            var map = imp.GetExternalObjectMap();
            foreach (var n in KitSlots)
            {
                if (!mats.TryGetValue(n, out var m) || !m) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (map.TryGetValue(id, out var cur) && cur == m) continue;
                imp.AddRemap(id, m); changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
        // Poly Haven: one SnowPlague/Prop material per source material, from the diffuse/normal maps beside the FBX
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/ThirdParty/PolyHaven" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var names = new HashSet<string>();
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) if (m) names.Add(m.name);
            bool changed = imp.importAnimation;
            imp.importAnimation = false;
            foreach (var n in names)
            {
                if (n.StartsWith("SPPH_")) continue;
                string diff = null, nor = null;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fn = Path.GetFileName(f);
                    if (fn.EndsWith(".meta")) continue;
                    if (fn.Contains("_diff_") && (fn.StartsWith(n + "_diff") || diff == null)) diff = dir + "/" + fn;
                    if (fn.Contains("_nor_gl_") && (fn.StartsWith(n + "_nor") || nor == null)) nor = dir + "/" + fn;
                }
                bool branches = n.Contains("branch") || n.Contains("root");
                var mm = Prop("SPPH_" + n, diff != null ? Tex(diff, false, false, false, 1024) : null, nor != null ? Tex(nor, true, false, false, 1024) : null,
                              branches ? Hex("#8a8580") : Hex("#a3a7ab"), 0.12f, 0.45f, branches);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mm); changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ placement
    static GameObject Src(string model)
    {
        if (model.StartsWith("SK:")) return SkSrc(model);   // Sketchfab trees (SPBuilder.Vegetation.cs)
        var path = model.StartsWith("PH:") ? PH + model.Substring(3) + "/" + model.Substring(3) + ".fbx" : Models + model + ".fbx";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!src) Log("MISSING model " + path);
        return src;
    }

    static GameObject Place(string model, float x, float z, float yaw, float scale, Transform parent, bool shadows = true, float sink = 0.04f)
    {
        if (model.StartsWith("SK:")) { model = SkResolve(model); scale = SkScaleFix(model, scale); }   // hero pine budget / size
        var src = Src(model); if (!src) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(new Vector3(x, Height(x, z) - sink, z), Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale * (model.StartsWith("SK:") ? SkNorm(model) : 1f);   // SK: scale 1 = the tree's reference height
        if (model.StartsWith("SK:"))
        {
            // single-mesh FBX roots carry the Blender axis fix: keep it, yaw on top (tr1: dead trees lay on their side)
            float k = scale * SkNorm(model);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * src.transform.localRotation;
            go.transform.localScale = src.transform.localScale * k;
            go.transform.position += Vector3.up * (-SkBase(model) * k);   // pivot may not sit at the base
            SkPost(go, model, x, z);
        }   // pivot may not sit at the base
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        return go;
    }

    static Transform Group(string name, Transform parent = null) { var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g; }

    // ------------------------------------------------------------------ build
    public static void Build()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != ScenePath) { Log("ABORT unsaved changes in " + active.path); return; }
        rng = new System.Random(2611);
        mats.Clear();
        BuildMaterials();
        RemapModels();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("SnowPlague").transform;
        var field = root.gameObject.AddComponent<SPField>();
        field.arena = new Vector2(HX * 2, HZ * 2);
        field.snowAmount = 0.95f;   // 11-11 redo: snow caps on every up-facing surface

        BuildGround();
        var lanterns = new List<Light>();
        BuildBorder();
        BuildArchitecture();
        BuildForeground();
        BuildVignettes(lanterns);
        lanterns.AddRange(archLanterns);
        BuildMasks();
        int scattered = BuildScatter();
        BuildVegetationPass();
        BuildMegascans();
        BuildGroundClutter();
        BuildIce();
        BuildFogAndVfx();
        BuildLightAndPost(field, lanterns);
        BuildWalls();
        VgThinShadows();   // tr15: tall thin poles drew the long straight line shadow across the lantern yard
        BuildLandmarks();   // 11-12 (Kido): big props outside the arena - 솟을삼문, jangseung, pagoda, hanok walls (SPBuilder.Landmarks.cs)
        KLSwap();           // tr28: Korean stone lanterns replace the Japanese ones, all moved past the boundary (SPBuilder.KoreanLantern.cs)
        LookLanternsLate();   // look8: Korean-lantern spots/roof lights get the look values (SPBuilder.Look.cs)
        HeroCornerVignette(); // tr36: ss5-like corner - corpse vignette by the west gate (SPBuilder.KoreanLantern.cs)
        CorpseSwap();         // tr41: covered-body models in place of the flat straw-mat lumps
        BorderRocks();        // tr44: irregular rock clusters along the edges instead of the straight wall rows (Kido)
        R13Pass();          // r13 (11-13): skewed bridges, blood redo, KCISA houses at the edges (SPBuilder.R13.cs)
        ArenaCheck();       // arena rule: no colliders inside, list anything tall that is not vegetation
        VgLineShadowProbe();   // tr20: finds and un-shadows the caster of the long line shadow in view 02
        LMGateFill();            // tr21: soft warm fill on the 솟을삼문 front (it stands in the moon shadow)
        VgPost32();              // tr32: dead-conifer bark without snow, dark wet straw mats, view-15 rectangle probe
        VgProbe14b();            // tr39: finds and removes the white stick in view 14
        VgPost45();              // tr45: jars varied + half-buried, clay/wood textures, wheel sunk
        VgProbe16();             // tr45: names the straight hairline in view 16 (logs only)
        // VgRectShadowProbe();  // tr23: exact moon-ray probe - Object_8 is hit but un-shadowing it does not change the rectangle (not a shadow); off so Object_8 keeps its shadow

        lanterns.RemoveAll(l => !l);
        foreach (var sl in root.GetComponentsInChildren<Light>(true)) if (sl.name == "LanternSpot" && !lanterns.Contains(sl)) lanterns.Add(sl);   // Megascans lanterns too (warm rim + flicker)
        field.lanterns = lanterns.ToArray();
        field.heightFog = mats.GetValueOrDefault("SP_HeightFog");
        field.silhouette = mats.GetValueOrDefault("SP_Silhouette");
        field.blobShadow = mats.GetValueOrDefault("SP_BlobShadow");
        field.blobStretch = mats.GetValueOrDefault("SP_BlobStretch");

        int blockers = CheckArena();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddRoomRunRoom();
        Log($"BUILT {ScenePath}: scatter={scattered} lanterns={lanterns.Count} (shadowed {lanterns.Count(l => l.shadows != LightShadows.None)}) collidersInArena={blockers}");
    }

    // ------------------------------------------------------------------ ground (vertex colour: R gravel, G wet soil, B leaves, A snow allowed)
    static readonly List<Vector3> bare = new List<Vector3>();   // (x, z, radius): trodden / no snow (yards, corpse spots)
    static readonly List<Vector3> muddy = new List<Vector3>();

    static void BuildGround()
    {
        bare.Clear(); muddy.Clear();
        // cluster spots are known up front so the ground can be trodden around them
        foreach (var c in Clusters) { bare.Add(new Vector3(c.x, c.y, c.z * (c == Clusters[0] ? 0.3f : 0.9f))); if (c.w > 0.5f) muddy.Add(new Vector3(c.x, c.y, c.z * 0.7f)); }
        const float x0 = -105f, x1 = 105f, z0 = -72f, z1 = 70f, cell = 0.5f;
        int nx = Mathf.CeilToInt((x1 - x0) / cell), nz = Mathf.CeilToInt((z1 - z0) / cell);
        var v = new List<Vector3>((nx + 1) * (nz + 1)); var col = new List<Color>((nx + 1) * (nz + 1)); var t = new List<int>(nx * nz * 6);
        for (int j = 0; j <= nz; j++)
        for (int i = 0; i <= nx; i++)
        {
            float x = x0 + i * cell, z = z0 + j * cell;
            v.Add(new Vector3(x, Height(x, z), z));
            float ox = Mathf.Max(0, Mathf.Abs(x) - HX), oz = Mathf.Max(0, Mathf.Abs(z) - HZ), o = Mathf.Sqrt(ox * ox + oz * oz);
            float inEdge = Mathf.Clamp01(1f - Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z)) / 10f);   // 1 at the arena edge
            float gravel = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.06f + 40f, z * 0.06f + 2f) - 0.52f) * 3f);
            // 11-11 first look: mud and leaf patches read as big hard-edged dark blobs on the snow; smaller and softer
            float mud = Mathf.Clamp01((Mathf.PerlinNoise(x * 0.06f + 9f, z * 0.06f + 70f) - 0.66f) * 2f) * 0.45f;
            // leaves only out under the border trees; inside the arena they buried the snow in a 10 m band along the edge
            float leaf = Mathf.Clamp01(Mathf.Clamp01((o - 1f) / 8f) * 0.8f * (0.3f + 0.7f * Mathf.PerlinNoise(x * 0.11f, z * 0.11f + 30f)));
            gravel = Mathf.Max(gravel, inEdge * 0.25f * Mathf.PerlinNoise(x * 0.2f + 5f, z * 0.2f));
            float snow = 1f;
            foreach (var b in bare)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(b.x, b.y)) / b.z
                          + (Mathf.PerlinNoise(x * 0.35f + 17f, z * 0.35f - 4f) - 0.5f) * 0.9f + (Mathf.PerlinNoise(x * 1.1f, z * 1.1f + 8f) - 0.5f) * 0.35f;   // tr30 (QA #8): ragged, not a disc
                snow = Mathf.Min(snow, Mathf.Lerp(0.5f, 1f, Mathf.SmoothStep(0, 1, (d - 0.3f) / 1.2f)));
                gravel = Mathf.Max(gravel, 0.6f * (1 - Mathf.SmoothStep(0, 1, d)));
            }
            foreach (var mm in muddy) mud = Mathf.Max(mud, 0.9f * (1 - Mathf.Clamp01(Vector2.Distance(new Vector2(x, z), new Vector2(mm.x, mm.y)) / mm.z)));
            if (z < -HZ - 1f) snow = Mathf.Min(snow, 0.55f + 0.3f * Mathf.PerlinNoise(x * 0.2f, z * 0.2f));   // under the FG pines
            col.Add(new Color(gravel, mud, leaf, snow));
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
            t.Add(a); t.Add(d); t.Add(b); t.Add(b); t.Add(d); t.Add(e);
        }
        var mesh = new Mesh { name = "SP_Ground", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetColors(col); mesh.SetTriangles(t, 0);
        var uv = new List<Vector2>(v.Count); foreach (var p in v) uv.Add(new Vector2(p.x, p.z)); mesh.SetUVs(0, uv);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        var path = Gen + "/SP_Ground.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject("Ground"); go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mats["SP_Ground"]; r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);   // ContributeGI back on with the lightmap bake
    }

    // ------------------------------------------------------------------ ground masks (redo 11-11)
    // Two RGBA masks over the whole ground mesh, ~0.1 m per texel (SPGroundSurface.hlsl):
    //   A: R snow cover, G wet soil, B gravel, A drift (snow piled against props, rocks, trees, lanterns)
    //   B: R trodden path, G path direction (0..1 = 0..pi), B drag marks / cart ruts, A ice
    // Majority snow; dark wet soil and gravel break through in organic patches, round the corpse spots, the cart and
    // along the footpaths. Drifts are also raised into the ground mesh so prop bases sit buried in real mounds.
    static readonly Rect MaskRect = new Rect(-105f, -72f, 210f, 142f);
    const int MW = 2048, MH = 1385;
    static float[] mSnow, mWet, mGravel, mDrift, mPath, mDir, mDrag, mIce;

    // footpaths through the arena (people walked to the lantern yard, carried the dead, dragged the cart)
    static readonly Vector2[][] Paths =
    {
        new[] { new Vector2(-8f, -60f), new Vector2(-12f, -30f), new Vector2(-22f, -6f), new Vector2(-33f, 14f), new Vector2(-42f, 28f), new Vector2(-47f, 40f), new Vector2(-52f, 58f) },
        new[] { new Vector2(-70f, -14f), new Vector2(-46f, -12f), new Vector2(-22f, -6f), new Vector2(4f, 2f), new Vector2(26f, 7f), new Vector2(46f, 9f), new Vector2(70f, 14f) },
        new[] { new Vector2(-6f, 52f), new Vector2(-6f, 38f), new Vector2(-2f, 18f), new Vector2(4f, 2f), new Vector2(10f, -16f), new Vector2(17f, -37f), new Vector2(22f, -62f) },
        new[] { new Vector2(46f, 9f), new Vector2(44f, 22f), new Vector2(41f, 34f), new Vector2(43f, 56f) },
    };
    // cart ruts follow path 1 east of the centre; the corpse drag trail runs from the west spot to the edge
    static readonly Vector2[] Ruts = { new Vector2(4f, 2f), new Vector2(26f, 7f), new Vector2(46f, 9f) };
    static readonly Vector2[] DragTrail = { new Vector2(-47f, -12f), new Vector2(-52f, -10.5f), new Vector2(-57f, -11.5f), new Vector2(-64f, -10f) };

    static float SegDist(Vector2 p, Vector2[] line, out float dirT)
    {
        float best = 1e9f; dirT = 0;
        for (int i = 0; i + 1 < line.Length; i++)
        {
            var a = line[i]; var b = line[i + 1]; var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = (p - (a + ab * t)).magnitude;
            if (d < best) { best = d; float ang = Mathf.Atan2(ab.y, ab.x); if (ang < 0) ang += Mathf.PI; dirT = ang / Mathf.PI; }
        }
        return best;
    }

    static float Fbm(float x, float z, float f, int oct = 4)
    {
        float v = 0, a = 0.5f, s = 0;
        for (int i = 0; i < oct; i++) { v += a * Mathf.PerlinNoise(x * f + 31.7f * i, z * f + 11.3f * i); s += a; f *= 2.03f; a *= 0.5f; }
        return v / s;
    }

    static void BuildMasks()
    {
        int n = MW * MH;
        mSnow = new float[n]; mWet = new float[n]; mGravel = new float[n]; mDrift = new float[n];
        mPath = new float[n]; mDir = new float[n]; mDrag = new float[n]; mIce = new float[n];
        float px = MaskRect.width / MW, pz = MaskRect.height / MH;
        // redo 2 (ground): ~40% of the arena broken/bare (TARAE ss5), ragged edges at three scales. The threshold is
        // calibrated from the noise itself (quantile over the arena) so the share holds whatever the Perlin range is.
        // gnd1 (QA #7): large irregular connected bare areas - 8-15 m fBm through a ~14 m domain warp, no round blotches
        float BareN(float x, float z)
        {
            float wx = x + (Fbm(x + 3f, z - 8f, 0.03f, 3) - 0.5f) * 16f, wz = z + (Fbm(x - 40f, z + 40f, 0.03f, 3) - 0.5f) * 16f;
            return Fbm(wx, wz, 0.075f, 4) * 0.8f + (Fbm(x + 13f, z + 77f, 0.35f, 3) - 0.5f) * 0.25f + (Mathf.PerlinNoise(x * 1.7f + 3f, z * 1.7f + 41f) - 0.5f) * 0.05f;
        }
        var samples = new List<float>();
        for (float sz = -HZ; sz < HZ; sz += 0.8f) for (float sx = -HX; sx < HX; sx += 0.8f) samples.Add(BareN(sx, sz));
        samples.Sort();
        // tr12 (ss5: ~60-70 % snow): bare only in the top ~13 % of the noise, thin snow ~7 % (was 36 % / 8 %)
        // gnd1 (QA #7: snow 60-70 %): bare ~30 %, thin ~8 %
        float qBare = samples[(int)(samples.Count * 0.70f)];   // above: bare (~30%)
        float qThin = samples[(int)(samples.Count * 0.62f)];   // between: broken, thin snow (~8%)
        float bareW = Mathf.Max(0.01f, qBare - qThin);
        var onRiver = new bool[n];   // frozen river band (SPBuilder.Ice.cs): full snow cover, no drifts, no bare patches (the ice mesh covers it)
        for (int j = 0; j < MH; j++)
        for (int i = 0; i < MW; i++)
        {
            int k = j * MW + i;
            float x = MaskRect.xMin + (i + 0.5f) * px, z = MaskRect.yMin + (j + 0.5f) * pz;
            float mid = Fbm(x + 50f, z - 20f, 0.12f, 3), fine = Mathf.PerlinNoise(x * 0.6f + 7f, z * 0.6f + 3f);
            float bareN = BareN(x, z);
            float cover = Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 1f, (bareN - qThin) / bareW) * 0.97f);
            float wet = Mathf.Clamp01((bareN - qBare - bareW) / (bareW * 2f)) * 0.5f;
            float gravel = Mathf.Max(Mathf.Clamp01((Fbm(x - 40f, z + 2f, 0.07f, 3) - 0.5f) * 3f),
                (1f - cover) * Mathf.Clamp01(0.3f + (Fbm(x + 7f, z - 9f, 0.22f, 3) - 0.5f) * 3.5f));   // gravel in patches of the bare ground, leaf litter between
            // trampled ground round the vignettes (the dead were laid out and fetched here)
            foreach (var c in Clusters)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y)) / (c.z * (c == Clusters[0] ? 0.3f : 0.95f));   // tr12: 1.25 - trodden patches only round the props; tr14: the lantern yard only small melt patches
                // gnd5 (QA #8): noise-eroded rim at 0.3-1 m scales so the trampled patch never reads as a disc
                float ero = (Mathf.PerlinNoise(x * 0.9f + 17f, z * 0.9f + 5f) - 0.5f) * 0.6f + (Mathf.PerlinNoise(x * 2.6f + 3f, z * 2.6f + 29f) - 0.5f) * 0.3f;
                float t = 1f - Mathf.SmoothStep(0f, 1f, (d - 0.25f) / 0.9f + (fine - 0.5f) * 0.55f + (mid - 0.5f) * 0.5f + ero);   // ragged edge
                cover = Mathf.Min(cover, 1f - t * 0.55f);
                gravel = Mathf.Max(gravel, t * 0.5f);
                if (c.w > 0.5f) wet = Mathf.Max(wet, t * 0.9f);
            }
            // footpaths
            float path = 0, dir = 0;
            foreach (var line in Paths)
            {
                float d = SegDist(new Vector2(x, z), line, out float dt);
                float half = 1.1f + 0.5f * Mathf.PerlinNoise(x * 0.05f, z * 0.05f + 9f);
                float p = 1f - Mathf.SmoothStep(0f, 1f, (d - half * 0.5f) / half);
                if (p > path) { path = p; dir = dt; }
            }
            path *= 0.75f + 0.5f * mid;
            // tr22 (ss5): the lantern yard is snow - bare only along one narrow trodden path and the melt rings round its lanterns
            {
                float dy = Vector2.Distance(new Vector2(x, z), new Vector2(Clusters[0].x, Clusters[0].y));
                if (dy < 12f)
                {
                    float ky = 1f - Mathf.SmoothStep(9f, 12f, dy + (fine - 0.5f) * 1.5f);
                    float trod = Mathf.Clamp01((path - 0.45f) / 0.35f);   // only the core of the footpath stays bare
                    cover = Mathf.Lerp(cover, Mathf.Max(cover, 0.95f) * (1f - 0.75f * trod), ky);
                    gravel *= 1f - ky * (1f - trod) * 0.85f; wet *= 1f - ky * 0.9f;
                    path *= 1f - ky * (1f - trod);   // tr23: the footpath band darkened the yard by itself - keep only its core
                    // melt rings: a ragged ~1.3 m damp bare ring at the foot of each yard lantern (2 vignette + the MX one)
                    // gnd10 (Kido: "저 두개의 구멍같은거 뭐야"): the lanterns now stand outside the arena (KLSwap, after this pass),
                    // so the melt rings at the old yard positions read as holes - no rings here any more
                    foreach (var lp in new Vector2[0])
                    {
                        float dl = Vector2.Distance(new Vector2(x, z), lp) + (fine - 0.5f) * 0.45f + (mid - 0.5f) * 0.3f;
                        float mr = 1f - Mathf.SmoothStep(0.75f, 1.45f, dl);
                        cover = Mathf.Min(cover, 1f - mr * 0.8f);
                        wet = Mathf.Max(wet, mr * 0.6f); gravel = Mathf.Max(gravel, mr * 0.35f);
                    }
                }
            }
            // cart ruts (two grooves 1.3 m apart) and the drag trail
            float drag = 0;
            { float d = Mathf.Abs(SegDist(new Vector2(x, z), Ruts, out _)); if (Vector2.Dot(new Vector2(x, z) - Ruts[0], Ruts[1] - Ruts[0]) < 0f || Vector2.Dot(new Vector2(x, z) - Ruts[Ruts.Length - 1], Ruts[Ruts.Length - 2] - Ruts[Ruts.Length - 1]) < 0f) d = 99f;   // no U-turn round the ends
              drag = Mathf.Max(drag, (1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(d - 0.65f) / 0.32f)) * 1f); }
            { float d = SegDist(new Vector2(x, z), DragTrail, out _); drag = Mathf.Max(drag, (1f - Mathf.SmoothStep(0f, 1f, d / (0.55f + 0.2f * fine))) * 0.85f); }
            // ice: frozen puddles in the wet hollows and glazed spots on the trodden paths
            float ice = Mathf.Clamp01((Fbm(x + 90f, z - 70f, 0.09f, 3) - 0.6f) * 5f) * Mathf.Clamp01(wet * 1.4f + path * 0.6f);
            // outside the arena: snowbanks, deep and fairly unbroken
            float ox = Mathf.Max(0, Mathf.Abs(x) - HX), oz = Mathf.Max(0, Mathf.Abs(z) - HZ);
            float outside = Mathf.Clamp01(Mathf.Sqrt(ox * ox + oz * oz) / 3f);
            cover = Mathf.Lerp(cover, Mathf.Max(cover, 0.92f), outside);
            float dRiv = DistanceToRiver(x, z);
            // gr4: the river's snow banks (ice mesh) run out into the ground: snow thins away from them over ~4 m
            if (dRiv < 11f)
            {
                float bank = 1f - Mathf.SmoothStep(0f, 1f, (dRiv - 6.5f + (fine - 0.5f) * 1.5f) / 4.5f);
                bank *= Mathf.Lerp(0.15f, 1f, Mathf.SmoothStep(0.3f, 0.7f, Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 11f)));   // gnd1 (QA #4): the bank snow comes and goes along the river
                cover = Mathf.Max(cover, bank * 0.95f); wet *= 1f - bank; path *= 1f - bank * 0.6f;
            }
            if (dRiv < 6.5f) { onRiver[k] = true; cover = 1f; wet = 0f; gravel = 0f; path = 0f; drag = 0f; ice = 0f; }
            mSnow[k] = cover; mWet[k] = wet; mGravel[k] = gravel; mPath[k] = path; mDir[k] = dir; mDrag[k] = drag; mIce[k] = ice;
        }

        // drifts against everything solid (vignette props, lanterns, border rocks/trees/stumps); deeper downwind (+x +z)
        var wind = new Vector2(0.45f, 0.35f);
        var mMelt = new float[n];
        foreach (Transform grp in root)
        {
            if (grp.name != "Border" && grp.name != "Vignettes") continue;
            foreach (var r in grp.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial && r.sharedMaterial.shader.name.Contains("LightCone")) continue;
                // gnd10: no drift mound / melt ring for lanterns - KLSwap moves them past the boundary after this pass, which left
                // round snow mounds with dark melt-ring centres (holes) where they used to stand
                { bool lant = false; for (var t = r.transform; t && t != grp; t = t.parent) if (t.name.Contains("Lantern")) { lant = true; break; } if (lant) continue; }
                var b = r.bounds;
                if (b.size.y < 0.3f) continue;
                float rad = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.3f, 3f);
                float reach = rad + 0.8f + Mathf.Min(b.size.y, 3f) * 0.35f;
                float amp = Mathf.Clamp01(0.4f + b.size.y * 0.25f);
                var c = new Vector2(b.center.x, b.center.z) + wind * rad * 0.6f;
                int i0 = Mathf.Max(0, (int)((c.x - reach - 1 - MaskRect.xMin) / px)), i1 = Mathf.Min(MW - 1, (int)((c.x + reach + 1 - MaskRect.xMin) / px));
                int j0 = Mathf.Max(0, (int)((c.y - reach - 1 - MaskRect.yMin) / pz)), j1 = Mathf.Min(MH - 1, (int)((c.y + reach + 1 - MaskRect.yMin) / pz));
                for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    float x = MaskRect.xMin + (i + 0.5f) * px, z = MaskRect.yMin + (j + 0.5f) * pz;
                    var q = new Vector2(x, z) - c;
                    float d = q.magnitude + (Mathf.PerlinNoise(x * 1.3f, z * 1.3f) - 0.5f) * 0.6f;
                    float t = 1f - Mathf.SmoothStep(0f, 1f, (d - rad * 0.5f) / reach);
                    int k = j * MW + i;
                    mDrift[k] = Mathf.Max(mDrift[k], t * amp);
                    // sn1: the snow melts first right round the base (ground sheet 5/8): a ragged wet ring, 10-35 cm
                    float dm = (new Vector2(x, z) - new Vector2(b.center.x, b.center.z)).magnitude / rad + (Mathf.PerlinNoise(x * 3.1f + 5f, z * 3.1f) - 0.5f) * 0.35f;
                    mMelt[k] = Mathf.Max(mMelt[k], 1f - Mathf.SmoothStep(0f, 1f, (dm - 0.9f) / Mathf.Clamp(0.5f / rad, 0.15f, 0.9f)));
                }
            }
        }
        // r9 (Kido: snow looked cut out with scissors): blur the cover field ~1.5 m so the snow/ground border is a wide
        // soft gradient; the shader's height blend (snow in the gaps between stones, thin on their tops) does the detail
        {
            void Blur(float[] f, int r)
            {
                var tmp = new float[f.Length];
                for (int j = 0; j < MH; j++)
                {
                    float acc = 0; int cnt = 0;
                    for (int i = -r; i < MW + r; i++)
                    {
                        if (i + r < MW) { acc += f[j * MW + i + r]; cnt++; }
                        if (i - r - 1 >= 0) { acc -= f[j * MW + i - r - 1]; cnt--; }
                        if (i >= 0 && i < MW) tmp[j * MW + i] = acc / Mathf.Max(cnt, 1);
                    }
                }
                for (int i = 0; i < MW; i++)
                {
                    float acc = 0; int cnt = 0;
                    for (int j = -r; j < MH + r; j++)
                    {
                        if (j + r < MH) { acc += tmp[(j + r) * MW + i]; cnt++; }
                        if (j - r - 1 >= 0) { acc -= tmp[(j - r - 1) * MW + i]; cnt--; }
                        if (j >= 0 && j < MH) f[j * MW + i] = acc / Mathf.Max(cnt, 1);
                    }
                }
            }
            Blur(mSnow, 12); Blur(mSnow, 12);   // gr2: wider level gradient = a wider granular breakup band in the shader
            Blur(mGravel, 6); Blur(mWet, 6);
            Blur(mPath, 7);   // gr4: path edges cut a straight granular line through the snow
        }
        // the drift wins over bare/wet ground right at the prop
        for (int k = 0; k < n; k++)
        {
            if (onRiver[k]) mDrift[k] = 0f;
            float d = mDrift[k];
            mSnow[k] = Mathf.Max(mSnow[k], d * 1.2f);
            mWet[k] *= 1f - d; mIce[k] *= 1f - d; mPath[k] *= 1f - d * 0.7f;
            float me = 0f;   // gnd11: no melt rings at all - props moved/removed after this pass left them as dark holes (view 17)
            if (me > 0f) { mSnow[k] *= 1f - me * 0.5f; mDrift[k] *= 1f - me; mWet[k] = Mathf.Max(mWet[k], me * 0.55f); }
        }

        Texture2D Save(string name, float[] cr, float[] cg, float[] cb, float[] ca)
        {
            var tex = new Texture2D(MW, MH, TextureFormat.RGBA32, false, true);
            var px32 = new Color32[n];
            for (int k = 0; k < n; k++)
                px32[k] = new Color32((byte)(Mathf.Clamp01(cr[k]) * 255), (byte)(Mathf.Clamp01(cg[k]) * 255), (byte)(Mathf.Clamp01(cb[k]) * 255), (byte)(Mathf.Clamp01(ca[k]) * 255));
            tex.SetPixels32(px32); tex.Apply();
            var path = $"{Gen}/{name}.png";
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false; imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true; imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.npotScale = TextureImporterNPOTScale.None; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        var g = mats["SP_Ground"];
        g.SetTexture("_MaskA", Save("SP_GroundMaskA", mSnow, mWet, mGravel, mDrift));
        g.SetTexture("_MaskB", Save("SP_GroundMaskB", mPath, mDir, mDrag, mIce));
        g.SetVector("_MaskRect", new Vector4(MaskRect.xMin, MaskRect.yMin, MaskRect.width, MaskRect.height));
        EditorUtility.SetDirty(g);

        // gnd4: the frozen river runs past the arena edge where the ground rises (Height) and through the bridge pads; the
        // ice (SPBuilder.Ice.cs, at the centreline height) then showed as rectangles / was buried. Carve a shallow bed:
        // within ~7.5 m of the centreline the ground drops to the centreline height, easing back over the next 3.5 m.
        Vector2 RiverNearest(Vector2 p)
        {
            Vector2 best = RiverPath[0]; float bd = 1e9f;
            for (int s = 0; s + 1 < RiverPath.Length; s++)
            {
                var a = RiverPath[s]; var ab = RiverPath[s + 1] - a;
                var q = a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                float dd = (p - q).sqrMagnitude; if (dd < bd) { bd = dd; best = q; }
            }
            return best;
        }
        // gnd11 (QA amateur #5): the ground's slope changes abruptly on the arena rectangle (flat inside, Height rising
        // outside), which drew a ruler-straight lit seam along the edges. Ease it: a smooth quadratic over +-3 m.
        float EdgeEasedHeight(float x, float z)
        {
            float h = Height(x, z);
            float ox = Mathf.Max(0f, Mathf.Abs(x) - HX), oz = Mathf.Max(0f, Mathf.Abs(z) - HZ);
            float os = (ox > 0f || oz > 0f) ? Mathf.Sqrt(ox * ox + oz * oz) : -Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z));
            if (os > -3f && os < 3f && z < CliffZ - 1f)
            {
                float kk = (0.6f + Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 11f)) / 10f;
                h += kk * ((os + 3f) * (os + 3f) / 12f - Mathf.Max(os, 0f));
            }
            return h;
        }
        // raise the drifts into the ground mesh (up to ~0.5 m), smooth mounds
        var gm = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "/SP_Ground.asset");
        var v = gm.vertices;
        for (int k = 0; k < v.Length; k++)
        {
            float d = SampleMask(mDrift, v[k].x, v[k].z);
            v[k].y = EdgeEasedHeight(v[k].x, v[k].z) + d * d * 0.55f;
            var rp = new Vector2(v[k].x, v[k].z); var rn = RiverNearest(rp);
            float carve = 1f - Mathf.SmoothStep(0f, 1f, ((rp - rn).magnitude - 9f) / 4f);   // gnd9: wide enough that the ice shows fully under the bridges
            if (carve > 0f) { float bed = Height(rn.x, rn.y) - 0.02f; if (v[k].y > bed) v[k].y = Mathf.Lerp(v[k].y, bed, carve); }
        }
        gm.vertices = v; gm.RecalculateNormals(); gm.RecalculateTangents(); gm.RecalculateBounds();
        EditorUtility.SetDirty(gm);
        var mc = root.Find("Ground").GetComponent<MeshCollider>(); mc.sharedMesh = null; mc.sharedMesh = gm;
    }

    public static float SampleMask(float[] m, float x, float z)
    {
        if (m == null) return 0;
        int i = Mathf.Clamp((int)((x - MaskRect.xMin) / MaskRect.width * MW), 0, MW - 1);
        int j = Mathf.Clamp((int)((z - MaskRect.yMin) / MaskRect.height * MH), 0, MH - 1);
        return m[j * MW + i];
    }

    // Redo 2: keep the old border rows off the architecture (bridge x -34..-6 and gate x 34..50 on the north edge, side
    // walls on |x| 56.5..59) and off the frozen river where it runs out under the border.
    static bool BorderBlocked(float x, float z)
    {
        if (z > 44.5f && z < 54f && ((x > -35f && x < -5f) || (x > 33f && x < 51f))) return true;
        if (Mathf.Abs(x) > 56.3f && Mathf.Abs(x) < 59.2f && z > -36f && z < 42f) return true;
        return DistanceToRiver(x, z) < 8f;
    }

    // ------------------------------------------------------------------ border: dead forest, snowbanks, rocks (east, west, north)
    static void BuildBorder()
    {
        var b = Group("Border");
        var trees = SkBorderTrees;   // tr1: downloaded trees replace the procedural ones (owner: "어디서 좀 받아")
        var rocks = new[] { "PH:boulder_01", "PH:rock_07", "PH:rock_09", "PH:namaqualand_rocks_01" };
        var low = new[] {   // r11: kit snowbank/drift meshes read as smooth white blobs; drifts come from the ground mask now
            "SP_DeadShrub_A", "SP_DeadShrub_B", "SP_DeadShrub_C", "PH:root_cluster_01", "PH:root_cluster_02", "PH:tree_stump_01", "PH:tree_stump_02", "SP_Stump_A", "SP_Stump_B" };
        // three depth rows on east and west, two on the north; each row a little denser than one model per 4-6 m
        void Row(System.Func<float, Vector2> at, float len, float step, string[] set, float sMin, float sMax, bool shadows)
        {
            for (float s = R(0, step); s < len; s += step * R(0.7f, 1.3f))
            {
                var p = at(s);
                if (BorderBlocked(p.x, p.y)) continue;
                Place(set[rng.Next(set.Length)], p.x, p.y, R(0, 360), R(sMin, sMax), b, shadows);
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            float zLen = 2 * HZ + 30f;
            Row(s => new Vector2(side * (HX + R(0.8f, 2.2f)), -HZ - 15f + s), zLen, 3.2f, low, 0.9f, 1.4f, false);          // snowbanks, shrubs, roots on the line
            Row(s => new Vector2(side * (HX + R(2.5f, 5f)), -HZ - 15f + s), zLen, 6f, rocks, 0.8f, 1.6f, false);   // no shadows: ~6M shadow-caster tris with the scanned rocks (FPS 64, 11-11)
            Row(s => new Vector2(side * (HX + R(3.5f, 8f)), -HZ - 15f + s), zLen, 4.2f, trees, 0.9f, 1.3f, true);
            Row(s => new Vector2(side * (HX + R(9f, 16f)), -HZ - 15f + s), zLen, 4.5f, trees, 1.0f, 1.5f, false);
            Row(s => new Vector2(side * (HX + R(18f, 34f)), -HZ - 15f + s), zLen, 5f, SkFarTrees, 1.2f, 1.8f, false);
        }
        float xLen = 2 * HX + 20f;
        Row(s => new Vector2(-HX - 10f + s, HZ + R(0.8f, 2.4f)), xLen, 3.0f, low, 0.9f, 1.4f, false);
        // north rows cast no shadows: the moon is behind them and their 20-30 m shadows covered a third of the arena
        Row(s => new Vector2(-HX - 10f + s, HZ + R(3f, 6f)), xLen, 5.5f, rocks, 0.9f, 1.8f, false);
        Row(s => new Vector2(-HX - 10f + s, HZ + R(4.5f, 8.5f)), xLen, 6.5f, trees, 0.9f, 1.3f, false);
        // cliff lip: boulders hide the drop; far dead trees stand on the other side of the fog sea
        Row(s => new Vector2(-HX - 20f + s, CliffZ + R(-0.5f, 1.2f)), xLen + 20f, 4f, rocks, 1.4f, 2.4f, false);
        Row(s => new Vector2(-HX - 30f + s, CliffZ + R(28f, 40f)), xLen + 60f, 6f, SkFarTrees, 2.2f, 3.2f, false);
        // a few snow-covered maples among the border (the only warm colour in the trees)
        for (int i = 0; i < 8; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            Place(i % 3 == 0 ? "SP_MapleBush_B" : "SP_MapleBush_A", side * (HX + R(1.5f, 4f)), R(-HZ + 4, HZ - 4), R(0, 360), R(0.9f, 1.3f), b, false);
        }
    }

    // ------------------------------------------------------------------ FG0: red pines and branches along the camera-side edge (dither shader)
    static void BuildForeground()
    {
        var fg = Group("Foreground");
        var swap = new Dictionary<Material, Material>
        {
            { mats["SP_PineBark"], mats["SP_FG_PineBark"] }, { mats["SP_Bark"], mats["SP_FG_Bark"] }, { mats["SP_Needles"], mats["SP_FG_Needles"] },
        };
        foreach (var kv in SkFgSwap()) swap[kv.Key] = kv.Value;
        void FGPlace(string model, float x, float z, float yaw, float s)
        {
            var go = Place(model, x, z, yaw, s, fg, false);
            if (!go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => m && swap.TryGetValue(m, out var f) ? f : m).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);   // no GI, unlit
        }
        var pines = SkFgPines;   // tr1: the hero pine (적송-like JBP) frames the screen bottom, conifers between
        // a ragged row just past the south edge (the bottom 10-15% of the screen when the player is near it)
        // 11-11 first look: 12 m pines 3-7 m past the edge filled half the screen. The RoomRun camera (17 up, 14.5
        // back, fov 30) sees the screen bottom ~8 m ahead of itself, so FG sits 6-12 m past the edge and lower.
        // tr30 (QA #15): the even row read as a fence - irregular groups of 2-5 mixed sizes with open gaps. The old loop's
        // draws on the shared rng are kept (consumed, not used) so every builder after this one lays out as before.
        for (float x = -HX - 14f; x < HX + 14f; x += R(7f, 11f)) { rng.Next(pines.Length); R(7f, 12f); R(0, 360); R(0.55f, 0.75f); }
        {
            var gr = new System.Random(1530);
            float G(float a, float b) => a + (float)gr.NextDouble() * (b - a);
            for (float x = -HX - 14f + G(0f, 6f); x < HX + 14f; )
            {
                int n = gr.Next(2, 6);
                for (int i = 0; i < n; i++, x += G(2.6f, 4.2f))
                    FGPlace(pines[gr.Next(pines.Length)], x + G(-0.8f, 0.8f), -HZ - G(6.5f, 13f), G(0, 360), G(0.4f, 0.85f));   // mixed sizes
                x += G(9f, 18f);   // open gap to the next group
            }
        }
        for (float x = -HX - 10f; x < HX + 10f; x += R(9f, 14f))
            FGPlace(rng.NextDouble() < 0.5 ? "SP_FGBranch_A" : "SP_FGBranch_B", x, -HZ - R(4.5f, 6.5f), R(-30, 30), R(0.8f, 1.1f));
        for (float x = -HX - 12f; x < HX + 12f; x += R(12f, 18f))
            FGPlace(SkDeads[rng.Next(SkDeads.Length)], x, -HZ - R(9f, 14f), R(0, 360), R(0.8f, 1.0f));
    }

    // ------------------------------------------------------------------ vignette clusters (outer ring; middle 70% open)
    // (x, z, radius, muddy)
    // tr22: yard lantern feet (vignette lanterns at cluster 0 + (-2.2,1.2)/(2.6,2.0), MX lantern at (-49.2,28.2)) for the melt rings
    static readonly Vector2[] YardLanterns = { new Vector2(-46.2f, 33.2f), new Vector2(-41.4f, 34f), new Vector2(-49.2f, 28.2f) };
    static readonly Vector4[] Clusters =
    {
        new Vector4(-44f, 32f, 6f, 0),   // 0 stone-lantern yard (NW)
        new Vector4(-47f, -12f, 5f, 0),  // 1 거적 corpse spot (W)
        new Vector4(46f, 9f, 5.5f, 1),   // 2 broken cart (E)
        new Vector4(41f, 34f, 5f, 0),    // 3 jars and stakes (NE)
        new Vector4(17f, -37f, 5f, 0),   // 4 second corpse spot (S)
        new Vector4(-6f, 38.5f, 5f, 0),  // 5 lantern pair (N centre)
    };

    static void BuildVignettes(List<Light> lanterns)
    {
        var vg = Group("Vignettes");
        Transform C(int i, string name) { var g = Group(name, vg); g.position = new Vector3(Clusters[i].x, 0, Clusters[i].y); return g; }
        GameObject P(Transform g, string m, float dx, float dz, float yaw, float s = 1f, bool sh = true)
            => Place(m, g.position.x + dx, g.position.z + dz, yaw, s, g, sh);
        void Blood(Transform g, float dx, float dz, float yaw, float size, int cell)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Blood";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(g, false);
            float x = g.position.x + dx, z = g.position.z + dz;
            go.transform.SetPositionAndRotation(new Vector3(x, Height(x, z), z), Quaternion.Euler(0, yaw, 0));
            size *= 1.9f;   // r9: ss5 kill splatters are 2-5 m, ours read as small stains
            go.transform.localScale = new Vector3(size, 1.2f, size * (cell == 2 ? 1.4f : 1f));   // tr31: back to 1.2 (2.6 did not close the white hole in 04)
            var r = go.GetComponent<MeshRenderer>();
            var m = mats["SP_Blood"];
            if (cell >= 0)
            {
                var path = $"{MatDir}/SP_Blood_{cell}.mat";
                var mc = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mc) { mc = new Material(m); AssetDatabase.CreateAsset(mc, path); }
                mc.CopyPropertiesFromMaterial(m); mc.SetFloat("_Cell", cell); EditorUtility.SetDirty(mc); m = mc;
            }
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        // 0 stone-lantern yard: two lanterns (one realtime-shadowed), a broken one, chips, sacks by the wall
        { var g = C(0, "LanternYard");
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", -2.2f, 1.2f, 150f), true));
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", 2.6f, 2.0f, 200f), false));
          // tr11: SP_StoneLantern_Broken removed - its wide flat base read as a pale oval disc from the game camera
          P(g, "SP_StoneChip_A", 0.9f, 3.0f, 80f, 1f, false); P(g, "SP_StoneChip_B", -0.3f, 2.6f, 10f, 1f, false);
          P(g, "SP_Sack_Pile", -4.4f, 3.5f, 70f); P(g, "SP_Jar_Lid", 1.4f, 0.6f, 0f, 1f, false); }
        // 1 corpse spot: straw-mat covered bodies, rags, stakes behind, blood pool and a drag trail toward the edge
        { var g = C(1, "CorpseWest");
          P(g, "SP_StrawMat_Two", 0f, 0f, 75f); P(g, "SP_StrawMat_A", 2.3f, -1.4f, 110f); P(g, "SP_StrawMat_Pulled", -1.6f, 1.9f, 40f);
          P(g, "SP_Rags_A", 1.1f, 1.5f, 0f, 1f, false); P(g, "SP_Rags_B", -0.8f, -1.8f, 200f, 1f, false);
          P(g, "SP_Stakes_Row", -3.6f, 0.2f, 90f); P(g, "SP_Stakes_Broken", -3.3f, 3.2f, 120f);
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", -3.9f, -2.8f, 100f), false));
          Blood(g, 0.4f, 0.2f, 20f, 2.4f, 0); Blood(g, 1.6f, -1.6f, 70f, 1.6f, 1); Blood(g, -2.2f, 1.6f, 120f, 1.2f, 2); }
        // 2 broken cart: cart on its side, loose wheel, sacks spilled, a tipped jar, wet churned mud
        { var g = C(2, "BrokenCart");
          P(g, "SP_Handcart_Broken", 0f, 0f, 205f); P(g, "SP_CartWheel", -2.2f, 1.4f, 30f, 1f, false);
          P(g, "SP_Sack_A", 1.8f, -1.2f, 60f); P(g, "SP_Sack_A", 2.4f, 0.2f, 140f); P(g, "SP_Sack_Pile", 3.1f, 2.2f, 10f);
          P(g, "SP_Jar_Tipped", -1.4f, -1.9f, 250f); P(g, "SP_Rags_B", 0.8f, 2.3f, 90f, 1f, false);
          Blood(g, -0.6f, -0.8f, 40f, 1.4f, 1); }
        // 3 jars and stakes
        { var g = C(3, "JarsStakes");
          P(g, "SP_Jar_A", 0f, 0f, 0f); P(g, "SP_Jar_A", 0.9f, 0.5f, 120f, 0.85f); P(g, "SP_Jar_Broken", -1.0f, 0.8f, 70f);
          P(g, "SP_Jar_Lid", 0.4f, -1.0f, 30f, 1f, false); P(g, "SP_Jar_Tipped", 1.8f, -0.9f, 300f);
          P(g, "SP_Stakes_Bare", -2.4f, 2.6f, 15f); P(g, "SP_Stakes_Broken", 2.9f, 2.4f, 160f); P(g, "SP_Stakes_Row", 0.3f, 3.6f, 0f); }
        // 4 second corpse spot (south, near the camera side): one body under a mat, bones, a lantern (shadowed)
        { var g = C(4, "CorpseSouth");
          P(g, "SP_StrawMat_B", 0f, 0f, 160f); P(g, "SP_Rags_A", -1.3f, 0.6f, 90f, 1f, false);
          P(g, "SP_Bone", 1.5f, 1.0f, 30f, 1f, false); P(g, "SP_Bone", 1.9f, 0.4f, 200f, 1f, false); P(g, "SP_Stakes_Bare", -2.6f, -1.4f, 60f);
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", 2.8f, -2.4f, 330f), true));
          Blood(g, 0.2f, 0.1f, 200f, 2.0f, 1); Blood(g, -1.6f, 1.5f, 250f, 1.1f, 3); }   // tr37: cell 0 draws a pale flat centre (the pale shape in 04, probe) - cell 1 until the look worker fixes SP_Blood_0
        // 5 lantern pair at the north centre (realtime-shadowed: the boss side)
        { var g = C(5, "LanternPairNorth");
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", -3f, 0f, 180f), true));
          lanterns.Add(Lantern(P(g, "Arch/SP_Arch_Lantern_A", 3f, 0f, 180f), false));
          P(g, "SP_StoneChip_A", 0.4f, 1.1f, 0f, 1f, false); }
        // edge lanterns on east and west (unshadowed pools)
        var edge = Group("EdgeLanterns", vg);
        foreach (var p in new[] { new Vector2(-HX + 1.2f, -30f), new Vector2(-HX + 1.2f, 10f), new Vector2(HX - 1.2f, -24f), new Vector2(HX - 1.2f, 28f) })
            lanterns.Add(Lantern(Place("Arch/SP_Arch_Lantern_B", p.x, p.y, p.x < 0 ? 90f : 270f, 1f, edge), false));
    }

    // Spot from the fire box aimed down and into the arena: 2200 K, cookie (lattice + branches), Mixed (bake later),
    // emissive glow is on the kit's SP_LanternGlow slot; a light-shaft cone hangs under it.
    static Light Lantern(GameObject lantern, bool shadowed)
    {
        if (!lantern) return null;
        var bounds = new Bounds(lantern.transform.position, Vector3.zero);
        foreach (var r in lantern.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var box = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.62f, bounds.center.z);
        var inward = -new Vector3(box.x, 0, box.z).normalized;   // toward the arena centre
        var go = new GameObject("LanternSpot"); go.transform.SetParent(lantern.transform, true);
        // r10: the source sits higher (just under the cap, ~0.7 m above the box centre): from the old 0.25 m the 1/d²
        // falloff made a hot orange disc with a hard ring at the lantern foot; from higher up the pool is wider, softer
        // and evenly fading (half brightness ~1.5 m out), still lit from the fire box direction
        // lt3: clamped under the top so on the short Megascans stone lanterns it does not sit above the cap and light it
        go.transform.position = box + Vector3.up * Mathf.Clamp(bounds.max.y - 0.3f - box.y, 0.2f, 0.7f);
        // r9 (Kido: "빛은 왜이러고"): straight down, soft round pool, no cookie wedges, no additive cone mesh
        go.transform.rotation = Quaternion.LookRotation(Vector3.down + inward * 0.08f, Vector3.up);
        var l = go.AddComponent<Light>();
        // 2500 K, not 2100: warm amber instead of saturated orange; modest intensity (ss5 pools are subtle, the box glows)
        // lt1/lt2: 2500 K, then 2800 K, still went salmon-pink: the grade keeps red (Hue vs Sat x1.7) and drains yellow, and
        // the cool snow base pulls it further toward pink. 3300 K x a yellow filter lands on amber after the grade
        l.type = LightType.Spot; l.useColorTemperature = true; l.colorTemperature = 3300f; l.color = new Color(1f, 0.93f, 0.7f);
        l.intensity = 5f; l.range = shadowed ? 8f : 7f; l.spotAngle = 170f; l.innerSpotAngle = 70f;
        l.lightmapBakeType = LightmapBakeType.Realtime;   // Mixed after the bake (see moon)
        l.cookie = null; rng.NextDouble();   // keep the random stream the same for everything placed after
        if (shadowed) { l.shadows = LightShadows.Soft; l.shadowStrength = 0.6f;   // lt1: hard 0.9 drew long dark grass streaks across the pool
                        l.shadowNearPlane = 0.3f; l.shadowBias = 0.02f; l.shadowNormalBias = 0.3f; l.shadowResolution = LightShadowResolution.Medium; }
        else l.shadows = LightShadows.None;
        // light shaft
        GameObject cone = null;   // the additive light-shaft mesh read as a flat orange wedge on the ground
        if (cone)
        {
            var c = (GameObject)PrefabUtility.InstantiatePrefab(cone, lantern.transform);
            c.transform.position = box; c.transform.rotation = Quaternion.LookRotation(inward) ;
            foreach (var r in c.GetComponentsInChildren<Renderer>()) { r.sharedMaterial = mats["SP_LightCone"]; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }
        foreach (var r in lantern.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;   // its own pool would shadow itself
        return l;
    }

    // ------------------------------------------------------------------ scatter (~10k, instanced, no shadows)
    static int BuildScatter()
    {
        var go = new GameObject("Scatter"); go.transform.SetParent(root, false);
        go.AddComponent<LookStudyInstancer>();
        var sc = go.AddComponent<SPScatter>();
        // (model, count, min scale, max scale, how much it prefers the edge 0..1, near clusters, bare: 1 = only where the
        // ground shows through the snow, clump: points per clump (11-11 redo: uniform specks read as noise, the reference
        // has dense clumps of dead grass and brush with open snow between))
        var plan = new (string m, int n, float s0, float s1, float edge, bool clusters, float bare, int clump)[]
        {
            ("SP_Pebble_A", 700, 0.7f, 1.5f, 0.5f, false, 0.85f, 10), ("SP_Pebble_B", 700, 0.7f, 1.5f, 0.5f, false, 0.85f, 10),
            ("SP_Pebble_C", 700, 0.7f, 1.5f, 0.5f, false, 0.85f, 10), ("SP_Pebble_D", 700, 0.7f, 1.5f, 0.5f, false, 0.85f, 10),
            ("SP_PebbleCluster", 400, 0.8f, 1.4f, 0.6f, false, 0.7f, 6),
            ("SP_StoneChip_A", 400, 0.7f, 1.3f, 0.5f, true, 0.6f, 6), ("SP_StoneChip_B", 400, 0.7f, 1.3f, 0.5f, true, 0.6f, 6),
            ("SP_TwigBit", 900, 0.8f, 1.4f, 0.6f, false, 0.5f, 12), ("SP_Twigs", 500, 0.9f, 1.4f, 0.7f, false, 0.3f, 8),
            ("SP_GrassTuft_A", 40, 0.9f, 1.6f, 0.65f, false, 0.15f, 4), ("SP_GrassTuft_B", 40, 0.9f, 1.6f, 0.65f, false, 0.15f, 4),   // r2b: thinned (owner: small identical tufts), VegetationDense carries the grass
            ("SP_GrassTuft_C", 40, 0.9f, 1.6f, 0.65f, false, 0.15f, 4), ("SP_TwigBit", 600, 0.9f, 1.5f, 0.5f, false, 0.4f, 10),   // SP_GrassCards out: the cards drew as white rectangles in the instanced path (11-11 r1)
            ("SP_DeadShrub_A", 260, 0.8f, 1.5f, 0.6f, false, 0f, 6), ("SP_DeadShrub_B", 260, 0.8f, 1.5f, 0.6f, false, 0f, 6),
            ("SP_DeadShrub_C", 260, 0.8f, 1.5f, 0.6f, false, 0f, 6),
            ("SP_SnowClump", 350, 0.8f, 1.8f, 0.5f, false, -0.6f, 5), ("SP_Bone", 120, 0.8f, 1.1f, 0.3f, true, 0.5f, 4),
        };
        int total = 0; var singleRng = new System.Random(29);
        foreach (var p in plan)
        {
            var src = Src(p.m); if (!src) continue;
            var mf = src.GetComponentInChildren<MeshFilter>(); var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) { Log("SCATTER skip " + p.m + ": no mesh"); continue; }
            if (mf.sharedMesh.subMeshCount > 1) Log($"SCATTER {p.m}: {mf.sharedMesh.subMeshCount} submeshes, only the first is drawn");
            var set = new SPScatter.Set
            {
                mesh = mf.sharedMesh, mat = mr.sharedMaterial,
                pre = mf.transform.rotation, preOffset = mf.transform.position, preScale = mf.transform.lossyScale.x,
            };
            int made = 0;
            float cx = 0, cz = 0; int left = 0; float spread = 1f;
            for (int k = 0; k < p.n * 12 && made < p.n; k++)
            {
                float x, z;
                if (left <= 0)
                {
                    // new clump centre
                    if (p.clusters && rng.NextDouble() < 0.6)
                    {
                        var c = Clusters[rng.Next(Clusters.Length)]; float a = Rn() * 6.283f, d = Mathf.Sqrt(Rn()) * c.z * 1.4f;
                        cx = c.x + Mathf.Cos(a) * d; cz = c.y + Mathf.Sin(a) * d;
                    }
                    else { cx = R(-HX - 6f, HX + 6f); cz = R(-HZ - 6f, HZ + 8f); }
                    float edge = Mathf.Clamp01(1f - Mathf.Min(HX + 6f - Mathf.Abs(cx), HZ + 6f - Mathf.Abs(cz)) / 22f);   // 1 near/outside the edge
                    float dens = Mathf.Lerp(1f - p.edge, 1f, edge) * (0.45f + 0.9f * Mathf.PerlinNoise(cx * 0.13f + p.m.Length, cz * 0.13f));
                    // bare-ground lovers (pebbles) go where the snow is broken, snow clumps where it is whole
                    float snowAt = SampleMask(mSnow, cx, cz) - SampleMask(mPath, cx, cz) * 0.4f;
                    dens *= p.bare >= 0 ? Mathf.Lerp(1f, Mathf.Clamp01(1.25f - snowAt), p.bare) : Mathf.Lerp(1f, snowAt, -p.bare);
                    if (Rn() > dens) continue;
                    left = Mathf.Max(1, (int)(p.clump * R(0.5f, 1.5f))); spread = R(0.4f, 1.2f) * Mathf.Sqrt(p.clump) * 0.35f;
                    // tr29 (QA #6): lone shrubs / tufts (clumps of 1-2) mostly cut - own random stream, the shared one stays as it was
                    if (left < 3 && (p.m.StartsWith("SP_DeadShrub") || p.m.StartsWith("SP_GrassTuft")) && singleRng.NextDouble() < 0.7) { left = 0; continue; }
                }
                left--;
                // gaussian-ish spread round the clump centre
                float ga = Rn() * 6.283f, gr = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-4f, Rn()))) * spread;
                x = cx + Mathf.Cos(ga) * gr; z = cz + Mathf.Sin(ga) * gr;
                if (DistanceToRiver(x, z) < 6.5f) continue;   // nothing on the ice
                float dr = SampleMask(mDrift, x, z);
                set.at.Add(new Vector4(x, Height(x, z) + dr * dr * 0.55f - 0.01f, z, R(0, 360)));
                set.scale.Add(R(p.s0, p.s1));
                made++;
            }
            sc.sets.Add(set); total += made;
        }
        sc.Push();
        return total;
    }

    // ------------------------------------------------------------------ fog sheets, fog sea, purple wall, falling snow
    static void BuildFogAndVfx()
    {
        var fx = Group("FogVfx");
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        void Sheet(string name, string mat, Vector3 c, Vector2 size)
        {
            var go = new GameObject(name); go.transform.SetParent(fx, false);
            go.transform.SetPositionAndRotation(c, Quaternion.Euler(90, 0, 0)); go.transform.localScale = new Vector3(size.x, size.y, 1);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mats[mat]; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        // low sheets over everything (thin over the arena), the 1.5 m one only on the border ring (overdraw budget)
        Sheet("FogSheet_0.3", "SP_FogSheetLow", new Vector3(0, 0.3f, 0), new Vector2(2 * HX + 50, 2 * HZ + 40));
        Sheet("FogSheet_0.8", "SP_FogSheetMid", new Vector3(0, 0.8f, 0), new Vector2(2 * HX + 50, 2 * HZ + 40));
        Sheet("FogSheet_1.5_W", "SP_FogSheetHigh", new Vector3(-HX - 10f, 1.5f, 0), new Vector2(24, 2 * HZ + 40));
        Sheet("FogSheet_1.5_E", "SP_FogSheetHigh", new Vector3(HX + 10f, 1.5f, 0), new Vector2(24, 2 * HZ + 40));
        Sheet("FogSheet_1.5_N", "SP_FogSheetHigh", new Vector3(0, 1.5f, HZ + 8f), new Vector2(2 * HX + 50, 16));
        Sheet("FogSea_A", "SP_FogSea", new Vector3(0, -6.5f, CliffZ + 30f), new Vector2(260, 64));
        Sheet("FogSea_B", "SP_FogSea", new Vector3(0, -4.2f, CliffZ + 24f), new Vector2(260, 52));

        // purple boundary wall: one open ring mesh on the arena edge, 4 m tall, UV.x around, UV.y up
        var wallMesh = RingMesh(HX + 0.6f, HZ + 0.6f, 4f, 2f);
        var wp = Gen + "/SP_BoundaryRing.asset"; AssetDatabase.DeleteAsset(wp); AssetDatabase.CreateAsset(wallMesh, wp);
        var wall = new GameObject("BoundaryWall"); wall.transform.SetParent(fx, false);
        wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
        var wr = wall.AddComponent<MeshRenderer>(); wr.sharedMaterial = mats["SP_BoundaryWall"]; wr.shadowCastingMode = ShadowCastingMode.Off; wr.receiveShadows = false;

        // tr24: falling snow = three camera-following layers from the weather bundle (SPBuilder.Snowfall.cs); the old
        // single emitter (SP_SnowFlake, AV_Glow dots) is gone
        BuildSnowfall(fx);
    }

    static Mesh RingMesh(float hx, float hz, float height, float step)
    {
        var corners = new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) };
        var pts = new List<Vector3>(); var nrm = new List<Vector3>();
        for (int c = 0; c < 4; c++)
        {
            var a = corners[c]; var b = corners[(c + 1) % 4]; float L = Vector3.Distance(a, b); int n = Mathf.CeilToInt(L / step);
            var outward = Vector3.Cross(Vector3.up, (b - a).normalized) * -1f;
            for (int i = 0; i < n; i++) { pts.Add(Vector3.Lerp(a, b, (float)i / n)); nrm.Add(outward); }
        }
        pts.Add(corners[0]); nrm.Add(nrm[0]);
        float perim = 0; var dist = new List<float> { 0 };
        for (int i = 1; i < pts.Count; i++) { perim += Vector3.Distance(pts[i - 1], pts[i]); dist.Add(perim); }
        var v = new List<Vector3>(); var n2 = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i < pts.Count; i++)
        {
            float gy = Height(pts[i].x, pts[i].z) - 0.3f;
            v.Add(new Vector3(pts[i].x, gy, pts[i].z)); v.Add(new Vector3(pts[i].x, gy + height, pts[i].z));
            n2.Add(nrm[i]); n2.Add(nrm[i]);
            uv.Add(new Vector2(dist[i] / perim, 0)); uv.Add(new Vector2(dist[i] / perim, 1));
            if (i > 0) { int k = v.Count - 4; t.AddRange(new[] { k, k + 1, k + 2, k + 2, k + 1, k + 3 }); }
        }
        var m = new Mesh { name = "SP_BoundaryRing" };
        m.SetVertices(v); m.SetNormals(n2); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ moon, ambient, volume, camera
    static void BuildLightAndPost(SPField field, List<Light> lanterns)
    {
        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.transform.SetParent(root, false);
        // 11-11 redo: 1.1 (x0.45 from StageMood) left the snow a flat dark grey-blue; snow must be the brightest thing
        moon.type = LightType.Directional; moon.color = Hex("#b4c3d4"); moon.intensity = 2.1f;
        // 11-11 first look: at 20° and strength 0.8 the border trees and rocks threw 20-30 m shadows that read as huge
        // black blocks over the snow; 25° and a lighter shadow keep the long-shadow feel
        moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.72f; moon.lightmapBakeType = LightmapBakeType.Realtime;   // Mixed only after a real bake: unbaked, static ground read a stale shadowmask (big black blobs, 11-11)
        moon.transform.rotation = Quaternion.Euler(25f, 205f, 0f);   // low, from the north: long shadows toward the camera
        RenderSettings.sun = moon;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#3a4a5e");      // night sky over snow (shadowed snow must stay blue-white, not black)
        RenderSettings.ambientEquatorColor = Hex("#5f6e7b");  // blue-grey
        RenderSettings.ambientGroundColor = Hex("#8a98a6");   // light snow bounce
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.fog = false;                           // the full-screen height fog does this
        // reflections: a small night-sky cubemap (dark zenith, pale moon-haze band at the horizon) so icy and wet
        // patches catch a sheen; without it the custom reflection was black and gloss did nothing
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = EnvCube();
        RenderSettings.reflectionIntensity = 1f;
        // the getter throws when the scene has no LightingSettings asset yet
        LightingSettings ls = null;
        try { ls = Lightmapping.lightingSettings; } catch { }
        if (ls == null)
        {
            var lsPath = Root + "/SP_Lighting.lighting";
            ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings { name = "SP_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            Lightmapping.lightingSettings = ls;
        }
        // 11-11: Shadowmask with nothing baked left the static ground reading a stale mask (big dark blobs): no baked GI until the bake pass
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
        ls.bakedGI = false; ls.realtimeGI = false;

        var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
        vol.sharedProfile = SharedLook() ?? SpecProfile();
        ConfigureLook(vol.sharedProfile, moon);

        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.transform.SetParent(root, false);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#14120f");   // umber, matches the warm far fog (look pass 11-11)
        var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.Medium;
        cd.requiresDepthOption = CameraOverrideOption.On;
        cam.gameObject.AddComponent<AudioListener>();
        SetGameView(cam, Vector3.zero, 1f);
    }

    static Cubemap EnvCube()
    {
        const int S = 64;
        var path = Gen + "/SP_EnvCube.asset";
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (!cube) { cube = new Cubemap(S, TextureFormat.RGBAHalf, true); AssetDatabase.CreateAsset(cube, path); }
        // moon direction (from the north, ~25 degrees up): a soft glow round it
        var moonDir = Quaternion.Euler(25f, 205f, 0f) * Vector3.back;
        Color zen = new Color(0.035f, 0.05f, 0.075f), hor = new Color(0.32f, 0.37f, 0.44f), below = new Color(0.22f, 0.25f, 0.29f);
        foreach (CubemapFace f in System.Enum.GetValues(typeof(CubemapFace)))
        {
            if (f == CubemapFace.Unknown) continue;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1;
                Vector3 d;
                switch (f)
                {
                    case CubemapFace.PositiveX: d = new Vector3(1, -v, -u); break;
                    case CubemapFace.NegativeX: d = new Vector3(-1, -v, u); break;
                    case CubemapFace.PositiveY: d = new Vector3(u, 1, v); break;
                    case CubemapFace.NegativeY: d = new Vector3(u, -1, -v); break;
                    case CubemapFace.PositiveZ: d = new Vector3(u, -v, 1); break;
                    default: d = new Vector3(-u, -v, -1); break;
                }
                d.Normalize();
                Color c = d.y >= 0 ? Color.Lerp(hor, zen, Mathf.Pow(d.y, 0.6f)) : Color.Lerp(hor, below, Mathf.Clamp01(-d.y * 3f));
                float m = Mathf.Max(0, Vector3.Dot(d, moonDir));
                c += new Color(0.55f, 0.62f, 0.72f) * (Mathf.Pow(m, 24f) * 2.5f + Mathf.Pow(m, 4f) * 0.25f);
                px[y * S + x] = c;
            }
            cube.SetPixels(px, f);
        }
        cube.Apply(true);
        EditorUtility.SetDirty(cube);
        return cube;
    }

    // The other thread is moving every arena to one dark POE2/Tarae grade. If it left a shared profile, use that.
    static VolumeProfile SharedLook()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:VolumeProfile", new[] { "Assets" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            var n = Path.GetFileNameWithoutExtension(p);
            if (p.StartsWith(Root)) continue;
            if (n.Contains("ArenaLook") || n.Contains("DarkFantasy") || n.Contains("SharedLook") || AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(p)).Contains("ArenaLook"))
            { Log("POST shared profile " + p); return AssetDatabase.LoadAssetAtPath<VolumeProfile>(p); }
        }
        return null;
    }

    static VolumeProfile SpecProfile()
    {
        var path = Root + "/SP_PostFX.asset";
        AssetDatabase.DeleteAsset(path);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, path);
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
        var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(-0.2f); ca.contrast.Override(18f); ca.colorFilter.Override(Hex("#E6EEF5")); ca.saturation.Override(-28f);
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(-12f); wb.tint.Override(3f);
        var st = prof.Add<SplitToning>(true); st.shadows.Override(Hex("#3A5566")); st.highlights.Override(Hex("#D9C8B0")); st.balance.Override(20f);
        // hue vs sat (0.5 = unchanged): red x1.1, orange x0.75, yellow..blue x0.5 -> blood stays the one saturated colour
        var cc = prof.Add<ColorCurves>(true);
        cc.hueVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, 0.55f), new Keyframe(0.08f, 0.375f), new Keyframe(0.16f, 0.25f), new Keyframe(0.7f, 0.25f), new Keyframe(0.92f, 0.5f) }, 0.5f, true, new Vector2(0, 1)));
        var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.2f); bl.intensity.Override(0.4f); bl.scatter.Override(0.6f); bl.clamp.Override(30f); bl.highQualityFiltering.Override(false);
        var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#05070A")); vg.intensity.Override(0.45f); vg.smoothness.Override(0.4f); vg.center.Override(new Vector2(0.5f, 0.48f));
        var fg = prof.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Medium2); fg.intensity.Override(0.2f);
        foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
        AssetDatabase.SaveAssets();
        return prof;
    }

    public static void SetGameView(Camera cam, Vector3 focus, float zoom)
    {
        var offset = new Vector3(0, 17f, -14.5f) * zoom;
        cam.transform.position = focus + offset;
        cam.transform.rotation = Quaternion.LookRotation(-offset);
    }

    // ------------------------------------------------------------------ invisible walls (outside the edge) and checks
    static void BuildWalls()
    {
        var walls = Group("ArenaWalls");
        void Box(Vector3 c, Vector3 s) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.transform.position = c; w.AddComponent<BoxCollider>().size = s; }
        Box(new Vector3(-HX - 0.5f, 2, 0), new Vector3(1, 6, 2 * HZ + 2));
        Box(new Vector3(HX + 0.5f, 2, 0), new Vector3(1, 6, 2 * HZ + 2));
        Box(new Vector3(0, 2, -HZ - 0.5f), new Vector3(2 * HX + 2, 6, 1));
        Box(new Vector3(0, 2, HZ + 0.5f), new Vector3(2 * HX + 2, 6, 1));
    }

    // Colliders reaching into the play area (other than the ground) and solid props standing in the open middle 70%.
    static int CheckArena()
    {
        int n = 0;
        foreach (var c in root.GetComponentsInChildren<Collider>())
        {
            if (c is MeshCollider && c.name == "Ground") continue;
            var b = c.bounds;
            if (b.max.x <= -HX || b.min.x >= HX || b.max.z <= -HZ || b.min.z >= HZ) continue;
            n++; Log($"COLLIDER IN ARENA {c.name} {c.GetType().Name} {b.center}");
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r.name == "Ground" || (r.transform.parent && r.transform.parent.name == "FogVfx")) continue;
            var p = r.bounds.center;
            if (Mathf.Abs(p.x) < HX * 0.7f && Mathf.Abs(p.z) < HZ * 0.7f && r.bounds.size.y > 0.25f)
                Log($"NOTE prop in the open middle: {r.transform.root.name}/{r.name} at {p}");
        }
        return n;
    }

    // ------------------------------------------------------------------ RoomRun dev room (F-key list in the dev panel)
    public static void AddRoomRunRoom()
    {
        const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
        if (!cfg) { Log("NO RoomRunConfig"); return; }
        var so = new SerializedObject(cfg);
        var rooms = so.FindProperty("rooms");
        var size = new Vector2(HX * 2, HZ * 2);
        for (int i = 0; i < rooms.arraySize; i++)
            if (rooms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == RoomId)
            {
                rooms.GetArrayElementAtIndex(i).FindPropertyRelative("size").vector2Value = size;
                so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
                return;
            }
        int at = rooms.arraySize;
        rooms.InsertArrayElementAtIndex(at - 1);   // copies the last room, then everything is overwritten
        var r = rooms.GetArrayElementAtIndex(at);
        r.FindPropertyRelative("id").stringValue = RoomId;
        r.FindPropertyRelative("displayName").stringValue = "설원 역병";
        r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(7, 0);
        r.FindPropertyRelative("scenePath").stringValue = ScenePath;
        r.FindPropertyRelative("groundMeshName").stringValue = "";
        r.FindPropertyRelative("center").vector3Value = Vector3.zero;
        r.FindPropertyRelative("size").vector2Value = size;
        r.FindPropertyRelative("fitToStage").boolValue = false;
        r.FindPropertyRelative("floorColor").colorValue = Hex("#2b3540");
        r.FindPropertyRelative("pillarCount").intValue = 0;
        r.FindPropertyRelative("doors").arraySize = 0;
        r.FindPropertyRelative("waves").arraySize = 0;
        r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
        r.FindPropertyRelative("bossOffset").vector3Value = Vector3.zero;
        r.FindPropertyRelative("devOnly").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
}
