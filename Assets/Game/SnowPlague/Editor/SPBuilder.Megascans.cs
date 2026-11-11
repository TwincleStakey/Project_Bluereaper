// 11-11 redo 2, Megascans detail pass (설원 역병, Kido: "디테일좀 챙겨", target tarae ss5). Places the Quixel Megascans
// assets in ThirdParty/Megascans (Fab, Standard License, free):
//   MegascansScatter (SPScatter + LookStudyInstancer, no shadows): dead shrub clumps (8 variants, LOD1) dense in the
//       outer ring and around vignettes / rock piles / lanterns, a few low ones in the field (fallen pine branches left out, ms3),
//       snowy rock piles, rocky snow piles, dead pine stumps near the edges and vignettes; snowy ground mounds on the
//       edge banks outside the arena.
//   MegascansProps (GameObjects): pine trunks framing east / west / south outside the arena, Japanese stone wall runs
//       on the north (between the bridge and the gate), south and the south ends of the side walls, 4 stone lanterns
//       next to vignettes with an emissive core and the shared Lantern() spot.
// Import: the "Mid" FBX are in cm (UnitScaleFactor 1) -> useFileScale on, globalScale 1, so 1 unit = 1 m. Every mesh
// is baked once into Generated/SP_MX_*.asset with the FBX transform applied, the origin at the footprint centre and
// lowest point, one submesh (the instancer draws submesh 0 only), so scale/pivot quirks of the FBX can't leak into the
// scene. Normals are DirectX: flipGreenChannel on import. The shrubs get their own vertex colours (Veg shader reads
// R frost, G AO, A sway) and an albedo+opacity PNG for the alpha clip.
// Nothing within 7 m of the frozen river centreline, no colliders anywhere.
using System.Collections.Generic;
using System.IO;
using SnowPlague;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const string MXDir = Root + "/ThirdParty/Megascans/";
    const string MXTexDir = TexDir + "Megascans/";

    class MXSrc { public string name; public Mesh mesh; public Material mat; public Vector3 size; public int tris; }

    static string ProjAbs(string assetPath) => Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);

    static string MXFind(string folder, string suffix, string contains = null)
    {
        var abs = ProjAbs(MXDir + folder);
        if (!Directory.Exists(abs)) return null;
        foreach (var p in Directory.GetFiles(abs))
        {
            var fn = Path.GetFileName(p);
            if (fn.EndsWith(".meta") || fn.Contains("Billboard")) continue;
            if (fn.EndsWith(suffix) && (contains == null || fn.Contains(contains))) return MXDir + folder + "/" + fn;
        }
        return null;
    }

    static Texture2D MXNormal(string path)
    {
        if (path == null) return null;
        var t = Tex(path, true);
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp && !imp.flipGreenChannel) { imp.flipGreenChannel = true; imp.SaveAndReimport(); t = AssetDatabase.LoadAssetAtPath<Texture2D>(path); }
        return t;
    }

    static void MXImportFix(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!imp) return;
        bool ch = false;
        if (!imp.useFileScale) { imp.useFileScale = true; ch = true; }
        if (Mathf.Abs(imp.globalScale - 1f) > 1e-4f) { imp.globalScale = 1f; ch = true; }
        if (!imp.isReadable) { imp.isReadable = true; ch = true; }
        if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; ch = true; }
        if (imp.importAnimation || imp.importCameras || imp.importLights) { imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; ch = true; }
        if (imp.meshCompression != ModelImporterMeshCompression.Off) { imp.meshCompression = ModelImporterMeshCompression.Off; ch = true; }
        if (ch) imp.SaveAndReimport();
    }

    // Bakes an FBX into one mesh in metres, origin at footprint centre / lowest point.
    // colours: 0 none (Prop, _VertexFrost 0), 1 shrub (frost tips, AO, sway), 2 static Veg (no frost, no sway); squashY flattens
    static MXSrc MXLoad(string key, string fbxPath, Material mat, int colours, float squashY = 1f)
    {
        if (fbxPath == null || !mat) { Log("MX missing " + key); return null; }
        MXImportFix(fbxPath);
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (!go) { Log("MX no model " + fbxPath); return null; }
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            var m = mf.sharedMesh; if (!m) continue;
            var M = mf.transform.localToWorldMatrix;
            var mv = m.vertices; var mn = m.normals; var mc = m.colors;
            var mu = new List<Vector2>();
            for (int ch = 0; ch < 4 && mu.Count == 0; ch++) { m.GetUVs(ch, mu); if (mu.Count > 0 && ch > 0) Log($"MX {key}: UVs found in channel {ch}"); }
            if (mu.Count == 0) Log($"MX {key}: NO UVs on {m.name} (readable={m.isReadable}, attrs={string.Join(",", System.Linq.Enumerable.Select(m.GetVertexAttributes(), x => x.attribute + ":" + x.dimension))})");
            else { Vector2 ulo = mu[0], uhi = mu[0]; foreach (var q in mu) { ulo = Vector2.Min(ulo, q); uhi = Vector2.Max(uhi, q); } Log($"MX {key}: uv {mu.Count}/{mv.Length} range {ulo}..{uhi}"); }
            int b = v.Count;
            for (int i = 0; i < mv.Length; i++)
            {
                v.Add(M.MultiplyPoint3x4(mv[i]));
                n.Add(i < mn.Length ? M.MultiplyVector(mn[i]).normalized : Vector3.up);
                uv.Add(i < mu.Count ? mu[i] : Vector2.zero);
                c.Add(i < mc.Length ? mc[i] : new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            for (int s = 0; s < m.subMeshCount; s++) foreach (var idx in m.GetTriangles(s)) t.Add(idx + b);
        }
        if (v.Count == 0) { Log("MX empty " + fbxPath); return null; }
        Vector3 lo = v[0], hi = v[0];
        foreach (var p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        var anchor = new Vector3((lo.x + hi.x) * 0.5f, lo.y, (lo.z + hi.z) * 0.5f);
        float h = Mathf.Max(0.01f, hi.y - lo.y);
        for (int i = 0; i < v.Count; i++)
        {
            v[i] -= anchor;
            v[i] = new Vector3(v[i].x, v[i].y * squashY, v[i].z);
            if (colours == 2) c[i] = new Color(0f, Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(v[i].y / (h * squashY))), c[i].b, 0f);
            else if (colours == 1)
            {
                // R frost (only a little, on the upper twigs), G AO (dark at the base), B per-part random (scan), A sway
                float hy = Mathf.Clamp01(v[i].y / h);
                c[i] = new Color(0.12f * Mathf.SmoothStep(0.55f, 1f, hy), Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(hy)), c[i].b, Mathf.SmoothStep(0.05f, 1f, hy) * 0.85f);
            }
        }
        // winding: some of these FBX come in with the triangles wound against their normals (front faces culled, the
        // piles drew as their flat base discs, the lantern near black): flip when most faces disagree with the normals
        int against = 0, tris = t.Count / 3;
        for (int k = 0; k < t.Count; k += 3)
        {
            var fn = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
            if (Vector3.Dot(fn, n[t[k]] + n[t[k + 1]] + n[t[k + 2]]) < 0) against++;
        }
        if (against > tris / 2) { for (int k = 0; k < t.Count; k += 3) { int tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; } }
        var mesh = new Mesh { name = "SP_MX_" + key, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(t, 0);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        var path = $"{Gen}/SP_MX_{key}.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
        var src = new MXSrc { name = key, mesh = mesh, mat = mat, size = hi - lo, tris = t.Count / 3 };
        src.size.y *= squashY;
        Log($"MX {key}: {src.size.x:0.00} x {src.size.y:0.00} x {src.size.z:0.00} m, {src.tris} tris, {against} faces against normals{(against > tris / 2 ? " (flipped)" : "")} ({Path.GetFileName(fbxPath)})");
        return src;
    }

    // dead shrub atlas with the opacity scan in alpha (the Veg shader clips on _BaseMap.a)
    static Texture2D MXShrubAlbedo()
    {
        var path = MXTexDir + "SP_MX_Shrub_BaseOpacity_v2.png";
        if (!File.Exists(ProjAbs(path)))
        {
            var bc = MXFind("dead_shrubs_vdknafgha_mid", "_BaseColor.jpg"); var op = MXFind("dead_shrubs_vdknafgha_mid", "_Opacity.jpg");
            if (bc == null || op == null) { Log("MX shrub maps missing"); return null; }
            var a = new Texture2D(2, 2, TextureFormat.RGBA32, false); a.LoadImage(File.ReadAllBytes(ProjAbs(bc)));
            var o = new Texture2D(2, 2, TextureFormat.RGBA32, false); o.LoadImage(File.ReadAllBytes(ProjAbs(op)));
            if (a.width != o.width || a.height != o.height) { Log("MX shrub maps differ in size"); return null; }
            // LoadImage turns a jpg into RGB24: write into a fresh RGBA32 texture or the alpha is lost
            var pa = a.GetPixels32(); var po = o.GetPixels32();
            for (int i = 0; i < pa.Length; i++) pa[i].a = po[i].r;
            var outT = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
            outT.SetPixels32(pa); outT.Apply();
            Directory.CreateDirectory(ProjAbs(MXTexDir));
            File.WriteAllBytes(ProjAbs(path), outT.EncodeToPNG());
            Object.DestroyImmediate(a); Object.DestroyImmediate(o); Object.DestroyImmediate(outT);
            AssetDatabase.ImportAsset(path);
        }
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp && (!imp.alphaIsTransparency || !imp.mipMapsPreserveCoverage || imp.alphaSource != TextureImporterAlphaSource.FromInput || Mathf.Abs(imp.alphaTestReferenceValue - 0.25f) > 0.01f))
        {
            imp.alphaIsTransparency = true; imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.25f;
            imp.SaveAndReimport();
        }
        return Tex(path);
    }

    // albedo with an alpha cut from the black padding outside the UV islands (scans with no opacity map: the fallen
    // branch litter skirts drew their black padding as dark sails)
    static Texture2D MXPaddingAlpha(string folder, string outName)
    {
        var path = MXTexDir + outName + ".png";
        if (!File.Exists(ProjAbs(path)))
        {
            var bc = MXFind(folder, "_BaseColor.jpg"); if (bc == null) return null;
            var a = new Texture2D(2, 2, TextureFormat.RGBA32, false); a.LoadImage(File.ReadAllBytes(ProjAbs(bc)));
            var pa = a.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
            {
                float l = (pa[i].r * 0.3f + pa[i].g * 0.55f + pa[i].b * 0.15f) / 255f;
                pa[i].a = (byte)(255 * Mathf.SmoothStep(0f, 1f, (l - 0.035f) / 0.05f));
            }
            var outT = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
            outT.SetPixels32(pa); outT.Apply();
            Directory.CreateDirectory(ProjAbs(MXTexDir));
            File.WriteAllBytes(ProjAbs(path), outT.EncodeToPNG());
            Object.DestroyImmediate(a); Object.DestroyImmediate(outT);
            AssetDatabase.ImportAsset(path);
        }
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp && (!imp.alphaIsTransparency || imp.alphaSource != TextureImporterAlphaSource.FromInput))
        { imp.alphaIsTransparency = true; imp.alphaSource = TextureImporterAlphaSource.FromInput; imp.SaveAndReimport(); }
        return Tex(path);
    }

    static Material MXProp(string name, string folder, Color tint, float smooth, float desat, float snowBias, float melt)
    {
        var bc = MXFind(folder, "_BaseColor.jpg"); var nm = MXFind(folder, "_Normal.jpg");
        var m = Prop(name, bc != null ? Tex(bc) : null, MXNormal(nm), tint, smooth, desat);
        if (!m) return null;
        m.SetFloat("_VertexFrost", 0f); m.SetFloat("_SnowBias", snowBias); m.SetFloat("_GroundMelt", melt);
        m.SetColor("_TintVar", new Color(1.04f, 0.98f, 0.92f, 0.12f));
        return m;
    }

    // Fire box of a stone lantern mesh (origin at the bottom centre): the widest band in the upper half is the roof, the
    // fire box sits right under it. Returns (centre height, core diameter) in mesh metres.
    static (float h, float d) MXFireBox(Mesh m)
    {
        const int Bins = 60;
        var v = m.vertices; float top = m.bounds.max.y;
        var rad = new float[Bins];
        foreach (var p in v) { int b = Mathf.Clamp((int)(p.y / top * Bins), 0, Bins - 1); rad[b] = Mathf.Max(rad[b], new Vector2(p.x, p.z).magnitude); }
        int roof = Bins / 2;
        for (int b = Bins / 2; b < Bins; b++) if (rad[b] > rad[roof]) roof = b;
        int bottom = roof;
        while (bottom > Bins / 3 && rad[bottom - 1] > rad[roof] * 0.7f) bottom--;
        int lo = Mathf.Max(0, bottom - 4); float box = 1e9f;
        for (int b = lo; b < bottom; b++) if (rad[b] > 0) box = Mathf.Min(box, rad[b]);
        if (box > 1e8f) box = rad[roof] * 0.3f;
        float h = (lo + bottom) * 0.5f / Bins * top;
        return (h, Mathf.Clamp(box * 0.32f, 0.05f, 0.3f));
    }

    // ------------------------------------------------------------------ north-west ledge (layout pass 2, ss5 top-left)
    // A raised rocky ledge in two tiers that runs up the west side behind the wall line and curves round the north-west
    // corner along the north edge, so most of its rock face points south / south-east at the game camera. Snow on the
    // tops and the bench, lighter scanned rock on the faces, big pines and dead trees on the tiers leaning toward the
    // arena, boulders half sunk along the lips. Everything stands outside |x| < 56, |z| < 44; no colliders.
    // Lip line (the foot of the lower face), walked so the back of the ledge is on the left.
    static readonly Vector2[] LedgeLip =
    {
        new Vector2(-59.6f, -16f), new Vector2(-59.4f, 10f), new Vector2(-59.0f, 30f), new Vector2(-58.0f, 39.5f),
        new Vector2(-55.0f, 44.6f), new Vector2(-49f, 46.0f), new Vector2(-38f, 46.3f), new Vector2(-24f, 46.5f), new Vector2(-8f, 46.2f),
    };
    // signed distance behind the lip (+ = back, toward the outside) and arc length along it
    static float LedgeDist(float x, float z, out float arc, out float total)
    {
        var p = new Vector2(x, z); float best = 1e9f, sgn = 1f, acc = 0f; arc = 0f;
        for (int i = 0; i + 1 < LedgeLip.Length; i++)
        {
            var a = LedgeLip[i]; var b = LedgeLip[i + 1]; var ab = b - a; float len = ab.magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / (len * len));
            var q = a + ab * t; float d = (p - q).magnitude;
            if (d < best) { best = d; var left = new Vector2(-ab.y, ab.x) / len; sgn = Vector2.Dot(p - q, left) >= 0f ? 1f : -1f; arc = acc + len * t; }
            acc += len;
        }
        total = acc;
        // wobble the lip so the face is not a ruled line
        float w = (Mathf.PerlinNoise(arc * 0.07f, 3.3f) - 0.5f) * 1.4f + (Mathf.PerlinNoise(arc * 0.37f, 8.1f) - 0.5f) * 0.8f;
        return best * sgn - w;
    }
    // height above the ground snow, 0..~3.8 m: lower face over 1.1 m up to a bench at ~1.8 m, second face 2.6-4 m
    // further back up to the top at ~3.5 m; both ends taper over 8 m; it fades before the north cliff line
    static float LedgeH(float x, float z)
    {
        float d = LedgeDist(x, z, out float arc, out float total);
        if (d <= 0f) return 0f;
        float n1 = Mathf.PerlinNoise(arc * 0.05f + 7f, 1.1f), n2 = Mathf.PerlinNoise(arc * 0.09f + 2f, 5.7f);
        float h1 = 1.5f + 0.7f * n1, h2 = h1 + 1.4f + 0.8f * n2;
        float back = 2.6f + 1.4f * n2;
        float h = h1 * Mathf.SmoothStep(0f, 1f, d / 1.1f) + (h2 - h1) * Mathf.SmoothStep(0f, 1f, (d - back) / 1.1f);
        h += (Mathf.PerlinNoise(x * 0.21f + 4f, z * 0.21f) - 0.5f) * 0.35f * Mathf.Clamp01(d / 1.5f);
        h *= Mathf.SmoothStep(0f, 1f, arc / 8f) * Mathf.SmoothStep(0f, 1f, (total - arc) / 8f);
        h *= Mathf.SmoothStep(0f, 1f, (CliffZ - 0.5f - z) / 2.5f) * Mathf.SmoothStep(0f, 1f, (x + 97f) / 5f);
        return Mathf.Max(0f, h);
    }
    static float PlateauY(float x, float z) { float h = LedgeH(x, z); return MXGround(x, z) + (h > 0.02f ? h : -0.3f); }
    static float PlateauS(float x, float z) => Mathf.Clamp01(LedgeH(x, z) / 1.5f);

    // direction from a point on the ledge toward the arena (for turning crowns)
    static Vector2 LedgeInward(float x, float z)
    {
        var p = new Vector2(x, z); var c = new Vector2(Mathf.Clamp(x, -HX + 6f, HX - 6f), Mathf.Clamp(z, -HZ + 6f, HZ - 6f));
        var d = c - p; return d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.right;
    }

    static void BuildWestPlateau(System.Func<float, float, float, bool> kept, System.Func<float, float, float, bool> river, System.Random xr)
    {
        float Rx(float a, float b) => a + (float)xr.NextDouble() * (b - a);
        var grp = Group("WestPlateau");
        // scanned rock (Poly Haven rock_face_01), lifted toward the ss5 grey; world-up snow on the tops and the bench
        var mat = Prop("SP_MX_Plateau", Tex(ArchTex + "rock_face_01_diff_1k.jpg", false, false, false, 1024), Tex(ArchTex + "rock_face_01_nor_gl_1k.jpg", true, false, false, 1024),
                       new Color(1.35f, 1.31f, 1.26f), 0.12f, 0.55f);
        if (!mat) return;
        mat.SetFloat("_VertexFrost", 0f); mat.SetFloat("_SnowBias", 0.3f); mat.SetFloat("_GroundMelt", 0f); mat.SetFloat("_NormalStrength", 1.4f);
        mat.SetFloat("_Triplanar", 1f); mat.SetFloat("_TriScale", 4f);   // gnd5 (QA #11): world triplanar, the scan was smeared along its UVs
        // heightfield over the corner, 0.45 m cells; cells under the ground snow are skipped
        const float C = 0.45f, X0 = -98f, X1 = -4f, Z0 = -24f, Z1 = 54f;
        int nx = Mathf.CeilToInt((X1 - X0) / C), nz = Mathf.CeilToInt((Z1 - Z0) / C);
        var v = new List<Vector3>((nx + 1) * (nz + 1)); var uv = new List<Vector2>(v.Capacity); var hIn = new List<float>(v.Capacity);
        for (int j = 0; j <= nz; j++)
        for (int i = 0; i <= nx; i++)
        {
            float x = X0 + i * C, z = Z0 + j * C, h = LedgeH(x, z), y = MXGround(x, z) + (h > 0.02f ? h : -0.3f);
            v.Add(new Vector3(x, y, z)); uv.Add(new Vector2(x + z, y * 1.4f + (x - z) * 0.3f) / 3.2f); hIn.Add(h);
        }
        var t = new List<int>();
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
            if (hIn[a] < 0.02f && hIn[b] < 0.02f && hIn[d] < 0.02f && hIn[e] < 0.02f) continue;
            t.Add(a); t.Add(d); t.Add(b); t.Add(b); t.Add(d); t.Add(e);
        }
        var m = new Mesh { name = "SP_MX_Ledge", indexFormat = IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        foreach (var old in new[] { "SP_MX_Plateau_0", "SP_MX_Plateau_1" }) AssetDatabase.DeleteAsset($"{Gen}/{old}.asset");
        var path = $"{Gen}/SP_MX_Ledge.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
        var lg = new GameObject("Ledge"); lg.transform.SetParent(grp, false);
        lg.AddComponent<MeshFilter>().sharedMesh = m;
        var lr = lg.AddComponent<MeshRenderer>(); lr.sharedMaterial = mat; lr.shadowCastingMode = ShadowCastingMode.On;
        GameObjectUtility.SetStaticEditorFlags(lg, StaticEditorFlags.BatchingStatic);
        int tris = t.Count / 3;

        float totalLen = 0f; for (int i = 0; i + 1 < LedgeLip.Length; i++) totalLen += Vector2.Distance(LedgeLip[i], LedgeLip[i + 1]);
        Vector2 At(float s, out Vector2 back)
        {
            for (int i = 0; i + 1 < LedgeLip.Length; i++)
            {
                var a = LedgeLip[i]; var b = LedgeLip[i + 1]; float len = Vector2.Distance(a, b);
                if (s <= len || i + 2 == LedgeLip.Length) { var ab = (b - a) / len; back = new Vector2(-ab.y, ab.x); return a + ab * Mathf.Min(s, len); }
                s -= len;
            }
            back = Vector2.left; return LedgeLip[0];
        }
        // boulders half sunk along both lips, low rocks at the foot outside the arena
        int rocks = 0, trees = 0;
        for (float s = 6f; s < totalLen - 6f; s += Rx(3.5f, 5.5f))
        {
            var p = At(s, out var back);
            foreach (float off in new[] { Rx(0.2f, 0.8f), Rx(3.2f, 4.2f) })
            {
                var q = p + back * off; float y = PlateauY(q.x, q.y);
                if (Place(xr.NextDouble() < 0.5 ? "PH:boulder_01" : "PH:rock_09", q.x, q.y, Rx(0, 360), Rx(0.7f, 1.25f), grp, false, Height(q.x, q.y) - (y - 0.8f))) rocks++;
            }
            if (xr.NextDouble() < 0.4)
            {
                var q = p - back * Rx(1.0f, 1.8f);
                if (InArena(q.x, q.y) || kept(q.x, q.y, 0.3f) || river(q.x, q.y, 7f)) continue;
                if (Place("PH:rock_07", q.x, q.y, Rx(0, 360), Rx(0.5f, 0.9f), grp, false, 0.15f)) rocks++;
            }
        }
        // trees on the bench and the top, crowns turned toward the arena
        GameObject Tree(string model, Vector2 q, float scale)
        {
            float y = PlateauY(q.x, q.y) - 0.25f;
            var go = Place(model, q.x, q.y, Rx(0, 360), scale, grp, !model.Contains("Dead_Tree"), Height(q.x, q.y) - y);   // tr21: dead conifers cast no shadow (their straight trunks drew 30 m lines across the yard)
            if (!go) return null;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var off = b.center - go.transform.position; off.y = 0f;
            var inw = LedgeInward(q.x, q.y);
            if (off.magnitude > 0.4f)
            {
                float cur = Mathf.Atan2(-off.z, off.x) * Mathf.Rad2Deg, want = Mathf.Atan2(-inw.y, inw.x) * Mathf.Rad2Deg;
                go.transform.rotation = Quaternion.Euler(0, (want - cur) + Rx(-25f, 25f), 0) * go.transform.rotation;   // keep the FBX root tilt (single-mesh Sketchfab dead trees lay flat: the white disc by the yard)
            }
            trees++;
            return go;
        }
        string[] pines = SkLedgePines, deads = SkDeads;   // tr1: downloaded Sketchfab trees
        int k = 0;
        for (float s = 9f; s < totalLen - 8f; s += Rx(6.5f, 9.5f), k++)
        {
            var p = At(s, out var back);
            var q = p + back * (k % 2 == 0 ? Rx(1.6f, 2.4f) : Rx(4.5f, 6.5f));   // alternate bench / top
            if (q.y > CliffZ - 1.5f) q.y = CliffZ - 1.5f;
            if (k % 3 == 1) Tree(deads[xr.Next(deads.Length)], q, Rx(1.0f, 1.25f));
            else Tree(pines[xr.Next(pines.Length)], q, Rx(0.95f, 1.3f));
        }
        Log($"MX north-west ledge: {tris / 1000}k tris, lip {totalLen:0} m, {rocks} rocks, {trees} trees");
    }

    static float MXGround(float x, float z) { float d = SampleMask(mDrift, x, z); return Height(x, z) + d * d * 0.55f; }

    static partial void BuildMegascans()
    {
        if (!Directory.Exists(ProjAbs(MXDir))) { Log("MX no Megascans folder"); return; }
        var xr = new System.Random(5531);   // own stream: the main rng sequence stays as it was
        float Rx(float a, float b) => a + (float)xr.NextDouble() * (b - a);

        // ------------------------------------------------------------------ materials (ss5: brown-sepia brush, grey stone, snow only on top)
        var shrubN = MXNormal(MXFind("dead_shrubs_vdknafgha_mid", "_Normal.jpg"));
        var shrubMat = Veg("SP_MX_Shrub", MXShrubAlbedo(), shrubN, new Color(1.45f, 1.3f, 1.15f), true, 0f, 0.25f);   // the atlas is dark umber: lift to ss5 mid brown
        if (shrubMat)
        {
            shrubMat.SetFloat("_Cutoff", 0.25f); shrubMat.SetFloat("_SnowBias", -0.5f); shrubMat.SetFloat("_AOStrength", 0.6f);
            shrubMat.SetFloat("_GroundMelt", 0.05f); shrubMat.SetColor("_TipCol", new Color(0.7f, 0.66f, 0.6f, 0f));
            shrubMat.SetFloat("_Wrap", 0.45f); shrubMat.SetFloat("_Smoothness", 0.08f); shrubMat.SetFloat("_Trample", 0.25f);
        }
        var trunkMat = MXProp("SP_MX_PineTrunk", "pine_tree_trunk_theqbbpda_mid", new Color(0.62f, 0.56f, 0.52f), 0.1f, 0.35f, -0.8f, 0.3f);   // tr30 (QA #10): darker, no snow on the trunks (white stick in 14)
        var deadMat = MXProp("SP_MX_DeadPine", "dead_pine_tree_trunk_tksvbajda_mid", new Color(0.82f, 0.77f, 0.72f), 0.1f, 0.35f, -0.3f, 0.2f);
        var lanternMat = MXProp("SP_MX_Lantern", "japanese_stone_lantern_ufnscjdga_mid", new Color(1.45f, 1.42f, 1.38f), 0.12f, 0.7f, 0.05f, 0.15f);
        var wallMat = MXProp("SP_MX_StoneWall", "japanese_stone_wall_ubnnaghfa_mid", new Color(0.98f, 0.95f, 0.91f), 0.12f, 0.5f, -0.1f, 0.15f);
        var groundMat = MXProp("SP_MX_SnowGround", "snowy_ground_vefkajufa_mid", new Color(0.5f, 0.51f, 0.53f), 0.3f, 0.5f, -0.8f, 0.6f);
        var rocksMat = MXProp("SP_MX_SnowyRocks", "snowy_rocks_pile_vcykbjsva_mid", new Color(0.8f, 0.8f, 0.81f), 0.25f, 0.5f, -0.6f, 0.6f);
        var rockyMat = MXProp("SP_MX_RockySnow", "rocky_snow_pile_xfroec0qx_mid", new Color(0.8f, 0.8f, 0.81f), 0.25f, 0.5f, -0.6f, 0.6f);
        AssetDatabase.SaveAssets();

        // ------------------------------------------------------------------ meshes
        var shrubs = new List<MXSrc>();
        foreach (var vr in "ABCDEFGH")
        {
            var s = MXLoad("Shrub" + vr, MXFind("dead_shrubs_vdknafgha_mid", "_LOD1.fbx", "Var" + vr), shrubMat, 1);
            if (s != null) shrubs.Add(s);
        }
        MXSrc branches = null; // ms3: the litter patch read as rusty folded cards from the game camera; left out
        var trunk = MXLoad("PineTrunk", MXFind("pine_tree_trunk_theqbbpda_mid", ".fbx"), trunkMat, 0);
        var dead = MXLoad("DeadPine", MXFind("dead_pine_tree_trunk_tksvbajda_mid", ".fbx"), deadMat, 0);
        var lantern = MXLoad("Lantern", MXFind("japanese_stone_lantern_ufnscjdga_mid", ".fbx"), lanternMat, 0);
        var wall = MXLoad("StoneWall", MXFind("japanese_stone_wall_ubnnaghfa_mid", ".fbx"), wallMat, 0);
        var snowGround = MXLoad("SnowGround", MXFind("snowy_ground_vefkajufa_mid", ".fbx"), groundMat, 0);
        var rocks = MXLoad("SnowyRocks", MXFind("snowy_rocks_pile_vcykbjsva_mid", ".fbx"), rocksMat, 0);
        var rocky = MXLoad("RockySnow", MXFind("rocky_snow_pile_xfroec0qx_mid", ".fbx"), rockyMat, 0);
        // landmarks (lead, 11-11: Fab assets instead of self-made where possible)
        var bridge = MXLoad("Bridge", MXFind("japanese_arched_wooden_bridge_veqhdjt_mid", ".fbx"),
            MXProp("SP_MX_Bridge", "japanese_arched_wooden_bridge_veqhdjt_mid", new Color(1.2f, 1.1f, 1.0f), 0.12f, 0.35f, 0.8f, 0.1f), 0);   // tr21/22: snow on the deck planks
        { var dm = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/SP_MX_Bridge.mat"); if (dm) { dm.SetFloat("_SnowCap", 0.92f); EditorUtility.SetDirty(dm); }   /* tr30 (QA #14): 0.75 -> 0.92 */ else Log("MX bridge: no SP_MX_Bridge.mat for the snow cap"); }   // tr23: bias alone left the planks bare - patchy cap ~60% white
        // tr14 (ss5 hero prop): vermilion railings on both sides of the deck; faded lacquer, snow on the top rail
        var railMat = MXProp("SP_MX_BridgeRail", "japanese_bridge_railing_vdkfcdc_mid", new Color(1.12f, 0.62f, 0.62f), 0.3f, 0.3f, 0.1f, 0f);
        var rail = MXLoad("BridgeRail", MXFind("japanese_bridge_railing_vdkfcdc_mid", ".fbx"), railMat, 0);
        if (rail != null && bridge != null)
        {
            // the scan is straight; lift it onto the deck arch (deck top: ~0.35 m at the ends, ~0.69 m mid, baked mesh metres)
            var rv = rail.mesh.vertices; float half = Mathf.Max(0.1f, rail.size.x * 0.5f);
            for (int i = 0; i < rv.Length; i++) { float u = Mathf.Clamp(rv[i].x / half, -1f, 1f); rv[i].y += 0.33f + 0.34f * (1f - u * u); }
            rail.mesh.vertices = rv; rail.mesh.RecalculateBounds(); rail.mesh.RecalculateNormals(); EditorUtility.SetDirty(rail.mesh);
        }
        var lantern2 = MXLoad("Lantern2", MXFind("japanese_stone_lantern_veniffjdb_mid", ".fbx"),
            MXProp("SP_MX_Lantern2", "japanese_stone_lantern_veniffjdb_mid", new Color(1.45f, 1.42f, 1.38f), 0.12f, 0.7f, 0.05f, 0.15f), 0);
        var statue = MXLoad("FoxStatue", MXFind("japanese_statue_venlfjjcb_mid", ".fbx"),
            MXProp("SP_MX_FoxStatue", "japanese_statue_venlfjjcb_mid", new Color(1.45f, 1.42f, 1.38f), 0.12f, 0.7f, 0.0f, 0.15f), 0);
        var pillar = MXLoad("StonePillar", MXFind("japanese_stone_pillar_uddocimqx_mid", ".fbx"),
            MXProp("SP_MX_StonePillar", "japanese_stone_pillar_uddocimqx_mid", new Color(1.45f, 1.42f, 1.38f), 0.12f, 0.7f, 0.0f, 0.15f), 0);
        AssetDatabase.SaveAssets();

        // the self-made walkway bridge is no longer placed (Architecture.cs); drop it if an older Architecture still does
        var arch = root.Find("Architecture");
        if (arch && bridge != null)
            foreach (Transform ch in arch)
                if (ch.name.StartsWith("SP_Arch_Bridge")) { Object.DestroyImmediate(ch.gameObject); Log("MX removed the self-made Arch/SP_Arch_Bridge"); break; }

        // ------------------------------------------------------------------ keep-out: props and architecture already placed
        var keep = new List<Rect>();
        void KeepFrom(Transform grp)
        {
            if (!grp) return;
            foreach (var r in grp.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial && (r.sharedMaterial.shader.name.Contains("Blood") || r.sharedMaterial.shader.name.Contains("Blob") || r.sharedMaterial.shader.name.Contains("LightCone"))) continue;
                var b = r.bounds;
                if (b.size.y < 0.2f || b.size.x > 60f || b.size.z > 60f) continue;
                keep.Add(new Rect(b.min.x - 0.2f, b.min.z - 0.2f, b.size.x + 0.4f, b.size.z + 0.4f));
            }
        }
        KeepFrom(root.Find("Vignettes")); KeepFrom(root.Find("Architecture"));
        bool Kept(float x, float z, float pad = 0f)
        {
            foreach (var k in keep) if (x > k.xMin - pad && x < k.xMax + pad && z > k.yMin - pad && z < k.yMax + pad) return true;
            return false;
        }
        bool River(float x, float z, float m = 7f) => DistanceToRiver(x, z) < m;
        float Inside(float x, float z) => Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z));   // metres inside the edge (negative outside)
        bool Cliff(float x, float z) => z > CliffZ - 2.5f;

        // ------------------------------------------------------------------ GameObject props
        var props = Group("MegascansProps");
        int propTris = 0, propCount = 0;
        GameObject PlaceMX(MXSrc s, float x, float z, float yaw, Vector3 scale, bool shadows, float sink)
        {
            if (s == null) return null;
            // sit on the lowest ground under the footprint so no corner floats
            var rot = Quaternion.Euler(0, yaw, 0);
            float y = MXGround(x, z);
            for (int k = 0; k < 4; k++)
            {
                var o = rot * new Vector3(((k & 1) == 0 ? -0.45f : 0.45f) * s.size.x * scale.x, 0, ((k & 2) == 0 ? -0.45f : 0.45f) * s.size.z * scale.z);
                y = Mathf.Min(y, MXGround(x + o.x, z + o.z));
            }
            var go = new GameObject("SP_MX_" + s.name);
            go.transform.SetParent(props, false);
            go.transform.SetPositionAndRotation(new Vector3(x, y - sink, z), rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = s.mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            var b = mr.bounds; keep.Add(new Rect(b.min.x - 0.15f, b.min.z - 0.15f, b.size.x + 0.3f, b.size.z + 0.3f));
            propTris += s.tris; propCount++;
            return go;
        }

        // pine trunks framing east / west (behind the side walls), a few just outside where there is no wall, and low
        // ones on the camera side
        int trunks = 0;
        if (trunk != null)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                for (float z = -HZ - 6f; z < HZ + 5f; z += Rx(8f, 13f))
                {
                    float x = side * (HX + Rx(3.6f, 9f));
                    if (BorderBlocked(x, z) || River(x, z)) continue;
                    { var tg = PlaceMX(trunk, x, z, Rx(0, 360), new Vector3(Rx(0.85f, 1.15f), Rx(0.95f, 1.25f), Rx(0.85f, 1.15f)), false, 0.15f);   /* tr30: Y 1.2-1.9 stretched the bark into white streaks (14) */   /* tr13: no shadow - the tall straight trunks drew the long thin line shadows across the lantern yard */ if (tg) { trunks++; if (PlateauS(x, z) > 0.3f) tg.transform.position = new Vector3(x, PlateauY(x, z) - 0.2f, z); } }   // west ones stand on the ledge
                }
                foreach (float z in new[] { Rx(-42f, -38.5f), Rx(-1.5f, 3.5f) })
                {
                    float x = side * (HX + Rx(1.0f, 1.8f));
                    if (BorderBlocked(x, z) || River(x, z) || Kept(x, z, 0.4f)) continue;
                    if (PlaceMX(trunk, x, z, Rx(0, 360), new Vector3(Rx(0.8f, 1f), Rx(1.0f, 1.4f), Rx(0.8f, 1f)), false, 0.15f)) trunks++;   // tr13: no shadow (line shadows)
                }
            }
            for (float x = -HX - 6f; x < HX + 6f; x += Rx(14f, 21f))
            {
                float z = -HZ - Rx(3.5f, 7f);
                if (River(x, z)) continue;
                if (PlaceMX(trunk, x, z, Rx(0, 360), new Vector3(Rx(0.85f, 1.1f), Rx(0.85f, 1.25f), Rx(0.85f, 1.1f)), false, 0.15f)) trunks++;
            }
        }

        // Japanese stone wall runs (low, ~0.65 m): north between the bridge and the gate, south in three pieces,
        // the south ends of the side walls
        int walls = 0;
        void WallRun(Vector2 a, Vector2 b, float yaw)
        {
            if (wall == null) return;
            float len = Vector2.Distance(a, b), step = wall.size.x * 0.97f;
            for (float s = 0; s + step * 0.5f < len; s += step)
            {
                if (xr.NextDouble() < 0.14) { s += step * Rx(0.2f, 0.8f); continue; }   // fallen gaps
                var p = Vector2.Lerp(a, b, (s + step * 0.5f) / len);
                if (River(p.x, p.y) || Kept(p.x, p.y)) continue;
                if (PlaceMX(wall, p.x + Rx(-0.08f, 0.08f), p.y + Rx(-0.08f, 0.08f), yaw + Rx(-2.5f, 2.5f) + (xr.NextDouble() < 0.5 ? 180f : 0f), Vector3.one * Rx(0.95f, 1.05f), true, 0.1f)) walls++;
            }
        }
        // WallRun(new Vector2(-3.5f, HZ + 1.3f), new Vector2(31.5f, HZ + 1.3f), 0f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)
        // WallRun(new Vector2(-52f, -HZ - 1.5f), new Vector2(-31f, -HZ - 1.5f), 0f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)
        // WallRun(new Vector2(-14f, -HZ - 1.5f), new Vector2(5f, -HZ - 1.5f), 0f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)
        // WallRun(new Vector2(23f, -HZ - 1.5f), new Vector2(51f, -HZ - 1.5f), 0f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)
        // WallRun(new Vector2(-HX - 1.6f, -HZ - 0.5f), new Vector2(-HX - 1.6f, -37.4f), 90f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)
        // WallRun(new Vector2(HX + 1.6f, -HZ - 0.5f), new Vector2(HX + 1.6f, -33.4f), 90f);   // tr44 (Kido: 1자돌 이상해) - straight wall runs out, irregular rock clusters instead (BorderRocks)

        // stone lanterns next to vignettes: emissive core + the shared warm spot (unshadowed)
        var lanternSpots = new List<Vector2>();
        // warm core: SP_LanternGlow at a lower HDR level (x6 bloomed to a white box through the window, lt4/ms3)
        Material glowMat = null;
        if (mats.TryGetValue("SP_LanternGlow", out var baseGlow) && baseGlow)
            glowMat = Mat("SP_MX_LanternCore", baseGlow.shader.name, m => { m.CopyPropertiesFromMaterial(baseGlow); m.SetColor("_EmissionColor", new Color(1f, 0.42f, 0.12f) * 2.2f); m.SetColor("_Tint", new Color(1f, 0.6f, 0.3f)); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive; });
        void LanternAt(MXSrc src, Vector2 p, float s)
        {
            if (src == null) return;
            if (River(p.x, p.y) || Kept(p.x, p.y, 0.3f)) { Log($"MX lantern at {p} blocked"); return; }
            var go = PlaceMX(src, p.x, p.y, Rx(0, 360), Vector3.one * s, true, 0.06f);
            if (!go) return;
            if (glowMat)
            {
                // a small warm core inside the fire box (found from the mesh profile), well inside the window openings
                var (fh, fr) = MXFireBox(src.mesh);
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "GlowCore";
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.position = go.transform.position + Vector3.up * fh * s;
                g.transform.localScale = Vector3.one * fr * s * 0.9f;
                g.transform.SetParent(go.transform, true);
                var gr = g.GetComponent<MeshRenderer>(); gr.sharedMaterial = glowMat; gr.shadowCastingMode = ShadowCastingMode.Off; gr.receiveShadows = false;
            }
            var l = Lantern(go, false);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;   // the spot is unshadowed, the moon shadow stays
            if (l) l.name = "MXLanternSpot";
            lanternSpots.Add(p);
        }
        foreach (var p in new[] { new Vector2(-49.2f, 28.2f), new Vector2(11.8f, -35.2f), new Vector2(36f, 33f), new Vector2(50.2f, 3.8f) })
            LanternAt(lantern, p, Rx(0.56f, 0.64f));
        foreach (var p in new[] { new Vector2(-43.5f, -8f), new Vector2(2.5f, 40f) })
            LanternAt(lantern2, p, Rx(1.0f, 1.15f));

        // Megascans arched bridge where the self-made one stood: the 4.3 m scan stretched to a ~10 m span (decorative,
        // north edge, outside the play area, no collider)
        // Layout pass 2: two arched bridges over the frozen river inside the arena (ly4: on the side wall lines the purple
        // boundary cut them): west x -40 by the corpse spot, east x 48 by the cart; long enough to land on both banks, deck
        // kept low (arch ~0.9 m), no collider
        int bridges = 0;
        if (bridge != null)
            foreach (float bx in new[] { -66f, 61f })   // tr17: west one lines up with the 솟을삼문 on the south bank   // 11-12 (Kido: nothing arched / climbable inside the arena): just outside the west / east boundary where the river leaves
            {
                // river centre and direction at x = bx (polyline; arc length ~ the Ice builder's s)
                float acc = 0f; Vector2 c = Vector2.zero, d = Vector2.right; float sAt = -1f;
                for (int i = 0; i + 1 < RiverPath.Length; i++)
                {
                    var a = RiverPath[i]; var b = RiverPath[i + 1]; float len = Vector2.Distance(a, b);
                    if ((a.x - bx) * (b.x - bx) <= 0f && Mathf.Abs(b.x - a.x) > 1e-3f)
                    { float t = (bx - a.x) / (b.x - a.x); c = Vector2.Lerp(a, b, t); d = (b - a).normalized; sAt = acc + len * t; break; }
                    acc += len;
                }
                if (sAt < 0f) { Log($"MX bridge: no river at x {bx}"); continue; }
                float hw = RiverHalfWidth(sAt, bx);
                float span = 2f * hw + 4.5f;   // ice plus the bank lips on both sides
                var n = new Vector2(-d.y, d.x);
                float yaw = Mathf.Atan2(-n.y, n.x) * Mathf.Rad2Deg;   // local +X (the span) across the river
                // tr14: ~2.2x the scan so it reads big at the game camera (ss5); the span still lands on both banks
                const float BigY = 2.2f, BigZ = 2.2f;
                var go = PlaceMX(bridge, c.x, c.y, yaw, new Vector3(span / bridge.size.x, BigY, BigZ), false, 0.05f);   // tr22: the deck shadow was the dark block beside it (view 15); railings keep theirs
                if (go && rail != null)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        var rg = PlaceMX(rail, c.x, c.y, yaw, Vector3.one, true, 0f);
                        if (!rg) continue;
                        rg.transform.SetParent(go.transform, false);
                        rg.transform.localRotation = Quaternion.identity;
                        rg.transform.localPosition = new Vector3(0f, 0f, side * (bridge.size.z * 0.5f - 0.08f));
                        rg.transform.localScale = new Vector3(bridge.size.x / rail.size.x, 1f, 1f);
                    }
                if (go) { bridges++; Log($"MX bridge at ({c.x:0.0}, {c.y:0.0}) span {span:0.0} m (river half width {hw:0.0}), yaw {yaw:0}"); }
            }

        BuildWestPlateau(Kept, River, xr);

        // fox statues: a pair flanking the open (arena) side of the lantern yard, facing the centre
        int statues = 0;
        if (statue != null)
        {
            var yard = new Vector2(Clusters[0].x, Clusters[0].y);
            var outDir = (-yard).normalized; var across = new Vector2(-outDir.y, outDir.x);
            float faceYaw = Mathf.Atan2(outDir.x, outDir.y) * Mathf.Rad2Deg;
            foreach (float sd in new[] { -1f, 1f })
            {
                var p = yard + outDir * (Clusters[0].z * 0.55f) + across * sd * 2.0f;
                if (River(p.x, p.y) || Kept(p.x, p.y, 0.2f)) { Log($"MX statue at {p} blocked"); continue; }
                if (PlaceMX(statue, p.x, p.y, faceYaw, Vector3.one * 1.2f, true, 0.04f)) statues++;
            }
        }
        // stone pillars: a row on the arena side of the gate (gap at the opening) and a few along the north edge
        int pillars = 0;
        if (pillar != null)
        {
            for (float x = 33.5f; x < 51f; x += 2.6f)
            {
                if (Mathf.Abs(x - 42f) < 2.4f || Kept(x, 44.7f, 0.15f)) continue;
                if (PlaceMX(pillar, x + Rx(-0.15f, 0.15f), 44.7f + Rx(-0.15f, 0.15f), Rx(-8f, 8f), Vector3.one * Rx(1.05f, 1.25f), true, 0.04f)) pillars++;
            }
            foreach (float x in new[] { -4.6f, 9f, 20f, 32.6f })
            {
                float z = HZ + Rx(0.3f, 0.6f);
                if (River(x, z) || Kept(x, z, 0.15f)) continue;
                if (PlaceMX(pillar, x, z, Rx(0, 360), Vector3.one * Rx(1.0f, 1.2f), true, 0.04f)) pillars++;
            }
        }

        // ------------------------------------------------------------------ instanced detail
        var sgo = new GameObject("MegascansScatter"); sgo.transform.SetParent(root, false);
        sgo.AddComponent<LookStudyInstancer>();
        var sc = sgo.AddComponent<SPScatter>();
        var setOf = new Dictionary<MXSrc, SPScatter.Set>();
        long scTris = 0; int scCount = 0;
        void Add(MXSrc s, float x, float z, float scale, float sink, float yaw = -1f)
        {
            if (s == null) return;
            if (!setOf.TryGetValue(s, out var set)) { set = new SPScatter.Set { mesh = s.mesh, mat = s.mat }; setOf[s] = set; sc.sets.Add(set); }
            set.at.Add(new Vector4(x, MXGround(x, z) - sink, z, yaw < 0 ? Rx(0, 360) : yaw));
            set.scale.Add(scale);
            scTris += s.tris; scCount++;
        }
        var piles = new List<Vector3>();   // x, z, radius: shrubs gather round these

        // snowy ground mounds on the edge banks (outside the arena), long axis along the edge
        // ly2: the snowy-ground strips came out across the edge (long axis into the arena) as flat grey tongues; left out
        if (snowGround != null && System.Environment.GetEnvironmentVariable("SP_MX_SNOWGROUND") == "1")
        {
            int made = 0;
            for (int k = 0; k < 400 && made < 18; k++)
            {
                int side = xr.Next(3);   // 0 W, 1 E, 2 N (not on the camera side: a big pale sheet over the FG, ms1)
                float x, z, yaw;
                if (side < 2) { x = (side == 0 ? -1 : 1) * (HX + Rx(4f, 10f)); z = Rx(-HZ - 6f, HZ + 4f); yaw = 90f + Rx(-12f, 12f); }
                else if (side == 2) { x = Rx(-HX, HX); z = HZ + Rx(4f, 7f); yaw = Rx(-10f, 10f); }
                else { x = Rx(-HX, HX); z = -HZ - Rx(4f, 8f); yaw = Rx(-10f, 10f); }
                if (River(x, z, 9f) || BorderBlocked(x, z) || Cliff(x, z) || Kept(x, z, 1f)) continue;
                float s = Rx(0.38f, 0.58f);
                Add(snowGround, x, z, s, 0.45f * s / 0.5f, yaw + (xr.NextDouble() < 0.5 ? 180f : 0f));
                made++;
            }
        }

        // rock piles along the ring and one small one by each vignette
        void Pile(MXSrc s, float x, float z, float scale)
        {
            Add(s, x, z, scale, s.size.y * scale * 0.4f);
            piles.Add(new Vector3(x, z, Mathf.Max(s.size.x, s.size.z) * scale * 0.5f));
        }
        foreach (var s in new[] { rocks, rocky })
        {
            if (s == null) continue;
            int made = 0;
            for (int k = 0; k < 600 && made < 0; k++)   // tr6 (lead): off - the snowy rock piles read as the pale round discs by the lantern yard / south edge (ly6..tr5)
            {
                float x = Rx(-HX - 7f, HX + 7f), z = Rx(-HZ - 6f, HZ + 6f);
                float e = Inside(x, z);
                if (e > -1.5f ||   /* r10: both pile kinds read as flat discs inside the arena */  e < -6f || River(x, z, 8f) || Cliff(x, z) || Kept(x, z, 1.2f)) continue;
                bool near = false; foreach (var p in piles) if (Vector2.Distance(new Vector2(p.x, p.y), new Vector2(x, z)) < 9f) near = true;
                if (near) continue;
                Pile(s, x, z, (s == rocks ? Rx(0.25f, 0.45f) : Rx(0.22f, 0.42f)));
                made++;
            }
        }
        foreach (var c in Clusters)
        {
            var s = rocky; if (s == null || s != null) continue;   // r10 lead: off, the piles by the vignettes read as flat white discs. Was: the snowy-rocks embankment read as a flat disc on level ground (ms3-5): only outside the edge
            for (int k = 0; k < 30; k++)
            {
                float a = Rx(0, 6.283f), d = c.z * Rx(1.15f, 1.5f);
                float x = c.x + Mathf.Cos(a) * d, z = c.y + Mathf.Sin(a) * d;
                if (River(x, z) || Kept(x, z, 0.8f) || Inside(x, z) < -8f) continue;
                Pile(s, x, z, Rx(0.18f, 0.28f));
                break;
            }
        }

        // dead pine stumps: ring and vignettes
        if (dead != null)
        {
            int made = 0;
            for (int k = 0; k < 800 && made < 24; k++)
            {
                float x = Rx(-HX - 7f, HX + 7f), z = Rx(-HZ - 6f, HZ + 6f), e = Inside(x, z);
                if (e > -1.5f || e < -7f || River(x, z) || Cliff(x, z) || Kept(x, z, 0.6f)) continue;   // tr8: outside the arena only (the snowy stump tops read as pale discs)
                Add(dead, x, z, Rx(0.85f, 1.35f), 0.06f); made++;
            }
            foreach (var c in Clusters)
                for (int k = 0, put = 0; k < 20 && put < 0; k++)   // tr8: off - the pale oval left of the lantern yard (ly6..tr7) was a dead pine stump here
                {
                    float a = Rx(0, 6.283f), d = c.z * Rx(0.95f, 1.45f), x = c.x + Mathf.Cos(a) * d, z = c.y + Mathf.Sin(a) * d;
                    if (River(x, z) || Kept(x, z, 0.5f)) continue;
                    Add(dead, x, z, Rx(0.7f, 1.1f), 0.05f); put++;
                }
        }

        // fallen pine branches: ring and round the vignettes
        if (branches != null)
        {
            int made = 0;
            for (int k = 0; k < 1200 && made < 46; k++)
            {
                float x = Rx(-HX - 6f, HX + 6f), z = Rx(-HZ - 5f, HZ + 5f), e = Inside(x, z);
                if (e > 4f || e < -6f || River(x, z) || Cliff(x, z) || Kept(x, z, 0.4f)) continue;
                if (Fbm(x + 50f, z - 20f, 0.08f, 2) < 0.42f) continue;
                Add(branches, x, z, Rx(0.55f, 0.95f), 0.06f); made++;
            }
            foreach (var c in Clusters)
                for (int k = 0, put = 0; k < 30 && put < 4; k++)
                {
                    float a = Rx(0, 6.283f), d = c.z * Rx(0.8f, 1.6f), x = c.x + Mathf.Cos(a) * d, z = c.y + Mathf.Sin(a) * d;
                    if (River(x, z) || Kept(x, z, 0.3f)) continue;
                    Add(branches, x, z, Rx(0.45f, 0.8f), 0.05f); put++;
                }
        }

        // dead shrub clumps
        int shrubCount = 0;
        if (shrubs.Count > 0)
        {
            void Clump(float cx, float cz, int items, float spread, float h0, float h1)
            {
                for (int i = 0; i < items; i++)
                {
                    float ga = Rx(0, 6.283f), gr = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-4f, Rx(0, 1)))) * spread;
                    float x = cx + Mathf.Cos(ga) * gr, z = cz + Mathf.Sin(ga) * gr;
                    if (River(x, z) || Cliff(x, z) || Kept(x, z) || Inside(x, z) < -10f) continue;
                    var s = shrubs[xr.Next(shrubs.Count)];
                    float scale = Rx(h0, h1) / Mathf.Max(0.1f, s.size.y);
                    Add(s, x, z, scale, 0.03f * scale);
                    shrubCount++;
                }
            }
            // jittered grid of clump centres; density: outer ring, rings round the vignettes, sparse low ones in the field
            const float Cell = 3f;
            for (float gz = -HZ - 9f; gz < HZ + 8f; gz += Cell)
            for (float gx = -HX - 9f; gx < HX + 9f; gx += Cell)
            {
                float x = gx + Rx(0, Cell), z = gz + Rx(0, Cell), e = Inside(x, z);
                if (River(x, z) || Cliff(x, z)) continue;
                float ring = Mathf.SmoothStep(0, 1, (e + 9f) / 5f) * (1f - Mathf.SmoothStep(0, 1, (e - 3f) / 6f));
                float vig = 0f;
                foreach (var c in Clusters)
                {
                    float dc = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.y)) / c.z;
                    vig = Mathf.Max(vig, dc < 0.8f ? 0f : Mathf.Exp(-(dc - 1.3f) * (dc - 1.3f) / 0.18f));
                }
                float patch = Fbm(x - 140f, z + 61f, 0.07f, 3);
                float d = Mathf.Max(ring * 1.0f, vig * 0.95f) * Mathf.Clamp01((patch - 0.3f) / 0.3f);
                bool field = d < 0.05f && e > 9f;
                if (field) d = 0.06f * Mathf.Clamp01((patch - 0.45f) / 0.15f);
                d *= 1f - 0.85f * Mathf.Clamp01(SampleMask(mPath, x, z) * 1.4f);
                if (Rx(0, 1) > d) continue;
                if (field) Clump(x, z, 1 + xr.Next(2), 0.35f, 0.25f, 0.5f);
                else if (vig > ring) Clump(x, z, 2 + xr.Next(4), 0.6f, 0.5f, 1.1f);
                else Clump(x, z, 4 + xr.Next(6), 0.85f, e > 0f ? 0.55f : 0.75f, e > 0f ? 1.1f : 1.5f);
            }
            // round rock piles and lanterns (tucked against them)
            foreach (var p in piles)
                for (int i = 0; i < 4; i++)
                {
                    float a = Rx(0, 6.283f), d = p.z + Rx(0.2f, 0.9f);
                    Clump(p.x + Mathf.Cos(a) * d, p.y + Mathf.Sin(a) * d, 1 + xr.Next(2), 0.3f, 0.5f, 1.0f);
                }
            foreach (var p in lanternSpots)
                for (int i = 0; i < 3; i++)
                {
                    float a = Rx(0, 6.283f), d = Rx(1.3f, 2.4f);
                    Clump(p.x + Mathf.Cos(a) * d, p.y + Mathf.Sin(a) * d, 2 + xr.Next(2), 0.35f, 0.5f, 1.0f);
                }
        }
        sc.Push();

        // tri report per set
        foreach (var kv in setOf) Log($"MX scatter {kv.Key.name}: {kv.Value.at.Count} x {kv.Key.tris} tris");
        Log($"MX props: {propCount} objects ({trunks} trunks, {walls} wall pieces, {lanternSpots.Count} lanterns, {statues} statues, {pillars} pillars, {bridges} bridges), {propTris / 1000}k tris; scatter: {scCount} instances ({shrubCount} shrubs), {scTris / 1000}k tris before culling");
    }
}
