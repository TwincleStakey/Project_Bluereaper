// 태엽열쇠 setup: import settings, mesh asset with turn-safe bounds, 4 phase materials, the WindKey prefab, and a demo
// (장산범 + dark monsters in the 장산범 glow test scene) captured from the RoomRun game camera without entering Play mode.
// Trigger: create Temp/wk_build.trigger (log lines start with [WindKey]; Temp/wk_build.done when finished).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static class WindKeySetup
{
    const string Root = "Assets/Game/WindKey";
    static readonly float[] Phases = { 0f, 0.27f, 0.53f, 0.79f };
    static readonly float[] Rates = { 1.5f, 1.38f, 1.62f, 1.45f };

    [InitializeOnLoadMethod]
    static void Watch() { EditorApplication.update += Tick; }
    static void Tick()
    {
        const string trig = "Temp/wk_build.trigger";
        if (!File.Exists(trig) || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var mode = File.ReadAllText(trig).Trim();
        File.Delete(trig);
        try { BuildAssets(); if (mode != "assets") Demo(mode); } catch (System.Exception e) { Debug.LogError("[WindKey] " + e); }
        File.WriteAllText("Temp/wk_build.done", System.DateTime.Now.ToString("o"));
    }

    public static GameObject BuildAssets()
    {
        AssetDatabase.Refresh();
        // importers
        var mi = (ModelImporter)AssetImporter.GetAtPath($"{Root}/Models/WindKey.fbx");
        if (mi != null && (!mi.bakeAxisConversion || mi.importAnimation || mi.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            mi.bakeAxisConversion = true; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importCameras = false; mi.importLights = false;
            mi.isReadable = false; mi.importTangents = ModelImporterTangents.CalculateMikk; mi.SaveAndReimport();
        }
        void Tex(string n, bool normal, bool srgb)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/{n}.png");
            if (ti == null) return;
            var want = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType == want && ti.sRGBTexture == srgb && ti.maxTextureSize == 1024) return;
            ti.textureType = want; ti.sRGBTexture = srgb; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.Compressed; ti.SaveAndReimport();
        }
        Tex("WK_Albedo", false, true); Tex("WK_Normal", true, false); Tex("WK_AO", false, false);

        // mesh asset: the shader spins the key about Y, so the bounds must cover the full turn or keys pop at screen edges
        var src = AssetDatabase.LoadAllAssetsAtPath($"{Root}/Models/WindKey.fbx").OfType<Mesh>().First();
        var meshPath = $"{Root}/Models/WindKey_Mesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
        EditorUtility.CopySerialized(src, mesh); mesh.name = "WindKey_Mesh";
        // the Blender export comes in with the shaft along -Z: stand it up so the shaft is +Y (origin = where it enters the body)
        var sb = src.bounds;
        if (sb.size.z > sb.size.y * 1.5f)
        {
            var q = Quaternion.Euler(sb.center.z < 0 ? 90f : -90f, 0, 0);
            var vv = mesh.vertices; var nn = mesh.normals; var tt = mesh.tangents;
            for (int i = 0; i < vv.Length; i++) { vv[i] = q * vv[i]; nn[i] = q * nn[i]; var t3 = q * (Vector3)tt[i]; tt[i] = new Vector4(t3.x, t3.y, t3.z, tt[i].w); }
            mesh.vertices = vv; mesh.normals = nn; mesh.tangents = tt; mesh.RecalculateBounds();
        }
        var b = mesh.bounds; float r = 0f;
        var vs = mesh.vertices; foreach (var v in vs) r = Mathf.Max(r, new Vector2(v.x, v.z).magnitude);
        mesh.bounds = new Bounds(new Vector3(0, b.center.y, 0), new Vector3(2 * r, b.size.y, 2 * r));
        EditorUtility.SetDirty(mesh);
        Debug.Log($"[WindKey] mesh tris={mesh.triangles.Length / 3} bounds={b.min}..{b.max} turnRadius={r:0.000}");

        // materials (same look, different phase/rate so keys don't tick in unison)
        var mats = new List<Material>();
        for (int i = 0; i < Phases.Length; i++)
        {
            var p = $"{Root}/Materials/WK_Key_{(char)('A' + i)}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("BulletHeaven/WindKey")); AssetDatabase.CreateAsset(m, p); }
            m.shader = Shader.Find("BulletHeaven/WindKey");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/WK_Albedo.png"));
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/WK_Normal.png"));
            m.SetTexture("_AOMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/WK_AO.png"));
            m.SetFloat("_Phase", Phases[i]); m.SetFloat("_TickRate", Rates[i]);
            m.enableInstancing = true;
            // look (kept here so every variant matches): rusty bronze stays readable, red only in the deep crevices,
            // the wound collar and on each tick
            m.SetFloat("_Brighten", 2.2f); m.SetFloat("_Metallic", 0.5f); m.SetFloat("_Smoothness", 0.45f);
            m.SetFloat("_GlowBase", 0.5f); m.SetFloat("_GlowPeak", 4f); m.SetFloat("_CrevGlow", 2.5f); m.SetFloat("_CrevLo", 0.25f); m.SetFloat("_CrevHi", 0.6f);
            m.SetFloat("_CollarGlow", 2f); m.SetFloat("_CollarHeight", 0.1f); m.SetFloat("_RimGlow", 0.25f);
            ApplyOverrides(m);
            EditorUtility.SetDirty(m); mats.Add(m);
        }
        foreach (var msg in ShaderUtil.GetShaderMessages(mats[0].shader)) Debug.Log($"[WindKey] shader msg: {msg.severity} {msg.message} line {msg.line}");

        // prefab
        var prefabPath = $"{Root}/Prefabs/WindKey.prefab";
        var go = new GameObject("WindKey");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[0];
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera; mr.allowOcclusionWhenDynamic = false;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log("[WindKey] assets built");
        return prefab;
    }

    // tuning without code edits: Temp/wk_overrides.txt, lines "_Name value"
    static void ApplyOverrides(Material m)
    {
        if (!File.Exists("Temp/wk_overrides.txt")) return;
        foreach (var line in File.ReadAllLines("Temp/wk_overrides.txt"))
        {
            var p = line.Split(' ');
            if (p.Length == 2 && m.HasProperty(p[0]) && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) m.SetFloat(p[0], f);
        }
    }

    public static Material[] Variants() => Enumerable.Range(0, Phases.Length).Select(i => AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/WK_Key_{(char)('A' + i)}.mat")).ToArray();

    static WindKeyAttach AddKey(GameObject host, float width, float pitch, float sink, Vector3 offset, string[] hints = null, float yaw = 0f)
    {
        var a = host.AddComponent<WindKeyAttach>();
        if (hints != null) a.boneHints = hints;
        a.yaw = yaw;
        a.keyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/WindKey.prefab");
        a.phaseVariants = Variants(); a.width = width; a.pitch = pitch; a.sink = sink; a.offset = offset;
        a.Attach();
        Debug.Log($"[WindKey] {host.name}: bone={(a.bone ? a.bone.name : "-")} key at {a.Key.position} (bone {(a.bone ? a.bone.position : Vector3.zero)})");
        return a;
    }

    // humanoid clips only pose in the editor through AnimationMode (stopped again when the demo ends)
    static void Pose(GameObject go, float t)
    {
        var an = go.GetComponentInChildren<Animator>();
        if (an == null || an.runtimeAnimatorController == null) return;
        var clips = an.runtimeAnimatorController.animationClips;
        var clip = clips.FirstOrDefault(c => c.name.Contains("Walk")) ?? clips.FirstOrDefault();
        if (clip == null) return;
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
        AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(an.gameObject, clip, t % clip.length); AnimationMode.EndSampling();
    }

    public static void Demo(string mode)
    {
        // open the 장산범 glow test scene additively (it has the boss with its materials, ground, light, bloom volume)
        var prevActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scenePath = "Assets/Game/Jangsanbeom/Scenes/JSB_GlowTest.unity";
        var hidden = new List<GameObject>();
        var cleanBefore = new List<UnityEngine.SceneManagement.Scene>();
        for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            if (!sc.isLoaded) continue;
            if (!sc.isDirty) cleanBefore.Add(sc);
            foreach (var g in sc.GetRootGameObjects()) if (g.activeSelf) { g.SetActive(false); hidden.Add(g); }
        }
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var amb = (RenderSettings.ambientMode, RenderSettings.ambientLight, RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogMode);
        bool wasAsync = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        try
        {
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.03f, 0.033f, 0.05f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.01f, 0.012f, 0.02f); RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = 0.006f;
            var roots = scene.GetRootGameObjects();
            foreach (var g in roots) if (g.GetComponent<Camera>()) g.SetActive(false);
            foreach (var g in roots) { var l = g.GetComponent<Light>(); if (l && l.type == LightType.Directional) l.intensity = Arg(mode, "sun", 0.9f); }
            string bossName = Arg(mode, "howl", 0f) > 0 ? "p1_howl_gramophone" : "p2_charge_gramophone";
            var boss = roots.FirstOrDefault(g => g.name == bossName);
            foreach (var g in roots) if ((g.name.StartsWith("p0_") || g.name.StartsWith("p1_") || g.name.StartsWith("p2_")) && g != boss) g.SetActive(false);
            if (boss) boss.SetActive(true);
            var ground = roots.FirstOrDefault(g => g.name == "Ground"); if (ground) ground.transform.localScale = Vector3.one * 20;

            float bossW = Arg(mode, "bossw", 5.5f), monW = Arg(mode, "monw", 1.3f), monPitch = Arg(mode, "pitch", 55f);
            if (boss != null)
            {
                boss.transform.position = new Vector3(0, 0, 14); boss.transform.rotation = Quaternion.Euler(0, Arg(mode, "bossyaw", 250f), 0);
                var bones = boss.GetComponentsInChildren<Transform>().Where(t => t.name.IndexOf("pine", System.StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("chest", System.StringComparison.OrdinalIgnoreCase) >= 0).Select(t => t.name);
                Debug.Log("[WindKey] boss bones: " + string.Join(", ", bones));
                var an = boss.GetComponent<Animator>();
                var clip = an && an.runtimeAnimatorController ? an.runtimeAnimatorController.animationClips[0] : null;
                if (clip) clip.SampleAnimation(boss, Arg(mode, "bosst", 0.3f));
                // the 장산범 already carries the gramophone on its shoulders and the snake round its middle: the key goes on the lower back, gears across the body
                var bk = AddKey(boss, bossW, Arg(mode, "bosspitch", 0f), 0.2f, Vector3.zero, new[] { "Spine1" }, Arg(mode, "bossyawkey", 90f));
                if (bk.bone) Debug.Log($"[WindKey] boss bone up={bk.bone.up} fwd={bk.bone.forward} bounds={boss.GetComponentInChildren<SkinnedMeshRenderer>().bounds}");
            }
            else Debug.LogWarning("[WindKey] boss not found in " + scenePath);

            // regular monsters in front of the boss, facing the player side (south) and sideways
            var names = new[] { "DM_Goblin", "DM_Warrok", "DM_Maw", "DM_Ganfaul" };
            var spots = new (Vector3 p, float yaw)[] { (new Vector3(-7, 0, -6), 160), (new Vector3(-2.5f, 0, -8), 200), (new Vector3(2.5f, 0, -7), 120), (new Vector3(7, 0, -6), 230),
                                                        (new Vector3(-5, 0, -12), 180), (new Vector3(0, 0, -13), 90), (new Vector3(5, 0, -12), 270), (new Vector3(-9, 0, -11), 30) };
            var mons = new List<GameObject>();
            for (int i = 0; i < spots.Length; i++)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/DarkMonsters/Prefabs/{names[i % names.Length]}.prefab");
                if (pf == null) continue;
                var m = (GameObject)PrefabUtility.InstantiatePrefab(pf, scene);
                PrefabUtility.UnpackPrefabInstance(m, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                m.transform.position = spots[i].p; m.transform.rotation = Quaternion.Euler(0, spots[i].yaw, 0);
                foreach (var mb in m.GetComponentsInChildren<MonoBehaviour>()) if (!(mb is WindKeyAttach) && mb.GetType().Namespace == "RoomRun") mb.enabled = false;
                AddKey(m, monW, monPitch, 0.18f, Vector3.zero);
                Pose(m, 0.3f + 0.17f * i);
                mons.Add(m);
            }

            var camGo = new GameObject("WK_GameCam");
            var cam = camGo.AddComponent<Camera>(); cam.allowHDR = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            cam.fieldOfView = 30; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/WindKey/shots"));
            Directory.CreateDirectory(outDir);
            // RoomRun CameraRig: height 17, back 14.5, fov 30, x zoom (1.6 = default game view)
            void Place(Vector3 focus, float zoom) { cam.transform.position = focus + new Vector3(0, 17f, -14.5f) * zoom; cam.transform.LookAt(focus); }
            Shader.SetGlobalFloat("_WK_UseFixedTime", 1f); Shader.SetGlobalFloat("_JSB_UseFixedTime", 1f);
            try
            {
                var shots = new (string n, Vector3 f, float z, float t)[] {
                    ("game_zoom1.6", new Vector3(0, 0, -4), 1.6f, 0.02f), ("game_zoom1.6_rest", new Vector3(0, 0, -4), 1.6f, 0.45f),
                    ("mid_monsters", new Vector3(0, 1, -9), 0.75f, 0.02f), ("close_monsters", new Vector3(-2.5f, 2, -8), 0.35f, 0.05f),
                    ("boss_game", new Vector3(0, 0, 12), 2.6f, 0.03f), ("boss_mid", new Vector3(0, 5, 14), 1.4f, 0.45f) };
                foreach (var s in shots)
                {
                    Place(s.f, s.z);
                    Shader.SetGlobalFloat("_WK_FixedTime", s.t / 1.5f); Shader.SetGlobalFloat("_JSB_FixedTime", 1.0f);
                    Shoot(cam, Path.Combine(outDir, s.n + ".png"), 1600, 900);
                }
                // turn sequence on a close monster (for a gif/mp4)
                if (mode.Contains("seq"))
                {
                    var vdir = Path.Combine(outDir, "seq"); Directory.CreateDirectory(vdir);
                    foreach (var f0 in Directory.GetFiles(vdir, "*.png")) File.Delete(f0);
                    Place(new Vector3(-2.5f, 2, -8), 0.35f);
                    for (int f = 0; f < 90; f++) { Shader.SetGlobalFloat("_WK_FixedTime", f / 30f); Shader.SetGlobalFloat("_JSB_FixedTime", f / 30f); Shoot(cam, Path.Combine(vdir, $"f{f:000}.png"), 960, 540); }
                    var bdir = Path.Combine(outDir, "seq_boss"); Directory.CreateDirectory(bdir);
                    foreach (var f0 in Directory.GetFiles(bdir, "*.png")) File.Delete(f0);
                    Place(new Vector3(0, 5, 14), 1.4f);
                    for (int f = 0; f < 90; f++) { Shader.SetGlobalFloat("_WK_FixedTime", f / 30f); Shader.SetGlobalFloat("_JSB_FixedTime", f / 30f); Shoot(cam, Path.Combine(bdir, $"f{f:000}.png"), 960, 540); }
                }
                if (mode.Contains("perf")) Perf(cam, scene, mons, monW, monPitch);
            }
            finally { Shader.SetGlobalFloat("_WK_UseFixedTime", 0f); Shader.SetGlobalFloat("_JSB_UseFixedTime", 0f); }
            Debug.Log($"[WindKey] demo captured to {outDir}");
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = wasAsync;
            (RenderSettings.ambientMode, RenderSettings.ambientLight, RenderSettings.fog, RenderSettings.fogColor, RenderSettings.fogDensity, RenderSettings.fogMode) = amb;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            if (prevActive.IsValid() && prevActive.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(prevActive);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);   // not saved: the glow test scene is left as it was
            foreach (var g in hidden) if (g != null) g.SetActive(true);
            foreach (var sc in cleanBefore) if (sc.IsValid() && sc.isLoaded && sc.isDirty) EditorSceneManager.SaveScene(sc);
        }
    }

    // rough cost: render the game view with N keys on static props vs without (GPU synced by a readback each frame)
    static void Perf(Camera cam, UnityEngine.SceneManagement.Scene scene, List<GameObject> mons, float w, float pitch)
    {
        var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/WindKey.prefab");
        var mats = Variants();
        var holder = new GameObject("WK_PerfKeys"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
        var rnd = new System.Random(3);
        for (int i = 0; i < 300; i++)
        {
            var k = (GameObject)Object.Instantiate(pf, holder.transform);
            k.transform.position = new Vector3((float)rnd.NextDouble() * 36 - 18, 1.5f, (float)rnd.NextDouble() * 26 - 18);
            k.transform.localScale = Vector3.one * (w / 0.864f);
            k.GetComponent<Renderer>().sharedMaterial = mats[i % mats.Length];
        }
        cam.transform.position = new Vector3(0, 0, -4) + new Vector3(0, 17f, -14.5f) * 1.6f; cam.transform.LookAt(new Vector3(0, 0, -4));
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.DefaultHDR); var px = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
        double Run(bool on)
        {
            holder.SetActive(on);
            for (int i = 0; i < 5; i++) Render(cam, rt);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) { Render(cam, rt); var prev = RenderTexture.active; RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = prev; }
            return sw.Elapsed.TotalMilliseconds / 60.0;
        }
        double off1 = Run(false), on1 = Run(true), off2 = Run(false), on2 = Run(true);
        Shoot(cam, Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/WindKey/shots/perf_300keys.png")), 1600, 900);
        var line = $"[WindKey] perf 1920x1080 frame ms: without keys {off1:0.00}/{off2:0.00}, with 300 extra keys {on1:0.00}/{on2:0.00}";
        Debug.Log(line); File.AppendAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource/WindKey/perf.txt")), System.DateTime.Now.ToString("s") + " " + line + "\n");
        rt.Release(); Object.DestroyImmediate(holder);
    }

    static float Arg(string mode, string key, float def)
    {
        foreach (var tok in mode.Split(' ')) { var kv = tok.Split('='); if (kv.Length == 2 && kv[0] == key && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) return f; }
        return def;
    }

    static void Render(Camera cam, RenderTexture rt)
    {
        var req = new RenderPipeline.StandardRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
        else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
    }

    static void Shoot(Camera cam, string file, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR);
        var ldr = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        Render(cam, rt); Render(cam, rt);
        Graphics.Blit(rt, ldr);
        var prev = RenderTexture.active; RenderTexture.active = ldr;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        rt.Release(); ldr.Release(); Object.DestroyImmediate(tex);
    }
}
#endif
