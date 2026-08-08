Shader "RocketFooxball/RetroShield"
{
    Properties
    {
        [MainTexture] _BaseMap("Shield Mask", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (0.18, 0.72, 0.95, 1)
        _EmissionColor("Emission Color", Color) = (0.10, 0.85, 1.0, 1)
        _PulseSpeed("Pulse Speed", Range(0, 8)) = 1.2
        _ScanScale("Scan Scale", Range(0, 12)) = 3
        _Alpha("Alpha", Range(0, 1)) = 0.52
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RetroShieldVertex
            #pragma fragment RetroShieldFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _PulseSpeed;
                half _ScanScale;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings RetroShieldVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 RetroShieldFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half pulse = 0.78h + 0.22h * sin(_Time.y * _PulseSpeed);
                half scan = 0.5h + 0.5h * sin((input.uv.y * _ScanScale + _Time.y * _PulseSpeed) * 6.2831853h);
                half boundedScan = 0.72h + 0.28h * scan;
                half alpha = saturate(mask.a * _BaseColor.a * _Alpha * pulse * boundedScan * input.color.a);
                half3 color = mask.rgb * _BaseColor.rgb * input.color.rgb;
                color += _EmissionColor.rgb * (0.30h + 0.70h * scan) * pulse;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
