// Builds the valley stage scene and a test player. Env LS_PLAYER = exe path.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LookStudyStageImport : AssetPostprocessor
{
    static bool InStage(string p) => p.Replace('\\', '/').Contains("/Stages/");

    void OnPreprocessModel()
    {
        if (!InStage(assetPath)) return;
        var mi = (ModelImporter)assetImporter;
        mi.isReadable = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Off;
    }

    void OnPreprocessTexture()
    {
        if (!InStage(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        var n = Path.GetFileNameWithoutExtension(assetPath);
        if (n.EndsWith("_nor_gl") || n.EndsWith("_normal")) ti.textureType = TextureImporterType.NormalMap;
        ti.maxTextureSize = 1024;
    }
}

public static class LookStudyValleyEditor
{
    const string Root = "Assets/Art/LookStudy/Stages";
    const string Models = Root + "/Models/";
    const string Tex = Root + "/Textures/";
    static string ScenePath = "Assets/Art/LookStudy/Stages/Valley.unity";

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    static Texture2D T(string path) { var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path); if (!t) Debug.LogError("[LS] no texture " + path); return t; }

    static Material Mat(string name, string shader)
    {
        Directory.CreateDirectory(Root + "/Materials");
        var path = $"{Root}/Materials/{name}.mat";
        var s = Shader.Find(shader);
        if (!s) { Debug.LogError("[LS] missing shader " + shader); return null; }
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
        m.shader = s;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh M(string n)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Models + n + ".fbx")) if (o is Mesh me) return me;
        Debug.LogError("[LS] no mesh " + n); return null;
    }

    static Material RockMat(string name, string texBase)
    {
        var m = Mat("V_Rock_" + name, "LookStudy/StageRock");
        m.SetTexture("_MainTex", T(Models + texBase + "_diff.png"));
        m.SetTexture("_NormalTex", T(Models + texBase + "_nor_gl.png"));
        m.SetTexture("_MossTex", T(Tex + "mossy_rock_diff.jpg"));
        m.SetColor("_Granite", Hex("#e2d9c8")); m.SetFloat("_Remap", 0.8f);
        m.SetColor("_MossTint", Hex("#4a4f40")); m.SetFloat("_MossAmt", 0.08f); m.SetFloat("_Wet", 0.6f);   // 11-14 Kido: no green on the boulders, only a hint
        return m;
    }

    static Material Particle(string name, Texture2D tex, bool additive)
    {
        var m = Mat(name, "Universal Render Pipeline/Particles/Unlit");
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Surface", 1f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Blend", additive ? 2f : 0f); // 0 alpha, 2 additive
        m.SetInt("_SrcBlend", additive ? (int)BlendMode.SrcAlpha : (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);
        m.renderQueue = (int)RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (additive) m.EnableKeyword("_ALPHAMODULATE_ON"); else m.DisableKeyword("_ALPHAMODULATE_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        return m;
    }

    static Texture2D Puff(string file, int size, float soft, float noise)
    {
        var path = Tex + file;
        if (!File.Exists(path))
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1;
                float n = Mathf.PerlinNoise(u * 2.3f + 4, v * 2.3f + 9) * 0.6f + Mathf.PerlinNoise(u * 5.7f + 1, v * 5.7f + 2) * 0.4f;
                float a = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v) - (1 - n) * noise);
                a = Mathf.SmoothStep(0, 1, a / soft);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            Object.DestroyImmediate(t);
        }
        return T(path);
    }

    public static GameObject BuildScene()
    {
        AssetDatabase.Refresh();
        var terrain = Mat("V_Terrain", "LookStudy/StageTerrain");
        terrain.SetTexture("_PebbleTex", T(Tex + "river_small_rocks_diff.jpg")); terrain.SetTexture("_PebbleNor", T(Tex + "river_small_rocks_nor_gl.jpg"));
        terrain.SetTexture("_GravelTex", T(Tex + "gray_rocks_diff.jpg")); terrain.SetTexture("_GravelNor", T(Tex + "gray_rocks_nor_gl.jpg"));
        terrain.SetTexture("_RockTex", T(Tex + "rock_face_diff.jpg")); terrain.SetTexture("_RockNor", T(Tex + "rock_face_nor_gl.jpg"));
        terrain.SetTexture("_ForestTex", T(Tex + "forest_ground_04_diff.jpg")); terrain.SetTexture("_ForestNor", T(Tex + "forest_ground_04_nor_gl.jpg"));
        terrain.SetColor("_RockTint", Hex("#cfc7b8")); terrain.SetColor("_PebbleTint", Hex("#ded6c4")); terrain.SetColor("_ForestTint", Hex("#b0b28e"));

        var water = Mat("V_Water", "LookStudy/StageWater");
        water.SetColor("_Shallow", Hex("#2e4f4c")); water.SetColor("_Mid", Hex("#123431")); water.SetColor("_Deep", Hex("#061214")); water.SetColor("_Foam", Hex("#c9d3cc"));
        water.SetFloat("_Absorb", 1.0f); water.SetFloat("_Refract", 0.035f);
        var fall = Mat("V_Fall", "LookStudy/StageFall");
        fall.SetColor("_Col", Hex("#a9c0bc"));

        var puffSoft = Puff("ls_puff.png", 128, 0.9f, 0.55f);
        var dot = Puff("ls_dot.png", 32, 1f, 0f);
        var mote = Particle("V_Mote", dot, true);
        var mist = Particle("V_Mist", puffSoft, false);
        var wisp = Particle("V_Wisp", puffSoft, true);
        var splash = Particle("V_Splash", dot, false);

        var r1 = RockMat("Set1", "rock_moss_set_01");
        var r2 = RockMat("Set2", "rock_moss_set_02");
        var b1 = RockMat("B1", "boulder_01");
        var b3 = RockMat("B3", "namaqualand_boulder_03");

        LookStudyValley.Prop P(string mesh, Material mat) => new LookStudyValley.Prop { mesh = M(mesh), material = mat };

        var branches = Mat("V_PineBranch", "LookStudy/Foliage");
        branches.SetTexture("_MainTex", T(Models + "M_TreeAtlas_baseColor.png")); branches.SetFloat("_Remap", 0f); branches.SetFloat("_Bend", 0.03f); branches.SetFloat("_Cutoff", 0.45f);
        var trunk = Mat("V_PineTrunk", "LookStudy/Foliage");
        trunk.SetTexture("_MainTex", T(Models + "Trunk_baseColor.png")); trunk.SetFloat("_Remap", 0f); trunk.SetFloat("_Bend", 0f); trunk.SetFloat("_Cutoff", 0f);
        var dead = Mat("V_PineDead", "LookStudy/Foliage");
        dead.SetTexture("_MainTex", T(Models + "Bark_Mat_baseColor.png")); dead.SetFloat("_Remap", 0f); dead.SetFloat("_Bend", 0f); dead.SetFloat("_Cutoff", 0f);
        var grass = Mat("V_Grass", "LookStudy/Foliage");
        grass.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/LookStudy/Models/Real/GrassGrass_MAT_baseColor.png"));
        grass.SetFloat("_Remap", 1f); grass.SetColor("_Dark", Hex("#1c3a2c")); grass.SetColor("_Light", Hex("#7d9a4c")); grass.SetFloat("_LumScale", 2.1f); grass.SetFloat("_Bend", 0.2f);

        var vol = ScriptableObject.CreateInstance<VolumeProfile>();
        var volPath = Root + "/V_Volume.asset";
        AssetDatabase.DeleteAsset(volPath);
        AssetDatabase.CreateAsset(vol, volPath);
        var bloom = vol.Add<Bloom>(true); bloom.threshold.Override(0.85f); bloom.intensity.Override(0.75f); bloom.scatter.Override(0.72f);
        var ca = vol.Add<ColorAdjustments>(true); ca.saturation.Override(-18f); ca.contrast.Override(14f); ca.postExposure.Override(0.55f); ca.colorFilter.Override(Hex("#dfe9ea"));
        var vig = vol.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.55f);
        var tone = vol.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
        var lgg = vol.Add<LiftGammaGain>(true); lgg.lift.Override(new Vector4(0.96f, 1f, 1.02f, -0.03f)); lgg.gain.Override(new Vector4(1f, 1f, 1f, 0f));
        foreach (var c in vol.components) AssetDatabase.AddObjectToAsset(c, vol);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Hex("#141c20"); cam.farClipPlane = 400; cam.nearClipPlane = 0.3f; cam.allowHDR = true; cam.fieldOfView = 38;
        var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cd.requiresDepthOption = CameraOverrideOption.On; cd.renderShadows = true; cd.requiresColorOption = CameraOverrideOption.On;

        var sun = new GameObject("Key Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = Color.white; sun.intensity = 1f; sun.shadows = LightShadows.Soft; sun.shadowNormalBias = 0.5f; sun.shadowBias = 0.4f;
        RenderSettings.sun = sun;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;

        var volGo = new GameObject("Global Volume"); var v = volGo.AddComponent<Volume>(); v.isGlobal = true; v.sharedProfile = vol;

        var stage = new GameObject("Valley");
        stage.SetActive(false);
        var boot = stage.AddComponent<LookStudyValleyBoot>();
        var w = stage.AddComponent<LookStudyValley>();
        w.terrain = terrain; w.water = water; w.fall = fall; w.moteMat = mote; w.mistMat = mist; w.wispMat = wisp;
        w.bigRocks = new[] { P("Rock_rock01", r1), P("Rock_rock02", r1), P("Rock_rock03", r1), P("Rock_rock04", r1), P("Rock_rock05", r1), P("Rock_rock06", r1), P("Rock_boulder01", b1), P("Rock_boulder03", b3) };
        w.smallRocks = new[] { P("Rock_rock07", r2), P("Rock_rock08", r2), P("Rock_rock09", r2), P("Rock_rock10", r2), P("Rock_rock11", r2), P("Rock_rock12", r2), P("Rock_rock13", r2) };
        w.pineBranches = P("Pine_Branches", branches); w.pineTrunk = P("Pine_Trunk", trunk); w.pineDead = P("Pine_Dead", dead);
        var gl = new System.Collections.Generic.List<Mesh>();
        foreach (var gn in new[] { "Grass_Clump01", "Grass_Ckump02", "Grass_Ckump03", "Grass_Clump01", "Grass_Ckump02", "Blade_low1", "Blade_low2", "Blade_low3" })
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/Art/LookStudy/Models/Real/" + gn + ".fbx")) if (o is Mesh g) gl.Add(g);
        w.grassClumps = gl.ToArray();
        w.grassMat = grass;
        stage.SetActive(true);

        // Test character: the current hunter model, swapped later when the character work lands.
var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/LookStudy/Models/Hunter.fbx") ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/DDConcept/Models/Hunter.fbx");
        var walkerGo = new GameObject("Player");
        walkerGo.transform.position = new Vector3(0, LookStudyValley.Height(0, -10), -10);
        var walker = walkerGo.AddComponent<LookStudyWalker>();
        walker.splashMat = splash; walker.mistMat = mist; walker.dustMat = mist; walker.gritMat = splash;
        if (fbx)
        {
            var body = (GameObject)Object.Instantiate(fbx, walkerGo.transform, false);
            body.name = "Body";
            foreach (var r in body.GetComponentsInChildren<Renderer>())
            {
                var src = r.sharedMaterials; var dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    var name = src[i] ? src[i].name : "None";
                    var path = "Assets/Art/LookStudy/Materials/LS_H_" + name + ".mat";
                    dst[i] = AssetDatabase.LoadAssetAtPath<Material>(path) ?? src[i];
                }
                r.sharedMaterials = dst;
            }
        }
        boot.walker = walker;
        cam.transform.position = walkerGo.transform.position + walker.cameraOffset;
        cam.transform.rotation = Quaternion.LookRotation(-walker.cameraOffset);
        EditorSceneManager.SaveScene(scene, ScenePath);
        return stage;
    }

    // Edit-time debug renders with parts of the stage switched off. Env LS_OUT = folder.
    public static void DebugRender()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var stage = BuildScene();
        var dir = System.Environment.GetEnvironmentVariable("LS_OUT");
        Directory.CreateDirectory(dir);
        var cam = Camera.main;
        LookStudyValley.ApplyMood(RenderSettings.sun);
        Shader.SetGlobalFloat("_LSTime", 12f);
        cam.transform.position = new Vector3(1, 44, -33); cam.transform.LookAt(new Vector3(1, 0, 3)); cam.fieldOfView = 40;
        var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
        void Shot(string name)
        {
            cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt;
            var t = new Texture2D(1600, 900, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); t.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), t.EncodeToJPG(90)); Object.DestroyImmediate(t);
        }
        Shot("all");
        var world = stage.GetComponent<LookStudyValley>();
        long total = 0;
        foreach (var mf in stage.GetComponentsInChildren<MeshFilter>(true)) { var m = mf.sharedMesh; long n = m.GetIndexCount(0) / 3; total += n; Debug.Log($"[LS] {mf.name} tris={n} verts={m.vertexCount} bounds={m.bounds}"); }
        Debug.Log("[LS] total tris " + total);
        var water = stage.transform.Find("ValleyGenerated/Water");
        water.gameObject.SetActive(false); Shot("nowater"); water.gameObject.SetActive(true);
        var terr = stage.transform.Find("ValleyGenerated/Terrain");
        terr.gameObject.SetActive(false); Shot("noterrain"); terr.gameObject.SetActive(true);
        EditorApplication.Exit(0);
    }

    public static void BuildTestPlayer()
    {
        ShaderUtil.allowAsyncCompilation = false;
        BuildScene();
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, System.Environment.GetEnvironmentVariable("LS_PLAYER"), BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[LS] BUILD " + report.summary.result + " size " + report.summary.totalSize + " errors " + report.summary.totalErrors);
        foreach (var n in new[] { "LookStudy/StageRock", "LookStudy/StageTerrain", "LookStudy/StageWater", "LookStudy/StageFall" })
        {
            var sh = Shader.Find(n);
            foreach (var m in ShaderUtil.GetShaderMessages(sh)) if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Debug.LogError($"[LS] {n} line {m.line}: {m.message}");
        }
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }
}
