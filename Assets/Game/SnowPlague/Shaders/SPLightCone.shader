// Lantern light shaft (SP_LightCone mesh, UV.y = 0 at the lantern): additive, brightest along the axis
// (view-facing part of the cone), fades to the floor and where it meets the ground (depth fade), slow scrolling
// dust noise, shared lantern flicker. No real volumetrics.
Shader "SnowPlague/LightCone"
{
    Properties
    {
        _NoiseTex("Noise (tiling)", 2D) = "grey" {}
        [HDR] _Col("Colour", Color) = (1.6, 0.75, 0.3, 1)
        _Intensity("Intensity", Range(0, 2)) = 0.35
        _Core("Axis focus", Range(0.5, 6)) = 2
        _SoftDepth("Soft intersection (m)", Float) = 0.6
        _NoiseScale("Noise scale", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "LightCone"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST, _Col;
                float _Intensity, _Core, _SoftDepth, _NoiseScale;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; float4 sp : TEXCOORD3; float3 os : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(a.normalOS); o.uv = a.uv; o.sp = ComputeScreenPos(o.positionCS); o.os = a.positionOS.xyz;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                float facing = pow(abs(dot(normalize(i.n), v)), _Core);
                float along = i.uv.y;   // 0 at the lantern .. 1 at the floor
                float fall = smoothstep(0.0, 0.15, along) * (1 - smoothstep(0.55, 1.0, along));
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * 2, along * 0.6 - _RF_Time * 0.05) * _NoiseScale / 3).r;
                float2 suv = i.sp.xy / i.sp.w;
                float soft = saturate((LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams) - LinearEyeDepth(i.ws, GetWorldToViewMatrix())) / _SoftDepth);
                float a = facing * fall * soft * (0.55 + 0.9 * n) * _Intensity * (0.9 + 0.2 * _SP_Flicker);
                return half4(_Col.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
