// DarkCrypt courtyard floor (v4, 11-15): the Megascans castle floor scan without the visible 7 m grid.
//   - the scan's four big slabs are cut out (between its own grout centre lines) and re-laid one slab per cell in
//     courses of random order and random shift (broken bond, no grid), each mirrored at random; every cell uses the
//     scan slab of its own size, so nothing is stretched. Derivatives come from the continuous world UVs (no mip seams).
//   - per-slab tone/hue shift and two world-space macro noises (different scales, rotated) so no two slabs read the same;
//   - a second Megascans scan (dirt ground) fills the grout and cracks and lies in large drifting patches;
//   - dark grime along the wall bases (distance to the arena border, broken by noise);
//   - a few shallow dark puddles in the low parts (only the puddles get a little smoothness: matte rule).
// URP forward lit (main light + shadows, additional lights, fog). Flat floor only: tangent = +X, bitangent = +Z.
Shader "DarkCrypt/Floor"
{
    Properties
    {
        _BaseMap("Floor albedo", 2D) = "grey" {}
        _BumpMap("Floor normal (GL)", 2D) = "bump" {}
        _AOMap("Floor AO", 2D) = "white" {}
        _DirtMap("Dirt albedo", 2D) = "grey" {}
        _DirtNormal("Dirt normal (GL)", 2D) = "bump" {}
        _Macro("Macro noise (R)", 2D) = "grey" {}
        _Tint("Floor tint", Color) = (0.86, 0.85, 0.89, 1)
        _DirtTint("Dirt tint", Color) = (0.55, 0.5, 0.52, 1)
        _Tile("Metres per scan tile", Float) = 7
        _DirtTile("Metres per dirt tile", Float) = 4.3
        _CellTone("Per-cell tone variation", Range(0, 0.5)) = 0.14
        _MacroAmt("Macro brightness variation", Range(0, 1)) = 0.45
        _DirtAmt("Dirt patch coverage", Range(0, 1)) = 0.45
        _GroutDirt("Dirt in grout and cracks", Range(0, 1)) = 0.7
        _Edge("Arena half size (x, z), wall-grime width, strength", Vector) = (56, 44, 4.5, 0.75)
        _PuddleAmt("Puddles", Range(0, 1)) = 0.5
        _NrmStr("Normal strength", Range(0, 2)) = 1
        _AOStr("AO strength", Range(0, 1)) = 0.85
        _Smooth("Dry smoothness", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); TEXTURE2D(_AOMap);
        TEXTURE2D(_DirtMap); SAMPLER(sampler_DirtMap); TEXTURE2D(_DirtNormal);
        TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _Tint, _DirtTint, _Edge;
        float _Tile, _DirtTile, _CellTone, _MacroAmt, _DirtAmt, _GroutDirt, _PuddleAmt, _NrmStr, _AOStr, _Smooth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
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
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float H21(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float2 H22(float2 p) { float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973)); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.xx + p3.yz) * p3.zy); }
            float VN(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(H21(i), H21(i + float2(1, 0)), f.x), lerp(H21(i + float2(0, 1)), H21(i + float2(1, 1)), f.x), f.y);
            }
            float FBM(float2 p) { return VN(p) * 0.55 + VN(p * 2.07 + 11.3) * 0.3 + VN(p * 4.3 + 3.7) * 0.15; }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float2 w = ws.xz;

                // ---- re-laid slabs. The scan holds 2 x 2 big slabs between grout centre lines at u = 0.503 / 0.951 and
                // v = 0.094 / 0.496 (measured on the AO map): widths 0.448 / 0.552, heights 0.402 / 0.598 of a tile.
                // The floor is laid in courses (rows): each 7 m band of z holds one short and one long course in random
                // order; each course is shifted by a random amount and holds pairs of a narrow and a wide slab in random
                // order. Every slab shows the scan slab of exactly its own size (no stretch), turned 180 degrees half the
                // time, so the grout lines run in a broken bond instead of a grid and no two neighbours mirror each other.
                const float SW = 0.448 * 7.0, SH = 0.402 * 7.0;
                float pz = floor(w.y / _Tile);
                float zl = w.y - pz * _Tile;
                float hA = H21(float2(pz, 3.7)) < 0.5 ? SH : _Tile - SH;
                bool firstRow = zl < hA;
                float rowH = firstRow ? hA : _Tile - hA;
                float rowZ0 = firstRow ? 0.0 : hA;
                float rid = pz * 2.0 + (firstRow ? 0.0 : 1.0);
                float xs = w.x + H21(float2(rid, 9.1)) * _Tile;
                float px = floor(xs / _Tile);
                float xl = xs - px * _Tile;
                float wA = H21(float2(px, rid + 0.37)) < 0.5 ? SW : _Tile - SW;
                bool firstCol = xl < wA;
                float colW = firstCol ? wA : _Tile - wA;
                float colX0 = firstCol ? 0.0 : wA;
                float2 lp = float2(xl - colX0, zl - rowZ0);                    // metres inside the slab
                float2 cell = float2(px * 2.0 + (firstCol ? 0.0 : 1.0), rid);
                float2 hc = H22(cell + 17.0);
                float2 src0 = float2(colW < 3.5 ? 0.503 : 0.951, rowH < 3.5 ? 0.094 : 0.496);
                // mirrors: across x once per course (so two neighbours in a course never mirror each other at their
                // shared joint), across z per slab (courses are offset, so joints between courses never line up)
                float2 flip = float2(H21(float2(rid, 5.5)) < 0.5 ? -1.0 : 1.0, hc.x < 0.5 ? -1.0 : 1.0);
                float2 tuv = src0 + (flip > 0 ? lp : float2(colW, rowH) - lp) / _Tile;
                float2 tdx = ddx(w) / _Tile * flip, tdy = ddy(w) / _Tile * flip;
                float3 alb = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, tuv, tdx, tdy).rgb;
                float4 nrmRaw = SAMPLE_TEXTURE2D_GRAD(_BumpMap, sampler_BaseMap, tuv, tdx, tdy);
                float ao = SAMPLE_TEXTURE2D_GRAD(_AOMap, sampler_BaseMap, tuv, tdx, tdy).r;
                float3 nts = UnpackNormalScale(nrmRaw, _NrmStr);
                nts.xy *= flip;

                // ---- per-slab tone (weathered differently)
                float tone = 1.0 + (hc.y - 0.5) * 2.0 * _CellTone;
                float3 hue = lerp(float3(1.02, 0.99, 0.98), float3(0.97, 0.98, 1.04), H21(cell * 1.7 + 3.1));
                alb *= _Tint.rgb * tone * hue;

                // ---- macro: two world noises (rotated, unrelated scales), soft
                float2 wr = float2(w.x * 0.8 - w.y * 0.6, w.x * 0.6 + w.y * 0.8);
                float m1 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, w * 0.0093).r;   // ~107 m
                float m2 = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, wr * 0.031 + 0.37).r; // ~32 m
                float macro = (m1 - 0.5) * 1.2 + (m2 - 0.5) * 0.8;
                alb *= 1.0 + macro * _MacroAmt;

                // ---- dirt (second scan): in the grout/cracks (low AO) and in large patches
                float2 duv = wr / _DirtTile;
                float3 dirt = SAMPLE_TEXTURE2D(_DirtMap, sampler_DirtMap, duv).rgb * _DirtTint.rgb;
                float3 dn = UnpackNormalScale(SAMPLE_TEXTURE2D(_DirtNormal, sampler_DirtMap, duv), _NrmStr);
                float low = saturate((0.8 - ao) / 0.45);                      // 1 in grout and cracks
                float patch = smoothstep(0.42, 0.72, FBM(w * 0.045 + 7.3) + (m2 - 0.5) * 0.4);
                // wall-base grime: distance to the arena border, broken by noise
                float2 e2 = _Edge.xy - abs(w);
                float edgeD = min(e2.x, e2.y) + (FBM(w * 0.21) - 0.5) * 2.4;
                float grime = (1.0 - smoothstep(0.0, _Edge.z, edgeD)) * _Edge.w;
                float dirtW = saturate(low * _GroutDirt * (0.55 + patch * 0.6) + patch * _DirtAmt * (0.35 + low * 0.65) + grime * 0.55);
                // height-ish blend: dirt settles into the low parts first
                dirtW = smoothstep(0.0, 1.0, saturate(dirtW * 1.4 - (1.0 - low) * 0.35));
                alb = lerp(alb, dirt, dirtW);
                nts = normalize(lerp(nts, dn, dirtW * 0.7));
                alb *= lerp(1.0, 0.32, grime * (0.7 + 0.3 * low));            // soot / damp darkening at the wall feet

                // ---- shallow puddles in the low parts
                float pn = FBM(w * 0.11 + 41.0);
                float puddle = smoothstep(0.66, 0.72, pn + low * 0.12) * _PuddleAmt;
                alb *= lerp(1.0, 0.45, puddle);
                nts = normalize(lerp(nts, float3(0, 0, 1), puddle * 0.85));
                float smooth = lerp(_Smooth, 0.5, puddle);

                float3 n = normalize(float3(nts.x, nts.z, nts.y));            // T = +X, B = +Z, N = +Y
                float occ = lerp(1.0, ao, _AOStr);

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

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = saturate(alb);
                sd.smoothness = smooth;
                sd.occlusion = occ;
                sd.alpha = 1;
                sd.normalTS = float3(0, 0, 1);
                half4 col = UniversalFragmentPBR(d, sd);
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
    }
}
