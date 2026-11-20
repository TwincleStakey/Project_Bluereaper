// Ground crack left by the slam. Texture R = crack lines, G = scorch radial mask.
// Cracks grow outward, glow hot, then cool into dark grooves while the scorch fades.
Shader "Dokkaebi/CrackDecal"
{
    Properties
    {
        _MainTex ("Crack (R lines, G scorch)", 2D) = "black" {}
        [HDR] _GlowColor ("Glow", Color) = (0.4,1.6,3,1)
        _GrooveColor ("Groove", Color) = (0.16,0.11,0.09,1)
        _ScorchColor ("Scorch", Color) = (0.18,0.15,0.14,1)
        _Glow ("Glow Amount", Range(0,1)) = 1
        _Fade ("Fade", Range(0,1)) = 1
        _Reveal ("Reveal (crack growth)", Range(0,1.2)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _GlowColor; half4 _GrooveColor; half4 _ScorchColor; float _Glow; float _Fade; float _Reveal;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag (V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float r = length(i.uv * 2 - 1);
                float reveal = 1 - smoothstep(_Reveal - 0.08, _Reveal, r);
                float crack = t.r * reveal;
                float scorch = t.g;
                half3 col = lerp(_ScorchColor.rgb, _GrooveColor.rgb, crack);
                float glowMask = crack * _Glow * saturate(1.2 - r);
                col += _GlowColor.rgb * glowMask;
                float a = saturate(max(scorch * 0.6, crack) + glowMask) * _Fade;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
