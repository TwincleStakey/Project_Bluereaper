// Alpha-blended soft smoke/dust with a baked fake top light so puffs read as volumes.
// 11-13: soft-particle depth fade (the slam dust billboards cut into the floor with a hard straight edge) and a global
// brightness scale _DK_SmokeScale (0 = 1x) that the underworld look sets, since its +2.2 EV grade blew the beige dust out.
Shader "Dokkaebi/Smoke"
{
    Properties
    {
        _MainTex ("Texture (R lit amount, A mask)", 2D) = "white" {}
        _Color ("Lit Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Color", Color) = (0.5,0.45,0.42,1)
        _SoftDist ("Soft particle fade distance (m)", Float) = 0.8
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float _DK_SmokeScale;
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Color; half4 _ShadeColor; float _SoftDist;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; float4 sp : TEXCOORD1; };
            V vert (A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.col = i.col;
                o.sp = ComputeScreenPos(o.pos); o.sp.z = -TransformWorldToView(TransformObjectToWorld(i.pos.xyz)).z;   // eye depth
                return o;
            }
            half4 frag (V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 c = lerp(_ShadeColor.rgb, _Color.rgb, t.r) * i.col.rgb;
                half edge = saturate((0.5 - length(i.uv - 0.5)) * 6);
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                half soft = saturate((scene - i.sp.z) / max(_SoftDist, 1e-3));
                c *= _DK_SmokeScale > 0 ? _DK_SmokeScale : 1.0;
                return half4(c, t.a * edge * i.col.a * _Color.a * soft);
            }
            ENDHLSL
        }
    }
}
