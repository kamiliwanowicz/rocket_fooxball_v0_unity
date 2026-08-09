#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RocketFooxball.Editor
{
    /// <summary>Single editor authority for High/Low URP assets and project quality records.</summary>
    public static class GraphicsQualityConfigurator
    {
        public const string HighPipelinePath = "Assets/Settings/PC_RPAsset.asset";
        public const string HighRendererPath = "Assets/Settings/PC_Renderer.asset";
        public const string LowPipelinePath = "Assets/Settings/PC_Low_RPAsset.asset";
        public const string LowRendererPath = "Assets/Settings/PC_Low_Renderer.asset";
        public const string QualitySettingsPath = "ProjectSettings/QualitySettings.asset";
        public const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";

        public const string HighQualityName = "High";
        public const string LowQualityName = "Low";
        public const int HighQualityIndex = 0;
        public const int LowQualityIndex = 1;
        public const int NativeWidth = 1920;
        public const int NativeHeight = 1080;

        private const float HighRenderScale = 1f;
        private const float LowRenderScale = 0.8f;
        private const float HighShadowDistance = 80f;
        private const int HighShadowCascadeCount = 2;
        private const int HighShadowResolution = 2048;
        private const int LowShadowResolution = 256;
        private const float SsaoIntensity = 1.2f;
        private const float SsaoDirectLightingStrength = 0.25f;
        private const float SsaoRadius = 0.035f;
        private const float SsaoFalloff = 100f;

        [MenuItem("Rocket Fooxball/Configure Graphics Quality")]
        public static void ConfigureFromMenu()
        {
            Configure();
        }

        /// <summary>Creates or updates High/Low state, then validates persisted references.</summary>
        public static void Configure()
        {
            var highRenderer = LoadRequiredAsset<UniversalRendererData>(HighRendererPath);
            var highPipeline = LoadRequiredAsset<UniversalRenderPipelineAsset>(HighPipelinePath);

            ConfigureRenderer(highRenderer, high: true);
            ConfigurePipeline(highPipeline, highRenderer, high: true);

            var lowRenderer = GetOrCreateCopy<UniversalRendererData>(HighRendererPath, LowRendererPath);
            var lowPipeline = GetOrCreateCopy<UniversalRenderPipelineAsset>(HighPipelinePath, LowPipelinePath);
            ConfigureRenderer(lowRenderer, high: false);
            ConfigurePipeline(lowPipeline, lowRenderer, high: false);

            ConfigureQualitySettings(highPipeline, lowPipeline);
            ConfigureNativeResolution();

            AssetDatabase.SaveAssets();
            Validate();
        }

        /// <summary>Read-only contract check. Throws on missing assets or unsupported serialized schema.</summary>
        public static void Validate()
        {
            var highRenderer = LoadRequiredAsset<UniversalRendererData>(HighRendererPath);
            var highPipeline = LoadRequiredAsset<UniversalRenderPipelineAsset>(HighPipelinePath);
            var lowRenderer = LoadRequiredAsset<UniversalRendererData>(LowRendererPath);
            var lowPipeline = LoadRequiredAsset<UniversalRenderPipelineAsset>(LowPipelinePath);

            ValidatePipeline(highPipeline, highRenderer, high: true);
            ValidatePipeline(lowPipeline, lowRenderer, high: false);
            ValidateRenderer(highRenderer, high: true);
            ValidateRenderer(lowRenderer, high: false);
            ValidateQualitySettings(highPipeline, lowPipeline);
            ValidateNativeResolution();
        }

        private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
        {
            var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset == null)
            {
                throw new InvalidOperationException("Required graphics asset is missing: " + path);
            }

            var typed = mainAsset as T;
            if (typed == null)
            {
                throw new InvalidOperationException("Graphics asset has unsupported type at " + path + ": " + mainAsset.GetType().FullName);
            }

            return typed;
        }

        private static T GetOrCreateCopy<T>(string sourcePath, string destinationPath) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(destinationPath) as T;
            if (existing != null)
            {
                return existing;
            }

            var wrongType = AssetDatabase.LoadMainAssetAtPath(destinationPath);
            if (wrongType != null)
            {
                throw new InvalidOperationException("Graphics asset has unsupported type at " + destinationPath + ": " + wrongType.GetType().FullName);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
            {
                throw new InvalidOperationException("Could not create graphics asset copy: " + destinationPath);
            }

            return LoadRequiredAsset<T>(destinationPath);
        }

        private static void ConfigurePipeline(UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer, bool high)
        {
            var serialized = new SerializedObject(pipeline);
            SetInt(serialized, "k_AssetVersion", 13);
            SetInt(serialized, "k_AssetPreviousVersion", 13);
            SetInt(serialized, "m_RendererType", (int)RendererType.UniversalRenderer);
            SetInt(serialized, "m_DefaultRendererIndex", 0);
            var rendererDataList = Required(serialized, "m_RendererDataList");
            if (!rendererDataList.isArray)
            {
                throw new InvalidOperationException("Unsupported URP schema: m_RendererDataList is not an array.");
            }
            rendererDataList.arraySize = 1;
            rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;

            SetBool(serialized, "m_SupportsHDR", high);
            SetInt(serialized, "m_HDRColorBufferPrecision", 0);
            SetInt(serialized, "m_MSAA", 1);
            SetFloat(serialized, "m_RenderScale", high ? HighRenderScale : LowRenderScale);
            SetInt(serialized, "m_MainLightRenderingMode", high ? (int)LightRenderingMode.PerPixel : (int)LightRenderingMode.Disabled);
            SetBool(serialized, "m_MainLightShadowsSupported", high);
            SetInt(serialized, "m_MainLightShadowmapResolution", high ? HighShadowResolution : LowShadowResolution);
            SetInt(serialized, "m_AdditionalLightsRenderingMode", high ? (int)LightRenderingMode.PerPixel : (int)LightRenderingMode.Disabled);
            SetInt(serialized, "m_AdditionalLightsPerObjectLimit", high ? 16 : 0);
            SetBool(serialized, "m_AdditionalLightShadowsSupported", false);
            SetInt(serialized, "m_AdditionalLightsShadowmapResolution", LowShadowResolution);
            SetFloat(serialized, "m_ShadowDistance", high ? HighShadowDistance : 0f);
            SetInt(serialized, "m_ShadowCascadeCount", high ? HighShadowCascadeCount : 1);
            SetBool(serialized, "m_AnyShadowsSupported", high);
            SetBool(serialized, "m_SoftShadowsSupported", high);
            SetInt(serialized, "m_SoftShadowQuality", (int)SoftShadowQuality.Medium);
            SetBool(serialized, "m_ReflectionProbeBlending", high);
            SetBool(serialized, "m_ReflectionProbeBoxProjection", high);
            SetBool(serialized, "m_ReflectionProbeAtlas", high);
            SetBool(serialized, "m_UseSRPBatcher", true);
            SetInt(serialized, "m_ColorGradingMode", high ? (int)ColorGradingMode.HighDynamicRange : (int)ColorGradingMode.LowDynamicRange);
            SetInt(serialized, "m_VolumeFrameworkUpdateMode", (int)VolumeFrameworkUpdateMode.EveryFrame);
            Apply(serialized, pipeline);
        }

        private static void ConfigureRenderer(UniversalRendererData renderer, bool high)
        {
            var serialized = new SerializedObject(renderer);
            SetInt(serialized, "m_AssetVersion", 3);
            SetInt(serialized, "m_RenderingMode", high ? (int)RenderingMode.ForwardPlus : (int)RenderingMode.Forward);
            Apply(serialized, renderer);

            var ssaoFeatures = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().ToArray();
            if (ssaoFeatures.Length > 1)
            {
                throw new InvalidOperationException("Renderer contains duplicate SSAO features: " + renderer.name);
            }

            var ssao = ssaoFeatures.FirstOrDefault();
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                ssao.hideFlags = HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
            }

            ConfigureSsao(ssao, high);
            UpdateRendererFeatureMap(renderer);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
        }

        private static void UpdateRendererFeatureMap(UniversalRendererData renderer)
        {
            var serialized = new SerializedObject(renderer);
            var map = Required(serialized, "m_RendererFeatureMap");
            if (!map.isArray)
            {
                throw new InvalidOperationException("Unsupported URP schema: m_RendererFeatureMap is not an array.");
            }

            map.arraySize = renderer.rendererFeatures.Count;
            for (var i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                var entry = map.GetArrayElementAtIndex(i);
                var feature = renderer.rendererFeatures[i];
                if (feature != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
                {
                    entry.longValue = localId;
                }
                else
                {
                    entry.longValue = 0;
                }
            }

            Apply(serialized, renderer);
        }

        private static void ConfigureSsao(ScreenSpaceAmbientOcclusion ssao, bool high)
        {
            var serialized = new SerializedObject(ssao);
            SetBool(serialized, "m_Active", high);
            var settings = Required(serialized, "m_Settings");
            SetInt(settings, "AOMethod", 0);
            SetBool(settings, "Downsample", true);
            SetBool(settings, "AfterOpaque", false);
            SetInt(settings, "Source", 1);
            SetInt(settings, "NormalSamples", 1);
            SetFloat(settings, "Intensity", SsaoIntensity);
            SetFloat(settings, "DirectLightingStrength", SsaoDirectLightingStrength);
            SetFloat(settings, "Radius", SsaoRadius);
            SetInt(settings, "Samples", 1);
            SetInt(settings, "BlurQuality", 1);
            SetFloat(settings, "Falloff", SsaoFalloff);
            SetInt(settings, "SampleCount", -1);
            Apply(serialized, ssao);
        }

        private static void ConfigureQualitySettings(RenderPipelineAsset highPipeline, RenderPipelineAsset lowPipeline)
        {
            var target = LoadProjectSettingsObject(QualitySettingsPath);
            var serialized = new SerializedObject(target);
            var levels = Required(serialized, "m_QualitySettings");
            if (!levels.isArray)
            {
                throw new InvalidOperationException("Unsupported QualitySettings schema: m_QualitySettings is not an array.");
            }

            levels.arraySize = 2;
            ConfigureQualityLevel(levels.GetArrayElementAtIndex(HighQualityIndex), HighQualityName, highPipeline, high: true);
            ConfigureQualityLevel(levels.GetArrayElementAtIndex(LowQualityIndex), LowQualityName, lowPipeline, high: false);
            SetInt(serialized, "m_CurrentQuality", HighQualityIndex);
            SetPerPlatformDefault(serialized, HighQualityIndex);
            Apply(serialized, target);
        }

        private static void SetPerPlatformDefault(SerializedObject serialized, int qualityIndex)
        {
            var defaults = Required(serialized, "m_PerPlatformDefaultQuality");
            var standalone = FindPlatformDefault(defaults, serialized);
            if (standalone == null)
            {
                throw new InvalidOperationException("Unsupported QualitySettings schema: Standalone default quality is missing.");
            }

            standalone.intValue = qualityIndex;
        }

        private static void ConfigureQualityLevel(SerializedProperty level, string name, RenderPipelineAsset pipeline, bool high)
        {
            SetString(level, "name", name);
            SetObject(level, "customRenderPipeline", pipeline);
            SetInt(level, "globalTextureMipmapLimit", high ? 0 : 1);
            SetInt(level, "anisotropicTextures", high ? 2 : 0);
            SetInt(level, "antiAliasing", 0);
            SetInt(level, "pixelLightCount", high ? 8 : 0);
            SetInt(level, "shadows", high ? 2 : 0);
            SetInt(level, "shadowResolution", high ? 2 : 0);
        }

        private static void ConfigureNativeResolution()
        {
            var target = LoadProjectSettingsObject(ProjectSettingsPath);
            var serialized = new SerializedObject(target);
            SetInt(serialized, "defaultScreenWidth", NativeWidth);
            SetInt(serialized, "defaultScreenHeight", NativeHeight);
            Apply(serialized, target);
        }

        private static void ValidatePipeline(UniversalRenderPipelineAsset pipeline, UniversalRendererData renderer, bool high)
        {
            var serialized = new SerializedObject(pipeline);
            ExpectInt(serialized, "k_AssetVersion", 13);
            ExpectInt(serialized, "k_AssetPreviousVersion", 13);
            ExpectInt(serialized, "m_DefaultRendererIndex", 0);
            var rendererDataList = Required(serialized, "m_RendererDataList");
            if (!rendererDataList.isArray || rendererDataList.arraySize != 1 || rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue != renderer)
            {
                throw new InvalidOperationException("URP renderer reference does not match quality contract: " + pipeline.name);
            }

            ExpectInt(serialized, "m_RendererType", (int)RendererType.UniversalRenderer);
            ExpectBool(serialized, "m_SupportsHDR", high);
            ExpectInt(serialized, "m_HDRColorBufferPrecision", 0);
            ExpectInt(serialized, "m_MSAA", 1);
            ExpectFloat(serialized, "m_RenderScale", high ? HighRenderScale : LowRenderScale);
            ExpectInt(serialized, "m_MainLightRenderingMode", high ? 1 : 0);
            ExpectBool(serialized, "m_MainLightShadowsSupported", high);
            ExpectInt(serialized, "m_MainLightShadowmapResolution", high ? HighShadowResolution : LowShadowResolution);
            ExpectInt(serialized, "m_AdditionalLightsRenderingMode", high ? 1 : 0);
            ExpectInt(serialized, "m_AdditionalLightsPerObjectLimit", high ? 16 : 0);
            ExpectBool(serialized, "m_AdditionalLightShadowsSupported", false);
            ExpectInt(serialized, "m_AdditionalLightsShadowmapResolution", LowShadowResolution);
            ExpectFloat(serialized, "m_ShadowDistance", high ? HighShadowDistance : 0f);
            ExpectInt(serialized, "m_ShadowCascadeCount", high ? HighShadowCascadeCount : 1);
            ExpectBool(serialized, "m_AnyShadowsSupported", high);
            ExpectBool(serialized, "m_SoftShadowsSupported", high);
            ExpectInt(serialized, "m_SoftShadowQuality", (int)SoftShadowQuality.Medium);
            ExpectBool(serialized, "m_UseSRPBatcher", true);
            ExpectBool(serialized, "m_ReflectionProbeBlending", high);
            ExpectBool(serialized, "m_ReflectionProbeBoxProjection", high);
            ExpectBool(serialized, "m_ReflectionProbeAtlas", high);
            ExpectInt(serialized, "m_ColorGradingMode", high ? (int)ColorGradingMode.HighDynamicRange : (int)ColorGradingMode.LowDynamicRange);
            ExpectInt(serialized, "m_VolumeFrameworkUpdateMode", (int)VolumeFrameworkUpdateMode.EveryFrame);
        }

        private static void ValidateRenderer(UniversalRendererData renderer, bool high)
        {
            var serialized = new SerializedObject(renderer);
            ExpectInt(serialized, "m_AssetVersion", 3);
            ExpectInt(serialized, "m_RenderingMode", high ? (int)RenderingMode.ForwardPlus : (int)RenderingMode.Forward);

            var ssaoFeatures = renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().ToArray();
            if (ssaoFeatures.Length != 1)
            {
                throw new InvalidOperationException("Renderer must contain exactly one SSAO feature: " + renderer.name);
            }
            var ssao = ssaoFeatures[0];

            var feature = new SerializedObject(ssao);
            ExpectBool(feature, "m_Active", high);
            var settings = Required(feature, "m_Settings");
            ExpectInt(settings, "AOMethod", 0);
            ExpectBool(settings, "Downsample", true);
            ExpectBool(settings, "AfterOpaque", false);
            ExpectInt(settings, "Source", 1);
            ExpectInt(settings, "NormalSamples", 1);
            ExpectFloat(settings, "Intensity", SsaoIntensity);
            ExpectFloat(settings, "DirectLightingStrength", SsaoDirectLightingStrength);
            ExpectFloat(settings, "Radius", SsaoRadius);
            ExpectInt(settings, "Samples", 1);
            ExpectInt(settings, "BlurQuality", 1);
            ExpectFloat(settings, "Falloff", SsaoFalloff);
        }

        private static void ValidateQualitySettings(RenderPipelineAsset highPipeline, RenderPipelineAsset lowPipeline)
        {
            var target = LoadProjectSettingsObject(QualitySettingsPath);
            var serialized = new SerializedObject(target);
            var levels = Required(serialized, "m_QualitySettings");
            if (!levels.isArray || levels.arraySize != 2)
            {
                throw new InvalidOperationException("QualitySettings must contain exactly High and Low levels.");
            }

            ValidateQualityLevel(levels.GetArrayElementAtIndex(HighQualityIndex), HighQualityName, highPipeline, high: true);
            ValidateQualityLevel(levels.GetArrayElementAtIndex(LowQualityIndex), LowQualityName, lowPipeline, high: false);
            ExpectInt(serialized, "m_CurrentQuality", HighQualityIndex);
            var defaults = Required(serialized, "m_PerPlatformDefaultQuality");
            var standalone = FindPlatformDefault(defaults, serialized);
            if (standalone == null || standalone.intValue != HighQualityIndex)
            {
                throw new InvalidOperationException("Standalone default quality must be High (index 0).");
            }
        }

        private static void ValidateQualityLevel(SerializedProperty level, string name, RenderPipelineAsset pipeline, bool high)
        {
            ExpectString(level, "name", name);
            if (Required(level, "customRenderPipeline").objectReferenceValue != pipeline)
            {
                throw new InvalidOperationException("Quality pipeline reference mismatch for " + name + ".");
            }
            ExpectInt(level, "globalTextureMipmapLimit", high ? 0 : 1);
            ExpectInt(level, "anisotropicTextures", high ? 2 : 0);
            ExpectInt(level, "antiAliasing", 0);
            ExpectInt(level, "pixelLightCount", high ? 8 : 0);
            ExpectInt(level, "shadows", high ? 2 : 0);
            ExpectInt(level, "shadowResolution", high ? 2 : 0);
        }

        private static void ValidateNativeResolution()
        {
            var target = LoadProjectSettingsObject(ProjectSettingsPath);
            var serialized = new SerializedObject(target);
            ExpectInt(serialized, "defaultScreenWidth", NativeWidth);
            ExpectInt(serialized, "defaultScreenHeight", NativeHeight);
        }

        private static UnityEngine.Object LoadProjectSettingsObject(string path)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                throw new InvalidOperationException("Project settings object is unavailable: " + path);
            }

            return assets[0];
        }

        private static SerializedProperty FindPlatformDefault(SerializedProperty defaults, SerializedObject serialized)
        {
            var standalone = defaults.FindPropertyRelative("Standalone");
            if (standalone == null) standalone = serialized.FindProperty("m_PerPlatformDefaultQuality.Standalone");
            if (standalone == null) standalone = serialized.FindProperty("m_PerPlatformDefaultQuality.m_Standalone");
            if (standalone == null && defaults.isArray)
            {
                for (var i = 0; i < defaults.arraySize; i++)
                {
                    var item = defaults.GetArrayElementAtIndex(i);
                    var key = item.FindPropertyRelative("first");
                    if (key != null && string.Equals(key.stringValue, "Standalone", StringComparison.Ordinal))
                    {
                        standalone = item.FindPropertyRelative("second");
                        break;
                    }
                }
            }
            return standalone;
        }

        private static SerializedProperty Required(SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("Unsupported serialized schema: missing '" + name + "' on " + serialized.targetObject.name + ".");
            }

            return property;
        }

        private static SerializedProperty Required(SerializedProperty parent, string name)
        {
            var property = parent.FindPropertyRelative(name);
            if (property == null)
            {
                throw new InvalidOperationException("Unsupported serialized schema: missing '" + name + "' on quality level.");
            }

            return property;
        }

        private static void Apply(SerializedObject serialized, UnityEngine.Object target)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetBool(SerializedObject serialized, string name, bool value) => Required(serialized, name).boolValue = value;
        private static void SetInt(SerializedObject serialized, string name, int value) => Required(serialized, name).intValue = value;
        private static void SetFloat(SerializedObject serialized, string name, float value) => Required(serialized, name).floatValue = value;
        private static void SetString(SerializedProperty parent, string name, string value) => Required(parent, name).stringValue = value;
        private static void SetInt(SerializedProperty parent, string name, int value) => Required(parent, name).intValue = value;
        private static void SetBool(SerializedProperty parent, string name, bool value) => Required(parent, name).boolValue = value;
        private static void SetFloat(SerializedProperty parent, string name, float value) => Required(parent, name).floatValue = value;
        private static void SetObject(SerializedProperty parent, string name, UnityEngine.Object value) => Required(parent, name).objectReferenceValue = value;

        private static void ExpectBool(SerializedObject serialized, string name, bool expected)
        {
            if (Required(serialized, name).boolValue != expected) throw ContractError(serialized, name, expected.ToString());
        }

        private static void ExpectInt(SerializedObject serialized, string name, int expected)
        {
            if (Required(serialized, name).intValue != expected) throw ContractError(serialized, name, expected.ToString());
        }

        private static void ExpectFloat(SerializedObject serialized, string name, float expected)
        {
            if (Mathf.Abs(Required(serialized, name).floatValue - expected) > 0.0001f) throw ContractError(serialized, name, expected.ToString("R"));
        }

        private static void ExpectString(SerializedProperty parent, string name, string expected)
        {
            if (!string.Equals(Required(parent, name).stringValue, expected, StringComparison.Ordinal)) throw new InvalidOperationException("Quality contract mismatch: " + name + " expected " + expected + ".");
        }

        private static void ExpectInt(SerializedProperty parent, string name, int expected)
        {
            if (Required(parent, name).intValue != expected) throw new InvalidOperationException("Quality contract mismatch: " + name + " expected " + expected + ".");
        }

        private static void ExpectBool(SerializedProperty parent, string name, bool expected)
        {
            if (Required(parent, name).boolValue != expected) throw new InvalidOperationException("Quality contract mismatch: " + name + " expected " + expected + ".");
        }

        private static void ExpectFloat(SerializedProperty parent, string name, float expected)
        {
            if (Mathf.Abs(Required(parent, name).floatValue - expected) > 0.0001f) throw new InvalidOperationException("Quality contract mismatch: " + name + " expected " + expected + ".");
        }

        private static InvalidOperationException ContractError(SerializedObject serialized, string name, string expected)
        {
            return new InvalidOperationException("Quality contract mismatch on " + serialized.targetObject.name + ": " + name + " expected " + expected + ".");
        }
    }
}
#endif
