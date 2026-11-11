// SPIceReflect's history copy: scene colour (rgb) + linear eye depth (a) into a half-res ARGBHalf texture, after
// transparents. The frozen river marches its reflections against it next frame (SPIceSurface.hlsl).
Shader "Hidden/SnowPlague/IceCopy"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "IceCopy"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float raw = SampleSceneDepth(uv);
                float eye = unity_OrthoParams.w > 0.5 ? lerp(_ProjectionParams.y, _ProjectionParams.z, 1 - raw) : LinearEyeDepth(raw, _ZBufferParams);
                return half4(min(col, 60000), min(eye, 60000));
            }
            ENDHLSL
        }
    }
}
