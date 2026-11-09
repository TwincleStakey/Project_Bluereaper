// Lets the room run be checked without clicking: write ArtSource/rr_cmd.txt with one line
//   clip <seconds> [roomId]
// and the editor enters play mode, jumps to the room, walks the player in a figure eight and saves
// frames to ArtSource/rr_clip, then leaves play mode. Progress goes to ArtSource/rr_log.txt.
using System.IO;
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

            if (state == "" && File.Exists(Cmd) && !EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                var parts = File.ReadAllText(Cmd).Trim().Split(' ');
                File.Delete(Cmd);
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
            if (state == "devkeys-running" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
            if (state != "" && state != "starting" && state != "devkeys" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
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
