// 11-11 redo 2, vegetation pass (설원 역병).
// r2b (owner: "풀도 과하게 너무 자그마한 똑같이 생긴것들만 일정 위치로 박아서 이상해, 나무들 식생들도 부족하고"; refs2
// sheet_dead_vegetation_winter + NOTES.md): five mixed forms instead of one tuft on a grid -
//   low dome shrubs 10-30 cm, upright cattail groups 1-1.5 m, dense reed walls (some stems snapped and leaning),
//   wind-bent grass bunches, seed-head weeds with snow caps (+ twiggy shrubs and big thickets toward the edges).
// Placed as irregular clusters of 3-10 with 2-3x size variation inside a cluster and big empty snow areas between.
// Colour from a straw palette texture (SP_V_StrawPalette.png: shaded base #6a5440 -> straw #a88a5a-#c8a870), no white
// tips and no frost on stems: snow only as caps/lumps baked into vertex R, plus a shallow darker dip ring at the base of
// the reed/grass/weed pieces so they poke THROUGH the snow.
//   VegetationDense: one SPScatter + LookStudyInstancer, no shadows, no colliders, one submesh per mesh.
//   VegetationFrame: gnarled dead trees and snowy red pines along the east / north / south edges (the west side is the
//                    layout worker's rocky plateau with its own pines: only its far corners get trees here).
// Models: BlenderSource~/veg/build_veg.py (Blender, headless). Textures: BlenderSource~/veg/make_atlas.js, make_palette.js.
//
// Cards and the instanced path: SP_GrassCards drew as white rectangles because its atlas quadrant (SP_FoliageAtlas,
// grass) is ~110 hairline blades over a solid opaque base block. Down the mip chain the blades average out under the
// 0.5 clip (no coverage-preserving mips on the importer), leaving only the solid base as an opaque quad, and the card
// normals baked to point up + frost in vertex R made the whole quad snow-white. Here: no cards in the instanced path
// at all; the card atlas used by the framing trees has no solid areas, strokes >= 5 px and coverage-preserving mips.
using System.Collections.Generic;
using System.Linq;
using SnowPlague;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const string VgModels = "Veg/";                       // under Models/
    const string VgTex = TexDir + "Veg/";
    static readonly string[] VgSlots = { "SP_V_Brush", "SP_V_Grass", "SP_V_Bark", "SP_V_PineBark", "SP_V_Needles", "SP_V_Twigs" };

    // cluster types: models, members per cluster, spread (m), zone weights (centre > 14 m inside, mid, edge < 5 m)
    static readonly (string name, string[] models, int n0, int n1, float spread, float wC, float wM, float wE)[] VgTypes =
    {
        ("dome",    new[] { "SP_V_Dome_A", "SP_V_Dome_B" },                               3, 10, 1.3f, 3.0f, 2.0f, 1.0f),
        ("bent",    new[] { "SP_V_BentGrass_A", "SP_V_BentGrass_B", "SP_V_BentGrass_C" }, 3, 9,  1.1f, 0.0f, 0.0f, 0.0f),   // tr29 (QA #6): the repeated straw "comb" in 02 - off
        ("weed",    new[] { "SP_V_SeedWeed_A", "SP_V_SeedWeed_B" },                       2, 5,  1.0f, 0.7f, 0.9f, 0.6f),
        ("cattail", new[] { "SP_V_Cattails_A", "SP_V_Cattails_B", "SP_V_Cattails_C" },    2, 5,  1.4f, 0.5f, 1.8f, 2.0f),
        ("reeds",   new[] { "SP_V_ReedWall_A", "SP_V_ReedWall_B" },                       1, 3,  2.2f, 0.0f, 0.7f, 2.5f),
        ("shrub",   new[] { "SP_V_Brush_A", "SP_V_Brush_B" },                             3, 6,  1.4f, 0.4f, 1.6f, 2.0f),
        ("thicket", new[] { "SP_V_Thicket_A", "SP_V_Thicket_B" },                         1, 3,  2.4f, 0.0f, 0.4f, 2.2f),
    };

    static partial void BuildVegetationPass()
    {
        var vr = new System.Random(9071);   // own stream: the main rng sequence of the other builders stays as it was
        float Rv(float a, float b) => a + (float)vr.NextDouble() * (b - a);

        VgMaterials();
        VgRemap();

        // ------------------------------------------------------------------ dense vegetation (instanced)
        var go = new GameObject("VegetationDense"); go.transform.SetParent(root, false);
        go.AddComponent<LookStudyInstancer>();
        var sc = go.AddComponent<SPScatter>();

        var setOf = new Dictionary<string, (SPScatter.Set set, float h)>();
        foreach (var m in VgTypes.SelectMany(t => t.models).Distinct())
        {
            var src = Src(VgModels + m); if (!src) continue;
            var mf = src.GetComponentInChildren<MeshFilter>(); var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mf.sharedMesh) { Log("VEG skip " + m + ": no mesh"); continue; }
            if (mf.sharedMesh.subMeshCount > 1) Log($"VEG {m}: {mf.sharedMesh.subMeshCount} submeshes, only the first is drawn");
            var mat = mr.sharedMaterial && mats.TryGetValue(mr.sharedMaterial.name, out var mm) ? mm : mr.sharedMaterial;
            var set = new SPScatter.Set { mesh = mf.sharedMesh, mat = mat, pre = mf.transform.rotation, preOffset = mf.transform.position, preScale = mf.transform.lossyScale.x };
            var ext = mf.transform.rotation * mf.sharedMesh.bounds.size;   // height after the FBX axis fix
            setOf[m] = (set, Mathf.Max(0.1f, Mathf.Abs(ext.y) * mf.transform.lossyScale.x));
        }
        if (setOf.Count == 0) { Log("VEG no vegetation models found under Models/Veg"); return; }

        // keep-out: footprints of props, lanterns and architecture already in the scene, and the river
        var keep = new List<Rect>();
        var skip = new HashSet<string> { "Ground", "Border", "Foreground", "Scatter", "GroundClutter", "FogVfx", "VegetationDense", "VegetationFrame" };
        foreach (Transform grp in root)
        {
            if (skip.Contains(grp.name)) continue;
            foreach (var r in grp.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.sharedMaterial && (r.sharedMaterial.shader.name.Contains("LightCone") || r.sharedMaterial.shader.name.Contains("Fog") || r.sharedMaterial.shader.name.Contains("Blood") || r.sharedMaterial.shader.name.Contains("Blob"))) continue;
                var b = r.bounds;
                if (b.size.y < 0.25f) continue;
                if (b.size.x > 60f || b.size.z > 60f) continue;   // ground-sized sheets
                keep.Add(new Rect(b.min.x - 0.15f, b.min.z - 0.15f, b.size.x + 0.3f, b.size.z + 0.3f));
            }
        }
        bool Blocked(float x, float z, float pad = 0f)
        {
            if (DistanceToRiver(x, z) < 6.5f + pad) return true;
            if (Vector2.Distance(new Vector2(x, z), GateSpot()) < 6.5f + pad) return true;   // tr14: kept clear for the 솟을삼문
            foreach (var k in keep) if (x > k.xMin - pad && x < k.xMax + pad && z > k.yMin - pad && z < k.yMax + pad) return true;
            return false;
        }
        float Inside(float x, float z) => Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z));   // negative outside

        // cluster centres: random, kept only in the "growing" patches of a low-frequency noise -> big empty snow fields
        // tr29 (QA #6): fewer, fuller clumps - 1.5-3.5 m, 4-12 members, >= 3 meshes, 0.6-2.2 scale - about half the floor
        // left as open snow, clumps pulled to bare/dirt patches and the river bank edges
        const int ClusterTarget = 1100;   // vg1: 1150 read as bare snow (4.5k instances); tr29: bigger clumps, more open snow
        int clusters = 0, blocked = 0, total = 0;
        var made = new Dictionary<string, int>();
        for (int tries = 0; tries < ClusterTarget * 12 && clusters < ClusterTarget; tries++)
        {
            float cx = Rv(-HX - 7f, HX + 7f), cz = Rv(-HZ - 7f, HZ + 7f);
            float e = Inside(cx, cz);
            float edge = 1f - Mathf.Clamp01(e / 18f);
            float patch = Fbm(cx + 211f, cz - 87f, 0.045f, 3) * 0.8f + Mathf.PerlinNoise(cx * 0.16f + 5f, cz * 0.16f - 3f) * 0.2f;
            float grow = Mathf.Clamp01((patch - Mathf.Lerp(0.40f, 0.33f, edge)) / 0.12f);
            grow *= 1f - 0.9f * Mathf.Clamp01(SampleMask(mPath, cx, cz) * 1.5f);   // footpaths stay trodden
            foreach (var c in Clusters)                                              // corpse spots / yards stay readable
            {
                float dc = Vector2.Distance(new Vector2(cx, cz), new Vector2(c.x, c.y)) / (c.z * 0.8f);
                if (dc < 1f) grow *= Mathf.SmoothStep(0.1f, 1f, dc);
            }
            if (e < 0f) grow *= Mathf.Clamp01(1f + e / 7f);
            {   // tr29: dirt patches (snow broken) and the bank edge (7-11 m from the river centre line) grow, whole snow less
                float dirt = Mathf.Clamp01(1.15f - SampleMask(mSnow, cx, cz));
                float bank = Mathf.Clamp01(1f - Mathf.Abs(DistanceToRiver(cx, cz) - 9f) / 2.5f);
                grow *= 0.35f + 0.65f * Mathf.Max(dirt, bank);
            }
            if (Rn2(vr) > grow * Mathf.Lerp(0.75f, 1f, edge)) continue;
            if (Blocked(cx, cz)) { blocked++; continue; }

            // pick the cluster type by zone
            float wsum = 0; var w = new float[VgTypes.Length];
            for (int t = 0; t < VgTypes.Length; t++)
            {
                var T0 = VgTypes[t];
                w[t] = e > 14f ? T0.wC : (e > 5f ? Mathf.Lerp(T0.wE, T0.wM, (e - 5f) / 9f) : T0.wE);
                if (!T0.models.Any(setOf.ContainsKey)) w[t] = 0;
                wsum += w[t];
            }
            if (wsum <= 0) continue;
            float pick = Rv(0, wsum); int ti = 0;
            while (ti < w.Length - 1 && pick > w[ti]) { pick -= w[ti]; ti++; }
            var T = VgTypes[ti];
            clusters++;
            int n = vr.Next(4, 13);                       // tr29: 4-12 members
            float spread = Rv(1.5f, 3.5f);                // tr29: 1.5-3.5 m clump radius
            float big = Rv(0.85f, 1.2f);   // the whole cluster a little bigger or smaller
            // tr29: at least 3 different meshes per clump - the type's own forms first, then low filler (domes, seed weeds)
            var pool = T.models.Where(setOf.ContainsKey).OrderBy(_ => vr.Next()).ToList();
            foreach (var fm in VgTypes[0].models.Concat(VgTypes[2].models).Where(setOf.ContainsKey).OrderBy(_ => vr.Next()))
                if (pool.Count < 3 && !pool.Contains(fm)) pool.Add(fm);
            if (pool.Count == 0) continue;
            for (int i = 0; i < n; i++)
            {
                string m = i < pool.Count ? pool[i] : pool[vr.Next(pool.Count)];
                var s = setOf[m];
                float ga = Rv(0, 6.283f), gr = spread * Mathf.Sqrt(Rv(0.02f, 1f));
                float x = cx + Mathf.Cos(ga) * gr, z = cz + Mathf.Sin(ga) * gr;
                if (Inside(x, z) < -8f) continue;
                if (Blocked(x, z)) { blocked++; continue; }
                // 2-3x size spread inside a cluster; the middle of the cluster holds the big ones
                float scale = big * Mathf.Lerp(2.2f, 0.6f, Mathf.Pow(Rv(0, 1), 0.7f) * 0.6f + gr / Mathf.Max(spread, 0.01f) * 0.4f);   // tr29: 0.6-2.2
                // centre: keep combat readable (cattails / weeds are thin: allowed to 1 m, everything else 0.5 m)
                float ei = Inside(x, z);
                float maxH = ei > 14f ? (T.name == "cattail" || T.name == "weed" ? 1.05f : 0.5f) : (ei > 0f ? 1.1f : 2.2f);   // arena rule: only low brush inside (<= 1.1 m), the tall stands outside
                if (s.h * scale > maxH) scale = maxH / s.h * Rv(0.85f, 1f);
                if (scale < 0.35f) continue;
                float dr = SampleMask(mDrift, x, z);
                s.set.at.Add(new Vector4(x, Height(x, z) + dr * dr * 0.55f - 0.02f * scale, z, Rv(0, 360)));
                s.set.scale.Add(scale);
                made[m] = made.TryGetValue(m, out var k0) ? k0 + 1 : 1;
                total++;
            }
        }
        long tris = 0;
        foreach (var kv in setOf)
        {
            sc.sets.Add(kv.Value.set);
            long t = kv.Value.set.mesh.GetIndexCount(0) / 3; int c = made.TryGetValue(kv.Key, out var cc) ? cc : 0;
            tris += t * c;
            Log($"VEG {kv.Key}: {c} x {t} tris");
        }
        sc.Push();
        Log($"VEG dense: {total} instances in {clusters} clusters, {tris / 1000}k tris in all (~5% on screen at the RoomRun zoom), {blocked} kept off props/river");

        // ------------------------------------------------------------------ framing trees
        var fr = Group("VegetationFrame");
        var swap = new Dictionary<Material, Material>();
        void Sw(string a, string b) { if (mats.TryGetValue(a, out var ma) && mats.TryGetValue(b, out var mb) && ma && mb) swap[ma] = mb; }
        Sw("SP_V_Bark", "SP_V_FG_Bark"); Sw("SP_V_PineBark", "SP_V_FG_PineBark"); Sw("SP_V_Needles", "SP_V_FG_Needles"); Sw("SP_V_Twigs", "SP_V_FG_Twigs");
        string[] dead = SkDeads, pine = SkSidePines;   // tr1: downloaded trees
        int trees = 0;
        void Tree(string m, float x, float z, float s, bool shadows, bool fg)
        {
            if (!fg && Blocked(x, z, 1.5f)) return;   // not through the bridge / gate / river
            var g = Place(m.StartsWith("SK:") ? m : VgModels + m, x, z, Rv(0, 360), s, fr, shadows, 0.12f);
            if (!g) return;
            trees++;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                if (fg) { r.sharedMaterials = r.sharedMaterials.Select(mm => mm && swap.TryGetValue(mm, out var f) ? f : mm).ToArray(); r.receiveShadows = false; }
                r.shadowCastingMode = shadows && !m.Contains("Dead_Tree") ? ShadowCastingMode.On : ShadowCastingMode.Off;   // tr14: frame dead conifers cast the long line shadows (02)
            }
            if (fg) GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        }
        string Pick(float pinePct) => vr.NextDouble() < pinePct ? pine[vr.Next(pine.Length)] : dead[vr.Next(dead.Length)];
        // east: two rows, the inner one reaching over the rim of the arena
        for (float z = -HZ - 6f; z < HZ + 8f; z += Rv(5.5f, 8.5f))
            Tree(Pick(0.5f), HX + Rv(1.2f, 4.5f), z, Rv(0.85f, 1.25f), vr.NextDouble() < 0.6, false);
        for (float z = -HZ - 4f; z < HZ + 8f; z += Rv(7f, 11f))
            Tree(Pick(0.55f), HX + Rv(6.5f, 11f), z, Rv(1.0f, 1.35f), false, false);
        // west: only the far corners (the rocky plateau with its own pines is in between)
        foreach (float z0 in new[] { -HZ - 6f, HZ - 6f })
            for (float z = z0; z < z0 + 12f; z += Rv(5f, 7f))
                Tree(Pick(0.5f), -HX - Rv(1.5f, 5f), z, Rv(0.85f, 1.2f), false, false);
        // north: two rows (no shadows: the moon is behind them)
        for (float x = -HX + 2f; x < HX + 6f; x += Rv(6f, 9f))
            Tree(Pick(0.55f), x, HZ + Rv(1.5f, 4.5f), Rv(0.9f, 1.25f), false, false);
        for (float x = -HX - 4f; x < HX + 8f; x += Rv(8f, 12f))
            Tree(Pick(0.6f), x, HZ + Rv(6f, 10f), Rv(1.0f, 1.4f), false, false);
        // inside the rim (east / north / south-east): single gnarled trees and pines standing in the arena's outer band
        for (int i = 0, made2 = 0; i < 400 && made2 < 0; i++)   // arena rule (11-12): no trees inside the boundary
        {
            float x = Rv(-HX + 14f, HX - 1.5f), z = Rv(-HZ + 1.5f, HZ - 1.5f);
            float e2 = Inside(x, z);
            if (e2 > 4.5f || (x < -HX + 20f && Mathf.Abs(z) < HZ - 6f)) continue;   // the outer 4.5 m only, not the west plateau
            if (Blocked(x, z, 2.5f)) continue;
            Tree(Pick(0.5f), x, z, Rv(0.6f, 0.95f), vr.NextDouble() < 0.5, false); made2++;
        }
        // south (camera side): near-black dithered silhouettes framing the screen bottom, between the existing FG pines
        // tr30 (QA #15): irregular groups of 2-5, mixed sizes, open gaps (was one every 7-11 m)
        for (float x = -HX - 12f + Rv(0f, 8f); x < HX + 12f; )
        {
            int gn = vr.Next(2, 6);
            for (int i = 0; i < gn; i++, x += Rv(2.4f, 4f))
                Tree(Pick(0.5f), x + Rv(-0.8f, 0.8f), -HZ - Rv(5f, 11f), Rv(0.45f, 0.95f), false, true);
            x += Rv(10f, 20f);
        }
        // tr30 (QA #3b): dark masses in the 4-8 m band just outside the west / east / north boundary - a pine or bare
        // tree with 2-3 thickets round it every 7-14 m, so the arena edge has dark weight instead of a thin tree line
        int masses = 0;
        void Mass(float x, float z)
        {
            if (Blocked(x, z, 2f) || LedgeH(x, z) > 0.3f || DistanceToRiver(x, z) < 8f) return;
            Tree(Pick(0.65f), x, z, Rv(0.7f, 1.05f), false, false);
            int th = vr.Next(2, 4);
            for (int i = 0; i < th; i++)
            {
                float a = Rv(0f, 6.283f), d = Rv(1.5f, 3.5f), tx = x + Mathf.Cos(a) * d, tz = z + Mathf.Sin(a) * d;
                if (Inside(tx, tz) > -3f || Blocked(tx, tz, 1f)) continue;
                var tg = Place(VgModels + (vr.NextDouble() < 0.5 ? "SP_V_Thicket_A" : "SP_V_Thicket_B"), tx, tz, Rv(0, 360), Rv(1.4f, 2.3f), fr, false, 0.1f);
                if (tg) foreach (var r in tg.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            masses++;
        }
        for (float z = -HZ + 4f; z < HZ + 2f; z += Rv(7f, 14f)) { Mass(HX + Rv(4f, 8f), z); Mass(-HX - Rv(4f, 8f), z); }
        for (float x = -HX + 4f; x < HX - 2f; x += Rv(7f, 14f)) Mass(x, HZ + Rv(4f, 8f));
        Log($"VEG edge masses: {masses} (pine/bare tree + thickets, 4-8 m outside W/E/N)");
        SkSnagsPlace(fr, vr, (x, z, p) => Blocked(x, z, p));
        // tr12 screen-edge framing (ss5 top-left tree): two dithered heroes whose crowns hang over the top-left of views 02 / 07
        // tr14: from the capture camera (SetGameView, fov 30, 16:9): the ground point under a crown at ~8.5 m that projects to
        // the top-left corner (viewport 0.1, 0.88) of view 02 (focus -40, 28, zoom 1) and view 07 (focus 0, 0, zoom 1.6)
        foreach (var fp0 in new[] { EdgeCrown(new Vector3(-40f, 0f, 28f), 1f, 0.0f, 1.02f, 11f) })   // tr15: tr14 (0.1, 0.88, 8.5 m) covered the left third of 02; arena rule: outside only
        {
            var fp = fp0; if (InArena(fp.x, fp.y, -1f)) fp.x = -HX - 4f;   // arena rule: the framing tree stands just past the west boundary
            var g = Place(SkHero, fp.x, fp.y, Rv(0, 360), 1.2f, fr, false, 0.15f);
            if (!g) continue;
            skHeroPlaced++;
            var skfg = SkFgSwap();
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { r.sharedMaterials = r.sharedMaterials.Select(mm => mm && skfg.TryGetValue(mm, out var f) ? f : mm).ToArray(); r.receiveShadows = false; r.shadowCastingMode = ShadowCastingMode.Off; }
        }
        // tr12: white paper-like fragment by the south corpse spot (view 04): log what is there; cloth takes no snow
        foreach (var r in root.GetComponentsInChildren<Renderer>())
            if (Vector2.Distance(new Vector2(r.bounds.center.x, r.bounds.center.z), new Vector2(Clusters[4].x, Clusters[4].y)) < 6f)
                Log($"VEG south corpse spot: {r.name} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")} size={r.bounds.size}");
        if (mats.TryGetValue("SP_Cloth", out var cloth) && cloth) { cloth.SetFloat("_SnowBias", -1f); cloth.SetColor("_Tint", Hex("#5f5a52")); EditorUtility.SetDirty(cloth); }
        // tr13: the white paper-like shapes were the snow patches on the straw mat over the body (SP_StrawMat_B)
        if (mats.TryGetValue("SP_Straw", out var straw) && straw) { straw.SetFloat("_SnowBias", -1f); EditorUtility.SetDirty(straw); }
        // tr15: the flat mat / rags sit inside the ground-melt band and took the (now snowy) ground colour: the white shapes in view 04
        foreach (var mn in new[] { "SP_Straw", "SP_Cloth", "SP_Rope" }) if (mats.TryGetValue(mn, out var fm) && fm) { fm.SetFloat("_GroundMelt", 0.01f); EditorUtility.SetDirty(fm); }
        // hero accents: two by the stone-lantern yard (cluster 0), one by the north gate
        int acc = 0;
        for (int i = 0; i < 200 && acc < 0; i++)   // arena rule (11-12): no accent heroes inside the boundary
        {
            bool yard = false;
            var c0 = Clusters[0]; float a = Rv(0, 6.283f), d = Rv(c0.z * 1.7f, c0.z * 2.4f);   // tr1: 1.1-1.7 r crowned over the whole yard
            float x = yard ? c0.x + Mathf.Cos(a) * d : Rv(-14f, 14f), z = yard ? c0.y + Mathf.Sin(a) * d : HZ - Rv(2.5f, 6f);
            if (Blocked(x, z, 2.5f) || Mathf.Abs(x) > HX - 1f || Mathf.Abs(z) > HZ + 4f) continue;
            if (Place(SkHero, x, z, Rv(0, 360), 0.8f, fr, true, 0.15f)) { acc++; skHeroPlaced++; }   // clamps to 9 m
        }
        Log($"SK hero pines in the scene so far: {skHeroPlaced} (accents {acc})");
        // SkMakePrefabs();   // tr8: off - the abandoned field now owns its copies of the Trees/ materials; regenerate by hand only
        Log($"VEG frame: {trees} framing trees");
    }

    static float Rn2(System.Random r) => (float)r.NextDouble();

    static Vector2 EdgeCrown(Vector3 focus, float zoom, float u = 0.1f, float v = 0.88f, float crownH = 8.5f)
    {
        var off = new Vector3(0, 17f, -14.5f) * zoom; var p = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var d = fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f);
        float t = (crownH - p.y) / d.y; var q = p + d * t;
        Log($"VEG edge crown for focus {focus} zoom {zoom}: ({q.x:0.0}, {q.z:0.0})");
        return new Vector2(q.x, q.z);
    }

    // tr14: the yard-side approach of the west arched bridge (x -34, same lookup as the Megascans bridge), kept clear
    static Vector2 GateSpot()
    {
        const float bx = -34f;
        for (int i = 0; i + 1 < RiverPath.Length; i++)
        {
            var a = RiverPath[i]; var b = RiverPath[i + 1];
            if ((a.x - bx) * (b.x - bx) > 0f || Mathf.Abs(b.x - a.x) < 1e-3f) continue;
            var c = Vector2.Lerp(a, b, (bx - a.x) / (b.x - a.x)); var d = (b - a).normalized; var n = new Vector2(-d.y, d.x);
            if (n.y < 0) n = -n;   // the north (yard) side
            return LMGatePos;
        }
        return new Vector2(-34f, -6f);
    }

    static void VgMaterials()
    {
        var atlasPath = VgTex + "SP_V_CardAtlas.png";
        var atlas = Tex(atlasPath);
        var imp = AssetImporter.GetAtPath(atlasPath) as TextureImporter;
        if (imp && (!imp.alphaIsTransparency || !imp.mipMapsPreserveCoverage || Mathf.Abs(imp.alphaTestReferenceValue - 0.5f) > 0.01f || imp.alphaSource != TextureImporterAlphaSource.FromInput))
        {
            imp.alphaIsTransparency = true; imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f;
            imp.SaveAndReimport();
            atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath);
        }
        // straw palette: tiny lookup (8 rows x 2 px); no mips so the rows never bleed into each other
        var palPath = VgTex + "SP_V_StrawPalette.png";
        var pal = Tex(palPath, false, false, true, 64);
        var pimp = AssetImporter.GetAtPath(palPath) as TextureImporter;
        if (pimp && (pimp.mipmapEnabled || pimp.textureCompression != TextureImporterCompression.Uncompressed || pimp.filterMode != FilterMode.Bilinear))
        {
            pimp.mipmapEnabled = false; pimp.textureCompression = TextureImporterCompression.Uncompressed; pimp.filterMode = FilterMode.Bilinear;
            pimp.SaveAndReimport();
            pal = AssetDatabase.LoadAssetAtPath<Texture2D>(palPath);
        }
        var barkA = T("bark_brown_02_diff_1k.jpg"); var barkN = T("bark_brown_02_nor_gl_1k.jpg", true);
        var pineA = T("pine_bark_diff_1k.jpg"); var pineN = T("pine_bark_nor_gl_1k.jpg", true);

        // brush and grass: colour straight from the palette (no wither tint, no pale tips); snow only from vertex R
        // (caps, lumps, dip rings), the world-up snow is biased off so stems and blades stay straw / brown
        foreach (var (name, bend, trample, bias) in new[] { ("SP_V_Brush", 0.25f, 0.35f, -0.8f), ("SP_V_Grass", 0.55f, 1f, -1f) })
        {
            var m = Veg(name, pal, null, Color.white, false, 0f, bend);
            if (!m) continue;
            m.SetColor("_TipCol", new Color(0.75f, 0.78f, 0.8f, 0f));
            m.SetFloat("_Trample", trample); m.SetFloat("_SnowBias", bias); m.SetFloat("_GroundMelt", 0.05f);
            m.SetFloat("_AOStrength", 0.8f); m.SetFloat("_Wrap", 0.45f); m.SetFloat("_Smoothness", 0.08f);
        }
        Veg("SP_V_Bark", barkA, barkN, Hex("#6f6a66"), false, 0f, 0.04f);
        var pb = Veg("SP_V_PineBark", pineA, pineN, Hex("#7d6559"), false, 0f, 0.04f);
        if (pb) pb.SetFloat("_SnowBias", -0.3f);
        var nd = Veg("SP_V_Needles", atlas, null, Hex("#3a463c"), true, 0f, 0.3f);
        var tw = Veg("SP_V_Twigs", atlas, null, Hex("#7e7670"), true, 0f, 0.3f);
        if (nd) { nd.SetFloat("_Cutoff", 0.5f); nd.SetFloat("_SnowBias", -0.5f); }   // dark green under the white cushions
        if (tw) { tw.SetFloat("_Cutoff", 0.5f); tw.SetFloat("_SnowBias", -0.8f); }
        // pn1: pine snow as soft cushions shaded on the needle cards and the tops of the limbs (SPVegSurface _SnowClump:
        // world-up x noise, soft edge, thinner on sparse needles, a little translucency); also the kit red pines
        void Cushion(string n, float cover) { if (mats.TryGetValue(n, out var m) && m) { m.SetFloat("_SnowClump", 1f); m.SetFloat("_SnowCover", cover); m.SetFloat("_Wrap", 0.45f); EditorUtility.SetDirty(m); } }
        Cushion("SP_V_Needles", 0.5f); Cushion("SP_V_PineBark", 0.45f); Cushion("SP_Needles", 0.5f); Cushion("SP_PineBark", 0.45f);
        foreach (var nn in new[] { "SP_V_Needles", "SP_Needles" }) if (mats.TryGetValue(nn, out var nmat) && nmat) nmat.SetFloat("_SnowScale", 1.8f);
        FG("SP_V_FG_Bark", barkA, false); FG("SP_V_FG_PineBark", pineA, false);
        FG("SP_V_FG_Needles", atlas, true); FG("SP_V_FG_Twigs", atlas, true);
        if (mats.TryGetValue("SP_V_FG_Twigs", out var fgt) && fgt) fgt.SetFloat("_Frost", 0.35f);
        AssetDatabase.SaveAssets();
    }

    static void VgRemap()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Models/Veg" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            if (imp.importAnimation || imp.importCameras || imp.importLights) { imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; changed = true; }
            if (imp.generateSecondaryUV) { imp.generateSecondaryUV = false; changed = true; }
            var map = imp.GetExternalObjectMap();
            foreach (var n in VgSlots)
            {
                if (!mats.TryGetValue(n, out var m) || !m) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (map.TryGetValue(id, out var cur) && cur == m) continue;
                imp.AddRemap(id, m); changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
    }
    // ------------------------------------------------------------------ tr1: downloaded trees (ThirdParty/Sketchfab, CC BY 4.0)
    // Owner (11-12): "나무 저거 제발 어디서 좀 받아, 너가 코딩으로 만들지 말고 너무 어설프잖아" - every procedural tree
    // (SP_V_Frame*, kit SP_RedPine/SP_DeadTree, FG pines/dead trees, Megascans ledge trees) is now one of these.
    // "SK:<name>" resolves ThirdParty/Sketchfab/**/SKF_<name>.fbx; Place() scales it so scale 1 = the reference height below
    // and lifts it so the base of the mesh sits on the ground.
    const string SkDir = Root + "/ThirdParty/Sketchfab";
    static readonly Dictionary<string, float> SkRef = new Dictionary<string, float>
    {
        { "Con_PineTree_1", 12f }, { "Con_FirTree_0", 11.5f }, { "Con_NobleFirTree_2", 7.5f }, { "Con_ConiferousShrub_3", 2.4f },
        { "Con_Coniferous_Sapling_4", 2.2f }, { "Con_Coniferous_Dead_Tree_6", 10f }, { "Con_Coniferous_Dead_Tree_2_7", 8f },
        { "Dead_Object_0_1", 2.6f }, { "Dead_Object_1_3", 1.6f }, { "Dead_Object_2_5", 2.0f }, { "Dead_Object_3_7", 2.2f },
        { "HeroPine_JBP", 11f }, { "HeroPine_JBP_Snow", 11f },
    };
    // hero pine (matt z chan "Japanese Black Pine", CC BY; 156k tris): bent trunk + cloud pads, the closest to 적송. Kept to
    // <= 13 in the scene: "SK:HERO_<slot>" draws a hero while that slot has budget, else a straight conifer.
    const string SkHero = "SK:HeroPine_JBP_Snow";   // tr4: hull normals on the needle cards + vertex AO (BlenderSource~/veg/hero_pine_prep.py)
    static readonly Dictionary<string, int> skHeroCap = new Dictionary<string, int> { { "FG", 5 }, { "LEDGE", 2 }, { "SIDE", 1 } };
    static Dictionary<string, int> skHeroLeft = new Dictionary<string, int>();
    static int skHeroPlaced;
    static readonly string[] SkFgPines = { "SK:HERO_FG", "SK:Con_PineTree_1", "SK:HERO_FG", "SK:Con_FirTree_0" };
    static readonly string[] SkLedgePines = { "SK:HERO_LEDGE", "SK:Con_PineTree_1", "SK:HERO_LEDGE", "SK:Con_FirTree_0" };
    static readonly string[] SkSidePines = { "SK:HERO_SIDE", "SK:Con_PineTree_1", "SK:Con_FirTree_0", "SK:Con_PineTree_1", "SK:Con_NobleFirTree_2" };
    static int skFallback;
    static string SkResolve(string model)
    {
        if (!model.StartsWith("SK:HERO_")) return model;
        if (!mats.ContainsKey("SP_SK_Bark")) SkSetup();
        string slot = model.Substring(8);
        if (skHeroLeft.TryGetValue(slot, out var left) && left > 0) { skHeroLeft[slot] = left - 1; skHeroPlaced++; return SkHero; }
        return (skFallback++ & 1) == 0 ? "SK:Con_PineTree_1" : "SK:Con_FirTree_0";
    }
    // hero pines always read 9-14 m (Kido), whatever the slot scale was tuned for
    static float SkScaleFix(string model, float scale) => model == SkHero ? Mathf.Clamp(scale * 11f, 9f, 14f) / 11f : scale;
    // mirror every other hero (negative X scale) for variety
    static void SkPost(GameObject go, string model, float x, float z)
    {
        // tr12: the straight dead conifer trunks threw long thin line shadows across the yard (view 02)
        if (model.Contains("Dead_Tree")) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        if (model != SkHero) return;
        if (((Mathf.FloorToInt(x * 3.1f) ^ Mathf.FloorToInt(z * 1.7f)) & 1) == 1) { var s = go.transform.localScale; go.transform.localScale = new Vector3(-s.x, s.y, s.z); }
        go.transform.position -= Vector3.up * 0.3f;   // tr4: the root flare read as a reddish cone
        Log($"SK hero at ({x:0.0}, {z:0.0}) under {go.transform.parent?.name}");
    }
    static readonly string[] SkPines = { "SK:Con_PineTree_1", "SK:Con_PineTree_1", "SK:Con_FirTree_0", "SK:Con_NobleFirTree_2" };
    static readonly string[] SkDeads = { "SK:Con_Coniferous_Dead_Tree_6", "SK:Con_Coniferous_Dead_Tree_2_7", "SK:Con_Coniferous_Dead_Tree_6" };
    static readonly string[] SkBorderTrees = { "SK:Con_Coniferous_Dead_Tree_6", "SK:Con_Coniferous_Dead_Tree_2_7", "SK:Con_PineTree_1", "SK:Con_FirTree_0" };
    static readonly string[] SkFarTrees = { "SK:Con_Coniferous_Dead_Tree_6", "SK:Con_FirTree_0", "SK:Con_PineTree_1" };
    static readonly string[] SkSnags = { "SK:Dead_Object_0_1", "SK:Dead_Object_1_3", "SK:Dead_Object_2_5", "SK:Dead_Object_3_7" };
    static readonly Dictionary<string, (float norm, float baseY)> skDims = new Dictionary<string, (float, float)>();

    static string SkPath(string model)
    {
        string name = "SKF_" + model.Substring(3) + ".fbx";
        foreach (var sub in new[] { "/Coniferous/", "/DeadTrees/", "/HeroPine/" })
            if (System.IO.File.Exists(ProjAbsVg(SkDir + sub + name))) return SkDir + sub + name;
        return SkDir + "/" + name;
    }
    static string ProjAbsVg(string p) => System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, p);

    static GameObject SkSrc(string model)
    {
        if (!mats.ContainsKey("SP_SK_Bark")) SkSetup();
        var path = SkPath(model);
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!src) Log("MISSING model " + path);
        return src;
    }

    static (float norm, float baseY) SkMeasure(string model)
    {
        if (skDims.TryGetValue(model, out var d)) return d;
        var src = SkSrc(model);
        float lo = float.MaxValue, hi = float.MinValue;
        if (src)
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var b = mf.sharedMesh.bounds; var m = Matrix4x4.TRS(Vector3.zero, src.transform.localRotation, src.transform.localScale) * src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float y = m.MultiplyPoint3x4(c).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            }
        float h = hi > lo ? hi - lo : 1f;
        float want = SkRef.TryGetValue(model.Substring(3), out var r) ? r : h;
        d = (want / h, hi > lo ? lo : 0f);
        skDims[model] = d;
        Log($"SK {model}: native height {h:0.00} m -> {want} m (x{d.norm:0.000}), base y {d.baseY:0.00}, root rot {src?.transform.localRotation.eulerAngles}");
        return d;
    }
    static float SkNorm(string model) => SkMeasure(model).norm;
    static float SkBase(string model) => SkMeasure(model).baseY;

    static Dictionary<Material, Material> SkFgSwap()
    {
        if (!mats.ContainsKey("SP_SK_Bark")) SkSetup();
        var d = new Dictionary<Material, Material>();
        foreach (var (a, b) in new[] { ("SP_SK_Bark", "SP_SK_FG_Bark"), ("SP_SK_Branch", "SP_SK_FG_Branch"), ("SP_SK_DeadBark", "SP_SK_FG_DeadBark"),
                                       ("SP_SK_HeroBark", "SP_SK_FG_HeroBark"), ("SP_SK_HeroNeedles", "SP_SK_FG_HeroNeedles"), ("SP_SK_HeroCore", "SP_SK_FG_HeroCore") })
            if (mats.TryGetValue(a, out var ma) && mats.TryGetValue(b, out var mb) && ma && mb) d[ma] = mb;
        return d;
    }

    // materials + texture import + FBX material remap (BarkMat* / BranchMat* / DT_Material* / Boulder*)
    static void SkSetup()
    {
        string C = SkDir + "/Coniferous/Textures/", D = SkDir + "/DeadTrees/Textures/";
        var barkA = Tex(C + "Image_0.png"); var barkN = Tex(C + "Image_1.png", true);
        var brA = Tex(C + "Image_2.png"); var brN = Tex(C + "Image_3.png", true);
        var deadA = Tex(D + "Image_0.png"); var deadN = Tex(D + "Image_2.png", true);
        var bmA = Tex(C + "Image_4.png", false, false, false, 1024); var bmN = Tex(C + "Image_5.png", true, false, false, 1024);
        var bpA = Tex(C + "Image_6.png", false, false, false, 1024); var bpN = Tex(C + "Image_7.png", true, false, false, 1024);
        var bi = AssetImporter.GetAtPath(C + "Image_2.png") as TextureImporter;
        if (bi && (!bi.alphaIsTransparency || !bi.mipMapsPreserveCoverage || bi.alphaSource != TextureImporterAlphaSource.FromInput))
        {
            bi.alphaIsTransparency = true; bi.alphaSource = TextureImporterAlphaSource.FromInput;
            bi.mipMapsPreserveCoverage = true; bi.alphaTestReferenceValue = 0.5f; bi.SaveAndReimport();
            brA = AssetDatabase.LoadAssetAtPath<Texture2D>(C + "Image_2.png");
        }
        // ss5 look: dark red-brown pine bark, desaturated green-black needles, grey dead wood; snow via _SnowClump
        var bark = Veg("SP_SK_Bark", barkA, barkN, new Color(0.95f, 0.78f, 0.72f), false, 0f, 0.03f);
        var branch = Veg("SP_SK_Branch", brA, brN, new Color(0.46f, 0.48f, 0.42f), true, 0f, 0.25f);   // tr30 (QA #10): darker needles (0.66/0.58/0.44 read pale)
        var dead = Veg("SP_SK_DeadBark", deadA, deadN, new Color(0.72f, 0.68f, 0.64f), false, 0f, 0.03f);
        // the downloaded meshes carry no vertex colours: no vertex AO, no vertex-driven sway (A = 0)
        void Snow(Material m, float cover, float wrap) { if (!m) return; m.SetFloat("_SnowClump", 1f); m.SetFloat("_SnowCover", cover); m.SetFloat("_Wrap", wrap); m.SetFloat("_SnowBias", -0.3f); m.SetFloat("_AOStrength", 0f); m.SetFloat("_GroundMelt", 0f); }
        Snow(bark, 0.42f, 0.2f); Snow(branch, 0.5f, 0.45f); Snow(dead, 0.22f, 0.2f);   // tr30: branch cover 0.64 -> 0.5, cushions only on the up-facing clumps
        if (dead) dead.SetFloat("_SnowClump", 0.55f);   // tr4: snag/stump tops must not read as white discs
        // hero pine: warm red-brown bark (적송), needles dark green-black instead of the source lime, a dark core volume
        // (Default_1, inside the pads) so the pads read solid; snow cushions on top of the pads via _SnowClump
        string H = SkDir + "/HeroPine/Textures/";
        var hbA = Tex(H + "Standardmaterial_baseColor.jpeg", false, false, false, 512); var hbN = Tex(H + "Standardmaterial_normal.png", true, false, false, 512);
        var hnA = Tex(VgTex + "SP_SK_HeroNeedles.png", false, false, false, 512);
        var hi = AssetImporter.GetAtPath(VgTex + "SP_SK_HeroNeedles.png") as TextureImporter;
        if (hi && (!hi.alphaIsTransparency || !hi.mipMapsPreserveCoverage || hi.alphaSource != TextureImporterAlphaSource.FromInput))
        {
            hi.alphaIsTransparency = true; hi.alphaSource = TextureImporterAlphaSource.FromInput;
            hi.mipMapsPreserveCoverage = true; hi.alphaTestReferenceValue = 0.5f; hi.SaveAndReimport();
            hnA = AssetDatabase.LoadAssetAtPath<Texture2D>(VgTex + "SP_SK_HeroNeedles.png");
        }
        var hBark = Veg("SP_SK_HeroBark", hbA, hbN, new Color(0.9f, 0.62f, 0.52f), false, 0f, 0f);   // tr6: 1.15/0.72/0.55 read as a red rock at the base
        // tr8 (Kido): winter needles - greyed (_Desat 0.88), warmed to dull grey/yellow-brown, ~10-15 % green left
        var hNeedles = Veg("SP_SK_HeroNeedles", hnA, null, new Color(0.34f, 0.34f, 0.27f), true, 0f, 0f);   // tr30: darker
        var hCore = Veg("SP_SK_HeroCore", null, null, new Color(0.09f, 0.12f, 0.1f), false, 0f, 0f);
        Snow(hBark, 0.38f, 0.2f); Snow(hNeedles, 0.55f, 0.5f); Snow(hCore, 0.5f, 0.4f);   // tr30: needles cover 0.7 -> 0.55
        // tr4: the hero carries vertex AO (dark trunk base); the hull core stays dark (its snow was the pale disc in the yard)
        if (hBark) hBark.SetFloat("_AOStrength", 0.85f);
        if (hNeedles) hNeedles.SetFloat("_AOStrength", 0.5f);
        foreach (var nm in new[] { hNeedles, branch, hCore }) if (nm) nm.SetFloat("_Desat", 0.88f);   // Kido tr8 winter needles: kept (tr30 only darkens)
        foreach (var nm in new[] { hNeedles, branch, hCore }) if (nm) nm.SetFloat("_SnowScale", 1.8f);   // tr20: 0.55-1 scale snow covered whole cards (white flakes)
        if (hCore) hCore.SetColor("_Tint", new Color(0.133f, 0.165f, 0.141f));   // tr30 (QA #10): core #222a24
        if (hCore) { hCore.SetFloat("_SnowClump", 0f); hCore.SetFloat("_SnowBias", -1f); }
        if (hNeedles) hNeedles.SetFloat("_Cutoff", 0.5f);
        FG("SP_SK_FG_HeroBark", hbA, false); FG("SP_SK_FG_HeroNeedles", hnA, true); FG("SP_SK_FG_HeroCore", null, false);
        if (mats.TryGetValue("SP_SK_FG_HeroCore", out var fgc) && fgc) fgc.SetFloat("_Frost", 0f);   // tr2: the core volume read as a grey plate
        foreach (var k in skHeroCap.Keys.ToList()) skHeroLeft[k] = skHeroCap[k];
        skHeroPlaced = 0;
        if (branch) branch.SetFloat("_Cutoff", 0.5f);
        Prop("SP_SK_BoulderMossy", bmA, bmN, new Color(0.75f, 0.75f, 0.72f), 0.12f, 0.55f);
        Prop("SP_SK_BoulderPlain", bpA, bpN, new Color(0.8f, 0.8f, 0.8f), 0.12f, 0.45f);
        FG("SP_SK_FG_Bark", barkA, false); FG("SP_SK_FG_Branch", brA, true); FG("SP_SK_FG_DeadBark", deadA, false);

        var byPrefix = new (string prefix, string mat)[] { ("BarkMat", "SP_SK_Bark"), ("BranchMat", "SP_SK_Branch"), ("DT_Material", "SP_SK_DeadBark"), ("BoulderMossy", "SP_SK_BoulderMossy"), ("BoulderPlain", "SP_SK_BoulderPlain"),
            ("Standardmaterial", "SP_SK_HeroBark"), ("wire_", "SP_SK_HeroNeedles"), ("Default_1", "SP_SK_HeroCore") };
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { SkDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!imp || !go) continue;
            bool changed = false;
            if (imp.importAnimation || imp.importCameras || imp.importLights) { imp.importAnimation = false; imp.importCameras = false; imp.importLights = false; changed = true; }
            var map = imp.GetExternalObjectMap();
            var names = new HashSet<string>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) names.Add(m.name);
            foreach (var kv in map) if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
            foreach (var n in names)
            {
                var hit = byPrefix.FirstOrDefault(p => n.StartsWith(p.prefix));
                if (hit.mat == null || !mats.TryGetValue(hit.mat, out var mm) || !mm) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (map.TryGetValue(id, out var cur) && cur == mm) continue;
                imp.AddRemap(id, mm); changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    // a few downloaded snags / broken stumps in the outer band (dead wood at eye level next to the big trees)
    static void SkSnagsPlace(Transform parent, System.Random vr, System.Func<float, float, float, bool> blocked)
    {
        float Rv(float a, float b) => a + (float)vr.NextDouble() * (b - a);
        int n = 0;
        for (int i = 0; i < 300 && n < 18; i++)
        {
            float x = Rv(-HX - 4f, HX + 4f), z = Rv(-HZ - 4f, HZ + 4f);
            float e = Mathf.Min(HX - Mathf.Abs(x), HZ - Mathf.Abs(z));
            if (e > 6f || e < -4f || (x < -HX + 20f && Mathf.Abs(z) < HZ - 6f)) continue;
            if (blocked(x, z, 1f)) continue;
            if (Place(SkSnags[vr.Next(SkSnags.Length)], x, z, Rv(0, 360), Rv(0.8f, 1.35f), parent, true, 0.1f)) n++;
        }
        Log($"SK snags: {n}");
    }
    // ------------------------------------------------------------------ shared tree prefabs (Kido: reuse in the abandoned village)
    // ThirdParty/Trees/{Textures,Materials,Prefabs}: copies of the SP_SK_* materials (TR_*) pointing at copied textures,
    // and one prefab per tree, pre-scaled to its game height with the base at the origin. Snow = _SnowClump/_SnowCover.
    const string TrDir = Root + "/ThirdParty/Trees";
    static void SkMakePrefabs()
    {
        foreach (var sub in new[] { "Textures", "Materials", "Prefabs" })
            if (!AssetDatabase.IsValidFolder(TrDir + "/" + sub)) AssetDatabase.CreateFolder(TrDir, sub);
        var texMap = new Dictionary<Texture, Texture>();
        Texture CopyTex(Texture t)
        {
            if (!t) return null;
            if (texMap.TryGetValue(t, out var c)) return c;
            var src = AssetDatabase.GetAssetPath(t);
            if (string.IsNullOrEmpty(src) || src.StartsWith(TrDir)) return texMap[t] = t;
            var name = src.Contains("/HeroPine/") ? "Hero_" + System.IO.Path.GetFileName(src)
                     : src.Contains("/DeadTrees/") ? "Dead_" + System.IO.Path.GetFileName(src)
                     : src.Contains("/Coniferous/") ? "Con_" + System.IO.Path.GetFileName(src) : System.IO.Path.GetFileName(src);
            var dst = TrDir + "/Textures/" + name;
            if (!System.IO.File.Exists(ProjAbsVg(dst))) AssetDatabase.CopyAsset(src, dst);
            return texMap[t] = AssetDatabase.LoadAssetAtPath<Texture>(dst);
        }
        var matMap = new Dictionary<Material, Material>();
        foreach (var n in new[] { "SP_SK_Bark", "SP_SK_Branch", "SP_SK_DeadBark", "SP_SK_HeroBark", "SP_SK_HeroNeedles", "SP_SK_HeroCore" })
        {
            if (!mats.TryGetValue(n, out var m) || !m) continue;
            var path = TrDir + "/Materials/" + n.Replace("SP_SK_", "TR_") + ".mat";
            var tm = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!tm) { tm = new Material(m); AssetDatabase.CreateAsset(tm, path); }
            else { tm.shader = m.shader; tm.CopyPropertiesFromMaterial(m); }
            foreach (var p in new[] { "_BaseMap", "_BumpMap" }) if (tm.HasProperty(p)) tm.SetTexture(p, CopyTex(m.GetTexture(p)));
            tm.enableInstancing = true;
            EditorUtility.SetDirty(tm);
            matMap[m] = tm;
        }
        var list = new (string prefab, string model, bool mirror)[]
        {
            ("TR_HeroPine_A", SkHero, false), ("TR_HeroPine_B", SkHero, true),
            ("TR_Pine", "SK:Con_PineTree_1", false), ("TR_Fir", "SK:Con_FirTree_0", false), ("TR_NobleFir", "SK:Con_NobleFirTree_2", false),
            ("TR_DeadConifer_A", "SK:Con_Coniferous_Dead_Tree_6", false), ("TR_DeadConifer_B", "SK:Con_Coniferous_Dead_Tree_2_7", false),
            ("TR_Snag_A", "SK:Dead_Object_0_1", false), ("TR_Snag_B", "SK:Dead_Object_1_3", false), ("TR_Snag_C", "SK:Dead_Object_2_5", false), ("TR_Snag_D", "SK:Dead_Object_3_7", false),
        };
        int made = 0;
        foreach (var (prefab, model, mirror) in list)
        {
            var src = SkSrc(model); if (!src) continue;
            float k = SkNorm(model);
            var rootGo = new GameObject(prefab);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src, rootGo.transform);
            inst.transform.localRotation = src.transform.localRotation;
            var s = src.transform.localScale * k; if (mirror) s.x = -s.x;
            inst.transform.localScale = s;
            inst.transform.localPosition = Vector3.up * (-SkBase(model) * k);
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
                r.sharedMaterials = r.sharedMaterials.Select(m => m && matMap.TryGetValue(m, out var t) ? t : m).ToArray();
            PrefabUtility.SaveAsPrefabAsset(rootGo, TrDir + "/Prefabs/" + prefab + ".prefab");
            Object.DestroyImmediate(rootGo);
            made++;
        }
        AssetDatabase.SaveAssets();
        Log($"SK prefabs: {made} in {TrDir}/Prefabs");
    }
    // ------------------------------------------------------------------ tr7: the pale oval "disc" left of the lantern yard
    // A ~5 m flat pale mesh with rubble on top that survived every tree change (ly6..tr6). Runs after every builder: logs
    // what is near it, then removes flat wide meshes (>= 3 m across, height <= 35 % of the width) near the yard that are
    // not ground / ice / architecture / vegetation, and every other instance of the same mesh in the scene.
    public static void VgPostClean()
    {
        // tr10: no scatter instance and no flat (<= 35 %) renderer matched, so: every renderer within 10 m of the yard
        // is logged; mound-like ones (2.5-9 m across, height <= 60 % of the width, not a lantern) are switched off
        var yard = new Vector2(Clusters[0].x, Clusters[0].y);
        var skipGroups = new HashSet<string> { "Ground", "FogVfx", "FrozenRiver", "Ice", "Walls" };
        string Group0(Transform t) { while (t.parent && t.parent != root) t = t.parent; return t.name; }
        int off = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var b = r.bounds; var c = new Vector2(b.center.x, b.center.z);
            if (Vector2.Distance(c, yard) > 10f) continue;
            var mf = r.GetComponent<MeshFilter>(); string g = Group0(r.transform);
            float wide = Mathf.Max(b.size.x, b.size.z);
            Log($"VEG yard renderer: {r.name} group={g} mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name : r.GetType().Name)} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")} centre=({b.center.x:0.0}, {b.center.z:0.0}) size={b.size}");
            if (skipGroups.Contains(g) || r.name.Contains("Lantern") || r.name.Contains("Glow") || r.name.Contains("Cone")) continue;
            if (wide >= 2.5f && wide <= 9f && b.size.y <= wide * 0.6f) { r.enabled = false; off++; Log($"VEG yard: switched off {r.name} ({g})"); }
        }
        Log($"VEG yard clean: {off} renderers off");
    }
    // ------------------------------------------------------------------ tr9: the pale oval by the lantern yard
    // Still there with the packed draw off (tr8_nopack) and no scene renderer matched (tr7), so it is an instanced
    // scatter entry. Every SPScatter set in the scene is scanned: entries near the yard whose footprint (mesh bounds x
    // scale) is >= 2.5 m are logged; flat ones (height <= 45 % of the width) are dropped, and the same mesh is dropped
    // everywhere inside the arena (the lead: remove that object type, not just the one instance).
    public static void VgDiscFix()
    {
        var yard = new Vector2(Clusters[0].x, Clusters[0].y);
        var flat = new HashSet<Mesh>();
        var scs = root.GetComponentsInChildren<SPScatter>(true);
        foreach (var sc in scs)
            foreach (var s in sc.sets)
            {
                if (!s.mesh) continue;
                var sz = s.mesh.bounds.size * s.preScale; sz = s.pre * sz; sz = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
                for (int i = 0; i < s.at.Count; i++)
                {
                    var a = s.at[i]; float k = s.scale[i];
                    float wide = Mathf.Max(sz.x, sz.z) * k, high = sz.y * k;
                    if (wide < 2.5f || Vector2.Distance(new Vector2(a.x, a.z), yard) > 16f) continue;
                    Log($"VEG disc probe: {sc.name}/{s.mesh.name} mat={(s.mat ? s.mat.name : "-")} at ({a.x:0.0}, {a.z:0.0}) {wide:0.0} x {high:0.0} m");
                    if (high <= wide * 0.45f) flat.Add(s.mesh);
                }
            }
        int removed = 0;
        foreach (var sc in scs)
        {
            bool dirty = false;
            foreach (var s in sc.sets)
            {
                if (!flat.Contains(s.mesh)) continue;
                for (int i = s.at.Count - 1; i >= 0; i--)
                {
                    if (!InArena(s.at[i].x, s.at[i].z, -6f)) continue;
                    s.at.RemoveAt(i); s.scale.RemoveAt(i); removed++; dirty = true;
                }
            }
            if (dirty) { sc.Push(); EditorUtility.SetDirty(sc); }
        }
        Log($"VEG disc fix: {removed} instances removed, meshes [{string.Join(", ", flat.Select(m => m.name))}]");
    }

    // tr15: no renderer that is a tall thin pole (> 5 m high, < 1.6 m across) casts a shadow: at the 15-25 deg moon they
    // drew 20-40 m straight lines across the arena (view 02). Logs what it switched.
    public static void VgThinShadows()
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
            var b = r.bounds;
            if (b.size.y < 5f || Mathf.Max(b.size.x, b.size.z) > 1.6f) continue;
            r.shadowCastingMode = ShadowCastingMode.Off; n++;
            Log($"VEG thin pole shadow off: {r.name} at ({b.center.x:0.0}, {b.center.z:0.0}) {b.size}");
        }
        Log($"VEG thin pole shadows off: {n}");
    }
    // tr20: the long straight diagonal shadow across the lantern yard (view 02). Samples points along the line on screen
    // (game camera, focus -40, 28, zoom 1), casts a ray from each ground point back toward the moon and counts which
    // shadow-casting renderer's bounds every ray passes through first; the one hit by most rays loses its shadow.
    public static void VgLineShadowProbe()
    {
        Light moon = RenderSettings.sun;
        if (!moon) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { moon = l; break; }
        if (!moon) { Log("VEG line probe: no directional light"); return; }
        var toLight = -moon.transform.forward;
        var focus = new Vector3(-40f, 0f, 28f); var off = new Vector3(0, 17f, -14.5f); var cp = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var casters = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.shadowCastingMode != ShadowCastingMode.Off && !(r is ParticleSystemRenderer)).ToArray();
        var hits = new Dictionary<Renderer, int>();
        const int N = 12;
        for (int i = 0; i < N; i++)
        {
            float t = (i + 0.5f) / N;
            float u = Mathf.Lerp(0.675f, 0.40f, t), v = Mathf.Lerp(0.98f, 0.02f, t);   // the line in view 02 (tr13-tr19)
            var d = fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f);
            float k = (0.2f - cp.y) / d.y; var g = cp + d * k; g.y = Height(g.x, g.z) + 0.1f;
            var ray = new Ray(g, toLight);
            Renderer best = null; float bestD = 1e9f;
            foreach (var r in casters)
            {
                var b = r.bounds;
                if (b.size.x > 25f || b.size.z > 25f) continue;   // ground / sheets
                if (b.IntersectRay(ray, out float dist) && dist > 0.3f && dist < bestD)
                {
                    // thin things only along the ray: the ray must pass within ~1.2 m of the bounds' vertical axis
                    var c = b.center; var p = ray.origin + ray.direction * Vector3.Dot(c - ray.origin, ray.direction);
                    if (new Vector2(p.x - c.x, p.z - c.z).magnitude > Mathf.Max(1.2f, Mathf.Min(b.extents.x, b.extents.z))) continue;
                    best = r; bestD = dist;
                }
            }
            if (best) { hits[best] = hits.TryGetValue(best, out var n0) ? n0 + 1 : 1; Log($"VEG line probe {i}: ground ({g.x:0.0}, {g.z:0.0}) -> {best.name} ({best.transform.parent?.name}) at {bestD:0.0} m, bounds {best.bounds.center} {best.bounds.size}"); }
            else Log($"VEG line probe {i}: ground ({g.x:0.0}, {g.z:0.0}) -> nothing");
        }
        foreach (var kv in hits.OrderByDescending(h => h.Value).Take(4))
        {
            var mf = kv.Key.GetComponent<MeshFilter>();
            Log($"VEG line probe: {kv.Key.name} hit by {kv.Value}/{N} rays, mesh {(mf && mf.sharedMesh ? mf.sharedMesh.name : "-")}, bounds {kv.Key.bounds.center} {kv.Key.bounds.size}");
            if (kv.Value >= 3) { kv.Key.shadowCastingMode = ShadowCastingMode.Off; Log($"VEG line probe: shadow OFF on {kv.Key.name}"); }
        }
    }

    // tr21: the dark rectangle on the left of the west bridge deck (view 15): same probe as the yard line, then no shadow
    public static void VgBridgeShadowProbe()
    {
        var deck = GameObject.Find("SP_MX_Bridge"); if (!deck) { Log("VEG bridge probe: no deck"); return; }
        Light moon = RenderSettings.sun;
        if (!moon) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { moon = l; break; }
        if (!moon) return;
        var toLight = -moon.transform.forward;
        var focus = new Vector3(deck.transform.position.x, 0, deck.transform.position.z); var off = new Vector3(0, 17f, -14.5f) * 1.15f;
        var cp = focus + off; var fwd = (-off).normalized; var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var casters = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.shadowCastingMode != ShadowCastingMode.Off && !(r is ParticleSystemRenderer)).ToArray();
        var hits = new Dictionary<Renderer, int>(); int n = 0;
        foreach (float u in new[] { 0.24f, 0.29f, 0.33f })
            foreach (float v in new[] { 0.35f, 0.45f, 0.55f, 0.65f })
            {
                var d = fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f);
                float k = (0.2f - cp.y) / d.y; var g = cp + d * k; g.y = Height(g.x, g.z) + 0.15f;
                var ray = new Ray(g, toLight); Renderer best = null; float bestD = 1e9f;
                foreach (var r in casters) { var b = r.bounds; if (b.size.x > 40f || b.size.z > 40f) continue; if (b.IntersectRay(ray, out float dist) && dist > 0.2f && dist < bestD) { best = r; bestD = dist; } }
                n++;
                if (best) { hits[best] = hits.TryGetValue(best, out var c0) ? c0 + 1 : 1; Log($"VEG bridge probe: ground ({g.x:0.0}, {g.z:0.0}) -> {best.name} ({best.transform.parent?.name}) {bestD:0.0} m"); }
            }
        foreach (var kv in hits.OrderByDescending(h => h.Value).Take(3))
        {
            Log($"VEG bridge probe: {kv.Key.name} hit by {kv.Value}/{n}, bounds {kv.Key.bounds.center} {kv.Key.bounds.size}");
            if (kv.Value * 3 >= n) { kv.Key.shadowCastingMode = ShadowCastingMode.Off; Log($"VEG bridge probe: shadow OFF on {kv.Key.name}"); }
        }
    }

    // tr23: exact moon-ray probe for the dark rectangle left of the bridge deck (view 15). tr21's probe tested bounding
    // boxes only and blamed a pine (Object_8) whose box happened to cover the rays; this one tests the triangles.
    static bool VgRayMesh(Renderer r, Ray ray, out float dist)
    {
        dist = 1e9f;
        Mesh m = r is SkinnedMeshRenderer sk ? sk.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
        if (!m) return false;
        Vector3[] v; int[] t;
        try { v = m.vertices; t = m.triangles; } catch { return r.bounds.IntersectRay(ray, out dist); }
        if (v == null || v.Length == 0) return r.bounds.IntersectRay(ray, out dist);
        var tr = r.transform; var o = tr.InverseTransformPoint(ray.origin); var d = tr.InverseTransformVector(ray.direction);
        bool hit = false;
        for (int k = 0; k + 2 < t.Length; k += 3)
        {
            Vector3 a = v[t[k]], e1 = v[t[k + 1]] - a, e2 = v[t[k + 2]] - a;
            var p = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) continue;
            float inv = 1f / det; var s = o - a; float u = Vector3.Dot(s, p) * inv; if (u < 0f || u > 1f) continue;
            var q = Vector3.Cross(s, e1); float w = Vector3.Dot(d, q) * inv; if (w < 0f || u + w > 1f) continue;
            float tt = Vector3.Dot(e2, q) * inv;
            if (tt > 0.3f && tt < dist) { dist = tt; hit = true; }
        }
        return hit;
    }

    public static void VgRectShadowProbe()
    {
        var deck = GameObject.Find("SP_MX_Bridge"); if (!deck) { Log("VEG rect probe: no deck"); return; }
        Light moon = RenderSettings.sun;
        if (!moon) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { moon = l; break; }
        if (!moon) return;
        var toLight = -moon.transform.forward;
        var focus = new Vector3(deck.transform.position.x, 0, deck.transform.position.z); var off = new Vector3(0, 17f, -14.5f) * 1.15f;
        var cp = focus + off; var fwd = (-off).normalized; var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var all = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        var casters = all.Where(r => r.shadowCastingMode != ShadowCastingMode.Off).ToArray();
        var hits = new Dictionary<Renderer, int>(); int n = 0;
        foreach (float u in new[] { 0.24f, 0.29f, 0.34f })
            foreach (float v in new[] { 0.35f, 0.5f, 0.65f })
            {
                var d = fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f);
                float k = (0.2f - cp.y) / d.y; var g = cp + d * k; g.y = Height(g.x, g.z) + 0.15f;
                // what surface is drawn here (straight down)
                var down = new Ray(g + Vector3.up * 40f, Vector3.down);
                var surf = all.Where(r => r.bounds.IntersectRay(down)).Select(r => VgRayMesh(r, down, out float dd) ? (r, dd) : (r, -1f)).Where(x => x.Item2 > 0f).OrderBy(x => x.Item2).Take(3)
                    .Select(x => $"{x.r.name}({x.r.transform.parent?.name}, {x.r.sharedMaterial?.name}) y {40f + g.y - x.Item2:0.00}");
                // first shadow caster toward the moon
                var ray = new Ray(g, toLight); Renderer best = null; float bestD = 1e9f;
                foreach (var r in casters) { if (!r.bounds.IntersectRay(ray)) continue; if (VgRayMesh(r, ray, out float dist) && dist < bestD) { best = r; bestD = dist; } }
                n++;
                if (best) hits[best] = hits.TryGetValue(best, out var c0) ? c0 + 1 : 1;
                Log($"VEG rect probe: ground ({g.x:0.0}, {g.y:0.00}, {g.z:0.0}) surface [{string.Join("; ", surf)}] moon hit -> {(best ? $"{best.name} ({best.transform.parent?.name}) {bestD:0.0} m" : "none")}");
            }
        foreach (var kv in hits.OrderByDescending(h => h.Value).Take(3))
        {
            var b = kv.Key.bounds;
            Log($"VEG rect probe: {kv.Key.name} ({kv.Key.transform.parent?.name}) hit by {kv.Value}/{n}, bounds {b.center} {b.size}");
            bool ground = b.size.x > 40f || b.size.z > 40f || kv.Key.name.Contains("Ice") || kv.Key.name.Contains("River") || kv.Key.name == "Ground";
            if (kv.Value * 3 >= n && !ground) { kv.Key.shadowCastingMode = ShadowCastingMode.Off; Log($"VEG rect probe: shadow OFF on {kv.Key.name}"); }
            else if (kv.Value * 3 >= n) Log($"VEG rect probe: {kv.Key.name} is terrain/ice - left alone");
        }
    }

    // tr31: view 14 (hero pine on the west plateau) shows white vertical sticks at the top left; tr30's trunk change did
    // not touch them. Rays from the capture camera through those pixels -> first renderer hit (exact triangles), logged.
    public static void VgViewProbe()
    {
        Transform heroPl = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("SKF_HeroPine") && t.parent && t.parent.name == "WestPlateau") heroPl = t;
        if (!heroPl) { Log("VEG view probe: no plateau hero"); return; }
        var b = new Bounds(heroPl.position, Vector3.zero); foreach (var r in heroPl.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        var focus = new Vector3(b.center.x, 0, b.center.z + b.extents.y * 0.35f);
        var off = new Vector3(0, 17f, -14.5f) * 1.25f; var cp = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var all = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        foreach (var (u, v) in new[] { (0.09f, 0.78f), (0.09f, 0.86f), (0.09f, 0.94f), (0.37f, 0.93f), (0.36f, 0.97f) })
        {
            var ray = new Ray(cp, (fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f)).normalized);
            Renderer best = null; float bd = 1e9f;
            foreach (var r in all) { if (!r.bounds.IntersectRay(ray)) continue; if (VgRayMesh(r, ray, out float d) && d < bd) { bd = d; best = r; } }
            Log(best ? $"VEG view14 probe ({u:0.00}, {v:0.00}): {best.name} ({best.transform.parent?.name}) mat {(best.sharedMaterial ? best.sharedMaterial.name + "/" + best.sharedMaterial.shader.name : "-")} at {ray.GetPoint(bd)} scale {best.transform.lossyScale}"
                      : $"VEG view14 probe ({u:0.00}, {v:0.00}): nothing");
        }
    }

    // tr32 post fixes (after every builder):
    //  a) view 14 white sticks = the downloaded dead conifers (SKF_Con_Coniferous_Dead_Tree_*) on SP_SK_Bark: their bare
    //     trunks took the clumpy snow over their whole length -> own bark copy without snow
    //  b) view 04 pale straw mat over the body (거적): dark wet straw, no snow, no ground tint
    //  c) view 15: the dark hard-edged rectangle left of the west bridge is not a shadow -> rays from the CAPTURE camera
    //     through it, all renderers along each ray logged; the deck's submeshes / materials logged (black plank bands)
    public static void VgPost32()
    {
        // a)
        if (mats.TryGetValue("SP_SK_Bark", out var bark) && bark)
        {
            var dcb = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/SP_SK_DeadConBark.mat");
            if (!dcb) { dcb = new Material(bark); AssetDatabase.CreateAsset(dcb, $"{MatDir}/SP_SK_DeadConBark.mat"); }
            dcb.CopyPropertiesFromMaterial(bark);
            dcb.SetFloat("_SnowClump", 0f); dcb.SetFloat("_SnowCover", 0f); dcb.SetFloat("_SnowBias", -1f);
            dcb.SetColor("_Tint", bark.GetColor("_Tint") * 0.4f); dcb.SetFloat("_RimScale", 0.12f);   /* tr40: 0.8 -> 0.4, the pale bark lit by the moon read as white sticks (14) */ EditorUtility.SetDirty(dcb);   // tr36: rim made the thin trunks white from above
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                bool dead = false; for (var t = r.transform; t && t != root; t = t.parent) if (t.name.Contains("Dead_Tree")) { dead = true; break; }
                if (!dead) continue;
                var ms = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < ms.Length; i++) if (ms[i] == bark) { ms[i] = dcb; ch = true; }
                if (ch) { r.sharedMaterials = ms; n++; }
            }
            Log($"VEG post: {n} dead-conifer renderers -> SP_SK_DeadConBark (no snow on the bare trunks)");
        }
        // b)
        var strawMats = new HashSet<Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("SP_StrawMat") || (r.transform.parent && r.transform.parent.name.StartsWith("SP_StrawMat")))
                foreach (var m in r.sharedMaterials) if (m) strawMats.Add(m);
        foreach (var m in strawMats)
        {
            Log($"VEG post: straw mat material {m.name} ({m.shader.name})");
            if (!m.shader.name.Contains("Prop")) continue;
            m.SetColor("_Tint", Hex("#3a3328")); m.SetFloat("_Wetness", 0.55f); m.SetFloat("_SnowBias", -1f);
            m.SetFloat("_VertexFrost", 0f); m.SetFloat("_GroundMelt", 0f); m.SetFloat("_Desaturate", 0.35f); m.SetFloat("_SnowCap", 0f);
            EditorUtility.SetDirty(m);
        }
        // c)
        var deck = GameObject.Find("SP_MX_Bridge"); if (!deck) { Log("VEG view15 probe: no deck"); return; }
        var dr = deck.GetComponent<Renderer>(); var dmf = deck.GetComponent<MeshFilter>();
        if (dr) Log($"VEG deck: {(dmf && dmf.sharedMesh ? dmf.sharedMesh.subMeshCount : 0)} submeshes, materials [{string.Join(", ", dr.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null"))}]");
        var focus = new Vector3(deck.transform.position.x, 0, deck.transform.position.z);
        var off = new Vector3(0, 17f, -14.5f) * 1.15f; var cp = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var all = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        var first = new Dictionary<Renderer, int>(); int rays = 0;
        foreach (float u in new[] { 0.24f, 0.29f, 0.34f })
            foreach (float v in new[] { 0.35f, 0.5f, 0.65f })
            {
                var ray = new Ray(cp, (fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f)).normalized);
                var hits = new List<(Renderer r, float d)>();
                foreach (var r in all) { if (!r.bounds.IntersectRay(ray)) continue; if (VgRayMesh(r, ray, out float d)) hits.Add((r, d)); }
                hits.Sort((a, b) => a.d.CompareTo(b.d)); rays++;
                if (hits.Count > 0) first[hits[0].r] = first.TryGetValue(hits[0].r, out var c0) ? c0 + 1 : 1;
                Log($"VEG view15 ray ({u:0.00}, {v:0.00}): " + string.Join(" | ", hits.Take(4).Select(h => $"{h.r.name} ({h.r.transform.parent?.name}) {(h.r.sharedMaterial ? h.r.sharedMaterial.name : "-")} y {ray.GetPoint(h.d).y:0.00}")));
            }
        foreach (var kv in first.OrderByDescending(k => k.Value))
        {
            var r = kv.Key; var b = r.bounds; string g = r.transform.parent ? r.transform.parent.name : "";
            Log($"VEG view15 first hit: {r.name} ({g}) {kv.Value}/{rays}, bounds {b.center} {b.size}");
            // a flat sheet that is not the ground / ice / fog: switch it off (the rectangle)
            bool floor = r.name == "Ground" || r.name.Contains("River") || r.name.Contains("Ice") || r.name.StartsWith("FogSheet") || g == "FogVfx";
            if (kv.Value * 2 > rays && !floor && b.size.y < 1.5f && (b.size.x > 3f || b.size.z > 3f)) { r.enabled = false; Log($"VEG view15: {r.name} switched off (flat dark sheet)"); }
        }
    }

    // tr36: view 04's pale flat shape (top right) - rays from the capture camera, everything along each ray logged
    public static void VgProbe04()
    {
        var focus = new Vector3(6f, 0, -HZ + 1.5f);
        var off = new Vector3(0, 17f, -14.5f); var cp = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var all = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        foreach (var (u, v) in new[] { (0.94f, 0.80f), (0.96f, 0.83f), (0.98f, 0.81f), (0.97f, 0.86f) })
        {
            var ray = new Ray(cp, (fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f)).normalized);
            var hits = new List<(Renderer r, float d)>();
            foreach (var r in all) { if (!r.bounds.IntersectRay(ray)) continue; if (VgRayMesh(r, ray, out float d)) hits.Add((r, d)); }
            hits.Sort((a, b) => a.d.CompareTo(b.d));
            Log($"VEG view04 ray ({u:0.00}, {v:0.00}): " + string.Join(" | ", hits.Take(3).Select(h => $"{h.r.name} ({h.r.transform.parent?.name}) {(h.r.sharedMaterial ? h.r.sharedMaterial.name : "-")} at {ray.GetPoint(h.d)}")));
        }
    }

    // tr39: view 14's white diagonal stick - rays along it from the capture camera; the object hit by most rays is logged
    // with its material, and if it is a bare dead trunk / snag (outside the arena) it is switched off
    public static void VgProbe14b()
    {
        Transform heroPl = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("SKF_HeroPine") && t.parent && t.parent.name == "WestPlateau") heroPl = t;
        if (!heroPl) { Log("VEG 14b: no plateau hero"); return; }
        var b = new Bounds(heroPl.position, Vector3.zero); foreach (var r in heroPl.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        var focus = new Vector3(b.center.x, 0, b.center.z + b.extents.y * 0.35f);
        var off = new Vector3(0, 17f, -14.5f) * 1.25f; var cp = focus + off; var fwd = (-off).normalized;
        var right = Vector3.Cross(Vector3.up, fwd).normalized; var up = Vector3.Cross(fwd, right);
        float tv = Mathf.Tan(15f * Mathf.Deg2Rad), th = tv * 16f / 9f;
        var all = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        var count = new Dictionary<Renderer, int>(); int rays = 0;
        for (int k = 0; k < 7; k++)
        {
            float s = k / 6f, u = Mathf.Lerp(0.05f, 0.11f, s), v = Mathf.Lerp(0.95f, 0.75f, s);
            var ray = new Ray(cp, (fwd + right * th * (2f * u - 1f) + up * tv * (2f * v - 1f)).normalized);
            var hits = new List<(Renderer r, float d)>();
            foreach (var r in all) { if (!r.bounds.IntersectRay(ray)) continue; if (VgRayMesh(r, ray, out float d)) hits.Add((r, d)); }
            hits.Sort((a, c) => a.d.CompareTo(c.d)); rays++;
            if (hits.Count > 0) count[hits[0].r] = count.TryGetValue(hits[0].r, out var c0) ? c0 + 1 : 1;
            Log($"VEG 14b ray ({u:0.00}, {v:0.00}): " + string.Join(" | ", hits.Take(3).Select(h => $"{h.r.name} ({h.r.transform.parent?.name}) {(h.r.sharedMaterial ? h.r.sharedMaterial.name : "-")} size {h.r.bounds.size}")));
        }
        foreach (var kv in count.OrderByDescending(x => x.Value).Take(2))
        {
            var r = kv.Key; string n = r.name + "/" + (r.transform.parent ? r.transform.parent.name : "");
            Log($"VEG 14b first hit {n} {kv.Value}/{rays}, mat {(r.sharedMaterial ? r.sharedMaterial.name : "-")}, bounds {r.bounds.center} {r.bounds.size}");
            bool bare = n.Contains("Dead") || n.Contains("Snag") || n.Contains("Trunk") || n.Contains("Log") || (r.sharedMaterial && (r.sharedMaterial.name.Contains("Bark") || r.sharedMaterial.name.Contains("Dead")));
            bool hero = n.Contains("HeroPine");
            if (kv.Value >= 2 && bare && !hero && !InArena(r.bounds.center.x, r.bounds.center.z)) { r.enabled = false; Log($"VEG 14b: {r.name} switched off (white stick)"); }   // tr40: both overlapping copies (5/7 and 2/7)
        }
    }
}
