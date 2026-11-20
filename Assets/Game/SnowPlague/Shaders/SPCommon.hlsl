// Snow plague field: shared globals and helpers for every SnowPlague shader.
//   - one snow rule for ground, props and vegetation: saturate(dot(N, up)) x height/hollows x noise x _SP_SnowAmount
//   - rim light: cold by default, warm near the closest lit stone lantern (_SP_Lanterns)
//   - see-through: an ordered 4x4 Bayer dither hole around up to 3 players on screen (_PlayerScreenPos), opaque queue
//   - ground colour baked top-down (_SP_GroundTex) so props and rocks melt into the ground at their base
// Wind, trample map, noise and fog come from ReedField/ReedCommon.hlsl (globals set by the field script every frame).
#ifndef SP_COMMON_INCLUDED
#define SP_COMMON_INCLUDED

#include "Assets/Game/ReedField/Shaders/ReedCommon.hlsl"
#include "Assets/Game/RoomRun/Shaders/RRSeeThrough.hlsl"   // 11-13: soft see-through (declares _PlayerScreenPos)

float  _SP_SnowAmount;      // 0..1, whole field
float4 _SP_SnowParams;      // x: noise scale (1/m), y: noise amount, z: up threshold, w: softness
float4 _SP_Lanterns[4];     // xyz: lit lantern position, w: warm radius (0 = off)
float4 _SP_RimWarm;         // rgb: lantern rim colour (HDR), a: strength
float4 _SP_RimCold;         // rgb: moon rim colour, a: strength
float4 _SP_GroundRect;      // xy: min xz, zw: size of the baked ground albedo
float  _SP_GroundY;         // ground height of the arena (m)
float  _SP_GroundOn;
TEXTURE2D(_SP_GroundTex); SAMPLER(sampler_SP_GroundTex);
float4 _SP_SeeThrough;      // x: hole radius (fraction of screen height), y: soft edge, z: depth margin (m), w: 1 = on
float  _SP_Flicker;         // 0..1 lantern flicker, same for every lantern core and light shaft

// ---- seam-free noise (integer hash). ReedCommon RFNoise uses a float hash whose lattice corners can disagree between
// neighbouring cells after compiler re-association, which showed as 1 m block seams in the snow (ice worker, 11-11).
float SPHashI(int2 c)
{
    uint h = (uint)c.x * 0x8da6b343u ^ (uint)c.y * 0xd8163841u;
    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
    return (h & 0xffffffu) * (1.0 / 16777216.0);
}
float SPNoiseI(float2 p)
{
    float2 fl = floor(p), f = p - fl;
    int2 i = (int2)fl;
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(SPHashI(i), SPHashI(i + int2(1, 0)), u.x), lerp(SPHashI(i + int2(0, 1)), SPHashI(i + int2(1, 1)), u.x), u.y);
}
float SPFbmI(float2 p)
{
    float v = 0, a = 0.5;
    [unroll] for (int k = 0; k < 4; k++) { v += a * SPNoiseI(p); p = p * 2.03 + 17.1; a *= 0.5; }
    return v;   // same range as RFFbm, so tuned thresholds keep working
}

// ---- snow --------------------------------------------------------------------------------------------------------
// The "up" factor of the shared snow rule: 0 on walls, 1 on anything facing the sky. The ground uses it too (its mesh
// has slopes at the cliff edge), so ground, props and vegetation agree on what counts as an upward face.
float SPSnowUp(float upY)
{
    float th = _SP_SnowParams.z > 0 ? _SP_SnowParams.z : 0.45;
    float soft = _SP_SnowParams.w > 0 ? _SP_SnowParams.w : 0.25;
    return saturate((upY - th) / soft);
}

// up: world normal Y. hollow: 0..1 how sheltered/low the spot is (ground height map, vertex mask). bias shifts coverage.
// World-position noise only (no per-object data), so the pattern is continuous across neighbouring objects.
float SPSnow(float upY, float hollow, float2 xz, float bias)
{
    float scale = _SP_SnowParams.x > 0 ? _SP_SnowParams.x : 0.35;
    float amt = _SP_SnowParams.y > 0 ? _SP_SnowParams.y : 0.35;
    float n = RFNoise(xz * scale) * 0.65 + RFNoise(xz * scale * 4.3 + 7.1) * 0.35;
    float up = SPSnowUp(upY);
    float s = up * saturate(_SP_SnowAmount * 1.4 + bias + hollow * 0.5 + (n - 0.5) * amt * 2.0 - 0.35);
    return smoothstep(0.15, 0.6, s);
}

// Frost/snow colour: albedo sRGB ~200-235, a little blue in the shade.
float3 SPSnowCol(float detail) { return lerp(float3(0.58, 0.63, 0.69), float3(0.8, 0.83, 0.86), saturate(detail)); }

// ---- rim ------------------------------------------------------------------------------------------------------
// 0..1: how close the point is to the nearest lit lantern (squared falloff).
float SPLanternWarm(float3 ws)
{
    float warm = 0;
    [unroll] for (int k = 0; k < 4; k++)
    {
        float4 l = _SP_Lanterns[k];
        if (l.w <= 0) continue;
        warm = max(warm, saturate(1 - length(ws - l.xyz) / l.w));
    }
    return warm * warm;
}

float3 SPRim(float3 n, float3 v, float3 ws, float mask)
{
    float f = pow(1 - saturate(dot(n, v)), 4);
    float warm = SPLanternWarm(ws);
    float3 c = lerp(_SP_RimCold.rgb * _SP_RimCold.a, _SP_RimWarm.rgb * _SP_RimWarm.a * (0.9 + 0.2 * _SP_Flicker), warm);
    return c * f * mask;
}

// ---- see-through dither ---------------------------------------------------------------------------------------
float SPBayer4(float2 pix)
{
    uint2 p = uint2(pix) % 4;
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[p.y * 4 + p.x] + 0.5) / 16.0;
}

// 0 = keep, 1 = fully cut. positionCS.xy are pixel coordinates in the fragment stage.
float SPSeeThroughCut(float4 positionCS, float3 ws)
{
    // 11-13 (Kido: hard circle -> soft): in RoomRun the shared soft falloff (RRSeeThrough.hlsl, set by SeeThroughFX), in
    // every field (the arena-edge trees/houses of other fields use SP materials too); the hard hole below stays only for
    // the builders' editor captures, where SeeThroughFX does not run
    if (RRSeeThroughActive()) return RRSeeThroughCut(positionCS, ws);
    if (_SP_SeeThrough.w < 0.5) return 0;
    // 0..1 viewport, y up, same convention as Camera.WorldToViewportPoint. URP's own mapping handles the render
    // scale (_ScaledScreenParams) and the RT / back buffer flip, which the old _ScreenParams version did not.
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float eye = LinearEyeDepth(ws, GetWorldToViewMatrix());
    float aspect = _ScreenParams.x / _ScreenParams.y;
    float cut = 0;
    [unroll] for (int k = 0; k < 3; k++)
    {
        float4 p = _PlayerScreenPos[k];
        if (p.w < 0.5 || eye > p.z - _SP_SeeThrough.z) continue;   // only what is in front of that player
        float2 d = uv - p.xy; d.x *= aspect;
        float r = length(d);
        cut = max(cut, 1 - smoothstep(_SP_SeeThrough.x - _SP_SeeThrough.y, _SP_SeeThrough.x, r));
    }
    return cut;
}

// Discards the fragment inside the hole. The same function runs in the colour AND the depth passes, so a hole never
// writes depth (the occluded player's silhouette pass and the real player show through it).
void SPSeeThroughClip(float4 positionCS, float3 ws)
{
    float c = SPSeeThroughCut(positionCS, ws);
    if (c > 0) clip((RRSeeThroughActive() ? RRSeeDither(positionCS.xy) : SPBayer4(positionCS.xy)) - c);
}

// ---- ground melt ---------------------------------------------------------------------------------------------
// Blend a prop's colour toward the baked ground albedo near its base. Returns blend weight in .a.
float4 SPGroundMelt(float3 ws, float height)
{
    if (_SP_GroundOn < 0.5 || height <= 0) return 0;
    float2 uv = (ws.xz - _SP_GroundRect.xy) / _SP_GroundRect.zw;
    float3 g = SAMPLE_TEXTURE2D_LOD(_SP_GroundTex, sampler_SP_GroundTex, uv, 0).rgb;
    float w = saturate(1 - (ws.y - _SP_GroundY) / height);
    return float4(g, w * w * (0.8 + 0.2 * RFNoise(ws.xz * 3.0)));
}

// Per-instance variation without MaterialPropertyBlocks (keeps SRP Batcher / GPU Resident Drawer batching).
// SPInstanceRandAt takes the object origin in world space (the procedural-instancing path has no unity_ObjectToWorld).
float SPInstanceRandAt(float3 root) { return RFHash(root.xz * 0.731 + root.y); }
float SPInstanceRand() { return SPInstanceRandAt(TransformObjectToWorld(float3(0, 0, 0))); }
#endif
