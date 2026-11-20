// DarkCrypt mist mask update (DCMist, one blit per frame into a ping-pong RHalf mask over the arena).
// r = how cleared the mist is (0 mist .. ~1.3 cleared).
// v4 (11-15): the player no longer wipes a disc around the feet. DCMist drops soft blotch "stamps" along the path walked
// (spaced, jittered sideways, random size and outline seed); each stamp eats into the mist gradually over ~0.8 s, so the
// newest blotch under the feet is only half cleared and the trail reads behind the player. Standing still adds nothing.
// Everything regrows unevenly over _BrushParams.z per second.
Shader "Hidden/DarkCrypt/MistBrush"
{
    Properties { _Prev ("Prev", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Prev;
            float4 _Rect;          // x0, z0, width, depth
            float4 _BrushParams;   // unused x, unused y, regrow amount this frame, time
            float4 _Stamps[8];     // x, z, radius, clear amount this frame (0 = inactive)
            float4 _StampSeed[8];  // x = outline noise seed

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return vnoise(p) * 0.6 + vnoise(p * 2.13 + 17.1) * 0.3 + vnoise(p * 4.1 + 3.3) * 0.1; }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 w = _Rect.xy + i.uv * _Rect.zw;
                float prev = tex2D(_Prev, i.uv).r;
                // uneven regrowth: some spots fill back in faster, so the cleared patches break up as they close
                float grow = _BrushParams.z * (0.5 + 1.0 * fbm(w * 0.29 + 7.0));
                float m = max(0, prev - grow);
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 s = _Stamps[k];
                    if (s.w <= 0) continue;
                    float2 dv = w - s.xy;
                    // soft blotch: outline wobble from low-frequency noise of the direction (seeded per stamp)
                    float ang = atan2(dv.y, dv.x);
                    float wob = fbm(float2(cos(ang), sin(ang)) * 1.3 + _StampSeed[k].x) - 0.5;
                    float r = s.z * (0.9 + 0.75 * wob + 0.35 * (fbm(w * 0.4 + _StampSeed[k].x * 3.1) - 0.5));
                    float core = smoothstep(r, r * 0.25, length(dv));
                    m = max(m, min(m + core * s.w, core * 1.3));
                }
                return float4(m, 0, 0, 1);
            }
            ENDCG
        }
    }
}
