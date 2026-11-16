using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld
{
    /// <summary>
    /// 2026-11-15 (Kido: "the lava is only a texture, there should be something on it"): downloaded Megascans volcanic rock
    /// clusters / ledges / spires (Fab, Quixel) standing in the lava sea around the plaza, so the sea is not a flat painted
    /// plane. Self-booting: waits for UWLook's lava sea object and parents the rocks to the same root, so no scene or UWLook
    /// edits are needed. Everything goes outside the plaza; nothing is placed in the arena.
    /// </summary>
    public class UWLavaRocks : MonoBehaviour
    {
        const string SeaName = "UWLook lava sea", RootName = "UWLavaRocks set";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("UWLavaRocks");
            DontDestroyOnLoad(go);
            go.AddComponent<UWLavaRocks>();
        }

        float nextCheck;

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            var sea = GameObject.Find(SeaName);
            if (!sea) return;
            var root = sea.transform.parent;
            if (!root || root.Find(RootName)) return;
            var look = sea.GetComponentInParent<UWLook>();
            Rect plaza = look ? look.plazaRect : new Rect(-44f, -29f, 88f, 56f);
            float seaY = sea.transform.position.y;
            Build(root, plaza, seaY);
            PortalFix(root.Find(RootName), sea.scene);
            GateFix(root.Find(RootName), root, plaza);
        }

        static void Build(Transform parent, Rect plaza, float seaY)
        {
            var set = new GameObject(RootName).transform;
            set.SetParent(parent, false);
            var rnd = new System.Random(7781);
            float F(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

            string[] flat = { "LR_C03", "LR_C06", "LR_C08" };
            var placed = new System.Collections.Generic.List<Vector3>();
            float gx = plaza.center.x, gz = plaza.yMax + 10f;   // UWLook's gate rises out of the sea here

            bool Free(float x, float z, float r)
            {
                if (Mathf.Abs(x - gx) < 20f && Mathf.Abs(z - gz) < 16f) return false;
                foreach (var p in placed)
                    if ((new Vector2(p.x - x, p.z - z)).sqrMagnitude < (p.y + r) * (p.y + r)) return false;
                return true;
            }

            // low rock islands and crust slabs scattered over the sea: 5 m to 50 m off the plaza edge
            int tries = 0, count = 0;
            while (count < 26 && tries++ < 600)
            {
                float x = F(plaza.xMin - 50f, plaza.xMax + 50f), z = F(plaza.yMin - 40f, plaza.yMax + 45f);
                float dx = Mathf.Max(plaza.xMin - x, 0f, x - plaza.xMax), dz = Mathf.Max(plaza.yMin - z, 0f, z - plaza.yMax);
                float d = Mathf.Max(dx, dz);
                if (d < 5f) continue;
                // 11-16 Kido: rocks on the camera (south) side loomed up as orange shards at the screen bottom
                if (z < plaza.yMin + 4f) continue;
                bool ledge = rnd.NextDouble() < 0.3;
                float s = ledge ? F(2.2f, 3.2f) : F(3.5f, 6.5f);
                float r = (ledge ? 3.5f : 3.2f) * s;
                if (!Free(x, z, r)) continue;
                Put(set, ledge ? "LR_L11" : flat[rnd.Next(flat.Length)], new Vector3(x, seaY, z), F(0f, 360f), s, ledge ? 0.25f : 0.35f);
                placed.Add(new Vector3(x, r, z));
                count++;
            }
            // a few big lava spires far out at the corners / north for depth (never on the camera side)
            float[,] spires = { { plaza.xMin - 30f, plaza.yMax + 22f }, { plaza.xMax + 28f, plaza.yMax + 26f }, { plaza.xMin - 38f, plaza.center.y }, { plaza.xMax + 36f, plaza.center.y - 6f } };
            for (int i = 0; i < spires.GetLength(0); i++)
                Put(set, "LR_Spire", new Vector3(spires[i, 0], seaY, spires[i, 1]), F(0f, 360f), F(1.6f, 2.2f), 0.2f);
            Debug.Log($"UWLavaRocks: {set.childCount} rocks in the lava sea (sea y {seaY})");
        }

        // Kido 11-15: the exit swirl was cut off by the coded octagon dais under it, and the dais + floating black shards
        // (UW_PortalFrame, a first-draft model) read fake. Hide that frame, stand the swirl on a downloaded Megascans basalt
        // ledge with two volcanic spires either side, and lift the swirl (with its sparks / lights) clear of the rock.
        // 18:45: the raised box platform and its box steps under the swirl read as "네모네모" too: both hidden (the walk
        // surface is the flat plaza anyway, there is no collider on them), the chains that hung off the old frame go too.
        // The swirl now stands at plaza level on the basalt ledge, and the back edge of the old platform becomes basalt
        // cliffs rising out of the lava.
        const float PortalLift = 1.2f;
        static readonly string[] PortalHide = { "UW_PortalFrame", "UW_PlatformBack", "UW_Steps", "UW_Chain" };
        static void PortalFix(Transform set, UnityEngine.SceneManagement.Scene scene)
        {
            if (!set) return;
            Vector3 frame = new Vector3(20f, 1.2f, 29.6f);
            bool found = false; int hid = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != scene) continue;
                for (var t = r.transform; t; t = t.parent)
                {
                    bool hit = false;
                    foreach (var h in PortalHide) if (t.name.StartsWith(h)) { hit = true; break; }
                    if (!hit) continue;
                    if (t.name.StartsWith("UW_PortalFrame")) { frame = t.position; found = true; }
                    r.enabled = false; hid++; break;
                }
            }
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.gameObject.scene != scene) continue;
                string n = t.name;
                if (n == "PortalVortex" || n == "PortalSpiral" || n == "PortalKey" || n == "PortalBack" || n == "PortalHaze")
                    t.position += Vector3.up * PortalLift;
            }
            if (!found) return;
            var g = new Vector3(frame.x, 0f, frame.z);   // plaza level
            var p = new GameObject("UW portal rocks").transform; p.SetParent(set, false);
            Put(p, "LR_L11", g + new Vector3(0f, 0f, 0.6f), 180f, 1.7f, 0.82f);
            Put(p, "LR_Spire", g + new Vector3(-6.8f, 0f, 0.8f), 75f, 1.15f, 0.1f);
            Put(p, "LR_Spire", g + new Vector3(6.9f, 0f, 1.2f), 250f, 1.05f, 0.1f);
            // basalt cliffs rising out of the lava behind and beside the swirl, tops just above plaza level
            float sea = -6f;
            float[,] cliffs = { { -9f, 3.5f, 20f }, { 0f, 5.2f, 185f }, { 9f, 3.8f, 340f }, { -13f, -1.5f, 95f }, { 13.5f, -0.5f, 265f } };
            for (int i = 0; i < cliffs.GetLength(0); i++)
                Put(p, "LR_L11", new Vector3(g.x + cliffs[i, 0], sea, g.z + cliffs[i, 1]), cliffs[i, 2], 3.0f, 0.02f);
            Debug.Log($"UWLavaRocks: portal area rebuilt at {frame}, hid {hid} box renderers, swirl lifted {PortalLift} m");
        }

        // 18:45 Kido: the gate hall ("집같은거") looked clumsy: fire puffs were parked inside its roof, the two front fires
        // burned through the pillars, one red spot light flattened the whole hall, and the hall stood on the lava like a box.
        // Roof fires off, front fires pulled clear of the pillars, the roof light halved, basalt cliffs under its footing.
        static void GateFix(Transform set, Transform hellRoot, Rect plaza)
        {
            if (!set) return;
            float gx = plaza.center.x, gz = plaza.yMax + 10f;
            int roof = 0, moved = 0;
            foreach (Transform t in hellRoot)
            {
                if (t.name == "HellFire" && Mathf.Abs(t.position.x - gx) < 12f && Mathf.Abs(t.position.z - gz) < 6f)
                {
                    if (t.position.y > 3f) { t.gameObject.SetActive(false); roof++; }
                    else { t.position += new Vector3(Mathf.Sign(t.position.x - gx) * 1.5f, 0f, -3f); moved++; }
                }
                else if (t.name == "GateRoofLight")
                {
                    var l = t.GetComponent<Light>(); if (l) { l.intensity *= 0.5f; l.color = new Color(1f, 0.55f, 0.3f); }
                }
            }
            var p = new GameObject("UW gate footing").transform; p.SetParent(set, false);
            float sea = -6f;
            float[,] cliffs = { { -14f, 2f, 10f }, { 14f, 3f, 200f }, { -9f, 8f, 160f }, { 8f, 9f, 350f } };
            for (int i = 0; i < cliffs.GetLength(0); i++)
                Put(p, "LR_L11", new Vector3(gx + cliffs[i, 0], sea, gz + cliffs[i, 1]), cliffs[i, 2], 3.0f, 0.06f);
            Debug.Log($"UWLavaRocks: gate fixed, {roof} roof fires off, {moved} front fires moved");
        }

        static void Put(Transform set, string name, Vector3 pos, float yaw, float scale, float sinkFrac)
        {
            var src = Resources.Load<GameObject>("LavaRocks/" + name);
            if (!src) { Debug.LogWarning("UWLavaRocks: missing Resources/LavaRocks/" + name); return; }
            var mat = Resources.Load<Material>("LavaRocks/M_" + name);
            var go = Instantiate(src, set);
            go.name = name;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            Bounds b = new Bounds(pos, Vector3.zero);
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.bounds.size.magnitude < 1f) { r.enabled = false; continue; }   // Megascans ships tiny helper meshes
                if (mat) r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            if (any) go.transform.position += Vector3.up * (pos.y - b.min.y - b.size.y * sinkFrac);   // base sunk into the lava
        }
    }
}
