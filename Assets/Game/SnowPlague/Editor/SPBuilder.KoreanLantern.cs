// tr28: Korean stone lanterns (석등) in place of the Japanese-looking ones (SP_Arch_Lantern_A/B kit lanterns and the
// Megascans tōrō). Model: "Oriental_stone_lantern" by kebijini (CC BY 4.0, see CREDITS.md), decimated scan with a square
// pebble base slab, colour in the vertex colours (no textures) -> SnowPlague/Prop with _VColAlbedo.
// Arena rule: lanterns standing inside the arena are tall blockers, so every lantern is moved just past the nearest
// west / east / north boundary (never the camera side) and rebuilt there; lanterns already outside are swapped in place.
// Spots: the shared Lantern() spot (its range / look stays with the look worker) + a warm glow core in the fire box.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const float KLHeight = 1.7f, KLOut = 3f, KLMinGap = 8f;

    static void KLSwap()
    {
        klColLogged = false; klMeshes.Clear();
        if (LMPath("KoreanLantern") == null) { Log("KL no KoreanLantern model - swap skipped"); return; }
        var olds = root.GetComponentsInChildren<Transform>(true)
            .Where(t => (t.name.StartsWith("SP_Arch_Lantern") || t.name.StartsWith("SP_MX_Lantern")) && !t.name.Contains("Core"))
            .Where(t => { var p = t.parent; while (p && p != root) { if (p.name.StartsWith("SP_Arch_Lantern") || p.name.StartsWith("SP_MX_Lantern")) return false; p = p.parent; } return true; })
            .ToList();
        var g = Group("KoreanLanterns");
        var placed = new List<Vector2>(); var klr = new System.Random(28);
        var coreBase = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/SP_MX_LanternCore.mat");
        int moved = 0, inPlace = 0, dropped = 0;
        // tr30 (lead: ss5's composition is 2-3 big lit stone lanterns): three hero-size lanterns (2.4 m) just past the west
        // boundary beside the lantern yard, inside view 02's frame (view 02 reframed to (-48, 34) x1.3 to include that edge)
        int heroes = 0;
        foreach (var hz in new[] { 29f, 35.5f, 42f, 22.5f, 41f })   // tr31: 42 had no free spot; fall back south / a little lower
        {
            Vector2? hs = null;
            foreach (float dz in new[] { 0f, 1.5f, -1.5f, 3f, -3f })
            { var q = new Vector2(-HX - 2.2f, hz + dz); if (KLFree(q, placed, 6f)) { hs = q; break; } }
            if (heroes >= 3) break;
            if (hs == null) { Log($"KL hero lantern near z {hz}: no free spot"); continue; }
            heroes++;
            KLPut(hs.Value, 2.4f, "KoreanLanternHero");
        }
        // tr36 (lead, ss5 hero corner): two hero lanterns at the west gate / bridge corner, just past the boundary on the
        // arena side of the 솟을삼문, framed with the bridge and gate in view 20 (placed by hand, the river check is the gate's)
        foreach (var hp in HCLanterns) KLPut(hp, 2.4f, "KoreanLanternCorner");
        void KLPut(Vector2 at, float h, string nm)
        {
            float yaw = (float)klr.NextDouble() * 360f;
            var go = LMPlace("KoreanLantern", at, yaw, Quaternion.Euler(0f, -yaw, 0f) * Vector3.back, h, g, true, false);
            if (!go) return;
            go.name = nm; placed.Add(at); KLSink(go, h);
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            if (coreBase)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Sphere); c.name = "GlowCore";
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z);
                c.transform.localScale = Vector3.one * b.size.y * 0.1f;
                c.transform.SetParent(go.transform, true);
                var cr = c.GetComponent<MeshRenderer>(); cr.sharedMaterial = coreBase; cr.shadowCastingMode = ShadowCastingMode.Off; cr.receiveShadows = false;
            }
            Lantern(go, false); KLFill(go, b); KLRoofLight(go); KLPlug(go, b);
            Log($"KL {nm} at ({at.x:0.0}, {at.y:0.0}), h {b.size.y:0.00} m");
        }
        foreach (var t in olds)
        {
            var p = new Vector2(t.position.x, t.position.z);
            Object.DestroyImmediate(t.gameObject);
            bool inside = Mathf.Abs(p.x) < HX && Mathf.Abs(p.y) < HZ;
            var spot = inside ? KLEdge(p, placed) : (KLFree(p, placed) ? p : (Vector2?)null);
            if (spot == null) { dropped++; Log($"KL drop lantern at ({p.x:0.0}, {p.y:0.0}): no free spot past the boundary"); continue; }
            var at = spot.Value;
            // tr45 (QA #1): the moved lanterns stood in ruler-straight evenly spaced rows - drop ~30 %, jitter the rest
            if (klr.NextDouble() < 0.3) { dropped++; Log($"KL thin-out: drop lantern for ({p.x:0.0}, {p.y:0.0})"); continue; }
            { var jit = new Vector2((float)klr.NextDouble() * 2f - 1f, (float)klr.NextDouble() * 2f - 1f) * 0.9f; var outw = Mathf.Abs(at.x) > HX ? new Vector2(Mathf.Sign(at.x), 0) : new Vector2(0, Mathf.Sign(at.y)); var q = at + jit + outw * (float)klr.NextDouble() * 1.4f; if (KLFree(q, placed, 6f)) at = q; }
            float yaw = (float)klr.NextDouble() * 360f;
            var go = LMPlace("KoreanLantern", at, yaw, Quaternion.Euler(0f, -yaw, 0f) * Vector3.back, KLHeight, g, true, false);   // round lantern: random turn, "front" kept to the camera for the check
            if (!go) continue;
            go.name = "KoreanLantern"; KLSink(go, KLHeight);
            placed.Add(at);
            if (inside) moved++; else inPlace++;
            var b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            if (coreBase)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Sphere); c.name = "GlowCore";
                Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z);
                c.transform.localScale = Vector3.one * b.size.y * 0.1f;
                c.transform.SetParent(go.transform, true);
                var cr = c.GetComponent<MeshRenderer>(); cr.sharedMaterial = coreBase; cr.shadowCastingMode = ShadowCastingMode.Off; cr.receiveShadows = false;
            }
            Lantern(go, false); KLFill(go, b); KLRoofLight(go); KLPlug(go, b);
            Log($"KL {(inside ? "moved" : "swapped")} ({p.x:0.0}, {p.y:0.0}) -> ({at.x:0.0}, {at.y:0.0}), h {b.size.y:0.00} m");
        }
        Log($"KL swap: {olds.Count} old lanterns -> {moved} moved past the boundary, {inPlace} swapped in place, {dropped} dropped (spacing / river / landmarks)");
    }

    static bool KLFree(Vector2 p, List<Vector2> placed, float gap = KLMinGap)
    {
        if (DistanceToRiver(p.x, p.y) < 9f || LedgeH(p.x, p.y) > 0.3f) return false;   // tr30: 7.5 let the (59, 36) footprint touch the 7 m river band
        if (placed.Any(q => Vector2.Distance(p, q) < gap)) return false;
        if (lmFootprints.Any(f => Vector2.Distance(p, f.c) < f.r + 0.5f)) return false;
        if (lmBoxes.Any(o => o.name != "KoreanLantern" && new Rect(o.box.xMin - 2f, o.box.yMin - 2f, o.box.width + 4f, o.box.height + 4f).Contains(p))) return false;   // tr31: (59, 18) overlapped the thatched house
        return true;
    }

    // nearest west / east / north edge, KLOut past it; slide along the edge until the spot is free
    static Vector2? KLEdge(Vector2 p, List<Vector2> placed)
    {
        var cands = new List<(float d, Vector2 at, Vector2 along)>
        {
            (HX - Mathf.Abs(p.x), new Vector2(Mathf.Sign(p.x) * (HX + KLOut), p.y), Vector2.up),   // west or east
            (HZ - p.y, new Vector2(p.x, HZ + KLOut), Vector2.right),                                // north
        };
        foreach (var c in cands.OrderBy(c => c.d))
            for (int k = 0; k <= 12; k++)
            {
                float s = (k + 1) / 2 * 3f * (k % 2 == 0 ? 1f : -1f);
                var q = c.at + c.along * s;
                if (q.y < -HZ + 4f || Mathf.Abs(q.x) > HX + KLOut + 0.1f || q.y > HZ + KLOut + 0.1f) continue;
                if (KLFree(q, placed)) return q;
            }
        return null;
    }

    // tr33 (lead): the square pebble slab read as a hard-edged glowing square under the lantern's own pool - sink the
    // model so the slab sits in the ground snow (8 cm at 1.7 m, scaled with the lantern). Logs the scan's vertex colours once.
    static bool klColLogged;
    static void KLSink(GameObject go, float h)
    {
        float cut = KLFixMesh(go);   // tr33: winding fixed + pebble slab cut off; cut = height of the removed slab (m)
        go.transform.position += Vector3.down * (cut + 0.04f * (h / KLHeight));
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        if (klColLogged) return; klColLogged = true;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh; if (!m) continue;
            Color[] c = null; try { c = m.colors; } catch { }
            if (c == null || c.Length == 0) { Log($"KL {m.name}: NO vertex colours"); continue; }
            Color avg = Color.clear; foreach (var x in c) avg += x; avg /= c.Length;
            Log($"KL {m.name}: {c.Length} vertex colours, mean ({avg.r:0.00}, {avg.g:0.00}, {avg.b:0.00})");
        }
    }

    // tr33: the scan comes in with its triangles wound against its normals (like the Megascans lanterns, MXLoad): seen
    // from above the roof showed its dark inside - near-black lump. Also cuts the square pebble slab (every triangle wholly
    // in the lowest 8 % of the lantern) - it read as a hard glowing square under the lantern's own light. One fixed copy
    // per source mesh (Generated/SP_KL_*.asset); returns the slab height in world metres for the sink.
    static readonly Dictionary<Mesh, (Mesh fixedMesh, float cutFrac)> klMeshes = new Dictionary<Mesh, (Mesh, float)>();
    static float KLFixMesh(GameObject go)
    {
        var mfs = go.GetComponentsInChildren<MeshFilter>();
        var b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        float h = Mathf.Max(0.01f, b.size.y), cutY = b.min.y + h * 0.3f;   // tr35: low band = lowest 30 % (the pebbles reach high)
        // foot ring: widest point of the lantern between 15 and 25 % of its height (the base stone just above the slab)
        float footR = 0.05f;
        float roofR = 0.05f;   // tr35: radius-based cut - widest point of the roof (60-95 % of the height)
        foreach (var mf0 in mfs) { if (!mf0.sharedMesh) continue; var M0 = mf0.transform.localToWorldMatrix; foreach (var p0 in mf0.sharedMesh.vertices) { var w = M0.MultiplyPoint3x4(p0); if (w.y > b.min.y + h * 0.6f && w.y < b.min.y + h * 0.95f) roofR = Mathf.Max(roofR, new Vector2(w.x - b.center.x, w.z - b.center.z).magnitude); } }
        footR = roofR * 0.6f;   // tr35: slab = every low triangle farther out than 0.6 x the roof radius
        float topOfCut = b.min.y;
        foreach (var mf in mfs)
        {
            var src = mf.sharedMesh; if (!src) continue;
            if (klMeshes.TryGetValue(src, out var done) && done.fixedMesh) { mf.sharedMesh = done.fixedMesh; PrefabUtility.RecordPrefabInstancePropertyModifications(mf); continue; }
            var v = src.vertices; var n = src.normals; var t = src.triangles; var M = mf.transform.localToWorldMatrix;
            int against = 0, tris = t.Length / 3;
            for (int k = 0; k < t.Length; k += 3)
            {
                var fn = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                if (n.Length == v.Length && Vector3.Dot(fn, n[t[k]] + n[t[k + 1]] + n[t[k + 2]]) < 0) against++;
            }
            bool flip = against > tris / 2;
            var keep = new List<int>(t.Length); int cutTris = 0;
            for (int k = 0; k < t.Length; k += 3)
            {
                float y0 = M.MultiplyPoint3x4(v[t[k]]).y, y1 = M.MultiplyPoint3x4(v[t[k + 1]]).y, y2 = M.MultiplyPoint3x4(v[t[k + 2]]).y;
                var cw = (M.MultiplyPoint3x4(v[t[k]]) + M.MultiplyPoint3x4(v[t[k + 1]]) + M.MultiplyPoint3x4(v[t[k + 2]])) / 3f;
                if (cw.y < cutY && new Vector2(cw.x - b.center.x, cw.z - b.center.z).magnitude > footR) { cutTris++; continue; }   // tr34: slab = low AND outside the foot ring
                if (flip) { keep.Add(t[k]); keep.Add(t[k + 2]); keep.Add(t[k + 1]); } else { keep.Add(t[k]); keep.Add(t[k + 1]); keep.Add(t[k + 2]); }
            }
            var m = Object.Instantiate(src); m.name = "SP_KL_" + src.name;
            m.indexFormat = v.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.subMeshCount = 1; m.SetTriangles(keep, 0); m.RecalculateBounds();
            var path = $"{Gen}/SP_KL_{klMeshes.Count}.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path);
            klMeshes[src] = (m, 0f); mf.sharedMesh = m; PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
            Log($"KL mesh {src.name}: {against}/{tris} faces against normals{(flip ? " (flipped)" : "")}, {cutTris} slab triangles cut (cut r {footR:0.00} m, roof r {roofR:0.00} m, lantern half-width {Mathf.Max(b.extents.x, b.extents.z):0.00} m, cut below {cutY - b.min.y:0.00} m)");
        }
        // the lowest point left after the cut, for the sink
        float lo = float.MaxValue;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) lo = Mathf.Min(lo, r.bounds.min.y);
        return Mathf.Max(0f, lo - b.min.y);
    }

    // tr34 (lead approved): weak unshadowed warm fill on the lantern body itself (the spot points down under the roof, the
    // body stayed near-black from the game camera) - ~1.5 m range, low intensity, on the camera side of the lantern
    static void KLFill(GameObject go, Bounds b)
    {
        var f = new GameObject("LanternBodyFill"); f.transform.SetParent(go.transform, true);
        f.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.8f, b.center.z - b.extents.z - 0.35f);
        var l = f.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.8f, 0.58f); l.intensity = 0.7f; l.range = 1.5f * (b.size.y / KLHeight);
        l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime; l.renderMode = LightRenderMode.Auto;
    }

    // tr35 (lead): the roof stayed near-black under the canopy cookie - a warm unshadowed point light just above the roof
    // (intensity 2, ~2.5 m range at 1.7 m, scaled), placed from the renderer bounds after the slab cut
    static void KLRoofLight(GameObject go)
    {
        var b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (r.name == "GlowCore") continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        var f = new GameObject("LanternRoofLight"); f.transform.SetParent(go.transform, true);
        f.transform.position = new Vector3(b.center.x, b.max.y + 0.45f * (b.size.y / KLHeight), b.center.z - 0.2f);
        var l = f.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.84f, 0.64f); l.intensity = 1.2f;   /* tr36: 2 -> 1.2 */ l.range = 2.5f * (b.size.y / KLHeight);
        l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
    }

    // tr36 hero corner (ss5 layout): bridge + 솟을삼문 top-left, two lanterns mid, a corpse vignette with straw mats and
    // blood to the right just inside the edge (all low, < 0.4 m: the arena rule allows flat props inside)
    public static readonly Vector2[] HCLanterns = { new Vector2(-57.4f, -33.8f), new Vector2(-57.2f, -40.4f), new Vector2(-52.5f, -46.8f) };   // tr37: gate end, west edge, south edge
    public static readonly Vector3 HCView = new Vector3(-56f, 1.8f, -36f);   /* tr38 */   // (focus x, zoom, focus z) of view 20 - tr37: wider (x1.4 was all gate roof)
    static void HeroCornerVignette()
    {
        var g = Group("HeroCorner"); hcDarkBark = null;
        int made = 0;
        void P(string m, float x, float z, float yaw)
        {
            var go = Place(m, x, z, yaw, 1f, g, true);
            if (!go) { Log("HC missing " + m); return; }
            var b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            float top = b.max.y - Height(x, z);
            if (InArena(x, z) && top > 0.4f) Log($"HC CHECK {m} at ({x:0.0}, {z:0.0}) stands {top:0.00} m inside the arena (> 0.4)");
            made++;
        }
        void Blood(float x, float z, float yaw, float size, int cell)
        {
            if (!mats.TryGetValue("SP_Blood", out var bm) || !bm) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Blood";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(new Vector3(x, Height(x, z), z), Quaternion.Euler(0, yaw, 0));
            size *= 1.9f; go.transform.localScale = new Vector3(size, 1.2f, size * (cell == 2 ? 1.4f : 1f));
            var mc = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/SP_Blood_{cell}.mat");
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mc ? mc : bm; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            made++;
        }
        // corpses under straw mats (거적) just inside the west edge, south of the bridge end; rags, drag of blood toward the gate
        // tr37: south of the gate band so the frame reads top to bottom bridge > gate > lanterns > corpses (ss5)
        P("SP_StrawMat_Two", -50.2f, -40.6f, 80f); P("SP_StrawMat_A", -47.4f, -38.6f, 115f); P("SP_StrawMat_Pulled", -51.8f, -38.0f, 35f);
        P("SP_Rags_A", -48.6f, -40.9f, 10f); P("SP_Rags_B", -52.6f, -39.8f, 200f); P("SP_Bone", -46.6f, -40.2f, 40f);
        Blood(-49.6f, -39.8f, 25f, 3.4f, 1); Blood(-47.2f, -39.4f, 70f, 2.4f, 3); Blood(-52.4f, -38.8f, 100f, 2.0f, 2);   // tr37: no cell 0 (pale centre); tr38: bigger (unreadable at view 20)
        // tr45 (look worker): the two extra pools dropped
        P("SP_StrawMat_B", -45.2f, -40.8f, 150f); P("SP_StrawMat_A", -53.2f, -41.4f, 20f); P("SP_Rags_A", -46.2f, -36.8f, 250f); P("SP_Bone", -51.0f, -42.0f, 300f);
        // tr38: clear the tall dark masses / trees standing between the camera and the corner (they hid the lanterns in 20)
        int cleared = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var c = r.bounds.center; string grp = r.transform.parent ? r.transform.parent.name : "";
            if (c.x < -66f || c.x > -46f || c.z < -50f || c.z > -38f || InArena(c.x, c.z) || r.bounds.size.y < 2f) continue;
            for (var t = r.transform; t && t != root; t = t.parent) grp = t.name;
            if (grp != "VegetationFrame" && grp != "Border" && grp != "Foreground" && grp != "MegascansProps") continue;
            r.enabled = false; cleared++;
        }
        Log($"HC cleared {cleared} tall renderers in front of the corner");
        // tr39 (lead): ss5's left side - a big dark dead tree whose crown hangs over the left edge of view 20, outside the arena
        {
            var fp = EdgeCrown(new Vector3(HCView.x, 0f, HCView.z), HCView.y, 0.07f, 0.3f, 10f);   // tr41: lower-left (z ~ -48), out of the gate's moon shadow
            if (InArena(fp.x, fp.y, -1f)) fp.x = -HX - 3f;
            var tg = Place("SK:Con_Coniferous_Dead_Tree_6", fp.x, fp.y, 35f, 1.3f, g, false, 0.15f);   // tr42: Dead_Object_* are short snags - a tall dead conifer (bark darkened by VgPost32)
            if (tg) { foreach (var r in tg.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.sharedMaterials = r.sharedMaterials.Select(_ => HCDarkBark()).ToArray(); }   /* tr40: normal bark; tr42: VgPost32 gives it the darkened dead-conifer bark */ Log($"HC framing dead tree at ({fp.x:0.0}, {fp.y:0.0})"); made++; }
            else Log("HC framing dead tree: model missing");
            // tr43: a second dead conifer higher on the left edge for framing weight (ss5's left side)
            var fp2 = EdgeCrown(new Vector3(HCView.x, 0f, HCView.z), HCView.y, 0.03f, 0.58f, 11f);
            if (InArena(fp2.x, fp2.y, -1f)) fp2.x = -HX - 3f;
            var tg2 = Place("SK:Con_Coniferous_Dead_Tree_2_7", fp2.x, fp2.y, 200f, 1.25f, g, false, 0.15f);
            if (tg2) { foreach (var r in tg2.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.Off; r.sharedMaterials = r.sharedMaterials.Select(_ => HCDarkBark()).ToArray(); } Log($"HC framing dead tree 2 at ({fp2.x:0.0}, {fp2.y:0.0})"); made++; }
        }
        // low dark shrubs / thicket round the lantern bases (outside the arena only) to break the clean snow
        var hr = new System.Random(39);
        foreach (var lp in HCLanterns)
            for (int i = 0; i < 4; i++)
            {
                float a = (float)hr.NextDouble() * 6.283f, d = 1.3f + (float)hr.NextDouble() * 1.4f;
                float x = lp.x + Mathf.Cos(a) * d, z = lp.y + Mathf.Sin(a) * d;
                if (InArena(x, z, -0.5f)) continue;
                string m = new[] { "SP_V_Thicket_A", "SP_V_Brush_A", "SP_V_Brush_B", "SP_V_Dome_A" }[hr.Next(4)];
                var sg = Place(VgModels + m, x, z, (float)hr.NextDouble() * 360f, 1.3f + (float)hr.NextDouble() * 1.0f, g, false, 0.05f);   // tr40: x1.6
                if (sg) made++;
            }
        Log($"HC hero corner: {made} props (corpse vignette inside the west edge), lanterns {HCLanterns.Length} outside");
    }

    // tr41: covered-body models (CC BY: "Low Poly Dead Body Covered" by -Arthur-, "Corpse" by Tamal De Quezo) in place of
    // every flat straw-mat lump (SP_StrawMat_*: hero corner, west and south corpse spots). Recoloured as 거적: straw albedo,
    // the model's own normal map for the body shape, dull straw-brown, wet dark edge into the ground, a little snow dusting.
    // All < 0.4 m, so they may lie just inside the arena edge.
    static readonly string[] CorpseModels = { "CorpseCovered", "CorpseCloth" };
    static Material CorpseMat(string folder)
    {
        string dir = LMDir + folder + "/textures";
        Texture2D nrm = null, alb = null;
        if (System.IO.Directory.Exists(ProjAbsVg(dir)))
        {
            foreach (var p in System.IO.Directory.GetFiles(ProjAbsVg(dir), "*_normal.png")) nrm = Tex(dir + "/" + System.IO.Path.GetFileName(p), true, false, false, 1024);
            foreach (var p in System.IO.Directory.GetFiles(ProjAbsVg(dir), "*_baseColor.png")) alb = Tex(dir + "/" + System.IO.Path.GetFileName(p), false, false, false, 1024);
        }
        // tr44: the models' own cloth texture (blood stains included) tinted toward dull straw / rag brown. tr41-43 read as grey
        // blobs: at snow amount 0.95 even bias -0.8 left patchy snow on every up face, and the ground-melt band greyed the rest
        var m = Prop("SP_LM_" + folder, alb, nrm, Hex("#6b5638"), 0.06f, 0f, false);   // tr45: the lighter tint still read grey-blue under the grade - warmer, darker, no desaturation
        if (m) { m.SetFloat("_VertexFrost", 0f); m.SetFloat("_SnowBias", -3f); m.SetFloat("_SnowCap", 0f); m.SetFloat("_GroundMelt", 0f); m.SetFloat("_Wetness", 0.15f); m.SetFloat("_NormalStrength", 1.1f); }
        Log($"CORPSE mat {m?.name}: albedo {(alb ? alb.name : "NONE")}, normal {(nrm ? nrm.name : "none")}, tint {m?.GetColor("_Tint")}");
        return m;
    }
    static void CorpseSwap()
    {
        var mats0 = new Dictionary<string, Material>();
        foreach (var c in CorpseModels) if (LMPath(c) != null) mats0[c] = CorpseMat(c);
        if (mats0.Count == 0) { Log("CORPSE no models"); return; }
        var olds = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("SP_StrawMat") && t.parent && !t.parent.name.StartsWith("SP_StrawMat")).ToList();
        var rnd = new System.Random(41); int n = 0;
        foreach (var t in olds)
        {
            var at = t.position; float yaw = t.eulerAngles.y + (float)rnd.NextDouble() * 30f - 15f; var parent = t.parent;
            string folder = CorpseModels[n % CorpseModels.Length];
            if (!mats0.TryGetValue(folder, out var cm)) folder = mats0.Keys.First();
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(LMPath(folder)); if (!src) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
            var rot0 = src.transform.localRotation;
            // lying flat: if the model stands on end in its own axes, tip it over so its longest side is horizontal
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * rot0; go.transform.localScale = src.transform.localScale * (0.9f + (float)rnd.NextDouble() * 0.25f);
            var b = new Bounds(); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            if (b.size.y > Mathf.Max(b.size.x, b.size.z)) { go.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(-90f, 0, 0) * rot0; first = true; foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); } }
            float gy = Height(at.x, at.z);
            go.transform.position += new Vector3(at.x - b.center.x, gy - 0.03f - b.min.y, at.z - b.center.z);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.sharedMaterials = r.sharedMaterials.Select(_ => mats0[folder]).ToArray(); r.shadowCastingMode = ShadowCastingMode.On; }
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            float top = b.size.y - 0.03f;
            Log($"CORPSE {folder} for {t.name} at ({at.x:0.0}, {at.z:0.0}) in {parent.name}: {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} m{(InArena(at.x, at.z) && top > 0.4f ? " CHECK: > 0.4 m inside the arena" : "")}");
            Object.DestroyImmediate(t.gameObject); n++;
        }
        Log($"CORPSE swap: {n} straw-mat lumps -> covered bodies");
        // tr44: the flat grey rectangles beside the bodies - log every flat non-body prop within 3 m of a body; rags get the
        // cloth body material (brown rag texture) instead of the plain grey SP_Cloth
        var bodies = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("SKF_Corpse")).Select(t => new Vector2(t.position.x, t.position.z)).ToList();
        var rag = mats0.TryGetValue("CorpseCloth", out var rc) ? rc : mats0.Values.First(); int ragged = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            var c = new Vector2(r.bounds.center.x, r.bounds.center.z); bool near = bodies.Any(p => Vector2.Distance(p, c) < 3f);
            bool isBody = false; for (var t = r.transform; t && t != root; t = t.parent) if (t.name.StartsWith("SKF_Corpse")) { isBody = true; break; }
            if (!near || isBody || r.bounds.size.y > 0.5f || r.name == "Ground" || r.name.StartsWith("Blood") || r.name.StartsWith("FogSheet")) continue;
            bool isRag = false; for (var t = r.transform; t && t != root; t = t.parent) if (t.name.StartsWith("SP_Rags")) { isRag = true; break; }
            if (isRag) { r.sharedMaterials = r.sharedMaterials.Select(_ => rag).ToArray(); ragged++; }
            if (r.bounds.size.x > 0.4f || r.bounds.size.z > 0.4f) Log($"CORPSE near-body flat prop: {r.name} ({r.transform.parent?.name}) mat {(r.sharedMaterial ? r.sharedMaterial.name : "-")} size {r.bounds.size}{(isRag ? " -> rag texture" : "")}");
        }
        Log($"CORPSE rags retextured: {ragged}");
    }

    // tr44 (Kido: "1자돌 이상해" - the straight, evenly spaced stone-wall rows along the boundary looked fake): irregular rock
    // clusters instead - 2-5 boulders of mixed size per cluster, half sunk into the snow, uneven gaps, just outside all four
    // edges (never on the river, the ledge, a landmark or a lantern)
    static readonly string[] BorderRockModels = { "SK:Con_BoulderPlainLarge_13", "SK:Con_BoulderMossyLarge_8", "SK:Con_BoulderOutcropPlainMedium_12", "SK:Con_BoulderOutcropMossyMedium_10" };
    static void BorderRocks()
    {
        var g = Group("BorderRocks");
        var rr = new System.Random(44);
        float Rr(float a, float b) => a + (float)rr.NextDouble() * (b - a);
        var lanterns = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("KoreanLantern")).Select(t => new Vector2(t.position.x, t.position.z)).ToList();
        bool Free(float x, float z) => !InArena(x, z, -0.6f) && DistanceToRiver(x, z) > 8f && LedgeH(x, z) < 0.3f
            && !lmFootprints.Any(fp => Vector2.Distance(new Vector2(x, z), fp.c) < fp.r + 1f) && !lanterns.Any(l => Vector2.Distance(l, new Vector2(x, z)) < 3.5f);
        int clusters = 0, rocks = 0;
        void Cluster(float cx, float cz)
        {
            if (!Free(cx, cz)) return;
            int n = rr.Next(2, 6); int put = 0;
            for (int i = 0; i < n; i++)
            {
                float a = Rr(0, 6.283f), d = i == 0 ? 0f : Rr(0.8f, 2.6f), x = cx + Mathf.Cos(a) * d, z = cz + Mathf.Sin(a) * d;
                if (!Free(x, z)) continue;
                var go = Place(BorderRockModels[rr.Next(BorderRockModels.Length)], x, z, Rr(0, 360), 1f, g, true, 0f);
                if (!go) continue;
                var b = new Bounds(); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
                float want = i == 0 ? Rr(0.8f, 1.4f) : Rr(0.35f, 0.9f);   // one bigger stone, smaller ones round it
                float k = want / Mathf.Max(0.05f, b.size.y);
                go.transform.localScale *= k;
                go.transform.position -= Vector3.up * want * Rr(0.5f, 0.65f);   // tr45: sunk deeper (the bubbly white blobs in 04)
                put++; rocks++;
            }
            if (put > 0) clusters++;
        }
        // walk each edge with uneven gaps (6-20 m), clusters 2-6 m outside the line
        for (float x = -HX + Rr(0, 6); x < HX; x += Rr(6f, 20f)) { Cluster(x, HZ + Rr(2f, 6f)); }
        for (float x = -HX + Rr(0, 6); x < HX; x += Rr(7f, 22f)) { Cluster(x, -HZ - Rr(2f, 5f)); }
        for (float z = -HZ + Rr(0, 6); z < HZ; z += Rr(6f, 20f)) { Cluster(-HX - Rr(2f, 6f), z); Cluster(HX + Rr(2f, 6f), z + Rr(-3f, 3f)); }
        // tr45: the boulders read as white bubbly blobs (04) - dark rock, snow only as a thin top cap
        foreach (var bn in new[] { "SP_SK_BoulderMossy", "SP_SK_BoulderPlain" })
            if (mats.TryGetValue(bn, out var bm) && bm) { bm.SetColor("_Tint", new Color(0.42f, 0.41f, 0.4f)); bm.SetFloat("_SnowBias", -0.7f); bm.SetFloat("_SnowCap", 0.22f); bm.SetFloat("_VertexFrost", 0f); bm.SetFloat("_Desaturate", 0.4f); EditorUtility.SetDirty(bm); }
        Log($"BORDER rocks: {clusters} irregular clusters, {rocks} boulders (straight wall runs removed)");
    }

    // tr45 (QA #2): the decimated scan has black jagged holes round the firebox base - a short stone core plugs them from
    // inside (same granite material), just under the glow core
    static void KLPlug(GameObject go, Bounds b)
    {
        var mr = go.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name != "GlowCore"); if (!mr) return;
        var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder); c.name = "FireboxPlug";
        Object.DestroyImmediate(c.GetComponent<Collider>());
        float h = b.size.y;
        c.transform.position = new Vector3(b.center.x, b.min.y + h * 0.5f, b.center.z);
        c.transform.localScale = new Vector3(h * 0.17f, h * 0.05f, h * 0.17f);   // cylinder primitive: 2 units tall
        c.transform.SetParent(go.transform, true);
        var r = c.GetComponent<MeshRenderer>(); r.sharedMaterial = mr.sharedMaterial; r.shadowCastingMode = ShadowCastingMode.On;
    }

    // tr45: the view-20 framing trunks read as long pale sticks - a dark dead bark of their own (low albedo, no snow, no rim)
    static Material hcDarkBark;
    static Material HCDarkBark()
    {
        if (hcDarkBark) return hcDarkBark;
        var src = mats.TryGetValue("SP_SK_Bark", out var b) && b ? b : null;
        hcDarkBark = Veg("SP_HC_DarkBark", src ? src.GetTexture("_BaseMap") as Texture2D : null, src ? src.GetTexture("_BumpMap") as Texture2D : null, Hex("#2b2622"), false, 0f, 0f);
        if (hcDarkBark) { hcDarkBark.SetFloat("_SnowClump", 0f); hcDarkBark.SetFloat("_SnowCover", 0f); hcDarkBark.SetFloat("_SnowBias", -3f); hcDarkBark.SetFloat("_RimScale", 0f); hcDarkBark.SetFloat("_AOStrength", 0f); hcDarkBark.SetFloat("_GroundMelt", 0f); }
        return hcDarkBark;
    }
}
