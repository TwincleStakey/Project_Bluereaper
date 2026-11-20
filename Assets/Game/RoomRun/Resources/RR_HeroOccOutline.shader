// 11-16 occluded-hero outline v2 (Kido: v1 GSOccluded pass = white blob, thinned v1 = dotted/jaggy ghost at z1.6).
// v1 drew a fresnel band per pixel (1 - N.V), so on the low-poly hero it broke into dots wherever the normals jumped.
// v2 is screen space and does not look at normals at all:
//   pass 0 "MaskAll": hero renderers -> R = 1 (ZTest Always: the whole silhouette)
//   pass 1 "MaskVis": hero renderers -> G = 1 (ZTest LEqual vs the scene depth: the parts that are in front)
//   pass 2 "Composite" (fullscreen): hidden = R and not G. A ring just outside the silhouette (disc coverage of R minus
//     R itself) is drawn wherever hidden hero pixels are nearby, plus a very faint fill on the hidden pixels.
//     Width is in pixels scaled to 1080p (_OutlinePx at 1080 lines), soft outer edge, so it is one clean continuous line.
// FieldLighting draws it (PlayerOverVeilPass) only while its _GSOccluded gate is up (enemy stack / building / big prop).
Shader "Hidden/RoomRun/HeroOccOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Back

        Pass
        {
            Name "MaskAll"
            ZTest Always
            ColorMask R
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag() : SV_Target { return half4(1, 0, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "MaskVis"
            ZTest LEqual
            ColorMask G
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _OutlineColor;      // rgb warm neutral, a = line strength
            float _OutlinePx;         // line width in pixels at 1080 lines
            half _OutlineFill;        // fill on hidden pixels
            half _GSOccluded;         // global gate 0..1 (FieldLighting)
            float4 _MaskSize;         // xy = mask pixels (set by FieldLighting)
            half _HeroOutlineAll;     // global 0..1: always-on outer line (FieldLighting.HeroOutline)
            half4 _HeroOutlineAllColor;

            #define TAPS 16
            static const float2 DIRS[8] = { float2(1,0), float2(0.7071,0.7071), float2(0,1), float2(-0.7071,0.7071),
                                            float2(-1,0), float2(-0.7071,-0.7071), float2(0,-1), float2(0.7071,-0.7071) };

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float2 px = 1.0 / _MaskSize.xy;
                float w = max(1.0, _OutlinePx * _MaskSize.y / 1080.0);
                half2 c0 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rg;
                half inside = c0.r;
                half hiddenHere = saturate(c0.r - c0.g);
                // disc coverage of the silhouette (R) and of the hidden part (R - G) at radius w and w/2 (8 + 8 taps,
                // the second ring rotated 22.5 deg); bilinear taps soften the mask's stair steps
                half cov = 0, hid = 0, vis = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float2 d = DIRS[k];
                    float2 d2 = float2(d.x * 0.9239 - d.y * 0.3827, d.x * 0.3827 + d.y * 0.9239);
                    half2 a = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d * w * px).rg;
                    half2 b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d2 * w * 0.55 * px).rg;
                    cov += a.r + b.r;
                    hid += saturate(a.r - a.g) + saturate(b.r - b.g);
                    vis += a.g + b.g;
                }
                cov /= TAPS; hid /= TAPS; vis /= TAPS;
                // ring: outside the silhouette, within w pixels of it; soft outer falloff, inner edge = silhouette
                half ring = smoothstep(0.02, 0.22, cov) * (1 - inside);
                // only along hidden parts of the contour; thin slivers (grass blades crossing the hero) stay below this
                half near = smoothstep(0.06, 0.2, hid);
                half aOcc = (ring * near * _OutlineColor.a + hiddenHere * _OutlineFill) * _GSOccluded;
                // 11-18 always-on outer line (Kido: rim light striped the cloth; other ways to read the player): the same ring
                // along the VISIBLE contour only, all the time, in its own colour. Never inside the body, so no cloth stripes.
                half aAll = ring * smoothstep(0.06, 0.2, vis) * _HeroOutlineAll * _HeroOutlineAllColor.a;
                half a = max(aOcc, aAll);
                if (a <= 0.002) discard;
                return half4(aOcc >= aAll ? _OutlineColor.rgb : _HeroOutlineAllColor.rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
