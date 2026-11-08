// Korean mountain valley stage: S-curved stream, two deep pools, shallows, waterfall, granite boulders,
// pines on the slopes, dokkaebi fire. Authored meshes are placed from fixed seeds at runtime.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyValley : MonoBehaviour
{
    [System.Serializable]
    public class Prop { public Mesh mesh; public Material material; }

    public Material terrain, water, fall, moteMat, mistMat, wispMat;
    public Prop[] bigRocks, smallRocks;
    public Prop pineBranches, pineTrunk, pineDead;
    public Mesh[] grassClumps; public Material grassMat, reedTintUnused;
    public Prop[] extras;

    public const float Water = 0f;
    Transform root;
    public readonly List<Vector3> WispPositions = new List<Vector3>();

    // Stream centreline: position (x, z), half width, depth. Runs from the waterfall (north-east) to south-west.
    static readonly Vector4[] River =
    {
        new Vector4(46, 44, 3.6f, 1.2f), new Vector4(34, 37, 5.0f, 2.0f), new Vector4(27, 26, 7.5f, 2.4f),
        new Vector4(20, 17, 4.2f, 0.42f), new Vector4(10, 12, 3.2f, 0.34f), new Vector4(3, 6, 6.2f, 2.0f),
        new Vector4(-1, -2, 4.0f, 1.3f), new Vector4(-3, -9, 6.6f, 2.2f), new Vector4(-12, -16, 3.6f, 0.4f),
        new Vector4(-24, -20, 3.4f, 0.32f), new Vector4(-38, -29, 4.4f, 0.5f), new Vector4(-60, -42, 5.5f, 0.8f)
    };

    void OnEnable() { Build(); }
    void OnDisable() { if (!Application.isPlaying) Clear(); else ReleaseMeshes(); }
    // In play mode the scene unload destroys the generated objects, but meshes made with new Mesh() are
    // assets that outlive them, so free them here. (Objects cannot be destroyed while being deactivated.)
    void ReleaseMeshes()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) Destroy(mf.sharedMesh);
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (mc.sharedMesh) Destroy(mc.sharedMesh);
    }


    void Update()
    {
        if (Application.isPlaying) Shader.SetGlobalFloat("_LSTime", Time.time);
        PushWisps(Application.isPlaying ? Time.time : 12f);
    }

    public void Clear()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) DestroyImmediate(mf.sharedMesh);
        DestroyImmediate(root.gameObject);
    }

    // ---------- shape

    static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
    static float N(float x, float y) => Mathf.PerlinNoise(x + 311.7f, y + 97.3f);

    // Distance to the stream, plus interpolated width, depth and flow direction at the nearest point.
    public static void Nearest(float x, float z, out float dist, out float width, out float depth, out Vector2 dir, out float side)
    {
        dist = 1e9f; width = 4; depth = 1; dir = Vector2.right; side = 0;
        var p = new Vector2(x, z);
        for (int i = 0; i < River.Length - 1; i++)
        {
            var a = new Vector2(River[i].x, River[i].y); var b = new Vector2(River[i + 1].x, River[i + 1].y);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float s = t * t * (3 - 2 * t);
            var q = a + ab * t;
            float d = Vector2.Distance(p, q);
            if (d < dist)
            {
                dist = d;
                width = Mathf.Lerp(River[i].z, River[i + 1].z, s);
                depth = Mathf.Lerp(River[i].w, River[i + 1].w, s);
                dir = ab.normalized;
                side = ab.x * (p.y - a.y) - ab.y * (p.x - a.x) > 0 ? 1 : -1; // +1 left of flow = north-west bank
            }
        }
    }

    public static float Height(float x, float z)
    {
        float wob = (N(x * 0.07f, z * 0.07f) - 0.5f) * 3.2f;
        Nearest(x, z, out float d0, out float w, out float depth, out Vector2 dir, out float side);
        float d = d0 + wob;
        float bumps = (N(x * 0.11f + 5f, z * 0.11f) - 0.5f) * 1.5f + (N(x * 0.33f, z * 0.33f + 9f) - 0.5f) * 0.5f;
        float bed = -depth * (1f - SS(0f, 1f, Mathf.Clamp01(d / w)));
        float bank = SS(w, w + 5f, d) * (1.0f + bumps * 0.35f);
        float slopeK = side < 0 ? 0.62f : 0.14f;               // east slope is steep, west side rises gently
        float hill = Mathf.Max(0f, d - w - 5f) * slopeK;
        hill += SS(w + 12f, w + 40f, d) * (N(x * 0.04f, z * 0.04f) * 9f);
        float h = bed + bank + hill + (d > w ? bumps * 0.7f * SS(w, w + 10f, d) : 0f);

        // Flat granite shelf on the west bank (너럭바위).
        float shelf = 1f - SS(0.8f, 1.25f, new Vector2((x + 19f) / 9.5f, (z - 7f) / 6.5f).magnitude + (N(x * 0.3f, z * 0.3f) - 0.5f) * 0.25f);
        h = Mathf.Lerp(h, Mathf.Max(1.5f, h * 0.2f) + (N(x * 0.5f, z * 0.5f) - 0.5f) * 0.14f, shelf);

        // Cliff wall that closes the valley behind the waterfall.
        var f = new Vector2(x - 37f, z - 41f);
        float behind = Vector2.Dot(f, new Vector2(0.62f, 0.78f));
        float wall = SS(-1.5f, 3.5f, behind) * (11f + (N(x * 0.09f, z * 0.09f) - 0.5f) * 5f + Mathf.Max(0f, behind - 3f) * 0.7f);
        wall *= 1f - SS(16f, 38f, Mathf.Abs(Vector2.Dot(f, new Vector2(0.78f, -0.62f)))) * 0.75f;
        if (wall > 0.01f) h = Mathf.Max(h, wall);
        return h;
    }

    // ---------- build

    public void Build()
    {
        if (!terrain) return;
        Clear();
        root = new GameObject("ValleyGenerated").transform;
        root.SetParent(transform, false);
        root.gameObject.hideFlags = HideFlags.DontSave;
        BuildTerrain();
        BuildWater();
        BuildFall();
        BuildProps();
        BuildEffects();
    }

    MeshRenderer Add(string name, Mesh m, Material mat, bool shadows)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        go.GetComponent<MeshFilter>().sharedMesh = m;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return mr;
    }

    void BuildTerrain()
    {
        const float X0 = -70, X1 = 70, Z0 = -55, Z1 = 62, Step = 0.35f;
        int nx = Mathf.CeilToInt((X1 - X0) / Step) + 1, nz = Mathf.CeilToInt((Z1 - Z0) / Step) + 1;
        var v = new Vector3[nx * nz]; var c = new Color[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = X0 + i * Step, z = Z0 + j * Step, h = Height(x, z);
            v[j * nx + i] = new Vector3(x, h, z);
            Nearest(x, z, out float d, out float w, out _, out _, out _);
            float shelf = 1f - SS(0.7f, 1.0f, new Vector2((x + 19f) / 9.5f, (z - 7f) / 6.5f).magnitude);
            float pebble = 1f - SS(w * 0.85f, w + 1.2f, d);
            float gravel = 1f - SS(w + 1.5f, w + 6.5f, d);
            float forest = SS(w + 4f, w + 9f, d);
            c[j * nx + i] = new Color(pebble, gravel, Mathf.Max(shelf, SS(7f, 12f, h)), forest);
        }
        var t = new int[(nx - 1) * (nz - 1) * 6];
        int k = 0;
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int a = j * nx + i, b = a + 1, cc = a + nx, d = cc + 1;
            t[k++] = a; t[k++] = cc; t[k++] = b; t[k++] = b; t[k++] = cc; t[k++] = d;
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.vertices = v; m.colors = c; m.triangles = t;
        m.RecalculateNormals(); m.RecalculateBounds();
        Add("Terrain", m, terrain, true).gameObject.AddComponent<MeshCollider>().sharedMesh = m;
    }

    void BuildWater()
    {
        const float X0 = -70, X1 = 56, Z0 = -55, Z1 = 52, Step = 0.5f;
        var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
        int nx = Mathf.CeilToInt((X1 - X0) / Step) + 1, nz = Mathf.CeilToInt((Z1 - Z0) / Step) + 1;
        var map = new int[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = X0 + i * Step, z = Z0 + j * Step;
            map[j * nx + i] = -1;
            if (Height(x, z) > 1.2f) continue;
            Nearest(x, z, out float d, out float w, out float depth, out Vector2 dir, out _);
            // Pools barely move; narrow shallows run fast.
            float speed = Mathf.Clamp01(1.25f - depth * 0.55f);
            map[j * nx + i] = verts.Count;
            verts.Add(new Vector3(x, Water, z));
            cols.Add(new Color(dir.x * 0.5f + 0.5f, dir.y * 0.5f + 0.5f, speed, 1));
        }
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int a = map[j * nx + i], b = map[j * nx + i + 1], c = map[(j + 1) * nx + i], d = map[(j + 1) * nx + i + 1];
            if (a < 0 || b < 0 || c < 0 || d < 0) continue;
            tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.SetVertices(verts); m.SetColors(cols); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        Add("Water", m, water, false);
    }

    void BuildFall()
    {
        // A curved ribbon sliding down the rock face into the upper pool.
        var top = new Vector3(39.5f, 11.2f, 42.5f); var bottom = new Vector3(35.5f, -0.1f, 37.5f);
        var side = new Vector3(0.78f, 0, -0.62f);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        const int Seg = 18;
        for (int s = 0; s <= Seg; s++)
        {
            float f = s / (float)Seg;
            var p = Vector3.Lerp(top, bottom, f) + (bottom - top).normalized * 0f;
            p.y = Mathf.Lerp(top.y, bottom.y, f * f * 0.7f + f * 0.3f);
            float w = Mathf.Lerp(1.4f, 2.6f, f);
            v.Add(p - side * w); uv.Add(new Vector2(0, f));
            v.Add(p + side * w); uv.Add(new Vector2(1, f));
            if (s < Seg) { int b = s * 2; t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); t.Add(b + 2); }
        }
        var m = new Mesh { hideFlags = HideFlags.DontSave };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds(); m.RecalculateNormals();
        Add("Waterfall", m, fall, false);
    }

    // ---------- props

    class Batch
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector4> tan = new List<Vector4>(), root = new List<Vector4>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<int> tri = new List<int>();
        public int Tris => tri.Count / 3;
    }

    class Source
    {
        public Vector3[] v, n; public Vector4[] tan; public Vector2[] uv; public int[] t;
        public Source(Mesh m) { v = m.vertices; n = m.normals; tan = m.tangents; uv = m.uv; t = m.triangles; }
    }

    readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
    readonly Dictionary<Mesh, Source> sources = new Dictionary<Mesh, Source>();

    void Stamp(Prop p, Vector3 pos, Quaternion rot, Vector3 scale, Color tint)
    {
        if (p == null || !p.mesh || !p.material) return;
        if (!sources.TryGetValue(p.mesh, out var s)) sources[p.mesh] = s = new Source(p.mesh);
        if (!batches.TryGetValue(p.material, out var b)) batches[p.material] = b = new Batch();
        int bv = b.v.Count;
        var r = new Vector4(pos.x, pos.y, pos.z, Random.value);
        for (int i = 0; i < s.v.Length; i++)
        {
            var q = s.v[i]; q.x *= scale.x; q.y *= scale.y; q.z *= scale.z;
            b.v.Add(pos + rot * q);
            b.n.Add(rot * s.n[i]);
            var tn = s.tan.Length > i ? s.tan[i] : new Vector4(1, 0, 0, 1);
            var tv = rot * new Vector3(tn.x, tn.y, tn.z);
            b.tan.Add(new Vector4(tv.x, tv.y, tv.z, tn.w));
            b.uv.Add(s.uv.Length > i ? s.uv[i] : Vector2.zero);
            b.root.Add(r);
            b.col.Add(tint);
        }
        for (int i = 0; i < s.t.Length; i++) b.tri.Add(bv + s.t[i]);
    }

    void FlushBatches(bool shadows)
    {
        foreach (var kv in batches)
        {
            var b = kv.Value;
            if (b.v.Count == 0) continue;
            var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetTangents(b.tan); m.SetUVs(0, b.uv); m.SetUVs(1, b.root); m.SetColors(b.col); m.SetTriangles(b.tri, 0);
            m.RecalculateBounds();
            Add("Props_" + kv.Key.name, m, kv.Key, shadows);
        }
        batches.Clear();
    }

    static Quaternion RandY(System.Random r, float tilt = 8f) => Quaternion.Euler(R(r, -tilt, tilt), R(r, 0, 360), R(r, -tilt, tilt));
    static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    static Color Tint(System.Random r, float k = 0.1f) { float t = R(r, 1f - k, 1f + k); return new Color(t, t, t, 1); }

    void Big(System.Random r, float x, float z, float size, float sink = 0.22f)
    {
        var p = bigRocks[r.Next(bigRocks.Length)];
        float h = Height(x, z);
        float base0 = p.mesh.bounds.size.y;
        float s = size / Mathf.Max(p.mesh.bounds.size.x, p.mesh.bounds.size.z);
        var pos = new Vector3(x, Mathf.Max(h, Water - 1.2f) - base0 * s * sink, z);
        var rot = RandY(r, 12f);
        float sy = s * R(r, 0.8f, 1.2f);
        Stamp(p, pos, rot, new Vector3(s, sy, s), Tint(r));
        if (size >= 1.5f)
        {
            var b = p.mesh.bounds;
            Col(pos + rot * new Vector3(b.center.x * s, b.center.y * sy, b.center.z * s), new Vector3(b.size.x * s * 0.82f, b.size.y * sy, b.size.z * s * 0.82f), Quaternion.Euler(0, rot.eulerAngles.y, 0));
        }
    }

    // Invisible blockers on the Default layer: they stop the player, enemies and bullets.
    void Col(Vector3 center, Vector3 size, Quaternion rot)
    {
        var go = new GameObject("Col"); go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(center, rot);
        go.AddComponent<BoxCollider>().size = size;
    }


    void Cobble(System.Random r, float x, float z, float size)
    {
        var p = smallRocks[r.Next(smallRocks.Length)];
        float s = size / Mathf.Max(p.mesh.bounds.size.x, p.mesh.bounds.size.z);
        Stamp(p, new Vector3(x, Height(x, z) - p.mesh.bounds.size.y * s * 0.3f, z), RandY(r, 22f), new Vector3(s, s * R(r, 0.7f, 1.1f), s), Tint(r, 0.14f));
    }

    void BuildProps()
    {
        var r = new System.Random(2025);
        Random.InitState(2025);

        // The piled granite group from the reference, hugging the upper pool.
        foreach (var b in new[] { new Vector3(18.5f, 25, 6.2f), new Vector3(22, 34, 5.4f), new Vector3(16, 30, 4.6f), new Vector3(35, 22, 5.8f), new Vector3(39, 28, 4.8f), new Vector3(33, 16, 4.2f),
                                  new Vector3(14, 19.5f, 3.8f), new Vector3(27, 13, 3.4f), new Vector3(26, 37, 4.4f), new Vector3(43, 35, 5.2f) })
            Big(r, b.x, b.y, b.z * 1.25f);
        // Middle gourd pool.
        foreach (var b in new[] { new Vector3(12, 5, 4.6f), new Vector3(9, -7, 5.4f), new Vector3(-9, 6, 3.8f), new Vector3(-12, -5, 4.8f), new Vector3(7, 9, 2.6f), new Vector3(9, -18, 3.6f),
                                  new Vector3(-2, -1.8f, 2.4f), new Vector3(-13, -9.5f, 2.6f) })
            Big(r, b.x, b.y, b.z * 1.2f);
        // Lower run and arena edges.
        foreach (var b in new[] { new Vector3(-21, -13, 3.6f), new Vector3(-27, -27, 4.2f), new Vector3(-16, -24, 3.2f), new Vector3(-33, -19, 4.6f), new Vector3(-30, 16, 5.6f), new Vector3(-27, 20, 3.8f), new Vector3(24, -12, 6.4f), new Vector3(30, -5, 5.0f), new Vector3(20, -22, 5.4f) })
            Big(r, b.x, b.y, b.z);
        // Rocks breaking the surface of the shallows raise foam Vs.
        foreach (var b in new[] { new Vector3(21, 18, 1.3f), new Vector3(16, 15.5f, 0.9f), new Vector3(12, 11, 1.2f), new Vector3(-10, -15, 1.1f), new Vector3(-17, -18, 1.4f), new Vector3(-25, -19, 1.0f), new Vector3(-31, -25, 1.2f) })
            Big(r, b.x, b.y, b.z, 0.35f);
        // Stepping stones over each run.
        foreach (var b in new[] { new Vector3(11, 15.6f, 2.2f), new Vector3(12.6f, 12.9f, 2.4f), new Vector3(14.1f, 10.2f, 2.2f), new Vector3(-20, -16, 2.2f), new Vector3(-21, -19, 2.4f), new Vector3(-22, -22, 2.2f) })
        {
            var p = bigRocks[r.Next(bigRocks.Length)];
            float s = b.z / Mathf.Max(p.mesh.bounds.size.x, p.mesh.bounds.size.z);
            Stamp(p, new Vector3(b.x, -0.28f, b.y), Quaternion.Euler(0, R(r, 0, 360), 0), new Vector3(s, 0.42f * s, s), Tint(r));
        }
        // Rounded cobbles along the shoreline, thicker near the water.
        for (int i = 0; i < 900; i++)
        {
            float x = R(r, -56, 50), z = R(r, -44, 44);
            Nearest(x, z, out float d, out float w, out float depth, out _, out _);
            if (d > w + 7f) continue;
            if (d < w - 1.2f && depth > 0.7f) continue;
            if (r.NextDouble() > 1.0 - Mathf.Clamp01((d - w) / 8f) * 0.8) continue;
            Cobble(r, x, z, R(r, 0.28f, 0.95f));
        }

        // Stone towers built on the shelf and by the path.
        foreach (var c in new[] { new Vector3(-16.5f, 8.5f, 5), new Vector3(-22, 4.5f, 3), new Vector3(-6, 12, 5), new Vector3(16.5f, 6, 3) })
        {
            float y = Height(c.x, c.y);
            float size = 0.95f;
            for (int k = 0; k < (int)c.z; k++)
            {
                var p = smallRocks[r.Next(smallRocks.Length)];
                float s = size / Mathf.Max(p.mesh.bounds.size.x, p.mesh.bounds.size.z);
                Stamp(p, new Vector3(c.x + R(r, -0.05f, 0.05f), y, c.y + R(r, -0.05f, 0.05f)), Quaternion.Euler(R(r, -4, 4), R(r, 0, 360), R(r, -4, 4)), new Vector3(s, s * 0.8f, s), Tint(r));
                y += p.mesh.bounds.size.y * s * 0.62f;
                size *= 0.82f;
            }
        }

        // Fallen pine bridge over the gourd pool, and a dead tree snagged in the lower run.
        if (pineTrunk != null && pineTrunk.mesh)
        {
            Stamp(pineTrunk, new Vector3(-5.6f, 0.95f, 1.2f), Quaternion.Euler(88, 58, 0), new Vector3(2.0f, 1.25f, 2.0f), new Color(0.72f, 0.62f, 0.54f, 0));
        }
        if (pineDead != null && pineDead.mesh)
        {
            Stamp(pineDead, new Vector3(-29, 0.2f, -22), Quaternion.Euler(80, 150, 12), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.6f, 0.58f, 0.54f, 0));
            foreach (var d in new[] { new Vector3(-14, 16, 1f), new Vector3(29, -9, 1.1f), new Vector3(-32, -8, 0.9f), new Vector3(21, 40, 1.15f) })
                Stamp(pineDead, new Vector3(d.x, Height(d.x, d.y) - 0.3f, d.y), RandY(r, 6f), new Vector3(d.z, d.z, d.z), new Color(0.66f, 0.62f, 0.58f, 0));
        }

        // Pine forest on both slopes, thick outside the arena so the middle stays open.
        if (pineTrunk != null && pineTrunk.mesh && pineBranches != null && pineBranches.mesh)
        {
            int placed = 0;
            for (int i = 0; i < 620 && placed < 150; i++)
            {
                float x = R(r, -68, 66), z = R(r, -52, 60);
                Nearest(x, z, out float d, out float w, out _, out _, out _);
                float h = Height(x, z);
                if (d < w + 15f || h > 16f) continue;
                if (Mathf.Abs(x) < 34f && Mathf.Abs(z) < 25f && d < w + 21f) continue;
                if (new Vector2((x + 19f) / 11f, (z - 7f) / 8f).magnitude < 1f) continue;
                float s = R(r, 0.55f, 0.95f);
                var rot = Quaternion.Euler(R(r, -4, 4), R(r, 0, 360), R(r, -4, 4));
                var tint = new Color(R(r, 0.34f, 0.5f), R(r, 0.46f, 0.62f), R(r, 0.46f, 0.6f), 1);
                var pos = new Vector3(x, h - 0.25f, z);
                Stamp(pineTrunk, pos, rot, new Vector3(s, s, s), new Color(0.42f, 0.36f, 0.34f, 0));
                Col(pos + Vector3.up * 2f, new Vector3(0.7f * s, 4f, 0.7f * s), Quaternion.identity);
                Stamp(pineBranches, pos, rot, new Vector3(s, s, s), tint);
                placed++;
            }
        }
        FlushBatches(true);

        BuildBankGrass(r);
        // Older sparse pass kept off: dense banks replace it.
        if (false && grassMat && grassClumps != null && grassClumps.Length > 0)
        {
            var gs = new Prop[grassClumps.Length];
            for (int i = 0; i < gs.Length; i++) gs[i] = new Prop { mesh = grassClumps[i], material = grassMat };
            for (int i = 0; i < 22000; i++)
            {
                float x = R(r, -56, 52), z = R(r, -44, 46);
                Nearest(x, z, out float d, out float w, out _, out _, out _);
                float h = Height(x, z);
                if (h < 0.08f || h > 9f) continue;
                if (new Vector2((x + 19f) / 9.5f, (z - 7f) / 6.5f).magnitude < 1.05f) continue;
                float edge = Mathf.Exp(-Mathf.Abs(d - w - 1.5f) * 0.28f);
                float patch = N(x * 0.12f, z * 0.12f);
                if (r.NextDouble() > edge * 0.8 + (patch > 0.55f ? 0.35f : 0.02f)) continue;
                float s = R(r, 1.0f, 2.3f) * (0.9f + edge * 0.8f);
                var t = Color.Lerp(new Color(0.55f, 0.68f, 0.6f, 1), new Color(0.9f, 0.88f, 0.6f, 1), patch);
                Stamp(gs[r.Next(gs.Length)], new Vector3(x, h - 0.03f, z), RandY(r, 10f), new Vector3(s, s, s), t);
            }
            FlushBatches(false);
        }
    }

    // Thick grass along both banks and in meadow patches; split into 8 m chunks so the camera culls them.
    public static float GrassDensity(float x, float z)
    {
        Nearest(x, z, out float d, out float w, out _, out _, out _);
        float h = Height(x, z);
        if (h < 0.06f || h > 8f) return 0f;
        if (new Vector2((x + 19f) / 9.5f, (z - 7f) / 6.5f).magnitude < 1.02f) return 0f;
        float bank = SS(w + 0.2f, w + 1.4f, d) * (1f - SS(w + 7f, w + 12f, d));
        float patch = SS(0.48f, 0.62f, N(x * 0.09f + 3f, z * 0.09f));
        return Mathf.Clamp01(Mathf.Max(bank, patch * 0.85f)) * (0.75f + 0.25f * N(x * 0.6f, z * 0.6f));
    }

    public static LookStudyWalker.Surface SurfaceAt(float x, float z)
    {
        Nearest(x, z, out float d, out float w, out _, out _, out _);
        if (GrassDensity(x, z) > 0.45f) return LookStudyWalker.Surface.Grass;
        if (new Vector2((x + 19f) / 9.5f, (z - 7f) / 6.5f).magnitude < 1.0f) return LookStudyWalker.Surface.Stone;
        if (d < w + 6.5f) return LookStudyWalker.Surface.Gravel;
        return LookStudyWalker.Surface.Dirt;
    }

    void BuildBankGrass(System.Random r)
    {
        if (!grassMat || grassClumps == null || grassClumps.Length == 0) return;
        var gs = new Prop[grassClumps.Length];
        for (int i = 0; i < gs.Length; i++) gs[i] = new Prop { mesh = grassClumps[i], material = grassMat };
        const float Chunk = 8f;
        for (float cz = -48; cz < 48; cz += Chunk)
        for (float cx = -60; cx < 56; cx += Chunk)
        {
            for (int i = 0; i < 1500; i++)
            {
                float x = cx + (float)r.NextDouble() * Chunk, z = cz + (float)r.NextDouble() * Chunk;
                float dens = GrassDensity(x, z);
                if (r.NextDouble() > dens) continue;
                float h = Height(x, z);
                var g = gs[r.Next(gs.Length)];
                bool blade = g.mesh.bounds.size.x < 0.2f;
                float s = blade ? R(r, 1.0f, 1.8f) : R(r, 0.85f, 1.45f);
                float patch = N(x * 0.12f, z * 0.12f);
                var t = Color.Lerp(new Color(0.46f, 0.6f, 0.52f, 1), new Color(0.9f, 0.86f, 0.56f, 1), patch);
                if (r.NextDouble() < 0.12) t *= new Color(1.25f, 1.15f, 0.8f, 1);
                Stamp(g, new Vector3(x, h - 0.03f, z), RandY(r, 10f), new Vector3(s, s, s), t);
            }
            FlushBatches(false);
        }
    }

    // ---------- light and particles

    static readonly Vector3[] Wisps =
    {
        new Vector3(26, 1.3f, 27), new Vector3(1.5f, 1.0f, -6), new Vector3(-13.5f, 2.2f, -6), new Vector3(10.5f, 2.4f, 4), new Vector3(-22, 3.3f, 9), new Vector3(34, 1.7f, 33)
    };

    void BuildEffects()
    {
        WispPositions.Clear();
        var col = new Color(0.25f, 0.82f, 0.75f);
        for (int i = 0; i < Wisps.Length; i++)
        {
            WispPositions.Add(Wisps[i]);
            var go = new GameObject("Wisp", typeof(Light));
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            go.transform.position = Wisps[i];
            var l = go.GetComponent<Light>();
            l.type = LightType.Point; l.color = col; l.range = 21f; l.intensity = 30f; l.shadows = LightShadows.None;
            if (wispMat)
            {
                var flame = Particle(go.transform, "Flame", wispMat, 40, 0.45f, 0.8f, 0.28f, 0.6f, Vector3.up * 0.75f, 0.12f, 20f, true);
                var sh = flame.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;
                var c = flame.colorOverLifetime; c.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.5f, 0.95f, 0.88f), 0), new GradientColorKey(col, 0.45f), new GradientColorKey(new Color(0.03f, 0.22f, 0.26f), 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0, 1) });
                c.color = g;
                var sz = flame.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.25f));
                flame.Play();
            }
        }
        if (mistMat)
        {
            // Spray at the base of the waterfall, and low mist drifting along the stream.
            var spray = Particle(root, "FallMist", mistMat, 60, 2f, 4f, 2.5f, 5f, Vector3.up * 0.6f, 0f, 16f, true);
            spray.transform.position = new Vector3(35.5f, 0.3f, 37.5f);
            var sh = spray.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 2.4f;
            FadeAlpha(spray, new Color(0.72f, 0.82f, 0.82f), 0.2f);
            spray.Play();
            foreach (var p in new[] { new Vector3(27, 0.5f, 26), new Vector3(1, 0.4f, -2), new Vector3(-20, 0.4f, -18) })
            {
                var drift = Particle(root, "StreamMist", mistMat, 26, 6f, 11f, 5f, 11f, new Vector3(-0.28f, 0.04f, -0.2f), 0.12f, 2.6f, true);
                drift.transform.position = p;
                var s2 = drift.shape; s2.shapeType = ParticleSystemShapeType.Box; s2.scale = new Vector3(14, 0.5f, 10);
                FadeAlpha(drift, new Color(0.42f, 0.54f, 0.54f), 0.13f);
                drift.Play();
            }
        }
        if (moteMat)
        {
            var motes = Particle(root, "Motes", moteMat, 150, 4f, 9f, 0.04f, 0.09f, Vector3.up * 0.1f, 0.25f, 16f, true);
            motes.transform.position = new Vector3(4, 3, 4);
            var sh = motes.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(55, 5, 44);
            var c = motes.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 1f, 0.9f), 0), new GradientColorKey(new Color(0.55f, 1f, 0.9f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0, 1) });
            c.color = g;
            motes.Play();
        }
        PushWisps(12f);
    }

    static void FadeAlpha(ParticleSystem p, Color c, float alpha)
    {
        var o = p.colorOverLifetime; o.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(alpha, 0.35f), new GradientAlphaKey(0, 1) });
        o.color = g;
    }

    public static ParticleSystem Particle(Transform parent, string name, Material mat, int max, float life0, float life1, float size0, float size1, Vector3 vel, float noise, float rate, bool world)
    {
        var go = new GameObject(name, typeof(ParticleSystem));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        var ps = go.GetComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.maxParticles = max; m.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1); m.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
        m.startSpeed = 0f; m.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.prewarm = true; m.loop = true;
        var e = ps.emission; e.rateOverTime = rate;
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
        v.x = vel.x; v.y = vel.y; v.z = vel.z;
        if (noise > 0f) { var n = ps.noise; n.enabled = true; n.strength = noise; n.frequency = 0.3f; }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        return ps;
    }

    readonly Vector4[] wispData = new Vector4[6];

    void PushWisps(float t)
    {
        for (int i = 0; i < wispData.Length; i++)
        {
            if (i < Wisps.Length)
            {
                var p = Wisps[i];
                float k = 0.8f + 0.2f * Mathf.Sin(t * 3.1f + i * 2f);
                wispData[i] = new Vector4(p.x, p.y, p.z, k);
            }
            else wispData[i] = Vector4.zero;
        }
        Shader.SetGlobalVectorArray("_LSWisps", wispData);
        Shader.SetGlobalVector("_LSWispCol", new Vector3(0.12f, 0.62f, 0.56f));
    }

    public static void ApplyMood(Light sun)
    {
        // Blue hour with a cold rim from the waterfall end: dark, desaturated, lit mostly by wisps.
        if (sun) sun.transform.rotation = Quaternion.Euler(40, 205, 0);
        Shader.SetGlobalVector("_LSSunCol", new Vector3(1.0f, 1.08f, 1.12f));
        Shader.SetGlobalVector("_LSShadowMul", new Vector3(0.36f, 0.46f, 0.52f));
        Shader.SetGlobalVector("_LSSkyTop", LookStudyWorld.Lin(0.05f, 0.08f, 0.13f));
        Shader.SetGlobalVector("_LSSkyHor", LookStudyWorld.Lin(0.16f, 0.22f, 0.25f));
        Shader.SetGlobalVector("_LSSkyGlow", LookStudyWorld.Lin(0.2f, 0.22f, 0.22f));
        Shader.SetGlobalVector("_LSCloudCol", LookStudyWorld.Lin(0.13f, 0.16f, 0.18f));
        Shader.SetGlobalVector("_LSFogCol", LookStudyWorld.Lin(0.13f, 0.18f, 0.18f));
        Shader.SetGlobalFloat("_LSFogDensity", 0.006f);
        Shader.SetGlobalFloat("_LSStars", 0f);
        Shader.SetGlobalFloat("_LSCloudShade", 0.15f);
        Shader.SetGlobalVector("_LSPush", Vector4.zero);
        Shader.SetGlobalFloat("_LSWaterY", Water);
        Shader.SetGlobalFloat("_LSHeightFog", 0.32f);
        Shader.SetGlobalVector("_LSHeightFogCol", LookStudyWorld.Lin(0.22f, 0.3f, 0.3f));
    }
}
