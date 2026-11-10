// Bamboo grove floor, after the bamboo-forest photo refs (dry bamboo leaves over the soil, a packed-earth trail):
// everything flush with the ground (nothing blocks movement), calm contrast so combat stays readable.
//   - forest soil (Poly Haven forest_ground_04, CC0) as the base;
//   - dry bamboo leaf litter (brown_mud_leaves_01, CC0, tinted pale straw/tan) in clustered drifts, denser near the
//     bamboo, the stumps and the fallen culms (vertex colour R);
//   - a narrow winding mountain-trail footpath of packed earth with worn edges and a few flat stones set in it (G);
//   - scattered small pebbles (river_small_rocks, CC0), moss near the bamboo, dark damp soil patches;
//   - dappled light through the canopy, darker under the bamboo (B).
Shader "BambooArena/Floor"
{
    Properties
    {
        _SoilTex("Soil", 2D) = "grey" {}
        _SoilNrm("Soil (normal)", 2D) = "bump" {}
        _LeafTex("Leaf litter", 2D) = "grey" {}
        _LeafNrm("Leaf litter (normal)", 2D) = "bump" {}
        _PathTex("Packed earth (trail)", 2D) = "grey" {}
        _PathNrm("Packed earth (normal)", 2D) = "bump" {}
        _PebbleTex("Pebbles", 2D) = "grey" {}
        _PebbleNrm("Pebbles (normal)", 2D) = "bump" {}
        _MossTex("Moss colour source", 2D) = "grey" {}
        _SoilTile("Soil metres per tile", Float) = 3.0
        _Tint("Tint", Color) = (0.92, 1.0, 0.86, 1)
        _Shade("Under canopy", Color) = (0.06, 0.075, 0.05, 1)
        _Ambient("Ambient", Color) = (0.20, 0.25, 0.21, 1)
        _LeafCol("Dry bamboo leaf tint", Color) = (1.0, 0.86, 0.58, 1)
        _MossCol("Moss tint", Color) = (0.36, 0.42, 0.20, 1)
        _NormalStrength("Normal strength", Range(0, 2)) = 0.55
        _Calm("Calm (less texture contrast)", Range(0, 1)) = 0.3
        _Leaves("Leaf litter amount", Range(0, 1)) = 0.75
        _Moss("Moss amount", Range(0, 1)) = 0.5
        _Pebbles("Pebbles", Range(0, 1)) = 0.5
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
            TEXTURE2D(_SoilTex); TEXTURE2D(_SoilNrm); TEXTURE2D(_LeafTex); TEXTURE2D(_LeafNrm);
            TEXTURE2D(_PathTex); TEXTURE2D(_PathNrm); TEXTURE2D(_PebbleTex); TEXTURE2D(_PebbleNrm); TEXTURE2D(_MossTex);
            SAMPLER(sampler_SoilTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _SoilTex_ST, _SoilNrm_ST, _LeafTex_ST, _LeafNrm_ST, _PathTex_ST, _PathNrm_ST, _PebbleTex_ST, _PebbleNrm_ST, _MossTex_ST;
                float _SoilTile, _NormalStrength, _Calm, _Leaves, _Moss, _Pebbles, _Damp, _Dapple;
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

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.ws.xz;
                float drift = saturate(i.color.r), trail = saturate(i.color.g), under = saturate(i.color.b);
                float big = RFNoise(xz * 0.05 + 11.0), mid = RFNoise(xz * 0.23 + 2.0), fine = RFNoise(xz * 1.4);

                // soil, two scales mixed so it never tiles
                float mixN = smoothstep(0.3, 0.7, RFNoise(xz * 0.07 + 3.3));
                float2 s1 = xz / _SoilTile, s2 = Rot(xz, 0.588, 0.809) / (_SoilTile * 1.73) + 0.37;
                float3 col = lerp(SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, s1).rgb, SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, s2).rgb, mixN);
                float2 nxy = SAMPLE_TEXTURE2D(_SoilNrm, sampler_SoilTex, lerp(s1, s2, step(0.5, mixN))).rg * 2 - 1;

                // the trail: packed earth with worn, ragged edges (broken by noise), a few flat stones set in it
                float tr = smoothstep(0.3, 0.65, trail + (mid - 0.5) * 0.5 + (fine - 0.5) * 0.25);
                float flat = 0;
                [branch] if (tr > 0.01)
                {
                    float2 pu = Rot(xz, 0.31, 0.95) / 2.6;
                    col = lerp(col, SAMPLE_TEXTURE2D(_PathTex, sampler_SoilTex, pu).rgb * float3(1.02, 0.95, 0.82), tr);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_PathNrm, sampler_SoilTex, pu).rg * 2 - 1, tr * 0.6);
                    // flat stones: rare, irregular, only well inside the trail
                    flat = smoothstep(0.8, 0.86, RFNoise(xz * 1.1 + 13.0) * 0.75 + fine * 0.25) * smoothstep(0.6, 0.9, tr);
                }
                // pebbles: small clusters here and there (more along the trail edges), never a carpet
                float peb = smoothstep(0.7, 0.8, RFNoise(xz * 0.6 + 61.0) * 0.6 + fine * 0.4) * _Pebbles * (0.6 + tr * (1 - tr) * 1.6);
                peb = max(peb, flat);
                [branch] if (peb > 0.01)
                {
                    float2 bu = xz / (flat > 0.01 ? 2.2 : 1.1);
                    float3 pc = SAMPLE_TEXTURE2D(_PebbleTex, sampler_SoilTex, bu).rgb;
                    pc = lerp(pc, dot(pc, float3(0.33, 0.33, 0.34)).xxx, 0.3) * 0.85;
                    col = lerp(col, pc, peb);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_PebbleNrm, sampler_SoilTex, bu).rg * 2 - 1, peb);
                }
                // dry bamboo leaves: the main cover, in drifts (pale straw / tan, muted), thin on the trail
                float leafN = RFNoise(xz * 0.14 + 7.7) * 0.5 + mid * 0.3 + fine * 0.2;
                float leaves = smoothstep(0.26, 0.66, leafN + 0.1 + drift * 0.4 + under * 0.1) * (1 - tr * 0.85) * (1 - peb * 0.7) * _Leaves;
                [branch] if (leaves > 0.01)
                {
                    float2 lu = Rot(xz, 0.83, 0.56) / 1.9;
                    float3 lc = SAMPLE_TEXTURE2D(_LeafTex, sampler_SoilTex, lu).rgb;
                    float ll = dot(lc, float3(0.33, 0.33, 0.34));
                    // dry bamboo leaves: keep the scan's leaf-by-leaf light/dark (contrast stretched), straw coloured
                    lc = _LeafCol.rgb * saturate(0.6 + (ll - 0.22) * 2.2) * lerp(0.82, 1.05, RFNoise(xz * 2.3));
                    col = lerp(col, lc, leaves * 0.9);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_LeafNrm, sampler_SoilTex, lu).rg * 2 - 1, leaves * 0.7);
                }
                // moss toward the bamboo and in damp hollows
                float moss = smoothstep(0.55, 0.75, RFNoise(xz * 0.2 + 21.0) * 0.6 + under * 0.45 + mid * 0.15) * (1 - leaves * 0.7) * (1 - tr) * _Moss;
                [branch] if (moss > 0.01)
                {
                    float3 mc = SAMPLE_TEXTURE2D(_MossTex, sampler_SoilTex, xz / 1.6).rgb;
                    col = lerp(col, _MossCol.rgb * (0.7 + dot(mc, float3(0.3, 0.6, 0.1)) * 0.9), moss * 0.75);
                }
                // calm the scan contrast, tint, shade under the bamboo
                float3 avg = float3(0.30, 0.27, 0.19);
                col = lerp(col, avg * (0.8 + 0.4 * dot(col, float3(0.33, 0.33, 0.33)) / 0.24), _Calm * (1 - peb * 0.5) * (1 - leaves * 0.7));
                col *= lerp(0.72, 1.0, saturate(leaves + tr + peb));   // bare soil between the drifts reads darker
                col *= _Tint.rgb * (0.9 + 0.2 * big);
                col = lerp(col, col * _Shade.rgb * 6.0, under * 0.65);
                // damp patches: darker soil, a little glossy (not on the dry leaves)
                float damp = smoothstep(0.6, 0.72, RFNoise(xz * 0.09 + 41.0) * 0.7 + fine * 0.3) * _Damp * (1 - leaves * 0.8);
                col *= 1 - damp * 0.4;
                nxy *= _NormalStrength * (1 - damp * 0.6);

                float4 trl = RFTrail(xz);
                col *= 1 - trl.g * 0.35;

                float3 n0 = normalize(i.n);
                float3 n = normalize(n0 + float3(nxy.x, 0, nxy.y));
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.ws));
                ml.shadowAttenuation *= RFStaticShadow(xz);
                // dappled light through the canopy: soft blobs drifting slowly with the wind
                float2 dw = _RF_Wind.xy * _RF_Time * 0.08;
                float dap = smoothstep(0.38, 0.62, RFNoise(xz * 0.33 + dw) * 0.65 + RFNoise(xz * 0.9 - dw * 1.7) * 0.35);
                float dappled = lerp(1, lerp(0.5, 1.2, dap), _Dapple * lerp(0.6, 1.0, under));
                float3 lit = _Ambient.rgb + ml.color * saturate(dot(n, ml.direction)) * ml.shadowAttenuation * dappled;
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float3 h = normalize(ml.direction + v);
                float spec = pow(saturate(dot(normalize(lerp(n, n0, damp)), h)), lerp(20, 90, damp)) * (0.04 + damp * 0.4 + peb * 0.1) * ml.shadowAttenuation * dappled;
                float3 c = col * lit + ml.color * spec;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    c += col * add.color * add.distanceAttenuation * saturate(dot(n, add.direction));
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
