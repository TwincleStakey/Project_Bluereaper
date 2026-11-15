// Builds Assets/Game/BossSeal/BossSeal.unity: a boss arena after Kido's reference (Diablo IV Vessel of Hatred boss
// platform), about twice its size, with a Korean shamanic seal instead of its sigil (11-15).
//   - one flat floor mesh: a 30.8 m round sealing platform raised 0.3 m (gentle 0.8 m chamfer, ring lips of 3-4 cm:
//     nothing to step over), then reddish-brown dirt out to 75 m. Zones/inlays are drawn by BossSeal/Ground
//     (BSGround.shader) from downloaded images only: bagua octagon (CC0) + Korean taegeuk (PD flag) in the centre,
//     the Joseon 길상호신부 cinnabar talisman (CC BY 4.0) and the 삼두매 부적 print (PD) round the talisman ring, the
//     부적목판 woodblock characters round the outer ring; Poly Haven CC0 stone/sandstone/rusty metal/mud scans.
//   - 24 radial blade rays: Poly Haven CC0 antique_estoc / ornate_medieval_dagger scaled up (giant swords), lying flat
//     on the dirt (level, 1.5 cm up), roots under the platform rim; no colliders.
//   - edge only: Megascans stone pillars and wall pieces (DarkCrypt sources) 32-44 m out; the north ones and every
//     top fade into red translucent haze (BossSeal/HazeProp), the floor itself melts into red haze to the north.
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
        
        // floor radii (must match the BSGround material vectors)
        static readonly float[] Rad = { 6.3f, 6.9f, 11.1f, 11.6f, 14.2f, 14.8f, 15.6f };
        const float PlatY = 0.3f;

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
            rng = new System.Random(7721);
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
                m.SetTexture("_MudA", Tex(PH("red_mud_stones", "diff"))); m.SetTexture("_MudAN", Tex(PH("red_mud_stones", "nor_gl"), normal: true));
                m.SetTexture("_MudB", Tex(PH("cracked_red_ground", "diff"))); m.SetTexture("_MudBN", Tex(PH("cracked_red_ground", "nor_gl"), normal: true));
                m.SetTexture("_Stone", Tex(PH("dark_rock_02", "diff"))); m.SetTexture("_StoneN", Tex(PH("dark_rock_02", "nor_gl"), normal: true));
                m.SetTexture("_StoneAO", Tex(PH("dark_rock_02", "ao"), linear: true));
                m.SetTexture("_Trim", Tex(PH("red_sandstone_wall", "diff"))); m.SetTexture("_TrimN", Tex(PH("red_sandstone_wall", "nor_gl"), normal: true));
                m.SetTexture("_Metal", Tex(PH("rusty_metal_02", "diff"))); m.SetTexture("_MetalN", Tex(PH("rusty_metal_02", "nor_gl"), normal: true));
                m.SetTexture("_Seal", Mask("BS_Seal_TaegeukPalgwae.png"));
                m.SetTexture("_Bujeok", Mask("BS_Bujeok_Gilsang.png", 1024));
                m.SetTexture("_Hawk", Mask("BS_Samdumae.png", 1024));
                m.SetTexture("_Text", Mask("BS_Woodblock_Text.png", 1024));
                m.SetVector("_R1", new Vector4(Rad[0], Rad[1], Rad[2], Rad[3]));
                m.SetVector("_R2", new Vector4(Rad[4], Rad[5], Rad[6], 11.6f));
                m.SetVector("_Tiles", new Vector4(12, 28, 3.0f, 1.75f));
            });
            Mat("BS_Blade", "Universal Render Pipeline/Lit", m =>
            {
                const string d = Src + "PH_antique_estoc/textures/";
                m.SetTexture("_BaseMap", Tex(d + "antique_estoc_diff_2k.jpg")); m.SetColor("_BaseColor", Hex("#9c7e74"));
                var n = Tex(d + "antique_estoc_nor_gl_2k.exr", normal: true);
                m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); if (n) m.EnableKeyword("_NORMALMAP");
                m.SetFloat("_Metallic", 0.6f); m.SetFloat("_Smoothness", 0.32f);
                m.enableInstancing = true;
            });
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
            float h = PlatY;
            h += 0.04f * (S(Rad[0] - 0.03f, Rad[0] + 0.03f, r) - S(Rad[1] - 0.03f, Rad[1] + 0.03f, r));   // metal band 1 lip
            h -= 0.01f * (S(Rad[1] - 0.03f, Rad[1] + 0.03f, r) - S(Rad[2] - 0.03f, Rad[2] + 0.03f, r));   // talisman ring sits a touch lower
            h += 0.03f * (S(Rad[2] - 0.03f, Rad[2] + 0.03f, r) - S(Rad[3] - 0.03f, Rad[3] + 0.03f, r));   // metal band 2 lip
            h -= 0.03f * S(Rad[3] - 0.03f, Rad[3] + 0.03f, r);                                              // text ring lower
            h += 0.04f * (S(Rad[4] - 0.03f, Rad[4] + 0.03f, r) - S(Rad[5] - 0.03f, Rad[5] + 0.03f, r));   // metal rim
            if (r > Rad[5]) h = Mathf.Lerp(h, 0f, S(Rad[5], Rad[6], r));                                   // chamfer down
            return h;
        }

        static void BuildFloor()
        {
            var rs = new List<float>();
            for (float r = 0.4f; r < Rad[0] - 0.1f; r += 0.5f) rs.Add(r);
            foreach (var b in Rad)
                for (int k = -3; k <= 3; k++) rs.Add(b + k * 0.03f);
            for (float r = Rad[0]; r < Rad[6]; r += 0.35f) rs.Add(r);
            for (float r = Rad[6] + 0.4f; r < 75f; r *= 1.07f) rs.Add(r);
            rs.Add(75f);
            rs = rs.Where(r => r > 0.2f).Select(r => Mathf.Round(r * 1000f) / 1000f).Distinct().OrderBy(r => r).ToList();
            const int seg = 288;
            var v = new List<Vector3> { new Vector3(0, PlatY, 0) };
            var tri = new List<int>();
            foreach (var r in rs)
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Sin(a) * r, Height(r), Mathf.Cos(a) * r));
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
            const int n = 24;
            for (int i = 0; i < n; i++)
            {
                bool longRay = i % 2 == 0 || i % 6 == 3;
                float ang = (i + 0.5f) * 360f / n + R(-3.5f, 3.5f);
                float len = longRay ? R(13f, 17f) : R(7f, 10f);              // visible blade length (m)
                if (i % 4 == 2) len *= 0.8f;
                float s = len / bladeLen;
                // 11-15 (Kido): no tips stuck in the dirt. Every ray lies flat on the dirt (level, no pitch/roll), its flat
                // side 1.5 cm above the ground (no z-fight, nothing cut by the floor), the root tucked under the metal rim:
                // the floor there is 0.3 m up, so the blade slides out from under the chamfer.
                float rootR = Rad[4] + R(0.05f, 0.3f);                        // under the metal rim trim (14.2-14.8)
                R(0, 1); R(0, 1);                                             // (the old pitch/roll draws: keeps the edge layout unchanged)
                var t = new GameObject("BladeRay").transform;
                t.SetParent(g, false);
                var dir = Quaternion.Euler(0, ang, 0);
                t.position = dir * new Vector3(0, 0, rootR) + Vector3.up * 0.015f;
                t.rotation = dir;
                t.localScale = new Vector3(s * (longRay ? 3.2f : 2.7f), s * 0.32f, s);   // flattened: rays stay low (arena rule)
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = blade;
                var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On;
            }
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
            // a broken colonnade on an ellipse just outside the arena; tall at the back, short and sparse at the camera side
            for (int i = 0; i < 26; i++)
            {
                float a = (i + R(-0.25f, 0.25f)) * Mathf.PI * 2f / 26f;
                var p = new Vector3(Mathf.Sin(a) * (HX + R(5f, 9f)), -0.4f, Mathf.Cos(a) * (HZ + R(5f, 9f)));
                p = OutsideArena(p, 3.5f);
                bool south = p.z < -HZ * 0.6f;
                if (south && rng.NextDouble() < 0.5) continue;
                float h = south ? R(2.5f, 5f) : p.z > 0 ? R(7f, 13f) : R(4f, 9f);
                Place(PillarFbx, mats["BS_Pillar"], p, R(0, 360), h, g, R(-4f, 4f)); n++;
                if (!south && rng.NextDouble() < 0.45)
                {
                    int courses = rng.Next(2, 5);
                    float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg + 90f + R(-8, 8);
                    for (int k = 0; k < courses; k++)
                        {
                            var wt = Place(WallFbx, mats["BS_Wall"], p + Quaternion.Euler(0, yaw, 0) * new Vector3(R(2.5f, 4f), 0, 0) + Vector3.up * (k * 1.4f - 0.3f), yaw + R(-3, 3), 1.5f, g);
                            if (wt && Overlaps(wt)) { Object.DestroyImmediate(wt.gameObject); n--; }
                        }
                    n += courses;
                }
            }
            // a second, taller row further back (north and sides) dissolving into the red
            for (int i = 0; i < 16; i++)
            {
                float a = (i / 15f - 0.5f) * Mathf.PI * 1.25f;
                var p = new Vector3(Mathf.Sin(a) * (HX + R(14f, 20f)), -1f, Mathf.Cos(a) * (HZ + R(12f, 20f)));
                Place(PillarFbx, mats["BS_Pillar"], p, R(0, 360), R(10f, 17f), g); n++;
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
            RenderSettings.ambientSkyColor = Hex("#5a3428");
            RenderSettings.ambientEquatorColor = Hex("#3e2219");
            RenderSettings.ambientGroundColor = Hex("#1c100c");
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("#3a140c");
            RenderSettings.fogDensity = 0.009f;
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
            AddLight("SealGlow", new Vector3(0, 7f, 0), Hex("#ff6a2a"), 1.3f, 21f);
            // red light leaking from the haze behind the colonnade
            AddLight("RimRed_N", new Vector3(0, 3f, HZ + 9f), Hex("#ff2a12"), 4f, 22f);
            AddLight("RimRed_NE", new Vector3(HX + 4f, 3f, HZ - 2f), Hex("#ff3a1a"), 3f, 18f);
            AddLight("RimRed_NW", new Vector3(-HX - 4f, 3f, HZ - 2f), Hex("#ff3a1a"), 3f, 18f);
            AddLight("RimRed_S", new Vector3(R(-6, 6), 2.5f, -HZ - 8f), Hex("#d8301a"), 2f, 16f);

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
            vol.sharedProfile = Profile();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#1a0604");
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
