// Scanned rocks with their own texture and normal map. _Remap pulls brown scans to pale Korean granite.
// Moss grows on upward faces, and a dark glossy band marks the waterline.
Shader "LookStudy/StageRock"
{
    Properties
    {
        _MainTex("Albedo", 2D) = "white" {}
        _NormalTex("Normal", 2D) = "bump" {}
        _MossTex("Moss", 2D) = "white" {}
        _Granite("Granite tint", Color) = (0.78, 0.74, 0.68, 1)
        _Remap("Granite remap", Range(0,1)) = 0.75
        _MossTint("Moss tint", Color) = (0.34, 0.46, 0.22, 1)
        _MossAmt("Moss amount", Range(0,1)) = 0.55
        _Wet("Wet band height", Float) = 0.55
        _BurnOn("Burn from vertex alpha", Range(0,1)) = 0
        _DryGloss("Dry gloss scale (0 = matte straw, cloth, clay)", Range(0,2)) = 1
        _WetGloss("Wet band gloss (11-14 Kido: valley rocks sparkled)", Range(0,1)) = 0.34
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSStage.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
        TEXTURE2D(_MossTex); SAMPLER(sampler_MossTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _Granite;
        float _Remap;
        float4 _MossTint;
        float _MossAmt;
        float _Wet;
        float _BurnOn;
        float _DryGloss; float _WetGloss;
        float _Cull;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 tangentWS : TEXCOORD2; float2 uv : TEXCOORD3; float4 screen : TEXCOORD4; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(a.tangentOS.xyz), a.tangentOS.w);
                o.uv = a.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                o.color = a.color;
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                RRSeeThroughClip(i.positionCS, i.positionWS);   // 11-13: walls/roofs/rocks between camera and player thin out softly
                float3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                float3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv));
                float3 N = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 T = normalize(i.tangentWS.xyz);
                float3 B = cross(N, T) * i.tangentWS.w;
                float3 n = normalize(T * tn.x + B * tn.y + N * tn.z);

                float lum = dot(tex, float3(0.3, 0.59, 0.11));
                float3 albedo = lerp(tex, _Granite.rgb * saturate(lum * 2.6) , _Remap) * i.color.rgb;

                float2 mp = i.positionWS.xz * 0.45;
                float noise = LSFbm(i.positionWS.xz * 0.55 + i.positionWS.y * 0.6);
                float up = saturate(N.y * 1.5 - 0.45);
                float moss = smoothstep(1.0 - _MossAmt, 1.0 - _MossAmt + 0.2, up * 0.75 + noise * 0.55);
                float3 mossCol = SAMPLE_TEXTURE2D(_MossTex, sampler_MossTex, mp).rgb * _MossTint.rgb * 2.0;
                albedo = lerp(albedo, mossCol, moss);

                // Dark wet band above the waterline.
                float wet = 1.0 - smoothstep(_LSWaterY + _Wet * 0.3, _LSWaterY + max(_Wet, 0.01), i.positionWS.y + (noise - 0.5) * 0.25);
                albedo *= lerp(1.0, 0.36, wet);
                // 11-13 QA: 0.72 read as plastic under the wisp lights. 11-13 Kido: thatch/straw/crocks still shone, so the dry
                // gloss is per material now (_DryGloss), the wet band keeps its own.
                float gloss = lerp(0.12 * _DryGloss, _WetGloss, wet) * (1.0 - moss * 0.7);

                // Fire damage: char spreads through noise as vertex alpha rises, cracks glow at the worst parts.
                float burn = i.color.a * _BurnOn;
                float cn = LSFbm(i.positionWS.xz * 0.9 + i.positionWS.y * 1.1);
                float charMask = smoothstep(1.02 - burn * 1.15, 1.2 - burn * 1.1, cn + burn * 0.25);
                albedo = lerp(albedo, float3(0.018, 0.015, 0.014) + albedo * 0.07, charMask);
                float3 col = StageLight(albedo, n, i.positionWS, i.screen.xy / i.screen.w, gloss * (1.0 - charMask), 0.7 + saturate(lum * 1.6) * 0.3);
                if (burn > 0.5)
                {
                    float2 cuv = i.positionWS.xz * 4.5 + i.positionWS.y * 3.1;
                    float crack = smoothstep(0.05, 0.0, LSVoronoi(cuv).y) * smoothstep(0.55, 0.78, LSFbm(i.positionWS.xz * 0.65 + 5.0 + i.positionWS.y * 0.4));
                    float pulse = 0.65 + 0.35 * sin(_LSTime * 2.0 + cn * 12.0);
                    col += float3(1.5, 0.36, 0.06) * crack * charMask * saturate((burn - 0.5) * 2.0) * pulse;
                }
                col = StageFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float3 ws : TEXCOORD1; };
            DV vert(DA a) { DV o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { RRSeeThroughClip(i.cs, i.ws); return half4(normalize(i.n), 0); }
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
            struct OV { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; };
            OV vert(float4 p : POSITION) { OV o; o.ws = TransformObjectToWorld(p.xyz); o.cs = TransformWorldToHClip(o.ws); return o; }
            half frag(OV i) : SV_Target { RRSeeThroughClip(i.cs, i.ws); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vert(Attr a) : SV_POSITION
            {
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
