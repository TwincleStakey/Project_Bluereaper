// File-driven helper for the water FX work (no menus): write ArtSource/wfx_cmd.txt with one line, result in ArtSource/wfx_log.txt.
//   methods                - list the Package Manager's Asset Store download service members
//   download <productId>   - download a package you own from the Asset Store into the local cache
//   import <path>          - import a .unitypackage without the dialog
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class WaterFXRemote
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
    static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../ArtSource"));
    static string Cmd => Path.Combine(Dir, "wfx_cmd.txt");
    static string Log => Path.Combine(Dir, "wfx_log.txt");

    static WaterFXRemote() { EditorApplication.update += Tick; }

    static void Note(string s) { File.AppendAllText(Log, DateTime.Now.ToString("HH:mm:ss ") + s + "\n"); }

    const string KeyWade = "WaterFX.wade", KeyWadeT = "WaterFX.wadeT", KeyRestore = "WaterFX.restore";

    // reopen the scene that was open before a wade capture
    static void Restore()
    {
        var r = SessionState.GetString(KeyRestore, "");
        if (r == "") return;
        SessionState.EraseString(KeyRestore);
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(r, UnityEditor.SceneManagement.OpenSceneMode.Single);
        Note("restored scene " + r);
    }
    static double next;
    static bool wadeRunning;
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.5;
        var wade = SessionState.GetString(KeyWade, "");
        if (wade != "")
        {
            if (!EditorApplication.isPlaying) { if (!EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.EraseString(KeyWade); Restore(); } return; }
            // never hold play mode longer than a minute
            if (EditorApplication.timeSinceStartup - SessionState.GetFloat(KeyWadeT, 0) > 60) { Note("wade timeout, leaving play mode"); SessionState.EraseString(KeyWade); EditorApplication.isPlaying = false; return; }
            if (wadeRunning) return;
            var run = RoomRun.RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null) return;
            wadeRunning = true;
            run.StartCoroutine(Wade(run, wade));
            return;
        }
        if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode) Restore();
        if (!File.Exists(Cmd) || !Gate.Ready("wfx")) return;
        var line = File.ReadAllText(Cmd).Trim();
        File.Delete(Cmd);
        try { Run(line); } catch (Exception e) { Note("ERR " + e); }
    }

    static object Service(string iface)
    {
        var asm = typeof(EditorWindow).Assembly;
        var all = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).ToArray();
        var sc = all.First(t => t.Name == "ServicesContainer");
        var it = all.First(t => t.Name == iface && t.IsInterface);
        var inst = sc.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
                   ?? sc.BaseType?.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
        var resolve = (sc.GetMethod("Resolve", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                       ?? sc.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "Resolve" && m.IsGenericMethodDefinition));
        return resolve.MakeGenericMethod(it).Invoke(inst, null);
    }

    // Renders a splash prefab in an isolated preview scene over dark water-ish ground at several times, side by side.
    static void Preview(string prefabPath, string outPng, float dist, float pitch)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (!prefab) { Note("no prefab " + prefabPath); return; }
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.localScale = Vector3.one * 3f;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.color = new Color(0.09f, 0.11f, 0.11f); gm.SetFloat("_Smoothness", 0.85f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            var lgo = new GameObject("sun"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lgo, scene);
            var l = lgo.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f; l.color = new Color(0.85f, 0.88f, 0.9f); lgo.transform.rotation = Quaternion.Euler(50, -30, 0);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.transform.position = Vector3.zero;
            var cgo = new GameObject("cam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cgo, scene);
            var cam = cgo.AddComponent<Camera>(); cam.scene = scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.06f, 0.06f); cam.fieldOfView = 35f;
            cam.cameraType = CameraType.Game;
            cgo.transform.rotation = Quaternion.Euler(pitch, 0, 0);
            cgo.transform.position = cgo.transform.rotation * new Vector3(0, 0, -dist) + Vector3.up * 0.3f;
            var systems = inst.GetComponentsInChildren<ParticleSystem>(true);
            float[] times = { 0.08f, 0.2f, 0.4f, 0.7f, 1.1f };
            int w = 360, h = 300;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
            var strip = new Texture2D(w * times.Length, h, TextureFormat.RGB24, false);
            var tmp = new Texture2D(w, h, TextureFormat.RGB24, false);
            cam.targetTexture = rt;
            for (int i = 0; i < times.Length; i++)
            {
                foreach (var ps in systems) { ps.useAutoRandomSeed = false; ps.randomSeed = 7; }
                var root = inst.GetComponent<ParticleSystem>();
                if (root) root.Simulate(times[i], true, true, true);
                else foreach (var ps in systems) ps.Simulate(times[i], false, true, true);
                cam.Render();
                RenderTexture.active = rt; tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0); tmp.Apply();
                strip.SetPixels(i * w, 0, w, h, tmp.GetPixels());
            }
            RenderTexture.active = null; strip.Apply();
            File.WriteAllBytes(outPng, strip.EncodeToPNG());
            cam.targetTexture = null; rt.Release();
            Note("preview " + prefabPath + " -> " + outPng);
        }
        finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
    }

    // ---- footstep splash prefab, built from the Namu pack's Water_Splash_A ----
    const string Src = "Assets/NamuFX/StylizedWaterEffects/Prefabs/Water_Splash_A.prefab";
    const string Dst = "Assets/Game/WaterFX/Resources/WaterFX_Splash.prefab";
    // look: murky stream water under an overcast dark-fantasy sky - grey-green, little glow
    static float Desat = 0.55f, Bright = 1.1f, Alpha = 0.95f, Scale = 1.0f;
    static Color Hue = new Color(0.86f, 0.95f, 0.93f);
    static readonly string[] Drop = { "water_ring", "ripples" };

    static Color MuteC(Color c)
    {
        float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        var d = Color.Lerp(c, new Color(g, g, g), Desat) * Bright;
        return new Color(d.r * Hue.r, d.g * Hue.g, d.b * Hue.b, c.a * Alpha);
    }
    static Gradient MuteG(Gradient s)
    {
        var k = s.colorKeys; for (int i = 0; i < k.Length; i++) { var c = MuteC(k[i].color); k[i].color = new Color(c.r, c.g, c.b, 1); }
        var a = s.alphaKeys; for (int i = 0; i < a.Length; i++) a[i].alpha *= Alpha;
        var g = new Gradient { mode = s.mode }; g.SetKeys(k, a); return g;
    }
    static ParticleSystem.MinMaxGradient Mute(ParticleSystem.MinMaxGradient g)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(MuteC(g.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(MuteC(g.colorMin), MuteC(g.colorMax));
            case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(MuteG(g.gradient));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(MuteG(g.gradientMin), MuteG(g.gradientMax));
            case ParticleSystemGradientMode.RandomColor: return new ParticleSystem.MinMaxGradient(MuteG(g.gradient)) { mode = ParticleSystemGradientMode.RandomColor };
        }
        return g;
    }

    static void Make()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Dst)));
        AssetDatabase.DeleteAsset(Dst);
        if (!AssetDatabase.CopyAsset(Src, Dst)) { Note("copy failed"); return; }
        var root = PrefabUtility.LoadPrefabContents(Dst);
        try
        {
            foreach (var n in Drop) foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == n).ToArray()) UnityEngine.Object.DestroyImmediate(t.gameObject);
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var m = ps.main; m.startColor = Mute(m.startColor); m.playOnAwake = false; m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var col = ps.colorOverLifetime; if (col.enabled) col.color = Mute(col.color);
                Note("  ps " + ps.name + " mat=" + (ps.GetComponent<ParticleSystemRenderer>().sharedMaterial ? ps.GetComponent<ParticleSystemRenderer>().sharedMaterial.name : "-") + " col=" + m.startColor.mode);
            }
            root.transform.localScale = Vector3.one * Scale;
            root.name = "WaterFX_Splash";
            PrefabUtility.SaveAsPrefabAsset(root, Dst);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Note("made " + Dst);
    }

    static System.Collections.IEnumerator Wade(RoomRun.RunDirector run, string tag)
    {
        string dir = Path.Combine(Dir, "wfx_shots"); Directory.CreateDirectory(dir);
        run.Player.DebugInvulnerable = true;
        for (float w = 0; w < 20f && (run.Room == null || run.Room.id != "valley" || run.Transitioning); w += Time.unscaledDeltaTime)
        {
            if (!run.Transitioning && (run.Room == null || run.Room.id != "valley")) run.DevJump("valley");
            yield return null;
        }
        yield return new WaitForSeconds(2.5f);
        if (run.Room.id != "valley") { Note("wade: not in valley (" + run.Room.id + "), stopping"); SessionState.EraseString(KeyWade); wadeRunning = false; EditorApplication.isPlaying = false; yield break; }
        var pt = run.Player.transform;
        // shallow stream spot nearest the player (depth 0.15-0.45)
        Vector3 best = pt.position; float bd = 1e9f;
        for (float x = -50; x <= 50; x += 1f) for (float z = -40; z <= 40; z += 1f)
        {
            float d = LookStudyValley.Water - LookStudyValley.Height(x, z);
            if (d < 0.2f || d > 0.5f) continue;
            bool open = true;   // keep 2 m of water all around so the splash isn't cut by the bank
            for (int a = 0; a < 8 && open; a++) { float an = a * Mathf.PI / 4f; open = LookStudyValley.Water - LookStudyValley.Height(x + Mathf.Cos(an) * 2f, z + Mathf.Sin(an) * 2f) > 0.1f; }
            if (!open) continue;
            float dist = (new Vector3(x, 0, z) - new Vector3(pt.position.x, 0, pt.position.z)).sqrMagnitude;
            if (dist < bd) { bd = dist; best = new Vector3(x, 0, z); }
        }
        Note($"wade at {best} (player was {pt.position})");
        var cc = pt.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;
        // walk back and forth along x through the water at walking speed, capture at a few moments
        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        var rig = run.Rig;
        float t = 0; int shot = 0;
        float[] shotAt = { 1.2f, 1.45f, 2.1f, 2.35f };
        float[] zoomFor = { 1.6f, 1.6f, 0.8f, 0.8f };
        while (shot < shotAt.Length)
        {
            t += Time.deltaTime;
            float s = Mathf.PingPong(t * 4.5f, 2f) - 1f;
            var p = best + new Vector3(s, 0, 0);
            p.y = Mathf.Max(LookStudyValley.Height(p.x, p.z), LookStudyValley.Water - 0.6f);
            pt.position = p;
            if (t >= shotAt[shot])
            {
                rig.Zoom = zoomFor[shot]; rig.Snap();
                var cam = rig.Cam;
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(dir, $"{tag}_{shot}_z{zoomFor[shot]:0.00}.jpg"), tex.EncodeToJPG(90));
                shot++;
            }
            yield return null;
        }
        if (cc) cc.enabled = true;
        rig.Zoom = RoomRun.CameraRig.FixedZoom;
        UnityEngine.Object.Destroy(rt); UnityEngine.Object.Destroy(tex);
        Note($"wade {tag} done, fps {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}");
        SessionState.EraseString(KeyWade);
        wadeRunning = false;
        EditorApplication.isPlaying = false;
    }

    static void Run(string line)
    {
        var p = line.Split(new[] { ' ' }, 2);
        if (p[0] == "methods")
        {
            foreach (var n in new[] { "IAssetStoreDownloadManager", "IAssetStoreCache" })
            {
                var s = Service(n);
                Note(n + " -> " + s.GetType().FullName);
                foreach (var m in s.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.DeclaringType == s.GetType()))
                    Note("  " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")");
            }
        }
        else if (p[0] == "download")
        {
            long id = long.Parse(p[1]);
            var s = Service("IAssetStoreDownloadManager");
            var m = s.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .First(x => x.Name == "Download" && x.GetParameters().Length == 1);
            var pt = m.GetParameters()[0].ParameterType;
            object arg = pt == typeof(long) ? (object)id : pt.IsAssignableFrom(typeof(long[])) ? new[] { id } : (object)new System.Collections.Generic.List<long> { id };
            Note("download " + id + " via " + m + " -> " + m.Invoke(s, new[] { arg }));
        }
        else if (p[0] == "import")
        {
            AssetDatabase.ImportPackage(p[1], false);
            Note("imported " + p[1]);
        }
        else if (p[0] == "preview")
        {
            // preview <prefabPath> <out.png> [distance] [pitch]
            var a = p[1].Split(' ');
            Preview(a[0], a[1], a.Length > 2 ? float.Parse(a[2]) : 4f, a.Length > 3 ? float.Parse(a[3]) : 55f);
        }
        else if (p[0] == "make") { Make(); }
        else if (p[0] == "wade")
        {
            // wade <tag> : play the room-run valley, walk the player through the stream, capture game view + close-up
            const string rr = "Assets/Game/RoomRun/Scenes/RoomRun.unity";
            var sc = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (sc.path != rr)
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                    if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) { Note("wade refused: unsaved changes in " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path); return; }
                SessionState.SetString(KeyRestore, sc.path);
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(rr, UnityEditor.SceneManagement.OpenSceneMode.Single);
                Note("opened RoomRun (will restore " + sc.path + ")");
            }
            SessionState.SetFloat(KeyWadeT, (float)EditorApplication.timeSinceStartup);
            SessionState.SetString(KeyWade, p.Length > 1 ? p[1] : "wade");
            Note("wade: entering play mode");
            EditorApplication.isPlaying = true;
        }
        else Note("unknown: " + line);
    }
}
