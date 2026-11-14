// Spider lily (UWSpiderLily mesh): vertex colour = albedo, vertex alpha = emission mask. Double-sided ribbons, simple
// half-lambert from the main light + ambient, emission glow so the petals read in the dark hell grade. Lives in Resources
// so Shader.Find works in builds.
Shader "Underworld/Lily"
{
    Properties
    {
        _Glow("Emission (HDR multiplier)", Float) = 1.6
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Glow; float4 _Tint;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 nrm : NORMAL; float4 col : COLOR; };
            struct V { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float4 col : COLOR; float fog : TEXCOORD1; float3 ws : TEXCOORD2; };
            V vert(A a)
            {
                V o; o.ws = TransformObjectToWorld(a.pos.xyz); o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(a.nrm); o.col = a.col; o.fog = ComputeFogFactor(o.cs.z); return o;
            }
            half4 frag(V i, bool face : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.n) * (face ? 1 : -1);
                Light L = GetMainLight();
                float lam = saturate(dot(n, L.direction)) * 0.5 + 0.5;
                float3 alb = i.col.rgb * _Tint.rgb;
                float3 c = alb * (L.color * lam * 0.6 + SampleSH(n) + 0.05) + alb * i.col.a * _Glow;
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
            HLSLPROGRAM
            #pragma vertex v2
            #pragma fragment f2
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 v2(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half f2() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
