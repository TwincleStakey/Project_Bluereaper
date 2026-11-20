// DarkCrypt corruption mist: a violet-black mist lies over the whole courtyard floor (DarkCrypt/MistOverlay on a flat
// quad just above the stones). Where the player walks it is eaten away in soft blotches and creeps back over ~25 s.
// The cleared amount lives in one small floating-point mask (two RTs ping-ponged by DarkCrypt/MistBrush, one blit per
// frame) mapped over the arena rectangle; the overlay reads it through global properties.
// v4 (11-15): no disc at the feet. Blotch stamps are dropped along the path actually walked (every ~0.6 radius of
// travel, jittered sideways, random size), each clearing gradually over stampSeconds, so the trail lags behind the
// player and standing still clears nothing new.
// Nothing here has a collider or a light. In edit mode the mask stays empty (full mist).
using System.Collections.Generic;
using UnityEngine;

namespace DarkCrypt
{
    [DisallowMultipleComponent]
    public class DCMist : MonoBehaviour
    {
        [Tooltip("World rectangle the mask covers: x, z of the min corner, then width and depth (m).")]
        public Vector4 rect = new Vector4(-60f, -48f, 120f, 96f);
        public int resolution = 1024;
        [Tooltip("Mean blotch radius (m).")]
        public float radius = 2.4f;
        [Tooltip("Seconds one blotch takes to clear fully.")]
        public float stampSeconds = 0.8f;
        [Tooltip("Travel between blotches, as a fraction of the radius.")]
        public float spacing = 0.6f;
        [Tooltip("Seconds for a fully cleared spot to fill back in.")]
        public float regrowSeconds = 25f;
        public Material brush;
        [Tooltip("Optional: the object to follow. In Play the object named Player is used when this is empty.")]
        public Transform target;

        RenderTexture a, b;
        float nextFind, travelled;
        Vector3 lastPos, lastStamp; bool hasLast;
        struct Stamp { public Vector2 p; public float r, age, seed; }
        readonly List<Stamp> stamps = new List<Stamp>();
        readonly Vector4[] sv = new Vector4[8], seeds = new Vector4[8];

        static readonly int ID_Mask = Shader.PropertyToID("_DC_MistMask"), ID_Rect = Shader.PropertyToID("_DC_MistRect"),
            ID_Prev = Shader.PropertyToID("_Prev"), ID_Brush = Shader.PropertyToID("_BrushParams"),
            ID_Stamps = Shader.PropertyToID("_Stamps"), ID_Seeds = Shader.PropertyToID("_StampSeed");

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

        // Shared use by other stages at play time (11-15, 저승): a flat quad over the world rectangle at height y with
        // this component on it. The brush material is copied (Update writes into it); the overlay material is used as is.
        public static DCMist Spawn(Transform parent, Rect area, float y, Material overlay, Material brushAsset, float radius = 2.6f)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "CorruptionMist";
            var col = g.GetComponent<Collider>(); if (col) Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(new Vector3(area.center.x, y, area.center.y), Quaternion.Euler(90, 0, 0));
            g.transform.localScale = new Vector3(area.width, area.height, 1f);
            var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = overlay;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var mist = g.AddComponent<DCMist>();
            mist.rect = new Vector4(area.xMin, area.yMin, area.width, area.height);
            mist.resolution = Mathf.Clamp(Mathf.RoundToInt(area.width * 10f), 256, 1024);
            mist.brush = new Material(brushAsset) { name = brushAsset.name + " (runtime)" };
            mist.ownsBrush = true;
            mist.radius = radius; mist.stampSeconds = 0.8f; mist.spacing = 0.6f;
            return mist;
        }
        [System.NonSerialized] public bool ownsBrush;
        void OnDestroy() { if (ownsBrush && brush) Destroy(brush); }

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
            if (target)
            {
                Vector3 p = target.position; p.y = 0;
                if (!hasLast || (p - lastPos).sqrMagnitude > 25f) { lastPos = p; lastStamp = p; travelled = 0; }   // teleports do not smear
                hasLast = true;
                travelled += (p - lastPos).magnitude;
                float gap = Mathf.Max(0.3f, radius * spacing);
                if (travelled >= gap)
                {
                    travelled = 0;
                    Vector3 dir = (p - lastStamp).sqrMagnitude > 1e-4f ? (p - lastStamp).normalized : Vector3.forward;
                    Vector3 side = new Vector3(dir.z, 0, -dir.x);
                    // between the last blotch and the feet, jittered sideways: a chain of blotches, not a tube
                    Vector3 c = Vector3.Lerp(lastStamp, p, 0.55f) + side * (Random.Range(-0.4f, 0.4f) * radius);
                    if (stamps.Count >= 8) stamps.RemoveAt(0);
                    stamps.Add(new Stamp { p = new Vector2(c.x, c.z), r = radius * Random.Range(0.7f, 1.25f), age = 0, seed = Random.Range(0f, 100f) });
                    lastStamp = p;
                }
                lastPos = p;
            }
            float life = Mathf.Max(0.05f, stampSeconds);
            float rate = 1.3f * dt / life;
            for (int i = 0; i < 8; i++)
            {
                if (i < stamps.Count)
                {
                    var s = stamps[i];
                    // eases in: slow at first, so the blotch nearest the player is still half misted
                    float k = Mathf.SmoothStep(0.2f, 1.6f, s.age / life);
                    sv[i] = new Vector4(s.p.x, s.p.y, s.r, s.age < life * 1.6f ? rate * k : 0f);
                    seeds[i] = new Vector4(s.seed, 0, 0, 0);
                    s.age += dt; stamps[i] = s;
                }
                else { sv[i] = Vector4.zero; seeds[i] = Vector4.zero; }
            }
            brush.SetVectorArray(ID_Stamps, sv);
            brush.SetVectorArray(ID_Seeds, seeds);
            brush.SetVector(ID_Brush, new Vector4(0, 0, dt / Mathf.Max(1f, regrowSeconds), Time.time));
            brush.SetVector("_Rect", rect);
            brush.SetTexture(ID_Prev, a);
            Graphics.Blit(a, b, brush);
            (a, b) = (b, a);
            Shader.SetGlobalTexture(ID_Mask, a);
            Shader.SetGlobalVector(ID_Rect, rect);
        }
    }
}
