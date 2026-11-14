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
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Intensity, _Dust; CBUFFER_END
            float _ShaftFade;         // per shaft (property block): breathing 0..1
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float len : TEXCOORD2; };
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
                o.uv = float2(a.positionOS.x + 0.5, along);
                o.len = len;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float across = abs(i.uv.x * 2 - 1);
                float soft = pow(saturate(1 - across), 1.8);
                // fades in from the sky end, strongest over the lower third, short soft foot at the ground
                float along = (1 - smoothstep(0.55, 1.0, i.uv.y)) * smoothstep(0.0, 0.06, i.uv.y) * lerp(1.0, 0.55, i.uv.y);
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
