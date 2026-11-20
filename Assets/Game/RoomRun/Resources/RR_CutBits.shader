// Greatsword cut bits (RoomRun CutDebris): grass clippings, chaff, reed leaf bits and plume fluff, all one instanced
// blade mesh. _BitA.w picks how a bit is coloured and lit:
//   1 = LookStudy grass field: the field's own colour at the blade's root (LSFieldColor, the grass material's colours
//       are copied onto this material) and the field's sun/shadow/cloud/fog, so a clipping is the blade it was cut from.
//   2 = flat colour with the LookStudy lighting (bushes / foliage in LookStudy fields).
//   0 = flat colour with the URP main light + ambient + fog (reed field).
// Fading bits dissolve with a screen-door pattern (no transparency, no sorting).
Shader "RoomRun/CutBits"
{
    Properties
    {
        _ColA("Patch deep", Color) = (0.05, 0.42, 0.13, 1)
        _ColB("Patch mid", Color) = (0.22, 0.66, 0.16, 1)
        _ColC("Patch light", Color) = (0.62, 0.84, 0.22, 1)
        _RootDark("Root darkening", Range(0,1)) = 0.5
        _TipLift("Tip lift", Range(1,2)) = 1.32
        _BladeVar("Per blade variation", Range(0,0.6)) = 0.07
        _PatchContrast("Patch contrast", Range(0,5)) = 3.2
        _ClumpAmt("Clump amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Assets/Art/LookStudy/Shaders/LSCommon.hlsl"
        #include "Assets/Art/LookStudy/Shaders/LSField.hlsl"

        UNITY_INSTANCING_BUFFER_START(CutBits)
            UNITY_DEFINE_INSTANCED_PROP(float4, _BitA)   // root x, root z, rnd, mode
            UNITY_DEFINE_INSTANCED_PROP(float4, _BitB)   // colour rgb, dissolve
        UNITY_INSTANCING_BUFFER_END(CutBits)

        void BitDissolve(float4 positionCS, float fade)
        {
            if (fade <= 0) return;
            float n = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
            clip(n - fade);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            struct Attr { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float3 ws : TEXCOORD0;
                float3 nws : TEXCOORD1;
                float4 a : TEXCOORD2;
                float4 b : TEXCOORD3;
                float2 uv : TEXCOORD4;
                float fog : TEXCOORD5;
            };

            V2F vert(Attr i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V2F o;
                o.ws = TransformObjectToWorld(i.positionOS.xyz);
                o.nws = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.a = UNITY_ACCESS_INSTANCED_PROP(CutBits, _BitA);
                o.b = UNITY_ACCESS_INSTANCED_PROP(CutBits, _BitB);
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V2F i, bool front : SV_IsFrontFace) : SV_Target
            {
                BitDissolve(i.positionCS, i.b.w);
                float h = i.uv.y;   // along the piece, 0 = cut end
                float mode = i.a.w;
                float3 n = normalize(i.nws) * (front ? 1 : -1);
                float3 col;
                if (mode > 0.5 && mode < 1.5)
                    col = LSFieldColor(i.a.xy, lerp(0.5, 1.05, h), i.a.z);   // the blade above the stubble, out of the canopy shade
                else
                    col = i.b.rgb * lerp(0.8, 1.08, h);
                // the rounded blade: one edge a little brighter
                col *= 1.0 + (i.uv.x - 0.5) * 0.16;
                if (mode > 0.5)
                {
                    float light = LSShadow(i.ws) * LSCloud(i.ws.xz);
                    col *= lerp(_LSShadowMul, _LSSunCol, light);
                    // tumbling blades flicker as their faces turn to the sky
                    col *= 0.86 + 0.26 * abs(n.y);
                    col = LSFog(col, i.ws);
                }
                else
                {
                    Light l = GetMainLight(TransformWorldToShadowCoord(i.ws));
                    float ndl = abs(dot(n, l.direction)) * 0.7 + 0.3;
                    col = col * (SampleSH(n) + l.color * ndl * l.shadowAttenuation);
                    col = MixFog(col, i.fog);
                }
                return half4(col, 1);
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
            #pragma multi_compile_instancing
            struct Attr { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V2F { float4 positionCS : SV_POSITION; float fade : TEXCOORD0; };
            V2F vert(Attr i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V2F o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.fade = UNITY_ACCESS_INSTANCED_PROP(CutBits, _BitB).w;
                return o;
            }
            half frag(V2F i) : SV_Target { BitDissolve(i.positionCS, i.fade); return 0; }
            ENDHLSL
        }
    }
}
