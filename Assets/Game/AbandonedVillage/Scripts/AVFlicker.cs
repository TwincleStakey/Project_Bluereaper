// Ambient motion for the abandoned village test scene.
using UnityEngine;

namespace AbandonedVillage
{
    public sealed class AVFlicker : MonoBehaviour
    {
        public float baseIntensity = 4f;
        public float amount = 0.25f;
        public float speed = 6f;
        Light lightRef;
        float seed;

        void Awake() { lightRef = GetComponent<Light>(); seed = Random.value * 100f; }

        void Update()
        {
            if (!lightRef) return;
            float n = Mathf.PerlinNoise(seed, Time.time * speed);
            lightRef.intensity = baseIntensity * (1f - amount + amount * 2f * n);
        }
    }
}

