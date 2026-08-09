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
using static RocketFooxball.Editor.MovementLabSerializedProperties;
using MaterialSpecification = RocketFooxball.Editor.MovementLabContract.MaterialSpecification;
using PbrMaterialSpecification = RocketFooxball.Editor.MovementLabContract.PbrMaterialSpecification;
using WorldAnimatorConditionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorConditionSpecification;
using WorldAnimatorTransitionSpecification = RocketFooxball.Editor.MovementLabContract.WorldAnimatorTransitionSpecification;

using static RocketFooxball.Editor.MovementLabContractCatalog;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabValidator
    {
        internal static void Validate(bool includeBakedLighting, bool logSuccess)
        {
            ValidateMovementLabInternal(ComputeBuilderSignature(), includeBakedLighting, logSuccess);
            MovementLabImportPipeline.ValidateTextureImporterContracts();
            MovementLabAnimatorPipeline.Validate();
            MovementLabPrefabPipeline.Validate();
            MovementLabArenaPipeline.Validate();
        }
        internal static void ValidatePreBakeSemantics() => ValidateMovementLabInternal(ComputeBuilderSignature(), false, false);
    }

    internal static partial class MovementLabValidator
    {
                internal static void ValidateMovementLabInternal(string builderSignature, bool includeBakedLighting, bool logSuccess)
                {
                    EnsureAssetExists(PrefabPath);
                    EnsureAssetExists(BallPrefabPath);
                    EnsureAssetExists(RocketPrefabPath);
                    EnsureAssetExists(RocketModelPath);
                    EnsureAssetExists(ArenaKitModelPath);
                    EnsureAssetExists(CharacterModelPath);
                    EnsureAssetExists(FpsKickModelPath);
                    EnsureAssetExists(WeaponModelPath);
                    EnsureAssetExists(GrassTexturePath);
                    EnsureAssetExists(GrassNormalTexturePath);
                    EnsureAssetExists(GrassMetallicTexturePath);
                    EnsureAssetExists(GrassOcclusionTexturePath);
                    EnsureAssetExists(BallTexturePath);
                    EnsureAssetExists(BallNormalTexturePath);
                    EnsureAssetExists(BallMetallicTexturePath);
                    EnsureAssetExists(BallOcclusionTexturePath);
                    EnsureAssetExists(WeaponMetalTexturePath);
                    EnsureAssetExists(WeaponMetalNormalTexturePath);
                    EnsureAssetExists(WeaponMetalMetallicTexturePath);
                    EnsureAssetExists(WeaponMetalOcclusionTexturePath);
                    EnsureAssetExists(WeaponDarkTexturePath);
                    EnsureAssetExists(WeaponDarkNormalTexturePath);
                    EnsureAssetExists(WeaponDarkMetallicTexturePath);
                    EnsureAssetExists(WeaponDarkOcclusionTexturePath);
                    EnsureAssetExists(WeaponAccentTexturePath);
                    EnsureAssetExists(WeaponAccentNormalTexturePath);
                    EnsureAssetExists(WeaponAccentMetallicTexturePath);
                    EnsureAssetExists(WeaponAccentOcclusionTexturePath);
                    EnsureAssetExists(WeaponAccentEmissionTexturePath);
                    EnsureAssetExists(RocketTexturePath);
                    EnsureAssetExists(RocketNormalTexturePath);
                    EnsureAssetExists(RocketMetallicTexturePath);
                    EnsureAssetExists(RocketOcclusionTexturePath);
                    EnsureAssetExists(RocketEmissionTexturePath);
                    EnsureAssetExists(RocketGlowTexturePath);
                    EnsureAssetExists(ExplosionTexturePath);
                    EnsureAssetExists(SmokeTexturePath);
                    EnsureAssetExists(SkyTexturePath);
                    EnsureAssetExists(SkyShaderPath);
                    EnsureAssetExists(DetailNormalTexturePath);
                    EnsureAssetExists(ToonShaderPath);
                    EnsureAssetExists(ParticleShaderPath);
                    EnsureAssetExists(AdditiveParticleShaderPath);
                    EnsureAssetExists(PowerGridShaderPath);
                    EnsureAssetExists(ShieldShaderPath);
                    EnsureAssetExists(WallTexturePath);
                    EnsureAssetExists(TrimTexturePath);
                    EnsureAssetExists(HazardTexturePath);
                    EnsureAssetExists(ShieldTexturePath);
                    EnsureAssetExists(WorldControllerPath);
                    EnsureAssetExists(FpsControllerPath);
                    EnsureAssetExists(ExplosionPrefabPath);
                    EnsureAssetExists(ScenePath);
                    EnsureAssetExists(BallSurfacePath);
                    EnsureAssetExists(RocketHotMaterialPath);
                    EnsureAssetExists(ProjectileGlowMaterialPath);
                    EnsureAssetExists(ExplosionAdditiveMaterialPath);
                    EnsureAssetExists(ExplosionSparksMaterialPath);
                    EnsureAssetExists(GridCeilingMaterialPath);
                    EnsureAssetExists(GridLongWallMaterialPath);
                    EnsureAssetExists(GridEndWallMaterialPath);
                    EnsureAssetExists(SkyMaterialPath);
                    EnsureAssetExists(VolumeProfilePath);
                    EnsureAssetExists(LightingSettingsPath);
                    EnsureAssetExists(LightingManifestPath);
                    EnsureAssetExists(ReflectionCenterPath);
                    EnsureAssetExists(ReflectionWestPath);
                    EnsureAssetExists(ReflectionEastPath);

                    var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    if (!scene.IsValid() || scene.path != ScenePath)
                    {
                        throw new InvalidOperationException("MovementLab scene failed to reopen: " + ScenePath);
                    }

                    var arena = GameObject.Find("Arena");
                    var player = GameObject.Find("Player");
                    var ball = GameObject.Find("Ball");
                    var matchObject = GameObject.Find("MatchController");
                    var explosionObject = GameObject.Find("ExplosionResolver");
                    var hudObject = GameObject.Find("DebugHUD");
                    Require(arena, "Arena root");
                    Require(player, "Player root");
                    Require(ball, "Ball root");
                    Require(matchObject, "MatchController root");
                    Require(explosionObject, "ExplosionResolver root");
                    Require(hudObject, "DebugHUD root");
                    Require(GameObject.Find(GetBuildMarkerName(builderSignature)), "T5 build marker");

                    var playerMotor = Require(player.GetComponent<PlayerMotor>(), "PlayerMotor");
                    var input = Require(player.GetComponent<PlayerInputReader>(), "PlayerInputReader");
                    var look = Require(player.GetComponent<PlayerLook>(), "PlayerLook");
                    var cameraFeedback = Require(player.GetComponent<PlayerCameraFeedback>(), "PlayerCameraFeedback");
                    var launcher = Require(player.GetComponent<RocketLauncher>(), "RocketLauncher");
                    var kick = Require(player.GetComponent<BallKick>(), "BallKick");
                    var camera = Require(player.GetComponentInChildren<Camera>(true), "Player camera");
                    var qualityRuntime = Require(camera.GetComponent<GraphicsQualityRuntime>(), "GraphicsQualityRuntime");
                    if (camera.clearFlags != CameraClearFlags.SolidColor || Mathf.Abs(camera.backgroundColor.r - 0.72f) > 0.001f || Mathf.Abs(camera.backgroundColor.g - 0.88f) > 0.001f || Mathf.Abs(camera.backgroundColor.b - 0.96f) > 0.001f || Mathf.Abs(camera.fieldOfView - 75f) > 0.001f || Mathf.Abs(camera.farClipPlane - 180f) > 0.01f) throw new InvalidOperationException("Gameplay camera bright-scene contract invalid.");
                    if (RenderSettings.skybox == null || RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox || !RenderSettings.fog || Mathf.Abs(RenderSettings.fogStartDistance - 75f) > 0.01f || Mathf.Abs(RenderSettings.fogEndDistance - 170f) > 0.01f) throw new InvalidOperationException("Scene environment contract invalid.");
                    Require(player.GetComponent<CharacterController>(), "Player CharacterController");
                    if (Vector3.Distance(player.transform.position, new Vector3(PlayerSpawnOffset, 0f, 0f)) > 0.001f || Vector3.Dot(player.transform.forward, Vector3.left) < 0.999f)
                    {
                        throw new InvalidOperationException("Player spawn must be neutral midfield offset on goal axis facing centered ball.");
                    }

                    var ballMotor = Require(ball.GetComponent<BallMotor>(), "BallMotor");
                    var ballBody = Require(ball.GetComponent<Rigidbody>(), "Ball Rigidbody");
                    var ballCollider = Require(ball.GetComponent<Collider>(), "Ball collider");
                    if (ballBody.isKinematic || ballBody.useGravity == false || ballBody.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
                    {
                        throw new InvalidOperationException("Ball Rigidbody must be dynamic, gravity-enabled, and ContinuousDynamic.");
                    }
                    var ballSurface = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallSurfacePath);
                    if (ballSurface == null || ballCollider.sharedMaterial != ballSurface)
                    {
                        throw new InvalidOperationException("Ball collider is missing shared BallSurface material.");
                    }
                    if (Vector3.Distance(ball.transform.position, new Vector3(0f, BallSpawnHeight, 0f)) > 0.001f)
                    {
                        throw new InvalidOperationException("Ball spawn/reset height must match the enlarged ball radius.");
                    }

                    var resolver = Require(explosionObject.GetComponent<ExplosionResolver>(), "ExplosionResolver");
                    var match = Require(matchObject.GetComponent<MatchController>(), "MatchController");
                    var hud = Require(hudObject.GetComponent<MovementDebugHud>(), "MovementDebugHud");
                    ValidateSerializedFloat(resolver, "blastRadius", BlastRadius, "ExplosionResolver.blastRadius");
                    ValidateSerializedFloat(resolver, "underfootForwardImpulseScale", UnderfootForwardImpulseScale, "ExplosionResolver.underfootForwardImpulseScale");
                    ValidateSerializedFloat(resolver, "underfootUpwardImpulseScale", UnderfootUpwardImpulseScale, "ExplosionResolver.underfootUpwardImpulseScale");
                    ValidateSerializedFloat(resolver, "underfootHighSpeedVerticalRedirect", UnderfootHighSpeedVerticalRedirect, "ExplosionResolver.underfootHighSpeedVerticalRedirect");
                    var goals = UnityEngine.Object.FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (goals.Length != 2)
                    {
                        throw new InvalidOperationException("MovementLab must contain exactly two GoalTrigger components.");
                    }

                    GoalTrigger north = null;
                    GoalTrigger south = null;
                    for (var i = 0; i < goals.Length; i++)
                    {
                        var trigger = goals[i];
                        Require(trigger.GetComponent<Collider>(), trigger.name + " goal collider");
                        if (!trigger.GetComponent<Collider>().isTrigger)
                        {
                            throw new InvalidOperationException(trigger.name + " goal plane must be a trigger collider.");
                        }
                        if (trigger.Side == GoalTrigger.GoalSide.North)
                        {
                            if (north != null) throw new InvalidOperationException("Duplicate North goal.");
                            north = trigger;
                        }
                        else
                        {
                            if (south != null) throw new InvalidOperationException("Duplicate South goal.");
                            south = trigger;
                        }
                    }
                    Require(north, "North goal");
                    Require(south, "South goal");
                    if (Vector3.Distance(north.transform.position, new Vector3(-GoalAxisPosition, 0f, 0f)) > 0.01f ||
                        Vector3.Distance(south.transform.position, new Vector3(GoalAxisPosition, 0f, 0f)) > 0.01f ||
                        Vector3.Dot(north.transform.forward, Vector3.left) < 0.999f ||
                        Vector3.Dot(south.transform.forward, Vector3.right) < 0.999f)
                    {
                        throw new InvalidOperationException("Goals must face across longest arena axis at opposite furthest walls.");
                    }
                    ValidateSerializedVector3(north, "planeNormal", Vector3.right, "NorthGoal.planeNormal");
                    ValidateSerializedVector3(south, "planeNormal", Vector3.right, "SouthGoal.planeNormal");

                    var northShield = Require(north.transform.Find("ShieldCollider"), "North goal ShieldCollider").GetComponent<Collider>();
                    var southShield = Require(south.transform.Find("ShieldCollider"), "South goal ShieldCollider").GetComponent<Collider>();
                    Require(northShield, "North goal shield collider");
                    Require(southShield, "South goal shield collider");
                    if (northShield.isTrigger || southShield.isTrigger)
                    {
                        throw new InvalidOperationException("Goal shields must block player/rocket with non-trigger colliders.");
                    }

                    ValidateReference(ballMotor, "body", ballBody, "BallMotor.body");
                    ValidateReference(ballMotor, "ballCollider", ballCollider, "BallMotor.ballCollider");
                    ValidateReference(ballMotor, "player", playerMotor, "BallMotor.player");
                    ValidateArrayContains(ballMotor, "goalShieldColliders", northShield, southShield, "BallMotor.goalShieldColliders");
                    ValidateReference(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath), "PlayerInputReader.actions");
                    ValidateReference(playerMotor, "input", input, "PlayerMotor.input");
                    ValidateReference(look, "input", input, "PlayerLook.input");
                    ValidateReference(look, "head", player.transform.Find("Head"), "PlayerLook.head");
                    ValidateReference(launcher, "input", input, "RocketLauncher.input");
                    ValidateReference(launcher, "look", look, "RocketLauncher.look");
                    ValidateReference(launcher, "aimCamera", camera, "RocketLauncher.aimCamera");
                    ValidateReference(launcher, "spawnPoint", player.transform.Find("Head/Camera/RocketMuzzle"), "RocketLauncher.spawnPoint");
                    ValidateReference(launcher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "RocketLauncher.projectilePrefab");
                    ValidateReference(launcher, "explosionResolver", resolver, "RocketLauncher.explosionResolver");
                    ValidateReference(cameraFeedback, "player", playerMotor, "PlayerCameraFeedback.player");
                    ValidateReference(cameraFeedback, "targetCamera", camera, "PlayerCameraFeedback.targetCamera");
                    ValidateReference(qualityRuntime, "targetCamera", camera, "GraphicsQualityRuntime.targetCamera");
                    ValidateSerializedFloat(cameraFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius, "PlayerCameraFeedback.celebrationOrbitRadius");
                    ValidateSerializedFloat(cameraFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight, "PlayerCameraFeedback.celebrationOrbitHeight");
                    ValidateSerializedFloat(cameraFeedback, "celebrationLookHeight", CelebrationLookHeight, "PlayerCameraFeedback.celebrationLookHeight");
                    ValidateSerializedFloat(cameraFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees, "PlayerCameraFeedback.celebrationOrbitDegrees");
                    ValidateSerializedFloat(cameraFeedback, "celebrationFov", CelebrationFov, "PlayerCameraFeedback.celebrationFov");
                    ValidateReference(kick, "input", input, "BallKick.input");
                    ValidateReference(kick, "player", playerMotor, "BallKick.player");
                    ValidateReference(kick, "look", look, "BallKick.look");
                    ValidateReference(kick, "aimCamera", camera, "BallKick.aimCamera");
                    ValidateReference(kick, "ball", ballMotor, "BallKick.ball");
                    ValidateArrayContains(resolver, "goalShieldColliders", northShield, southShield, "ExplosionResolver.goalShieldColliders");
                    ValidatePrefabReference(resolver, "explosionVfxPrefab", ExplosionPrefabPath, "ExplosionResolver.explosionVfxPrefab");

                    var presentation = Require(player.GetComponent<PlayerPresentation>(), "PlayerPresentation");
                    ValidateReference(presentation, "kick", kick, "PlayerPresentation.kick");
                    ValidateReference(presentation, "motor", playerMotor, "PlayerPresentation.motor");
                    ValidateReference(presentation, "launcher", launcher, "PlayerPresentation.launcher");
                    var worldVisual = Require(player.transform.Find("WorldVisual"), "Player WorldVisual");
                    var worldAnimator = Require(worldVisual.GetComponent<Animator>(), "World Animator");
                    ValidateReference(presentation, "worldAnimator", worldAnimator, "PlayerPresentation.worldAnimator");
                    var viewmodels = Require(camera.transform.Find("Viewmodels"), "Viewmodels");
                    var weaponVisual = Require(viewmodels.Find("WeaponVisual"), "WeaponVisual");
                    var fpsVisual = Require(viewmodels.Find("FpsKickVisual"), "FpsKickVisual");
                    var fpsAnimator = Require(fpsVisual.GetComponent<Animator>(), "FPS Animator");
                    ValidateReference(presentation, "fpsKickAnimator", fpsAnimator, "PlayerPresentation.fpsKickAnimator");
                    ValidateReference(presentation, "weaponVisual", weaponVisual, "PlayerPresentation.weaponVisual");
                    if (worldAnimator.applyRootMotion || fpsAnimator.applyRootMotion)
                    {
                        throw new InvalidOperationException("Player visual animators must not apply root motion.");
                    }
                    if (worldAnimator.avatar == null || fpsAnimator.avatar == null)
                    {
                        throw new InvalidOperationException("World/FPS animators must have imported avatars.");
                    }
                    MovementLabPrefabPipeline.ValidateImportedVisual(worldVisual.gameObject, CharacterModelPath, "WorldVisual");
                    MovementLabPrefabPipeline.ValidateImportedVisual(weaponVisual.gameObject, WeaponModelPath, "WeaponVisual");
                    MovementLabMaterialPipeline.ValidateWeaponMaterials(weaponVisual.gameObject);
                    MovementLabPrefabPipeline.ValidateImportedVisual(fpsVisual.gameObject, FpsKickModelPath, "FpsKickVisual");
                    MovementLabPrefabPipeline.ValidateNoPhysics(weaponVisual.gameObject, "WeaponVisual");
                    MovementLabPrefabPipeline.ValidateNoPhysics(fpsVisual.gameObject, "FpsKickVisual");
                    MovementLabAnimatorPipeline.ValidateWorldAnimatorController(worldAnimator, WorldControllerPath, CharacterModelPath);
                    MovementLabPrefabPipeline.ValidateAnimatorController(fpsAnimator, FpsControllerPath, FpsKickModelPath);
                    var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
                    if (hiddenLayer < 0 || (camera.cullingMask & (1 << hiddenLayer)) != 0)
                    {
                        throw new InvalidOperationException("LocalPlayerHidden layer must be excluded from player camera culling.");
                    }
                    MovementLabPrefabPipeline.ValidateLayerRecursively(worldVisual.gameObject, hiddenLayer, "WorldVisual");
                    MovementLabPrefabPipeline.ValidateLayerExcluded(viewmodels.gameObject, hiddenLayer, "Viewmodels");
                    MovementLabPrefabPipeline.ValidateCrosshair(camera);
                    MovementLabPrefabPipeline.ValidateTrail(AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath));
                    MovementLabPrefabPipeline.ValidateExplosionPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath));

                    ValidateReference(north, "ball", ballMotor, "NorthGoal.ball");
                    ValidateReference(north, "match", match, "NorthGoal.match");
                    ValidateReference(north, "planeReference", north.transform, "NorthGoal.planeReference");
                    ValidateReference(north, "openingTrigger", north.GetComponent<Collider>(), "NorthGoal.openingTrigger");
                    ValidateReference(south, "ball", ballMotor, "SouthGoal.ball");
                    ValidateReference(south, "match", match, "SouthGoal.match");
                    ValidateReference(south, "planeReference", south.transform, "SouthGoal.planeReference");
                    ValidateReference(south, "openingTrigger", south.GetComponent<Collider>(), "SouthGoal.openingTrigger");

                    ValidateReference(match, "input", input, "MatchController.input");
                    ValidateReference(match, "player", playerMotor, "MatchController.player");
                    ValidateReference(match, "playerLook", look, "MatchController.playerLook");
                    ValidateReference(match, "cameraFeedback", cameraFeedback, "MatchController.cameraFeedback");
                    ValidateReference(match, "ball", ballMotor, "MatchController.ball");
                    ValidateReference(match, "launcher", launcher, "MatchController.launcher");
                    ValidateReference(match, "kick", kick, "MatchController.kick");
                    ValidateReference(match, "northGoal", north, "MatchController.northGoal");
                    ValidateReference(match, "southGoal", south, "MatchController.southGoal");
                    ValidateSerializedFloat(match, "goalFreezeDuration", GoalFreezeDuration, "MatchController.goalFreezeDuration");
                    ValidateSerializedVector3(match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f), "MatchController.ballResetPosition");
                    ValidateSerializedVector3(match, "playerResetPosition", new Vector3(PlayerSpawnOffset, 0f, 0f), "MatchController.playerResetPosition");
                    ValidateReference(hud, "player", playerMotor, "HUD.player");
                    ValidateReference(hud, "ball", ballMotor, "HUD.ball");
                    ValidateReference(hud, "launcher", launcher, "HUD.launcher");
                    ValidateReference(hud, "kick", kick, "HUD.kick");
                    ValidateReference(hud, "match", match, "HUD.match");

                    MovementLabPrefabPipeline.ValidatePrefab(PrefabPath, "Player", false, ballSurface);
                    MovementLabPrefabPipeline.ValidatePrefab(BallPrefabPath, "Ball", true, ballSurface);
                    MovementLabPrefabPipeline.ValidatePrefab(RocketPrefabPath, "Rocket", false, null);
                    MovementLabArenaPipeline.ValidateArenaMaterials(arena, ballSurface);
                    MovementLabArenaPipeline.ValidateArenaArchitecture(arena);
                    MovementLabMaterialPipeline.ValidateOpaqueMaterialReferences();
                    MovementLabImportPipeline.ValidateTextureImporterContracts();
                    MovementLabImportPipeline.ValidateModelImporterContracts();
                    MovementLabSceneComposer.ValidateRenderPipelineSettings();
                    MovementLabLightingPipeline.ValidateSceneEnvironment(scene, arena, includeBakedLighting);
                    MovementLabSceneComposer.ValidatePhysicsAndBuildSettings();
                    ValidateNoMissingComponents(scene);

                    if (logSuccess)
                    {
                        Debug.Log("Rocket Fooxball Movement Lab validation succeeded: " + ScenePath);
                    }
                }

                internal static void ValidateNoMissingComponents(Scene scene)
                {
                    var roots = scene.GetRootGameObjects();
                    for (var i = 0; i < roots.Length; i++)
                    {
                        var components = roots[i].GetComponentsInChildren<Component>(true);
                        for (var j = 0; j < components.Length; j++)
                        {
                            if (components[j] == null)
                            {
                                throw new InvalidOperationException("Missing script/component under " + roots[i].name);
                            }
                        }
                    }
                }

                internal static void EnsureAssetExists(string path)
                {
                    if (!File.Exists(path))
                    {
                        throw new InvalidOperationException("Missing generated asset: " + path);
                    }
                }

    }
}
