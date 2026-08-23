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
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabImportPipeline
    {
        internal static void Apply()
        {
            // Preserve importer settlement order: textures first, models second.
            ConfigureTextureImporters();
            ConfigureModelImporters();
        }
    }

    internal static partial class MovementLabImportPipeline
    {
        // T3 entry point: this deliberately has a closed six-asset surface.
        // Do not call Apply here; the launcher proof must not reimport or
        // rewrite unrelated source metas.
        internal static void ImportLauncherVisualAssets()
        {
            ConfigureStaticModelImporter(WeaponModelPath);
            ConfigureLauncherTextureImporter(LauncherBaseColorTexturePath, true, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherNormalTexturePath, false, TextureImporterType.NormalMap);
            ConfigureLauncherTextureImporter(LauncherMetallicTexturePath, false, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherOcclusionTexturePath, false, TextureImporterType.Default);
            ConfigureLauncherTextureImporter(LauncherEmissionTexturePath, false, TextureImporterType.Default);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ConfigureLauncherTextureImporter(string path, bool sRgb, TextureImporterType textureType)
        {
            ConfigureTextureImporter(path, LauncherAtlasSize, sRgb, textureType,
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

                    var sourceClips = importer.clipAnimations;
                    var syntheticClips = false;
                    if (sourceClips == null || sourceClips.Length == 0)
                    {
                        sourceClips = importer.defaultClipAnimations;
                    }
                    if (sourceClips == null || sourceClips.Length == 0)
                    {
                        // Unity may not expose FBX takes through defaultClipAnimations
                        // until clipAnimations is explicitly seeded. Use deterministic
                        // source ranges authored by the generators as a fallback.
                        var idle = new ModelImporterClipAnimation { name = "Idle", takeName = "Idle", firstFrame = 1f, lastFrame = character ? 30f : 31f };
                        var kick = new ModelImporterClipAnimation { name = "Kick", takeName = "Kick", firstFrame = 1f, lastFrame = character ? 12f : 11f };
                        sourceClips = new[] { idle, kick };
                        syntheticClips = true;
                    }

                    var expectedNames = character ? new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" } : new[] { "Idle", "Kick" };
                    var expectedLoops = character ? new[] { true, true, false, false, false, false } : new[] { true, false };
                    var expectedStarts = character ? new[] { 1f, 1f, 1f, 1f, 1f, 1f } : new[] { 1f, 1f };
                    var expectedEnds = character ? new[] { 30f, 20f, 12f, 15f, 10f, 12f } : new[] { 31f, 11f };
                    var clips = new List<ModelImporterClipAnimation>();
                    for (var expectedIndex = 0; expectedIndex < expectedNames.Length; expectedIndex++)
                    {
                        ModelImporterClipAnimation source = null;
                        for (var sourceIndex = 0; sourceIndex < sourceClips.Length; sourceIndex++)
                        {
                            var sourceName = sourceClips[sourceIndex].name ?? string.Empty;
                            if (string.Equals(sourceName, expectedNames[expectedIndex], StringComparison.OrdinalIgnoreCase) ||
                                (sourceName.EndsWith(expectedNames[expectedIndex], StringComparison.OrdinalIgnoreCase) && sourceName.Length > expectedNames[expectedIndex].Length && !char.IsLetterOrDigit(sourceName[sourceName.Length - expectedNames[expectedIndex].Length - 1])))
                            {
                                source = sourceClips[sourceIndex];
                                break;
                            }
                        }
                        if (source == null)
                        {
                            source = new ModelImporterClipAnimation();
                        }
                        source.name = expectedNames[expectedIndex];
                        source.takeName = expectedNames[expectedIndex];
                        source.firstFrame = expectedStarts[expectedIndex];
                        source.lastFrame = expectedEnds[expectedIndex];
                        source.loopTime = expectedLoops[expectedIndex];
                        source.lockRootRotation = true;
                        source.keepOriginalOrientation = true;
                        source.lockRootHeightY = true;
                        source.keepOriginalPositionY = true;
                        source.lockRootPositionXZ = true;
                        source.keepOriginalPositionXZ = true;
                        source.heightFromFeet = false;
                        source.hasAdditiveReferencePose = false;
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

                internal static bool ClipsEqual(ModelImporterClipAnimation[] a, ModelImporterClipAnimation[] b)
                {
                    if (a == null || b == null || a.Length != b.Length) return false;
                    for (var i = 0; i < a.Length; i++)
                    {
                        if (a[i].name != b[i].name || Mathf.Abs(a[i].firstFrame - b[i].firstFrame) > 0.001f || Mathf.Abs(a[i].lastFrame - b[i].lastFrame) > 0.001f || a[i].loopTime != b[i].loopTime || a[i].lockRootRotation != b[i].lockRootRotation || a[i].lockRootHeightY != b[i].lockRootHeightY || a[i].lockRootPositionXZ != b[i].lockRootPositionXZ)
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

                internal static AnimationClip FindImportedClip(string modelPath, string name)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);

                    // Prefer an exact imported take name. Model importers commonly prefix
                    // clips with the source model name, so a broad substring match can
                    // bind e.g. "FpsKickRig|Idle" to the Kick state just because the
                    // model prefix contains "Kick".
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is AnimationClip clip && string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase)) return clip;
                    }

                    // Fall back to a delimiter-safe take suffix ("|Idle", "@Kick",
                    // etc.). The character before the take must be a non-alphanumeric
                    // delimiter; this excludes model-prefix substrings such as
                    // "FpsKickRig|Idle" when looking for Kick.
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (!(assets[i] is AnimationClip clip)) continue;
                        var clipName = clip.name;
                        if (clipName.Length <= name.Length || !clipName.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                        var delimiter = clipName[clipName.Length - name.Length - 1];
                        if (!char.IsLetterOrDigit(delimiter)) return clip;
                    }
                    return null;
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
                    ValidateLauncherTextureImporterContracts();
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
                    ValidateStaticWeaponModel(ShotgunModelPath, "Shotgun");
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
                    var assets = AssetDatabase.LoadAllAssetsAtPath(WeaponModelPath);
                    var expectedGroups = new[] { "WeaponMetal", "WeaponDark", "WeaponAccentCore", "WeaponAccent" };
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < assets.Length; i++)
                    {
                        var mesh = assets[i] as Mesh;
                        if (mesh == null || AssetDatabase.GetAssetPath(mesh) != WeaponModelPath) continue;
                        var group = GetStaticWeaponMeshGroup(mesh.name, expectedGroups);
                        if (group == null) continue;
                        var zone = group == "WeaponMetal" ? LauncherMetalUvZone :
                            group == "WeaponDark" ? LauncherDarkUvZone :
                            group == "WeaponAccentCore" ? LauncherAccentCoreUvZone : LauncherAccentUvZone;
                        ValidateMeshUvZone(mesh, zone, "Launcher/" + group);
                        seen.Add(group);
                    }
                    if (seen.Count != expectedGroups.Length)
                        throw new InvalidOperationException("Launcher UV-zone mesh groups are incomplete.");
                }

                internal static void ValidateMeshUvZone(Mesh mesh, RectInt zone, string label)
                {
                    if (mesh == null || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                        throw new InvalidOperationException(label + " UV0 data is missing.");
                    var minU = zone.xMin / (float)LauncherAtlasSize;
                    var maxU = zone.xMax / (float)LauncherAtlasSize;
                    var minV = zone.yMin / (float)LauncherAtlasSize;
                    var maxV = zone.yMax / (float)LauncherAtlasSize;
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
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.None || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
                    {
                        throw new InvalidOperationException(label + " importer contract invalid.");
                    }
                    ValidatePbrModelImporter(importer, false, label);

                    var expectedGroups = new[] { "WeaponMetal", "WeaponDark", "WeaponAccentCore", "WeaponAccent" };
                    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                    var meshes = new List<Mesh>();
                    for (var i = 0; i < assets.Length; i++)
                    {
                        if (assets[i] is Mesh mesh && AssetDatabase.GetAssetPath(mesh) == path) meshes.Add(mesh);
                    }
                    if (meshes.Count != expectedGroups.Length)
                    {
                        throw new InvalidOperationException(label + " imported mesh count must equal four.");
                    }
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < meshes.Count; i++)
                    {
                        var mesh = meshes[i];
                        var group = GetStaticWeaponMeshGroup(mesh.name, expectedGroups);
                        if (group == null || !seen.Add(group) || mesh.subMeshCount != 1)
                        {
                            throw new InvalidOperationException(label + " imported mesh groups must be exactly WeaponMetal, WeaponDark, WeaponAccent, and WeaponAccentCore.");
                        }
                        ValidateMeshPbrChannels(mesh, false, label + "/" + group);
                    }
                    if (seen.Count != expectedGroups.Length) throw new InvalidOperationException(label + " imported mesh groups are incomplete.");
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
                    ValidatePbrModelImporter(importer, true, "ArenaKit");
                    var expected = new[] { "ArenaGoalShell", "ArenaRampRails", "ArenaWallPylon", "ArenaPerimeterTruss", "ArenaScoreboard" };
                    var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
                    for (var i = 0; i < expected.Length; i++)
                    {
                        Mesh found = null;
                        for (var j = 0; j < assets.Length; j++) if (assets[j] is Mesh mesh && mesh.name == expected[i]) found = mesh;
                        if (found == null || AssetDatabase.GetAssetPath(found) != ArenaKitModelPath || found.subMeshCount < 1)
                        {
                            var importedNames = new List<string>();
                            for (var k = 0; k < assets.Length; k++) if (assets[k] != null) importedNames.Add(assets[k].name + "[" + assets[k].GetType().Name + "]");
                            throw new InvalidOperationException("ArenaKit named mesh missing/provenance invalid: " + expected[i] + "; imported assets=" + string.Join(",", importedNames.ToArray()));
                        }
                        ValidateMeshPbrChannels(found, true, "ArenaKit/" + expected[i]);
                    }
                }

                internal static void ValidateRigImporter(string path)
                {
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    if (importer == null || importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.importAnimation || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
                    {
                        throw new InvalidOperationException("Rig importer contract invalid: " + path);
                    }
                    var clips = importer.clipAnimations;
                    var expected = path == CharacterModelPath ? new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" } : new[] { "Idle", "Kick" };
                    var loops = path == CharacterModelPath ? new[] { true, true, false, false, false, false } : new[] { true, false };
                    if (clips == null || clips.Length != expected.Length) throw new InvalidOperationException("Rig importer clip count mismatch: " + path);
                    for (var i = 0; i < expected.Length; i++) if (clips[i].name != expected[i] || clips[i].loopTime != loops[i]) throw new InvalidOperationException("Rig importer clip contract invalid: " + path + "/" + expected[i]);
                }

    }
}
