// Underworld full-screen height fog (UWLook pass, after opaques: bullets and VFX drawn later stay crisp).
// Pass 11-13b: no camera-distance term any more. Fog density is exponential in world height,
//   d(y) = _Density * exp(-_Falloff * (y - _Base)),
// integrated analytically along the view ray from the camera to the depth-buffer point:
//   tau = _Density * L * (e(y_point) - e(y_cam)) / (_Falloff * (y_cam - y_point)),  e(y) = exp(-_Falloff * (y - _Base)).
// The 30-45 m of air between the high camera and the floor contribute ~0, so there is no uniform veil; fog only builds up
// in the first ~1.5 m above the floor and fills the low ground / abyss. Two drifting value-noise layers break it into
// patches. The fog keeps part of the lit scene colour (_LitScatter) so a lantern pool glows through it instead of greying.
// _FogCol is given pre-exposure (UWLook divides its colour by 2^exposure). In Resources so builds keep it.
Shader "Underworld/HeightFog"
{
    Properties
    {
        _FogCol("Fog colour (pre-exposure)", Color) = (0.006, 0.007, 0.008, 1)
        _LitScatter("Lit scene colour kept in fog", Range(0, 1)) = 0.6
        _Base("Base height (m)", Float) = 0.15
        _Falloff("Height falloff (1/m)", Float) = 1.6
        _Density("Density at base (1/m)", Float) = 0.22
        _MaxFog("Max fog", Range(0, 1)) = 0.9
        _Noise("Noise scale, amount, wind x, wind z", Vector) = (0.05, 0.75, 0.05, 0.02)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "HeightFog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float4 _FogCol, _Noise;
            float _LitScatter, _Base, _Falloff, _Density, _MaxFog;

            float H(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float N(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(H(i), H(i + float2(1, 0)), u.x), lerp(H(i + float2(0, 1)), H(i + 1), u.x), u.y);
            }
            // relative density at height y, capped so the abyss / far plane cannot overflow
            float E(float y) { return exp(min(-_Falloff * (y - _Base), 6.0)); }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 ws = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float L = length(ws - cam);
                float dy = cam.y - ws.y;
                float ep = E(ws.y), ec = E(cam.y);
                float tau = abs(dy) > 1e-3 ? _Density * L * (ep - ec) / (_Falloff * dy) : _Density * L * ep;

                float t = _Time.y;
                float2 wind = _Noise.zw * t;
                float n = N(ws.xz * _Noise.x + wind) * 0.65 + N(ws.xz * _Noise.x * 2.7 - wind.yx * 1.6) * 0.35;
                tau *= lerp(1 - _Noise.y, 1 + _Noise.y, smoothstep(0.2, 0.8, n));

                float f = (1 - exp(-max(tau, 0))) * _MaxFog;
                float3 fc = _FogCol.rgb + col.rgb * _LitScatter;
                return half4(lerp(col.rgb, fc, f), col.a);
            }
            ENDHLSL
        }
    }
}
