// 11-12 night pass (Kido: Korean / East-Asian horror refs, inner arena too plain; everything downloaded, nothing to trip
// on). Refs: Downloads/scene-refs/abandoned-village/horror (Tarae 01/02/04 = bodies under straw mats in big blood,
// dropped tools; 제사상/굿 photos = overturned offering tables). Four more ground-level stories, all scans from Sketchfab
// KOREA HERITAGE SERVICE (CC BY) and other CC BY authors (see CREDITS.md), clamped under ~0.5 m, no colliders:
//   4 covered body  - a straw mat (거적) over a body-sized lump, a wide blood pool spreading from under it, hand smears
//                     and footprints leading off, a dropped sickle
//   5 offering      - an overturned 소반 with earthen bowls, wooden bowl, spoon and chopsticks, a gourd, a cauldron lid,
//                     straw strewn round it, a few blood drops
//   6 dropped tools - hoe, fire shovel, broom, winnow, sieve, basket and straw bags left where people fled
//   7 bones         - animal skulls and broken planks on a fallen roof-end tile, blood splatter
using UnityEngine;

public static partial class AVBuilder
{
    // keep a placed prop low: lay it on its side if it is taller than wide, then shrink it under maxH
    // real-world longest dimension (m) per downloaded folder: the KHS FBXs import at 1/100, other scans at odd scales
    static readonly System.Collections.Generic.Dictionary<string, float> MSSize = new System.Collections.Generic.Dictionary<string, float>
    {
        { "KBowl", 0.3f }, { "KBasin", 0.55f }, { "KLid", 0.35f }, { "KTileBird", 0.3f }, { "KTileFace", 0.3f }, { "KSickle", 0.45f },
        { "KHoe", 0.5f }, { "KShovel", 0.65f }, { "KStraw", 0.9f }, { "KStrawBag", 0.9f }, { "KBroom", 1.2f }, { "KWinnow", 0.75f },
        { "KSieve", 0.5f }, { "KBasket", 0.6f }, { "KSoban", 0.6f }, { "KWoodBowl", 0.6f }, { "KSpoon", 0.25f }, { "KGourd", 0.35f },
        { "Planks", 1.1f }, { "Skulls", 1.2f }, { "Sack", 0.9f }, { "KJar", 0.6f }, { "KStrawJar", 0.5f }, { "KStrawMat", 2.6f },
        { "RubblePatch", 2.6f },
    };
    public static void NormalizeMS(GameObject go, string model)
    {
        if (!go || !model.StartsWith("MS:")) return;
        var folder = model.Substring(3, model.IndexOf('/') - 3);
        if (!MSSize.TryGetValue(folder, out var target)) return;
        var b = ModelBounds(go); float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (m > 1e-5f) go.transform.localScale *= target / m * (folder.StartsWith("K") && folder != "KStrawMat" ? 2.5f : 1.2f);   // household scans read as dots from the game camera: x1.8
        // 11-13 QA: the rolled straw mat read as a log lying in the arena; pressed flat to a mat ~12 cm high
        if (folder == "KStrawMat") { var bb = ModelBounds(go); if (bb.size.y > 0.12f) { var s = go.transform.localScale; go.transform.localScale = new Vector3(s.x, s.y * 0.12f / bb.size.y, s.z); } }
    }
    static readonly System.Collections.Generic.HashSet<string> loggedK = new System.Collections.Generic.HashSet<string>();
    static void KeepLow(GameObject go, float maxH = 0.5f)
    {
        if (!go) return;
        var b = ModelBounds(go);
        if (b.size.y > Mathf.Max(b.size.x, b.size.z) * 1.1f) { go.transform.rotation = Quaternion.Euler(90f, R(0, 360), 0) * Quaternion.identity; b = ModelBounds(go); }
        if (b.size.y > maxH) go.transform.localScale *= maxH / b.size.y;
        b = ModelBounds(go);
        go.transform.position += Vector3.up * (go.transform.position.y - b.min.y) * 0.85f;   // rest on the ground
    }

    // world height from the meshes themselves (Renderer.bounds lags a transform change inside one editor call)
    static float MeshHeight(GameObject go)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var b = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float y = m.MultiplyPoint3x4(c).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
        }
        return hi > lo ? hi - lo : 0f;
    }

    static void HorrorVignette(int kind, Vector2 c, float yaw, System.Func<float, float, float> G, Material[] bloods, Material leaves, Transform root)
    {
        Vector3 At(float x, float z, float sink = 0.02f) => new Vector3(x, G(x, z) - sink, z);
        Vector2 Off(float fx, float fz) { var v = Quaternion.Euler(0, yaw, 0) * new Vector3(fx, 0, fz); return c + new Vector2(v.x, v.y == 0 ? v.z : v.z); }
        void Blood(int q, float x, float z, float size, float lift = 0.03f) => Decal(root, bloods[q], x, z, size, G, lift);
        GameObject P(string folder, Vector2 p, float s = 1f, float maxH = 0.45f, bool tip = false)
        {
            var go = Place($"MS:{folder}/AV_MS_{folder}", At(p.x, p.y), R(0, 360), s, root);
            if (go && tip) go.transform.rotation = Quaternion.Euler(R(70, 110), R(0, 360), R(-10, 10));
            if (go && (folder == "KWinnow" || folder == "KSieve" || folder == "KBasket" || folder == "KSoban" || folder == "KLid" || folder == "KTileBird" || folder == "KTileFace"))
            {
                // 11-13 r8: flat things stood on edge as black half-discs; lay them on their thinnest side
                var best = go.transform.rotation; float bh = MeshHeight(go);
                foreach (var e in new[] { new Vector3(90, 0, 0), new Vector3(0, 0, 90), new Vector3(-90, 0, 0) })
                {
                    go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0) * Quaternion.Euler(e);
                    float h = MeshHeight(go); if (h < bh) { bh = h; best = go.transform.rotation; }
                }
                go.transform.rotation = best * Quaternion.Euler(R(-6, 6), 0, R(-6, 6));
            }
            if (go && loggedK.Add(folder)) System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"), $"KPROP {folder} raw {ModelBounds(go).size}\n");
            KeepLow(go, maxH);
            return go;
        }
        switch (kind)
        {
            case 4:   // covered body
            {
                var lump = Off(0, 0);
                P("Sack", lump, 1.4f, 0.32f);                                                                     // the body under the mat
                var mat = Place("MS:KStrawMat/AV_MS_KStrawMat", At(lump.x, lump.y, -0.12f), yaw + 90f + R(-10, 10), R(0.55f, 0.7f), root);
                if (mat) mat.transform.rotation *= Quaternion.Euler(R(-4, 4), 0, R(-4, 4));
                // 11-13 r8: one big gory pool from under the mat and a drag trail off it ending in a single small handprint
                BloodPool(root, bloods, c.x + R(-0.5f, 0.5f), c.y + R(-0.5f, 0.5f), R(2.6f, 3.4f), G, false);
                { var d = Off(1f, R(-0.4f, 0.4f)) - c; DragTrail(root, bloods, c + d.normalized * 1.2f, d, R(3f, 4.5f), G, true); }
                var s = Off(R(-2.5f, -1.5f), R(1f, 2f)); P("KSickle", s, 1f, 0.15f);
                break;
            }
            case 5:   // overturned offering table
            {
                P("KSoban", c, 1f, 0.45f, true);
                for (int i = 0; i < 3; i++) { var p = Off(R(-1.6f, 1.6f), R(0.6f, 2f)); P("KBowl", p, 1f, 0.2f, rng.NextDouble() < 0.5); }
                var wb = Off(R(1.2f, 2f), R(-1.2f, 0f)); P("KWoodBowl", wb, 1f, 0.25f, true);
                var sp = Off(R(-1f, 1f), R(-1.6f, -0.8f)); P("KSpoon", sp, 1f, 0.06f);
                var gd = Off(R(-2.4f, -1.4f), R(-0.5f, 0.8f)); P("KGourd", gd, 1f, 0.25f, true);
                var ld = Off(R(1.6f, 2.4f), R(1f, 2f)); P("KLid", ld, 1f, 0.15f);
                for (int i = 0; i < 0; i++)   /* r10: loose straw read as black X stamps */ { var p = Off(R(-2.5f, 2.5f), R(-2.5f, 2.5f)); P("KStraw", p, 1f, 0.15f); }
                for (int i = 0; i < 3; i++) { var p = Off(R(-2.5f, 2.5f), R(-2.5f, 2.5f)); Blood(1, p.x, p.y, R(0.8f, 1.4f), 0.033f); }
                Decal(root, leaves, c.x + R(-1, 1), c.y + R(-1, 1), R(2.5f, 3.5f), G, 0.015f);
                break;
            }
            case 6:   // dropped tools and baskets
            {
                string[] tools = { "KHoe", "KShovel", "KBroom", "KWinnow", "KSieve", "KBasket", "KStrawBag" };
                for (int i = 0; i < 5; i++)
                {
                    var p = Off(R(-3f, 3f), R(-3f, 3f));
                    var t = tools[(i * 3 + rng.Next(2)) % tools.Length];   // r11 QA: never the same tool twice in one spot
                    P(t, p, 1f, t == "KStrawBag" || t == "KBasket" ? 0.4f : 0.15f, false);   // 11-13 r8: tipped-up baskets/sieves stood as black half-discs
                }
                var bl = Off(R(-1, 1), R(-1, 1)); Blood(3, bl.x, bl.y, R(1.8f, 2.6f));
                break;
            }
            default:  // bones on a fallen roof-end tile, broken planks
            {
                P(rng.NextDouble() < 0.5 ? "KTileBird" : "KTileFace", c, 1f, 0.2f);
                for (int i = 0; i < 2; i++) { var p = Off(R(-1.5f, 1.5f), R(-1.5f, 1.5f)); P("Skulls", p, 1f, 0.3f); }
                for (int i = 0; i < 3; i++) { var p = Off(R(-2.5f, 2.5f), R(-2.5f, 2.5f)); P("Planks", p, 1f, 0.12f); }
                Blood(3, c.x + R(-1, 1), c.y + R(-1, 1), R(2f, 3f));
                for (int i = 0; i < 2; i++) { var p = Off(R(-2.5f, 2.5f), R(-2.5f, 2.5f)); Place("MS:Tile/AV_MS_Tile", At(p.x, p.y), R(0, 360), R(0.9f, 1.3f), root); }
                break;
            }
        }
    }
}
