// Translucent ghost: pale cloth, violet fresnel rim, hem dissolves into wisps, hover + flutter in the vertex stage.
Shader "Underworld/Ghost"
{
    Properties
    {
        _Col("Cloth colour", Color) = (0.78, 0.76, 0.86, 0.6)
        [HDR] _Rim("Rim colour", Color) = (1.6, 0.9, 3.2, 1)
        _RimPow("Rim power", Float) = 2.5
        [HDR] _Edge("Dissolve edge", Color) = (2.0, 0.8, 4.0, 1)
        _Height("Figure height", Float) = 1.85
        _Fade("Hem fade height", Range(0,1)) = 0.45
        _Phase("Phase", Float) = 0
        _Hover("Hover amplitude", Float) = 0.12
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "UWCommon.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _Col, _Rim, _Edge;
        float _RimPow, _Height, _Fade, _Phase, _Hover;
        CBUFFER_END
        struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float h : TEXCOORD2; float fog : TEXCOORD3; };
        V2F vert(Attr a)
        {
            V2F o;
            float3 p = a.positionOS.xyz;
            float h = saturate(p.y / _Height);
            float t = _UWTime + _Phase;
            float low = 1.0 - h;
            p.x += sin(t * 2.1 + p.y * 3.0) * 0.06 * low * low;
            p.z += cos(t * 1.7 + p.y * 2.5 + p.x * 2.0) * 0.06 * low * low;
            p.y += sin(t * 1.3) * _Hover;
            o.positionWS = TransformObjectToWorld(p);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(a.normalOS);
            o.h = h;
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        ENDHLSL
        // depth prepass so the figure does not sort against itself
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragD
            half4 fragD(V2F i) : SV_Target
            {
                float n = UWFbm(i.positionWS.xz * 3.0 + float2(0, _UWTime * 0.5) + i.positionWS.y * 2.0);
                clip(n - (1.0 - i.h) * 1.25 + 0.15 - 0.2);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Ghost"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            half4 frag(V2F i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = pow(1.0 - saturate(abs(dot(n, v))), _RimPow);
                float n1 = UWFbm(i.positionWS.xz * 3.0 + float2(0, _UWTime * 0.5) + i.positionWS.y * 2.0);
                float cut = n1 - (1.0 - i.h) * 1.25 + 0.15;
                clip(cut);
                float edge = smoothstep(0.12, 0.0, cut);
                Light L = GetMainLight();
                float lit = 0.55 + 0.45 * saturate(dot(n, L.direction));
                float3 col = _Col.rgb * lit * (0.6 + 0.6 * i.h) + _Rim.rgb * fres + _Edge.rgb * edge;
                float a = _Col.a * smoothstep(0.0, _Fade, i.h + 0.1) + fres * 0.4 + edge * 0.6;
                col = MixFog(col, i.fog);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
