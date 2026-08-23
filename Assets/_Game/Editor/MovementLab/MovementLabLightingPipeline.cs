using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Diagnostics;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Physics;
using RocketFooxball.Runtime.Rendering;
using RocketFooxball.Runtime.Weapons;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using MaterialSpecification = RocketFooxball.Editor.MovementLabContract.MaterialSpecification;
using PbrMaterialSpecification = RocketFooxball.Editor.MovementLabContract.PbrMaterialSpecification;
using WorldAnimatorConditionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorConditionSpecification;
using WorldAnimatorTransitionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorTransitionSpecification;

using static RocketFooxball.Editor.MovementLabContractCatalog;
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabLightingPipeline
    {
                internal static readonly Color ProductionAmbientSkyColor = new Color(0.42f, 0.40f, 0.36f, 1f);
                internal static readonly Color ProductionAmbientEquatorColor = new Color(0.28f, 0.25f, 0.22f, 1f);
                internal static readonly Color ProductionAmbientGroundColor = new Color(0.16f, 0.14f, 0.12f, 1f);
                internal static readonly Vector3 ProductionSunEuler = new Vector3(50f, 330f, 0f);
                internal const float ProductionAmbientIntensity = 0.85f;
                internal const float FastAmbientIntensity = 1.05f;
                internal const float ProductionSunIntensity = 2.4f;
                internal const float ProductionSunShadowStrength = 0.65f;
                internal const float TonemappingPostExposure = 0.35f;
                internal const float ColorAdjustmentsContrast = 2f;
                internal const float ColorAdjustmentsSaturation = 2f;
                internal const float BloomThreshold = 1.1f;
                internal const float BloomIntensity = 0.20f;
                internal const float BloomScatter = 0.60f;
                internal const float BloomClamp = 10f;
                internal const bool BloomHighQualityFiltering = false;
                private const float GoalAccentIntensity = 350f;
                private const float GoalAccentRange = 24f;
                private static readonly Color WallFillColor = new Color(1.0f, 0.82f, 0.64f, 1f);
                private const float WallFillIntensity = 900f;
                private const float WallFillRange = 32f;
                private const float WallFillOuterAngle = 120f;
                private const float WallFillInnerAngle = 105f;
                private static readonly (string name, Vector3 position, Vector3 target)[] WallFillLightContract =
                {
                    ("WallFill_North_West", new Vector3(-43f, 10f, -24f), new Vector3(-43f, 4f, -44.5f)),
                    ("WallFill_North_Center", new Vector3(0f, 10f, -24f), new Vector3(0f, 4f, -44.5f)),
                    ("WallFill_North_East", new Vector3(43f, 10f, -24f), new Vector3(43f, 4f, -44.5f)),
                    ("WallFill_South_West", new Vector3(-43f, 10f, 24f), new Vector3(-43f, 4f, 44.5f)),
                    ("WallFill_South_Center", new Vector3(0f, 10f, 24f), new Vector3(0f, 4f, 44.5f)),
                    ("WallFill_South_East", new Vector3(43f, 10f, 24f), new Vector3(43f, 4f, 44.5f))
                };

                // Gameplay assembly owns scene objects and bindings only. The
                // sky material, VolumeProfile subassets, and LightingSettings
                // asset are authored by the lighting-owned pipeline methods
                // below and are loaded here without mutation.
                internal static void BindSceneEnvironment(Scene scene, ArenaBuild arena)
                {
                    AuthorPersistedVolumeProfile();
                    var environment = new GameObject("Environment");
                    var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
                    if (sun == null)
                    {
                        sun = new GameObject("Sun").AddComponent<Light>();
                    }
                    sun.GetUniversalAdditionalLightData();

                    sun.gameObject.name = "Sun";
                    sun.transform.SetParent(environment.transform, false);
                    sun.type = LightType.Directional;
                    sun.color = SunColor;
                    sun.intensity = ProductionSunIntensity;
                    sun.transform.rotation = Quaternion.Euler(ProductionSunEuler);
                    sun.lightmapBakeType = LightmapBakeType.Mixed;
                    sun.shadows = LightShadows.Soft;
                    sun.shadowStrength = ProductionSunShadowStrength;
                    sun.shadowBias = 0.05f;
                    sun.shadowNormalBias = 0.4f;
                    sun.cullingMask = -1;

                    var skyMaterial = AuthorSkyMaterial(-sun.transform.forward);

                    RenderSettings.skybox = skyMaterial;
                    RenderSettings.sun = sun;
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = ProductionAmbientSkyColor;
                    RenderSettings.ambientEquatorColor = ProductionAmbientEquatorColor;
                    RenderSettings.ambientGroundColor = ProductionAmbientGroundColor;
                    RenderSettings.ambientIntensity = ProductionAmbientIntensity;
                    RenderSettings.fog = true;
                    RenderSettings.fogColor = SkyHorizonColor;
                    RenderSettings.fogMode = FogMode.Linear;
                    RenderSettings.fogStartDistance = 75f;
                    RenderSettings.fogEndDistance = 170f;
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                    RenderSettings.defaultReflectionResolution = 128;
                    RenderSettings.reflectionBounces = 2;
                    RenderSettings.reflectionIntensity = 1f;

                    ConfigureAccentLights(environment.transform);
                    ConfigureWallFillLights(environment.transform);
                    BindExistingGlobalVolume(environment.transform);
                    ConfigureLightProbes(environment.transform);
                    ConfigureReflectionProbes(environment.transform);
                    MarkArenaStaticForLighting(arena.Root);
                    BindExistingLightingSettings(scene);
                }

                private static Material AuthorSkyMaterial(Vector3 sunDirection)
                {
                    var skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
                    if (skyMaterial == null)
                    {
                        throw new InvalidOperationException("Lighting-owned sky material is missing: " + SkyMaterialPath);
                    }

                    skyMaterial.SetVector("_SunDirection", sunDirection);
                    EditorUtility.SetDirty(skyMaterial);
                    AssetDatabase.SaveAssetIfDirty(skyMaterial);
                    return skyMaterial;
                }

                private static void BindExistingGlobalVolume(Transform parent)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                    if (profile == null)
                    {
                        throw new InvalidOperationException("Lighting-owned VolumeProfile is missing: " + VolumeProfilePath);
                    }

                    var volumeObject = new GameObject("GlobalVolume");
                    volumeObject.transform.SetParent(parent, false);
                    var volume = volumeObject.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 0f;
                    volume.sharedProfile = profile;
                }

                private static void BindExistingLightingSettings(Scene scene)
                {
                    var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
                    if (settings == null)
                    {
                        throw new InvalidOperationException("Lighting-owned LightingSettings asset is missing: " + LightingSettingsPath);
                    }

                    // This updates the scene-owned LightmapSettings binding;
                    // it never mutates or dirties the LightingSettings asset.
                    Lightmapping.SetLightingSettingsForScene(scene, settings);
                }

                internal static void ConfigureAccentLights(Transform parent)
                {
                    for (var i = 0; i < AccentLightContract.Length; i++)
                    {
                        var contract = AccentLightContract[i];
                        var light = new GameObject(contract.name).AddComponent<Light>();
                        light.GetUniversalAdditionalLightData();
                        light.transform.SetParent(parent, false);
                        light.transform.localPosition = contract.position;
                        light.type = LightType.Point;
                        light.color = contract.color;
                        light.intensity = GoalAccentIntensity;
                        light.range = GoalAccentRange;
                        light.shadows = LightShadows.None;
                        light.lightmapBakeType = LightmapBakeType.Realtime;
                    }
                }

                private static void ConfigureWallFillLights(Transform parent)
                {
                    for (var i = 0; i < WallFillLightContract.Length; i++)
                    {
                        var contract = WallFillLightContract[i];
                        var light = new GameObject(contract.name).AddComponent<Light>();
                        light.GetUniversalAdditionalLightData();
                        light.transform.SetParent(parent, false);
                        light.transform.localPosition = contract.position;
                        light.transform.rotation = Quaternion.LookRotation(contract.target - contract.position);
                        light.type = LightType.Spot;
                        light.color = WallFillColor;
                        light.intensity = WallFillIntensity;
                        light.range = WallFillRange;
                        light.spotAngle = WallFillOuterAngle;
                        light.innerSpotAngle = WallFillInnerAngle;
                        light.shadows = LightShadows.Soft;
                        light.shadowStrength = 0.85f;
                        light.lightmapBakeType = LightmapBakeType.Baked;
                    }
                }

                [Serializable]
                internal sealed class VolumeProfileIdentitySnapshot
                {
                    public string profileGuid;
                    public long profileLocalId;
                    public string[] componentTypes;
                    public string[] componentGuids;
                    public long[] componentLocalIds;
                    public int componentCount;
                    public int tonemappingCount;
                    public int bloomCount;
                    public int colorAdjustmentsCount;
                }

                [Serializable]
                internal sealed class VolumeProfileAuthorSnapshot
                {
                    public string persistedHash;
                    public VolumeProfileIdentitySnapshot identity;
                }

                internal static VolumeProfileAuthorSnapshot AuthorPersistedVolumeProfile()
                {
                    var before = CapturePersistedVolumeProfileSnapshot();
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                    var components = ValidateVolumeProfileComposition(profile);
                    var tonemapping = components.OfType<Tonemapping>().Single();
                    var color = components.OfType<ColorAdjustments>().Single();
                    var changed = false;

                    changed |= SetActive(tonemapping, true);
                    changed |= SetOverride(tonemapping.mode, true);
                    changed |= SetValue(tonemapping.mode, TonemappingMode.ACES);
                    changed |= SetActive(color, true);
                    changed |= SetOverride(color.postExposure, true);
                    changed |= SetValue(color.postExposure, TonemappingPostExposure);
                    changed |= SetOverride(color.contrast, true);
                    changed |= SetValue(color.contrast, ColorAdjustmentsContrast);
                    changed |= SetOverride(color.saturation, true);
                    changed |= SetValue(color.saturation, ColorAdjustmentsSaturation);

                    if (changed)
                    {
                        EditorUtility.SetDirty(profile);
                        EditorUtility.SetDirty(tonemapping);
                        EditorUtility.SetDirty(components.OfType<Bloom>().Single());
                        EditorUtility.SetDirty(color);
                    }
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(VolumeProfilePath, ImportAssetOptions.ForceSynchronousImport);
                    var after = CapturePersistedVolumeProfileSnapshot();
                    AssertVolumeProfileIdentityStable(before.identity, after.identity);
                    ValidateVolumeProfileValues(AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath));
                    return after;
                }

                internal static VolumeProfileAuthorSnapshot CapturePersistedVolumeProfileSnapshot()
                {
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                    var components = ValidateVolumeProfileComposition(profile);
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(profile, out var profileGuid, out long profileLocalId) ||
                        string.IsNullOrEmpty(profileGuid) || profileLocalId == 0)
                        throw new InvalidOperationException("Lighting-owned VolumeProfile has no persistent GUID/local file ID.");

                    var componentTypes = new string[components.Count];
                    var componentGuids = new string[components.Count];
                    var componentLocalIds = new long[components.Count];
                    for (var i = 0; i < components.Count; i++)
                    {
                        var component = components[i];
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out var componentGuid, out long componentLocalId) ||
                            string.IsNullOrEmpty(componentGuid) || componentLocalId == 0)
                            throw new InvalidOperationException("Lighting-owned VolumeProfile component has no persistent GUID/local file ID: " + component.GetType().Name);
                        componentTypes[i] = component.GetType().FullName;
                        componentGuids[i] = componentGuid;
                        componentLocalIds[i] = componentLocalId;
                    }

                    var absolutePath = MovementLabManifestStore.ResolveProjectPath(VolumeProfilePath);
                    if (!File.Exists(absolutePath)) throw new InvalidOperationException("Lighting-owned VolumeProfile bytes are missing: " + VolumeProfilePath);
                    return new VolumeProfileAuthorSnapshot
                    {
                        persistedHash = HashBytes(File.ReadAllBytes(absolutePath)),
                        identity = new VolumeProfileIdentitySnapshot
                        {
                            profileGuid = profileGuid,
                            profileLocalId = profileLocalId,
                            componentTypes = componentTypes,
                            componentGuids = componentGuids,
                            componentLocalIds = componentLocalIds,
                            componentCount = components.Count,
                            tonemappingCount = components.Count(component => component is Tonemapping),
                            bloomCount = components.Count(component => component is Bloom),
                            colorAdjustmentsCount = components.Count(component => component is ColorAdjustments)
                        }
                    };
                }

                private static List<VolumeComponent> ValidateVolumeProfileComposition(VolumeProfile profile)
                {
                    if (profile == null) throw new InvalidOperationException("Lighting-owned VolumeProfile is missing: " + VolumeProfilePath);
                    var components = profile.components;
                    var persistedSubassets = AssetDatabase.LoadAllAssetsAtPath(VolumeProfilePath).OfType<VolumeComponent>().ToArray();
                    if (components == null || components.Count != 3 ||
                        components.Count(component => component is Tonemapping) != 1 ||
                        components.Count(component => component is Bloom) != 1 ||
                        components.Count(component => component is ColorAdjustments) != 1 ||
                        persistedSubassets.Length != 3 ||
                        persistedSubassets.Count(component => component is Tonemapping) != 1 ||
                        persistedSubassets.Count(component => component is Bloom) != 1 ||
                        persistedSubassets.Count(component => component is ColorAdjustments) != 1 ||
                        components.Any(component => component == null || AssetDatabase.GetAssetPath(component) != VolumeProfilePath || !EditorUtility.IsPersistent(component)))
                        throw new InvalidOperationException("Lighting-owned VolumeProfile must contain exactly one existing Tonemapping, Bloom, and ColorAdjustments subasset.");
                    return components.ToList();
                }

                private static void ValidateVolumeProfileValues(VolumeProfile profile)
                {
                    var components = ValidateVolumeProfileComposition(profile);
                    var tonemapping = components.OfType<Tonemapping>().Single();
                    var bloom = components.OfType<Bloom>().Single();
                    var color = components.OfType<ColorAdjustments>().Single();
                    if (!tonemapping.active || !tonemapping.mode.overrideState || tonemapping.mode.value != TonemappingMode.ACES ||
                        !bloom.active || !bloom.threshold.overrideState || Mathf.Abs(bloom.threshold.value - BloomThreshold) > 0.001f ||
                        !bloom.intensity.overrideState || Mathf.Abs(bloom.intensity.value - BloomIntensity) > 0.001f ||
                        !bloom.scatter.overrideState || Mathf.Abs(bloom.scatter.value - BloomScatter) > 0.001f ||
                        !bloom.clamp.overrideState || Mathf.Abs(bloom.clamp.value - BloomClamp) > 0.001f ||
                        !bloom.highQualityFiltering.overrideState || bloom.highQualityFiltering.value != BloomHighQualityFiltering ||
                        !color.active || !color.postExposure.overrideState || Mathf.Abs(color.postExposure.value - TonemappingPostExposure) > 0.001f ||
                        !color.contrast.overrideState || Mathf.Abs(color.contrast.value - ColorAdjustmentsContrast) > 0.001f ||
                        !color.saturation.overrideState || Mathf.Abs(color.saturation.value - ColorAdjustmentsSaturation) > 0.001f)
                        throw new InvalidOperationException("Lighting-owned VolumeProfile value contract invalid.");
                }

                private static void AssertVolumeProfileIdentityStable(VolumeProfileIdentitySnapshot expected, VolumeProfileIdentitySnapshot actual)
                {
                    if (expected == null || actual == null || expected.profileGuid != actual.profileGuid || expected.profileLocalId != actual.profileLocalId ||
                        expected.componentCount != actual.componentCount || expected.tonemappingCount != actual.tonemappingCount ||
                        expected.bloomCount != actual.bloomCount || expected.colorAdjustmentsCount != actual.colorAdjustmentsCount ||
                        !expected.componentTypes.SequenceEqual(actual.componentTypes, StringComparer.Ordinal) ||
                        !expected.componentGuids.SequenceEqual(actual.componentGuids, StringComparer.Ordinal) ||
                        !expected.componentLocalIds.SequenceEqual(actual.componentLocalIds))
                        throw new InvalidOperationException("Lighting-owned VolumeProfile identity changed while authoring.");
                }

                private static bool SetActive(VolumeComponent component, bool value)
                {
                    if (component.active == value) return false;
                    component.active = value;
                    return true;
                }

                private static bool SetOverride<T>(VolumeParameter<T> parameter, bool value)
                {
                    if (parameter.overrideState == value) return false;
                    parameter.overrideState = value;
                    return true;
                }

                private static bool SetValue<T>(VolumeParameter<T> parameter, T value)
                {
                    if (EqualityComparer<T>.Default.Equals(parameter.value, value)) return false;
                    parameter.value = value;
                    return true;
                }

                private static string HashBytes(byte[] bytes)
                {
                    using (var sha = SHA256.Create())
                        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
                }

                internal static void ConfigureLightProbes(Transform parent)
                {
                    var probeObject = new GameObject("LightProbes");
                    probeObject.transform.SetParent(parent, false);
                    var positions = new List<Vector3>();
                    for (var yIndex = 0; yIndex < 5; yIndex++)
                    {
                        var y = new[] { 1.5f, 8f, 20f, 36f, 46f }[yIndex];
                        for (var x = -56f; x <= 56f; x += 16f)
                        {
                            for (var z = -36f; z <= 36f; z += 18f)
                            {
                                var position = new Vector3(x, y, z);
                                var overlaps = UnityEngine.Physics.OverlapSphere(position, 0.20f, ~0, QueryTriggerInteraction.Ignore);
                                var blocked = false;
                                for (var i = 0; i < overlaps.Length; i++)
                                {
                                    if (overlaps[i] != null && !overlaps[i].isTrigger)
                                    {
                                        blocked = true;
                                        break;
                                    }
                                }
                                if (!blocked) positions.Add(position);
                            }
                        }
                    }

                    var group = probeObject.AddComponent<LightProbeGroup>();
                    group.probePositions = positions.ToArray();
                }

                internal static void ConfigureReflectionProbes(Transform parent)
                {
                    for (var i = 0; i < ReflectionProbeContract.Length; i++)
                    {
                        var contract = ReflectionProbeContract[i];
                        var probeObject = new GameObject(contract.name);
                        probeObject.transform.SetParent(parent, false);
                        probeObject.transform.localPosition = contract.center;
                        var probe = probeObject.AddComponent<ReflectionProbe>();
                        probe.mode = ReflectionProbeMode.Baked;
                        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                        probe.resolution = 128;
                        probe.hdr = true;
                        probe.boxProjection = true;
                        probe.size = contract.size;
                        probe.center = Vector3.zero;
                        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                        probe.intensity = 1f;
                    }
                }

                internal static void MarkArenaStaticForLighting(GameObject arena)
                {
                    if (arena == null) return;
                    var renderers = arena.GetComponentsInChildren<MeshRenderer>(true);
                    var opaqueFlags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                      StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic |
                                      StaticEditorFlags.ReflectionProbeStatic;
                    var transparentFlags = StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        if (renderer == null) continue;
                        var owner = renderer.gameObject;
                        var transparent = owner.name.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          owner.name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0;
                        owner.isStatic = true;
                        GameObjectUtility.SetStaticEditorFlags(owner, transparent ? transparentFlags : opaqueFlags);
                        renderer.lightProbeUsage = transparent ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
                        renderer.reflectionProbeUsage = transparent ? ReflectionProbeUsage.Off : ReflectionProbeUsage.BlendProbes;
                        renderer.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.On;
                        renderer.receiveShadows = !transparent;
                    }
                }

                internal static Scene BakeSceneLighting(Scene scene, string passPath)
                {
                    return BakeSceneLighting(scene, passPath, MovementLabLightingProfiles.ProfileId.Production);
                }

                internal static Scene BakeSceneLighting(Scene scene, string passPath, MovementLabLightingProfiles.ProfileId profile)
                {
                    // Profile selection and validation happen before entering
                    // Unity's bake boundary. No setting/material repair or
                    // dirty/save call is permitted after Lightmapping.Bake
                    // starts.
                    if (string.IsNullOrWhiteSpace(passPath))
                        throw new InvalidOperationException("MovementLab lighting bake requires a profile-bound pre-bake pass record.");
                    if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal) ||
                        SceneManager.GetActiveScene().handle != scene.handle)
                        throw new InvalidOperationException("MovementLab lighting bake requires the prepared MovementLab scene to remain active.");
                    MovementLabLightingProfiles.LoadValidatedSettings(profile);
                    MovementLabLightingProfiles.ValidatePreparedScene(profile);
                    MovementLabPreBakeGate.RevalidatePassRecord(passPath, profile);
                    var revalidatedScene = SceneManager.GetActiveScene();
                    if (!revalidatedScene.IsValid() || !revalidatedScene.isLoaded ||
                        !string.Equals(revalidatedScene.path, MovementLabContract.ScenePath, StringComparison.Ordinal) ||
                        SceneManager.GetActiveScene().handle != revalidatedScene.handle)
                        throw new InvalidOperationException("MovementLab lighting bake requires revalidation to leave the prepared MovementLab scene loaded and active.");
                    MovementLabLightingProfiles.ValidatePreparedScene(profile);
                    var preservedProductionOnlyLightmaps = profile == MovementLabLightingProfiles.ProfileId.Development
                        ? CaptureProductionOnlyLightmaps()
                        : Array.Empty<PreservedBakedOutput>();
                    Exception bakeFailure = null;
                    try
                    {
                        var baked = Lightmapping.Bake();
                        if (!baked)
                            throw new InvalidOperationException("Lightmapping.Bake returned false for MovementLab.");

                        if (profile == MovementLabLightingProfiles.ProfileId.Development)
                            ValidateBakedLightmapTopology(MovementLabContract.DevelopmentLightmapCount);
                    }
                    catch (Exception exception)
                    {
                        bakeFailure = exception;
                        throw;
                    }
                    finally
                    {
                        if (preservedProductionOnlyLightmaps.Length > 0)
                        {
                            try
                            {
                                RestorePreservedBakedOutputs(preservedProductionOnlyLightmaps);
                            }
                            catch (Exception restoreException)
                            {
                                if (bakeFailure == null) throw;
                                Debug.LogException(new InvalidOperationException(
                                    "MovementLab development bake failed and production-only lightmap restoration also failed.",
                                    restoreException));
                            }
                        }
                    }

                    // Authoritative probe cubemaps are generated by the
                    // normal bake into the scene folder. The obsolete named
                    // probe loop/EXRs intentionally no longer exist.
                    return revalidatedScene;
                }

                private static PreservedBakedOutput[] CaptureProductionOnlyLightmaps()
                {
                    var productionLightmaps = MovementLabContract.BakedLightmapPaths(MovementLabContract.ExpectedLightmapCount);
                    var firstProductionOnlyIndex = MovementLabContract.DevelopmentLightmapCount * 3;
                    var preserved = new List<PreservedBakedOutput>();
                    for (var i = firstProductionOnlyIndex; i < productionLightmaps.Length; i++)
                    {
                        var assetPath = productionLightmaps[i];
                        var assetAbsolutePath = MovementLabManifestStore.ResolveProjectPath(assetPath);
                        var metaAbsolutePath = assetAbsolutePath + ".meta";
                        var assetExists = File.Exists(assetAbsolutePath);
                        var metaExists = File.Exists(metaAbsolutePath);
                        if (assetExists != metaExists)
                            throw new InvalidOperationException("MovementLab development bake cannot preserve a broken production-only lightmap pair: " + assetPath);
                        if (!assetExists) continue;

                        ValidateAssetMetaGuid(assetPath);
                        preserved.Add(new PreservedBakedOutput(assetAbsolutePath, File.ReadAllBytes(assetAbsolutePath)));
                        preserved.Add(new PreservedBakedOutput(metaAbsolutePath, File.ReadAllBytes(metaAbsolutePath)));
                    }

                    var expectedOutputCount = (MovementLabContract.ExpectedLightmapCount - MovementLabContract.DevelopmentLightmapCount) * 3;
                    if (preserved.Count != 0 && preserved.Count != expectedOutputCount * 2)
                        throw new InvalidOperationException("MovementLab development bake requires all production-only lightmap outputs to be present or absent as complete asset/meta pairs.");
                    return preserved.ToArray();
                }

                private static void RestorePreservedBakedOutputs(PreservedBakedOutput[] preserved)
                {
                    for (var i = 0; i < preserved.Length; i++)
                        MovementLabAtomicFile.WriteAllBytesAtomic(preserved[i].Bytes, preserved[i].AbsolutePath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                private static void ValidateBakedLightmapTopology(int expectedCount)
                {
                    var lightmaps = LightmapSettings.lightmaps;
                    if (lightmaps == null || lightmaps.Length != expectedCount)
                        throw new InvalidOperationException("MovementLab lightmap atlas count invalid: expected " + expectedCount + ".");

                    var expectedPaths = MovementLabContract.BakedLightmapPaths(expectedCount);
                    for (var i = 0; i < lightmaps.Length; i++)
                    {
                        var data = lightmaps[i];
                        if (data == null || data.lightmapDir == null || data.lightmapColor == null || data.shadowMask == null)
                            throw new InvalidOperationException("MovementLab lightmap data entry is incomplete: " + i);

                        var pathIndex = i * 3;
                        if (!string.Equals(AssetDatabase.GetAssetPath(data.lightmapDir), expectedPaths[pathIndex], StringComparison.Ordinal) ||
                            !string.Equals(AssetDatabase.GetAssetPath(data.lightmapColor), expectedPaths[pathIndex + 1], StringComparison.Ordinal) ||
                            !string.Equals(AssetDatabase.GetAssetPath(data.shadowMask), expectedPaths[pathIndex + 2], StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("MovementLab lightmap data reference is stale: " + i);
                        }
                    }
                }

                private readonly struct PreservedBakedOutput
                {
                    internal readonly string AbsolutePath;
                    internal readonly byte[] Bytes;

                    internal PreservedBakedOutput(string absolutePath, byte[] bytes)
                    {
                        AbsolutePath = absolutePath;
                        Bytes = bytes;
                    }
                }

                internal static void ValidateSceneEnvironment(Scene scene, GameObject arena, bool includeBakedLighting)
                {
                    var sun = GameObject.Find("Environment/Sun")?.GetComponent<Light>();
                    var sunData = sun != null ? sun.GetComponent<UniversalAdditionalLightData>() : null;
                    if (sun == null || sunData == null || sun.type != LightType.Directional || sun.lightmapBakeType != LightmapBakeType.Mixed ||
                        sun.shadows != LightShadows.Soft || Mathf.Abs(sun.intensity - ProductionSunIntensity) > 0.001f ||
                        Vector3.Distance(sun.transform.eulerAngles, ProductionSunEuler) > 0.1f ||
                        sun.color != SunColor || Mathf.Abs(sun.shadowStrength - ProductionSunShadowStrength) > 0.001f ||
                        Mathf.Abs(sun.shadowBias - 0.05f) > 0.001f || Mathf.Abs(sun.shadowNormalBias - 0.4f) > 0.001f)
                    {
                        throw new InvalidOperationException("MovementLab mixed sun contract invalid.");
                    }
                    MovementLabSerializedProperties.ValidatePersistentIdentity(sun, "Environment/Sun");
                    MovementLabSerializedProperties.ValidatePersistentIdentity(sunData, "Environment/Sun UniversalAdditionalLightData");

                    if (RenderSettings.ambientMode != AmbientMode.Trilight ||
                        RenderSettings.ambientSkyColor != ProductionAmbientSkyColor ||
                        RenderSettings.ambientEquatorColor != ProductionAmbientEquatorColor ||
                        RenderSettings.ambientGroundColor != ProductionAmbientGroundColor ||
                        Mathf.Abs(RenderSettings.ambientIntensity - ProductionAmbientIntensity) > 0.001f)
                    {
                        throw new InvalidOperationException("MovementLab Trilight ambient fill contract invalid.");
                    }

                    var sky = RenderSettings.skybox;
                    if (sky == null || sky.shader == null || sky.shader.name != "RocketFooxball/SunnyArenaSky" ||
                        sky.GetTexture("_Panorama") != LoadTexture(SkyTexturePath) ||
                        sky.GetColor("_HorizonColor") != SkyHorizonColor || sky.GetColor("_ZenithColor") != SkyZenithColor ||
                        sky.GetColor("_CloudTint") != SkyCloudColor || Mathf.Abs(sky.GetFloat("_CloudCoverage") - 0.22f) > 0.001f ||
                        Mathf.Abs(sky.GetFloat("_CloudSoftness") - 0.65f) > 0.001f ||
                        Vector3.Distance(sky.GetVector("_SunDirection"), -sun.transform.forward) > 0.001f ||
                        sky.GetColor("_SunColor") != SunColor || Mathf.Abs(sky.GetFloat("_SunAngularRadius") - 0.012f) > 0.001f ||
                        Mathf.Abs(sky.GetFloat("_SunIntensity") - 3f) > 0.001f)
                    {
                        throw new InvalidOperationException("Sunny sky material contract invalid.");
                    }

                    var accents = GameObject.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                    var accentCount = 0;
                    var wallFillCount = 0;
                    for (var i = 0; i < accents.Length; i++)
                    {
                        var accent = accents[i];
                        if (accent == null || accent == sun) continue;
                        var contractIndex = -1;
                        for (var j = 0; j < AccentLightContract.Length; j++)
                            if (accent.name == AccentLightContract[j].name) contractIndex = j;
                        if (contractIndex >= 0)
                        {
                            var contract = AccentLightContract[contractIndex];
                            var accentData = accent.GetComponent<UniversalAdditionalLightData>();
                            if (accentData == null || accent.type != LightType.Point || accent.shadows != LightShadows.None || accent.lightmapBakeType != LightmapBakeType.Realtime ||
                                Vector3.Distance(accent.transform.position, contract.position) > 0.001f || accent.color != contract.color ||
                                Mathf.Abs(accent.intensity - GoalAccentIntensity) > 0.01f || Mathf.Abs(accent.range - GoalAccentRange) > 0.001f)
                            {
                                throw new InvalidOperationException("Goal accent light contract invalid: " + accent.name);
                            }
                            MovementLabSerializedProperties.ValidatePersistentIdentity(accent, "Environment/" + accent.name);
                            MovementLabSerializedProperties.ValidatePersistentIdentity(accentData, "Environment/" + accent.name + " UniversalAdditionalLightData");
                            accentCount++;
                            continue;
                        }

                        for (var j = 0; j < WallFillLightContract.Length; j++)
                            if (accent.name == WallFillLightContract[j].name) contractIndex = j;
                        if (contractIndex < 0) throw new InvalidOperationException("Unexpected shadow/light source: " + accent.name);
                        var wallContract = WallFillLightContract[contractIndex];
                        var wallData = accent.GetComponent<UniversalAdditionalLightData>();
                        var expectedRotation = Quaternion.LookRotation(wallContract.target - wallContract.position);
                        if (wallData == null || accent.type != LightType.Spot || accent.shadows != LightShadows.Soft || accent.lightmapBakeType != LightmapBakeType.Baked ||
                            Vector3.Distance(accent.transform.position, wallContract.position) > 0.001f || Quaternion.Angle(accent.transform.rotation, expectedRotation) > 0.1f ||
                            accent.color != WallFillColor || Mathf.Abs(accent.intensity - WallFillIntensity) > 0.01f || Mathf.Abs(accent.range - WallFillRange) > 0.001f ||
                            Mathf.Abs(accent.spotAngle - WallFillOuterAngle) > 0.001f || Mathf.Abs(accent.innerSpotAngle - WallFillInnerAngle) > 0.001f ||
                            Mathf.Abs(accent.shadowStrength - 0.85f) > 0.001f)
                        {
                            throw new InvalidOperationException("Wall fill light contract invalid: " + accent.name);
                        }
                        MovementLabSerializedProperties.ValidatePersistentIdentity(accent, "Environment/" + accent.name);
                        MovementLabSerializedProperties.ValidatePersistentIdentity(wallData, "Environment/" + accent.name + " UniversalAdditionalLightData");
                        wallFillCount++;
                    }
                    if (accentCount != AccentLightContract.Length) throw new InvalidOperationException("Goal accent light count invalid.");
                    if (wallFillCount != WallFillLightContract.Length) throw new InvalidOperationException("Wall fill light count invalid.");

                    var volume = GameObject.Find("Environment/GlobalVolume")?.GetComponent<Volume>();
                    var expectedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                     if (volume == null || !volume.isGlobal || volume.sharedProfile == null || volume.sharedProfile != expectedProfile ||
                         !EditorUtility.IsPersistent(volume.sharedProfile) ||
                         !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(volume.sharedProfile, out _, out long profileLocalId) || profileLocalId == 0)
                         throw new InvalidOperationException("Global post Volume reference invalid.");
                     var volumeProfile = volume.sharedProfile;
                     ValidateVolumeProfileValues(volumeProfile);
                     var volumeComponents = volumeProfile.components;
                    var requiredVolumeTypes = new[] { typeof(Tonemapping), typeof(Bloom), typeof(ColorAdjustments) };
                    if (volumeComponents == null || volumeComponents.Count != requiredVolumeTypes.Length ||
                        requiredVolumeTypes.Any(type => volumeComponents.Count(component => component != null && component.GetType() == type) != 1) ||
                        volumeComponents.Any(component => component == null || !requiredVolumeTypes.Contains(component.GetType()) ||
                            !EditorUtility.IsPersistent(component) || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out _, out long componentLocalId) || componentLocalId == 0))
                    {
                        throw new InvalidOperationException("Global post VolumeProfile component persistence contract invalid.");
                    }
                     if (!volumeProfile.TryGet<Tonemapping>(out var tonemapping) || !tonemapping.active || !tonemapping.mode.overrideState || tonemapping.mode.value != TonemappingMode.ACES ||
                         !volumeProfile.TryGet<Bloom>(out var bloom) || !bloom.active || !bloom.threshold.overrideState || Mathf.Abs(bloom.threshold.value - BloomThreshold) > 0.001f ||
                         !bloom.intensity.overrideState || Mathf.Abs(bloom.intensity.value - BloomIntensity) > 0.001f || !bloom.scatter.overrideState || Mathf.Abs(bloom.scatter.value - BloomScatter) > 0.001f ||
                         !bloom.clamp.overrideState || Mathf.Abs(bloom.clamp.value - BloomClamp) > 0.001f || !bloom.highQualityFiltering.overrideState || bloom.highQualityFiltering.value != BloomHighQualityFiltering ||
                         !volumeProfile.TryGet<ColorAdjustments>(out var color) || !color.active || !color.contrast.overrideState || Mathf.Abs(color.contrast.value - ColorAdjustmentsContrast) > 0.001f ||
                         !color.saturation.overrideState || Mathf.Abs(color.saturation.value - ColorAdjustmentsSaturation) > 0.001f || !color.postExposure.overrideState || Mathf.Abs(color.postExposure.value - TonemappingPostExposure) > 0.001f)
                    {
                        throw new InvalidOperationException("Global Volume post contract invalid.");
                    }

                    var probeGroup = GameObject.Find("Environment/LightProbes")?.GetComponent<LightProbeGroup>();
                    if (probeGroup == null || probeGroup.probePositions == null)
                        throw new InvalidOperationException("Light probe lattice is missing.");
                    var probes = GameObject.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                    if (probes.Length != ReflectionProbeContract.Length) throw new InvalidOperationException("Reflection probe count invalid.");
                    var expectedLightingProfile = ResolveReadOnlyValidationProfile(includeBakedLighting, probeGroup.probePositions.Length, probes);
                    ValidateLightingManifest(includeBakedLighting, expectedLightingProfile);
                    var expectedReflectionResolution = expectedLightingProfile.ReflectionResolution;
                    for (var i = 0; i < ReflectionProbeContract.Length; i++)
                    {
                        var expected = ReflectionProbeContract[i];
                        var probeObject = GameObject.Find("Environment/" + expected.name);
                        var probe = probeObject != null ? probeObject.GetComponent<ReflectionProbe>() : null;
                        if (probe == null || probe.mode != ReflectionProbeMode.Baked || !probe.boxProjection || !probe.hdr || probe.resolution != expectedReflectionResolution ||
                            Vector3.Distance(probe.transform.position, expected.center) > 0.001f || Vector3.Distance(probe.size, expected.size) > 0.001f)
                            throw new InvalidOperationException("Reflection probe contract invalid: " + expected.name);
                    }

                    if (includeBakedLighting)
                    {
                        if (LightmapSettings.lightmaps == null || LightmapSettings.lightmaps.Length == 0)
                            throw new InvalidOperationException("MovementLab lightmap bake data is missing.");
                        if (LightmapSettings.lightmapsMode != LightmapsMode.CombinedDirectional)
                            throw new InvalidOperationException("MovementLab lightmaps must use directional mode.");
                        ValidatePersistedBakeOutputs(scene);
                    }

                    var renderers = scene.GetRootGameObjects();
                    var meshRenderers = 0;
                    var opaqueDraws = 0;
                    var transparentStatic = 0;
                    long sceneTriangles = 0;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var root = renderers[i];
                        var meshes = root.GetComponentsInChildren<MeshRenderer>(true);
                        meshRenderers += meshes.Length;
                        for (var j = 0; j < meshes.Length; j++)
                        {
                            var renderer = meshes[j];
                            if (renderer == null) continue;
                            var materials = renderer.sharedMaterials;
                            var transparent = renderer.gameObject.isStatic && (renderer.name.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0 || renderer.name.IndexOf("Shield", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (transparent) transparentStatic++;
                            for (var m = 0; m < materials.Length; m++)
                            {
                                var material = materials[m];
                                if (material == null) continue;
                                if (!transparent && material.shader != null && material.shader.name == LitShaderName) opaqueDraws++;
                            }
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (filter != null && filter.sharedMesh != null) sceneTriangles += filter.sharedMesh.triangles.Length / 3;
                        }
                        var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                        for (var j = 0; j < skinned.Length; j++) if (skinned[j] != null && skinned[j].sharedMesh != null) sceneTriangles += skinned[j].sharedMesh.triangles.Length / 3;
                        meshRenderers += skinned.Length;
                    }
                    Debug.Log("Rocket Fooxball Movement Lab render budget: triangles=" + sceneTriangles + " MeshRenderers=" + meshRenderers + " opaqueDraws=" + opaqueDraws + " staticTransparent=" + transparentStatic);
                }

                private static MovementLabLightingProfiles.Specification ResolveReadOnlyValidationProfile(bool includeBakedLighting, int probeCount, ReflectionProbe[] probes)
                {
                    if (includeBakedLighting)
                    {
                        if (probeCount != MovementLabLightingProfiles.Production.ProbeCount ||
                            probes.Any(probe => probe == null || probe.resolution != MovementLabLightingProfiles.Production.ReflectionResolution))
                            throw new InvalidOperationException("MovementLab baked lighting requires the exact production probe profile.");
                        return MovementLabLightingProfiles.Production;
                    }

                    if (probeCount == MovementLabLightingProfiles.Development.ProbeCount &&
                        probes.All(probe => probe != null && probe.resolution == MovementLabLightingProfiles.Development.ReflectionResolution))
                        return MovementLabLightingProfiles.Development;
                    if (probeCount == MovementLabLightingProfiles.Production.ProbeCount &&
                        probes.All(probe => probe != null && probe.resolution == MovementLabLightingProfiles.Production.ReflectionResolution))
                        return MovementLabLightingProfiles.Production;

                    throw new InvalidOperationException("MovementLab scene probe profile must be exact development (80/64) or production (200/128).");
                }

                private static void ValidateLightingManifest(bool includeBakedLighting, MovementLabLightingProfiles.Specification expectedProfile)
                {
                    var path = MovementLabManifestStore.ResolveProjectPath(MovementLabContract.LightingManifestPath);
                    if (!File.Exists(path))
                    {
                        if (includeBakedLighting) throw new InvalidOperationException("MovementLab production lighting manifest is missing; run the explicit production bake.");
                        return;
                    }

                    MovementLabLightingManifestState manifest;
                    try { manifest = JsonUtility.FromJson<MovementLabLightingManifestState>(File.ReadAllText(path)); }
                    catch (Exception exception) { throw new InvalidOperationException("MovementLab lighting manifest could not be parsed: " + exception.Message, exception); }
                    if (manifest == null || manifest.schemaVersion != 2 || string.IsNullOrWhiteSpace(manifest.profileId) ||
                        string.IsNullOrWhiteSpace(manifest.profileTag) || string.IsNullOrWhiteSpace(manifest.unityVersion) ||
                        string.IsNullOrWhiteSpace(manifest.lightingInputDigest) || manifest.outputPaths == null ||
                        manifest.outputHashes == null || manifest.outputPaths.Length != manifest.outputHashes.Length)
                    {
                        throw new InvalidOperationException("MovementLab lighting manifest is incomplete or structurally invalid.");
                    }

                    if (!includeBakedLighting)
                    {
                        var manifestProfile = string.Equals(manifest.profileTag, MovementLabLightingProfiles.Development.Tag, StringComparison.OrdinalIgnoreCase)
                            ? MovementLabLightingProfiles.Development :
                            string.Equals(manifest.profileTag, MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase)
                                ? MovementLabLightingProfiles.Production : null;
                        if (manifestProfile == null)
                            throw new InvalidOperationException("MovementLab lighting manifest profile tag is invalid: " + manifest.profileTag);
                        if (!string.Equals(manifest.profileId, manifestProfile.Id.ToString(), StringComparison.Ordinal))
                            throw new InvalidOperationException("MovementLab lighting manifest profile ID does not match its profile tag: id=" + manifest.profileId + ", tag=" + manifest.profileTag + ".");
                        ValidateManifestSpecification(manifest, manifestProfile);
                        return;
                    }

                    if (!string.Equals(manifest.profileTag, MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(manifest.profileId, MovementLabLightingProfiles.ProfileId.Production.ToString(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("MovementLab validation requires a production lighting manifest; run 'Rocket Fooxball/Bake Movement Lab Lighting' explicitly. Current profile=" + manifest.profileTag + ".");
                    }
                    if (!string.Equals(manifest.unityVersion, Application.unityVersion, StringComparison.Ordinal))
                        throw new InvalidOperationException("MovementLab lighting manifest Unity version mismatch.");
                    ValidateManifestSpecification(manifest, expectedProfile);
                }

                private static void ValidateManifestSpecification(MovementLabLightingManifestState manifest, MovementLabLightingProfiles.Specification expected)
                {
                    if (manifest.specificationProbeCount != expected.ProbeCount || manifest.specificationReflectionResolution != expected.ReflectionResolution ||
                        manifest.specificationLightmapResolution != expected.LightmapResolution || manifest.specificationMinBounces != expected.MinBounces ||
                        manifest.specificationMaxBounces != expected.MaxBounces || manifest.specificationDirectSamples != expected.DirectSamples ||
                        manifest.specificationIndirectSamples != expected.IndirectSamples || manifest.specificationEnvironmentSamples != expected.EnvironmentSamples ||
                        manifest.specificationSampleMultiplier != expected.SampleMultiplier || manifest.actualProbeCount != expected.ProbeCount ||
                        manifest.actualReflectionResolution != expected.ReflectionResolution || manifest.actualLightmapResolution != expected.LightmapResolution ||
                        manifest.actualMinBounces != expected.MinBounces || manifest.actualMaxBounces != expected.MaxBounces ||
                        manifest.actualDirectSamples != expected.DirectSamples || manifest.actualIndirectSamples != expected.IndirectSamples ||
                        manifest.actualEnvironmentSamples != expected.EnvironmentSamples || manifest.actualSampleMultiplier != expected.SampleMultiplier)
                    {
                        throw new InvalidOperationException("MovementLab lighting manifest settings do not match " + expected.Tag + " profile.");
                    }
                }

                internal static void ValidatePersistedBakeOutputs(Scene scene)
                {
                    if (GeneratedBakedLightingPaths.Length != 1 + (ExpectedLightmapCount * 3) + ExpectedReflectionProbeBakeCount)
                    {
                        throw new InvalidOperationException("MovementLab baked lighting path contract is invalid.");
                    }

                    var projectRoot = ResolveProjectRoot();
                    var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < GeneratedBakedLightingPaths.Length; i++)
                    {
                        var relativePath = NormalizeRepositoryRelativePath(GeneratedBakedLightingPaths[i]);
                        expectedPaths.Add(relativePath);
                        expectedPaths.Add(relativePath + ".meta");
                        var absolutePath = GetAbsoluteProjectPath(projectRoot, relativePath);
                        if (!File.Exists(absolutePath))
                        {
                            throw new InvalidOperationException("Missing persisted MovementLab bake output: " + relativePath);
                        }
                        if (!File.Exists(absolutePath + ".meta"))
                        {
                            throw new InvalidOperationException("Missing persisted MovementLab bake output metadata: " + relativePath + ".meta");
                        }
                        ValidateAssetMetaGuid(relativePath);
                    }

                    var bakedLightingDirectory = GetAbsoluteProjectPath(projectRoot, BakedLightingPath);
                    var persistedFiles = Directory.GetFiles(bakedLightingDirectory);
                    for (var i = 0; i < persistedFiles.Length; i++)
                    {
                        var persistedPath = persistedFiles[i].Substring(projectRoot.FullName.Length + 1).Replace('\\', '/');
                        var fileName = Path.GetFileName(persistedPath);
                        var isBakedOutput = string.Equals(fileName, "LightingData.asset", StringComparison.Ordinal) ||
                                            string.Equals(fileName, "LightingData.asset.meta", StringComparison.Ordinal) ||
                                            fileName.StartsWith("Lightmap-", StringComparison.Ordinal) ||
                                            fileName.StartsWith("ReflectionProbe-", StringComparison.Ordinal);
                        if (isBakedOutput && !expectedPaths.Contains(persistedPath))
                        {
                            throw new InvalidOperationException("Unexpected persisted MovementLab bake output: " + persistedPath);
                        }
                    }

                    var lightmaps = LightmapSettings.lightmaps;
                    if (lightmaps == null || lightmaps.Length != ExpectedLightmapCount)
                    {
                        throw new InvalidOperationException("MovementLab lightmap atlas count invalid: expected " + ExpectedLightmapCount + ".");
                    }

                    for (var i = 0; i < lightmaps.Length; i++)
                    {
                        var data = lightmaps[i];
                        if (data == null || data.lightmapDir == null || data.lightmapColor == null || data.shadowMask == null)
                        {
                            throw new InvalidOperationException("MovementLab lightmap data entry is incomplete: " + i);
                        }

                        var pathIndex = 1 + (i * 3);
                        var expectedDirectionPath = NormalizeRepositoryRelativePath(GeneratedBakedLightingPaths[pathIndex]);
                        var expectedColorPath = NormalizeRepositoryRelativePath(GeneratedBakedLightingPaths[pathIndex + 1]);
                        var expectedShadowMaskPath = NormalizeRepositoryRelativePath(GeneratedBakedLightingPaths[pathIndex + 2]);
                        if (!string.Equals(AssetDatabase.GetAssetPath(data.lightmapDir), expectedDirectionPath, StringComparison.Ordinal) ||
                            !string.Equals(AssetDatabase.GetAssetPath(data.lightmapColor), expectedColorPath, StringComparison.Ordinal) ||
                            !string.Equals(AssetDatabase.GetAssetPath(data.shadowMask), expectedShadowMaskPath, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("MovementLab lightmap data reference is stale: " + i);
                        }
                    }

                    var lightingDataGuid = AssetDatabase.AssetPathToGUID(GeneratedBakedLightingPaths[0]);
                    if (string.IsNullOrEmpty(lightingDataGuid))
                    {
                        throw new InvalidOperationException("MovementLab lighting data asset GUID is missing.");
                    }

                    var sceneYamlPath = GetAbsoluteProjectPath(projectRoot, scene.path);
                    var sceneYaml = File.ReadAllText(sceneYamlPath);
                    var lightingDataReference = "guid: " + lightingDataGuid + ",";
                    var lightingDataReferenceCount = 0;
                    var sceneLines = sceneYaml.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (var i = 0; i < sceneLines.Length; i++)
                    {
                        var line = sceneLines[i].TrimStart();
                        if (line.StartsWith("m_LightingDataAsset:", StringComparison.Ordinal) && line.IndexOf(lightingDataReference, StringComparison.Ordinal) >= 0)
                        {
                            lightingDataReferenceCount++;
                        }
                    }
                    if (lightingDataReferenceCount != 1)
                    {
                        throw new InvalidOperationException("MovementLab scene lighting data reference count invalid: " + lightingDataReferenceCount);
                    }

                    var lightingDependencies = AssetDatabase.GetDependencies(GeneratedBakedLightingPaths[0], true);
                    for (var i = 0; i < ExpectedReflectionProbeBakeCount; i++)
                    {
                        var expectedReflection = BakedLightingPath + "/ReflectionProbe-" + i + ".exr";
                        if (!lightingDependencies.Contains(expectedReflection))
                            throw new InvalidOperationException("MovementLab LightingData is missing direct reflection cubemap dependency: " + expectedReflection);
                    }
                }

                private static void ValidateAssetMetaGuid(string path)
                {
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    var metaPath = MovementLabManifestStore.ResolveProjectPath(path + ".meta");
                    if (string.IsNullOrEmpty(guid) || !File.Exists(metaPath))
                        throw new InvalidOperationException("MovementLab baked output GUID/meta is missing: " + path);
                    var metaGuid = File.ReadAllLines(metaPath)
                        .Select(line => line.Trim())
                        .Where(line => line.StartsWith("guid:", StringComparison.Ordinal))
                        .Select(line => line.Substring("guid:".Length).Trim())
                        .FirstOrDefault();
                    if (!string.Equals(guid, metaGuid, StringComparison.Ordinal))
                        throw new InvalidOperationException("MovementLab baked output GUID/meta mismatch: " + path);
                }

    }
}
