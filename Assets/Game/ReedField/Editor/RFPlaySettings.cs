// Keeps reed settings changed with the Play-mode keys (PgUp/PgDn height, Home/End wind, Insert mood):
// when Play stops, they are written into the reed field scene as its new defaults and the scene is saved.
using Reeds;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class RFPlaySettings
{
    const string Key = "ReedField.PlayTweak";

    static RFPlaySettings()
    {
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Apply; };
    }

    static void Apply()
    {
        if (!PlayerPrefs.HasKey(Key)) return;
        var t = JsonUtility.FromJson<ReedField.Tweak>(PlayerPrefs.GetString(Key));
        var active = EditorSceneManager.GetActiveScene();
        bool reopen = active.path != RFBuilder.ScenePath;
        if (reopen && active.isDirty) { Debug.LogWarning("[ReedField] Play tweaks not saved: the open scene has unsaved changes"); return; }
        PlayerPrefs.DeleteKey(Key);
        var prev = active.path;
        var scene = reopen ? EditorSceneManager.OpenScene(RFBuilder.ScenePath, OpenSceneMode.Single) : active;
        var f = Object.FindAnyObjectByType<ReedField>();
        if (f)
        {
            Undo.RecordObject(f, "Reed field play tweaks");
            f.height = t.height; f.windStrength = t.windStrength; f.mood = (ReedField.Mood)t.mood;
            EditorUtility.SetDirty(f);
            f.Generate(); f.ApplyMood();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ReedField] kept Play settings as defaults: height {t.height:0.00} m, wind {t.windStrength:0.0}, mood {(ReedField.Mood)t.mood}");
        }
        if (reopen && !string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
    }
}
