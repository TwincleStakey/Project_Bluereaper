// Forward pass keywords for SnowPlague lit shaders (same set as URP 17 Lit minus debug/DBuffer/SSGI). Include with #include_with_pragmas.
// _ADDITIONAL_LIGHTS_VERTEX is intentionally absent: SPLit does not compute vertex lights, and Forward+ is per pixel anyway.
// multi_compile_instancing comes from SPPassPragmas.hlsl (not repeated here).
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS
#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
#pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
#pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
#pragma multi_compile_fragment _ _LIGHT_COOKIES
#pragma multi_compile _ _LIGHT_LAYERS
#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
#pragma multi_compile _ SHADOWS_SHADOWMASK
#pragma multi_compile _ DIRLIGHTMAP_COMBINED
#pragma multi_compile _ LIGHTMAP_ON
#pragma multi_compile _ DYNAMICLIGHTMAP_ON
#pragma multi_compile _ USE_LEGACY_LIGHTMAPS
#pragma multi_compile _ LOD_FADE_CROSSFADE
#pragma instancing_options renderinglayer
