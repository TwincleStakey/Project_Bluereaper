// 거점 talisman strips on the sealed gates (11-20): a yellow paper strip with the cinnabar strokes of the 길상호신부
// woodblock talisman (Wikimedia Commons, public domain; mask cut for BossSeal, BossSeal/Source/Motifs/BS_Bujeok_Gilsang.png).
// Drawn as particles (HubSealGate burns them off). Opaque cutout, lit only by a fixed ambient so it reads in the dark court.
Shader "Hub/Talisman"
{
    Properties
    {
        _MainTex ("Stroke mask", 2D) = "black" {}
        _Paper ("Paper", Color) = (0.86, 0.68, 0.22, 1)
        _Ink ("Ink", Color) = (0.62, 0.08, 0.04, 1)
        _Bright ("Brightness", Float) = 0.75
        _Photo ("1 = _MainTex is a full-colour talisman photo", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+20" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float4 _Paper; float4 _Ink; float _Bright; float _Photo;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 frag(V i) : SV_Target
            {
                float m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                float2 e = min(i.uv, 1 - i.uv);
                float edge = saturate(min(e.x, e.y) / 0.06);            // darker torn edges
                float3 c = lerp(_Paper.rgb * (0.65 + 0.35 * edge), _Ink.rgb, saturate(m * 1.4));
                float3 photo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * (0.7 + 0.3 * edge);
                c = lerp(c, photo, _Photo);
                return half4(c * _Bright * i.col.rgb, 1);
            }
            ENDHLSL
        }
    }
}
