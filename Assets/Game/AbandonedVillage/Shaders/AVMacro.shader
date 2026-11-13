// 11-13: large-scale light/colour variation laid over the ground (Kido: the floor reads as one fine repeated pattern).
// A terrain-hugging grid whose vertex colours carry slow noise; drawn as a 2x multiply (0.5 = no change), so it darkens
// and lifts the tiling textures in 10-40 m patches without touching their detail.
Shader "AV/MacroMultiply"
{
    Properties { _Strength ("Strength", Range(0, 1)) = 1 }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-120" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Macro"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Strength;
            CBUFFER_END
            struct A { float4 pos : POSITION; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; return o; }
            half4 frag(V i) : SV_Target { return half4(lerp(0.5.xxx, i.col.rgb, _Strength * i.col.a), 1); }
            ENDHLSL
        }
    }
}
