// Soft shaft of light falling from above. The quad turns around its vertical axis to face the camera.
// It fades in and out on its own slow cycle (_Phase), so shafts appear here and there rather than all at once.
Shader "Underworld/LightShaft"
{
    Properties
    {
        _NoiseTex("Noise", 2D) = "grey" {}
        [HDR] _Col("Colour", Color) = (0.9, 0.8, 1.3, 1)
        _Strength("Strength", Float) = 0.35
        _Cycle("Cycle speed", Float) = 0.22
        _Duty("Visible share (0-1)", Range(0.05, 1)) = 0.45
        _Phase("Phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+15" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Col; float _Strength, _Cycle, _Duty, _Phase;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; float3 ws : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), 1);
                float3 axis = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 toCam = _WorldSpaceCameraPos - origin;
                float3 side = normalize(cross(axis, toCam));
                // quad uv: x across, y along the shaft (0 bottom, 1 top)
                float3 ws = origin + side * (a.uv.x - 0.5) * scale.x + axis * a.uv.y * scale.y;
                o.ws = ws;
                o.cs = TransformWorldToHClip(ws);
                o.uv = a.uv;
                o.sp = ComputeScreenPos(o.cs);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _UWTime;
                // slow on/off cycle per shaft
                float cyc = sin(t * _Cycle + _Phase * 6.2831853) * 0.5 + 0.5;
                float on = smoothstep(1.0 - _Duty, 1.0 - _Duty + 0.25, cyc);
                float across = 1.0 - abs(i.uv.x - 0.5) * 2.0;
                across = pow(saturate(across), 1.6);
                float along = smoothstep(0.0, 0.25, i.uv.y) * (1.0 - smoothstep(0.75, 1.0, i.uv.y));
                float dust = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * 0.6 + _Phase, i.uv.y * 0.35 + t * 0.02)).r;
                float streak = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(i.uv.x * 2.5 + _Phase * 3.0, t * 0.01)).r;
                float2 suv = i.sp.xy / i.sp.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragZ = LinearEyeDepth(i.cs.z, _ZBufferParams);
                float soft = saturate((sceneZ - fragZ) / 1.5);
                float a = across * along * (0.55 + 0.6 * dust) * (0.6 + 0.6 * streak) * on * soft * _Strength;
                return half4(_Col.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
