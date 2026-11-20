// 11-19 풀밭 (F1) dressing (Kido: "여기도 씬이 너무 심심한데 좀 정한 규칙내에 좀 더해봐"). Rules: 환경규칙.md
// (C:/Users/PC/Downloads/env-rules-refs) - centre clearing left empty, vignettes of L1 + M2-3 + S5-10 along the edges with
// 8-15 m gaps, bases buried with a skirt of small stones, 5-7 tall verticals irregularly on a ring at 65-85% of the arena
// half-size (none on the camera / south side), one hook. Grass start set (rules 7): rock clusters + shrubs at the boundary.
// Only downloaded assets already in the project: Resources/GrassDress (valley round rock set, Poly Haven shrubs / stumps,
// see GrassDressBake) and EdgeFeatures dead trees / snags (Poly Haven, Sketchfab CC BY, credits as before).
// No colliders, no lights, nothing moved or flattened; the existing EdgeFeatures trees and hedge stay as they are.
// Off: PlayerPrefs RR.EnvRecompose.grass = 0 (next room entry).
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    public sealed partial class EnvRecompose
    {
        const string GD = "GrassDress/";
        static readonly string[] GBig = { GD + "GD_Boulder1", GD + "GD_Boulder3" };
        static readonly string[] GMed = { GD + "GD_Rock01", GD + "GD_Rock02", GD + "GD_Rock03", GD + "GD_Rock04", GD + "GD_Rock05", GD + "GD_Rock06" };
        static readonly string[] GSmall = { GD + "GD_Small07", GD + "GD_Small08", GD + "GD_Small09", GD + "GD_Small10", GD + "GD_Small11", GD + "GD_Small12", GD + "GD_Small13" };
        static readonly string[] GShrub = { GD + "GD_Shrub02" };                       // the round one (2 m model); 01 / 03 are flat ground plants
        static readonly string[] GCover = { GD + "GD_Shrub01", GD + "GD_Shrub03" };    // used at their own 0.3-0.4 m height
        static readonly string[] GStump = { GD + "GD_Stump01", GD + "GD_Stump02" };

        static string Pick(string[] a) => a[rng.Next(a.Length)];

        // a piece scaled to height h, `sink` of it under the floor, shadows only when it is tall enough to read
        static GameObject FitH(string path, float x, float z, float h, float sink, float tilt = 0f)
        {
            var g = Prop(path, x, z, R(0f, 360f), R(0.9f, 1.1f), 0f, tilt, 0f);
            if (!g) return null;
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return g;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            if (b.size.y > 1e-3f) g.transform.localScale *= (h / (1f - sink)) / b.size.y;
            b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            g.transform.position += Vector3.up * (GroundAt(x, z) - b.min.y - sink * b.size.y);
            foreach (var r in rs) { r.shadowCastingMode = h >= 0.6f ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true; }
            return g;
        }

        // small stones (+ a shrub) around a base, drifting toward `towards` (the clearing side)
        static void GSkirt(float x, float z, float rad, int n, Vector2 towards, bool shrub)
        {
            for (int i = 0; i < n; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = rad * R(0.6f, 1.3f);
                float px = x + Mathf.Cos(a) * d + towards.x * R(0f, 1.6f), pz = z + Mathf.Sin(a) * d + towards.y * R(0f, 1.6f);
                FitH(Pick(GSmall), px, pz, R(0.12f, 0.32f), 0.25f, R(-15f, 15f));
            }
            if (shrub) { float a = R(0f, Mathf.PI * 2f); FitH(Pick(GShrub), x + Mathf.Cos(a) * rad * 0.9f, z + Mathf.Sin(a) * rad * 0.9f, R(0.7f, 1.2f), 0.05f); }
        }

        // a vignette: one hero (L boulder 1.8-2.6 m, or a stump / M rock when low), 2-3 M around it overlapping, 5-10 S
        // flowing toward the clearing. low = camera-side (south) version, nothing over ~1.1 m
        static void Vignette(float x, float z, bool low)
        {
            var inward = new Vector2(-x, -z).normalized;
            var side = new Vector2(-inward.y, inward.x);
            float heroR;
            if (!low) { FitH(Pick(GBig), x, z, R(1.8f, 2.6f), 0.2f, R(-6f, 6f)); heroR = 1.3f; }
            else if (Chance(0.5)) { FitH(Pick(GStump), x, z, R(1.3f, 1.7f), 0.1f); heroR = 1.0f; }   // 11-19 later: waist-chest of the 2.85 m player
            else { FitH(Pick(GMed), x, z, R(0.8f, 1.1f), 0.18f, R(-8f, 8f)); heroR = 0.8f; }
            int nm = rng.Next(2, 4);
            for (int i = 0; i < nm; i++)
            {
                float s = (i % 2 == 0 ? 1f : -1f) * R(0.6f, 1.1f);
                var p = new Vector2(x, z) + side * s * heroR * 1.4f + inward * R(-0.3f, 0.9f) * heroR;
                if (i == nm - 1) FitH(Pick(GShrub), p.x, p.y, R(0.8f, low ? 1.0f : 1.4f), 0.05f);
                else FitH(Pick(GMed), p.x, p.y, low ? R(0.4f, 0.7f) : R(0.6f, 1.15f), 0.18f, R(-10f, 10f));
            }
            int ns = rng.Next(5, 11);
            for (int i = 0; i < ns; i++)
            {
                var p = new Vector2(x, z) + inward * R(heroR * 0.8f, heroR * 3.2f) + side * R(-1.8f, 1.8f);
                FitH(Pick(GSmall), p.x, p.y, R(0.1f, 0.3f), 0.25f, R(-15f, 15f));
            }
        }

        static void MidGroup(float x, float z)
        {
            var inward = new Vector2(-x, -z).normalized; var side = new Vector2(-inward.y, inward.x);
            FitH(Pick(GMed), x, z, R(0.6f, 0.9f), 0.22f, R(-8f, 8f));
            var p = new Vector2(x, z) + side * R(0.8f, 1.4f) - inward * R(0.2f, 0.8f);
            FitH(Pick(GMed), p.x, p.y, R(0.3f, 0.5f), 0.2f, R(-12f, 12f));
            if (Chance(0.6)) { p = new Vector2(x, z) - side * R(1f, 1.8f) - inward * R(0.3f, 1f); FitH(Pick(GShrub), p.x, p.y, R(0.6f, 0.85f), 0.05f); }
            for (int i = 0, n = rng.Next(2, 4); i < n; i++) { p = new Vector2(x, z) + side * R(-2.5f, 2.5f) - inward * R(-0.5f, 2.2f); FitH(Pick(GCover), p.x, p.y, R(0.25f, 0.4f), 0.05f); }
            for (int i = 0, n = rng.Next(4, 8); i < n; i++) { p = new Vector2(x, z) + inward * R(0.6f, 3f) + side * R(-1.6f, 1.6f); FitH(Pick(GSmall), p.x, p.y, R(0.08f, 0.25f), 0.25f, R(-15f, 15f)); }
        }

        // a tall vertical on the ring with its skirt
        static void RingTree(string path, float x, float z, float h)
        {
            FitH(path, x, z, h, 0.06f, R(-3f, 3f));
            GSkirt(x, z, 0.9f, rng.Next(3, 6), new Vector2(-x, -z).normalized, Chance(0.6));
        }

        // the field's own EdgeFeatures trees (built before this layer, sized for the old 1.9 m player): scaled about their
        // foot to at least `min` m (scale only, position kept, foot stays at the same height). Jangseung etc. untouched.
        static bool IsTree(string n) => n.Contains("Conifer") || n.Contains("Fir") || n.Contains("Pine") || n.Contains("DeadTree") || n.Contains("BareTree") || n.Contains("Snag") || n.Contains("DeadTrunk");
        static int ScaleEdgeTrees(string room, float min, float minDark)
        {
            var holder = GameObject.Find("EdgeFeatures_" + room); if (!holder) return 0;
            int n = 0;
            foreach (Transform t in holder.transform)
            {
                if (!IsTree(t.name)) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                bool dark = t.position.z < off.z - 30f;   // the camera-side (south) silhouettes
                float target = dark ? minDark : min, h = b.size.y;
                if (h >= target || h < 0.1f) { Debug.Log($"[EnvRecompose]   {room} tree {t.name} {h:0.0} m kept"); continue; }
                float k = target / h, footY = b.min.y;
                t.localScale *= k;
                b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                t.position += Vector3.up * (footY - b.min.y);
                Debug.Log($"[EnvRecompose]   {room} tree {t.name} {h:0.0} -> {b.size.y:0.0} m (x{k:0.00}){(dark ? " dark" : "")}");
                n++;
            }
            return n;
        }
        // measuring only (other fields): tree heights vs the 2.85 m player
        public static void MeasureEdgeTrees(string room)
        {
            var holder = GameObject.Find("EdgeFeatures_" + room); if (!holder) return;
            var sb = new System.Text.StringBuilder();
            foreach (Transform t in holder.transform)
            {
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                sb.Append($"{t.name} {b.size.y:0.0}m({b.size.y / 2.85f:0.0}x) ");
            }
            Debug.Log($"[EnvRecompose] measure {room}: " + sb);
        }

        static int Grass()
        {
            MeasureEdgeTrees("grass");
            int scaled = ScaleEdgeTrees("grass", 17f, 13f);
            Debug.Log($"[EnvRecompose]   grass edge trees scaled: {scaled}");
            if (!Load<GameObject>(GBig[0])) { Debug.LogWarning("[EnvRecompose] grass: Resources/GrassDress missing (run RoomRun.GrassDressBake.Run)"); return 0; }
            // 1. boundary vignettes, 8-15 m apart, just inside the edge (arena 112 x 88: x +-56, z +-44); north door |x| < 8 kept clear
            Vignette(-46f, 35f, false); Vignette(-34f, 36.5f, false); Vignette(19f, 36f, false); Vignette(33f, 37.5f, false);
            Vignette(48.5f, 15f, false); Vignette(47.5f, -11f, false);
            Vignette(-48.5f, 7f, false); Vignette(-47f, -19f, false);
            // camera side: low groups only (the dark foreground silhouettes are EdgeFeatures')
            Vignette(-30f, -36.5f, true); Vignette(5f, -38f, true); Vignette(33f, -35.5f, true);

            // 2. ring verticals at 65-85% of the half-size, irregular, north / sides only (bare trees, snags)
            // 11-19 later (Kido: "사람이 저만한대 나무가 저렇게 작은게 말이돼"): the player is 2.85 m since the x1.5 (a 1.8 m man):
            // dead trees 5-8 men = 15-22 m, snags (broken trunks) 4-6 m; pushed ~12% further out on the ring (less screen cover)
            const float O = 1.12f;
            RingTree("EdgeFeatures/VH_BareTreeA", -34f * O, 22f * O, 19f);
            RingTree("EdgeFeatures/EF_RF_Snag_B", -15.6f * O, 26.3f * O, 5.5f);
            RingTree("EdgeFeatures/VH_BareTreeB", 15.3f, 33.1f * 1.06f, 17f);
            RingTree("EdgeFeatures/EF_RF_DeadTreeB", 39f * 1.08f, 24.5f * 1.08f, 16f);
            RingTree("EdgeFeatures/EF_RF_Snag_D", 37.9f * O, -2.6f, 4.5f);
            RingTree("EdgeFeatures/EF_RF_DeadTreeA", -45.8f * 1.06f, -6.3f, 17f);
            RingTree("EdgeFeatures/EF_RF_Snag_C", 32.2f * O, -21.2f * O, 4f);

            // 2b. transition groups around the main clearing (rules 2: main clearing 35-50 m across + smaller side clearings):
            //     low only (M <= 0.9 m, the rest <= 0.4 m), no colliders, so the open floor is framed, not blocked
            MidGroup(-19f, 10f); MidGroup(17f, -11f); MidGroup(-8f, -21f); MidGroup(22f, 12f); MidGroup(-27f, -9f); MidGroup(6f, 21f);

            // 3. hook: an old dead trunk sunk in a boulder mound at the north-east edge (3 boulders overlapping, a stump)
            FitH("EdgeFeatures/EF_RF_DeadTrunk", 44f, 30f, 26f, 0.05f, 2f);   // 11-19 later: 9 -> 26 m (a big old tree, 14 men)
            FitH(GBig[0], 42.6f, 28.6f, 2.4f, 0.22f, 5f); FitH(GBig[1], 45.6f, 28.9f, 2.0f, 0.2f, -4f); FitH(GBig[0], 44.4f, 31.8f, 2.8f, 0.22f, 3f);
            FitH(GStump[0], 41.3f, 30.9f, 1.6f, 0.1f);
            GSkirt(43.8f, 28f, 2.2f, 9, new Vector2(-0.8f, -0.6f), true);
            return count;
        }
    }
}
