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
    // 11-10 (Kido: arenas 4x the area, reed field size): a 112 x 88 arena over the valley floor, x -62..50, z -47..41.
    // A belt of pines with trunk colliders and boulders closes the edges; the terrain runs on well past them.
    public const float ArenaCX = -6f, ArenaCZ = -3f, ArenaHX = 56f, ArenaHZ = 44f;
    public static bool InArena(float x, float z, float margin = 0f) => Mathf.Abs(x - ArenaCX) < ArenaHX + margin && Mathf.Abs(z - ArenaCZ) < ArenaHZ + margin;
    Transform root;
    public readonly List<Vector3> WispPositions = new List<Vector3>();

    // Stream centreline: position (x, z), half width, depth. Runs from the waterfall (north-east) to south-west.
    static readonly Vector4[] River =
    {
        new Vector4(46, 44, 3.6f, 1.2f), new Vector4(34, 37, 5.0f, 2.0f), new Vector4(27, 26, 7.5f, 2.4f),
        new Vector4(20, 17, 4.2f, 0.42f), new Vector4(10, 12, 3.2f, 0.34f), new Vector4(3, 6, 6.2f, 2.0f),
        new Vector4(-1, -2, 4.0f, 1.3f), new Vector4(-3, -9, 6.6f, 2.2f), new Vector4(-12, -16, 3.6f, 0.4f),
        new Vector4(-24, -20, 3.4f, 0.32f), new Vector4(-38, -29, 4.4f, 0.5f), new Vector4(-60, -42, 5.5f, 0.8f),
        new Vector4(-84, -56, 6.0f, 0.9f), new Vector4(-112, -74, 6.5f, 1.0f)   // runs on out of the arena to the south-west
    };

    void OnEnable() { Build(); }
    void OnDisable() { if (!Application.isPlaying) Clear(); else ReleaseMeshes(); }
    // In play mode the scene unload destroys the generated objects, but meshes made with new Mesh() are
    // assets that outlive them, so free them here. (Objects cannot be destroyed while being deactivated.)
    // Only meshes this script made (DontSave); the downloaded pines keep their imported meshes.
    static bool Own(Mesh m) => m && (m.hideFlags & HideFlags.DontSave) == HideFlags.DontSave;
    void ReleaseMeshes()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (Own(mf.sharedMesh)) Destroy(mf.sharedMesh);
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (Own(mc.sharedMesh)) Destroy(mc.sharedMesh);
    }


    void Update()
    {
        if (Application.isPlaying) Shader.SetGlobalFloat("_LSTime", Time.time);
        PushWisps(Application.isPlaying ? Time.time : 12f);
    }

    public void Clear()
    {
        if (!root) return;
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) if (Own(mc.sharedMesh) && !mc.GetComponent<MeshFilter>()) DestroyImmediate(mc.sharedMesh);
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (Own(mf.sharedMesh)) DestroyImmediate(mf.sharedMesh);
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
        const float X0 = -110, X1 = 92, Z0 = -92, Z1 = 86, Step = 0.45f;
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
        const float X0 = -110, X1 = 56, Z0 = -92, Z1 = 52, Step = 0.5f;
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

        // 11-13 (Kido: the pools swallowed the player): an invisible floor WadeDepth under the surface,
        // so walkers stand waist-deep over the deep pools while the bed (and the dark water colour) stays as is.
        // Where the bed is shallower the terrain is hit first; it sits under the ground everywhere else.
        var floor = new GameObject("WadeFloor", typeof(MeshCollider));
        floor.hideFlags = HideFlags.DontSave;
        floor.transform.SetParent(root, false);
        floor.transform.localPosition = new Vector3(0f, -WadeDepth, 0f);
        floor.GetComponent<MeshCollider>().sharedMesh = m;
        BuildPoolWalls();
    }

    public const float WadeDepth = 0.7f;

    // 11-13 (Kido: the pools look deep and should stay deep, the player only splashes in at the edge): a low invisible
    // fence along the contour where a pool's bed drops below PoolEdgeDepth. Shallow runs (crossings) stay open.
    // Its top sits a little above the surface, under bullet height, so shots still fly over the pools.
    public const float PoolEdgeDepth = 0.3f, PoolWallTop = 0.45f;
    static float PoolField(float x, float z)
    {
        Nearest(x, z, out _, out _, out float depth, out _, out _);
        return Height(x, z) - (Water - PoolEdgeDepth) + Mathf.Max(0f, 0.7f - depth) * 10f;   // < 0: inside a deep pool
    }

    void BuildPoolWalls()
    {
        const float Step = 0.5f;
        float x0 = ArenaCX - ArenaHX - 4f, z0 = ArenaCZ - ArenaHZ - 4f;
        int nx = Mathf.CeilToInt((ArenaHX * 2f + 8f) / Step) + 1, nz = Mathf.CeilToInt((ArenaHZ * 2f + 8f) / Step) + 1;
        var g = new float[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++) g[j * nx + i] = PoolField(x0 + i * Step, z0 + j * Step);

        var v = new List<Vector3>(); var t = new List<int>();
        var pts = new Vector2[4];
        void Seg(Vector2 a, Vector2 b)
        {
            int k = v.Count;
            v.Add(new Vector3(a.x, -3f, a.y)); v.Add(new Vector3(b.x, -3f, b.y));
            v.Add(new Vector3(b.x, PoolWallTop, b.y)); v.Add(new Vector3(a.x, PoolWallTop, a.y));
            t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);   // both faces
            t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2);
        }
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            // corners counter-clockwise: (i,j) (i+1,j) (i+1,j+1) (i,j+1); marching squares, one crossing per sign change
            float[] c = { g[j * nx + i], g[j * nx + i + 1], g[(j + 1) * nx + i + 1], g[(j + 1) * nx + i] };
            Vector2[] p = { new Vector2(x0 + i * Step, z0 + j * Step), new Vector2(x0 + (i + 1) * Step, z0 + j * Step),
                            new Vector2(x0 + (i + 1) * Step, z0 + (j + 1) * Step), new Vector2(x0 + i * Step, z0 + (j + 1) * Step) };
            int n = 0;
            for (int e = 0; e < 4; e++)
            {
                float a = c[e], b = c[(e + 1) & 3];
                if ((a < 0f) == (b < 0f)) continue;
                pts[n++] = Vector2.Lerp(p[e], p[(e + 1) & 3], a / (a - b));
            }
            if (n >= 2) Seg(pts[0], pts[1]);
            if (n == 4) Seg(pts[2], pts[3]);
        }
        if (v.Count == 0) return;
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
        var go = new GameObject("PoolWalls", typeof(MeshCollider));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        go.GetComponent<MeshCollider>().sharedMesh = m;
    }

    // 11-13 (Kido: "여기 폭포", the middle pool by the south gate): a low granite ledge standing in the deep part of the pool's
    // north-west side, water spilling off it in a few uneven strands, with a churn of spray and mist where it lands.
    // The ledge stands where the pool fence already stops the player, so it blocks nothing; it has no collider.
    public static bool PoolFallOn = true;
    // r2 (overview shots 11-14): the pool by the south gate is the lower one, (-3, -9.5); its upper-left bank faces the
    // wisp at (-13.5, -6), so the water comes off the west bank and pours east-south-east into it.
    static Vector2 PoolC = new Vector2(-3f, -9.5f), PoolOut = new Vector2(-0.89f, 0.46f);
    static Material poolFallMat;

    void BuildPoolFall()
    {
        if (!PoolFallOn || bigRocks == null || bigRocks.Length == 0) return;
        // walk out from the pool centre to where the fence runs, and stand the ledge just inside it
        float reach = 1f;
        while (reach < 12f && PoolField(PoolC.x + PoolOut.x * reach, PoolC.y + PoolOut.y * reach) < 0f) reach += 0.25f;
        var lip2 = PoolC + PoolOut * (reach - 1.3f);
        var r = new System.Random(4711);
        var across = new Vector2(PoolOut.y, -PoolOut.x);
        // ledge: a tall boulder behind, two shoulders either side, a flat cap the water pours from
        void Rock(Vector2 at, float size, float sy, float sink)
        {
            var p = bigRocks[r.Next(bigRocks.Length)];
            float s = size / Mathf.Max(p.mesh.bounds.size.x, p.mesh.bounds.size.z);
            float y = Mathf.Max(Height(at.x, at.y), Water - 1.6f) - p.mesh.bounds.size.y * s * sy * sink;
            Stamp(p, new Vector3(at.x, y, at.y), Quaternion.Euler(R(r, -6, 6), R(r, 0, 360), R(r, -6, 6)), new Vector3(s, s * sy, s), Tint(r));
            rockSpots.Add(new Vector3(at.x, at.y, size * 0.5f));
        }
        Rock(lip2 + PoolOut * 1.6f, 5.2f, 1.25f, 0.1f);
        Rock(lip2 + PoolOut * 0.6f + across * 2.4f, 3.8f, 1.1f, 0.18f);
        Rock(lip2 + PoolOut * 0.7f - across * 2.6f, 4.2f, 1.0f, 0.2f);
        Rock(lip2 + PoolOut * 0.3f + across * 0.4f, 3.0f, 0.55f, 0.05f);
        Rock(lip2 - PoolOut * 1.6f + across * 1.7f, 1.3f, 0.6f, 0.4f);   // broken stone in the plunge
        FlushBatches(true);

        if (!fall) return;
        // strands: one wide sheet and two narrow ones, each with its own lean and width so the curtain is uneven
        float top = 2.35f;
        var down = new Vector3(-PoolOut.x, 0, -PoolOut.y);
        var side = new Vector3(across.x, 0, across.y);
        var lip = new Vector3(lip2.x, top, lip2.y);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        // 11-13 r2 (Kido: thinner, see-through, splitting): many narrow strands leaving the ledge at different points and
        // heights, each with its own throw and width; a see-through copy of the fall material with no sheet between streaks.
        var rs = new System.Random(77);
        void Strand(float offset, float w0, float w1, float push, float topY)
        {
            const int Seg = 14;
            int b0 = v.Count;
            float h = topY - Water + 0.1f;
            var start = new Vector3(lip.x, topY, lip.z) + side * offset;
            for (int s = 0; s <= Seg; s++)
            {
                float f = s / (float)Seg;
                // falls off the lip with a little forward throw, curving down under gravity, wobbling sideways a touch
                var p = start + down * (push * Mathf.Sqrt(f) + 0.12f) + side * (Mathf.Sin(f * 5f + offset * 3f) * 0.06f) + Vector3.down * (h * f * f * 0.85f + h * f * 0.15f);
                float w = Mathf.Lerp(w0, w1, f * f);
                v.Add(p - side * w); uv.Add(new Vector2(0, f));
                v.Add(p + side * w); uv.Add(new Vector2(1, f));
                if (s < Seg) { int b = b0 + s * 2; t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); t.Add(b + 2); }
            }
        }
        foreach (var o in new[] { -1.55f, -1.1f, -0.62f, -0.2f, 0.18f, 0.55f, 0.98f, 1.4f })
        {
            if (rs.NextDouble() < 0.15) continue;
            float w = (float)(0.07 + rs.NextDouble() * 0.2);
            Strand(o + (float)(rs.NextDouble() - 0.5) * 0.2f, w, w * (1.6f + (float)rs.NextDouble()), 0.45f + (float)rs.NextDouble() * 0.7f, top - (float)rs.NextDouble() * 0.55f);
        }
        Strand(-0.05f, 0.32f, 0.55f, 0.75f, top + 0.05f);   // the main pour
        var m = new Mesh { hideFlags = HideFlags.DontSave };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds(); m.RecalculateNormals();
        if (!poolFallMat) poolFallMat = new Material(fall) { name = "LS_PoolFall", hideFlags = HideFlags.DontSave };
        poolFallMat.SetFloat("_Alpha", 0.62f); poolFallMat.SetFloat("_Fill", -0.05f); poolFallMat.SetFloat("_Speed", 1.7f);
        Add("PoolFall", m, poolFallMat, false);

        var foot = lip + down * 1.1f; foot.y = Water + 0.15f;
        if (mistMat)
        {
            var churn = Particle(root, "PoolFallChurn", mistMat, 50, 0.8f, 1.6f, 0.7f, 1.6f, Vector3.up * 0.9f, 0.4f, 34f, true);
            churn.transform.position = foot;
            var sh = churn.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2.8f, 0.2f, 1.2f);
            churn.transform.rotation = Quaternion.LookRotation(down);
            FadeAlpha(churn, new Color(0.8f, 0.88f, 0.88f), 0.35f);
            churn.Play();
            var mist = Particle(root, "PoolFallMist", mistMat, 30, 3f, 6f, 2.5f, 4.5f, new Vector3(-PoolOut.x * 0.25f, 0.25f, -PoolOut.y * 0.25f), 0.1f, 6f, true);
            mist.transform.position = foot + Vector3.up * 0.3f;
            var sh2 = mist.shape; sh2.shapeType = ParticleSystemShapeType.Sphere; sh2.radius = 1.6f;
            FadeAlpha(mist, new Color(0.62f, 0.72f, 0.72f), 0.14f);
            mist.Play();
        }
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
        rockSpots.Add(new Vector3(x, z, size * 0.5f));
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

    // Big rock footprints (x, z, radius): bank grass keeps off them (11-13 Kido: real valley boulders carry
    // a little moss but no grass on top).
    readonly List<Vector3> rockSpots = new List<Vector3>();
    bool OnRock(float x, float z)
    {
        foreach (var k in rockSpots)
        {
            float dx = x - k.x, dz = z - k.y, rr = k.z * 1.05f + 0.25f;
            if (dx * dx + dz * dz < rr * rr) return true;
        }
        return false;
    }

    // ---------- pines (11-13 Kido: all the same, tops too long and spiky)

    System.Random mixR;
    LookStudyValleyTrees trees;
    int heroesLeft;
    const int HeroCap = 7;   // ~156k tris each

    // The valley's own pine, squashed so the spire top is shorter and blunter, and a bit wider.
    void OwnPine(Vector3 pos, Quaternion rot, float s, Color tint)
    {
        var sc = new Vector3(s * 1.08f, s * R(mixR, 0.72f, 0.84f), s * 1.08f);
        Stamp(pineTrunk, pos, rot, sc, new Color(0.42f, 0.36f, 0.34f, 0));
        Stamp(pineBranches, pos, rot, sc, tint);
    }

    // Maybe a downloaded pine here instead; true when one was placed.
    bool MixPine(Vector3 pos, double heroChance, double pineChance)
    {
        if (!trees) return false;
        double roll = mixR.NextDouble();
        GameObject src; float scale;
        if (roll < heroChance && heroesLeft > 0 && trees.heroes != null && trees.heroes.Length > 0)
        {
            src = trees.heroes[mixR.Next(trees.heroes.Length)]; scale = R(mixR, 0.75f, 1.0f); heroesLeft--;
        }
        else if (roll < heroChance + pineChance && trees.pines != null && trees.pines.Length > 0)
        {
            src = trees.pines[mixR.Next(trees.pines.Length)]; scale = R(mixR, 0.55f, 0.9f);
        }
        else return false;
        if (!src) return false;
        Place(src, pos, scale);
        return true;
    }

    GameObject Place(GameObject src, Vector3 pos, float scale)
    {
        var go = Instantiate(src, root, false);
        go.name = src.name;
        go.transform.SetPositionAndRotation(pos + Vector3.down * 0.1f, Quaternion.Euler(R(mixR, -3, 3), R(mixR, 0, 360), R(mixR, -3, 3)));
        float sx = mixR.NextDouble() < 0.5 ? -1f : 1f;   // mirror half of them
        go.transform.localScale = new Vector3(sx * scale, scale * R(mixR, 0.9f, 1.1f), scale);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var mr in go.GetComponentsInChildren<Renderer>(true))
        {
            var ms = mr.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
                if (ms[i]) foreach (var sw in trees.swaps) if (sw && sw.name == "LSV_" + ms[i].name) { ms[i] = sw; break; }
            mr.sharedMaterials = ms;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        return go;
    }

    // Near-edge silhouettes (11-13 Kido, Hades II reference: very tall, almost black shapes along the camera-side edge
    // only): the same pine, its materials swapped for near-black, desaturated, rim-less copies, shared across rebuilds.
    static readonly Dictionary<Material, Material> darkMats = new Dictionary<Material, Material>();
    static void Silhouette(GameObject go)
    {
        if (!go) return;
        foreach (var mr in go.GetComponentsInChildren<Renderer>(true))
        {
            var ms = mr.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                var src = ms[i];
                if (!src) continue;
                if (!darkMats.TryGetValue(src, out var d) || !d)
                {
                    d = new Material(src) { name = src.name + "_Silhouette", hideFlags = HideFlags.DontSave };
                    if (d.HasProperty("_Tint")) d.SetColor("_Tint", new Color(0.075f, 0.08f, 0.085f, 1f));
                    if (d.HasProperty("_Desat")) d.SetFloat("_Desat", 1f);
                    if (d.HasProperty("_RimScale")) d.SetFloat("_RimScale", 0f);
                    if (d.HasProperty("_Smoothness")) d.SetFloat("_Smoothness", 0f);
                    darkMats[src] = d;
                }
                ms[i] = d;
            }
            mr.sharedMaterials = ms;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // their long shadows would fall across the arena
        }
    }

    // 11-13 (Kido, all fields: the fight space lacks big shapes that give depth; use existing/downloaded assets; never small
    // things you trip on): tall downloaded pines just outside the near (south) edge whose crowns hang into the frame, tall
    // pines along the far edge, and big granite masses outside the east and west edges. All outside the arena: the pines
    // have no colliders, the boulders only their own big box.
    void BuildDepth(System.Random r)
    {
        if (trees && trees.heroes != null && trees.heroes.Length > 0)
        {
            float zS = ArenaCZ - ArenaHZ;
            foreach (var x in new[] { -50f, -31f, -14f, 4f, 22f, 40f, 52f })
            {
                float px = x + R(r, -4f, 4f), pz = zS - R(r, 3f, 6.5f);
                Nearest(px, pz, out float d, out float w, out _, out _, out _);
                if (d < w + 2f) continue;
                var go = Place(trees.heroes[r.Next(trees.heroes.Length)], new Vector3(px, Height(px, pz) - 0.2f, pz), R(r, 1.55f, 1.9f));
                Silhouette(go);
            }
        }
        if (trees && trees.pines != null && trees.pines.Length > 0)
        {
            float zN = ArenaCZ + ArenaHZ;
            for (float x = -58f; x < 50f; x += R(r, 9f, 15f))
            {
                float pz = zN + R(r, 2f, 5f);
                Nearest(x, pz, out float d, out float w, out _, out _, out _);
                if (d < w + 2f) continue;
                Place(trees.pines[r.Next(trees.pines.Length)], new Vector3(x, Height(x, pz) - 0.2f, pz), R(r, 1.05f, 1.35f));
            }
        }
        float xW = ArenaCX - ArenaHX, xE = ArenaCX + ArenaHX;
        foreach (var b in new[] { new Vector3(xW - 6f, 14f, 10f), new Vector3(xW - 5f, -12f, 8.5f), new Vector3(xE + 6f, 22f, 11f), new Vector3(xE + 5.5f, -24f, 9f) })
        {
            Nearest(b.x, b.y, out float d, out float w, out _, out _, out _);
            if (d < w + 3f) continue;
            Big(r, b.x, b.y, b.z, 0.15f);
        }
        FlushBatches(true);
    }

    void BuildProps()
    {
        rockSpots.Clear();
        mixR = new System.Random(1113);
        trees = Resources.Load<LookStudyValleyTrees>("ValleyTrees");
        heroesLeft = HeroCap;
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
            rockSpots.Add(new Vector3(b.x, b.y, b.z * 0.5f));
        }
        // Rounded cobbles along the shoreline, thicker near the water.
        for (int i = 0; i < 1500; i++)
        {
            float x = R(r, -64, 52), z = R(r, -49, 43);
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

        // A dead tree snagged in the lower run. (The fallen pine bridge over the gourd pool is gone, 11-13 Kido: a pine
        // trunk laid flat read as a fake flat board.)
        if (pineDead != null && pineDead.mesh)
        {
            Stamp(pineDead, new Vector3(-29, 0.2f, -22), Quaternion.Euler(80, 150, 12), new Vector3(0.8f, 0.8f, 0.8f), new Color(0.6f, 0.58f, 0.54f, 0));
            foreach (var d in new[] { new Vector3(-14, 16, 1f), new Vector3(29, -9, 1.1f), new Vector3(-32, -8, 0.9f), new Vector3(21, 40, 1.15f) })
                Stamp(pineDead, new Vector3(d.x, Height(d.x, d.y) - 0.3f, d.y), RandY(r, 6f), new Vector3(d.z, d.z, d.z), new Color(0.66f, 0.62f, 0.58f, 0));
        }

        // Boulders where the stream leaves the arena, so the edge there is closed too.
        foreach (var b in new[] { new Vector3(-63, -36, 5.0f), new Vector3(-64.5f, -50, 5.6f), new Vector3(-66, -31, 4.2f), new Vector3(-67, -55, 4.6f) })
            Big(r, b.x, b.y, b.z);
        FlushBatches(true);

        // Pine forest on both slopes, thick outside the arena so the middle stays open.
        if (pineTrunk != null && pineTrunk.mesh && pineBranches != null && pineBranches.mesh)
        {
            // the arena edge: rows of pines right outside it, trunks solid, gaps only over the water
            for (int pass = 0; pass < 3; pass++)
            {
                float off = 1.8f + pass * 3.3f, hx = ArenaHX + off, hz = ArenaHZ + off;
                float per = 4f * (hx + hz);
                for (float dd = R(r, 0, 2); dd < per; dd += R(r, 2.6f, 3.6f))
                {
                    float t = dd; Vector2 q;
                    if (t < 2 * hx) q = new Vector2(-hx + t, -hz);
                    else if ((t -= 2 * hx) < 2 * hz) q = new Vector2(hx, -hz + t);
                    else if ((t -= 2 * hz) < 2 * hx) q = new Vector2(hx - t, hz);
                    else { t -= 2 * hx; q = new Vector2(-hx, hz - t); }
                    float x = ArenaCX + q.x + R(r, -0.9f, 0.9f), z = ArenaCZ + q.y + R(r, -0.9f, 0.9f);
                    Nearest(x, z, out float d0, out float w0, out _, out _, out _);
                    float h0 = Height(x, z);
                    if (d0 < w0 + 1.5f || h0 < 0.15f) continue;
                    float s0 = R(r, 0.65f, 1.0f);
                    var rot0 = Quaternion.Euler(R(r, -4, 4), R(r, 0, 360), R(r, -4, 4));
                    var pos0 = new Vector3(x, h0 - 0.25f, z);
                    var tint0 = new Color(R(r, 0.34f, 0.5f), R(r, 0.46f, 0.62f), R(r, 0.46f, 0.6f), 1);
                    if (pass == 0) Col(pos0 + Vector3.up * 2f, new Vector3(0.8f * s0, 4f, 0.8f * s0), Quaternion.identity);
                    // irregular spacing: some slots stay empty, some get a downloaded pine instead
                    if (mixR.NextDouble() < 0.12) continue;
                    if (MixPine(pos0, pass == 0 ? 0.05 : 0.02, 0.4)) continue;
                    OwnPine(pos0, rot0, s0, tint0);
                }
                FlushBatches(true);
            }

            int placed = 0;
            for (int i = 0; i < 2400 && placed < 520; i++)
            {
                float x = R(r, -106, 88), z = R(r, -88, 82);
                Nearest(x, z, out float d, out float w, out _, out _, out _);
                float h = Height(x, z);
                if (d < w + 15f || h > 16f) continue;
                if (InArena(x, z, 9f)) continue;   // inside: open floor; the edge belt is placed above
                if (new Vector2((x + 19f) / 11f, (z - 7f) / 8f).magnitude < 1f) continue;
                float s = R(r, 0.55f, 0.95f);
                var rot = Quaternion.Euler(R(r, -4, 4), R(r, 0, 360), R(r, -4, 4));
                var tint = new Color(R(r, 0.34f, 0.5f), R(r, 0.46f, 0.62f), R(r, 0.46f, 0.6f), 1);
                var pos = new Vector3(x, h - 0.25f, z);
                Col(pos + Vector3.up * 2f, new Vector3(0.7f * s, 4f, 0.7f * s), Quaternion.identity);
                if (!MixPine(pos, 0.0, 0.3)) OwnPine(pos, rot, s, tint);
                if (++placed % 130 == 0) FlushBatches(true);
            }
        }
        FlushBatches(true);

        BuildPoolFall();
        BuildDepth(new System.Random(1314));
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
        for (float cz = -56; cz < 50; cz += Chunk)
        for (float cx = -72; cx < 60; cx += Chunk)
        {
            for (int i = 0; i < 1500; i++)
            {
                float x = cx + (float)r.NextDouble() * Chunk, z = cz + (float)r.NextDouble() * Chunk;
                float dens = GrassDensity(x, z);
                if (r.NextDouble() > dens) continue;
                if (OnRock(x, z)) continue;
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
            foreach (var p in new[] { new Vector3(27, 0.5f, 26), new Vector3(1, 0.4f, -2), new Vector3(-20, 0.4f, -18), new Vector3(-48, 0.4f, -34) })
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
            var motes = Particle(root, "Motes", moteMat, 450, 4f, 9f, 0.04f, 0.09f, Vector3.up * 0.1f, 0.25f, 48f, true);
            motes.transform.position = new Vector3(ArenaCX, 3, ArenaCZ);
            var sh = motes.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(112, 5, 88);
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
