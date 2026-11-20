// 태엽열쇠 red glow drawn AFTER post-processing (11-17, Kido: 발광 더 잘 보이게).
// The stage grades (LookFX: exposure -0.7..+1.9, saturation -38..-55) turned the in-scene red emission pink or peach, so
// the keys' red crevices / collar / tick flare are added again on top of the final image, the same idea as the bullet
// cores (LookFX BulletCorePass). Occlusion is tested by hand against the camera depth texture (the key is in it), so a
// key behind a wall or its own monster stays hidden. Draws the "WindKeyGlow" pass of BulletHeaven/WindKey.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class WindKeyGlowPass : ScriptableRenderPass
{
    static WindKeyGlowPass instance;
    public static int ActiveKeys;   // counted by WindKeyAttach (enabled keys); no pass when there are none
    static readonly ShaderTagId Tag = new ShaderTagId("WindKeyGlow");
    static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");
    static readonly int FlipId = Shader.PropertyToID("_WK_DepthFlip");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (ActiveKeys <= 0 || cam.cameraType != CameraType.Game) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null || data.scriptableRenderer == null || !data.renderPostProcessing) return;
        if (instance == null) instance = new WindKeyGlowPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        instance.ConfigureInput(ScriptableRenderPassInput.Depth);
        instance.cam = cam;
        data.scriptableRenderer.EnqueuePass(instance);
    }

    Camera cam;
    class PassData { public RendererListHandle list; public TextureHandle target, depth; public Camera cam; }

    public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
    {
        if (!cam) return;
        var res = frameData.Get<UniversalResourceData>();
        if (!res.cameraDepthTexture.IsValid()) return;
        var rd = frameData.Get<UniversalRenderingData>();
        var cd = frameData.Get<UniversalCameraData>();
        var ld = frameData.Get<UniversalLightData>();
        var ds = RenderingUtils.CreateDrawingSettings(Tag, rd, cd, ld, SortingCriteria.CommonOpaque);
        var list = rg.CreateRendererList(new RendererListParams(rd.cullResults, ds, new FilteringSettings(RenderQueueRange.all)));
        using (var b = rg.AddRasterRenderPass<PassData>("WindKey Glow (after post)", out var d))
        {
            d.list = list; d.target = res.activeColorTexture; d.depth = res.cameraDepthTexture; d.cam = cam;
            b.UseRendererList(list);
            b.UseTexture(d.depth, AccessFlags.Read);
            b.SetRenderAttachment(d.target, 0, AccessFlags.Write);
            b.AllowGlobalStateModification(true);
            b.AllowPassCulling(false);
            b.SetRenderFunc((PassData p, RasterGraphContext c) =>
            {
                // after post the target has a bottom-left origin and wants the unflipped projection (see LookFX BulletCorePass)
                bool flip = c.GetTextureUVOrigin(p.target) != TextureUVOrigin.BottomLeft;
                c.cmd.SetViewProjectionMatrices(p.cam.worldToCameraMatrix, GL.GetGPUProjectionMatrix(p.cam.projectionMatrix, flip));
                c.cmd.SetGlobalTexture(DepthId, p.depth);
                c.cmd.SetGlobalFloat(FlipId, c.GetTextureUVOrigin(p.depth) != c.GetTextureUVOrigin(p.target) ? 1f : 0f);
                c.cmd.DrawRendererList(p.list);
            });
        }
    }
}
