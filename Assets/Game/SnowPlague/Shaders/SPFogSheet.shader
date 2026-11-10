// Low fog sheet: a flat plane at 0.3 / 0.8 / 1.5 m (2-3 sheets, each its own speed). Two tiling noise layers
// scroll in different directions; soft intersection with the scene depth, fades near the camera and at the sheet edge.
// Also used for the fog sea below the cliff (larger, denser, slower). Needs the URP depth texture.
Shader "SnowPlague/FogSheet"
{
    Properties
    {
        _NoiseTex("Noise (tiling)", 2D) = "grey" {}
        _Col("Fog colour", Color) = (0.6, 0.67, 0.72, 1)
        _Density("Density", Range(0, 2)) = 0.5
        _Scale("Noise scale (m per tile)", Vector) = (18, 7, 0, 0)
        _Speed("Scroll (xy layer 1, zw layer 2) m/s", Vector) = (0.35, 0.1, -0.2, 0.25)
        _Contrast("Noise contrast", Range(0.5, 4)) = 1.8
        _SoftDepth("Soft intersection (m)", Float) = 1.2
        _CamFade("Camera fade (near m, far m)", Vector) = (6, 14, 0, 0)
        _EdgeFade("Edge fade (UV)", Range(0.01, 0.5)) = 0.2
        _ArenaClear("Thinner over the arena", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "FogSheet"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
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
                float4 _NoiseTex_ST, _Col, _Scale, _Speed, _CamFade;
                float _Density, _Contrast, _SoftDepth, _EdgeFade, _ArenaClear;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float4 sp : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws);
                o.uv = a.uv; o.sp = ComputeScreenPos(o.positionCS); return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _RF_Time;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.ws.xz / _Scale.x + _Speed.xy * t / _Scale.x).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.ws.xz / _Scale.y + _Speed.zw * t / _Scale.y).r;
                float n = saturate(pow(saturate(n1 * n2 * 2.2), _Contrast) * 1.6);
                // soft intersection with whatever stands in the fog (ground, props, monsters)
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = LinearEyeDepth(i.ws, GetWorldToViewMatrix());
                float soft = saturate((scene - self) / _SoftDepth);
                // fade right in front of the camera and at the sheet edge
                float cam = smoothstep(_CamFade.x, _CamFade.y, self);
                float2 e = min(i.uv, 1 - i.uv);
                float edge = smoothstep(0, _EdgeFade, min(e.x, e.y));
                float arena = 1;
                if (_RF_FogArena.z > 0)
                {
                    float2 o = max(abs(i.ws.xz - _RF_FogArena.xy) - _RF_FogArena.zw, 0);
                    arena = lerp(1 - _ArenaClear, 1, saturate(length(o) / 6.0));
                }
                float a = saturate(n * _Density * soft * cam * edge * arena);
                return half4(_Col.rgb, a);
            }
            ENDHLSL
        }
    }
}
