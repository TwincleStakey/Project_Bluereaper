// Ambient motion for the abandoned village test scene.
using UnityEngine;

namespace AbandonedVillage
{
    public sealed class AVCrowCircle : MonoBehaviour
    {
        public Vector3 center;
        public float radius = 6f;
        public float height = 9f;
        public float speed = 0.35f;
        public float phase;

        void Update()
        {
            float a = Time.time * speed + phase;
            var p = center + new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(a * 2.3f) * 0.6f, Mathf.Sin(a) * radius);
            var dir = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(speed);
            transform.position = p;
            transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 9f + phase) * 12f);
        }
    }
}

