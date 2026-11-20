// Impact crater decal on a flat ground quad, built from a CC0 cracked-concrete scan.
// Pass 1 multiplies the floor underneath by a relit shading factor (works on any floor texture):
//   lighting with the crater normal / lighting with a flat normal, times fissure + scorch darkening.
// Pass 2 adds a short dokkaebi-fire glow inside the fissures right after the hit.
// Mask: R fissures, G scorch, B coverage. Normal: tangent space (u = object X, v = object Z).
Shader "Dokkaebi/CraterDecal"
{
    Properties
    {
        _Mask ("Mask (R fissure, G scorch, B coverage)", 2D) = "black" {}
        [Normal] _Normal ("Normal", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
        _FissureDark ("Fissure Darkness", Range(0,1)) = 0.75
        _ScorchDark ("Scorch Darkness", Range(0,1)) = 0.3
        [HDR] _GlowColor ("Glow", Color) = (0.15,0.7,1.8,1)
        _Glow ("Glow Amount", Range(0,1)) = 0
        _Fade ("Fade", Range(0,1)) = 1
        _Reveal ("Reveal", Range(0,1.5)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Off
        Offset -1, -1

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
        TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
        CBUFFER_START(UnityPerMaterial)
        float4 _Mask_ST; float _NormalStrength; float _FissureDark; float _ScorchDark; half4 _GlowColor; float _Glow; float _Fade; float _Reveal;
        CBUFFER_END
        struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
        struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 tx : TEXCOORD1; float3 tz : TEXCOORD2; float3 ny : TEXCOORD3; };
        V vert (A i)
        {
            V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv;
            o.tx = normalize(TransformObjectToWorldDir(float3(1,0,0)));
            o.tz = normalize(TransformObjectToWorldDir(float3(0,0,1)));
            o.ny = normalize(TransformObjectToWorldDir(float3(0,1,0)));
            return o;
        }
        float RevealMask(float2 uv)
        {
            float r = length(uv * 2 - 1);
            return 1 - smoothstep(_Reveal - 0.12, _Reveal, r);
        }
        ENDHLSL

        Pass
        {
            Name "CraterShade"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (V i) : SV_Target
            {
                half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uv);
                float3 nT = UnpackNormal(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, i.uv));
                nT.xy *= _NormalStrength;
                float3 n = normalize(nT.x * i.tx + nT.y * i.tz + nT.z * i.ny);
                Light L = GetMainLight();
                half3 amb = SampleSH(n);
                half3 ambFlat = SampleSH(i.ny);
                half3 lit = amb + L.color * saturate(dot(n, L.direction));
                half3 litFlat = ambFlat + L.color * saturate(dot(i.ny, L.direction));
                half3 relight = lit / max(litFlat, 0.05);
                float rev = RevealMask(i.uv);
                half dark = (1 - m.r * _FissureDark * rev) * (1 - m.g * _ScorchDark);
                half3 f = relight * dark;
                half k = m.b * _Fade;
                return half4(lerp(1.0.xxx, f, k), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "CraterGlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (V i) : SV_Target
            {
                half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uv);
                float r = length(i.uv * 2 - 1);
                float g = m.r * RevealMask(i.uv) * _Glow * saturate(1.1 - r * 1.3) * _Fade;
                return half4(_GlowColor.rgb * g, 1);
            }
            ENDHLSL
        }
    }
}
