// 심연 고원 (KillKnight-style plateau, 11-19) floor. Flat, matte, cool dark slate grey with faint cracks, low saturation.
// All surface detail comes from downloaded Poly Haven CC0 scans:
//   base   rock_01 (smooth grey weathered rock), sampled twice at different scale/rotation and mixed by a 23 m noise
//          (no visible repeat), desaturated and tinted cool grey
//   macro  dark_rock (fissured rock) at 11 m, blended in patches + a +-10% value drift on 30 m
//   cracks cracked_concrete height map (the scan's own cracks), only in sparse patches
// uv2.x = distance to the plateau edge (metres, from KKBuilder): the cut lip catches a little light.
// URP forward lit (main light + shadows, additional lights, SSAO, fog).
Shader "KillKnight/Floor"
{
    Properties
    {
        _Base("Base albedo (rock_01)", 2D) = "grey" {}
        _BaseN("Base normal GL", 2D) = "bump" {}
        _Macro("Macro albedo (dark_rock)", 2D) = "grey" {}
        _MacroN("Macro normal GL", 2D) = "bump" {}
        _MacroH("Macro height", 2D) = "grey" {}
        _Crack("Crack height (cracked_concrete disp)", 2D) = "white" {}
        _CrackN("Crack normal GL", 2D) = "bump" {}
        _Scale("Tile m: base A, base B, macro, crack", Vector) = (5.5, 8.3, 11, 7)
        _Tint("Slate tint (linear albedo)", Color) = (0.37, 0.405, 0.41, 1)
        _MacroAmt("Macro patch amount, value drift, crack cover, crack dark", Vector) = (0.55, 0.14, 0.45, 0.55)
        _Lip("Lip: width m, brighten, inner shade width m, inner shade", Vector) = (0.22, 0.08, 0, 0)
        _EdgeGlow("Edge glow from below (HDR, linear)", Color) = (0, 0, 0, 1)   // v6 (Kido): the floor stays cool grey up to the edge
        _EdgeGlowW("Edge glow: width m, falloff power, lip boost, 0", Vector) = (4.5, 1.8, 1.5, 0)
        _NrmStr("Normal strength", Range(0, 2)) = 0.75
        _Smooth("Smoothness (matte)", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_Base); SAMPLER(sampler_Base); TEXTURE2D(_BaseN); TEXTURE2D(_Macro); TEXTURE2D(_MacroN); TEXTURE2D(_MacroH);
        TEXTURE2D(_Crack); TEXTURE2D(_CrackN);
        CBUFFER_START(UnityPerMaterial)
        float4 _Base_ST, _Scale, _Tint, _MacroAmt, _Lip, _EdgeGlow, _EdgeGlowW;
        float _NrmStr, _Smooth;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv2 : TEXCOORD1; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fog : TEXCOORD2; float edge : TEXCOORD3; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(a.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.edge = a.uv2.x;
                return o;
            }

            // gradient noise (value noise thresholds into square patches)
            float2 G2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return -1 + 2 * frac(sin(p) * 43758.5453); }
            float GN(float2 p)
            {
                float2 i = floor(p), f = frac(p); float2 u = f * f * (3 - 2 * f);
                return 0.5 + 0.5 * lerp(lerp(dot(G2(i), f), dot(G2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                                        lerp(dot(G2(i + float2(0, 1)), f - float2(0, 1)), dot(G2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }
            float FBM(float2 p) { return GN(p) * 0.55 + GN(p * 2.03 + 7.1) * 0.3 + GN(p * 4.1 + 2.3) * 0.15; }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float2 Rot(float2 p, float a) { float s = sin(a), c = cos(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
            float3 UN(float4 n) { return UnpackNormalScale(n, _NrmStr); }
            float3 RotN(float3 n, float a) { n.xy = Rot(n.xy, a); return n; }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float2 w = ws.xz;

                // ---- base: two samples of rock_01 at different scale / rotation, mixed by low-frequency noise
                float2 uA = w / _Scale.x;
                float2 uB = Rot(w, 0.65) / _Scale.y + 0.37;
                float mixAB = smoothstep(0.35, 0.65, FBM(w / 23.0));
                float3 cA = SAMPLE_TEXTURE2D(_Base, sampler_Base, uA).rgb, cB = SAMPLE_TEXTURE2D(_Base, sampler_Base, uB).rgb;
                float3 nA = UN(SAMPLE_TEXTURE2D(_BaseN, sampler_Base, uA)), nB = RotN(UN(SAMPLE_TEXTURE2D(_BaseN, sampler_Base, uB)), -0.65);
                float baseL = lerp(Luma(cA), Luma(cB), mixAB);
                float3 nts = normalize(lerp(nA, nB, mixAB));

                // ---- macro: fissured dark_rock patches, height-blended
                float2 uM = w / _Scale.z + 0.53;   // v3: isotropic scan (rock_boulder_dry)
                float mL = Luma(SAMPLE_TEXTURE2D(_Macro, sampler_Base, uM).rgb);
                float mH = SAMPLE_TEXTURE2D(_MacroH, sampler_Base, uM).r;
                float patch = FBM(w / 17.0 + 3.1);
                // 11-19 v2 (Kido: diagonal lines / scale-like patches): off by default; when on, a soft wide blend (no height contour edges)
                float wM = _MacroAmt.x > 0 ? smoothstep(1 - _MacroAmt.x, 1 - _MacroAmt.x + 0.35, patch) * 0.85 : 0;
                float l = lerp(baseL, mL * 0.85, wM);
                nts = normalize(lerp(nts, UN(SAMPLE_TEXTURE2D(_MacroN, sampler_Base, uM)), wM));

                // ---- value drift on 30 m (no flat sheet), contrast pulled toward the mean (faint detail, characters pop)
                l = lerp(0.42, l, 0.8);
                l *= 1 + _MacroAmt.y * (FBM(w / 31.0 + 11.7) * 2 - 1) + _MacroAmt.y * 0.5 * (GN(w / 9.0 + 4.4) * 2 - 1);

                // ---- cracks: the cracked_concrete scan's crack channels, only in sparse patches
                float2 uC = Rot(w, 1.1) / _Scale.w;
                float ch = SAMPLE_TEXTURE2D(_Crack, sampler_Base, uC).r;
                float crackZone = smoothstep(1 - _MacroAmt.z, 1 - _MacroAmt.z + 0.12, FBM(w / 13.0 + 21.3));
                float crack = (1 - smoothstep(0.18, 0.42, ch)) * crackZone;
                l *= 1 - _MacroAmt.w * crack;
                nts = normalize(lerp(nts, RotN(UN(SAMPLE_TEXTURE2D(_CrackN, sampler_Base, uC)), -1.1), crack * 0.7));

                // ---- the cut lip catches light
                float lip = 1 - smoothstep(0, _Lip.x, i.edge);
                l *= 1 + _Lip.y * lip;

                float3 alb = _Tint.rgb * (l / 0.42);

                float3 N = normalize(i.normalWS);
                float3 T = normalize(float3(1, 0, 0) - N * N.x);
                float3 n = normalize(T * nts.x + cross(T, N) * nts.y + N * nts.z);

                InputData d = (InputData)0;
                d.positionWS = ws;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(ws);
                d.shadowCoord = TransformWorldToShadowCoord(ws);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = saturate(alb);
                sd.smoothness = _Smooth;
                sd.metallic = 0;
                sd.occlusion = 1;
                sd.alpha = 1;
                sd.normalTS = float3(0, 0, 1);
                // v5 (Kido: "아래쪽에서 아레나 외각에 은은하게 광원이 빛이는"): the abyss glow spills over the rim onto the stone
                float eg = pow(saturate(1 - i.edge / _EdgeGlowW.x), _EdgeGlowW.y) * (1 + _EdgeGlowW.z * lip);
                sd.emission = saturate(alb) * _EdgeGlow.rgb * eg;
                half4 col = UniversalFragmentPBR(d, sd);
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half4 frag() : SV_Target { return 0; }
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
    }
}
