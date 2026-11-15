// The deep, fast river along the south edge of the reed field (Kido 11-10: "물이 빨리 흐르는 깊은 강").
//   - flows west (-x); the current is fastest mid-stream and slow at the banks (flow-map style: two phases of the
//     scrolled normal/foam layers cross-faded, so the speed can vary across the river without the texture shearing);
//   - colour from real water depth (scene depth): see-through at the bank, dark and opaque over the deep channel;
//   - foam streaks carried by the current, whitecaps around rocks (_RF_Rocks) with a wake trailing downstream;
//   - sky reflection (_PlanarReflectionTexture stand-in from RFWater) by fresnel and sun glints.
// Textures: URP_Water WaterSurface_single / WaterFoam (ChiliMilk, MIT), the same ones the shallow water uses.
Shader "ReedField/River"
{
    Properties
    {
        _SurfaceMap("Surface normal (URP_Water WaterSurface_single)", 2D) = "bump" {}
        _FoamTex("Foam (URP_Water WaterFoam)", 2D) = "black" {}
        _ShallowCol("Shallow tint", Color) = (0.26, 0.20, 0.12, 1)
        _DeepCol("Deep tint", Color) = (0.025, 0.035, 0.04, 1)
        _MurkDepth("Opaque at depth (m)", Float) = 1.4
        _FoamCol("Foam colour", Color) = (0.95, 0.88, 0.76, 1)
        _SurfaceScale("Surface tiling (m)", Float) = 4.0
        _NormalStrength("Normal strength", Range(0, 2)) = 0.9
        _Flow("Current speed mid-stream (m/s)", Float) = 2.4
        _Streaks("Foam streaks", Range(0, 1)) = 0.55
        _Reflection("Reflection", Range(0, 1)) = 0.45
        _Glint("Sun glint", Range(0, 4)) = 1.4
        _RiverZ("River near / far bank z, mesh near / far edge z (world)", Vector) = (-22, -38, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-99" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "RFWaterSurface.hlsl"

            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_PlanarReflectionTexture); SAMPLER(sampler_PlanarReflectionTexture);
            TEXTURE2D(_WaterFXTexture); SAMPLER(sampler_WaterFXTexture);
            float4 _RF_Rocks[6];   // xz: rock centre, z: radius (0 = none); set by RFRiver
            CBUFFER_START(UnityPerMaterial)
                float4 _SurfaceMap_ST, _FoamTex_ST;
                float4 _ShallowCol, _DeepCol, _FoamCol, _RiverZ;
                float _MurkDepth, _SurfaceScale, _NormalStrength, _Flow, _Streaks, _Reflection, _Glint;
            CBUFFER_END

            struct Attr { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float4 screen : TEXCOORD1; };

            V2F vert(Attr a)
            {
                V2F o;
                o.ws = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            // 0 at the near bank, 1 at the far bank
            float Across(float z) { return saturate((_RiverZ.x - z) / max(_RiverZ.x - _RiverZ.y, 0.01)); }
            // current speed: slow at the banks, fastest mid-stream, a little uneven along the river
            float Speed(float2 xz)
            {
                float t = Across(xz.y);
                float prof = pow(saturate(sin(t * PI)), 0.6);
                // 11-14: the river surface now also covers the wading shallows inside the arena: nearly still there
                float raw = (_RiverZ.x - xz.y) / max(_RiverZ.x - _RiverZ.y, 0.01);
                float shallowCalm = lerp(0.12, 1.0, smoothstep(-0.3, 0.02, raw));
                return _Flow * lerp(0.25, 1.0, prof) * shallowCalm * lerp(0.85, 1.15, RFNoise(xz * 0.05 + 4.0));
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 suv = i.screen.xy / i.screen.w;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float depth = max(sceneEye - i.screen.w, 0) * saturate(v.y + 0.05);

                float2 xz = i.ws.xz;
                float spd = Speed(xz);
                // flow map phases (period 2 s): the layers slide west and cross-fade so speed can vary in space
                const float period = 2.0;
                float ph0 = frac(_RF_Time / period), ph1 = frac(_RF_Time / period + 0.5);
                float wgt = abs(1 - 2 * ph0);
                float2 off0 = float2(-spd * ph0 * period, 0), off1 = float2(-spd * ph1 * period, 0);
                // 11-14: non-repeating surface (RFWaterSurface.hlsl) instead of one normal map tiled every 4 m
                float2 s0 = RFWaterSlope(TEXTURE2D_ARGS(_SurfaceMap, sampler_SurfaceMap), xz, off0, _SurfaceScale, _RF_Time);
                float2 s1 = RFWaterSlope(TEXTURE2D_ARGS(_SurfaceMap, sampler_SurfaceMap), xz + 0.5, off1, _SurfaceScale, _RF_Time);
                float2 nxy = lerp(s0, s1, wgt);
                // choppier where it runs fast, calm and ruffled patches, calmer far from the camera
                float patch = RFWaterPatch(xz, _RF_Time);
                nxy *= _NormalStrength * lerp(0.55, 1.15, saturate(spd / max(_Flow, 0.01))) * lerp(0.35, 1.0, patch) * RFWaterNear(i.screen.w);
                float3 n = normalize(float3(nxy.x, 1, nxy.y));

                // 11-14: no foam streaks on the open water any more (they read as clumsy clumps of white lines); only a
                // faint darker-than-before churn around the rocks below
                float2 fs = float2(0.12, 1.6);
                float f0 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, (xz - off0) * fs / 2.2).r;
                float f1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, (xz - off1) * fs / 2.2 + 0.5).r;
                float ft = lerp(f0, f1, wgt);
                float foam = 0;

                // rocks: a white collar around each and a wake trailing downstream (west)
                float2 rockN = 0;
                [unroll] for (int k = 0; k < 6; k++)
                {
                    float4 rk = _RF_Rocks[k];
                    if (rk.z <= 0) continue;
                    float2 d = xz - rk.xy;
                    float dist = length(d);
                    // a thin broken collar hugging the rock, not a disc
                    float collar = smoothstep(rk.z * 0.55, rk.z * 0.85, dist) * (1 - smoothstep(rk.z * 0.85, rk.z + 0.45, dist));
                    float along = -d.x;                                   // > 0 downstream of the rock
                    float spread = rk.z * 0.5 + max(along, 0) * 0.18 + 0.15;
                    // the wake: two churned lines peeling off the sides, broken up and fading within a few metres
                    float side = smoothstep(spread * 0.35, spread * 0.75, abs(d.y)) * (1 - smoothstep(spread * 0.75, spread, abs(d.y)));
                    float brk = smoothstep(0.45, 0.8, RFNoise(float2((xz.x - lerp(off0.x, off1.x, wgt)) * 0.9, xz.y * 2.2)));
                    float wake = step(0, along) * exp(-along / (1.6 + rk.z)) * side * brk;
                    float churn = saturate(ft * 1.5 - 0.1);
                    foam = max(foam, saturate(collar * churn * 0.9 + wake * churn * 0.8) * _Streaks);
                    // eddies: water curls in behind the rock
                    rockN += float2(-d.y, d.x) / max(dist * dist, 0.3) * wake * 0.6;
                }
                n = normalize(n + float3(rockN.x, 0, rockN.y) * 0.3);
                // wading ripples from the WaterFX buffer (same as the shallow water; the river now covers the shallows too)
                float4 fx = SAMPLE_TEXTURE2D(_WaterFXTexture, sampler_WaterFXTexture, suv);
                n = normalize(lerp(n, normalize(n + float3(fx.x, 0, fx.z) * 2.0), saturate(fx.a * 1.4)));

                Light ml = GetMainLight();
                // reflections read a calmer normal than the ripples: the mirrored sky drifts softly instead of breaking
                // into specks; the moon gives one broad dull sheen, no pin-point glints (they read as white clumps)
                float3 nr = normalize(float3(n.x * 0.5, 1, n.z * 0.5));
                float fres = pow(1 - saturate(dot(nr, v)), 4);
                float murk = smoothstep(0.05, _MurkDepth, depth);
                float3 col = lerp(_ShallowCol.rgb, _DeepCol.rgb, murk) * (0.35 + 0.65 * ml.color);
                float2 ruv = float2(suv.x + nr.x * 0.04, 1 - saturate(0.5 + nr.z * 0.2 + fres * 0.5));
                float3 sky = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, ruv).rgb;
                float refl = saturate(_Reflection * (0.15 + fres * 1.3));
                col = lerp(col, sky, refl);
                float3 r = reflect(-v, n);
                col += ml.color * pow(saturate(dot(r, ml.direction)), 28) * 0.035 * _Glint * lerp(0.4, 1.0, patch);
                // slight darkening in the troughs so the ripples read by shading, not by sparkle
                col *= 1 - saturate(-dot(nxy, v.xz)) * 0.25;

                col = lerp(col, _FoamCol.rgb * (0.35 + 0.65 * ml.color), saturate(foam));

                // see-through at the bank, opaque over the deep channel; edge broken by noise
                float edgeN = RFNoise(xz * 0.9 + 3.1) * 0.12;
                float alpha = lerp(0.35, 1.0, murk) * smoothstep(0.02 + edgeN, 0.45 + edgeN, depth);
                alpha = max(alpha, saturate(foam) * 0.85 * smoothstep(0.0, 0.1, depth));
                // 11-14 (Kido: "잘라져 있는거 너무 티나"): never show the mesh edge. _RiverZ.zw = the mesh's near / far z;
                // the surface fades out over the last metres, edge broken by noise
                if (_RiverZ.z != _RiverZ.w)
                {
                    float toEdge = min(_RiverZ.z - xz.y, xz.y - _RiverZ.w) + (RFNoise(xz * 0.35) - 0.5) * 1.5;
                    alpha *= smoothstep(0.0, 3.0, toEdge);
                }
                col = RFFog(col, i.ws + float3(0, 2, 0));
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
