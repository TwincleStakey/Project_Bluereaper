// Stand-in for URP Lit on tall plants that have no see-through of their own (11-20, Kido: a bare branchy tree in the
// field covered the player and did not fade). RoomRun.SeeThroughFX swaps a clone of this in at runtime for URP Lit
// materials on tall plant renderers (names reading as tree/branch/...), copying base map/colour, normal map, cutoff and
// smoothness, so they take the same see-through as bamboo and LS foliage: clear in the player's light circle, semi-
// transparent around it (RRSeeThrough.hlsl, RR_SEE_WIDE). Shadows are never clipped.
Shader "RoomRun/SeeLit"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
        _Metallic("Metallic", Range(0, 1)) = 0
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        _AlphaClip("Alpha clip on", Float) = 0
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define RR_SEE_WIDE
        #include "Assets/Game/RoomRun/Shaders/RRSeeThrough.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _BaseColor;
        float _BumpScale, _Smoothness, _Metallic, _Cutoff, _AlphaClip, _Cull;
        CBUFFER_END
        void AlphaTest(float2 uv)
        {
            if (_AlphaClip > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a * _BaseColor.a - _Cutoff);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 n : TEXCOORD1; float4 t : TEXCOORD2; float2 uv : TEXCOORD3; float fog : TEXCOORD4; };

            V2F vert(Attr a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.n = TransformObjectToWorldNormal(a.normalOS);
                o.t = float4(TransformObjectToWorldDir(a.tangentOS.xyz), a.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                AlphaTest(i.uv);
                RRSeeThroughClip(i.positionCS, i.positionWS);
                float4 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _BumpScale);
                float3 N = normalize(i.n) * (front ? 1 : -1), T = normalize(i.t.xyz), B = cross(N, T) * i.t.w;
                float3 n = normalize(T * nts.x + B * nts.y + N * nts.z);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog; d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = 1;
                SurfaceData sd = (SurfaceData)0;
                sd.albedo = alb.rgb; sd.smoothness = _Smoothness; sd.metallic = _Metallic; sd.occlusion = 1; sd.alpha = 1; sd.normalTS = float3(0, 0, 1);
                half4 col = UniversalFragmentPBR(d, sd);
                col.rgb = MixFog(col.rgb, i.fog);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o;
                float3 ws = TransformObjectToWorld(a.p.xyz), n = TransformObjectToWorldNormal(a.n);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 ld = normalize(_LightPosition - ws);
            #else
                float3 ld = _LightDirection;
            #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, ld));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.cs = cs; o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                return o;
            }
            half frag(V i) : SV_Target { AlphaTest(i.uv); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float2 uv : TEXCOORD1; };
            V vert(A a) { UNITY_SETUP_INSTANCE_ID(a); V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.uv = TRANSFORM_TEX(a.uv, _BaseMap); return o; }
            half frag(V i) : SV_Target { AlphaTest(i.uv); RRSeeThroughClip(i.cs, i.ws); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            V vert(A a) { UNITY_SETUP_INSTANCE_ID(a); V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.n = TransformObjectToWorldNormal(a.n); o.uv = TRANSFORM_TEX(a.uv, _BaseMap); return o; }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target { AlphaTest(i.uv); RRSeeThroughClip(i.cs, i.ws); return half4(normalize(i.n) * (front ? 1 : -1), 0); }
            ENDHLSL
        }
    }
}
