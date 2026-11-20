// Fog sheet: two scrolling noise layers, soft quad edges, depth fade against the scene and fade near the camera.
Shader "Underworld/FogCard"
{
    Properties
    {
        _NoiseTex("Noise", 2D) = "grey" {}
        _Col("Colour", Color) = (0.29, 0.23, 0.37, 0.55)
        _Scale("Noise scale", Float) = 0.12
        _Speed("Scroll speed", Float) = 0.02
        _DepthFade("Depth fade (m)", Float) = 2.0
        _EdgeSoft("Edge softness", Range(0.01,0.5)) = 0.3
        _Contrast("Noise contrast", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Col; float _Scale, _Speed, _DepthFade, _EdgeSoft, _Contrast;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float4 sp : TEXCOORD2; };
            V vert(A a)
            {
                V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.uv = a.uv;
                o.sp = ComputeScreenPos(o.cs);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = (i.ws.xz + i.ws.y * float2(0.6, 0.3)) * _Scale;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p + float2(_UWTime * _Speed, _UWTime * _Speed * 0.4)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p * 1.9 - float2(_UWTime * _Speed * 0.7, -_UWTime * _Speed * 0.5)).r;
                float n = saturate(pow(saturate(n1 * 0.65 + n2 * 0.55), _Contrast) * 1.6);
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = smoothstep(0.0, _EdgeSoft, e.x) * smoothstep(0.0, _EdgeSoft, e.y);
                float2 suv = i.sp.xy / i.sp.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragZ = LinearEyeDepth(i.cs.z, _ZBufferParams);
                float df = saturate((sceneZ - fragZ) / _DepthFade);
                float camFade = saturate((fragZ - 2.0) / 6.0);
                float a = _Col.a * n * edge * df * camFade;
                return half4(_Col.rgb * (0.7 + 0.5 * n), a);
            }
            ENDHLSL
        }
    }
}
