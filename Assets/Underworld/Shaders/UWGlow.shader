// Small FX shaders for the underworld stage.
// Underworld/Glow     : camera-facing billboard (orbs, lantern halos): hot core, soft halo, 4-point flare.
// Underworld/Emit     : unlit HDR emissive mesh (lantern fireboxes, paper lanterns) with flicker.
// Underworld/FogCard  : soft scrolling fog sheet with depth fade and camera fade.
// Underworld/Particle : particle sprite, additive or alpha via _Src/_Dst, soft-particle depth fade.
Shader "Underworld/Glow"
{
    Properties
    {
        [HDR] _Core("Core", Color) = (6, 4.5, 8, 1)
        [HDR] _Halo("Halo", Color) = (1.2, 0.45, 2.6, 1)
        _Size("Size (m)", Float) = 1.4
        _Flare("Flare", Range(0,1)) = 0.6
        _Phase("Phase", Float) = 0
        _Bob("Bob (m)", Float) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Core, _Halo; float _Size, _Flare, _Phase, _Bob;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                V o;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                center.y += sin(_UWTime * 1.2 + _Phase) * _Bob;
                float3 vc = TransformWorldToView(center);
                float pulse = 1.0 + 0.08 * sin(_UWTime * 3.1 + _Phase * 2.0);
                vc.xy += (a.uv - 0.5) * _Size * pulse;
                o.cs = TransformWViewToHClip(vc);
                o.uv = a.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 c = (i.uv - 0.5) * 2.0;
                float r = length(c);
                float core = exp(-r * r * 90.0);
                float halo = exp(-r * r * 9.0) * 0.6 + exp(-r * 3.2) * 0.25;
                float rot = _UWTime * 0.4 + _Phase;
                float2 rc = float2(c.x * cos(rot) - c.y * sin(rot), c.x * sin(rot) + c.y * cos(rot));
                float flare = (exp(-abs(rc.x) * 40.0) + exp(-abs(rc.y) * 40.0)) * exp(-r * 3.0) * _Flare;
                float3 col = _Core.rgb * (core + flare * 0.5) + _Halo.rgb * (halo + flare * 0.5);
                col *= smoothstep(1.0, 0.8, r);
                return half4(col, 0);
            }
            ENDHLSL
        }
    }
}
