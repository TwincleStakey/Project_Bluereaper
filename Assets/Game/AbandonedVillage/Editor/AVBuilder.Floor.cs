// 11-12 (Kido: fill the inside richly with low things only, nothing tall enough to hide projectiles; no purple edge):
// the whole arena gets a dense, uneven layer of Megascans (Fab, free) floor dressing: pebble and small-rock clusters,
// rubble patches, fallen-leaf decals and dark blood pools, densest around the ruins and along the edge, thinner
// (never empty) in the middle. Everything stays under ~0.5 m, casts no shadow and has no collider.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class AVBuilder
{
    static Mesh decalQuad;
    static Mesh DecalQuad()
    {
        if (decalQuad) return decalQuad;
        decalQuad = new Mesh { name = "AV_DecalQuad" };
        decalQuad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) });
        decalQuad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
        decalQuad.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
        decalQuad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        decalQuad.RecalculateBounds(); decalQuad.RecalculateTangents();
        var path = Root + "/Generated/AV_DecalQuad.asset";
        AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(decalQuad, path);
        return decalQuad;
    }

    static Material DecalMat(string name, string tex, Color tint, float smooth, bool blend)
    {
        var imp = AssetImporter.GetAtPath(tex) as TextureImporter;
        if (imp && (!imp.alphaIsTransparency || imp.wrapMode != TextureWrapMode.Clamp)) { imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
        var m = Lit(name, TP(tex), null, tint, 1f, smooth);
        if (blend)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_BlendModePreserveSpecular", 0f); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");   // else the wet highlight shows the whole quad
            m.renderQueue = (int)RenderQueue.Transparent - 50;
        }
        else { m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.2f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = (int)RenderQueue.AlphaTest; }
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Decal(Transform parent, Material m, float x, float z, float size, System.Func<float, float, float> G, float lift)
    {
        var go = new GameObject(m.name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(x, G(x, z) + lift, z), Quaternion.Euler(0, R(0, 360), 0));
        go.transform.localScale = new Vector3(size * R(0.8f, 1.2f), 1f, size * R(0.8f, 1.2f));
        go.AddComponent<MeshFilter>().sharedMesh = DecalQuad();
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // 11-13 r8 (Kido: the handprints were far bigger than the characters and lay in rows; more gore): a pool = dark wide
    // pool + a second overlapping pool + an arterial spray over it + droplets; a drag trail = small pool decals stretched
    // along one direction, overlapping into one smear. A handprint is hand-sized (~0.4 m) and only ever one, on the rim
    // of a pool or at the end of a trail.
    static void DecalDir(Transform parent, Material m, float x, float z, float w, float l, float yaw, System.Func<float, float, float> G, float lift)
    {
        var go = new GameObject(m.name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(x, G(x, z) + lift, z), Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = new Vector3(w, 1f, l);
        go.AddComponent<MeshFilter>().sharedMesh = DecalQuad();
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void HandPrint(Transform parent, Material[] b, float x, float z, float yaw, System.Func<float, float, float> G)
    {
        float s = R(0.34f, 0.44f);
        DecalDir(parent, b[0], x, z, s, s, yaw, G, 0.036f);
    }

    static void BloodPool(Transform parent, Material[] b, float x, float z, float size, System.Func<float, float, float> G, bool hand)
    {
        DecalDir(parent, b[2], x, z, size * R(0.85f, 1.15f), size * R(0.85f, 1.15f), R(0, 360), G, 0.03f);
        DecalDir(parent, b[2], x + R(-0.4f, 0.4f) * size, z + R(-0.4f, 0.4f) * size, size * R(0.55f, 0.8f), size * R(0.55f, 0.8f), R(0, 360), G, 0.031f);
        DecalDir(parent, b[3], x, z, size * R(1.1f, 1.5f), size * R(1.1f, 1.5f), R(0, 360), G, 0.032f);
        for (int i = rng.Next(0, 3); i > 0; i--)   // r13: fewer round splats, stretched unevenly
        {
            float a = R(0, 360) * Mathf.Deg2Rad, d = size * R(0.6f, 1.2f);
            float s = R(0.5f, 1.1f);
            DecalDir(parent, b[1], x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, s * R(0.6f, 1f), s * R(1.1f, 1.8f), R(0, 360), G, 0.033f);
        }
        if (hand) { float a = R(0, 360) * Mathf.Deg2Rad, d = size * 0.42f; HandPrint(parent, b, x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, a * Mathf.Rad2Deg + R(-30, 30), G); }
    }

    static void DragTrail(Transform parent, Material[] b, Vector2 from, Vector2 dir, float len, System.Func<float, float, float> G, bool hand)
    {
        dir.Normalize();
        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
        var side = new Vector2(dir.y, -dir.x);
        int n = Mathf.Max(4, Mathf.RoundToInt(len / 0.32f));
        Vector2 p = from;
        float bend = R(-9f, 9f);   // r11 (reviewer: drags read as straight painted rods): the trail curves and thins out
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            dir = (Vector2)(Quaternion.Euler(0, 0, -bend) * (Vector3)dir); yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            p += dir * 0.32f;
            float w = Mathf.Lerp(1.0f, 0.3f, t * t) * R(0.8f, 1.2f);
            if (rng.NextDouble() < 0.12 && t > 0.3f) continue;   // gaps where the body lifted
            DecalDir(parent, t < 0.55f ? b[2] : b[1], p.x + R(-0.08f, 0.08f), p.y + R(-0.08f, 0.08f), w, w * R(1.4f, 2.0f), yaw + R(-12, 12), G, 0.0305f + i * 0.0002f);
        }
        if (hand) { var e = p + dir * 0.35f; HandPrint(parent, b, e.x, e.y, yaw + R(-25, 25), G); }
    }

    // 11-13 r8: a Poly Haven moss rock (CC0 scan) sunk into the ground so only a low hump shows (< 0.3 m), no collider
    static GameObject HalfRock(Vector3 p, float width, Transform parent)
    {
        var go = PlacePart(rng.NextDouble() < 0.5 ? "PH:rock_moss_set_01" : "PH:rock_moss_set_02", p, R(0, 360), 1f, parent);
        if (!go) return null;
        var b = ModelBounds(go); float w = Mathf.Max(b.size.x, b.size.z);
        if (w > 1e-4f) go.transform.localScale *= width / w;
        b = ModelBounds(go);
        float show = Mathf.Min(b.size.y * 0.55f, 0.28f);
        go.transform.position += Vector3.up * (p.y + show - b.max.y);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        return go;
    }

    // One piece out of a Megascans "pack" FBX (several rocks side by side in one file).
    static GameObject PlacePart(string model, Vector3 pos, float yaw, float scale, Transform parent)
    {
        var go = Place(model, pos, yaw, 1f, parent);
        if (!go) return null;
        var parts = go.GetComponentsInChildren<MeshRenderer>();
        if (parts.Length > 1)
        {
            var keep = parts[rng.Next(parts.Length)].gameObject;
            var mf = keep.GetComponent<MeshFilter>();
            var piece = new GameObject(go.name + "_" + keep.name);
            piece.transform.SetParent(parent, false);
            piece.transform.SetPositionAndRotation(pos, Quaternion.Euler(R(-12, 12), yaw, R(-12, 12)));
            // the pack's pieces are laid out in a row: re-centre the chosen one on its own pivot
            var mesh = mf.sharedMesh; var off = -mesh.bounds.center; off.y = -mesh.bounds.min.y;
            var child = new GameObject("mesh"); child.transform.SetParent(piece.transform, false);
            child.transform.localPosition = Vector3.Scale(off, keep.transform.lossyScale);
            child.transform.localRotation = keep.transform.localRotation;
            child.transform.localScale = keep.transform.lossyScale;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = child.AddComponent<MeshRenderer>(); r.sharedMaterials = keep.GetComponent<MeshRenderer>().sharedMaterials; r.shadowCastingMode = ShadowCastingMode.Off;
            piece.transform.localScale = Vector3.one * scale;
            GameObjectUtility.SetStaticEditorFlags(child, StaticEditorFlags.BatchingStatic);
            Object.DestroyImmediate(go);
            return piece;
        }
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    static void BuildFloorDressing(System.Func<float, float, float> G)
    {
        var floor = Group("FloorDressing");
        // blood pools: the Megascans blood atlas (2x2: two splashes, a drip trail, droplets), one material per quadrant
        var bloods = new Material[4];
        // 11-12 r7 (Kido: the blood looked clumsy): Weasel Whistle free PBR blood decals (Fab) - [2] wide pool with
        // footprints, [3] arterial splatter; [0] Megascans hand smear as drag marks; [1] the splatter again, small
        string[] bt = { "AV_MS_HandSmear_RGBA.png", "AV_WW_BloodSplatter_RGBA.png", "AV_WW_BloodPoolFootprints_RGBA.png", "AV_WW_BloodSplatter_RGBA.png" };
        string[] bn = { null, "AV_WW_BloodSplatter_N.png", "AV_WW_BloodPoolFootprints_N.png", "AV_WW_BloodSplatter_N.png" };
        for (int q = 0; q < 4; q++)
        {
            bloods[q] = DecalMat("AV_BloodDecal_" + q, MS + "Decals/" + bt[q], q == 0 ? new Color(0.24f, 0.06f, 0.05f, 1f) : new Color(0.5f, 0.3f, 0.28f, 1f)   /* r10: was cartoon red */, 0.9f, true);
            bloods[q].SetTextureScale("_BaseMap", Vector2.one); bloods[q].SetTextureOffset("_BaseMap", Vector2.zero);
            // 11-13 QA (blood read as flat red paint): the emissive red lit every decal evenly regardless of light; now
            // almost none, darker brown-red albedo and a wet gloss, so the pools only show red where light catches them
            Emissive(bloods[q], new Color(0.015f, 0.002f, 0.001f));   // r2: 0.012 left the pools as black holes in the dark
            if (bn[q] != null) { bloods[q].SetTexture("_BumpMap", TP(MS + "Decals/" + bn[q], true)); bloods[q].SetFloat("_BumpScale", 0.8f); bloods[q].EnableKeyword("_NORMALMAP"); }
        }
        var leaves = DecalMat("AV_LeafDecal", MS + "Decals/AV_MS_Leaves_RGBA.png", new Color(0.46f, 0.42f, 0.37f, 1f), 0.08f, false);   // 11-13 r8: was 0.72/0.64/0.56, read as yellow dots
        int nPeb = 0, nRock = 0, nLeaf = 0, nBlood = 0, nPatch = 0, nKhs = 0;
        const float Cell = 3.6f;
        for (float cz = ArenaMinZ - 2f; cz < ArenaMaxZ + 2f; cz += Cell)
        for (float cx = -ArenaHalfX - 2f; cx < ArenaHalfX + 2f; cx += Cell)
        {
            float x = cx + R(0, Cell), z = cz + R(0, Cell);
            float edge = Mathf.Max(Mathf.Abs(x) / ArenaHalfX, Mathf.Abs(z + 3.5f) / 45f);
            float near = IslandNear(x, z);
            float rich = Mathf.Clamp01(0.45f + 0.5f * Mathf.Clamp01((edge - 0.55f) * 2.5f) + 0.4f * near) * (0.55f + 0.9f * Mathf.PerlinNoise(x * 0.11f + 70f, z * 0.11f + 30f));
            // pebble cluster: a few stones of very different size huddled together
            if (rng.NextDouble() < 0.1f * rich)   // 11-13 r5 (Kido: riverbed gravel): was 0.75 x 2-8 stones, ~1700 in all
            {
                int n = rng.Next(1, 4);
                for (int i = 0; i < n; i++)
                {
                    float px = x + R(-1.1f, 1.1f), pz = z + R(-1.1f, 1.1f);
                    // 11-13 (Kido: "that stone looks weird"): scaled 4-7x the 5-10 cm scans smeared into crumpled-plastic
                    // blobs; now at most ~2.5x, so they stay real-looking pebbles and small stones
                    float s = R(1.3f, 2.4f) * (rng.NextDouble() < 0.15 ? 1.3f : 1f);   // pack stones are 5-10 cm at scale 1
                    if (PlacePart(rng.NextDouble() < 0.6 ? "MS:Pebbles/AV_MS_Pebbles" : "MS:SmallRocks/AV_MS_SmallRocks", new Vector3(px, G(px, pz) - 0.03f, pz), R(0, 360), s, floor)) nPeb++;
                }
            }
            if (false && rng.NextDouble() < 0.18f * rich)   // 11-13: big upscaled single rocks off (read as grey plastic bags)
            {
                float px = x + R(-1.5f, 1.5f), pz = z + R(-1.5f, 1.5f);
                if (PlacePart("MS:SmallRocks/AV_MS_SmallRocks", new Vector3(px, G(px, pz) - 0.06f, pz), R(0, 360), R(4f, 7f), floor)) { nRock++; }
            }
            if (rng.NextDouble() < 0.25f * rich) /* 11-13 r8: 0.4 -> 0.25, yellow specks popped */ { Decal(floor, leaves, x + R(-1, 1), z + R(-1, 1), R(2f, 4f), G, 0.015f); nLeaf++; }
            // 11-13 r8 (Kido: the rubble patches read as stacked boxes): a few half-buried moss rocks instead, sparse
            if (rng.NextDouble() < 0.02f + 0.03f * near) { if (HalfRock(new Vector3(x, G(x, z), z), R(0.5f, 1.2f), floor)) nPatch++; }
            // abandoned household things (KHS scans, CC BY): straw mats lying flat, a few jars knocked over, straw lids
            if (rng.NextDouble() < 0.035f + 0.05f * near)
            {
                double k = rng.NextDouble();
                if (k < 0.0) { var m = Place("MS:KStrawMat/AV_MS_KStrawMat", new Vector3(x, G(x, z) + 0.01f, z), R(0, 360), R(0.8f, 1.1f), floor); if (m && nKhs++ == 0) Debug.Log($"[AV] straw mat {ModelBounds(m).size}"); }
                else if (k < 0.8)
                    for (int i = 1; i > 0; i--)   /* 11-13: single jars only, no heaps */
                    {
                        float px = x + R(-1.2f, 1.2f), pz = z + R(-1.2f, 1.2f);
                        var j = Place("MS:KJar/AV_MS_KJar", new Vector3(px, G(px, pz) - 0.05f, pz), R(0, 360), R(0.8f, 1.1f), floor);
                        if (j && rng.NextDouble() < 0.85) j.transform.rotation = Quaternion.Euler(R(75, 100), R(0, 360), 0);   // knocked over
                        nKhs++;
                    }
                else { Place("MS:KStrawJar/AV_MS_KStrawJar", new Vector3(x, G(x, z) - 0.02f, z), R(0, 360), R(0.9f, 1.2f), floor); nKhs++; }
            }
            // blood: gory pools, sometimes a drag trail leading off; at most one small handprint each (11-13 r8)
            if (InArena(x, z, 1f) && rng.NextDouble() < 0.05f + 0.06f * near)
            {
                bool trail = rng.NextDouble() < 0.18;
                BloodPool(floor, bloods, x, z, R(1.6f, 2.6f), G, !trail && rng.NextDouble() < 0.3);
                if (trail) { float a = R(0, 360) * Mathf.Deg2Rad; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); DragTrail(floor, bloods, new Vector2(x, z) + d * 0.6f, d, R(2f, 3.5f), G, rng.NextDouble() < 0.5); }
                nBlood++;
            }
        }
        BuildVignettes(G, bloods, leaves, floor);   // 11-12: composed scenes (household spill, carnage, dead campfire, offering)
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"),
            $"FLOOR pebbles={nPeb} rocks={nRock} leaves={nLeaf} blood={nBlood} patches={nPatch} khs={nKhs}\n");
    }
}
