// Full-screen height fog for a Full Screen Pass Renderer Feature (injection: After Rendering Opaques, requirement:
// Color + Depth), so bullets and VFX drawn later stay crisp. World position from depth:
//   fog = (1 - exp(-distance x density)) x exp(-(y - base) x falloff) x scrolling XZ noise
// colour fog blue-grey (#9aabb8) near, navy far. The RoomRun zoom shift (_RR_FogShift) is taken out of the distance.
Shader "SnowPlague/HeightFog"
{
    Properties
    {
        _NoiseTex("Noise (tiling)", 2D) = "grey" {}
        _NearCol("Near colour", Color) = (0.6, 0.67, 0.72, 1)
        _FarCol("Far colour", Color) = (0.12, 0.16, 0.22, 1)
        _Base("Base height (m)", Float) = 0
        _Falloff("Height falloff (1/m)", Float) = 0.35
        _Density("Distance density (1/m)", Float) = 0.02
        _Start("Start distance (m)", Float) = 18
        _FarDist("Distance to far colour (m)", Float) = 90
        _NoiseScale("Noise scale (m)", Float) = 24
        _NoiseAmount("Noise amount", Range(0, 1)) = 0.5
        _MaxFog("Max fog", Range(0, 1)) = 0.85
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
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            float4 _NearCol, _FarCol;
            float _Base, _Falloff, _Density, _Start, _FarDist, _NoiseScale, _NoiseAmount, _MaxFog;
            float _RF_Time;
            float4 _RF_Wind;
            #ifndef RR_FOGSHIFT_DECLARED
            #define RR_FOGSHIFT_DECLARED
            float _RR_FogShift;
            #endif
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 ws = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float dist = max(length(ws - cam) - _RR_FogShift - _Start, 0);
                float2 drift = _RF_Wind.xy * _RF_Time * 0.6;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, (ws.xz + drift) / _NoiseScale).r;
                float h = exp(-max(ws.y - _Base, 0) * _Falloff);
                float f = (1 - exp(-dist * _Density)) * h * lerp(1 - _NoiseAmount, 1 + _NoiseAmount, n);
                float3 fc = lerp(_NearCol.rgb, _FarCol.rgb, saturate(dist / _FarDist));
                return half4(lerp(col.rgb, fc, saturate(f) * _MaxFog), col.a);
            }
            ENDHLSL
        }
    }
}
