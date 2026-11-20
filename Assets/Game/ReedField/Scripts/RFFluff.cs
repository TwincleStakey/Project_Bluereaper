// A few reed fluffs and dry leaf slivers drifting through the air on the field's wind. One particle system that
// follows the area the camera looks at, never touches the ground (particles die in the air), no lights, no shadows.
using UnityEngine;

namespace Reeds
{
    [RequireComponent(typeof(ParticleSystem))]
    public class RFFluff : MonoBehaviour
    {
        public float heightAboveField = 0.6f;
        ParticleSystem ps;
        ReedField field;

        void OnEnable() { ps = GetComponent<ParticleSystem>(); }

        void LateUpdate()
        {
            if (!field) field = FindAnyObjectByType<ReedField>();
            var cam = Camera.main;
            if (!field || !cam) return;
            // Centre on where the camera ray meets the reed tops.
            float y = field.transform.position.y + field.height + heightAboveField;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Mathf.Abs(ray.direction.y) > 0.01f)
            {
                float t = (y - ray.origin.y) / ray.direction.y;
                if (t > 0) transform.position = ray.origin + ray.direction * t;
            }
            // Drift with the field's wind; gusts push a little harder.
            float a = field.windAngle * Mathf.Deg2Rad;
            var wind = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (0.6f + 1.6f * field.windStrength);
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.7f, wind.x * 1.3f);
            vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.7f, wind.z * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.12f);
        }
    }
}
