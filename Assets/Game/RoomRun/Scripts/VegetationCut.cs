// Cut map for grass and reeds (greatsword swings). A small RHalf render texture around the player; each swing stamps
// a sector into it, values above 1 hold the blades cut, then the map decays so they grow back after a few seconds.
// Shaders read it through _RRCut / _RRCutRect / _RRCutOn (Assets/Art/LookStudy/Shaders/RRCut.hlsl).
// Nothing is blitted while no cut is alive, and _RRCutOn = 0 lets the shaders skip the sample.
using UnityEngine;

namespace RoomRun
{
    public sealed class VegetationCut : MonoBehaviour
    {
        public static VegetationCut Instance;
        public float size = 160f;
        public int resolution = 768;
        public float hold = 2.2f;      // stamp strength; >1 keeps the blades fully cut
        public float regrowPerSec = 0.32f;

        RenderTexture a, b;
        Material mat;
        Vector2 center;
        bool centered;
        float alive;   // seconds until the map is fully regrown
        readonly Vector4[] stampA = new Vector4[8], stampB = new Vector4[8];
        int stamps;

        static readonly int IdCut = Shader.PropertyToID("_RRCut"), IdRect = Shader.PropertyToID("_RRCutRect"),
            IdOn = Shader.PropertyToID("_RRCutOn");

        void OnEnable()
        {
            Instance = this;
            var sh = Resources.Load<Shader>("RR_VegCut");
            if (!sh) { Debug.LogWarning("[RoomRun] RR_VegCut shader missing"); enabled = false; return; }
            mat = new Material(sh);
            a = Make(); b = Make();
            Shader.SetGlobalFloat(IdOn, 0f);
        }

        RenderTexture Make()
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RHalf) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RR_VegCut" };
            rt.Create();
            Clear(rt);
            return rt;
        }

        static void Clear(RenderTexture rt)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, Color.clear); RenderTexture.active = prev;
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(IdOn, 0f);
            if (a) { a.Release(); Destroy(a); }
            if (b) { b.Release(); Destroy(b); }
            if (mat) Destroy(mat);
            if (Instance == this) Instance = null;
        }

        /// Cut a sector: centre, facing (flat), radius (m), half angle (deg, 180 = full circle).
        public void Sector(Vector3 pos, Vector3 dir, float radius, float halfAngleDeg)
        {
            if (!mat) return;
            var p = new Vector2(pos.x, pos.z);
            if (!centered || (p - center).sqrMagnitude > (size * 0.3f) * (size * 0.3f))
            {
                // re-centre on the player (a new room); old cuts are dropped
                center = p; centered = true;
                Clear(a); Clear(b);
            }
            if (stamps >= stampA.Length) return;
            dir.y = 0; if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
            stampA[stamps] = new Vector4(p.x, p.y, radius, hold);
            stampB[stamps] = new Vector4(dir.x, dir.z, Mathf.Cos(Mathf.Min(halfAngleDeg, 180f) * Mathf.Deg2Rad), 0);
            stamps++;
            alive = hold / regrowPerSec + 0.5f;
        }

        void LateUpdate()
        {
            if (!mat) return;
            if (alive <= 0f && stamps == 0) { Shader.SetGlobalFloat(IdOn, 0f); return; }
            float dt = Time.deltaTime;
            alive -= dt;
            var rect = new Vector4(center.x - size * 0.5f, center.y - size * 0.5f, size, 0);
            for (int i = stamps; i < stampA.Length; i++) { stampA[i] = Vector4.zero; stampB[i] = Vector4.zero; }
            mat.SetVector("_Rect", rect);
            mat.SetVectorArray("_StampA", stampA);
            mat.SetVectorArray("_StampB", stampB);
            mat.SetFloat("_Decay", regrowPerSec * dt);
            stamps = 0;
            Graphics.Blit(a, b, mat);
            (a, b) = (b, a);
            Shader.SetGlobalTexture(IdCut, a);
            Shader.SetGlobalVector(IdRect, rect);
            Shader.SetGlobalFloat(IdOn, alive > 0f ? 1f : 0f);
        }
    }
}
