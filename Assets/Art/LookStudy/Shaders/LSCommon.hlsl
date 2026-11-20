#ifndef LS_COMMON_INCLUDED
#define LS_COMMON_INCLUDED
// Shared look of the study: painterly field colour, sky, cloud shade, aerial fog.
// All shading is driven by these globals, not by engine light intensity, so art direction
// (a shadow is a saturated cool colour, never black) stays under direct control.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float _LSTime;
float3 _LSSunCol;
float3 _LSShadowMul;
float3 _LSSkyTop;
float3 _LSSkyHor;
float3 _LSSkyGlow;
float3 _LSCloudCol;
float3 _LSFogCol;
float _LSFogDensity;
#ifndef RR_FOGSHIFT_DECLARED
#define RR_FOGSHIFT_DECLARED
float _RR_FogShift;   // RoomRun CameraRig: extra camera distance beyond zoom 1 (m); fog is measured without it
#endif
float _LSStars;
float _LSCloudShade;
float4 _LSPush;
float _LSTrailOn;
float4 _LSTrailRect;
TEXTURE2D(_LSTrail); SAMPLER(sampler_LSTrail);
#include "RRCut.hlsl"
#include "../../../Game/RoomRun/Shaders/RRSeeThrough.hlsl"   // 11-13: soft see-through near the player

// R = grass flattened, G = footprint, BA = push direction (0..1 encoded).
float4 LSTrail(float2 xz)
{
    if (_LSTrailOn < 0.5) return float4(0, 0, 0.5, 0.5);
    float2 uv = (xz - _LSTrailRect.xy) / _LSTrailRect.z;
    if (any(uv < 0.0) || any(uv > 1.0)) return float4(0, 0, 0.5, 0.5);
    return SAMPLE_TEXTURE2D_LOD(_LSTrail, sampler_LSTrail, uv, 0);
}

float LSHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float2 LSHash2(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.xx + p3.yz) * p3.zy);
}

float LSNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(LSHash(i), LSHash(i + float2(1, 0)), f.x),
                lerp(LSHash(i + float2(0, 1)), LSHash(i + float2(1, 1)), f.x), f.y);
}

float LSFbm(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { v += a * LSNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v / 0.9375;
}

// x = distance to nearest cell point (F1), y = distance to the cell edge (F2 - F1).
float2 LSVoronoi(float2 p)
{
    float2 n = floor(p), f = frac(p);
    float f1 = 8.0, f2 = 8.0;
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++)
    {
        float2 g = float2(i, j);
        float2 o = LSHash2(n + g);
        float d = length(g + o - f);
        if (d < f1) { f2 = f1; f1 = d; }
        else if (d < f2) { f2 = d; }
    }
    return float2(f1, f2 - f1);
}

// Slow drifting cloud shade over the whole world, gives large light/dark masses.
float LSCloud(float2 xz)
{
    float n = LSFbm(xz * 0.022 + float2(_LSTime * 0.018, _LSTime * 0.007));
    return lerp(1.0, smoothstep(0.34, 0.60, n), _LSCloudShade);
}

float3 LSSky(float3 d)
{
    d = normalize(d);
    float y = saturate(d.y);
    float3 col = lerp(_LSSkyHor, _LSSkyTop, pow(y, 0.55));

    float3 sun = _MainLightPosition.xyz;
    float s = saturate(dot(d, sun));
    col += _LSSkyGlow * (pow(s, 6.0) * 0.45 + pow(s, 60.0) * 0.8);
    col += _LSSunCol * smoothstep(0.9975, 0.9988, s) * 1.4;

    if (_LSStars > 0.001)
    {
        float2 q = d.xz / (abs(d.y) + 0.35);
        float band = exp(-pow(dot(d, normalize(float3(0.55, 0.25, -0.8))) * 3.2, 2.0));
        float dust = LSFbm(q * 3.0 + 9.0);
        col += _LSStars * float3(0.42, 0.30, 0.72) * band * smoothstep(0.25, 0.85, dust) * 1.1;
        col += _LSStars * float3(0.85, 0.35, 0.65) * band * smoothstep(0.62, 0.9, LSFbm(q * 1.6 + 3.0)) * 0.9;
        for (int layer = 0; layer < 3; layer++)
        {
            float2 g = q * (70.0 + layer * 75.0) + layer * 19.0;
            float2 id = floor(g);
            float h = LSHash(id);
            float2 c = LSHash2(id) - 0.5;
            float dist = length(frac(g) - 0.5 - c * 0.7);
            float density = lerp(0.97, 0.72, band);
            col += _LSStars * step(density, h) * smoothstep(0.16, 0.0, dist) * (0.5 + 1.8 * LSHash(id + 3.0)) * (1.0 - layer * 0.25);
        }
    }

    // Soft painterly cloud bank projected onto a flat layer.
    float2 cp = d.xz / (abs(d.y) + 0.22);
    float cl = LSFbm(cp * 1.05 + float2(_LSTime * 0.006, 0.0));
    cl = smoothstep(0.50, 0.72, cl) * smoothstep(0.0, 0.1, abs(d.y));
    col = lerp(col, _LSCloudCol, cl * (1.0 - _LSStars * 0.8));
    return col;
}

float3 LSFog(float3 col, float3 positionWS)
{
    float dist = max(length(_WorldSpaceCameraPos - positionWS) - _RR_FogShift, 0.0);
    float f = 1.0 - exp(-pow(dist * _LSFogDensity, 1.35));
    return lerp(col, _LSFogCol, saturate(f));
}

float LSShadow(float3 positionWS)
{
    return MainLightRealtimeShadow(TransformWorldToShadowCoord(positionWS));
}

#endif
