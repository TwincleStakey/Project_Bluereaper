// Choga Slime (초가집 슬라임) eyes: small hot-red embers. Opaque, so the jelly in front refracts them; HDR for bloom.
// Hot core where the eye faces the viewer, darker blood-red edge, slow smoulder flicker. _Glow is driven per renderer.
Shader "BulletHeaven/ChogaSlimeEye"
{
    Properties
    {
        _CoreColor ("Core", Color) = (4.5, 0.32, 0.1, 1)
        _EdgeColor ("Edge", Color) = (0.9, 0.015, 0.01, 1)
        _Glow ("Glow", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _CoreColor; float4 _EdgeColor; float _Glow; CBUFFER_END
            float _CS_Time;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; float3 nWS : TEXCOORD0; float3 pWS : TEXCOORD1; };
            V vert(A i) { V o; o.pWS = TransformObjectToWorld(i.positionOS.xyz); o.nWS = TransformObjectToWorldNormal(i.normalOS); o.positionCS = TransformWorldToHClip(o.pWS); return o; }
            half4 frag(V i) : SV_Target
            {
                float nv = saturate(dot(normalize(i.nWS), normalize(GetWorldSpaceViewDir(i.pWS))));
                float flick = 0.85 + 0.15 * sin(_CS_Time * 2.3) * sin(_CS_Time * 0.71 + 1.3);
                float3 c = lerp(_EdgeColor.rgb, _CoreColor.rgb, pow(nv, 2.5)) * flick * _Glow;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
