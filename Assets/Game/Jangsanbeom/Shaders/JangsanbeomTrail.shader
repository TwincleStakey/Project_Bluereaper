// 장산범 claw trail: additive HDR red ribbon (vertex colour carries the fade), so bloom makes it read in the dark.
Shader "BulletHeaven/JangsanbeomTrail"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (6, 0.25, 0.1, 1)
        _Softness ("Edge Softness", Range(0.01, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Name "Trail"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Softness;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; o.color = i.color; return o; }
            half4 frag(V i) : SV_Target
            {
                half edge = 1 - abs(i.uv.y * 2 - 1);                 // bright core, soft sides
                half core = smoothstep(0, _Softness, edge);
                return half4(_Color.rgb * i.color.rgb * i.color.a * core, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
