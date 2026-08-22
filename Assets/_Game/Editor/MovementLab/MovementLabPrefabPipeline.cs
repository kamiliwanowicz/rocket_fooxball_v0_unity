using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RocketFooxball.Runtime.Ball;
using RocketFooxball.Runtime.Bots;
using RocketFooxball.Runtime.Diagnostics;
using RocketFooxball.Runtime.Feedback;
using RocketFooxball.Runtime.Input;
using RocketFooxball.Runtime.Match;
using RocketFooxball.Runtime.Movement;
using RocketFooxball.Runtime.Physics;
using RocketFooxball.Runtime.Rendering;
using RocketFooxball.Runtime.Weapons;
using RocketFooxball.Runtime.Participants;
using RocketFooxball.Runtime.Pickups;
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
using static RocketFooxball.Editor.MovementLabImportPipeline;
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
using static RocketFooxball.Editor.MovementLabAnimatorPipeline;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabPrefabPipeline
    {
        internal static void Validate()
        {
            RequireComponent<RocketFooxball.Runtime.Movement.PlayerMotor>(MovementLabContract.PlayerPrefabPath, "PlayerMotor");
            RequireComponent<RocketFooxball.Runtime.Ball.BallMotor>(MovementLabContract.BallPrefabPath, "BallMotor");
            RequireComponent<RocketFooxball.Runtime.Weapons.RocketLauncher>(MovementLabContract.PlayerPrefabPath, "RocketLauncher");
            RequireComponent<RocketFooxball.Runtime.Weapons.ShotgunWeapon>(MovementLabContract.PlayerPrefabPath, "ShotgunWeapon");
            RequireComponent<RocketFooxball.Runtime.Participants.ParticipantState>(MovementLabContract.PlayerPrefabPath, "ParticipantState");
            RequireComponent<RocketFooxball.Runtime.Feedback.ExplosionVfx>(MovementLabContract.ExplosionPrefabPath, "ExplosionVfx");
            RequireComponent<RocketFooxball.Runtime.Pickups.HealthPickup>(MovementLabContract.HealthPickupPrefabPath, "HealthPickup");
            RequireComponent<RocketFooxball.Runtime.Pickups.ShotgunPickup>(MovementLabContract.ShotgunPickupPrefabPath, "ShotgunPickup");
            RequireComponent<RocketFooxball.Runtime.Pickups.AmmoPickup>(MovementLabContract.AmmoPickupPrefabPath, "AmmoPickup");
            ValidateBotPrefabContract();
        }

        private static void ValidateBotPrefabContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MovementLabContract.PlayerPrefabPath);
            if (prefab == null) throw new InvalidOperationException("Player prefab is required for bot composition.");
            var participant = prefab.GetComponent<ParticipantState>();
            var controller = prefab.GetComponent<BotController>();
            var navigator = prefab.GetComponent<BotNavigator>();
            var perception = prefab.GetComponent<BotPerception>();
            if (participant == null || controller == null || navigator == null || perception == null ||
                prefab.GetComponents<BotController>().Length != 1 || prefab.GetComponents<BotNavigator>().Length != 1 ||
                prefab.GetComponents<BotPerception>().Length != 1 || controller.enabled || navigator.enabled || perception.enabled)
                throw new InvalidOperationException("Player prefab must contain exactly one disabled BotController, BotNavigator, and BotPerception.");
            ValidateReference(participant, "botController", controller, "Player prefab ParticipantState.botController");
            ValidateReference(controller, "participant", participant, "Player prefab BotController.participant");
        }
        internal static void RequireComponent<T>(string path, string label) where T : UnityEngine.Component
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            if (prefab == null || prefab.GetComponent<T>() == null) throw new System.InvalidOperationException("Generated prefab missing " + label + ": " + path);
        }
    }

    internal static partial class MovementLabPrefabPipeline
    {
                internal static GameObject BuildPlayerPrefab(GameObject rocketPrefab)
                {
                    var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
                    if (actions == null)
                    {
                        throw new InvalidOperationException("Missing Input System asset: " + InputActionsPath);
                    }
                    if (rocketPrefab == null || rocketPrefab.GetComponent<RocketProjectile>() == null)
                    {
                        throw new InvalidOperationException("Rocket prefab must exist before Player prefab build.");
                    }

                    var characterModel = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterModelPath);
                    var fpsKickModel = AssetDatabase.LoadAssetAtPath<GameObject>(FpsKickModelPath);
                    var weaponModel = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponModelPath);
                    var fpsShotgunModel = AssetDatabase.LoadAssetAtPath<GameObject>(FpsShotgunModelPath);
                    var shotgunModel = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunModelPath);
                    if (characterModel == null || fpsKickModel == null || weaponModel == null || fpsShotgunModel == null || shotgunModel == null)
                    {
                        throw new InvalidOperationException("Missing imported character, FPS kick, rocket weapon, or shotgun model.");
                    }

                    var root = new GameObject("Player") { tag = "Player" };
                    var controller = root.AddComponent<CharacterController>();
                    controller.radius = PlayerControllerRadius;
                    controller.height = PlayerControllerHeight;
                    controller.center = PlayerControllerCenter;
                    controller.slopeLimit = 60f;
                    controller.stepOffset = 0.3f;
                    controller.skinWidth = PlayerControllerSkinWidth;

                    var input = root.AddComponent<PlayerInputReader>();
                    var motor = root.AddComponent<PlayerMotor>();
                    var look = root.AddComponent<PlayerLook>();
                    var feedback = root.AddComponent<PlayerCameraFeedback>();
                    var launcher = root.AddComponent<RocketLauncher>();
                    var kick = root.AddComponent<BallKick>();
                    var presentation = root.AddComponent<PlayerPresentation>();
                    var participant = root.AddComponent<ParticipantState>();
                    var shotgun = root.AddComponent<ShotgunWeapon>();
                    var participantLayer = EnsureGameplayLayer(MovementLabContract.ParticipantsLayerName);
                    var projectileLayer = EnsureGameplayLayer(MovementLabContract.ProjectilesLayerName);
                    root.layer = participantLayer;
                    var head = new GameObject("Head").transform;
                    head.SetParent(root.transform, false);
                    head.localPosition = new Vector3(0f, PlayerHeadHeight, 0f);
                    var camera = new GameObject("Camera").AddComponent<Camera>();
                    camera.transform.SetParent(head, false);
                    camera.tag = "MainCamera";
                    camera.fieldOfView = 75f;
                    camera.nearClipPlane = 0.03f;
                    camera.farClipPlane = 180f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.72f, 0.88f, 0.96f, 1f);
                    camera.gameObject.AddComponent<AudioListener>();
                    camera.allowHDR = true;
                    var cameraData = camera.GetUniversalAdditionalCameraData();
                    cameraData.renderPostProcessing = true;
                    cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    cameraData.antialiasingQuality = AntialiasingQuality.High;
                    var qualityRuntime = camera.gameObject.AddComponent<GraphicsQualityRuntime>();
                    var muzzle = new GameObject("RocketMuzzle").transform;
                    muzzle.SetParent(camera.transform, false);
                    muzzle.localPosition = new Vector3(0f, -0.05f, 0.45f);

                    var hiddenLayer = EnsureLocalPlayerHiddenLayer();
                    camera.cullingMask &= ~(1 << hiddenLayer);

                    var characterRed = GetOrCreateRetroMaterial("CharacterRed", new Color(0.56f, 0.025f, 0.035f), null, Vector2.one);
                    var characterBlack = GetOrCreateRetroMaterial("CharacterBlack", new Color(0.018f, 0.014f, 0.018f), null, Vector2.one);
                    var characterCream = GetOrCreateRetroMaterial("CharacterCream", new Color(0.78f, 0.67f, 0.50f), null, Vector2.one);
                    var characterEye = GetOrCreateRetroMaterial("CharacterEye", new Color(0.96f, 0.04f, 0.02f), null, Vector2.one);
                    var teamBlueMaterial = GetOrCreateRetroMaterial("TeamBlue", new Color(0.08f, 0.35f, 1.00f, 1f), null, Vector2.one);
                    var teamRedMaterial = GetOrCreateRetroMaterial("TeamRed", new Color(1.00f, 0.12f, 0.10f, 1f), null, Vector2.one);
                    var teamBlueShieldMaterial = GetOrCreateShieldMaterial("TeamBlueShield", new Color(0.10f, 0.50f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    var teamRedShieldMaterial = GetOrCreateShieldMaterial("TeamRedShield", new Color(1.00f, 0.22f, 0.20f, 1f), new Color(1.00f, 0.55f, 0.45f, 1f));
                    var worldVisual = InstantiateImportedVisual(characterModel, "WorldVisual", root.transform, Vector3.zero, Quaternion.identity, Vector3.one * WorldVisualScale);
                    AssignImportedMaterials(worldVisual, characterRed, characterBlack, characterCream, characterEye);
                    var worldAnimator = worldVisual.GetComponent<Animator>();
                    if (worldAnimator == null)
                    {
                        worldAnimator = worldVisual.AddComponent<Animator>();
                    }
                    worldAnimator.runtimeAnimatorController = EnsureWorldAnimatorController(WorldControllerPath, CharacterModelPath);
                    worldAnimator.avatar = FindImportedAvatar(CharacterModelPath);
                    worldAnimator.applyRootMotion = false;

                    var handR = FindNamedTransform(worldVisual.transform, "Hand.R");
                    if (handR == null)
                    {
                        throw new InvalidOperationException("World shotgun requires imported Hand.R bind pose: " + CharacterModelPath);
                    }
                    var worldShotgunMount = new GameObject("WorldShotgunMount").transform;
                    worldShotgunMount.position = handR.position;
                    worldShotgunMount.rotation = root.transform.rotation;
                    worldShotgunMount.SetParent(handR, true);

                    // Hide the complete imported world model from the local player's camera.
                    // The imported eye/head and body meshes are separate branches, so hiding
                    // only CharacterHead leaves the rest of the model rendered in first person.
                    // Reusable prefab keeps world model visible. ParticipantState applies
                    // LocalPlayerHidden only for local slot at runtime.
                    SetLayerRecursively(worldVisual, 0);

                    var blueCue = CreateShapeCue("BlueCircleCue", false, teamBlueMaterial, new Vector3(0f, 1.12f, -0.32f));
                    blueCue.transform.SetParent(root.transform, false);
                    blueCue.transform.localScale *= TeamCueScaleMultiplier;
                    var redCue = CreateShapeCue("RedTriangleCue", true, teamRedMaterial, new Vector3(0f, 1.12f, -0.32f));
                    redCue.transform.SetParent(root.transform, false);
                    redCue.transform.localScale *= TeamCueScaleMultiplier;
                    redCue.SetActive(false);

                    var immunityShield = new GameObject("ImmunityShield");
                    immunityShield.transform.SetParent(root.transform, false);
                    immunityShield.transform.localPosition = Vector3.zero;
                    immunityShield.SetActive(false);
                    var blueImmunityShield = CreateImmunityShieldVfx("BlueImmunityShield", immunityShield.transform, teamBlueShieldMaterial);
                    var redImmunityShield = CreateImmunityShieldVfx("RedImmunityShield", immunityShield.transform, teamRedShieldMaterial);
                    blueImmunityShield.transform.localScale *= ImmunityShieldScaleMultiplier;
                    redImmunityShield.transform.localScale *= ImmunityShieldScaleMultiplier;

                    var nameplate = new GameObject("Nameplate");
                    nameplate.transform.SetParent(root.transform, false);
                    nameplate.transform.localPosition = new Vector3(0f, NameplateHeight, 0f);
                    var nameplateText = nameplate.AddComponent<TextMesh>();
                    nameplateText.text = "Participant";
                    nameplateText.anchor = TextAnchor.MiddleCenter;
                    nameplateText.alignment = TextAlignment.Center;
                    nameplateText.characterSize = 0.24f;
                    nameplateText.fontSize = 32;
                    nameplateText.color = Color.white;

                    var viewmodels = new GameObject("Viewmodels").transform;
                    viewmodels.SetParent(camera.transform, false);
                    viewmodels.localPosition = Vector3.zero;
                    viewmodels.localRotation = Quaternion.identity;
                    // Keep the launcher close enough that the camera crops its rear like a classic FPS viewmodel.
                    var weaponVisual = InstantiateImportedVisual(weaponModel, "WeaponVisual", viewmodels, new Vector3(-0.28f, -0.22f, 0.34f), Quaternion.identity, Vector3.one);
                    var weaponMetal = GetOrCreateLitMaterial(new PbrMaterialSpecification("WeaponMetal", LoadTexture(WeaponMetalTexturePath), LoadTexture(WeaponMetalNormalTexturePath), LoadTexture(WeaponMetalMetallicTexturePath), LoadTexture(WeaponMetalOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponMetalBaseColor, Color.clear, 0f, 1f, 1f, 0.70f, 1f));
                    var weaponDark = GetOrCreateLitMaterial(new PbrMaterialSpecification("WeaponDark", LoadTexture(WeaponDarkTexturePath), LoadTexture(WeaponDarkNormalTexturePath), LoadTexture(WeaponDarkMetallicTexturePath), LoadTexture(WeaponDarkOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponDarkBaseColor, Color.clear, 0f, 1f, 1f, 0.70f, 1f));
                    var weaponAccent = GetOrCreateWeaponShellMaterial("WeaponAccent");
                    var weaponAccentCore = GetOrCreateWeaponCoreMaterial("WeaponAccentCore");
                    AssignImportedMaterials(weaponVisual, weaponMetal, weaponDark, weaponAccent, weaponAccentCore);
                    RemovePhysicsComponents(weaponVisual);
                    var shotgunMetal = GetOrCreateLitMaterial(new PbrMaterialSpecification("ShotgunMetal", LoadTexture(WeaponMetalTexturePath), LoadTexture(WeaponMetalNormalTexturePath), LoadTexture(WeaponMetalMetallicTexturePath), LoadTexture(WeaponMetalOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, ShotgunMetalBaseColor, Color.clear, 0f, 1f, 1f, 0.70f, 1f));
                    var shotgunDark = GetOrCreateLitMaterial(new PbrMaterialSpecification("ShotgunDark", LoadTexture(WeaponDarkTexturePath), LoadTexture(WeaponDarkNormalTexturePath), LoadTexture(WeaponDarkMetallicTexturePath), LoadTexture(WeaponDarkOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, ShotgunDarkBaseColor, Color.clear, 0f, 1f, 1f, 0.70f, 1f));
                    var shotgunAccent = GetOrCreateWeaponShellMaterial("ShotgunAccent");
                    var shotgunAccentCore = GetOrCreateWeaponCoreMaterial("ShotgunAccentCore");
                    var fpsShotgunVisual = InstantiateImportedVisual(fpsShotgunModel, "FpsShotgunVisual", viewmodels, new Vector3(0.30f, -0.28f, 0.45f), Quaternion.identity, Vector3.one);
                    AssignImportedMaterials(fpsShotgunVisual, shotgunMetal, shotgunDark, shotgunAccent, shotgunAccentCore);
                    RemovePhysicsAndAnimators(fpsShotgunVisual);
                    var worldShotgunVisual = InstantiateImportedVisual(shotgunModel, "WorldShotgunVisual", worldShotgunMount, Vector3.zero, Quaternion.identity, Vector3.one);
                    AssignImportedMaterials(worldShotgunVisual, shotgunMetal, shotgunDark, shotgunAccent, shotgunAccentCore);
                    RemovePhysicsAndAnimators(worldShotgunVisual);
                    SetDynamicRecursively(worldShotgunMount.gameObject);
                    SetDynamicRecursively(fpsShotgunVisual);
                    var fpsVisual = InstantiateImportedVisual(fpsKickModel, "FpsKickVisual", viewmodels, new Vector3(0.12f, -0.42f, 0.30f), Quaternion.identity, Vector3.one);
                    AssignImportedMaterials(fpsVisual, characterRed, characterBlack, characterCream, characterEye);
                    RemovePhysicsComponents(fpsVisual);
                    var fpsAnimator = fpsVisual.GetComponent<Animator>();
                    if (fpsAnimator == null)
                    {
                        fpsAnimator = fpsVisual.AddComponent<Animator>();
                    }
                    fpsAnimator.runtimeAnimatorController = EnsureAnimatorController(FpsControllerPath, FpsKickModelPath);
                    fpsAnimator.avatar = FindImportedAvatar(FpsKickModelPath);
                    fpsAnimator.applyRootMotion = false;

                    BuildCrosshair(camera);

                    SetObjectReference(input, "actions", actions);
                    SetObjectReference(motor, "input", input);
                    SetFloat(motor, "bhopSoftCapMultiplier", PlayerMotorDefaults.BhopSoftCapMultiplier);
                    // Keep gameplay tuning at the approved review baseline. Presentation
                    // changes must not silently retune movement or ball control.
                    SetFloat(motor, "jumpVelocity", JumpVelocity);
                    SetInteger(motor, "jumpsToHardCap", PlayerMotorDefaults.JumpsToHardCap);
                    SetFloat(motor, "dashBurstSpeed", PlayerMotorDefaults.DashBurstSpeed);
                    SetFloat(motor, "dashDuration", PlayerMotorDefaults.DashDuration);
                    SetFloat(motor, "dashSteerRateDegrees", PlayerMotorDefaults.DashSteerRateDegrees);
                    SetFloat(motor, "dashSpeedCap", PlayerMotorDefaults.DashSpeedCap);
                    SetObjectReference(look, "input", input);
                    SetObjectReference(look, "head", head);
                    SetObjectReference(feedback, "player", motor);
                    SetObjectReference(feedback, "targetCamera", camera);
                    SetObjectReference(feedback, "viewmodels", viewmodels.gameObject);
                    SetObjectReference(feedback, "crosshairCanvas", camera.transform.Find("CrosshairCanvas").gameObject);
                    SetObjectReference(qualityRuntime, "targetCamera", camera);
                    SetObjectReference(launcher, "input", input);
                    SetObjectReference(launcher, "look", look);
                    SetObjectReference(launcher, "aimCamera", camera);
                    SetObjectReference(launcher, "spawnPoint", muzzle);
                     SetObjectReference(launcher, "projectilePrefab", rocketPrefab.GetComponent<RocketProjectile>());
                     SetFloat(launcher, "firingInterval", 0.90f);
                     SetObjectReference(shotgun, "input", input);
                     SetObjectReference(shotgun, "look", look);
                     SetObjectReference(shotgun, "aimCamera", camera);
                     SetObjectReference(shotgun, "ownerParticipant", participant);
                     SetLayerMask(shotgun, "hitMask", ~(1 << projectileLayer));
                     SetFloat(shotgun, "pelletDamage", ShotgunDamageRules.DefaultPelletDamage);
                     SetInteger(shotgun, "pelletCount", ShotgunDamageRules.DefaultPelletCount);
                     SetFloat(shotgun, "spreadAngleDegrees", ShotgunDamageRules.DefaultSpreadAngleDegrees);
                     SetFloat(shotgun, "fullDamageRange", ShotgunDamageRules.DefaultFullDamageRange);
                     SetFloat(shotgun, "mediumRange", ShotgunDamageRules.DefaultMediumRange);
                     SetFloat(shotgun, "maxRange", ShotgunDamageRules.DefaultMaxRange);
                     SetFloat(shotgun, "mediumMultiplier", ShotgunDamageRules.DefaultMediumMultiplier);
                     SetFloat(shotgun, "farMultiplier", ShotgunDamageRules.DefaultFarMultiplier);
                     SetFloat(shotgun, "pumpDelay", ShotgunDamageRules.DefaultPumpDelay);
                     SetFloat(shotgun, "ballImpulsePerPellet", ShotgunDamageRules.DefaultPerPelletBallImpulse);
                     SetFloat(shotgun, "ballImpulseCap", ShotgunDamageRules.DefaultBallImpulseCap);
                    SetObjectReference(kick, "input", input);
                    SetObjectReference(kick, "player", motor);
                    SetObjectReference(kick, "look", look);
                    SetObjectReference(kick, "aimCamera", camera);
                    SetObjectReference(kick, "ownerParticipant", participant);
                    SetFloat(kick, "dashContactStartDelay", BallKickDefaults.DashContactStartDelay);
                    SetFloat(kick, "dashContactReach", BallKickDefaults.DashContactReach);
                    SetFloat(kick, "dashContactRadiusPadding", BallKickDefaults.DashContactRadiusPadding);
                    SetFloat(kick, "cooldown", BallKickDefaults.Cooldown);
                    SetFloat(kick, "speedFraction", BallKickDefaults.SpeedFraction);
                    SetFloat(kick, "playerMomentumShare", BallKickDefaults.PlayerMomentumShare);
                    SetFloat(kick, "enemyContactDamage", BallKickDefaults.EnemyContactDamage);
                    SetFloat(kick, "enemyShoveImpulse", BallKickDefaults.EnemyShoveImpulse);
                    SetFloat(kick, "enemyDashRetention", BallKickDefaults.EnemyDashRetention);
                    SetFloat(feedback, "baseFov", 75f);
                    SetFloat(feedback, "maxFov", 84f);
                    SetFloat(feedback, "dashKickImpulse", PlayerCameraFeedback.DefaultDashKickImpulse);
                    SetFloat(feedback, "dashKickImpulseDuration", PlayerCameraFeedback.DefaultDashKickImpulseDuration);
                    SetFloat(feedback, "celebrationOrbitRadius", CelebrationOrbitRadius);
                    SetFloat(feedback, "celebrationOrbitHeight", CelebrationOrbitHeight);
                    SetFloat(feedback, "celebrationLookHeight", CelebrationLookHeight);
                     SetFloat(feedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees);
                     SetFloat(feedback, "celebrationFov", CelebrationFov);
                     SetVector3(feedback, "spectatorOffset", PlayerCameraFeedback.ExpectedSpectatorOffset);
                    SetObjectReference(presentation, "kick", kick);
                    SetObjectReference(presentation, "motor", motor);
                     SetObjectReference(presentation, "launcher", launcher);
                     SetObjectReference(presentation, "shotgun", shotgun);
                    SetObjectReference(presentation, "worldAnimator", worldAnimator);
                    SetObjectReference(presentation, "fpsKickAnimator", fpsAnimator);
                    SetObjectReference(presentation, "cameraFeedback", feedback);
                    SetObjectReference(presentation, "weaponVisual", weaponVisual.transform);
                    SetObjectReference(presentation, "fpsShotgunVisual", fpsShotgunVisual.transform);
                    SetObjectReference(presentation, "worldShotgunVisual", worldShotgunVisual.transform);
                    SetObjectReference(presentation, "gameplayCamera", camera);
                    SetObjectReference(presentation, "audioListener", camera.GetComponent<AudioListener>());
                    SetObjectReference(presentation, "participant", participant);
                    SetObjectArray(presentation, "teamTintRenderers", worldVisual.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer => !renderer.transform.IsChildOf(worldShotgunMount)).Cast<UnityEngine.Object>().ToArray());
                    SetObjectReference(presentation, "blueTeamCue", blueCue);
                    SetObjectReference(presentation, "redTeamCue", redCue);
                    SetObjectReference(presentation, "immunityShield", immunityShield);
                    SetObjectReference(presentation, "blueImmunityShield", blueImmunityShield);
                    SetObjectReference(presentation, "redImmunityShield", redImmunityShield);
                     SetObjectReference(presentation, "worldVisual", worldVisual);
                     SetObjectReference(presentation, "fpsVisual", fpsVisual);
                     SetObjectReference(presentation, "nicknameVisual", nameplate);
                     SetObjectReference(presentation, "nicknameText", nameplateText);
                     SetObjectReference(presentation, "nicknameCamera", null);
                     SetObjectReference(presentation, "localParticipant", null);
                     SetObjectReference(presentation, "match", null);
                     SetBool(presentation, "showNickname", false);
                     SetBool(presentation, "spawnCorpseOnDeath", false);
                     SetFloat(presentation, "corpseLifetime", 30f);
                    SetObjectReference(participant, "motor", motor);
                    SetObjectReference(participant, "characterController", controller);
                    SetObjectReference(participant, "input", input);
                    SetObjectReference(participant, "look", look);
                    SetObjectReference(participant, "kick", kick);
                     SetObjectReference(participant, "launcher", launcher);
                     SetObjectReference(participant, "shotgun", shotgun);
                    SetObjectReference(participant, "presentation", presentation);
                    SetObjectReference(participant, "cameraFeedback", feedback);
                    SetInteger(participant, "slotId", 0);
                    SetString(participant, "displayName", "Player");
                    SetEnum(participant, "team", "Blue");
                    SetBool(participant, "localParticipant", true);
                    SetFloat(participant, "maxHealth", ParticipantState.DefaultMaxHealth);
                    SetFloat(participant, "deathWait", MovementLabContract.LocalRespawnDelay);
                    SetFloat(participant, "immunityDuration", ParticipantState.DefaultImmunityDuration);
                    SetObjectReference(feedback, "participant", participant);
                    SetObjectReference(launcher, "ownerParticipant", participant);

                    // Bot components are part of the shared prefab contract. They remain
                    // disabled in the asset and are composed per-slot by the scene builder.
                    root.SetActive(false);
                    var botController = root.AddComponent<BotController>();
                    var botNavigator = root.AddComponent<BotNavigator>();
                    var botPerception = root.AddComponent<BotPerception>();
                    botController.enabled = false;
                    botNavigator.enabled = false;
                    botPerception.enabled = false;
                    SetObjectReference(participant, "botController", botController);
                    SetObjectReference(botController, "participant", participant);
                    root.SetActive(true);

                    // Keep collider root explicit while camera/viewmodel children remain default.
                    root.layer = participantLayer;

                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static GameObject BuildBallPrefab(Material ballMaterial, PhysicsMaterial ballSurface)
                {
                    var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    root.name = "Ball";
                    root.transform.localScale = Vector3.one * BallPrefabScale;
                    root.GetComponent<Renderer>().sharedMaterial = ballMaterial;
                    var collider = root.GetComponent<SphereCollider>();
                    collider.sharedMaterial = ballSurface;
                    var body = root.AddComponent<Rigidbody>();
                    body.mass = 1f;
                    body.useGravity = true;
                    body.isKinematic = false;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    body.linearDamping = 0.15f;
                    body.angularDamping = 0.05f;
                    var motor = root.AddComponent<BallMotor>();
                    SetObjectReference(motor, "body", body);
                    SetObjectReference(motor, "ballCollider", collider);
                    SetFloat(motor, "baseSpeedReference", 10f);
                    SetFloat(motor, "speedCapMultiplier", 4f);
                    SetFloat(motor, "rollingResistance", 1.25f);
                    SetFloat(motor, "restSpeed", 0.08f);
                    SetFloat(motor, "contactAssistStrength", 0.35f);
                    SetFloat(motor, "contactAssistImpulseCap", 5f);
                    SetFloat(motor, "meaningfulContactSpeedThreshold", 1f);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static GameObject BuildHealthPickupPrefab(Material healthMaterial)
                {
                    if (healthMaterial == null) throw new InvalidOperationException("Health pickup material is required before prefab build.");

                    var root = new GameObject("HealthPickup");
                    root.transform.localScale = Vector3.one;
                    var trigger = root.AddComponent<SphereCollider>();
                    trigger.isTrigger = true;
                    trigger.radius = MovementLabContract.HealthPickupTriggerRadius;
                    var body = root.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.constraints = RigidbodyConstraints.FreezeAll;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.interpolation = RigidbodyInterpolation.None;

                    var visualRoot = new GameObject("VisualRoot");
                    visualRoot.transform.SetParent(root.transform, false);
                    var horizontal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    horizontal.name = "BarHorizontal";
                    horizontal.transform.SetParent(visualRoot.transform, false);
                    horizontal.transform.localScale = MovementLabContract.HealthCrossHorizontalScale;
                    UnityEngine.Object.DestroyImmediate(horizontal.GetComponent<Collider>());
                    var horizontalRenderer = horizontal.GetComponent<MeshRenderer>();
                    horizontalRenderer.sharedMaterial = healthMaterial;
                    horizontalRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    horizontalRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    var vertical = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    vertical.name = "BarVertical";
                    vertical.transform.SetParent(visualRoot.transform, false);
                    vertical.transform.localScale = MovementLabContract.HealthCrossVerticalScale;
                    UnityEngine.Object.DestroyImmediate(vertical.GetComponent<Collider>());
                    var verticalRenderer = vertical.GetComponent<MeshRenderer>();
                    verticalRenderer.sharedMaterial = healthMaterial;
                    verticalRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    verticalRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    core.name = "Core";
                    core.transform.SetParent(visualRoot.transform, false);
                    core.transform.localScale = MovementLabContract.HealthCrossCoreScale;
                    core.transform.localPosition = new Vector3(0f, 0f, -0.05f);
                    UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
                    var coreRenderer = core.GetComponent<MeshRenderer>();
                    coreRenderer.sharedMaterial = healthMaterial;
                    coreRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    coreRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    var pickup = root.AddComponent<HealthPickup>();
                    SetObjectReference(pickup, "pickupTrigger", trigger);
                    SetObjectReference(pickup, "visualRoot", visualRoot);
                    SetFloat(pickup, "respawnDelay", MovementLabContract.HealthPickupRespawnDelay);
                    SetFloat(pickup, "restoreFraction", MovementLabContract.HealthPickupRestoreFraction);

                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (var i = 0; i < transforms.Length; i++) transforms[i].gameObject.isStatic = false;
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, HealthPickupPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static GameObject BuildShotgunPickupPrefab(Material shotgunMetal, Material shotgunDark,
                    Material shotgunAccent, Material shotgunAccentCore, Material teamBlueMaterial, Material teamRedMaterial)
                {
                    if (shotgunMetal == null || shotgunDark == null || shotgunAccent == null || shotgunAccentCore == null || teamBlueMaterial == null || teamRedMaterial == null)
                        throw new InvalidOperationException("Shotgun pickup materials are required before prefab build.");
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunModelPath);
                    if (model == null) throw new InvalidOperationException("Missing shotgun pickup model: " + ShotgunModelPath);

                    var root = new GameObject("ShotgunPickup");
                    root.transform.localScale = Vector3.one;
                    var trigger = root.AddComponent<SphereCollider>();
                    trigger.isTrigger = true;
                    trigger.radius = MovementLabContract.ShotgunPickupTriggerRadius;
                    var body = root.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.constraints = RigidbodyConstraints.FreezeAll;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.interpolation = RigidbodyInterpolation.None;

                    var visualRoot = new GameObject("VisualRoot");
                    visualRoot.transform.SetParent(root.transform, false);
                    var shotgunVisual = InstantiateImportedVisual(model, "ShotgunModel", visualRoot.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    AssignImportedMaterials(shotgunVisual, shotgunMetal, shotgunDark, shotgunAccent, shotgunAccentCore);
                    RemovePhysicsAndAnimators(shotgunVisual);
                    var blueCue = CreateShapeCue("BlueCircleCue", false, teamBlueMaterial, MovementLabContract.PickupCueBluePosition);
                    blueCue.transform.SetParent(visualRoot.transform, false);
                    blueCue.transform.localPosition = MovementLabContract.PickupCueBluePosition;
                    blueCue.transform.localScale = MovementLabContract.PickupCueScale;
                    var redCue = CreateShapeCue("RedTriangleCue", true, teamRedMaterial, MovementLabContract.PickupCueRedPosition);
                    redCue.transform.SetParent(visualRoot.transform, false);
                    redCue.transform.localPosition = MovementLabContract.PickupCueRedPosition;
                    redCue.transform.localScale = MovementLabContract.PickupCueScale;

                    var pickup = root.AddComponent<ShotgunPickup>();
                    SetObjectReference(pickup, "pickupTrigger", trigger);
                    SetObjectReference(pickup, "visualRoot", visualRoot);
                    SetInteger(pickup, "grant", MovementLabContract.ShotgunPickupGrant);
                    SetFloat(pickup, "respawnDelay", MovementLabContract.ShotgunPickupRespawnDelay);
                    SetDynamicRecursively(root);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, ShotgunPickupPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static GameObject BuildAmmoPickupPrefab(Material ammoShellMaterial, Material teamBlueMaterial, Material teamRedMaterial)
                {
                    if (ammoShellMaterial == null || teamBlueMaterial == null || teamRedMaterial == null)
                        throw new InvalidOperationException("Ammo pickup materials are required before prefab build.");

                    var root = new GameObject("AmmoPickup");
                    root.transform.localScale = Vector3.one;
                    var trigger = root.AddComponent<SphereCollider>();
                    trigger.isTrigger = true;
                    trigger.radius = MovementLabContract.AmmoPickupTriggerRadius;
                    var body = root.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.constraints = RigidbodyConstraints.FreezeAll;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.interpolation = RigidbodyInterpolation.None;

                    var visualRoot = new GameObject("VisualRoot");
                    visualRoot.transform.SetParent(root.transform, false);
                    var leftShell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    leftShell.name = "ShellLeft";
                    leftShell.transform.SetParent(visualRoot.transform, false);
                    leftShell.transform.localPosition = MovementLabContract.AmmoShellLeftPosition;
                    leftShell.transform.localScale = MovementLabContract.AmmoShellScale;
                    UnityEngine.Object.DestroyImmediate(leftShell.GetComponent<Collider>());
                    var leftRenderer = leftShell.GetComponent<MeshRenderer>();
                    leftRenderer.sharedMaterial = ammoShellMaterial;
                    leftRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    leftRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    var rightShell = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    rightShell.name = "ShellRight";
                    rightShell.transform.SetParent(visualRoot.transform, false);
                    rightShell.transform.localPosition = MovementLabContract.AmmoShellRightPosition;
                    rightShell.transform.localScale = MovementLabContract.AmmoShellScale;
                    UnityEngine.Object.DestroyImmediate(rightShell.GetComponent<Collider>());
                    var rightRenderer = rightShell.GetComponent<MeshRenderer>();
                    rightRenderer.sharedMaterial = ammoShellMaterial;
                    rightRenderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                    rightRenderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

                    var blueCue = CreateShapeCue("BlueCircleCue", false, teamBlueMaterial, MovementLabContract.PickupCueBluePosition);
                    blueCue.transform.SetParent(visualRoot.transform, false);
                    blueCue.transform.localPosition = MovementLabContract.PickupCueBluePosition;
                    blueCue.transform.localScale = MovementLabContract.PickupCueScale;
                    var redCue = CreateShapeCue("RedTriangleCue", true, teamRedMaterial, MovementLabContract.PickupCueRedPosition);
                    redCue.transform.SetParent(visualRoot.transform, false);
                    redCue.transform.localPosition = MovementLabContract.PickupCueRedPosition;
                    redCue.transform.localScale = MovementLabContract.PickupCueScale;

                    var pickup = root.AddComponent<AmmoPickup>();
                    SetObjectReference(pickup, "pickupTrigger", trigger);
                    SetObjectReference(pickup, "visualRoot", visualRoot);
                    SetInteger(pickup, "grant", MovementLabContract.AmmoPickupGrant);
                    SetFloat(pickup, "respawnDelay", MovementLabContract.AmmoPickupRespawnDelay);
                    SetDynamicRecursively(root);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, AmmoPickupPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static GameObject BuildRocketPrefab(Material rocketMaterial, Material rocketHotMaterial, Material projectileGlowMaterial)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(RocketModelPath);
                    if (model == null)
                    {
                        throw new InvalidOperationException("Missing rocket model: " + RocketModelPath);
                    }

                    var root = new GameObject("Rocket");
                    root.layer = EnsureGameplayLayer(MovementLabContract.ProjectilesLayerName);
                    root.transform.localScale = Vector3.one * 0.24f;
                    var collider = root.AddComponent<SphereCollider>();
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    visual.name = "Visual";
                    visual.transform.SetParent(root.transform, false);
                    var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 2)
                    {
                        throw new InvalidOperationException("Rocket model must contain exactly RocketSurface and RocketHot renderers: " + RocketModelPath);
                    }
                    var seenSurface = false;
                    var seenHot = false;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1)
                        {
                            throw new InvalidOperationException("Rocket renderer must contain one material slot: " + renderer.name);
                        }
                        if (renderer.name == "RocketSurface")
                        {
                            if (seenSurface) throw new InvalidOperationException("Duplicate RocketSurface renderer.");
                            seenSurface = true;
                            renderer.sharedMaterials = new[] { rocketMaterial };
                        }
                        else if (renderer.name == "RocketHot")
                        {
                            if (seenHot) throw new InvalidOperationException("Duplicate RocketHot renderer.");
                            seenHot = true;
                            renderer.sharedMaterials = new[] { rocketHotMaterial };
                        }
                        else
                        {
                            throw new InvalidOperationException("Unknown rocket renderer: " + renderer.name);
                        }
                    }
                    if (!seenSurface || !seenHot) throw new InvalidOperationException("Rocket model must expose RocketSurface and RocketHot renderers.");
                    var body = root.AddComponent<Rigidbody>();
                    body.useGravity = false;
                    body.isKinematic = true;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.interpolation = RigidbodyInterpolation.Interpolate;
                    var projectile = root.AddComponent<RocketProjectile>();
                    SetObjectReference(projectile, "body", body);
                    SetObjectReference(projectile, "projectileCollider", collider);
                    SetFloat(projectile, "speed", 48f);
                    SetFloat(projectile, "lifetime", 8f);

                    var glowObject = new GameObject("ProjectileGlow");
                    glowObject.transform.SetParent(root.transform, false);
                    glowObject.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                    var glowSystem = glowObject.AddComponent<ParticleSystem>();
                    var glowMain = glowSystem.main;
                    glowMain.loop = true;
                    glowMain.playOnAwake = true;
                    glowMain.prewarm = true;
                    glowMain.duration = 0.22f;
                    glowMain.simulationSpace = ParticleSystemSimulationSpace.Local;
                    glowMain.startLifetime = 0.22f;
                    glowMain.startSpeed = 0f;
                    glowMain.gravityModifier = 0f;
                    glowMain.startSize = 0.85f;
                    glowMain.startColor = Color.white;
                    glowMain.maxParticles = 2;
                    var glowEmission = glowSystem.emission;
                    glowEmission.rateOverTime = 10f;
                    glowEmission.rateOverDistance = 0f;
                    var glowShape = glowSystem.shape;
                    glowShape.enabled = false;
                    glowSystem.useAutoRandomSeed = false;
                    glowSystem.randomSeed = 0xC0FFEEu;
                    var glowRenderer = glowObject.GetComponent<ParticleSystemRenderer>();
                    glowRenderer.material = projectileGlowMaterial;
                    glowRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                    glowRenderer.alignment = ParticleSystemRenderSpace.View;

                    var smokeMaterial = GetOrCreateParticleMaterial("Smoke", new Color(0.36f, 0.34f, 0.31f, 0.78f), AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath));
                    var smokeTrail = new GameObject("SmokeTrail");
                    smokeTrail.transform.SetParent(root.transform, false);
                    smokeTrail.transform.localPosition = new Vector3(0f, 0f, -0.16f);
                    var smokeSystem = smokeTrail.AddComponent<ParticleSystem>();
                    var smokeMain = smokeSystem.main;
                    smokeMain.loop = true;
                    smokeMain.simulationSpace = ParticleSystemSimulationSpace.World;
                    smokeMain.startLifetime = RocketTrailLifetime;
                    smokeMain.startSpeed = 0f;
                    smokeMain.startSize = RocketTrailStartSize;
                    smokeMain.startColor = RocketTrailStartColor;
                    smokeMain.gravityModifier = 0f;
                    smokeMain.maxParticles = 48;
                    var smokeEmission = smokeSystem.emission;
                    smokeEmission.rateOverTime = 0f;
                    smokeEmission.rateOverDistance = RocketTrailRateOverDistance;
                    var smokeSize = smokeSystem.sizeOverLifetime;
                    smokeSize.enabled = true;
                    var smokeCurve = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1.25f)));
                    smokeSize.size = smokeCurve;
                    var smokeColor = smokeSystem.colorOverLifetime;
                    smokeColor.enabled = true;
                    var smokeGradient = new Gradient();
                    smokeGradient.SetKeys(new[] { new GradientColorKey(RocketTrailStartColor, 0f), new GradientColorKey(RocketTrailEndColor, 1f) }, new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                    smokeColor.color = smokeGradient;
                    var smokeRenderer = smokeTrail.GetComponent<ParticleSystemRenderer>();
                    smokeRenderer.material = smokeMaterial;
                    smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                    var smokeSheet = smokeSystem.textureSheetAnimation;
                    smokeSheet.enabled = true;
                    smokeSheet.numTilesX = 4;
                    smokeSheet.numTilesY = 4;
                    smokeSheet.animation = ParticleSystemAnimationType.WholeSheet;
                    smokeSheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
                     var trailVfx = smokeTrail.AddComponent<RocketTrailVfx>();
                     SetObjectArray(trailVfx, "particleSystems", new UnityEngine.Object[] { smokeSystem });
                     SetObjectReference(trailVfx, "projectileGlow", glowSystem);
                    var blueTrailMaterial = GetOrCreateParticleMaterial("TeamBlueTrail", new Color(0.08f, 0.35f, 1f, 1f), LoadTexture(RocketGlowTexturePath));
                    var redTrailMaterial = GetOrCreateParticleMaterial("TeamRedTrail", new Color(1f, 0.12f, 0.1f, 1f), LoadTexture(RocketGlowTexturePath));
                    var blueAccent = CreateShapeCue("BlueImpactRing", false, blueTrailMaterial, new Vector3(0f, 0f, 0.16f));
                    blueAccent.transform.SetParent(smokeTrail.transform, false);
                    blueAccent.transform.localScale = Vector3.one * 0.55f;
                    blueAccent.SetActive(false);
                    var redAccent = CreateShapeCue("RedImpactTriangle", true, redTrailMaterial, new Vector3(0f, 0f, 0.16f));
                    redAccent.transform.SetParent(smokeTrail.transform, false);
                    redAccent.transform.localScale = Vector3.one * 0.55f;
                    redAccent.SetActive(false);
                    SetObjectReference(trailVfx, "blueImpactAccent", blueAccent);
                    SetObjectReference(trailVfx, "redImpactAccent", redAccent);
                     SetObjectReference(trailVfx, "blueTrailMaterial", blueTrailMaterial);
                     SetObjectReference(trailVfx, "redTrailMaterial", redTrailMaterial);
                     SetObjectReference(trailVfx, "neutralTrailMaterial", projectileGlowMaterial);
                    SetObjectReference(projectile, "trailVfx", trailVfx);
                    PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    PersistRocketHierarchyReferences();
                    return AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath);
                }

                private static void PersistRocketHierarchyReferences()
                {
                    var root = PrefabUtility.LoadPrefabContents(RocketPrefabPath);
                    try
                    {
                        var projectile = Require(root.GetComponent<RocketProjectile>(), "Rocket prefab RocketProjectile");
                        var trail = Require(root.GetComponentInChildren<RocketTrailVfx>(true), "Rocket prefab RocketTrailVfx");
                        var system = Require(trail.GetComponent<ParticleSystem>(), "Rocket prefab SmokeTrail ParticleSystem");
                        var blueAccent = Require(trail.transform.Find("BlueImpactRing"), "Rocket prefab BlueImpactRing");
                        var redAccent = Require(trail.transform.Find("RedImpactTriangle"), "Rocket prefab RedImpactTriangle");
                        SetObjectArray(trail, "particleSystems", new UnityEngine.Object[] { system });
                        SetObjectReference(trail, "projectileGlow", root.transform.Find("ProjectileGlow")?.GetComponent<ParticleSystem>());
                        SetObjectReference(trail, "blueImpactAccent", blueAccent.gameObject);
                        SetObjectReference(trail, "redImpactAccent", redAccent.gameObject);
                        SetObjectReference(trail, "neutralTrailMaterial", AssetDatabase.LoadAssetAtPath<Material>(ProjectileGlowMaterialPath));
                        SetObjectReference(projectile, "trailVfx", trail);
                        PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }

                internal static ExplosionVfx BuildExplosionVfxPrefab()
                {
                    var root = new GameObject("ExplosionVfx");
                    // Runtime scales each one-shot from the authored 4.5-unit reference.
                    root.transform.localScale = Vector3.one;
                    var explosionMaterial = GetOrCreateParticleMaterial("Explosion", ExplosionFireMaterialColor, LoadTexture(ExplosionTexturePath));
                    var explosionFlashMaterial = GetOrCreateAdditiveParticleMaterial("ExplosionAdditive", Color.white, LoadTexture(ExplosionTexturePath), 3.0f);
                    var explosionSparksMaterial = GetOrCreateAdditiveParticleMaterial("ExplosionSparks", Color.white, LoadTexture(ExplosionTexturePath), 2.0f);
                    var smokeMaterial = GetOrCreateParticleMaterial("Smoke", ExplosionSmokeMaterialColor, LoadTexture(SmokeTexturePath));
                    var systems = new List<ParticleSystem>();

                    var flash = CreateExplosionSystem(root.transform, "Flash", explosionFlashMaterial, 1, 0.13f, 0.13f, 2.8f, 0f, 1, 0f, 0f);
                    ConfigureExplosionIdentity(flash, 0xF001u, 1);
                    ConfigureExplosionGradient(flash, new[] { new GradientColorKey(new Color(1f, 1f, 0.78f, 1f), 0f), new GradientColorKey(new Color(1f, 0.78f, 0.12f, 1f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    ConfigureExplosionSize(flash, 0.75f, 1.25f);
                    systems.Add(flash);
                    var fire = CreateExplosionSystem(root.transform, "FireballBody", explosionMaterial, 20, 0.40f, 0.56f, 1.35f, 1f, 20, 0.65f, 2.10f);
                    var fireShape = fire.shape;
                    fireShape.radius = 0.06f;
                    ConfigureExplosionIdentity(fire, 0xF002u, 0);
                    ConfigureExplosionGradient(fire, new[] { new GradientColorKey(new Color(1f, 1f, 0.82f, 1f), 0f), new GradientColorKey(new Color(1f, 0.88f, 0.16f, 1f), 0.32f), new GradientColorKey(new Color(1f, 0.62f, 0.04f, 1f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.95f, 0.65f), new GradientAlphaKey(0f, 1f) });
                    ConfigureExplosionSize(fire, 0.70f, 0.75f, 1.18f);
                    systems.Add(fire);
                    var sparks = CreateExplosionSystem(root.transform, "Sparks", explosionSparksMaterial, 10, 0.20f, 0.32f, 0.10f, 1f, 10, 7f, 12f);
                    ConfigureExplosionIdentity(sparks, 0xF003u, 2);
                    ConfigureExplosionGradient(sparks, new[] { new GradientColorKey(new Color(1f, 1f, 0.78f, 1f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.04f, 1f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    systems.Add(sparks);
                    var smoke = CreateExplosionSystem(root.transform, "Smoke", smokeMaterial, 6, 0.62f, 0.86f, 0.82f, 1f, 6, 0.5f, 1.6f);
                    var smokeEmission = smoke.emission;
                    smokeEmission.SetBursts(new[] { new ParticleSystem.Burst(0.10f, (short)6) });
                    ConfigureExplosionIdentity(smoke, 0xF004u, -1);
                    ConfigureExplosionGradient(smoke, new[] { new GradientColorKey(new Color(0.52f, 0.49f, 0.44f, 1f), 0f), new GradientColorKey(new Color(0.20f, 0.19f, 0.18f, 1f), 1f) }, new[] { new GradientAlphaKey(0.30f, 0f), new GradientAlphaKey(0f, 1f) });
                    ConfigureExplosionSize(smoke, 0.55f, 1.40f);
                    systems.Add(smoke);
                    var blastRadiusCue = CreateExplosionSystem(root.transform, "BlastRadiusCue", explosionMaterial, 1, 0.28f, 0.28f, 9f, 0f, 1, 0f, 0f);
                    blastRadiusCue.transform.localPosition = Vector3.zero;
                    blastRadiusCue.transform.localRotation = Quaternion.identity;
                    blastRadiusCue.transform.localScale = Vector3.one;
                    var cueMain = blastRadiusCue.main;
                    cueMain.simulationSpace = ParticleSystemSimulationSpace.Local;
                    cueMain.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    var cueShape = blastRadiusCue.shape;
                    cueShape.enabled = false;
                    var cueRenderer = blastRadiusCue.GetComponent<ParticleSystemRenderer>();
                    cueRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                    cueRenderer.alignment = ParticleSystemRenderSpace.World;
                    cueRenderer.sharedMaterial = explosionMaterial;
                    ConfigureExplosionIdentity(blastRadiusCue, 0xF005u, 3);
                    ConfigureExplosionGradient(blastRadiusCue,
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0.22f, 0f), new GradientAlphaKey(0f, 1f) });
                    ConfigureExplosionSize(blastRadiusCue, 0.15f, 1f);
                    systems.Add(blastRadiusCue);

                    var effect = root.AddComponent<ExplosionVfx>();
                    SetObjectArray(effect, "particleSystems", systems.ToArray());
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab.GetComponent<ExplosionVfx>();
                }

                internal static void ConfigureExplosionIdentity(ParticleSystem system, uint seed, int sortingOrder)
                {
                    system.useAutoRandomSeed = false;
                    system.randomSeed = seed;
                    system.GetComponent<ParticleSystemRenderer>().sortingOrder = sortingOrder;
                }

                internal static void ConfigureExplosionGradient(ParticleSystem system, GradientColorKey[] colors, GradientAlphaKey[] alpha)
                {
                    var color = system.colorOverLifetime;
                    color.enabled = true;
                    var gradient = new Gradient();
                    gradient.SetKeys(colors, alpha);
                    color.color = gradient;
                }

                internal static void ConfigureExplosionSize(ParticleSystem system, float first, float last, float middle = -1f)
                {
                    var size = system.sizeOverLifetime;
                    size.enabled = true;
                    if (middle > 0f)
                    {
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, first), new Keyframe(0.18f, 1f), new Keyframe(0.65f, middle), new Keyframe(1f, last)));
                    }
                    else
                    {
                        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, first), new Keyframe(1f, last)));
                    }
                }

                internal static ParticleSystem CreateExplosionSystem(Transform parent, string name, Material material, int burstCount, float minLifetime, float maxLifetime, float size, float speed, int maxParticles, float minSpeed, float maxSpeed)
                {
                    var child = new GameObject(name);
                    child.transform.SetParent(parent, false);
                    var system = child.AddComponent<ParticleSystem>();
                    var main = system.main;
                    main.loop = false;
                    main.playOnAwake = false;
                    main.duration = 0.01f;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
                    main.startSpeed = speed > 0f ? new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed) : 0f;
                    main.startSize = size;
                    main.startColor = Color.white;
                    main.maxParticles = Mathf.Max(maxParticles, burstCount);
                    var emission = system.emission;
                    emission.enabled = true;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });
                    var shape = system.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    shape.radius = 0.02f;
                    var renderer = child.GetComponent<ParticleSystemRenderer>();
                    renderer.material = material;
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    var sheet = system.textureSheetAnimation;
                    sheet.enabled = true;
                    sheet.numTilesX = 4;
                    sheet.numTilesY = 4;
                    sheet.animation = ParticleSystemAnimationType.WholeSheet;
                    sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
                    return system;
                }

                internal static GameObject InstantiateImportedVisual(GameObject source, string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
                {
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                    visual.name = name;
                    visual.transform.SetParent(parent, false);
                    visual.transform.localPosition = localPosition;
                    visual.transform.localRotation = localRotation;
                    visual.transform.localScale = localScale;
                    return visual;
                }

                internal static void AssignImportedMaterials(GameObject visual, params Material[] materials)
                {
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length == 0)
                    {
                        throw new InvalidOperationException("Imported visual has no renderers: " + visual.name);
                    }
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var slots = renderer.sharedMaterials;
                        if (slots == null || slots.Length == 0) slots = new Material[1];
                        for (var j = 0; j < slots.Length; j++)
                        {
                            var weaponGroup = GetWeaponRendererGroup(renderer.name);
                            var materialIndex = weaponGroup == null ? GetCharacterMaterialIndex(renderer.name) : GetWeaponMaterialIndex(weaponGroup);
                            if (materialIndex < 0)
                                throw new InvalidOperationException("Imported renderer has unknown material group: " + renderer.name + ".");
                            if (materialIndex >= materials.Length || materials[materialIndex] == null)
                                throw new InvalidOperationException("Imported renderer material group is not configured: " + renderer.name + ".");
                            slots[j] = materials[materialIndex];
                        }
                        renderer.sharedMaterials = slots;
                    }
                }

                internal static void RemovePhysicsComponents(GameObject visual)
                {
                    var colliders = visual.GetComponentsInChildren<Collider>(true);
                    for (var i = 0; i < colliders.Length; i++) UnityEngine.Object.DestroyImmediate(colliders[i]);
                    var bodies = visual.GetComponentsInChildren<Rigidbody>(true);
                    for (var i = 0; i < bodies.Length; i++) UnityEngine.Object.DestroyImmediate(bodies[i]);
                }

                internal static void RemovePhysicsAndAnimators(GameObject visual)
                {
                    RemovePhysicsComponents(visual);
                    var animators = visual.GetComponentsInChildren<Animator>(true);
                    for (var i = 0; i < animators.Length; i++) UnityEngine.Object.DestroyImmediate(animators[i]);
                }

                internal static Renderer FindRendererByName(GameObject root, string name)
                {
                    if (root == null) throw new InvalidOperationException("Unable to find imported renderer '" + name + "' under a null root.");
                    var matches = root.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer => renderer != null && string.Equals(renderer.name, name, StringComparison.Ordinal))
                        .ToArray();
                    if (matches.Length != 1)
                    {
                        throw new InvalidOperationException("Expected exactly one imported renderer named '" + name + "' under " + root.name + ", found " + matches.Length + ".");
                    }
                    return matches[0];
                }

                internal static void SetDynamicRecursively(GameObject root)
                {
                    if (root == null) return;
                    root.isStatic = false;
                    for (var i = 0; i < root.transform.childCount; i++)
                    {
                        SetDynamicRecursively(root.transform.GetChild(i).gameObject);
                    }
                }

                internal static Transform FindNamedTransform(Transform root, string name)
                {
                    if (root == null) return null;
                    if (root.name == name) return root;
                    for (var i = 0; i < root.childCount; i++)
                    {
                        var found = FindNamedTransform(root.GetChild(i), name);
                        if (found != null) return found;
                    }
                    return null;
                }

                internal static void BuildCrosshair(Camera camera)
                {
                    var existing = camera.transform.Find("CrosshairCanvas");
                    if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                    var canvasObject = new GameObject("CrosshairCanvas");
                    canvasObject.transform.SetParent(camera.transform, false);
                    var canvas = canvasObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 0.10f;
                    canvas.sortingOrder = 100;
                    var scaler = canvasObject.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);
                    scaler.matchWidthOrHeight = 0.5f;

                    var dark = new Color(0.05f, 0.03f, 0.04f, 0.85f);
                    var ivory = new Color(0.95f, 0.88f, 0.72f, 1f);
                    var red = new Color(0.85f, 0.08f, 0.12f, 1f);
                    AddCrosshairImage(canvasObject.transform, "DarkTop", dark, new Vector2(14f, 4f), new Vector2(0.5f, 0.5f), new Vector2(0f, 7f));
                    AddCrosshairImage(canvasObject.transform, "DarkBottom", dark, new Vector2(14f, 4f), new Vector2(0.5f, 0.5f), new Vector2(0f, -7f));
                    AddCrosshairImage(canvasObject.transform, "DarkLeft", dark, new Vector2(4f, 14f), new Vector2(0.5f, 0.5f), new Vector2(-7f, 0f));
                    AddCrosshairImage(canvasObject.transform, "DarkRight", dark, new Vector2(4f, 14f), new Vector2(0.5f, 0.5f), new Vector2(7f, 0f));
                    AddCrosshairImage(canvasObject.transform, "IvoryTop", ivory, new Vector2(10f, 2f), new Vector2(0.5f, 0.5f), new Vector2(0f, 7f));
                    AddCrosshairImage(canvasObject.transform, "IvoryBottom", ivory, new Vector2(10f, 2f), new Vector2(0.5f, 0.5f), new Vector2(0f, -7f));
                    AddCrosshairImage(canvasObject.transform, "IvoryLeft", ivory, new Vector2(2f, 10f), new Vector2(0.5f, 0.5f), new Vector2(-7f, 0f));
                    AddCrosshairImage(canvasObject.transform, "IvoryRight", ivory, new Vector2(2f, 10f), new Vector2(0.5f, 0.5f), new Vector2(7f, 0f));
                    AddCrosshairImage(canvasObject.transform, "DarkCenter", dark, new Vector2(5f, 5f), new Vector2(0.5f, 0.5f), Vector2.zero);
                    AddCrosshairImage(canvasObject.transform, "RedDot", red, new Vector2(3f, 3f), new Vector2(0.5f, 0.5f), Vector2.zero);
                }

                internal static void AddCrosshairImage(Transform parent, string name, Color color, Vector2 size, Vector2 pivot, Vector2 position)
                {
                    var imageObject = new GameObject(name);
                    imageObject.transform.SetParent(parent, false);
                    var rect = imageObject.AddComponent<RectTransform>();
                    rect.anchorMin = Vector2.one * 0.5f;
                    rect.anchorMax = Vector2.one * 0.5f;
                    rect.pivot = pivot;
                    rect.sizeDelta = size;
                    rect.anchoredPosition = position;
                    var image = imageObject.AddComponent<Image>();
                    image.color = color;
                    image.raycastTarget = false;
                }

                internal static int EnsureLocalPlayerHiddenLayer()
                {
                    return EnsureGameplayLayer(MovementLabContract.LocalPlayerHiddenLayerName);
                }

                internal static int EnsureGameplayLayer(string layerName)
                {
                    if (string.IsNullOrEmpty(layerName)) throw new InvalidOperationException("Gameplay layer name is empty.");
                    var layer = LayerMask.NameToLayer(layerName);
                    if (layer >= 0) return layer;
                    var settings = AssetDatabase.LoadAllAssetsAtPath(MovementLabContract.TagManagerPath);
                    if (settings.Length == 0) throw new InvalidOperationException("TagManager.asset unavailable.");
                    var serialized = new SerializedObject(settings[0]);
                    var layers = serialized.FindProperty("layers");
                    for (var i = 8; i < layers.arraySize; i++)
                    {
                        var item = layers.GetArrayElementAtIndex(i);
                        if (string.IsNullOrEmpty(item.stringValue))
                        {
                            item.stringValue = layerName;
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            AssetDatabase.SaveAssets();
                            return i;
                        }
                    }
                    throw new InvalidOperationException("No free user layer for " + layerName + ".");
                }

                internal static GameObject CreateShapeCue(string name, bool triangle, Material material, Vector3 localPosition)
                {
                    var cue = new GameObject(name);
                    cue.transform.localPosition = localPosition;
                    var filter = cue.AddComponent<MeshFilter>();
                    var renderer = cue.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    filter.sharedMesh = GetOrCreateShapeMesh(triangle);
                    cue.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    cue.transform.localScale = triangle ? Vector3.one : new Vector3(0.42f, 0.42f, 1f);
                    return cue;
                }

                private static GameObject CreateImmunityShieldVfx(string name, Transform parent, Material material)
                {
                    var shield = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    shield.name = name;
                    shield.transform.SetParent(parent, false);
                    shield.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                    shield.transform.localScale = new Vector3(1.2f, 2.0f, 1.2f);
                    var mesh = Require(shield.GetComponent<MeshFilter>(), name + " MeshFilter").sharedMesh;
                    UnityEngine.Object.DestroyImmediate(shield.GetComponent<Collider>());
                    UnityEngine.Object.DestroyImmediate(shield.GetComponent<MeshRenderer>());
                    UnityEngine.Object.DestroyImmediate(shield.GetComponent<MeshFilter>());

                    var system = shield.AddComponent<ParticleSystem>();
                    var main = system.main;
                    main.loop = true;
                    main.playOnAwake = true;
                    main.duration = 1f;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.startLifetime = 1.05f;
                    main.startSpeed = 0f;
                    main.startSize = 1f;
                    main.maxParticles = 2;
                    var emission = system.emission;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
                    var shape = system.shape;
                    shape.enabled = false;
                    var renderer = shield.GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode = ParticleSystemRenderMode.Mesh;
                    renderer.mesh = mesh;
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    shield.SetActive(false);
                    return shield;
                }

                internal static Mesh GetOrCreateShapeMesh(bool triangle)
                {
                    var path = triangle ? RedTriangleCueMeshPath : BlueCircleCueMeshPath;
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh != null) return mesh;
                    mesh = new Mesh { name = triangle ? "RedTriangleCueMesh" : "BlueCircleCueMesh" };
                    if (triangle)
                    {
                        mesh.vertices = new[] { new Vector3(0f, 0.32f, 0f), new Vector3(-0.32f, -0.24f, 0f), new Vector3(0.32f, -0.24f, 0f) };
                        mesh.uv = new[] { new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
                        mesh.triangles = new[] { 0, 1, 2 };
                    }
                    else
                    {
                        const int segments = 16;
                        var vertices = new Vector3[segments + 1];
                        var uv = new Vector2[segments + 1];
                        var triangles = new int[segments * 3];
                        vertices[0] = Vector3.zero;
                        uv[0] = new Vector2(0.5f, 0.5f);
                        for (var i = 0; i < segments; i++)
                        {
                            var angle = (float)i / segments * Mathf.PI * 2f;
                            vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
                            uv[i + 1] = new Vector2(vertices[i + 1].x + 0.5f, vertices[i + 1].y + 0.5f);
                            triangles[i * 3] = 0;
                            triangles[(i * 3) + 1] = i + 1;
                            triangles[(i * 3) + 2] = (i + 1) % segments + 1;
                        }
                        mesh.vertices = vertices;
                        mesh.uv = uv;
                        mesh.triangles = triangles;
                    }
                    mesh.RecalculateNormals();
                    AssetDatabase.CreateAsset(mesh, path);
                    AssetDatabase.SaveAssets();
                    return mesh;
                }

                internal static void SetString(UnityEngine.Object target, string propertyName, string value)
                {
                    var serialized = new SerializedObject(target);
                    var property = serialized.FindProperty(propertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.String) throw new InvalidOperationException(target.GetType().Name + " has no serialized string '" + propertyName + "'.");
                    property.stringValue = value ?? string.Empty;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                internal static void SetBool(UnityEngine.Object target, string propertyName, bool value)
                {
                    var serialized = new SerializedObject(target);
                    var property = serialized.FindProperty(propertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.Boolean) throw new InvalidOperationException(target.GetType().Name + " has no serialized bool '" + propertyName + "'.");
                    property.boolValue = value;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                internal static void SetLayerMask(UnityEngine.Object target, string propertyName, int value)
                {
                    var serialized = new SerializedObject(target);
                    var property = serialized.FindProperty(propertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.LayerMask) throw new InvalidOperationException(target.GetType().Name + " has no serialized layer mask '" + propertyName + "'.");
                    property.intValue = value;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                internal static void SetLayerRecursively(GameObject root, int layer)
                {
                    root.layer = layer;
                    for (var i = 0; i < root.transform.childCount; i++) SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
                }

                internal static void ValidateLayerRecursively(GameObject root, int expectedLayer, string label)
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (var i = 0; i < transforms.Length; i++)
                    {
                        var item = transforms[i].gameObject;
                        if (item.layer != expectedLayer)
                        {
                            throw new InvalidOperationException(label + " hierarchy must use LocalPlayerHidden: " + item.name);
                        }
                    }
                }

                internal static void ValidateLayerExcluded(GameObject root, int excludedLayer, string label)
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (var i = 0; i < transforms.Length; i++)
                    {
                        var item = transforms[i].gameObject;
                        if (item.layer == excludedLayer)
                        {
                            throw new InvalidOperationException(label + " hierarchy must remain visible to the player camera: " + item.name);
                        }
                    }
                }

                internal static void ValidatePrefab(string path, string expectedName, bool dynamicBody, PhysicsMaterial ballSurface)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        if (root == null || root.name != expectedName)
                        {
                            throw new InvalidOperationException("Prefab root mismatch: " + path);
                        }
                        if (root.GetComponentsInChildren<Transform>(true).Length == 0)
                        {
                            throw new InvalidOperationException("Prefab has no hierarchy: " + path);
                        }
                        if (path == PrefabPath)
                        {
                            var controller = Require(root.GetComponent<CharacterController>(), "Player prefab CharacterController");
                            if (Vector3.Distance(root.transform.localScale, Vector3.one) > 0.001f ||
                                Mathf.Abs(controller.radius - PlayerControllerRadius) > 0.001f ||
                                Mathf.Abs(controller.height - PlayerControllerHeight) > 0.001f ||
                                Vector3.Distance(controller.center, PlayerControllerCenter) > 0.001f ||
                                Mathf.Abs(controller.skinWidth - PlayerControllerSkinWidth) > 0.001f)
                                throw new InvalidOperationException("Player prefab root/CharacterController scale contract invalid.");
                            if (root.layer != LayerMask.NameToLayer("Participants")) throw new InvalidOperationException("Player prefab root must use Participants layer.");
                            var input = Require(root.GetComponent<PlayerInputReader>(), "Player prefab PlayerInputReader");
                            var prefabMotor = Require(root.GetComponent<PlayerMotor>(), "Player prefab PlayerMotor");
                            ValidateReference(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath), "PlayerInputReader.actions");
                            var prefabLook = Require(root.GetComponent<PlayerLook>(), "Player prefab PlayerLook");
                             var prefabLauncher = Require(root.GetComponent<RocketLauncher>(), "Player prefab RocketLauncher");
                             var prefabShotgun = Require(root.GetComponent<ShotgunWeapon>(), "Player prefab ShotgunWeapon");
                            var prefabKick = Require(root.GetComponent<BallKick>(), "Player prefab BallKick");
                            var prefabFeedback = Require(root.GetComponent<PlayerCameraFeedback>(), "Player prefab PlayerCameraFeedback");
                            var prefabQualityRuntime = Require(root.transform.Find("Head/Camera").GetComponent<GraphicsQualityRuntime>(), "Player prefab GraphicsQualityRuntime");
                            var prefabPresentation = Require(root.GetComponent<PlayerPresentation>(), "Player prefab PlayerPresentation");
                            var prefabParticipant = Require(root.GetComponent<ParticipantState>(), "Player prefab ParticipantState");
                            ValidateReference(prefabLauncher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "Player prefab RocketLauncher.projectilePrefab");
                            ValidateReference(prefabLauncher, "spawnPoint", root.transform.Find("Head/Camera/RocketMuzzle"), "Player prefab RocketLauncher.spawnPoint");
                            ValidateReference(prefabMotor, "input", input, "Player prefab PlayerMotor.input");
                            ValidateReference(prefabLook, "input", input, "Player prefab PlayerLook.input");
                            ValidateReference(prefabLook, "head", root.transform.Find("Head"), "Player prefab PlayerLook.head");
                            ValidateReference(prefabFeedback, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab PlayerCameraFeedback.targetCamera");
                            ValidateReference(prefabFeedback, "viewmodels", root.transform.Find("Head/Camera/Viewmodels").gameObject, "Player prefab PlayerCameraFeedback.viewmodels");
                            ValidateReference(prefabFeedback, "crosshairCanvas", root.transform.Find("Head/Camera/CrosshairCanvas").gameObject, "Player prefab PlayerCameraFeedback.crosshairCanvas");
                            ValidateReference(prefabQualityRuntime, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab GraphicsQualityRuntime.targetCamera");
                            ValidateReference(prefabKick, "input", input, "Player prefab BallKick.input");
                            ValidateReference(prefabKick, "player", prefabMotor, "Player prefab BallKick.player");
                            ValidateReference(prefabKick, "look", prefabLook, "Player prefab BallKick.look");
                            ValidateReference(prefabKick, "aimCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab BallKick.aimCamera");
                            ValidateReference(prefabKick, "ownerParticipant", prefabParticipant, "Player prefab BallKick.ownerParticipant");
                             ValidateReference(prefabPresentation, "kick", prefabKick, "Player prefab PlayerPresentation.kick");
                             ValidateReference(prefabPresentation, "shotgun", prefabShotgun, "Player prefab PlayerPresentation.shotgun");
                            ValidateReference(prefabPresentation, "cameraFeedback", prefabFeedback, "Player prefab PlayerPresentation.cameraFeedback");
                             ValidateReference(prefabPresentation, "participant", prefabParticipant, "Player prefab PlayerPresentation.participant");
                             ValidateReference(prefabPresentation, "gameplayCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab PlayerPresentation.gameplayCamera");
                             ValidateReference(prefabPresentation, "audioListener", root.transform.Find("Head/Camera").GetComponent<AudioListener>(), "Player prefab PlayerPresentation.audioListener");
                             ValidateReference(prefabPresentation, "fpsShotgunVisual", root.transform.Find("Head/Camera/Viewmodels/FpsShotgunVisual"), "Player prefab PlayerPresentation.fpsShotgunVisual");
                             var prefabWorldVisual = Require(root.transform.Find("WorldVisual"), "Player prefab WorldVisual");
                              if (Vector3.Distance(prefabWorldVisual.localScale, Vector3.one * WorldVisualScale) > 0.001f)
                                  throw new InvalidOperationException("Player prefab WorldVisual scale must be doubled.");
                             var prefabWorldShotgunMount = FindNamedTransform(prefabWorldVisual, "WorldShotgunMount");
                             if (prefabWorldShotgunMount == null) throw new InvalidOperationException("Player prefab WorldShotgunMount is missing.");
                             var prefabWorldShotgunVisualReference = FindNamedTransform(prefabWorldShotgunMount, "WorldShotgunVisual");
                             if (prefabWorldShotgunVisualReference == null) throw new InvalidOperationException("Player prefab WorldShotgunVisual is missing.");
                             ValidateReference(prefabPresentation, "worldShotgunVisual", prefabWorldShotgunVisualReference, "Player prefab PlayerPresentation.worldShotgunVisual");
                            ValidateReference(prefabParticipant, "motor", prefabMotor, "Player prefab ParticipantState.motor");
                            ValidateReference(prefabParticipant, "characterController", controller, "Player prefab ParticipantState.characterController");
                             ValidateReference(prefabParticipant, "presentation", prefabPresentation, "Player prefab ParticipantState.presentation");
                             ValidateReference(prefabParticipant, "cameraFeedback", prefabFeedback, "Player prefab ParticipantState.cameraFeedback");
                             ValidateReference(prefabParticipant, "shotgun", prefabShotgun, "Player prefab ParticipantState.shotgun");
                             ValidateReference(prefabLauncher, "ownerParticipant", prefabParticipant, "Player prefab RocketLauncher.ownerParticipant");
                             ValidateReference(prefabShotgun, "input", input, "Player prefab ShotgunWeapon.input");
                             ValidateReference(prefabShotgun, "look", prefabLook, "Player prefab ShotgunWeapon.look");
                             ValidateReference(prefabShotgun, "aimCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab ShotgunWeapon.aimCamera");
                             ValidateReference(prefabShotgun, "ownerParticipant", prefabParticipant, "Player prefab ShotgunWeapon.ownerParticipant");
                             var projectileLayer = LayerMask.NameToLayer(MovementLabContract.ProjectilesLayerName);
                             if (projectileLayer < 0 || (prefabShotgun.HitMask.value & (1 << projectileLayer)) != 0)
                                 throw new InvalidOperationException("Player prefab ShotgunWeapon.hitMask must exclude Projectiles.");
                             ValidateReference(prefabFeedback, "participant", prefabParticipant, "Player prefab PlayerCameraFeedback.participant");
                             var prefabNameplate = Require(root.transform.Find("Nameplate"), "Player prefab Nameplate");
                             var prefabNameplateText = Require(prefabNameplate.GetComponent<TextMesh>(), "Player prefab Nameplate TextMesh");
                             if (Vector3.Distance(prefabNameplate.localPosition, new Vector3(0f, NameplateHeight, 0f)) > 0.001f ||
                                 prefabNameplate.GetComponentsInChildren<Collider>(true).Length != 0)
                                 throw new InvalidOperationException("Player prefab Nameplate must be collider-free at the authored height.");
                             ValidateReference(prefabPresentation, "nicknameVisual", prefabNameplate.gameObject, "Player prefab PlayerPresentation.nicknameVisual");
                             ValidateReference(prefabPresentation, "nicknameText", prefabNameplateText, "Player prefab PlayerPresentation.nicknameText");
                             ValidateNullReference(prefabPresentation, "nicknameCamera", "Player prefab PlayerPresentation.nicknameCamera");
                             ValidateNullReference(prefabPresentation, "localParticipant", "Player prefab PlayerPresentation.localParticipant");
                             ValidateNullReference(prefabPresentation, "match", "Player prefab PlayerPresentation.match");
                             ValidateSerializedBool(prefabPresentation, "showNickname", false, "Player prefab PlayerPresentation.showNickname");
                             ValidateSerializedBool(prefabPresentation, "spawnCorpseOnDeath", false, "Player prefab PlayerPresentation.spawnCorpseOnDeath");
                             ValidateSerializedFloat(prefabPresentation, "corpseLifetime", 30f, "Player prefab PlayerPresentation.corpseLifetime");
                            ValidateSerializedInteger(prefabParticipant, "slotId", 0, "Player prefab ParticipantState.slotId");
                            ValidateSerializedFloat(prefabParticipant, "maxHealth", ParticipantState.DefaultMaxHealth, "Player prefab ParticipantState.maxHealth");
                             ValidateSerializedFloat(prefabParticipant, "deathWait", MovementLabContract.LocalRespawnDelay, "Player prefab ParticipantState.deathWait");
                             ValidateSerializedFloat(prefabParticipant, "immunityDuration", ParticipantState.DefaultImmunityDuration, "Player prefab ParticipantState.immunityDuration");
                             ValidateSerializedInteger(prefabParticipant, "shotgunShellCapacity", ParticipantState.DefaultShotgunShellCapacity, "Player prefab ParticipantState.shotgunShellCapacity");
                             ValidateSerializedFloat(prefabShotgun, "pelletDamage", ShotgunDamageRules.DefaultPelletDamage, "Player prefab ShotgunWeapon.pelletDamage");
                             ValidateSerializedInteger(prefabShotgun, "pelletCount", ShotgunDamageRules.DefaultPelletCount, "Player prefab ShotgunWeapon.pelletCount");
                             ValidateSerializedFloat(prefabShotgun, "spreadAngleDegrees", ShotgunDamageRules.DefaultSpreadAngleDegrees, "Player prefab ShotgunWeapon.spreadAngleDegrees");
                             ValidateSerializedFloat(prefabShotgun, "fullDamageRange", ShotgunDamageRules.DefaultFullDamageRange, "Player prefab ShotgunWeapon.fullDamageRange");
                             ValidateSerializedFloat(prefabShotgun, "mediumRange", ShotgunDamageRules.DefaultMediumRange, "Player prefab ShotgunWeapon.mediumRange");
                             ValidateSerializedFloat(prefabShotgun, "maxRange", ShotgunDamageRules.DefaultMaxRange, "Player prefab ShotgunWeapon.maxRange");
                             ValidateSerializedFloat(prefabShotgun, "mediumMultiplier", ShotgunDamageRules.DefaultMediumMultiplier, "Player prefab ShotgunWeapon.mediumMultiplier");
                             ValidateSerializedFloat(prefabShotgun, "farMultiplier", ShotgunDamageRules.DefaultFarMultiplier, "Player prefab ShotgunWeapon.farMultiplier");
                             ValidateSerializedFloat(prefabShotgun, "pumpDelay", ShotgunDamageRules.DefaultPumpDelay, "Player prefab ShotgunWeapon.pumpDelay");
                             ValidateSerializedFloat(prefabShotgun, "ballImpulsePerPellet", ShotgunDamageRules.DefaultPerPelletBallImpulse, "Player prefab ShotgunWeapon.ballImpulsePerPellet");
                             ValidateSerializedFloat(prefabShotgun, "ballImpulseCap", ShotgunDamageRules.DefaultBallImpulseCap, "Player prefab ShotgunWeapon.ballImpulseCap");
                            if (LayerMask.NameToLayer("Participants") < 0 || LayerMask.NameToLayer("Projectiles") < 0) throw new InvalidOperationException("Participants and Projectiles layers are required.");
                            ValidateSerializedFloat(prefabFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius, "Player prefab PlayerCameraFeedback.celebrationOrbitRadius");
                            ValidateSerializedFloat(prefabFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight, "Player prefab PlayerCameraFeedback.celebrationOrbitHeight");
                            ValidateSerializedFloat(prefabFeedback, "celebrationLookHeight", CelebrationLookHeight, "Player prefab PlayerCameraFeedback.celebrationLookHeight");
                             ValidateSerializedFloat(prefabFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees, "Player prefab PlayerCameraFeedback.celebrationOrbitDegrees");
                             ValidateSerializedFloat(prefabFeedback, "celebrationFov", CelebrationFov, "Player prefab PlayerCameraFeedback.celebrationFov");
                             ValidateSerializedVector3(prefabFeedback, "spectatorOffset", PlayerCameraFeedback.ExpectedSpectatorOffset, "Player prefab PlayerCameraFeedback.spectatorOffset");
                            ValidateSerializedFloat(prefabMotor, "jumpVelocity", JumpVelocity, "Player prefab PlayerMotor.jumpVelocity");
                            ValidateSerializedFloat(prefabMotor, "dashBurstSpeed", PlayerMotorDefaults.DashBurstSpeed, "Player prefab PlayerMotor.dashBurstSpeed");
                            ValidateSerializedFloat(prefabMotor, "dashDuration", PlayerMotorDefaults.DashDuration, "Player prefab PlayerMotor.dashDuration");
                            ValidateSerializedFloat(prefabMotor, "dashSteerRateDegrees", PlayerMotorDefaults.DashSteerRateDegrees, "Player prefab PlayerMotor.dashSteerRateDegrees");
                            ValidateSerializedFloat(prefabMotor, "dashSpeedCap", PlayerMotorDefaults.DashSpeedCap, "Player prefab PlayerMotor.dashSpeedCap");
                            ValidateSerializedInteger(prefabMotor, "jumpsToHardCap", PlayerMotorDefaults.JumpsToHardCap, "Player prefab PlayerMotor.jumpsToHardCap");
                            ValidateSerializedFloat(prefabKick, "dashContactStartDelay", BallKickDefaults.DashContactStartDelay, "Player prefab BallKick.dashContactStartDelay");
                            ValidateSerializedFloat(prefabKick, "dashContactReach", BallKickDefaults.DashContactReach, "Player prefab BallKick.dashContactReach");
                            ValidateSerializedFloat(prefabKick, "dashContactRadiusPadding", BallKickDefaults.DashContactRadiusPadding, "Player prefab BallKick.dashContactRadiusPadding");
                            ValidateSerializedFloat(prefabKick, "cooldown", BallKickDefaults.Cooldown, "Player prefab BallKick.cooldown");
                            ValidateSerializedFloat(prefabKick, "speedFraction", BallKickDefaults.SpeedFraction, "Player prefab BallKick.speedFraction");
                            ValidateSerializedFloat(prefabKick, "playerMomentumShare", BallKickDefaults.PlayerMomentumShare, "Player prefab BallKick.playerMomentumShare");
                            ValidateSerializedFloat(prefabKick, "enemyContactDamage", BallKickDefaults.EnemyContactDamage, "Player prefab BallKick.enemyContactDamage");
                            ValidateSerializedFloat(prefabKick, "enemyShoveImpulse", BallKickDefaults.EnemyShoveImpulse, "Player prefab BallKick.enemyShoveImpulse");
                            ValidateSerializedFloat(prefabKick, "enemyDashRetention", BallKickDefaults.EnemyDashRetention, "Player prefab BallKick.enemyDashRetention");
                            ValidateSerializedFloat(prefabFeedback, "dashKickImpulse", PlayerCameraFeedback.DefaultDashKickImpulse, "Player prefab PlayerCameraFeedback.dashKickImpulse");
                            ValidateSerializedFloat(prefabFeedback, "dashKickImpulseDuration", PlayerCameraFeedback.DefaultDashKickImpulseDuration, "Player prefab PlayerCameraFeedback.dashKickImpulseDuration");
                            var prefabCamera = root.transform.Find("Head/Camera").GetComponent<Camera>();
                             ValidateCrosshair(prefabCamera);
                             var prefabWeaponVisual = Require(root.transform.Find("Head/Camera/Viewmodels/WeaponVisual"), "Player prefab WeaponVisual");
                             ValidateWeaponMaterials(prefabWeaponVisual.gameObject);
                             ValidateImportedVisual(prefabWeaponVisual.gameObject, WeaponModelPath, "Player prefab WeaponVisual");
                             ValidateWeaponVisualContract(prefabWeaponVisual.gameObject, "Player prefab WeaponVisual",
                                 LauncherWeaponBoundsMin, LauncherWeaponBoundsMax);
                             ValidateNoPhysics(prefabWeaponVisual.gameObject, "Player prefab WeaponVisual");
                             ValidateNoAnimators(prefabWeaponVisual.gameObject, "Player prefab WeaponVisual");
                             var prefabFpsShotgunVisual = Require(root.transform.Find("Head/Camera/Viewmodels/FpsShotgunVisual"), "Player prefab FpsShotgunVisual");
                             ValidateShotgunMaterials(prefabFpsShotgunVisual.gameObject);
                             ValidateImportedVisual(prefabFpsShotgunVisual.gameObject, FpsShotgunModelPath, "Player prefab FpsShotgunVisual");
                             ValidateWeaponVisualContract(prefabFpsShotgunVisual.gameObject, "Player prefab FpsShotgunVisual",
                                 FpsShotgunBoundsMin, FpsShotgunBoundsMax);
                             ValidateNoPhysics(prefabFpsShotgunVisual.gameObject, "Player prefab FpsShotgunVisual");
                             ValidateNoAnimators(prefabFpsShotgunVisual.gameObject, "Player prefab FpsShotgunVisual");
                             ValidateNoPhysics(root.transform.Find("Head/Camera/Viewmodels/FpsKickVisual").gameObject, "Player prefab FpsKickVisual");
                             var worldVisual = prefabWorldVisual;
                             ValidateLayerRecursively(worldVisual.gameObject, 0, "Player prefab WorldVisual");
                             if (prefabWorldShotgunMount == null || prefabWorldShotgunMount.parent == null || prefabWorldShotgunMount.parent.name != "Hand.R")
                                 throw new InvalidOperationException("Player prefab WorldShotgunMount must be parented to imported Hand.R.");
                             var prefabWorldShotgunVisual = FindNamedTransform(prefabWorldShotgunMount, "WorldShotgunVisual");
                             if (prefabWorldShotgunVisual == null) throw new InvalidOperationException("Player prefab WorldShotgunVisual is missing.");
                             ValidateShotgunMaterials(prefabWorldShotgunVisual.gameObject);
                             ValidateImportedVisual(prefabWorldShotgunVisual.gameObject, ShotgunModelPath, "Player prefab WorldShotgunVisual");
                             ValidateWeaponVisualContract(prefabWorldShotgunVisual.gameObject, "Player prefab WorldShotgunVisual",
                                 WorldShotgunBoundsMin, WorldShotgunBoundsMax);
                             ValidateNoPhysics(prefabWorldShotgunVisual.gameObject, "Player prefab WorldShotgunVisual");
                             ValidateNoAnimators(prefabWorldShotgunVisual.gameObject, "Player prefab WorldShotgunVisual");
                             ValidateShotgunPresentation(root, prefabCamera, prefabFpsShotgunVisual, prefabWorldVisual, prefabWorldShotgunMount, prefabWorldShotgunVisual, "Player prefab");
                             ValidateTeamTintRenderers(prefabPresentation, prefabWorldVisual, prefabWorldShotgunMount,
                                 FindRendererByName(prefabWorldShotgunVisual.gameObject, "WeaponAccent"),
                                 FindRendererByName(prefabWorldShotgunVisual.gameObject, "WeaponAccentCore"),
                                 "Player prefab PlayerPresentation.teamTintRenderers");
                             var blueCue = Require(root.transform.Find("BlueCircleCue"), "Player prefab BlueCircleCue");
                             var redCue = Require(root.transform.Find("RedTriangleCue"), "Player prefab RedTriangleCue");
                             ValidateShapeCue(blueCue, BlueCircleCueMeshPath, "Player prefab BlueCircleCue");
                             ValidateShapeCue(redCue, RedTriangleCueMeshPath, "Player prefab RedTriangleCue");
                             if (Vector3.Distance(blueCue.localScale, new Vector3(0.84f, 0.84f, 2f)) > 0.001f ||
                                 Vector3.Distance(redCue.localScale, Vector3.one * 2f) > 0.001f)
                                 throw new InvalidOperationException("Player team cue scale must be doubled.");
                             var blueShield = Require(root.transform.Find("ImmunityShield/BlueImmunityShield"), "Player prefab BlueImmunityShield");
                             var redShield = Require(root.transform.Find("ImmunityShield/RedImmunityShield"), "Player prefab RedImmunityShield");
                             ValidateImmunityShield(blueShield, AssetDatabase.LoadAssetAtPath<Material>(TeamBlueShieldMaterialPath), "Player prefab BlueImmunityShield");
                             ValidateImmunityShield(redShield, AssetDatabase.LoadAssetAtPath<Material>(TeamRedShieldMaterialPath), "Player prefab RedImmunityShield");
                             if (Vector3.Distance(blueShield.localScale, new Vector3(2.4f, 4f, 2.4f)) > 0.001f ||
                                 Vector3.Distance(redShield.localScale, new Vector3(2.4f, 4f, 2.4f)) > 0.001f)
                                 throw new InvalidOperationException("Player immunity shield scale must be doubled.");
                        }
                        else if (path == BallPrefabPath)
                        {
                            var body = Require(root.GetComponent<Rigidbody>(), "Ball prefab Rigidbody");
                            var collider = Require(root.GetComponent<Collider>(), "Ball prefab collider");
                            var motor = Require(root.GetComponent<BallMotor>(), "Ball prefab BallMotor");
                            ValidateReference(motor, "body", body, "Ball prefab BallMotor.body");
                            ValidateReference(motor, "ballCollider", collider, "Ball prefab BallMotor.ballCollider");
                            var sphere = collider as SphereCollider;
                            var worldRadius = sphere != null ? sphere.radius * root.transform.lossyScale.x : 0f;
                            if (body.isKinematic != !dynamicBody || body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic || collider.sharedMaterial != ballSurface ||
                                Vector3.Distance(root.transform.localScale, Vector3.one * BallPrefabScale) > 0.001f || Mathf.Abs(worldRadius - BallRadius) > 0.001f)
                            {
                                throw new InvalidOperationException("Ball prefab Rigidbody/collider/scale settings invalid.");
                            }
                            var renderer = Require(root.GetComponent<Renderer>(), "Ball prefab renderer");
                            var material = renderer.sharedMaterial;
                            ValidateBallMaterial(material);
                            var ballFilter = Require(root.GetComponent<MeshFilter>(), "Ball prefab MeshFilter");
                            ValidateBallMesh(ballFilter.sharedMesh);
                        }
                        else if (path == HealthPickupPrefabPath)
                        {
                            ValidateHealthPickupPrefab(root);
                        }
                        else if (path == ShotgunPickupPrefabPath)
                        {
                            ValidateShotgunPickupPrefab(root);
                        }
                        else if (path == AmmoPickupPrefabPath)
                        {
                            ValidateAmmoPickupPrefab(root);
                        }
                        else if (path == RocketPrefabPath)
                        {
                            if (root.layer != LayerMask.NameToLayer("Projectiles")) throw new InvalidOperationException("Rocket prefab root must use Projectiles layer.");
                            var body = Require(root.GetComponent<Rigidbody>(), "Rocket prefab Rigidbody");
                            var collider = Require(root.GetComponent<Collider>(), "Rocket prefab collider");
                            var projectile = Require(root.GetComponent<RocketProjectile>(), "Rocket prefab RocketProjectile");
                            ValidateReference(projectile, "body", body, "Rocket prefab RocketProjectile.body");
                            ValidateReference(projectile, "projectileCollider", collider, "Rocket prefab RocketProjectile.projectileCollider");
                            ValidateTrail(root);
                            if (!body.isKinematic || body.useGravity || body.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
                            {
                                throw new InvalidOperationException("Rocket prefab Rigidbody settings invalid.");
                            }
                            var visual = Require(root.transform.Find("Visual"), "Rocket prefab imported Visual");
                            var meshFilters = visual.GetComponentsInChildren<MeshFilter>(true);
                            if (meshFilters.Length == 0)
                            {
                                throw new InvalidOperationException("Rocket prefab Visual contains no mesh filters.");
                            }
                            var surfaceCount = 0;
                            var hotCount = 0;
                            for (var i = 0; i < meshFilters.Length; i++)
                            {
                                var mesh = meshFilters[i].sharedMesh;
                                if (mesh == null || AssetDatabase.GetAssetPath(mesh) != RocketModelPath)
                                {
                                    throw new InvalidOperationException("Rocket prefab Visual must use imported rocket mesh.");
                                }
                                var renderer = Require(meshFilters[i].GetComponent<Renderer>(), "Rocket prefab imported mesh renderer");
                                if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1) throw new InvalidOperationException("Rocket renderer must have one material slot: " + renderer.name);
                                if (renderer.name == "RocketSurface") surfaceCount++; else if (renderer.name == "RocketHot") hotCount++; else throw new InvalidOperationException("Unknown rocket renderer: " + renderer.name);
                                ValidateRocketMaterial(renderer.sharedMaterial, renderer.name);
                            }
                            if (surfaceCount != 1 || hotCount != 1) throw new InvalidOperationException("Rocket prefab must contain exactly one surface and one hot renderer.");
                            ValidateRocketVisualForward(root.transform, meshFilters);
                            var components = root.GetComponentsInChildren<Component>(true);
                            for (var i = 0; i < components.Length; i++)
                            {
                                if (components[i] == null)
                                {
                                    throw new InvalidOperationException("Rocket prefab contains a missing component.");
                                }
                            }
                        }
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }

                internal static void ValidateHealthPickupPrefab(GameObject root)
                {
                    if (root == null || root.name != "HealthPickup" || root.transform.localScale != Vector3.one || root.isStatic)
                        throw new InvalidOperationException("Health pickup prefab root contract invalid.");
                    var trigger = Require(root.GetComponent<SphereCollider>(), "Health pickup trigger collider");
                    var body = Require(root.GetComponent<Rigidbody>(), "Health pickup Rigidbody");
                    var pickup = Require(root.GetComponent<HealthPickup>(), "Health pickup HealthPickup component");
                    var visualRoot = Require(root.transform.Find("VisualRoot"), "Health pickup VisualRoot");
                    if (!trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.HealthPickupTriggerRadius) > 0.001f ||
                        body.isKinematic == false || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        root.GetComponents<Collider>().Length != 1 || root.GetComponents<Rigidbody>().Length != 1)
                        throw new InvalidOperationException("Health pickup trigger/body contract invalid.");
                    ValidateReference(pickup, "pickupTrigger", trigger, "Health pickup pickupTrigger");
                    ValidateReference(pickup, "visualRoot", visualRoot.gameObject, "Health pickup visualRoot");
                    ValidateSerializedFloat(pickup, "respawnDelay", MovementLabContract.HealthPickupRespawnDelay, "Health pickup respawnDelay");
                    ValidateSerializedFloat(pickup, "restoreFraction", MovementLabContract.HealthPickupRestoreFraction, "Health pickup restoreFraction");
                    var matchProperty = new SerializedObject(pickup).FindProperty("match");
                    if (matchProperty == null || matchProperty.propertyType != SerializedPropertyType.ObjectReference || matchProperty.objectReferenceValue != null)
                        throw new InvalidOperationException("Health pickup prefab match reference must remain null.");

                    var cross = new[] { "BarHorizontal", "BarVertical", "Core" };
                    if (visualRoot.childCount != cross.Length) throw new InvalidOperationException("Health pickup VisualRoot must contain exactly three cross primitives.");
                    for (var i = 0; i < cross.Length; i++)
                    {
                        var child = visualRoot.Find(cross[i]);
                        if (child == null || child.GetComponents<Collider>().Length != 0 || child.GetComponents<Rigidbody>().Length != 0 ||
                            child.GetComponents<MonoBehaviour>().Length != 0 || child.gameObject.isStatic)
                            throw new InvalidOperationException("Health pickup cross visual contract invalid: " + cross[i]);
                        var expectedScale = i == 0 ? MovementLabContract.HealthCrossHorizontalScale : i == 1 ? MovementLabContract.HealthCrossVerticalScale : MovementLabContract.HealthCrossCoreScale;
                        var expectedPosition = i == 2 ? new Vector3(0f, 0f, -0.05f) : Vector3.zero;
                        if (Vector3.Distance(child.localScale, expectedScale) > 0.001f || Vector3.Distance(child.localPosition, expectedPosition) > 0.001f)
                            throw new InvalidOperationException("Health pickup cross transform contract invalid: " + cross[i]);
                        var renderer = Require(child.GetComponent<MeshRenderer>(), "Health pickup cross renderer " + cross[i]);
                        if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(HealthPickupMaterialPath) ||
                            renderer.lightProbeUsage != LightProbeUsage.BlendProbes || renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes)
                            throw new InvalidOperationException("Health pickup cross material contract invalid: " + cross[i]);
                    }
                    if (root.GetComponentsInChildren<Light>(true).Length != 0 || root.GetComponentsInChildren<ParticleSystem>(true).Length != 0 ||
                        root.GetComponentsInChildren<Animator>(true).Length != 0)
                        throw new InvalidOperationException("Health pickup prefab must not contain lights, particles, or animation.");
                }

                internal static void ValidateShotgunPickupPrefab(GameObject root)
                {
                    if (root == null || root.name != "ShotgunPickup" || root.transform.localScale != Vector3.one || root.isStatic)
                        throw new InvalidOperationException("Shotgun pickup prefab root contract invalid.");
                    var trigger = Require(root.GetComponent<SphereCollider>(), "Shotgun pickup trigger collider");
                    var body = Require(root.GetComponent<Rigidbody>(), "Shotgun pickup Rigidbody");
                    var pickup = Require(root.GetComponent<ShotgunPickup>(), "Shotgun pickup ShotgunPickup component");
                    var visualRoot = Require(root.transform.Find("VisualRoot"), "Shotgun pickup VisualRoot");
                    if (!trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.ShotgunPickupTriggerRadius) > 0.001f ||
                        !body.isKinematic || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        root.GetComponents<Collider>().Length != 1 || root.GetComponents<Rigidbody>().Length != 1)
                        throw new InvalidOperationException("Shotgun pickup trigger/body contract invalid.");
                    ValidateReference(pickup, "pickupTrigger", trigger, "Shotgun pickup pickupTrigger");
                    ValidateReference(pickup, "visualRoot", visualRoot.gameObject, "Shotgun pickup visualRoot");
                    ValidateSerializedInteger(pickup, "grant", MovementLabContract.ShotgunPickupGrant, "Shotgun pickup grant");
                    ValidateSerializedFloat(pickup, "respawnDelay", MovementLabContract.ShotgunPickupRespawnDelay, "Shotgun pickup respawnDelay");
                    ValidatePickupPrefabMatchIsNull(pickup, "Shotgun pickup");
                    ValidatePickupVisualRoot(visualRoot, 3, "Shotgun pickup");

                    var model = Require(visualRoot.Find("ShotgunModel"), "Shotgun pickup imported model");
                    ValidateShotgunMaterials(model.gameObject);
                    ValidateImportedVisual(model.gameObject, ShotgunModelPath, "Shotgun pickup imported model");
                    ValidateWeaponVisualContract(model.gameObject, "Shotgun pickup imported model", WorldShotgunBoundsMin, WorldShotgunBoundsMax);
                    ValidateNoPhysics(model.gameObject, "Shotgun pickup imported model");
                    ValidateNoAnimators(model.gameObject, "Shotgun pickup imported model");
                    ValidateImportedVisualForward(model, "Shotgun pickup imported model");
                    if (model.localRotation != Quaternion.identity || model.localScale != Vector3.one)
                        throw new InvalidOperationException("Shotgun pickup imported model must use identity +Z mounting.");
                    ValidatePickupCuePair(visualRoot, "Shotgun pickup");
                    ValidateDynamicHierarchy(root, "Shotgun pickup");
                }

                internal static void ValidateAmmoPickupPrefab(GameObject root)
                {
                    if (root == null || root.name != "AmmoPickup" || root.transform.localScale != Vector3.one || root.isStatic)
                        throw new InvalidOperationException("Ammo pickup prefab root contract invalid.");
                    var trigger = Require(root.GetComponent<SphereCollider>(), "Ammo pickup trigger collider");
                    var body = Require(root.GetComponent<Rigidbody>(), "Ammo pickup Rigidbody");
                    var pickup = Require(root.GetComponent<AmmoPickup>(), "Ammo pickup AmmoPickup component");
                    var visualRoot = Require(root.transform.Find("VisualRoot"), "Ammo pickup VisualRoot");
                    if (!trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.AmmoPickupTriggerRadius) > 0.001f ||
                        !body.isKinematic || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        root.GetComponents<Collider>().Length != 1 || root.GetComponents<Rigidbody>().Length != 1)
                        throw new InvalidOperationException("Ammo pickup trigger/body contract invalid.");
                    ValidateReference(pickup, "pickupTrigger", trigger, "Ammo pickup pickupTrigger");
                    ValidateReference(pickup, "visualRoot", visualRoot.gameObject, "Ammo pickup visualRoot");
                    ValidateSerializedInteger(pickup, "grant", MovementLabContract.AmmoPickupGrant, "Ammo pickup grant");
                    ValidateSerializedFloat(pickup, "respawnDelay", MovementLabContract.AmmoPickupRespawnDelay, "Ammo pickup respawnDelay");
                    ValidatePickupPrefabMatchIsNull(pickup, "Ammo pickup");
                    ValidatePickupVisualRoot(visualRoot, 4, "Ammo pickup");

                    var expectedShellMaterial = AssetDatabase.LoadAssetAtPath<Material>(AmmoShellMaterialPath);
                    for (var i = 0; i < 2; i++)
                    {
                        var shellName = i == 0 ? "ShellLeft" : "ShellRight";
                        var shell = Require(visualRoot.Find(shellName), "Ammo pickup " + shellName);
                        var filter = Require(shell.GetComponent<MeshFilter>(), "Ammo pickup " + shellName + " MeshFilter");
                        var renderer = Require(shell.GetComponent<MeshRenderer>(), "Ammo pickup " + shellName + " MeshRenderer");
                        var expectedPosition = i == 0 ? MovementLabContract.AmmoShellLeftPosition : MovementLabContract.AmmoShellRightPosition;
                        if (filter.sharedMesh == null || filter.sharedMesh.name != "Capsule" || shell.localPosition != expectedPosition ||
                            shell.localScale != MovementLabContract.AmmoShellScale || renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 ||
                            renderer.sharedMaterial != expectedShellMaterial || shell.GetComponents<Collider>().Length != 0 ||
                            shell.GetComponents<Rigidbody>().Length != 0 || shell.GetComponents<MonoBehaviour>().Length != 0 || shell.gameObject.isStatic)
                            throw new InvalidOperationException("Ammo pickup shell visual contract invalid: " + shellName);
                    }
                    ValidatePickupCuePair(visualRoot, "Ammo pickup");
                    ValidateDynamicHierarchy(root, "Ammo pickup");
                }

                private static void ValidatePickupPrefabMatchIsNull(ArenaPickup pickup, string label)
                {
                    var matchProperty = new SerializedObject(pickup).FindProperty("match");
                    if (matchProperty == null || matchProperty.propertyType != SerializedPropertyType.ObjectReference || matchProperty.objectReferenceValue != null)
                        throw new InvalidOperationException(label + " prefab match reference must remain null.");
                }

                private static void ValidatePickupVisualRoot(Transform visualRoot, int expectedChildCount, string label)
                {
                    if (visualRoot.parent == null || visualRoot.childCount != expectedChildCount || visualRoot.GetComponentsInChildren<Collider>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<Rigidbody>(true).Length != 0 || visualRoot.GetComponentsInChildren<Light>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<ParticleSystem>(true).Length != 0 || visualRoot.GetComponentsInChildren<Animator>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                        throw new InvalidOperationException(label + " visual hierarchy must contain only dynamic mesh visuals.");
                }

                private static void ValidatePickupCuePair(Transform visualRoot, string label)
                {
                    var blue = Require(visualRoot.Find("BlueCircleCue"), label + " BlueCircleCue");
                    var red = Require(visualRoot.Find("RedTriangleCue"), label + " RedTriangleCue");
                    ValidateShapeCue(blue, BlueCircleCueMeshPath, label + " BlueCircleCue");
                    ValidateShapeCue(red, RedTriangleCueMeshPath, label + " RedTriangleCue");
                    var blueRenderer = blue.GetComponent<MeshRenderer>();
                    var redRenderer = red.GetComponent<MeshRenderer>();
                    var blueMaterial = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueMaterialPath);
                    var redMaterial = AssetDatabase.LoadAssetAtPath<Material>(TeamRedMaterialPath);
                    if (blueRenderer.sharedMaterial != blueMaterial || redRenderer.sharedMaterial != redMaterial ||
                        blue.localScale != MovementLabContract.PickupCueScale || red.localScale != MovementLabContract.PickupCueScale ||
                        blue.localPosition != MovementLabContract.PickupCueBluePosition || red.localPosition != MovementLabContract.PickupCueRedPosition ||
                        blue.GetComponents<MonoBehaviour>().Length != 0 || red.GetComponents<MonoBehaviour>().Length != 0 ||
                        blue.gameObject.isStatic || red.gameObject.isStatic)
                        throw new InvalidOperationException(label + " cue pair contract invalid.");
                }

                internal static void ValidateBallMesh(Mesh mesh)
                {
                    if (mesh == null || mesh.name != "Sphere" || mesh.vertexCount == 0 || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
                    {
                        throw new InvalidOperationException("Ball must use built-in Sphere mesh with matching UVs.");
                    }
                    var uv = mesh.uv;
                    for (var i = 0; i < uv.Length; i++)
                    {
                        if (float.IsNaN(uv[i].x) || float.IsNaN(uv[i].y) || float.IsInfinity(uv[i].x) || float.IsInfinity(uv[i].y)) throw new InvalidOperationException("Ball mesh UVs must be finite.");
                    }
                }

                internal static void ValidateShapeCue(Transform cue, string meshPath, string label)
                {
                    if (cue == null) throw new InvalidOperationException(label + " is missing.");
                    var filter = Require(cue.GetComponent<MeshFilter>(), label + " MeshFilter");
                    var renderer = Require(cue.GetComponent<MeshRenderer>(), label + " MeshRenderer");
                    if (filter.sharedMesh == null || AssetDatabase.GetAssetPath(filter.sharedMesh) != meshPath || renderer.sharedMaterial == null)
                        throw new InvalidOperationException(label + " mesh/material provenance invalid.");
                }

                internal static void ValidateImmunityShield(Transform shield, Material expectedMaterial, string label)
                {
                    var system = Require(shield.GetComponent<ParticleSystem>(), label + " ParticleSystem");
                    var renderer = Require(shield.GetComponent<ParticleSystemRenderer>(), label + " ParticleSystemRenderer");
                    if (shield.GetComponent<MeshRenderer>() != null || renderer.renderMode != ParticleSystemRenderMode.Mesh || renderer.mesh == null || renderer.sharedMaterial != expectedMaterial || system.main.maxParticles != 2)
                        throw new InvalidOperationException(label + " particle mesh/material contract invalid.");
                }

                internal static void ValidateImportedVisualForward(Transform visualRoot, string label)
                {
                    if (visualRoot == null) throw new InvalidOperationException(label + " root is missing.");
                    var meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>(true);
                    var localBounds = new Bounds();
                    var hasBounds = false;
                    for (var i = 0; i < meshFilters.Length; i++)
                    {
                        var filter = meshFilters[i];
                        if (filter == null || filter.sharedMesh == null) continue;
                        var meshBounds = filter.sharedMesh.bounds;
                        var center = meshBounds.center;
                        var extents = meshBounds.extents;
                        for (var x = -1; x <= 1; x += 2)
                        {
                            for (var y = -1; y <= 1; y += 2)
                            {
                                for (var z = -1; z <= 1; z += 2)
                                {
                                    var worldPoint = filter.transform.TransformPoint(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                                    var localPoint = visualRoot.InverseTransformPoint(worldPoint);
                                    if (hasBounds) localBounds.Encapsulate(localPoint);
                                    else
                                    {
                                        localBounds = new Bounds(localPoint, Vector3.zero);
                                        hasBounds = true;
                                    }
                                }
                            }
                        }
                    }

                    if (!hasBounds || localBounds.size.z <= localBounds.size.x || localBounds.size.z <= localBounds.size.y || localBounds.max.z <= 0f || localBounds.max.z <= -localBounds.min.z)
                    {
                        throw new InvalidOperationException(label + " imported mesh must be Z-major with muzzle-positive local +Z bounds.");
                    }
                }

                internal static void ValidateRocketVisualForward(Transform rocketRoot, MeshFilter[] meshFilters)
                {
                    var localBounds = new Bounds();
                    var hasBounds = false;
                    for (var i = 0; i < meshFilters.Length; i++)
                    {
                        var meshBounds = meshFilters[i].sharedMesh.bounds;
                        var center = meshBounds.center;
                        var extents = meshBounds.extents;
                        for (var x = -1; x <= 1; x += 2)
                        {
                            for (var y = -1; y <= 1; y += 2)
                            {
                                for (var z = -1; z <= 1; z += 2)
                                {
                                    var meshPoint = center + Vector3.Scale(extents, new Vector3(x, y, z));
                                    var rocketPoint = rocketRoot.InverseTransformPoint(meshFilters[i].transform.TransformPoint(meshPoint));
                                    if (hasBounds)
                                    {
                                        localBounds.Encapsulate(rocketPoint);
                                    }
                                    else
                                    {
                                        localBounds = new Bounds(rocketPoint, Vector3.zero);
                                        hasBounds = true;
                                    }
                                }
                            }
                        }
                    }
                    if (!hasBounds || localBounds.size.z <= localBounds.size.x || localBounds.size.z <= localBounds.size.y || localBounds.max.z <= -localBounds.min.z)
                    {
                        throw new InvalidOperationException("Rocket prefab imported mesh must point along local +Z.");
                    }
                }

                internal static void ValidateImportedVisual(GameObject visual, string sourcePath, string label)
                {
                    var nestedShotgun = FindNamedTransform(visual.transform, "WorldShotgunMount");
                    var renderers = visual.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer => nestedShotgun == null || !renderer.transform.IsChildOf(nestedShotgun))
                        .ToArray();
                    if (renderers.Length == 0) throw new InvalidOperationException(label + " contains no renderers.");
                    var sourceGuids = AssetDatabase.AssetPathToGUID(sourcePath);
                    if (string.IsNullOrEmpty(sourceGuids)) throw new InvalidOperationException("Missing source GUID: " + sourcePath);
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var path = AssetDatabase.GetAssetPath(renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh);
                        if (path != sourcePath)
                        {
                            throw new InvalidOperationException(label + " renderer provenance mismatch: " + renderer.name + " -> " + path);
                        }
                        if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                        {
                            throw new InvalidOperationException(label + " renderer missing material: " + renderer.name);
                        }
                    }
                }

                internal static void ValidateWeaponVisualContract(GameObject visual, string label,
                    Vector3 expectedMin, Vector3 expectedMax)
                {
                    if (visual == null) throw new InvalidOperationException(label + " visual is missing.");
                    var renderers = visual.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 4) throw new InvalidOperationException(label + " must contain exactly four renderer groups.");
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var shell = default(MeshRenderer);
                    var core = default(MeshRenderer);
                    var aggregate = new Bounds();
                    var hasBounds = false;
                    for (var i = 0; i < renderers.Length; i++)
                    {
                        var renderer = renderers[i];
                        var group = GetWeaponRendererGroup(renderer.name);
                        if (group == null || !seen.Add(group))
                            throw new InvalidOperationException(label + " renderer groups must be exactly WeaponMetal, WeaponDark, WeaponAccent, and WeaponAccentCore.");
                        if (renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterials[0] == null)
                            throw new InvalidOperationException(label + " renderer must have exactly one material slot: " + renderer.name);
                        var filter = renderer.GetComponent<MeshFilter>();
                        if (filter == null || filter.sharedMesh == null)
                            throw new InvalidOperationException(label + " renderer mesh is missing: " + renderer.name);
                        ValidateMeshPbrChannels(filter.sharedMesh, false, label + "/" + group);
                        if (group == "WeaponAccent") shell = renderer;
                        if (group == "WeaponAccentCore") core = renderer;

                        var meshBounds = filter.sharedMesh.bounds;
                        var center = meshBounds.center;
                        var extents = meshBounds.extents;
                        for (var x = -1; x <= 1; x += 2)
                        for (var y = -1; y <= 1; y += 2)
                        for (var z = -1; z <= 1; z += 2)
                        {
                            var point = visual.transform.InverseTransformPoint(renderer.transform.TransformPoint(center + Vector3.Scale(extents, new Vector3(x, y, z))));
                            if (hasBounds) aggregate.Encapsulate(point);
                            else { aggregate = new Bounds(point, Vector3.zero); hasBounds = true; }
                        }
                    }
                    if (seen.Count != 4 || shell == null || core == null)
                        throw new InvalidOperationException(label + " renderer groups are incomplete.");
                    if (!hasBounds || !WithinWeaponBoundsTolerance(aggregate.min, expectedMin) ||
                        !WithinWeaponBoundsTolerance(aggregate.max, expectedMax))
                        throw new InvalidOperationException(label + " Unity bounds mismatch: expected " + expectedMin + ".." + expectedMax + ", got " + aggregate.min + ".." + aggregate.max + ".");
                    ValidateShellCoreIslandContainment(visual, shell, core, label);
                }

                private static bool WithinWeaponBoundsTolerance(Vector3 actual, Vector3 expected)
                {
                    return Mathf.Abs(actual.x - expected.x) <= WeaponBoundsTolerance &&
                           Mathf.Abs(actual.y - expected.y) <= WeaponBoundsTolerance &&
                           Mathf.Abs(actual.z - expected.z) <= WeaponBoundsTolerance;
                }

                private static string GetWeaponRendererGroup(string rendererName)
                {
                    if (string.IsNullOrEmpty(rendererName)) return null;
                    if (string.Equals(rendererName, "WeaponAccentCore", StringComparison.Ordinal)) return "WeaponAccentCore";
                    if (string.Equals(rendererName, "WeaponAccent", StringComparison.Ordinal)) return "WeaponAccent";
                    if (string.Equals(rendererName, "WeaponDark", StringComparison.Ordinal)) return "WeaponDark";
                    if (string.Equals(rendererName, "WeaponMetal", StringComparison.Ordinal)) return "WeaponMetal";
                    return null;
                }

                private static int GetWeaponMaterialIndex(string group)
                {
                    if (string.Equals(group, "WeaponMetal", StringComparison.Ordinal)) return 0;
                    if (string.Equals(group, "WeaponDark", StringComparison.Ordinal)) return 1;
                    if (string.Equals(group, "WeaponAccentCore", StringComparison.Ordinal)) return 3;
                    if (string.Equals(group, "WeaponAccent", StringComparison.Ordinal)) return 2;
                    return -1;
                }

                private static int GetCharacterMaterialIndex(string rendererName)
                {
                    if (string.Equals(rendererName, "FpsKickMesh", StringComparison.Ordinal)) return 0;
                    if (string.Equals(rendererName, "CharacterArmor", StringComparison.Ordinal)) return 0;
                    if (string.Equals(rendererName, "CharacterHead", StringComparison.Ordinal)) return 1;
                    if (string.Equals(rendererName, "CharacterBody", StringComparison.Ordinal)) return 2;
                    if (string.Equals(rendererName, "CharacterEye", StringComparison.Ordinal)) return 3;
                    return -1;
                }

                private static void ValidateShellCoreIslandContainment(GameObject visual, MeshRenderer shellRenderer,
                    MeshRenderer coreRenderer, string label)
                {
                    var shellIslands = GetConnectedMeshIslands(visual, shellRenderer);
                    var coreIslands = GetConnectedMeshIslands(visual, coreRenderer);
                    if (shellIslands.Count == 0 || shellIslands.Count != coreIslands.Count)
                        throw new InvalidOperationException(label + " shell/core island counts must match and be nonzero.");
                    var matchedShell = new bool[shellIslands.Count];
                    for (var coreIndex = 0; coreIndex < coreIslands.Count; coreIndex++)
                    {
                        var coreBounds = coreIslands[coreIndex];
                        var match = -1;
                        var containingShellCount = 0;
                        for (var shellIndex = 0; shellIndex < shellIslands.Count; shellIndex++)
                        {
                            var shellBounds = shellIslands[shellIndex];
                            var inset = Vector3.one * WeaponShellCoreInset;
                            if (coreBounds.min.x >= shellBounds.min.x + inset.x && coreBounds.min.y >= shellBounds.min.y + inset.y && coreBounds.min.z >= shellBounds.min.z + inset.z &&
                                coreBounds.max.x <= shellBounds.max.x - inset.x && coreBounds.max.y <= shellBounds.max.y - inset.y && coreBounds.max.z <= shellBounds.max.z - inset.z)
                            {
                                containingShellCount++;
                                match = shellIndex;
                            }
                        }
                        if (containingShellCount != 1)
                            throw new InvalidOperationException(label + " core island " + coreIndex + " must have exactly one containing shell island with the required inset; found " + containingShellCount + ".");
                        if (matchedShell[match]) throw new InvalidOperationException(label + " shell island " + match + " contains multiple core islands.");
                        matchedShell[match] = true;
                    }
                    for (var shellIndex = 0; shellIndex < matchedShell.Length; shellIndex++)
                        if (!matchedShell[shellIndex]) throw new InvalidOperationException(label + " shell island " + shellIndex + " has no matching core island.");
                }

                private static List<Bounds> GetConnectedMeshIslands(GameObject visual, MeshRenderer renderer)
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    var vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
                    var triangles = mesh != null ? mesh.triangles : Array.Empty<int>();
                    var triangleCount = triangles.Length / 3;
                    var weldedVertexIds = GetWeldedVertexIds(vertices);
                    var triangleNeighbors = new List<int>[triangleCount];
                    for (var triangle = 0; triangle < triangleCount; triangle++)
                        triangleNeighbors[triangle] = new List<int>();

                    var trianglesByEdge = new Dictionary<UndirectedMeshEdge, List<int>>();
                    for (var triangle = 0; triangle < triangleCount; triangle++)
                    {
                        var triangleStart = triangle * 3;
                        for (var corner = 0; corner < 3; corner++)
                        {
                            var firstVertex = triangles[triangleStart + corner];
                            var secondVertex = triangles[triangleStart + (corner + 1) % 3];
                            if (firstVertex < 0 || firstVertex >= weldedVertexIds.Length ||
                                secondVertex < 0 || secondVertex >= weldedVertexIds.Length)
                                continue;

                            var firstWeldedVertex = weldedVertexIds[firstVertex];
                            var secondWeldedVertex = weldedVertexIds[secondVertex];
                            if (firstWeldedVertex == secondWeldedVertex) continue;
                            var edge = new UndirectedMeshEdge(firstWeldedVertex, secondWeldedVertex);
                            if (!trianglesByEdge.TryGetValue(edge, out var edgeTriangles))
                            {
                                edgeTriangles = new List<int>();
                                trianglesByEdge.Add(edge, edgeTriangles);
                            }
                            for (var edgeTriangleIndex = 0; edgeTriangleIndex < edgeTriangles.Count; edgeTriangleIndex++)
                            {
                                var edgeTriangle = edgeTriangles[edgeTriangleIndex];
                                if (edgeTriangle == triangle) continue;
                                triangleNeighbors[triangle].Add(edgeTriangle);
                                triangleNeighbors[edgeTriangle].Add(triangle);
                            }
                            if (edgeTriangles.Count == 0 || edgeTriangles[edgeTriangles.Count - 1] != triangle)
                                edgeTriangles.Add(triangle);
                        }
                    }

                    var visited = new bool[triangleCount];
                    var islands = new List<Bounds>();
                    for (var start = 0; start < triangleCount; start++)
                    {
                        if (visited[start]) continue;
                        var queue = new Queue<int>();
                        queue.Enqueue(start);
                        visited[start] = true;
                        var islandBounds = new Bounds();
                        var hasBounds = false;
                        while (queue.Count > 0)
                        {
                            var triangle = queue.Dequeue();
                            for (var corner = 0; corner < 3; corner++)
                            {
                                var vertexIndex = triangles[triangle * 3 + corner];
                                if (vertexIndex < 0 || vertexIndex >= vertices.Length) continue;
                                var point = visual.transform.InverseTransformPoint(renderer.transform.TransformPoint(vertices[vertexIndex]));
                                if (hasBounds) islandBounds.Encapsulate(point);
                                else { islandBounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                            }
                            for (var neighborIndex = 0; neighborIndex < triangleNeighbors[triangle].Count; neighborIndex++)
                            {
                                var neighbor = triangleNeighbors[triangle][neighborIndex];
                                if (!visited[neighbor]) { visited[neighbor] = true; queue.Enqueue(neighbor); }
                            }
                        }
                        if (hasBounds) islands.Add(islandBounds);
                    }
                    return islands;
                }

                private static int[] GetWeldedVertexIds(Vector3[] vertices)
                {
                    var weldedVertexIds = new int[vertices.Length];
                    var weldedPositions = new List<Vector3>();
                    var positionBuckets = new Dictionary<MeshPositionCell, List<int>>();
                    for (var vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        var position = vertices[vertex];
                        var cell = new MeshPositionCell(position);
                        var weldedVertex = -1;
                        for (var x = -1; x <= 1 && weldedVertex < 0; x++)
                        for (var y = -1; y <= 1 && weldedVertex < 0; y++)
                        for (var z = -1; z <= 1 && weldedVertex < 0; z++)
                        {
                            var nearbyCell = cell.Offset(x, y, z);
                            if (!positionBuckets.TryGetValue(nearbyCell, out var nearbyVertices)) continue;
                            for (var nearbyIndex = 0; nearbyIndex < nearbyVertices.Count; nearbyIndex++)
                            {
                                var candidate = nearbyVertices[nearbyIndex];
                                if (AreCoincidentMeshPositions(position, weldedPositions[candidate]))
                                {
                                    weldedVertex = candidate;
                                    break;
                                }
                            }
                        }
                        if (weldedVertex < 0)
                        {
                            weldedVertex = weldedPositions.Count;
                            weldedPositions.Add(position);
                            if (!positionBuckets.TryGetValue(cell, out var cellVertices))
                            {
                                cellVertices = new List<int>();
                                positionBuckets.Add(cell, cellVertices);
                            }
                            cellVertices.Add(weldedVertex);
                        }
                        weldedVertexIds[vertex] = weldedVertex;
                    }
                    return weldedVertexIds;
                }

                private static bool AreCoincidentMeshPositions(Vector3 first, Vector3 second)
                {
                    return (first - second).sqrMagnitude <=
                           WeaponMeshIslandPositionTolerance * WeaponMeshIslandPositionTolerance;
                }

                private readonly struct MeshPositionCell : IEquatable<MeshPositionCell>
                {
                    private readonly int x;
                    private readonly int y;
                    private readonly int z;

                    internal MeshPositionCell(Vector3 position)
                    {
                        x = Mathf.FloorToInt(position.x / WeaponMeshIslandPositionTolerance);
                        y = Mathf.FloorToInt(position.y / WeaponMeshIslandPositionTolerance);
                        z = Mathf.FloorToInt(position.z / WeaponMeshIslandPositionTolerance);
                    }

                    private MeshPositionCell(int x, int y, int z)
                    {
                        this.x = x;
                        this.y = y;
                        this.z = z;
                    }

                    internal MeshPositionCell Offset(int offsetX, int offsetY, int offsetZ)
                    {
                        return new MeshPositionCell(x + offsetX, y + offsetY, z + offsetZ);
                    }

                    public bool Equals(MeshPositionCell other)
                    {
                        return x == other.x && y == other.y && z == other.z;
                    }

                    public override bool Equals(object obj)
                    {
                        return obj is MeshPositionCell other && Equals(other);
                    }

                    public override int GetHashCode()
                    {
                        unchecked
                        {
                            var hash = x;
                            hash = hash * 397 ^ y;
                            return hash * 397 ^ z;
                        }
                    }
                }

                private readonly struct UndirectedMeshEdge : IEquatable<UndirectedMeshEdge>
                {
                    private readonly int first;
                    private readonly int second;

                    internal UndirectedMeshEdge(int first, int second)
                    {
                        if (first <= second)
                        {
                            this.first = first;
                            this.second = second;
                        }
                        else
                        {
                            this.first = second;
                            this.second = first;
                        }
                    }

                    public bool Equals(UndirectedMeshEdge other)
                    {
                        return first == other.first && second == other.second;
                    }

                    public override bool Equals(object obj)
                    {
                        return obj is UndirectedMeshEdge other && Equals(other);
                    }

                    public override int GetHashCode()
                    {
                        unchecked
                        {
                            return first * 397 ^ second;
                        }
                    }
                }

                internal static void ValidateNoPhysics(GameObject root, string label)
                {
                    if (root.GetComponentsInChildren<Collider>(true).Length > 0 || root.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    {
                        throw new InvalidOperationException(label + " must not contain physics components.");
                    }
                }

                internal static void ValidateNoAnimators(GameObject root, string label)
                {
                    if (root.GetComponentsInChildren<Animator>(true).Length > 0)
                    {
                        throw new InvalidOperationException(label + " must not contain imported animators.");
                    }
                }

                internal static void ValidateShotgunPresentation(GameObject player, Camera camera, Transform fpsVisual,
                    Transform worldVisual, Transform worldMount, Transform worldShotgunVisual, string label)
                {
                    if (player == null || camera == null || fpsVisual == null || worldVisual == null || worldMount == null || worldShotgunVisual == null)
                        throw new InvalidOperationException(label + " shotgun presentation references are incomplete.");

                    var expectedFpsPosition = new Vector3(0.30f, -0.28f, 0.45f);
                    var viewmodels = camera.transform.Find("Viewmodels");
                    if (viewmodels == null || fpsVisual.parent != viewmodels || Vector3.Distance(fpsVisual.localPosition, expectedFpsPosition) > 0.001f ||
                        Quaternion.Angle(fpsVisual.localRotation, Quaternion.identity) > 0.001f || Vector3.Distance(fpsVisual.localScale, Vector3.one) > 0.001f ||
                        Vector3.Dot(fpsVisual.forward, camera.transform.forward) < 0.999f)
                    {
                        throw new InvalidOperationException(label + " FPS shotgun must be identity-mounted at camera +Z.");
                    }

                    var handR = FindNamedTransform(worldVisual, "Hand.R");
                    if (handR == null || worldMount.parent != handR || worldShotgunVisual.parent != worldMount ||
                        Vector3.Distance(worldMount.position, handR.position) > 0.001f ||
                        Vector3.Dot(worldMount.forward, player.transform.forward) < 0.999f ||
                        Quaternion.Angle(worldShotgunVisual.localRotation, Quaternion.identity) > 0.001f ||
                        Vector3.Distance(worldShotgunVisual.localScale, Vector3.one) > 0.001f ||
                        Vector3.Dot(worldShotgunVisual.forward, player.transform.forward) < 0.999f)
                    {
                        throw new InvalidOperationException(label + " world shotgun must preserve Hand.R bind position and player +Z.");
                    }

                    ValidateImportedVisualForward(fpsVisual, label + " FpsShotgunVisual");
                    ValidateImportedVisualForward(worldShotgunVisual, label + " WorldShotgunVisual");
                    ValidateDynamicHierarchy(fpsVisual.gameObject, label + " FpsShotgunVisual");
                    ValidateDynamicHierarchy(worldMount.gameObject, label + " WorldShotgunMount");
                }

                internal static void ValidateDynamicHierarchy(GameObject root, string label)
                {
                    var transforms = root.GetComponentsInChildren<Transform>(true);
                    for (var i = 0; i < transforms.Length; i++)
                    {
                        if (transforms[i] != null && transforms[i].gameObject.isStatic)
                            throw new InvalidOperationException(label + " hierarchy must remain dynamic: " + transforms[i].name);
                    }
                }

                internal static void ValidateTeamTintRenderers(PlayerPresentation presentation, Transform worldVisual,
                    Transform worldShotgunMount, Renderer worldShotgunAccent, Renderer worldShotgunAccentCore, string label)
                {
                    if (presentation == null || worldVisual == null || worldShotgunMount == null || worldShotgunAccent == null || worldShotgunAccentCore == null)
                        throw new InvalidOperationException(label + " references are incomplete.");
                    var expected = worldVisual.GetComponentsInChildren<Renderer>(true)
                        .Where(renderer => !renderer.transform.IsChildOf(worldShotgunMount)).ToArray();
                    var serialized = new SerializedObject(presentation);
                    var property = serialized.FindProperty("teamTintRenderers");
                    if (property == null || !property.isArray || property.arraySize != expected.Length)
                        throw new InvalidOperationException(label + " must contain world character renderers and exclude both shotgun accent renderers.");
                    for (var i = 0; i < expected.Length; i++)
                    {
                        if (property.GetArrayElementAtIndex(i).objectReferenceValue != expected[i])
                            throw new InvalidOperationException(label + " renderer routing mismatch at index " + i + ".");
                    }
                    for (var i = 0; i < property.arraySize; i++)
                    {
                        var value = property.GetArrayElementAtIndex(i).objectReferenceValue;
                        if (value == worldShotgunAccent || value == worldShotgunAccentCore)
                            throw new InvalidOperationException(label + " must exclude WorldShotgunVisual WeaponAccent and WeaponAccentCore.");
                    }
                }

                internal static void ValidateAnimatorController(Animator animator, string path, string modelPath)
                {
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    if (controller == null || AssetDatabase.GetAssetPath(controller) != path)
                    {
                        throw new InvalidOperationException("Animator controller provenance mismatch: " + path);
                    }
                    var hasKick = false;
                    for (var i = 0; i < controller.parameters.Length; i++) hasKick |= controller.parameters[i].name == "Kick" && controller.parameters[i].type == AnimatorControllerParameterType.Trigger;
                    if (!hasKick || controller.layers.Length == 0) throw new InvalidOperationException("Animator controller missing Kick trigger: " + path);
                    var stateMachine = controller.layers[0].stateMachine;
                    if (stateMachine.defaultState == null || stateMachine.defaultState.name != "Idle") throw new InvalidOperationException("Animator controller default state must be Idle: " + path);
                    var states = stateMachine.states;
                    var idle = false;
                    var kick = false;
                    AnimatorState idleState = null;
                    AnimatorState kickState = null;
                    for (var i = 0; i < states.Length; i++)
                    {
                        idle |= states[i].state.name == "Idle";
                        kick |= states[i].state.name == "Kick";
                        if (states[i].state.name == "Idle") idleState = states[i].state;
                        if (states[i].state.name == "Kick") kickState = states[i].state;
                    }
                    if (!idle || !kick || stateMachine.anyStateTransitions.Length == 0) throw new InvalidOperationException("Animator controller states/transitions incomplete: " + path);
                    var expectedIdle = FindImportedClip(modelPath, "Idle");
                    var expectedKick = FindImportedClip(modelPath, "Kick");
                    if (expectedIdle == null || expectedKick == null || expectedIdle == expectedKick || idleState.motion == null || kickState.motion == null || idleState.motion != expectedIdle || kickState.motion != expectedKick || idleState.motion == kickState.motion)
                    {
                        throw new InvalidOperationException("Animator controller clip bindings incomplete or non-distinct: " + path);
                    }
                    for (var i = 0; i < stateMachine.anyStateTransitions.Length; i++)
                    {
                        var transition = stateMachine.anyStateTransitions[i];
                        if (transition.destinationState != kickState || transition.duration > 0.03f || transition.conditions.Length == 0) throw new InvalidOperationException("Animator AnyState Kick transition invalid: " + path);
                    }
                    var kickToIdle = kickState.transitions;
                    var hasReturn = false;
                    for (var i = 0; i < kickToIdle.Length; i++) hasReturn |= kickToIdle[i].destinationState == idleState && kickToIdle[i].hasExitTime && Mathf.Abs(kickToIdle[i].exitTime - 1f) < 0.001f && kickToIdle[i].duration <= 0.03f;
                    if (!hasReturn) throw new InvalidOperationException("Animator Kick->Idle transition invalid: " + path);
                }

                internal static void ValidateDashAnimationCompatibility()
                {
                    var fpsImporter = AssetImporter.GetAtPath(FpsKickModelPath) as ModelImporter;
                    var worldImporter = AssetImporter.GetAtPath(CharacterModelPath) as ModelImporter;
                    if (fpsImporter == null || worldImporter == null)
                        throw new InvalidOperationException("Dash animation importers are missing.");

                    ModelImporterClipAnimation fpsKickSettings = null;
                    for (var i = 0; fpsImporter.clipAnimations != null && i < fpsImporter.clipAnimations.Length; i++)
                    {
                        if (fpsImporter.clipAnimations[i].name == "Kick")
                        {
                            fpsKickSettings = fpsImporter.clipAnimations[i];
                            break;
                        }
                    }
                    if (fpsKickSettings == null || Mathf.Abs(fpsKickSettings.firstFrame - 1f) > 0.001f || Mathf.Abs(fpsKickSettings.lastFrame - 11f) > 0.001f)
                        throw new InvalidOperationException("FPS Kick import must use frames 1..11.");

                    ModelImporterClipAnimation worldKickSettings = null;
                    for (var i = 0; worldImporter.clipAnimations != null && i < worldImporter.clipAnimations.Length; i++)
                    {
                        if (worldImporter.clipAnimations[i].name == "Kick")
                        {
                            worldKickSettings = worldImporter.clipAnimations[i];
                            break;
                        }
                    }
                    if (worldKickSettings == null || Mathf.Abs(worldKickSettings.firstFrame - 1f) > 0.001f || Mathf.Abs(worldKickSettings.lastFrame - 12f) > 0.001f)
                        throw new InvalidOperationException("World Kick import must use distinct frames 1..12.");

                    var fpsKickClip = FindImportedClip(FpsKickModelPath, "Kick");
                    var worldKickClip = FindImportedClip(CharacterModelPath, "Kick");

                    var fpsController = AssetDatabase.LoadAssetAtPath<AnimatorController>(FpsControllerPath);
                    if (fpsController == null || fpsController.layers.Length == 0)
                        throw new InvalidOperationException("FPS Kick controller is missing.");
                    var fpsStateMachine = fpsController.layers[0].stateMachine;
                    AnimatorState fpsKickState = null;
                    for (var i = 0; i < fpsStateMachine.states.Length; i++)
                    {
                        if (fpsStateMachine.states[i].state != null && fpsStateMachine.states[i].state.name == "Kick")
                        {
                            fpsKickState = fpsStateMachine.states[i].state;
                            break;
                        }
                    }
                    if (fpsKickState == null || fpsKickState.motion != fpsKickClip || Mathf.Abs(fpsKickState.speed - 1f) > 0.001f)
                        throw new InvalidOperationException("FPS Kick controller must bind Kick clip at speed 1.");
                    var hasFpsTriggerPath = false;
                    for (var i = 0; i < fpsStateMachine.anyStateTransitions.Length; i++)
                    {
                        var transition = fpsStateMachine.anyStateTransitions[i];
                        var conditions = transition.conditions;
                        if (transition.destinationState == fpsKickState && conditions != null && conditions.Length == 1 &&
                            conditions[0].mode == AnimatorConditionMode.If && conditions[0].parameter == "Kick")
                        {
                            hasFpsTriggerPath = true;
                            break;
                        }
                    }
                    if (!hasFpsTriggerPath)
                        throw new InvalidOperationException("FPS Kick controller trigger path must be AnyState -> Kick via Kick trigger.");

                    var worldController = AssetDatabase.LoadAssetAtPath<AnimatorController>(WorldControllerPath);
                    if (worldController == null || worldController.layers.Length == 0)
                        throw new InvalidOperationException("World character controller is missing.");
                    var worldStateMachine = worldController.layers[0].stateMachine;
                    AnimatorState worldKickState = null;
                    for (var i = 0; i < worldStateMachine.states.Length; i++)
                    {
                        if (worldStateMachine.states[i].state != null && worldStateMachine.states[i].state.name == "Kick")
                        {
                            worldKickState = worldStateMachine.states[i].state;
                            break;
                        }
                    }
                    if (worldKickState == null || worldKickState.motion != worldKickClip || worldKickState.motion == fpsKickClip)
                        throw new InvalidOperationException("World Kick controller must retain distinct imported motion.");
                }

                internal static void ValidateCrosshair(Camera camera)
                {
                    var canvasObject = Require(camera.transform.Find("CrosshairCanvas"), "CrosshairCanvas");
                    var canvas = Require(canvasObject.GetComponent<Canvas>(), "Crosshair Canvas");
                    if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera != camera || Mathf.Abs(canvas.planeDistance - 0.10f) > 0.001f || canvas.sortingOrder != 100)
                    {
                        throw new InvalidOperationException("Crosshair Canvas camera/render contract invalid.");
                    }
                    if (canvasObject.GetComponent<GraphicRaycaster>() != null) throw new InvalidOperationException("CrosshairCanvas must omit GraphicRaycaster.");
                    var scaler = Require(canvasObject.GetComponent<CanvasScaler>(), "Crosshair CanvasScaler");
                    if (scaler.referenceResolution != new Vector2(1920f, 1080f) || Mathf.Abs(scaler.matchWidthOrHeight - 0.5f) > 0.001f) throw new InvalidOperationException("Crosshair CanvasScaler contract invalid.");
                    var images = canvasObject.GetComponentsInChildren<Image>(true);
                    if (images.Length != 10) throw new InvalidOperationException("Crosshair must contain ten image bars/dots.");
                    for (var i = 0; i < images.Length; i++) if (images[i].raycastTarget) throw new InvalidOperationException("Crosshair image must not raycast: " + images[i].name);
                }

                internal static void ValidateTrail(GameObject rocketPrefab)
                {
                    if (rocketPrefab == null) throw new InvalidOperationException("Rocket prefab unavailable for trail validation.");
                    var trail = Require(rocketPrefab.GetComponentInChildren<RocketTrailVfx>(true), "RocketTrailVfx");
                    ValidateReference(trail, "blueImpactAccent", trail.transform.Find("BlueImpactRing").gameObject, "RocketTrailVfx.blueImpactAccent");
                    ValidateReference(trail, "redImpactAccent", trail.transform.Find("RedImpactTriangle").gameObject, "RocketTrailVfx.redImpactAccent");
                    var smokeSystems = trail.GetComponentsInChildren<ParticleSystem>(true);
                    if (smokeSystems.Length != 1) throw new InvalidOperationException("Rocket trail must contain one particle system.");
                    var serializedTrail = new SerializedObject(trail);
                    var configuredSystems = serializedTrail.FindProperty("particleSystems");
                    var configuredBlueMaterial = serializedTrail.FindProperty("blueTrailMaterial");
                    var configuredRedMaterial = serializedTrail.FindProperty("redTrailMaterial");
                    var expectedBlueMaterial = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueTrailMaterialPath);
                    var expectedRedMaterial = AssetDatabase.LoadAssetAtPath<Material>(TeamRedTrailMaterialPath);
                    if (configuredSystems == null || !configuredSystems.isArray || configuredSystems.arraySize != smokeSystems.Length ||
                        configuredSystems.GetArrayElementAtIndex(0).objectReferenceValue != smokeSystems[0] ||
                        configuredBlueMaterial == null || configuredRedMaterial == null ||
                        configuredBlueMaterial.objectReferenceValue != expectedBlueMaterial || configuredRedMaterial.objectReferenceValue != expectedRedMaterial ||
                        expectedBlueMaterial == null || expectedRedMaterial == null || expectedBlueMaterial == expectedRedMaterial)
                    {
                        throw new InvalidOperationException("Rocket trail team-color particle routing is incomplete.");
                    }
                    var system = smokeSystems[0];
                    var main = system.main;
                    var emission = system.emission;
                    var color = system.colorOverLifetime;
                    if (main.maxParticles > 48 || main.simulationSpace != ParticleSystemSimulationSpace.World ||
                        Mathf.Abs(main.startLifetime.constantMax - RocketTrailLifetime) > 0.01f ||
                        Mathf.Abs(main.startSize.constantMax - RocketTrailStartSize) > 0.01f ||
                        Mathf.Abs(emission.rateOverDistance.constantMax - RocketTrailRateOverDistance) > 0.01f ||
                        main.startSpeed.constantMax > 0.001f ||
                        !color.enabled || color.color.gradient == null || color.color.gradient.alphaKeys.Length < 2 || color.color.gradient.alphaKeys[0].alpha < 0.75f)
                    {
                        throw new InvalidOperationException("Rocket trail particle visibility contract invalid.");
                    }
                    var sheet = system.textureSheetAnimation;
                    if (!sheet.enabled || sheet.numTilesX != 4 || sheet.numTilesY != 4 || sheet.animation != ParticleSystemAnimationType.WholeSheet) throw new InvalidOperationException("Rocket smoke sheet contract invalid.");
                    var glow = Require(rocketPrefab.transform.Find("ProjectileGlow"), "ProjectileGlow");
                    var glowSystems = glow.GetComponentsInChildren<ParticleSystem>(true);
                    if (glowSystems.Length != 1) throw new InvalidOperationException("ProjectileGlow must contain one particle system.");
                    var glowSystem = glowSystems[0];
                    var glowMain = glowSystem.main;
                    var glowEmission = glowSystem.emission;
                    if (!glowMain.loop || !glowMain.prewarm || glowMain.simulationSpace != ParticleSystemSimulationSpace.Local ||
                        Mathf.Abs(glowMain.startLifetime.constantMax - 0.22f) > 0.01f || Mathf.Abs(glowMain.startSpeed.constantMax) > 0.001f ||
                        Mathf.Abs(glowMain.gravityModifier.constantMax) > 0.001f || Mathf.Abs(glowMain.startSize.constantMax - 0.85f) > 0.01f ||
                        glowMain.maxParticles != 2 || Mathf.Abs(glowEmission.rateOverTime.constantMax - 10f) > 0.01f || glowSystem.useAutoRandomSeed)
                    {
                        throw new InvalidOperationException("ProjectileGlow particle contract invalid.");
                    }
                    var glowMaterial = glowSystem.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    if (glowMaterial != AssetDatabase.LoadAssetAtPath<Material>(ProjectileGlowMaterialPath) || glowMaterial == null || glowMaterial.shader == null || glowMaterial.shader.name != "RocketFooxball/RetroAdditiveParticle" || Mathf.Abs(glowMaterial.GetFloat("_Intensity") - 2.5f) > 0.01f)
                    {
                        throw new InvalidOperationException("ProjectileGlow material contract invalid.");
                    }
                    var projectile = Require(rocketPrefab.GetComponent<RocketProjectile>(), "RocketProjectile");
                    ValidateReference(projectile, "trailVfx", trail, "RocketProjectile.trailVfx");
                    ValidateReference(trail, "projectileGlow", glowSystems[0], "RocketTrailVfx.projectileGlow");
                    ValidateReference(trail, "neutralTrailMaterial", glowMaterial, "RocketTrailVfx.neutralTrailMaterial");
                    if (rocketPrefab.GetComponentsInChildren<Light>(true).Length != 0) throw new InvalidOperationException("Rocket prefab must not contain Point Light components.");
                }

                internal static void ValidateExplosionPrefab(GameObject prefab)
                {
                    if (prefab == null) throw new InvalidOperationException("Explosion prefab unavailable.");
                    if (Vector3.Distance(prefab.transform.localScale, Vector3.one) > 0.001f)
                    {
                        throw new InvalidOperationException("Explosion VFX prefab root must remain unit scale; runtime owns radius scaling.");
                    }
                    if (Mathf.Abs(ExplosionVfx.ReferenceVisualRadius - 4.5f) > 0.001f ||
                        Mathf.Abs(ExplosionVfx.ReferenceVisualDiameter - 9f) > 0.001f ||
                        Mathf.Abs(ExplosionVfx.ComputeVisualScale(BlastRadius) - 2.6f) > 0.001f ||
                        Mathf.Abs(ExplosionVfx.ComputeVisualRadius(ExplosionVfx.ReferenceVisualDiameter, BlastRadius) - BlastRadius) > 0.001f)
                        throw new InvalidOperationException("Explosion VFX reference radius/reach contract invalid.");
                    var effect = Require(prefab.GetComponent<ExplosionVfx>(), "ExplosionVfx");
                    var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                    if (systems.Length != 5) throw new InvalidOperationException("Explosion VFX must contain exactly Flash/FireballBody/Sparks/Smoke/BlastRadiusCue systems.");
                    var emitted = 0;
                    ParticleSystem flash = null;
                    ParticleSystem fire = null;
                    ParticleSystem sparks = null;
                    ParticleSystem smoke = null;
                    ParticleSystem blastRadiusCue = null;
                    var systemNames = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < systems.Length; i++)
                    {
                        var system = systems[i];
                        if (system == null || !systemNames.Add(system.name)) throw new InvalidOperationException("Explosion VFX system names must be unique and exact.");
                        if (system.name == "Flash") flash = system;
                        else if (system.name == "FireballBody") fire = system;
                        else if (system.name == "Sparks") sparks = system;
                        else if (system.name == "Smoke") smoke = system;
                        else if (system.name == "BlastRadiusCue") blastRadiusCue = system;
                        else throw new InvalidOperationException("Unknown explosion system: " + system.name);
                        var emission = system.emission;
                        var bursts = new ParticleSystem.Burst[emission.burstCount];
                        emission.GetBursts(bursts);
                        for (var j = 0; j < bursts.Length; j++)
                        {
                            emitted += bursts[j].maxCount;
                        }
                        var main = system.main;
                        if (main.maxParticles > 40 || main.duration + main.startLifetime.constantMax > 1.25f) throw new InvalidOperationException("Explosion particle lifetime/max contract invalid: " + system.name);
                        var sheet = system.textureSheetAnimation;
                        if (!sheet.enabled || sheet.numTilesX != 4 || sheet.numTilesY != 4 || sheet.animation != ParticleSystemAnimationType.WholeSheet) throw new InvalidOperationException("Explosion texture-sheet contract invalid: " + system.name);
                    }
                    if (systemNames.Count != 5 || emitted != 38) throw new InvalidOperationException("Explosion VFX must contain the exact five systems and total 38 burst particles.");
                    if (flash == null || fire == null || sparks == null || smoke == null || blastRadiusCue == null ||
                        fire.emission.burstCount != 1 || sparks.emission.burstCount != 1 || smoke.emission.burstCount != 1 || blastRadiusCue.emission.burstCount != 1 ||
                        GetBurstParticleCount(fire) != 20 || GetBurstParticleCount(sparks) != 10 || GetBurstParticleCount(smoke) != 6 || GetBurstParticleCount(blastRadiusCue) != 1 ||
                        Mathf.Abs(flash.main.startLifetime.constantMax - 0.13f) > 0.01f || Mathf.Abs(fire.main.startLifetime.constantMin - 0.40f) > 0.01f ||
                        Mathf.Abs(fire.main.startLifetime.constantMax - 0.56f) > 0.01f || Mathf.Abs(sparks.main.startLifetime.constantMin - 0.20f) > 0.01f ||
                        Mathf.Abs(sparks.main.startLifetime.constantMax - 0.32f) > 0.01f || Mathf.Abs(smoke.main.startLifetime.constantMin - 0.62f) > 0.01f ||
                        Mathf.Abs(smoke.main.startLifetime.constantMax - 0.86f) > 0.01f ||
                        Mathf.Abs(flash.main.startSize.constantMax - 2.80f) > 0.01f || Mathf.Abs(fire.main.startSize.constantMax - 1.35f) > 0.01f ||
                        Mathf.Abs(sparks.main.startSize.constantMax - 0.10f) > 0.01f || Mathf.Abs(smoke.main.startSize.constantMax - 0.82f) > 0.01f ||
                        Mathf.Abs(fire.main.startSpeed.constantMin - 0.65f) > 0.01f || Mathf.Abs(fire.main.startSpeed.constantMax - 2.10f) > 0.01f ||
                        Mathf.Abs(sparks.main.startSpeed.constantMin - 7f) > 0.01f || Mathf.Abs(sparks.main.startSpeed.constantMax - 12f) > 0.01f ||
                        Mathf.Abs(smoke.main.startSpeed.constantMin - 0.5f) > 0.01f || Mathf.Abs(smoke.main.startSpeed.constantMax - 1.6f) > 0.01f ||
                        Mathf.Abs(fire.shape.radius - 0.06f) > 0.001f || fire.GetComponent<ParticleSystemRenderer>().sortingOrder != 0 || sparks.GetComponent<ParticleSystemRenderer>().sortingOrder != 2 || smoke.GetComponent<ParticleSystemRenderer>().sortingOrder != -1 || flash.GetComponent<ParticleSystemRenderer>().sortingOrder != 1 ||
                        fire.useAutoRandomSeed || sparks.useAutoRandomSeed || smoke.useAutoRandomSeed || flash.useAutoRandomSeed || fire.randomSeed != 0xF002u || sparks.randomSeed != 0xF003u || smoke.randomSeed != 0xF004u || flash.randomSeed != 0xF001u ||
                        fire.main.startSpeed.constantMin < 0f || sparks.main.startSpeed.constantMin < 0f || smoke.main.startSpeed.constantMin < 0f ||
                        fire.main.startSize.constantMax <= smoke.main.startSize.constantMax ||
                        !fire.colorOverLifetime.enabled || fire.colorOverLifetime.color.gradient == null ||
                        !smoke.colorOverLifetime.enabled || smoke.colorOverLifetime.color.gradient == null ||
                        fire.colorOverLifetime.color.gradient.alphaKeys.Length < 2 || smoke.colorOverLifetime.color.gradient.alphaKeys.Length < 2 ||
                        !GradientColorsMatch(flash.colorOverLifetime.color.gradient, new[] { new Color(1f, 1f, 0.78f, 1f), new Color(1f, 0.78f, 0.12f, 1f) }) ||
                        !GradientColorsMatch(fire.colorOverLifetime.color.gradient, new[] { new Color(1f, 1f, 0.82f, 1f), new Color(1f, 0.88f, 0.16f, 1f), new Color(1f, 0.62f, 0.04f, 1f) }) ||
                        !GradientColorsMatch(sparks.colorOverLifetime.color.gradient, new[] { new Color(1f, 1f, 0.78f, 1f), new Color(1f, 0.62f, 0.04f, 1f) }) ||
                        !GradientColorsMatch(smoke.colorOverLifetime.color.gradient, new[] { new Color(0.52f, 0.49f, 0.44f, 1f), new Color(0.20f, 0.19f, 0.18f, 1f) }))
                    {
                        throw new InvalidOperationException("Explosion VFX tuning contract invalid.");
                    }
                    var cueMain = blastRadiusCue.main;
                    var cueStartSpeed = cueMain.startSpeed;
                    var cueStartSize = cueMain.startSize;
                    var cueRenderer = blastRadiusCue.GetComponent<ParticleSystemRenderer>();
                    var cueSize = blastRadiusCue.sizeOverLifetime;
                    var cueSizeCurve = cueSize.size.curve;
                    var cueSizeKeys = cueSizeCurve == null ? Array.Empty<Keyframe>() : cueSizeCurve.keys;
                    var cueGradient = blastRadiusCue.colorOverLifetime.color.gradient;
                    var cueAlphaKeys = cueGradient == null ? Array.Empty<GradientAlphaKey>() : cueGradient.alphaKeys;
                    var cuePeakAlpha = 0f;
                    for (var i = 0; i < cueAlphaKeys.Length; i++) cuePeakAlpha = Mathf.Max(cuePeakAlpha, cueAlphaKeys[i].alpha);
                    if (cueMain.startLifetime.constantMin < 0.279f || cueMain.startLifetime.constantMax > 0.281f ||
                        cueStartSpeed.mode != ParticleSystemCurveMode.Constant || cueStartSpeed.constant != 0f ||
                        cueStartSize.mode != ParticleSystemCurveMode.Constant || Mathf.Abs(cueStartSize.constant - 9f) > 0.001f ||
                        Mathf.Abs(cueStartSize.constant - ExplosionVfx.ReferenceVisualDiameter) > 0.001f ||
                        Vector3.Distance(blastRadiusCue.transform.localPosition, Vector3.zero) > 0.001f || Quaternion.Angle(blastRadiusCue.transform.localRotation, Quaternion.identity) > 0.001f ||
                        Vector3.Distance(blastRadiusCue.transform.localScale, Vector3.one) > 0.001f ||
                        cueMain.maxParticles != 1 || cueMain.loop || cueMain.playOnAwake || !blastRadiusCue.emission.enabled ||
                        cueMain.simulationSpace != ParticleSystemSimulationSpace.Local || cueMain.scalingMode != ParticleSystemScalingMode.Hierarchy ||
                        !cueSize.enabled || cueSize.size.mode != ParticleSystemCurveMode.Curve || Mathf.Abs(cueSize.size.curveMultiplier - 1f) > 0.001f ||
                        cueSizeKeys.Length != 2 || Mathf.Abs(cueSizeKeys[0].time) > 0.001f || Mathf.Abs(cueSizeKeys[0].value - 0.15f) > 0.001f ||
                        Mathf.Abs(cueSizeKeys[1].time - 1f) > 0.001f || Mathf.Abs(cueSizeKeys[1].value - 1f) > 0.001f ||
                        cueRenderer == null || cueRenderer.renderMode != ParticleSystemRenderMode.HorizontalBillboard ||
                        cueRenderer.alignment != ParticleSystemRenderSpace.World || cueRenderer.sortingOrder != 3 ||
                        blastRadiusCue.useAutoRandomSeed || blastRadiusCue.randomSeed != 0xF005u || blastRadiusCue.shape.enabled ||
                        cueGradient == null || !GradientColorsMatch(cueGradient, new[] { Color.white, Color.white }) || cueAlphaKeys.Length < 2 ||
                        cueAlphaKeys[0].time > 0.001f || cueAlphaKeys[cueAlphaKeys.Length - 1].time < 0.999f ||
                        Mathf.Abs(cueAlphaKeys[cueAlphaKeys.Length - 1].alpha) > 0.001f || cuePeakAlpha > 0.22f)
                    {
                        throw new InvalidOperationException("Explosion BlastRadiusCue tuning contract invalid.");
                    }
                    if (prefab.GetComponentsInChildren<Collider>(true).Length != 0 || prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0)
                        throw new InvalidOperationException("Explosion VFX must not contain physics.");
                    if (prefab.GetComponentsInChildren<Light>(true).Length != 0) throw new InvalidOperationException("Explosion VFX must not contain lights.");
                    var flashMaterial = flash.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    var sparksMaterial = sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    var expectedFlashMaterial = AssetDatabase.LoadAssetAtPath<Material>(ExplosionAdditiveMaterialPath);
                    var expectedSparksMaterial = AssetDatabase.LoadAssetAtPath<Material>(ExplosionSparksMaterialPath);
                    var expectedExplosionMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Explosion.mat");
                    if (flashMaterial != expectedFlashMaterial || sparksMaterial != expectedSparksMaterial || fire.GetComponent<ParticleSystemRenderer>().sharedMaterial != expectedExplosionMaterial || blastRadiusCue.GetComponent<ParticleSystemRenderer>().sharedMaterial != expectedExplosionMaterial || smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Smoke.mat") ||
                        expectedExplosionMaterial == null || expectedExplosionMaterial.shader == null || expectedExplosionMaterial.shader.name != "RocketFooxball/RetroParticle" ||
                        flashMaterial == null || sparksMaterial == null || flashMaterial.shader == null || sparksMaterial.shader == null || flashMaterial.shader.name != "RocketFooxball/RetroAdditiveParticle" || sparksMaterial.shader.name != "RocketFooxball/RetroAdditiveParticle" || Mathf.Abs(flashMaterial.GetFloat("_Intensity") - 3.0f) > 0.001f || Mathf.Abs(sparksMaterial.GetFloat("_Intensity") - 2.0f) > 0.001f)
                    {
                        throw new InvalidOperationException("Explosion material routing/intensity contract invalid.");
                    }
                    var serialized = new SerializedObject(effect);
                    var configured = serialized.FindProperty("particleSystems");
                    if (configured == null || !configured.isArray || configured.arraySize != 5) throw new InvalidOperationException("ExplosionVfx.particleSystems must contain five systems.");
                    var expectedSystems = new[] { flash, fire, sparks, smoke, blastRadiusCue };
                    for (var i = 0; i < expectedSystems.Length; i++)
                    {
                        var expectedSystem = expectedSystems[i];
                        var configuredElement = configured.GetArrayElementAtIndex(i);
                        var configuredSystem = configuredElement.propertyType == SerializedPropertyType.ObjectReference
                            ? configuredElement.objectReferenceValue as ParticleSystem
                            : null;
                        if (configuredSystem == null || configuredSystem != expectedSystem)
                        {
                            throw new InvalidOperationException("ExplosionVfx.particleSystems[" + i + "] must reference the ordered " + expectedSystem.name + " system.");
                        }

                        ValidatePrefabReference(effect, "particleSystems.Array.data[" + i + "]", ExplosionPrefabPath,
                            "ExplosionVfx.particleSystems[" + i + "]");
                    }
                }

                internal static int GetBurstParticleCount(ParticleSystem system)
                {
                    var emission = system.emission;
                    var bursts = new ParticleSystem.Burst[emission.burstCount];
                    emission.GetBursts(bursts);
                    var count = 0;
                    for (var i = 0; i < bursts.Length; i++) count += bursts[i].maxCount;
                    return count;
                }

                private static void ValidateNullReference(UnityEngine.Object target, string propertyName, string label)
                {
                    var property = new SerializedObject(target).FindProperty(propertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != null)
                        throw new InvalidOperationException(label + " must remain null on the prefab.");
                }

                private static void ValidateSerializedBool(UnityEngine.Object target, string propertyName, bool expected, string label)
                {
                    var property = new SerializedObject(target).FindProperty(propertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.Boolean || property.boolValue != expected)
                        throw new InvalidOperationException(label + " tuning mismatch.");
                }

                internal static bool GradientColorsMatch(Gradient gradient, Color[] expected)
                {
                    if (gradient == null || expected == null || gradient.colorKeys == null || gradient.colorKeys.Length != expected.Length) return false;
                    var actual = gradient.colorKeys;
                    for (var i = 0; i < expected.Length; i++) if (Vector4.Distance(actual[i].color, expected[i]) > 0.01f) return false;
                    return true;
                }

    }
}
