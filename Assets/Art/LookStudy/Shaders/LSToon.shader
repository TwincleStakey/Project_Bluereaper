// Stepped light for rocks, foliage and the character. Colour comes from _BaseColor x vertex colour.
// Vertex alpha: 1 = leaf (gentle sway). _Cull lets leaf cards render both sides.
Shader "LookStudy/Toon"
{
    Properties
    {
        _BaseColor("Base", Color) = (1, 1, 1, 1)
        _Rim("Rim", Range(0,1)) = 0.12
        _Sway("Leaf sway", Range(0,0.2)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float _Rim;
        float _Sway;
        float _Cull;
        CBUFFER_END
        float3 LSLeaf(float3 ws, float amount)
        {
            float w = sin(_LSTime * 1.7 + dot(ws, float3(1.3, 0.7, 1.1)));
            float w2 = sin(_LSTime * 3.1 + dot(ws, float3(2.9, 1.7, 2.3)));
            return ws + float3(w, w2 * 0.5, w2) * amount * _Sway;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                float3 ws = LSLeaf(TransformObjectToWorld(a.positionOS.xyz), a.color.a);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.color = a.color;
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                RRSeeThroughClip(i.positionCS, i.positionWS);   // 11-13: soft see-through near the player
                float3 n = normalize(i.normalWS);
                float3 l = _MainLightPosition.xyz;
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float ndl = dot(n, l) * 0.5 + 0.5;
                float lit = smoothstep(0.47, 0.53, ndl);
                float high = smoothstep(0.76, 0.81, ndl);
                float shadow = lerp(LSShadow(i.positionWS), 1.0, saturate(i.color.a) * 0.85) * LSCloud(i.positionWS.xz);
                float3 base = _BaseColor.rgb * i.color.rgb;
                float light = lit * shadow;
                float3 col = base * lerp(_LSShadowMul, _LSSunCol * (1.0 + high * 0.14), light);
                col += base * _LSSunCol * _Rim * pow(1.0 - saturate(dot(n, v)), 3.0) * (0.35 + light);
                col = LSFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            float4 vert(Attr a) : SV_POSITION
            {
                float3 ws = LSLeaf(TransformObjectToWorld(a.positionOS.xyz), a.color.a);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
