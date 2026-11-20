// Choga Slime (초가집 슬라임) body: black translucent jelly.
// - The scene behind is refracted (_CameraOpaqueTexture) and absorbed by thickness (depth difference, capped by a
//   view-angle thickness guess), so the thatch under the jelly shows dimly and the centre goes near-black.
// - Wet look: two specular lobes from the main + additional lights, glossy env reflection with fresnel, dark wine rim.
// - Faint murky veins drift inside (object-space noise with view parallax).
// - Vertex: slow jiggle along the normal + an impact ripple ring (_RippleOrigin/_RippleStart, set by ChogaSlimeBoss).
// Time comes from the global _CS_Time (driver sets it) so edit-mode captures are deterministic.
Shader "BulletHeaven/ChogaSlime"
{
    Properties
    {
        _TintColor ("Transmission Tint", Color) = (0.55, 0.25, 0.6, 1)
        _Absorb ("Absorption per m", Float) = 1.4
        _MaxThick ("Max Thickness", Float) = 5
        _Refract ("Refraction", Float) = 0.05
        _BaseColor ("Surface Base", Color) = (0.008, 0.006, 0.012, 1)
        _SpecIntensity ("Spec Intensity", Float) = 6
        _SpecPower ("Spec Power (sharp)", Float) = 700
        _SpecPower2 ("Spec Power (broad)", Float) = 45
        _Spec2Intensity ("Spec Broad Intensity", Float) = 0.25
        _ReflIntensity ("Env Reflection", Float) = 0.9
        _EnvTop ("Fake Env Top", Color) = (0.22, 0.25, 0.34, 1)
        _EnvBottom ("Fake Env Bottom", Color) = (0.03, 0.025, 0.02, 1)
        _RimColor ("Rim Color", Color) = (0.32, 0.05, 0.12, 1)
        _RimPower ("Rim Power", Float) = 3.5
        _RimIntensity ("Rim Intensity", Float) = 0.6
        _MurkColor ("Murk Color", Color) = (0.35, 0.03, 0.06, 1)
        _MurkScale ("Murk Scale", Float) = 0.35
        _MurkIntensity ("Murk Intensity", Float) = 0.35
        _WobbleAmp ("Wobble Amp (m)", Float) = 0.07
        _WobbleFreq ("Wobble Freq", Float) = 0.45
        _WobbleSpeed ("Wobble Speed", Float) = 0.8
        _ArmWobble ("Arm Wobble Mult", Float) = 0.6
        _RippleOrigin ("Ripple Origin (world)", Vector) = (0, 0, 0, 0)
        _RippleStart ("Ripple Start Time", Float) = -100
        _RippleAmp ("Ripple Amp", Float) = 0.35
        _RippleSpeed ("Ripple Speed", Float) = 9
        _RippleWave ("Ripple Wavelength", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _TintColor; float _Absorb; float _MaxThick; float _Refract; float4 _BaseColor;
            float _SpecIntensity; float _SpecPower; float _SpecPower2; float _Spec2Intensity; float _ReflIntensity; float4 _EnvTop; float4 _EnvBottom;
            float4 _RimColor; float _RimPower; float _RimIntensity;
            float4 _MurkColor; float _MurkScale; float _MurkIntensity;
            float _WobbleAmp; float _WobbleFreq; float _WobbleSpeed; float _ArmWobble;
            float4 _RippleOrigin; float _RippleStart; float _RippleAmp; float _RippleSpeed; float _RippleWave;
        CBUFFER_END
        float _CS_Time;

        float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
        float vnoise(float3 p)
        {
            float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(hash13(i), hash13(i + float3(1,0,0)), f.x), lerp(hash13(i + float3(0,1,0)), hash13(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(hash13(i + float3(0,0,1)), hash13(i + float3(1,0,1)), f.x), lerp(hash13(i + float3(0,1,1)), hash13(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        float fbm(float3 p) { return vnoise(p) * 0.55 + vnoise(p * 2.03 + 7.1) * 0.3 + vnoise(p * 4.1 + 3.3) * 0.15; }

        // jiggle + impact ripple, applied in world space along the world normal
        float3 Deform(float3 posWS, float3 nWS, float armMask)
        {
            float t = _CS_Time;
            float w = fbm(posWS * _WobbleFreq + float3(0, t * _WobbleSpeed, t * 0.37 * _WobbleSpeed)) * 2 - 1;
            float amp = _WobbleAmp * lerp(1, _ArmWobble, armMask);
            float d = 0;
            float rt = t - _RippleStart;
            if (rt > 0 && rt < 4)
            {
                float dist = distance(posWS, _RippleOrigin.xyz);
                float front = rt * _RippleSpeed;
                float x = (dist - front) / (_RippleWave * 1.5);
                float env = exp(-x * x) * exp(-rt * 1.2);
                d = sin((dist - front) * (6.2831 / _RippleWave)) * env * _RippleAmp;
            }
            return posWS + nWS * (w * amp + d);
        }
        ENDHLSL

        Pass
        {
            Name "SlimeForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On Cull Back Blend One Zero
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; float3 posOS : TEXCOORD2; float arm : TEXCOORD3; float fog : TEXCOORD4; };

            V vert(A i)
            {
                V o;
                float3 pw = TransformObjectToWorld(i.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                pw = Deform(pw, nw, i.color.r);
                o.posWS = pw; o.nWS = nw; o.posOS = i.positionOS.xyz; o.arm = i.color.r;
                o.positionCS = TransformWorldToHClip(pw);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Spec(Light l, float3 N, float3 V, float shadow)
            {
                float3 H = normalize(l.direction + V);
                float nh = saturate(dot(N, H)); float nl = saturate(dot(N, l.direction));
                float s = pow(nh, _SpecPower) * _SpecIntensity + pow(nh, _SpecPower2) * _Spec2Intensity;
                return l.color * s * nl * shadow * l.distanceAttenuation;
            }

            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.nWS);
                // small surface ripples in the normal so highlights break up like a wet skin
                float t = _CS_Time;
                float3 q = i.posWS * 1.7 + float3(0, t * 0.15, 0);
                float3 bump = float3(vnoise(q) - 0.5, vnoise(q + 11.3) - 0.5, vnoise(q + 23.7) - 0.5);
                N = normalize(N + bump * 0.18);
                float3 V = normalize(GetWorldSpaceViewDir(i.posWS));
                float nv = saturate(dot(N, V));

                // refraction + absorption
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nVS = TransformWorldToViewDir(N);
                float2 off = nVS.xy * _Refract * (1.2 - nv);
                float surfEye = i.positionCS.w;
                float2 ruv = suv + off;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (sceneEye < surfEye) { ruv = suv; sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams); }
                float thick = min(max(sceneEye - surfEye, 0), _MaxThick * pow(nv, 0.8) + 0.08);
                float3 absorb = _Absorb * (1.15 - _TintColor.rgb);
                float3 trans = SampleSceneColor(ruv) * exp(-absorb * thick);

                // murky veins inside, slowly drifting, seen with a bit of parallax
                float3 mp = i.posOS * _MurkScale - V * 0.6 + float3(t * 0.03, -t * 0.05, 0);
                float m = fbm(mp);
                // soft clouded strands, only in some patches (thin contour lines read as "computer")
                float vein = pow(saturate(1 - abs(m * 2 - 1) * 2.2), 2.2);
                float patch = smoothstep(0.45, 0.7, vnoise(i.posOS * 0.18 + 5.3 + float3(0, t * 0.02, 0)));
                float3 murk = _MurkColor.rgb * vein * patch * _MurkIntensity * (0.25 + 0.75 * nv);

                // lights
                float4 sc = TransformWorldToShadowCoord(i.posWS);
                Light ml = GetMainLight(sc);
                float sh = ml.shadowAttenuation;
                float3 spec = Spec(ml, N, V, lerp(0.25, 1, sh));
                float3 diff = _BaseColor.rgb * (ml.color * saturate(dot(N, ml.direction)) * sh + SampleSH(N));
                #if defined(_ADDITIONAL_LIGHTS)
                uint cnt = GetAdditionalLightsCount();
                InputData inputData = (InputData)0; inputData.positionWS = i.posWS; inputData.normalizedScreenSpaceUV = suv;
                LIGHT_LOOP_BEGIN(cnt)
                    Light al = GetAdditionalLight(lightIndex, i.posWS, half4(1,1,1,1));
                    spec += Spec(al, N, V, al.shadowAttenuation);
                    diff += _BaseColor.rgb * al.color * al.distanceAttenuation * saturate(dot(N, al.direction));
                LIGHT_LOOP_END
                #endif

                float fres = 0.03 + 0.97 * pow(1 - nv, 5);
                float3 R = reflect(-V, N);
                float3 env = lerp(_EnvBottom.rgb, _EnvTop.rgb, smoothstep(-0.3, 0.6, R.y));   // a dim sky so the wet skin reads even with no probe
                float3 refl = (GlossyEnvironmentReflection(R, 0.06h, 1.0h) + env) * fres * _ReflIntensity;
                float3 rim = _RimColor.rgb * pow(1 - nv, _RimPower) * _RimIntensity * (0.4 + 0.6 * sh);

                float3 col = trans * (1 - fres) + diff + spec + refl + rim + murk;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            float4 vert(A i) : SV_POSITION
            {
                float3 pw = TransformObjectToWorld(i.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                pw = Deform(pw, nw, i.color.r);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
