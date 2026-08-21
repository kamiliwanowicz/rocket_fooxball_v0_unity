using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
                private static readonly Color ProductionAmbientSkyColor = new Color(0.55f, 0.62f, 0.70f);
                private static readonly Color ProductionAmbientEquatorColor = new Color(0.42f, 0.46f, 0.50f);
                private static readonly Color ProductionAmbientGroundColor = new Color(0.24f, 0.27f, 0.30f);
                private const float ProductionAmbientIntensity = 1.35f;
                private const float ProductionSunIntensity = 1.1f;
                private const float ProductionSunShadowStrength = 0.55f;

                // Gameplay assembly owns scene objects and bindings only. The
                // sky material, VolumeProfile subassets, and LightingSettings
                // asset are authored by the lighting-owned pipeline methods
                // below and are loaded here without mutation.
                internal static void BindSceneEnvironment(Scene scene, ArenaBuild arena)
                {
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
                    sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                    sun.lightmapBakeType = LightmapBakeType.Mixed;
                    sun.shadows = LightShadows.Soft;
                    sun.shadowStrength = ProductionSunShadowStrength;
                    sun.shadowBias = 0.05f;
                    sun.shadowNormalBias = 0.4f;
                    sun.cullingMask = -1;

                    var skyMaterial = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
                    if (skyMaterial == null)
                    {
                        throw new InvalidOperationException("Lighting-owned sky material is missing: " + SkyMaterialPath);
                    }

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
                    BindExistingGlobalVolume(environment.transform);
                    ConfigureLightProbes(environment.transform);
                    ConfigureReflectionProbes(environment.transform);
                    MarkArenaStaticForLighting(arena.Root);
                    BindExistingLightingSettings(scene);
                }

                internal static Material GetOrCreateSkyMaterial(Light sun)
                {
                    var shader = Shader.Find("RocketFooxball/SunnyArenaSky");
                    if (shader == null)
                    {
                        throw new InvalidOperationException("SunnyArenaSky shader is unavailable.");
                    }

                    var material = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
                    if (material == null)
                    {
                        material = new Material(shader) { name = "RetroSunnySky" };
                        AssetDatabase.CreateAsset(material, SkyMaterialPath);
                    }

                    material.shader = shader;
                    material.SetTexture("_Panorama", LoadTexture(SkyTexturePath));
                    material.SetColor("_HorizonColor", SkyHorizonColor);
                    material.SetColor("_ZenithColor", SkyZenithColor);
                    material.SetColor("_CloudTint", SkyCloudColor);
                    material.SetFloat("_CloudCoverage", 0.22f);
                    material.SetFloat("_CloudSoftness", 0.65f);
                    material.SetVector("_SunDirection", -sun.transform.forward);
                    material.SetColor("_SunColor", SunColor);
                    material.SetFloat("_SunAngularRadius", 0.012f);
                    material.SetFloat("_SunIntensity", 3f);
                    material.SetColor("_FogHorizonColor", SkyHorizonColor);
                    material.SetFloat("_FogHorizonHeight", 0.02f);
                    material.SetFloat("_FogHorizonWidth", 0.28f);
                    EditorUtility.SetDirty(material);
                    return material;
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
                        light.intensity = 500f;
                        light.range = 14f;
                        light.shadows = LightShadows.None;
                        light.lightmapBakeType = LightmapBakeType.Realtime;
                    }
                }

                internal static void ConfigureGlobalVolume(Transform parent)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                    if (profile == null)
                    {
                        profile = ScriptableObject.CreateInstance<VolumeProfile>();
                        profile.name = "MovementLabVolumeProfile";
                        AssetDatabase.CreateAsset(profile, VolumeProfilePath);
                    }

                    VolumeComponent[] stale = profile.components.ToArray();
                    for (var i = 0; i < stale.Length; i++)
                    {
                        if (stale[i] != null)
                        {
                            profile.Remove(stale[i].GetType());
                            UnityEngine.Object.DestroyImmediate(stale[i], true);
                        }
                    }
                    profile.components.Clear();

                    var tonemapping = AddPersistentVolumeComponent<Tonemapping>(profile);
                    tonemapping.active = true;
                    tonemapping.mode.value = TonemappingMode.ACES;
                    tonemapping.mode.overrideState = true;

                    var bloom = AddPersistentVolumeComponent<Bloom>(profile);
                    bloom.active = true;
                    bloom.threshold.value = 1.1f;
                    bloom.threshold.overrideState = true;
                    bloom.intensity.value = 0.20f;
                    bloom.intensity.overrideState = true;
                    bloom.scatter.value = 0.60f;
                    bloom.scatter.overrideState = true;
                    bloom.clamp.value = 10f;
                    bloom.clamp.overrideState = true;
                    bloom.highQualityFiltering.value = false;
                    bloom.highQualityFiltering.overrideState = true;

                    var color = AddPersistentVolumeComponent<ColorAdjustments>(profile);
                    color.active = true;
                    color.postExposure.value = 0f;
                    color.postExposure.overrideState = true;
                    color.contrast.value = 5f;
                    color.contrast.overrideState = true;
                    color.saturation.value = 4f;
                    color.saturation.overrideState = true;

                    var volumeObject = new GameObject("GlobalVolume");
                    volumeObject.transform.SetParent(parent, false);
                    var volume = volumeObject.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 0f;
                    // Editor-authored profile must use sharedProfile so serialized
                    // scene YAML retains nonzero GUID/fileID reference.
                    volume.sharedProfile = profile;
                    EditorUtility.SetDirty(profile);
                    EditorUtility.SetDirty(volume);
                }

                internal static T AddPersistentVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
                {
                    var component = profile.Add<T>();
                    if (component == null)
                    {
                        throw new InvalidOperationException("Unable to create Volume component: " + typeof(T).Name);
                    }

                    if (AssetDatabase.GetAssetPath(component) != VolumeProfilePath)
                    {
                        component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                        AssetDatabase.AddObjectToAsset(component, profile);
                    }
                    EditorUtility.SetDirty(component);
                    return component;
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

                internal static LightingSettings ConfigureLightingSettings(Scene scene)
                {
                    var settings = MovementLabLightingProfiles.EnsurePersistedProductionSettings();
                    Lightmapping.SetLightingSettingsForScene(scene, settings);
                    return settings;
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
                    var baked = Lightmapping.Bake();
                    if (!baked)
                    {
                        throw new InvalidOperationException("Lightmapping.Bake returned false for MovementLab.");
                    }

                    // Authoritative probe cubemaps are generated by the
                    // normal bake into the scene folder. The obsolete named
                    // probe loop/EXRs intentionally no longer exist.
                    return revalidatedScene;
                }

                internal static void ValidateSceneEnvironment(Scene scene, GameObject arena, bool includeBakedLighting)
                {
                    var sun = GameObject.Find("Environment/Sun")?.GetComponent<Light>();
                    var sunData = sun != null ? sun.GetComponent<UniversalAdditionalLightData>() : null;
                    if (sun == null || sunData == null || sun.type != LightType.Directional || sun.lightmapBakeType != LightmapBakeType.Mixed ||
                        sun.shadows != LightShadows.Soft || Mathf.Abs(sun.intensity - ProductionSunIntensity) > 0.001f ||
                        Vector3.Distance(sun.transform.eulerAngles, new Vector3(50f, 330f, 0f)) > 0.1f ||
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
                    for (var i = 0; i < accents.Length; i++)
                    {
                        var accent = accents[i];
                        if (accent == null || accent == sun) continue;
                        var contractIndex = -1;
                        for (var j = 0; j < AccentLightContract.Length; j++)
                            if (accent.name == AccentLightContract[j].name) contractIndex = j;
                        if (contractIndex < 0) throw new InvalidOperationException("Unexpected shadow/light source: " + accent.name);
                        var contract = AccentLightContract[contractIndex];
                        var accentData = accent.GetComponent<UniversalAdditionalLightData>();
                        if (accentData == null || accent.type != LightType.Point || accent.shadows != LightShadows.None || accent.lightmapBakeType != LightmapBakeType.Realtime ||
                            Vector3.Distance(accent.transform.position, contract.position) > 0.001f || accent.color != contract.color ||
                            Mathf.Abs(accent.intensity - 500f) > 0.01f || Mathf.Abs(accent.range - 14f) > 0.001f)
                        {
                            throw new InvalidOperationException("Goal accent light contract invalid: " + accent.name);
                        }
                        MovementLabSerializedProperties.ValidatePersistentIdentity(accent, "Environment/" + accent.name);
                        MovementLabSerializedProperties.ValidatePersistentIdentity(accentData, "Environment/" + accent.name + " UniversalAdditionalLightData");
                        accentCount++;
                    }
                    if (accentCount != AccentLightContract.Length) throw new InvalidOperationException("Goal accent light count invalid.");

                    var volume = GameObject.Find("Environment/GlobalVolume")?.GetComponent<Volume>();
                    var expectedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
                    if (volume == null || !volume.isGlobal || volume.sharedProfile == null || volume.sharedProfile != expectedProfile ||
                        !EditorUtility.IsPersistent(volume.sharedProfile) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(volume.sharedProfile, out _, out long profileLocalId) || profileLocalId == 0)
                        throw new InvalidOperationException("Global post Volume reference invalid.");
                    var volumeProfile = volume.sharedProfile;
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
                        !volumeProfile.TryGet<Bloom>(out var bloom) || !bloom.active || !bloom.threshold.overrideState || Mathf.Abs(bloom.threshold.value - 1.1f) > 0.001f ||
                        !bloom.intensity.overrideState || Mathf.Abs(bloom.intensity.value - 0.20f) > 0.001f || !bloom.scatter.overrideState || Mathf.Abs(bloom.scatter.value - 0.60f) > 0.001f ||
                        !bloom.clamp.overrideState || Mathf.Abs(bloom.clamp.value - 10f) > 0.001f || !bloom.highQualityFiltering.overrideState || bloom.highQualityFiltering.value ||
                        !volumeProfile.TryGet<ColorAdjustments>(out var color) || !color.active || !color.contrast.overrideState || Mathf.Abs(color.contrast.value - 5f) > 0.001f ||
                        !color.saturation.overrideState || Mathf.Abs(color.saturation.value - 4f) > 0.001f || !color.postExposure.overrideState || Mathf.Abs(color.postExposure.value) > 0.001f)
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
