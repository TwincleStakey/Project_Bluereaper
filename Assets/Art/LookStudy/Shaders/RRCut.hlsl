// Greatsword cut map (RoomRun VegetationCut). Include after URP Core.hlsl. RRCutApply shortens a plant toward its root
// where the swing cut it (vertex path only: no instance is removed or moved on the CPU).
#ifndef RR_CUT_INCLUDED
#define RR_CUT_INCLUDED
float _RRCutOn;
float4 _RRCutRect;
TEXTURE2D(_RRCut); SAMPLER(sampler_RRCut);

float RRCut(float2 xz)
{
    if (_RRCutOn < 0.5) return 0;
    float2 uv = (xz - _RRCutRect.xy) / _RRCutRect.z;
    if (any(uv < 0.0) || any(uv > 1.0)) return 0;
    return saturate(SAMPLE_TEXTURE2D_LOD(_RRCut, sampler_RRCut, uv, 0).r);
}

// keep: fraction of the height left when fully cut
float3 RRCutApply(float3 ws, float3 root, float keep)
{
    float c = RRCut(root.xz);
    if (c <= 0.001) return ws;
    float3 rel = ws - root;
    rel.y *= lerp(1.0, keep, c);
    rel.xz *= lerp(1.0, 0.75, c);
    return root + rel;
}
#endif
