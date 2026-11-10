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
        float shake, zoom = 1f;
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

        // Wheel zoom (1 = default). Set by remote captures.
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, 0.6f, 3.5f); }

        Vector3 WantedFocus()
        {
            if (!Target) return focus;
            var p = Target.transform.position + Target.AimDir * aimLean;
            p.x = Mathf.Clamp(p.x, center.x - clamp.x, center.x + clamp.x);
            p.z = Mathf.Clamp(p.z, center.z - clamp.y, center.z + clamp.y);
            return p;
        }

        void LateUpdate()
        {
            // Mouse wheel zooms out for looking over a whole stage.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) zoom = Mathf.Clamp(zoom * (wheel > 0 ? 0.88f : 1.14f), 0.6f, 3.5f);
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

        void OnDisable()
        {
            Shader.SetGlobalFloat(ID_FogShift, 0f);
            if (fogBase >= 0f && Mathf.Approximately(RenderSettings.fogDensity, fogWritten)) RenderSettings.fogDensity = fogBase;
            fogBase = fogWritten = -1f;
        }
    }
}
