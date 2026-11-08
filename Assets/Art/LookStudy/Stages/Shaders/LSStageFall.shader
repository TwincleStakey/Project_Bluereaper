// Waterfall ribbon: stretched streaks panning along the mesh UV, whiter toward the churning base.
Shader "LookStudy/StageFall"
{
    Properties { _Col("Water", Color) = (0.62, 0.74, 0.72, 1) _Speed("Speed", Float) = 1.3 }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "LSStage.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Col;
            float _Speed;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float t = _LSTime * _Speed;
                float2 q = float2(i.uv.x * 16.0, i.uv.y * 2.2 + t);
                float a = LSFbm(q) ;
                float b = LSFbm(float2(i.uv.x * 34.0 + 5.0, i.uv.y * 3.4 + t * 1.5));
                float streak = smoothstep(0.34, 0.76, a * 0.65 + b * 0.5);
                float edge = smoothstep(0.0, 0.14, i.uv.x) * smoothstep(1.0, 0.86, i.uv.x);
                float alpha = saturate(streak + 0.25) * edge;
                float base = smoothstep(0.75, 1.0, i.uv.y);
                float3 col = _Col.rgb * (_LSShadowMul * 1.5 + _LSSunCol * 0.55) * (0.65 + streak * 0.7 + base * 0.8);
                col = StageFog(col, i.ws);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
