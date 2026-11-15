// 11-15 (Kido: "use the original"): verbatim copy of the ORIGINAL DarkCrypt v2 MistOverlay (recovered from the 11-14
// session log), for the 저승 film. Colours, edge and rim are unchanged. The only additions (marked "UW") put it on the
// floor only: ZTest Always, the scene surface from camera depth, drawn where that surface is up-facing floor near the
// quad height (not over the lava sea, walls, props or bodies). Global names are _UW_Film* (UWCorruptFilm.cs).
// DarkCrypt corruption mist, drawn on a flat quad just above the courtyard stones (transparent, premultiplied).
// Slowly drifting violet-black mist (two fbm layers moving against each other, darker veins) covers the floor; where
// DCMist's mask says the player has walked it is gone, with a noisy paint-like edge and a faint lilac rim. Cleared stone
// gets a tiny lift so the walked path reads in the dark. Fades out toward the arena border (walls stand there).
Shader "Underworld/CorruptFilm"
{
    Properties
    {
        _Deep ("Deep colour (linear)", Color) = (0.010, 0.002, 0.016, 1)
        _Mid ("Mist colour (linear)", Color) = (0.085, 0.012, 0.13, 1)
        _Glow ("Bright wisps (HDR)", Color) = (0.30, 0.05, 0.55, 1)
        _Rim ("Clear edge rim (HDR)", Color) = (0.42, 0.18, 0.62, 1)
        _Alpha ("Opacity", Range(0, 1)) = 0.86
        _Scale ("Mist scale (1/m)", Float) = 0.07
        _Drift ("Drift speed", Float) = 0.6
        _EdgeScale ("Clear edge noise scale (1/m)", Float) = 0.55
        _ClearLift ("Cleared floor lift (linear)", Color) = (0.010, 0.008, 0.014, 1)
        _BorderFade ("Border fade (m)", Float) = 3
        _CrackTex ("UW: crack height map (Poly Haven cracked_concrete disp)", 2D) = "gray" {}
        _CrackScale ("UW: crack map metres per tile", Float) = 4.5
        _CrackAmt ("UW: how far cracks break the edge", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Mist"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always   // UW
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"   // UW

            CBUFFER_START(UnityPerMaterial)
            float4 _Deep, _Mid, _Glow, _Rim, _ClearLift;
            float _Alpha, _Scale, _Drift, _EdgeScale, _BorderFade, _CrackScale, _CrackAmt;   // UW: crack params
            CBUFFER_END
            TEXTURE2D(_UW_FilmMask); SAMPLER(sampler_UW_FilmMask);
            float4 _UW_FilmRect;
            TEXTURE2D(_CrackTex); SAMPLER(sampler_CrackTex);   // UW

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return vnoise(p) * 0.5 + vnoise(p * 2.07 + 5.2) * 0.28 + vnoise(p * 4.3 + 1.7) * 0.14 + vnoise(p * 8.9 + 9.1) * 0.08; }

            V vert(A v)
            {
                V o;
                o.w = TransformObjectToWorld(v.pos.xyz);
                o.pos = TransformWorldToHClip(o.w);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                // UW: lie on the scene floor (camera depth -> world), only on up-facing floor within -1.5 .. +1.0 m of the quad
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                #if UNITY_REVERSED_Z
                float rawD = SampleSceneDepth(suv);
                #else
                float rawD = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(suv));
                #endif
                float3 sp = ComputeWorldSpacePosition(suv, rawD, UNITY_MATRIX_I_VP);
                float dy = sp.y - i.w.y;
                float3 sn = normalize(cross(ddy(sp), ddx(sp)));
                float surf = smoothstep(-1.5, -1.275, dy) * (1 - smoothstep(0.85, 1.0, dy)) * smoothstep(0.6, 0.85, abs(sn.y));
                if (surf <= 0.001) return 0;
                float2 w = sp.xz;
                float t = _Time.y * _Drift;
                // two layers drifting against each other, domain-warped so it rolls instead of sliding
                float2 q = w * _Scale;
                float warp = fbm(q * 1.7 + float2(t * 0.05, -t * 0.03));
                float f1 = fbm(q + float2(t * 0.04, t * 0.015) + warp * 0.8);
                float f2 = fbm(q * 2.3 - float2(t * 0.03, t * 0.05) + 13.0);
                float f = saturate(f1 * 0.7 + f2 * 0.45 - 0.1);
                float veins = smoothstep(0.47, 0.5, f2) * (1 - smoothstep(0.5, 0.53, f2));   // thin darker threads

                float3 col = lerp(_Deep.rgb, _Mid.rgb, smoothstep(0.25, 0.75, f));
                col += _Glow.rgb * pow(saturate((f - 0.58) / 0.42), 2.2) * 0.6;
                col *= 1 - veins * 0.55;
                float a = _Alpha * (0.78 + 0.22 * f);

                // cleared by the player (mask) with a noisy, paint-like edge
                float2 muv = (w - _UW_FilmRect.xy) / max(_UW_FilmRect.zw, 1e-3);
                float m = 0;
                if (all(muv > 0) && all(muv < 1)) m = SAMPLE_TEXTURE2D(_UW_FilmMask, sampler_UW_FilmMask, muv).r;
                float n = 0.22 + 0.55 * fbm(w * _EdgeScale + 31.0);
                // UW (Kido 11-15: a broken, chipped edge like cracked floor): the film near a hole gives way along the cracks of a
                // scanned crack height map first, so the rim breaks into plates and fragments; the outline follows the cracks
                float ch = SAMPLE_TEXTURE2D(_CrackTex, sampler_CrackTex, w / _CrackScale).r;
                float crack = saturate((0.44 - pow(ch, 0.4545)) * 9.0);   // the map imports as sRGB: back to its stored values
                float plate = fbm(w * 1.9 + 7.0);   // some plates go before others (fragments left standing)
                n = n - (crack * _CrackAmt + (plate - 0.5) * _CrackAmt * 0.5) * saturate(m * 5.0);   // only where the player has been
                float clear = smoothstep(n - 0.05, n + 0.05, m);
                float rim = saturate(1 - abs(m - (n - 0.08)) / 0.06) * (1 - clear) * step(0.02, m);

                // fade to nothing over the last metres before the arena border (the walls stand there)
                float2 hx = _UW_FilmRect.zw * 0.5 - 2.0;
                float2 c = _UW_FilmRect.xy + _UW_FilmRect.zw * 0.5;
                float2 e = hx - abs(w - c);
                float border = saturate(min(e.x, e.y) / max(_BorderFade, 0.01));
                border *= surf;   // UW

                float alpha = a * (1 - clear) * border;
                float3 rgb = col * alpha + _Rim.rgb * rim * 0.35 * border + _ClearLift.rgb * clear * border;
                // fog: premultiplied, so the fog colour is weighted by coverage
                rgb = lerp(unity_FogColor.rgb * alpha, rgb, saturate(ComputeFogIntensity(i.fog)));
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
