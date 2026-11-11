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
            bloods[q] = DecalMat("AV_BloodDecal_" + q, MS + "Decals/" + bt[q], q == 0 ? new Color(0.45f, 0.12f, 0.1f, 1f) : new Color(0.85f, 0.8f, 0.8f, 1f), 0.88f, true);
            bloods[q].SetTextureScale("_BaseMap", Vector2.one); bloods[q].SetTextureOffset("_BaseMap", Vector2.zero);
            Emissive(bloods[q], new Color(0.12f, 0.008f, 0.006f));   // just enough red to read on the near-black floor (refs: red only on blood)
            if (bn[q] != null) { bloods[q].SetTexture("_BumpMap", TP(MS + "Decals/" + bn[q], true)); bloods[q].SetFloat("_BumpScale", 0.8f); bloods[q].EnableKeyword("_NORMALMAP"); }
        }
        var leaves = DecalMat("AV_LeafDecal", MS + "Decals/AV_MS_Leaves_RGBA.png", new Color(0.72f, 0.64f, 0.56f, 1f), 0.08f, false);
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
            if (rng.NextDouble() < 0.75f * rich)
            {
                int n = rng.Next(2, 8);
                for (int i = 0; i < n; i++)
                {
                    float px = x + R(-1.1f, 1.1f), pz = z + R(-1.1f, 1.1f);
                    float s = R(2.6f, 5.2f) * (rng.NextDouble() < 0.15 ? 1.8f : 1f);   // pack stones are 5-10 cm at scale 1
                    if (PlacePart(rng.NextDouble() < 0.6 ? "MS:Pebbles/AV_MS_Pebbles" : "MS:SmallRocks/AV_MS_SmallRocks", new Vector3(px, G(px, pz) - 0.03f, pz), R(0, 360), s, floor)) nPeb++;
                }
            }
            if (rng.NextDouble() < 0.18f * rich)
            {
                float px = x + R(-1.5f, 1.5f), pz = z + R(-1.5f, 1.5f);
                if (PlacePart("MS:SmallRocks/AV_MS_SmallRocks", new Vector3(px, G(px, pz) - 0.06f, pz), R(0, 360), R(4f, 7f), floor)) { nRock++; }
            }
            if (rng.NextDouble() < 0.5f * rich) { Decal(floor, leaves, x + R(-1, 1), z + R(-1, 1), R(2f, 4f), G, 0.015f); nLeaf++; }
            if (rng.NextDouble() < 0.05f + 0.06f * near)
            {
                var p = Place("MS:RubblePatch/AV_MS_RubblePatch", new Vector3(x, G(x, z) - 0.12f, z), R(0, 360), R(0.6f, 1f), floor);
                if (p) { foreach (var r in p.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off; nPatch++; if (nPatch == 1) Debug.Log($"[AV] rubble patch {ModelBounds(p).size}"); }
            }
            // abandoned household things (KHS scans, CC BY): straw mats lying flat, a few jars knocked over, straw lids
            if (rng.NextDouble() < 0.035f + 0.05f * near)
            {
                double k = rng.NextDouble();
                if (k < 0.4) { var m = Place("MS:KStrawMat/AV_MS_KStrawMat", new Vector3(x, G(x, z) + 0.01f, z), R(0, 360), R(0.8f, 1.1f), floor); if (m && nKhs++ == 0) Debug.Log($"[AV] straw mat {ModelBounds(m).size}"); }
                else if (k < 0.8)
                    for (int i = rng.Next(1, 4); i > 0; i--)
                    {
                        float px = x + R(-1.2f, 1.2f), pz = z + R(-1.2f, 1.2f);
                        var j = Place("MS:KJar/AV_MS_KJar", new Vector3(px, G(px, pz) - 0.05f, pz), R(0, 360), R(0.8f, 1.1f), floor);
                        if (j && rng.NextDouble() < 0.6) j.transform.rotation = Quaternion.Euler(R(75, 100), R(0, 360), 0);   // knocked over
                        nKhs++;
                    }
                else { Place("MS:KStrawJar/AV_MS_KStrawJar", new Vector3(x, G(x, z) - 0.02f, z), R(0, 360), R(0.9f, 1.2f), floor); nKhs++; }
            }
            // blood: dark wet pools with smaller splashes around, a bit more near the ruins
            if (InArena(x, z, 1f) && rng.NextDouble() < 0.05f + 0.06f * near)
            {
                Decal(floor, bloods[rng.Next(2) == 0 ? 2 : 3], x, z, R(1.6f, 3f), G, 0.03f);   // top row of the atlas (v 0.5-1) = big splashes
                for (int i = rng.Next(1, 4); i > 0; i--) Decal(floor, bloods[rng.Next(2)], x + R(-1.8f, 1.8f), z + R(-1.8f, 1.8f), R(0.6f, 1.4f), G, 0.031f);
                nBlood++;
            }
        }
        BuildVignettes(G, bloods, leaves, floor);   // 11-12: composed scenes (household spill, carnage, dead campfire, offering)
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "ArtSource/AbandonedVillage/av_log.txt"),
            $"FLOOR pebbles={nPeb} rocks={nRock} leaves={nLeaf} blood={nBlood} patches={nPatch} khs={nKhs}\n");
    }
}
