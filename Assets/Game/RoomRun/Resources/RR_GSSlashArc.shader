// Greatsword slash arc (GSSlashArc): a flat crescent on the ground plane, revealed head-first along the arc and then
// eroded by noise. Mesh UV: x = along the arc (0 = where the swing starts, 1 = where it ends), y = across (0 inner, 1 outer).
// Additive, HDR colours so bloom picks up the core. Optional: stacked ribbon bands in the head, a thin dim line that stays
// along the whole swept rim, and a faint haze filling the swept area.
Shader "RoomRun/GSSlashArc"
{
    Properties
    {
        _NoiseTex("Noise", 2D) = "gray" {}
        _StreakTex("Streak noise", 2D) = "gray" {}
        [HDR] _CoreColor("Core", Color) = (4, 4.4, 5, 1)
        [HDR] _GlowColor("Glow", Color) = (0.25, 0.9, 2.2, 1)
        [HDR] _EdgeColor("Leading edge", Color) = (6, 6, 6, 1)
        _Head("Head (0..1+tail)", Float) = 0.5
        _Tail("Tail length", Float) = 0.75
        _Dissolve("Dissolve", Range(0, 1)) = 0
        _Seed("Seed", Float) = 0
        _Bands("Ribbon bands (0 = off)", Float) = 0
        _Rim("Rim position (v)", Float) = 0.86
        _BandStart("Band inner edge at the head (v)", Float) = 0.05
        _TrailLine("Trail line brightness", Float) = 0
        _Haze("Haze", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_StreakTex); SAMPLER(sampler_StreakTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _GlowColor, _EdgeColor;
                float _Head, _Tail, _Dissolve, _Seed, _Bands, _Rim, _BandStart, _TrailLine, _Haze;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }

            float4 frag(V i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float swept = _Head - u;                      // arc length behind the head (u units)
                if (swept < 0) return 0;
                float fade = 1 - _Dissolve;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 2.5 + _Seed, v * 0.8 - _Head * 0.6)).r;
                float3 c = 0;

                // haze and the thin trail line cover everything swept so far
                float sweptMask = smoothstep(0, 0.04, swept) * step(u, 1);
                float rimLine = exp(-pow((v - _Rim) / 0.02, 2));
                c += _GlowColor.rgb * rimLine * _TrailLine * sweptMask * fade * (0.6 + 0.4 * n);
                // haze: brightest just behind the head and toward the rim, fading out around the ring and toward the centre
                float haze = smoothstep(0.15, 0.85, v) * smoothstep(_Rim + 0.04, _Rim - 0.1, v) * exp(-swept * 2.2);
                c += _GlowColor.rgb * haze * _Haze * sweptMask * fade * (0.3 + 0.7 * n);

                float behind = swept / max(_Tail, 1e-3);
                if (behind <= 1 && u <= 1.02)
                {
                    float life = 1 - behind;                     // 1 at the head
                    // the crescent: full width near the head, thinning to a hairline toward the tail, hugging the rim
                    float width = lerp(0.04, _Rim - _BandStart, pow(life, 1.3));
                    float inner = _Rim - width;
                    float across = saturate((v - inner) / max(width, 1e-3));   // 0 inner edge of the band, 1 at the rim
                    float band = smoothstep(0, 0.3, across) * smoothstep(_Rim + 0.12, _Rim + 0.02, v);
                    // streaks running along the swing (stretched noise), scrolling with the head
                    float streak = SAMPLE_TEXTURE2D(_StreakTex, sampler_StreakTex, float2(u * 1.2 - _Head * 0.9 + _Seed, v * 6)).r;
                    // stacked ribbons: parallel curved smears across the band
                    float rib = 1;
                    if (_Bands > 0)
                    {
                        float s = sin(across * 3.14159 * _Bands + n * 1.5);
                        rib = 0.25 + 0.75 * pow(s * s, 2.5);
                    }
                    float erode = saturate(behind * 0.85 + _Dissolve * 1.1);
                    float keep = smoothstep(erode - 0.08, erode + 0.08, n * 0.7 + across * 0.3 + 0.15);
                    float body = band * keep;
                    float rim = exp(-pow((v - _Rim) / 0.035, 2)) * smoothstep(0, 0.25, life);
                    float glow = body * (0.35 + 0.65 * streak) * life * rib;
                    float core = (rim * keep * 0.8 + pow(saturate(across), 10) * body * 0.6 + body * pow(rib, 6) * 0.45 * step(0.01, _Bands)) * life * life;
                    float lead = exp(-pow(behind * 9, 2)) * band;
                    c += (_GlowColor.rgb * glow + _CoreColor.rgb * core + _EdgeColor.rgb * lead * 0.6) * fade;
                }
                return float4(c, 1);
            }
            ENDHLSL
        }
    }
}
