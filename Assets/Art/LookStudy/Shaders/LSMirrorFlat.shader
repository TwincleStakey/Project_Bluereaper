// Salt flat that mirrors the sky. There is no planar reflection camera: the reflected view ray
// just samples the same sky function as the skybox, which is why it stays cheap.
Shader "LookStudy/MirrorFlat"
{
    Properties
    {
        _Salt("Salt", Color) = (0.97, 0.93, 0.86, 1)
        _Crack("Crack", Color) = (0.74, 0.68, 0.68, 1)
        _WetAmount("Wet amount", Range(0,1)) = 0.58
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
            CBUFFER_START(UnityPerMaterial)
            float4 _Salt;
            float4 _Crack;
            float _WetAmount;
            CBUFFER_END

            struct Attr { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // Expanding rings around a point, returned as a slope to bend the reflection.
            float2 Ring(float2 xz, float2 c, float t)
            {
                float2 d = xz - c;
                float r = length(d);
                float front = frac(t) * 4.6;
                float env = smoothstep(0.55, 0.0, abs(r - front)) * (1.0 - frac(t));
                float wave = sin((r - front) * 16.0) * env;
                return (r > 0.001 ? d / r : float2(0, 0)) * wave;
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);

                float2 cell = LSVoronoi(xz * 0.34 + (LSNoise(xz * 0.5) - 0.5) * 0.35);
                float crack = smoothstep(0.075, 0.02, cell.y);

                float wetN = LSFbm(xz * 0.062 + 2.7);
                float wet = smoothstep(1.0 - _WetAmount - 0.012, 1.0 - _WetAmount + 0.012, wetN + (LSNoise(xz * 1.5) - 0.5) * 0.03);
                float rim = smoothstep(0.03, 0.0, abs(wetN + (LSNoise(xz * 1.5) - 0.5) * 0.03 - (1.0 - _WetAmount)));

                float2 slope = Ring(xz, _LSPush.xz, _LSTime * 0.55) * 0.05 + Ring(xz, _LSPush.xz, _LSTime * 0.55 + 0.5) * 0.05;
                float g = 1.1;
                float2 rc = floor(xz / 7.0);
                float2 rp = (rc + LSHash2(rc)) * 7.0;
                slope += Ring(xz, rp, _LSTime * 0.2 + LSHash(rc) * 7.0) * 0.012 * g;
                slope += float2(LSNoise(xz * 0.8 + _LSTime * 0.1), LSNoise(xz * 0.8 - _LSTime * 0.1 + 4.0)) * 0.012 - 0.006;

                float3 n = normalize(float3(slope.x, 1.0, slope.y));
                float3 r = reflect(-v, n);
                r.y = abs(r.y);
                float3 refl = LSSky(r);
                float fres = lerp(0.88, 1.0, pow(1.0 - saturate(v.y), 3.0));

                float shadow = LSShadow(i.positionWS);
                float3 salt = lerp(_Salt.rgb, _Crack.rgb, crack) * (0.97 + (LSNoise(xz * 6.0) - 0.5) * 0.06);
                salt *= lerp(_LSShadowMul, _LSSunCol, shadow);
                salt += (0.42 * _LSSkyTop + 0.18 * _LSSkyHor) * (1.0 - _LSSunCol * 0.5) * 0.3;

                float3 mirror = refl * fres * lerp(0.42, 1.0, shadow);
                mirror *= 1.0 - crack * 0.12;
                float3 col = lerp(salt, mirror, wet);
                col = lerp(col, _Salt.rgb * lerp(_LSShadowMul, _LSSunCol, shadow) * 1.04, rim * 0.75);
                col = LSFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
