// 심연 고원 (11-19): everything that hangs in the abyss - the plateau's sheer cliff faces, the rock spires, the dead
// roots. Downloaded albedo/normal (Poly Haven CC0) desaturated to a cool dark grey, a dim cool key from the main
// light, then the KillKnight look by world height:
//   - underglow: faces turned down / out pick up red-orange light from the deep (stronger the deeper they hang)
//   - depth haze: the deeper a point is, the more it melts into the glow colour (crimson-black high up, hot orange low)
// No realtime lights needed for the red; one shader, cheap.
Shader "KillKnight/AbyssRock"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "grey" {}
        _BumpMap("Normal GL", 2D) = "bump" {}
        _Tint("Rock tint (linear)", Color) = (0.13, 0.13, 0.145, 1)
        _Silhouette("Silhouette: 0 rock, 1 near-black (roots)", Range(0, 1)) = 0
        _Key("Key light scale", Range(0, 2)) = 0.8
        _Amb("Ambient scale", Range(0, 6)) = 0.6
        _Fill("Flat fill (linear)", Color) = (0, 0, 0, 1)
        _Band("Light band from below (HDR, linear)", Color) = (0, 0, 0, 1)
        _BandY("Band: centre y, width up, width down, 0", Vector) = (-3, 3, 30, 0)
        _Glow("Underglow colour (HDR)", Color) = (1.2, 0.3, 0.18, 1)
        _GlowY("Underglow: y start (none above), y full, facing power, rim", Vector) = (-20, -260, 1.5, 0.6)
        _HazeHi("Haze colour high (crimson)", Color) = (0.48, 0.08, 0.11, 1)
        _HazeLo("Haze colour deep (ember red)", Color) = (1.4, 0.36, 0.26, 1)
        _HazeY("Haze: y start, y full, max amount, curve", Vector) = (-15, -300, 0.9, 0.8)
        _HazeD("Aerial haze: start m from camera, full m, max", Vector) = (45, 330, 0.85, 0)
        _NrmStr("Normal strength", Range(0, 2)) = 1
        _Tri("Planar map scale (0 = mesh uv)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST, _Tint, _Fill, _Band, _BandY, _HazeD, _Glow, _GlowY, _HazeHi, _HazeLo, _HazeY;
        float _Silhouette, _Key, _Amb, _NrmStr, _Tri;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct A { float4 p : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 cs : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float4 t : TEXCOORD2; float2 uv : TEXCOORD3; };
            V vert(A a)
            {
                V o; o.ws = TransformObjectToWorld(a.p.xyz); o.cs = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(a.n); o.t = float4(TransformObjectToWorldDir(a.t.xyz), a.t.w);
                o.uv = TRANSFORM_TEX(a.uv, _BaseMap); return o;
            }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            half4 frag(V i) : SV_Target
            {
                float3 N = normalize(i.n);
                float3 alb; float3 n = N;
                if (_Tri > 0)
                {
                    // planar on the dominant horizontal axis (cliff skirts: no stretching on the vertical faces)
                    float2 uv = (abs(N.x) > abs(N.z) ? i.ws.zy : i.ws.xy) / _Tri;
                    alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                }
                else
                {
                    alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                    float3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _NrmStr);
                    float3 T = normalize(i.t.xyz); float3 B = cross(N, T) * (i.t.w > 0 ? 1 : -1);
                    n = normalize(T * nt.x + B * nt.y + N * nt.z);
                }
                float l = Luma(alb);
                float3 a = _Tint.rgb * (0.35 + 1.3 * l) * (1 - _Silhouette * 0.85);

                Light ml = GetMainLight();
                float ndl = saturate(dot(n, ml.direction));
                float3 lit = a * (ml.color * ndl * _Key + SampleSH(n) * _Amb + _Fill.rgb);

                // underglow from the deep
                float y = i.ws.y;
                float depthG = saturate((y - _GlowY.x) / (_GlowY.y - _GlowY.x));
                float down = pow(saturate(-n.y * 0.7 + 0.3), _GlowY.z);
                float3 V3 = GetWorldSpaceNormalizeViewDir(i.ws);
                float rim = pow(1 - saturate(dot(n, V3)), 3) * _GlowY.w;
                float glowAmt = (0.15 + 0.85 * depthG) * (down + rim) * smoothstep(_GlowY.x + 1.5, _GlowY.x - 3, y);
                lit += _Glow.rgb * glowAmt * (0.25 + l) * (1 - _Silhouette * 0.6);
                // v5: the rim band (cliff walls): strongest just under the lip, fading up to the edge and down the wall
                float by = y - _BandY.x; float bw = by > 0 ? _BandY.y : _BandY.z;
                lit += _Band.rgb * exp(-(by * by) / max(0.01, bw * bw)) * (0.35 + l) * saturate(0.4 + 0.6 * dot(n, V3));

                // depth haze
                float hz = pow(saturate((y - _HazeY.x) / (_HazeY.y - _HazeY.x)), _HazeY.w);
                float3 hcol = lerp(_HazeHi.rgb, _HazeLo.rgb, hz * hz);
                // aerial perspective: far things fade into the lighter haze (v3, ref: silhouettes at many depths)
                float hd = saturate((distance(_WorldSpaceCameraPos, i.ws) - _HazeD.x) / (_HazeD.y - _HazeD.x)) * _HazeD.z;
                float hAll = 1 - (1 - hz * _HazeY.z) * (1 - hd * smoothstep(-3, -20, y));
                hcol = lerp(_HazeHi.rgb, _HazeLo.rgb, saturate(hz * hz + hd * 0.35));
                lit = lerp(lit, hcol, hAll);
                return half4(lit, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct DA { float4 p : POSITION; float3 n : NORMAL; };
            struct DV { float4 cs : SV_POSITION; float3 n : TEXCOORD0; };
            DV vert(DA a) { DV o; o.cs = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 frag(DV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
}
