// GPU-instanced reed clumps: bucketed into 8 m chunks per mesh and material, frustum-culled per camera and drawn
// right before each camera renders (game view, scene view and captures). Same idea as LookStudyInstancer, kept separate
// so the reed field can rebuild its instances live without touching the LookStudy fields.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reeds
{
    [ExecuteAlways]
    public class ReedInstancer : MonoBehaviour
    {
        const float Chunk = 8f;
        const int Max = 1023;

        class Bucket
        {
            public Mesh mesh; public Material mat;
            public readonly List<Matrix4x4[]> mats = new List<Matrix4x4[]>();
            public readonly List<int> counts = new List<int>();
            public Bounds bounds; public bool hasBounds;
        }

        readonly Dictionary<(int, int, Mesh, Material), Bucket> map = new Dictionary<(int, int, Mesh, Material), Bucket>();
        readonly List<Bucket> buckets = new List<Bucket>();
        readonly Plane[] planes = new Plane[6];
        public int Count { get; private set; }
        // Stats of the last camera drawn (for the benchmark).
        public int LastDrawnInstances { get; private set; }
        public long LastDrawnTris { get; private set; }

        // Combined bounds of every instance (RoomRun sizes rooms from renderers and reads this by reflection if needed).
        public Bounds WorldBounds
        {
            get
            {
                var b = new Bounds(); bool any = false;
                foreach (var k in buckets) if (k.hasBounds) { if (any) b.Encapsulate(k.bounds); else { b = k.bounds; any = true; } }
                return b;
            }
        }

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

        public void Clear() { map.Clear(); buckets.Clear(); Count = 0; }

        public void Add(Mesh mesh, Material mat, Matrix4x4 m) => Add(mesh, mat, m, null);

        // worldBounds: for meshes modelled at real size (bamboo); by default the bounds assume a 1 m mesh scaled by height.
        public void Add(Mesh mesh, Material mat, Matrix4x4 m, Bounds? worldBounds)
        {
            if (!mesh || !mat) return;
            if (!mat.enableInstancing) mat.enableInstancing = true;
            Vector3 pos = m.GetColumn(3);
            var key = (Mathf.FloorToInt(pos.x / Chunk), Mathf.FloorToInt(pos.z / Chunk), mesh, mat);
            if (!map.TryGetValue(key, out var b)) { map[key] = b = new Bucket { mesh = mesh, mat = mat }; buckets.Add(b); }
            if (b.counts.Count == 0 || b.counts[b.counts.Count - 1] == Max) { b.mats.Add(new Matrix4x4[Max]); b.counts.Add(0); }
            int arr = b.counts.Count - 1, i = b.counts[arr];
            b.mats[arr][i] = m;
            b.counts[arr] = i + 1;
            float s = m.lossyScale.y;
            // Generous bounds: bent reeds can lean out by most of their height.
            var wb = worldBounds ?? new Bounds(pos + Vector3.up * s * 0.5f, new Vector3(s * 2.2f + 1f, s * 1.2f + 0.5f, s * 2.2f + 1f));
            if (b.hasBounds) b.bounds.Encapsulate(wb); else { b.bounds = wb; b.hasBounds = true; }
            Count++;
        }

        void Draw(ScriptableRenderContext ctx, Camera cam)
        {
            if (!isActiveAndEnabled || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            int drawn = 0; long tris = 0;
            foreach (var b in buckets)
            {
                if (!b.hasBounds || !GeometryUtility.TestPlanesAABB(planes, b.bounds)) continue;
                var rp = new RenderParams(b.mat)
                {
                    camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    worldBounds = b.bounds
                };
                // Leaves, stems and plumes are separate submeshes from Blender; all use the one reed material.
                for (int sm = 0; sm < b.mesh.subMeshCount; sm++)
                for (int k = 0; k < b.mats.Count; k++)
                {
                    Graphics.RenderMeshInstanced(rp, b.mesh, sm, b.mats[k], b.counts[k]);
                    drawn += b.counts[k]; tris += (long)b.counts[k] * (long)(b.mesh.GetIndexCount(sm) / 3);
                }
            }
            LastDrawnInstances = drawn; LastDrawnTris = tris;
        }
    }
}
