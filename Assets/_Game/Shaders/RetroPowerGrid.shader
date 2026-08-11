Shader "RocketFooxball/RetroPowerGrid"
{
    Properties
    {
        [MainColor] _GridColor("Grid Color", Color) = (0.05, 0.85, 0.95, 1)
        _Alpha("Grid Alpha", Range(0, 1)) = 0.11
        _CellSize("Cell Size (m)", Range(0.25, 32)) = 4
        _GridScale("Grid UV Scale", Vector) = (32.5, 22.5, 0, 0)
        _MajorInterval("Major Interval (cells)", Range(1, 16)) = 5
        _MinorWidth("Minor Line Width", Range(0.001, 0.25)) = 0.025
        _MajorWidth("Major Line Width", Range(0.001, 0.5)) = 0.045
        _FogColor("Fog Color", Color) = (0.12, 0.28, 0.38, 1)
        _FogStrength("Fog Strength", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex RetroPowerGridVertex
            #pragma fragment RetroPowerGridFragment
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GridColor;
                half _Alpha;
                half _CellSize;
                float4 _GridScale;
                half _MajorInterval;
                half _MinorWidth;
                half _MajorWidth;
                half4 _FogColor;
                half _FogStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half2 uv : TEXCOORD3;
            };

            Varyings RetroPowerGridVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.uv = input.uv;
                return output;
            }

            float2 GridPlaneCoordinates(float3 positionWS, float3 normalWS)
            {
                float3 axis = abs(normalWS);
                if (axis.y >= axis.x && axis.y >= axis.z)
                {
                    return positionWS.xz;
                }
                if (axis.x >= axis.z)
                {
                    return positionWS.zy;
                }
                return positionWS.xy;
            }

            half LineMask(float2 coordinates, half width)
            {
                float2 distanceToLine = abs(frac(coordinates) - 0.5);
                float lineDistance = min(distanceToLine.x, distanceToLine.y);
                float aa = max(fwidth(coordinates.x), fwidth(coordinates.y));
                return 1.0h - smoothstep(max(width, 0.0), max(width + aa, 0.0001), lineDistance);
            }

            half4 RetroPowerGridFragment(Varyings input) : SV_Target
            {
                float2 plane = GridPlaneCoordinates(input.positionWS, input.normalWS);
                half cellSize = max(_CellSize, 0.25h);
                float2 worldCells = plane / cellSize;
                float2 uvCells = input.uv * max(_GridScale.xy, 1.0h);
                // UV scale gives stable cells on generated quads; world coordinates retain orientation fallback.
                float2 cells = lerp(worldCells, uvCells, 0.85h);
                half minor = LineMask(cells, max(_MinorWidth, 0.001h));
                half major = LineMask(cells / max(_MajorInterval, 1.0h), max(_MajorWidth, 0.001h));
                half gridLine = saturate(max(minor * 0.55h, major));
                half alpha = saturate(gridLine * _Alpha);
                half3 color = MixFog(_GridColor.rgb, input.fogFactor);
                color = lerp(color, _FogColor.rgb, saturate(_FogStrength) * (1.0h - input.fogFactor));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
