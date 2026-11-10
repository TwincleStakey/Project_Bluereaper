// Frame pacing for every scene (RoomRun, dev fields, editor Play mode).
// 11-10 (Kido: "200 fps on a 4060"): uncapped by default so the real frame cost shows. The 60 fps cap and VSync are dev
// panel options (개발자 도구 > 프레임), saved in PlayerPrefs under these keys; LookStudyDevTools writes the same keys.
// Note: with VSync off, a monitor slower than the frame rate can show tearing (seen in the bamboo arena on a 60 Hz screen);
// turn VSync on there.
using UnityEngine;

public static class FrameCap
{
    public const int Fps = 60;
    public const string KeyCap = "RR.Cap60", KeyVSync = "RR.VSync";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = PlayerPrefs.GetInt(KeyVSync, 0) != 0 ? 1 : 0;
        Application.targetFrameRate = PlayerPrefs.GetInt(KeyCap, 0) != 0 ? Fps : -1;
        // Dev runs (editor Play, Development builds) keep running when another window has focus, so remote checks and
        // measurements are not frozen at frame 1 while someone works in another app. Release builds still pause.
        if (Application.isEditor || Debug.isDebugBuild) Application.runInBackground = true;
    }
}
