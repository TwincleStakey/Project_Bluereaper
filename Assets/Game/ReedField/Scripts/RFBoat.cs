// The small ferry boat (나룻배) moored in the river, tied to a 장승 on the bank (Kido 11-10).
// The current pushes it downstream until the rope is taut: it rides at the end of the rope, swings a little across the
// stream, surges and tugs, bobs and rocks. The rope is a thin line from the 장승 to the iron ring on the bow, nearly
// straight while it is pulled tight and sagging a touch when the boat surges back.
using System.Collections.Generic;
using UnityEngine;

namespace Reeds
{
    [ExecuteAlways]
    public class RFBoat : MonoBehaviour
    {
        [Tooltip("The boat (mesh RF_Boat; its submesh 2 is the bow ring the rope is tied to).")]
        public Transform boat;
        [Tooltip("Where the rope is tied on the 장승 / post (world point is this transform's position).")]
        public Transform tiePoint;
        public float ropeLength = 5.5f;
        [Tooltip("Direction the river flows (x, z).")]
        public Vector2 flow = new Vector2(-1, 0);
        [Tooltip("Away from the bank, into the stream (x, z). The rope runs out between this and the flow.")]
        public Vector2 offshore = new Vector2(0, -1);
        [Range(0f, 2f)] public float offshoreAmount = 0.85f;
        public float waterY = -0.32f;
        [Range(0f, 2f)] public float swing = 0.7f;      // side to side across the current (m)
        [Range(0f, 1f)] public float surge = 0.35f;     // back and forth along the rope (m)
        [Range(0f, 10f)] public float roll = 4f;        // degrees
        [Range(0f, 6f)] public float pitch = 2f;
        public float bob = 0.05f;
        public LineRenderer rope;

        [Tooltip("Height (m above the waterline) of the invisible lid that keeps the river out of the hull.")]
        public float lidHeight = 0.14f;

        Vector3 ringLocal, bowLocal;
        bool measured;
        GameObject lid;
        Mesh lidMesh;
        static Material lidMat;

        // 11-14 (Kido: the boat's bottom looked see-through): the hull rides 0.18 m deep, so the transparent river was
        // drawn over the floorboards inside it. A depth-only lid (ReedField/WaterMask) cut from the hull at lidHeight
        // stops the water there; built in Play from the mesh itself as a plain scene object (dies with the scene; no
        // DontSave GameObjects, they leak across scenes, see MeshChunker).
        void OnEnable() { if (Application.isPlaying) BuildLid(); }

        void OnDisable()
        {
            if (lid) Destroy(lid);
            if (lidMesh) Destroy(lidMesh);
        }

        void BuildLid()
        {
            var mf = boat ? boat.GetComponentInChildren<MeshFilter>() : null;
            var mesh = mf ? mf.sharedMesh : null;
            if (!mesh || lid) return;
            if (!lidMat) lidMat = Resources.Load<Material>("RF_BoatWaterMask");   // keeps the shader in builds
            if (!lidMat)
            {
                var sh = Shader.Find("ReedField/WaterMask");
                if (!sh) return;
                lidMat = new Material(sh) { name = "RF_BoatWaterMask", hideFlags = HideFlags.DontSave };
            }
            var verts = mesh.vertices;
            var tris = mesh.GetTriangles(0);   // hull skin + thwarts (floorboards, pole and ring are on other submeshes)
            var bnd = mesh.bounds;
            bool alongX = bnd.size.x > bnd.size.z;
            float h = lidHeight / Mathf.Max(1e-4f, mf.transform.lossyScale.y);
            // slice the hull at y = h: points where triangle edges cross the plane (skipping the end boards)
            var pts = new List<Vector2>();   // (along, across)
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                var nrm = Vector3.Cross(b - a, c - a).normalized;
                if (Mathf.Abs(alongX ? nrm.x : nrm.z) > 0.7f) continue;
                void Edge(Vector3 p, Vector3 q)
                {
                    if ((p.y - h) * (q.y - h) > 0 || Mathf.Approximately(p.y, q.y)) return;
                    var x = Vector3.Lerp(p, q, (h - p.y) / (q.y - p.y));
                    pts.Add(alongX ? new Vector2(x.x, x.z) : new Vector2(x.z, x.x));
                }
                Edge(a, b); Edge(b, c); Edge(c, a);
            }
            if (pts.Count < 8) return;
            float mid = alongX ? bnd.center.z : bnd.center.x;
            float amin = float.MaxValue, amax = float.MinValue;
            foreach (var p in pts) { amin = Mathf.Min(amin, p.x); amax = Mathf.Max(amax, p.x); }
            int nb = Mathf.Clamp(Mathf.CeilToInt((amax - amin) / 0.12f), 4, 80);
            var lo = new float[nb]; var hi = new float[nb];
            for (int k = 0; k < nb; k++) { lo[k] = float.MinValue; hi[k] = float.MaxValue; }
            foreach (var p in pts)
            {
                int k = Mathf.Clamp((int)((p.x - amin) / (amax - amin) * nb), 0, nb - 1);
                float s = p.y - mid;
                // the inside skin is the crossing nearest the keel line on each side
                if (s < 0) lo[k] = Mathf.Max(lo[k], s); else hi[k] = Mathf.Min(hi[k], s);
            }
            var v = new List<Vector3>(); var t = new List<int>();
            bool first = true;
            for (int k = 0; k < nb; k++)
            {
                if (lo[k] == float.MinValue || hi[k] == float.MaxValue) continue;
                float al = first ? amin : (k == nb - 1 ? amax : amin + (k + 0.5f) * (amax - amin) / nb);
                first = false;
                Vector3 P(float along, float across) => alongX ? new Vector3(along, h, mid + across) : new Vector3(mid + across, h, along);
                v.Add(P(al, lo[k])); v.Add(P(al, hi[k]));
                if (v.Count >= 4) { int s0 = v.Count - 4; t.AddRange(new[] { s0, s0 + 1, s0 + 2, s0 + 1, s0 + 3, s0 + 2 }); }
            }
            if (t.Count == 0) return;
            // stretch the last row to the end of the slice so no gap is left at the stern/bow board
            {
                var last = v[v.Count - 1]; var last2 = v[v.Count - 2];
                if (alongX) { last.x = amax; last2.x = amax; } else { last.z = amax; last2.z = amax; }
                v[v.Count - 1] = last; v[v.Count - 2] = last2;
            }
            lidMesh = new Mesh { name = "RF_BoatLid" };
            lidMesh.SetVertices(v); lidMesh.SetTriangles(t, 0); lidMesh.RecalculateBounds();
            lid = new GameObject("WaterLid");
            lid.transform.SetParent(mf.transform, false);
            lid.AddComponent<MeshFilter>().sharedMesh = lidMesh;
            var r = lid.AddComponent<MeshRenderer>();
            r.sharedMaterial = lidMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void Measure()
        {
            measured = true;
            var mf = boat ? boat.GetComponentInChildren<MeshFilter>() : null;
            var mesh = mf ? mf.sharedMesh : null;
            if (!mesh) return;
            // bow ring = submesh 2 (only the ring uses it); bow direction = from the hull centre toward it
            var b = mesh.subMeshCount > 2 ? mesh.GetSubMesh(2).bounds : mesh.bounds;
            var toMf = boat.InverseTransformPoint(mf.transform.TransformPoint(b.center));
            ringLocal = toMf;
            bowLocal = new Vector3(ringLocal.x, 0, ringLocal.z).normalized;
        }

        void LateUpdate()
        {
            if (!boat || !tiePoint) return;
            if (!measured) Measure();
            float t = Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600.0);
            // the rope runs out from the bank into the stream and downstream with the current
            var down = (new Vector3(flow.x, 0, flow.y).normalized + new Vector3(offshore.x, 0, offshore.y).normalized * offshoreAmount).normalized;
            var across = Vector3.Cross(Vector3.up, down);
            var tie = tiePoint.position;

            // the current holds it out downstream; slow swing across the stream, quicker tugging surges along the rope
            float sw = (Mathf.Sin(t * 0.33f) * 0.7f + Mathf.Sin(t * 0.81f + 1.3f) * 0.3f) * swing;
            float sg = (Mathf.Sin(t * 1.1f) * 0.6f + Mathf.Sin(t * 2.3f + 0.4f) * 0.4f) * surge;
            float reach = ropeLength - Mathf.Max(0f, -sg);      // surging back gives slack, pulling out keeps it tight
            var bowFlat = new Vector3(tie.x, 0, tie.z) + (down * Mathf.Sqrt(Mathf.Max(reach * reach - sw * sw, 0.01f)) + across * sw);

            // bow points back up the rope toward the 장승, with a little yaw from the swing
            var toTie = new Vector3(tie.x - bowFlat.x, 0, tie.z - bowFlat.z).normalized;
            var yaw = Quaternion.FromToRotation(bowLocal == Vector3.zero ? Vector3.forward : bowLocal, toTie);
            yaw = Quaternion.AngleAxis(Mathf.Sin(t * 0.47f) * 4f, Vector3.up) * yaw;
            var rock = Quaternion.AngleAxis(Mathf.Sin(t * 1.3f) * roll + Mathf.Sin(t * 2.9f) * roll * 0.25f, toTie) *
                       Quaternion.AngleAxis(Mathf.Sin(t * 0.9f + 0.7f) * pitch, Vector3.Cross(Vector3.up, toTie));
            boat.rotation = rock * yaw;
            // place so the bow ring lands on bowFlat, hull riding at the waterline
            var ringWorldOffset = boat.rotation * Vector3.Scale(ringLocal, boat.lossyScale);
            float y = waterY + Mathf.Sin(t * 1.7f) * bob + Mathf.Sin(t * 3.1f + 1f) * bob * 0.3f;
            boat.position = new Vector3(bowFlat.x - ringWorldOffset.x, y, bowFlat.z - ringWorldOffset.z);

            if (rope)
            {
                var ring = boat.position + ringWorldOffset;
                float slack = Mathf.Max(0f, ropeLength - Vector3.Distance(tie, ring));
                float sag = 0.03f + Mathf.Sqrt(slack * ropeLength) * 0.35f;   // near straight when taut
                const int N = 14;
                if (rope.positionCount != N) rope.positionCount = N;
                for (int i = 0; i < N; i++)
                {
                    float u = i / (N - 1f);
                    rope.SetPosition(i, Vector3.Lerp(tie, ring, u) + Vector3.down * sag * 4f * u * (1 - u));
                }
            }
        }
    }
}
