// Invisible lid over the inside of the moored boat (11-14, Kido: the boat's floor looked see-through). The hull sits
// 0.18 m deep, so the river surface runs through the inside of the boat and the transparent water was drawn over the
// floorboards. This writes depth only, after the opaque boat and before the water (Transparent-100 / -99): the water
// fails the depth test inside the hull, the floor drawn earlier keeps its colour. No shadow, no depth prepass, so the
// water's own scene-depth read and everything opaque are unaffected.
Shader "ReedField/WaterMask"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-150" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WaterMask"
            Tags { "LightMode"="UniversalForward" }
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
