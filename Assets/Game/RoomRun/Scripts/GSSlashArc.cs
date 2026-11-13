// Greatsword slash made for the top-down camera (11-13 look test, not attached to the player yet):
// a flat crescent mesh on the ground plane (RR_GSSlashArc shader: head-first reveal along the arc, noise erosion,
// bright rim + soft glow, leading-edge line) plus a small spark burst flung outward as the head passes.
// One draw call for the arc, one for the sparks. Aim = local +Z; mirror = negative local X scale.
using UnityEngine;

namespace RoomRun
{
    public sealed class GSSlashArc : MonoBehaviour
    {
        [System.Serializable]
        public struct Look
        {
            [ColorUsage(false, true)] public Color core, glow, edge, spark;
            public float arcDegrees, innerRadius, tail, sweepTime, fadeTime;
            public float bands, rim, bandStart, trailLine, haze;
            public int sparks;
        }

        public static readonly Look Ice = new Look
        {
            core = new Color(1.5f, 2.3f, 3.2f), glow = new Color(0.12f, 0.9f, 3.0f), edge = new Color(3f, 3.6f, 4.2f), spark = new Color(1.4f, 2.6f, 4f),
            arcDegrees = 210f, innerRadius = 0.3f, tail = 0.9f, sweepTime = 0.14f, fadeTime = 0.3f,
            bands = 3f, rim = 0.88f, bandStart = 0.12f, trailLine = 0.35f, haze = 0.02f, sparks = 45,
        };
        public static readonly Look Ember = new Look
        {
            core = new Color(2.6f, 0.9f, 0.3f), glow = new Color(2.4f, 0.16f, 0.03f), edge = new Color(4f, 2f, 0.8f), spark = new Color(4f, 1.2f, 0.25f),
            arcDegrees = 210f, innerRadius = 0.3f, tail = 0.9f, sweepTime = 0.14f, fadeTime = 0.3f,
            bands = 3f, rim = 0.88f, bandStart = 0.12f, trailLine = 0.35f, haze = 0.02f, sparks = 55,
        };
        public static readonly Look Ghost = new Look
        {
            core = new Color(1.4f, 1.6f, 1.8f), glow = new Color(0.55f, 0.7f, 1.0f), edge = new Color(2.6f, 2.8f, 3.2f), spark = new Color(1.6f, 1.8f, 2.4f),
            arcDegrees = 210f, innerRadius = 0.3f, tail = 0.9f, sweepTime = 0.14f, fadeTime = 0.3f,
            bands = 3f, rim = 0.88f, bandStart = 0.12f, trailLine = 0.35f, haze = 0.02f, sparks = 40,
        };
        /// Kido's reference (11-13): near-full violet ring, thick banded white-pink head, thin dim trail line, inner haze.
        public static readonly Look VioletRing = new Look
        {
            core = new Color(3.2f, 1.6f, 3.4f), glow = new Color(1.8f, 0.22f, 3.2f), edge = new Color(4.5f, 3f, 4.8f), spark = new Color(3.5f, 1.4f, 4f),
            arcDegrees = 330f, innerRadius = 0.04f, tail = 0.34f, sweepTime = 0.18f, fadeTime = 0.3f,
            bands = 4f, rim = 0.93f, bandStart = 0.55f, trailLine = 0.9f, haze = 0.035f, sparks = 40,
        };

        public Texture noiseTex, streakTex, sparkTex;
        public Look look = Ice;

        MeshRenderer mr;
        Material mat;
        ParticleSystem ps;
        float t = -1f, lastHead;
        float seed;

        Mesh arcMesh;
        float arcDegrees => look.arcDegrees;
        float sweepTime => look.sweepTime;
        float fadeTime => look.fadeTime;
        float tail => look.tail;
        static readonly int PHead = Shader.PropertyToID("_Head"), PTail = Shader.PropertyToID("_Tail"), PDis = Shader.PropertyToID("_Dissolve"),
            PSeed = Shader.PropertyToID("_Seed");

        public bool Playing => t >= 0f;

        public void Build()
        {
            if (mr) return;
            arcMesh = MakeArc(look.arcDegrees, look.innerRadius);
            var mf = gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = arcMesh;
            mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            mat = new Material(Shader.Find("RoomRun/GSSlashArc")) { name = "GS Slash Arc" };
            if (noiseTex) mat.SetTexture("_NoiseTex", noiseTex);
            if (streakTex) mat.SetTexture("_StreakTex", streakTex);
            mr.sharedMaterial = mat;
            mr.enabled = false;
            ApplyLook();

            var go = new GameObject("Sparks"); go.transform.SetParent(transform, false);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startSpeed = 0f; main.maxParticles = 200; main.gravityModifier = 0f;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var vol = ps.limitVelocityOverLifetime; vol.enabled = true; vol.limit = 0f; vol.dampen = 0.12f;
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var sm = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "GS Slash Sparks" };
            if (sparkTex) sm.SetTexture("_BaseMap", sparkTex);
            sm.SetFloat("_Surface", 1f); sm.SetFloat("_Blend", 2f);
            sm.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            sm.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            sm.SetFloat("_ZWrite", 0f);
            sm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            sm.renderQueue = 3010;
            r.sharedMaterial = sm;
        }

        public void ApplyLook()
        {
            if (!mat) return;
            mat.SetColor("_CoreColor", look.core); mat.SetColor("_GlowColor", look.glow); mat.SetColor("_EdgeColor", look.edge);
            mat.SetFloat("_Bands", look.bands); mat.SetFloat("_Rim", look.rim); mat.SetFloat("_BandStart", look.bandStart);
            mat.SetFloat("_TrailLine", look.trailLine); mat.SetFloat("_Haze", look.haze); mat.SetFloat("_Tail", look.tail);
        }

        /// Start a swing. radius in metres; mirrored = the swing runs the other way round.
        public void Play(float radius, bool mirrored)
        {
            Build();
            transform.localScale = new Vector3(mirrored ? -radius : radius, radius, radius);
            t = 0f; lastHead = 0f; seed = Random.value * 10f;
            mat.SetFloat(PSeed, seed);
            mat.SetFloat(PTail, tail);
            mr.enabled = true;
            ps.Clear(true);
            Apply();
        }

        void Update() { if (t >= 0f) Tick(Time.deltaTime); }

        /// Advance by dt (also used by the editor preview to step frames).
        public void Tick(float dt)
        {
            if (t < 0f) return;
            t += dt;
            if (t > sweepTime + fadeTime) { t = -1f; mr.enabled = false; return; }
            Apply();
        }

        void Apply()
        {
            float head = t <= sweepTime ? t / sweepTime : 1f + tail * (t - sweepTime) / fadeTime;
            // ease out: fast start, settles at the end of the arc
            float h = head <= 1f ? 1f - (1f - head) * (1f - head) : head;
            float dis = t <= sweepTime ? 0f : Mathf.Clamp01((t - sweepTime) / fadeTime);
            mat.SetFloat(PHead, h);
            mat.SetFloat(PDis, dis * dis);
            EmitSparks(lastHead, Mathf.Min(h, 1f));
            lastHead = Mathf.Min(h, 1f);
        }

        void EmitSparks(float from, float to)
        {
            if (!ps || to <= from || look.sparks <= 0) return;
            int n = Mathf.RoundToInt(look.sparks * (to - from));
            var ep = new ParticleSystem.EmitParams();
            var c = look.spark;
            float mirror = Mathf.Sign(transform.lossyScale.x);
            float rad = Mathf.Abs(transform.lossyScale.z);
            for (int i = 0; i < n; i++)
            {
                float u = Random.Range(from, to);
                float a = ArcAngle(u, arcDegrees) * Mathf.Deg2Rad;
                var local = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var tangent = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a));   // direction of travel along the arc
                Vector3 pLocal = local * Random.Range(look.rim - 0.15f, look.rim + 0.02f);
                pLocal.x *= mirror; tangent.x *= mirror; local.x *= mirror;
                ep.position = transform.position + transform.rotation * (pLocal * rad);
                var dir = transform.rotation * (local * Random.Range(0.6f, 1.2f) + tangent * Random.Range(0.6f, 1.6f));
                ep.velocity = dir * Random.Range(6f, 13f) + Vector3.up * Random.Range(0f, 1.2f);
                ep.startColor = c * Random.Range(1.2f, 2.2f);
                ep.startSize = Random.Range(0.3f, 0.6f) * Mathf.Max(1f, rad * 0.25f);
                ep.startLifetime = Random.Range(0.18f, 0.38f);
                ps.Emit(ep, 1);
            }
        }

        /// u 0..1 along the arc -> degrees from +Z (swing starts on the left, ends on the right).
        static float ArcAngle(float u, float deg) => -deg * 0.5f + deg * u;

        static Mesh MakeArc(float deg, float inner)
        {
            const int seg = 96, rings = 16;
            var verts = new Vector3[(seg + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            var tris = new int[seg * rings * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg;
                float a = ArcAngle(u, deg) * Mathf.Deg2Rad;
                for (int j = 0; j <= rings; j++)
                {
                    float v = j / (float)rings;
                    float r = Mathf.Lerp(inner, 1f, v);
                    int k = i * (rings + 1) + j;
                    verts[k] = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                    uvs[k] = new Vector2(u, v);
                }
            }
            int ti = 0;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < rings; j++)
                {
                    int a0 = i * (rings + 1) + j, a1 = a0 + rings + 1;
                    tris[ti++] = a0; tris[ti++] = a0 + 1; tris[ti++] = a1;
                    tris[ti++] = a1; tris[ti++] = a0 + 1; tris[ti++] = a1 + 1;
                }
            var m = new Mesh { name = "GS Slash Arc", vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateBounds();
            return m;
        }
    }
}
