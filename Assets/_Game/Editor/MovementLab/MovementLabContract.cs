using System;
using UnityEditor.Animations;
using UnityEngine;

namespace RocketFooxball.Editor
{
    internal static class MovementLabContract
    {
        // Stage-local manifests carry explicit ownership, stale reasons, and a
        // top-level fingerprint/path union. Bump whenever that wire contract changes.
        internal const int ManifestSchemaVersion = 8;
        internal const int SerializedContractVersion = 2;
        internal const string ManifestPath = "Assets/_Game/Generated/MovementLabBuildManifest.json";
        internal const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        internal const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        internal const string BallPrefabPath = "Assets/_Game/Prefabs/Ball.prefab";
        internal const string RocketPrefabPath = "Assets/_Game/Prefabs/Rocket.prefab";
        internal const string ExplosionPrefabPath = "Assets/_Game/Prefabs/ExplosionVfx.prefab";
        internal const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        internal const string MaterialsPath = "Assets/_Game/Materials";
        internal const string TexturesPath = "Assets/_Game/Textures";
        internal const string ShadersPath = "Assets/_Game/Shaders";
        internal const string AnimationsPath = "Assets/_Game/Animations";
        internal const string GeneratedPath = "Assets/_Game/Generated";
        internal const string BlueCircleCueMeshPath = GeneratedPath + "/BlueCircleCueMesh.asset";
        internal const string RedTriangleCueMeshPath = GeneratedPath + "/RedTriangleCueMesh.asset";
        internal const string LightingPath = "Assets/_Game/Lighting";
        internal const string BakedLightingPath = "Assets/_Game/Scenes/MovementLab";
        internal const string VolumeProfilePath = LightingPath + "/MovementLabVolumeProfile.asset";
        internal const string LightingSettingsPath = LightingPath + "/MovementLabLightingSettings.asset";
        internal const string LightingManifestPath = LightingPath + "/MovementLabLightingManifest.json";
        internal const string BuildMarkerPrefix = "MovementLabGeneratedT8_";

        internal const float BallPrefabScale = 4.32f;
        internal const float BallRadius = 2.16f;
        internal const float BallSpawnHeight = BallRadius;
        internal const float BlastRadius = 5.85f;
        internal const float BlastVisualScale = 1.30f;
        internal const float GoalAxisPosition = 64f;
        internal const float PlayerSpawnOffset = 12f;
        internal const float GoalFreezeDuration = 5f;
        internal const float CelebrationOrbitRadius = 5.5f;
        internal const float CelebrationOrbitHeight = 2.5f;
        internal const float CelebrationLookHeight = 1.05f;
        internal const float CelebrationOrbitDegrees = 360f;
        internal const float CelebrationFov = 60f;
        internal const float RocketTrailLifetime = 0.55f;
        internal const float RocketTrailRateOverDistance = 1.5f;
        internal const float RocketTrailStartSize = 0.70f;
        internal const float RocketEmissionStrength = 3.0f;
        internal const float JumpVelocity = 4.80f;
        internal const float UnderfootForwardImpulseScale = 0.5625f;
        internal const float UnderfootUpwardImpulseScale = 1f;
        internal const float UnderfootHighSpeedVerticalRedirect = 1f;

        internal static readonly Color RocketTrailStartColor = new Color(0.58f, 0.55f, 0.50f, 0.75f);
        internal static readonly Color RocketTrailEndColor = new Color(0.20f, 0.19f, 0.18f, 1f);
        internal static readonly Color RocketEmissionColor = new Color(1.00f, 0.20f, 0.04f, 1f);
        internal static readonly Color RocketBaseColor = Color.white;
        internal static readonly Color WeaponMetalBaseColor = new Color(0.95f, 0.86f, 0.70f, 1f);
        internal static readonly Color WeaponDarkBaseColor = new Color(0.88f, 0.90f, 0.92f, 1f);
        internal static readonly Color WeaponAccentBaseColor = new Color(0.95f, 0.56f, 0.38f, 1f);
        internal static readonly Color ExplosionFireMaterialColor = Color.white;
        internal static readonly Color ExplosionSmokeMaterialColor = new Color(0.52f, 0.49f, 0.44f, 0.72f);
        internal static readonly Color GridColor = new Color(0.12f, 0.50f, 0.72f, 1f);

        internal static readonly string[] ImportedAssetPaths =
        {
            "Assets/_Game/Models/LowPolyRocket.fbx",
            "Assets/_Game/Models/ArenaKit.fbx",
            "Assets/_Game/Models/LowPolyCharacter.fbx",
            "Assets/_Game/Models/FpsKickRig.fbx",
            "Assets/_Game/Models/FpsRocketLauncher.fbx",
            TexturesPath + "/RetroGrass.png", TexturesPath + "/RetroGrass_Normal.png", TexturesPath + "/RetroGrass_MetallicSmoothness.png", TexturesPath + "/RetroGrass_Occlusion.png",
            TexturesPath + "/RetroWall.png", TexturesPath + "/RetroWall_Normal.png", TexturesPath + "/RetroWall_MetallicSmoothness.png", TexturesPath + "/RetroWall_Occlusion.png",
            TexturesPath + "/RetroTrim.png", TexturesPath + "/RetroTrim_Normal.png", TexturesPath + "/RetroTrim_MetallicSmoothness.png", TexturesPath + "/RetroTrim_Occlusion.png",
            TexturesPath + "/RetroHazard.png", TexturesPath + "/RetroHazard_Normal.png", TexturesPath + "/RetroHazard_MetallicSmoothness.png", TexturesPath + "/RetroHazard_Occlusion.png",
            TexturesPath + "/RetroDetailNormal.png", TexturesPath + "/RetroShield.png",
            TexturesPath + "/RetroBall.png", TexturesPath + "/RetroBall_Normal.png", TexturesPath + "/RetroBall_MetallicSmoothness.png", TexturesPath + "/RetroBall_Occlusion.png",
            TexturesPath + "/RetroWeaponMetal.png", TexturesPath + "/RetroWeaponMetal_Normal.png", TexturesPath + "/RetroWeaponMetal_MetallicSmoothness.png", TexturesPath + "/RetroWeaponMetal_Occlusion.png",
            TexturesPath + "/RetroWeaponDark.png", TexturesPath + "/RetroWeaponDark_Normal.png", TexturesPath + "/RetroWeaponDark_MetallicSmoothness.png", TexturesPath + "/RetroWeaponDark_Occlusion.png",
            TexturesPath + "/RetroWeaponAccent.png", TexturesPath + "/RetroWeaponAccent_Normal.png", TexturesPath + "/RetroWeaponAccent_MetallicSmoothness.png", TexturesPath + "/RetroWeaponAccent_Occlusion.png", TexturesPath + "/RetroWeaponAccent_Emission.png",
            TexturesPath + "/RetroRocket.png", TexturesPath + "/RetroRocket_Normal.png", TexturesPath + "/RetroRocket_MetallicSmoothness.png", TexturesPath + "/RetroRocket_Occlusion.png", TexturesPath + "/RetroRocket_Emission.png", TexturesPath + "/RetroRocketGlow.png",
            TexturesPath + "/RetroExplosion.png", TexturesPath + "/RetroSmoke.png", TexturesPath + "/RetroSunnySky.png"
        };

        internal static readonly string[] ImporterContractInputs = WithMetas(ImportedAssetPaths);

        internal static readonly string[] MaterialPrefabOutputs =
        {
            PlayerPrefabPath, BallPrefabPath, RocketPrefabPath, ExplosionPrefabPath,
            AnimationsPath + "/WorldCharacter.controller", AnimationsPath + "/FpsKick.controller",
            MaterialsPath + "/Floor.mat", MaterialsPath + "/Wall.mat", MaterialsPath + "/Trim.mat", MaterialsPath + "/Hazard.mat",
            MaterialsPath + "/Marking.mat", MaterialsPath + "/Ball.mat", MaterialsPath + "/Rocket.mat", MaterialsPath + "/RocketHot.mat",
            MaterialsPath + "/ProjectileGlow.mat", MaterialsPath + "/GoalFrame.mat", MaterialsPath + "/Shield.mat", MaterialsPath + "/ShieldBlue.mat",
            MaterialsPath + "/ShieldRed.mat", MaterialsPath + "/ArenaPrimary.mat", MaterialsPath + "/ArenaTrim.mat", MaterialsPath + "/ArenaHazard.mat",
            MaterialsPath + "/ArenaGlow.mat", MaterialsPath + "/BallSurface.physicMaterial", MaterialsPath + "/Explosion.mat",
            MaterialsPath + "/ExplosionAdditive.mat", MaterialsPath + "/ExplosionSparks.mat", MaterialsPath + "/Smoke.mat",
            MaterialsPath + "/ContainmentGridCeiling.mat", MaterialsPath + "/ContainmentGridLongWall.mat", MaterialsPath + "/ContainmentGridEndWall.mat",
            MaterialsPath + "/RetroSunnySky.mat", MaterialsPath + "/CharacterRed.mat", MaterialsPath + "/CharacterBlack.mat",
            MaterialsPath + "/CharacterCream.mat", MaterialsPath + "/CharacterEye.mat", MaterialsPath + "/WeaponMetal.mat",
            MaterialsPath + "/WeaponDark.mat", MaterialsPath + "/WeaponAccent.mat",
            MaterialsPath + "/TeamBlue.mat", MaterialsPath + "/TeamRed.mat",
            MaterialsPath + "/TeamBlueShield.mat", MaterialsPath + "/TeamRedShield.mat",
            MaterialsPath + "/TeamBlueTrail.mat", MaterialsPath + "/TeamRedTrail.mat",
            BlueCircleCueMeshPath, RedTriangleCueMeshPath
        };

        internal static readonly string[] GameplaySceneOutputs =
        {
            ScenePath,
            "ProjectSettings/EditorBuildSettings.asset",
            "ProjectSettings/DynamicsManager.asset",
            "ProjectSettings/TimeManager.asset",
            "ProjectSettings/TagManager.asset"
        };

        internal static readonly string[] QualityOutputs =
        {
            GraphicsQualityConfigurator.HighPipelinePath,
            GraphicsQualityConfigurator.HighRendererPath,
            GraphicsQualityConfigurator.LowPipelinePath,
            GraphicsQualityConfigurator.LowRendererPath,
            GraphicsQualityConfigurator.IterationPipelinePath,
            GraphicsQualityConfigurator.IterationRendererPath,
            GraphicsQualityConfigurator.QualitySettingsPath,
            GraphicsQualityConfigurator.ProjectSettingsPath
        };

        internal static readonly string[] BakedOutputPaths =
        {
            ScenePath,
            BakedLightingPath + "/LightingData.asset",
            BakedLightingPath + "/Lightmap-0_comp_dir.png", BakedLightingPath + "/Lightmap-0_comp_light.exr", BakedLightingPath + "/Lightmap-0_comp_shadowmask.png",
            BakedLightingPath + "/Lightmap-1_comp_dir.png", BakedLightingPath + "/Lightmap-1_comp_light.exr", BakedLightingPath + "/Lightmap-1_comp_shadowmask.png",
            BakedLightingPath + "/Lightmap-2_comp_dir.png", BakedLightingPath + "/Lightmap-2_comp_light.exr", BakedLightingPath + "/Lightmap-2_comp_shadowmask.png",
            BakedLightingPath + "/Lightmap-3_comp_dir.png", BakedLightingPath + "/Lightmap-3_comp_light.exr", BakedLightingPath + "/Lightmap-3_comp_shadowmask.png",
            BakedLightingPath + "/Lightmap-4_comp_dir.png", BakedLightingPath + "/Lightmap-4_comp_light.exr", BakedLightingPath + "/Lightmap-4_comp_shadowmask.png",
            BakedLightingPath + "/ReflectionProbe-0.exr", BakedLightingPath + "/ReflectionProbe-1.exr", BakedLightingPath + "/ReflectionProbe-2.exr", BakedLightingPath + "/ReflectionProbe-3.exr",
            LightingManifestPath
        };

        internal static readonly WorldAnimatorTransitionSpecification[] WorldAnimatorTransitions = CreateWorldAnimatorTransitions();

        internal readonly struct GeometrySpecification
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Vector3 Scale;

            internal GeometrySpecification(string name, Vector3 position, Vector3 scale)
            {
                Name = name;
                Position = position;
                Scale = scale;
            }
        }

        internal readonly struct WorldAnimatorConditionSpecification
        {
            internal readonly AnimatorConditionMode Mode;
            internal readonly float Threshold;
            internal readonly string Parameter;

            internal WorldAnimatorConditionSpecification(AnimatorConditionMode mode, float threshold, string parameter)
            {
                Mode = mode;
                Threshold = threshold;
                Parameter = parameter;
            }
        }

        internal readonly struct WorldAnimatorTransitionSpecification
        {
            internal readonly string Source;
            internal readonly string Destination;
            internal readonly bool AnyState;
            internal readonly bool HasExitTime;
            internal readonly float ExitTime;
            internal readonly float Duration;
            internal readonly bool CanTransitionToSelf;
            internal readonly WorldAnimatorConditionSpecification[] Conditions;

            internal WorldAnimatorTransitionSpecification(string source, string destination, bool anyState, bool hasExitTime,
                float exitTime, float duration, bool canTransitionToSelf, params WorldAnimatorConditionSpecification[] conditions)
            {
                Source = source;
                Destination = destination;
                AnyState = anyState;
                HasExitTime = hasExitTime;
                ExitTime = exitTime;
                Duration = duration;
                CanTransitionToSelf = canTransitionToSelf;
                Conditions = conditions ?? Array.Empty<WorldAnimatorConditionSpecification>();
            }
        }

        internal readonly struct MaterialSpecification
        {
            internal readonly string Name;
            internal readonly string ShaderName;
            internal readonly Texture2D Texture;
            internal readonly Vector2 TextureScale;
            internal readonly Color BaseColor;
            internal readonly Color ShadowColor;
            internal readonly Color AmbientColor;
            internal readonly float AmbientStrength;
            internal readonly Color RimColor;
            internal readonly float RimPower;
            internal readonly float RimStrength;
            internal readonly Color EmissionColor;
            internal readonly float EmissionStrength;

            internal MaterialSpecification(string name, string shaderName, Texture2D texture, Vector2 textureScale,
                Color baseColor, Color shadowColor, Color ambientColor, float ambientStrength, Color rimColor,
                float rimPower, float rimStrength, Color emissionColor, float emissionStrength)
            {
                Name = name; ShaderName = shaderName; Texture = texture; TextureScale = textureScale;
                BaseColor = baseColor; ShadowColor = shadowColor; AmbientColor = ambientColor;
                AmbientStrength = ambientStrength; RimColor = rimColor; RimPower = rimPower;
                RimStrength = rimStrength; EmissionColor = emissionColor; EmissionStrength = emissionStrength;
            }
        }

        internal readonly struct PbrMaterialSpecification
        {
            internal readonly string Name;
            internal readonly Texture2D BaseMap;
            internal readonly Texture2D NormalMap;
            internal readonly Texture2D MetallicGlossMap;
            internal readonly Texture2D OcclusionMap;
            internal readonly Texture2D EmissionMap;
            internal readonly Texture2D DetailNormalMap;
            internal readonly Vector2 TextureScale;
            internal readonly Color BaseColor;
            internal readonly Color EmissionColor;
            internal readonly float EmissionStrength;
            internal readonly float Metallic;
            internal readonly float Smoothness;
            internal readonly float OcclusionStrength;
            internal readonly float BumpScale;

            internal PbrMaterialSpecification(string name, Texture2D baseMap, Texture2D normalMap,
                Texture2D metallicGlossMap, Texture2D occlusionMap, Texture2D emissionMap,
                Texture2D detailNormalMap, Vector2 textureScale, Color baseColor, Color emissionColor,
                float emissionStrength, float metallic, float smoothness, float occlusionStrength, float bumpScale)
            {
                Name = name; BaseMap = baseMap; NormalMap = normalMap; MetallicGlossMap = metallicGlossMap;
                OcclusionMap = occlusionMap; EmissionMap = emissionMap; DetailNormalMap = detailNormalMap;
                TextureScale = textureScale; BaseColor = baseColor; EmissionColor = emissionColor;
                EmissionStrength = emissionStrength; Metallic = metallic; Smoothness = smoothness;
                OcclusionStrength = occlusionStrength; BumpScale = bumpScale;
            }
        }

        private static string[] WithMetas(string[] paths)
        {
            var result = new string[paths.Length * 2];
            for (var i = 0; i < paths.Length; i++)
            {
                result[i * 2] = paths[i];
                result[(i * 2) + 1] = paths[i] + ".meta";
            }
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static WorldAnimatorTransitionSpecification[] CreateWorldAnimatorTransitions()
        {
            const float blend = 0.02f;
            return new[]
            {
                Transition("Idle", "Run", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Greater, 0.30f, "Speed")),
                Transition("Run", "Idle", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                Transition("Idle", "Jump", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Idle", "Fall", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Run", "Jump", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Run", "Fall", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Jump", "Fall", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Less, 0f, "VerticalSpeed")),
                Transition("Jump", "Land", false, false, 0f, blend, true, Condition(AnimatorConditionMode.If, 0f, "Grounded")),
                Transition("Fall", "Jump", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Fall", "Land", false, false, 0f, blend, true, Condition(AnimatorConditionMode.If, 0f, "Grounded")),
                Transition("Land", "Jump", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Land", "Fall", false, false, 0f, blend, true, Condition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), Condition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                Transition("Land", "Idle", false, true, 0.65f, blend, true, Condition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                Transition("Land", "Run", false, true, 0.65f, blend, true, Condition(AnimatorConditionMode.Greater, 0.20f, "Speed")),
                Transition("Kick", "Idle", false, true, 1f, blend, true, Condition(AnimatorConditionMode.If, 0f, "Grounded"), Condition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                Transition("Kick", "Run", false, true, 1f, blend, true, Condition(AnimatorConditionMode.If, 0f, "Grounded"), Condition(AnimatorConditionMode.Greater, 0.20f, "Speed")),
                Transition("Kick", "Jump", false, true, 1f, blend, true, Condition(AnimatorConditionMode.IfNot, 0f, "Grounded"), Condition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed")),
                Transition("Kick", "Fall", false, true, 1f, blend, true, Condition(AnimatorConditionMode.IfNot, 0f, "Grounded"), Condition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed")),
                Transition("AnyState", "Kick", true, false, 0f, blend, false, Condition(AnimatorConditionMode.If, 0f, "Kick"))
            };
        }

        private static WorldAnimatorConditionSpecification Condition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            return new WorldAnimatorConditionSpecification(mode, threshold, parameter);
        }

        private static WorldAnimatorTransitionSpecification Transition(string source, string destination, bool anyState,
            bool hasExitTime, float exitTime, float duration, bool canTransitionToSelf, params WorldAnimatorConditionSpecification[] conditions)
        {
            return new WorldAnimatorTransitionSpecification(source, destination, anyState, hasExitTime, exitTime, duration, canTransitionToSelf, conditions);
        }
    }
}
