// Builds the reed field scene (menu Tools/Dev/Reed Field/Build Scene): materials, ground and water meshes, the broken boardwalk,
// landmarks, fog, 도깨비불 wisps, the unseen stalkers and their eyes, moon light, camera and post-processing.
// Re-running it rebuilds everything from the ReedField settings in the open scene (or the defaults the first time).
using System.Collections.Generic;
using System.Linq;
using Reeds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class RFBuilder
{
    public const string Root = "Assets/Game/ReedField";
    public const string ScenePath = Root + "/ReedField.unity";
    const string Gen = Root + "/Generated";
    const string Mats = Root + "/Materials";
    const string AV = "Assets/Game/AbandonedVillage/Models/";

    static Material Mat(string name, string shader, System.Action<Material> init = null)
    {
        var path = $"{Mats}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); init?.Invoke(m); }
        else if (m.shader != sh) m.shader = sh;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh FbxMesh(string name) =>
        AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/{name}.fbx").OfType<Mesh>().FirstOrDefault();

    static void SaveMesh(Mesh m, string name)
    {
        var path = $"{Gen}/{name}.asset";
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old) { EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); }
        else AssetDatabase.CreateAsset(m, path);
    }

    static void ImportSettings(Material wood, Material post)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool bw = path.Contains("Boardwalk");
            bool changed = false;
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (imp.importCameras || imp.importLights) { imp.importCameras = false; imp.importLights = false; changed = true; }
            if (!bw && imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (bw)
            {
                imp.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "BW_Wood"), wood);
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "BW_Post"), post);
                if (!imp.addCollider) { } // colliders are added per segment by the builder
                changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
    }

    // Shader graph properties have generated reference names; set them by their display name.
    static void SetByLabel(Material m, string label, object value)
    {
        var sh = m.shader;
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            if (sh.GetPropertyDescription(i) != label && sh.GetPropertyName(i) != label) continue;
            var id = sh.GetPropertyNameId(i);
            if (value is Color c) m.SetColor(id, c);
            else if (value is float f) m.SetFloat(id, f);
            else if (value is Vector4 v) m.SetVector(id, v);
            return;
        }
        Debug.LogWarning("[ReedField] water shader has no property " + label);
    }

    static Material ParticleMat(string name, Texture tex)
    {
        var m = Mat(name, "Universal Render Pipeline/Particles/Unlit");
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    static ParticleSystem MakePS(Transform parent, string name, Material mat, int layer, float life, float gravity, ParticleSystemRenderMode mode)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = layer;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.startLifetime = life; main.startSpeed = 0;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = gravity; main.maxParticles = 300;
        var em = ps.emission; em.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = mode;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    // Ground scans: 1k on import; normal maps read as linear data (the ground shader unpacks them itself),
    // the plank normal map as a real normal map for URP Lit.
    static void TextureSettings()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Textures" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            bool nrm = path.Contains("_nor_");
            bool planks = path.Contains("weathered_planks");
            var type = nrm && planks ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = !nrm;
            if (imp.maxTextureSize == 1024 && imp.textureType == type && imp.sRGBTexture == srgb) continue;
            imp.maxTextureSize = 1024; imp.textureType = type; imp.sRGBTexture = srgb;
            imp.SaveAndReimport();
        }
    }

    [MenuItem("Tools/Dev/Reed Field/Rebuild With Default Settings")]
    public static void BuildDefaults() => Build(false);

    [MenuItem("Tools/Dev/Reed Field/Build Scene")]
    public static void BuildKeep() => Build(true);

    public static void Build(bool keepSettings)
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != ScenePath) { Debug.LogError("[ReedField] ABORT: unsaved changes in " + active.path); return; }
        foreach (var d in new[] { Gen, Mats })
            if (!AssetDatabase.IsValidFolder(d)) AssetDatabase.CreateFolder(Root, d.Substring(Root.Length + 1));

        // ---------------------------------------------------------------- materials
        var susuki = Mat("RF_Susuki", "ReedField/Reed");
        var galdae = Mat("RF_Galdae", "ReedField/Reed", m =>
        {
            m.SetFloat("_IsReed", 1); m.SetColor("_LeafCol", new Color(0.17f, 0.19f, 0.14f)); m.SetColor("_StemCol", new Color(0.30f, 0.26f, 0.20f));
        });
        var grass = Mat("RF_LowGrass", "ReedField/Reed", m =>
        {
            m.SetColor("_LeafCol", new Color(0.16f, 0.17f, 0.12f)); m.SetColor("_LeafTip", new Color(0.34f, 0.30f, 0.21f)); m.SetFloat("_GustSheen", 0.3f);
        });
        var ground = Mat("RF_Ground", "ReedField/Ground");
        // Shallow wetland water (ReedField/ShallowWater) on the free URP_Water textures (ChiliMilk, MIT).
        var water = Mat("RF_Water", "ReedField/ShallowWater");
        {
            var surfPath = Root + "/ThirdParty/URP_Water/WaterSurface_single.tif";
            var si = (TextureImporter)AssetImporter.GetAtPath(surfPath);
            if (si && si.textureType != TextureImporterType.NormalMap) { si.textureType = TextureImporterType.NormalMap; si.maxTextureSize = 1024; si.SaveAndReimport(); }
            var fi = (TextureImporter)AssetImporter.GetAtPath(Root + "/ThirdParty/URP_Water/WaterFoam.png");
            if (fi && fi.maxTextureSize != 1024) { fi.maxTextureSize = 1024; fi.SaveAndReimport(); }
            water.SetTexture("_SurfaceMap", AssetDatabase.LoadAssetAtPath<Texture2D>(surfPath));
            water.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/URP_Water/WaterFoam.png"));
        }
        var fogMat = Mat("RF_FogCard", "ReedField/FogCard");
        fogMat.SetColor("_Color", new Color(0.30f, 0.34f, 0.42f, 0.13f));
        var wispMat = Mat("RF_Wisp", "ReedField/Glow", m => { m.SetColor("_Color", new Color(0.55f, 1.1f, 1.4f, 1) * 2.2f); m.SetFloat("_Core", 5); m.SetFloat("_Soft", 0.45f); });
        var wood = Mat("RF_Wood", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.16f, 0.13f, 0.10f)); m.SetFloat("_Smoothness", 0.12f); });
        var post = Mat("RF_Post", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.09f, 0.075f, 0.06f)); m.SetFloat("_Smoothness", 0.08f); });
        ImportSettings(wood, post);
        TextureSettings();
        Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{n}_2k.jpg");
        ground.SetTexture("_LitterTex", Tex("brown_mud_leaves_01_diff")); ground.SetTexture("_LitterNrm", Tex("brown_mud_leaves_01_nor_gl"));
        ground.SetTexture("_DryTex", Tex("dry_ground_01_diff")); ground.SetTexture("_DryNrm", Tex("dry_ground_01_nor_gl"));
        ground.SetTexture("_MudTex", Tex("brown_mud_02_diff")); ground.SetTexture("_MudNrm", Tex("brown_mud_02_nor_gl"));
        ground.SetTexture("_PebbleTex", Tex("river_small_rocks_diff")); ground.SetTexture("_PebbleNrm", Tex("river_small_rocks_nor_gl"));
        foreach (var m in new[] { wood, post })
        {
            m.SetTexture("_BaseMap", Tex("weathered_planks_diff")); m.SetTexture("_BumpMap", Tex("weathered_planks_nor_gl"));
            m.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); m.EnableKeyword("_NORMALMAP");
        }
        wood.SetColor("_BaseColor", new Color(0.78f, 0.62f, 0.46f)); post.SetColor("_BaseColor", new Color(0.45f, 0.36f, 0.27f));
        var blobMat = Mat("RF_Blob", "ReedField/Blob");
        EditorUtility.SetDirty(ground); EditorUtility.SetDirty(wood); EditorUtility.SetDirty(post);
        AssetDatabase.SaveAssets();

        // ---------------------------------------------------------------- scene
        var scene = EditorSceneManager.GetActiveScene().path == ScenePath
            ? EditorSceneManager.GetActiveScene()
            : (System.IO.File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath) : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single));

        // Keep the field settings across rebuilds.
        string keep = null;
        var oldField = Object.FindAnyObjectByType<ReedField>();
        if (oldField && keepSettings) keep = JsonUtility.ToJson(oldField);
        foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);

        // moon
        var moonGo = new GameObject("Moon");
        var moon = moonGo.AddComponent<Light>();
        moon.type = LightType.Directional; moon.shadows = LightShadows.Soft; moon.shadowStrength = 0.85f;
        RenderSettings.sun = moon;
        RenderSettings.skybox = null;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.19f);

        // field root
        var rootGo = new GameObject("ReedField");
        rootGo.AddComponent<ReedInstancer>();
        var field = rootGo.AddComponent<ReedField>();
        if (keep != null) JsonUtility.FromJsonOverwrite(keep, field);
        field.riverWidth = Mathf.Max(field.riverWidth, 14f);   // 11-10: a wide, deep river along the south
        // 11-10 (Kido: every arena 2x per side, 4x the area): 112 x 88, channel, path and clearings laid out again for it
        field.arena = new Vector2(112, 88);
        field.channel = ReedLayout.Channel; field.path = ReedLayout.Path; field.clearings = ReedLayout.Clearings;
        _ = field.PathLine;   // rebuilds the cached channel and path curves from the new points
        // Old (56 x 44) layout points -> the new arena: inside the old arena they spread 2x, outside they keep their
        // distance from the arena edge, so bank silhouettes and river rocks stay on the bank and in the river.
        Vector2 Map(float x, float z)
        {
            const float ox = 28f, oz = 22f;
            float mx = Mathf.Abs(x) <= ox ? x * 2f : x + Mathf.Sign(x) * ox;
            float mz = Mathf.Abs(z) <= oz ? z * 2f : z + Mathf.Sign(z) * oz;
            return new Vector2(mx, mz);
        }
        field.susuki = new[] { FbxMesh("RF_Susuki_A"), FbxMesh("RF_Susuki_B"), FbxMesh("RF_Susuki_C") };
        field.susukiBroken = FbxMesh("RF_Susuki_Broken");
        field.susukiFar = FbxMesh("RF_Susuki_Far");
        field.susukiCard = FbxMesh("RF_Susuki_Card");
        field.reeds = new[] { FbxMesh("RF_Reed_A"), FbxMesh("RF_Reed_B") };
        field.lowGrass = FbxMesh("RF_LowGrass");
        field.litter = FbxMesh("RF_Litter");
        field.susukiMat = susuki; field.reedMat = galdae; field.grassMat = grass;
        field.groundMat = ground; field.waterMat = water; field.moon = moon;
        field.fogMat = fogMat; field.wispMat = wispMat;

        // landmarks first, so the reeds keep clear of them
        var props = new GameObject("Landmarks").transform;
        var keepOut = new List<Vector3>();
        void Prop(string fbx, float x, float z, float yaw, float scale, float clear, bool mapped = true)
        {
            if (mapped) { var m = Map(x, z); x = m.x; z = m.y; }
            // nothing stands in the channel or on its sandbar
            if (clear > 0 && field.ChannelDistance(new Vector2(x, z), out float chHalf) < chHalf + 3f) { Debug.LogWarning($"[ReedField] {fbx} at {x},{z} is in the channel, skipped"); return; }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(AV + fbx + ".fbx");
            if (!src) { Debug.LogWarning("[ReedField] missing " + fbx); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, props);
            go.transform.position = new Vector3(x, field.GroundHeight(new Vector2(x, z)) - 0.05f, z);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            if (clear > 0) keepOut.Add(new Vector3(x, z, clear));
        }
        Prop("AV_DeadTree_A", 17f, 10.5f, 30, 1.0f, 1.6f);
        Prop("AV_DeadTree_B", -24f, -13f, 140, 1.1f, 1.4f);
        Prop("AV_DeadTree_C", 25f, 19f, 250, 1.0f, 1.2f);
        Prop("AV_DeadTree_B", -24f, 18f, 80, 0.9f, 1.2f);
        Prop("AV_DeadTree_C", 8f, -20f, 300, 1.2f, 1.2f);
        Prop("AV_Jangseung", -5.6f, -19.5f, 10, 1f, 0.9f);
        Prop("AV_Jangseung", -1.8f, -19.9f, -8, 1f, 0.9f);
        Prop("AV_Jangseung", -19.5f, 15.5f, 120, 1f, 0.9f);
        Prop("AV_Sotdae", 15.2f, -14.2f, 0, 1f, 0.7f);
        Prop("AV_Sotdae", 16.6f, -15.6f, 40, 0.85f, 0.7f);
        Prop("AV_Sotdae", 17.4f, -13.8f, 75, 1.1f, 0.7f);
        // silhouettes on the banks outside the arena
        Prop("AV_DeadTree_A", -35f, 6f, 60, 1.3f, 0);
        Prop("AV_DeadTree_C", 34f, -5f, 200, 1.4f, 0);
        Prop("AV_DeadTree_B", 9f, 29f, 10, 1.3f, 0);
        Prop("AV_DeadTree_A", -21f, 30f, 150, 1.2f, 0);
        Prop("AV_DeadTree_C", 31f, 26f, 280, 1.2f, 0);
        Prop("AV_DeadTree_B", -12f, -43f, 100, 1.3f, 0);   // far bank (river is 14 m wide since 11-10)
        Prop("AV_DeadTree_A", 20f, -44f, 40, 1.1f, 0);
        Prop("AV_Sotdae", -18f, 5.5f, 20, 1f, 0.7f);
        Prop("AV_Crow", 16.2f, 11.6f, 200, 1f, 0);
        Prop("AV_Crow", 17.9f, 9.3f, 80, 1f, 0);
        Prop("AV_Crow", -13.2f, 10.4f, 140, 1f, 0);
        // landmarks for the new space (already in 112 x 88 coordinates): one in or beside each new clearing
        Prop("AV_DeadTree_A", -38f, -21f, 210, 1.1f, 1.6f, false);
        Prop("AV_Crow", -37.2f, -22.4f, 30, 1f, 0, false);
        Prop("AV_Sotdae", 12.5f, 33.5f, 10, 1f, 0.7f, false);
        Prop("AV_Sotdae", 14.1f, 32.2f, 55, 0.9f, 0.7f, false);
        Prop("AV_Sotdae", 11.2f, 31.6f, 95, 1.05f, 0.7f, false);
        Prop("AV_DeadTree_C", -48f, 35f, 120, 1.0f, 1.2f, false);
        Prop("AV_Jangseung", -44.5f, 29.5f, 200, 1f, 0.9f, false);
        Prop("AV_DeadTree_B", 47f, -33f, 20, 1.1f, 1.4f, false);
        Prop("AV_DeadTree_A", 50f, 12f, 300, 0.95f, 1.4f, false);
        Prop("AV_DeadTree_C", -52f, 2f, 160, 1.05f, 1.3f, false);
        Prop("AV_Sotdae", 28f, -6f, 30, 1f, 0.7f, false);
        Prop("AV_Jangseung", 36f, 40f, 170, 1f, 0.9f, false);
        // more bank silhouettes along the longer edges
        Prop("AV_DeadTree_B", -63f, -30f, 70, 1.3f, 0, false);
        Prop("AV_DeadTree_A", 64f, -22f, 240, 1.25f, 0, false);
        Prop("AV_DeadTree_C", -40f, 52f, 330, 1.3f, 0, false);
        Prop("AV_DeadTree_B", 44f, 53f, 190, 1.2f, 0, false);
        Prop("AV_DeadTree_A", -45f, -66f, 15, 1.2f, 0, false);
        Prop("AV_DeadTree_C", 48f, -67f, 260, 1.3f, 0, false);
        field.keepOut = keepOut.ToArray();

        // ground and water
        var groundMesh = field.BuildGroundMesh(); SaveMesh(groundMesh, "RF_Ground");
        var waterMesh = field.BuildWaterMesh(); SaveMesh(waterMesh, "RF_Water");
        AssetDatabase.SaveAssets();
        var gGo = new GameObject("RF_Ground");
        gGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/RF_Ground.asset");
        gGo.AddComponent<MeshRenderer>().sharedMaterial = ground;
        gGo.AddComponent<MeshCollider>().sharedMesh = gGo.GetComponent<MeshFilter>().sharedMesh;
        var wGo = new GameObject("RF_Water");
        wGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/RF_Water.asset");
        var wr = wGo.AddComponent<MeshRenderer>(); wr.sharedMaterial = water; wr.shadowCastingMode = ShadowCastingMode.Off;

        // ---------------------------------------------------------------- the deep, fast river (Kido 11-10)
        var riverMat = Mat("RF_River", "ReedField/River");
        riverMat.SetTexture("_SurfaceMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/URP_Water/WaterSurface_single.tif"));
        riverMat.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/URP_Water/WaterFoam.png"));
        riverMat.SetVector("_RiverZ", field.RiverParams); RFWaterRefresh.ApplyRiverLook(riverMat);   // 11-14: zw = mesh edges (the shader fades out there)
        EditorUtility.SetDirty(riverMat);
        var riverMesh = field.BuildRiverMesh(); SaveMesh(riverMesh, "RF_River");
        AssetDatabase.SaveAssets();
        var rGo = new GameObject("RF_River");
        rGo.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Gen}/RF_River.asset");
        var rr = rGo.AddComponent<MeshRenderer>(); rr.sharedMaterial = riverMat; rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;
        // rocks breaking the current (LookStudy rock set): whitecaps and wakes come from RFRiver
        var rocksRoot = new GameObject("RiverRocks").transform;
        rocksRoot.gameObject.AddComponent<RFRiver>();
        var rockMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/BambooArena/Materials/BA_Stone2.mat");
        var rockSpots = new[] { new Vector3(-17f, -28.5f, 0.9f), new Vector3(-4f, -31f, 1.2f), new Vector3(9f, -27.5f, 0.7f), new Vector3(21f, -30f, 1.0f), new Vector3(-27f, -32f, 0.8f) };
        var rrnd = new System.Random(field.seed + 41);
        var moreRocks = new[] { new Vector3(-62f, -52f, 1.0f), new Vector3(-44f, -55f, 0.8f), new Vector3(38f, -53f, 1.1f), new Vector3(58f, -56f, 0.9f), new Vector3(2f, -57f, 0.8f) };
        foreach (var rs0 in rockSpots.Select(r => { var m = Map(r.x, r.y); return new Vector3(m.x, m.y, r.z); }).Concat(moreRocks))
        {
            var rs = rs0;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/LookStudy/Stages/Models/Rock_rock{1 + rrnd.Next(13):00}.fbx");
            if (!src) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, rocksRoot);
            go.transform.position = new Vector3(rs.x, field.waterLevel - 0.25f, rs.y);
            go.transform.rotation = Quaternion.Euler((float)rrnd.NextDouble() * 20 - 10, (float)rrnd.NextDouble() * 360, (float)rrnd.NextDouble() * 20 - 10);
            go.transform.localScale = Vector3.one * rs.z;
            if (rockMat) foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterials = Enumerable.Repeat(rockMat, r.sharedMaterials.Length).ToArray();
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }

        // ---------------------------------------------------------------- the moored ferry boat, tied to the 장승 at the
        // south landing (the path's entrance); the current holds it out downstream (west) on a taut rope.
        {
            var boatSrc = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/RF_Boat.fbx");
            var jang = props.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith("AV_Jangseung") && Mathf.Abs(t.position.x - Map(-1.8f, -19.9f).x) < 0.1f);
            if (boatSrc && jang)
            {
                var boatWood = Mat("RF_BoatWood", "Universal Render Pipeline/Lit");
                var boatDark = Mat("RF_BoatWoodDark", "Universal Render Pipeline/Lit");
                var boatIron = Mat("RF_BoatIron", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.07f, 0.065f, 0.06f)); m.SetFloat("_Metallic", 0.6f); m.SetFloat("_Smoothness", 0.35f); });
                var plank = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/AbandonedVillage/Textures/weathered_brown_planks_diff_1k.jpg");
                var plankN = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/AbandonedVillage/Textures/weathered_brown_planks_nor_gl_1k.jpg");
                foreach (var m in new[] { boatWood, boatDark })
                {
                    m.SetTexture("_BaseMap", plank); m.SetTexture("_BumpMap", plankN); m.EnableKeyword("_NORMALMAP");
                    m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Cull", 0f);   // two-sided: thin plank shell
                }
                boatWood.SetColor("_BaseColor", new Color(0.72f, 0.58f, 0.42f)); boatDark.SetColor("_BaseColor", new Color(0.42f, 0.33f, 0.24f));
                foreach (var m in new[] { boatWood, boatDark, boatIron }) EditorUtility.SetDirty(m);

                var moor = new GameObject("MooredBoat");
                var tie = new GameObject("RopeTie").transform;
                tie.SetParent(moor.transform, false);
                // tied round the 장승's body about knee-to-waist high, on its river side
                tie.position = jang.position + new Vector3(0, 0.95f, -0.18f);
                var b = (GameObject)PrefabUtility.InstantiatePrefab(boatSrc, moor.transform);
                b.name = "RF_Boat";
                foreach (var r in b.GetComponentsInChildren<MeshRenderer>()) { r.sharedMaterials = new[] { boatWood, boatDark, boatIron }; r.shadowCastingMode = ShadowCastingMode.On; }
                foreach (var c in b.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var ropeGo = new GameObject("Rope");
                ropeGo.transform.SetParent(moor.transform, false);
                var lr = ropeGo.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.widthMultiplier = 0.035f; lr.numCapVertices = 2; lr.positionCount = 14;
                lr.shadowCastingMode = ShadowCastingMode.On; lr.textureMode = LineTextureMode.Tile;
                lr.sharedMaterial = Mat("RF_Rope", "Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", new Color(0.36f, 0.29f, 0.19f)); m.SetFloat("_Smoothness", 0.05f); });
                var boat = moor.AddComponent<RFBoat>();
                boat.boat = b.transform; boat.tiePoint = tie; boat.rope = lr;
                boat.waterY = field.waterLevel; boat.ropeLength = 6.5f; boat.flow = new Vector2(-1, 0);
                keepOut.Add(new Vector3(jang.position.x, jang.position.z, 1.2f));
                field.keepOut = keepOut.ToArray();
            }
            else Debug.LogWarning("[ReedField] boat or landing 장승 missing");
        }

        // boardwalk where the path meets the water, and a ruined stretch at the south entrance
        var bwRoot = new GameObject("Boardwalk").transform;
        var segs = new[] { "RF_Boardwalk_A", "RF_Boardwalk_B", "RF_Boardwalk_C" }
            .Select(n => AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{n}.fbx")).ToArray();
        var line = field.PathLine;
        var rnd = new System.Random(field.seed + 3);
        float acc = 0, next = 0;
        for (int i = 1; i < line.Count; i++)
        {
            acc += Vector2.Distance(line[i - 1], line[i]);
            if (acc < next) continue;
            // a segment from the point 4 m back to here
            int j = i; float back = 0;
            while (j > 0 && back < 4f) { back += Vector2.Distance(line[j - 1], line[j]); j--; }
            if (back < 3.5f) continue;
            Vector2 a = line[j], b = line[i], mid = (a + b) * 0.5f;
            float cd = field.ChannelDistance(mid, out float half);
            bool nearWater = cd < half + 3.5f;
            bool entrance = acc < 14f;
            if (!nearWater && !entrance) continue;
            next = acc + 4.05f;
            int v = nearWater ? (rnd.NextDouble() < 0.6 ? 0 : 1) : (rnd.NextDouble() < 0.5 ? 1 : 2);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(segs[v], bwRoot);
            var dir = new Vector3(b.x - a.x, 0, b.y - a.y).normalized;
            go.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.right));
            go.transform.position = Vector3.zero;
            var mr = go.GetComponentInChildren<MeshRenderer>();
            var wb = mr.bounds;
            float top = Mathf.Max(field.GroundHeight(mid), 0.06f) + 0.22f;
            go.transform.position = new Vector3(mid.x - wb.center.x, top - wb.max.y, mid.y - wb.center.z);
            // walkable deck
            var mf = mr.GetComponent<MeshFilter>();
            var lb = mf.sharedMesh.bounds;
            var col = mr.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(lb.center.x, lb.max.y - 0.06f, lb.center.z);
            col.size = new Vector3(lb.size.x, 0.12f, lb.size.z);
            mr.shadowCastingMode = ShadowCastingMode.On;
            // Soft contact shadow on the ground or water under the deck, so it does not float.
            var blob = new GameObject("ContactShadow");
            blob.transform.SetParent(go.transform, true);
            float gy = Mathf.Max(field.GroundHeight(mid), field.waterLevel) + 0.03f;
            blob.transform.position = new Vector3(mid.x, gy, mid.y);
            blob.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
            blob.transform.localScale = new Vector3(2.0f, 4.6f, 1f);
            blob.transform.SetParent(go.transform, true);
            blob.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var br = blob.AddComponent<MeshRenderer>(); br.sharedMaterial = blobMat; br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;
        }

        // Static props: shadows baked into a texture per mood, no realtime shadow casting (saves the shadow pass).
        foreach (var root in new[] { props, bwRoot })
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.sharedMaterial && r.sharedMaterial.shader.name == "ReedField/Blob") continue;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
        }
        gGo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(gGo, StaticEditorFlags.BatchingStatic);
        field.propShadowSunset = BakePropShadow(field, new[] { props, bwRoot }, ReedField.Mood.Sunset, "RF_PropShadow_Sunset");
        field.propShadowMoonlit = BakePropShadow(field, new[] { props, bwRoot }, ReedField.Mood.Moonlit, "RF_PropShadow_Moonlit");

        // place the reeds now that the keep-out spots are known
        field.Generate();
        field.ApplyMood();
        field.BakeReedShadow();

        // fog sheets lying in the reeds
        var fogRoot = new GameObject("GroundFog").transform;
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var fr = new System.Random(field.seed + 11);
        for (int i = 0; i < 46 * 4; i++)
        {
            var go = new GameObject("FogSheet");
            go.transform.SetParent(fogRoot, false);
            float x = (float)(fr.NextDouble() - 0.5) * (field.arena.x + 30f), z = (float)(fr.NextDouble() - 0.5) * (field.arena.y + 30f);
            float y = field.height * Mathf.Lerp(0.25f, 0.6f, (float)fr.NextDouble());
            go.transform.position = new Vector3(x, y, z);
            go.transform.rotation = Quaternion.Euler(90, (float)fr.NextDouble() * 360f, 0);
            go.transform.localScale = Vector3.one * Mathf.Lerp(7f, 12f, (float)fr.NextDouble());
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = fogMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }

        // 도깨비불
        var spirits = new GameObject("Spirits").transform;
        var wispSpots = new[] { new Vector3(-40, 0, 4), new Vector3(18, 0, 28), new Vector3(40, 0, -16), new Vector3(-16, 0, -24),
                                new Vector3(-36, 0, -30), new Vector3(30, 0, 24), new Vector3(-48, 0, 34), new Vector3(48, 0, -30) };
        foreach (var s in wispSpots)
        {
            var go = new GameObject("Dokkaebibul");
            go.transform.SetParent(spirits, false);
            go.transform.position = new Vector3(s.x, field.GroundHeight(new Vector2(s.x, s.z)), s.z);
            var w = go.AddComponent<RFWisp>();
            var g = new GameObject("Glow");
            g.transform.SetParent(go.transform, false); g.transform.localScale = Vector3.one * 0.7f;
            g.AddComponent<MeshFilter>().sharedMesh = quad;
            var gr = g.AddComponent<MeshRenderer>(); gr.sharedMaterial = wispMat; gr.shadowCastingMode = ShadowCastingMode.Off;
            var l = new GameObject("Light").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.type = LightType.Point; l.range = 7f; l.color = new Color(0.45f, 0.85f, 1f); l.intensity = 2.2f; l.shadows = LightShadows.None;
            w.glow = gr; w.glowLight = l;
        }
        // water: ripples into the water normals, spray and droplets when walking through it
        {
            var wfx = new GameObject("WaterFX").transform;
            wfx.SetParent(spirits, false);
            var rw = wfx.gameObject.AddComponent<RFWater>();
            var rippleMat = Mat("RF_Ripple", "ReedField/WaterFX Ripple");
            rippleMat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/URP_Water/RippleNormal.psd"));
            var rp = MakePS(wfx, "Ripples", rippleMat, RFWater.FxLayer, 1.6f, 0f, ParticleSystemRenderMode.HorizontalBillboard);
            var sol = rp.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.25f, 1, 1f));
            var col = rp.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            rw.ripples = rp;

            var sprayMat = ParticleMat("RF_Spray", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/BoatAttack/WaterSplashes.tif"));
            sprayMat.SetColor("_BaseColor", new Color(1f, 0.93f, 0.82f, 0.85f));
            var sp = MakePS(wfx, "Spray", sprayMat, 0, 0.45f, 1.4f, ParticleSystemRenderMode.Billboard);
            var tsa = sp.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 4; tsa.numTilesY = 4;
            tsa.animation = ParticleSystemAnimationType.WholeSheet; tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0f);
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 15f);
            var spSol = sp.sizeOverLifetime; spSol.enabled = true; spSol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.6f, 1, 1.3f));
            var spCol = sp.colorOverLifetime; spCol.enabled = true; spCol.color = g;
            var spRot = sp.main; spRot.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            rw.spray = sp;

            var dropMat = ParticleMat("RF_Droplet", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/ThirdParty/URP_Water/Circle.psd"));
            dropMat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.88f, 0.9f));
            var dp = MakePS(wfx, "Droplets", dropMat, 0, 0.55f, 2.6f, ParticleSystemRenderMode.Stretch);
            var dm = dp.main; dm.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3.2f); dm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            var dsh = dp.shape; dsh.enabled = true; dsh.shapeType = ParticleSystemShapeType.Cone; dsh.angle = 35f; dsh.radius = 0.15f; dsh.rotation = new Vector3(-90, 0, 0);
            dp.GetComponent<ParticleSystemRenderer>().velocityScale = 0.05f;
            rw.droplets = dp;
        }

        // reed fluff and leaf slivers drifting in the air around the camera view
        {
            var fluffMat = Mat("RF_Fluff", "ReedField/Fluff");
            var go = new GameObject("AirFluff");
            go.transform.SetParent(spirits, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 11f);
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.07f, 0.22f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.7f, 0.8f), new Color(0.85f, 0.62f, 0.38f, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;
            main.gravityModifier = 0f;
            var em = ps.emission; em.rateOverTime = 11f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(44f, 4f, 34f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1f, 1.4f); vel.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.12f); vel.z = new ParticleSystem.MinMaxCurve(1f, 1.4f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.7f; noise.frequency = 0.35f; noise.scrollSpeed = 0.3f; noise.quality = ParticleSystemNoiseQuality.Low;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.75f), new GradientAlphaKey(0, 1) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = fluffMat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.AddComponent<RFFluff>();
            ps.Play();
        }

        // camera at the RoomRun angle (height 17, back 14.5, fov 30)
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 30; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.86f, 0.56f, 0.32f);
        camGo.transform.position = new Vector3(0, 17f, -14.5f);
        camGo.transform.LookAt(Vector3.zero);
        var cd2 = camGo.AddComponent<UniversalAdditionalCameraData>(); cd2.renderPostProcessing = true;
        camGo.AddComponent<AudioListener>();

        // post
        var profPath = $"{Root}/RF_PostFX.asset";
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
        if (!prof)
        {
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, profPath);
            var bloom = prof.Add<Bloom>(true); bloom.intensity.Override(0.75f); bloom.threshold.Override(0.85f); bloom.scatter.Override(0.72f);
            var vig = prof.Add<Vignette>(true); vig.intensity.Override(0.4f); vig.smoothness.Override(0.5f);
            var ca = prof.Add<ColorAdjustments>(true); ca.contrast.Override(16f); ca.saturation.Override(-28f); ca.postExposure.Override(0.15f);
            var grain = prof.Add<FilmGrain>(true); grain.intensity.Override(0.28f); grain.type.Override(FilmGrainLookup.Medium1);
            var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
        }
        if (!prof.Has<WhiteBalance>())
        {
            var wbc = prof.Add<WhiteBalance>(true); wbc.temperature.Override(12f);
            AssetDatabase.AddObjectToAsset(wbc, prof); EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        }
        var vol = new GameObject("PostFX").AddComponent<Volume>();
        vol.isGlobal = true; vol.sharedProfile = prof;
        field.volume = vol;

        // Invisible walls on the arena edge (RoomRun adds its own too; these keep the scene closed on its own).
        var walls = new GameObject("ArenaWalls").transform;
        float ax = field.arena.x * 0.5f, az = field.arena.y * 0.5f;
        void WallBox(Vector3 c, Vector3 sz) { var w = new GameObject("Wall"); w.transform.SetParent(walls, false); w.transform.position = c; w.AddComponent<BoxCollider>().size = sz; }
        WallBox(new Vector3(-ax - 0.5f, 2, 0), new Vector3(1, 6, field.arena.y + 2));
        WallBox(new Vector3(ax + 0.5f, 2, 0), new Vector3(1, 6, field.arena.y + 2));
        WallBox(new Vector3(0, 2, -az - 0.5f), new Vector3(field.arena.x + 2, 6, 1));
        WallBox(new Vector3(0, 2, az + 0.5f), new Vector3(field.arena.x + 2, 6, 1));
        field.Generate();
        field.ApplyMood();
        field.BakeReedShadow();

        // screen filters (F10). The rebuild clears the scene, so the filter has to be put back every time (11-10: it was
        // lost on a rebuild and F10 did nothing in the reed field).
        InkFilterTools.Ensure();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddRoomRunRoom();
        Debug.Log($"[ReedField] built: {Object.FindAnyObjectByType<ReedInstancer>().Count} reed instances");
    }

    // Ray-casts toward the sun from every ground cell against temporary mesh colliders on the props, blurs the result
    // and saves it as a greyscale texture (1 = lit). Done in the editor only; the props never cast realtime shadows.
    static Texture2D BakePropShadow(ReedField f, Transform[] roots, ReedField.Mood mood, string name)
    {
        var temp = new List<Collider>();
        foreach (var root in roots)
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<MeshRenderer>()?.sharedMaterial?.shader.name == "ReedField/Blob") continue;
            var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; temp.Add(mc);
        }
        Physics.SyncTransforms();
        var sun = f.SunFor(mood);
        var toSun = -(Quaternion.Euler(sun.x, sun.y, 0) * Vector3.forward);
        var rect = f.ShadowRect;
        int w = Mathf.CeilToInt(rect.z / ReedField.ShadowCell), h = Mathf.CeilToInt(rect.w / ReedField.ShadowCell);
        var occ = new float[w * h];
        int mask = ~0;
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            float wx = rect.x + (x + 0.5f) * ReedField.ShadowCell, wz = rect.y + (z + 0.5f) * ReedField.ShadowCell;
            var gp = new Vector3(wx, f.GroundHeight(new Vector2(wx - f.transform.position.x, wz - f.transform.position.z)) + 0.05f, wz);
            // a few rays per cell for soft edges
            float hit = 0;
            for (int s = 0; s < 4; s++)
            {
                var o = gp + new Vector3((s & 1) * 0.25f - 0.125f, 0, (s >> 1) * 0.25f - 0.125f);
                if (Physics.Raycast(o, toSun, out var rh, 60f, mask, QueryTriggerInteraction.Ignore) && temp.Contains(rh.collider)) hit += 0.25f;
            }
            occ[z * w + x] = hit;
        }
        foreach (var c in temp) Object.DestroyImmediate(c);
        var tmp = new float[occ.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int z = 0; z < h; z++) for (int x = 0; x < w; x++)
            {
                float sum = 0; int n = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, zz = z + dz; if (xx < 0 || zz < 0 || xx >= w || zz >= h) continue;
                    sum += occ[zz * w + xx]; n++;
                }
                tmp[z * w + x] = sum / n;
            }
            (occ, tmp) = (tmp, occ);
        }
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false, true);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) { byte v = (byte)Mathf.RoundToInt(255f * (1f - occ[i] * 0.75f)); px[i] = new Color32(v, v, v, 255); }
        tex.SetPixels32(px); tex.Apply();
        var path = $"{Gen}/{name}.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp; imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Registers the field as a dev-only RoomRun room (F8 in Play) without touching any other room.
    public static void AddRoomRunRoom()
    {
        const string cfgPath = "Assets/Game/RoomRun/RoomRunConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<ScriptableObject>(cfgPath);
        if (!cfg) return;
        var so = new SerializedObject(cfg);
        var rooms = so.FindProperty("rooms");
        for (int i = 0; i < rooms.arraySize; i++)
            if (rooms.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == "reeds")
            {
                // already there: only keep the size in step with the arena
                var ex = rooms.GetArrayElementAtIndex(i);
                var f = Object.FindAnyObjectByType<ReedField>();
                ex.FindPropertyRelative("size").vector2Value = f ? f.arena : new Vector2(112, 88);
                so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets();
                return;
            }
        int at = rooms.arraySize;
        rooms.InsertArrayElementAtIndex(at - 1);   // copies the last room, then every field is set below
        var r = rooms.GetArrayElementAtIndex(at);
        r.FindPropertyRelative("id").stringValue = "reeds";
        r.FindPropertyRelative("displayName").stringValue = "갈대밭";
        r.FindPropertyRelative("mapCell").vector2IntValue = new Vector2Int(5, 0);
        r.FindPropertyRelative("scenePath").stringValue = ScenePath;
        r.FindPropertyRelative("groundMeshName").stringValue = "";
        r.FindPropertyRelative("center").vector3Value = Vector3.zero;
        r.FindPropertyRelative("size").vector2Value = new Vector2(112, 88);
        r.FindPropertyRelative("fitToStage").boolValue = false;
        r.FindPropertyRelative("floorColor").colorValue = new Color(0.08f, 0.08f, 0.09f);
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

// Renders the scene camera (and a closer view) to ArtSource/ReedField for a quick check.
public static class RFCapture
{
    [MenuItem("Tools/Dev/Reed Field/Capture Check Shots (no fog sheets)")]
    public static void CaptureNoFog()
    {
        var f = GameObject.Find("GroundFog");
        if (f) f.SetActive(false);
        try { Capture(); } finally { if (f) f.SetActive(true); }
    }

    [MenuItem("Tools/Dev/Reed Field/Capture Check Shots")]
    public static void Capture()
    {
        var dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "ReedField");
        System.IO.Directory.CreateDirectory(dir);
        var field = Object.FindAnyObjectByType<ReedField>();
        var cam = Camera.main;
        if (!field || !cam) { Debug.LogError("[ReedField] open the reed field scene first"); return; }
        field.SetGlobals();
        ShaderUtil.allowAsyncCompilation = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation;
        var views = new (string, Vector3, Vector3)[]
        {
            ("game", new Vector3(0, 17f, -14.5f), Vector3.zero),
            ("wide", new Vector3(0, 40f, -34f), Vector3.zero),
            ("close", new Vector3(-12, 7f, 2f), new Vector3(-14, 0.5f, 10f)),
            ("eye", new Vector3(0, 2.6f, -1.5f), new Vector3(0, 1.4f, 8f)),
        };
        foreach (var (n, pos, look) in views)
        {
            cam.transform.position = pos; cam.transform.LookAt(look);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"shot_{n}.png"), tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        cam.transform.SetPositionAndRotation(p0, r0);
        ShaderUtil.allowAsyncCompilation = true;
        Debug.Log("[ReedField] shots saved to " + dir);
    }
}

// Play-mode helper: jumps the RoomRun run into the reed field (same as pressing F8).
public static class RFPlayJump
{
    [MenuItem("Tools/Dev/Reed Field/Play: Jump To Reed Field")]
    public static void Jump()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[ReedField] enter Play first"); return; }
        var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RoomRun.RunDirector")).FirstOrDefault(x => x != null);
        var inst = t?.GetProperty("Instance")?.GetValue(null);
        if (inst == null) { Debug.LogWarning("[ReedField] no RunDirector"); return; }
        t.GetMethod("DevJump").Invoke(inst, new object[] { "reeds" });
        Debug.Log("[ReedField] jumped");
    }
}

// Play-mode helper: puts the player in the next arena corner (to check the edges from the RoomRun camera).
public static class RFPlayCorners
{
    static int next;

    [MenuItem("Tools/Dev/Reed Field/Play: Next Corner")]
    public static void NextCorner()
    {
        if (!Application.isPlaying) return;
        var f = Object.FindAnyObjectByType<ReedField>();
        var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RoomRun.RunDirector")).FirstOrDefault(x => x != null);
        var run = t?.GetProperty("Instance")?.GetValue(null);
        if (!f || run == null) return;
        var player = t.GetProperty("Player").GetValue(run) as Component;
        float hx = f.arena.x * 0.5f - 1.5f, hz = f.arena.y * 0.5f - 1.5f;
        var corners = new[] { new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz) };
        var c = corners[next++ % 4];
        player.GetType().GetMethod("PlaceAt").Invoke(player, new object[] { c, 0f });
        var rig = t.GetProperty("Rig").GetValue(run);
        rig?.GetType().GetMethod("Snap")?.Invoke(rig, null);
        Debug.Log($"[ReedField] player at corner {c}");
    }
}

// Renders the scene camera N times at 1920x1080 and reports the average frame time (GPU synced every frame),
// with the baked shadows on, off, and with realtime prop shadows on, for a fair before/after.
public static class RFBenchmark
{
    [MenuItem("Tools/Dev/Reed Field/Benchmark (editor)")]
    public static void Run()
    {
        var cam = Camera.main; var f = Object.FindAnyObjectByType<ReedField>();
        if (!cam || !f) return;
        cam.transform.position = new Vector3(0, 17f, -14.5f); cam.transform.LookAt(Vector3.zero);
        var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
        var props = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.transform.root.name == "Landmarks" || r.transform.root.name == "Boardwalk").ToArray();
        double Measure(int n)
        {
            f.SetGlobals();
            for (int i = 0; i < 60; i++) cam.Render();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                cam.Render();
                if (i % 10 == 9 || i == n - 1) UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
            }
            return sw.Elapsed.TotalMilliseconds / n;
        }
        Measure(100); // warm up
        double best = double.MaxValue;
        for (int k = 0; k < 3; k++) best = System.Math.Min(best, Measure(200));
        var inst = f.GetComponent<ReedInstancer>();
        Debug.Log($"[ReedField] benchmark: {best:0.00} ms per frame (best of 3 x 200, 1920x1080, RoomRun view), {inst.Count} reed instances, drawn {inst.LastDrawnInstances} ({inst.LastDrawnTris / 1000000.0:0.0} M tris)");
        cam.targetTexture = null; Object.DestroyImmediate(rt);
    }
}

// Play-mode helper: walks the player through the water channel for 3 s (to see ripples and splashes).
public static class RFPlayWade
{
    static double start; static Component player; static System.Reflection.MethodInfo place;
    static readonly Vector3 from = new Vector3(-10f, 0, 0.5f), to = new Vector3(-4f, 0, -5.5f);

    [MenuItem("Tools/Dev/Reed Field/Play: Wade Through Water")]
    public static void Wade()
    {
        if (!Application.isPlaying) return;
        var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("RoomRun.RunDirector")).FirstOrDefault(x => x != null);
        var run = t?.GetProperty("Instance")?.GetValue(null);
        if (run == null) return;
        player = t.GetProperty("Player").GetValue(run) as Component;
        place = player.GetType().GetMethod("PlaceAt");
        start = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Step; EditorApplication.update += Step;
    }

    static void Step()
    {
        float k = (float)((EditorApplication.timeSinceStartup - start) / 3.0);
        if (!Application.isPlaying || !player || k > 1f) { EditorApplication.update -= Step; return; }
        place.Invoke(player, new object[] { Vector3.Lerp(from, to, k), 0f });
    }
}
