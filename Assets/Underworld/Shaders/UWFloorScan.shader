// Underworld floor, rebuilt 2026-11-15 (ref: PoE2 volcanic ground - dark basalt, lava sand in the hollows, no drawn lines).
// Scan layers only (Fab / Quixel Megascans), packed AH = albedo RGB + height A, NRA = normal GL RG + roughness B + AO A:
//   Basalt  Hawaiian Lava Stone (4 m)   main rock, hex-tile bombed so it never repeats
//   Sand    Icelandic Lava Sand (2 m)   dark sand filling the low parts of the rock, large soft fields
//   Ripple  Lava Stone Ripple (2 m)     ropey pahoehoe rock in a few big regions
//   Ash     Powdered Ash (0.36 m scan)  faint grey ash dusting in rare patches
// Nothing here draws a crack, vein, rift or glowing line: every line on the floor comes from the scans themselves.
// Layer masks are wide, domain-warped gradient noise blended by the scan heights, so borders follow the stone shapes.
// 19:10 Kido: "can't you put lava on that volcanic floor?" - lava pools from the downloaded lava texture (same set as the
// lava sea, Fab "Lava Texture Vol.04" CC BY) settle in the LOW parts of the basalt scan, in big regions that grow toward
// the plaza rim. Its glow is the texture's own emissive map; nothing is drawn by code.
Shader "Underworld/FloorScan"
{
    Properties
    {
        _BasaltAH("Basalt albedo+height", 2D) = "grey" {}
        _BasaltNRA("Basalt normal+rough+AO", 2D) = "grey" {}
        _SandAH("Lava sand albedo+height", 2D) = "grey" {}
        _SandNRA("Lava sand normal+rough+AO", 2D) = "grey" {}
        _RippleAH("Ripple rock albedo+height", 2D) = "grey" {}
        _RippleNRA("Ripple rock normal+rough+AO", 2D) = "grey" {}
        _AshAH("Ash albedo+height", 2D) = "grey" {}
        _AshNRA("Ash normal+rough+AO", 2D) = "grey" {}
        _Tiles("Metres per tile (basalt, sand, ripple, ash)", Vector) = (5, 2.5, 3, 1.2)
        _CliffTile("Cliff metres per tile", Float) = 4
        _BasaltTint("Basalt tint", Color) = (0.55, 0.53, 0.52, 1)
        _SandTint("Sand tint", Color) = (0.9, 0.86, 0.84, 1)
        _RippleTint("Ripple tint", Color) = (0.6, 0.58, 0.58, 1)
        _AshTint("Ash tint", Color) = (0.42, 0.4, 0.4, 1)
        _CliffTint("Cliff tint", Color) = (0.45, 0.43, 0.42, 1)
        _Sat("Albedo saturation (fraction of scan)", Range(0, 1)) = 0.35
        _SandAmt("Sand coverage", Range(0, 1)) = 0.45
        _RippleAmt("Ripple rock regions", Range(0, 1)) = 0.35
        _AshAmt("Ash dusting", Range(0, 1)) = 0.25
        _BlendDepth("Height blend depth", Range(0.02, 0.6)) = 0.18
        _NrmStr("Normal strength", Range(0, 2)) = 1.0
        _Macro("Macro brightness variation", Range(0, 1)) = 0.45
        _AOStr("Texture AO strength", Range(0, 1)) = 0.85
        _SmoothScale("Smoothness scale", Range(0, 1)) = 0.45
        _SmoothMax("Smoothness cap", Range(0, 1)) = 0.35
        _AlbedoMax("Albedo ceiling", Range(0.05, 1)) = 0.3
        _Contrast("Albedo contrast (1 = scan)", Range(0, 1.5)) = 0.6
        _CavityDark("Darken low parts of the scan", Range(0, 1)) = 0.55
        _AmbLift("Flat ambient lift (no pattern)", Color) = (0.1, 0.095, 0.1, 1)
        _LavaAH("Lava basecolor+height", 2D) = "grey" {}
        _LavaE("Lava emissive", 2D) = "black" {}
        _LavaTile("Lava metres per tile", Float) = 10
        _LavaAmt("Lava pool coverage", Range(0, 1)) = 0.4
        _LavaEdge("Extra lava toward the plaza rim (m)", Float) = 14
        _PlazaHalf("Plaza centre xz, half size xz", Vector) = (0, -1, 44, 28)
        [HDR] _LavaCol("Lava emission colour", Color) = (1, 0.5, 0.25, 1)
        _LavaInt("Lava emission intensity", Range(0, 12)) = 4
        _LavaTint("Lava crust tint", Color) = (0.35, 0.3, 0.28, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "UWCommon.hlsl"
        TEXTURE2D(_BasaltAH); TEXTURE2D(_BasaltNRA);
        TEXTURE2D(_SandAH); TEXTURE2D(_SandNRA);
        TEXTURE2D(_RippleAH); TEXTURE2D(_RippleNRA);
        TEXTURE2D(_AshAH); TEXTURE2D(_AshNRA);
        TEXTURE2D(_LavaAH); TEXTURE2D(_LavaE);
        SAMPLER(sampler_BasaltAH);
        #define SMP sampler_BasaltAH
        CBUFFER_START(UnityPerMaterial)
        float4 _Tiles, _BasaltTint, _SandTint, _RippleTint, _AshTint, _CliffTint, _AmbLift, _PlazaHalf, _LavaCol, _LavaTint;
        float _LavaTile, _LavaAmt, _LavaEdge, _LavaInt;
        float _CliffTile, _Sat, _SandAmt, _RippleAmt, _AshAmt, _BlendDepth, _NrmStr, _Macro, _AOStr, _SmoothScale, _SmoothMax, _AlbedoMax, _Contrast, _CavityDark;
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

            struct L { float3 a; float h; float2 n; float r; float o; };

            L Unpack(float4 ah, float4 nra, float2x2 rotBack)
            {
                L l; l.a = ah.rgb; l.h = ah.a; l.n = mul(rotBack, nra.rg * 2.0 - 1.0); l.r = nra.b; l.o = nra.a; return l;
            }
            L Lerp(L x, L y, float t)
            {
                L l; l.a = lerp(x.a, y.a, t); l.h = lerp(x.h, y.h, t); l.n = lerp(x.n, y.n, t); l.r = lerp(x.r, y.r, t); l.o = lerp(x.o, y.o, t); return l;
            }
            L Mul(L x, float k) { L l; l.a = x.a * k; l.h = x.h * k; l.n = x.n * k; l.r = x.r * k; l.o = x.o * k; return l; }
            L Add(L x, L y) { L l; l.a = x.a + y.a; l.h = x.h + y.h; l.n = x.n + y.n; l.r = x.r + y.r; l.o = x.o + y.o; return l; }
            float2x2 Rot(float a) { float s, c; sincos(a, s, c); return float2x2(c, -s, s, c); }

            L SampleRot(TEXTURE2D(ah), TEXTURE2D(nra), float2 uv, float2 duvx, float2 duvy, float2 cell)
            {
                float2 rnd = UWHash22(cell);
                float2x2 R = Rot(rnd.x * 6.2831853);
                float2 suv = mul(R, uv) + rnd * 7.31;
                float2 dx = mul(R, duvx), dy = mul(R, duvy);
                return Unpack(SAMPLE_TEXTURE2D_GRAD(ah, SMP, suv, dx, dy), SAMPLE_TEXTURE2D_GRAD(nra, SMP, suv, dx, dy), transpose(R));
            }

            // hex-tile bombing: three overlapping hex cells with random rotation/offset, weights sharpened by scan height
            L Hex(TEXTURE2D(ah), TEXTURE2D(nra), float2 uv, float2 duvx, float2 duvy, float sharp)
            {
                float2 sk = mul(float2x2(1.0, -0.57735027, 0.0, 1.15470054), uv * 1.7320508);
                float2 b = floor(sk); float2 f = frac(sk);
                float z = 1.0 - f.x - f.y;
                float3 w; float2 v1, v2, v3;
                if (z > 0) { w = float3(z, f.y, f.x); v1 = b; v2 = b + float2(0, 1); v3 = b + float2(1, 0); }
                else { w = float3(-z, 1.0 - f.y, 1.0 - f.x); v1 = b + 1; v2 = b + float2(1, 0); v3 = b + float2(0, 1); }
                L s1 = SampleRot(ah, nra, uv, duvx, duvy, v1), s2 = SampleRot(ah, nra, uv, duvx, duvy, v2), s3 = SampleRot(ah, nra, uv, duvx, duvy, v3);
                float3 hw = w * (float3(s1.h, s2.h, s3.h) * 1.6 + 0.3);
                hw = pow(max(hw, 1e-4), sharp);
                hw /= hw.x + hw.y + hw.z;
                return Add(Add(Mul(s1, hw.x), Mul(s2, hw.y)), Mul(s3, hw.z));
            }

            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float3 Desat(float3 c, float s) { return lerp(Luma(c).xxx, c, s); }
            // squeeze the scan's light blotches (lichen, dust) toward the mid tone; the relief then comes from the normals
            float3 Squash(float3 c, float mid) { return max(lerp(mid.xxx, c, _Contrast), 0.0); }

            // gradient noise 0..1 (no lattice-aligned contours)
            float GN(float2 x)
            {
                x = mul(float2x2(0.8, -0.6, 0.6, 0.8), x);
                float2 i = floor(x), f = frac(x);
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = dot(UWHash22(i) * 2.0 - 1.0, f);
                float b = dot(UWHash22(i + float2(1, 0)) * 2.0 - 1.0, f - float2(1, 0));
                float c = dot(UWHash22(i + float2(0, 1)) * 2.0 - 1.0, f - float2(0, 1));
                float d = dot(UWHash22(i + float2(1, 1)) * 2.0 - 1.0, f - float2(1, 1));
                return saturate(0.5 + 0.9 * lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y));
            }
            float GN2(float2 x) { return GN(mul(float2x2(0.5, -0.866, 0.866, 0.5), x)); }
            float Field(float2 q, float s, float o) { return 0.65 * GN(q * s + o) + 0.35 * GN2(q * s * 2.3 + o * 1.7); }

            // height blend: layer y covers x where its mask plus its height beats the rock height
            float HB(float hx, float hy, float m, float depth)
            {
                return smoothstep(-depth, depth, (m * 2.0 - 1.0) + (hy - hx) * 0.8);
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float2 p = ws.xz;
                float3 dwx = ddx(ws), dwy = ddy(ws);
                float3 Ng = normalize(cross(dwx, dwy));
                float top = smoothstep(0.35, 0.6, abs(Ng.y));
                N = normalize(lerp(N, float3(0, 1, 0), top));

                // ---- sides: triplanar basalt
                float3 sideAlb = 0, sideN = N; float sideR = 0.85, sideAO = 1;
                if (top < 0.999)
                {
                    float3 tw = pow(abs(N), 4.0); tw /= tw.x + tw.y + tw.z;
                    float t = 1.0 / _CliffTile;
                    float2 uvx = ws.zy * t, uvy = ws.xz * t, uvz = ws.xy * t;
                    float4 ax = SAMPLE_TEXTURE2D_GRAD(_BasaltAH, SMP, uvx, dwx.zy * t, dwy.zy * t);
                    float4 ay = SAMPLE_TEXTURE2D_GRAD(_BasaltAH, SMP, uvy, dwx.xz * t, dwy.xz * t);
                    float4 az = SAMPLE_TEXTURE2D_GRAD(_BasaltAH, SMP, uvz, dwx.xy * t, dwy.xy * t);
                    float4 nx = SAMPLE_TEXTURE2D_GRAD(_BasaltNRA, SMP, uvx, dwx.zy * t, dwy.zy * t);
                    float4 ny = SAMPLE_TEXTURE2D_GRAD(_BasaltNRA, SMP, uvy, dwx.xz * t, dwy.xz * t);
                    float4 nz = SAMPLE_TEXTURE2D_GRAD(_BasaltNRA, SMP, uvz, dwx.xy * t, dwy.xy * t);
                    sideAlb = Desat(ax.rgb * tw.x + ay.rgb * tw.y + az.rgb * tw.z, _Sat) * _CliffTint.rgb;
                    float2 tx = nx.rg * 2 - 1, ty = ny.rg * 2 - 1, tz = nz.rg * 2 - 1;
                    float3 wx = float3(0, tx.y, tx.x) * sign(N.x), wy = float3(ty.x, 0, ty.y), wz = float3(tz.x, tz.y, 0) * sign(N.z);
                    sideN = normalize(N + (wx * tw.x + wy * tw.y + wz * tw.z) * _NrmStr);
                    sideR = nx.b * tw.x + ny.b * tw.y + nz.b * tw.z;
                    sideAO = nx.a * tw.x + ny.a * tw.y + nz.a * tw.z;
                }

                // ---- top
                float3 topAlb = 0, topN = float3(0, 1, 0); float topR = 0.85, topAO = 1; float3 lavaEm = 0;
                if (top > 0.001)
                {
                    float2 dpx = dwx.xz, dpy = dwy.xz;
                    // domain warp so every field edge is organic
                    float2 warp = float2(GN(p * 0.05 + 3.1), GN2(p * 0.05 + 8.7)) - 0.5;
                    float2 q = p + warp * 14.0;

                    float2 ub = p / _Tiles.x;
                    L g = Hex(_BasaltAH, _BasaltNRA, ub, dpx / _Tiles.x, dpy / _Tiles.x, 1.4);
                    g.a = Squash(Desat(g.a, _Sat), 0.3) * _BasaltTint.rgb;

                    // ripple rock: a few big regions (~35 m) replacing the basalt
                    float rReg = smoothstep(0.62 - _RippleAmt * 0.25, 0.78 - _RippleAmt * 0.25, Field(q, 0.028, 41.0));
                    if (rReg > 0.01)
                    {
                        float2 ur = p / _Tiles.z;
                        L lr = Hex(_RippleAH, _RippleNRA, ur, dpx / _Tiles.z, dpy / _Tiles.z, 1.4);
                        lr.a = Squash(Desat(lr.a, _Sat), 0.28) * _RippleTint.rgb;
                        g = Lerp(g, lr, HB(g.h, lr.h, rReg, _BlendDepth * 1.5));
                    }

                    // lava sand: settles in the hollows, wide fields (~20 m) with ragged borders following the rock height
                    float sField = Field(q, 0.045, 7.0) * 0.75 + GN(q * 0.22 + 19.0) * 0.25;
                    float sReg = smoothstep(0.66 - _SandAmt * 0.4, 0.84 - _SandAmt * 0.4, sField);
                    if (sReg > 0.01)
                    {
                        float2 us = p / _Tiles.y;
                        L ls = Hex(_SandAH, _SandNRA, us, dpx / _Tiles.y, dpy / _Tiles.y, 1.2);
                        ls.a = Squash(Desat(ls.a, _Sat), 0.13) * _SandTint.rgb;
                        float sandW = HB(g.h, 0.5, sReg, _BlendDepth);   // (0.5 - rock height): fills where the rock is low
                        g = Lerp(g, ls, sandW);
                    }

                    // ash: rare faint grey dusting
                    float aReg = smoothstep(0.7, 0.85, Field(q, 0.06, 77.0)) * _AshAmt;
                    if (aReg > 0.01)
                    {
                        float2 ua = p / _Tiles.w;
                        L la = Hex(_AshAH, _AshNRA, ua, dpx / _Tiles.w, dpy / _Tiles.w, 1.0);
                        la.a = Desat(la.a, _Sat * 0.5) * _AshTint.rgb;
                        g = Lerp(g, la, saturate(HB(g.h, la.h, aReg, _BlendDepth)) * 0.85);
                    }

                    // lava pools: big regions, denser toward the plaza rim, filling only where the rock is low
                    float2 pe = _PlazaHalf.zw - abs(p - _PlazaHalf.xy);
                    float rim = saturate(1.0 - min(pe.x, pe.y) / max(_LavaEdge, 0.1));
                    float lField = Field(q, 0.04, 113.0) * 0.8 + GN(q * 0.15 + 31.0) * 0.2 + rim * 0.22;
                    float lReg = smoothstep(0.78 - _LavaAmt * 0.3, 0.9 - _LavaAmt * 0.3, lField);
                    if (lReg > 0.01)
                    {
                        float2 ul = (p + float2(_UWTime, -0.6 * _UWTime) * 0.03) / _LavaTile;
                        float4 la = SAMPLE_TEXTURE2D_GRAD(_LavaAH, SMP, ul, dpx / _LavaTile, dpy / _LavaTile);
                        float3 le = SAMPLE_TEXTURE2D_GRAD(_LavaE, SMP, ul, dpx / _LavaTile, dpy / _LavaTile).rgb;
                        float lw = HB(g.h, 0.3, lReg, _BlendDepth * 0.6) * lReg;   // lava only in the rock's hollows
                        g.a = lerp(g.a, la.rgb * _LavaTint.rgb, lw);
                        g.n = lerp(g.n, float2(0, 0), lw * 0.7);
                        g.h = lerp(g.h, 0.0, lw);
                        // a pool, not crack lines: the border is a dark cooled crust (the texture's cracks only glow
                        // well inside), the core is open molten lava with the crust plates floating on it, and the
                        // rock around the pool gets a faint warm heat bleed
                        float core = smoothstep(0.45, 0.9, lw);
                        float melt = core * (1.0 - smoothstep(0.25, 0.75, la.a));
                        g.a = lerp(g.a, g.a * 0.35, smoothstep(0.05, 0.4, lw) * (1.0 - core));
                        lavaEm = _LavaCol.rgb * _LavaInt * (le * core + melt * 0.55 * float3(1.0, 0.75, 0.55))
                               + _LavaCol.rgb * 0.06 * lReg * (1.0 - lw);
                    }

                    // cavity: the scan's own low areas go darker (reads as depth from the top-down camera)
                    g.a *= lerp(1.0 - _CavityDark, 1.0, smoothstep(0.15, 0.7, g.h));

                    // macro: slow brightness and warm/cool drift so no two screens look the same
                    float m1 = Field(p, 0.02, 2.7), m2 = GN(p * 0.12 + 13.0);
                    g.a *= 1.0 + _Macro * ((m1 - 0.5) * 1.2 + (m2 - 0.5) * 0.5);
                    g.a *= lerp(float3(1.05, 1.0, 0.95), float3(0.96, 0.99, 1.03), GN(p * 0.03 + 5.0));
                    g.a = min(g.a, _AlbedoMax);

                    topAlb = g.a;
                    float2 tn = g.n * _NrmStr;
                    topN = normalize(float3(tn.x, sqrt(saturate(1.0 - dot(tn, tn))), tn.y));
                    topR = g.r; topAO = g.o;
                }

                float3 albedo = lerp(sideAlb, topAlb, top);
                float3 n = normalize(lerp(sideN, topN, top));
                float rough = lerp(sideR, topR, top);
                float ao = lerp(1.0, lerp(sideAO, topAO, top), _AOStr);
                float smoothness = min(saturate(1.0 - rough) * _SmoothScale, _SmoothMax);

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
                s.emission = albedo * ao * _AmbLift.rgb + lavaEm * top;   // uniform lift so the scan reads in the dim hell light; lava from its texture
                s.alpha = 1;
                s.normalTS = float3(0, 0, 1);

                half4 col = UniversalFragmentPBR(d, s);
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
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
