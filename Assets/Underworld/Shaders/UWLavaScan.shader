// Underworld lava sea (below/around the plaza), rebuilt 2026-11-15: texture-only, from the downloaded
// "Lava Texture & Material Vol. 04" by ehsan vaezi (Fab, CC BY 4.0). The glowing seams are the texture's own emissive map;
// nothing is drawn by code. Hex-tile bombing hides the repeat, a slow drift and a soft brightness breathing keep it alive.
//   AH = basecolor RGB + height A,  NR = normal RG + roughness B,  E = emissive RGB.
Shader "Underworld/LavaScan"
{
    Properties
    {
        _AH("Lava basecolor+height", 2D) = "grey" {}
        _NR("Lava normal+rough", 2D) = "bump" {}
        _E("Lava emissive", 2D) = "black" {}
        _Tile("Metres per tile", Float) = 9
        _Tint("Crust tint", Color) = (0.35, 0.3, 0.28, 1)
        [HDR] _EmitCol("Emission colour", Color) = (1.0, 0.55, 0.3, 1)
        _EmitInt("Emission intensity", Range(0, 12)) = 3
        _Flow("Drift speed (m/s)", Range(0, 0.5)) = 0.06
        _Breathe("Brightness breathing", Range(0, 1)) = 0.25
        _NrmStr("Normal strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "UWCommon.hlsl"
        TEXTURE2D(_AH); TEXTURE2D(_NR); TEXTURE2D(_E);
        SAMPLER(sampler_AH);
        CBUFFER_START(UnityPerMaterial)
        float4 _Tint, _EmitCol;
        float _Tile, _EmitInt, _Flow, _Breathe, _NrmStr;
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
            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };
            V2F vert(Attr a)
            {
                V2F o; o.positionWS = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z); return o;
            }
            float2x2 Rot(float a) { float s, c; sincos(a, s, c); return float2x2(c, -s, s, c); }
            // one hex cell sample: random rotation/offset; returns albedo+height, normal xy (world-aligned), rough, emission
            void Cell(float2 uv, float2 dx, float2 dy, float2 cell, out float4 ah, out float3 nr, out float3 em)
            {
                // offset only, no rotation: the crust plates all flow the same way, so seams never criss-cross
                float2 rnd = UWHash22(cell); float2x2 R = Rot((rnd.x - 0.5) * 0.35);
                float2 suv = mul(R, uv) + rnd * 7.31; float2 rdx = mul(R, dx), rdy = mul(R, dy);
                ah = SAMPLE_TEXTURE2D_GRAD(_AH, sampler_AH, suv, rdx, rdy);
                float4 n = SAMPLE_TEXTURE2D_GRAD(_NR, sampler_AH, suv, rdx, rdy);
                nr = float3(mul(transpose(R), n.rg * 2 - 1), n.b);
                em = SAMPLE_TEXTURE2D_GRAD(_E, sampler_AH, suv, rdx, rdy).rgb;
            }
            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float t = _UWTime;
                float2 uv = (ws.xz + float2(t, -0.6 * t) * _Flow) / _Tile;
                float2 dx = ddx(uv), dy = ddy(uv);
                float2 sk = mul(float2x2(1.0, -0.57735027, 0.0, 1.15470054), uv * 1.7320508);
                float2 b = floor(sk), f = frac(sk); float z = 1.0 - f.x - f.y;
                float3 w; float2 v1, v2, v3;
                if (z > 0) { w = float3(z, f.y, f.x); v1 = b; v2 = b + float2(0, 1); v3 = b + float2(1, 0); }
                else { w = float3(-z, 1.0 - f.y, 1.0 - f.x); v1 = b + 1; v2 = b + float2(1, 0); v3 = b + float2(0, 1); }
                float4 a1, a2, a3; float3 n1, n2, n3, e1, e2, e3;
                Cell(uv, dx, dy, v1, a1, n1, e1); Cell(uv, dx, dy, v2, a2, n2, e2); Cell(uv, dx, dy, v3, a3, n3, e3);
                // sharp weights: one cell dominates, so overlapping rotated copies never make thin criss-cross veins
                float3 hw = pow(max(w * (float3(a1.a, a2.a, a3.a) * 1.6 + 0.3), 1e-4), 7.0); hw /= hw.x + hw.y + hw.z;
                float3 alb = (a1.rgb * hw.x + a2.rgb * hw.y + a3.rgb * hw.z) * _Tint.rgb;
                float3 nrm = n1 * hw.x + n2 * hw.y + n3 * hw.z;
                float3 em = e1 * hw.x + e2 * hw.y + e3 * hw.z;
                float breathe = 1.0 + _Breathe * (UWNoise(ws.xz * 0.04 + t * 0.15) - 0.5) * 2.0;
                float2 tn = nrm.xy * _NrmStr;
                float3 N = normalize(float3(tn.x, sqrt(saturate(1.0 - dot(tn, tn))), tn.y));

                InputData d = (InputData)0;
                d.positionWS = ws; d.positionCS = i.positionCS; d.normalWS = N;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(ws);
                d.shadowCoord = float4(0, 0, 0, 1);   // never shadowed by the plaza above
                d.fogCoord = i.fog; d.bakedGI = SampleSH(N);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = alb; s.smoothness = saturate(1.0 - nrm.z) * 0.4; s.occlusion = 1;
                s.emission = em * _EmitCol.rgb * _EmitInt * breathe; s.alpha = 1; s.normalTS = float3(0, 0, 1);
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
