// Bamboo grove floor (11-13 redo, Kido picked the Black Myth Wukong bamboo-grove floor, ref board B2): dark forest
// earth, never one material over everything. Everything is flush with the ground (nothing blocks movement).
//   - dark forest soil (mud_forest, Poly Haven CC0) as the base, two scales mixed so it never tiles;
//   - dry bamboo leaf litter (Megascans forest floor: narrow straw strands) in big drifts toward the bamboo and around
//     the props (vertex colour R), darker dead-leaf litter (Megascans) in other regions;
//   - a worn packed-earth path (Megascans tracks) with old stone slabs set in it in stretches (stone_pathway), (G);
//   - bamboo roots surfacing near the bamboo, gravel in small patches, moss in the shade, dark damp hollows;
//   - moonlight dappled through the canopy, darker under the bamboo (B).
Shader "BambooArena/Floor"
{
    Properties
    {
        _SoilTex("Soil", 2D) = "grey" {}
        _SoilNrm("Soil (normal)", 2D) = "bump" {}
        _LeafTex("Bamboo leaf litter", 2D) = "grey" {}
        _LeafNrm("Bamboo leaf litter (normal)", 2D) = "bump" {}
        _Leaf2Tex("Dead leaf litter", 2D) = "grey" {}
        _Leaf2Nrm("Dead leaf litter (normal)", 2D) = "bump" {}
        _PathTex("Packed earth (path)", 2D) = "grey" {}
        _PathNrm("Packed earth (normal)", 2D) = "bump" {}
        _SlabTex("Stone slabs (path)", 2D) = "grey" {}
        _SlabNrm("Stone slabs (normal)", 2D) = "bump" {}
        _PebbleTex("Gravel", 2D) = "grey" {}
        _PebbleNrm("Gravel (normal)", 2D) = "bump" {}
        _RootTex("Roots", 2D) = "grey" {}
        _RootNrm("Roots (normal)", 2D) = "bump" {}
        _MossTex("Moss colour source", 2D) = "grey" {}
        _SoilTile("Soil metres per tile", Float) = 3.0
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Shade("Under canopy", Color) = (0.06, 0.06, 0.06, 1)
        _Ambient("Ambient", Color) = (0.20, 0.25, 0.21, 1)
        _LeafCol("Dry bamboo leaf tint", Color) = (1.0, 0.9, 0.7, 1)
        _MossCol("Moss tint", Color) = (0.26, 0.31, 0.16, 1)
        _NormalStrength("Normal strength", Range(0, 2)) = 0.9
        _Calm("Calm (less texture contrast)", Range(0, 1)) = 0.1
        _Leaves("Leaf litter amount", Range(0, 1)) = 0.75
        _Moss("Moss amount", Range(0, 1)) = 0.5
        _Pebbles("Gravel", Range(0, 1)) = 0.5
        _Roots("Roots", Range(0, 1)) = 0.6
        _Slabs("Stone slabs on the path", Range(0, 1)) = 0.7
        _Damp("Damp patches", Range(0, 1)) = 0.5
        _Dapple("Dappled light", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Assets/Game/ReedField/Shaders/ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_SoilTex); TEXTURE2D(_SoilNrm); TEXTURE2D(_LeafTex); TEXTURE2D(_LeafNrm); TEXTURE2D(_Leaf2Tex); TEXTURE2D(_Leaf2Nrm);
            TEXTURE2D(_PathTex); TEXTURE2D(_PathNrm); TEXTURE2D(_SlabTex); TEXTURE2D(_SlabNrm); TEXTURE2D(_PebbleTex); TEXTURE2D(_PebbleNrm);
            TEXTURE2D(_RootTex); TEXTURE2D(_RootNrm); TEXTURE2D(_MossTex);
            SAMPLER(sampler_SoilTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SoilTex_ST, _SoilNrm_ST, _LeafTex_ST, _LeafNrm_ST, _Leaf2Tex_ST, _Leaf2Nrm_ST, _PathTex_ST, _PathNrm_ST, _SlabTex_ST, _SlabNrm_ST;
                float4 _PebbleTex_ST, _PebbleNrm_ST, _RootTex_ST, _RootNrm_ST, _MossTex_ST;
                float _SoilTile, _NormalStrength, _Calm, _Leaves, _Moss, _Pebbles, _Roots, _Slabs, _Damp, _Dapple;
                float4 _Tint, _Shade, _Ambient, _LeafCol, _MossCol;
            CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.n = TransformObjectToWorldNormal(a.normalOS);
                o.positionCS = TransformWorldToHClip(o.ws); o.color = a.color; return o;
            }

            float2 Rot(float2 p, float s, float c) { return float2(p.x * c - p.y * s, p.x * s + p.y * c); }
            float2 Nrm(TEXTURE2D_PARAM(t, s), float2 uv) { return SAMPLE_TEXTURE2D(t, s, uv).rg * 2 - 1; }

            // r18: bamboo-leaf shadows: per cell, two slender lance-shaped leaves (thin ellipses) at random angles, in
            // sprays (a cell-wide bias angle), soft edges. Returns 0..1 shadow coverage.
            float LeafShadow(float2 p)
            {
                float2 c = floor(p); float sh = 0;
                float spray = RFHash(floor(p * 0.25) + 3.7) * 6.283;
                [unroll] for (int j = -1; j <= 1; j++)
                [unroll] for (int i = -1; i <= 1; i++)
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float2 cc = c + float2(i, j) + k * 0.37;
                    float2 ctr = c + float2(i, j) + float2(RFHash(cc + 1.3), RFHash(cc + 7.9));
                    float a = spray + (RFHash(cc + 4.1) - 0.5) * 1.6;
                    float sa = sin(a), ca = cos(a);
                    float2 d = p - ctr; d = float2(d.x * ca - d.y * sa, d.x * sa + d.y * ca);
                    float len = 0.42 + RFHash(cc + 9.2) * 0.25;
                    float wid = 0.075 * (1 - saturate(abs(d.x) / len) * 0.7);   // tapers to a point
                    float e = (d.x * d.x) / (len * len) + (d.y * d.y) / (wid * wid);
                    sh = max(sh, 1 - smoothstep(0.2, 1.0, e));
                }
                return sh;
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.ws.xz;
                float drift = saturate(i.color.r), trail = saturate(i.color.g), under = saturate(i.color.b);
                // r27 rewrite (Kido 11-14: "자글거리는 일정한 패턴, 레퍼런스랑 하나도 안 똑같다"): B2 (Wukong bamboo grove) is
                // read from BIG shapes: smooth dark earth, old flagstones with moss in the joints, big moss carpets, a few
                // leaf clumps, pebbles along the stone edges. No fine grain carpet, no moon pools / leaf-shadow strokes.
                float big = RFFbm(xz * 0.03 + 11.0), mid = RFFbm(xz * 0.12 + 2.0), fine = RFFbm(xz * 0.6);

                // smooth dark earth: the scan sampled large and flattened toward one earth colour (no grain)
                float2 s1 = xz / _SoilTile, s2 = Rot(xz, 0.588, 0.809) / (_SoilTile * 1.61) + 0.37;
                float3 soil = lerp(SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, s1).rgb, SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, s2).rgb, smoothstep(0.35, 0.65, mid));
                float3 earth = float3(0.115, 0.085, 0.06) * lerp(0.8, 1.2, big);   // r28: dark brown earth (r27 0.2 read as grey snow)
                float3 col = lerp(earth, soil * float3(0.8, 0.68, 0.55), 0.3);
                float2 nxy = Nrm(TEXTURE2D_ARGS(_SoilNrm, sampler_SoilTex), s1) * 0.35;

                // worn path (packed earth) along the trail, edges broken by large noise
                float tr = smoothstep(0.3, 0.6, trail + (mid - 0.5) * 0.5);
                [branch] if (tr > 0.01)
                {
                    float2 pu = Rot(xz, 0.31, 0.95) / 5.0;
                    float3 pc = SAMPLE_TEXTURE2D(_PathTex, sampler_SoilTex, pu).rgb;
                    col = lerp(col, lerp(earth * 1.15, pc * 0.75, 0.35), tr);
                }

                // old flagstones: along the path and in courtyard patches (~20 % of the floor), some missing
                float court = max(smoothstep(0.66, 0.72, RFFbm(xz * 0.025 + 55.0) + (mid - 0.5) * 0.2) * 0.8, 1 - saturate(i.color.a));   // the shrine court (vertex a) + a few old paved patches
                float slabArea = saturate(max(court, smoothstep(0.45, 0.8, tr))) * _Slabs;   // r27: the path is paved (B2 stone path)
                float slab = 0, joint = 0;
                [branch] if (slabArea > 0.01)
                {
                    float2 su = Rot(xz, 0.12, 0.99) / 3.6;
                    float3 sc = SAMPLE_TEXTURE2D(_SlabTex, sampler_SoilTex, su).rgb;
                    float sl = dot(sc, float3(0.3, 0.5, 0.2));
                    float stone = smoothstep(0.22, 0.3, sl);                    // the scan's dark joints -> earth/moss
                    float missing = smoothstep(0.45, 0.4, RFNoise(floor(su * 4.0) + 7.0) * 0.6 + RFFbm(xz * 0.2) * 0.4);   // whole stones missing here and there
                    slab = slabArea * stone * (1 - missing * 0.85);
                    joint = slabArea * (1 - stone);
                    float3 stoneCol = lerp(float3(0.24, 0.225, 0.2), float3(0.19, 0.18, 0.165), RFNoise(floor(su * 4.0) + 3.0)) * lerp(0.85, 1.1, sl);
                    col = lerp(col, stoneCol, slab);
                    nxy = lerp(nxy, Nrm(TEXTURE2D_ARGS(_SlabNrm, sampler_SoilTex), su) * 0.8, slab);
                }

                // moss: big soft carpets (~25 %), in the slab joints, toward the bamboo
                float moss = saturate(smoothstep(0.52, 0.66, RFFbm(xz * 0.035 + 21.0) + (fine - 0.5) * 0.25 + under * 0.25) * (1 - tr * 0.8) + joint * 0.85) * (1 - slab * 0.9) * _Moss;
                [branch] if (moss > 0.01)
                {
                    float3 mc = SAMPLE_TEXTURE2D(_MossTex, sampler_SoilTex, xz / 3.0).rgb;
                    float ml0 = dot(mc, float3(0.3, 0.6, 0.1));
                    col = lerp(col, _MossCol.rgb * lerp(0.75, 1.15, ml0) * lerp(0.85, 1.1, mid), moss);
                }

                // leaves: a few clumps, more at the foot of the bamboo, low contrast (never a carpet)
                float leaves = smoothstep(0.62, 0.74, RFFbm(xz * 0.09 + 7.7) + drift * 0.35 + under * 0.15) * (1 - slab) * (1 - tr * 0.5) * _Leaves;
                [branch] if (leaves > 0.01)
                {
                    float2 lu = Rot(xz, 0.83, 0.56) / 3.0;
                    float3 lc = SAMPLE_TEXTURE2D(_Leaf2Tex, sampler_SoilTex, lu).rgb;
                    lc = lerp(dot(lc, float3(0.33, 0.33, 0.34)).xxx, lc, 0.6) * float3(0.85, 0.72, 0.58);
                    col = lerp(col, lerp(col, lc, 0.7), leaves);
                }

                // pebbles along the stone edges
                float edge = slabArea * (1 - slabArea) * 4.0;
                float peb = smoothstep(0.55, 0.75, edge + (fine - 0.5) * 0.5) * (1 - slab) * _Pebbles;
                [branch] if (peb > 0.01)
                {
                    float3 pcol = SAMPLE_TEXTURE2D(_PebbleTex, sampler_SoilTex, xz / 1.8).rgb;
                    col = lerp(col, lerp(dot(pcol, float3(0.33, 0.33, 0.34)).xxx, pcol, 0.5) * 0.45, peb * 0.8);
                }

                col *= _Tint.rgb;
                col = lerp(col, col * _Shade.rgb * 6.0, under * 0.7);
                float damp = smoothstep(0.62, 0.74, RFFbm(xz * 0.05 + 41.0)) * _Damp * (1 - slab) * (1 - moss * 0.5);
                col *= 1 - damp * 0.3;
                nxy *= _NormalStrength * 0.6;

                float4 trl = RFTrail(xz);
                col *= 1 - trl.g * 0.35;

                float3 n0 = normalize(i.n);
                float3 n = normalize(n0 + float3(nxy.x, 0, nxy.y));
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.ws));
                ml.shadowAttenuation *= RFStaticShadow(xz);
                // canopy dapple only toward the bamboo (soft), nothing in the open
                float2 dw = _RF_Wind.xy * _RF_Time * 0.08;
                float dap = smoothstep(0.38, 0.62, RFNoise(xz * 0.25 + dw) * 0.65 + RFNoise(xz * 0.7 - dw * 1.7) * 0.35);
                float dappled = lerp(1, lerp(0.55, 1.15, dap), _Dapple * saturate(under * 1.5 + drift * 0.4));
                float3 lit = _Ambient.rgb + ml.color * saturate(dot(n, ml.direction)) * ml.shadowAttenuation * dappled;
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float3 h = normalize(ml.direction + v);
                float spec = pow(saturate(dot(normalize(lerp(n, n0, damp)), h)), lerp(20, 90, damp)) * (0.015 + damp * 0.08) * ml.shadowAttenuation * dappled;
                float3 c = col * lit + ml.color * spec;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    c += col * add.color * add.distanceAttenuation * saturate(dot(n, add.direction) * 0.8 + 0.2);
                LIGHT_LOOP_END
                return half4(RFFog(c, i.ws), 1);
            }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.positionOS.xyz); o.n = TransformObjectToWorldNormal(a.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
