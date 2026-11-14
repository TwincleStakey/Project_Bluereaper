// 11-13 (Kido: "is the floor from Fab or made in code? ... feels nothing like POE2"; textures from Fab only, quality
// first): the ground was one dark pebble/mud texture tinted grey everywhere, so the arena read as one repeated pattern.
// It now uses four free Quixel Megascans surfaces from Fab (Standard License) with their height maps, and the terrain
// blends them by height, so stones, roots and leaves poke through each other instead of a soft cross-fade:
//   packed dirt with roots (base, the trampled village yard), moist fallen leaves (hollows, drifts and the edges),
//   dry forest floor (needle/grass litter in big patches away from the streets), gravel on soil (streets, broken patches).
// Mask maps (R 0, G AO, B displacement, A gloss) and GL normals (Megascans ships DirectX) are made from the downloaded
// sets by ArtSource/AbandonedVillage/fabmask.py; the downloads stay in ArtSource/AbandonedVillage/fab_ground.
using UnityEditor;
using UnityEngine;

public static partial class AVBuilder
{
    // 11-13: Kido took the revert back ("the texture is fine, it's too empty"): the Fab ground stays on
    const bool UseFabGround = true;

    const string FG = MS + "Ground/AV_FG_";

    static TerrainLayer GLayer(string name, string id, Vector2 size, Color tint, float normal, float gloss, float height = 1f)
    {
        var l = Layer(name, FG + id + "_A.jpg", FG + id + "_N.jpg", size.x, tint, 0.2f);
        l.tileSize = size;
        var mp = FG + id + "_M.png";
        var imp = AssetImporter.GetAtPath(mp) as TextureImporter;
        if (imp && (imp.sRGBTexture || imp.maxTextureSize > 2048)) { imp.sRGBTexture = false; imp.maxTextureSize = 2048; imp.SaveAndReimport(); }
        l.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(mp);
        l.maskMapRemapMin = Vector4.zero;
        l.maskMapRemapMax = new Vector4(1f, 1f, height, gloss);
        l.normalScale = normal;
        EditorUtility.SetDirty(l);
        return l;
    }

    // scan areas: roots 2x1 m, forest floor 2x2 m, leaves 0.36 m, gravel 0.39 m; scaled up ~2-4x for the top-down camera
    // 11-13 r5 (Kido: "far too much gravel, it reads like a riverbed, not a village"): the leaf litter read as pebbles
    // from the game camera, plus the gravel layer and 1700 scattered stones. Now a village yard: trampled earth with
    // straw bits as the base, paler dry soil in patches, cart-track mud along the streets, sparse grass in the corners.
    // r6 (Kido: "more round marks on the floor now"): the goat-pen earth is full of round droppings and the pale dry
    // soil came out as round blotches. Base is now the plain packed cart-track mud; dry soil only as a faint, darker,
    // stretched drift; sparse grass toward the edges.
    static TerrainLayer[] GroundLayers() => new[]
    {
        GLayer("AV_G_Tracks", "Tracks", new Vector2(5f, 5f), new Color(0.52f, 0.55f, 0.5f, 1), 1.2f, 0.6f),   // r11: still mauve in game, cooler; r10: less red, the base read mauve
        GLayer("AV_G_Gravel", "Gravel", new Vector2(3.6f, 3.6f), new Color(0.5f, 0.5f, 0.5f, 1), 1.2f, 0.5f),   // r10: grey ash and gravel zones (reviewer: one dirt everywhere)
        GLayer("AV_G_Grass", "SparseGrass", new Vector2(1.8f, 1.8f), new Color(0.75f, 0.72f, 0.66f, 1), 1.0f, 0.4f),
        // 11-13 r9 (dark-fantasy refs: Tarae / POE2 / D4 floors are broken into stone, mud and litter zones, never one
        // dirt sheet): old cobbled paving along the streets and in broken islands, half sunk into the mud
        // 11-14 (Kido: "a Joseon village path is a dirt road, not marble"): the stone_pathway paving is replaced by a
        // free Fab Megascans scan, Dirt Ground xdhhefr (dry worn dirt road, 2x2 m scan; Standard License)
        GLayer("AV_G_DirtRoad", "DirtRoad", new Vector2(4.6f, 4.6f), new Color(0.4f, 0.37f, 0.33f, 1), 1.0f, 0.35f),
        GLayer("AV_G_Floor", "ForestFloor", new Vector2(8f, 8f), new Color(0.55f, 0.6f, 0.5f, 1), 1.0f, 0.4f),   // r10: dark moss / forest litter along the rim and in shade
    };

    static TerrainLayer[] GroundLayersR4() => new[]
    {
        // r2: the roots repeated as one embossed pattern everywhere -> wet leaf litter is the base, roots only in patches
        // r3 (Kido: "too uniform, too dense"): every layer ~2.5x bigger, so the pattern reads as big leaves and stones
        GLayer("AV_G_Leaves", "Leaves", new Vector2(3.2f, 3.2f), new Color(0.95f, 0.9f, 0.86f, 1), 0.8f, 0.6f),   // r4: 4.5 m smeared the leaves into flakes
        GLayer("AV_G_Roots", "Roots", new Vector2(12f, 6f), new Color(0.58f, 0.53f, 0.49f, 1), 0.8f, 0.45f, 0.6f),   // r3: darker, the pale dirt read as blotches
        GLayer("AV_G_Floor", "ForestFloor", new Vector2(8f, 8f), new Color(0.85f, 0.8f, 0.74f, 1), 1.0f, 0.4f),
        GLayer("AV_G_Gravel", "Gravel", new Vector2(3.6f, 3.6f), new Color(0.9f, 0.88f, 0.86f, 1), 1.2f, 0.6f),
    };

    static Material GroundMaterial()
    {
        var path = MatDir + "/AV_TerrainLit.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh;
        m.SetFloat("_EnableHeightBlend", 1f); m.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
        m.SetFloat("_HeightTransition", 0.2f);
        m.SetFloat("_EnableInstancedPerPixelNormal", 1f); m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
        m.EnableKeyword("_MASKMAP");
        EditorUtility.SetDirty(m);
        return m;
    }

    // r5 weights: trampled earth (base), dry soil patches, street tracks, sparse grass
    public static void SplatGround(float x, float z, float[] w)
    {
        float sd = StreetDist(x, z);
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z + 3.5f));
        float jit = (Mathf.PerlinNoise(x * 0.9f + 4f, z * 0.9f + 17f) - 0.5f) * 0.12f + (Mathf.PerlinNoise(x * 0.31f + 40f, z * 0.31f + 2f) - 0.5f) * 0.22f;
        // r6 weights: track mud (base), dry soil (stretched along x, soft, never full), sparse grass
        float dry = Mathf.Clamp01((Fbm(x * 0.45f, z, 0.05f, 17f, 91f) + jit * 0.5f - 0.5f) * 2.5f) * 0.55f * Mathf.Clamp01(sd / 4f);
        // r7 (Kido: "the earlier floor with a little grass texture in it was nicer"): sparse grass mixed into the dirt
        // almost everywhere at a low, varying share, denser toward the edges, worn off the streets
        float grass = Mathf.Max(Mathf.Clamp01(Mathf.Pow(GrassCover(x, z), 0.6f) * 1.3f), 0.5f * Mathf.Clamp01(0.15f + (Fbm(x, z, 0.06f, 71f, 13f) - 0.45f) * 1.6f + jit + Mathf.Clamp01((edge - 30f) / 16f) * 0.35f))   // 11-14: the turf under the grass patches
                      * Mathf.Clamp01((sd - 1.2f) / 2.5f) * (1f - dry * 0.6f) * 0.9f;
        // r9: paving - the street cores (ragged, with gaps where the stones are gone) plus broken islands of old yard
        // paving near the ruins and here and there in the open; grass grows over its edges
        // 11-14: the dirt road follows the street cores with a wide ragged, feathered shoulder (no stone gaps, no paved
        // islands); its strength wanders along the street so it fades into the trampled mud in places
        float road = Mathf.Clamp01((2.4f - sd + (Fbm(x, z, 0.22f, 3f, 77f) - 0.5f) * 2.6f + jit * 2f) / 1.8f);
        float pave = road * road * (3f - 2f * road) * Mathf.Lerp(0.55f, 1f, Fbm(x, z, 0.06f, 19f, 5f))
                     * (1f - Mathf.Clamp01((edge - 50f) / 6f));
        // r10 zones (reviewer, refs Tarae ash field / POE2): grey ash-gravel in ragged 2-5 m patches, dark moss toward the
        // rim and around the ruins, both breaking the packed earth
        float ash = Band(Fbm(x, z, 0.09f, 301f, 17f) + jit * 0.9f, 0.6f, 0.04f) * (1f - pave);
        float moss = Band(Fbm(x, z, 0.07f, 55f, 220f) + jit + Mathf.Clamp01((edge - 30f) / 14f) * 0.3f + 0.15f * IslandNear(x, z), 0.64f, 0.04f) * (1f - pave) * (1f - ash);
        w[0] = 1f; w[1] = ash * 1.6f; w[2] = grass * (1f - pave * 0.8f) * (1f - ash); w[3] = pave * 1.4f; w[4] = moss * 1.6f;
        float s = w[0] + w[1] + w[2] + w[3] + w[4]; for (int i = 0; i < 5; i++) w[i] /= s;
    }

    // weights: wet leaves (base), roots/packed dirt, dry forest floor, gravel
    public static void SplatGroundR4(float x, float z, float[] w)
    {
        float sd = StreetDist(x, z);
        float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z + 3.5f));
        // r3: ragged borders (no round blotches): two jitter octaves, the coarse one strong enough to break the outlines
        float jit = (Mathf.PerlinNoise(x * 0.9f + 4f, z * 0.9f + 17f) - 0.5f) * 0.12f + (Mathf.PerlinNoise(x * 0.31f + 40f, z * 0.31f + 2f) - 0.5f) * 0.22f;
        float street = Mathf.Clamp01(1.4f - sd / 2.2f + (Fbm(x, z, 0.14f, 8f, 3f) - 0.5f) * 1.5f);
        float grav = Mathf.Max(street * 0.9f, Band(Fbm(x, z, 0.11f, 5f, 47f) + jit, 0.68f, 0.035f) * 0.8f);
        float low = Mathf.Clamp01(-(TerrainHeight(x, z) + 0.05f) * 3f);                                     // hollows collect wet leaves
        float roots = Band(Fbm(x, z, 0.05f, 17f, 91f) + jit - Mathf.Clamp01((edge - 32f) / 16f) * 0.2f - low * 0.3f, 0.55f, 0.05f)
                      * (1f - street * 0.6f);
        float floor = Band(Fbm(x, z, 0.035f, 71f, 13f) + jit, 0.62f, 0.05f) * (1f - street) * (1f - roots * 0.7f);
        float other = Mathf.Max(roots, Mathf.Max(floor, grav));
        w[0] = 0.15f + 0.85f * (1f - other); w[1] = roots; w[2] = floor; w[3] = grav;
        float s = w[0] + w[1] + w[2] + w[3]; for (int i = 0; i < 4; i++) w[i] /= s;
    }

    // r3: macro variation over the tiling (see Shaders/AVMacro.shader): a 1 m grid hugging the terrain over the arena
    // and its surroundings, vertex colour = slow noise (0.5 neutral; darker damp patches, lighter dry ones, a faint
    // warm/cool drift), alpha fading out toward the grid border.
    static void BuildMacro(Transform parent)
    {
        const float half = 64f, step = 1f, cz = -3.5f;
        int n = Mathf.RoundToInt(half * 2 / step) + 1;
        var v = new Vector3[n * n]; var c = new Color[n * n]; var tri = new int[(n - 1) * (n - 1) * 6];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = -half + i * step, z = cz - half + j * step;
                v[j * n + i] = new Vector3(x, TerrainHeight(x, z) + 0.035f, z);
                float big = Fbm(x, z, 0.022f, 5f, 33f), mid = Fbm(x, z, 0.07f, 81f, 9f);
                float k = 0.5f + (big - 0.5f) * 0.55f + (mid - 0.5f) * 0.25f;            // ~0.3 .. 0.7
                float warm = (Mathf.PerlinNoise(x * 0.03f + 7f, z * 0.03f + 61f) - 0.5f) * 0.08f;
                float fade = Mathf.Clamp01((half - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z - cz))) / 6f);
                c[j * n + i] = new Color(k + warm, k, k - warm, fade);
            }
        int t = 0;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i;
                tri[t++] = a; tri[t++] = a + n; tri[t++] = a + 1; tri[t++] = a + 1; tri[t++] = a + n; tri[t++] = a + n + 1;
            }
        var mesh = new Mesh { name = "AV_Macro", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = v; mesh.colors = c; mesh.triangles = tri; mesh.RecalculateBounds();
        var path = Root + "/Generated/AV_Macro.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
        var mpath = MatDir + "/AV_Macro.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mpath);
        if (!m) { m = new Material(Shader.Find("AV/MacroMultiply")); AssetDatabase.CreateAsset(m, mpath); }
        m.SetFloat("_Strength", 1f);
        var go = new GameObject("GroundMacro");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
    }
}

