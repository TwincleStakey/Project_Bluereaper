// DarkCrypt corruption mist, drawn on a flat quad just above the courtyard stones (transparent, premultiplied).
// v3 (11-15): a semi-transparent deep-violet haze, never a sheet. Its density follows large soft noise (~20 % thin
// patches .. ~70 % dense banks, tens of metres across) that drifts slowly, so the stone tiles read through everywhere.
// Where DCMist's mask says the player has walked the haze is gone, with a wide feathered, blotchy edge (low-frequency
// noise, no outline ring, no flecks) and at most a faint violet glow; the cleared stone gets a small grey-violet lift.
// 11-15 shared use (저승): with _SURFACE on (UW_CorruptMist) the quad draws with ZTest Always and puts the mist on the
// scene surface it covers (camera depth -> world position), only where that surface lies within a thin band around the
// quad height (the walkable floor): never over the lava sea below the plaza, walls, props or the upper body of
// characters; it also thins out over bright warm pixels of the opaque colour (lava rivers glow through).
// DarkCrypt keeps the keyword off and looks exactly as before.
Shader "DarkCrypt/MistOverlay"
{
    Properties
    {
        _Deep ("Thin haze colour (linear)", Color) = (0.035, 0.012, 0.06, 1)
        _Mid ("Dense haze colour (linear)", Color) = (0.085, 0.03, 0.14, 1)
        _Glow ("Soft highlight in the dense banks (linear)", Color) = (0.07, 0.025, 0.12, 1)
        _Rim ("Clear edge glow (linear)", Color) = (0.05, 0.02, 0.09, 1)
        _MinDensity ("Thinnest haze", Range(0, 1)) = 0.2
        _MaxDensity ("Densest haze", Range(0, 1)) = 0.7
        _Scale ("Density noise scale (1/m)", Float) = 0.035
        _Drift ("Drift speed", Float) = 0.5
        _EdgeScale ("Clear edge blotch scale (1/m)", Float) = 0.22
        _Feather ("Clear edge feather (mask units)", Float) = 0.28
        _ClearLift ("Cleared floor lift (linear)", Color) = (0.022, 0.02, 0.028, 1)
        _BorderFade ("Border fade (m)", Float) = 3
        _NoiseOffset ("Density noise offset (m, xz)", Vector) = (0, 0, 0, 0)
        [Toggle(_SURFACE)] _Surface ("Lie on the scene floor (depth band + glow mask)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _BandBelow ("Floor band below the quad (m)", Float) = 0.6
        _BandAbove ("Floor band above the quad (m)", Float) = 0.7
        _GlowCut ("Glow mask: warm brightness start, full", Vector) = (0.05, 0.25, 0, 0)
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
            ZTest [_ZTest]
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _SURFACE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(_SURFACE)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #endif

            CBUFFER_START(UnityPerMaterial)
            float4 _Deep, _Mid, _Glow, _Rim, _ClearLift, _NoiseOffset;
            float _MinDensity, _MaxDensity, _Scale, _Drift, _EdgeScale, _Feather, _BorderFade, _BandBelow, _BandAbove, _Surface, _ZTest;
            float4 _GlowCut;
            CBUFFER_END
            TEXTURE2D(_DC_MistMask); SAMPLER(sampler_DC_MistMask);
            float4 _DC_MistRect;

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            // soft fbm: three octaves only, so there are no fine speckles or contour lines
            float fbm(float2 p) { return vnoise(p) * 0.55 + vnoise(p * 1.93 + 5.2) * 0.3 + vnoise(p * 3.7 + 1.7) * 0.15; }

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
                float2 w = i.w.xz;
                float surf = 1;
            #if defined(_SURFACE)
                float2 suv = GetNormalizedScreenSpaceUV(i.pos);
                #if UNITY_REVERSED_Z
                float rawD = SampleSceneDepth(suv);
                #else
                float rawD = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(suv));
                #endif
                float3 sp = ComputeWorldSpacePosition(suv, rawD, UNITY_MATRIX_I_VP);
                float dy = sp.y - i.w.y;
                // floor band: fades out below (plaza lip / lava sea) and above (walls, props, bodies)
                surf = smoothstep(-_BandBelow, -_BandBelow * 0.4, dy) * (1 - smoothstep(_BandAbove * 0.3, _BandAbove, dy));
                // lava / fire on the floor: warm bright opaque pixels show through
                float3 oc = SampleSceneColor(suv);
                float warm = max(oc.r - oc.b * 1.3, 0) + max(oc.r + oc.g - 0.6, 0) * 0.5;
                surf *= 1 - smoothstep(_GlowCut.x, _GlowCut.y, warm);
                w = sp.xz;
                if (surf <= 0.001) return 0;
            #endif
                float t = _Time.y * _Drift;
                // large banks: domain-warped, drifting very slowly
                float2 q = (w + _NoiseOffset.xy) * _Scale;
                float warp = fbm(q * 0.8 + float2(t * 0.02, -t * 0.015));
                float banks = fbm(q + float2(t * 0.025, t * 0.01) + warp * 0.9);
                // a finer wisp layer moving the other way, small amplitude (keeps it alive, never noisy)
                float wisps = fbm(q * 3.1 - float2(t * 0.04, t * 0.03) + 13.0);
                // v4: more contrast between thin (~25 %) and dense (~85 %) clumps
                float d = saturate(smoothstep(0.28, 0.66, banks) + (wisps - 0.5) * 0.3);
                // inside the clumps: drifting swirls (alpha and colour), so a dense bank is never a flat painted sheet
                float sw = fbm(q * 5.3 + warp * 2.0 + float2(-t * 0.06, t * 0.05) + 3.0);
                float density = lerp(_MinDensity, _MaxDensity, d) * (0.84 + 0.32 * sw * d);
                density = min(density, 0.92);

                float3 col = lerp(_Deep.rgb, _Mid.rgb, d) * (0.75 + 0.5 * sw) + _Glow.rgb * smoothstep(0.55, 0.85, sw) * d * 0.8;

                // cleared by the player (mask): wide feathered blotchy edge
                float2 muv = (w - _DC_MistRect.xy) / max(_DC_MistRect.zw, 1e-3);
                float m = 0;
                if (all(muv > 0) && all(muv < 1)) m = SAMPLE_TEXTURE2D(_DC_MistMask, sampler_DC_MistMask, muv).r;
                float n = 0.3 + 0.35 * fbm(w * _EdgeScale + 31.0);
                float clear = smoothstep(n - _Feather * 0.5, n + _Feather * 0.5, m);
                float edge = clear * (1 - clear) * 4;   // 1 in the middle of the feather, 0 either side

                // fade over the last metres before the arena border (the walls stand there)
                float2 hx = _DC_MistRect.zw * 0.5 - 2.0;
                float2 c = _DC_MistRect.xy + _DC_MistRect.zw * 0.5;
                float2 e = hx - abs(w - c);
                float border = saturate(min(e.x, e.y) / max(_BorderFade, 0.01));

                border *= surf;
                float alpha = density * (1 - clear) * border;
                float3 rgb = col * alpha + _Rim.rgb * edge * border + _ClearLift.rgb * clear * border;
                rgb = lerp(unity_FogColor.rgb * alpha, rgb, saturate(ComputeFogIntensity(i.fog)));
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
