// Snow plague vegetation, one shader for dead trees, dead shrubs, frosted grass tufts, red maple and needle cards:
//   wind  : ReedCommon gust field, scaled by vertex colour A (sway weight baked in Blender, 0 at the roots)
//   trample: the LookStudy trample map flattens grass/shrubs where players and monsters walk
//   snow  : shared world-up rule + baked frost mask (vertex R); cards use the clump normal baked into the mesh
//   wither: colour along a gradient (olive > ochre > brown > grey) picked per instance from a world-position hash
//   light : wrap lighting for thin things; _ALPHATEST_ON for atlas cards (tight cards, opaque queue)
#ifndef SP_VEG_SURFACE
#define SP_VEG_SURFACE
#define SP_DEFORM

TEXTURE2D(_BaseMap); TEXTURE2D(_BumpMap); SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _Tint, _WitherA, _WitherB, _WitherC, _WitherD, _TipCol;
    float _UseTex, _UseNormal, _NormalStrength, _Wither, _Bend, _Trample, _SnowBias, _Wrap, _Smoothness, _AOStrength, _GroundMelt, _Cutoff, _SnowClump, _SnowCover, _Desat, _SnowScale, _SnowLift, _RimScale;
CBUFFER_END

// tr19: smooth snow breakup - value noise with a quintic fade, each octave rotated and offset, so no square lattice shows
// (the thatch roof read as a pixel mosaic with RFNoise at 4-7 / m)
float SPSmoothNoise1(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * f * (f * (f * 6 - 15) + 10);
    return lerp(lerp(RFHash(i), RFHash(i + float2(1, 0)), u.x), lerp(RFHash(i + float2(0, 1)), RFHash(i + float2(1, 1)), u.x), u.y);
}
float SPSmoothNoise(float2 p)
{
    float2 a = float2(p.x * 0.8 - p.y * 0.6, p.x * 0.6 + p.y * 0.8);
    float2 b = float2(a.x * 0.31 - a.y * 0.95, a.x * 0.95 + a.y * 0.31) * 2.03 + 17.3;
    float2 c = float2(b.x * 0.71 + b.y * 0.71, -b.x * 0.71 + b.y * 0.71) * 2.01 + 5.7;
    return SPSmoothNoise1(a) * 0.57 + SPSmoothNoise1(b) * 0.29 + SPSmoothNoise1(c) * 0.14;
}

float3 SPDeform(float3 ws, float3 root, SPAttr a)
{
    float sway = a.color.a;
    if (sway <= 0.001 || _Bend <= 0) return ws;
    float gust, tr;
    float2 offs = RFBend(root.xz, frac(a.color.b + RFHash(root.xz)), 0.6, gust, tr);
    float3 rel = ws - root;
    float h = max(rel.y, 0.01);
    // bend like the reeds (offset grows with sway^2), trampled grass lies down
    float2 o2 = offs * _Bend * sway * sway * h;
    o2 = lerp(o2, offs * h * sway * 0.9, saturate(tr * _Trample));
    ws.xz += o2;
    ws.y -= (dot(o2, o2) / max(h, 0.05)) * 0.5 + tr * _Trample * sway * h * 0.6;
    return ws;
}

void SPSurface(SPInput i, inout SPOut o)
{
    float2 uv = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float4 t = _UseTex > 0.5 ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) : float4(1, 1, 1, 1);
    float rnd = i.extra.x;
    // wither gradient per instance (+ slow world noise so neighbours drift together)
    float w = saturate(_Wither * 0.6 + rnd * 0.5 + (RFNoise(i.ws.xz * 0.05) - 0.5) * 0.6);
    float3 grad = w < 0.33 ? lerp(_WitherA.rgb, _WitherB.rgb, w / 0.33) : (w < 0.66 ? lerp(_WitherB.rgb, _WitherC.rgb, (w - 0.33) / 0.33) : lerp(_WitherC.rgb, _WitherD.rgb, (w - 0.66) / 0.34));
    // _Desat: winter needles (Kido 11-12: "초록색 거의 다 죽이고") - grey the albedo before the tint warms it
    float3 tc = lerp(t.rgb, dot(t.rgb, float3(0.3, 0.59, 0.11)).xxx, _Desat);
    float3 a = tc * _Tint.rgb * i.tint.rgb * lerp(float3(1, 1, 1), grad, _Wither > 0 ? 1 : 0);
    // tips paler (grass, twig ends)
    a = lerp(a, _TipCol.rgb, saturate(i.color.a) * _TipCol.a);
    float3 n = i.nws;
    if (_UseNormal > 0.5)
    {
        float2 nxy = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv).rg * 2 - 1;
        n = SPNormalTS(i, nxy, _NormalStrength);
    }
    float ao = lerp(1, i.color.g, _AOStrength);
    float s = SPSnow(n.y, i.color.r, i.ws.xz, _SnowBias);
    s = saturate(max(s, i.color.r * _SP_SnowAmount * smoothstep(0.0, 0.5, n.y + 0.15)));
    float3 snowCol = SPSnowCol(0.5 + 0.5 * n.y);
    if (_SnowClump > 0)
    {
        // pines (refs2 snowy_pines): snow as soft irregular cushions along the branch layers, dark needles between and
        // under. World-up (card clump normal / branch normal) x a 3D-ish world noise, soft falloff; the baked vertex
        // frost is ignored (it turned every up-facing card into a flat white slab). Thickness varies with the noise,
        // and sparse needle texels carry less snow so the needles still show through the edges.
    #if defined(_ALPHATEST_ON)
        // two-sided cards carry the clump normal: seen from the back it is flipped, which hid the snow on half the cards
        n *= n.y < 0 ? -1 : 1;
    #endif
        float sk = _SnowScale > 0 ? _SnowScale : 1;
        float m = SPSmoothNoise(i.ws.xz * 0.9 * sk + i.ws.y * 0.5 * sk);
        float up = smoothstep(0.2, 0.85, n.y);
        float th = 1 - _SnowCover;
        float sc = up * smoothstep(th - 0.12, th + 0.16, m) * saturate(_SP_SnowAmount * 1.3);
        sc *= lerp(0.75, 1, smoothstep(0.55, 0.95, t.a));
        // clumps: a finer noise breaks the cover into lumps (thick in the middle of a clump, speckled at its edge)
        float cl = SPSmoothNoise(i.ws.xz * 2.6 * sk + i.ws.y * 1.3 * sk + 11.7);
        sc = saturate(sc * lerp(0.7, 1.25, smoothstep(0.2, 0.8, cl)));
        s = saturate(sc * _SnowClump);
        snowCol *= lerp(0.78, 1.02, saturate((m - th) * 3)) * (1 + _SnowLift);   // _SnowLift: shadowed landmark roofs
        n = normalize(lerp(n, float3(0, 1, 0), s * 0.55));   // cushions shade rounder and softer than the cards
    }
    a = lerp(a, snowCol, s);
    float4 g = SPGroundMelt(i.ws, _GroundMelt);
    a = lerp(a, g.rgb, g.a * (1 - s));

    o.albedo = a;
    o.alpha = t.a * 2 - _Cutoff * 2 + 0.5;     // clip at alpha = _Cutoff (skeleton clips at 0.5)
    o.normalWS = n;
    o.smoothness = lerp(_Smoothness, 0.2, s);
    o.occlusion = ao;
    o.wrap = lerp(_Wrap, max(_Wrap, 0.55), s * saturate(_SnowClump));   // a little translucency in the snow
    o.rim = _RimScale;   // tr36: thin bare trunks seen from above were all rim (white sticks in 14)
    o.emission = snowCol * s * _SnowLift * 0.12;   // a little ambient lift on lifted snow only (0 by default)
}
#endif
