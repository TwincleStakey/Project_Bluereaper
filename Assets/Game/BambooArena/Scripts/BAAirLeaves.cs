// Bamboo leaves drifting down through the air in front of the camera. One particle system that follows the point the
// camera looks at (a little above head height); leaves die in the air, no lights, no shadows.
using UnityEngine;

namespace BambooArena
{
    [RequireComponent(typeof(ParticleSystem))]
    public class BAAirLeaves : MonoBehaviour
    {
        public float height = 7f;
        BambooField field;
        ParticleSystem ps;

        void OnEnable() { ps = GetComponent<ParticleSystem>(); }

        void LateUpdate()
        {
            if (!field) field = FindAnyObjectByType<BambooField>();
            var cam = Camera.main;
            if (!cam) return;
            float y = (field ? field.transform.position.y : 0) + height;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Mathf.Abs(ray.direction.y) > 0.01f)
            {
                float t = (y - ray.origin.y) / ray.direction.y;
                if (t > 0) transform.position = ray.origin + ray.direction * t;
            }
            if (!field) return;
            float a = field.windAngle * Mathf.Deg2Rad;
            var w = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * (0.3f + 1.2f * field.windStrength);
            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(w.x * 0.5f, w.x * 1.3f);
            vel.z = new ParticleSystem.MinMaxCurve(w.z * 0.5f, w.z * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.75f, -0.35f);
        }
    }
}
