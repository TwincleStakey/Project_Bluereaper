// Draws nothing: used to blank one material slot of a mesh (the character model's built-in foot ring, PlayerController).
// Kept in Resources so player builds include it (Shader.Find only finds shaders that are in the build).
Shader "RoomRun/Invisible"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ColorMask 0
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 p : POSITION) : SV_POSITION { return float4(2, 2, 2, 1); }   // outside the screen
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
