// LookFX (Kido 11-11, POE2-style light on shots): additive glow cards, GPU instanced.
//   _Streak 0: a soft round pool of light lying on the ground under a bullet (fake light, any number of bullets);
//   _Streak 1: a tapered streak lying along the bullet's path, bright at the head, fading to the tail;
//   _Streak 2: a texture (white-on-black sprite, Kenney Particle Pack CC0), e.g. a halo or core;
//   _Streak 3: the same with u and v swapped (a horizontal trail sprite laid along the card's length).
// Adds light only, so it brightens whatever it lies on and blooms where it is strong.
Shader "RoomRun/AddGlow"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1, 0.5, 0.2, 1)
        _MainTex("Sprite (modes 2, 3)", 2D) = "white" {}
        _Streak("Mode", Float) = 0
        _Soft("Falloff power", Range(0.5, 6)) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; float4 _MainTex_ST; float _Streak, _Soft;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o; o.pos = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 c = i.uv * 2 - 1;
                float k;
                if (_Streak > 1.5)
                {
                    float2 uv = _Streak > 2.5 ? i.uv.yx : i.uv;
                    float4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                    k = max(t.r, max(t.g, t.b)) * t.a;
                    return half4(_Color.rgb * pow(k, _Soft), 1);
                }
                if (_Streak > 0.5)
                {
                    // uv.y: 1 at the head, 0 at the tail; narrow across, tapering toward the tail
                    float along = saturate(i.uv.y);
                    float width = lerp(0.15, 1.0, along);
                    k = saturate(1 - abs(c.x) / width) * pow(along, 1.6);
                }
                else k = saturate(1 - length(c));
                k = pow(k, _Soft);
                return half4(_Color.rgb * k, 1);
            }
            ENDHLSL
        }
    }
}
