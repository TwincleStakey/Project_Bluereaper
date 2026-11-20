// Death's Door-style cel shader for URP 17 (Unity 6).
// 3-band ramp on the main light (with shadows), light-hue tint, stepped additional lights,
// rim on the lit side, optional tiger stripes, and an inverted-hull outline pass that
// extrudes along smoothed normals baked into UV3 by DDConceptSetup's import step.
Shader "DD/Toon"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color", Color) = (0.5, 0.5, 0.5, 1)
        _ShadowTint ("Shadow Tint", Color) = (0.29, 0.33, 0.47, 1)
        _Step1 ("Shadow -> Mid", Range(0, 1)) = 0.06
        _Step2 ("Mid -> Lit", Range(0, 1)) = 0.32
        _MidBlend ("Mid Band Blend", Range(0, 1)) = 0.5
        _LightHue ("Light Hue Tint", Range(0, 1)) = 0.5
        _RimColor ("Rim Color", Color) = (0.78, 0.85, 1, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.15
        _StripeScale ("Stripe Scale (0 = off)", Float) = 0
        _StripeColor ("Stripe Color", Color) = (0.09, 0.07, 0.05, 1)
        _OutlineColor ("Outline Color", Color) = (0.027, 0.03, 0.043, 1)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.05)) = 0.012
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadowTint;
            half _Step1;
            half _Step2;
            half _MidBlend;
            half _LightHue;
            half4 _RimColor;
            half _RimStrength;
            float _StripeScale;
            half4 _StripeColor;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardToon"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half fog          : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionOS = i.positionOS.xyz;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Band(half v)
            {
                return v < _Step1 ? 0.0h : (v < _Step2 ? _MidBlend : 1.0h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 baseCol = _BaseColor.rgb;
                if (_StripeScale > 0)
                {
                    float3 q = i.positionOS;
                    float w = q.y * _StripeScale + sin(q.x * 13.0 + q.z * 11.0) * 0.35 + sin(q.x * 31.0 - q.z * 23.0) * 0.12;
                    baseCol = frac(w) < 0.3 ? _StripeColor.rgb : baseCol;
                }
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndl = saturate(dot(N, mainLight.direction)) * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half band = Band(ndl);

                half3 shadowCol = baseCol * _ShadowTint.rgb;
                half3 cel = lerp(shadowCol, baseCol, band);
                half lum = max(max(mainLight.color.r, mainLight.color.g), mainLight.color.b) + 1e-3;
                half3 hue = lerp(half3(1, 1, 1), mainLight.color / lum, _LightHue);
                half3 col = cel * hue * saturate(lum);

                // small ambient lift so shadows keep their tint instead of going black
                col += baseCol * SampleSH(N) * 0.15;

                // stepped additional lights (lanterns, fox fire, muzzle flash)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    half a = saturate(dot(N, l.direction)) * l.distanceAttenuation * l.shadowAttenuation;
                    // soft pools (stepped pools read as hard discs on the floor)
                    col += baseCol * l.color * smoothstep(0.0h, 0.6h, a) * 0.55;
                LIGHT_LOOP_END
                #endif

                // rim on the lit side
                half rim = (1.0h - saturate(dot(N, V))) > 0.55h ? 1.0h : 0.0h;
                col += _RimColor.rgb * rim * _RimStrength * band;

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 smoothN : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half fog : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = dot(i.smoothN, i.smoothN) > 0.01 ? normalize(i.smoothN) : i.normalOS;
                float3 p = i.positionOS.xyz + n * _OutlineWidth;
                o.positionCS = TransformObjectToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return half4(MixFog(_OutlineColor.rgb, i.fog), 1); }
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 pw = TransformObjectToWorld(i.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes i) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(i); return TransformObjectToHClip(i.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
