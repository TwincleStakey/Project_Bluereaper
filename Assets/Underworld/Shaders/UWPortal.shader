// Swirling vortex on a quad: polar UV twisted by radius and time, noise rings, bright rim, dark eye.
Shader "Underworld/Portal"
{
    Properties
    {
        _NoiseTex("Noise", 2D) = "grey" {}
        [HDR] _Core("Core colour", Color) = (3.2, 1.6, 5.5, 1)
        [HDR] _Mid("Mid colour", Color) = (1.2, 0.35, 2.6, 1)
        _Edge("Edge colour", Color) = (0.16, 0.06, 0.29, 1)
        _Twist("Twist", Float) = 5
        _Spin("Spin speed", Float) = 0.35
        _Pull("Inward speed", Float) = 0.35
        _Eye("Dark eye radius", Range(0,0.3)) = 0.09
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Core, _Mid, _Edge;
            float _Twist, _Spin, _Pull, _Eye;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.cs = TransformObjectToHClip(a.p.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float r = length(c) * 2.0;
                float ang = atan2(c.y, c.x);
                float t = _UWTime;
                float sw = ang + _Twist * (1.0 - r) + t * _Spin;
                float2 puv = float2(sw / 6.2831853 * 3.0, r * 1.6 + t * _Pull);
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, puv).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, puv * float2(2.0, 0.7) + 0.37).r;
                float bands = pow(saturate(n1 * 0.7 + n2 * 0.5), 2.2);
                // streak arms
                float arms = pow(saturate(0.5 + 0.5 * sin(sw * 4.0 + n1 * 3.0)), 6.0);
                float disk = smoothstep(1.0, 0.75, r);
                float eye = smoothstep(_Eye, _Eye + 0.12, r);
                float rim = smoothstep(0.55, 0.85, r) * smoothstep(1.0, 0.86, r);
                float3 col = lerp(_Core.rgb, _Mid.rgb, saturate(r * 1.4));
                col = lerp(col, _Edge.rgb, smoothstep(0.6, 1.0, r));
                float intensity = (bands * 1.4 + arms * 0.7) * disk * eye + rim * 0.6 * n1;
                float3 rgb = col * intensity;
                float alpha = disk * saturate(0.55 + bands) * 0.95;
                rgb *= 1.0; // premultiplied
                return half4(rgb, alpha * (1.0 - eye * 0.0) * (0.35 + 0.65 * eye));
            }
            ENDHLSL
        }
    }
}
