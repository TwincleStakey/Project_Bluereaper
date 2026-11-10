// Builds the bamboo arena scene (menu Tools/Dev/Bamboo Arena/Build Scene): materials, the forest floor (dry bamboo leaves, a
// packed-earth footpath, pebbles, moss), floor props after the bamboo refs (fallen and split culms lying flat; stumps,
// shoots, roots and mossy rocks near the bamboo; nothing blocks the clearing), low undergrowth, the instanced bamboo wall, mist sheets, light shafts, drifting leaves, sun, camera
// at the RoomRun angle, post-processing, the ink filter, invisible walls, and the RoomRun dev room (F9).
// Re-running rebuilds everything; the BambooField settings in the open scene are kept.
using System.Collections.Generic;
using System.Linq;
using BambooArena;
using Reeds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class BABuilder
{
    public const string Root = "Assets/Game/BambooArena";
    public const string ScenePath = Root + "/BambooArena.unity";
    public static readonly Vector2 ArenaSize = new Vector2(88, 72);   // was 44 x 36
    const int E = 2, A = 2;   // prop counts: x2 along the edge (perimeter); inside only x2 so the bigger clearing stays clean (11-10)
    const string Gen = Root + "/Generated";
    const string Mats = Root + "/Materials";
    const string AV = "Assets/Game/AbandonedVillage/Models/";

    static Material Mat(string name, string shader, System.Action<Material> init = null)
    {
        var path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (!sh) { Debug.LogError("[Bamboo] missing shader " + shader); return null; }
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        init?.Invoke(m);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh FbxMesh(string name) => AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/{name}.fbx").OfType<Mesh>().FirstOrDefault();
    static GameObject Fbx(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{name}.fbx");
    static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{n}_2k.jpg");

    static void SaveMesh(Mesh m, string name)
    {
        var path = $"{Gen}/{name}.asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old) { EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); }
        else AssetDatabase.CreateAsset(m, path);
    }

    static void ImportSettings()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            bool ch = false;
            if (imp.importAnimation) { imp.importAnimation = false; ch = true; }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; ch = true; }
            if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; ch = true; }
            if (ch) imp.SaveAndReimport();
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool nrm = path.Contains("_nor_");
            bool lit = path.Contains("mossy") ;   // URP Lit props need real normal maps; the ground shader unpacks its own
            var type = nrm && lit ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = !nrm;
            if (imp.maxTextureSize == 1024 && imp.textureType == type && imp.sRGBTexture == srgb) continue;
            imp.maxTextureSize = 1024; imp.textureType = type; imp.sRGBTexture = srgb;
            imp.SaveAndReimport();
        }
    }

    static Material LitTex(string name, string tex, Color tint, float tile)
    {
        return Mat(name, "Universal Render Pipeline/Lit", m =>
        {
            m.SetTexture("_BaseMap", Tex(tex + "_diff")); m.SetTexture("_BumpMap", Tex(tex + "_nor_gl"));
            m.SetTextureScale("_BaseMap", Vector2.one * tile); m.EnableKeyword("_NORMALMAP");
            m.SetColor("_BaseColor", tint); m.SetFloat("_Smoothness", 0.1f);
        });
    }

    [MenuItem("Tools/Dev/Bamboo Arena/Build Scene")]
    public static void Build()
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != ScenePath) { Debug.LogError("[Bamboo] ABORT: unsaved changes in " + active.path); return; }
        foreach (var d in new[] { Gen, Mats })
            if (!AssetDatabase.IsValidFolder(d)) AssetDatabase.CreateFolder(Root, d.Substring(Root.Length + 1));
        ImportSettings();

        // ---------------------------------------------------------------- materials
        var bamboo = Mat("BA_Bamboo", "BambooArena/Bamboo");
        // Flush floor only (dry bamboo leaves, a packed-earth trail, pebbles, moss, damp patches, dappled light).
        Texture2D RF(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Game/ReedField/Textures/{n}_2k.jpg");
        var ground = Mat("BA_Ground", "BambooArena/Floor", m =>
        {
            m.SetTexture("_SoilTex", Tex("forest_ground_04_diff")); m.SetTexture("_SoilNrm", Tex("forest_ground_04_nor_gl"));
            m.SetTexture("_LeafTex", RF("brown_mud_leaves_01_diff")); m.SetTexture("_LeafNrm", RF("brown_mud_leaves_01_nor_gl"));
            m.SetTexture("_PathTex", RF("brown_mud_02_diff")); m.SetTexture("_PathNrm", RF("brown_mud_02_nor_gl"));   // packed earth
            m.SetTexture("_PebbleTex", RF("river_small_rocks_diff")); m.SetTexture("_PebbleNrm", RF("river_small_rocks_nor_gl"));
            m.SetTexture("_MossTex", Tex("mossy_rock_diff"));
            m.SetFloat("_SoilTile", 2.4f); m.SetFloat("_Calm", 0.4f);   // 11-10: bare soil pulled toward the leaf palette (it read as a red-brown patch on the bigger floor) m.SetFloat("_NormalStrength", 0.9f);   // 11-10: crisper floor
            m.SetFloat("_Leaves", 0.95f); m.SetFloat("_Moss", 0.5f); m.SetFloat("_Pebbles", 0.5f); m.SetFloat("_Damp", 0.5f); m.SetFloat("_Dapple", 0.45f);
            m.SetColor("_LeafCol", new Color(0.86f, 0.72f, 0.44f));   // straw drifts that read against the darker soil
            m.SetColor("_Tint", new Color(0.92f, 1.0f, 0.86f));
            m.SetColor("_Shade", new Color(0.06f, 0.075f, 0.05f));
            m.SetColor("_Ambient", new Color(0.20f, 0.25f, 0.21f));
        });
        // low ground cover (the reed field's low grass mesh, in bamboo-floor greens)
        var coverMat = Mat("BA_GroundCover", "ReedField/Reed", m =>
        {
            m.SetColor("_LeafCol", new Color(0.13f, 0.18f, 0.09f)); m.SetColor("_LeafTip", new Color(0.30f, 0.36f, 0.17f));
            m.SetColor("_RootCol", new Color(0.07f, 0.08f, 0.05f)); m.SetColor("_StemCol", new Color(0.18f, 0.20f, 0.12f));
            m.SetFloat("_Translucency", 0.3f); m.SetFloat("_GustSheen", 0.1f);
        });
        var stone = LitTex("BA_Stone", "mossy_rock", new Color(0.82f, 0.86f, 0.78f), 0.45f);
        var stone2 = LitTex("BA_Stone2", "rock_pitted_mossy", new Color(0.55f, 0.58f, 0.54f), 0.6f);
        var stump = Mat("BA_Stump", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.46f, 0.44f, 0.24f)); m.SetFloat("_Smoothness", 0.25f); });
        var stumpCut = Mat("BA_StumpCut", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.72f, 0.64f, 0.44f)); m.SetFloat("_Smoothness", 0.1f); });
        var fallenMat = Mat("BA_Fallen", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.52f, 0.46f, 0.30f)); m.SetFloat("_Smoothness", 0.15f); });
        var shootMat = Mat("BA_Shoot", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.40f, 0.33f, 0.20f)); m.SetFloat("_Smoothness", 0.22f); });
        var rootMat = Mat("BA_Root", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.24f, 0.18f, 0.11f)); m.SetFloat("_Smoothness", 0.12f); });
        var mist = Mat("BA_Mist", "ReedField/FogCard", m => { m.SetColor("_Color", new Color(0.70f, 0.78f, 0.70f, 0.12f)); m.SetFloat("_Scale", 0.1f); m.SetFloat("_Speed", 0.35f); });
        var shaftMat = Mat("BA_Shaft", "BambooArena/LightShaft");
        var leafMat = Mat("BA_Leaf", "ReedField/Fluff", m => { m.SetColor("_Color", new Color(0.62f, 0.68f, 0.36f, 0.9f)); m.SetFloat("_Backlight", 1.4f); });
        AssetDatabase.SaveAssets();

        // ---------------------------------------------------------------- scene
        var scene = EditorSceneManager.GetActiveScene().path == ScenePath
            ? EditorSceneManager.GetActiveScene()
            : (System.IO.File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single));
        string keep = null;
        var old = Object.FindAnyObjectByType<BambooField>();
        if (old) keep = JsonUtility.ToJson(old);
        foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.75f;
        RenderSettings.sun = sun; RenderSettings.skybox = null; RenderSettings.fog = false;

        var rootGo = new GameObject("BambooField");
        rootGo.AddComponent<ReedInstancer>();
        var field = rootGo.AddComponent<BambooField>();
        if (keep != null) JsonUtility.FromJsonOverwrite(keep, field);
        // 11-10 (Kido: the wall did not read as a tall dense wall, the floor was a foggy blur)
        field.spacing = Mathf.Min(field.spacing, 1.05f); field.heightScale = Mathf.Max(field.heightScale, 1.25f);
        field.wallDepth = Mathf.Max(field.wallDepth, 12f); field.groundFog = Mathf.Min(field.groundFog, 0.12f);
        field.clumps = new[] { FbxMesh("BA_Bamboo_A"), FbxMesh("BA_Bamboo_B"), FbxMesh("BA_Bamboo_C") };
        field.farClump = FbxMesh("BA_Bamboo_Far");
        field.bambooMat = bamboo; field.sunLight = sun;
        field.groundCover = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/ReedField/Models/RF_LowGrass.fbx").OfType<Mesh>().FirstOrDefault();
        field.groundCoverMat = coverMat; field.groundCoverScale = 1.1f;
        field.seeThroughTarget = null;
        // 11-10 (Kido: every arena 2x per side, 4x the area). The scene keeps its own settings, so force the new size.
        field.arena = ArenaSize;

        // ---------------------------------------------------------------- floor props (after the bamboo-forest refs)
        // Nothing here has a collider and nothing in the middle stands up: fallen and split culms lie flat, stumps,
        // shoots, roots and mossy rocks stay near the bamboo. The trail through the clearing is kept clear.
        var props = new GameObject("Props").transform;
        var drifts = new List<Vector3>();   // (x, z, radius): dry leaves pile up around these
        GameObject Place(GameObject src, Vector3 pos, float yaw, float scale, params Material[] mats)
        {
            if (!src) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, props);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            if (mats.Length > 0) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterials = mats.Take(r.GetComponent<MeshFilter>().sharedMesh.subMeshCount).ToArray();
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            // dry leaves pile up only against props near the bamboo; out in the clearing the floor stays clean
            // (11-10: with the bigger clearing every scattered culm left a leaf blotch and the floor read as one smear)
            if (field.Outside(new Vector2(pos.x, pos.z)) > -6f) drifts.Add(new Vector3(pos.x, pos.z, 0.9f + scale * 0.6f));
            return go;
        }
        var rs = new System.Random(field.seed + 5);
        float Rn() => (float)rs.NextDouble();
        float hx = field.arena.x * 0.5f, hz = field.arena.y * 0.5f;
        bool OnTrail(Vector3 p) => field.Trail(new Vector2(p.x, p.z)) > 0.05f;
        Vector3 NearEdge(float inset, float depth = 3f)
        {
            for (int k = 0; k < 30; k++)
            {
                int side = rs.Next(4); float t = Rn() * 2 - 1;
                float i = inset + Rn() * depth;
                var p = side switch
                {
                    0 => new Vector3(t * (hx - 2), 0, hz - i),
                    1 => new Vector3(t * (hx - 2), 0, -hz + i),
                    2 => new Vector3(hx - i, 0, t * (hz - 2)),
                    _ => new Vector3(-hx + i, 0, t * (hz - 2)),
                };
                if (!OnTrail(p)) return p;
            }
            return new Vector3(hx - inset, 0, 0);
        }
        Vector3 Inside(float margin)
        {
            for (int k = 0; k < 40; k++)
            {
                var p = new Vector3((Rn() * 2 - 1) * (hx - margin), 0, (Rn() * 2 - 1) * (hz - margin));
                if (!OnTrail(p) && field.Trail(new Vector2(p.x + 1.5f, p.z)) < 0.05f && field.Trail(new Vector2(p.x - 1.5f, p.z)) < 0.05f) return p;
            }
            return Vector3.zero;
        }
        Vector3 Jit(Vector3 c, float r) => c + new Vector3((Rn() * 2 - 1) * r, 0, (Rn() * 2 - 1) * r);
        // cut stumps in small groups at the bamboo edge, with a shoot or two and roots beside them
        for (int i = 0; i < 9 * E; i++)
        {
            var c = NearEdge(0.2f, 2.5f);
            Place(Fbx("BA_Stumps"), c, Rn() * 360, 0.9f + Rn() * 0.6f, stump, stumpCut);
            Rn();   // (a shoot used to go here; keeps the rest of the layout seeded the same)
            if (Rn() < 0.5f) Place(Fbx("BA_Roots"), Jit(c, 0.6f), Rn() * 360, 0.9f + Rn() * 0.4f, rootMat);
        }
        // no bamboo shoots: from the RoomRun camera they read as odd brown cones (Kido 11-10)
        // roots surfacing from the bamboo wall
        for (int i = 0; i < 8 * E; i++) Place(Fbx("BA_Roots"), NearEdge(0.0f, 1.5f), Rn() * 360, 1f + Rn() * 0.6f, rootMat);
        // fallen culms spread naturally: lone ones and small groups of 2-3 lying at different angles, more toward the
        // bamboo, a couple of small piles at the foot of the wall (one heap in the open read as a bundle of sticks)
        for (int i = 0; i < 8 * E; i++) Place(Fbx("BA_Fallen"), NearEdge(0.6f, 4f), Rn() * 360, 0.8f + Rn() * 0.35f, fallenMat);
        for (int i = 0; i < 2 * E; i++) Place(Fbx("BA_FallenPile"), NearEdge(0.4f, 1.5f), Rn() * 360, 0.8f, fallenMat);
        for (int g = 0; g < 5 * A; g++)
        {
            var c = Inside(5f); float yaw = Rn() * 360;
            int n = 1 + rs.Next(3);
            for (int k = 0; k < n; k++) Place(Fbx("BA_Fallen"), Jit(c, 1.4f), yaw + (Rn() * 2 - 1) * 35f, 0.7f + Rn() * 0.4f, fallenMat);
        }
        for (int i = 0; i < 6 * A; i++) Place(Fbx("BA_Fallen"), Inside(4f), Rn() * 360, 0.6f + Rn() * 0.4f, fallenMat);
        // split and broken bits scattered about, a few in the open
        for (int i = 0; i < 7 * A; i++) Place(Fbx("BA_Split"), Inside(3f), Rn() * 360, 0.8f + Rn() * 0.3f, fallenMat);
        for (int i = 0; i < 5 * E; i++) Place(Fbx("BA_Split"), NearEdge(0.5f), Rn() * 360, 1f, fallenMat);
        // small mossy rocks half sunk at the bamboo foot (LookStudy rock set)
        for (int i = 0; i < 8 * E; i++)
        {
            var rock = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/LookStudy/Stages/Models/Rock_rock{1 + rs.Next(13):00}.fbx");
            var go = Place(rock, NearEdge(0.0f, 2f) + Vector3.down * 0.08f, Rn() * 360, 0.25f + Rn() * 0.3f, Rn() < 0.5f ? stone : stone2);
            if (go) go.transform.rotation = Quaternion.Euler(Rn() * 20 - 10, Rn() * 360, Rn() * 20 - 10);
        }

        // ---------------------------------------------------------------- ground
        var gm = BuildGround(field, drifts); SaveMesh(gm, "BA_Ground");
        AssetDatabase.SaveAssets();
        var gGo = new GameObject("BA_Ground");
        gGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/BA_Ground.asset");
        var gr = gGo.AddComponent<MeshRenderer>(); gr.sharedMaterial = ground; gr.shadowCastingMode = ShadowCastingMode.Off;
        gGo.AddComponent<MeshCollider>().sharedMesh = gGo.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(gGo, StaticEditorFlags.BatchingStatic);
        foreach (var r in props.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = ShadowCastingMode.On;

        // ---------------------------------------------------------------- bamboo
        field.Generate();
        field.ApplyMood();

        // ---------------------------------------------------------------- mist sheets: thick in the bamboo, thin in the clearing
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var fogRoot = new GameObject("Mist").transform;
        var fr = new System.Random(field.seed + 11);
        var full = field.FullSize;
        int made = 0;
        for (int i = 0; i < 400 * E && made < 60 * E; i++)
        {
            float x = (float)(fr.NextDouble() - 0.5) * (field.arena.x + 24f), z = (float)(fr.NextDouble() - 0.5) * (field.arena.y + 24f);
            float o = field.Outside(new Vector2(x, z));
            if (o < 1.5f) continue;   // 11-10: none over the clearing; mist only lies in the bamboo
            var go = new GameObject("MistSheet");
            go.transform.SetParent(fogRoot, false);
            go.transform.position = new Vector3(x, Mathf.Lerp(0.8f, 3.5f, (float)fr.NextDouble()), z);
            go.transform.rotation = Quaternion.Euler(90, (float)fr.NextDouble() * 360f, 0);
            go.transform.localScale = Vector3.one * Mathf.Lerp(8f, 14f, (float)fr.NextDouble());
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mist; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            made++;
        }

        // ---------------------------------------------------------------- light shafts falling into the clearing edge
        var shaftMesh = new Mesh { name = "BA_ShaftQuad" };
        shaftMesh.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
        shaftMesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        shaftMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        shaftMesh.bounds = new Bounds(Vector3.up * 8, Vector3.one * 30);   // stretched toward the sun in the shader
        SaveMesh(shaftMesh, "BA_ShaftQuad");
        AssetDatabase.SaveAssets();
        shaftMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/BA_ShaftQuad.asset");
        var sunDir = -(Quaternion.Euler(field.sun.x, field.sun.y, 0) * Vector3.forward);
        shaftMat.SetVector("_SunDir", sunDir);
        shaftMat.SetColor("_Color", new Color(1.0f, 0.88f, 0.62f)); shaftMat.SetFloat("_Intensity", 0.16f);
        var shafts = new GameObject("LightShafts").transform;
        var sr = new System.Random(field.seed + 23);
        for (int i = 0; i < 8 * E; i++)
        {
            var p = NearEdgeShaft(field, sr);
            var go = new GameObject("Shaft");
            go.transform.SetParent(shafts, false);
            go.transform.position = new Vector3(p.x, 0, p.y);
            go.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, (float)sr.NextDouble());
            go.AddComponent<MeshFilter>().sharedMesh = shaftMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = shaftMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        // ---------------------------------------------------------------- leaves drifting down
        {
            var go = new GameObject("AirLeaves");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 12f);
            main.startSpeed = 0f; main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.9f, 0.55f, 0.9f), new Color(0.55f, 0.62f, 0.3f, 0.8f));
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 120; main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 9f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40f, 3f, 30f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.3f, 0.8f); vel.y = new ParticleSystem.MinMaxCurve(-0.75f, -0.35f); vel.z = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.4f; noise.scrollSpeed = 0.25f; noise.quality = ParticleSystemNoiseQuality.Low;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = leafMat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.AddComponent<BAAirLeaves>();
            ps.Play();
        }

        // ---------------------------------------------------------------- camera (RoomRun angle) and post
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 30; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = field.fog;
        camGo.transform.position = new Vector3(0, 17f, -14.5f); camGo.transform.LookAt(Vector3.zero);
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        camGo.AddComponent<AudioListener>();

        var profPath = $"{Root}/BA_PostFX.asset";
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
        if (!prof)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, profPath);
            var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(0.6f); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.7f);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.5f);
            var ca = prof.Add<ColorAdjustments>(true); ca.contrast.Override(14f); ca.saturation.Override(-12f); ca.postExposure.Override(0.1f);
            var wb = prof.Add<WhiteBalance>(true); wb.temperature.Override(4f); wb.tint.Override(-4f);
            var grain = prof.Add<FilmGrain>(true); grain.intensity.Override(0.18f); grain.type.Override(FilmGrainLookup.Medium1);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
        }
        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true; vol.sharedProfile = prof;
        field.volume = vol;

        // screen filters (F10), shared with the reed field
        InkFilterTools.Ensure();

        // invisible walls on the clearing edge
        var walls = new GameObject("ArenaWalls").transform;
        void WallBox(Vector3 c, Vector3 sz) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.transform.position = c; w.AddComponent<BoxCollider>().size = sz; }
        WallBox(new Vector3(-hx - 0.5f, 2, 0), new Vector3(1, 6, field.arena.y + 2));
        WallBox(new Vector3(hx + 0.5f, 2, 0), new Vector3(1, 6, field.arena.y + 2));
        WallBox(new Vector3(0, 2, -hz - 0.5f), new Vector3(field.arena.x + 2, 6, 1));
        WallBox(new Vector3(0, 2, hz + 0.5f), new Vector3(field.arena.x + 2, 6, 1));

        field.Generate(); field.ApplyMood(); field.SetGlobals();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddRoomRunRoom(field);
        Debug.Log($"[Bamboo] built: {rootGo.GetComponent<ReedInstancer>().Count} bamboo clumps");
    }

    static Vector2 NearEdgeShaft(BambooField f, System.Random r)
    {
        for (int k = 0; k < 50; k++)
        {
            var p = new Vector2((float)(r.NextDouble() - 0.5) * f.arena.x, (float)(r.NextDouble() - 0.5) * f.arena.y);
            float o = f.Outside(p);
            if (o > -7f && o < -1f) return p;
        }
        return Vector2.zero;
    }

    // Flat clearing; outside it the forest floor rolls a little. Colour: R = dry-leaf drift (bamboo edge and around the
    // floor props), G = the packed-earth footpath, B = under the bamboo (darker).
    static Mesh BuildGround(BambooField f, List<Vector3> drifts)
    {
        var full = f.FullSize;
        int nx = Mathf.CeilToInt(full.x * 2f), nz = Mathf.CeilToInt(full.y * 2f);   // 0.5 m cells: soft mask edges, no 1 m stair steps
        var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
        for (int j = 0; j <= nz; j++)
        for (int i = 0; i <= nx; i++)
        {
            float x = -full.x * 0.5f + full.x * i / nx, z = -full.y * 0.5f + full.y * j / nz;
            var p = new Vector2(x, z);
            float o = f.Outside(p);
            float y = Mathf.Max(o, 0) > 0 ? Mathf.Clamp01(o / 8f) * (Mathf.PerlinNoise(x * 0.08f, z * 0.08f) * 1.4f) : 0f;
            float path = f.Trail(p);   // packed-earth footpath (flush)
            // dry leaves drift against the bamboo and around stumps / fallen culms
            float drift = Mathf.Clamp01(Mathf.InverseLerp(-5f, 0f, o));
            foreach (var d in drifts) drift = Mathf.Max(drift, 1f - Mathf.Clamp01(Vector2.Distance(p, new Vector2(d.x, d.y)) / d.z));
            float under = f.UnderCanopy(p);
            v.Add(new Vector3(x, y, z)); c.Add(new Color(drift, path, under, 1));
        }
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
            t.AddRange(new[] { a, d, b, b, d, e });
        }
        var m = new Mesh { name = "BA_Ground", indexFormat = IndexFormat.UInt32 };
        m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    public static void AddRoomRunRoom(BambooField f)
    {
        const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
        if (!cfg) return;
        var so = new SerializedObject(cfg);
        var rooms = so.FindProperty("rooms");
        for (int i = 0; i < rooms.arraySize; i++)
            if (rooms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == "bamboo")
            {
                rooms.GetArrayElementAtIndex(i).FindPropertyRelative("size").vector2Value = f.arena;
                so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
                return;
            }
        int at = rooms.arraySize;
        rooms.InsertArrayElementAtIndex(at - 1);
        var r = rooms.GetArrayElementAtIndex(at);
        r.FindPropertyRelative("id").stringValue = "bamboo";
        r.FindPropertyRelative("displayName").stringValue = "대나무숲";
        r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(6, 0);
        r.FindPropertyRelative("scenePath").stringValue = ScenePath;
        r.FindPropertyRelative("groundMeshName").stringValue = "";
        r.FindPropertyRelative("center").vector3Value = Vector3.zero;
        r.FindPropertyRelative("size").vector2Value = f.arena;
        r.FindPropertyRelative("fitToStage").boolValue = false;
        r.FindPropertyRelative("floorColor").colorValue = new Color(0.08f, 0.09f, 0.08f);
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

// Captures for the review: the clearing from the RoomRun camera (filters off and the three ink filters), a corner
// (no void past the bamboo), and the player at the south edge with the bamboo in front dissolved around them.
public static class BACapture
{
    [MenuItem("Tools/Dev/Bamboo Arena/Capture Shots")]
    public static void Capture()
    {
        var f = Object.FindAnyObjectByType<BambooField>(); var cam = Camera.main;
        if (!f || !cam) { Debug.LogError("[Bamboo] open the bamboo arena scene first"); return; }
        var ink = InkFilterTools.Ensure();
        ShaderUtil.allowAsyncCompilation = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation; var keepMode = ink.editorMode;
        void View(Vector3 target) { cam.transform.position = target + new Vector3(0, 17f, -14.5f); cam.transform.LookAt(target); }
        void Shot(string file) { f.SetGlobals(); for (int w = 0; w < 3; w++) cam.Render(); InkFilterTools.Shot(cam, file); }
        try
        {
            ink.editorMode = InkFilters.InkMode.Off;
            View(Vector3.zero); Shot("10_bamboo_center.png");
            View(new Vector3(f.arena.x * 0.5f - 3f, 0, f.arena.y * 0.5f - 3f)); Shot("10_bamboo_corner_ne.png");
            View(new Vector3(-f.arena.x * 0.5f + 3f, 0, -f.arena.y * 0.5f + 3f)); Shot("10_bamboo_corner_sw.png");
            ink.editorMode = InkFilters.InkMode.Ink; View(new Vector3(f.arena.x * 0.5f - 3f, 0, f.arena.y * 0.5f - 3f)); Shot("10_bamboo_corner_ink.png");
            ink.editorMode = InkFilters.InkMode.SumiE; Shot("10_bamboo_corner_sumie.png");
            ink.editorMode = InkFilters.InkMode.Watercolour; Shot("10_bamboo_corner_watercolour.png");
            ink.editorMode = InkFilters.InkMode.Off;

            // the RoomRun player model standing at the south edge, bamboo between it and the camera
            var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/RoomRun/RoomRunConfig.asset");
            var prefab = cfg ? new SerializedObject(cfg).FindProperty("playerPrefab").objectReferenceValue as GameObject : null;
            var stand = prefab ? (GameObject)Object.Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            stand.name = "Player (capture stand-in)";
            var at = new Vector3(4f, 0, -f.arena.y * 0.5f + 1.2f);
            stand.transform.position = at + (prefab ? Vector3.zero : Vector3.up); stand.transform.localScale *= 1.15f;
            stand.transform.rotation = Quaternion.Euler(0, 160, 0);
            View(at);
            f.seeThroughTarget = stand.transform;
            f.seeThrough = false; f.UpdateSeeThrough(); Shot("10_bamboo_edge_no_seethrough.png");
            f.seeThrough = true; f.UpdateSeeThrough(); Shot("10_bamboo_edge_seethrough.png");
            f.seeThroughTarget = null; f.UpdateSeeThrough();
            Object.DestroyImmediate(stand);
        }
        finally
        {
            ink.editorMode = keepMode; cam.transform.SetPositionAndRotation(p0, r0);
            ShaderUtil.allowAsyncCompilation = true;
        }
        Debug.Log("[Bamboo] shots saved to " + InkFilterTools.OutDir);
    }

    [MenuItem("Tools/Dev/Bamboo Arena/Benchmark (editor)")]
    public static void Bench()
    {
        var cam = Camera.main; var f = Object.FindAnyObjectByType<BambooField>();
        if (!cam || !f) return;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation;
        var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
        var inst = f.GetComponent<ReedInstancer>();
        var sb = new System.Text.StringBuilder("[Bamboo] benchmark (1920x1080, RoomRun view): ");
        foreach (var (name, at) in new[] { ("centre", Vector3.zero), ("NE corner", new Vector3(f.arena.x * 0.5f - 3f, 0, f.arena.y * 0.5f - 3f)), ("south edge", new Vector3(0, 0, -f.arena.y * 0.5f + 1.5f)) })
        {
            cam.transform.position = at + new Vector3(0, 17f, -14.5f); cam.transform.LookAt(at);
            f.SetGlobals();
            for (int i = 0; i < 30; i++) cam.Render();
            double best = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) { cam.Render(); if (i % 10 == 9) AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion(); }
                best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds / 100);
            }
            sb.Append($"{name} {best:0.00} ms ({inst.LastDrawnInstances} clumps, {inst.LastDrawnTris / 1000000.0:0.0} M tris)  ");
        }
        sb.Append($"| {inst.Count} clumps total");
        Debug.Log(sb.ToString());
        cam.targetTexture = null; Object.DestroyImmediate(rt); cam.transform.SetPositionAndRotation(p0, r0);
    }
}

// Play-mode helpers: jump the RoomRun run into the bamboo arena (same as F9), and put the player at the south edge
// and save a game-camera shot with the see-through dither (10_bamboo_play_edge.png).
public static class BAPlay
{
    static System.Type Dir => System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RoomRun.RunDirector")).FirstOrDefault(x => x != null);

    [MenuItem("Tools/Dev/Bamboo Arena/Play: Jump To Bamboo")]
    public static void Jump()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[Bamboo] enter Play first"); return; }
        var inst = Dir?.GetProperty("Instance")?.GetValue(null);
        if (inst == null) { Debug.LogWarning("[Bamboo] no RunDirector"); return; }
        Dir.GetMethod("DevJump").Invoke(inst, new object[] { "bamboo" });
        Debug.Log("[Bamboo] jumped");
    }

    // Centre shot (ink-filter-test/v3/bamboo_center.png), then the player's CharacterController is walked across the
    // whole clearing in rows and columns; anything that stops it before the outer wall is logged.
    [MenuItem("Tools/Dev/Bamboo Arena/Play: Centre Shot + Walk Test")]
    public static void WalkTest()
    {
        if (!Application.isPlaying) return;
        var t = Dir; var run = t?.GetProperty("Instance")?.GetValue(null);
        var f = Object.FindAnyObjectByType<BambooField>();
        if (run == null || !f) { Debug.LogWarning("[Bamboo] not in the bamboo arena"); return; }
        var player = t.GetProperty("Player").GetValue(run) as Component;
        var place = player.GetType().GetMethod("PlaceAt");
        var rig = t.GetProperty("Rig").GetValue(run);
        var dir = System.IO.Path.Combine(InkFilterTools.OutDir, "v3"); System.IO.Directory.CreateDirectory(dir);
        place.Invoke(player, new object[] { new Vector3(0.5f, 0, -2.5f), 0f });
        rig?.GetType().GetMethod("Snap")?.Invoke(rig, null);
        double at = EditorApplication.timeSinceStartup + 1.2; bool shot = false;
        void Step()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Step; return; }
            if (!shot && EditorApplication.timeSinceStartup > at) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "bamboo_center.png")); shot = true; at = EditorApplication.timeSinceStartup + 0.5; return; }
            if (!shot || EditorApplication.timeSinceStartup < at) return;
            EditorApplication.update -= Step;
            var cc = player.GetComponent<CharacterController>();
            float hx = f.arena.x * 0.5f - 1.2f, hz = f.arena.y * 0.5f - 1.2f;
            var sb = new System.Text.StringBuilder("[Bamboo] walk test: ");
            int blocked = 0;
            void Walk(Vector3 a, Vector3 b)
            {
                place.Invoke(player, new object[] { a, 0f });
                var d = b - a; d.y = 0; float len = d.magnitude; var stepV = d.normalized * 0.2f;
                for (int k = 0; k < len / 0.2f; k++) cc.Move(stepV + Vector3.down * 0.3f);
                var end = player.transform.position; float miss = new Vector2(b.x - end.x, b.z - end.z).magnitude;
                if (miss > 0.6f) { blocked++; sb.Append($"blocked {a}->{b} at {end}; "); }
            }
            for (float z = -hz; z <= hz; z += 3f) Walk(new Vector3(-hx, 0, z), new Vector3(hx, 0, z));
            for (float x = -hx; x <= hx; x += 3f) Walk(new Vector3(x, 0, -hz), new Vector3(x, 0, hz));
            sb.Append(blocked == 0 ? "nothing blocks inside the clearing" : $"{blocked} lines blocked");
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "bamboo_walk.txt"), sb.ToString());
            place.Invoke(player, new object[] { Vector3.zero, 0f });
        }
        EditorApplication.update += Step;
    }

    [MenuItem("Tools/Dev/Bamboo Arena/Play: Edge Shot")]
    public static void EdgeShot()
    {
        if (!Application.isPlaying) return;
        var t = Dir; var run = t?.GetProperty("Instance")?.GetValue(null);
        var f = Object.FindAnyObjectByType<BambooField>();
        if (run == null || !f) { Debug.LogWarning("[Bamboo] not in the bamboo arena"); return; }
        var player = t.GetProperty("Player").GetValue(run) as Component;
        player.GetType().GetMethod("PlaceAt").Invoke(player, new object[] { new Vector3(4f, 0, -f.arena.y * 0.5f + 1.2f), 0f });
        var rig = t.GetProperty("Rig").GetValue(run);
        rig?.GetType().GetMethod("Snap")?.Invoke(rig, null);
        f.LateUpdateNow();
        InkFilterTools.Shot(Camera.main, "10_bamboo_play_edge.png");
        Debug.Log($"[Bamboo] play edge shot, player found: {f.seeThroughTarget != null}");
    }
}
