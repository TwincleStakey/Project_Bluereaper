// 설원 역병 ground (11-11 redo 2, ground worker). Kido rejected the clean flat blue-grey snow sheet. Target = tarae ss5:
// patchy dirty snow, thin and trampled, brownish-grey; dense gravel, scattered rocks and pebbles, leaves and twigs;
// brown leaf litter / mud showing through over large areas (~40% of the arena), strong relief at the game camera.
//   ConfigureGround: Poly Haven 2k scans in Textures/Ground2 (snow_01, rocky_trail, stony_dirt_path, winter_leaves) and
//                    the redo-2 shader params (SPGroundSurface.hlsl). Bare share/edges: BuildMasks in SPBuilder.cs.
//   BuildGroundClutter: "GroundClutter", ~20k instanced rocks, stones, pebbles, leaf clumps, bark and twigs over the
//                    whole arena (centre included), denser where the snow is broken, plus a few Megascans snowy rock
//                    piles. Nothing within 6.5 m of the frozen river centreline. No colliders, no shadows.
using System.Collections.Generic;
using System.IO;
using SnowPlague;
using UnityEditor;
using UnityEngine;

public static partial class SPBuilder
{
    const string G2 = "Ground2/SP_G2_";

    static partial void ConfigureGround(Material m)
    {
        if (!File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, TexDir + G2 + "Snow_A.jpg")))
        { Log("GROUND Ground2 textures missing, keeping the old layers"); return; }
        // normal maps go in as linear colour textures (the shader decodes .rg itself), heights linear
        void L(string slot, string n)
        {
            m.SetTexture(slot + "A", T(G2 + n + "_A.jpg"));
            m.SetTexture(slot + "N", T(G2 + n + "_N.jpg", false, true));
            m.SetTexture(slot + "H", T(G2 + n + "_H.jpg", false, true));
        }
        // snow: Megascans fresh_windswept_snow (clean scan; the shader dirties it), thin/dirty snow: Megascans snow_gravel
        // (both converted to GL normals + grey height by the ground worker); bare layers Poly Haven
        bool ms = File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, TexDir + G2 + "SnowMS_A.jpg"));
        L("_Snow", ms ? "SnowMS" : "Snow"); L("_Gravel", "Gravel"); L("_Mud", "Mud"); L("_Leaf", "Leaves");
        if (File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, TexDir + G2 + "Slush_A.jpg")))
        { L("_Slush", "Slush"); m.SetFloat("_Slush", 0.7f); m.SetFloat("_SlushTile", 2.5f); }
        else m.SetFloat("_Slush", 0f);
        // scans are 2-2.2 m; kept near real size so pebbles and leaves stay small and crisp at ~13 mm per pixel
        m.SetVector("_Tiles", new Vector4(2.4f, 2.6f, 3.0f, 2.6f));
        m.SetFloat("_HexScale", 1.2f);
        // gnd1 (QA vs ss5): neutral snow albedo ~#d4d6d8 (scan 224 x 0.95 sRGB tint), B never above R/G; blue only in light
        m.SetColor("_SnowTint", new Color(0.952f, 0.95f, 0.948f));
        m.SetColor("_ShadeTint", new Color(0.86f, 0.87f, 0.9f));   // gnd1: was strongly blue (read lilac); the cool shadow comes from the lighting
        m.SetFloat("_SnowRelief", 1f);
        m.SetFloat("_MeltHoles", 0.8f);
        // gr1: gravel/pebbles is the bare base now (ss5: light warm grey-brown ground dense with pebbles), leaves in patches
        // gnd1: bare ground grey-olive ~#6e665a, no pink (the warm tints x tan scans read pink-tan under the lanterns)
        m.SetColor("_LeafTint", new Color(1.35f, 1.35f, 1.33f));
        m.SetColor("_SoilTint", new Color(1.55f, 1.55f, 1.52f));   // gr3: churned soil round the vignettes read near-black
        m.SetColor("_GravelTint", new Color(1.0f, 1.0f, 0.98f));   // gnd2: dirt ~0.65 x the lit snow value (gnd1 measured 70 vs snow 136)
        m.SetColor("_RedLeaf", new Color(0.4f, 0.07f, 0.05f));
        m.SetFloat("_RedLeaves", 0.05f);
        m.SetFloat("_Calm", 0.7f);
        m.SetFloat("_Wet", 0.3f);
        m.SetFloat("_SnowBias", 0f);
        m.SetFloat("_SnowFill", 1.0f);   // gr1: weight of the ground's own height in the snow threshold (x0.6)
        m.SetFloat("_BlendDepth", 0.2f);
        m.SetFloat("_NormalStrength", 1.35f);
        m.SetFloat("_SnowFlatten", ms ? 1f : 0.6f);   // the Megascans snow is near-flat white already: keep all its detail
        m.SetFloat("_Sparkle", 8f);
        m.SetFloat("_SnowSmooth", 0.2f);   // gr2: the snow read as glossy wet plaster; ss5 powder is matte
        // redo 2
        m.SetColor("_DirtTint", new Color(0.88f, 0.86f, 0.82f));
        m.SetFloat("_DirtySnow", 0.85f);
        m.SetFloat("_ThinFrost", 0.35f);
        m.SetFloat("_HollowSnow", 0f);   // gr1: unused (the level/height threshold leaves the specks in the gaps itself)
        m.SetFloat("_SnowEdge", 0.06f);   // gr1: per-pixel softness against the local height (the gradient comes from the level)
        m.SetFloat("_BareNormal", 1.3f);
        m.SetFloat("_MudAmount", 0.3f);
        m.SetFloat("_GravelAmount", 1f);
        m.SetFloat("_GroundAO", 0.35f);
    }

    // ------------------------------------------------------------------ clutter
    struct ClutterSrc { public Mesh mesh; public Material mat; public Quaternion pre; public Vector3 preOffset; public float preScale, extent; public int tris; }

    // every mesh of a model, re-anchored so its footprint centre and lowest point sit on the instance origin
    static List<ClutterSrc> ClutterSources(string model)
    {
        var list = new List<ClutterSrc>();
        var src = Src(model); if (!src) return list;
        foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (!mr || !mr.sharedMaterial || !mf.sharedMesh) continue;
            var b = mf.sharedMesh.bounds;
            Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f;
            for (int c = 0; c < 8; c++)
            {
                var p = mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            var anchor = new Vector3((lo.x + hi.x) * 0.5f, lo.y, (lo.z + hi.z) * 0.5f);
            list.Add(new ClutterSrc
            {
                mesh = mf.sharedMesh, mat = mr.sharedMaterial, pre = mf.transform.rotation, preOffset = mf.transform.position - anchor,
                preScale = mf.transform.lossyScale.x, extent = Mathf.Max(0.01f, Mathf.Max(hi.x - lo.x, hi.z - lo.z)),
                tris = (int)(mf.sharedMesh.GetIndexCount(0) / 3),
            });
        }
        return list;
    }

    // Megascans model (ThirdParty/Megascans/<folder>): its meshes with a SnowPlague/Prop material built from the 2K maps
    // (DirectX normal: green flipped on import)
    const string MS = Root + "/ThirdParty/Megascans/";
    static List<ClutterSrc> MegaSources(string folder)
    {
        var list = new List<ClutterSrc>();
        var dir = MS + folder;
        var abs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, dir);
        if (!Directory.Exists(abs)) { Log("CLUTTER missing " + dir); return list; }
        string fbx = null, bc = null, nm = null;
        foreach (var p in Directory.GetFiles(abs))
        {
            var fn = Path.GetFileName(p);
            if (fn.EndsWith(".meta")) continue;
            if (fn.EndsWith(".fbx") && (fbx == null || fn.Contains("LOD1"))) fbx = dir + "/" + fn;
            if (fn.EndsWith("_BaseColor.jpg") && !fn.Contains("Billboard")) bc = dir + "/" + fn;
            if (fn.EndsWith("_Normal.jpg") && !fn.Contains("Billboard")) nm = dir + "/" + fn;
        }
        if (fbx == null) return list;
        Texture2D normal = null;
        if (nm != null)
        {
            normal = Tex(nm, true);
            var imp = AssetImporter.GetAtPath(nm) as TextureImporter;
            if (imp && !imp.flipGreenChannel) { imp.flipGreenChannel = true; imp.SaveAndReimport(); normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nm); }
        }
        var mat = Prop("SP_MS_" + folder, bc != null ? Tex(bc) : null, normal, new Color(0.92f, 0.92f, 0.93f), 0.2f, 0.3f);
        if (!mat) return list;
        mat.SetFloat("_VertexFrost", 0f); mat.SetFloat("_SnowBias", -0.3f);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (!go) { Log("CLUTTER no model " + fbx); return list; }
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            var b = mf.sharedMesh.bounds;
            Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f;
            for (int c = 0; c < 8; c++)
            {
                var p = mf.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
            }
            list.Add(new ClutterSrc
            {
                mesh = mf.sharedMesh, mat = mat, pre = mf.transform.rotation, preOffset = mf.transform.position - new Vector3((lo.x + hi.x) * 0.5f, lo.y, (lo.z + hi.z) * 0.5f),
                preScale = mf.transform.lossyScale.x, extent = Mathf.Max(0.01f, Mathf.Max(hi.x - lo.x, hi.z - lo.z)), tris = (int)(mf.sharedMesh.GetIndexCount(0) / 3),
            });
        }
        return list;
    }

    // a clump of fallen leaves: flat cupped leaf shapes, each mapped onto a random patch of the leaf-litter scan
    static Mesh LeafClumpMesh(string name, int leaves, float radius, int seed)
    {
        var r = new System.Random(seed);
        float Rf(float a, float b) => a + (float)r.NextDouble() * (b - a);
        var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var t = new List<int>();
        for (int l = 0; l < leaves; l++)
        {
            float a = Rf(0, 6.283f), d = Mathf.Sqrt(Rf(0, 1)) * radius;
            var c = new Vector3(Mathf.Cos(a) * d, 0.004f + l * 0.0012f, Mathf.Sin(a) * d);
            float len = Rf(0.045f, 0.1f), wid = len * Rf(0.35f, 0.6f);
            var rot = Quaternion.Euler(Rf(-18, 18), Rf(0, 360), Rf(-18, 18));
            var up = rot * Vector3.up;
            var uv0 = new Vector2(Rf(0, 0.9f), Rf(0, 0.9f));
            var tone = new Color(0f, Rf(0.7f, 1f), Rf(0, 1), 1f);   // R frost off, G AO, B per-leaf random
            int baseI = v.Count;
            v.Add(c + rot * new Vector3(0, -0.003f, 0)); nrm.Add(up); uv.Add(uv0 + new Vector2(0.02f, 0.02f)); col.Add(tone);
            const int N = 8;
            for (int k = 0; k < N; k++)
            {
                float th = k / (float)N * 6.283f;
                float sx = Mathf.Sin(th), cz = Mathf.Cos(th);
                float w = wid * 0.5f * Mathf.Abs(sx) * (cz > 0 ? 1f - cz * 0.45f : 1f);   // pointed tip, round base
                var p = new Vector3(Mathf.Sign(sx) * w, 0.004f * Mathf.Abs(sx), cz * len * 0.5f);   // edges curl up
                v.Add(c + rot * p); nrm.Add(up);
                uv.Add(uv0 + new Vector2(0.02f + p.x / 2.6f, 0.02f + p.z / 2.6f)); col.Add(tone);
            }
            for (int k = 0; k < N; k++) { t.Add(baseI); t.Add(baseI + 1 + k); t.Add(baseI + 1 + (k + 1) % N); }
        }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(t, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        var path = $"{Gen}/{name}.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static partial void BuildGroundClutter()
    {
        var go = new GameObject("GroundClutter"); go.transform.SetParent(root, false);
        go.AddComponent<LookStudyInstancer>();
        var sc = go.AddComponent<SPScatter>();
        var crng = new System.Random(7713);
        float Rc(float a, float b) => a + (float)crng.NextDouble() * (b - a);

        // leaf clump meshes and materials (scan albedo, no normal map: the ground's linear import of it is not a normal map)
        var leafA = T(G2 + "Leaves_A.jpg");
        var leafMat = Prop("SP_GC_Leaf", leafA, null, new Color(1.0f, 0.86f, 0.72f), 0.08f, 0.3f, true);
        var redMat = Prop("SP_GC_LeafRed", leafA, null, new Color(1.25f, 0.42f, 0.3f), 0.1f, 0.15f, true);
        foreach (var lm in new[] { leafMat, redMat }) if (lm) { lm.SetFloat("_SnowBias", -0.55f); lm.SetFloat("_GroundMelt", 0.03f); }
        var leafMeshes = new[] { LeafClumpMesh("SP_GC_LeafClump_A", 14, 0.22f, 11), LeafClumpMesh("SP_GC_LeafClump_B", 22, 0.35f, 23), LeafClumpMesh("SP_GC_LeafClump_C", 8, 0.14f, 37) };

        // (model, count, size min/max in metres (negative: plain scale), prefers broken snow 0..1, prefers gravel 0..1,
        //  points per clump, sink as a fraction of the size)
        var plan = new (string m, int n, float s0, float s1, float bare, float grav, int clump, float sink)[]
        {
            ("SP_Pebble_A", 1400, 0.05f, 0.16f, 0.8f, 0.5f, 9, 0.2f), ("SP_Pebble_B", 1400, 0.05f, 0.16f, 0.8f, 0.5f, 9, 0.2f),
            ("SP_Pebble_C", 1400, 0.05f, 0.16f, 0.8f, 0.5f, 9, 0.2f), ("SP_Pebble_D", 1400, 0.05f, 0.16f, 0.8f, 0.5f, 9, 0.2f),
            ("SP_PebbleCluster", 900, 0.25f, 0.5f, 0.75f, 0.6f, 5, 0.15f),
            ("SP_StoneChip_A", 700, 0.08f, 0.22f, 0.7f, 0.4f, 6, 0.2f), ("SP_StoneChip_B", 700, 0.08f, 0.22f, 0.7f, 0.4f, 6, 0.2f),
            ("PH:stone_01", 500, 0.12f, 0.35f, 0.7f, 0.5f, 4, 0.25f),
            ("PH:namaqualand_stones_01", 1200, 0.06f, 0.25f, 0.75f, 0.6f, 7, 0.2f),
            ("PH:rock_07", 160, 0.35f, 0.8f, 0.5f, 0.3f, 2, 0.3f), ("PH:rock_09", 100, 0.4f, 0.9f, 0.5f, 0.3f, 2, 0.3f),
            ("SP_TwigBit", 1500, -0.8f, -1.4f, 0.7f, 0f, 10, 0f), ("SP_Twigs", 500, -0.9f, -1.3f, 0.6f, 0f, 6, 0f),
            ("PH:bark_debris_01", 500, 0.12f, 0.3f, 0.7f, 0f, 6, 0.1f),
            ("PH:dry_branches_medium_01", 220, 0.5f, 1.1f, 0.5f, 0f, 2, 0.05f),
            ("LEAF:0", 1800, -0.8f, -1.4f, 0.95f, -0.6f, 10, 0f), ("LEAF:1", 1400, -0.8f, -1.3f, 0.95f, -0.6f, 8, 0f),
            ("MS:snowy_rocks_pile_vcykbjsva_mid", 0, 0.7f, 1.6f, 0.2f, 0.3f, 2, 0.2f), ("MS:rocky_snow_pile_xfroec0qx_mid", 0, 0.7f, 1.6f, 0.2f, 0.3f, 2, 0.2f),
            ("LEAF:2", 1800, -0.8f, -1.5f, 0.9f, -0.6f, 12, 0f), ("LEAFRED:0", 300, -0.7f, -1.1f, 0.9f, -0.6f, 4, 0f),
        };
        const int SetTriBudget = 450000;   // per set (all instances, before culling)
        int total = 0; long tris = 0;
        foreach (var p in plan)
        {
            List<ClutterSrc> srcs;
            if (p.m.StartsWith("LEAF"))
            {
                var mesh = leafMeshes[p.m[p.m.Length - 1] - '0'];
                srcs = new List<ClutterSrc> { new ClutterSrc { mesh = mesh, mat = p.m.StartsWith("LEAFRED") ? redMat : leafMat, pre = Quaternion.identity, preOffset = Vector3.zero, preScale = 1f, extent = 0.5f, tris = (int)(mesh.GetIndexCount(0) / 3) } };
            }
            else if (p.m.StartsWith("MS:")) srcs = MegaSources(p.m.Substring(3));
            else srcs = ClutterSources(p.m);
            if (srcs.Count == 0) { Log("CLUTTER skip " + p.m); continue; }
            foreach (var s in srcs)
            {
                if (!s.mat) continue;
                int n = Mathf.Max(1, p.n / srcs.Count);
                if (s.tris * n > SetTriBudget) { int cap = Mathf.Max(20, SetTriBudget / Mathf.Max(1, s.tris)); Log($"CLUTTER {p.m}/{s.mesh.name}: {s.tris} tris, {n} -> {cap}"); n = Mathf.Min(n, cap); }
                var set = new SPScatter.Set { mesh = s.mesh, mat = s.mat, pre = s.pre, preOffset = s.preOffset, preScale = s.preScale };
                int made = 0, left = 0; float cx = 0, cz = 0, spread = 1f;
                for (int k = 0; k < n * 14 && made < n; k++)
                {
                    if (left <= 0)
                    {
                        cx = Rc(-HX - 4f, HX + 4f); cz = Rc(-HZ - 4f, HZ + 4f);
                        float snow = SampleMask(mSnow, cx, cz) - SampleMask(mPath, cx, cz) * 0.3f, grav = SampleMask(mGravel, cx, cz);
                        float dens = Mathf.Lerp(1f, Mathf.Clamp01(1.2f - snow), p.bare);
                        dens *= p.grav >= 0 ? Mathf.Lerp(1f, 0.35f + grav, p.grav) : Mathf.Lerp(1f, 1.2f - grav, -p.grav);
                        dens *= 0.35f + 0.9f * Mathf.PerlinNoise(cx * 0.17f + p.m.Length * 3.1f, cz * 0.17f + p.n * 0.01f);
                        if ((float)crng.NextDouble() > dens) continue;
                        left = Mathf.Max(1, (int)(p.clump * Rc(0.5f, 1.5f))); spread = Rc(0.3f, 1.1f) * Mathf.Sqrt(p.clump) * 0.3f;
                    }
                    left--;
                    float ga = Rc(0, 6.283f), gr = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-4f, Rc(0, 1)))) * spread;
                    float x = cx + Mathf.Cos(ga) * gr, z = cz + Mathf.Sin(ga) * gr;
                    if (DistanceToRiver(x, z) < 6.5f) continue;   // nothing on the frozen river (SPBuilder.Ice.cs)
                    float size = Rc(Mathf.Abs(p.s0), Mathf.Abs(p.s1));
                    float scale = p.s0 < 0 ? size : size / s.extent;
                    float sink = p.s0 < 0 ? 0.005f : size * p.sink * 0.5f;
                    float dr = SampleMask(mDrift, x, z);
                    set.at.Add(new Vector4(x, Height(x, z) + dr * dr * 0.55f - sink, z, Rc(0, 360)));
                    set.scale.Add(scale);
                    made++;
                }
                sc.sets.Add(set); total += made; tris += (long)made * s.tris;
            }
        }
        sc.Push();
        Log($"CLUTTER {total} instances, {sc.sets.Count} sets, {tris / 1000}k tris before culling");
    }
}
