// Generates the study sets from code: meadow with a lake, salt mirror flat, layered canyon.
// Nothing is saved into the scene; it rebuilds on enable from a fixed seed.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyWorld : MonoBehaviour
{
    public Material grass, saltGrass, ground, water, props, mirror, canyon;
    public bool onlyVivid, onlyReal;
    public Mesh[] realClumps, realBlades; public Mesh mYellow, mWhite, mRose, mIvy, mLeaves, mVines, mBush;
    public Material fGrass, fYellow, fWhite, fRose, fIvy, fLeaves, fVines, fBush;
    public const float VividX = -400f;
    public const float SaltX = 400f, CanyonX = 800f;
    public static readonly Vector2 Lake = new Vector2(20, -8);
    public const float LakeR = 15f;
    static readonly Vector2[] Path =
    {
        new Vector2(-46,-22), new Vector2(-20,-12), new Vector2(-7,3), new Vector2(7,15), new Vector2(34,22)
    };
    Transform root;

    void OnEnable()
    {
        // Shading reads these globals, so the scene looks right without any capture step.
        if (onlyReal)
        {
            ApplySky(Sky.Vivid, RenderSettings.sun, 0.0006f, Vector3.zero, 0f, 0.12f);
            Shader.SetGlobalFloat("_LSTime", 12f);
        }
        Build();
    }
    void OnDisable() { if (!Application.isPlaying) Clear(); else ReleaseMeshes(); }
    // In play mode the scene unload destroys the generated objects, but meshes made with new Mesh() are
    // assets that outlive them, so free them here. (Objects cannot be destroyed while being deactivated.)
    void ReleaseMeshes()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) Destroy(mf.sharedMesh);
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (mc.sharedMesh) Destroy(mc.sharedMesh);
    }

    void Update() { if (Application.isPlaying) Shader.SetGlobalFloat("_LSTime", Time.time); }

    public void Clear()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>()) DestroyImmediate(mf.sharedMesh);
        DestroyImmediate(root.gameObject);
    }

    public void Build()
    {
        if (!grass || !ground) return;
        Clear();
        root = new GameObject("Generated").transform;
        root.SetParent(transform, false);
        root.gameObject.hideFlags = HideFlags.DontSave;
        if (onlyReal) { BuildReal(); return; }
        if (onlyVivid) { BuildVivid(); return; }
        BuildMeadow();
        BuildSalt();
        BuildCanyon();
    }

    // ---------- height and masks

    static float SS(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }
    static float N(float x, float y) => Mathf.PerlinNoise(x + 137.1f, y + 71.3f);

    public static float MeadowH(float x, float z)
    {
        float h = Mathf.Max(0.9f, 1.5f + (N(x * 0.025f, z * 0.025f) - 0.5f) * 2.4f + (N(x * 0.09f, z * 0.09f) - 0.5f) * 0.5f);
        float d = Vector2.Distance(new Vector2(x, z), Lake) + (N(x * 0.07f + 4f, z * 0.07f) - 0.5f) * 8f;
        return Mathf.Lerp(h, -2.6f, 1f - SS(LakeR - 6f, LakeR + 4f, d));
    }

    static float PathDist(float x, float z)
    {
        var p = new Vector2(x + (N(x * 0.06f, z * 0.06f) - 0.5f) * 5f, z + (N(z * 0.06f + 9f, x * 0.06f) - 0.5f) * 5f);
        float best = 1e9f;
        for (int i = 0; i < Path.Length - 1; i++)
        {
            var a = Path[i]; var ab = Path[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }
    static float PathMask(float x, float z) => Mathf.Clamp01(1f - PathDist(x, z) / 2.2f);
    static float SandMask(float h) => 1f - SS(0.12f, 0.75f, h);

    public static float CanyonH(float wx, float z)
    {
        float x = wx - CanyonX;
        float n = N(x * 0.012f, z * 0.012f) * 0.62f + N(x * 0.031f, z * 0.031f) * 0.28f + N(x * 0.08f, z * 0.08f) * 0.10f;
        float cut = Mathf.Abs(x - Mathf.Sin(z * 0.022f) * 34f);
        float h = (n * 92f - 10f) * SS(9f, 44f, cut) + 2f;
        float f = Mathf.Max(h, 0f) / 8f;
        return (Mathf.Floor(f) + SS(0.08f, 0.5f, f - Mathf.Floor(f))) * 8f;
    }

    // ---------- matched field: long thin blades combed along a flow, with hard flat shadows

    public static float VividH(float x, float z) => 0.3f + (N(x * 0.03f + 40f, z * 0.03f) - 0.5f) * 1.1f;
    static float VClear(float x, float z)
    {
        float d = Vector2.Distance(new Vector2(x - VividX, z), new Vector2(-9.5f, -7.5f)) + (N(x * 0.25f, z * 0.25f) - 0.5f) * 3f;
        return 1f - SS(3.4f, 7.2f, d);
    }

    void BuildVivid()
    {
        AddMesh("VGround", GridMesh(VividX - 60, VividX + 60, -60, 60, 0.5f, VividH, (x, z, h) => new Color(0, 0, VClear(x, z), 0)), ground);
        var rnd = new System.Random(4242);
        const float Chunk = 8f;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var rt = new List<Vector4>(); var col = new List<Color32>(); var tri = new List<int>();
        var dark = new Color(0.07f, 0.36f, 0.17f).linear;
        var tick = new Color32((byte)(dark.r * 255), (byte)(dark.g * 255), (byte)(dark.b * 255), 255);
        void Blade(Vector3 p, Vector3 fwd, float L, float w, float r, Color32 c, float heightFrac = 0.55f, bool grad = false)
        {
            var side = new Vector3(-fwd.z, 0, fwd.x);
            var rtv = new Vector4(p.x, p.y, p.z, r);
            int b = v.Count;
            float H = L * heightFrac, F = L * Mathf.Sqrt(1f - heightFrac * heightFrac);
            var m = p + Vector3.up * H * 0.68f + fwd * F * 0.40f;
            var e = p + Vector3.up * H + fwd * F;
            v.Add(p - side * w); uv.Add(new Vector2(-1, 0));
            v.Add(p + side * w); uv.Add(new Vector2(1, 0));
            v.Add(m - side * w * 0.82f); uv.Add(new Vector2(-1, 0.55f));
            v.Add(m + side * w * 0.82f); uv.Add(new Vector2(1, 0.55f));
            v.Add(e); uv.Add(new Vector2(0, 1));
            rt.Add(rtv); rt.Add(rtv); rt.Add(rtv); rt.Add(rtv); rt.Add(rtv);
            var c0 = c; var c1 = c; if (grad) { c0.a = 0; c1.a = (byte)(c.a * 0.42f); }
            col.Add(c0); col.Add(c0); col.Add(c1); col.Add(c1); col.Add(c);
            tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
            tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
            tri.Add(b + 2); tri.Add(b + 4); tri.Add(b + 3);
        }
        void Flush(string n)
        {
            if (v.Count == 0) return;
            var mesh = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, rt); mesh.SetColors(col); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            var bd = mesh.bounds; bd.Expand(2.5f); mesh.bounds = bd;
            AddMesh(n, mesh, grass).shadowCastingMode = ShadowCastingMode.Off;
            v.Clear(); uv.Clear(); rt.Clear(); col.Clear(); tri.Clear();
        }
        var none = new Color32(0, 0, 0, 0);
        for (float cz = -48; cz < 48; cz += Chunk)
        for (float cx = VividX - 48; cx < VividX + 48; cx += Chunk)
        {
            for (int n = 0, tries = (int)(300 * Chunk * Chunk); n < tries; n++)
            {
                float x = cx + (float)rnd.NextDouble() * Chunk, z = cz + (float)rnd.NextDouble() * Chunk;
                float dist = new Vector2(x - VividX, z).magnitude;
                float keep = 1f - 0.72f * SS(19f, 34f, dist);
                if ((float)rnd.NextDouble() > keep) continue;
                float clear = VClear(x, z);
                float a = 3.84f + (N(x * 0.055f, z * 0.055f) - 0.5f) * 1.9f + R(rnd, -0.32f, 0.32f);
                var fwd = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = new Vector3(x, VividH(x, z), z);
                if ((float)rnd.NextDouble() < clear)
                {
                    if (rnd.NextDouble() > 0.012) continue;
                    Blade(p, fwd, R(rnd, 0.12f, 0.24f), 0.022f, (float)rnd.NextDouble(), tick);
                    continue;
                }
                float L = R(rnd, 0.6f, 1.05f) * Mathf.Lerp(0.82f, 1.15f, N(x * 0.13f, z * 0.13f)) * (1f - clear * 0.6f);
                Blade(p, fwd, L, R(rnd, 0.034f, 0.058f), (float)rnd.NextDouble(), none);
            }
            Flush("VBlades");
        }
        // Taller radial tufts break up the combed field.
        var limeC = new Color(0.60f, 0.90f, 0.26f).linear;
        var lime = new Color32((byte)(limeC.r * 255), (byte)(limeC.g * 255), (byte)(limeC.b * 255), 215);
        for (int i = 0; i < 46; i++)
        {
            bool near = i < 11;
            float x = VividX + (near ? R(rnd, -15, -2) : R(rnd, -40, 40)), z = near ? R(rnd, 0, 11) : R(rnd, -40, 40);
            if (VClear(x, z) > 0.2f) continue;
            for (int b = 0, cnt = rnd.Next(34, 60); b < cnt; b++)
            {
                float a = R(rnd, 0, 6.283f);
                var fwd = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var p = new Vector3(x + fwd.x * R(rnd, 0, 0.35f), VividH(x, z), z + fwd.z * R(rnd, 0, 0.35f));
                Blade(p, fwd, R(rnd, 0.95f, 1.5f), R(rnd, 0.04f, 0.065f), (float)rnd.NextDouble(), lime, 0.68f, true);
            }
        }
        Flush("VTufts");

        var mb = new MeshBuilder();
        foreach (var t in new[] { new Vector3(-13.5f, -3.5f, 1.25f), new Vector3(-7.5f, -1.5f, 1.1f), new Vector3(-5.5f, -10.5f, 1.3f), new Vector3(-10f, 2f, 0.85f), new Vector3(18f, 10f, 1.4f), new Vector3(-24f, 14f, 1.5f), new Vector3(24f, -14f, 1.2f), new Vector3(8f, 17f, 1.1f) })
            mb.VividBush(new Vector3(VividX + t.x, VividH(VividX + t.x, t.y), t.y), t.z, rnd);
        for (int i = 0; i < 10; i++)
        {
            float x = VividX + R(rnd, -38, 38), z = R(rnd, -30, 30);
            if (new Vector2(x - VividX, z).magnitude < 13f) continue;
            mb.VividBush(new Vector3(x, VividH(x, z), z), R(rnd, 0.8f, 1.3f), rnd);
        }
        AddMesh("VProps", mb.ToMesh(), props);

        var fv = new List<Vector3>(); var fu = new List<Vector2>(); var fr = new List<Vector4>(); var fc = new List<Color>(); var ft = new List<int>();
        void Flower(float x, float z, float s, Color rim, Color centre)
        {
            if (VClear(x, z) > 0.6f) return;
            float h = VividH(x, z), y = h + R(rnd, 0.42f, 0.58f), rot = R(rnd, 0, 6.283f);
            float tx = R(rnd, -0.45f, 0.45f), tz = R(rnd, -0.45f, 0.45f);
            var root4 = new Vector4(x, h, z, (float)rnd.NextDouble());
            void Disc(float scale, float yo, Color inner, Color outer)
            {
                int b = fv.Count;
                fv.Add(new Vector3(x, y + yo, z)); fc.Add(inner); fu.Add(new Vector2(0, 0.85f)); fr.Add(root4);
                for (int p = 0; p < 20; p++)
                {
                    float ang = rot + p / 20f * 6.283f;
                    float pr = s * scale * (0.78f + 0.22f * Mathf.Abs(Mathf.Cos(ang * 2.5f)));
                    float ox = Mathf.Cos(ang) * pr, oz = Mathf.Sin(ang) * pr;
                    fv.Add(new Vector3(x + ox, y + yo + ox * tx + oz * tz, z + oz)); fc.Add(outer); fu.Add(new Vector2(0, 0.85f)); fr.Add(root4);
                    ft.Add(b); ft.Add(b + 1 + (p + 1) % 20); ft.Add(b + 1 + p);
                }
            }
            var shade = new Color(0.05f, 0.27f, 0.14f, 1);
            Disc(1.1f, -0.03f, shade, shade);
            Disc(1f, 0f, rim, rim);
            Disc(0.3f, 0.012f, centre, centre);
        }
        Color Y1 = new Color(1f, 0.86f, 0.13f, 1), YC = new Color(0.97f, 0.66f, 0.08f, 1), W1 = new Color(0.97f, 0.97f, 0.93f, 1), WC = new Color(0.96f, 0.80f, 0.20f, 1), B1 = new Color(0.20f, 0.42f, 0.90f, 1), BC = new Color(0.72f, 0.82f, 0.98f, 1);
        float G() => (R(rnd, -1, 1) + R(rnd, -1, 1) + R(rnd, -1, 1)) / 1.5f;
        for (int i = 0; i < 230; i++) Flower(VividX + 3.5f + G() * 6.2f, -1.5f + G() * 3.7f, R(rnd, 0.11f, 0.17f), Y1, YC);
        for (int i = 0; i < 26; i++) Flower(VividX - 8f + G() * 3f, 7f + G() * 3f, R(rnd, 0.1f, 0.15f), Y1, YC);
        for (int i = 0; i < 110; i++) Flower(VividX + R(rnd, -36, 36), R(rnd, -28, 28), R(rnd, 0.1f, 0.16f), W1, WC);
        for (int i = 0; i < 80; i++) Flower(VividX - 7f + G() * 4f, 8f + G() * 2.6f, R(rnd, 0.06f, 0.09f), B1, BC);
        for (int i = 0; i < fc.Count; i++) { var l = fc[i].linear; l.a = fc[i].a; fc[i] = l; }
        var fm = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        fm.SetVertices(fv); fm.SetUVs(0, fu); fm.SetUVs(1, fr); fm.SetColors(fc); fm.SetTriangles(ft, 0);
        fm.RecalculateBounds();
        AddMesh("VFlowers", fm, grass).shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------- field made of authored plant meshes: standing clumps, many species

    class Batch
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Vector4> root = new List<Vector4>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<int> tri = new List<int>();
    }

    class Source
    {
        public Vector3[] v, n; public Vector2[] uv; public int[] t;
        public Source(Mesh m) { v = m.vertices; n = m.normals; uv = m.uv; t = m.triangles; }
    }

    static void Stamp(Batch b, Source s, Vector3 pos, Quaternion rot, float scale, Color tint, float rnd)
    {
        int bv = b.v.Count;
        var r = new Vector4(pos.x, pos.y, pos.z, rnd);
        for (int i = 0; i < s.v.Length; i++)
        {
            b.v.Add(pos + rot * (s.v[i] * scale));
            b.n.Add(rot * s.n[i]);
            b.uv.Add(s.uv[i]);
            b.root.Add(r);
            b.col.Add(tint);
        }
        for (int i = 0; i < s.t.Length; i++) b.tri.Add(bv + s.t[i]);
    }

    void Flush(Batch b, string name, Material m, bool shadows)
    {
        if (b.v.Count == 0) return;
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv); mesh.SetUVs(1, b.root); mesh.SetColors(b.col); mesh.SetTriangles(b.tri, 0);
        mesh.RecalculateBounds();
        var bd = mesh.bounds; bd.Expand(2.5f); mesh.bounds = bd;
        var mr = AddMesh(name, mesh, m);
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        b.v.Clear(); b.n.Clear(); b.uv.Clear(); b.root.Clear(); b.col.Clear(); b.tri.Clear();
    }

    LookStudyInstancer inst;
    void I(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, float s, Color tint, float unused = 0f) => inst.Add(mesh, mat, pos, rot, s, tint);

    void BuildReal()
    {
        inst = root.gameObject.AddComponent<LookStudyInstancer>();
        AddMesh("RGround", GridMesh(VividX - 70, VividX + 70, -70, 70, 0.5f, VividH, (x, z, h) => Color.black), ground);
        var rnd = new System.Random(909);
        var clumps = System.Array.ConvertAll(realClumps, m => new Source(m));
        var blades = System.Array.ConvertAll(realBlades, m => new Source(m));
        var batch = new Batch();

        Color Tone(float x, float z)
        {
            // Big masses of tone plus individual clumps that stand out, so the field never reads as one colour.
            float big = N(x * 0.05f + 3f, z * 0.05f), mid = N(x * 0.21f, z * 0.21f + 8f);
            float t = Mathf.Clamp01((big - 0.5f) * 2.6f + 0.5f + (mid - 0.5f) * 1.2f);
            var c = Color.Lerp(new Color(0.46f, 0.86f, 0.86f), new Color(1.12f, 1.42f, 1.0f), t);
            // The upper-left of the hero view is a brighter, sunnier stretch of meadow.
            float sunny = SS(9f, 1f, Vector2.Distance(new Vector2((x - VividX + 12f) * 0.75f, z - 5f), Vector2.zero));
            c *= Color.Lerp(Color.white, new Color(1.5f, 1.4f, 1.0f), sunny);
            float pick = (float)rnd.NextDouble();
            if (pick < 0.13f) c *= new Color(1.5f, 1.25f, 0.62f);      // sunlit yellow-green
            else if (pick < 0.28f) c *= new Color(0.58f, 0.7f, 0.8f); // deep blue-green
            c *= R(rnd, 0.88f, 1.12f);
            c.a = 1;
            return c;
        }
        Quaternion Stand(float tilt)
        {
            return Quaternion.Euler(R(rnd, -tilt, tilt), 0, R(rnd, -tilt, tilt)) * Quaternion.Euler(0, R(rnd, 0, 360), 0);
        }

        const float Chunk = 8f;
        for (float cz = -56; cz < 56; cz += Chunk)
        for (float cx = VividX - 56; cx < VividX + 56; cx += Chunk)
        {
            bool view = true; // the whole field is playable now, so it is dense everywhere
            int nc = (int)((view ? 13f : 4f) * Chunk * Chunk), nb = (int)((view ? 30f : 7f) * Chunk * Chunk);
            for (int i = 0; i < nc; i++)
            {
                float x = cx + (float)rnd.NextDouble() * Chunk, z = cz + (float)rnd.NextDouble() * Chunk;
                float s = R(rnd, 0.82f, 1.4f) * Mathf.Lerp(0.8f, 1.25f, N(x * 0.15f, z * 0.15f)) * (view ? 1f : 1.25f);
                I(realClumps[rnd.Next(realClumps.Length)], fGrass, new Vector3(x, VividH(x, z) - 0.03f, z), Stand(10f), s, Tone(x, z), (float)rnd.NextDouble());
            }
            for (int i = 0; i < nb; i++)
            {
                float x = cx + (float)rnd.NextDouble() * Chunk, z = cz + (float)rnd.NextDouble() * Chunk;
                I(realBlades[rnd.Next(realBlades.Length)], fGrass, new Vector3(x, VividH(x, z) - 0.02f, z), Stand(14f), R(rnd, 0.8f, 1.45f), Tone(x, z), (float)rnd.NextDouble());
            }
        }

        for (int i = 0; i < 200; i++)
        {
            bool near = i < 26;
            float x = VividX + (near ? R(rnd, -17, -3) : R(rnd, -54, 54)), z = near ? R(rnd, 0, 12) : R(rnd, -54, 54);
            var c = new Color(1.5f, 1.38f, 0.72f, 1) * R(rnd, 0.88f, 1.14f); c.a = 1;
            for (int b = 0; b < 3; b++)
                I(realClumps[rnd.Next(realClumps.Length)], fGrass, new Vector3(x + R(rnd, -0.3f, 0.3f), VividH(x, z), z + R(rnd, -0.3f, 0.3f)), Stand(16f), R(rnd, 1.9f, 2.7f), c, (float)rnd.NextDouble());
        }
        float G() => (R(rnd, -1, 1) + R(rnd, -1, 1) + R(rnd, -1, 1)) / 1.5f;
        void Scatter(Mesh mesh, Material mat, int count, float cx, float cz, float spreadX, float spreadZ, float s0, float s1, float faceUp, Color tint, float jitter, bool shadows, string name)
        {
            if (!mesh) return;
            var src = new Source(mesh);
            for (int i = 0; i < count; i++)
            {
                float x = VividX + cx + G() * spreadX, z = cz + G() * spreadZ;
                var rot = Quaternion.Euler(faceUp + R(rnd, -16, 16), R(rnd, -28, 28), R(rnd, -12, 12));
                float lift = faceUp != 0f ? 0.42f : 0.06f;
                var c = tint * R(rnd, 1f - jitter, 1f + jitter); c.a = 1;
                I(mesh, mat, new Vector3(x, VividH(x, z) + lift, z), rot, R(rnd, s0, s1), c);
            }
        }
        var white = Color.white;
        // Flowers: a large yellow drift, scattered daisies, pockets of tinted and rare species.
        var yel = Color.white;
        // Flower drifts all over the field: big yellow patches, pockets of blue and pink, daisies everywhere.
        Scatter(mYellow, fYellow, 230, 4f, -1.5f, 7.5f, 4.4f, 0.36f, 0.52f, FlowerTilt, yel, 0.1f, false, "RYellow");
        for (int d = 0; d < 22; d++)
            Scatter(mYellow, fYellow, rnd.Next(70, 170), R(rnd, -50, 50), R(rnd, -50, 50), R(rnd, 3f, 7f), R(rnd, 2.5f, 5f), 0.34f, 0.5f, FlowerTilt, yel, 0.1f, false, "RYellow");
        for (int d = 0; d < 10; d++)
            Scatter(mWhite, fWhite, rnd.Next(60, 120), R(rnd, -50, 50), R(rnd, -50, 50), R(rnd, 3f, 5f), R(rnd, 2f, 4f), 0.2f, 0.3f, FlowerTilt, new Color(0.42f, 0.56f, 1.15f), 0.1f, false, "RBlue");
        for (int d = 0; d < 7; d++)
            Scatter(mWhite, fWhite, rnd.Next(40, 80), R(rnd, -50, 50), R(rnd, -50, 50), R(rnd, 2.5f, 4f), R(rnd, 2f, 3.5f), 0.24f, 0.34f, FlowerTilt, new Color(1.05f, 0.62f, 0.86f), 0.1f, false, "RPink");
        Scatter(mWhite, fWhite, 900, 0f, 0f, 56f, 56f, 0.34f, 0.5f, FlowerTilt, white, 0.06f, false, "RWhite");
        Scatter(mLeaves, fLeaves, 600, 0f, 0f, 56f, 56f, 0.5f, 0.9f, 0f, new Color(1.25f, 1.3f, 1.0f), 0.12f, false, "RLeaves");
        Scatter(mIvy, fIvy, 260, 0f, 0f, 56f, 56f, 0.8f, 1.15f, 0f, new Color(0.62f, 0.72f, 0.62f), 0.1f, false, "RIvy");
        Scatter(mYellow, fYellow, 60, -9f, 8f, 4.5f, 3.5f, 0.32f, 0.46f, FlowerTilt, yel, 0.1f, false, "RYellow");
        Scatter(mWhite, fWhite, 110, 0f, 0f, 24f, 15f, 0.34f, 0.5f, FlowerTilt, white, 0.06f, false, "RWhite");
        Scatter(mWhite, fWhite, 90, -7f, 9f, 4.5f, 2.8f, 0.2f, 0.3f, FlowerTilt, new Color(0.42f, 0.56f, 1.15f), 0.1f, false, "RBlue");
        Scatter(mWhite, fWhite, 50, 12f, 9f, 3.5f, 2.8f, 0.24f, 0.34f, FlowerTilt, new Color(1.05f, 0.62f, 0.86f), 0.1f, false, "RPink");
        Scatter(mRose, fRose, 10, 15f, -5f, 7f, 5f, 0.34f, 0.46f, FlowerTilt, white, 0.05f, false, "RRose");
        Scatter(mLeaves, fLeaves, 90, 0f, 2f, 24f, 15f, 0.5f, 0.9f, 0f, new Color(1.25f, 1.3f, 1.0f), 0.12f, false, "RLeaves");
        Scatter(mIvy, fIvy, 50, -3f, -2f, 22f, 14f, 0.8f, 1.15f, 0f, new Color(0.62f, 0.72f, 0.62f), 0.1f, false, "RIvy");
        Scatter(mVines, fVines, 16, -4f, 5f, 20f, 12f, 0.45f, 0.7f, 0f, new Color(0.92f, 1.05f, 0.9f), 0.1f, false, "RVines");

        // Bushes, hand placed like the reference group plus a few further out.
        if (mBush)
        {
            var bs = new Source(mBush);
            foreach (var t in new[] { new Vector3(-13.5f, -3.5f, 1.6f), new Vector3(-7.5f, -1.5f, 1.4f), new Vector3(-5.5f, -10.5f, 1.7f), new Vector3(-10.5f, 2f, 1.15f),
                                      new Vector3(18f, 10f, 1.8f), new Vector3(-24f, 14f, 2f), new Vector3(24f, -14f, 1.6f), new Vector3(9f, 17f, 1.4f), new Vector3(-27f, -12f, 1.8f), new Vector3(30f, 6f, 1.6f) })
            {
                float x = VividX + t.x;
                var c = Color.white * R(rnd, 0.93f, 1.08f); c.a = 1;
                Stamp(batch, bs, new Vector3(x, VividH(x, t.y) + 0.12f, t.y), Quaternion.Euler(0, R(rnd, 0, 360), 0), t.z, c, (float)rnd.NextDouble());
            }
            Flush(batch, "RBush", fBush, true);
        }
    }
    public static float FlowerTilt = -60f;

    // ---------- sets

    void BuildMeadow()
    {
        var c = new Color32[0];
        AddMesh("Ground", GridMesh(-70, 70, -70, 70, 0.5f, MeadowH, (x, z, h) =>
            new Color(PathMask(x, z), SandMask(h), 0, 0)), ground);
        AddMesh("Lake", GridMesh(Lake.x - 26, Lake.x + 26, Lake.y - 26, Lake.y + 26, 0.3f, (x, z) => 0f, (x, z, h) =>
            new Color(Mathf.Clamp01(-MeadowH(x, z) / 2.6f), 0, 0, 0)), water);

        BuildGrass("Blades", -56, 56, -56, 56, grass, MeadowH, (x, z, h, r) =>
        {
            if (h < 0.42f + r * 0.3f) return 0f;
            if (PathMask(x, z) + (N(x * 1.1f, z * 1.1f) - 0.5f) * 0.45f > 0.33f) return 0f;
            return 1f - 0.65f * SS(20f, 42f, new Vector2(x + 3, z + 1).magnitude);
        }, 150);

        var mb = new MeshBuilder();
        var rnd = new System.Random(23);
        // Hand-placed around the hero view: large shadows and silhouettes frame the character.
        foreach (var t in new[] { new Vector2(-11, 6), new Vector2(5, 8), new Vector2(-16, -8) })
            mb.Tree(new Vector3(t.x, MeadowH(t.x, t.y), t.y), 1.25f, rnd);
        foreach (var t in new[] { new Vector2(-7, -5), new Vector2(1.5f, -4.5f), new Vector2(-1, 3.5f), new Vector2(-8.5f, 1.5f), new Vector2(3.5f, 1f) })
            mb.Bush(new Vector3(t.x, MeadowH(t.x, t.y), t.y), 1.1f, rnd);
        mb.Rock(new Vector3(2.5f, MeadowH(2.5f, -1f), -1f), 1.0f, rnd);
        mb.Rock(new Vector3(-6.5f, MeadowH(-6.5f, -7.5f), -7.5f), 0.8f, rnd);
        foreach (var t in new[] { new Vector2(-13, 13), new Vector2(13, 33), new Vector2(-26, -30), new Vector2(-34, 8),
                                  new Vector2(-4, -22), new Vector2(38, 30), new Vector2(-18, 32), new Vector2(-46, -8) })
            mb.Tree(new Vector3(t.x, MeadowH(t.x, t.y), t.y), 1f + (float)rnd.NextDouble() * 0.5f, rnd);
        for (int i = 0; i < 38; i++)
        {
            float x = R(rnd, -50, 50), z = R(rnd, -50, 50), h = MeadowH(x, z);
            if (h < 0.5f || PathMask(x, z) > 0.1f) continue;
            mb.Bush(new Vector3(x, h, z), R(rnd, 0.75f, 1.5f), rnd);
        }
        for (int i = 0; i < 30; i++)
        {
            float x = R(rnd, -50, 50), z = R(rnd, -50, 50), h = MeadowH(x, z);
            if (h < 0.05f) continue;
            mb.Rock(new Vector3(x, h, z), R(rnd, 0.35f, i < 6 ? 2.2f : 0.9f), rnd);
        }
        // Rocks around the shore give the water something to wrap against.
        for (int i = 0; i < 12; i++)
        {
            float a = R(rnd, 0, 6.28f);
            float x = Lake.x + Mathf.Cos(a) * R(rnd, LakeR - 2f, LakeR + 3f), z = Lake.y + Mathf.Sin(a) * R(rnd, LakeR - 2f, LakeR + 3f);
            mb.Rock(new Vector3(x, MeadowH(x, z), z), R(rnd, 0.4f, 1.3f), rnd);
        }
        AddMesh("Props", mb.ToMesh(), props);
        AddMesh("Flowers", Flowers(MeadowH, -3, -1, 30, 150, rnd), grass);
    }

    void BuildSalt()
    {
        var plane = GridMesh(SaltX - 200, SaltX + 200, -200, 200, 8f, (x, z) => 0f, (x, z, h) => Color.black);
        AddMesh("Salt", plane, mirror);
        BuildGrass("SaltBlades", SaltX - 44, SaltX + 44, -44, 44, saltGrass, (x, z) => 0f, (x, z, h, r) =>
        {
            float n = N(x * 0.055f + 2f, z * 0.055f + 8f);
            return SS(0.50f, 0.68f, n) * 0.55f;
        }, 90);
        var mb = new MeshBuilder();
        var rnd = new System.Random(5);
        for (int i = 0; i < 24; i++)
        {
            float x = SaltX + R(rnd, -70, 70), z = R(rnd, -70, 70);
            if (new Vector2(x - SaltX, z).magnitude < 7f) continue;
            mb.Rock(new Vector3(x, -0.05f, z), R(rnd, 0.3f, i < 4 ? 1.8f : 0.8f), rnd);
        }
        AddMesh("SaltProps", mb.ToMesh(), props);
    }

    void BuildCanyon()
    {
        AddMesh("Canyon", GridMesh(CanyonX - 150, CanyonX + 150, -150, 190, 0.8f, CanyonH, (x, z, h) => Color.black), canyon);
        AddMesh("River", GridMesh(CanyonX - 190, CanyonX + 190, -190, 190, 3f, (x, z) => 2.6f, (x, z, h) =>
            new Color(Mathf.Clamp01(1f - SS(0f, 6f, CanyonH(x, z) - 2f)) * 0.6f, 0, 0, 0)), water);
        var mb = new MeshBuilder();
        var rnd = new System.Random(77);
        for (int i = 0; i < 260; i++)
        {
            float x = CanyonX + R(rnd, -170, 170), z = R(rnd, -170, 170), h = CanyonH(x, z);
            float e = 1.5f;
            float slope = Mathf.Abs(CanyonH(x + e, z) - h) + Mathf.Abs(CanyonH(x, z + e) - h);
            if (slope > 0.5f || h < 4f) continue;
            mb.Tree(new Vector3(x, h, z), R(rnd, 1.0f, 1.8f), rnd, 420);
        }
        AddMesh("CanyonTrees", mb.ToMesh(), props);
    }

    static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    MeshRenderer AddMesh(string name, Mesh m, Material mat)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().sharedMesh = m;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return mr;
    }

    delegate float HeightFn(float x, float z);
    delegate Color ColorFn(float x, float z, float h);
    delegate float DensityFn(float x, float z, float h, float r);

    static Mesh GridMesh(float x0, float x1, float z0, float z1, float step, HeightFn hf, ColorFn cf)
    {
        int nx = Mathf.CeilToInt((x1 - x0) / step) + 1, nz = Mathf.CeilToInt((z1 - z0) / step) + 1;
        var v = new Vector3[nx * nz];
        var col = new Color[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = x0 + i * step, z = z0 + j * step, h = hf(x, z);
            v[j * nx + i] = new Vector3(x, h, z);
            col[j * nx + i] = cf(x, z, h);
        }
        var t = new int[(nx - 1) * (nz - 1) * 6];
        int k = 0;
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
            t[k++] = a; t[k++] = c; t[k++] = b;
            t[k++] = b; t[k++] = c; t[k++] = d;
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.vertices = v; m.colors = col; m.triangles = t;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void BuildGrass(string name, float x0, float x1, float z0, float z1, Material mat, HeightFn hf, DensityFn density, int perM2)
    {
        const float Chunk = 8f;
        var rnd = new System.Random(name.Length * 97 + 11);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var rootUv = new List<Vector4>(); var tri = new List<int>();
        for (float cz = z0; cz < z1; cz += Chunk)
        for (float cx = x0; cx < x1; cx += Chunk)
        {
            v.Clear(); uv.Clear(); rootUv.Clear(); tri.Clear();
            int tries = Mathf.RoundToInt(perM2 * Chunk * Chunk);
            for (int n = 0; n < tries && v.Count < 64000; n++)
            {
                float x = cx + (float)rnd.NextDouble() * Chunk, z = cz + (float)rnd.NextDouble() * Chunk;
                float h = hf(x, z), r = (float)rnd.NextDouble();
                if ((float)rnd.NextDouble() > density(x, z, h, r)) continue;
                float tall = Mathf.Lerp(0.34f, 0.66f, N(x * 0.11f, z * 0.11f)) * R(rnd, 0.72f, 1.2f);
                float w = R(rnd, 0.032f, 0.052f);
                float yaw = R(rnd, 0f, 6.283f), lean = R(rnd, 0.05f, 0.35f) * tall;
                var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
                var fwd = new Vector3(-side.z, 0, side.x);
                var p = new Vector3(x, h, z);
                var rt = new Vector4(x, h, z, r);
                int b = v.Count;
                v.Add(p - side * w); uv.Add(new Vector2(-1, 0));
                v.Add(p + side * w); uv.Add(new Vector2(1, 0));
                var m = p + Vector3.up * tall * 0.55f + fwd * lean * 0.35f;
                v.Add(m - side * w * 0.72f); uv.Add(new Vector2(-1, 0.55f));
                v.Add(m + side * w * 0.72f); uv.Add(new Vector2(1, 0.55f));
                v.Add(p + Vector3.up * tall + fwd * lean); uv.Add(new Vector2(0, 1));
                for (int q = 0; q < 5; q++) rootUv.Add(rt);
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1);
                tri.Add(b + 1); tri.Add(b + 2); tri.Add(b + 3);
                tri.Add(b + 2); tri.Add(b + 4); tri.Add(b + 3);
            }
            if (v.Count == 0) continue;
            var mesh = new Mesh { hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, rootUv); mesh.SetTriangles(tri, 0);
            var empty = new Color32[v.Count];
            mesh.colors32 = empty;
            mesh.RecalculateBounds();
            var bd = mesh.bounds; bd.Expand(2f); mesh.bounds = bd;
            var mr = AddMesh(name, mesh, mat);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // Flowers grow in drifts rather than evenly, one species per drift.
    static Mesh Flowers(HeightFn hf, float cx, float cz, float radius, int drifts, System.Random rnd)
    {
        var pal = new[]
        {
            new Color(0.82f,0.66f,0.28f,1), new Color(0.90f,0.86f,0.72f,1), new Color(0.44f,0.54f,0.74f,1),
            new Color(0.74f,0.42f,0.34f,1), new Color(0.84f,0.74f,0.42f,1)
        };
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var rt = new List<Vector4>(); var col = new List<Color>(); var tri = new List<int>();
        for (int d = 0; d < drifts; d++)
        {
            float dx = cx + R(rnd, -radius, radius), dz = cz + R(rnd, -radius, radius);
            var c = pal[rnd.Next(pal.Length)];
            float spread = R(rnd, 1.2f, 3.6f);
            int count = rnd.Next(40, 170);
            for (int i = 0; i < count; i++)
            {
                float a = R(rnd, 0, 6.283f), rr = spread * Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(0.02f, (float)rnd.NextDouble()))) * 0.5f;
                float x = dx + Mathf.Cos(a) * rr, z = dz + Mathf.Sin(a) * rr, h = hf(x, z);
                if (h < 0.5f || PathMask(x, z) > 0.28f) continue;
                float y = h + R(rnd, 0.32f, 0.56f), s = R(rnd, 0.05f, 0.08f), rot = R(rnd, 0, 6.283f);
                var cc = c * R(rnd, 0.88f, 1.08f); cc.a = 1;
                int b = v.Count;
                v.Add(new Vector3(x, y, z)); col.Add(cc * 0.75f + new Color(0.25f, 0.2f, 0.05f, 0.25f));
                uv.Add(new Vector2(0, 0.85f));
                for (int p = 0; p < 12; p++)
                {
                    float ang = rot + p / 12f * 6.283f, pr = (p % 2 == 0 ? s * 1.15f : s * 0.8f);
                    v.Add(new Vector3(x + Mathf.Cos(ang) * pr, y, z + Mathf.Sin(ang) * pr));
                    col.Add(cc); uv.Add(new Vector2(0, 0.85f));
                    tri.Add(b); tri.Add(b + 1 + (p + 1) % 12); tri.Add(b + 1 + p);
                }
                for (int p = 0; p < 13; p++) rt.Add(new Vector4(x, h, z, (float)rnd.NextDouble()));
            }
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        for (int i = 0; i < col.Count; i++) { var l = col[i].linear; l.a = col[i].a; col[i] = l; }
        m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, rt); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    // ---------- props

    class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<int> t = new List<int>();
        static readonly Color LeafDark = new Color(0.12f, 0.23f, 0.17f, 1), LeafMid = new Color(0.25f, 0.42f, 0.24f, 1),
            LeafLight = new Color(0.49f, 0.60f, 0.30f, 1), LeafTip = new Color(0.66f, 0.65f, 0.35f, 1);

        public Mesh ToMesh()
        {
            var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < c.Count; i++) { var l = c[i].linear; l.a = c[i].a; c[i] = l; }
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        void Tri(Vector3 a, Vector3 b, Vector3 d, Vector3 normal, Color col)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            n.Add(normal); n.Add(normal); n.Add(normal);
            c.Add(col); c.Add(col); c.Add(col);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Tree(Vector3 p, float s, System.Random r, int leaves = 1700)
        {
            var bark = new Color(0.33f, 0.24f, 0.18f, 0);
            float H = 3.0f * s, r0 = 0.27f * s, r1 = 0.12f * s;
            for (int i = 0; i < 7; i++)
            {
                float a0 = i / 7f * 6.283f, a1 = (i + 1) / 7f * 6.283f;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var a = p + d0 * r0; var b = p + d1 * r0; var e = p + d0 * r1 + Vector3.up * H; var f = p + d1 * r1 + Vector3.up * H;
                var nm = (d0 + d1).normalized;
                var cc = bark * (0.9f + i % 3 * 0.08f); cc.a = 0;
                Tri(a, e, b, nm, cc); Tri(b, e, f, nm, cc);
            }
            Blob(p + Vector3.up * (H + 0.65f * s), 1.7f * s, leaves, r);
            int extras = r.Next(3, 6);
            for (int i = 0; i < extras; i++)
            {
                float a = R(r, 0, 6.283f);
                Blob(p + new Vector3(Mathf.Cos(a) * 1.35f * s, H + R(r, -0.45f, 0.5f) * s, Mathf.Sin(a) * 1.35f * s), R(r, 0.85f, 1.25f) * s, leaves / 2, r);
            }
        }

        // Dark solid dome with bright leaf clumps gathered on the sun side.
        public void VividBush(Vector3 p, float s, System.Random r)
        {
            var core = new Color(0.07f, 0.29f, 0.17f, 0);
            float rad = 0.95f * s;
            var c0 = p + Vector3.up * 0.62f * s;
            const int Rings = 7, Seg = 12;
            for (int j = 0; j < Rings; j++)
            for (int i = 0; i < Seg; i++)
            {
                Vector3 P(int a, int b)
                {
                    float th = b / (float)Rings * Mathf.PI, ph = a / (float)Seg * 6.283f;
                    return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                }
                var d = P(i, j); var e = P(i + 1, j); var f = P(i, j + 1); var g = P(i + 1, j + 1);
                int b0 = v.Count;
                v.Add(c0 + d * rad); v.Add(c0 + f * rad); v.Add(c0 + e * rad); v.Add(c0 + g * rad);
                n.Add(d); n.Add(f); n.Add(e); n.Add(g);
                c.Add(core); c.Add(core); c.Add(core); c.Add(core);
                t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 1); t.Add(b0 + 1); t.Add(b0 + 2); t.Add(b0 + 3);
            }
            var sunSide = new Vector3(-0.55f, 0.72f, -0.42f).normalized;
            for (int cl = 0, clumps = Mathf.RoundToInt(74 * s); cl < clumps; cl++)
            {
                var dir = (Random3(r) * 0.75f + new Vector3(-0.2f, 0.9f, -0.35f) * R(r, 0.25f, 0.85f)).normalized;
                var cc = c0 + dir * rad * 1.02f;
                float lit = Mathf.Clamp01(Vector3.Dot(dir, sunSide) * 0.9f + 0.22f);
                var bright = Color.Lerp(new Color(0.12f, 0.46f, 0.21f, 1), new Color(0.50f, 0.92f, 0.22f, 1), lit * lit);
                for (int l = 0; l < 24; l++)
                {
                    var pos = cc + Random3(r) * R(r, 0.02f, 0.26f) * s;
                    var nm = (dir * 0.9f + Vector3.up * 0.4f + Random3(r) * 0.15f).normalized;
                    var tan = Vector3.Cross(nm, Random3(r)).normalized;
                    var bit = Vector3.Cross(nm, tan);
                    float q = R(r, 0.06f, 0.115f) * s;
                    var col = bright * R(r, 0.82f, 1.12f); col.a = 1;
                    int b = v.Count;
                    v.Add(pos - tan * q); v.Add(pos + bit * q * 0.7f); v.Add(pos + tan * q); v.Add(pos - bit * q * 0.7f);
                    for (int k = 0; k < 4; k++) { n.Add(sunSide); c.Add(col); }
                    t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }
        }

        public void Bush(Vector3 p, float s, System.Random r)
        {
            Blob(p + Vector3.up * 0.42f * s, 0.8f * s, 700, r);
            Blob(p + new Vector3(R(r, -0.6f, 0.6f), 0.28f, R(r, -0.6f, 0.6f)) * s, 0.55f * s, 360, r);
        }

        // A dark solid core with bright leaf cards facing outward: top lit, underside deep.
        void Blob(Vector3 c0, float rad, int leaves, System.Random r)
        {
            const int Rings = 5, Seg = 9;
            for (int j = 0; j < Rings; j++)
            for (int i = 0; i < Seg; i++)
            {
                Vector3 P(int a, int b)
                {
                    float th = b / (float)Rings * Mathf.PI, ph = a / (float)Seg * 6.283f;
                    return new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                }
                var d = P(i, j); var e = P(i + 1, j); var f = P(i, j + 1); var g = P(i + 1, j + 1);
                float cr = rad * 0.7f;
                var cc = LeafDark;  cc.a = 0;
                int b0 = v.Count;
                v.Add(c0 + d * cr); v.Add(c0 + f * cr); v.Add(c0 + e * cr); v.Add(c0 + g * cr);
                n.Add(d); n.Add(f); n.Add(e); n.Add(g);
                c.Add(cc); c.Add(cc); c.Add(cc); c.Add(cc);
                t.Add(b0); t.Add(b0 + 2); t.Add(b0 + 1); t.Add(b0 + 1); t.Add(b0 + 2); t.Add(b0 + 3);
            }
            for (int l = 0; l < leaves; l++)
            {
                var dir = Random3(r);
                float shell = Mathf.Lerp(0.66f, 1.02f, Mathf.Pow((float)r.NextDouble(), 0.6f));
                var pos = c0 + dir * rad * shell;
                var nm = (dir + Vector3.up * 0.25f + Random3(r) * 0.18f).normalized;
                var tan = Vector3.Cross(nm, Random3(r)).normalized;
                var bit = Vector3.Cross(nm, tan);
                float s = R(r, 0.13f, 0.24f) * Mathf.Clamp(rad, 0.8f, 1.8f);
                var k = dir.y * 0.5f + 0.5f + R(r, -0.1f, 0.1f);
                var col = k < 0.45f ? Color.Lerp(LeafDark, LeafMid, k / 0.45f)
                        : k < 0.8f ? Color.Lerp(LeafMid, LeafLight, (k - 0.45f) / 0.35f)
                        : Color.Lerp(LeafLight, LeafTip, (k - 0.8f) / 0.2f);
                col.a = 1;
                int b = v.Count;
                v.Add(pos - tan * s); v.Add(pos + bit * s * 0.55f); v.Add(pos + tan * s); v.Add(pos - bit * s * 0.55f);
                for (int q = 0; q < 4; q++) { n.Add(nm); c.Add(col); }
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }

        static Vector3 Random3(System.Random r)
        {
            float z = R(r, -1f, 1f), a = R(r, 0f, 6.283f), s = Mathf.Sqrt(1f - z * z);
            return new Vector3(s * Mathf.Cos(a), z, s * Mathf.Sin(a));
        }

        // Displaced icosphere with flat facets, moss where the face looks up.
        public void Rock(Vector3 p, float s, System.Random r)
        {
            float g = 1.618f;
            var b = new[]
            {
                new Vector3(-1,g,0), new Vector3(1,g,0), new Vector3(-1,-g,0), new Vector3(1,-g,0),
                new Vector3(0,-1,g), new Vector3(0,1,g), new Vector3(0,-1,-g), new Vector3(0,1,-g),
                new Vector3(g,0,-1), new Vector3(g,0,1), new Vector3(-g,0,-1), new Vector3(-g,0,1)
            };
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            float sx = R(r, 0.9f, 1.5f), sy = R(r, 0.55f, 0.95f), sz = R(r, 0.8f, 1.3f), seed = R(r, 0, 90);
            var rot = Quaternion.Euler(R(r, -14, 14), R(r, 0, 360), R(r, -14, 14));
            var stone = Color.Lerp(new Color(0.42f, 0.46f, 0.45f, 0), new Color(0.56f, 0.57f, 0.52f, 0), (float)r.NextDouble());
            var moss = new Color(0.42f, 0.50f, 0.27f, 0);
            Vector3 D(Vector3 q)
            {
                q = q.normalized;
                float k = 0.78f + Mathf.PerlinNoise(q.x * 1.3f + seed, q.z * 1.3f + q.y + seed) * 0.42f;
                return p + rot * new Vector3(q.x * sx, q.y * sy, q.z * sz) * s * k + Vector3.up * sy * s * 0.28f;
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                var a0 = b[f[i]]; var b0 = b[f[i + 1]]; var c0 = b[f[i + 2]];
                var ab = (a0 + b0) * 0.5f; var bc = (b0 + c0) * 0.5f; var ca = (c0 + a0) * 0.5f;
                Face(D(a0), D(ab), D(ca), stone, moss);
                Face(D(ab), D(b0), D(bc), stone, moss);
                Face(D(ca), D(bc), D(c0), stone, moss);
                Face(D(ab), D(bc), D(ca), stone, moss);
            }
        }

        void Face(Vector3 a, Vector3 b, Vector3 d, Color stone, Color moss)
        {
            var nm = Vector3.Cross(b - a, d - a).normalized;
            if (Vector3.Dot(nm, (a + b + d) / 3f) < -1e9f) nm = -nm;
            var col = nm.y > 0.86f ? Color.Lerp(moss, stone, 0.25f) : stone * (0.94f + Mathf.Abs(nm.x) * 0.12f);
            col.a = 0;
            Tri(a, b, d, nm, col);
        }
    }

    // ---------- presets

    public enum Sky { Day, Dusk, Night, Vivid }

    public static Vector3 Lin(float r, float g, float b)
    {
        var c = new Color(r, g, b).linear;
        return new Vector3(c.r, c.g, c.b);
    }

    public static void ApplySky(Sky s, Light sun, float fogDensity, Vector3 pushPos, float pushRadius, float cloud)
    {
        Vector3 sunCol, shadow, top, hor, glow, cl, fog; float stars = 0; Vector3 euler;
        switch (s)
        {
            case Sky.Vivid:
                euler = new Vector3(42, 45, 0);
                sunCol = new Vector3(1.0f, 1.0f, 0.95f); shadow = new Vector3(0.28f, 0.40f, 0.72f);
                top = Lin(0.30f, 0.55f, 0.84f); hor = Lin(0.80f, 0.90f, 0.95f); glow = Lin(1f, 0.92f, 0.72f);
                cl = Lin(0.98f, 0.97f, 0.95f); fog = Lin(0.74f, 0.84f, 0.92f);
                break;
            case Sky.Dusk:
                euler = new Vector3(16, 300, 0);
                sunCol = new Vector3(1.16f, 0.88f, 0.68f); shadow = new Vector3(0.50f, 0.42f, 0.58f);
                top = Lin(0.32f, 0.33f, 0.62f); hor = Lin(1.0f, 0.62f, 0.42f); glow = Lin(1f, 0.56f, 0.28f);
                cl = Lin(0.98f, 0.46f, 0.36f); fog = Lin(0.86f, 0.60f, 0.56f);
                break;
            case Sky.Night:
                euler = new Vector3(52, 40, 0);
                sunCol = new Vector3(0.24f, 0.30f, 0.50f); shadow = new Vector3(0.12f, 0.15f, 0.28f);
                top = Lin(0.035f, 0.055f, 0.20f); hor = Lin(0.11f, 0.15f, 0.36f); glow = Lin(0.22f, 0.28f, 0.45f);
                cl = Lin(0.1f, 0.13f, 0.3f); fog = Lin(0.08f, 0.11f, 0.26f); stars = 1;
                break;
            default:
                euler = new Vector3(50, 322, 0);
                sunCol = new Vector3(1.12f, 1.07f, 0.92f); shadow = new Vector3(0.40f, 0.47f, 0.56f);
                top = Lin(0.30f, 0.55f, 0.84f); hor = Lin(0.80f, 0.90f, 0.95f); glow = Lin(1f, 0.92f, 0.72f);
                cl = Lin(0.98f, 0.97f, 0.95f); fog = Lin(0.74f, 0.84f, 0.92f);
                break;
        }
        if (sun) sun.transform.rotation = Quaternion.Euler(euler);
        Shader.SetGlobalVector("_LSSunCol", sunCol);
        Shader.SetGlobalVector("_LSShadowMul", shadow);
        Shader.SetGlobalVector("_LSSkyTop", top);
        Shader.SetGlobalVector("_LSSkyHor", hor);
        Shader.SetGlobalVector("_LSSkyGlow", glow);
        Shader.SetGlobalVector("_LSCloudCol", cl);
        Shader.SetGlobalVector("_LSFogCol", fog);
        Shader.SetGlobalFloat("_LSFogDensity", fogDensity);
        Shader.SetGlobalFloat("_LSStars", stars);
        Shader.SetGlobalFloat("_LSCloudShade", cloud);
        Shader.SetGlobalVector("_LSPush", new Vector4(pushPos.x, pushPos.y, pushPos.z, pushRadius));
    }
}
