// 11-12 (Kido, refs: Tarae thatch eave over the frame, POE2 palisade village, dark tangled trees at the bottom edge):
// give the 폐촌 vertical volume. The camera looks north from ~23 m behind and 27 m above the player, so whatever stands
// on the south side of the arena shows at the bottom of the frame. The south side now gets a raised stone bank with a
// row of tall 2-3 storey houses and hanok roofs, and big dead trees standing in front of them; the east, west and north
// edges get big trees between the existing house rows. Nothing here is inside the arena.
using UnityEngine;

public static partial class AVBuilder
{
    const float BankZ0 = -51.6f, BankZ1 = -52.4f;   // ramp up to the bank (mirrors the north terrace)
    const float BankY = 2.6f;
    const float SouthGateX = -17.6f;                // the street leaves through the south wall here

    public static float SouthBank(float x, float z)
    {
        if (z > BankZ0) return 0f;
        float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BankZ0, BankZ1, z));
        float gate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Abs(x - SouthGateX) - 3.5f) / 3f));   // street cut
        return BankY * up * gate;
    }

    // foreground trees: no shadows (60 big branchy casters cost ~40 FPS in the editor, 11-12)
    static void NoShadow(GameObject g) { if (g) foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

    static readonly System.Collections.Generic.Dictionary<Material, Material> silMats = new System.Collections.Generic.Dictionary<Material, Material>();
    static void Silhouette(GameObject go)
    {
        if (!go) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                var src = ms[i]; if (!src) continue;
                if (!silMats.TryGetValue(src, out var m) || !m)
                {
                    var path = AVTreeMats + "/AV_Sil_" + src.name.Replace("AV_", "") + ".mat";
                    m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!m) { m = new Material(src); UnityEditor.AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
                    if (m.HasProperty("_Tint")) m.SetColor("_Tint", src.GetColor("_Tint") * new Color(0.13f, 0.12f, 0.12f, 1f));
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor") * new Color(0.13f, 0.12f, 0.12f, 1f));
                    UnityEditor.EditorUtility.SetDirty(m);
                    silMats[src] = m;
                }
                ms[i] = m;
            }
            r.sharedMaterials = ms;
        }
    }

    static void BuildSouthVolume(System.Func<float, float, float> G, Transform bld)
    {
        var vol = Group("Volume");
        // retaining wall along the bank face (the kit wall faces -Z; turned to face the arena)
        for (float x = -74f; x <= 74f; x += 5.95f)
        {
            if (Mathf.Abs(x - SouthGateX) < 5.5f) continue;
            Place("AV_RetainingWall", new Vector3(x, -0.25f, BankZ0 + 0.75f), 180f + R(-1.2f, 1.2f), 1f, vol);
        }
        // 11-12 r2: the camera sits right above the south bank when the player is at the south edge, so tall houses there
        // filled half the frame and hid the player. The bank keeps only low thatch eaves (4-5 m) whose roof edges show
        // at the bottom of the frame like the Tarae ref; the tall 2-3 storey houses stand behind the north terrace instead,
        // where the camera sees their height from the side.
        string[] low = { "AV_Choga_A", "AV_ChogaCollapsed", "AV_Choga_B", "AV_Choga_C", "AV_ChogaCollapsed" };
        int i = 0;
        for (float x = -70f; x <= 70f; x += R(9f, 13f))
        {
            if (Mathf.Abs(x - SouthGateX) < 7f) continue;
            float z = R(-61f, -58.5f);
            Place(low[i++ % low.Length], new Vector3(x, G(x, z) - 0.1f, z), 180f + R(-12f, 12f), R(0.95f, 1.1f), bld);
        }
        string[] tall = { "AV_Tall3F_A", "AV_Hanok2F_B", "AV_Tall3F_Lean", "AV_Tall2F_Broken", "AV_Hanok2F_C", "AV_Tall3F_Broken" };
        for (float x = -52f; x <= 52f; x += R(10f, 13f))
        {
            if (Mathf.Abs(x) < 7f) continue;   // keep the big hanok on the axis free
            float z = R(63f, 66f);
            Place(tall[i++ % tall.Length], new Vector3(x, G(x, z) - 0.05f, z), R(-6f, 6f), R(1.15f, 1.35f), bld);
        }
        // 11-13 r8 (Kido, Hades 2 ref: very tall, almost black things along the bottom edge for depth, outside the fight):
        // big free scan snags and dead firs (snow thread's import) at 2x on the south bank, drawn as near-black
        // silhouettes, see-through near the player. They only show at the bottom of the frame near the south edge.
        string[] sil = { "TR_Pine", "TR_Fir", "TR_NobleFir" };   // 11-14: no broken-topped snags
        int si = 0;
        for (float x = -66f; x <= 66f; x += R(7f, 11f))
        {
            if (Mathf.Abs(x - SouthGateX) < 4f) continue;
            float z = R(-58f, -53.5f);
            var t = ClearOfHouses(BareTree(new Vector3(x, G(x, z) - 0.2f, z), R(1.5f, 2.0f), vol, true)); si++;
            Silhouette(t);
        }
        // tree line between the side house rows and along the north terrace edge
        foreach (float sx in new[] { -1f, 1f })
            for (float z = -44f; z <= 40f; z += R(11f, 16f))
            {
                float x = sx * R(47.6f, 49.2f);
                DeadScan(new Vector3(x, G(x, z) - 0.15f, z), R(1.3f, 1.8f), vol);
            }
        for (float x = -40f; x <= 40f; x += R(14f, 20f))
        {
            if (Mathf.Abs(x) < 6f) continue;   // the steps
            float z = R(43.2f, 44.2f);
            DeadScan(new Vector3(x, G(x, z) - 0.15f, z), R(1.3f, 1.7f), vol);
        }
        // the bank itself is overgrown: big dry-grass and shrub masses along its top and foot
        for (float x = -70f; x <= 70f; x += R(3f, 6f))
        {
            Clump(G, vol, new Vector2(x, R(-55.5f, -52.8f)), R(1.2f, 3f), rng.Next(6, 14), R(1.2f, 2f));
            if (rng.NextDouble() < 0.5) Clump(G, vol, new Vector2(x + R(-2, 2), R(-51.3f, -50.4f)), R(0.6f, 1.6f), rng.Next(3, 8), R(0.8f, 1.3f));
        }
    }
}
