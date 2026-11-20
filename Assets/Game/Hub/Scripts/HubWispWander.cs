// 거점 ghost fires (11-20): drifts a downloaded fire VFX (Wallcoeur "Fire Toon Particle", itch.io, free) slowly round
// the court on a smooth wandering path (two sine pairs, per-wisp phase), bobbing a little.
using UnityEngine;

namespace Hub
{
    public sealed class HubWispWander : MonoBehaviour
    {
        public Vector3 centre;
        public Vector2 radius = new Vector2(3f, 2f);
        public float speed = 0.12f, bob = 0.25f, seed;

        void Start() { if (centre == Vector3.zero) centre = transform.position; }

        void Update()
        {
            float t = Time.time * speed + seed;
            var p = centre + new Vector3(
                Mathf.Sin(t * 1.0f) * radius.x + Mathf.Sin(t * 2.3f + 1.7f) * radius.x * 0.3f,
                Mathf.Sin(t * 3.1f + seed) * bob,
                Mathf.Cos(t * 0.8f) * radius.y + Mathf.Sin(t * 1.9f + 0.4f) * radius.y * 0.3f);
            transform.position = p;
        }
    }
}
