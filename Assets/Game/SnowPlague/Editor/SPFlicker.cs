// 11-14 (Kido: "식생들이 깜빡거려" while the camera moves, and an awkward dark post on the snow).
// Remote diagnostics, own command file so it never collides with sp_cmd.txt:
//   ArtSource/SnowPlague/spf_cmd.txt -> "flicker <outDir> [x z zoom]" | "posts" | "closeups <outDir>"
// flicker: renders the game view at three camera positions 1/3 px apart and scores |A - 2B + C| per pixel (smooth
// motion changes a pixel linearly, aliasing pops it), writes the frames + a heat map, then the same score with each
// vegetation material switched off in turn, so the log names which material shimmers most.
// posts: logs every upright post-like renderer / scatter instance (taller than ~1.6x its width) in or near the arena.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SnowPlague;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class SPFlicker
{
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "SnowPlague");
    static double next, lastChange;
    static string LockPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "remote_play.lock");

    static SPFlicker()
    {
        EditorApplication.update += Tick;
        lastChange = EditorApplication.timeSinceStartup;
        EditorApplication.playModeStateChanged += _ => lastChange = EditorApplication.timeSinceStartup;
    }

    static bool Ready()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
        if (EditorApplication.timeSinceStartup - lastChange < 15.0) return false;
        try
        {
            if (File.Exists(LockPath) && !File.ReadAllText(LockPath).StartsWith("spf ")
                && (System.DateTime.Now - File.GetLastWriteTime(LockPath)).TotalMinutes < 10) return false;
        }
        catch (IOException) { return false; }
        return true;
    }

    static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "sp_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] SPF {s}\n"); }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmdPath = Path.Combine(Dir, "spf_cmd.txt");
        if (!File.Exists(cmdPath) || !Ready()) return;
        var cmd = File.ReadAllText(cmdPath).Trim();
        File.Delete(cmdPath);
        try { File.WriteAllText(LockPath, "spf " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (IOException) { }
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != SPBuilder.ScenePath) { Log("ABORT unsaved changes in " + active.path); Release(); return; }
        var prev = active.path;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            if (EditorSceneManager.GetActiveScene().path != SPBuilder.ScenePath) EditorSceneManager.OpenScene(SPBuilder.ScenePath, OpenSceneMode.Single);
            var p = cmd.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (p[0] == "flicker") Flicker(p.Length > 1 ? p[1] : Path.Combine(Dir, "flicker"),
                p.Length > 4 ? new Vector3(float.Parse(p[2]), 0, float.Parse(p[3])) : new Vector3(-10f, 0, -8f), p.Length > 4 ? float.Parse(p[4]) : 1.6f);
            if (p[0] == "posts") Posts();
            if (p[0] == "census") Census();
            if (p[0] == "closeups") Closeups(p.Length > 1 ? p[1] : Path.Combine(Dir, "posts"));
            Log("DONE " + cmd);
        }
        catch (System.Exception e) { Log("EXCEPTION " + e); }
        finally
        {
            ShaderUtil.allowAsyncCompilation = true;
            Release();
            if (!string.IsNullOrEmpty(prev) && prev != SPBuilder.ScenePath && EditorSceneManager.GetActiveScene().path != prev) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
    }
    static void Release() { try { if (File.Exists(LockPath) && File.ReadAllText(LockPath).StartsWith("spf ")) File.Delete(LockPath); } catch (IOException) { } }

    const int W = 1920, H = 1080;

    static Color[] Render(Camera cam, SPField field, Vector3 focus, float zoom, Vector3 shift)
    {
        SPBuilder.SetGameView(cam, focus, zoom);
        cam.transform.position += shift;
        SPBuilder.SnowfallFollow(cam);
        if (field) field.SetGlobals(cam);
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null;
        var px = tex.GetPixels();
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
        return px;
    }
    static void Save(Color[] px, string file)
    {
        var t = new Texture2D(W, H, TextureFormat.RGB24, false); t.SetPixels(px); t.Apply();
        File.WriteAllBytes(file, t.EncodeToPNG()); Object.DestroyImmediate(t);
    }
    static float Lum(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

    // Second temporal difference over three renders; returns the mean score (x1000) and optionally the per-pixel map.
    static float Score(Camera cam, SPField field, Vector3 focus, float zoom, Vector3 step, float[] map, string saveDir = null)
    {
        var a = Render(cam, field, focus, zoom, Vector3.zero);
        var b = Render(cam, field, focus, zoom, step);
        var c = Render(cam, field, focus, zoom, step * 2f);
        if (saveDir != null) { Save(a, Path.Combine(saveDir, "frame_a.png")); Save(b, Path.Combine(saveDir, "frame_b.png")); }
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            float s = Mathf.Abs(Lum(a[i]) - 2f * Lum(b[i]) + Lum(c[i]));
            if (map != null) map[i] = s;
            sum += s;
        }
        return (float)(sum / a.Length * 1000.0);
    }

    static void Flicker(string outDir, Vector3 focus, float zoom)
    {
        Directory.CreateDirectory(outDir);
        var cam = Camera.main; var field = Object.FindAnyObjectByType<SPField>();
        if (!cam) { Log("flicker: no camera"); return; }
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        // the game camera: SMAA (CameraRig), plus whatever this field sets
        var oldAA = data ? data.antialiasing : AntialiasingMode.None;
        if (data) data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        // film grain re-rolls every render: off while scoring (scored separately at the end)
        var grains = new List<FilmGrain>();
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            if (v.sharedProfile && v.sharedProfile.TryGet<FilmGrain>(out var fg) && fg.active) { fg.active = false; grains.Add(fg); }
        // one pixel at the focus distance in world units: move 1/3 px along the camera's right axis per step
        float dist = (new Vector3(0, 17f, -14.5f) * zoom).magnitude;
        float pxWorld = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / H;
        SPBuilder.SetGameView(cam, focus, zoom);
        var step = cam.transform.right * pxWorld / 3f;
        var map = new float[W * H];
        float baseScore = Score(cam, field, focus, zoom, step, map, outDir);
        var heat = new Color[W * H];
        for (int i = 0; i < map.Length; i++) { float s = Mathf.Clamp01(map[i] * 6f); heat[i] = new Color(s, s * s, 0.15f * (1 - s)); }
        Save(heat, Path.Combine(outDir, "heat.png"));
        Log($"flicker focus {focus} zoom {zoom} px {pxWorld * 100f:0.00} cm: score {baseScore:0.00}");

        // per vegetation material: renderers and scatter sets that use it
        var scatters = Object.FindObjectsByType<SPScatter>(FindObjectsSortMode.None);
        var mats = new Dictionary<Material, int>();
        foreach (var sc in scatters) foreach (var s in sc.sets) if (s.mat && s.at.Count > 0) mats[s.mat] = (mats.TryGetValue(s.mat, out var n) ? n : 0) + s.at.Count;
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials) if (m && m.shader && (m.shader.name.Contains("Vegetation") || m.shader.name.Contains("Foreground"))) mats[m] = (mats.TryGetValue(m, out var n) ? n : 0) + 1;
        var results = new List<(string, float, int)>();
        foreach (var kv in mats)
        {
            var m = kv.Key;
            var off = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (r.enabled && r.sharedMaterials.Contains(m)) { r.enabled = false; off.Add(r); }
            var saved = new List<(SPScatter, List<SPScatter.Set>)>();
            foreach (var sc in scatters)
                if (sc.sets.Any(s => s.mat == m)) { saved.Add((sc, sc.sets)); sc.sets = sc.sets.Where(s => s.mat != m).ToList(); sc.Push(); }
            float sOff = Score(cam, field, focus, zoom, step, null);
            foreach (var r in off) r.enabled = true;
            foreach (var (sc, sets) in saved) { sc.sets = sets; sc.Push(); }
            results.Add((m.name + " [" + m.shader.name + "]", baseScore - sOff, kv.Value));
        }
        foreach (var (n, d, c) in results.OrderByDescending(r => r.Item2).Take(14)) Log($"flicker   {d,7:0.00}  {n}  x{c}");
        foreach (var g in grains) g.active = true;
        if (grains.Count > 0)
        {
            var a = Render(cam, field, focus, zoom, Vector3.zero); var b = Render(cam, field, focus, zoom, Vector3.zero);
            double sum = 0; for (int i = 0; i < a.Length; i++) sum += Mathf.Abs(Lum(a[i]) - Lum(b[i]));
            Log($"flicker   film grain frame-to-frame (static camera): {sum / a.Length * 1000.0:0.00}");
        }
        if (data) data.antialiasing = oldAA;
    }

    // per scatter / mesh / material instance counts, in the arena vs outside
    static void Census()
    {
        foreach (var sc in Object.FindObjectsByType<SPScatter>(FindObjectsSortMode.None))
            foreach (var g in sc.sets.Where(s => s.mesh && s.at.Count > 0).OrderByDescending(s => s.at.Count))
            {
                int inA = g.at.Count(a => Mathf.Abs(a.x) < SPBuilder.HX && Mathf.Abs(a.z) < SPBuilder.HZ);
                Log($"census {sc.name}/{g.mesh.name} [{(g.mat ? g.mat.name : "-")}] x{g.at.Count} (arena {inA}), {g.mesh.triangles.Length / 3} tris, size {g.mesh.bounds.size}");
            }
    }

    // ------------------------------------------------------------------ posts
    static bool PostLike(Bounds b) => b.size.y > 0.4f && b.size.y < 8f && Mathf.Max(b.size.x, b.size.z) < b.size.y / 1.6f && Mathf.Max(b.size.x, b.size.z) > 0.08f;
    static bool Near(Vector3 p) => Mathf.Abs(p.x) < SPBuilder.HX + 6f && Mathf.Abs(p.z) < SPBuilder.HZ + 6f;
    static string PathOf(Transform t) { var s = t.name; for (var q = t.parent; q && q.parent; q = q.parent) s = q.name + "/" + s; return s; }

    static List<(string name, Vector3 pos, Bounds b)> FindPosts()
    {
        var list = new List<(string, Vector3, Bounds)>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !Near(r.bounds.center) || !PostLike(r.bounds)) continue;
            var mf = r.GetComponent<MeshFilter>();
            list.Add(($"{PathOf(r.transform)} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : "-")} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")}", r.bounds.center, r.bounds));
        }
        foreach (var sc in Object.FindObjectsByType<SPScatter>(FindObjectsSortMode.None))
            foreach (var s in sc.sets)
            {
                if (!s.mesh) continue;
                for (int i = 0; i < s.at.Count; i++)
                {
                    var sz = s.mesh.bounds.size * s.scale[i] * s.preScale;
                    var c = new Vector3(s.at[i].x, s.at[i].y + sz.y * 0.5f, s.at[i].z);
                    var b = new Bounds(c, sz);
                    if (!Near(c) || !PostLike(b)) continue;
                    list.Add(($"scatter {sc.name} mesh={s.mesh.name} mat={(s.mat ? s.mat.name : "-")}", c, b));
                }
            }
        return list;
    }

    static void Posts()
    {
        var list = FindPosts();
        foreach (var g in list.GroupBy(e => System.Text.RegularExpressions.Regex.Replace(e.name, @"\s*\(\d+\)", "")).OrderByDescending(g => g.Count()))
        {
            var f = g.First();
            Log($"posts x{g.Count(),-4} {g.Key}  e.g. at ({f.pos.x:0.0}, {f.pos.z:0.0}) size {f.b.size}");
        }
        Log($"posts total {list.Count}");
    }

    // close game-angle shots of one example of each post-like kind
    static void Closeups(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var cam = Camera.main; var field = Object.FindAnyObjectByType<SPField>();
        int k = 0;
        foreach (var g in FindPosts().GroupBy(e => System.Text.RegularExpressions.Regex.Replace(e.name, @"\s*\(\d+\)", "")))
        {
            var e = g.OrderBy(x => Mathf.Abs(x.pos.x) + Mathf.Abs(x.pos.z)).First();
            var px = Render(cam, field, new Vector3(e.pos.x, 0, e.pos.z), 0.3f, Vector3.zero);
            var file = Path.Combine(outDir, $"post_{k++:00}.png");
            Save(px, file);
            Log($"closeup {file} <- {g.Key} at ({e.pos.x:0.0}, {e.pos.z:0.0})");
        }
    }
}
