// Builds Assets/Game/BossSeal/BossSeal.unity: a boss arena after Kido's reference (Diablo IV Vessel of Hatred boss
// platform), about twice its size, with a Korean shamanic seal instead of its sigil (11-15).
//   - one flat floor mesh: a 30.8 m round sealing platform raised 0.3 m (gentle 0.8 m chamfer, ring lips of 3-4 cm:
//     nothing to step over), then reddish-brown dirt out to 75 m. Zones/inlays are drawn by BossSeal/Ground
//     (BSGround.shader) from downloaded images only: bagua octagon (CC0) + Korean taegeuk (PD flag) in the centre,
//     the Joseon 길상호신부 cinnabar talisman (CC BY 4.0) and the 삼두매 부적 print (PD) round the talisman ring, the
//     부적목판 woodblock characters round the outer ring; Poly Haven CC0 stone/sandstone/rusty metal/mud scans.
//   - 24 radial blade rays: Poly Haven CC0 antique_estoc / ornate_medieval_dagger scaled up (giant swords), lying flat
//     on the dirt (level, 1.5 cm up), roots under the platform rim; no colliders.
//   - edge only (11-15): 5 tall Megascans pillars far back at the north end and a short pillar + broken wall at each
//     bottom corner (DarkCrypt sources); tops sink into dark red haze (BossSeal/HazeProp), the floor melts into it northward.
//   - lights: moon + seal glow + 4 red rim lights = 6 realtime, nothing under the player.
//   - registers the RoomRun dev room "bossseal" (next dev field key).
// Nothing inside the arena has a collider except the floor. Re-running rebuilds the scene and materials.
// Run via rr_queue: "call BossSeal.BSBuilder.Build" (no menu items, project rule).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BossSeal
{
    public static class BSBuilder
    {
        public const string Root = "Assets/Game/BossSeal";
        public const string ScenePath = Root + "/BossSeal.unity";
        const string Src = Root + "/Source/";
        const string MatDir = Root + "/Materials";
        const string Gen = Root + "/Generated";
        public const string RoomId = "bossseal";
        public const float HX = 30f, HZ = 26f;          // arena half size (60 x 52): the platform (r 15.6) plus the blade rays
        const string DC = "Assets/Game/DarkCrypt/Source/";
        const string PillarFbx = DC + "Fab_stone_pillar_selvf_mid/Stone_Pillar_selvF_Mid.fbx";
        const string WallFbx = DC + "Fab_castle_wall_sbxuw_mid/Castle_Wall_sbxuw_Mid.fbx";
        const string EstocFbx = Src + "PH_antique_estoc/antique_estoc_2k.fbx";
        const string CliffFbx = "Assets/Art/LookStudy/Stages/ThirdParty/Megascans/granite_cliff_rivav_mid/Granite_Cliff_rivav_Mid.fbx";
        const string CliffTex = "Assets/Art/LookStudy/Stages/ThirdParty/Megascans/granite_cliff_rivav_mid/Granite_Cliff_rivav_Mid_2K_";
        const string SpireFbx = "Assets/Underworld/Resources/LavaRocks/LR_Spire.fbx";
        const string DebrisFbx = "Assets/Game/AbandonedVillage/ThirdParty/Megascans/Debris/AV_MS_Debris.fbx";
        const string RocksFbx = "Assets/Game/AbandonedVillage/ThirdParty/Megascans/SmallRocks/AV_MS_SmallRocks.fbx";
        const float AbyssY = -42f;
        // 11-15 mesa (Kido: "외곽 아래로 떨어지는 큰 아레나 ... 절벽"): the whole arena stands on a raised mesa whose rim runs
        // just outside the arena walls on a wobbly ellipse; past it the ground falls away into a red abyss
        static float EdgeR(float ang)
        {
            float sn = Mathf.Sin(ang), cs = Mathf.Cos(ang);
            float ex = HX + 14f, ez = HZ + 4.5f;
            float e = ex * ez / Mathf.Sqrt(ez * ez * sn * sn + ex * ex * cs * cs);
            e += 1.6f * (Mathf.PerlinNoise(cs * 2.4f + 5.3f, sn * 2.4f + 7.1f) - 0.5f) * 2f + 0.9f * (Mathf.PerlinNoise(cs * 7f + 11.2f, sn * 7f + 3.4f) - 0.5f) * 2f;
            return Mathf.Lerp(90f, e, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.5f, cs)));   // south / sides: beyond the floor (80 m)
        }
        
        // floor radii (must match the BSGround material vectors)
        static readonly float[] Rad = { 2.1f, 2.3f, 3.7f, 3.87f, 4.73f, 4.93f, 5.2f };   // 11-15 redo: ~10 m dais (was 31 m)
        const float PlatY = 0.8f;   // dais top; tiers 0.8 / 0.55 / 0.3 down to the dirt

        static System.Random rng;
        static Transform root;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static int lightCount;

        static string LogPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "BossSeal", "bs_log.txt");
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
            rng = new System.Random(7721); pSmoke = null; pEmber = null;
            mats.Clear(); lightCount = 0;
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();
            BuildMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("BossSeal").transform;
            BuildFloor();
            BuildBlades();
            BuildEdge();
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
            if (imp.anisoLevel != 8) { imp.anisoLevel = 8; dirty = true; }   // 11-15: the low game camera saw the ground scans mip-blurred
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        // masks: linear, clamped, full mips (the shader reads blurred mips for the inlay edges)
        static Texture2D Mask(string file, int max = 2048)
        {
            var path = Src + "Motifs/" + file;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Log("MISSING mask " + path); return null; }
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true; imp.maxTextureSize = max; imp.alphaSource = TextureImporterAlphaSource.None;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static string PH(string id, string kind) => $"{Src}PH_{id}/{id}_{kind}_2k.jpg";

        static Material Mat(string name, string shader, System.Action<Material> init)
        {
            var path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (!sh) { Log("MISSING shader " + shader); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.CopyPropertiesFromMaterial(new Material(sh));   // the shader defaults are the tuned values; init sets the rest
            init(m);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static void BuildMaterials()
        {
            Mat("BS_Ground", "BossSeal/Ground", m =>
            {
                string[] gl = { "red_laterite_soil_stones", "terrain_red_01", "rocky_gravel", "red_dirt_mud_01" };   // 11-15 ground redo: base, fillers, accent
                for (int k = 0; k < 4; k++)
                {
                    m.SetTexture($"_G{k}", Tex(PH(gl[k], "diff"))); m.SetTexture($"_G{k}N", Tex(PH(gl[k], "nor_gl"), normal: true));
                    m.SetTexture($"_G{k}H", Tex(PH(gl[k], "disp"), linear: true, max: 1024));
                }
                m.SetTexture("_Stone", Tex(PH("dark_rock_02", "diff"))); m.SetTexture("_StoneN", Tex(PH("dark_rock_02", "nor_gl"), normal: true));
                m.SetTexture("_StoneAO", Tex(PH("dark_rock_02", "ao"), linear: true));
                m.SetTexture("_Trim", Tex(PH("red_sandstone_wall", "diff"))); m.SetTexture("_TrimN", Tex(PH("red_sandstone_wall", "nor_gl"), normal: true));
                m.SetTexture("_Metal", Tex(PH("rusty_metal_02", "diff"))); m.SetTexture("_MetalN", Tex(PH("rusty_metal_02", "nor_gl"), normal: true));
                m.SetTexture("_Seal", Mask("BS_Seal_TaegeukPalgwae.png"));
                m.SetTexture("_Bujeok", Mask("BS_Bujeok_Gilsang.png", 1024));
                m.SetTexture("_Hawk", Mask("BS_Samdumae.png", 1024));
                m.SetTexture("_Text", Mask("BS_Woodblock_Text.png", 1024));
                m.SetVector("_R1", new Vector4(Rad[0], Rad[1], Rad[2], Rad[3]));
                m.SetVector("_R2", new Vector4(Rad[4], Rad[5], Rad[6], 3.87f));
                m.SetVector("_Tiles", new Vector4(8, 20, 1.05f, 0.62f));
            });
            Mat("BS_Blade", "Universal Render Pipeline/Lit", m =>
            {
                const string d = Src + "PH_antique_estoc/textures/";
                m.SetTexture("_BaseMap", Tex(d + "antique_estoc_diff_2k.jpg")); m.SetColor("_BaseColor", Hex("#4a3c36"));   // dark iron
                var n = Tex(d + "antique_estoc_nor_gl_2k.exr", normal: true);
                m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.38f);   // edges catch the light
                m.enableInstancing = true;
            });
            Mat("BS_Cliff", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex(CliffTex + "BaseColor.jpg")); m.SetColor("_BaseColor", Hex("#7a5448"));
                var n = Tex(CliffTex + "Normal.jpg", normal: true); m.SetTexture("_BumpMap", n); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_OcclusionMap", Tex(CliffTex + "AO.jpg", linear: true)); m.EnableKeyword("_OCCLUSIONMAP");
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.05f); m.enableInstancing = true;
            });
            Mat("BS_Spire", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex("Assets/Underworld/Resources/LavaRocks/T_LR_Spire_BaseColor.jpg")); m.SetColor("_BaseColor", Hex("#6a463c"));
                var n = Tex("Assets/Underworld/Resources/LavaRocks/T_LR_Spire_Normal.jpg", normal: true); m.SetTexture("_BumpMap", n); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.05f); m.enableInstancing = true;
            });
            Mat("BS_Debris", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex("Assets/Game/AbandonedVillage/ThirdParty/Megascans/Debris/AV_MS_Debris_A.jpg")); m.SetColor("_BaseColor", Hex("#c08a78"));
                var n = Tex("Assets/Game/AbandonedVillage/ThirdParty/Megascans/Debris/AV_MS_Debris_N.jpg", normal: true); m.SetTexture("_BumpMap", n); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.05f); m.enableInstancing = true;
            });
            Mat("BS_Rocks", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex("Assets/Game/AbandonedVillage/ThirdParty/Megascans/SmallRocks/AV_MS_SmallRocks_A.jpg")); m.SetColor("_BaseColor", Hex("#b8806e"));
                var n = Tex("Assets/Game/AbandonedVillage/ThirdParty/Megascans/SmallRocks/AV_MS_SmallRocks_N.jpg", normal: true); m.SetTexture("_BumpMap", n); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.05f); m.enableInstancing = true;
            });
            Mat("BS_Abyss", "BossSeal/Abyss", m => { });
            Mat("BS_Pillar", "BossSeal/HazeProp", m =>
            {
                m.SetTexture("_BaseMap", Tex(DC + "Fab_stone_pillar_selvf_mid/Stone_Pillar_selvF_Mid_2K_BaseColor.jpg"));
                m.SetTexture("_BumpMap", Tex(DC + "Fab_stone_pillar_selvf_mid/Stone_Pillar_selvF_Mid_2K_Normal.jpg", normal: true));
            });
            Mat("BS_Wall", "BossSeal/HazeProp", m =>
            {
                m.SetTexture("_BaseMap", Tex(DC + "Fab_castle_wall_sbxuw_mid/Castle_Wall_sbxuw_Mid_2K_BaseColor.jpg"));
                m.SetTexture("_BumpMap", Tex(DC + "Fab_castle_wall_sbxuw_mid/Castle_Wall_sbxuw_Mid_2K_Normal.jpg", normal: true));
            });
        }

        static Transform Group(string name, Transform parent = null)
        {
            var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g;
        }

        // ------------------------------------------------------------------ floor: one radial mesh
        static float Height(float r)
        {
            // ring lips: 3-4 cm, ramped over 6 cm (walkable everywhere)
            float S(float a, float b, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x));
            // three thick tiers with vertical 25-30 cm risers (walkable steps): seal + talisman ring on top, text ring + metal
            // rim one step down, a stone foot ring one more step down, then the dirt
            float h = 0f;
            h += 0.3f * (1 - S(Rad[6] - 0.03f, Rad[6] + 0.03f, r));
            h += 0.25f * (1 - S(Rad[5] - 0.03f, Rad[5] + 0.03f, r));
            h += 0.25f * (1 - S(Rad[3] - 0.03f, Rad[3] + 0.03f, r));
            h += 0.02f * (S(Rad[0] - 0.03f, Rad[0] + 0.03f, r) - S(Rad[1] - 0.03f, Rad[1] + 0.03f, r));   // metal band lip
            return h;
        }

        static void BuildFloor()
        {
            var rs = new List<float>();
            for (float r = 0.25f; r < Rad[0] - 0.1f; r += 0.25f) rs.Add(r);
            foreach (var b in Rad)
                for (int k = -3; k <= 3; k++) rs.Add(b + k * 0.03f);
            for (float r = Rad[0]; r < Rad[6]; r += 0.15f) rs.Add(r);
            for (float r = Rad[6] + 0.25f; r < 80f; r = r < 12f ? r + 0.5f : r * 1.05f) rs.Add(r);
            rs.Add(80f);
            rs = rs.Where(r => r > 0.2f).Select(r => Mathf.Round(r * 1000f) / 1000f).Distinct().OrderBy(r => r).ToList();
            const int seg = 288;
            var v = new List<Vector3> { new Vector3(0, PlatY, 0) };
            var tri = new List<int>();
            for (int j = 0; j < rs.Count; j++)
                for (int s = 0; s < seg; s++)
                {
                    // the mesa rim: the last ring inside is pulled out to the rim itself (a smooth, not ring-stepped, edge) and
                    // every ring past it drops down the cliff just outside the rim
                    float a = s * Mathf.PI * 2f / seg, r = rs[j], er = EdgeR(a) - 2.0f;   // the dirt ends under the rim rocks (they reach 3 m in)
                    bool nextOut = j + 1 < rs.Count && rs[j + 1] > er;
                    if (r > er) v.Add(new Vector3(Mathf.Sin(a) * (er + 0.4f), -60f, Mathf.Cos(a) * (er + 0.4f)));
                    else if (nextOut) v.Add(new Vector3(Mathf.Sin(a) * er, Height(er), Mathf.Cos(a) * er));
                    else v.Add(new Vector3(Mathf.Sin(a) * r, Height(r), Mathf.Cos(a) * r));
                }
            for (int s = 0; s < seg; s++) tri.AddRange(new[] { 0, 1 + s, 1 + (s + 1) % seg });
            for (int j = 0; j < rs.Count - 1; j++)
                for (int s = 0; s < seg; s++)
                {
                    int a = 1 + j * seg + s, b = 1 + j * seg + (s + 1) % seg, c = a + seg, d = b + seg;
                    tri.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "BS_FloorMesh", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            {   // flat dirt stays straight up (the cliff drop bent the rim normals: lit triangle bands on the ground)
                var nn = mesh.normals;
                for (int k = 0; k < v.Count; k++)
                    if (v[k].y > -0.05f && new Vector2(v[k].x, v[k].z).magnitude > Rad[6] + 0.3f) nn[k] = Vector3.up;
                mesh.normals = nn;
            }
            var mp = Gen + "/BS_FloorMesh.asset";
            AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var g = new GameObject("Ground");
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["BS_Ground"];
            mr.shadowCastingMode = ShadowCastingMode.Off;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            Log($"floor: {rs.Count} rings x {seg}, {v.Count} verts, {tri.Count / 3} tris");
        }

        // ------------------------------------------------------------------ blade rays
        // The model's long axis, which end is the tip (the narrow end) and its flat (wide) side.
        struct Shape { public Vector3 axis, wide, center; public float length, width, minAlong, guardFromHilt; public bool tipAtMax; }
        static Shape Measure(GameObject prefab)
        {
            var tmp = Object.Instantiate(prefab); tmp.transform.position = Vector3.zero;
            var pts = new List<Vector3>();
            foreach (var mf in tmp.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh) foreach (var p in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(p));
            Object.DestroyImmediate(tmp);
            var b = new Bounds(pts[0], Vector3.zero); foreach (var p in pts) b.Encapsulate(p);
            var ext = b.size;
            int li = ext.x >= ext.y && ext.x >= ext.z ? 0 : ext.y >= ext.z ? 1 : 2;
            var axis = Vector3.zero; axis[li] = 1;
            int wi = -1; float best = -1;
            for (int k = 0; k < 3; k++) if (k != li && ext[k] > best) { best = ext[k]; wi = k; }
            var wide = Vector3.zero; wide[wi] = 1;
            float lo = b.min[li], len = ext[li];
            // lateral spread near each end: the guard/hilt end is wide, the tip end narrow
            float spreadLo = 0, spreadHi = 0;
            var bins = new float[40];
            foreach (var p in pts)
            {
                float t = (p[li] - lo) / len;
                float lat = Mathf.Abs(p[wi] - b.center[wi]);
                if (t < 0.12f) spreadLo = Mathf.Max(spreadLo, lat);
                if (t > 0.88f) spreadHi = Mathf.Max(spreadHi, lat);
                int bi = Mathf.Clamp((int)(t * 40), 0, 39); bins[bi] = Mathf.Max(bins[bi], lat);
            }
            bool tipAtMax = spreadHi < spreadLo;
            int gb = 0; for (int k = 1; k < 40; k++) if (bins[k] > bins[gb]) gb = k;   // the cross guard = widest slice
            float gt = (gb + 0.5f) / 40f;
            return new Shape { axis = axis, wide = wide, center = b.center, length = len, width = best, minAlong = lo, tipAtMax = tipAtMax, guardFromHilt = tipAtMax ? gt : 1 - gt };
        }

        // The estoc's blade only: the downloaded mesh cut just past its guard (the swept hilt stood ~0.5 m proud of the rim
        // once scaled). Vertices go to the model's root space, re-oriented so +Z runs from the blade root to the tip, +X across
        // the flat, origin at the blade root on the centre line.
        static Mesh BladeMesh(GameObject prefab, Shape s, out float bladeLen)
        {
            var tmp = Object.Instantiate(prefab); tmp.transform.position = Vector3.zero;
            var fwd = s.tipAtMax ? s.axis : -s.axis;
            var q = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.Cross(s.wide, fwd)));
            float gAbs = s.tipAtMax ? s.minAlong + s.guardFromHilt * s.length : s.minAlong + (1 - s.guardFromHilt) * s.length;
            float cut = Vector3.Dot(fwd, s.axis * gAbs) + 0.035f * s.length;     // a little past the guard
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tan = new List<Vector4>(); var tri = new List<int>();
            foreach (var mf in tmp.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m) continue;
                var mv = m.vertices; var mn = m.normals; var mu = m.uv; var mt = m.tangents; var mi = m.triangles;
                int b0 = v.Count;
                for (int k = 0; k < mv.Length; k++)
                {
                    var p = mf.transform.TransformPoint(mv[k]);
                    v.Add(q * (p - s.center));
                    nrm.Add(q * mf.transform.TransformDirection(mn.Length > k ? mn[k] : Vector3.up));
                    uv.Add(mu.Length > k ? mu[k] : Vector2.zero);
                    if (mt.Length > k) { var t3 = q * mf.transform.TransformDirection(mt[k]); tan.Add(new Vector4(t3.x, t3.y, t3.z, mt[k].w)); } else tan.Add(new Vector4(1, 0, 0, 1));
                }
                float cz = cut - Vector3.Dot(fwd, s.center);
                for (int k = 0; k < mi.Length; k += 3)
                    if (v[b0 + mi[k]].z > cz && v[b0 + mi[k + 1]].z > cz && v[b0 + mi[k + 2]].z > cz)
                        tri.AddRange(new[] { b0 + mi[k], b0 + mi[k + 1], b0 + mi[k + 2] });
            }
            Object.DestroyImmediate(tmp);
            // keep only the blade's own vertices (the cut-away hilt verts made the bounds ~1 m tall and are never drawn)
            var remap = new Dictionary<int, int>();
            var cv = new List<Vector3>(); var cn = new List<Vector3>(); var cu = new List<Vector2>(); var ct = new List<Vector4>();
            for (int k = 0; k < tri.Count; k++)
            {
                if (!remap.TryGetValue(tri[k], out int ni))
                {
                    ni = cv.Count; remap[tri[k]] = ni;
                    cv.Add(v[tri[k]]); cn.Add(nrm[tri[k]]); cu.Add(uv[tri[k]]); ct.Add(tan[tri[k]]);
                }
                tri[k] = ni;
            }
            v = cv; nrm = cn; uv = cu; tan = ct;
            // re-base: origin at the blade root on the centre line, flat side down at y = 0 (so a ray can lie on the dirt)
            float zMin = float.MaxValue, zMax = float.MinValue, yMin = float.MaxValue, xMid = 0;
            float xLo = float.MaxValue, xHi = float.MinValue;
            foreach (var p in v) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); yMin = Mathf.Min(yMin, p.y); xLo = Mathf.Min(xLo, p.x); xHi = Mathf.Max(xHi, p.x); }
            xMid = (xLo + xHi) * 0.5f;
            for (int k = 0; k < v.Count; k++) v[k] = new Vector3(v[k].x - xMid, v[k].y - yMin, v[k].z - zMin);
            bladeLen = zMax - zMin;
            var mesh = new Mesh { name = "BS_EstocBlade", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetUVs(0, uv); mesh.SetTangents(tan); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            var path = Gen + "/BS_EstocBlade.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
            Log($"blade mesh: {tri.Count / 3} tris, length {bladeLen:0.000} (model units), width {mesh.bounds.size.x:0.000}, thick {mesh.bounds.size.y:0.000}");
            return mesh;
        }

        static void BuildBlades()
        {
            var g = Group("BladeRays");
            var est = AssetDatabase.LoadAssetAtPath<GameObject>(EstocFbx);
            if (!est) { Log("MISSING blade model"); return; }
            var sE = Measure(est);
            var blade = BladeMesh(est, sE, out float bladeLen);
            var mat = mats["BS_Blade"];
            // 11-15 redo (Kido, ref: dark thick metal wings in bundles, not even bright sticks): six bundles fanning out from
            // under the dais foot, 3-5 overlapping blades each, longest in the middle; laid flat in layers (each blade a little
            // above the one under it, outer ones lowest), tips ending 8+ m inside the mesa rim
            int bundles = 6, made = 0;
            for (int bI = 0; bI < bundles; bI++)
            {
                float baseAng = 30f + bI * 360f / bundles + R(-9f, 9f);
                int cnt = 3 + rng.Next(3);
                for (int k = 0; k < cnt; k++)
                {
                    float c = cnt == 1 ? 0 : (k / (float)(cnt - 1)) * 2f - 1f;          // -1 .. 1 across the fan
                    float ang = baseAng + c * R(9f, 13f) + R(-1.5f, 1.5f);
                    float len = Mathf.Lerp(R(11f, 14.5f), R(5.5f, 8f), Mathf.Abs(c));
                    float s = len / bladeLen;
                    float layer = (1 - Mathf.Abs(c)) * 0.2f;
                    float rootR = Rad[6] - R(0.6f, 1.1f);                               // under the dais foot ring
                    var t = new GameObject("BladeRay").transform;
                    t.SetParent(g, false);
                    var dir = Quaternion.Euler(0, ang, 0);
                    t.position = dir * new Vector3(0, 0, rootR) + Vector3.up * (0.015f + layer);
                    t.rotation = dir * Quaternion.Euler(0, 0, c * R(4f, 8f));         // banked a little across, like fanned wings
                    t.localScale = new Vector3(s * R(3.4f, 4.4f), s * 0.55f, s);
                    t.gameObject.AddComponent<MeshFilter>().sharedMesh = blade;
                    var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On;
                    made++;
                }
            }
            Log($"blades: {made} in {bundles} bundles");
        }

        // ------------------------------------------------------------------ edge structures (outside the arena only)
        static readonly Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();
        static Bounds ModelBounds(string path, GameObject go)
        {
            if (bounds.TryGetValue(path, out var b)) return b;
            var tmp = Object.Instantiate(go); tmp.transform.position = Vector3.zero;
            var rs = tmp.GetComponentsInChildren<Renderer>();
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(tmp);
            bounds[path] = b; return b;
        }
        static Transform Place(string path, Material mat, Vector3 pos, float yaw, float height, Transform parent, float tilt = 0f)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!go) { Log("MISSING model " + path); return null; }
            var b = ModelBounds(path, go);
            var holder = new GameObject(go.name).transform;
            holder.SetParent(parent, false);
            holder.position = pos; holder.rotation = Quaternion.Euler(tilt, yaw, 0); holder.localScale = Vector3.one * (height / Mathf.Max(0.01f, b.size.y));
            var inst = Object.Instantiate(go, holder);
            inst.transform.localRotation = go.transform.localRotation; inst.transform.localScale = go.transform.localScale;
            inst.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var arr = new Material[r.sharedMaterials.Length]; for (int i = 0; i < arr.Length; i++) arr[i] = mat; r.sharedMaterials = arr;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            return holder;
        }

        // like Place, but sized per axis (the cliff scans are wide slabs: a uniform height scale made them cover the arena)
        static Transform PlaceSized(string path, Material mat, Vector3 pos, float yaw, Vector3 size, Transform parent, float tilt = 0f)
        {
            var t = Place(path, mat, pos, yaw, size.y, parent, tilt);
            if (!t) return null;
            var b = ModelBounds(path, AssetDatabase.LoadAssetAtPath<GameObject>(path));
            t.localScale = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
            return t;
        }

        static Material pSmoke, pEmber;
        static void FxMats()
        {
            if (pSmoke) return;
            var sh = Shader.Find("Underworld/Particle");
            var smokeTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/smoke_04.png");
            var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/spark_01.png");
            pSmoke = new Material(sh) { name = "BS_AbyssSmoke" }; pSmoke.SetTexture("_MainTex", smokeTex); pSmoke.SetColor("_Tint", Color.white);
            pSmoke.SetFloat("_AlphaFromRed", 1f); pSmoke.SetFloat("_Src", (float)BlendMode.SrcAlpha); pSmoke.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); pSmoke.SetFloat("_SoftFade", 4f);
            pEmber = new Material(sh) { name = "BS_AbyssEmber" }; pEmber.SetTexture("_MainTex", sparkTex); pEmber.SetColor("_Tint", new Color(3f, 0.9f, 0.25f, 1f));
            pEmber.SetFloat("_Src", (float)BlendMode.One); pEmber.SetFloat("_Dst", (float)BlendMode.One); pEmber.SetFloat("_SoftFade", 0.5f);
            foreach (var m in new[] { pSmoke, pEmber })
            {
                var path = $"{MatDir}/{m.name}.mat"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
            }
        }
        // slow rolling smoke column (alpha blended Kenney CC0 puff), prewarmed
        static void Smoke(Transform parent, Vector3 pos, float radius, float size, float rise, Color tint)
        {
            FxMats();
            var go = new GameObject("AbyssSmoke"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = pSmoke; r.shadowCastingMode = ShadowCastingMode.Off; r.maxParticleSize = 3f;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true; mm.playOnAwake = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f); mm.startSpeed = 0f; mm.maxParticles = 22;
            mm.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f); mm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            mm.startColor = tint;
            var em = ps.emission; em.rateOverTime = 1.3f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f); v.y = new ParticleSystem.MinMaxCurve(rise * 0.6f, rise); v.z = new ParticleSystem.MinMaxCurve(-0.6f, -0.1f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.4f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.6f), 0), new GradientColorKey(Color.white, 0.4f), new GradientColorKey(new Color(0.6f, 0.55f, 0.55f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            ps.Play();
        }
        static void Embers(Transform parent, Vector3 pos)
        {
            FxMats();
            var go = new GameObject("AbyssEmbers"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = pEmber; r.shadowCastingMode = ShadowCastingMode.Off;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true; mm.playOnAwake = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f); mm.startSpeed = 0f; mm.maxParticles = 30;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            var em = ps.emission; em.rateOverTime = 4f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(18f, 6f, 10f);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); v.y = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); v.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.2f);
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            ps.Play();
        }

        static Vector3 OutsideArena(Vector3 p, float m)
        {
            if (Mathf.Abs(p.x) < HX + m && Mathf.Abs(p.z) < HZ + m)
            {
                float dx = HX + m - Mathf.Abs(p.x), dz = HZ + m - Mathf.Abs(p.z);
                if (dx < dz) p.x = Mathf.Sign(p.x) * (HX + m); else p.z = Mathf.Sign(p.z) * (HZ + m);
            }
            return p;
        }

        static bool Overlaps(Transform t)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (b.max.x > -HX - 0.5f && b.min.x < HX + 0.5f && b.max.z > -HZ - 0.5f && b.min.z < HZ + 0.5f) return true;
            }
            return false;
        }

        static void BuildEdge()
        {
            var g = Group("EdgeStructures");
            int n = 0;
            // ---- the cliff: granite cliff slabs along the north arc only (where the rim is inside the floor), tops just
            // under the floor edge, falling ~40 m; volcanic spires standing up out of the deep
            var cliffs = Group("MesaCliffs", g);
            for (int i = 0; i < 90; i++)
            {
                float a = (i + R(-0.2f, 0.2f)) * Mathf.PI * 2f / 90;
                float er = EdgeR(a);
                if (er > 70f) continue;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                // 11-15 (Kido: "종이로 오린 것처럼 엣지"): the rock now rides OVER the floor's edge: each slab reaches ~3 m in
                // over the dirt and stands 0.3-0.8 m proud of it, so the rim is thick broken rock, never the floor's cut edge
                float depth = R(6f, 7.5f);
                var p = dir * (er + depth * 0.5f - 3.0f) + Vector3.up * AbyssY;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + R(-15f, 15f);
                PlaceSized(CliffFbx, mats["BS_Cliff"], p, yaw, new Vector3(R(7f, 10f), -AbyssY + R(0.3f, 0.8f), depth), cliffs, R(-4f, 4f)); n++;
                // gravel and stones heaped where the dirt meets the rock (hides the seam)
                for (int k = 0; k < 3; k++)
                {
                    var q = dir * (er - 3.0f - R(0.2f, 1.6f)) + new Vector3(dir.z, 0, -dir.x) * R(-4f, 4f) + Vector3.down * 0.02f;
                    var tq = Place(RocksFbx, mats["BS_Rocks"], q, R(0, 360), R(0.25f, 0.5f), cliffs);
                    if (tq) { float sc = R(3f, 5f); tq.localScale = new Vector3(tq.localScale.x * sc, tq.localScale.y, tq.localScale.z * sc); }
                }
            }
            for (int i = 0; i < 6; i++)
            {
                float a = (R(-55f, 55f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var p = dir * (EdgeR(a) + R(12f, 30f)) + Vector3.up * AbyssY;
                Place(SpireFbx, mats["BS_Spire"], p, R(0, 360), R(18f, 30f), cliffs); n++;
            }
            // lava far below: the abyss plane (dim molten veins) and two warm lights welling up the cliff face
            var abyss = GameObject.CreatePrimitive(PrimitiveType.Plane);
            abyss.name = "Abyss"; abyss.transform.SetParent(g, false);
            Object.DestroyImmediate(abyss.GetComponent<Collider>());
            abyss.transform.position = new Vector3(0, AbyssY + 0.5f, HZ + 40f); abyss.transform.localScale = new Vector3(30f, 1f, 12f);
            var ar = abyss.GetComponent<MeshRenderer>(); ar.sharedMaterial = mats["BS_Abyss"]; ar.shadowCastingMode = ShadowCastingMode.Off;
            AddLight("AbyssGlow_NW", new Vector3(-22f, -10f, HZ + 12f), Hex("#ff5a1e"), 6f, 30f);
            AddLight("AbyssGlow_NE", new Vector3(24f, -10f, HZ + 12f), Hex("#ff5a1e"), 6f, 30f);

            // ---- smoke / sulphur steam rising out of the deep along the cliff, in three depth layers, and embers
            var fx = Group("AbyssFX", g);
            for (int i = 0; i < 9; i++)
            {
                float x = -48f + i * 12f + R(-3f, 3f);
                float a = Mathf.Atan2(x, HZ + 6f);
                float er = EdgeR(a);
                int layer = i % 3;
                var pos = new Vector3(x, AbyssY + 8f + layer * 9f, Mathf.Cos(a) * er + 4f + layer * 7f);
                Smoke(fx, pos, 9f + layer * 3f, R(12f, 20f), 1.6f + layer * 0.4f, new Color(0.42f, 0.22f, 0.14f, 0.55f - layer * 0.1f));
            }
            for (int i = 0; i < 5; i++)
            {
                float x = -36f + i * 18f + R(-4f, 4f);
                float a = Mathf.Atan2(x, HZ + 6f);
                var pos = new Vector3(x, -3f, Mathf.Cos(a) * EdgeR(a) + 2.5f);
                Smoke(fx, pos, 6f, R(6f, 9f), 0.9f, new Color(0.38f, 0.24f, 0.17f, 0.3f));   // wisps curling over the lip
            }
            for (int i = 0; i < 4; i++)
                Embers(fx, new Vector3(-36f + i * 24f + R(-4f, 4f), -14f, HZ + 12f + R(0f, 6f)));

            // ---- four thin, low pillars on the north rim only
            float[] rimAng = { -48f, -17f, 14f, 44f };
            foreach (var deg in rimAng)
            {
                float a = (deg + R(-4f, 4f)) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var p = OutsideArena(dir * (EdgeR(a) - 3.8f) + Vector3.down * 0.3f, 0.6f);   // on the dirt just inside the rock lip
                var t = Place(PillarFbx, mats["BS_Pillar"], p, R(0, 360), R(3f, 5f), g, R(-3f, 3f));
                if (t) { t.localScale = new Vector3(t.localScale.x * 0.6f, t.localScale.y, t.localScale.z * 0.6f); n++; }
            }

            // ---- debris collar: Megascans debris / small rocks lying flat round the dais foot, sparser outward
            var deb = Group("Debris", g);
            for (int i = 0; i < 80; i++)
            {
                float a = R(0, Mathf.PI * 2f);
                float d = Rad[6] + 0.4f + Mathf.Pow(R(0, 1), 1.6f) * 18f;
                var p = new Vector3(Mathf.Sin(a) * d, -0.02f, Mathf.Cos(a) * d);
                bool rock = rng.NextDouble() < 0.4;
                var t = Place(rock ? RocksFbx : DebrisFbx, mats[rock ? "BS_Rocks" : "BS_Debris"], p, R(0, 360), rock ? R(0.15f, 0.3f) : R(0.1f, 0.22f), deb);
                if (t) { float sc = R(2.2f, 3.6f); t.localScale = new Vector3(t.localScale.x * sc, t.localScale.y, t.localScale.z * sc); n++; }
            }
            Log($"edge pieces {n}");
        }

        // ------------------------------------------------------------------ light, fog, post, camera
        static Light AddLight(string name, Vector3 pos, Color c, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(root, false); l.transform.position = pos;
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            lightCount++;
            return l;
        }

        static void BuildLightAndPost()
        {
            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.transform.SetParent(root, false);
            moon.type = LightType.Directional; moon.color = Hex("#dc9a72"); moon.intensity = 1.5f;   // ref: warm orange-brown ground, readable
            moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.75f; moon.lightmapBakeType = LightmapBakeType.Realtime;
            moon.transform.rotation = Quaternion.Euler(42f, 200f, 0f);   // from the north-east behind the arena: blade edges catch it
            lightCount++;
            RenderSettings.sun = moon;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#5a3a2a");
            RenderSettings.ambientEquatorColor = Hex("#3e281a");
            RenderSettings.ambientGroundColor = Hex("#1c100c");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#2a1a12");   // brown, not red
            RenderSettings.fogDensity = 0.006f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.25f;
            LightingSettings ls = null;
            try { ls = Lightmapping.lightingSettings; } catch { }
            if (ls == null)
            {
                var lsPath = Root + "/BS_Lighting.lighting";
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
                if (!ls) { ls = new LightingSettings { name = "BS_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = false; ls.realtimeGI = false;

            // the seal's own glow (high, wide: lights the rings and the blade roots, no pool at anyone's feet)
            // the dais's warm pool of light (ref: warm centre, darker outward); the deep's glow comes from BuildEdge
            AddLight("SealGlow", new Vector3(0, 6f, 0), Hex("#ff8a3a"), 2.4f, 30f);
            AddLight("WarmFill_S", new Vector3(0, 5f, -18f), Hex("#ff9a55"), 0.8f, 24f);

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
            vol.sharedProfile = Profile();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#140806");
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true;
            cam.gameObject.AddComponent<AudioListener>();
            var off = new Vector3(0, 17f, -14.5f) * 1.6f;
            cam.transform.position = off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }

        static VolumeProfile Profile()
        {
            var path = Root + "/BS_PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.3f); ca.contrast.Override(10f); ca.saturation.Override(-8f); ca.colorFilter.Override(Hex("#F2E6E2"));
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.1f); bl.intensity.Override(0.75f); bl.scatter.Override(0.6f); bl.highQualityFiltering.Override(false);
            var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#0a0202")); vg.intensity.Override(0.4f); vg.smoothness.Override(0.45f);
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
            float tallest = 0;
            float lowest = 9f; foreach (var r in root.Find("BladeRays").GetComponentsInChildren<Renderer>()) { tallest = Mathf.Max(tallest, r.bounds.max.y); lowest = Mathf.Min(lowest, r.bounds.min.y); }
            Log($"blade rays top y {tallest:0.000}, bottom y {lowest:0.000} (dirt 0, platform {PlatY})");
            foreach (var r in root.Find("EdgeStructures").GetComponentsInChildren<Renderer>())
            {
                var b = r.bounds;
                if (b.max.x > -HX && b.min.x < HX && b.max.z > -HZ && b.min.z < HZ) Log($"NOTE edge piece overlaps the arena: {r.transform.parent?.parent?.name} {b.center}");
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
            rooms.InsertArrayElementAtIndex(at - 1);
            var r = rooms.GetArrayElementAtIndex(at);
            r.FindPropertyRelative("id").stringValue = RoomId;
            r.FindPropertyRelative("displayName").stringValue = "봉인의 제단";
            r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(10, 0);
            r.FindPropertyRelative("scenePath").stringValue = ScenePath;
            r.FindPropertyRelative("groundMeshName").stringValue = "";
            r.FindPropertyRelative("center").vector3Value = new Vector3(0, PlatY, 0);
            r.FindPropertyRelative("size").vector2Value = size;
            r.FindPropertyRelative("fitToStage").boolValue = false;
            r.FindPropertyRelative("floorColor").colorValue = Hex("#2a1612");
            r.FindPropertyRelative("pillarCount").intValue = 0;
            r.FindPropertyRelative("doors").arraySize = 0;
            r.FindPropertyRelative("waves").arraySize = 0;
            r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
            r.FindPropertyRelative("devOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Log("room 'bossseal' added to RoomRunConfig");
        }
    }
}
