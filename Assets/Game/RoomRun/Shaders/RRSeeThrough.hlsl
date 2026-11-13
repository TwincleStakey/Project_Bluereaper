// Soft see-through for whatever stands between the camera and the player (11-13, Kido: "the hard circle cut-out should
// be soft and natural like other top-down games"). Include after URP Core.hlsl.
//
// A screen-space radial falloff around each player's screen position (aspect-corrected, radius already converted from
// metres by RoomRun.SeeThroughFX so it follows the camera zoom):
//     cut = (1 - smoothstep(inner, outer, dist)) * (1 - minVisibility) * heightRamp
// applied as a dithered clip (interleaved gradient noise, opaque queue: no sorting, depth stays right). The centre keeps
// minVisibility of the occluder, so a tree thins out around the player instead of a hollow hole with a hard rim.
// Only fragments closer to the camera than the player (depth margin) and higher than the player's knees (height ramp)
// fade, so grass, the ground, low rocks and things behind the player never get a hole.
#ifndef RR_SEETHROUGH_INCLUDED
#define RR_SEETHROUGH_INCLUDED

#ifndef RR_PLAYERSCREENPOS_DECLARED
#define RR_PLAYERSCREENPOS_DECLARED
float4 _PlayerScreenPos[3]; // xy: viewport 0..1 (y up, Camera.WorldToViewportPoint), z: linear eye depth, w: 1 = active
#endif
float4 _RR_SeeShape;        // x: inner radius, y: outer radius (fraction of screen height), z: min visibility, w: depth margin (m). y = 0: off
float4 _RR_SeeShape2;       // x: player feet height (world y), y/z: fade starts/full this far above the feet, w: temporal dither frame (0 = static)

bool RRSeeThroughActive() { return _RR_SeeShape.y > 0; }

// 0..1 threshold. Interleaved gradient noise reads finer and less grid-like than a 4x4 Bayer at low coverage.
float RRSeeDither(float2 pix)
{
    if (_RR_SeeShape2.w > 0) pix += 5.588238 * fmod(_RR_SeeShape2.w, 64.0);
    return frac(52.9829189 * frac(dot(floor(pix), float2(0.06711056, 0.00583715))));
}

// Fade (0 keep .. 1 fully in the falloff) toward one player. uv: 0..1 viewport, eye: linear eye depth of the fragment.
float RRSeeFadeOne(float2 uv, float eye, float4 p, float aspect)
{
    if (p.w < 0.5 || eye > p.z - _RR_SeeShape.w) return 0;
    float2 d = uv - p.xy; d.x *= aspect;
    return 1 - smoothstep(_RR_SeeShape.x, _RR_SeeShape.y, length(d));
}

float RRSeeHeight(float wsY) { return smoothstep(_RR_SeeShape2.y, _RR_SeeShape2.z, wsY - _RR_SeeShape2.x); }

// 0 = keep, 1 = discard everything (never reached: min visibility keeps a part)
float RRSeeThroughCut(float4 positionCS, float3 ws)
{
    if (!RRSeeThroughActive()) return 0;
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float eye = LinearEyeDepth(ws, GetWorldToViewMatrix());
    float aspect = _ScreenParams.x / _ScreenParams.y;
    float f = 0;
    [unroll] for (int k = 0; k < 3; k++) f = max(f, RRSeeFadeOne(uv, eye, _PlayerScreenPos[k], aspect));
    return f * RRSeeHeight(ws.y) * (1 - _RR_SeeShape.z);
}

// Run it in the colour AND the depth passes (DepthOnly, DepthNormals), never the shadow caster.
void RRSeeThroughClip(float4 positionCS, float3 ws)
{
    float c = RRSeeThroughCut(positionCS, ws);
    if (c > 0.001) clip(RRSeeDither(positionCS.xy) - c);
}
#endif
