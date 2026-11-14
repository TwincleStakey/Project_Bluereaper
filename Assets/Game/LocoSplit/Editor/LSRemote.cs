// Drives the split-locomotion test without menus. Write one line to ArtSource/LocoSplit/ls_cmd.txt:
//   build   (import settings, masks, controller, sword prefab, scene)
//   demo    (opens the scene, enters play mode, runs LSDemo which saves frames to ArtSource/LocoSplit/shots,
//            leaves play mode when demo_done.txt appears)
//   stop    (leaves play mode)
//   rrplayer (RR_GreatswordPlayer prefab for the RoomRun player, set on RoomRunConfig.greatswordPrefab)
// Results go to ArtSource/LocoSplit/ls_log.txt.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LocoSplit
{
    [InitializeOnLoad]
    public static class LSRemote
    {

        // 11-14 freeze guard (two editor freezes 11-13: a remote command opened a scene / started Play while Play was still
        // entering or exiting). Same code in every *_cmd remote (see RoomRunRemote.Gate): nothing starts while Play changes,
        // for 15 s after a Play change or domain reload, while compiling/importing, or while another remote holds
        // ArtSource/remote_play.lock (a lock older than 10 min counts as stale).
        static class Gate
        {
            static double lastChange = UnityEditor.EditorApplication.timeSinceStartup;
            static Gate() { UnityEditor.EditorApplication.playModeStateChanged += _ => lastChange = UnityEditor.EditorApplication.timeSinceStartup; }
            static string LockPath => System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName, "ArtSource", "remote_play.lock");
            public static bool Settled() => !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && !UnityEditor.EditorApplication.isCompiling
                && !UnityEditor.EditorApplication.isUpdating && UnityEditor.EditorApplication.timeSinceStartup - lastChange >= 15.0;
            public static bool Ready(string me)
            {
                if (!Settled()) return false;
                try
                {
                    if (System.IO.File.Exists(LockPath) && !System.IO.File.ReadAllText(LockPath).StartsWith(me + " ")
                        && (System.DateTime.Now - System.IO.File.GetLastWriteTime(LockPath)).TotalMinutes < 10) return false;
                }
                catch (System.IO.IOException) { return false; }
                return true;
            }
            public static void Take(string me) { try { System.IO.File.WriteAllText(LockPath, me + " " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (System.IO.IOException) { } }
            public static void Release(string me)
            {
                try { if (System.IO.File.Exists(LockPath) && System.IO.File.ReadAllText(LockPath).StartsWith(me + " ")) System.IO.File.Delete(LockPath); } catch (System.IO.IOException) { }
            }
        }

        static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "LocoSplit");
        static string Cmd => Path.Combine(Dir, "ls_cmd.txt");
        static string Log => Path.Combine(Dir, "ls_log.txt");
        static string Done => Path.Combine(Dir, "demo_done.txt");
        const string KeyState = "LocoSplit.State";
        static double next, demoStart, retryAt;

        static LSRemote() { EditorApplication.update += Tick; }

        static void Note(string s)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Log, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }

        static void RestoreStartScene()
        {
            string p = SessionState.GetString("LocoSplit.StartScene", "");
            if (p != "") EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(p);
            SessionState.EraseString("LocoSplit.StartScene");
        }

        static string VfxTest(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) return "vfxtest: no prefab " + path;
            var scene = EditorSceneManager.NewPreviewScene();
            // the game's orientation (aim +Z, then -90 about X) at four moments, to see which way the arc sweeps
            var rots = new[] { new Vector3(-90, 0, 0), new Vector3(-90, 0, 0), new Vector3(-90, 0, 0), new Vector3(-90, 0, 0) };
            var times = new[] { 0.03f, 0.07f, 0.12f, 0.25f };
            for (int i = 0; i < rots.Length; i++)
            {
                var go = (GameObject)Object.Instantiate(prefab);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = new Vector3(i * 12 - 18, 1, 0);
                go.transform.rotation = Quaternion.Euler(rots[i]);   // the player aims along +Z (up on screen)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(times[i], false, true);
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, scene);
                marker.transform.position = new Vector3(i * 12 - 18, 0, 3); marker.transform.localScale = new Vector3(0.3f, 0.3f, 2f);
            }
            var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            var camGo = new GameObject("Cam");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.12f, 0.1f);
            cam.transform.position = new Vector3(0, 30, -18); cam.transform.rotation = Quaternion.Euler(55, 0, 0); cam.fieldOfView = 45;
            var rt = new RenderTexture(1600, 600, 24);
            cam.targetTexture = rt; cam.Render();
            var tex = new Texture2D(1600, 600, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 600), 0, 0); RenderTexture.active = null;
            string outp = Path.Combine(Dir, "vfxtest.png");
            File.WriteAllBytes(outp, tex.EncodeToPNG());
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            EditorSceneManager.ClosePreviewScene(scene);
            return "vfxtest saved " + outp + " (X-90 at t=0.03/0.07/0.12/0.25; cube shows aim)";
        }

        // Every prefab in Assets/Free Slash VFX/Prefabs: tiles to ArtSource/LocoSplit/vfxall/<name>__<view>.png
        // views: game0 / game1 = 55° game camera as the game attaches it (aim +Z, -90 about X) at t 0.08 / 0.2;
        //        side = side view as attached at t 0.12; raw = game camera, prefab's own rotation, t 0.12
        static string VfxAll()
        {
            string outDir = Path.Combine(Dir, "vfxall");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Free Slash VFX/Prefabs" });
            const int TW = 360, TH = 300;
            var rt = new RenderTexture(TW, TH, 24); var tex = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            int n = 0;
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string name = Path.GetFileNameWithoutExtension(path);
                var views = new[] { ("game0", 0.08f, true, 0), ("game1", 0.2f, true, 0), ("side", 0.12f, true, 1), ("raw", 0.12f, false, 0) };
                foreach (var (tag, time, attached, cam) in views)
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    var go = (GameObject)Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                    go.transform.position = new Vector3(0, 1, 0);
                    if (attached) go.transform.rotation = Quaternion.Euler(-90, 0, 0);
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(time, false, true);
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);   // the player, 1 m wide
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(body, scene);
                    body.transform.position = new Vector3(0, 1, 0); body.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                    var aimBar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(aimBar, scene);
                    aimBar.transform.position = new Vector3(0, 0, 2.5f); aimBar.transform.localScale = new Vector3(0.08f, 0.05f, 3f);
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ground, scene);
                    ground.transform.localScale = Vector3.one * 2f;
                    var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional;
                    light.transform.rotation = Quaternion.Euler(50, -30, 0);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                    var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                    var c = camGo.AddComponent<Camera>(); c.scene = scene; c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.08f, 0.09f, 0.1f); c.fieldOfView = 50;
                    if (cam == 0) { c.transform.position = new Vector3(0, 9f, -6.3f); c.transform.rotation = Quaternion.Euler(55, 0, 0); }
                    else { c.transform.position = new Vector3(8f, 1.5f, 0.5f); c.transform.rotation = Quaternion.Euler(5, -90, 0); }
                    c.targetTexture = rt; c.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); RenderTexture.active = null;
                    c.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(outDir, $"{name}__{tag}.png"), tex.EncodeToPNG());
                    EditorSceneManager.ClosePreviewScene(scene);
                }
                n++;
            }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            return $"vfxall: {n} prefabs to {outDir}";
        }

        // Every greatsword attack clip (Art/MixamoRR + Art/Mixamo GS_Slash*) sampled on the RR player at 6 moments,
        // game camera and side, tiles to ArtSource/LocoSplit/clipsheet/<clip>__<view>_<k>.png
        static string ClipSheet()
        {
            string outDir = Path.Combine(Dir, "clipsheet");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            var sword = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/Greatsword.prefab");
            var clips = new List<AnimationClip>();
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { LSBuilder.Root + "/Art/MixamoRR", LSBuilder.Root + "/Art/Mixamo" }))
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview__") && !c.isLooping && !c.name.Contains("Run") && !c.name.Contains("Walk") && !c.name.Contains("Strafe") && !c.name.Contains("Idle"))
                        clips.Add(c);
            float[] times = { 0.1f, 0.3f, 0.42f, 0.52f, 0.62f, 0.8f };
            const int cw = 220, ch = 240;
            var rt = new RenderTexture(cw, ch, 24); var tile = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            var scene = EditorSceneManager.NewPreviewScene();
            var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            var go = (GameObject)Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            var anim = go.GetComponentInChildren<Animator>();
            anim.runtimeAnimatorController = null;
            var r = anim.GetBoneTransform(HumanBodyBones.RightHand); var l = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var idle = AssetDatabase.LoadAllAssetsAtPath(LSBuilder.Root + "/Art/Mixamo/GS_Idle.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__"));
            if (idle) idle.SampleAnimation(anim.gameObject, 0f);
            if (sword && r && l)
            {
                var sw = (GameObject)Object.Instantiate(sword, r);
                Vector3 dir = r.InverseTransformDirection((r.position - l.position).normalized);
                sw.transform.localRotation = Quaternion.LookRotation(dir, Vector3.right);
                sw.transform.localPosition = new Vector3(0, 0.07f, 0);
            }
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bar, scene);
            bar.transform.position = new Vector3(0, 0.02f, 2.2f); bar.transform.localScale = new Vector3(0.06f, 0.04f, 3f);
            var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f); cam.fieldOfView = 40; cam.targetTexture = rt;
            foreach (var clip in clips)
                for (int k = 0; k < times.Length; k++)
                {
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clip.SampleAnimation(anim.gameObject, times[k] * clip.length);
                    anim.transform.localPosition = Vector3.zero; anim.transform.localRotation = Quaternion.identity;
                    for (int view = 0; view < 2; view++)
                    {
                        if (view == 0) { cam.transform.position = new Vector3(0, 6.5f, -4.5f); cam.transform.rotation = Quaternion.Euler(55, 0, 0); }
                        else { cam.transform.position = new Vector3(5.5f, 1.2f, 0.4f); cam.transform.rotation = Quaternion.Euler(6, -90, 0); }
                        cam.Render();
                        RenderTexture.active = rt; tile.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{clip.name}__{(view == 0 ? "game" : "side")}_{k}.png"), tile.EncodeToPNG());
                    }
                }
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tile);
            EditorSceneManager.ClosePreviewScene(scene);
            return $"clipsheet: {clips.Count} clips: " + string.Join(", ", clips.Select(c => c.name));
        }

        static string VfxFlat(string folder)
        {
            string outDir = Path.Combine(Dir, "vfxflat");
            Directory.CreateDirectory(outDir);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            const int TW = 360, TH = 300;
            var rt = new RenderTexture(TW, TH, 24); var tex = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            int n = 0;
            var rots = new[] { ("xm90", new Vector3(-90, 0, 0)), ("xp90", new Vector3(90, 0, 0)) };
            var times = new[] { 0.08f, 0.2f };
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains("/Demo/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab.GetComponentInChildren<ParticleSystem>(true)) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                foreach (var (rtag, rot) in rots)
                    foreach (var time in times)
                    {
                        var scene = EditorSceneManager.NewPreviewScene();
                        void Put(GameObject o) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene);
                        var go = (GameObject)Object.Instantiate(prefab); Put(go);
                        go.transform.position = new Vector3(0, 1, 0);
                        go.transform.rotation = Quaternion.Euler(rot);
                        // fit: about 4.5 m across like the hit sector
                        var b = new Bounds(go.transform.position, Vector3.zero);
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var pr = ps.GetComponent<ParticleSystemRenderer>();
                            if (pr && pr.renderMode == ParticleSystemRenderMode.Mesh) pr.alignment = ParticleSystemRenderSpace.Local;
                            var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                        }
                        go.transform.localScale = Vector3.one * 3.5f;
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(time, false, true);
                        if (player)
                        {
                            var p = (GameObject)Object.Instantiate(player); Put(p);
                            foreach (var mb in p.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                            p.transform.localScale = Vector3.one * 1.15f;
                        }
                        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Put(ground); ground.transform.localScale = Vector3.one * 3f;
                        var aimBar = GameObject.CreatePrimitive(PrimitiveType.Cube); Put(aimBar);
                        aimBar.transform.position = new Vector3(0, 0.03f, 4f); aimBar.transform.localScale = new Vector3(0.08f, 0.05f, 2f);
                        var light = new GameObject("L").AddComponent<Light>(); Put(light.gameObject);
                        light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                        var camGo = new GameObject("Cam"); Put(camGo);
                        var c = camGo.AddComponent<Camera>(); c.scene = scene; c.clearFlags = CameraClearFlags.SolidColor;
                        c.backgroundColor = new Color(0.08f, 0.09f, 0.1f); c.fieldOfView = 50;
                        c.transform.position = new Vector3(0, 11f, -7.7f); c.transform.rotation = Quaternion.Euler(55, 0, 0);
                        c.targetTexture = rt; c.Render();
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); RenderTexture.active = null;
                        c.targetTexture = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{name}__{rtag}_{time:0.00}.png"), tex.EncodeToPNG());
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                n++;
            }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            return $"vfxflat {folder}: {n} prefabs";
        }

        // waterflat: copy Slash Water VFX to Generated/GS_SlashWater_Flat.prefab with every mesh particle on Local alignment and
        // camera billboards turned into ground (horizontal) billboards, set it on RoomRunConfig, then render the game's
        // orientation (aim +Z, -90 about X, scale 4.6) at t 0.2 from top / 55° game cam / ground-level side -> vfxflatcheck/.
        static string WaterFlat()
        {
            const string src = "Assets/Free Slash VFX/Prefabs/Slash Water VFX.prefab";
            string dst = LSBuilder.Root + "/Generated/GS_SlashWater_Flat.prefab";
            var log = new List<string>();
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(dst) && !AssetDatabase.CopyAsset(src, dst)) return "waterflat: copy failed";
            var root = PrefabUtility.LoadPrefabContents(dst);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                if (!pr) continue;
                string before = $"{pr.renderMode}/{pr.alignment}";
                if (ps.name == "Impact") { ps.gameObject.SetActive(false); log.Add("  Impact: stretched shards stand up -> off"); continue; }
                if (pr.renderMode == ParticleSystemRenderMode.Mesh) pr.alignment = ParticleSystemRenderSpace.Local;
                else if (pr.renderMode == ParticleSystemRenderMode.Billboard) pr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                log.Add($"  {ps.name}: {before} -> {pr.renderMode}/{pr.alignment}");
            }
            PrefabUtility.SaveAsPrefabAsset(root, dst);
            PrefabUtility.UnloadPrefabContents(root);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dst);

            var cfg = AssetDatabase.LoadAssetAtPath<RoomRun.RoomRunConfig>("Assets/Game/RoomRun/RoomRunConfig.asset");
            if (cfg)
            {
                var so = new SerializedObject(cfg);
                so.FindProperty("slashVfx").objectReferenceValue = prefab;
                so.FindProperty("slashVfxFinisher").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                log.Add("  RoomRunConfig slashVfx + finisher = " + dst);
            }

            // flatness check: original (pack, mesh->Local like the compare sheet) vs the copy, three views
            string outDir = Path.Combine(Dir, "vfxflatcheck");
            Directory.CreateDirectory(outDir);
            const int TW = 520, TH = 400;
            var rt = new RenderTexture(TW, TH, 24); var tex = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            var O = new Vector3(0, -2000, 0);   // away from the open scene's indirect-drawn foliage
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            foreach (var (tag, pf) in new[] { ("sheet", AssetDatabase.LoadAssetAtPath<GameObject>(src)), ("copy", prefab) })
                foreach (var time in new[] { 0.08f, 0.2f })
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    void Put(GameObject o) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene);
                    var go = (GameObject)Object.Instantiate(pf); Put(go);
                    go.transform.position = O + new Vector3(0, 1, 0);
                    go.transform.rotation = Quaternion.LookRotation(Vector3.forward) * Quaternion.Euler(0, 45, 0);   // as GreatswordCombat
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var pr = ps.GetComponent<ParticleSystemRenderer>();
                        
                        var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    }
                    go.transform.localScale = Vector3.one * 4.6f;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(time, false, true);
                    // per-system height of live particles (world y), to say plainly which parts stand up
                    var buf = new ParticleSystem.Particle[2000];
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        int k = ps.GetParticles(buf); if (k == 0) continue;
                        float lo = float.MaxValue, hi = float.MinValue, sz = 0;
                        bool world = ps.main.simulationSpace == ParticleSystemSimulationSpace.World;
                        for (int i = 0; i < k; i++)
                        {
                            var p = world ? buf[i].position : ps.transform.TransformPoint(buf[i].position);
                            lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); sz = Mathf.Max(sz, buf[i].GetCurrentSize(ps));
                        }
                        var pr = ps.GetComponent<ParticleSystemRenderer>();
                        log.Add($"  [{tag} t{time:0.00}] {ps.name}: {k} parts, centre y {lo:0.00}..{hi:0.00}, max size {sz:0.00}, {pr.renderMode}/{pr.alignment}");
                    }
                    if (player)
                    {
                        var p = (GameObject)Object.Instantiate(player); Put(p);
                        foreach (var mb in p.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                        p.transform.localScale = Vector3.one * 1.15f; p.transform.position = O;
                    }
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Put(ground); ground.transform.localScale = Vector3.one * 3f; ground.transform.position = O;
                    var aimBar = GameObject.CreatePrimitive(PrimitiveType.Cube); Put(aimBar);
                    aimBar.transform.position = O + new Vector3(0, 0.03f, 6f); aimBar.transform.localScale = new Vector3(0.08f, 0.05f, 2f);
                    var light = new GameObject("L").AddComponent<Light>(); Put(light.gameObject);
                    light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                    var camGo = new GameObject("Cam"); Put(camGo);
                    var c = camGo.AddComponent<Camera>(); c.scene = scene; c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.08f, 0.09f, 0.1f); c.fieldOfView = 50; c.targetTexture = rt;
                    var views = new[] { ("top", new Vector3(0, 16f, 1f), new Vector3(90, 0, 0)),
                                        ("game", new Vector3(0, 13f, -8.1f), new Vector3(55, 0, 0)),
                                        ("side", new Vector3(13f, 1.0f, 1.5f), new Vector3(0, -90, 0)) };
                    foreach (var (vtag, pos, eul) in views)
                    {
                        c.transform.SetPositionAndRotation(O + pos, Quaternion.Euler(eul));
                        c.Render();
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); RenderTexture.active = null;
                        File.WriteAllBytes(Path.Combine(outDir, $"{tag}_{time:0.00}_{vtag}.png"), tex.EncodeToPNG());
                    }
                    c.targetTexture = null;
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            return "waterflat:\n" + string.Join("\n", log);
        }

        // rotprobe <prefab> : the prefab under several root rotations at t 0.2 (scale 4.6), each renderer's world bounds
        // (y size small = lying flat) and a top-down + side render per rotation -> vfxflatcheck/rot_<i>_{top,side}.png
        static string RotProbe(string path)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!pf) return "rotprobe: no prefab " + path;
            var rots = new[] { new Vector3(0, 0, 0), new Vector3(-90, 0, 0), new Vector3(90, 0, 0), new Vector3(0, 0, 90), new Vector3(0, 0, -90),
                               new Vector3(0, 90, 0), new Vector3(-90, 90, 0), new Vector3(0, 90, 90), new Vector3(0, 90, -90), new Vector3(90, 90, 0) };
            string outDir = Path.Combine(Dir, "vfxflatcheck");
            Directory.CreateDirectory(outDir);
            const int TW = 320, TH = 320;
            var rt = new RenderTexture(TW, TH, 24); var tex = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            var log = new List<string>();
            for (int i = 0; i < rots.Length; i++)
            {
                var scene = EditorSceneManager.NewPreviewScene();
                void Put(GameObject o) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene);
                var go = (GameObject)Object.Instantiate(pf); Put(go);
                go.transform.position = new Vector3(0, 1, 0);
                go.transform.rotation = Quaternion.Euler(rots[i]);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                go.transform.localScale = Vector3.one * 4.6f;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(0.2f, false, true);
                var sb = new System.Text.StringBuilder($"rot {i} {rots[i]}:");
                foreach (var pr in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (pr.renderMode == ParticleSystemRenderMode.Mesh && pr.GetComponent<ParticleSystem>().particleCount > 0)
                    { var b = pr.bounds; sb.Append($" {pr.name} c{b.center:0.0} s{b.size:0.0};"); }
                log.Add(sb.ToString());
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube); Put(bar);
                bar.transform.position = new Vector3(0, 0.03f, 6f); bar.transform.localScale = new Vector3(0.15f, 0.05f, 2f);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Put(ground); ground.transform.localScale = Vector3.one * 3f;
                var light = new GameObject("L").AddComponent<Light>(); Put(light.gameObject);
                light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                var camGo = new GameObject("Cam"); Put(camGo);
                var c = camGo.AddComponent<Camera>(); c.scene = scene; c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = new Color(0.08f, 0.09f, 0.1f); c.fieldOfView = 50; c.targetTexture = rt;
                foreach (var (vtag, pos, eul) in new[] { ("top", new Vector3(0, 18f, 1f), new Vector3(90, 0, 0)), ("side", new Vector3(14f, 1.0f, 1f), new Vector3(0, -90, 0)) })
                {
                    c.transform.SetPositionAndRotation(pos, Quaternion.Euler(eul));
                    c.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(outDir, $"rot_{i}_{vtag}.png"), tex.EncodeToPNG());
                }
                c.targetTexture = null;
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            return "rotprobe " + path + "\n" + string.Join("\n", log);
        }

        // slashprev: the attached Slash Water copy and the GSSlashArc looks at several moments of one swing, 55° game camera,
        // dark ground, bloom on, next to the hunter -> slashprev/<name>_<k>.png (k = timing index)
        static string SlashPreview(bool post = true)
        {
            string outDir = Path.Combine(Dir, "slashprev");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            var water = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/GS_SlashWater_Flat.prefab");
            var noise = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Free Slash VFX/Textures/Noise.png");
            var streak = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Free Slash VFX/Textures/Slash Noise_Slash Noise.png");
            var spark = AssetDatabase.LoadAssetAtPath<Texture>("Assets/Free Slash VFX/Textures/Flare.png");
            float[] times = { 0.04f, 0.08f, 0.12f, 0.18f, 0.26f, 0.38f };
            var looks = new (string, RoomRun.GSSlashArc.Look?)[]
            {
                ("water", null), ("ice", RoomRun.GSSlashArc.Ice), ("ember", RoomRun.GSSlashArc.Ember),
                ("ghost", RoomRun.GSSlashArc.Ghost), ("violet", RoomRun.GSSlashArc.VioletRing),
            };
            const int TW = 420, TH = 360;
            var rt = new RenderTexture(TW, TH, 24, RenderTextureFormat.DefaultHDR);
            var ldr = new RenderTexture(TW, TH, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(TW, TH, TextureFormat.RGB24, false);
            var O = new Vector3(0, -2000, 0);
            int count = 0;
            foreach (var (name, look) in looks)
                for (int k = 0; k < times.Length; k++)
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    void Put(GameObject o) => UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(o, scene);
                    var aimRot = Quaternion.LookRotation(Vector3.forward);
                    if (look == null)
                    {
                        if (!water) continue;
                        var go = (GameObject)Object.Instantiate(water); Put(go);
                        go.transform.SetPositionAndRotation(O + Vector3.up, aimRot * Quaternion.Euler(0, 45, 0));
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; }
                        go.transform.localScale = Vector3.one * 4.6f;
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(times[k], false, true);
                    }
                    else
                    {
                        var go = new GameObject("slash"); Put(go);
                        go.transform.SetPositionAndRotation(O + Vector3.up * 0.25f, aimRot);
                        var arc = go.AddComponent<RoomRun.GSSlashArc>();
                        arc.look = look.Value; arc.noiseTex = noise; arc.streakTex = streak; arc.sparkTex = spark;
                        Random.InitState(7);
                        arc.Play(4.6f, false);
                        var ps = go.GetComponentInChildren<ParticleSystem>();
                        float step = 1f / 120f, tt = 0f;
                        while (tt + step <= times[k] + 1e-4f)
                        {
                            arc.Tick(step);
                            if (ps) ps.Simulate(step, true, false, false);
                            tt += step;
                        }
                    }
                    if (player)
                    {
                        var p = (GameObject)Object.Instantiate(player); Put(p);
                        foreach (var mb in p.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                        p.transform.position = O; p.transform.localScale = Vector3.one * 1.15f;
                    }
                    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); Put(ground);
                    ground.transform.position = O; ground.transform.localScale = Vector3.one * 4f;
                    // unlit dark floor: the open scene's ambient probe lights preview objects too (turned the floor cyan)
                    var gm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    gm.SetColor("_BaseColor", new Color(0.07f, 0.07f, 0.085f));
                    ground.GetComponent<Renderer>().sharedMaterial = gm;
                    var light = new GameObject("L").AddComponent<Light>(); Put(light.gameObject);
                    light.type = LightType.Directional; light.intensity = 0.7f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
                    var volGo = new GameObject("Vol"); Put(volGo); volGo.layer = 31;   // only this camera's volume, not the open scene's
                    var vol = volGo.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 1000;
                    var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                    var bloom = prof.Add<UnityEngine.Rendering.Universal.Bloom>(true);
                    bloom.threshold.Override(1.0f); bloom.intensity.Override(1.4f); bloom.scatter.Override(0.65f);
                    var tm = prof.Add<UnityEngine.Rendering.Universal.Tonemapping>(true);
                    tm.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.Neutral);
                    // neutralise whatever the project's default/scene volumes grade with (another thread's look turned frames cyan)
                    var ca = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                    ca.colorFilter.Override(Color.white); ca.postExposure.Override(0f); ca.contrast.Override(0f); ca.saturation.Override(0f); ca.hueShift.Override(0f);
                    var wb = prof.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true); wb.temperature.Override(0f); wb.tint.Override(0f);
                    var cm = prof.Add<UnityEngine.Rendering.Universal.ChannelMixer>(true);
                    cm.redOutRedIn.Override(100f); cm.redOutGreenIn.Override(0f); cm.redOutBlueIn.Override(0f);
                    cm.greenOutRedIn.Override(0f); cm.greenOutGreenIn.Override(100f); cm.greenOutBlueIn.Override(0f);
                    cm.blueOutRedIn.Override(0f); cm.blueOutGreenIn.Override(0f); cm.blueOutBlueIn.Override(100f);
                    var lgg = prof.Add<UnityEngine.Rendering.Universal.LiftGammaGain>(true);
                    lgg.lift.Override(new Vector4(1, 1, 1, 0)); lgg.gamma.Override(new Vector4(1, 1, 1, 0)); lgg.gain.Override(new Vector4(1, 1, 1, 0));
                    var smh = prof.Add<UnityEngine.Rendering.Universal.ShadowsMidtonesHighlights>(true);
                    smh.shadows.Override(new Vector4(1, 1, 1, 0)); smh.midtones.Override(new Vector4(1, 1, 1, 0)); smh.highlights.Override(new Vector4(1, 1, 1, 0));
                    var st = prof.Add<UnityEngine.Rendering.Universal.SplitToning>(true); st.shadows.Override(Color.grey); st.highlights.Override(Color.grey);
                    var vg = prof.Add<UnityEngine.Rendering.Universal.Vignette>(true); vg.intensity.Override(0f);
                    bloom.tint.Override(Color.white); bloom.dirtIntensity.Override(0f);
                    vol.sharedProfile = prof;
                    var camGo = new GameObject("Cam"); Put(camGo);
                    var c = camGo.AddComponent<Camera>(); c.scene = scene; c.clearFlags = CameraClearFlags.SolidColor;
                    c.backgroundColor = new Color(0.05f, 0.05f, 0.06f); c.fieldOfView = 50; c.allowHDR = true;
                    var cd = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                    cd.renderPostProcessing = post; cd.volumeLayerMask = 1 << 31;
                    c.transform.SetPositionAndRotation(O + new Vector3(0, 11.5f, -8.0f), Quaternion.Euler(55, 0, 0));
                    // the open scene's fog/ambient apply to preview cameras too: neutral for the shot, then restore
                    bool fog = RenderSettings.fog; var amb = RenderSettings.ambientLight; var ambMode = RenderSettings.ambientMode;
                    RenderSettings.fog = false; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = new Color(0.25f, 0.25f, 0.28f);
                    c.targetTexture = rt; c.Render();
                    RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = amb;
                    Graphics.Blit(rt, ldr);
                    RenderTexture.active = ldr; tex.ReadPixels(new Rect(0, 0, TW, TH), 0, 0); RenderTexture.active = null;
                    c.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(outDir, $"{name}_{k}.png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(prof); Object.DestroyImmediate(gm);
                    EditorSceneManager.ClosePreviewScene(scene);
                    count++;
                }
            Object.DestroyImmediate(rt); Object.DestroyImmediate(ldr); Object.DestroyImmediate(tex);
            return $"slashprev: {count} tiles to {outDir} (t = {string.Join(", ", times)})";
        }

        // cutpieces: the top part of each reed/susuki model (above 40 % of its height), split into connected stalks/leaves,
        // pivot at the cut -> Assets/Game/RoomRun/Resources/GS_CutPieces.asset (RoomRun.CutPieceSet), for flying cut reeds.
        static string CutPieces()
        {
            const float cut = 0.4f;
            string[] files = { "RF_Reed_A", "RF_Reed_B", "RF_Susuki_A", "RF_Susuki_B", "RF_Susuki_C", "RF_Susuki_Broken" };
            const string outPath = "Assets/Game/RoomRun/Resources/GS_CutPieces.asset";
            var set = ScriptableObject.CreateInstance<RoomRun.CutPieceSet>();
            var entries = new List<RoomRun.CutPieceSet.Entry>();
            var allPieces = new List<Mesh>();
            var log = new List<string>();
            foreach (var f in files)
                foreach (var src in AssetDatabase.LoadAllAssetsAtPath($"Assets/Game/ReedField/Models/{f}.fbx").OfType<Mesh>())
                {
                    var v = src.vertices; var nrm = src.normals; var t = src.triangles;
                    var uv0 = new List<Vector2>(); src.GetUVs(0, uv0);
                    var uv1 = new List<Vector2>(); src.GetUVs(1, uv1);
                    var col = src.colors;
                    if (v.Length == 0 || uv1.Count != v.Length) { log.Add($"  {src.name}: unreadable or no height data ({v.Length} verts, uv1 {uv1.Count})"); continue; }
                    // connected components over shared vertices (and identical positions)
                    var parent = new int[v.Length];
                    for (int i = 0; i < parent.Length; i++) parent[i] = i;
                    int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                    void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
                    for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i + 1], t[i + 2]); }
                    var byPos = new Dictionary<Vector3, int>();
                    for (int i = 0; i < v.Length; i++) { if (byPos.TryGetValue(v[i], out int o)) Union(i, o); else byPos[v[i]] = i; }
                    var comps = new Dictionary<int, List<int>>();   // root -> triangle starts kept
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        if (uv1[t[i]].x < cut || uv1[t[i + 1]].x < cut || uv1[t[i + 2]].x < cut) continue;
                        int r = Find(t[i]);
                        if (!comps.TryGetValue(r, out var l)) comps[r] = l = new List<int>();
                        l.Add(i);
                    }
                    var pieces = new List<Mesh>();
                    foreach (var kv in comps.OrderByDescending(k => k.Value.Count).Take(14))
                    {
                        if (kv.Value.Count < 2) continue;
                        var map = new Dictionary<int, int>();
                        var pv = new List<Vector3>(); var pn = new List<Vector3>(); var p0 = new List<Vector2>(); var p1 = new List<Vector2>();
                        var pc = new List<Color>(); var pt = new List<int>();
                        foreach (int ti in kv.Value)
                            for (int k = 0; k < 3; k++)
                            {
                                int s = t[ti + k];
                                if (!map.TryGetValue(s, out int d))
                                {
                                    d = pv.Count; map[s] = d;
                                    pv.Add(v[s]); pn.Add(nrm.Length > s ? nrm[s] : Vector3.up); p0.Add(uv0.Count > s ? uv0[s] : Vector2.zero);
                                    p1.Add(uv1[s]); pc.Add(col.Length > s ? col[s] : Color.white);
                                }
                                pt.Add(d);
                            }
                        // pivot at the cut: the lowest point of the piece
                        var low = pv[0]; foreach (var p in pv) if (p.y < low.y) low = p;
                        for (int i = 0; i < pv.Count; i++) pv[i] -= low;
                        var m = new Mesh { name = $"{src.name}_cut{pieces.Count}" };
                        m.SetVertices(pv); m.SetNormals(pn); m.SetUVs(0, p0); m.SetUVs(1, p1); m.SetColors(pc); m.SetTriangles(pt, 0);
                        m.RecalculateBounds();
                        pieces.Add(m); allPieces.Add(m);
                    }
                    entries.Add(new RoomRun.CutPieceSet.Entry { source = src.name, pieces = pieces.ToArray() });
                    log.Add($"  {src.name}: {pieces.Count} pieces");
                }
            set.entries = entries.ToArray();
            AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(set, outPath);
            foreach (var m in allPieces) AssetDatabase.AddObjectToAsset(m, set);
            AssetDatabase.SaveAssets();
            return "cutpieces -> " + outPath + "\n" + string.Join("\n", log);
        }

        static string PoseTest(string[] states)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            var sword = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/Greatsword.prefab");
            if (!prefab) return "posetest: no prefab";
            float[] times = { 0.15f, 0.3f, 0.4f, 0.5f, 0.6f, 0.8f };
            const int cw = 220, ch = 260;
            int W = cw * times.Length, H = ch * states.Length * 2;
            var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
            var scene = EditorSceneManager.NewPreviewScene();
            var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            var go = (GameObject)Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            var anim = go.GetComponentInChildren<Animator>();
            anim.Rebind();
            anim.Update(0f);
            // sword like LSRig's auto grip, from the idle pose
            var r = anim.GetBoneTransform(HumanBodyBones.RightHand); var l = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            if (sword && r && l)
            {
                var sw = (GameObject)Object.Instantiate(sword, r);
                Vector3 dir = r.InverseTransformDirection((r.position - l.position).normalized);
                sw.transform.localRotation = Quaternion.LookRotation(dir, Vector3.right);
                sw.transform.localPosition = new Vector3(0, 0.07f, 0);
            }
            // aim marker: a long thin bar along +Z (the cursor direction)
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bar, scene);
            bar.transform.position = new Vector3(0, 0.02f, 2.2f); bar.transform.localScale = new Vector3(0.06f, 0.04f, 3f);
            var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f); cam.fieldOfView = 40;
            var rt = new RenderTexture(cw, ch, 24); cam.targetTexture = rt;
            var tile = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            int layer = anim.GetLayerIndex("AttackFull");
            for (int si = 0; si < states.Length; si++)
                for (int ti = 0; ti < times.Length; ti++)
                {
                    anim.Rebind();
                    anim.SetLayerWeight(layer, 1f);
                    anim.Play(states[si], layer, times[ti]);
                    anim.Update(0f);
                    for (int view = 0; view < 2; view++)
                    {
                        if (view == 0) { cam.transform.position = new Vector3(0, 6.5f, -4.5f); cam.transform.rotation = Quaternion.Euler(55, 0, 0); }
                        else { cam.transform.position = new Vector3(5.5f, 1.4f, 0.5f); cam.transform.rotation = Quaternion.Euler(8, -90, 0); }
                        cam.Render();
                        RenderTexture.active = rt; tile.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); RenderTexture.active = null;
                        int row = si * 2 + view;
                        sheet.SetPixels(ti * cw, H - (row + 1) * ch, cw, ch, tile.GetPixels());
                    }
                }
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tile);
            EditorSceneManager.ClosePreviewScene(scene);
            string outp = Path.Combine(Dir, "posetest.png");
            File.WriteAllBytes(outp, sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
            return "posetest saved " + outp + " rows: " + string.Join(",", states) + " (game angle, then side; bar = aim)";
        }

        // Head + hat as a ring stack in the head frame (metres at character scale 1): verts weighted mostly to the head,
        // binned by height; each boundary takes the wider of its two bins so interpolation never undercuts the mesh.
        public static string MeasureHat(GameObject go, Animator anim, LSPoseFix fix)
        {
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var pts = new List<Vector3>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = smr.sharedMesh; if (!mesh) continue;
                int hi = System.Array.IndexOf(smr.bones, head); if (hi < 0) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, true);
                var bw = mesh.boneWeights; var v = baked.vertices;
                for (int i = 0; i < v.Length && i < bw.Length; i++)
                {
                    float w = (bw[i].boneIndex0 == hi ? bw[i].weight0 : 0) + (bw[i].boneIndex1 == hi ? bw[i].weight1 : 0);
                    if (w >= 0.5f) pts.Add(Quaternion.Inverse(head.rotation) * (smr.transform.TransformPoint(v[i]) - head.position) / go.transform.lossyScale.y);
                }
                Object.DestroyImmediate(baked);
            }
            if (pts.Count == 0) return "hat: no head verts";
            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            var cxz = new Vector2((mn.x + mx.x) * 0.5f, (mn.z + mx.z) * 0.5f);
            const int nb = 12;
            var binR = new float[nb];
            foreach (var p in pts)
            {
                int b = Mathf.Clamp((int)((p.y - mn.y) / (mx.y - mn.y) * nb), 0, nb - 1);
                binR[b] = Mathf.Max(binR[b], new Vector2(p.x - cxz.x, p.z - cxz.y).magnitude);
            }
            var ys = new float[nb + 1]; var rs = new float[nb + 1];
            for (int k = 0; k <= nb; k++)
            {
                ys[k] = Mathf.Lerp(mn.y, mx.y, k / (float)nb);
                rs[k] = Mathf.Max(k > 0 ? binR[k - 1] : 0f, k < nb ? binR[k] : 0f);
            }
            fix.hatCenterXZ = cxz; fix.hatYs = ys; fix.hatRs = rs;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return $"hat: {pts.Count} verts, centre xz {cxz.x:0.000},{cxz.y:0.000}, profile y:r " +
                   string.Join(" ", ys.Select((y, k) => string.Format(ci, "{0:0.00}:{1:0.00}", y, rs[k])));
        }

        // idletest: base-layer poses (idle, walk, runs) without / with LSPoseFix, 55° game camera from behind and from the
        // front, plus a side view, to idletest.png. Logs spine lean and the blade-to-hat gap per pose.
        static string IdleTest()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab");
            var swordPf = AssetDatabase.LoadAssetAtPath<GameObject>(LSBuilder.Root + "/Generated/Greatsword.prefab");
            if (!prefab || !swordPf) return "idletest: no prefab";
            var log = new List<string>();
            var poses = new (string name, float x, float z, float t)[] {
                ("idle", 0, 0, 0.0f), ("idle", 0, 0, 0.5f), ("walkF", 0, 0.35f, 0.3f), ("runF", 0, 1, 0.25f), ("runF", 0, 1, 0.75f),
                ("runB", 0, -1, 0.3f), ("runL", -1, 0, 0.3f), ("runR", 1, 0, 0.3f) };
            const int cw = 180, ch = 220, views = 3;
            int W = cw * poses.Length, H = ch * views * 2;
            var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
            var scene = EditorSceneManager.NewPreviewScene();
            var light = new GameObject("L").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light.gameObject, scene);
            var go = (GameObject)Object.Instantiate(prefab);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            var anim = go.GetComponentInChildren<Animator>();
            var fix = go.GetComponent<LSPoseFix>() ?? go.AddComponent<LSPoseFix>();
            fix.anim = anim;
            var rH = anim.GetBoneTransform(HumanBodyBones.RightHand); var lH = anim.GetBoneTransform(HumanBodyBones.LeftHand);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var neck = anim.GetBoneTransform(HumanBodyBones.Neck) ?? anim.GetBoneTransform(HumanBodyBones.Head);

            void Pose((string name, float x, float z, float t) ps)
            {
                anim.Rebind();
                anim.SetFloat("MoveX", ps.x); anim.SetFloat("MoveZ", ps.z);
                anim.Play("Locomotion", 0, ps.t); anim.Update(0f);
            }
            Pose(poses[0]);
            log.Add(MeasureHat(go, anim, fix));
            log.Add($"upright chain {fix.ChainLength} bones, idle {fix.uprightIdleDeg} run {fix.uprightRunDeg}");
            // LSRig's auto grip, from the idle pose
            var gripRot = Quaternion.LookRotation(rH.InverseTransformDirection((rH.position - lH.position).normalized), Vector3.right);
            var sw = (GameObject)Object.Instantiate(swordPf, rH); sw.name = "Greatsword";

            var camGo = new GameObject("Cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f); cam.fieldOfView = 30;
            var rt = new RenderTexture(cw, ch, 24); cam.targetTexture = rt;
            var tile = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            for (int pi = 0; pi < poses.Length; pi++)
                for (int pass = 0; pass < 2; pass++)
                {
                    var ps = poses[pi];
                    Pose(ps);
                    sw.transform.localPosition = new Vector3(0, 0.07f, 0); sw.transform.localRotation = gripRot;
                    float turn = pass == 1 ? fix.PreviewApply(new Vector2(ps.x, ps.z).magnitude) : fix.PreviewApply(0f, false, false);
                    Vector3 up = go.transform.InverseTransformDirection(neck.position - hips.position);
                    float lean = Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg;
                    log.Add($"{ps.name}@{ps.t:0.00} {(pass == 0 ? "orig " : "fixed")}: lean {lean:0.0} deg fwd, blade-hat gap {fix.MinHatGap(sw.transform):0.000} m, sword turn {turn:0.0}");
                    for (int view = 0; view < views; view++)
                    {
                        if (view == 0) { cam.transform.position = new Vector3(0, 1.0f + 4.1f, -2.9f); cam.transform.rotation = Quaternion.Euler(55, 0, 0); }
                        else if (view == 1) { cam.transform.position = new Vector3(0, 1.0f + 4.1f, 2.9f); cam.transform.rotation = Quaternion.Euler(55, 180, 0); }
                        else { cam.transform.position = new Vector3(4.6f, 1.1f, 0.1f); cam.transform.rotation = Quaternion.Euler(2, -90, 0); }
                        cam.Render();
                        RenderTexture.active = rt; tile.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); RenderTexture.active = null;
                        int row = view * 2 + pass;
                        sheet.SetPixels(pi * cw, H - (row + 1) * ch, cw, ch, tile.GetPixels());
                    }
                }
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tile);
            EditorSceneManager.ClosePreviewScene(scene);
            string outp = Path.Combine(Dir, "idletest.png");
            File.WriteAllBytes(outp, sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
            log.Add("idletest saved " + outp + " (row pairs orig/fixed: game cam behind, game cam front, side)");
            return string.Join("\n", log);
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            string state = SessionState.GetString(KeyState, "");

            if (state == "demo" && EditorApplication.isPlaying)
            {
                if (demoStart == 0) demoStart = EditorApplication.timeSinceStartup;
                if (File.Exists(Done) || EditorApplication.timeSinceStartup - demoStart > 90)
                {
                    Note(File.Exists(Done) ? "demo done" : "demo timeout");
                    SessionState.SetString(KeyState, "restore");
                    demoStart = 0;
                    EditorApplication.isPlaying = false;
                    RestoreStartScene();
                }
                return;
            }
            // After the demo, reopen the scene that was open before it.
            if (state == "restore")
            {
                if (!Gate.Settled()) return;   // 11-14: opening a scene right as Play exits froze the editor
                SessionState.SetString(KeyState, "");
                string prev = SessionState.GetString("LocoSplit.PrevScene", "");
                if (prev != "" && prev != LSBuilder.ScenePath && File.Exists(prev)) { EditorSceneManager.OpenScene(prev); Note("reopened " + prev); }
                return;
            }
            // Another thread's import/reload can cancel the play request: ask again once the editor is idle.
            if (state == "" && !EditorApplication.isPlayingOrWillChangePlaymode) Gate.Release("lsr");
            if (state == "demo" && Gate.Ready("lsr") && !File.Exists(Cmd))
            {
                if (EditorApplication.timeSinceStartup - retryAt < 4) return;
                retryAt = EditorApplication.timeSinceStartup;
                if (EditorSceneManager.GetActiveScene().path != LSBuilder.ScenePath) { Note("demo: scene changed, giving up"); SessionState.SetString(KeyState, ""); RestoreStartScene(); return; }
                if (!File.Exists(Path.Combine(Dir, "demo_request.txt"))) File.WriteAllText(Path.Combine(Dir, "demo_request.txt"), "1");
                EditorSceneManager.playModeStartScene = null;
                Note("demo: play request was dropped, retrying");
                EditorApplication.isPlaying = true;
                return;
            }

            if (!File.Exists(Cmd)) return;
            string cmd = File.ReadAllText(Cmd).Trim();
            if (cmd == "stop")
            {
                File.Delete(Cmd);
                SessionState.SetString(KeyState, "");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                RestoreStartScene();
                Note("stop");
                return;
            }
            if (!Gate.Ready("lsr")) return;
            File.Delete(Cmd);
            Gate.Take("lsr");
            Note("got " + cmd);
            Debug.Log("[LocoSplit] command " + cmd);

            if (cmd == "build" || cmd == "build2")   // build2: only understood once this version is compiled
            {
                try { Note(LSBuilder.Build()); }
                catch (System.Exception e) { Note("build failed: " + e); }
            }
            else if (cmd.StartsWith("reimport "))
            {
                string p = cmd.Substring(9).Trim();
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
                Note("reimported " + p);
            }
            else if (cmd.StartsWith("vfxtest"))
            {
                // vfxtest <prefab path> : the prefab at 4 rotations in a preview scene, top-down, to ArtSource/LocoSplit/vfxtest.png
                try { Note(VfxTest(cmd.Length > 8 ? cmd.Substring(8).Trim() : "Assets/Free Slash VFX/Prefabs/Slash Eletric VFX.prefab")); }
                catch (System.Exception e) { Note("vfxtest failed: " + e); }
            }
            else if (cmd.StartsWith("vfxflat "))
            {
                // vfxflat <folder> : each prefab with render alignment Local (instance only), laid flat two ways,
                // next to the RR greatsword player, 55° game camera, two timings -> ArtSource/LocoSplit/vfxflat/
                try { Note(VfxFlat(cmd.Substring(8).Trim())); } catch (System.Exception e) { Note("vfxflat failed: " + e); }
            }
            else if (cmd.StartsWith("rotprobe"))
            {
                try { Note(RotProbe(cmd.Length > 9 ? cmd.Substring(9).Trim() : LSBuilder.Root + "/Generated/GS_SlashWater_Flat.prefab")); }
                catch (System.Exception e) { Note("rotprobe failed: " + e); }
            }
            else if (cmd.StartsWith("slashprev"))
            {
                try { Note(SlashPreview(!cmd.Contains("nopost"))); } catch (System.Exception e) { Note("slashprev failed: " + e); }
            }
            else if (cmd == "cutpieces")
            {
                try { Note(CutPieces()); } catch (System.Exception e) { Note("cutpieces failed: " + e); }
            }
            else if (cmd == "waterflat")
            {
                try { Note(WaterFlat()); } catch (System.Exception e) { Note("waterflat failed: " + e); }
            }
            else if (cmd == "vfxall")
            {
                try { Note(VfxAll()); } catch (System.Exception e) { Note("vfxall failed: " + e); }
            }
            else if (cmd == "clipsheet")
            {
                try { Note(ClipSheet()); } catch (System.Exception e) { Note("clipsheet failed: " + e); }
            }
            else if (cmd.StartsWith("posetest"))
            {
                // posetest [state ...]: RR_GreatswordPlayer posed through each attack state, game angle + side, to posetest.png
                try { Note(PoseTest(cmd.Length > 9 ? cmd.Substring(9).Trim().Split(' ') : new[] { "SweepA", "SweepB", "SweepC", "Spin" })); }
                catch (System.Exception e) { Note("posetest failed: " + e); }
            }
            else if (cmd.StartsWith("posefix"))
            {
                // posefix [idleDeg runDeg]: add/tune LSPoseFix on RR_GreatswordPlayer.prefab, hat profile measured from the mesh
                try
                {
                    string pp = LSBuilder.Root + "/Generated/RR_GreatswordPlayer.prefab";
                    // measure on a throwaway copy so the prefab's bones are never saved posed
                    var tmp = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(pp));
                    tmp.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var mb in tmp.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
                    var tAnim = tmp.GetComponentInChildren<Animator>();
                    var tFix = tmp.GetComponent<LSPoseFix>() ?? tmp.AddComponent<LSPoseFix>();
                    tAnim.Rebind(); tAnim.Play("Locomotion", 0, 0f); tAnim.Update(0f);
                    string hat = MeasureHat(tmp, tAnim, tFix);
                    var cxz = tFix.hatCenterXZ; var ys = tFix.hatYs; var rs = tFix.hatRs;
                    Object.DestroyImmediate(tmp);

                    var root = PrefabUtility.LoadPrefabContents(pp);
                    var fix = root.GetComponent<LSPoseFix>() ?? root.AddComponent<LSPoseFix>();
                    fix.anim = root.GetComponentInChildren<Animator>();
                    fix.hatCenterXZ = cxz; fix.hatYs = ys; fix.hatRs = rs;
                    var a = cmd.Split(' ').Skip(1).Where(s => s != "").Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    if (a.Length >= 2) { fix.uprightIdleDeg = a[0]; fix.uprightRunDeg = a[1]; }
                    PrefabUtility.SaveAsPrefabAsset(root, pp);
                    PrefabUtility.UnloadPrefabContents(root);
                    Note($"posefix: idle {fix.uprightIdleDeg} run {fix.uprightRunDeg}; {hat}");
                }
                catch (System.Exception e) { Note("posefix failed: " + e); }
            }
            else if (cmd == "idletest")
            {
                try { Note(IdleTest()); } catch (System.Exception e) { Note("idletest failed: " + e); }
            }
            else if (cmd == "rrplayer")
            {
                try { Note(LSBuilder.BuildRoomRunPlayer()); }
                catch (System.Exception e) { Note("rrplayer failed: " + e); }
            }
            else if (cmd == "demo")
            {
                SessionState.SetString("LocoSplit.PrevScene", EditorSceneManager.GetActiveScene().path);
                if (EditorSceneManager.GetActiveScene().path != LSBuilder.ScenePath)
                {
                    if (EditorSceneManager.GetActiveScene().isDirty) { Note("demo: open scene has unsaved changes, not switching"); return; }
                    EditorSceneManager.OpenScene(LSBuilder.ScenePath);
                }
                if (File.Exists(Done)) File.Delete(Done);
                File.WriteAllText(Path.Combine(Dir, "demo_request.txt"), "1");
                SessionState.SetString(KeyState, "demo");
                demoStart = 0;
                Note("demo: entering play mode");
                // RoomRunPlayStart makes Play boot the room run; play this scene alone, restore it afterwards.
                var start = EditorSceneManager.playModeStartScene;
                SessionState.SetString("LocoSplit.StartScene", start ? AssetDatabase.GetAssetPath(start) : "");
                EditorSceneManager.playModeStartScene = null;
                EditorApplication.isPlaying = true;
            }
            else if (cmd.StartsWith("open "))
            {
                string p = cmd.Substring(5).Trim();
                if (EditorSceneManager.GetActiveScene().isDirty) Note("open: current scene has unsaved changes");
                else { EditorSceneManager.OpenScene(p); Note("opened " + p); }
            }
            else Note("unknown command " + cmd);
        }
    }
}
