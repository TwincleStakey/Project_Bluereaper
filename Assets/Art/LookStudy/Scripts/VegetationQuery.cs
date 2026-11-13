// Which cuttable plants (grass, foliage, reeds) stand inside a swing's sector, for the greatsword's cut debris.
// The instancers (LookStudyInstancer, Reeds.ReedInstancer) register as sources; RoomRun's GreatswordCombat asks here,
// so debris only flies where something was actually cut, in that plant's colour.
using System.Collections.Generic;
using UnityEngine;

public static class VegetationQuery
{
    public enum Kind { None, Grass, Reed }

    public struct Hit
    {
        public Vector3 pos; public Kind kind; public Color color;
        public Mesh mesh; public Material mat; public Quaternion rot; public float scale;   // instanced plant
        public Mesh field; public int vertex;                                              // blade in a baked grass field mesh
    }

    public interface ISource
    {
        /// Add plants inside the sector (centre, flat unit dir, radius, cos of half angle) to hits, reservoir-sampled to max.
        void Collect(Vector3 centre, Vector3 dir, float radius, float cosHalf, List<Hit> hits, int max, ref int seen);
    }

    static readonly List<ISource> sources = new List<ISource>();
    public static void Register(ISource s) { if (!sources.Contains(s)) sources.Add(s); }
    public static void Unregister(ISource s) => sources.Remove(s);

    /// Up to max plants cut by the sector (random sample when there are more). Returns how many stood in it.
    public static int Collect(Vector3 centre, Vector3 dir, float radius, float halfAngleDeg, List<Hit> hits, int max)
    {
        hits.Clear();
        dir.y = 0; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
        float cosHalf = Mathf.Cos(Mathf.Min(halfAngleDeg, 180f) * Mathf.Deg2Rad);
        int seen = 0;
        for (int i = sources.Count - 1; i >= 0; i--)
        {
            var s = sources[i];
            if (s == null || (s is Object o && !o)) { sources.RemoveAt(i); continue; }
            s.Collect(centre, dir, radius, cosHalf, hits, max, ref seen);
        }
        return seen;
    }

    /// Cuttable kind of a vegetation material (the shaders that read the cut map), and the colour its bits should have.
    public static Kind KindOf(Material m, out Color color)
    {
        color = Color.white;
        if (!m || !m.shader) return Kind.None;
        string n = m.shader.name;
        if (n == "LookStudy/Grass")
        {
            color = Color.Lerp(Get(m, "_ColB", new Color(0.22f, 0.66f, 0.16f)), Get(m, "_ColC", new Color(0.62f, 0.84f, 0.22f)), 0.35f);
            return Kind.Grass;
        }
        if (n == "LookStudy/Foliage")
        {
            color = Color.Lerp(Get(m, "_Dark", new Color(0.05f, 0.3f, 0.17f)), Get(m, "_Light", new Color(0.42f, 0.82f, 0.25f)), 0.6f);
            return Kind.Grass;
        }
        if (n == "ReedField/Reed")
        {
            // dry straw: the leaf tip / stem colours, lifted so they read in the dark field
            var c = Color.Lerp(Get(m, "_LeafTip", new Color(0.42f, 0.38f, 0.28f)), Get(m, "_StemCol", new Color(0.38f, 0.34f, 0.26f)), 0.5f);
            color = new Color(Mathf.Min(1, c.r * 1.9f), Mathf.Min(1, c.g * 1.9f), Mathf.Min(1, c.b * 1.8f));
            return Kind.Reed;
        }
        return Kind.None;
    }

    static Color Get(Material m, string p, Color fallback) => m.HasProperty(p) ? m.GetColor(p) : fallback;

    /// Shared test + reservoir sampling for sources. Returns the slot the plant was stored in (fill its mesh etc.), or -1.
    public static int Offer(Vector3 p, Vector3 centre, Vector3 dir, float radius, float cosHalf, Kind kind, Color color,
                             List<Hit> hits, int max, ref int seen)
    {
        float dx = p.x - centre.x, dz = p.z - centre.z;
        float d2 = dx * dx + dz * dz;
        if (d2 > radius * radius) return -1;
        if (cosHalf > -0.999f && d2 > 0.36f)
        {
            float d = Mathf.Sqrt(d2);
            if ((dx * dir.x + dz * dir.z) / d < cosHalf) return -1;
        }
        seen++;
        var h = new Hit { pos = p, kind = kind, color = color, rot = Quaternion.identity, scale = 1f, vertex = -1 };
        if (hits.Count < max) { hits.Add(h); return hits.Count - 1; }
        int j = Random.Range(0, seen);
        if (j < max) { hits[j] = h; return j; }
        return -1;
    }

    // ---------- baked grass fields (LookStudyWorld.BuildGrass: chunk meshes, 5 vertices per blade, uv1 = root xyz + rnd)
    static readonly FieldGrass fieldGrass = new FieldGrass();
    static VegetationQuery() { sources.Add(fieldGrass); }

    sealed class FieldGrass : ISource
    {
        readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        readonly Dictionary<Mesh, Vector4[]> roots = new Dictionary<Mesh, Vector4[]>();
        int sceneStamp = -1;
        float lastScan = -99f;
        readonly List<Vector4> tmp = new List<Vector4>();

        void Refresh()
        {
            int stamp = UnityEngine.SceneManagement.SceneManager.sceneCount * 7919;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                stamp = stamp * 31 + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).handle;
            // fields are built at runtime after the scene loads: rescan now and then while none was found
            if (stamp == sceneStamp && renderers.TrueForAll(r => r) && (renderers.Count > 0 || Time.unscaledTime - lastScan < 3f)) return;
            sceneStamp = stamp; lastScan = Time.unscaledTime;
            renderers.Clear(); roots.Clear();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (!m || !m.shader || m.shader.name != "LookStudy/Grass") continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable || mf.sharedMesh.vertexCount % 5 != 0) continue;
                renderers.Add(r);
            }
        }

        public void Collect(Vector3 centre, Vector3 dir, float radius, float cosHalf, List<Hit> hits, int max, ref int seen)
        {
            Refresh();
            foreach (var r in renderers)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var bb = r.bounds;
                if (centre.x + radius < bb.min.x || centre.x - radius > bb.max.x || centre.z + radius < bb.min.z || centre.z - radius > bb.max.z) continue;
                var mesh = r.GetComponent<MeshFilter>().sharedMesh;
                if (!roots.TryGetValue(mesh, out var rt))
                {
                    tmp.Clear(); mesh.GetUVs(1, tmp);
                    rt = tmp.ToArray(); roots[mesh] = rt;
                }
                if (rt.Length != mesh.vertexCount) continue;
                var kind = KindOf(r.sharedMaterial, out var col);
                for (int v = 0; v < rt.Length; v += 5)
                {
                    var p = new Vector3(rt[v].x, rt[v].y, rt[v].z);
                    int slot = Offer(p, centre, dir, radius, cosHalf, kind, col, hits, max, ref seen);
                    if (slot >= 0) { var h = hits[slot]; h.field = mesh; h.vertex = v; hits[slot] = h; }
                }
            }
        }
    }
}
