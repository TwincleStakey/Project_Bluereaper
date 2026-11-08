#ifndef UW_COMMON_INCLUDED
#define UW_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Global clock set by UWClock (play mode) or the preview builder (batch), so animation is deterministic.
float _UWTime;

float UWHash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
float2 UWHash22(float2 p) { float n = UWHash21(p); return float2(n, UWHash21(p + n)); }

float UWNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(UWHash21(i), UWHash21(i + float2(1, 0)), u.x),
                lerp(UWHash21(i + float2(0, 1)), UWHash21(i + float2(1, 1)), u.x), u.y);
}

float UWFbm(float2 p)
{
    float v = 0.0, a = 0.5;
    [unroll] for (int k = 0; k < 5; k++) { v += a * UWNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;
}

// x = distance to nearest cell point, y = distance to the cell border (crack lines)
float2 UWVoronoi(float2 p)
{
    float2 n = floor(p), f = frac(p);
    float2 mg, mr; float md = 8.0;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        float2 g = float2(i, j);
        float2 r = g + UWHash22(n + g) - f;
        float d = dot(r, r);
        if (d < md) { md = d; mr = r; mg = g; }
    }
    float bd = 8.0;
    [unroll] for (int j2 = -2; j2 <= 2; j2++)
    [unroll] for (int i2 = -2; i2 <= 2; i2++)
    {
        float2 g = mg + float2(i2, j2);
        float2 r = g + UWHash22(n + g) - f;
        if (dot(mr - r, mr - r) > 0.00001) bd = min(bd, dot(0.5 * (mr + r), normalize(r - mr)));
    }
    return float2(sqrt(md), bd);
}
#endif
