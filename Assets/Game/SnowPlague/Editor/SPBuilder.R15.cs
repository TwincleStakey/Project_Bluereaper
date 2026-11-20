// Round 15 (11-13, Kido): misfit props out, bridge deck fix, north wall pieces out, tall dark foreground masses on the
// camera-side edge only, ground clutter drawn cheaper, grass tearing. Kept in its own file; R15Pass runs after R13Pass.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    // "r15probe": every renderer per top-level group (name, parent, centre, size, mesh, material, shadows) to a log file
    public static void R15Probe()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var r0 = GameObject.Find("SnowPlague"); if (!r0) { Log("R15PROBE no root"); return; }
        var sb = new System.Text.StringBuilder();
        foreach (Transform g in r0.transform)
        {
            var rs = g.GetComponentsInChildren<Renderer>(true);
            sb.AppendLine($"== {g.name}: children {g.childCount}, renderers {rs.Length}, comps {string.Join(",", g.GetComponents<Component>().Select(c => c.GetType().Name))}");
            foreach (var r in rs.Take(400))
            {
                var mf = r.GetComponent<MeshFilter>();
                var b = r.bounds;
                sb.AppendLine($"  {r.transform.parent?.name}/{r.name} on={r.enabled && r.gameObject.activeInHierarchy} c=({b.center.x:0.0},{b.center.y:0.0},{b.center.z:0.0}) s=({b.size.x:0.0},{b.size.y:0.0},{b.size.z:0.0}) mesh={(mf && mf.sharedMesh ? mf.sharedMesh.name + ":" + mf.sharedMesh.vertexCount : "-")} mat={string.Join("|", r.sharedMaterials.Select(m => m ? m.name : "null"))} sh={r.shadowCastingMode}");
            }
        }
        var path = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource", "SnowPlague", "r15probe.txt");
        System.IO.File.WriteAllText(path, sb.ToString());
        Log("R15PROBE wrote " + path);
    }
}

public static partial class SPBuilder
{
    // edge survey views for SPRemote ("shots <dir containing r15only>"): north, west, east edges at the game camera
    public static List<(string name, Vector3 focus, float zoom, bool player)> R15Views()
    {
        var v = new List<(string, Vector3, float, bool)>();
        foreach (float x in new[] { -40f, -12f, 16f, 44f }) v.Add(($"r15_n{x:0}", new Vector3(x, 0, HZ - 6f), 1.6f, false));
        foreach (float z in new[] { -30f, 0f, 28f }) { v.Add(($"r15_w{z:0}", new Vector3(-HX + 8f, 0, z), 1.6f, false)); v.Add(($"r15_e{z:0}", new Vector3(HX - 8f, 0, z), 1.6f, false)); }
        foreach (float x in new[] { -30f, 0f, 30f }) v.Add(($"r15_s{x:0}", new Vector3(x, 0, -HZ + 8f), 1.6f, false));
        v.Add(("r15_s_edge_player", new Vector3(-6f, 0, -HZ + 2f), 1.6f, true)); v.Add(("r15_s_edge_player2", new Vector3(20f, 0, -HZ + 4f), 1.6f, true));
        v.Add(("r15_ledge_w0", new Vector3(-HX - 1f, 0, 0f), 0.7f, false)); v.Add(("r15_ledge_w25", new Vector3(-HX - 1f, 0, 25f), 0.7f, false)); v.Add(("r15_ledge_n", new Vector3(-30f, 0, HZ + 1f), 0.7f, false));
        return v;
    }
}

public static partial class SPBuilder
{
    static void R15Pass()
    {
        try { R15Remove(); } catch (System.Exception e) { Log("R15 remove EXCEPTION " + e); }
        try { R15Bridge(); } catch (System.Exception e) { Log("R15 bridge EXCEPTION " + e); }
        try { R15Boundary(); } catch (System.Exception e) { Log("R15 boundary EXCEPTION " + e); }
        try { R15TallForeground(); } catch (System.Exception e) { Log("R15 foreground EXCEPTION " + e); }
        try { R15Cliffs(); } catch (System.Exception e) { Log("R15 cliffs EXCEPTION " + e); }
        try { R15Shadows(); } catch (System.Exception e) { Log("R15 shadows EXCEPTION " + e); }
        try { R15Coverage(); } catch (System.Exception e) { Log("R15 coverage EXCEPTION " + e); }
    }

    // 1. misfits (Kido 11-13): the dark mossy boulder clusters on every edge (BorderRocks, read as black angular chunks
    //    with a brown face), the hanok wall runs on the north edge ("요 위에 이런거는 궂이 없어도"), the stretched stone-wall
    //    blocks under the bridge ends, and the repeated pagodas / jangseungs of the r14 edge masses
    static void R15Remove()
    {
        int n = 0;
        void Kill(Transform t) { if (t) { Object.DestroyImmediate(t.gameObject); n++; } }
        Kill(root.Find("BorderRocks"));
        var all = root.GetComponentsInChildren<Transform>(true);
        foreach (var t in all.Where(t => t && (t.name == "SKF_HanokWall" || t.name == "SP_R13_Abutment")).ToList()) Kill(t);
        var em = root.Find("EdgeMasses");
        if (em) foreach (var t in em.Cast<Transform>().Where(t => t.name.Contains("Pagoda") || t.name.Contains("Jangseung")).ToList()) Kill(t);
        Log($"R15 removed {n} misfit objects/groups");
    }

    // 2. bridge deck: half the scanned planks are wound the other way and were back-face culled, so the dark ice under
    //    the deck showed through as black plank stripes. Draw both faces (the lit pass flips the normal on back faces).
    static void R15Bridge()
    {
        foreach (var name in new[] { "SP_MX_Bridge", "SP_MX_BridgeRail" })
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{name}.mat");
            if (!m) { Log("R15 bridge: no " + name); continue; }
            m.SetFloat("_Cull", 0f);
            // r15b diag (r15bridgediag): the black plank bands were the scan normal map - on the deck stretched ~3.7x along the
            // span its tangent frame tilts whole planks away from the moon. With the map off the deck is evenly snowed.
            if (name == "SP_MX_Bridge") m.SetFloat("_NormalStrength", 0.12f);
            EditorUtility.SetDirty(m);
        }
        Log("R15 bridge: deck and rails two-sided");
    }

    // 3. the faint grey line the old boundary ring left on the ground (the long straight seam across the snow)
    static void R15Boundary()
    {
        var w = root.Find("FogVfx/BoundaryWall");
        if (w) { Object.DestroyImmediate(w.gameObject); Log("R15 boundary line removed"); }
    }

    // 4. depth (Kido 11-13, Hades 2 ref): very tall near-black trees on the camera-side (south) edge only, outside the
    //    arena, leaning in so their crowns reach up into the bottom of the screen; dead conifers as bare pillars between.
    //    No colliders, no shadows, FG (dark gradient + see-through) materials.
    static void R15TallForeground()
    {
        var fg = root.Find("Foreground") ?? Group("Foreground");
        var swap = new Dictionary<Material, Material>();
        foreach (var kv in SkFgSwap()) swap[kv.Key] = kv.Value;
        var rnd = new System.Random(1515);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        string[] live = { "SK:Con_FirTree_0", "SK:Con_PineTree_1" };
        int made = 0;
        for (float x = -HX - 8f + R(0f, 4f); x < HX + 8f; x += R(11f, 17f))
        {
            bool dead = rnd.NextDouble() < 0.3;
            string model = dead ? SkDeads[rnd.Next(SkDeads.Length)] : live[rnd.Next(live.Length)];
            float h = dead ? R(11f, 14f) : R(12f, 16f);   // r15b/d/e: 17-27 m covered the screen; 10-14 m at 12-17 m out never showed at the game zoom (1.6)
            float z = -HZ - R(9f, 12f);   // game cam (zoom 1.6, 27 up / 23 back): a 12-16 m tree here fills only the bottom ~15% when the player is near the edge
            var go = Place(model, x, z, R(0, 360), h / SkRef[model.Substring(3)], fg, false);
            if (!go) continue;
            go.name = "R15_TallFG";
            // lean toward the arena (north) about the base, a little sideways
            var basePos = go.transform.position;
            go.transform.rotation = Quaternion.AngleAxis(R(3f, 7f), Vector3.right) * Quaternion.AngleAxis(R(-5f, 5f), Vector3.forward) * go.transform.rotation;
            go.transform.position = basePos;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => m && swap.TryGetValue(m, out var f) ? f : m).ToArray();
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            made++;
        }
        Log($"R15 tall foreground: {made} trees on the south edge");
    }
}

public static partial class SPBuilder
{
    // 5. cliff faces (Kido "여기 벽들 왜이래 택스쳐"): the north-west ledge used the brown Poly Haven rock_face_01 scan,
    //    which reads as vertical brown streaks and blocky noise on the steep heightfield faces. Megascans Snow Cliff surface
    //    instead (grey rock with snow in the cracks), world triplanar at 6 m, softer normals; the self-made north rock
    //    faces (SP_Arch_RockFace_*) get the same material so all cliff walls match.
    static void R15Cliffs()
    {
        const string F = Root + "/ThirdParty/Megascans/snow_cliff_uhqncfpdy_2k/Snow_Cliff_uhqncfpdy_2K_";
        var alb = Tex(F + "BaseColor.jpg", false, false, false, 2048);
        var nrm = MXNormal(F + "Normal.jpg");
        if (!alb) { Log("R15 cliffs: no Snow Cliff albedo"); return; }
        var m = Prop("SP_MX_Plateau", alb, nrm, new Color(0.46f, 0.47f, 0.5f), 0.06f, 0.55f);   // r15c/d: 0.82, then 0.6 still glared where the stone lanterns light the face
        if (!m) return;
        m.SetFloat("_VertexFrost", 0f); m.SetFloat("_SnowBias", 0.15f); m.SetFloat("_GroundMelt", 0f); m.SetFloat("_NormalStrength", 0.9f);
        m.SetFloat("_Triplanar", 1f); m.SetFloat("_TriScale", 6f); m.SetColor("_TintVar", new Color(1f, 1f, 1f, 0.06f));
        EditorUtility.SetDirty(m);
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            if (r.name.StartsWith("SP_Arch_RockFace")) { r.sharedMaterial = m; n++; }
        Log($"R15 cliffs: ledge + {n} rock faces on Snow Cliff triplanar");
    }
}

public static partial class SPBuilder
{
    // 6. perf: the census counted ~11M shadow-caster triangles (4 cascades) for a field whose camera only ever sees the
    //    arena plus a ~15 m rim. Casters farther than 14 m from the arena edge (back rows of the forest, the far side of
    //    the ledge, houses past the rim) stop casting: their shadows never reach anything the game camera shows.
    //    The trees on the rim keep theirs (the long tree shadows across the snow are part of the look).
    static void R15Shadows()
    {
        long Tris(Mesh m) { long t = 0; if (m) for (int i = 0; i < m.subMeshCount; i++) t += m.GetIndexCount(i) / 3; return t; }
        var keep = new Bounds(Vector3.zero, new Vector3(2 * HX + 28f, 400f, 2 * HZ + 28f));
        long before = 0, after = 0; int off = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) continue;
            var mf = r.GetComponent<MeshFilter>(); long t = mf ? Tris(mf.sharedMesh) : 0;
            before += t;
            if (!keep.Intersects(r.bounds) && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) { r.shadowCastingMode = ShadowCastingMode.Off; off++; continue; }
            after += t;
        }
        Log($"R15 shadows: casters {before / 1000}k -> {after / 1000}k tris ({off} renderers off, > 14 m outside the arena)");
    }
}

public static partial class SPBuilder
{
    // 7. grass shimmer while moving (Kido "움직이면 풀이 찢어져"): the dry-grass tuft / needle / maple atlas was the only
    //    alpha-cut atlas without coverage-preserving mips, so thin blades thinned out and popped from mip to mip as the
    //    camera moved over the white snow (no MSAA in this project, so alpha-to-coverage is not an option)
    static void R15Coverage()
    {
        const string P = Root + "/Textures/SP_FoliageAtlas.png";
        var imp = AssetImporter.GetAtPath(P) as TextureImporter;
        if (!imp) { Log("R15 coverage: no " + P); return; }
        if (imp.mipMapsPreserveCoverage && imp.alphaIsTransparency) { Log("R15 coverage: atlas already preserves coverage"); return; }
        imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = 0.5f; imp.alphaIsTransparency = true;
        imp.SaveAndReimport();
        Log("R15 coverage: SP_FoliageAtlas mips now preserve alpha coverage");
    }
}
