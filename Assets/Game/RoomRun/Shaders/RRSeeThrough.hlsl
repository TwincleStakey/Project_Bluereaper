// Soft see-through for whatever stands between the camera and the player (11-13, Kido: "the hard circle cut-out should
// be soft and natural like other top-down games"). Include after URP Core.hlsl.
//
// A screen-space radial falloff around each player's screen position (aspect-corrected, radius already converted from
// metres by RoomRun.SeeThroughFX so it follows the camera zoom):
//     cut = (1 - smoothstep(inner, outer, dist))^curve *(1 - minVisibility) * heightRamp
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
float4 _RR_SeeShape3;       // x: falloff curve exponent applied after the smoothstep (0 = unset -> 1)
// 11-20 wide clear (Kido, 대나무숲: "bamboo between the camera and me covers most of the screen"; bullet heaven = the play
// area must stay readable). Anything above the knee ramp whose pixel covers the GROUND within a wide ellipse around a player
// is removed completely (no minimum visibility, no "in front of the player" test: a culm behind the player hides enemies
// too). The pixel's ground point is where the camera ray through it meets the player's feet height, so the test is in
// metres on the ground and a tree far away whose top only overlaps the player on screen is kept. Shadows stay.
// Set by RoomRun.SeeThroughFX. FOLIAGE ONLY, opt-in: #define RR_SEE_WIDE before including (bamboo, LS foliage, SP vegetation).
// 11-20 Kido: landmarks (서낭당 tower, 문인석), houses, rocks keep the soft v2 thinning; reeds too (their half-hiding
// of the player is intended).
float4 _RR_SeeWide;         // x: inner radius (m, fully gone), y: outer radius (m, untouched beyond), z: lateral stretch along camera right, w: 1 = on
float4 _RR_SeeWideRight;    // xy: camera right on the ground (normalised xz), z/w: fade starts/full this high above the feet (m)
float4 _RR_SeeWide2;        // x: how much of the occluder stays inside the ellipse (r4: semi-transparent), y/z: light-circle clear radius full/fade (m, r5), w: clear height start above feet (m, r6)
float4 _RR_SeePlayerWS[3];  // xyz: player feet (world), w: 1 = active

bool RRSeeThroughActive() { return _RR_SeeShape.y > 0; }

#if !defined(RR_SEE_WIDE)
bool RRSeeWideActive() { return false; }
float RRSeeWideCut(float3 ws) { return 0; }
#else
bool RRSeeWideActive() { return _RR_SeeWide.w > 0.5 && _RR_SeeWide.y > 0; }

// 0 keep .. 1 gone
float RRSeeWideCut(float3 ws)
{
    float3 cam = _WorldSpaceCameraPos;
    float2 rgt = _RR_SeeWideRight.xy, fwd = float2(-rgt.y, rgt.x);
    float stretch = max(_RR_SeeWide.z, 0.1);
    float keep = saturate(_RR_SeeWide2.x);
    float f = 0;
    [unroll] for (int k = 0; k < 3; k++)
    {
        float4 p = _RR_SeePlayerWS[k];
        if (p.w < 0.5) continue;
        float denom = cam.y - ws.y;
        float2 g = denom > 0.05 ? cam.xz + (ws.xz - cam.xz) * ((cam.y - p.y) / denom) : ws.xz;
        float2 d = g - p.xz;
        // 11-20 r5 (Kido: "딱 빛범위만 아무것도 안가리게 하고 나머지는 지금처럼 투명"): inside the player's light circle
        // (round, metres on the ground, _RR_SeeWide2.yz) nothing above the knees covers the lit ground at all
        float clearCut = (1 - smoothstep(_RR_SeeWide2.y, _RR_SeeWide2.z, length(d)))
                       * smoothstep(_RR_SeeWide2.w, _RR_SeeWide2.w + 0.8, ws.y - p.y);
        // r6 (Kido, 계곡: "높은 나무만, 왜 잔디까지... 캐릭터가 박혀서"): the clear starts above chest height, not at the knees,
        // so grass/flowers/shrubs never go even when the player stands sunk in water below the bank
        f = max(f, clearCut);
        // around it: only the tall upper parts (r2 "위에 나무만") standing between camera and player (r3 "주위 싹다
        // 지워버리면") go semi-transparent (r4 "위에 환경이 좀 투명하게"), keep = what stays
        float ramp = smoothstep(_RR_SeeWideRight.z, _RR_SeeWideRight.w, ws.y - p.y);
        if (ramp <= 0 || LinearEyeDepth(ws, GetWorldToViewMatrix()) > _PlayerScreenPos[k].z + 0.5) continue;
        float dist = length(float2(dot(d, rgt) / stretch, dot(d, fwd)));
        f = max(f, (1 - smoothstep(_RR_SeeWide.x, _RR_SeeWide.y, dist)) * ramp * (1 - keep));
    }
    return f;
}
#endif

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
    float t = 1 - smoothstep(_RR_SeeShape.x, _RR_SeeShape.y, length(d));
    float curve = _RR_SeeShape3.x > 1e-3 ? _RR_SeeShape3.x : 1;   // 11-13: >1 = long gentle tail, no ring at the edge
    return pow(t, curve);
}

float RRSeeHeight(float wsY) { return smoothstep(_RR_SeeShape2.y, _RR_SeeShape2.z, wsY - _RR_SeeShape2.x); }

// 0 = keep, 1 = discard everything (soft mode never reaches 1: min visibility keeps a part; wide mode does)
float RRSeeThroughCut(float4 positionCS, float3 ws)
{
    if (!RRSeeThroughActive()) return 0;
    float w = RRSeeWideActive() ? RRSeeWideCut(ws) : 0;   // 11-20: wide clear above tree height + soft v2 below it
    float2 uv = GetNormalizedScreenSpaceUV(positionCS);
    float eye = LinearEyeDepth(ws, GetWorldToViewMatrix());
    float aspect = _ScreenParams.x / _ScreenParams.y;
    float f = 0;
    [unroll] for (int k = 0; k < 3; k++) f = max(f, RRSeeFadeOne(uv, eye, _PlayerScreenPos[k], aspect));
    return max(w, f * RRSeeHeight(ws.y) * (1 - _RR_SeeShape.z));
}

// Run it in the colour AND the depth passes (DepthOnly, DepthNormals), never the shadow caster.
void RRSeeThroughClip(float4 positionCS, float3 ws)
{
    float c = RRSeeThroughCut(positionCS, ws);
    if (c > 0.001) clip(RRSeeDither(positionCS.xy) - c);
}
#endif
