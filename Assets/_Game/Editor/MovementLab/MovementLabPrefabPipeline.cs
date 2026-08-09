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
            RequireComponent<RocketFooxball.Runtime.Weapons.RocketLauncher>(MovementLabContract.RocketPrefabPath, "RocketLauncher");
            RequireComponent<RocketFooxball.Runtime.Feedback.ExplosionVfx>(MovementLabContract.ExplosionPrefabPath, "ExplosionVfx");
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
                    if (characterModel == null || fpsKickModel == null || weaponModel == null)
                    {
                        throw new InvalidOperationException("Missing imported character, FPS kick, or weapon model.");
                    }

                    var root = new GameObject("Player") { tag = "Player" };
                    var controller = root.AddComponent<CharacterController>();
                    controller.radius = 0.4f;
                    controller.height = 1.8f;
                    controller.center = new Vector3(0f, 0.9f, 0f);
                    controller.slopeLimit = 60f;
                    controller.stepOffset = 0.3f;
                    controller.skinWidth = 0.04f;

                    var input = root.AddComponent<PlayerInputReader>();
                    var motor = root.AddComponent<PlayerMotor>();
                    var look = root.AddComponent<PlayerLook>();
                    var feedback = root.AddComponent<PlayerCameraFeedback>();
                    var launcher = root.AddComponent<RocketLauncher>();
                    var kick = root.AddComponent<BallKick>();
                    var presentation = root.AddComponent<PlayerPresentation>();
                    var head = new GameObject("Head").transform;
                    head.SetParent(root.transform, false);
                    head.localPosition = new Vector3(0f, 1.55f, 0f);
                    var camera = new GameObject("Camera").AddComponent<Camera>();
                    camera.transform.SetParent(head, false);
                    camera.tag = "MainCamera";
                    camera.fieldOfView = 75f;
                    camera.nearClipPlane = 0.03f;
                    camera.farClipPlane = 180f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.72f, 0.88f, 0.96f, 1f);
                    camera.gameObject.AddComponent<AudioListener>();
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
                    var worldVisual = InstantiateImportedVisual(characterModel, "WorldVisual", root.transform, Vector3.zero, Quaternion.identity, Vector3.one);
                    AssignImportedMaterials(worldVisual, characterRed, characterBlack, characterCream, characterEye);
                    var worldAnimator = worldVisual.GetComponent<Animator>();
                    if (worldAnimator == null)
                    {
                        worldAnimator = worldVisual.AddComponent<Animator>();
                    }
                    worldAnimator.runtimeAnimatorController = EnsureWorldAnimatorController(WorldControllerPath, CharacterModelPath);
                    worldAnimator.avatar = FindImportedAvatar(CharacterModelPath);
                    worldAnimator.applyRootMotion = false;
                    // Hide the complete imported world model from the local player's camera.
                    // The imported eye/head and body meshes are separate branches, so hiding
                    // only CharacterHead leaves the rest of the model rendered in first person.
                    SetLayerRecursively(worldVisual, hiddenLayer);

                    var viewmodels = new GameObject("Viewmodels").transform;
                    viewmodels.SetParent(camera.transform, false);
                    viewmodels.localPosition = Vector3.zero;
                    viewmodels.localRotation = Quaternion.identity;
                    // Keep the launcher close enough that the camera crops its rear like a classic FPS viewmodel.
                    var weaponVisual = InstantiateImportedVisual(weaponModel, "WeaponVisual", viewmodels, new Vector3(0.28f, -0.22f, 0.34f), Quaternion.identity, Vector3.one);
                    var weaponMetal = GetOrCreateLitMaterial(new PbrMaterialSpecification("WeaponMetal", LoadTexture(WeaponMetalTexturePath), LoadTexture(WeaponMetalNormalTexturePath), LoadTexture(WeaponMetalMetallicTexturePath), LoadTexture(WeaponMetalOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponMetalBaseColor, Color.clear, 0f, 1f, 1f, 0.90f, 1f));
                    var weaponDark = GetOrCreateLitMaterial(new PbrMaterialSpecification("WeaponDark", LoadTexture(WeaponDarkTexturePath), LoadTexture(WeaponDarkNormalTexturePath), LoadTexture(WeaponDarkMetallicTexturePath), LoadTexture(WeaponDarkOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponDarkBaseColor, Color.clear, 0f, 1f, 1f, 0.90f, 1f));
                    var weaponAccent = GetOrCreateLitMaterial(new PbrMaterialSpecification("WeaponAccent", LoadTexture(WeaponAccentTexturePath), LoadTexture(WeaponAccentNormalTexturePath), LoadTexture(WeaponAccentMetallicTexturePath), LoadTexture(WeaponAccentOcclusionTexturePath), LoadTexture(WeaponAccentEmissionTexturePath), LoadTexture(DetailNormalTexturePath), Vector2.one, WeaponAccentBaseColor, new Color(1f, 0.16f, 0.03f, 1f), 1.5f, 1f, 1f, 0.90f, 1f));
                    AssignImportedMaterials(weaponVisual, weaponMetal, weaponDark, weaponAccent);
                    RemovePhysicsComponents(weaponVisual);
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
                    SetFloat(motor, "bhopSoftCapMultiplier", 2.5f);
                    // Keep gameplay tuning at the approved review baseline. Presentation
                    // changes must not silently retune movement or ball control.
                    SetFloat(motor, "jumpVelocity", JumpVelocity);
                    SetInteger(motor, "jumpsToHardCap", 4);
                    SetObjectReference(look, "input", input);
                    SetObjectReference(look, "head", head);
                    SetObjectReference(feedback, "player", motor);
                    SetObjectReference(feedback, "targetCamera", camera);
                    SetObjectReference(qualityRuntime, "targetCamera", camera);
                    SetObjectReference(launcher, "input", input);
                    SetObjectReference(launcher, "look", look);
                    SetObjectReference(launcher, "aimCamera", camera);
                    SetObjectReference(launcher, "spawnPoint", muzzle);
                    SetObjectReference(launcher, "projectilePrefab", rocketPrefab.GetComponent<RocketProjectile>());
                    SetFloat(launcher, "firingInterval", 0.70f);
                    SetObjectReference(kick, "input", input);
                    SetObjectReference(kick, "player", motor);
                    SetObjectReference(kick, "look", look);
                    SetObjectReference(kick, "aimCamera", camera);
                    SetFloat(kick, "kickRange", 3.00f);
                    SetFloat(kick, "contactReachPadding", 1.00f);
                    SetFloat(kick, "coneTotalDegrees", 35f);
                    SetFloat(kick, "cooldown", 0.40f);
                    SetFloat(kick, "inputBuffer", 0.50f);
                    SetFloat(kick, "speedFraction", 0.91f);
                    SetFloat(kick, "playerMomentumShare", 0.20f);
                    SetFloat(feedback, "baseFov", 75f);
                    SetFloat(feedback, "maxFov", 84f);
                    SetFloat(feedback, "celebrationOrbitRadius", CelebrationOrbitRadius);
                    SetFloat(feedback, "celebrationOrbitHeight", CelebrationOrbitHeight);
                    SetFloat(feedback, "celebrationLookHeight", CelebrationLookHeight);
                    SetFloat(feedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees);
                    SetFloat(feedback, "celebrationFov", CelebrationFov);
                    SetObjectReference(presentation, "kick", kick);
                    SetObjectReference(presentation, "motor", motor);
                    SetObjectReference(presentation, "launcher", launcher);
                    SetObjectReference(presentation, "worldAnimator", worldAnimator);
                    SetObjectReference(presentation, "fpsKickAnimator", fpsAnimator);
                    SetObjectReference(presentation, "weaponVisual", weaponVisual.transform);

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
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
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
                    SetObjectReference(projectile, "trailVfx", trailVfx);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
                    UnityEngine.Object.DestroyImmediate(root);
                    return prefab;
                }

                internal static ExplosionVfx BuildExplosionVfxPrefab()
                {
                    var root = new GameObject("ExplosionVfx");
                    // Keep blast readability aligned with the 30% gameplay radius increase.
                    root.transform.localScale = Vector3.one * BlastVisualScale;
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
                            var slotName = renderer.name + (j > 0 ? j.ToString() : string.Empty);
                            var chosen = materials.Length > 0 ? materials[0] : null;
                            if (slotName.IndexOf("Eye", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 3) chosen = materials[3];
                            else if (slotName.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 1) chosen = materials[1];
                            else if (slotName.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                            else if (slotName.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 0) chosen = materials[0];
                            else if (slotName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                            else if (slotName.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 1) chosen = materials[1];
                            else if (slotName.IndexOf("Cream", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                            else if (slotName.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0 || slotName.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0) chosen = materials.Length > 0 ? materials[0] : null;
                            if (chosen != null) slots[j] = chosen;
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

                internal static Transform FindNamedTransform(Transform root, string name)
                {
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
                    var layer = LayerMask.NameToLayer("LocalPlayerHidden");
                    if (layer >= 0) return layer;
                    var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                    if (settings.Length == 0) throw new InvalidOperationException("TagManager.asset unavailable.");
                    var serialized = new SerializedObject(settings[0]);
                    var layers = serialized.FindProperty("layers");
                    for (var i = 8; i < layers.arraySize; i++)
                    {
                        var item = layers.GetArrayElementAtIndex(i);
                        if (string.IsNullOrEmpty(item.stringValue))
                        {
                            item.stringValue = "LocalPlayerHidden";
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            AssetDatabase.SaveAssets();
                            return i;
                        }
                    }
                    throw new InvalidOperationException("No free user layer for LocalPlayerHidden.");
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
                            Require(root.GetComponent<CharacterController>(), "Player prefab CharacterController");
                            var input = Require(root.GetComponent<PlayerInputReader>(), "Player prefab PlayerInputReader");
                            var prefabMotor = Require(root.GetComponent<PlayerMotor>(), "Player prefab PlayerMotor");
                            ValidateReference(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath), "PlayerInputReader.actions");
                            var prefabLauncher = Require(root.GetComponent<RocketLauncher>(), "Player prefab RocketLauncher");
                            var prefabKick = Require(root.GetComponent<BallKick>(), "Player prefab BallKick");
                            var prefabFeedback = Require(root.GetComponent<PlayerCameraFeedback>(), "Player prefab PlayerCameraFeedback");
                            var prefabQualityRuntime = Require(root.transform.Find("Head/Camera").GetComponent<GraphicsQualityRuntime>(), "Player prefab GraphicsQualityRuntime");
                            var prefabPresentation = Require(root.GetComponent<PlayerPresentation>(), "Player prefab PlayerPresentation");
                            ValidateReference(prefabLauncher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "Player prefab RocketLauncher.projectilePrefab");
                            ValidateReference(prefabLauncher, "spawnPoint", root.transform.Find("Head/Camera/RocketMuzzle"), "Player prefab RocketLauncher.spawnPoint");
                            ValidateReference(prefabFeedback, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab PlayerCameraFeedback.targetCamera");
                            ValidateReference(prefabQualityRuntime, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab GraphicsQualityRuntime.targetCamera");
                            ValidateReference(prefabKick, "aimCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab BallKick.aimCamera");
                            ValidateReference(prefabPresentation, "kick", prefabKick, "Player prefab PlayerPresentation.kick");
                            ValidateSerializedFloat(prefabFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius, "Player prefab PlayerCameraFeedback.celebrationOrbitRadius");
                            ValidateSerializedFloat(prefabFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight, "Player prefab PlayerCameraFeedback.celebrationOrbitHeight");
                            ValidateSerializedFloat(prefabFeedback, "celebrationLookHeight", CelebrationLookHeight, "Player prefab PlayerCameraFeedback.celebrationLookHeight");
                            ValidateSerializedFloat(prefabFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees, "Player prefab PlayerCameraFeedback.celebrationOrbitDegrees");
                            ValidateSerializedFloat(prefabFeedback, "celebrationFov", CelebrationFov, "Player prefab PlayerCameraFeedback.celebrationFov");
                            ValidateSerializedFloat(prefabMotor, "jumpVelocity", JumpVelocity, "Player prefab PlayerMotor.jumpVelocity");
                            ValidateSerializedFloat(prefabKick, "kickRange", 3.00f, "Player prefab BallKick.kickRange");
                            ValidateSerializedFloat(prefabKick, "contactReachPadding", 1.00f, "Player prefab BallKick.contactReachPadding");
                            ValidateSerializedInteger(prefabMotor, "jumpsToHardCap", 4, "Player prefab PlayerMotor.jumpsToHardCap");
                             ValidateSerializedFloat(prefabKick, "speedFraction", 0.91f, "Player prefab BallKick.speedFraction");
                             var prefabCamera = root.transform.Find("Head/Camera").GetComponent<Camera>();
                             ValidateCrosshair(prefabCamera);
                             var prefabWeaponVisual = Require(root.transform.Find("Head/Camera/Viewmodels/WeaponVisual"), "Player prefab WeaponVisual");
                             ValidateWeaponMaterials(prefabWeaponVisual.gameObject);
                             ValidateNoPhysics(prefabWeaponVisual.gameObject, "Player prefab WeaponVisual");
                            ValidateNoPhysics(root.transform.Find("Head/Camera/Viewmodels/FpsKickVisual").gameObject, "Player prefab FpsKickVisual");
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
                        else if (path == RocketPrefabPath)
                        {
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
                    var renderers = visual.GetComponentsInChildren<Renderer>(true);
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

                internal static void ValidateNoPhysics(GameObject root, string label)
                {
                    if (root.GetComponentsInChildren<Collider>(true).Length > 0 || root.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    {
                        throw new InvalidOperationException(label + " must not contain physics components.");
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
                    var smokeSystems = trail.GetComponentsInChildren<ParticleSystem>(true);
                    if (smokeSystems.Length != 1) throw new InvalidOperationException("Rocket trail must contain one particle system.");
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
                    if (rocketPrefab.GetComponentsInChildren<Light>(true).Length != 0) throw new InvalidOperationException("Rocket prefab must not contain Point Light components.");
                }

                internal static void ValidateExplosionPrefab(GameObject prefab)
                {
                    if (prefab == null) throw new InvalidOperationException("Explosion prefab unavailable.");
                    if (Vector3.Distance(prefab.transform.localScale, Vector3.one * BlastVisualScale) > 0.001f)
                    {
                        throw new InvalidOperationException("Explosion VFX scale must track the enlarged blast radius.");
                    }
                    var effect = Require(prefab.GetComponent<ExplosionVfx>(), "ExplosionVfx");
                    var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                    if (systems.Length != 4) throw new InvalidOperationException("Explosion VFX must contain Flash/FireballBody/Sparks/Smoke systems.");
                    var emitted = 0;
                    ParticleSystem flash = null;
                    ParticleSystem fire = null;
                    ParticleSystem sparks = null;
                    ParticleSystem smoke = null;
                    for (var i = 0; i < systems.Length; i++)
                    {
                        var system = systems[i];
                        if (system.name == "Flash") flash = system;
                        else if (system.name == "FireballBody") fire = system;
                        else if (system.name == "Sparks") sparks = system;
                        if (system.name == "Smoke") smoke = system;
                        if (system.name != "Flash" && system.name != "FireballBody" && system.name != "Sparks" && system.name != "Smoke") throw new InvalidOperationException("Unknown explosion system: " + system.name);
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
                    if (emitted != 37) throw new InvalidOperationException("Explosion burst count must total 37.");
                    if (flash == null || fire == null || sparks == null || smoke == null ||
                        fire.emission.burstCount != 1 || sparks.emission.burstCount != 1 || smoke.emission.burstCount != 1 ||
                        GetBurstParticleCount(fire) != 20 || GetBurstParticleCount(sparks) != 10 || GetBurstParticleCount(smoke) != 6 ||
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
                    var renderer = prefab.GetComponentsInChildren<Renderer>(true);
                    for (var i = 0; i < renderer.Length; i++) if (renderer[i].GetComponent<Collider>() != null || renderer[i].GetComponent<Rigidbody>() != null) throw new InvalidOperationException("Explosion VFX must not contain physics.");
                    if (prefab.GetComponentsInChildren<Light>(true).Length != 0) throw new InvalidOperationException("Explosion VFX must not contain lights.");
                    var flashMaterial = flash.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    var sparksMaterial = sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                    var expectedFlashMaterial = AssetDatabase.LoadAssetAtPath<Material>(ExplosionAdditiveMaterialPath);
                    var expectedSparksMaterial = AssetDatabase.LoadAssetAtPath<Material>(ExplosionSparksMaterialPath);
                    if (flashMaterial != expectedFlashMaterial || sparksMaterial != expectedSparksMaterial || fire.GetComponent<ParticleSystemRenderer>().sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Explosion.mat") || smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Smoke.mat") ||
                        flashMaterial == null || sparksMaterial == null || flashMaterial.shader == null || sparksMaterial.shader == null || flashMaterial.shader.name != "RocketFooxball/RetroAdditiveParticle" || sparksMaterial.shader.name != "RocketFooxball/RetroAdditiveParticle" || Mathf.Abs(flashMaterial.GetFloat("_Intensity") - 3.0f) > 0.001f || Mathf.Abs(sparksMaterial.GetFloat("_Intensity") - 2.0f) > 0.001f)
                    {
                        throw new InvalidOperationException("Explosion material routing/intensity contract invalid.");
                    }
                    var serialized = new SerializedObject(effect);
                    var configured = serialized.FindProperty("particleSystems");
                    if (configured == null || !configured.isArray || configured.arraySize != 4) throw new InvalidOperationException("ExplosionVfx.particleSystems must contain four systems.");
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

                internal static bool GradientColorsMatch(Gradient gradient, Color[] expected)
                {
                    if (gradient == null || expected == null || gradient.colorKeys == null || gradient.colorKeys.Length != expected.Length) return false;
                    var actual = gradient.colorKeys;
                    for (var i = 0; i < expected.Length; i++) if (Vector4.Distance(actual[i].color, expected[i]) > 0.01f) return false;
                    return true;
                }

    }
}
