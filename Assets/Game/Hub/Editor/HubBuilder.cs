// Builds Assets/Game/Hub/Hub.unity, the 거점 (HUB) courtyard (11-20, from Kido's two concept images
// ArtSource/Hub/ref/hub_concept_persp.png / hub_concept_top.png).
//   - walled court, north up (+z): three roofed gates in the north wall (KCISA 경선문, CC BY). The left one is the sealed
//     portal gate (HubSealGate: talismans burn, chains snap, doors swing, cyan portal opens, walk in -> targetRoom); the
//     middle and right ones stay chained shut. Walls: Sketchfab "Korea Traditional House_House Wall" straight module.
//   - floor: Quixel Megascans castle floor stone (already in DarkCrypt), the carved 팔괘 circle in the centre is a real
//     temple-floor scan (Sketchfab stubfish, CC BY) sunk so only its carved top shows
//   - west: two stone lanterns with cyan flames, the scholar's desk + armchair (Poly Haven CC0), books / scrolls
//     (NPC anchor NPC_Scholar; Assets/Game/Hub/ScholarNPC/ScholarNPC.prefab is dropped on it when present)
//   - east: blacksmith corner (Old Smithy forge, anvil, tool rack, gears, weapon rack with KHS spears / swords)
//   - south: KHS 교방고 drum, an empty slot for the scarecrow (Kido makes it)
//   - every model is downloaded (see Assets/Game/Hub/CREDITS.txt); this builder only places, scales and lights them.
//     Code-made geometry: the walkable ground grid (textured by the scan), invisible edge colliders, VFX cards
//     (portal swirl quad, flame / fog / talisman particles).
// Run via rr_queue: "call Hub.HubBuilder.Build". Edit-mode looks: "call Hub.HubBuilder.Shots" (ArtSource/Hub/hub_shots.txt).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hub
{
    public static class HubBuilder
    {
        public const string Root = "Assets/Game/Hub";
        public const string ScenePath = Root + "/Hub.unity";
        public const string RoomId = "hub";
        const string TP = Root + "/ThirdParty/";
        const string MatDir = Root + "/Materials";
        const string ScholarPrefab = Root + "/ScholarNPC/ScholarNPC.prefab";
        const int WallLayer = 1;   // RoomRun Ground.WallLayer

        // court layout (metres): walkable floor x in [-InX, InX], z in [SouthZ, NorthZ]
        const float InX = 13.8f, NorthZ = 7.3f, SouthZ = -8.8f;   // 11-20 v2: ~28 x 17 m like the concept (v1 40 x 25 read empty)
        const float WallX = 15.1f, WallNZ = 8.8f, WallSZ = -10.1f;
        static readonly float[] GateX = { 0f };   // v11 (Kido): one big 저승문 (torii) instead of three gates
        const float GateScale = 1.6f;
        const float ToriiWidth = 10.5f, ToriiDepth = 0f;   // roof span
        static float ToriiOuterHalf = 4.5f;   // set by BuildGates (pads outer edge), used by BuildWalls for the gap
        public static readonly Vector3 DeskPos = new Vector3(-10.27f, 0f, 0.6f);
        const float DeskScale = 1.7f;   // v3 1.3, v4 1.7 (Kido: scholar too small next to the 2.85 m player)
        public static readonly Vector3 ChairPos = new Vector3(-10.9f, 0f, 0.6f);
        static readonly Vector2 SealCentre = new Vector2(0f, -0.6f);
        public static readonly Vector3 AnvilPos = new Vector3(9.5f, 0f, 0.4f);   // v7 (Kido): back at its original spot; the smith works it facing WEST (screen-left)
        public static readonly Vector3 SmithPos = new Vector3(10.45f, 0f, 0.4f);   // east of the anvil, faces -X = west = screen-left, 0.95 m from its centre
        const string GoblinPrefab = Root + "/GoblinSmith/GoblinSmith.prefab";

        static Transform root;
        static int lightCount;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static System.Random rng;

        static string LogPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "Hub", "hub_log.txt");
        static void Log(string s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }
        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

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
            rng = new System.Random(1120);
            mats.Clear(); lightCount = 0;
            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Hub").transform;
            BuildGround();
            BuildGates();   // first: sets ToriiOuterHalf for the wall gap
            BuildWalls();
            BuildCentre();
            BuildWest();
            BuildEast();
            BuildSouth();
            BuildDressing();
            BuildFog();
            BuildWisps();   // v9: downloaded flame flipbook, no self-made dots/trails; motes removed (Kido)
            BuildLightAndCamera();
            BuildEdgeColliders();
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddRoomRunRoom();
            Log($"BUILT {ScenePath}: lights={lightCount} renderers={root.GetComponentsInChildren<Renderer>().Length}");
        }

        // ------------------------------------------------------------------ helpers
        static Transform Group(string name, Transform parent = null)
        {
            var g = new GameObject(name).transform; g.SetParent(parent ? parent : root, false); return g;
        }

        static Texture2D Tex(string path, bool normal = false, int max = 1024)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return null;
            bool dirty = false;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (imp.textureType != type) { imp.textureType = type; dirty = true; }
            if (imp.maxTextureSize != max) { imp.maxTextureSize = max; dirty = true; }
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, string shader, System.Action<Material> init)
        {
            if (mats.TryGetValue(name, out var have)) return have;
            var path = $"{MatDir}/{name}.mat";
            var sh = Shader.Find(shader);
            if (!sh) { Log("MISSING shader " + shader); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.CopyPropertiesFromMaterial(new Material(sh));
            init(m);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        // URP Lit from a converted model's per-slot maps (ThirdParty/<id>/<id>_<slot>_albedo/normal.jpg)
        static Material SlotMat(string id, int slot, Color tint, float smooth = 0.15f)
        {
            string a = $"{TP}{id}/{id}_{slot}_albedo.jpg", n = $"{TP}{id}/{id}_{slot}_normal.jpg";
            return Mat($"{id}_{slot}", "Universal Render Pipeline/Lit", m =>
            {
                var at = Tex(a); if (at) m.SetTexture("_BaseMap", at);
                var nt = File.Exists(n) ? Tex(n, normal: true) : null;
                if (nt) { m.SetTexture("_BumpMap", nt); m.EnableKeyword("_NORMALMAP"); }
                m.SetColor("_BaseColor", at ? tint : tint * 0.35f);
                m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                m.enableInstancing = true;
            });
        }

        // instantiate a model (keeps the FBX link), scale it so its bounds reach `size` along `axis`
        // (0 x, 1 y = height, 2 z, 3 = largest), stand it on y = baseY (or sink by `sink` of its height), yaw it.
        static GameObject Place(string assetPath, string name, Transform parent, Vector3 pos, float yaw, float size, int axis = 1, float sink = 0f, float baseY = 0f)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (!src) { Log("MISSING model " + assetPath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            float cur = axis == 3 ? Mathf.Max(b.size.x, b.size.y, b.size.z) : b.size[axis];
            float s = cur > 1e-5f ? size / cur : 1f;
            go.transform.localScale = Vector3.one * s;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            b = Bounds(go);
            var off = new Vector3(pos.x - b.center.x, baseY - b.min.y - sink * b.size.y, pos.z - b.center.z);
            go.transform.position += off + (parent ? Vector3.zero : Vector3.zero);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            return go;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // assign HB slot materials by the importer's slot names (<id>_<i>)
        static void Skin(GameObject go, string id, Color tint, float smooth = 0.15f)
        {
            if (!go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    int slot = i;
                    if (ms[i]) { var nm = ms[i].name; int us = nm.LastIndexOf('_'); if (us >= 0 && int.TryParse(nm.Substring(us + 1), out int k)) slot = k; }
                    ms[i] = SlotMat(id, slot, tint, smooth);
                }
                r.sharedMaterials = ms;
            }
        }
        static void SkinAll(GameObject go, Material m)
        {
            if (!go || !m) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = m; r.sharedMaterials = ms; }
        }
        static void Solid(GameObject go, float pad = 0f, float height = -1f)
        {
            if (!go) return;
            var b = Bounds(go);
            var c = new GameObject("Collider"); c.transform.SetParent(go.transform.parent, false);
            c.transform.position = new Vector3(b.center.x, height > 0 ? height * 0.5f : b.center.y, b.center.z);
            var bc = c.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(0.2f, b.size.x - pad), height > 0 ? height : b.size.y, Mathf.Max(0.2f, b.size.z - pad));
        }
        static Light PointLight(string name, Transform parent, Vector3 pos, Color col, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent, false); l.transform.position = pos;
            l.type = LightType.Point; l.color = col; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
            lightCount++;
            return l;
        }

        // ------------------------------------------------------------------ ground
        static void BuildGround()
        {
            const string F = "Assets/Game/DarkCrypt/Source/Fab_castle_floor_stone_tdblbcyt_2k/Castle_Floor_Stone_tdblbcyt_2K_";
            // v3 (Kido: "바닥에 건곤 ... 보스라운드에 있는 그거"): the 봉인의 제단 seal mask carved into the floor shader
            var m = Mat("HB_Floor", "Hub/Ground", mm =>
            {
                mm.SetTexture("_BaseMap", Tex(F + "BaseColor.jpg", max: 2048));
                mm.SetTexture("_BumpMap", Tex(F + "Normal.jpg", normal: true, max: 2048));
                var ao = Tex(F + "AO.jpg"); if (ao) mm.SetTexture("_OcclusionMap", ao);
                mm.SetColor("_BaseColor", Hex("#a3a8ad"));
                mm.SetFloat("_Tile", 2.5f);
                mm.SetTexture("_Seal", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/BossSeal/Source/Motifs/BS_Seal_TaegeukPalgwae.png"));
                mm.SetVector("_SealCentre", new Vector4(SealCentre.x, SealCentre.y, 4.3f, 0.35f));
                // v4 ground rules: macro breakup, wall-foot dirt, walk lanes (Megascans dirt ground + Poly Haven rock macro, both in DarkCrypt)
                const string D = "Assets/Game/DarkCrypt/Source/";
                mm.SetTexture("_Dirt", Tex(D + "Fab_dirt_ground_xdhhefr_2k/Dirt_Ground_xdhhefr_2K_BaseColor.jpg"));
                mm.SetTexture("_DirtN", Tex(D + "Fab_dirt_ground_xdhhefr_2k/Dirt_Ground_xdhhefr_2K_Normal.jpg", normal: true));
                mm.SetTexture("_Macro", Tex(D + "PH_rock_boulder_dry/rock_boulder_dry_diff_2k.jpg"));
                mm.SetVector("_Court", new Vector4(WallX - 0.4f, WallSZ + 0.4f, WallNZ - 0.4f, 1.6f));
                mm.SetVector("_MacroAmt", new Vector4(8f, 0.28f, 0.58f, 0.08f));   // v7: stronger macro value swing, more dirt patches
            });
            // one grid mesh over the whole walled area, uv = world metres / 2.5 (scan tile ~2.5 m)
            float x0 = -WallX - 2f, x1 = WallX + 2f, z0 = WallSZ - 4f, z1 = WallNZ + 3f;
            int nx = Mathf.CeilToInt((x1 - x0) / 2f), nz = Mathf.CeilToInt((z1 - z0) / 2f);
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
                {
                    float x = Mathf.Lerp(x0, x1, i / (float)nx), z = Mathf.Lerp(z0, z1, j / (float)nz);
                    v.Add(new Vector3(x, 0f, z)); uv.Add(new Vector2(x, z) / 2.5f);
                }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    t.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh { name = "HB_Ground" };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            var gen = Root + "/Generated"; Directory.CreateDirectory(gen);
            AssetDatabase.DeleteAsset(gen + "/HB_Ground.asset");
            AssetDatabase.CreateAsset(mesh, gen + "/HB_Ground.asset");
            var g = new GameObject("Ground"); g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            g.AddComponent<MeshRenderer>().sharedMaterial = m;
            g.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // ------------------------------------------------------------------ walls (straight module of the hanok wall kit)
        const string WallFbx = "Assets/Game/SnowPlague/ThirdParty/Landmarks/HanokWall/SKF_HanokWall.fbx";
        const string WallTex = "Assets/Game/SnowPlague/ThirdParty/Landmarks/HanokWall/textures/";
        static Material wallCap, wallBody;
        static GameObject WallRun(Transform parent, Vector3 a, Vector3 b, float height, string name)
        {
            // keeps only the kit's straight segment (Object074: cap + body), stretched to the run length a->b
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(WallFbx);
            if (!src) { Log("MISSING wall"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            go.name = name; go.transform.SetParent(parent, false);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!r.name.StartsWith("Object074")) r.gameObject.SetActive(false);
                else
                {
                    r.sharedMaterial = r.name.Contains("925") ? wallBody : wallCap;
                    // v9 (Kido: jagged edge on the floor by the doors): the tile caps' sawtooth shadow off; the wall body still casts
                    if (!r.name.Contains("925")) r.shadowCastingMode = ShadowCastingMode.Off;
                }
            go.transform.localScale = Vector3.one; go.transform.rotation = Quaternion.identity; go.transform.position = Vector3.zero;
            var bb = ActiveBounds(go);
            bool alongX = bb.size.x >= bb.size.z;
            float len = alongX ? bb.size.x : bb.size.z, h = bb.size.y;
            var dir = b - a; float want = dir.magnitude;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - (alongX ? 90f : 0f);
            float sy = height / h, sl = want / len;
            go.transform.localScale = alongX ? new Vector3(sl, sy, sy) : new Vector3(sy, sy, sl);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            bb = ActiveBounds(go);
            var mid = (a + b) * 0.5f;
            go.transform.position += new Vector3(mid.x - bb.center.x, -bb.min.y, mid.z - bb.center.z);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            return go;
        }
        static Bounds ActiveBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void BuildWalls()
        {
            wallCap = Mat("HB_WallCap", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex(WallTex + "15_-_Default_baseColor.png"));
                var n = Tex(WallTex + "15_-_Default_normal.png", normal: true); if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
                m.SetColor("_BaseColor", Hex("#8a8c90")); m.SetFloat("_Smoothness", 0.2f);
            });
            wallBody = Mat("HB_WallBody", "Universal Render Pipeline/Lit", m =>
            {
                m.SetTexture("_BaseMap", Tex(WallTex + "Material_925_baseColor.png"));
                m.SetColor("_BaseColor", Hex("#6f6a66")); m.SetFloat("_Smoothness", 0.1f);
            });
            var g = Group("Walls");
            const float H = 3.6f, Hs = 1.6f, seg = 7.2f;
            // west / east: full height, split into ~7 m modules
            foreach (float sx in new[] { -1f, 1f })
                Runs(g, new Vector3(sx * WallX, 0, WallSZ), new Vector3(sx * WallX, 0, WallNZ), H, seg, sx < 0 ? "W" : "E");
            // north: between and beside the three gates
            // v11 (Kido: one 저승문, walls joined): the north wall runs unbroken behind the torii, which stands just in front of it
            // v14 (Kido: remove the wall over the gate's width): gap the width of the torii pads, wall ends tucked into them
            float gap = ToriiOuterHalf - 0.15f;
            Runs(g, new Vector3(-WallX, 0, WallNZ), new Vector3(GateX[0] - gap, 0, WallNZ), H, seg, "NW");
            Runs(g, new Vector3(GateX[0] + gap, 0, WallNZ), new Vector3(WallX, 0, WallNZ), H, seg, "NE");
            // south: low wall (the camera looks over it)
            Runs(g, new Vector3(-WallX, 0, WallSZ), new Vector3(WallX, 0, WallSZ), Hs, seg, "S");
        }
        static void Runs(Transform g, Vector3 a, Vector3 b, float h, float seg, string tag)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((b - a).magnitude / seg));
            for (int i = 0; i < n; i++)
                WallRun(g, Vector3.Lerp(a, b, i / (float)n), Vector3.Lerp(a, b, (i + 1) / (float)n), h, $"Wall_{tag}_{i}");
        }

        // ------------------------------------------------------------------ gates
        static void BuildGates()
        {
            var g = Group("Gates");
            var chainMat = SlotMat("HB_Chain", 0, Hex("#8c8580"), 0.35f);
            var talMat = Mat("HB_Talisman", "Hub/Talisman", m =>
            {
                // 11-20 Kido: "부적 ... 괜찮은거 받아서": a real talisman scan, Wikimedia Commons "TaoistCharm.JPG" (public domain)
                m.SetTexture("_MainTex", Tex(TP + "HB_Talisman/HB_TalismanAtlas.png"));   // 4 public-domain scans side by side
                m.SetFloat("_Photo", 1f); m.SetFloat("_Bright", 0.85f);
            });
            for (int gi = 0; gi < GateX.Length; gi++)
            {
                var gate = Group(gi == 0 ? "Gate_Portal" : $"Gate_Sealed{gi}", g);
                gate.position = new Vector3(GateX[gi], 0f, WallNZ);
                // v11: the KCISA frame is only used to orient + size the door leaves, then removed; the gate is the torii
                var frame = Place(TP + "HB_Gate/HB_Gate.fbx", "Frame", gate, gate.position, 0f, 4.26f * GateScale);
                // face the court: the 현판 (slot 8) hangs on the front side
                var sign = frame.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials.Select((m, i) => (r, m, i))).FirstOrDefault(x => x.m && x.m.name.EndsWith("_8"));
                float frontSign = 1f;
                if (sign.r)
                {
                    var mf = sign.r.GetComponent<MeshFilter>();
                    var sub = mf.sharedMesh.GetSubMesh(sign.i);
                    var c = sign.r.transform.TransformPoint(sub.bounds.center);
                    frontSign = Mathf.Sign(c.z - gate.position.z);
                }
                float yaw = frontSign > 0 ? 180f : 0f;   // front must point -z (south, into the court)
                gate.rotation = Quaternion.Euler(0, yaw, 0);
                // door leaves: same transform as the frame, so they sit in it; their child nodes pivot on the hinges
                var dl = Place(TP + "HB_DoorL/HB_DoorL.fbx", "DoorL", gate, Vector3.zero, 0f, 1f);
                var dr = Place(TP + "HB_DoorR/HB_DoorR.fbx", "DoorR", gate, Vector3.zero, 0f, 1f);
                foreach (var d in new[] { dl, dr })
                {
                    if (!d) continue;
                    d.transform.localPosition = frame.transform.localPosition; d.transform.localRotation = frame.transform.localRotation; d.transform.localScale = frame.transform.localScale;
                }
                Skin(dl, "HB_DoorL", Hex("#4a3a2c")); Skin(dr, "HB_DoorR", Hex("#4a3a2c"));   // dark weathered wood like the concept, not palace green
                Object.DestroyImmediate(frame);
                // the 저승문: Sketchfab "Torii gate" (DESIGN BUREAU, CC BY-NC-SA - non-commercial, see CREDITS) in front of the
                // north wall (the wall runs on behind it, so it joins with no gap); doors fill the opening between its pillars
                var torii = Place(TP + "HB_Torii/HB_Torii.fbx", "Torii", gate, new Vector3(GateX[gi], 0f, WallNZ - ToriiDepth), 0f, ToriiWidth, 0);
                // its own URP materials (the FBX .meta remap was lost on reimport): "01 - Default" = gate, "02 - Default" = stone base
                {
                    var mMain = AssetDatabase.LoadAssetAtPath<Material>(TP + "HB_Torii/Materials/HB_Torii_Main.mat");
                    var mGround = AssetDatabase.LoadAssetAtPath<Material>(TP + "HB_Torii/Materials/HB_Torii_Ground.mat");
                    foreach (var r in torii.GetComponentsInChildren<Renderer>())
                    {
                        var ms = r.sharedMaterials;
                        for (int i = 0; i < ms.Length; i++) ms[i] = (ms[i] && ms[i].name.StartsWith("02")) ? mGround : mMain;
                        r.sharedMaterials = ms;
                    }
                }
                var tb = Bounds(torii);
                // the real pillars + beams are "Object120"; their foot pads are "Object118" (two pads); front = the side the stone
                // steps / lanterns stick out to -> turn it to face the court (-z)
                var pads = torii.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name.StartsWith("Object118"));
                var padR = pads ? pads.GetComponent<Renderer>() : null;
                if (padR && tb.center.z > padR.bounds.center.z) torii.transform.RotateAround(new Vector3(padR.bounds.center.x, 0, padR.bounds.center.z), Vector3.up, 180f);
                // v14 (Kido: move the gate + lanterns back): the pillar line sits ON the wall line (the wall has a gap for it)
                if (padR) torii.transform.position += new Vector3(GateX[gi] - padR.bounds.center.x, 0f, WallNZ - padR.bounds.center.z);
                // sink the base slab so its top sits ~8 cm above the floor
                var baseR = torii.GetComponentsInChildren<Renderer>().Where(r => r.name.StartsWith("Object084")).ToArray();
                if (baseR.Length > 0) { float top = baseR[0].bounds.max.y; torii.transform.position += Vector3.up * (0.08f - top); }
                // the opening = between the pads' inner edges (vertex scan: nearest vertex to the centre on each side)
                float inL = -1f, inR = 1f, outL = -2f, outR = 2f; float cx = GateX[gi];
                if (pads)
                {
                    var vs0 = pads.sharedMesh.vertices.Select(v => pads.transform.TransformPoint(v)).ToArray(); float yMin = vs0.Min(v => v.y); var vs = vs0.Where(v => v.y < yMin + 0.8f).ToArray();   // the pads only (the mesh also holds the top beam)
                    inL = vs.Where(v => v.x < cx).Max(v => v.x); inR = vs.Where(v => v.x > cx).Min(v => v.x);
                    outL = vs.Min(v => v.x); outR = vs.Max(v => v.x);
                }
                float innerW = inR - inL;
                ToriiOuterHalf = Mathf.Max(cx - outL, outR - cx);
                var open = new Vector3((inL + inR) * 0.5f, 0.08f, padR ? padR.bounds.center.z : WallNZ);
                // door leaves: exactly fill between the pads (height 3.4 m), standing in the pillar line
                var db0 = Bounds(dl); db0.Encapsulate(Bounds(dr));
                float sx = innerW / db0.size.x, sy = 3.4f / db0.size.y;
                foreach (var d in new[] { dl, dr }) if (d) d.transform.localScale = new Vector3(d.transform.localScale.x * sx, d.transform.localScale.y * sy, d.transform.localScale.z * sy);
                db0 = Bounds(dl); db0.Encapsulate(Bounds(dr));
                var mv = new Vector3(open.x - db0.center.x, open.y - db0.min.y, open.z - db0.center.z);
                foreach (var d in new[] { dl, dr }) if (d) d.transform.position += mv;
                Log($"torii opening {innerW:0.00} m ({inL:0.00}..{inR:0.00}), outer half {ToriiOuterHalf:0.00}, at {open}");
                var pil = padR ? new[] { padR } : new Renderer[0];
                var frameGo = torii; frame = torii;
                var b = Bounds(frame);
                var doorB = Bounds(dl); doorB.Encapsulate(Bounds(dr));
                // chains: an X across the doors (+ one horizontal), on the court side
                var chains = new List<Rigidbody>();
                Vector3 fwd = -gate.forward * frontSign * (frontSign > 0 ? -1f : 1f);
                fwd = (Vector3.back);   // court side is -z in world
                float faceZ = doorB.min.z - 0.06f;
                float cw = doorB.size.x, ch = doorB.size.y;
                var cy = doorB.center.y;
                // v16 (Kido: download a proper rope, varied talismans, a rope above them too; the model's own top rope must burn):
                // ropes = Sketchfab "注連縄 (Shimenawa)" by ik-models (Free Standard) - thick twisted rope with its own paper shide,
                // one high across the top and three across the doors, each a little different (sag, tilt, depth); talismans =
                // four public-domain scans (atlas, random design per strip), varied size / angle / spacing, all hung clear of the
                // floor. The torii's own rope + shide + bells join the burn list so the opening doors never pass through them.
                var hang = new List<Vector3>(); var tilt = new List<float>(); var tscale = new List<float>(); var burn = new List<GameObject>();
                foreach (var r in torii.GetComponentsInChildren<Renderer>())
                    if (r.name.StartsWith("Line008") || r.name.StartsWith("Shape022") || r.name.StartsWith("Object26") || r.name.StartsWith("Cylinder021")) burn.Add(r.gameObject);
                var ropeMat = Mat("HB_Rope", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", Hex("#5a4a36")); m.SetFloat("_Smoothness", 0.1f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); });
                var paperMat = Mat("HB_Shide", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", Hex("#d8d2c4")); m.SetFloat("_Smoothness", 0.05f); m.SetFloat("_Cull", 0f); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); });
                float ropeY0 = doorB.min.y;
                var ropeSpecs = new[] { (top: ropeY0 + ch * 1.02f, w: innerW + 1.6f, z: 0.02f, tiltZ: 0f, sag: 1.0f),    // above everything, pillar to pillar
                                        (top: ropeY0 + ch * 0.86f, w: innerW + 0.5f, z: 0.12f, tiltZ: 2.5f, sag: 0.8f),
                                        (top: ropeY0 + ch * 0.66f, w: innerW + 0.3f, z: 0.20f, tiltZ: -4f, sag: 0.9f),
                                        (top: ropeY0 + ch * 0.47f, w: innerW + 0.6f, z: 0.28f, tiltZ: 3f, sag: 0.7f) };
                int ri = 0;
                foreach (var rs in ropeSpecs)
                {
                    var rp = Place(TP + "HB_Shimenawa/HB_Shimenawa.fbx", "SealRope" + ri, gate, new Vector3(open.x, 0f, doorB.min.z - rs.z), 0f, rs.w, 0);
                    if (!rp) break;
                    foreach (var r in rp.GetComponentsInChildren<Renderer>())
                    {
                        var ms = r.sharedMaterials;
                        for (int q = 0; q < ms.Length; q++) ms[q] = q == 0 ? ropeMat : paperMat;
                        r.sharedMaterials = ms; r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    rp.transform.localScale = Vector3.Scale(rp.transform.localScale, new Vector3(1f, rs.sag, 1f));
                    rp.transform.Rotate(0f, 0f, rs.tiltZ, Space.World);
                    var rb1 = Bounds(rp);
                    rp.transform.position += new Vector3(0f, rs.top - rb1.max.y, 0f);
                    burn.Add(rp);
                    var rb2 = Bounds(rp);
                    if (ri > 0)
                    {
                        // talismans: 4-6 per door rope, uneven spacing, varied size and angle; centres kept >= 1.0 m off the floor
                        int n = 4 + (ri % 3);
                        float x = rb2.min.x + R(0.35f, 0.6f);
                        for (int q = 0; q < n && x < rb2.max.x - 0.3f; q++)
                        {
                            float u = Mathf.InverseLerp(rb2.min.x, rb2.max.x, x);
                            float ropeAt = rb2.max.y - 0.08f - Mathf.Sin(Mathf.PI * u) * (rb2.size.y * 0.38f) + Mathf.Tan(rs.tiltZ * Mathf.Deg2Rad) * (x - rb2.center.x);
                            float sc = R(0.7f, 1.15f);
                            float y = Mathf.Max(1.0f + 0.5f * sc, ropeAt - 0.5f * sc - 0.05f);
                            hang.Add(new Vector3(x, y, rb2.min.z - 0.06f - q * 0.004f)); tilt.Add(R(-14f, 14f)); tscale.Add(sc);
                            x += R(0.42f, 0.95f);
                        }
                    }
                    ri++;
                }
                var runs = new int[ri];
                var tal = Talismans(gate, talMat, new Vector3(doorB.center.x, cy, faceZ - 0.14f), cw, ch, gi);
                {
                    var em2 = tal.emission; em2.SetBursts(new ParticleSystem.Burst[0]);
                    var hh = tal.gameObject.AddComponent<HubTalismanHang>();
                    hh.points = hang.Select(v => tal.transform.InverseTransformPoint(v)).ToArray(); hh.tilt = tilt.ToArray(); hh.scale = tscale.ToArray(); hh.size = new Vector2(0.33f, 0.99f);
                    var tsa = tal.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 1; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.999f); tsa.cycleCount = 1;
                    Log($"seal ropes {runs.Length}, {burn.Count} burn pieces, {hang.Count} talismans");
                }
                if (gi != 0) Dais(gate, doorB, false);   // v3 (Kido): a round stone step in front of every gate
                if (gi == 0)
                {
                    BuildPortal(gate, frame, dl, dr, b, doorB, chains.ToArray(), tal, frontSign);
                    gate.GetComponent<HubSealGate>().burnAway = burn.ToArray();
                }
                // colliders on the two pillars only (the opening stays for the portal)
                foreach (var r in pil) { var c = new GameObject("PillarCol").AddComponent<BoxCollider>(); c.transform.SetParent(gate, false); c.transform.position = new Vector3(r.bounds.center.x, 2f, r.bounds.center.z); c.size = new Vector3(r.bounds.size.x + 0.2f, 4f, r.bounds.size.z + 0.2f); }
            }
        }

        // round stone step in front of a gate (Sketchfab LukeCG, CC BY); the portal one glows cyan when it opens
        static Renderer Dais(Transform gate, Bounds doorB, bool glow)
        {
            var dais = Place(TP + "HB_Dais/HB_Dais.fbx", "Dais", gate, new Vector3(doorB.center.x, 0f, doorB.min.z - 1.9f), 0f, 3.4f, 0);   // v11: clear of the seal ring
            if (!dais) return null;
            var db = Bounds(dais); dais.transform.localScale = new Vector3(dais.transform.localScale.x, dais.transform.localScale.y * (0.38f / db.size.y), dais.transform.localScale.z);
            db = Bounds(dais); dais.transform.position += Vector3.up * -db.min.y;
            Skin(dais, "HB_Dais", Hex("#9aa2a6"));
            if (glow) foreach (var m in dais.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            var mc = dais.GetComponentInChildren<MeshFilter>(); if (mc) mc.gameObject.AddComponent<MeshCollider>().sharedMesh = mc.sharedMesh;
            return dais.GetComponentInChildren<Renderer>();
        }

        static Transform Leaf(GameObject d) => !d ? null : d.transform.childCount > 0 ? d.transform.GetChild(0) : d.transform;

        static ParticleSystem Talismans(Transform gate, Material mat, Vector3 centre, float w, float h, int seed)
        {
            var go = new GameObject("Talismans"); go.transform.SetParent(gate, false); go.transform.position = centre;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = false; main.duration = 1f; main.startLifetime = 1e6f; main.startSpeed = 0f;
            main.startSize3D = true; main.startSizeX = 0.3f; main.startSizeY = 0.99f; main.startSizeZ = 1f;   // the scan is 224 x 741
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.playOnAwake = true; main.maxParticles = 64;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 22) });   // v12 (Kido: "부적도 많이 걸어놔")
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(w * 0.85f, h * 0.7f, 0.01f);
            ps.useAutoRandomSeed = false; ps.randomSeed = (uint)(77 + seed * 13);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.VerticalBillboard; r.alignment = ParticleSystemRenderSpace.View;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static void BuildPortal(Transform gate, GameObject frame, GameObject dl, GameObject dr, Bounds b, Bounds doorB, Rigidbody[] chains, ParticleSystem tal, float frontSign)
        {
            var sg = gate.gameObject.AddComponent<HubSealGate>();
            sg.doorL = Leaf(dl); sg.doorR = Leaf(dr);
            sg.chains = chains; sg.talismans = new[] { tal };
            // swing inward (away from the court, +z): decided from the hinge positions after placement
            sg.doorSwing = 105f;
            // portal card just behind the door plane
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "PortalSwirl";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(gate, true);
            q.transform.position = new Vector3(doorB.center.x, doorB.center.y + 0.05f, doorB.max.z + 0.12f);
            q.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            q.transform.localScale = new Vector3(doorB.size.x * 0.98f, doorB.size.y * 1.02f, 1f) / gate.lossyScale.x;
            var pm = Mat("HB_Portal", "Hub/Portal", m =>
            {
                m.SetTexture("_SwirlTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/twirl_01.png"));
                m.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/smoke_04.png"));
                m.SetFloat("_Aspect", doorB.size.x / doorB.size.y);
            });
            var qr = q.GetComponent<MeshRenderer>(); qr.sharedMaterial = pm; qr.shadowCastingMode = ShadowCastingMode.Off;
            sg.portal = q.transform; sg.portalRenderer = qr;
            sg.portalLight = PointLight("PortalLight", gate, new Vector3(doorB.center.x, 1.6f, doorB.min.z - 1.2f), Hex("#3fe6ff"), 3.2f, 9f);
            sg.portalLightMax = 3.2f;
            // burn burst: sparks + paper bits from the talismans
            var burst = FireFX(gate, "TalismanBurn", new Vector3(doorB.center.x, doorB.center.y, doorB.min.z - 0.2f), Hex("#ffb347"), 0.5f, false);
            var bm = burst.main; bm.loop = false; bm.playOnAwake = false; bm.duration = 0.6f;
            var be = burst.emission; be.rateOverTime = 0; be.SetBursts(new[] { new ParticleSystem.Burst(0f, 40) });
            var bs = burst.shape; bs.shapeType = ParticleSystemShapeType.Box; bs.scale = new Vector3(doorB.size.x * 0.8f, doorB.size.y * 0.6f, 0.1f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f); bm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            sg.burst = new[] { burst };
            // staged burn (v15): a flame emitter and an ash emitter the gate fires at whatever is burning
            var sf = FireFX(gate, "SealFire", doorB.center, Hex("#ff8a3a"), 0.4f);
            var sfm = sf.main; sfm.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f); sfm.maxParticles = 600; sfm.gravityModifier = -0.15f;
            var sfe = sf.emission; sfe.rateOverTime = 0f;
            sg.fireFx = sf;
            var ash = new GameObject("SealAsh").AddComponent<ParticleSystem>(); ash.transform.SetParent(gate, false); ash.transform.position = doorB.center;
            var am = ash.main; am.loop = true; am.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f); am.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            am.gravityModifier = 0.12f; am.maxParticles = 800; am.simulationSpace = ParticleSystemSimulationSpace.World; am.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            am.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.11f, 0.1f, 0.9f), new Color(0.3f, 0.27f, 0.25f, 0.9f));
            var ae = ash.emission; ae.rateOverTime = 0f;
            var an = ash.noise; an.enabled = true; an.strength = 0.4f; an.frequency = 0.6f;
            var arot = ash.rotationOverLifetime; arot.enabled = true; arot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var acol = ash.colorOverLifetime; acol.enabled = true; var ag = new Gradient(); ag.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.45f, 0.15f), 0f), new GradientColorKey(Color.white, 0.25f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }); acol.color = ag;
            var ar = ash.GetComponent<ParticleSystemRenderer>(); ar.shadowCastingMode = ShadowCastingMode.Off;
            ar.sharedMaterial = Mat("HB_Ash", "Underworld/Particle", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/smoke_04.png"));
                m.SetColor("_Tint", Color.white); m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SoftFade", 0.2f); m.SetFloat("_AlphaFromRed", 1f);
            });
            sg.ashFx = ash;
            sg.daisGlow = Dais(gate, doorB, true);
            var trig = new GameObject("PortalEnter"); trig.transform.SetParent(gate, true);
            trig.transform.position = new Vector3(doorB.center.x, 1f, doorB.min.z + 0.3f);
            var tc = trig.AddComponent<BoxCollider>(); tc.isTrigger = true; tc.size = new Vector3(doorB.size.x * 0.8f, 2f, 1.2f);
            sg.enter = trig.AddComponent<HubPortalTrigger>();
        }

        // ------------------------------------------------------------------ fire / glow particles (downloaded flipbook)
        static Material FlameMat(string name, Color tint)
        {
            const string H = "Assets/Underworld/HellFX/";
            return Mat(name, "Underworld/Particle", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(H + "Textures/T_FireFlipbook_16x5.png"));
                m.SetColor("_Tint", tint); m.SetFloat("_Src", (float)BlendMode.One); m.SetFloat("_Dst", (float)BlendMode.One);
                m.SetFloat("_SoftFade", 0.3f); m.SetFloat("_AlphaFromRed", 1f);
            });
        }
        static ParticleSystem FireFX(Transform parent, string name, Vector3 pos, Color col, float size, bool flipbook = true)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, true); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f); main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.05f; main.maxParticles = 60;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 10f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = size * 0.15f;
            var col2 = ps.colorOverLifetime; col2.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col2.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
            if (flipbook)
            {
                var tsa = ps.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 16; tsa.numTilesY = 5;
                r.sharedMaterial = FlameMat("HB_Flame_" + ColorUtility.ToHtmlStringRGB(col), col * 2.4f);
            }
            else
            {
                const string H = "Assets/Underworld/HellFX/";
                r.sharedMaterial = Mat("HB_Spark", "Underworld/Particle", m =>
                {
                    m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(H + "Textures/T_FireSpark.png"));
                    m.SetColor("_Tint", new Color(1f, 0.6f, 0.25f) * 3f); m.SetFloat("_Src", (float)BlendMode.One); m.SetFloat("_Dst", (float)BlendMode.One);
                    m.SetFloat("_SoftFade", 0.1f); m.SetFloat("_AlphaFromRed", 0f);
                });
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f); main.gravityModifier = 0.4f;
            }
            return ps;
        }

        // ------------------------------------------------------------------ centre: 팔괘 scan + court
        static void BuildCentre()
        {
            var g = Group("Centre");
            // v3: the floor circle is the boss-seal carving in HB_Floor (Hub/Ground); the stubfish scan is no longer placed
        }

        // ------------------------------------------------------------------ west: lanterns + scholar desk
        static void BuildWest()
        {
            var g = Group("West");
            var lanternMat = Mat("HB_Lantern", "RoomRun/VColLit", m => { if (m.HasProperty("_Tint")) m.SetColor("_Tint", new Color(0.62f, 0.61f, 0.6f)); });
            foreach (var z in new[] { 4.4f, -4.6f })
            {
                var p = new Vector3(-13.0f, 0, z);
                var lt = Place("Assets/Game/RoomRun/Resources/StageLantern/KoreanStoneLantern.fbx", "StoneLantern", g, p, 90f, 2.6f, 1, 0.17f);
                SkinAll(lt, lanternMat);
                if (lt)
                {
                    var b = Bounds(lt);
                    // flame sits in the firebox, ~62% up the lantern
                    var fp = new Vector3(b.center.x, b.min.y + b.size.y * 0.62f, b.center.z);
                    FireFX(g, "CyanFlame", fp, Hex("#46e8ff"), 0.45f);
                    Halo(g, fp, 2.2f, Hex("#2fd6f0"));   // v8: soft lantern halo
                    PointLight("LanternLight", g, fp + Vector3.right * 0.6f, Hex("#4fe3ff"), 2.4f, 8.5f);
                    Solid(lt, 0.6f, 2.5f);
                }
            }
            var sd = Group("ScholarDesk", g);
            sd.position = DeskPos;
            var desk = Place(TP + "HB_Desk/HB_Desk.fbx", "Desk", sd, DeskPos, 90f, 1.05f, 1);   // v6b: top = anchor + 0.235 for the calligraphy brush hold (depth ~0.54)
            Skin(desk, "HB_Desk", Color.white * 0.85f, 0.3f);
            var chair = Place(TP + "HB_Chair/HB_Chair.fbx", "Chair", sd, ChairPos, 90f, 1.2f * DeskScale, 1);
            Skin(chair, "HB_Chair", Color.white * 0.85f, 0.3f);
            // the NPC anchor: seat surface, facing +X (into the court)
            var anchor = new GameObject("NPC_Scholar").transform; anchor.SetParent(sd, false);
            anchor.position = new Vector3(ChairPos.x, 0.48f * DeskScale, ChairPos.z);   // unscaled anchor; the prefab carries its own 1.3
            anchor.rotation = Quaternion.Euler(0, 90f, 0);
            var npc = AssetDatabase.LoadAssetAtPath<GameObject>(ScholarPrefab);
            if (npc) { var n = (GameObject)PrefabUtility.InstantiatePrefab(npc); n.transform.SetParent(anchor, false); Log("scholar NPC placed"); }
            if (desk) { var db = Bounds(desk); Log($"desk bounds {db.center} size {db.size} top {db.max.y:0.00} near edge x {db.min.x:0.00}"); }
            // desk candle (Poly Haven wooden candlestick, in DarkCrypt) + warm light
            var cand = Place("Assets/Game/DarkCrypt/Source/PH_wooden_candlestick/wooden_candlestick_2k.fbx", "Candle", sd, DeskPos + new Vector3(0.05f, 0f, 0.9f), 0f, 0.4f, 1, 0f, desk ? Bounds(desk).max.y : 0.78f);
            PointLight("DeskLight", sd, DeskPos + new Vector3(0.9f, 1.9f, 0.4f), Hex("#ffb36b"), 3.4f, 6.5f);   // v3: lights the scholar's front (black robe)
            if (desk) Solid(desk, 0f, 1f);
            // books and scrolls on the floor at the south end
            var books = Place(TP + "HB_Books/HB_Books.fbx", "Books", sd, DeskPos + new Vector3(0.3f, 0f, -1.6f), 20f, 0.45f, 1);
            Skin(books, "HB_Books", Color.white * 0.8f);
            for (int i = 0; i < 4; i++)
            {
                var s = Place(TP + "HB_Scroll/HB_Scroll.fbx", "Scroll", sd, DeskPos + new Vector3(0.9f + R(-0.15f, 0.15f), 0f, -1.4f + R(-0.25f, 0.25f)), R(-30f, 30f) + (i % 2) * 70f, 0.55f, 0, 0f, i < 2 ? 0f : 0.09f);
                Skin(s, "HB_Scroll", Color.white * 0.85f);
            }
            var s2 = Place(TP + "HB_Scroll/HB_Scroll.fbx", "ScrollDesk", sd, DeskPos + new Vector3(0.05f, 0f, -0.55f), 5f, 0.5f, 0, 0f, desk ? Bounds(desk).max.y : 0.78f);
            Skin(s2, "HB_Scroll", Color.white * 0.85f);
        }

        // ------------------------------------------------------------------ east: blacksmith
        static void BuildEast()
        {
            var g = Group("East");
            var smithy = Place(TP + "HB_Smithy/HB_Smithy.fbx", "Forge", g, new Vector3(12.6f, 0, 0.6f), 0f, 3.4f, 1);   // v3 (Kido): the whole forge turned so the furnace faces west
            Skin(smithy, "HB_Smithy", Color.white);
            if (smithy)
            {
                var b = Bounds(smithy);
                Log($"smithy bounds {b.center} size {b.size}");
                // the hearth bed (the model's own ember sparkles) sits 35% in from the chimney side, 21% up: the fire goes there (v4 fix after the turn)
                var fp = new Vector3(b.max.x - 0.35f * b.size.x, b.min.y + 0.21f * b.size.y + 0.08f, b.center.z + 0.05f);
                // v10 (Kido: "화구 안에 불 붉게, 재랑 색 맞추고, 화구 입구 안 넘어가게"): a deep-red bed fire that starts below the
                // hearth rim and stays inside the mouth: low flames spread over the bed (box emitter the size of the opening),
                // short life, slow rise; colour of the glowing ash (dark red-orange), no yellow tips
                var bed = fp + new Vector3(-0.22f, -0.02f, -0.05f);   // centre of the hearth box opening (close shot 14:35)
                var ff = FireFX(g, "ForgeFire", bed, Hex("#d2401c") * 0.8f, 0.42f);
                var fm = ff.main; fm.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f); fm.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f); fm.gravityModifier = -0.02f; fm.maxParticles = 40;
                var fe = ff.emission; fe.rateOverTime = 26f;
                var fs = ff.shape; fs.shapeType = ParticleSystemShapeType.Box; fs.scale = new Vector3(0.6f, 0.05f, 0.55f);
                var fz = ff.sizeOverLifetime; fz.enabled = true; fz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(0.4f, 1f), new Keyframe(1, 0.35f)));
                var sp = FireFX(g, "ForgeSparks", fp + Vector3.up * 0.1f, Hex("#ff6a2a"), 0.3f, false); var sm = sp.emission; sm.rateOverTime = 2.5f;
                var sps = sp.shape; sps.shapeType = ParticleSystemShapeType.Box; sps.scale = new Vector3(0.5f, 0.05f, 0.45f);
                PointLight("ForgeLight", g, fp + new Vector3(-0.9f, 0.4f, 0f), Hex("#ff5a26"), 4.2f, 10f);
                // v5 (Kido: "광원줘서 어둡지 않게"): a warm work light over the smith + anvil, from the camera side so his front reads
                PointLight("SmithWorkLight", g, new Vector3(9.8f, 2.4f, -0.3f), Hex("#ffb070"), 5.5f, 7.5f);
                Solid(smithy, 0.3f, 3f);
            }
            var anvil = Place(TP + "HB_Anvil/HB_Anvil.fbx", "Anvil", g, AnvilPos, 90f, 0.98f, 1);   // top 0.98 m for the ~2.1 m goblin smith (v4; was 0.62 for 1.35 m)
            Skin(anvil, "HB_Anvil", Color.white * 0.9f, 0.35f); Solid(anvil, 0.1f, 1.1f);
            // goblin smith anchor: stands north of the anvil facing it (-Z), so the game camera sees his front
            var sa = new GameObject("NPC_GoblinSmith").transform; sa.SetParent(g, false);
            sa.position = SmithPos; sa.rotation = Quaternion.Euler(0f, 270f, 0f);   // forward -X: screen-left in the game camera (yaw 0, looking +Z)
            var gob = AssetDatabase.LoadAssetAtPath<GameObject>(GoblinPrefab);
            if (gob)
            {
                var n = (GameObject)PrefabUtility.InstantiatePrefab(gob); n.transform.SetParent(sa, false);
                var an = n.transform.Find("Anvil"); if (an && anvil) an.gameObject.SetActive(false);
                // 11-20: the smith brings his own anvil ("SmithAnvil") where his hammer lands -> the hub's own anvil goes
                if (anvil && n.GetComponentsInChildren<Transform>(true).Any(t => t.name == "SmithAnvil"))
                {
                    foreach (var c in g.GetComponentsInChildren<BoxCollider>().Where(c => c.name == "Collider" && Vector3.Distance(new Vector3(c.transform.position.x, 0, c.transform.position.z), AnvilPos) < 0.6f).ToArray()) Object.DestroyImmediate(c.gameObject);
                    Object.DestroyImmediate(anvil); anvil = null; Log("hub anvil removed (SmithAnvil in the prefab)");
                }
                Log("goblin smith placed");
            }
            if (anvil) { var ab = Bounds(anvil); Log($"anvil bounds {ab.center} size {ab.size} top {ab.max.y:0.00}"); }
            var hammer = Place(TP + "HB_Hammer/HB_Hammer.fbx", "Hammer", g, new Vector3(10.5f, 0, -1.2f), 30f, 0.45f, 3, 0f, 0.03f);
            if (hammer) { hammer.transform.rotation = Quaternion.Euler(0, 30, 90) ; var hb = Bounds(hammer); hammer.transform.position += Vector3.up * (0.01f - hb.min.y); Skin(hammer, "HB_Hammer", Color.white); }
            var rack = Place(TP + "HB_ToolRack/HB_ToolRack.fbx", "ToolRack", g, new Vector3(13.0f, 0, 5.0f), -90f, 2.4f, 1);   // against the east wall, clear of the right gate step
            Skin(rack, "HB_ToolRack", Color.white * 0.9f); Solid(rack, 0.2f, 2.4f);
            for (int i = 0; i < 2; i++)
            {
                var gear = Place(TP + "HB_Gear/HB_Gear.fbx", "Gear", g, new Vector3(13.4f, 0, -3.0f - i * 1.1f), -90f + i * 14f, 1.1f - i * 0.25f, 3);
                if (gear)
                {
                    gear.transform.rotation = Quaternion.Euler(-72f, -90f + i * 14f, 0f);
                    var gb = Bounds(gear); gear.transform.position += new Vector3(13.6f - gb.center.x, -gb.min.y, 0f);
                    Skin(gear, "HB_Gear", Hex("#9a8a76"), 0.35f);
                }
            }
            var wr = Place(TP + "HB_WeaponRack/HB_WeaponRack.fbx", "WeaponRack", g, new Vector3(13.3f, 0, -5.9f), -90f, 2.2f, 0);
            Skin(wr, "HB_WeaponRack", Color.white * 0.85f); Solid(wr, 0.1f, 1.4f);
            if (wr)
            {
                var b = Bounds(wr);
                string[] ws = { "PP_Spear", "PP_Hwando", "PP_Woldo", "PP_Spear" };
                for (int i = 0; i < ws.Length; i++)
                {
                    var path = $"Assets/Game/RoomRun/Resources/EdgeFeatures/{ws[i]}.prefab";
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (!src) { Log("MISSING " + path); continue; }
                    var w = (GameObject)PrefabUtility.InstantiatePrefab(src); w.transform.SetParent(g, false);
                    w.transform.rotation = Quaternion.identity; w.transform.localScale = Vector3.one;
                    var wb0 = Bounds(w); float longest = Mathf.Max(wb0.size.x, wb0.size.y, wb0.size.z);
                    w.transform.localScale = Vector3.one * ((ws[i] == "PP_Hwando" ? 1.0f : 2.3f) / Mathf.Max(0.01f, longest));
                    // stand it upright, leaning back against the wall
                    var wb = Bounds(w);
                    if (wb.size.y < Mathf.Max(wb.size.x, wb.size.z)) w.transform.rotation = Quaternion.Euler(0, 0, 90) * w.transform.rotation;
                    w.transform.rotation = Quaternion.Euler(0, 0, -10f) * w.transform.rotation;
                    wb = Bounds(w);
                    float zz = Mathf.Lerp(b.min.z + 0.3f, b.max.z - 0.3f, (i + 0.5f) / ws.Length);
                    w.transform.position += new Vector3(b.center.x + 0.15f - wb.center.x, 0.02f - wb.min.y, zz - wb.center.z);
                    foreach (var c in w.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                }
            }
        }

        // ------------------------------------------------------------------ south: drum + scarecrow slot
        static void BuildSouth()
        {
            var g = Group("South");
            var drum = Place(TP + "HB_Drum/HB_Drum.fbx", "Drum_Kyobanggo", g, new Vector3(-12.3f, 0, -7.4f), 35f, 1.5f, 1);   // v8 (Kido): bottom-left (south-west) corner
            Skin(drum, "HB_Drum", Color.white * 0.8f, 0.25f); Solid(drum, 0.2f, 1.6f);
            // Kido makes the scarecrow: empty slot only (gizmo in the editor, nothing in game)
            var slot = new GameObject("Scarecrow_Slot"); slot.transform.SetParent(g, false); slot.transform.position = new Vector3(SealCentre.x, 0, -6.9f); slot.transform.rotation = Quaternion.Euler(0f, 180f, 0f);   // masked face toward the camera; 11-20 Kido: on the centre line of the 팔괘 under its lower edge (the centre itself is the portal return spot)
            var sc = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Scarecrow/Scarecrow.prefab");
            if (sc) { var n = (GameObject)PrefabUtility.InstantiatePrefab(sc); n.transform.SetParent(slot.transform, false); Log("scarecrow placed"); }
            var ns = new GameObject("NPC_Slots"); ns.transform.SetParent(root, false);
        }

        // ------------------------------------------------------------------ v4 set dressing (Kido: "환경 규칙으로 채워")
        // D4 GDC rules: hand-placed clusters per zone (no noise scatter), filler weeds as clumps along the wall feet (the
        // walkable edge reads), broken things everywhere (fallen roof tiles, rubble), doors / steps / NPC spots / the
        // walk lanes from the centre to the gates kept clear. All assets already in the project (AbandonedVillage
        // Megascans + Poly Haven; credited in AbandonedVillage).
        const string AVM = "Assets/Game/AbandonedVillage/ThirdParty/Megascans/";
        const string AVP = "Assets/Game/AbandonedVillage/ThirdParty/PolyHaven/";
        static Material DressMat(string fbxPath, string slotName, bool cutout)
        {
            var dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            var files = Directory.GetFiles(dir).Select(f => f.Replace('\\', '/')).Where(f => !f.EndsWith(".meta")).ToArray();
            string sn = (slotName ?? "").ToLowerInvariant();
            string Pick(params string[] keys) => files.FirstOrDefault(f => keys.All(k => Path.GetFileName(f).ToLowerInvariant().Contains(k)));
            string alb = (sn.Length > 0 ? Pick(sn, "diff") : null) ?? Pick("_rgba") ?? Pick("diff") ?? Pick("_a.");
            string nor = (sn.Length > 0 ? Pick(sn, "nor") : null) ?? Pick("nor_gl") ?? Pick("_n.");
            string key = "HB_D_" + Path.GetFileNameWithoutExtension(alb ?? fbxPath);
            return Mat(key, "Universal Render Pipeline/Lit", m =>
            {
                if (alb != null) m.SetTexture("_BaseMap", Tex(alb));
                if (nor != null) { var n = Tex(nor, normal: true); if (n) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 0.6f); m.EnableKeyword("_NORMALMAP"); } }
                m.SetColor("_BaseColor", Hex("#a7a39e"));   // pulled into the court's value range
                m.SetFloat("_Smoothness", 0.12f); m.SetFloat("_Metallic", 0f);
                if (cutout) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cull", 0f); m.SetColor("_BaseColor", Hex("#8f8a78")); }
                m.enableInstancing = true;
            });
        }
        static GameObject Dress(Transform g, string fbx, float x, float z, float yaw, float size, int axis = 3, bool cutout = false, float sink = 0f, float tiltX = 0f, float tiltZ = 0f)
        {
            var go = Place(fbx, Path.GetFileNameWithoutExtension(fbx), g, new Vector3(x, 0, z), yaw, size, axis, sink);
            if (!go) return null;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) ms[i] = DressMat(fbx, ms[i] ? ms[i].name : null, cutout);
                r.sharedMaterials = ms;
                if (cutout) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (tiltX != 0f || tiltZ != 0f)
            {
                go.transform.rotation = Quaternion.Euler(tiltX, 0, tiltZ) * go.transform.rotation;
                var b = Bounds(go); go.transform.position += Vector3.up * (-b.min.y - sink * b.size.y);
            }
            return go;
        }
        // a clump of weeds: 3-5 tufts around a point, all turned the same way (one flow direction for the whole court)
        static readonly string[] DryWeeds = { "WildDry/AV_MS_WildDry_VarA.fbx", "WildDry/AV_MS_WildDry_VarC.fbx", "WildDry/AV_MS_WildDry_VarE.fbx", "DryGrass/AV_MS_DryGrass_VarB.fbx", "Clumps/AV_MS_Clumps_VarA.fbx" };
        static readonly string[] GreenWeeds = { "WildGreen/AV_MS_WildGreen_VarA.fbx", "WildGreen/AV_MS_WildGreen_VarC.fbx", "Clumps/AV_MS_Clumps_VarB.fbx" };
        static void Weeds(Transform g, float x, float z, int n, float spread, bool green = false)
        {
            var set = green ? GreenWeeds : DryWeeds;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.39f + (float)rng.NextDouble(), d = spread * Mathf.Sqrt((i + 0.5f) / n);
                Dress(g, AVM + set[rng.Next(set.Length)], x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, 35f + R(-15f, 15f), R(0.7f, 1.1f), 1, true);   // v4b: clumps read as masses from the game camera
            }
        }

        // v7 (Kido: "바닥 타일 너무 일정"): downloaded Megascans dirt / mud decals (RoomRun Resources/Decals, darkened
        // RR_DecalOpacity) in clusters where dirt gathers: wall feet, the forge, the scarecrow ground, the gate steps' edges.
        static void Decals(Transform g)
        {
            var dg = Group("Decals", g);
            void D(string mat, float x, float z, float w, float l, float yaw)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/RoomRun/Resources/Decals/" + mat + ".mat");
                if (!m) { Log("MISSING decal " + mat); return; }
                var go = new GameObject("Decal_" + mat); go.transform.SetParent(dg, false);
                go.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(90f, yaw, 0f));
                var d = go.AddComponent<DecalProjector>();
                d.material = m; d.pivot = Vector3.zero; d.size = new Vector3(w, l, 1.2f); d.fadeFactor = 0.85f; d.drawDistance = 1000f;
            }
            var spots = new (string m, float x, float z, float w, float yaw)[]
            {
                ("RRD_DirtPatch", -12.8f, 5.6f, 3.4f, 20f), ("RRD_MudSplash", -13.5f, -6.8f, 2.6f, 130f), ("RRD_DirtPatch", -7.0f, -8.4f, 3.8f, 70f),
                ("RRD_DirtPatch", 6.6f, -8.2f, 3.2f, 200f), ("RRD_MudSplash", 11.2f, -1.2f, 3.0f, 40f), ("RRD_DirtPatch", 12.2f, -6.0f, 3.6f, 310f),
                ("RRD_Charcoal", 11.0f, 0.6f, 2.4f, 15f), ("RRD_DirtPatch", -0.4f, -7.6f, 3.0f, 160f), ("RRD_MudSplash", 3.6f, 6.0f, 2.2f, 80f),
                ("RRD_DirtPatch", -4.4f, 6.4f, 2.6f, 250f), ("RRD_MudSplash", 7.4f, 3.2f, 1.8f, 300f), ("RRD_DirtPatch", -9.0f, -3.4f, 2.4f, 10f),
            };
            foreach (var sp in spots) D(sp.m, sp.x, sp.z, sp.w, sp.w * 0.8f, sp.yaw);
        }

        static void BuildDressing()
        {
            var g = Group("Dressing");
            var wall = Group("WallFeet", g);
            // wall feet: weed clumps where the floor meets the wall (west / east / north between gates / south)
            foreach (var z in new[] { 6.6f, 2.8f, -5.8f, -8.6f }) Weeds(wall, -14.4f, z, 4, 0.6f, z > 0f);
            foreach (var z in new[] { 7.0f, -3.2f, -8.4f }) Weeds(wall, 14.4f, z, 4, 0.6f);
            foreach (var x in new[] { -13.6f, -4.6f, 4.5f, 13.4f }) Weeds(wall, x, 8.1f, 3, 0.5f, x < 0);
            foreach (var x in new[] { -12.5f, -5.5f, 5.2f, 12.6f }) Weeds(wall, x, -9.4f, 4, 0.7f);
            // corners: fallen roof tiles from the wall caps, rubble, small rocks (broken things)
            var cor = Group("Corners", g);
            foreach (var (cx, cz, s) in new[] { (-14.2f, 7.8f, 1f), (14.3f, -9.2f, -1f), (-14.0f, -9.3f, 1f) })
            {
                Dress(cor, AVM + "RubblePatch/AV_MS_RubblePatch.fbx", cx, cz, R(0, 360), 1.8f, 3, false, 0.35f);
                Dress(cor, AVM + "KTileFace/AV_MS_KTileFace.fbx", cx + 0.9f * s, cz - 0.4f * s, R(0, 360), 0.32f, 3, false, 0f, R(-25f, 25f), R(60f, 85f));
                Dress(cor, AVM + "Tile/AV_MS_Tile.fbx", cx + 0.5f * s, cz - 0.9f * s, R(0, 360), 0.38f, 3, false, 0.1f, R(-20f, 20f), R(-15f, 15f));
                Dress(cor, AVM + "Tile/AV_MS_Tile.fbx", cx + 1.2f * s, cz + 0.2f * s, R(0, 360), 0.36f, 3, false, 0.2f);
                Dress(cor, AVM + "SmallRocks/AV_MS_SmallRocks.fbx", cx - 0.2f * s, cz - 1.3f * s, R(0, 360), 0.9f, 3, false, 0.3f);
            }
            // blacksmith corner: barrels, crates, water bucket (quench), firewood, a sack of charcoal, planks
            var bs = Group("Smithy", g);
            Dress(bs, AVP + "wooden_barrels_01/wooden_barrels_01.fbx", 13.6f, -2.6f, 20f, 1.25f, 1);
            Dress(bs, AVP + "wooden_crate_01/wooden_crate_01.fbx", 13.5f, -4.4f, 12f, 0.75f, 1);
            var top = Dress(bs, AVP + "wooden_crate_01/wooden_crate_01.fbx", 13.6f, -4.35f, 40f, 0.6f, 1);
            if (top) top.transform.position += Vector3.up * 0.74f;
            Dress(bs, AVP + "wooden_bucket_01/wooden_bucket_01.fbx", 10.7f, 0.2f, 0f, 0.45f, 1);
            Dress(bs, AVM + "Sticks/AV_MS_Sticks.fbx", 11.2f, -1.8f, 75f, 1.2f, 3, false, 0.2f);
            Dress(bs, AVM + "Sack/AV_MS_Sack.fbx", 12.4f, -2.9f, 30f, 0.7f, 3);
            Dress(bs, AVM + "Planks/AV_MS_Planks.fbx", 13.9f, 3.9f, 90f, 1.8f, 3, false, 0f, 0f, 70f);
            Dress(bs, AVM + "Pebbles/AV_MS_Pebbles.fbx", 11.0f, -0.9f, R(0, 360), 1.2f, 3, false, 0.4f);
            // scholar corner: jars and a basket by the wall, a soban, the straw mat under the chair
            var sc = Group("Scholar", g);
            Dress(sc, AVM + "KStrawMat/AV_MS_KStrawMat.fbx", -10.5f, 0.6f, 90f, 2.6f, 3, false, 0.6f);
            Dress(sc, AVM + "KJar/AV_MS_KJar.fbx", -14.0f, -2.4f, 0f, 0.8f, 1);
            Dress(sc, AVM + "KJar/AV_MS_KJar.fbx", -14.2f, -3.2f, 70f, 0.6f, 1);
            Dress(sc, AVM + "KBasket/AV_MS_KBasket.fbx", -13.2f, 3.0f, 20f, 0.6f, 3);
            Dress(sc, AVM + "KSoban/AV_MS_KSoban.fbx", -12.6f, -1.9f, 15f, 0.8f, 3);
            // south edge: straw and sacks by the scarecrow (training ground), a broom against the wall, a pot and a jar
            var so = Group("South", g);
            // v8 (Kido: "허수아비 아래 이상한 오브젝트 치우고"): the straw bundle under the scarecrow removed
            Dress(so, AVM + "KStrawBag/AV_MS_KStrawBag.fbx", -2.6f, -8.6f, 20f, 0.8f, 3);
            Dress(so, AVM + "Sack/AV_MS_Sack.fbx", -3.3f, -8.9f, 70f, 0.7f, 3);
            Dress(so, AVM + "KBroom/AV_MS_KBroom.fbx", 5.6f, -9.5f, 0f, 1.5f, 3, false, 0f, -18f, 0f);
            Dress(so, AVM + "Pot/AV_MS_Pot.fbx", 7.0f, -9.0f, 30f, 0.7f, 1);
            Dress(so, AVM + "KJar/AV_MS_KJar.fbx", 7.8f, -9.2f, 80f, 0.85f, 1);
            Decals(g);
            Log($"dressing {g.GetComponentsInChildren<Renderer>().Length} renderers");
        }

        // ------------------------------------------------------------------ low fog (Kenney smoke, CC0)
        static void BuildFog()
        {
            var go = new GameObject("GroundFog"); go.transform.SetParent(root, false); go.transform.position = new Vector3(0, 0.35f, -1.4f);   // v9: kept off the gate band
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = 16f; main.startSpeed = 0.05f;
            // v8 (Kido: "움직이는 안개"): the sheets drift west->east and curl slowly
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(0.25f, 0.45f); vel.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 0.08f; nz.scrollSpeed = 0.05f; nz.damping = true;
            main.startSize = new ParticleSystem.MinMaxCurve(5f, 9f); main.maxParticles = 60; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 3.5f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(24f, 0.2f, 10f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; r.shadowCastingMode = ShadowCastingMode.Off;
            r.sharedMaterial = Mat("HB_Fog", "Underworld/Particle", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/smoke_04.png"));
                m.SetColor("_Tint", new Color(0.62f, 0.78f, 0.82f, 0.11f));   // v9: lighter teal haze, not dark smoke m.SetFloat("_Src", (float)BlendMode.SrcAlpha); m.SetFloat("_Dst", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SoftFade", 1.2f); m.SetFloat("_AlphaFromRed", 1f);
            });
        }

        // v8 (Kido: "도깨비불 같은 거 움직이고"): a few dim cyan will-o'-wisps wandering slowly round the court (Kenney
        // light_01, CC0). Particles only (no extra lights); bloom carries the glow.
        static void Halo(Transform g, Vector3 p, float size, Color c)
        {
            var go = new GameObject("Halo"); go.transform.SetParent(g, true); go.transform.position = p;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = 4f; main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(size * 0.9f, size * 1.1f);
            main.maxParticles = 3; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = c;
            var em = ps.emission; em.rateOverTime = 0.75f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
            r.sharedMaterial = Mat("HB_Halo", "Underworld/Particle", m =>
            {
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/RoomRun/Resources/VFX/light_02.png"));
                m.SetColor("_Tint", Color.white * 1.2f); m.SetFloat("_Src", (float)BlendMode.One); m.SetFloat("_Dst", (float)BlendMode.One);
                m.SetFloat("_SoftFade", 0.8f); m.SetFloat("_AlphaFromRed", 1f);
            });
        }

        // v8: spirit motes - slow-rising tiny lights over the whole court
        static void BuildMotes()
        {
            var go = new GameObject("SpiritMotes"); go.transform.SetParent(root, false); go.transform.position = new Vector3(0, 0.3f, -0.6f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = 0f; main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f); main.maxParticles = 90; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 9f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, 0.4f, 15f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); vel.y = new ParticleSystem.MinMaxCurve(0.18f, 0.35f); vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
            var nz = ps.noise; nz.enabled = true; nz.strength = 0.3f; nz.frequency = 0.4f; nz.scrollSpeed = 0.2f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Hex("#bff8ff"), 0f), new GradientColorKey(Hex("#5fe0f0"), 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.2f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off;
            r.sharedMaterial = Mat("HB_Wisp", "Underworld/Particle", m => { });
        }

        // v9 (Kido: "귀신불 ... 너가 만들지 말고 어디서 받아서"): ghost fires = the downloaded Fab "Fire VFX - Lite -
        // Surface Burning" flipbook (Skulmovski Studio, Underworld/HellFX) burning cyan, a handful drifting slowly round the
        // court. No code-drawn dots or trails.
        static void BuildWisps()
        {
            // v10 (Kido: download it): Wallcoeur "Fire Toon Particle" (itch.io, free, Assets/VFXPACK_FIRE_WALLCOEUR), its green torch
            // fire, drifting on HubWispWander paths. The pack's materials use legacy built-in particle shaders (pink in URP), so
            // the renderers get Underworld/Particle copies with the pack's own textures and blend modes.
            const string W = "Assets/VFXPACK_FIRE_WALLCOEUR/";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(W + "Prefab/VFX_TorchLight_Green.prefab");
            if (!src) { Log("MISSING wallcoeur pack"); return; }
            var g = Group("GhostFires");
            var spots = new[] { new Vector3(-6.5f, 1.4f, 3.2f), new Vector3(5.8f, 1.6f, 3.6f), new Vector3(-4.0f, 1.3f, -5.0f), new Vector3(6.4f, 1.5f, -4.6f), new Vector3(-10.5f, 1.7f, -2.0f) };
            for (int k = 0; k < spots.Length; k++)
            {
                var w = (GameObject)PrefabUtility.InstantiatePrefab(src); w.transform.SetParent(g, false);
                w.transform.position = spots[k]; w.transform.localScale = Vector3.one * 0.55f;
                foreach (var r in w.GetComponentsInChildren<ParticleSystemRenderer>())
                {
                    var om = r.sharedMaterial; if (!om) continue;
                    bool add = om.shader && om.shader.name.ToLowerInvariant().Contains("add");
                    var tex = om.HasProperty("_MainTex") ? om.GetTexture("_MainTex") : null;
                    r.sharedMaterial = Mat("HB_W_" + om.name.Replace(" ", ""), "Underworld/Particle", m =>
                    {
                        if (tex) m.SetTexture("_MainTex", tex);
                        m.SetColor("_Tint", add ? new Color(0.55f, 1.25f, 1.35f) : new Color(0.45f, 0.75f, 0.8f, 0.6f));
                        m.SetFloat("_Src", add ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
                        m.SetFloat("_Dst", add ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                        m.SetFloat("_SoftFade", 0.3f); m.SetFloat("_AlphaFromRed", 0f);
                    });
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                foreach (var l in w.GetComponentsInChildren<Light>()) Object.DestroyImmediate(l);   // light budget: glow only
                var wd = w.AddComponent<HubWispWander>(); wd.centre = spots[k]; wd.seed = k * 1.7f; wd.radius = new Vector2(2.6f, 1.8f);
            }
            Log($"ghost fires {spots.Length} (wallcoeur VFX_TorchLight_Green)");
        }

        // ------------------------------------------------------------------ light, post, camera
        static void BuildLightAndCamera()
        {
            var key = new GameObject("MoonLight").AddComponent<Light>();
            key.transform.SetParent(root, false);
            key.type = LightType.Directional; key.color = Hex("#8fbccb"); key.intensity = 1.5f;   // v8: teal moonlight (mystic)
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.75f; key.lightmapBakeType = LightmapBakeType.Realtime;
            key.transform.rotation = Quaternion.Euler(52f, 140f, 0f);
            lightCount++;
            RenderSettings.sun = key;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#56646f");
            RenderSettings.ambientEquatorColor = Hex("#343d44");
            RenderSettings.ambientGroundColor = Hex("#15181b");
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.reflectionIntensity = 0.2f;
            var lsPath = Root + "/HB_Lighting.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls) { ls = new LightingSettings { name = "HB_Lighting" }; AssetDatabase.CreateAsset(ls, lsPath); }
            ls.bakedGI = false; ls.realtimeGI = false;
            Lightmapping.lightingSettings = ls;

            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.transform.SetParent(root, false); vol.isGlobal = true; vol.priority = 0;
            var path = Root + "/HB_PostFX.asset";
            AssetDatabase.DeleteAsset(path);
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, path);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var ca = prof.Add<ColorAdjustments>(true); ca.contrast.Override(12f); ca.saturation.Override(-8f);
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(1.0f); bl.intensity.Override(0.6f); bl.scatter.Override(0.65f);
            var vg = prof.Add<Vignette>(true); vg.color.Override(Hex("#05080a")); vg.intensity.Override(0.32f); vg.smoothness.Override(0.5f);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            vol.sharedProfile = prof;
            // v8 (Kido: "좀더 신비한 느낌"): the hub keeps more of its colour than the shared crisp grade allows
            // (CrispLook p1300 sat -60 bleached the cyan portal / lanterns): own volume above it, saturation + teal split only
            var mv = new GameObject("HubMysticGrade").AddComponent<Volume>(); mv.transform.SetParent(root, false); mv.isGlobal = true; mv.priority = 1400;
            var mpath = Root + "/HB_Mystic.asset";
            AssetDatabase.DeleteAsset(mpath);
            var mp = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(mp, mpath);
            var mca = mp.Add<ColorAdjustments>(true); mca.saturation.Override(-18f);
            var mst = mp.Add<SplitToning>(true); mst.shadows.Override(new Color(0.36f, 0.56f, 0.6f)); mst.highlights.Override(new Color(0.62f, 0.56f, 0.5f)); mst.balance.Override(-15f);
            var mbl = mp.Add<Bloom>(true); mbl.threshold.Override(0.95f); mbl.intensity.Override(0.75f); mbl.scatter.Override(0.72f); mbl.tint.Override(new Color(0.75f, 0.95f, 1f));
            foreach (var c in mp.components) AssetDatabase.AddObjectToAsset(c, mp);
            mv.sharedProfile = mp;

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 300f; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#07090b");
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true; cd.requiresDepthTexture = true;
            cam.gameObject.AddComponent<AudioListener>();
            var off = Quaternion.Euler(49.5f, 0f, 0f) * Vector3.back * 32.4f;
            cam.transform.position = off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }

        // ------------------------------------------------------------------ invisible edges
        static void BuildEdgeColliders()
        {
            var walls = Group("EdgeWalls");
            void Wall(Vector3 c, Vector3 size) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.layer = WallLayer; w.transform.position = c; w.AddComponent<BoxCollider>().size = size; }
            float w0 = InX * 2f + 2f, d0 = NorthZ - SouthZ + 2f, cz = (NorthZ + SouthZ) * 0.5f;
            Wall(new Vector3(-InX - 0.5f, 3f, cz), new Vector3(1f, 8f, d0));
            Wall(new Vector3(InX + 0.5f, 3f, cz), new Vector3(1f, 8f, d0));
            Wall(new Vector3(0, 3f, SouthZ - 0.5f), new Vector3(w0, 8f, 1f));
            // north: solid except the portal gate's doorway (HubSealGate blocks it with its own collider until open)
            Wall(new Vector3(0, 3f, NorthZ + 0.5f), new Vector3(w0, 8f, 1f));
        }

        // ------------------------------------------------------------------ RoomRun dev room
        public static void AddRoomRunRoom()
        {
            const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
            if (!cfg) { Log("NO RoomRunConfig"); return; }
            var so = new SerializedObject(cfg);
            var rooms = so.FindProperty("rooms");
            var size = new Vector2(InX * 2f, NorthZ - SouthZ);
            var centre = new Vector3(0f, 0f, (NorthZ + SouthZ) * 0.5f);
            for (int i = 0; i < rooms.arraySize; i++)
                if (rooms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == RoomId)
                {
                    var e = rooms.GetArrayElementAtIndex(i);
                    e.FindPropertyRelative("size").vector2Value = size; e.FindPropertyRelative("center").vector3Value = centre;
                    so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
                    return;
                }
            int at = rooms.arraySize;
            rooms.InsertArrayElementAtIndex(at - 1);
            var r = rooms.GetArrayElementAtIndex(at);
            r.FindPropertyRelative("id").stringValue = RoomId;
            r.FindPropertyRelative("displayName").stringValue = "거점";
            r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(12, 0);
            r.FindPropertyRelative("scenePath").stringValue = ScenePath;
            r.FindPropertyRelative("groundMeshName").stringValue = "";
            r.FindPropertyRelative("center").vector3Value = centre;
            r.FindPropertyRelative("size").vector2Value = size;
            r.FindPropertyRelative("fitToStage").boolValue = false;
            r.FindPropertyRelative("floorColor").colorValue = Hex("#2a2e33");
            r.FindPropertyRelative("pillarCount").intValue = 0;
            r.FindPropertyRelative("doors").arraySize = 0;
            r.FindPropertyRelative("waves").arraySize = 0;
            r.FindPropertyRelative("bossPrefab").objectReferenceValue = null;
            r.FindPropertyRelative("devOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Log("room 'hub' added to RoomRunConfig");
        }

        // ------------------------------------------------------------------ edit-mode shots (quick checks, not deliverables)
        // ArtSource/Hub/hub_shots.txt: "name x z zoom [pitch]" per line -> ArtSource/Hub/shots/<name>.png. zoom 1 = 32.4 m away.
        public static void Shots()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string dir = Path.GetDirectoryName(LogPath);
            string list = Path.Combine(dir, "hub_shots.txt");
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
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(6f, true, true);
            var p0 = cam.transform.position; var r0 = cam.transform.rotation;
            foreach (var raw in File.ReadAllLines(list))
            {
                var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split(' ');
                float x = float.Parse(p[1], ci), z = float.Parse(p[2], ci), zoom = float.Parse(p[3], ci);
                float pitch = p.Length > 4 ? float.Parse(p[4], ci) : 49.5f;
                var off = Quaternion.Euler(pitch, 0f, 0f) * Vector3.back * (32.4f * zoom);
                var target = new Vector3(x, 0f, z);
                cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, p[0] + ".png"), tex.EncodeToPNG());
            }
            cam.transform.position = p0; cam.transform.rotation = r0;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            ShaderUtil.allowAsyncCompilation = async0;
            Log("shots done");
        }
    }
}
