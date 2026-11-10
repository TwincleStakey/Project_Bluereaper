// Fake shadows for monsters (they cast no realtime shadows): a soft blob under the feet plus, near a lit lantern,
// a quad stretched away from that lantern (the field script orients and scales the instanced quads; this shader
// only draws the falloff). Multiply blend, instanced. UV (0.5, 0) = the feet; v grows away from the light.
Shader "SnowPlague/BlobShadow"
{
    Properties
    {
        _Strength("Darkness", Range(0, 1)) = 0.55
        _Col("Shadow tint", Color) = (0.32, 0.38, 0.46, 1)
        [Toggle] _Stretch("Stretched (lantern) shadow", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-200" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Blob"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Col;
                float _Strength, _Stretch;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a) { V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o); o.positionCS = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float d;
                if (_Stretch > 0.5) { float2 p = float2((i.uv.x - 0.5) * 2, i.uv.y); d = saturate(length(float2(p.x * (1 + p.y), p.y))); d = d * d * (0.6 + 0.4 * p.y); }
                else { float2 p = (i.uv - 0.5) * 2; d = saturate(dot(p, p)); }
                float s = (1 - d) * _Strength;
                return half4(lerp(float3(1, 1, 1), _Col.rgb, s), 1);
            }
            ENDHLSL
        }
    }
}
