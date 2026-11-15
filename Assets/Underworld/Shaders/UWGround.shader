// Underworld ground, HELL pass 11-13 (Diablo IV / Burning Hells from a high top-down camera).
// Dark cracked volcanic rock with glowing molten-lava rifts and veins; glow carries the image, the rock stays near black.
// Fab Megascans packed as  AH = albedo RGB + height A,  NRA = normal GL RG + roughness B + AO A:
//   Flag   main cracked rock (hex-tile bombed)         Ash    cooled dark crust (hex-tile bombed, only a little, fills crevices)
//   Rubble coal debris along the crack banks (hex-tile bombed, only evaluated near cracks)
//   Mud    unused now (properties kept so the material keeps its slots)       Cliff  triplanar sides
// Top surface pipeline:
//   1 warped soft masks  2 hex bombing of Flag/Ash  3 height blend rock vs crust  4 lava distance fields:
//   rifts = contour of a smooth 2-octave noise (0.3-1 m wide, pinched into broken rivers), veins = contours of two
//   mid-frequency noises inside a ~40% region mask (+ always near rifts = branching), both perturbed by the rock height
//   so the glow follows the scan's natural crevices   5 heat ramp cool->mid->hot + pulse + slow crust mottling
//   6 scorch halo (rock darkens near lava) + warm fake bounce (emission tinted by rock albedo)  7 obsidian patches.
// Texture samples (top): Flag 6 + Ash 6 + Rubble 6 (only near cracks) = 18 max. Sides (Cliff 6) only on non-up faces.
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
        _FloorRefY("Floor reference height (m) for the view-projected mapping", Float) = 0
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
        _LavaAmt("Lava vein region coverage", Range(0, 1)) = 0.6
        _LavaWidth("Rift half-width (m)", Range(0.05, 2.0)) = 1.0
        _LavaScale("Lava crust/sea feature scale", Range(0.3, 6)) = 1.0
        _SeaMode("Lava sea mode (all molten)", Range(0, 1)) = 0.0
        _CodeFX("Coded lava rifts / veins / plate seams (0 = downloaded scans only)", Range(0, 1)) = 1.0
        _ClearZone("River-free zone (x, z, radius m, fade m)", Vector) = (-2, -2, 5, 2.5)
        _Lane0("River-free lane 0 (x1, z1, x2, z2)", Vector) = (0, 0, 0, 0)
        _Lane1("River-free lane 1 (x1, z1, x2, z2)", Vector) = (0, 0, 0, 0)
        _LaneWidth("Lane half width / fade (m)", Vector) = (4, 3, 0, 0)
        _EdgeRect("Plaza rect for the broken rim (xMin, zMin, xMax, zMax; zero = off)", Vector) = (0, 0, 0, 0)
        [HDR] _LavaColHot("Lava hottest core", Color) = (1.0, 0.82, 0.42, 1)
        [HDR] _LavaColMid("Lava mid", Color) = (1.0, 0.3, 0.04, 1)
        [HDR] _LavaColCool("Lava cool edge", Color) = (0.55, 0.04, 0.01, 1)
        _LavaIntensity("Lava HDR intensity", Range(0, 12)) = 3.5
        _LavaPulse("Lava pulse depth", Range(0, 1)) = 0.35
        _LavaFlow("Lava crust flow speed", Range(0, 3)) = 1.0
        _LavaBounce("Warm bounce on rock", Range(0, 2)) = 0.5
        _ScorchAmt("Scorch halo darkening", Range(0, 1)) = 0.6
        _ObsAmt("Obsidian patches", Range(0, 1)) = 0.5
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
        float _FloorRefY, _CliffTile, _Sat, _AshAmt, _RubAmt, _MudAmt, _GroutLevel, _AshLift, _BlendDepth, _SeamDark, _Cavity, _NrmStr, _Macro, _AOStr;
        float _SmoothScale, _WetAmt, _WetSmooth, _SmoothMax, _CrackAmt, _RimAmt;
        float4 _LavaColHot, _LavaColMid, _LavaColCool;
        float4 _ClearZone, _EdgeRect, _Lane0, _Lane1, _LaneWidth;
            float SegD(float2 p, float4 s) { float2 a = s.xy, b = s.zw, ab = b - a; float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4)); return length(p - a - ab * t); }
        float _CodeFX;
        float _LavaScale, _SeaMode, _LavaAmt, _LavaWidth, _LavaIntensity, _LavaPulse, _LavaFlow, _LavaBounce, _ScorchAmt, _ObsAmt;
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

            // Gradient (Perlin) noise, 0..1, quintic fade, lattice pre-rotated. Value noise (UWNoise) has zero slope on every lattice
            // line, so its mid-level contours snap to the grid and read as straight axis-aligned cuts; gradient noise does not.
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
            // second-octave helper: lattice turned a further 60 degrees so octaves never share grid directions
            float GN2(float2 x) { return GN(mul(float2x2(0.5, -0.866, 0.866, 0.5), x)); }
            // pass 3: sin-free hash + value noise (Hoskins). UWHash21 is frac(sin(big)): on the lava sea (|p| ~ 100-500 m after
            // scaling) GPU sin loses precision and the mottle came out as blocky horizontal bands.
            float HH(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
            float VN(float2 x)
            {
                float2 i = floor(x), f = frac(x); float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(HH(i), HH(i + float2(1, 0)), u.x), lerp(HH(i + float2(0, 1)), HH(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                // the plaza floor is many tiles at slightly different heights; plain ws.xz shifts the pattern by the step
                // height x the view slope, which cut every lava line at tile borders. Project along the view ray onto the
                // reference plane instead, so the pattern is continuous on screen across steps.
                float3 vr = ws - _WorldSpaceCameraPos;
                float2 p = ws.xz;   // (view-projected mapping tried 11-13; floor is one flat mesh, not needed) vr unused: float _unusedRef = _FloorRefY;
                float3 dwx = ddx(ws), dwy = ddy(ws);     // derivatives outside any branch (sampling uses _GRAD)
                // robust 'upward' test from the geometric normal (ignores smoothed/tilted/scaled vertex normals, same for every floor mesh)
                float3 Ng = normalize(cross(dwx, dwy));
                float top = smoothstep(0.35, 0.6, abs(Ng.y));
                N = lerp(N, float3(0, 1, 0), top);   // top pieces shade with a clean up normal; the cliff branch is skipped at top >= 0.999
                N = normalize(N);

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
                float3 topAlb = 0, topN = float3(0, 1, 0); float topR = 0.8, topAO = 1;
                float3 lavaEm = 0, bounceEm = 0; float lavaMask = 0, obsOut = 0, crackMask = 0;
                if (top > 0.001)
                {
                    // ---- 1. soft, domain-warped masks
                    float2 warp = float2(GN(p * 0.06 + 3.1), GN2(p * 0.06 + 8.7)) - 0.5;
                    float2 q = p + warp * 16.0;                                         // large warp: organic outlines
                    q += (float2(GN(p * 0.45 + 1.7), GN2(p * 0.45 + 5.9)) - 0.5) * 1.6;   // small warp: ragged edge
                    float m1 = UWFbm(p * 0.03 + 2.7);                                   // ~33 m macro
                    float n1 = saturate((m1 - 0.25) / 0.5);
                    float m2 = UWNoise(p * 0.2 + 13.0);                                 // ~5 m mid
                    float density = UWNoise(p * 0.02 + 40.0) - 0.5;
                    float earthN = UWFbm(q * 0.045 + 7.3) + (UWNoise(q * 0.35) - 0.5) * 0.2;
                    float thr = 0.62 - _AshAmt * 0.25 - density * 0.35;
                    float ash = smoothstep(thr - 0.13, thr + 0.13, earthN);             // cooled crust coverage (kept small)

                    // ---- 2. layers (hex-tile bombing: no visible cells, no repetition)
                    float2 dpx = dwx.xz, dpy = dwy.xz;
                    float2 uf = p / _Tiles.x, ue = p / _Tiles.y;
                    L lf = Hex(_FlagAH, _FlagNRA, uf, dpx / _Tiles.x, dpy / _Tiles.x, 1.5, float2(_HeightLo.x, _HeightHi.x));
                    L la = Hex(_AshAH, _AshNRA, ue, dpx / _Tiles.y, dpy / _Tiles.y, 1.2, float2(_HeightLo.y, _HeightHi.y));
                    lf.a = Desat(lf.a, _Sat) * _FlagTint.rgb;
                    la.a = Desat(la.a, _Sat) * _AshTint.rgb;
                    la.o = lerp(1.0, la.o, 0.6);

                    // cavity on the rock: low parts darker, proud tops slightly lighter
                    float cav = lerp(1.0 - _Cavity * 0.75, 1.0, smoothstep(0.1, 0.6, lf.h));
                    lf.a *= cav * (1.0 + 0.18 * smoothstep(0.6, 0.95, lf.h));

                    // ---- 3. height blend rock vs cooled crust (crust only fills the low crevices)
                    float hS = lf.h + (1.0 - ash) * _AshLift;
                    float hE = _GroutLevel + la.h * 0.2 + ash * _AshLift;
                    float hmax = max(hS, hE) - _BlendDepth;
                    float2 bw = max(float2(hS, hE) - hmax, 0.0);
                    bw /= max(bw.x + bw.y, 1e-4);
                    stoneW = bw.x; earthW = bw.y;
                    L g = Lerp(la, lf, stoneW);
                    edge = 4.0 * stoneW * (1.0 - stoneW);

                    // ---- 4. lava distance fields (metres, approximate = noise distance / noise gradient)
                    // all shapes are gradient noise of the warped plane q only (no hex-sampled term anywhere in the glow path)
                    float tt = _UWTime;
                    float sea = _SeaMode;
                    float2 ql = q / max(_LavaScale, 0.1);
                    // rifts: a FEW wide rivers = contour of a smooth 2-octave noise (33 m cells), 0.5-1.5 x _LavaWidth half-width, pinched into segments
                    float fr = 0.65 * GN(q * 0.032 + 11.3) + 0.35 * GN2(q * 0.08 + 23.7);
                    // pass 3: true-ish distance (divide by the local world gradient, from screen derivatives). With a constant
                    // divisor, flat stretches of the noise (fr ~ 0.5 over metres) became huge molten pools.
                    float2 wd = float2(length(float2(ddx(p.x), ddy(p.x))), length(float2(ddx(p.y), ddy(p.y)))) + 1e-4;
                    float gfr = length(float2(ddx(fr), ddy(fr))) / max(min(wd.x, wd.y), 1e-4);
                    float dR0 = abs(fr - 0.5) / clamp(gfr, 0.006, 0.08);
                    float dR = dR0 + (GN(q * 0.9 + 5.0) - 0.5) * 0.5;
                    float pinch = smoothstep(0.2, 0.45, GN2(q * 0.07 + 77.0));
                    float hwR = max(_LavaWidth * (0.5 + 1.0 * GN(q * 0.1 + 3.0)) * pinch, 0.02);
                    // pass 5 (final_04 #1/#2): rivers TAPER to a point instead of being cut: (a) only about half of the river
                    // network exists (low-frequency keep mask), (b) none within _ClearZone of the spawn / boss centre,
                    // (c) none in the last metres before the plaza rim (they used to end flat against the edge)
                    float taper = smoothstep(0.3, 0.5, GN(p * 0.018 + 71.0));   // pass 7: more rivers back (glow)
                    taper *= smoothstep(_ClearZone.z, _ClearZone.z + _ClearZone.w, distance(ws.xz, _ClearZone.xy));
                    if (_EdgeRect.z > _EdgeRect.x)
                    {
                        float dE0 = min(min(p.x - _EdgeRect.x, _EdgeRect.z - p.x), min(p.y - _EdgeRect.y, _EdgeRect.w - p.y));
                        taper *= smoothstep(6.0, 11.0, dE0);   // pass 8: also clears the narrow NE ledge (final_07 #2)
                    }
                    // pass 6 (final_05 #2): no rivers on the natural walking lanes (spawn -> gate, spawn -> portal)
                    if (dot(_Lane0, _Lane0) > 0) taper *= smoothstep(_LaneWidth.x, _LaneWidth.x + _LaneWidth.y, SegD(p, _Lane0));
                    if (dot(_Lane1, _Lane1) > 0) taper *= smoothstep(_LaneWidth.x, _LaneWidth.x + _LaneWidth.y, SegD(p, _Lane1));
                    taper = lerp(taper, 1.0, _SeaMode);
                    hwR = max(hwR * taper, 0.0005);
                    // veins: RARE, short, only within a few metres of a rift (branching off), thin and dim red
                    float f2 = 0.7 * GN(q * 0.16 + 51.7) + 0.3 * GN2(q * 0.37 + 13.9);
                    float f3 = 0.7 * GN2(q * 0.13 + 87.3) + 0.3 * GN(q * 0.41 + 33.1);
                    float dV = min(abs(f2 - 0.5), abs(f3 - 0.5)) / 0.12;
                    float regN = 0.6 * GN(q * 0.035 + 91.0) + 0.3 * GN2(q * 0.07 + 12.0) + 0.1 * GN(q * 0.15 + 3.0);
                    float thrV = 0.68 - _LavaAmt * 0.12;
                    float region = smoothstep(thrV - 0.05, thrV + 0.05, regN);
                    float nearR = saturate(1.0 - dR0 / (_LavaWidth * 3.0 + 2.5));
                    float regTot = nearR * region;
                    float hwV = _LavaWidth * 0.1 * (0.6 + 0.8 * GN(q * 0.5 + 9.0)) * regTot;
                    float cR = saturate(1.0 - dR / hwR);
                    // final_03 #1: keep the spawn / boss centre free of rivers (rivers fade out over _ClearZone.w metres)
                    cR *= smoothstep(0.02, 0.15, hwR);   // tapered-away tips vanish cleanly
                    float cV = saturate(1.0 - dV / max(hwV, 0.005)) * smoothstep(0.02, 0.07, hwV) * 0.3;   // caps veins at dim red-orange
                    float lavaCore = max(cR, cV);                                       // 0 at bank .. 1 at centre line
                    lavaCore *= _CodeFX;   // 11-14 Kido: no coded glowing lines on the floor
                    // pass 4 (final_03 #3, ruler-straight plaza edges): a jagged glowing fissure runs 0.8-3.5 m inside the rim and
                    // the strip outside it is charred and crumbling, so the edge reads as broken-off rock over the lava sea
                    float crumble = 0;
                    if (_EdgeRect.z > _EdgeRect.x)
                    {
                        float dE = min(min(p.x - _EdgeRect.x, _EdgeRect.z - p.x), min(p.y - _EdgeRect.y, _EdgeRect.w - p.y));
                        float eL = dE - (0.6 + 4.0 * GN(p * 0.12 + 61.0) + 0.8 * GN2(p * 0.6 + 17.0));
                        float hwE = 0.12 + 0.3 * GN(p * 0.35 + 5.0);
                        float segE = smoothstep(0.42, 0.58, GN(p * 0.09 + 41.0));       // broken into separate cracks, not one line
                        float cE = saturate(1.0 - abs(eL) / hwE) * smoothstep(-1.0, 4.0, dE) * (1.0 - sea) * segE;
                        lavaCore = max(lavaCore, cE * 0.0);   // fissure off: even broken, a crack parallel to the rim still read as a ruler line
                        crumble = smoothstep(0.2, -0.6, eL) * (1.0 - sea) * _CodeFX;
                    }
                    // lava sea mode: the whole surface is molten, brightness drifting slowly
                    // sea = glowing channels along the 0.5 contour of a warped gradient-noise field, everything else is dark crust islands
                    float2 qs = ql + float2(tt * 0.04, -tt * 0.025) * _LavaFlow;        // slow flow
                    qs += (float2(GN(ql * 0.25 + 3.0), GN2(ql * 0.25 + 9.0)) - 0.5) * 4.0;
                    float sf = 0.55 * GN(qs * 0.05 + 21.0) + 0.3 * GN2(qs * 0.13 + 4.0) + 0.15 * GN(qs * 0.4 + 15.0);
                    float seaCh = saturate(1.0 - abs(sf - 0.5) / 0.05);                 // pass 4: narrower, crisper channels
                    seaCh = seaCh * seaCh * (3.0 - 2.0 * seaCh);
                    lavaCore = lerp(lavaCore, seaCh, sea);
                    if (sea > 0.5)
                    {
                        // pass 4 (final_03 #2, "mushy sea"): the crust islands break into ~4 m basalt plates with glowing seams,
                        // so the sea reads as a cracked crust floating on lava instead of soft cream blobs
                        float bdS = UWVoronoi(qs * 0.6).y;
                        float seam = 1.0 - smoothstep(0.03, 0.12, bdS);
                        float seamReg = smoothstep(0.33, 0.55, GN(qs * 0.08 + 13.0));   // only some crust fields are cracked (no even web)
                        lavaCore = max(lavaCore, seam * (1.0 - seaCh) * 0.8 * seamReg);
                    }
                    lavaMask = smoothstep(0.0, 0.2, lavaCore);
                    // wide soft red halo onto the rock around rifts, much tighter around veins
                    float presence = smoothstep(0.1, 0.5, hwR);                           // pinched-off rift stretches have no halo/bounce
                    float halo = max(saturate(1.0 - dR / (hwR * 4.0 + 2.0)) * presence, saturate(1.0 - dV / (hwV * 3.0 + 0.2)) * regTot * 0.5);
                    float bounceFall = saturate(1.0 - max(dR - hwR, 0.0) / 4.0);   // red bounce only within ~4 m of a rift
                    bounceFall *= bounceFall * presence * (1.0 - sea);
                    halo *= 1.0 - sea;
                    halo *= halo;
                    halo *= _CodeFX; bounceFall *= _CodeFX;

                    // heat-cracked basalt plates: polygonal slabs ~2.6 m (voronoi border distance is continuous), dark seams
                    float bdR = UWVoronoi(q * 0.38).y;
                    float rockSeam = (1.0 - smoothstep(0.03, 0.12, bdR)) * _CodeFX;   // coded voronoi plate web: off with _CodeFX 0
                    float plateLight = lerp(0.57, smoothstep(0.0, 0.45, bdR), _CodeFX);

                    // ---- 5. coal debris along the banks (only sampled near cracks)
                    float bank = halo * (1.0 - lavaMask);
                    float rubGate = bank * _RubAmt;   // noise-only gate (no hex-derived edge)
                    // scans-only floor: coal debris in large soft drifts instead of along the (now absent) lava banks
                    rubGate += (1.0 - _CodeFX) * _RubAmt * smoothstep(0.64, 0.8, 0.65 * GN(q * 0.05 + 17.0) + 0.35 * GN2(q * 0.13 + 3.0));   // gradient noise: value-noise contours snapped to the grid (square patches)
                    if (rubGate > 0.04)
                    {
                        float2 ur = p / _Tiles.z;
                        L lr = Hex(_RubAH, _RubNRA, ur, dpx / _Tiles.z, dpy / _Tiles.z, 1.2, float2(_HeightLo.z, _HeightHi.z));
                        lr.a = Desat(lr.a, _Sat) * _RubTint.rgb;
                        float rubN = GN(p * 1.1 + 9.0);   // 11-14: gradient noise (value noise made square-edged patches)
                        float rubW = smoothstep(0.2, 0.8, rubGate * (0.55 + rubN) * 1.6 + (lr.h - 0.5) * 0.6 * saturate(rubGate * 3.0));
                        g = Lerp(g, lr, rubW * (1.0 - lavaMask));
                    }
                    g.a *= 1.0 - _SeamDark * edge;
                    g.a *= (1.0 - 0.55 * rockSeam) * (0.8 + 0.35 * plateLight);

                    // ---- 6. macro variation
                    float bright = 1.0 + _Macro * ((n1 - 0.5) * 1.1 + (m2 - 0.5) * 0.7);
                    g.a *= bright;
                    g.a *= lerp(float3(1.06, 1.0, 0.92), float3(0.95, 0.99, 1.04), UWNoise(p * 0.035 + 5.0));   // warm/cool drift
                    g.r = saturate(g.r + (n1 - 0.5) * 0.25 * _Macro);

                    // ---- 7. obsidian: cooled black glass patches, smooth and dark, away from the molten core
                    float obsN = 0.6 * GN(q * 0.07 + 201.0) + 0.3 * GN2(q * 0.15 + 7.0) + 0.1 * GN(q * 0.4 + 2.0);
                    float obs = smoothstep(0.56, 0.7, obsN) * _ObsAmt * (1.0 - lavaMask);
                    g.a = lerp(g.a, g.a * 0.3 + 0.008, obs);
                    g.n *= 1.0 - 0.55 * obs;
                    obsOut = obs;

                    // scorch: rock darkens near the glow; the lava surface itself is a dark skin under the emission
                    g.a *= 1.0 - _ScorchAmt * 0.7 * halo;
                    g.a *= 1.0 - 0.85 * lavaMask;
                    g.a *= 1.0 - 0.8 * crumble;   // pass 4: charred broken rim strip
                    g.a = min(g.a, 0.2);   // hard ceiling: the rock must never bloom, only the lava does

                    // ---- cooling crust skin: dark cracked plates floating on the rift; many at the banks, few in the deep core
                    float crust = 0;
                    if (lavaCore > 0.001 && sea < 0.5)
                    {
                        float2 pd = ql + float2(tt * 0.03, -tt * 0.02) * _LavaFlow;
                        float bdC = UWVoronoi(pd * 0.9).y;
                        float plate = smoothstep(0.04, 0.16, bdC);                       // 0 in the glowing gaps between plates
                        float covRift = saturate((1.0 - cR) * 0.9 + 0.1) * saturate(cR * 6.0);   // none on thin veins, dense at banks, sparse in the core
                        float islandN = 0.6 * GN(ql * 0.05 + 31.0) + 0.4 * GN2(ql * 0.12 + 5.0);
                        float covSea = smoothstep(0.45, 0.65, islandN) * 0.95;           // drifting dark crust islands
                        crust = plate * covRift;
                    }

                    // ---- lava emission: heat ramp (deep red edge -> orange -> yellow-orange core), slow pulse, crust mottling
                    float flowN = VN(p * 1.3 + float2(tt * 0.12, -tt * 0.08) * _LavaFlow);
                    float flowN2 = VN(p * 3.1 + float2(-tt * 0.2, tt * 0.15) * _LavaFlow + 7.0);
                    float mottle = lerp(0.75, 1.3, saturate(flowN * 0.6 + flowN2 * 0.4));
                    float heat = saturate(pow(lavaCore, 1.6) * mottle) * (1.0 - 0.92 * crust);
                    float phase = VN(p * 0.06 + 4.0) * 6.2831853;                  // pulse rolls across the floor, not in lockstep
                    float pulse = 1.0 + _LavaPulse * (0.7 * sin(tt * 0.8 + phase) + 0.3 * sin(tt * 1.9 + phase * 1.7));
                    float3 lcol = lerp(_LavaColCool.rgb, _LavaColMid.rgb, smoothstep(0.05, 0.5, heat));
                    lcol = lerp(lcol, _LavaColHot.rgb, smoothstep(0.7, 1.0, heat));
                    // HDR levels (at _LavaIntensity 3.5): edge 0.4 -> mid 1.2 -> hot core 3.5 (exposure +1.6 EV is applied after)
                    float level = lerp(0.4, 1.2, smoothstep(0.05, 0.5, heat));
                    level = lerp(level, 2.6, smoothstep(0.7, 1.0, heat));   // final_03: hot core was 3.5 -> cream-white after exposure
                    lavaEm = lcol * level * (_LavaIntensity / 3.5) * pulse * smoothstep(0.02, 0.25, heat);
                    // fake bounce: dark-red light on the rock near rifts, strongest in the plate seams
                    float3 bcol = _LavaColCool.rgb * 0.5 + _LavaColMid.rgb * 0.5;
                    bounceEm = bcol * 0.37 * bounceFall * (1.0 - lavaMask) * _LavaIntensity * _LavaBounce * pulse * 0.35 * (0.25 + 1.2 * rockSeam);
                    // pass 7 (final_06 #1, "too dark/empty"): dim crimson heat in the basalt plate seams over about half of the
                    // floor - glow everywhere without a bright core; the bright orange stays reserved for the real rivers
                    // (first try 0.12 seams over half the floor = the old red voronoi web again; now faint and patchy)
                    float glowReg = smoothstep(0.55, 0.8, GN(p * 0.05 + 5.0));
                    // pass 8 (final_07 #1): a ring of seam heat + flecks around the spawn hub (4-13 m), lanes kept clean
                    float dHub = distance(ws.xz, _ClearZone.xy);
                    float hub = smoothstep(3.0, 5.0, dHub) * (1.0 - smoothstep(10.0, 14.0, dHub));
                    if (dot(_Lane0, _Lane0) > 0) hub *= smoothstep(2.5, 4.5, SegD(p, _Lane0));
                    if (dot(_Lane1, _Lane1) > 0) hub *= smoothstep(2.5, 4.5, SegD(p, _Lane1));
                    glowReg = max(glowReg, 0.6 * hub);
                    float fleck = smoothstep(lerp(0.72, 0.66, hub), lerp(0.86, 0.8, hub), GN(p * 0.35 + 19.0)) * smoothstep(0.6, 1.0, rockSeam);   // sparse short seam flecks
                    bounceEm += _LavaColCool.rgb * (0.04 * rockSeam * glowReg + 0.12 * fleck) * _LavaIntensity * (1.0 - lavaMask) * (1.0 - sea) * pulse;
                    bounceEm *= _CodeFX;

                    topAlb = g.a;
                    float2 tn = g.n * _NrmStr;
                    topN = normalize(float3(tn.x, sqrt(saturate(1.0 - dot(tn, tn))), tn.y));
                    topR = g.r; topAO = g.o; height = g.h;
                    crackMask = pow(edge, 3.0) * (1.0 - ash);
                }

                albedo = lerp(sideAlb, topAlb, top);
                n = normalize(lerp(sideN, topN, top));
                rough = lerp(sideR, topR, top);
                ao = lerp(1.0, lerp(sideAO, topAO, top), _AOStr);

                float dry = saturate(1.0 - rough) * _SmoothScale;
                float smoothness = min(dry, _SmoothMax);
                smoothness = max(smoothness, min(obsOut * top * 0.75, 0.7));              // obsidian: glassy, capped (no hot specular disks)
                smoothness = lerp(smoothness, 0.1, lavaMask * top);

                // emission: lava + warm bounce; old violet soul cracks are added below only when _CrackAmt > 0
                float3 emission = (lavaEm + bounceEm) * top;
                // pass 4: lava seeping down the plaza cliff walls (below the walking surface only), vertical streaks
                if (top < 0.999 && _SeaMode < 0.5)
                {
                    float ux = (ws.x + ws.z) * 0.6;
                    float drip = smoothstep(0.6, 0.78, GN(float2(ux, ws.y * 0.08 + 7.0))) * smoothstep(-1.2, -2.2, ws.y);   // only the real cliff, not the low rim step ~3 m inside the edge
                    drip *= 0.6 + 0.4 * GN(float2(ux * 3.0, ws.y * 0.6 - _UWTime * 0.4));
                    drip *= _CodeFX;
                    emission += _LavaColMid.rgb * 1.1 * drip * (1.0 - top) * (_LavaIntensity / 3.5);
                }
                if (_CrackAmt > 0.001 && top > 0.001)
                {
                    float clN = UWFbm(p * 0.13 + 5.3);
                    float cluster = smoothstep(0.7 - _CrackAmt * 0.15, 0.88 - _CrackAmt * 0.15, clN) * crackMask * top;
                    float breathe = 0.55 + 0.45 * sin(_UWTime * 0.6 + UWNoise(p * 0.3) * 6.28);
                    emission += _CrackCol.rgb * cluster * breathe * 0.4;
                }

                InputData d = (InputData)0;
                d.positionWS = ws;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(ws);
                d.shadowCoord = TransformWorldToShadowCoord(ws);
                // lava sea (pass 2): never shadowed - the plaza 6 m above cast a hard black rectangle onto it. z = 0 is "beyond shadow far"
                if (_SeaMode > 0.5) d.shadowCoord = float4(0, 0, 0, 1);
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
