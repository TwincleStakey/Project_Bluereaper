#ifndef LS_FIELD_INCLUDED
#define LS_FIELD_INCLUDED

CBUFFER_START(UnityPerMaterial)
float4 _ColA;
float4 _ColB;
float4 _ColC;
float _RootDark;
float _TipLift;
float4 _LaneCol;
float _LaneAmt;
float4 _WindDir;
float _Bend;
float4 _DirtA;
float4 _DirtB;
float4 _SandCol;
float4 _ClearCol;
float _BladeVar;
float _PatchContrast;
float _ClumpAmt;
CBUFFER_END

// Large, soft colour masses first, then per-blade variation. Shared by ground and blades.
float3 LSFieldColor(float2 xz, float h, float rnd)
{
    float big = LSFbm(xz * 0.042 + 7.3);
    float mid = LSFbm(xz * 0.17 + 3.1);
    float t = saturate((big - 0.5) * _PatchContrast + 0.5 + (mid - 0.5) * _PatchContrast * 0.3);
    float3 c = t < 0.5 ? lerp(_ColA.rgb, _ColB.rgb, t * 2.0) : lerp(_ColB.rgb, _ColC.rgb, t * 2.0 - 1.0);
    c *= lerp((1.0 - _RootDark).xxx, _TipLift * float3(1.08, 1.0, 0.68), pow(saturate(h), 1.25));
    c *= 1.0 + (LSNoise(xz * 0.42 + 21.0) - 0.5) * _ClumpAmt + (LSNoise(xz * 1.3 + 5.0) - 0.5) * _ClumpAmt * 0.7;
    c *= 1.0 - _BladeVar + rnd * 2.0 * _BladeVar;
    c *= 1.0 + smoothstep(0.86, 1.0, rnd) * _BladeVar * 1.6;
    return c;
}

// Bright bands of wind that travel across the field.
float LSWindLane(float2 xz)
{
    float2 dir = normalize(_WindDir.xy);
    float2 side = float2(-dir.y, dir.x);
    float2 p = float2(dot(xz, dir) * 0.055, dot(xz, side) * 0.17);
    p.x -= _LSTime * 0.32;
    float n = LSFbm(p);
    return smoothstep(0.52, 0.72, n);
}
#endif
