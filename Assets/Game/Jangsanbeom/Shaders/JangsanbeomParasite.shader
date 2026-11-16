// 장산범 snake parasite: wet dark-red flesh that gulps (bulges travel along it, toward the gramophone, as if sucking blood)
// with an inner red heartbeat glow that peaks very bright. UV.x = distance along the tube (set in Blender).
// URP forward. Needs Bloom + HDR on the camera for the peaks to bloom.
Shader "BulletHeaven/JangsanbeomParasite"
{
    Properties
    {
        _BaseColor ("Flesh Color", Color) = (0.20, 0.025, 0.02, 1)
        _CrustColor ("Dried Crust Color", Color) = (0.09, 0.07, 0.06, 1)
        _CrustAmount ("Crust Amount", Range(0, 1)) = 0.35
        _Smoothness ("Wetness", Range(0, 1)) = 0.75

        [Header(Gulp (travelling bulges))]
        _WaveDir ("Wave Direction (+1 / -1 along UV.x)", Float) = 1
        _GulpFreq ("Gulps per UV unit", Range(0, 40)) = 8
        _GulpSpeed ("Gulp Speed", Range(0, 10)) = 1.6
        _GulpSharp ("Gulp Sharpness", Range(1, 30)) = 10
        _GulpBulge ("Gulp Bulge (object units)", Range(0, 1)) = 0.16

        [Header(Heartbeat glow)]
        [HDR] _GlowColor ("Glow Color", Color) = (1, 0.06, 0.02, 1)
        _GlowBase ("Glow at rest", Range(0, 5)) = 0.25
        _GlowPeak ("Glow at heartbeat peak", Range(0, 40)) = 14
        _GulpGlow ("Extra glow inside a gulp", Range(0, 40)) = 10
        _BeatSpeed ("Heartbeat Speed", Range(0, 10)) = 1.4
        _Warn ("Warn (attack telegraph)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half4 _CrustColor; half _CrustAmount; half _Smoothness;
            float _WaveDir; half _GulpFreq; half _GulpSpeed; half _GulpSharp; half _GulpBulge;
            half4 _GlowColor; half _GlowBase; half _GlowPeak; half _GulpGlow; half _BeatSpeed; half _Warn;
        CBUFFER_END
        // capture tools can freeze time: _JSB_FixedTime >= 0 replaces _Time.y (default global is 0, so the flag is separate)
        float _JSB_UseFixedTime; float _JSB_FixedTime;
        float T() { return _JSB_UseFixedTime > 0.5 ? _JSB_FixedTime : _Time.y; }
        float Gulp(float u)
        {
            float w = frac(u * _GulpFreq - _WaveDir * T() * _GulpSpeed);   // 11-16 Kido: no speed-up while warning
            return pow(saturate(sin(w * 6.2832) * 0.5 + 0.5), _GulpSharp);
        }
        float Beat()
        {
            float t = T() * _BeatSpeed;   // a calm heart, also while warning
            float p = frac(t);
            // 11-16 Kido ("핏줄이 맥박같이, 너무 반짝거려"): a soft swell and fade instead of the sharp lub-dub spikes
            float swell = smoothstep(0.0, 0.14, p) * (1 - smoothstep(0.14, 0.8, p));
            float echo = smoothstep(0.2, 0.3, p) * (1 - smoothstep(0.3, 0.6, p));
            return swell + 0.35 * echo;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; float3 normalWS:TEXCOORD2; float3 positionOS:TEXCOORD3; half gulp:TEXCOORD4; half fog:TEXCOORD5; };
            V vert(A i)
            {
                V o; float g = Gulp(i.uv.x);
                float3 p = i.positionOS.xyz + i.normalOS * g * _GulpBulge;
                VertexPositionInputs vp = GetVertexPositionInputs(p);
                o.positionCS = vp.positionCS; o.positionWS = vp.positionWS; o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv; o.positionOS = i.positionOS.xyz; o.gulp = g; o.fog = ComputeFogFactor(vp.positionCS.z);
                return o;
            }
            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash31(i), hash31(i + float3(1,0,0)), f.x), lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x), lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS); float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float nz = vnoise(i.positionOS * 3.0) * 0.6 + vnoise(i.positionOS * 9.0) * 0.4;
                half crust = smoothstep(1 - _CrustAmount, 1 - _CrustAmount + 0.12, nz);
                half3 albedo = lerp(_BaseColor.rgb * (0.6 + 0.8 * vnoise(i.positionOS * 20.0)), _CrustColor.rgb, crust);
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, ml.direction));
                half3 h = normalize(ml.direction + v);
                half wet = (1 - crust) * _Smoothness;
                half spec = pow(saturate(dot(n, h)), 8 + 120 * wet) * wet * 2;
                half3 amb = max(unity_AmbientSky.rgb, half3(0.02, 0.022, 0.03));
                half3 col = albedo * (ml.color * ndl * ml.shadowAttenuation + amb * 3) + ml.color * spec * ml.shadowAttenuation;
                // inner glow: strongest where the flesh faces the camera (light from inside), crust blocks it
                half inner = (0.35 + 0.65 * saturate(dot(n, v))) * (1 - 0.85 * crust);
                // Diablo-tumour look: dark skin, red light seeping through a web of cracks; the cracks widen on each beat
                half beat = Beat();
                float cn = vnoise(i.positionOS * 4.0 + float3(0, 0, i.uv.x * 2.0)) * 0.65 + vnoise(i.positionOS * 11.0) * 0.35;
                half crackW = 0.06 + 0.10 * beat + 0.08 * i.gulp;
                half crack = 1 - smoothstep(0.0, crackW, abs(cn - 0.5));
                half glow = _GlowBase + _GlowPeak * beat * (1 + 0.3 * _Warn) + _GulpGlow * i.gulp;
                half mask = lerp(0.18, 1.0, crack) * inner;
                // keep the hue deep red even at the peak (bright values would clip toward orange under ACES)
                half3 gc = _GlowColor.rgb * glow * mask;
                gc = half3(gc.r, gc.r * 0.035, gc.r * 0.02);
                col = col * (1 - 0.6 * crack * saturate(glow * 0.2)) + gc;
                col = MixFog(col, i.fog);
                return half4(col, 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            float4 vertS(A i):SV_POSITION
            {
                float3 p = i.positionOS.xyz + i.normalOS * Gulp(i.uv.x) * _GulpBulge;
                float3 pw = TransformObjectToWorld(p); float3 nw = TransformObjectToWorldNormal(i.normalOS);
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
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            float4 vertD(A i):SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz + i.normalOS * Gulp(i.uv.x) * _GulpBulge); }
            half4 fragD():SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
