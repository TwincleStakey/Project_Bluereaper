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
        _RiverZ("River near / far bank z (world)", Vector) = (-22, -38, 0, 0)
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

            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_PlanarReflectionTexture); SAMPLER(sampler_PlanarReflectionTexture);
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
                return _Flow * lerp(0.25, 1.0, prof) * lerp(0.85, 1.15, RFNoise(xz * 0.05 + 4.0));
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
                // the texture moves with the water: sample at (position - offset)
                float2 uvA = xz / _SurfaceScale, uvB = float2(xz.x * 0.6, xz.y) / (_SurfaceScale * 0.55) + 0.37;
                float3 nA0 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uvA - off0 / _SurfaceScale));
                float3 nA1 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uvA - off1 / _SurfaceScale + 0.5));
                float3 nB0 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uvB - off0 / (_SurfaceScale * 0.55)));
                float3 nB1 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uvB - off1 / (_SurfaceScale * 0.55) + 0.5));
                float2 nxy = lerp(nA0.xy + nB0.xy * 0.7, nA1.xy + nB1.xy * 0.7, wgt);
                // choppier where it runs fast
                nxy *= _NormalStrength * lerp(0.6, 1.3, saturate(spd / max(_Flow, 0.01)));
                float3 n = normalize(float3(nxy.x, 1, nxy.y));

                // foam streaks: long thin lines along the current, broken up and carried west with it
                float2 fs = float2(0.12, 1.6);   // stretched along x
                float f0 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, (xz - off0) * fs / 2.2).r;
                float f1 = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, (xz - off1) * fs / 2.2 + 0.5).r;
                float ft = lerp(f0, f1, wgt);
                float m0 = RFNoise(float2((xz.x - off0.x) * 0.07, xz.y * 0.55));
                float m1 = RFNoise(float2((xz.x - off1.x) * 0.07, xz.y * 0.55) + 0.5);
                float mask = smoothstep(0.55, 0.8, lerp(m0, m1, wgt)) * saturate(spd / max(_Flow, 0.01));
                float foam = saturate(ft * 1.6 - 0.35) * mask * _Streaks;

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
                    foam = max(foam, saturate(collar * churn * 0.9 + wake * churn * 0.8));
                    // eddies: water curls in behind the rock
                    rockN += float2(-d.y, d.x) / max(dist * dist, 0.3) * wake * 0.6;
                }
                n = normalize(n + float3(rockN.x, 0, rockN.y) * 0.3);

                Light ml = GetMainLight();
                float fres = pow(1 - saturate(dot(n, v)), 4);
                float murk = smoothstep(0.05, _MurkDepth, depth);
                float3 col = lerp(_ShallowCol.rgb, _DeepCol.rgb, murk) * (0.35 + 0.65 * ml.color);
                float2 ruv = float2(suv.x + n.x * 0.06, 1 - saturate(0.5 + n.z * 0.3 + fres * 0.5));
                float3 sky = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, ruv).rgb;
                float refl = saturate(_Reflection * (0.2 + fres * 1.5));
                col = lerp(col, sky, refl);
                float3 r = reflect(-v, n);
                col += ml.color * pow(saturate(dot(r, ml.direction)), 160) * _Glint;
                col += ml.color * pow(saturate(dot(r, ml.direction)), 20) * 0.06 * _Glint;


                col = lerp(col, _FoamCol.rgb * (0.35 + 0.65 * ml.color), saturate(foam));

                // see-through at the bank, opaque over the deep channel; edge broken by noise
                float edgeN = RFNoise(xz * 0.9 + 3.1) * 0.12;
                float alpha = lerp(0.35, 1.0, murk) * smoothstep(0.02 + edgeN, 0.45 + edgeN, depth);
                alpha = max(alpha, saturate(foam) * 0.85 * smoothstep(0.0, 0.1, depth));
                col = RFFog(col, i.ws + float3(0, 2, 0));
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
