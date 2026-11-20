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
            // 11-19: a capture's ShadowPass override (shots token "sp=0|1") never outlives it; the default is ON (RR.ShadowPass)
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingPlayMode || s == PlayModeStateChange.EnteredEditMode) ShadowPass.ForceOff(); };
            // 11-14: the third freeze (02:24) came from a script compile requested while a remote run was in Play (other
            // threads keep saving scripts). Compile only after Play ends (0 = recompile and continue playing, the
            // default, which reloads the domain mid-Play; 1 = recompile after finished playing).
            if (EditorPrefs.GetInt("ScriptCompilationDuringPlay", 0) != 1) { EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1); Debug.Log("[RoomRunRemote] script changes now compile after Play ends"); }
            // 11-13: reed field ground/water tiles leaked out of Play (old DontSave MeshChunker) drew over every scene
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                int n = MeshChunker.PurgeLeaked();
                if (n > 0) Note($"purged {n} leaked MeshChunker tiles (outside any scene)");
            };
        }

        // 11-14 (two editor freezes, 11-13 23:23 and 00:54: a remote command started a scene build / Play while Play was
        // still entering or exiting). Shared by every *_cmd remote (same code copied into each): nothing starts while Play
        // changes, for 15 s after any Play change or domain reload, while compiling or importing, or while another remote
        // holds ArtSource/remote_play.lock (its Play run; a lock older than 10 min counts as stale).
        static class Gate
        {
            static double lastChange = EditorApplication.timeSinceStartup;
            static Gate() { EditorApplication.playModeStateChanged += _ => lastChange = EditorApplication.timeSinceStartup; }
            static string LockPath => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource", "remote_play.lock");
            public static bool Ready(string me)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
                if (EditorApplication.timeSinceStartup - lastChange < 15.0) return false;
                try
                {
                    if (File.Exists(LockPath) && !File.ReadAllText(LockPath).StartsWith(me + " ")
                        && (System.DateTime.Now - File.GetLastWriteTime(LockPath)).TotalMinutes < 10) return false;
                }
                catch (IOException) { return false; }
                return true;
            }
            public static void Take(string me) { try { File.WriteAllText(LockPath, me + " " + System.DateTime.Now.ToString("HH:mm:ss")); } catch (IOException) { } }
            public static void Release(string me)
            {
                try { if (File.Exists(LockPath) && File.ReadAllText(LockPath).StartsWith(me + " ")) File.Delete(LockPath); } catch (IOException) { }
            }
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

            if (state == "" && !EditorApplication.isPlayingOrWillChangePlaymode) Gate.Release("rr");
            // 11-14: a compile/import right when a run asked for Play cancels the request, and the run then waited for
            // Play forever (rr stuck 03:09 -> 03:28, every other remote blocked). Ask again a few times, then give up.
            if ((state == "shots" || state == "perf" || state == "census" || state == "inkkeys" || state == "devkeys" || state == "starting")
                && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                double asked = SessionState.GetFloat("RoomRun.Clip.AskedAt", 0f);
                if (asked <= 0 || asked > EditorApplication.timeSinceStartup) SessionState.SetFloat("RoomRun.Clip.AskedAt", (float)EditorApplication.timeSinceStartup);
                else if (EditorApplication.timeSinceStartup - asked > 20)
                {
                    int tries = SessionState.GetInt("RoomRun.Clip.Retries", 0);
                    if (tries < 3) { SessionState.SetInt("RoomRun.Clip.Retries", tries + 1); SessionState.SetFloat("RoomRun.Clip.AskedAt", (float)EditorApplication.timeSinceStartup); Note($"{state}: play request was dropped, asking again"); EditorApplication.isPlaying = true; }
                    else { Note($"{state}: Play never started, run dropped"); SessionState.SetString(KeyState, ""); SessionState.SetInt("RoomRun.Clip.Retries", 0); SessionState.EraseFloat("RoomRun.Clip.AskedAt"); }
                    return;
                }
            }
            else if (EditorApplication.isPlaying) { SessionState.EraseFloat("RoomRun.Clip.AskedAt"); SessionState.SetInt("RoomRun.Clip.Retries", 0); }
            // 11-14: several threads overwrote each other's rr_cmd.txt. A thread can instead drop its line into
            // ArtSource/rr_queue/<anything>.txt; the oldest one becomes the next rr_cmd.txt when the slot is free.
            if (state == "" && !File.Exists(Cmd))
            {
                var q = Path.Combine(Dir, "rr_queue");
                if (Directory.Exists(q))
                {
                    var files = new DirectoryInfo(q).GetFiles("*.txt");
                    if (files.Length > 0)
                    {
                        System.Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                        try { File.Move(files[0].FullName, Cmd); Note("queued " + files[0].Name); } catch (IOException) { }
                    }
                }
            }
            if (state == "" && File.Exists(Cmd) && Gate.Ready("rr"))
            {
                var parts = File.ReadAllText(Cmd).Trim().Split(' ');
                File.Delete(Cmd);
                ShadowPass.ForceOff();   // 11-19: every queue item starts from the ShadowPass default (no capture override)
                Gate.Take("rr");
                // "orphans" lists renderers outside any scene (leaked from another field); "orphans clear" destroys them
                if (parts[0] == "orphans")
                {
                    var roots = new HashSet<GameObject>();
                    var names = new Dictionary<string, int>();
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    {
                        if (!r || EditorUtility.IsPersistent(r) || r.gameObject.scene.IsValid()) continue;
                        var root = r.transform.root.gameObject;
                        roots.Add(root);
                        string k = $"{root.name} > {r.name} flags={r.gameObject.hideFlags} y={r.bounds.center.y:0.0}";
                        k = System.Text.RegularExpressions.Regex.Replace(k, @"-?\d+_-?\d+", "#");
                        names[k] = names.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
                    foreach (var kv in names.OrderByDescending(x => x.Value).Take(20)) Note($"orphan {kv.Value} x {kv.Key}");
                    if (parts.Length > 1 && parts[1] == "clear")
                    {
                        int n = 0;
                        foreach (var g in roots) if (g) { Object.DestroyImmediate(g); n++; }
                        Note($"orphans cleared {n} roots");
                    }
                    else Note($"orphans {roots.Count} roots");
                    return;
                }
                if (parts[0] == "call" && parts.Length > 1)
                {
                    // call Namespace.Type.StaticMethod -> invokes a public static parameterless editor method (no menu items needed)
                    string full = parts[1]; int dot = full.LastIndexOf('.');
                    string tn = dot > 0 ? full.Substring(0, dot) : ""; string mn = full.Substring(dot + 1);
                    var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(tn)).FirstOrDefault(x => x != null);
                    var m = t?.GetMethod(mn, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, System.Type.EmptyTypes, null);
                    if (m == null) { Note($"call {full}: not found"); return; }
                    try { m.Invoke(null, null); Note($"call {full}: done"); }
                    catch (System.Exception e) { var x = e.InnerException ?? e; Note($"call {full}: {x.GetType().Name} {x.Message}"); }
                    return;
                }
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
        // "room~": aim fixed up-screen, walk 8 directions (0.9 s each, a stop between), then a held combo; traced to gs_trace.csv
        sealed class Dirs8 : ICommandSource
        {
            readonly float t0 = Time.time;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0;
                int i = (int)(t / 1.2f);
                float local = t - i * 1.2f;
                var c = new PlayerCommand { aimDir = Vector3.forward };
                if (i < 8 && local < 0.9f)
                {
                    float a = i * 45f * Mathf.Deg2Rad;
                    c.move = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                }
                else if (i >= 8)
                {
                    // combo aimed up, right, down-left, left (1.6 s each): the sweeps must follow the cursor
                    var aims = new[] { Vector3.forward, Vector3.right, new Vector3(-1, 0, -1).normalized, Vector3.left };
                    c.aimDir = aims[Mathf.Min(3, (int)((t - 9.6f) / 1.6f))];
                    c.fire = true;
                }
                return c;
            }
        }

        static float swingCancel = 0.8f;
        static string splitTag = "";
        static string sweepTag = "";   // "tag=before": file name suffix for the sweep test (before/after runs)

        // "$": cursor fixed up-screen; 8 move directions (1 s each, 0.25 s stand between), then attack while strafing right
        // (2 s) and while backpedalling (2 s)
        static ICommandSource whipKeep;
        sealed class WhipRun : ICommandSource
        {
            readonly float t0 = Time.time;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = (Time.time - t0) % 7f;
                var c = new PlayerCommand { aimDir = Vector3.forward, fire = true };
                c.move = t < 2f ? new Vector2(1f, 0f) : t < 4f ? new Vector2(-1f, 0f) : t < 5.5f ? new Vector2(0f, -1f) : new Vector2(0f, 1f);
                return c;
            }
        }

        sealed class Move8 : ICommandSource
        {
            readonly float t0 = Time.time;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0;
                var c = new PlayerCommand { aimDir = Vector3.forward };
                int i = (int)(t / 1.25f);
                if (i < 8)
                {
                    if (t - i * 1.25f < 1f) { float a = i * 45f * Mathf.Deg2Rad; c.move = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }
                }
                else if (t < 12f) { c.move = new Vector2(1f, 0f); c.fire = true; }
                else if (t < 14f) { c.move = new Vector2(0f, -1f); c.fire = true; }
                return c;
            }
        }

        // "&" (11-15 facing check): cursor at +Z, +X, -Z, -X (2 s each): attack held 1.2 s, then let go and stand
        sealed class Facing4 : ICommandSource
        {
            readonly float t0 = Time.time;
            static readonly Vector3[] aims = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            public Vector3 Aim { get; private set; } = Vector3.forward;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0;
                int i = Mathf.Min(3, (int)(t / 2f));
                Aim = aims[i];
                return new PlayerCommand { aimDir = Aim, fire = t - i * 2f < 1.2f && t < 8f };
            }
        }

        // "%" (11-14 cut debris check): walks a slow circle swinging the greatsword left and right of the way it walks,
        // so every swing goes into uncut grass / reeds
        sealed class CutWalker : ICommandSource
        {
            readonly float t0 = Time.time;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                float t = Time.time - t0;
                float head = t * 0.3f;
                var mv = new Vector2(Mathf.Sin(head), Mathf.Cos(head));
                float a = head + Mathf.Sin(t * 1.6f) * 1.0f;
                return new PlayerCommand { move = mv * 0.45f, aimDir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), fire = true };
            }
        }

        // "^": 3.2 s loop: hold attack at the nearest enemy (the four-swing chain), let go and step aside, then dodge toward the
        // enemies and attack out of the dash (dash slash)
        sealed class ComboShow : ICommandSource
        {
            readonly RunDirector run;
            readonly float t0 = Time.time;
            int dodged = -1;
            public ComboShow(RunDirector r) => run = r;
            public PlayerCommand Read(PlayerController p, Camera cam)
            {
                Vector3 best = p.transform.forward; float bd = float.MaxValue;
                foreach (var e in run.Enemies)
                {
                    if (!e) continue;
                    var d = e.transform.position - p.transform.position; d.y = 0;
                    if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = d; }
                }
                var aim = best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
                float t = Time.time - t0;
                int cycle = (int)(t / 3.2f);
                float local = t - cycle * 3.2f;
                var c = new PlayerCommand { aimDir = aim };
                if (local < 2.0f) c.fire = true;
                else if (local < 2.4f) { var side = Vector3.Cross(Vector3.up, aim); c.move = new Vector2(side.x, side.z) * 0.6f; }
                else
                {
                    c.move = new Vector2(aim.x, aim.z);
                    if (dodged != cycle) { dodged = cycle; c.dodge = true; }
                    else if (local > 2.55f && local < 2.8f) c.fire = true;
                }
                return c;
            }
        }

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
            int seq = 0;
            string seeTag = "";
            string[] offList = new string[0]; string offTag = "";
            bool hideBanner = false;
            foreach (var token in rooms)
            {
                if (string.IsNullOrEmpty(token)) continue;
                // "seq=90": after the stills, that many consecutive frames at the first zoom to rr_clip/<tag>_NNN.jpg (short video)
                if (token.StartsWith("seq=")) { int.TryParse(token.Substring(4), out seq); continue; }
                // "banner=0" (11-19 contact shadow captures): hide the HUD banner ("모든 방을 정리했다") in the shots after it
                if (token.StartsWith("banner=")) { hideBanner = token.EndsWith("0"); continue; }
                // "sp=1" / "sp=0": ShadowPass (+ contact shadow) forced on / off for this capture only (cleared when it ends)
                if (token.StartsWith("sp=")) { ShadowPass.CaptureOn = token.EndsWith("1"); yield return new WaitForSeconds(0.8f); continue; }
                // "see=0" / "see=1": soft see-through off (previous behaviour) / on for the rooms after it (SeeThroughFX);
                // the file names get _see0 / _see1, the setting goes back to on after the shots
                // (see=1 = the first soft version, see=2 = v2 with the default v2 shape)
                if (token.StartsWith("see=")) { int.TryParse(token.Substring(4), out int sm); PlayerPrefs.SetInt(SeeThroughFX.ModeKey, sm); if (sm == 2) typeof(SeeThroughFX).GetMethod("ResetV2")?.Invoke(null, null); /* 11-14: ResetV2 is in the academy PC copy of SeeThroughFX, not on main yet */ seeTag = "_see" + token.Substring(4); continue; }
                // "z=1,1.2,1.4" sets the zoom levels for the rooms after it
                // "off=StageMood,FireShadowSpot": for the rooms after it, behaviours of those types (or lights with those
                // names) are switched off just for the capture; file names get _off-<list> ("off=" clears it). A/B for look bugs.
                if (token.StartsWith("off="))
                {
                    offList = token.Length > 4 ? token.Substring(4).Split(',') : new string[0];
                    offTag = offList.Length > 0 ? "_off-" + string.Join("-", offList) : "";
                    continue;
                }
                // "split=0" / "split=1": SplitBody (upper/lower body split) off/on for the rooms after it, file tag _split0/_split1
                if (token.StartsWith("split=")) { SplitBody.On = token.EndsWith("1"); splitTag = "_split" + (SplitBody.On ? "1" : "0"); continue; }
                // "feel=0" / "feel=1": aim-mode run feel (lean, sword drag spring, sway) off / on on top of the split, tag _feel0
                if (token.StartsWith("feel=")) { SplitBody.Feel = token.EndsWith("1"); splitTag = SplitBody.Feel ? "" : "_feel0"; continue; }
                // "limb=0" / "limb=1" (11-15 limb fix A/B): the old SplitBody ease into swings + SwingFacing on the berserker, or the fix
                // "calm=1": no encounter enemies for the rooms after it (player close-ups); calm=0 back
                if (token.StartsWith("calm=")) { RunDirector.DevNoEnemies = token.EndsWith("1"); foreach (var e in run.Enemies) if (e) Object.Destroy(e.gameObject); run.Enemies.Clear(); continue; }
                // "occe=1" (11-15 visibility pass A/B): the hero's occluded outline also shows under a pile of enemies; occe=0 back
                // "occe=2": outline forced on (checks the pass itself)
                // "occv=1" (11-16): the v1 fresnel outline pass instead of the v2 screen-space line, for A/B
                if (token.StartsWith("occv=")) { FieldLighting.OutlineV1 = token.EndsWith("1"); continue; }
                if (token.StartsWith("occe=")) { FieldLighting.OutlineThroughEnemies = !token.EndsWith("0"); FieldLighting.ForceOutline = token.EndsWith("2"); continue; }
                if (token.StartsWith("limb=")) { bool old = token.EndsWith("0"); SplitBody.LegacyEase = old; SwingFacing.LegacyBerserker = old; splitTag = old ? "_limb0" : ""; continue; }
                if (token.StartsWith("sf=")) { float.TryParse(token.Substring(3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out swingCancel); swingCancel = swingCancel > 0f ? 0.8f : 0f; continue; }
                if (token.StartsWith("tag=")) { sweepTag = "_" + token.Substring(4); continue; }
                // "vfxs=1" (11-16 slash VFX size check): the "#" sweep test only stands and does two 3-hit combos (SweepTest.VfxMode)
                if (token.StartsWith("vfxs=")) { SweepTest.VfxMode = token.EndsWith("1"); continue; }
                // "wide=1|2|3" (11-16 wide sweep capture, see GSSweepTest), "wide=0" off; "sway=0" = the old dragged-blade
                // spring (SplitBody.LegacySway) for A/B, "sway=1" the calm one
                if (token.StartsWith("wide=")) { int.TryParse(token.Substring(5), out SweepTest.WideMode); continue; }
                if (token.StartsWith("sway=")) { SplitBody.LegacySway = token.EndsWith("0"); continue; }
                // 11-16 single big hit: "hand=0|1" = walk grip fix off/on (SplitBody.HandCarries), "single=0|1" = 3-hit combo /
                // single hit 1 (GreatswordCombat.SingleHit), "stretch=0|1" = arm stretch off/on; all also tag the clip name
                if (token.StartsWith("hand=")) { SplitBody.HandCarries = token.EndsWith("1"); sweepTag = "_hand" + token.Substring(5); continue; }
                if (token.StartsWith("single=")) { GreatswordCombat.SingleHit = token.EndsWith("1"); continue; }
                if (token.StartsWith("stance=")) { SplitBody.StanceYaw = float.Parse(token.Substring(7), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("stretch=")) { ArmStretch.On = token.EndsWith("1"); continue; }
                if (token.StartsWith("reach=")) { if (float.TryParse(token.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rc)) ArmStretch.ChainExtra = rc; continue; }
                // 11-16 flat hold: "flat=0|1" = the old sword seat / the face rolled flat in the hand (SplitBody.FlatHold)
                if (token.StartsWith("flat=")) { SplitBody.FlatHold = !token.EndsWith("0"); continue; }
                if (token.StartsWith("edge=")) { SplitBody.EdgeLead = !token.EndsWith("0"); continue; }
                if (token.StartsWith("seat=")) { SplitBody.SeatFix = !token.EndsWith("0"); continue; }
                if (token.StartsWith("mx=")) { GreatswordCombat.MixamoChain = token.EndsWith("1"); continue; }
                if (token.StartsWith("mxik=")) { MxGrip.LeftHandIK = !token.EndsWith("0"); continue; }
                if (token.StartsWith("swap=")) { SplitBody.HandSwap = !token.EndsWith("0"); continue; }
                if (token.StartsWith("walkright=")) { SplitBody.WalkCarryRight = !token.EndsWith("0"); continue; }
                if (token.StartsWith("ghost=")) { BladeGhost.On = !token.EndsWith("0"); continue; }
                if (token.StartsWith("arcsprite=")) { GreatswordCombat.ChainSprite = token.EndsWith("1"); continue; }
                if (token.StartsWith("cblend=")) { if (float.TryParse(token.Substring(7), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cb)) GreatswordCombat.ChainBlend = cb; continue; }
                if (token.StartsWith("vfxlate=")) { if (float.TryParse(token.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vl)) GreatswordCombat.VfxLate = vl; continue; }
                if (token.StartsWith("vfxlatespin=")) { if (float.TryParse(token.Substring(12), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vs2)) GreatswordCombat.VfxLateSpin = vs2; continue; }
                if (token.StartsWith("seatdeg=")) { if (float.TryParse(token.Substring(8), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sd)) SplitBody.SeatTurn = sd; continue; }
                if (token.StartsWith("vfxdir=")) { GreatswordCombat.VfxFollowBlade = !token.EndsWith("0"); continue; }
                if (token.StartsWith("vfxy=")) { if (float.TryParse(token.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fy)) GreatswordCombat.ChainVfxHeight = fy; continue; }
                if (token.StartsWith("cvfx=")) { if (float.TryParse(token.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cv)) GreatswordCombat.ChainVfx = cv; continue; }
                if (token.StartsWith("offhand=")) { SplitBody.OffHand = !token.EndsWith("0"); continue; }
                if (token.StartsWith("fist=")) { SplitBody.GripFist = !token.EndsWith("0"); continue; }
                // 11-18 greatsword pass tokens
                if (token.StartsWith("runarm=")) { SplitBody.RunArmV6 = token.EndsWith("6"); SplitBody.RunArmV4 = !token.EndsWith("3"); continue; }   // 6 = 11-20 option 2, 5 = v5 pump, 3 = v3
                if (token.StartsWith("heroline=")) { FieldLighting.HeroOutline = float.Parse(token.Substring(9), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("prehold=")) { GreatswordCombat.PreHoldSec = float.Parse(token.Substring(8), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("thick=")) { GreatswordCombat.ThickTrail = !token.EndsWith("0"); continue; }
                if (token.StartsWith("vr=")) { GreatswordCombat.VRisingChain = !token.EndsWith("0"); continue; }
                if (token.StartsWith("style=")) { GreatswordCombat.ChainStyle = token.EndsWith("0") ? 0 : 1; continue; }
                if (token.StartsWith("spin3=")) { GreatswordCombat.SpinThird = !token.EndsWith("0"); continue; }
                if (token.StartsWith("flow=")) { GreatswordCombat.ChainFlow = !token.EndsWith("0"); continue; }
                if (token.StartsWith("twohand=")) { SplitBody.TwoHand = !token.EndsWith("0"); continue; }
                if (token.StartsWith("carry=")) { SplitBody.CarryMix = token.EndsWith("mix"); continue; }
                if (token.StartsWith("carrylow=")) { SplitBody.CarryLow = !token.EndsWith("0"); continue; }
                if (token.StartsWith("carryf=")) { var cf = token.Substring(7).Split(','); SplitBody.CarryLowFrameR = float.Parse(cf[0], System.Globalization.CultureInfo.InvariantCulture); if (cf.Length > 1) SplitBody.CarryLowFrameL = float.Parse(cf[1], System.Globalization.CultureInfo.InvariantCulture); continue; }
                // 11-18 whip test: "whip=1|0" swaps the greatsword for the whip (PlayerPrefs RR.Whip), "whipproc=1" = every basic hit procs the sweep
                if (token.StartsWith("whip=")) { PlayerPrefs.SetInt(WhipCombat.Key, token.EndsWith("0") ? 0 : 1); yield return new WaitForSeconds(0.7f); continue; }
                // 11-18 이기어검 test: "eg=1|0" (PlayerPrefs RR.Eogeom), "egcap=N" = EogeomCapture renders N frames (3 cameras) to ArtSource/Eogeom/out/frames
                if (token.StartsWith("eg=")) { PlayerPrefs.SetInt(EogeomCombat.Key, token.EndsWith("0") ? 0 : 1); yield return new WaitForSeconds(0.7f); continue; }
                if (token.StartsWith("egcap=")) { int.TryParse(token.Substring(6), out EogeomCapture.Frames); continue; }
                // v5: "egtrail=0|1|2" trail style, "egtrailfx=<prefab path>" asset trail for style 2, "egout=<name>" capture folder under out/v5_vfx
                if (token.StartsWith("egtrail=")) { int.TryParse(token.Substring(8), out EogeomCombat.TrailStyle); continue; }
                if (token.StartsWith("egtrailfx=")) { EogeomCombat.TrailPrefabPath = token.Substring(10).Replace('|', ' '); continue; }
                if (token.StartsWith("egout=")) { EogeomCapture.OutDir = System.IO.Path.Combine(Application.dataPath, "..", "ArtSource", "Eogeom", "out", token.Substring(6)); continue; }
                if (token.StartsWith("egv16=")) { EogeomCombat.V16 = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egv15=")) { EogeomCombat.V15 = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egv14=")) { EogeomCombat.V14 = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egv13=")) { EogeomCombat.V13 = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egff14=")) { EogeomCombat.FF14Base = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egmx=")) { EogeomCombat.MxLayer = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egshoulder=")) { EogeomCombat.ShoulderRest = !token.EndsWith("0"); continue; }
                if (token.StartsWith("eghold=")) { EogeomCombat.Interceptor = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egcolor=")) { EogeomCombat.Recolor = !token.EndsWith("0"); continue; }
                if (token.StartsWith("egfps=")) { float.TryParse(token.Substring(6), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out EogeomCapture.FpsSeconds); EogeomCapture.Frames = 1; continue; }
                if (token.StartsWith("egmove=")) { int.TryParse(token.Substring(7), out EogeomCapture.Mode); continue; }
                if (token.StartsWith("whipproc=")) { WhipCombat.ForceProc = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipleft=")) { WhipCombat.LeftShare = token.EndsWith("0") ? 1f : 0f; continue; }
                if (token.StartsWith("whipfollow=")) { WhipCombat.BodyFollowOn = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipviolent=")) { WhipCombat.Violent = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whiptorch=")) { WhipCombat.TorchCombo = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipprev=")) { var v = token.Substring(9); WhipCombat.PreviewClips = v == "0" ? null : v.Split(','); continue; }
                if (token.StartsWith("whipstraight=")) { WhipCombat.Straight = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipover=")) { WhipCombat.Overhead = !token.EndsWith("0"); continue; }
                if (token.StartsWith("suit=")) { SuitBody.Force = !token.EndsWith("0"); continue; }
                if (token.StartsWith("coat=")) { CoatTail.Force = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whiparms=")) { WhipCombat.FullClipArms = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipswish=")) { WhipCombat.Swish = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipspiral=")) { WhipCombat.Spiral = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipfree=")) { WhipCombat.Free = token.EndsWith("1"); continue; }
                if (token.StartsWith("whipauth=")) { WhipCombat.Authored = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipcombo=")) { WhipCombat.ComboWide = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipsweep=")) { WhipCombat.SweepSpiral = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipchop=")) { WhipCombat.Chop = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipdiag=")) { WhipCombat.Diag = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipghost=")) { WhipCombat.Ghost = token.EndsWith("1"); continue; }
                if (token.StartsWith("whiprake=")) { WhipCombat.Rake = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whiparch=")) { WhipCombat.Arch = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whiplight=")) { WhipCombat.LightChain = token.EndsWith("1"); continue; }
                if (token.StartsWith("whipbody2=")) { WhipCombat.FullBodyER = token.EndsWith("1"); continue; }
                if (token.StartsWith("whipmusa=")) { WhipCombat.MusaLash = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipwrap=")) { WhipCombat.MusaWrap = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whiparc=")) { WhipCombat.MusaArc = !token.EndsWith("0"); continue; }
                if (token.StartsWith("whipbody=")) { WhipCombat.UseClipBody = !token.EndsWith("0"); continue; }
                // "whiprun=1": cursor fixed north, attack held, running east / west / back / forward (segmented blade while moving)
                if (token.StartsWith("whiprun=")) { if (token.EndsWith("0")) { if (whipKeep != null) run.Player.Source = whipKeep; whipKeep = null; } else { whipKeep ??= run.Player.Source; run.Player.Source = new WhipRun(); } continue; }
                if (token.StartsWith("whipgap=")) { WhipCombat.DevFireGap = float.Parse(token.Substring(8), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("hold=")) { SweepTest.ComboHold = float.Parse(token.Substring(5), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("slide=")) { SplitBody.GripSlide = float.Parse(token.Substring(6), System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (token.StartsWith("z="))
                {
                    zooms = System.Array.ConvertAll(token.Substring(2).Split(','), s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture));
                    continue;
                }
                string id = token, tag = token;
                Vector3? at = null;
                bool dirs8 = id.EndsWith("~");
                if (dirs8) { id = id.TrimEnd('~'); tag = id + "_dirs8"; }
                // "grass+12^" (11-14 greatsword feel pass): full combo at the nearest enemy, then a dodge dash into a dash slash, looping
                bool show = id.EndsWith("^");
                if (show) { id = id.TrimEnd('^'); tag = id + "_combo"; }
                // "grass&" (11-15 facing check): cursor fixed at +Z, +X, -Z, -X in turn, attack held 1.2 s each; a marker shows the
                // cursor side and ArtSource/GSHero/facing_log.csv gets aim / visual / chest / blade yaw per frame.
                // "sf=0" before it switches SwingFacing off (before/after), "sf=1" back on
                bool face4 = id.EndsWith("&");
                if (face4) { id = id.TrimEnd('&'); tag = id + "_face4" + (swingCancel == 0f ? "_sf0" : "") + splitTag; }
                // "grass$" (11-15 split body compare): cursor fixed up-screen, 8 move directions, then attacks while strafing / backpedalling
                bool move8 = id.EndsWith("$");
                if (move8) { id = id.TrimEnd('$'); tag = id + "_move8" + splitTag; }
                // "grass#" (11-15 sweep fix): cursor north, 8 runs, combo standing, combo moving east, turns; GSSweepTest.cs
                bool sweep = id.EndsWith("#");
                if (sweep) { id = id.TrimEnd('#'); tag = id + "_sweep" + splitTag + sweepTag; }
                bool cutWalk = token.Contains("%");
                if (cutWalk) { id = id.Replace("%", ""); tag = id + "_cutwalk"; }
                bool firing = id.EndsWith("!");
                if (firing) { id = id.TrimEnd('!'); tag = id + "_fire"; }
                // "grass+12*" : the extra enemies hold still where they spawned (looks check: they do not pile onto the player)
                bool still = id.EndsWith("*");
                if (still) { id = id.TrimEnd('*'); tag = id + "_still"; }
                // "grass+150!" : 150 extra enemies around the player before the shots
                int swarm = 0, plus = id.IndexOf('+');
                if (plus > 0) { int.TryParse(id.Substring(plus + 1), out swarm); id = id.Substring(0, plus); tag = id + $"_swarm{swarm}" + (firing ? "_fire" : "") + (still ? "_still" : "") + (show ? "_combo" : ""); }
                int a = token.IndexOf('@');
                if (a > 0)
                {
                    id = token.Substring(0, a).Replace("%", "");
                    var xz = token.Substring(a + 1).Split(',');
                    at = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), 0, float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                    tag = $"{id}_{xz[0]}_{xz[1]}" + (firing ? "_fire" : "") + (cutWalk ? "_cutwalk" : "");
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
                    // v2_check (11-15): with calm=1 ("calm=1 grass+4*^") the still enemies are still spawned (the encounter is
                    // not), stand in an arc 2.6-3.4 m in front of the player and cannot die, so every hit of the combo connects
                    bool calm = RunDirector.DevNoEnemies;
                    RunDirector.DevNoEnemies = false;
                    run.DevSwarm(swarm);
                    RunDirector.DevNoEnemies = calm;
                    int k = 0;
                    var pp0 = run.Player.transform.position;
                    foreach (var e in run.Enemies)
                    {
                        if (!e) continue;
                        e.enabled = false; // the Animator keeps playing their walk
                        if (!calm) continue;
                        float ang = (k - (swarm - 1) * 0.5f) * 28f * Mathf.Deg2Rad, r = 2.6f + 0.8f * (k % 2);
                        // 11-16: a big still swarm (more than 12) stands in a ring all round, 2.4 - 4.4 m, so a sweep's reach shows
                        if (swarm > 12) { ang = k * (360f / swarm) * Mathf.Deg2Rad + 0.3f; r = 2.4f + 1.0f * (k % 3); }
                        var cc = e.GetComponent<CharacterController>(); if (cc) cc.enabled = false;
                        var p = pp0 + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
                        p.y = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var gh, 60f, Ground.GroundMask) ? gh.point.y : pp0.y;
                        e.transform.position = p;
                        e.transform.rotation = Quaternion.LookRotation(pp0 - p, Vector3.up);
                        var hh = e.GetComponent<Health>(); if (hh) { hh.max = hh.current = 99999; }
                        k++;
                    }
                    yield return new WaitForSeconds(1f);
                }
                else if (swarm > 0) { run.DevSwarm(swarm); yield return new WaitForSeconds(4f); }
                var keepSource = run.Player.Source;
                if (firing) { run.Player.Source = new Firer(run); yield return new WaitForSeconds(1.2f); }
                if (cutWalk) run.Player.Source = new CutWalker();
                if (show) run.Player.Source = new ComboShow(run);
                if (move8) run.Player.Source = new Move8();
                Facing4 f4 = null; GameObject marker = null; SwingFacing sfc = null; StreamWriter flog = null;
                if (face4)
                {
                    sfc = run.Player.GetComponent<SwingFacing>();
                    if (sfc) sfc.cancel = swingCancel;
                    f4 = new Facing4(); run.Player.Source = f4;
                    marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(marker.GetComponent<Collider>());
                    marker.transform.localScale = Vector3.one * 0.45f;
                    var mm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mm.SetColor("_BaseColor", new Color(1f, 0.85f, 0.2f));
                    marker.GetComponent<Renderer>().sharedMaterial = mm;
                    string lp = Path.Combine(Dir, "GSHero", "facing_log.csv");
                    bool head = !File.Exists(lp);
                    flog = new StreamWriter(lp, true);
                    if (head) flog.WriteLine("tag,frame,aimYaw,visualYaw,chestYaw,bladeYaw,twist,attacking");
                }
                if (dirs8)
                {
                    GreatswordCombat.TracePath = Path.Combine(Dir, "gs_trace.csv");
                    run.Player.Source = new Dirs8();
                }
                var rig = run.Rig;
                // other fields' stale DontSave renderers (no scene) draw over every stage in the shared editor; hide them
                var orphans = new List<Renderer>();
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    if (r && !EditorUtility.IsPersistent(r) && r.enabled && !r.gameObject.scene.IsValid()) { r.enabled = false; orphans.Add(r); }
                if (orphans.Count > 0) Note($"  hid {orphans.Count} orphan renderers from other fields");
                var switchedOff = new List<Behaviour>();
                if (offList.Length > 0)
                    foreach (var b in Object.FindObjectsByType<Behaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (b && b.enabled && (System.Array.IndexOf(offList, b.GetType().Name) >= 0 || (b is Light && System.Array.IndexOf(offList, b.name) >= 0)))
                        { b.enabled = false; switchedOff.Add(b); }
                if (switchedOff.Count > 0) { Note($"  off: {switchedOff.Count} ({string.Join(",", offList)})"); yield return null; yield return null; }
                if (hideBanner && run.Hud) run.Hud.HideBanner();
                foreach (float z in zooms)
                {
                    rig.Zoom = z; rig.Snap();
                    for (int f = 0; f < 4; f++) yield return null;
                    var cam = rig.Cam;
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, $"{tag}{seeTag}{offTag}_z{z:0.0}.jpg"), tex.EncodeToJPG(90));
                }
                foreach (var b in switchedOff) if (b) b.enabled = true;
                if (seq > 0)
                {
                    string clipDir = Path.Combine(Dir, "rr_clip");
                    Directory.CreateDirectory(clipDir);
                    rig.Zoom = zooms[0]; rig.Snap();
                    Time.captureFramerate = 30;   // fixed game time per saved frame, however slow the encode is
                    // v2_check (11-15): per-frame log next to the frames: combo ("^") = hit-stop / hold / camera shake,
                    // move8 ("$") = knee direction vs toe direction and thigh twist per leg
                    StreamWriter vlog = null; Animator van = null; GreatswordCombat vgs = null;
                    if (show || move8)
                    {
                        string vd = Path.Combine(Dir, "GSHero", "ingame_shots", "v2_check"); Directory.CreateDirectory(vd);
                        vlog = new StreamWriter(Path.Combine(vd, $"{tag}_z{zooms[0]:0.0}_log.csv"), false);
                        van = run.Player.GetComponentInChildren<Animator>(); vgs = run.Player.GetComponent<GreatswordCombat>();
                        vlog.WriteLine(show ? "frame,timeScale,step,stepT,hitStopLeft,holdLeft,shake,jitter_cm,attacking"
                                            : "frame,moveX,moveZ,attacking,L_kneeVsToe,L_kneeOff_cm,L_thighTwist,R_kneeVsToe,R_kneeOff_cm,R_thighTwist,splitTwist");
                    }
                    SweepTest swt = null;
                    if (sweep)
                    {
                        swt = new SweepTest(); run.Player.Source = swt;
                        swt.OpenLog(Path.Combine(Dir, "GSHero", "ingame_shots", "sweep_fix", $"{tag}_z{zooms[0]:0.0}_log.csv"), run.Player);
                    }
                    for (int f = 0; f < seq; f++)
                    {
                        yield return new WaitForEndOfFrame();
                        if (swt != null) swt.LogFrame(f, run.Player);
                        if (vlog != null && show && vgs)
                            vlog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:0.###},{2},{3:0.###},{4:0.###},{5:0.###},{6:0.###},{7:0.0},{8}",
                                f, Time.timeScale, vgs.StepIndex, vgs.StepT, vgs.HitStopLeft, vgs.HoldLeft, rig.ShakeNow, rig.LastJitter.magnitude * 100f, vgs.Attacking ? 1 : 0));
                        if (vlog != null && move8 && van)
                        {
                            string Leg(HumanBodyBones up, HumanBodyBones lo, HumanBodyBones ft, HumanBodyBones toe)
                            {
                                var h = van.GetBoneTransform(up); var k = van.GetBoneTransform(lo); var a = van.GetBoneTransform(ft); var t = van.GetBoneTransform(toe);
                                if (!h || !k || !a || !t) return ",,";
                                var line = (a.position - h.position).normalized;
                                var ko = k.position - h.position; ko -= line * Vector3.Dot(ko, line); ko.y = 0f;
                                var td = t.position - a.position; td.y = 0f;
                                float ang = ko.sqrMagnitude > 1e-6f && td.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(td, ko, Vector3.up) : 0f;
                                // thigh twist: the knee hinge (thigh local X) against the hips' X, about the thigh axis
                                var hipsT = van.GetBoneTransform(HumanBodyBones.Hips);
                                var ax = (k.position - h.position).normalized;
                                Vector3 Pr(Vector3 v) => (v - ax * Vector3.Dot(v, ax)).normalized;
                                float tw = hipsT ? Vector3.SignedAngle(Pr(hipsT.right), Pr(h.right), ax) : 0f;
                                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0},{1:0.0},{2:0}", ang, ko.magnitude * 100f, tw);
                            }
                            var vel = run.Player.GetComponent<CharacterController>() ? run.Player.GetComponent<CharacterController>().velocity : Vector3.zero;
                            var sb = run.Player.GetComponent<SplitBody>();
                            vlog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1:0.00},{2:0.00},{3},{4},{5},{6:0}",
                                f, vel.x, vel.z, vgs && vgs.Attacking ? 1 : 0,
                                Leg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
                                Leg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
                                sb ? sb.Twist : 0f));
                        }
                        if (f4 != null)
                        {
                            var pp = run.Player.transform.position;
                            marker.transform.position = pp + f4.Aim * 2.6f + Vector3.up * 0.4f;
                            var an = run.Player.GetComponentInChildren<Animator>();
                            float Yw(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? 0f : Vector3.SignedAngle(Vector3.forward, v, Vector3.up); }
                            float chest = 0f, blade = 0f;
                            if (an)
                            {
                                var l = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
                                if (l && r) chest = Yw(Vector3.Cross(r.position - l.position, Vector3.up));
                            }
                            foreach (var t in run.Player.GetComponentsInChildren<Transform>()) if (t.name == "Greatsword") { blade = Yw(t.forward); break; }
                            var vis = sfc && sfc.visual ? sfc.visual : (an ? an.transform.parent : null);
                            var gsc = run.Player.GetComponent<GreatswordCombat>();
                            flog.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2:0},{3:0},{4:0},{5:0},{6:0},{7}",
                                tag, f, Yw(f4.Aim), vis ? Yw(vis.forward) : 0f, chest, blade, sfc ? sfc.LastTwist : 0f, gsc && gsc.Attacking ? 1 : 0));
                        }
                        var cam = rig.Cam;
                        cam.targetTexture = rt; cam.Render();
                        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                        cam.targetTexture = null;
                        File.WriteAllBytes(Path.Combine(clipDir, $"{tag}_{f:000}.jpg"), tex.EncodeToJPG(88));
                    }
                    Time.captureFramerate = 0;
                    GreatswordCombat.TracePath = null;
                    if (swt != null) swt.CloseLog();
                }
                foreach (var r in orphans) if (r) r.enabled = true;
                rig.Zoom = CameraRig.FixedZoom;
                if (firing || dirs8 || cutWalk || show || face4 || move8 || sweep) run.Player.Source = keepSource;
                if (marker) Object.Destroy(marker);
                if (flog != null) flog.Close();
                if (sfc) sfc.cancel = 0.8f;
                // what the post stack actually resolves to for the game camera (diagnosing the dark underworld)
                var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
                var cadj = stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
                var vig = stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
                var vols = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None)
                    .Select(v => $"{v.name}(p{v.priority} w{v.weight:0.##} {(v.isGlobal ? "global" : "local")} layer{v.gameObject.layer})");
                var camData = rig.Cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                Note($"  post stack: exposure {(cadj ? cadj.postExposure.value : -99):0.00}, saturation {(cadj ? cadj.saturation.value : 0):0}, vignette {(vig ? vig.intensity.value : 0):0.00}; volumeMask {(camData ? camData.volumeLayerMask.value : -1)} trigger {(camData && camData.volumeTrigger ? camData.volumeTrigger.name : "cam")}; volumes: {string.Join(", ", vols)}");
                Note($"  fog {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity:0.0000} colour {RenderSettings.fogColor}; ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight}");
                { var flz = Object.FindAnyObjectByType<FieldLighting>(); Note($"shots {tag} done (fps {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0}){(flz ? $" heroOccluded {flz.Occluded:0.00}" : "")}"); }
            }
            Object.Destroy(rt); Object.Destroy(tex);
            ShadowPass.ForceOff();
            if (seeTag != "") PlayerPrefs.SetInt(SeeThroughFX.ModeKey, 2);
            RunDirector.DevNoEnemies = false; FieldLighting.OutlineThroughEnemies = FieldLighting.OutlineThroughEnemiesDefault; FieldLighting.ForceOutline = false; FieldLighting.OutlineV1 = false; SplitBody.LegacyEase = false; SwingFacing.LegacyBerserker = false;
            sweepTag = ""; SweepTest.VfxMode = false; SplitBody.On = true; SplitBody.Feel = true; SplitBody.FlatHold = true; SplitBody.OffHand = true; SplitBody.GripFist = true;   // aim mode default (split=0 = v3 travel facing, feel=0 = frozen old split)
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
            // 11-16: stage switcher keys (PageDown/PageUp step, F12 jump, F7 back), then the panel's stage tab itself
            var K = new[] { UnityEngine.InputSystem.Key.PageDown, UnityEngine.InputSystem.Key.PageUp, UnityEngine.InputSystem.Key.F12, UnityEngine.InputSystem.Key.F7 };
            string dir = Path.Combine(Dir, "stage_switcher");
            Directory.CreateDirectory(dir);
            foreach (var key in K)
            {
                string before = run.Room.id;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                yield return null; yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
                while (run.Transitioning) yield return null;
                yield return new WaitForSeconds(1.5f);
                Note($"{key}: {before} -> {run.Room.id} hp={run.Player.Health.current}");
            }
            int shot = 0;
            System.Collections.IEnumerator Capture(string what)
            {
                yield return new WaitForSeconds(0.4f);
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"panel_{shot++}_{what}.png"), tex.EncodeToPNG());
                Note($"stagepanel capture {what} {tex.width}x{tex.height}");
                Object.Destroy(tex);
            }
            yield return Capture("stages");
            var search = GameObject.Find("StageSearch")?.GetComponent<UnityEngine.UI.InputField>();
            Note("search box " + (search ? "found" : "MISSING"));
            if (search)
            {
                search.text = "ㅈㅅ";
                yield return Capture("search_chosung");
                UnityEngine.UI.Button first = null;
                foreach (var b in search.transform.parent.GetComponentsInChildren<UnityEngine.UI.Button>())
                    if (b.transform.parent.name == "Grid" && b.transform.parent.parent.GetSiblingIndex() >= 0 && first == null && b.transform.parent.GetSiblingIndex() > 3) first = b;
                Note("first match " + (first ? first.name : "none"));
                if (first) first.onClick.Invoke();
                for (int f = 0; f < 10 && !run.Transitioning; f++) yield return null;
                while (run.Transitioning) yield return null;
                yield return new WaitForSeconds(2f);
                Note("after search click: room=" + run.Room.id);
                yield return Capture("after_jump");
            }
            Application.logMessageReceived -= onLog;
            Note($"devkeys done, errors={errors}");
            SessionState.SetString(KeyState, "stopping");
        }
    }
}
