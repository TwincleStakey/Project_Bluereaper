#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Keeps the editor working while it is in the background, so script/asset changes made from outside
// (Claude, Blender exports) import and compile without anyone clicking the window.
// - Interaction Mode: No Throttling.
// - Watches Assets/ and refreshes the AssetDatabase shortly after files change.
[InitializeOnLoad]
static class BackgroundRefresh
{
    static FileSystemWatcher watcher;
    static volatile bool dirty;
    static double nextCheck;

    static BackgroundRefresh()
    {
        if (EditorPrefs.GetInt("InteractionMode", 0) != 1) EditorPrefs.SetInt("InteractionMode", 1);
        watcher = new FileSystemWatcher(Path.GetFullPath("Assets")) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName };
        FileSystemEventHandler mark = (s, e) => { if (!e.FullPath.EndsWith(".meta")) dirty = true; };
        watcher.Changed += mark; watcher.Created += mark; watcher.Deleted += mark;
        watcher.Renamed += (s, e) => dirty = true;
        watcher.EnableRaisingEvents = true;
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += () => { if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; } };
    }

    static void Tick()
    {
        if (!dirty || EditorApplication.timeSinceStartup < nextCheck) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        nextCheck = EditorApplication.timeSinceStartup + 1.5;
        dirty = false;
        AssetDatabase.Refresh();
    }
}
#endif
