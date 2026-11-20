// 저승 피안화 field: every spider lily plant (CC0 scan, Resources/Hell/SpiderLily) drawn with GPU instancing in one call.
// The scan's UVs are fragmented (~10k verts after the 4k-tri decimation), so combining ~200 copies into clump meshes
// would cost ~100 MB of vertex data; instancing keeps one mesh and a matrix per plant. No GameObjects, no colliders:
// knee-high ground dressing that never blocks movement.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Underworld
{
    public class UWLilyField : MonoBehaviour
    {
        public Mesh mesh;
        public Material material;
        public readonly List<Matrix4x4> plants = new();
        Matrix4x4[] arr;
        RenderParams rp;

        public void Init(Mesh m, Material mat)
        {
            mesh = m; material = mat;
            rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
        }

        public void Bake()
        {
            arr = plants.ToArray();
            if (arr.Length == 0) return;
            Vector3 mn = arr[0].GetPosition(), mx = mn;
            foreach (var t in arr) { mn = Vector3.Min(mn, t.GetPosition()); mx = Vector3.Max(mx, t.GetPosition()); }
            var b = new Bounds(); b.SetMinMax(mn - Vector3.one, mx + Vector3.one * 1.5f);
            rp.worldBounds = b;
        }

        void Update()
        {
            if (arr == null || arr.Length == 0 || !mesh || !material) return;
            for (int i = 0; i < arr.Length; i += 1023) Graphics.RenderMeshInstanced(rp, mesh, 0, arr, Mathf.Min(1023, arr.Length - i), i);
        }
    }
}
