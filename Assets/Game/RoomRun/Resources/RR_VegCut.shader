// Cut map update (VegetationCut): R = how cut the blades are (>1 holds them cut). Decays linearly; sectors stamped in.
Shader "Hidden/RoomRun/VegCut"
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
            float4 _Rect;          // x0, z0, size
            float4 _StampA[8];     // x, z, radius, strength (0 = none)
            float4 _StampB[8];     // dir x, dir z, cos(half angle)
            float _Decay;

            float4 frag(v2f_img i) : SV_Target
            {
                float r = max(tex2D(_MainTex, i.uv).r - _Decay, 0);
                float2 p = _Rect.xy + i.uv * _Rect.z;
                for (int k = 0; k < 8; k++)
                {
                    float4 s = _StampA[k];
                    if (s.w <= 0) continue;
                    float2 d = p - s.xy;
                    float dist = length(d);
                    float inR = 1 - smoothstep(s.z * 0.88, s.z, dist);
                    float c = dist > 0.001 ? dot(d / dist, _StampB[k].xy) : 1;
                    float inA = smoothstep(_StampB[k].z - 0.08, _StampB[k].z + 0.04, c);
                    // ragged edge so the cut line is not a perfect arc
                    float n = frac(sin(dot(floor(p * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                    float v = inR * inA * s.w * (0.8 + 0.2 * n);
                    r = max(r, v);
                }
                return float4(r, 0, 0, 1);
            }
            ENDCG
        }
    }
}
