// Builds Assets/Game/D4Field/D4Field.unity, v2 (11-15 18:19 Kido): a small hand-composed field toward the TARAE
// reference shot (/mnt/project-files/kido_shots/tarae_ref_field_1115.png): wet dark mud with puddles, grey gravel,
// grey rock outcrops, bare twisted trees in mist, a thatched house as the near foreground, overcast grey light.
// v1 (the Diablo IV talk test, noise-scattered 112 x 88 m) read as "어설프다": this one is a 60 x 44 m showcase where
// every landmark, puddle and patch is placed by hand (lists below), and only the small debris inside a patch is random.
//   - Ground: Unity Terrain, 4 layers from scans already in the project (Megascans via Fab):
//       0 dry soil (AV_FG_DrySoil), 1 wet mud (moist chunky mud, smoothness from its gloss map),
//       2 gravel (AV_FG_Gravel), 3 dry forest floor under the trees (AV_FG_ForestFloor). Height blend on.
//     Puddles are real dips in the heightmap with one water plane just under the floor, so the shoreline is the
//     terrain itself (nothing drawn). An overcast HDRI (Poly Haven kloppenheim_02, CC0) is the reflection source:
//     without a real reflection the water reads as black holes (폐촌 lesson).
//   - Landmarks: rock ridge N/NE (Megascans granite cliff + boulders), bare trees NW/W, thatched house SW (camera
//     side, bottom-left of the screen), boulders E. Nothing inside the arena has a collider or is taller than ~0.6 m.
//   - Light: one soft overcast key + 1 warm lantern at the house. Mist particles at the trees and the ridge.
// Run (no menu item, project rule): drop "call D4Field.D4FieldBuilder.Build" into ArtSource/rr_queue/<name>.txt.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace D4Field
{
    public static class D4FieldBuilder
    {
        public const string Root = "Assets/Game/D4Field";
        public const string ScenePath = Root + "/D4Field.unity";
        const string Src = Root + "/Source/";
        const string MS = Root + "/Source/MS/";   // Megascans from Fab (free, Fab Standard License): Rocky Ground vl0hccalw, Wet Soil sjzjejua, Icelandic Rocky Ground uczmfghew, Icelandic Rock Formation siCs3 / siExJ
        const string MatDir = Root + "/Materials";
        const string Gen = Root + "/Generated";
        const string AV = "Assets/Game/AbandonedVillage/";
        const string AVMS = AV + "ThirdParty/Megascans/";
        const string SPMS = "Assets/Game/SnowPlague/ThirdParty/Megascans/";
        const string LSMS = "Assets/Art/LookStudy/Stages/ThirdParty/Megascans/";
        const string EF = "Assets/Game/RoomRun/Resources/EdgeFeatures/";
        const string DownloadHdri = @"C:\Users\imsoh\Downloads\free-assets\polyhaven\hdri\kloppenheim_02_2k.hdr";
        public const string RoomId = "d4field";
        public const float HX = 30f, HZ = 22f, NZ = 17f, RS = 6f;   // NZ: north walk limit (layout), RS: ridge and trees pulled south into the frame          // arena half size (60 x 44)
        const float TW = 100f, TD = 92f;
        // the layout below is written in a 60 x 44 space and squeezed into the world, so the ridge, the trees and the house all sit inside one game-camera frame (like the reference)
        public const float SX = 0.6f, SZ = 0.4f, WX = HX * SX, WZ = HZ * SZ;                // terrain size
        const float TBase = -6f, THeight = 16f;
        const float WaterY = -0.065f;

        static System.Random rng;
        static Transform root;
        static Terrain terrain;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly List<GameObject> templates = new List<GameObject>();
        static int lightCount;

        static string ProjectDir => Directory.GetParent(Application.dataPath).FullName;
        static string LogPath => Path.Combine(ProjectDir, "ArtSource", "D4Field", "d4_log.txt");
        static void Log(string s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

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
            Log("---- v2 (TARAE direction) build");
            rng = new System.Random(77);
            mats.Clear(); templates.Clear(); lightCount = 0;
            Directory.CreateDirectory(Path.Combine(ProjectDir, MatDir));
            Directory.CreateDirectory(Path.Combine(ProjectDir, Gen));
            Directory.CreateDirectory(Path.Combine(ProjectDir, Src, "HDRI"));
            var hdriDst = Path.Combine(ProjectDir, Src, "HDRI", "kloppenheim_02_2k.hdr");
            if (!File.Exists(hdriDst)) { if (File.Exists(DownloadHdri)) { File.Copy(DownloadHdri, hdriDst); Log("copied hdri"); } else Log("MISSING " + DownloadHdri); }
            AssetDatabase.Refresh();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("D4Field").transform;

            BuildTerrain();
            BuildMaterials();
            LoadModels();
            BuildWater();
            BuildRidge();
            BuildTrees();
            BuildHouse();
            BuildEastAndSouth();
            BuildGroundDressing();
            BuildMist();
            foreach (var t in templates) if (t) Object.DestroyImmediate(t);
            BuildLightAndPost();
            BuildEdgeColliders();
            int blockers = CheckArena();

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddRoomRunRoom();
            Log($"BUILT {ScenePath}: lights={lightCount} collidersInArena={blockers}");
        }

        // ------------------------------------------------------------------ textures
        static Texture2D Tex(string path, bool normal = false, bool linear = false, int max = 2048)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Log("MISSING texture " + path); return null; }
            if (path.StartsWith(Root))   // our own downloads: set the import; shared project textures are left as they are
            {
                var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !linear;
                if (imp.textureType != want || imp.sRGBTexture != srgb || imp.maxTextureSize != max)
                { imp.textureType = want; imp.sRGBTexture = srgb; imp.maxTextureSize = max; imp.SaveAndReimport(); }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D ReadFile(string assetPath)
        {
            var full = Path.Combine(ProjectDir, assetPath);
            if (!File.Exists(full)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.LoadImage(File.ReadAllBytes(full));
            return t;
        }

        // URP terrain mask map: R metallic, G AO, B height, A smoothness (from the Megascans gloss map). Half res.
        static Texture2D PackMudMask()
        {
            string dir = SPMS + "moist_chunky_mud_sb0oaalp_2k/Moist_Chunky_Mud_sb0oaalp_2K_";
            string outPath = $"{Gen}/TF_Mask_Mud.png";
            var ao = ReadFile(dir + "AO.jpg"); var disp = ReadFile(dir + "Displacement.jpg"); var gloss = ReadFile(dir + "Gloss.jpg");
            if (!ao || !disp || !gloss) { Log("pack mud mask: missing maps"); return null; }
            int w = ao.width / 2, h = ao.height / 2;
            var a = ao.GetPixels32(); var d = disp.GetPixels32(); var g = gloss.GetPixels32();
            var o = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int si = (y * 2) * ao.width + x * 2;
                    o[y * w + x] = new Color32(0, a[si].r, d[Mathf.Min(si, d.Length - 1)].r, g[Mathf.Min(si, g.Length - 1)].r);
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(o); t.Apply();
            File.WriteAllBytes(Path.Combine(ProjectDir, outPath), t.EncodeToPNG());
            Object.DestroyImmediate(ao); Object.DestroyImmediate(disp); Object.DestroyImmediate(gloss); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(outPath) as TextureImporter;
            if (imp && (imp.sRGBTexture || imp.maxTextureSize != 1024)) { imp.sRGBTexture = false; imp.maxTextureSize = 1024; imp.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        // ------------------------------------------------------------------ hand-placed layout
        static float Fbm(float x, float z, float scale, int oct = 4)
        {
            float s = 0, amp = 0.5f, f = 1f / scale, norm = 0;
            for (int i = 0; i < oct; i++) { s += amp * Mathf.PerlinNoise(x * f + 31.7f * i + 100f, z * f + 17.3f * i + 100f); norm += amp; amp *= 0.5f; f *= 2.03f; }
            return s / norm;
        }
        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Outside(float x, float z) { float dx = Mathf.Abs(x) - HX, dz = Mathf.Max(z - NZ, -z - HZ); return Mathf.Max(dx, dz); }
        static float DistPoly(Vector2[] pts, Vector2 p)
        {
            float best = 1e9f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        // puddles: x, z, radius x, radius z, yaw (deg). The mud basin runs from the ridge foot (N) down to the house (SW).
        static readonly float[][] Puddles =
        {
            new[] { -3f, 8f, 3.4f, 2.5f, 20f }, new[] { -6f, -3.5f, 3.0f, 1.9f, -15f }, new[] { -8.8f, 3.2f, 1.4f, 1.2f, -50f },
        };
        // the wet spine of the basin and the thin runnel that leaves it to the south (camera side)
        static readonly Vector2[] Basin = { new Vector2(1, 19), new Vector2(-2, 11), new Vector2(-5, 3), new Vector2(-6, -4), new Vector2(-10, -10), new Vector2(-16, -15) };
        static readonly Vector2[] Runnel = { new Vector2(-4, -3), new Vector2(-1.5f, -9), new Vector2(-2.5f, -15), new Vector2(0.5f, -21), new Vector2(0f, -30) };

        // 0..1, 1 at the puddle centre, 0 outside; the edge wobbles so no puddle is an ellipse
        static float PuddleAtL(float x, float z)
        {
            float best = 0f;
            foreach (var p in Puddles)
            {
                float c = Mathf.Cos(p[4] * Mathf.Deg2Rad), s = Mathf.Sin(p[4] * Mathf.Deg2Rad);
                float dx = x - p[0], dz = z - p[1];
                float u = (dx * c + dz * s) / p[2], v = (-dx * s + dz * c) / p[3];
                float d = Mathf.Sqrt(u * u + v * v) + (Fbm(x, z, 1.7f, 3) - 0.5f) * 0.9f;
                best = Mathf.Max(best, 1f - Smooth(0.55f, 1.05f, d));
            }
            return best;
        }
        static float MudAtL(float x, float z)
        {
            var p = new Vector2(x, z);
            float wob = (Fbm(x + 40, z - 20, 5f) - 0.5f) * 7f;
            float basin = 1f - Smooth(3.5f, 8.5f, DistPoly(Basin, p) + wob);
            float run = 0f;   // no runnel: Kido found the water trailing south odd
            float pud = 0f;
            foreach (var q in Puddles) pud = Mathf.Max(pud, 1f - Smooth(1.2f, 2.3f, new Vector2((x - q[0]) / q[2], (z - q[1]) / q[3]).magnitude + wob * 0.06f));
            return Mathf.Clamp01(Mathf.Max(basin, Mathf.Max(run, pud)));
        }
        // gravel: the whole east side and the ridge foot, breaking up toward the basin
        static float GravelAtL(float x, float z)
        {
            float wob = (Fbm(x - 70, z + 15, 7f) - 0.5f) * 12f;
            float east = Smooth(2f, 11f, x + wob + (z > 0 ? z * 0.25f : 0f));
            float ridgeFoot = Smooth(7f, 13f, z + wob * 0.5f) * Smooth(-8f, 0f, x);
            float patch = Smooth(0.60f, 0.72f, Fbm(x + 11, z + 53, 9f)) * 0.8f;
            return Mathf.Clamp01(Mathf.Max(east, Mathf.Max(ridgeFoot, patch)));
        }
        // dry litter under the trees: north-west and west
        static float LitterAtL(float x, float z)
        {
            float wob = (Fbm(x + 5, z + 95, 6f) - 0.5f) * 10f;
            float nw = Smooth(8f, 0f, new Vector2((x + 26f) / 1.6f, z - 18f).magnitude - 12f + wob);
            float w = Smooth(-20f, -29f, x + wob * 0.6f) * Smooth(-16f, -6f, z);
            return Mathf.Clamp01(Mathf.Max(nw, w));
        }

        static float HeightAtL(float x, float z)
        {
            float o = Outside(x, z);
            float h = (Fbm(x, z, 6f, 3) - 0.5f) * 0.14f;
            float mud = MudAtL(x, z);
            h -= mud * 0.035f;                                            // the basin sits a touch lower
            h -= PuddleAtL(x, z) * 0.13f;
            // the dry ground stands above the wet basin: a real bank at every mud border, and slow swells the low light can rake across
            // (Kido 11-15 21:23: no step that looks pressed in by a plane) -> the rise is a long slope away from the basin, not a terrace on the mud border
            h += Smooth(5f, 24f, DistPoly(Basin, new Vector2(x, z)) + (Fbm(x - 13, z + 61, 9f) - 0.5f) * 8f) * (0.12f + 0.36f * Fbm(x + 77, z - 31, 12f, 2));
            if (o > -2f)
            {
                float d = Mathf.Max(0f, o + 1f);
                if (z < -HZ + 1f && Mathf.Abs(x) < HX + 4f) h -= Smooth(0f, 9f, d) * (1.6f + 1.6f * Fbm(x, z, 14f));           // camera side drops
                else if (z > NZ - 1f) h += (Smooth(0f, 14f, d) * (0.5f + 1.3f * Fbm(x + 300, z, 12f)) * Smooth(-24f, -6f, x)    // rock ridge N / NE
                                         + Smooth(0f, 12f, d) * 0.7f) * (1f - 0.9f * Smooth(7f, 2f, Mathf.Abs(x + 4f)))        // a gully runs north between the trees and the rocks
                                         - Smooth(7f, 1f, Mathf.Abs(x + 4f)) * Smooth(1f, 9f, d) * 0.25f;
                else if (x > HX - 1f) h += Smooth(0f, 10f, d) * (1.0f + 2.2f * Fbm(x, z + 200, 12f));
                else h += Smooth(0f, 12f, d) * (0.5f + 1.2f * Fbm(x - 200, z, 12f));
            }
            return h;
        }

        // ------------------------------------------------------------------ terrain
                static float PuddleAt(float x, float z) => PuddleAtL(x / SX, z / SZ);
        static float MudAt(float x, float z) => MudAtL(x / SX, z / SZ);
        static float GravelAt(float x, float z) => GravelAtL(x / SX, z / SZ);
        static float LitterAt(float x, float z) => LitterAtL(x / SX, z / SZ);
        static float HeightAt(float x, float z) => HeightAtL(x / SX, z / SZ);

        static TerrainLayer Layer(string name, string albedo, string normal, Texture2D mask, float tile, Color tint, float smoothMax, float normalScale)
        {
            var path = $"{Gen}/TF_{name}.terrainlayer";
            var tl = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!tl) { tl = new TerrainLayer(); AssetDatabase.CreateAsset(tl, path); }
            tl.diffuseTexture = Tex(albedo);
            tl.normalMapTexture = Tex(normal, normal: true);
            tl.normalScale = normalScale;
            tl.maskMapTexture = mask;
            tl.tileSize = new Vector2(tile, tile);
            tl.tileOffset = new Vector2(tile * 0.37f, tile * 0.61f);
            tl.diffuseRemapMin = Vector4.zero;
            tl.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            float met = name == "Mud" ? 0.3f : 0f;
            tl.maskMapRemapMin = new Vector4(met, 0.2f, 0f, name == "Mud" ? 0.52f : 0f);
            tl.maskMapRemapMax = new Vector4(met, 1f, 1f, smoothMax);
            tl.smoothness = 0f; tl.metallic = 0f;
            EditorUtility.SetDirty(tl);
            return tl;
        }

        static void BuildTerrain()
        {
            string fg = AVMS + "Ground/AV_FG_";
            // the scans are warm brown; the reference is grey: cool tints, and the grade takes the rest of the saturation out
            var layers = new[]
            {
                Layer("Soil", MS + "TF_IceRocky_A.jpg", MS + "TF_IceRocky_N.jpg", Tex(MS + "TF_IceRocky_M.png", linear: true), 5f, new Color(0.58f, 0.61f, 0.66f), 0.3f, 1f),
                Layer("Mud", MS + "TF_WetSoil_A.jpg", MS + "TF_WetSoil_N.jpg", Tex(MS + "TF_WetSoil_M.png", linear: true), 5f, new Color(0.78f, 0.92f, 1.22f), 0.95f, 0.9f),
                Layer("Gravel", MS + "TF_RockyGround_A.jpg", MS + "TF_RockyGround_N.jpg", Tex(MS + "TF_RockyGround_M.png", linear: true), 4f, new Color(0.36f, 0.36f, 0.37f), 0.45f, 1.1f),
                Layer("Litter", fg + "ForestFloor_A.jpg", fg + "ForestFloor_N.jpg", Tex(fg + "ForestFloor_M.png"), 4f, new Color(0.62f, 0.68f, 0.74f), 0.15f, 0.9f),
            };

            var tdPath = $"{Gen}/D4_TerrainData.asset";
            AssetDatabase.DeleteAsset(tdPath);
            var td = new TerrainData();
            AssetDatabase.CreateAsset(td, tdPath);            // asset first: alphamaps set on an unsaved TerrainData were lost on save
            td.heightmapResolution = 1025;
            td.alphamapResolution = 1024;
            td.baseMapResolution = 1024;
            td.size = new Vector3(TW, THeight, TD);
            int hr = td.heightmapResolution;
            var hts = new float[hr, hr];
            for (int z = 0; z < hr; z++)
                for (int x = 0; x < hr; x++)
                {
                    float wx = -TW / 2 + x * TW / (hr - 1), wz = -TD / 2 + z * TD / (hr - 1);
                    hts[z, x] = Mathf.Clamp01((HeightAt(wx, wz) - TBase) / THeight);
                }
            td.SetHeights(0, 0, hts);
            td.terrainLayers = layers;

            int ar = td.alphamapResolution;
            var al = new float[ar, ar, 4];
            for (int z = 0; z < ar; z++)
                for (int x = 0; x < ar; x++)
                {
                    float wx = -TW / 2 + (x + 0.5f) * TW / ar, wz = -TD / 2 + (z + 0.5f) * TD / ar;
                    float mud = MudAt(wx, wz), gravel = GravelAt(wx, wz) * (1f - mud), litter = LitterAt(wx, wz) * (1f - mud);
                    float fine = 0.75f + 0.5f * Fbm(wx + 9, wz - 9, 1.3f, 2);              // metre-scale breakup of every border
                    float w0 = 0.45f, w1 = mud * 3.2f * fine, w2 = gravel * 2.2f * fine, w3 = litter * 1.8f * fine;
                    float sum = w0 + w1 + w2 + w3;
                    al[z, x, 0] = w0 / sum; al[z, x, 1] = w1 / sum; al[z, x, 2] = w2 / sum; al[z, x, 3] = w3 / sum;
                }
            td.SetAlphamaps(0, 0, al);
            td.SetBaseMapDirty();
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var tm = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")) { name = "D4_TerrainLit" };
            tm.SetFloat("_EnableHeightBlend", 1f); tm.EnableKeyword("_TERRAIN_BLEND_HEIGHT");
            tm.SetFloat("_HeightTransition", 0.13f);
            tm.EnableKeyword("_MASKMAP"); tm.EnableKeyword("_NORMALMAP");
            tm.SetFloat("_EnableInstancedPerPixelNormal", 1f); tm.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            var tmPath = $"{MatDir}/D4_TerrainLit.mat";
            AssetDatabase.DeleteAsset(tmPath);
            AssetDatabase.CreateAsset(tm, tmPath);

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Ground";
            tgo.transform.SetParent(root, false);
            tgo.transform.position = new Vector3(-TW / 2, TBase, -TD / 2);
            terrain = tgo.GetComponent<Terrain>();
            terrain.materialTemplate = tm;
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 2f;
            terrain.basemapDistance = 400f;
            terrain.shadowCastingMode = ShadowCastingMode.Off;
        }

        static float GroundY(float x, float z) => terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;

        // ------------------------------------------------------------------ materials
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

        static Material Lit(string name, string albedo, string normal, Color tint, float bump = 1f, string ao = null, float smooth = 0.06f)
            => Mat(name, "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", albedo != null ? Tex(albedo) : null); m.SetColor("_BaseColor", tint);
                var n = normal != null ? Tex(normal, normal: true) : null;
                m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", bump);
                if (n) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                var o = ao != null ? Tex(ao, linear: true) : null;
                m.SetTexture("_OcclusionMap", o);
                if (o) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                m.enableInstancing = true;
            });

        static Material Shared(string key, string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) Log("MISSING material " + path); else mats[key] = m;
            return m;
        }

        static void BuildMaterials()
        {
            Color rock = Hex("#b9bdc3");
            string c = LSMS + "granite_cliff_rivav_mid/Granite_Cliff_rivav_Mid_2K_";
            Lit("TF_Cliff", MS + "TF_RockA_A.jpg", MS + "TF_RockA_N.jpg", new Color(1.35f, 1.37f, 1.42f), 1f, MS + "TF_RockA_AO.jpg");
            Lit("TF_DeadTree", MS + "TF_DeadTree_A.jpg", MS + "TF_DeadTree_N.jpg", new Color(1.5f, 1.45f, 1.4f), 1f, MS + "TF_DeadTree_AO.jpg");
            Lit("TF_Boulder", MS + "TF_RockB_A.jpg", MS + "TF_RockB_N.jpg", new Color(1.1f, 1.12f, 1.16f), 1f, MS + "TF_RockB_AO.jpg");
            string n = LSMS + "nordic_beach_rock_uknmedo_mid/Nordic_Beach_Rock_uknmedo_Mid_2K_";
            Lit("TF_BeachRock", n + "BaseColor.jpg", n + "Normal.jpg", Hex("#a9adb3"), 1f, n + "AO.jpg");
            // puddle water: dark, mirror-smooth; what it shows is the overcast sky reflection
            Mat("TF_Water", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", Hex("#a39c92"));
                m.SetFloat("_Smoothness", 0.93f); m.SetFloat("_Metallic", 1f);
                m.SetFloat("_WorkflowMode", 1f); m.DisableKeyword("_SPECULAR_SETUP");
                m.SetFloat("_EnvironmentReflections", 1f); m.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                m.SetFloat("_SpecularHighlights", 1f); m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            });
            Mat("TF_Silhouette", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", Hex("#0d0e10"));
                m.SetFloat("_Smoothness", 0f); m.SetFloat("_Metallic", 0f); m.SetFloat("_EnvironmentReflections", 0f);
            });
            Mat("TF_Flame", "Universal Render Pipeline/Unlit", m => { m.SetColor("_BaseColor", Hex("#ffb35c") * 2.4f); });
            // scans that 폐촌 already imported and made materials for (same assets, same materials)
            foreach (var k in new[] { "Planks", "Sticks", "Pebbles", "SmallRocks", "RubblePatch", "Debris", "WildDry", "DryGrass", "Clumps", "shrubs" })
                Shared("MS_" + k, AV + "Materials/AV_MS_" + k + ".mat");
            Shared("Fog", AV + "Materials/AV_FogMat.mat");
            if (mats.TryGetValue("Fog", out var fsm) && fsm)
            {
                var fp = $"{MatDir}/TF_Fog.mat";
                AssetDatabase.DeleteAsset(fp);
                var f2 = new Material(fsm);
                var fc = new Color(0.78f, 0.79f, 0.80f, 1f);
                foreach (var pn in new[] { "_BaseColor", "_Color", "_TintColor", "_Tint" })
                    if (f2.HasProperty(pn)) { var oc = f2.GetColor(pn); f2.SetColor(pn, new Color(fc.r, fc.g, fc.b, oc.a)); }
                AssetDatabase.CreateAsset(f2, fp);
                mats["Fog"] = f2;
            }
            foreach (var k in new[] { "WildDry", "DryGrass", "Clumps", "shrubs" })
            {
                if (!mats.TryGetValue("MS_" + k, out var sm) || !sm) continue;
                var cp = $"{MatDir}/TF_{k}.mat";
                AssetDatabase.DeleteAsset(cp);
                var m2 = new Material(sm);
                if (m2.HasProperty("_BaseColor")) m2.SetColor("_BaseColor", new Color(0.42f, 0.41f, 0.40f, 1f));
                if (m2.HasProperty("_Color")) m2.SetColor("_Color", new Color(0.42f, 0.41f, 0.40f, 1f));
                AssetDatabase.CreateAsset(m2, cp);
                mats["MS_" + k] = m2;
            }
        }

        // ------------------------------------------------------------------ models
        static Transform Group(string name) { var g = new GameObject(name).transform; g.SetParent(root, false); return g; }
        static GameObject Load(string path)
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!g) Log("MISSING model " + path);
            return g;
        }
        static Bounds Bounds(Renderer[] rs)
        {
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // Instantiates a prefab / model, strips colliders, optionally overrides every material, scales to a target
        // height or footprint, and sets its base into the ground (sink = fraction of its height).
        static GameObject Place(GameObject src, Transform parent, float x, float z, float size, bool sizeIsHeight, float yaw,
                                Material mat = null, float sink = 0.08f, float tilt = 0f, bool shadows = true)
            => PlaceW(src, parent, x * SX, z * SZ, size, sizeIsHeight, yaw, mat, sink, tilt, shadows);
        static GameObject PlaceW(GameObject src, Transform parent, float x, float z, float size, bool sizeIsHeight, float yaw,
                                Material mat, float sink, float tilt, bool shadows)
        {
            if (!src) return null;
            var go = (GameObject)Object.Instantiate(src, parent);
            go.name = src.name;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var l in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(l);
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) { Object.DestroyImmediate(go); return null; }
            if (mat)
                foreach (var r in rs) { var a = r.sharedMaterials; for (int i = 0; i < a.Length; i++) a[i] = mat; r.sharedMaterials = a; }
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.Euler(R(-tilt, tilt), yaw, R(-tilt, tilt));
            go.transform.localScale = Vector3.one;
            var b = Bounds(rs);
            float cur = sizeIsHeight ? b.size.y : Mathf.Max(b.size.x, b.size.z);
            if (cur > 1e-4f) go.transform.localScale = Vector3.one * (size / cur);
            b = Bounds(rs);
            float wide = Mathf.Max(b.size.x, b.size.z);
            if (sizeIsHeight && size < 3f && wide > size * 1.6f)   // flat-lying plant scans: size them by their spread, not their height
            { go.transform.localScale *= size * 1.6f / wide; b = Bounds(rs); }
            float gy = GroundY(x, z);
            if (!sizeIsHeight && size > 2.5f)
                for (int a = 0; a < 12; a++)
                {
                    float an = a * Mathf.PI / 6f;
                    gy = Mathf.Min(gy, GroundY(x + Mathf.Cos(an) * b.extents.x * 0.9f, z + Mathf.Sin(an) * b.extents.z * 0.9f));
                    gy = Mathf.Min(gy, GroundY(x + Mathf.Cos(an) * b.extents.x * 0.5f, z + Mathf.Sin(an) * b.extents.z * 0.5f));
                }
            go.transform.position += new Vector3(x - b.center.x, gy - b.min.y - b.size.y * sink, z - b.center.z);
            foreach (var r in rs) { r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true; }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // A pack FBX holds its pieces side by side: one template per mesh (LOD0 only), destroyed after the build.
        static GameObject[] Split(string fbx, string matKey)
        {
            var src = Load(fbx);
            var list = new List<GameObject>();
            if (!src) return list.ToArray();
            var inst = (GameObject)Object.Instantiate(src);
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                if (r.name.Contains("LOD") && !r.name.Contains("LOD0")) continue;
                var t = new GameObject(Path.GetFileNameWithoutExtension(fbx) + "_" + list.Count);
                var c = new GameObject("mesh"); c.transform.SetParent(t.transform, false);
                c.transform.rotation = r.transform.rotation; c.transform.localScale = r.transform.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = c.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats.TryGetValue(matKey, out var m) && m ? new[] { m } : r.sharedMaterials;
                t.transform.position = new Vector3(0, -500, 0);
                list.Add(t); templates.Add(t);
            }
            if (list.Count == 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) sb.Append(r.GetType().Name + ":" + r.name + " ");
                Log("SPLIT EMPTY " + fbx + " -> " + sb);
            }
            Object.DestroyImmediate(inst);
            return list.ToArray();
        }
        static GameObject[] SplitMany(string matKey, params string[] fbx)
        {
            var l = new List<GameObject>();
            foreach (var f in fbx) l.AddRange(Split(f, matKey));
            return l.ToArray();
        }
        static GameObject Pick(GameObject[] a) => a != null && a.Length > 0 ? a[rng.Next(a.Length)] : null;

        static GameObject msTree, cliff, boulder, beachRock, house, choga;
        static GameObject[] bareTrees, deadTrees, planks, sticks, pebbles, smallRocks, rubble, debris, wildDry, dryGrass, clumps, shrubs;

        static void LoadModels()
        {
            cliff = Load(MS + "TF_RockA.fbx");
            boulder = Load(MS + "TF_RockB.fbx");
            msTree = Load(MS + "TF_DeadTree.fbx");
            beachRock = Load(LSMS + "nordic_beach_rock_uknmedo_mid/Nordic_Beach_Rock_uknmedo_Mid.fbx");
            house = Load(EF + "EF_ThatchHouse.prefab");
            choga = Load(EF + "VH_AV_ChogaB.prefab");
            var bt = new List<GameObject>();
            foreach (var p in new[] { AV + "ThirdParty/PolyHaven/bare_trees/AV_BareTree_A.fbx", AV + "ThirdParty/PolyHaven/bare_trees/AV_BareTree_B.fbx" }) { var g = Load(p); if (g) bt.Add(g); }
            bareTrees = bt.ToArray();
            var dt = new List<GameObject>();
            foreach (var n in new[] { "EF_RF_DeadTreeA", "EF_RF_DeadTreeB", "EF_RF_DeadTreeC", "EF_RF_Snag_A", "EF_RF_Snag_C" }) { var g = Load(EF + n + ".prefab"); if (g) dt.Add(g); }
            deadTrees = dt.ToArray();
            planks = Split(AVMS + "Planks/AV_MS_Planks.fbx", "MS_Planks");
            sticks = Split(AVMS + "Sticks/AV_MS_Sticks.fbx", "MS_Sticks");
            pebbles = Split(AVMS + "Pebbles/AV_MS_Pebbles.fbx", "MS_Pebbles");
            smallRocks = Split(AVMS + "SmallRocks/AV_MS_SmallRocks.fbx", "MS_SmallRocks");
            rubble = Split(AVMS + "RubblePatch/AV_MS_RubblePatch.fbx", "MS_RubblePatch");
            debris = Split(AVMS + "Debris/AV_MS_Debris.fbx", "MS_Debris");
            var wd = new List<string>(); foreach (var v in "ABCDEFGHIJKL") wd.Add(AVMS + $"WildDry/AV_MS_WildDry_Var{v}.fbx");
            wildDry = SplitMany("MS_WildDry", wd.ToArray());
            var dg = new List<string>(); foreach (var v in "ABCDE") dg.Add(AVMS + $"DryGrass/AV_MS_DryGrass_Var{v}.fbx");
            dryGrass = SplitMany("MS_DryGrass", dg.ToArray());
            clumps = SplitMany("MS_Clumps", AVMS + "Clumps/AV_MS_Clumps_VarA.fbx", AVMS + "Clumps/AV_MS_Clumps_VarB.fbx", AVMS + "Clumps/AV_MS_Clumps_VarC.fbx");
            var sh = new List<string>(); foreach (var v in "ABCDEFGH") sh.Add(AVMS + $"shrubs/AV_MS_shrubs_Var{v}.fbx");
            shrubs = SplitMany("MS_shrubs", sh.ToArray());
            Log($"models: bare {bareTrees.Length} dead {deadTrees.Length} planks {planks.Length} sticks {sticks.Length} pebbles {pebbles.Length} smallRocks {smallRocks.Length} rubble {rubble.Length} debris {debris.Length} wildDry {wildDry.Length} dryGrass {dryGrass.Length} clumps {clumps.Length} shrubs {shrubs.Length}");
        }

        // n small things inside a patch: the patch (where, what, how many) is the hand-made decision
        static void Scatter(Transform parent, GameObject[] set, float cx, float cz, float radius, int n, float sizeMin, float sizeMax, bool sizeIsHeight, float sink, float tilt = 6f, bool shadows = false)
        {
            if (set == null || set.Length == 0) return;
            cx *= SX; cz *= SZ; radius *= 0.75f;
            for (int i = 0; i < n; i++)
            {
                float a = R(0, 6.283f), d = radius * Mathf.Sqrt(R(0, 1));
                float x = cx + Mathf.Cos(a) * d, z = cz + Mathf.Sin(a) * d;
                if (PuddleAt(x, z) > 0.25f) continue;                  // nothing floats in the water
                PlaceW(Pick(set), parent, x, z, R(sizeMin, sizeMax), sizeIsHeight, R(0, 360), null, sink, tilt, shadows);
            }
        }

        // ------------------------------------------------------------------ water
        static void BuildWater()
        {
            float ox = -WX + 0.5f, oz = -WZ + 0.5f, sx = WX * 2 - 1, sz = (NZ * SZ - 0.4f) - oz;
            // water depth under the plane, baked from the same height function the terrain uses: 0 at the shore, 1 at 5 cm
            const int MW = 2048, MH = 1024;
            var tex = new Texture2D(MW, MH, TextureFormat.RGB24, false, true);
            var px = new Color32[MW * MH];
            for (int y = 0; y < MH; y++)
                for (int x = 0; x < MW; x++)
                {
                    float d = WaterY - HeightAt(ox + (x + 0.5f) / MW * sx, oz + (y + 0.5f) / MH * sz);
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(d / 0.05f) * 255f);
                    px[y * MW + x] = new Color32(v, v, v, 255);
                }
            tex.SetPixels32(px); tex.Apply();
            string mp = Gen + "/TF_PuddleMask.png";
            File.WriteAllBytes(Path.Combine(ProjectDir, mp), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(mp, ImportAssetOptions.ForceUpdate);
            var imp = AssetImporter.GetAtPath(mp) as TextureImporter;
            if (imp) { imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.SaveAndReimport(); }
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(mp);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Src + "HDRI/kloppenheim_02_2k.hdr");
            var wm = Shader.Find("D4Field/Puddle") ? Mat("TF_Puddle", "D4Field/Puddle", m =>
            {
                m.SetTexture("_Mask", mask); m.SetTexture("_Sky", cube);
                m.SetColor("_SkyTint", new Color(1.0f, 0.99f, 0.96f)); m.SetFloat("_SkyInt", 1.7f); m.SetFloat("_F0", 0.34f);
                m.SetColor("_Deep", new Color(0.035f, 0.034f, 0.032f)); m.SetFloat("_Blur", 1.5f); m.SetFloat("_Glint", 2.5f);
                m.SetVector("_Origin", new Vector4(ox, oz, sx, sz));
            }) : null;
            if (!wm) { Log("MISSING shader D4Field/Puddle or sky cube " + (cube ? "ok" : "NULL") + ": plain water"); wm = mats["TF_Water"]; }
            var w = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(w.GetComponent<Collider>());
            w.name = "PuddleWater"; w.transform.SetParent(root, false);
            w.transform.position = new Vector3(ox + sx * 0.5f, WaterY, oz + sz * 0.5f);
            w.transform.localScale = new Vector3(sx / 10f, 1, sz / 10f);
            var r = w.GetComponent<Renderer>(); r.sharedMaterial = wm; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ landmarks (all hand-placed: x, z, size, yaw)
        static void BuildRidge()
        {
            var g = Group("RockRidge");
            // the ridge: big cliff pieces behind the north line, stepping down toward the basin head
            foreach (var p in new[] {
                new[] { 8f, 31f, 15f, 20f }, new[] { 19f, 30f, 17f, 200f }, new[] { 30f, 29f, 15f, 95f }, new[] { -16f, 34f, 13f, 150f },
                new[] { 12f, 26.5f, 9f, 300f }, new[] { 24f, 26f, 10f, 40f }, new[] { 36f, 22f, 12f, 250f }, new[] { 5.5f, 27f, 7f, 70f } })
                Place(cliff, g, p[0], p[1] - RS, p[2] * 1.0f, false, p[3], mats["TF_Cliff"], 0.3f, 0f);
            // boulders along the walkable line (just outside it)
            foreach (var p in new[] {
                new[] { 6.5f, 24f, 4.2f, 10f }, new[] { 13f, 23.6f, 3.4f, 120f }, new[] { 19.5f, 24.2f, 4.6f, 220f }, new[] { 27f, 23.8f, 3.8f, 60f },
                new[] { 3.5f, 24.5f, 3.0f, 300f }, new[] { -11f, 25f, 3.6f, 170f }, new[] { 16f, 22.9f, 2.2f, 30f }, new[] { 23.5f, 23.1f, 2.0f, 280f } })
                Place(rng.NextDouble() < 0.5 ? boulder : cliff, g, p[0], p[1] - RS, p[2] * 1.9f, false, p[3], rng.NextDouble() < 0.5 ? mats["TF_Boulder"] : mats["TF_Cliff"], 0.3f, 0f);
            // rock spilling into the arena at the ridge foot: low slabs only (no collider, under 0.6 m)
            foreach (var p in new[] { new[] { 9f, 19.5f, 1.6f }, new[] { 12.5f, 18f, 1.1f }, new[] { 7f, 17.2f, 0.9f }, new[] { 15f, 20f, 1.4f }, new[] { 4f, 20.3f, 1.2f }, new[] { 20f, 19.6f, 1.0f } })
                Place(boulder, g, p[0], p[1] - RS, p[2] * 1.6f, false, R(0, 360), mats["TF_Boulder"], 0.72f, 0f);
            // rock walls flanking the gully and closing its back (covers the bare slope)
            foreach (var p in new[] { new[] { -10.5f, 25f, 9f, 40f }, new[] { 2f, 25.5f, 10f, 160f }, new[] { -8f, 21f, 4.5f, 210f }, new[] { 0.5f, 21.5f, 4f, 20f } })
                Place(p[2] > 8f ? cliff : boulder, g, p[0], p[1] - RS, p[2], false, p[3], p[2] > 8f ? mats["TF_Cliff"] : mats["TF_Boulder"], 0.3f, 0f);
            // rock breaking through the mud: low outcrops only (no collider, mostly buried)
            foreach (var p in new[] { new[] { -8f, 12f, 3.2f }, new[] { -1f, 5.5f, 2.6f }, new[] { -9.5f, -5f, 3.4f }, new[] { 3.5f, -2f, 2.4f }, new[] { -15f, 5f, 3f }, new[] { -3f, 13.5f, 2.8f } })
                Place(boulder, g, p[0], p[1], p[2], false, R(0, 360), mats["TF_Boulder"], 0.78f, 0f);
            // the gravel field (east): bedrock breaking the surface in a few places, each with weeds on its lee side
            foreach (var p in new[] { new[] { 14f, 5f, 5.5f }, new[] { 21f, -5f, 6.5f }, new[] { 12.5f, -12f, 4.5f }, new[] { 25f, 9f, 5f }, new[] { 17.5f, 13f, 4f } })
            {
                Place(cliff, g, p[0], p[1], p[2] * 1.3f, false, R(0, 360), mats["TF_Cliff"], 0.66f, 0f);
                Place(boulder, g, p[0] + 2.2f, p[1] - 1.5f, p[2] * 0.6f, false, R(0, 360), mats["TF_Boulder"], 0.55f, 0f);
                Scatter(g, wildDry, p[0] - 1.5f, p[1] - 2f, 2.2f, 6, 0.4f, 0.8f, true, 0.05f);
            }
            // stones sitting in the bank where mud meets dry ground (hand-spaced along the basin's east and west shores)
            foreach (var p in new[] { new[] { 5.5f, 14f, 1.5f }, new[] { 5.8f, 5.5f, 1.9f }, new[] { 2.2f, -7.5f, 1.6f },
                                      new[] { -10.5f, 14.5f, 1.6f }, new[] { -13.5f, 9f, 1.1f }, new[] { -15.5f, 0.5f, 1.8f }, new[] { -14f, -5.5f, 1.2f }, new[] { -18.5f, -11f, 1.5f } })
                Place(boulder, g, p[0], p[1], p[2], false, R(0, 360), mats["TF_Boulder"], 0.6f, 0f);
            FixBoulderMats(g);
            Scatter(g, smallRocks, 10f, 19f, 5f, 5, 0.3f, 0.6f, false, 0.55f);
            Scatter(g, pebbles, 12f, 17f, 6f, 12, 0.2f, 0.45f, false, 0.25f);
            Scatter(g, wildDry, 8f, 22.6f, 7f, 10, 0.5f, 0.9f, true, 0.05f);
        }
        // boulders placed with a null material keep the FBX default: give every rock in the group a real one
        static void FixBoulderMats(Transform g)
        {
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
            {
                var n = r.transform.root == root ? r.transform.name : r.name;
                var top = r.transform; while (top.parent && top.parent != g) top = top.parent;
                if (top.name.StartsWith("Nordic")) r.sharedMaterial = mats["TF_BeachRock"];
                else if (top.name.StartsWith("TF_RockB")) r.sharedMaterial = mats["TF_Boulder"];
            }
        }

        static void BuildTrees()
        {
            var g = Group("Trees");
            // bare twisted trees: the NW corner group (top-left of the screen) and a looser line down the west side
            foreach (var p in new[] {
                new[] { -22f, 25.5f, 8.5f, 0f }, new[] { -28.5f, 27f, 10f, 80f }, new[] { -15f, 27f, 7.5f, 200f }, new[] { -33f, 22f, 9f, 140f },
                new[] { -25f, 32f, 11f, 30f }, new[] { -11f, 31f, 9f, 260f }, new[] { -34.5f, 14f, 8f, 310f }, new[] { -36f, 4f, 9.5f, 100f },
                new[] { -34f, -7f, 7.5f, 220f }, new[] { -38f, 24f, 11f, 15f } })
                { if (msTree && rng.NextDouble() < 0.75) Place(msTree, g, p[0] * 0.92f, p[1] - RS - 3.5f, p[2] * 0.66f, true, p[3], mats["TF_DeadTree"], 0.03f, 7f);
                  else Place(bareTrees.Length > 0 ? bareTrees[rng.Next(bareTrees.Length)] : Pick(deadTrees), g, p[0], p[1] - RS, p[2], true, p[3], null, 0.02f, 5f); }
            foreach (var p in new[] { new[] { -19f, 30f, 9f }, new[] { -30f, 18f, 7f }, new[] { -31f, 31f, 12f }, new[] { -37f, -1f, 8f } })
                Place(Pick(deadTrees), g, p[0], p[1] - RS, p[2], true, R(0, 360), null, 0.02f, 6f);
            // under the trees: dry grass, shrubs, fallen sticks (the mid-scale layer v1 did not have)
            foreach (var p in new[] { new[] { -22f, 24f }, new[] { -28f, 25f }, new[] { -15f, 25.5f }, new[] { -32.5f, 20f }, new[] { -33f, 12f }, new[] { -34f, 3f }, new[] { -33f, -8f } })
            {
                Scatter(g, wildDry, p[0], p[1] - RS, 3.2f, 9, 0.6f, 1.2f, true, 0.05f);
                Scatter(g, shrubs, p[0], p[1] - RS, 2.4f, 4, 0.8f, 1.5f, true, 0.06f, 6f, true);
                Scatter(g, sticks, p[0], p[1] - RS, 3.5f, 5, 0.8f, 1.8f, false, 0.2f);
            }
            // the litter reaches into the arena as low tufts and sticks only
            foreach (var p in new[] { new[] { -24f, 18f }, new[] { -18f, 19f }, new[] { -26f, 11f }, new[] { -27f, 2f }, new[] { -25f, -6f }, new[] { -12f, 20f } })
            {
                Scatter(g, dryGrass, p[0], p[1] - RS, 3f, 10, 0.3f, 0.55f, true, 0.05f);
                Scatter(g, clumps, p[0], p[1] - RS, 3.4f, 8, 0.2f, 0.4f, true, 0.05f);
                Scatter(g, sticks, p[0], p[1] - RS, 3f, 3, 0.6f, 1.3f, false, 0.25f);
            }
            // one big dead tree leans in from the left edge of the frame (reference: the branch coming over the roof)
            foreach (var p in new[] { new[] { -31f, 7f, 8f, 40f }, new[] { -32f, -7f, 7f, 32f } })
            {
                var t = msTree ? Place(msTree, g, p[0], p[1], p[2], true, R(0, 360), mats["TF_DeadTree"], 0.03f, 0f) : Place(bareTrees.Length > 0 ? bareTrees[0] : Pick(deadTrees), g, p[0], p[1], p[2], true, R(0, 360), null, 0.03f, 0f);
                if (!t) continue;
                var foot = new Vector3(p[0] * SX, GroundY(p[0] * SX, p[1] * SZ), p[1] * SZ);
                t.transform.RotateAround(foot, Vector3.forward, -p[3]);
                t.name = "LeaningTree";
            }
            var dark = new Dictionary<Material, Material>();
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var top = r.transform; while (top.parent && top.parent != g) top = top.parent;
                if (!(top.name.Contains("Tree") || top.name.Contains("Snag"))) continue;
                var a = r.sharedMaterials;
                for (int i = 0; i < a.Length; i++)
                {
                    var om = a[i]; if (!om) continue;
                    if (!dark.TryGetValue(om, out var dm))
                    {
                        var dp = $"{MatDir}/TF_T_{om.name}.mat";
                        AssetDatabase.DeleteAsset(dp);
                        dm = new Material(om); AssetDatabase.CreateAsset(dm, dp);
                        foreach (var pn in new[] { "_BaseColor", "_Color" })
                            if (dm.HasProperty(pn)) { var c = dm.GetColor(pn); dm.SetColor(pn, new Color(c.r * 0.34f, c.g * 0.33f, c.b * 0.32f, c.a)); }
                        dark[om] = dm;
                    }
                    a[i] = dm;
                }
                r.sharedMaterials = a;
            }
        }

        static void BuildHouse()
        {
            var g = Group("House");
            // the thatched house sits below the south-west corner: from the game camera its roof is the dark near
            // foreground at the bottom-left of the screen (reference)
            var h = Place(house ? house : choga, g, -21f, -26f, 6.2f, true, 28f, null, 0.04f, 0f);
            if (h)
            {
                h.name = "ThatchHouse_SW";
                var sb = new System.Text.StringBuilder("house renderers: ");
                foreach (var hr in h.GetComponentsInChildren<Renderer>()) sb.Append(hr.name + "[" + (hr.sharedMaterial ? hr.sharedMaterial.name + "/" + hr.sharedMaterial.shader.name : "null") + "] ");
                Log(sb.ToString());
                foreach (var hr in h.GetComponentsInChildren<Renderer>())
                {
                    var om = hr.sharedMaterial; if (!om || !om.name.Contains("Roof")) continue;
                    Texture bt = om.HasProperty("_BaseMap") ? om.GetTexture("_BaseMap") : null;
                    if (!bt && om.HasProperty("_MainTex")) bt = om.GetTexture("_MainTex");
                    Texture nt = om.HasProperty("_BumpMap") ? om.GetTexture("_BumpMap") : null;
                    bool cut = om.name.Contains("ACC");
                    var rm = Mat("TF_" + om.name, "Universal Render Pipeline/Lit", m =>
                    {
                        m.SetTexture("_BaseMap", bt); m.SetColor("_BaseColor", new Color(0.52f, 0.46f, 0.35f, 1f));
                        m.SetTexture("_BumpMap", nt); if (nt) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
                        m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0f); m.SetFloat("_Cull", 0f);
                        m.SetFloat("_AlphaClip", cut ? 1f : 0f); m.SetFloat("_Cutoff", 0.4f);
                        if (cut) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                        m.renderQueue = cut ? 2450 : 2000;
                    });
                    Log("roof " + om.name + " tex " + (bt ? bt.name : "null") + " nrm " + (nt ? nt.name : "null"));
                    if (rm && bt) hr.sharedMaterial = rm;
                }
            }
            var h2 = Place(choga ? choga : house, g, -38f, -22f, 5.4f, true, 118f, null, 0.04f, 0f);
            if (h2) h2.name = "Choga_W";
            // the yard between the house and the basin: planks, broken wood, rubble, pressed flat into the mud
            Scatter(g, planks, -13f, -17.5f, 2.6f, 7, 1.0f, 1.9f, false, 0.35f, 3f);
            Scatter(g, planks, -20f, -19.5f, 2.2f, 5, 0.9f, 1.6f, false, 0.35f, 3f);
            Scatter(g, debris, -16f, -19f, 4f, 9, 0.5f, 1.1f, false, 0.3f);
            Scatter(g, wildDry, -17f, -23.2f, 5f, 12, 0.6f, 1.1f, true, 0.05f);
            Scatter(g, wildDry, -29f, -20f, 4f, 10, 0.6f, 1.2f, true, 0.05f);
            Scatter(g, dryGrass, -23f, -18f, 3.5f, 10, 0.3f, 0.55f, true, 0.05f);
            // one warm lantern under the eave: the only colour in the frame besides the fight
            var lg = new GameObject("EaveLantern").AddComponent<Light>();
            lg.transform.SetParent(g, false);
            lg.transform.position = new Vector3(-15.5f * SX, GroundY(-15.5f * SX, -24.5f * SZ) + 2.1f, -24.5f * SZ);
            lg.type = LightType.Point; lg.color = Hex("#ff9a4a"); lg.intensity = 5f; lg.range = 9f;
            lg.shadows = LightShadows.None; lg.renderMode = LightRenderMode.ForcePixel;
            lightCount++;
            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.name = "Flame"; flame.transform.SetParent(g, false);
            flame.transform.position = lg.transform.position; flame.transform.localScale = Vector3.one * 0.16f;
            flame.GetComponent<Renderer>().sharedMaterial = mats["TF_Flame"]; flame.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildEastAndSouth()
        {
            var g = Group("EastSouth");
            foreach (var p in new[] {
                new[] { 33.5f, 14f, 5f, 0f }, new[] { 34f, 5f, 3.6f, 90f }, new[] { 35.5f, -3f, 6f, 200f }, new[] { 33f, -11f, 3.2f, 300f },
                new[] { 36f, -18f, 5.5f, 45f }, new[] { 40f, 9f, 8f, 130f }, new[] { 41f, -9f, 9f, 20f } })
                Place(p[2] > 7f ? cliff : boulder, g, p[0], p[1], p[2], false, p[3], p[2] > 7f ? mats["TF_Cliff"] : mats["TF_Boulder"], 0.42f, 0f);
            Scatter(g, wildDry, 32.5f, 9f, 3f, 7, 0.5f, 1f, true, 0.05f);
            Scatter(g, wildDry, 32.8f, -7f, 3f, 7, 0.5f, 1f, true, 0.05f);
            Scatter(g, smallRocks, 30f, 0f, 3.5f, 3, 0.3f, 0.6f, false, 0.55f);
            // camera side (south), east half: sparse near-black shapes below the edge (Kido rule: bottom edge only)
            var sil = mats["TF_Silhouette"];
            Place(Pick(deadTrees), g, 9f, -29f, 8f, true, 40f, sil, 0.02f, 6f);
            Place(boulder, g, 21f, -28.5f, 6f, false, 110f, sil, 0.3f, 8f);
            Place(Pick(deadTrees), g, 31f, -30f, 9f, true, 200f, sil, 0.02f, 6f);
            Place(boulder, g, 1f, -30f, 4.5f, false, 10f, sil, 0.3f, 8f);
        }

        // the mid-scale layer on the open ground: every patch below is a decision (where the eye should find something)
        static void BuildGroundDressing()
        {
            var g = Group("GroundDressing");
            // stones gathered on the basin's shores
            foreach (var p in new[] { new[] { 2f, 11f }, new[] { -9f, 8.5f }, new[] { 1.5f, 1f }, new[] { -11.5f, -1.5f }, new[] { -1f, -7f }, new[] { -13f, -8.5f }, new[] { 4.5f, -6f }, new[] { -6f, 15f } })
            {
                Scatter(g, pebbles, p[0], p[1], 1.6f, 4, 0.15f, 0.32f, false, 0.45f);
                Scatter(g, smallRocks, p[0], p[1], 1.4f, 1, 0.3f, 0.55f, false, 0.55f);
            }
            // the gravel field (east): rubble patches and bigger loose stones, with open rests between them
            foreach (var p in new[] { new[] { 12f, 8f }, new[] { 20f, 2f }, new[] { 14f, -8f }, new[] { 24f, -13f }, new[] { 22f, 12f }, new[] { 9f, -15f } })
            {
                Scatter(g, smallRocks, p[0], p[1], 1.6f, 2, 0.3f, 0.6f, false, 0.55f);
                Scatter(g, pebbles, p[0], p[1], 1.8f, 3, 0.15f, 0.35f, false, 0.45f);
            }
            // dead weeds where gravel meets soil, sticks washed along the runnel
            foreach (var p in new[] { new[] { 7f, 3f }, new[] { 8.5f, -10f }, new[] { 16f, 15f }, new[] { 26f, 6f }, new[] { 18f, -17f } })
                Scatter(g, dryGrass, p[0], p[1], 2.4f, 8, 0.3f, 0.5f, true, 0.05f);
            foreach (var p in new[] { new[] { -1.5f, -10f }, new[] { -2f, -16f }, new[] { 0.5f, -20f } })
                Scatter(g, sticks, p[0], p[1], 1.6f, 4, 0.6f, 1.4f, false, 0.25f);
        }

        // ------------------------------------------------------------------ mist
        static void BuildMist()
        {
            var fog = Group("Mist");
            if (!mats.TryGetValue("Fog", out var fm) || !fm) return;
            void Field(string name, Vector3 c, Vector3 size, int count, float sizeMin, float sizeMax, float alpha)
            {
                c = new Vector3(c.x * SX, c.y, c.z * SZ); size = new Vector3(size.x * SX, size.y, size.z * SZ); sizeMin *= 0.7f; sizeMax *= 0.7f;
                var go = new GameObject(name); go.transform.SetParent(fog, false); go.transform.position = c;
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main; main.loop = true; main.startLifetime = 40f; main.startSpeed = 0f; main.maxParticles = count;
                main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = new Color(1, 1, 1, alpha); main.simulationSpace = ParticleSystemSimulationSpace.World; main.prewarm = true;
                var em = ps.emission; em.rateOverTime = count / 40f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.x = new ParticleSystem.MinMaxCurve(0.1f, 0.3f); vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                var col = ps.colorOverLifetime; col.enabled = true;
                var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
                col.color = gr;
                var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = fm; r.sortingFudge = 10;
                r.renderMode = ParticleSystemRenderMode.Billboard; r.maxParticleSize = 3f;
                ps.Simulate(40f, true, true); ps.Play();
            }
            Field("MistTrees", new Vector3(-24f, 0.9f, 22f), new Vector3(34, 1.2f, 16), 80, 9f, 15f, 0.42f);
            Field("MistGully", new Vector3(-4f, 1.4f, 25f), new Vector3(14, 1.5f, 12), 26, 8f, 13f, 0.26f);
            Field("MistRidge", new Vector3(14f, 2.6f, 28f), new Vector3(50, 3f, 10), 60, 9f, 16f, 0.45f);
            Field("MistWest", new Vector3(-34f, 1.2f, 2f), new Vector3(12, 1.6f, 36), 40, 8f, 14f, 0.36f);
            // no mist over the open arena: it veiled the player (Kido 11-15 20:10). Mist stays at the edges only.
        }

        // ------------------------------------------------------------------ light and post
        static void BuildLightAndPost()
        {
            var key = new GameObject("OvercastKey").AddComponent<Light>();
            key.transform.SetParent(root, false);
            key.type = LightType.Directional; key.color = Hex("#e8eaec"); key.intensity = 2.0f;
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.9f; key.lightmapBakeType = LightmapBakeType.Realtime;
            key.transform.rotation = Quaternion.Euler(40f, 122f, 0f);   // from the top-left of the screen: shadows fall down-right (reference)
            lightCount++;
            RenderSettings.sun = key;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#909295");
            RenderSettings.ambientEquatorColor = Hex("#626467");
            RenderSettings.ambientGroundColor = Hex("#2e2d2c");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#7d8184");
            RenderSettings.fogDensity = 0.014f;

            // overcast sky as the reflection source (puddles, wet mud)
            string hdri = Src + "HDRI/kloppenheim_02_2k.hdr";
            var imp = AssetImporter.GetAtPath(hdri) as TextureImporter;
            if (imp)
            {
                if (imp.textureShape != TextureImporterShape.TextureCube || imp.maxTextureSize != 512)
                { imp.textureShape = TextureImporterShape.TextureCube; imp.maxTextureSize = 512; imp.sRGBTexture = false; }
                var ts = new TextureImporterSettings(); imp.ReadTextureSettings(ts);
                ts.cubemapConvolution = TextureImporterCubemapConvolution.Specular; ts.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
                imp.SetTextureSettings(ts); imp.SaveAndReimport();
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(hdri);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 0.55f;
                Log("reflection cube " + (cube ? cube.width.ToString() : "NULL"));
                var rp = new GameObject("SkyReflection").AddComponent<ReflectionProbe>();
                rp.transform.SetParent(root, false); rp.transform.position = new Vector3(0, 4, 0);
                rp.mode = ReflectionProbeMode.Custom; rp.customBakedTexture = cube; rp.size = new Vector3(160, 60, 160);
                rp.intensity = 1.1f; rp.boxProjection = false; rp.importance = 10;
            }
            else Log("MISSING hdri import " + hdri);

            LightingSettings ls = null;
            try { ls = Lightmapping.lightingSettings; } catch { }
            if (ls == null)
            {
                var lsPath = Root + "/D4_Lighting.lighting";
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
                if (!ls) { ls = new LightingSettings { name = "D4_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = false; ls.realtimeGI = false;

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true; vol.priority = 2000;
            vol.sharedProfile = Profile();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#3a4047");
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cam.gameObject.AddComponent<AudioListener>();
            var off = new Vector3(0, 17f, -14.5f) * 1.6f;
            cam.transform.position = off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }

        static VolumeProfile Profile()
        {
            var path = Root + "/D4_PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.2f); ca.contrast.Override(24f); ca.saturation.Override(-22f); ca.hueShift.Override(0f); ca.colorFilter.Override(Hex("#F3F1EC"));
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.25f); bl.intensity.Override(0.45f); bl.scatter.Override(0.6f); bl.highQualityFiltering.Override(false);
            var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#05070a")); vg.intensity.Override(0.26f); vg.smoothness.Override(0.5f);
            var dofO = prof.Add<DepthOfField>(true); dofO.mode.Override(DepthOfFieldMode.Off);
            var cab = prof.Add<ChromaticAberration>(true); cab.intensity.Override(0f);
            var smh = prof.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(1f, 1f, 1f, 0f)); smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f)); smh.highlights.Override(new Vector4(1f, 1f, 1f, 0f));
            var lgg = prof.Add<LiftGammaGain>(true);
            lgg.lift.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f)); lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        // ------------------------------------------------------------------ invisible walls and the arena check
        static void BuildEdgeColliders()
        {
            var walls = Group("ArenaWalls");
            void Box(Vector3 c, Vector3 s) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.transform.position = c; w.AddComponent<BoxCollider>().size = s; }
            Box(new Vector3(-WX - 0.5f, 2, 0), new Vector3(1, 8, 2 * WZ + 2));
            Box(new Vector3(WX + 0.5f, 2, 0), new Vector3(1, 8, 2 * WZ + 2));
            Box(new Vector3(0, 2, -WZ - 0.5f), new Vector3(2 * WX + 2, 8, 1));
            Box(new Vector3(0, 2, NZ * SZ + 0.5f), new Vector3(2 * WX + 2, 8, 1));
        }

        static int CheckArena()
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (c is TerrainCollider || c.transform.parent.name == "ArenaWalls") continue;
                var b = c.bounds;
                if (b.max.x <= -WX || b.min.x >= WX || b.max.z <= -WZ || b.min.z >= NZ * SZ) continue;
                n++; Log($"COLLIDER IN ARENA {c.name} {b.center}");
            }
            float maxH = 0f; string who = "";
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var b = r.bounds;
                if (Mathf.Abs(b.center.x) < WX - 1f && Mathf.Abs(b.center.z) < WZ - 1f && b.max.y > maxH) { maxH = b.max.y; who = r.transform.parent ? r.transform.parent.name : r.name; }
            }
            Log($"tallest thing inside the arena: {maxH:F2} m ({who})");
            return n;
        }

        // ------------------------------------------------------------------ debug: editor renders + splat stats
        public static void Overview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Log("ABORT overview: play mode"); return; }
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty) { Log("ABORT overview: unsaved " + active.path); return; }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            var dir = Path.Combine(ProjectDir, "ArtSource", "D4Field");
            void Shot(string name, Vector3 pos, Vector3 look, float fov)
            {
                var go = new GameObject("OverviewCam");
                var cam = go.AddComponent<Camera>();
                var cd = go.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true;
                cam.fieldOfView = fov; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f; cam.allowHDR = true;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#3a4047");
                go.transform.position = pos; go.transform.rotation = Quaternion.LookRotation(look - pos);
                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(88));
                cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
                Log("overview " + name);
            }
            var off = new Vector3(0, 17f, -14.5f) * 1.6f;
            void Game(string name, float lx, float lz) { float x = lx * SX, z = lz * SZ; Shot(name, new Vector3(x, 0, z) + off, new Vector3(x, 0, z), 30f); }
            Shot("ov_top", new Vector3(0, 150, -1), Vector3.zero, 40f);
            Game("ov_g_basin", -4f, 4f);
            Game("ov_g_house", -11f, -12f);
            Game("ov_g_trees", -17f, 13f);
            Game("ov_g_ridge", 8f, 16f);
            Game("ov_g_gravel", 18f, -4f);
        }

        // ------------------------------------------------------------------ RoomRun dev room
        public static void AddRoomRunRoom()
        {
            const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
            if (!cfg) { Log("NO RoomRunConfig"); return; }
            var so = new SerializedObject(cfg);
            var rooms = so.FindProperty("rooms");
            var size = new Vector2(WX * 2, WZ * 2);
            const string display = "황야 (타래 방향)";
            for (int i = 0; i < rooms.arraySize; i++)
                if (rooms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == RoomId)
                {
                    rooms.GetArrayElementAtIndex(i).FindPropertyRelative("size").vector2Value = size;
                    rooms.GetArrayElementAtIndex(i).FindPropertyRelative("displayName").stringValue = display;
                    so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
                    return;
                }
            int at = rooms.arraySize;
            rooms.InsertArrayElementAtIndex(at - 1);
            var r = rooms.GetArrayElementAtIndex(at);
            r.FindPropertyRelative("id").stringValue = RoomId;
            r.FindPropertyRelative("displayName").stringValue = display;
            r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(9, 0);
            r.FindPropertyRelative("scenePath").stringValue = ScenePath;
            r.FindPropertyRelative("groundMeshName").stringValue = "";
            r.FindPropertyRelative("center").vector3Value = Vector3.zero;
            r.FindPropertyRelative("size").vector2Value = size;
            r.FindPropertyRelative("fitToStage").boolValue = false;
            r.FindPropertyRelative("floorColor").colorValue = Hex("#1b1c18");
            r.FindPropertyRelative("pillarCount").intValue = 0;
            r.FindPropertyRelative("doors").arraySize = 0;
            r.FindPropertyRelative("waves").arraySize = 0;
            r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
            r.FindPropertyRelative("devOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Log("room 'd4field' added to RoomRunConfig");
        }
    }
}
