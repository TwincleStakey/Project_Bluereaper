// 11-11 redo of the 폐촌 field from Kido's reference board (https://claude.ai/artifact/BFvGkUmtbVoBscqHT9xyke):
// - ruin islands on a ring just inside the arena edge (collapsed 초가, broken 돌담, 장독대 with smashed jars, big dead
//   trees on low knolls), so the game camera always has a ruin, a tree or a wall in frame; the middle stays open;
// - vegetation in natural clusters: each clump mixes 2-4 kinds with 2-3x size differences, clumps of very different size,
//   dense around ruins/walls/edge, sparse and small in the middle, bare mud between;
// - Megascans dead shrubs (Fab, free) next to the kit dry grass and Poly Haven grass/fern.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class AVBuilder
{
    enum Isl { Ruin, Jars, Wall, Tree }
    struct Island { public float x, z, yaw; public Isl kind; public Island(float x, float z, Isl k, float yaw) { this.x = x; this.z = z; kind = k; this.yaw = yaw; } }

    // Arena: x -46..46, z -48.5..41.5. Islands sit 4-12 m inside the edge; the middle ~60 x 55 m is left open.
    static readonly Island[] Islands =
    {
        new Island(-37f, 31f, Isl.Ruin, 15f),
        new Island(-13f, 34.5f, Isl.Jars, 0f),
        new Island(15f, 33f, Isl.Tree, 0f),
        new Island(37f, 29f, Isl.Wall, -35f),
        new Island(39f, 4f, Isl.Ruin, -95f),
        new Island(38f, -24f, Isl.Jars, 20f),
        new Island(22f, -40f, Isl.Ruin, 172f),
        new Island(-5f, -41f, Isl.Tree, 0f),
        new Island(-29f, -38f, Isl.Wall, 35f),
        new Island(-39f, -14f, Isl.Ruin, 88f),
        new Island(-38.5f, 9f, Isl.Tree, 0f),
    };

    // 11-13 QA (Kido's arena rule: nothing inside the arena blocks movement, projectiles or changes height): the knolls,
    // fallen trunks, stumps, moss rocks and solid rubble walls of the islands moved out to the arena edge; inside, the
    // islands keep only flat dressing (rubble patches without colliders, jars, mats, litter).
    static Vector2 EdgeOut(float x, float z, float outM = 2.5f)
    {
        float dl = x + ArenaHalfX, dr = ArenaHalfX - x, db = z - ArenaMinZ, dt = ArenaMaxZ - z;
        float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
        if (m == dl) return new Vector2(-ArenaHalfX - outM, z);
        if (m == dr) return new Vector2(ArenaHalfX + outM, z);
        if (m == db) return new Vector2(x, ArenaMinZ - outM);
        return new Vector2(x, ArenaMaxZ + outM);
    }

    static float IslandMound(float x, float z)
    {
        return 0f;   // 11-13 QA: no knolls inside the arena
#pragma warning disable CS0162
        float y = 0f;
        foreach (var s in Islands)
        {
            if (s.kind != Isl.Tree) continue;
            float d = new Vector2(x - s.x, z - s.z).magnitude;
            y += 1.1f * Mathf.SmoothStep(1f, 0f, Mathf.Clamp01((d - 1.5f) / 4.5f));
        }
        return y;
#pragma warning restore CS0162
    }

    static float IslandNear(float x, float z)
    {
        float best = 0f;
        foreach (var s in Islands)
        {
            float d = new Vector2(x - s.x, z - s.z).magnitude;
            best = Mathf.Max(best, 1f - Mathf.Clamp01((d - 3f) / 6f));
        }
        return best;
    }

    static void RemapMegascans()
    {
        // one material per Megascans folder: plants = AV_MS_<f>_RGBA.png (alpha clip, two-sided), props = AV_MS_<f>_A.jpg
        var tints = new Dictionary<string, string> { { "shrubs", "#8d8478" }, { "WildGreen", "#949274" }, { "Clumps", "#a29e8a" }, { "WildDry", "#a89e82" },
                                                     { "DryGrass", "#8f866c" }, { "Pot", "#6a5a50" }, { "Pebbles", "#968f86" }, { "KJar", "#d8c4b0" }, { "KStrawJar", "#9a9080" }, { "KStrawMat", "#6e6452" }, { "SmallRocks", "#968f86" }, { "RubblePatch", "#7a746c" }, { "Tile", "#46464a" }, { "Skulls", "#8a8274" }, { "Sack", "#5e564a" }, { "KSickle", "#5e564a" }, { "KHoe", "#5e564a" }, { "KShovel", "#5e564a" }, { "KBroom", "#5e564a" }, { "KLid", "#5e564a" }, { "KBasket", "#7a6e5c" }, { "KSieve", "#7a6e5c" }, { "KWinnow", "#7a6e5c" }, { "KSpoon", "#6a6052" }, { "KBowl", "#6a6052" }, { "KWoodBowl", "#6a6052" }, { "KBasin", "#6a6052" }, { "KGourd", "#7a6e5c" }, { "KSoban", "#6a5a4a" }, { "Planks", "#6a6052" }, { "Debris", "#4e473f" }, { "Sticks", "#4c443c" }, { "KStraw", "#6a604c" }, { "KStrawBag", "#7a705c" }, { "stonewall", "#8f8c86" } };
        var byFolder = new Dictionary<string, Material>();
        foreach (var dir in System.IO.Directory.GetDirectories(MS))
        {
            var f = System.IO.Path.GetFileName(dir);
            if (f == "mud" || f == "thatch" || f == "Decals") continue;
            var rgba = $"{MS}{f}/AV_MS_{f}_RGBA.png"; bool plant = System.IO.File.Exists(rgba);
            if (plant && AssetImporter.GetAtPath(rgba) is TextureImporter ti && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var m = Lit("AV_MS_" + f, TP(plant ? rgba : $"{MS}{f}/AV_MS_{f}_A.jpg"), TP($"{MS}{f}/AV_MS_{f}_N.jpg", true),
                        Hex(tints.TryGetValue(f, out var t) ? t : "#ffffff"), 1f, plant ? 0.08f : (f == "Pot" ? 0.15f : 0.15f), 1f, plant);
            if (plant) { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); }
            byFolder[f] = m;
        }
        // the kit thatch gets the Megascans mossy thatch maps (every 초가 roof)
        var th = mats["AV_Thatch"];
        th.SetTexture("_BaseMap", TP(MS + "thatch/AV_MS_Thatch_A.jpg")); th.SetTexture("_BumpMap", TP(MS + "thatch/AV_MS_Thatch_N.jpg", true));
        th.SetColor("_BaseColor", Hex("#a49c8e")); th.SetFloat("_BumpScale", 1.3f); EditorUtility.SetDirty(th);
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/ThirdParty/Megascans" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            var folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path));
            if (!byFolder.TryGetValue(folder, out var target)) continue;
            bool changed = false;
            foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || m == target) continue;
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), target); changed = true;
                }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (changed) imp.SaveAndReimport();
        }
    }

    static Bounds ModelBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void Solid(GameObject go, float shrink = 0.8f, float maxH = 3f)
    {
        return;   // 11-12 Kido: nothing inside the arena may block movement or projectiles -> no blockers
        if (!go) return;
        var b = ModelBounds(go);
        var c = new GameObject("Blocker"); c.transform.SetParent(go.transform.parent, false);
        c.transform.position = new Vector3(b.center.x, b.min.y + Mathf.Min(b.size.y, maxH) * 0.5f, b.center.z);
        var bc = c.AddComponent<BoxCollider>();
        bc.size = new Vector3(b.size.x * shrink, Mathf.Min(b.size.y, maxH), b.size.z * shrink);
    }

    // 11-11 (Fab assets in): Megascans grasses carry the clumps; the kit dry grass stays as a minority accent
    static string[] grassKinds, shrubKinds;
    static string[] Kinds(params string[] folders)
    {
        var l = new List<string>();
        foreach (var f in folders)
            foreach (var p in System.IO.Directory.GetFiles(MS + f, "*.fbx")) l.Add("MS:" + f + "/" + System.IO.Path.GetFileNameWithoutExtension(p));
        return l.ToArray();
    }
    // 11-13 (Kido: "the grass looks low quality"): the black spiky dead shrubs carried half the clumps; now the grass
    // clumps (Megascans Clumps, dry-tinted) join the wild/dry grasses and shrubs are a rare accent (see Clump)
    static string[] GrassKinds => grassKinds ??= Kinds("WildDry");   // 11-13 r8: no DryGrass / Clumps balls
    static string[] ShrubKinds => GrassKinds;   // 11-13 r8 (Kido): black spiky dead shrubs removed
    static string[] Concat(string[] a, string[] b) { var r = new string[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
    // Megascans grasses are real-size (0.3-0.6 m clumps) and vanish from the top-down camera; exaggerated ~2x like the refs
    static float KindScale(string m) => m.Contains("/AV_MS_shrubs") ? shrubScale : m.Contains("/AV_MS_Clumps") || m.Contains("/AV_MS_WildGreen") ? 1.5f : m.StartsWith("MS:") ? 1.1f : 1.35f;   // 11-12 Kido: grass was head-high

    // One natural clump: a dominant kind plus 1-3 others, sizes spread 2-3x, denser and taller in the middle.
    static int Clump(System.Func<float, float, float> G, Transform parent, Vector2 c, float radius, int count, float sizeMul)
    {
        string main = rng.NextDouble() < 0.9 ? GrassKinds[rng.Next(GrassKinds.Length)] : ShrubKinds[rng.Next(ShrubKinds.Length)];
        var others = new List<string>();
        int k = 1 + rng.Next(3);
        for (int i = 0; i < k; i++) others.Add(rng.NextDouble() < 0.9 ? GrassKinds[rng.Next(GrassKinds.Length)] : ShrubKinds[rng.Next(ShrubKinds.Length)]);
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            float a = R(0, Mathf.PI * 2), rr = radius * Mathf.Sqrt((float)rng.NextDouble());
            float x = c.x + Mathf.Cos(a) * rr * R(0.7f, 1.3f), z = c.y + Mathf.Sin(a) * rr;
            if (StreetDist(x, z) < 1.6f) continue;
            float centre = 1f - rr / Mathf.Max(radius, 0.01f);
            string m = rng.NextDouble() < 0.6 ? main : others[rng.Next(others.Count)];
            bool isShrub = m.StartsWith("MS:");
            float s = sizeMul * (0.65f + 0.85f * centre) * R(0.75f, 1.3f) * KindScale(m); s = Mathf.Min(s, 2.2f);
            var go = Place(m, new Vector3(x, G(x, z) - 0.04f, z), R(0, 360), s, parent);
            if (go) { go.transform.rotation *= Quaternion.Euler(R(-6, 6), 0, R(-6, 6)); n++; if (isShrub && shrubLog++ % 400 == 0) vegLog.Append($"{m} s={s:F2} size={ModelBounds(go).size} "); }
        }
        return n;
    }

    static List<Vector2> midSpots = new List<Vector2>();
    static int shrubLog; static readonly System.Text.StringBuilder vegLog = new System.Text.StringBuilder();
    const float shrubScale = 1.6f;   // Megascans dead shrubs are ~0.5 m at scale 1

    // 11-13 r8 (Kido: the round dry clumps - black spiky shrubs and bright yellow stalk balls at regular spacing - looked
    // coded): no more clumps. Single Megascans wild-grass tufts (Fab, free) are scattered one by one through two layers of
    // noise, so they gather into ragged patches that thin out into bare mud the way weeds grow; one kind dominates per
    // patch, sizes stay real (knee height at most), colour pulled down to the ground's olive brown.
    static string[] weedKinds, weedKindsB;
    // 11-14 (Kido, again: "the grass doesn't look like real grass"): lone tufts on bare dirt read as stamped weeds. Grass now
    // grows as turf: where this cover is up the terrain's sparse-grass layer is painted under it (SplatGround) and small
    // tufts stand close together, overlapping, so a patch reads as one mass of grass that thins out at its edge.
    public static float GrassCover(float x, float z)
    {
        float edge = Mathf.Max(Mathf.Abs(x) / 46f, Mathf.Abs(z + 3.5f) / 45f);
        float ring = Mathf.Clamp01((edge - 0.6f) * 2.5f);
        float n = 0.7f * Mathf.PerlinNoise(x * 0.05f + 50f, z * 0.05f + 20f) + 0.45f * Mathf.PerlinNoise(x * 0.19f + 9f, z * 0.19f + 3f);
        return Mathf.Clamp01((n - 0.5f + 0.22f * ring + 0.15f * IslandNear(x, z)) * 2.6f) * Mathf.Clamp01((StreetDist(x, z) - 1.5f) / 1.5f);
    }
    static int BuildVegetation(System.Func<float, float, float> G, Transform grass)
    {
        weedKinds ??= Kinds("WildDry"); weedKindsB ??= Kinds("WildDry");   // r10b: the few-blade WildGreen tufts read as black X stamps from the game camera
        int placed = 0;
        for (int i = 0; i < 320000 && placed < 16000; i++)
        {
            float x = R(-56f, 56f), z = R(-58f, 52f);
            if (StreetDist(x, z) < 2.0f) continue;
            if (Mathf.Abs(x) < 4.5f && z > 40.5f && z < 47.5f) continue;   // steps
            float edge = Mathf.Max(Mathf.Abs(x) / 46f, Mathf.Abs(z + 3.5f) / 45f);
            float ring = Mathf.Clamp01((edge - 0.6f) * 2.5f);
            float cover = GrassCover(x, z);
            if (cover < 0.12f || rng.NextDouble() > 0.17f * cover) continue;   // 11-14: dense turf inside the patches, nothing outside
            bool kindB = Mathf.PerlinNoise(x * 0.09f + 300f, z * 0.09f + 120f) > 0.55f;
            var arr = kindB ? weedKindsB : weedKinds;
            string m = arr[rng.Next(arr.Length)];
            float s = R(0.5f, 0.85f) * (0.7f + 0.4f * cover);   // 11-14: small, many, overlapping
            var go = Place(m, new Vector3(x, G(x, z) - 0.05f, z), R(0, 360), s, grass);
            if (go) { go.transform.rotation *= Quaternion.Euler(R(-8, 8), 0, R(-8, 8)); placed++; }
        }
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"), $"VEG weeds placed={placed}\n");
        return placed;
    }

    static void BuildRuins(System.Func<float, float, float> G)
    {
        var ruins = Group("Ruins");
        Vector3 At(float x, float z, float sink = 0.05f) => new Vector3(x, G(x, z) - sink, z);
        Vector2 Off(Island s, float fx, float fz)
        {
            var q = Quaternion.Euler(0, s.yaw, 0) * new Vector3(fx, 0, fz);
            return new Vector2(s.x + q.x, s.z + q.z);
        }
        var log = new System.Text.StringBuilder();
        foreach (var s in Islands)
        {
            var g = new GameObject($"Island_{s.kind}_{s.x:F0}_{s.z:F0}").transform; g.SetParent(ruins, false);
            switch (s.kind)
            {
                case Isl.Ruin:
                {
                    // 11-13 r8 (Kido: "이게 뭐야", the rubble patches read as stacked boxes): two half-buried moss rocks
                    var h = HalfRock(At(s.x, s.z, 0f), R(1.0f, 1.4f), g);
                    if (h) log.Append($"ruin rock {ModelBounds(h).size} ");
                    var p1 = Off(s, R(3.5f, 5f), R(-3, 3)); HalfRock(At(p1.x, p1.y, 0f), R(0.5f, 0.8f), g);
                    var p2 = Off(s, R(-5f, -3.5f), R(-2, 2)); Place("MS:Debris/AV_MS_Debris", At(p2.x, p2.y, 0.02f), R(0, 360), R(0.9f, 1.2f), g);
                    // 11-13 (Kido: don't heap the jars, they'd be broken): one jar knocked over, one smashed, apart
                    { var p = Off(s, R(-4, -2), R(-5.5f, -3.5f)); var jj = Place("MS:KJar/AV_MS_KJar", At(p.x, p.y, 0.04f), R(0, 360), R(0.7f, 1f), g); if (jj) jj.transform.rotation = Quaternion.Euler(R(78, 98), R(0, 360), 0); }
                    { var p = Off(s, R(2, 4), R(-5.5f, -3.5f)); Place("AV_OnggiBroken", At(p.x, p.y, 0.02f), R(0, 360), R(0.8f, 1f), g); }
                    var t = EdgeOut(s.x + R(-3f, 3f), s.z + R(-3f, 3f)); Place("PH:tree_stump_01", At(t.x, t.y, 0.05f), R(0, 360), R(0.9f, 1.2f), g);   // 11-12 Kido: no standing trees in the field
                    break;
                }
                case Isl.Jars:
                {
                    // 11-13 (Kido: "don't heap the jangdok in one place, they break easily"): was 9 whole jars in a heap;
                    // now three single pieces well apart (5 m+), two of them smashed, one knocked over
                    var jp = new List<Vector2>();
                    for (int tries = 0; tries < 60 && jp.Count < 3; tries++)
                    {
                        var p = Off(s, R(-7f, 7f), R(-7f, 7f));
                        bool ok = Vector2.Distance(p, new Vector2(s.x, s.z)) > 2.5f;
                        foreach (var q in jp) if (Vector2.Distance(p, q) < 5f) ok = false;
                        if (!ok) continue;
                        jp.Add(p);
                        if (jp.Count == 1) { var jj = Place("MS:KJar/AV_MS_KJar", At(p.x, p.y, 0.04f), R(0, 360), R(0.7f, 1f), g); if (jj) jj.transform.rotation = Quaternion.Euler(R(78, 98), R(0, 360), 0); }
                        else Place("AV_OnggiBroken", At(p.x, p.y, 0.02f), R(0, 360), R(0.8f, 1.1f), g);
                        for (int k = 0; k < 1; k++) Place("MS:Tile/AV_MS_Tile", At(p.x + R(-1.2f, 1.2f), p.y + R(-1.2f, 1.2f)), R(0, 360), R(0.8f, 1.1f), g);   // shards
                    }
                    var sh = Off(s, R(2.5f, 4f), R(-1, 1)); if (false) Place("MS:KStrawMat/AV_MS_KStrawMat", At(sh.x, sh.y, 0.12f), R(0, 360), R(0.5f, 0.7f), g);
                    break;
                }
                case Isl.Wall:
                {
                    // an L of dry-stone wall, one end tumbled into a heap, a dead tree in the corner
                    // 11-13 r8: the wall of rubble patches is gone; the remains of the wall are three sunk stones, uneven gaps
                    for (int i = 0; i < 3; i++) { var w = Off(s, i * R(2.6f, 4.2f) - 4f, R(-0.6f, 0.6f)); HalfRock(At(w.x, w.y, 0f), R(0.5f, 1.0f), g); }
                    var t = EdgeOut(s.x - 3f, s.z + 2f); Place("PH:tree_stump_01", At(t.x, t.y, 0.05f), R(0, 360), R(1f, 1.3f), g);
                    var r = EdgeOut(s.x + 4f, s.z - 1.6f, 3.5f); Place(rng.NextDouble() < 0.5 ? "PH:rock_moss_set_01" : "PH:rock_moss_set_02", At(r.x, r.y, 0.25f), R(0, 360), R(0.5f, 0.7f), g);
                    var pp = Off(s, 2f, 2.5f); Place("MS:Debris/AV_MS_Debris", At(pp.x, pp.y, 0.02f), R(0, 360), R(0.8f, 1.1f), g);
                    break;
                }
                case Isl.Tree:
                {
                    // a big old dead tree on a low knoll, roots and a fallen trunk, a jangseung or a stump beside it
                    var e = EdgeOut(s.x, s.z, 3f); var tr = Place("PH:dead_tree_trunk", At(e.x, e.y, 0.1f), R(0, 360), R(1.1f, 1.4f), g);   // fallen trunk on the knoll (no standing tree in the field)
                    Solid(tr, 0.7f, 1.2f);
                    log.Append($"tree bounds {ModelBounds(tr).size} ");
                    // 11-13 (Kido: "why are two stumps sitting together"): the second trunk and the stump beside it are gone
                    // (11-12 Kido: nothing tall inside the arena; no jangseung here)
                    var rk = EdgeOut(s.x + R(-3, 3), s.z + R(-3, 3), 4f); Place(rng.NextDouble() < 0.5 ? "PH:rock_moss_set_01" : "PH:rock_moss_set_02", At(rk.x, rk.y, 0.3f), R(0, 360), R(0.5f, 0.8f), g);
                    break;
                }
            }
        }
        // small things in the open middle, ~12-16 m apart, so the camera never shows bare mud only: a lone dead tree
        // (thin trunk blocker), a fallen trunk, a few smashed jars with tile shards, a plank heap. Nothing wider than ~2 m
        // blocks movement.
        var mid = new GameObject("Middle").transform; mid.SetParent(ruins, false);
        var spots = new List<Vector2>();
        for (int tries = 0; tries < 900 && spots.Count < 30; tries++)   // 11-13 (Kido: "too empty"): 16 spots 12 m apart -> 30, 8.5 m
        {
            var p = new Vector2(R(-36f, 36f), R(-38f, 30f));
            if (StreetDist(p.x, p.y) < 3f) continue;
            bool ok = true;
            foreach (var q in spots) if (Vector2.Distance(p, q) < 8.5f) { ok = false; break; }
            foreach (var s in Islands) if (Vector2.Distance(p, new Vector2(s.x, s.z)) < 11f) { ok = false; break; }
            if (ok) spots.Add(p);
        }
        int kk = 0;
        foreach (var p in spots)
        {
            switch (kk++ % 4)
            {
                // 11-13 QA: no fallen trunks or stumps in the middle any more; flat debris and sticks instead
                case 0:
                case 1: for (int i = 0; i < 3; i++) Place("MS:Debris/AV_MS_Debris", At(p.x + R(-1.5f, 1.5f), p.y + R(-1.5f, 1.5f), 0.02f), R(0, 360), R(0.7f, 1.1f), mid); break;
                case 2:
                    // 11-13: one smashed jar with its shards instead of a heap of four whole ones
                    Place("AV_OnggiBroken", At(p.x, p.y, 0.02f), R(0, 360), R(0.8f, 1.1f), mid);
                    for (int i = 0; i < 2; i++) Place("MS:Tile/AV_MS_Tile", At(p.x + R(-1.5f, 1.5f), p.y + R(-1.5f, 1.5f)), R(0, 360), R(0.8f, 1.1f), mid);
                    break;
                default:
                    Place("MS:Debris/AV_MS_Debris", At(p.x, p.y, 0.03f), R(0, 360), R(0.7f, 1f), mid);
                    // a flattened straw mat and loose straw beside the timber (11-13 fill pass)
                    if (false) Place("MS:KStrawMat/AV_MS_KStrawMat", At(p.x + R(-2f, 2f), p.y + R(-2f, 2f), -0.01f), R(0, 360), R(0.8f, 1f), mid);
                    for (int i = 0; i < 0; i++)   /* r10: loose straw read as black X stamps */ KeepLow(Place("MS:KStraw/AV_MS_KStraw", At(p.x + R(-2.5f, 2.5f), p.y + R(-2.5f, 2.5f), 0f), R(0, 360), 1f, mid), 0.15f);
                    break;
            }
        }
        midSpots = spots;

        // 11-13 (Kido: "a few pillars or trees are fine now, a little landmark, not too much"): six landmarks spread over
        // the arena on every fifth middle spot: two dead trees, a jangseung, a sotdae, a stone lantern, a lantern post.
        // Each gets a thin trunk collider so the player walks round it; no shadows from the trees (perf).
        // 11-13 r8: Kido first asked for big trees for depth, then (Hades 2 ref) wanted them only as dark silhouettes on the
        // camera-side outer edge, never inside the fight space (see BuildSouthVolume). The arena keeps the small marks.
        string[] bigTrees = { };
        string[] marks = { "AV_Jangseung", "AV_StoneLantern", "AV_Sotdae", "AV_LanternPost" };
        int mk = 0, bt = 0;
        for (int i = 0; i < spots.Count; i++)
        {
            var p = spots[i] + new Vector2(R(-2.5f, 2.5f), R(-2.5f, 2.5f));
            if (i % 5 == 2) p = EdgeOut(p.x, p.y, -1.2f);   // r11 (reviewer + Kido: tall things only at the edge): the landmarks stand just inside the rim
            if (i % 3 == 0 && bt < bigTrees.Length && p.magnitude > 7f)
            {
                var t = PlaceTree(bigTrees[bt++], At(p.x, p.y, 0.1f), R(0, 360), R(1.1f, 1.45f), mid);
                if (!t) continue;
                var tb = ModelBounds(t);
                var tc = t.AddComponent<CapsuleCollider>(); tc.radius = 0.45f / t.transform.localScale.x; tc.height = 4f / t.transform.localScale.x; tc.center = new Vector3(0, tc.height * 0.5f, 0);
                log.Append($"bigtree {t.name} h={tb.size.y:F1} at {p.x:F0},{p.y:F0} ");
            }
            else if (i % 5 == 2 && mk < marks.Length)
            {
                var m = marks[mk++];
                var go = Place(m, At(p.x, p.y, 0.05f), R(0, 360), 1f, mid);
                if (!go) continue;
                var col = go.AddComponent<CapsuleCollider>(); col.radius = 0.35f; col.height = 3f; col.center = new Vector3(0, 1.5f, 0);
                log.Append($"landmark {m} at {p.x:F0},{p.y:F0} ");
            }
        }

        // Megascans litter (Fab): sticks, weathered timber, roof tiles and clay pots strewn around every island and every
        // small thing in the middle, plus a thin random scatter over the yard. Low, no colliders.
        var lit = new GameObject("Litter").transform; lit.SetParent(ruins, false);
        var centres = new List<(Vector2 p, float r, int n)>();
        foreach (var s in Islands) centres.Add((new Vector2(s.x, s.z), 7f, 22));
        foreach (var p in spots) centres.Add((p, 3.5f, 7));
        for (int i = 0; i < 110; i++) centres.Add((new Vector2(R(-44f, 44f), R(-46f, 39f)), 2f, 3));   // r12: denser floor story (approver: empty stretches)   // 11-13 r5: was 70, the shards and pots read as gravel
        string[] litter = { "MS:Debris/AV_MS_Debris", "MS:Tile/AV_MS_Tile", "MS:Debris/AV_MS_Debris", "MS:Pot/AV_MS_Pot" };   // r10: the stick pack read as repeated black X stamps
        int litN = 0;
        foreach (var (p, r, n) in centres)
            for (int i = 0; i < n; i++)
            {
                float a = R(0, Mathf.PI * 2), d = r * Mathf.Sqrt((float)rng.NextDouble());
                float x = p.x + Mathf.Cos(a) * d, z = p.y + Mathf.Sin(a) * d;
                if (StreetDist(x, z) < 1.2f) continue;
                var m = litter[rng.Next(litter.Length)];
                var go = Place(m, At(x, z, m.Contains("Pot") ? 0.08f : 0.02f), R(0, 360), m.Contains("Tile") ? R(0.8f, 1.2f) : m.Contains("Pot") ? R(0.7f, 1.1f) : R(0.6f, 1.3f) * (m.Contains("Sticks") ? 4f : 1f), lit);
                if (!go) continue;
                if (m.Contains("Pot") && rng.NextDouble() < 0.5) go.transform.rotation = Quaternion.Euler(R(70, 100), R(0, 360), 0);   // knocked over
                else go.transform.rotation *= Quaternion.Euler(R(-8, 8), 0, R(-8, 8));
                if (litN++ < 4) log.Append($"{m} size={ModelBounds(go).size} ");
            }
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"),
            $"REDO {log}\n");
    }
}
