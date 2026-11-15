// A few terrain features at the arena edges (11-13, Kido relaxed the arena rule: some big trees, 장승, a house corner
// poking slightly into the arena are fine at edges/corners; never clutter the centre). Prefabs from
// Resources/EdgeFeatures (baked from downloaded CC BY assets by Editor/EdgeFeaturesBake.cs, credits in
// Assets/Game/RoomRun/CREDITS.txt), placed when a field is entered, into that field's scene (gone with it).
// Fields without an owner thread only: grass, valley, village, palace, reeds, bamboo.
// Trees stand just outside the edge (their crowns overhang, soft see-through when the player walks under them); 장승
// stand just outside the north edge. A house (EF_ThatchHouse: outside a corner, only its corner inside, box collider) is
// supported but not placed: from the top-down camera its thatch roof read as a flat wooden slab (11-13 shots).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomRun
{
    public sealed class EdgeFeatures : MonoBehaviour
    {
        // what: prefab name; u,v: -1..1 across the arena (x, z; corners at +-1); out: metres outside the edge (negative =
        // inside); yaw: extra turn (deg); scale. A house uses out as how far its corner pokes in.
        // dark: a near-black foreground silhouette (11-13, Kido's Hades 2 ref: very tall black shapes on the camera-side edge only).
        struct F { public string what; public float u, v, outM, yaw, scale; public bool dark; public F(string w, float u, float v, float o, float yaw = 0, float s = 1, bool dark = false) { what = w; this.u = u; this.v = v; outM = o; this.yaw = yaw; scale = s; this.dark = dark; } }

        static readonly Dictionary<string, F[]> Layout = new Dictionary<string, F[]>
        {
            // 11-14 (Kido: the Japanese black pine EF_PineA/B "looks like a bonsai, never use it"): replaced by the downloaded dead
            // conifers in every field; 민가 has no living pines or firs at all.
            // 11-13 grass/village depth (Kido, Hades 2 ref: very tall, near-black silhouettes only along the bottom (camera)
            // edge, outside the fight area; tall trees around the sides). dark = a
            // foreground silhouette (see Silhouette). Doors: grass north centre, village east centre (kept clear).
            ["grass"] = new[] { new F("EF_DeadConiferA", -0.82f, -1f, 0.8f, 0, 0.85f), new F("EF_RF_DeadConifer_B", 1f, 0.78f, 1.2f, 60), new F("EF_Jangseung", -0.52f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", -0.45f, 1f, 0.5f, 8, 1.25f),
                // bottom (camera) edge: dark pine silhouettes rising into the lower part of the screen only (no shadows into the
                // arena), branchy dead conifers at the corners; spaced so the middle of the screen stays open
                new F("EF_RF_DeadConifer_B", -0.62f, -1f, 5f, 200, 1.05f, true), new F("EF_DeadConiferA", -0.25f, -1f, 4f, 300, 1.0f, true), new F("EF_RF_DeadConifer_B", 0.1f, -1f, 5.5f, 30, 1.1f, true),
                new F("EF_DeadConiferA", 0.42f, -1f, 4.5f, 80, 1.1f, true), new F("EF_RF_DeadConifer_B", 0.75f, -1f, 5f, 260, 1.0f, true),
                new F("EF_DeadConiferA", -0.95f, -1f, 4f, 150, 1.4f, true), new F("EF_DeadConiferA", 0.96f, -1f, 4f, 30, 1.35f, true),
                // sides: tall firs and pines over the hedge (on screen when the player is near that side)
                new F("EF_Fir", -1f, 0.42f, 4f, 0, 1.3f), new F("EF_TallPine", -1f, 0.05f, 6f, 70, 1.1f), new F("EF_RF_DeadConifer_B", -1f, -0.35f, 3f, 130, 1.25f), new F("EF_DeadConiferA", -1f, -0.66f, 4f, 240, 1.1f),
                new F("EF_Fir", 1f, 0.35f, 4.5f, 160, 1.25f), new F("EF_TallPine", 1f, -0.1f, 4f, 20, 1.15f), new F("EF_DeadConiferA", 1f, -0.5f, 3.5f, 300), new F("EF_DeadConiferA", 1f, -0.78f, 3f, 50, 1.2f) },
            ["valley"] = new[] { new F("EF_RF_DeadConifer_B", -0.8f, -1f, 0.8f, 30), new F("EF_DeadConiferA", 0.6f, 1f, 1.5f, 200, 0.9f), new F("EF_Jangseung", -1f, 0.55f, 0.6f, -70, 1.25f) },
            ["village"] = new[] { new F("EF_Jangseung", 0.42f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", 0.49f, 1f, 0.5f, -8, 1.25f), new F("EF_DeadConiferA", 1f, 0.8f, 1.2f, 140),
                // bottom (camera) edge: the pine belt there is darkened to silhouettes by LookStudyRuins.SouthSil; a
                // pair of 장승 like pillars where the south lane leaves
                new F("EF_Jangseung", -0.06f, -1f, 1.8f, 190, 2.2f, true), new F("EF_Jangseung", 0.06f, -1f, 1.8f, 170, 2.2f, true),
                // sides (east centre is the door): tall trees over the belt, clear of the village houses that stand just
                // outside the north and side edges (LookStudyRuins.BuildVillage, 11-14)
                new F("EF_DeadConiferA", -1f, -0.75f, 5f, 30, 1.3f), new F("EF_RF_DeadConifer_B", -1f, 0.85f, 5f, 220, 1.1f),
                new F("EF_RF_DeadConifer_B", 1f, 0.2f, 5f, 80, 1.15f), new F("EF_DeadConiferA", 1f, -0.95f, 5f, 10, 1.25f), new F("EF_DeadConiferA", 1f, 0.88f, 4f, 140, 1.1f) },
            // 11-13 (Kido, all fields: the fight area lacked big things for depth): tall pines between the galleries (x +-54)
            // and the outer wall (+-63) and flanking the hall, crowns towering over the gallery roofs. All outside the arena.
            ["palace"] = new[] { new F("EF_DeadConiferA", -0.78f, -1f, 0.9f, 0, 0.9f), new F("EF_RF_DeadConifer_B", 0.8f, -1f, 0.9f, 110, 0.85f),
                new F("EF_RF_DeadConifer_B", -1f, 0.55f, 6f, 20, 1.25f), new F("EF_DeadConiferA", -1f, -0.15f, 7.5f, 140, 1.4f), new F("EF_RF_DeadConifer_B", -1f, -0.72f, 5.5f, 250, 1.15f),
                new F("EF_DeadConiferA", 1f, 0.35f, 6.5f, 80, 1.35f), new F("EF_RF_DeadConifer_B", 1f, -0.45f, 5.5f, 200, 1.2f),
                new F("EF_DeadConiferA", -0.55f, 1f, 9f, 30, 1.45f), new F("EF_RF_DeadConifer_B", 0.62f, 1f, 8f, 300, 1.35f) },
            // 11-13 night pass (Kido: the pine in the reeds was round at the bottom and did not fit): no pines; all outside the edge.
            ["reeds"] = new[] { new F("EF_Jangseung", 0.55f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", 0.62f, 1f, 0.5f, -10, 1.25f),
                                // 11-14 (Kido: the coded 폐촌 dead trees looked "weird", remove): downloaded dead conifers and snags only
                                new F("EF_DeadConiferA", -1f, 0.72f, 3.0f, 250, 1.1f), new F("EF_RF_Snag_A", -1f, 0.2f, 2.5f, 40, 4.0f), new F("EF_RF_DeadConifer_B", -1f, -0.45f, 3.0f, 130, 1.2f),
                                new F("EF_RF_DeadConifer_B", 1f, 0.55f, 3.0f, 300, 1.25f), new F("EF_DeadConiferA", 1f, 0.05f, 4.0f, 160, 1.15f), new F("EF_RF_Snag_C", 1f, -0.5f, 2.5f, 220, 3.8f),
                                new F("EF_RF_Snag_D", -0.3f, 1f, 3.0f, 70, 4.2f), new F("EF_DeadConiferA", 0.15f, 1f, 4.5f, 10, 1.2f), new F("EF_RF_DeadConifer_B", 0.95f, 1f, 3.0f, 200, 1.3f),
                                // camera side: tall near-black snags (thin, no crown: 5.5-6 m out is fine) and dead conifers 8-9 m out (closer put
                                // crowns over the player and cost FPS)
                                new F("EF_RF_Snag_A", -0.75f, -1f, 5.5f, 15, 6.0f, true), new F("EF_RF_DeadConifer_B", -0.38f, -1f, 8.5f, 130, 1.5f, true),
                                new F("EF_RF_Snag_D", 0.08f, -1f, 5.5f, 250, 6.5f, true), new F("EF_DeadConiferA", 0.45f, -1f, 9f, 60, 1.4f, true),
                                new F("EF_RF_Snag_C", 0.8f, -1f, 6f, 300, 6.0f, true) },
            // bamboo: no entry since the 11-13 redo; BABuilder.Landmarks places its own 서낭당 landmarks and silhouettes
        };

        // the LookStudy fields shade with their own globals (painterly sun colour), not the URP light the SP materials
        // read: there these props come out much darker than the field around them, so their tint is lifted per field
        static readonly Dictionary<string, float> Lift = new Dictionary<string, float> { ["grass"] = 2.4f, ["valley"] = 2.0f, ["village"] = 2.0f, ["palace"] = 2.0f, ["reeds"] = 2.0f, ["bamboo"] = 1.6f };
        static readonly Dictionary<(Material, string), Material> lifted = new Dictionary<(Material, string), Material>();

        // per-field look on top of the lift (11-13, Kido on the reed-field pine: "잎 색깔같은거 통일하고 너무 붕뜨는데"): the
        // needle atlas mixes saturated, blue and yellow greens per clump and the rim/specular lit their edges, so the tree
        // read as pasted onto the dusky reeds. In the reeds the needles are greyed almost fully and re-tinted one dark,
        // heavy olive (darker than the reeds), with no rim or specular; bark loses its rim too. Runtime copies only: the
        // EF_* materials and the snow / 폐촌 pines are untouched.
        static void FieldLook(string field, Material m)
        {
            if (field != "reeds") return;
            string n = m.name;
            if (n.StartsWith("EF_HeroNeedles"))
            {
                m.SetFloat("_Desat", 0.94f);
                // 11-13 Kido: "푸른 소나무 색감을 맞춰봐 아니면 은행나무같은거": 0 = old dark olive, 1 = dry reed tan
                // (matches the field), 2 = golden autumn larch (낙엽송, the ginkgo-yellow option). Dev: RR.ReedPineLook.
                int look = PlayerPrefs.GetInt("RR.ReedPineLook", 1);
                m.SetColor("_Tint", look == 2 ? new Color(1.55f, 1.18f, 0.42f) : look == 1 ? new Color(1.22f, 1.0f, 0.64f)
                                               : new Color(1.0f, 1.04f, 0.70f));   // after the x2 lift (0.5 read black)
                m.SetFloat("_RimScale", 0f);
                m.SetFloat("_Smoothness", 0f);
                m.SetFloat("_Wrap", 0.3f);
                m.SetFloat("_AOStrength", 0.9f);
            }
            else if (n.StartsWith("EF_HeroBark") || n.StartsWith("EF_HeroCore"))
            {
                var t = m.GetColor("_Tint");
                m.SetColor("_Tint", new Color(t.r * 0.9f, t.g * 0.86f, t.b * 0.8f, t.a));
                if (m.HasProperty("_RimScale")) m.SetFloat("_RimScale", 0.15f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            }
        }

        // foreground silhouette: almost black, no rim or gloss, so it frames the screen without pulling the eye
        static void Silhouette(Material m)
        {
            string c = m.HasProperty("_Tint") ? "_Tint" : "_BaseColor";
            var t = m.GetColor(c);
            m.SetColor(c, new Color(t.r * 0.13f, t.g * 0.12f, t.b * 0.12f, t.a));
            if (m.HasProperty("_Desat")) m.SetFloat("_Desat", 0.9f);
            if (m.HasProperty("_RimScale")) m.SetFloat("_RimScale", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
        }

        public static bool Enabled => PlayerPrefs.GetInt("RR.EdgeFeatures", 1) != 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("EdgeFeatures");
            DontDestroyOnLoad(go);
            go.AddComponent<EdgeFeatures>();
        }

        string doneFor;

        void LateUpdate()
        {
            var run = RunDirector.Instance;
            if (!run || run.Transitioning || run.Room == null) return;
            var scene = SceneManager.GetActiveScene();
            string key = run.Room.id + "@" + scene.handle;
            if (key == doneFor) return;
            doneFor = key;
            if (!Enabled || !Layout.TryGetValue(run.Room.id, out var list)) return;
            var holder = new GameObject("EdgeFeatures_" + run.Room.id);
            SceneManager.MoveGameObjectToScene(holder, scene);
            Vector3 c = run.Room.center; float hx = run.Room.size.x * 0.5f, hz = run.Room.size.y * 0.5f;
            int n = 0;
            float k = Lift.TryGetValue(run.Room.id, out var l) ? l : 1f;
            foreach (var f in list) if (Place(f, c, hx, hz, holder.transform, k, run.Room.id) != null) n++;
            if (run.Room.id == "palace") n += PalaceWalls(c, hx, hz, holder.transform, k);
            Debug.Log($"[EdgeFeatures] {run.Room.id}: {n} features at the edges");
        }

        // 11-14 (Kido on the coded 대전 앞 galleries, "quality is awful, download something and use it"): the court is ringed
        // by the downloaded tiled hanok wall (EF_HanokWall) just outside the arena: both long sides (the side gates at z ~6
        // stay open) and the north side from the corners in to the hall terrace (|x| > 27). LookStudyRuins no longer builds
        // the gallery posts and roofs; the low south row stays (camera side).
        static int PalaceWalls(Vector3 c, float hx, float hz, Transform parent, float lift)
        {
            var probe = Place(new F("EF_HanokWall", 0, 0, 0, 90), c, hx, hz, parent, lift * 0.5f, "palace");
            if (!probe) return 0;
            float len = WorldBounds(probe).size.z * 0.97f;   // slight overlap so the runs read continuous
            Destroy(probe);
            if (len < 1f) return 0;
            int n = 0;
            void Run(Vector3 a, Vector3 b, float yaw, float gateZ, Vector3 outward, float edge)
            {
                float d = Vector3.Distance(a, b); int count = Mathf.Max(1, Mathf.RoundToInt(d / len));
                for (int i = 0; i < count; i++)
                {
                    var p = Vector3.Lerp(a, b, (i + 0.5f) / count);
                    if (!float.IsNaN(gateZ) && Mathf.Abs(p.z - gateZ) < len * 0.5f + 2.5f) continue;   // side gate stays open
                    var go = Place(new F("EF_HanokWall", 0, 0, 0, yaw), c, hx, hz, parent, lift * 0.5f, "palace");
                    if (!go) continue;
                    p.y = Ground(p, c.y);
                    go.transform.position = p + Vector3.down * 0.06f;
                    // the model has a short return wall at one end (6 m deep): push the piece out until its inner side sits
                    // 0.3 m past the arena edge (arena rule: nothing stands inside)
                    var wb = WorldBounds(go);
                    float inner = outward.x > 0 ? wb.min.x : outward.x < 0 ? -wb.max.x : wb.min.z;
                    go.transform.position += outward * Mathf.Max(0f, edge + 0.3f - inner);
                    n++;
                }
            }
            // 11-14 capture: the model's short return wall stood into the court; turned 180 so the return points away from
            // the arena (the wall's back is the same plaster and tile), and the plaster is kept darker than the x2 field lift
            float wx = c.x - hx - 0.6f, ex = c.x + hx + 0.6f, s0 = c.z - hz + 1.5f, s1 = c.z + hz + 0.6f, nz = c.z + hz + 0.6f;
            Run(new Vector3(wx, 0, s0), new Vector3(wx, 0, s1), 90f, 6f, Vector3.left, -(c.x - hx));   // west, face east
            Run(new Vector3(ex, 0, s0), new Vector3(ex, 0, s1), -90f, 6f, Vector3.right, c.x + hx);                         // east, face west
            Run(new Vector3(c.x - hx, 0, nz), new Vector3(c.x - 27f, 0, nz), 180f, float.NaN, Vector3.forward, c.z + hz);         // north-west, face south
            Run(new Vector3(c.x + 27f, 0, nz), new Vector3(c.x + hx, 0, nz), 180f, float.NaN, Vector3.forward, c.z + hz);         // north-east
            Debug.Log($"[EdgeFeatures] palace: {n} hanok wall pieces ({len:0.0} m)");
            return n;
        }

        static GameObject Place(F f, Vector3 c, float hx, float hz, Transform parent, float lift, string field)
        {
            var prefab = Resources.Load<GameObject>("EdgeFeatures/" + f.what);
            if (!prefab) { Debug.LogWarning("[EdgeFeatures] no prefab " + f.what); return null; }
            bool house = f.what.Contains("House");
            // on an edge (|u| or |v| = 1) push outward by outM; a house is pushed so its corner pokes in by outM
            var p = new Vector3(c.x + f.u * hx, c.y, c.z + f.v * hz);
            var go = Instantiate(prefab, parent);
            go.name = f.what;
            if (lift != 1f || field == "reeds" || f.dark)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        // _Tint: the snow-field prop shaders (lifted per field); _BaseColor (URP Lit scans): silhouettes only
                        if (!ms[i] || !(ms[i].HasProperty("_Tint") || (f.dark && ms[i].HasProperty("_BaseColor")))) continue;
                        string lk = field + PlayerPrefs.GetInt("RR.ReedPineLook", 1) + (f.dark ? "/dark" : "");
                        if (!lifted.TryGetValue((ms[i], lk), out var lm) || !lm)
                        {
                            lm = new Material(ms[i]) { name = ms[i].name + " (" + field + ")" };
                            if (lm.HasProperty("_Tint")) { var t = lm.GetColor("_Tint"); lm.SetColor("_Tint", new Color(t.r * lift, t.g * lift, t.b * lift, t.a)); }
                            FieldLook(field, lm);
                            if (f.dark) Silhouette(lm);
                            lifted[(ms[i], lk)] = lm;
                        }
                        ms[i] = lm;
                    }
                    r.sharedMaterials = ms;
                }
            if (f.dark && (field == "grass" || field == "village")) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.rotation = Quaternion.Euler(0, f.yaw, 0);
            go.transform.localScale = Vector3.one * f.scale;
            go.transform.position = p;
            if (house)
            {
                var b = WorldBounds(go);
                float sx = Mathf.Sign(f.u), sz = Mathf.Sign(f.v);
                // move the box outward until it overlaps the arena by outM on both axes
                p.x += sx * (b.extents.x - f.outM);
                p.z += sz * (b.extents.z - f.outM);
            }
            else
            {
                if (Mathf.Abs(f.u) >= 0.999f) p.x += Mathf.Sign(f.u) * f.outM;
                if (Mathf.Abs(f.v) >= 0.999f) p.z += Mathf.Sign(f.v) * f.outM;
            }
            p.y = Ground(p, c.y);
            go.transform.position = p + Vector3.down * 0.06f;
            if (house)
            {
                // the part inside the arena must block like a wall (the rest is out of reach anyway)
                var col = go.AddComponent<BoxCollider>();
                var lb = LocalBounds(go);
                col.center = lb.center; col.size = lb.size;
            }
            var wb = WorldBounds(go);
            Debug.Log($"[EdgeFeatures]   {f.what} at ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) bounds x {wb.min.x:0.0}..{wb.max.x:0.0} z {wb.min.z:0.0}..{wb.max.z:0.0}");
            return go;
        }

        static float Ground(Vector3 p, float fallback)
        {
            // the lowest solid hit under the spot near the arena floor (not a roof or a canopy)
            var hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var h in hits)
                if (Mathf.Abs(h.point.y - fallback) < 4f && h.point.y < best && !h.collider.GetComponentInParent<PlayerController>()) best = h.point.y;
            return best < float.MaxValue ? best : fallback;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Bounds LocalBounds(GameObject go)
        {
            var inv = go.transform.worldToLocalMatrix;
            bool first = true; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var q = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { b = new Bounds(q, Vector3.zero); first = false; } else b.Encapsulate(q);
                }
            }
            return b;
        }
    }
}
