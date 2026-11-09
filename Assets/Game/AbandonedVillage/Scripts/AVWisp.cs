// Ambient motion for the abandoned village test scene.
using UnityEngine;

namespace AbandonedVillage
{
    public sealed class AVWisp : MonoBehaviour
    {
        public float radius = 0.6f;
        public float bob = 0.35f;
        public float speed = 0.6f;
        Vector3 home;
        float seed;

        void Awake() { home = transform.localPosition; seed = Random.value * 10f; }

        void Update()
        {
            float t = Time.time * speed + seed;
            transform.localPosition = home + new Vector3(Mathf.Sin(t) * radius, Mathf.Sin(t * 1.7f) * bob, Mathf.Cos(t * 0.8f) * radius);
        }
    }
}

