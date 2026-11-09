// Soft god-ray beams falling through gaps in the bamboo canopy. Each beam is a quad (x -0.5..0.5, y 0..1) that the
// vertex shader stretches from the ground point up toward the sun and turns to face the camera around its own axis.
// Additive, no depth write, slow breathing with the wind; fades near the ground and toward the camera.
Shader "BambooArena/LightShaft"
{
    Properties
    {
        _Color("Colour", Color) = (1.0, 0.86, 0.6, 1)
        _Intensity("Intensity", Range(0, 1)) = 0.12
        _Length("Length (m)", Float) = 14
        _Width("Width (m)", Float) = 1.6
        _SunDir("Direction to the sun", Vector) = (0.4, 0.8, 0.4, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Assets/Game/ReedField/Shaders/ReedCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _SunDir; float _Intensity, _Length, _Width;
            CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float seed : TEXCOORD2; };

            V2F vert(Attr a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                float3 root = TransformObjectToWorld(float3(0, 0, 0));
                float scale = length(TransformObjectToWorld(float3(1, 0, 0)) - root);
                float3 axis = normalize(_SunDir.xyz);
                float3 toCam = normalize(_WorldSpaceCameraPos - root);
                float3 side = normalize(cross(axis, toCam));
                float3 ws = root + axis * a.positionOS.y * _Length * scale + side * a.positionOS.x * _Width * scale * lerp(0.8, 1.4, a.positionOS.y);
                V2F o; o.ws = ws; o.positionCS = TransformWorldToHClip(ws); o.uv = float2(a.positionOS.x + 0.5, a.positionOS.y);
                o.seed = RFHash(root.xz);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float across = 1 - abs(i.uv.x * 2 - 1);
                across = smoothstep(0, 1, across);
                float along = smoothstep(0.0, 0.12, i.uv.y) * (1 - smoothstep(0.55, 1.0, i.uv.y));
                // streaks drifting slowly through the beam, breathing with the wind gusts
                float streak = RFNoise(float2(i.uv.x * 6 + i.seed * 10, i.uv.y * 1.5 - _RF_Time * 0.05)) * 0.6 + 0.4;
                float breathe = 0.65 + 0.35 * sin(_RF_Time * 0.4 + i.seed * 6.28);
                float camFade = saturate((length(i.ws - _WorldSpaceCameraPos) - 6) / 8);
                float a = across * along * streak * breathe * camFade * _Intensity;
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
