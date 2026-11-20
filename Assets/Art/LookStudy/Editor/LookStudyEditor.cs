// Builds materials and the study scene, then renders the preview shots.
// Batch: Unity.exe -batchmode -projectPath ... -executeMethod LookStudyEditor.BatchRender -quit
// env LS_OUT = output folder, LS_SHOTS = comma list (empty = all), LS_VIDEO = shot name for a frame sequence.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LookStudyModelImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Replace('\\', '/').Contains("/Models/Real/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.isReadable = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importNormals = ModelImporterNormals.Import;
        mi.meshCompression = ModelImporterMeshCompression.Off;
    }
}

public static class LookStudyEditor
{
    const string Root = "Assets/Art/LookStudy";
    static string ScenePath => ForceReal ? "Assets/Scenes/LookStudy.unity" : Root + "/LookStudy.unity";
    public static bool ForceReal;

    // Fixed olive palette for the grass.
    static readonly Color Root1F = Hex("#1f3b2c"), Mid = Hex("#3f6a3c"), Tip = Hex("#7d9a4c"), Tip2 = Hex("#a8a65a"), Dry = Hex("#b9a45f");

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    static Material Mat(string name, string shader)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var s = Shader.Find(shader);
        if (!s) { Debug.LogError("[LS] missing shader " + shader); return null; }
        if (!m) { m = new Material(s); AssetDatabase.CreateAsset(m, path); }
        m.shader = s;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void FieldColours(Material m)
    {
        m.SetColor("_ColA", Mid); m.SetColor("_ColB", Tip); m.SetColor("_ColC", Tip2);
        m.SetFloat("_RootDark", 0.62f); m.SetFloat("_TipLift", 1.2f);
    }

    static LookStudyWorld BuildScene()
    {
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();

        var grass = Mat("LS_Grass", "LookStudy/Grass");
        FieldColours(grass);
        grass.SetColor("_LaneCol", Dry); grass.SetFloat("_LaneAmt", 0.55f); grass.SetVector("_WindDir", new Vector4(0.8f, 0.6f)); grass.SetFloat("_Bend", 0.55f);

        var salt = Mat("LS_SaltGrass", "LookStudy/Grass");
        salt.SetColor("_ColA", Tip); salt.SetColor("_ColB", Tip2); salt.SetColor("_ColC", Dry);
        salt.SetFloat("_RootDark", 0.4f); salt.SetFloat("_TipLift", 1.1f);
        salt.SetColor("_LaneCol", Dry); salt.SetFloat("_LaneAmt", 0.3f); salt.SetVector("_WindDir", new Vector4(0.8f, 0.6f)); salt.SetFloat("_Bend", 0.45f);

        var ground = Mat("LS_Ground", "LookStudy/Ground");
        FieldColours(ground);
        ground.SetColor("_DirtA", Hex("#c9ab7c")); ground.SetColor("_DirtB", Hex("#94775a")); ground.SetColor("_SandCol", Hex("#d9c79a"));

        var water = Mat("LS_Water", "LookStudy/Water");
        water.SetColor("_Shallow", Hex("#8fd6c4")); water.SetColor("_Mid", Hex("#3f9fa8")); water.SetColor("_Deep", Hex("#1e5f86"));
        water.SetColor("_Foam", Hex("#f2f4e8")); water.SetFloat("_Caustic", 0.7f);

        var props = Mat("LS_Props", "LookStudy/Toon");
        props.SetColor("_BaseColor", Color.white); props.SetFloat("_Rim", 0.1f); props.SetFloat("_Sway", 0.04f); props.SetFloat("_Cull", 0f);

        var mirror = Mat("LS_Mirror", "LookStudy/MirrorFlat");
        mirror.SetColor("_Salt", Hex("#f4ebdc")); mirror.SetColor("_Crack", Hex("#c0b3ad")); mirror.SetFloat("_WetAmount", 0.5f);

        var canyon = Mat("LS_Canyon", "LookStudy/Canyon");
        canyon.SetColor("_B1", Hex("#a8644c")); canyon.SetColor("_B2", Hex("#c28b68")); canyon.SetColor("_B3", Hex("#8a5a5e"));
        canyon.SetColor("_B4", Hex("#c9b08c")); canyon.SetColor("_Top", Hex("#6f8646")); canyon.SetFloat("_BandScale", 0.42f);

        var sky = Mat("LS_Sky", "LookStudy/Sky");
        AssetDatabase.SaveAssets();

        var profilePath = Root + "/LS_Volume.asset";
        AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.05f); bloom.intensity.Override(0.55f); bloom.scatter.Override(0.72f);
        var color = profile.Add<ColorAdjustments>(true);
        color.saturation.Override(10f); color.contrast.Override(10f);
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.16f); vig.smoothness.Override(0.5f);
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.None);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; cam.farClipPlane = 1500f; cam.nearClipPlane = 0.2f; cam.allowHDR = true;
        var extra = camGo.AddComponent<UniversalAdditionalCameraData>();
        extra.renderPostProcessing = true; extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional; sun.color = Color.white; sun.intensity = 1f;
        sun.shadows = System.Environment.GetEnvironmentVariable("LS_ONLY") == "vivid" ? LightShadows.Hard : LightShadows.Soft; sun.shadowStrength = 1f; sun.shadowNormalBias = 0.4f; sun.shadowBias = 0.3f;
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;

        var volGo = new GameObject("Global Volume");
        var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = profile;

        bool real = ForceReal || System.Environment.GetEnvironmentVariable("LS_ONLY") == "real";
        bool vivid = real || System.Environment.GetEnvironmentVariable("LS_ONLY") == "vivid";
        if (vivid)
        {
            color.saturation.Override(4f); color.contrast.Override(3f);
            bloom.threshold.Override(1.1f); bloom.intensity.Override(0.35f); vig.intensity.Override(0.1f);
            // Colours sampled from the reference frame.
            foreach (var m in new[] { grass, ground })
            {
                m.SetColor("_ColA", Hex("#126b36")); m.SetColor("_ColB", Hex("#208c3c")); m.SetColor("_ColC", Hex("#4cb438"));
                m.SetFloat("_RootDark", 0.72f); m.SetFloat("_TipLift", 1.42f); m.SetFloat("_BladeVar", 0.34f); m.SetFloat("_PatchContrast", 1.5f); m.SetFloat("_ClumpAmt", 0.5f);
            }
            ground.SetColor("_ClearCol", Hex("#7fd844")); ground.SetFloat("_ClumpAmt", 0.2f);
            grass.SetColor("_LaneCol", Hex("#8bd950")); grass.SetFloat("_LaneAmt", 0.22f); grass.SetFloat("_Bend", 0.3f); grass.SetVector("_WindDir", new Vector4(-0.77f, -0.64f));
            AssetDatabase.SaveAssets();
        }
        var worldGo = new GameObject("LookStudyWorld");
        worldGo.SetActive(false);
        var w = worldGo.AddComponent<LookStudyWorld>();
        w.grass = grass; w.saltGrass = salt; w.ground = ground; w.water = water; w.props = props; w.mirror = mirror; w.canyon = canyon; w.onlyVivid = vivid; w.onlyReal = real;
        if (real)
        {
            Mesh M(string n)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Root + "/Models/Real/" + n + ".fbx")) if (o is Mesh me) { Debug.Log($"[LS] {n} bounds {me.bounds.size} verts {me.vertexCount}"); return me; }
                Debug.LogError("[LS] no mesh " + n); return null;
            }
            Material F(string n, string tex, float remap)
            {
                var m = Mat("LS_F_" + n, "LookStudy/Foliage");
                m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Models/Real/" + tex + "_baseColor.png"));
                m.SetFloat("_Remap", remap); return m;
            }
            w.realClumps = new[] { M("Grass_Clump01"), M("Grass_Ckump02"), M("Grass_Ckump03") };
            w.realBlades = new[] { M("Blade_Master"), M("Blade_low1"), M("Blade_low2"), M("Blade_low3") };
            w.mYellow = M("Flower_Yellow"); w.mWhite = M("Flower_White"); w.mRose = M("Flower_Rose"); w.mIvy = M("Plant_Ivy"); w.mLeaves = M("Plant_Leaves"); w.mVines = M("Plant_Vines"); w.mBush = M("Bush_Leafy");
            w.fGrass = F("Grass", "GrassGrass_MAT", 1f);
            w.fGrass.SetColor("_Dark", Hex("#0c5a30")); w.fGrass.SetColor("_Light", Hex("#58d04c")); w.fGrass.SetFloat("_LumScale", 2.3f); w.fGrass.SetFloat("_Bend", 0.22f);
            w.fYellow = F("Yellow", "Yellow_Flower", 0f); w.fWhite = F("White", "White_Flower", 0f); w.fRose = F("Rose", "Rose", 0f);
            w.fIvy = F("Ivy", "Ivy_Plant", 0f); w.fLeaves = F("Leaves", "Material.001", 0f); w.fVines = F("Vines", "Vines_Plant", 0f); w.fBush = F("Bush", "stylized_bush", 0f);
            foreach (var m in new[] { w.fYellow, w.fWhite, w.fRose, w.fIvy, w.fLeaves, w.fVines, w.fBush }) m.SetFloat("_Bend", 0.1f);
            // Keep the painted petals but pull them from amber to the reference's clean yellow.
            w.fYellow.SetFloat("_Remap", 0.82f); w.fYellow.SetColor("_Dark", Hex("#e8a012")); w.fYellow.SetColor("_Light", Hex("#ffe640")); w.fYellow.SetFloat("_LumScale", 1.25f); w.fYellow.SetFloat("_RemapDark", 0f);
            ground.SetColor("_ColA", Hex("#0e5530")); ground.SetColor("_ColB", Hex("#146734")); ground.SetColor("_ColC", Hex("#1c7a38"));
            ground.SetFloat("_RootDark", 0f); ground.SetFloat("_TipLift", 1f); ground.SetFloat("_ClumpAmt", 0.4f);
            AssetDatabase.SaveAssets();
        }
        worldGo.SetActive(true);

        if (!ForceReal) Hunter(new Vector3(-3f, LookStudyWorld.MeadowH(-3f, -1f), -1f), 200f);
        if (!ForceReal) Hunter(new Vector3(LookStudyWorld.SaltX, 0f, 0f), 200f);
        if (!ForceReal) Hunter(new Vector3(LookStudyWorld.VividX, LookStudyWorld.VividH(LookStudyWorld.VividX, 0f), 0f), 200f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        return w;
    }

    static readonly Dictionary<string, string> HunterCols = new Dictionary<string, string>
    {
        {"DD_Skin","#d9a982"},{"DD_Indigo","#34425f"},{"DD_Navy","#1b2131"},{"DD_White","#d6d2c4"},{"DD_Black","#17171b"},
        {"DD_Red","#a3352a"},{"DD_Gold","#c09a45"},{"DD_Wood","#6e4a2c"},{"DD_Metal","#3a3c42"},{"DD_Tiger","#c27a2c"},
        {"DD_Paper","#dcc070"},{"DD_Amber","#b5612a"},{"DD_Gourd","#a88a4e"},{"DD_EyeDark","#0b0b0e"},{"DD_Straw","#8f7a45"}
    };

    static void Hunter(Vector3 pos, float yaw)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Hunter.fbx");
        if (!fbx) { Debug.LogWarning("[LS] Hunter.fbx not found"); return; }
        var go = (GameObject)Object.Instantiate(fbx, pos, Quaternion.Euler(0, yaw, 0));
        go.name = "Hunter";
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var src = r.sharedMaterials;
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var name = src[i] ? src[i].name : "None";
                var m = Mat("LS_H_" + name, "LookStudy/Toon");
                m.SetColor("_BaseColor", HunterCols.TryGetValue(name, out var hex) ? Hex(hex) : new Color(0.6f, 0.6f, 0.6f));
                m.SetFloat("_Rim", 0.18f); m.SetFloat("_Sway", 0f); m.SetFloat("_Cull", 2f);
                dst[i] = m;
            }
            r.sharedMaterials = dst;
        }
    }

    struct Shot
    {
        public string name; public Vector3 pos, look; public float fov, fog, cloud, pushR; public LookStudyWorld.Sky sky; public Vector3 push;
    }

    static List<Shot> Shots()
    {
        var hm = new Vector3(-3f, LookStudyWorld.MeadowH(-3f, -1f), -1f);
        var hs = new Vector3(LookStudyWorld.SaltX, 0f, 0f);
        var lake = new Vector3(LookStudyWorld.Lake.x, 0, LookStudyWorld.Lake.y);
        var cx = LookStudyWorld.CanyonX;
        var hv = new Vector3(LookStudyWorld.VividX, LookStudyWorld.VividH(LookStudyWorld.VividX, 0f), 0f);
        return new List<Shot>
        {
            new Shot{name="30_real_top", pos=hv+new Vector3(-1.5f,19.5f,-10.5f), look=hv+new Vector3(-1.5f,0,1.2f), fov=38, fog=0.0006f, cloud=0.12f, sky=LookStudyWorld.Sky.Vivid, push=hv, pushR=1.4f},
            new Shot{name="31_real_low", pos=hv+new Vector3(-3f,3.2f,-8f), look=hv+new Vector3(1,0.8f,6), fov=45, fog=0.002f, cloud=0.12f, sky=LookStudyWorld.Sky.Vivid, push=hv, pushR=1.4f},
            new Shot{name="32_real_close", pos=hv+new Vector3(2f,6.5f,-5f), look=hv+new Vector3(3f,0.3f,-0.6f), fov=38, fog=0.0006f, cloud=0.12f, sky=LookStudyWorld.Sky.Vivid, push=hv, pushR=1.4f},
            new Shot{name="20_vivid_top", pos=hv+new Vector3(-1.5f,19.5f,-10.5f), look=hv+new Vector3(-1.5f,0,1.2f), fov=38, fog=0.0006f, cloud=0.12f, sky=LookStudyWorld.Sky.Vivid, push=hv, pushR=1.4f},
            new Shot{name="21_vivid_low", pos=hv+new Vector3(-3f,3.2f,-8f), look=hv+new Vector3(1,0.8f,6), fov=45, fog=0.002f, cloud=0.12f, sky=LookStudyWorld.Sky.Vivid, push=hv, pushR=1.4f},
            new Shot{name="01_meadow_top", pos=hm+new Vector3(0,15.5f,-12f), look=hm, fov=35, fog=0.0015f, cloud=0.55f, sky=LookStudyWorld.Sky.Day, push=hm, pushR=1.5f},
            new Shot{name="02_meadow_close", pos=hm+new Vector3(-1.5f,5.5f,-6.5f), look=hm+new Vector3(0,0.4f,0), fov=35, fog=0.0015f, cloud=0.55f, sky=LookStudyWorld.Sky.Day, push=hm, pushR=1.5f},
            new Shot{name="03_meadow_low", pos=hm+new Vector3(-2f,2f,-8f), look=hm+new Vector3(3,1.4f,10), fov=45, fog=0.0035f, cloud=0.5f, sky=LookStudyWorld.Sky.Day, push=hm, pushR=1.5f},
            new Shot{name="04_lake_top", pos=lake+new Vector3(-7,21,-22), look=lake+new Vector3(-7,0,-6), fov=38, fog=0.0015f, cloud=0.4f, sky=LookStudyWorld.Sky.Day, push=hm, pushR=1.5f},
            new Shot{name="05_salt_day", pos=hs+new Vector3(0,15f,-12f), look=hs, fov=35, fog=0.0008f, cloud=0, sky=LookStudyWorld.Sky.Day, push=hs, pushR=1.3f},
            new Shot{name="06_salt_dusk", pos=hs+new Vector3(0,15f,-12f), look=hs, fov=35, fog=0.0008f, cloud=0, sky=LookStudyWorld.Sky.Dusk, push=hs, pushR=1.3f},
            new Shot{name="07_salt_night", pos=hs+new Vector3(0,15f,-12f), look=hs, fov=35, fog=0.0006f, cloud=0, sky=LookStudyWorld.Sky.Night, push=hs, pushR=1.3f},
            new Shot{name="08_salt_dusk_low", pos=hs+new Vector3(-3f,1.6f,-9f), look=hs+new Vector3(4f,1.4f,12f), fov=48, fog=0.0012f, cloud=0, sky=LookStudyWorld.Sky.Dusk, push=hs, pushR=1.3f},
            new Shot{name="09_canyon", pos=new Vector3(cx+62,70,-128), look=new Vector3(cx-10,26,30), fov=46, fog=0.0017f, cloud=0.3f, sky=LookStudyWorld.Sky.Day, push=hs, pushR=0.1f},
        };
    }

    static void Render(Shot s, string file, float time, int w = 3840, int h = 2160)
    {
        var cam = Camera.main;
        var sun = Object.FindAnyObjectByType<Light>();
        LookStudyWorld.ApplySky(s.sky, sun, s.fog, s.push, s.pushR, s.cloud);
        Shader.SetGlobalFloat("_LSTime", time);
        cam.transform.position = s.pos;
        cam.transform.LookAt(s.look);
        cam.fieldOfView = s.fov;

        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
        var small = RenderTexture.GetTemporary(w / 2, h / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = null;
        Graphics.Blit(rt, small);
        RenderTexture.active = small;
        var tex = new Texture2D(w / 2, h / 2, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w / 2, h / 2), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToJPG(92));
        Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        RenderTexture.ReleaseTemporary(small);
    }

    // Called by the project's file watcher: builds and saves the scene, renders checks, then
    // reopens whatever scene was open. Refuses to run over unsaved changes.
    public static string BuildInOpenEditor(string outDir)
    {
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty) return "ABORT: active scene has unsaved changes: " + active.path;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "ABORT: play mode";
        var prev = active.path;
        ForceReal = true;
        bool async = EditorSettings.asyncShaderCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try
        {
            BuildScene();
            Directory.CreateDirectory(outDir);
            foreach (var s in Shots())
                if (s.name.StartsWith("3")) { var q = s; q.pushR = 0f; Render(q, Path.Combine(outDir, q.name + ".jpg"), 12f); }
            // Leave the saved scene framed on the hero view.
            var first = Shots().Find(x => x.name.StartsWith("30"));
            Camera.main.transform.position = first.pos; Camera.main.transform.LookAt(first.look); Camera.main.fieldOfView = first.fov;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = true;
            ForceReal = false;
            if (!string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
        return "OK restored " + prev;
    }

    // Totals plus a rough frame cost at 1080p from the hero camera (forced GPU sync through a 1px read).
    static void Measure()
    {
        long tris = 0; int rs = 0;
        foreach (var mf in Object.FindAnyObjectByType<LookStudyWorld>().GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh) { tris += mf.sharedMesh.GetIndexCount(0) / 3; rs++; }
        var s = Shots().Find(x => x.name.StartsWith("30"));
        var cam = Camera.main;
        LookStudyWorld.ApplySky(s.sky, Object.FindAnyObjectByType<Light>(), s.fog, s.push, s.pushR, s.cloud);
        cam.transform.position = s.pos; cam.transform.LookAt(s.look); cam.fieldOfView = s.fov;
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        long vis = 0;
        foreach (var mf in Object.FindAnyObjectByType<LookStudyWorld>().GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh && GeometryUtility.TestPlanesAABB(planes, mf.GetComponent<Renderer>().bounds)) vis += mf.sharedMesh.GetIndexCount(0) / 3;
        var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32);
        var px = new Texture2D(1, 1, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
        for (int i = 0; i < 5; i++) cam.Render();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int N = 30;
        for (int i = 0; i < N; i++)
        {
            cam.Render();
            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply(); RenderTexture.active = null;
        }
        sw.Stop();
        cam.targetTexture = null;
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log($"[LS] MEASURE renderers={rs} totalTris={tris} visibleTris={vis} frameMs={sw.ElapsedMilliseconds / (float)N:0.0}");
    }

    public static void BuildTestPlayer()
    {
        System.Environment.SetEnvironmentVariable("LS_ONLY", "real");
        ShaderUtil.allowAsyncCompilation = false;
        BuildScene();
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        var first = Shots().Find(x => x.name.StartsWith("30"));
        Camera.main.transform.position = first.pos; Camera.main.transform.LookAt(first.look); Camera.main.fieldOfView = first.fov;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, System.Environment.GetEnvironmentVariable("LS_PLAYER"), BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[LS] BUILD " + report.summary.result + " size " + report.summary.totalSize);
        EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    public static void BatchRender()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var outDir = System.Environment.GetEnvironmentVariable("LS_OUT");
        Directory.CreateDirectory(outDir);
        var filter = System.Environment.GetEnvironmentVariable("LS_SHOTS");
        var video = System.Environment.GetEnvironmentVariable("LS_VIDEO");

        BuildScene();
        foreach (var n in new[] { "LookStudy/Grass", "LookStudy/Ground", "LookStudy/Toon", "LookStudy/Water", "LookStudy/MirrorFlat", "LookStudy/Canyon", "LookStudy/Sky" })
        {
            var sh = Shader.Find(n);
            var msgs = sh ? ShaderUtil.GetShaderMessages(sh) : null;
            Debug.Log($"[LS] {n}: found={(sh != null)} supported={(sh ? sh.isSupported : false)} messages={(msgs != null ? msgs.Length : -1)}");
            if (msgs != null) foreach (var m in msgs) if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) Debug.LogError($"[LS] {n} line {m.line}: {m.message}");
        }

        Measure();
        var shots = Shots();
        if (!string.IsNullOrEmpty(video))
        {
            var dir = Path.Combine(outDir, "seq_" + video);
            Directory.CreateDirectory(dir);
            var s = shots.Find(x => x.name.Contains(video));
            for (int f = 0; f < 150; f++)
                Render(s, Path.Combine(dir, $"f{f:0000}.jpg"), 3f + f / 30f, 2560, 1440);
            Debug.Log("[LS] sequence done " + dir);
        }
        else
        {
            foreach (var s in shots)
            {
                if (!string.IsNullOrEmpty(filter) && !filter.Contains(s.name.Substring(0, 2))) continue;
                Render(s, Path.Combine(outDir, s.name + ".jpg"), 12f);
                Debug.Log("[LS] rendered " + s.name);
            }
        }
        EditorApplication.Exit(0);
    }
}
