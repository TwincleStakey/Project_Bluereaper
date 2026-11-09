// Soft dark contact shadow laid flat under props (the boardwalk), so they sit on the ground instead of floating.
// Multiplies what is under it; a rounded rectangle that fades out at the edges.
Shader "ReedField/Blob"
{
    Properties
    {
        _Strength("Darkness", Range(0, 1)) = 0.6
        _Soft("Edge softness", Range(0.05, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend DstColor Zero
        ZWrite Off
        Offset -1, -1
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float _Strength, _Soft; CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V2F vert(Attr a) { V2F o; o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o; }
            half4 frag(V2F i) : SV_Target
            {
                float2 e = abs(i.uv - 0.5) * 2;
                float m = (1 - smoothstep(1 - _Soft, 1, e.x)) * (1 - smoothstep(1 - _Soft, 1, e.y));
                return half4((1 - m * _Strength).xxx, 1);
            }
            ENDHLSL
        }
    }
}
