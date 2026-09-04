using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabImportPipeline
    {
        private static readonly FieldInfo ModelImporterClipAnimationInternalIdField =
            typeof(ModelImporterClipAnimation).GetField("internalID", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Apply()
        {
            // Preserve importer settlement order: textures first, models second.
            ConfigureTextureImporters();
            ConfigureModelImporters();
        }
    }

    internal static partial class MovementLabImportPipeline
    {
        // Narrow weapon import entry point. Do not call Apply here: this
        // command is intentionally closed over the two FBX pairs and ten
        // atlas maps so source-art import cannot rewrite unrelated metas.
        internal static void ImportWeaponVisualAssets()
        {
            ConfigureStaticModelImporter(WeaponModelPath);
            ConfigureStaticModelImporter(FpsShotgunModelPath);
            ConfigureStaticModelImporter(ShotgunModelPath);
            ConfigureLauncherTextureImporter(LauncherBaseColorTexturePath, true, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherNormalTexturePath, false, TextureImporterType.NormalMap);
            ConfigureLauncherTextureImporter(LauncherMetallicTexturePath, false, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherOcclusionTexturePath, false, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherEmissionTexturePath, false, TextureImporterType.Default);
            ConfigureShotgunTextureImporter(ShotgunBaseColorTexturePath, true, TextureImporterType.Default);
            ConfigureShotgunTextureImporter(ShotgunNormalTexturePath, false, TextureImporterType.NormalMap);
            ConfigureShotgunTextureImporter(ShotgunMetallicTexturePath, false, TextureImporterType.Default);
            ConfigureShotgunTextureImporter(ShotgunOcclusionTexturePath, false, TextureImporterType.Default);
            ConfigureShotgunTextureImporter(ShotgunEmissionTexturePath, false, TextureImporterType.Default);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        // Compatibility facade retained for existing T3 automation and menu
        // invocations. Both launcher and shotgun assets are imported.
        internal static void ImportLauncherVisualAssets() => ImportWeaponVisualAssets();

        private static void ConfigureLauncherTextureImporter(string path, bool sRgb, TextureImporterType textureType)
        {
            ConfigureTextureImporter(path, LauncherAtlasSize, sRgb, textureType,
                TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
        }

        private static void ConfigureShotgunTextureImporter(string path, bool sRgb, TextureImporterType textureType)
        {
            ConfigureTextureImporter(path, ShotgunAtlasSize, sRgb, textureType,
                TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
        }

                internal static void ConfigureTextureImporters()
                {
                    ConfigureTextureImporter(GrassTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(GrassNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(GrassMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(GrassOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WallTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WallNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WallMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WallOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(TrimTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(TrimNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(TrimMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(TrimOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(HazardTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(HazardNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(HazardMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(HazardOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(DetailNormalTexturePath, 512, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(BallTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(BallNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(BallMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(BallOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponMetalTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponMetalNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponMetalMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponMetalOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponDarkTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponDarkNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponDarkMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponDarkOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponAccentTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponAccentNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponAccentMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponAccentOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(WeaponAccentEmissionTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureShotgunTextureImporter(ShotgunBaseColorTexturePath, true, TextureImporterType.Default);
                    ConfigureShotgunTextureImporter(ShotgunNormalTexturePath, false, TextureImporterType.NormalMap);
                    ConfigureShotgunTextureImporter(ShotgunMetallicTexturePath, false, TextureImporterType.Default);
                    ConfigureShotgunTextureImporter(ShotgunOcclusionTexturePath, false, TextureImporterType.Default);
                    ConfigureShotgunTextureImporter(ShotgunEmissionTexturePath, false, TextureImporterType.Default);
                    ConfigureTextureImporter(WeaponMicroDetailNormalTexturePath, WeaponMicroDetailNormalMaxTextureSize, false, TextureImporterType.NormalMap,
                        TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketEmissionTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ConfigureTextureImporter(RocketGlowTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ConfigureTextureImporter(ShieldTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ConfigureTextureImporter(ExplosionTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ConfigureTextureImporter(SmokeTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    // Sky panorama is a regular sRGB 2D texture. Preserve the
                    // accepted repeat/bilinear importer contract used by the
                    // SunnyArenaSky material while making its metadata explicit
                    // in the importer stage.
                    ConfigureTextureImporter(SkyTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Bilinear, 1);
                }

                internal static void ConfigureTextureImporter(string path, int maxSize, bool sRgb, TextureImporterType textureType, TextureWrapMode wrapU, TextureWrapMode wrapV, FilterMode filterMode, int anisoLevel)
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException("Missing texture importer: " + path);
                    }

                    var changed = false;
                    if (importer.sRGBTexture != sRgb) { importer.sRGBTexture = sRgb; changed = true; }
                    if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
                    if (importer.textureType != textureType) { importer.textureType = textureType; changed = true; }
                    if (importer.filterMode != filterMode) { importer.filterMode = filterMode; changed = true; }
                    if (importer.anisoLevel != anisoLevel) { importer.anisoLevel = anisoLevel; changed = true; }
                    if (importer.maxTextureSize != maxSize) { importer.maxTextureSize = maxSize; changed = true; }
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    if (!settings.ignoreMipmapLimit) { settings.ignoreMipmapLimit = true; importer.SetTextureSettings(settings); changed = true; }
                    if (importer.wrapModeU != wrapU || importer.wrapModeV != wrapV || importer.wrapModeW != TextureWrapMode.Clamp)
                    {
                        importer.wrapModeU = wrapU;
                        importer.wrapModeV = wrapV;
                        importer.wrapModeW = TextureWrapMode.Clamp;
                        changed = true;
                    }
                    var platform = importer.GetDefaultPlatformTextureSettings();
                    if (platform.maxTextureSize != maxSize || platform.overridden || platform.textureCompression != TextureImporterCompression.CompressedHQ)
                    {
                        platform.maxTextureSize = maxSize;
                        platform.textureCompression = TextureImporterCompression.CompressedHQ;
                        platform.overridden = false;
                        importer.SetPlatformTextureSettings(platform);
                        changed = true;
                    }
                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }

                internal static void ConfigureModelImporters()
                {
                    ConfigureRigModelImporter(CharacterModelPath, true);
                    ConfigureRigModelImporter(FpsKickModelPath, false);
                    ConfigureStaticModelImporter(WeaponModelPath);
                    ConfigureStaticModelImporter(FpsShotgunModelPath);
                    ConfigureStaticModelImporter(ShotgunModelPath);
                    ConfigureStaticModelImporter(RocketModelPath);
                    ConfigureStaticModelImporter(ArenaKitModelPath);
                }

                internal static void ConfigureRigModelImporter(string path, bool character)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException("Missing model importer: " + path);
                    }

                    var changed = false;
                    if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
                    if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
                    if (Mathf.Abs(importer.globalScale - 1f) > 0.0001f) { importer.globalScale = 1f; changed = true; }
                    if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
                    if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
                    if (importer.optimizeGameObjects) { importer.optimizeGameObjects = false; changed = true; }

                    var sourceClips = importer.clipAnimations;
                    if (sourceClips == null || sourceClips.Length == 0)
                    {
                        sourceClips = importer.defaultClipAnimations;
                    }
                    if (sourceClips == null || sourceClips.Length == 0)
                    {
                        // Unity may not expose FBX takes through defaultClipAnimations
                        // until clipAnimations is explicitly seeded. Use deterministic
                        // source ranges authored by the generators as a fallback.
                        var idle = new ModelImporterClipAnimation
                        {
                            name = MovementLabContract.IdleStateName,
                            takeName = MovementLabContract.IdleStateName,
                            firstFrame = MovementLabContract.KickStartFrame,
                            lastFrame = character ? 30f : 31f
                        };
                        var kick = new ModelImporterClipAnimation
                        {
                            name = MovementLabContract.KickStateName,
                            takeName = MovementLabContract.KickStateName,
                            firstFrame = MovementLabContract.KickStartFrame,
                            lastFrame = MovementLabContract.KickEndFrame
                        };
                        sourceClips = new[] { idle, kick };
                    }

                    var expectedNames = character
                        ? new[] { MovementLabContract.IdleStateName, MovementLabContract.RunStateName, MovementLabContract.JumpStateName, MovementLabContract.FallStateName, MovementLabContract.LandStateName, MovementLabContract.KickStateName }
                        : new[] { MovementLabContract.IdleStateName, MovementLabContract.KickStateName };
                    var expectedLoops = character ? new[] { true, true, false, false, false, false } : new[] { true, false };
                    var expectedStarts = character
                        ? new[] { 1f, 1f, 1f, 1f, 1f, (float)MovementLabContract.WorldKickStartFrame }
                        : new[] { 1f, (float)MovementLabContract.FpsKickStartFrame };
                    var expectedEnds = character
                        ? new[] { 30f, 20f, 12f, 15f, 10f, (float)MovementLabContract.WorldKickEndFrame }
                        : new[] { 31f, (float)MovementLabContract.FpsKickEndFrame };
                    var clips = new List<ModelImporterClipAnimation>();
                    for (var expectedIndex = 0; expectedIndex < expectedNames.Length; expectedIndex++)
                    {
                        var source = FindSourceClip(sourceClips, expectedNames[expectedIndex]);
                        if (source == null)
                        {
                            source = new ModelImporterClipAnimation();
                        }
                        var sourceTakeName = ResolveImportedSourceTakeName(
                            importer,
                            expectedNames[expectedIndex]);
                        source.name = expectedNames[expectedIndex];
                        source.takeName = sourceTakeName;
                        source.firstFrame = expectedStarts[expectedIndex];
                        source.lastFrame = expectedEnds[expectedIndex];
                        source.cycleOffset = 0f;
                        source.loopTime = expectedLoops[expectedIndex];
                        source.lockRootRotation = true;
                        source.keepOriginalOrientation = true;
                        source.lockRootHeightY = true;
                        source.keepOriginalPositionY = true;
                        source.lockRootPositionXZ = true;
                        source.keepOriginalPositionXZ = true;
                        source.heightFromFeet = false;
                        source.hasAdditiveReferencePose = false;
                        source.mirror = false;
                        clips.Add(source);
                    }
                    var configured = clips.ToArray();
                    if (!ClipsEqual(importer.clipAnimations, configured))
                    {
                        importer.clipAnimations = configured;
                        changed = true;
                    }
                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }

                private static string ResolveImportedSourceTakeName(ModelImporter importer, string logicalName)
                {
                    var importedTakeInfos = importer.importedTakeInfos;
                    var matches = new List<string>();
                    if (importedTakeInfos != null)
                    {
                        for (var i = 0; i < importedTakeInfos.Length; i++)
                        {
                            var take = importedTakeInfos[i];
                            var sourceTakeName = take.name;
                            if (string.IsNullOrEmpty(sourceTakeName)) continue;
                            if (IsExactOrDelimiterSafeTake(sourceTakeName, logicalName))
                            {
                                matches.Add(sourceTakeName);
                            }
                        }
                    }

                    if (matches.Count == 0)
                    {
                        throw new InvalidOperationException("Model importer has no source take matching " + importer.assetPath + "/" + logicalName + ".");
                    }
                    if (matches.Count > 1)
                    {
                        throw new InvalidOperationException("Model importer has ambiguous source takes matching " + importer.assetPath + "/" + logicalName + ": " + string.Join(", ", matches) + ".");
                    }
                    return matches[0];
                }

                private static ModelImporterClipAnimation FindSourceClip(ModelImporterClipAnimation[] sourceClips, string expected)
                {
                    // Exact name/take matches win even when a prefixed suffix
                    // appears earlier in Unity's source clip ordering.
                    for (var sourceIndex = 0; sourceIndex < sourceClips.Length; sourceIndex++)
                    {
                        var source = sourceClips[sourceIndex];
                        if (source == null) continue;
                        if (string.Equals(source.name, expected, StringComparison.Ordinal) ||
                            string.Equals(source.takeName, expected, StringComparison.Ordinal)) return source;
                    }
                    for (var sourceIndex = 0; sourceIndex < sourceClips.Length; sourceIndex++)
                    {
                        var source = sourceClips[sourceIndex];
                        if (source == null) continue;
                        if (IsExactOrDelimiterSafeTake(source.name ?? string.Empty, expected) ||
                            IsExactOrDelimiterSafeTake(source.takeName ?? string.Empty, expected)) return source;
                    }
                    return null;
                }

                private static bool IsExactOrDelimiterSafeTake(string candidate, string expected)
                {
                    if (string.Equals(candidate, expected, StringComparison.Ordinal)) return true;
                    if (string.IsNullOrEmpty(candidate) || candidate.Length <= expected.Length ||
                        !candidate.EndsWith(expected, StringComparison.Ordinal)) return false;
                    return !char.IsLetterOrDigit(candidate[candidate.Length - expected.Length - 1]);
                }

                internal static bool ClipsEqual(ModelImporterClipAnimation[] a, ModelImporterClipAnimation[] b)
                {
                    if (a == null || b == null || a.Length != b.Length) return false;
                    for (var i = 0; i < a.Length; i++)
                    {
                        if (a[i].name != b[i].name || a[i].takeName != b[i].takeName || Mathf.Abs(a[i].firstFrame - b[i].firstFrame) > 0.001f || Mathf.Abs(a[i].lastFrame - b[i].lastFrame) > 0.001f || Mathf.Abs(a[i].cycleOffset - b[i].cycleOffset) > 0.001f || a[i].loopTime != b[i].loopTime || a[i].lockRootRotation != b[i].lockRootRotation || a[i].keepOriginalOrientation != b[i].keepOriginalOrientation || a[i].lockRootHeightY != b[i].lockRootHeightY || a[i].keepOriginalPositionY != b[i].keepOriginalPositionY || a[i].lockRootPositionXZ != b[i].lockRootPositionXZ || a[i].keepOriginalPositionXZ != b[i].keepOriginalPositionXZ || a[i].heightFromFeet != b[i].heightFromFeet || a[i].hasAdditiveReferencePose != b[i].hasAdditiveReferencePose || a[i].mirror != b[i].mirror)
                        {
                            return false;
                        }
                    }
                    return true;
                }

                internal static void ConfigureStaticModelImporter(string path)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException("Missing model importer: " + path);
                    }
                    var changed = false;
                    if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; changed = true; }
                    if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
                    if (Mathf.Abs(importer.globalScale - 1f) > 0.0001f) { importer.globalScale = 1f; changed = true; }
                    if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
                    var tangentSpace = new SerializedObject(importer);
                    var normalImport = tangentSpace.FindProperty("normalImportMode");
                    var tangents = tangentSpace.FindProperty("tangentImportMode");
                    var secondaryUv = tangentSpace.FindProperty("generateSecondaryUV");
                    if (normalImport != null && normalImport.intValue != 0) { normalImport.intValue = 0; changed = true; }
                    if (tangents != null && tangents.intValue != 3) { tangents.intValue = 3; changed = true; }
                    var requireUv1 = path == ArenaKitModelPath;
                    if (secondaryUv != null && secondaryUv.boolValue != requireUv1) { secondaryUv.boolValue = requireUv1; changed = true; }
                    tangentSpace.ApplyModifiedPropertiesWithoutUndo();
                    if (changed)
                    {
                        importer.SaveAndReimport();
                    }
                }

                internal static Avatar FindImportedAvatar(string modelPath)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Avatar avatar) return avatar;
                    }
                    return null;
                }

                internal static Avatar[] FindImportedAvatars(string modelPath)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                    var avatars = new List<Avatar>();
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Avatar avatar) avatars.Add(avatar);
                    }
                    return avatars.ToArray();
                }

                internal static AnimationClip FindImportedClip(string modelPath, string name)
                {
                    var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
                    if (importer == null)
                    {
                        throw new InvalidOperationException("Missing model importer for imported clip lookup: " + modelPath + ".");
                    }
                    if (string.IsNullOrEmpty(name))
                    {
                        throw new ArgumentException("Imported clip name must not be null or empty.", nameof(name));
                    }

                    var configuredClips = importer.clipAnimations;
                    if (configuredClips == null || configuredClips.Length == 0)
                    {
                        throw new InvalidOperationException("Model importer has no configured clipAnimations for imported clip lookup: " + modelPath + ".");
                    }

                    var exactMatches = new List<ModelImporterClipAnimation>();
                    for (var i = 0; i < configuredClips.Length; i++)
                    {
                        var configured = configuredClips[i];
                        if (configured == null ||
                            (!string.Equals(configured.name, name, StringComparison.Ordinal) &&
                             !string.Equals(configured.takeName, name, StringComparison.Ordinal)))
                        {
                            continue;
                        }
                        exactMatches.Add(configured);
                    }

                    ModelImporterClipAnimation selected = null;
                    if (exactMatches.Count > 1)
                    {
                        throw new InvalidOperationException("Model importer has ambiguous exact clip configuration for " + modelPath + "/" + name + ".");
                    }
                    if (exactMatches.Count == 1)
                    {
                        selected = exactMatches[0];
                    }
                    else
                    {
                        var suffixMatches = new List<ModelImporterClipAnimation>();
                        for (var i = 0; i < configuredClips.Length; i++)
                        {
                            var configured = configuredClips[i];
                            if (configured == null ||
                                (!IsDelimiterSafeTakeSuffix(configured.name, name) &&
                                 !IsDelimiterSafeTakeSuffix(configured.takeName, name)))
                            {
                                continue;
                            }
                            suffixMatches.Add(configured);
                        }

                        if (suffixMatches.Count > 1)
                        {
                            throw new InvalidOperationException("Model importer has ambiguous delimiter-safe clip configuration for " + modelPath + "/" + name + ".");
                        }
                        if (suffixMatches.Count == 1) selected = suffixMatches[0];
                    }

                    if (selected == null)
                    {
                        throw new InvalidOperationException("Model importer has no configured clip matching " + modelPath + "/" + name + ".");
                    }
                    var configuredInternalId = GetConfiguredClipInternalId(selected, modelPath + "/" + name);

                    var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                    var modelGuid = AssetDatabase.AssetPathToGUID(modelPath);
                    var matches = new List<AnimationClip>();
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (!(assets[i] is AnimationClip clip)) continue;
                        if (!string.Equals(AssetDatabase.GetAssetPath(clip), modelPath, StringComparison.Ordinal)) continue;
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out var clipGuid, out long clipLocalId)) continue;
                        if (!string.Equals(clipGuid, modelGuid, StringComparison.Ordinal) || clipLocalId != configuredInternalId) continue;
                        matches.Add(clip);
                    }
                    if (matches.Count == 0)
                    {
                        throw new InvalidOperationException("No imported AnimationClip matched configured internalID " + configuredInternalId + " for " + modelPath + "/" + name + ".");
                    }
                    if (matches.Count > 1)
                    {
                        throw new InvalidOperationException("Multiple imported AnimationClips matched configured internalID " + configuredInternalId + " for " + modelPath + "/" + name + ".");
                    }
                    return matches[0];
                }

                private static long GetConfiguredClipInternalId(ModelImporterClipAnimation clip, string label)
                {
                    if (ModelImporterClipAnimationInternalIdField == null ||
                        ModelImporterClipAnimationInternalIdField.FieldType != typeof(long))
                    {
                        throw new InvalidOperationException("ModelImporterClipAnimation.internalID reflection contract is unavailable; expected a private Int64 instance field.");
                    }
                    var internalIdValue = ModelImporterClipAnimationInternalIdField.GetValue(clip);
                    if (!(internalIdValue is long configuredInternalId))
                    {
                        throw new InvalidOperationException("ModelImporterClipAnimation.internalID reflection value is not Int64 for " + label + ".");
                    }
                    if (configuredInternalId == 0)
                    {
                        throw new InvalidOperationException("Configured clip internalID is zero for " + label + ".");
                    }
                    if (configuredInternalId >= 110800000L && configuredInternalId <= 110800999L)
                    {
                        throw new InvalidOperationException("Configured clip internalID resolves to an AnimatorState preview identity for " + label + ": " + configuredInternalId + ".");
                    }
                    return configuredInternalId;
                }

                private static bool IsDelimiterSafeTakeSuffix(string candidate, string expected)
                {
                    if (string.IsNullOrEmpty(candidate) || candidate.Length <= expected.Length ||
                        !candidate.EndsWith(expected, StringComparison.Ordinal)) return false;
                    return !char.IsLetterOrDigit(candidate[candidate.Length - expected.Length - 1]);
                }

                internal static void ValidatePbrModelImporter(ModelImporter importer, bool arena, string label)
                {
                    var serialized = new SerializedObject(importer);
                    var normalImport = serialized.FindProperty("normalImportMode");
                    var tangents = serialized.FindProperty("tangentImportMode");
                    var secondaryUv = serialized.FindProperty("generateSecondaryUV");
                    if (normalImport == null || tangents == null || secondaryUv == null || normalImport.intValue != 0 || tangents.intValue != 3 ||
                        secondaryUv.boolValue != arena)
                    {
                        throw new InvalidOperationException(label + " importer must import authored normals and calculate Mikk tangents.");
                    }
                }

                internal static void ValidateMeshPbrChannels(Mesh mesh, bool requireUv1, string label)
                {
                    if (mesh == null || mesh.vertexCount == 0 || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                    {
                        throw new InvalidOperationException(label + " mesh UV0 contract invalid.");
                    }
                    if (requireUv1 && (mesh.uv2 == null || mesh.uv2.Length != mesh.vertexCount))
                    {
                        throw new InvalidOperationException(label + " mesh UV1 contract invalid.");
                    }
                    if (!requireUv1 && mesh.uv2 != null && mesh.uv2.Length != 0)
                    {
                        throw new InvalidOperationException(label + " dynamic mesh must not contain UV1.");
                    }
                    var normals = mesh.normals;
                    var tangents = mesh.tangents;
                    if (normals == null || normals.Length != mesh.vertexCount || tangents == null || tangents.Length != mesh.vertexCount)
                    {
                        throw new InvalidOperationException(label + " mesh normal/tangent count invalid.");
                    }
                    for (var i = 0; i < mesh.vertexCount; i++)
                    {
                        var n = normals[i];
                        var t = tangents[i];
                        if (float.IsNaN(n.x) || float.IsNaN(n.y) || float.IsNaN(n.z) || float.IsInfinity(n.x) || float.IsInfinity(n.y) || float.IsInfinity(n.z) ||
                            float.IsNaN(t.x) || float.IsNaN(t.y) || float.IsNaN(t.z) || float.IsNaN(t.w) || float.IsInfinity(t.x) || float.IsInfinity(t.y) || float.IsInfinity(t.z) || float.IsInfinity(t.w) || n.sqrMagnitude < 0.000001f || new Vector3(t.x, t.y, t.z).sqrMagnitude < 0.000001f)
                        {
                            throw new InvalidOperationException(label + " mesh contains non-finite or zero normal/tangent.");
                        }
                    }
                }

                internal static void ValidateTextureImporterContracts()
                {
                    ValidateTextureImporter(GrassTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(GrassNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(GrassMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(GrassOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WallTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WallNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WallMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WallOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(TrimTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(TrimNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(TrimMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(TrimOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(HazardTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(HazardNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(HazardMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(HazardOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(DetailNormalTexturePath, 512, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(BallTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(BallNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(BallMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(BallOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponMetalTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponMetalNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponMetalMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponMetalOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponDarkTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponDarkNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponDarkMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponDarkOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponAccentTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponAccentNormalTexturePath, 2048, false, TextureImporterType.NormalMap, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponAccentMetallicTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponAccentOcclusionTexturePath, 2048, false, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(WeaponAccentEmissionTexturePath, 2048, true, TextureImporterType.Default, TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketNormalTexturePath, 1024, false, TextureImporterType.NormalMap, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketMetallicTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketOcclusionTexturePath, 1024, false, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketEmissionTexturePath, 1024, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(RocketGlowTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ValidateTextureImporter(ShieldTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ValidateTextureImporter(ExplosionTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ValidateTextureImporter(SmokeTexturePath, 128, true, TextureImporterType.Default, TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Bilinear, 0);
                    ValidateTextureImporter(WeaponMicroDetailNormalTexturePath, WeaponMicroDetailNormalMaxTextureSize, false, TextureImporterType.NormalMap,
                        TextureWrapMode.Repeat, TextureWrapMode.Repeat, FilterMode.Trilinear, 8);
                    ValidateLauncherTextureImporterContracts();
                    ValidateShotgunTextureImporterContracts();
                }

                internal static void ValidateLauncherTextureImporterContracts()
                {
                    ValidateTextureImporter(LauncherBaseColorTexturePath, LauncherAtlasSize, true, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(LauncherNormalTexturePath, LauncherAtlasSize, false, TextureImporterType.NormalMap,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(LauncherMetallicTexturePath, LauncherAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(LauncherOcclusionTexturePath, LauncherAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(LauncherEmissionTexturePath, LauncherAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                }

                internal static void ValidateShotgunTextureImporterContracts()
                {
                    ValidateTextureImporter(ShotgunBaseColorTexturePath, ShotgunAtlasSize, true, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(ShotgunNormalTexturePath, ShotgunAtlasSize, false, TextureImporterType.NormalMap,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(ShotgunMetallicTexturePath, ShotgunAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(ShotgunOcclusionTexturePath, ShotgunAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                    ValidateTextureImporter(ShotgunEmissionTexturePath, ShotgunAtlasSize, false, TextureImporterType.Default,
                        TextureWrapMode.Clamp, TextureWrapMode.Clamp, FilterMode.Trilinear, 8);
                }

                internal static void ValidateTextureImporter(string path, int expectedSize, bool sRgb, TextureImporterType textureType, TextureWrapMode wrapU, TextureWrapMode wrapV, FilterMode filterMode, int anisoLevel)
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    var settings = new TextureImporterSettings();
                    if (importer != null) importer.ReadTextureSettings(settings);
                    var platform = importer != null ? importer.GetDefaultPlatformTextureSettings() : default(TextureImporterPlatformSettings);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    var expectedWidth = expectedSize;
                    var expectedHeight = expectedSize;
                    if (path == BallTexturePath || path == BallNormalTexturePath || path == BallMetallicTexturePath || path == BallOcclusionTexturePath)
                    {
                        expectedWidth = 1024;
                        expectedHeight = 512;
                    }
                    if (importer == null || texture == null || texture.width != expectedWidth || texture.height != expectedHeight || importer.textureType != textureType || importer.sRGBTexture != sRgb || !importer.mipmapEnabled || importer.filterMode != filterMode || importer.anisoLevel != anisoLevel || importer.maxTextureSize != expectedWidth || importer.wrapModeU != wrapU || importer.wrapModeV != wrapV || importer.wrapModeW != TextureWrapMode.Clamp || !settings.ignoreMipmapLimit || platform.overridden || platform.maxTextureSize != expectedWidth || platform.textureCompression != TextureImporterCompression.CompressedHQ)
                    {
                        throw new InvalidOperationException("Texture importer contract invalid: " + path);
                    }
                }

                internal static void ValidateModelImporterContracts()
                {
                    ValidateRigImporter(CharacterModelPath);
                    ValidateRigImporter(FpsKickModelPath);
                    ValidateStaticWeaponModel(WeaponModelPath, "Weapon");
                    ValidateLauncherUvZones();
                    ValidateStaticWeaponModel(FpsShotgunModelPath, "FpsShotgun");
                    ValidateShotgunUvZones(FpsShotgunModelPath, true);
                    ValidateStaticWeaponModel(ShotgunModelPath, "Shotgun");
                    ValidateShotgunUvZones(ShotgunModelPath, false);
                    var rocket = AssetImporter.GetAtPath(RocketModelPath) as ModelImporter;
                    if (rocket == null || rocket.animationType != ModelImporterAnimationType.None || rocket.importAnimation || rocket.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(rocket.globalScale - 1f) > 0.0001f) throw new InvalidOperationException("Rocket importer contract invalid.");
                    ValidatePbrModelImporter(rocket, false, "Rocket");
                    var rocketMeshes = AssetDatabase.LoadAllAssetsAtPath(RocketModelPath);
                    var rocketSurface = false;
                    var rocketHot = false;
                    for (var i = 0; i < rocketMeshes.Length; i++)
                    {
                        var mesh = rocketMeshes[i] as Mesh;
                        if (mesh == null) continue;
                        if (mesh.name == "RocketSurface") { rocketSurface = true; ValidateMeshPbrChannels(mesh, false, "RocketSurface"); }
                        else if (mesh.name == "RocketHot") { rocketHot = true; ValidateMeshPbrChannels(mesh, false, "RocketHot"); }
                    }
                    if (!rocketSurface || !rocketHot) throw new InvalidOperationException("Rocket imported mesh names are incomplete.");
                    ValidateArenaKitModel();
                }

                internal static void ValidateLauncherUvZones()
                {
                    var expectedGroups = StaticWeaponMeshGroups();
                    var meshes = LoadStaticWeaponMeshesByGroup(WeaponModelPath, "Launcher");
                    for (var i = 0; i < expectedGroups.Length; i++)
                    {
                        var group = expectedGroups[i];
                        ValidateLegacyWeaponUv(meshes[group], "Launcher/" + group);
                    }
                }

                internal static void ValidateShotgunUvZones(string modelPath, bool fps)
                {
                    var expectedGroups = StaticWeaponMeshGroups();
                    var modelLabel = fps ? "FpsShotgun" : "Shotgun";
                    var meshes = LoadStaticWeaponMeshesByGroup(modelPath, modelLabel);
                    for (var i = 0; i < expectedGroups.Length; i++)
                    {
                        var group = expectedGroups[i];
                        ValidateLegacyWeaponUv(meshes[group], modelLabel + "/" + group);
                    }
                }

                internal static void ValidateLegacyWeaponUv(Mesh mesh, string label)
                {
                    if (mesh == null || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidOperationException(label + " UV0 data is missing.");
                    const float epsilon = 0.0005f;
                    for (var i = 0; i < mesh.uv.Length; i++)
                    {
                        var uv = mesh.uv[i];
                        if (float.IsNaN(uv.x) || float.IsNaN(uv.y) || float.IsInfinity(uv.x) || float.IsInfinity(uv.y) ||
                            uv.x < -epsilon || uv.x > 1f + epsilon || uv.y < -epsilon || uv.y > 1f + epsilon)
                        {
                            throw new InvalidOperationException(label + " UV0 must be finite and remain within the legacy 0..1 range.");
                        }
                    }
                }

                internal static void ValidateMeshUvZone(Mesh mesh, RectInt zone, string label)
                {
                    if (mesh == null || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidOperationException(label + " UV0 data is missing.");
                    var minU = zone.xMin / (float)ShotgunAtlasSize;
                    var maxU = zone.xMax / (float)ShotgunAtlasSize;
                    var minV = zone.yMin / (float)ShotgunAtlasSize;
                    var maxV = zone.yMax / (float)ShotgunAtlasSize;
                    const float epsilon = 0.0005f;
                    for (var i = 0; i < mesh.uv.Length; i++)
                    {
                        var uv = mesh.uv[i];
                        if (uv.x < minU - epsilon || uv.x > maxU + epsilon || uv.y < minV - epsilon || uv.y > maxV + epsilon)
                            throw new InvalidOperationException(label + " UV0 leaves atlas zone " + zone + ".");
                    }
                }

                internal static void ValidateStaticWeaponModel(string path, string label)
                {
                    if (path != WeaponModelPath && path != FpsShotgunModelPath && path != ShotgunModelPath)
                    {
                        throw new InvalidOperationException(label + " static weapon validation does not support model path: " + path);
                    }
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.None || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
                    {
                        throw new InvalidOperationException(label + " importer contract invalid.");
                    }
                    ValidatePbrModelImporter(importer, false, label);

                    var expectedGroups = StaticWeaponMeshGroups();
                    var meshes = LoadStaticWeaponMeshesByGroup(path, label);
                    var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    for (var i = 0; i < expectedGroups.Length; i++)
                    {
                        var group = expectedGroups[i];
                        var mesh = meshes[group];
                        ValidateMeshPbrChannels(mesh, false, label + "/" + group);
                        boundsMin = Vector3.Min(boundsMin, mesh.bounds.min);
                        boundsMax = Vector3.Max(boundsMax, mesh.bounds.max);
                    }
                    ValidateStaticWeaponBounds(path, label, boundsMin, boundsMax);
                }

                private static string[] StaticWeaponMeshGroups()
                {
                    return new[] { "WeaponMetal", "WeaponDark", "WeaponAccent" };
                }

                private static Dictionary<string, Mesh> LoadStaticWeaponMeshesByGroup(string path, string label)
                {
                    var expectedGroups = StaticWeaponMeshGroups();
                    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    var meshes = new List<Mesh>();
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Mesh mesh && AssetDatabase.GetAssetPath(mesh) == path) meshes.Add(mesh);
                    }
                    if (meshes.Count != expectedGroups.Length)
                    {
                        throw new InvalidOperationException(label + " imported mesh count must equal three.");
                    }

                    var meshesByGroup = new Dictionary<string, Mesh>(StringComparer.Ordinal);
                    for (var i = 0; i < meshes.Count; i++)
                    {
                        var mesh = meshes[i];
                        var group = GetStaticWeaponMeshGroup(mesh.name, expectedGroups);
                        if (group == null || meshesByGroup.ContainsKey(group) || mesh.subMeshCount != 1)
                        {
                            throw new InvalidOperationException(label + " imported mesh groups must be exactly WeaponMetal, WeaponDark, and WeaponAccent, each with one submesh.");
                        }
                        meshesByGroup.Add(group, mesh);
                    }
                    if (meshesByGroup.Count != expectedGroups.Length)
                    {
                        throw new InvalidOperationException(label + " imported mesh groups are incomplete.");
                    }
                    return meshesByGroup;
                }

                private static void ValidateStaticWeaponBounds(string path, string label, Vector3 boundsMin, Vector3 boundsMax)
                {
                    Vector3 expectedMin;
                    Vector3 expectedMax;
                    if (path == WeaponModelPath)
                    {
                        expectedMin = LauncherWeaponBoundsMin;
                        expectedMax = LauncherWeaponBoundsMax;
                    }
                    else if (path == FpsShotgunModelPath)
                    {
                        expectedMin = FpsShotgunBoundsMin;
                        expectedMax = FpsShotgunBoundsMax;
                    }
                    else
                    {
                        expectedMin = WorldShotgunBoundsMin;
                        expectedMax = WorldShotgunBoundsMax;
                    }

                    var authoredMin = new Vector3(boundsMin.x, boundsMin.z, -boundsMax.y);
                    var authoredMax = new Vector3(boundsMax.x, boundsMax.z, -boundsMin.y);
                    if (!WithinBoundsTolerance(authoredMin, expectedMin, WeaponBoundsTolerance) ||
                        !WithinBoundsTolerance(authoredMax, expectedMax, WeaponBoundsTolerance))
                    {
                        throw new InvalidOperationException(label + " imported mesh bounds invalid; expected authored bounds " + expectedMin + ".." + expectedMax +
                                                            " within " + WeaponBoundsTolerance + ", observed imported bounds " + boundsMin + ".." + boundsMax +
                                                            ", derived authored bounds " + authoredMin + ".." + authoredMax + ".");
                    }
                }

                private static string GetStaticWeaponMeshGroup(string meshName, string[] expectedGroups)
                {
                    for (var i = 0; i < expectedGroups.Length; i++)
                    {
                        var group = expectedGroups[i];
                        if (string.Equals(meshName, group, StringComparison.Ordinal) ||
                            string.Equals(meshName, group + "Mesh", StringComparison.Ordinal) ||
                            meshName.EndsWith("_" + group + "Mesh", StringComparison.Ordinal)) return group;
                    }
                    return null;
                }

                internal static void ValidateArenaKitModel()
                {
                    var importer = AssetImporter.GetAtPath(ArenaKitModelPath) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.None || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(importer.globalScale - 1f) > 0.0001f) throw new InvalidOperationException("ArenaKit importer contract invalid.");
                    ValidateArenaKitSourceMaterials(importer);
                    ValidatePbrModelImporter(importer, true, "ArenaKit");
                    var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
                    var meshes = new List<Mesh>();
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Mesh mesh && AssetDatabase.GetAssetPath(mesh) == ArenaKitModelPath)
                        {
                            meshes.Add(mesh);
                        }
                    }
                    var expectedNames = new[] { MovementLabContract.ArenaGoalRecessMesh, MovementLabContract.ArenaWallSconceMesh };
                    var expectedSubMeshCounts = new[] { 4, 2 };
                    var expectedBoundsMin = new[] { MovementLabContract.ArenaGoalRecessBoundsMin, MovementLabContract.ArenaWallSconceBoundsMin };
                    var expectedBoundsMax = new[] { MovementLabContract.ArenaGoalRecessBoundsMax, MovementLabContract.ArenaWallSconceBoundsMax };
                    var expectedSubmeshSlotSpecifications = new[]
                    {
                        MovementLabContract.ArenaGoalRecessSubmeshSlotSpecifications,
                        MovementLabContract.ArenaWallSconceSubmeshSlotSpecifications
                    };
                    if (meshes.Count != expectedNames.Length)
                    {
                        throw new InvalidOperationException("ArenaKit imported mesh set must contain exactly ArenaGoalRecessMesh and ArenaWallSconceMesh.");
                    }
                    var importedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaKitModelPath);
                    if (importedRoot == null)
                    {
                        throw new InvalidOperationException("ArenaKit imported model root is missing; expected a main GameObject at " + ArenaKitModelPath + ".");
                    }
                    var importedRoots = new List<GameObject> { importedRoot };
                    ValidateArenaKitRendererOnly(importedRoots);
                    for (var i = 0; i < expectedNames.Length; i++)
                    {
                        Mesh found = null;
                        for (var j = 0; j < meshes.Count; j++)
                        {
                            if (string.Equals(meshes[j].name, expectedNames[i], StringComparison.Ordinal))
                            {
                                if (found != null) throw new InvalidOperationException("ArenaKit imported mesh name is duplicated: " + expectedNames[i]);
                                found = meshes[j];
                            }
                        }
                        if (found == null || AssetDatabase.GetAssetPath(found) != ArenaKitModelPath || found.subMeshCount != expectedSubMeshCounts[i])
                        {
                            var importedNames = new List<string>();
                            for (var k = 0; k < assets.Length; k++) if (assets[k] != null) importedNames.Add(assets[k].name + "[" + assets[k].GetType().Name + "]");
                            throw new InvalidOperationException("ArenaKit named mesh missing/provenance/submesh invalid: " + expectedNames[i] + "; imported assets=" + string.Join(",", importedNames.ToArray()));
                        }
                        ValidateMeshPbrChannels(found, true, "ArenaKit/" + expectedNames[i]);
                        if (Vector3.Distance(found.bounds.min, expectedBoundsMin[i]) > 0.001f || Vector3.Distance(found.bounds.max, expectedBoundsMax[i]) > 0.001f)
                        {
                            throw new InvalidOperationException("ArenaKit mesh bounds contract invalid: " + expectedNames[i] + "; expected " + expectedBoundsMin[i] + ".." + expectedBoundsMax[i] + ", actual " + found.bounds.min + ".." + found.bounds.max);
                        }
                        ValidateImportedSubmeshSlotOrder(importedRoots, found, expectedSubmeshSlotSpecifications[i], expectedNames[i]);
                    }
                }

                private static void ValidateArenaKitSourceMaterials(ModelImporter importer)
                {
                    var sourceMaterialsProperty = typeof(ModelImporter).GetProperty(
                        "sourceMaterials",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var expectedPropertyType = typeof(AssetImporter.SourceAssetIdentifier[]);
                    if (sourceMaterialsProperty == null || sourceMaterialsProperty.PropertyType != expectedPropertyType)
                    {
                        throw new InvalidOperationException("ArenaKit importer sourceMaterials property is missing or has an unexpected type; expected SourceAssetIdentifier[].");
                    }

                    var sourceMaterials = sourceMaterialsProperty.GetValue(importer, null) as AssetImporter.SourceAssetIdentifier[];
                    if (sourceMaterials == null)
                    {
                        throw new InvalidOperationException("ArenaKit importer sourceMaterials value is null.");
                    }

                    var expectedNames = MovementLabContract.ArenaGoalRecessMaterialSlots;
                    if (sourceMaterials.Length != expectedNames.Length)
                    {
                        throw new InvalidOperationException("ArenaKit importer source material count invalid; expected " + expectedNames.Length + ", actual " + sourceMaterials.Length + ".");
                    }

                    var expectedNameSet = new HashSet<string>(expectedNames, StringComparer.Ordinal);
                    var seenNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < sourceMaterials.Length; i++)
                    {
                        var sourceMaterial = sourceMaterials[i];
                        if (sourceMaterial.type != typeof(Material) || !expectedNameSet.Contains(sourceMaterial.name))
                        {
                            throw new InvalidOperationException("ArenaKit importer source material identifier invalid at index " + i + "; expected Material with one of [" + string.Join(",", expectedNames) + "], actual " + sourceMaterial.type + "/" + sourceMaterial.name + ".");
                        }
                        if (!seenNames.Add(sourceMaterial.name))
                        {
                            throw new InvalidOperationException("ArenaKit importer source material is duplicated at index " + i + ": " + sourceMaterial.name + ".");
                        }
                    }
                    for (var i = 0; i < expectedNames.Length; i++)
                    {
                        if (!seenNames.Contains(expectedNames[i]))
                        {
                            throw new InvalidOperationException("ArenaKit importer source material is missing: " + expectedNames[i] + ".");
                        }
                    }

                    var externalObjectMap = importer.GetExternalObjectMap();
                    if (externalObjectMap == null || externalObjectMap.Count != 0)
                    {
                        throw new InvalidOperationException("ArenaKit importer must not contain external object remaps; actual count " + (externalObjectMap == null ? "null" : externalObjectMap.Count.ToString()) + ".");
                    }
                }

                private static void ValidateArenaKitRendererOnly(List<GameObject> importedRoots)
                {
                    var rendererCount = 0;
                    var meshFilterCount = 0;
                    for (var i = 0; i < importedRoots.Count; i++)
                    {
                        var root = importedRoots[i];
                        if (root == null) continue;
                        if (root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                            root.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                            root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                        {
                            throw new InvalidOperationException("ArenaKit imported objects must remain renderer-only: " + root.name);
                        }
                        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                        var filters = root.GetComponentsInChildren<MeshFilter>(true);
                        rendererCount += renderers.Length;
                        meshFilterCount += filters.Length;
                        for (var j = 0; j < renderers.Length; j++)
                        {
                            var filter = renderers[j].GetComponent<MeshFilter>();
                            if (filter == null || filter.sharedMesh == null || AssetDatabase.GetAssetPath(filter.sharedMesh) != ArenaKitModelPath)
                                throw new InvalidOperationException("ArenaKit imported renderer hierarchy must bind each renderer to one ArenaKit mesh: " + renderers[j].name);
                        }
                    }
                    if (rendererCount != 2 || meshFilterCount != rendererCount)
                    {
                        throw new InvalidOperationException("ArenaKit imported renderer hierarchy must contain exactly two MeshRenderers and matching MeshFilters.");
                    }
                }

                private static void ValidateImportedSubmeshSlotOrder(List<GameObject> importedRoots, Mesh mesh,
                    RocketFooxball.Editor.MovementLabContract.ArenaSubmeshSlotSpecification[] expectedSlots, string label)
                {
                    if (expectedSlots == null)
                    {
                        throw new InvalidOperationException("ArenaKit expected submesh slot specifications are missing: " + label);
                    }
                    if (mesh == null)
                    {
                        throw new InvalidOperationException("ArenaKit imported mesh is missing: " + label + ".");
                    }

                    MeshRenderer target = null;
                    var matchCount = 0;
                    for (var i = 0; i < importedRoots.Count; i++)
                    {
                        var renderers = importedRoots[i].GetComponentsInChildren<MeshRenderer>(true);
                        for (var j = 0; j < renderers.Length; j++)
                        {
                            var filter = renderers[j].GetComponent<MeshFilter>();
                            if (filter != null && filter.sharedMesh == mesh)
                            {
                                matchCount++;
                                target = renderers[j];
                            }
                        }
                    }
                    if (target == null)
                    {
                        throw new InvalidOperationException("ArenaKit imported mesh renderer missing: " + label);
                    }
                    if (matchCount != 1)
                    {
                        throw new InvalidOperationException("ArenaKit imported mesh renderer hierarchy must bind exactly one renderer to " + label + "; actual " + matchCount + ".");
                    }
                    var materials = target.sharedMaterials;
                    if (materials == null || materials.Length != mesh.subMeshCount || materials.Length != expectedSlots.Length)
                    {
                        throw new InvalidOperationException("ArenaKit material slot count invalid: " + label + "; expected mesh/spec count " + mesh.subMeshCount + "/" + expectedSlots.Length + ", actual " + (materials == null ? "null" : materials.Length.ToString()) + ".");
                    }

                    var vertices = mesh.vertices;
                    if (vertices == null)
                    {
                        throw new InvalidOperationException("ArenaKit imported mesh vertices are missing: " + label + ".");
                    }
                    for (var i = 0; i < expectedSlots.Length; i++)
                    {
                        var expectedSlot = expectedSlots[i];
                        var slotLabel = label + "/" + expectedSlot.LogicalMaterialName + "[" + i + "]";
                        if (materials[i] == null)
                        {
                            throw new InvalidOperationException("ArenaKit imported renderer material slot is null: " + slotLabel + ".");
                        }
                        if (mesh.GetTopology(i) != MeshTopology.Triangles)
                        {
                            throw new InvalidOperationException("ArenaKit imported submesh topology invalid: " + slotLabel + "; expected " + MeshTopology.Triangles + ".");
                        }

                        var indices = mesh.GetIndices(i);
                        if (indices == null)
                        {
                            throw new InvalidOperationException("ArenaKit imported submesh indices are missing: " + slotLabel + ".");
                        }
                        if (indices.Length != expectedSlot.IndexCount)
                        {
                            throw new InvalidOperationException("ArenaKit imported submesh index count invalid: " + slotLabel + "; expected " + expectedSlot.IndexCount + ", actual " + indices.Length + ".");
                        }

                        var uniquePositions = new HashSet<Vector3Int>();
                        var boundsMin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                        var boundsMax = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                        for (var j = 0; j < indices.Length; j++)
                        {
                            var vertexIndex = indices[j];
                            if (vertexIndex < 0 || vertexIndex >= vertices.Length)
                            {
                                throw new InvalidOperationException("ArenaKit imported submesh vertex index out of range: " + slotLabel + "; index " + j + " references vertex " + vertexIndex + " of " + vertices.Length + ".");
                            }

                            var position = vertices[vertexIndex];
                            boundsMin = Vector3.Min(boundsMin, position);
                            boundsMax = Vector3.Max(boundsMax, position);
                            uniquePositions.Add(new Vector3Int(
                                Mathf.RoundToInt(position.x * 10000f),
                                Mathf.RoundToInt(position.y * 10000f),
                                Mathf.RoundToInt(position.z * 10000f)));
                        }

                        if (uniquePositions.Count != expectedSlot.UniqueQuantizedPositionCount)
                        {
                            throw new InvalidOperationException("ArenaKit imported submesh unique position count invalid: " + slotLabel + "; expected " + expectedSlot.UniqueQuantizedPositionCount + ", actual " + uniquePositions.Count + ".");
                        }
                        if (!WithinBoundsTolerance(boundsMin, expectedSlot.BoundsMin, 0.001f) ||
                            !WithinBoundsTolerance(boundsMax, expectedSlot.BoundsMax, 0.001f))
                        {
                            throw new InvalidOperationException("ArenaKit imported submesh bounds invalid: " + slotLabel + "; expected " + expectedSlot.BoundsMin + ".." + expectedSlot.BoundsMax + ", actual " + boundsMin + ".." + boundsMax + ".");
                        }
                    }
                }

                private static bool WithinBoundsTolerance(Vector3 actual, Vector3 expected, float tolerance)
                {
                    return Mathf.Abs(actual.x - expected.x) <= tolerance &&
                           Mathf.Abs(actual.y - expected.y) <= tolerance &&
                           Mathf.Abs(actual.z - expected.z) <= tolerance;
                }

                internal static void ValidateRigImporter(string path)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.importAnimation || importer.optimizeGameObjects || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
                    {
                        throw new InvalidOperationException("Rig importer contract invalid: " + path);
                    }
                    var clips = importer.clipAnimations;
                    var character = path == CharacterModelPath;
                    var expected = character
                        ? new[] { MovementLabContract.IdleStateName, MovementLabContract.RunStateName, MovementLabContract.JumpStateName, MovementLabContract.FallStateName, MovementLabContract.LandStateName, MovementLabContract.KickStateName }
                        : new[] { MovementLabContract.IdleStateName, MovementLabContract.KickStateName };
                    var loops = path == CharacterModelPath ? new[] { true, true, false, false, false, false } : new[] { true, false };
                    var starts = character
                        ? new[] { 1f, 1f, 1f, 1f, 1f, (float)MovementLabContract.WorldKickStartFrame }
                        : new[] { 1f, (float)MovementLabContract.FpsKickStartFrame };
                    var ends = character
                        ? new[] { 30f, 20f, 12f, 15f, 10f, (float)MovementLabContract.WorldKickEndFrame }
                        : new[] { 31f, (float)MovementLabContract.FpsKickEndFrame };
                    if (clips == null || clips.Length != expected.Length) throw new InvalidOperationException("Rig importer clip count mismatch: " + path);
                    ValidateConfiguredClipInternalIds(path, clips);
                    var importedClips = new HashSet<AnimationClip>();
                    var configuredSourceTakeNames = new HashSet<string>(StringComparer.Ordinal);
                    var loadedClipIds = new HashSet<long>();
                    for (var i = 0; i < expected.Length; i++)
                    {
                        var clip = clips[i];
                        var expectedTakeName = ResolveImportedSourceTakeName(
                            importer,
                            expected[i]);
                        if (clip.name != expected[i] || clip.takeName != expectedTakeName ||
                            Mathf.Abs(clip.firstFrame - starts[i]) > 0.001f || Mathf.Abs(clip.lastFrame - ends[i]) > 0.001f ||
                            Mathf.Abs(clip.cycleOffset) > 0.001f ||
                            clip.loopTime != loops[i] || !clip.lockRootRotation || !clip.keepOriginalOrientation ||
                            !clip.lockRootHeightY || !clip.keepOriginalPositionY || !clip.lockRootPositionXZ ||
                            !clip.keepOriginalPositionXZ || clip.heightFromFeet || clip.hasAdditiveReferencePose || clip.mirror)
                        {
                            throw new InvalidOperationException("Rig importer clip contract invalid: " + path + "/" + expected[i]);
                        }

                        var imported = FindImportedClip(path, expected[i]);
                        if (imported == null || !importedClips.Add(imported) || Mathf.Abs(imported.frameRate - MovementLabContract.AnimationSourceFrameRate) > 0.001f)
                        {
                            throw new InvalidOperationException("Rig importer imported clip identity/rate invalid: " + path + "/" + expected[i]);
                        }
                        if (!character)
                        {
                            if (!configuredSourceTakeNames.Add(clip.takeName))
                            {
                                throw new InvalidOperationException("FPS rig importer Idle and Kick must use distinct full source take names: " + path + "/" + clip.takeName + ".");
                            }
                            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(imported, out var importedGuid, out long importedId) ||
                                !string.Equals(importedGuid, AssetDatabase.AssetPathToGUID(path), StringComparison.Ordinal) ||
                                importedId >= 110800000L && importedId <= 110800999L ||
                                !loadedClipIds.Add(importedId))
                            {
                                throw new InvalidOperationException("FPS rig imported Idle and Kick must have distinct non-legacy clip identities: " + path + "/" + expected[i] + ".");
                            }
                        }
                    }

                    var avatars = FindImportedAvatars(path);
                    if (avatars.Length != 1 || avatars[0] == null || FindImportedAvatar(path) != avatars[0] ||
                        !string.Equals(AssetDatabase.GetAssetPath(avatars[0]), path, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Rig importer Avatar provenance invalid: " + path);
                    }
                }

                private static void ValidateConfiguredClipInternalIds(string path, ModelImporterClipAnimation[] clips)
                {
                    var configuredIds = new HashSet<long>();
                    for (var i = 0; i < clips.Length; i++)
                    {
                        var configuredId = GetConfiguredClipInternalId(clips[i], path + "/" + clips[i].name);
                        if (!configuredIds.Add(configuredId))
                        {
                            throw new InvalidOperationException("Rig importer configured clip internalID is not unique: " + path + "/" + clips[i].name + ".");
                        }
                    }
                }

    }
}
