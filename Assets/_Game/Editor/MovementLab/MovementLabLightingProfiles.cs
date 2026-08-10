#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Immutable bake contracts.  Scene/settings mutation is limited to the
    /// explicit profile preparation methods; baking itself only consumes a
    /// previously validated settings asset.
    /// </summary>
    internal static class MovementLabLightingProfiles
    {
        internal enum ProfileId
        {
            Development,
            Production
        }

        internal sealed class Specification
        {
            internal readonly ProfileId Id;
            internal readonly string Tag;
            internal readonly int ProbeCount;
            internal readonly int ReflectionResolution;
            internal readonly int LightmapResolution;
            internal readonly int MinBounces;
            internal readonly int MaxBounces;
            internal readonly int DirectSamples;
            internal readonly int IndirectSamples;
            internal readonly int EnvironmentSamples;
            internal readonly int SampleMultiplier;
            internal readonly string SettingsPath;

            internal Specification(ProfileId id, string tag, int probeCount, int reflectionResolution,
                int lightmapResolution, int minBounces, int maxBounces, int directSamples,
                int indirectSamples, int environmentSamples, int sampleMultiplier, string settingsPath)
            {
                Id = id;
                Tag = tag;
                ProbeCount = probeCount;
                ReflectionResolution = reflectionResolution;
                LightmapResolution = lightmapResolution;
                MinBounces = minBounces;
                MaxBounces = maxBounces;
                DirectSamples = directSamples;
                IndirectSamples = indirectSamples;
                EnvironmentSamples = environmentSamples;
                SampleMultiplier = sampleMultiplier;
                SettingsPath = settingsPath;
            }
        }

        internal const string DevelopmentSettingsPath = "Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset";
        internal const string ProductionSettingsPath = MovementLabContract.LightingSettingsPath;

        internal static readonly Specification Development = new Specification(
            ProfileId.Development, "development", 80, 64, 5, 1, 1, 16, 128, 64, 1, DevelopmentSettingsPath);
        internal static readonly Specification Production = new Specification(
            ProfileId.Production, "production", 200, 128, 10, 2, 2, 32, 512, 256, 4, ProductionSettingsPath);

        internal static Specification Get(ProfileId id) => id == ProfileId.Development ? Development : Production;

        internal static LightingSettings LoadValidatedSettings(ProfileId id)
        {
            var spec = Get(id);
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(spec.SettingsPath);
            if (settings == null) throw new InvalidOperationException("Missing persisted " + spec.Tag + " LightingSettings asset: " + spec.SettingsPath);
            ValidateSettings(settings, spec);
            return settings;
        }

        internal static LightingSettings EnsurePersistedDevelopmentSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(DevelopmentSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "MovementLabLightingSettings_Development" };
                AssetDatabase.CreateAsset(settings, DevelopmentSettingsPath);
                ApplySettingsValues(settings, Development);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
            ValidateSettings(settings, Development);
            return settings;
        }

        internal static LightingSettings EnsurePersistedProductionSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(ProductionSettingsPath);
            if (settings == null) throw new InvalidOperationException("Production LightingSettings asset is missing: " + ProductionSettingsPath);
            ValidateSettings(settings, Production);
            return settings;
        }

        internal static void ValidateSettings(LightingSettings settings, Specification spec)
        {
            if (settings == null) throw new InvalidOperationException("LightingSettings is null for " + spec.Tag + ".");
            if (settings.lightmapper != LightingSettings.Lightmapper.ProgressiveCPU || !settings.bakedGI || settings.realtimeGI ||
                settings.mixedBakeMode != MixedLightingMode.Shadowmask || settings.directionalityMode != LightmapsMode.CombinedDirectional ||
                Mathf.Abs(settings.lightmapResolution - spec.LightmapResolution) > 0.001f || settings.lightmapMaxSize != 1024 ||
                settings.lightmapPadding != 2 || settings.maxBounces != spec.MaxBounces || ReadInt(settings, "m_PVRMinBounces") != spec.MinBounces ||
                settings.directSampleCount != spec.DirectSamples || settings.indirectSampleCount != spec.IndirectSamples ||
                settings.environmentSampleCount != spec.EnvironmentSamples || settings.lightProbeSampleCountMultiplier != spec.SampleMultiplier ||
                settings.autoGenerate)
            {
                throw new InvalidOperationException("LightingSettings profile mismatch for " + spec.Tag + ".");
            }
        }

        private static void ApplySettingsValues(LightingSettings settings, Specification spec)
        {
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Shadowmask;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;
            settings.lightmapResolution = spec.LightmapResolution;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 2;
            settings.maxBounces = spec.MaxBounces;
            WriteInt(settings, "m_PVRMinBounces", spec.MinBounces);
            settings.directSampleCount = spec.DirectSamples;
            settings.indirectSampleCount = spec.IndirectSamples;
            settings.environmentSampleCount = spec.EnvironmentSamples;
            settings.lightProbeSampleCountMultiplier = spec.SampleMultiplier;
            settings.compressLightmaps = true;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            settings.autoGenerate = false;
        }

        internal static void PrepareScene(Scene scene, ProfileId id)
        {
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Lighting profile requires the generated MovementLab scene.");

            var spec = Get(id);
            var settings = LoadValidatedSettings(id);
            Lightmapping.SetLightingSettingsForScene(scene, settings);

            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (probes.Length != MovementLabContractCatalog.ReflectionProbeContract.Length)
                throw new InvalidOperationException("MovementLab reflection probe count changed before " + spec.Tag + " bake.");
            for (var i = 0; i < probes.Length; i++) probes[i].resolution = spec.ReflectionResolution;

            var group = GameObject.Find("Environment/LightProbes")?.GetComponent<LightProbeGroup>();
            if (group == null) throw new InvalidOperationException("MovementLab LightProbeGroup is missing before " + spec.Tag + " bake.");
            group.probePositions = id == ProfileId.Development ? CreateDevelopmentPositions() : CreateProductionPositions();
            if (group.probePositions.Length != spec.ProbeCount)
                throw new InvalidOperationException("MovementLab " + spec.Tag + " probe count is not " + spec.ProbeCount + ".");
        }

        internal static void ValidatePreparedScene(ProfileId id)
        {
            var spec = Get(id);
            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            if (probes.Length != MovementLabContractCatalog.ReflectionProbeContract.Length || probes.Any(probe => probe == null || probe.resolution != spec.ReflectionResolution))
                throw new InvalidOperationException("MovementLab " + spec.Tag + " reflection resolution/count mismatch.");
            var group = GameObject.Find("Environment/LightProbes")?.GetComponent<LightProbeGroup>();
            if (group == null || group.probePositions == null || group.probePositions.Length != spec.ProbeCount)
                throw new InvalidOperationException("MovementLab " + spec.Tag + " light probe count mismatch.");
            var scene = SceneManager.GetActiveScene();
            var settings = Lightmapping.GetLightingSettingsForScene(scene);
            if (settings == null) throw new InvalidOperationException("MovementLab " + spec.Tag + " scene LightingSettings binding is missing.");
            ValidateSettings(settings, spec);
        }

        internal static Vector3[] CreateDevelopmentPositions()
        {
            var positions = new List<Vector3>(Development.ProbeCount);
            var heights = new[] { 1.5f, 8f, 20f, 36f, 46f };
            var x = new[] { -56f, -18f, 18f, 56f };
            var z = new[] { -36f, -12f, 12f, 36f };
            for (var h = 0; h < heights.Length; h++)
                for (var xi = 0; xi < x.Length; xi++)
                    for (var zi = 0; zi < z.Length; zi++) positions.Add(new Vector3(x[xi], heights[h], z[zi]));
            return positions.ToArray();
        }

        internal static Vector3[] CreateProductionPositions()
        {
            var positions = new List<Vector3>(Production.ProbeCount);
            var heights = new[] { 1.5f, 8f, 20f, 36f, 46f };
            for (var h = 0; h < heights.Length; h++)
                for (var x = -56f; x <= 56f; x += 16f)
                    for (var z = -36f; z <= 36f; z += 18f) positions.Add(new Vector3(x, heights[h], z));
            return positions.ToArray();
        }

        internal static void WriteManifest(ProfileId id, string lightingInputDigest)
        {
            var spec = Get(id);
            ValidatePreparedScene(id);
            var paths = MovementLabContractCatalog.GeneratedBakedLightingPaths
                .Concat(new[] { MovementLabContract.LightingManifestPath })
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var hashes = paths.Select(HashAsset).ToArray();
            var settings = Lightmapping.GetLightingSettingsForScene(SceneManager.GetActiveScene());
            if (settings == null) throw new InvalidOperationException("MovementLab " + spec.Tag + " LightingSettings binding is missing while writing manifest.");
            var state = new MovementLabLightingManifestState
            {
                schemaVersion = 2,
                profileId = spec.Id.ToString(),
                profileTag = spec.Tag,
                specificationProbeCount = spec.ProbeCount,
                specificationReflectionResolution = spec.ReflectionResolution,
                specificationLightmapResolution = spec.LightmapResolution,
                specificationMinBounces = spec.MinBounces,
                specificationMaxBounces = spec.MaxBounces,
                specificationDirectSamples = spec.DirectSamples,
                specificationIndirectSamples = spec.IndirectSamples,
                specificationEnvironmentSamples = spec.EnvironmentSamples,
                specificationSampleMultiplier = spec.SampleMultiplier,
                actualProbeCount = GameObject.Find("Environment/LightProbes").GetComponent<LightProbeGroup>().probePositions.Length,
                actualReflectionResolution = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID)[0].resolution,
                actualLightmapResolution = Mathf.RoundToInt(settings.lightmapResolution),
                actualMinBounces = ReadInt(settings, "m_PVRMinBounces"),
                actualMaxBounces = Mathf.RoundToInt(settings.maxBounces),
                actualDirectSamples = settings.directSampleCount,
                actualIndirectSamples = settings.indirectSampleCount,
                actualEnvironmentSamples = settings.environmentSampleCount,
                actualSampleMultiplier = Mathf.RoundToInt(settings.lightProbeSampleCountMultiplier),
                sourceSha = MovementLabManifestStore.GetCurrentGitSha(),
                unityVersion = Application.unityVersion,
                lightingInputDigest = lightingInputDigest ?? string.Empty,
                outputPaths = paths,
                outputHashes = hashes
            };
            var json = JsonUtility.ToJson(state, true) + "\n";
            var path = MovementLabManifestStore.ResolveProjectPath(MovementLabContract.LightingManifestPath);
            var directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            AssetDatabase.ImportAsset(MovementLabContract.LightingManifestPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static string HashAsset(string path)
        {
            var absolute = MovementLabManifestStore.ResolveProjectPath(path);
            if (!File.Exists(absolute)) return "missing";
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(absolute))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static int ReadInt(LightingSettings settings, string propertyName)
        {
            var serialized = new SerializedObject(settings);
            var property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException("LightingSettings schema missing " + propertyName + ".");
            return property.intValue;
        }

        private static void WriteInt(LightingSettings settings, string propertyName, int value)
        {
            var serialized = new SerializedObject(settings);
            var property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException("LightingSettings schema missing " + propertyName + ".");
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
