// Round 13 (11-13, Kido: "피는 어색하고, 건물들이랑 받았자나 그런것도 넣고, 얼어붙은강에 다리도 놔봐 지형에 맞게 또 위아래
// 일자로 놓지 말고"). One pass after the other builders (R13Pass, called before ArenaCheck), kept in its own file so the
// earlier passes stay untouched:
//  1. bridges: the two Megascans arched bridges (outside the arena, where the river leaves it) are turned ~38 deg off
//     square to the river so they cross on a diagonal on screen instead of straight up/down; the deck is stretched to
//     land on both banks, tilted to the bank heights (raycast on the ground mesh) and gets a stone abutment under each end
//  2. blood: the old decals (big round crimson pools centred under the bodies, embers, red glow) are replaced by a few
//     darker, desaturated soaks beside some of the bodies, small satellite spatters and drag marks leading away
//  3. buildings: KCISA 초가집02 and 사랑채 placed at the edges, outside the arena, never on the river or the ledge,
//     fronts to the camera, roof snow (LMMat), auto layout checks (LMPlace) plus a pre-check that picks the first free spot
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const float R13Skew = 38f;   // degrees off square to the river
    static readonly List<(Vector2 c, Vector2 u, float halfL, float halfW)> r13Decks = new List<(Vector2, Vector2, float, float)>();

    static void R13Pass()
    {
        r13Decks.Clear();
        try { R13Bridges(); } catch (System.Exception e) { Log("R13 bridges EXCEPTION " + e); }
        try { R13Blood(); } catch (System.Exception e) { Log("R13 blood EXCEPTION " + e); }
        try { R13Buildings(); } catch (System.Exception e) { Log("R13 buildings EXCEPTION " + e); }
    }

    // ground height from the ground mesh collider (carved river banks, drifts); falls back to Height()
    static MeshCollider r13Ground;
    static float R13GroundY(float x, float z)
    {
        if (!r13Ground) { var g = root.Find("Ground"); r13Ground = g ? g.GetComponent<MeshCollider>() : null; }
        if (r13Ground && r13Ground.Raycast(new Ray(new Vector3(x, 60f, z), Vector3.down), out var hit, 200f)) return hit.point.y;
        return Height(x, z);
    }

    // nearest river centreline point, unit direction and arc length
    static void R13RiverAt(Vector2 p, out Vector2 c, out Vector2 dir, out float s)
    {
        var sm = RiverSamples(0.5f);
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < sm.Count; i++) { float d = (sm[i] - p).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
        c = sm[best];
        var a = sm[Mathf.Max(best - 2, 0)]; var b = sm[Mathf.Min(best + 2, sm.Count - 1)];
        dir = (b - a).normalized; s = best * 0.5f;
    }

    static bool R13InDeck(Vector2 p, float pad)
    {
        foreach (var d in r13Decks)
        {
            var q = p - d.c; var n = new Vector2(-d.u.y, d.u.x);
            if (Mathf.Abs(Vector2.Dot(q, d.u)) < d.halfL + pad && Mathf.Abs(Vector2.Dot(q, n)) < d.halfW + pad) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ 1. bridges
    static void R13Bridges()
    {
        var bridges = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SP_MX_Bridge").ToList();
        if (bridges.Count == 0) { Log("R13 bridge: no SP_MX_Bridge in the scene"); return; }
        // stone for the abutments: the Megascans stone wall pieces (else any rock pile mesh)
        MeshFilter stone = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "SP_MX_StoneWall" && m.sharedMesh)
                           ?? root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name.StartsWith("SP_MX_Snowy") && m.sharedMesh);
        Physics.SyncTransforms();
        foreach (var br in bridges)
        {
            var mf = br.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
            var size = mf.sharedMesh.bounds.size;
            var p0 = new Vector2(br.position.x, br.position.z);
            float halfW = size.z * br.localScale.z * 0.5f;
            bool placed = false;
            // try the skew sign that keeps both ends outside the arena; slide along the river if needed
            foreach (float slide in new[] { 0f, -3f, 3f, -6f, 6f, -9f, 9f })
            {
                R13RiverAt(p0, out var c0, out var d0, out _);
                R13RiverAt(c0 + d0 * slide, out var c, out var d, out var s);
                var n = new Vector2(-d.y, d.x);
                float hw = RiverHalfWidth(s, c.x);
                float halfL = (hw + 2.2f) / Mathf.Cos(R13Skew * Mathf.Deg2Rad);   // ice + ~2 m onto each bank
                // prefer the sign whose south end is nearer the west gate (west bridge leads to the 솟을삼문/small gate)
                var signs = new[] { 1f, -1f }.OrderBy(sg =>
                {
                    var u0 = (Vector2)(Quaternion.Euler(0, 0, sg * R13Skew) * (Vector3)n);
                    var a0 = c - u0 * halfL; var b0 = c + u0 * halfL; var south = a0.y < b0.y ? a0 : b0;
                    return c.x < 0 ? Vector2.Distance(south, LMGatePos) : -south.x;
                }).ToArray();
                foreach (float sg in signs)
                {
                    var u = (Vector2)(Quaternion.Euler(0, 0, sg * R13Skew) * (Vector3)n);
                    var A = c - u * halfL; var B = c + u * halfL;
                    var nn = new Vector2(-u.y, u.x);
                    bool ok = true;
                    foreach (var e in new[] { A, B })
                        foreach (float k in new[] { -1f, 1f })
                        {
                            var q = e + nn * halfW * k;
                            if (InArena(q.x, q.y, -1.0f)) ok = false;                        // 1 m clear of the boundary
                            if (Vector2.Distance(q, LMGatePos) < 4.5f) ok = false;          // not into the gate
                        }
                    if (!ok) continue;
                    float hA = R13GroundY(A.x, A.y), hB = R13GroundY(B.x, B.y);
                    float L = halfL * 2f;
                    float yaw = Mathf.Atan2(-u.y, u.x) * Mathf.Rad2Deg;   // local +X along u
                    float tilt = Mathf.Atan2(hB - hA, L) * Mathf.Rad2Deg;
                    br.SetPositionAndRotation(new Vector3(c.x, (hA + hB) * 0.5f - 0.12f, c.y), Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, tilt));
                    br.localScale = new Vector3(L / size.x, br.localScale.y, br.localScale.z);
                    r13Decks.Add((c, u, halfL, halfW));
                    // abutments: a stone block under each end, across the deck, sunk into the bank
                    if (stone)
                    {
                        var ss = stone.sharedMesh.bounds.size; var smr = stone.GetComponent<MeshRenderer>();
                        foreach (var (e, h, sgn) in new[] { (A, hA, -1f), (B, hB, 1f) })
                        {
                            var ab = new GameObject("SP_R13_Abutment");
                            ab.transform.SetParent(br.parent, false);
                            var at = e + u * sgn * 0.4f;   // a little out onto the bank
                            ab.transform.SetPositionAndRotation(new Vector3(at.x, R13GroundY(at.x, at.y) - 0.55f, at.y), Quaternion.Euler(0, yaw + 90f, 0));
                            ab.transform.localScale = new Vector3((halfW * 2f + 1.4f) / Mathf.Max(0.1f, ss.x), 1.0f / Mathf.Max(0.1f, ss.y), 1.6f / Mathf.Max(0.1f, ss.z));
                            ab.AddComponent<MeshFilter>().sharedMesh = stone.sharedMesh;
                            var r = ab.AddComponent<MeshRenderer>(); r.sharedMaterial = smr ? smr.sharedMaterial : null; r.shadowCastingMode = ShadowCastingMode.On;
                            GameObjectUtility.SetStaticEditorFlags(ab, StaticEditorFlags.BatchingStatic);
                        }
                    }
                    Log($"R13 bridge at ({c.x:0.0}, {c.y:0.0}) skew {sg * R13Skew:0} deg, span {L:0.0} m (river half width {hw:0.0}), ends ({A.x:0.0}, {A.y:0.0}) h {hA:0.00} / ({B.x:0.0}, {B.y:0.0}) h {hB:0.00}, tilt {tilt:0.0} deg, yaw {yaw:0}");
                    placed = true; break;
                }
                if (placed) break;
            }
            if (!placed) Log($"R13 bridge CHECK FAIL: no skewed placement outside the arena near ({p0.x:0.0}, {p0.y:0.0}) - left as it was");
        }
        // clear tall clutter standing on the decks (trees, shrubs, props)
        int cleared = 0;
        var keepGroups = new HashSet<string> { "Ground", "FrozenRiver", "Ice", "Landmarks", "FogVfx", "ArenaWalls" };
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r || !r.enabled || r is ParticleSystemRenderer) continue;
            if (r.name == "SP_MX_Bridge" || r.name == "SP_R13_Abutment" || r.name.StartsWith("SP_MX_BridgeRail") || r.transform.parent && r.transform.parent.name == "SP_MX_Bridge") continue;
            var t = r.transform; while (t.parent && t.parent != root) t = t.parent;
            if (keepGroups.Contains(t.name)) continue;
            var cc = new Vector2(r.bounds.center.x, r.bounds.center.z);
            if (R13InDeck(cc, 0.8f) && r.bounds.size.y > 0.25f) { r.enabled = false; cleared++; }
        }
        foreach (var sc in root.GetComponentsInChildren<SnowPlague.SPScatter>(true))
        {
            bool dirty = false;
            foreach (var s in sc.sets)
                for (int i = s.at.Count - 1; i >= 0; i--)
                    if (R13InDeck(new Vector2(s.at[i].x, s.at[i].z), 0.6f)) { s.at.RemoveAt(i); s.scale.RemoveAt(i); dirty = true; cleared++; }
            if (dirty) sc.Push();
        }
        Log($"R13 bridges: {r13Decks.Count} placed, {cleared} items cleared off the decks");
    }

    // ------------------------------------------------------------------ 2. blood
    static Material R13BloodMat(string name, int cell, System.Action<Material> set)
    {
        if (!mats.TryGetValue("SP_Blood", out var b) || !b) { Log("R13 blood: no SP_Blood"); return null; }
        var path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(b); AssetDatabase.CreateAsset(m, path); }
        m.shader = b.shader; m.CopyPropertiesFromMaterial(b);
        m.SetFloat("_Cell", cell);
        // soaked into snow: pinkish-grey diffusion at the rim, dark desaturated body, near-black wet clots (TARAE ss5 kill)
        m.SetColor("_SoakMul", new Color(0.88f, 0.74f, 0.75f)); m.SetFloat("_Soak", 1f);
        m.SetColor("_FilmMul", new Color(0.46f, 0.25f, 0.25f));   // r13b: less red at the soaked body
        m.SetColor("_StainMul", new Color(0.16f, 0.055f, 0.05f));
        m.SetColor("_PoolCol", new Color(0.07f, 0.016f, 0.015f)); m.SetFloat("_Pool", 0.7f);
        m.SetFloat("_Spec", 0.8f); m.SetFloat("_SpecPow", 360f); m.SetFloat("_Glint", 0.25f); m.SetFloat("_Sheen", 0.1f);
        m.SetFloat("_Emit", 0.015f); m.SetFloat("_MaxRise", 0.05f); m.SetFloat("_Grain", 0.45f); m.SetFloat("_Drops", 0f);
        set?.Invoke(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void R13Blood()
    {
        // drop the old pools and their embers / glow
        int removed = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t && (t.name == "Blood" || t.name == "BloodEmbers" || t.name == "BloodGlow")).ToList())
        { if (t) { Object.DestroyImmediate(t.gameObject); removed++; } }
        var pool1 = R13BloodMat("SP_Blood_R13_1", 1, null);
        var pool3 = R13BloodMat("SP_Blood_R13_3", 3, null);
        var drag = R13BloodMat("SP_Blood_R13_Drag", 1, m =>
        {   // smeared: thin film, lighter, almost no wet clots
            m.SetColor("_FilmMul", new Color(0.6f, 0.44f, 0.44f)); m.SetColor("_StainMul", new Color(0.36f, 0.2f, 0.2f));
            m.SetFloat("_Pool", 0.12f); m.SetFloat("_Grain", 0.8f);
        });
        if (!pool1 || !drag) return;
        var g = root.Find("BloodR13") ?? Group("BloodR13");
        var rnd = new System.Random(1313);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        int made = 0;
        void Decal(string nm, Material m, Vector2 p, float yaw, float sx, float sz)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = nm;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(g, false);
            go.transform.SetPositionAndRotation(new Vector3(p.x, R13GroundY(p.x, p.y), p.y), Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(sx, 1.2f, sz);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            made++;
        }
        // bodies (covered-body models from CorpseSwap): position and long axis
        var bodies = root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name.StartsWith("SKF_Corpse") && !(t.parent && t.parent.name.StartsWith("SKF_Corpse")))
            .Select(t =>
            {
                var rs = t.GetComponentsInChildren<Renderer>(); var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.one);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var ax = b.size.x > b.size.z ? Vector2.right : Vector2.up;
                return (p: new Vector2(b.center.x, b.center.z), axis: ax, len: Mathf.Max(b.size.x, b.size.z), wid: Mathf.Min(b.size.x, b.size.z));
            }).ToList();
        if (bodies.Count == 0) { Log("R13 blood: no SKF_Corpse bodies"); return; }
        // group into sites
        var sites = new List<List<int>>();
        for (int i = 0; i < bodies.Count; i++)
        {
            var s = sites.FirstOrDefault(l => l.Any(j => Vector2.Distance(bodies[j].p, bodies[i].p) < 5.5f));
            if (s != null) s.Add(i); else sites.Add(new List<int> { i });
        }
        foreach (var site in sites)
        {
            var centre = site.Aggregate(Vector2.zero, (a, i) => a + bodies[i].p) / site.Count;
            // not every body bleeds: one per site, two at the bigger sites
            int bleeders = site.Count >= 3 ? 2 : 1;
            foreach (int i in site.OrderBy(_ => rnd.Next()).Take(bleeders))
            {
                var bd = bodies[i];
                var side = new Vector2(-bd.axis.y, bd.axis.x) * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                // main soak beside the body (not under it), uneven oval
                var pp = bd.p + side * (bd.wid * 0.5f + R(0.35f, 0.7f)) + bd.axis * R(-0.4f, 0.4f) * bd.len * 0.5f;
                float big = R(3.4f, 4.4f);   // r13b: the visible stain is ~45 % of the box
                Decal("BloodR13_Pool", rnd.NextDouble() < 0.5 ? pool1 : (pool3 ?? pool1), pp, R(0, 360), big, big * R(0.6f, 0.85f));
                // a small satellite spatter now and then
                if (rnd.NextDouble() < 0.6)
                {
                    var sp = pp + (Vector2)(Quaternion.Euler(0, 0, R(0, 360)) * Vector3.right) * R(1.0f, 1.7f);
                    float sm = R(1.1f, 1.6f);
                    Decal("BloodR13_Spat", pool3 ?? pool1, sp, R(0, 360), sm, sm * R(0.6f, 1f));
                }
                // drag mark: from the body outward (away from the site centre, bent a little), 2-3 overlapping smears
                var away = (bd.p - centre).sqrMagnitude > 0.2f ? (bd.p - centre).normalized : -side;
                away = (Vector2)(Quaternion.Euler(0, 0, R(-25f, 25f)) * (Vector3)away);
                float len = R(2.6f, 4.2f); int pieces = len > 3.4f ? 3 : 2;
                var start = bd.p + away * (bd.len * 0.45f + 0.3f);
                float bend = R(-12f, 12f);
                for (int k = 0; k < pieces; k++)
                {
                    float t0 = (k + 0.5f) / pieces;
                    var dk = (Vector2)(Quaternion.Euler(0, 0, bend * t0) * (Vector3)away);
                    var pc = start + dk * len * t0;
                    float yawk = Mathf.Atan2(dk.x, dk.y) * Mathf.Rad2Deg;   // local Z along the drag
                    Decal("BloodR13_Drag", drag, pc, yawk, R(1.0f, 1.3f) * (1f - 0.3f * t0), len / pieces * 2.0f);   // r13b: 0.5 m wide read as thin red brush strokes
                }
            }
        }
        Log($"R13 blood: removed {removed} old decals/particles, {made} new decals at {sites.Count} body sites ({bodies.Count} bodies)");
    }

    // ------------------------------------------------------------------ 3. buildings
    static void R13Buildings()
    {
        var g = root.Find("Landmarks") ?? Group("Landmarks");
        // (folder, target height (0 = model size), local front (model root space), candidate spots, rename)
        var plan = new (string folder, float h, Vector3 front, Vector2[] at)[]
        {
            ("Sarangchae", 6.6f, Vector3.left, new[] { new Vector2(HX + 13f, HZ + 2f), new Vector2(HX + 14f, -26f), new Vector2(-HX - 14f, 2f) }),
            ("ThatchHouse2", 4.6f, Vector3.back, new[] { new Vector2(HX + 12f, -17f), new Vector2(HX + 12f, -30f), new Vector2(-HX - 13f, 6f), new Vector2(46f, HZ + 5.2f) }),
        };
        foreach (var p in plan)
        {
            var path = LMPath(p.folder); if (path == null) { Log("R13 building: missing " + p.folder); continue; }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!src) { Log("R13 building: no model " + path); continue; }
            var d = LMMeasure(p.folder, src);
            float k = p.h > 0 ? p.h / Mathf.Max(0.1f, d.h) : 1f;
            float yaw = Vector3.SignedAngle(new Vector3(p.front.x, 0, p.front.z), Vector3.back, Vector3.up);
            bool swap = Mathf.Abs(Mathf.Abs(yaw) - 90f) < 1f;
            float fx = (swap ? d.size.z : d.size.x) * k * 0.5f, fz = (swap ? d.size.x : d.size.z) * k * 0.5f;
            Vector2? pick = null; string why = "";
            foreach (var c in p.at)
            {
                var box = new Rect(c.x - fx, c.y - fz, fx * 2, fz * 2);
                if (box.Overlaps(new Rect(-HX - 1f, -HZ - 1f, 2 * HX + 2f, 2 * HZ + 2f))) { why += $" ({c.x:0},{c.y:0}) arena;"; continue; }
                if (box.yMax > 53f + 1f) { why += $" ({c.x:0},{c.y:0}) cliff;"; continue; }
                int river = 0, ledge = 0, deck = 0;
                for (float sx = box.xMin; sx <= box.xMax; sx += 0.5f) for (float sz = box.yMin; sz <= box.yMax; sz += 0.5f)
                { if (DistanceToRiver(sx, sz) < 8f) river++; if (LedgeH(sx, sz) > 0.3f) ledge++; if (R13InDeck(new Vector2(sx, sz), 2f)) deck++; }
                if (river + ledge + deck > 0) { why += $" ({c.x:0},{c.y:0}) river {river} ledge {ledge} bridge {deck};"; continue; }
                if (lmBoxes.Any(o => o.box.Overlaps(box))) { why += $" ({c.x:0},{c.y:0}) overlaps {lmBoxes.First(o => o.box.Overlaps(box)).name};"; continue; }
                pick = c; break;
            }
            if (pick == null) { Log($"R13 building CHECK FAIL {p.folder}: no free spot -{why}"); continue; }
            if (why.Length > 0) Log($"R13 building {p.folder}: skipped{why}");
            var go = LMPlace(p.folder, pick.Value, yaw, p.front, p.h, g);
            if (go) go.name = "R13_" + p.folder;
            if (go && p.folder == "Sarangchae")
                foreach (var m in go.GetComponentsInChildren<Renderer>().SelectMany(x => x.sharedMaterials).Where(x => x && x.HasProperty("_SnowClump")).Distinct())
                {   // r13b: the clumpy snow followed every tile row (black / white stripes): fuller, softer cover, flatter tiles
                    m.SetFloat("_SnowCover", 0.95f); m.SetFloat("_SnowScale", 0.3f); m.SetFloat("_SnowLift", 0.35f);
                    if (m.HasProperty("_NormalStrength")) m.SetFloat("_NormalStrength", 0.15f);
                    EditorUtility.SetDirty(m);
                }
        }
    }

    // extra capture views for SPRemote (bridges, blood sites, new buildings, sparkle close-up, game-camera frames)
    public static List<(string name, Vector3 focus, float zoom, bool player)> R13Views()
    {
        var v = new List<(string, Vector3, float, bool)>();
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int i = 0;
        foreach (var t in all.Where(t => t.name == "SP_MX_Bridge").OrderBy(t => t.position.x))
        { v.Add(($"r13_bridge_{i}", new Vector3(t.position.x, 0, t.position.z), 1.25f, false)); v.Add(($"r13_bridge_{i}_game", new Vector3(Mathf.Clamp(t.position.x, -HX + 8f, HX - 8f), 0, t.position.z - 4f), 1.6f, false)); i++; }
        i = 0;
        var pools = all.Where(t => t.name == "BloodR13_Pool").Select(t => t.position).ToList();
        var done = new List<Vector3>();
        foreach (var p in pools)
        {
            if (done.Any(q => Vector3.Distance(p, q) < 6f)) continue;
            done.Add(p);
            v.Add(($"r13_blood_{i}", new Vector3(p.x, 0, p.z), 0.45f, false));
            v.Add(($"r13_blood_{i}_game", new Vector3(p.x, 0, p.z), 1.0f, false)); i++;
        }
        foreach (var t in all.Where(t => t.name.StartsWith("R13_")))
            v.Add(($"r13_{t.name.Substring(4).ToLowerInvariant()}", new Vector3(Mathf.Clamp(t.position.x, -HX - 4f, HX + 4f), 0, Mathf.Clamp(t.position.z, -HZ, HZ + 2f)), 1.6f, false));
        v.Add(("r13_sparkle_close", new Vector3(-20f, 0, -6f), 0.4f, false));
        v.Add(("r13_sparkle_game", new Vector3(8f, 0, -18f), 1.0f, false));
        return v;
    }
}
