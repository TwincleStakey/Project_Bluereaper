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
        struct F { public string what; public float u, v, outM, yaw, scale; public F(string w, float u, float v, float o, float yaw = 0, float s = 1) { what = w; this.u = u; this.v = v; outM = o; this.yaw = yaw; scale = s; } }

        static readonly Dictionary<string, F[]> Layout = new Dictionary<string, F[]>
        {
            ["grass"] = new[] { new F("EF_PineA", -0.82f, -1f, 0.8f, 0, 0.85f), new F("EF_PineB", 1f, 0.78f, 1.2f, 60), new F("EF_Jangseung", -0.52f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", -0.45f, 1f, 0.5f, 8, 1.25f) },
            ["valley"] = new[] { new F("EF_PineB", -0.8f, -1f, 0.8f, 30), new F("EF_PineA", 0.6f, 1f, 1.5f, 200, 0.9f), new F("EF_Jangseung", -1f, 0.55f, 0.6f, -70, 1.25f) },
            ["village"] = new[] { new F("EF_Jangseung", 0.42f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", 0.49f, 1f, 0.5f, -8, 1.25f), new F("EF_PineA", 1f, 0.8f, 1.2f, 140) },
            ["palace"] = new[] { new F("EF_PineA", -0.78f, -1f, 0.9f, 0, 0.9f), new F("EF_PineB", 0.8f, -1f, 0.9f, 110, 0.85f) },
            ["reeds"] = new[] { new F("EF_PineB", -1f, 0.75f, 1.0f, 250), new F("EF_Jangseung", 0.55f, 1f, 0.5f, 0, 1.25f), new F("EF_Jangseung", 0.62f, 1f, 0.5f, -10, 1.25f) },
            ["bamboo"] = new[] { new F("EF_Jangseung", -0.5f, 1f, 0.4f, 0, 1.25f), new F("EF_Jangseung", -0.42f, 1f, 0.4f, 10, 1.25f), new F("EF_Jangseung", 1f, -0.55f, 0.4f, -80, 1.25f) },
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
                m.SetColor("_Tint", new Color(1.0f, 1.04f, 0.70f));   // after the x2 lift: dark grey olive (0.5 read black)
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
            foreach (var f in list) if (Place(f, c, hx, hz, holder.transform, k, run.Room.id)) n++;
            Debug.Log($"[EdgeFeatures] {run.Room.id}: {n} features at the edges");
        }

        static bool Place(F f, Vector3 c, float hx, float hz, Transform parent, float lift, string field)
        {
            var prefab = Resources.Load<GameObject>("EdgeFeatures/" + f.what);
            if (!prefab) { Debug.LogWarning("[EdgeFeatures] no prefab " + f.what); return false; }
            bool house = f.what.Contains("House");
            // on an edge (|u| or |v| = 1) push outward by outM; a house is pushed so its corner pokes in by outM
            var p = new Vector3(c.x + f.u * hx, c.y, c.z + f.v * hz);
            var go = Instantiate(prefab, parent);
            go.name = f.what;
            if (lift != 1f || field == "reeds")
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (!ms[i] || !ms[i].HasProperty("_Tint")) continue;
                        if (!lifted.TryGetValue((ms[i], field), out var lm) || !lm)
                        {
                            lm = new Material(ms[i]) { name = ms[i].name + " (" + field + ")" };
                            var t = lm.GetColor("_Tint"); lm.SetColor("_Tint", new Color(t.r * lift, t.g * lift, t.b * lift, t.a));
                            FieldLook(field, lm);
                            lifted[(ms[i], field)] = lm;
                        }
                        ms[i] = lm;
                    }
                    r.sharedMaterials = ms;
                }
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
            return true;
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
