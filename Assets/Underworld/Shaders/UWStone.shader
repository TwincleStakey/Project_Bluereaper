// Underworld stone: URP PBR lighting (Forward+, shadows, SSAO), world-space mapping.
// Up-facing faces get the flagstone texture remapped to violet-grey, wet sheen, dark stains and glowing cracks.
// Other faces get triplanar rock. _TopMode 0 = everything triplanar (props).
Shader "Underworld/Stone"
{
    Properties
    {
        _TopTex("Top albedo (flagstone)", 2D) = "grey" {}
        _TopNrm("Top normal", 2D) = "bump" {}
        _SideTex("Side albedo (rock)", 2D) = "grey" {}
        _SideNrm("Side normal", 2D) = "bump" {}
        _TopTile("Top tile size (m)", Float) = 3.2
        _SideTile("Side tile size (m)", Float) = 3.0
        _TopMode("Use flagstone on top faces", Range(0,1)) = 1
        _Tint("Stone tint", Color) = (0.27, 0.24, 0.31, 1)
        _SideTint("Side tint", Color) = (0.16, 0.14, 0.19, 1)
        _Remap("Remap to tint", Range(0,1)) = 0.85
        _Smooth("Dry smoothness", Range(0,1)) = 0.28
        _WetSmooth("Wet smoothness", Range(0,1)) = 0.78
        _WetAmt("Wet amount", Range(0,1)) = 0.45
        _NrmStr("Normal strength", Range(0,2)) = 1.0
        _StainAmt("Stain amount", Range(0,1)) = 0.45
        _CrackAmt("Glowing cracks", Range(0,1)) = 0.0
        [HDR] _CrackCol("Crack colour", Color) = (2.2, 0.8, 5.0, 1)
        [HDR] _Emission("Emission", Color) = (0,0,0,1)
        _RimCol("Violet rim", Color) = (0.48, 0.24, 0.78, 1)
        _RimAmt("Rim amount", Range(0,1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "UWCommon.hlsl"
        TEXTURE2D(_TopTex); SAMPLER(sampler_TopTex);
        TEXTURE2D(_TopNrm); SAMPLER(sampler_TopNrm);
        TEXTURE2D(_SideTex); SAMPLER(sampler_SideTex);
        TEXTURE2D(_SideNrm); SAMPLER(sampler_SideNrm);
        CBUFFER_START(UnityPerMaterial)
        float _TopTile, _SideTile, _TopMode, _Remap, _Smooth, _WetSmooth, _WetAmt, _NrmStr, _StainAmt, _CrackAmt, _RimAmt;
        float4 _Tint, _SideTint, _CrackCol, _Emission, _RimCol;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 Triplanar(TEXTURE2D_PARAM(tex, smp), float3 p, float3 w, float tile)
            {
                return SAMPLE_TEXTURE2D(tex, smp, p.zy / tile).rgb * w.x + SAMPLE_TEXTURE2D(tex, smp, p.xz / tile).rgb * w.y + SAMPLE_TEXTURE2D(tex, smp, p.xy / tile).rgb * w.z;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float3 w = pow(abs(N), 4.0); w /= (w.x + w.y + w.z);
                float topMask = smoothstep(0.75, 0.9, N.y) * _TopMode;

                // --- side / prop: triplanar rock
                float3 side = Triplanar(TEXTURE2D_ARGS(_SideTex, sampler_SideTex), ws, w, _SideTile);
                float sl = dot(side, float3(0.3, 0.59, 0.11));
                float3 sideAlb = lerp(side, _SideTint.rgb * saturate(sl * 2.4), _Remap);

                // --- top: flagstone
                float2 tuv = ws.xz / _TopTile;
                float3 top = SAMPLE_TEXTURE2D(_TopTex, sampler_TopTex, tuv).rgb;
                float tl = dot(top, float3(0.3, 0.59, 0.11));
                float big = UWFbm(ws.xz * 0.08);
                float3 topAlb = lerp(top, _Tint.rgb * pow(saturate(tl * 2.0), 1.35), _Remap) * lerp(0.7, 1.25, big);
                float grout = saturate(1.0 - tl * 2.2);

                float3 albedo = lerp(sideAlb, topAlb, topMask);

                // normal: flagstone normal on top only
                float3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_TopNrm, sampler_TopNrm, tuv), _NrmStr);
                float3 nTop = normalize(float3(tn.x, tn.z, tn.y));
                float3 sn = UnpackNormalScale(SAMPLE_TEXTURE2D(_SideNrm, sampler_SideNrm, ws.xz / _SideTile), 0.6);
                float3 n = normalize(lerp(N, nTop, topMask));

                // wet sheen + stains (corruption), both noise driven, mostly on top faces
                float wetN = UWFbm(ws.xz * 0.22 + 3.1);
                float wet = smoothstep(1.0 - _WetAmt, 1.05 - _WetAmt + 0.25, wetN) * topMask;
                float stainN = UWFbm(ws.xz * 0.15 + 11.7);
                float stain = smoothstep(0.62 - _StainAmt * 0.3, 0.8 - _StainAmt * 0.3, stainN) * topMask;
                albedo *= lerp(1.0, 0.55, wet);
                albedo = lerp(albedo, float3(0.05, 0.025, 0.07) + albedo * 0.2, stain * 0.85);
                float smooth = lerp(_Smooth, _WetSmooth, saturate(wet + stain * 0.6)) * (1.0 - grout * 0.6 * topMask);

                // cracks: voronoi borders, masked into clusters
                float3 emission = _Emission.rgb;
                if (_CrackAmt > 0.001)
                {
                    // 11-11 (Kido: "줄로 찍찍 그어논거 같에, 은은하게"): the cracks were straight voronoi edges of one width
                    // and one brightness. Now: the cell lattice is bent by a two-scale warp so edges wander, the width
                    // swells and pinches along each crack and whole stretches fade out, the core is thin and dim and
                    // most of the light is a wide soft halo, and the glow breathes slowly in patches instead of a wave.
                    float2 cp = ws.xz * 0.8;
                    float2 warp = float2(UWNoise(cp * 0.9 + 3.7), UWNoise(cp * 0.9 + 9.1)) - 0.5;
                    warp += (float2(UWNoise(cp * 3.1 + 1.3), UWNoise(cp * 3.1 + 6.6)) - 0.5) * 0.35;
                    float2 v = UWVoronoi(cp + warp * 0.9);
                    float along = UWNoise(ws.xz * 1.7 + 21.0);
                    float width = lerp(0.006, 0.03, along * along);
                    float present = smoothstep(0.3, 0.55, UWNoise(ws.xz * 0.9 + 40.0));
                    float core = smoothstep(width, width * 0.25, v.y) * present;
                    float halo = exp(-v.y * 18.0) * lerp(0.35, 1.0, present);
                    float clN = UWFbm(ws.xz * 0.16 + 5.3);
                    float cluster = smoothstep(0.66 - _CrackAmt * 0.12, 0.9 - _CrackAmt * 0.12, clN) * topMask;
                    float darkCl = smoothstep(0.42, 0.7, clN) * topMask;
                    albedo *= 1.0 - core * darkCl * 0.6;
                    float breathe = 0.6 + 0.4 * sin(_UWTime * 0.7 + UWNoise(ws.xz * 0.35) * 6.28);
                    emission += _CrackCol.rgb * cluster * breathe * (core * 0.45 + halo * 0.12);
                    // faint violet bloom over the whole cracked patch, so the cracks sit in a glow rather than on top
                    emission += _CrackCol.rgb * 0.018 * cluster * cluster;
                }

                InputData d = (InputData)0;
                d.positionWS = ws;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(ws);
                d.shadowCoord = TransformWorldToShadowCoord(ws);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = smooth;
                s.occlusion = lerp(1.0, 0.55, grout * topMask);
                s.emission = emission;
                s.alpha = 1;
                s.normalTS = float3(0, 0, 1);

                half4 col = UniversalFragmentPBR(d, s);
                float rim = pow(1.0 - saturate(dot(n, d.viewDirectionWS)), 3.0);
                col.rgb += _RimCol.rgb * rim * _RimAmt;
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attr a) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
