// Alpha-blended soft smoke/dust with a baked fake top light so puffs read as volumes.
Shader "Dokkaebi/Smoke"
{
    Properties
    {
        _MainTex ("Texture (R lit amount, A mask)", 2D) = "white" {}
        _Color ("Lit Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.5,0.45,0.42,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Color; half4 _ShadeColor;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.col = i.col; return o; }
            half4 frag (V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 c = lerp(_ShadeColor.rgb, _Color.rgb, t.r) * i.col.rgb;
                half edge = saturate((0.5 - length(i.uv - 0.5)) * 6);
                return half4(c, t.a * edge * i.col.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
