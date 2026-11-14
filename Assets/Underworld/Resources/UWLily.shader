// Spider lily (11-14: CC0 Sketchfab scan, Resources/Hell/SpiderLily): albedo from _MainTex, emission mask = how red the
// texel is (petals / stamens glow, stem and green bits do not). Double-sided, simple half-lambert from the main light +
// ambient, emission glow so the petals read in the dark hell grade. Lives in Resources so Shader.Find works in builds.
Shader "Underworld/Lily"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _Glow("Emission (HDR multiplier)", Float) = 1.6
        _MaskGain("Red -> emission mask gain", Float) = 2.5
        _Tint("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float _Glow; float _MaskGain; float4 _Tint;
            CBUFFER_END
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float2 uv : TEXCOORD1; float fog : TEXCOORD2; };
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a); V o; float3 ws = TransformObjectToWorld(a.pos.xyz); o.cs = TransformWorldToHClip(ws);
                o.n = TransformObjectToWorldNormal(a.nrm); o.uv = TRANSFORM_TEX(a.uv, _MainTex); o.fog = ComputeFogFactor(o.cs.z); return o;
            }
            half4 frag(V i, bool face : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.n) * (face ? 1 : -1);
                Light L = GetMainLight();
                float lam = saturate(dot(n, L.direction)) * 0.5 + 0.5;
                float3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 alb = tex * _Tint.rgb;
                float mask = saturate((tex.r - max(tex.g, tex.b)) * _MaskGain);
                float3 c = alb * (L.color * lam * 0.6 + SampleSH(n) + 0.05) + alb * mask * _Glow;
                c = MixFog(c, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex v2
            #pragma fragment f2
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A2 { float4 p : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 v2(A2 a) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(a); return TransformObjectToHClip(a.p.xyz); }
            half f2() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
