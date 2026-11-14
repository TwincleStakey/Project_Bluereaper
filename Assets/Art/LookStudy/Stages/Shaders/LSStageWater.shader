// River surface. Colour comes from real scene depth, refraction from the opaque texture, flow from a
// per-vertex direction (RG = direction, B = speed). Global ripple and wisp arrays add splashes and reflections.
Shader "LookStudy/StageWater"
{
    Properties
    {
        _Shallow("Shallow", Color) = (0.24, 0.40, 0.36, 1)
        _Mid("Mid (emerald)", Color) = (0.07, 0.26, 0.23, 1)
        _Deep("Deep", Color) = (0.02, 0.07, 0.08, 1)
        _Foam("Foam", Color) = (0.72, 0.80, 0.76, 1)
        _Absorb("Depth scale", Float) = 1.1
        _Refract("Refraction", Float) = 0.04
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "LSStage.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Shallow;
            float4 _Mid;
            float4 _Deep;
            float4 _Foam;
            float _Absorb;
            float _Refract;
            CBUFFER_END
            float4 _LSRipples[8];   // xz position, age seconds, strength
            float4 _LSWisps[6];     // xyz position, w strength
            float3 _LSWispCol;

            struct Attr { float4 positionOS : POSITION; float4 color : COLOR; };
            struct V2F { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screen : TEXCOORD1; float4 flow : TEXCOORD2; };

            V2F vert(Attr a)
            {
                V2F o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.flow = float4((a.color.rg - 0.5) * 2.0, a.color.b, a.color.a);
                return o;
            }

            float Height(float2 p)
            {
                return LSNoise(p * 1.7) * 0.55 + LSNoise(p * 3.9 + 7.0) * 0.3 + LSNoise(p * 8.1 + 3.0) * 0.15;
            }

            // Two phases of advected noise blended so flow never visibly resets.
            float FlowHeight(float2 xz, float2 dir, float speed)
            {
                float t = _LSTime * 0.3;   // 11-14 Kido: the middle ran far too fast for a mountain pool
                float a = frac(t), b = frac(t + 0.5);
                float w = abs(1.0 - 2.0 * a);
                float2 adv = dir * speed * 0.9;
                // 11-13 fix (Kido: streaks blinked on and off): each phase must be invisible at the moment it jumps
                // back (a = 0 shows only B, b = 0 shows only A); the weights were swapped, so the visible layer reset
                // twice a cycle. The mix is also renormalised so its contrast (and the foam thresholds on it) stays even.
                float m = lerp(Height(xz - adv * a), Height(xz - adv * b + 3.7), w);
                return (m - 0.5) * rsqrt(w * w + (1.0 - w) * (1.0 - w)) + 0.5;
            }

            half4 frag(V2F i) : SV_Target
            {
                float3 ws = i.positionWS;
                float2 uv = i.screen.xy / i.screen.w;
                float2 dir = i.flow.xy;
                float speed = i.flow.z;

                float e = 0.09;
                float h0 = FlowHeight(ws.xz, dir, speed);
                float hx = FlowHeight(ws.xz + float2(e, 0), dir, speed);
                float hz = FlowHeight(ws.xz + float2(0, e), dir, speed);
                float amp = lerp(0.04, 0.11, speed);
                float2 slope = float2(hx - h0, hz - h0) / e * amp;

                // Expanding rings from footsteps.
                float rings = 0.0;
                for (int k = 0; k < 8; k++)
                {
                    float4 r = _LSRipples[k];
                    if (r.w <= 0.0) continue;
                    float2 d = ws.xz - r.xy;
                    float dist = length(d);
                    float front = r.z * 2.2;
                    float env = saturate(1.0 - r.z / 1.6) * r.w * smoothstep(0.5, 0.0, abs(dist - front));
                    float wave = sin((dist - front) * 15.0) * env;
                    if (dist > 0.001) slope += d / dist * wave * 0.65;
                    rings += env * smoothstep(0.1, 0.0, abs(dist - front)) ;
                }
                float3 n = normalize(float3(-slope.x, 1.0, -slope.y));

                // Refracted scene behind the surface, tinted by true depth.
                float2 ruv = uv + slope * _Refract;
                float rawR = SampleSceneDepth(ruv);
                float eye = i.screen.w;
                float depthR = LinearEyeDepth(rawR, _ZBufferParams) - eye;
                if (depthR < 0.0) { ruv = uv; rawR = SampleSceneDepth(uv); depthR = LinearEyeDepth(rawR, _ZBufferParams) - eye; }
                float raw = SampleSceneDepth(uv);
                float depth = max(LinearEyeDepth(raw, _ZBufferParams) - eye, 0.0);
                float vy = saturate(normalize(_WorldSpaceCameraPos - ws).y);
                float d = depth * vy * _Absorb;

                float3 scene = SampleSceneColor(ruv);
                float3 tint = lerp(_Shallow.rgb, _Mid.rgb, smoothstep(0.1, 0.8, d));
                tint = lerp(tint, _Deep.rgb, smoothstep(0.7, 2.2, d));
                float clearness = exp(-d * 2.0);
                float3 ambient = _LSShadowMul + _LSSunCol * 0.35;
                scene *= lerp(0.72, 0.42, saturate(d * 3.0));
                float3 col = lerp(tint * ambient * 0.8, scene, clearness);

                float3 v = normalize(_WorldSpaceCameraPos - ws);
                float fres = 0.03 + 0.97 * pow(1.0 - saturate(dot(n, v)), 5.0);
                col = lerp(col, LSSky(reflect(-v, n)), fres * 0.45);

                float shadow = LSShadow(ws);
                float3 hv = normalize(_MainLightPosition.xyz + v);
                col += _LSSunCol * pow(saturate(dot(n, hv)), 700.0) * 0.45 * shadow;

                // Point lights glance off the surface, wisps also leave long vertical reflections.
                InputData inputData = (InputData)0;
                inputData.positionWS = ws;
                inputData.normalizedScreenSpaceUV = uv;
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light add = GetAdditionalLight(lightIndex, ws, half4(1, 1, 1, 1));
                    float3 ah = normalize(add.direction + v);
                    col += add.color * add.distanceAttenuation * (pow(saturate(dot(n, ah)), 220.0) * 0.7 + 0.018);
                LIGHT_LOOP_END
                for (int k = 0; k < 6; k++)
                {
                    float4 wp = _LSWisps[k];
                    if (wp.w <= 0.0) continue;
                    float2 q = ws.xz + slope * 0.45 - wp.xz;
                    q.y *= 0.3;
                    col += _LSWispCol * wp.w * exp(-dot(q, q) * 1.5) * 0.65;
                }

                // Shore and rock foam from depth intersection, broken by flowing noise.
                float fn = FlowHeight(ws.xz * 2.1, dir, speed * 1.4);
                float edge = smoothstep(0.1, 0.0, depth * vy + (fn - 0.5) * 0.07);
                float foam = edge * smoothstep(0.32, 0.62, fn + edge * 0.3);
                float rapids = speed * smoothstep(0.66, 0.86, FlowHeight(float2(dot(ws.xz, dir) * 0.45, dot(ws.xz, float2(-dir.y, dir.x)) * 2.4) + 11.0, float2(1, 0), speed * 1.6)) * 0.3;
                // 11-14 Kido: real valley water has no white band along the banks and no white rapid streaks: only a faint
                // broken foam right at rocks and the shore, plus the footstep rings
                foam = saturate(foam * 0.18 + rapids * 0.0 + rings * 0.9);
                col = lerp(col, _Foam.rgb * (ambient * 0.8 + 0.04), foam * 0.6);

                col = StageFog(col, ws);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
