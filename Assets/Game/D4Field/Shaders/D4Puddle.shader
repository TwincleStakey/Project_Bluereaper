// Shallow puddle water for the D4Field test room. One plane just under the floor; the builder bakes the water depth
// (plane height minus terrain height) into _Mask, so the water fades out over the shallow rim and the wet ground shows
// through there. What the surface shows is the overcast sky cubemap, stronger at grazing angles, plus the key light glint.
Shader "D4Field/Puddle"
{
    Properties
    {
        _Mask("Depth mask (R)", 2D) = "black" {}
        _Sky("Sky cubemap", CUBE) = "" {}
        _SkyTint("Sky tint", Color) = (1, 1, 1, 1)
        _SkyInt("Sky intensity", Range(0, 4)) = 1
        _F0("Reflection facing the camera", Range(0, 1)) = 0.35
        _Deep("Deep water colour", Color) = (0.03, 0.03, 0.03, 1)
        _Blur("Sky blur (mip)", Range(0, 6)) = 1.5
        _Glint("Key light glint", Range(0, 8)) = 2
        _Origin("World origin xz, size xz", Vector) = (0, 0, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
            TEXTURECUBE(_Sky); SAMPLER(sampler_Sky);
            CBUFFER_START(UnityPerMaterial)
            float4 _SkyTint, _Deep, _Origin;
            float _SkyInt, _F0, _Blur, _Glint;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fogFactor : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = (i.positionWS.xz - _Origin.xy) / _Origin.zw;
                half depth = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv).r;
                half3 N = half3(0, 1, 0);
                half3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 R = reflect(-V, N);
                half3 sky = SAMPLE_TEXTURECUBE_LOD(_Sky, sampler_Sky, R, _Blur).rgb * _SkyTint.rgb * _SkyInt;
                half fres = lerp(_F0, 1.0, pow(1.0 - saturate(dot(N, V)), 4.0));
                Light l = GetMainLight();
                half3 H = normalize(l.direction + V);
                half ndh = saturate(dot(N, H));
                half glint = pow(ndh, 400.0) * _Glint + pow(ndh, 14.0) * 0.4;   // tight sun glint + the broad bright patch of sky around it
                sky = lerp(dot(sky, half3(0.3, 0.59, 0.11)).xxx, sky, 0.25);
                // the open middle is dark still water; the shallow rim is a wet film that catches the sky
                half rim = smoothstep(0.04, 0.3, depth) * (1.0 - smoothstep(0.3, 0.85, depth));
                fres = saturate(fres * lerp(1.0, 0.6, smoothstep(0.5, 1.0, depth)) + rim * 0.45);
                half3 col = lerp(_Deep.rgb, sky, fres) + l.color * glint;
                half a = smoothstep(0.0, 1.0, depth);
                a *= lerp(0.55, 0.96, depth);
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
