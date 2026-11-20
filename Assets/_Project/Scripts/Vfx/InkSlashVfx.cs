using System.Collections.Generic;
using UnityEngine;

namespace BlueReaper.Vfx
{
    /// <summary>
    /// 무사 평타 연출 (VFX 시안 03 수묵 + 광원, 2026-11-14 선택).
    /// 휘두르기: 먹 붓 획(부채꼴) + 바깥 테두리 청록 빛 + 칼끝 따라 움직이는 실제 조명.
    /// 적중: 빛 번쩍 + 조명 + 먹물 튐(바닥에 떨어지면 먹 자국).
    /// 판정과 같은 사거리·각도로 그려서 "보이는 범위 = 맞는 범위"가 되게 한다.
    /// 메시·머티리얼은 코드로 만든다 (프리팹에 따로 배치할 것 없음).
    /// </summary>
    public class InkSlashVfx : MonoBehaviour
    {
        [Header("재질 원본 (기존 VFX 머티리얼 재사용)")]
        [Tooltip("반투명 파티클 머티리얼 (M_VFX_Dust)")]
        [SerializeField] Material alphaSource;
        [Tooltip("가산(빛) 파티클 머티리얼 (M_VFX_GlowAdd)")]
        [SerializeField] Material glowSource;
        [Tooltip("먹물 방울 모양 (T_VFX_Splat)")]
        [SerializeField] Texture2D splatTexture;

        [Header("색")]
        [SerializeField] Color inkColor = new(0.02f, 0.02f, 0.03f, 1f);
        [SerializeField, ColorUsage(true, true)] Color glowColor = new(0.35f, 2.2f, 2.0f, 1f);

        [Header("모양")]
        [Tooltip("획 안쪽 반지름 = 사거리 × 이 값")]
        [SerializeField] float innerRatio = 0.45f;
        [SerializeField] float strokeTime = 0.07f;
        [SerializeField] float strokeLife = 0.45f;
        [SerializeField] float lightIntensity = 3f;

        const int ArcSegments = 28;

        Material inkMat, rimMat, glowMat, splatMat;
        readonly List<Fx> active = new();
        readonly Stack<Fx> pool = new();
        Camera cam;

        class Fx
        {
            public GameObject go;
            public Mesh mesh;
            public MeshRenderer mr;
            public Light light;
            public float age, life;
            public System.Action<Fx> tick;
            // 공용 데이터
            public Vector3 center, dir, vel;
            public float r0, r1, arc, height, tilt, size, seed;
            public int sweep;
            public bool grounded;
        }

        void Awake()
        {
            inkMat = new Material(alphaSource) { name = "InkStroke" };
            inkMat.SetTexture("_BaseMap", BuildBrushTexture());
            inkMat.SetColor("_BaseColor", inkColor);
            rimMat = new Material(glowSource) { name = "InkRim" };
            rimMat.SetColor("_BaseColor", glowColor);
            glowMat = new Material(glowSource) { name = "InkGlow" };
            glowMat.SetColor("_BaseColor", glowColor);
            splatMat = new Material(alphaSource) { name = "InkSplat" };
            if (splatTexture != null) splatMat.SetTexture("_BaseMap", splatTexture);
            splatMat.SetColor("_BaseColor", inkColor);
        }

        void LateUpdate()
        {
            if (cam == null) cam = Camera.main;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Fx fx = active[i];
                fx.age += Time.deltaTime;
                if (fx.age >= fx.life)
                {
                    Release(fx);
                    active.RemoveAt(i);
                    continue;
                }
                fx.tick(fx);
            }
        }

        // ---------------------------------------------------------------- 외부 호출
        /// <summary>휘두를 때: 가로베기 (오른→왼).</summary>
        public void PlaySwing(Vector3 origin, Vector3 dir, float range, float arc)
        {
            dir.y = 0f; dir.Normalize();
            Fx s = Take(inkMat, false);
            s.center = origin; s.dir = dir; s.arc = arc; s.r0 = range * innerRatio; s.r1 = range;
            s.sweep = -1; s.height = 1.15f; s.tilt = 10f;
            s.life = strokeLife; s.tick = TickStroke;
            Fx rim = Take(rimMat, true);
            rim.center = origin; rim.dir = dir; rim.arc = arc; rim.r0 = range * 0.96f; rim.r1 = range * 1.04f;
            rim.sweep = s.sweep; rim.height = s.height; rim.tilt = s.tilt; rim.life = strokeLife * 0.75f; rim.tick = TickRim;
            rim.light.range = range * 2f;
        }

        /// <summary>적을 맞힌 지점. outward = 나 → 적 방향.</summary>
        public void PlayHit(Vector3 point, Vector3 outward, float groundY)
        {
            Fx flash = Take(glowMat, true);
            flash.center = point; flash.size = 0.9f; flash.life = 0.14f; flash.tick = TickFlash;
            flash.light.range = 3f;

            const int n = 8;
            for (int i = 0; i < n; i++)
            {
                Fx d = Take(splatMat, false);
                float s = Random.value;
                Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
                d.center = point;
                d.vel = (outward * Random.Range(1.5f, 4.5f) + side * Random.Range(-2.5f, 2.5f) + Vector3.up * Random.Range(1f, 3.5f));
                d.size = Random.Range(0.08f, 0.2f);
                d.height = groundY + 0.02f; d.seed = s; d.grounded = false; d.life = 1.8f; d.tick = TickDrop;
            }
        }

        // ---------------------------------------------------------------- 매 프레임
        void TickStroke(Fx fx)
        {
            float reveal = Mathf.Clamp01(fx.age / strokeTime);
            float fade = 1f - Mathf.Clamp01((fx.age - strokeTime) / (fx.life - strokeTime));
            BuildArc(fx, reveal, (u, v) =>
            {
                // 붓을 대는 쪽(획 시작)은 옅게, 끝으로 갈수록 진하게
                float a = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, u * 2f)) * fade;
                return new Color(1f, 1f, 1f, a);
            });
        }

        void TickRim(Fx fx)
        {
            float reveal = Mathf.Clamp01(fx.age / strokeTime);
            float fade = 1f - fx.age / fx.life;
            BuildArc(fx, reveal, (u, v) => new Color(1f, 1f, 1f, fade * fade * Mathf.Sin(v * Mathf.PI)));
            // 조명: 획의 머리(칼끝 자리)를 따라간다
            Vector3 head = ArcPoint(fx, Mathf.Max(0.02f, reveal), fx.r1);
            fx.light.transform.position = head + Vector3.up * 0.2f;
            fx.light.intensity = lightIntensity * fade * fade;
        }

        void TickFlash(Fx fx)
        {
            float k = fx.age / fx.life;
            float s = fx.size * Mathf.Lerp(0.6f, 1.1f, k);
            Billboard(fx, fx.center, s, new Color(1f, 1f, 1f, 1f - k));
            fx.light.transform.position = fx.center;
            fx.light.intensity = lightIntensity * 1.3f * (1f - k);
        }

        void TickDrop(Fx fx)
        {
            float dt = Time.deltaTime;
            if (!fx.grounded)
            {
                fx.vel += Physics.gravity * dt;
                fx.center += fx.vel * dt;
                if (fx.center.y <= fx.height)
                {
                    fx.center.y = fx.height;
                    fx.grounded = true;
                    fx.size *= 1.8f;   // 바닥에 퍼지면서 커진다
                }
                Billboard(fx, fx.center, fx.size, Color.white);
            }
            else
            {
                float k = Mathf.Clamp01((fx.age - fx.life * 0.6f) / (fx.life * 0.4f));
                GroundQuad(fx, fx.center, fx.size, fx.seed * 360f, new Color(1f, 1f, 1f, 0.9f * (1f - k)));
            }
        }

        // ---------------------------------------------------------------- 메시 만들기
        Vector3 ArcPoint(Fx fx, float u, float r)
        {
            // sweep -1: 오른쪽(+각)에서 왼쪽으로, +1: 왼쪽에서 오른쪽으로
            float ang = fx.sweep < 0 ? Mathf.Lerp(fx.arc * 0.5f, -fx.arc * 0.5f, u) : Mathf.Lerp(-fx.arc * 0.5f, fx.arc * 0.5f, u);
            Vector3 flat = Quaternion.Euler(0f, ang, 0f) * fx.dir * r;
            Vector3 tilted = Quaternion.AngleAxis(fx.tilt, fx.dir) * flat;
            return fx.center + Vector3.up * fx.height + tilted;
        }

        readonly List<Vector3> vbuf = new();
        readonly List<Vector2> ubuf = new();
        readonly List<Color> cbuf = new();
        readonly List<int> ibuf = new();

        void BuildArc(Fx fx, float reveal, System.Func<float, float, Color> color)
        {
            vbuf.Clear(); ubuf.Clear(); cbuf.Clear(); ibuf.Clear();
            int segs = Mathf.Max(1, Mathf.CeilToInt(ArcSegments * reveal));
            for (int i = 0; i <= segs; i++)
            {
                float u = reveal * i / segs;
                vbuf.Add(ArcPoint(fx, u, fx.r0)); ubuf.Add(new Vector2(u, 0f)); cbuf.Add(color(u, 0f));
                vbuf.Add(ArcPoint(fx, u, fx.r1)); ubuf.Add(new Vector2(u, 1f)); cbuf.Add(color(u, 1f));
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    ibuf.Add(a); ibuf.Add(a + 1); ibuf.Add(a + 3);
                    ibuf.Add(a); ibuf.Add(a + 3); ibuf.Add(a + 2);
                }
            }
            Apply(fx);
        }

        void Billboard(Fx fx, Vector3 c, float size, Color col)
        {
            Vector3 right = cam != null ? cam.transform.right : Vector3.right;
            Vector3 up = cam != null ? cam.transform.up : Vector3.up;
            Quad(fx, c - right * size - up * size, c + right * size - up * size, c + right * size + up * size, c - right * size + up * size, col);
        }

        void GroundQuad(Fx fx, Vector3 c, float size, float rotDeg, Color col)
        {
            Quaternion q = Quaternion.Euler(0f, rotDeg, 0f);
            Vector3 r = q * Vector3.right * size, f = q * Vector3.forward * size;
            Quad(fx, c - r - f, c + r - f, c + r + f, c - r + f, col);
        }

        void Quad(Fx fx, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            vbuf.Clear(); ubuf.Clear(); cbuf.Clear(); ibuf.Clear();
            vbuf.Add(a); vbuf.Add(b); vbuf.Add(c); vbuf.Add(d);
            ubuf.Add(new Vector2(0, 0)); ubuf.Add(new Vector2(1, 0)); ubuf.Add(new Vector2(1, 1)); ubuf.Add(new Vector2(0, 1));
            for (int i = 0; i < 4; i++) cbuf.Add(col);
            ibuf.Add(0); ibuf.Add(2); ibuf.Add(1); ibuf.Add(0); ibuf.Add(3); ibuf.Add(2);
            Apply(fx);
        }

        void Apply(Fx fx)
        {
            Mesh m = fx.mesh;
            m.Clear();
            m.SetVertices(vbuf); m.SetUVs(0, ubuf); m.SetColors(cbuf); m.SetTriangles(ibuf, 0);
            m.RecalculateBounds();
        }

        // ---------------------------------------------------------------- 풀
        Fx Take(Material mat, bool withLight)
        {
            Fx fx = pool.Count > 0 ? pool.Pop() : Create();
            fx.go.SetActive(true);
            fx.mr.sharedMaterial = mat;
            fx.age = 0f;
            fx.light.enabled = withLight;
            fx.light.color = new Color(glowColor.r, glowColor.g, glowColor.b) / Mathf.Max(glowColor.maxColorComponent, 0.001f);
            fx.light.intensity = 0f;
            fx.light.range = 3f;
            active.Add(fx);
            return fx;
        }

        Fx Create()
        {
            var go = new GameObject("InkFx");
            // 월드 좌표로 메시를 그리므로 플레이어를 따라 움직이지 않게 루트에 둔다
            go.hideFlags = HideFlags.DontSave;
            var fx = new Fx { go = go, mesh = new Mesh { name = "InkFx" } };
            fx.mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = fx.mesh;
            fx.mr = go.AddComponent<MeshRenderer>();
            fx.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fx.mr.receiveShadows = false;
            fx.light = go.AddComponent<Light>();
            fx.light.type = LightType.Point;
            fx.light.shadows = LightShadows.None;
            return fx;
        }

        void Release(Fx fx)
        {
            fx.mesh.Clear();
            fx.light.enabled = false;
            fx.go.SetActive(false);
            pool.Push(fx);
        }

        void OnDestroy()
        {
            foreach (var fx in active) if (fx.go) Destroy(fx.go);
            foreach (var fx in pool) if (fx.go) Destroy(fx.go);
        }

        /// <summary>마른 붓 느낌 텍스처: 가로(획 진행)로 결이 있고, 위아래 가장자리가 들쭉날쭉.</summary>
        static Texture2D BuildBrushTexture()
        {
            const int w = 256, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "T_InkBrush" };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                // 획 양 끝은 가늘게
                float taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(u / 0.15f, (1f - u) / 0.08f));
                float top = 1f - 0.18f * Mathf.PerlinNoise(u * 18f, 1.3f) - (1f - taper) * 0.35f;
                float bot = 0.12f * Mathf.PerlinNoise(u * 22f, 7.7f) + (1f - taper) * 0.35f;
                for (int y = 0; y < h; y++)
                {
                    float v = y / (float)(h - 1);
                    float inside = Mathf.Clamp01((v - bot) * 25f) * Mathf.Clamp01((top - v) * 25f);
                    // 붓털 결: 가로로 긴 줄무늬
                    float hair = 0.55f + 0.45f * Mathf.PerlinNoise(u * 3f, v * 40f);
                    float dry = Mathf.Clamp01(Mathf.PerlinNoise(u * 30f, v * 6f) * 1.6f - 0.25f);
                    float a = inside * Mathf.Clamp01(hair * Mathf.Lerp(0.6f, 1f, dry));
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
