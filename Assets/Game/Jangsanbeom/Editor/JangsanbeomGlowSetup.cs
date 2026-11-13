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
    static readonly string[] Clips = { "p1_howl_gramophone", "p2_charge_gramophone" };

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/jsb_glow.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(trig);
        try { Build(); } catch (System.Exception e) { Debug.LogError("[JSB] " + e); }
        File.WriteAllText("Temp/jsb_glow.done", System.DateTime.Now.ToString("o"));
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
        mat.SetFloat("_AmbientBoost", 1.2f);
        mat.SetFloat("_EyeIntensity", 8f);
        mat.SetFloat("_RimIntensity", 4f); mat.SetFloat("_RimPower", 2.5f); mat.SetFloat("_FurLumMin", 0.35f);
        mat.SetFloat("_SparkleDensity", 7f); mat.SetFloat("_SparkleAmount", 0.22f); mat.SetFloat("_SparkleIntensity", 9f); mat.SetFloat("_SparkleSpeed", 3f);
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
                else if (r.name.StartsWith("HornInside")) r.sharedMaterial = Emissive("JSB_HornMouth", new Color(0.05f, 0.08f, 0.08f), new Color(0.2f, 1.6f, 1.5f));
                else if (r.name.StartsWith("GoldBand")) r.sharedMaterial = Emissive("JSB_ChainGold", new Color(0.75f, 0.52f, 0.18f), new Color(0.6f, 0.38f, 0.08f));
                else if (r.name.StartsWith("Link")) r.sharedMaterial = Emissive("JSB_ChainIron", new Color(0.16f, 0.16f, 0.18f), new Color(0.02f, 0.03f, 0.05f));
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/{c}.fbx");
            AnimationClip ac = null; foreach (var o in clip) if (o is AnimationClip a && !a.name.StartsWith("__preview")) ac = a;
            if (ac != null)
            {
                AssetDatabase.DeleteAsset($"{Root}/Animation/{c}.controller"); var ctrl = AnimatorController.CreateAnimatorControllerAtPathWithClip($"{Root}/Animation/{c}.controller", ac);
                var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>(); an.runtimeAnimatorController = ctrl;
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

    static void CaptureInner(Camera cam)
    {
        var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/Jangsanbeom/renders/glow"));
        Directory.CreateDirectory(outDir);
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.DefaultHDR);
        var ldr = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        { var w = new RenderPipeline.StandardRequest { destination = rt }; if (RenderPipeline.SupportsRenderRequest(cam, w)) RenderPipeline.SubmitRenderRequest(cam, w); }
        float[] ts = { 0.3f, 1.6f, 2.6f, 3.5f };
        var anims = cam.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Animator>()).ToArray();
        foreach (var only in anims)
        for (int k = 0; k < ts.Length; k++)
        {
            foreach (var an in anims) an.gameObject.SetActive(an == only);
            var ctrl = only.runtimeAnimatorController; if (ctrl == null || ctrl.animationClips.Length == 0) continue;
            var clip = ctrl.animationClips[0]; clip.SampleAnimation(only.gameObject, ts[k] % clip.length);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; Debug.LogWarning("[JSB] StandardRequest unsupported, used Camera.Render"); }
            Graphics.Blit(rt, ldr);
            var prev = RenderTexture.active; RenderTexture.active = ldr;
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(outDir, $"{only.name}_{k}.png"), tex.EncodeToPNG());
        }
        Debug.Log($"[JSB] captured stills to {outDir}");
    }
}
#endif
