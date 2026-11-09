// Flat drifting fog sheets lying in the reeds. Alpha-blended noise that scrolls with the wind and fades at the card edge.
Shader "ReedField/FogCard"
{
    Properties
    {
        _Color("Fog", Color) = (0.32, 0.36, 0.44, 0.22)
        _Scale("Noise scale", Float) = 0.12
        _Speed("Drift speed", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "ReedCommon.hlsl"
            CBUFFER_START(UnityPerMaterial) float4 _Color; float _Scale, _Speed; CBUFFER_END
            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float2 uv : TEXCOORD1; };
            V2F vert(Attr a) { V2F o; o.ws = TransformObjectToWorld(a.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.ws); o.uv = a.uv; return o; }
            half4 frag(V2F i) : SV_Target
            {
                float2 drift = _RF_Wind.xy * _RF_Time * _Speed;
                float2 p = (i.ws.xz - drift) * _Scale;
                float n = RFNoise(p) * 0.6 + RFNoise(p * 2.7 + 3.1) * 0.3 + RFNoise(p * 6.1 - 1.7) * 0.1;
                float m = smoothstep(0.35, 0.8, n);
                float2 e = abs(i.uv - 0.5) * 2;
                float edge = (1 - smoothstep(0.55, 1.0, e.x)) * (1 - smoothstep(0.55, 1.0, e.y));
                return half4(_Color.rgb, _Color.a * m * edge);
            }
            ENDHLSL
        }
    }
}
