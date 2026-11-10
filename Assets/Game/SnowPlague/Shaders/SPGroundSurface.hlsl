// Snow plague ground: four layers mixed by height (TARAE-style): wet dark soil, gravel/stones, dead leaves (a few red
// maple leaves), packed snow. Snow fills the gaps between the stones first, then covers flat open ground; footprints
// (LookStudy trample map) press it down. Vertex colour from the ground mesh:
//   R gravel path / stony ground, G wet mud (darker, glossy), B leaf litter (near trees), A snow allowed (0 = bare:
//   under trees, round the dead, trodden yard)
#ifndef SP_GROUND_SURFACE
#define SP_GROUND_SURFACE

TEXTURE2D(_SnowA); TEXTURE2D(_SnowN); TEXTURE2D(_SnowH);
TEXTURE2D(_GravelA); TEXTURE2D(_GravelN); TEXTURE2D(_GravelH);
TEXTURE2D(_MudA); TEXTURE2D(_MudN); TEXTURE2D(_MudH);
TEXTURE2D(_LeafA); TEXTURE2D(_LeafN); TEXTURE2D(_LeafH);
SAMPLER(sampler_SnowA);

CBUFFER_START(UnityPerMaterial)
    float4 _SnowA_ST;
    float4 _Tiles;          // metres per tile: snow, gravel, mud, leaves
    float4 _SoilTint, _GravelTint, _LeafTint, _SnowTint, _RedLeaf;
    float _BlendDepth, _SnowFill, _SnowBias, _NormalStrength, _Calm, _RedLeaves, _Wet, _Footprints;
CBUFFER_END

struct SPLayer { float3 a; float2 n; float h; };

float2 SPRot(float2 p, float s, float c) { return float2(p.x * c - p.y * s, p.x * s + p.y * c); }

SPLayer SPSample(TEXTURE2D_PARAM(ta, sa), TEXTURE2D(tn), TEXTURE2D(th), float2 uv)
{
    SPLayer l;
    l.a = SAMPLE_TEXTURE2D(ta, sa, uv).rgb;
    l.n = SAMPLE_TEXTURE2D(tn, sa, uv).rg * 2 - 1;
    l.h = SAMPLE_TEXTURE2D(th, sa, uv).r;
    return l;
}

// height blend: how much of 'top' (height ht, weight w) shows over the current surface of height h
float SPHB(float h, float ht, float w, float depth)
{
    return saturate((ht + w * 2 - 1 - h) / max(depth, 1e-3) + 0.5) * saturate(w * 4);
}

void SPSurface(SPInput i, inout SPOut o)
{
    float2 xz = i.ws.xz;
    float4 vc = i.color;
    float n1 = RFFbm(xz * 0.045 + 3.1), n2 = RFNoise(xz * 0.17 + 9.7), n3 = RFNoise(xz * 0.6 + 1.3);

    // base: wet dark soil
    SPLayer mud = SPSample(TEXTURE2D_ARGS(_MudA, sampler_SnowA), _MudN, _MudH, SPRot(xz, 0.31, 0.95) / _Tiles.z);
    float3 col = mud.a * _SoilTint.rgb;
    float2 nxy = mud.n;
    float h = mud.h * 0.6;
    float smooth = 0.25;

    // gravel / stones (vertex R + broken noise)
    float wg = saturate(vc.r * 1.1 + (n1 - 0.5) * 0.7 + (n3 - 0.5) * 0.2);
    SPLayer gr = SPSample(TEXTURE2D_ARGS(_GravelA, sampler_SnowA), _GravelN, _GravelH, SPRot(xz, 0.83, 0.56) / _Tiles.y);
    float tg = SPHB(h, gr.h, wg, _BlendDepth);
    col = lerp(col, gr.a * _GravelTint.rgb, tg); nxy = lerp(nxy, gr.n, tg); h = lerp(h, gr.h, tg); smooth = lerp(smooth, 0.18, tg);

    // dead leaves (vertex B), a few red maple ones
    float wl = saturate(vc.b * smoothstep(0.35, 0.65, n2 * 0.7 + n3 * 0.3) * 1.4);
    [branch] if (wl > 0.01)
    {
        float2 lu = SPRot(xz, 0.59, 0.81) / _Tiles.w;
        SPLayer lf = SPSample(TEXTURE2D_ARGS(_LeafA, sampler_SnowA), _LeafN, _LeafH, lu);
        float lum = dot(lf.a, float3(0.3, 0.55, 0.15));
        float3 lc = lerp(lf.a * _LeafTint.rgb, _RedLeaf.rgb * (0.6 + lum * 1.6), smoothstep(0.78, 0.86, RFNoise(xz * 7.0) * 0.6 + n2 * 0.4) * _RedLeaves);
        float tl = SPHB(h, lf.h, wl, _BlendDepth);
        col = lerp(col, lc, tl); nxy = lerp(nxy, lf.n, tl); h = lerp(h, lf.h, tl); smooth = lerp(smooth, 0.12, tl);
    }

    // wet mud patches (vertex G): darker, glossier, flatter
    float wet = saturate(vc.g * 1.2 + (n2 - 0.5) * 0.4) * _Wet;
    col *= 1 - wet * 0.45; smooth = lerp(smooth, 0.6, wet * (1 - tg * 0.5)); nxy *= 1 - wet * 0.5;

    // snow: global amount x allowed (vertex A) x up; fills the low points first (between stones), then flat ground
    SPLayer sn = SPSample(TEXTURE2D_ARGS(_SnowA, sampler_SnowA), _SnowN, _SnowH, xz / _Tiles.x);
    float allowed = vc.a;
    float cover = _SP_SnowAmount * allowed + _SnowBias + (n1 - 0.5) * 0.6 + (n2 - 0.5) * 0.25;
    float snowT = saturate((cover + (0.55 - h) * _SnowFill + sn.h * 0.25 - 0.5) / 0.12);
    // footprints / trodden snow: the trample map packs it down to grey slush
    float4 trl = RFTrail(xz);
    float trod = saturate(trl.r * _Footprints);
    float3 snowCol = sn.a * _SnowTint.rgb;
    snowCol = lerp(snowCol, snowCol * float3(0.55, 0.6, 0.66), trod);
    col = lerp(col, snowCol, snowT);
    nxy = lerp(nxy, sn.n * 0.5, snowT);
    smooth = lerp(smooth, lerp(0.2, 0.55, trod), snowT);

    // calm the scans so combat reads (keep tone, lose some contrast), slow big-scale variation
    float lumc = dot(col, float3(0.3, 0.55, 0.15));
    col = lerp(col, lumc.xxx * normalize(col + 1e-4) * 1.732 * 0.5 + col * 0.5, _Calm * 0.5);
    col *= 0.9 + 0.2 * n1;

    o.albedo = col;
    o.normalWS = normalize(i.nws + float3(nxy.x, 0, nxy.y) * _NormalStrength);
    o.smoothness = smooth;
    o.occlusion = lerp(0.75, 1.0, saturate(h * 1.5));
    o.rim = 0;
}
#endif
