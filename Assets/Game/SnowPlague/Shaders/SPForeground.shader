// Foreground frame (FG0): red pines, dead branches and eaves at the screen edge, almost black silhouettes.
// Unlit (no realtime lights or shadows), vertical gradient ink at the base -> fog colour higher up, a little frost on
// top faces, an amber rim near lit lanterns, gentle sway. Opaque queue: atlas cards are alpha clipped, and an ordered
// 4x4 Bayer dither cuts a round hole around each player on screen (_PlayerScreenPos, set by the field script).
Shader "SnowPlague/Foreground"
{
    Properties
    {
        _BaseMap("Detail (atlas or bark)", 2D) = "white" {}
        [Toggle] _UseTex("Use detail texture", Float) = 1
        _InkCol("Base (ink)", Color) = (0.055, 0.07, 0.086, 1)
        _TopCol("Top (fog)", Color) = (0.27, 0.32, 0.37, 1)
        _GradBase("Gradient base height (m)", Float) = 0
        _GradTop("Gradient top height (m)", Float) = 12
        _Detail("Detail contrast", Range(0, 1)) = 0.35
        _FrostCol("Frost", Color) = (0.55, 0.6, 0.65, 1)
        _Frost("Frost on top faces", Range(0, 1)) = 0.35
        [HDR] _RimCol("Rim colour (amber)", Color) = (0.95, 0.6, 0.25, 1)
        _Rim("Amber rim strength", Range(0, 1)) = 0.2
        _Bend("Sway", Range(0, 1)) = 0.15
        _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha clip (cards)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        Cull [_Cull]
        ZWrite On
        ZTest LEqual

        HLSLINCLUDE
        #include "Assets/Game/SnowPlague/Shaders/SPCommon.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _InkCol, _TopCol, _FrostCol, _RimCol;
            float _UseTex, _GradBase, _GradTop, _Detail, _Frost, _Rim, _Bend, _Cutoff;
        CBUFFER_END
        struct FGAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct FGVary { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float3 n : TEXCOORD2; float4 color : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
        FGVary vert(FGAttr a)
        {
            FGVary o = (FGVary)0; UNITY_SETUP_INSTANCE_ID(a); UNITY_TRANSFER_INSTANCE_ID(a, o);
            float3 ws = TransformObjectToWorld(a.positionOS.xyz);
            float3 root = TransformObjectToWorld(float3(0, 0, 0));
            float s = a.color.a * a.color.a * _Bend;
            ws.xz += _RF_Wind.xy * (sin(_RF_Time * 0.7 + root.x * 0.3 + a.color.b * 6.28) * 0.5 + 0.6) * s * _RF_Wind.z;
            o.ws = ws; o.positionCS = TransformWorldToHClip(ws); o.n = TransformObjectToWorldNormal(a.normalOS);
            o.uv = TRANSFORM_TEX(a.uv, _BaseMap); o.color = a.color;
            return o;
        }
        float4 FGDetail(FGVary i)
        {
            return _UseTex > 0.5 ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) : float4(0.5, 0.5, 0.5, 1);
        }
        void FGClip(FGVary i, float4 t)
        {
        #if defined(_ALPHATEST_ON)
            clip(t.a - _Cutoff);
        #endif
            SPSeeThroughClip(i.positionCS, i.ws);
        }
        ENDHLSL

        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            half4 frag(FGVary i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 t = FGDetail(i);
                FGClip(i, t);
                float3 n = normalize(i.n) * (front ? 1 : -1);
                float g = saturate((i.ws.y - _GradBase) / max(_GradTop - _GradBase, 0.1));
                float3 c = lerp(_InkCol.rgb, _TopCol.rgb, g * g);
                float lum = dot(t.rgb, float3(0.3, 0.55, 0.15));
                c *= lerp(1, 0.6 + lum * 1.6, _Detail);
                // snow in soft irregular cushions on the branch layers (refs2 snowy_pines), not flat slabs: world-up x
                // world noise with a soft edge, no vertex frost (it lit whole cards), thinner on sparse needle texels,
                // and shaded - darker toward the sides/under and where the cushion is thin
            #if defined(_ALPHATEST_ON)
                n *= n.y < 0 ? -1 : 1;   // two-sided cards: the clump normal, not its back-face flip
            #endif
                float m = RFNoise(i.ws.xz * 1.6 + i.ws.y * 0.83) * 0.62 + RFNoise(i.ws.xz * 4.3 - i.ws.y * 1.7 + 3.1) * 0.38;
                float frost = smoothstep(0.25, 0.9, n.y) * smoothstep(0.4, 0.68, m) * _Frost * saturate(_SP_SnowAmount * 1.5);
                frost *= lerp(0.5, 1, smoothstep(0.55, 0.95, t.a));
                float3 fc = _FrostCol.rgb * lerp(0.55, 1.0, saturate(n.y)) * lerp(0.8, 1.05, saturate((m - 0.5) * 3));
                c = lerp(c, fc, frost * 0.9);
                float3 v = GetWorldSpaceNormalizeViewDir(i.ws);
                // amber fresnel rim 0.1-0.3 (spec), brighter near a lit lantern; cold moon rim from SPRim stays off here
                float fr = pow(1 - saturate(dot(n, v)), 3);
                c += _RimCol.rgb * fr * _Rim * (1 + 2 * SPLanternWarm(i.ws));
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            half4 frag(FGVary i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); FGClip(i, FGDetail(i)); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            half4 frag(FGVary i, bool front : SV_IsFrontFace) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); FGClip(i, FGDetail(i)); return half4(normalize(i.n) * (front ? 1 : -1), 0); }
            ENDHLSL
        }
    }
}
