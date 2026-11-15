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
            Debug.Log("UWLavaRocks: boot");
        }

        float nextCheck;
        bool logged;

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            var sea = GameObject.Find(SeaName);
            if (!sea) return;
            var root = sea.transform.parent;
            if (!logged) { logged = true; Debug.Log("UWLavaRocks: found lava sea under " + (root ? root.name : "none")); }
            if (!root || root.Find(RootName)) return;
            var look = sea.GetComponentInParent<UWLook>();
            Rect plaza = look ? look.plazaRect : new Rect(-44f, -29f, 88f, 56f);
            float seaY = sea.transform.position.y;
            Build(root, plaza, seaY);
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
            if (set.childCount <= 3) Debug.Log($"UWLavaRocks: {name} at {go.transform.position} bounds {b.center} size {b.size} renderers {go.GetComponentsInChildren<Renderer>().Length} mat {(mat ? mat.shader.name : "none")}");
        }
    }
}
