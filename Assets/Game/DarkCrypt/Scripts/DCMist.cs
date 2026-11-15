// DarkCrypt corruption mist: a violet-black mist lies over the whole courtyard floor (DarkCrypt/MistOverlay on a flat
// quad just above the stones). Where the player walks it is wiped away in blotchy, paint-like splotches and creeps back
// over ~25 s. The cleared amount lives in one small floating-point mask (two RTs ping-ponged by DarkCrypt/MistBrush,
// one blit per frame) mapped over the arena rectangle; the overlay reads it through global properties.
// Nothing here has a collider or a light. In edit mode the mask stays empty (full mist).
using UnityEngine;

namespace DarkCrypt
{
    [DisallowMultipleComponent]
    public class DCMist : MonoBehaviour
    {
        [Tooltip("World rectangle the mask covers: x, z of the min corner, then width and depth (m).")]
        public Vector4 rect = new Vector4(-60f, -48f, 120f, 96f);
        public int resolution = 1024;
        [Tooltip("Radius of the wiped patch around the player (m).")]
        public float radius = 2.4f;
        [Tooltip("How fast the patch under the player clears (mask units per second).")]
        public float clearRate = 3.5f;
        [Tooltip("Seconds for a fully cleared spot to fill back in.")]
        public float regrowSeconds = 25f;
        public Material brush;
        [Tooltip("Optional: the object to follow. In Play the object named Player is used when this is empty.")]
        public Transform target;

        RenderTexture a, b;
        float nextFind;
        Vector3 lastPos; bool hasLast;

        static readonly int ID_Mask = Shader.PropertyToID("_DC_MistMask"), ID_Rect = Shader.PropertyToID("_DC_MistRect"),
            ID_Prev = Shader.PropertyToID("_Prev"), ID_P0 = Shader.PropertyToID("_P0"), ID_P1 = Shader.PropertyToID("_P1"),
            ID_Brush = Shader.PropertyToID("_BrushParams");

        void OnEnable()
        {
            Shader.SetGlobalVector(ID_Rect, rect);
            Shader.SetGlobalTexture(ID_Mask, Texture2D.blackTexture);
        }

        void OnDisable()
        {
            Shader.SetGlobalTexture(ID_Mask, Texture2D.blackTexture);
            Release(ref a); Release(ref b);
        }

        static void Release(ref RenderTexture rt) { if (rt) { rt.Release(); Destroy(rt); } rt = null; }

        RenderTexture Make()
        {
            int h = Mathf.Max(16, Mathf.RoundToInt(resolution * rect.w / Mathf.Max(1f, rect.z)));
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            var rt = new RenderTexture(resolution, h, 0, fmt) { name = "DC_MistMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false };
            rt.Create();
            var keep = RenderTexture.active; RenderTexture.active = rt; GL.Clear(false, true, Color.clear); RenderTexture.active = keep;
            return rt;
        }

        void Update()
        {
            if (!Application.isPlaying || !brush) return;
            if (!a) { a = Make(); b = Make(); }
            if (!target && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 0.5f;
                var go = GameObject.Find("Player");
                if (go) target = go.transform;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            Vector3 p = target ? target.position : new Vector3(1e6f, 0, 1e6f);
            if (!hasLast || (p - lastPos).sqrMagnitude > 25f) lastPos = p;   // teleports (room entry) do not smear a line
            hasLast = target;
            // the wipe is a capsule from last frame's position to this one, so fast movement leaves no gaps
            brush.SetVector(ID_P0, new Vector4(lastPos.x, lastPos.z, 0, 0));
            brush.SetVector(ID_P1, new Vector4(p.x, p.z, 0, 0));
            brush.SetVector(ID_Brush, new Vector4(radius, clearRate * dt, dt / Mathf.Max(1f, regrowSeconds), Time.time));
            brush.SetVector("_Rect", rect);
            brush.SetTexture(ID_Prev, a);
            Graphics.Blit(a, b, brush);
            (a, b) = (b, a);
            lastPos = p;
            Shader.SetGlobalTexture(ID_Mask, a);
            Shader.SetGlobalVector(ID_Rect, rect);
        }
    }
}
