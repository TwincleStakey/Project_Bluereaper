// 도깨비불: a pale flame drifting just above the reed tops. Wanders around its home spot, bobs, flickers,
// sometimes goes out and lights up again somewhere nearby. Lights the plumes around it with a point light.
using UnityEngine;

namespace Reeds
{
    public class RFWisp : MonoBehaviour
    {
        public float roam = 7f;
        public float speed = 0.8f;
        public float baseHeight = 1.8f;
        public Light glowLight;
        public Renderer glow;
        public float lightIntensity = 2.2f;

        Vector3 home, target;
        float seed, fade = 1f, outTimer, waitOut;
        MaterialPropertyBlock mpb;

        // Start does not run again after a domain reload in Play mode (a script recompile or a forced asset refresh
        // while playing), and the non-serialized fields come back empty; Update re-initialises when that happened.
        void Start() { Init(); }

        void Init()
        {
            home = transform.position;
            seed = Random.value * 100f;
            target = Pick();
            waitOut = Random.Range(8f, 20f);
            mpb = new MaterialPropertyBlock();
        }

        Vector3 Pick()
        {
            var r = Random.insideUnitCircle * roam;
            return new Vector3(home.x + r.x, home.y, home.z + r.y);
        }

        void Update()
        {
            if (mpb == null) Init();
            float t = Time.time + seed;
            var field = FindField();
            float h = field ? field.height : baseHeight;
            var p = transform.position;
            var to = target - p; to.y = 0;
            if (to.magnitude < 0.5f) target = Pick();
            p += to.normalized * speed * Time.deltaTime * (0.6f + 0.4f * Mathf.Sin(t * 0.7f));
            p.y = home.y + h * 0.95f + 0.35f * Mathf.Sin(t * 1.3f) + 0.15f * Mathf.Sin(t * 3.1f);
            transform.position = p;

            // Goes out now and then and comes back somewhere else.
            waitOut -= Time.deltaTime;
            if (waitOut <= 0f && outTimer <= 0f) { outTimer = Random.Range(2f, 5f); waitOut = Random.Range(12f, 26f); }
            if (outTimer > 0f)
            {
                outTimer -= Time.deltaTime;
                fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime * 1.5f);
                if (fade <= 0f) { var r = Pick(); transform.position = new Vector3(r.x, p.y, r.z); target = Pick(); }
            }
            else fade = Mathf.MoveTowards(fade, 1f, Time.deltaTime * 0.8f);

            float flick = 0.75f + 0.25f * Mathf.PerlinNoise(t * 4f, seed);
            if (glowLight) glowLight.intensity = lightIntensity * flick * fade;
            if (glow && glow.sharedMaterial)
            {
                glow.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", glow.sharedMaterial.GetColor("_Color") * (flick * fade));
                glow.SetPropertyBlock(mpb);
            }
        }

        static ReedField cached;
        static ReedField FindField() { if (!cached) cached = FindAnyObjectByType<ReedField>(); return cached; }
    }
}
