using UnityEngine;

namespace Underworld
{
    // Drives the shared shader clock. Preview tools set Manual and push time themselves.
    [ExecuteAlways]
    public class UWClock : MonoBehaviour
    {
        public static bool Manual;
        public static void Set(float t) => Shader.SetGlobalFloat("_UWTime", t);
        void Update() { if (!Manual) Set(Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 1000.0)); }
    }
}
