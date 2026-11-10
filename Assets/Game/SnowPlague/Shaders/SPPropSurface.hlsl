// Snow plague props: wood, straw mats, cloth, clay, stone lanterns, rocks, logs, bark (Poly Haven and our own kit).
// Albedo + GL normal map tiled in metres, vertex colour from the kit (R frost mask, G AO, B per-part random) when
// _VertexFrost is on, and the shared world-up snow rule on top (shaped by the normal map so snow sits on the bumps'
// upper faces). The base melts into the baked ground colour. _Emission + flicker for the lantern cores.
#ifndef SP_PROP_SURFACE
#define SP_PROP_SURFACE

TEXTURE2D(_BaseMap); TEXTURE2D(_BumpMap); SAMPLER(sampler_BaseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _Tint, _TintVar, _EmissionColor;
    float _NormalStrength, _Smoothness, _VertexFrost, _SnowBias, _GroundMelt, _AOStrength, _Desaturate, _Flicker, _Wetness;
CBUFFER_END

void SPSurface(SPInput i, inout SPOut o)
{
    float2 uv = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    float2 nxy = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv).rg * 2 - 1;
    float rnd = frac(i.extra.x + (_VertexFrost > 0.5 ? i.color.b : 0));
    a = lerp(a, dot(a, float3(0.3, 0.55, 0.15)).xxx, _Desaturate);
    a *= _Tint.rgb * lerp(1 - _TintVar.a, 1 + _TintVar.a, rnd) * lerp(1, _TintVar.rgb, rnd);
    float3 n = SPNormalTS(i, nxy, _NormalStrength);
    float ao = _VertexFrost > 0.5 ? lerp(1, i.color.g, _AOStrength) : 1;

    // snow: world-up rule + the kit's baked frost mask
    float vf = _VertexFrost > 0.5 ? i.color.r : 0;
    float s = SPSnow(n.y, vf, i.ws.xz, _SnowBias);
    s = saturate(max(s, vf * _SP_SnowAmount * smoothstep(0.1, 0.5, n.y + 0.2)) );
    float3 sc = SPSnowCol(0.6 + 0.4 * n.y);
    a = lerp(a, sc, s);

    // base melts into the ground colour
    float4 g = SPGroundMelt(i.ws, _GroundMelt);
    a = lerp(a, g.rgb, g.a * (1 - s));

    o.albedo = a * lerp(1, 0.75, _Wetness);
    o.normalWS = normalize(lerp(n, i.nws, s * 0.6));
    o.smoothness = lerp(lerp(_Smoothness, 0.55, _Wetness), 0.25, s);
    o.occlusion = ao;
    o.emission = _EmissionColor.rgb * lerp(1, 0.85 + 0.3 * _SP_Flicker, _Flicker);
    o.rim = 1;
}
#endif
