// 11-15: verbatim copy of the ORIGINAL DarkCrypt v2 mist brush (recovered from the 11-14 session log), used by the 저승 film.
// DarkCrypt mist mask update (DCMist, one blit per frame into a ping-pong RHalf mask over the arena).
// r = how cleared the mist is (0 mist .. ~1.3 cleared). The player wipes a blotchy capsule from last frame's position
// to this one (world-fixed noise, so the splotch shapes stay put like spilled paint, with droplets around the rim);
// everything regrows unevenly over _BrushParams.z per second.
Shader "Hidden/Underworld/CorruptFilmBrush"
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

                float r = _BrushParams.x * (0.7 + 0.6 * fbm(w * 0.85));            // ragged, world-fixed outline
                float core = saturate((r - d) / (r * 0.4));
                // droplets in a ring past the main patch (paint splatter)
                float spot = step(0.74, vnoise(w * 2.6 + 11.0)) * saturate((r * 1.8 - d) / (r * 0.45)) * step(r * 0.9, d);
                m = min(1.3, m + (core + spot * 0.8) * _BrushParams.y);
                return float4(m, 0, 0, 1);
            }
            ENDCG
        }
    }
}
