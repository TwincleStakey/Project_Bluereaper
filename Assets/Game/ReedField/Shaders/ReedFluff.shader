// Reed fluff and dry leaf slivers drifting in the air (particle system). Soft alpha-blended oval, tinted by the
// particle colour, a little brighter when the low sun is behind it.
Shader "ReedField/Fluff"
{
    Properties
    {
        _Color("Color", Color) = (1, 0.86, 0.62, 0.7)
        _Backlight("Backlight", Range(0, 3)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Backlight; CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float3 ws : TEXCOORD1; };
            V2F vert(Attr a) { V2F o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws); o.uv = a.uv; o.color = a.color; return o; }
            half4 frag(V2F i) : SV_Target
            {
                float d = length((i.uv - 0.5) * 2);
                float m = saturate(1 - d);
                m = m * m * (3 - 2 * m);
                Light ml = GetMainLight();
                float3 v = normalize(_WorldSpaceCameraPos - i.ws);
                float back = pow(saturate(dot(-v, ml.direction) * 0.5 + 0.5), 3);
                float3 col = _Color.rgb * i.color.rgb * (0.55 + ml.color * (0.4 + back * _Backlight));
                return half4(col, m * _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
