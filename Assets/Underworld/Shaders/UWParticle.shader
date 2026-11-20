Shader "Underworld/Particle"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        [HDR] _Tint("Tint", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _Src("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst("Dst", Float) = 1
        _SoftFade("Soft fade (m)", Float) = 0.5
        _AlphaFromRed("Alpha from red channel", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_Src] [_Dst]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UWCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint; float _Src, _Dst, _SoftFade, _AlphaFromRed;
            CBUFFER_END
            struct A { float4 p : POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float4 c : COLOR; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            V vert(A a) { V o; o.cs = TransformObjectToHClip(a.p.xyz); o.c = a.c; o.uv = a.uv; o.sp = ComputeScreenPos(o.cs); return o; }
            half4 frag(V i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                t.a = lerp(t.a, t.r, _AlphaFromRed);
                float2 suv = i.sp.xy / i.sp.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragZ = LinearEyeDepth(i.cs.z, _ZBufferParams);
                float soft = saturate((sceneZ - fragZ) / max(_SoftFade, 0.001));
                float4 c = t * i.c * _Tint;
                c.a *= soft;
                // additive (Dst == One): premultiply so alpha drives brightness
                if (_Dst == 1) c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
}
