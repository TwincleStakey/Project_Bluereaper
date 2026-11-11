// Snow plague ground. Redo 2 (11-11, Kido: "a clean flat blue-grey snow sheet"): patchy, dirty, trampled snow over a
// frozen forest floor. ~40% of the arena is broken/bare ground: leaf litter as the main bare layer (inside the arena too),
// wet dark soil and dense gravel in patches, snow lying in the hollows of the bare ground, thin snow that shows the
// ground's texture through it (not a flat grey veil), and brownish dirty snow where it is thin, trodden or wind-soiled.
// gr1 (Kido: snow "cut out with scissors", ss5 snow thins into the ground): gravel/pebbles is the bare base (hex-tiled),
// leaf litter and wet soil patches over it, snow (hex-tiled, A+N) laid by a level-vs-local-height threshold (see SPSurface)
// so it fills the gaps between stones first and breaks up into granular patches and specks over metres.
// Anti-tiling: hex-tile stochastic sampling on the two big layers, one rotated tap per noise-warped cell on the patch
// layers, macro noise on colour and coverage. Cost: 2 mask taps + 9 (gravel hex) + 3 (leaves) + 3 (soil) + 6 (snow hex,
// height from albedo) + 2 (slush) = 25 worst case, on the broken zone only.
// Where it goes comes from two masks painted by SPBuilder over the whole ground (~0.1 m per texel):
//   _MaskA  R snow cover, G wet soil, B gravel, A drift (fresh deep snow piled against props/rocks/trees)
//   _MaskB  R trodden path, G path direction (0..1 = 0..pi), B drag marks / cart ruts, A ice
// Footprints are procedural on the paths. Fresh snow sparkles (SPLit sparkle). Vertex colour: B leaf litter (outside the
// arena: deeper, redder), A snow allowed (under the foreground pines).
#ifndef SP_GROUND_SURFACE
#define SP_GROUND_SURFACE

TEXTURE2D(_SnowA); TEXTURE2D(_SnowN); TEXTURE2D(_SnowH);
TEXTURE2D(_GravelA); TEXTURE2D(_GravelN); TEXTURE2D(_GravelH);
TEXTURE2D(_MudA); TEXTURE2D(_MudN); TEXTURE2D(_MudH);
TEXTURE2D(_LeafA); TEXTURE2D(_LeafN); TEXTURE2D(_LeafH);
TEXTURE2D(_SlushA); TEXTURE2D(_SlushN); TEXTURE2D(_SlushH);   // redo 2: thin / dirty snow with grit (Megascans snow_gravel)
TEXTURE2D(_MaskA); TEXTURE2D(_MaskB);
SAMPLER(sampler_SnowA); SAMPLER(sampler_MaskA);

CBUFFER_START(UnityPerMaterial)
    float4 _SnowA_ST;
    float4 _Tiles;          // metres per tile: snow, gravel, mud, leaves
    float4 _SoilTint, _GravelTint, _LeafTint, _SnowTint, _RedLeaf, _ShadeTint;
    float4 _MaskRect;       // xy: min xz, zw: size (m) of the masks
    float _BlendDepth, _SnowFill, _SnowBias, _NormalStrength, _Calm, _RedLeaves, _Wet, _Footprints;
    float _HexScale, _Sparkle, _SnowFlatten, _Macro, _IceSmooth, _SnowSmooth;
    // redo 2
    float4 _DirtTint;       // dirty / trodden snow (brownish grey multiplier)
    float _DirtySnow, _ThinFrost, _HollowSnow, _SnowEdge, _BareNormal, _MudAmount, _GravelAmount, _GroundAO, _Slush, _SlushTile;
    float _SnowRelief, _MeltHoles;   // sn1
CBUFFER_END

struct SPLayer { float3 a; float2 n; float h; };

float2 SPHash22(float2 p)
{
    p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
    return frac(sin(p) * 43758.5453);
}

// One tap of a hex cell: random rotation + offset, explicit gradients (safe in branches), normal rotated back.
SPLayer SPTap(TEXTURE2D_PARAM(ta, sa), TEXTURE2D(tn), TEXTURE2D(th), float2 uv, float2 dx, float2 dy, float2 cell)
{
    float2 r = SPHash22(cell);
    float s, c; sincos(r.x * 6.2831853, s, c);
    float2x2 R = float2x2(c, -s, s, c);
    float2 u = mul(R, uv) + SPHash22(cell + 17.3) * 9.0;
    float2 gx = mul(R, dx), gy = mul(R, dy);
    SPLayer l;
    l.a = SAMPLE_TEXTURE2D_GRAD(ta, sa, u, gx, gy).rgb;
    l.n = mul(transpose(R), SAMPLE_TEXTURE2D_GRAD(tn, sa, u, gx, gy).rg * 2 - 1);
    l.h = SAMPLE_TEXTURE2D_GRAD(th, sa, u, gx, gy).r;
    return l;
}

// Hex-tile stochastic sampling (after Mikkelsen 2022): three rotated/offset copies blended over a triangle grid, the
// blend sharpened by each copy's height so the seams follow the texture (stones over gaps) instead of a visible lattice.
SPLayer SPHex(TEXTURE2D_PARAM(ta, sa), TEXTURE2D(tn), TEXTURE2D(th), float2 uv, float2 dx, float2 dy)
{
    float2 g = uv * _HexScale;
    float2 sk = float2(g.x - 0.57735027 * g.y, 1.15470054 * g.y);
    float2 b = floor(sk);
    float3 t = float3(frac(sk), 0); t.z = 1 - t.x - t.y;
    float sg = step(0, -t.z), s2 = 2 * sg - 1;
    float3 w = float3(-t.z * s2, sg - t.y * s2, sg - t.x * s2);
    SPLayer l1 = SPTap(TEXTURE2D_ARGS(ta, sa), tn, th, uv, dx, dy, b + float2(sg, sg));
    SPLayer l2 = SPTap(TEXTURE2D_ARGS(ta, sa), tn, th, uv, dx, dy, b + float2(sg, 1 - sg));
    SPLayer l3 = SPTap(TEXTURE2D_ARGS(ta, sa), tn, th, uv, dx, dy, b + float2(1 - sg, sg));
    w = w * w * w * (float3(l1.h, l2.h, l3.h) + 0.35);
    w /= max(w.x + w.y + w.z, 1e-4);
    SPLayer o;
    o.a = l1.a * w.x + l2.a * w.y + l3.a * w.z;
    o.n = l1.n * w.x + l2.n * w.y + l3.n * w.z;
    o.h = l1.h * w.x + l2.h * w.y + l3.h * w.z;
    return o;
}

// gr1: albedo + normal only (height from the albedo's luminance): the snow and slush scans are bright where they are
// high, so this costs 2 taps instead of 3 per copy
SPLayer SPTap2(TEXTURE2D_PARAM(ta, sa), TEXTURE2D(tn), float2 uv, float2 dx, float2 dy, float2 cell)
{
    float2 r = SPHash22(cell);
    float s, c; sincos(r.x * 6.2831853, s, c);
    float2x2 R = float2x2(c, -s, s, c);
    float2 u = mul(R, uv) + SPHash22(cell + 17.3) * 9.0;
    float2 gx = mul(R, dx), gy = mul(R, dy);
    SPLayer l;
    l.a = SAMPLE_TEXTURE2D_GRAD(ta, sa, u, gx, gy).rgb;
    l.n = mul(transpose(R), SAMPLE_TEXTURE2D_GRAD(tn, sa, u, gx, gy).rg * 2 - 1);
    l.h = dot(l.a, float3(0.3, 0.55, 0.15));
    return l;
}

SPLayer SPHex2(TEXTURE2D_PARAM(ta, sa), TEXTURE2D(tn), float2 uv, float2 dx, float2 dy)
{
    float2 g = uv * _HexScale;
    float2 sk = float2(g.x - 0.57735027 * g.y, 1.15470054 * g.y);
    float2 b = floor(sk);
    float3 t = float3(frac(sk), 0); t.z = 1 - t.x - t.y;
    float sg = step(0, -t.z), s2 = 2 * sg - 1;
    float3 w = float3(-t.z * s2, sg - t.y * s2, sg - t.x * s2);
    SPLayer l1 = SPTap2(TEXTURE2D_ARGS(ta, sa), tn, uv, dx, dy, b + float2(sg, sg));
    SPLayer l2 = SPTap2(TEXTURE2D_ARGS(ta, sa), tn, uv, dx, dy, b + float2(sg, 1 - sg));
    SPLayer l3 = SPTap2(TEXTURE2D_ARGS(ta, sa), tn, uv, dx, dy, b + float2(1 - sg, sg));
    w = w * w * w * (float3(l1.h, l2.h, l3.h) + 0.35);
    w /= max(w.x + w.y + w.z, 1e-4);
    SPLayer o;
    o.a = l1.a * w.x + l2.a * w.y + l3.a * w.z;
    o.n = l1.n * w.x + l2.n * w.y + l3.n * w.z;
    o.h = l1.h * w.x + l2.h * w.y + l3.h * w.z;
    return o;
}

// gr1: ~2-14 cm grain (three rotated value-noise octaves, so no axis-aligned grid shows): the snow's local threshold.
float SPGrain(float2 xz)
{
    float2 a = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
    float2 b = float2(xz.x * 0.34 + xz.y * 0.94, -xz.x * 0.94 + xz.y * 0.34);
    return SPNoiseI(xz * 7.3 + 1.7) * 0.5 + SPNoiseI(a * 19.0 + 5.1) * 0.3 + SPNoiseI(b * 47.0 + 9.3) * 0.2;
}

// Rotation cell for the single-tap patch layers: the cell grid is warped by noise so the (rare) seams wander like
// cracks in the ground instead of running in straight lines every N metres.
float2 SPCell(float2 xz, float size, float seed)
{
    float2 w = float2(SPNoiseI(xz * 0.19 + seed), SPNoiseI(xz * 0.19 + seed + 4.7)) - 0.5;
    return floor((xz + w * size * 0.9) / size) + seed;
}

// height blend: how much of 'top' (height ht, weight w) shows over the current surface of height h
float SPHB(float h, float ht, float w, float depth)
{
    return saturate((ht + w * 2 - 1 - h) / max(depth, 1e-3) + 0.5) * saturate(w * 4);
}

// sn1 (Kido: "where there is snow it doesn't look like snow, no texture"): procedural snow-surface height, 0..1.
// Wind ripples / sastrugi (crests across the wind, ~0.3 m apart, ~1 m long, ridged), larger drifts of them, crust lumps.
float SPSnowH(float2 xz)
{
    const float2 w = float2(0.79, 0.61);                 // wind (+x +z), same as the drifts in BuildMasks
    float u = dot(xz, w), v = dot(xz, float2(-w.y, w.x));
    float warp = SPNoiseI(xz * 0.35 + 4.4) * 2.0;
    float rip = SPNoiseI(float2(u * 2.4 + warp, v * 0.8));   // sn2: soft crests (sharp ones drew thin regular lines)
    rip = lerp(rip, 1 - abs(rip * 2 - 1), 0.4);
    float sas = SPNoiseI(float2(u * 1.1 - warp * 0.5, v * 0.42) + 13.3);
    float2 r1 = float2(xz.x * 0.8 - xz.y * 0.6, xz.x * 0.6 + xz.y * 0.8);
    float lump = SPNoiseI(xz * 4.7 + 7.7);               // ~20 cm crust lumps
    float crust = SPNoiseI(r1 * 11.0 + 2.1) * 0.6 + SPNoiseI(float2(r1.y, -r1.x) * 24.0 + 5.3) * 0.4;   // 4-9 cm granules
    return rip * lerp(0.2, 0.5, sas) * 0.5 + sas * 0.13 + lump * 0.22 + crust * 0.18 + 0.12;   // gnd1: wind ripples/sastrugi at half strength (read as diagonal bands)
}

// sn1: round melt holes (snow melts first round grass tufts and stones): at most one per 1.3 m cell, kept inside its
// cell. Returns x: hole (1 = bare), y: wet grey rim just outside it.
float2 SPMeltHole(float2 xz, float grain)
{
    const float C = 1.3;
    float2 cf = floor(xz / C); int2 ic = (int2)cf;
    float pres = step(0.72, SPHashI(ic + int2(11, 5)));   // sn5: fewer (sn4 read as a field of grey coins)
    float2 ctr = (cf + 0.3 + float2(SPHashI(ic), SPHashI(ic + int2(17, 3))) * 0.4) * C;
    float rad = 0.06 + 0.15 * SPHashI(ic + int2(7, 29));
    float d = length(xz - ctr) / rad + (grain - 0.5) * 1.2;   // ragged, not a clean disc
    float hole = (1 - smoothstep(0.75, 1.0, d)) * pres;
    float rim = smoothstep(0.8, 1.0, d) * (1 - smoothstep(1.0, 1.8, d)) * pres;
    return float2(hole, rim);
}

// Footprints: lanes of alternating left/right prints along the path direction (many walkers, not one line).
// Returns 0..1 print depth.
float SPFootprints(float2 xz, float dirT)
{
    float a = dirT * 3.14159265;
    float2 d = float2(cos(a), sin(a)), p = float2(-d.y, d.x);
    float along = dot(xz, d), side = dot(xz, p);
    const float lane = 0.62, stride = 0.78;
    float li = floor(side / lane);
    float lr = SPHashI(int2((int)li, 37));
    along += lr * stride * 3.0;                         // lanes out of step with each other
    float k = floor(along / stride);
    float kr = SPHashI(int2((int)li + 101, (int)k));
    float foot = fmod(abs(k), 2) * 2 - 1;               // left / right
    float2 q = float2(along - (k + 0.5) * stride, side - (li + 0.5) * lane - foot * 0.11 + (lr - 0.5) * 0.18);
    q = float2(q.x * cos(foot * 0.12) - q.y * sin(foot * 0.12), q.x * sin(foot * 0.12) + q.y * cos(foot * 0.12));
    float e = length(q / float2(0.15, 0.065));
    float keep = step(0.22, kr) * step(0.3, lr + 0.25);  // gaps: not every lane, not every step
    return (1 - smoothstep(0.65, 1.0, e)) * keep;
}

void SPSurface(SPInput i, inout SPOut o)
{
    float2 xz = i.ws.xz;
    float4 vc = i.color;
    float2 muv = (xz - _MaskRect.xy) / _MaskRect.zw;
    float4 mA = SAMPLE_TEXTURE2D(_MaskA, sampler_MaskA, muv);
    float4 mB = SAMPLE_TEXTURE2D(_MaskB, sampler_MaskA, muv);
    float m1 = SPFbmI(xz * 0.025 + 3.1), m2 = SPNoiseI(xz * 0.11 + 9.7), m3 = SPNoiseI(xz * 0.7 + 1.3);
    float m4 = SPNoiseI(float2(xz.x * 0.6 - xz.y * 0.8, xz.x * 0.8 + xz.y * 0.6) * 2.1 + 6.1);   // ~50 cm clumps
    float2 dxz = ddx(xz), dyz = ddy(xz);
    float3 dws = ddx(i.ws), dws2 = ddy(i.ws);
    const float3 LUM = float3(0.3, 0.55, 0.15);

    // ---- gr1 snow model (Kido: "cut out with scissors"; ss5 snow thins out into the ground over metres).
    // A smooth, multi-scale snow LEVEL (blurred painted mask + 25 m / 9 m / 1.4 m / 50 cm noise) is compared per pixel
    // against the LOCAL height = ground scan height (stones high, gaps low) + 2-14 cm grain. Where the level is high the
    // snow buries everything; as it drops, stone tops then pebbles then gravel poke through, until only specks are left
    // in the lowest gaps. So every border is a wide granular gradient, never a line, and there is no extra blur.
    float path = mB.r, drag = mB.b, ice = mB.a, drift = mA.a;
    float cover = mA.r * vc.a * lerp(0.7, 1.15, _SP_SnowAmount) + _SnowBias + drift * 0.4
                + (m2 - 0.5) * 0.3 + (m3 - 0.5) * 0.22 + (m4 - 0.5) * 0.2 + (m1 - 0.5) * 0.25 * _Macro;
    cover -= path * 0.15 + drag * 0.45;  // sn4: paths stay trodden snow so the print trails show                  // sn1: drag marks are grooves in the snow, not mud strips
    float L = cover * 1.3 - 0.15;                        // snow level: cover 0.12 -> nothing, 0.88 -> everything
    float up = SPSnowUp(i.nws.y);
    float edge = clamp(_SnowEdge, 0.02, 0.4);            // per-pixel softness of the snow against the local height
    float grain = SPGrain(xz);
    // sn1 melt holes: only in snow deep enough to show them, none on the fresh drifts. Computed before the bare-ground
    // branch so a hole in deep snow samples the real ground (sn5: holes showed the flat 0.5 grey placeholder)
    float2 mh = SPMeltHole(xz, grain) * (_MeltHoles * saturate(L * 2.5 - 0.7) * (1 - drift));

    // ---- bare ground: light grey-brown gravel and pebbles (base), leaf litter and wet soil patches over it
    float3 col = 0.5; float2 nxy = 0; float h = 0.5; float smo = 0.3; float wet = 0, tg = 1, bareOK = 0;
    [branch] if (L < 1.0 + edge || up < 0.999 || mh.x > 0.001)
    {
        bareOK = 1;
        SPLayer gr = SPHex(TEXTURE2D_ARGS(_GravelA, sampler_SnowA), _GravelN, _GravelH, xz / _Tiles.y, dxz / _Tiles.y, dyz / _Tiles.y);
        col = gr.a * _GravelTint.rgb; nxy = gr.n; h = gr.h; smo = 0.22;
        // gnd4 (QA #11): steep faces (cliff drop, bank slopes) get the gravel/rock scan triplanar instead of the
        // vertically smeared top-down projection; a little darker, like weathered rock
        float steep = 1 - smoothstep(0.5, 0.85, i.nws.y);
        [branch] if (steep > 0.01)
        {
            float2 an = abs(i.nws.xz); an /= max(an.x + an.y, 1e-3);
            float tt = _Tiles.y * 1.8;
            float3 tx = SAMPLE_TEXTURE2D_GRAD(_GravelA, sampler_SnowA, i.ws.zy / tt, dws.zy / tt, dws2.zy / tt).rgb;
            float3 tz = SAMPLE_TEXTURE2D_GRAD(_GravelA, sampler_SnowA, i.ws.xy / tt + 0.37, dws.xy / tt, dws2.xy / tt).rgb;
            float3 rock = (tx * an.x + tz * an.y) * _GravelTint.rgb * 0.72;
            col = lerp(col, rock, steep); nxy *= 1 - steep; h = lerp(h, 0.5, steep);
        }
        // leaf litter: in drifts of its own between the gravel (more under the border trees, vertex B), sits in the gaps
        float wl = saturate(vc.b * 1.2 + smoothstep(0.5, 0.8, m2 * 0.45 + m4 * 0.3 + (1 - mA.b) * 0.25) * saturate(2.0 - _GravelAmount));
        [branch] if (wl > 0.02)
        {
            SPLayer lf = SPTap(TEXTURE2D_ARGS(_LeafA, sampler_SnowA), _LeafN, _LeafH, xz / _Tiles.w, dxz / _Tiles.w, dyz / _Tiles.w, SPCell(xz, 12.0, 3.1));
            float t = SPHB(h, lf.h, wl, _BlendDepth);
            float lum = dot(lf.a, LUM);
            float redT = smoothstep(0.78, 0.86, SPNoiseI(xz * 7.0) * 0.6 + m2 * 0.4) * _RedLeaves * (0.35 + 0.65 * vc.b);
            float3 lc = lerp(lf.a * _LeafTint.rgb * (0.9 + 0.25 * vc.b), _RedLeaf.rgb * (0.6 + lum * 1.6), redT);
            col = lerp(col, lc, t); nxy = lerp(nxy, lf.n, t); h = lerp(h, lf.h, t); smo = lerp(smo, 0.18, t); tg = 1 - t;
        }
        // wet dark soil: churned mud round the wet mask, the drag trail, the paths, a few noise patches
        float wm = saturate(mA.g * 1.4 + drag * 0.9 + path * 0.3 + smoothstep(0.55, 0.8, m2 * 0.55 + m3 * 0.25 + m1 * 0.2) * _MudAmount);
        [branch] if (wm > 0.02)
        {
            SPLayer mu = SPTap(TEXTURE2D_ARGS(_MudA, sampler_SnowA), _MudN, _MudH, xz / _Tiles.z, dxz / _Tiles.z, dyz / _Tiles.z, SPCell(xz, 13.0, 1.7));
            float t = SPHB(h, mu.h, wm, _BlendDepth);
            col = lerp(col, mu.a * _SoilTint.rgb, t); nxy = lerp(nxy, mu.n, t); h = lerp(h, mu.h, t); smo = lerp(smo, 0.3, t); tg *= 1 - t;
        }
        // calm the scans (keep tone, lose the orange), then wet: darker, glossy, flatter
        float lumc = dot(col, LUM);
        col = lerp(col, lumc.xxx * float3(1.0, 0.98, 0.88), _Calm);   // gnd1: grey-olive, no pink
        wet = saturate(mA.g * 1.2 + drag * 0.8 + path * 0.3 + (m2 - 0.5) * 0.3) * _Wet;
        col *= 1 - wet * 0.4;
        smo = lerp(smo, 0.7, wet * (1 - tg * 0.4));
        nxy *= (1 - wet * 0.45) * _BareNormal;
        col *= 0.88 + 0.24 * m1;
    }

    // local height: the ground's own relief (snow settles between the stones first) plus grain; contrast stretched so
    // the granular band spans a wide range of the level
    float local = saturate((lerp(grain, h, saturate(_SnowFill * 0.6)) - 0.5) * 2.6 + 0.5);

    // ---- snow: sampled only where it can show
    SPLayer sn; sn.a = 0.8; sn.n = 0; sn.h = 0.5;
    [branch] if (L > -edge && up > 0.001)
        sn = SPHex2(TEXTURE2D_ARGS(_SnowA, sampler_SnowA), _SnowN, xz / _Tiles.x, dxz / _Tiles.x, dyz / _Tiles.x);

    float s = L - local - mh.x * 2.6;
    float snowT = saturate(s / edge + 0.5) * up;
    float thick = saturate(s * 2.2) * saturate(L * 1.3);  // 0 at a speck / the stone's edge, 1 where it is deep

    // sn1 snow relief: procedural ripples / crust (finite differences, 3 evaluations, only where snow shows)
    float hs = 0.5; float2 reliefN = 0;
    [branch] if (snowT > 0.001)
    {
        const float e = 0.012;
        hs = SPSnowH(xz);
        float hx = SPSnowH(xz + float2(e, 0)), hz = SPSnowH(xz + float2(0, e));
        reliefN = -float2(hx - hs, hz - hs) / e * 0.045 * _SnowRelief * lerp(0.35, 1.0, thick) * (1 - drift * 0.5);
    }

    // snow albedo: scan marks, macro drift; blue-grey in every dip of the relief, only the crests near white
    float3 sa = 0.8 + (sn.a - 0.8) * _SnowFlatten;
    float3 snowCol = sa * _SnowTint.rgb * (0.94 + 0.1 * m1 * _Macro + 0.04 * m3);
    float dip = saturate((0.6 - hs) * 3.0) * 0.85 + saturate((0.6 - sn.h) * 1.6) * 0.25;
    snowCol = lerp(snowCol, snowCol * _ShadeTint.rgb, saturate(dip) * lerp(0.5, 1.0, thick) * 0.5);   // gnd1: half (diagonal wind bands)
    snowCol *= lerp(0.97, 1.04, saturate((hs - 0.55) * 3));
    snowCol = lerp(snowCol, snowCol * 1.04, drift);
    // dirty snow: thin, trodden, soiled by blown earth; takes the colour of the ground under it
    float soil = smoothstep(0.42, 0.78, SPFbmI(xz * 0.21 + 7.1) * 0.65 + m3 * 0.35);
    float thin = 1 - saturate(L * 1.1);                  // the whole broken zone, not only the grain edges
    float dirty = saturate(thin * 0.08 + soil * 0.2 + path * 0.35 + drag * 0.4 - drift * 1.3) * _DirtySnow;   // sn1: thin snow stays white stipple
    float3 groundTone = bareOK > 0.5 ? saturate(col * 1.9 + 0.28) : float3(0.75, 0.7, 0.64);
    float3 dirtCol = _DirtTint.rgb * lerp(float3(1, 1, 1), groundTone, 0.6);
    // broken / thin snow turns into gritty slush (one rotated tap of the snow-gravel scan, only where it shows)
    float slushW = saturate(thin * 1.3 + path * 0.4 + drag * 0.4 - drift) * _Slush;
    float2 slushN = 0; float slushT = 0;
    [branch] if (slushW * snowT > 0.02)
    {
        SPLayer sl = SPTap2(TEXTURE2D_ARGS(_SlushA, sampler_SnowA), _SlushN, xz / _SlushTile, dxz / _SlushTile, dyz / _SlushTile, SPCell(xz, 9.0, 8.9));
        slushT = slushW * saturate((0.6 - sl.h) * 1.2 + slushW);   // grit shows first where the slush scan is dark
        snowCol = lerp(snowCol, sl.a * _SnowTint.rgb * 1.05, slushT);
        slushN = sl.n;
    }
    snowCol = lerp(snowCol, snowCol * dirtCol, dirty * (1 - slushT * 0.4));
    // trodden: packed brownish-grey snow, footprints pressed in
    float fp = SPFootprints(xz, mB.g) * saturate(path * 2.5) * _Footprints * saturate(snowT * 2);
    float trod = saturate(path * 0.75 + fp * 0.6);
    snowCol = lerp(snowCol, snowCol * lerp(float3(0.76, 0.77, 0.79), _DirtTint.rgb * 0.85, _DirtySnow * 0.5), path * 0.6);
    // sn1: prints are sky-blue shadowed hollows (ground sheet 1/2/4), drag marks blue-grey grooves with a little dirt
    snowCol = lerp(snowCol, snowCol * _ShadeTint.rgb * float3(0.76, 0.78, 0.84), fp);
    snowCol = lerp(snowCol, snowCol * _ShadeTint.rgb * lerp(float3(0.85, 0.88, 0.95), _DirtTint.rgb, 0.4), saturate(drag * 1.3) * 0.8);
    // melt-hole rims: wet, a little grey
    snowCol = lerp(snowCol, snowCol * float3(0.78, 0.8, 0.84), mh.y * 0.7);
    float3 under = lerp(col, dot(col, LUM).xxx, 0.5);
    float3 frosted = saturate(under * (1 + 0.9 * _ThinFrost) + 0.1 * _ThinFrost);
    // a trace of hoarfrost on the bare ground right next to the snow (no grey veil: ground sheet 7)
    // gnd6 (QA #7): bare ground next to the snow is lighter and gritty (hoarfrost, snow grains in the gaps), so the bare
    // areas do not read as dark smudges at the game camera: a ~1 m lighter fringe broken up by the grain
    float nearSnow = smoothstep(0.05, 0.55, L) * (1 - snowT) * bareOK;
    float grit = smoothstep(0.45, 0.75, grain + (h - 0.5) * 0.6 + L * 0.5);
    float rime = nearSnow * _ThinFrost * (0.1 + 0.55 * grit);
    col *= 1 - mh.y * 0.25 * (1 - snowT);              // wet ground showing in the holes
    col = lerp(col, frosted, rime);
    col *= 1 + nearSnow * 0.18;
    col = lerp(col, snowCol, snowT);

    float2 snowN = lerp(sn.n * lerp(0.4, 0.2, drift) + reliefN, slushN, saturate(slushT));   // sn1: scan grain + procedural relief
    float2 fpg = float2(ddx(fp), ddy(fp));
    snowN += float2(-fpg.x, fpg.y) * 4.0 * fp;
    nxy = lerp(nxy, snowN, snowT * lerp(0.4, 1.0, thick));   // thin snow keeps the ground's relief
    float snowSmo = _SnowSmooth + 0.08 * m3 + drift * 0.06 - dirty * 0.08 - (1 - thick) * 0.08;   // powder: matte
    snowSmo = lerp(snowSmo, 0.55, trod);
    smo = lerp(smo, snowSmo, snowT);

    // ice: frozen puddles / glazed packed snow, dark blue-grey, mirror-like
    float iceT = saturate(ice * 1.3 - 0.15 + (m3 - 0.5) * 0.4) * (1 - drift);
    col = lerp(col, lerp(col * 0.45, float3(0.2, 0.25, 0.31), 0.55), iceT);
    smo = lerp(smo, _IceSmooth, iceT);
    nxy *= 1 - iceT * 0.85;

    // gnd1 (QA #15): outside the arena a 60-100 m macro variation over the detail so the outer terrain does not tile
    {
        float2 oo = max(abs(xz - _RF_FogArena.xy) - _RF_FogArena.zw, 0);
        float outside = _RF_FogArena.z > 0 ? saturate(length(oo) / 6.0) : 0;
        float macro = SPFbmI(xz * 0.012 + 5.3) * 0.7 + SPNoiseI(xz * 0.035 + 1.9) * 0.3;
        col *= lerp(1.0, lerp(0.78, 1.12, macro), outside);
    }
    o.albedo = col;
    o.normalWS = normalize(i.nws + float3(nxy.x, 0, nxy.y) * _NormalStrength);
    o.smoothness = saturate(smo);
    // cavity occlusion from the height: stones and pebbles read as relief, not a printed photo
    o.occlusion = lerp(1 - _GroundAO, 1.0, saturate(h * 1.6 + snowT * thick));
    o.occlusion *= 1 - snowT * thick * saturate((0.5 - hs) * 2.5) * 0.25 - fp * 0.2;   // sn1: crust / print cavities
    o.rim = 0;

    // sparkle: tiny glints in fresh snow (~3.5 cm cells), each lights up for a narrow band of view directions
    float2 sc = xz * 15.0;   // sn2: ~7 cm cells so a glint covers a pixel or two at the game camera
    float2 cid = floor(sc);
    float2 r = float2(SPHashI((int2)cid), SPHashI((int2)cid + int2(5, 9)));
    float2 cp = frac(sc) - 0.25 - r * 0.5;
    float dot1 = 1 - smoothstep(0.08, 0.24, length(cp));
    float tw = frac(r.x * 37.1 + dot(i.viewWS, float3(5.3, 9.1, 7.7)) * (0.6 + r.y));
    float glint = dot1 * smoothstep(0.91, 0.97, tw) * step(0.72, r.y) * (0.4 + 0.6 * frac(r.x * 13.7));   // sn4: sparse, uneven (sn3 read as polka dots)
    o.sparkle = glint * snowT * saturate(thick * 1.5) * (1 - trod * 0.7) * (1 - dirty * 0.6) * (1 - iceT) * lerp(0.6, 1.2, saturate((hs - 0.4) * 2.5)) * _Sparkle;
}
#endif
