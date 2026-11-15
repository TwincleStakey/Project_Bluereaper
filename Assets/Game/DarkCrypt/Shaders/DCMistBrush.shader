// DarkCrypt mist mask update (DCMist, one blit per frame into a ping-pong RHalf mask over the arena).
// r = how cleared the mist is (0 mist .. ~1.3 cleared). The player wipes a blotchy capsule from last frame's position
// to this one (world-fixed low-frequency noise, so the soft blotch shapes stay put);
// everything regrows unevenly over _BrushParams.z per second.
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
            float4 _Rect;         // x0, z0, width, depth
            float4 _P0, _P1;      // xz of the player last frame / now
            float4 _BrushParams;  // radius, clear amount this frame, regrow amount this frame, time

            float hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return vnoise(p) * 0.55 + vnoise(p * 2.13 + 17.1) * 0.3 + vnoise(p * 4.7 + 3.3) * 0.15; }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 w = _Rect.xy + i.uv * _Rect.zw;
                float prev = tex2D(_Prev, i.uv).r;
                // uneven regrowth: some spots fill back in faster, so the cleared patch breaks up as it closes
                float grow = _BrushParams.z * (0.55 + 0.9 * fbm(w * 0.33 + 7.0));
                float m = max(0, prev - grow);

                // distance to the segment walked this frame
                float2 a = _P0.xy, b = _P1.xy, ab = b - a;
                float t = saturate(dot(w - a, ab) / max(dot(ab, ab), 1e-5));
                float d = length(w - (a + ab * t));

                // v3: soft blotchy outline from low-frequency world-fixed noise, wide falloff, no droplets/flecks
                float r = _BrushParams.x * (0.75 + 0.55 * fbm(w * 0.32));
                float core = smoothstep(r, r * 0.35, d);
                // grows toward the soft profile (core * 1.3), never past it: the cleared patch keeps a wide feathered rim
                m = max(m, min(m + core * _BrushParams.y, core * 1.3));
                return float4(m, 0, 0, 1);
            }
            ENDCG
        }
    }
}
