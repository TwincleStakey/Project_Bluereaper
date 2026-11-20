// GPU-instanced reed clumps: bucketed into 8 m chunks per mesh and material, frustum-culled per camera and drawn
// right before each camera renders (game view, scene view and captures). Same idea as LookStudyInstancer, kept separate
// so the reed field can rebuild its instances live without touching the LookStudy fields.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Reeds
{
    [ExecuteAlways]
    public class ReedInstancer : MonoBehaviour, VegetationQuery.ISource
    {
        // 8 m chunks: 16 m was measured (11-10) to add ~15% triangles for no CPU gain
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

        void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; VegetationQuery.Register(this); }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; VegetationQuery.Unregister(this); ReleaseProc(); }

        // Greatsword cut debris: reeds inside a swing sector (bamboo and other non-cuttable materials are skipped).
        public void Collect(Vector3 centre, Vector3 dir, float radius, float cosHalf, List<VegetationQuery.Hit> hits, int max, ref int seen)
        {
            if (!isActiveAndEnabled) return;
            foreach (var b in buckets)
            {
                if (!b.hasBounds) continue;
                var bb = b.bounds;
                if (centre.x + radius < bb.min.x || centre.x - radius > bb.max.x || centre.z + radius < bb.min.z || centre.z - radius > bb.max.z) continue;
                var kind = VegetationQuery.KindOf(b.mat, out var col);
                if (kind == VegetationQuery.Kind.None) continue;
                for (int k = 0; k < b.mats.Count; k++)
                    for (int i = 0; i < b.counts[k]; i++)
                        {
                        var m = b.mats[k][i];
                        int slot = VegetationQuery.Offer(m.GetColumn(3), centre, dir, radius, cosHalf, kind, col, hits, max, ref seen);
                        if (slot < 0) continue;
                        var h = hits[slot]; h.mesh = b.mesh; h.mat = b.mat; h.rot = m.rotation; h.scale = ((Vector3)m.GetColumn(1)).magnitude; h.matrix = m; hits[slot] = h;
                    }
            }
        }

        public void Clear() { map.Clear(); buckets.Clear(); Count = 0; procDirty = true; }

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
            procDirty = true;
        }


        // 11-10 perf: vegetation is left out of the SSAO depth-normals pass, which drew every clump a second time
        // (half the reed/grass triangles). Drawn with a runtime copy of the material that has that pass off; the copy
        // re-syncs its properties once a frame, so runtime colour changes (dev panel palettes) still show.
        static readonly Dictionary<Material, Material> noAO = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, int> noAOFrame = new Dictionary<Material, int>();
        static Material NoAO(Material src)
        {
            if (!src) return src;
            if (!noAO.TryGetValue(src, out var m) || !m)
            {
                m = new Material(src) { name = src.name + " (no AO)", hideFlags = HideFlags.DontSave };
                m.SetShaderPassEnabled("DepthNormals", false);
                noAO[src] = m; noAOFrame[src] = Time.frameCount;
            }
            else if (noAOFrame[src] != Time.frameCount)
            {
                m.CopyPropertiesFromMaterial(src); m.SetShaderPassEnabled("DepthNormals", false);
                noAOFrame[src] = Time.frameCount;
            }
            return m;
        }

        // 11-13 perf (Kido: the reed field ran slower than the other fields): every chunk used to re-send its matrices
        // (Graphics.RenderMeshInstanced, 64 B per clump per frame) in ~1,000 calls x 3 submeshes. Now every matrix lives in
        // one GPU buffer, uploaded once when the field is generated; each frame only the ids of the visible chunks are
        // packed (4 B each) and every mesh + material + submesh is ONE RenderMeshPrimitives call. Same clumps, same
        // meshes, same culling. Needs the RF_PROC keyword (ReedField/Reed); other shaders (bamboo) keep the old path.
        GraphicsBuffer procMats;
        readonly Dictionary<Bucket, (int start, int count)> procRange = new Dictionary<Bucket, (int, int)>();
        bool procDirty = true;
        static readonly int ID_ProcMats = Shader.PropertyToID("_RFProcMats"), ID_ProcIdx = Shader.PropertyToID("_RFProcIdx"), ID_ProcBase = Shader.PropertyToID("_RFProcBase");
        class PackDraw { public Mesh mesh; public Material mat; public Bounds bounds; public int start, count; public readonly List<Vector2Int> ranges = new List<Vector2Int>(); public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
        class CamPack { public GraphicsBuffer idx; public uint[] data; public readonly Dictionary<(Mesh, Material), PackDraw> draws = new Dictionary<(Mesh, Material), PackDraw>(); public readonly List<PackDraw> used = new List<PackDraw>(); }
        readonly Dictionary<Camera, CamPack> camPacks = new Dictionary<Camera, CamPack>();
        static readonly Dictionary<Shader, bool> procShader = new Dictionary<Shader, bool>();
        static readonly Dictionary<Material, Material> procMat = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, int> procFrame = new Dictionary<Material, int>();
        [Tooltip("Draw from one GPU buffer, one call per mesh (off = the old per-chunk RenderMeshInstanced path, for A/B).")]
        public bool gpuBuffer = true;

        static bool HasProc(Material m)
        {
            if (!m || !m.shader) return false;
            if (!procShader.TryGetValue(m.shader, out var ok)) procShader[m.shader] = ok = m.shader.keywordSpace.FindKeyword("RF_PROC").isValid;
            return ok;
        }

        static Material ProcMat(Material src)
        {
            if (!procMat.TryGetValue(src, out var m) || !m)
            {
                m = new Material(src) { name = src.name + " (proc)", hideFlags = HideFlags.DontSave, enableInstancing = false };
                procMat[src] = m; procFrame[src] = -1;
            }
            if (procFrame[src] != Time.frameCount)
            {
                // re-synced once a frame, so mood / dev-panel colour changes still show
                m.CopyPropertiesFromMaterial(src);
                m.EnableKeyword("RF_PROC");
                m.SetShaderPassEnabled("DepthNormals", false);
                procFrame[src] = Time.frameCount;
            }
            return m;
        }

        void ReleaseProc()
        {
            procMats?.Release(); procMats = null; procRange.Clear();
            foreach (var p in camPacks.Values) p.idx?.Release();
            camPacks.Clear();
            procDirty = true;
        }

        void BuildProc()
        {
            procDirty = false;
            procMats?.Release(); procMats = null; procRange.Clear();
            if (!SystemInfo.supportsComputeShaders) return;
            int total = 0;
            foreach (var b in buckets) if (HasProc(b.mat)) foreach (var c in b.counts) total += c;
            if (total == 0) return;
            var m = new Matrix4x4[total];
            int at = 0;
            // buckets of one mesh + material laid out next to each other, so neighbouring visible chunks merge into one range
            var order = new List<Bucket>(buckets);
            order.Sort((x, y) => x.mesh.GetInstanceID() != y.mesh.GetInstanceID() ? x.mesh.GetInstanceID().CompareTo(y.mesh.GetInstanceID()) : x.mat.GetInstanceID().CompareTo(y.mat.GetInstanceID()));
            foreach (var b in order)
            {
                if (!HasProc(b.mat)) continue;
                int s = at;
                for (int k = 0; k < b.mats.Count; k++) { System.Array.Copy(b.mats[k], 0, m, at, b.counts[k]); at += b.counts[k]; }
                procRange[b] = (s, at - s);
            }
            procMats = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, 64);
            procMats.SetData(m);
            if (Application.isPlaying)
            {
                var per = new Dictionary<Mesh, int>();
                foreach (var b in buckets) { int n = 0; foreach (var c in b.counts) n += c; per[b.mesh] = (per.TryGetValue(b.mesh, out var o) ? o : 0) + n; }
                var sb = new System.Text.StringBuilder($"[ReedInstancer] {name}: {total} clumps in one buffer, {buckets.Count} chunks;");
                foreach (var kv in per) { long t = 0; for (int i = 0; i < kv.Key.subMeshCount; i++) t += kv.Key.GetIndexCount(i) / 3; sb.Append($" {kv.Key.name} x{kv.Value} ({t} tris)"); }
                Debug.Log(sb.ToString());
            }
        }

        CamPack PackFor(Camera cam)
        {
            if (!camPacks.TryGetValue(cam, out var p))
            {
                List<Camera> dead = null;
                foreach (var k in camPacks.Keys) if (!k) (dead ??= new List<Camera>()).Add(k);
                if (dead != null) foreach (var k in dead) { camPacks[k].idx?.Release(); camPacks.Remove(k); }
                camPacks[cam] = p = new CamPack();
            }
            foreach (var d in p.used) { d.ranges.Clear(); d.count = 0; }
            p.used.Clear();
            return p;
        }

        bool Collect(CamPack p, Bucket b)
        {
            if (!procRange.TryGetValue(b, out var r) || r.count == 0) return false;
            var key = (b.mesh, b.mat);
            if (!p.draws.TryGetValue(key, out var d)) p.draws[key] = d = new PackDraw { mesh = b.mesh, mat = b.mat };
            if (d.ranges.Count == 0) { p.used.Add(d); d.bounds = b.bounds; } else d.bounds.Encapsulate(b.bounds);
            int last = d.ranges.Count - 1;
            if (last >= 0 && d.ranges[last].x + d.ranges[last].y == r.start) d.ranges[last] = new Vector2Int(d.ranges[last].x, d.ranges[last].y + r.count);
            else d.ranges.Add(new Vector2Int(r.start, r.count));
            d.count += r.count;
            return true;
        }

        void FlushPack(CamPack p, Camera cam, ref int drawn, ref long tris)
        {
            if (p.used.Count == 0) return;
            int total = 0;
            foreach (var d in p.used) total += d.count;
            if (p.data == null || p.data.Length < total) p.data = new uint[Mathf.NextPowerOfTwo(total)];
            if (p.idx == null || !p.idx.IsValid() || p.idx.count < total) { p.idx?.Release(); p.idx = new GraphicsBuffer(GraphicsBuffer.Target.Structured, p.data.Length, 4); }
            int n = 0;
            foreach (var d in p.used)
            {
                d.start = n;
                foreach (var r in d.ranges) for (int i = 0; i < r.y; i++) p.data[n++] = (uint)(r.x + i);
            }
            p.idx.SetData(p.data, 0, 0, n);
            foreach (var d in p.used)
            {
                d.mpb.SetBuffer(ID_ProcMats, procMats); d.mpb.SetBuffer(ID_ProcIdx, p.idx); d.mpb.SetInt(ID_ProcBase, d.start);
                var rp = new RenderParams(ProcMat(d.mat))
                {
                    camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                    worldBounds = d.bounds, matProps = d.mpb
                };
                for (int sm = 0; sm < d.mesh.subMeshCount; sm++)
                {
                    Graphics.RenderMeshPrimitives(rp, d.mesh, sm, d.count);
                    tris += (long)d.count * (long)(d.mesh.GetIndexCount(sm) / 3);
                }
                drawn += d.count;
            }
        }

        void OnDestroy() => ReleaseProc();

        static readonly Unity.Profiling.ProfilerMarker mDraw = new Unity.Profiling.ProfilerMarker("Reed.Draw");

        void Draw(ScriptableRenderContext ctx, Camera cam) { using (mDraw.Auto()) DrawImpl(cam); }

        void DrawImpl(Camera cam)
        {
            if (!isActiveAndEnabled || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            // cameras that cannot see this layer (water-ripple / trail / effect cameras) never draw the reeds
            if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;
            if (gpuBuffer && (procDirty || (procMats != null && !procMats.IsValid()))) BuildProc();
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var pack = gpuBuffer && procMats != null ? PackFor(cam) : null;
            int drawn = 0; long tris = 0;
            foreach (var b in buckets)
            {
                if (!b.hasBounds || !GeometryUtility.TestPlanesAABB(planes, b.bounds)) continue;
                if (pack != null && Collect(pack, b)) continue;
                // reeds and grass leave SSAO; the bamboo keeps it (few triangles, and its wall reads with the AO)
                var mat = b.mat.shader && b.mat.shader.name == "ReedField/Reed" ? NoAO(b.mat) : b.mat;
                var rp = new RenderParams(mat)
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
            if (pack != null) FlushPack(pack, cam, ref drawn, ref tris);
            LastDrawnInstances = drawn; LastDrawnTris = tris;
        }
    }
}
