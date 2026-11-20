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
        _SoilTile("Soil metres per tile (unused)", Float) = 3.0
        _TileLitter("Bamboo litter metres per tile", Float) = 5.0
        _TileLeaves("Dead leaves metres per tile", Float) = 4.5
        _TileEarth("Bare earth metres per tile", Float) = 6.0
        _TileRoots("Roots metres per tile", Float) = 5.0
        _Detail("Scan grain kept (0 = flat mean colour)", Range(0, 1)) = 0.55
        _MipBias("Scan mip bias (softer grain)", Range(0, 3)) = 0.8
        _TileMoss("Moss metres per tile", Float) = 4.0
        _TileSlab("Flagstones metres per tile", Float) = 3.0
        _MossGain("Moss gain", Float) = 2.0
        _SlabGain("Flagstone gain", Float) = 1.5
        _Gains("Scan gains (litter, leaves, earth, roots)", Vector) = (2, 2.4, 1.8, 1.6)
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
            SAMPLER(sampler_LeafTex);
            float4 _BA_Contact[12];   // r29: landmark feet (x, z, radius, strength), set by BambooField
            CBUFFER_START(UnityPerMaterial)
                float4 _SoilTex_ST, _SoilNrm_ST, _LeafTex_ST, _LeafNrm_ST, _Leaf2Tex_ST, _Leaf2Nrm_ST, _PathTex_ST, _PathNrm_ST, _SlabTex_ST, _SlabNrm_ST;
                float4 _PebbleTex_ST, _PebbleNrm_ST, _RootTex_ST, _RootNrm_ST, _MossTex_ST;
                float _SoilTile, _TileLitter, _TileLeaves, _TileEarth, _TileRoots, _NormalStrength, _Calm, _Leaves, _Moss, _Pebbles, _Roots, _Slabs, _Damp, _Dapple;
                float4 _Tint, _Shade, _Ambient, _LeafCol, _MossCol, _Gains;
                float _Detail, _MipBias, _TileMoss, _TileSlab, _MossGain, _SlabGain;
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

            // r30 (Kido 11-18: the texture itself looked washed out, flat grey earth, a jagged dark band): the scans now
            // carry the floor. Five Megascans surfaces, each at its own colour (only a gain to bring it into the value
            // range), height-blended (scan luminance as height) in big regions (D4 GDC: few materials, big shapes, borders
            // broken by the scans themselves): dry bamboo litter (base), dead leaves, bare packed earth, roots toward the
            // bamboo, moss in the shade. Anti-tiling picks one of two rotated samples by height instead of cross-fading
            // them (a cross-fade averages the grain away, which read as fog). The player trample trail is not drawn.
            float Lum(float3 c) { return dot(c, float3(0.3, 0.55, 0.15)); }
            // r38 (Kido 11-18: "바닥패턴"): two samples still showed the tile as diagonal streaks. Texture bombing: the floor is
            // cut into ~1.7-tile cells, each cell samples the scan with its own random rotation and offset, and the four
            // nearest cells are blended across smooth cell borders (then sharpened by the scan's own light/dark), so no
            // two neighbouring patches repeat and no straight seam or streak lines up.
            void Surf(TEXTURE2D_PARAM(t, s), TEXTURE2D_PARAM(tn, sn), float2 xz, float tile, float ang, float pick, out float3 col, out float2 nrm)
            {
                float cellSize = tile * 1.7;
                float2 g = xz / cellSize - 0.5, c0 = floor(g), f = frac(g);
                float3 acc = 0; float2 nacc = 0; float wsum = 0;
                [unroll] for (int j = 0; j < 2; j++)
                [unroll] for (int i2 = 0; i2 < 2; i2++)
                {
                    float2 cid = c0 + float2(i2, j);
                    float a = ang + RFHash(cid + 3.1) * 6.2832;
                    float2 off = float2(RFHash(cid + 7.7), RFHash(cid + 1.9));
                    float sc = lerp(0.85, 1.2, RFHash(cid + 5.3));
                    float2 uv = Rot(xz, sin(a), cos(a)) / (tile * sc) + off;
                    float3 c = SAMPLE_TEXTURE2D_BIAS(t, s, uv, _MipBias).rgb;
                    float2 bw = lerp(1 - f, f, float2(i2, j));
                    float w = smoothstep(0.0, 1.0, bw.x) * smoothstep(0.0, 1.0, bw.y);
                    w = pow(saturate(w), 1.5) * (0.6 + Lum(c) * 4.0);
                    float2 n = Nrm(TEXTURE2D_ARGS(tn, sn), uv);
                    n = float2(n.x * cos(a) + n.y * sin(a), -n.x * sin(a) + n.y * cos(a));   /* rotate the normal back with the uv */
                    acc += c * w; nacc += n * w; wsum += w;
                }
                col = acc / max(wsum, 1e-4);
                float3 avg = SAMPLE_TEXTURE2D_LOD(t, s, xz / tile, 10).rgb;
                col = lerp(avg, col, _Detail);
                nrm = nacc / max(wsum, 1e-4) * _Detail;
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.ws.xz;
                float drift = saturate(i.color.r), trail = saturate(i.color.g), under = saturate(i.color.b);
                float court = 1 - saturate(i.color.a);
                float big = RFFbm(xz * 0.025 + 11.0), mid = RFFbm(xz * 0.11 + 2.0), fine = RFFbm(xz * 0.5);
                float pick = RFFbm(xz * 0.06 + 37.0);

                // r32 (Kido 11-18: "바닥 아무거나 가져다 쓰지 말고 레퍼런스 준거대로"): the layers are exactly what the B2 floor
                // (Wukong bamboo grove) is made of: dark damp soil as the base (mud_forest), big mossy carpets (Fab Megascans
                // Mossy Ground), a few dead leaves, a packed-earth path with flat flagstones set in it (Fab Megascans Mossy
                // Flagstone Floor), roots toward the bamboo. No dry straw (not in B2).
                float3 cL, cD, cE, cR, cM; float2 nL, nD, nE, nR, nM;
                Surf(TEXTURE2D_ARGS(_SoilTex, sampler_LeafTex), TEXTURE2D_ARGS(_SoilNrm, sampler_LeafTex), xz, _TileLitter, 0.3, pick, cL, nL);
                Surf(TEXTURE2D_ARGS(_Leaf2Tex, sampler_LeafTex), TEXTURE2D_ARGS(_Leaf2Nrm, sampler_LeafTex), xz, _TileLeaves, 1.4, pick, cD, nD);
                Surf(TEXTURE2D_ARGS(_PathTex, sampler_LeafTex), TEXTURE2D_ARGS(_PathNrm, sampler_LeafTex), xz, _TileEarth, 2.5, pick, cE, nE);
                Surf(TEXTURE2D_ARGS(_RootTex, sampler_LeafTex), TEXTURE2D_ARGS(_RootNrm, sampler_LeafTex), xz, _TileRoots, 0.9, pick, cR, nR);
                Surf(TEXTURE2D_ARGS(_LeafTex, sampler_LeafTex), TEXTURE2D_ARGS(_LeafNrm, sampler_LeafTex), xz, _TileMoss, 1.9, pick, cM, nM);
                cL *= _Gains.x * float3(0.86, 0.9, 1.0);   /* r36c: damp soil read orange-red next to B2 */
                /* r37 (Kido 11-18: "초록바닥하고 흙바닥 색감차이 너무 심해"): the moss is moss growing on this soil, so it takes
                   the soil's value and only part of the green; the soil gets a faint olive cast. One family of colours. */ cD *= _Gains.y; cE *= _Gains.z; cR *= _Gains.w; cM *= _MossGain * _MossCol.rgb;
                cM = lerp(cL * float3(0.95, 1.05, 0.9), cM * lerp(1.0, Lum(cL) / max(Lum(cM), 1e-4), 0.6), 0.75);
                cL *= float3(0.96, 1.0, 0.94);
                cE = lerp(cL, cE, 0.45);   /* r33b: trodden soil under the path stones, not a separate brown band */

                // region weights (0..1): big soft shapes, 20-40 m (B2: soil and moss in large interlocking patches)
                float wL = 0.55;
                float wM = saturate(smoothstep(0.44, 0.6, RFFbm(xz * 0.03 + 21.0) + (mid - 0.5) * 0.35 + (RFFbm(xz * 0.28 + 33.0) - 0.5) * 0.35) + under * 0.35) * _Moss;   /* r36c: moss edge broken at 2-4 m, no smooth blob outline */
                float wD = smoothstep(0.6, 0.74, RFFbm(xz * 0.05 + 3.0) + drift * 0.25) * _Leaves;
                float wE = 0;   /* r39 (Kido 11-18: "흙길 다른 높이로 놓은 건 이상해"): no separate path earth; the path is the same soil with stones on it */   /* r36c: no separate path band; only worn patches between the stones */
                float wR = under * smoothstep(0.45, 0.65, RFFbm(xz * 0.07 + 8.0)) * _Roots;
                // height blend: each scan's own light/dark pattern decides who wins along the borders
                const float hs = 0.45, depth = 0.16;   /* r37: softer soil-moss transition */
                float hL = wL + saturate(Lum(cL) * 4.0) * hs, hD = wD + saturate(Lum(cD) * 4.0) * hs, hE = wE + saturate(Lum(cE) * 4.0) * hs;
                float hR = wR + saturate(Lum(cR) * 4.0) * hs, hM = wM + saturate(Lum(cM) * 4.0) * hs;
                float top = max(max(max(hL, hD), max(hE, hR)), hM) - depth;
                float bL = max(hL - top, 0), bD = max(hD - top, 0) * step(0.02, wD), bE = max(hE - top, 0) * step(0.02, wE);
                float bR = max(hR - top, 0) * step(0.02, wR), bM = max(hM - top, 0) * step(0.02, wM);
                float bs = bL + bD + bE + bR + bM + 1e-4;
                float3 col = (cL * bL + cD * bD + cE * bE + cR * bR + cM * bM) / bs;
                float2 nxy = (nL * bL + nD * bD + nE * bE + nR * bR + nM * bM) / bs;
                float moss = bM / bs;
                // macro value variation (10-40 m) on top, never flattening the grain
                col *= lerp(0.8, 1.1, smoothstep(0.25, 0.75, big));   /* r34: only a light macro variation, no shading of the scans */   /* r31: big soft light/dark shapes carry the floor */

                // the shrine court: old flagstones, the paving front ragged and buried under litter and moss
                float slab = 0, joint = 0;
                float pave = court * _Slabs;   /* r33: the path stones are real geometry now; only the shrine court is paved in the texture */
                [branch] if (pave > 0.01)
                {
                    float2 su = Rot(xz, 0.12, 0.99) / _TileSlab;
                    float3 sc = SAMPLE_TEXTURE2D(_SlabTex, sampler_LeafTex, su).rgb;
                    float sl = Lum(sc);
                    float savg = Lum(SAMPLE_TEXTURE2D_LOD(_SlabTex, sampler_LeafTex, su, 10).rgb);
                    float stone = smoothstep(savg * 0.75, savg * 1.0, sl);   /* r32: joints relative to the scan mean */
                    float front = pave + (RFFbm(xz * 0.35 + 5.0) - 0.5) * 0.9 + (fine - 0.5) * 0.3;
                    float keep = smoothstep(0.45, 0.6, front);
                    float missing = smoothstep(0.45, 0.4, RFFbm(xz * 0.18 + 7.0));
                    slab = keep * stone * (1 - missing * 0.85);
                    joint = keep * (1 - stone);
                    float3 stoneCol = sc * _SlabGain * float3(1.0, 0.94, 0.84);   /* r32: B2 stones grey-brown, not moon-blue */
                    col = lerp(col, stoneCol, slab);
                    col = lerp(col, lerp(cL * 0.8, cM, 0.6), joint * 0.85);   /* r32: joints = soil and moss (was _MossCol, now white) */
                    nxy = lerp(nxy, Nrm(TEXTURE2D_ARGS(_SlabNrm, sampler_LeafTex), su), slab);
                }

                // ground contact round the landmarks: soil and moss heaped against the base, darker right at the foot
                float contact = 0;
                [unroll] for (int ci = 0; ci < 12; ci++)
                {
                    float4 cd = _BA_Contact[ci];
                    if (cd.z <= 0) continue;
                    float d = length(xz - cd.xy) / cd.z + (mid - 0.5) * 0.35 + (fine - 0.5) * 0.15;
                    contact = max(contact, saturate(1 - smoothstep(0.75, 1.35, d)) * cd.w);
                }
                [branch] if (contact > 0.01)
                {
                    float3 heap = lerp(cE, cM, smoothstep(0.4, 0.7, fine + contact * 0.3) * 0.7);
                    col = lerp(col, heap, contact * 0.75 * (1 - slab * 0.6));
                    col *= 1 - contact * contact * 0.3;
                }

                col *= _Tint.rgb;
                col = lerp(col, col * _Shade.rgb * 6.0, under * 0.6);
                float damp = smoothstep(0.62, 0.74, RFFbm(xz * 0.05 + 41.0)) * _Damp * (1 - slab) * (1 - moss * 0.5) * 0.4;
                col *= 1 - damp * 0.25;
                nxy *= _NormalStrength;

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
