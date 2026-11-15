// Builds Assets/Game/DarkCrypt/DarkCrypt.unity: a dark corrupted crypt courtyard (ref: top-down ARPG courtyard with big
// stone floor tiles under purple corruption mist that clears where the player walks), much darker than the reference.
//   - flat 112 x 88 m arena of big Megascans castle floor stones (~3.5 m slabs, macro brightness variation, matte);
//   - DCMist: violet-black mist over the floor, wiped by the player in blotchy splotches, regrowing over ~25 s;
//   - EDGES ONLY: low Megascans castle walls (N/E/W) with stone pillars at the joints, the Poly Haven iron gate in the
//     north wall, crystal groups against the walls (dim violet / one cyan), candle groups on the wall tops by the
//     pillars (Megascans beeswax candles + Poly Haven candlestick, small emissive flames), two benches;
//   - behind the walls the floor stops and drops into darkness; tall pillar/wall stacks and big crystals beyond the
//     north edge for depth; near-black silhouettes (wall/pillar pieces) along the camera-side (south) edge;
//   - moon + 6 candle lights + 1 crystal light (8 realtime lights), violet-black fog, invisible edge walls;
//   - registers the RoomRun dev room "crypt" (next dev field key).
// v3 (11-15): semi-transparent noise-density mist with feathered clearing; lighter floor; Megascans debris quads; the
//   Sketchfab CC BY crystal scans (CREDITS.txt) bursting through the walls; walls/pillars ~1.5x; a 6.6 m gate with a
//   violet/candle backlight; lights: moon + 2 gate + 4 candle + 1 crystal = 8; no player light pool (FieldLighting).
// v4 (11-15): DarkCrypt/Floor (per-cell symmetry shuffle, macro tone, Megascans dirt in grout/patches, wall-base grime,
//   puddles); denser mist clumps with a lagging blotch trail (no disc at the feet, no cleared-stone lift); the south
//   foreground uses the wall/pillar scans in a near-black lit material plus crystals/candles; more flat debris and soft
//   grime stain decals in the open. Still 8 realtime lights.
// Nothing inside the arena has a collider except the floor. Re-running rebuilds the scene and materials.
// Run: Tools/DarkCrypt/Build Scene, or the RoomRun remote ("menu Tools/DarkCrypt/Build Scene" in ArtSource/rr_queue).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DarkCrypt
{
    public static class DCBuilder
    {
        public const string Root = "Assets/Game/DarkCrypt";
        public const string ScenePath = Root + "/DarkCrypt.unity";
        const string Src = Root + "/Source/";
        const string MatDir = Root + "/Materials";
        const string Gen = Root + "/Generated";
        public const string RoomId = "crypt";
        public const float HX = 56f, HZ = 44f;          // arena half size (112 x 88)
        const float WallLine = 0.6f;                    // walls stand this far outside the arena edge
        const float FloorTile = 7f;                     // one 2 m scan texture (2 x 2 slabs) stretched over 7 m -> ~3.5 m slabs

        static System.Random rng;
        static Transform root;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly List<string> log = new List<string>();
        static int lightCount;

        static string LogPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "DarkCrypt", "dc_log.txt");
        static void Log(string s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // No menu item (project rule). Run via rr_queue: "call DarkCrypt.DCBuilder.Build"
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ABORT build: play mode"); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty && active.path != ScenePath) { Log("ABORT unsaved changes in " + active.path); return; }
            try { BuildInner(); }
            catch (System.Exception e) { Log("BUILD FAILED " + e); throw; }
        }

        static void BuildInner()
        {
            rng = new System.Random(4413);
            mats.Clear(); lightCount = 0;
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();
            BuildMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("DarkCrypt").transform;

            BuildFloor();
            BuildMist();
            var candleSpots = BuildWalls();
            BuildGate();
            BuildCrystals();
            BuildCandles(candleSpots);
            BuildDebris();
            BuildBenches();
            BuildBackdrop();
            BuildSouthSilhouettes();
            BuildLightAndPost();
            BuildEdgeColliders();
            int blockers = CheckArena();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddRoomRunRoom();
            Log($"BUILT {ScenePath}: lights={lightCount} collidersInArena={blockers}");
        }

        // ------------------------------------------------------------------ textures / materials
        static Texture2D Tex(string path, bool normal = false, bool linear = false, int max = 2048)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Log("MISSING texture " + path); return null; }
            bool dirty = false;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != type) { imp.textureType = type; dirty = true; }
            if (!normal && imp.sRGBTexture == linear) { imp.sRGBTexture = !linear; dirty = true; }
            if (imp.maxTextureSize != max) { imp.maxTextureSize = max; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, string shader, System.Action<Material> init)
        {
            var path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (!sh) { Log("MISSING shader " + shader); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            init(m);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        // URP Lit, matte (11-13 no-gloss rule): albedo, normal (all sources here are OpenGL / green-up), AO
        static Material Lit(string name, string dir, string albedo, string normal, string ao, Color tint, float smooth = 0.1f, float metal = 0f, string metalMap = null)
            => Mat(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex(dir + albedo)); m.SetColor("_BaseColor", tint);
                var n = normal != null ? Tex(dir + normal, normal: true) : null;
                m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f);
                if (n) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                var o = ao != null ? Tex(dir + ao, linear: true) : null;
                m.SetTexture("_OcclusionMap", o); m.SetFloat("_OcclusionStrength", 1f);
                if (o) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
                var mm = metalMap != null ? Tex(dir + metalMap, linear: true) : null;
                m.SetTexture("_MetallicGlossMap", mm);
                if (mm) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_SpecularHighlights", 1f); m.SetFloat("_EnvironmentReflections", 1f);
                m.enableInstancing = true;
            });

        static Material Emissive(string name, Color baseCol, Color emission, float smooth)
            => Mat(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", baseCol);
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                m.SetColor("_EmissionColor", emission); m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP");
                m.enableInstancing = true;
            });

        // v3: the scan's colour map is only the crystal/rock mask (see DCCrystal.shader); glow = violet, glow2 = for red-ish texels
        static Material Crystal(string name, Texture2D scan, Color body, Color glow, Color glow2, Color rim, float glowBase, float height)
            => Mat(name, "DarkCrypt/Crystal", m =>
            {
                m.SetTexture("_BaseMap", scan); m.SetFloat("_UseTex", scan ? 1f : 0f);
                m.SetColor("_Body", body); m.SetColor("_Rock", new Color(0.035f, 0.03f, 0.042f));
                m.SetColor("_Glow", glow); m.SetColor("_Glow2", glow2); m.SetColor("_Rim", rim);
                m.SetFloat("_GlowBase", glowBase); m.SetFloat("_Height", height); m.SetFloat("_Pulse", 0.12f);
                m.enableInstancing = true;
            });

        // Megascans debris decal: colour + opacity packed into one RGBA texture (alpha-clipped flat quads, opaque queue)
        static Texture2D DebrisRGBA() => PackRGBA(Src + "Fab_Debris/Debris_phhh0kp2_2K_BaseColor.jpg", Src + "Fab_Debris/Debris_phhh0kp2_2K_Opacity.jpg", Gen + "/DC_DebrisRGBA.png", false);

        // colour (optionally as grey) + opacity map packed into one RGBA png under Generated
        static Texture2D PackRGBA(string colPath, string opPath, string path, bool grey)
        {
            string full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
            {
                Texture2D Readable(string p)
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);   // LoadImage turns it RGB24 for a jpg
                    t.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, p)));
                    return t;
                }
                if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, colPath))) { Log("MISSING " + colPath); return null; }
                var col = Readable(colPath);
                var op = Readable(opPath);
                if (col.width != op.width || col.height != op.height) { Log("pack: size mismatch " + path); return null; }
                var c = col.GetPixels32(); var o = op.GetPixels32();
                for (int i = 0; i < c.Length; i++)
                {
                    if (grey) { byte g = (byte)Mathf.Clamp(c[i].r * 0.3f + c[i].g * 0.59f + c[i].b * 0.11f + 60, 0, 255); c[i].r = c[i].g = c[i].b = g; }
                    c[i].a = o[i].r;
                }
                var rgba = new Texture2D(col.width, col.height, TextureFormat.RGBA32, false);
                rgba.SetPixels32(c); rgba.Apply();
                File.WriteAllBytes(full, rgba.EncodeToPNG());
                Object.DestroyImmediate(col); Object.DestroyImmediate(op); Object.DestroyImmediate(rgba);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp && (!imp.alphaIsTransparency || imp.maxTextureSize != 1024 || imp.wrapMode != TextureWrapMode.Clamp)) { imp.alphaIsTransparency = true; imp.maxTextureSize = 1024; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Unlit(string name, Color c)
            => Mat(name, "Universal Render Pipeline/Unlit", m => { m.SetColor("_BaseColor", c); m.enableInstancing = true; });

        static void BuildMaterials()
        {
            const string fl = Src + "Fab_castle_floor_stone_tdblbcyt_2k/";
            // v4: DarkCrypt/Floor - per-cell symmetry shuffle of the scan (seams in the grout), per-cell tone, two macro
            // noises, the Megascans dirt scan in grout/cracks and patches, wall-base grime, a few dark puddles
            const string dg = Src + "Fab_dirt_ground_xdhhefr_2k/";
            Mat("DC_Floor", "DarkCrypt/Floor", m =>
            {
                m.shaderKeywords = new string[0];
                m.SetTexture("_BaseMap", Tex(fl + "Castle_Floor_Stone_tdblbcyt_2K_BaseColor.jpg"));
                m.SetTexture("_BumpMap", Tex(fl + "Castle_Floor_Stone_tdblbcyt_2K_Normal.jpg", normal: true));
                m.SetTexture("_AOMap", Tex(fl + "Castle_Floor_Stone_tdblbcyt_2K_AO.jpg", linear: true));
                m.SetTexture("_DirtMap", Tex(dg + "Dirt_Ground_xdhhefr_2K_BaseColor.jpg"));
                m.SetTexture("_DirtNormal", Tex(dg + "Dirt_Ground_xdhhefr_2K_Normal.jpg", normal: true));
                m.SetTexture("_Macro", MacroNoise());
                m.SetColor("_Tint", Hex("#dcd8e4")); m.SetColor("_DirtTint", Hex("#a59ca2"));
                m.SetFloat("_Tile", FloorTile); m.SetFloat("_DirtTile", 4.3f);
                m.SetFloat("_CellTone", 0.2f); m.SetFloat("_MacroAmt", 0.45f);
                m.SetFloat("_DirtAmt", 0.45f); m.SetFloat("_GroutDirt", 0.7f);
                m.SetVector("_Edge", new Vector4(HX, HZ, 4.5f, 0.75f));
                m.SetFloat("_PuddleAmt", 0.5f); m.SetFloat("_NrmStr", 1f); m.SetFloat("_AOStr", 0.85f); m.SetFloat("_Smooth", 0.08f);
            });
            // v4 grime stains: the Megascans stain decal (SnowPlague's blood stain scan) as a grey mask, soft-blended near-black
            Mat("DC_Stain", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", PackRGBA("Assets/Game/SnowPlague/ThirdParty/Megascans/blood_stain_sg3bdgoc_2k/Blood_Stain_sg3bdgoc_2K_BaseColor.jpg",
                    "Assets/Game/SnowPlague/ThirdParty/Megascans/blood_stain_sg3bdgoc_2k/Blood_Stain_sg3bdgoc_2K_Opacity.jpg", Gen + "/DC_StainRGBA.png", true));
                m.SetColor("_BaseColor", new Color(0.09f, 0.075f, 0.085f, 0.8f));
                m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP");
                m.SetFloat("_Smoothness", 0.05f); m.SetFloat("_Metallic", 0f);
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_BlendModePreserveSpecular", 0f); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetFloat("_ReceiveShadows", 1f);
                m.renderQueue = (int)RenderQueue.Transparent - 80;   // before the mist (Transparent-50)
                m.enableInstancing = true;
            });
            Lit("DC_Wall", Src + "Fab_castle_wall_sbxuw_mid/", "Castle_Wall_sbxuw_Mid_2K_BaseColor.jpg", "Castle_Wall_sbxuw_Mid_2K_Normal.jpg", "Castle_Wall_sbxuw_Mid_2K_AO.jpg", Hex("#8c8792"), 0.08f);
            Lit("DC_Pillar", Src + "Fab_stone_pillar_selvf_mid/", "Stone_Pillar_selvF_Mid_2K_BaseColor.jpg", "Stone_Pillar_selvF_Mid_2K_Normal.jpg", "Stone_Pillar_selvF_Mid_2K_AO.jpg", Hex("#8a8690"), 0.08f);
            const string cd = Src + "Fab_historic_medieval_farm_candle_beeswax_burnt_02_xmnnffi_mid/";
            Lit("DC_Candle", cd, "Historic_Medieval_Farm_Candle_Beeswax_Burnt_02_xmnnffi_Mid_2K_BaseColor.jpg", "Historic_Medieval_Farm_Candle_Beeswax_Burnt_02_xmnnffi_Mid_2K_Normal.jpg", "Historic_Medieval_Farm_Candle_Beeswax_Burnt_02_xmnnffi_Mid_2K_AO.jpg", Hex("#c8b89a"), 0.15f);
            Lit("DC_Gate", Src + "PH_large_iron_gate/", "large_iron_gate_diff_2k.jpg", "large_iron_gate_nor_gl_2k.png", null, Hex("#8e8896"), 0.25f, 0.3f);
            Lit("DC_Bench", Src + "PH_painted_wooden_bench/", "painted_wooden_bench_diff_2k.jpg", "painted_wooden_bench_nor_gl_2k.png", null, Hex("#5d5a60"), 0.08f);
            Lit("DC_Candlestick", Src + "PH_wooden_candlestick/", "wooden_candlestick_diff_2k.jpg", "wooden_candlestick_nor_gl_2k.png", null, Hex("#8a8088"), 0.12f);
            // crystals: two Sketchfab CC BY scans (see CREDITS.txt), each with its own colour map as the mask
            var cc2Tex = Tex(CC2Dir + "Image_0.001.png", max: 2048);
            var formTex = Tex(FormDir + "Zyklus5_Zeile10_basecolor.jpeg", max: 2048);
            var body = new Color(0.028f, 0.012f, 0.05f);
            var violet = new Color(0.5f, 0.12f, 1.0f); var pinkViolet = new Color(0.75f, 0.14f, 0.9f);
            var blueViolet = new Color(0.22f, 0.2f, 1.1f); var magenta = new Color(0.95f, 0.12f, 0.6f);
            var rim = new Color(0.55f, 0.32f, 1.1f);
            Crystal("DC_CrystalC2Violet", cc2Tex, body, violet, pinkViolet, rim, 0.06f, 4.8f);
            Crystal("DC_CrystalC2Magenta", cc2Tex, body, blueViolet, magenta, new Color(0.5f, 0.35f, 1.2f), 0.06f, 4.8f);
            Crystal("DC_CrystalFormViolet", formTex, body, violet, pinkViolet, rim, 0.05f, 4.8f);
            Crystal("DC_CrystalFar", formTex, new Color(0.02f, 0.01f, 0.035f), new Color(0.2f, 0.05f, 0.42f), new Color(0.3f, 0.05f, 0.3f), new Color(0.25f, 0.12f, 0.45f), 0.12f, 6f);
            // floor debris (flat, alpha-clipped; dark grey-violet dust/rubble)
            Mat("DC_Debris", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", DebrisRGBA()); m.SetColor("_BaseColor", Hex("#5a5058"));
                var dn = Tex(Src + "Fab_Debris/Debris_phhh0kp2_2K_Normal.jpg", normal: true, max: 1024);
                m.SetTexture("_BumpMap", dn); m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.45f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Smoothness", 0.05f); m.SetFloat("_Metallic", 0f);
                m.renderQueue = (int)RenderQueue.AlphaTest;
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.enableInstancing = true;
            });
            Mat("DC_GateGlow", "DarkCrypt/GlowCard", m => { m.SetColor("_Color", new Color(0.3f, 0.09f, 0.55f)); m.SetFloat("_Low", 0.22f); m.SetFloat("_Falloff", 1.5f); });
            Mat("DC_GateWarm", "DarkCrypt/GlowCard", m => { m.SetColor("_Color", new Color(0.45f, 0.2f, 0.07f)); m.SetFloat("_Low", 0.08f); m.SetFloat("_Falloff", 2.2f); });
            Unlit("DC_Flame", new Color(5.5f, 2.1f, 0.55f));
            Unlit("DC_Silhouette", new Color(0.004f, 0.003f, 0.006f));
            // v4 camera-side foreground: the real wall/pillar scans, near-black but still lit (normals and edges read)
            Lit("DC_SouthWall", Src + "Fab_castle_wall_sbxuw_mid/", "Castle_Wall_sbxuw_Mid_2K_BaseColor.jpg", "Castle_Wall_sbxuw_Mid_2K_Normal.jpg", "Castle_Wall_sbxuw_Mid_2K_AO.jpg", Hex("#2a2630"), 0.05f);
            Lit("DC_SouthPillar", Src + "Fab_stone_pillar_selvf_mid/", "Stone_Pillar_selvF_Mid_2K_BaseColor.jpg", "Stone_Pillar_selvF_Mid_2K_Normal.jpg", "Stone_Pillar_selvF_Mid_2K_AO.jpg", Hex("#2a2630"), 0.05f);
            // backdrop stones: the wall/pillar scans, dimmer (they sit in fog and darkness)
            var bd = Object.Instantiate(mats["DC_Pillar"]); bd.SetColor("_BaseColor", Hex("#4a4650"));
            SaveCopy(bd, "DC_Backdrop");
            // v3 mist: set every value (an existing material keeps the old saved ones otherwise)
            Mat("DC_Mist", "DarkCrypt/MistOverlay", m =>
            {
                // v4: denser violet clumps (~25 % thin .. ~88 % dense), cleared stone gets almost no lift (no spotlight)
                m.SetColor("_Deep", new Color(0.1f, 0.026f, 0.19f)); m.SetColor("_Mid", new Color(0.19f, 0.05f, 0.34f));
                m.SetColor("_Glow", new Color(0.13f, 0.035f, 0.22f)); m.SetColor("_Rim", new Color(0f, 0f, 0f));
                m.SetFloat("_MinDensity", 0.4f); m.SetFloat("_MaxDensity", 0.9f); m.SetFloat("_Scale", 0.08f); m.SetFloat("_Drift", 0.5f);
                m.SetFloat("_EdgeScale", 0.16f); m.SetFloat("_Feather", 0.85f); m.SetColor("_ClearLift", new Color(0.004f, 0.0035f, 0.005f));
                m.SetFloat("_BorderFade", 3f);
                m.SetVector("_NoiseOffset", new Vector4(-22f, -3f, 0, 0));   // v4: puts a dense clump over the spawn so the walked trail reads
            });
            Mat("DC_MistBrush", "Hidden/DarkCrypt/MistBrush", m => { });
        }

        static void SaveCopy(Material m, string name)
        {
            var path = $"{MatDir}/{name}.mat";
            var old = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (old) { old.CopyPropertiesFromMaterial(m); old.shaderKeywords = m.shaderKeywords; EditorUtility.SetDirty(old); mats[name] = old; Object.DestroyImmediate(m); }
            else { m.name = name; AssetDatabase.CreateAsset(m, path); mats[name] = m; }
        }

        static Texture2D MacroNoise()
        {
            const int S = 256;
            var path = Gen + "/DC_MacroNoise.png";
            var t = new Texture2D(S, S, TextureFormat.RGB24, false);
            var px = new Color[S * S];
            float Tile(float x, float y, float f) // tileable value noise via 4-corner blend
            {
                float a = Mathf.PerlinNoise(x * f, y * f), b = Mathf.PerlinNoise((x - S) * f, y * f);
                float c = Mathf.PerlinNoise(x * f, (y - S) * f), d = Mathf.PerlinNoise((x - S) * f, (y - S) * f);
                float u = x / S, v = y / S;
                return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
            }
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = Tile(x, y, 0.02f) * 0.6f + Tile(x + 50, y + 90, 0.06f) * 0.3f + Tile(x + 7, y + 3, 0.15f) * 0.1f;
                float g = Mathf.Lerp(0.4f, 0.6f, Mathf.SmoothStep(0.2f, 0.8f, n));   // x2 detail: 0.8 .. 1.2 (v3: less deep dips)
                px[y * S + x] = new Color(g, g * 0.99f, g * 1.01f);
            }
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp && imp.sRGBTexture) { imp.sRGBTexture = false; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ placing models
        static Transform Group(string name, Transform parent = null)
        {
            var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g;
        }

        static readonly Dictionary<string, (GameObject go, Bounds b)> models = new Dictionary<string, (GameObject, Bounds)>();

        // the model with its renderers' local bounds (identity transform), to sit it by its bottom centre
        static (GameObject go, Bounds b) Model(string path)
        {
            if (models.TryGetValue(path, out var cached) && cached.go) return cached;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) { Log("MISSING model " + path); return (null, default); }
            var tmp = Object.Instantiate(go);
            tmp.transform.position = Vector3.zero;   // keeps the root's own rotation/scale (Blender FBX roots carry -90 X and x100)
            var rs = tmp.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds();
            foreach (var r in rs) b.Encapsulate(r.bounds);
            long tris = 0;
            foreach (var mf in tmp.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
            Object.DestroyImmediate(tmp);
            Log($"model {Path.GetFileName(path)} size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} centre {b.center} tris {tris}");
            models[path] = (go, b);
            return (go, b);
        }

        // Places the model so its bottom centre sits at pos; height in metres (uniform scale), yaw in degrees.
        static Transform Place(string path, Material mat, Vector3 pos, float yaw, float height, Transform parent, bool shadows = true, bool lengthAlongX = false)
        {
            var (go, b) = Model(path);
            if (!go) return null;
            float s = height / Mathf.Max(0.01f, b.size.y);
            var holder = new GameObject(go.name).transform;
            holder.SetParent(parent, false);
            holder.position = pos; holder.rotation = Quaternion.Euler(0, yaw, 0); holder.localScale = Vector3.one * s;
            var inst = Object.Instantiate(go, holder);
            inst.name = go.name;
            // a long piece (wall) is turned so its long side runs along the holder's X
            var align = lengthAlongX && b.size.z > b.size.x ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
            inst.transform.localRotation = align * go.transform.localRotation; inst.transform.localScale = go.transform.localScale;
            inst.transform.localPosition = align * -new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                if (mat) { var arr = new Material[r.sharedMaterials.Length]; for (int i = 0; i < arr.Length; i++) arr[i] = mat; r.sharedMaterials = arr; }
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
            }
            return holder;
        }

        static float SizeY(string path) => Model(path).b.size.y;
        static float SizeLong(string path) { var b = Model(path).b; return Mathf.Max(b.size.x, b.size.z); }

        // ------------------------------------------------------------------ floor
        static void BuildFloor()
        {
            // one flat slab: arena + 7 m beyond the walls on N/E/W (then nothing: the drop into darkness), 16 m south
            const float x0 = -HX - 7.5f, x1 = HX + 7.5f, z0 = -HZ - 16f, z1 = HZ + 7.5f;
            var mesh = new Mesh { name = "DC_FloorMesh" };
            const int nx = 32, nz = 26;   // a few rows so per-vertex fog and lighting stay smooth
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)nx), z = Mathf.Lerp(z0, z1, j / (float)nz);
                v.Add(new Vector3(x, 0, z)); uv.Add(new Vector2(x, z) / FloorTile);
            }
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i;
                tri.AddRange(new[] { a, a + nx + 1, a + 1, a + 1, a + nx + 1, a + nx + 2 });
            }
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var mp = Gen + "/DC_FloorMesh.asset";
            AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var g = new GameObject("Ground");
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["DC_Floor"];
            mr.shadowCastingMode = ShadowCastingMode.Off;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        }

        static void BuildMist()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "CorruptionMist";
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.position = new Vector3(0, 0.035f, 0);
            g.transform.rotation = Quaternion.Euler(90, 0, 0);
            g.transform.localScale = new Vector3(120f, 96f, 1f);
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = mats["DC_Mist"];
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            var mist = g.AddComponent<DCMist>();
            mist.rect = new Vector4(-60f, -48f, 120f, 96f);
            mist.brush = mats["DC_MistBrush"];
            mist.radius = 2.6f;   // v4: mean blotch radius of the trail stamps (DCMist drops them along the path walked)
            mist.stampSeconds = 0.8f; mist.spacing = 0.6f;
        }

        // ------------------------------------------------------------------ walls and pillars (N, E, W edges)
        const string WallFbx = Src + "Fab_castle_wall_sbxuw_mid/Castle_Wall_sbxuw_Mid.fbx";
        const string PillarFbx = Src + "Fab_stone_pillar_selvf_mid/Stone_Pillar_selvF_Mid.fbx";
        const string CandleFbx = Src + "Fab_historic_medieval_farm_candle_beeswax_burnt_02_xmnnffi_mid/Historic_Medieval_Farm_Candle_Beeswax_Burnt_02_xmnnffi_Mid.fbx";
        const string GateFbx = Src + "PH_large_iron_gate/large_iron_gate_2k.fbx";
        const string BenchFbx = Src + "PH_painted_wooden_bench/painted_wooden_bench_2k.fbx";
        const string StickFbx = Src + "PH_wooden_candlestick/wooden_candlestick_2k.fbx";
        const string CC2Dir = Src + "SF_CrystalCluster2/", FormDir = Src + "SF_VioletCrystalFormation/";
        const string CC2Obj = CC2Dir + "CrystalCluster2.obj", FormObj = FormDir + "violet_crystal_formation.obj";
        // v3: everything at the edge ~1.5x bigger so it shows in the 1.6x game camera when the player is near the edge
        const float GateGap = 6.6f;
        const float WallH = 1.15f, PillarH = 4.0f;
        // crystal groups break through the walls here (x, z, magenta-blue?); the wall run has a gap at each
        static readonly (Vector2 p, bool magenta)[] CrystalSpots =
        {
            (new Vector2(-31f, HZ + 1.4f), false), (new Vector2(24f, HZ + 1.4f), true),
            (new Vector2(HX + 1.4f, 17f), true), (new Vector2(HX + 1.4f, -24f), false),
            (new Vector2(-HX - 1.4f, 26f), false), (new Vector2(-HX - 1.4f, -15f), false),
        };
        static bool NearCrystal(Vector3 p, float r) => CrystalSpots.Any(c => (new Vector2(p.x, p.z) - c.p).sqrMagnitude < r * r);

        // returns spots on the wall tops next to pillars (with the inward direction) for the candle groups
        static List<(Vector3 p, Vector3 inward)> BuildWalls()
        {
            var grp = Group("Walls");
            var spots = new List<(Vector3, Vector3)>();
            Model(WallFbx);
            float segLen = SizeLong(WallFbx) * (WallH / SizeY(WallFbx));   // segment length at the wall height used
            // edges: start, end, outward normal
            var edges = new[]
            {
                (a: new Vector3(-HX - WallLine, 0, HZ + WallLine), b: new Vector3(HX + WallLine, 0, HZ + WallLine), outN: Vector3.forward, name: "North"),
                (a: new Vector3(HX + WallLine, 0, HZ + WallLine), b: new Vector3(HX + WallLine, 0, -HZ - 2f), outN: Vector3.right, name: "East"),
                (a: new Vector3(-HX - WallLine, 0, -HZ - 2f), b: new Vector3(-HX - WallLine, 0, HZ + WallLine), outN: Vector3.left, name: "West"),
            };
            int pillarEvery = 4;
            foreach (var e in edges)
            {
                var g = Group(e.name, grp);
                Vector3 dir = (e.b - e.a).normalized;
                float len = (e.b - e.a).magnitude;
                int n = Mathf.CeilToInt(len / segLen);
                float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;   // holder X (the wall's long side) along the edge
                for (int i = 0; i < n; i++)
                {
                    float t = (i + 0.5f) * segLen;
                    var p = e.a + dir * t;
                    bool gate = e.name == "North" && Mathf.Abs(p.x) < GateGap * 0.5f + segLen * 0.3f;
                    bool broken = NearCrystal(p, 2.6f);   // the crystals have burst through here: only a stub course
                    if (broken && !gate)
                    {
                        Place(WallFbx, mats["DC_Wall"], p, yaw + R(-6f, 6f), WallH * R(0.4f, 0.55f), g, true, true);
                    }
                    else if (!gate)
                    {
                        // lower course everywhere, an upper course on ~60 % (broken, uneven top like the reference)
                        Place(WallFbx, mats["DC_Wall"], p + e.outN * R(-0.05f, 0.05f), yaw + R(-1.2f, 1.2f) + (rng.NextDouble() < 0.5 ? 180f : 0f), WallH * R(0.95f, 1.05f), g, true, true);
                        if (rng.NextDouble() < 0.6)
                            Place(WallFbx, mats["DC_Wall"], p + Vector3.up * WallH * 0.97f + dir * R(-0.25f, 0.25f), yaw + R(-2f, 2f) + (rng.NextDouble() < 0.5 ? 180f : 0f), WallH * R(0.85f, 1.0f), g, true, true);
                    }
                    if (i % pillarEvery == 0 || i == n - 1)
                    {
                        var pp = e.a + dir * (i * segLen);
                        if (e.name == "North" && Mathf.Abs(pp.x) < GateGap * 0.5f) continue;
                        Place(PillarFbx, mats["DC_Pillar"], pp, R(0, 360), PillarH * R(0.92f, 1.08f), g);
                        if (i % (pillarEvery * 2) == 0) spots.Add((pp + dir * R(0.9f, 1.4f) + Vector3.up * WallH * 1.9f, -e.outN));
                    }
                }
            }
            Log($"walls: segment {segLen:0.00} m, candle spots {spots.Count}");
            return spots;
        }

        static void BuildGate()
        {
            var g = Group("Gate");
            var z = HZ + WallLine;
            // two tall gate pillars and the iron gate between them
            Place(PillarFbx, mats["DC_Pillar"], new Vector3(-GateGap * 0.5f - 0.5f, 0, z), 0, PillarH * 1.75f, g);
            Place(PillarFbx, mats["DC_Pillar"], new Vector3(GateGap * 0.5f + 0.5f, 0, z), 90, PillarH * 1.75f, g);
            var (go, b) = Model(GateFbx);
            float gateH = 5f;
            if (go)
            {
                float wide = Mathf.Max(b.size.x, b.size.z);
                gateH = b.size.y * ((GateGap - 0.3f) / wide);
                Place(GateFbx, mats["DC_Gate"], new Vector3(0, 0, z + 0.2f), 0, gateH, g, true, true);
            }
            // v3 backlight (no black blob): a violet glow card and a low warm one behind the bars, a candle cluster on the
            // floor beyond the gate and its light; the bars read as dark lines against it
            Card("GateGlowViolet", mats["DC_GateGlow"], new Vector3(0, gateH * 0.55f, z + 2.4f), GateGap * 1.9f, gateH * 1.5f, g);
            Card("GateGlowWarm", mats["DC_GateWarm"], new Vector3(0, gateH * 0.3f, z + 1.6f), GateGap * 1.2f, gateH * 0.7f, g);
            var cg = Group("GateCandles", g);
            for (int i = 0; i < 14; i++)
            {
                var q = new Vector3(R(-2.6f, 2.6f), 0, z + R(1.0f, 2.2f));
                float h = R(0.25f, 0.8f);
                if (Place(CandleFbx, mats["DC_Candle"], q, R(0, 360), h, cg, false)) Flame(q + Vector3.up * (h + 0.07f), h > 0.5f ? 1.4f : 1f, cg);
            }
            AddLight("GateBacklight", new Vector3(0, 1.4f, z + 1.6f), new Color(1f, 0.55f, 0.3f), 3.2f, 9f, g);
            AddLight("GateVioletLight", new Vector3(0, 3.5f, z + 3.5f), new Color(0.6f, 0.3f, 1f), 2.6f, 12f, g);
        }

        // a vertical quad facing the camera side (-Z), for the additive glow cards
        static void Card(string name, Material m, Vector3 centre, float w, float h, Transform parent)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(parent, false);
            q.transform.position = centre; q.transform.rotation = Quaternion.identity; q.transform.localScale = new Vector3(w, h, 1);
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        static void Flame(Vector3 at, float size, Transform parent)
        {
            var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(f.GetComponent<Collider>());
            f.name = "Flame"; f.transform.SetParent(parent, false);
            f.transform.position = at; f.transform.localScale = new Vector3(0.07f, 0.14f, 0.07f) * size;
            var fr = f.GetComponent<MeshRenderer>(); fr.sharedMaterial = mats["DC_Flame"]; fr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ crystals
        static readonly string[] Clusters = { "crystal_cluster_a", "crystal_cluster_b", "crystal_cluster_c", "crystal_cluster_d", "crystal_cluster_spiky" };
        static readonly string[] Shards = { "crystal_shard_a", "crystal_shard_b" };
        static string Crys(string n) => Src + "Crystals/" + n + ".obj";

        static void BuildCrystals()
        {
            var g = Group("Crystals");
            // v3: groups of 3-4 Sketchfab crystal scans bursting through the wall line, the big one just outside the edge,
            // smaller ones beside it (all outside the arena; no colliders). Mostly violet, two magenta-blue groups.
            Light glow = null;
            int k = 0;
            foreach (var gr in CrystalSpots)
            {
                var gg = Group("CrystalGroup", g);
                var c = new Vector3(gr.p.x, 0, gr.p.y);
                var outN = Mathf.Abs(c.x) > HX ? new Vector3(Mathf.Sign(c.x), 0, 0) : Vector3.forward;
                var side = Vector3.Cross(Vector3.up, outN);
                var cc2 = mats[gr.magenta ? "DC_CrystalC2Magenta" : "DC_CrystalC2Violet"];
                var form = mats["DC_CrystalFormViolet"];
                bool bigIsForm = (k++ % 2) == 0;
                Place(bigIsForm ? FormObj : CC2Obj, bigIsForm ? form : cc2, c + outN * 0.6f, R(0, 360), R(4.2f, 5.2f), gg);
                for (int i = 0; i < 3; i++)
                {
                    float s = (i == 0 ? -1 : 1) * R(1.8f, 3.0f) + (i == 2 ? R(0.5f, 1.2f) : 0f);
                    var p = c + side * s + outN * R(0.2f, 1.6f);
                    bool f2 = rng.NextDouble() < 0.4;
                    var t = Place(f2 ? FormObj : CC2Obj, f2 ? form : cc2, p, R(0, 360), R(1.6f, 3.0f), gg);
                    if (t) t.rotation *= Quaternion.Euler(R(-12, 12), 0, R(-12, 12));
                }
                if (gr.magenta && !glow)
                    glow = AddLight("CrystalGlow", c + Vector3.up * 2.5f - outN * 1.8f, new Color(0.62f, 0.35f, 1f), 2.4f, 10f, g);
            }
        }

        // ------------------------------------------------------------------ candles
        static void BuildCandles(List<(Vector3 p, Vector3 inward)> spots)
        {
            var g = Group("Candles");
            float candleH = SizeY(CandleFbx);
            // six groups, spread over the three walls (the gate pillars get one each side)
            var pick = new List<(Vector3 p, Vector3 inward)>();
            // v3: the two groups flanking the gate have no light of their own (the gate backlight covers them)
            pick.Add((new Vector3(-GateGap * 0.5f - 2.4f, WallH * 1.9f, HZ + WallLine), Vector3.back));
            pick.Add((new Vector3(GateGap * 0.5f + 2.4f, WallH * 1.9f, HZ + WallLine), Vector3.back));
            var rest = spots.Where(s => !NearCrystal(s.p, 4f) && Mathf.Abs(s.p.x) > GateGap + 3f).OrderBy(s => Mathf.Atan2(s.p.z, s.p.x)).ToList();
            for (int i = 0; i < rest.Count && pick.Count < 6; i += Mathf.Max(1, rest.Count / 4)) pick.Add(rest[i]);
            int gi = 0;
            foreach (var (p, inward) in pick)
            {
                bool lit = gi++ >= 2;
                var gg = Group("CandleGroup", g);
                // on the wall top (the upper course may be missing: ray down to the stone)
                var top = p + Vector3.up * 3f;
                int n = rng.Next(7, 12);
                for (int i = 0; i < n; i++)
                {
                    var q = top + new Vector3(R(-0.75f, 0.75f), 0, R(-0.6f, 0.6f));
                    float h = R(0.25f, 0.6f);
                    float y = SurfaceY(q, p.y);
                    var t = Place(CandleFbx, mats["DC_Candle"], new Vector3(q.x, y, q.z), R(0, 360), h, gg, false);
                    if (!t) continue;
                    // a small flame on the wick
                    var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(f.GetComponent<Collider>());
                    f.name = "Flame"; f.transform.SetParent(gg, false);
                    f.transform.position = new Vector3(q.x, y + h + 0.06f, q.z);
                    f.transform.localScale = new Vector3(0.1f, 0.2f, 0.1f);
                    var fr = f.GetComponent<MeshRenderer>(); fr.sharedMaterial = mats["DC_Flame"]; fr.shadowCastingMode = ShadowCastingMode.Off;
                }
                // one tall candle per group (the Poly Haven candlestick is 213k triangles: left out)
                var sq = top + new Vector3(R(-0.3f, 0.3f), 0, R(-0.3f, 0.3f));
                float sy = SurfaceY(sq, p.y);
                float th = R(0.9f, 1.1f);
                if (Place(CandleFbx, mats["DC_Candle"], new Vector3(sq.x, sy, sq.z), R(0, 360), th, gg, false))
                {
                    var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.DestroyImmediate(f.GetComponent<Collider>());
                    f.name = "Flame"; f.transform.SetParent(gg, false);
                    f.transform.position = new Vector3(sq.x, sy + th + 0.08f, sq.z);
                    f.transform.localScale = new Vector3(0.14f, 0.28f, 0.14f);
                    var fr = f.GetComponent<MeshRenderer>(); fr.sharedMaterial = mats["DC_Flame"]; fr.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (lit) AddLight("CandleLight", new Vector3(p.x, sy + 0.7f, p.z) + inward * 0.8f, new Color(1f, 0.58f, 0.28f), 2.6f, 7.5f, gg);
            }
        }

        // top of whatever stone is under q (walls have mesh renderers but no colliders: test renderer bounds)
        static float SurfaceY(Vector3 q, float fallback)
        {
            float best = -1f;
            foreach (var r in root.Find("Walls").GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (q.x < b.min.x || q.x > b.max.x || q.z < b.min.z || q.z > b.max.z) continue;
                if (b.max.y > 3.5f) continue;   // not the pillars
                best = Mathf.Max(best, b.max.y - 0.03f);
            }
            return best > 0 ? best : fallback;
        }

        // ------------------------------------------------------------------ floor debris (Megascans decal, flat)
        // v3: alpha-clipped flat quads a few millimetres over the stones (under the mist), sparse: drifts of dust and grit
        // along the wall bases, a few in the open. No colliders, no shadows. (The project has no URP decal feature;
        // flat cut-out quads cost one instanced draw and look the same from this camera.)
        static void BuildDebris()
        {
            var g = Group("FloorDebris");
            var m = mats["DC_Debris"];
            int n = 0;
            void Piece(Vector3 p, float size)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Debris"; q.transform.SetParent(g, false);
                q.transform.position = new Vector3(p.x, 0.006f + (n++ % 8) * 0.002f, p.z);
                q.transform.rotation = Quaternion.Euler(90, R(0, 360), 0);
                q.transform.localScale = new Vector3(size * R(0.85f, 1.15f), size, 1);
                var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(q, StaticEditorFlags.BatchingStatic);
            }
            // wall bases (N, E, W): a drift every ~9-14 m, hugging the edge
            for (float x = -HX + 3f; x < HX - 3f; x += R(9f, 14f)) if (Mathf.Abs(x) > GateGap) Piece(new Vector3(x, 0, HZ - R(0.6f, 1.6f)), R(2.6f, 4.2f));
            for (float z = -HZ + 4f; z < HZ - 3f; z += R(9f, 14f)) { Piece(new Vector3(HX - R(0.6f, 1.6f), 0, z), R(2.6f, 4.2f)); Piece(new Vector3(-HX + R(0.6f, 1.6f), 0, z + R(-3f, 3f)), R(2.6f, 4.2f)); }
            // gate threshold and the crystal feet (inside the edge)
            Piece(new Vector3(R(-1.5f, 1.5f), 0, HZ - 1.8f), 4.5f);
            foreach (var c in CrystalSpots) Piece(new Vector3(Mathf.Clamp(c.p.x, -HX + 1.4f, HX - 1.4f), 0, Mathf.Clamp(c.p.y, -HZ + 1.4f, HZ - 1.4f)), R(3f, 4f));
            // v4: the open interior gets its own scatter (jittered grid, ~1 per 110 m2, some in small clusters of 2-3)
            var open = new List<Vector2>();
            for (float x = -HX + 9f; x < HX - 8f; x += 11f)
            for (float z = -HZ + 8f; z < HZ - 8f; z += 10f)
            {
                if (rng.NextDouble() < 0.3) continue;
                var p = new Vector2(x + R(-4f, 4f), z + R(-4f, 4f));
                open.Add(p);
                Piece(new Vector3(p.x, 0, p.y), R(1.6f, 3.4f));
                if (rng.NextDouble() < 0.35) Piece(new Vector3(p.x + R(-2.5f, 2.5f), 0, p.y + R(-2.5f, 2.5f)), R(1.0f, 2.0f));
            }
            Log($"debris decals {n}");

            // v4 grime stains: soft near-black blotches (blended, before the mist), larger ones at wall feet, smaller in the open
            var sg = Group("FloorStains");
            var sm = mats["DC_Stain"];
            int ns = 0;
            void Stain(Vector3 p, float size)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Stain"; q.transform.SetParent(sg, false);
                q.transform.position = new Vector3(p.x, 0.004f + (ns++ % 6) * 0.0015f, p.z);
                q.transform.rotation = Quaternion.Euler(90, R(0, 360), 0);
                q.transform.localScale = new Vector3(size * R(0.7f, 1.3f), size, 1);
                var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = sm; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            for (float x = -HX + 4f; x < HX - 4f; x += R(12f, 20f)) if (Mathf.Abs(x) > GateGap) Stain(new Vector3(x, 0, HZ - R(1f, 2.5f)), R(3.5f, 6f));
            for (float z = -HZ + 6f; z < HZ - 4f; z += R(12f, 20f)) { Stain(new Vector3(HX - R(1f, 2.5f), 0, z), R(3.5f, 6f)); Stain(new Vector3(-HX + R(1f, 2.5f), 0, z + R(-4f, 4f)), R(3.5f, 6f)); }
            for (int i = 0; i < 16; i++) Stain(new Vector3(R(-HX + 8f, HX - 8f), 0, R(-HZ + 8f, HZ - 8f)), R(2.2f, 4.5f));
            Log($"stain decals {ns}");
        }

        // ------------------------------------------------------------------ benches (broken, at the edges)
        static void BuildBenches()
        {
            var g = Group("Benches");
            var a = Place(BenchFbx, mats["DC_Bench"], new Vector3(-HX + 0.4f, 0, -6f), 90 + R(-8, 8), 0.5f, g, true, true);
            var b = Place(BenchFbx, mats["DC_Bench"], new Vector3(HX - 0.3f, 0.25f, 31f), -90, 0.5f, g, true, true);
            if (b) b.rotation *= Quaternion.Euler(0, 0, 78f);   // knocked over on its side against the east wall
            var c = Place(BenchFbx, mats["DC_Bench"], new Vector3(14f, 0, HZ - 0.3f), 180 + R(-10, 10), 0.5f, g, true, true);
            if (c) c.rotation *= Quaternion.Euler(R(4, 8), 0, 0);   // sagging, a leg gone
        }

        // ------------------------------------------------------------------ depth beyond the north edge
        static void BuildBackdrop()
        {
            var g = Group("Backdrop");
            var bd = mats["DC_Backdrop"];
            // tall pillar / wall stacks rising out of the dark past the north and side walls (no floor there)
            for (float x = -70f; x <= 70f; x += R(9f, 14f))
            {
                float z = HZ + R(13f, 24f);
                float h = R(7f, 12f);
                Place(PillarFbx, bd, new Vector3(x, -3f, z), R(0, 360), h + 3f, g);
                if (rng.NextDouble() < 0.5)
                {
                    int courses = rng.Next(3, 7);
                    float yaw = R(-6, 6);
                    for (int k = 0; k < courses; k++)
                        Place(WallFbx, bd, new Vector3(x + R(2.5f, 4.5f), -2f + k * 1.45f, z + R(-0.2f, 0.2f)), yaw + R(-3, 3), 1.5f, g, true, true);
                }
            }
            foreach (var sx in new[] { -1f, 1f })
                for (float z = -20f; z <= 50f; z += R(14f, 20f))
                    Place(PillarFbx, bd, new Vector3(sx * (HX + R(12f, 18f)), -3f, z), R(0, 360), R(8f, 12f), g);
            // big dim crystal masses out in the dark
            var far = mats["DC_CrystalFar"];
            foreach (var p in new[] { new Vector3(-44f, -1.5f, HZ + 12f), new Vector3(38f, -1.5f, HZ + 15f), new Vector3(HX + 11f, -1.5f, -8f), new Vector3(-HX - 12f, -1.5f, 6f) })
                Place(FormObj, far, p, R(0, 360), R(6f, 8f), g, false);
        }

        // ------------------------------------------------------------------ camera-side silhouettes
        static void BuildSouthSilhouettes()
        {
            // v4: the real Megascans wall/pillar scans in a near-black lit material (not flat black boxes), with crystal and
            // candle pieces. Camera fov 30 at (0, 17, -14.5) * 1.6 from the player: the bottom screen edge meets the ground
            // ~10 m south of the player, so with the player near the south edge the low wall runs (z -48..-52) sit in the
            // lower screen and the tall pillars (z -53..-58, 6-11 m) cross the bottom edge. They never reach the line of sight
            // to the player's feet.
            var g = Group("SouthSilhouettes");
            var sw = mats["DC_SouthWall"]; var sp = mats["DC_SouthPillar"]; var far = mats["DC_CrystalFar"];
            int pieces = 0;
            for (float x = -66f; x <= 66f; x += R(7f, 11f))
            {
                // broken wall run: 1-3 segments, a second course on some, uneven heights, slightly askew
                float z = -HZ - R(4.2f, 7.5f);
                float yaw = R(-10, 10);
                int segs = rng.Next(1, 4);
                for (int k = 0; k < segs; k++)
                {
                    var p = new Vector3(x + k * 3.6f + R(-0.4f, 0.4f), 0, z + R(-0.4f, 0.4f));
                    float h = R(1.1f, 1.8f);
                    if (Place(WallFbx, sw, p, yaw + R(-4, 4), h, g, false, true)) pieces++;
                    if (rng.NextDouble() < 0.5 && Place(WallFbx, sw, p + Vector3.up * h * 0.95f + Vector3.right * R(-0.6f, 0.6f), yaw + R(-6, 6), h * R(0.6f, 0.95f), g, false, true)) pieces++;
                }
                // a tall pillar (some leaning or snapped short) further toward the camera
                if (rng.NextDouble() < 0.6)
                {
                    var t = Place(PillarFbx, sp, new Vector3(x + R(-3f, 3f), 0, -HZ - R(6.5f, 10f)), R(0, 360), R(5f, 9.5f), g, false);
                    if (t) { pieces++; if (rng.NextDouble() < 0.35) t.rotation *= Quaternion.Euler(R(-9, 9), 0, R(-9, 9)); }
                }
                // a dim crystal clump at the wall foot now and then
                if (rng.NextDouble() < 0.3 && Place(FormObj, far, new Vector3(x + R(-2f, 2f), 0, z - R(0.8f, 2f)), R(0, 360), R(1.8f, 3.4f), g, false)) pieces++;
                // a few burnt-down candles on a wall top (unlit: no light, tiny dim flames)
                if (rng.NextDouble() < 0.25)
                {
                    var top = new Vector3(x + R(-0.8f, 0.8f), 0, z);
                    float ty = 0f;
                    foreach (var r in g.GetComponentsInChildren<Renderer>())
                    {
                        var b = r.bounds;
                        if (top.x > b.min.x && top.x < b.max.x && top.z > b.min.z && top.z < b.max.z && b.max.y < 4f) ty = Mathf.Max(ty, b.max.y - 0.03f);
                    }
                    if (ty > 0.5f)
                        for (int c = 0; c < rng.Next(2, 5); c++)
                        {
                            var q = top + new Vector3(R(-0.5f, 0.5f), ty, R(-0.25f, 0.25f));
                            float h = R(0.15f, 0.45f);
                            if (Place(CandleFbx, mats["DC_Candle"], q, R(0, 360), h, g, false) && rng.NextDouble() < 0.5) Flame(q + Vector3.up * (h + 0.05f), 0.9f, g);
                        }
                }
            }
            Log($"south foreground pieces {pieces}");
        }

        // ------------------------------------------------------------------ light, fog, post, camera
        static Light AddLight(string name, Vector3 pos, Color c, float intensity, float range, Transform parent)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent, false); l.transform.position = pos;
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            lightCount++;
            return l;
        }

        static void BuildLightAndPost()
        {
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional; moon.color = Hex("#8c88a2"); moon.intensity = 0.9f;
            moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.8f; moon.lightmapBakeType = LightmapBakeType.Realtime;
            moon.transform.rotation = Quaternion.Euler(48f, 160f, 0f);   // from the north-west, high: short shadows toward the camera
            lightCount++;
            RenderSettings.sun = moon;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#1a1426");
            RenderSettings.ambientEquatorColor = Hex("#120e1a");
            RenderSettings.ambientGroundColor = Hex("#0a0810");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#0b0712");
            RenderSettings.fogDensity = 0.016f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.3f;
            LightingSettings ls = null;
            try { ls = Lightmapping.lightingSettings; } catch { }
            if (ls == null)
            {
                var lsPath = Root + "/DC_Lighting.lighting";
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
                if (!ls) { ls = new LightingSettings { name = "DC_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = false; ls.realtimeGI = false;

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
            vol.sharedProfile = Profile();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#06040a");
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cam.gameObject.AddComponent<AudioListener>();
            var off = new Vector3(0, 17f, -14.5f) * 1.6f;
            cam.transform.position = off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }

        static VolumeProfile Profile()
        {
            var path = Root + "/DC_PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0f); ca.contrast.Override(7f); ca.saturation.Override(-40f); ca.colorFilter.Override(Hex("#E9E4F0"));
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.4f); bl.intensity.Override(0.6f); bl.scatter.Override(0.65f); bl.highQualityFiltering.Override(false);
            var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#040208")); vg.intensity.Override(0.42f); vg.smoothness.Override(0.45f);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        // ------------------------------------------------------------------ invisible walls and the arena check
        static void BuildEdgeColliders()
        {
            var walls = Group("ArenaWalls");
            void Box(Vector3 c, Vector3 s) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.transform.position = c; w.AddComponent<BoxCollider>().size = s; }
            Box(new Vector3(-HX - 0.5f, 2, 0), new Vector3(1, 6, 2 * HZ + 2));
            Box(new Vector3(HX + 0.5f, 2, 0), new Vector3(1, 6, 2 * HZ + 2));
            Box(new Vector3(0, 2, -HZ - 0.5f), new Vector3(2 * HX + 2, 6, 1));
            Box(new Vector3(0, 2, HZ + 0.5f), new Vector3(2 * HX + 2, 6, 1));
        }

        static int CheckArena()
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (c.name == "Ground" || c.transform.parent.name == "ArenaWalls") continue;
                var b = c.bounds;
                if (b.max.x <= -HX || b.min.x >= HX || b.max.z <= -HZ || b.min.z >= HZ) continue;
                n++; Log($"COLLIDER IN ARENA {c.name} {b.center}");
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Ground" || r.name == "CorruptionMist" || r.name == "Debris" || r.name == "Stain") continue;
                var p = r.bounds.center;
                if (Mathf.Abs(p.x) < HX - 1.5f && Mathf.Abs(p.z) < HZ - 1.5f) Log($"NOTE renderer inside the arena: {r.transform.parent?.name}/{r.name} at {p}");
            }
            return n;
        }

        // ------------------------------------------------------------------ RoomRun dev room (next dev field key)
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
            r.FindPropertyRelative("displayName").stringValue = "타락한 묘역";
            r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(8, 0);
            r.FindPropertyRelative("scenePath").stringValue = ScenePath;
            r.FindPropertyRelative("groundMeshName").stringValue = "";
            r.FindPropertyRelative("center").vector3Value = Vector3.zero;
            r.FindPropertyRelative("size").vector2Value = size;
            r.FindPropertyRelative("fitToStage").boolValue = false;
            r.FindPropertyRelative("floorColor").colorValue = Hex("#1a1622");
            r.FindPropertyRelative("pillarCount").intValue = 0;
            r.FindPropertyRelative("doors").arraySize = 0;
            r.FindPropertyRelative("waves").arraySize = 0;
            r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
            r.FindPropertyRelative("devOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Log("room 'crypt' added to RoomRunConfig");
        }
    }
}
