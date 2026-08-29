using System;
using System.Collections.Generic;
using System.IO;
using RocketFooxball.Runtime.Participants;
using UnityEngine;

namespace RocketFooxball.Editor
{
    // Shared immutable path, output, lighting, and fingerprint contracts.
    // Generation and validation modules consume this catalog; no command facade bridge.
    internal static class MovementLabContractCatalog
    {
        internal const string PrefabPath = MovementLabContract.PlayerPrefabPath;
        internal const string BallPrefabPath = MovementLabContract.BallPrefabPath;
        internal const string RocketPrefabPath = MovementLabContract.RocketPrefabPath;
        internal const string RocketModelPath = "Assets/_Game/Models/LowPolyRocket.fbx";
        internal const string ArenaKitModelPath = "Assets/_Game/Models/ArenaKit.fbx";
        internal const string CharacterModelPath = "Assets/_Game/Models/LowPolyCharacter.fbx";
        internal const string FpsKickModelPath = "Assets/_Game/Models/FpsKickRig.fbx";
        internal const string WeaponModelPath = "Assets/_Game/Models/FpsRocketLauncher.fbx";
        internal const string FpsShotgunModelPath = "Assets/_Game/Models/FpsShotgun.fbx";
        internal const string ShotgunModelPath = "Assets/_Game/Models/Shotgun.fbx";
        internal const string LauncherBaseColorTexturePath = MovementLabContract.LauncherBaseColorTexturePath;
        internal const string LauncherNormalTexturePath = MovementLabContract.LauncherNormalTexturePath;
        internal const string LauncherMetallicTexturePath = MovementLabContract.LauncherMetallicTexturePath;
        internal const string LauncherOcclusionTexturePath = MovementLabContract.LauncherOcclusionTexturePath;
        internal const string LauncherEmissionTexturePath = MovementLabContract.LauncherEmissionTexturePath;
        internal const string WeaponMicroDetailNormalTexturePath = MovementLabContract.WeaponMicroDetailNormalTexturePath;
        internal const string ShotgunBaseColorTexturePath = MovementLabContract.ShotgunBaseColorTexturePath;
        internal const string ShotgunNormalTexturePath = MovementLabContract.ShotgunNormalTexturePath;
        internal const string ShotgunMetallicTexturePath = MovementLabContract.ShotgunMetallicTexturePath;
        internal const string ShotgunOcclusionTexturePath = MovementLabContract.ShotgunOcclusionTexturePath;
        internal const string ShotgunEmissionTexturePath = MovementLabContract.ShotgunEmissionTexturePath;
        internal const string ScenePath = MovementLabContract.ScenePath;
        internal const string InputActionsPath = MovementLabContract.InputActionsPath;
        internal const string MaterialsPath = MovementLabContract.MaterialsPath;
        internal const string TexturesPath = MovementLabContract.TexturesPath;
        internal const string ShadersPath = MovementLabContract.ShadersPath;
        internal const string AnimationsPath = MovementLabContract.AnimationsPath;
        internal const string ExplosionPrefabPath = MovementLabContract.ExplosionPrefabPath;
        internal const string HealthPickupPrefabPath = MovementLabContract.HealthPickupPrefabPath;
        internal const string HealthPickupMaterialPath = MovementLabContract.HealthPickupMaterialPath;
        internal const string ShotgunPickupPrefabPath = MovementLabContract.ShotgunPickupPrefabPath;
        internal const string AmmoPickupPrefabPath = MovementLabContract.AmmoPickupPrefabPath;
        internal const string AmmoShellMaterialPath = MovementLabContract.AmmoShellMaterialPath;
        internal const string ShotgunPelletMaterialPath = MovementLabContract.ShotgunPelletMaterialPath;
        internal const string WeaponImpactMarkMaterialPath = MovementLabContract.WeaponImpactMarkMaterialPath;
        internal const string HealthPickupsRootName = MovementLabContract.HealthPickupsRootName;
        internal const string HealthPickupWestNorthName = MovementLabContract.HealthPickupWestNorthName;
        internal const string HealthPickupEastSouthName = MovementLabContract.HealthPickupEastSouthName;
        internal const string ShotgunPickupsRootName = MovementLabContract.ShotgunPickupsRootName;
        internal const string AmmoPickupsRootName = MovementLabContract.AmmoPickupsRootName;
        internal const string ShotgunPickupNorthName = MovementLabContract.ShotgunPickupNorthName;
        internal const string ShotgunPickupSouthName = MovementLabContract.ShotgunPickupSouthName;
        internal const string ShotgunPickupName = MovementLabContract.ShotgunPickupName;
        internal const string AmmoPickupWestNorthName = MovementLabContract.AmmoPickupWestNorthName;
        internal const string AmmoPickupEastSouthName = MovementLabContract.AmmoPickupEastSouthName;
        internal const string WorldControllerPath = AnimationsPath + "/WorldCharacter.controller";
        internal const string FpsControllerPath = AnimationsPath + "/FpsKick.controller";
        internal const string TeamBlueMaterialPath = MaterialsPath + "/TeamBlue.mat";
        internal const string TeamRedMaterialPath = MaterialsPath + "/TeamRed.mat";
        internal const string TeamBlueShieldMaterialPath = MaterialsPath + "/TeamBlueShield.mat";
        internal const string TeamRedShieldMaterialPath = MaterialsPath + "/TeamRedShield.mat";
        internal const string TeamBlueTrailMaterialPath = MaterialsPath + "/TeamBlueTrail.mat";
        internal const string TeamRedTrailMaterialPath = MaterialsPath + "/TeamRedTrail.mat";
        internal const string ShotgunMetalMaterialPath = MovementLabContract.ShotgunMetalMaterialPath;
        internal const string ShotgunDarkMaterialPath = MovementLabContract.ShotgunDarkMaterialPath;
        internal const string WeaponAccentMaterialPath = MovementLabContract.WeaponAccentMaterialPath;
        internal const string ShotgunAccentMaterialPath = MovementLabContract.ShotgunAccentMaterialPath;
        internal const string WeaponAccentCoreMaterialPath = MovementLabContract.WeaponAccentCoreMaterialPath;
        internal const string ShotgunAccentCoreMaterialPath = MovementLabContract.ShotgunAccentCoreMaterialPath;
        internal const string BlueCircleCueMeshPath = MovementLabContract.BlueCircleCueMeshPath;
        internal const string RedTriangleCueMeshPath = MovementLabContract.RedTriangleCueMeshPath;

        internal readonly struct ParticipantSlotDefinition
        {
            internal readonly int SlotId;
            internal readonly string DisplayName;
            internal readonly ParticipantTeam Team;
            internal readonly bool IsLocal;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;

            internal ParticipantSlotDefinition(int slotId, string displayName, ParticipantTeam team, bool isLocal,
                Vector3 position, Quaternion rotation)
            {
                SlotId = slotId;
                DisplayName = displayName;
                Team = team;
                IsLocal = isLocal;
                Position = position;
                Rotation = rotation;
            }
        }

        internal readonly struct HealthPickupSpawnDefinition
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;

            internal HealthPickupSpawnDefinition(string name, Vector3 position, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }
        }

        internal readonly struct ShotgunPickupSpawnDefinition
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;

            internal ShotgunPickupSpawnDefinition(string name, Vector3 position, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }
        }

        internal readonly struct AmmoPickupSpawnDefinition
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;

            internal AmmoPickupSpawnDefinition(string name, Vector3 position, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }
        }

        internal static readonly HealthPickupSpawnDefinition[] HealthPickupSpawns =
        {
            new HealthPickupSpawnDefinition(HealthPickupWestNorthName, MovementLabContract.HealthPickupWestNorthPosition, MovementLabContract.HealthPickupWestNorthRotation),
            new HealthPickupSpawnDefinition(HealthPickupEastSouthName, MovementLabContract.HealthPickupEastSouthPosition, MovementLabContract.HealthPickupEastSouthRotation)
        };

        internal static readonly ShotgunPickupSpawnDefinition[] ShotgunPickupSpawns =
        {
            new ShotgunPickupSpawnDefinition(ShotgunPickupNorthName, MovementLabContract.ShotgunPickupNorthPosition, MovementLabContract.ShotgunPickupNorthRotation),
            new ShotgunPickupSpawnDefinition(ShotgunPickupSouthName, MovementLabContract.ShotgunPickupSouthPosition, MovementLabContract.ShotgunPickupSouthRotation)
        };

        internal static readonly AmmoPickupSpawnDefinition[] AmmoPickupSpawns =
        {
            new AmmoPickupSpawnDefinition(AmmoPickupWestNorthName, MovementLabContract.AmmoPickupWestNorthPosition, MovementLabContract.AmmoPickupWestNorthRotation),
            new AmmoPickupSpawnDefinition(AmmoPickupEastSouthName, MovementLabContract.AmmoPickupEastSouthPosition, MovementLabContract.AmmoPickupEastSouthRotation)
        };

        // Stable six-slot composition. Blue owns positive-X/South goal; Red owns negative-X/North goal.
        internal static readonly ParticipantSlotDefinition[] ParticipantSlots =
        {
            Slot(0, "Player", ParticipantTeam.Blue, true, new Vector3(MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, 0f), Vector3.left),
            Slot(1, "Bolt", ParticipantTeam.Blue, false, new Vector3(MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, -10f), Vector3.left),
            Slot(2, "Echo", ParticipantTeam.Blue, false, new Vector3(MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, 10f), Vector3.left),
            Slot(3, "Rook", ParticipantTeam.Red, false, new Vector3(-MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, 0f), Vector3.right),
            Slot(4, "Nova", ParticipantTeam.Red, false, new Vector3(-MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, 10f), Vector3.right),
            Slot(5, "Vex", ParticipantTeam.Red, false, new Vector3(-MovementLabContract.PlayerSpawnOffset, MovementLabContract.PlayerControllerSkinWidth, -10f), Vector3.right)
        };

        private static ParticipantSlotDefinition Slot(int id, string name, ParticipantTeam team, bool local, Vector3 position, Vector3 forward)
        {
            return new ParticipantSlotDefinition(id, name, team, local, position, Quaternion.LookRotation(forward, Vector3.up));
        }

        internal const string GrassTexturePath = TexturesPath + "/RetroGrass.png";
        internal const string GrassNormalTexturePath = TexturesPath + "/RetroGrass_Normal.png";
        internal const string GrassMetallicTexturePath = TexturesPath + "/RetroGrass_MetallicSmoothness.png";
        internal const string GrassOcclusionTexturePath = TexturesPath + "/RetroGrass_Occlusion.png";
        internal const string WallNormalTexturePath = TexturesPath + "/RetroWall_Normal.png";
        internal const string WallMetallicTexturePath = TexturesPath + "/RetroWall_MetallicSmoothness.png";
        internal const string WallOcclusionTexturePath = TexturesPath + "/RetroWall_Occlusion.png";
        internal const string TrimNormalTexturePath = TexturesPath + "/RetroTrim_Normal.png";
        internal const string TrimMetallicTexturePath = TexturesPath + "/RetroTrim_MetallicSmoothness.png";
        internal const string TrimOcclusionTexturePath = TexturesPath + "/RetroTrim_Occlusion.png";
        internal const string HazardNormalTexturePath = TexturesPath + "/RetroHazard_Normal.png";
        internal const string HazardMetallicTexturePath = TexturesPath + "/RetroHazard_MetallicSmoothness.png";
        internal const string HazardOcclusionTexturePath = TexturesPath + "/RetroHazard_Occlusion.png";
        internal const string DetailNormalTexturePath = TexturesPath + "/RetroDetailNormal.png";
        internal const string BallTexturePath = TexturesPath + "/RetroBall.png";
        internal const string BallNormalTexturePath = TexturesPath + "/RetroBall_Normal.png";
        internal const string BallMetallicTexturePath = TexturesPath + "/RetroBall_MetallicSmoothness.png";
        internal const string BallOcclusionTexturePath = TexturesPath + "/RetroBall_Occlusion.png";
        internal const string WeaponMetalTexturePath = TexturesPath + "/RetroWeaponMetal.png";
        internal const string WeaponMetalNormalTexturePath = TexturesPath + "/RetroWeaponMetal_Normal.png";
        internal const string WeaponMetalMetallicTexturePath = TexturesPath + "/RetroWeaponMetal_MetallicSmoothness.png";
        internal const string WeaponMetalOcclusionTexturePath = TexturesPath + "/RetroWeaponMetal_Occlusion.png";
        internal const string WeaponDarkTexturePath = TexturesPath + "/RetroWeaponDark.png";
        internal const string WeaponDarkNormalTexturePath = TexturesPath + "/RetroWeaponDark_Normal.png";
        internal const string WeaponDarkMetallicTexturePath = TexturesPath + "/RetroWeaponDark_MetallicSmoothness.png";
        internal const string WeaponDarkOcclusionTexturePath = TexturesPath + "/RetroWeaponDark_Occlusion.png";
        internal const string WeaponAccentTexturePath = TexturesPath + "/RetroWeaponAccent.png";
        internal const string WeaponAccentNormalTexturePath = TexturesPath + "/RetroWeaponAccent_Normal.png";
        internal const string WeaponAccentMetallicTexturePath = TexturesPath + "/RetroWeaponAccent_MetallicSmoothness.png";
        internal const string WeaponAccentOcclusionTexturePath = TexturesPath + "/RetroWeaponAccent_Occlusion.png";
        internal const string WeaponAccentEmissionTexturePath = TexturesPath + "/RetroWeaponAccent_Emission.png";
        internal const string RocketTexturePath = TexturesPath + "/RetroRocket.png";
        internal const string RocketNormalTexturePath = TexturesPath + "/RetroRocket_Normal.png";
        internal const string RocketMetallicTexturePath = TexturesPath + "/RetroRocket_MetallicSmoothness.png";
        internal const string RocketOcclusionTexturePath = TexturesPath + "/RetroRocket_Occlusion.png";
        internal const string RocketEmissionTexturePath = TexturesPath + "/RetroRocket_Emission.png";
        internal const string RocketGlowTexturePath = TexturesPath + "/RetroRocketGlow.png";
        internal const string ExplosionTexturePath = TexturesPath + "/RetroExplosion.png";
        internal const string SmokeTexturePath = TexturesPath + "/RetroSmoke.png";
        internal const string WallTexturePath = TexturesPath + "/RetroWall.png";
        internal const string TrimTexturePath = TexturesPath + "/RetroTrim.png";
        internal const string HazardTexturePath = TexturesPath + "/RetroHazard.png";
        internal const string ShieldTexturePath = TexturesPath + "/RetroShield.png";

        internal const string ToonShaderPath = ShadersPath + "/RetroToonLit.shader";
        internal const string LitShaderName = "Universal Render Pipeline/Lit";
        internal const string ParticleShaderPath = ShadersPath + "/RetroParticle.shader";
        internal const string AdditiveParticleShaderPath = ShadersPath + "/RetroAdditiveParticle.shader";
        internal const string PowerGridShaderPath = ShadersPath + "/RetroPowerGrid.shader";
        internal const string ShieldShaderPath = ShadersPath + "/RetroShield.shader";
        internal const string SkyTexturePath = TexturesPath + "/RetroSunnySky.png";
        internal const string SkyMaterialPath = MaterialsPath + "/RetroSunnySky.mat";
        internal const string BallSurfacePath = MaterialsPath + "/BallSurface.physicMaterial";
        internal const string RocketHotMaterialPath = MaterialsPath + "/RocketHot.mat";
        internal const string ProjectileGlowMaterialPath = MaterialsPath + "/ProjectileGlow.mat";
        internal const string ExplosionAdditiveMaterialPath = MaterialsPath + "/ExplosionAdditive.mat";
        internal const string ExplosionSparksMaterialPath = MaterialsPath + "/ExplosionSparks.mat";
        internal const string GridCeilingMaterialPath = MaterialsPath + "/ContainmentGridCeiling.mat";
        internal const string GridLongWallMaterialPath = MaterialsPath + "/ContainmentGridLongWall.mat";
        internal const string GridEndWallMaterialPath = MaterialsPath + "/ContainmentGridEndWall.mat";
        internal const string ManifestPath = MovementLabContract.ManifestPath;
        internal const string SkyShaderPath = ShadersPath + "/SunnyArenaSky.shader";
        internal const string LightingPath = MovementLabContract.LightingPath;
        internal const string VolumeProfilePath = MovementLabContract.VolumeProfilePath;
        internal const string LightingSettingsPath = MovementLabContract.LightingSettingsPath;
        internal const string LightingManifestPath = MovementLabContract.LightingManifestPath;
        internal const string BakedLightingPath = MovementLabContract.BakedLightingPath;
        internal const int ExpectedLightmapCount = MovementLabContract.ExpectedLightmapCount;
        internal const int ExpectedReflectionProbeBakeCount = MovementLabContract.ExpectedReflectionProbeBakeCount;
        internal const string DetailNormalKeyword = "_DETAIL_MULX2";
        internal const string BuildMarkerPrefix = MovementLabContract.BuildMarkerPrefix;

        internal const int MaterialPrefabStageContractVersion = MovementLabContract.MaterialPrefabStageContractVersion;
        internal const int GameplaySceneStageContractVersion = MovementLabContract.GameplaySceneStageContractVersion;
        internal const int QualityStageContractVersion = MovementLabContract.QualityStageContractVersion;
        internal const int LightingStageContractVersion = MovementLabContract.LightingStageContractVersion;
        internal const int BakedOutputStageContractVersion = MovementLabContract.BakedOutputStageContractVersion;
        internal const int LocalPlayerHiddenLayer = MovementLabContract.LocalPlayerHiddenLayer;
        internal const int ProjectilesLayer = MovementLabContract.ProjectilesLayer;
        internal const int ParticipantsLayer = MovementLabContract.ParticipantsLayer;
        internal const int ViewmodelsLayer = MovementLabContract.ViewmodelsLayer;
        internal const string LocalPlayerHiddenLayerName = MovementLabContract.LocalPlayerHiddenLayerName;
        internal const string ProjectilesLayerName = MovementLabContract.ProjectilesLayerName;
        internal const string ParticipantsLayerName = MovementLabContract.ParticipantsLayerName;
        internal const string ViewmodelsLayerName = MovementLabContract.ViewmodelsLayerName;
        internal const string ViewmodelsRootName = MovementLabContract.ViewmodelsRootName;
        internal const string ViewmodelLightName = MovementLabContract.ViewmodelLightName;
        internal static readonly Vector3 ViewmodelLightLocalEuler = MovementLabContract.ViewmodelLightLocalEuler;
        internal const float ViewmodelLightIntensity = MovementLabContract.ViewmodelLightIntensity;
        internal const int ViewmodelLightCullingMask = MovementLabContract.ViewmodelLightCullingMask;
        internal const LightType ViewmodelLightType = MovementLabContract.ViewmodelLightType;
        internal const LightmapBakeType ViewmodelLightBakeType = MovementLabContract.ViewmodelLightBakeType;
        internal const LightShadows ViewmodelLightShadows = MovementLabContract.ViewmodelLightShadows;

        internal const float BallPrefabScale = MovementLabContract.BallPrefabScale;
        internal const float BallRadius = MovementLabContract.BallRadius;
        internal const float BallSpawnHeight = MovementLabContract.BallSpawnHeight;
        internal const float BlastRadius = MovementLabContract.BlastRadius;
        internal const float GoalAxisPosition = MovementLabContract.GoalAxisPosition;
        internal const float PlayerSpawnOffset = MovementLabContract.PlayerSpawnOffset;
        internal const float CelebrationOrbitRadius = MovementLabContract.CelebrationOrbitRadius;
        internal const float CelebrationOrbitHeight = MovementLabContract.CelebrationOrbitHeight;
        internal const float CelebrationLookHeight = MovementLabContract.CelebrationLookHeight;
        internal const float CelebrationOrbitDegrees = MovementLabContract.CelebrationOrbitDegrees;
        internal const float CelebrationFov = MovementLabContract.CelebrationFov;
        internal const float PlayerControllerRadius = MovementLabContract.PlayerControllerRadius;
        internal const float PlayerControllerHeight = MovementLabContract.PlayerControllerHeight;
        internal static readonly Vector3 PlayerControllerCenter = MovementLabContract.PlayerControllerCenter;
        internal const float PlayerControllerSkinWidth = MovementLabContract.PlayerControllerSkinWidth;
        internal const float PlayableFloorTop = MovementLabContract.PlayableFloorTop;
        internal const float ParticipantRecoveryThreshold = MovementLabContract.ParticipantRecoveryThreshold;
        internal const float WorldVisualScale = MovementLabContract.WorldVisualScale;
        internal const float PlayerHeadHeight = MovementLabContract.PlayerHeadHeight;
        internal const float TeamCueScaleMultiplier = MovementLabContract.TeamCueScaleMultiplier;
        internal const float ImmunityShieldScaleMultiplier = MovementLabContract.ImmunityShieldScaleMultiplier;
        internal const float NameplateHeight = MovementLabContract.NameplateHeight;
        internal const float LocalRespawnDelay = MovementLabContract.LocalRespawnDelay;
        internal const float BotRespawnDelay = MovementLabContract.BotRespawnDelay;
        internal const float RocketTrailLifetime = MovementLabContract.RocketTrailLifetime;
        internal const float RocketTrailRateOverDistance = MovementLabContract.RocketTrailRateOverDistance;
        internal const float RocketTrailStartSize = MovementLabContract.RocketTrailStartSize;
        internal static readonly Color RocketTrailStartColor = MovementLabContract.RocketTrailStartColor;
        internal static readonly Color RocketTrailEndColor = MovementLabContract.RocketTrailEndColor;
        internal static readonly Color RocketEmissionColor = MovementLabContract.RocketEmissionColor;
        internal const float RocketEmissionStrength = MovementLabContract.RocketEmissionStrength;
        internal static readonly Color RocketBaseColor = MovementLabContract.RocketBaseColor;
        internal const float WeaponImpactFeedbackEpsilon = MovementLabContract.WeaponImpactFeedbackEpsilon;
        internal const float WeaponImpactTracerOriginOffset = MovementLabContract.WeaponImpactTracerOriginOffset;
        internal const float WeaponImpactTracerSpeed = MovementLabContract.WeaponImpactTracerSpeed;
        internal const float WeaponImpactTracerSystemDuration = MovementLabContract.WeaponImpactTracerSystemDuration;
        internal const float WeaponImpactMinimumTracerLifetime = MovementLabContract.WeaponImpactMinimumTracerLifetime;
        internal const float WeaponImpactMarkSurfaceOffset = MovementLabContract.WeaponImpactMarkSurfaceOffset;
        internal const float WeaponImpactMarkLifetime = MovementLabContract.WeaponImpactMarkLifetime;
        internal const float ShotgunImpactMarkSize = MovementLabContract.ShotgunImpactMarkSize;
        internal const float RocketImpactMarkSize = MovementLabContract.RocketImpactMarkSize;
        internal const int ShotgunPelletMaxParticles = MovementLabContract.ShotgunPelletMaxParticles;
        internal const int WeaponImpactMarkMaxParticles = MovementLabContract.WeaponImpactMarkMaxParticles;
        internal const float PlayerCollisionRetentionFraction = MovementLabContract.PlayerCollisionRetentionFraction;
        internal const float PlayerCollisionTransferFraction = MovementLabContract.PlayerCollisionTransferFraction;
        internal const float BallContactAssistPerContactCap = MovementLabContract.BallContactAssistPerContactCap;
        internal const float BallContactAssistAggregateCap = MovementLabContract.BallContactAssistAggregateCap;
        internal static readonly Color WeaponMetalBaseColor = MovementLabContract.WeaponMetalBaseColor;
        internal static readonly Color WeaponDarkBaseColor = MovementLabContract.WeaponDarkBaseColor;
        internal static readonly Color WeaponAccentBaseColor = MovementLabContract.WeaponAccentBaseColor;
        internal static readonly Color ShotgunMetalBaseColor = MovementLabContract.ShotgunMetalBaseColor;
        internal static readonly Color ShotgunDarkBaseColor = MovementLabContract.ShotgunDarkBaseColor;
        internal static readonly Color ShotgunAccentBaseColor = MovementLabContract.ShotgunAccentBaseColor;
        internal static readonly Color ShotgunAccentCoreBaseColor = MovementLabContract.ShotgunAccentCoreBaseColor;
        internal static readonly Color ShotgunAccentCoreEmissionColor = MovementLabContract.ShotgunAccentCoreEmissionColor;
        internal const float ShotgunAccentCoreEmissionStrength = MovementLabContract.ShotgunAccentCoreEmissionStrength;
        internal static readonly Color AmmoShellBaseColor = MovementLabContract.AmmoShellBaseColor;
        internal static readonly Color AmmoShellEmissionColor = MovementLabContract.AmmoShellEmissionColor;
        internal const float AmmoShellEmissionStrength = MovementLabContract.AmmoShellEmissionStrength;
        internal static readonly Color ExplosionFireMaterialColor = MovementLabContract.ExplosionFireMaterialColor;
        internal static readonly Color ExplosionSmokeMaterialColor = MovementLabContract.ExplosionSmokeMaterialColor;
        internal static readonly Color GridColor = MovementLabContract.GridColor;
        internal const float JumpVelocity = MovementLabContract.JumpVelocity;
        internal const float UnderfootForwardImpulseScale = MovementLabContract.UnderfootForwardImpulseScale;
        internal const float UnderfootUpwardImpulseScale = MovementLabContract.UnderfootUpwardImpulseScale;
        internal const float UnderfootHighSpeedVerticalRedirect = MovementLabContract.UnderfootHighSpeedVerticalRedirect;
        internal static readonly Vector2 FloorTextureScale = MovementLabContract.FloorTextureScale;
        internal static readonly Vector2 WallTextureScale = MovementLabContract.WallTextureScale;
        internal const bool BotsEnabledByDefault = MovementLabContract.BotsEnabledByDefault;
        internal static readonly Color WeaponAccentShellBaseColor = MovementLabContract.WeaponAccentShellBaseColor;
        internal const float WeaponAccentShellMetallic = MovementLabContract.WeaponAccentShellMetallic;
        internal const float WeaponAccentShellSmoothness = MovementLabContract.WeaponAccentShellSmoothness;
        internal const float WeaponAccentShellOcclusion = MovementLabContract.WeaponAccentShellOcclusion;
        internal const float WeaponAccentShellBumpScale = MovementLabContract.WeaponAccentShellBumpScale;
        internal static readonly Color WeaponAccentCoreBaseColor = MovementLabContract.WeaponAccentCoreBaseColor;
        internal const float WeaponAccentCoreMetallic = MovementLabContract.WeaponAccentCoreMetallic;
        internal const float WeaponAccentCoreSmoothness = MovementLabContract.WeaponAccentCoreSmoothness;
        internal const float WeaponAccentCoreOcclusion = MovementLabContract.WeaponAccentCoreOcclusion;
        internal const float WeaponAccentCoreBumpScale = MovementLabContract.WeaponAccentCoreBumpScale;
        internal static readonly Color WeaponAccentCoreEmissionColor = MovementLabContract.WeaponAccentCoreEmissionColor;
        internal const float WeaponAccentCoreEmissionStrength = MovementLabContract.WeaponAccentCoreEmissionStrength;
        internal static readonly Color LauncherMetalBaseColor = MovementLabContract.LauncherMetalBaseColor;
        internal static readonly Color LauncherDarkBaseColor = MovementLabContract.LauncherDarkBaseColor;
        internal static readonly Color LauncherAccentBaseColor = MovementLabContract.LauncherAccentBaseColor;
        internal static readonly Color LauncherAccentCoreBaseColor = MovementLabContract.LauncherAccentCoreBaseColor;
        internal const float LauncherMetallic = MovementLabContract.LauncherMetallic;
        internal const float LauncherSmoothness = MovementLabContract.LauncherSmoothness;
        internal const float LauncherOcclusion = MovementLabContract.LauncherOcclusion;
        internal const float LauncherBumpScale = MovementLabContract.LauncherBumpScale;
        internal const int LauncherAtlasSize = MovementLabContract.LauncherAtlasSize;
        internal static readonly RectInt LauncherMetalUvZone = MovementLabContract.LauncherMetalUvZone;
        internal static readonly RectInt LauncherDarkUvZone = MovementLabContract.LauncherDarkUvZone;
        internal static readonly RectInt LauncherAccentUvZone = MovementLabContract.LauncherAccentUvZone;
        internal static readonly RectInt LauncherAccentCoreUvZone = MovementLabContract.LauncherAccentCoreUvZone;
        internal const int ShotgunAtlasSize = MovementLabContract.ShotgunAtlasSize;
        internal static readonly RectInt FpsShotgunMetalUvZone = MovementLabContract.FpsShotgunMetalUvZone;
        internal static readonly RectInt FpsShotgunDarkUvZone = MovementLabContract.FpsShotgunDarkUvZone;
        internal static readonly RectInt FpsShotgunAccentUvZone = MovementLabContract.FpsShotgunAccentUvZone;
        internal static readonly RectInt FpsShotgunAccentCoreUvZone = MovementLabContract.FpsShotgunAccentCoreUvZone;
        internal static readonly RectInt WorldShotgunMetalUvZone = MovementLabContract.WorldShotgunMetalUvZone;
        internal static readonly RectInt WorldShotgunDarkUvZone = MovementLabContract.WorldShotgunDarkUvZone;
        internal static readonly RectInt WorldShotgunAccentUvZone = MovementLabContract.WorldShotgunAccentUvZone;
        internal static readonly RectInt WorldShotgunAccentCoreUvZone = MovementLabContract.WorldShotgunAccentCoreUvZone;
        internal const float WeaponBoundsTolerance = MovementLabContract.WeaponBoundsTolerance;
        internal const float WeaponShellCoreInset = MovementLabContract.WeaponShellCoreInset;
        internal const float WeaponMeshIslandPositionTolerance = MovementLabContract.WeaponMeshIslandPositionTolerance;
        internal static readonly Vector3 LauncherWeaponBoundsMin = MovementLabContract.LauncherWeaponBoundsMin;
        internal static readonly Vector3 LauncherWeaponBoundsMax = MovementLabContract.LauncherWeaponBoundsMax;
        internal static readonly Vector3 FpsShotgunBoundsMin = MovementLabContract.FpsShotgunBoundsMin;
        internal static readonly Vector3 FpsShotgunBoundsMax = MovementLabContract.FpsShotgunBoundsMax;
        internal static readonly Vector3 WorldShotgunBoundsMin = MovementLabContract.WorldShotgunBoundsMin;
        internal static readonly Vector3 WorldShotgunBoundsMax = MovementLabContract.WorldShotgunBoundsMax;
        internal const int WeaponMicroDetailNormalMaxTextureSize = MovementLabContract.WeaponMicroDetailNormalMaxTextureSize;
        internal static readonly Vector2 WeaponMicroDetailNormalTiling = MovementLabContract.WeaponMicroDetailNormalTiling;
        internal const float WeaponMicroDetailNormalScale = MovementLabContract.WeaponMicroDetailNormalScale;
        internal static readonly Vector3 LauncherViewmodelPosition = MovementLabContract.LauncherViewmodelPosition;
        internal static readonly Vector3 ShotgunViewmodelPosition = MovementLabContract.ShotgunViewmodelPosition;
        internal static readonly string[] GeneratedYamlAssetPaths =
        {
            PrefabPath, BallPrefabPath, RocketPrefabPath, ExplosionPrefabPath, HealthPickupPrefabPath, ShotgunPickupPrefabPath, AmmoPickupPrefabPath, ScenePath,
            HealthPickupMaterialPath, AmmoShellMaterialPath, ShotgunPelletMaterialPath, WeaponImpactMarkMaterialPath,
            MaterialsPath + "/Floor.mat", MaterialsPath + "/Wall.mat", MaterialsPath + "/Trim.mat", MaterialsPath + "/Hazard.mat",
            MaterialsPath + "/Marking.mat", MaterialsPath + "/Ball.mat", MaterialsPath + "/Rocket.mat", RocketHotMaterialPath,
            ProjectileGlowMaterialPath, MaterialsPath + "/GoalFrame.mat", MaterialsPath + "/Shield.mat", MaterialsPath + "/ShieldBlue.mat",
            MaterialsPath + "/ShieldRed.mat", MaterialsPath + "/ArenaPrimary.mat", MaterialsPath + "/ArenaTrim.mat", MaterialsPath + "/ArenaHazard.mat",
            MaterialsPath + "/ArenaGlow.mat", BallSurfacePath, MaterialsPath + "/Explosion.mat", ExplosionAdditiveMaterialPath,
            ExplosionSparksMaterialPath, MaterialsPath + "/Smoke.mat", GridCeilingMaterialPath, GridLongWallMaterialPath, GridEndWallMaterialPath,
            SkyMaterialPath, VolumeProfilePath, LightingSettingsPath, MovementLabLightingProfiles.DevelopmentSettingsPath,
            MaterialsPath + "/CharacterRed.mat", MaterialsPath + "/CharacterBlack.mat",
            MaterialsPath + "/CharacterCream.mat", MaterialsPath + "/CharacterEye.mat", MaterialsPath + "/WeaponMetal.mat", MaterialsPath + "/WeaponDark.mat",
            WeaponAccentCoreMaterialPath, WeaponAccentMaterialPath,
            ShotgunMetalMaterialPath, ShotgunDarkMaterialPath, ShotgunAccentCoreMaterialPath, ShotgunAccentMaterialPath,
            TeamBlueMaterialPath, TeamRedMaterialPath,
            TeamBlueShieldMaterialPath, TeamRedShieldMaterialPath, TeamBlueTrailMaterialPath, TeamRedTrailMaterialPath,
            BlueCircleCueMeshPath, RedTriangleCueMeshPath,
            WorldControllerPath, FpsControllerPath
        };

        internal static readonly string[] GeneratedBakedLightingPaths = CreateGeneratedBakedLightingPaths();

        internal static readonly string[] GeneratedImporterMetadataPaths =
        {
            RocketModelPath + ".meta", ArenaKitModelPath + ".meta", CharacterModelPath + ".meta", FpsKickModelPath + ".meta", WeaponModelPath + ".meta",
            FpsShotgunModelPath + ".meta", ShotgunModelPath + ".meta",
            GrassTexturePath + ".meta", GrassNormalTexturePath + ".meta", GrassMetallicTexturePath + ".meta", GrassOcclusionTexturePath + ".meta",
            WallTexturePath + ".meta", WallNormalTexturePath + ".meta", WallMetallicTexturePath + ".meta", WallOcclusionTexturePath + ".meta",
            TrimTexturePath + ".meta", TrimNormalTexturePath + ".meta", TrimMetallicTexturePath + ".meta", TrimOcclusionTexturePath + ".meta",
            HazardTexturePath + ".meta", HazardNormalTexturePath + ".meta", HazardMetallicTexturePath + ".meta", HazardOcclusionTexturePath + ".meta",
            DetailNormalTexturePath + ".meta", ShieldTexturePath + ".meta",
            BallTexturePath + ".meta", BallNormalTexturePath + ".meta", BallMetallicTexturePath + ".meta", BallOcclusionTexturePath + ".meta",
            WeaponMetalTexturePath + ".meta", WeaponMetalNormalTexturePath + ".meta", WeaponMetalMetallicTexturePath + ".meta", WeaponMetalOcclusionTexturePath + ".meta",
            WeaponDarkTexturePath + ".meta", WeaponDarkNormalTexturePath + ".meta", WeaponDarkMetallicTexturePath + ".meta", WeaponDarkOcclusionTexturePath + ".meta",
            WeaponAccentTexturePath + ".meta", WeaponAccentNormalTexturePath + ".meta", WeaponAccentMetallicTexturePath + ".meta", WeaponAccentOcclusionTexturePath + ".meta",
            WeaponAccentEmissionTexturePath + ".meta",
            LauncherBaseColorTexturePath + ".meta", LauncherNormalTexturePath + ".meta", LauncherMetallicTexturePath + ".meta",
            LauncherOcclusionTexturePath + ".meta", LauncherEmissionTexturePath + ".meta", WeaponMicroDetailNormalTexturePath + ".meta",
            ShotgunBaseColorTexturePath + ".meta", ShotgunNormalTexturePath + ".meta", ShotgunMetallicTexturePath + ".meta",
            ShotgunOcclusionTexturePath + ".meta", ShotgunEmissionTexturePath + ".meta",
            RocketTexturePath + ".meta", RocketNormalTexturePath + ".meta", RocketMetallicTexturePath + ".meta", RocketOcclusionTexturePath + ".meta", RocketEmissionTexturePath + ".meta", RocketGlowTexturePath + ".meta",
            ExplosionTexturePath + ".meta", SmokeTexturePath + ".meta", SkyTexturePath + ".meta"
        };

        // Unread by design: constructing this list IS the .meta-coverage check (see ValidateGeneratedFingerprintPathList).
        internal static readonly string[] GeneratedFingerprintPaths = CreateGeneratedFingerprintPaths();

        internal static readonly Color SkyHorizonColor = new Color(0.7254902f, 0.8627451f, 0.9490196f, 1f);
        internal static readonly Color SkyZenithColor = new Color(0.2980392f, 0.5686275f, 0.8470588f, 1f);
        internal static readonly Color SkyCloudColor = new Color(0.9607843f, 0.9529412f, 0.9098039f, 1f);
        internal static readonly Color SunColor = new Color(1.0f, 0.8392157f, 0.6392157f, 1f);
        internal static readonly (string name, Vector3 position, Color color)[] AccentLightContract =
        {
            // Red owns negative-X/North; Blue owns positive-X/South.
            ("GoalAccent_WestRed_North", new Vector3(-58f, 5f, -22f), new Color(1.00f, 0.20f, 0.14f, 1f)),
            ("GoalAccent_WestRed_South", new Vector3(-58f, 5f, 22f), new Color(1.00f, 0.20f, 0.14f, 1f)),
            ("GoalAccent_EastBlue_North", new Vector3(58f, 5f, -22f), new Color(0.20f, 0.46f, 1.00f, 1f)),
            ("GoalAccent_EastBlue_South", new Vector3(58f, 5f, 22f), new Color(0.20f, 0.46f, 1.00f, 1f))
        };
        internal static readonly (string name, Vector3 center, Vector3 size)[] ReflectionProbeContract =
        {
            ("ReflectionProbe_Center", new Vector3(0f, 12f, 0f), new Vector3(100f, 28f, 70f)),
            ("ReflectionProbe_WestGoal", new Vector3(-58f, 6f, 0f), new Vector3(20f, 12f, 38f)),
            ("ReflectionProbe_EastGoal", new Vector3(58f, 6f, 0f), new Vector3(20f, 12f, 38f))
        };

        internal static DirectoryInfo ResolveProjectRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null) throw new InvalidOperationException("Unable to resolve Unity project root.");
            return projectRoot;
        }

        internal static string GetAbsoluteProjectPath(DirectoryInfo projectRoot, string repositoryRelativePath)
        {
            var normalizedPath = NormalizeRepositoryRelativePath(repositoryRelativePath);
            return Path.Combine(projectRoot.FullName, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string NormalizeRepositoryRelativePath(string repositoryRelativePath)
        {
            if (string.IsNullOrEmpty(repositoryRelativePath) || Path.IsPathRooted(repositoryRelativePath) || repositoryRelativePath.IndexOf(':') >= 0)
            {
                throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
            }
            var normalizedPath = repositoryRelativePath.Replace('\\', '/');
            var segments = normalizedPath.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..") throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
            }
            if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) && !normalizedPath.StartsWith("ProjectSettings/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
            }
            return normalizedPath;
        }

        internal static string ComputeBuilderSignature() => "serialized-contract-v" + MovementLabContract.SerializedContractVersion;
        internal static string GetBuildMarkerName(string builderSignature) => BuildMarkerPrefix + builderSignature;

        private static string[] CreateGeneratedBakedLightingPaths()
        {
            var lightmapPaths = MovementLabContract.BakedLightmapPaths(ExpectedLightmapCount);
            var paths = new string[1 + lightmapPaths.Length + ExpectedReflectionProbeBakeCount];
            paths[0] = BakedLightingPath + "/LightingData.asset";
            Array.Copy(lightmapPaths, 0, paths, 1, lightmapPaths.Length);
            var reflectionOffset = 1 + lightmapPaths.Length;
            for (var i = 0; i < ExpectedReflectionProbeBakeCount; i++)
                paths[reflectionOffset + i] = BakedLightingPath + "/ReflectionProbe-" + i + ".exr";
            return paths;
        }

        private static string[] CreateGeneratedFingerprintPaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
            {
                AddGeneratedFingerprintPath(paths, seen, GeneratedYamlAssetPaths[i]);
                AddGeneratedFingerprintPath(paths, seen, GeneratedYamlAssetPaths[i] + ".meta");
            }

            // Raw FBX/PNG sources are stage inputs, never trusted owned outputs.
            // Importer metadata is the builder-owned output and remains drift-protected.
            for (var i = 0; i < GeneratedImporterMetadataPaths.Length; i++)
            {
                AddGeneratedFingerprintPath(paths, seen, GeneratedImporterMetadataPaths[i]);
            }
            AddGeneratedFingerprintPath(paths, seen, MovementLabContract.EditorBuildSettingsPath);
            AddGeneratedFingerprintPath(paths, seen, MovementLabContract.DynamicsManagerPath);
            AddGeneratedFingerprintPath(paths, seen, MovementLabContract.TimeManagerPath);
            AddGeneratedFingerprintPath(paths, seen, MovementLabContract.TagManagerPath);
            for (var i = 0; i < MovementLabContract.QualityOutputs.Length; i++)
            {
                var qualityPath = MovementLabContract.QualityOutputs[i];
                AddGeneratedFingerprintPath(paths, seen, qualityPath);
                if (qualityPath.StartsWith("Assets/", StringComparison.Ordinal))
                    AddGeneratedFingerprintPath(paths, seen, qualityPath + ".meta");
            }
            AddGeneratedFingerprintPath(paths, seen, LightingManifestPath);
            AddGeneratedFingerprintPath(paths, seen, LightingManifestPath + ".meta");
            for (var i = 0; i < GeneratedBakedLightingPaths.Length; i++)
            {
                AddGeneratedFingerprintPath(paths, seen, GeneratedBakedLightingPaths[i]);
                AddGeneratedFingerprintPath(paths, seen, GeneratedBakedLightingPaths[i] + ".meta");
            }
            paths.Sort(StringComparer.Ordinal);
            var result = paths.ToArray();
            ValidateGeneratedFingerprintPathList(result);
            return result;
        }

        private static void AddGeneratedFingerprintPath(List<string> paths, HashSet<string> seen, string path)
        {
            var normalizedPath = NormalizeRepositoryRelativePath(path);
            if (!seen.Add(normalizedPath)) throw new InvalidOperationException("Duplicate generated fingerprint path: " + normalizedPath);
            paths.Add(normalizedPath);
        }

        private static void ValidateGeneratedFingerprintPathList(string[] paths)
        {
            if (paths == null || paths.Length == 0) throw new InvalidOperationException("Generated fingerprint path list is empty.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < paths.Length; i++)
            {
                var normalizedPath = NormalizeRepositoryRelativePath(paths[i]);
                if (!string.Equals(paths[i], normalizedPath, StringComparison.Ordinal) || !seen.Add(paths[i]))
                {
                    throw new InvalidOperationException("Generated fingerprint path list is unsafe or duplicated: " + paths[i]);
                }
            }
            for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
            {
                var yamlPath = NormalizeRepositoryRelativePath(GeneratedYamlAssetPaths[i]);
                if (!seen.Contains(yamlPath) || !seen.Contains(yamlPath + ".meta")) throw new InvalidOperationException("Generated YAML asset fingerprint coverage is incomplete: " + yamlPath);
            }
            for (var i = 0; i < GeneratedBakedLightingPaths.Length; i++)
            {
                var bakedPath = NormalizeRepositoryRelativePath(GeneratedBakedLightingPaths[i]);
                if (!seen.Contains(bakedPath) || !seen.Contains(bakedPath + ".meta")) throw new InvalidOperationException("Generated baked lighting fingerprint coverage is incomplete: " + bakedPath);
            }
            for (var i = 0; i < MovementLabContract.QualityOutputs.Length; i++)
            {
                var qualityPath = NormalizeRepositoryRelativePath(MovementLabContract.QualityOutputs[i]);
                if (!seen.Contains(qualityPath)) throw new InvalidOperationException("Quality output fingerprint coverage is incomplete: " + qualityPath);
                if (qualityPath.StartsWith("Assets/", StringComparison.Ordinal) && !seen.Contains(qualityPath + ".meta"))
                    throw new InvalidOperationException("Quality output metadata fingerprint coverage is incomplete: " + qualityPath);
            }
        }
    }
}
