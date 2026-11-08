// Runs ArtSource/ls_cmd.txt ("build") without clicking and writes the result to ArtSource/ls_log.txt.
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LookStudyWatcher
{
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
        if (!File.Exists(cmd) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(cmd);
        string result;
        try { result = LookStudyEditor.BuildInOpenEditor(Path.Combine(Dir, "LookStudyShots")); }
        catch (System.Exception e) { result = "EXCEPTION " + e; }
        File.WriteAllText(Path.Combine(Dir, "ls_log.txt"), result);
    }
}
