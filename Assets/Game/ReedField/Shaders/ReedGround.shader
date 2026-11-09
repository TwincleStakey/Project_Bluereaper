// Marsh ground under the reeds, textured with Poly Haven CC0 scans:
//   reed litter on mud (main), cracked dry ground (raised spots and the trodden path), wet mud near water,
//   river pebbles along the banks, and small puddles in the wet zone.
// Vertex colour: R = wet (near the channel/river), G = trodden path/clearing, B = under the reeds (darker, litter).
// Every layer is sampled twice at different scales and rotations and blended with large noise, so tiling never shows.
// Also: footprints from the LookStudy trail map, moon/sun shadows, wisp light and the field's fog.
Shader "ReedField/Ground"
{
    Properties
    {
        _LitterTex("Reed litter (diffuse)", 2D) = "grey" {}
        _LitterNrm("Reed litter (normal)", 2D) = "bump" {}
        _DryTex("Cracked dry ground", 2D) = "grey" {}
        _DryNrm("Cracked dry ground (normal)", 2D) = "bump" {}
        _MudTex("Wet mud", 2D) = "grey" {}
        _MudNrm("Wet mud (normal)", 2D) = "bump" {}
        _PebbleTex("River pebbles", 2D) = "grey" {}
        _PebbleNrm("River pebbles (normal)", 2D) = "bump" {}
        _Tile("Metres per tile", Float) = 3.5
        _Tint("Tint (mood)", Color) = (1, 0.9, 0.78, 1)
        _Peat("Under reeds", Color) = (0.10, 0.07, 0.045, 1)
        _Mud("unused", Color) = (0.17, 0.12, 0.075, 1)
        _Moss("unused", Color) = (0.20, 0.16, 0.08, 1)
        _Path("unused", Color) = (0.26, 0.19, 0.12, 1)
        _Ambient("Ambient", Color) = (0.34, 0.22, 0.14, 1)
        _NormalStrength("Normal strength", Range(0, 2)) = 0.45
        _Calm("Calm (less texture contrast)", Range(0, 1)) = 0.55
        _Puddles("Puddles", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
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
            #include "ReedCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_LitterTex); TEXTURE2D(_LitterNrm); TEXTURE2D(_DryTex); TEXTURE2D(_DryNrm);
            TEXTURE2D(_MudTex); TEXTURE2D(_MudNrm); TEXTURE2D(_PebbleTex); TEXTURE2D(_PebbleNrm);
            TEXTURE2D(_PlanarReflectionTexture);
            SAMPLER(sampler_LitterTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _LitterTex_ST, _LitterNrm_ST, _DryTex_ST, _DryNrm_ST, _MudTex_ST, _MudNrm_ST, _PebbleTex_ST, _PebbleNrm_ST;
                float _Tile, _NormalStrength, _Puddles, _Calm;
                float4 _Tint, _Peat, _Mud, _Moss, _Path, _Ambient;
            CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float4 color : COLOR; };

            V2F vert(Attr a)
            {
                V2F o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.n = TransformObjectToWorldNormal(a.normalOS);
                o.positionCS = TransformWorldToHClip(o.ws); o.color = a.color; return o;
            }

            // Two samples of one layer: base tiling and a rotated, larger one, mixed by a broad noise.
            void Layer(TEXTURE2D_PARAM(tex, smp), TEXTURE2D_PARAM(nrm, smpN), float2 xz, float mixN, out float3 col, out float2 nxy)
            {
                float2 uv1 = xz / _Tile;
                float2 uv2 = float2(xz.x * 0.809 - xz.y * 0.588, xz.x * 0.588 + xz.y * 0.809) / (_Tile * 1.73) + 0.37;
                col = lerp(SAMPLE_TEXTURE2D(tex, smp, uv1).rgb, SAMPLE_TEXTURE2D(tex, smp, uv2).rgb, mixN);
                float2 n1 = SAMPLE_TEXTURE2D(nrm, smpN, uv1).rg * 2 - 1;
                float2 n2 = SAMPLE_TEXTURE2D(nrm, smpN, uv2).rg * 2 - 1;
                nxy = lerp(n1, n2, mixN);
            }

            half4 frag(V2F i) : SV_Target
            {
                float2 xz = i.ws.xz;
                float mixN = smoothstep(0.3, 0.7, RFNoise(xz * 0.07 + 3.3));
                float big = RFNoise(xz * 0.045 + 11.0), mid = RFNoise(xz * 0.21 + 2.0), fine = RFNoise(xz * 1.3);

                float path = saturate(i.color.g), under = saturate(i.color.b);
                // Wetness from the height above the water: dry bank -> damp band -> glossy wet line -> submerged.
                float above = i.ws.y - _RF_WaterLevel;
                float wet = 1 - smoothstep(0.02, 0.85, above);          // damp band, wide and soft
                float sheen = 1 - smoothstep(-0.02, 0.14, above);      // thin glossy film right at the waterline
                float sunk = 1 - smoothstep(-0.25, 0.0, above);        // under the water: dark settled mud
                // Weights first, then only the layers that show here are sampled (most pixels need one or two).
                float wDry = saturate(smoothstep(0.55, 0.75, big + (mid - 0.5) * 0.4) * (1 - wet) + path * 0.8);
                float wMud = smoothstep(0.25, 0.75, wet + (mid - 0.5) * 0.5);
                // River gravel: clumps along the waterline (from the shallows up the wet band), thinning out up the bank,
                // broken up by noise so it never reads as a stripe.
                float band = smoothstep(-0.38, -0.08, above) * (1 - smoothstep(0.2, 0.7, above));
                float clump = smoothstep(0.34, 0.58, RFNoise(xz * 0.55 + 5.0) * 0.55 + mid * 0.35 + fine * 0.2);
                float strayN = RFNoise(xz * 1.7 + 9.0);
                float stray = (1 - smoothstep(0.3, 1.3, above)) * smoothstep(-0.3, -0.1, above) * smoothstep(0.66, 0.78, strayN);
                float wPeb = saturate(max(band * clump, stray * 0.8)) * (1 - path);
                // pale bare sand/mud on the bank just above the water, like a sandbar
                wDry = max(wDry, smoothstep(0.0, 0.35, wet) * (1 - sunk) * 0.85) * (1 - wPeb);
                float2 uv1 = xz / _Tile;
                float2 uv2 = float2(xz.x * 0.809 - xz.y * 0.588, xz.x * 0.588 + xz.y * 0.809) / (_Tile * 1.73) + 0.37;
                // The main litter layer keeps two blended samples against tiling; the others use one.
                float3 col = lerp(SAMPLE_TEXTURE2D(_LitterTex, sampler_LitterTex, uv1).rgb, SAMPLE_TEXTURE2D(_LitterTex, sampler_LitterTex, uv2).rgb, mixN);
                float2 nxy = SAMPLE_TEXTURE2D(_LitterNrm, sampler_LitterTex, lerp(uv1, uv2, step(0.5, mixN))).rg * 2 - 1;
                [branch] if (wDry > 0.01)
                {
                    col = lerp(col, SAMPLE_TEXTURE2D(_DryTex, sampler_LitterTex, uv2).rgb, wDry);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_DryNrm, sampler_LitterTex, uv2).rg * 2 - 1, wDry);
                }
                [branch] if (wMud > 0.01)
                {
                    col = lerp(col, SAMPLE_TEXTURE2D(_MudTex, sampler_LitterTex, uv1).rgb, wMud);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_MudNrm, sampler_LitterTex, uv1).rg * 2 - 1, wMud);
                }
                [branch] if (wPeb > 0.01)
                {
                    col = lerp(col, SAMPLE_TEXTURE2D(_PebbleTex, sampler_LitterTex, uv1).rgb, wPeb);
                    nxy = lerp(nxy, SAMPLE_TEXTURE2D(_PebbleNrm, sampler_LitterTex, uv1).rg * 2 - 1, wPeb);
                }
                // Under the reeds: darker, damper litter in their shade.
                col = lerp(col, col * _Peat.rgb * 4.0, under * 0.75);
                // Calm, low-contrast floor: pull the scan detail toward its local average colour.
                float3 avg = float3(0.30, 0.24, 0.17);
                col = lerp(col, avg * (0.8 + 0.4 * dot(col, float3(0.33, 0.33, 0.33)) / 0.24), _Calm * (1 - wPeb * 0.6));
                col *= _Tint.rgb * (0.92 + 0.16 * big);

                // Puddles: flat, dark, glossy patches in the wet zone.
                float puddle = smoothstep(0.62, 0.68, RFNoise(xz * 0.35 + 7.0) * 0.7 + mid * 0.3) * smoothstep(0.35, 0.7, wet) * _Puddles;
                col = lerp(col, col * 0.35, puddle);
                // damp darkening, wet film and submerged mud, all wide soft gradients
                col *= lerp(1.0, 0.62, smoothstep(0.0, 1.0, wet));
                col *= lerp(1.0, 0.75, sheen);
                col = lerp(col, col * 0.68, sunk);   // shallow bed stays readable through the water (gravel, mud)
                nxy *= (1 - puddle) * _NormalStrength;

                float4 tr = RFTrail(xz);
                col *= 1 - tr.g * 0.45;   // footprints
                col *= 1 - tr.r * 0.12;   // flattened reeds lying on it

                float3 n0 = normalize(i.n);
                float3 n = normalize(n0 + float3(nxy.x, 0, nxy.y));
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.ws));
                // Realtime shadows now come only from moving things (players, enemies); the field itself is baked.
                ml.shadowAttenuation *= RFStaticShadow(xz);
                float3 lit = _Ambient.rgb + ml.color * saturate(dot(n, ml.direction)) * ml.shadowAttenuation;
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float3 h = normalize(ml.direction + v);
                float gloss = max(max(wet * 0.25, puddle), sheen);
                float3 ng = normalize(lerp(n, n0, max(puddle, sheen)));
                float spec = pow(saturate(dot(ng, h)), lerp(24, 160, max(puddle, sheen))) * gloss * ml.shadowAttenuation * (0.35 + max(puddle, sheen) * 1.6);
                // the wet film mirrors a little of the warm sky
                float fresG = pow(1 - saturate(dot(ng, v)), 3);
                float3 c = col * lit + ml.color * spec + SAMPLE_TEXTURE2D(_PlanarReflectionTexture, sampler_LitterTex, float2(0.5, 0.6)).rgb * sheen * (0.08 + fresG * 0.3);
                InputData inputData = (InputData)0;
                inputData.positionWS = i.ws;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, i.ws, half4(1, 1, 1, 1));
                    c += col * add.color * add.distanceAttenuation * saturate(dot(n, add.direction));
                LIGHT_LOOP_END
                // low fog measured from y = 0, so the sunken channel bed is not whitened under the water
                return half4(RFFog(c, float3(i.ws.x, max(i.ws.y, 0), i.ws.z)), 1);
            }
            ENDHLSL
        }
        // The PC renderer runs SSAO from a DepthNormals prepass, and the camera depth texture then comes from that pass:
        // a shader without DepthNormals is missing from scene depth. Without these two passes the ground was invisible to
        // the water's depth fade (the water went fully opaque and showed its mesh edge) and to the ink outlines.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.positionOS.xyz); o.n = TransformObjectToWorldNormal(a.normalOS); return o; }
            half4 frag(V i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
