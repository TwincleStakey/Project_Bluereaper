// Keeps the falling snow/ash emitter over what the camera looks at (world-space particles, so they do not slide).
using UnityEngine;

namespace SnowPlague
{
    public class SPFollowCamera : MonoBehaviour
    {
        public float ahead = 20f;    // metres along the view to the ground focus
        public float height = 9f;

        void LateUpdate()
        {
            var cam = Camera.main; if (!cam) return;
            var f = cam.transform.position + cam.transform.forward * ahead;
            transform.position = new Vector3(f.x, height, f.z);
        }
    }
}
