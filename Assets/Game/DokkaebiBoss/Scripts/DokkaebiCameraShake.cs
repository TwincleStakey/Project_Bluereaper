using UnityEngine;

namespace Dokkaebi
{
    // Trauma-based camera shake (shake = trauma^2), Perlin noise on position and roll.
    // Put it on the camera itself or on a child pivot of a follow rig; it offsets localPosition/localRotation.
    public sealed class DokkaebiCameraShake : MonoBehaviour
    {
        public float maxOffset = 0.45f;
        public float maxRollDeg = 2.5f;
        public float frequency = 22f;
        public float decayPerSecond = 1.6f;
        public bool manualTick;

        float trauma, t;
        Vector3 basePos; Quaternion baseRot; bool captured;

        public float Trauma => trauma;

        public void AddTrauma(float amount)
        {
            Capture();
            trauma = Mathf.Clamp01(trauma + amount);
        }

        void Capture()
        {
            if (captured) return;
            basePos = transform.localPosition; baseRot = transform.localRotation; captured = true;
        }

        void LateUpdate() { if (!manualTick) Tick(Time.deltaTime); }

        public void Tick(float dt)
        {
            if (!captured) return;
            t += dt;
            float s = trauma * trauma;
            float nx = Mathf.PerlinNoise(t * frequency, 0.1f) * 2 - 1;
            float ny = Mathf.PerlinNoise(0.7f, t * frequency) * 2 - 1;
            float nr = Mathf.PerlinNoise(t * frequency * 0.7f, 5.3f) * 2 - 1;
            // Mostly vertical bounce for a ground slam, less sideways.
            transform.localPosition = basePos + transform.localRotation * new Vector3(nx * 0.6f, ny, 0) * (maxOffset * s);
            transform.localRotation = baseRot * Quaternion.Euler(0, 0, nr * maxRollDeg * s);
            trauma = Mathf.Max(0, trauma - decayPerSecond * dt);
            if (trauma <= 0) { transform.localPosition = basePos; transform.localRotation = baseRot; }
        }
    }
}
