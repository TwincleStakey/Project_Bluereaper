// Terrain splat with scanned textures. Vertex colour: R = riverbed pebbles, G = shore gravel, B = rock, A = forest floor.
Shader "LookStudy/StageTerrain"
{
    Properties
    {
        _PebbleTex("Pebbles", 2D) = "gray" {}
        _PebbleNor("Pebbles normal", 2D) = "bump" {}
        _GravelTex("Gravel", 2D) = "gray" {}
        _GravelNor("Gravel normal", 2D) = "bump" {}
        _RockTex("Rock", 2D) = "gray" {}
        _RockNor("Rock normal", 2D) = "bump" {}
        _ForestTex("Forest floor", 2D) = "gray" {}
        _ForestNor("Forest normal", 2D) = "bump" {}
        _RockTint("Rock tint", Color) = (0.82, 0.78, 0.72, 1)
        _PebbleTint("Pebble tint", Color) = (0.8, 0.76, 0.66, 1)
        _ForestTint("Forest tint", Color) = (0.62, 0.62, 0.5, 1)
        _RockScale("Rock texture scale", Float) = 0.11
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "LSStage.hlsl"
        TEXTURE2D(_PebbleTex); SAMPLER(sampler_PebbleTex); TEXTURE2D(_PebbleNor);
        TEXTURE2D(_GravelTex); TEXTURE2D(_GravelNor);
        TEXTURE2D(_RockTex); TEXTURE2D(_RockNor);
        TEXTURE2D(_ForestTex); TEXTURE2D(_ForestNor);
        CBUFFER_START(UnityPerMaterial)
        float4 _RockTint;
        float4 _PebbleTint;
        float4 _ForestTint;
        float _RockScale;
        CBUFFER_END
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 screen : TEXCOORD2; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.color = a.color;
                return o;
            }

            #define S(t, uv) SAMPLE_TEXTURE2D(t, sampler_PebbleTex, uv)
            float3 NorW(float4 packed, float3 n, float k)
            {
                float3 t = UnpackNormal(packed);
                return float3(t.x * k, 0, t.y * k);
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float3 N = normalize(i.normalWS);
                float2 uvF = ws.xz * 0.22, uvP = ws.xz * 0.28, uvG = ws.xz * 0.34;

                // Triplanar rock so steep cliff faces do not stretch.
                float3 tw = pow(abs(N), 4.0); tw /= tw.x + tw.y + tw.z;
                float rs = _RockScale;
                float3 rock = S(_RockTex, ws.zy * rs).rgb * tw.x + S(_RockTex, ws.xz * rs).rgb * tw.y + S(_RockTex, ws.xy * rs).rgb * tw.z;
                float3 rockN = NorW(S(_RockNor, ws.xz * rs), N, 0.9);
                float rl = dot(rock, float3(0.3, 0.59, 0.11));
                rock = _RockTint.rgb * saturate(rl * 2.3);

                float3 pebble = S(_PebbleTex, uvP).rgb * _PebbleTint.rgb;
                float3 gravel = S(_GravelTex, uvG).rgb * _PebbleTint.rgb;
                float3 forest = S(_ForestTex, uvF).rgb * _ForestTint.rgb;

                float brk = (LSFbm(ws.xz * 0.4) - 0.5) * 0.6;
                float mP = smoothstep(0.35, 0.65, i.color.r + brk);
                float mG = smoothstep(0.35, 0.65, i.color.g + brk);
                float mF = smoothstep(0.35, 0.65, i.color.a + brk);
                float slope = smoothstep(0.55, 0.78, 1.0 - N.y + brk * 0.25);
                float mR = saturate(max(i.color.b, slope));

                float3 albedo = gravel;
                float3 dn = NorW(S(_GravelNor, uvG), N, 0.8);
                albedo = lerp(albedo, forest, mF); dn = lerp(dn, NorW(S(_ForestNor, uvF), N, 0.8), mF);
                albedo = lerp(albedo, pebble, mP); dn = lerp(dn, NorW(S(_PebbleNor, uvP), N, 1.0), mP);
                albedo = lerp(albedo, rock, mR); dn = lerp(dn, rockN, mR);
                float3 n = normalize(N + dn);
                // Footprints: darker, smoother hollows with a lip, strongest on dirt and gravel.
                float soft = saturate(1.0 - mR * 0.85);
                float e = _LSTrailRect.z / 2048.0;
                float fp = LSTrail(ws.xz).g * soft;
                if (fp > 0.002)
                {
                    float fx = LSTrail(ws.xz + float2(e, 0)).g - LSTrail(ws.xz - float2(e, 0)).g;
                    float fz = LSTrail(ws.xz + float2(0, e)).g - LSTrail(ws.xz - float2(0, e)).g;
                    n = normalize(n + float3(fx, 0, fz) * 2.2 * soft);
                    albedo *= 1.0 - fp * 0.42;
                }

                float wet = 1.0 - smoothstep(_LSWaterY + 0.05, _LSWaterY + 0.5, ws.y + brk * 0.3);
                albedo *= lerp(1.0, 0.42, wet);
                float gloss = lerp(0.08, 0.6, wet) + fp * 0.18;
                float3 col = StageLight(albedo, n, ws, i.screen.xy / i.screen.w, gloss, 1.0);
                col = StageFog(col, ws);
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
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
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
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
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
