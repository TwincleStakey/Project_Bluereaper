// GPU-instanced plants for large fields: instances are bucketed into 8 m chunks per mesh and material,
// culled per camera, and drawn just before that camera renders (game, scene view and manual captures).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class LookStudyInstancer : MonoBehaviour, VegetationQuery.ISource
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

    void OnEnable() { RenderPipelineManager.beginCameraRendering += Draw; VegetationQuery.Register(this); }
    void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; VegetationQuery.Unregister(this); ReleaseProc(); groupsDirty = true; }

    // Greatsword cut debris: cuttable plants inside a swing sector (see VegetationQuery).
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
                        var h = hits[slot]; h.mesh = b.mesh; h.mat = b.mat; h.rot = m.rotation; h.scale = ((Vector3)m.GetColumn(1)).magnitude; h.matrix = m; h.tint = b.tints[k][i]; hits[slot] = h;
                    }
        }
    }

    public void Clear() { map.Clear(); buckets.Clear(); groups.Clear(); ReleaseProc(); groupsDirty = true; Count = 0; }

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
        groupsDirty = true;
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

    // For the room-run census: triangles this instancer would draw for a camera, per mesh actually drawn.
    public Dictionary<string, long> VisibleTris(Camera cam)
    {
        var res = new Dictionary<string, long>();
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        if (groupsDirty) BuildGroups();
        void Count(Bucket b, Mesh mesh)
        {
            long t = 0; for (int i = 0; i < mesh.subMeshCount; i++) t += mesh.GetIndexCount(i) / 3;
            int n = 0; foreach (var c in b.counts) n += c;
            string key = $"{mesh.name} ({b.mat.shader.name}, {t} tris)";
            res[key] = (res.TryGetValue(key, out var o) ? o : 0) + t * n;
            res["~draw calls"] = (res.TryGetValue("~draw calls", out var d) ? d : 0) + b.mats.Count;
        }
        foreach (var g in groups)
        {
            if (!GeometryUtility.TestPlanesAABB(planes, g.bounds)) continue;
            if (g.bounds.SqrDistance(cam.transform.position) > FarDist * FarDist) { Count(g, Far(g.mesh) ? Far(g.mesh) : Lod(g.mesh)); continue; }
            foreach (var b in g.children) if (GeometryUtility.TestPlanesAABB(planes, b.bounds)) Count(b, PickMesh(b, cam));
        }
        return res;
    }

    Mesh PickMesh(Bucket b, Camera cam)
    {
        float d2 = b.bounds.SqrDistance(cam.transform.position);
        if (d2 > FarDist * FarDist) { var f = Far(b.mesh); if (f) return f; }
        return d2 > LodNear * LodNear ? Lod(b.mesh) : b.mesh;
    }

    // 11-11 perf (Kido: "줌아웃하면 FPS가 더 떨어지나?" - grass 208 -> 62 fps at full zoom-out, 35M triangles, 31M of
    // them the Grass_Clump meshes). Chunks farther than FarDist from the camera draw the grass clumps and loose blades
    // with each blade cut to 3 triangles (base, middle, tip of its own vertices, so UVs, normals and wind stay the
    // same). The camera sits ~22 m from the ground at zoom 1, so this only shows when zoomed out. Same plant count.
    const float FarDist = 45f;
    static readonly Dictionary<Mesh, Mesh> far = new Dictionary<Mesh, Mesh>();
    static Mesh Far(Mesh src)
    {
        if (far.TryGetValue(src, out var m)) return m;
        m = null;
        if (src.name.StartsWith("Grass_C") || src.name.StartsWith("Blade_")) m = BladeLod(src);
        far[src] = m;
        return m;
    }

    static Mesh BladeLod(Mesh src)
    {
        if (!src.isReadable || src.subMeshCount != 1) return null;
        var v = src.vertices; var t = src.triangles;
        // islands: vertices joined by triangles (union-find), plus vertices at the same position (split UV seams)
        var parent = new int[v.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        for (int i = 0; i < t.Length; i += 3) { Join(t[i], t[i + 1]); Join(t[i + 1], t[i + 2]); }
        var byPos = new Dictionary<Vector3, int>();
        for (int i = 0; i < v.Length; i++) { if (byPos.TryGetValue(v[i], out var o)) Join(i, o); else byPos[v[i]] = i; }
        var islands = new Dictionary<int, List<int>>();
        for (int i = 0; i < v.Length; i++) { int r = Find(i); if (!islands.TryGetValue(r, out var l)) islands[r] = l = new List<int>(); l.Add(i); }
        var tri = new List<int>();
        foreach (var l in islands.Values)
        {
            if (l.Count < 6) continue; // already a sliver
            l.Sort((a, b) => v[a].y.CompareTo(v[b].y));
            int b0 = l[0], b1 = l[1], tip = l[l.Count - 1];
            int mi = Mathf.Clamp(l.Count / 2, 2, l.Count - 3);
            int m0 = l[mi], m1 = l[mi + 1];
            // pair each middle vertex with the base vertex on its side
            if ((v[m0] - v[b0]).sqrMagnitude + (v[m1] - v[b1]).sqrMagnitude > (v[m0] - v[b1]).sqrMagnitude + (v[m1] - v[b0]).sqrMagnitude) { var s = m0; m0 = m1; m1 = s; }
            tri.Add(b0); tri.Add(b1); tri.Add(m0);
            tri.Add(b1); tri.Add(m1); tri.Add(m0);
            tri.Add(m0); tri.Add(m1); tri.Add(tip);
        }
        if (tri.Count == 0 || tri.Count >= t.Length) return null;
        var m = Object.Instantiate(src);
        m.name = src.name + "_Far"; m.hideFlags = HideFlags.DontSave;
        m.SetTriangles(tri, 0, false);
        m.bounds = src.bounds;
        return m;
    }

    void Draw(ScriptableRenderContext ctx, Camera cam)
    {
        if (!isActiveAndEnabled || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        // cameras that cannot see this layer (water-ripple / trail / effect cameras) never draw the plants
        if ((cam.cullingMask & (1 << gameObject.layer)) == 0) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        if (groupsDirty) BuildGroups();
        var camPos = cam.transform.position;
        var pack = packVisible && procMats != null && procMats.IsValid() ? PackFor(cam) : null;
        foreach (var g in groups)
        {
            if (!GeometryUtility.TestPlanesAABB(planes, g.bounds)) continue;
            // a whole far group: few big draws with the far mesh (or the usual distance mesh when it has none)
            if (g.bounds.SqrDistance(camPos) > FarDist * FarDist)
            {
                var fm = Far(g.mesh);
                if (!fm) fm = Lod(g.mesh);
                if (pack != null && Collect(pack, g, fm)) continue;
                if (!DrawProcedural(g, fm, cam)) DrawArrays(g, fm, cam);
                continue;
            }
            // nearer groups: chunk by chunk (culled, per-chunk mesh choice), still from the buffer
            foreach (var b in g.children)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, b.bounds)) continue;
                var mesh = PickMesh(b, cam);
                if (pack != null && Collect(pack, b, mesh)) continue;
                if (!DrawProcedural(b, mesh, cam)) DrawArrays(b, mesh, cam);
            }
        }
        if (pack != null) FlushPack(pack, cam);
    }

    // 11-12 perf (snow field, opt-in): the visible chunks are culled as above, but instead of one RenderMeshPrimitives
    // per chunk (each with its own property block, so each was its own SetPass: ~450 in the snow field) the visible
    // instance ids of every mesh + material are packed into a per-camera index buffer and drawn with ONE call. Same
    // instances, same meshes; needs a shader with the LS_PROC_IDX keyword (reads _LSProcIdx[SV_InstanceID + _LSProcBase]).
    // Materials whose shader has no LS_PROC_IDX keep the per-chunk path.
    [Tooltip("Pack the visible chunks of each mesh/material into one draw (shader needs LS_PROC_IDX).")]
    public bool packVisible;
    static readonly int ID_ProcIdx = Shader.PropertyToID("_LSProcIdx");
    class PackDraw { public Mesh mesh; public Material mat; public Bounds bounds; public int start, count; public readonly List<Vector2Int> ranges = new List<Vector2Int>(); public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
    class CamPack { public GraphicsBuffer idx; public uint[] data; public readonly Dictionary<(Mesh, Material), PackDraw> draws = new Dictionary<(Mesh, Material), PackDraw>(); public readonly List<PackDraw> used = new List<PackDraw>(); }
    readonly Dictionary<Camera, CamPack> camPacks = new Dictionary<Camera, CamPack>();
    static readonly Dictionary<Shader, bool> idxShader = new Dictionary<Shader, bool>();
    static readonly Dictionary<Material, Material> idxMat = new Dictionary<Material, Material>();
    static readonly Dictionary<Material, int> idxFrame = new Dictionary<Material, int>();

    static bool HasIdx(Material m)
    {
        if (!m || !m.shader) return false;
        if (!idxShader.TryGetValue(m.shader, out var ok)) idxShader[m.shader] = ok = m.shader.keywordSpace.FindKeyword("LS_PROC_IDX").isValid;
        return ok;
    }

    static Material IdxMat(Material src)
    {
        if (!idxMat.TryGetValue(src, out var m) || !m)
        {
            m = new Material(src) { name = src.name + " (proc idx)", hideFlags = HideFlags.DontSave, enableInstancing = false };
            idxMat[src] = m; idxFrame[src] = -1;
        }
        if (idxFrame[src] != Time.frameCount)
        {
            m.CopyPropertiesFromMaterial(src);
            m.DisableKeyword("LS_PROC"); m.EnableKeyword("LS_PROC_IDX");
            m.SetShaderPassEnabled("DepthNormals", false);
            idxFrame[src] = Time.frameCount;
        }
        return m;
    }

    CamPack PackFor(Camera cam)
    {
        if (!camPacks.TryGetValue(cam, out var p))
        {
            // forget cameras that are gone (captures, closed scene views)
            List<Camera> dead = null;
            foreach (var k in camPacks.Keys) if (!k) (dead ??= new List<Camera>()).Add(k);
            if (dead != null) foreach (var k in dead) { camPacks[k].idx?.Release(); camPacks.Remove(k); }
            camPacks[cam] = p = new CamPack();
        }
        foreach (var d in p.used) { d.ranges.Clear(); d.count = 0; }
        p.used.Clear();
        return p;
    }

    bool Collect(CamPack p, Bucket b, Mesh mesh)
    {
        if (!HasIdx(b.mat) || !procRange.TryGetValue(b, out var r) || r.count == 0) return false;
        var key = (mesh, b.mat);
        if (!p.draws.TryGetValue(key, out var d)) p.draws[key] = d = new PackDraw { mesh = mesh, mat = b.mat };
        if (d.ranges.Count == 0) { p.used.Add(d); d.bounds = b.bounds; } else d.bounds.Encapsulate(b.bounds);
        // a chunk that follows the previous one in the buffer extends its range
        int last = d.ranges.Count - 1;
        if (last >= 0 && d.ranges[last].x + d.ranges[last].y == r.start) d.ranges[last] = new Vector2Int(d.ranges[last].x, d.ranges[last].y + r.count);
        else d.ranges.Add(new Vector2Int(r.start, r.count));
        d.count += r.count;
        return true;
    }

    void FlushPack(CamPack p, Camera cam)
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
            d.mpb.SetBuffer(ID_ProcMats, procMats); d.mpb.SetBuffer(ID_ProcTints, procTints); d.mpb.SetBuffer(ID_ProcIdx, p.idx);
            d.mpb.SetInt(ID_ProcBase, d.start);
            var rp = new RenderParams(IdxMat(d.mat))
            {
                camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
                worldBounds = d.bounds, matProps = d.mpb
            };
            Graphics.RenderMeshPrimitives(rp, d.mesh, 0, d.count);
        }
    }

    void DrawArrays(Bucket b, Mesh mesh, Camera cam)
    {
        if (b.blocks == null)
        {
            b.blocks = new MaterialPropertyBlock[b.mats.Count];
            for (int k = 0; k < b.mats.Count; k++) { b.blocks[k] = new MaterialPropertyBlock(); b.blocks[k].SetVectorArray("_Tint", b.tints[k]); }
        }
        var mat = NoAO(b.mat);
        for (int k = 0; k < b.mats.Count; k++)
            Graphics.DrawMeshInstanced(mesh, 0, mat, b.mats[k], b.counts[k], b.blocks[k], ShadowCastingMode.Off, true, gameObject.layer, cam);
    }

    // 11-11 perf: at full zoom-out the field is ~700 chunks x a dozen meshes, and the draw calls (not the triangles) were
    // the cost: 1,370 calls, 16 ms of main thread. Chunks are grouped 4x4 (32 m) per mesh and material; a group wholly
    // beyond FarDist is drawn as one set of packed arrays, nearer groups chunk by chunk as before.
    const int GroupSpan = 4;
    class Group : Bucket { public readonly List<Bucket> children = new List<Bucket>(); }
    readonly List<Group> groups = new List<Group>();
    bool groupsDirty = true;

    // 11-11 perf: DrawMeshInstanced re-sends every instance's matrix each frame (64 B x ~200k plants at full zoom-out,
    // ~15 ms of CPU in a player build). Far groups instead live in one GPU buffer, uploaded once, and are drawn with
    // RenderMeshPrimitives; LookStudy/Foliage reads the matrix and tint by instance id under the LS_PROC keyword.
    GraphicsBuffer procMats, procTints;
    static readonly int ID_ProcMats = Shader.PropertyToID("_LSProcMats"), ID_ProcTints = Shader.PropertyToID("_LSProcTints"), ID_ProcBase = Shader.PropertyToID("_LSProcBase");
    readonly Dictionary<Bucket, (int start, int count, MaterialPropertyBlock mpb)> procRange = new Dictionary<Bucket, (int, int, MaterialPropertyBlock)>();
    static readonly Dictionary<Material, Material> procMat = new Dictionary<Material, Material>();
    static readonly Dictionary<Material, int> procFrame = new Dictionary<Material, int>();

    static Material ProcMat(Material src)
    {
        if (!procMat.TryGetValue(src, out var m) || !m)
        {
            m = new Material(src) { name = src.name + " (proc)", hideFlags = HideFlags.DontSave, enableInstancing = false };
            procMat[src] = m; procFrame[src] = -1;
        }
        if (procFrame[src] != Time.frameCount)
        {
            // re-synced once a frame like NoAO, so dev-panel colour changes still show
            m.CopyPropertiesFromMaterial(src);
            m.EnableKeyword("LS_PROC");
            m.SetShaderPassEnabled("DepthNormals", false);
            procFrame[src] = Time.frameCount;
        }
        return m;
    }

    void ReleaseProc()
    {
        procMats?.Release(); procTints?.Release(); procMats = procTints = null; procRange.Clear();
        foreach (var p in camPacks.Values) p.idx?.Release();
        camPacks.Clear();
    }

    void BuildProc()
    {
        ReleaseProc();
        int total = 0;
        foreach (var g in groups) foreach (var c in g.counts) total += c;
        if (total == 0 || !SystemInfo.supportsInstancing) return;
        var m = new Matrix4x4[total]; var t = new Vector4[total];
        int at = 0;
        // laid out group by group, and inside a group chunk by chunk, so a group and each of its chunks are one range
        foreach (var g in groups)
        {
            int start = at;
            foreach (var b in g.children)
            {
                int cs = at;
                for (int k = 0; k < b.mats.Count; k++) { System.Array.Copy(b.mats[k], 0, m, at, b.counts[k]); System.Array.Copy(b.tints[k], 0, t, at, b.counts[k]); at += b.counts[k]; }
                var cm = new MaterialPropertyBlock(); cm.SetInt(ID_ProcBase, cs);
                procRange[b] = (cs, at - cs, cm);
            }
            var mpb = new MaterialPropertyBlock();
            mpb.SetInt(ID_ProcBase, start);
            procRange[g] = (start, at - start, mpb);
        }
        procMats = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, 64);
        procTints = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, 16);
        procMats.SetData(m); procTints.SetData(t);
        foreach (var r in procRange.Values) { r.mpb.SetBuffer(ID_ProcMats, procMats); r.mpb.SetBuffer(ID_ProcTints, procTints); }
    }

    bool DrawProcedural(Bucket g, Mesh mesh, Camera cam)
    {
        if (procMats == null || !procMats.IsValid() || !procRange.TryGetValue(g, out var r) || r.count == 0) return false;
        var rp = new RenderParams(ProcMat(g.mat))
        {
            camera = cam, layer = gameObject.layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true,
            worldBounds = g.bounds, matProps = r.mpb
        };
        Graphics.RenderMeshPrimitives(rp, mesh, 0, r.count);
        return true;
    }

    void OnDestroy() => ReleaseProc();

    void BuildGroups()
    {
        groupsDirty = false;
        groups.Clear();
        var gmap = new Dictionary<(int, int, Mesh, Material), Group>();
        foreach (var kv in map)
        {
            var b = kv.Value;
            if (!b.hasBounds) continue;
            var key = (Mathf.FloorToInt(kv.Key.Item1 / (float)GroupSpan), Mathf.FloorToInt(kv.Key.Item2 / (float)GroupSpan), b.mesh, b.mat);
            if (!gmap.TryGetValue(key, out var g)) { gmap[key] = g = new Group { mesh = b.mesh, mat = b.mat, bounds = b.bounds, hasBounds = true }; groups.Add(g); }
            g.children.Add(b); g.bounds.Encapsulate(b.bounds);
            for (int k = 0; k < b.mats.Count; k++)
                for (int i = 0; i < b.counts[k]; i++)
                {
                    if (g.counts.Count == 0 || g.counts[g.counts.Count - 1] == Max) { g.mats.Add(new Matrix4x4[Max]); g.tints.Add(new Vector4[Max]); g.counts.Add(0); }
                    int arr = g.counts.Count - 1, n = g.counts[arr];
                    g.mats[arr][n] = b.mats[k][i]; g.tints[arr][n] = b.tints[k][i]; g.counts[arr] = n + 1;
                }
        }
        BuildProc();
    }
}
