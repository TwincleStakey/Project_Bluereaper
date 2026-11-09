// Still black marsh water in the channel: mirror-like sky tint, moon glint, slow ripples, fog on top.
Shader "ReedField/Water"
{
    Properties
    {
        _Deep("Deep", Color) = (0.01, 0.012, 0.016, 1)
        _Sky("Reflected sky", Color) = (0.10, 0.12, 0.17, 1)
        _Glint("Glint", Float) = 0.8
        _Ripple("Ripple", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Deep, _Sky; float _Glint, _Ripple; CBUFFER_END
            struct Attr { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; };
            V2F vert(Attr a) { V2F o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws); return o; }
            half4 frag(V2F i) : SV_Target
            {
                float t = _RF_Time;
                float2 p = i.ws.xz;
                float e = 0.15;
                float h0 = RFNoise(p * 0.9 + float2(t * 0.12, t * 0.07)) + RFNoise(p * 2.3 - float2(t * 0.2, -t * 0.1)) * 0.5;
                float hx = RFNoise((p + float2(e, 0)) * 0.9 + float2(t * 0.12, t * 0.07)) + RFNoise((p + float2(e, 0)) * 2.3 - float2(t * 0.2, -t * 0.1)) * 0.5;
                float hz = RFNoise((p + float2(0, e)) * 0.9 + float2(t * 0.12, t * 0.07)) + RFNoise((p + float2(0, e)) * 2.3 - float2(t * 0.2, -t * 0.1)) * 0.5;
                float3 n = normalize(float3((h0 - hx) * _Ripple * 4, 1, (h0 - hz) * _Ripple * 4));
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float fres = pow(1 - saturate(dot(n, v)), 3);
                Light ml = GetMainLight();
                float3 col = lerp(_Deep.rgb, _Sky.rgb, 0.35 + fres * 0.65);
                float3 r = reflect(-v, n);
                col += ml.color * pow(saturate(dot(r, ml.direction)), 220) * _Glint;
                col += ml.color * pow(saturate(dot(r, ml.direction)), 18) * 0.06;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    col += add.color * add.distanceAttenuation * (pow(saturate(dot(r, add.direction)), 60) * 1.5 + 0.05);
                LIGHT_LOOP_END
                return half4(RFFog(col, i.ws), 1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
}
