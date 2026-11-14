// Runs ArtSource/ls_cmd.txt ("build") without clicking and writes the result to ArtSource/ls_log.txt.
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LookStudyWatcher
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
    static double next;
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");

    static LookStudyWatcher()
    {
        EditorApplication.update += Tick;
        File.WriteAllText(Path.Combine(Dir, "ls_log.txt"), "watcher ready");
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmd = Path.Combine(Dir, "ls_cmd.txt");
        if (!File.Exists(cmd) || !Gate.Ready("ls1")) return;
        File.Delete(cmd);
        string result;
        try { result = LookStudyEditor.BuildInOpenEditor(Path.Combine(Dir, "LookStudyShots")); }
        catch (System.Exception e) { result = "EXCEPTION " + e; }
        File.WriteAllText(Path.Combine(Dir, "ls_log.txt"), result);
    }
}
