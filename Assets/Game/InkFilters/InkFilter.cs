// Switchable ink-wash screen filters for the reed field and the bamboo arena: Off / Ink (sacshadow) / Sumi-e (bentoBAUX)
// / Watercolour (Cyanilux). Press F10 in Play to cycle. The pass is added from script to each game/scene camera every
// frame (ScriptableRenderer.EnqueuePass), so the project's URP renderer asset is not changed.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace InkFilters
{
    public enum InkMode { Off = 0, Ink = 1, SumiE = 2, Watercolour = 3 }

    [ExecuteAlways]
    public class InkFilter : MonoBehaviour
    {
        // Kept across rooms in RoomRun, so the choice survives going from the reeds to the bamboo.
        public static InkMode Mode = InkMode.Off;
        [Tooltip("Mode used in the editor (scene view and captures); Play uses the F10 choice.")]
        public InkMode editorMode = InkMode.Off;
        [Range(0, 1)] public float strength = 1f;
        [Tooltip("1 = keep the scene's own colours and tones, only lay the ink over them (outlines, brush texture, paper). 0 = fully stylised filters.")]
        [Range(0, 1)] public float colourKeep = 1f;
        [Tooltip("Sumi-e (stylised, Colour Keep below 1): how much of the scene's own hue comes back over the ink tones. 0 = pure black/white sumi-e.")]
        [Range(0, 1)] public float sumiColour = 0.5f;
        [Tooltip("Watercolour: wet-edge distortion amount (anchored to the world, so it never swims while moving).")]
        [Range(0, 1)] public float waterWobble = 0.5f;
        public Shader shader;
        public Texture2D stroke, stroke2, inkNoise, waterNoise, paper;

        Material mat;
        InkPass pass;
        float hintUntil;
        static readonly string[] Names = { "필터 끔", "수묵 (먹)", "스미에", "수채" };

        void OnEnable()
        {
            if (!shader) shader = Shader.Find("Hidden/InkFilters/Ink");
            if (!shader) { enabled = false; return; }
            mat = CoreUtils.CreateEngineMaterial(shader);
            pass = new InkPass(mat) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
            RenderPipelineManager.beginCameraRendering += Inject;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= Inject;
            CoreUtils.Destroy(mat);
        }

        public InkMode Current => Application.isPlaying ? Mode : editorMode;

        void Update()
        {
            if (!Application.isPlaying) return;
            var kb = Keyboard.current;
            if (kb != null && kb.f10Key.wasPressedThisFrame) { Mode = (InkMode)(((int)Mode + 1) % 4); hintUntil = Time.unscaledTime + 2f; }
        }

        void Inject(ScriptableRenderContext ctx, Camera cam)
        {
            if (!isActiveAndEnabled || mat == null || Current == InkMode.Off) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (cam.targetTexture && cam.targetTexture.format == RenderTextureFormat.ARGBHalf && cam.name.Contains("WaterFX")) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            mat.SetTexture("_InkStroke", stroke); mat.SetTexture("_InkStroke2", stroke2); mat.SetTexture("_InkNoise", inkNoise);
            mat.SetTexture("_WaterNoise", waterNoise); mat.SetTexture("_Paper", paper);
            mat.SetFloat("_InkStrength", strength); mat.SetFloat("_InkTime", Application.isPlaying ? Time.time : 0f);
            mat.SetFloat("_SumiColour", sumiColour);
            mat.SetFloat("_WaterWobble", waterWobble);
            // World anchoring: brush, noise and paper textures are laid on the ground plane (not on the screen), so they
            // travel with the scene when the camera follows the player instead of staying burned onto the screen.
            mat.SetMatrix("_InkInvVP", (cam.projectionMatrix * cam.worldToCameraMatrix).inverse);
            mat.SetVector("_InkCamPos", cam.transform.position);
            mat.SetVector("_InkCamFwd", cam.transform.forward);
            mat.SetFloat("_ColourKeep", colourKeep);
            mat.SetFloat("_InkPlaneY", transform.position.y);
            pass.passIndex = (int)Current - 1;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            data.scriptableRenderer.EnqueuePass(pass);
        }

        GUIStyle style;
        void OnGUI()
        {
            if (!Application.isPlaying || Time.unscaledTime > hintUntil) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.92f, 0.85f) } };
            GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 40), $"화면 필터: {Names[(int)Mode]}   (F10)", style);
        }

        class InkPass : ScriptableRenderPass
        {
            readonly Material mat;
            public int passIndex;
            public InkPass(Material m) { mat = m; requiresIntermediateTexture = true; }

            class PassData { public TextureHandle src, depth; public Material mat; public int index; }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) return;
                var src = res.activeColorTexture;
                var desc = rg.GetTextureDesc(src);
                desc.name = "InkFilter"; desc.clearBuffer = false; desc.depthBufferBits = 0;
                var dst = rg.CreateTexture(desc);
                using (var builder = rg.AddRasterRenderPass<PassData>("Ink Filter", out var data))
                {
                    data.src = src; data.mat = mat; data.index = passIndex; data.depth = res.cameraDepthTexture;
                    builder.UseTexture(src, AccessFlags.Read);
                    // The filters read scene depth (outlines, mist): declare it so it is bound as _CameraDepthTexture.
                    if (data.depth.IsValid()) builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        if (d.depth.IsValid()) d.mat.SetTexture("_CameraDepthTexture", d.depth);
                        Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1, 1, 0, 0), d.mat, d.index);
                    });
                }
                res.cameraColor = dst;
            }
        }
    }
}
