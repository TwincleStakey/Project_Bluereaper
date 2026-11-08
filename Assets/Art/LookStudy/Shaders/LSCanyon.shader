// Layered rock. Colour bands are a function of world height, not textures; the faceted look
// comes from a per-pixel face normal, so the mesh can be plain heightfield.
Shader "LookStudy/Canyon"
{
    Properties
    {
        _B1("Band 1", Color) = (0.70, 0.36, 0.30, 1)
        _B2("Band 2", Color) = (0.85, 0.62, 0.48, 1)
        _B3("Band 3", Color) = (0.56, 0.34, 0.42, 1)
        _B4("Band 4", Color) = (0.82, 0.72, 0.58, 1)
        _Top("Top moss", Color) = (0.62, 0.62, 0.22, 1)
        _BandScale("Band scale", Float) = 0.33
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _B1;
        float4 _B2;
        float4 _B3;
        float4 _B4;
        float4 _Top;
        float _BandScale;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 face = normalize(cross(ddy(ws), ddx(ws)));
                if (dot(face, i.normalWS) < 0.0) face = -face;
                float3 n = normalize(lerp(normalize(i.normalWS), face, 0.75));

                float warp = (LSFbm(ws.xz * 0.035) - 0.5) * 7.0 + (LSNoise(ws.xz * 0.5) - 0.5) * 0.55;
                float y = (ws.y + warp) * _BandScale;
                float b0 = LSNoise(float2(y * 0.55, 3.0));
                float b1 = LSNoise(float2(y * 1.9, 11.0));
                float3 col = lerp(_B1.rgb, _B2.rgb, smoothstep(0.25, 0.62, b0));
                col = lerp(col, _B3.rgb, smoothstep(0.58, 0.72, b1) * 0.75);
                col = lerp(col, _B4.rgb, smoothstep(0.66, 0.78, LSNoise(float2(y * 0.9, 23.0))));
                col *= 1.0 - smoothstep(0.76, 0.86, LSNoise(float2(y * 7.0, 31.0))) * 0.2;
                col *= 0.88 + LSNoise(float2(y * 14.0, ws.x * 0.05)) * 0.24;

                float top = smoothstep(0.70, 0.92, normalize(i.normalWS).y + (LSFbm(ws.xz * 0.25) - 0.5) * 0.3);
                float3 moss = _Top.rgb * (0.82 + LSFbm(ws.xz * 0.11 + 4.0) * 0.5);
                col = lerp(col, moss, top);

                float ndl = dot(n, _MainLightPosition.xyz) * 0.5 + 0.5;
                float lit = smoothstep(0.38, 0.66, ndl);
                float shadow = LSShadow(ws);
                col *= lerp(_LSShadowMul, _LSSunCol, lit * shadow * LSCloud(ws.xz));
                col = LSFog(col, ws);
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
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attr a) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
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
