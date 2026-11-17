// 11-17 대전 앞 staged the Diablo IV way (Kido: "아직도휑해 디아식으로 스테이지해봐"; refs + analysis in
// C:/Users/PC/Downloads/palace_stage, ArtSource/PalaceStage). What the refs do, applied here:
//   1. the floor is never one value: soft mud / leaf / ash / dust patches over the stone scan (2-3 value steps), denser
//      and darker toward the walls, lighter dust drifts in the open lanes
//   2. the centre holds only a few flat "story" vignettes (fallen 착호갑사 under straw mats, a burnt 행각 that spilled its
//      roof tiles, charred yokai with scorch, a broken supply train) with wide clear lanes between them
//   3. the edges are dense: a cluster every ~5 m along the walls (1 big + 2-3 mid + debris) so the walls sit on rubble
//   4. warm real light only at a few fires (all lights in the field <= 8), smoke and sparks from the downloaded flipbook
// Everything is a downloaded asset baked by Editor/PalaceStageBake.cs (credits Assets/Game/RoomRun/CREDITS.txt) or the
// 11-16 PP_* heritage props. Nothing here has a collider; small pieces cast no shadow; nothing tall stands in the centre.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    public static class PalaceStage
    {
        public static bool Enabled => PlayerPrefs.GetInt("RR.PalaceStage", 1) != 0;
        const int MaxLights = 8;

        static Transform root; static Vector3 off; static float baseY; static System.Random rng;
        static int count, lights;
        static Mesh quad;
        static readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();

        static T Load<T>(string path) where T : Object
        {
            if (!cache.TryGetValue(path, out var o) || !o) { o = Resources.Load<T>(path); cache[path] = o; }
            return o as T;
        }
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static bool Chance(double p) => rng.NextDouble() < p;

        public static int Build(Vector3 centre, Transform parent)
        {
            if (!Enabled) return 0;
            var holder = new GameObject("PalaceStage");
            holder.transform.SetParent(parent, false);
            root = holder.transform; off = new Vector3(centre.x, 0f, centre.z); baseY = centre.y;
            rng = new System.Random(1117); count = 0;
            // light budget: what the field already has (gallery fires etc.)
            lights = 0;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.enabled && l.type != LightType.Directional && l.gameObject.scene == parent.gameObject.scene) { lights++; Debug.Log($"[PalaceStage]   light {l.name} {l.type} at {l.transform.position} i{l.intensity:0.0} r{l.range:0}"); }
            int had = lights;

            GroundPatches();
            FallenGuards(new Vector2(-21f, -15f));
            BurntGallery(new Vector2(45f, -21f));
            YokaiScorch(new Vector2(25f, 24f));
            SupplyTrain(new Vector2(-34f, 30f));
            Ambush(new Vector2(6f, -33f));
            EdgeBands();
            Fires();
            Debug.Log($"[PalaceStage] {count} pieces, lights {had} -> {lights} (max {MaxLights})");
            return count;
        }

        // ------------------------------------------------------------------ placing
        static float GroundAt(float x, float z)
        {
            var p = new Vector3(x, 0, z) + off;
            var hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - baseY) < 4f && h.point.y < best && !h.collider.GetComponentInParent<PlayerController>()) best = h.point.y;
            return best < float.MaxValue ? best : baseY;
        }

        static Mesh Quad()
        {
            if (quad) return quad;
            quad = new Mesh { name = "PS_Quad" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) });
            quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            quad.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            quad.SetTangents(new List<Vector4> { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) });
            quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            quad.RecalculateBounds();
            return quad;
        }

        // a flat textured quad on the floor (ground patch or decal); lift keeps the layers apart
        static void Flat(string mat, float x, float z, float sx, float sz, float yaw, float lift)
        {
            var m = Load<Material>("PalaceStage/" + mat);
            if (!m) { Debug.LogWarning("[PalaceStage] no material " + mat); return; }
            var go = new GameObject(mat);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) + lift, z) + off, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = new Vector3(sx, 1f, sz);
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            count++;
        }
        static void Patch(string kind, float x, float z, float size, float aspect = 1f)
            => Flat($"PS_G_{kind}_{rng.Next(3)}", x, z, size * aspect, size / aspect, R(0, 360), kind == "Dust" ? 0.012f : kind == "Leaves" ? 0.03f : 0.02f);
        static void Blood(int q, float x, float z, float size, float stretch = 1f, float yaw = float.NaN)
            => Flat("PS_BloodDecal_" + q, x, z, size * stretch, size, float.IsNaN(yaw) ? R(0, 360) : yaw, 0.04f);

        // a prop: "PS_*" (PalaceStage) or "PP_*" (EdgeFeatures heritage set); lay = tip over about X (deg), tilt about Z
        static GameObject Prop(string what, float x, float z, float yaw, float s = 1f, float lay = 0f, float tilt = 0f, float sink = 0.02f)
        {
            var prefab = Load<GameObject>((what.StartsWith("PP_") ? "EdgeFeatures/" : "PalaceStage/") + what);
            if (!prefab) { Debug.LogWarning("[PalaceStage] no prefab " + what); return null; }
            var go = Object.Instantiate(prefab, root);
            go.name = what;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lay, 0f, tilt);
            go.transform.localScale = Vector3.one * s;
            float gy = GroundAt(x, z);
            go.transform.position = new Vector3(x, gy, z) + off;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return go;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += Vector3.up * (gy - b.min.y - sink * Mathf.Max(1f, b.size.y));
            bool small = b.size.y < 0.7f;
            foreach (var r in rs) { if (small) r.shadowCastingMode = ShadowCastingMode.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            count++;
            return go;
        }
        static GameObject Corpse(string what, float x, float z, float yaw, float s = 1f)
        {
            // HellFX/Corpse_* (burnt, posed lying, Underworld) or the straw-mat covered PS_Corpse*
            s *= 1.25f;   // 11-17 r3: real size read as a small lump from the game camera
            if (!what.StartsWith("Corpse_")) return Prop(what, x, z, yaw, s, 0f, 0f, 0.05f);
            var prefab = Load<GameObject>("HellFX/" + what);
            if (!prefab) return null;
            var go = Object.Instantiate(prefab, root);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            go.transform.localScale *= s;
            float gy = GroundAt(x, z);
            go.transform.position = new Vector3(x, gy, z) + off;
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); go.transform.position += Vector3.up * (gy - b.min.y - 0.04f); }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            count++;
            return go;
        }

        // n pieces of the given kinds strewn around (x,z): denser toward the middle, sizes spread
        static void Strew(string[] kinds, float x, float z, float radius, int n, float sMin, float sMax, bool tumble = true, float dirYaw = float.NaN, float stretch = 1f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = R(0, Mathf.PI * 2), d = radius * Mathf.Pow((float)rng.NextDouble(), 0.75f);
                float px = Mathf.Cos(a) * d, pz = Mathf.Sin(a) * d;
                if (!float.IsNaN(dirYaw)) { pz *= 1f / stretch; px *= stretch; var q = Quaternion.Euler(0, dirYaw, 0) * new Vector3(px, 0, pz); px = q.x; pz = q.z; }
                Prop(kinds[rng.Next(kinds.Length)], x + px, z + pz, R(0, 360), R(sMin, sMax), tumble ? R(-25f, 25f) : 0f, tumble ? R(-25f, 25f) : 0f);
            }
        }
        // a bundle of 5-10 spears dropped together (single spears vanish from this camera)
        static void SpearBundle(float x, float z, float yaw, int n)
        {
            for (int i = 0; i < n; i++)
                Prop("PP_Spear", x + R(-0.5f, 0.5f), z + R(-0.5f, 0.5f), yaw + R(-14f, 14f), 1.45f, 0f, R(-3f, 3f));
        }

        static readonly string[] TileBits = { "PS_Tile", "PS_Tile", "PS_Tile", "PS_TileBird", "PS_TileFace" };
        static readonly string[] Stones = { "PS_Rocks", "PS_Pebbles", "PS_Rocks" };
        static readonly string[] Wood = { "PS_Debris", "PS_Sticks", "PS_Planks" };

        // ------------------------------------------------------------------ 1. floor value
        static bool Reserved(float x, float z) =>
            new Vector2(x, z).magnitude < 3.5f;                       // spawn spot stays plain

        static void GroundPatches()
        {
            // 11-17 r7 (d4-style-env-art: no noise / random placement, hand-composed per screen): every patch is placed by
            // hand around the landmarks and along the walls. Mud and leaves pile at the wall feet and corners, ash spreads
            // from the burnt places, a few long dust drifts cross the open lanes (lighter step), and the lanes between keep
            // plain stone as rest areas. kind, x, z, size, aspect
            var list = new (string k, float x, float z, float s, float a)[]
            {
                // west wall feet (leaves blown in, wet mud in the corners)
                ("Leaves", -47f, -40f, 9f, 1.4f), ("Mud", -49f, -31f, 7f, 0.7f), ("Leaves", -47f, -20f, 8f, 1.2f), ("Mud", -48f, -9f, 6f, 0.8f),
                ("Leaves", -47f, 18f, 8f, 0.8f), ("Mud", -48f, 27f, 7f, 1.2f), ("Leaves", -46f, 37f, 9f, 1.3f), ("Mud", -50f, 42f, 6f),
                // east wall feet
                ("Mud", 48f, -41f, 8f, 1.3f), ("Leaves", 47f, -32f, 7f, 0.8f), ("Leaves", 47f, 19f, 8f, 1.2f), ("Mud", 48f, 28f, 7f, 0.8f),
                ("Leaves", 46f, 38f, 9f, 1.2f), ("Mud", 49f, 6f, 5f, 1.4f),
                // north wall, both sides of the hall approach
                ("Leaves", -38f, 41f, 9f, 1.5f), ("Mud", -29f, 42f, 6f, 1.3f), ("Leaves", 33f, 41f, 9f, 1.5f), ("Mud", 41f, 43f, 6f),
                ("Dust", -8f, 33f, 9f, 0.6f), ("Dust", 8f, 33f, 9f, 0.6f),
                // south (camera) edge
                ("Mud", -36f, -46f, 8f, 1.6f), ("Leaves", -18f, -46f, 7f, 1.4f), ("Mud", 20f, -46f, 8f, 1.5f), ("Leaves", 38f, -45f, 7f, 1.3f),
                // ash spreading out of the burnt gallery and the yokai scorch
                ("Ash", 38f, -22f, 9f, 1.4f), ("Ash", 33f, -18f, 6f), ("Ash", 30f, 20f, 7f), ("Ash", 19f, 28f, 6f, 1.2f),
                // mud trampled where the guards fell and along the south lane
                ("Mud", -26f, -19f, 7f, 1.2f), ("Mud", -15f, -10f, 5f), ("Mud", 0f, -36f, 6f, 1.3f), ("Mud", 11f, -29f, 5f),
                // supply train: spilled straw / leaves
                ("Leaves", -38f, 26f, 6f), ("Leaves", -28f, 33f, 6f, 1.2f),
                // long dust drifts across the open lanes (a lighter step), rest lanes left plain between them
                ("Dust", -30f, 2f, 13f, 1.8f), ("Dust", 28f, -4f, 12f, 1.6f), ("Dust", -6f, 18f, 12f, 1.7f), ("Dust", 4f, -18f, 11f, 1.5f),
                ("Mud", -36f, 10f, 5f), ("Leaves", 36f, 8f, 5f, 1.3f),
            };
            foreach (var p in list) Patch(p.k, p.x, p.z, p.s, p.a);
            // the rank-stone rows: dirt gathered at the stones' feet, every other stone
            for (int side = -1; side <= 1; side += 2)
                foreach (float z in new[] { -32f, -18f, -4f, 14f, 27f }) Patch(side < 0 ? "Mud" : "Leaves", side * 12f, z + side * 2f, 4f, 0.7f);
        }

        // ------------------------------------------------------------------ 2. centre vignettes (flat)
        // fallen 착호갑사: three bodies under straw mats where the line broke, a wide pool and drag marks, their swords
        // and a dropped spear bundle, a spilled sack
        static void FallenGuards(Vector2 c)
        {
            Patch("Mud", c.x, c.y, 9f, 1.3f);
            Blood(2, c.x + 0.5f, c.y - 0.3f, 4.2f);
            Blood(1, c.x - 2.6f, c.y + 1.8f, 2.6f);
            Blood(0, c.x + 3.2f, c.y + 1.4f, 2.2f, 2.4f, 25f);           // dragged
            Corpse("PS_CorpseCovered", c.x - 1.2f, c.y + 0.6f, 75f);
            Corpse("PS_CorpseCloth", c.x + 1.4f, c.y - 1.4f, 160f);
            Corpse("PS_CorpseCovered", c.x + 4.6f, c.y + 2.1f, 30f, 0.95f);
            Prop("PP_Hwando", c.x - 3.2f, c.y - 1.6f, 40f, 1.6f, 90f);
            Prop("PP_Woldo", c.x + 0.4f, c.y + 2.9f, 110f, 1.5f);
            SpearBundle(c.x - 4.6f, c.y + 2.6f, 15f, 6);
            Prop("PS_Sack", c.x + 3f, c.y - 2.6f, 40f, 1f, 80f);
            Strew(new[] { "PS_Pebbles", "PS_Sticks" }, c.x, c.y, 4.5f, 6, 0.7f, 1.1f);
        }

        // a burnt 행각 bay fell in on the east side: rubble and charred beams against the wall, its roof tiles fanned out
        // into the court (dense at the wall, thinning toward the middle), ash under it all, still burning (see Fires)
        static void BurntGallery(Vector2 c)
        {
            Patch("Ash", c.x - 1f, c.y, 11f, 1.2f); Patch("Ash", c.x - 7f, c.y + 2f, 7f);
            Prop("PS_Rubble", c.x + 1.5f, c.y - 1.5f, 20f, 1.15f);
            Prop("PS_Rubble", c.x - 1.5f, c.y + 2.5f, 200f, 1.0f);
            Prop("PS_Rubble", c.x + 0.5f, c.y + 6f, 100f, 0.9f);
            for (int i = 0; i < 6; i++) Prop("PP_Beam", c.x + R(-3f, 2f), c.y + R(-4f, 6f), R(0, 180), R(2.4f, 3f), 0f, R(-6f, 6f));
            Prop("PS_Planks", c.x - 3.5f, c.y - 2.5f, 70f, 1.1f);
            Prop("PS_Planks", c.x - 2.5f, c.y + 4.5f, 140f, 1f);
            // the tile spill: a fan toward -x (into the court)
            Strew(TileBits, c.x - 3f, c.y + 1f, 3.5f, 26, 0.9f, 1.3f);
            Strew(TileBits, c.x - 8f, c.y + 1f, 4.5f, 16, 0.9f, 1.2f, true, 0f, 1.6f);
            Strew(TileBits, c.x - 13f, c.y + 0.5f, 3.5f, 6, 0.9f, 1.1f);
            Strew(Stones, c.x - 4f, c.y, 6f, 10, 0.8f, 1.3f);
            Strew(Wood, c.x - 2f, c.y + 1f, 5f, 6, 0.7f, 1f);
        }

        // charred yokai where the fire caught them: burnt bodies, a scorch and spatter, bones
        static void YokaiScorch(Vector2 c)
        {
            Patch("Ash", c.x, c.y, 10f, 0.9f);
            Patch("Mud", c.x + 4f, c.y - 3f, 5f);
            Corpse("Corpse_Back", c.x - 1.5f, c.y + 0.5f, 30f, 1.1f);
            Corpse("Corpse_Side", c.x + 1.8f, c.y - 0.8f, 200f, 1.1f);
            Corpse("Corpse_Front", c.x + 0.2f, c.y + 3f, 120f, 1.05f);
            Blood(1, c.x + 2.5f, c.y + 1.5f, 3f); Blood(3, c.x - 2.5f, c.y - 1.8f, 2.4f);
            Prop("PS_Skulls", c.x - 3.5f, c.y + 2.6f, 60f, 1f);
            Prop("PS_Skulls", c.x + 3.6f, c.y - 2.8f, 200f, 0.85f);
            Strew(Stones, c.x, c.y, 4.5f, 6, 0.8f, 1.2f);
            Strew(new[] { "PS_Sticks", "PS_Debris" }, c.x, c.y, 4f, 4, 0.8f, 1.1f);
        }

        // a supply train broke up by the north-west wall: jars (one smashed), straw jars, sacks, a basket, a straw mat
        static void SupplyTrain(Vector2 c)
        {
            Patch("Leaves", c.x, c.y, 9f, 1.3f); Patch("Mud", c.x + 3f, c.y - 2f, 6f);
            Prop("PS_Jar", c.x - 1.5f, c.y + 1.5f, 10f, 1f);
            Prop("PS_Jar", c.x + 0.2f, c.y + 2.4f, 80f, 0.9f, 82f);          // knocked over
            Prop("PS_Pot", c.x + 1.2f, c.y + 0.6f, 200f, 1f, 70f);
            Prop("PS_StrawJar", c.x - 3f, c.y - 0.5f, 30f, 1f);
            Prop("PS_StrawJar", c.x - 2.2f, c.y + 2.8f, 130f, 0.9f);
            Prop("PS_Sack", c.x + 2.5f, c.y - 1.2f, 15f, 1f, 75f); Prop("PS_Sack", c.x + 3.4f, c.y - 0.2f, 120f, 0.95f, 80f);
            Prop("PS_StrawBag", c.x + 0.8f, c.y - 2.6f, 60f, 1f, 85f);
            Prop("PS_Basket", c.x - 0.8f, c.y - 1.8f, 200f, 1f, 60f);
            Prop("PS_StrawMat", c.x + 4.5f, c.y + 2.5f, 25f, 1f);
            Corpse("PS_CorpseCloth", c.x + 5.5f, c.y - 3f, 100f);
            Blood(2, c.x + 5.3f, c.y - 3.3f, 3f);
            Strew(new[] { "PS_Pebbles", "PS_Sticks", "PS_Tile" }, c.x, c.y, 4.5f, 8, 0.8f, 1.1f);
        }

        // south lane: where the yokai broke through: one body, spears dropped in a heap, blood trail toward the gate
        static void Ambush(Vector2 c)
        {
            Patch("Mud", c.x, c.y, 7f, 1.4f);
            Corpse("PS_CorpseCovered", c.x, c.y, 120f);
            Blood(2, c.x + 0.3f, c.y + 0.2f, 3.4f);
            Blood(0, c.x - 2.8f, c.y - 1.2f, 2f, 2.6f, 200f);
            SpearBundle(c.x + 2.8f, c.y + 1.6f, 70f, 7);
            Corpse("Corpse_Side", c.x - 4f, c.y + 2.5f, 300f, 1.05f);
            Strew(Stones, c.x, c.y, 3.5f, 4, 0.8f, 1.1f);
        }

        // ------------------------------------------------------------------ 3. edge bands
        // every ~5 m along the walls a cluster: 1 big + 2-3 mid + 4-8 bits, set 3.5-6 m in from the wall line (closer is
        // hidden under the hanok wall's roof from the game camera). The side gates (z 0..12) and the hall approach stay open.
        static readonly string[] Big = { "PS_Rubble", "PS_Rubble", "PP_Beam", "PS_Jar", "PS_Sack", "PS_CorpseCovered" };
        static readonly string[] Mid = { "PS_Jar", "PS_StrawJar", "PS_Pot", "PS_Planks", "PS_Sack", "PS_Basket", "PS_StrawBag", "PS_Debris", "PP_Beam" };
        static readonly string[] Bits = { "PS_Tile", "PS_Tile", "PS_TileBird", "PS_Rocks", "PS_Pebbles", "PS_Sticks", "PS_Rocks" };

        static void Cluster(float x, float z, float inwardYaw)
        {
            string big = Big[rng.Next(Big.Length)];
            if (big == "PS_CorpseCovered") Corpse(big, x, z, R(0, 360));
            else if (big == "PP_Beam") { for (int i = 0; i < 3; i++) Prop("PP_Beam", x + R(-1.2f, 1.2f), z + R(-1.2f, 1.2f), R(0, 180), R(2.4f, 2.9f), 0f, R(-6f, 6f)); }
            else Prop(big, x, z, R(0, 360), big == "PS_Rubble" ? R(0.75f, 1f) : R(0.95f, 1.15f), big == "PS_Sack" ? 80f : 0f);
            int mid = 2 + rng.Next(2);
            for (int i = 0; i < mid; i++)
            {
                string m = Mid[rng.Next(Mid.Length)];
                float a = R(0, Mathf.PI * 2), d = R(1.2f, 2.6f);
                bool tip = m == "PS_Sack" || m == "PS_StrawBag" || (m != "PP_Beam" && m != "PS_Planks" && m != "PS_Debris" && Chance(0.3));
                Prop(m, x + Mathf.Cos(a) * d, z + Mathf.Sin(a) * d, R(0, 360), m == "PP_Beam" ? R(2.2f, 2.7f) : R(0.85f, 1.1f), tip ? R(70f, 88f) : 0f);
            }
            Strew(Bits, x, z, 3.2f, 4 + rng.Next(5), 0.8f, 1.2f);
            if (Chance(0.35)) Blood(Chance(0.5) ? 1 : 3, x + R(-2f, 2f), z + R(-2f, 2f), R(1.6f, 2.6f));
        }

        static void EdgeBands()
        {
            // west / east (x = -/+54), gate gap z -1..13
            for (int side = -1; side <= 1; side += 2)
                for (float z = -45f; z < 43f; z += R(4.2f, 6f))
                {
                    if (z > -1.5f && z < 13.5f) continue;
                    if (side > 0 && Mathf.Abs(z + 21f) < 8f) continue;          // the burnt gallery is there
                    Cluster(side * (54f - R(3.6f, 5.6f)), z, side > 0 ? -90f : 90f);
                }
            // north (z = 45.75) from the corners in to the hall terrace (|x| > 27)
            for (int side = -1; side <= 1; side += 2)
                for (float x = 50f; x > 27f; x -= R(4.4f, 6f))
                {
                    if (side < 0 && Mathf.Abs(x - 34f) < 6f) continue;         // the supply train is there
                    Cluster(side * x, 45.75f - R(3.4f, 5f), 180f);
                }
            // south (camera side, z = -49.5): lower, sparser (it is in front of the player)
            for (float x = -48f; x < 48f; x += R(7f, 10f))
                Strew(Chance(0.5) ? Bits : Wood, x, -47f + R(0f, 1.5f), 2.6f, 3 + rng.Next(3), 0.8f, 1.1f);
        }

        // ------------------------------------------------------------------ 4. fires + light (<= 8 lights in the field)
        static Material flameMat, smokeMat, sparkMat;
        static void Fires()
        {
            flameMat = Load<Material>("HellFX/UW_Flame"); smokeMat = Load<Material>("HellFX/UW_FireSmoke"); sparkMat = Load<Material>("HellFX/UW_Spark");
            if (!flameMat) { Debug.LogWarning("[PalaceStage] no HellFX flame material"); return; }
            // the burnt gallery still burns (big), embers on the yokai scorch (small, no light if the budget is short)
            Fire(45.5f, -20.5f, 0.9f, true);
            Fire(42.5f, -16.5f, 0.5f, true);
            Fire(25f, 24.5f, 0.55f, true);
            // braziers lit: the 11-16 PP_Brazier set (north-west, both gates)
            foreach (var b in new[] { new Vector2(-44f, 44.2f), new Vector2(-49.6f, -2.5f), new Vector2(49.6f, 14.5f) })
                BrazierFire(b.x, b.y);
            // two more braziers lit by the hall approach: real light where the centre meets the north edge
            foreach (var b in new[] { new Vector2(-14f, 40.5f), new Vector2(14f, 40.5f) })
            {
                var go = Prop("PP_Brazier", b.x, b.y, 0f, 4.5f);
                if (go) BrazierFire(b.x, b.y, go);
            }
        }

        static void BrazierFire(float x, float z, GameObject brazier = null)
        {
            float top = 1.2f;
            if (!brazier)
            {
                // the 11-16 brazier already standing there: find its top
                foreach (Transform t in root.parent)
                    if (t.name == "PP_Brazier" && (new Vector2(t.position.x - off.x, t.position.z - off.z) - new Vector2(x, z)).sqrMagnitude < 1f) { brazier = t.gameObject; break; }
            }
            if (brazier)
            {
                var rs = brazier.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); top = b.max.y - GroundAt(x, z); }
            }
            var p = new Vector3(x, GroundAt(x, z) + top - 0.15f, z) + off;
            Flames(p, new Vector3(0.5f, 0.05f, 0.5f), 0.9f, 1.5f, 7f, 0.8f);
            Smoke(p + Vector3.up * 1.4f, 0.3f, 1.4f, 0.5f);
            Sparks(p, new Vector3(0.4f, 0.1f, 0.4f), 3f);
            Light(p + Vector3.up * 0.8f, 11f, 9f);
        }

        static void Fire(float x, float z, float k, bool light)
        {
            var p = new Vector3(x, GroundAt(x, z) + 0.1f, z) + off;
            Flames(p, new Vector3(2.4f * k, 0.1f, 2.4f * k), 1.6f * k + 0.6f, 2.8f * k + 0.8f, 10f * k + 2f, 0.6f);
            Smoke(p + Vector3.up * (2f + 1.5f * k), 1.2f * k, 3.2f * k + 0.8f, 1.4f * k);
            Sparks(p + Vector3.up * 0.4f, new Vector3(2f * k, 0.2f, 2f * k), 6f * k);
            if (light) Light(p + Vector3.up * (1.2f + k), 9f + 7f * k, 8f + 10f * k);
        }

        static void Light(Vector3 pos, float range, float intensity)
        {
            if (lights >= MaxLights) return;
            lights++;
            var lg = new GameObject("PS_FireLight"); lg.transform.SetParent(root, false); lg.transform.position = pos;
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.46f, 0.18f); l.range = range; l.intensity = intensity;
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
            lg.AddComponent<PSFlicker>().baseIntensity = intensity;
        }

        // flame / smoke / spark Shuriken systems on the downloaded Skulmovski flipbook (same set-up as the 저승 fire sea)
        static ParticleSystem NewPS(string name, Vector3 pos, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(root, false); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            var mm = ps.main; mm.simulationSpace = ParticleSystemSimulationSpace.World; mm.loop = true; mm.prewarm = true; mm.playOnAwake = true;
            return ps;
        }
        static void Flames(Vector3 pos, Vector3 box, float sizeMin, float sizeMax, float rate, float hot)
        {
            var ps = NewPS("PS_Flames", pos, flameMat);
            var r = ps.GetComponent<ParticleSystemRenderer>(); r.pivot = new Vector3(0f, 0.35f, 0f); r.maxParticleSize = 2f;
            var mm = ps.main; mm.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f); mm.startSpeed = 0f;
            mm.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax); mm.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            mm.maxParticles = Mathf.CeilToInt(rate * 2f) + 2;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.15f); v.y = new ParticleSystem.MinMaxCurve(0.2f, 0.5f); v.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.8f, 0.5f), hot), 0), new GradientColorKey(new Color(1f, 0.36f, 0.08f), 0.45f), new GradientColorKey(new Color(0.55f, 0.07f, 0.02f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            var col = ps.colorOverLifetime; col.enabled = true; col.color = g;
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.mode = ParticleSystemAnimationMode.Grid; ts.numTilesX = 16; ts.numTilesY = 5;
            ts.animation = ParticleSystemAnimationType.WholeSheet; ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
            ts.startFrame = new ParticleSystem.MinMaxCurve(0f, 79f); ts.cycleCount = 1;
            ps.Play();
        }
        static void Smoke(Vector3 pos, float radius, float size, float rate)
        {
            if (!smokeMat) return;
            var ps = NewPS("PS_Smoke", pos, smokeMat);
            var mm = ps.main; mm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); mm.startSpeed = 0f; mm.maxParticles = Mathf.CeilToInt(rate * 7f) + 2;
            mm.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.3f); mm.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            mm.startColor = new Color(0.35f, 0.33f, 0.32f, 0.7f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = Mathf.Max(0.05f, radius); sh.rotation = new Vector3(90, 0, 0);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(0.1f, 0.5f); v.y = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); v.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(0, 1) });
            var col = ps.colorOverLifetime; col.enabled = true; col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            ps.Play();
        }
        static void Sparks(Vector3 pos, Vector3 box, float rate)
        {
            if (!sparkMat) return;
            var ps = NewPS("PS_Sparks", pos, sparkMat);
            var mm = ps.main; mm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f); mm.maxParticles = Mathf.CeilToInt(rate * 2.5f) + 2;
            mm.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.y = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); v.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.6f); v.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            var col = ps.colorOverLifetime; col.enabled = true; col.color = g;
            var no = ps.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.8f;
            ps.Play();
        }
    }

    // fire light flicker
    public sealed class PSFlicker : MonoBehaviour
    {
        public float baseIntensity = 10f, amount = 0.22f;
        Light l; float seed;
        void Awake() { l = GetComponent<Light>(); seed = Random.value * 100f; }
        void Update()
        {
            if (!l) return;
            float t = Time.time * 7f + seed;
            l.intensity = baseIntensity * (1f + amount * (Mathf.PerlinNoise(t, seed) - 0.5f) * 2f);
        }
    }
}
