// Builds the Death's Door-style concept test from the Blender exports in Assets/Art/DDConcept/Models.
// Menu: DD > Build Concept Scene  (materials, prefabs, test scene, preview PNG)
//       DD > Capture Preview       (re-render ArtSource/unity_preview.png from the scene camera)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class DDModelPostprocessor : AssetPostprocessor
{
    static bool IsDD(string p) => p.Replace('\\', '/').Contains("/DDConcept/Models/");

    void OnPreprocessModel()
    {
        if (!IsDD(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importBlendShapes = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importAnimation = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
    }

    // Outline pass extrudes along these averaged normals so flat-shaded hulls don't crack.
    void OnPostprocessModel(GameObject g)
    {
        if (!IsDD(assetPath)) return;
        foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            BakeSmoothNormals(mf.sharedMesh);
    }

    static Vector3 Q(Vector3 v) => new Vector3(Mathf.Round(v.x * 10000f), Mathf.Round(v.y * 10000f), Mathf.Round(v.z * 10000f));

    public static void BakeSmoothNormals(Mesh m)
    {
        var v = m.vertices; var n = m.normals;
        var acc = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < v.Length; i++)
        {
            var k = Q(v[i]);
            acc.TryGetValue(k, out var s);
            acc[k] = s + n[i];
        }
        var o = new List<Vector3>(v.Length);
        for (int i = 0; i < v.Length; i++)
        {
            var s = acc[Q(v[i])];
            o.Add(s.sqrMagnitude > 1e-8f ? s.normalized : n[i]);
        }
        m.SetUVs(3, o);
    }
}

public static class DDConceptSetup
{
    const string Root = "Assets/Art/DDConcept";
    const string ScenePath = "Assets/Scenes/DDConcept.unity";
    // Blender characters face -Y; after FBX axis conversion this yaw makes them face +Z (Unity forward).
    public const float YawFix = 0f;

    struct Toon { public string hex, shadow; public float rim, stripes; }
    struct Glow { public string hex; public float intensity; }

    static readonly Dictionary<string, Toon> ToonTable = new Dictionary<string, Toon>
    {
        {"DD_Skin", new Toon{hex="#d9a982", shadow="#7a5a6a", rim=.15f}},
        {"DD_Indigo", new Toon{hex="#34425f", shadow="#4a5578", rim=.15f}},
        {"DD_Navy", new Toon{hex="#1b2131", shadow="#4a5578", rim=.15f}},
        {"DD_White", new Toon{hex="#d6d2c4", shadow="#4a5578", rim=.15f}},
        {"DD_Black", new Toon{hex="#17171b", shadow="#8088a0", rim=.15f}},
        {"DD_Red", new Toon{hex="#a3352a", shadow="#5a4060", rim=.15f}},
        {"DD_Gold", new Toon{hex="#c09a45", shadow="#4a5578", rim=.2f}},
        {"DD_Wood", new Toon{hex="#6e4a2c", shadow="#4a5578", rim=.1f}},
        {"DD_Metal", new Toon{hex="#3a3c42", shadow="#4a5578", rim=.35f}},
        {"DD_Tiger", new Toon{hex="#c27a2c", shadow="#4a5578", rim=.15f, stripes=7f}},
        {"DD_Paper", new Toon{hex="#dcc070", shadow="#4a5578", rim=.1f}},
        {"DD_Amber", new Toon{hex="#b5612a", shadow="#4a5578", rim=.1f}},
        {"DD_Gourd", new Toon{hex="#a88a4e", shadow="#4a5578", rim=.1f}},
        {"DD_EyeDark", new Toon{hex="#0b0b0e", shadow="#4a5578", rim=0f}},
        {"DD_Dokkaebi", new Toon{hex="#7c3a33", shadow="#3a3a66", rim=.15f}},
        {"DD_DokDark", new Toon{hex="#2a1c1e", shadow="#4a5578", rim=.15f}},
        {"DD_Bone", new Toon{hex="#d8ccb0", shadow="#4a5578", rim=.15f}},
        {"DD_Straw", new Toon{hex="#8f7a45", shadow="#4a5578", rim=.1f}},
        {"DD_Fox", new Toon{hex="#eadcc4", shadow="#7a6a8a", rim=.2f}},
        {"DD_FoxDark", new Toon{hex="#2a2830", shadow="#4a5578", rim=.15f}},
        {"DD_Ghost", new Toon{hex="#dfe3e6", shadow="#6a7aa0", rim=.4f}},
        {"DD_GhostSkin", new Toon{hex="#c9d0d4", shadow="#5a6a90", rim=.2f}},
        {"DD_Hair", new Toon{hex="#0a0a0e", shadow="#9098b0", rim=.25f}},
        {"DD_Shadow", new Toon{hex="#16131e", shadow="#a0a0c0", rim=.5f}},
        {"DD_Outline", new Toon{hex="#07080b", shadow="#ffffff", rim=0f}},
        // environment
        {"DD_Ground", new Toon{hex="#262a28", shadow="#4a5578", rim=0f}},
        {"DD_Slab", new Toon{hex="#4b4f52", shadow="#4a5578", rim=0f}},
        {"DD_Stone", new Toon{hex="#5a5d5c", shadow="#4a5578", rim=.05f}},
        {"DD_Hongsal", new Toon{hex="#9a2f26", shadow="#5a4060", rim=.08f}},
        {"DD_Tile", new Toon{hex="#25272d", shadow="#4a5578", rim=.1f}},
    };

    static readonly Dictionary<string, Glow> GlowTable = new Dictionary<string, Glow>
    {
        {"DD_Ember", new Glow{hex="#ff9a3a", intensity=1.25f}},
        {"DD_TealGlow", new Glow{hex="#3fb9a8", intensity=1.2f}},
        {"DD_TealFire", new Glow{hex="#3fe0c0", intensity=1.2f}},
        {"DD_RedEye", new Glow{hex="#ff2a1a", intensity=1.3f}},
        {"DD_GoldEye", new Glow{hex="#ffc23a", intensity=1.2f}},
        {"DD_RingPlayer", new Glow{hex="#5fd8ff", intensity=1.0f}},
        {"DD_RingEnemy", new Glow{hex="#ff4a2a", intensity=0.9f}},
        {"DD_EnemyBullet", new Glow{hex="#ff4a14", intensity=1.15f}},
        {"DD_EnemyBulletCore", new Glow{hex="#ffb060", intensity=1.25f}},
        {"DD_PlayerBullet", new Glow{hex="#9fe8ff", intensity=1.3f}},
        {"DD_LampFire", new Glow{hex="#ffa040", intensity=1.25f}},
    };

    static Dictionary<string, Material> mats;

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }

    static Material MakeOrUpdate(string name, Shader shader)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        return m;
    }

    static void BuildMaterials()
    {
        EnsureFolder($"{Root}/Materials");
        var toon = Shader.Find("DD/Toon"); var glow = Shader.Find("DD/Glow");
        if (toon == null || glow == null) throw new System.Exception("DD shaders not found. Check Assets/Art/DDConcept/Shaders compiled.");
        mats = new Dictionary<string, Material>();
        foreach (var kv in ToonTable)
        {
            var m = MakeOrUpdate(kv.Key, toon);
            m.SetColor("_BaseColor", Hex(kv.Value.hex));
            m.SetColor("_ShadowTint", Hex(kv.Value.shadow));
            m.SetFloat("_RimStrength", kv.Value.rim);
            m.SetFloat("_StripeScale", kv.Value.stripes);
            m.SetFloat("_OutlineWidth", kv.Key == "DD_Ground" || kv.Key == "DD_Slab" ? 0.008f : 0.012f);
            EditorUtility.SetDirty(m); mats[kv.Key] = m;
        }
        foreach (var kv in GlowTable)
        {
            var m = MakeOrUpdate(kv.Key, glow);
            m.SetColor("_BaseColor", Hex(kv.Value.hex));
            m.SetFloat("_Intensity", kv.Value.intensity);
            EditorUtility.SetDirty(m); mats[kv.Key] = m;
        }
    }

    static Material Find(string importedName)
    {
        if (string.IsNullOrEmpty(importedName)) return mats["DD_Outline"];
        var n = importedName.Replace(" (Instance)", "").Trim();
        int dot = n.IndexOf('.'); if (dot > 0) n = n.Substring(0, dot);
        return mats.TryGetValue(n, out var m) ? m : mats["DD_Outline"];
    }

    static readonly string[] Characters = { "Hunter", "Dokkaebi", "Gumiho", "Gwisin", "Eodukshini" };

    static Dictionary<string, GameObject> BuildPrefabs()
    {
        EnsureFolder($"{Root}/Prefabs");
        var result = new Dictionary<string, GameObject>();
        foreach (var c in Characters)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{c}.fbx");
            if (model == null) { Debug.LogError($"[DD] missing model {c}.fbx"); continue; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) dst[i] = Find(src[i] ? src[i].name : null);
                r.sharedMaterials = dst;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
            var holder = new GameObject(c);
            inst.transform.SetParent(holder.transform, false);
            inst.transform.localRotation = Quaternion.Euler(0, YawFix, 0);
            var prefab = PrefabUtility.SaveAsPrefabAsset(holder, $"{Root}/Prefabs/{c}.prefab");
            Object.DestroyImmediate(holder);
            result[c] = prefab;
        }
        return result;
    }

    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, string mat, Transform parent, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localEulerAngles = euler;
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mats[mat];
        if (mats[mat].shader.name == "DD/Glow") r.shadowCastingMode = ShadowCastingMode.Off;
        return g;
    }

    static Light PointLight(string name, Vector3 pos, string hex, float intensity, float range, Transform parent)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = pos;
        var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = Hex(hex); l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        return l;
    }

    static VolumeProfile BuildVolumeProfile()
    {
        var path = $"{Root}/DDConcept_Volume.asset";
        AssetDatabase.DeleteAsset(path);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);
        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.8f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.65f);
        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.32f); vig.smoothness.Override(0.45f);
        var ca = p.Add<ColorAdjustments>(true);
        ca.contrast.Override(8f); ca.saturation.Override(-6f);
        foreach (var comp in p.components) { comp.name = comp.GetType().Name; AssetDatabase.AddObjectToAsset(comp, p); }
        EditorUtility.SetDirty(p);
        return p;
    }

    [MenuItem("DD/Build Concept Scene")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        BuildMaterials();
        var prefabs = BuildPrefabs();
        var profile = BuildVolumeProfile();
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // camera: high 3/4 view like Death's Door
        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#0a0d14");
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 100f;
        camGo.transform.position = new Vector3(0.2f, 13.6f, -11.6f);
        camGo.transform.LookAt(new Vector3(0.2f, 0.3f, 0.5f));
        var acd = camGo.AddComponent<UniversalAdditionalCameraData>(); acd.renderPostProcessing = true; acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        // moonlight
        var sunGo = new GameObject("Moon (Directional)");
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.color = Hex("#c4d0ff"); sun.intensity = 1.5f;
        sun.shadows = LightShadows.Soft; sun.shadowStrength = 1f;
        sunGo.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Hex("#1a2030");
        RenderSettings.skybox = null; RenderSettings.fog = false;

        var volGo = new GameObject("Global Volume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

        // stage
        var env = new GameObject("Environment").transform;
        Prim(PrimitiveType.Plane, "Ground", Vector3.zero, new Vector3(4, 1, 4), "DD_Ground", env);
        var rnd = new System.Random(7);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        for (int i = 0; i < 12; i++)   // stone path toward the gate
            for (int j = -1; j <= 1; j += 2)
            {
                if (rnd.NextDouble() < 0.08) continue;
                float z = -6f + i * 0.92f;
                Prim(PrimitiveType.Cube, "Slab", new Vector3(j * 0.42f + R(-.03f, .03f), R(-.02f, .03f), z), new Vector3(0.8f, 0.12f, 0.86f), "DD_Slab", env, new Vector3(0, R(-4, 4), 0));
            }
        for (int ix = -3; ix <= 3; ix++)   // courtyard plaza
            for (int iz = -2; iz <= 2; iz++)
            {
                if (rnd.NextDouble() < 0.18) continue;
                Prim(PrimitiveType.Cube, "Slab", new Vector3(ix * 0.85f, R(-.02f, .02f), 1.3f + iz * 0.85f), new Vector3(0.8f, 0.12f, 0.8f), "DD_Slab", env, new Vector3(0, R(-3, 3), 0));
            }
        foreach (var x in new[] { -2.6f, 2.6f })   // stone lanterns
        {
            var lt = new GameObject("StoneLantern").transform; lt.SetParent(env, false); lt.localPosition = new Vector3(x, 0, 4.2f);
            Prim(PrimitiveType.Cylinder, "Base", new Vector3(0, 0.1f, 0), new Vector3(0.75f, 0.1f, 0.75f), "DD_Stone", lt);
            Prim(PrimitiveType.Cylinder, "Pillar", new Vector3(0, 0.55f, 0), new Vector3(0.24f, 0.35f, 0.24f), "DD_Stone", lt);
            Prim(PrimitiveType.Cylinder, "Shelf", new Vector3(0, 0.95f, 0), new Vector3(0.62f, 0.06f, 0.62f), "DD_Stone", lt);
            Prim(PrimitiveType.Cube, "Fire", new Vector3(0, 1.22f, 0), new Vector3(0.36f, 0.36f, 0.36f), "DD_LampFire", lt);
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
                Prim(PrimitiveType.Cube, "Post", new Vector3(d.x * 0.19f, 1.22f, d.y * 0.19f), new Vector3(0.1f, 0.44f, 0.1f), "DD_Stone", lt);
            Prim(PrimitiveType.Cube, "Roof", new Vector3(0, 1.5f, 0), new Vector3(0.7f, 0.14f, 0.7f), "DD_Stone", lt, new Vector3(0, 45, 0));
            PointLight("LanternLight", new Vector3(0, 1.25f, 0), "#ff9a48", 2f, 3.5f, lt);
        }
        // 홍살문 gate
        var gate = new GameObject("Hongsalmun").transform; gate.SetParent(env, false); gate.localPosition = new Vector3(0, 0, 5.4f);
        foreach (var x in new[] { -1.25f, 1.25f }) Prim(PrimitiveType.Cylinder, "Post", new Vector3(x, 1.7f, 0), new Vector3(0.2f, 1.7f, 0.2f), "DD_Hongsal", gate);
        foreach (var y in new[] { 2.7f, 3.25f }) Prim(PrimitiveType.Cube, "Beam", new Vector3(0, y, 0), new Vector3(2.9f, 0.14f, 0.16f), "DD_Hongsal", gate);
        for (int i = 0; i < 11; i++) Prim(PrimitiveType.Cylinder, "Arrow", new Vector3(-1f + i * 0.2f, 3.05f, 0), new Vector3(0.05f, 0.45f, 0.05f), "DD_Hongsal", gate);
        // wall segments
        foreach (var x in new[] { -5.2f, 5.2f })
        {
            Prim(PrimitiveType.Cube, "Wall", new Vector3(x, 0.45f, 5.4f), new Vector3(7.6f, 0.9f, 0.55f), "DD_Stone", env);
            Prim(PrimitiveType.Cube, "WallTile", new Vector3(x, 1.0f, 5.4f), new Vector3(7.8f, 0.18f, 0.75f), "DD_Tile", env);
        }

        // characters (positions match the Blender battle shot)
        var chars = new GameObject("Characters").transform;
        GameObject Place(string n, Vector3 p, float s)
        {
            if (!prefabs.TryGetValue(n, out var pf)) return null;
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pf, chars);
            g.transform.position = p; g.transform.localScale = Vector3.one * s; return g;
        }
        var hp = new Vector3(0.3f, 0, -2.2f); var dp = new Vector3(-2.1f, 0, 1.9f);
        var hunter = Place("Hunter", hp, 1.15f);
        var dok = Place("Dokkaebi", dp, 1.1f);
        var fox = Place("Gumiho", new Vector3(3.9f, 0, -0.2f), 1.3f);
        var ghost = Place("Gwisin", new Vector3(1.3f, 0, 2.7f), 1.05f);
        var shade = Place("Eodukshini", new Vector3(-4.6f, 0, -2.7f), 0.9f);
        void Face(GameObject g, Vector3 target, float extra = 0)
        {
            if (!g) return;
            var d = target - g.transform.position; d.y = 0;
            g.transform.rotation = Quaternion.LookRotation(d.normalized) * Quaternion.Euler(0, extra, 0);
        }
        Face(hunter, dp, 0); Face(dok, hp); Face(fox, hp, -20); Face(ghost, hp); Face(shade, hp);

        // bullets: enemy = warm round, player = cool elongated
        var fx = new GameObject("BulletsFX").transform;
        for (int k = 0; k < 16; k++)
        {
            float a = Mathf.PI * 2 * k / 16f;
            var p = dp + new Vector3(Mathf.Cos(a), 0.8f, Mathf.Sin(a)) * 1f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.15f;
            p.y = 0.8f;
            Prim(PrimitiveType.Sphere, "EnemyBullet", p, Vector3.one * 0.3f, "DD_EnemyBullet", fx);
            Prim(PrimitiveType.Sphere, "Core", p, Vector3.one * 0.16f, "DD_EnemyBulletCore", fx);
        }
        var dir = (dp - hp); dir.y = 0; dir.Normalize();
        for (int k = 0; k < 3; k++)
        {
            var p = hp + dir * (1.6f + k * 0.9f) + Vector3.up * 1.0f;
            var b = Prim(PrimitiveType.Capsule, "PlayerBullet", p, new Vector3(0.12f, 0.3f, 0.12f), "DD_PlayerBullet", fx);
            b.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0);
        }
        PointLight("MuzzleLight", hp + dir * 1.3f + Vector3.up * 1.1f, "#ffb347", 2f, 2.5f, fx);
        PointLight("DokkaebiGlow", dp + Vector3.up * 1.3f, "#ff7a2a", 1.2f, 3f, fx);
        foreach (var p in new[] { new Vector3(-3.4f, 1.6f, 2.9f), new Vector3(-1f, 2.2f, 3.4f), new Vector3(6f, 1.7f, 3.5f), new Vector3(-6f, 1f, -3.6f) })
        {
            Prim(PrimitiveType.Sphere, "Dokkaebibul", p, new Vector3(0.18f, 0.24f, 0.18f), "DD_TealFire", fx);
            PointLight("WispLight", p, "#4fe3c8", 1f, 2.2f, fx);
        }

        EnsureFolder("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        Capture("unity_preview");
        Debug.Log("[DD] Concept scene built: " + ScenePath);
    }

    [MenuItem("DD/Capture Preview")]
    public static void CaptureMenu() => Capture("unity_preview");

    public static void Capture(string fileName)
    {
        var cam = Camera.main;
        if (!cam) { Debug.LogError("[DD] no main camera"); return; }
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
        cam.targetTexture = prev; RenderTexture.active = null;
        var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        Debug.Log("[DD] preview saved: " + path);
    }
}
