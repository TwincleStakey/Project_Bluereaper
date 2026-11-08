Shader "LookStudy/Sky"
{
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "LSCommon.hlsl"
            struct Attr { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V2F vert(Attr a)
            {
                V2F o;
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.dir = a.positionOS.xyz;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float3 d = normalize(i.dir);
                d.y = abs(d.y);
                return half4(LSSky(d), 1);
            }
            ENDHLSL
        }
    }
}
