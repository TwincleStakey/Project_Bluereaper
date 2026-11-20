// Builds Assets/Game/KillKnight/KillKnight.unity, "심연 고원" (11-19): a big dark slate plateau floating over a glowing
// abyss, after the KILL KNIGHT trailer frame Kido sent, about 4x its area.
//   - plateau: 90 x 90 m bounding box, angular outline with stepped rectangular notches (not a circle), flat walkable top
//     at y 0 (KillKnight/Floor: Poly Haven CC0 rock_01 + dark_rock + cracked_concrete scans), sheer cliff faces
//     straight down into the glow (KillKnight/AbyssRock on dark_rock)
//   - abyss: Poly Haven CC0 cliff/rock/dead-tree models as rock masses hugging the walls, spires at many depths and
//     dead roots at the sides, all lit from below by the shader (red-orange underglow + depth haze); a glowing floor far
//     below and three crimson haze sheets in between; a few ember streams
//   - lights: one cool key (soft shadows); every red is emissive (no extra realtime lights)
//   - invisible edge walls (layer WallLayer) along every edge, nothing on the walkable top
//   - registers the RoomRun dev room "killknight"
// Run via rr_queue: "call KillKnight.KKBuilder.Build". Shots: "call KillKnight.KKBuilder.Shots" (edit mode, ArtSource/KillKnight/kk_shots.txt).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KillKnight
{
    public static class KKBuilder
    {
        public const string Root = "Assets/Game/KillKnight";
        public const string ScenePath = Root + "/KillKnight.unity";
        const string Src = Root + "/Source/";
        const string MatDir = Root + "/Materials";
        const string Gen = Root + "/Generated";
        public const string RoomId = "killknight";
        const float Half = 45f;
        const float SkirtBottom = -345f, AbyssFloorY = -320f;   // v3 (Kido: "아래쪽이 훨씬 높아 보이는데"): ~3x deeper
        const int WallLayer = 1;   // RoomRun Ground.WallLayer

        // the plateau outline (x, z), metres; integer corners so the 1 m floor grid fits it exactly
        static readonly Vector2[] Outline = KKOutline.Points;   // shared with KKSpawnGuard

        const string SpireFbx = "Assets/Underworld/Resources/LavaRocks/LR_Spire.fbx";
        const string SpireTex = "Assets/Underworld/Resources/LavaRocks/T_LR_Spire_";
        static string PHM(string id) => $"{Src}PH_{id}/{id}_1k.fbx";
        static string PHMT(string id, string k) => $"{Src}PH_{id}/textures/{id}_{k}_1k.jpg";

        static System.Random rng;
        static Transform root;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static int lightCount;

        static string LogPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "KillKnight", "kk_log.txt");
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
            rng = new System.Random(4417);
            mats.Clear(); lightCount = 0; bounds.Clear();
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();
            BuildMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("KillKnight").transform;
            BuildFloor();
            BuildCliff();
            BuildAbyss();
            BuildLightAndPost();
            BuildEdgeColliders();
            root.gameObject.AddComponent<KKEdgeWalk>();
            root.gameObject.AddComponent<KKIsoView>();
            root.gameObject.AddComponent<KKSpawnGuard>();
            int blockers = CheckArena();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddRoomRunRoom();
            Log($"BUILT {ScenePath}: lights={lightCount} collidersOnTop={blockers} area={Area():0} m2");
        }

        // ------------------------------------------------------------------ outline helpers
        static bool Inside(Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = Outline.Length - 1; i < Outline.Length; j = i++)
            {
                var a = Outline[i]; var b = Outline[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) c = !c;
            }
            return c;
        }
        static float EdgeDist(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length];
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }
        static float Area()
        {
            float s = 0; for (int i = 0; i < Outline.Length; i++) { var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length]; s += a.x * b.y - b.x * a.y; }
            return Mathf.Abs(s) * 0.5f;
        }
        // outward normal of edge i (and whether start->end runs left-to-right seen from outside)
        static Vector2 Outward(int i)
        {
            var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length];
            var d = (b - a).normalized; var n = new Vector2(d.y, -d.x);
            if (Inside((a + b) * 0.5f + n * 0.3f)) n = -n;
            return n;
        }

        // ------------------------------------------------------------------ textures / materials
        static Texture2D Tex(string path, bool normal = false, bool linear = false, int max = 1024)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { Log("MISSING texture " + path); return null; }
            bool dirty = false;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != type) { imp.textureType = type; dirty = true; }
            if (!normal && imp.sRGBTexture == linear) { imp.sRGBTexture = !linear; dirty = true; }
            if (imp.maxTextureSize != max) { imp.maxTextureSize = max; dirty = true; }
            if (imp.anisoLevel != 8) { imp.anisoLevel = 8; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        static string PH(string id, string kind, string res = "2k") => $"{Src}PH_{id}/{id}_{kind}_{res}.jpg";

        static Material Mat(string name, string shader, System.Action<Material> init)
        {
            var path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (!sh) { Log("MISSING shader " + shader); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            m.CopyPropertiesFromMaterial(new Material(sh));
            init(m);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static void RockMat(string name, string albedo, string normal, Color tint, float silhouette = 0f, float tile = 0f, System.Action<Material> extra = null)
        {
            Mat(name, "KillKnight/AbyssRock", m =>
            {
                m.SetTexture("_BaseMap", Tex(albedo));
                if (normal != null) m.SetTexture("_BumpMap", Tex(normal, normal: true));
                m.SetColor("_Tint", tint); m.SetFloat("_Silhouette", silhouette); m.SetFloat("_Tri", tile);
                m.enableInstancing = true;
                extra?.Invoke(m);
            });
        }

        static void BuildMaterials()
        {
            Mat("KK_Floor", "KillKnight/Floor", m =>
            {
                m.SetTexture("_Base", Tex(PH("rock_01", "diff"), max: 2048)); m.SetTexture("_BaseN", Tex(PH("rock_01", "nor_gl"), normal: true, max: 2048));
                m.SetTexture("_Macro", Tex(PH("rock_boulder_dry", "diff"))); m.SetTexture("_MacroN", Tex(PH("rock_boulder_dry", "nor_gl"), normal: true));   // v3: non-directional scan (dark_rock striation drew diagonal lines)
                m.SetTexture("_MacroH", Tex(PH("rock_boulder_dry", "disp"), linear: true));
                m.SetTexture("_Crack", Tex(PH("cracked_concrete", "disp"), linear: true)); m.SetTexture("_CrackN", Tex(PH("cracked_concrete", "nor_gl"), normal: true));
            });
            // the plateau walls: dark_rock planar-mapped every 9 m
            RockMat("KK_Cliff", PH("rock_boulder_dry", "diff"), null, new Color(0.24f, 0.2f, 0.24f), 0f, 11f, m => { m.SetFloat("_Key", 0.9f); m.SetFloat("_Amb", 1.5f); m.SetColor("_Fill", new Color(0.6f, 0.54f, 0.64f)); m.SetVector("_HazeY", new Vector4(-30f, -330f, 0.95f, 0.9f)); m.SetVector("_GlowY", new Vector4(-25f, -260f, 1.0f, 0.3f)); m.SetColor("_Band", new Color(0.32f, 0.055f, 0.035f)); m.SetVector("_BandY", new Vector4(-4f, 3f, 26f, 0f)); });   // v3: ref side faces = dark purple-grey stone, warm only low down
            RockMat("KK_CliffMass1", PHMT("namaqualand_cliff_01", "diff"), PHMT("namaqualand_cliff_01", "nor_gl"), new Color(0.11f, 0.105f, 0.115f));
            RockMat("KK_CliffMass2", PHMT("namaqualand_cliff_02", "diff"), PHMT("namaqualand_cliff_02", "nor_gl"), new Color(0.11f, 0.105f, 0.115f));
            RockMat("KK_Rock09", PHMT("rock_09", "diff"), PHMT("rock_09", "nor_gl"), new Color(0.12f, 0.11f, 0.115f));
            RockMat("KK_Spire", SpireTex + "BaseColor.jpg", SpireTex + "Normal.jpg", new Color(0.12f, 0.11f, 0.115f));
            RockMat("KK_SKF_BoneDemon", Src + "SKF_BoneDemon/SKF_BoneDemon_albedo.jpg", Src + "SKF_BoneDemon/SKF_BoneDemon_normal.jpg", new Color(0.12f, 0.1f, 0.11f), 0.35f);
            RockMat("KK_SKF_GatesOfHell", Src + "SKF_GatesOfHell/SKF_GatesOfHell_albedo.jpg", null, new Color(0.13f, 0.11f, 0.11f), 0.2f);
            RockMat("KK_SKF_DragonSkull", Src + "SKF_DragonSkull/SKF_DragonSkull_albedo.jpg", Src + "SKF_DragonSkull/SKF_DragonSkull_normal.jpg", new Color(0.16f, 0.13f, 0.12f), 0.1f);
            RockMat("KK_SKF_AqueductRuin", Src + "SKF_AqueductRuin/SKF_AqueductRuin_albedo.jpg", Src + "SKF_AqueductRuin/SKF_AqueductRuin_normal.jpg", new Color(0.13f, 0.11f, 0.12f), 0.1f);
            RockMat("KK_Root1", PHMT("dead_tree_trunk_02", "diff"), PHMT("dead_tree_trunk_02", "nor_gl"), new Color(0.07f, 0.06f, 0.06f), 0.7f);
            RockMat("KK_Root2", PHMT("dead_quiver_trunk", "diff"), PHMT("dead_quiver_trunk", "nor_gl"), new Color(0.07f, 0.06f, 0.06f), 0.7f);
            RockMat("KK_Root3", PHMT("dead_tree_trunk_02", "diff"), null, new Color(0.06f, 0.05f, 0.05f), 0.8f);
            Mat("KK_AbyssFloor", "KillKnight/AbyssGlow", m => { m.SetFloat("_Mode", 0); });
            Mat("KK_RimGlow", "KillKnight/AbyssGlow", m =>
            {
                m.SetFloat("_Mode", 2); m.SetFloat("_Src", (float)BlendMode.One); m.SetFloat("_Dst", (float)BlendMode.One); m.SetFloat("_ZW", 0);
                m.SetColor("_Hot", new Color(0.5f, 0.08f, 0.04f)); m.SetFloat("_Alpha", 0.3f); m.SetFloat("_Fade", 3f);   // v5b: 1.0 washed the walls into flat salmon panels
                m.SetVector("_RimY", new Vector4(-9f, -0.3f, -55f, 0f));
                m.renderQueue = 3020;
            });
            for (int k = 0; k < 3; k++)
            {
                int kk = k;
                Mat("KK_Haze" + k, "KillKnight/AbyssGlow", m =>
                {
                    m.SetFloat("_Mode", 1); m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZW", 0);
                    m.SetFloat("_Alpha", new[] { 0.32f, 0.42f, 0.6f }[kk]); m.SetFloat("_Fade", new[] { 10f, 16f, 22f }[kk]);
                    m.SetColor("_Hot", new[] { new Color(0.85f, 0.15f, 0.18f), new Color(1.35f, 0.3f, 0.26f), new Color(2.2f, 0.6f, 0.32f) }[kk]);
                    m.SetColor("_Deep", new[] { new Color(0.4f, 0.06f, 0.09f), new Color(0.5f, 0.08f, 0.1f), new Color(0.62f, 0.1f, 0.11f) }[kk]);
                    m.renderQueue = 3000 - 10 + kk;
                });
            }
        }

        static Transform Group(string name, Transform parent = null)
        {
            var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g;
        }

        // ------------------------------------------------------------------ the flat top: 1 m grid clipped to the outline
        static void BuildFloor()
        {
            var v = new List<Vector3>(); var uv2 = new List<Vector2>(); var tri = new List<int>();
            var idx = new Dictionary<(int, int), int>();
            int Vtx(int x, int z)
            {
                if (idx.TryGetValue((x, z), out var i)) return i;
                i = v.Count; idx[(x, z)] = i;
                v.Add(new Vector3(x, 0, z)); uv2.Add(new Vector2(EdgeDist(new Vector2(x, z)), 0));
                return i;
            }
            int cells = 0;
            for (int x = -(int)Half; x < (int)Half; x++)
                for (int z = -(int)Half; z < (int)Half; z++)
                {
                    if (!Inside(new Vector2(x + 0.5f, z + 0.5f))) continue;
                    int v00 = Vtx(x, z), v10 = Vtx(x + 1, z), v01 = Vtx(x, z + 1), v11 = Vtx(x + 1, z + 1);
                    tri.AddRange(new[] { v01, v11, v10, v01, v10, v00 });
                    cells++;
                }
            var mesh = new Mesh { name = "KK_FloorMesh", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetUVs(1, uv2); mesh.SetTriangles(tri, 0);
            mesh.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
            mesh.RecalculateBounds();
            var mp = Gen + "/KK_FloorMesh.asset";
            AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var g = new GameObject("Ground"); g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["KK_Floor"]; mr.shadowCastingMode = ShadowCastingMode.Off;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
            Log($"floor: {cells} cells (m2), {v.Count} verts");
        }

        // ------------------------------------------------------------------ sheer walls straight down, rock masses under the lip
        static void BuildCliff()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector4>(); var uv = new List<Vector2>(); var tri = new List<int>();
            float run = 0;
            float[] ys = { 0f, -0.6f, -3f, -10f, -25f, -50f, -80f, -130f, -200f, -270f, SkirtBottom };
            for (int i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length];
                var o = Outward(i);
                var d = (b - a).normalized;
                // front normal of (TL, TR, BL) is (d.y, 0, -d.x) when start is on the left: swap ends if that points inward
                bool swap = Vector2.Dot(new Vector2(d.y, -d.x), o) < 0;
                var s = swap ? b : a; var e = swap ? a : b;
                var dd = (e - s).normalized; float len = (e - s).magnitude;
                int b0 = v.Count;
                foreach (var y in ys)
                {
                    v.Add(new Vector3(s.x, y, s.y)); v.Add(new Vector3(e.x, y, e.y));
                    uv.Add(new Vector2(run, y)); uv.Add(new Vector2(run + len, y));
                    for (int k = 0; k < 2; k++) { n.Add(new Vector3(o.x, 0, o.y)); t.Add(new Vector4(dd.x, 0, dd.y, -1)); }
                }
                for (int r = 0; r < ys.Length - 1; r++)
                {
                    int tl = b0 + r * 2, tr = tl + 1, bl = tl + 2, br = tl + 3;
                    tri.AddRange(new[] { tl, tr, bl, tr, br, bl });
                }
                run += len;
            }
            var mesh = new Mesh { name = "KK_CliffSkirt", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTangents(t); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds();
            var mp = Gen + "/KK_CliffSkirt.asset";
            AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var g = new GameObject("CliffWalls"); g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats["KK_Cliff"]; mr.shadowCastingMode = ShadowCastingMode.Off;

            // downloaded cliff scans pressed against the walls below the lip (tops at least 4 m down, so the cut edge stays
            // clean like the ref); they break the straight wall into rock and catch the glow
            var masses = Group("CliffMasses");
            int count = 0;
            for (int i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length];
                var o = Outward(i); float len = (b - a).magnitude;
                int pieces = len < 12f ? (rng.NextDouble() < 0.5 ? 1 : 0) : Mathf.RoundToInt(len / 16f);
                for (int k = 0; k < pieces; k++)
                {
                    float f = (k + R(0.25f, 0.75f)) / pieces;
                    var p2 = Vector2.Lerp(a, b, f) + o * R(1.5f, 5f);
                    float top = R(-60f, -16f), h = R(30f, 70f);
                    string id = rng.NextDouble() < 0.5 ? "namaqualand_cliff_01" : "namaqualand_cliff_02";
                    var yaw = Mathf.Atan2(o.x, o.y) * Mathf.Rad2Deg + 180f + R(-12f, 12f);
                    var tr = PlaceSized(PHM(id), mats[id.EndsWith("01") ? "KK_CliffMass1" : "KK_CliffMass2"], new Vector3(p2.x, top - h, p2.y), yaw,
                        new Vector3(R(8f, 13f), h, R(4f, 7f)), masses, R(-6f, 6f));
                    if (tr) count++;
                }
            }
            Log($"cliff: skirt {v.Count} verts, {count} rock masses");

            // v5: glow welling up under the rim: two additive sheets standing just outside every wall (2.5 m and 7 m out)
            var gv = new List<Vector3>(); var gt = new List<int>();
            foreach (float outM in new[] { 2.5f, 7f })
                for (int i = 0; i < Outline.Length; i++)
                {
                    var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length]; var o = Outward(i);
                    var d = (b - a).normalized;
                    var s0 = a + o * outM - d * outM; var e0 = b + o * outM + d * outM;   // stretched so the corners overlap
                    int b0 = gv.Count;
                    gv.Add(new Vector3(s0.x, 0f, s0.y)); gv.Add(new Vector3(e0.x, 0f, e0.y));
                    gv.Add(new Vector3(s0.x, -60f, s0.y)); gv.Add(new Vector3(e0.x, -60f, e0.y));
                    gt.AddRange(new[] { b0, b0 + 1, b0 + 2, b0 + 1, b0 + 3, b0 + 2 });
                }
            var gm = new Mesh { name = "KK_RimGlow" }; gm.SetVertices(gv); gm.SetTriangles(gt, 0); gm.RecalculateNormals(); gm.RecalculateBounds();
            var gp = Gen + "/KK_RimGlow.asset"; AssetDatabase.DeleteAsset(gp); AssetDatabase.CreateAsset(gm, gp);
            var rg = new GameObject("RimGlow"); rg.transform.SetParent(root, false);
            rg.AddComponent<MeshFilter>().sharedMesh = gm;
            var rr = rg.AddComponent<MeshRenderer>(); rr.sharedMaterial = mats["KK_RimGlow"]; rr.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ the abyss: spires, roots, glow floor, haze, embers
        static Vector2 OutsideAt(float minOut, float maxOut, out Vector2 dir)
        {
            for (int tries = 0; ; tries++)
            {
                float ang = R(0f, Mathf.PI * 2f);
                dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                float rr = Half + R(minOut, maxOut);
                var p = dir * rr;
                if (!Inside(p) && EdgeDist(p) >= minOut || tries > 60) return p;
            }
        }

        static void BuildAbyss()
        {
            var g = Group("Abyss");
            var spires = Group("Spires", g);
            int ns = 0, rejects = 0;
            // near ring: below the rim, tops 8-30 m down (seen past every edge); far ring: taller, some rising above the
            // plateau level at the back and sides (never in front of the camera, which looks north)
            for (int i = 0; i < 52; i++)
            {
                bool far = i >= 18;
                var p = OutsideAt(far ? 24f : 12f, far ? 110f : 34f, out var dir);
                float top = far ? R(-70f, 22f) : R(-120f, -20f);
                if (p.y < -20f) top = Mathf.Min(top, -14f);        // south of the plateau: stay well below the view line
                float h = top - AbyssFloorY + 6f;
                int kind = i % 4;
                string path = kind == 0 ? SpireFbx : kind == 1 ? PHM("rock_09") : kind == 2 ? PHM("namaqualand_cliff_02") : PHM("namaqualand_cliff_01");
                string mat = kind == 0 ? "KK_Spire" : kind == 1 ? "KK_Rock09" : kind == 2 ? "KK_CliffMass2" : "KK_CliffMass1";
                float w = kind == 0 ? R(9f, 15f) : R(7f, 13f);
                var mb = ModelBounds(path, AssetDatabase.LoadAssetAtPath<GameObject>(path));
                float natW = h * Mathf.Max(mb.size.x, mb.size.z) / Mathf.Max(0.01f, mb.size.y);
                w = Mathf.Max(w, natW / 2.5f);
                var tr = PlaceSized(path, mats[mat], new Vector3(p.x, AbyssFloorY - 6f, p.y), R(0, 360), new Vector3(w, h, w * R(0.7f, 1.1f)), spires, R(-5f, 5f));
                if (tr && Blocks(tr)) { Object.DestroyImmediate(tr.gameObject); tr = null; i--; if (++rejects > 900) break; continue; }
                if (tr) ns++;
            }
            // dead roots / thorn silhouettes at the sides and hanging off the walls
            var roots = Group("Roots", g);
            int nr = 0;
            string[] rootIds = { "dead_tree_trunk_02", "dead_quiver_trunk", "dead_tree_trunk_02" };
            for (int i = 0; i < 26; i++)
            {
                bool hang = i < 9;
                Vector3 pos; float yaw, tilt, size;
                if (hang)
                {
                    int e = rng.Next(Outline.Length);
                    var a = Outline[e]; var b = Outline[(e + 1) % Outline.Length]; var o = Outward(e);
                    var p2 = Vector2.Lerp(a, b, R(0.2f, 0.8f)) + o * R(2f, 6f);
                    pos = new Vector3(p2.x, R(-30f, -12f), p2.y);
                    yaw = Mathf.Atan2(o.x, o.y) * Mathf.Rad2Deg + R(-30f, 30f); tilt = R(30f, 70f); size = R(12f, 22f);
                }
                else
                {
                    var p = OutsideAt(10f, 45f, out var dir);
                    pos = new Vector3(p.x, R(-120f, -10f), p.y);
                    yaw = R(0, 360); tilt = R(-40f, 40f); size = R(30f, 70f);
                }
                if (pos.z < -Half - 2f) pos.y = Mathf.Min(pos.y, -16f);
                var tr = PlaceLong(PHM(rootIds[i % 3]), mats[i % 3 == 1 ? "KK_Root2" : i % 3 == 2 ? "KK_Root3" : "KK_Root1"], pos, yaw, size, roots, tilt);
                if (tr && Blocks(tr)) { Object.DestroyImmediate(tr.gameObject); tr = null; i--; if (++rejects > 800) break; continue; }
                if (tr) nr++;
            }

            // glow far below + haze sheets between
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "AbyssGlowFloor"; floor.transform.SetParent(g, false);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.position = new Vector3(0, AbyssFloorY, 0); floor.transform.localScale = new Vector3(110f, 1f, 110f);
            var fr = floor.GetComponent<MeshRenderer>(); fr.sharedMaterial = mats["KK_AbyssFloor"]; fr.shadowCastingMode = ShadowCastingMode.Off;
            float[] hy = { -55f, -125f, -210f };
            for (int k = 0; k < 3; k++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Plane);
                s.name = "HazeSheet" + k; s.transform.SetParent(g, false);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.transform.position = new Vector3(0, hy[k], 0); s.transform.localScale = new Vector3(110f, 1f, 110f);
                var sr = s.GetComponent<MeshRenderer>(); sr.sharedMaterial = mats["KK_Haze" + k]; sr.shadowCastingMode = ShadowCastingMode.Off;
            }
            // v3 (Kido: "무섭게 ... 커다란 구조물이나 화면 볼륨"): downloaded Sketchfab CC BY giants in the deep, on the far
            // sides only (camera looks north-east), several depths; dark silhouettes, glow from below by the shader
            var giants = Group("Giants", g);
            int ngi = 0;
            void Giant(string id, Vector3 pos, float yaw, float height, float tilt = 0f)
            {
                var t = Place($"{Src}{id}/{id}.fbx", mats["KK_" + id], pos, yaw, height, giants, tilt);
                if (t && Blocks(t)) { Log($"giant {id} at {pos} blocked, dropped"); Object.DestroyImmediate(t.gameObject); return; }
                if (t) ngi++;
            }
            Giant("SKF_BoneDemon", new Vector3(78f, -205f, -8f), 250f, 235f);            // east, 33 m out: head above the rim (screen right)
            Giant("SKF_BoneDemon", new Vector3(-22f, -255f, 82f), 200f, 230f);           // north, 37 m out
            Giant("SKF_GatesOfHell", new Vector3(70f, -150f, 45f), 225f, 100f, -6f);      // north-east, mid depth
            Giant("SKF_DragonSkull", new Vector3(12f, -62f, 76f), 175f, 55f, 18f);         // north, half sunk below the rim
            Giant("SKF_DragonSkull", new Vector3(82f, -125f, -40f), 285f, 70f, -10f);      // east-south-east
            Giant("SKF_AqueductRuin", new Vector3(68f, -95f, 78f), 40f, 70f);             // north-east, broken arches
            Giant("SKF_AqueductRuin", new Vector3(-78f, -175f, 40f), 120f, 85f);          // west, deep (camera side: stays low)
            Giant("SKF_BoneDemon", new Vector3(62f, -215f, -22f), 250f, 205f);          // v4: east, 17 m out, screen right
            Giant("SKF_GatesOfHell", new Vector3(60f, -112f, 24f), 240f, 95f);           // v4: east, close
            Giant("SKF_DragonSkull", new Vector3(-18f, -50f, 63f), 160f, 42f, 20f);      // v4: north, just under the rim
            Log($"giants: {ngi}");
            int ne = 0;
            for (int i = 0; i < 6; i++)
            {
                var p = OutsideAt(4f, 16f, out _);
                Embers(g, new Vector3(p.x, -45f, p.y)); ne++;
            }
            Log($"abyss: {ns} spires, {nr} roots, {ne} ember streams, {rejects} placements rejected");
        }

        // 11-19 isometric view (camera at the south-west, KKIsoView): nothing may rise through or near the plateau top,
        // and on the camera's side everything stays well below the rim (a spire widened to its natural shape poked
        // through the floor; a tilted root covered the west edge)
        static bool Blocks(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return false;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float gap = Mathf.Max(Mathf.Max(-Half - b.max.x, b.min.x - Half), Mathf.Max(-Half - b.max.z, b.min.z - Half));   // > 0: outside the bbox by that much
            if (b.max.y > -3f && gap < 10f) return true;
            bool camSide = b.center.x + b.center.z < 0f;
            if (camSide && b.max.y > -12f && gap < 45f) return true;
            if (camSide && b.max.y > -4f) return true;
            return false;
        }

        static Material pEmber;
        static void Embers(Transform parent, Vector3 pos)
        {
            if (!pEmber)
            {
                var sh = Shader.Find("Underworld/Particle");
                var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/spark_01.png");
                var path = $"{MatDir}/KK_Ember.mat";
                AssetDatabase.DeleteAsset(path);
                pEmber = new Material(sh) { name = "KK_Ember" };
                pEmber.SetTexture("_MainTex", sparkTex); pEmber.SetColor("_Tint", new Color(3f, 0.9f, 0.25f, 1f));
                pEmber.SetFloat("_Src", (float)BlendMode.One); pEmber.SetFloat("_Dst", (float)BlendMode.One); pEmber.SetFloat("_SoftFade", 0.5f);
                AssetDatabase.CreateAsset(pEmber, path);
            }
            var go = new GameObject("AbyssEmbers"); go.transform.SetParent(parent, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = pEmber; r.shadowCastingMode = ShadowCastingMode.Off;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true; mm.playOnAwake = true;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(8f, 13f); mm.startSpeed = 0f; mm.maxParticles = 40;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            var em = ps.emission; em.rateOverTime = 4f;
            var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(20f, 10f, 20f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f); vel.y = new ParticleSystem.MinMaxCurve(2.5f, 4.5f); vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.35f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.35f, 0.12f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0.7f, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            ps.Play();
        }

        // ------------------------------------------------------------------ model placement (from BossSeal's builder)
        static readonly Dictionary<string, Bounds> bounds = new Dictionary<string, Bounds>();
        static Bounds ModelBounds(string path, GameObject go)
        {
            if (bounds.TryGetValue(path, out var b)) return b;
            var tmp = Object.Instantiate(go); tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity;
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
        // sized by the model's longest side (lying trunks: a height fit blew them up to 100 m)
        static Transform PlaceLong(string path, Material mat, Vector3 pos, float yaw, float length, Transform parent, float tilt = 0f)
        {
            var t = Place(path, mat, pos, yaw, 1f, parent, tilt);
            if (!t) return null;
            var b = ModelBounds(path, AssetDatabase.LoadAssetAtPath<GameObject>(path));
            t.localScale = Vector3.one * (length / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))));
            return t;
        }
        static Transform PlaceSized(string path, Material mat, Vector3 pos, float yaw, Vector3 size, Transform parent, float tilt = 0f)
        {
            var t = Place(path, mat, pos, yaw, size.y, parent, tilt);
            if (!t) return null;
            var b = ModelBounds(path, AssetDatabase.LoadAssetAtPath<GameObject>(path));
            t.localScale = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
            return t;
        }

        // ------------------------------------------------------------------ light, post, camera
        static void BuildLightAndPost()
        {
            var key = new GameObject("KeyLight").AddComponent<Light>();
            key.transform.SetParent(root, false);
            key.type = LightType.Directional; key.color = Hex("#d8dad8"); key.intensity = 1.6f;
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.8f; key.lightmapBakeType = LightmapBakeType.Realtime;
            key.transform.rotation = Quaternion.Euler(58f, 150f, 0f);
            lightCount++;
            RenderSettings.sun = key;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#3a3e3e");
            RenderSettings.ambientEquatorColor = Hex("#1e1515");
            RenderSettings.ambientGroundColor = Hex("#3a1418");   // the deep's red bounce from below
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0.15f;
            LightingSettings ls = null;
            try { ls = Lightmapping.lightingSettings; } catch { }
            if (ls == null)
            {
                var lsPath = Root + "/KK_Lighting.lighting";
                ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
                if (!ls) { ls = new LightingSettings { name = "KK_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
                Lightmapping.lightingSettings = ls;
            }
            ls.bakedGI = false; ls.realtimeGI = false;

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true;
            vol.priority = 0;   // like the other stage scenes: the base look; StageMood / LookFX / CrispLook layer on top
            vol.sharedProfile = Profile();
            // v3: the game-wide crisp grade (RoomRun CrispLook, priority 1300: saturation -60, contrast 22, black point down) crushed
            // this map's crimson abyss to black. This map keeps its own saturation / contrast / lift-gain above it; the
            // rest of that grade (split toning, red-keeping hue curve, no DOF/CA/grain) still applies.
            // 11-20 (Kido: "다른맵들처럼 후처리 제대로"): no opt-out any more; the map takes the shared StageMood / LookFX / CrispLook
            // chain like every field (its room entries: CrispLook grade without black-point drop, LookFX press)

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#0a0505");
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = true; cd.requiresDepthTexture = true;
            cam.gameObject.AddComponent<AudioListener>();
            var off = Quaternion.Euler(KKIsoView.Pitch, KKIsoView.Yaw, 0) * Vector3.back * (new Vector2(17f, 14.5f).magnitude * 1.6f);
            cam.transform.position = off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }

        static VolumeProfile Profile()
        {
            var path = Root + "/KK_PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);
            // 11-20 v4: the map's own mood under the shared StageMood / LookFX / CrispLook chain (those win where they set a value)
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0f); ca.contrast.Override(18f); ca.saturation.Override(-15f); ca.colorFilter.Override(Color.white);
            var st = prof.Add<SplitToning>(true); st.shadows.Override(new Color(0.42f, 0.42f, 0.56f)); st.highlights.Override(new Color(0.72f, 0.55f, 0.5f)); st.balance.Override(10f);
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.2f); bl.intensity.Override(0.35f); bl.scatter.Override(0.7f); bl.highQualityFiltering.Override(false);
            var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#0a0306")); vg.intensity.Override(0.3f); vg.smoothness.Override(0.5f);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        // ------------------------------------------------------------------ invisible walls, check
        static void BuildEdgeColliders()
        {
            var walls = Group("EdgeWalls");
            for (int i = 0; i < Outline.Length; i++)
            {
                var a = Outline[i]; var b = Outline[(i + 1) % Outline.Length]; var o = Outward(i);
                var mid = (a + b) * 0.5f + o * 0.3f;   // 1 m thick, inner face 0.2 m inside the edge
                float len = (b - a).magnitude + 1f;
                var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.layer = WallLayer;
                w.transform.position = new Vector3(mid.x, 3f, mid.y);
                w.transform.rotation = Quaternion.LookRotation(new Vector3(o.x, 0, o.y));
                w.AddComponent<BoxCollider>().size = new Vector3(len, 8f, 1f);
            }
        }

        static int CheckArena()
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (c.name == "Ground" || c.transform.parent.name == "EdgeWalls") continue;
                if (c.bounds.max.y > -0.5f) { n++; Log($"COLLIDER ON TOP {c.name} {c.bounds.center}"); }
            }
            foreach (var r in root.Find("Abyss").GetComponentsInChildren<Renderer>().Concat(root.Find("CliffMasses").GetComponentsInChildren<Renderer>()))
            {
                var b = r.bounds;
                if (b.max.y > -0.2f && Inside(new Vector2(b.center.x, b.center.z))) Log($"NOTE piece above the top inside the outline: {r.name} {b.center} top {b.max.y:0.0}");
            }
            return n;
        }

        // ------------------------------------------------------------------ RoomRun dev room
        public static void AddRoomRunRoom()
        {
            const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
            if (!cfg) { Log("NO RoomRunConfig"); return; }
            var so = new SerializedObject(cfg);
            var rooms = so.FindProperty("rooms");
            var size = new Vector2(Half * 2, Half * 2);
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
            r.FindPropertyRelative("displayName").stringValue = "심연 고원";
            r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(11, 0);
            r.FindPropertyRelative("scenePath").stringValue = ScenePath;
            r.FindPropertyRelative("groundMeshName").stringValue = "";
            r.FindPropertyRelative("center").vector3Value = Vector3.zero;
            r.FindPropertyRelative("size").vector2Value = size;
            r.FindPropertyRelative("fitToStage").boolValue = false;
            r.FindPropertyRelative("floorColor").colorValue = Hex("#1e1f23");
            r.FindPropertyRelative("pillarCount").intValue = 0;
            r.FindPropertyRelative("doors").arraySize = 0;
            r.FindPropertyRelative("waves").arraySize = 0;
            r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
            r.FindPropertyRelative("devOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Log("room 'killknight' added to RoomRunConfig");
        }

        // which renderers sit between the game camera and a floor point (rr_queue "call KillKnight.KKBuilder.Probe")
        public static void Probe()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().path != ScenePath) { if (EditorSceneManager.GetActiveScene().isDirty) return; EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); }
            foreach (var t in new[] { Vector3.zero, new Vector3(30, 0, -31), new Vector3(-41, 0, 0) })
            {
                var off = Quaternion.Euler(KKIsoView.Pitch, KKIsoView.Yaw, 0) * Vector3.back * (new Vector2(17f, 14.5f).magnitude * KKIsoView.Zoom);
                var cam = t + off; var ray = new Ray(cam, -off.normalized); float dist = off.magnitude;
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r.bounds.Contains(cam)) Log($"probe {t}: CAMERA INSIDE {r.transform.parent?.name}/{r.name} bounds {r.bounds.center} size {r.bounds.size}");
                    else if (r.bounds.IntersectRay(ray, out float d) && d < dist - 0.5f) Log($"probe {t}: on the line at {d:0.0} m: {r.transform.parent?.name}/{r.name} bounds {r.bounds.center} size {r.bounds.size}");
                }
            }
            Log("probe done");
        }

        // ------------------------------------------------------------------ edit-mode shots (quick look checks, not the deliverables)
        // ArtSource/KillKnight/kk_shots.txt: "name x z zoom [yaw]" per line -> ArtSource/KillKnight/shots/<name>.png
        public static void Shots()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string dir = Path.GetDirectoryName(LogPath);
            string list = Path.Combine(dir, "kk_shots.txt");
            if (!File.Exists(list)) return;
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty) { Log("shots: active scene dirty, skipped"); return; }
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            bool async0 = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var cam = Camera.main;
            var outDir = Path.Combine(dir, "shots"); Directory.CreateDirectory(outDir);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(10f, true, true);
            foreach (var raw in File.ReadAllLines(list))
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(' ');
                float x = float.Parse(p[1], ci), z = float.Parse(p[2], ci), zoom = float.Parse(p[3], ci);
                float yaw = p.Length > 4 ? float.Parse(p[4], ci) : 0f;
                var off = Quaternion.Euler(KKIsoView.Pitch, KKIsoView.Yaw + yaw, 0) * Vector3.back * (new Vector2(17f, 14.5f).magnitude * zoom * (zoom >= 1.6f ? KKIsoView.Zoom / 1.6f : 1f));
                var target = new Vector3(x, 0f, z);
                cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, p[0] + ".png"), tex.EncodeToPNG());
            }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            ShaderUtil.allowAsyncCompilation = async0;
            Log("shots done");
        }
    }
}
