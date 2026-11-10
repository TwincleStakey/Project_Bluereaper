// Pressing Play starts the room run from whatever scene is open, so every stage is reachable through its doors.
// Menu: RoomRun > Always Play From RoomRun (on by default) turns this off to play a single stage scene alone.
using UnityEditor;
using UnityEditor.SceneManagement;

namespace RoomRun
{
    [InitializeOnLoad]
    public static class RoomRunPlayStart
    {
        const string BootPath = "Assets/Game/RoomRun/Scenes/RoomRun.unity";
        const string Pref = "RoomRun.AlwaysPlayFromRoomRun";
        const string MenuPath = "Tools/RoomRun/Always Play From RoomRun";

        static RoomRunPlayStart()
        {
            EditorApplication.delayCall += Apply;
        }

        static bool Enabled => EditorPrefs.GetBool(Pref, true);

        static void Apply()
        {
            EditorSceneManager.playModeStartScene = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(BootPath) : null;
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            EditorPrefs.SetBool(Pref, !Enabled);
            Apply();
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Enabled);
            return true;
        }
    }
}
