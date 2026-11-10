// Editor helpers for the ink filters: add the filter object to the open scene, and capture the four modes from the
// scene's game camera (the RoomRun angle) into Downloads/scene-refs/ink-filter-test.
using System.IO;
using InkFilters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class InkFilterTools
{
    const string Root = "Assets/Game/InkFilters";

    public static InkFilter Ensure()
    {
        var f = Object.FindAnyObjectByType<InkFilter>();
        if (!f) f = new GameObject("InkFilter").AddComponent<InkFilter>();
        T(ref f.stroke, "ThirdParty/sacshadow_ChineseInk/Stroke_0.psd");
        T(ref f.stroke2, "ThirdParty/sacshadow_ChineseInk/Stroke_2.psd");
        T(ref f.inkNoise, "ThirdParty/sacshadow_ChineseInk/CIP_OutLine_Noise.psd");
        T(ref f.waterNoise, "ThirdParty/Cyanilux_Watercolour/noise.png");
        T(ref f.paper, "ThirdParty/ambientCG/Paper005_1K-JPG_Color.jpg");
        f.shader = Shader.Find("Hidden/InkFilters/Ink");
        EditorUtility.SetDirty(f);
        return f;
    }

    static void T(ref Texture2D t, string rel)
    {
        var path = $"{Root}/{rel}";
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp && (imp.maxTextureSize > 1024 || imp.wrapMode != TextureWrapMode.Repeat))
        {
            imp.maxTextureSize = 1024; imp.wrapMode = TextureWrapMode.Repeat;
            if (rel.Contains("noise") || rel.Contains("Noise")) imp.sRGBTexture = false;
            imp.SaveAndReimport();
        }
        t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    [MenuItem("Tools/Dev/Ink Filters/Add To Open Scene")]
    public static void Add() { Ensure(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes(); }

    public static string OutDir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Downloads", "scene-refs", "ink-filter-test");

    public static void Shot(Camera cam, string file)
    {
        Directory.CreateDirectory(OutDir);
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); tex.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
        cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }

    [MenuItem("Tools/Dev/Ink Filters/Capture Reed Field Comparison")]
    public static void CaptureReeds()
    {
        var f = Ensure(); var cam = Camera.main;
        if (!cam) { Debug.LogError("[Ink] no camera"); return; }
        ShaderUtil.allowAsyncCompilation = false;
        var keep = f.editorMode;
        string[] files = { "01_off.png", "02_ink_sacshadow.png", "03_sumie.png", "04_watercolour.png" };
        for (int m = 0; m < 4; m++)
        {
            f.editorMode = (InkMode)m;
            for (int w = 0; w < 3; w++) cam.Render();   // warm up
            Shot(cam, files[m]);
        }
        f.editorMode = keep;
        ShaderUtil.allowAsyncCompilation = true;
        Debug.Log("[Ink] reed field shots saved to " + OutDir);
    }

    // ---------- Play-mode probe: jump to a field, switch each mode on, capture the game view, move the player, capture again.
    // Used to check the filters update every frame (no frozen / ghosted image) and how they look while moving.
    static System.Collections.Generic.Queue<System.Action> probeSteps;
    static double probeNext;
    public static string ProbeDir = Path.Combine(OutDir, "v2");

    static void Wait(double s) => probeSteps.Enqueue(() => probeNext = EditorApplication.timeSinceStartup + s);
    static void Do(System.Action a) => probeSteps.Enqueue(a);

    static void ProbeTick()
    {
        if (!EditorApplication.isPlaying || probeSteps == null || probeSteps.Count == 0) { EditorApplication.update -= ProbeTick; return; }
        if (EditorApplication.timeSinceStartup < probeNext) return;
        probeSteps.Dequeue()();
    }

    static void Jump(string room)
    {
        var t = System.Type.GetType("RoomRun.RunDirector, RoomRun");
        if (t == null) foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType("RoomRun.RunDirector"); if (t != null) break; }
        var inst = t?.GetProperty("Instance")?.GetValue(null);
        t?.GetMethod("DevJump")?.Invoke(inst, new object[] { room });
    }

    static void MovePlayer(Vector3 d)
    {
        var p = GameObject.Find("Player"); if (!p) return;
        var cc = p.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
        p.transform.position += d; if (cc) cc.enabled = true;
    }

    static void Cap(string file) { Directory.CreateDirectory(ProbeDir); ScreenCapture.CaptureScreenshot(Path.Combine(ProbeDir, file)); }

    static void LogCams()
    {
        var sb = new System.Text.StringBuilder("[Ink] cameras: ");
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            sb.Append($"{c.name}(en {c.isActiveAndEnabled}, d {c.depth}, clear {c.clearFlags}, rt {(c.targetTexture ? c.targetTexture.name : "-")}) ");
        sb.Append($" filters: {Object.FindObjectsByType<InkFilter>(FindObjectsSortMode.None).Length}, mode {InkFilter.Mode}");
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/Dev/Ink Filters/Probe Play (reeds)")] public static void ProbeReeds() => Probe("reeds");
    [MenuItem("Tools/Dev/Ink Filters/Probe Play (bamboo)")] public static void ProbeBamboo() => Probe("bamboo");

    // Small forward step with ink on: the brush strokes must slide down the screen together with the ground.
    [MenuItem("Tools/Dev/Ink Filters/Probe Step (current field)")]
    public static void ProbeStep()
    {
        if (!EditorApplication.isPlaying) return;
        probeSteps = new System.Collections.Generic.Queue<System.Action>();
        if (!Object.FindAnyObjectByType<InkFilter>()) { Do(() => Jump("reeds")); Wait(4); }
        Do(() => InkFilter.Mode = InkMode.Ink); Wait(0.6);
        Do(() => Cap("step_a.png")); Wait(0.3);
        Do(() => MovePlayer(new Vector3(0, 0, 1.2f))); Wait(1.5);
        Do(() => Cap("step_b.png")); Wait(0.3);
        Do(() => { MovePlayer(new Vector3(0, 0, -1.2f)); Debug.Log("[Ink] step done"); });
        probeNext = 0;
        EditorApplication.update -= ProbeTick; EditorApplication.update += ProbeTick;
    }

    public static void Probe(string room)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Ink] enter Play first"); return; }
        probeSteps = new System.Collections.Generic.Queue<System.Action>();
        Do(() => { InkFilter.Mode = InkMode.Off; Jump(room); }); Wait(4);
        Do(LogCams);
        string[] tag = { "off", "ink", "sumie", "water" };
        for (int m = 0; m < 4; m++)
        {
            int mm = m;
            Do(() => InkFilter.Mode = (InkMode)mm); Wait(0.6);
            Do(() => Cap($"probe_{room}_{mm}{tag[mm]}_a.png")); Wait(0.3);
            Do(() => MovePlayer(new Vector3(5, 0, 4))); Wait(1.0);
            Do(() => Cap($"probe_{room}_{mm}{tag[mm]}_b.png")); Wait(0.3);
            Do(() => MovePlayer(new Vector3(-5, 0, -4))); Wait(0.8);
        }
        Do(() =>
        {
            InkFilter.Mode = InkMode.Off;
            // the "a" frames of each filter as the review shots
            string[] final = { null, "01_ink.png", "02_sumie_colour.png", "03_watercolour.png" };
            for (int m = 1; m < 4; m++)
            {
                var srcPath = Path.Combine(ProbeDir, $"probe_{room}_{m}{tag[m]}_a.png");
                if (File.Exists(srcPath)) File.Copy(srcPath, Path.Combine(ProbeDir, final[m]), true);
            }
            Debug.Log("[Ink] probe done -> " + ProbeDir);
        });
        probeNext = 0;
        EditorApplication.update -= ProbeTick; EditorApplication.update += ProbeTick;
    }

    // Average frame time of the scene camera per mode (GPU synced every 10 frames).
    [MenuItem("Tools/Dev/Ink Filters/Benchmark Modes")]
    public static void Bench()
    {
        var f = Ensure(); var cam = Camera.main; if (!cam) return;
        var rt = new RenderTexture(1920, 1080, 24); cam.targetTexture = rt;
        var keep = f.editorMode; var sb = new System.Text.StringBuilder("[Ink] ms per frame (1920x1080): ");
        for (int m = 0; m < 4; m++)
        {
            f.editorMode = (InkMode)m;
            for (int i = 0; i < 30; i++) cam.Render();
            double best = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) { cam.Render(); if (i % 10 == 9) UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion(); }
                best = System.Math.Min(best, sw.Elapsed.TotalMilliseconds / 100);
            }
            sb.Append($"{(InkMode)m} {best:0.00}  ");
        }
        f.editorMode = keep; cam.targetTexture = null; Object.DestroyImmediate(rt);
        Debug.Log(sb.ToString());
        Directory.CreateDirectory(OutDir);
        File.AppendAllText(Path.Combine(OutDir, "bench.txt"), System.DateTime.Now.ToString("s") + " " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " " + sb + "\n");
    }
}
