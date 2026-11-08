using UnityEngine;

namespace Underworld
{
    // Lantern / orb light flicker.
    public class UWFlicker : MonoBehaviour
    {
        public float baseIntensity = 3f, amount = 0.18f, speed = 7f, phase;
        Light l;
        void Awake() { l = GetComponent<Light>(); }
        public void Tick(float t) { if (l) l.intensity = baseIntensity * (1f + amount * (Mathf.PerlinNoise(t * speed, phase) * 2f - 1f)); }
        void Update() { Tick(Time.time); }
    }
}
