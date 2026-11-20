// Moonlight shaft through broken clouds (11-13, Kido: dark fantasy where light carries the scene, "달빛 줄기").
// One quad per shaft, billboarded around the shaft axis in the vertex shader: object origin = where the shaft meets the
// ground, object scale x = width, y = length; _RF_ShaftDir = unit vector toward the moon. Additive, soft across,
// dust streaks drifting down the beam, brightest near the ground, gone near the camera. RFMoonShafts drives it.
Shader "ReedField/LightShaft"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (0.55, 0.62, 0.78, 1)
        _Intensity("Intensity", Range(0, 2)) = 0.35
        _Dust("Dust streaks", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+5" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Intensity, _Dust; CBUFFER_END
            float _ShaftFade;         // per shaft (property block): breathing 0..1
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float len : TEXCOORD2; float4 sp : TEXCOORD3; };
            V2F vert(Attr a)
            {
                V2F o;
                float3 p = TransformObjectToWorld(float3(0, 0, 0));
                float w = length(UNITY_MATRIX_M._m00_m10_m20), len = length(UNITY_MATRIX_M._m01_m11_m21);
                float3 axis = normalize(_RF_ShaftDir.xyz);
                float3 mid = p + axis * len * 0.5;
                float3 side = cross(axis, normalize(_WorldSpaceCameraPos - mid));
                side = length(side) > 1e-3 ? normalize(side) : float3(1, 0, 0);
                // quad corners: x -0.5..0.5 across, y 0..1 along (Unity quad is -0.5..0.5 in both)
                float along = a.positionOS.y + 0.5;
                o.ws = p + axis * along * len + side * a.positionOS.x * w;
                o.positionCS = TransformWorldToHClip(o.ws);
                o.sp = ComputeScreenPos(o.positionCS);
                o.uv = float2(a.positionOS.x + 0.5, along);
                o.len = len;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float across = abs(i.uv.x * 2 - 1);
                float soft = pow(saturate(1 - across), 1.8);
                // fades in from the sky end, strongest over the lower third.
                // 11-18 (Kido: the bottom of the beam is cut square): the foot used to fade over only 6% (~2 m) along a
                // straight line, and the flat quad cut through the reeds hard. Now the foot is a long rounded fade (the
                // centre reaches lower than the sides, so no straight bottom edge) plus a soft fade where it meets geometry.
                float footM = i.uv.y * i.len - across * across * 3.0;   // metres above the ground, curved across
                float foot = smoothstep(0.0, 7.0, footM);
                float along = (1 - smoothstep(0.55, 1.0, i.uv.y)) * foot * foot * lerp(1.0, 0.55, i.uv.y);
                float sceneZ = LinearEyeDepth(SampleSceneDepth(i.sp.xy / i.sp.w), _ZBufferParams);
                float meZ = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                along *= saturate((sceneZ - meZ) / 2.5);
                // dust and mist moving down the beam
                float t = _RF_Time;
                float2 q = float2(i.uv.x * 4.0, i.uv.y * i.len * 0.35 + t * 0.12);
                float dust = RFNoise(q) * 0.6 + RFNoise(q * 2.3 + 7.1) * 0.4;
                float streak = lerp(1.0, 0.45 + dust, _Dust);
                // no beam across the camera's face when it gets close
                float camFade = saturate((length(i.ws - _WorldSpaceCameraPos) - 6.0) / 10.0);
                float a = soft * along * streak * camFade * _Intensity * _ShaftFade;
                return half4(_Color.rgb * a, 0);
            }
            ENDHLSL
        }
    }
}
