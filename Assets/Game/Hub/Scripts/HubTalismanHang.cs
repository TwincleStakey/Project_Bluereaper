// 거점 gate talismans (11-20): emits one static talisman particle at each stored local point (hung along the chains),
// slightly tilted. The ParticleSystem itself only renders them (no bursts); HubSealGate clears it when the seal burns.
using UnityEngine;

namespace Hub
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class HubTalismanHang : MonoBehaviour
    {
        public Vector3[] points;   // local to this transform
        public float[] tilt;       // degrees
        public float[] scale;      // per talisman size factor (varied)
        public Vector2 size = new Vector2(0.3f, 0.99f);

        void Start()
        {
            var ps = GetComponent<ParticleSystem>();
            if (points == null) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false, startLifetime = 1e6f, velocity = Vector3.zero };
            for (int i = 0; i < points.Length; i++)
            {
                ep.position = points[i];
                float k = scale != null && i < scale.Length ? scale[i] : 1f;
                ep.startSize3D = new Vector3(size.x * k, size.y * k, 1f);
                ep.rotation = tilt != null && i < tilt.Length ? tilt[i] : 0f;
                ps.Emit(ep, 1);
            }
        }
    }
}
