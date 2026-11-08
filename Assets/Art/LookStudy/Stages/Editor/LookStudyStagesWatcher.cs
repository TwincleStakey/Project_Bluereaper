// Builds the three stage scenes when ArtSource/ls_stages_cmd.txt appears, then reopens the scene that was open.
// Result goes to ArtSource/ls_stages_log.txt.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LookStudyStagesWatcher
{
    static double next;
    static readonly string Dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtSource");

    static LookStudyStagesWatcher() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1.0;
        var cmd = Path.Combine(Dir, "ls_stages_cmd.txt");
        if (!Directory.Exists(Dir) || !File.Exists(cmd) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
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
