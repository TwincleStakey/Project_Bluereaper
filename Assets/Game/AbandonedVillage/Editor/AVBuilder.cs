// Builds Assets/Game/AbandonedVillage/AbandonedVillage.unity: materials, FBX material remaps, terrain, layout,
// lights, fog and post-processing for the abandoned Joseon village (폐촌) art test. Re-running rebuilds the scene.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AbandonedVillage;

public static partial class AVBuilder
{
    public const string Root = "Assets/Game/AbandonedVillage";
    public const string ScenePath = Root + "/AbandonedVillage.unity";
    const string Tex = Root + "/Textures/";
    const string MatDir = Root + "/Materials";
    const string Models = Root + "/Models/";
    const string PH = Root + "/ThirdParty/PolyHaven/";
    const string MS = Root + "/ThirdParty/Megascans/";
    public const float TerraceY = 2.56f;
    public const float TerraceZ = 46.5f;
    // Open combat arena: nearly square, flat ground only. Nothing that blocks or stops movement is placed inside;
    // houses, walls, fences, props and trees all sit on or beyond these edges.
    // 11-10 (Kido: every arena 2x per side): 92 x 90 m, was 46 x 45.
    public const float ArenaHalfX = 46f, ArenaMinZ = -48.5f, ArenaMaxZ = 41.5f;   // 92 x 90 m
    const float RowX = 51f;        // west/east house row centres (fronts at about +-47.3)
    const float BackRowX = 60.2f;  // second rows behind
    const float SouthZ = -57f;     // south house row centres
    public const float TerrainSize = 240f, TerrainHRange = 12f, TerrainBaseY = -2f;
    // Old (46 x 45) layout points -> the new arena: inside the old arena they spread 2x, outside they keep their
    // distance from the arena edge, so things that stood on the edge or behind it still do.
    public static float MapX(float x) => Mathf.Abs(x) <= 23f ? x * 2f : x + Mathf.Sign(x) * 23f;
    public static float MapZ(float z) { float d = z + 3.5f; return Mathf.Abs(d) <= 22.5f ? -3.5f + d * 2f : z + Mathf.Sign(d) * 22.5f; }
    static Vector2 Map(Vector2 p) => new Vector2(MapX(p.x), MapZ(p.y));
    public static bool InArena(float x, float z, float margin = 0f) =>
        Mathf.Abs(x) < ArenaHalfX - margin && z > ArenaMinZ + margin && z < ArenaMaxZ - margin;

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static System.Random rng;
    static Transform root;

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ------------------------------------------------------------------ textures and materials
    static Texture2D T(string file, bool normal = false)
    {
        var path = Tex + file;
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) { Debug.LogError("[AV] missing texture " + path); return null; }
        bool dirty = false;
        if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (imp.maxTextureSize > 2048) { imp.maxTextureSize = 2048; dirty = true; }
        if (dirty) imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D TP(string file, bool normal = false)
    {
        var imp = AssetImporter.GetAtPath(file) as TextureImporter;
        if (imp == null) return null;
        if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(file);
    }

    static Material Lit(string name, Texture2D diff, Texture2D nor, Color tint, float tiling, float smooth, float bump = 1f, bool twoSided = false)
    {
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", diff); m.SetColor("_BaseColor", tint);
        m.SetTextureScale("_BaseMap", Vector2.one * tiling);
        if (nor) { m.SetTexture("_BumpMap", nor); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Cull", twoSided ? 0f : 2f);
        m.SetFloat("_EnvironmentReflections", 1f);
        m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static Material Emissive(Material m, Color emission)
    {
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Particle(string name, Texture2D tex, Color color, bool additive)
    {
        var path = $"{MatDir}/{name}.mat";
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f); m.SetFloat("_ZWrite", 0f); m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SoftParticlesEnabled", 1f); m.EnableKeyword("_SOFTPARTICLES_ON");
        m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1.5f, 0, 0));
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(m);
        mats[name] = m;
        return m;
    }

    static void BuildMaterials()
    {
        Directory.CreateDirectory(MatDir);
        Lit("AV_Wood", T("weathered_brown_planks_diff_1k.jpg"), T("weathered_brown_planks_nor_gl_1k.jpg", true), Hex("#9a8a7e"), 0.45f, 0.12f);
        Lit("AV_Plank", T("old_planks_02_diff_1k.jpg"), T("old_planks_02_nor_gl_1k.jpg", true), Hex("#aaa196"), 0.4f, 0.1f, 1.2f, true);
        Lit("AV_Thatch", T("ThatchedRoof001A_1K-JPG_Color.jpg"), T("ThatchedRoof001A_1K-JPG_NormalGL.jpg", true), Hex("#bdb19c"), 0.5f, 0.05f, 1.4f, true);
        Lit("AV_MudWall", T("plastered_stone_wall_diff_1k.jpg"), T("plastered_stone_wall_nor_gl_1k.jpg", true), Hex("#9a8f80"), 0.4f, 0.08f);
        Lit("AV_Stone", T("rock_path_diff_2k.jpg"), T("rock_path_nor_gl_2k.jpg", true), Hex("#c4c2bc"), 0.35f, 0.12f, 1.3f);
        Lit("AV_StoneWall", T("mossy_stone_wall_diff_1k.jpg"), T("mossy_stone_wall_nor_gl_1k.jpg", true), Hex("#a5a49c"), 0.6f, 0.15f, 1.3f);
        Lit("AV_Tile", T("plastered_stone_wall_diff_1k.jpg"), T("plastered_stone_wall_nor_gl_1k.jpg", true), Hex("#5a5f68"), 0.6f, 0.2f, 0.6f, true);
        Lit("AV_Paper", T("Paper005_1K-JPG_Color.jpg"), T("Paper005_1K-JPG_NormalGL.jpg", true), Hex("#a99e86"), 1.2f, 0.05f, 1f, true);
        Emissive(Lit("AV_PaperGlow", T("Paper005_1K-JPG_Color.jpg"), null, Hex("#8a5a32"), 1.2f, 0.05f, 1f, true), Hex("#ff6a12") * 1.6f);
        Emissive(Lit("AV_Glow", null, null, Hex("#ffb060"), 1f, 0.2f), Hex("#ff7a20") * 3.5f);
        Lit("AV_Bark", T("bark_willow_diff_1k.jpg"), T("bark_willow_nor_gl_1k.jpg", true), Hex("#7a746c"), 0.7f, 0.08f, 1.4f);
        Lit("AV_Twig", T("bark_willow_diff_1k.jpg"), null, Hex("#8a7a66"), 2f, 0.05f, 1f, true);
        Lit("AV_Rope", T("ThatchedRoof001A_1K-JPG_Color.jpg"), null, Hex("#7a6e5a"), 3f, 0.05f);
        Lit("AV_Onggi", null, T("plastered_stone_wall_nor_gl_1k.jpg", true), Hex("#3a2219"), 1.5f, 0.22f, 0.25f);
        Lit("AV_DryGrass", T("AV_DryGrassGrad.png"), null, Hex("#f2e8d4"), 1f, 0.02f, 1f, true);   // 11-11: bleached straw, was reading as black spikes
        Lit("AV_Reed", null, null, Hex("#4a453d"), 1f, 0.05f, 1f, true);
        Lit("AV_Crow", null, null, Hex("#0b0b0e"), 1f, 0.3f);
        Lit("AV_Puddle", null, null, Hex("#1d2125"), 1f, 0.9f);   // 11-12: near black read as holes, blue-grey read as blue blobs
        Particle("AV_FogMat", T("AV_FogPuff.png"), new Color(0.42f, 0.48f, 0.56f, 0.22f), false);
        Particle("AV_WispMat", T("AV_Glow.png"), new Color(0.45f, 0.95f, 1f, 1f), true);
        Particle("AV_EmberMat", T("AV_Glow.png"), new Color(1f, 0.6f, 0.25f, 1f), true);
        AssetDatabase.SaveAssets();
    }

    static void RemapModels()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            foreach (var n in new[] { "AV_Wood", "AV_Plank", "AV_Thatch", "AV_MudWall", "AV_Stone", "AV_StoneWall", "AV_Tile", "AV_Paper", "AV_PaperGlow", "AV_Glow", "AV_Bark", "AV_Twig", "AV_Rope", "AV_Onggi", "AV_DryGrass", "AV_Reed", "AV_Crow" })
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                var map = imp.GetExternalObjectMap();
                if (map.TryGetValue(id, out var cur) && cur == mats[n]) continue;
                imp.AddRemap(id, mats[n]); changed = true;
            }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
        // Poly Haven models: one Lit material per source material, from the copied diffuse/normal maps.
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/ThirdParty/PolyHaven" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var dir = Path.GetDirectoryName(path).Replace('\\', '/');
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var names = new HashSet<string>();
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) if (m) names.Add(m.name);
            bool changed = false;
            foreach (var n in names)
            {
                if (n.StartsWith("PH_")) continue;
                string diff = null, nor = null;
                foreach (var f in Directory.GetFiles(dir))
                {
                    var fn = Path.GetFileName(f);
                    if (fn.EndsWith(".meta")) continue;
                    var key = n.Replace("military_crate_m_01", "old_military_crate");
                    if (fn.Contains("_diff_") && (fn.StartsWith(key + "_diff") || (diff == null && names.Count == 1))) diff = dir + "/" + fn;
                    if (fn.Contains("_nor_gl_") && (fn.StartsWith(key + "_nor") || (nor == null && names.Count == 1))) nor = dir + "/" + fn;
                }
                var tint = n.Contains("grass") || n.Contains("shrub") || n.Contains("fern") ? Hex("#a89a80") : n.Contains("stump") || n.Contains("trunk") ? Hex("#6a5c4c") : n.Contains("rock") || n.Contains("fire_pit") ? Hex("#5e5952") : Hex("#8e857a");   // r11: pale wood props popped at night   // r10: stumps read as pale cylinders
                var mm = Lit("PH_" + n, TP(diff), TP(nor, true), tint, 1f, n == "ceramic_pot" ? 0.18f : n.Contains("pot") ? 0.12f : 0.12f, 1f, n.Contains("grass") || n.Contains("fern") || n.Contains("shrub") || n.Contains("branches"));
                if (n.Contains("grass") || n.Contains("fern") || n.Contains("shrub"))
                {
                    mm.SetFloat("_AlphaClip", 1f); mm.SetFloat("_Cutoff", 0.4f); mm.EnableKeyword("_ALPHATEST_ON");
                }
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), n), mm); changed = true;
            }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
    }

    // ------------------------------------------------------------------ placement helpers
    static GameObject Place(string model, Vector3 pos, float yaw, float scale = 1f, Transform parent = null, string label = null)
    {
        var path = model.StartsWith("PH:") ? PH + model.Substring(3) + "/" + model.Substring(3) + ".fbx"
                 : model.StartsWith("MS:") ? MS + model.Substring(3) + ".fbx" : model.StartsWith("P:") ? model.Substring(2) : Models + model + ".fbx";
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!src) { Debug.LogError("[AV] missing model " + path); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent ? parent : root);
        go.name = label ?? src.name;
        go.transform.position = pos;
        // Blender kit FBXs come in turned 180° (Blender -Y front ends up on +Z); turn them so the front faces -Z.
        go.transform.rotation = Quaternion.Euler(0, yaw + (model.StartsWith("PH:") || model.StartsWith("MS:") || model.StartsWith("P:") ? 0f : 180f), 0);
        go.transform.localScale = Vector3.one * scale;
        NormalizeMS(go, model);   // 11-12: downloaded scans come in at 1/100 or odd scales -> real size x scale
        // r11 QA: household props inside the arena stay under knee height (jars were normalised to 1.5 m)
        if (InArena(pos.x, pos.z, -1f) && (model.Contains("KJar") || model.Contains("KStrawJar") || model == "AV_OnggiBroken" || model.Contains("wicker_basket")
            || model.Contains("wooden_stool") || model.Contains("wooden_bucket") || model.Contains("MS:Pot"))) KeepLow(go, 0.55f);
        if (model.StartsWith("MS:"))
        {
            // 11-13 r8: some scans (sticks, skulls) kept the FBX's default white "Lit" material despite the remap and read as
            // white bits on the ground; force the folder's material. The sticks pack drops all but 1-2 sticks (13 sticks
            // crossed in one spot read as an X).
            var folder = model.Substring(3, model.IndexOf('/') - 3);
            var fm = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/AV_MS_" + folder + ".mat");
            if (fm) foreach (var r in go.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) if (!ms[i] || ms[i].name == "Lit") ms[i] = fm; r.sharedMaterials = ms; }
            if (folder == "Sticks")
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var rs = go.GetComponentsInChildren<Renderer>();
                int keep = rs.Length > 1 ? rng.Next(rs.Length) : -1, keep2 = rng.NextDouble() < 0.5 ? rng.Next(rs.Length) : keep;
                for (int i = 0; i < rs.Length; i++) if (i != keep && i != keep2 && keep >= 0) Object.DestroyImmediate(rs[i].gameObject);
            }
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.On;
            if (model.StartsWith("MS:") || model.StartsWith("AV_DryGrass") || model.Contains("grass") || model.Contains("fern") || model == "AV_HangingLantern" || model == "AV_StoneLantern") r.shadowCastingMode = ShadowCastingMode.Off;
        }
        // Megascans plants stay dynamic: per-object culling + SRP batcher/instancing; static batching merged ~5k plants into huge always-drawn meshes (11-12)
        if (!(model.StartsWith("MS:") && (model.Contains("WildDry") || model.Contains("DryGrass") || model.Contains("shrubs")))) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        if (model.StartsWith("AV_DeadTree")) SeeThroughKitTree(go);   // 11-12: dither hole near the player
        return go;
    }

    static Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(root, false); return g; }

    static float Ground(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y;

    static Light PointLight(Vector3 pos, Color c, float intensity, float range, Transform parent, bool shadows = false, bool flicker = true)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false); go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point; l.color = c; intensity *= 3f; range *= 1.3f; l.intensity = intensity; l.range = range;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        if (flicker) { var f = go.AddComponent<AVFlicker>(); f.baseIntensity = intensity; }
        return l;
    }

    // ------------------------------------------------------------------ street and terrain
    static readonly Vector2[][] Streets = System.Array.ConvertAll(new[]
    {
        new[] { new Vector2(-14, -52), new Vector2(-10, -32), new Vector2(-4, -17), new Vector2(0, -4), new Vector2(2.5f, 8), new Vector2(0, 21) },
        new[] { new Vector2(2.5f, 8), new Vector2(15, 10.5f), new Vector2(30, 6), new Vector2(52, 8), new Vector2(90, 9) },
        new[] { new Vector2(0, -4), new Vector2(-13, -1.5f), new Vector2(-30, 3), new Vector2(-52, 1), new Vector2(-90, 2) },
        new[] { new Vector2(0, 25), new Vector2(0, 30) },
    }, s => System.Array.ConvertAll(s, Map));   // old layout, mapped onto the 92 x 90 arena

    public static float StreetDist(float x, float z)
    {
        float best = 1e9f; var p = new Vector2(x, z);
        foreach (var s in Streets)
            for (int i = 0; i < s.Length - 1; i++)
            {
                var a = s[i]; var b = s[i + 1]; var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
        return best;
    }

    static TerrainLayer Layer(string name, string diff, string nor, float size, Color tint, float smooth = 0.1f)
    {
        var path = $"{MatDir}/{name}.terrainlayer";
        var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (!l) { l = new TerrainLayer(); AssetDatabase.CreateAsset(l, path); }
        l.diffuseTexture = diff.StartsWith("Assets/") ? TP(diff) : T(diff); l.normalMapTexture = nor.StartsWith("Assets/") ? TP(nor, true) : T(nor, true); l.normalScale = 1.4f;
        l.tileSize = new Vector2(size, size); l.diffuseRemapMax = tint; l.smoothness = smooth;
        EditorUtility.SetDirty(l);
        return l;
    }

    // Ground height (world y) and splat weights (mud, tracks, leaves, stony, paving) at a point. AVTerrainRepair uses
    // these too, so a repaired terrain always matches a rebuilt one.
    public static float TerrainHeight(float x, float z)
    {
        float y = 0f;
        y += (Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 7f) - 0.5f) * 0.5f;
        y += (Mathf.PerlinNoise(x * 0.3f + 1f, z * 0.3f + 2f) - 0.5f) * 0.12f;
        float sd = StreetDist(x, z);
        y -= 0.08f * (1f - Mathf.Clamp01(sd / 2.2f));            // worn street sits a little lower
        float pud = Mathf.PerlinNoise(x * 0.11f + 20f, z * 0.11f + 40f);
        y -= Mathf.Clamp01((pud - 0.64f) * 6f) * 0.5f * (1f - Mathf.Clamp01(sd / 5f));   // puddles along the street
        // terrace to the north behind the retaining wall
        float terr = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TerraceZ - 1.6f, TerraceZ - 0.9f, z));
        y = Mathf.Lerp(y, TerraceY + y * 0.4f, terr);
        // hills rising beyond the village edge (behind the back house rows)
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        y += Mathf.Pow(Mathf.Clamp01((edge - 66f) / 18f), 1.6f) * (5f + 3f * Mathf.PerlinNoise(x * 0.05f, z * 0.05f));
        y += IslandMound(x, z);   // 11-11: raised knolls under the big trees on the ruin ring
        y += SouthBank(x, z);     // 11-12: raised bank on the camera side carrying the tall houses
        // r7 (Kido: too flat, but nothing to trip on): long gentle swells and hollows, +-0.45 m over 15-30 m, slope < 5 deg
        y += (Fbm(x, z, 0.03f, 51f, 19f) - 0.5f) * 0.9f * (1f - Mathf.Clamp01((Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - 60f) / 10f));
        // 11-13 (Kido: hard straight cuts in the light pools and decals, "too many"): flat quads (player/shot light
        // pools, blood and leaf decals, the macro overlay) sank into the +-0.45 m swells and puddle dips and were cut
        // off along a line. Inside the arena (plus 3 m) the ground is flat now, only a 2 cm ripple; the relief there
        // comes from the height-blended textures. The swells fade back in over 6 m outside.
        float ox = Mathf.Max(0f, Mathf.Abs(x) - ArenaHalfX), oz = Mathf.Max(0f, Mathf.Max(ArenaMinZ - z, z - ArenaMaxZ));
        float flat = 1f - Mathf.Clamp01((Mathf.Max(ox, oz) - 3f) / 6f);
        y = Mathf.Lerp(y, (Mathf.PerlinNoise(x * 0.3f + 1f, z * 0.3f + 2f) - 0.5f) * 0.02f, flat);
        return y;
    }

    static float Fbm(float x, float z, float f, float ox, float oz)
    {
        return Mathf.PerlinNoise(x * f + ox, z * f + oz) * 0.55f + Mathf.PerlinNoise(x * f * 2.3f + ox + 13f, z * f * 2.3f + oz + 7f) * 0.3f
             + Mathf.PerlinNoise(x * f * 5.7f + ox + 31f, z * f * 5.7f + oz + 3f) * 0.15f;
    }
    static float Band(float v, float t, float soft) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t - soft, t + soft, v));

    // 11-11 redo: wet mud everywhere; cart ruts along the streets with ragged edges; leaf litter drifting against walls,
    // ruins and the edge; gravel scattered in smaller broken patches. Thresholds sit on multi-octave noise with a fine
    // jitter, so borders are broken and a few metres across instead of 10 m blobs with smooth outlines.
    // 11-12 Kido preferred the previous ground: the 11-10 splat is back (SplatAtRedo kept for reference, unused)
    // 11-12 r2 (Kido, with a crop of the dark pebble ground): one ground only, the dark small-pebble layer. The pale
    // cracked-dirt and mud patches mixed in read as snow on mud. Only the north terrace keeps its paving.
    // 11-12 r5: Kido's floor is the committed one -> the committed splat (SplatAtMixed, unchanged from git)
    // r6 (Kido, crop of the dark forest mud with green sprouts: "원래는 이 바닥만 있었다고"): that one layer only
    public static void SplatAt(float x, float z, float[] w)
    {
        // r7: three dark muds in big uneven regions (8-25 m) with soft 1-2 m borders, a few small gravel patches.
        float pave = z > TerraceZ - 0.5f ? Mathf.Clamp01(1.2f - Mathf.Abs(x) / 9f + (Mathf.PerlinNoise(x * 0.23f + 2f, z * 0.23f + 9f) - 0.5f) * 0.9f) : 0f;
        float a = Fbm(x, z, 0.045f, 13f, 77f), b = Fbm(x, z, 0.06f, 91f, 5f);
        float forest = Band(a, 0.56f, 0.05f) * 0.85f;                    // the green-sprout forest mud, ~35% of the ground
        float ms = (1f - forest) * Band(b, 0.5f, 0.06f);                 // Megascans moist mud
        float brown = (1f - forest) * (1f - Band(b, 0.5f, 0.06f));       // brown mud
        float grav = Band(Fbm(x, z, 0.22f, 3f, 29f), 0.7f, 0.04f) * 0.6f;
        float k = 1f - pave;
        w[0] = forest * k; w[1] = ms * k; w[2] = brown * k; w[3] = grav * k; w[4] = pave;
        float s = w[0] + w[1] + w[2] + w[3] + w[4]; for (int i = 0; i < 5; i++) w[i] /= s;
    }

    static void SplatAtMeadow(float x, float z, float[] w)
    {
        // 11-12 r3 (Kido's crop was the original forest mud with green bits + pebble/plank scraps): forest mud everywhere,
        // the muddy-tracks layer only in soft broken patches and along the streets so the tiling doesn't read as one
        // repeated pattern; no pale cracked dirt, no gravel layer
        // r4 (Kido: the first board image): big green meadow regions and big brown mud regions (10-30 m, ragged edges),
        // the streets worn to mud, leafy mud fringing the meadows and leaf-strewn gravel in small broken patches.
        float sd = StreetDist(x, z);
        float big = Fbm(x, z, 0.035f, 31f, 17f) + (Mathf.PerlinNoise(x * 0.5f + 3f, z * 0.5f + 8f) - 0.5f) * 0.08f;
        float meadow = Band(big, 0.45f, 0.035f) * Mathf.Clamp01((sd - 1.5f) / 2.5f);
        float fringe = Mathf.Clamp01(1f - Mathf.Abs(big - 0.45f) / 0.08f) * 0.8f + (1f - meadow) * Band(Fbm(x, z, 0.09f, 3f, 61f), 0.5f, 0.06f) * 0.9f;          // leafy mud along the meadow edge
        float grav = Band(Fbm(x, z, 0.2f, 7f, 41f), 0.66f, 0.05f) * 0.8f * (1f - meadow);
        float pave = z > TerraceZ - 0.5f ? Mathf.Clamp01(1.2f - Mathf.Abs(x) / 9f + (Mathf.PerlinNoise(x * 0.23f + 2f, z * 0.23f + 9f) - 0.5f) * 0.9f) : 0f;
        float mud = 1f - meadow;
        float k = 1f - pave;
        w[0] = meadow * k; w[1] = mud * k; w[2] = fringe * k; w[3] = grav * k; w[4] = pave;
        float s = w[0] + w[1] + w[2] + w[3] + w[4]; for (int i = 0; i < 5; i++) w[i] /= s;
    }

    static void SplatAtMixed(float x, float z, float[] w)
    {
        float sd = StreetDist(x, z);
        float n1 = Mathf.PerlinNoise(x * 0.09f + 11f, z * 0.09f + 5f), n2 = Mathf.PerlinNoise(x * 0.23f + 2f, z * 0.23f + 9f);
        float edge = Mathf.Max(Mathf.Abs(x) / 47f, Mathf.Abs(z + 3.5f) / 47f);
        float w0 = 1f;                                                    // mud
        float w1 = Mathf.Clamp01(1.8f - sd / 1.8f + (n2 - 0.5f) * 0.6f);  // street tracks
        float w2 = Mathf.Clamp01((edge - 0.75f) * 1.2f + (n2 - 0.5f) * 1.1f) * (1f - w1) * 0.7f;   // pebbles: edges and drifts
        float w3 = Mathf.Clamp01((n1 - 0.4f) * 1.6f) * (1f - w1) * 0.9f;   // cracked dry dirt, blended in softly
        float w4 = z > TerraceZ - 0.5f ? Mathf.Clamp01(1.2f - Mathf.Abs(x) / 9f + (n2 - 0.5f) * 0.9f) : 0f; // courtyard paving
        if (w4 > 0) { w1 *= 1f - w4; w2 *= 1f - w4; }
        float s = w0 * 0.35f + w1 + w2 + w3 + w4;
        w[0] = w0 * 0.35f / s; w[1] = w1 / s; w[2] = w2 / s; w[3] = w3 / s; w[4] = w4 / s;
    }

    static void SplatAtRedo(float x, float z, float[] w)
    {
        float sd = StreetDist(x, z);
        float jit =(Mathf.PerlinNoise(x * 1.7f + 5f, z * 1.7f + 9f) - 0.5f) * 0.12f;
        float edge = Mathf.Max(Mathf.Abs(x) / 47f, Mathf.Abs(z + 3.5f) / 47f);
        float near = IslandNear(x, z);   // 1 at a ruin island, 0 beyond ~9 m
        float w0 = 1f;                                                                                   // mud
        float rut = Band(-sd + (Fbm(x, z, 0.35f, 4f, 2f) - 0.5f) * 2.2f, -2.1f, 0.5f);                    // street ruts, ragged
        float w1 = rut;
        float leafN = Fbm(x, z, 0.16f, 11f, 5f) + jit + Mathf.Clamp01((edge - 0.7f) * 1.5f) * 0.25f + near * 0.3f;
        float w2 = Band(leafN, 0.6f, 0.06f) * (1f - rut * 0.85f);                                        // leaf litter
        float grN = Fbm(x, z, 0.28f, 2f, 9f) + jit * 1.4f + near * 0.12f;
        float w3 = Band(grN, 0.63f, 0.05f) * 0.85f * (1f - w2 * 0.6f);                                   // gravel
        w1 += Band(-sd, -3.2f, 0.6f) * Band(grN, 0.5f, 0.08f) * 0.6f;                                   // gravel kerb along ruts
        float w4 = z > TerraceZ - 0.5f ? Mathf.Clamp01(1.2f - Mathf.Abs(x) / 9f + (Fbm(x, z, 0.23f, 2f, 9f) - 0.5f) * 0.9f) : 0f; // courtyard paving
        if (w4 > 0) { w1 *= 1f - w4; w2 *= 1f - w4; w3 *= 1f - w4; }
        float s = w0 * 0.3f + w1 + w2 + w3 + w4;
        w[0] = w0 * 0.3f / s; w[1] = w1 / s; w[2] = w2 / s; w[3] = w3 / s; w[4] = w4 / s;
    }

    // ------------------------------------------------------------------ look pass (11-10, Kido's references)
    // What is taken from the refs: (1) stone lanterns at the arena edge throwing warm pools with long, hard shadows
    // (a few shadowed spot lights aimed down and in, low over the ground, so debris shadows stretch away from them);
    // (2) cracked dark dirt with pebbles and plank scraps everywhere, denser toward the edge; (3) a purple/magenta wisp
    // wall standing on the arena edge; (4) small ground braziers with embers. Cold grey everywhere else, hot orange
    // only at the light sources. Nothing here has a collider inside the arena.
    const int ShadowedLanterns = 4;   // budget (reed thread, 200 FPS on a 4060): at most 4 shadowed lights, 1024 maps

    static void BuildLook(System.Func<float, float, float> G)
    {
        var look = Group("Look");
        var veilMat = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/AV_EdgeVeil.mat");
        var fogShader = Shader.Find("Underworld/FogCard");
        if (!veilMat && fogShader) { veilMat = new Material(fogShader); AssetDatabase.CreateAsset(veilMat, $"{MatDir}/AV_EdgeVeil.mat"); }
        if (veilMat)
        {
            veilMat.shader = fogShader;
            veilMat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Underworld/Textures/T_UWNoise.png"));
            veilMat.SetColor("_Col", new Color(0.62f, 0.26f, 1.0f, 0.62f));
            veilMat.SetFloat("_Scale", 0.16f); veilMat.SetFloat("_Speed", 0.14f); veilMat.SetFloat("_DepthFade", 0.6f);
            veilMat.SetFloat("_EdgeSoft", 0.22f); veilMat.SetFloat("_Contrast", 1.8f);
            EditorUtility.SetDirty(veilMat);
        }
        var wispMat = Particle("AV_EdgeWisp", T("AV_Glow.png"), new Color(0.78f, 0.36f, 1f, 0.9f), true);
        var emberMat = Particle("AV_Ember", T("AV_Glow.png"), new Color(1f, 0.55f, 0.18f, 1f), true);
        var flameMat = Particle("AV_Flame", T("AV_FogPuff.png"), new Color(1f, 0.48f, 0.14f, 0.85f), true);

        // ---- the purple wall: overlapping vertical veils on all four edges, a second fainter layer behind, and
        // wisps rising off the ground line. Just outside the walkable box, in front of the fences and house fronts.
        var wall = new GameObject("EdgeVeil").transform; wall.SetParent(look, false);
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        float ex = ArenaHalfX + 0.55f, ez0 = ArenaMinZ - 0.55f, ez1 = ArenaMaxZ + 0.55f;
        void Veil(Vector3 a, Vector3 b, float h, float offset, float yawFix)
        {
            float L = Vector3.Distance(a, b); int n = Mathf.CeilToInt(L / 8f);
            var dir = (b - a).normalized; var outward = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(a, b, (i + 0.5f) / n) + outward * offset;
                var go = new GameObject("Veil"); go.transform.SetParent(wall, false);
                go.transform.position = new Vector3(p.x, G(p.x, p.z) + h * 0.5f - 0.15f, p.z);
                go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 90 + yawFix, 0);
                go.transform.localScale = new Vector3(L / n * 1.3f, h * R(0.9f, 1.15f), 1);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = veilMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }
        var c00 = new Vector3(-ex, 0, ez0); var c10 = new Vector3(ex, 0, ez0); var c11 = new Vector3(ex, 0, ez1); var c01 = new Vector3(-ex, 0, ez1);
        foreach (var (a, b) in new[] { (c00, c10), (c10, c11), (c11, c01), (c01, c00) })
        {
            Veil(a, b, 3.8f, 0f, 0f);   // one layer, ~8 m cards (budget: few cards, none screen-sized)
        }
        // wisps: one long thin emitter per edge
        foreach (var (a, b) in new[] { (c00, c10), (c10, c11), (c11, c01), (c01, c00) })
        {
            var mid = (a + b) * 0.5f; float L = Vector3.Distance(a, b);
            var go = new GameObject("EdgeWisps"); go.transform.SetParent(wall, false);
            go.transform.position = new Vector3(mid.x, G(mid.x, mid.z) + 0.2f, mid.z);
            go.transform.rotation = Quaternion.LookRotation((b - a).normalized);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.9f); main.maxParticles = (int)(L * 6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.4f, 1f, 0.9f), new Color(1f, 0.35f, 0.75f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = L * 2.2f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.6f, 0.1f, L);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.8f); vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.5f; noise.frequency = 0.6f; noise.quality = ParticleSystemNoiseQuality.Low;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.7f, 0.4f, 1f), 1) },
                                               new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.3f));
            var rr = go.GetComponent<ParticleSystemRenderer>(); rr.sharedMaterial = wispMat; rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
            ps.Play();
            // a faint purple light every ~15 m so the veil tints the ground in front of it (no shadows)
            for (float t = 7f; t < L; t += 15f)
            {
                var p = Vector3.Lerp(a, b, t / L) - Vector3.Cross(Vector3.up, (b - a).normalized) * 1.2f;
                PointLight(new Vector3(p.x, G(p.x, p.z) + 1.2f, p.z), new Color(0.65f, 0.3f, 1f), 0.9f, 2.3f, wall, false, false);   // ~3 m
            }
        }

        // ---- stone lanterns on the edge (inside the veil), every ~13 m; spot lights aimed down and into the arena
        var lan = new GameObject("EdgeLanterns").transform; lan.SetParent(look, false);
        var spots = new List<(Vector3 pos, Vector3 inward)>();
        foreach (var (a, b) in new[] { (c00, c10), (c10, c11), (c11, c01), (c01, c00) })
        {
            float L = Vector3.Distance(a, b); var dir = (b - a).normalized; var inward = -Vector3.Cross(Vector3.up, dir);
            for (float t = 9f; t < L - 4f; t += 13f)
            {
                var p = Vector3.Lerp(a, b, t / L) + inward * 0.2f;
                if (StreetDist(p.x, p.z) < 2.6f) continue;   // keep the street openings clear
                spots.Add((p, inward));
            }
        }
        int shadowed = 0;
        for (int i = 0; i < spots.Count; i++)
        {
            var (p, inward) = spots[i];
            float y = G(p.x, p.z);
            var sl = Place("AV_StoneLantern", new Vector3(p.x, y - 0.05f, p.z), Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg + R(-8, 8), 1.15f, lan);
            // glow in the fire box
            PointLight(new Vector3(p.x, y + 1.35f, p.z) + inward * 0.1f, Hex("#ff8a3a"), 1.6f, 1.5f, sl.transform, false, true);   // glow, ~2 m
            // the pool: a spot from just above the fire box, tilted into the arena so shadows run long away from it
            var go = new GameObject("LanternPool"); go.transform.SetParent(sl.transform, true);
            go.transform.position = new Vector3(p.x, y + 1.55f, p.z);
            go.transform.rotation = Quaternion.LookRotation(Vector3.down * 0.55f + inward, Vector3.up);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.color = Hex("#ff8636"); l.range = 5f; l.spotAngle = 130f; l.innerSpotAngle = 60f; l.intensity = 16f;
            bool sh = (i % Mathf.Max(1, spots.Count / ShadowedLanterns)) == 0 && shadowed < ShadowedLanterns;
            if (sh) { l.shadows = LightShadows.Hard; l.shadowStrength = 0.95f; l.shadowBias = 0.02f; l.shadowNormalBias = 0.3f; l.shadowResolution = UnityEngine.Rendering.LightShadowResolution.Medium; l.range = 7f; shadowed++; }
            else l.shadows = LightShadows.None;
            var f = go.AddComponent<AVFlicker>(); f.baseIntensity = l.intensity; f.amount = 0.12f; f.speed = 4f;
        }

        // ---- ground braziers (low bowls with fire and embers), near the edge so the middle stays clear
        var braz = new GameObject("Braziers").transform; braz.SetParent(look, false);
        foreach (var p0 in new[] { new Vector2(-38, -40), new Vector2(38, 32), new Vector2(10, 35), new Vector2(-41, -6) })   // r13 (approver: fire pits read as stamped glowing coins): 8 -> 4
        {
            float y = G(p0.x, p0.y);
            var bowl = new GameObject("Brazier"); bowl.transform.SetParent(braz, false); bowl.transform.position = new Vector3(p0.x, y, p0.y);   // r16: no round pit (read as a coin)
            for (int bi = 0; bi < 4; bi++) HalfRock(new Vector3(p0.x + R(-0.7f, 0.7f), y, p0.y + R(-0.7f, 0.7f)), R(0.3f, 0.55f), bowl.transform);
            var fire = new GameObject("Fire"); fire.transform.SetParent(bowl.transform, false); fire.transform.position = new Vector3(p0.x, y + 0.25f, p0.y);
            var ps = fire.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f); main.maxParticles = 50; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 30f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.28f; sh.rotation = new Vector3(-90, 0, 0);
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.4f), 0), new GradientColorKey(new Color(1f, 0.3f, 0.08f), 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = fire.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = flameMat; r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            var emb = new GameObject("Embers"); emb.transform.SetParent(bowl.transform, false); emb.transform.position = new Vector3(p0.x, y + 0.3f, p0.y);
            var es = emb.AddComponent<ParticleSystem>();
            var em2 = es.main; em2.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); em2.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2f);
            em2.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f); em2.maxParticles = 40; em2.simulationSpace = ParticleSystemSimulationSpace.World;
            var ee = es.emission; ee.rateOverTime = 12f;
            var esh = es.shape; esh.shapeType = ParticleSystemShapeType.Cone; esh.angle = 25f; esh.radius = 0.2f; esh.rotation = new Vector3(-90, 0, 0);
            var en = es.noise; en.enabled = true; en.strength = 0.6f; en.frequency = 1.2f; en.quality = ParticleSystemNoiseQuality.Low;
            es.GetComponent<ParticleSystemRenderer>().sharedMaterial = emberMat;
            es.Play();
            PointLight(new Vector3(p0.x, y + 0.7f, p0.y), Hex("#e05a24"), 1.8f, 3.6f, bowl.transform, false, true);   // ~4.7 m
        }

        // ---- ground debris: pebbles and plank scraps, merged into 16 m chunk meshes (one draw per chunk and material,
        // culled by the camera). Denser toward the edge and along the streets; nothing tall, nothing solid.
        var pebbleMat = mats["AV_Stone"]; var plankMat = mats["AV_Plank"];
        var deb = new GameObject("GroundDebris").transform; deb.SetParent(look, false);
        var pebbleSrc = PebbleMesh();
        var plankSrc = PlankMesh();
        const float DC = 16f;
        // the arena plus a margin only (the debris meshes are saved as one LFS asset, not into the scene: 11-10 the
        // scene reached 138 MB with them embedded and GitHub refused it)
        debrisMeshes.Clear();
        for (float cz = -56f; cz < 48f; cz += DC)
        for (float cx = -56f; cx < 56f; cx += DC)
        {
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pt = new List<int>();
            var kv = new List<Vector3>(); var kn = new List<Vector3>(); var ku = new List<Vector2>(); var kt = new List<int>();
            for (int i = 0; i < 0; i++)   // 11-12 r7 (Kido: floor objects must be downloaded, not made): procedural pebbles/plank scraps off
            {
                float x = cx + R(0, DC), z = cz + R(0, DC);
                float edge = Mathf.Max(Mathf.Abs(x) / ArenaHalfX, Mathf.Abs(z + 3.5f) / 45f);
                float sd = StreetDist(x, z);
                float dens = 0.28f + 0.55f * Mathf.Clamp01((edge - 0.55f) * 2.2f) + 0.35f * (1f - Mathf.Clamp01(sd / 3.5f));
                dens *= 0.45f + 0.9f * Mathf.PerlinNoise(x * 0.18f + 7f, z * 0.18f + 3f);
                if (Mathf.Abs(x) < 4.5f && z > 40.5f && z < 47.5f) continue;   // steps
                if (z > TerraceZ - 2.2f && z < TerraceZ + 0.8f) continue;          // retaining wall line
                if (rng.NextDouble() > dens) continue;
                var pos = new Vector3(x, G(x, z) - 0.02f, z);
                var rot = Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20));
                if (rng.NextDouble() < 0.88)
                {
                    float s = R(0.05f, 0.16f) * (rng.NextDouble() < 0.1 ? 2f : 1f);
                    Append(pebbleSrc, pos, rot, new Vector3(s, s * R(0.5f, 0.8f), s * R(0.8f, 1.2f)), pv, pn, pu, pt);
                }
                else
                {
                    var prot = Quaternion.Euler(R(-4, 4), R(0, 360), R(-6, 6));
                    Append(plankSrc, pos + Vector3.up * 0.02f, prot, new Vector3(R(0.5f, 1.5f), 1f, R(0.6f, 1.2f)), kv, kn, ku, kt);
                }
            }
            Chunk(deb, "Pebbles", pv, pn, pu, pt, pebbleMat, false);
            Chunk(deb, "Planks", kv, kn, ku, kt, plankMat, false);
        }
        var debPath = Root + "/Generated/AV_Debris.asset";
        if (!AssetDatabase.IsValidFolder(Root + "/Generated")) AssetDatabase.CreateFolder(Root, "Generated");
        AssetDatabase.DeleteAsset(debPath);
        if (debrisMeshes.Count > 0)
        {
            AssetDatabase.CreateAsset(debrisMeshes[0], debPath);
            for (int i = 1; i < debrisMeshes.Count; i++) AssetDatabase.AddObjectToAsset(debrisMeshes[i], debPath);
            AssetDatabase.SaveAssets();
        }

        // ---- the border, as in the burnt-camp ref: collapsed timber, carts and stumps heaped in the gaps behind the edge
        var bord = new GameObject("BorderDebris").transform; bord.SetParent(look, false);
        foreach (var (a, b) in new[] { (c00, c10), (c10, c11), (c11, c01) })
        {
            float L = Vector3.Distance(a, b); var dir = (b - a).normalized; var outward = Vector3.Cross(Vector3.up, dir);
            for (float t = R(2, 6); t < L - 2f; t += R(5f, 9f))
            {
                var p = Vector3.Lerp(a, b, t / L) + outward * R(3.6f, 4.8f);   // 11-13 QA: was 1.4-2.6 m, the 5 m piles reached into the arena
                if (StreetDist(p.x, p.z) < 3f) continue;
                double k = rng.NextDouble();
                string m = k < 0.35 ? "AV_PlankPile" : k < 0.55 ? "AV_TileRubble" : k < 0.7 ? "PH:dead_tree_trunk" : k < 0.82 ? "PH:tree_stump_01" : k < 0.92 ? "AV_FenceSsariBroken" : "AV_Cart";
                Place(m, new Vector3(p.x, G(p.x, p.z) - 0.05f, p.z), Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + R(-40, 40), R(0.85f, 1.15f), bord);
            }
        }
    }

    // A low-poly pebble (squashed, jittered octahedron, 8 faces) and a plank scrap (thin box), both in metres.
    class Src { public Vector3[] v, n; public Vector2[] uv; public int[] t; }
    static Src PebbleMesh()
    {
        var r = new System.Random(5);
        var basev = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down * 0.4f, Vector3.forward, Vector3.back };
        for (int i = 0; i < basev.Length; i++) basev[i] *= 0.8f + 0.4f * (float)r.NextDouble();
        int[] f = { 2, 4, 0, 2, 1, 4, 2, 5, 1, 2, 0, 5, 3, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0 };
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i < f.Length; i += 3)
        {
            var a = basev[f[i]]; var b = basev[f[i + 1]]; var c = basev[f[i + 2]];
            var nn = Vector3.Cross(b - a, c - a).normalized;
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
            uv.Add(new Vector2(a.x, a.z) * 0.5f); uv.Add(new Vector2(b.x, b.z) * 0.5f); uv.Add(new Vector2(c.x, c.z) * 0.5f);
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }
        return new Src { v = v.ToArray(), n = n.ToArray(), uv = uv.ToArray(), t = t.ToArray() };
    }
    static Src PlankMesh()
    {
        var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = m.GetComponent<MeshFilter>().sharedMesh;
        var s = new Src { v = mesh.vertices, n = mesh.normals, uv = mesh.uv, t = mesh.triangles };
        Object.DestroyImmediate(m);
        for (int i = 0; i < s.v.Length; i++) { s.v[i] = Vector3.Scale(s.v[i], new Vector3(1f, 0.035f, 0.14f)) + Vector3.up * 0.017f; s.uv[i] = new Vector2(s.uv[i].x * 2f, s.uv[i].y * 0.25f); }
        return s;
    }
    static void Append(Src s, Vector3 pos, Quaternion rot, Vector3 scale, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
    {
        int b = v.Count;
        for (int i = 0; i < s.v.Length; i++) { v.Add(pos + rot * Vector3.Scale(s.v[i], scale)); n.Add(rot * s.n[i]); uv.Add(s.uv[i] + new Vector2(pos.x, pos.z) * 0.37f); }
        for (int i = 0; i < s.t.Length; i++) t.Add(b + s.t[i]);
    }
    static readonly List<Mesh> debrisMeshes = new List<Mesh>();
    static void Chunk(Transform parent, string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Material mat, bool shadows)
    {
        if (v.Count == 0) return;
        var mesh = new Mesh { name = $"AV_{name}_{debrisMeshes.Count}", indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        var go = new GameObject(name); go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        debrisMeshes.Add(mesh);
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static Terrain BuildTerrain()
    {
        const float size = TerrainSize, hRange = TerrainHRange, baseY = TerrainBaseY;
        var dataPath = Root + "/AV_TerrainData.asset";
        AssetDatabase.DeleteAsset(dataPath);
        var td = new TerrainData { heightmapResolution = 1025, alphamapResolution = 1024 };   // same ~0.23 m cells over 240 m
        td.size = new Vector3(size, hRange, size);
        // 11-11 redo (refs board: trampled wet mud yard, cart ruts, leaf litter, gravel; no big flat colour patches)
        var layers = new[]
        {
            // 11-12 Kido: the previous ground textures were better -> back to them (mud_forest / tracks / pebbles / cracked dirt)
            // 11-12 r4 (Kido: the first image of the reference board): green mossy meadow and brown mud in big regions,
            // leafy mud and leaf-strewn gravel breaking them up (same textures the bamboo arena and reed field use)
            // 11-12 r5 (Kido: the floor he liked is the original 폐촌 terrain, not the green meadow): exactly the committed layers
            Layer("AV_L_Mud", "mud_forest_diff_2k.jpg", "mud_forest_nor_gl_2k.jpg", 6f, new Color(1.05f, 1.05f, 1.1f, 1)),
            // r7 (Kido: too repetitive, too much green): the forest mud is now one of three dark muds blended in big uneven
            // regions (Megascans moist mud, brown mud), so neither its tiling nor its green sprouts dominate
            Layer("AV_L_Tracks", MS + "mud/AV_MS_Mud_A.jpg", MS + "mud/AV_MS_Mud_N.jpg", 6.5f, new Color(0.46f, 0.43f, 0.43f, 1), 0.3f),
            Layer("AV_L_Leaves", "brown_mud_02_diff_2k.jpg", "brown_mud_02_nor_gl_2k.jpg", 9f, new Color(0.7f, 0.64f, 0.6f, 1), 0.25f),
            Layer("AV_L_Stony", "river_small_rocks_diff_2k.jpg", "river_small_rocks_nor_gl_2k.jpg", 2.6f, new Color(0.42f, 0.42f, 0.45f, 1)),
            Layer("AV_L_Paving", "stone_pathway_diff_2k.jpg", "stone_pathway_nor_gl_2k.jpg", 3.2f, new Color(1.3f, 1.32f, 1.38f, 1), 0.3f),
        };
        // 11-13 (Kido: "the one before the gravel field was much better"): back to the morning ground (these five layers,
        // SplatAt, the package TerrainLit, no macro overlay). The Fab ground (AVBuilder.Ground.cs) stays behind UseFabGround.
        if (UseFabGround) layers = GroundLayers();
        AssetDatabase.CreateAsset(td, dataPath);
        td.terrainLayers = layers;
        int hr = td.heightmapResolution;
        var h = new float[hr, hr];
        for (int j = 0; j < hr; j++)
            for (int i = 0; i < hr; i++)
            {
                float x = -size / 2 + size * i / (hr - 1), z = -size / 2 + size * j / (hr - 1);
                h[j, i] = Mathf.Clamp01((TerrainHeight(x, z) - baseY) / hRange);
            }
        td.SetHeights(0, 0, h);
        int ar = td.alphamapResolution;
        var a = new float[ar, ar, layers.Length];
        var wk = new float[layers.Length];
        for (int j = 0; j < ar; j++)
            for (int i = 0; i < ar; i++)
            {
                float x = -size / 2 + size * (i + 0.5f) / ar, z = -size / 2 + size * (j + 0.5f) / ar;
                if (UseFabGround) SplatGround(x, z, wk); else SplatAt(x, z, wk);
                for (int k = 0; k < wk.Length; k++) a[j, i, k] = wk[k];
            }
        td.SetAlphamaps(0, 0, a);
        // 11-12 Kido: "git에 남아있을거아니야" -> the painted ground is taken as-is from the committed terrain data
        // (Generated/AV_TerrainData_Committed.asset = git HEAD AV_TerrainData.asset); only the heights are new
        var committed = AssetDatabase.LoadAssetAtPath<TerrainData>(Root + "/Generated/AV_TerrainData_Committed.asset");
        if (false && committed && committed.alphamapResolution == ar && committed.alphamapLayers == layers.Length)   // r6: off, forest mud only
            td.SetAlphamaps(0, 0, committed.GetAlphamaps(0, 0, ar, ar));
        else Debug.LogWarning("[AV] committed terrain data missing or different, using the generated splat");
        {
            int ci = Mathf.RoundToInt((3f + size / 2) / size * ar), cj = Mathf.RoundToInt((0.5f + size / 2) / size * ar);
            File.AppendAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"),
                $"DEBUG street weights at (3,0.5): {a[cj, ci, 0]:F2} {a[cj, ci, 1]:F2} sd={StreetDist(3f, 0.5f):F2} layers={td.terrainLayers.Length} tex1={(layers[1].diffuseTexture ? layers[1].diffuseTexture.name : "null")}\n");
        }
        EditorUtility.SetDirty(td); AssetDatabase.SaveAssets();
        var go = Terrain.CreateTerrainGameObject(td);
        go.name = "Terrain"; go.transform.SetParent(root, false);
        go.transform.position = new Vector3(-size / 2, baseY, -size / 2);
        var t = go.GetComponent<Terrain>();
        t.materialTemplate = GroundMaterial();
        if (UseFabGround) BuildMacro(root);   // 11-13 r3: big light/colour patches over the tiling (AVBuilder.Ground.cs)
        t.heightmapPixelError = 3; t.basemapDistance = 200; t.drawInstanced = true;
        t.shadowCastingMode = ShadowCastingMode.On;
        // puddle water just under the street dips
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Puddles"; water.transform.SetParent(root, false);
        water.transform.position = new Vector3(0, -0.36f, -20); water.transform.localScale = new Vector3(18, 1, 14);
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.GetComponent<Renderer>().sharedMaterial = mats["AV_Puddle"];
        water.SetActive(false);   // 11-12: read as blue holes on the single pebble ground (Kido); kept off
        return t;
    }

    // ------------------------------------------------------------------ scene
    public static void Build()
    {
        rng = new System.Random(1592);
        mats.Clear();
        foreach (var stray in new[] { "old_military_crate", "Lantern_01", "Barrel_01", "wooden_lantern_01", "barrel_03", "stone_01", "rock_07", "rock_09", "namaqualand_stones_01", "dry_branches_medium_01", "tree_stump_02", "planter_pot_clay", "ceramic_pot", "shrub_04", "grass_medium_02" })
            if (AssetDatabase.IsValidFolder(PH + stray)) AssetDatabase.DeleteAsset(PH + stray);
        foreach (var unused in new[] { "dark_wooden_planks", "stacked_stone_wall", "stony_dirt_path", "grey_roof_tiles", "forest_leaves_02" })
            foreach (var guid in AssetDatabase.FindAssets(unused, new[] { Root + "/Textures" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
        BuildMaterials();
        RemapModels();
        RemapMegascans();
        // r12: the Poly Haven materials are made once at import, so the night tints are applied here on every build
        foreach (var guid in AssetDatabase.FindAssets("PH_ t:Material", new[] { MatDir }))
        {
            var pm = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (!pm || pm.name.Contains("grass") || pm.name.Contains("fern") || pm.name.Contains("shrub")) continue;
            var c = pm.name.Contains("stool") || pm.name.Contains("bucket") || pm.name.Contains("broom") ? Hex("#4e463c") : pm.name.Contains("stump") || pm.name.Contains("trunk") ? Hex("#6a5c4c") : pm.name.Contains("rock") || pm.name.Contains("fire_pit") ? Hex("#5e5952") : Hex("#8e857a");
            pm.SetColor("_BaseColor", c); EditorUtility.SetDirty(pm);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("AbandonedVillage").transform;
        root.gameObject.AddComponent<AVSeeThrough>();   // 11-12: see-through trees around the player
        seeBark = seeTwig = null; treeMats.Clear(); houseBounds = null; bareBark = bareTwig = bareTrunkB = null;
        var terrain = BuildTerrain();
        System.Func<float, float, float> G = (x, z) => Ground(terrain, x, z);

        // ---------------- buildings (front of every model faces local -Z)
        // 11-10: laid out again for the 92 x 90 arena. The rows keep the old spacing and get longer, so the edge is
        // still a continuous wall of houses with the same density.
        var bld = Group("Buildings");
        void House(string m, float x, float z, float yaw, float sink = 0.05f) => Place(m, new Vector3(x, G(x, z) - sink, z), yaw, 1f, bld);
        // north terrace
        House("AV_Hanok2F_A", 0f, 55f, 0f);
        House("AV_Choga_C", -28f, 53f, 4f);
        House("AV_Watchtower", 25f, 51f, -8f);
        House("AV_ChogaCollapsed", 46f, 54.5f, 10f);
        House("AV_Hanok2F_B", -49f, 54.5f, 12f);
        House("AV_Choga_A", -14f, 54f, -3f);
        House("AV_Choga_B", 12.5f, 55.5f, 6f);
        House("AV_Hanok2F_C", 35.5f, 55f, -5f);
        House("AV_Choga_C", -38.5f, 55.5f, 8f);
        // west and east: packed rows of 2-3 storey wooden houses, fronts facing the arena, some leaning or half fallen
        string[] westRow = { "AV_Tall2F_Broken", "AV_Tall3F_A", "AV_Tall2F_Lean", "AV_Tall3F_Broken", "AV_Tall2F_A", "AV_Tall3F_Lean", "AV_Tall2F_A" };
        string[] eastRow = { "AV_Tall3F_Lean", "AV_Tall2F_A", "AV_Tall3F_Broken", "AV_Tall2F_Lean", "AV_Tall3F_A", "AV_Tall2F_Broken", "AV_Tall3F_A" };
        const int RowCount = 13;
        var westZ = new float[RowCount]; var eastZ = new float[RowCount];
        for (int i = 0; i < RowCount; i++)
        {
            float z = -47f + i * 7.2f + R(-0.3f, 0.3f);
            westZ[i] = z; eastZ[i] = z + 1.2f;
            House(westRow[(i * 3 + 1) % westRow.Length], -RowX + R(-0.3f, 0.3f), z, -90f + R(-4f, 4f));
            House(eastRow[(i * 5 + 2) % eastRow.Length], RowX + R(-0.3f, 0.3f), z + 1.2f, 90f + R(-4f, 4f));
        }
        // second rows behind, taller roofs peeking over
        string[] back = { "AV_Tall3F_A", "AV_Hanok2F_C", "AV_Tall3F_Lean", "AV_Choga_C", "AV_Tall3F_Broken", "AV_Hanok2F_B" };
        for (int i = 0; i < 11; i++)
        {
            float z = -44f + i * 8.4f;
            House(back[i % back.Length], -BackRowX + R(-0.6f, 0.6f), z, -90f + R(-6f, 6f));
            House(back[(i + 3) % back.Length], BackRowX + R(-0.6f, 0.6f), z + 2f, 90f + R(-6f, 6f));
        }
        // south row behind the low wall, fronts face north; the street to the jangseung gate runs out at x ~ -20
        // 11-12 (Kido: volume like the Tarae/POE2 refs): the camera side gets tall 2-3 storey houses and hanok roofs on the
        // raised south bank (see SouthBank), so their roofs and the big trees in front loom into the bottom of the frame
        BuildSouthVolume(G, bld);
        // ---------------- terrace: retaining wall, steps and lanterns
        var ter = Group("Terrace");
        for (int k = 0; k < 16; k++)
            foreach (var side in new[] { -1f, 1f })
            {
                float x = side * (5.75f + 5.95f * k);
                Place("AV_RetainingWall", new Vector3(x, -0.25f, TerraceZ - 0.75f), R(-1.2f, 1.2f), 1f, ter);
                if ((k == 2 && side > 0) || (k == 1 && side < 0) || (k == 9 && side > 0) || (k == 11 && side < 0)) Place("AV_TileRubble", new Vector3(x, G(x, TerraceZ - 2.4f), TerraceZ - 2.4f), R(0, 360), 0.8f, ter);
            }
        Place("AV_StoneSteps", new Vector3(0, -0.05f, TerraceZ - 3.6f), 0f, 1f, ter);
        // steps local origin is the bottom front edge; model rises along +Z
        foreach (var sx in new[] { -3.3f, 3.3f })
        {
            var sl = Place("AV_StoneLantern", new Vector3(sx, G(sx, TerraceZ - 4.1f), TerraceZ - 4.1f), R(-10, 10), 1f, ter);
            PointLight(sl.transform.position + Vector3.up * 1.35f, Hex("#ff9440"), 3.5f, 7f, sl.transform);
            var st = Place("AV_StoneLantern", new Vector3(sx * 0.9f, TerraceY, TerraceZ + 1.2f), R(-10, 10), 1f, ter);
            PointLight(st.transform.position + Vector3.up * 1.35f, Hex("#ff9440"), 3f, 7f, st.transform);
        }

        // ---------------- walls, fences
        var fen = Group("Fences");
        void Run(string a, string b, Vector3 from, Vector3 to, float seg, float breakChance, float gapChance)
        {
            var d = to - from; int n = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / seg));
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg - 90f;
            for (int i = 0; i < n; i++)
            {
                if (rng.NextDouble() < gapChance) continue;
                var p = Vector3.Lerp(from, to, (i + 0.5f) / n);
                p.y = G(p.x, p.z) - 0.05f;
                Place(rng.NextDouble() < breakChance ? b : a, p, yaw + R(-4, 4), 1f, fen);
            }
        }
        // fences only along the arena's side edges, in the yards in front of the house rows
        float fx = ArenaHalfX + 0.4f;
        Run("AV_FenceSsari", "AV_FenceSsariBroken", new Vector3(-fx, 0, -20.5f), new Vector3(-fx, 0, -4.5f), 3f, 0.5f, 0.2f);
        Run("AV_FenceSsari", "AV_FenceSsariBroken", new Vector3(-fx, 0, 20f), new Vector3(-fx, 0, 32f), 3f, 0.5f, 0.2f);
        Run("AV_FencePlank", "AV_FencePlankBroken", new Vector3(fx, 0, 19.5f), new Vector3(fx, 0, 35.5f), 3f, 0.5f, 0.2f);
        Run("AV_FencePlank", "AV_FencePlankBroken", new Vector3(fx, 0, -40f), new Vector3(fx, 0, -26f), 3f, 0.5f, 0.2f);
        // low south wall on the arena's south edge, gap for the jangseung gate where the street leaves
        float wz = ArenaMinZ - 1.3f;
        const float GateX = -17.6f;   // where the south street crosses the wall
        Run("AV_StoneWall", "AV_StoneWallBroken", new Vector3(-ArenaHalfX - 1f, 0, wz), new Vector3(GateX - 3f, 0, wz), 4f, 0.45f, 0.08f);
        Run("AV_StoneWall", "AV_StoneWallBroken", new Vector3(GateX + 3f, 0, wz), new Vector3(ArenaHalfX + 1f, 0, wz), 4f, 0.45f, 0.08f);
        Run("AV_StoneWall", "AV_StoneWallBroken", new Vector3(-20, TerraceY, 60.5f), new Vector3(20, TerraceY, 60.5f), 4f, 0.4f, 0.05f);

        // ---------------- street furniture, props: all outside the arena (south strip behind the wall, north terrace)
        var props = Group("Props");
        float sz = ArenaMinZ - 3.2f;   // strip between the south wall and the south houses
        Place("AV_Well", new Vector3(-29f, TerraceY, 48.8f), 20f, 1f, props);
        Place("AV_Cart", new Vector3(11f, G(11f, sz), sz), 80f, 1f, props);
        Place("AV_Cart", new Vector3(-52f, G(-52f, sz), sz), 100f, 1f, props);
        Place("AV_Jangdokdae", new Vector3(-20f, TerraceY, 51f), 0f, 1f, props);
        Place("AV_Jangdokdae", new Vector3(-44f, G(-44f, sz), sz), 180f, 1f, props);
        Place("AV_Jangdokdae", new Vector3(31f, G(31f, sz), sz), 175f, 1f, props);
        Place("AV_Jangseung", new Vector3(GateX - 2.4f, G(GateX - 2.4f, wz - 1.2f), wz - 1.2f), 170f, 1f, props);
        Place("AV_Jangseung", new Vector3(GateX + 2.4f, G(GateX + 2.4f, wz - 1.2f), wz - 1.2f), 190f, 0.92f, props);
        Place("AV_Sotdae", new Vector3(GateX + 5.3f, G(GateX + 5.3f, wz - 1.6f), wz - 1.6f), 0f, 1f, props);
        Place("AV_Sotdae", new Vector3(14f, TerraceY, 49.5f), 40f, 1.1f, props);
        foreach (var p in new[] { new Vector3(-38f, 0, sz), new Vector3(39f, 0, sz - 0.2f), new Vector3(-3f, 0, sz - 0.3f), new Vector3(19f, TerraceY, 48.7f), new Vector3(-29.5f, 0, sz + 0.2f), new Vector3(24f, 0, sz - 0.1f) })
        {
            float y = p.y > 0 ? p.y : G(p.x, p.z);
            Place(rng.NextDouble() < 0.5 ? "AV_OnggiBroken" : "AV_Onggi", new Vector3(p.x, y, p.z), R(0, 360), R(0.8f, 1.1f), props);
            Place("AV_OnggiBroken", new Vector3(p.x + R(-1.2f, 1.2f), y, p.z + R(-0.5f, 0.5f)), R(0, 360), R(0.6f, 0.9f), props);
        }
        Place("PH:wooden_barrels_01", new Vector3(43f, G(43f, sz), sz), 80f, 1f, props);
        Place("PH:wooden_barrels_01", new Vector3(-36f, G(-36f, sz - 0.3f), sz - 0.3f), 20f, 1f, props);
        Place("AV_PlankPile", new Vector3(3f, G(3f, sz - 0.4f) - 0.02f, sz - 0.4f), R(0, 360), R(0.8f, 1.0f), props);
        Place("AV_PlankPile", new Vector3(34f, G(34f, sz - 0.3f) - 0.02f, sz - 0.3f), R(0, 360), R(0.8f, 1.0f), props);
        Place("AV_StrawHeap", new Vector3(-31f, G(-31f, sz) - 0.12f, sz), R(0, 360), 0.65f, props);
        Place("AV_StrawHeap", new Vector3(-6f, G(-6f, sz) - 0.12f, sz), R(0, 360), 0.6f, props);
        // stepping stones only where the streets leave the arena
        foreach (var s in new[] { Streets[0], Streets[1], Streets[2] })
            for (int i = 0; i < s.Length - 1; i++)
            {
                var a = s[i]; var b = s[i + 1]; float L = Vector2.Distance(a, b);
                for (float t = 2f; t < L - 1f; t += R(4.5f, 7f))
                {
                    var q = Vector2.Lerp(a, b, t / L) + new Vector2(R(-0.6f, 0.6f), R(-0.6f, 0.6f));
                    float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                    if (InArena(q.x, q.y, -1.5f)) continue;
                    Place("AV_StreetStones", new Vector3(q.x, G(q.x, q.y) - 0.04f, q.y), yaw + R(-8, 8), 1f, props);
                }
            }
        Place("PH:wooden_bucket_01", new Vector3(-25.2f, TerraceY, 48.3f), 40f, 1f, props);
        Place("PH:wooden_bucket_02", new Vector3(-26f, G(-26f, sz), sz), 0f, 1f, props);
        Place("PH:wicker_basket_01", new Vector3(9.2f, TerraceY, 49.9f), 30f, 1.2f, props);
        Place("PH:wooden_crate_01", new Vector3(35f, G(35f, sz + 0.3f), sz + 0.3f), 15f, 1f, props);
        Place("PH:wooden_crate_01", new Vector3(-48f, G(-48f, sz), sz), 55f, 1f, props);
        Place("PH:stone_fire_pit", new Vector3(25f, G(25f, sz), sz), 0f, 1f, props);
        Place("PH:wooden_stool_01", new Vector3(23.5f, G(23.5f, sz + 0.6f), sz + 0.6f), 40f, 1f, props);
        foreach (var p in new[] { new Vector3(-8f, TerraceY, 58f), new Vector3(18f, 0, sz - 0.6f), new Vector3(-56f, 0, sz - 0.4f) })
            Place("AV_TileRubble", new Vector3(p.x, p.y > 0 ? p.y : G(p.x, p.z), p.z), R(0, 360), 1f, props);

        // ---------------- rocks at the village edge
        var rocks = Group("Rocks");
        foreach (var p in new[] { new Vector3(-68, 0, -19), new Vector3(68, 0, -24.5f), new Vector3(-57, 0, 61.5f), new Vector3(59, 0, -61.5f), new Vector3(-61, 0, -61.5f), new Vector3(70, 0, 13), new Vector3(-71, 0, 9),
                                  new Vector3(-70, 0, 36), new Vector3(71, 0, 40), new Vector3(-24, 0, -70), new Vector3(30, 0, -72), new Vector3(-30, 0, 68), new Vector3(28, 0, 70), new Vector3(72, 0, -45) })
            Place(rng.NextDouble() < 0.5 ? "PH:rock_moss_set_01" : "PH:rock_moss_set_02", new Vector3(p.x, G(p.x, p.z) - 0.3f, p.z), R(0, 360), R(0.9f, 1.3f), rocks);

        // ---------------- trees: none in the arena
        var trees = Group("Trees");
        var bigTree = DeadScan(new Vector3(-40f, TerraceY - 0.1f, 50f), 1.15f * 1.15f, trees);
        DeadScan(new Vector3(-7f, TerraceY - 0.1f, 49.7f), 1f * 1.15f, trees);
        DeadScan(new Vector3(-2f, G(-2f, sz - 1f) - 0.1f, sz - 1f), 1.1f * 1.15f, trees);
        DeadScan(new Vector3(37f, TerraceY - 0.1f, 49.5f), 1.1f * 1.15f, trees);
        DeadScan(new Vector3(48f, G(48f, sz - 1f) - 0.1f, sz - 1f), 1.0f * 1.15f, trees);
        DeadScan(new Vector3(-53f, G(-53f, sz - 2.4f) - 0.1f, sz - 2.4f), 1.05f * 1.15f, trees);
        for (int i = 0; i < 68; i++)
        {
            float a = i / 68f * Mathf.PI * 2f + R(-0.04f, 0.04f), r = R(70f, 86f);
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            DeadScan(new Vector3(x, G(x, z) - 0.15f, z), R(1.1f, 1.6f), trees);
        }

        // ---------------- dry grass: dense at the edges, sparse in the arena, none on the street
        BuildRuins(G);   // 11-11 redo: ruin islands on the ring inside the arena edge + small things in the middle
        BuildPines(G);   // 11-12: Japanese black pines and conifers from the snow thread's import (copied materials)
        var grass = Group("Grass");
        int placed = BuildVegetation(G, grass);
        for (int i = 0; i < 0; i++)   // 11-11: old even scatter replaced by BuildVegetation (clusters)
        {
            float x = R(-92f, 92f), z = R(-92f, 92f);
            float sd = StreetDist(x, z);
            if (sd < 2.2f) continue;
            float edge = Mathf.Max(Mathf.Abs(x) / 46f, Mathf.Abs(z + 3.5f) / 46f);
            float dens = Mathf.Clamp01((edge - 0.55f) * 1.5f) * 0.9f + 0.16f;
            dens *= Mathf.Clamp01((sd - 2.2f) / 2.5f);
            dens *= 0.6f + 0.8f * Mathf.PerlinNoise(x * 0.12f, z * 0.12f);
            if (rng.NextDouble() > dens) continue;
            if (Mathf.Abs(x) < 4.5f && z > 40.5f && z < 47.5f) continue; // steps
            string m = rng.NextDouble() < 0.45 ? "AV_DryGrass_A" : (rng.NextDouble() < 0.5 ? "AV_DryGrass_C" : "AV_DryGrass_B");
            Place(m, new Vector3(x, G(x, z) - 0.03f, z), R(0, 360), R(0.75f, 1.35f), grass);
            placed++;
        }
        for (int i = 0; i < 0; i++)
        {
            float x = R(-80f, 80f), z = R(-80f, 80f);
            if (StreetDist(x, z) < 3f || InArena(x, z, -1f)) continue;
            Place(rng.NextDouble() < 0.5 ? "PH:grass_medium_01" : "PH:fern_02", new Vector3(x, G(x, z), z), R(0, 360), R(0.8f, 1.3f), grass);
        }

        // ---------------- lanterns
        var lights = Group("Lanterns");
        void Post(float x, float z, float yaw, bool shadows = false)
        {
            var p = Place("AV_LanternPost", new Vector3(x, G(x, z), z), yaw, 1f, lights);
            // the arm points along local +X in Blender; follow wherever the import put it
            var armSide = Mathf.Sign(p.transform.InverseTransformPoint(p.GetComponentInChildren<Renderer>().bounds.center).x);
            var hp = p.transform.TransformPoint(new Vector3(armSide * 0.75f, 2.15f, 0f));
            var lan = Place("AV_HangingLantern", hp, R(0, 360), 1f, p.transform);
            lan.name = "HangingLantern";
            PointLight(hp, Hex("#ff8a3a"), 4f, 3.8f, lan.transform, shadows);   // ~5 m (11-10 light budget)
        }
        // posts stand on the arena edges (side yards, south wall, foot of the terrace); arms run along the edge.
        // The first eight keep their shadows; the extra ones on the longer edges are unshadowed (frame time).
        float px = ArenaHalfX + 0.9f;
        Post(-px, -28.5f, 90f);
        Post(-px, 11.5f, 90f);
        Post(px, -8.5f, 90f);
        Post(px, 27.5f, 90f);
        Post(-34f, ArenaMinZ - 1.4f, 0f);
        Post(32f, ArenaMinZ - 1.4f, 0f);
        Post(-18f, ArenaMaxZ + 2.5f, 0f);
        Post(20f, ArenaMaxZ + 2.5f, 0f);
        Post(-px, -8.5f, 90f, false);
        Post(-px, 32f, 90f, false);
        Post(px, -32f, 90f, false);
        Post(px, 8f, 90f, false);
        Post(2f, ArenaMinZ - 1.4f, 0f, false);
        Post(-40f, ArenaMaxZ + 2.5f, 0f, false);
        Post(40f, ArenaMaxZ + 2.5f, 0f, false);
        float hx = RowX - 3.7f, ix = RowX - 0.3f;   // house fronts / inside the houses
        void Hang(Vector3 at)
        {
            var lan = Place("AV_HangingLantern", at, R(0, 360), 1f, lights);
            PointLight(at, Hex("#ff8a3a"), 3.2f, 3.6f, lan.transform);
        }
        // lanterns hang off particular house fronts, so they follow the row positions
        Hang(new Vector3(hx, 2.6f, eastZ[2] - 0.6f));
        Hang(new Vector3(hx, 2.6f, eastZ[7] - 0.6f));
        Hang(new Vector3(hx, 2.6f, eastZ[11] - 0.6f));
        Hang(new Vector3(-hx, 2.6f, westZ[5] + 0.6f));
        Hang(new Vector3(-hx, 2.6f, westZ[9] + 0.6f));
        Hang(new Vector3(-hx, 2.6f, westZ[1] + 0.6f));
        PointLight(new Vector3(ix, 4.4f, eastZ[9]), Hex("#ff8c3c"), 2f, 5.5f, lights);
        Hang(new Vector3(-2.5f, TerraceY + 4.2f, 50.9f));
        Hang(new Vector3(2.5f, TerraceY + 4.2f, 50.9f));
        Hang(new Vector3(26f, TerraceY + 8.2f, 49.3f));
        // warm light inside a few houses behind the glowing paper doors
        PointLight(new Vector3(0, TerraceY + 1.5f, 53.5f), Hex("#ff8c3c"), 2.5f, 6f, lights);
        PointLight(new Vector3(-ix, 4.4f, westZ[4]), Hex("#ff8c3c"), 2.2f, 5.5f, lights);
        PointLight(new Vector3(ix, 1.6f, eastZ[5]), Hex("#ff8c3c"), 2.2f, 5.5f, lights);
        PointLight(new Vector3(-ix, 1.6f, westZ[10]), Hex("#ff8c3c"), 2.2f, 5.5f, lights);
        // the open arena is wide: soft unshadowed fills so the middle doesn't go black
        PointLight(new Vector3(-16f, 6f, -12f), Hex("#7f8fb0"), 0.8f, 18f, lights, false, false);
        PointLight(new Vector3(18f, 6f, 8f), Hex("#7f8fb0"), 0.8f, 18f, lights, false, false);
        PointLight(new Vector3(-20f, 6f, 24f), Hex("#7f8fb0"), 0.8f, 18f, lights, false, false);
        PointLight(new Vector3(22f, 6f, -32f), Hex("#7f8fb0"), 0.8f, 18f, lights, false, false);

        // ---------------- 도깨비불 wisps
        var wisps = Group("Wisps");
        foreach (var p in new[] { new Vector3(-ArenaHalfX - 0.5f, 1.4f, 11.5f), new Vector3(ArenaHalfX + 0.5f, 1.2f, -24.5f), new Vector3(-10f, TerraceY + 1.6f, 56.5f), new Vector3(12f, 1.0f, ArenaMinZ - 2.5f),
                                  new Vector3(ArenaHalfX + 0.5f, 1.3f, 20f), new Vector3(-ArenaHalfX - 0.5f, 1.2f, -32f), new Vector3(36f, 1.0f, ArenaMinZ - 2.5f), new Vector3(-36f, TerraceY + 1.6f, 52f) })
        {
            var w = new GameObject("Dokkaebibul"); w.transform.SetParent(wisps, false); w.transform.position = p;
            w.AddComponent<AVWisp>();
            var ps = w.AddComponent<ParticleSystem>();
            var main = ps.main; main.startLifetime = 1.2f; main.startSpeed = 0.15f; main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startColor = new Color(0.5f, 1f, 1f, 0.8f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 60;
            var em = ps.emission; em.rateOverTime = 22f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.12f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(new Color(0.6f, 1f, 1f), 0), new GradientColorKey(new Color(0.2f, 0.6f, 1f), 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.9f, 0.2f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            w.GetComponent<ParticleSystemRenderer>().sharedMaterial = mats["AV_WispMat"];
            ps.Simulate(2f, true, true); ps.Play();
            var l = PointLight(p, new Color(0.35f, 0.85f, 1f), 2.4f, 3f, w.transform);
            l.GetComponent<AVFlicker>().amount = 0.4f;
        }

        // ---------------- crows
        var crows = Group("Crows");
        void Perch(Vector3 p, float yaw) => Place("AV_Crow", p, yaw, 1.3f, crows);
        Perch(new Vector3(0.6f, TerraceY + 9.4f, 55f), 160f);
        Perch(new Vector3(-1.4f, TerraceY + 9.35f, 54.9f), 200f);
        Perch(new Vector3(25.7f, TerraceY + 7.15f, 49.6f), 190f);
        foreach (var r in bigTree ? new[] { bigTree.GetComponentInChildren<MeshFilter>() } : new MeshFilter[0])
        {
            var b = r.sharedMesh.bounds; var top = r.transform.TransformPoint(b.center + Vector3.up * b.extents.y * 0.55f);
            Perch(top + new Vector3(0.6f, 0, 0.2f), 140f);
            Perch(top + new Vector3(-0.9f, -0.6f, -0.4f), 210f);
        }
        foreach (var (centre, count) in new[] { (new Vector3(-18f, 0, 18.5f), 7), (new Vector3(24f, 0, -22f), 5) })
            for (int i = 0; i < count; i++)
            {
                var c = Place("AV_CrowFly", Vector3.zero, 0, 1.1f, crows);
                var circ = c.AddComponent<AVCrowCircle>();
                circ.center = centre; circ.radius = R(4f, 9f); circ.height = R(9f, 13f); circ.speed = R(0.25f, 0.45f) * (i % 3 == 0 ? -1 : 1); circ.phase = R(0, 6.3f);
                float aa = circ.phase; c.transform.position = circ.center + new Vector3(Mathf.Cos(aa) * circ.radius, circ.height, Mathf.Sin(aa) * circ.radius);
                c.transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(aa), 0, Mathf.Cos(aa)));
            }

        // ================ 11-10 look pass (Kido's refs: burnt camp in cold fog, stone lanterns throwing warm pools with long
        // shadows over cracked dirt full of pebbles and plank scraps, a purple energy wall closing the arena edge).
        BuildLook(G);
        { var veil = root.Find("Look/EdgeVeil"); if (veil) Object.DestroyImmediate(veil.gameObject); }   // 11-12 Kido: no purple border
        // BuildPuddles: 11-13 r9b off, without real reflections the puddles read as black smudges in the game view
        BuildAsh(root);
        charMat = null; BuildBurntFootprints(G, root);   // r14: flat burnt-house remains in the yard
        BuildFloorDressing(G);   // 11-12: low Megascans pebbles, rocks, rubble, leaf and blood decals all over the arena

        // ---------------- fog
        var fog = Group("Fog");
        void FogField(string name, Vector3 c, Vector3 size, int count, float sizeMin, float sizeMax, float alpha)
        {
            var go = new GameObject(name); go.transform.SetParent(fog, false); go.transform.position = c;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = 40f; main.startSpeed = 0f; main.maxParticles = count;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(1, 1, 1, alpha); main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
            var em = ps.emission; em.rateOverTime = count / 40f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.45f); vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mats["AV_FogMat"]; r.sortingFudge = 10;
            r.renderMode = ParticleSystemRenderMode.Billboard; r.maxParticleSize = 3f;
            ps.Simulate(40f, true, true); ps.Play();
        }
        FogField("GroundFog", new Vector3(0, 0.6f, -3.5f), new Vector3(180, 0.8f, 180), 880, 7f, 14f, 0.32f);
        FogField("EdgeFog", new Vector3(0, 2.5f, 24f), new Vector3(220, 4f, 160), 480, 14f, 24f, 0.3f);

        // ---------------- light, sky, fog, post
        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.transform.SetParent(root, false);
        moon.type = LightType.Directional; moon.color = Hex("#9fb3d6"); moon.intensity = 3.0f; moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.85f;
        moon.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
        RenderSettings.sun = moon;
        MoonCookie(moon);   // 11-13 r9: cloud / branch shadow drifting over the ground
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#46546a"); RenderSettings.ambientEquatorColor = Hex("#2c3440"); RenderSettings.ambientGroundColor = Hex("#171a20");
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.013f; RenderSettings.fogColor = Hex("#4d5a6b");
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

        var probeGo = new GameObject("ReflectionProbe"); probeGo.transform.SetParent(root, false); probeGo.transform.position = new Vector3(0, 6f, 0);
        var rp = probeGo.AddComponent<ReflectionProbe>(); rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        rp.size = new Vector3(240, 40, 240); rp.resolution = 128; rp.boxProjection = false; rp.RenderProbe();

        var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
        var profPath = Root + "/AV_PostFX.asset";
        AssetDatabase.DeleteAsset(profPath);
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, profPath);
        var bloom = prof.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(1.1f); bloom.scatter.Override(0.72f); bloom.tint.Override(Hex("#ffd2a8"));
        var ca = prof.Add<ColorAdjustments>(true); ca.saturation.Override(-32f); ca.contrast.Override(18f); ca.postExposure.Override(1.1f); ca.colorFilter.Override(Hex("#dfe8f5"));
        var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(-14f); wb.tint.Override(3f);
        var smh = prof.Add<ShadowsMidtonesHighlights>(true); smh.shadows.Override(new Vector4(0.86f, 0.95f, 1.12f, -0.05f)); smh.highlights.Override(new Vector4(1.05f, 1.0f, 0.95f, 0f));
        var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.5f); vig.color.Override(Hex("#05070a"));
        var tone = prof.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.ACES);
        var grain = prof.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.18f);
        foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
        vol.sharedProfile = prof;

        // ---------------- camera matching RoomRun's CameraRig (height 17, back 14.5, fov 30)
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.transform.SetParent(root, false);
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f; cam.allowHDR = true;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#2d3540");
        var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.High;
        cd.requiresDepthOption = CameraOverrideOption.On;
        SetGameView(cam, Vector3.zero, 1f);

        int blockers = CheckArena();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[AV] built " + ScenePath + " grass=" + placed + " arenaBlockers=" + blockers);
    }

    // Logs every solid object reaching into the arena below head height (grass, fog, wisps, crows, puddles and the
    // terrain are fine) and every collider other than the terrain's. Returns how many were found.
    static int CheckArena()
    {
        var log = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt");
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var g = r.transform; while (g.parent && g.parent != root) g = g.parent;
            if (g.name is "Grass" or "Fog" or "Wisps" or "Crows" or "Terrain" or "Puddles" or "Ruins" || r is ParticleSystemRenderer) continue;
            if (r.name.Contains("HangingLantern") || (r.transform.parent && r.transform.parent.name == "HangingLantern")) continue;
            var b = r.bounds;
            if (b.min.y > 2.2f) continue;
            if (b.max.x <= -ArenaHalfX || b.min.x >= ArenaHalfX || b.max.z <= ArenaMinZ || b.min.z >= ArenaMaxZ) continue;
            float ox = Mathf.Min(b.max.x, ArenaHalfX) - Mathf.Max(b.min.x, -ArenaHalfX), oz = Mathf.Min(b.max.z, ArenaMaxZ) - Mathf.Max(b.min.z, ArenaMinZ);
            n++; File.AppendAllText(log, $"ARENA {g.name}/{r.transform.root.name}:{r.name} at {r.transform.position} overlap {ox:F1}x{oz:F1}\n");
        }
        foreach (var c in root.GetComponentsInChildren<Collider>())
            if (!(c is TerrainCollider) && !c.transform.IsChildOf(root.Find("Ruins"))) { n++; File.AppendAllText(log, $"COLLIDER {c.name} {c.GetType().Name}\n"); }
        File.AppendAllText(log, $"ARENA CHECK {n} ({ArenaHalfX * 2}x{ArenaMaxZ - ArenaMinZ} m)\n");
        return n;
    }

    public static void SetGameView(Camera cam, Vector3 focus, float zoom)
    {
        var offset = new Vector3(0, 17f, -14.5f) * zoom;
        cam.transform.position = focus + offset;
        cam.transform.rotation = Quaternion.LookRotation(-offset);
    }
}
