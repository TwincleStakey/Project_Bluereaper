// BossSeal edge structures (pillars/walls outside the arena). Lit matte scan with a red fade: the higher a fragment and the
// further back (north) it stands, the more it turns into a thin red translucent glow, so the back of the room melts into the
// red haze like the reference. Camera-side (south) pieces also take the RoomRun soft see-through around the player.
Shader "BossSeal/HazeProp"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "grey" {}
        _BumpMap("Normal (GL)", 2D) = "bump" {}
        _Tint("Tint", Color) = (0.5, 0.36, 0.32, 1)
        _Haze("Haze colour (HDR)", Color) = (0.7, 0.09, 0.04, 1)
        _FadeY("Fade: start y, full y, alpha at full, north z full", Vector) = (1.0, 9.0, 0.12, 46)
        _NorthZ("North fade starts at z", Float) = 22
        _NrmStr("Normal strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Game/RoomRun/Shaders/RRSeeThrough.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _Tint, _Haze, _FadeY;
        float _NorthZ, _NrmStr;
        CBUFFER_END
        float HazeF(float3 ws)
        {
            float h = smoothstep(_FadeY.x, _FadeY.y, ws.y);
            float nz = smoothstep(_NorthZ, _FadeY.w, ws.z);
            return saturate(h * (0.55 + 0.45 * nz) + nz * 0.35);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 n : TEXCOORD1; float4 t : TEXCOORD2; float2 uv : TEXCOORD3; float fog : TEXCOORD4; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.n = TransformObjectToWorldNormal(a.normalOS);
                o.t = float4(TransformObjectToWorldDir(a.tangentOS.xyz), a.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                RRSeeThroughClip(i.positionCS, i.positionWS);
                float3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                alb = lerp(alb, dot(alb, float3(0.3, 0.59, 0.11)).xxx, 0.3) * _Tint.rgb;
                float3 nts = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _NrmStr);
                float3 N = normalize(i.n), T = normalize(i.t.xyz), B = cross(N, T) * i.t.w;
                float3 n = normalize(T * nts.x + B * nts.y + N * nts.z);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog; d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS); d.shadowMask = 1;
                SurfaceData sd = (SurfaceData)0;
                sd.albedo = saturate(alb); sd.smoothness = 0.05; sd.occlusion = 1; sd.alpha = 1; sd.normalTS = float3(0, 0, 1);
                half4 col = UniversalFragmentPBR(d, sd);
                col.rgb = MixFog(col.rgb, i.fog);
                float f = HazeF(i.positionWS);
                col.rgb = lerp(col.rgb, _Haze.rgb, f * 0.85);
                col.a = lerp(1, _FadeY.z, f);
                return col;
            }
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
            struct A { float4 p : POSITION; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); return o; }
            half frag(V i) : SV_Target { RRSeeThroughClip(i.cs, i.ws); if (HazeF(i.ws) > 0.5) clip(-1); return 0; }
            ENDHLSL
        }
    }
}
