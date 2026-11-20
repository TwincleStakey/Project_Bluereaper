// Ground scatter for the snow plague field (pebbles, stone chips, twigs, dry grass tufts, snow clumps, bones):
// ~10k small instances, no shadows, no colliders. The builder stores them here (mesh, material, x/y/z/yaw, scale);
// on enable they are handed to the shared LookStudyInstancer (one GraphicsBuffer, chunk culling, drawn per camera).
using System.Collections.Generic;
using UnityEngine;

namespace SnowPlague
{
    [ExecuteAlways]
    [RequireComponent(typeof(LookStudyInstancer))]
    public class SPScatter : MonoBehaviour
    {
        [System.Serializable]
        public class Set
        {
            public Mesh mesh;
            public Material mat;
            // the FBX child's own transform relative to the model root (Blender axis fix, unit scale), baked once
            public Quaternion pre = Quaternion.identity;
            public Vector3 preOffset;
            public float preScale = 1f;
            public List<Vector4> at = new List<Vector4>();   // xyz position, w yaw (degrees)
            public List<float> scale = new List<float>();
        }

        public List<Set> sets = new List<Set>();
        public static bool PackVisible = true;   // dev A/B of the packed instanced draw
        public int Count { get { int n = 0; foreach (var s in sets) n += s.at.Count; return n; } }

        void OnEnable() => Push();

        public void Push()
        {
            var inst = GetComponent<LookStudyInstancer>();
            inst.packVisible = PackVisible;   // 11-12 perf: one draw per mesh + material (SPLit LS_PROC_IDX)
            inst.Clear();
            foreach (var s in sets)
            {
                if (!s.mesh || !s.mat) continue;
                for (int i = 0; i < s.at.Count; i++)
                {
                    var a = s.at[i]; float sc = s.scale[i];
                    var yaw = Quaternion.Euler(0, a.w, 0);
                    var rot = yaw * s.pre;
                    var pos = new Vector3(a.x, a.y, a.z) + yaw * (s.preOffset * sc);
                    inst.Add(s.mesh, s.mat, pos, rot, sc * s.preScale, Color.white);
                }
            }
        }
    }
}
