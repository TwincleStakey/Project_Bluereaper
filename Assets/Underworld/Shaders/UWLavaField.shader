// 저승 lava sea (11-14): surface comes entirely from the downloaded "Lava Texture & Material Vol. 04" maps (Fab, CC BY):
// dark crust albedo + its own emissive map for the molten seams. Nothing is drawn procedurally; the shader only
//   - samples the set at two scales / rotations and blends them with a soft low-frequency mask so the 300 m plane
//     shows no tile grid,
//   - slides the second layer slowly (lava drifts), and breathes the emission a little,
//   - darkens the stylised brown albedo towards basalt black so the glow carries the image.
Shader "Underworld/LavaField"
{
    Properties
    {
        _BaseMap("Albedo (Lava04 basecolor)", 2D) = "grey" {}
        _EmisMap("Emissive (Lava04 emissive)", 2D) = "black" {}
        [Normal] _NormalMap("Normal (Lava04 normal)", 2D) = "bump" {}
        _HeightMap("Height (Lava04 height)", 2D) = "grey" {}
        [HDR] _MoltenCol("Molten glow in the low gaps (height map)", Color) = (1, 0.25, 0.04, 1)
        _MoltenLo("Height below which it is molten", Range(0, 1)) = 0.35
        _Tile("Metres per tile", Float) = 7
        _AlbedoMul("Albedo multiplier", Range(0, 1)) = 0.18
        [HDR] _EmisTint("Emission tint", Color) = (1, 0.55, 0.3, 1)
        _EmisPow("Emission contrast", Range(0.5, 4)) = 1.6
        _EmisGain("Emission HDR gain", Range(0, 12)) = 3
        _Flow("Drift speed (m/s)", Float) = 0.12
        _Breathe("Emission breathing", Range(0, 1)) = 0.25
        _NoShadow("Ignore shadows", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UWCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); TEXTURE2D(_EmisMap); TEXTURE2D(_NormalMap); TEXTURE2D(_HeightMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _EmisTint, _MoltenCol; float _MoltenLo; float _Tile, _AlbedoMul, _EmisPow, _EmisGain, _Flow, _Breathe, _NoShadow;
            CBUFFER_END
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float fog : TEXCOORD1; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.fog = ComputeFogFactor(o.cs.z); return o; }

            struct S { float3 alb; float3 em; float3 n; float h; };
            S Sample(float2 uv)
            {
                S s;
                s.alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                s.em = SAMPLE_TEXTURE2D(_EmisMap, sampler_BaseMap, uv).rgb;
                s.n = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_BaseMap, uv));
                s.h = SAMPLE_TEXTURE2D(_HeightMap, sampler_BaseMap, uv).r;
                return s;
            }
            half4 frag(V i) : SV_Target
            {
                float2 p = i.ws.xz;
                float t = _UWTime;
                // layer A: plain; layer B: rotated 37 deg, 0.63x scale, drifting. Blend by a soft 40 m mask (no seams, no grid).
                float2 uvA = p / _Tile;
                float c = 0.7986, sn = 0.6018;
                float2 pb = float2(c * p.x - sn * p.y, sn * p.x + c * p.y) + float2(t, -0.6 * t) * _Flow;
                float2 uvB = pb / (_Tile * 1.6) + 0.37;
                S a = Sample(uvA), b = Sample(uvB);
                float w = smoothstep(0.3, 0.7, UWFbm(p * 0.025 + 3.0));
                float3 alb = lerp(a.alb, b.alb, w);
                float3 em = lerp(a.em, b.em, w);
                float3 tn = normalize(lerp(a.n, b.n, w));
                float h = lerp(a.h, b.h, w);
                float3 N = normalize(float3(tn.x, tn.z, tn.y));

                // basalt crust: desaturate the stylised brown and darken it
                float l = dot(alb, float3(0.3, 0.59, 0.11));
                alb = lerp(float3(l, l, l), alb, 0.35) * _AlbedoMul;
                Light mainL = GetMainLight();
                float3 lit = alb * (mainL.color * saturate(dot(N, mainL.direction)) * 0.6 + SampleSH(N));

                // emission from the downloaded map; contrast so only the seams glow, slow breathing rolling across the sea
                float e = dot(em, float3(0.33, 0.34, 0.33));
                float breathe = 1.0 + _Breathe * sin(t * 0.7 + UWNoise(p * 0.04) * 6.2831);
                float3 emis = em * pow(saturate(e), _EmisPow - 1.0) * _EmisTint.rgb * _EmisGain * breathe;

                // molten lava in the low gaps between the downloaded crust plates (from the set's own height map)
                float molten = 1.0 - smoothstep(_MoltenLo - 0.15, _MoltenLo + 0.1, h);
                emis += _MoltenCol.rgb * molten * breathe;
                lit *= 1.0 - molten;
                float3 col = lit + emis;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
