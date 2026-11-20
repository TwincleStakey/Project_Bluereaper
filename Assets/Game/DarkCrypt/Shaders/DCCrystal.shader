// DarkCrypt crystal (v3, 11-15: the Sketchfab CC BY crystal scans). The scan's colour map only decides WHERE crystal is
// (saturated texels) versus rock (grey/black texels) and which glow it gets (red-ish texels -> _Glow2 magenta-blue,
// the rest -> _Glow violet); its own bright cyan never shows. Dark body lit by the main light, a self-lit inner glow that
// rises toward the tips (height gradient) and pools in the facet centres (texture brightness), plus a fresnel rim.
// HDR so only crystals and candles reach the bloom threshold. Opaque, casts shadows, fogged. No texture = whole mesh is crystal.
Shader "DarkCrypt/Crystal"
{
    Properties
    {
        _BaseMap ("Scan colour map (mask source)", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _Body ("Body colour (linear)", Color) = (0.03, 0.012, 0.05, 1)
        _Rock ("Rock colour (linear)", Color) = (0.035, 0.03, 0.04, 1)
        _Glow ("Glow (HDR)", Color) = (0.55, 0.14, 1.1, 1)
        _Glow2 ("Second glow for red-ish texels (HDR)", Color) = (0.9, 0.12, 0.7, 1)
        _Rim ("Rim (HDR)", Color) = (0.7, 0.4, 1.3, 1)
        _GlowBase ("Glow at the base", Range(0, 1)) = 0.12
        _Height ("Crystal height for the tip gradient (m)", Float) = 3
        _Pulse ("Pulse amount", Range(0, 1)) = 0.12
        _UseTex ("Use scan mask", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _Body, _Rock, _Glow, _Glow2, _Rim; float _GlowBase, _Height, _Pulse, _UseTex;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; float3 n : TEXCOORD1; float h : TEXCOORD2; float fog : TEXCOORD3; float3 o : TEXCOORD4; float2 uv : TEXCOORD5; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.w = TransformObjectToWorld(v.pos.xyz);
                o.n = TransformObjectToWorldNormal(v.n);
                o.o = TransformObjectToWorld(float3(0, 0, 0));
                o.h = o.w.y - o.o.y;
                o.uv = v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                o.pos = TransformWorldToHClip(o.w);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.n) * (front ? 1 : -1);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.w));
                float3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float mx = max(tex.r, max(tex.g, tex.b)), mn = min(tex.r, min(tex.g, tex.b));
                float sat = mx - mn;
                float crystal = lerp(1, smoothstep(0.08, 0.3, sat) * smoothstep(0.06, 0.22, mx), _UseTex);
                float hot = lerp(1, smoothstep(0.3, 0.95, mx), _UseTex);             // facet centres/edges brighter
                float facet = 0.45 + 0.9 * frac(sin(dot(round(n * 3), float3(12.9, 78.2, 37.7))) * 43758.5);   // each face its own brightness
                float red = _UseTex * smoothstep(0.05, 0.25, tex.r - tex.g);          // magenta texels -> second glow
                float3 glowCol = lerp(_Glow.rgb, _Glow2.rgb, red);

                Light L = GetMainLight(TransformWorldToShadowCoord(i.w));
                float ndl = saturate(dot(n, L.direction));
                float3 lit = 0.3 + L.color * ndl * L.shadowAttenuation * 1.2;
                float3 col = lerp(_Rock.rgb * (0.6 + 0.8 * mx), _Body.rgb, crystal) * lit;

                // facets catch the moon: a tight reflection so the dark faces still show their shape
                float spec = pow(saturate(dot(reflect(-L.direction, n), vdir)), 24) * L.shadowAttenuation;
                col += _Rim.rgb * spec * 0.35 * crystal;

                float tip = saturate(i.h / max(_Height, 0.1));
                float pulse = 1 + _Pulse * sin(_Time.y * 1.1 + i.o.x * 0.7 + i.o.z * 0.4);
                // inner glow: almost nothing at the base, rising toward the tips, strongest in the bright facet centres
                float inner = lerp(_GlowBase, 1, pow(tip, 2.2)) * (0.12 + 0.9 * hot * hot);
                col += glowCol * inner * crystal * pulse * facet * 0.5;
                float fres = pow(1 - saturate(dot(n, vdir)), 3.5);
                col += _Rim.rgb * fres * (0.2 + 0.8 * crystal) * 0.8;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(A v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 w = TransformObjectToWorld(v.pos.xyz), n = TransformObjectToWorldNormal(v.n);
                float4 p = TransformWorldToHClip(ApplyShadowBias(w, n, _LightDirection));
                #if UNITY_REVERSED_Z
                p.z = min(p.z, UNITY_NEAR_CLIP_VALUE);
                #else
                p.z = max(p.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return p;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 pos : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(A v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
