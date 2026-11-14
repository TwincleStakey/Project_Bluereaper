// 11-12 (Kido: place the props properly, do the blood properly, the scene must not be boring; nothing inside the
// arena may block movement or projectiles): small composed scenes instead of random scatter. ~22 spots over the arena,
// 11-16 m apart, each one of four stories, all knee height or lower, no colliders:
//   spill    - a household dragged out and dropped: straw mat, jars (some knocked over), bucket, basket, broom, tiles
//   carnage  - a wide dark blood pool, a drag trail leading off, splashes, a dropped basket or bucket, scattered sticks
//   fire     - a dead campfire ring with charred planks, stools around it, a pot, blood nearby
//   offering - straw jar lids and broken onggi round a small straw heap, leaves, a few candles of blood drops
using System.Collections.Generic;
using UnityEngine;

public static partial class AVBuilder
{
    static void BuildVignettes(System.Func<float, float, float> G, Material[] bloods, Material leaves, Transform parent)
    {
        var root = new GameObject("Vignettes").transform; root.SetParent(parent, false);
        Vector3 At(float x, float z, float sink = 0.02f) => new Vector3(x, G(x, z) - sink, z);
        void Blood(int q, float x, float z, float size, float lift = 0.03f) => Decal(root, bloods[q], x, z, size, G, lift);

        var spots = new List<Vector2>();
        for (int t = 0; t < 900 && spots.Count < 34; t++)
        {
            var p = new Vector2(R(-40f, 40f), R(-43f, 36f));
            if (StreetDist(p.x, p.y) < 2.5f) continue;
            bool ok = true;
            foreach (var q in spots) if (Vector2.Distance(p, q) < 9.5f) { ok = false; break; }
            if (ok) spots.Add(p);
        }
        int n = 0;
        foreach (var c in spots)
        {
            float yaw = R(0, 360);
            Vector2 Off(float fx, float fz) { var v = Quaternion.Euler(0, yaw, 0) * new Vector3(fx, 0, fz); return c + new Vector2(v.x, v.z); }
            int kind = n++ % 8;
            // refs: small warm candle / lantern pools anchor the story clusters (3-6 per screen), the rest stays near black
            // r12 (approver: central screens had no warm pocket): every campfire still smoulders - a low ember glow with
            // sparks rising, the only warm light in the open yard (r9: no sourceless light pools)
            if (kind == 2 && rng.NextDouble() < 0.5) Smoulder(new Vector3(c.x, G(c.x, c.y), c.y), root);   // r13: half still smoulder, half are cold ash
            if (kind >= 4) { HorrorVignette(kind, kind == 7 ? EdgeOut(c.x, c.y, 3f) : c, yaw, G, bloods, leaves, root); continue; }   // 11-13 QA: bone piles out at the edge
            switch (kind)
            {
                case 0:   // spill
                {
                    if (false) Place("MS:KStrawMat/AV_MS_KStrawMat", At(c.x, c.y, -0.01f), yaw + R(-20, 20), R(0.9f, 1.1f), root);
                    // 11-13 (Kido: no jar heaps): one jar knocked over and one smashed, a few metres apart
                    { var p = Off(R(-2.5f, -1.5f), R(1f, 2.5f)); var j = Place("MS:KJar/AV_MS_KJar", At(p.x, p.y, 0.04f), R(0, 360), R(0.75f, 1.05f), root); if (j) j.transform.rotation = Quaternion.Euler(R(78, 98), R(0, 360), 0); }
                    { var p = Off(R(1.5f, 3f), R(1.5f, 3f)); Place("AV_OnggiBroken", At(p.x, p.y, 0.02f), R(0, 360), R(0.8f, 1f), root); }
                    var b = Off(R(-2.5f, -1.2f), R(-1.5f, 0f)); Place(rng.NextDouble() < 0.5 ? "PH:wooden_bucket_01" : "PH:wooden_bucket_02", At(b.x, b.y), R(0, 360), 1f, root).transform.rotation = Quaternion.Euler(R(80, 95), R(0, 360), 0);
                    var k = Off(R(1.5f, 2.8f), R(-1.8f, -0.4f)); Place("PH:wicker_basket_01", At(k.x, k.y), R(0, 360), 1f, root);
                    var br = Off(R(-0.5f, 0.5f), R(-2.4f, -1.6f)); Place("PH:wooden_broom", At(br.x, br.y, -0.03f), R(0, 360), 1f, root).transform.rotation = Quaternion.Euler(R(84, 92), R(0, 360), 0);
                    for (int i = 0; i < 2; i++) { var p = Off(R(-3, 3), R(-3, 3)); Place("MS:Tile/AV_MS_Tile", At(p.x, p.y), R(0, 360), R(0.9f, 1.3f), root); }
                    var bl = Off(R(2f, 3.5f), R(1f, 3f)); Blood(rng.Next(2, 4), bl.x, bl.y, R(1.6f, 2.4f));
                    break;
                }
                case 1:   // carnage
                {
                    // 11-13 r8: the big gory pool, a second one beside it, a long drag trail (one small handprint at its end)
                    BloodPool(root, bloods, c.x, c.y, R(2.8f, 3.8f), G, false);
                    BloodPool(root, bloods, c.x + R(-1.5f, 1.5f), c.y + R(-1.5f, 1.5f), R(1.4f, 2f), G, false);
                    var dir = new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), Mathf.Sin(yaw * Mathf.Deg2Rad));
                    DragTrail(root, bloods, c + dir * 1.3f, dir, R(3f, 4.5f), G, rng.NextDouble() < 0.6);
                    for (int i = 0; i < rng.Next(3, 6); i++) { var p = c + new Vector2(R(-3.5f, 3.5f), R(-3.5f, 3.5f)); Blood(1, p.x, p.y, R(0.8f, 1.6f), 0.033f); }
                    var d = Off(R(-2.5f, 2.5f), R(1.5f, 3f));
                    Place(rng.NextDouble() < 0.5 ? "PH:wicker_basket_01" : "PH:wooden_bucket_02", At(d.x, d.y), R(0, 360), 1f, root).transform.rotation = Quaternion.Euler(R(75, 100), R(0, 360), 0);
                    for (int i = 0; i < 3; i++) { var p = Off(R(-3, 3), R(-3, 3)); Place("MS:Debris/AV_MS_Debris", At(p.x, p.y), R(0, 360), R(0.7f, 1f), root); }
                    break;
                }
                case 2:   // dead campfire
                {
                    // r16 (approver: round fire pits read as coins): a few loose blackened stones and charred sticks, no ring
                    for (int i = rng.Next(2, 4); i > 0; i--) { var p = Off(R(-0.9f, 0.9f), R(-0.9f, 0.9f)); HalfRock(At(p.x, p.y, 0f), R(0.3f, 0.5f), root); }
                    for (int i = rng.Next(2, 4); i > 0; i--) { var p = Off(R(-0.5f, 0.5f), R(-0.5f, 0.5f)); var pl = PlacePart("MS:Planks/AV_MS_Planks", At(p.x, p.y, 0f), R(0, 360), R(0.5f, 0.9f), root); KeepLow(pl, 0.1f); if (pl) foreach (var r in pl.GetComponentsInChildren<Renderer>()) { var m = r.sharedMaterials; for (int k = 0; k < m.Length; k++) m[k] = CharMat(m[k]); r.sharedMaterials = m; } }
                    for (int i = 0; i < 4; i++) { var p = Off(R(-0.6f, 0.6f), R(-0.6f, 0.6f)); Place("MS:Debris/AV_MS_Debris", At(p.x, p.y, 0f), R(0, 360), R(0.6f, 0.9f), root); }
                    for (int i = 0; i < 2; i++) { var p = Off(i == 0 ? R(1.6f, 2f) : R(-2f, -1.6f), R(-0.6f, 0.6f)); var s = Place("PH:wooden_stool_01", At(p.x, p.y), R(0, 360), 1f, root); if (s && rng.NextDouble() < 0.5) s.transform.rotation = Quaternion.Euler(R(80, 95), R(0, 360), 0); }
                    var pot = Off(R(-1f, 1f), R(1.6f, 2.2f)); Place("MS:Pot/AV_MS_Pot", At(pot.x, pot.y, 0.04f), R(0, 360), 1f, root);
                    var bl = Off(R(2.5f, 4f), R(-1.5f, 1.5f)); Blood(rng.Next(2, 4), bl.x, bl.y, R(1.8f, 2.8f));
                    Decal(root, leaves, c.x + R(-2, 2), c.y + R(-2, 2), R(2.5f, 3.5f), G, 0.015f);
                    break;
                }
                default:  // offering
                {
                    if (false) Place("MS:KStrawMat/AV_MS_KStrawMat", At(c.x, c.y, -0.01f), yaw, R(0.9f, 1.1f), root);
                    for (int i = 0; i < 1; i++) { var p = Off(R(-1.8f, 1.8f), R(-1.8f, 1.8f)); Place("MS:KStrawJar/AV_MS_KStrawJar", At(p.x, p.y), R(0, 360), R(0.9f, 1.2f), root); }
                    { var p = Off(R(-2.6f, 2.6f), R(-2.6f, 2.6f)); Place("AV_OnggiBroken", At(p.x, p.y, 0.02f), R(0, 360), R(0.7f, 0.9f), root); }   // 11-13: one smashed jar, not four whole
                    Decal(root, leaves, c.x, c.y, R(3f, 4f), G, 0.015f);
                    for (int i = 0; i < 3; i++) { var p = Off(R(-2.5f, 2.5f), R(-2.5f, 2.5f)); Blood(1, p.x, p.y, R(0.9f, 1.5f), 0.033f); }
                    break;
                }
            }
        }
        foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"),
            $"VIGNETTES {spots.Count} " + string.Join(" ", spots.ConvertAll(s => $"{s.x:F0},{s.y:F0}")) + "\n");
    }
}
