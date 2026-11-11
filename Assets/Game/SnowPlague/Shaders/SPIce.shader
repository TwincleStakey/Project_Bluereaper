// Frozen river (SPIceSurface.hlsl), r5: the ice8 look (dark glossy scanned ice) on the r3 shoreline (no banks; the band
// is cut at the shoreline so the ground snow runs onto the ice). Opaque + alpha clip, SPLit skeleton (URP PBR, Forward+
// lights, shadows, height-fog pass afterwards). Reflections are added as emission in the forward pass. Casts no shadows.
Shader "SnowPlague/Ice"
{
    Properties
    {
        [Header(Ice scan)]
        _IceA("Ice albedo", 2D) = "grey" {}
        _IceN("Ice normal (linear)", 2D) = "grey" {}
        [Toggle] _IceNFlipY("Normal is DirectX (flip green)", Float) = 0
        _IceR("Ice roughness", 2D) = "black" {}
        _RoughScale("Roughness map weight", Range(0, 2)) = 1
        _IceTile("Ice tile (m)", Float) = 6
        _IceTint("Scan colour scale", Color) = (0.32, 0.38, 0.42, 1)
        _DeepCol("Deep colour", Color) = (0.012, 0.02, 0.03, 1)
        _CrackCol("Crack / frost colour", Color) = (0.5, 0.56, 0.62, 1)
        _Cracks("Cracks (pale part of the scan)", Range(0, 1)) = 0.85
        _CrackDepth("Fake depth of the inner layer (m)", Range(0, 0.5)) = 0.15
        _Frost("Frost haze along the flow", Range(0, 1)) = 0.5
        _IceNormal("Ice normal strength", Range(0, 1)) = 0.25
        _Smooth("Smoothness", Range(0, 1)) = 0.92
        [Header(Shore)]
        _SnowA("Snow albedo (same as the ground)", 2D) = "white" {}
        _SnowN("Snow normal (GL, linear)", 2D) = "grey" {}
        _SnowTint("Snow tint (same as the ground)", Color) = (0.88, 0.89, 0.9, 1)
        _SnowTile("Snow tile (m)", Float) = 2.4
        _SnowSmooth("Snow smoothness", Range(0, 1)) = 0.2
        _Dust("Snow dust near the shore", Range(0, 1)) = 1
        _Rim("Frost rim at the shore", Range(0, 1)) = 0.9
        _ShoreNoise("Shoreline wobble (m)", Range(0, 2)) = 0.6
        _Sparkle("Sparkle", Range(0, 20)) = 5
        [Header(Reflections)]
        _SkyCol("Reflected sky", Color) = (0.07, 0.09, 0.13, 1)
        _ReflStrength("Reflection strength (x fresnel)", Range(0, 10)) = 3
        _SSRStrength("SSR colour scale", Range(0, 2)) = 0.7
        _GlowStrength("Bullet glow reflection", Range(0, 4)) = 1
        _GlowStretch("Streak length toward the camera", Range(1, 6)) = 2.2
        _GlowRadius("Glow reach (m)", Float) = 5
        [HDR] _LanternCol("Lantern flame (reflected)", Color) = (2.4, 1.1, 0.35, 1)
        _LanternStreak("Lantern streak", Range(0, 4)) = 0.6
        _MoonStreak("Moon streak", Range(0, 4)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #include_with_pragmas "Assets/Game/SnowPlague/Shaders/SPPassPragmas.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Assets/Game/SnowPlague/Shaders/SPForwardPragmas.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #define _ALPHATEST_ON
            #define SP_PASS_FORWARD
            // ice8: no probe reflections and no URP direct specular on the mirror ice; the shader reflects the sky
            // colour, moon, lanterns and bullets itself (soft short streaks)
            #define _ENVIRONMENTREFLECTIONS_OFF
            #define _SPECULARHIGHLIGHTS_OFF
            #include "Assets/Game/SnowPlague/Shaders/SPLit.hlsl"
            #include "Assets/Game/SnowPlague/Shaders/SPIceSurface.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #include_with_pragmas "Assets/Game/SnowPlague/Shaders/SPPassPragmas.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #define _ALPHATEST_ON
            #define SP_PASS_DEPTH
            #include "Assets/Game/SnowPlague/Shaders/SPLit.hlsl"
            #include "Assets/Game/SnowPlague/Shaders/SPIceSurface.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #include_with_pragmas "Assets/Game/SnowPlague/Shaders/SPPassPragmas.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #define _ALPHATEST_ON
            #define SP_PASS_DEPTHNORMALS
            #include "Assets/Game/SnowPlague/Shaders/SPLit.hlsl"
            #include "Assets/Game/SnowPlague/Shaders/SPIceSurface.hlsl"
            ENDHLSL
        }
    }
}
