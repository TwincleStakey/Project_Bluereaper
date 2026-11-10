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
    float _UseTex, _UseNormal, _NormalStrength, _Wither, _Bend, _Trample, _SnowBias, _Wrap, _Smoothness, _AOStrength, _GroundMelt, _Cutoff;
CBUFFER_END

float3 SPDeform(float3 ws, SPAttr a)
{
    float sway = a.color.a;
    if (sway <= 0.001 || _Bend <= 0) return ws;
    float3 root = TransformObjectToWorld(float3(0, 0, 0));
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
    float3 a = t.rgb * _Tint.rgb * lerp(float3(1, 1, 1), grad, _Wither > 0 ? 1 : 0);
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
    a = lerp(a, SPSnowCol(0.5 + 0.5 * n.y), s);
    float4 g = SPGroundMelt(i.ws, _GroundMelt);
    a = lerp(a, g.rgb, g.a * (1 - s));

    o.albedo = a;
    o.alpha = t.a * 2 - _Cutoff * 2 + 0.5;     // clip at alpha = _Cutoff (skeleton clips at 0.5)
    o.normalWS = n;
    o.smoothness = lerp(_Smoothness, 0.2, s);
    o.occlusion = ao;
    o.wrap = _Wrap;
    o.rim = 1;
}
#endif
