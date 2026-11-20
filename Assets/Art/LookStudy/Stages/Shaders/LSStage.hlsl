#ifndef LS_STAGE_INCLUDED
#define LS_STAGE_INCLUDED
// Shared stage lighting: low cold key, dark ambient, real point lights (fire, dokkaebi fire), height fog.
#include "../../Shaders/LSCommon.hlsl"
#include "Assets/Game/RoomRun/Shaders/RRGroundShadow.hlsl"   // 11-19 ShadowPass v4: _RRGroundShadow

float _LSWaterY;
float _LSHeightFog;
float3 _LSHeightFogCol;

float3 StageLight(float3 albedo, float3 n, float3 ws, float2 screenUV, float gloss, float ao)
{
    float3 v = normalize(_WorldSpaceCameraPos - ws);
    float3 l = _MainLightPosition.xyz;
    float mainSh = LSShadow(ws);
    float shadow = lerp(0.22, 1.0, mainSh);
    // 11-19 ShadowPass v4 (_RRGroundShadow, 0 = the old look exactly): the unshadowed point lights near the player (the
    // light-radius lamp 5 m above, lanterns, fire spots) filled the sun shadow, so actors cast none on this floor in
    // 대전 앞. Inside the sun shadow the local lights are dimmed as well, as the actor would also block a lamp overhead.
    float localSh = lerp(1.0, mainSh, 0.85 * saturate(_RRGroundShadow));
    float ndl = saturate(dot(n, l));

    // Hemisphere ambient: sky above, near black below.
    float3 ambient = _LSShadowMul * lerp(0.35, 1.0, n.y * 0.5 + 0.5) * ao;
    float3 col = albedo * ambient;
    col += albedo * _LSSunCol * ndl * shadow;

    // Backlight rim catches edges of rocks and trunks.
    float rim = pow(1.0 - saturate(dot(n, v)), 3.0) * saturate(dot(-v, l) * 0.5 + 0.5);
    col += _LSSunCol * rim * 0.3 * shadow * (0.3 + gloss);

    float3 h = normalize(l + v);
    float specPow = lerp(16.0, 220.0, gloss);
    col += _LSSunCol * pow(saturate(dot(n, h)), specPow) * gloss * shadow * ndl * 1.6;

    InputData inputData = (InputData)0;
    inputData.positionWS = ws;
    inputData.normalizedScreenSpaceUV = screenUV;
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light add = GetAdditionalLight(lightIndex, ws, half4(1, 1, 1, 1));
        float3 lc = add.color * add.distanceAttenuation * localSh;
        col += albedo * lc * saturate(dot(n, add.direction) * 0.8 + 0.2);
        float3 ah = normalize(add.direction + v);
        col += lc * pow(saturate(dot(n, ah)), specPow) * gloss * 1.2;
    LIGHT_LOOP_END
    return col;
}

float3 StageFog(float3 col, float3 ws)
{
    col = LSFog(col, ws);
    float band = saturate(exp(-max(ws.y - _LSWaterY, 0.0) * 0.65)) * _LSHeightFog;
    band *= 0.65 + LSFbm(ws.xz * 0.06 + _LSTime * 0.015) * 0.7;
    return lerp(col, _LSHeightFogCol, saturate(band));
}
#endif
