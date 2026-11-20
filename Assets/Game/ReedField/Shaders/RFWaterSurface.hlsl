// Shared water surface for the reed field river and shallows (11-14, Kido: "흰 선 뭉치들 어설프고 웨이브 패턴이 너무
// 일정하고 어색"). The old surface was one normal map tiled every few metres, so the same ripple shape marched across
// the whole river, and sharp sun glints on it read as clumps of white specks. This one never visibly repeats:
//   - two octaves of the normal map at unrelated scales and angles (they only line up again after hundreds of metres);
//   - the lookup is domain-warped by low-frequency noise, so ripple rows bend and wander instead of running in lines;
//   - calm and ruffled patches (wind on water is never uniform): the ripple strength varies over ~30 m;
//   - ripples fade with camera distance so far water does not shimmer into speckle.
#ifndef RF_WATER_SURFACE_INCLUDED
#define RF_WATER_SURFACE_INCLUDED

float2 RFRot(float2 p, float a) { float s = sin(a), c = cos(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }

// 0..1: how ruffled the water is here (slowly drifting calm / ruffled patches)
float RFWaterPatch(float2 xz, float t)
{
    return smoothstep(0.3, 0.72, RFFbm(xz * 0.032 + float2(t * 0.018, -t * 0.007)));
}

// Surface slope (xz of the normal) at world xz, the pattern carried by 'off' metres (flow).
float2 RFWaterSlope(TEXTURE2D_PARAM(map, smp), float2 xz, float2 off, float scale, float t)
{
    float2 warp = float2(RFNoise(xz * 0.06 + float2(t * 0.011, 3.7)), RFNoise(xz * 0.06 + float2(9.1, -t * 0.009))) - 0.5;
    float2 p = xz + warp * 3.2 - off;
    float3 a = UnpackNormal(SAMPLE_TEXTURE2D(map, smp, RFRot(p, 0.31) / (scale * 1.9) + 0.13));
    float3 b = UnpackNormal(SAMPLE_TEXTURE2D(map, smp, RFRot(p, -0.87) / (scale * 0.77) + 0.61));
    return a.xy * 0.6 + b.xy * 0.4;
}

// Distance fade for the ripples (eye depth in metres): near water keeps its detail, far water goes calm.
float RFWaterNear(float eye) { return lerp(1.0, 0.35, saturate((eye - 22.0) / 40.0)); }

#endif
