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
                    material.SetFloat("_DetailNormalMapScale", 1f);
                    material.SetTextureScale("_BaseMap", specification.TextureScale);
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
                        null, LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponAccentShellBaseColor,
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
                        LoadTexture(WeaponAccentEmissionTexturePath), LoadTexture(DetailNormalTexturePath), Vector2.one,
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

                internal static void ValidatePbrMaterial(Material material, Texture2D baseMap, Texture2D normalMap, Texture2D metallicMap, Texture2D occlusionMap, Texture2D emissionMap, Texture2D detailNormalMap, Vector2 scale, string label)
                {
                    if (material == null || material.shader == null || material.shader.name != LitShaderName) throw new InvalidOperationException(label + " must use URP Lit.");
                    if (material.GetTexture("_BaseMap") != (baseMap != null ? baseMap : Texture2D.whiteTexture)) throw new InvalidOperationException(label + " base texture mismatch.");
                    if (material.GetTexture("_BumpMap") != normalMap || material.GetTexture("_MetallicGlossMap") != metallicMap || material.GetTexture("_OcclusionMap") != occlusionMap || material.GetTexture("_EmissionMap") != emissionMap) throw new InvalidOperationException(label + " PBR map routing mismatch.");
                    if (material.GetTexture("_DetailNormalMap") != detailNormalMap) throw new InvalidOperationException(label + " detail normal map mismatch.");
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
                    ValidateLauncherMaterial(metal, baseMap, normalMap, metallicMap, occlusionMap, LauncherMetalBaseColor, "WeaponMetal");
                    ValidateLauncherMaterial(dark, baseMap, normalMap, metallicMap, occlusionMap, LauncherDarkBaseColor, "WeaponDark");
                    ValidateLauncherMaterial(accent, baseMap, normalMap, metallicMap, occlusionMap, LauncherAccentBaseColor, "WeaponAccent");
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
                    Texture2D metallicMap, Texture2D occlusionMap, Color baseColor, string label)
                {
                    ValidatePbrMaterial(material, baseMap, normalMap, metallicMap, occlusionMap, null, null, Vector2.one, label);
                    ValidatePbrScalars(material, LauncherMetallic, LauncherSmoothness, LauncherOcclusion, LauncherBumpScale, 0f, label);
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
                    ValidatePbrScalars(material, LauncherMetallic, LauncherSmoothness,
                        LauncherOcclusion, LauncherBumpScale, WeaponAccentCoreEmissionStrength, label);
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
                    ValidateWeaponMaterial(metal, LoadTexture(WeaponMetalTexturePath), ShotgunMetalBaseColor, "WeaponMetal");
                    ValidateWeaponMaterial(dark, LoadTexture(WeaponDarkTexturePath), ShotgunDarkBaseColor, "WeaponDark");
                    ValidateWeaponMaterial(accent, LoadTexture(WeaponAccentTexturePath), ShotgunAccentBaseColor, "ShotgunAccent");
                    ValidateWeaponCoreMaterial(core, "ShotgunAccentCore");

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

                internal static void ValidateWeaponMaterial(Material material, Texture2D texture, Color baseColor, string label)
                {
                    var normal = label == "WeaponMetal" ? LoadTexture(WeaponMetalNormalTexturePath) : label == "WeaponDark" ? LoadTexture(WeaponDarkNormalTexturePath) : LoadTexture(WeaponAccentNormalTexturePath);
                    var metallic = label == "WeaponMetal" ? LoadTexture(WeaponMetalMetallicTexturePath) : label == "WeaponDark" ? LoadTexture(WeaponDarkMetallicTexturePath) : LoadTexture(WeaponAccentMetallicTexturePath);
                    var occlusion = label == "WeaponMetal" ? LoadTexture(WeaponMetalOcclusionTexturePath) : label == "WeaponDark" ? LoadTexture(WeaponDarkOcclusionTexturePath) : LoadTexture(WeaponAccentOcclusionTexturePath);
                    var isAccentShell = label == "WeaponAccent" || label == "ShotgunAccent";
                    Texture2D emission = null;
                    ValidatePbrMaterial(material, texture, normal, metallic, occlusion, emission, LoadTexture(DetailNormalTexturePath), Vector2.one, label);
                    var occlusionStrength = isAccentShell ? WeaponAccentShellOcclusion : 0.70f;
                    var metallicValue = isAccentShell ? WeaponAccentShellMetallic : 1f;
                    var smoothnessValue = isAccentShell ? WeaponAccentShellSmoothness : 1f;
                    var bumpValue = isAccentShell ? WeaponAccentShellBumpScale : 1f;
                    ValidatePbrScalars(material, metallicValue, smoothnessValue, occlusionStrength, bumpValue, 0f, label);
                    ValidateEmission(material, Color.clear, 0f, label);
                    if (isAccentShell) ValidateTransparentWeaponShellState(material, label); else ValidateOpaqueSurfaceState(material, label);
                    var actual = material.GetColor("_BaseColor");
                    if (Vector4.Distance(actual, baseColor) > 0.001f)
                    {
                        throw new InvalidOperationException(label + " base color contract mismatch.");
                    }
                }

                internal static void ValidateWeaponCoreMaterial(Material material, string label)
                {
                    ValidatePbrMaterial(material, LoadTexture(WeaponAccentTexturePath), LoadTexture(WeaponAccentNormalTexturePath),
                        LoadTexture(WeaponAccentMetallicTexturePath), LoadTexture(WeaponAccentOcclusionTexturePath),
                        LoadTexture(WeaponAccentEmissionTexturePath), LoadTexture(DetailNormalTexturePath), Vector2.one, label);
                    ValidatePbrScalars(material, WeaponAccentCoreMetallic, WeaponAccentCoreSmoothness,
                        WeaponAccentCoreOcclusion, WeaponAccentCoreBumpScale, WeaponAccentCoreEmissionStrength, label);
                    ValidateEmission(material, WeaponAccentCoreEmissionColor, WeaponAccentCoreEmissionStrength, label);
                    ValidateOpaqueSurfaceState(material, label);
                    if (Vector4.Distance(material.GetColor("_BaseColor"), WeaponAccentCoreBaseColor) > 0.001f)
                        throw new InvalidOperationException(label + " base color contract mismatch.");
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
