// Bamboo arena floor clutter (Kido 11-11: the floor was too plain next to the abandoned village's): pebbles, snapped
// bamboo sticks, culm splinters and dark twigs scattered over the clearing, in clumps (noise), denser toward the bamboo
// wall and around the floor props, a few bare patches. Built when the stage starts playing, merged into 16 m chunk
// meshes (one draw per chunk and material, camera-culled, no shadow casting), flagged DontSave so RoomRun frees them.
// Materials are taken from the scene's own floor props (BA_Stone, BA_Fallen, BA_Stump), so nothing new is saved.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BambooArena
{
    public class BAGroundDebris : MonoBehaviour
    {
        public float density = 3.2f;   // tries per square metre (before the noise and edge falloff)
        public int seed = 23;

        System.Random rng;
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        IEnumerator Start()
        {
            // the ground collider is added by the room run a frame or two after load
            for (int i = 0; i < 4; i++) yield return null;
            Build();
        }

        void Build()
        {
            var field = GetComponent<BambooField>();
            if (!field) return;
            Material stone = null, fallen = null, stump = null;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    if (m.name == "BA_Stone") stone = m; else if (m.name == "BA_Stone2" && !stone) stone = m;
                    else if (m.name == "BA_Fallen") fallen = m;
                    else if (m.name == "BA_Stump") stump = m;
                }
            if (!stone || !fallen) return;
            if (!stump) stump = fallen;

            // props already on the floor: clutter gathers around them
            var props = new List<Vector3>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var mm = r.sharedMaterial;
                if (mm && (mm.name == "BA_Fallen" || mm.name == "BA_Stump" || mm.name.StartsWith("BA_Stone"))) props.Add(r.bounds.center);
            }

            rng = new System.Random(seed);
            var pebble = Pebble(); var stick = Box(new Vector3(1f, 0.05f, 0.05f)); var splint = Box(new Vector3(1f, 0.012f, 0.06f));
            var root = new GameObject("GroundDebris").transform;   // scene root, identity: the chunk vertices are in world space
            var c = transform.position;
            float hx = field.arena.x * 0.5f + 2f, hz = field.arena.y * 0.5f + 2f;
            const float DC = 16f; int total = 0, hits = 0;
            for (float cz = -hz; cz < hz; cz += DC)
                for (float cx = -hx; cx < hx; cx += DC)
                {
                    var P = new Lists(); var S = new Lists(); var T = new Lists();
                    int tries = Mathf.RoundToInt(DC * DC * density);
                    for (int i = 0; i < tries; i++)
                    {
                        float x = cx + R(0, DC), z = cz + R(0, DC);
                        if (Mathf.Abs(x) > hx || Mathf.Abs(z) > hz) continue;
                        float edge = Mathf.Max(Mathf.Abs(x) / hx, Mathf.Abs(z) / hz);
                        float near = 0f;
                        foreach (var p in props) { float d = (p.x - c.x - x) * (p.x - c.x - x) + (p.z - c.z - z) * (p.z - c.z - z); if (d < 16f) near = Mathf.Max(near, 1f - d / 16f); }
                        float dens = 0.75f + 0.6f * Mathf.Clamp01((edge - 0.6f) * 2.5f) + 0.6f * near;
                        float n = Mathf.PerlinNoise(x * 0.13f + 11f, z * 0.13f + 5f);
                        dens *= Mathf.Clamp01((n - 0.12f) * 2.4f);   // clumps, and bare patches
                        if (rng.NextDouble() > dens) continue;
                        var pos = c + new Vector3(x, 0, z);
                        pos.y = Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f) ? hit.point.y : c.y;
                        double k = rng.NextDouble();
                        if (k < 0.62)
                        {
                            float s = R(0.07f, 0.2f) * (rng.NextDouble() < 0.12 ? 2.2f : 1f);
                            Append(pebble, pos - Vector3.up * 0.015f, Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20)), new Vector3(s, s * R(0.45f, 0.75f), s * R(0.8f, 1.2f)), P);
                        }
                        else if (k < 0.82)
                        {
                            float len = R(0.35f, 1.3f), th = R(0.7f, 1.4f);
                            Append(stick, pos + Vector3.up * 0.025f * th, Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3)), new Vector3(len, th, th), S);
                        }
                        else if (k < 0.93)
                            Append(splint, pos + Vector3.up * 0.008f, Quaternion.Euler(R(-4, 4), R(0, 360), R(-8, 8)), new Vector3(R(0.3f, 0.9f), 1f, R(0.6f, 1.2f)), S);
                        else
                        {
                            // a small twig: two thin crossed sticks
                            var rot = Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3));
                            float len = R(0.25f, 0.6f);
                            Append(stick, pos + Vector3.up * 0.012f, rot, new Vector3(len, 0.4f, 0.4f), T);
                            Append(stick, pos + rot * new Vector3(len * 0.2f, 0.012f, 0), rot * Quaternion.Euler(0, R(25, 50), 0), new Vector3(len * 0.5f, 0.3f, 0.3f), T);
                        }
                    }
                    Chunk(root, "Pebbles", P, stone);
                    Chunk(root, "Sticks", S, fallen);
                    Chunk(root, "Twigs", T, stump);
                    total += P.v.Count / 24 + S.v.Count / 24 + T.v.Count / 48; if (P.v.Count > 0) hits++;
                }
            Debug.Log($"[BambooArena] floor clutter: {total} pieces in {hits} chunks (stone {stone.name}, y {c.y:0.00}, props {props.Count})");
        }

        class Src { public Vector3[] v, n; public Vector2[] uv; public int[] t; }
        class Lists { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }

        // a squashed, jittered octahedron (8 faces)
        static Src Pebble()
        {
            var r = new System.Random(5);
            var bv = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down * 0.4f, Vector3.forward, Vector3.back };
            for (int i = 0; i < bv.Length; i++) bv[i] *= 0.8f + 0.4f * (float)r.NextDouble();
            int[] f = { 2, 4, 0, 2, 1, 4, 2, 5, 1, 2, 0, 5, 3, 0, 4, 3, 4, 1, 3, 1, 5, 3, 5, 0 };
            var s = new Src { v = new Vector3[24], n = new Vector3[24], uv = new Vector2[24], t = new int[24] };
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = bv[f[i]], b = bv[f[i + 1]], cc = bv[f[i + 2]];
                var nn = Vector3.Cross(b - a, cc - a).normalized;
                s.v[i] = a; s.v[i + 1] = b; s.v[i + 2] = cc; s.n[i] = s.n[i + 1] = s.n[i + 2] = nn;
                s.uv[i] = new Vector2(a.x, a.z) * 0.5f; s.uv[i + 1] = new Vector2(b.x, b.z) * 0.5f; s.uv[i + 2] = new Vector2(cc.x, cc.z) * 0.5f;
                s.t[i] = i; s.t[i + 1] = i + 1; s.t[i + 2] = i + 2;
            }
            return s;
        }

        // a box of the given size, x along its length, sitting on y = 0
        static Src Box(Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var s = new Src { v = mesh.vertices, n = mesh.normals, uv = mesh.uv, t = mesh.triangles };
            Destroy(go);
            for (int i = 0; i < s.v.Length; i++) { s.v[i] = Vector3.Scale(s.v[i], size) + Vector3.up * size.y * 0.5f; s.uv[i] = new Vector2(s.uv[i].x * 2f, s.uv[i].y * 0.2f); }
            return s;
        }

        static void Append(Src s, Vector3 pos, Quaternion rot, Vector3 scale, Lists L)
        {
            int b = L.v.Count;
            for (int i = 0; i < s.v.Length; i++) { L.v.Add(pos + rot * Vector3.Scale(s.v[i], scale)); L.n.Add(rot * s.n[i]); L.uv.Add(s.uv[i] + new Vector2(pos.x, pos.z) * 0.37f); }
            for (int i = 0; i < s.t.Length; i++) L.t.Add(b + s.t[i]);
        }

        static void Chunk(Transform parent, string name, Lists L, Material mat)
        {
            if (L.v.Count == 0) return;
            var mesh = new Mesh { name = "BA_" + name, hideFlags = HideFlags.DontSave, indexFormat = L.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(L.v); mesh.SetNormals(L.n); mesh.SetUVs(0, L.uv); mesh.SetTriangles(L.t, 0); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
