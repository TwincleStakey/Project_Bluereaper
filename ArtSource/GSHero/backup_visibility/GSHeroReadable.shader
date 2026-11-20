// GS_Hero in-game look (11-15): the GS_Hero textures are mostly near-black armour and a deep burgundy shirt, so under
// URP/Lit in the dark fields the player read as a black/navy blob from the top-down camera (only the blue eye fire
// showed). This keeps the textures but reads like the old 착호갑사 DD/Toon player: soft wrapped moon light, a lift on the
// darkest albedo (hue kept, a little desaturated), a faint sky fill on up-facing surfaces (hat, shoulders), a soft rim on
// the lit side, and the 삿갓 (vertex colour R, baked by GSHeroRemote) tinted a muted straw. No lights are added.
// The sword uses the same shader with _Metal (dull iron: darker diffuse, one broad low sheen, no mirror gloss).
Shader "GSHero/Readable"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Lift ("Dark lift (0 = texture as is)", Range(0, 1)) = 0.5
        _Desat ("Desaturate", Range(0, 1)) = 0.1
        _Wrap ("Light wrap", Range(0, 1)) = 0.5
        _LightHue ("Keep light hue (0 = neutral)", Range(0, 1)) = 0.35
        _ShadowFloor ("Shadowed side keeps", Range(0, 1)) = 0.25
        _SkyFill ("Sky fill on up-facing", Range(0, 2)) = 0.35
        _AmbientBoost ("Ambient boost", Range(0, 4)) = 1.5
        _RimColor ("Rim Color", Color) = (0.85, 0.82, 0.75, 1)
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.2
        _RimWidth ("Rim width", Range(0.05, 1)) = 0.45
        _HatColor ("Hat (vertex R) straw tint", Color) = (0.36, 0.29, 0.18, 1)
        _HatAmount ("Hat tint amount", Range(0, 1)) = 0.85
        _Metal ("Metal (sword)", Range(0, 1)) = 0
        _Sheen ("Metal sheen", Range(0, 2)) = 0.5
        _SheenPower ("Metal sheen tightness", Range(2, 64)) = 10
        _NoFog ("Ignore scene fog", Range(0, 1)) = 1
        _EmissionMap ("Emission Map", 2D) = "black" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Lift, _Desat, _Wrap, _LightHue, _ShadowFloor, _SkyFill, _AmbientBoost;
            half4 _RimColor;
            half _RimStrength, _RimWidth;
            half4 _HatColor;
            half _HatAmount, _Metal, _Sheen, _SheenPower, _NoFog;
            half4 _EmissionColor;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        ENDHLSL

        Pass
        {
            Name "ForwardReadable"
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

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half hat          : TEXCOORD3;
                half fog          : TEXCOORD4;
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
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.hat = i.color.r * i.color.a;   // meshes without colours read white: only the baked hat mesh sets R=1, others R=0
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                // lift the darkest values toward their square root (hue kept), then pull a little colour out
                a = lerp(a, sqrt(max(a, 0)), _Lift);
                half lum = dot(a, half3(0.2126, 0.7152, 0.0722));
                a = lerp(a, lum.xxx, _Desat);
                // 삿갓: the woven texture's detail as luminance, coloured straw
                half3 straw = _HatColor.rgb * (0.55 + 0.9 * saturate(lum / 0.3));
                a = lerp(a, straw, saturate(i.hat) * _HatAmount);
                half3 diff = a * (1 - 0.55 * _Metal);

                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(N, ml.direction);
                half wrap = saturate((ndl + _Wrap) / (1 + _Wrap));
                half sh = lerp(_ShadowFloor, 1, ml.shadowAttenuation) * ml.distanceAttenuation;
                // the dark fields light everything blue (moon, sky): keep only part of that hue, so the burgundy shirt and the
                // straw hat stay their own colour instead of turning purple/navy
                half3 mlc = ml.color;
                mlc = lerp(dot(mlc, half3(0.2126, 0.7152, 0.0722)).xxx, mlc, _LightHue);
                half3 col = diff * mlc * wrap * sh;

                // ambient (scene SH, boosted) and a little extra sky on up-facing surfaces so the hat/shoulders read from above
                half3 amb = SampleSH(N);
                amb = lerp(dot(amb, half3(0.2126, 0.7152, 0.0722)).xxx, amb, _LightHue);
                col += diff * amb * _AmbientBoost;
                col += diff * amb * _SkyFill * 4 * saturate(N.y);

                // dull iron: one broad sheen from the moon and a soft grazing glint from the sky
                half3 H = normalize(ml.direction + V);
                half spec = pow(saturate(dot(N, H)), _SheenPower) * sh;
                col += _Metal * _Sheen * (mlc * spec * 0.6 + amb * 2 * pow(1 - saturate(dot(N, V)), 2)) * (0.4 + a);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                uint lc = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lc)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                    half la = saturate((dot(N, l.direction) + _Wrap) / (1 + _Wrap)) * l.distanceAttenuation * l.shadowAttenuation;
                    col += diff * l.color * la;
                LIGHT_LOOP_END
                #endif

                // soft rim on the lit side of the silhouette
                half fres = smoothstep(1 - _RimWidth, 1, 1 - saturate(dot(N, V)));
                col += _RimColor.rgb * fres * _RimStrength * (0.35 + 0.65 * wrap);

                col += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                // 11-15 v3 (Kido: "뿌옇고 잘 안 보인다"): the player is not fogged; the field fog greyed it from the top-down distance
                col = lerp(MixFog(col, i.fog), col, _NoFog);
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
