// High 3/4 follow camera. Leans toward the aim direction and stays framed on the room.
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoomRun
{
    public sealed class CameraRig : MonoBehaviour
    {
        public float height = 17f;
        public float back = 14.5f;
        public float fov = 30f;
        public float aimLean = 2.2f;
        public float smooth = 7f;

        public PlayerController Target;
        public Camera Cam { get; private set; }

        static readonly int ID_FogShift = Shader.PropertyToID("_RR_FogShift");
        float fogBase = -1f, fogWritten = -1f;

        Vector3 focus, center;
        Vector2 clamp;
        // 11-11 (Kido): the view starts at 1.6x the original framing (like POE2 / Death Must Die) and never goes further
        // out; the wheel zooms in until the character nearly fills the screen ("휠줌넣어 거의 캐릭터 화면에 꽉차게").
        // At 0.2 the camera is ~4.5 m away and the character fills most of the screen height (checked in a capture).
        public const float FixedZoom = 1.6f, MinZoom = 0.2f;
        float shake, zoom = FixedZoom;
        static readonly Color Background = new Color(0.04f, 0.05f, 0.08f);

        void Awake()
        {
            gameObject.tag = "MainCamera";
            Cam = gameObject.AddComponent<Camera>();
            Cam.fieldOfView = fov;
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 600f;
            var data = gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            gameObject.AddComponent<AudioListener>();
            ResetClear();
        }

        public void ResetClear()
        {
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Background;
        }

        public void CopyClearFrom(Camera other)
        {
            Cam.clearFlags = other.clearFlags;
            Cam.backgroundColor = other.backgroundColor;
        }

        public void SetBounds(Vector3 c, Vector2 size)
        {
            center = c;
            // The focus may reach the room edge, so every part of the environment can be brought on screen.
            clamp = new Vector2(Mathf.Max(0f, size.x / 2f - 4f), Mathf.Max(0f, size.y / 2f - 3f));
        }

        public void Snap()
        {
            focus = WantedFocus();
            Apply(0f);
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        // 1 = the original framing (height 17, back 14.5). Fixed at FixedZoom in play; set only by remote captures.
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, MinZoom, 3.5f); }

        Vector3 WantedFocus()
        {
            if (!Target) return focus;
            // the lean toward the aim shrinks with the zoom, so a close-up stays on the character; aim at chest height
            // when close so the body, not the feet, sits in the middle of the screen
            float close = Mathf.Clamp01(zoom);
            var p = Target.transform.position + Target.AimDir * aimLean * close + Vector3.up * (1f - close) * 1.0f;
            p.x = Mathf.Clamp(p.x, center.x - clamp.x, center.x + clamp.x);
            p.z = Mathf.Clamp(p.z, center.z - clamp.y, center.z + clamp.y);
            return p;
        }

        void LateUpdate()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                // over the dev tools panel the wheel scrolls the panel instead (11-13)
                if (Mathf.Abs(wheel) > 0.01f && !LookStudyDevTools.PointerOverPanel) zoom = Mathf.Clamp(zoom * (wheel > 0 ? 0.85f : 1.18f), MinZoom, FixedZoom);
            }
            focus = Vector3.Lerp(focus, WantedFocus(), 1f - Mathf.Exp(-smooth * Time.deltaTime));
            Apply(Time.deltaTime);
        }

        void Apply(float dt)
        {
            var offset = new Vector3(0, height, -back) * zoom;
            var jitter = Vector3.zero;
            if (shake > 0.001f)
            {
                jitter = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)) * shake * shake * 0.5f;
                shake = Mathf.MoveTowards(shake, 0f, dt * 3.5f);
            }
            transform.position = focus + offset + jitter;
            transform.rotation = Quaternion.LookRotation(-offset);
            KeepFogAtZoom();
            KeepShadowsAtZoom();
        }

        // Fog is measured from the camera, so pulling the camera back used to drown the whole arena (Kido 11-10, every
        // field). Fog is kept as it looks at zoom 1: the field shaders subtract the extra distance (_RR_FogShift) and
        // Unity's own exponential fog is thinned by the same ratio. Zooming in never adds fog.
        void KeepFogAtZoom()
        {
            float baseDist = new Vector2(height, back).magnitude;
            float extra = Mathf.Max(0f, zoom - 1f) * baseDist;
            Shader.SetGlobalFloat(ID_FogShift, extra);

            // a scene load or a field script may set its own density; take that as the zoom-1 value
            if (!Mathf.Approximately(RenderSettings.fogDensity, fogWritten)) fogBase = RenderSettings.fogDensity;
            if (fogBase < 0f) return;
            float k = baseDist / (baseDist + extra);
            fogWritten = fogBase * k;
            RenderSettings.fogDensity = fogWritten;
        }

        // 11-11 (Kido: "폐촌 뒤로 시야 멀리 하면 검게 변했다 다시 변했다"): shadow distance is measured from the camera, so
        // zooming out pushed the ground past it and the far half lost its shadows, with the cut line sweeping across
        // the field. The pipeline's shadow distance now grows with the zoom so the same ground stays shadowed as at
        // zoom 1; it is put back when the rig goes away (the asset is shared and would otherwise stay changed).
        float shadowBase = -1f;
        void KeepShadowsAtZoom()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;
            if (shadowBase < 0f) shadowBase = asset.shadowDistance;
            float want = shadowBase * Mathf.Max(1f, zoom);
            if (!Mathf.Approximately(asset.shadowDistance, want)) asset.shadowDistance = want;
        }

        void RestoreShadows()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset && shadowBase > 0f) asset.shadowDistance = shadowBase;
            shadowBase = -1f;
        }

        void OnDisable()
        {
            RestoreShadows();
            Shader.SetGlobalFloat(ID_FogShift, 0f);
            if (fogBase >= 0f && Mathf.Approximately(RenderSettings.fogDensity, fogWritten)) RenderSettings.fogDensity = fogBase;
            fogBase = fogWritten = -1f;
        }
    }
}
