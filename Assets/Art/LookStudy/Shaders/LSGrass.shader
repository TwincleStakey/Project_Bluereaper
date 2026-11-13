// Grass blades and flowers. Ground uses the same FieldColor so the gaps between blades disappear.
Shader "LookStudy/Grass"
{
    Properties
    {
        _ColA("Patch deep", Color) = (0.05, 0.42, 0.13, 1)
        _ColB("Patch mid", Color) = (0.22, 0.66, 0.16, 1)
        _ColC("Patch light", Color) = (0.62, 0.84, 0.22, 1)
        _RootDark("Root darkening", Range(0,1)) = 0.5
        _TipLift("Tip lift", Range(1,2)) = 1.32
        _LaneCol("Wind lane colour", Color) = (0.86, 0.95, 0.42, 1)
        _LaneAmt("Wind lane amount", Range(0,1)) = 0.5
        _WindDir("Wind dir XZ", Vector) = (0.8, 0.6, 0, 0)
        _Bend("Wind bend", Range(0,1.5)) = 0.5
        _BladeVar("Per blade variation", Range(0,0.6)) = 0.07
        _PatchContrast("Patch contrast", Range(0,5)) = 3.2
        _ClumpAmt("Clump amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "LSCommon.hlsl"
            #include "LSField.hlsl"

            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 root : TEXCOORD1; float4 color : COLOR; };
            struct V2F
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 root : TEXCOORD1;
                float4 data : TEXCOORD2; // height, rand, lane, across
                float4 color : COLOR;
            };

            V2F vert(Attr a)
            {
                V2F o;
                float3 ws = TransformObjectToWorld(a.positionOS.xyz);
                float3 root = a.root.xyz;
                float h = a.uv.y;
                float rnd = a.root.w;

                float lane = LSWindLane(root.xz);
                float2 dir = normalize(_WindDir.xy);
                float sway = sin(_LSTime * 2.3 + rnd * 6.28 + dot(root.xz, float2(0.6, 0.4))) * 0.05;
                float amount = (0.14 + lane * 0.6 + sway) * _Bend;
                float blade = ws.y - root.y;

                // Walking through grass pushes it aside.
                float2 away = root.xz - _LSPush.xz;
                float dist = length(away);
                float push = saturate(1.0 - dist / max(_LSPush.w, 0.001));
                push = push * push * 1.4;

                float2 offs = dir * amount + (dist > 0.001 ? away / dist : float2(0, 0)) * push;
                float k = h * h;
                ws.xz += offs * k * max(blade / max(h, 0.05), 0.25);
                ws.y -= saturate(length(offs)) * k * blade * 0.55;

                ws = RRCutApply(ws, root, 0.15);   // greatsword cuts
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.root = root;
                o.data = float4(h, rnd, lane, a.uv.x);
                o.color = a.color;
                // greatsword cut: straw-coloured stubble (vertex alpha drives the colour override in frag)
                float cut = RRCut(root.xz);
                o.color = lerp(o.color, float4(0.62, 0.55, 0.26, 0.8), cut);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float h = i.data.x;
                float3 col = LSFieldColor(i.root.xz, h, i.data.y);
                // The rounded blade: one side catches a little more light.
                col *= 1.0 + i.data.w * 0.12;
                col = lerp(col, _LaneCol.rgb, i.data.z * _LaneAmt * h * h);
                // Flowers and tinted blades override through vertex alpha.
                col = lerp(col, i.color.rgb, saturate(i.color.a));

                float shadow = LSShadow(i.positionWS);
                float light = shadow * LSCloud(i.root.xz);
                col *= lerp(_LSShadowMul, _LSSunCol, light);
                col = LSFog(col, i.positionWS);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
