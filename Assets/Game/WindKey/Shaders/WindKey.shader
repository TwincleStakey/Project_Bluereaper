// 태엽열쇠 (wind-up key) stuck in a monster's back. The whole key turns about its shaft (local +Y) in clockwork ticks,
// done in the vertex shader so hundreds of keys cost no scripts (SRP Batcher friendly; per-key desync = a few shared
// materials with different _Phase). Red light seeps out of the crevices (baked AO) and the collar where it enters the
// flesh, and flares on every tick (same deep red as the 장산범 parasite). Needs Bloom + HDR for the flare to bloom.
Shader "BulletHeaven/WindKey"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal", 2D) = "bump" {}
        _AOMap ("AO (crevice glow mask)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Brighten ("Albedo Brighten", Range(0.5, 4)) = 2.2
        _Metallic ("Metallic", Range(0, 1)) = 0.55
        _Smoothness ("Smoothness", Range(0, 1)) = 0.42
        _NrmStr ("Normal Strength", Range(0, 2)) = 1

        [Header(Wind turn)]
        _TickRate ("Ticks per second", Range(0, 8)) = 1.5
        _TickAngle ("Degrees per tick", Range(0, 180)) = 45
        _TickSnap ("Turn time (fraction of a tick)", Range(0.05, 1)) = 0.3
        _Continuous ("Continuous (0 = clockwork ticks)", Range(0, 1)) = 0
        _Dir ("Direction (+1 / -1)", Float) = 1
        _Phase ("Phase (desync)", Range(0, 1)) = 0
        _Wobble ("Tick wobble (degrees)", Range(0, 15)) = 4

        [Header(Red glow)]
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.04, 0.02, 1)
        _GlowBase ("Glow at rest", Range(0, 10)) = 0.5
        _GlowPeak ("Glow on tick", Range(0, 40)) = 4
        _CrevGlow ("Crevice glow", Range(0, 4)) = 1.4
        _CrevLo ("Crevice: AO full glow", Range(0, 1)) = 0.25
        _CrevHi ("Crevice: AO no glow", Range(0, 1)) = 0.6
        _CollarGlow ("Collar (wound) glow", Range(0, 4)) = 1.6
        _CollarHeight ("Collar glow height (mesh units)", Range(0.01, 0.5)) = 0.12
        _RimGlow ("Red rim (readability)", Range(0, 4)) = 0.6
        _Warn ("Warn (overdrive)", Range(0, 1)) = 0

        [Header(After post overlay (WindKeyGlowPass))]
        _OverRest ("Overlay at rest", Range(0, 2)) = 0.35
        _OverPeak ("Overlay on tick", Range(0, 3)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap); TEXTURE2D(_AOMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _Brighten; half _Metallic; half _Smoothness; half _NrmStr;
            float _TickRate; float _TickAngle; float _TickSnap; float _Continuous; float _Dir; float _Phase; float _Wobble;
            half4 _GlowColor; half _GlowBase; half _GlowPeak; half _CrevGlow; half _CrevLo; half _CrevHi; half _CollarGlow; half _CollarHeight; half _RimGlow; half _Warn;
            half _OverRest; half _OverPeak;
        CBUFFER_END
        // capture tools can freeze time (same convention as the 장산범 shaders)
        float _WK_UseFixedTime; float _WK_FixedTime;
        float _WK_PostExposure;   // set by WindKeyAttach from the active volume stack (0 when unset)
        float WKTime() { return _WK_UseFixedTime > 0.5 ? _WK_FixedTime : _Time.y; }
        // x = turn angle (radians), y = tick pulse 0..1
        float2 WKTurn()
        {
            float t = WKTime() * _TickRate * (1 + 1.5 * _Warn) + _Phase;
            float f = frac(t);
            float e = smoothstep(0, _TickSnap, f);
            // small overshoot + settle at the end of each turn: reads as a spring ratchet
            float settle = sin(saturate((f - _TickSnap) / max(1 - _TickSnap, 1e-3)) * 9.42) * exp(-max(f - _TickSnap, 0) * 9) * step(_TickSnap, f);
            float stepped = (floor(t) + e) * radians(_TickAngle) + settle * radians(_Wobble);
            float cont = t * radians(_TickAngle);
            float ang = lerp(stepped, cont, _Continuous) * _Dir;
            float pulse = lerp(exp(-f * 4.5) * (1 - 0.2 * e), 0.5 + 0.5 * sin(t * 6.2832), _Continuous);
            return float2(ang, pulse);
        }
        float3 WKRotY(float3 p, float a) { float s, c; sincos(a, s, c); return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 n:TEXCOORD1; float4 t:TEXCOORD2; float2 uv:TEXCOORD3; float hOS:TEXCOORD4; float pulse:TEXCOORD5; float fog:TEXCOORD6; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float2 tp = WKTurn();
                float3 p = WKRotY(i.positionOS.xyz, tp.x);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.n = TransformObjectToWorldNormal(WKRotY(i.normalOS, tp.x));
                o.t = float4(TransformObjectToWorldDir(WKRotY(i.tangentOS.xyz, tp.x)), i.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.hOS = i.positionOS.y; o.pulse = tp.y;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb * _Brighten;
                half ao = SAMPLE_TEXTURE2D(_AOMap, sampler_BaseMap, i.uv).r;
                float3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _NrmStr);
                float3 N = normalize(i.n), T = normalize(i.t.xyz), B = cross(N, T) * i.t.w;
                float3 n = normalize(T * nts.x + B * nts.y + N * nts.z);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog; d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = 1;
                SurfaceData sd = (SurfaceData)0;
                sd.albedo = saturate(alb); sd.metallic = _Metallic; sd.smoothness = _Smoothness; sd.occlusion = lerp(1, ao, 0.6); sd.alpha = 1; sd.normalTS = float3(0, 0, 1);
                half4 col = UniversalFragmentPBR(d, sd);

                // red glow: crevices + collar, flaring on each tick; rim keeps the silhouette readable in dark stages
                half crev = 1 - smoothstep(_CrevLo, _CrevHi, ao);
                half collar = 1 - smoothstep(0, _CollarHeight, i.hOS);
                half rim = pow(1 - saturate(dot(n, d.viewDirectionWS)), 3);
                half amp = _GlowBase + _GlowPeak * i.pulse * (1 + _Warn);
                half g = (crev * _CrevGlow + collar * _CollarGlow) * amp + rim * _RimGlow * (0.6 + 0.4 * i.pulse);
                half r = _GlowColor.r * g * exp2(-clamp(_WK_PostExposure, -3, 3));   // same glow in every stage grade
                half3 gc = half3(r, r * 0.035, r * 0.02);   // stay deep red at the peak (ACES pushes bright red toward orange)
                col.rgb = col.rgb * (1 - 0.5 * crev * saturate(amp * 0.15)) + gc;
                col.rgb = MixFog(col.rgb, i.fog);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }
        // drawn after post-processing by WindKeyGlowPass: pure red added on the final image so the stage grade can't wash it out
        Pass
        {
            Name "WindKeyGlow"
            Tags { "LightMode"="WindKeyGlow" }
            Blend One One
            ZWrite Off ZTest Always Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vertG
            #pragma fragment fragG
            #pragma multi_compile_instancing
            TEXTURE2D_X_FLOAT(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture); SAMPLER(sampler_AOMap);
            float _WK_DepthFlip;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float hOS:TEXCOORD1; float pulse:TEXCOORD2; float eye:TEXCOORD3; float4 sp:TEXCOORD4; float3 n:TEXCOORD5; float3 ws:TEXCOORD6; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vertG(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float2 tp = WKTurn();
                float3 ws = TransformObjectToWorld(WKRotY(i.positionOS.xyz, tp.x));
                o.positionCS = TransformWorldToHClip(ws); o.ws = ws;
                o.eye = -TransformWorldToView(ws).z;
                o.sp = ComputeScreenPos(o.positionCS);
                o.n = TransformObjectToWorldNormal(WKRotY(i.normalOS, tp.x));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.hOS = i.positionOS.y; o.pulse = tp.y;
                return o;
            }
            half4 fragG(V i) : SV_Target
            {
                float2 suv = i.sp.xy / i.sp.w;
                if (_WK_DepthFlip > 0.5) suv.y = 1 - suv.y;
                float raw = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_CameraDepthTexture, suv).r;
                float scene = LinearEyeDepth(raw, _ZBufferParams);
                if (i.eye > scene + 0.08 + 0.02 * i.eye) discard;   // hidden behind something (the key itself is in the depth)
                half ao = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, i.uv).r;
                half crev = 1 - smoothstep(_CrevLo, _CrevHi, ao);
                half collar = 1 - smoothstep(0, _CollarHeight, i.hOS);
                half face = 0.5 + 0.5 * saturate(dot(normalize(i.n), normalize(GetWorldSpaceViewDir(i.ws))));
                half amt = saturate(crev + collar) * face * (_OverRest + _OverPeak * i.pulse * (1 + _Warn));
                return half4(amt, amt * 0.03, amt * 0.02, 0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertS(A i):SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float a = WKTurn().x;
                float3 pw = TransformObjectToWorld(WKRotY(i.positionOS.xyz, a)); float3 nw = TransformObjectToWorldNormal(WKRotY(i.normalOS, a));
                float4 c = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 fragS():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            #pragma multi_compile_instancing
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vertD(A i):SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(WKRotY(i.positionOS.xyz, WKTurn().x)); }
            half fragD():SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertN
            #pragma fragment fragN
            #pragma multi_compile_instancing
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs:SV_POSITION; float3 n:TEXCOORD0; };
            V vertN(A i) { UNITY_SETUP_INSTANCE_ID(i); V o; float a = WKTurn().x; o.cs = TransformObjectToHClip(WKRotY(i.positionOS.xyz, a)); o.n = TransformObjectToWorldNormal(WKRotY(i.normalOS, a)); return o; }
            half4 fragN(V i):SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
}
