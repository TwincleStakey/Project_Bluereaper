Shader "Underworld/Emit"
{
    Properties
    {
        [HDR] _Col("Colour", Color) = (4, 1.6, 0.6, 1)
        _Flicker("Flicker", Range(0,1)) = 0.25
        _Phase("Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Col; float _Flicker, _Phase;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(V i) : SV_Target
            {
                float f = 1.0 - _Flicker + _Flicker * UWNoise(float2(_UWTime * 7.0 + _Phase * 13.0, _Phase));
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float facing = 0.55 + 0.45 * saturate(dot(normalize(i.n), v));
                return half4(_Col.rgb * f * facing, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
