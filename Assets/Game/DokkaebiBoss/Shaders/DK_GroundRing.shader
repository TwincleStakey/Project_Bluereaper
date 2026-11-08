// Flat ground quad: expanding shockwave ring (additive) or telegraph circle with fill (alpha).
Shader "Dokkaebi/GroundRing"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        _Radius ("Radius 0-1", Range(0,1)) = 0.5
        _Width ("Width", Range(0.001,0.5)) = 0.08
        _Fill ("Inner Fill", Range(0,1)) = 0
        _Fade ("Fade", Range(0,1)) = 1
        _Wobble ("Edge Wobble", Range(0,0.2)) = 0.03
        [Toggle] _AlphaMode ("Alpha Blend Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Radius; float _Width; float _Fill; float _Fade; float _Wobble; float _AlphaMode; float _SrcBlend; float _DstBlend;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            float hash1(float n) { return frac(sin(n) * 43758.5453); }
            float vnoise(float x) { float i = floor(x); float f = frac(x); f = f * f * (3 - 2 * f); return lerp(hash1(i), hash1(i + 1), f); }
            half4 frag (V i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float r = length(p);
                float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;
                // periodic in angle (integer frequencies) so there is no seam where atan2 wraps
                float a2 = ang * 6.2831853;
                float wob = (sin(a2 * 7 + 1.3) * 0.5 + sin(a2 * 13 + 4.1) * 0.3 + sin(a2 * 29 + 2.2) * 0.2) * _Wobble;
                float rr = r + wob;
                float d = abs(rr - _Radius);
                float band = 1 - smoothstep(_Width * 0.3, _Width, d);
                band *= lerp(0.45, 1.0, smoothstep(_Radius - _Width, _Radius, rr));
                float fill = _Fill * (1 - smoothstep(_Radius - 0.02, _Radius, rr)) * (0.3 + 0.7 * saturate(r / max(_Radius, 0.001)));
                float a = saturate(band + fill) * _Fade * (1 - step(1, r));
                if (_AlphaMode > 0.5) return half4(_Color.rgb, a * _Color.a);
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
