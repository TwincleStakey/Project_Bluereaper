// GPU-instanced plants for large fields: instances are bucketed into 8 m chunks per mesh and material,
// culled per camera, and drawn just before that camera renders (game, scene view and manual captures).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyInstancer : MonoBehaviour
{
    // 8 m chunks: 16 m was measured (11-10) to add ~15% triangles for no CPU gain
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

    // 11-10 perf: lighter copies of the heavy flower/plant models (Resources/LookStudyLOD, made by
    // BlenderSource~/make_lods.py: flowers 4,000 -> 400 triangles). Same count of plants; the light copy is drawn unless
    // the camera is close to that chunk. Used only when its bounds match the original (same pivot, scale, orientation).
    const float LodNear = 14f;
    static Dictionary<Mesh, Mesh> lods;
    static Mesh Lod(Mesh src)
    {
        if (lods == null)
        {
            lods = new Dictionary<Mesh, Mesh>();
            byName = new Dictionary<string, Mesh>();
            foreach (var m in Resources.LoadAll<Mesh>("LookStudyLOD")) byName[m.name.Replace("_LOD", "")] = m;
        }
        if (lods.TryGetValue(src, out var l)) return l;
        Mesh found = null;
        foreach (var kv in byName) if (src.name == kv.Key || src.name.StartsWith(kv.Key)) { found = kv.Value; break; }
        if (found)
        {
            Vector3 a = src.bounds.size, b = found.bounds.size, c = src.bounds.center, d = found.bounds.center;
            float tol = 0.12f * Mathf.Max(a.x, Mathf.Max(a.y, a.z));
            bool match = Mathf.Abs(a.x - b.x) < tol && Mathf.Abs(a.y - b.y) < tol && Mathf.Abs(a.z - b.z) < tol && (c - d).magnitude < tol;
            if (!match) Debug.LogWarning($"[LookStudyInstancer] LOD for {src.name} skipped: bounds {src.bounds} vs {found.bounds}");
            if (!match) found = null;
        }
        lods[src] = found ? found : src;
        return lods[src];
    }
    static Dictionary<string, Mesh> byName;

    void Draw(ScriptableRenderContext ctx, Camera cam)
    {
        if (!isActiveAndEnabled || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        // cameras that cannot see this layer (water-ripple / trail / effect cameras) never draw the plants
        if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        foreach (var b in buckets)
        {
            if (!b.hasBounds || !GeometryUtility.TestPlanesAABB(planes, b.bounds)) continue;
            if (b.blocks == null)
            {
                b.blocks = new MaterialPropertyBlock[b.mats.Count];
                for (int k = 0; k < b.mats.Count; k++) { b.blocks[k] = new MaterialPropertyBlock(); b.blocks[k].SetVectorArray("_Tint", b.tints[k]); }
            }
            var mesh = b.bounds.SqrDistance(cam.transform.position) > LodNear * LodNear ? Lod(b.mesh) : b.mesh;
            for (int k = 0; k < b.mats.Count; k++)
                Graphics.DrawMeshInstanced(mesh, 0, NoAO(b.mat), b.mats[k], b.counts[k], b.blocks[k], ShadowCastingMode.Off, true, gameObject.layer, cam);
        }
    }
}
