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
    internal static partial class MovementLabMaterialPipeline
    {
        // Weapon response is intentionally owned here because the prefab
        // composition root routes all eight weapon assets through this Lit
        // material factory. Keeping the values canonical here also repairs
        // older generated materials when the lab is rebuilt.
        internal const float WeaponMetallicResponse = 0.55f;
        internal const float WeaponMetalSmoothnessResponse = 0.52f;
        internal const float WeaponDarkMetallicResponse = 0.08f;
        internal const float WeaponDarkSmoothnessResponse = 0.28f;
        internal const float WeaponAccentMetallicResponse = 0f;
        internal const float WeaponAccentSmoothnessResponse = 0.72f;
        internal const float WeaponAccentCoreMetallicResponse = 0.15f;
        internal const float WeaponAccentCoreSmoothnessResponse = 0.60f;
        internal const float WeaponResponseOcclusion = 1f;
        internal const float WeaponResponseBumpScale = 1f;
        internal const float WeaponAccentAlpha = 0.42f;

        internal readonly struct MovementLabMaterialCatalog
        {
            internal readonly UnityEngine.Material Floor, Wall, Trim, Hazard, Marking, Ball, Rocket;
            internal MovementLabMaterialCatalog(UnityEngine.Material floor, UnityEngine.Material wall, UnityEngine.Material trim, UnityEngine.Material hazard, UnityEngine.Material marking, UnityEngine.Material ball, UnityEngine.Material rocket)
            { Floor = floor; Wall = wall; Trim = trim; Hazard = hazard; Marking = marking; Ball = ball; Rocket = rocket; }
        }
        internal static void ValidateCatalog(Material floor, Material wall, Material trim, Material hazard,
            Material marking, Material ball, Material rocket)
        {
            var catalog = new MovementLabMaterialCatalog(floor, wall, trim, hazard, marking, ball, rocket);
            if (catalog.Floor == null || catalog.Wall == null || catalog.Trim == null || catalog.Hazard == null ||
                catalog.Marking == null || catalog.Ball == null || catalog.Rocket == null)
            {
                throw new InvalidOperationException("MovementLab material catalog contains a null generated material.");
            }
        }
    }

    internal static partial class MovementLabMaterialPipeline
    {
                internal static void FinalizeGeneratedMaterialPersistence()
                {
                    var materialPaths = new List<string>();
                    for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
                    {
                        var path = GeneratedYamlAssetPaths[i];
                        if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) materialPaths.Add(path);
                    }

                    for (var i = 0; i < materialPaths.Count; i++)
                    {
                        AssetDatabase.ImportAsset(materialPaths[i], ImportAssetOptions.ForceSynchronousImport);
                    }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                internal static Texture2D LoadTexture(string path)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture == null) throw new InvalidOperationException("Missing texture asset: " + path);
                    return texture;
                }

                internal static Material GetOrCreateRetroMaterial(string name, Color color, Texture2D texture, Vector2 textureScale)
                {
                    return GetOrCreateLitMaterial(new PbrMaterialSpecification(name, texture, null, null, null, null, null,
                        textureScale, color, Color.clear, 0f, 0f, 0.5f, 1f, 1f));
                }

                internal static Material GetOrCreateLitMaterial(PbrMaterialSpecification specification)
                {
                    var path = MaterialsPath + "/" + specification.Name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var shader = Shader.Find(LitShaderName);
                    if (shader == null) throw new InvalidOperationException("Missing material shader: " + LitShaderName);
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    else if (material.shader != shader)
                    {
                        material.shader = shader;
                    }

                    material.SetColor("_BaseColor", specification.BaseColor);
                    material.SetFloat("_Metallic", specification.Metallic);
                    material.SetFloat("_Smoothness", specification.Smoothness);
                    material.SetFloat("_OcclusionStrength", specification.OcclusionStrength);
                    material.SetFloat("_BumpScale", specification.BumpScale);
                    material.SetColor("_EmissionColor", specification.EmissionStrength > 0.001f ? specification.EmissionColor * specification.EmissionStrength : Color.clear);
                    if (material.HasProperty("_EmissionStrength")) material.SetFloat("_EmissionStrength", specification.EmissionStrength);
                    material.SetTexture("_BaseMap", specification.BaseMap != null ? specification.BaseMap : Texture2D.whiteTexture);
                    material.SetTexture("_BumpMap", specification.NormalMap);
                    material.SetTexture("_MetallicGlossMap", specification.MetallicGlossMap);
                    material.SetTexture("_OcclusionMap", specification.OcclusionMap);
                    material.SetTexture("_EmissionMap", specification.EmissionMap);
                    material.SetTexture("_DetailNormalMap", specification.DetailNormalMap);
                    material.SetTextureScale("_DetailNormalMap", specification.DetailNormalTiling);
                    material.SetFloat("_DetailNormalMapScale", specification.DetailNormalScale);
                    material.SetTextureScale("_BaseMap", specification.TextureScale);
                    ApplyWeaponMaterialResponse(material, specification.Name);
                    var hasEmission = specification.EmissionMap != null || specification.EmissionStrength > 0.001f;
                    material.globalIlluminationFlags = hasEmission
                        ? MaterialGlobalIlluminationFlags.BakedEmissive
                        : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    if (specification.NormalMap != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
                    if (specification.MetallicGlossMap != null) material.EnableKeyword("_METALLICSPECGLOSSMAP"); else material.DisableKeyword("_METALLICSPECGLOSSMAP");
                    if (specification.OcclusionMap != null) material.EnableKeyword("_OCCLUSIONMAP"); else material.DisableKeyword("_OCCLUSIONMAP");
                    if (hasEmission) material.EnableKeyword("_EMISSION"); else material.DisableKeyword("_EMISSION");
                    SetDetailNormalKeyword(material, specification.DetailNormalMap != null);
                    material.SetFloat("_SmoothnessTextureChannel", 0f);
                    SetOpaqueLitState(material);
                    material.enableInstancing = true;
                    EditorUtility.SetDirty(material);
                    return material;
                }

                private static void ApplyWeaponMaterialResponse(Material material, string name)
                {
                    if (material == null || string.IsNullOrEmpty(name)) return;

                    var isAccent = false;
                    var isWeapon = true;
                    var metallic = 0f;
                    var smoothness = 0f;
                    switch (name)
                    {
                        case "WeaponMetal":
                        case "ShotgunMetal":
                            metallic = WeaponMetallicResponse;
                            smoothness = WeaponMetalSmoothnessResponse;
                            break;
                        case "WeaponDark":
                        case "ShotgunDark":
                            metallic = WeaponDarkMetallicResponse;
                            smoothness = WeaponDarkSmoothnessResponse;
                            break;
                        case "WeaponAccent":
                        case "ShotgunAccent":
                            metallic = WeaponAccentMetallicResponse;
                            smoothness = WeaponAccentSmoothnessResponse;
                            isAccent = true;
                            break;
                        case "WeaponAccentCore":
                        case "ShotgunAccentCore":
                            metallic = WeaponAccentCoreMetallicResponse;
                            smoothness = WeaponAccentCoreSmoothnessResponse;
                            break;
                        default:
                            isWeapon = false;
                            break;
                    }

                    if (!isWeapon) return;
                    material.SetFloat("_Metallic", metallic);
                    material.SetFloat("_Smoothness", smoothness);
                    material.SetFloat("_OcclusionStrength", WeaponResponseOcclusion);
                    material.SetFloat("_BumpScale", WeaponResponseBumpScale);
                    material.SetColor("_BaseColor", isAccent
                        ? new Color(1f, 1f, 1f, WeaponAccentAlpha)
                        : Color.white);
                    material.SetFloat("_EnvironmentReflections", 0f);
                    material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    // Direct specular remains enabled for readable weapon
                    // highlights; repair any stale generated keyword.
                    material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                }

                internal static void SetOpaqueLitState(Material material)
                {
                    if (material == null) throw new ArgumentNullException(nameof(material));
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.SetFloat("_Surface", 0f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_BlendModePreserveSpecular", 0f);
                    material.SetFloat("_AlphaClip", 0f);
                    material.SetFloat("_SrcBlend", 1f);
                    material.SetFloat("_DstBlend", 0f);
                    material.SetFloat("_SrcBlendAlpha", 1f);
                    material.SetFloat("_DstBlendAlpha", 0f);
                    material.SetFloat("_ZWrite", 1f);
                    material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.Geometry;
                    material.SetShaderPassEnabled("ShadowCaster", true);
                }

                internal static void SetTransparentWeaponShellState(Material material)
                {
                    if (material == null) throw new ArgumentNullException(nameof(material));
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_BlendModePreserveSpecular", 0f);
                    material.SetFloat("_AlphaClip", 0f);
                    material.SetFloat("_SrcBlend", 5f);
                    material.SetFloat("_DstBlend", 10f);
                    material.SetFloat("_SrcBlendAlpha", 1f);
                    material.SetFloat("_DstBlendAlpha", 10f);
                    material.SetFloat("_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                }

                internal static Material GetOrCreateWeaponShellMaterial(string name)
                {
                    var material = GetOrCreateLitMaterial(new PbrMaterialSpecification(
                        name, LoadTexture(WeaponAccentTexturePath), LoadTexture(WeaponAccentNormalTexturePath),
                        LoadTexture(WeaponAccentMetallicTexturePath), LoadTexture(WeaponAccentOcclusionTexturePath),
                        null, null, Vector2.one, WeaponAccentShellBaseColor,
                        Color.clear, 0f, WeaponAccentShellMetallic, WeaponAccentShellSmoothness,
                        WeaponAccentShellOcclusion, WeaponAccentShellBumpScale));
                    SetTransparentWeaponShellState(material);
                    material.SetColor("_EmissionColor", Color.clear);
                    material.SetTexture("_EmissionMap", null);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    material.DisableKeyword("_EMISSION");
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateWeaponCoreMaterial(string name)
                {
                    var material = GetOrCreateLitMaterial(new PbrMaterialSpecification(
                        name, LoadTexture(WeaponAccentTexturePath), LoadTexture(WeaponAccentNormalTexturePath),
                        LoadTexture(WeaponAccentMetallicTexturePath), LoadTexture(WeaponAccentOcclusionTexturePath),
                        LoadTexture(WeaponAccentEmissionTexturePath), null, Vector2.one,
                        WeaponAccentCoreBaseColor, WeaponAccentCoreEmissionColor, WeaponAccentCoreEmissionStrength,
                        WeaponAccentCoreMetallic, WeaponAccentCoreSmoothness, WeaponAccentCoreOcclusion,
                        WeaponAccentCoreBumpScale));
                    SetOpaqueLitState(material);
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateHealthPickupMaterial()
                {
                    var material = GetOrCreateLitMaterial(new PbrMaterialSpecification(
                        "HealthPickup", null, null, null, null, null, null, Vector2.one,
                        new Color(0.10f, 0.85f, 0.25f, 1f),
                        new Color(0.18f, 1.00f, 0.35f, 1f), 2.50f,
                        0.10f, 0.65f, 1f, 1f));
                    SetOpaqueLitState(material);
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateAmmoShellMaterial()
                {
                    var material = GetOrCreateLitMaterial(new PbrMaterialSpecification(
                        "AmmoShell", null, null, null, null, null, null, Vector2.one,
                        AmmoShellBaseColor, AmmoShellEmissionColor, AmmoShellEmissionStrength,
                        0.75f, 0.80f, 1f, 1f));
                    SetOpaqueLitState(material);
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static void SetDetailNormalKeyword(Material material, bool enabled)
                {
                    material.DisableKeyword("_DETAIL");
                    material.DisableKeyword("_DETAIL_MULX2");
                    material.DisableKeyword("_DETAIL_SCALED");
                    if (enabled)
                    {
                        material.EnableKeyword(DetailNormalKeyword);
                    }
                }

                internal static Material GetOrCreateGridMaterial(string name, Vector2 gridScale)
                {
                    var path = MaterialsPath + "/" + name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var shader = Shader.Find("RocketFooxball/RetroPowerGrid");
                    if (shader == null) throw new InvalidOperationException("RocketFooxball/RetroPowerGrid shader is unavailable.");
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = shader;
                    material.SetColor("_GridColor", GridColor);
                    material.SetFloat("_Alpha", 0.11f);
                    material.SetFloat("_CellSize", 4f);
                    material.SetVector("_GridScale", new Vector4(gridScale.x, gridScale.y, 0f, 0f));
                    material.SetFloat("_MajorInterval", 5f);
                    material.SetFloat("_MinorWidth", 0.025f);
                    material.SetFloat("_MajorWidth", 0.045f);
                    material.SetColor("_FogColor", new Color(0.12f, 0.28f, 0.38f, 1f));
                    material.SetFloat("_FogStrength", 0.7f);
                    material.renderQueue = 3000;
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateShieldMaterial(string name, Color baseColor, Color emissionColor)
                {
                    var path = MaterialsPath + "/" + name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var shader = Shader.Find("RocketFooxball/RetroShield");
                    if (shader == null) throw new InvalidOperationException("RocketFooxball/RetroShield shader is unavailable.");
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = shader;
                    material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ShieldTexturePath));
                    material.SetColor("_BaseColor", baseColor);
                    material.SetColor("_EmissionColor", emissionColor);
                    material.SetFloat("_PulseSpeed", 1.2f);
                    material.SetFloat("_ScanScale", 3f);
                    material.SetFloat("_Alpha", 0.52f);
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateParticleMaterial(string name, Color color, Texture2D texture)
                {
                    var path = MaterialsPath + "/" + name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var shader = Shader.Find("RocketFooxball/RetroParticle");
                    if (shader == null)
                    {
                        throw new InvalidOperationException("RocketFooxball/RetroParticle shader is unavailable.");
                    }
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    else if (material.shader != shader)
                    {
                        material.shader = shader;
                    }
                    material.SetColor("_BaseColor", color);
                    material.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
                    material.SetTextureScale("_BaseMap", Vector2.one);
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static Material GetOrCreateAdditiveParticleMaterial(string name, Color color, Texture2D texture, float intensity)
                {
                    var path = MaterialsPath + "/" + name + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    var shader = Shader.Find("RocketFooxball/RetroAdditiveParticle");
                    if (shader == null) throw new InvalidOperationException("RocketFooxball/RetroAdditiveParticle shader is unavailable.");
                    if (material == null)
                    {
                        material = new Material(shader);
                        AssetDatabase.CreateAsset(material, path);
                    }
                    material.shader = shader;
                    material.SetColor("_BaseColor", color);
                    material.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
                    material.SetTextureScale("_BaseMap", Vector2.one);
                    material.SetFloat("_Intensity", intensity);
                    material.renderQueue = 3000;
                    EditorUtility.SetDirty(material);
                    return material;
                }

                internal static void ValidateOpaqueMaterialReferences()
                {
                    var paths = new[] { "Floor.mat", "Wall.mat", "Trim.mat", "Hazard.mat", "Marking.mat", "Ball.mat", "Rocket.mat", "RocketHot.mat", "HealthPickup.mat", "AmmoShell.mat", "ArenaPrimary.mat", "ArenaTrim.mat", "ArenaHazard.mat", "ArenaGlow.mat", "CharacterRed.mat", "CharacterBlack.mat", "CharacterCream.mat", "CharacterEye.mat", "WeaponMetal.mat", "WeaponDark.mat", "WeaponAccentCore.mat", "ShotgunMetal.mat", "ShotgunDark.mat", "ShotgunAccentCore.mat", "TeamBlue.mat", "TeamRed.mat" };
                    for (var i = 0; i < paths.Length; i++)
                    {
                        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/" + paths[i]);
                        if (material == null || material.shader == null || material.shader.name != LitShaderName) throw new InvalidOperationException("Opaque material must use URP Lit: " + paths[i]);
                        ValidateOpaqueSurfaceState(material, paths[i]);
                    }
                    ValidateWeaponMaterialAssets();
                }

                internal static void ValidateWeaponMaterialAssets()
                {
                    var launcherBaseMap = LoadTexture(LauncherBaseColorTexturePath);
                    var launcherNormalMap = LoadTexture(LauncherNormalTexturePath);
                    var launcherMetallicMap = LoadTexture(LauncherMetallicTexturePath);
                    var launcherOcclusionMap = LoadTexture(LauncherOcclusionTexturePath);
                    var launcherEmissionMap = LoadTexture(LauncherEmissionTexturePath);
                    ValidateLauncherMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponMetal.mat"), launcherBaseMap,
                        launcherNormalMap, launcherMetallicMap, launcherOcclusionMap, null, LauncherMetalBaseColor, "WeaponMetal");
                    ValidateLauncherMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponDark.mat"), launcherBaseMap,
                        launcherNormalMap, launcherMetallicMap, launcherOcclusionMap, null, LauncherDarkBaseColor, "WeaponDark");
                    ValidateLauncherMaterial(AssetDatabase.LoadAssetAtPath<Material>(WeaponAccentMaterialPath), launcherBaseMap,
                        launcherNormalMap, launcherMetallicMap, launcherOcclusionMap, null, LauncherAccentBaseColor, "WeaponAccent");
                    ValidateLauncherCoreMaterial(AssetDatabase.LoadAssetAtPath<Material>(WeaponAccentCoreMaterialPath), launcherBaseMap,
                        launcherNormalMap, launcherMetallicMap, launcherOcclusionMap, launcherEmissionMap, "WeaponAccentCore");

                    var shotgunBaseMap = LoadTexture(ShotgunBaseColorTexturePath);
                    var shotgunNormalMap = LoadTexture(ShotgunNormalTexturePath);
                    var shotgunMetallicMap = LoadTexture(ShotgunMetallicTexturePath);
                    var shotgunOcclusionMap = LoadTexture(ShotgunOcclusionTexturePath);
                    var shotgunEmissionMap = LoadTexture(ShotgunEmissionTexturePath);
                    ValidateShotgunMaterial(AssetDatabase.LoadAssetAtPath<Material>(ShotgunMetalMaterialPath), shotgunBaseMap,
                        shotgunNormalMap, shotgunMetallicMap, shotgunOcclusionMap, null, ShotgunMetalBaseColor, "ShotgunMetal");
                    ValidateShotgunMaterial(AssetDatabase.LoadAssetAtPath<Material>(ShotgunDarkMaterialPath), shotgunBaseMap,
                        shotgunNormalMap, shotgunMetallicMap, shotgunOcclusionMap, null, ShotgunDarkBaseColor, "ShotgunDark");
                    ValidateShotgunMaterial(AssetDatabase.LoadAssetAtPath<Material>(ShotgunAccentMaterialPath), shotgunBaseMap,
                        shotgunNormalMap, shotgunMetallicMap, shotgunOcclusionMap, null, ShotgunAccentBaseColor, "ShotgunAccent");
                    ValidateShotgunCoreMaterial(AssetDatabase.LoadAssetAtPath<Material>(ShotgunAccentCoreMaterialPath), shotgunBaseMap,
                        shotgunNormalMap, shotgunMetallicMap, shotgunOcclusionMap, shotgunEmissionMap, "ShotgunAccentCore");
                }

                internal static void ValidateOpaqueSurfaceState(Material material, string label)
                {
                    if (material == null || material.GetTag("RenderType", false) != "Opaque" ||
                        Mathf.Abs(material.GetFloat("_Surface")) > 0.001f || Mathf.Abs(material.GetFloat("_Blend")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_BlendModePreserveSpecular")) > 0.001f || Mathf.Abs(material.GetFloat("_AlphaClip")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_SrcBlend") - 1f) > 0.001f || Mathf.Abs(material.GetFloat("_DstBlend")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_SrcBlendAlpha") - 1f) > 0.001f || Mathf.Abs(material.GetFloat("_DstBlendAlpha")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_ZWrite") - 1f) > 0.001f || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                        material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        material.renderQueue != (int)RenderQueue.Geometry || !material.GetShaderPassEnabled("ShadowCaster"))
                    {
                        throw new InvalidOperationException(label + " must use the exact opaque URP Lit state.");
                    }
                }

                internal static void ValidateTransparentWeaponShellState(Material material, string label)
                {
                    if (material == null || material.GetTag("RenderType", false) != "Transparent" ||
                        Mathf.Abs(material.GetFloat("_Surface") - 1f) > 0.001f || Mathf.Abs(material.GetFloat("_Blend")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_BlendModePreserveSpecular")) > 0.001f || Mathf.Abs(material.GetFloat("_AlphaClip")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_SrcBlend") - 5f) > 0.001f || Mathf.Abs(material.GetFloat("_DstBlend") - 10f) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_SrcBlendAlpha") - 1f) > 0.001f || Mathf.Abs(material.GetFloat("_DstBlendAlpha") - 10f) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_ZWrite")) > 0.001f || !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                        material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        material.renderQueue != (int)RenderQueue.Transparent || material.GetShaderPassEnabled("ShadowCaster"))
                    {
                        throw new InvalidOperationException(label + " must use the exact transparent URP Lit shell state.");
                    }
                }

                internal static void ValidateGridMaterial(Material material, Vector2 scale, string label)
                {
                    if (material == null || material.shader == null || material.shader.name != "RocketFooxball/RetroPowerGrid" ||
                        Mathf.Abs(material.GetFloat("_Alpha") - 0.11f) > 0.001f || Mathf.Abs(material.GetFloat("_CellSize") - 4f) > 0.001f ||
                        material.GetVector("_GridScale") != new Vector4(scale.x, scale.y, 0f, 0f) || Mathf.Abs(material.GetFloat("_MajorInterval") - 5f) > 0.001f)
                    {
                        throw new InvalidOperationException("Containment grid material contract invalid: " + label);
                    }
                }

                internal static void ValidatePbrMaterial(Material material, Texture2D baseMap, Texture2D normalMap, Texture2D metallicMap, Texture2D occlusionMap, Texture2D emissionMap, Vector2 scale, string label)
                {
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, emissionMap, null, scale, label);
                }

                internal static void ValidatePbrMaterial(Material material, Texture2D baseMap, Texture2D normalMap, Texture2D metallicMap, Texture2D occlusionMap, Texture2D emissionMap, Texture2D detailNormalMap, Vector2 scale, string label,
                    Vector2? detailNormalTiling = null, float detailNormalScale = 1f)
                {
                    if (material == null || material.shader == null || material.shader.name != LitShaderName) throw new InvalidOperationException(label + " must use URP Lit.");
                    if (material.GetTexture("_BaseMap") != (baseMap != null ? baseMap : Texture2D.whiteTexture)) throw new InvalidOperationException(label + " base texture mismatch.");
                    if (material.GetTexture("_BumpMap") != normalMap || material.GetTexture("_MetallicGlossMap") != metallicMap || material.GetTexture("_OcclusionMap") != occlusionMap || material.GetTexture("_EmissionMap") != emissionMap) throw new InvalidOperationException(label + " PBR map routing mismatch.");
                    if (material.GetTexture("_DetailNormalMap") != detailNormalMap) throw new InvalidOperationException(label + " detail normal map mismatch.");
                    var expectedDetailNormalTiling = detailNormalTiling ?? Vector2.one;
                    if (material.GetTextureScale("_DetailNormalMap") != expectedDetailNormalTiling ||
                        Mathf.Abs(material.GetFloat("_DetailNormalMapScale") - detailNormalScale) > 0.001f)
                    {
                        throw new InvalidOperationException(label + " detail normal tiling or scale mismatch.");
                    }
                    var detailEnabled = detailNormalMap != null;
                    if (HasSerializedKeyword(material, "_DETAIL") || HasSerializedKeyword(material, "_DETAIL_SCALED") || HasSerializedKeyword(material, "_DETAIL_MULX2") != detailEnabled)
                    {
                        throw new InvalidOperationException(label + " detail normal keyword contract mismatch.");
                    }
                    if (material.GetTextureScale("_BaseMap") != scale) throw new InvalidOperationException(label + " texture scale mismatch.");
                }

                internal static void ValidatePbrScalars(Material material, float metallic, float smoothness, float occlusion, float bumpScale, float emissionStrength, string label)
                {
                    if (material == null || Mathf.Abs(material.GetFloat("_Metallic") - metallic) > 0.001f || Mathf.Abs(material.GetFloat("_Smoothness") - smoothness) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_OcclusionStrength") - occlusion) > 0.001f || Mathf.Abs(material.GetFloat("_BumpScale") - bumpScale) > 0.001f ||
                        (material.HasProperty("_EmissionStrength") && Mathf.Abs(material.GetFloat("_EmissionStrength") - emissionStrength) > 0.001f))
                    {
                        throw new InvalidOperationException(label + " PBR scalar contract mismatch.");
                    }
                }

                private static void ValidateWeaponMaterialResponse(Material material, string label)
                {
                    if (material == null) throw new InvalidOperationException(label + " weapon material is missing.");

                    var metallic = 0f;
                    var smoothness = 0f;
                    var isAccent = false;
                    var isCore = false;
                    switch (label)
                    {
                        case "WeaponMetal":
                        case "ShotgunMetal":
                            metallic = WeaponMetallicResponse;
                            smoothness = WeaponMetalSmoothnessResponse;
                            break;
                        case "WeaponDark":
                        case "ShotgunDark":
                            metallic = WeaponDarkMetallicResponse;
                            smoothness = WeaponDarkSmoothnessResponse;
                            break;
                        case "WeaponAccent":
                        case "ShotgunAccent":
                            metallic = WeaponAccentMetallicResponse;
                            smoothness = WeaponAccentSmoothnessResponse;
                            isAccent = true;
                            break;
                        case "WeaponAccentCore":
                        case "ShotgunAccentCore":
                            metallic = WeaponAccentCoreMetallicResponse;
                            smoothness = WeaponAccentCoreSmoothnessResponse;
                            isCore = true;
                            break;
                        default:
                            throw new InvalidOperationException("Unknown weapon material response label: " + label);
                    }

                    ValidatePbrScalars(material, metallic, smoothness, WeaponResponseOcclusion, WeaponResponseBumpScale,
                        isCore ? WeaponAccentCoreEmissionStrength : 0f, label);
                    if (!material.HasProperty("_EnvironmentReflections") ||
                        Mathf.Abs(material.GetFloat("_EnvironmentReflections")) > 0.001f ||
                        !material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF") ||
                        material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"))
                    {
                        throw new InvalidOperationException(label + " must disable environment reflections while keeping direct specular enabled.");
                    }

                    var expectedBaseColor = isAccent
                        ? new Color(1f, 1f, 1f, WeaponAccentAlpha)
                        : Color.white;
                    if (Vector4.Distance(material.GetColor("_BaseColor"), expectedBaseColor) > 0.001f)
                        throw new InvalidOperationException(label + " base color multiplier mismatch.");
                }

                internal static void ValidateHealthPickupMaterial(Material material)
                {
                    ValidatePbrScalars(material, 0.10f, 0.65f, 1f, 1f, 2.50f, "HealthPickup");
                    ValidateEmission(material, new Color(0.18f, 1.00f, 0.35f, 1f), 2.50f, "HealthPickup");
                    if (material == null || Mathf.Abs(material.GetFloat("_Surface")) > 0.001f || Mathf.Abs(material.GetFloat("_Blend")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_AlphaClip")) > 0.001f || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                        material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        material.renderQueue != (int)RenderQueue.Geometry)
                        throw new InvalidOperationException("HealthPickup material must be opaque URP Lit geometry.");
                    var baseColor = material.GetColor("_BaseColor");
                    if (Vector4.Distance(baseColor, new Color(0.10f, 0.85f, 0.25f, 1f)) > 0.001f)
                        throw new InvalidOperationException("HealthPickup base color contract mismatch.");
                }

                internal static void ValidateAmmoShellMaterial(Material material)
                {
                    ValidatePbrScalars(material, 0.75f, 0.80f, 1f, 1f, AmmoShellEmissionStrength, "AmmoShell");
                    ValidateEmission(material, AmmoShellEmissionColor, AmmoShellEmissionStrength, "AmmoShell");
                    if (material == null || material.shader == null || material.shader.name != LitShaderName ||
                        Mathf.Abs(material.GetFloat("_Surface")) > 0.001f || Mathf.Abs(material.GetFloat("_Blend")) > 0.001f ||
                        Mathf.Abs(material.GetFloat("_AlphaClip")) > 0.001f || material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                        material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") || material.IsKeywordEnabled("_ALPHATEST_ON") ||
                        material.renderQueue != (int)RenderQueue.Geometry)
                        throw new InvalidOperationException("AmmoShell material must be opaque URP Lit geometry.");
                    if (Vector4.Distance(material.GetColor("_BaseColor"), AmmoShellBaseColor) > 0.001f)
                        throw new InvalidOperationException("AmmoShell base color contract mismatch.");
                }

                internal static void ValidateEmission(Material material, Color baseColor, float strength, string label)
                {
                    var expected = strength > 0.001f ? baseColor * strength : Color.clear;
                    var emissive = strength > 0.001f;
                    var expectedFlags = emissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    if (material == null || Vector4.Distance(material.GetColor("_EmissionColor"), expected) > 0.01f ||
                        material.globalIlluminationFlags != expectedFlags || material.IsKeywordEnabled("_EMISSION") != emissive)
                    {
                        throw new InvalidOperationException(label + " emission contract mismatch.");
                    }
                }

                internal static bool HasSerializedKeyword(Material material, string keyword)
                {
                    return material != null && !string.IsNullOrEmpty(keyword) && material.IsKeywordEnabled(keyword);
                }

                internal static void ValidateBallMaterial(Material material)
                {
                    ValidatePbrMaterial(material, LoadTexture(BallTexturePath), LoadTexture(BallNormalTexturePath), LoadTexture(BallMetallicTexturePath), LoadTexture(BallOcclusionTexturePath), null, Vector2.one, "Ball");
                    ValidatePbrScalars(material, 1f, 1f, 0.65f, 0.45f, 0f, "Ball");
                    ValidateEmission(material, Color.clear, 0f, "Ball");
                    var baseColor = material.GetColor("_BaseColor");
                    if (Vector3.Distance(new Vector3(baseColor.r, baseColor.g, baseColor.b), Vector3.one) > 0.001f || Mathf.Abs(baseColor.a - 1f) > 0.001f)
                    {
                        throw new InvalidOperationException("Ball material must preserve neutral black/white texture color.");
                    }
                }

                internal static void ValidateWeaponMaterials(GameObject visual)
                {
                    var metal = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponMetal.mat");
                    var dark = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponDark.mat");
                    var accent = AssetDatabase.LoadAssetAtPath<Material>(WeaponAccentMaterialPath);
                    var core = AssetDatabase.LoadAssetAtPath<Material>(WeaponAccentCoreMaterialPath);
                    var baseMap = LoadTexture(LauncherBaseColorTexturePath);
                    var normalMap = LoadTexture(LauncherNormalTexturePath);
                    var metallicMap = LoadTexture(LauncherMetallicTexturePath);
                    var occlusionMap = LoadTexture(LauncherOcclusionTexturePath);
                    var emissionMap = LoadTexture(LauncherEmissionTexturePath);
                    ValidateLauncherMaterial(metal, baseMap, normalMap, metallicMap, occlusionMap, null, LauncherMetalBaseColor, "WeaponMetal");
                    ValidateLauncherMaterial(dark, baseMap, normalMap, metallicMap, occlusionMap, null, LauncherDarkBaseColor, "WeaponDark");
                    ValidateLauncherMaterial(accent, baseMap, normalMap, metallicMap, occlusionMap, null, LauncherAccentBaseColor, "WeaponAccent");
                    ValidateLauncherCoreMaterial(core, baseMap, normalMap, metallicMap, occlusionMap, emissionMap, "WeaponAccentCore");

                    var seenMetal = false;
                    var seenDark = false;
                    var seenAccent = false;
                    var seenCore = false;
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var expected = metal;
                        if (renderer.name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = core;
                            seenCore = true;
                        }
                        else if (renderer.name.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = dark;
                            seenDark = true;
                        }
                        else if (renderer.name.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = accent;
                            seenAccent = true;
                        }
                        else if (renderer.name.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0 || renderer.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            seenMetal = true;
                        }

                        var slots = renderer.sharedMaterials;
                        if (slots == null || slots.Length != 1) throw new InvalidOperationException("Weapon renderer must have exactly one material slot: " + renderer.name);
                        for (var j = 0; j < slots.Length; j++)
                        {
                            if (slots[j] != expected) throw new InvalidOperationException("Weapon material slot mapping mismatch: " + renderer.name + "/" + j);
                        }
                    }
                    if (renderers.Length != 4 || !seenMetal || !seenDark || !seenAccent || !seenCore)
                    {
                        throw new InvalidOperationException("Weapon material slots must contain exactly Metal, Dark, Accent, and AccentCore parts.");
                    }
                }

                internal static void ValidateLauncherMaterial(Material material, Texture2D baseMap, Texture2D normalMap,
                    Texture2D metallicMap, Texture2D occlusionMap, Texture2D microDetailNormal, Color baseColor, string label)
                {
                    if (microDetailNormal != null) throw new InvalidOperationException(label + " must not use the shared weapon detail normal.");
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, null, null, Vector2.one, label,
                        Vector2.one, 1f);
                    ValidateWeaponMaterialResponse(material, label);
                    ValidateEmission(material, Color.clear, 0f, label);
                    if (label == "WeaponAccent") ValidateTransparentWeaponShellState(material, label);
                    else ValidateOpaqueSurfaceState(material, label);
                    if (Vector4.Distance(material.GetColor("_BaseColor"), baseColor) > 0.001f)
                        throw new InvalidOperationException(label + " launcher base color multiplier mismatch.");
                }

                internal static void ValidateLauncherCoreMaterial(Material material, Texture2D baseMap, Texture2D normalMap,
                    Texture2D metallicMap, Texture2D occlusionMap, Texture2D emissionMap, string label)
                {
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, emissionMap, null, Vector2.one, label);
                    ValidateWeaponMaterialResponse(material, label);
                    ValidateEmission(material, WeaponAccentCoreEmissionColor, WeaponAccentCoreEmissionStrength, label);
                    ValidateOpaqueSurfaceState(material, label);
                    if (Vector4.Distance(material.GetColor("_BaseColor"), LauncherAccentCoreBaseColor) > 0.001f)
                        throw new InvalidOperationException(label + " launcher base color multiplier mismatch.");
                }

                internal static void ValidateShotgunMaterials(GameObject visual)
                {
                    if (visual == null) throw new InvalidOperationException("Shotgun visual is null.");
                    var metal = AssetDatabase.LoadAssetAtPath<Material>(ShotgunMetalMaterialPath);
                    var dark = AssetDatabase.LoadAssetAtPath<Material>(ShotgunDarkMaterialPath);
                    var accent = AssetDatabase.LoadAssetAtPath<Material>(ShotgunAccentMaterialPath);
                    var core = AssetDatabase.LoadAssetAtPath<Material>(ShotgunAccentCoreMaterialPath);
                    var baseMap = LoadTexture(ShotgunBaseColorTexturePath);
                    var normalMap = LoadTexture(ShotgunNormalTexturePath);
                    var metallicMap = LoadTexture(ShotgunMetallicTexturePath);
                    var occlusionMap = LoadTexture(ShotgunOcclusionTexturePath);
                    var emissionMap = LoadTexture(ShotgunEmissionTexturePath);
                    ValidateShotgunMaterial(metal, baseMap, normalMap, metallicMap, occlusionMap, null, ShotgunMetalBaseColor, "ShotgunMetal");
                    ValidateShotgunMaterial(dark, baseMap, normalMap, metallicMap, occlusionMap, null, ShotgunDarkBaseColor, "ShotgunDark");
                    ValidateShotgunMaterial(accent, baseMap, normalMap, metallicMap, occlusionMap, null, ShotgunAccentBaseColor, "ShotgunAccent");
                    ValidateShotgunCoreMaterial(core, baseMap, normalMap, metallicMap, occlusionMap, emissionMap, "ShotgunAccentCore");

                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length != 4) throw new InvalidOperationException("Shotgun visual must contain exactly four renderers.");
                    var seenMetal = false;
                    var seenDark = false;
                    var seenAccent = false;
                    var seenCore = false;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var expected = metal;
                        if (renderer.name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = core;
                            seenCore = true;
                        }
                        else if (renderer.name.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = dark;
                            seenDark = true;
                        }
                        else if (renderer.name.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            expected = accent;
                            seenAccent = true;
                        }
                        else if (renderer.name.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            seenMetal = true;
                        }
                        else
                        {
                            throw new InvalidOperationException("Shotgun renderer name must identify Metal, Dark, or Accent: " + renderer.name);
                        }

                        var slots = renderer.sharedMaterials;
                        if (slots == null || slots.Length != 1 || slots[0] != expected)
                            throw new InvalidOperationException("Shotgun material slot contract mismatch: " + renderer.name);
                    }
                    if (!seenMetal || !seenDark || !seenAccent || !seenCore)
                        throw new InvalidOperationException("Shotgun material slots must contain exactly Metal, Dark, Accent, and AccentCore parts.");
                }

                internal static void ValidateShotgunMaterial(Material material, Texture2D baseMap, Texture2D normalMap,
                    Texture2D metallicMap, Texture2D occlusionMap, Texture2D microDetailNormal, Color baseColor, string label)
                {
                    if (microDetailNormal != null) throw new InvalidOperationException(label + " must not use the shared weapon detail normal.");
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, null, null, Vector2.one, label,
                        Vector2.one, 1f);
                    ValidateWeaponMaterialResponse(material, label);
                    ValidateEmission(material, Color.clear, 0f, label);
                    if (label == "ShotgunAccent") ValidateTransparentWeaponShellState(material, label); else ValidateOpaqueSurfaceState(material, label);
                    var actual = material.GetColor("_BaseColor");
                    if (Vector4.Distance(actual, baseColor) > 0.001f)
                    {
                        throw new InvalidOperationException(label + " shotgun atlas base color multiplier mismatch.");
                    }
                }

                internal static void ValidateShotgunCoreMaterial(Material material, Texture2D baseMap, Texture2D normalMap,
                    Texture2D metallicMap, Texture2D occlusionMap, Texture2D emissionMap, string label)
                {
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, emissionMap, null, Vector2.one, label);
                    ValidateWeaponMaterialResponse(material, label);
                    ValidateEmission(material, ShotgunAccentCoreEmissionColor, ShotgunAccentCoreEmissionStrength, label);
                    ValidateOpaqueSurfaceState(material, label);
                    if (Vector4.Distance(material.GetColor("_BaseColor"), ShotgunAccentCoreBaseColor) > 0.001f)
                        throw new InvalidOperationException(label + " shotgun atlas base color multiplier mismatch.");
                }

                internal static void ValidateRocketMaterial(Material material, string rendererName)
                {
                    var hot = rendererName == "RocketHot";
                    if (rendererName != "RocketSurface" && !hot) throw new InvalidOperationException("Unknown rocket renderer for material validation: " + rendererName);
                    var expected = AssetDatabase.LoadAssetAtPath<Material>(hot ? RocketHotMaterialPath : MaterialsPath + "/Rocket.mat");
                    if (material != expected) throw new InvalidOperationException("Rocket material asset routing mismatch: " + rendererName);
                    ValidatePbrMaterial(material, LoadTexture(RocketTexturePath), LoadTexture(RocketNormalTexturePath), LoadTexture(RocketMetallicTexturePath), LoadTexture(RocketOcclusionTexturePath), hot ? LoadTexture(RocketEmissionTexturePath) : null, Vector2.one, rendererName);
                    ValidatePbrScalars(material, 1f, 1f, 0.85f, 0.80f, hot ? RocketEmissionStrength : 0f, rendererName);
                    ValidateEmission(material, hot ? RocketEmissionColor : Color.clear, hot ? RocketEmissionStrength : 0f, rendererName);
                    if (Vector4.Distance(material.GetColor("_BaseColor"), Color.white) > 0.001f)
                    {
                        throw new InvalidOperationException("Rocket PBR emission material contract invalid.");
                    }
                }

    }
}
