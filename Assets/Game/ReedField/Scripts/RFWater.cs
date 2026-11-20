// Shallow wetland water for the reed field, using the free URP_Water shader graph (ChiliMilk, MIT; ThirdParty/URP_Water).
// - Feeds the graph's globals: _WaterFXTexture (ripple normals) and _PlanarReflectionTexture (the warm sky it mirrors).
// - Ripples: an extra camera that copies the main camera each frame draws only the ripple particles (layer 30) into a
//   half-resolution texture; the water reads it, so rings spread and bend the reflections like real ripples.
// - Walking in the water: ripple rings, a spray flipbook burst (BoatAttack splash sheet, Unity Companion License) and
//   droplets at the feet, only while the player is actually in water.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Reeds
{
    [ExecuteAlways]
    public class RFWater : MonoBehaviour
    {
        public const int FxLayer = 30;
        public ParticleSystem ripples, spray, droplets;
        [Tooltip("Metres walked in water between splashes.")]
        public float stepDistance = 0.55f;
        [Tooltip("Random small rings on the water now and then (insects, drops).")]
        public float ambientRipplesPerSecond = 1.5f;

        ReedField field;
        Camera fxCam;
        RenderTexture fxRT;
        Texture2D sky;
        Transform player; float findT;
        Vector3 lastFoot; float walked;
        static readonly int FxTexId = Shader.PropertyToID("_WaterFXTexture"), ReflId = Shader.PropertyToID("_PlanarReflectionTexture");

        void OnEnable()
        {
            field = FindAnyObjectByType<ReedField>();
            sky = new Texture2D(1, 8, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            UpdateSky();
        }

        void OnDisable()
        {
            if (fxCam) DestroyImmediate(fxCam.gameObject);
            if (fxRT) { fxRT.Release(); DestroyImmediate(fxRT); }
            if (sky) DestroyImmediate(sky);
            Shader.SetGlobalTexture(FxTexId, Texture2D.blackTexture);
        }

        // The water mirrors a warm sky gradient (or the night sky): cheap stand-in for planar reflections.
        public void UpdateSky()
        {
            if (!sky) return;
            bool sun = !field || field.mood == ReedField.Mood.Sunset;
            Color top = sun ? new Color(0.62f, 0.40f, 0.22f) : new Color(0.10f, 0.13f, 0.20f);
            Color hor = sun ? new Color(0.30f, 0.20f, 0.12f) : new Color(0.20f, 0.24f, 0.32f);
            for (int i = 0; i < 8; i++) sky.SetPixel(0, i, Color.Lerp(hor, top, i / 7f));
            sky.Apply();
            Shader.SetGlobalTexture(ReflId, sky);
        }

        void EnsureFxCamera(Camera main)
        {
            int w = Mathf.Max(64, main.pixelWidth / 2), h = Mathf.Max(64, main.pixelHeight / 2);
            if (!fxRT || fxRT.width != w || fxRT.height != h)
            {
                if (fxRT) { fxRT.Release(); DestroyImmediate(fxRT); }
                // RenderGraph needs a depth buffer on a camera's output texture (without one every frame logs
                // "EndRenderPass: Not inside a Renderpass" / "Fake or uninitialized surface"). The ripples ignore it.
                fxRT = new RenderTexture(w, h, 16, RenderTextureFormat.ARGBHalf) { name = "WaterFX", hideFlags = HideFlags.DontSave };
                fxRT.Create();
            }
            if (!fxCam)
            {
                var go = new GameObject("WaterFX Camera") { hideFlags = HideFlags.DontSave };
                fxCam = go.AddComponent<Camera>();
                fxCam.clearFlags = CameraClearFlags.SolidColor;
                fxCam.backgroundColor = new Color(0, 0, 0, 0);
                fxCam.cullingMask = 1 << FxLayer;
                var d = go.AddComponent<UniversalAdditionalCameraData>();
                d.renderPostProcessing = false; d.renderShadows = false;
                d.requiresColorOption = CameraOverrideOption.Off; d.requiresDepthOption = CameraOverrideOption.Off;
                d.antialiasing = AntialiasingMode.None;
            }
            fxCam.targetTexture = fxRT;
            fxCam.depth = main.depth - 10;
            // The main camera must not draw the ripple particles themselves.
            main.cullingMask &= ~(1 << FxLayer);
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (!main) return;
            if (Application.isPlaying)
            {
                EnsureFxCamera(main);
                fxCam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                fxCam.fieldOfView = main.fieldOfView; fxCam.nearClipPlane = main.nearClipPlane; fxCam.farClipPlane = main.farClipPlane;
                fxCam.aspect = main.aspect;
                Shader.SetGlobalTexture(FxTexId, fxRT);
                Feet();
                Ambient(main);
            }
            else Shader.SetGlobalTexture(FxTexId, Texture2D.blackTexture);
        }

        public bool InWater(Vector3 world, out float waterY)
        {
            waterY = 0;
            if (!field) return false;
            var o = field.transform.position;
            var p = new Vector2(world.x - o.x, world.z - o.z);
            waterY = o.y + field.waterLevel;
            return field.GroundHeight(p) < field.waterLevel - 0.03f;
        }

        void Feet()
        {
            if (!player) { findT -= Time.deltaTime; if (findT <= 0f) { findT = 0.5f; var go = GameObject.Find("Player"); if (go) { player = go.transform; lastFoot = player.position; } } return; }
            var p = player.position;
            var d = p - lastFoot; d.y = 0;
            lastFoot = p;
            if (!InWater(p, out float wy)) { walked = 0; return; }
            walked += d.magnitude;
            if (walked < stepDistance) return;
            walked = 0;
            var at = new Vector3(p.x, wy + 0.02f, p.z);
            var dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
            Emit(ripples, at, Vector3.zero, Random.Range(1.6f, 2.4f), 1);
            Emit(spray, at + dir * 0.15f, Vector3.up * Random.Range(0.8f, 1.4f) + dir * 0.6f, Random.Range(0.8f, 1.2f), 3);
            Emit(droplets, at, Vector3.zero, 0f, Random.Range(8, 14));
        }

        // A few rings appear by themselves on the water near the camera, so it never looks frozen.
        void Ambient(Camera main)
        {
            if (!ripples || !field || Random.value > ambientRipplesPerSecond * Time.deltaTime) return;
            var c = main.transform.position + main.transform.forward * 22f;
            for (int k = 0; k < 6; k++)
            {
                var q = new Vector3(c.x + Random.Range(-16f, 16f), 0, c.z + Random.Range(-10f, 10f));
                if (InWater(q, out float wy)) { Emit(ripples, new Vector3(q.x, wy + 0.02f, q.z), Vector3.zero, Random.Range(0.6f, 1.1f), 1); return; }
            }
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, int count)
        {
            if (!ps) return;
            var e = new ParticleSystem.EmitParams { position = pos, velocity = vel, applyShapeToPosition = true };
            if (size > 0f) e.startSize = size;
            ps.Emit(e, count);
        }
    }
}
