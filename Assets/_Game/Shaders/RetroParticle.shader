Shader "RocketFooxball/RetroParticle"
{
    Properties
    {
        [MainTexture] _BaseMap("Sprite", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
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
            #pragma vertex RetroParticleVertex
            #pragma fragment RetroParticleFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _SCORCH_MARK

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
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

            Varyings RetroParticleVertex(Attributes input)
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

            half4 RetroParticleFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 sprite = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
#if defined(_SCORCH_MARK)
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x);
                float irregularRadius = 0.78 + 0.08 * sin(7.0 * a + 0.4) + 0.05 * sin(13.0 * a - 1.1);
                float edge = 1.0 - smoothstep(irregularRadius - 0.18, irregularRadius, r);
                float mottle = saturate(0.65 + 0.20 * sin(31.0 * p.x + 17.0 * p.y) + 0.15 * sin(19.0 * p.x - 29.0 * p.y));
                sprite.a *= edge * mottle;
#endif
                return sprite * _BaseColor * input.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
