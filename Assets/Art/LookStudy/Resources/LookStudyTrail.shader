// Trail map update: R = how flat the grass is, G = footprint depth, BA = push direction.
// Each frame the previous map fades and the player's disc and new footprints are stamped in.
Shader "Hidden/LookStudy/Trail"
{
    Properties { _MainTex("Prev", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Rect;      // x0, z0, size, unused
            float4 _Player;    // x, z, radius, strength
            float4 _Feet[4];   // x, z, yaw radians, strength (0 = none)
            float4 _Decay;     // x: grass recovery per frame (subtracted, as in the original GrassTrample), y: footprint multiplier

            float Foot(float2 p, float4 f)
            {
                if (f.w <= 0) return 0;
                float2 d = p - f.xy;
                float c = cos(f.z), s = sin(f.z);
                float2 q = float2(d.x * c - d.y * s, d.x * s + d.y * c); // q.y along the foot
                float sole = 1 - smoothstep(0.75, 1.0, length(float2(q.x / 0.065, (q.y + 0.035) / 0.13)));
                float toes = 1 - smoothstep(0.7, 1.0, length(float2(q.x / 0.058, (q.y - 0.105) / 0.055)));
                return saturate(max(sole, toes)) * f.w;
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float4 prev = tex2D(_MainTex, i.uv);
                float2 p = _Rect.xy + i.uv * _Rect.z;
                // Linear recovery like the original: flattened grass stays down and rises evenly over ~7 s.
                // (The old exponential fade halved the trail within a second, so it was hardly visible.)
                float r = max(prev.r - _Decay.x, 0);
                float g = prev.g * _Decay.y;
                float2 dir = prev.ba * 2 - 1;

                float2 d = p - _Player.xy;
                float dist = length(d);
                float disc = saturate(1 - dist / _Player.z) * _Player.w;
                disc = smoothstep(0, 0.6, disc);
                if (disc > r)
                {
                    float2 nd = dist > 0.001 ? d / dist : float2(0, 1);
                    dir = normalize(lerp(dir, nd, saturate(disc - r + 0.3)));
                    r = disc;
                }
                for (int k = 0; k < 4; k++) g = max(g, Foot(p, _Feet[k]));
                return float4(r, g, dir * 0.5 + 0.5);
            }
            ENDCG
        }
    }
}
