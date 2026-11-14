// Cascade sheet (11-14, after Diablo IV top-down waterfalls): a wide, soft, translucent curtain of long vertical streaks
// sliding down, brighter at the lip where it rolls over and whitest in the churn at the foot; gaps between streaks let the
// dark rock show through. uv.x runs across the sheet in metres, uv.y from the lip (0) to the foot (1).
Shader "LookStudy/StageCascade"
{
    Properties
    {
        _Col("Water", Color) = (0.62, 0.72, 0.72, 1)
        _Foam("Foam", Color) = (0.86, 0.9, 0.9, 1)
        _Speed("Fall speed", Float) = 0.9
        _Alpha("Opacity", Float) = 0.85
        _Glow("Glow in the dark (ambient-independent)", Float) = 0.12
    }
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
            float4 _Col; float4 _Foam; float _Speed; float _Alpha; float _Glow;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; float4 c : COLOR; };
            struct V { float4 cs : SV_POSITION; float2 uv : TEXCOORD0; float3 ws : TEXCOORD1; float4 c : TEXCOORD2; };
            V vert(A a) { V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws); o.uv = a.uv; o.c = a.c; return o; }
            half4 frag(V i) : SV_Target
            {
                float t = _LSTime * _Speed;
                float x = i.uv.x, y = i.uv.y;
                // long streaks: fine across, very stretched down, two layers at different speeds
                float s1 = LSFbm(float2(x * 3.2, y * 0.55 - t * 1.0));
                float s2 = LSFbm(float2(x * 7.5 + 13.0, y * 1.1 - t * 1.45));
                float streak = saturate(s1 * 0.6 + s2 * 0.55);
                float body = smoothstep(0.30, 0.78, streak);
                // the lip rolls over smooth and glassy, then breaks up; the foot churns white
                float lip = 1.0 - smoothstep(0.0, 0.22, y);
                float foot = smoothstep(0.62, 1.0, y);
                float churn = foot * smoothstep(0.35, 0.7, LSFbm(float2(x * 2.4, y * 3.0 - t * 2.2) + 5.0));
                // r6: sides fade over a ragged, moving edge; the curtain splits into a few strands with see-through gaps
                float en = (LSFbm(float2(x * 0.9 + 2.0, y * 2.0 - t * 0.8)) - 0.5) * 0.6;
                float edge = smoothstep(0.0, 0.5, i.c.r + en) * smoothstep(0.0, 0.35, i.c.g);
                float strands = lerp(0.4, 1.0, smoothstep(0.32, 0.6, LSFbm(float2(x * 0.65 + 7.0, y * 0.2 - t * 0.05))));
                // r2 (review): alpha breaks up so dark rock shows between streaks, ragged bottom edge, uneven brightness across
                float rag = smoothstep(1.0, 0.86 + (LSFbm(float2(x * 1.7, t * 0.7)) - 0.5) * 0.18, y);
                float alpha = saturate(lerp(0.12 + body * 0.88, 0.7, lip * 0.45) + churn * 0.5) * edge * rag * strands * _Alpha;
                float bands = 0.75 + 0.5 * LSFbm(float2(x * 0.55 + 3.0, 0.5));
                // foam lying on the pool (vertex blue > 0): patchy and see-through, never a solid white disc
                if (i.c.b > 0.0) { float patch = smoothstep(0.42, 0.75, LSFbm(i.ws.xz * 1.3 + float2(t * 0.25, -t * 0.18))); alpha = i.c.b * (0.25 + patch * 0.75) * _Alpha; }
                float3 light = _LSShadowMul * 1.6 + _LSSunCol * 0.5;
                float3 col = lerp(_Col.rgb, _Foam.rgb, saturate(body * 0.55 + churn + lip * 0.15)) * (light + _Glow) * bands;
                col = StageFog(col, i.ws);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
