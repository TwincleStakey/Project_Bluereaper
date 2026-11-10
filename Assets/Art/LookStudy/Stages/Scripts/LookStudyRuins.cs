// Burning stages built from the Blender kit: the palace court in front of the main hall, and the thatched village.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyRuins : MonoBehaviour
{
    public enum Kind { Palace, Village }
    public Kind kind;
    [System.Serializable] public class Part { public string name; public Mesh mesh; public Material material; }
    public Part[] parts;
    public Mesh[] rocks; public Material rockMat;
    public Mesh[] grass; public Material grassMat;
    public Mesh pineBranch, pineTrunk, pineDead; public Material pineBranchMat, pineTrunkMat, pineDeadMat;
    public Mesh jangseung; public Material jangseungMat;
    public Material ground, flameMat, smokeMat, emberMat, wispMat;

    Transform root;
    readonly Dictionary<string, Part> byName = new Dictionary<string, Part>();
    readonly List<(Light l, float i, float seed)> flicker = new List<(Light, float, float)>();

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
        float t = Application.isPlaying ? Time.time : 12f;
        if (Application.isPlaying) Shader.SetGlobalFloat("_LSTime", t);
        foreach (var (l, i, s) in flicker)
            if (l) l.intensity = i * (0.78f + 0.22f * Mathf.PerlinNoise(t * 6f, s) + 0.1f * Mathf.Sin(t * 13f + s));
    }

    public void Clear()
    {
        if (!root) return;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) DestroyImmediate(mf.sharedMesh);
        DestroyImmediate(root.gameObject);
        flicker.Clear();
    }

    public void Build()
    {
        if (!ground || parts == null || parts.Length == 0) return;
        Clear();
        root = new GameObject("RuinsGenerated").transform;
        root.SetParent(transform, false);
        root.gameObject.hideFlags = HideFlags.DontSave;
        byName.Clear();
        foreach (var p in parts) if (p != null) byName[p.name] = p;
        if (kind == Kind.Palace) BuildPalace(); else BuildVillage();
    }

    // ---------- shared

    static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
    static float N(float x, float y) => Mathf.PerlinNoise(x + 83.1f, y + 11.7f);
    static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    class Batch
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector4> tan = new List<Vector4>(), root = new List<Vector4>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> col = new List<Color>();
        public readonly List<int> tri = new List<int>();
    }
    class Source
    {
        public Vector3[] v, n; public Vector4[] tan; public Vector2[] uv; public int[] t;
        public Source(Mesh m) { v = m.vertices; n = m.normals; tan = m.tangents; uv = m.uv; t = m.triangles; }
    }
    readonly Dictionary<Material, Batch> batches = new Dictionary<Material, Batch>();
    readonly Dictionary<Mesh, Source> sources = new Dictionary<Mesh, Source>();

    void Stamp(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, Color tint)
    {
        if (!mesh || !mat) return;
        if (!sources.TryGetValue(mesh, out var s)) sources[mesh] = s = new Source(mesh);
        if (!batches.TryGetValue(mat, out var b)) batches[mat] = b = new Batch();
        int bv = b.v.Count;
        var rv = new Vector4(pos.x, pos.y, pos.z, Random.value);
        for (int i = 0; i < s.v.Length; i++)
        {
            var q = s.v[i]; q.x *= scale.x; q.y *= scale.y; q.z *= scale.z;
            b.v.Add(pos + rot * q);
            b.n.Add(rot * s.n[i]);
            var tn = s.tan.Length > i ? s.tan[i] : new Vector4(1, 0, 0, 1);
            var tv = rot * new Vector3(tn.x, tn.y, tn.z);
            b.tan.Add(new Vector4(tv.x, tv.y, tv.z, tn.w));
            b.uv.Add(s.uv.Length > i ? s.uv[i] : Vector2.zero);
            b.root.Add(rv);
            b.col.Add(tint);
        }
        for (int i = 0; i < s.t.Length; i++) b.tri.Add(bv + s.t[i]);
    }

    void K(string part, Vector3 pos, float yaw, float burn = 0f, float s = 1f, float tint = 1f, float tiltX = 0f, float tiltZ = 0f)
    {
        if (!byName.TryGetValue(part, out var p)) { Debug.LogWarning("[LS] no kit part " + part); return; }
        var rot = Quaternion.Euler(tiltX, yaw, tiltZ);
        Stamp(p.mesh, p.material, pos, rot, new Vector3(s, s, s), new Color(tint, tint, tint, burn));
        if (System.Array.IndexOf(Blocking, part) >= 0)
        {
            var b = p.mesh.bounds;
            Col(pos + rot * (b.center * s), b.size * s, rot);
        }
    }
    static readonly string[] Blocking = { "Choga_Base", "Choga_Wall", "Terrace_Stone", "Hall_Wall", "Gal_Wall", "Prop_Pot", "Prop_PotBroken", "Prop_Well", "Prop_Straw", "Prop_Cart", "Prop_Palanquin", "Prop_RankStone", "Prop_Basin", "Prop_Fence" };

    // Invisible blockers on the Default layer: they stop the player, enemies and bullets.
    void Col(Vector3 center, Vector3 size, Quaternion rot)
    {
        var go = new GameObject("Col"); go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(center, rot);
        go.AddComponent<BoxCollider>().size = size;
    }


    void Flush(bool shadows)
    {
        foreach (var kv in batches)
        {
            var b = kv.Value;
            if (b.v.Count == 0) continue;
            var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            m.SetVertices(b.v); m.SetNormals(b.n); m.SetTangents(b.tan); m.SetUVs(0, b.uv); m.SetUVs(1, b.root); m.SetColors(b.col); m.SetTriangles(b.tri, 0);
            m.RecalculateBounds();
            Add("P_" + kv.Key.name, m, kv.Key, shadows);
        }
        batches.Clear();
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

    delegate float HF(float x, float z);
    delegate Color CF(float x, float z, float h);

    void Ground(float x0, float x1, float z0, float z1, float step, HF hf, CF cf)
    {
        int nx = Mathf.CeilToInt((x1 - x0) / step) + 1, nz = Mathf.CeilToInt((z1 - z0) / step) + 1;
        var v = new Vector3[nx * nz]; var c = new Color[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            float x = x0 + i * step, z = z0 + j * step, h = hf(x, z);
            v[j * nx + i] = new Vector3(x, h, z); c[j * nx + i] = cf(x, z, h);
        }
        var t = new int[(nx - 1) * (nz - 1) * 6]; int k = 0;
        for (int j = 0; j < nz - 1; j++)
        for (int i = 0; i < nx - 1; i++)
        {
            int a = j * nx + i, b = a + 1, cc = a + nx, d = cc + 1;
            t[k++] = a; t[k++] = cc; t[k++] = b; t[k++] = b; t[k++] = cc; t[k++] = d;
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
        m.vertices = v; m.colors = c; m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
        Add("Ground", m, ground, true).gameObject.AddComponent<MeshCollider>().sharedMesh = m;
    }

    void RockAt(System.Random r, Vector3 p, float size, float burn = 0f)
    {
        if (rocks == null || rocks.Length == 0) return;
        var mesh = rocks[r.Next(rocks.Length)];
        float s = size / Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z);
        Stamp(mesh, rockMat, p - Vector3.up * mesh.bounds.size.y * s * 0.2f, Quaternion.Euler(R(r, -14, 14), R(r, 0, 360), R(r, -14, 14)), new Vector3(s, s * R(r, 0.7f, 1f), s), new Color(1, 1, 1, burn));
        if (size > 0.5f) Col(p + Vector3.up * 0.45f, new Vector3(size * 0.8f, 0.9f, size * 0.8f), Quaternion.Euler(0, R(r, 0, 360), 0));
    }

    // ---------- fire

    void Fire(Vector3 pos, float size, float light, float smoke = 1f, bool warmLight = true)
    {
        var anchor = new GameObject("Fire"); anchor.hideFlags = HideFlags.DontSave;
        anchor.transform.SetParent(root, false); anchor.transform.position = pos;
        if (light > 0f)
        {
            var l = anchor.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.46f, 0.16f); l.range = 7f + size * 7f; l.intensity = light; l.shadows = LightShadows.None;
            flicker.Add((l, light, pos.x * 0.37f + pos.z));
        }
        if (flameMat)
        {
            var f = LookStudyValley.Particle(anchor.transform, "Flame", flameMat, 220, 0.4f, 0.95f, 0.32f * size, 0.95f * size, Vector3.up * (3.2f * Mathf.Sqrt(size)), 0.9f, 120f, true);
            var fr = f.GetComponent<ParticleSystemRenderer>(); fr.renderMode = ParticleSystemRenderMode.Stretch; fr.lengthScale = 1.6f; fr.velocityScale = 0.28f;
            var sh = f.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.8f * size, 0.5f, 1.8f * size);
            var c = f.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.95f, 0.62f, 0.26f), 0), new GradientColorKey(new Color(0.85f, 0.28f, 0.07f), 0.45f), new GradientColorKey(new Color(0.45f, 0.07f, 0.02f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.42f, 0.14f), new GradientAlphaKey(0.2f, 0.6f), new GradientAlphaKey(0, 1) });
            c.color = g;
            var so = f.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0.3f));
            f.Play();
        }
        if (smokeMat && smoke > 0f)
        {
            var s = LookStudyValley.Particle(anchor.transform, "Smoke", smokeMat, 50, 4f, 8f, 2.2f * size, 5.5f * size, new Vector3(-0.35f, 1.5f, 0.25f) * Mathf.Sqrt(size), 0.35f, 5f * smoke, true);
            s.transform.localPosition = Vector3.up * size;
            var c = s.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.34f, 0.22f, 0.17f), 0), new GradientColorKey(new Color(0.11f, 0.1f, 0.11f), 0.5f), new GradientColorKey(new Color(0.12f, 0.12f, 0.13f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.62f, 0.2f), new GradientAlphaKey(0, 1) });
            c.color = g;
            var so = s.sizeOverLifetime; so.enabled = true; so.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.55f, 1, 1.6f));
            s.Play();
        }
        if (emberMat)
        {
            var e = LookStudyValley.Particle(anchor.transform, "Embers", emberMat, 60, 1.6f, 3.6f, 0.05f, 0.11f, new Vector3(-0.5f, 2.1f, -0.7f), 0.9f, 12f * size, true);
            var sh = e.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2f * size, 1f, 2f * size);
            var c = e.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.72f, 0.3f), 0), new GradientColorKey(new Color(1f, 0.3f, 0.08f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0, 1) });
            c.color = g;
            e.Play();
        }
    }

    void Wisp(Vector3 pos)
    {
        var go = new GameObject("Wisp", typeof(Light)); go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(root, false); go.transform.position = pos;
        var l = go.GetComponent<Light>();
        l.type = LightType.Point; l.color = new Color(0.25f, 0.82f, 0.75f); l.range = 9f; l.intensity = 8f; l.shadows = LightShadows.None;
        flicker.Add((l, 8f, pos.x + pos.z * 0.5f));
        if (wispMat)
        {
            var p = LookStudyValley.Particle(go.transform, "Flame", wispMat, 40, 0.45f, 0.8f, 0.28f, 0.6f, Vector3.up * 0.75f, 0.12f, 20f, true);
            var c = p.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.5f, 0.95f, 0.88f), 0), new GradientColorKey(new Color(0.25f, 0.82f, 0.75f), 0.45f), new GradientColorKey(new Color(0.03f, 0.22f, 0.26f), 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0, 1) });
            c.color = g;
            p.Play();
        }
    }

    void AshFall(Vector3 center, Vector3 box, float rate)
    {
        if (!emberMat) return;
        var a = LookStudyValley.Particle(root, "Ash", emberMat, Mathf.Max(240, (int)(rate * 11f)), 5f, 10f, 0.03f, 0.07f, new Vector3(0.25f, -0.32f, -0.45f), 0.3f, rate, true);
        a.transform.position = center;
        var sh = a.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
        var c = a.colorOverLifetime; c.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.52f, 0.48f, 0.44f), 0), new GradientColorKey(new Color(0.42f, 0.38f, 0.34f), 1) },
                  new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.7f, 0.2f), new GradientAlphaKey(0, 1) });
        c.color = g;
        a.Play();
    }

    // ---------- palace court (11-10: 108 x 95, was 54 x 63; hall to the north)
    // Old layout points spread 2x across and 1.5x along the court (PX, PZ); the buildings and props keep their size,
    // and the gallery rows get longer with the same spacing.
    const float PalaceSX = 2f, PalaceSZ = 1.5f;
    static float PX(float x) => x * PalaceSX;
    static float PZ(float z) => z * PalaceSZ;
    public const float PalaceGalX = 54f, PalaceSouthZ = -49.5f, PalaceNorthZ = 45.75f;

    public static float PalaceH(float x, float z)
    {
        float road = (1f - SS(2.6f, 3.9f, Mathf.Abs(x))) * (1f - SS(42f, 45f, z)) * SS(-51f, -48f, z) * 0.13f;
        return road + (N(x * 0.3f, z * 0.3f) - 0.5f) * 0.05f;
    }

    void BuildPalace()
    {
        var r = new System.Random(77); Random.InitState(77);
        Ground(-110, 110, -85, 100, 0.5f, PalaceH, (x, z, h) =>
        {
            float ash = N(x * 0.07f + 2, z * 0.07f + 5);
            float nearFire = Mathf.Max(1f - SS(6f, 20f, new Vector2(x, z - 45f).magnitude), 1f - SS(10f, 18f, 59f - Mathf.Abs(x)) * 1f);
            // court paving fades into the trampled dirt outside over a few metres, broken up by noise (no hard cut line)
            float outD = Mathf.Max(Mathf.Abs(x) - PalaceGalX - 2f, PalaceSouthZ - 2f - z) + (N(x * 0.35f + 9f, z * 0.35f) - 0.5f) * 2.4f;
            float dirt = SS(-1.2f, 1.6f, outD);
            return new Color(0, dirt, 1f - dirt, Mathf.Clamp01(SS(0.52f, 0.72f, ash) * 0.75f + nearFire * 0.5f));
        });

        // Terrace and the main hall: stonework survives, the upper roof is burning open.
        var hall = new Vector3(0, 0, PZ(38));
        K("Terrace_Stone", hall, 0, 0.18f); K("Terrace_Rail", hall, 0, 0.25f);
        var top = hall + Vector3.up * 1.6f;
        K("Hall_Wall", top, 0, 0.7f); K("Hall_Pillar", top, 0, 0.72f); K("Hall_Bracket", top, 0, 0.72f);
        K("Hall_RoofLower", top, 0, 0.42f); K("Hall_RoofUpperHole", top, 0, 0.58f);

        // Rank stones in two rows, some toppled.
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 17; i++)
        {
            float z = -36f + i * 4.7f, x = side * 12f;
            var c = r.NextDouble();
            if (c < 0.18) K("Prop_RankStone", new Vector3(x + R(r, -.4f, .4f), 0.12f, z + R(r, -.6f, .6f)), R(r, 0, 360), 0.3f, 1f, 1f, 86f);
            else K("Prop_RankStone", new Vector3(x, PalaceH(x, z), z), R(r, -6, 6), c < 0.5 ? 0.45f : 0.1f, 1.05f);
        }

        // Galleries around the court: whole, scorched, burning, or only charred frames.
        void Gallery(float x, float z, float yaw, int state, bool low)
        {
            var p = new Vector3(x, 0.02f, z);
            if (!low) { K("Gal_Plinth", p, yaw, 0.2f); }
            else K("Gal_Plinth", p, yaw, 0.35f);
            if (low) { if (r.NextDouble() < 0.5) K("Prop_TileHeap", p + Vector3.up * 0.2f, R(r, 0, 360), 0.2f, 1.1f); return; }
            if (state == 0) { K("Gal_Post", p, yaw, 0.15f); K("Gal_Wall", p, yaw, 0.2f); K("Gal_Roof", p, yaw, 0.12f); }
            else if (state == 1) { K("Gal_Post", p, yaw, 0.75f); K("Gal_Wall", p, yaw, 0.65f); K("Gal_Roof", p, yaw, 0.68f); }
            else if (state == 2) { K("Gal_Rafters", p, yaw, 0.95f); K("Gal_Wall", p - Vector3.up * 0.9f, yaw, 0.92f); K("Prop_TileHeap", p + Vector3.up * 0.2f, R(r, 0, 360), 0.3f, 1.2f); }
            else { K("Prop_TileHeap", p + Vector3.up * 0.2f, R(r, 0, 360), 0.3f, 1.3f); K("Prop_Beam", p + new Vector3(0.4f, 0.3f, 0.2f), R(r, 0, 360), 0.95f); }
        }
        var fires = new List<Vector3>();
        for (int i = 0; i < 30; i++)
        {
            float z = -46.55f + i * 3.05f;     // runs right down to the south row, no gap at the corners
            if (Mathf.Abs(z - 6f) < 3f) continue;       // side gates (spawn chokepoints)
            foreach (var x in new[] { -PalaceGalX, PalaceGalX })
            {
                int st = PickState(r, x * 0.13f + z);
                Gallery(x, z, x < 0 ? 90 : -90, st, false);
                if (st == 1 && r.NextDouble() < 0.55) fires.Add(new Vector3(x, 2.4f, z));
            }
        }
        for (int i = 0; i < 36; i++)
        {
            float x = -52.5f + i * 3.05f;
            if (Mathf.Abs(x) < 3.5f) continue;  // south gate
            Gallery(x, PalaceSouthZ, 180, 2, true);     // kept low so it never hides the player
        }
        // north galleries from the corners in toward the hall terrace
        for (int i = 0; i < 18; i++) { float x = i < 9 ? -53f + i * 3.05f : 53f - (i - 9) * 3.05f; Gallery(x, PalaceNorthZ, 0, PickState(r, i), false); }
        // corner posts close the gaps where the rows meet
        foreach (var cx in new[] { -PalaceGalX, PalaceGalX }) { Gallery(cx, PalaceSouthZ, 180, 2, true); Gallery(cx, PalaceNorthZ, 0, 0, false); }

        // Debris and barricades: dense at the edges, the centre stays clear for bullets.
        K("Prop_Palanquin", new Vector3(PX(-11.5f), 0.05f, PZ(-4)), 28, 0.72f, 1.1f, 1f, 0, 13f);
        K("Prop_Palanquin", new Vector3(PX(13), 0.05f, PZ(14)), 200, 0.95f, 1f, 1f, 7f, -10f);
        K("Prop_Palanquin", new Vector3(-38f, 0.05f, -34f), 120, 0.8f, 1f, 1f, 5f, 9f);
        K("Prop_Cart", new Vector3(PX(12.5f), 0.1f, PZ(-12)), 62, 0.5f, 1f, 1f, 6f, 10f);
        K("Prop_Cart", new Vector3(-40f, 0.1f, 20f), 150, 0.6f, 1f, 1f, -5f, 8f);
        for (int b = 0; b < 6; b++)
        {
            var c = new Vector3(new[] { PX(-15f), PX(16f), PX(-17f), 40f, -42f, 36f }[b], 0, new[] { PZ(12f), PZ(3f), PZ(-20f), -36f, -8f, 32f }[b]);
            float yaw = R(r, 0, 180);
            var d = Quaternion.Euler(0, yaw, 0);
            for (int k = -1; k <= 1; k++) K("Prop_Fence", c + d * new Vector3(k * 3.4f, 0, 0), yaw + R(r, -10, 10), 0.45f, 1.1f, 1f, R(r, -10, 10), R(r, -8, 8));
        }
        foreach (var f in new[] { new Vector3(PX(-20f), PZ(-9f), 30f), new Vector3(PX(18f), PZ(22f), 150f), new Vector3(PX(-5f), PZ(22f), 280f), new Vector3(PX(21f), PZ(-16f), 80f),
                                  new Vector3(-24f, -40f, 200f), new Vector3(46f, 6f, 320f), new Vector3(-46f, 30f, 110f) })
        {
            var p = new Vector3(f.x, 0.05f, f.y);
            K("Prop_BannerPole", p, f.z, 0.5f, 1f, 1f, 84f);
            K("Prop_BannerCloth", p + new Vector3(0.4f, 0.03f, 0.3f), f.z + 90f, 0.2f, 1.6f, 1f);
        }
        K("Prop_Basin", new Vector3(PX(-18), 0, PZ(24)), 0, 0.15f, 1.1f);
        K("Prop_Basin", new Vector3(PX(19), 0.5f, PZ(24)), 30, 0.2f, 1.1f, 1f, 110f);
        // debris: dense toward the galleries, the middle stays clear for bullets
        for (int i = 0; i < 104; i++)
        {
            float x = R(r, -51, 51), z = R(r, -46, 43);
            if (Mathf.Abs(x) < 28f && z < 30f) continue;
            if (r.NextDouble() < 0.5) K("Prop_TileHeap", new Vector3(x, 0.05f, z), R(r, 0, 360), 0.25f, R(r, 0.8f, 1.5f));
            else K("Prop_Beam", new Vector3(x, 0.08f, z), R(r, 0, 360), 0.95f, R(r, 0.8f, 1.6f));
        }
        // Beyond the galleries: a broken outer palace wall, charred pines and rubble, so the edge does not open onto bare dirt.
        foreach (var side in new[] { -1f, 1f })
            for (float z = PalaceSouthZ - 6f; z < PalaceNorthZ + 10f; z += 3.05f)
            {
                if (r.NextDouble() < 0.18) { K("Prop_TileHeap", new Vector3(side * (PalaceGalX + 9f), 0.05f, z), R(r, 0, 360), 0.3f, R(r, 1f, 1.4f)); continue; }
                { var q = new Vector3(side * (PalaceGalX + 9f), 0.02f, z); float yw = side < 0 ? 90 : -90; K("Gal_Plinth", q, yw, 0.4f); K("Gal_Wall", q, yw, R(r, 0.72f, 0.95f), 1f, 0.8f); }
            }
        for (float x = -PalaceGalX - 9f; x <= PalaceGalX + 9f; x += 3.05f)
        {
            if (Mathf.Abs(x) < 4f) continue;   // the south gate's road
            if (r.NextDouble() < 0.18) { K("Prop_TileHeap", new Vector3(x, 0.05f, PalaceSouthZ - 9f), R(r, 0, 360), 0.3f, R(r, 1f, 1.4f)); continue; }
            { var q = new Vector3(x, 0.02f, PalaceSouthZ - 9f); K("Gal_Plinth", q, 180, 0.4f); K("Gal_Wall", q, 180, R(r, 0.72f, 0.95f), 1f, 0.8f); }
        }
        for (int i = 0; i < 90; i++)
        {
            float x = R(r, -96, 96), z = R(r, -78, 90);
            if (Mathf.Abs(x) < PalaceGalX + 4f && z > PalaceSouthZ - 4f) continue;
            if (Mathf.Abs(x) < 5f && z < PalaceSouthZ) continue;
            if (r.NextDouble() < 0.5) K("Prop_TileHeap", new Vector3(x, 0.05f, z), R(r, 0, 360), 0.35f, R(r, 0.9f, 1.6f));
            else K("Prop_Beam", new Vector3(x, 0.08f, z), R(r, 0, 360), 0.95f, R(r, 0.8f, 1.6f));
        }
        Flush(true);
        if (pineDead)
        {
            for (int i = 0; i < 70; i++)
            {
                float x = R(r, -100, 100), z = R(r, -82, 95);
                if (Mathf.Abs(x) < PalaceGalX + 12f && z > PalaceSouthZ - 12f) continue;
                if (Mathf.Abs(x) < 6f && z < PalaceSouthZ) continue;
                float s = R(r, 0.8f, 1.3f);
                Stamp(pineDead, pineDeadMat, new Vector3(x, PalaceH(x, z) - 0.3f, z), Quaternion.Euler(R(r, -5, 5), R(r, 0, 360), R(r, -5, 5)), new Vector3(s, s, s), new Color(0.4f, 0.36f, 0.34f, 1));
            }
            Flush(true);
        }

        Fire(hall + new Vector3(-1.5f, 9.4f, 0.3f), 2.8f, 70f, 2.6f);
        Fire(hall + new Vector3(-6.5f, 7f, -3.8f), 1.9f, 42f, 1.4f);
        Fire(hall + new Vector3(6.5f, 7.2f, -3f), 1.6f, 36f, 1.2f);
        Fire(hall + new Vector3(0, 3.5f, -4.8f), 1.2f, 30f, 0.5f);
        int nf = 0;
        foreach (var f in fires) { if (nf++ > 13) break; Fire(f, 1.0f, 20f, 0.7f); }
        Fire(new Vector3(PX(-11.5f), 0.9f, PZ(-4)), 0.6f, 12f, 0.5f);
        Fire(new Vector3(PX(13), 0.7f, PZ(14)), 0.7f, 12f, 0.5f);
        Fire(new Vector3(-38f, 0.8f, -34f), 0.6f, 12f, 0.5f);
        foreach (var w in new[] { new Vector3(PX(-6), 1.3f, PZ(-18)), new Vector3(PX(7), 1.3f, PZ(6)), new Vector3(PX(-3.5f), 1.3f, PZ(15)), new Vector3(34, 1.3f, -30), new Vector3(-30, 1.3f, 10) }) Wisp(w);
        AshFall(new Vector3(0, 18, 0), new Vector3(140, 6, 120), 60);
    }

    static int PickState(System.Random r, float seed)
    {
        double v = r.NextDouble();
        return v < 0.26 ? 0 : v < 0.55 ? 1 : v < 0.85 ? 2 : 3;
    }

    // ---------- thatched village (threshing yard in the centre). 11-10: a 112 x 88 arena (x +-56, z -48..40), closed by
    // a belt of pines with low stone walls in front; the old house ring stays and more houses fill the wider sides.
    public const float VillageHalfX = 56f, VillageMinZ = -48f, VillageMaxZ = 40f;
    public static bool InVillageArena(float x, float z, float margin = 0f) =>
        Mathf.Abs(x) < VillageHalfX + margin && z > VillageMinZ - margin && z < VillageMaxZ + margin;

    static readonly Vector2[] Lanes = { new Vector2(0, -32), new Vector2(22, 24), new Vector2(-24, 22), new Vector2(30, -8), new Vector2(-31, -6) };

    public static float VillageH(float x, float z)
    {
        float north = SS(16f, 42f, z) * 5.2f;
        return north + (N(x * 0.09f, z * 0.09f) - 0.5f) * 0.8f * SS(7f, 18f, new Vector2(x, z).magnitude) + (N(x * 0.04f, z * 0.04f) - 0.5f) * 1.2f * SS(26f, 52f, new Vector2(x, z).magnitude) * 3f;
    }

    static float LaneDist(float x, float z)
    {
        float best = 1e9f; var p = new Vector2(x + (N(x * 0.1f, z * 0.1f) - 0.5f) * 4f, z + (N(z * 0.1f, x * 0.1f) - 0.5f) * 4f);
        foreach (var e in Lanes)
        {
            float t = Mathf.Clamp01(Vector2.Dot(p, e) / e.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, e * t));
        }
        return best;
    }

    void House(System.Random r, Vector3 p, float yaw, int state, float s = 1f)
    {
        // state 0 intact old thatch, 1 scorched with a hole, 2 burning, 3 only rafters.
        float t = R(r, 0.82f, 1.08f);
        K("Choga_Base", p, yaw, state >= 2 ? 0.35f : 0.05f, s);
        K("Choga_Wall", p, yaw, state == 3 ? 0.9f : state == 2 ? 0.55f : 0.12f, s, t);
        K("Choga_Wood", p, yaw, state >= 2 ? 0.95f : 0.2f, s);
        if (state == 0) { K("Choga_Roof", p, yaw, 0.08f, s, t); K("Choga_Ridge", p, yaw, 0.1f, s, t); }
        else if (state == 1) { K("Choga_RoofHole", p, yaw, 0.55f, s, t * 0.8f); K("Choga_Rafters", p, yaw, 0.85f, s); }
        else if (state == 2) { K("Choga_RoofHole", p, yaw, 0.8f, s, t * 0.65f); K("Choga_Rafters", p, yaw, 0.95f, s); }
        else { K("Choga_Rafters", p, yaw, 1f, s); }
    }

    void BuildVillage()
    {
        var r = new System.Random(31); Random.InitState(31);
        Ground(-104, 104, -96, 92, 0.5f, VillageH, (x, z, h) =>
        {
            float yard = 1f - SS(8f, 11.5f, new Vector2(x, z).magnitude + (N(x * 0.2f, z * 0.2f) - 0.5f) * 3f);
            float lane = 1f - SS(1.9f, 3.6f, LaneDist(x, z));
            float dirt = Mathf.Max(yard, lane);
            float ash = N(x * 0.11f + 3, z * 0.11f + 7);
            return new Color(dirt, 1f - dirt, 0, 1f - dirt * 0.9f + SS(0.58f, 0.72f, ash) * 0.2f);
        });

        // Houses keep to the edges and the north hill, facing the yard.
        var layout = new[]
        {
            new Vector4(-18, -16, 40, 2), new Vector4(-25, -3, 75, 0), new Vector4(-21, 11, 112, 1), new Vector4(-10, 22, 150, 0),
            new Vector4(10, 21, 208, 2), new Vector4(21, 10, 248, 0), new Vector4(26, -6, 285, 3), new Vector4(18, -18, 322, 0),
            new Vector4(-35, 19, 110, 3), new Vector4(-24, 32, 140, 0), new Vector4(0, 34, 180, 1), new Vector4(25, 31, 214, 2),
            new Vector4(37, 16, 240, 0), new Vector4(-38, -16, 65, 1), new Vector4(37, -22, 300, 3),
            // the wider arena: more houses out on the east and west sides and in the south corners, facing in
            new Vector4(-47, 2, 90, 0), new Vector4(48, -6, 270, 2), new Vector4(-46, -34, 50, 3), new Vector4(46, -36, 310, 1),
            new Vector4(-22, -38, 20, 1), new Vector4(24, -39, 340, 0), new Vector4(-49, 26, 120, 1), new Vector4(49, 28, 235, 3),
        };
        var fires = new List<Vector3>();
        foreach (var h in layout)
        {
            var p = new Vector3(h.x, VillageH(h.x, h.y) - 0.1f, h.y);
            House(r, p, h.z, (int)h.w, R(r, 0.92f, 1.12f));
            if ((int)h.w == 2) fires.Add(p + new Vector3(0.4f, 3.5f, -0.3f));
            if ((int)h.w == 1 && r.NextDouble() < 0.5) fires.Add(p + new Vector3(-0.8f, 3.2f, 0.2f));
            // Low stone walls around the plot, broken where the lane runs through.
            var rot = Quaternion.Euler(0, h.z, 0);
            for (int side = 0; side < 3; side++)
            {
                var a = rot * (side == 0 ? new Vector3(-7, 0, 6) : side == 1 ? new Vector3(-7, 0, -5) : new Vector3(7, 0, -5));
                var b = rot * (side == 0 ? new Vector3(-7, 0, -5) : side == 1 ? new Vector3(-1.5f, 0, -5) : new Vector3(7, 0, 6));
                for (float t = 0; t <= 1f; t += 0.075f)
                {
                    if (r.NextDouble() < 0.14) continue;
                    var q = p + Vector3.Lerp(a, b, t);
                    q.y = VillageH(q.x, q.z);
                    if (LaneDist(q.x, q.z) < 2.6f || new Vector2(q.x, q.z).magnitude < 12f) continue;
                    RockAt(r, q + Vector3.up * 0.05f, R(r, 0.55f, 0.95f));
                    if (r.NextDouble() < 0.7) RockAt(r, q + Vector3.up * 0.42f, R(r, 0.42f, 0.7f));
                }
            }
            // Jar stand beside the house.
            if (r.NextDouble() < 0.6)
            {
                var j = p + rot * new Vector3(R(r, -4.5f, -3f), 0, R(r, 2.8f, 3.8f));
                for (int k = 0; k < r.Next(4, 9); k++)
                {
                    var q = j + new Vector3(R(r, -1.1f, 1.1f), 0, R(r, -0.9f, 0.9f));
                    q.y = VillageH(q.x, q.z);
                    float s = R(r, 0.72f, 1.1f);
                    if (r.NextDouble() < 0.25) K("Prop_PotBroken", q, R(r, 0, 360), 0.15f, s);
                    else { K("Prop_Pot", q, R(r, 0, 360), 0.05f, s); K("Prop_PotLid", q + Vector3.up * 0.85f * s, 0, 0f, s); }
                }
            }
        }
        // Yard furniture stays sparse.
        K("Prop_Well", new Vector3(-6.5f, VillageH(-6.5f, 5), 5), 0, 0.05f, 1.05f);
        foreach (var s in new[] { new Vector3(7, -7, 1.1f), new Vector3(10, -4, 0.9f), new Vector3(-9, -7, 1f), new Vector3(13, 7, 1f), new Vector3(-14, 2, 0.8f) })
            K("Prop_Straw", new Vector3(s.x, VillageH(s.x, s.y) - 0.05f, s.y), R(r, 0, 360), 0.14f, s.z, 0.92f);
        K("Prop_Cart", new Vector3(4.5f, 0.1f, 7), 200, 0.35f, 1f, 0.9f, 7f, 11f);
        for (int i = 0; i < 26; i++)
        {
            float a = R(r, 0, 6.28f), d = R(r, 12, 46);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); p.y = VillageH(p.x, p.z);
            if (LaneDist(p.x, p.z) < 3.2f || !InVillageArena(p.x, p.z, -3f)) continue;
            K("Prop_Fence", p, R(r, 0, 360), R(r, 0.1f, 0.7f), 1f, 0.95f, R(r, -14, 14), R(r, -12, 12));
        }
        for (int i = 0; i < 60; i++)
        {
            float a = R(r, 0, 6.28f), d = R(r, 7, 48);
            var p = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d); p.y = VillageH(p.x, p.z) + 0.06f;
            if (!InVillageArena(p.x, p.z, -2f)) continue;
            K(r.NextDouble() < 0.5 ? "Prop_Beam" : "Prop_Plank", p, R(r, 0, 360), 0.85f, R(r, 0.8f, 1.5f));
        }
        Flush(true);

        // Guardian poles at the south entrance.
        if (jangseung)
        {
            float s = 1f;
            Stamp(jangseung, jangseungMat, new Vector3(-3.8f, VillageH(-3.8f, -27) - 0.15f, -27), Quaternion.Euler(0, 160, 0), new Vector3(s, s, s), new Color(0.85f, 0.85f, 0.85f, 0.1f));
            Stamp(jangseung, jangseungMat, new Vector3(3.8f, VillageH(3.8f, -27) - 0.15f, -27), Quaternion.Euler(4, 200, -7), new Vector3(s, s, s), new Color(0.85f, 0.85f, 0.85f, 0.2f));
            Flush(true);
        }
        // The arena edge: a low broken stone wall just inside the pine belt, gaps where the lanes leave.
        {
            void WallRun(Vector2 a, Vector2 b)
            {
                float L = Vector2.Distance(a, b);
                for (float t = 0; t <= L; t += 0.75f)
                {
                    var q2 = Vector2.Lerp(a, b, t / L);
                    if (LaneDist(q2.x, q2.y) < 2.8f || r.NextDouble() < 0.1) continue;
                    var q = new Vector3(q2.x, VillageH(q2.x, q2.y), q2.y);
                    RockAt(r, q + Vector3.up * 0.05f, R(r, 0.6f, 1.0f));
                    if (r.NextDouble() < 0.75) RockAt(r, q + Vector3.up * 0.45f, R(r, 0.45f, 0.75f));
                }
            }
            float wx = VillageHalfX + 1.2f, wz0 = VillageMinZ - 1.2f, wz1 = VillageMaxZ + 1.2f;
            WallRun(new Vector2(-wx, wz0), new Vector2(wx, wz0));
            WallRun(new Vector2(wx, wz0), new Vector2(wx, wz1));
            WallRun(new Vector2(wx, wz1), new Vector2(-wx, wz1));
            WallRun(new Vector2(-wx, wz1), new Vector2(-wx, wz0));
            Flush(true);
        }
        // Pine belt right behind the wall (with trunk colliders), then dead and living pines massed outside.
        for (int pass = 0; pass < 3; pass++)
        {
            float off = 3.2f + pass * 3.4f, hx = VillageHalfX + off, z0 = VillageMinZ - off, z1 = VillageMaxZ + off;
            float per = 2f * (2f * hx + (z1 - z0));
            for (float d = R(r, 0, 2); d < per; d += R(r, 2.6f, 3.6f))
            {
                float t = d; Vector2 q;
                if (t < 2 * hx) q = new Vector2(-hx + t, z0);
                else if ((t -= 2 * hx) < z1 - z0) q = new Vector2(hx, z0 + t);
                else if ((t -= z1 - z0) < 2 * hx) q = new Vector2(hx - t, z1);
                else { t -= 2 * hx; q = new Vector2(-hx, z1 - t); }
                float x = q.x + R(r, -1f, 1f), z = q.y + R(r, -1f, 1f);
                if (LaneDist(x, z) < 2.2f && pass == 0) continue;   // lanes run out between the trees
                float y = VillageH(x, z) - 0.25f, s = R(r, 0.75f, 1.2f);
                var rot = Quaternion.Euler(R(r, -3, 3), R(r, 0, 360), R(r, -3, 3));
                Stamp(pineTrunk, pineTrunkMat, new Vector3(x, y, z), rot, new Vector3(s, s, s), new Color(0.42f, 0.36f, 0.34f, 1));
                Stamp(pineBranch, pineBranchMat, new Vector3(x, y, z), rot, new Vector3(s, s, s), new Color(R(r, 0.3f, 0.42f), R(r, 0.38f, 0.5f), R(r, 0.38f, 0.48f), 1));
                if (pass == 0) Col(new Vector3(x, y + 2f, z), new Vector3(0.8f * s, 4f, 0.8f * s), Quaternion.identity);
            }
            Flush(true);
        }
        for (int i = 0; i < 900; i++)
        {
            float x = R(r, -100, 100), z = R(r, -92, 88);
            if (InVillageArena(x, z, 12f)) continue;
            float y = VillageH(x, z) - 0.25f, s = R(r, 0.65f, 1.15f);
            var rot = Quaternion.Euler(R(r, -3, 3), R(r, 0, 360), R(r, -3, 3));
            Stamp(pineTrunk, pineTrunkMat, new Vector3(x, y, z), rot, new Vector3(s, s, s), new Color(0.42f, 0.36f, 0.34f, 1));
            Stamp(pineBranch, pineBranchMat, new Vector3(x, y, z), rot, new Vector3(s, s, s), new Color(R(r, 0.3f, 0.42f), R(r, 0.38f, 0.5f), R(r, 0.38f, 0.48f), 1));
        }
        foreach (var d in new[] { new Vector3(-6, -19, 1.1f), new Vector3(31, 7, 1f), new Vector3(-31, 7, 0.9f), new Vector3(17, 33, 1f), new Vector3(-16, 12, 0.7f), new Vector3(42, -20, 1f), new Vector3(-40, -26, 1.1f), new Vector3(-36, 32, 0.9f) })
            Stamp(pineDead, pineDeadMat, new Vector3(d.x, VillageH(d.x, d.y) - 0.3f, d.y), Quaternion.Euler(R(r, -5, 5), R(r, 0, 360), R(r, -5, 5)), new Vector3(d.z, d.z, d.z), new Color(0.52f, 0.48f, 0.45f, 1));
        Flush(true);
        // Dead grass in tufts along walls and verges.
        if (grass != null && grass.Length > 0)
        {
            // 16 m chunks, one mesh each, so the camera culls what it cannot see (the field is 4x bigger now)
            const float GC = 16f;
            for (float cz = -56f; cz < 48f; cz += GC)
            for (float cx = -64f; cx < 64f; cx += GC)
            {
                for (int i = 0; i < 2900; i++)
                {
                    float x = cx + R(r, 0, GC), z = cz + R(r, 0, GC);
                    if (r.NextDouble() > VillageGrass(x, z)) continue;
                    float s = R(r, 0.8f, 1.7f);
                    var t = Color.Lerp(new Color(0.46f, 0.44f, 0.34f, 1), new Color(0.82f, 0.72f, 0.46f, 1), N(x * 0.3f, z * 0.3f));
                    Stamp(grass[r.Next(grass.Length)], grassMat, new Vector3(x, VillageH(x, z) - 0.03f, z), Quaternion.Euler(R(r, -10, 10), R(r, 0, 360), R(r, -10, 10)), new Vector3(s, s, s), t);
                }
                Flush(false);
            }
        }
        foreach (var f in fires) Fire(f, 1.5f, 34f, 1.5f);
        Fire(new Vector3(4.5f, 1.1f, 7), 0.55f, 11f, 0.4f);
        foreach (var w in new[] { new Vector3(0, 1.4f, -12), new Vector3(-13, 1.6f, -13), new Vector3(12, 1.6f, 14), new Vector3(-40, 1.6f, -10), new Vector3(40, 1.6f, 12), new Vector3(8, 1.5f, -36) }) Wisp(w);
        AshFall(new Vector3(0, 15, -4), new Vector3(130, 6, 110), 50);
    }

    public static LookStudyWalker.Surface PalaceSurface(float x, float z)
    {
        return Mathf.Abs(x) > PalaceGalX + 1f || z < PalaceSouthZ - 1f ? LookStudyWalker.Surface.Dirt : LookStudyWalker.Surface.Stone;
    }

    public static float VillageGrass(float x, float z)
    {
        float ld = LaneDist(x, z), dd = new Vector2(x, z).magnitude;
        if (ld < 2.4f || dd < 10.5f) return 0f;
        return SS(2.4f, 4.5f, ld) * SS(10.5f, 13f, dd) * (0.35f + 0.65f * SS(0.4f, 0.6f, N(x * 0.13f, z * 0.13f)));
    }

    public static LookStudyWalker.Surface VillageSurface(float x, float z)
    {
        if (VillageGrass(x, z) > 0.4f) return LookStudyWalker.Surface.Grass;
        return LookStudyWalker.Surface.Dirt;
    }

    public static void ApplyMood(Kind k, Light sun)
    {
        if (k == Kind.Palace)
        {
            // Smoke-choked dusk: ashen court, orange from the burning hall, faint moon from the west.
            if (sun) sun.transform.rotation = Quaternion.Euler(42, 112, 0);
            Shader.SetGlobalVector("_LSSunCol", new Vector3(0.62f, 0.62f, 0.76f));
            Shader.SetGlobalVector("_LSShadowMul", new Vector3(0.40f, 0.36f, 0.37f));
            Shader.SetGlobalVector("_LSSkyTop", LookStudyWorld.Lin(0.1f, 0.06f, 0.06f));
            Shader.SetGlobalVector("_LSSkyHor", LookStudyWorld.Lin(0.42f, 0.2f, 0.12f));
            Shader.SetGlobalVector("_LSSkyGlow", LookStudyWorld.Lin(0.4f, 0.22f, 0.12f));
            Shader.SetGlobalVector("_LSCloudCol", LookStudyWorld.Lin(0.2f, 0.12f, 0.1f));
            Shader.SetGlobalVector("_LSFogCol", LookStudyWorld.Lin(0.24f, 0.17f, 0.15f));
            Shader.SetGlobalFloat("_LSFogDensity", 0.0058f);
            Shader.SetGlobalFloat("_LSHeightFog", 0.1f);
            Shader.SetGlobalVector("_LSHeightFogCol", LookStudyWorld.Lin(0.23f, 0.19f, 0.2f));
        }
        else
        {
            // Just after sunset: violet-grey smoke, orange from burning roofs.
            if (sun) sun.transform.rotation = Quaternion.Euler(30, 150, 0);
            Shader.SetGlobalVector("_LSSunCol", new Vector3(0.68f, 0.68f, 0.86f));
            Shader.SetGlobalVector("_LSShadowMul", new Vector3(0.4f, 0.4f, 0.5f));
            Shader.SetGlobalVector("_LSSkyTop", LookStudyWorld.Lin(0.13f, 0.16f, 0.22f));
            Shader.SetGlobalVector("_LSSkyHor", LookStudyWorld.Lin(0.72f, 0.42f, 0.28f));
            Shader.SetGlobalVector("_LSSkyGlow", LookStudyWorld.Lin(0.7f, 0.4f, 0.22f));
            Shader.SetGlobalVector("_LSCloudCol", LookStudyWorld.Lin(0.25f, 0.21f, 0.25f));
            Shader.SetGlobalVector("_LSFogCol", LookStudyWorld.Lin(0.22f, 0.22f, 0.27f));
            Shader.SetGlobalFloat("_LSFogDensity", 0.0064f);
            Shader.SetGlobalFloat("_LSHeightFog", 0.18f);
            Shader.SetGlobalVector("_LSHeightFogCol", LookStudyWorld.Lin(0.29f, 0.32f, 0.36f));
        }
        Shader.SetGlobalFloat("_LSStars", 0f);
        Shader.SetGlobalFloat("_LSCloudShade", 0.1f);
        Shader.SetGlobalVector("_LSPush", Vector4.zero);
        Shader.SetGlobalFloat("_LSWaterY", -3f);
        var empty = new Vector4[8];
        Shader.SetGlobalVectorArray("_LSRipples", empty);
    }
}
