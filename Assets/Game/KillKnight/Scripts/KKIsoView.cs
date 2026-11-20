// 심연 고원 (11-19, Kido: "킬나이트는 아이소매트릭인데 저런식으로"): this map's own camera, KILL KNIGHT-like diagonal view.
// While the stage is loaded the RoomRun camera turns 45 degrees round the player and looks down at 45 degrees, still
// locked on the player; WASD stays screen-relative (InputReader reads CameraRig.ViewYaw). On entry the rig's zoom is set
// once to this map's wider framing (v3: player ~1/20 of the screen like the KILL KNIGHT frame; this also overrides the
// game-wide closer camera for this map only); the wheel still zooms in from there. Unloading the stage puts yaw/pitch
// back to 0 and the zoom back to what it was, so no other map changes. Captures pass z=2.1 explicitly.
using RoomRun;
using UnityEngine;

namespace KillKnight
{
    [DefaultExecutionOrder(-50)]
    public sealed class KKIsoView : MonoBehaviour
    {
        public const float Yaw = 45f, Pitch = 45f, Zoom = 2.1f;
        CameraRig rigSet; float zoomBefore = -1f;

        void OnEnable() { CameraRig.ViewYaw = Yaw; CameraRig.ViewPitch = Pitch; rigSet = null; }
        void OnDisable()
        {
            CameraRig.ViewYaw = 0f; CameraRig.ViewPitch = 0f;
            if (rigSet && zoomBefore > 0f) rigSet.Zoom = zoomBefore;
            rigSet = null;
        }
        void LateUpdate()
        {
            var run = RunDirector.Instance;
            var rig = run ? run.Rig : null;
            if (!rig || rig == rigSet || run.Transitioning) return;
            rigSet = rig; zoomBefore = rig.Zoom; rig.Zoom = Zoom;
        }
    }
}
