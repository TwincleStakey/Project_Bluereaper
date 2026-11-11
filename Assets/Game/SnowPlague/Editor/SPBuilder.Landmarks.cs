// 11-12 landmarks outside the arena + the arena rule check (Kido, every field: inside the arena nothing may block movement,
// create height differences or block projectiles; only low non-blocking brush, debris and decals. Big props go just past
// the boundary so they still read big at the game camera).
// Models (CC BY 4.0, see CREDITS.md): ThirdParty/Landmarks/<Name>/ - KCISA 솟을삼문 (KoreanGate), KCISA small gate,
// KCISA thatched house, stone pagoda (PagodaGame), hanok wall, jangseung. Each source material gets a SnowPlague/Prop copy
// (albedo from <folder>/textures/<material>_baseColor.*), darkened and desaturated to the field's dark look, world-up snow.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const string LMDir = Root + "/ThirdParty/Landmarks/";
    // west gate (솟을삼문) just past the west boundary, north of where the river leaves the arena, facing the arena
    public static readonly Vector2 LMGatePos = new Vector2(-66f, -36.5f);   // tr17: south bank at the west bridge (tr15/16 at -64,-7 sat against the ledge)

    static readonly Dictionary<string, (float h, float baseY, Quaternion rot, Vector3 scl, Vector3 size)> lmDims = new Dictionary<string, (float, float, Quaternion, Vector3, Vector3)>();

    static string LMPath(string name)
    {
        var dir = LMDir + name;
        if (!Directory.Exists(ProjAbsVg(dir))) return null;
        var f = Directory.GetFiles(ProjAbsVg(dir), "*.fbx").FirstOrDefault();
        return f == null ? null : dir + "/" + Path.GetFileName(f);
    }

    // height / base / footprint of the model in its root-rotated space (Z-up sources keep their root axis fix)
    static (float h, float baseY, Quaternion rot, Vector3 scl, Vector3 size) LMMeasure(string name, GameObject src)
    {
        if (lmDims.TryGetValue(name, out var d)) return d;
        Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f;
        var R = Matrix4x4.TRS(Vector3.zero, src.transform.localRotation, src.transform.localScale) * src.transform.worldToLocalMatrix;
        foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            var b = mf.sharedMesh.bounds; var M = R * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = M.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                lo = Vector3.Min(lo, c); hi = Vector3.Max(hi, c);
            }
        }
        d = (hi.y - lo.y, lo.y, src.transform.localRotation, src.transform.localScale, hi - lo);
        lmDims[name] = d;
        Log($"LM {name}: {d.size.x:0.0} x {d.size.y:0.0} x {d.size.z:0.0} m (root rot {src.transform.localRotation.eulerAngles})");
        return d;
    }

    static readonly Dictionary<string, Material> lmMats = new Dictionary<string, Material>();
    static Material LMMat(string folder, Material srcMat)
    {
        if (!srcMat) return null;
        string key = folder + "/" + srcMat.name;
        if (lmMats.TryGetValue(key, out var m) && m) return m;
        string dir = LMDir + folder + "/textures";
        Texture2D alb = null, nrm = null;
        if (Directory.Exists(ProjAbsVg(dir)))
        {
            string bn = srcMat.name.Replace(" (Instance)", "");
            foreach (var f in Directory.GetFiles(ProjAbsVg(dir)))
            {
                var fn = Path.GetFileName(f); if (fn.EndsWith(".meta")) continue;
                if (!fn.StartsWith(bn + "_")) continue;
                if (fn.Contains("_baseColor")) alb = Tex(dir + "/" + fn, false, false, false, 1024);
                else if (fn.Contains("_normal")) nrm = Tex(dir + "/" + fn, true, false, false, 1024);
            }
        }
        if (!alb) alb = srcMat.mainTexture as Texture2D;
        // dark field look: darker and desaturated; snow on top faces (world-up, patchy)
        string safe = new string((folder + "_" + srcMat.name).Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        string nm = srcMat.name;
        bool roof = nm.StartsWith("MI_R_Roof") || nm.StartsWith("MI_R_Buyeon") || nm.StartsWith("MI_R_Dancheong") || nm.StartsWith("MI_KoreanWood")
                    || nm.IndexOf("Roof", System.StringComparison.OrdinalIgnoreCase) >= 0 || nm == "15_-_Default" || nm == "Material_925";   // + hanok wall cap (tr19)
        if (folder == "KoreanLantern")
        {
            // tr28: scan colour lives in the vertex colours (no textures): Prop with _VColAlbedo, snow cap on the roof stone
            m = Prop("SP_LM_" + safe, null, null, Hex("#6c6a66"), 0.1f, 0.3f, false);   // tr33 (lead): weathered grey granite base tint
            if (m) { m.SetFloat("_VColAlbedo", 0.35f); m.SetFloat("_VertexFrost", 0f); m.SetFloat("_SnowBias", 0.4f); m.SetFloat("_GroundMelt", 0.12f); m.SetFloat("_SnowCap", 0.7f); }   /* tr34: roof top reads white */   // tr33: the scan colours read as a near-black lump - only 35 % of them over the granite tint
        }
        else if (roof)
        {
            // tr18: roofs on the Vegetation shader for its clumpy world-up snow (_SnowClump: up x noise, soft edge, thick on the
            // flatter upper rows / ridge, the tiles and dancheong showing in the gutters and at the steeper eaves)
            m = Veg("SP_LM_" + safe, alb, nrm, new Color(0.7f, 0.67f, 0.64f), false, 0f, 0f);
            if (m)
            {
                bool thatch = folder == "ThatchHouse";
                bool gate = folder == "KoreanGate" || folder == "SmallGate";
                m.SetFloat("_SnowClump", 1f); m.SetFloat("_SnowCover", thatch ? 0.82f : (gate ? 0.9f : 0.8f));
                m.SetFloat("_SnowScale", 0.55f);                 // tr19: ~1.5-2 m soft drifts, no lattice
                m.SetFloat("_SnowLift", gate ? 0.6f : 0.25f);    // the gate stands in the moon shadow: lift its snow toward white m.SetFloat("_Desat", 0.4f);
                m.SetFloat("_AOStrength", 0f); m.SetFloat("_GroundMelt", 0f); m.SetFloat("_Wrap", 0.2f); m.SetFloat("_SnowBias", 0.3f);
                if (nrm) m.SetFloat("_NormalStrength", 0.6f);
            }
            Log($"LM roof snow: {folder}/{nm} -> {(m ? m.name : "-")}");
        }
        else
        {
            m = Prop("SP_LM_" + safe, alb, nrm, new Color(0.66f, 0.63f, 0.6f), 0.12f, 0.45f, true);
            if (m) { m.SetFloat("_SnowBias", 0.2f); m.SetFloat("_GroundMelt", 0.05f); m.SetFloat("_VertexFrost", 0f); }
            // tr30 (QA #14): the gate's stone platform read as a flat grey rectangle in 16 - snow lying on its top, stone relief
            bool plinth = nm.Contains("Floor") || nm.Contains("Stone") || nm.Contains("Brick");
            if (m && plinth && (folder == "KoreanGate" || folder == "SmallGate"))
            {
                m.SetFloat("_SnowCap", 0f); m.SetFloat("_SnowBias", 0.2f);   // tr32/33: the patchy cap read as blocky white patches in 16
                // tr31 (lead): still a flat grey slab in 16 - darker, desaturated, world triplanar stone (gnd5 _Triplanar), stronger relief
                m.SetColor("_Tint", new Color(0.48f, 0.47f, 0.45f)); m.SetFloat("_Desaturate", 0.6f);
                // tr36 (lead): real stone - the Megascans japanese stone wall albedo/normal in world triplanar (~1.4 m per tile)
                var sbc = MXFind("japanese_stone_wall_ubnnaghfa_mid", "_BaseColor.jpg"); var snm = MXFind("japanese_stone_wall_ubnnaghfa_mid", "_Normal.jpg");
                if (sbc != null) { m.SetTexture("_BaseMap", Tex(sbc)); if (snm != null) m.SetTexture("_BumpMap", MXNormal(snm)); m.SetFloat("_Triplanar", 1f); m.SetFloat("_TriScale", 1.4f); m.SetColor("_Tint", new Color(0.62f, 0.6f, 0.57f)); m.SetFloat("_NormalStrength", 1.2f); }
                else { m.SetFloat("_Triplanar", 0f); Log("LM plinth: no Megascans stone wall texture"); }
                if (nrm) m.SetFloat("_NormalStrength", 1.8f);
            }
            Log($"LM mat {folder}/{nm}: albedo {(alb ? alb.name : "NONE")}, normal {(nrm ? nrm.name : "none")}{(plinth ? " (plinth: snow cap)" : "")}");
        }
        lmMats[key] = m;
        return m;
    }

    static readonly List<(Vector2 c, float r)> lmFootprints = new List<(Vector2, float)>();
    static readonly List<(string name, Rect box)> lmBoxes = new List<(string, Rect)>();
    static int lmFails;

    // place a landmark: base on the ground; frontYaw turns the model so its FRONT (main door) faces the game camera (world
    // -Z: the camera looks north). Footprint checks: not on the river/ice, not in the arena, not on the west ledge, no overlap.
    static GameObject LMPlace(string folder, Vector2 at, float frontYaw, Vector3 localFront, float targetH, Transform parent, bool castShadows = true, bool building = true)
    {
        var path = LMPath(folder); if (path == null) { Log("LM missing " + folder); return null; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!src) { Log("LM no model " + path); return null; }
        var d = LMMeasure(folder, src);
        float k = targetH > 0 ? targetH / Mathf.Max(0.1f, d.h) : 1f;
        // materials first (texture imports reimport the FBX; doing it after instantiating reset the instance to the origin)
        var matMap = new Dictionary<Material, Material>();
        foreach (var r0 in src.GetComponentsInChildren<Renderer>(true)) foreach (var mm in r0.sharedMaterials) if (mm && !matMap.ContainsKey(mm)) matMap[mm] = LMMat(folder, mm);
        src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = r.sharedMaterials.Select(mm => mm && matMap.TryGetValue(mm, out var t) && t ? t : (LMMat(folder, mm) ?? mm)).ToArray();
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        float y = Height(at.x, at.y);
        var rot = Quaternion.Euler(0, frontYaw, 0);
        go.transform.SetPositionAndRotation(new Vector3(at.x, y - d.baseY * k - 0.05f, at.y), rot * d.rot);
        go.transform.localScale = d.scl * k;
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
        // world footprint from the renderers
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        var box = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
        lmFootprints.Add((at, Mathf.Max(b.extents.x, b.extents.z) + 1.5f));
        // front vs camera
        var front = rot * localFront; float ang = Vector3.Angle(new Vector3(front.x, 0, front.z), Vector3.back);
        Log($"LM placed {folder} at ({at.x:0.0}, {at.y:0.0}) h {d.h * k:0.0} m, yaw {frontYaw:0}, footprint {box.width:0.0} x {box.height:0.0} m, front vs camera {ang:0} deg");
        // checks
        void Fail(string why) { lmFails++; Log($"LM CHECK FAIL {folder}: {why}"); }
        if (ang > 30f) Fail($"front {ang:0} deg away from the camera");
        var inner = new Rect(-HX, -HZ, 2 * HX, 2 * HZ);
        if (box.Overlaps(inner)) Fail("footprint reaches into the arena");
        int river = 0, ledge = 0;
        for (float sx = box.xMin; sx <= box.xMax; sx += 0.5f) for (float sz = box.yMin; sz <= box.yMax; sz += 0.5f)
        { if (DistanceToRiver(sx, sz) < 7f) river++; if (LedgeH(sx, sz) > 0.3f) ledge++; }
        if (river > 0) Fail($"{river} footprint samples on the river / ice band");
        if (building && ledge > 0) Fail($"{ledge} footprint samples on the west/north ledge");
        foreach (var o in lmBoxes) if (o.box.Overlaps(box)) Fail("overlaps " + o.name);
        lmBoxes.Add((folder, box));
        return go;
    }

    static void BuildLandmarks()
    {
        lmFootprints.Clear(); lmMats.Clear(); lmBoxes.Clear(); lmFails = 0;
        if (!Directory.Exists(ProjAbsVg(LMDir))) { Log("LM no landmarks folder"); return; }
        var g = Group("Landmarks");
        // flow: west bridge (x -66, over the river) -> 솟을삼문 on the south bank, door facing the camera -> jangseung pair and
        // a stone pagoda in front of it. Gate width runs along X (door through-axis = model X -> world -Z with yaw 90)
        // tr44 (Kido, decision card): the hero corner gate is the small KCISA gate (House of Changwon) at the bridge end, front to the
        // camera, roof snow; the 솟을삼문 is removed (its 16 m roof filled view 20) and the north-band small gate dropped (one copy)
        LMPlace("SmallGate", LMGatePos, 90f, Vector3.right, 0f, g);
        foreach (float s in new[] { -1f, 1f })
            LMPlace("Jangseung", new Vector2(LMGatePos.x + s * 4.2f, LMGatePos.y - 6.5f), 180f, Vector3.forward, 2.5f, g, true, false);
        LMPlace("PagodaGame", new Vector2(LMGatePos.x + 7.5f, LMGatePos.y - 9f), 0f, Vector3.back, 5.5f, g, true, false);
        // north band past the end of the ledge: small gate + two hanok wall runs, fronts to the camera
        // tr44: north-band small gate dropped - the one copy now stands at the west bridge (hero corner)
        LMPlace("HanokWall", new Vector2(20f, HZ + 4.5f), 0f, Vector3.back, 0f, g, false);
        LMPlace("HanokWall", new Vector2(38f, HZ + 4.5f), 0f, Vector3.back, 0f, g, false);
        // thatched house on the east bank south of the river exit; Kido (tr15): its back faced the camera -> model -Z is the front
        LMPlace("ThatchHouse", new Vector2(HX + 10f, 8f), 0f, Vector3.back, 0f, g);
        Log(lmFails == 0 ? $"LM CHECKS OK: {g.childCount} landmarks, no river / arena / ledge / overlap / facing problems" : $"LM CHECKS FAILED: {lmFails} problems (see LM CHECK FAIL lines)");
    }

    // final pass, after every builder: clear the landmark footprints of border clutter, then enforce the arena rule
    static void ArenaCheck()
    {
        var protect = new HashSet<string> { "Ground", "ArenaWalls", "FrozenRiver", "Ice", "Landmarks", "FogVfx", "KoreanLanterns", "HeroCorner" };   // tr31: the lanterns sat in their own footprints and were switched off
        string Group0(Transform t) { while (t.parent && t.parent != root) t = t.parent; return t.name; }
        // 1) nothing from the border / tree rows / Megascans props stands inside a landmark
        int cleared = 0;
        if (lmFootprints.Count > 0)
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                string g = Group0(r.transform);
                if (protect.Contains(g) || g == "VegetationDense" || g == "Scatter" || g == "GroundClutter" || g == "MegascansScatter") continue;
                var c = new Vector2(r.bounds.center.x, r.bounds.center.z);
                if (InArena(c.x, c.y)) continue;
                if (lmFootprints.Any(f => Vector2.Distance(c, f.c) < f.r)) { r.enabled = false; cleared++; }
            }
        // instanced scatter: drop entries under a landmark
        foreach (var sc in root.GetComponentsInChildren<SnowPlague.SPScatter>(true))
        {
            bool dirty = false;
            foreach (var s in sc.sets)
                for (int i = s.at.Count - 1; i >= 0; i--)
                {
                    var p = new Vector2(s.at[i].x, s.at[i].z);
                    if (lmFootprints.Any(f => Vector2.Distance(p, f.c) < f.r)) { s.at.RemoveAt(i); s.scale.RemoveAt(i); dirty = true; cleared++; }
                }
            if (dirty) sc.Push();
        }
        // 2) arena rule: no colliders inside (except the ground / ice floor), list anything tall that is not vegetation
        int colliders = 0; var tall = new List<string>();
        var inner = new Rect(-HX + 0.5f, -HZ + 0.5f, 2 * HX - 1f, 2 * HZ - 1f);
        bool Hits(Bounds b) => b.max.x > inner.xMin && b.min.x < inner.xMax && b.max.z > inner.yMin && b.min.z < inner.yMax;
        foreach (var col in root.GetComponentsInChildren<Collider>(true))
        {
            if (!col || protect.Contains(Group0(col.transform)) || !Hits(col.bounds)) continue;
            Log($"ARENA collider removed: {col.name} ({Group0(col.transform)}) {col.GetType().Name} at {col.bounds.center}");
            Object.DestroyImmediate(col); colliders++;
        }
        var vegGroups = new HashSet<string> { "VegetationDense", "VegetationFrame", "Scatter", "GroundClutter", "MegascansScatter", "Foreground" };
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r || !r.enabled || protect.Contains(Group0(r.transform)) || !Hits(r.bounds)) continue;
            if (r is ParticleSystemRenderer) continue;
            string g = Group0(r.transform);
            if (r.bounds.size.y > 1.2f && !vegGroups.Contains(g)) tall.Add($"{r.name} ({g}) h {r.bounds.size.y:0.0} at ({r.bounds.center.x:0.0}, {r.bounds.center.z:0.0})");
        }
        foreach (var t in tall) Log("ARENA tall: " + t);
        Log($"ARENA check: {colliders} colliders removed inside the arena, {tall.Count} tall non-vegetation renderers inside (listed above), {cleared} border items cleared from landmark footprints");
    }

    // tr21: the gate's lower half sat black in the moon shadow; a low warm spot from above the approach lights its front
    static void LMGateFill()
    {
        var gate = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("KCISA_SosamMun") || t.name.StartsWith("SKF_SmallGate")).OrderBy(t => Vector2.Distance(new Vector2(t.position.x, t.position.z), LMGatePos)).FirstOrDefault();   // tr44: the small gate at the bridge
        if (!gate) { Log("LM fill: no gate"); return; }
        var go = new GameObject("GateFill"); go.transform.SetParent(gate.parent, false);
        var target = new Vector3(LMGatePos.x, 1.4f, LMGatePos.y);   // tr23: aimed at the door / pillar feet, not the roof
        go.transform.position = target + new Vector3(0f, 3.4f, -7.5f);
        go.transform.rotation = Quaternion.LookRotation(target - go.transform.position);
        var l = go.AddComponent<Light>();
        l.type = LightType.Spot; l.color = new Color(1f, 0.82f, 0.6f); l.intensity = 8f; l.range = 16f; l.spotAngle = 95f; l.innerSpotAngle = 50f;   // tr23: lower, x8
        l.shadows = LightShadows.None; l.lightmapBakeType = LightmapBakeType.Realtime;
        Log($"LM fill: spot at {go.transform.position} -> {target}");
    }
}
