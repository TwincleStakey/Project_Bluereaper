// Purple arena boundary: an open ring/cylinder 3-5 m tall (UV.x around, UV.y = 0 at the ground .. 1 at the top).
// Additive HDR purple (x4-8 so bloom picks it up): fresnel edges, two noise layers scrolling up at different speeds,
// a bright line where it cuts into the ground and monsters (depth intersection), strongest at the bottom, fading out
// at the top.
Shader "SnowPlague/BoundaryWall"
{
    Properties
    {
        _NoiseTex("Noise (tiling)", 2D) = "grey" {}
        [HDR] _Col("Colour", Color) = (2.2, 0.6, 4.5, 1)
        [HDR] _EdgeCol("Intersection colour", Color) = (4, 1.6, 6, 1)
        _Intensity("Intensity", Range(0, 3)) = 1
        _Fresnel("Fresnel power", Range(0.5, 6)) = 2
        _Scroll("Scroll speeds (layer 1, layer 2)", Vector) = (0.25, 0.6, 0, 0)
        _Tiling("Noise tiling (x around, y up) layer 1 / 2", Vector) = (8, 1.5, 14, 3)
        _Intersect("Intersection width (m)", Float) = 0.4
        _FadeTop("Fade-out height (0-1 of the ring)", Range(0.05, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Boundary"
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
                float4 _NoiseTex_ST, _Col, _EdgeCol, _Scroll, _Tiling;
                float _Intensity, _Fresnel, _Intersect, _FadeTop;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; float4 sp : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A a)
            {
                V o; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
                o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(a.normalOS); o.uv = a.uv; o.sp = ComputeScreenPos(o.positionCS); return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _RF_Time;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * _Tiling.x, i.uv.y * _Tiling.y - t * _Scroll.x)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * _Tiling.z + 0.37, i.uv.y * _Tiling.w - t * _Scroll.y)).r;
                float noise = saturate(n1 * n2 * 2.5);
                float3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                float fres = pow(1 - abs(dot(normalize(i.n), v)), _Fresnel);
                float bottom = pow(1 - saturate(i.uv.y), 1.6);
                float top = 1 - smoothstep(_FadeTop * 0.6, _FadeTop, i.uv.y);   // look1: _FadeTop < 1 = a low line, not a wall
                float2 suv = i.sp.xy / i.sp.w;
                float gap = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams) - LinearEyeDepth(i.ws, GetWorldToViewMatrix());
                float hit = 1 - saturate(gap / _Intersect);
                float a = (noise * (0.35 + fres) * (0.3 + bottom) * top) * _Intensity;
                float3 c = _Col.rgb * a + _EdgeCol.rgb * hit * hit * _Intensity;
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
