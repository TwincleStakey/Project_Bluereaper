// Greatsword cut debris (11-14 rewrite; Kido: "풀 배는것들 좀 리얼리스틱하게 ... 갈댄 훨씬 길게 팍팍 날라가야돼").
//
// Reeds: the clump really is cut. Reed.shader clips everything above a slanted cut line where the cut map says so (the
// stub keeps its shape). 11-14 Kido: the part above the cut no longer flies off as one piece; it shreds into many small
// leaf strips, stalk splinters and plume fluff thrown in every direction (pushed along the swing), falling like grass.
// Cut heights mirror Reed.shader (RFCutKeep / RFCutSeed / RFHash) and the cut map (VegetationCut + RR_VegCut), so the top
// starts exactly where the stub ends; a clump that is already cut throws nothing (or only what grew back).
//
// Grass: clippings shaped like blades (one tapered, curled, twisted strip), coloured and lit exactly like the field they
// came from (RR_CutBits mode 1 = LSFieldColor at the blade's root). Thrown along the swing in the order the blade passes,
// they tumble on the way up, then fall like leaves: slow terminal speed, rolling about their long axis, rocking and
// swaying side to side; they lie flat where they land for a moment and dissolve. Plus a little chaff.
//
// Everything is plain structs + Graphics.RenderMeshInstanced (no GameObjects): one draw per reed mesh/material/submesh
// and one per grass material, whatever the count. Caps: maxTops reed tops, maxBits bits (oldest recycled).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    public sealed class CutDebris : MonoBehaviour
    {
        public int maxTops = 220;
        public int maxBits = 2600;
        public int grassPerSwing = 80;
        public int reedPiecesPerSwing = 700;   // leaf strips + stalk splinters shared by all reed clumps one swing cuts
        float reedPiecesThisSwing;

        // ---------------------------------------------------------------- data
        struct Top
        {
            public Mesh mesh; public Material mat;
            public Matrix4x4 m0;                  // the clump where it stood
            public Vector3 com0, axis0;           // spin centre at launch, clump up axis
            public Vector3 pos, vel, spin; public Quaternion rot;
            public Vector4 cut, root;             // _RFPieceCut (lo, hi, dissolve), _RFPieceRoot
            public float delay, age, restLeft, ground, halfLen, drag, grav, restLift;
            public int bounces; public bool resting;
            public bool foliage, keepPose;        // foliage: LookStudy/Foliage piece (cut = tint); keepPose: rests as it lands (bushes)
        }

        struct Bit
        {
            public Vector3 pos, vel, spin, side, scale; public Quaternion rot;
            public Vector4 a, b;                  // _BitA (root x, z, rnd, mode), _BitB (colour, dissolve)
            public int mat;
            public float delay, age, maxAge, restLeft, fadeTime, ground, vt, dragH, phase, freq, sway, roll, grav;
            public bool fluff, resting;
        }

        Top[] tops; int topCount;
        Bit[] bits; int bitCount;

        Mesh blade;
        Shader bitShader;
        readonly List<Material> bitMats = new List<Material>();          // [0] = flat colour (modes 0 / 2)
        readonly Dictionary<Material, int> bitMatOf = new Dictionary<Material, int>();
        readonly Dictionary<Material, Material> pieceMats = new Dictionary<Material, Material>();

        // cut map mirror: per reed clump (quantised root) the map value written at time t
        struct CutRec { public float t, v; }
        readonly Dictionary<long, CutRec> cutRecs = new Dictionary<long, CutRec>();
        readonly List<long> pruneTmp = new List<long>();
        float nextPrune;

        static readonly int IdPieceCut = Shader.PropertyToID("_RFPieceCut"), IdPieceRoot = Shader.PropertyToID("_RFPieceRoot"),
            IdBitA = Shader.PropertyToID("_BitA"), IdBitB = Shader.PropertyToID("_BitB"), IdTint = Shader.PropertyToID("_Tint");
        static readonly string[] FieldProps = { "_ColA", "_ColB", "_ColC", "_RootDark", "_TipLift", "_BladeVar", "_PatchContrast", "_ClumpAmt" };

        void Awake()
        {
            tops = new Top[maxTops];
            bits = new Bit[maxBits];
            blade = BuildBlade();
            bitShader = Resources.Load<Shader>("RR_CutBits");
            if (!bitShader) Debug.LogWarning("[RoomRun] RR_CutBits shader missing: no grass clippings");
            else bitMats.Add(NewBitMat("GS Cut Bits"));
        }

        void OnDestroy()
        {
            if (blade) Destroy(blade);
            foreach (var m in bitMats) if (m) Destroy(m);
            foreach (var m in pieceMats.Values) if (m) Destroy(m);
        }

        Material NewBitMat(string name) => new Material(bitShader) { name = name, enableInstancing = true, hideFlags = HideFlags.DontSave };

        // One blade: a tapered strip, centred on its middle (it tumbles about its centre), curled and a little twisted so
        // it catches the light differently as it turns. Local y = along (uv.y 0 = cut end), x = across, z = face normal.
        static Mesh BuildBlade()
        {
            const int Seg = 6;
            var v = new Vector3[(Seg + 1) * 2]; var uv = new Vector2[v.Length]; var t = new int[Seg * 6];
            for (int j = 0; j <= Seg; j++)
            {
                float f = j / (float)Seg;
                float w = Mathf.Lerp(0.5f, 0.03f, Mathf.Pow(f, 1.6f));
                float tw = f * 0.55f, z = 0.16f * f * f;
                float c = Mathf.Cos(tw), s = Mathf.Sin(tw);
                v[j * 2] = new Vector3(-w * c, f - 0.5f, z - w * s);
                v[j * 2 + 1] = new Vector3(w * c, f - 0.5f, z + w * s);
                uv[j * 2] = new Vector2(0, f); uv[j * 2 + 1] = new Vector2(1, f);
                if (j < Seg) { int k = j * 6, i = j * 2; t[k] = i; t[k + 1] = i + 2; t[k + 2] = i + 1; t[k + 3] = i + 1; t[k + 4] = i + 2; t[k + 5] = i + 3; }
            }
            var m = new Mesh { name = "GS cut blade", hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = uv; m.triangles = t;
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- mirrors of Reed.shader
        static float Frac(float x) => x - Mathf.Floor(x);
        static float CutKeep(float x, float z) => 0.2f + 0.16f * Frac((x * 0.7131f + z * 0.3217f) * 7.13f);
        const float Uncut = 1.3f;   // Reed.shader: lerp(1.3, keep, c)

        // the value RR_VegCut stamps at p (its ragged-edge noise averaged)
        static float StampValue(Vector3 p, Vector3 centre, Vector3 aim, float radius, float cosHalf, float hold)
        {
            var d = new Vector2(p.x - centre.x, p.z - centre.z);
            float dist = d.magnitude;
            float inR = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.88f, radius, dist));
            float c = dist > 0.001f ? Vector2.Dot(d / dist, new Vector2(aim.x, aim.z)) : 1f;
            float inA = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cosHalf - 0.08f, cosHalf + 0.04f, c));
            return inR * inA * hold * 0.9f;
        }

        static long Key(Vector3 p) => ((long)Mathf.RoundToInt(p.x * 20f) << 32) ^ (uint)Mathf.RoundToInt(p.z * 20f);

        // ---------------------------------------------------------------- spawning
        readonly List<int> grassIdx = new List<int>();

        /// Throw what a swing cut. centre = player, aim = swing direction, sweepSign = +1/-1 (which way round the blade
        /// travels), radius / halfAngle = the swing's sector (same as VegetationCut.Sector).
        public void Spawn(List<VegetationQuery.Hit> hits, Vector3 centre, Vector3 aim, float sweepSign, float radius, float halfAngle, bool finisher)
        {
            aim.y = 0; if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward; aim.Normalize();
            var vc = VegetationCut.Instance;
            float hold = vc ? vc.hold : 2.2f, regrow = vc ? vc.regrowPerSec : 0.32f;
            float cosHalf = Mathf.Cos(Mathf.Min(halfAngle, 180f) * Mathf.Deg2Rad);
            float now = Time.time;
            float power = finisher ? 1.2f : 1f;
            grassIdx.Clear();
            foliageBlades = 0; int reeds = 0;
            foreach (var h in hits)
            {
                if (h.kind == VegetationQuery.Kind.Grass && IsFoliagePlant(h)) foliageBlades++;
                else if (h.kind == VegetationQuery.Kind.Reed) reeds++;
            }
            reedPiecesThisSwing = reeds > 0 ? Mathf.Clamp(reedPiecesPerSwing / (float)reeds, 3f, 14f) : 0f;
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                if (h.kind == VegetationQuery.Kind.Reed) SpawnReed(h, centre, aim, sweepSign, radius, halfAngle, cosHalf, hold, regrow, now, power);
                else if (h.kind == VegetationQuery.Kind.Grass && IsFoliagePlant(h)) SpawnFoliage(h, centre, aim, sweepSign, radius, halfAngle, cosHalf, hold, regrow, now, power);
                else if (h.kind == VegetationQuery.Kind.Grass) grassIdx.Add(i);
            }
            if (grassIdx.Count > 0 && bitMats.Count > 0)
            {
                float p = Mathf.Min(1f, grassPerSwing / (float)grassIdx.Count);
                foreach (int i in grassIdx) if (Random.value < p) SpawnGrass(hits[i], centre, aim, sweepSign, halfAngle, power);
            }
        }

        // When along the swing the blade reaches this plant (s): the sweep takes ~0.09 s across the sector.
        static float SweepDelay(Vector3 outward, Vector3 aim, float sweepSign, float halfAngle)
        {
            float ang = Vector3.SignedAngle(aim, outward, Vector3.up) * sweepSign;
            float half = Mathf.Max(halfAngle, 1f);
            return Mathf.Clamp01((ang + half) / (2f * half)) * (halfAngle >= 179f ? 0.16f : 0.09f);
        }

        void SpawnReed(VegetationQuery.Hit h, Vector3 centre, Vector3 aim, float sweepSign, float radius, float halfAngle,
                       float cosHalf, float hold, float regrow, float now, float power)
        {
            if (!h.mesh || !h.mat || h.matrix.m33 == 0f) return;
            var m0 = h.matrix;
            Vector3 root = m0.GetColumn(3);
            // the cut map before and after this swing, as the shader sees it at the root
            long key = Key(root);
            float vPrev = 0f;
            if (cutRecs.TryGetValue(key, out var rec)) vPrev = Mathf.Max(0f, rec.v - regrow * (now - rec.t));
            float vNew = Mathf.Max(vPrev, StampValue(root, centre, aim, radius, cosHalf, hold));
            cutRecs[key] = new CutRec { t = now, v = vNew };
            float keep = CutKeep(root.x, root.z);
            float hi = vPrev > 0.001f ? Mathf.Lerp(Uncut, keep, Mathf.Clamp01(vPrev)) : 3f;
            float lo = Mathf.Lerp(Uncut, keep, Mathf.Clamp01(vNew));
            if (lo > Mathf.Min(hi, 1f) - 0.05f) return;   // nothing (left) above the cut

            // 11-14 Kido: no flying clumps. The part above the cut shreds into many small leaf strips, stalk splinters and
            // plume fluff, thrown every which way (biased along the swing), falling like the grass clippings.
            if (bitMats.Count == 0) return;
            float meshH = Mathf.Max(h.mesh.bounds.max.y, 0.01f);
            float top = Mathf.Min(hi, 1f);
            var axisC = (Vector3)m0.GetColumn(1);
            float H = axisC.magnitude * meshH;
            float spread = Mathf.Clamp(Mathf.Max(h.mesh.bounds.extents.x, h.mesh.bounds.extents.z) * ((Vector3)m0.GetColumn(0)).magnitude, 0.08f, 0.6f);
            var outward = root - centre; outward.y = 0;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : aim;
            var tangent = Vector3.Cross(Vector3.up, outward) * sweepSign;
            float delay = SweepDelay(outward, aim, sweepSign, halfAngle);
            float band = top - lo;
            int n = Mathf.Clamp(Mathf.RoundToInt(reedPiecesThisSwing * Mathf.Clamp01(band / 0.7f)), 3, 18);
            var swingKick = (tangent * 2.2f + outward * 1.2f) * power;
            for (int i = 0; i < n; i++)
            {
                float f = Random.Range(lo, top);
                var ring = Random.insideUnitCircle * spread;
                var p = m0.MultiplyPoint3x4(new Vector3(0f, f * meshH, 0f)) + new Vector3(ring.x, 0f, ring.y);
                // every direction: a random burst, plus the swing's push
                var burst = Random.onUnitSphere; burst.y = Mathf.Abs(burst.y) * 0.6f + 0.25f;
                var vel = burst * Random.Range(2.2f, 6.5f) * power + swingKick * Random.Range(0.3f, 1.2f) + Random.insideUnitSphere * 0.6f;
                bool stalk = Random.value < 0.22f;
                float len = stalk ? Random.Range(0.05f, 0.14f) * Mathf.Clamp(H, 0.6f, 1.8f)
                                  : Random.Range(0.08f, 0.24f) * Mathf.Clamp(H, 0.6f, 1.8f);
                float wid = stalk ? len * Random.Range(0.1f, 0.16f) : len * Random.Range(0.06f, 0.11f);
                var col = h.color * (stalk ? Random.Range(0.4f, 0.55f) : Random.Range(0.5f, 0.78f));
                SpawnBlade(p, vel, len, wid, new Vector4(root.x, root.z, Random.value, 0f), col, 0, root.y, delay + Random.Range(0f, 0.03f), stalk);
            }
            if (top > 0.85f)
            {
                var plumeCol = PlumeColour(h.mat);
                var plume = m0.MultiplyPoint3x4(new Vector3(0f, 0.86f * meshH, 0f));
                int nf = Random.Range(2, 5);
                for (int i = 0; i < nf; i++)
                    SpawnFluff(plume + Random.insideUnitSphere * 0.18f * H, Random.onUnitSphere * Random.Range(0.8f, 2.4f) + swingKick * 0.4f + Vector3.up * 0.8f,
                               plumeCol * Random.Range(0.85f, 1.2f), root.y, delay);
            }
        }

        // ---- meadow plants drawn with LookStudy/Foliage (instanced blades and clumps, flowers, the bushes): the plant's own
        // mesh flies, in its own material (LS_PIECE) and instance tint. The field squashes clumps/bushes to a stub and
        // removes flower heads whole (LSFoliage.shader), here the part that came off is thrown.
        static bool IsFoliagePlant(VegetationQuery.Hit h) =>
            h.mesh && h.mat && h.mat.shader && h.mat.shader.name == "LookStudy/Foliage" && h.matrix.m33 != 0f;

        int foliageBlades;   // thinned like the grass clippings

        void SpawnFoliage(VegetationQuery.Hit h, Vector3 centre, Vector3 aim, float sweepSign, float radius, float halfAngle,
                          float cosHalf, float hold, float regrow, float now, float power)
        {
            var m0 = h.matrix;
            Vector3 root = m0.GetColumn(3);
            var up = ((Vector3)m0.GetColumn(1));
            float scale = up.magnitude; up /= Mathf.Max(scale, 1e-5f);
            bool flower = up.y < 0.8f;                                    // LSFoliage.shader LSFlowerOf
            float height = h.mesh.bounds.size.y * scale;
            bool bush = !flower && h.mat.name.Contains("Bush");
            bool clump = !flower && !bush && (h.mesh.name.Contains("lump") || h.mesh.name.Contains("kump"));   // the big grass tufts (덤불)
            // the same thresholds the shader uses: flowers and bushes vanish above 0.35, clumps read as cut from half way
            float th = flower || bush ? 0.35f : 0.5f;
            long key = Key(root);
            float vPrev = 0f;
            if (cutRecs.TryGetValue(key, out var rec)) vPrev = Mathf.Max(0f, rec.v - regrow * (now - rec.t));
            float vNew = Mathf.Max(vPrev, StampValue(root, centre, aim, radius, cosHalf, hold));
            cutRecs[key] = new CutRec { t = now, v = vNew };
            if (vPrev >= th || vNew < th) return;
            if (!flower && !bush && !clump && Random.value > Mathf.Clamp01(grassPerSwing / Mathf.Max(1f, foliageBlades))) return;
            var mat = FoliagePieceMat(h.mat);
            if (!mat) return;

            int slot = topCount < tops.Length ? topCount++ : Oldest();
            var com0 = m0.MultiplyPoint3x4(h.mesh.bounds.center);
            var outward = root - centre; outward.y = 0;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : aim;
            var tangent = Vector3.Cross(Vector3.up, outward) * sweepSign;
            Vector3 vel, spin; float drag, rest, ground, lift;
            if (bush)
            {
                // heavy: knocked out along the swing, a slow roll
                vel = (tangent * Random.Range(1.5f, 3.5f) + outward * Random.Range(2.5f, 5f)) * power + Vector3.up * Random.Range(3f, 5f);
                spin = (Vector3.Cross(Vector3.up, vel.normalized) + Random.insideUnitSphere * 0.4f).normalized * Random.Range(2.5f, 6f);
                drag = 0.5f; rest = Random.Range(2.6f, 3.6f); ground = root.y - 0.12f; lift = height * 0.3f;
            }
            else
            {
                vel = (tangent * Random.Range(1.5f, 4.5f) + outward * Random.Range(0.8f, 3f)) * power + Vector3.up * Random.Range(flower ? 2.5f : 1.8f, flower ? 5f : 4.2f)
                      + Random.insideUnitSphere * 0.5f;
                spin = Random.onUnitSphere * (clump ? Random.Range(4f, 10f) : Random.Range(6f, 16f));
                drag = flower ? 1.6f : clump ? 1.1f : 1.9f; rest = Random.Range(1.6f, 3f); ground = root.y - (flower ? 0.42f : 0f); lift = 0.04f;
            }
            tops[slot] = new Top
            {
                mesh = h.mesh, mat = mat, m0 = m0, com0 = com0, axis0 = up,
                pos = com0, vel = vel, spin = spin, rot = Quaternion.identity,
                cut = new Vector4(h.tint.r, h.tint.g, h.tint.b, 1f), root = Vector4.zero,
                delay = SweepDelay(outward, aim, sweepSign, halfAngle), age = 0f,
                restLeft = rest, ground = ground, halfLen = bush ? height * 0.3f : height * 0.5f,
                drag = drag, grav = 9.8f, restLift = lift, bounces = 0, resting = false, foliage = true, keepPose = bush
            };
            if (bitMats.Count == 0) return;
            // petals off a flower, leaves off a bush
            int n = flower ? Random.Range(2, 5) : bush ? Random.Range(8, 14) : 0;
            var col = flower ? new Color(Mathf.Min(1f, h.tint.r * 0.7f), Mathf.Min(1f, h.tint.g * 0.7f), Mathf.Min(1f, h.tint.b * 0.6f)) : h.color * 0.55f;
            for (int i = 0; i < n; i++)
            {
                float len = flower ? Random.Range(0.025f, 0.05f) : Random.Range(0.05f, 0.12f);
                SpawnBlade(com0 + Random.insideUnitSphere * (bush ? height * 0.35f : 0.06f), vel * Random.Range(0.3f, 0.7f) + Random.insideUnitSphere * 1.8f + Vector3.up,
                           len, len * (flower ? 0.6f : 0.45f), new Vector4(root.x, root.z, Random.value, 2f), col, 0, ground, tops[slot].delay, false);
            }
        }

        Material FoliagePieceMat(Material src)
        {
            if (pieceMats.TryGetValue(src, out var m) && m) return m;
            if (!src.shader.keywordSpace.FindKeyword("LS_PIECE").isValid) { pieceMats[src] = null; return null; }
            m = new Material(src) { name = src.name + " (cut piece)", hideFlags = HideFlags.DontSave, enableInstancing = true };
            m.DisableKeyword("LS_PROC");
            m.EnableKeyword("LS_PIECE");
            m.SetShaderPassEnabled("DepthNormals", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            pieceMats[src] = m;
            return m;
        }

        int Oldest()
        {
            int best = 0; float age = -1f;
            for (int i = 0; i < topCount; i++) if (tops[i].age > age) { age = tops[i].age; best = i; }
            return best;
        }

        Material PieceMat(Material src)
        {
            if (pieceMats.TryGetValue(src, out var m) && m) return m;
            if (!src.shader || !src.shader.keywordSpace.FindKeyword("RF_PIECE").isValid) { pieceMats[src] = null; return null; }
            m = new Material(src) { name = src.name + " (cut piece)", hideFlags = HideFlags.DontSave, enableInstancing = true };
            m.DisableKeyword("RF_PROC");
            m.EnableKeyword("RF_PIECE");
            m.SetShaderPassEnabled("DepthNormals", false);
            pieceMats[src] = m;
            return m;
        }

        static Color PlumeColour(Material m)
        {
            Color Get(string p, Color f) => m.HasProperty(p) ? m.GetColor(p) : f;
            float isReed = m.HasProperty("_IsReed") ? m.GetFloat("_IsReed") : 0f;
            var c = Color.Lerp(Get("_PlumeCol", new Color(0.62f, 0.58f, 0.5f)), Get("_ReedPlumeCol", new Color(0.36f, 0.28f, 0.24f)), isReed);
            c = Color.Lerp(c, Get("_PlumeTip", new Color(0.86f, 0.82f, 0.74f)), 0.4f);
            return new Color(Mathf.Min(1f, c.r * 1.3f), Mathf.Min(1f, c.g * 1.3f), Mathf.Min(1f, c.b * 1.3f));
        }

        int BitMatFor(Material src, out float mode)
        {
            mode = 2f;
            if (!src || !src.shader) return 0;
            if (src.shader.name != "LookStudy/Grass") return 0;
            mode = 1f;
            if (bitMatOf.TryGetValue(src, out int idx)) return idx;
            var m = NewBitMat("GS Cut Bits (" + src.name + ")");
            foreach (var p in FieldProps)
            {
                if (!src.HasProperty(p)) continue;
                if (p.StartsWith("_Col")) m.SetColor(p, src.GetColor(p)); else m.SetFloat(p, src.GetFloat(p));
            }
            bitMats.Add(m);
            bitMatOf[src] = idx = bitMats.Count - 1;
            return idx;
        }

        void SpawnGrass(VegetationQuery.Hit h, Vector3 centre, Vector3 aim, float sweepSign, float halfAngle, float power)
        {
            int mat = BitMatFor(h.mat, out float mode);
            float len;
            if (h.field && h.vertex >= 0)
            {
                // the blade's real length, from its 5 vertices in the field mesh
                len = 0f;
                vTmp.Clear(); h.field.GetVertices(vTmp);
                if (h.vertex + 5 <= vTmp.Count)
                    for (int i = 0; i < 5; i++) len = Mathf.Max(len, (vTmp[h.vertex + i] - h.pos).magnitude);
                if (len < 0.05f) len = 0.35f;
            }
            else if (h.mesh) len = Mathf.Clamp(h.mesh.bounds.size.y * h.scale, 0.15f, 0.8f);
            else return;

            var outward = h.pos - centre; outward.y = 0;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : aim;
            var tangent = Vector3.Cross(Vector3.up, outward) * sweepSign;
            float delay = SweepDelay(outward, aim, sweepSign, halfAngle) + Random.Range(0f, 0.02f);
            float rnd = h.field ? h.rnd : Random.value;
            var a = new Vector4(h.pos.x, h.pos.z, rnd, mode);
            float piece = len * 0.85f * Random.Range(0.55f, 1f);
            var p = h.pos + Vector3.up * (len * 0.15f + piece * 0.5f);
            var vel = tangent * Random.Range(1.5f, 4.5f) + outward * Random.Range(0.6f, 3f) + Vector3.up * Random.Range(1.6f, 4.2f);
            vel = vel * power + Random.insideUnitSphere * 0.5f;
            SpawnBlade(p, vel, piece, Mathf.Clamp(len * 0.075f, 0.016f, 0.04f) * Random.Range(0.8f, 1.25f), a, h.color, mat, h.pos.y, delay, false);
            if (Random.value < 0.35f)
            {
                // chaff: short snippets, thrown a bit harder and wider
                float cl = Random.Range(0.03f, 0.08f);
                var cv = tangent * Random.Range(2f, 5.5f) + outward * Random.Range(1f, 3.5f) + Vector3.up * Random.Range(2f, 5f) + Random.insideUnitSphere * 1.2f;
                SpawnBlade(p, cv * power, cl, cl * Random.Range(0.25f, 0.4f), a, h.color, mat, h.pos.y, delay, true);
            }
        }
        readonly List<Vector3> vTmp = new List<Vector3>();

        int recycle;
        ref Bit NewBit()
        {
            int slot;
            if (bitCount < bits.Length) slot = bitCount++;
            else slot = recycle = (recycle + 1) % bitCount;   // full: overwrite round-robin (no O(n) oldest scan per bit)
            bits[slot] = default;
            return ref bits[slot];
        }

        void SpawnBlade(Vector3 pos, Vector3 vel, float len, float width, Vector4 a, Color col, int mat, float ground, float delay, bool chaff)
        {
            ref var b = ref NewBit();
            b.pos = pos; b.vel = vel; b.mat = mat; b.a = a; b.b = new Vector4(col.r, col.g, col.b, 0f);
            b.scale = new Vector3(width, len, len);
            b.rot = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
            b.spin = Random.onUnitSphere * Random.Range(8f, 22f) * (chaff ? 1.5f : 1f);
            b.delay = delay; b.age = 0f; b.maxAge = 9f;
            b.ground = ground + Random.Range(0.01f, 0.035f);
            // light blades: slow fall, roll about the long axis, rock and sway
            b.vt = chaff ? Random.Range(1.4f, 2.4f) : Random.Range(0.7f, 1.35f);
            b.dragH = chaff ? Random.Range(2f, 3f) : Random.Range(1.3f, 2.2f);
            b.freq = Random.Range(1.1f, 2.3f); b.phase = Random.Range(0f, 6.28f);
            b.sway = chaff ? 0.04f : Random.Range(0.08f, 0.22f);
            b.roll = Random.Range(4f, 11f) * (Random.value < 0.5f ? -1f : 1f);
            var s = Random.insideUnitCircle.normalized; b.side = new Vector3(s.x, 0f, s.y);
            b.grav = 9.8f;
            b.restLeft = chaff ? Random.Range(0.8f, 1.8f) : Random.Range(1.5f, 3f);
            b.fadeTime = 0.5f;
        }

        void SpawnFluff(Vector3 pos, Vector3 vel, Color col, float ground, float delay)
        {
            ref var b = ref NewBit();
            float s = Random.Range(0.025f, 0.05f);
            b.pos = pos; b.vel = vel; b.mat = 0; b.a = new Vector4(pos.x, pos.z, Random.value, 0f); b.b = new Vector4(col.r, col.g, col.b, 0f);
            b.scale = new Vector3(s * 0.6f, s, s);
            b.rot = Random.rotation; b.spin = Random.onUnitSphere * Random.Range(2f, 6f);
            b.delay = delay; b.age = 0f; b.maxAge = Random.Range(2.5f, 4.5f);
            b.ground = ground + 0.02f; b.fluff = true;
            b.dragH = Random.Range(1.8f, 2.8f); b.vt = Random.Range(0.25f, 0.45f); b.grav = 2.5f;
            b.freq = Random.Range(0.6f, 1.3f); b.phase = Random.Range(0f, 6.28f); b.sway = Random.Range(0.1f, 0.3f);
            var c = Random.insideUnitCircle.normalized; b.side = new Vector3(c.x, 0f, c.y);
            b.restLeft = 0.4f; b.fadeTime = 0.8f;
        }

        // ---------------------------------------------------------------- simulation
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            for (int i = 0; i < topCount;)
            {
                if (StepTop(ref tops[i], dt)) i++;
                else tops[i] = tops[--topCount];
            }
            for (int i = 0; i < bitCount;)
            {
                if (StepBit(ref bits[i], dt)) i++;
                else bits[i] = bits[--bitCount];
            }
            if (Time.time > nextPrune)
            {
                nextPrune = Time.time + 2f;
                var vc = VegetationCut.Instance;
                float regrow = vc ? vc.regrowPerSec : 0.32f;
                pruneTmp.Clear();
                foreach (var kv in cutRecs) if (kv.Value.v - regrow * (Time.time - kv.Value.t) <= 0f) pruneTmp.Add(kv.Key);
                foreach (var k in pruneTmp) cutRecs.Remove(k);
            }
        }

        static Quaternion Turn(Vector3 w, float dt, Quaternion q)
        {
            float a = w.magnitude;
            return a > 1e-4f ? Quaternion.AngleAxis(a * dt * Mathf.Rad2Deg, w / a) * q : q;
        }

        bool StepTop(ref Top t, float dt)
        {
            t.age += dt;
            if (t.delay > 0f) { t.delay -= dt; return true; }   // the blade has not reached it yet: it stands where it was
            if (!t.resting)
            {
                t.vel.y -= t.grav * dt;
                t.vel /= 1f + t.drag * dt;
                t.spin /= 1f + 0.25f * dt;
                t.pos += t.vel * dt;
                t.rot = Turn(t.spin, dt, t.rot);
                var axis = t.rot * t.axis0;
                float low = t.pos.y - Mathf.Abs(axis.y) * t.halfLen;
                if (low <= t.ground + 0.03f && t.vel.y < 0f)
                {
                    t.pos.y += t.ground + 0.03f - low;
                    if (t.vel.y < -2.2f && t.bounces < 2)
                    {
                        // the end that hits the ground kicks it: a low bounce, spin knocked about, skids on
                        t.bounces++;
                        t.vel = new Vector3(t.vel.x * 0.6f, -t.vel.y * Random.Range(0.18f, 0.32f), t.vel.z * 0.6f);
                        t.spin = t.spin * Random.Range(-0.5f, 0.4f) + Random.insideUnitSphere * 3f;
                    }
                    else { t.resting = true; t.vel.y = 0f; }
                }
            }
            else
            {
                // slide to a stop and fall flat
                float k = Mathf.Exp(-5f * dt);
                t.vel.x *= k; t.vel.z *= k;
                t.pos += t.vel * dt;
                var axis = t.rot * t.axis0;
                var flat = new Vector3(axis.x, 0f, axis.z);
                if (flat.sqrMagnitude < 1e-4f) flat = Vector3.Cross(Vector3.up, t.spin.sqrMagnitude > 1e-4f ? t.spin : Vector3.right);
                var target = t.keepPose ? t.rot : Quaternion.FromToRotation(axis, flat.normalized) * t.rot;
                t.rot = Quaternion.Slerp(t.rot, target, 1f - Mathf.Exp(-6f * dt));
                t.pos.y = Mathf.Lerp(t.pos.y, t.ground + t.restLift, 1f - Mathf.Exp(-6f * dt));
                t.restLeft -= dt;
                float fade = Mathf.Clamp01(1f - t.restLeft / 0.7f);
                if (t.foliage) t.cut.w = 1f - fade; else t.cut.z = fade;
                if (t.restLeft <= 0f) return false;
            }
            return t.age < 12f;
        }

        bool StepBit(ref Bit b, float dt)
        {
            b.age += dt;
            if (b.delay > 0f) { b.delay -= dt; return true; }
            if (!b.resting)
            {
                b.vel.y -= b.grav * dt;
                float kh = Mathf.Exp(-b.dragH * dt);
                b.vel.x *= kh; b.vel.z *= kh;
                bool falling = b.vel.y < 0f;
                if (b.vel.y < -b.vt) b.vel.y += (-b.vt - b.vel.y) * (1f - Mathf.Exp(-7f * dt));
                b.pos += b.vel * dt;
                if (falling)
                {
                    // leaf fall: sway side to side, rock with the sway, roll about the long axis, settle toward level
                    b.phase += b.freq * 6.2832f * dt;
                    b.pos += b.side * (b.sway * Mathf.Cos(b.phase) * b.freq * 6.2832f * dt);
                    if (!b.fluff)
                    {
                        var la = b.rot * Vector3.up;
                        b.spin = Vector3.Lerp(b.spin, la * b.roll + b.side * (Mathf.Sin(b.phase) * 2.5f), 1f - Mathf.Exp(-3f * dt));
                        var flat = new Vector3(la.x, 0f, la.z);
                        if (flat.sqrMagnitude > 1e-4f)
                            b.rot = Quaternion.Slerp(b.rot, Quaternion.FromToRotation(la, Vector3.Lerp(flat.normalized, la, 0.2f)) * b.rot, 1f - Mathf.Exp(-2.2f * dt));
                    }
                }
                else b.spin /= 1f + 0.8f * dt;
                b.rot = Turn(b.spin, dt, b.rot);
                if (b.pos.y <= b.ground)
                {
                    b.pos.y = b.ground;
                    if (!b.fluff && b.vel.y < -2.5f) { b.vel = new Vector3(b.vel.x * 0.4f, -b.vel.y * 0.15f, b.vel.z * 0.4f); b.spin *= 0.4f; }
                    else
                    {
                        b.resting = true;
                        // lie flat: face up (either side), long axis along the ground
                        var la = b.rot * Vector3.up;
                        var flat = new Vector3(la.x, 0f, la.z);
                        if (flat.sqrMagnitude < 1e-4f) flat = b.side;
                        b.spin = Vector3.zero;
                        b.vel = Vector3.zero;
                        b.side = flat.normalized;   // reused as the resting long axis
                        b.roll = Random.value < 0.5f ? 1f : -1f;
                    }
                }
            }
            else
            {
                var target = Quaternion.LookRotation(Vector3.up * b.roll, b.side);
                b.rot = Quaternion.Slerp(b.rot, target, 1f - Mathf.Exp(-14f * dt));
                b.restLeft -= dt;
            }
            float left = b.resting ? b.restLeft : b.maxAge - b.age;
            b.b.w = Mathf.Clamp01(1f - left / b.fadeTime);
            return left > 0f;
        }

        // ---------------------------------------------------------------- drawing
        sealed class Batch
        {
            public Mesh mesh; public Material mat;
            public readonly Matrix4x4[] m = new Matrix4x4[1023];
            public readonly Vector4[] a = new Vector4[1023], b = new Vector4[1023];
            public int n, idA, idB;
            public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        }
        readonly Dictionary<(Mesh, Material), Batch> topBatches = new Dictionary<(Mesh, Material), Batch>();
        readonly List<Batch> bitBatches = new List<Batch>();
        static readonly Bounds Huge = new Bounds(Vector3.zero, Vector3.one * 100000f);

        void LateUpdate()
        {
            foreach (var bt in topBatches.Values) bt.n = 0;
            for (int i = 0; i < topCount; i++)
            {
                ref var t = ref tops[i];
                if (!t.mat) continue;
                if (!topBatches.TryGetValue((t.mesh, t.mat), out var bt)) topBatches[(t.mesh, t.mat)] = bt = new Batch { mesh = t.mesh, mat = t.mat, idA = t.foliage ? IdTint : IdPieceCut, idB = t.foliage ? -1 : IdPieceRoot };
                if (bt.n == 1023) Flush(bt, bt.idA, bt.idB, true);
                bt.m[bt.n] = Matrix4x4.TRS(t.pos, t.rot, Vector3.one) * Matrix4x4.Translate(-t.com0) * t.m0;
                bt.a[bt.n] = t.cut; bt.b[bt.n] = t.root;
                bt.n++;
            }
            foreach (var bt in topBatches.Values) Flush(bt, bt.idA, bt.idB, true);

            while (bitBatches.Count < bitMats.Count) bitBatches.Add(new Batch { mesh = blade, mat = bitMats[bitBatches.Count] });
            foreach (var bt in bitBatches) bt.n = 0;
            for (int i = 0; i < bitCount; i++)
            {
                ref var b = ref bits[i];
                var bt = bitBatches[b.mat];
                if (bt.n == 1023) Flush(bt, IdBitA, IdBitB, false);
                bt.m[bt.n] = Matrix4x4.TRS(b.pos, b.rot, b.scale);
                bt.a[bt.n] = b.a; bt.b[bt.n] = b.b;
                bt.n++;
            }
            foreach (var bt in bitBatches) Flush(bt, IdBitA, IdBitB, false);
        }

        static void Flush(Batch bt, int idA, int idB, bool allSubmeshes)
        {
            if (bt.n == 0 || !bt.mat || !bt.mesh) { bt.n = 0; return; }
            bt.mpb.SetVectorArray(idA, bt.a);
            if (idB >= 0) bt.mpb.SetVectorArray(idB, bt.b);
            var rp = new RenderParams(bt.mat)
            {
                matProps = bt.mpb, worldBounds = Huge, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                layer = 0
            };
            int subs = allSubmeshes ? bt.mesh.subMeshCount : 1;
            for (int sm = 0; sm < subs; sm++) Graphics.RenderMeshInstanced(rp, bt.mesh, sm, bt.m, bt.n);
            bt.n = 0;
        }
    }
}
