// Snow plague frozen river (SPIce.shader), r5 (11-12, Kido: "얼음 그냥 전으로 돌려"): back to the ice8 look (dark glossy
// photo-scanned ice, its own pale cracks/specks kept pale, a darker copy at a fake depth below the surface, frost haze
// along the flow, soft snow dust near the shore, compact bullet/lantern reflections) on the r3 shoreline (no banks: the
// mesh is cut at an irregular shoreline so the ground snow runs onto the ice; r6: no rim line, noise-driven snow spill).
// The scan is any tileable ice set (_IceA albedo, _IceN normal (_IceNFlipY = 1 for DirectX maps), _IceR roughness),
// sampled with two continuous rotated taps blended by low-frequency noise so the tiling never shows.
// Mesh (SPBuilder.Ice.cs): UV x = metres along the river, y = metres across (signed); vertex colour G = lip / 10 (m).
// Reflections (forward pass, emission): up to 64 bullets/glows (_SPIce_Glow*) and the 4 lit lanterns as short mirrored
// streaks, a short moon streak, optional SSR (SPIceReflect.SSROn, default off).
#ifndef SP_ICE_SURFACE
#define SP_ICE_SURFACE
#include "Assets/Game/SnowPlague/Shaders/SPSparkle.hlsl"   // r13 sparse clustered glints

TEXTURE2D(_SnowA); TEXTURE2D(_SnowN); SAMPLER(sampler_SnowA);
TEXTURE2D(_IceA); TEXTURE2D(_IceN); TEXTURE2D(_IceR); SAMPLER(sampler_IceA);

CBUFFER_START(UnityPerMaterial)
    float4 _SnowA_ST, _IceA_ST;
    float4 _IceTint, _DeepCol, _CrackCol, _SnowTint, _SkyCol, _LanternCol;
    float _IceTile, _Smooth, _CrackDepth, _Cracks, _Frost, _IceNormal, _IceNFlipY, _RoughScale;
    float _SnowTile, _SnowSmooth, _Sparkle, _Dust, _Rim, _ShoreNoise;
    float _ReflStrength, _SSRStrength, _GlowStrength, _GlowStretch, _GlowRadius, _MoonStreak, _LanternStreak;
CBUFFER_END

// globals (SPIceReflect)
#define SP_ICE_MAX_GLOWS 64
float4 _SPIce_Glow[SP_ICE_MAX_GLOWS];      // xyz world position, w size (m)
float4 _SPIce_GlowCol[SP_ICE_MAX_GLOWS];   // rgb HDR colour
float  _SPIce_GlowCount;
float4 _SPIce_Params;                      // x SSR valid this camera, y glows on, z moon/lantern streaks on
TEXTURE2D(_SPIce_Prev); SAMPLER(sampler_SPIce_Prev);
float4 _SPIce_PrevTexel;
float4x4 _SPIce_PrevVP;                    // previous frame GPU VP (not flipped), uv = ndc * 0.5 + 0.5
float4 _SPIce_PrevCam, _SPIce_PrevFwd;

// Integer hash: the float hash (RFHash) disagreed between neighbouring cells on the same lattice corner (FMA folding),
// so value noise jumped at every cell border (r2 blocky squares). Exact integer corners cannot disagree.
uint SPIceHashU(int2 c)
{
    uint h = (uint)c.x * 0x8da6b343u ^ (uint)c.y * 0xd8163841u;
    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
    return h;
}
float SPIceHashI(int2 c) { return (SPIceHashU(c) & 0xffffffu) * (1.0 / 16777216.0); }

// smooth value noise (quintic), continuous across cells
float SPIceNoise(float2 p)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl;
    float2 u = f * f * f * (f * (f * 6 - 15) + 10);
    return lerp(lerp(SPIceHashI(i), SPIceHashI(i + int2(1, 0)), u.x), lerp(SPIceHashI(i + int2(0, 1)), SPIceHashI(i + int2(1, 1)), u.x), u.y);
}
// gradient (Perlin) noise, integer-hashed gradients, 0..1 (about). Value noise thresholded at this scale showed its
// lattice as a pixel mosaic at the game camera (ice12); gradient noise has no plateaus at the lattice points.
float SPIceGNoise(float2 p)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl;
    float2 u = f * f * f * (f * (f * 6 - 15) + 10);
    float a00 = SPIceHashI(i) * 6.2831853, a10 = SPIceHashI(i + int2(1, 0)) * 6.2831853;
    float a01 = SPIceHashI(i + int2(0, 1)) * 6.2831853, a11 = SPIceHashI(i + int2(1, 1)) * 6.2831853;
    float n00 = dot(float2(cos(a00), sin(a00)), f), n10 = dot(float2(cos(a10), sin(a10)), f - float2(1, 0));
    float n01 = dot(float2(cos(a01), sin(a01)), f - float2(0, 1)), n11 = dot(float2(cos(a11), sin(a11)), f - float2(1, 1));
    return saturate(lerp(lerp(n00, n10, u.x), lerp(n01, n11, u.x), u.y) * 0.9 + 0.5);
}
float SPIceNoiseR(float2 p, float a)
{
    float s, c; sincos(a, s, c);
    return SPIceGNoise(float2(c * p.x - s * p.y, s * p.x + c * p.y));
}

float2 SPIceRot(float2 p, float a) { float s, c; sincos(a, s, c); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }

// two continuous taps of a tiling texture, different rotation/scale, blended by low-frequency noise (anti-repeat)
struct SPIceTaps { float2 uvA, uvB; float w; };
SPIceTaps SPIceMakeTaps(float2 p, float tile, float seed)
{
    SPIceTaps t;
    t.uvA = SPIceRot(p, 0.6 + seed) / tile + seed * 0.37;
    t.uvB = SPIceRot(p, 2.3 + seed) / (tile * 2.2) + float2(0.53, 0.21) + seed;   // gnd1: the two taps differ ~2x in crack cell size
    float n = SPIceNoise(p / (tile * 2.2) + seed * 3.1) * 0.7 + SPIceNoise(p / (tile * 0.9) + 7.7) * 0.3;
    t.w = smoothstep(0.38, 0.62, n);
    return t;
}
float2 SPIceTapN(TEXTURE2D_PARAM(tn, sn), SPIceTaps t, float seed, float flipY)
{
    float2 a = SAMPLE_TEXTURE2D(tn, sn, t.uvA).rg * 2 - 1;
    float2 b = SAMPLE_TEXTURE2D(tn, sn, t.uvB).rg * 2 - 1;
    if (flipY > 0.5) { a.y = -a.y; b.y = -b.y; }   // DirectX normal maps (Megascans)
    return lerp(SPIceRot(a, -(0.6 + seed)), SPIceRot(b, -(2.3 + seed)), t.w);
}

#if defined(SP_PASS_FORWARD)
float3 SPIceSSR(float3 W, float3 R, float jitter, out float conf)
{
    conf = 0;
    float t = 0.05 + jitter * 0.06, stepLen = 0.1;
    [loop] for (int k = 0; k < 16; k++)
    {
        float3 P = W + R * t;
        float4 c = mul(_SPIce_PrevVP, float4(P, 1));
        if (c.w <= 0.01) break;
        float2 uv = c.xy / c.w * 0.5 + 0.5;
        if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) break;
        float scene = SAMPLE_TEXTURE2D_LOD(_SPIce_Prev, sampler_SPIce_Prev, uv, 0).a;
        float pe = dot(P - _SPIce_PrevCam.xyz, _SPIce_PrevFwd.xyz);
        float diff = pe - scene;
        if (diff > 0 && diff < 0.35 + t * 0.15)
        {
            float2 eu = min(uv, 1 - uv);
            conf = smoothstep(0.0, 0.1, eu.x) * smoothstep(0.0, 0.1, eu.y) * saturate(1 - t / 4.0) * saturate(1 - diff / (0.35 + t * 0.15));
            return min(SAMPLE_TEXTURE2D_LOD(_SPIce_Prev, sampler_SPIce_Prev, uv, 0).rgb, 8);
        }
        t += stepLen; stepLen *= 1.22;
    }
    return 0;
}

// mirrored glow of a light source at G (size s) seen on the ice point W: a short soft streak toward the camera
float SPIceGlowTerm(float3 W, float3 G, float s, float rough, float stretch)
{
    float3 E = _WorldSpaceCameraPos;
    float h = max(G.y - W.y, 0.05);
    float t = (E.y - W.y) / max(E.y - (W.y - h), 1e-3);
    float2 P0 = E.xz + t * (G.xz - E.xz);           // where the mirror image sits on the ice
    float2 d = W.xz - P0;
    float2 f = P0 - E.xz; f = f / max(length(f), 1e-3);
    float along = dot(d, f), lat = dot(d, float2(-f.y, f.x));
    float spread = s * (0.45 + h * 0.15) * (0.7 + rough * 2.0);
    float la = spread * (along < 0 ? stretch : 1.2);
    float core = exp(-(lat * lat) / (spread * spread) - (along * along) / (la * la));
    float halo = exp(-dot(d, d) / (spread * spread * 9.0)) * 0.15;
    return (core + halo) / (1 + h * h * 0.2);
}
#endif

void SPSurface(SPInput i, inout SPOut o)
{
    float2 xz = i.ws.xz;
    float s = i.uv.x, ac = i.uv.y, ao = abs(ac);
    float lip = i.color.g * 10.0;
    float side = ac < 0 ? 0.0 : 1.0;

    // ---- shoreline, r7 (Kido: no white line, no "오려놓듯이" cut-out contour).
    // f: signed distance (m) past the irregular ice edge. Instead of one crisp cut, the ground's own snow thins out onto
    // the ice: over a transition width (0.15 m on bare stretches .. ~3 m where snow spills, set by a wide low-frequency
    // noise along each shore and stretched into wind-blown tongues) the ice is clipped away in random grains whose
    // density falls off with distance, so the real ground snow shows through as powder and drift tongues and the ice
    // shows through the thin snow. Clipped in every pass (depth too), so it is exact.
    // gnd11 (QA amateur #3): the 1-D noise along the river gave regular scallops; the shoreline now comes from warped 2-D
    // noise at three scales (bays, tongues, ragged bits), so the bank never repeats a rhythm
    float2 swp = float2(SPIceNoise(xz * 0.07 + 3.0), SPIceNoise(xz * 0.07 + 8.0)) - 0.5;
    float sn = SPIceNoise(float2(s * 0.17, side * 17.0 + 3.0)) * 0.45 + SPIceNoise(float2(s * 0.6 + swp.x * 2.0, side * 17.0 + 9.0)) * 0.2 + SPIceNoiseR(xz * 0.35 + swp * 2.0, 0.7) * 0.25 + SPIceNoise(xz * 2.6) * 0.1;   // gnd12: 2-D share 0.25 (0.65 left ice puddles out in the snow)
    float shore = lip - 0.4 + (sn - 0.5) * 2.0 * _ShoreNoise;
    float f = ao - shore;
    // r8 (lead: a constant pale ribbon along the cut, mid-size "leopard" blotches): about half of each shore is bare (no
    // snow on the ice at all); elsewhere the spill width varies 0 -> 3 m along the shore and is shaped by few, large,
    // wind-stretched tongues; the snow is a soft low-contrast layer, not a thresholded blob pattern.
    float spill = SPIceNoise(float2(s * 0.05, side * 13.0 + 2.0)) * 0.75 + SPIceNoise(xz * 0.08 + 4.0) * 0.25;
    float spillAmt = smoothstep(0.44, 0.72, spill);                     // 0 on roughly 40-50% of the shore length
    float width = 0.05 + spillAmt * 3.0;
    float2 wd = normalize(_RF_Wind.xy + float2(1e-3, 0));
    float2 wp = float2(dot(xz, wd), dot(xz, float2(-wd.y, wd.x)));
    float tongue = SPIceNoise(float2(wp.x * 0.14, wp.y * 0.6) + 5.0) * 0.7 + SPIceNoise(xz * 0.25 + 2.0) * 0.3;
    float r = saturate(-f / (width * lerp(0.35, 1.7, tongue)));         // 0 at the shore .. 1 where the ice is clean
    float grain = SPIceNoiseR(xz * 9.0 + 11.0, 0.61) * 0.5 + SPIceNoiseR(xz * 19.0 + 3.0, 1.97) * 0.2 + SPIceNoiseR(xz * 4.1 + 7.0, 2.83) * 0.3;
    // r7b: clipping over the whole width showed the ground under the river (blocky soil mask) through the holes, so the
    // clip only roughens the last ~12 cm; the thinning-out itself is our snow layer over the ice (below)
    o.alpha = (f < 0 && !(f > -0.12 && grain > -f / 0.12)) ? 1 : 0;

    // ---- thin powder left on the ice itself next to the grains (soft, never a band: noise-gated, fades with r)
    float2 st = xz / max(_SnowTile, 0.1);
    float3 sa = SAMPLE_TEXTURE2D(_SnowA, sampler_SnowA, st).rgb * _SnowTint.rgb * 1.3;   // up to the lit ground snow value (no darker seam at the cut)
    float2 snN = SAMPLE_TEXTURE2D(_SnowN, sampler_SnowA, st).rg * 2 - 1;
    float3 snowN = normalize(float3(snN.x * 0.6, 1, snN.y * 0.6));
    float powderN = SPIceNoiseR(xz * 0.8 + 1.0, 1.13) * 0.65 + SPIceNoiseR(xz * 2.1 + 6.0, 2.41) * 0.35;   // large, soft
    // snow thinning out onto the ice: dense near the shore only where it spills, soft alpha, ice visible through it
    float cov = spillAmt * ((1 - r) * 1.35 + (powderN - 0.5) * 0.7) - 0.3;
    float dust = smoothstep(0.0, 0.9, cov) * 0.45 * _Dust;
    // ice8's soft frost clouds over the open ice (low frequency, faint, large)
    float cloud = smoothstep(0.55, 0.85, SPIceNoise(xz * 0.07 + 13.0) * 0.75 + SPIceNoise(xz * 0.2 + 2.0) * 0.25) * 0.18 * _Dust;
    dust = max(dust, cloud);
    // gnd11 (QA amateur #3: "ice is a flat empty slab"): a few blown snow patches out on the ice (soft-crisp, ~8 %),
    // thicker toward the banks, so the ice is not one empty slab
    {
        float2 pw2 = float2(SPIceNoise(xz * 0.1 + 31.0), SPIceNoise(xz * 0.1 + 47.0)) - 0.5;
        float pn2 = SPIceNoiseR(xz * 0.32 + pw2 * 2.5, 1.3) * 0.7 + SPIceNoiseR(xz * 1.4, 0.4) * 0.3;
        float paa2 = max(fwidth(pn2), 1e-4) * 2.0;
        float isle = smoothstep(0.63 - paa2, 0.77 + paa2, pn2 + saturate(1 + f / 3.0) * 0.06);   // gnd12: soft thin powder, not opaque white cut-outs
        dust = max(dust, isle * lerp(0.2, 0.4, powderN) * _Dust);
    }
    // gnd7 (view 15): past the arena edge the river runs into rising ground and ends; there the ice showed as a hard dark
    // rectangle beside the west bridge. Outside the arena the ice is snow-covered (patchy at first, then full).
    // gnd9: the ice runs under both bridges (x -66 / +61) and ends ~10 m past them, snow-covered, then cut raggedly
    float cutX = xz.x < 0 ? (-xz.x - 76.0) : (xz.x - 71.0);
    float outA = saturate((cutX + 4.0) / 6.0);
    dust = max(dust, outA * smoothstep(0.15, 0.55, powderN * 0.6 + outA * 0.7));
    if (outA + (powderN - 0.5) * 0.3 > 0.66) o.alpha = 0;   // gnd8/9: ragged end; the ground snow and the border forest beyond
    // r9 (Kido: "경계에는 좀 살얼음같은걸로 해보던지 눈을 살짝더 덮던지"): everywhere a thin patchy powder overlap of
    // 0.2-0.6 m onto the ice, so full snow never meets black ice in one step
    float tIn = max(-f, 0);                                              // metres in from the shoreline
    float pw = lerp(0.2, 0.6, SPIceNoise(float2(s * 0.2, side * 5.0 + 3.0)) * 0.7 + SPIceNoise(xz * 0.5 + 9.0) * 0.3);
    float pd = saturate(1 - tIn / pw);
    float pPatch = smoothstep(0.2, 0.65, SPIceNoiseR(xz * 2.4 + 4.0, 1.7) * 0.6 + SPIceNoiseR(xz * 6.0 + 1.0, 0.4) * 0.4 + pd * 0.45 - 0.15);
    dust = max(dust, pd * pd * pPatch * 0.25 * _Dust);   // gnd1 (QA #4): the powder halo along the cut read as a white ribbon - alpha -70%

    // ---- ice: photo scan, darkened, plus a deeper parallax copy (ice8)
    float3 V = i.viewWS;
    SPIceTaps it = SPIceMakeTaps(xz, max(_IceTile, 0.1), 1.7);
    float3 ia = lerp(SAMPLE_TEXTURE2D(_IceA, sampler_IceA, it.uvA).rgb, SAMPLE_TEXTURE2D(_IceA, sampler_IceA, it.uvB).rgb, it.w);
    float ir = lerp(SAMPLE_TEXTURE2D(_IceR, sampler_IceA, it.uvA).r, SAMPLE_TEXTURE2D(_IceR, sampler_IceA, it.uvB).r, it.w);
    float2 inrm = SPIceTapN(TEXTURE2D_ARGS(_IceN, sampler_IceA), it, 1.7, _IceNFlipY);
    float2 par = -V.xz / max(V.y, 0.3) * _CrackDepth;
    SPIceTaps dt = SPIceMakeTaps(xz + par, max(_IceTile, 0.1) * 1.6, 4.2);
    float3 da = SAMPLE_TEXTURE2D(_IceA, sampler_IceA, dt.uvA).rgb;
    float lum = dot(ia, float3(0.3, 0.55, 0.15));
    float dlum = dot(da, float3(0.3, 0.55, 0.15));
    float crack = smoothstep(0.35, 0.75, lum) * _Cracks;              // the scan's own pale cracks / specks stay pale
    // r6b (lead: read near-black in tr3): scan contribution x1.8 so the texture and cracks read like ice8
    float3 ice = _DeepCol.rgb * lerp(0.7, 1.3, saturate(lum * 2.2)) + ia * _IceTint.rgb * 3.2;   // gnd1/3: lifted toward #4b5869-#61707d (after the look pass contrast)
    ice += _CrackCol.rgb * smoothstep(0.4, 0.8, dlum) * 0.06;          // faint deep layer
    ice = lerp(ice, _CrackCol.rgb, crack * 0.75);
    float frost = SPIceNoise(float2(s * 0.18, ac * 1.4)) * SPIceNoise(float2(s * 0.05 + 4.0, ac * 0.45 + 1.0));
    frost = smoothstep(0.2, 0.6, frost) * _Frost;
    ice = lerp(ice, _CrackCol.rgb * 0.45, frost * 0.35);
    // r9: skim ice (살얼음) along the shore, irregular width 0.2-1.5 m: milky translucent grey-white ice with frost
    // feathers growing out from the shore and small milky bubbles, the ground snow faintly showing under it, fading
    // into the dark clear ice (soft, no line)
    float skimW = lerp(0.3, 1.6, smoothstep(0.25, 0.8, SPIceNoise(float2(s * 0.11, side * 7.0 + 1.0)) * 0.7 + SPIceNoise(xz * 0.3 + 8.0) * 0.3));
    float skimN = SPIceNoiseR(xz * 1.3 + 2.0, 0.8) * 0.6 + SPIceNoiseR(xz * 3.7 + 5.0, 2.2) * 0.4;
    float skim = saturate(1 - tIn / (skimW * lerp(0.55, 1.45, skimN)));
    skim = skim * skim * (3 - 2 * skim);
    // gnd1 (QA #4): broken - the skim only on some stretches of the shore, gone on others
    skim *= smoothstep(0.4, 0.62, SPIceNoise(float2(s * 0.07, side * 9.0 + 5.0)) * 0.7 + SPIceNoise(xz * 0.2 + 3.0) * 0.3);
    float feather = SPIceNoiseR(float2(s * 2.4, tIn * 0.8 + side * 3.0), 0.0) * 0.5 + SPIceNoiseR(xz * 3.1 + 4.0, 0.9) * 0.3 + SPIceNoiseR(float2(s * 6.0, tIn * 1.6), 0.0) * 0.2;
    float milky = step(0.78, SPIceNoiseR(xz * 16.0 + 3.0, 1.1)) * 0.5;
    float fe = smoothstep(0.45, 0.75, feather);                         // frost feathers: streaks growing out from the shore
    float3 skimCol = lerp(_CrackCol.rgb * 0.4, sa * 0.65, 0.3) * (0.55 + 0.75 * fe) + milky * _CrackCol.rgb * 0.35;
    ice = lerp(ice, skimCol, skim * lerp(0.45, 0.8, fe));             // translucent: the dark ice shows between the feathers
    // gnd1 (QA #5): frosted patches, 20-30 % of the ice, large and soft (#b4b1b4-#cad3e3 as frosted ice, not snow)
    float2 fw = float2(SPIceNoise(xz * 0.05 + 2.0), SPIceNoise(xz * 0.05 + 9.0)) - 0.5;
    float frostP = smoothstep(0.63, 0.69, SPIceNoise(xz * 0.09 + fw * 1.6 + 21.0) * 0.75 + SPIceNoiseR(xz * 0.4 + 4.0, 0.7) * 0.25);
    ice = lerp(ice, float3(0.42, 0.43, 0.47) * (0.45 + 1.1 * lum), frostP * 0.4);   // gnd2: crisper, smaller, the scan cracks still show through
    float iceSmooth = saturate(_Smooth * lerp(1.0, 0.75, saturate(ir * _RoughScale)) * (1 - frost * 0.3) * (1 - crack * 0.25) * (1 - skim * 0.35) * (1 - frostP * 0.4));
    float3 iceN = normalize(float3(inrm.x * _IceNormal, 1, inrm.y * _IceNormal));


    float snowW = dust;
    float3 alb = lerp(ice, sa, snowW);
    o.albedo = alb;
    o.normalWS = normalize(lerp(iceN, snowN, snowW));
    o.smoothness = lerp(iceSmooth, _SnowSmooth, snowW);
    o.rim = 0.6 * snowW;   // no fresnel rim on the ice (read as a white edge line)
    o.occlusion = lerp(0.12, 1, snowW);   // ice8: no probe GI on the glossy ice
    float gl = SPSparkleField(xz, i.viewWS) * 0.6;   // r13: was step(0.992) on a 2.5 cm grid (even sprinkle)
    o.sparkle = gl * _Sparkle * 0.012 * snowW;

#if defined(SP_PASS_FORWARD)
    float iceW = 1 - snowW;
    if (iceW > 0.001)
    {
        float rough = 1 - iceSmooth;
        float3 N = normalize(lerp(float3(0, 1, 0), iceN, 0.25));
        float3 R = reflect(-V, N);
        float ndv = saturate(dot(N, V));
        float fres = 0.04 + 0.96 * pow(1 - ndv, 5);
        float3 refl = _SkyCol.rgb;
        if (_SPIce_Params.x > 0.5)
        {
            float conf;
            float jit = RFHash(i.positionCS.xy * 0.731 + frac(_RF_Time * 7.3));
            float3 hit = SPIceSSR(i.ws + float3(0, 0.01, 0), R, jit, conf);
            refl = lerp(refl, hit * _SSRStrength, conf);
        }
        float3 em = refl * fres * _ReflStrength;
        if (_SPIce_Params.y > 0.5)
        {
            float3 g = 0;
            int cnt = (int)min(_SPIce_GlowCount, SP_ICE_MAX_GLOWS);
            float r2 = _GlowRadius * _GlowRadius;
            [loop] for (int k = 0; k < cnt; k++)
            {
                float4 gp = _SPIce_Glow[k];
                float2 dd = gp.xz - xz;
                if (dot(dd, dd) > r2) continue;
                g += _SPIce_GlowCol[k].rgb * SPIceGlowTerm(i.ws, gp.xyz, gp.w, rough, _GlowStretch);
            }
            em += g * _GlowStrength;
        }
        if (_SPIce_Params.z > 0.5)
        {
            float3 lc = _LanternCol.rgb * (0.9 + 0.2 * _SP_Flicker);
            [unroll] for (int l = 0; l < 4; l++)
            {
                float4 lp = _SP_Lanterns[l];
                if (lp.w <= 0) continue;
                float2 dd = lp.xz - xz;
                if (dot(dd, dd) > 64.0) continue;
                em += lc * SPIceGlowTerm(i.ws, lp.xyz, 0.35, rough, _GlowStretch) * _LanternStreak;
            }
            Light ml = GetMainLight();
            float3 rv = R - ml.direction;
            float3 right = UNITY_MATRIX_V[0].xyz;
            float lat = dot(rv, right);
            float vert = length(rv - lat * right);
            float a = 0.05 + rough * 0.12;
            float m = exp(-(lat * lat) / (a * a) - (vert * vert) / (a * a * 3.0));
            em += ml.color * m * _MoonStreak;
        }
        o.emission += em * iceW;
    }
#endif
}
#endif
