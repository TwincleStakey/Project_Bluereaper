// Terrain under the grass. Vertex colour: R = dirt path, G = shore sand, B = rock face.
Shader "LookStudy/Ground"
{
    Properties
    {
        _ColA("Patch deep", Color) = (0.05, 0.42, 0.13, 1)
        _ColB("Patch mid", Color) = (0.22, 0.66, 0.16, 1)
        _ColC("Patch light", Color) = (0.62, 0.84, 0.22, 1)
        _RootDark("Root darkening", Range(0,1)) = 0.5
        _TipLift("Tip lift", Range(1,2)) = 1.32
        _DirtA("Dirt light", Color) = (0.86, 0.72, 0.48, 1)
        _DirtB("Dirt dark", Color) = (0.62, 0.46, 0.30, 1)
        _SandCol("Sand", Color) = (0.93, 0.85, 0.62, 1)
        _ClearCol("Clearing", Color) = (0.5, 0.8, 0.3, 1)
        _BladeVar("Per blade variation", Range(0,0.6)) = 0.07
        _PatchContrast("Patch contrast", Range(0,5)) = 3.2
        _ClumpAmt("Clump amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "LSCommon.hlsl"
            #include "LSField.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.color = a.color;
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float3 col = LSFieldColor(xz, 0.42, 0.5);

                float breakup = LSFbm(xz * 1.1) - 0.5;
                float fine = LSNoise(xz * 7.0) - 0.5;
                float3 dirt = lerp(_DirtB.rgb, _DirtA.rgb, smoothstep(0.3, 0.7, LSFbm(xz * 0.55 + 4.0) + fine * 0.25));
                dirt *= 1.0 - smoothstep(0.46, 0.5, LSVoronoi(xz * 2.4).x) * 0.08;
                col = lerp(col, _SandCol.rgb * (0.94 + fine * 0.14), smoothstep(0.42, 0.52, i.color.g + breakup * 0.5));
                col = lerp(col, dirt, smoothstep(0.40, 0.50, i.color.r + breakup * 0.45));

                col = lerp(col, _ClearCol.rgb * (0.96 + fine * 0.1), smoothstep(0.30, 0.72, i.color.b + breakup * 0.2));
                float shadow = LSShadow(i.positionWS);
                float ndl = saturate(dot(normalize(i.normalWS), _MainLightPosition.xyz));
                float light = shadow * LSCloud(xz) * smoothstep(0.05, 0.45, ndl);
                col *= lerp(_LSShadowMul, _LSSunCol, light);
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
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "LSCommon.hlsl"
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
