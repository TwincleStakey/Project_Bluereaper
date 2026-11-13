// Lets the room run be checked without clicking: write ArtSource/rr_cmd.txt with one line
//   clip <seconds> [roomId]
//   census <roomId>              (logs the visible renderers with the most triangles, and the render passes)
//   stop                         (leaves play mode at any time)
//   menu <menu path>             (runs an editor menu item, e.g. a scene builder; result in the log)
//   perf <name> <roomId> [...]   (uncapped frame cost per field, with shadows/post/foliage/cams/lights off in turn;
//                                 ArtSource/rr_perf_<name>.csv, see PerfProbe)
//   inkkeys <roomId>             (presses F10 four times in that room, one capture per screen filter, to rr_shots)
//   shots <roomId>[@x,z] [...]   (each room from the RoomRun camera at a few zoom levels, to ArtSource/rr_shots;
//                                 @x,z stands the player there first; a trailing ! keeps firing at the nearest enemy)
// and the editor enters play mode, jumps to the room, walks the player in a figure eight and saves
// frames to ArtSource/rr_clip, then leaves play mode. Progress goes to ArtSource/rr_log.txt.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoomRun
{
    [InitializeOnLoad]
    public static class RoomRunRemote
    {
        static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");
        static string Cmd => Path.Combine(Dir, "rr_cmd.txt");
        static string Log => Path.Combine(Dir, "rr_log.txt");
        const string KeySecs = "RoomRun.Clip.Seconds", KeyRoom = "RoomRun.Clip.Room", KeyState = "RoomRun.Clip.State";
        static double next;

        static RoomRunRemote()
        {
            EditorApplication.update += Tick;
        }

        static void Note(string s)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Log, $"[{System.DateTime.Now:HH:mm:ss}] {s}\n");
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            string state = SessionState.GetString(KeyState, "");

            // "stop" works at any time: leaves play mode and clears a run that a mid-play recompile left hanging
            if (File.Exists(Cmd) && File.ReadAllText(Cmd).Trim() == "stop")
            {
                File.Delete(Cmd);
                SessionState.SetString(KeyState, "");
                Note("stop");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }

            if (state == "" && File.Exists(Cmd) && !EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                var parts = File.ReadAllText(Cmd).Trim().Split(' ');
                File.Delete(Cmd);
                if (parts[0] == "menu")
                {
                    string item = string.Join(" ", parts, 1, parts.Length - 1);
                    bool ok = false;
                    try { ok = EditorApplication.ExecuteMenuItem(item); }
                    catch (System.Exception e) { Note($"menu {item}: {e.GetType().Name} {e.Message}"); }
                    Note($"menu {item}: {(ok ? "done" : "failed")}");
                    return;
                }
                if (parts[0] == "census" && parts.Length > 1)
                {
                    SessionState.SetString(KeyRoom, parts[1]);
                    SessionState.SetString(KeyState, "census");
                    Note($"census {parts[1]}: entering play mode");
                    EditorApplication.isPlaying = true;
                    return;
                }
                if (parts[0] == "perf" && parts.Length > 2)
                {
                    SessionState.SetString(KeyRoom, string.Join(" ", parts, 1, parts.Length - 1));
                    SessionState.SetString(KeyState, "perf");
                    Note($"perf {SessionState.GetString(KeyRoom, "")}: entering play mode");
                    EditorApplication.isPlaying = true;
                    return;
                }
                if (parts[0] == "inkkeys" && parts.Length > 1)
                {
                    SessionState.SetString(KeyRoom, parts[1]);
                    SessionState.SetString(KeyState, "inkkeys");
                    Note($"inkkeys {parts[1]}: entering play mode");
                    EditorApplication.isPlaying = true;
                    return;
                }
                if (parts[0] == "shots")
                {
                    SessionState.SetString(KeyRoom, string.Join(" ", parts, 1, parts.Length - 1));
                    SessionState.SetString(KeyState, "shots");
                    Note($"shots {SessionState.GetString(KeyRoom, "")}: entering play mode");
                    EditorApplication.isPlaying = true;
                    return;
                }
                if (parts[0] == "devkeys")
                {
                    SessionState.SetString(KeyState, "devkeys");
                    Note("devkeys: entering play mode");
                    EditorApplication.isPlaying = true;
                    return;
                }
                if (parts.Length < 2 || parts[0] != "clip" || !float.TryParse(parts[1], out float secs)) { Note("unknown command"); return; }
                SessionState.SetFloat(KeySecs, secs);
                SessionState.SetString(KeyRoom, parts.Length > 2 ? parts[2] : "");
                SessionState.SetString(KeyState, "starting");
                Note($"clip {secs}s room={(parts.Length > 2 ? parts[2] : "start")}: entering play mode");
                EditorApplication.isPlaying = true;
                return;
            }

            if (state == "starting" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) return;
                string room = SessionState.GetString(KeyRoom, "");
                if (room != "" && run.Room.id != room) { run.JumpTo(room); return; }
                var rec = run.gameObject.AddComponent<ClipRecorder>();
                rec.folder = Path.Combine(Dir, "rr_clip");
                rec.seconds = SessionState.GetFloat(KeySecs, 10f);
                rec.Finished = () => SessionState.SetString(KeyState, "stopping");
                SessionState.SetString(KeyState, "recording");
                Note($"recording in {run.Room.id}");
                return;
            }

            if (state == "census" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) { Waiting(run); return; }
                SessionState.SetString(KeyState, "shots-running");
                run.StartCoroutine(Census(run, SessionState.GetString(KeyRoom, "")));
                return;
            }

            if (state == "perf" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) { Waiting(run); return; }
                SessionState.SetString(KeyState, "shots-running");
                var args = SessionState.GetString(KeyRoom, "").Split(' ');
                var probe = run.gameObject.AddComponent<PerfProbe>();
                // rooms, then optional variants after "--": perf <name> <rooms...> -- base off:EnemyController ...
                int dash = System.Array.IndexOf(args, "--");
                probe.rooms = (dash < 0 ? args.Skip(1) : args.Skip(1).Take(dash - 1)).ToArray();
                if (dash >= 0) probe.variants = args.Skip(dash + 1).ToArray();
                probe.outFile = Path.Combine(Dir, $"rr_perf_{args[0]}.csv");
                probe.Note = s => Note("perf " + s);
                probe.Finished = () => SessionState.SetString(KeyState, "stopping");
                return;
            }

            if (state == "inkkeys" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) return;
                SessionState.SetString(KeyState, "shots-running");
                run.StartCoroutine(InkKeys(run, SessionState.GetString(KeyRoom, "")));
                return;
            }

            if (state == "shots" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) { Waiting(run); return; }
                SessionState.SetString(KeyState, "shots-running");
                run.StartCoroutine(Shots(run, SessionState.GetString(KeyRoom, "").Split(' ')));
                return;
            }

            if (state == "devkeys" && EditorApplication.isPlaying)
            {
                var run = RunDirector.Instance;
                if (!run || run.Transitioning || run.Room == null) return;
                SessionState.SetString(KeyState, "devkeys-running");
                run.StartCoroutine(DevKeys(run));
                return;
            }

            if (state == "stopping")
            {
                SessionState.SetString(KeyState, "");
                Note("clip done");
                EditorApplication.isPlaying = false;
                return;
            }

            // Play mode was left by hand during a clip.
            if (state == "shots-running" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
            if (state == "devkeys-running" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
            if (state != "" && state != "starting" && state != "devkeys" && state != "shots" && state != "inkkeys" && state != "perf" && state != "census" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
        }

        // Presses F10 through the Input System (as a player would) and captures the screen after each press.
        static System.Collections.IEnumerator InkKeys(RunDirector run, string room)
        {
            string dir = Path.Combine(Dir, "rr_shots");
            Directory.CreateDirectory(dir);
            run.Player.DebugInvulnerable = true;
            if (run.Room.id != room) run.DevJump(room);
            for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
            while (run.Transitioning) yield return null;
            yield return new WaitForSeconds(2.5f);
            var modeField = System.Type.GetType("InkFilters.InkFilter, InkFilters")?.GetField("Mode");
            int filters = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Count(m => m.GetType().Name == "InkFilter");
            Note($"inkkeys {room}: InkFilter objects in play = {filters}");
            var kb = UnityEngine.InputSystem.Keyboard.current;
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(0.6f);
                // the screen filter is a pass on the real frame, so grab what the Game view shows (a camera render into a
                // texture skips it)
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                string mode = modeField != null ? modeField.GetValue(null).ToString() : "?";
                File.WriteAllBytes(Path.Combine(dir, $"{room}_ink{i}_{mode}.jpg"), shot.EncodeToJPG(90));
                Object.Destroy(shot);
                Note($"inkkeys {room}: capture {i} mode={mode}");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F10));
                yield return null; yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return null;
            }
            Note($"inkkeys {room}: after 4 presses mode={(modeField != null ? modeField.GetValue(null).ToString() : "?")}");
            Object.Destroy(rt); Object.Destroy(tex);
            SessionState.SetString(KeyState, "stopping");
        }

        // Stands still and keeps firing at the nearest enemy (captures of glowing shots).
        sealed class Firer : ICommandSource
        {
            readonly RunDirector run;
            public Firer(RunDirector r) => run = r;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                Vector3 best = p.transform.forward; float bd = float.MaxValue;
                foreach (var e in run.Enemies)
                {
                    if (!e) continue;
                    var d = e.transform.position - p.transform.position; d.y = 0;
                    if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = d; }
                }
                return new PlayerCommand { move = Vector2.zero, aimDir = best.normalized, fire = true };
            }
        }

        static System.Collections.IEnumerator Census(RunDirector run, string room)
        {
            run.Player.DebugInvulnerable = true;
            // census grass@3.5 : at that wheel zoom
            float zoom = CameraRig.FixedZoom;
            int at = room.IndexOf('@');
            if (at >= 0) { float.TryParse(room.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out zoom); room = room.Substring(0, at); }
            if (run.Room.id != room) run.DevJump(room);
            for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
            while (run.Transitioning) yield return null;
            yield return new WaitForSeconds(3f);
            run.Rig.Zoom = zoom; run.Rig.Snap();
            yield return null;
            var cam = run.Rig.Cam;
            // instanced vegetation is not a Renderer: list it per mesh
            var inst = new System.Collections.Generic.Dictionary<string, long>();
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects())
                    foreach (var li in root.GetComponentsInChildren<LookStudyInstancer>())
                        foreach (var kv in li.VisibleTris(cam)) inst[kv.Key] = (inst.TryGetValue(kv.Key, out var o) ? o : 0) + kv.Value;
            inst.TryGetValue("~draw calls", out long instCalls); inst.Remove("~draw calls");
            int nInst = 0;
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects()) nInst += root.GetComponentsInChildren<LookStudyInstancer>().Length;
            Note($"census {room}@{zoom}: instanced {inst.Values.Sum() / 1000}k tris, {instCalls} draw calls, {nInst} instancers");
            foreach (var kv in inst.OrderByDescending(k => k.Value).Take(12)) Note($"  inst {kv.Key} {kv.Value / 1000}k");
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var rows = new System.Collections.Generic.List<(long tris, string what)>();
            long total = 0, shadowTotal = 0;
            var all = new System.Collections.Generic.List<Renderer>();
            // stage meshes are DontSave objects, which FindObjectsByType skips: walk the scenes instead
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<Renderer>());
            all.AddRange(run.GetComponentsInChildren<Renderer>());
            foreach (var r in all)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Mesh m = r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : r is SkinnedMeshRenderer sk ? sk.sharedMesh : null;
                if (!m) continue;
                long t = 0; for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3;
                bool inView = GeometryUtility.TestPlanesAABB(planes, r.bounds);
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowTotal += t;
                if (!inView) continue;
                total += t;
                var b = r.bounds;
                rows.Add((t, $"{r.name} [{m.name}] {t / 1000}k tris, span {Mathf.Max(b.size.x, b.size.z):0}m, mats {r.sharedMaterials.Length} ({(r.sharedMaterial ? r.sharedMaterial.shader.name : "-")}), shadows {r.shadowCastingMode}"));
            }
            rows.Sort((x, y) => y.tris.CompareTo(x.tris));
            Note($"census {room}: in view {total / 1000}k tris in {rows.Count} renderers; shadow casters (anywhere) {shadowTotal / 1000}k tris");
            for (int i = 0; i < Mathf.Min(15, rows.Count); i++) Note("  " + rows[i].what);
            var types = new System.Collections.Generic.Dictionary<string, int>();
            var behaviours = new System.Collections.Generic.List<MonoBehaviour>();
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects()) behaviours.AddRange(root.GetComponentsInChildren<MonoBehaviour>());
            foreach (var mb in behaviours) { if (!mb || !mb.isActiveAndEnabled) continue; var n = mb.GetType().Name; types[n] = types.TryGetValue(n, out var c) ? c + 1 : 1; }
            int ps = 0, psParticles = 0;
            for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(si).GetRootGameObjects())
                    foreach (var p in root.GetComponentsInChildren<ParticleSystem>()) { if (p.isPlaying) { ps++; psParticles += p.particleCount; } }
            Note($"  renderers {all.Count}, particle systems playing {ps} ({psParticles} particles), terrains {Terrain.activeTerrains.Length}, scripts: " +
                 string.Join(", ", types.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} x{kv.Value}")));
            Note($"  camera: HDR {cam.allowHDR}, MSAA {cam.allowMSAA}, size {cam.pixelWidth}x{cam.pixelHeight}, far {cam.farClipPlane}");
            SessionState.SetString(KeyState, "stopping");
        }

        // Every 10 s while a command waits for the room run, say what it is waiting on.
        static double nextWaitNote;
        static void Waiting(RunDirector run)
        {
            if (EditorApplication.timeSinceStartup < nextWaitNote) return;
            nextWaitNote = EditorApplication.timeSinceStartup + 10;
            Note(!run ? $"waiting: no RunDirector (scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}, paused={EditorApplication.isPaused}, timeScale={Time.timeScale})"
                      : $"waiting: transitioning={run.Transitioning} room={(run.Room != null ? run.Room.id : "null")} scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path} paused={EditorApplication.isPaused} frame={Time.frameCount}");
        }

        static readonly float[] ShotZooms = { 1f, 1.3f, 1.8f, 2.6f };

        static System.Collections.IEnumerator Shots(RunDirector run, string[] rooms)
        {
            string dir = Path.Combine(Dir, "rr_shots");
            Directory.CreateDirectory(dir);
            run.Player.DebugInvulnerable = true;
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            float[] zooms = ShotZooms;
            foreach (var token in rooms)
            {
                if (string.IsNullOrEmpty(token)) continue;
                // "z=1,1.2,1.4" sets the zoom levels for the rooms after it
                if (token.StartsWith("z="))
                {
                    zooms = System.Array.ConvertAll(token.Substring(2).Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                }
                string id = token, tag = token;
                Vector3? at = null;
                bool firing = id.EndsWith("!");
                if (firing) { id = id.TrimEnd('!'); tag = id + "_fire"; }
                // "grass+12*" : the extra enemies hold still where they spawned (looks check: they do not pile onto the player)
                bool still = id.EndsWith("*");
                if (still) { id = id.TrimEnd('*'); tag = id + "_still"; }
                // "grass+150!" : 150 extra enemies around the player before the shots
                int swarm = 0, plus = id.IndexOf('+');
                if (plus > 0) { int.TryParse(id.Substring(plus + 1), out swarm); id = id.Substring(0, plus); tag = id + $"_swarm{swarm}" + (firing ? "_fire" : "") + (still ? "_still" : ""); }
                int a = token.IndexOf('@');
                if (a > 0)
                {
                    id = token.Substring(0, a);
                    var xz = token.Substring(a + 1).Split(',');
                    at = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                    tag = $"{id}_{xz[0]}_{xz[1]}" + (firing ? "_fire" : "");
                }
                if (run.Room.id != id) run.DevJump(id);
                for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
                while (run.Transitioning) yield return null;
                yield return new WaitForSeconds(2.5f);
                if (at.HasValue)
                {
                    var pt = run.Player.transform;
                    var cc = pt.GetComponent<CharacterController>();
                    if (cc) cc.enabled = false;
                    var p = at.Value;
                    p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var hit, 60f) ? hit.point.y : pt.position.y;
                    pt.position = p;
                    if (cc) cc.enabled = true;
                    yield return new WaitForSeconds(0.5f);
                }
                if (swarm > 0 && still)
                {
                    run.DevSwarm(swarm);
                    foreach (var e in run.Enemies) if (e) e.enabled = false; // the Animator keeps playing their walk
                    yield return new WaitForSeconds(1f);
                }
                else if (swarm > 0) { run.DevSwarm(swarm); yield return new WaitForSeconds(4f); }
                var keepSource = run.Player.Source;
                if (firing) { run.Player.Source = new Firer(run); yield return new WaitForSeconds(1.2f); }
                var rig = run.Rig;
                // other fields' stale DontSave renderers (no scene) draw over every stage in the shared editor; hide them
                var orphans = new List<Renderer>();
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    if (r && !EditorUtility.IsPersistent(r) && r.enabled && !r.gameObject.scene.IsValid()) { r.enabled = false; orphans.Add(r); }
                if (orphans.Count > 0) Note($"  hid {orphans.Count} orphan renderers from other fields");
                foreach (float z in zooms)
                {
                    rig.Zoom = z; rig.Snap();
                    for (int f = 0; f < 4; f++) yield return null;
                    var cam = rig.Cam;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, $"{tag}_z{z:0.0}.jpg"), tex.EncodeToJPG(90));
                }
                foreach (var r in orphans) if (r) r.enabled = true;
                rig.Zoom = CameraRig.FixedZoom;
                if (firing) run.Player.Source = keepSource;
                // what the post stack actually resolves to for the game camera (diagnosing the dark underworld)
                var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
                var cadj = stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
                var vig = stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
                var vols = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None)
                    .Select(v => $"{v.name}(p{v.priority} w{v.weight:0.##} {(v.isGlobal ? "global" : "local")} layer{v.gameObject.layer})");
                var camData = rig.Cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                Note($"  post stack: exposure {(cadj ? cadj.postExposure.value : -99):0.00}, saturation {(cadj ? cadj.saturation.value : 0):0}, vignette {(vig ? vig.intensity.value : 0):0.00}; volumeMask {(camData ? camData.volumeLayerMask.value : -1)} trigger {(camData && camData.volumeTrigger ? camData.volumeTrigger.name : "cam")}; volumes: {string.Join(", ", vols)}");
                Note($"  fog {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity:0.0000} colour {RenderSettings.fogColor}; ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight}");
                Note($"shots {tag} done (fps {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0})");
            }
            Object.Destroy(rt); Object.Destroy(tex);
            SessionState.SetString(KeyState, "stopping");
        }

        // Presses each field key through the Input System, waits for the room, lets the encounter start, logs what it finds.
        static System.Collections.IEnumerator DevKeys(RunDirector run)
        {
            int errors = 0;
            Application.LogCallback onLog = (msg, stack, type) => { if (type == LogType.Error || type == LogType.Exception) { errors++; Note("ERR " + msg); } };
            Application.logMessageReceived += onLog;
            run.Player.DebugInvulnerable = true;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var keys = new[] { UnityEngine.InputSystem.Key.F6, UnityEngine.InputSystem.Key.F7, UnityEngine.InputSystem.Key.F6, UnityEngine.InputSystem.Key.F2,
                UnityEngine.InputSystem.Key.F3, UnityEngine.InputSystem.Key.F4, UnityEngine.InputSystem.Key.F5, UnityEngine.InputSystem.Key.F1 };
            foreach (var key in keys)
            {
                string before = run.Room.id;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                yield return null; yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
                while (run.Transitioning) yield return null;
                yield return new WaitForSeconds(2.5f);
                var panel = GameObject.Find("FieldKeys");
                Note($"{key}: {before} -> {run.Room.id} player={run.Player.transform.position} area={run.AreaCenter}/{run.AreaSize} inCombat={run.InCombat} enemies={run.Enemies.Count} hp={run.Player.Health.current} panelKeys={(panel ? panel.GetComponent<UnityEngine.UI.Text>().text.Replace('\n', '|') : "none")}");
            }
            Application.logMessageReceived -= onLog;
            Note($"devkeys done, errors={errors}");
            SessionState.SetString(KeyState, "stopping");
        }
    }
}
