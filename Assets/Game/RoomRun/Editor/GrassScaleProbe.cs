// 11-19 character-scale check (Kido: "캐릭터가 환경에 비해 너무 작아보임").
//   call RoomRun.GrassScaleProbe.Run          edit mode: mesh/prefab sizes -> ArtSource/CharScale/probe_edit.txt
//   call RoomRun.GrassScaleProbe.StartBefore  play: F1 풀밭 still before the grass/flower change
//   call RoomRun.GrassScaleProbe.StartAfter   play: F1 still after + measurements + 1.0/1.2/1.35/1.5x stills/videos in 3 maps
// The player scale is only multiplied at runtime on the Visual transform (nothing is saved).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    [InitializeOnLoad]
    public static class GrassScaleProbe
    {
        const string Real = "Assets/Art/LookStudy/Models/Real/";
        const string KeyMode = "GSP.Mode", KeyRunning = "GSP.Running";
        static string Dir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");
        static string Out => Path.Combine(Dir, "CharScale");
        static string LockPath => Path.Combine(Dir, "remote_play.lock");
        static readonly float[] Scales = { 1f, 1.2f, 1.35f, 1.5f };
        static StringBuilder log = new StringBuilder();

        static GrassScaleProbe() { EditorApplication.update += Tick; }

        static void Note(string s)
        {
            Directory.CreateDirectory(Out);
            File.AppendAllText(Path.Combine(Out, "probe_log.txt"), $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }
        static void Lock() { try { File.WriteAllText(LockPath, "gsp " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (IOException) { } }
        static void Unlock() { try { if (File.Exists(LockPath) && File.ReadAllText(LockPath).StartsWith("gsp ")) File.Delete(LockPath); } catch (IOException) { } }

        public static void StartBefore() => Begin("before");
        public static void StartAfter() => Begin("after");
        public static void StartJar() => Begin("jar");
        public static void StartApplied() => Begin("applied");   // 11-19: playerSize 1.5 committed; one walk+attack video through 4 maps
        static void Begin(string mode)
        {
            Lock();
            SessionState.SetString(KeyMode, mode);
            SessionState.SetBool(KeyRunning, false);
            Note($"start {mode}: entering play mode");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            string mode = SessionState.GetString(KeyMode, "");
            if (mode == "") return;
            if (mode == "done")
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) { SessionState.SetString(KeyMode, ""); Unlock(); Note("released"); }
                return;
            }
            if (!EditorApplication.isPlaying || SessionState.GetBool(KeyRunning, false)) return;
            var run = Object.FindAnyObjectByType<RunDirector>();
            if (!run || run.Player == null || run.Room == null || run.Rig == null) return;
            SessionState.SetBool(KeyRunning, true);
            run.StartCoroutine(Drive(run, mode));
        }

        static IEnumerator Jump(RunDirector run, string id)
        {
            Lock();
            if (run.Room.id != id) run.DevJump(id);
            for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
            while (run.Transitioning) yield return null;
            yield return new WaitForSeconds(2.5f);
            foreach (var e in run.Enemies) if (e) Object.Destroy(e.gameObject);
            run.Enemies.Clear();
        }

        static void Place(RunDirector run, Vector3 p)
        {
            var pt = run.Player.transform;
            var cc = pt.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f) ? hit.point.y : pt.position.y;
            pt.position = p;
            if (cc) cc.enabled = true;
        }

        static Transform Visual(RunDirector run) => run.Player.transform.Find("Visual");

        static Bounds PlayerBounds(RunDirector run)
        {
            var b = new Bounds(run.Player.transform.position, Vector3.zero); bool any = false;
            foreach (var r in run.Player.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                string n = r.name.ToLowerInvariant();
                if (n.Contains("ring") || n.Contains("sword") || n.Contains("blade") || n.Contains("shadow") || n.Contains("trail")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static float ScreenFrac(Camera cam, Bounds b)
        {
            var c = b.center;
            float top = cam.WorldToViewportPoint(new Vector3(c.x, b.max.y, c.z)).y, bot = cam.WorldToViewportPoint(new Vector3(c.x, b.min.y, c.z)).y;
            return top - bot;
        }

        class Ctx { public RenderTexture rt; public Texture2D tex; public int w, h; }
        static Ctx Make(int w, int h) => new Ctx { w = w, h = h, rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 }, tex = new Texture2D(w, h, TextureFormat.RGB24, false) };
        static void Grab(Camera cam, Ctx c, string path, int q = 90)
        {
            cam.targetTexture = c.rt; cam.Render();
            RenderTexture.active = c.rt; c.tex.ReadPixels(new Rect(0, 0, c.w, c.h), 0, 0); RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(path, c.tex.EncodeToJPG(q));
        }

        sealed class Walker : ICommandSource
        {
            readonly float t0 = Time.time; readonly float phase;
            public Walker(float phase) { this.phase = phase; }
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0, a = phase + t * 0.7f;
                var mv = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                return new PlayerCommand { move = mv, aimDir = new Vector3(mv.x, 0, mv.y) };
            }
        }

        sealed class Fighter : ICommandSource
        {
            readonly float t0 = Time.time; readonly float phase;
            public Fighter(float phase) { this.phase = phase; }
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0, a = phase + t * 0.7f;
                var mv = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                bool atk = t > 1.2f && (t % 2.2f) < 0.9f;   // walk, then a short attack burst every 2.2 s
                return new PlayerCommand { move = atk ? mv * 0.3f : mv, aimDir = new Vector3(mv.x, 0, mv.y), fire = atk };
            }
        }

        static readonly string[] JarWords = { "jar", "onggi", "jang", "hangari", "pot", "crock", "urn", "dok", "항아리", "장독", "옹기" };
        static readonly string[] PropWords = { "door", "gate", "house", "hanok", "tree", "rock", "wall", "fence", "lantern", "bamboo", "pine", "roof" };

        static List<(string name, Bounds b)> Find(string[] words)
        {
            var res = new List<(string, Bounds)>();
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled || r is ParticleSystemRenderer) continue;
                        string n = r.name.ToLowerInvariant(), pn = r.transform.parent ? r.transform.parent.name.ToLowerInvariant() : "";
                        if (words.Any(w => n.Contains(w) || pn.Contains(w))) res.Add((r.transform.parent ? r.transform.parent.name + "/" + r.name : r.name, r.bounds));
                    }
            return res;
        }

        static IEnumerator Drive(RunDirector run, string mode)
        {
            Directory.CreateDirectory(Out);
            RunDirector.DevNoEnemies = true;
            run.Player.DebugInvulnerable = true;
            var orphans = new List<Renderer>();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                if (r && !EditorUtility.IsPersistent(r) && r.enabled && !r.gameObject.scene.IsValid()) { r.enabled = false; orphans.Add(r); }
            var rig = run.Rig; var cam = rig.Cam;
            var still = Make(1600, 900); var vid = Make(1280, 720);
            var keepSource = run.Player.Source;
            Note($"drive {mode}: camera height {rig.height} back {rig.back} fov {rig.fov} zoom {CameraRig.FixedZoom}");

            // F1 풀밭 before/after: room centre, default zoom, scale 1
            yield return Jump(run, "grass");
            var gc = run.Room.center;
            Place(run, gc); rig.Zoom = CameraRig.FixedZoom; rig.Snap();
            yield return new WaitForSeconds(0.6f); rig.Snap(); yield return null;
            Grab(cam, still, Path.Combine(Out, $"f1_grass_{mode}.jpg"));
            Note($"still f1_grass_{mode}.jpg");
            if (mode == "after")
            {
                // camera numbers
                var off = new Vector3(0, rig.height, -rig.back) * CameraRig.FixedZoom;
                Note($"camera: distance {off.magnitude:F2} m, pitch {Mathf.Atan2(rig.height, rig.back) * Mathf.Rad2Deg:F1} deg, vertical FOV {cam.fieldOfView:F1}, ortho {cam.orthographic}; " +
                     $"frustum height at focus {2f * off.magnitude * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad):F2} m");
                var vis = Visual(run); var baseScale = vis.localScale;
                // measurements at each scale
                foreach (float k in Scales)
                {
                    vis.localScale = baseScale * k; yield return null; yield return null;
                    var pb = PlayerBounds(run);
                    Note($"player x{k}: height {pb.size.y:F2} m (minY {pb.min.y - run.Player.transform.position.y:F2}), screen {ScreenFrac(cam, pb) * 100f:F1}% of height at zoom {rig.Zoom}");
                }
                vis.localScale = baseScale;
                foreach (float z in new[] { 1.1f, 1.0f })
                {
                    rig.Zoom = z; rig.Snap(); yield return null;
                    Note($"player x1 at zoom {z}: screen {ScreenFrac(cam, PlayerBounds(run)) * 100f:F1}%");
                }
                rig.Zoom = CameraRig.FixedZoom;

                // props / jars in the other maps
                foreach (var id in new[] { "village", "abandoned", "bamboo" })
                {
                    yield return Jump(run, id);
                    var jars = Find(JarWords).OrderByDescending(j => j.b.size.y).ToList();
                    Note($"{id}: {jars.Count} jar-like renderers");
                    foreach (var g in jars.GroupBy(j => j.name).Take(12)) Note($"  jar {g.Key}: height {g.First().b.size.y:F2} m width {g.First().b.size.x:F2} (x{g.Count()})");
                    foreach (var g in Find(PropWords).GroupBy(j => j.name).OrderByDescending(g => g.First().b.size.y).Take(14)) Note($"  prop {g.Key}: height {g.First().b.size.y:F2} m (x{g.Count()})");
                }

                // per map: stills at each scale (+ closer camera at x1) and a short walk per scale
                var spots = new Dictionary<string, Vector3>();
                foreach (var id in new[] { "grass", "village", "bamboo" })
                {
                    yield return Jump(run, id);
                    var spot = run.Room.center;
                    if (id == "village")
                    {
                        var jars = Find(JarWords).Where(j => j.b.size.y > 0.4f && Vector3.Distance(j.b.center, run.Room.center) < 60f).OrderByDescending(j => j.b.size.y).ToList();
                        if (jars.Count > 0)
                        {
                            // the biggest jar nearest the room centre
                            var jb = jars.Take(6).OrderBy(j => Vector3.Distance(j.b.center, run.Room.center)).First();
                            spot = jb.b.center + new Vector3(jb.b.extents.x + 0.7f, 0, -0.3f);
                            Note($"village jar shot next to {jb.name} (height {jb.b.size.y:F2} m) at {jb.b.center}");
                        }
                    }
                    spots[id] = spot;
                    foreach (float k in Scales)
                    {
                        Place(run, spot); vis = Visual(run); vis.localScale = baseScale * k;
                        rig.Zoom = CameraRig.FixedZoom; rig.Snap();
                        yield return new WaitForSeconds(0.5f); rig.Snap(); yield return null;
                        Grab(cam, still, Path.Combine(Out, $"{id}_scale_{k:0.00}.jpg"));
                        if (k == 1f)
                            foreach (float z in new[] { 1.25f, 1.1f })
                            {
                                rig.Zoom = z; rig.Snap(); yield return null; yield return null;
                                Grab(cam, still, Path.Combine(Out, $"{id}_cam_closer_z{z:0.00}.jpg"));
                            }
                        rig.Zoom = CameraRig.FixedZoom;
                    }
                    vis.localScale = baseScale;
                }
                // videos: one per scale, the three maps in a row, real-time walking (30 fps game time)
                foreach (float k in Scales)
                {
                    string vd = Path.Combine(Out, $"frames_scale_{k:0.00}"); Directory.CreateDirectory(vd);
                    int n = 0;
                    foreach (var id in new[] { "grass", "village", "bamboo" })
                    {
                        yield return Jump(run, id);
                        Place(run, spots[id]); vis = Visual(run); vis.localScale = baseScale * k;
                        rig.Zoom = CameraRig.FixedZoom; rig.Snap();
                        run.Player.Source = new Walker(id == "village" ? 1.6f : 0.4f);
                        Time.captureFramerate = 30;
                        for (int f = 0; f < 105; f++)
                        {
                            yield return new WaitForEndOfFrame();
                            Lock();
                            Grab(cam, vid, Path.Combine(vd, $"f_{n++:0000}.jpg"), 86);
                        }
                        Time.captureFramerate = 0;
                        run.Player.Source = keepSource;
                        vis.localScale = baseScale;
                    }
                    Note($"video frames scale {k}: {n} -> {vd}");
                }
            }
            if (mode == "applied")
            {
                string vd = Path.Combine(Out, "frames_applied"); Directory.CreateDirectory(vd);
                foreach (var f in Directory.GetFiles(vd, "f_*.jpg")) File.Delete(f);
                int n = 0;
                var spots = new (string id, Vector3 p, float ph)[] { ("grass", Vector3.zero, 0.4f), ("village", new Vector3(5.5f, 0, -27.3f), 1.6f), ("bamboo", Vector3.zero, 0.4f), ("abandoned", new Vector3(25.3f, 0, -20.4f), 2.4f) };
                foreach (var sp in spots)
                {
                    yield return Jump(run, sp.id);
                    Place(run, sp.p == Vector3.zero ? run.Room.center : sp.p);
                    rig.Zoom = CameraRig.FixedZoom; rig.Snap(); yield return null;
                    var pb = PlayerBounds(run);
                    Note($"applied {sp.id}: player height {pb.size.y:F2} m, screen {ScreenFrac(cam, pb) * 100f:F1}%");
                    run.Player.Source = new Fighter(sp.ph);
                    Time.captureFramerate = 30;
                    for (int f = 0; f < 150; f++)
                    {
                        yield return new WaitForEndOfFrame();
                        Lock();
                        Grab(cam, vid, Path.Combine(vd, $"f_{n++:0000}.jpg"), 86);
                    }
                    Time.captureFramerate = 0;
                    run.Player.Source = keepSource;
                }
                Note($"applied video frames: {n} -> {vd}");
            }
            if (mode == "jar")
            {
                // 폐촌: the player next to the big dark 장독 (AV_MS_KJar), every scale; the walk appended to each scale's frames
                yield return Jump(run, "abandoned");
                var jars = Find(new[] { "kjar", "onggi" }).Where(j => j.b.size.y > 0.9f && j.b.size.x < 2.5f)
                    .OrderBy(j => Vector3.Distance(j.b.center, run.Room.center)).ToList();
                var spot = run.Room.center;
                if (jars.Count > 0)
                {
                    var jb = jars[0];
                    spot = jb.b.center + new Vector3(jb.b.extents.x + 0.6f, 0, -0.2f);
                    Note($"abandoned jar shot next to {jb.name}: height {jb.b.size.y:F2} m width {jb.b.size.x:F2} at {jb.b.center}");
                }
                var vis = Visual(run); var baseScale = vis.localScale;
                foreach (float k in Scales)
                {
                    Place(run, spot); vis = Visual(run); vis.localScale = baseScale * k;
                    rig.Zoom = CameraRig.FixedZoom; rig.Snap();
                    yield return new WaitForSeconds(0.5f); rig.Snap(); yield return null;
                    Grab(cam, still, Path.Combine(Out, $"abandoned_jar_scale_{k:0.00}.jpg"));
                    if (k == 1f)
                        foreach (float z in new[] { 1.25f, 1.1f })
                        {
                            rig.Zoom = z; rig.Snap(); yield return null; yield return null;
                            Grab(cam, still, Path.Combine(Out, $"abandoned_jar_cam_closer_z{z:0.00}.jpg"));
                        }
                    rig.Zoom = CameraRig.FixedZoom;
                }
                foreach (float k in Scales)
                {
                    string vd = Path.Combine(Out, $"frames_scale_{k:0.00}"); Directory.CreateDirectory(vd);
                    int n = Directory.GetFiles(vd, "f_*.jpg").Length;
                    Place(run, spot); vis = Visual(run); vis.localScale = baseScale * k;
                    rig.Zoom = CameraRig.FixedZoom; rig.Snap();
                    run.Player.Source = new Walker(2.4f);
                    Time.captureFramerate = 30;
                    for (int f = 0; f < 105; f++)
                    {
                        yield return new WaitForEndOfFrame();
                        Lock();
                        Grab(cam, vid, Path.Combine(vd, $"f_{n++:0000}.jpg"), 86);
                    }
                    Time.captureFramerate = 0;
                    run.Player.Source = keepSource;
                    vis.localScale = baseScale;
                    Note($"jar walk frames scale {k}: now {n}");
                }
            }
            foreach (var r in orphans) if (r) r.enabled = true;
            run.Player.Source = keepSource;
            RunDirector.DevNoEnemies = false;
            Note($"drive {mode} done");
            SessionState.SetString(KeyMode, "done");
            EditorApplication.isPlaying = false;
        }

        // ---------- edit mode sizes
        static Bounds GoBounds(GameObject go)
        {
            var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Ring")) continue;
                Bounds rb;
                if (r is SkinnedMeshRenderer s && s.sharedMesh)
                {
                    var baked = new Mesh(); s.BakeMesh(baked, true);
                    var v = baked.vertices; if (v.Length == 0) continue;
                    rb = new Bounds(s.transform.TransformPoint(v[0]), Vector3.zero);
                    foreach (var p in v) rb.Encapsulate(s.transform.TransformPoint(p));
                    Object.DestroyImmediate(baked);
                }
                else if (r is MeshRenderer) rb = r.bounds;
                else continue;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        public static void Run()
        {
            var sb = new StringBuilder();
            var cfg = AssetDatabase.LoadAssetAtPath<RoomRunConfig>("Assets/Game/RoomRun/RoomRunConfig.asset");
            var list = new List<(string, GameObject, float)> { ("greatsword", cfg.greatswordPrefab, cfg.greatswordScale), ("player", cfg.playerPrefab, cfg.playerScale) };
            foreach (var e in cfg.enemies) list.Add(("enemy " + e.id, e.prefab, e.scale));
            foreach (var (label, prefab, scale) in list)
            {
                if (!prefab) continue;
                var go = (GameObject)Object.Instantiate(prefab);
                go.transform.position = Vector3.zero; go.transform.localScale = go.transform.localScale * scale;
                var b = GoBounds(go);
                sb.AppendLine($"{label} {prefab.name} x{scale}: height {b.size.y:F2} m width {b.size.x:F2}");
                Object.DestroyImmediate(go);
            }
            foreach (var n in new[] { "Grass_Clump01", "Grass_Ckump02", "Grass_Ckump03", "Blade_Master", "Blade_low1", "Blade_low2", "Blade_low3", "Flower_Yellow", "Flower_White", "Flower_Rose", "Plant_Leaves", "Plant_Ivy", "Bush_Leafy" })
            {
                var b = new Bounds(); bool any = false;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Real + n + ".fbx"))
                    if (o is Mesh m) { if (!any) { b = m.bounds; any = true; } else b.Encapsulate(m.bounds); }
                sb.AppendLine($"mesh {n}: {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} (minY {b.min.y:F2} maxY {b.max.y:F2})");
            }
            Directory.CreateDirectory(Out);
            File.WriteAllText(Path.Combine(Out, "probe_edit.txt"), sb.ToString());
        }
    }
}
