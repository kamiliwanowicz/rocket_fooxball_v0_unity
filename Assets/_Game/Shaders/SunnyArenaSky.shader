Shader "RocketFooxball/SunnyArenaSky"
{
    Properties
    {
        [MainTexture] _Panorama("Panorama / Clouds", 2D) = "white" {}
        _HorizonColor("Horizon Color", Color) = (0.725, 0.863, 0.949, 1)
        _ZenithColor("Zenith Color", Color) = (0.298, 0.569, 0.847, 1)
        _CloudTint("Cloud Tint", Color) = (0.961, 0.953, 0.91, 1)
        _CloudCoverage("Cloud Coverage", Range(0, 1)) = 0.22
        _CloudSoftness("Cloud Softness", Range(0, 1)) = 0.65
        _SunDirection("Sun Direction", Vector) = (0.433, 0.663, 0.612, 0)
        _SunColor("Sun Color", Color) = (1, 0.839, 0.639, 1)
        _SunAngularRadius("Sun Angular Radius", Range(0.001, 0.05)) = 0.012
        _SunIntensity("Sun HDR Intensity", Range(0, 8)) = 3
        _FogHorizonColor("Fog Horizon", Color) = (0.725, 0.863, 0.949, 1)
        _FogHorizonHeight("Fog Horizon Height", Range(-0.5, 0.5)) = 0.02
        _FogHorizonWidth("Fog Horizon Width", Range(0.01, 1)) = 0.28
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Skybox"
        }

        Pass
        {
            Name "Skybox"
            Tags { "LightMode" = "Skybox" }
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex SunnySkyVertex
            #pragma fragment SunnySkyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Panorama);
            SAMPLER(sampler_Panorama);

            CBUFFER_START(UnityPerMaterial)
                float4 _Panorama_ST;
                half4 _HorizonColor;
                half4 _ZenithColor;
                half4 _CloudTint;
                half _CloudCoverage;
                half _CloudSoftness;
                half4 _SunDirection;
                half4 _SunColor;
                half _SunAngularRadius;
                half _SunIntensity;
                half4 _FogHorizonColor;
                half _FogHorizonHeight;
                half _FogHorizonWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
            };

            Varyings SunnySkyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half2 EquirectangularUv(half3 direction)
            {
                const half invTwoPi = 0.1591549431h;
                const half invPi = 0.3183098862h;
                half longitude = atan2(direction.z, direction.x);
                half latitude = asin(clamp(direction.y, -1.0h, 1.0h));
                return half2(longitude * invTwoPi + 0.5h, latitude * invPi + 0.5h);
            }

            half4 SunnySkyFragment(Varyings input) : SV_Target
            {
                half3 direction = SafeNormalize(input.directionWS);
                half horizonT = smoothstep(-0.18h, 0.72h, direction.y);
                half3 gradient = lerp(_HorizonColor.rgb, _ZenithColor.rgb, horizonT);

                // One authored panorama supplies cloud shape and colour; coverage remains bounded.
                half2 panoramaUv = EquirectangularUv(direction);
                half4 panorama = SAMPLE_TEXTURE2D(_Panorama, sampler_Panorama, panoramaUv);
                half cloudMask = saturate((panorama.a - (1.0h - _CloudCoverage)) / max(_CloudSoftness, 0.001h));
                half3 cloudLayer = panorama.rgb * _CloudTint.rgb;
                gradient = lerp(gradient, cloudLayer, cloudMask * _CloudCoverage);

                half horizonFog = 1.0h - smoothstep(
                    _FogHorizonHeight - max(_FogHorizonWidth, 0.01h),
                    _FogHorizonHeight + max(_FogHorizonWidth, 0.01h),
                    direction.y);
                gradient = lerp(gradient, _FogHorizonColor.rgb, horizonFog * 0.35h);

                half3 sunDirection = SafeNormalize(_SunDirection.xyz);
                half angularDistance = distance(direction, sunDirection);
                half radius = max(_SunAngularRadius, 0.001h);
                half sunDisc = 1.0h - smoothstep(radius * 0.45h, radius, angularDistance);
                gradient += _SunColor.rgb * sunDisc * min(max(_SunIntensity, 0.0h), 8.0h);
                return half4(max(gradient, 0.0h), 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
