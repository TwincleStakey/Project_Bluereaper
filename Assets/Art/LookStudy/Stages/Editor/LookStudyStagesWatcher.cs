// Builds the three stage scenes when ArtSource/ls_stages_cmd.txt appears, then reopens the scene that was open.
// Result goes to ArtSource/ls_stages_log.txt.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LookStudyStagesWatcher
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

    static LookStudyStagesWatcher() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmd = Path.Combine(Dir, "ls_stages_cmd.txt");
        if (!Directory.Exists(Dir) || !File.Exists(cmd) || !Gate.Ready("lss")) return;
        File.Delete(cmd);
        string result;
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty) result = "ABORT: unsaved changes in " + active.path;
        else if (EditorApplication.isPlayingOrWillChangePlaymode) result = "ABORT: play mode";
        else
        {
            var prev = active.path;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                LookStudyValleyEditor.BuildScene();
                var v = LookStudyRuinsEditor.BuildStage(LookStudyRuins.Kind.Village);
                var p = LookStudyRuinsEditor.BuildStage(LookStudyRuins.Kind.Palace);
                result = "OK Assets/Art/LookStudy/Stages/Valley.unity " + v + " " + p;
            }
            catch (System.Exception e) { result = "EXCEPTION " + e; }
            finally
            {
                ShaderUtil.allowAsyncCompilation = true;
                if (!string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
            }
        }
        File.WriteAllText(Path.Combine(Dir, "ls_stages_log.txt"), result);
    }
}
