// 示範手（ghost hands）：柔和的半透明青色，邊緣（fresnel）比較亮、比較不透明，像發光的手的輪廓。
// 兩個 pass：先只寫深度（SRPDefaultUnlit，URP 先畫），再畫顏色（UniversalForward，ZTest LEqual）——
// 手指互相重疊的地方只畫最前面那一層，不會疊出深淺不一的斑塊。一個網格（兩隻手）= 2 個 draw call。
// 頂點色的 alpha 用來讓手腕往前臂的那一段淡出。放在 Resources，建置時才會被打包（Shader.Find 找得到）。
Shader "Poker/GhostHand"
{
    Properties
    {
        _BaseColor ("Color (alpha = overall)", Color) = (0.55, 0.92, 1, 0.5)
        _RimColor ("Rim color", Color) = (0.85, 1, 1, 1)
        _RimPower ("Rim power", Range(0.5, 8)) = 2.2
        _Fill ("Interior opacity (fraction of rim)", Range(0, 1)) = 0.38
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half4 _RimColor; half _RimPower; half _Fill;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "GhostColor"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half4 _RimColor; half _RimPower; half _Fill;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                half fres = pow(1.0h - saturate(dot(n, viewDir)), _RimPower);
                // 上方稍亮一點，看得出指節的立體感（不用打光）
                half shade = 0.86h + 0.14h * saturate(n.y * 0.5h + 0.5h);
                half3 rgb = lerp(_BaseColor.rgb * shade, _RimColor.rgb, fres);
                half a = _BaseColor.a * lerp(_Fill, 1.0h, fres) * i.color.a;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
