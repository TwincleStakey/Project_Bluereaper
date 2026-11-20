// Three ink-wash / painterly screen filters, ported to URP 17 full-screen passes (RenderGraph blit).
//   Pass 0 "Ink"         after sacshadow/3D_ChineseInkPaintingStyleShader (MIT): its brush-stroke (Stroke_0/2) and
//                         outline-noise (CIP_OutLine_Noise) textures, ink outlines from depth, ink washed into rice paper.
//   Pass 1 "Sumi-e"      after bentoBAUX/Sumi-e-in-3D (MIT): its colour-ramp banding and smooth Voronoi brush noise
//                         (Voronoi.hlsl), grey mist to paper with depth, saturated reds kept as accents.
//   Pass 2 "Watercolour" after Cyanilux/URP_WatercolourShaders (MIT): its noise texture distorting the image, pigment
//                         pooling at colour edges, paper granulation, softened washes.
// Paper: ambientCG Paper005 (CC0).
//
// _ColourKeep (default 1): the scene keeps its own colours and tones; only the ink elements are laid over it
// (outlines, brush texture in the shading, paper grain, a little bleed). 0 = the fully stylised versions.
// Every texture is fixed to the real surface it lands on (world position from scene depth, triplanar), so it moves with
// the world and characters/trees never show the ground pattern behind them. Offsets (bleed, blur) are depth-aware: a
// pixel never takes colour from something at another depth, so characters stay solid.
Shader "Hidden/InkFilters/Ink"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_InkStroke);  SAMPLER(sampler_InkStroke);
        TEXTURE2D(_InkStroke2);
        TEXTURE2D(_InkNoise);   SAMPLER(sampler_InkNoise);
        TEXTURE2D(_WaterNoise); SAMPLER(sampler_WaterNoise);
        TEXTURE2D(_Paper);      SAMPLER(sampler_Paper);
        float _InkStrength, _InkTime, _SumiColour, _WaterWobble, _ColourKeep;
        float4x4 _InkInvVP; float3 _InkCamPos, _InkCamFwd; float _InkPlaneY;

        float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
        // The camera colour is linear: tone work (bands, washes, thresholds) is done in perceptual sRGB and converted back.
        float3 ToS(float3 v) { return LinearToSRGB(max(v, 0)); }
        half4 Out(float3 v) { return half4(SRGBToLinear(saturate(v)), 1); }
        float3 Src(float2 uv) { return ToS(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb); }
        float Eye(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }
        bool HasGeo(float2 uv)
        {
            float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
            return raw > 1e-6;
            #else
            return raw < 1 - 1e-6;
            #endif
        }

        // World position of the surface seen in this pixel: camera ray scaled by the scene depth (ground plane if empty).
        float3 WorldPos(float2 uv)
        {
            float4 p = mul(_InkInvVP, float4(uv * 2 - 1, 1, 1));
            float3 dir = p.xyz / p.w - _InkCamPos;
            if (HasGeo(uv)) return _InkCamPos + dir * (Eye(uv) / max(dot(dir, _InkCamFwd), 1e-4));
            float t = (_InkPlaneY - _InkCamPos.y) / min(dir.y, -1e-4);
            return _InkCamPos + dir * max(t, 0);
        }
        float3 SurfNormal(float3 P)
        {
            float3 n = cross(ddy(P), ddx(P));
            n = dot(n, n) > 1e-12 ? normalize(n) : float3(0, 1, 0);
            return dot(n, _InkCamPos - P) < 0 ? -n : n;
        }
        // Triplanar weights: ground uses XZ, walls / bodies / stems use XY or ZY.
        float3 TriW(float3 n) { float3 w = pow(abs(n), 4); return w / (w.x + w.y + w.z); }
        #define TRI(tex, smp, P, w, m, off) \
            (SAMPLE_TEXTURE2D(tex, smp, (P).zy / (m) + (off)) * (w).x + SAMPLE_TEXTURE2D(tex, smp, (P).xz / (m) + (off)) * (w).y + SAMPLE_TEXTURE2D(tex, smp, (P).xy / (m) + (off)) * (w).z)

        // Depth-aware fetch: an offset sample is used only if it lies on the same surface (similar depth).
        float3 SrcSafe(float2 uv, float2 off, float eye0)
        {
            float e = Eye(uv + off);
            return abs(e - eye0) < eye0 * 0.02 + 0.05 ? Src(uv + off) : Src(uv);
        }
        float2 SafeOffset(float2 uv, float2 off, float eye0)
        {
            float e = Eye(uv + off);
            return abs(e - eye0) < eye0 * 0.02 + 0.05 ? off : 0;
        }

        // Depth edges (relative), so outlines appear around reeds, props and characters but not across flat ground.
        float DepthEdge(float2 uv, float px)
        {
            float2 o = px / _ScreenParams.xy;
            float c = Eye(uv);
            float e = abs(Eye(uv + float2(o.x, 0)) - c) + abs(Eye(uv - float2(o.x, 0)) - c)
                    + abs(Eye(uv + float2(0, o.y)) - c) + abs(Eye(uv - float2(0, o.y)) - c);
            return saturate(e / max(c, 1e-3) * 6.0);
        }
        // Brightness edges (inner lines: reed blades against each other, folds, the bank line).
        float LumaEdge(float2 uv, float px)
        {
            float2 o = px / _ScreenParams.xy;
            float gx = Luma(Src(uv + float2(o.x, 0))) - Luma(Src(uv - float2(o.x, 0)));
            float gy = Luma(Src(uv + float2(0, o.y))) - Luma(Src(uv - float2(0, o.y)));
            return smoothstep(0.06, 0.22, sqrt(gx * gx + gy * gy));
        }
        ENDHLSL

        // ------------------------------------------------------------ 0: Chinese ink (sacshadow)
        Pass
        {
            Name "Ink"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float eye0 = Eye(uv);
                float3 P = WorldPos(uv); float3 w = TriW(SurfNormal(P));
                float3 c0 = Src(uv);
                // Ink bleed: the image is read through the outline-noise texture so edges wobble like wet ink
                // (only within the same surface, never across a silhouette).
                float2 nz = TRI(_InkNoise, sampler_InkNoise, P, w, 7.0, 0).rg;
                float2 wob = SafeOffset(uv, (nz - 0.5) * 0.004, eye0);
                float3 c = Src(uv + wob);
                float3 blur = (SrcSafe(uv, wob + float2(0.002, 0), eye0) + SrcSafe(uv, wob - float2(0.002, 0), eye0)
                             + SrcSafe(uv, wob + float2(0, 0.003), eye0) + SrcSafe(uv, wob - float2(0, 0.003), eye0)) * 0.25;
                // Brush strokes (cun / ca texture strokes from the sacshadow package).
                float s1 = TRI(_InkStroke, sampler_InkStroke, P, w, 11.0, 0).r;
                float s2 = TRI(_InkStroke2, sampler_InkStroke, P, w, 17.0, 0.37).r;
                float stroke = lerp(s1, s2, 0.5);
                // Outlines (gou): depth edges, broken by the stroke texture.
                float edge = DepthEdge(uv, 1.5) * smoothstep(0.25, 0.75, 1 - stroke * 0.8);
                float3 paper = ToS(TRI(_Paper, sampler_Paper, P, w, 9.0, 0).rgb);
                float3 inkCol = float3(0.07, 0.065, 0.06);

                // --- stylised: tones -> layered ink washes on rice paper
                float l = smoothstep(0.15, 0.63, Luma(lerp(c, blur, 0.5)));
                float ink = saturate(1.0 - l * 1.15);
                ink = floor(ink * 4 + 0.5) / 4 * 0.6 + ink * 0.4;
                float dark = saturate(ink - stroke * 0.3 * smoothstep(0.4, 0.85, ink)) * ink;
                dark = max(dark, edge * 0.9);
                float3 res = lerp(paper * float3(0.97, 0.94, 0.86), inkCol, dark);
                res = lerp(res, res * (0.85 + c * 0.35), 0.25);

                // --- colour kept: the scene as it is, with ink laid over it
                float3 keep = lerp(c, blur, 0.25);
                float shade = smoothstep(0.1, 0.8, 1 - Luma(keep));
                keep *= lerp(1, 0.55 + stroke * 0.7, 0.85 * shade);           // brush strokes in the shadows
                keep = lerp(keep, inkCol, smoothstep(0.55, 0.95, shade) * (1 - stroke) * 0.45);   // ink pooling in the darks
                keep *= 0.9 + (Luma(paper) - 0.72) * 0.8;                     // paper grain
                float inner = LumaEdge(uv, 1.0) * smoothstep(0.2, 0.7, 1 - stroke * 0.8);
                keep = lerp(keep, inkCol, saturate(max(edge, inner * 0.6)) * 0.9);   // ink outlines and inner lines

                return Out(lerp(c0, lerp(res, keep, _ColourKeep), _InkStrength));
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ 1: Sumi-e (bentoBAUX)
        Pass
        {
            Name "SumiE"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "ThirdParty/bentoBAUX_SumiE/Voronoi.hlsl"
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float3 c = Src(uv);
                float3 P = WorldPos(uv); float3 w = TriW(SurfNormal(P));
                // Smooth Voronoi brush noise (bentoBAUX), in 3D world space: fixed to every surface.
                float dist = 0; float3 vc = 0, vp = 0;
                VoronoiSmoothF1_3D(P / 1.3, 0.6, 1.0, 1.0, 1, dist, vc, vp);
                float bristle = vc.r - 0.5;
                float edge = DepthEdge(uv, 1.0);
                float sat = max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
                float red = saturate((c.r - c.g * 1.9) * 5.0) * saturate((sat - 0.35) * 4.0);
                float3 pt = ToS(TRI(_Paper, sampler_Paper, P, w, 12.0, 0).rgb);

                // --- stylised: 4 ink tones (lifted, so dusk scenes are not near-black) + optional colour wash
                float l = smoothstep(0.04, 0.55, Luma(c));
                float t = saturate(l + bristle * 0.18);
                float b1 = smoothstep(0.14, 0.28, t), b2 = smoothstep(0.38, 0.54, t), b3 = smoothstep(0.62, 0.78, t);
                float3 dark = float3(0.24, 0.23, 0.23), mid1 = float3(0.46, 0.45, 0.44), mid2 = float3(0.68, 0.67, 0.65), light = float3(0.93, 0.92, 0.88);
                float3 g = lerp(lerp(lerp(dark, mid1, b1), mid2, b2), light, b3);
                float mist = saturate((Eye(uv) - 60.0) / 40.0) * (HasGeo(uv) ? 1 : 0);
                float3 paper = lerp(Luma(pt).xxx, pt, 0.35) * float3(1.0, 0.98, 0.93) * 1.15;
                g = lerp(g, paper, mist * 0.75);
                g *= lerp(1, Luma(paper) * 1.1, 0.5);
                float3 hue = clamp(c / max(Luma(c), 0.02), 0, 2.2);
                g = saturate(g * lerp(1, hue, _SumiColour * smoothstep(0.06, 0.45, Luma(g)) * (1 - red)));
                g = lerp(g, c * 1.15, red);
                g = lerp(g, float3(0.10, 0.10, 0.11), edge * 0.6);

                // --- colour kept: bristle marks in the shading, soft paper, fine ink outlines
                float3 keep = c * (1 + bristle * 0.4 * smoothstep(0.03, 0.4, Luma(c)));
                keep *= 0.92 + (Luma(pt) - 0.72) * 0.6;
                float inner = LumaEdge(uv, 1.0) * saturate(0.6 + bristle * 1.5);
                keep = lerp(keep, float3(0.08, 0.08, 0.09), saturate(max(edge, inner * 0.5)) * 0.75);

                return Out(lerp(c, lerp(g, keep, _ColourKeep), _InkStrength));
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ 2: Watercolour (Cyanilux)
        Pass
        {
            Name "Watercolour"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float eye0 = Eye(uv);
                float3 c0 = Src(uv);
                float3 P = WorldPos(uv); float3 w = TriW(SurfNormal(P));
                // Wet-on-wet bleeding: noise fixed to the surface distorts the image, but never across a silhouette.
                float2 n1 = TRI(_WaterNoise, sampler_WaterNoise, P, w, 11.0, 0).rg - 0.5;
                float2 n2 = TRI(_WaterNoise, sampler_WaterNoise, P, w, 2.8, 0.21).rg - 0.5;
                float2 off = SafeOffset(uv, (n1 * 0.010 + n2 * 0.003) * _WaterWobble, eye0);
                float2 duv = uv + off;
                float3 c = Src(duv);
                // Soft average around the pixel (same surface only): washes are flat, pigment pools where colour changes.
                float2 o = 3.0 / _ScreenParams.xy;
                float3 avg = (SrcSafe(duv, float2(o.x, 0), eye0) + SrcSafe(duv, float2(-o.x, 0), eye0) + SrcSafe(duv, float2(0, o.y), eye0)
                            + SrcSafe(duv, float2(0, -o.y), eye0) + SrcSafe(duv, o, eye0) + SrcSafe(duv, -o, eye0)) / 6.0;
                float3 wash = lerp(c, avg, 0.55);
                float pool = saturate((Luma(avg) - Luma(c)) * 3.0);
                wash *= 1 - pool * 0.35;
                float gran = Luma(ToS(TRI(_Paper, sampler_Paper, P, w, 7.5, 0).rgb));
                float edge = DepthEdge(uv, 1.0);

                // --- stylised: lighter, slightly desaturated pigment in a few value steps
                float3 st = lerp(wash, float3(1, 1, 1), 0.12);
                st = lerp(Luma(st).xxx, st, 0.7);
                float lw = Luma(st); float lq = floor(lw * 5 + 0.5) / 5;
                st *= lerp(1, lq / max(lw, 1e-3), 0.3);
                st *= lerp(1, gran * 1.15, 0.6);
                st *= 1 - edge * 0.25;

                // --- colour kept: the washes, pooling and paper only
                float3 keep = lerp(c, avg, 0.55) * (1 - pool * 0.5);
                keep *= lerp(1, gran * 1.15, 0.5);
                keep *= 1 - edge * 0.35;

                return Out(lerp(c0, lerp(st, keep, _ColourKeep), _InkStrength));
            }
            ENDHLSL
        }
    }
}
