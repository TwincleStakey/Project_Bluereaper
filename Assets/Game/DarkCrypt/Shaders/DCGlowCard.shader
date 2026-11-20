// DarkCrypt glow card: an additive, unlit soft glow on a quad (UV 0..1), brightest low in the middle and fading to
// nothing at every edge. Stands behind the iron gate so its bars read as dark lines against a violet/candle backlight
// instead of a black blob. Costs no realtime light. Fogged (fades to black with the fog, additive-safe).
Shader "DarkCrypt/GlowCard"
{
    Properties
    {
        _Color ("Glow (HDR, linear)", Color) = (0.35, 0.12, 0.6, 1)
        _Low ("Glow height centre (0 bottom .. 1 top)", Range(0, 1)) = 0.25
        _Falloff ("Falloff power", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color; float _Low, _Falloff;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v);
                o.pos = TransformObjectToHClip(v.pos.xyz); o.uv = v.uv; o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 d = float2((i.uv.x - 0.5) * 2, (i.uv.y - _Low) / (i.uv.y < _Low ? _Low : 1 - _Low));
                float g = pow(saturate(1 - length(d)), _Falloff);
                float3 c = _Color.rgb * g * saturate(ComputeFogIntensity(i.fog));
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
