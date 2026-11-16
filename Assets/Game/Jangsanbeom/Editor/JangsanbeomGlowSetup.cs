// 장산범 glow test: builds a dark test scene with the howl and charge FBX on the glow material,
// and captures stills so the look can be reviewed without entering Play mode.
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class JangsanbeomGlowSetup
{
    const string Root = "Assets/Game/Jangsanbeom";
    const float BossScale = 25f;   // model is 0.43 m tall; x25 = 10.6 m tall, 25 m long (player is 1.8 m): Lost Ark guardian scale
    static readonly string[] Clips = { "p0_telegraph_scrape", "p0_telegraph_scrape_L", "p1_howl_gramophone", "p2_charge_gramophone" };   // _L = mirrored left-paw scrape (11-16)

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (File.Exists("Temp/jsb_debug.trigger") && !EditorApplication.isCompiling && !EditorApplication.isPlaying)
        {
            File.Delete("Temp/jsb_debug.trigger");
            try { File.WriteAllText("Temp/jsb_debug.txt", DebugSkin()); } catch (System.Exception e) { File.WriteAllText("Temp/jsb_debug.txt", e.ToString()); }
        }
        if (File.Exists("Temp/jsb_map.trigger") && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            var args = File.ReadAllText("Temp/jsb_map.trigger").Trim().Split(' '); File.Delete("Temp/jsb_map.trigger"); var clipName = args[0]; var ci = System.Globalization.CultureInfo.InvariantCulture;
            float zoomArg = args.Length > 1 ? float.Parse(args[1], ci) : 2.7f, tzArg = args.Length > 2 ? float.Parse(args[2], ci) : 7f, dimArg = args.Length > 3 ? float.Parse(args[3], ci) : 1f; var clipFbx = args.Length > 4 && args[4] != "-" ? args[4] : null; float lapArg = args.Length > 5 ? float.Parse(args[5], ci) : 0f;
            try { MapCapture(clipName.Length > 0 ? clipName : "p0_telegraph_scrape", zoomArg, tzArg, dimArg, clipFbx, lapArg); } catch (System.Exception e) { Debug.LogError("[JSB] map " + e); File.WriteAllText("Temp/jsb_map.err", e.ToString()); }
            File.WriteAllText("Temp/jsb_map.done", System.DateTime.Now.ToString("o"));
        }
        const string trig = "Temp/jsb_glow.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(trig);
        try { Build(); } catch (System.Exception e) { Debug.LogError("[JSB] " + e); }
        File.WriteAllText("Temp/jsb_glow.done", System.DateTime.Now.ToString("o"));
    }

    // why does the body not follow the bones? sample the charge at two times and compare skinned vertices
    static string DebugSkin()
    {
        var sb = new System.Text.StringBuilder();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/p0_telegraph_scrape.fbx");
        var go = (GameObject)Object.Instantiate(model); go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/p0_telegraph_scrape.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            sb.AppendLine($"clip {clip.name} len {clip.length} curves {AnimationUtility.GetCurveBindings(clip).Length}");
            foreach (var b in AnimationUtility.GetCurveBindings(clip).Take(6)) sb.AppendLine($"  bind {b.path} {b.propertyName}");
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var hips = go.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips");
            foreach (var smr in smrs)
            {
                var m0 = new Mesh(); var m1 = new Mesh();
                clip.SampleAnimation(go, 0.1f); var h0 = hips ? hips.position : Vector3.zero; smr.BakeMesh(m0);
                clip.SampleAnimation(go, 1.5f); var h1 = hips ? hips.position : Vector3.zero; smr.BakeMesh(m1);
                float d = 0; var a = m0.vertices; var b2 = m1.vertices; for (int i = 0; i < a.Length; i += 50) d = Mathf.Max(d, (a[i] - b2[i]).magnitude);
                sb.AppendLine($"smr {smr.name} bones {smr.bones.Length} nullBones {smr.bones.Count(x => x == null)} root {(smr.rootBone ? smr.rootBone.name : "none")} weightsPerVert {smr.sharedMesh.GetBonesPerVertex().Length} bindposes {smr.sharedMesh.bindposes.Length} vtxMove {d:F3} hipsMove {(h1 - h0).magnitude:F3} quality {smr.quality}");
            }
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }

    [MenuItem("Tools/Jangsanbeom/Build Glow Test Scene")]
    public static void Build()
    {
        AssetDatabase.Refresh();
        foreach (var d in new[] { "Materials", "Animation", "Scenes" })
            if (!AssetDatabase.IsValidFolder($"{Root}/{d}")) AssetDatabase.CreateFolder(Root, d);

        // textures
        var eye = $"{Root}/Textures/JSB_EyeMask.png";
        var ti = (TextureImporter)AssetImporter.GetAtPath(eye);
        if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/JSB_Base.png");

        // glow material for the body
        var matPath = $"{Root}/Materials/JSB_BodyGlow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("BulletHeaven/JangsanbeomGlow"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetTexture("_EyeMask", AssetDatabase.LoadAssetAtPath<Texture2D>(eye));
        // tuned for a 25x guardian-size boss seen from the game camera
        mat.SetFloat("_AmbientBoost", 0.5f);   // 11-16 Kido: near-black, only the face shows a little
        mat.SetFloat("_EyeIntensity", 8f); mat.SetColor("_EyeColor", new Color(5f, 0.25f, 0.12f, 1f)); mat.SetFloat("_Warn", 0f); mat.SetFloat("_WarnFurIntensity", 1.2f); mat.SetFloat("_ClawIntensity", 8f); mat.SetFloat("_ClawHeight", 50f); mat.SetFloat("_ClawRedness", 0.05f); mat.SetFloat("_WarnRimIntensity", 3f);
        mat.SetFloat("_RimIntensity", 0.4f); mat.SetFloat("_RimPower", 2.5f); mat.SetFloat("_FurLumMin", 0.35f);
        mat.SetFloat("_SparkleDensity", 7f); mat.SetFloat("_SparkleAmount", 0.22f); mat.SetFloat("_SparkleIntensity", 2f); mat.SetFloat("_SparkleSpeed", 3f);
        // optional overrides for tuning/debugging: Temp/jsb_glow_overrides.txt with lines "name value"
        if (File.Exists("Temp/jsb_glow_overrides.txt"))
            foreach (var line in File.ReadAllLines("Temp/jsb_glow_overrides.txt"))
            { var p = line.Split(' '); if (p.Length == 2 && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) mat.SetFloat(p[0], f); }
        foreach (var m in ShaderUtil.GetShaderMessages(mat.shader)) Debug.Log($"[JSB] shader msg: {m.severity} {m.message} line {m.line}");
        Debug.Log($"[JSB] shader={mat.shader.name} supported={mat.shader.isSupported} rim={mat.GetFloat("_RimIntensity")}");
        EditorUtility.SetDirty(mat);

        // generic rigs, the charge loops
        foreach (var c in Clips)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath($"{Root}/Models/{c}.fbx");
            if (mi == null) { Debug.LogError($"[JSB] missing {c}.fbx"); continue; }
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.globalScale = BossScale; mi.useFileScale = true;
            var clips = mi.defaultClipAnimations;
            foreach (var cl in clips) cl.loopTime = c.Contains("charge");
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        // remove a leftover test scene from an earlier failed run (identified by its own objects only)
        for (int si = UnityEngine.SceneManagement.SceneManager.sceneCount - 1; si >= 0; si--)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            if (sc.isLoaded && sc.GetRootGameObjects().Any(g => g.name == "Glow Volume" || g.name == "Moonlight") && UnityEngine.SceneManagement.SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(sc, true);
        }
        var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        try {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.015f, 0.018f, 0.03f);
        RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.01f, 0.012f, 0.02f); RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.012f;

        var sun = new GameObject("Moonlight").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 0.35f; sun.color = new Color(0.55f, 0.65f, 1f);
        sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(40, -30, 0);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.localScale = Vector3.one * 12;
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule); player.name = "PlayerSize_1.8m"; player.transform.position = new Vector3(5f, 0.9f, -12f); player.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
        AssetDatabase.DeleteAsset($"{Root}/Materials/JSB_TestGround.mat"); AssetDatabase.CreateAsset(gm, $"{Root}/Materials/JSB_TestGround.mat"); ground.GetComponent<Renderer>().sharedMaterial = gm;

        
        foreach (var c in Clips)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{c}.fbx");
            if (model == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            go.name = c; go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("tripo"))
                {
                    if (File.Exists("Temp/jsb_use_lit")) { var lm = new Material(Shader.Find("Universal Render Pipeline/Lit")); lm.SetTexture("_BaseMap", baseTex); r.sharedMaterial = lm; }
                    else r.sharedMaterial = mat;
                    Debug.Log($"[JSB] body renderer {r.name} ({r.GetType().Name}) -> {r.sharedMaterial.shader.name}, mats={r.sharedMaterials.Length}");
                }
                else if (r.name.StartsWith("SnakeBody") || r.name.StartsWith("SnakeTop"))
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m => ParasiteMat(m != null ? m.name : "")).ToArray();
                    var pb = new MaterialPropertyBlock(); r.GetPropertyBlock(pb); pb.SetFloat("_WaveDir", r.name.StartsWith("SnakeTop") ? 1f : -1f); r.SetPropertyBlock(pb);   // blood flows toward the gramophone
                }
                else r.sharedMaterials = r.sharedMaterials.Select(GramoMat).ToArray();   // gramophone parts: the Blender node materials don't survive FBX
            WindKeySetup.AddToJangsanbeom(go);   // 11-16: 태엽열쇠 on the lower back
            BossKeyMat(go);
            FaceLight(go);
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/{c}.fbx");
            AnimationClip ac = null; foreach (var o in clip) if (o is AnimationClip a && !a.name.StartsWith("__preview")) ac = a;
            if (ac != null)
            {
                AssetDatabase.DeleteAsset($"{Root}/Animation/{c}.controller"); var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip($"{Root}/Animation/{c}.controller", ac);
                var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>(); an.runtimeAnimatorController = ctrl;
                // red claw afterimages + attack telegraph driver
                var trailMatPath = $"{Root}/Materials/JSB_ClawTrail.mat";
                var trailMat = AssetDatabase.LoadAssetAtPath<Material>(trailMatPath);
                if (trailMat == null) { trailMat = new Material(Shader.Find("BulletHeaven/JangsanbeomTrail")); AssetDatabase.CreateAsset(trailMat, trailMatPath); }
                var ct = go.GetComponent<JangsanbeomClawTrails>(); if (ct == null) ct = go.AddComponent<JangsanbeomClawTrails>();
                ct.trailMaterial = trailMat; ct.minSpeed = 2.5f; ct.fullSpeed = 10f; ct.Build(); Debug.Log($"[JSB] claw trails on {go.name}: {go.GetComponentsInChildren<LineRenderer>(true).Length}");
                if (go.GetComponent<JangsanbeomTelegraph>() == null) go.AddComponent<JangsanbeomTelegraph>();
                if (c.Contains("scrape"))
                {   // 11-16 Kido: ground FX under the scraping paw
                    var sf = go.GetComponent<JangsanbeomScrapeFX>(); if (sf == null) sf = go.AddComponent<JangsanbeomScrapeFX>();
                    sf.dustMaterial = FxMat("JSB_ScrapeDust", false); sf.sparkMaterial = GlowLineMat("JSB_ScrapeSpark", new Color(6f, 0.7f, 0.18f, 1f)); sf.gougeMaterial = GlowLineMat("JSB_ScrapeGouge", new Color(3f, 0.2f, 0.06f, 1f));
                    sf.Build();
                }
            }
        }

        var camGo = new GameObject("JSB_Camera");
        var cam = camGo.AddComponent<Camera>(); cam.allowHDR = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.005f, 0.006f, 0.01f);
        cam.transform.position = new Vector3(17f, 15f, -21f); cam.transform.LookAt(new Vector3(0f, 4.5f, 0f)); cam.fieldOfView = 40; cam.farClipPlane = 300;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true;

        var volGo = new GameObject("Glow Volume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = prof.Add<Bloom>(true); bloom.threshold.Override(0.9f); bloom.intensity.Override(1.6f); bloom.scatter.Override(0.75f);
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        AssetDatabase.DeleteAsset($"{Root}/Scenes/JSB_GlowTest_Profile.asset"); AssetDatabase.CreateAsset(prof, $"{Root}/Scenes/JSB_GlowTest_Profile.asset"); vol.sharedProfile = prof;

        EditorSceneManager.SaveScene(scene, $"{Root}/Scenes/JSB_GlowTest.unity");
        AssetDatabase.SaveAssets();
        // hide the other open scenes only while capturing (not saved), then restore them exactly
        var hidden = new System.Collections.Generic.List<GameObject>();
        var cleanBefore = new System.Collections.Generic.List<UnityEngine.SceneManagement.Scene>();
        for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            if (sc == scene || !sc.isLoaded) continue;
            if (!sc.isDirty) cleanBefore.Add(sc);
            foreach (var g in sc.GetRootGameObjects()) if (g.activeSelf) { g.SetActive(false); hidden.Add(g); }
        }
        try { Capture(cam); }
        finally
        {
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            // a scene that had no unsaved changes before is identical again: save it so it is not left marked dirty
            foreach (var sc in cleanBefore) if (sc.isDirty) EditorSceneManager.SaveScene(sc);
        }
        Debug.Log("[JSB] glow test scene built and captured");
        } finally {
            if (prevActive.IsValid() && prevActive.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    // 11-16 (Kido: "어두운 맵에서 발광하는 실제 인게임 모습"): the boss in the BossSeal arena with that map's own light, fog and
    // post, seen by the RoomRun game camera (offset (0,17,-14.5) x zoom). Uses the boss as set up by Build() in JSB_GlowTest.
    // Edit mode only; StageMood/LookFX grades run only in RoomRun Play, so they are not in these frames.
    const string MapScene = "Assets/Game/BossSeal/BossSeal.unity";
    public static void MapCapture(string clipName, float zoom = 2.7f, float targetZ = 7f, float dim = 1f, string clipFbx = null, float lapRadius = 0f)
    {
        var openedMap = !UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MapScene).isLoaded;
        var map = openedMap ? EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive) : UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MapScene);
        var test = EditorSceneManager.OpenScene($"{Root}/Scenes/JSB_GlowTest.unity", OpenSceneMode.Additive);
        var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(map);   // the map's ambient/fog drive the render
        var hidden = new System.Collections.Generic.List<GameObject>();
        var cleanBefore = new System.Collections.Generic.List<UnityEngine.SceneManagement.Scene>();
        bool async0 = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
                if (sc == map || !sc.isLoaded) continue;
                if (sc != test && !sc.isDirty) cleanBefore.Add(sc);
                foreach (var g in sc.GetRootGameObjects()) if (g.activeSelf && !(sc == test && g.name == clipName)) { g.SetActive(false); hidden.Add(g); }
            }
            var boss = test.GetRootGameObjects().First(g => g.name == clipName);
            var an = boss.GetComponent<Animator>(); var clip = an.runtimeAnimatorController.animationClips[0];
            // optional: another clip on the same AutoRig (e.g. the retargeted QuadAnim gallop), looped for 4 s
            bool loopClip = false;
            if (!string.IsNullOrEmpty(clipFbx))
            {
                AssetDatabase.ImportAsset(clipFbx);
                if (AssetImporter.GetAtPath(clipFbx) is ModelImporter cmi && (cmi.animationType != ModelImporterAnimationType.Generic || Mathf.Abs(cmi.globalScale - BossScale) > 0.01f))
                { cmi.animationType = ModelImporterAnimationType.Generic; cmi.globalScale = BossScale; cmi.useFileScale = true; cmi.SaveAndReimport(); }
                var alt = AssetDatabase.LoadAllAssetsAtPath(clipFbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
                if (alt != null) { clip = alt; loopClip = true; }
            }
            string outName = string.IsNullOrEmpty(clipFbx) ? clipName : Path.GetFileNameWithoutExtension(clipFbx);
            var trails = boss.GetComponent<JangsanbeomClawTrails>();
            var scrape = boss.GetComponent<JangsanbeomScrapeFX>(); if (scrape != null) scrape.Build(); if (trails != null) trails.Build();   // DontSave children were not in the saved scene
            // face the camera (-Z): measure hips->head at t=0 and turn the root about Y
            clip.SampleAnimation(boss, 0f);
            var head = boss.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            var hips = boss.GetComponentsInChildren<Transform>().First(t => t.name == "Hips");
            var fwd = head.position - hips.position; fwd.y = 0;
            boss.transform.rotation = Quaternion.FromToRotation(fwd.normalized, Vector3.back) * boss.transform.rotation;
            boss.transform.position = new Vector3(10f, 0f, 9f);   // off the 0.8 m dais (paws sank into it and hid the scrape marks)   // on the dirt north of the ~10 m dais, paws toward the player side

            var cam = map.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First(c => c.CompareTag("MainCamera"));
            AssetDatabase.Refresh();   // picks up a re-exported FBX
            var target = lapRadius > 0 ? new Vector3(0f, 0.3f, targetZ) : new Vector3(10f, 0.3f, targetZ);
            var off = new Vector3(0, 17f, -14.5f) * zoom;
            var p0 = cam.transform.position; var r0 = cam.transform.rotation; var far0 = cam.farClipPlane;
            cam.transform.position = target + off; cam.transform.rotation = Quaternion.LookRotation(-off); cam.farClipPlane = Mathf.Max(far0, off.magnitude + 150f);
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(12f, true, true);
            // capture-only darkening (11-16 Kido: "좀 더 많이 어둡게"): the map's lights, ambient and fog x dim, restored below
            var mapLights = map.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
            var lightI0 = mapLights.Select(l => l.intensity).ToArray();
            var amb0 = (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fogColor);
            if (dim < 0.999f)
            {
                foreach (var l in mapLights) l.intensity *= dim;
                RenderSettings.ambientSkyColor *= dim; RenderSettings.ambientEquatorColor *= dim; RenderSettings.ambientGroundColor *= dim; RenderSettings.ambientLight *= dim; RenderSettings.fogColor *= dim;
            }
            // the map's own emissive bits (seal plate, lava) ignore the lights: pull the exposure down too for a near-black frame
            GameObject darkVol = null;
            if (dim < 0.5f)
            {
                darkVol = new GameObject("JSB_DarkCapture") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(darkVol, map);
                var dv = darkVol.AddComponent<Volume>(); dv.isGlobal = true; dv.priority = 100;
                var dp = ScriptableObject.CreateInstance<VolumeProfile>(); dp.hideFlags = HideFlags.HideAndDontSave;
                var ca = dp.Add<ColorAdjustments>(true); ca.postExposure.Override(Mathf.Log(dim, 2f) * 0.4f);
                dv.sharedProfile = dp;
            }
            try {

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/Jangsanbeom/renders/map"));
            var vdir = Path.Combine(outDir, $"vid_{outName}"); Directory.CreateDirectory(vdir);
            foreach (var f0 in Directory.GetFiles(vdir, "*.png")) File.Delete(f0);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
            var ldr = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            Shader.SetGlobalFloat("_JSB_UseFixedTime", 1f); Shader.SetGlobalFloat("_WK_UseFixedTime", 1f);
            try
            {
                { var w = new RenderPipeline.StandardRequest { destination = rt }; if (RenderPipeline.SupportsRenderRequest(cam, w)) RenderPipeline.SubmitRenderRequest(cam, w); }
                // 11-16 Kido: the gallop must travel ("제자리 저러면 어떻게해"): run a lap of the arena, the body turning with
                // the path and banking into the curve; speed = how fast the planted paws move back in the in-place clip
                float[] lapCum = null; float lapCycle = 0f, lapLen = 1f;
                float lapSpeed = 0f; Quaternion lapBase = Quaternion.identity; var r0b = boss.transform.rotation; var p0b = boss.transform.position;
                if (lapRadius > 0)
                {
                    boss.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clip.SampleAnimation(boss, 0f);
                    var fL = boss.transform.InverseTransformDirection(head.position - hips.position); fL.y = 0; fL.Normalize();
                    lapBase = Quaternion.Inverse(Quaternion.LookRotation(fL));
                    var toes = new[] { "HindToes.L", "HindToes.R", "FrontToes.L", "FrontToes.R" }.Select(n => boss.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == n)).Where(t => t != null).ToArray();
                    // 11-16 Kido: "걸을 때마다 조금씩 나가야지, 일정 속도로 미끄러지면 이상해" -> the root advances each step by
                    // exactly what the planted paws push back (distance table over one cycle), not at a constant speed
                    var prev = new Vector3[toes.Length]; float sum = 0; int cnt = 0; float dt = clip.length / 240f;
                    lapCum = new float[241];
                    for (int k = 0; k <= 240; k++)
                    {
                        clip.SampleAnimation(boss, k * dt);
                        float low = toes.Min(t => t.position.y);
                        float vs = 0; int vn = 0;
                        for (int ti = 0; ti < toes.Length; ti++)
                        {
                            var pp = toes[ti].position;
                            if (k > 0 && pp.y < low + 0.35f) { float v = -Vector3.Dot(pp - prev[ti], fL) / dt; sum += v; cnt++; vs += v; vn++; }
                            prev[ti] = pp;
                        }
                        if (k > 0) lapCum[k] = lapCum[k - 1] + Mathf.Max(0f, vn > 0 ? vs / vn : 0f) * dt;
                    }
                    lapSpeed = cnt > 0 ? Mathf.Clamp(sum / cnt, 4f, 40f) : 15f;
                    lapCycle = lapCum[240]; lapLen = clip.length;
                    Debug.Log($"[JSB] lap: radius {lapRadius} speed {lapSpeed:F1} m/s from {cnt} planted samples");
                    File.WriteAllText("Temp/jsb_lap.txt", $"speed {lapSpeed:F2} samples {cnt} perCycle {lapCycle:F2}m over {clip.length:F2}s");
                }
                int nf = lapRadius > 0 ? Mathf.Min(360, Mathf.CeilToInt(2f * Mathf.PI * lapRadius / Mathf.Max(lapSpeed, lapCycle / lapLen) * 30f)) : loopClip ? 120 : Mathf.CeilToInt(clip.length * 30);
                System.Action<float> place = tt =>
                {
                    if (lapRadius <= 0) return;
                    float dist = lapSpeed * tt;
                    if (lapCum != null && lapCycle > 0.01f)
                    {
                        float cyc = Mathf.Floor(tt / lapLen), u = (tt - cyc * lapLen) / lapLen * 240f;
                        int i0 = Mathf.Clamp((int)u, 0, 239);
                        dist = cyc * lapCycle + Mathf.Lerp(lapCum[i0], lapCum[i0 + 1], u - i0);
                    }
                    float th = -Mathf.PI * 0.5f + dist / lapRadius;   // start at the south (near the camera), run anticlockwise
                    var pos = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)) * lapRadius;
                    var tan = new Vector3(-Mathf.Sin(th), 0, Mathf.Cos(th));
                    var rot = Quaternion.AngleAxis(-10f, tan) * Quaternion.LookRotation(tan) * lapBase;   // bank ~10 deg into the curve
                    boss.transform.SetPositionAndRotation(pos, rot);
                };
                for (int f = 0; f < nf; f++)
                {
                    float t = f / 30f;
                    if (f == 0 && trails != null) trails.ResetTrails();
                    if (f == 0 && scrape != null) scrape.ResetFX();
                    if (trails != null && f > 0) { place(t - 1f / 60f); clip.SampleAnimation(boss, loopClip ? (t - 1f / 60f) % clip.length : t - 1f / 60f); trails.Sample(t - 1f / 60f); if (scrape != null) scrape.Sample(t - 1f / 60f); }
                    place(t); clip.SampleAnimation(boss, loopClip ? t % clip.length : Mathf.Min(t, clip.length));
                    if (trails != null) trails.Sample(t);
                    if (scrape != null) scrape.Sample(t);
                    SetWarn(boss, loopClip ? 0f : Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((clip.length - t) / 0.5f));
                    Shader.SetGlobalFloat("_JSB_FixedTime", t); Shader.SetGlobalFloat("_WK_FixedTime", t);
                    SyncSkin(boss); Shoot(cam, rt, ldr, tex, Path.Combine(vdir, $"f{f:000}.png"));
                }
                SetWarn(boss, 0f);
                if (scrape != null) File.WriteAllText("Temp/jsb_scrape.txt", scrape.debugStats);
                Debug.Log($"[JSB] map capture {clipName}: {nf} frames, clip {clip.length:F2}s -> {vdir}");
            }
            finally
            {
                Shader.SetGlobalFloat("_JSB_UseFixedTime", 0f); Shader.SetGlobalFloat("_WK_UseFixedTime", 0f); ClearSkin();
                cam.transform.position = p0; cam.transform.rotation = r0; cam.farClipPlane = far0;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(ldr); Object.DestroyImmediate(tex);
            }
            } finally {
                if (darkVol != null) { Object.DestroyImmediate(darkVol.GetComponent<Volume>().sharedProfile); Object.DestroyImmediate(darkVol); }
                for (int li = 0; li < mapLights.Length; li++) if (mapLights[li]) mapLights[li].intensity = lightI0[li];
                (RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.ambientLight, RenderSettings.fogColor) = amb0;
            }
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = async0;
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            foreach (var sc in cleanBefore) if (sc.isLoaded && sc.isDirty) EditorSceneManager.SaveScene(sc);
            if (map.isDirty && openedMap) { }   // closed without saving below
            if (prevActive.IsValid() && prevActive.isLoaded && prevActive != map) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
            if (test.isLoaded) EditorSceneManager.CloseScene(test, true);   // not saved: the boss turn/move stays out of the asset
            if (openedMap && map.isLoaded) EditorSceneManager.CloseScene(map, true);
        }
    }

    // 11-16 Kido: the key turned too fast on the boss. Its own copy of key material A, slower ticks (the telegraph x2.5 still applies)
    // a faint warm light just in front of the face, so in a near-black map the face is what reads (no shadows)
    static void FaceLight(GameObject boss)
    {
        var head = boss.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
        var hips = boss.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips");
        if (head == null || hips == null) return;
        var old = head.Find("JSB_FaceLight"); if (old) Object.DestroyImmediate(old.gameObject);
        var fwd = head.position - hips.position; fwd.y = 0; fwd.Normalize();
        var l = new GameObject("JSB_FaceLight").AddComponent<Light>();
        l.transform.SetParent(head, false); l.transform.position = head.position + fwd * 4f + Vector3.up * 2f;
        l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.45f); l.intensity = 6f; l.range = 9f; l.shadows = LightShadows.None;
    }

    static void BossKeyMat(GameObject boss)
    {
        foreach (var r in boss.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "BulletHeaven/WindKey") continue;
            var path = $"{Root}/Materials/JSB_WindKey_Boss.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(r.sharedMaterial); AssetDatabase.CreateAsset(m, path); }
            else m.CopyPropertiesFromMaterial(r.sharedMaterial);
            m.SetFloat("_TickRate", 0.2f);   // 11-16: slower again (was 0.45); the telegraph no longer speeds it
            // 11-16 Kido: make the key on the back glow (the shared monster keys stay as they are)
            m.SetFloat("_GlowBase", 2.5f); m.SetFloat("_GlowPeak", 9f); m.SetFloat("_CrevGlow", 3.5f); m.SetFloat("_CollarGlow", 3.5f); m.SetFloat("_RimGlow", 1.4f);
            EditorUtility.SetDirty(m);
            r.sharedMaterial = m;
        }
    }

    // soft round puff for the dust (the built-in Default-Particle did not load: the puffs drew as squares)
    static Texture2D SoftDot()
    {
        var path = $"{Root}/Textures/JSB_SoftDot.png";
        if (!File.Exists(path))
        {
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
                float n = Mathf.PerlinNoise(x * 0.15f, y * 0.15f);   // a little breakup so stacked puffs read as dust, not discs
                float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a) * Mathf.Lerp(0.6f, 1f, n);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // URP particle materials for the scrape: soft round sprite, alpha (dust/clods) or additive (sparks)
    static Material FxMat(string name, bool additive)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseMap", SoftDot());
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", additive ? 2f : 0f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
        if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
        m.SetFloat("_Cull", 0f); m.renderQueue = 3000; EditorUtility.SetDirty(m); return m;
    }
    // sparks and red-hot claw gouges: the claw-trail shader (additive HDR, vertex colour fades it), which renders in the captures
    static Material GlowLineMat(string name, Color c)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find("BulletHeaven/JangsanbeomTrail");
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); } else if (m.shader != sh) m.shader = sh;
        m.SetColor("_Color", c); m.SetFloat("_Softness", 0.7f); EditorUtility.SetDirty(m); return m;
    }
    // (unused) claw gouges: glowing red furrows
    static Material GougeMat()
    {
        var m = FxMat("JSB_ScrapeGouge", true);   // red-hot claw marks that cool off (matches the red claws)
        m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", new Color(3f, 0.22f, 0.06f, 1f));
        EditorUtility.SetDirty(m); return m;
    }

    static Material Emissive(string name, Color baseCol, Color emit)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", baseCol); m.SetFloat("_Metallic", 0.8f); m.SetFloat("_Smoothness", 0.55f);
        m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        EditorUtility.SetDirty(m); return m;
    }

    public static void Capture(Camera cam)
    {
        // the editor draws a cyan placeholder while a shader variant compiles in the background; compile synchronously for the stills
        bool wasAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try { CaptureInner(cam); } finally { ShaderUtil.allowAsyncCompilation = wasAsync; }
    }

    // one parasite material per Blender slot (SnakeFlesh, HoleLip, HoleInside, Blood, Vein)
    static Material ParasiteMat(string slot)
    {
        string key = slot.Contains("Lip") ? "Lip" : slot.Contains("HoleInside") ? "Hole" : slot.Contains("Blood") ? "Blood" : slot.Contains("Vein") ? "Vein" : "Flesh";
        var path = $"{Root}/Materials/JSB_Parasite_{key}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("BulletHeaven/JangsanbeomParasite")); AssetDatabase.CreateAsset(m, path); }
        m.SetFloat("_GulpSpeed", 0.7f); m.SetFloat("_BeatSpeed", 0.6f);   // 11-16 Kido: a slow pulse, not a flicker (was 1.4; x1.8 while warning)   // 11-16 Kido: the blood gulp was too fast (was 1.6; the telegraph doubles it)
        m.SetFloat("_GlowBase", 0.06f); m.SetFloat("_GlowPeak", 0.9f); m.SetFloat("_GulpGlow", 0.25f);   // 11-16 Kido: a faint, pale heartbeat (was 0.35 / 6 / 2)   // gulp glow 5 -> 2: the travelling hot spots read as sparkle m.SetFloat("_GulpBulge", 0.16f);
        m.SetColor("_GlowColor", new Color(1f, 0.03f, 0.02f)); m.SetColor("_CrustColor", new Color(0.05f, 0.03f, 0.03f)); m.SetFloat("_CrustAmount", 0.45f);
        switch (key)
        {
            case "Lip":   m.SetColor("_BaseColor", new Color(0.28f, 0.04f, 0.035f)); m.SetFloat("_GulpBulge", 0.05f); m.SetFloat("_GlowPeak", 0.7f); break;
            case "Hole":  m.SetColor("_BaseColor", new Color(0.01f, 0f, 0f)); m.SetFloat("_CrustAmount", 0f); m.SetFloat("_GulpBulge", 0f); m.SetFloat("_GlowBase", 0.15f); m.SetFloat("_GlowPeak", 1.2f); break;
            case "Blood": m.SetColor("_BaseColor", new Color(0.06f, 0.002f, 0.002f)); m.SetFloat("_CrustAmount", 0f); m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_GulpBulge", 0f); m.SetFloat("_GlowBase", 0.05f); m.SetFloat("_GlowPeak", 2f); m.SetFloat("_GulpGlow", 0f); break;
            case "Vein":  m.SetColor("_BaseColor", new Color(0.13f, 0.02f, 0.03f)); m.SetFloat("_GulpBulge", 0.04f); m.SetFloat("_GlowPeak", 0.8f); break;
        }
        EditorUtility.SetDirty(m); return m;
    }

    // rusty gramophone: URP Lit with the same CC0 rusty_metal_04 maps used in Blender; banners/cloth keep their imported image
    static Material GramoMat(Material src)
    {
        string n = src != null ? src.name : "";
        if (n.Contains("Banner") || n.Contains("Cloth") || n.Contains("Talisman")) return src;
        string key = n.Contains("Rope") ? "Rope" : (n.Contains("Bronze") || n.Contains("Gold") || n.Contains("Brass")) ? "Bronze" : n.Contains("Inside") ? "Inside" : "Rust";
        var path = $"{Root}/Materials/JSB_Gramo_{key}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        string T = $"{Root}/Textures/Rust/rusty_metal_04_";
        var nrmImp = (TextureImporter)AssetImporter.GetAtPath(T + "nor_gl_1k.jpg");
        if (nrmImp != null && nrmImp.textureType != TextureImporterType.NormalMap) { nrmImp.textureType = TextureImporterType.NormalMap; nrmImp.SaveAndReimport(); }
        if (key == "Rope") { m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", new Color(0.35f, 0.05f, 0.04f)); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.25f); }
        else
        {
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(T + "diff_1k.jpg"));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(T + "nor_gl_1k.jpg")); m.EnableKeyword("_NORMALMAP");
            m.SetTextureScale("_BaseMap", new Vector2(2, 2));
            m.SetColor("_BaseColor", key == "Bronze" ? new Color(1f, 0.8f, 0.45f) : key == "Inside" ? new Color(0.35f, 0.3f, 0.28f) : Color.white);
            m.SetFloat("_Metallic", key == "Bronze" ? 0.9f : 0.6f); m.SetFloat("_Smoothness", key == "Bronze" ? 0.5f : 0.3f);
        }
        EditorUtility.SetDirty(m); return m;
    }

    static void PlaceFrontCam(Camera cam, GameObject boss)
    {
        var head = boss.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Head");
        var hips = boss.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Hips");
        if (head == null || hips == null) return;
        var fwd = head.position - hips.position; fwd.y = 0; fwd.Normalize();
        var look = (head.position + hips.position) * 0.5f; look.y = 4f;
        // the real game camera (RoomRun CameraRig): offset (0, 17, -14.5) x zoom 1.6, fov 30 -> pitch 49.5 deg, 35.8 m away
        float pitch = 30f * Mathf.Deg2Rad, dist = 62f;   // Kido: not from the top of the head; pulled back like the 11-13 reference shot
        cam.transform.position = look + fwd * Mathf.Cos(pitch) * dist + Vector3.up * Mathf.Sin(pitch) * dist;
        cam.transform.LookAt(look); cam.fieldOfView = 30;
    }

    static void Shoot(Camera cam, RenderTexture rt, RenderTexture ldr, Texture2D tex, string file)
    {
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        Graphics.Blit(rt, ldr);
        var prev = RenderTexture.active; RenderTexture.active = ldr;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
    }

    // 11-16: edit-mode captures never skinned the body. SampleAnimation moves the bones (rigid children like the gramophone
    // follow), but SkinnedMeshRenderers only re-skin in the player loop, which does not run between our renders, so the fur
    // and the parasite stood still in every capture. Bake each skinned mesh per frame and draw the baked copy instead.
    static readonly System.Collections.Generic.Dictionary<SkinnedMeshRenderer, MeshRenderer> skinProxies = new System.Collections.Generic.Dictionary<SkinnedMeshRenderer, MeshRenderer>();
    static void SyncSkin(GameObject boss)
    {
        foreach (var smr in boss.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skinProxies.TryGetValue(smr, out var mr) || mr == null)
            {
                var go = new GameObject("SkinProxy_" + smr.name) { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, boss.scene);
                go.AddComponent<MeshFilter>().sharedMesh = new Mesh { name = smr.name + "_baked", hideFlags = HideFlags.HideAndDontSave };
                mr = go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = smr.shadowCastingMode; mr.receiveShadows = smr.receiveShadows;
                skinProxies[smr] = mr;
            }
            var mesh = mr.GetComponent<MeshFilter>().sharedMesh;
            smr.BakeMesh(mesh, true);
            mr.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation); mr.transform.localScale = Vector3.one;
            mr.sharedMaterials = smr.sharedMaterials;
            var pb = new MaterialPropertyBlock(); smr.GetPropertyBlock(pb); mr.SetPropertyBlock(pb);
            mr.enabled = smr.enabled && smr.gameObject.activeInHierarchy;
            smr.forceRenderingOff = true;
        }
    }
    static void ClearSkin()
    {
        foreach (var kv in skinProxies)
        {
            if (kv.Key != null) kv.Key.forceRenderingOff = false;
            if (kv.Value != null) { Object.DestroyImmediate(kv.Value.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(kv.Value.gameObject); }
        }
        skinProxies.Clear();
    }

    static void SetWarn(GameObject boss, float w)
    {
        foreach (var r in boss.GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Warn") || r.sharedMaterial.shader.name == "BulletHeaven/WindKey") continue;
            var pb = new MaterialPropertyBlock(); r.GetPropertyBlock(pb); pb.SetFloat("_Warn", w); r.SetPropertyBlock(pb);
        }
    }

    static void CaptureInner(Camera cam)
    {
        var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/Jangsanbeom/renders/glow"));
        Directory.CreateDirectory(outDir);
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
        var ldr = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        { var w = new RenderPipeline.StandardRequest { destination = rt }; if (RenderPipeline.SupportsRenderRequest(cam, w)) RenderPipeline.SubmitRenderRequest(cam, w); }
        var anims = cam.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Animator>()).ToArray();
        const float beatSpeed = 1.4f;
        Shader.SetGlobalFloat("_JSB_UseFixedTime", 1f); Shader.SetGlobalFloat("_WK_UseFixedTime", 1f);
        try
        {
            float[] ts = { 0.3f, 1.6f, 2.6f, 3.5f };
            foreach (var only in anims)
            {
                foreach (var an in anims) an.gameObject.SetActive(an == only); ClearSkin();
                var ctrl = only.runtimeAnimatorController; if (ctrl == null || ctrl.animationClips.Length == 0) continue;
                var clip = ctrl.animationClips[0];
                bool warnClip = only.name.Contains("telegraph");
                // stills: alternate heartbeat peak / rest so both are visible
                var trails = only.GetComponent<JangsanbeomClawTrails>();
                var scrape = only.GetComponent<JangsanbeomScrapeFX>();
                for (int k = 0; k < ts.Length; k++)
                {
                    if (trails != null)
                    {   // run the claw trail over the 0.3 s before the still so its afterimage shows
                        trails.ResetTrails();
                        for (float tt = ts[k] - 0.3f; tt < ts[k]; tt += 1f / 60f) { clip.SampleAnimation(only.gameObject, Mathf.Max(0, tt) % clip.length); trails.Sample(tt); }
                    }
                    clip.SampleAnimation(only.gameObject, ts[k] % clip.length);
                    if (trails != null) trails.Sample(ts[k]);
                    PlaceFrontCam(cam, only.gameObject);
                    SetWarn(only.gameObject, warnClip ? 1f : 0f);
                    Shader.SetGlobalFloat("_JSB_FixedTime", (k + (k % 2 == 0 ? 0.02f : 0.6f)) / beatSpeed);
                    SyncSkin(only.gameObject); Shoot(cam, rt, ldr, tex, Path.Combine(outDir, $"{only.name}_{k}.png"));
                }
                // frame sequence so the gulping and the heartbeat can be seen moving
                var vdir = Path.Combine(outDir, $"vid_{only.name}"); Directory.CreateDirectory(vdir);
                foreach (var f0 in Directory.GetFiles(vdir, "*.png")) File.Delete(f0);
                int nf = Mathf.Min(150, Mathf.CeilToInt(clip.length * 30));
                for (int f = 0; f < nf; f++)
                {
                    float t = f / 30f;
                    if (f == 0 && trails != null) trails.ResetTrails();
                    if (f == 0 && scrape != null) scrape.ResetFX();
                    if (trails != null && f > 0)   // half steps between frames keep fast swipes as smooth arcs
                    { clip.SampleAnimation(only.gameObject, (t - 1f / 60f) % clip.length); trails.Sample(t - 1f / 60f); if (scrape != null) scrape.Sample(t - 1f / 60f); }
                    clip.SampleAnimation(only.gameObject, t % clip.length);
                    if (trails != null) trails.Sample(t);
                    if (scrape != null) scrape.Sample(t);
                    if (f == 0) PlaceFrontCam(cam, only.gameObject);
                    SetWarn(only.gameObject, warnClip ? Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((clip.length - t) / 0.5f) : 0f);
                    Shader.SetGlobalFloat("_JSB_FixedTime", t); Shader.SetGlobalFloat("_WK_FixedTime", t);
                    SyncSkin(only.gameObject); Shoot(cam, rt, ldr, tex, Path.Combine(vdir, $"f{f:000}.png"));
                }
                SetWarn(only.gameObject, 0f);
            }
        }
        finally { Shader.SetGlobalFloat("_JSB_UseFixedTime", 0f); Shader.SetGlobalFloat("_WK_UseFixedTime", 0f); ClearSkin(); }
        Debug.Log($"[JSB] captured stills to {outDir}");
    }
}
#endif
