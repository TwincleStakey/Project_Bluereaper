// Shallow wetland water. Uses the free URP_Water assets (ChiliMilk, MIT): its wind-scrolled surface normal map
// (WaterSurface_single), foam texture (WaterFoam) and its WaterFX ripple buffer (_WaterFXTexture, written by ripple
// particles), with BoatAttack-style screen-space ripples. Shallow look:
//   - alpha from real water depth (scene depth minus surface depth): clear at the shore, a little murkier deeper,
//     so the muddy bed shows through and the shoreline follows the ground, never the mesh edge;
//   - a soft broken foam/wet line where the depth goes to zero;
//   - reflections of the sky (_PlanarReflectionTexture stand-in) by fresnel, plus sun glints on the ripples.
Shader "ReedField/ShallowWater"
{
    Properties
    {
        _SurfaceMap("Surface normal (URP_Water WaterSurface_single)", 2D) = "bump" {}
        _FoamTex("Foam (URP_Water WaterFoam)", 2D) = "black" {}
        _ShallowCol("Shallow tint", Color) = (0.42, 0.30, 0.17, 1)
        _DeepCol("Deep tint", Color) = (0.12, 0.08, 0.05, 1)
        _ClearDepth("Fully clear depth (m)", Float) = 0.08
        _MurkDepth("Murky at depth (m)", Float) = 0.9
        _MaxAlpha("Max opacity", Range(0, 1)) = 0.78
        _FoamCol("Foam colour", Color) = (1, 0.9, 0.72, 1)
        _FoamWidth("Foam width (m)", Float) = 0.18
        _SurfaceScale("Surface tiling (m)", Float) = 3.5
        _NormalStrength("Normal strength", Range(0, 2)) = 0.6
        _FlowSpeed("Flow speed", Float) = 0.05
        _Reflection("Reflection", Range(0, 1)) = 0.55
        _Glint("Sun glint", Range(0, 4)) = 1.6
        _RippleStrength("Ripple strength", Range(0, 3)) = 1.4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_FoamTex); SAMPLER(sampler_FoamTex);
            TEXTURE2D(_WaterFXTexture); SAMPLER(sampler_WaterFXTexture);
            TEXTURE2D(_PlanarReflectionTexture); SAMPLER(sampler_PlanarReflectionTexture);
            CBUFFER_START(UnityPerMaterial)
                float4 _SurfaceMap_ST, _FoamTex_ST;
                float4 _ShallowCol, _DeepCol, _FoamCol;
                float _ClearDepth, _MurkDepth, _MaxAlpha, _FoamWidth, _SurfaceScale, _NormalStrength, _FlowSpeed, _Reflection, _Glint, _RippleStrength;
            CBUFFER_END

            struct Attr { float4 positionOS : POSITION; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float4 screen : TEXCOORD1; };

            V2F vert(Attr a)
            {
                V2F o;
                o.ws = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            float3 SurfaceNormal(float2 xz)
            {
                float2 wind = _RF_Wind.xy;
                float t = _RF_Time * _FlowSpeed;
                float2 uv1 = xz / _SurfaceScale + wind * t;
                float2 uv2 = float2(xz.y, -xz.x) / (_SurfaceScale * 1.7) + float2(-wind.y, wind.x) * t * 0.7;
                float3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uv1));
                float3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, uv2));
                float2 nxy = (n1.xy + n2.xy) * 0.5 * _NormalStrength;
                return normalize(float3(nxy.x, 1, nxy.y));
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 suv = i.screen.xy / i.screen.w;
                // Real water depth along the view ray -> vertical depth approximation.
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float surfEye = i.screen.w;
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float depth = max(sceneEye - surfEye, 0) * saturate(v.y + 0.05);

                float3 n = SurfaceNormal(i.ws.xz);
                // Ripples from the WaterFX buffer (world normals + strength), as in URP_Water / BoatAttack.
                float4 fx = SAMPLE_TEXTURE2D(_WaterFXTexture, sampler_WaterFXTexture, suv);
                n = normalize(lerp(n, normalize(n + float3(fx.x, 0, fx.z) * 2.0), saturate(fx.a * _RippleStrength)));

                Light ml = GetMainLight();
                float fres = pow(1 - saturate(dot(n, v)), 4);
                float murk = smoothstep(_ClearDepth, _MurkDepth, depth);
                float3 col = lerp(_ShallowCol.rgb, _DeepCol.rgb, murk) * (0.35 + 0.65 * ml.color);
                // Mirror of the sky, distorted by the ripples.
                float2 ruv = float2(suv.x + n.x * 0.05, 1 - saturate(0.5 + n.z * 0.3 + fres * 0.5));
                float3 sky = SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_PlanarReflectionTexture, ruv).rgb;
                float refl = saturate(_Reflection * (0.25 + fres * 1.5));
                col = lerp(col, sky, refl);
                float3 r = reflect(-v, n);
                col += ml.color * pow(saturate(dot(r, ml.direction)), 180) * _Glint;
                col += ml.color * pow(saturate(dot(r, ml.direction)), 24) * 0.08 * _Glint;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = suv;
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    col += add.color * add.distanceAttenuation * pow(saturate(dot(r, add.direction)), 60) * 1.2;
                LIGHT_LOOP_END

                float foam = 0;
                float alpha = lerp(0.05, _MaxAlpha * 0.8, murk) + refl * 0.3;
                // fades to nothing at the waterline, over a band broken by noise so the edge is never a clean contour
                float edgeN = RFNoise(i.ws.xz * 0.9 + 3.1) * 0.10 + RFNoise(i.ws.xz * 3.1) * 0.04;
                alpha *= smoothstep(0.0 + edgeN, 0.32 + edgeN, depth);
                // the wet line: a thin, broken, softly glowing film right where the water meets the bank
                float wetLine = smoothstep(0.0, 0.025, depth) * (1 - smoothstep(0.03, _FoamWidth, depth + edgeN * 0.4));
                float ft = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, i.ws.xz / 2.5 + _RF_Wind.xy * _RF_Time * 0.01).r;
                foam = wetLine * saturate(ft * 1.4 + 0.2) * 0.5;
                col = lerp(col, _FoamCol.rgb * (0.3 + 0.7 * ml.color), foam);
                alpha = max(alpha, foam * 0.8);
                col = RFFog(col, i.ws + float3(0, 2, 0));   // distance haze only; the low ground fog would whiten the water
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
