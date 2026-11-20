// Feeds the river shader (ReedField/River) the rocks standing in the current, so it draws a white collar around each
// and a wake trailing downstream. Rocks are this object's children (up to 6); their radius comes from their bounds.
using UnityEngine;

namespace Reeds
{
    [ExecuteAlways]
    public class RFRiver : MonoBehaviour
    {
        static readonly int ID_Rocks = Shader.PropertyToID("_RF_Rocks");
        readonly Vector4[] rocks = new Vector4[6];

        void Update()
        {
            int n = 0;
            foreach (Transform c in transform)
            {
                if (n >= rocks.Length) break;
                var r = c.GetComponentInChildren<Renderer>();
                if (!r) continue;
                var b = r.bounds;
                rocks[n++] = new Vector4(b.center.x, b.center.z, Mathf.Max(b.extents.x, b.extents.z) * 0.8f, 0);
            }
            for (int i = n; i < rocks.Length; i++) rocks[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(ID_Rocks, rocks);
        }

        void OnDisable() => Shader.SetGlobalVectorArray(ID_Rocks, new Vector4[6]);
    }
}
