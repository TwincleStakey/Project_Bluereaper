using UnityEngine;

namespace Underworld
{
    // Slow bob / spin / sway for floating pagodas and hanging chains.
    public class UWFloat : MonoBehaviour
    {
        public float bob = 0.3f, bobSpeed = 0.5f, spin = 3f, swayDeg = 0f, phase;
        Vector3 p0; Quaternion r0; bool init;
        public void Tick(float t)
        {
            if (!init) { p0 = transform.localPosition; r0 = transform.localRotation; init = true; }
            transform.localPosition = p0 + Vector3.up * Mathf.Sin(t * bobSpeed + phase) * bob;
            transform.localRotation = r0 * Quaternion.Euler(Mathf.Sin(t * 0.7f + phase) * swayDeg, t * spin, 0);
        }
        void Update() { Tick(Time.time); }
    }
}
