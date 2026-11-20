// 거점 portal (11-20): cyan swirl inside the left gate. Additive, unlit. Two layers of a downloaded Kenney texture
// (twirl_01 / smoke_04, CC0) spun in polar space with a radius-dependent twist, a bright rim and a dark eye.
// _Open 0..1 is driven by HubSealGate (0 = invisible).
Shader "Hub/Portal"
{
    Properties
    {
        _SwirlTex ("Swirl (Kenney twirl)", 2D) = "white" {}
        _NoiseTex ("Noise (Kenney smoke)", 2D) = "white" {}
        _Col ("Colour", Color) = (0.2, 0.95, 1.0, 1)
        _Deep ("Eye colour", Color) = (0.02, 0.12, 0.16, 1)
        _Gain ("Gain", Float) = 2.2
        _Spin ("Spin speed", Float) = 0.35
        _Twist ("Twist", Float) = 2.4
        _Open ("Open", Range(0, 1)) = 1
        _Aspect ("Width / height", Float) = 0.78
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_SwirlTex); SAMPLER(sampler_SwirlTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SwirlTex_ST; float4 _NoiseTex_ST;
                float4 _Col; float4 _Deep; float _Gain; float _Spin; float _Twist; float _Open; float _Aspect;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            float2 Rot(float2 p, float a) { float s = sin(a), c = cos(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
            half4 frag(V i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                p.x /= _Aspect;                                  // the doorway is taller than wide: an elliptical swirl
                float r = length(p);
                float open = saturate(_Open);
                float edge = 0.92 * open;
                float mask = saturate((edge - r) / 0.18);         // soft outer edge
                float t = _Time.y * _Spin;
                float tw = _Twist * (1.0 - saturate(r));         // inner turns faster: a vortex
                float2 q1 = Rot(p, t * 6.2832 + tw) * 0.5 + 0.5;
                float2 q2 = Rot(p * 0.8, -t * 3.1 + tw * 1.6) * 0.5 + 0.5;
                float s1 = SAMPLE_TEXTURE2D(_SwirlTex, sampler_SwirlTex, q1).r;
                float s2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, q2).r;
                float arms = saturate(s1 * 1.2 + s2 * 0.6 - 0.25);
                float rim = saturate(1.0 - abs(r - edge * 0.86) / 0.12) * 0.8;
                float eye = saturate(r / 0.22);
                float3 c = lerp(_Deep.rgb, _Col.rgb, saturate(arms + rim)) * (arms * 0.9 + rim + 0.12) * eye;
                return half4(c * _Gain * mask * open, 1);
            }
            ENDHLSL
        }
    }
}
