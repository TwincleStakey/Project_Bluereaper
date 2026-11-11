// Lets the abandoned village test scene be rebuilt and captured without clicking: write one line to
// ArtSource/AbandonedVillage/av_cmd.txt -> "build", "shots <outDir>" or "build+shots <outDir>".
// Results go to ArtSource/AbandonedVillage/av_log.txt. The open scene is restored afterwards.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AVRemote
{
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "AbandonedVillage");
    static double next;

    static AVRemote() { EditorApplication.update += Tick; }

    static void Log(string s)
    {
        Directory.CreateDirectory(Dir);
        File.AppendAllText(Path.Combine(Dir, "av_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmdPath = Path.Combine(Dir, "av_cmd.txt");
        if (!File.Exists(cmdPath) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var cmd = File.ReadAllText(cmdPath).Trim();
        File.Delete(cmdPath);
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && active.path != AVBuilder.ScenePath) { Log("ABORT unsaved changes in " + active.path); return; }
        var prev = active.path;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var parts = cmd.Split(new[] { ' ' }, 2);
            if (parts[0].Contains("build")) { AVBuilder.Build(); Log("BUILD OK"); }
            if (parts[0].Contains("shots")) { Shots(parts.Length > 1 ? parts[1] : Path.Combine(Dir, "shots")); }
            if (parts[0] == "probe") Probe();
            if (parts[0] == "orphans")
            {
                var counts = new Dictionary<string, int>();
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                {
                    if (EditorUtility.IsPersistent(r)) continue;
                    var sc = r.gameObject.scene;
                    string key = (sc.IsValid() ? sc.name : "NO-SCENE") + " | " + (r.sharedMaterial ? r.sharedMaterial.name : "null") + " | en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy;
                    counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                }
                foreach (var kv in counts) if (kv.Key.StartsWith("NO-SCENE") || kv.Key.Contains("Ground") || kv.Key.Contains("Puddle")) Log($"RENDERERS {kv.Value} x {kv.Key}");
                Log("ORPHANS DONE");
            }
            if (parts[0] == "probe-ms")
            {
                if (EditorSceneManager.GetActiveScene().path != AVBuilder.ScenePath) EditorSceneManager.OpenScene(AVBuilder.ScenePath, OpenSceneMode.Single);
                int k = 0;
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var m = r.sharedMaterial; if (!m || !m.name.StartsWith("AV_MS_")) continue;
                    if (k++ % 300 != 0) continue;
                    var mf = r.GetComponent<MeshFilter>(); var tex = m.GetTexture("_BaseMap");
                    Log($"PROBE {r.name} mat={m.name} sh={m.shader.name} en={r.enabled} act={r.gameObject.activeInHierarchy} b={r.bounds.center}/{r.bounds.size} verts={(mf && mf.sharedMesh ? mf.sharedMesh.vertexCount : -1)} tex={(tex ? tex.name + " " + tex.width + " " + ((Texture2D)tex).format : "null")} lossy={r.transform.lossyScale} layer={r.gameObject.layer} subs={(mf && mf.sharedMesh ? mf.sharedMesh.subMeshCount : 0)} mats={r.sharedMaterials.Length}");
                }
                Log($"PROBE total {k}");
            }
            if (parts[0] == "orphans-clear")
            {
                // stale runtime DontSave objects (no scene) left by other fields; their fields rebuild them when loaded
                var roots = new HashSet<GameObject>();
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    if (r && !EditorUtility.IsPersistent(r) && !r.gameObject.scene.IsValid() && (r.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0)
                        roots.Add(r.transform.root.gameObject);
                int n = 0;
                foreach (var g in roots) if (g) { Object.DestroyImmediate(g); n++; }
                Log($"ORPHANS CLEARED {n} roots");
            }
            if (parts[0] == "open")
            {
                if (EditorSceneManager.GetActiveScene().path != AVBuilder.ScenePath) EditorSceneManager.OpenScene(AVBuilder.ScenePath, OpenSceneMode.Single);
                prev = AVBuilder.ScenePath;
                var cam = Camera.main;
                var sv = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                sv.Focus();
                if (cam) sv.AlignViewToObject(cam.transform);
                sv.Repaint();
                Log("OPEN OK");
            }
        }
        catch (System.Exception e) { Log("EXCEPTION " + e); }
        finally
        {
            ShaderUtil.allowAsyncCompilation = true;
            if (!string.IsNullOrEmpty(prev) && prev != AVBuilder.ScenePath) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
        }
    }

    static void Probe()
    {
        foreach (var n in new[] { "AV_StoneSteps", "AV_LanternPost", "AV_Hanok2F_A", "AV_Watchtower" })
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AVBuilder.Root + "/Models/" + n + ".fbx");
            var r = go.GetComponentInChildren<MeshRenderer>();
            var mf = go.GetComponentInChildren<MeshFilter>();
            Log($"PROBE {n} child={r.transform.localRotation.eulerAngles} scale={r.transform.localScale} meshBounds={mf.sharedMesh.bounds}");
        }
    }

    public struct View { public string name; public Vector3 focus; public float zoom; public Vector3 pos; public Vector3 look; public float fov; }

    static void Shots(string outDir)
    {
        if (EditorSceneManager.GetActiveScene().path != AVBuilder.ScenePath) EditorSceneManager.OpenScene(AVBuilder.ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(outDir);
        var cam = Camera.main;
        // other fields' runtime DontSave renderers (reed/bamboo ground chunks) draw over every scene in the shared editor
        var orphans = new List<Renderer>();
        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            if (r && !EditorUtility.IsPersistent(r) && r.enabled && !r.gameObject.scene.IsValid()) { r.enabled = false; orphans.Add(r); }
        if (orphans.Count > 0) Log($"SHOTS hid {orphans.Count} orphan renderers from other fields");
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) ps.Simulate(ps.main.prewarm ? 45f : 3f, true, true);
        foreach (var rp in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)) rp.RenderProbe();
        var views = new List<View>
        {
            new View { name = "01_game_center", focus = new Vector3(0, 0, 0), zoom = 1.6f },
            new View { name = "02_game_north_steps", focus = new Vector3(-30f, 0, 24f), zoom = 1.6f },
            new View { name = "03_game_west", focus = new Vector3(30f, 0, -28f), zoom = 1.6f },
            new View { name = "04_game_east", focus = new Vector3(-28f, 0, -30f), zoom = 1.6f },
            new View { name = "05_overview_zoomout", focus = new Vector3(0, 0, 4f), zoom = 2.3f },
            new View { name = "06_close_street", pos = new Vector3(-30f, 4f, 18f), look = new Vector3(-38f, 1.5f, 31f), fov = 50f },
            new View { name = "07_close_west", pos = new Vector3(-6f, 4.5f, -6f), look = new Vector3(-20f, 2.5f, 6f), fov = 50f },
            new View { name = "08_close_east", pos = new Vector3(6f, 5f, 2f), look = new Vector3(20f, 2f, -10f), fov = 50f },
        };
        int w = 1920, h = 1080;
        foreach (var v in views)
        {
            if (v.fov > 0) { cam.fieldOfView = v.fov; cam.transform.position = v.pos; cam.transform.rotation = Quaternion.LookRotation(v.look - v.pos); }
            else { cam.fieldOfView = 30f; AVBuilder.SetGameView(cam, v.focus, v.zoom); }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            cam.targetTexture = null; RenderTexture.active = null;
            var file = Path.Combine(outDir, v.name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            Log("SHOT " + file);
        }
        foreach (var r in orphans) if (r) r.enabled = true;
        cam.fieldOfView = 30f; AVBuilder.SetGameView(cam, Vector3.zero, 1f);
        Log("SHOTS OK");
    }
}
