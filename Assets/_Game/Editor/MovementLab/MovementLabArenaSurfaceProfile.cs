using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

using static RocketFooxball.Editor.MovementLabContractCatalog;

namespace RocketFooxball.Editor
{
    internal static class MovementLabArenaSurfaceProfile
    {
        internal enum Preset
        {
            Satin,
            Gloss
        }

        internal enum Surface
        {
            Floor,
            Wall,
            ArenaPrimary
        }

        internal const Preset SelectedPreset = Preset.Satin;

        private readonly struct SurfaceExpectation
        {
            internal readonly string BaseMapPath;
            internal readonly string NormalMapPath;
            internal readonly string OcclusionMapPath;
            internal readonly Color BaseColor;
            internal readonly Vector2 TextureScale;
            internal readonly float BumpScale;
            internal readonly float SatinSmoothness;
            internal readonly float GlossSmoothness;

            internal SurfaceExpectation(string baseMapPath, string normalMapPath, string occlusionMapPath,
                Vector2 textureScale, float bumpScale, float satinSmoothness, float glossSmoothness)
            {
                BaseMapPath = baseMapPath;
                NormalMapPath = normalMapPath;
                OcclusionMapPath = occlusionMapPath;
                BaseColor = Color.white;
                TextureScale = textureScale;
                BumpScale = bumpScale;
                SatinSmoothness = satinSmoothness;
                GlossSmoothness = glossSmoothness;
            }
        }

        private static readonly IReadOnlyDictionary<Surface, SurfaceExpectation> ExpectedBySurface =
            new ReadOnlyDictionary<Surface, SurfaceExpectation>(new Dictionary<Surface, SurfaceExpectation>
            {
                { Surface.Floor, new SurfaceExpectation(GrassTexturePath, GrassNormalTexturePath, GrassOcclusionTexturePath,
                    new Vector2(13f, 9f), 0.55f, 0.32f, 0.52f) },
                { Surface.Wall, new SurfaceExpectation(WallTexturePath, WallNormalTexturePath, WallOcclusionTexturePath,
                    new Vector2(13f, 2f), 0.45f, 0.46f, 0.68f) },
                { Surface.ArenaPrimary, new SurfaceExpectation(WallTexturePath, WallNormalTexturePath, WallOcclusionTexturePath,
                    new Vector2(13f, 2f), 0.45f, 0.46f, 0.68f) }
            });

        internal static void Apply(Material material, Surface surface, Preset preset)
        {
            var expectation = GetExpectation(material, surface, preset, out var smoothness);

            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_OcclusionStrength", 1f);
            material.SetFloat("_BumpScale", expectation.BumpScale);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            material.DisableKeyword("_METALLICSPECGLOSSMAP");
            material.DisableKeyword("_EMISSION");
            material.DisableKeyword("_DETAIL");
            material.DisableKeyword("_DETAIL_MULX2");
            material.DisableKeyword("_DETAIL_SCALED");
            if (material.HasProperty("_EmissionStrength")) material.SetFloat("_EmissionStrength", 0f);
            material.SetColor("_EmissionColor", Color.clear);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 1f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        internal static void Validate(Material material, Surface surface, Preset preset)
        {
            var expectation = GetExpectation(material, surface, preset, out var smoothness);
            var label = surface.ToString();
            var baseMap = LoadExpectedTexture(expectation.BaseMapPath);
            var normalMap = LoadExpectedTexture(expectation.NormalMapPath);
            var occlusionMap = LoadExpectedTexture(expectation.OcclusionMapPath);

            if (material.GetTexture("_BaseMap") != baseMap ||
                material.GetTexture("_BumpMap") != normalMap ||
                material.GetTexture("_OcclusionMap") != occlusionMap ||
                material.GetTexture("_MetallicGlossMap") != null ||
                material.GetTexture("_EmissionMap") != null ||
                material.GetTexture("_DetailNormalMap") != null)
            {
                throw new InvalidOperationException(label + " arena surface texture routing contract invalid.");
            }

            if (Vector4.Distance(material.GetColor("_BaseColor"), expectation.BaseColor) > 0.001f ||
                material.GetTextureScale("_BaseMap") != expectation.TextureScale)
            {
                throw new InvalidOperationException(label + " arena surface color or tiling contract invalid.");
            }

            if (Mathf.Abs(material.GetFloat("_Metallic")) > 0.001f ||
                Mathf.Abs(material.GetFloat("_Smoothness") - smoothness) > 0.001f ||
                Mathf.Abs(material.GetFloat("_OcclusionStrength") - 1f) > 0.001f ||
                Mathf.Abs(material.GetFloat("_BumpScale") - expectation.BumpScale) > 0.001f ||
                Mathf.Abs(material.GetFloat("_SmoothnessTextureChannel")) > 0.001f ||
                material.IsKeywordEnabled("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A") ||
                material.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ||
                !material.IsKeywordEnabled("_NORMALMAP") ||
                !material.IsKeywordEnabled("_OCCLUSIONMAP") ||
                material.IsKeywordEnabled("_EMISSION") ||
                material.IsKeywordEnabled("_DETAIL") ||
                material.IsKeywordEnabled("_DETAIL_MULX2") ||
                material.IsKeywordEnabled("_DETAIL_SCALED") ||
                !material.HasProperty("_SpecularHighlights") ||
                Mathf.Abs(material.GetFloat("_SpecularHighlights") - 1f) > 0.001f ||
                material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF") ||
                !material.HasProperty("_EnvironmentReflections") ||
                Mathf.Abs(material.GetFloat("_EnvironmentReflections") - 1f) > 0.001f ||
                material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF") ||
                Vector4.Distance(material.GetColor("_EmissionColor"), Color.clear) > 0.001f ||
                (material.HasProperty("_EmissionStrength") && Mathf.Abs(material.GetFloat("_EmissionStrength")) > 0.001f) ||
                material.globalIlluminationFlags != MaterialGlobalIlluminationFlags.EmissiveIsBlack)
            {
                throw new InvalidOperationException(label + " arena surface response contract invalid.");
            }

            MovementLabMaterialPipeline.ValidateOpaqueSurfaceState(material, label);
        }

        private static SurfaceExpectation GetExpectation(Material material, Surface surface, Preset preset, out float smoothness)
        {
            if (material == null) throw new InvalidOperationException("Arena surface material is null.");
            if (material.shader == null || !string.Equals(material.shader.name, LitShaderName, StringComparison.Ordinal))
                throw new InvalidOperationException(surface + " arena surface must use URP Lit.");
            if (!ExpectedBySurface.TryGetValue(surface, out var expectation))
                throw new InvalidOperationException("Unsupported arena surface: " + surface);

            switch (preset)
            {
                case Preset.Satin:
                    smoothness = expectation.SatinSmoothness;
                    break;
                case Preset.Gloss:
                    smoothness = expectation.GlossSmoothness;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported arena surface preset: " + preset);
            }

            return expectation;
        }

        private static Texture2D LoadExpectedTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Missing arena surface texture: " + path);
            return texture;
        }
    }
}
