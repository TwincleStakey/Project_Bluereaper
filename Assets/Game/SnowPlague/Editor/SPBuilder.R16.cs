// Round 16 (11-14, Kido: "다리 너무 어설퍼 너가 만들지 말고 좀 받던지해"). The Megascans arched deck stretched ~3.7x with a
// flat white snow cap and vermilion rails read as self-made. Each skewed SP_MX_Bridge (R13 placement: position, yaw, bank
// tilt, span) is swapped for the downloaded "Snowy bridge" by NoodlePoodle (Sketchfab, CC BY 4.0, see CREDITS.md): a
// plain wooden trestle bridge with snow already in its texture. Old deck, rails and stone abutments are hidden.
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    const string R16BridgeDir = Root + "/ThirdParty/Sketchfab/SnowyBridge";
    const float R16DeckTop = 3.22f;   // deck surface above the lowest point (stilt feet) of the model, measured in Blender

    static void R16Pass()
    {
        try { R16Bridges(); } catch (System.Exception e) { Log("R16 bridges EXCEPTION " + e); }
        try { R16Posts(); } catch (System.Exception e) { Log("R16 posts EXCEPTION " + e); }
        try { R16Thin(); } catch (System.Exception e) { Log("R16 thin EXCEPTION " + e); }
    }

    // Kido 11-14 ("설원 프레임이 너무 낮아" + "식생들이 깜빡거려"). PerfProbe: base 114 fps, foliage off 181; SPFlicker: the
    // self-made sub-pixel twig meshes are most of the shimmer. VegetationDense alone was ~15k plants / ~25 M triangles
    // (domes 2.7k tris x 1.9k, thickets 5k x 600). Thinned by a position hash (stable between builds, even spread),
    // the high-triangle twiggy meshes and the white-capped seed weeds most; ground twig sticks mostly gone.
    static readonly (string mesh, float keep)[] R16Keep =
    {
        ("SP_V_Dome_A", 0.35f), ("SP_V_Dome_B", 0.45f), ("SP_V_SeedWeed_A", 0.4f), ("SP_V_SeedWeed_B", 0.3f),
        ("SP_V_Thicket_A", 0.55f), ("SP_V_Thicket_B", 0.45f), ("SP_V_Brush_A", 0.55f), ("SP_V_Brush_B", 0.55f),
        ("SP_V_ReedWall_A", 0.6f), ("SP_V_ReedWall_B", 0.65f), ("SP_V_Cattails_A", 0.7f), ("SP_V_Cattails_B", 0.7f), ("SP_V_Cattails_C", 0.7f),
        ("SP_TwigBit", 0.25f), ("SP_Twigs", 0.35f), ("SP_DeadShrub_A", 0.7f), ("SP_DeadShrub_B", 0.7f), ("SP_DeadShrub_C", 0.7f),
    };
    static void R16Thin()
    {
        int before = 0, after = 0; long trisBefore = 0, trisAfter = 0;
        foreach (var sc in root.GetComponentsInChildren<SnowPlague.SPScatter>(true))
        {
            bool dirty = false;
            foreach (var s in sc.sets)
            {
                if (!s.mesh) continue;
                int tris = s.mesh.triangles.Length / 3;
                before += s.at.Count; trisBefore += (long)tris * s.at.Count;
                var k = R16Keep.FirstOrDefault(e => e.mesh == s.mesh.name);
                if (k.mesh != null)
                    for (int i = s.at.Count - 1; i >= 0; i--)
                    {
                        float h = Mathf.Abs(Mathf.Sin(s.at[i].x * 12.9898f + s.at[i].z * 78.233f) * 43758.5453f) % 1f;
                        if (h >= k.keep) { s.at.RemoveAt(i); s.scale.RemoveAt(i); dirty = true; }
                    }
                after += s.at.Count; trisAfter += (long)tris * s.at.Count;
            }
            if (dirty) { sc.Push(); EditorUtility.SetDirty(sc); }
        }
        Log($"R16 thin: scatter {before} -> {after} instances, {trisBefore / 1000000f:0.0} M -> {trisAfter / 1000000f:0.0} M triangles");
    }

    // Kido 11-14 ("이거 어색해", a plain dark post on the snow): the Megascans branchless pine trunks (4 m dark cylinders),
    // the dead-pine trunk stubs in the scatter and the square Japanese stone pillars all read as bare posts from the game
    // camera (found with SPFlicker "posts" + "closeups"). Trunks well outside the boundary become real conifers (same
    // Coniferous pack as the frame trees), the rest go.
    static void R16Posts()
    {
        int trees = 0, gone = 0, stubs = 0, pillars = 0, k = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "SP_MX_PineTrunk" || r.name == "SP_MX_StonePillar").ToList())
        {
            var p = r.transform.position;
            bool pillar = r.name == "SP_MX_StonePillar";
            r.gameObject.SetActive(false);
            if (pillar) { pillars++; continue; }
            if (Mathf.Abs(p.x) > HX + 3f || Mathf.Abs(p.z) > HZ + 3f)
            {
                var g = Place((k++ & 1) == 0 ? "SK:Con_PineTree_1" : "SK:Con_FirTree_0", p.x, p.z, (k * 137.5f) % 360f, 0.9f + (k % 3) * 0.1f, r.transform.parent, false, 0.15f);
                if (g) { trees++; continue; }
            }
            gone++;
        }
        foreach (var sc in root.GetComponentsInChildren<SnowPlague.SPScatter>(true))
        {
            int before = sc.Count;
            foreach (var s in sc.sets.Where(s => s.mesh && s.mesh.name == "SP_MX_DeadPine")) { stubs += s.at.Count; s.at.Clear(); s.scale.Clear(); }
            if (sc.Count != before) { sc.Push(); EditorUtility.SetDirty(sc); }
        }
        Log($"R16 posts: {trees} bare trunks -> conifers, {gone} trunks removed, {stubs} dead-pine stubs removed, {pillars} stone pillars removed");
    }

    static void R16Bridges()
    {
        var bridges = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "SP_MX_Bridge").ToList();
        if (bridges.Count == 0) { Log("R16 bridge: no SP_MX_Bridge"); return; }
        var albedo = Tex(R16BridgeDir + "/SnowyBridge_BaseColor.jpg");
        var normal = Tex(R16BridgeDir + "/SnowyBridge_NormalGL.png", true);   // glTF normals are OpenGL (Y+): no flip
        var mat = Prop("SP_SnowyBridge", albedo, normal, new Color(0.92f, 0.9f, 0.88f), 0.12f, 0.35f, true);
        if (mat) { mat.SetFloat("_VertexFrost", 0f); mat.SetFloat("_SnowBias", -0.8f); mat.SetFloat("_GroundMelt", 0f); mat.SetColor("_TintVar", new Color(1f, 1f, 1f, 0f)); }
        var src = MXLoad("SnowyBridge", R16BridgeDir + "/SnowyBridge.fbx", mat, 0);
        if (src == null) { Log("R16 bridge: model missing, old bridges kept"); return; }
        bool alongZ = src.size.z > src.size.x;
        float len = alongZ ? src.size.z : src.size.x;
        foreach (var br in bridges)
        {
            var mf = br.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
            float span = mf.sharedMesh.bounds.size.x * br.localScale.x;   // R13 stretched the old deck to the span
            float k = Mathf.Clamp((span + 1.0f) / len, 0.85f, 1.45f);      // a little past both banks
            var go = new GameObject("SP_SnowyBridge");
            go.transform.SetParent(br.parent, false);
            // R13: local +X along the span, tilted to the bank heights; the old deck sat 0.12 below the bank mean
            var rot = br.rotation * (alongZ ? Quaternion.Euler(0, 90f, 0) : Quaternion.identity);
            float deckY = br.position.y + 0.12f + 0.22f;
            go.transform.SetPositionAndRotation(new Vector3(br.position.x, deckY - R16DeckTop, br.position.z), rot);
            go.transform.localScale = alongZ ? new Vector3(1f, 1f, k) : new Vector3(k, 1f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = src.mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            foreach (var old in br.GetComponentsInChildren<Renderer>(true)) old.enabled = false;
            Log($"R16 bridge at ({br.position.x:0.0}, {br.position.z:0.0}) span {span:0.0} m -> Snowy bridge x{k:0.00} ({len:0.0} m model, {src.tris} tris)");
        }
        int ab = 0;
        foreach (var t in root.GetComponentsInChildren<Renderer>(true)) if (t.name == "SP_R13_Abutment" && t.enabled) { t.enabled = false; ab++; }
        Log($"R16 bridges: {bridges.Count} swapped, {ab} stone abutments hidden");
    }
}
