// 11-19 대전 앞 court layer (Kido: "this is the front of the throne hall - look at real palaces (경복궁 근정전 ...) and make it
// like a real palace"; "not too complicated, place objects so it gives that feeling"; "use height differences and object
// filling for volume"). Refs: C:/Users/PC/Downloads/scene-refs/palace-court (board + sources.txt). Palace room only, built
// after the volume pilot and only while PlayerPrefs RR.EnvRecompose.palace.court = 1 (DEFAULT OFF until Kido approves).
// It changes no light: the 33 deg moon and the low ambient come from the volume pilot (RR.EnvRecompose.palace.volume).
// Kept as they are: the 박석 floor, the raised 어도 (0.13 m, |x| < 3.9), the 품계석 rows (x +-12), the hall and its terrace,
// the hanok wall, 해태 / 향로 / 드므 / 화로 and the PalaceStage vignettes.
// Passes 2-5 (reviews: nothing inside the arena -> furnish the court; toy-sized grey buildings -> true scale and their own
// colours; flat terrace bands; whole buildings in the frame read as boxes). Now, all downloaded (CC BY / Fab, CREDITS.txt):
//   1. 행각: the KCISA 수원화성행궁 득중정 at 1.0 along the north half of the west and east walls, where
//      its roof reads as a tile band in shade. Not on the lit east-south / south edges: the prop shader (SPLit) ignores URP
//      light layers, so those roofs could only be darkened by albedo, which the review rejected.
//   2. north edge: a near-symmetric 정전-front set (stone lions KHS ~2 m, 드므, stone lanterns, censers, stone pillars)
//      around the existing 해태 pair; the east half ruined; the axis open. (The Changdeokgung stairs scan is no longer used.)
//   3. order (passes 6-7): two even rows of KHS 품계석 at x +-7 (grounded in earth), mirrored stone-lantern rows at x +-14.6,
//      tall stone pillars in mirrored pairs for the long moon shadows; the 어도 is the floor's own raised road.
//   4. the south gate (KCISA 솟을삼문) behind the low south gallery with a 득중정 roof band either side (colour x0.62 on
//      those copies only), the 득중정 pair behind the north wall (pass 1).
//   Large pieces get world-aligned box colliders, small ones none.
// Left out (no free downloadable asset): the stone balustrade (돌난간) and its post animals, 정 on the terrace.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed partial class EnvRecompose
    {
        public const string CourtKey = "RR.EnvRecompose.palace.court";
        public static bool CourtOn() => PlayerPrefs.GetInt(CourtKey, 0) != 0;

        const string PC = "PalaceCourt/", EF = "EdgeFeatures/", PS = "PalaceStage/";
        static int courtPieces, courtCols;

        static void PalaceCourt(Scene scene)
        {
            int before = count; courtCols = 0;
            float front = TerraceFront(out float terraceTop);
            // pass 8 (Kido: "대전앞인데 뭔 무덤이 있고"; "약간 이런 느낌 괜찮은데" on the NE view; "궁궐 앞이니깐 그런식으로"):
            // standing stones, rank-stone rows and lantern rows read as a cemetery, so they are gone; the ruin vocabulary of
            // the north-east view (low wide heaps of stone, tile and broken timber, a 드므, a fallen banner, earth trails, a
            // warm light at the edge) is used instead, with a palace piece in every zone
            HideGraveLike();
            SouthBand();
            NorthTerrace();
            Colonnades();
            Ruins();
            Peninsulas();
            Verticals();
            WestBand();
            CourtLights();
            NorthGalleries();
            SouthGate();
            WalkHook();
            int quads = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i).gameObject;
                if (c.activeSelf && (c.name.StartsWith("PS_G_") || c.name == "ER_FogCard")) { c.SetActive(false); quads++; }
            }
            Debug.Log($"[EnvRecompose]   court: switched off {quads} code-made quads (ground patches, fog cards)");
            courtPieces = count - before;
            Debug.Log($"[EnvRecompose]   court: {courtPieces} pieces, {courtCols} colliders, hall terrace front z {front:0.0} top {terraceTop:0.00} m");
        }

        // the hall terrace (LookStudyRuins kit, collider outside the arena): walk north along the axis until the ground jumps
        // (pass 1: the invisible arena fence at z 46.8 answered first, 5.9 m tall; a terrace is 0.8-3.5 m high)
        static float TerraceFront(out float top)
        {
            float g0 = GroundAt(0f, 40f); top = 0f;
            for (float z = 47.5f; z < 70f; z += 0.25f)
            {
                var p = new Vector3(0f, 0f, z) + off;
                foreach (var h in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                {
                    float dy = h.point.y - g0;
                    if (dy > 0.8f && dy < 3.5f && !h.collider.GetComponentInParent<PlayerController>()) { top = dy; return z; }
                }
            }
            return 52f;
        }

        // ------------------------------------------------------------------ colliders: world-aligned boxes in the layer's holder
        static void ColBox(Vector3 centreWorld, Vector3 size)
        {
            var go = new GameObject("ER_CourtCol");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(centreWorld, Quaternion.identity);
            go.AddComponent<BoxCollider>().size = size;
            courtCols++;
        }
        static Bounds WB(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
        // whole bounds (buildings, vats); `shrink` pulls the sides in (eaves overhang the base)
        static void ColAll(GameObject g, float shrink = 0f, float maxH = 99f)
        {
            if (!g) return; var b = WB(g);
            float h = Mathf.Min(b.size.y, maxH);
            ColBox(new Vector3(b.center.x, b.min.y + h * 0.5f, b.center.z), new Vector3(Mathf.Max(0.3f, b.size.x - shrink * 2f), h, Mathf.Max(0.3f, b.size.z - shrink * 2f)));
        }
        // a post at the foot (trees, pillars): the crown never blocks
        static void ColFoot(float x, float z, float r, float h)
        {
            ColBox(new Vector3(x, GroundAt(x, z) + h * 0.5f, z) + new Vector3(off.x, 0f, off.z), new Vector3(r * 2f, h, r * 2f));
        }
        static void Cast(GameObject g)
        {
            if (!g) return;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
        }

        // ------------------------------------------------------------------ 2. north edge
        // Pass 5 (review: the stairs scan read as flat bands in every pass): no scan. The north edge in front of the hall
        // terrace is a near-symmetric 정전-front set around the existing 해태 pair (x +-9, z 48), 향로 (x +-17) and 드므
        // (x +-32): a stone-lion pair, a second 드므 pair, stone lanterns, big censers and stone pillars stepping out along
        // the edge; the axis (|x| < 4) stays open. The east half is the ruined one: its lion knocked over, its lantern
        // lying broken, its pillar fallen across rubble heaps. Everything sits north of z 42 (the arena edge is z 47).
        static void NorthTerrace()
        {
            // west half: standing
            Lion(-5.6f, 45.2f, false);
            NDumu(-13f, 44.6f, 0f);
            Big(EF + "PP_IncenseB", -25f, 45.4f, 3.1f, 0.03f, 1f, 0.5f);
            // east half: ruined
            Lion(5.6f, 45.2f, false);                            // pass 6: both lions stand (the symmetry has to read)
            NDumu(13.2f, 44.3f, R(10f, 16f));
            Big(EF + "PP_IncenseB", 25.3f, 45.6f, 2.9f, 0.05f, 7f, 0.5f);
            Fallen(25.8f, 42.6f, 5.2f, 165f);                   // pass 8: only the lying pillar (Kido liked it in the NE view)
            for (int i = 0; i < 6; i++)
            {
                var r = Prop(PS + "PS_Rubble", R(17f, 30f), R(42.2f, 46f), R(0, 360), 1f, R(-12f, 12f), R(-12f, 12f), 0.15f);
                if (r) { FitTop(r, R(0.6f, 1.3f), 0.15f); Matte(r, 0.85f); Cast(r); }
            }
            for (int i = 0; i < 5; i++) { var t = Prop(PS + (i % 2 == 0 ? "PS_Tile" : "PS_TileBird"), R(16f, 28f), R(41.5f, 45f), R(0, 360), R(0.9f, 1.2f), R(-25f, 25f), R(-25f, 25f), 0.15f); if (t) FitTop(t, R(0.3f, 0.55f), 0.15f); }
            // the foot of the hall terrace darker either side of the open axis
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", -12f, 45.6f, R(3f, 4f), R(14f, 16f), 90f, 0.024f);
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", 14f, 45.2f, R(3f, 4f), R(14f, 16f), 90f, 0.024f);
        }
        static void Lion(float x, float z, bool toppled)
        {
            var g = toppled
                ? Prop(PC + "PC_Lion", x, z, R(120f, 160f), 1.95f, R(4f, 8f), 78f, 0.2f)
                : Prop(PC + "PC_Lion", x, z, 180f + R(-4f, 4f), 2.5f, 0f, 0f, 0.05f);    // pass 9: ~2.6 m, reads at 1.6x
            if (g) { Cast(g); Matte(g, 0.95f); ColAll(g, 0.2f); }
            Skirt(x, z, 1.2f, toppled ? 5 : 3, toppled);
        }
        static void NDumu(float x, float z, float tilt)
        {
            var d = Prop(EF + "PP_Dumu", x, z, R(0f, 360f), 1.85f, 0f, tilt, tilt > 0f ? 0.12f : 0.05f);
            if (d) { Cast(d); Matte(d, 0.9f); ColAll(d, 0.15f); }
            Skirt(x, z, 1.2f, 3, tilt > 0f);
        }

        // ------------------------------------------------------------------ 1. 행각 at the edges
        // 득중정 at 1.0: 36.6 m long, 8.9 m deep, 7.5 m to the ridge. Pass 5 (review: whole buildings in the frame read as
        // boxes from this camera, lit or darkened): only the two north-side runs stay (west and east wall, z 14-50), whose
        // roofs read as tile bands in the shade of the north wall and pines; the east-south and both south runs are gone,
        // and the west-south collapsed bay is now its debris only (a building there would cross the outer pines).
        const float HallS = 1f;
        static void Colonnades()
        {
            float d = 8.85f * HallS * 0.5f, half = 36.56f * HallS * 0.5f;
            Hall(-52f + d, -5f - half, 90f, true, false);         // west-south: collapsed bay, debris only (pass 6 retry: seen end-on it was a box again)
            Hall(-52f + d, 14f + half, 90f, false, true);
            Hall(52f - d, 14f + half, -90f, false, true);
        }
        static void Hall(float x, float z, float yaw, bool collapsed, bool building)
        {
            bool ew = Mathf.Abs(Mathf.Abs(yaw) - 90f) < 1f;
            float len = 36.56f * HallS * 0.5f, hd = 8.85f * HallS * 0.5f;
            if (building)
            {
                var g = Prop(PC + "PC_Hall", x, z, yaw + R(-0.8f, 0.8f), HallS, 0f, 0f, 0.03f, 6f);
                if (!g) return;
                Cast(g); Matte(g, 1f); ColAll(g, 1.1f, 3f);
                // the base buried along its court side
                for (int i = 0; i < 4; i++)
                {
                    float t = R(-len, len);
                    float bx = ew ? x - Mathf.Sign(x) * (hd + 0.4f) : x + t;
                    float bz = ew ? z + t : z + hd + 0.4f;
                    Skirt(bx, bz, 1.0f, 3, i % 2 == 0);
                }
            }
            if (!collapsed) return;
            // the roof spilled 4-6 m into the court: tiles, charred beams, rubble
            float inward = -Mathf.Sign(x);
            for (int i = 0; i < 9; i++)
            {
                float t = R(-len * 0.7f, len * 0.7f), dx = inward * (hd + R(0.5f, 5.5f));
                var tile = Prop(PS + (Chance(0.6) ? "PS_Tile" : "PS_TileBird"), x + dx, z + t, R(0, 360), R(0.9f, 1.2f), R(-25f, 25f), R(-25f, 25f), 0.15f);
                if (tile) FitTop(tile, R(0.25f, 0.6f), 0.15f);
            }
            for (int i = 0; i < 4; i++)
            {
                float t = R(-len * 0.6f, len * 0.6f), dx = inward * (hd + R(1f, 5f));
                Cast(Prop(EF + "PP_Beam", x + dx, z + t, R(0, 360), R(2.4f, 2.9f), 0f, R(-6f, 6f), 0.05f));
            }
            for (int i = 0; i < 5; i++)
            {
                float t = R(-len * 0.7f, len * 0.7f), dx = inward * (hd + R(0.3f, 4.5f));
                var r = Prop(PS + "PS_Rubble", x + dx, z + t, R(0, 360), 1f, R(-10f, 10f), R(-10f, 10f), 0.2f);
                if (r) { FitTop(r, R(0.4f, 0.9f), 0.2f); Matte(r, 0.85f); }
            }
            Flat($"PalaceStage/PS_G_Ash_{rng.Next(3)}", x + inward * (hd + 2.5f), z, R(5f, 6f), R(16f, 20f), 0f, 0.025f);
        }

        // ------------------------------------------------------------------ 3. mid-court clusters
        static void Clusters()
        {
            // pass 6 (Kido: "대전앞이 많이 어설퍼"; review: scattered asymmetric clusters read as a junk yard, the order of a
            // real court is what reads as a palace): the scattered clusters are gone; only tall stone pillars stay, in pairs
            // mirrored on the axis, for the long moon shadows across the lit floor (the east trio was liked, so the west
            // gets the same trio)
            foreach (float sx in new[] { -1f, 1f })
            {
                Pillar(sx * 31f, -5f, 5.5f, 0.06f, 1.5f); Pillar(sx * 28.4f, -7.4f, 3f, 0.08f, 6f); Fallen(sx * 33.6f, -8.2f, 4.2f, sx > 0 ? 145f : 35f);
                Pillar(sx * 14.2f, 7.4f, 6f, 0.05f, 1.5f);
                Pillar(sx * 30f, 25f, 5f, 0.06f, 2f);
            }
        }

        // ------------------------------------------------------------------ 0. rank-stone rows, lanterns, south roof band
        // Pass 7: the pass-6 어도 of laid stone blocks read as a row of square tiles (Kido: no square tile outlines) and the
        // only paving in the project is that square-block scan, so the 어도 is left to the floor's own raised road; the
        // axis is carried by the rank-stone rows, the lantern rows and the mirrored pillars.
        // 품계석: the KHS 근정전 rank stone (1.1 m, real size; at 0.8 it read as specks) in two even rows at x +-7, 5 m apart, from the south row to
        // the north front, none inside the 12 m centre circle; a few lean or lie toppled, one or two missing. The map's own
        // rows at x +-12 stay as they are. No colliders (low; bullets fly at muzzle height).
        static void RankRows()
        {
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                for (float z = -42f; z <= 40.5f; z += 5f)
                {
                    float x = sx * 7f;
                    if (new Vector2(x, z).magnitude < 12.5f) continue;
                    if (Chance(0.06)) continue;
                    double c = rng.NextDouble();
                    float yaw = (sx < 0 ? 90f : -90f) + R(-4f, 4f);
                    GameObject g;
                    if (c < 0.08) g = Prop(PC + "PC_RankStone", x + R(-0.4f, 0.4f), z + R(-0.3f, 0.3f), R(0, 360), 1f, 84f, R(-6f, 6f), 0.25f);       // toppled
                    else if (c < 0.24) g = Prop(PC + "PC_RankStone", x, z, yaw, 1f, R(-12f, 12f), R(-12f, 12f), 0.12f);                         // leaning
                    else g = Prop(PC + "PC_RankStone", x, z, yaw, 1f, R(-2f, 2f), R(-2f, 2f), 0.04f);
                    if (!g) continue;
                    Cast(g); Matte(g, 0.9f); n++;
                    // grounded: a little dark earth round the foot (pass 7: they read as floating dots)
                    Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x + R(-0.2f, 0.2f), z + R(-0.2f, 0.2f), R(1.3f, 1.8f), R(1.5f, 2.1f), R(0, 360), 0.028f);
                }
            Debug.Log($"[EnvRecompose]   court: {n} rank stones");
        }
        // stone lanterns (the project's Korean stone lantern, CC BY) in mirrored rows just outside the map's rank stones
        // (x +-14.6), every 10 m: a vertical rhythm along the axis and long moon shadows; none lit (light budget, brightness)
        static void Lanterns()
        {
            foreach (float sx in new[] { -1f, 1f })
                for (float z = -40f; z <= 38.5f; z += 10f)
                {
                    if (Mathf.Abs(z - 7.4f) < 4f) continue;                         // the 6 m pillar pair stands there
                    float x = sx * 14.6f;
                    if (new Vector2(x, z).magnitude < 12.5f) continue;
                    bool fallen = (sx > 0 && Mathf.Abs(z + 20f) < 0.1f) || (sx < 0 && Mathf.Abs(z - 30f) < 0.1f);   // two down, never mirrored
                    if (fallen)
                    {
                        var lt = Prop("StageLantern/KoreanStoneLantern", x + R(-0.5f, 0.5f), z, R(0, 360), 1.2f, 84f, R(-6f, 6f), 0.2f);
                        if (lt) { FitTop(lt, 0.7f, 0.2f); Cast(lt); Matte(lt, 0.8f); }
                        Skirt(x, z, 1.2f, 4, true);
                        continue;
                    }
                    Vert("StageLantern/KoreanStoneLantern", x, z, 2.5f, (sx < 0 ? 90f : -90f) + R(-6f, 6f), 0.04f, 1.2f);
                    ColFoot(x, z, 0.45f, 1.8f);
                    Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x, z, R(1.8f, 2.4f), R(2f, 2.6f), R(0, 360), 0.028f);
                }
        }
        // the south roof band: the 30k-triangle 득중정 (its modelled tiles self-shadow into the pattern) right behind the low
        // south gallery (z -49.5), either side of the gate, so only its roof reaches the bottom edge of the south views. In
        // open moonlight these props render brighter than the self-shaded floor (the prop shader takes the key light the
        // volume pilot lifts), so this instance alone gets a plain colour multiplier (a material copy, nothing shared).
        const float SouthBandK = 0.62f;
        static void SouthBand()
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                var g = Prop(PC + "PC_Hall", sx * 24.5f, -54.2f, 180f + R(-0.6f, 0.6f), HallS, 0f, 0f, 0.03f, 6f);
                if (!g) continue;
                Cast(g); Matte(g, SouthBandK);
            }
        }
        // ------------------------------------------------------------------ pass 8: hide what reads as graves
        // Built before this layer (by the volume pilot and the base EnvRecompose pass) and only switched off while the court
        // is on: the volume pilot's upright stone pillars, 장승, lanterns, pagoda, dead trunks / trees / snags (stelae and a
        // cemetery from the top-down camera) and the row of round mud bands along the south edge (z < -44.5, grave mounds).
        // Their rubble skirts and the volume's warm lights stay.
        static readonly string[] GraveLike = { "StonePillar", "Jangseung", "Lantern", "Pagoda", "DeadTrunk", "Snag", "DeadTree", "DeadConifer" };
        static void HideGraveLike()
        {
            int n = 0, m = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i).gameObject;
                if (!c.activeSelf) continue;
                bool grave = false;
                foreach (var k in GraveLike) if (c.name.Contains(k)) { grave = true; break; }
                if (grave) { c.SetActive(false); n++; continue; }
                var p = c.transform.position - off;
                if (p.z < -44.5f && (c.name.StartsWith("PS_G_Mud") || c.name.StartsWith("PS_G_Ash"))) { c.SetActive(false); m++; }
            }
            Debug.Log($"[EnvRecompose]   court: hid {n} upright volume pieces, {m} south-edge mud bands");
        }

        // ------------------------------------------------------------------ pass 8: palace ruin zones (the NE vocabulary)
        // one low wide heap (rubble 0.5-1.3 m overlapping), roof tiles and ridge pieces spilling out, charred beams, one clear
        // palace piece (드므, 향로, 해태 or a collapsed 솟을삼문), a fallen banner, earth trails that cut the floor's grain.
        // Every zone different in size, turn and mix; wide gaps between zones; nothing inside the 12 m centre circle.
        static void Ruins()
        {
            // self-check: at r ~18 the centre view (~+-14 x +-9 m) missed both; at r ~15 they sit in its corners
            // pass 10: inside the court only walk-over pieces and tall thin ones; vats / censers only at the edges
            Zone(-13.8f, -7.8f, 2.4f, 25f, "none");      // centre view, lower left
            Zone(13.6f, 7.6f, 2.2f, 200f, "haetae");     // centre view, upper right (haetae on its base: tall-thin class)
            Zone(-35f, -9f, 3.8f, 80f, "haetae");       // west (self-check: the collapsed 솟을삼문 read as a tilted box)
            Zone(34f, -10f, 3.2f, 140f, "none");        // east
            Zone(-15f, -41f, 3f, 10f, "dumu");          // south edge, trails reaching over the south strip
            Zone(17f, -39.5f, 2.4f, 300f, "beams");     // south
            Zone(-31f, 30f, 3f, 250f, "none");          // north-west
        }
        static void Zone(float x, float z, float rad, float dir, string piece)
        {
            float d = dir * Mathf.Deg2Rad; var along = new Vector2(Mathf.Cos(d), Mathf.Sin(d)); var across = new Vector2(-along.y, along.x);
            // the heap: overlapping rubble pieces, higher in the middle
            int heap = Mathf.RoundToInt(rad * 2.2f);
            for (int i = 0; i < heap; i++)
            {
                float a = R(0f, 6.283f), r = rad * Mathf.Sqrt(R(0f, 1f)) * 0.8f;
                var g = Prop(PS + "PS_Rubble", x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r, R(0, 360), 1f, R(-12f, 12f), R(-12f, 12f), 0.18f);
                if (g) { FitTop(g, Mathf.Lerp(0.45f, 0.2f, r / rad) * R(0.85f, 1.1f), 0.18f); Matte(g, 0.85f); Cast(g); }   // pass 10: ankle height (no waist-high masses inside)
            }
            // roof tiles and ridge pieces spilling out along `dir`
            for (int i = 0; i < Mathf.RoundToInt(rad * 3f); i++)
            {
                var q = new Vector2(x, z) + along * (R(0.3f, 1.6f) * rad) + across * (R(-rad, rad) * 0.6f);
                var g = Prop(PS + (Chance(0.35) ? "PS_TileBird" : "PS_Tile"), q.x, q.y, R(0, 360), R(0.9f, 1.25f), R(-25f, 25f), R(-25f, 25f), 0.15f);
                if (g) FitTop(g, R(0.25f, 0.55f), 0.15f);
            }
            // charred beams across the heap
            int beams = piece == "beams" ? 4 : 2;
            for (int i = 0; i < beams; i++) Cast(Prop(EF + "PP_Beam", x + R(-rad, rad) * 0.6f, z + R(-rad, rad) * 0.6f, dir + R(-50f, 50f), R(2.4f, 3f), 0f, R(-6f, 6f), 0.05f));
            // the palace piece
            var side = new Vector2(x, z) - along * (rad * 0.9f);
            switch (piece)
            {
                case "dumu": Med(EF + "PP_Dumu", side.x, side.y, 1.35f, R(0f, 8f)); break;
                case "censer": Big(EF + "PP_IncenseC", side.x, side.y, 2.2f, 0.04f, 3f, 0.4f); break;
                case "haetae":
                {
                    // pass 10: standing on its base (Kido's list: 받침 위 해태상), thin foot collider under bullet height
                    var h = Prop(EF + "PP_Haetae", side.x, side.y, 180f + R(-25f, 25f), 1.1f, 0f, R(-3f, 3f), 0.04f);
                    if (h) { Cast(h); Matte(h, 1.25f); ColFoot(side.x, side.y, 0.5f, 0.8f); }
                    break;
                }
                case "gate":
                {
                    // the 솟을삼문 (red posts, painted beams, tiled roof) fallen over and half under its own rubble
                    var g = Prop(PC + "PC_Gate", x - along.x * 1.5f, z - along.y * 1.5f, dir + 90f, 0.75f, R(16f, 22f), R(-12f, -8f), 0.42f, 6f);
                    if (g) { Cast(g); Matte(g, 0.75f); ColAll(g, 0.8f, 2.2f); }
                    break;
                }
            }
            // a fallen banner beside it (cloth face up, pressed flat, as the court's own banners)
            var fp = new Vector2(x, z) + across * (rad * R(0.9f, 1.3f));
            var flag = Prop(EF + "PP_Flag", fp.x, fp.y, dir + R(-40f, 40f), R(0.66f, 0.78f), -88f, R(-3f, 3f), 0.02f);
            if (flag) { var t = flag.transform; t.localScale = new Vector3(t.localScale.x, t.localScale.y, t.localScale.z * 0.3f); }
            // earth trails cutting the floor grain (stretched along `dir`, 2-3 overlapping)
            int trails = Chance(0.5) ? 2 : 3;
            for (int i = 0; i < trails; i++)
            {
                var q = new Vector2(x, z) + along * (R(-0.5f, 1.8f) * rad);
                Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", q.x, q.y, R(2.2f, 3.4f), R(6f, 10f), 90f - dir + R(-15f, 15f), 0.026f + 0.001f * i);
            }
            // the heap's own footprint dark (walk-over: no collider since pass 10)
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x, z, rad * R(2f, 2.4f), rad * R(2.2f, 2.8f), R(0, 360), 0.025f);
        }

        // ------------------------------------------------------------------ pass 9: west roof band
        // the 득중정 between the two outer west pines (z -33.8 and -7, x -57.5 / -59.5) and outside the west wall (x -52.6),
        // at 0.7 so it fits that gap (25.6 m), its inner eave ~1 m over the wall: from the west view only the tile roof
        // reaches the lower-left edge. Colour multiplier on its own material copies only (it lies in the wall's shade).
        static void WestBand()
        {
            const float s = 0.7f;
            var g = Prop(PC + "PC_Hall", -52.6f - 8.85f * s * 0.5f + 1f, -20.4f, 90f + R(-0.5f, 0.5f), s, 0f, 0f, 0.03f, 6f);
            if (g) { Cast(g); Matte(g, 0.8f); }
        }

        // ------------------------------------------------------------------ pass 10: tall palace pieces inside the court
        // Kido 11-19 21:47 "환경이 아름다워 보이게", rule §2-A: wherever the player stands, 2-3 palace structures in frame;
        // inside the court only walk-over things, tall thin ones (>= 4 m, round collider <= 0.5 m that stops movement but
        // not bullets: 0.8 m high, under muzzle height) and overhead ones. Tall thin pieces used, all palace and never grey
        // stelae: the KCISA 수원화성행궁 small gates (whole downloaded models, since pass 11; pass 10 had cut a pillar out of 득중정), the KHS 깃발 upright on its pole, lit stone lanterns (warm light, see CourtLights),
        // 해태 on its base. Variants: standing / sunk and leaning / gone (only its beams and tiles left).
        // Groups 2-3 pieces, gaps >= 4.5 m; the axis road (|x| < 3) and the door lane (|z| < 3 west / east) stay open.
        static readonly List<Vector2> lanternSpots = new List<Vector2>();
        static void Verticals()
        {
            lanternSpots.Clear(); glowSpots.Clear();
            // round the centre (the old 12 m circle is gone: 2-3 pieces in every centre frame)
            Column(-6.5f, 5.5f, 0); Column(7f, -6f, 0); Column(6.6f, 7.8f, 1); Flag(-7.6f, -6.6f, 5.6f);
            Lantern(-10.5f, 3.6f); Lantern(10.4f, -3.4f);
            // north half, west: a pair with a beam across, one lying
            Column(-12f, 20f, 0); Column(-7.4f, 20f, 1); Column(-12.6f, 25.5f, 2);   // pass 10e: no beam across (read as a torii)
            Flag(-16.5f, 14.5f, 5.2f);
            // north half, east: standing, sunk, a banner
            Column(8f, 26f, 0); Column(12.6f, 26.4f, 1); Flag(10.5f, 20.5f, 5.8f); Lantern(15.5f, 17f);
            // south half, west: a pair with a beam, a lit lantern
            Column(-13.5f, -22f, 1); Column(-9f, -22.3f, 0); Lantern(-10.6f, -28.4f);
            // south half, east: banner, standing, sunk
            Flag(10.4f, -17.5f, 5.4f); Column(11f, -30f, 0); Column(6.4f, -30.4f, 1);
            // east / west of the door lanes (|z| > 3)
            Column(-24f, 6f, 0); Flag(-22f, -6.5f, 5f); Column(24.5f, -6.5f, 0); Column(25f, 7f, 2);
            // pass 10d: the north / south / west / east edge views held one structure each: one more lit group in each
            Column(-6.6f, 33f, 0); Flag(-9.8f, 35.2f, 5.4f);
            Column(6.6f, -37.4f, 0); Column(10.8f, -37.8f, 1);
            Column(-36f, 5.6f, 0); Flag(-32.6f, 6.2f, 5.2f);
            Column(36f, -5.6f, 0); Flag(36.8f, 5.8f, 5.4f);
            // pass 10b: under CrispLook only what stands in a light reads, so every group gets a small warm light at its foot
            // (groups with a lit lantern close by share it)
            foreach (var g in new[] { new Vector2(-9.7f, 18.6f), new Vector2(10.3f, 24.4f), new Vector2(8.7f, -28.6f), new Vector2(-23f, 0.2f),
                                      new Vector2(24.7f, 0.2f), new Vector2(13.6f, 7.6f), new Vector2(-35f, -9f), new Vector2(-7.4f, -8f),
                                      new Vector2(6.6f, 6.6f), new Vector2(22f, 35.5f), new Vector2(-33.5f, 21f), new Vector2(-19.5f, -37.5f),
                                      new Vector2(-7f, 31.6f), new Vector2(8.7f, -37.6f), new Vector2(-34.4f, 5.6f), new Vector2(36.4f, -4.4f) })   // + centre-east column, the three roof peninsulas
                glowSpots.Add(g);
        }
        static readonly List<Vector2> glowSpots = new List<Vector2>();
        // pass 11 (Kido: "너 또 프랍들 너가 만들었어?" - the red pillar had been cut out of the 득중정 model): the tall palace piece is
        // now a whole downloaded model, the KCISA 수원화성행궁 small gates 경선문 / 가어문 (red posts, painted lintel, tiled roof,
        // 3.5 x 2.5 x 4.3 m), used as they come. state 0 standing, 1 sunk ~30 % and leaning (half-collapsed), 2 gone: only its
        // charred beams and roof tiles lie there (no lying box).
        static int gateFlip;
        static void Column(float x, float z, int state)
        {
            if (state == 2)
            {
                for (int i = 0; i < 2; i++) Cast(Prop(EF + "PP_Beam", x + R(-1.2f, 1.2f), z + R(-1.2f, 1.2f), R(0, 360), R(2.4f, 2.9f), 0f, R(-6f, 6f), 0.05f));
                for (int i = 0; i < 5; i++) { var t = Prop(PS + (i % 2 == 0 ? "PS_Tile" : "PS_TileBird"), x + R(-2f, 2f), z + R(-2f, 2f), R(0, 360), 1f, R(-25f, 25f), R(-25f, 25f), 0.15f); if (t) FitTop(t, R(0.25f, 0.5f), 0.15f); }
                return;
            }
            string gate = (gateFlip++ % 2 == 0) ? PC + "PC_GateS" : PC + "PC_GateA";
            float yaw = R(-25f, 25f) + (Chance(0.5) ? 180f : 0f);
            var g = state == 1 ? Prop(gate, x, z, yaw, 1f, R(5f, 8f), R(-8f, -5f), 0.3f)
                               : Prop(gate, x, z, yaw, 1f, R(-1f, 1f), R(-1f, 1f), 0.02f);
            if (!g) return;
            Cast(g); Matte(g, 1.15f);
            // its two posts block walking (0.8 m, under muzzle height); the doorway between them stays passable
            var b = WB(g); var c = new Vector2(b.center.x - off.x, b.center.z - off.z);
            var along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            ColFoot(c.x + along.x * 1.45f, c.y + along.z * 1.45f, 0.35f, 0.8f);
            ColFoot(c.x - along.x * 1.45f, c.y - along.z * 1.45f, 0.35f, 0.8f);
            for (int i = 0; i < 3; i++)
            {
                float a = R(0f, 6.283f), d = R(1.2f, 2.4f);
                var t = Prop(PS + (i == 0 ? "PS_Rubble" : "PS_Tile"), x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, R(0, 360), 1f, R(-20f, 20f), R(-20f, 20f), 0.15f);
                if (t) FitTop(t, R(0.15f, 0.35f), 0.15f);
            }
        }
        static void Flag(float x, float z, float h)
        {
            var g = Prop(EF + "PP_Flag", x, z, R(0, 360), 1f, R(-3f, 3f), R(-3f, 3f), 0.02f);
            if (!g) return;
            var b = WB(g); if (b.size.y > 0.1f) g.transform.localScale *= h / b.size.y;
            b = WB(g); var p = g.transform.position - off;
            g.transform.position += Vector3.up * (GroundAt(p.x, p.z) - b.min.y);
            Cast(g); Matte(g, 1.3f);
            ColFoot(x, z, 0.25f, 0.8f);
            Skirt(x, z, 0.8f, 2, false);
        }
        static void Lantern(float x, float z)
        {
            var lg = Vert("StageLantern/KoreanStoneLantern", x, z, 2.7f, R(0, 360), 0.17f, 1f);   // pass 10e: base mostly under the floor
            // the scan's own vertex colours (VColLit), not brightened (pass 11: the x1.4 / x1.15 made the base a pale box)
            Skirt(x, z, 0.9f, 5, false);
            ColFoot(x, z, 0.45f, 0.8f);
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x, z, R(2f, 2.6f), R(2.2f, 2.8f), R(0, 360), 0.027f);
            lanternSpots.Add(new Vector2(x, z));
        }

        // pass 10: collapsed roofs as peninsulas from the edges (the hook: the north-east one, 8-12 m into the court) - the
        // 30k 득중정 sunk to its eaves and tipped, so only tile roof shows; walk-blocking (they are edge masses)
        static void Peninsulas()
        {
            Roof(24f, 39f, 6f, 0.72f, -9f, 0.52f);           // the hook, in front of the ruined east half of the north front
            Roof(-36.5f, 23f, 112f, 0.5f, 11f, 0.55f);       // off the west roof band
            Roof(-21f, -40.5f, 8f, 0.5f, 10f, 0.56f);        // off the south band
        }
        static void Roof(float x, float z, float yaw, float s, float lay, float sink)
        {
            var g = Prop(PC + "PC_Hall", x, z, yaw, s, lay, R(-4f, 4f), sink, 6f);
            if (!g) return;
            Cast(g); Matte(g, 0.85f); ColAll(g, 1.2f, 1.6f);
            var b = WB(g);
            for (int i = 0; i < 8; i++)
            {
                var t = Prop(PS + (Chance(0.4) ? "PS_TileBird" : "PS_Tile"), R(b.min.x, b.max.x) - off.x, R(b.min.z, b.max.z) - off.z + R(-3f, 3f), R(0, 360), R(0.9f, 1.2f), R(-25f, 25f), R(-25f, 25f), 0.15f);
                if (t) FitTop(t, R(0.25f, 0.5f), 0.15f);
            }
        }

        // ------------------------------------------------------------------ lights (pass 10: lit lanterns)
        // A lantern must be lit or it reads as a cemetery lantern. Warm point lights at the lanterns (the pass-9 pools are
        // gone). Budget: the volume pilot's 2 warm lights are re-used (one stays at the north-front foot), 2 lowest-priority
        // non-fire field lights are switched off, the rest are new - the total goes over 8 by the count logged.
        static void CourtLights()
        {
            var warm = new List<Light>();
            foreach (var l in root.GetComponentsInChildren<Light>(true)) if (l && l.name == "ER_WarmBase") warm.Add(l);
            var field = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.enabled && l.type != LightType.Directional && !l.transform.IsChildOf(root) && !l.GetComponentInParent<PlayerController>()
                    && !l.name.StartsWith("Player") && !l.name.StartsWith("Muzzle") && l.gameObject.scene == root.gameObject.scene) field.Add(l);
            int Prio(Light l) => (l.name.Contains("Fire") ? 2 : 0) + (l.shadows != LightShadows.None ? 1 : 0);
            field.Sort((a, b) => Prio(a) != Prio(b) ? Prio(a).CompareTo(Prio(b)) : a.intensity.CompareTo(b.intensity));
            var off2 = new List<string>();
            for (int i = 0; i < field.Count && off2.Count < 2; i++) { if (Prio(field[i]) >= 2) break; field[i].enabled = false; off2.Add(field[i].name); }
            var foot = warm.Count > 0 ? warm[warm.Count - 1] : null;
            if (foot) foot.transform.position = new Vector3(-9.2f, GroundAt(-9.2f, 44.4f) + 0.7f, 44.4f) + new Vector3(off.x, 0f, off.z);
            int reused = 0, made = 0;
            foreach (var p in lanternSpots)
            {
                Light l = reused < warm.Count - 1 ? warm[reused++] : null;
                if (!l) { var go = new GameObject("ER_CourtLantern"); go.transform.SetParent(root, false); l = go.AddComponent<Light>(); made++; }
                l.name = "ER_CourtLantern";
                l.transform.position = new Vector3(p.x, GroundAt(p.x, p.y) + 1.6f, p.y - 0.6f) + new Vector3(off.x, 0f, off.z);
                l.type = LightType.Point; l.color = new Color(1f, 0.66f, 0.36f); l.intensity = 5f; l.range = 8.5f;   // pass 10c: light islands
                l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            }
            foreach (var p in glowSpots)
            {
                var go = new GameObject("ER_CourtGlow"); go.transform.SetParent(root, false);
                // pass 10c (2.2 / 5 m barely showed under CrispLook): a warm island per group, set 2.6 m up and 1.4 m to the
                // camera (south) side so the red faces and the eave tops the camera sees catch it; yellow-orange, never pink
                go.transform.position = new Vector3(p.x, GroundAt(p.x, p.y) + 2.6f, p.y - 1.4f) + new Vector3(off.x, 0f, off.z);
                var l = go.AddComponent<Light>(); made++;
                l.type = LightType.Point; l.color = new Color(1f, 0.68f, 0.38f); l.intensity = 5.5f; l.range = 8.5f;
                l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            }
            int pools = 0;
            if (PlayerPrefs.GetInt(CourtKey + ".pools", 1) != 0)   // RR.EnvRecompose.palace.court.pools = 0 switches the floor pools off
            {
                foreach (var q in lanternSpots) pools += FloorPool(q.x, q.y, 1f);
                foreach (var q in glowSpots) pools += FloorPool(q.x, q.y, 1.15f);
            }
            int total = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l && l.isActiveAndEnabled && l.type != LightType.Directional) total++;
            Debug.Log($"[EnvRecompose]   court floor pools: {pools} boxes");
            Debug.Log($"[EnvRecompose]   court lights: {lanternSpots.Count} lit lanterns ({reused} re-used warm, {made} new), north foot {(foot ? 1 : 0)}, switched off: {string.Join(", ", off2)}; point/spot lights on now {total}");
        }

        // pass 10d: the floor (LookStudy/StageTerrain, LSStage.hlsl) adds point lights x albedo x localSh, and localSh is the
        // _RRGroundShadow dimming the volume pilot / ShadowPass switch on, which cuts local lights inside the moon shadow - so
        // the islands lit the props but left no pool on the ground. The pool is laid the way StageAccents lays its moon
        // pools: RR_LightShaft in pool mode on the built-in cube (a back-face box decal, Blend DstColor One: the floor's own
        // pattern brightened by the colour, never washed flat). Two overlapping boxes, stretched and turned differently, so
        // the patch is soft and a little lopsided, not a round stamp. Yellow-orange only.
        static Shader shaftSh; static Mesh cubeMesh;
        static int FloorPool(float x, float z, float k)
        {
            if (!shaftSh) shaftSh = Resources.Load<Shader>("RR_LightShaft");
            if (!cubeMesh) cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (!shaftSh || !cubeMesh) return 0;
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                float yaw = R(0f, 180f), w = (i == 0 ? R(5.5f, 7f) : R(3.5f, 4.5f)) * k, d = (i == 0 ? R(6.5f, 8.5f) : R(4f, 5.5f)) * k;
                var c = new Vector2(x, z) + (i == 0 ? Vector2.zero : new Vector2(R(-1.4f, 1.4f), R(-1.4f, 1.4f)));
                var go = new GameObject("ER_CourtFloorPool");
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(new Vector3(c.x, GroundAt(c.x, c.y), c.y) + new Vector3(off.x, 0f, off.z), Quaternion.Euler(0f, yaw, 0f));
                go.transform.localScale = new Vector3(w, 5f, d);
                go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                var m = new Material(shaftSh) { name = "ER_CourtFloorPool" };
                m.SetFloat("_Mode", 1f); m.SetFloat("_Edge", i == 0 ? 1.3f : 1.6f); m.SetFloat("_Seed", R(0f, 50f));
                m.SetColor("_Color", (i == 0 ? new Color(1.25f, 0.78f, 0.36f) : new Color(0.8f, 0.5f, 0.22f)));
                mr.sharedMaterial = m;
                go.AddComponent<MeshChunker.TileMarker>();          // never split into tiles (the decal needs its own box)
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                n++;
            }
            return n;
        }

        // editor capture helper: ArtSource/PalaceCourt/walk.trigger -> PalaceCourtWalk walks the player round the court
        static void WalkHook()
        {
            if (!Application.isEditor) return;
            var t = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "PalaceCourt", "walk.trigger");
            if (!System.IO.File.Exists(t)) return;
            try { System.IO.File.Delete(t); } catch { }
            var w = root.gameObject.AddComponent<PalaceCourtWalk>();
            w.offset = new Vector2(off.x, off.z);
        }

        static void Pillar(float x, float z, float h, float sink, float lean = 2.5f)
        {
            var g = Vert(EF + "EF_RF_StonePillar", x, z, h, R(0, 360), sink, lean);
            if (g) ColFoot(x, z, 0.45f, Mathf.Min(h, 3f));
            Skirt(x, z, 1.1f, 4, true);
        }
        // a pillar lying on the floor, `len` long, `yaw` along it
        static void Fallen(float x, float z, float len, float yaw)
        {
            var g = Prop(EF + "EF_RF_StonePillar", x, z, yaw, 1f, 88f, R(-4f, 4f), 0.22f);
            if (!g) return;
            var b = WB(g); float l = Mathf.Max(b.size.x, b.size.z);
            if (l > 0.1f) g.transform.localScale *= len / l;
            b = WB(g); var p = g.transform.position - off;
            g.transform.position += Vector3.up * (GroundAt(p.x, p.z) - b.min.y - 0.22f * b.size.y);
            Cast(g); Matte(g, 0.8f); ColAll(g, 0.1f);
        }
        static void Big(string path, float x, float z, float h, float sink, float lean, float colShrink)
        {
            var g = Vert(path, x, z, h, R(0, 360), sink, lean);
            if (g) ColAll(g, colShrink, 2f);
            Skirt(x, z, 1.4f, 5, true);
        }
        static void Med(string path, float x, float z, float s = 1f, float tilt = 0f)
        {
            var g = Prop(path, x, z, R(0, 360), s * R(0.9f, 1.1f), 0f, tilt, 0.06f);
            if (!g) return;
            Cast(g); Matte(g, 0.88f);
            if (WB(g).size.y > 0.9f) ColAll(g, 0.15f);
        }
        static void Small(float x, float z, float rad, int n)
        {
            string[] bits = { PS + "PS_Rubble", PS + "PS_Rocks", PS + "PS_Tile", PS + "PS_Pebbles", PS + "PS_Jar", PS + "PS_Pot", PS + "PS_Debris" };
            for (int i = 0; i < n; i++)
            {
                float a = R(0f, Mathf.PI * 2f), d = rad * R(0.5f, 1.15f);
                var g = Prop(bits[rng.Next(bits.Length)], x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, R(0, 360), R(0.8f, 1.2f), R(-15f, 15f), R(-15f, 15f), 0.15f);
                if (g) FitTop(g, R(0.15f, 0.45f), 0.15f);
            }
            Flat($"PalaceStage/PS_G_Mud_{rng.Next(3)}", x + R(-0.5f, 0.5f), z + R(-0.5f, 0.5f), rad * R(1.6f, 2f), rad * R(1.8f, 2.3f), R(0, 360), 0.024f);
        }

        // ------------------------------------------------------------------ 4. pass-1 pieces outside the arena
        // 득중정 at 0.8 right behind the north wall (z 47.4), both sides of the hall, the east one leaning
        static void NorthGalleries()
        {
            const float s = 0.8f, z = 51.5f;
            var w = Prop(PC + "PC_Hall", -40f, z, 180f, s, 0f, 0f, 0.03f, 6f);
            if (w) { Matte(w, 0.85f); Cast(w); }
            var e = Prop(PC + "PC_Hall", 40.5f, z + 0.4f, 180f + R(-2f, 2f), s, R(1.5f, 2.5f), R(-3f, -2f), 0.08f, 6f);
            if (e) { Matte(e, 0.8f); Cast(e); }
        }
        // 솟을삼문 (16.4 m wide, 8 m to the ridge) on the axis right behind the low south gallery (z -49.5)
        static void SouthGate()
        {
            const float z = -54.5f;
            var g = Prop(PC + "PC_Gate", 0f, z, 90f, 1f, 0f, 0f, 0.03f, 6f);
            if (g) { Matte(g, 1f); Cast(g); }
        }
    }
}
