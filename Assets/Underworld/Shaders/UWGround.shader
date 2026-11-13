// Underworld ground, rewrite 11-13 (owner: "too ugly"). Target: dark-fantasy ARPG ground (PoE2 / No Rest for the Wicked):
// big worn flagstones that break into dark earth, debris along the seams, strong cavity darkening, macro variation.
// Fab Megascans packed as  AH = albedo RGB + displacement A,  NRA = normal GL RG + roughness B + AO A:
//   Flag   flagstone paving (2 m scan, tiled ~6 m so slabs read 0.6-1.2 m)
//   Earth  burnt forest floor / ash (dark earth that fills the grout and eats the paving)
//   Rubble gravel on soil (debris band along the stone/earth seam)
//   Mud    second earth slot (damp dark dirt patches; generic, point it at a real mud scan later)
//   Cliff  triplanar sides
// Pipeline of the top surface:
//   1. domain-warped soft masks (ash coverage with area-density variation, damp earth)
//   2. hex-tile bombing of Flag and Earth (random rotation+offset per cell, height weighted) = no repetition
//   3. height blend: earth fills the grout first (GroutLevel), stones poke out; the stone/earth balance is shifted by the mask
//   4. seam: rubble where the blend is 50/50 (edge term), plus darkening band
//   5. cavity: low parts of stones dirty, tops worn lighter; texture AO
//   6. macro: 15-40 m brightness/hue/roughness, 4-8 m mid variation
//   7. wet: a few subtle damp zones, darker, smoothness capped (no white specular circles from spot lights)
// Texture samples on the top surface: Flag 6 + Earth 6 + Rubble 2 + Mud 2 = 16. Sides (Cliff 6) only run on non-up faces.
Shader "Underworld/Ground"
{
    Properties
    {
        _FlagAH("Flagstone albedo+height", 2D) = "grey" {}
        _FlagNRA("Flagstone normal+rough+AO", 2D) = "grey" {}
        _AshAH("Earth/ash albedo+height", 2D) = "grey" {}
        _AshNRA("Earth/ash normal+rough+AO", 2D) = "grey" {}
        _RubAH("Rubble albedo+height", 2D) = "grey" {}
        _RubNRA("Rubble normal+rough+AO", 2D) = "grey" {}
        _MudAH("Damp earth albedo+height", 2D) = "grey" {}
        _MudNRA("Damp earth normal+rough+AO", 2D) = "grey" {}
        _CliffAH("Cliff albedo+height", 2D) = "grey" {}
        _CliffNRA("Cliff normal+rough+AO", 2D) = "grey" {}
        _Tiles("Metres per tile (flag, earth, rubble, mud)", Vector) = (6, 4, 1.2, 7)
        _CliffTile("Cliff metres per tile", Float) = 4
        _HeightLo("Scan height low (flag, earth, rubble, mud)", Vector) = (0.28, 0.12, 0.28, 0.12)
        _HeightHi("Scan height high (flag, earth, rubble, mud)", Vector) = (0.66, 0.37, 0.69, 0.37)
        _FlagTint("Flagstone tint (luma lift)", Color) = (1.6, 1.6, 1.65, 1)
        _AshTint("Earth tint", Color) = (3.0, 2.8, 2.6, 1)
        _RubTint("Rubble tint", Color) = (1.5, 1.45, 1.4, 1)
        _MudTint("Damp earth tint", Color) = (2.2, 2.0, 1.85, 1)
        _CliffTint("Cliff tint", Color) = (2.6, 2.5, 2.4, 1)
        _Sat("Albedo saturation (fraction of scan)", Range(0, 1)) = 0.4
        _AshAmt("Earth coverage", Range(0, 1)) = 0.5
        _RubAmt("Debris along seams", Range(0, 1)) = 0.7
        _MudAmt("Damp earth patches", Range(0, 1)) = 0.5
        _GroutLevel("Earth level in the grout", Range(0, 1)) = 0.4
        _AshLift("Earth rise with coverage", Range(0, 0.6)) = 0.28
        _BlendDepth("Height blend depth", Range(0.02, 0.6)) = 0.14
        _SeamDark("Seam darkening", Range(0, 1)) = 0.45
        _Cavity("Cavity darkening of low stone", Range(0, 1)) = 0.6
        _NrmStr("Normal strength", Range(0, 2)) = 1.0
        _Macro("Macro variation", Range(0, 1)) = 0.5
        _AOStr("Texture AO strength", Range(0, 1)) = 0.9
        _SmoothScale("Dry smoothness scale", Range(0, 1)) = 0.7
        _WetAmt("Damp zones", Range(0, 1)) = 0.5
        _WetSmooth("Wet smoothness", Range(0, 1)) = 0.6
        _SmoothMax("Smoothness cap", Range(0, 1)) = 0.65
        _CrackAmt("Glowing soul cracks", Range(0, 1)) = 0.25
        [HDR] _CrackCol("Crack colour", Color) = (0.35, 0.9, 1.1, 1)
        _RimCol("Rim colour", Color) = (0.4, 0.5, 0.6, 1)
        _RimAmt("Rim amount", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "UWCommon.hlsl"
        TEXTURE2D(_FlagAH); TEXTURE2D(_FlagNRA);
        TEXTURE2D(_AshAH); TEXTURE2D(_AshNRA);
        TEXTURE2D(_RubAH); TEXTURE2D(_RubNRA);
        TEXTURE2D(_MudAH); TEXTURE2D(_MudNRA);
        TEXTURE2D(_CliffAH); TEXTURE2D(_CliffNRA);
        SAMPLER(sampler_FlagAH);
        #define SMP sampler_FlagAH
        CBUFFER_START(UnityPerMaterial)
        float4 _Tiles, _HeightLo, _HeightHi, _FlagTint, _AshTint, _RubTint, _MudTint, _CliffTint, _CrackCol, _RimCol;
        float _CliffTile, _Sat, _AshAmt, _RubAmt, _MudAmt, _GroutLevel, _AshLift, _BlendDepth, _SeamDark, _Cavity, _NrmStr, _Macro, _AOStr;
        float _SmoothScale, _WetAmt, _WetSmooth, _SmoothMax, _CrackAmt, _RimAmt;
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

            // one layer sample: albedo, normalised height 0..1, tangent-space normal xy (rotated back), roughness, ao
            struct L { float3 a; float h; float2 n; float r; float o; };

            L Unpack(float4 ah, float4 nra, float2x2 rotBack, float2 hr)
            {
                L l;
                l.a = ah.rgb;
                l.h = saturate((ah.a - hr.x) / max(hr.y - hr.x, 1e-3));
                l.n = mul(rotBack, nra.rg * 2.0 - 1.0);
                l.r = nra.b; l.o = nra.a;
                return l;
            }

            L Lerp(L x, L y, float t)
            {
                L l;
                l.a = lerp(x.a, y.a, t); l.h = lerp(x.h, y.h, t); l.n = lerp(x.n, y.n, t); l.r = lerp(x.r, y.r, t); l.o = lerp(x.o, y.o, t);
                return l;
            }
            L Mul(L x, float k) { L l; l.a = x.a * k; l.h = x.h * k; l.n = x.n * k; l.r = x.r * k; l.o = x.o * k; return l; }
            L Add(L x, L y) { L l; l.a = x.a + y.a; l.h = x.h + y.h; l.n = x.n + y.n; l.r = x.r + y.r; l.o = x.o + y.o; return l; }

            float2x2 Rot(float a) { float s, c; sincos(a, s, c); return float2x2(c, -s, s, c); }

            // random rotation + offset per cell; derivatives are passed in (computed once outside any branch)
            L SampleRot(TEXTURE2D(ah), TEXTURE2D(nra), float2 uv, float2 duvx, float2 duvy, float2 cell, float2 hr)
            {
                float2 rnd = UWHash22(cell);
                float2x2 R = Rot(rnd.x * 6.2831853);
                float2 suv = mul(R, uv) + rnd * 7.31;
                float2 dx = mul(R, duvx), dy = mul(R, duvy);
                return Unpack(SAMPLE_TEXTURE2D_GRAD(ah, SMP, suv, dx, dy), SAMPLE_TEXTURE2D_GRAD(nra, SMP, suv, dx, dy), transpose(R), hr);
            }

            // Hex-tile bombing (after Mikkelsen 2022): three overlapping hex cells, each with its own random rotation and
            // offset, weighted by barycentrics sharpened by the sampled height so seams follow stone edges, not lines.
            L Hex(TEXTURE2D(ah), TEXTURE2D(nra), float2 uv, float2 duvx, float2 duvy, float sharp, float2 hr)
            {
                float2 sk = mul(float2x2(1.0, -0.57735027, 0.0, 1.15470054), uv * 1.7320508);
                float2 b = floor(sk); float2 f = frac(sk);
                float z = 1.0 - f.x - f.y;
                float3 w; float2 v1, v2, v3;
                if (z > 0) { w = float3(z, f.y, f.x); v1 = b; v2 = b + float2(0, 1); v3 = b + float2(1, 0); }
                else { w = float3(-z, 1.0 - f.y, 1.0 - f.x); v1 = b + 1; v2 = b + float2(1, 0); v3 = b + float2(0, 1); }
                L s1 = SampleRot(ah, nra, uv, duvx, duvy, v1, hr), s2 = SampleRot(ah, nra, uv, duvx, duvy, v2, hr), s3 = SampleRot(ah, nra, uv, duvx, duvy, v3, hr);
                float3 hw = w * (float3(s1.h, s2.h, s3.h) * 1.6 + 0.3);
                hw = pow(max(hw, 1e-4), sharp);
                hw /= hw.x + hw.y + hw.z;
                return Add(Add(Mul(s1, hw.x), Mul(s2, hw.y)), Mul(s3, hw.z));
            }

            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float3 Desat(float3 c, float s) { return lerp(Luma(c).xxx, c, s); }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float top = smoothstep(0.7, 0.9, N.y);
                float2 p = ws.xz;
                float3 dwx = ddx(ws), dwy = ddy(ws);     // derivatives outside any branch (sampling uses _GRAD)

                float3 albedo; float3 n; float rough, ao, height = 0.5;
                float stoneW = 0, earthW = 0, edge = 0;

                // ------------------------------------------------------------- side faces: triplanar cliff
                float3 sideAlb = 0, sideN = N; float sideR = 0.8, sideAO = 1;
                if (top < 0.999)
                {
                    float3 tw = pow(abs(N), 4.0); tw /= tw.x + tw.y + tw.z;
                    float t = 1.0 / _CliffTile;
                    float2 uvx = ws.zy * t, uvy = ws.xz * t, uvz = ws.xy * t;
                    float4 ax = SAMPLE_TEXTURE2D_GRAD(_CliffAH, SMP, uvx, dwx.zy * t, dwy.zy * t);
                    float4 ay = SAMPLE_TEXTURE2D_GRAD(_CliffAH, SMP, uvy, dwx.xz * t, dwy.xz * t);
                    float4 az = SAMPLE_TEXTURE2D_GRAD(_CliffAH, SMP, uvz, dwx.xy * t, dwy.xy * t);
                    float4 nx = SAMPLE_TEXTURE2D_GRAD(_CliffNRA, SMP, uvx, dwx.zy * t, dwy.zy * t);
                    float4 ny = SAMPLE_TEXTURE2D_GRAD(_CliffNRA, SMP, uvy, dwx.xz * t, dwy.xz * t);
                    float4 nz = SAMPLE_TEXTURE2D_GRAD(_CliffNRA, SMP, uvz, dwx.xy * t, dwy.xy * t);
                    sideAlb = Desat(ax.rgb * tw.x + ay.rgb * tw.y + az.rgb * tw.z, _Sat) * _CliffTint.rgb;
                    // whiteout blend of the three planar normals
                    float2 tx = nx.rg * 2 - 1, ty = ny.rg * 2 - 1, tz = nz.rg * 2 - 1;
                    float3 wx = float3(0, tx.y, tx.x) * sign(N.x), wy = float3(ty.x, 0, ty.y), wz = float3(tz.x, tz.y, 0) * sign(N.z);
                    sideN = normalize(N + (wx * tw.x + wy * tw.y + wz * tw.z) * _NrmStr);
                    sideR = nx.b * tw.x + ny.b * tw.y + nz.b * tw.z;
                    sideAO = nx.a * tw.x + ny.a * tw.y + nz.a * tw.z;
                }

                // ------------------------------------------------------------- top surface
                float3 topAlb = 0, topN = float3(0, 1, 0); float topR = 0.8, topAO = 1, crackMask = 0, wetMask = 0;
                if (top > 0.001)
                {
                    // ---- 1. soft, domain-warped masks (everything below is smooth: no tiny-width smoothsteps)
                    float2 warp = float2(UWNoise(p * 0.06 + 3.1), UWNoise(p * 0.06 + 8.7)) - 0.5;
                    float2 q = p + warp * 16.0;                                         // large warp: organic outlines
                    q += (float2(UWNoise(p * 0.45 + 1.7), UWNoise(p * 0.45 + 5.9)) - 0.5) * 1.6;   // small warp: ragged edge
                    float m1 = UWFbm(p * 0.03 + 2.7);                                   // ~33 m macro
                    float n1 = saturate((m1 - 0.25) / 0.5);
                    float m2 = UWNoise(p * 0.2 + 13.0);                                 // ~5 m mid
                    float density = UWNoise(p * 0.02 + 40.0) - 0.5;                     // 50 m: some areas stay paved, some are mostly earth
                    float earthN = UWFbm(q * 0.045 + 7.3) + (UWNoise(q * 0.35) - 0.5) * 0.2;
                    float thr = 0.62 - _AshAmt * 0.25 - density * 0.35;
                    float ash = smoothstep(thr - 0.13, thr + 0.13, earthN);             // 0 = paved, 1 = earth with a few stones poking out
                    float mudM = smoothstep(0.5, 0.78, UWFbm(q * 0.08 + 31.7)) * _MudAmt;

                    // ---- 2. layers
                    float2 dpx = dwx.xz, dpy = dwy.xz;
                    float2 uf = p / _Tiles.x, ue = p / _Tiles.y, ur = p / _Tiles.z, um = p / _Tiles.w;
                    L lf = Hex(_FlagAH, _FlagNRA, uf, dpx / _Tiles.x, dpy / _Tiles.x, 3.0, float2(_HeightLo.x, _HeightHi.x));
                    L la = Hex(_AshAH, _AshNRA, ue, dpx / _Tiles.y, dpy / _Tiles.y, 2.0, float2(_HeightLo.y, _HeightHi.y));
                    L lr = SampleRot(_RubAH, _RubNRA, ur, dpx / _Tiles.z, dpy / _Tiles.z, floor(p / (_Tiles.z * 3.0)), float2(_HeightLo.z, _HeightHi.z));
                    L lm = SampleRot(_MudAH, _MudNRA, um, dpx / _Tiles.w, dpy / _Tiles.w, floor(p / (_Tiles.w * 3.0)) + 17, float2(_HeightLo.w, _HeightHi.w));

                    lf.a = Desat(lf.a, _Sat) * _FlagTint.rgb;
                    la.a = Desat(la.a, _Sat) * _AshTint.rgb;
                    lr.a = Desat(lr.a, _Sat) * _RubTint.rgb;
                    lm.a = Desat(lm.a, _Sat) * _MudTint.rgb;
                    la.o = lerp(1.0, la.o, 0.6);       // the earth scan's AO is very dark; keep it from crushing the floor
                    lm.o = lerp(1.0, lm.o, 0.6);

                    // cavity on the stones: dirt gathers in the low parts, the proud tops are worn lighter
                    float cav = lerp(1.0 - _Cavity * 0.75, 1.0, smoothstep(0.1, 0.6, lf.h));
                    lf.a *= cav * (1.0 + 0.18 * smoothstep(0.6, 0.95, lf.h));

                    // damp/dark earth patches blend softly into the base earth (by mask, nudged by relative height)
                    float mw = saturate(mudM * 1.4 + (lm.h - la.h) * 0.25 * mudM);
                    L le = Lerp(la, lm, mw);

                    // ---- 3. height blend stone vs earth: earth fills the grout first, stones poke out as coverage rises
                    float hS = lf.h + (1.0 - ash) * _AshLift;
                    float hE = _GroutLevel + le.h * 0.2 + ash * _AshLift;
                    float hmax = max(hS, hE) - _BlendDepth;
                    float2 bw = max(float2(hS, hE) - hmax, 0.0);
                    bw /= max(bw.x + bw.y, 1e-4);
                    stoneW = bw.x; earthW = bw.y;
                    L g = Lerp(le, lf, stoneW);

                    // ---- 4. seam: debris + darkening where stone and earth meet
                    edge = 4.0 * stoneW * (1.0 - stoneW);
                    float rubN = UWNoise(p * 1.1 + 9.0);
                    float rubW = smoothstep(0.2, 0.8, edge * _RubAmt * (0.55 + rubN) + (lr.h - 0.5) * 0.6 * saturate(edge * 2.0));
                    g = Lerp(g, lr, rubW);
                    g.a *= 1.0 - _SeamDark * edge * (1.0 - rubW * 0.5);

                    // ---- 6. macro variation: 15-40 m brightness / hue / roughness, 4-8 m mid brightness
                    float bright = 1.0 + _Macro * ((n1 - 0.5) * 1.1 + (m2 - 0.5) * 0.7);
                    g.a *= bright;
                    g.a *= lerp(float3(1.05, 1.0, 0.93), float3(0.94, 0.99, 1.06), UWNoise(p * 0.035 + 5.0));   // warm/cool drift
                    g.r = saturate(g.r + (n1 - 0.5) * 0.25 * _Macro);
                    g.a = min(g.a, 0.32);   // hard luma-ish ceiling: nothing on the floor should ever bloom

                    topAlb = g.a;
                    float2 tn = g.n * _NrmStr;
                    topN = normalize(float3(tn.x, sqrt(saturate(1.0 - dot(g.n, g.n))), tn.y));
                    topR = g.r; topAO = g.o; height = g.h;
                    crackMask = pow(edge, 3.0) * (1.0 - ash);

                    // ---- 7. a few subtle damp zones (low spots, mostly earth); computed here so it can darken albedo
                    float wetN = UWFbm(q * 0.11 + 3.1) + (0.45 - height) * 0.12 + earthW * 0.05;
                    wetMask = smoothstep(0.64, 0.86, wetN) * _WetAmt;
                }

                albedo = lerp(sideAlb, topAlb, top);
                n = normalize(lerp(sideN, topN, top));
                rough = lerp(sideR, topR, top);
                ao = lerp(1.0, lerp(sideAO, topAO, top), _AOStr);

                float wet = wetMask * top;
                albedo *= lerp(1.0, 0.68, wet);
                float dry = saturate(1.0 - rough) * _SmoothScale;
                float wetS = min(dry + 0.28, _WetSmooth);                      // damp = a bit smoother, never mirror-like
                float smoothness = lerp(dry, wetS, wet);
                smoothness = min(smoothness, _SmoothMax);                            // hard cap: no hot white circles under spot lights
                n = normalize(lerp(n, float3(0, 1, 0), wet * 0.35 * top));

                // soul cracks: faint cold glow in the grout line of a few paving clusters
                float3 emission = 0;
                if (_CrackAmt > 0.001 && top > 0.001)
                {
                    float clN = UWFbm(p * 0.13 + 5.3);
                    float cluster = smoothstep(0.7 - _CrackAmt * 0.15, 0.88 - _CrackAmt * 0.15, clN) * crackMask * top;
                    float breathe = 0.55 + 0.45 * sin(_UWTime * 0.6 + UWNoise(p * 0.3) * 6.28);
                    emission = _CrackCol.rgb * cluster * breathe * 0.4;
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
                s.smoothness = smoothness;
                s.occlusion = ao;
                s.emission = emission;
                s.alpha = 1;
                s.normalTS = float3(0, 0, 1);
                s.specular = 0;
                s.metallic = 0;

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
