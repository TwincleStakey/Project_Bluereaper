// Caps the frame rate at 60 for every scene (RoomRun, dev fields, editor Play mode) so tests don't run the GPU flat out.
using UnityEngine;

public static class FrameCap
{
    public const int Fps = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Fps;
    }
}
