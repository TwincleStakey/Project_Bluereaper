// 11-16 저승 white-out probe (no menu item, project rule). Queue one line in ArtSource/rr_queue/<name>.txt:
//   call RoomRun.UWWhiteoutProbe.Run     camera-render luminance per spot / zoom / variant + every room's size
//   call RoomRun.UWWhiteoutProbe.Screen  the same from the real Game view (ScreenCapture, HDR path; the camera render into
//                                        an LDR texture hid the white-out), variants rocks0/sea0/glight0/flight0/fx0/bloom0
//   call RoomRun.UWWhiteoutProbe.Bisect  Game-view white % with each shader group / component / light off in turn
//   call RoomRun.UWWhiteoutProbe.Rooms   player height + wall box and walkable floor of every RoomRun room
//   call RoomRun.UWWhiteoutProbe.Import  force-import the fix's shaders / materials (this editor only refreshes on focus)
// Output: ArtSource/rr_shots/uw_probe.txt (+ jpgs). Variants use property blocks / runtime volume copies; no asset changes.
// Found 11-16: Underworld/LavaField pow(0, 0) = NaN on black emissive texels -> bloom white discs (bisect: sea off 53% -> 5%).
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    [InitializeOnLoad]
    public static class UWWhiteoutProbe
    {
        const string KeyState = "UWProbe.State", Me = "uwprobe";
        static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");
        static string Out => Path.Combine(Dir, "rr_shots", "uw_probe.txt");
        static string LockPath => Path.Combine(Dir, "remote_play.lock");
        static double next;

        static UWWhiteoutProbe() { EditorApplication.update += Tick; }

        public static void Run() { Run("full"); }
        public static void Fixed() { Run("fixed"); }
        public static void Screen() { Run("screen"); }
        // the editor here only re-imports when its window gets focus; force the white-out fix files in
        public static void Import()
        {
            foreach (var f in new[] { "Assets/Underworld/Shaders/UWLavaField.shader", "Assets/Underworld/Shaders/UWLavaRock.shader" })
                AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            foreach (var g in AssetDatabase.FindAssets("M_LR_ t:Material", new[] { "Assets/Underworld/Resources/LavaRocks" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh();
            var sh = Shader.Find("Underworld/LavaField"); var rk = Shader.Find("Underworld/LavaRock");
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Underworld/Resources/LavaRocks/M_LR_C03.mat");
            File.AppendAllText(Path.Combine(Dir, "rr_log.txt"), $"[uwprobe] import: LavaField has _MaxLum {(sh && sh.FindPropertyIndex("_MaxLum") >= 0)}, LavaRock has _EmisMax {(rk && rk.FindPropertyIndex("_EmisMax") >= 0)}, M_LR_C03 GlowInt {(m ? m.GetFloat("_GlowInt") : -1)}" + "\n");
        }
        public static void Rooms() { Run("rooms"); }   // player height + every room's box and walkable floor only
        public static void Bisect() { Run("bisect"); }   // Game view white % with each shader group / camera / volume switched off in turn   // Game view (ScreenCapture) instead of a camera render, base only
        static void Run(string mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Out));
            File.WriteAllText(Out, $"[{System.DateTime.Now:HH:mm:ss}] probe {mode}\n");
            SessionState.SetString(KeyState, mode);
            File.WriteAllText(LockPath, Me + " " + System.DateTime.Now.ToString("HH:mm:ss"));
            EditorApplication.isPlaying = true;
        }

        static void Note(string s) { File.AppendAllText(Out, s + "\n"); }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            string st = SessionState.GetString(KeyState, "");
            if (st == "") return;
            if (st == "stopping") { SessionState.SetString(KeyState, ""); EditorApplication.isPlaying = false; Release(); return; }
            if (st.EndsWith("-running")) { if (!EditorApplication.isPlaying) { SessionState.SetString(KeyState, ""); Release(); } return; }
            if (!EditorApplication.isPlaying)
            {
                // Play never came (a compile ate the request): give the slot back after 60 s instead of hijacking someone else's Play later
                float asked = SessionState.GetFloat("UWProbe.AskedAt", 0f);
                if (asked <= 0f || asked > EditorApplication.timeSinceStartup) SessionState.SetFloat("UWProbe.AskedAt", (float)EditorApplication.timeSinceStartup);
                else if (EditorApplication.timeSinceStartup - asked > 60) { Note("Play never started, probe dropped"); SessionState.SetString(KeyState, ""); SessionState.EraseFloat("UWProbe.AskedAt"); Release(); }
                return;
            }
            SessionState.EraseFloat("UWProbe.AskedAt");
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null) return;
            SessionState.SetString(KeyState, st + "-running");
            run.StartCoroutine(Probe(run, st));
        }

        static void Release()
        {
            try { if (File.Exists(LockPath) && File.ReadAllText(LockPath).StartsWith(Me + " ")) File.Delete(LockPath); } catch (IOException) { }
        }

        static IEnumerator Jump(RunDirector run, string id)
        {
            if (run.Room.id != id) run.DevJump(id);
            for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
            while (run.Transitioning) yield return null;
            yield return new WaitForSeconds(2.5f);
        }

        static IEnumerator Probe(RunDirector run, string mode)
        {
            bool full = mode == "full", screen = mode == "screen";
            run.Player.DebugInvulnerable = true;
            RunDirector.DevNoEnemies = true;
            foreach (var e in run.Enemies) if (e) Object.Destroy(e.gameObject);
            run.Enemies.Clear();
            Note($"UW_Brightness pref = {PlayerPrefs.GetFloat("UW_Brightness", 0f):0.00}");
            yield return Jump(run, "underworld");
            foreach (var e in run.Enemies) if (e) Object.Destroy(e.gameObject);
            run.Enemies.Clear();
            yield return null;
            // what the post stack resolves to
            var stack = VolumeManager.instance.stack;
            var ca = stack.GetComponent<ColorAdjustments>(); var bl = stack.GetComponent<Bloom>(); var tm = stack.GetComponent<Tonemapping>();
            Note($"post: exposure {ca.postExposure.value:0.00} bloom int {bl.intensity.value:0.00} thr {bl.threshold.value:0.00} scatter {bl.scatter.value:0.00} tonemap {tm.mode.value}");
            foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var p = v.HasInstantiatedProfile() ? v.profile : v.sharedProfile;
                string s = $"  vol {v.name} p{v.priority} w{v.weight}";
                if (p && p.TryGet(out ColorAdjustments c2) && c2.postExposure.overrideState) s += $" exp {c2.postExposure.value:0.00}";
                if (p && p.TryGet(out Bloom b2) && b2.active) s += $" bloom {b2.intensity.value:0.00}/{b2.threshold.value:0.00}";
                Note(s);
            }
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled).OrderByDescending(l => l.intensity).Take(8);
            foreach (var l in lights) Note($"  light {l.name} {l.type} int {l.intensity:0.00} range {l.range:0} col {l.color}");

            // player height
            var pb = new Bounds(); bool any = false;
            foreach (var r in run.Player.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.name.ToLower().Contains("sword") || r.name.ToLower().Contains("ring") || r.name.ToLower().Contains("shadow")) continue;
                if (any) pb.Encapsulate(r.bounds); else { pb = r.bounds; any = true; }
            }
            var smr = run.Player.GetComponentInChildren<SkinnedMeshRenderer>();
            Note($"player height (renderer bounds, excl. sword) {pb.size.y:0.00} m; skinned '{(smr ? smr.name : "-")}' {(smr ? smr.bounds.size.y : 0):0.00} m; root scale {run.Player.transform.lossyScale.y:0.00}");
            var cc0 = run.Player.GetComponent<CharacterController>();
            if (cc0) Note($"player capsule height {cc0.height * run.Player.transform.lossyScale.y:0.00} m radius {cc0.radius * run.Player.transform.lossyScale.x:0.00}");

            // rocks / sea renderers
            var rockR = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial && r.sharedMaterial.shader.name == "Underworld/LavaRock").ToList();
            var seaR = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.sharedMaterial && (r.sharedMaterial.shader.name.Contains("LavaScan") || r.sharedMaterial.shader.name.Contains("LavaField") || r.sharedMaterial.name.Contains("LavaSea"))).ToList();
            Note($"lava rocks {rockR.Count} renderers, sea {seaR.Count} ({string.Join(",", seaR.Select(r => r.sharedMaterial.shader.name).Distinct())})");
            if (rockR.Count > 0) { var m = rockR[0].sharedMaterial; Note($"  rock mat {m.name} GlowInt {m.GetFloat("_GlowInt")} GlowFall {m.GetFloat("_GlowFall")} AmbLift {m.GetColor("_AmbLift")}"); }

            if (mode == "bisect") { yield return BisectRun(run); Note("done"); SessionState.SetString(KeyState, "stopping"); yield break; }
            var rig = run.Rig;
            const int W = 960, H = 540;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var spots = new (string name, Vector3 at)[]
            {
                ("gate", new Vector3(0f, 0f, 24f)), ("portal", new Vector3(20f, 0f, 26f)), ("southrim", new Vector3(0f, 0f, -27f)),
                ("eastrim", new Vector3(42f, 0f, 0f)), ("centre", new Vector3(0f, 0f, 0f)),
            };
            if (screen) spots = spots.Take(2).ToArray();
            if (mode == "rooms") spots = spots.Take(0).ToArray();
            string[] variants = full ? new[] { "base", "rocks0", "sea0", "bloom0", "all0" }
                : screen ? new[] { "base", "rocks0", "sea0", "glight0", "flight0", "fx0", "bloom0" } : new[] { "base" };
            float[] zooms = screen ? new[] { 0.8f, 1.6f } : new[] { 0.35f, 0.8f, 1.6f };
            var allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.enabled).ToList();
            var allFx = Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToList();
            var mpb = new MaterialPropertyBlock();
            var worst = (lum: -1f, spot: "", z: 0f, frame: (byte[])null);
            foreach (var spot in spots)
            {
                var pt = run.Player.transform; var cc = pt.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
                var p = spot.at; p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, Ground.GroundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : pt.position.y;
                pt.position = p; if (cc) cc.enabled = true;
                yield return new WaitForSeconds(0.4f);
                foreach (var v in variants)
                {
                    bool rocks0 = v == "rocks0" || v == "all0", sea0 = v == "sea0" || v == "all0", bloom0 = v == "bloom0" || v == "all0";
                    foreach (var r in rockR) { if (!r) continue; if (rocks0) { r.GetPropertyBlock(mpb); mpb.SetFloat("_GlowInt", 0f); mpb.SetColor("_AmbLift", Color.black); r.SetPropertyBlock(mpb); } else r.SetPropertyBlock(null); }
                    foreach (var r in seaR) { if (!r) continue; if (sea0) { r.GetPropertyBlock(mpb); mpb.SetFloat("_EmitInt", 0f); r.SetPropertyBlock(mpb); } else r.SetPropertyBlock(null); }
                    foreach (var l in allLights) if (l) l.enabled = !((v == "glight0" && l.name == "GateRoofLight") || (v == "flight0" && l.type == LightType.Point && l.name != "PlayerLightPool"));
                    foreach (var r in allFx) if (r) r.enabled = v != "fx0";
                    var blooms = new List<Bloom>();
                    foreach (var vol in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                        if (vol.profile && vol.profile.TryGet(out Bloom b) && b.active) { if (bloom0) { b.active = false; blooms.Add(b); } }
                    foreach (float z in zooms)
                    {
                        rig.Zoom = z; rig.Snap();
                        for (int f = 0; f < 4; f++) yield return null;
                        // across a breathing cycle: 8 samples over ~6 s (base variant), 3 for the rest
                        int n = v == "base" ? 8 : 3;
                        float sumMean = 0, maxMean = 0, maxWhite = 0;
                        for (int k = 0; k < n; k++)
                        {
                            Color32[] px;
                            if (screen)
                            {
                                yield return new WaitForEndOfFrame();
                                var sc = ScreenCapture.CaptureScreenshotAsTexture();
                                px = sc.GetPixels32();
                                if (k == 0 && (v == "base" || z > 1f)) File.WriteAllBytes(Path.Combine(Dir, "rr_shots", $"uw_screen_{spot.name}_{v}_z{z:0.00}.jpg"), sc.EncodeToJPG(85));
                                Object.Destroy(sc);
                            }
                            else
                            {
                                var cam = rig.Cam;
                                cam.targetTexture = rt; cam.Render();
                                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                                cam.targetTexture = null;
                                px = tex.GetPixels32();
                            }
                            double sum = 0; int white = 0;
                            for (int i = 0; i < px.Length; i++)
                            {
                                float l = (0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b) / 255f;
                                sum += l; if (l > 0.85f) white++;
                            }
                            float mean = (float)(sum / px.Length), wf = white / (float)px.Length;
                            sumMean += mean; maxMean = Mathf.Max(maxMean, mean); maxWhite = Mathf.Max(maxWhite, wf);
                            if (v == "base" && !screen && mean > worst.lum) worst = (mean, spot.name, z, tex.EncodeToJPG(90));
                            if (k < n - 1) yield return new WaitForSeconds(v == "base" ? 0.8f : 0.6f);
                        }
                        Note($"{spot.name,-8} z{z:0.00} {v,-6} meanLum {sumMean / n:0.000} max {maxMean:0.000} white>0.85 {maxWhite * 100f:0.0}%");
                    }
                    foreach (var b in blooms) b.active = true;
                    foreach (var l in allLights) if (l) l.enabled = true;
                    foreach (var r in allFx) if (r) r.enabled = true;
                }
                foreach (var r in rockR) if (r) r.SetPropertyBlock(null);
                foreach (var r in seaR) if (r) r.SetPropertyBlock(null);
            }
            if (worst.frame != null)
            {
                string name = full ? "uw_whiteout_before.jpg" : "uw_whiteout_fix.jpg";
                File.WriteAllBytes(Path.Combine(Dir, "rr_shots", name), worst.frame);
                Note($"brightest base frame: {worst.spot} z{worst.z:0.00} meanLum {worst.lum:0.000} -> rr_shots/{name}");
            }
            rt.Release(); Object.Destroy(rt); Object.Destroy(tex);

            if (full || mode == "rooms")
            {
                Note("rooms (config box = camera/arena box; walkable = connected ground cells from the room centre, 1 m grid, capsule clear):");
                foreach (var room in run.config.rooms)
                {
                    yield return Jump(run, room.id);
                    foreach (var e in run.Enemies) if (e) Object.Destroy(e.gameObject);
                    run.Enemies.Clear();
                    yield return null;
                    Note($"  {room.id,-10} {room.displayName} boss={(room.bossPrefab ? room.bossPrefab.name : "-")} devOnly={room.devOnly} " + Walkable(run));
                }
            }
            Note("done");
            SessionState.SetString(KeyState, "stopping");
        }

        static IEnumerator Shot(System.Action<float, float> res, string save = null)
        {
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            var sc = ScreenCapture.CaptureScreenshotAsTexture();
            var px = sc.GetPixels32();
            double sum = 0; int white = 0;
            for (int i = 0; i < px.Length; i++) { float l = (0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b) / 255f; sum += l; if (l > 0.85f) white++; }
            if (save != null) File.WriteAllBytes(Path.Combine(Dir, "rr_shots", save), sc.EncodeToJPG(85));
            Object.Destroy(sc);
            res((float)(sum / px.Length), white / (float)px.Length);
        }

        static IEnumerator BisectRun(RunDirector run)
        {
            var pt = run.Player.transform; var cc = pt.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            var p = new Vector3(20f, 0f, 26f); p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f, Ground.GroundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : 0f;
            pt.position = p; if (cc) cc.enabled = true;
            run.Rig.Zoom = 1.6f; run.Rig.Snap();
            yield return new WaitForSeconds(0.6f);
            float m = 0, w = 0;
            yield return Shot((a, b) => { m = a; w = b; }, "uw_bisect_base.jpg");
            Note($"bisect base meanLum {m:0.000} white {w * 100f:0.0}%");
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var d = c.GetUniversalAdditionalCameraData();
                Note($"  camera {c.name} enabled {c.enabled} depth {c.depth} type {d.renderType} post {d.renderPostProcessing} hdr {c.allowHDR} mask {c.cullingMask} target {(c.targetTexture ? c.targetTexture.name : "-")} pos {c.transform.position}");
            }
            var groups = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.sharedMaterial)
                .GroupBy(r => r.sharedMaterial.shader.name).ToList();
            foreach (var g in groups)
            {
                foreach (var r in g) r.enabled = false;
                yield return Shot((a, b) => { m = a; w = b; });
                foreach (var r in g) if (r) r.enabled = true;
                var big = g.OrderByDescending(r => r.bounds.size.magnitude).First();
                Note($"  off {g.Key,-44} n{g.Count(),4} -> meanLum {m:0.000} white {w * 100f:0.0}%   (biggest {big.name} {big.bounds.size.magnitude:0}m mat {big.sharedMaterial.name})");
            }
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(x => x.enabled && (x.GetType().Namespace ?? "").StartsWith("Underworld") || x is Volume).ToList())
            {
                if (!mb || !mb.enabled) continue;
                mb.enabled = false;
                yield return Shot((a, b) => { m = a; w = b; });
                if (mb) mb.enabled = true;
                Note($"  off comp {mb.GetType().Name} on {mb.name} -> meanLum {m:0.000} white {w * 100f:0.0}%");
            }
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(x => x.enabled).ToList())
            {
                l.enabled = false;
                yield return Shot((a, b) => { m = a; w = b; });
                l.enabled = true;
                if (w < 0.4f) Note($"  off light {l.name} {l.type} {l.intensity:0.0} -> meanLum {m:0.000} white {w * 100f:0.0}%");
            }
        }

        // Walkable floor inside the room's wall box (RoomBuilder puts colliding walls on the box edges): 0.5 m grid,
        // flood fill from the player's spawn; a cell joins if ground is under it, it rises/falls at most 0.45 m from the
        // neighbour (about 42 deg, the CharacterController limit) and a player capsule (r 0.38, 1.7 m) fits there.
        static string Walkable(RunDirector run)
        {
            var c = run.AreaCenter; var size = run.AreaSize;
            int player = Ground.ActorLayer;
            int block = 0;
            for (int l = 0; l < 32; l++) if (l != player && l != Ground.WallLayer && !Physics.GetIgnoreLayerCollision(player, l)) block |= 1 << l;
            const float step = 0.5f;
            int nx = Mathf.RoundToInt(size.x / step), nz = Mathf.RoundToInt(size.y / step);
            float x0 = c.x - size.x / 2f, z0 = c.z - size.y / 2f;
            var h = new float[nx, nz]; var ok = new bool[nx, nz];
            var blockers = new Dictionary<string, int>();
            var cols = new Collider[4];
            float top = run.Player.transform.position.y + 30f;
            string centreHit = "";
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = new Vector3(x0 + (i + 0.5f) * step, top, z0 + (j + 0.5f) * step);
                    if (!Physics.Raycast(p, Vector3.down, out var hit, 120f, Ground.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    if (i == nx / 2 && j == nz / 2) centreHit = $"{hit.collider.name}@{hit.point.y:0.0}";
                    var g = hit.point;
                    int n = Physics.OverlapCapsuleNonAlloc(g + Vector3.up * 0.45f, g + Vector3.up * 1.32f, 0.38f, cols, block, QueryTriggerInteraction.Ignore);
                    if (n > 0) { var k = cols[0].name; blockers[k] = blockers.TryGetValue(k, out var v) ? v + 1 : 1; continue; }
                    h[i, j] = g.y; ok[i, j] = true;
                }
            var sp = run.Player.transform.position;
            int si = Mathf.Clamp(Mathf.FloorToInt((sp.x - x0) / step), 0, nx - 1), sj = Mathf.Clamp(Mathf.FloorToInt((sp.z - z0) / step), 0, nz - 1);
            if (!ok[si, sj]) { float best = 1e9f; for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) if (ok[i, j]) { float d = (i - si) * (i - si) + (j - sj) * (j - sj); if (d < best) { best = d; } } }
            if (!ok[si, sj]) for (int r = 1; r < 20 && !ok[si, sj]; r++) for (int i = Mathf.Max(0, si - r); i <= Mathf.Min(nx - 1, si + r); i++) for (int j = Mathf.Max(0, sj - r); j <= Mathf.Min(nz - 1, sj + r); j++) if (ok[i, j]) { si = i; sj = j; }
            string tops = string.Join(", ", blockers.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key} {x.Value * step * step:0}m2"));
            if (!ok[si, sj]) return $"box {size.x:0.#}x{size.y:0.#}; no walkable cell near the player (player at {sp}, centre ray hit {centreHit}); blockers: {tops}";
            var seen = new bool[nx, nz]; var q = new Queue<Vector2Int>();
            q.Enqueue(new Vector2Int(si, sj)); seen[si, sj] = true;
            int cnt = 0, mi = si, ma = si, mj = sj, mb = sj;
            var d4 = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (q.Count > 0)
            {
                var v = q.Dequeue(); cnt++;
                mi = Mathf.Min(mi, v.x); ma = Mathf.Max(ma, v.x); mj = Mathf.Min(mj, v.y); mb = Mathf.Max(mb, v.y);
                foreach (var d in d4)
                {
                    int a = v.x + d.x, b = v.y + d.y;
                    if (a < 0 || b < 0 || a >= nx || b >= nz || seen[a, b] || !ok[a, b]) continue;
                    if (Mathf.Abs(h[a, b] - h[v.x, v.y]) > 0.45f) continue;
                    seen[a, b] = true; q.Enqueue(new Vector2Int(a, b));
                }
            }
            float area = cnt * step * step;
            return $"box {size.x:0.#}x{size.y:0.#} centre ({c.x:0},{c.z:0}); walkable extent {(ma - mi + 1) * step:0.#}x{(mb - mj + 1) * step:0.#} m (x {x0 + mi * step:0.#}..{x0 + (ma + 1) * step:0.#}, z {z0 + mj * step:0.#}..{z0 + (mb + 1) * step:0.#}), area {area:0} m2 = {100f * area / (size.x * size.y):0}% of box; centre ray {centreHit}; blockers: {tops}";
        }
    }
}
