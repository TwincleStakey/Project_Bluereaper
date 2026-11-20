using UnityEngine;

namespace LocoSplit
{
    // Top-down follow camera at the game's angle.
    public class LSCamera : MonoBehaviour
    {
        public Transform target;
        public float pitch = 55f, distance = 9f, yaw = 0f, height = 1f;

        void LateUpdate()
        {
            if (!target) return;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.rotation = rot;
            transform.position = target.position + Vector3.up * height - rot * Vector3.forward * distance;
        }
    }
}
