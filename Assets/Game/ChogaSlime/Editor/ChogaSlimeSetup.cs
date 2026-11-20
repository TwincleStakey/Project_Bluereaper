// 초가집 슬라임 test scene builder + edit-mode capture (no Play, so it never fights other threads over Play/rr_cmd).
// Temp/choga_slime.trigger:  "build"                 -> import settings, materials, ChogaSlime_Test.unity
//                            "capture <moves|all> [views]" -> stills + video frames to ArtSource/ChogaSlime/renders
// Done stamp Temp/choga_slime.done, log Temp/choga_slime.log, error Temp/choga_slime.err.
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ChogaSlimeSetup
{
    const string Root = "Assets/Game/ChogaSlime";
    const string Fbx = Root + "/Models/ChogaSlime.fbx";
    const string HouseFbx = "Assets/Game/AbandonedVillage/Models/AV_Choga_A.fbx";
    const string HousePrefab = "Assets/Game/RoomRun/Resources/EdgeFeatures/VH_AV_ChogaA.prefab";
    const string ScenePath = Root + "/Scenes/ChogaSlime_Test.unity";
    static readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
    static void L(string s) { log.AppendLine(s); Debug.Log("[ChogaSlime] " + s); }

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/choga_slime.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var args = File.ReadAllText(trig).Trim().Split(' '); File.Delete(trig);
        log.Clear();
        try
        {
            if (args[0] == "build") Build();
            else if (args[0] == "capture") Capture(args.Length > 1 ? args[1] : "all", args.Length > 2 ? args[2] : "hero,game");
            else if (args[0] == "patterns") Capture("P:" + (args.Length > 1 ? args[1] : "all"), args.Length > 2 ? args[2] : "doc");
        }
        catch (System.Exception e) { Debug.LogError("[ChogaSlime] " + e); File.WriteAllText("Temp/choga_slime.err", e.ToString()); }
        File.WriteAllText("Temp/choga_slime.log", log.ToString());
        File.WriteAllText("Temp/choga_slime.done", System.DateTime.Now.ToString("o"));
    }

    static Material Mat(string name, string shader)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader.name != shader) m.shader = Shader.Find(shader);
        return m;
    }

    [MenuItem("Tools/ChogaSlime/Build Test Scene")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        foreach (var d in new[] { "Materials", "Scenes" })
            if (!AssetDatabase.IsValidFolder($"{Root}/{d}")) AssetDatabase.CreateFolder(Root, d);
        var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        mi.animationType = ModelImporterAnimationType.Generic; mi.importAnimation = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false;
        mi.SaveAndReimport();

        var slimeMat = Mat("M_ChogaSlime", "BulletHeaven/ChogaSlime");
        var eyeMat = Mat("M_ChogaSlimeEye", "BulletHeaven/ChogaSlimeEye");
        eyeMat.SetColor("_CoreColor", new Color(4.5f, 0.32f, 0.1f)); eyeMat.SetColor("_EdgeColor", new Color(0.9f, 0.015f, 0.01f));
        EditorUtility.SetDirty(eyeMat);
        slimeMat.SetFloat("_Absorb", 0.95f); slimeMat.SetFloat("_MurkIntensity", 0.22f); slimeMat.SetFloat("_ReflIntensity", 1.0f);
        slimeMat.SetColor("_EnvTop", new Color(0.22f, 0.25f, 0.34f)); slimeMat.SetColor("_EnvBottom", new Color(0.03f, 0.025f, 0.02f));
        slimeMat.SetFloat("_RimIntensity", 0.45f);
        // tuning without recompiling: Temp/choga_slime_mat.txt lines "name value"
        if (File.Exists("Temp/choga_slime_mat.txt"))
            foreach (var line in File.ReadAllLines("Temp/choga_slime_mat.txt"))
            { var p = line.Split(' '); if (p.Length == 2 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) slimeMat.SetFloat(p[0], f); }
        EditorUtility.SetDirty(slimeMat);
        foreach (var m in ShaderUtil.GetShaderMessages(slimeMat.shader)) L($"slime shader msg: {m.severity} {m.message} line {m.line}");
        foreach (var m in ShaderUtil.GetShaderMessages(eyeMat.shader)) L($"eye shader msg: {m.severity} {m.message} line {m.line}");
        L($"slime shader supported={slimeMat.shader.isSupported}");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        try
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.07f, 0.08f, 0.12f); RenderSettings.ambientEquatorColor = new Color(0.04f, 0.04f, 0.05f); RenderSettings.ambientGroundColor = new Color(0.02f, 0.018f, 0.016f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogColor = new Color(0.03f, 0.035f, 0.05f); RenderSettings.fogDensity = 0.008f;

            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional; moon.intensity = 1.5f; moon.color = new Color(0.62f, 0.7f, 1f); moon.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(42, 35, 0);
            // a low warm fire on the front left: gives the wet jelly its hot glints and a rim to read the silhouette
            var fire = new GameObject("FireLight").AddComponent<Light>();
            fire.type = LightType.Point; fire.range = 22; fire.intensity = 14; fire.color = new Color(1f, 0.55f, 0.25f); fire.shadows = LightShadows.None;
            fire.transform.position = new Vector3(-9, 2.2f, -7);
            var back = new GameObject("BackRim").AddComponent<Light>();
            back.type = LightType.Point; back.range = 26; back.intensity = 10; back.color = new Color(0.55f, 0.6f, 1f); back.shadows = LightShadows.None;
            back.transform.position = new Vector3(6, 12, 12);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.localScale = Vector3.one * 12;
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_MS_Ground.mat");
            Texture tex = null, nrm = null;
            if (src != null) { if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap"); if (tex == null) tex = src.mainTexture; if (src.HasProperty("_BumpMap")) nrm = src.GetTexture("_BumpMap"); }
            if (tex == null) tex = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Game/AbandonedVillage/ThirdParty/Megascans/Ground/AV_FG_DrySoil_A.jpg");
            if (nrm == null) nrm = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Game/AbandonedVillage/ThirdParty/Megascans/Ground/AV_FG_DrySoil_N.jpg");
            if (tex != null) { gm.SetTexture("_BaseMap", tex); gm.SetTextureScale("_BaseMap", new Vector2(30, 30)); }
            if (nrm != null) { gm.SetTexture("_BumpMap", nrm); gm.EnableKeyword("_NORMALMAP"); }
            gm.SetColor("_BaseColor", new Color(0.5f, 0.47f, 0.44f)); gm.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.DeleteAsset($"{Root}/Materials/CS_Ground.mat"); AssetDatabase.CreateAsset(gm, $"{Root}/Materials/CS_Ground.mat");
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            L($"ground tex {(tex ? tex.name : "none")}");


            // boss
            var boss = new GameObject("ChogaSlime");
            var slime = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            PrefabUtility.UnpackPrefabInstance(slime, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            slime.transform.SetParent(boss.transform, false);
            var house = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HouseFbx));
            PrefabUtility.UnpackPrefabInstance(house, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            house.name = "House"; house.transform.SetParent(boss.transform, false);
            var hp = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefab);
            var hmats = hp != null ? hp.GetComponentInChildren<MeshRenderer>().sharedMaterials : null;
            if (hmats != null) foreach (var r in house.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterials = hmats;

            var all = slime.GetComponentsInChildren<Transform>(true);
            Transform F(string n) => all.FirstOrDefault(t => t.name == n);
            var drv = boss.AddComponent<ChogaSlimeBoss>();
            drv.house = house.transform; drv.bodyBone = F("Body");
            drv.armL = Enumerable.Range(0, 7).Select(k => F($"ArmL{k}")).ToArray();
            drv.armR = Enumerable.Range(0, 7).Select(k => F($"ArmR{k}")).ToArray();
            drv.body = slime.GetComponentInChildren<SkinnedMeshRenderer>();
            drv.eyes = new[] { F("EyeL"), F("EyeR") }.Where(t => t != null).Select(t => t.GetComponent<Renderer>()).Where(r => r != null).ToArray();
            L($"bones body={drv.bodyBone != null} armL={drv.armL.Count(t => t)} armR={drv.armR.Count(t => t)} smr={drv.body != null} eyes={drv.eyes.Length}");
            var rootBone = F("Root");
            foreach (var a0 in new[] { drv.armL[0], drv.armR[0] }) if (a0 != null && rootBone != null) a0.SetParent(rootBone, true);   // off the squashing body bone
            drv.body.sharedMaterial = slimeMat; drv.body.updateWhenOffscreen = true;
            drv.body.shadowCastingMode = ShadowCastingMode.On;
            foreach (var e in drv.eyes) { e.sharedMaterial = eyeMat; e.shadowCastingMode = ShadowCastingMode.Off; }
            var hb = new Bounds(house.transform.position, Vector3.zero); foreach (var r in house.GetComponentsInChildren<Renderer>()) hb.Encapsulate(r.bounds);
            var sb = drv.body.bounds;
            L($"house bounds {hb.center} {hb.size}  slime bounds {sb.center} {sb.size}");
            drv.CaptureRest();
            // face the game camera (-Z)
            var fw = drv.ForwardWorld; fw.y = 0;
            boss.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(fw, Vector3.back, Vector3.up), Vector3.up) * boss.transform.rotation;   // yaw only (FromToRotation of opposite vectors can flip it upside down)
            L($"forward was {fw}, boss yaw {boss.transform.eulerAngles.y}");
            drv.Evaluate(0);
            BuildArena(drv);

            var volGo = new GameObject("Post"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Root}/Scenes/ChogaSlime_Post.asset");
            if (prof == null) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, $"{Root}/Scenes/ChogaSlime_Post.asset"); }
            if (!prof.TryGet<Bloom>(out var bloom)) bloom = prof.Add<Bloom>(true);
            bloom.threshold.Override(1.1f); bloom.intensity.Override(0.9f); bloom.scatter.Override(0.65f);
            if (!prof.TryGet<Tonemapping>(out var tm)) tm = prof.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            if (!prof.TryGet<Vignette>(out var vg)) vg = prof.Add<Vignette>(true);
            vg.intensity.Override(0.3f);
            if (!prof.TryGet<ColorAdjustments>(out var ca)) ca = prof.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.9f);   // review captures: readable shape first; the real map grades it down
            EditorUtility.SetDirty(prof);
            vol.sharedProfile = prof;

            var cam = new GameObject("Main Camera").AddComponent<Camera>(); cam.tag = "MainCamera";
            cam.fieldOfView = 30; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.02f, 0.022f, 0.03f);
            var cd = cam.gameObject.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true;
            cam.transform.position = new Vector3(-14, 9, -22); cam.transform.LookAt(new Vector3(0, 5, 0));
            drv.fxCamera = cam;

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            L("saved " + ScenePath);
        }
        finally
        {
            if (prevActive.IsValid() && prevActive.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
            if (UnityEngine.SceneManagement.SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // 그슨대 design doc: alley in front of the house (walls both sides), 3 players, cursed gourds on the roof, wind-up key
    static void BuildArena(ChogaSlimeBoss drv)
    {
        var root = new GameObject("Arena").transform;
        var stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/AbandonedVillage/Materials/AV_StoneWall.mat");
        foreach (var sx in new[] { -1f, 1f })
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube); w.name = "AlleyWall"; Object.DestroyImmediate(w.GetComponent<Collider>());
            w.transform.SetParent(root); w.transform.position = drv.ScreenToWorld(9.0f * sx, 1.3f, 18f);
            w.transform.rotation = Quaternion.LookRotation(drv.ForwardWorld); w.transform.localScale = new Vector3(0.9f, 2.6f, 28f);
            if (stone != null) w.GetComponent<Renderer>().sharedMaterial = stone;
        }
        var cols = new[] { new Color(0.25f, 0.45f, 1f), new Color(0.3f, 0.85f, 0.4f), new Color(1f, 0.78f, 0.25f) };
        var pos = new[] { new Vector2(-4f, 14f), new Vector2(0.5f, 16.5f), new Vector2(4.5f, 13f) };
        var pl = new Transform[3];
        for (int i = 0; i < 3; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Capsule); c.name = $"Player{i + 1}"; Object.DestroyImmediate(c.GetComponent<Collider>());
            c.transform.SetParent(root); c.transform.position = drv.ScreenToWorld(pos[i].x, 0.9f, pos[i].y); c.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", cols[i]); m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            AssetDatabase.DeleteAsset($"{Root}/Materials/CS_Player{i + 1}.mat"); AssetDatabase.CreateAsset(m, $"{Root}/Materials/CS_Player{i + 1}.mat");
            c.GetComponent<Renderer>().sharedMaterial = m; pl[i] = c.transform;
        }
        drv.players = pl;
        // cursed gourds (흉박) on the front roof slope
        var gp = $"{Root}/Models/Hyungbak.fbx";
        var gmi = (ModelImporter)AssetImporter.GetAtPath(gp);
        if (gmi != null && gmi.materialImportMode != ModelImporterMaterialImportMode.None) { gmi.materialImportMode = ModelImporterMaterialImportMode.None; gmi.SaveAndReimport(); }
        var gMat = Mat("M_Hyungbak", "Universal Render Pipeline/Lit"); gMat.SetColor("_BaseColor", new Color(0.2f, 0.035f, 0.03f)); gMat.SetFloat("_Smoothness", 0.4f);
        gMat.EnableKeyword("_EMISSION"); gMat.SetColor("_EmissionColor", Color.black); gMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        var sMat = Mat("M_HyungbakStem", "Universal Render Pipeline/Lit"); sMat.SetColor("_BaseColor", new Color(0.12f, 0.09f, 0.05f)); sMat.SetFloat("_Smoothness", 0.1f);
        var fxm = Mat("M_GeuseundaeFX", "BulletHeaven/GeuseundaeFX");
        foreach (var m in ShaderUtil.GetShaderMessages(fxm.shader)) L($"fx shader msg: {m.severity} {m.message} line {m.line}");
        var fxa = Mat("M_GeuseundaeFX_Alpha", "BulletHeaven/GeuseundaeFX"); fxa.SetFloat("_Alpha", 1); fxa.SetFloat("_SrcB", (float)BlendMode.SrcAlpha); fxa.SetFloat("_DstB", (float)BlendMode.OneMinusSrcAlpha); fxa.renderQueue = 3010;
        fxm.SetFloat("_Alpha", 0); fxm.SetFloat("_SrcB", (float)BlendMode.One); fxm.SetFloat("_DstB", (float)BlendMode.One);
        drv.fxMat = fxm; drv.fxMatAlpha = fxa; drv.gourdMat = gMat; drv.gourdStemMat = sMat;
        drv.gourdMesh = AssetDatabase.LoadAllAssetsAtPath(gp).OfType<Mesh>().FirstOrDefault();
        L($"gourd mesh {(drv.gourdMesh ? drv.gourdMesh.name + " sub " + drv.gourdMesh.subMeshCount : "none")}");
        var gPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(gp);
        var spots = new[] { new Vector3(-1.0f, -2.05f, 4.35f), new Vector3(0.25f, -2.15f, 4.25f), new Vector3(1.35f, -2.0f, 4.35f), new Vector3(-2.4f, -2.15f, 4.1f), new Vector3(2.5f, -2.1f, 4.1f) };
        var gs = new Transform[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(gPrefab); PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            g.name = $"Hyungbak{i}"; g.transform.SetParent(drv.house, true);
            g.transform.rotation = drv.transform.rotation * Quaternion.Euler(0, i * 67, 0) * g.transform.rotation;
            g.transform.localScale = g.transform.localScale * drv.gourdScale;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterials = new[] { gMat, sMat };
            g.transform.position = drv.BlenderToWorld(spots[i]);   // centre first: CapturePatternRest stores where the gourd is
            gs[i] = g.transform;
        }
        drv.roofGourds = gs;
        drv.CapturePatternRest();
        foreach (var g in gs) g.position += Vector3.down * 0.55f * drv.gourdScale;   // then drop the pivot (bottom) under the centre
        // wind-up key (태엽) on top of the head, child of the body bone so it rides the squash
        var key = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/WindKey/Prefabs/WindKey.prefab");
        if (key != null)
        {
            var k = (GameObject)PrefabUtility.InstantiatePrefab(key); PrefabUtility.UnpackPrefabInstance(k, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            k.name = "SoulWindKey";
            k.transform.position = drv.BlenderToWorld(new Vector3(0.1f, 0.1f, 10.25f));
            var fw = drv.ForwardWorld; var right = Vector3.Cross(Vector3.up, fw);
            k.transform.rotation = Quaternion.AngleAxis(-18f, right) * Quaternion.LookRotation(fw);
            var b = new Bounds(k.transform.position, Vector3.zero); foreach (var r in k.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            float wdt = Mathf.Max(b.size.x, b.size.z); if (wdt > 0.01f) k.transform.localScale *= 3.4f / wdt;
            k.transform.SetParent(drv.bodyBone, true);
            L($"wind key size {b.size} -> width 3.4");
        }
        else L("WindKey.prefab missing");
    }

    // ---------------- capture ----------------
    public static void Capture(string moves, string views)
    {
        var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var wasLoaded = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath).isLoaded;
        var scene = wasLoaded ? UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ScenePath) : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var hidden = new System.Collections.Generic.List<GameObject>();
        bool async0 = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        MeshRenderer proxy = null; SkinnedMeshRenderer smr = null;
        try
        {
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (sc == scene || !sc.isLoaded) continue;
                foreach (var g in sc.GetRootGameObjects()) if (g.activeSelf) { g.SetActive(false); hidden.Add(g); }
            }
            var bossGo = scene.GetRootGameObjects().First(g => g.name == "ChogaSlime");
            var drv = bossGo.GetComponent<ChogaSlimeBoss>();
            var cam = scene.GetRootGameObjects().First(g => g.name == "Main Camera").GetComponent<Camera>();
            smr = drv.body;
            // edit-mode renders never re-skin: draw a baked copy of the skinned body each frame
            var pg = new GameObject("SkinProxy") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pg, scene);
            var mesh = new Mesh { name = "slime_baked", hideFlags = HideFlags.HideAndDontSave };
            pg.AddComponent<MeshFilter>().sharedMesh = mesh;
            proxy = pg.AddComponent<MeshRenderer>(); proxy.sharedMaterials = smr.sharedMaterials; proxy.shadowCastingMode = ShadowCastingMode.On;
            smr.forceRenderingOff = true;

            var outRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/ChogaSlime/renders"));
            Directory.CreateDirectory(outRoot);
            int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.DefaultHDR); var ldr = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var bossPos0 = bossGo.transform.position; var bossRot = bossGo.transform.rotation;
            bool pats = moves.StartsWith("P:"); if (pats) moves = moves.Substring(2);
            var mlist = pats ? (moves == "all" ? Enumerable.Range(1, 10).Select(i => i.ToString()).ToArray() : moves.Split(','))
                             : moves == "all" ? new[] { "Idle", "Walk", "Lift", "Slam" } : moves.Split(',');
            drv.fxCamera = cam;
            var vlist = views.Split(',');
            // warm-up render (first request compiles variants)
            Shoot(cam, rt, ldr, tex, null);
            foreach (var mname in mlist)
            {
                float len; Vector3 mid; var fwdW = drv.ForwardWorld;
                if (pats) { drv.pattern = int.Parse(mname); len = ChogaSlimeBoss.PatternLength(drv.pattern); ChogaSlimeBoss.HitStopTime(drv.pattern, 0, out var hsx); len += hsx; mid = bossPos0 + fwdW * 8.5f; }
                else
                {
                    drv.pattern = 0;
                    var mv = (ChogaSlimeBoss.Move)System.Enum.Parse(typeof(ChogaSlimeBoss.Move), mname);
                    drv.move = mv; len = ChogaSlimeBoss.Length(mv);
                    mid = bossPos0 + fwdW * ChogaSlimeBoss.NetForward(mv) * 0.5f;
                }
                foreach (var view in vlist)
                {
                    PlaceCam(cam, view, mid);
                    var dir = Path.Combine(outRoot, pats ? $"pat_{view}_{int.Parse(mname):00}" : $"{view}_{mname}"); Directory.CreateDirectory(dir);
                    foreach (var f0 in Directory.GetFiles(dir, "*.png")) File.Delete(f0);
                    int nf = Mathf.CeilToInt(len * 30);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int f = 0; f <= nf; f++)
                    {
                        float ct = f / 30f, pt = pats ? ChogaSlimeBoss.HitStopTime(drv.pattern, ct, out _) : ct;
                        Shader.SetGlobalFloat("_WK_UseFixedTime", 1f); Shader.SetGlobalFloat("_WK_FixedTime", pt);
                        drv.Evaluate(pt);
                        if (pats)
                        {   // camera shake on impacts (offset only for this frame)
                            PlaceCam(cam, view, mid);
                            float sh = ChogaSlimeBoss.Shake(drv.pattern, pt);
                            cam.transform.position += new Vector3(Mathf.PerlinNoise(ct * 31f, 1.3f) - 0.5f, Mathf.PerlinNoise(ct * 29f, 7.1f) - 0.5f, Mathf.PerlinNoise(ct * 27f, 3.7f) - 0.5f) * 2f * sh;
                        }
                        smr.BakeMesh(mesh, true);
                        if (f == -1) L($"dbg body {drv.bodyBone.position} scl {drv.bodyBone.lossyScale} house {drv.house.position} {drv.house.lossyScale} {drv.house.rotation.eulerAngles} meshB {mesh.bounds} proxy {pg.transform.position} armL4 {drv.armL[4].position}");
                        pg.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation); pg.transform.localScale = Vector3.one;
                        var pb = new MaterialPropertyBlock(); smr.GetPropertyBlock(pb); proxy.SetPropertyBlock(pb);
                        Shoot(cam, rt, ldr, tex, Path.Combine(dir, $"f{f:000}.png"));
                    }
                    L($"{view}_{mname}: {nf + 1} frames in {sw.ElapsedMilliseconds} ms");
                }
            }
            drv.pattern = 0; drv.move = ChogaSlimeBoss.Move.Idle; drv.Evaluate(0);
            Shader.SetGlobalFloat("_WK_UseFixedTime", 0f);
            bossGo.transform.SetPositionAndRotation(bossPos0, bossRot);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(ldr); Object.DestroyImmediate(tex);
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(pg);
        }
        finally
        {
            if (smr != null) smr.forceRenderingOff = false;
            ShaderUtil.allowAsyncCompilation = async0;
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            if (prevActive.IsValid() && prevActive.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
            if (!wasLoaded && UnityEngine.SceneManagement.SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void PlaceCam(Camera cam, string view, Vector3 c)
    {
        if (view == "doc")
        {   // design-doc review: game-camera angle over the house and the whole alley
            cam.fieldOfView = 34;
            var off = new Vector3(0, 17f, -14.5f) * 2.2f;
            var tgt = c + new Vector3(0, 4.5f, 0);
            cam.transform.position = tgt + off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }
        else if (view == "game")
        {   // RoomRun game camera angle, pulled back for a 13 m tall boss holding a house overhead
            cam.fieldOfView = 30;
            var off = new Vector3(0, 17f, -14.5f) * 1.65f;
            var tgt = c + new Vector3(0, 3.5f, 0);
            cam.transform.position = tgt + off; cam.transform.rotation = Quaternion.LookRotation(-off);
        }
        else if (view == "front")
        {
            cam.fieldOfView = 32; cam.transform.position = c + new Vector3(0, 7, -34); cam.transform.LookAt(c + new Vector3(0, 6, 0));
        }
        else
        {   // hero: low three-quarter, to judge the jelly
            cam.fieldOfView = 34; cam.transform.position = c + new Vector3(-17, 8, -26); cam.transform.LookAt(c + new Vector3(0, 6.2f, 0));
        }
    }

    static void Shoot(Camera cam, RenderTexture rt, RenderTexture ldr, Texture2D tex, string file)
    {
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        if (file == null) return;
        Graphics.Blit(rt, ldr);
        var prev = RenderTexture.active; RenderTexture.active = ldr;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
    }
}
#endif
