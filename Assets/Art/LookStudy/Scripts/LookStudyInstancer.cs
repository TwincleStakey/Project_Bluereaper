// GPU-instanced plants for large fields: instances are bucketed into 8 m chunks per mesh and material,
// culled per camera, and drawn just before that camera renders (game, scene view and manual captures).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyInstancer : MonoBehaviour
{
    const float Chunk = 8f;
    const int Max = 1023;

    class Bucket
    {
        public Mesh mesh; public Material mat;
        public readonly List<Matrix4x4[]> mats = new List<Matrix4x4[]>();
        public readonly List<Vector4[]> tints = new List<Vector4[]>();
        public readonly List<int> counts = new List<int>();
        public Bounds bounds; public bool hasBounds;
        public MaterialPropertyBlock[] blocks;
    }

    readonly Dictionary<(int, int, Mesh, Material), Bucket> map = new Dictionary<(int, int, Mesh, Material), Bucket>();
    readonly List<Bucket> buckets = new List<Bucket>();
    readonly Plane[] planes = new Plane[6];
    public int Count { get; private set; }
    // Combined bounds of every instance, for systems that size rooms from renderers (RoomRun reads it by reflection).
    public Bounds WorldBounds { get { var b = new Bounds(); bool any = false; foreach (var k in buckets) if (k.hasBounds) { if (any) b.Encapsulate(k.bounds); else { b = k.bounds; any = true; } } return b; } }

    void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; }

    public void Clear() { map.Clear(); buckets.Clear(); Count = 0; }

    public void Add(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, float scale, Color tint)
    {
        if (!mesh || !mat) return;
        if (!mat.enableInstancing) mat.enableInstancing = true;
        var key = (Mathf.FloorToInt(pos.x / Chunk), Mathf.FloorToInt(pos.z / Chunk), mesh, mat);
        if (!map.TryGetValue(key, out var b)) { map[key] = b = new Bucket { mesh = mesh, mat = mat }; buckets.Add(b); }
        if (b.counts.Count == 0 || b.counts[b.counts.Count - 1] == Max)
        {
            b.mats.Add(new Matrix4x4[Max]); b.tints.Add(new Vector4[Max]); b.counts.Add(0);
        }
        int arr = b.counts.Count - 1, i = b.counts[arr];
        b.mats[arr][i] = Matrix4x4.TRS(pos, rot, Vector3.one * scale);
        b.tints[arr][i] = tint;
        b.counts[arr] = i + 1;
        var wb = new Bounds(pos + Vector3.up * mesh.bounds.size.y * scale * 0.5f, mesh.bounds.size * scale * 1.4f + Vector3.one);
        if (b.hasBounds) b.bounds.Encapsulate(wb); else { b.bounds = wb; b.hasBounds = true; }
        Count++;
    }

    void Draw(ScriptableRenderContext ctx, Camera cam)
    {
        if (!isActiveAndEnabled || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        foreach (var b in buckets)
        {
            if (!b.hasBounds || !GeometryUtility.TestPlanesAABB(planes, b.bounds)) continue;
            if (b.blocks == null)
            {
                b.blocks = new MaterialPropertyBlock[b.mats.Count];
                for (int k = 0; k < b.mats.Count; k++) { b.blocks[k] = new MaterialPropertyBlock(); b.blocks[k].SetVectorArray("_Tint", b.tints[k]); }
            }
            for (int k = 0; k < b.mats.Count; k++)
                Graphics.DrawMeshInstanced(b.mesh, 0, b.mat, b.mats[k], b.counts[k], b.blocks[k], ShadowCastingMode.Off, true, gameObject.layer, cam);
        }
    }
}
