// 거점 court floor (11-20 v3, Kido: "바닥에 건곤 ... 보스라운드에 있는 그거 가져다 써봐"): the Megascans castle floor stone scan,
// with the 봉인의 제단 seal carved into it at the centre: the same mask the boss-seal floor uses
// (BossSeal/Source/Motifs/BS_Seal_TaegeukPalgwae.png: R = CC0 bagua octagon trigrams by Vuccala, G/B = taegeuk halves;
// Wikimedia Commons). Trigrams = sunken, darker grooves; taegeuk = red / blue tinted stone; a carved ring around it.
// Lit with URP's PBR path (main + additional lights, shadows, SH ambient).
Shader "Hub/Ground"
{
    Properties
    {
        _BaseMap ("Floor albedo", 2D) = "white" {}
        _BumpMap ("Floor normal", 2D) = "bump" {}
        _OcclusionMap ("Floor AO", 2D) = "white" {}
        _BaseColor ("Floor tint", Color) = (0.64, 0.66, 0.68, 1)
        _Tile ("Floor tile (m per repeat)", Float) = 2.5
        _Seal ("Seal mask (R trigrams, G red, B blue)", 2D) = "black" {}
        _SealCentre ("Seal centre xz, radius m, ring width m", Vector) = (0, -0.6, 4.3, 0.35)
        _Groove ("Groove darkness", Range(0, 1)) = 0.55
        _Red ("Taegeuk red tint", Color) = (0.62, 0.28, 0.24, 1)
        _Blue ("Taegeuk blue tint", Color) = (0.26, 0.34, 0.5, 1)
        _Relief ("Relief strength", Float) = 1.4
        _Smooth ("Smoothness", Range(0, 1)) = 0.2
        _Dirt ("Dirt albedo (Megascans dirt ground)", 2D) = "grey" {}
        _DirtN ("Dirt normal", 2D) = "bump" {}
        _Macro ("Macro breakup (Poly Haven rock_boulder_dry)", 2D) = "grey" {}
        _Court ("Court rect: x half, z south, z north, wall dirt band m", Vector) = (15.1, -10.1, 8.8, 1.6)
        _MacroAmt ("Macro: tile m, value swing, dirt patch threshold, lane polish", Vector) = (8, 0.16, 0.62, 0.08)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); TEXTURE2D(_OcclusionMap);
            TEXTURE2D(_Seal); SAMPLER(sampler_Seal);
            TEXTURE2D(_Dirt); TEXTURE2D(_DirtN); TEXTURE2D(_Macro);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float4 _BaseColor; float _Tile; float4 _SealCentre; float _Groove;
                float4 _Red; float4 _Blue; float _Relief; float _Smooth; float4 _Court; float4 _MacroAmt;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };
            V vert(A i)
            {
                V o; o.w = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.w);
                o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            float3 SealAt(float2 uv, float2 gx, float2 gy)
            {
                float inS = step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
                return SAMPLE_TEXTURE2D_GRAD(_Seal, sampler_Seal, uv, gx, gy).rgb * inS;
            }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.w.xz / _Tile;
                float3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb * _BaseColor.rgb;
                float3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv));
                float ao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, uv).r;
                // v7 (Kido: "바닥 타일 너무 일정하다"): mixed slab sizes - in macro zones the same scan is laid at a
                // second, larger size and turned 90 deg (different slab rhythm), so the 2.5 m repeat never runs for long
                {
                    float zn = SAMPLE_TEXTURE2D(_Macro, sampler_BaseMap, i.w.xz / 11.0 + 0.21).r;
                    float zn2 = SAMPLE_TEXTURE2D(_Macro, sampler_BaseMap, i.w.zx / 17.0 + 0.63).r;
                    float wB = smoothstep(0.47, 0.53, zn * 0.65 + zn2 * 0.35);
                    float2 uvB = float2(i.w.z, -i.w.x) / 3.6 + 0.37;
                    float3 aB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB).rgb * _BaseColor.rgb * float3(1.04, 1.0, 0.95);
                    float3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uvB)); nB.xy = float2(-nB.y, nB.x);
                    float oB = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_BaseMap, uvB).r;
                    alb = lerp(alb, aB, wB); nT = normalize(lerp(nT, nB, wB)); ao = lerp(ao, oB, wB);
                    // per-slab value / temperature jitter: hashed on a 1.25 m cell, softened so no square edges show
                    // smooth value noise (bilinear between hashed corners): no hard square edges (Kido's no-square-outline rule)
                    float2 q = i.w.xz / 1.6 + (zn - 0.5) * 0.8; float2 c = floor(q), f = q - c; f = f * f * (3 - 2 * f);
                    #define HH(o) frac(sin(dot(c + o, float2(12.9898, 78.233))) * 43758.5453)
                    float h = lerp(lerp(HH(float2(0, 0)), HH(float2(1, 0)), f.x), lerp(HH(float2(0, 1)), HH(float2(1, 1)), f.x), f.y);
                    alb *= lerp(0.82, 1.14, h) * lerp(float3(0.97, 0.99, 1.04), float3(1.04, 1.0, 0.95), frac(h * 7.13));
                    // a few broken / sunken slabs: darker, dirt-filled, rougher (low cells of the hash in dirty zones)
                    float broken = smoothstep(0.72, 0.85, h) * smoothstep(0.35, 0.55, zn2);
                    alb = lerp(alb, alb * 0.55, broken * (1 - ao * 0.6)); ao *= 1 - 0.35 * broken;
                }

                // v4 (D4 GDC ground rules): ~8 m macro value breakup so the 2.5 m stone tile stops reading as a grid,
                // dirt settled along the wall feet (ragged by the macro mask) and in a few low patches, polished lanes
                // where people walk (centre -> gates, centre -> south). Values stay in a narrow band.
                float mac = SAMPLE_TEXTURE2D(_Macro, sampler_BaseMap, i.w.xz / _MacroAmt.x).r;
                float mac2 = SAMPLE_TEXTURE2D(_Macro, sampler_BaseMap, i.w.zx / (_MacroAmt.x * 1.9) + 0.37).r;
                float m = saturate((mac * 0.6 + mac2 * 0.4 - 0.5) * 2.2 + 0.5);
                alb *= 1 + _MacroAmt.y * (m - 0.5) * 2;
                alb *= lerp(float3(0.96, 0.99, 1.03), float3(1.03, 1.0, 0.96), m);   // v7: macro also shifts temperature
                float wallD = min(min(_Court.x - abs(i.w.x), i.w.z - _Court.y), _Court.z - i.w.z);
                float dirtW = saturate(1 - (wallD - (m - 0.5) * 1.2) / _Court.w);
                float dirtP = smoothstep(_MacroAmt.z, _MacroAmt.z + 0.12, mac2 * 0.7 + mac * 0.3);
                float dirt = saturate(dirtW + dirtP * 0.55);
                float2 duv = i.w.xz / 3.0;
                float3 dAlb = SAMPLE_TEXTURE2D(_Dirt, sampler_BaseMap, duv).rgb * float3(0.62, 0.6, 0.57);
                float3 dN = UnpackNormal(SAMPLE_TEXTURE2D(_DirtN, sampler_BaseMap, duv));
                // dirt fills the joints first: dark AO areas of the stone take it before the stone faces do
                float fill = saturate(dirt * 1.6 - ao * 0.9 + 0.2) * dirt;
                alb = lerp(alb, dAlb, fill);
                float moss = saturate((1 - ao) * 2.2 - 0.6) * saturate(dirtW * 1.4 + smoothstep(0.62, 0.75, mac) * 0.8) * saturate((i.w.z + 2) / 6);
                alb = lerp(alb, alb * float3(0.62, 0.78, 0.52), moss * 0.85);
                nT = normalize(lerp(nT, dN, fill * 0.7));
                // walk lanes: three gate lanes + the south lane, soft-edged, slightly lighter and smoother
                float lane = 0;
                for (int k = -1; k <= 1; k++) { float2 a = float2(0, -0.6), b = float2(k * 9.0, 5.5); float2 pa = i.w.xz - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); lane = max(lane, saturate(1 - length(pa - ba * h) / 1.6)); }
                lane = max(lane, saturate(1 - abs(i.w.x) / 1.4) * saturate((-0.6 - i.w.z) / 2) * saturate((i.w.z + 8.5) / 1.5));
                lane *= 1 - dirt;
                alb *= 1 + _MacroAmt.w * lane;
                alb *= wallD < -0.3 ? 0.4 : 1;   // outside the walls: dark, not a bright dirt apron

                // seal: the octagon mask fills the square [-R, R]
                float2 d = i.w.xz - _SealCentre.xy;
                float R = _SealCentre.z;
                float2 su = float2(-d.x, d.y) / (2 * R) + 0.5;       // flipped x so the taegeuk reads like the flag from the camera
                float2 gx = ddx(su), gy = ddy(su);
                float3 s0 = SealAt(su, gx, gy);
                float3 s1 = SealAt(su, gx * 3, gy * 3);
                float e = 0.004;
                float hx = dot(SealAt(su + float2(e, 0), gx * 3, gy * 3) - SealAt(su - float2(e, 0), gx * 3, gy * 3), 1);
                float hy = dot(SealAt(su + float2(0, e), gx * 3, gy * 3) - SealAt(su - float2(0, e), gx * 3, gy * 3), 1);
                // carved ring just outside the octagon
                float r = length(d);
                float ringC = R * 1.08, rw = _SealCentre.w;
                float ring = saturate(1 - abs(r - ringC) / rw);
                float ringEdge = saturate(1 - abs(abs(r - ringC) - rw * 0.8) / (rw * 0.25));
                // albedo: trigrams sunken + dark, taegeuk tinted, edges shaded
                float edge = saturate(abs(hx) + abs(hy));
                alb = lerp(alb, alb * (1 - _Groove), s0.r);
                alb = lerp(alb, alb * _Red.rgb * 1.6, s0.g);
                alb = lerp(alb, alb * _Blue.rgb * 1.6, s0.b);
                alb *= 1 - 0.4 * edge;
                alb = lerp(alb, alb * 0.78, ring) * (1 - 0.35 * ringEdge);
                ao *= 1 - 0.35 * s1.r;
                // relief: mask gradient as a tangent-space bump (world xz -> tangent xy)
                float3 nS = float3(hx * float2(-1, 1).x, hy, 0) * _Relief;
                float3 n = normalize(float3(nT.x + nS.x, nT.y + nS.y, nT.z));
                float3 nW = normalize(float3(n.x, n.z, n.y));

                InputData id = (InputData)0;
                id.positionWS = i.w; id.normalWS = nW; id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.w);
                id.shadowCoord = TransformWorldToShadowCoord(i.w);
                id.fogCoord = i.fog; id.bakedGI = SampleSH(nW);
                id.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                id.shadowMask = half4(1, 1, 1, 1);
                SurfaceData sd = (SurfaceData)0;
                sd.albedo = alb; sd.metallic = 0; sd.smoothness = _Smooth * (1 - 0.5 * s0.r) * (1 - 0.6 * fill) + 0.08 * lane; sd.occlusion = ao; sd.alpha = 1;
                sd.normalTS = float3(0, 0, 1);
                half4 c = UniversalFragmentPBR(id, sd);
                c.rgb = MixFog(c.rgb, i.fog);
                return c;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 v(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 f() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 v(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 f() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct O { float4 p : SV_POSITION; };
            O v(float4 p : POSITION) { O o; o.p = TransformObjectToHClip(p.xyz); return o; }
            half4 f() : SV_Target { return half4(0, 1, 0, 0) * 0.5 + 0.5; }
            ENDHLSL
        }
    }
}
