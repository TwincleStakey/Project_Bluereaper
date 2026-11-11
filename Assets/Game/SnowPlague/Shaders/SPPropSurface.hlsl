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
    float _NormalStrength, _Smoothness, _VertexFrost, _SnowBias, _GroundMelt, _AOStrength, _Desaturate, _Flicker, _Wetness, _SnowCap, _VColAlbedo, _Triplanar, _TriScale;
CBUFFER_END

void SPSurface(SPInput i, inout SPOut o)
{
    float2 uv = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float3 a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    float2 nxy = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uv).rg * 2 - 1;
    // gnd (QA #11): opt-in world-space triplanar (_Triplanar, SP_MX_Plateau): the cliff scan was smeared along its UVs
    float3 triN = 0;
    if (_Triplanar > 0.5)
    {
        float3 bw = pow(abs(i.nws), 4); bw /= max(bw.x + bw.y + bw.z, 1e-4);
        float ts = max(_TriScale, 0.1);
        float2 uX = i.ws.zy / ts, uY = i.ws.xz / ts + 0.31, uZ = i.ws.xy / ts + 0.67;
        a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uX).rgb * bw.x + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uY).rgb * bw.y
          + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uZ).rgb * bw.z;
        float2 nX = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uX).rg * 2 - 1, nY = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uY).rg * 2 - 1;
        float2 nZ = SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, uZ).rg * 2 - 1;
        triN = (float3(0, nX.y, nX.x) * bw.x + float3(nY.x, 0, nY.y) * bw.y + float3(nZ.x, nZ.y, 0) * bw.z) * _NormalStrength;
    }
    a *= lerp(1.0.xxx, pow(saturate(i.color.rgb), 2.2), _VColAlbedo);   // tr28: scans coloured by vertex colour (sRGB)
    float rnd = frac(i.extra.x + (_VertexFrost > 0.5 ? i.color.b : 0));
    a = lerp(a, dot(a, float3(0.3, 0.55, 0.15)).xxx, _Desaturate);
    a *= _Tint.rgb * lerp(1 - _TintVar.a, 1 + _TintVar.a, rnd) * lerp(1, _TintVar.rgb, rnd);
    float3 n = _Triplanar > 0.5 ? normalize(i.nws + triN) : SPNormalTS(i, nxy, _NormalStrength);
    float ao = _VertexFrost > 0.5 ? lerp(1, i.color.g, _AOStrength) : 1;

    // snow: world-up rule + the kit's baked frost mask
    float vf = _VertexFrost > 0.5 ? i.color.r : 0;
    float s = SPSnow(n.y, vf, i.ws.xz, _SnowBias);
    // the kit frost mask only in patches, and held back by _SnowBias (straw mats read as solid white blobs 11-11)
    float patchN = RFNoise(i.ws.xz * 2.3 + i.ws.y * 1.7) * 0.7 + RFNoise(i.ws.xz * 7.1) * 0.3;
    float vfs = vf * _SP_SnowAmount * smoothstep(0.1, 0.5, n.y + 0.2) * saturate(1 + _SnowBias * 1.5) * smoothstep(0.3, 0.55, patchN + _SnowBias * 0.2);
    s = saturate(max(s, vfs));
    // tr23: _SnowCap - patchy snow lying on flat tops by the geometric normal (abs: some scans have flipped normals;
    // the normal map and the up-rule kept the bridge planks bare even at bias 0.8)
    float capN = RFNoise(i.ws.xz * 1.3 + 4.7) * 0.6 + RFNoise(i.ws.xz * 4.9 + 1.3) * 0.4;
    s = max(s, _SnowCap * smoothstep(0.35, 0.7, abs(i.nws.y)) * smoothstep(0.62, 0.42, capN - _SnowCap * 0.25));
    // thickness: thin dusting greys out over the material, thick caps are bright
    float thick = saturate(s * (0.6 + patchN * 0.8));
    float3 sc = SPSnowCol(0.55 + 0.45 * n.y) * lerp(0.9, 1.04, thick);
    a = lerp(a, sc, s);
    a = lerp(a, a * 0.8 + 0.05, s * (1 - thick) * 0.5);

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
