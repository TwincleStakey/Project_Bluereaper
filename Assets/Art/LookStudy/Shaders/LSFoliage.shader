// Authored plant meshes with their own painted textures. Instances are baked with a root (uv1)
// and a tint (vertex colour). _Remap > 0 recolours a texture through its luminance, keeping the
// painted gradient and shape while matching the target palette.
Shader "LookStudy/Foliage"
{
    Properties
    {
        _MainTex("Painted texture", 2D) = "white" {}
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.4
        _Remap("Palette remap", Range(0,1)) = 0
        _Dark("Remap dark", Color) = (0.05, 0.3, 0.17, 1)
        _Light("Remap light", Color) = (0.42, 0.82, 0.25, 1)
        _LumScale("Remap luminance scale", Float) = 1.6
        _Bend("Wind bend", Range(0,1)) = 0.25
        _RemapDark("Remap on dark areas", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull Off
        HLSLINCLUDE
        #include "LSCommon.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float _Cutoff;
        float _Remap;
        float4 _Dark;
        float4 _Light;
        float _LumScale;
        float _Bend;
        float _RemapDark;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(LSProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(LSProps)

        // Root and tint come from the instance when instanced, from uv1 and vertex colour when baked.
        #if defined(UNITY_INSTANCING_ENABLED)
            #define LS_ROOT(a) float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23)
        #else
            #define LS_ROOT(a) a.root.xyz
        #endif

        float3 Sway(float3 ws, float3 root)
        {
            float h = saturate((ws.y - root.y) / 1.2);
            float2 p = root.xz * 0.09 + float2(_LSTime * 0.35, _LSTime * 0.21);
            float gust = LSNoise(p) * 0.9 + sin(_LSTime * 2.1 + root.x * 0.9 + root.z * 0.7) * 0.12;
            float2 dir = float2(-0.77, -0.64);
            float2 away = root.xz - _LSPush.xz;
            float dist = length(away);
            float push = saturate(1.0 - dist / max(_LSPush.w, 0.001));
            float2 offs = dir * gust * _Bend + (dist > 0.001 ? away / dist : float2(0, 0)) * push * push * 0.9;
            ws.xz += offs * h * h;
            ws.y -= length(offs) * h * h * 0.25;
            float4 tr = LSTrail(root.xz);
            float flat = tr.r;
            if (flat > 0.001)
            {
                float2 pd = tr.ba * 2.0 - 1.0;
                float rise = max(ws.y - root.y, 0.0);
                ws.xz += pd * rise * flat * 0.95;
                ws.y -= rise * flat * 0.72;
            }
            return RRCutApply(ws, root, 0.15);   // greatsword cuts
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_instancing
            // LS_PROC: far plants drawn by LookStudyInstancer with RenderMeshPrimitives; matrix and tint come from
            // buffers uploaded once, indexed by the instance id (11-11 perf).
            #pragma multi_compile_local _ LS_PROC
            #if defined(LS_PROC)
            StructuredBuffer<float4x4> _LSProcMats;
            StructuredBuffer<float4> _LSProcTints;
            uint _LSProcBase;
            #endif
            struct Attr
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 root : TEXCOORD1; float4 color : COLOR;
                #if defined(LS_PROC) && !defined(UNITY_INSTANCING_ENABLED)
                uint procId : SV_InstanceID;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; float3 root : TEXCOORD3; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                #if defined(LS_PROC)
                    #if defined(UNITY_INSTANCING_ENABLED)
                    uint id = a.instanceID + _LSProcBase;
                    #else
                    uint id = a.procId + _LSProcBase;
                    #endif
                    float4x4 m = _LSProcMats[id];
                    float3 root = float3(m._m03, m._m13, m._m23);
                    float3 ws = Sway(mul(m, float4(a.positionOS.xyz, 1)).xyz, root);
                    o.normalWS = normalize(mul((float3x3)m, a.normalOS));
                    o.color = _LSProcTints[id];
                #else
                    UNITY_SETUP_INSTANCE_ID(a);
                    float3 root = LS_ROOT(a);
                    float3 ws = Sway(TransformObjectToWorld(a.positionOS.xyz), root);
                    o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                    #if defined(UNITY_INSTANCING_ENABLED)
                    o.color = UNITY_ACCESS_INSTANCED_PROP(LSProps, _Tint);
                    #else
                    o.color = a.color;
                    #endif
                #endif
                // greatsword cut: the stubble left behind reads straw-coloured from the top-down camera
                o.color.rgb *= lerp(float3(1, 1, 1), float3(1.25, 1.05, 0.45), RRCut(root.xz));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = a.uv;
                o.root = root;
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                RRSeeThroughClip(i.positionCS, i.positionWS);   // 11-13: soft see-through near the player
                float lum = dot(tex.rgb, float3(0.3, 0.59, 0.11));
                float3 remap = lerp(_Dark.rgb, _Light.rgb, saturate(lum * _LumScale));
                float petal = smoothstep(0.22, 0.42, lum);
                float3 col = lerp(tex.rgb, remap, _Remap * lerp(_RemapDark, 1.0, petal)) * i.color.rgb;

                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float ndl = dot(n, _MainLightPosition.xyz) * 0.5 + 0.5;
                float form = lerp(0.82, 1.08, smoothstep(0.25, 0.8, ndl));
                float light = LSShadow(i.positionWS) * LSCloud(i.root.xz);
                col *= lerp(_LSShadowMul, _LSSunCol * form, light);
                col = LSFog(col, i.positionWS);
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
            #pragma multi_compile_instancing
            struct DA { float4 p : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; float4 root : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; float2 uv : TEXCOORD1; float3 ws : TEXCOORD2; };
            DV vert(DA a) { DV o; UNITY_SETUP_INSTANCE_ID(a); o.ws = Sway(TransformObjectToWorld(a.p.xyz), LS_ROOT(a)); o.cs = TransformWorldToHClip(o.ws); o.n = TransformObjectToWorldNormal(a.n); o.uv = a.uv; return o; }
            half4 frag(DV i) : SV_Target { clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff); RRSeeThroughClip(i.cs, i.ws); return half4(normalize(i.n), 0); }
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
            #pragma multi_compile_instancing
            float3 _LightDirection;
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 root : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V2F vert(Attr a)
            {
                V2F o;
                UNITY_SETUP_INSTANCE_ID(a);
                float3 ws = Sway(TransformObjectToWorld(a.positionOS.xyz), LS_ROOT(a));
                float3 n = TransformObjectToWorldNormal(a.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, cs.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = a.uv;
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
