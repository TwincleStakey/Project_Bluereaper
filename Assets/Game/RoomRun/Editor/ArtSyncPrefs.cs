#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RoomRun.EditorTools
{
    // Applies the same in-game view toggles Kido uses (PlayerPrefs live per machine, not in git),
    // so a teammate who clones the Art branch sees the stages the same way.
    // Runs once automatically per machine; re-run any time from Tools > Art Sync.
    [InitializeOnLoad]
    static class ArtSyncPrefs
    {
        const string DoneKey = "RR.ArtSync.Applied.20261120";

        static ArtSyncPrefs()
        {
            if (EditorPrefs.GetBool(DoneKey, false)) return;
            EditorApplication.delayCall += () => { Apply(); EditorPrefs.SetBool(DoneKey, true); };
        }

        [MenuItem("Tools/Art Sync/Apply Kido view settings")]
        public static void Apply()
        {
            // values copied from Kido's academy PC on 2026-11-20
            PlayerPrefs.SetInt("RR.ShadowPass", 0);
            PlayerPrefs.SetInt("RR.ShadowPass.Default5", 1);
            PlayerPrefs.SetInt("RR.LightRadius", 1);
            PlayerPrefs.SetInt("RR.Whip", 0);
            PlayerPrefs.SetInt("RR.Eogeom", 0);
            PlayerPrefs.SetInt("RR.SeeThrough", 2);
            PlayerPrefs.SetInt("RR.SeeThroughTemporal", 0);
            PlayerPrefs.SetInt("RR.SeeV2Moved", 1);
            PlayerPrefs.SetFloat("RR.SeeInner", 0.5f);
            PlayerPrefs.SetFloat("RR.SeeOuter", 6.5f);
            PlayerPrefs.SetFloat("RR.SeeMin", 0.35f);
            PlayerPrefs.SetFloat("RR.SeeCurve", 1.2f);
            PlayerPrefs.SetFloat("RR.See.Inner", 3f);
            PlayerPrefs.SetFloat("RR.See.Outer", 10f);
            PlayerPrefs.SetFloat("RR.See.Min", 0.8f);
            PlayerPrefs.SetFloat("RR.See.Curve", 4f);
            PlayerPrefs.SetInt("RR.DevTab", 0);
            PlayerPrefs.Save();
            Debug.Log("[ArtSync] Kido view settings applied (PlayerPrefs). Enter Play in RoomRun.unity.");
        }

        [MenuItem("Tools/Art Sync/Open RoomRun scene")]
        static void OpenScene()
        {
            if (EditorApplication.isPlaying) return;
            if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Game/RoomRun/Scenes/RoomRun.unity");
        }
    }
}
#endif
