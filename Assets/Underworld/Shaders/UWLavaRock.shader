// Megascans volcanic rocks standing in the lava sea (UWLavaRocks.cs). The scene ambient is ~0.02, so plain Lit renders them
// as black holes over the lava. Here the scan albedo gets a flat ambient lift plus a warm under-glow that fades with height
// above the lava surface (light bouncing up from the sea). No patterns are drawn; all detail is the scan's own texture.
Shader "Underworld/LavaRock"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BumpMap("Normal", 2D) = "bump" {}
        _Tint("Tint", Color) = (0.55, 0.5, 0.48, 1)
        _AmbLift("Ambient lift", Color) = (0.05, 0.045, 0.05, 1)
        [HDR] _GlowCol("Lava under-glow", Color) = (1.0, 0.35, 0.1, 1)
        _GlowInt("Under-glow intensity", Range(0, 8)) = 2.5
        _GlowFall("Under-glow falloff (m)", Range(0.2, 10)) = 2.0
        _SeaY("Lava surface height", Float) = -6
        _NrmStr("Normal strength", Range(0, 2)) = 1
        _EmisMax("Hard ceiling: emission per channel", Range(0.1, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _Tint, _AmbLift, _GlowCol;
        float _GlowInt, _GlowFall, _SeaY, _NrmStr, _EmisMax;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct V2F
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1;
                float3 nWS : TEXCOORD2; float4 tWS : TEXCOORD3; float fog : TEXCOORD4;
            };
            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                VertexNormalInputs n = GetVertexNormalInputs(a.normalOS, a.tangentOS);
                o.nWS = n.normalWS; o.tWS = float4(n.tangentWS, a.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap); o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V2F i) : SV_Target
            {
                float3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _Tint.rgb;
                float3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _NrmStr);
                float3 nW = normalize(i.nWS), tW = normalize(i.tWS.xyz), bW = cross(nW, tW) * i.tWS.w;
                float3 N = normalize(mul(tn, float3x3(tW, bW, nW)));

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS; d.normalWS = N;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog; d.bakedGI = SampleSH(N);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = alb; s.smoothness = 0.08; s.occlusion = 1; s.alpha = 1; s.normalTS = float3(0, 0, 1);
                float h = max(i.positionWS.y - _SeaY, 0.0);
                float glow = exp(-h / _GlowFall) * saturate(0.35 - N.y * 0.65 + 0.65);   // faces turned down / sideways catch more
                // 11-16 white-out pass: the under-glow is a warm rim near the lava, never a light source (hard per-channel ceiling)
                s.emission = min(alb * (_AmbLift.rgb + _GlowCol.rgb * _GlowInt * glow), _EmisMax);
                half4 col = UniversalFragmentPBR(d, s);
                col.rgb = MixFog(col.rgb, i.fog);
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
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
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
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
