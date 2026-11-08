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

            if (state == "stopping")
            {
                SessionState.SetString(KeyState, "");
                Note("clip done");
                EditorApplication.isPlaying = false;
                return;
            }

            // Play mode was left by hand during a clip.
            if (state != "" && state != "starting" && !EditorApplication.isPlaying) SessionState.SetString(KeyState, "");
        }
    }
}
