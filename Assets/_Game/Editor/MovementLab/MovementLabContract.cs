using System;
using UnityEditor.Animations;
using UnityEngine;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;

namespace RocketFooxball.Editor
{
    internal static class MovementLabContract
    {
        // Stage-local manifests carry explicit ownership, stale reasons, and a
        // top-level fingerprint/path union. Bump whenever that wire contract changes.
        internal const int ManifestSchemaVersion = 8;
        internal const int SerializedContractVersion = 11;
        internal const string ManifestPath = "Assets/_Game/Generated/MovementLabBuildManifest.json";
        internal const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        internal const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        internal const string BallPrefabPath = "Assets/_Game/Prefabs/Ball.prefab";
        internal const string RocketPrefabPath = "Assets/_Game/Prefabs/Rocket.prefab";
        internal const string ExplosionPrefabPath = "Assets/_Game/Prefabs/ExplosionVfx.prefab";
        internal const string HealthPickupPrefabPath = "Assets/_Game/Prefabs/HealthPickup.prefab";
        internal const string ShotgunPickupPrefabPath = "Assets/_Game/Prefabs/ShotgunPickup.prefab";
        internal const string AmmoPickupPrefabPath = "Assets/_Game/Prefabs/AmmoPickup.prefab";
        internal const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        internal const string MaterialsPath = "Assets/_Game/Materials";
        internal const string HealthPickupMaterialPath = MaterialsPath + "/HealthPickup.mat";
        internal const string AmmoShellMaterialPath = MaterialsPath + "/AmmoShell.mat";
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
        internal const int DevelopmentLightmapCount = 2;
        internal const int ExpectedLightmapCount = 4;
        internal const string BuildMarkerPrefix = "MovementLabGeneratedT9_";
        internal const string EditorBuildSettingsPath = "ProjectSettings/EditorBuildSettings.asset";
        internal const string DynamicsManagerPath = "ProjectSettings/DynamicsManager.asset";
        internal const string TimeManagerPath = "ProjectSettings/TimeManager.asset";
        internal const string TagManagerPath = "ProjectSettings/TagManager.asset";
        internal const string FpsShotgunModelPath = "Assets/_Game/Models/FpsShotgun.fbx";
        internal const string ShotgunModelPath = "Assets/_Game/Models/Shotgun.fbx";
        internal const string ShotgunMetalMaterialPath = MaterialsPath + "/ShotgunMetal.mat";
        internal const string ShotgunDarkMaterialPath = MaterialsPath + "/ShotgunDark.mat";
        internal const string WeaponAccentMaterialPath = MaterialsPath + "/WeaponAccent.mat";
        internal const string ShotgunAccentMaterialPath = MaterialsPath + "/ShotgunAccent.mat";
        internal const string WeaponAccentCoreMaterialPath = MaterialsPath + "/WeaponAccentCore.mat";
        internal const string ShotgunAccentCoreMaterialPath = MaterialsPath + "/ShotgunAccentCore.mat";
        internal const string LauncherBaseColorTexturePath = TexturesPath + "/FpsRocketLauncher_BaseColor.png";
        internal const string LauncherNormalTexturePath = TexturesPath + "/FpsRocketLauncher_Normal.png";
        internal const string LauncherMetallicTexturePath = TexturesPath + "/FpsRocketLauncher_MetallicSmoothness.png";
        internal const string LauncherOcclusionTexturePath = TexturesPath + "/FpsRocketLauncher_Occlusion.png";
        internal const string LauncherEmissionTexturePath = TexturesPath + "/FpsRocketLauncher_Emission.png";
        internal const string WeaponMicroDetailNormalTexturePath = TexturesPath + "/WeaponMicroDetail_Normal.png";
        internal const int LauncherAtlasSize = 2048;
        internal static readonly RectInt LauncherMetalUvZone = new RectInt(32, 864, 1984, 1152);
        internal static readonly RectInt LauncherDarkUvZone = new RectInt(32, 352, 1280, 480);
        internal static readonly RectInt LauncherAccentUvZone = new RectInt(1344, 352, 672, 480);
        internal static readonly RectInt LauncherAccentCoreUvZone = new RectInt(32, 32, 1984, 288);
        internal const string ShotgunBaseColorTexturePath = TexturesPath + "/Shotgun_BaseColor.png";
        internal const string ShotgunNormalTexturePath = TexturesPath + "/Shotgun_Normal.png";
        internal const string ShotgunMetallicTexturePath = TexturesPath + "/Shotgun_MetallicSmoothness.png";
        internal const string ShotgunOcclusionTexturePath = TexturesPath + "/Shotgun_Occlusion.png";
        internal const string ShotgunEmissionTexturePath = TexturesPath + "/Shotgun_Emission.png";
        internal const int ShotgunAtlasSize = 2048;
        internal static readonly RectInt FpsShotgunMetalUvZone = new RectInt(32, 1056, 1312, 960);
        internal static readonly RectInt FpsShotgunDarkUvZone = new RectInt(1376, 1056, 640, 288);
        internal static readonly RectInt FpsShotgunAccentUvZone = new RectInt(1376, 1376, 640, 288);
        internal static readonly RectInt FpsShotgunAccentCoreUvZone = new RectInt(1376, 1696, 640, 320);
        internal static readonly RectInt WorldShotgunMetalUvZone = new RectInt(32, 32, 1312, 960);
        internal static readonly RectInt WorldShotgunDarkUvZone = new RectInt(1376, 32, 640, 288);
        internal static readonly RectInt WorldShotgunAccentUvZone = new RectInt(1376, 352, 640, 288);
        internal static readonly RectInt WorldShotgunAccentCoreUvZone = new RectInt(1376, 672, 640, 320);

        internal const string HealthPickupsRootName = "HealthPickups";
        internal const string HealthPickupWestNorthName = "HealthPickup_WestNorth";
        internal const string HealthPickupEastSouthName = "HealthPickup_EastSouth";
        internal const float HealthPickupRespawnDelay = ArenaPickup.DefaultRespawnDelay;
        internal const float HealthPickupRestoreFraction = 0.33f;
        internal const float HealthPickupTriggerRadius = 1.50f;
        internal const string ShotgunPickupsRootName = "ShotgunPickups";
        internal const string AmmoPickupsRootName = "AmmoPickups";
        internal const string ShotgunPickupName = "ShotgunPickup_Center";
        internal const string AmmoPickupWestNorthName = "AmmoPickup_WestNorth";
        internal const string AmmoPickupEastSouthName = "AmmoPickup_EastSouth";
        internal const float ShotgunPickupRespawnDelay = 15f;
        internal const float AmmoPickupRespawnDelay = 15f;
        internal const int ShotgunPickupGrant = 8;
        internal const int AmmoPickupGrant = 8;
        internal const float ShotgunPickupTriggerRadius = 1.50f;
        internal const float AmmoPickupTriggerRadius = 1.50f;
        internal const int ShotgunShellCapacity = 16;
        internal static readonly Vector3 ShotgunPickupPosition = new Vector3(0f, 1.10f, 14f);
        internal static readonly Quaternion ShotgunPickupRotation = Quaternion.identity;
        internal static readonly Vector3 AmmoPickupWestNorthPosition = new Vector3(-38f, 1.10f, 18f);
        internal static readonly Vector3 AmmoPickupEastSouthPosition = new Vector3(38f, 1.10f, -18f);
        internal static readonly Quaternion AmmoPickupWestNorthRotation = Quaternion.identity;
        internal static readonly Quaternion AmmoPickupEastSouthRotation = Quaternion.Euler(0f, 180f, 0f);
        internal static readonly Vector3 PickupCueBluePosition = new Vector3(-0.55f, 0.65f, 0f);
        internal static readonly Vector3 PickupCueRedPosition = new Vector3(0.55f, 0.65f, 0f);
        internal static readonly Vector3 PickupCueScale = Vector3.one * 0.80f;
        internal static readonly Vector3 AmmoShellLeftPosition = new Vector3(-0.22f, 0.25f, 0f);
        internal static readonly Vector3 AmmoShellRightPosition = new Vector3(0.22f, 0.25f, 0f);
        internal static readonly Vector3 AmmoShellScale = new Vector3(0.16f, 0.32f, 0.16f);
        internal static readonly Color AmmoShellBaseColor = new Color(0.90f, 0.45f, 0.08f, 1f);
        internal static readonly Color AmmoShellEmissionColor = new Color(1f, 0.16f, 0.02f, 1f);
        internal const float AmmoShellEmissionStrength = 1.25f;
        internal static readonly Vector3 HealthPickupWestNorthPosition = new Vector3(-36f, 1.10f, -28f);
        internal static readonly Vector3 HealthPickupEastSouthPosition = new Vector3(36f, 1.10f, 28f);
        internal static readonly Quaternion HealthPickupWestNorthRotation = Quaternion.identity;
        internal static readonly Quaternion HealthPickupEastSouthRotation = Quaternion.Euler(0f, 180f, 0f);
        internal static readonly Vector3 HealthCrossHorizontalScale = new Vector3(1.40f, 0.30f, 0.30f);
        internal static readonly Vector3 HealthCrossVerticalScale = new Vector3(0.30f, 1.40f, 0.30f);
        internal static readonly Vector3 HealthCrossCoreScale = new Vector3(0.45f, 0.45f, 0.45f);

        // GameplayScene owns these project-level physics names and collision
        // settings. MaterialPrefab may bootstrap names needed by prefab roots;
        // GameplayScene always reasserts and persists the contract.
        internal const string ParticipantsLayerName = "Participants";
        internal const string ProjectilesLayerName = "Projectiles";
        internal const string LocalPlayerHiddenLayerName = "LocalPlayerHidden";

        internal const float BallPrefabScale = 4.32f;
        internal const float BallRadius = 2.16f;
        internal const float BallSpawnHeight = BallRadius;
        internal const float BlastRadius = 11.7f;
        internal const float GoalAxisPosition = 64f;
        internal const float PlayerSpawnOffset = 12f;
        internal const float CelebrationOrbitRadius = PlayerCameraFeedback.ExpectedCelebrationOrbitRadius;
        internal const float CelebrationOrbitHeight = PlayerCameraFeedback.ExpectedCelebrationOrbitHeight;
        internal const float CelebrationLookHeight = 2.1f;
        internal const float CelebrationOrbitDegrees = 360f;
        internal const float CelebrationFov = 60f;
        internal const float PlayerControllerRadius = 0.8f;
        internal const float PlayerControllerHeight = 3.6f;
        internal static readonly Vector3 PlayerControllerCenter = new Vector3(0f, 1.8f, 0f);
        internal const float PlayerControllerSkinWidth = 0.08f;
        internal const float PlayableFloorTop = 0f;
        internal const float ParticipantRecoveryThreshold = -1f;
        internal const float WorldVisualScale = 2f;
        internal const float PlayerHeadHeight = 3.1f;
        internal const float TeamCueScaleMultiplier = 2f;
        internal const float ImmunityShieldScaleMultiplier = 2f;
        internal const float NameplateHeight = 4.1f;
        internal const float LocalRespawnDelay = ParticipantState.DefaultDeathWait;
        internal const float BotRespawnDelay = 5f;
        internal const float RocketTrailLifetime = 0.55f;
        internal const float RocketTrailRateOverDistance = 1.5f;
        internal const float RocketTrailStartSize = 0.70f;
        internal const float RocketEmissionStrength = 3.0f;
        internal const float JumpVelocity = 4.80f;
        internal const float UnderfootForwardImpulseScale = 0.5625f;
        internal const float UnderfootUpwardImpulseScale = 1f;
        internal const float UnderfootHighSpeedVerticalRedirect = 1f;
        internal static readonly Vector2 FloorTextureScale = new Vector2(13f, 9f);
        internal static readonly Vector2 WallTextureScale = new Vector2(4f, 1f);
        internal const bool BotsEnabledByDefault = MatchController.DefaultBotsEnabled;

        internal static readonly Color WeaponAccentShellBaseColor = new Color(0.68f, 0.03f, 0.015f, 0.42f);
        internal const float WeaponAccentShellMetallic = 0f;
        internal const float WeaponAccentShellSmoothness = 0.96f;
        internal const float WeaponAccentShellOcclusion = 0.85f;
        internal const float WeaponAccentShellBumpScale = 0.35f;
        internal static readonly Color WeaponAccentCoreBaseColor = new Color(0.25f, 0.005f, 0.002f, 1f);
        internal const float WeaponAccentCoreMetallic = 0.15f;
        internal const float WeaponAccentCoreSmoothness = 0.80f;
        internal const float WeaponAccentCoreOcclusion = 0.90f;
        internal const float WeaponAccentCoreBumpScale = 0.50f;
        internal static readonly Color WeaponAccentCoreEmissionColor = new Color(1f, 0f, 0f, 1f);
        internal const float WeaponAccentCoreEmissionStrength = 2f;
        internal static readonly Color ShotgunMetalBaseColor = Color.white;
        internal static readonly Color ShotgunDarkBaseColor = Color.white;
        internal static readonly Color ShotgunAccentBaseColor = new Color(1f, 1f, 1f, 0.42f);
        internal static readonly Color ShotgunAccentCoreBaseColor = Color.white;
        internal static readonly Color ShotgunAccentCoreEmissionColor = new Color(1f, 0f, 0f, 1f);
        internal const float ShotgunAccentCoreEmissionStrength = 2f;
        internal const float WeaponBoundsTolerance = 0.025f;
        internal const float WeaponShellCoreInset = 0.002f;
        internal const float WeaponMeshIslandPositionTolerance = 0.0001f;
        internal const int WeaponMicroDetailNormalMaxTextureSize = 1024;
        internal static readonly Vector2 WeaponMicroDetailNormalTiling = new Vector2(16f, 16f);
        internal const float WeaponMicroDetailNormalScale = 0.24f;
        internal static readonly Vector3 LauncherViewmodelPosition = new Vector3(-0.28f, -0.30f, 0.31f);
        internal static readonly Vector3 ShotgunViewmodelPosition = new Vector3(0.30f, -0.34f, 0.42f);
        internal static readonly Vector3 LauncherWeaponBoundsMin = new Vector3(-0.16f, -0.14f, -0.20f);
        internal static readonly Vector3 LauncherWeaponBoundsMax = new Vector3(0.16f, 0.11f, 0.55f);
        internal static readonly Vector3 FpsShotgunBoundsMin = new Vector3(-0.11f, -0.18f, -0.29f);
        internal static readonly Vector3 FpsShotgunBoundsMax = new Vector3(0.11f, 0.12f, 0.66f);
        internal static readonly Vector3 WorldShotgunBoundsMin = new Vector3(-0.09f, -0.16f, -0.28f);
        internal static readonly Vector3 WorldShotgunBoundsMax = new Vector3(0.09f, 0.10f, 0.64f);

        internal static readonly Color RocketTrailStartColor = new Color(0.58f, 0.55f, 0.50f, 0.75f);
        internal static readonly Color RocketTrailEndColor = new Color(0.20f, 0.19f, 0.18f, 1f);
        internal static readonly Color RocketEmissionColor = new Color(1.00f, 0.20f, 0.04f, 1f);
        internal static readonly Color RocketBaseColor = Color.white;
        internal static readonly Color WeaponMetalBaseColor = new Color(0.95f, 0.86f, 0.70f, 1f);
        internal static readonly Color WeaponDarkBaseColor = new Color(0.88f, 0.90f, 0.92f, 1f);
        internal static readonly Color WeaponAccentBaseColor = new Color(0.68f, 0.03f, 0.015f, 0.42f);
        // Launcher and shotgun maps are single atlases. These are intentionally
        // neutral multipliers so authored wear colours remain visible.
        internal static readonly Color LauncherMetalBaseColor = Color.white;
        internal static readonly Color LauncherDarkBaseColor = Color.white;
        internal static readonly Color LauncherAccentBaseColor = new Color(1f, 1f, 1f, 0.42f);
        internal static readonly Color LauncherAccentCoreBaseColor = Color.white;
        internal const float LauncherMetallic = 1f;
        internal const float LauncherSmoothness = 1f;
        internal const float LauncherOcclusion = 1f;
        internal const float LauncherBumpScale = 1f;
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
            FpsShotgunModelPath,
            ShotgunModelPath,
            LauncherBaseColorTexturePath, LauncherNormalTexturePath, LauncherMetallicTexturePath,
            LauncherOcclusionTexturePath, LauncherEmissionTexturePath, WeaponMicroDetailNormalTexturePath,
            ShotgunBaseColorTexturePath, ShotgunNormalTexturePath, ShotgunMetallicTexturePath,
            ShotgunOcclusionTexturePath, ShotgunEmissionTexturePath,
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
            HealthPickupPrefabPath, ShotgunPickupPrefabPath, AmmoPickupPrefabPath,
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
            MaterialsPath + "/WeaponDark.mat", WeaponAccentCoreMaterialPath, WeaponAccentMaterialPath,
            ShotgunMetalMaterialPath, ShotgunDarkMaterialPath, ShotgunAccentCoreMaterialPath, ShotgunAccentMaterialPath,
            MaterialsPath + "/TeamBlue.mat", MaterialsPath + "/TeamRed.mat",
            MaterialsPath + "/TeamBlueShield.mat", MaterialsPath + "/TeamRedShield.mat",
            MaterialsPath + "/TeamBlueTrail.mat", MaterialsPath + "/TeamRedTrail.mat",
            HealthPickupMaterialPath,
            AmmoShellMaterialPath,
            BlueCircleCueMeshPath, RedTriangleCueMeshPath
        };

        internal static readonly string[] GameplaySceneOutputs =
        {
            ScenePath,
            EditorBuildSettingsPath,
            DynamicsManagerPath,
            TimeManagerPath,
            TagManagerPath
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

        internal static readonly string[] BakedOutputPaths = CreateBakedOutputPaths();

        internal static string[] BakedLightmapPaths(int count)
        {
            var paths = new string[count * 3];
            for (var i = 0; i < count; i++)
            {
                var path = BakedLightingPath + "/Lightmap-" + i + "_comp_";
                var offset = i * 3;
                paths[offset] = path + "dir.png";
                paths[offset + 1] = path + "light.exr";
                paths[offset + 2] = path + "shadowmask.png";
            }
            return paths;
        }

        internal static readonly WorldAnimatorTransitionSpecification[] WorldAnimatorTransitions = CreateWorldAnimatorTransitions();

        internal static readonly string[] ArenaPylonNames =
        {
            "NorthWallPylon_-48", "NorthWallPylon_-24", "NorthWallPylon_0", "NorthWallPylon_24", "NorthWallPylon_48",
            "SouthWallPylon_-48", "SouthWallPylon_-24", "SouthWallPylon_0", "SouthWallPylon_24", "SouthWallPylon_48"
        };

        internal static readonly float[] ArenaPylonXs = { -48f, -24f, 0f, 24f, 48f };
        internal const float ArenaNorthWallPylonZ = -44f;
        internal const float ArenaSouthWallPylonZ = 44f;
        internal static readonly Quaternion ArenaNorthWallPylonRotation = Quaternion.identity;
        internal static readonly Quaternion ArenaSouthWallPylonRotation = Quaternion.Euler(0f, 180f, 0f);

        internal readonly struct CollisionGeometrySpecification
        {
            internal readonly string Name;
            internal readonly Vector3 Position;
            internal readonly Vector3 Scale;
            internal readonly Quaternion Rotation;

            internal CollisionGeometrySpecification(string name, Vector3 position, Vector3 scale, Quaternion rotation)
            {
                Name = name;
                Position = position;
                Scale = scale;
                Rotation = rotation;
            }
        }

        internal static readonly CollisionGeometrySpecification[] PrimaryCollisionGeometry =
        {
            new CollisionGeometrySpecification("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 90f), Quaternion.identity),
            new CollisionGeometrySpecification("NorthWall", new Vector3(0f, 4f, -44.5f), new Vector3(130f, 8f, 1f), Quaternion.identity),
            new CollisionGeometrySpecification("SouthWall", new Vector3(0f, 4f, 44.5f), new Vector3(130f, 8f, 1f), Quaternion.identity),
            new CollisionGeometrySpecification("WestWallNorth", new Vector3(-64.5f, 4f, -31.75f), new Vector3(1f, 8f, 26.5f), Quaternion.identity),
            new CollisionGeometrySpecification("WestWallSouth", new Vector3(-64.5f, 4f, 31.75f), new Vector3(1f, 8f, 26.5f), Quaternion.identity),
            new CollisionGeometrySpecification("EastWallNorth", new Vector3(64.5f, 4f, -31.75f), new Vector3(1f, 8f, 26.5f), Quaternion.identity),
            new CollisionGeometrySpecification("EastWallSouth", new Vector3(64.5f, 4f, 31.75f), new Vector3(1f, 8f, 26.5f), Quaternion.identity),
            new CollisionGeometrySpecification("RampWest", new Vector3(-22f, 2.1f, 2f), new Vector3(18f, 0.5f, 20f), Quaternion.Euler(-15f, -90f, 0f)),
            new CollisionGeometrySpecification("RampEast", new Vector3(22f, 2.1f, -2f), new Vector3(18f, 0.5f, 20f), Quaternion.Euler(-15f, 90f, 0f))
        };

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
            internal readonly Vector2 DetailNormalTiling;
            internal readonly Color BaseColor;
            internal readonly Color EmissionColor;
            internal readonly float EmissionStrength;
            internal readonly float Metallic;
            internal readonly float Smoothness;
            internal readonly float OcclusionStrength;
            internal readonly float BumpScale;
            internal readonly float DetailNormalScale;

            internal PbrMaterialSpecification(string name, Texture2D baseMap, Texture2D normalMap,
                Texture2D metallicGlossMap, Texture2D occlusionMap, Texture2D emissionMap,
                Texture2D detailNormalMap, Vector2 textureScale, Color baseColor, Color emissionColor,
                float emissionStrength, float metallic, float smoothness, float occlusionStrength, float bumpScale,
                Vector2? detailNormalTiling = null, float detailNormalScale = 1f)
            {
                Name = name; BaseMap = baseMap; NormalMap = normalMap; MetallicGlossMap = metallicGlossMap;
                OcclusionMap = occlusionMap; EmissionMap = emissionMap; DetailNormalMap = detailNormalMap;
                TextureScale = textureScale; DetailNormalTiling = detailNormalTiling ?? Vector2.one; BaseColor = baseColor; EmissionColor = emissionColor;
                EmissionStrength = emissionStrength; Metallic = metallic; Smoothness = smoothness;
                OcclusionStrength = occlusionStrength; BumpScale = bumpScale; DetailNormalScale = detailNormalScale;
            }
        }

        private static string[] CreateBakedOutputPaths()
        {
            var lightmapPaths = BakedLightmapPaths(ExpectedLightmapCount);
            var paths = new string[2 + lightmapPaths.Length + 5];
            paths[0] = ScenePath;
            paths[1] = BakedLightingPath + "/LightingData.asset";
            Array.Copy(lightmapPaths, 0, paths, 2, lightmapPaths.Length);
            var reflectionOffset = 2 + lightmapPaths.Length;
            paths[reflectionOffset] = BakedLightingPath + "/ReflectionProbe-0.exr";
            paths[reflectionOffset + 1] = BakedLightingPath + "/ReflectionProbe-1.exr";
            paths[reflectionOffset + 2] = BakedLightingPath + "/ReflectionProbe-2.exr";
            paths[reflectionOffset + 3] = BakedLightingPath + "/ReflectionProbe-3.exr";
            paths[reflectionOffset + 4] = LightingManifestPath;
            return paths;
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
