// 대전 앞 court floor (11-13). Kido: one rock texture repeating at one scale read as a wallpaper.
// The paving is now irregular granite slabs (박석) built in the shader: every slab picks one of three scans,
// a random rotation/offset and its own tone and tilt, so no two neighbours repeat. On top, world-space masks at
// different scales add moss in the seams, dirt wash, missing slabs filled with gravel, cracks, fallen leaves,
// puddles and fire scorch. Flat colour only: nothing here changes the walkable height.
// Vertex colour (LookStudyRuins.BuildPalace): G = trampled dirt outside the court, B = paving, A = ash/scorch.
Shader "LookStudy/PalaceGround"
{
    Properties
    {
        _SlabA("Slab scan A", 2D) = "gray" {}
        _SlabAN("Slab A normal", 2D) = "bump" {}
        _SlabB("Slab scan B", 2D) = "gray" {}
        _SlabBN("Slab B normal", 2D) = "bump" {}
        _SlabC("Slab scan C", 2D) = "gray" {}
        _Gravel("Gravel", 2D) = "gray" {}
        _GravelN("Gravel normal", 2D) = "bump" {}
        _Leaves("Leaves", 2D) = "gray" {}
        _LeavesN("Leaves normal", 2D) = "bump" {}
        _Moss("Moss / forest floor", 2D) = "gray" {}
        _Mud("Mud wash", 2D) = "gray" {}
        _Dirt("Outside dirt", 2D) = "gray" {}
        _DirtN("Outside dirt normal", 2D) = "bump" {}
        _Ash("Scorch", 2D) = "gray" {}
        _Granite("Granite tint", Color) = (0.6, 0.58, 0.55, 1)
        _MossTint("Moss tint", Color) = (0.36, 0.38, 0.3, 1)
        _DirtTint("Dirt tint", Color) = (0.62, 0.58, 0.55, 1)
        _SlabSize("Slab size (m)", Vector) = (1.3, 0.95, 0, 0)
        _MossAmt("Moss amount", Range(0,1)) = 0.5
        _LeafAmt("Leaves amount", Range(0,1)) = 0.5
        _PuddleAmt("Puddle amount", Range(0,1)) = 0.5
        _MissingAmt("Missing slabs", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSStage.hlsl"
        TEXTURE2D(_SlabA); SAMPLER(sampler_SlabA); TEXTURE2D(_SlabAN);
        TEXTURE2D(_SlabB); TEXTURE2D(_SlabBN); TEXTURE2D(_SlabC);
        TEXTURE2D(_Gravel); TEXTURE2D(_GravelN);
        TEXTURE2D(_Leaves); TEXTURE2D(_LeavesN);
        TEXTURE2D(_Moss); TEXTURE2D(_Mud);
        TEXTURE2D(_Dirt); TEXTURE2D(_DirtN); TEXTURE2D(_Ash);
        CBUFFER_START(UnityPerMaterial)
        float4 _Granite, _MossTint, _DirtTint, _SlabSize;
        float _MossAmt, _LeafAmt, _PuddleAmt, _MissingAmt;
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 screen : TEXCOORD2; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.color = a.color;
                return o;
            }

            #define S(t, uv) SAMPLE_TEXTURE2D(t, sampler_SlabA, uv)
            float2 NorXZ(float4 packed, float k) { float3 t = UnpackNormal(packed); return t.xy * k; }

            // Jittered cells with the true distance to the cell border (not F2-F1), so seams have even width.
            void Slabs(float2 p, float jitter, out float2 id, out float edge, out float2 toCentre)
            {
                float2 n = floor(p), f = frac(p), mg = 0, mr = 0;
                float md = 8.0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 r = g + 0.5 + (LSHash2(n + g) - 0.5) * jitter - f;
                    float d = dot(r, r);
                    if (d < md) { md = d; mr = r; mg = g; }
                }
                md = 8.0;
                for (int j2 = -2; j2 <= 2; j2++)
                for (int i2 = -2; i2 <= 2; i2++)
                {
                    float2 g = mg + float2(i2, j2);
                    float2 r = g + 0.5 + (LSHash2(n + g) - 0.5) * jitter - f;
                    float2 dr = r - mr;
                    if (dot(dr, dr) > 1e-5) md = min(md, dot(0.5 * (mr + r), normalize(dr)));
                }
                id = n + mg; edge = md; toCentre = mr;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float2 xz = ws.xz;
                float2 dx = ddx(xz), dy = ddy(xz);

                // ---- slab layout. The royal path (어도, |x| < 3.6) gets long, calmer slabs; the court gets irregular ones.
                bool road = abs(xz.x) < 3.6 && xz.y > -48.0 && xz.y < 43.0;
                float2 size = road ? float2(1.75, 0.9) : _SlabSize.xy;
                float2 warp = (float2(LSNoise(xz * 1.3), LSNoise(xz * 1.3 + 7.7)) - 0.5) * 0.09;
                float2 sp = (xz + warp) / size;
                float2 id, toC; float edge;
                Slabs(sp, road ? 0.35 : 0.8, id, edge, toC);
                edge *= min(size.x, size.y);                      // metres to the slab border
                float h1 = LSHash(id), h2 = LSHash(id + 7.31), h3 = LSHash(id + 19.17), h4 = LSHash(id + 31.73), h5 = LSHash(id + 47.9);

                // Each slab: one of three scans, a quarter-turn rotation and a random offset.
                float ang = floor(h2 * 4.0) * 1.5707963 + (h3 - 0.5) * 0.3;
                float cs = cos(ang), sn = sin(ang);
                float2x2 rot = float2x2(cs, -sn, sn, cs);
                float ts = 0.32 + h4 * 0.12;
                float2 suv = mul(rot, xz * ts) + h3 * 17.0 + h5 * 5.0;
                float2 sdx = mul(rot, dx * ts), sdy = mul(rot, dy * ts);
                float3 stex; float2 sn2;
                if (h1 < 0.45)      { stex = SAMPLE_TEXTURE2D_GRAD(_SlabA, sampler_SlabA, suv, sdx, sdy).rgb; sn2 = NorXZ(SAMPLE_TEXTURE2D_GRAD(_SlabAN, sampler_SlabA, suv, sdx, sdy), 0.8); }
                else if (h1 < 0.78) { stex = SAMPLE_TEXTURE2D_GRAD(_SlabB, sampler_SlabA, suv, sdx, sdy).rgb; sn2 = NorXZ(SAMPLE_TEXTURE2D_GRAD(_SlabBN, sampler_SlabA, suv, sdx, sdy), 0.8); }
                else                { stex = SAMPLE_TEXTURE2D_GRAD(_SlabC, sampler_SlabA, suv, sdx, sdy).rgb; sn2 = NorXZ(SAMPLE_TEXTURE2D_GRAD(_SlabAN, sampler_SlabA, suv * 0.7, sdx * 0.7, sdy * 0.7), 0.6); }
                sn2 = mul(transpose(rot), sn2);
                float sl = dot(stex, float3(0.3, 0.59, 0.11));
                float3 slab = lerp(stex, _Granite.rgb * saturate(sl * 2.2), 0.7);
                // 11-14 overnight review: near-white slabs next to near-black ones were too busy and dark enemies vanished on them;
                // each scan is pulled halfway toward one mid grey, and the per-slab tone range is narrower (was 0.74 + 0.4)
                slab *= lerp(1.0, 0.17 / max(dot(slab, float3(0.3, 0.59, 0.11)), 0.03), 0.45);
                slab *= 0.8 + h5 * 0.26;                                        // per-slab tone
                slab *= lerp(float3(1.04, 1.0, 0.95), float3(0.95, 0.99, 1.04), h4);   // warm / cool stones
                slab *= 0.82 + LSFbm(xz * 0.025 + 3.0) * 0.36;                 // big soft light/dark masses

                // worn: the slab centre is a little lighter and smoother, edges darker and chipped
                float rim = 1.0 - smoothstep(0.0, 0.16, edge + (LSNoise(xz * 9.0) - 0.5) * 0.05);
                slab *= 1.0 - rim * 0.28;

                // cracks across about a third of the slabs
                float crackCell = step(0.66, LSHash(id + 3.9));
                float2 cv = LSVoronoi(xz * 2.4 + id * 3.7);
                float crack = crackCell * smoothstep(0.035, 0.0, cv.y) * smoothstep(0.35, 0.6, LSNoise(xz * 1.6 + id));
                slab *= 1.0 - crack * 0.6;

                // seams: dark grit, wider and greener where moss grows
                float mossBig = smoothstep(0.42, 0.72, LSFbm(mul(float2x2(0.8, -0.6, 0.6, 0.8), xz) * 0.05 + 11.0) + (_MossAmt - 0.5) * 0.4);
                float seamW = 0.025 + mossBig * 0.05;
                float seam = 1.0 - smoothstep(seamW * 0.4, seamW, edge + (LSNoise(xz * 6.0) - 0.5) * 0.02);
                float3 grit = S(_Mud, xz * 0.9).rgb * 0.32;
                float3 mossC = S(_Moss, xz * 0.55).rgb * _MossTint.rgb * 1.5;
                float3 seamCol = lerp(grit, mossC * 0.8, smoothstep(0.2, 0.8, mossBig));

                // missing slabs filled with gravel / packed dirt, a few alone and more in damaged patches
                float missField = smoothstep(0.62, 0.82, LSFbm(xz * 0.04 + 27.0)) * 0.35;
                float missing = step(LSHash(id + 61.3), (0.035 + missField) * (_MissingAmt * 2.0)) * (road ? 0.0 : 1.0);
                float3 gravel = S(_Gravel, xz * 0.42).rgb * _DirtTint.rgb * 0.95;
                float2 gravelN = NorXZ(S(_GravelN, xz * 0.42), 1.0);

                float3 albedo = lerp(slab, seamCol, seam);
                float2 dn = sn2 * (1.0 - seam);
                // slab tilt (settled stones) and a bevel toward the seam
                dn += (float2(h3, h5) - 0.5) * 0.12 * (1.0 - seam);
                dn += -normalize(toC + 1e-4) * rim * 0.25 * (1.0 - seam);
                float ao = lerp(1.0, 0.55, seam) * (1.0 - rim * 0.15);
                albedo = lerp(albedo, gravel, missing);
                dn = lerp(dn, gravelN, missing);
                ao = lerp(ao, 0.75, missing);

                // moss creeping over slab edges inside the moss patches
                float creep = mossBig * smoothstep(0.55, 0.0, edge / 0.25 + (LSFbm(xz * 2.2) - 0.5) * 1.2) * (1.0 - missing);
                albedo = lerp(albedo, mossC, saturate(creep));

                // dirt wash (mud carried by rain), strongest toward the galleries
                float edgeCourt = smoothstep(30.0, 52.0, abs(xz.x)) + smoothstep(30.0, 45.0, xz.y) + smoothstep(-30.0, -48.0, xz.y);
                float wash = smoothstep(0.5, 0.82, LSFbm(xz * 0.06 + 41.0) + edgeCourt * 0.18);
                float3 mud = S(_Mud, xz * 0.3).rgb * _DirtTint.rgb;
                albedo = lerp(albedo, mud, wash * 0.65);

                // fallen leaves in drifts: corners and the foot of the galleries, a few loose patches in the court
                float leafN = LSFbm(xz * 0.09 + 73.0) + edgeCourt * 0.25 + (_LeafAmt - 0.5) * 0.3;
                float leaf = smoothstep(0.66, 0.8, leafN) * smoothstep(0.35, 0.6, LSNoise(xz * 1.1 + 5.0));
                float3 leaves = S(_Leaves, xz * 0.38).rgb * float3(0.68, 0.62, 0.58);
                albedo = lerp(albedo, leaves, leaf);
                dn = lerp(dn, NorXZ(S(_LeavesN, xz * 0.38), 0.9), leaf);

                // ---- outside the court: trampled dirt (vertex G)
                float brk = (LSFbm(xz * 0.4) - 0.5) * 0.5;
                float dirtM = smoothstep(0.35, 0.65, i.color.g + brk);
                float3 dirt = S(_Dirt, xz * 0.26).rgb * _DirtTint.rgb;
                dirt = lerp(dirt, S(_Leaves, xz * 0.3).rgb * 0.62, smoothstep(0.55, 0.75, LSFbm(xz * 0.07 + 9.0)) * 0.7);
                albedo = lerp(albedo, dirt, dirtM);
                dn = lerp(dn, NorXZ(S(_DirtN, xz * 0.26), 0.8), dirtM);
                ao = lerp(ao, 1.0, dirtM);

                // ---- scorch from the fires (vertex A): soot that follows the stone, not a flat decal
                float scorch = smoothstep(0.3, 0.75, i.color.a + brk);
                float3 ash = S(_Ash, xz * 0.21).rgb * 0.55;
                albedo = lerp(albedo, ash * lerp(1.0, 0.6, seam), scorch * 0.85);

                // ---- puddles: rain gathered in the low slabs, flat dark mirrors. Never on the outside dirt.
                float pN = LSFbm(xz * 0.075 + 91.0) + (_PuddleAmt - 0.5) * 0.2 + (LSNoise(xz * 0.9) - 0.5) * 0.08;
                float puddle = smoothstep(0.7, 0.735, pN) * (1.0 - dirtM) * (1.0 - leaf);
                float damp = smoothstep(0.6, 0.72, pN) * (1.0 - dirtM);
                albedo *= lerp(1.0, 0.7, damp);
                albedo = lerp(albedo, albedo * 0.38, puddle);
                dn = lerp(dn, 0, puddle);
                float gloss = lerp(0.1, 0.3, damp) * (1.0 - leaf);
                gloss = lerp(gloss, 0.85, puddle);

                float3 n = normalize(N + float3(dn.x, 0, dn.y));
                float3 col = StageLight(albedo, n, ws, i.screen.xy / i.screen.w, gloss, ao);
                col = StageFog(col, ws);
                return half4(col, 1);
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
