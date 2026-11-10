// Rim light for enemies in dark fields (FieldLighting): an additive fresnel edge, brighter where nearby lights
// (the player's light pool, glowing bullets, lanterns) fall on them, so they read against the dark and catch the light.
// Drawn as an extra material slot on the enemy renderers; adds light only.
Shader "RoomRun/Rim"
{
    Properties
    {
        _Color("Rim colour", Color) = (0.6, 0.72, 0.95, 1)
        _Power("Rim width (lower = wider)", Range(0.5, 8)) = 2.2
        _Base("Rim without nearby light", Range(0, 2)) = 0.7
        _Catch("Rim from nearby lights", Range(0, 4)) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Rim"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float _Power, _Base, _Catch;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; };
            V vert(A a)
            {
                V o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.n = TransformObjectToWorldNormal(a.normalOS);
                o.pos = TransformWorldToHClip(o.ws); return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.n), v = normalize(_WorldSpaceCameraPos - i.ws);
                float rim = pow(1 - saturate(dot(n, v)), _Power);
                float3 caught = 0;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    // light wrapping round the silhouette from behind/side reads as reflected glow
                    caught += l.color * l.distanceAttenuation * saturate(dot(n, l.direction) * 0.5 + 0.5);
                LIGHT_LOOP_END
                float3 col = rim * (_Color.rgb * _Base + caught * _Catch);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
