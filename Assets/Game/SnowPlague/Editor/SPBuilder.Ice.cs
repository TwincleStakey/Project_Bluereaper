// SPBuilder part: the frozen river, r3 (refs2: V Rising ss41 frozen lake + refs2/frozen_river). A flat band of
// blue-grey ice (SnowPlague/Ice: polygon plates with thin white cracks, darker clear patches with bubbles, frosted
// halos, wind-streaked snow patches on the ice, a thin frost rim at the shore) with no bank walls: the ground snow runs
// straight onto it. Shore scatter (rocks, pebbles, twigs, brown dry grass), no colliders (walkable play space), and
// SPIceReflect on the root (bullet/lantern glows reflected in the ice). Enters west at z ~ -24, crosses the open
// middle (straight run around x = 0..10 for a bridge), leaves east at z ~ 28 between the cart (E) and jar (NE) vignettes.
using System.Collections.Generic;
using SnowPlague;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    // river centreline control points (x, z), Catmull-Rom through them. Ground/lead: keep snow drifts and props off
    // the band |distance| < half width (4.2-6.8 m) + 3.5 m bank (DistanceToRiver).
    public static readonly Vector2[] RiverPath =
    {
        new Vector2(-84f, -25f), new Vector2(-58f, -25f),   // gnd9: runs on past the west bridge (x -66)
        new Vector2(-45f, -23.5f), new Vector2(-33f, -18f),
        new Vector2(-21f, -10f), new Vector2(-10f, -2.5f),
        // straight run (collinear) for a bridge: x = -3 .. 13
        new Vector2(-3f, 1.25f), new Vector2(0f, 2f), new Vector2(5f, 3.25f), new Vector2(10f, 4.5f), new Vector2(13f, 5.25f),
        new Vector2(22f, 11f), new Vector2(32f, 18.5f), new Vector2(42f, 23f), new Vector2(52f, 26f), new Vector2(61f, 28f), new Vector2(74f, 30f),
    };
    public const float RiverBank = 3.5f;   // bank reach past the ice edge (m)

    // distance (m) from (x, z) to the river centreline polyline (control points; within ~0.5 m of the spline)
    public static float DistanceToRiver(float x, float z) => SegDist(new Vector2(x, z), RiverPath, out _);

    static float RiverHalfWidth(float s, float x)
    {
        float w = Mathf.Lerp(4.2f, 6.8f, Mathf.PerlinNoise(s * 0.035f + 3f, 1.7f));
        float bridge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Max(Mathf.Abs(x - 5f) - 7f, 0f) / 6f);   // even 5 m half width under the bridge
        return Mathf.Lerp(w, 5f, bridge);
    }

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // centreline resampled every `step` metres
    static List<Vector2> RiverSamples(float step)
    {
        var dense = new List<Vector2>();
        int n = RiverPath.Length;
        for (int i = 0; i + 1 < n; i++)
        {
            var p0 = RiverPath[Mathf.Max(i - 1, 0)]; var p1 = RiverPath[i]; var p2 = RiverPath[i + 1]; var p3 = RiverPath[Mathf.Min(i + 2, n - 1)];
            for (int k = 0; k < 40; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / 40f));
        }
        dense.Add(RiverPath[n - 1]);
        var outp = new List<Vector2> { dense[0] };
        float acc = 0;
        for (int i = 1; i < dense.Count; i++)
        {
            var a = dense[i - 1]; var b = dense[i];
            float seg = Vector2.Distance(a, b);
            while (acc + seg >= step)
            {
                float t = (step - acc) / seg;
                a = Vector2.Lerp(a, b, t); seg = Vector2.Distance(a, b); acc = 0;
                outp.Add(a);
            }
            acc += seg;
        }
        return outp;
    }

    static float GroundY(float x, float z)
    {
        float d = mDrift != null ? SampleMask(mDrift, x, z) : 0f;
        return Height(x, z) + d * d * 0.55f;
    }

    // r3 (11-11, Kido: "주위에 저렇게 눈 쌓아두지 말고", "얼음 얼면 저런색깔 아니야"): no bank meshes. One flat band, ~1.1 m
    // wider than the ice on each side; its outer metre dips under the ground so the snow ground runs straight onto the
    // ice (the shader draws the irregular shoreline, the thin frost rim and the snow patches). Shore detail is a
    // scatter of rocks (snow on top only), pebbles, dark twigs and brown dry grass along both shorelines.
    // scanned ice set for the river (SPIce r5): "Ice003" (ambientCG), "FrozenLake" / "IcyRoad" (Quixel Megascans via Fab)
    public static string IceSet = "Ice003";
    static partial void BuildIce()
    {
        string g2 = TexDir + "Ground2/", ms = Root + "/ThirdParty/Megascans/";
        // r5 (11-12, Kido: "얼음 그냥 전으로 돌려, 그다음에 가능하면 얼음 텍스쳐팩 찾아봐"): ice8 shading, scanned ice sets
        var sets = new Dictionary<string, (string a, string n, string r, bool dx, float tile)>
        {
            { "Ice003", (TexDir + "Ice/SP_Ice_A.jpg", TexDir + "Ice/SP_Ice_N.jpg", TexDir + "Ice/SP_Ice_R.jpg", false, 6f) },
            { "FrozenLake", (ms + "frozen_lake_tcbkdcfr_2k/Frozen_Lake_tcbkdcfr_2K_BaseColor.jpg", ms + "frozen_lake_tcbkdcfr_2k/Frozen_Lake_tcbkdcfr_2K_Normal.jpg", ms + "frozen_lake_tcbkdcfr_2k/Frozen_Lake_tcbkdcfr_2K_Roughness.jpg", true, 4f) },
            { "IcyRoad", (ms + "icy_road_ugwlejnn_2k/Icy_Road_ugwlejnn_2K_BaseColor.jpg", ms + "icy_road_ugwlejnn_2k/Icy_Road_ugwlejnn_2K_Normal.jpg", ms + "icy_road_ugwlejnn_2k/Icy_Road_ugwlejnn_2K_Roughness.jpg", true, 4f) },
        };
        var set = sets.TryGetValue(IceSet, out var sv) ? sv : sets["Ice003"];
        foreach (var f in new[] { set.a, set.n, set.r })
            if (AssetImporter.GetAtPath(f) == null) AssetDatabase.ImportAsset(f, ImportAssetOptions.ForceSynchronousImport);
        var ground = mats.GetValueOrDefault("SP_Ground");
        var mat = Mat("SP_Ice", "SnowPlague/Ice", m =>
        {
            m.SetTexture("_IceA", Tex(set.a));
            m.SetTexture("_IceN", Tex(set.n, false, true));
            m.SetTexture("_IceR", Tex(set.r, false, true));
            m.SetFloat("_IceNFlipY", set.dx ? 1f : 0f);
            m.SetFloat("_IceTile", set.tile);
            // the ground's own snow layer (scan, tile, tint) for the dust near the shore
            m.SetTexture("_SnowA", Tex(g2 + "SP_G2_SnowMS_A.jpg"));
            m.SetTexture("_SnowN", Tex(g2 + "SP_G2_SnowMS_N.jpg", false, true));
            if (ground)
            {
                if (ground.HasProperty("_SnowTint")) m.SetColor("_SnowTint", ground.GetColor("_SnowTint"));
                if (ground.HasProperty("_Tiles")) m.SetFloat("_SnowTile", ground.GetVector("_Tiles").x);
            }
            // ice8 values (SetColor is sRGB here: converted to linear)
            m.SetColor("_IceTint", new Color(0.32f, 0.38f, 0.42f)); m.SetColor("_DeepCol", new Color(0.012f, 0.02f, 0.03f));
            m.SetColor("_CrackCol", new Color(0.5f, 0.56f, 0.62f)); m.SetColor("_SkyCol", new Color(0.07f, 0.09f, 0.13f));
            m.SetFloat("_Cracks", 0.85f); m.SetFloat("_CrackDepth", 0.15f); m.SetFloat("_Frost", 0.5f); m.SetFloat("_IceNormal", 0.25f); m.SetFloat("_Smooth", 0.92f);
            m.SetFloat("_Dust", 1f); m.SetFloat("_Rim", 0.9f); m.SetFloat("_ShoreNoise", 0.6f);
            m.SetFloat("_ReflStrength", 3f); m.SetFloat("_GlowStrength", 1f); m.SetFloat("_GlowStretch", 2.2f); m.SetFloat("_GlowRadius", 5f);
            m.SetFloat("_MoonStreak", 0.25f); m.SetFloat("_LanternStreak", 0.6f);
            m.renderQueue = (int)RenderQueue.AlphaTest;
        });
        if (!mat) return;
        Log("ICE set " + IceSet);

        const float step = 0.35f;
        var c = RiverSamples(step);
        // round off the corners of the uniform Catmull-Rom (uneven control spacing made visible kinks)
        for (int pass = 0; pass < 24; pass++)
        {
            var prev = c[0];
            for (int k = 1; k + 1 < c.Count; k++) { var cur = c[k]; c[k] = (prev + 2f * cur + c[k + 1]) * 0.25f; prev = cur; }
        }
        int ns = c.Count;
        var tan = new Vector2[ns]; var w = new float[ns]; var iceY = new float[ns]; var lipS = new float[2, ns];
        for (int k = 0; k < ns; k++)
        {
            tan[k] = (c[Mathf.Min(k + 3, ns - 1)] - c[Mathf.Max(k - 3, 0)]).normalized;
            float s = k * step;
            w[k] = RiverHalfWidth(s, c[k].x);
            iceY[k] = GroundY(c[k].x, c[k].y) + 0.03f;
            for (int sd = 0; sd < 2; sd++)
                lipS[sd, k] = w[k] + (Mathf.PerlinNoise(s * 0.11f + sd * 37f, 2.3f) - 0.5f) * 1.6f;
        }
        for (int pass = 0; pass < 4; pass++)
            for (int k = 1; k + 1 < ns; k++) iceY[k] = (iceY[k - 1] + 2f * iceY[k] + iceY[k + 1]) * 0.25f;
        Vector2 Across(int k) => new Vector2(-tan[k].y, tan[k].x);   // +o side

        var v = new List<Vector3>(); var col = new List<Color>(); var uv = new List<Vector2>(); var tri = new List<int>();
        var rnd = new System.Random(77);
        const int cols = 27;          // 13 per side + centre
        const float outer = 1.1f;     // band reaches this far past the lip
        for (int k = 0; k < ns; k++)
            for (int j = 0; j < cols; j++)
            {
                float u = j / (float)(cols - 1) * 2f - 1f;
                int sd = u < 0 ? 0 : 1;
                float lip = lipS[sd, k];
                // denser columns near the edge: u^0.6 spacing toward the lip
                float o = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 0.7f) * (lip + outer);
                var p2 = c[k] + Across(k) * o;
                float past = Mathf.Abs(o) - lip - 0.4f;
                float y = iceY[k];
                if (past > 0f)
                {
                    // dip under the ground over the outer metre (crosses it about half way)
                    float g = GroundY(p2.x, p2.y);
                    y = Mathf.Lerp(iceY[k], g - 0.04f, Mathf.SmoothStep(0f, 1f, past / (outer - 0.4f)));
                }
                v.Add(new Vector3(p2.x, y, p2.y));
                col.Add(new Color(0, lip / 10f, (float)rnd.NextDouble(), 1)); uv.Add(new Vector2(k * step, o));
            }
        for (int k = 0; k + 1 < ns; k++)
            for (int j = 0; j + 1 < cols; j++)
            {
                int a = k * cols + j, b = a + 1, d = a + cols, e = d + 1;
                tri.Add(a); tri.Add(b); tri.Add(d); tri.Add(b); tri.Add(e); tri.Add(d);
            }
        var mesh = new Mesh { name = "SP_River", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(v); mesh.SetColors(col); mesh.SetUVs(0, uv); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        var path = Gen + "/SP_River.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);

        var go = new GameObject("FrozenRiver"); go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        BuildShoreScatter(go.transform, c, Across, lipS, ns, step);

        // reflections: bullet glows + optional SSR history (on the stage root)
        var refl = root.gameObject.GetComponent<SPIceReflect>();
        if (!refl) refl = root.gameObject.AddComponent<SPIceReflect>();
        refl.copyShader = Shader.Find("Hidden/SnowPlague/IceCopy");
        refl.historyScale = 1f;
        var cs = new List<Vector2>(); var hw = new List<float>();
        for (int k = 0; k < ns; k += 6) { cs.Add(c[k]); hw.Add(w[k] + 0.6f); }
        refl.centre = cs.ToArray(); refl.halfWidth = hw.ToArray();
        refl.debugGlows.Clear();
        var orange = new Color(1.6f, 0.38f, 0.07f) * 1.4f; var blue = new Color(0.3f, 0.7f, 1.6f) * 1.4f;
        foreach (var (s, off, cl) in new[] { (72f, -1.5f, orange), (78f, 1.2f, orange), (84f, -0.4f, blue), (90f, 2.0f, orange), (96f, -2.5f, blue), (104f, 0.5f, orange) })
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(s / step), 0, ns - 1);
            var p2 = c[k] + Across(k) * off;
            refl.debugGlows.Add(new SPIceReflect.DebugGlow { pos = new Vector3(p2.x, iceY[k] + 1.05f, p2.y), color = cl, size = 0.42f });
        }
        Log($"ICE river {ns} samples, {v.Count} verts, ~{ns * step:0} m long");
    }

    // rocks with snow on top, pebbles, dark twigs, brown dry grass (#90704f) along both shorelines, irregular clumps
    static void BuildShoreScatter(Transform parent, List<Vector2> c, System.Func<int, Vector2> across, float[,] lipS, int ns, float step)
    {
        var go = new GameObject("ShoreScatter"); go.transform.SetParent(parent, false);
        go.AddComponent<LookStudyInstancer>();
        var sc = go.AddComponent<SPScatter>();
        var srng = new System.Random(4242);
        float Rs(float a, float b) => a + (float)srng.NextDouble() * (b - a);
        Material Brown(Material src) => Mat("SP_IceShoreGrass_" + src.name.Replace(" ", ""), src.shader.name, m => { m.CopyPropertiesFromMaterial(src); if (m.HasProperty("_Tint")) m.SetColor("_Tint", Hex("#90704f")); });
        // (model, clumps per 100 m of shore, items per clump, size min/max (m; negative = plain scale), offset from lip min/max (m), sink fraction)
        var plan = new (string m, float per100, int clump, float s0, float s1, float o0, float o1, float sink)[]
        {
            ("PH:rock_07", 7f, 1, 0.5f, 1.1f, -0.3f, 0.8f, 0.3f), ("PH:rock_09", 5f, 1, 0.5f, 1.2f, -0.2f, 0.9f, 0.3f),
            ("PH:stone_01", 18f, 3, 0.15f, 0.4f, -0.4f, 0.7f, 0.25f),
            ("SP_Pebble_A", 22f, 5, 0.06f, 0.16f, -0.5f, 0.6f, 0.2f), ("SP_Pebble_C", 22f, 5, 0.06f, 0.16f, -0.5f, 0.6f, 0.2f),
            ("SP_Twigs", 16f, 2, -0.9f, -1.3f, -0.3f, 0.9f, 0f), ("SP_TwigBit", 30f, 3, -0.9f, -1.4f, -0.5f, 0.8f, 0f),
            // gnd11 (QA amateur: an even grass fringe along the bank): fewer, bigger clumps; a few frozen-in tufts and twigs on the ice
            ("SP_GrassTuft_A", 12f, 12, -0.6f, -1.4f, -0.4f, 1.2f, 0.03f), ("SP_GrassTuft_B", 12f, 12, -0.6f, -1.4f, -0.4f, 1.2f, 0.03f),
            ("SP_GrassTuft_C", 10f, 10, -0.6f, -1.2f, -0.2f, 1.2f, 0.03f), ("SP_GrassTuft_B", 5f, 6, -0.6f, -1.0f, -1.6f, -0.3f, 0.05f),
            ("SP_TwigBit", 9f, 4, -0.9f, -1.3f, -4.0f, -0.8f, 0f),
        };
        float shoreLen = ns * step;
        int total = 0;
        foreach (var p in plan)
        {
            var srcs = ClutterSources(p.m);
            if (srcs.Count == 0) { Log("ICE shore skip " + p.m); continue; }
            var sets = new List<SPScatter.Set>();
            foreach (var s in srcs)
            {
                var m = s.mat;
                if (p.m.StartsWith("SP_GrassTuft") && m) m = Brown(m) ?? m;
                else if (m && (p.m.StartsWith("SP_Pebble") || p.m.StartsWith("PH:stone")))   // gnd11: same toned stone as the ground clutter
                { var src = m; m = Mat("SP_GC_Stone_" + src.name.Replace(" ", ""), src.shader.name, mm => { mm.CopyPropertiesFromMaterial(src); if (mm.HasProperty("_SnowBias")) mm.SetFloat("_SnowBias", -0.3f); if (mm.HasProperty("_Tint")) mm.SetColor("_Tint", mm.GetColor("_Tint") * 0.9f); }) ?? src; }
                var set = new SPScatter.Set { mesh = s.mesh, mat = m, pre = s.pre, preOffset = s.preOffset, preScale = s.preScale };
                sets.Add(set); sc.sets.Add(set);
            }
            int clumps = Mathf.RoundToInt(p.per100 * shoreLen * 2f / 100f);
            for (int q = 0; q < clumps; q++)
            {
                int k = srng.Next(ns); int sd = srng.Next(2);
                float sign = sd == 0 ? -1f : 1f;
                // clumps prefer some stretches of shore (irregular, not an even row)
                if (Mathf.PerlinNoise(k * step * 0.08f + p.m.Length, sd * 11f) < 0.42f) continue;
                float baseO = lipS[sd, k] + Rs(p.o0, p.o1);
                int n = Mathf.Max(1, (int)(p.clump * Rs(0.5f, 1.5f)));
                for (int t = 0; t < n; t++)
                {
                    var s = srcs[srng.Next(srcs.Count)];
                    var set = sets[srcs.IndexOf(s)];
                    float along = Rs(-0.8f, 0.8f) * Mathf.Sqrt(p.clump) * 0.4f, off = Rs(-0.3f, 0.3f) * Mathf.Sqrt(p.clump) * 0.4f;
                    int kk = Mathf.Clamp(k + Mathf.RoundToInt(along / step), 0, ns - 1);
                    var p2 = c[kk] + across(kk) * sign * (baseO + off);
                    float size = Rs(Mathf.Abs(p.s0), Mathf.Abs(p.s1));
                    float scale = p.s0 < 0 ? size : size / s.extent;
                    float sink = p.s0 < 0 ? 0.01f : size * p.sink * 0.5f;
                    set.at.Add(new Vector4(p2.x, GroundY(p2.x, p2.y) + 0.02f - sink, p2.y, Rs(0, 360)));
                    set.scale.Add(scale);
                    total++;
                }
            }
        }
        sc.sets.RemoveAll(st => st.at.Count == 0);
        sc.Push();
        Log($"ICE shore scatter {total} instances, {sc.sets.Count} sets");
    }
}
