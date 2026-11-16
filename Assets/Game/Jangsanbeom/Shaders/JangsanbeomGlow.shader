// 장산범 (Jangsanbeom) boss: glowing eyes, glowing fur rim and twinkling fur glints for very dark maps.
// URP forward. Needs Bloom (Volume) + HDR on the camera to actually spread light.
Shader "BulletHeaven/JangsanbeomGlow"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _AmbientBoost ("Ambient Boost (keeps the body readable in the dark)", Range(0, 2)) = 0.35

        [Header(Eyes)]
        [NoScaleOffset] _EyeMask ("Eye Mask (R)", 2D) = "black" {}
        [HDR] _EyeColor ("Eye Color", Color) = (5, 0.25, 0.12, 1)
        _EyeIntensity ("Eye Intensity", Range(0, 20)) = 6
        _EyePulseSpeed ("Eye Pulse Speed", Range(0, 10)) = 1.6
        _EyePulseAmount ("Eye Pulse Amount", Range(0, 1)) = 0.35

        [Header(Fur Rim)]
        [HDR] _RimColor ("Rim Color", Color) = (0.55, 0.75, 1.4, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity ("Rim Intensity", Range(0, 10)) = 1.5
        _FurLumMin ("Fur Brightness Threshold", Range(0, 1)) = 0.45

        [Header(Fur Glints)]
        [HDR] _SparkleColor ("Glint Color", Color) = (2.5, 2.8, 3.5, 1)
        _SparkleIntensity ("Glint Intensity", Range(0, 20)) = 5
        _SparkleDensity ("Glint Density (cells per metre)", Range(1, 600)) = 7
        _SparkleAmount ("Glint Amount", Range(0, 1)) = 0.12
        _SparkleSpeed ("Glint Twinkle Speed", Range(0, 20)) = 4

        [Header(Warning Telegraph (driven by JangsanbeomTelegraph))]
        [Header(Claws)]
        [HDR] _ClawColor ("Claw Glow Color", Color) = (4, 0.15, 0.08, 1)
        _ClawIntensity ("Claw Glow", Range(0, 20)) = 8
        _ClawHeight ("Claw Height (object units above the floor)", Range(0, 100)) = 50
        _ClawRedness ("Claw Redness Threshold", Range(0, 1)) = 0.05

        _Warn ("Warn (0 = calm, 1 = about to attack)", Range(0, 1)) = 0
        [HDR] _WarnColor ("Warn Color", Color) = (3, 0.18, 0.08, 1)
        _WarnFurIntensity ("Warn Fur Glow", Range(0, 8)) = 4
        _WarnRimIntensity ("Warn Rim Glow", Range(0, 20)) = 12
        _WarnPulseSpeed ("Warn Heartbeat Speed", Range(0, 20)) = 7
        _WarnWaveFreq ("Warn Wave Frequency (per metre)", Range(0, 5)) = 0.6
        _WarnWaveSpeed ("Warn Wave Speed", Range(0, 20)) = 6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _AmbientBoost;
            half4 _EyeColor; half _EyeIntensity; half _EyePulseSpeed; half _EyePulseAmount;
            half4 _RimColor; half _RimPower; half _RimIntensity; half _FurLumMin;
            half4 _SparkleColor; half _SparkleIntensity; half _SparkleDensity; half _SparkleAmount; half _SparkleSpeed;
            half4 _ClawColor; half _ClawIntensity; half _ClawHeight; half _ClawRedness;
            half _Warn; half4 _WarnColor; half _WarnFurIntensity; half _WarnRimIntensity; half _WarnPulseSpeed; half _WarnWaveFreq; half _WarnWaveSpeed;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EyeMask); SAMPLER(sampler_EyeMask);
        // capture tools freeze time (same flag as the parasite shader); without it the captures ran everything 3x+ fast
        float _JSB_UseFixedTime; float _JSB_FixedTime;
        float T() { return _JSB_UseFixedTime > 0.5 ? _JSB_FixedTime : _Time.y; }
        // lub-dub heartbeat shared with the parasite (_BeatSpeed 0.8 there), faster when warning
        float Beat(float warn)
        {
            float p = frac(T() * 0.8 * (1 + 0.8 * warn));
            return exp(-p * 10) + 0.6 * exp(-abs(p - 0.22) * 18);
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1;
                float3 normalWS:TEXCOORD2; float3 positionOS:TEXCOORD3; half fog:TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.positionOS = i.positionOS.xyz;   // skinned: stable on the body, so glints stick to the fur
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }

            half4 frag(Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                // geometric normal from screen derivatives: the imported Tripo normals are unreliable
                float3 gn = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                float3 vn = i.normalWS;
                float vlen = length(vn);
                float3 n = (vlen > 0.5 && vlen < 1.5) ? vn / vlen : gn;
                if (dot(n, gn) < 0) n = -n;          // agree with the real surface side
                if (dot(n, v) < 0) n = -n;           // and face the viewer

                // lighting: main + additional lights (Lambert) + ambient
                Light ml = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 lit = ml.color * saturate(dot(n, ml.direction)) * ml.shadowAttenuation * ml.distanceAttenuation;
                // ambient from the scene's flat ambient colour; SampleSH can pick up a bright probe from another open scene
                half3 amb = max(unity_AmbientSky.rgb, half3(0.02, 0.022, 0.03));
                half3 color = albedo.rgb * (lit + amb * (1 + _AmbientBoost * 4));

                half lum = dot(albedo.rgb, half3(0.3, 0.59, 0.11));
                half fur = smoothstep(_FurLumMin, _FurLumMin + 0.2, lum);

                // eyes: masked emission with a slow pulse
                half eye = SAMPLE_TEXTURE2D(_EyeMask, sampler_EyeMask, i.uv).r;
                half pulse = 1 - _EyePulseAmount * (0.5 + 0.5 * sin(T() * _EyePulseSpeed));
                color += eye * _EyeColor.rgb * _EyeIntensity * pulse * (1 + _Warn * 1.5);

                // claws: the reddish, non-fur parts of the paws near the floor (bind pose height), glowing red with the eye pulse
                half redness = albedo.r - max(albedo.g, albedo.b);
                half claw = smoothstep(_ClawRedness, _ClawRedness + 0.08, redness) * (1 - smoothstep(_ClawHeight * 0.6, _ClawHeight, i.positionOS.y));
                color += claw * _ClawColor.rgb * _ClawIntensity * pulse * (1 + _Warn * 2);

                // fur rim: silhouette glow on the pale fur
                half rim = pow(1 - saturate(abs(dot(gn, v))), _RimPower);   // geometric normal: robust on the Tripo mesh
                half3 rimCol = lerp(_RimColor.rgb * _RimIntensity, _WarnColor.rgb * _WarnRimIntensity, _Warn);
                color += rim * fur * rimCol;

                // warning telegraph: the fur glows red with a fast heartbeat and rings of light spreading out from the body centre
                half beat = saturate(Beat(_Warn));   // 11-16 Kido: a pulse, not a flicker
                half ring = 0.5 + 0.5 * sin(length(i.positionOS.xz) * _WarnWaveFreq * 6.2832 - T() * _WarnWaveSpeed * 0.5);
                color += _Warn * fur * _WarnColor.rgb * _WarnFurIntensity * (0.6 + 0.4 * beat) * (0.55 + 0.45 * ring * ring);

                // fur glints: random cells on the body glow with the heartbeat
                float3 cellP = i.positionOS * _SparkleDensity;
                float3 cell = floor(cellP);
                float h = hash31(cell);
                // 11-16 Kido ("맥박같은 느낌, 너무 반짝거려"): the glints no longer blink on their own; they swell together
                // with the heartbeat and a new set of cells takes over every few beats
                float bt = T() * 0.8 * (1 + 0.8 * _Warn);
                float tw = saturate(Beat(_Warn)) * 0.85 + 0.15 * h;
                float on = step(1 - _SparkleAmount, hash31(cell + floor(bt / 3.0)));
                float3 f = frac(cellP) - 0.5; float dotShape = saturate(1 - length(f) * 2.2);
                half facing = pow(saturate(dot(reflect(-v, n), ml.direction) * 0.5 + 0.5), 4) + 0.35;
                color += _SparkleColor.rgb * _SparkleIntensity * on * tw * dotShape * dotShape * fur * facing;

                color = MixFog(color, i.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            float4 vertS(A i):SV_POSITION
            {
                float3 pw = TransformObjectToWorld(i.positionOS.xyz); float3 nw = TransformObjectToWorldNormal(i.normalOS);
                float4 c = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
                #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return c;
            }
            half4 fragS():SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            struct A { float4 positionOS:POSITION; };
            float4 vertD(A i):SV_POSITION { return TransformObjectToHClip(i.positionOS.xyz); }
            half4 fragD():SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
