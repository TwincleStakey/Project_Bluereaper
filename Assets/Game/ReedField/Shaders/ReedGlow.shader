// Soft additive glow sprite, camera facing: 도깨비불 wisps and eyes in the reeds. Colour and alpha from the material
// or from vertex colour (particles). _Core sharpens the bright centre.
Shader "ReedField/Glow"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.4, 0.9, 1.0, 1)
        _Core("Core sharpness", Range(1, 16)) = 4
        _Soft("Halo", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Core, _Soft; CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            V2F vert(Attr a)
            {
                V2F o;
                // Billboard: object origin in world, quad corners offset in view space.
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), 1);
                float3 vc = TransformWorldToView(center);
                vc.xy += a.positionOS.xy * scale.xy;
                o.positionCS = TransformWViewToHClip(vc);
                o.uv = a.uv; o.color = a.color;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2;
                float core = pow(saturate(1 - d), _Core);
                float halo = saturate(1 - d) * saturate(1 - d) * _Soft;
                float a = (core + halo) * _Color.a * i.color.a;
                return half4(_Color.rgb * i.color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
