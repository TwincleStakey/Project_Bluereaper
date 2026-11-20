// 11-19 ShadowPass v4: low fog sheets that lie on the ground (snow FogSheet, reed FogCard) are unlit alpha layers, so they
// painted the same fog colour over lit and shadowed ground alike and washed the actors' shadows out. With the global
// _RRGroundShadow at 1 (only while ShadowPass is on; 0 = the old look exactly) the fog thins where the main light is
// shadowed at the sheet, so the darker ground under it shows through. Include after Core.hlsl, and add to the pass:
//   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
//   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#ifndef RR_GROUND_SHADOW_INCLUDED
#define RR_GROUND_SHADOW_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

float _RRGroundShadow;

// multiplier for a ground fog layer's alpha at ws: 1 in light, down to 0.25 in full shadow
float RRFogShadowKeep(float3 ws)
{
    if (_RRGroundShadow <= 0.0) return 1.0;
    float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(ws));
    return lerp(1.0, lerp(0.25, 1.0, sh), saturate(_RRGroundShadow));
}
#endif
