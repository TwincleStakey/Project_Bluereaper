// Greatsword blade trail (11-14 hack-and-slash feel pass): a thin additive ribbon swept by the blade between grip-side
// and tip. Vertex colour carries the fade (alpha = age and base-to-tip); uv.x = age (0 new, 1 old), uv.y = 0 base, 1 tip.
// Bright leading edge along the tip and at the newest part, soft and thin toward the base and the tail.
Shader "RoomRun/BladeTrail"
{
    Properties
    {
        [HDR] _Color("Colour", Color) = (1.1, 1.0, 0.9, 1)
        [HDR] _Edge("Tip edge colour", Color) = (1.6, 1.5, 1.35, 1)
        _EdgeWidth("Tip edge width", Range(0.01, 0.5)) = 0.12
        _BodyPow("Body falloff toward the base (low = thick band)", Range(0.3, 3)) = 1.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Trail"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Edge; float _EdgeWidth, _BodyPow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            V vert(A a)
            {
                V o; o.pos = TransformObjectToHClip(a.positionOS.xyz); o.uv = a.uv; o.color = a.color;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float age = saturate(i.uv.x), span = saturate(i.uv.y);
                // body: grows toward the tip, dies off with age
                float body = pow(span, _BodyPow) * pow(1 - age, 1.4);   // 11-18 thick trail: GreatswordCombat sets 0.7
                // edge: a thin bright line along the tip, shorter-lived than the body
                float edge = smoothstep(1 - _EdgeWidth, 1, span) * pow(1 - age, 2.5);
                float3 c = _Color.rgb * body + _Edge.rgb * edge;
                float a = saturate(body * 0.85 + edge) * i.color.a;
                return half4(c * i.color.rgb, a);
            }
            ENDHLSL
        }
    }
}
