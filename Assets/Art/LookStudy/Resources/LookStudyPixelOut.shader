// Draws the low-resolution frame on the UI. _Levels > 0 reduces colour steps per channel.
Shader "Hidden/LookStudy/PixelOut"
{
    Properties { _MainTex("Frame", 2D) = "black" {} _Levels("Levels", Float) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Levels;
            struct A { float4 v : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.p = UnityObjectToClipPos(a.v); o.uv = a.uv; return o; }
            fixed4 frag(V i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                if (_Levels > 0.5) c = floor(c * _Levels + 0.5) / _Levels;
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
