// Stylized water. Vertex colour R holds baked depth (0 = shore, 1 = deep), so the colour bands
// match the terrain exactly without needing the camera depth texture.
Shader "LookStudy/Water"
{
    Properties
    {
        _Shallow("Shallow", Color) = (0.48, 0.92, 0.86, 1)
        _Mid("Mid", Color) = (0.10, 0.68, 0.82, 1)
        _Deep("Deep", Color) = (0.05, 0.34, 0.66, 1)
        _Foam("Foam", Color) = (0.97, 1.0, 0.96, 1)
        _Caustic("Caustics", Range(0,1.5)) = 0.75
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "LSCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Shallow;
            float4 _Mid;
            float4 _Deep;
            float4 _Foam;
            float _Caustic;
            CBUFFER_END

            struct Attr { float4 positionOS : POSITION; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float depth : TEXCOORD1; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.depth = a.color.r;
                return o;
            }

            float Web(float2 p, float t)
            {
                float2 warp = float2(LSNoise(p * 0.7 + t * 0.25), LSNoise(p * 0.7 - t * 0.2 + 5.0));
                float e = LSVoronoi(p + warp * 1.1 + t * 0.09).y;
                return smoothstep(0.11, 0.01, e);
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float t = _LSTime;
                float wob = (LSFbm(xz * 0.5 + t * 0.05) - 0.5) * 0.14;
                float d = saturate(i.depth + wob);

                // Stepped colour bands read like painted water.
                float3 col = lerp(_Shallow.rgb, _Mid.rgb, smoothstep(0.12, 0.34, d));
                col = lerp(col, _Deep.rgb, smoothstep(0.42, 0.80, d));

                float web = Web(xz * 0.95, t) * 0.75 + Web(xz * 1.9 + 11.0, t * 1.3) * 0.5;
                col += _Shallow.rgb * web * _Caustic * lerp(0.75, 0.16, smoothstep(0.1, 0.8, d));

                // Sun sparkle where a noisy normal lines up with the half vector.
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float nx = LSNoise(xz * 2.3 + t * 0.6) - LSNoise(xz * 2.3 - t * 0.5 + 9.0);
                float nz = LSNoise(xz * 2.1 - t * 0.55 + 3.0) - LSNoise(xz * 2.1 + t * 0.45 + 13.0);
                float3 n = normalize(float3(nx * 0.32, 1.0, nz * 0.32));
                float3 hv = normalize(_MainLightPosition.xyz + v);
                float spark = pow(saturate(dot(n, hv)), 420.0);

                float fres = pow(1.0 - saturate(dot(n, v)), 4.0);
                col = lerp(col, LSSky(reflect(-v, n)), fres * 0.7);

                float shadow = LSShadow(i.positionWS);
                col *= lerp(_LSShadowMul * 1.25, _LSSunCol, shadow * LSCloud(xz));
                col += _LSSunCol * spark * 3.5 * shadow;

                // Two travelling foam lines hugging the shore.
                float edge = i.depth + (LSFbm(xz * 1.4) - 0.5) * 0.05;
                float lap = frac(edge * 7.0 - t * 0.22);
                float foam = smoothstep(0.075, 0.03, edge);
                foam = max(foam, smoothstep(0.91, 0.97, lap) * smoothstep(0.26, 0.04, edge) * step(0.36, LSNoise(xz * 2.8)));
                col = lerp(col, _Foam.rgb * _LSSunCol, foam);

                col = LSFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
