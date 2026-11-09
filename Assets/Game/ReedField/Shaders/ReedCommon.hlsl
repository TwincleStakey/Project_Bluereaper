// Shared bits for the reed field shaders: wind and gust field, the LookStudy trample map, things moving in the reeds,
// ground fog and colour grading. Globals are set by ReedField.cs every frame.
#ifndef REED_COMMON_INCLUDED
#define REED_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float  _RF_Time;
float4 _RF_Wind;        // xy: wind direction (normalized), z: strength, w: gust speed
float4 _RF_Gust;        // x: gust size (m), y: gust strength, z: flutter, w: sheen on gusts
float4 _RF_Stalkers[4]; // xz position, z unused, w radius (0 = off). Unseen things pushing through the reeds.
float4 _RF_FogCol;      // rgb, a: ground fog height (m)
float4 _RF_FogParams;   // x: ground fog density, y: distance fog density, z: fog noise amount, w: unused

// LookStudy trample map (written by LookStudyTrail): R = flattened amount, BA = push direction.
float _LSTrailOn;
float4 _LSTrailRect;
TEXTURE2D(_LSTrail); SAMPLER(sampler_LSTrail);

float4 RFTrail(float2 xz)
{
    if (_LSTrailOn < 0.5) return float4(0, 0, 0.5, 0.5);
    float2 uv = (xz - _LSTrailRect.xy) / _LSTrailRect.z;
    if (any(uv < 0) || any(uv > 1)) return float4(0, 0, 0.5, 0.5);
    return SAMPLE_TEXTURE2D_LOD(_LSTrail, sampler_LSTrail, uv, 0);
}

// Baked shadows (no realtime shadow pass for the field): reed masses and static props, top-down over the scene.
// _RF_ShadowRect = (x0, z0, sizeX, sizeZ). 1 = lit, 0 = shadow.
TEXTURE2D(_RF_ReedShadow); SAMPLER(sampler_RF_ReedShadow);
TEXTURE2D(_RF_PropShadow);
float4 _RF_ShadowRect;
float _RF_WaterLevel;
float _RF_ShadowOn;

float RFHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

float RFNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(RFHash(i), RFHash(i + float2(1, 0)), u.x), lerp(RFHash(i + float2(0, 1)), RFHash(i + float2(1, 1)), u.x), u.y);
}

float RFFbm(float2 p)
{
    float v = 0, a = 0.5;
    [unroll] for (int i = 0; i < 4; i++) { v += a * RFNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;
}

// 0..1 wind lane: soft bright bands that roll across the field, same scrolling-fbm idea as the LookStudy grass
// (LSWindLane), stretched across the wind so they read as gusts. Smooth in space and time, no jumps.
float RFGust(float2 xz)
{
    float2 dir = _RF_Wind.xy;
    float2 side = float2(-dir.y, dir.x);
    float size = max(_RF_Gust.x, 0.5);
    float2 p = float2(dot(xz, dir) / size, dot(xz, side) / (size * 3.0));
    p.x -= _RF_Time * 0.22 * _RF_Wind.w;
    return smoothstep(0.45, 0.75, RFFbm(p));
}

// Horizontal lean (metres per metre of height at the tip) at a reed root: steady lean + gust lanes + layered sway,
// trampling and unseen stalkers. Matches the LookStudy grass: offs * h^2, so the base stays planted.
float2 RFBend(float2 root, float rnd, float stiffness, out float gust, out float trample)
{
    gust = RFGust(root);
    float2 dir = _RF_Wind.xy;
    float2 side = float2(-dir.y, dir.x);
    float t = _RF_Time;
    float ph = rnd * 6.28 + dot(root, float2(0.6, 0.4));
    // Bend angle (radians at the tip): steady lean + gust lanes + gentle layered sway with a per-reed phase.
    float sway = sin(t * 1.3 + ph) * 0.035 + sin(t * 0.71 + ph * 1.7) * 0.025 + sin(t * 2.6 + ph * 2.3) * 0.01 * _RF_Gust.z;
    float soft = 1.15 - stiffness * 0.5;
    float amount = (0.2 + gust * 0.17 * _RF_Gust.y + sway) * _RF_Wind.z * soft;
    float2 offs = dir * amount + side * sin(t * 0.97 + ph * 1.3) * 0.03 * _RF_Wind.z * soft;

    // Trampled reeds lie down the way the player pushed them and rise back as the trail fades.
    float4 tr = RFTrail(root);
    trample = tr.r;
    float2 push = tr.ba * 2 - 1;
    offs = lerp(offs, normalize(push + 1e-4) * 1.1, saturate(trample));

    // Something unseen moving through the reeds parts them.
    [unroll] for (int k = 0; k < 4; k++)
    {
        float4 s = _RF_Stalkers[k];
        if (s.w <= 0) continue;
        float2 d = root - s.xy;
        float dist = length(d);
        float f = saturate(1 - dist / s.w);
        f = f * f;
        offs += (dist > 1e-3 ? d / dist : float2(0, 1)) * f * 0.8;
        offs += side * sin(t * 11 + rnd * 40) * f * 0.06;
    }
    return offs;
}

// Baked shadow at a ground point. The reed part sways a little with the gusts so it moves with the reeds.
float RFStaticShadow(float2 xz)
{
    if (_RF_ShadowOn < 0.5) return 1;
    float2 uv = (xz - _RF_ShadowRect.xy) / _RF_ShadowRect.zw;
    // cheap sway (one sine, no noise) so the baked reed shade breathes with the wind
    float2 sway = _RF_Wind.xy * 0.25 * _RF_Wind.z * sin(_RF_Time * 1.3 + dot(xz, float2(0.6, 0.4))) / _RF_ShadowRect.zw;
    float reed = SAMPLE_TEXTURE2D_LOD(_RF_ReedShadow, sampler_RF_ReedShadow, uv + sway, 0).r;
    float prop = SAMPLE_TEXTURE2D_LOD(_RF_PropShadow, sampler_RF_ReedShadow, uv, 0).r;
    return min(reed, prop);
}

// Low fog lying in the reeds plus distance fog. Fog thickness drifts with a slow noise.
float3 RFFog(float3 col, float3 ws)
{
    float n = RFNoise(ws.xz * 0.06 + float2(_RF_Time * 0.05, _RF_Time * 0.02));
    float low = saturate((_RF_FogCol.a - ws.y) / max(_RF_FogCol.a, 0.01));
    float fogLow = saturate(low * low * _RF_FogParams.x * lerp(1 - _RF_FogParams.z, 1 + _RF_FogParams.z, n));
    float dist = length(ws - _WorldSpaceCameraPos);
    // Distance haze starts beyond the play area, so the arena stays clear and the hills fade out.
    float fogFar = 1 - exp(-max(dist - _RF_FogParams.w, 0) * _RF_FogParams.y);
    col = lerp(col, _RF_FogCol.rgb, fogLow);
    return lerp(col, _RF_FogCol.rgb, saturate(fogFar));
}
#endif
