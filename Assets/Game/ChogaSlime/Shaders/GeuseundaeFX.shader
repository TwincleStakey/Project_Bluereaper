// 그슨대 pattern FX, one unlit additive shader for every mark the previs draws (immediate-mode, colour per renderer).
// _Mode: 6 trail segment (no end fade); 0 telegraph disc (faint fill + bright rim + inner pulse), 1 soft glow disc, 2 telegraph rectangle,
//        3 crack/streak line (bright core, ragged edge), 4 glowing orb (sphere: hot core, fresnel halo), 5 beam/cone (soft).
// Quads are Unity's default quad (uv 0..1). _Fill = progress for telegraphs (0..1 fill sweep), _Pulse = flash amount.
Shader "BulletHeaven/GeuseundaeFX"
{
    Properties
    {
        _Color ("Color", Color) = (2, 0.1, 0.05, 1)
        _Mode ("Mode", Float) = 0
        _Fill ("Fill", Float) = 1
        _Pulse ("Pulse", Float) = 0
        _Seed ("Seed", Float) = 0
        _Alpha ("Alpha Blend (dust/debris)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcB ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstB ("Dst", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcB] [_DstB] ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Mode; float _Fill; float _Pulse; float _Seed; float _Alpha; CBUFFER_END
            float _CS_Time;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 nWS : TEXCOORD1; float3 pWS : TEXCOORD2; };
            V vert(A i) { V o; o.pWS = TransformObjectToWorld(i.positionOS.xyz); o.nWS = TransformObjectToWorldNormal(i.normalOS); o.positionCS = TransformWorldToHClip(o.pWS); o.uv = i.uv; return o; }
            float h11(float x) { return frac(sin(x * 127.1 + _Seed * 311.7) * 43758.5453); }
            float n11(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(h11(i), h11(i + 1), f); }
            half4 frag(V i) : SV_Target
            {
                float2 c = i.uv * 2 - 1; float r = length(c);
                float a = 0; float t = _CS_Time;
                int m = (int)round(_Mode);
                if (m == 0)
                {
                    float rim = smoothstep(0.86, 0.96, r) * (1 - smoothstep(0.96, 1.0, r));
                    float fill = (1 - smoothstep(0.95, 1.0, r)) * 0.22;
                    float grow = (1 - smoothstep(_Fill - 0.02, _Fill, r)) * (1 - smoothstep(0.95, 1.0, r)) * 0.35;
                    a = rim * 1.2 + fill + grow + _Pulse * (1 - smoothstep(0.9, 1.0, r));
                }
                else if (m == 1) { a = pow(saturate(1 - r), 2.2); }
                else if (m == 2)
                {
                    float2 d = abs(c); float e = max(d.x, d.y);
                    float edge = smoothstep(0.9, 0.97, e) * (1 - smoothstep(0.97, 1.0, e));
                    float grow = step(i.uv.x, _Fill) * 0.3;
                    a = (1 - step(1.0, e)) * (0.18 + grow) + edge * 1.2 + _Pulse * 0.6;
                }
                else if (m == 3)
                {
                    // along x = length, y = width; ragged width from noise, fades at the ends
                    float w = 0.35 + 0.65 * n11(i.uv.x * 14.0);
                    float core = 1 - smoothstep(0.0, w, abs(c.y));
                    float ends = smoothstep(0.0, 0.06, i.uv.x) * (1 - smoothstep(_Fill - 0.04, _Fill, i.uv.x));
                    a = pow(core, 1.5) * ends * (1 + _Pulse);
                }
                else if (m == 7)
                {   // smoke puff: soft disc with a lumpy edge
                    float ang = atan2(c.y, c.x);
                    float edge = 0.7 + 0.3 * n11(ang * 2.2 + 10.0);
                    a = pow(saturate(1 - r / edge), 1.4) * (0.6 + 0.4 * n11(r * 6 + ang));
                }
                else if (m == 8)
                {   // solid chunk (sphere): lit from above, no glow
                    float up = saturate(normalize(i.nWS).y * 0.5 + 0.5);
                    return half4(_Color.rgb * (0.35 + 0.65 * up), _Color.a);
                }
                else if (m == 6)
                {   // chained trail segment: like 3 but no end fade, so neighbours join without stripes
                    float w = 0.45 + 0.55 * n11(i.uv.x * 6.0 + _Seed);
                    a = pow(1 - smoothstep(0.0, w, abs(c.y)), 1.5) * (1 + _Pulse);
                }
                else if (m == 4)
                {
                    float nv = saturate(dot(normalize(i.nWS), normalize(GetWorldSpaceViewDir(i.pWS))));
                    a = pow(nv, 3) * 1.5 + pow(1 - nv, 2) * 0.35 + _Pulse;
                }
                else
                {
                    float side = 1 - smoothstep(0.55, 1.0, abs(c.x));
                    a = side * (0.25 + 0.15 * sin(t * 9 + i.uv.y * 20)) * saturate(i.uv.y * 3);
                }
                if (_Alpha > 0.5) return half4(_Color.rgb, saturate(a) * _Color.a);
                return half4(_Color.rgb * a * _Color.a, 1);
            }
            ENDHLSL
        }
    }
}
