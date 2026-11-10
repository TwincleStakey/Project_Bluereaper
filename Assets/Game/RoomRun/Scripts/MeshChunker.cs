// Splits huge combined meshes into 16 m tiles when a field loads (Kido 11-10, "200 fps on a 4060").
// The LookStudy stages merge every object of a material into one mesh spanning the whole map, so nothing is ever culled
// and each mesh lands in every shadow cascade (민가: 42.8M triangles a frame). Tiles let the camera and the shadow passes
// skip what is off screen. Every vertex attribute is copied byte for byte and the material, layer, shadow and probe
// settings are kept, so the picture is unchanged. The original renderer is only disabled (its MeshCollider stays).
// Runs for meshes that are readable and larger than MinSpan; imported (non-readable) meshes and skinned meshes are left.
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomRun
{
    public static class MeshChunker
    {
        public const float Tile = 16f, MinSpan = 40f;

        // Splits every big mesh in the active scene. Returns how many renderers were split.
        public static bool Enabled = true;

        public static int SplitScene(out int tiles)
        {
            tiles = 0; int split = 0;
            if (!Enabled) return 0;
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var list = new List<MeshRenderer>();
            foreach (var root in scene.GetRootGameObjects()) list.AddRange(root.GetComponentsInChildren<MeshRenderer>(false));
            foreach (var mr in list)
            {
                if (!mr.enabled || mr.GetComponent<TileMarker>()) continue;
                var mf = mr.GetComponent<MeshFilter>();
                var mesh = mf ? mf.sharedMesh : null;
                if (!mesh || !mesh.isReadable || mesh.subMeshCount != 1 || mesh.blendShapeCount > 0 || mesh.bindposeCount > 0) continue;
                var b = mr.bounds;
                if (Mathf.Max(b.size.x, b.size.z) < MinSpan) continue;
                int made = Split(mr, mesh);
                if (made > 1) { mr.enabled = false; split++; tiles += made; }
            }
            return split;
        }

        sealed class TileMarker : MonoBehaviour { }

        static int Split(MeshRenderer mr, Mesh mesh)
        {
            var tr = mr.transform;
            using var data = Mesh.AcquireReadOnlyMeshData(mesh);
            var md = data[0];
            int vc = md.vertexCount;
            var attrs = mesh.GetVertexAttributes();
            int streams = 0; foreach (var a in attrs) streams = Mathf.Max(streams, a.stream + 1);
            var raw = new NativeArray<byte>[streams];
            var stride = new int[streams];
            for (int s = 0; s < streams; s++) { raw[s] = md.GetVertexData<byte>(s); stride[s] = mesh.GetVertexBufferStride(s); }
            var pos = new NativeArray<Vector3>(vc, Allocator.Temp);
            md.GetVertices(pos);
            var sub = md.GetSubMesh(0);
            if (sub.topology != MeshTopology.Triangles) { pos.Dispose(); return 0; }
            var idx = new NativeArray<int>(sub.indexCount, Allocator.Temp);
            md.GetIndices(idx, 0);

            // triangles into tiles by their centre (in world space, so tiles line up across meshes)
            var cells = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < idx.Length; t += 3)
            {
                var c = tr.TransformPoint((pos[idx[t]] + pos[idx[t + 1]] + pos[idx[t + 2]]) / 3f);
                var key = (Mathf.FloorToInt(c.x / Tile), Mathf.FloorToInt(c.z / Tile));
                if (!cells.TryGetValue(key, out var l)) cells[key] = l = new List<int>();
                l.Add(t);
            }
            if (cells.Count < 2) { pos.Dispose(); idx.Dispose(); return 0; }

            var remap = new int[vc];
            var used = new List<int>();
            int made = 0;
            foreach (var kv in cells)
            {
                for (int i = 0; i < used.Count; i++) remap[used[i]] = 0;
                used.Clear();
                var tris = new int[kv.Value.Count * 3];
                int w = 0;
                foreach (int t in kv.Value)
                    for (int k = 0; k < 3; k++)
                    {
                        int v = idx[t + k];
                        if (remap[v] == 0) { used.Add(v); remap[v] = used.Count; }
                        tris[w++] = remap[v] - 1;
                    }
                var m = new Mesh { name = mesh.name + $"_tile{kv.Key.Item1}_{kv.Key.Item2}", hideFlags = HideFlags.DontSave };
                m.indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                m.SetVertexBufferParams(used.Count, attrs);
                for (int s = 0; s < streams; s++)
                {
                    var buf = new NativeArray<byte>(used.Count * stride[s], Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    for (int i = 0; i < used.Count; i++)
                        NativeArray<byte>.Copy(raw[s], used[i] * stride[s], buf, i * stride[s], stride[s]);
                    m.SetVertexBufferData(buf, 0, 0, buf.Length, s, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                    buf.Dispose();
                }
                m.SetIndexBufferParams(tris.Length, m.indexFormat);
                if (m.indexFormat == IndexFormat.UInt16)
                {
                    var s16 = new ushort[tris.Length]; for (int i = 0; i < tris.Length; i++) s16[i] = (ushort)tris[i];
                    m.SetIndexBufferData(s16, 0, 0, s16.Length, MeshUpdateFlags.DontValidateIndices);
                }
                else m.SetIndexBufferData(tris, 0, 0, tris.Length, MeshUpdateFlags.DontValidateIndices);
                // bounds from the used positions
                var bb = new Bounds(pos[used[0]], Vector3.zero);
                for (int i = 1; i < used.Count; i++) bb.Encapsulate(pos[used[i]]);
                m.subMeshCount = 1;
                m.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length, MeshTopology.Triangles) { bounds = bb, vertexCount = used.Count }, MeshUpdateFlags.DontRecalculateBounds);
                m.bounds = bb;

                var go = new GameObject(m.name) { hideFlags = HideFlags.DontSave, layer = mr.gameObject.layer };
                // a sibling with the same local transform: correct under any parent, unloads with the scene
                go.transform.SetParent(tr.parent, false);
                go.transform.localPosition = tr.localPosition; go.transform.localRotation = tr.localRotation; go.transform.localScale = tr.localScale;
                go.AddComponent<TileMarker>();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                r.lightProbeUsage = mr.lightProbeUsage; r.reflectionProbeUsage = mr.reflectionProbeUsage;
                r.motionVectorGenerationMode = mr.motionVectorGenerationMode; r.renderingLayerMask = mr.renderingLayerMask;
                r.sortingLayerID = mr.sortingLayerID; r.sortingOrder = mr.sortingOrder;
                if (mr.HasPropertyBlock()) { var mpb = new MaterialPropertyBlock(); mr.GetPropertyBlock(mpb); r.SetPropertyBlock(mpb); }
                made++;
            }
            pos.Dispose(); idx.Dispose();
            return made;
        }
    }
}

namespace RoomRun
{
    // Splits after each field load: a few frames in, and again later for stage scripts that build their meshes late.
    public sealed class MeshChunkerRunner : MonoBehaviour
    {
        int sceneSeen = -1, frames;
        static readonly int[] PassAt = { 3, 30, 120 };

        void LateUpdate()
        {
            int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            var run = RunDirector.Instance;
            if (run && run.Transitioning) return;
            if (scene != sceneSeen) { sceneSeen = scene; frames = 0; }
            frames++;
            if (System.Array.IndexOf(PassAt, frames) < 0) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int n = MeshChunker.SplitScene(out int tiles);
            if (n > 0) Debug.Log($"[RoomRun] MeshChunker: split {n} meshes into {tiles} tiles in {sw.ElapsedMilliseconds} ms (frame {frames})");
        }
    }
}
