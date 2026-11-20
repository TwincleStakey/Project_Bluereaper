// Redo 2 — architecture / landmark props (models: BlenderSource~/arch/build_arch.py -> Models/Arch/SP_Arch_*.fbx).
// Arched walkway-bridge with dull red railings and a one-pillar-row temple gate on the north edge, stone walls with
// tile caps on the east/west borders, three tall lit stone lanterns near the edge, scanned rock faces on the cliff lip
// and framing the camera-side corners. Everything sits outside |x|<56, |z|<44 except the lanterns (x ~ +-50, no
// colliders: Place strips them). Lantern spots go into archLanterns for the lead to merge into SPField.lanterns.
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class SPBuilder
{
    static readonly System.Collections.Generic.List<Light> archLanterns = new System.Collections.Generic.List<Light>();
    const string ArchTex = Root + "/Textures/Arch/";
    const string ArchModels = Root + "/Models/Arch";
    static readonly string[] ArchSlots = { "SP_Arch_RedWood", "SP_Arch_Timber", "SP_Arch_Deck", "SP_Arch_RoofTile", "SP_Arch_WallStone", "SP_Arch_Rock01", "SP_Arch_Rock02" };

    static partial void BuildArchitecture()
    {
        archLanterns.Clear();
        ArchMaterials();
        ArchRemap();
        var g = Group("Architecture");

        // ---- north edge: the temple gate (12.6 x 6 m, 8.4 m tall). Layout pass (11-11, Kido: the bridge and gate must
        // show at the game camera): the gate straddles the north-east footpath right at the edge (z 46.6, front legs
        // ~0.5 m past the line), flanked by two tall lanterns just inside. The self-made walkway bridge is gone: the
        // Megascans arched bridges cross the frozen river where it leaves the arena (SPBuilder.Megascans.cs).
        Place("Arch/SP_Arch_Gate", 42f, 46.6f, 0f, 1f, g, false, 0.08f);

        // ---- east / west borders: stone walls with tile caps, outside the edge (x = +-57.6), gaps where it fell
        // (z, piece): S straight, B broken, - gap
        var run = new (float z, char k)[]
        {
            (-34f, 'S'), (-30f, 'S'), (-26f, 'B'), (-18f, 'S'), (-14f, 'S'), (-10f, 'S'), (-6f, 'B'),
            (6f, 'B'), (10f, 'S'), (14f, 'S'), (18f, 'S'), (22f, 'S'), (30f, 'B'), (34f, 'S'), (38f, 'S'),
        };
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (HX + 1.6f);
            foreach (var (z, k) in run)
            {
                float zz = z + (side > 0 ? 2f : 0f);   // the east run is offset so the two sides don't mirror
                if (zz > 37f) continue;
                // broken pieces: alternate which end has collapsed
                float yaw = 90f + (k == 'B' && ((int)z / 4) % 2 == 0 ? 180f : 0f);
                float jx = R(-0.08f, 0.08f), jy = R(-1.5f, 1.5f);   // drawn first so the rng stream stays the same when a piece is skipped
                if (DistanceToRiver(x, zz) < 8f) continue;          // the river (and its bridge) passes through the wall line
                // tr44 (Kido: 1자돌 이상해): the evenly spaced side-wall run is gone (rng draws above kept); BorderRocks puts irregular rock clusters along the edges
            }
            // corner at the north end: one arm continues the side wall south, the other turns outward (away from the arena)
            var c = (GameObject)null;   // tr44: corner piece dropped with the runs
            if (c) Orient(c, new Vector3(side, 0f, -1f));
        }

        // ---- tall lit lanterns: west edge, east edge, and one at the gate approach
        void Lamp(string m, float x, float z, float yaw)
        {
            var go = Place("Arch/" + m, x, z, yaw, 1f, g, true, 0.06f);
            var l = Lantern(go, false);
            if (l) archLanterns.Add(l);
        }
        Lamp("SP_Arch_Lantern_A", -50.5f, 18f, 8f);
        Lamp("SP_Arch_Lantern_B", 50.5f, -8f, -6f);
        Lamp("SP_Arch_Lantern_A", 34.6f, 43.0f, 3f);    // flanking the gate, inside the edge
        Lamp("SP_Arch_Lantern_A", 49.4f, 43.0f, -4f);

        // ---- rock faces: cliff lip (north drop) and the camera-side corners
        var lip = new[] { new Vector3(-50f, 52.6f, 1.2f), new Vector3(-35f, 53.4f, 1.0f), new Vector3(-2f, 52.8f, 1.3f), new Vector3(14f, 53.2f, 1.1f), new Vector3(58f, 51.5f, 1.25f), new Vector3(-64f, 47f, 1.3f), new Vector3(66f, 44f, 1.2f) };
        for (int i = 0; i < lip.Length; i++)
            Place(i % 2 == 0 ? "Arch/SP_Arch_RockFace_A" : "Arch/SP_Arch_RockFace_B", lip[i].x, lip[i].y, R(0, 360), lip[i].z, g, false, 0.3f);
        Place("Arch/SP_Arch_RockFace_B", -61f, -50f, 35f, 1.2f, g, false, 0.3f);
        Place("Arch/SP_Arch_RockFace_A", 61f, -51f, 210f, 1.15f, g, false, 0.3f);

        Log($"ARCH placed {g.childCount} pieces, lanterns={archLanterns.Count}");
    }

    // Turns a piece (yaw 0/90/180/270) so its bounds lean toward dir (corner wall arms).
    static void Orient(GameObject go, Vector3 dir)
    {
        if (!go) return;
        float best = -1e9f, bestYaw = 0f;
        var p = go.transform.position;
        for (int k = 0; k < 4; k++)
        {
            go.transform.rotation = Quaternion.Euler(0, k * 90f, 0);
            var b = new Bounds(p, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var d = b.center - p; d.y = 0;
            float s = Vector3.Dot(d, dir);
            if (s > best) { best = s; bestYaw = k * 90f; }
        }
        go.transform.rotation = Quaternion.Euler(0, bestYaw, 0);
    }

    static void ArchMaterials()
    {
        Texture2D A(string n) => Tex(ArchTex + n + "_diff_1k.jpg", false, false, false, 1024);
        Texture2D N(string n) => Tex(ArchTex + n + "_nor_gl_1k.jpg", true, false, false, 1024);
        // dull, weathered lacquer red (#8e1a17 blood stays the only saturated red)
        Prop("SP_Arch_RedWood", A("rough_wood"), N("rough_wood"), Hex("#7a3128"), 0.15f, 0.2f);
        Prop("SP_Arch_Timber", A("rough_wood"), N("rough_wood"), Hex("#6f675f"), 0.12f, 0.45f);
        Prop("SP_Arch_Deck", A("weathered_planks"), N("weathered_planks"), Hex("#8a847c"), 0.12f, 0.45f);
        Prop("SP_Arch_RoofTile", A("roof_tiles_14"), N("roof_tiles_14"), Hex("#6b7178"), 0.15f, 0.8f);
        Prop("SP_Arch_WallStone", A("japanese_stone_wall"), N("japanese_stone_wall"), Hex("#a3a7ab"), 0.12f, 0.45f);
        Prop("SP_Arch_Rock01", A("rock_face_01"), N("rock_face_01"), Hex("#a6abb2"), 0.1f, 0.6f);
        Prop("SP_Arch_Rock02", A("rock_face_02"), N("rock_face_02"), Hex("#a6abb2"), 0.1f, 0.6f);
        AssetDatabase.SaveAssets();
    }

    // RemapModels already maps the kit slots (SP_Stone, SP_StoneLantern, SP_LanternGlow) under Models/; add ours.
    static void ArchRemap()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArchModels }))
        {
            var imp = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ModelImporter;
            if (!imp) continue;
            bool changed = false;
            if (imp.materialImportMode != ModelImporterMaterialImportMode.ImportStandard) { imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            var map = imp.GetExternalObjectMap();
            foreach (var n in ArchSlots.Concat(KitSlots))
            {
                if (!mats.TryGetValue(n, out var m) || !m) continue;
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (map.TryGetValue(id, out var cur) && cur == m) continue;
                imp.AddRemap(id, m); changed = true;
            }
            if (changed) imp.SaveAndReimport();
        }
    }
}
