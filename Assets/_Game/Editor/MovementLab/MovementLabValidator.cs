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
    /// <summary>Ordered, stable-deduplicated collection for independent semantic checks.</summary>
    internal sealed class MovementLabValidationAccumulator
    {
        internal sealed class Violation
        {
            internal Violation(string scope, string check, string message)
            {
                Scope = scope ?? string.Empty;
                Check = check ?? string.Empty;
                Message = message ?? string.Empty;
            }

            internal string Scope { get; }
            internal string Check { get; }
            internal string Message { get; }
        }

        private readonly List<Violation> violations = new List<Violation>();
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);

        internal bool HasViolations => violations.Count > 0;
        internal IReadOnlyList<Violation> Violations => violations;

        internal void Add(string scope, string check, string message)
        {
            var normalizedScope = scope ?? string.Empty;
            var normalizedCheck = check ?? string.Empty;
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? "Validation failed." : message.Trim();
            var key = normalizedScope + "\u001f" + normalizedCheck + "\u001f" + normalizedMessage;
            if (!keys.Add(key)) return;
            violations.Add(new Violation(normalizedScope, normalizedCheck, normalizedMessage));
        }

        internal void Capture(string scope, string check, Action validation)
        {
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            try
            {
                validation();
            }
            catch (InvalidOperationException exception)
            {
                Add(scope, check, exception.Message);
            }
            catch (ArgumentException exception) when (IsExpectedArgumentContractException(exception))
            {
                Add(scope, check, exception.Message);
            }
        }

        internal void Capture(string scope, string check, Func<bool> validation)
        {
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            Capture(scope, check, () =>
            {
                if (!validation()) throw new InvalidOperationException("Validation predicate returned false.");
            });
        }

        internal void ThrowIfAny(string operation)
        {
            if (!HasViolations) return;
            var prefix = string.IsNullOrWhiteSpace(operation) ? "MovementLab validation" : operation.Trim();
            var lines = violations.Select((violation, index) =>
                (index + 1) + ". [" + violation.Scope + "/" + violation.Check + "] " + violation.Message);
            throw new InvalidOperationException(prefix + " failed with " + violations.Count + " validation violation(s):\n" + string.Join("\n", lines.ToArray()));
        }

        private static bool IsExpectedArgumentContractException(ArgumentException exception)
        {
            // Only path/property contract arguments are expected validation
            // failures. Unclassified argument/runtime faults propagate.
            if (exception == null) return false;
            var parameter = exception.ParamName;
            return string.Equals(parameter, "path", StringComparison.Ordinal) ||
                string.Equals(parameter, "assetPath", StringComparison.Ordinal) ||
                string.Equals(parameter, "propertyName", StringComparison.Ordinal) ||
                string.Equals(parameter, "target", StringComparison.Ordinal) ||
                string.Equals(parameter, "expected", StringComparison.Ordinal);
        }
    }

    internal static partial class MovementLabValidator
    {
        internal static void Validate(bool includeBakedLighting, bool logSuccess)
        {
            var accumulator = new MovementLabValidationAccumulator();
            Validate(ComputeBuilderSignature(), includeBakedLighting, logSuccess, accumulator);
            accumulator.ThrowIfAny("MovementLab validation");
        }

        internal static void Validate(MovementLabValidationAccumulator accumulator, bool includeBakedLighting, bool logSuccess)
        {
            Validate(ComputeBuilderSignature(), includeBakedLighting, logSuccess, accumulator);
        }

        private static void Validate(string builderSignature, bool includeBakedLighting, bool logSuccess,
            MovementLabValidationAccumulator accumulator)
        {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));
            ValidateMovementLabInternal(builderSignature, includeBakedLighting, logSuccess, accumulator);
            // Full and fast entrypoints share these checks with the core semantic
            // sections. No duplicate post-core first-fail calls.
        }

        internal static void ValidatePreBakeSemantics()
        {
            var accumulator = new MovementLabValidationAccumulator();
            ValidatePreBakeSemantics(accumulator);
            accumulator.ThrowIfAny("MovementLab pre-bake semantic validation");
        }

        internal static void ValidatePreBakeSemantics(MovementLabValidationAccumulator accumulator)
        {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));
            ValidateMovementLabInternal(ComputeBuilderSignature(), false, false, accumulator);
        }

        /// <summary>
        /// Fast preview validation is persisted/read-only semantic coverage. It
        /// deliberately excludes review markers, pass records, baked-output
        /// proof, capture, and any writer/repair path.
        /// </summary>
        internal static void ValidateFastPersistedSemantics()
        {
            var accumulator = new MovementLabValidationAccumulator();
            ValidateFastPersistedSemantics(accumulator);
            accumulator.ThrowIfAny("MovementLab fast persisted semantic validation");
        }

        internal static void ValidateFastPersistedSemantics(MovementLabValidationAccumulator accumulator)
        {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));
            ValidateMovementLabInternal(ComputeBuilderSignature(), false, false, accumulator);
        }
    }

    internal static partial class MovementLabValidator
    {
        private static readonly string[] RequiredCustomShaderPaths =
        {
            ToonShaderPath,
            ParticleShaderPath,
            AdditiveParticleShaderPath,
            PowerGridShaderPath,
            ShieldShaderPath,
            SkyShaderPath
        };

        private sealed class ValidationContext
        {
            internal Scene Scene;
            internal bool SceneReady;
            internal GameObject Arena;
            internal GameObject Player;
            internal GameObject Ball;
            internal GameObject MatchObject;
            internal GameObject ExplosionObject;
            internal GameObject ShieldSetObject;
            internal GameObject HudObject;
            internal PlayerMotor PlayerMotor;
            internal PlayerInputReader Input;
            internal PlayerLook Look;
            internal PlayerCameraFeedback CameraFeedback;
            internal RocketLauncher Launcher;
            internal BallKick Kick;
            internal Camera Camera;
            internal GraphicsQualityRuntime QualityRuntime;
            internal BallMotor BallMotor;
            internal Rigidbody BallBody;
            internal Collider BallCollider;
            internal PhysicsMaterial BallSurface;
            internal ExplosionResolver Resolver;
            internal ExplosionVfxSpawner ExplosionVfxSpawner;
            internal GoalShieldSet GoalShieldSet;
            internal MatchController Match;
            internal MovementDebugHud Hud;
            internal GoalTrigger North;
            internal GoalTrigger South;
            internal Collider NorthShield;
            internal Collider SouthShield;
            internal PlayerPresentation Presentation;
            internal Transform Head;
            internal Transform RocketMuzzle;
            internal Transform WorldVisual;
            internal Animator WorldAnimator;
            internal Transform Viewmodels;
            internal Transform WeaponVisual;
            internal Transform FpsVisual;
            internal Animator FpsAnimator;
        }

        internal static void ValidateMovementLabInternal(string builderSignature, bool includeBakedLighting, bool logSuccess)
        {
            var accumulator = new MovementLabValidationAccumulator();
            ValidateMovementLabInternal(builderSignature, includeBakedLighting, logSuccess, accumulator);
            accumulator.ThrowIfAny("MovementLab semantic validation");
        }

        internal static void ValidateMovementLabInternal(string builderSignature, bool includeBakedLighting, bool logSuccess,
            MovementLabValidationAccumulator accumulator)
        {
            if (accumulator == null) throw new ArgumentNullException(nameof(accumulator));
            var availableAssets = ValidateAssetPrerequisites(accumulator);
            var context = CaptureScenePrerequisites(builderSignature, accumulator, availableAssets);
            ValidateGameplayAndSerializedWiring(context, accumulator);
            ValidateImportedVisualAndAnimatorContracts(context, accumulator, availableAssets);
            ValidateMaterialImporterAndPrefabContracts(context, accumulator, availableAssets);
            ValidateArenaContracts(context, accumulator);
            ValidateLightingAndProjectContracts(context, includeBakedLighting, accumulator);
            if (context.SceneReady)
                ValidateNoMissingComponents(context.Scene, accumulator);

            if (logSuccess && !accumulator.HasViolations)
                Debug.Log("Rocket Fooxball Movement Lab validation succeeded: " + ScenePath);
        }

        private static HashSet<string> ValidateAssetPrerequisites(MovementLabValidationAccumulator accumulator)
        {
            var availableAssets = new HashSet<string>(StringComparer.Ordinal);
            var paths = new[]
            {
                PrefabPath, BallPrefabPath, RocketPrefabPath, RocketModelPath, ArenaKitModelPath, CharacterModelPath,
                FpsKickModelPath, WeaponModelPath, GrassTexturePath, GrassNormalTexturePath, GrassMetallicTexturePath,
                GrassOcclusionTexturePath, BallTexturePath, BallNormalTexturePath, BallMetallicTexturePath,
                BallOcclusionTexturePath, WeaponMetalTexturePath, WeaponMetalNormalTexturePath, WeaponMetalMetallicTexturePath,
                WeaponMetalOcclusionTexturePath, WeaponDarkTexturePath, WeaponDarkNormalTexturePath, WeaponDarkMetallicTexturePath,
                WeaponDarkOcclusionTexturePath, WeaponAccentTexturePath, WeaponAccentNormalTexturePath,
                WeaponAccentMetallicTexturePath, WeaponAccentOcclusionTexturePath, WeaponAccentEmissionTexturePath,
                RocketTexturePath, RocketNormalTexturePath, RocketMetallicTexturePath, RocketOcclusionTexturePath,
                RocketEmissionTexturePath, RocketGlowTexturePath, ExplosionTexturePath, SmokeTexturePath, SkyTexturePath,
                SkyShaderPath, DetailNormalTexturePath, ToonShaderPath, ParticleShaderPath, AdditiveParticleShaderPath,
                PowerGridShaderPath, ShieldShaderPath, WallTexturePath, TrimTexturePath, HazardTexturePath,
                ShieldTexturePath, WorldControllerPath, FpsControllerPath, ExplosionPrefabPath, ScenePath, BallSurfacePath,
                RocketHotMaterialPath, ProjectileGlowMaterialPath, ExplosionAdditiveMaterialPath, ExplosionSparksMaterialPath,
                GridCeilingMaterialPath, GridLongWallMaterialPath, GridEndWallMaterialPath, SkyMaterialPath,
                VolumeProfilePath, LightingSettingsPath, LightingManifestPath
            };
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (File.Exists(path)) availableAssets.Add(path);
                accumulator.Capture("assets", "exists:" + path, () => EnsureAssetExists(path));
            }
            ValidateCustomShaders(accumulator);
            return availableAssets;
        }

        private static ValidationContext CaptureScenePrerequisites(string builderSignature,
            MovementLabValidationAccumulator accumulator, ISet<string> availableAssets)
        {
            var context = new ValidationContext();
            if (availableAssets == null || !availableAssets.Contains(ScenePath)) return context;
            accumulator.Capture("scene/root", "open-scene", () =>
            {
                context.Scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (!context.Scene.IsValid() || context.Scene.path != ScenePath)
                    throw new InvalidOperationException("MovementLab scene failed to reopen: " + ScenePath);
                context.SceneReady = true;
            });
            if (!context.SceneReady) return context;

            context.Arena = CaptureRequired(accumulator, "scene/root", "Arena", GameObject.Find("Arena"), "Arena root");
            context.Player = CaptureRequired(accumulator, "scene/root", "Player", GameObject.Find("Player"), "Player root");
            context.Ball = CaptureRequired(accumulator, "scene/root", "Ball", GameObject.Find("Ball"), "Ball root");
            context.MatchObject = CaptureRequired(accumulator, "scene/root", "MatchController", GameObject.Find("MatchController"), "MatchController root");
            context.ExplosionObject = CaptureRequired(accumulator, "scene/root", "ExplosionResolver", GameObject.Find("ExplosionResolver"), "ExplosionResolver root");
            context.ShieldSetObject = CaptureRequired(accumulator, "scene/root", "GoalShieldSet", GameObject.Find("GoalShieldSet"), "GoalShieldSet root");
            context.HudObject = CaptureRequired(accumulator, "scene/root", "DebugHUD", GameObject.Find("DebugHUD"), "DebugHUD root");
            accumulator.Capture("scene/root", "build-marker", () => Require(GameObject.Find(GetBuildMarkerName(builderSignature)), "T5 build marker"));

            if (context.Player != null)
            {
                context.PlayerMotor = CaptureRequired(accumulator, "scene/player-components", "PlayerMotor", context.Player.GetComponent<PlayerMotor>(), "PlayerMotor");
                context.Input = CaptureRequired(accumulator, "scene/player-components", "PlayerInputReader", context.Player.GetComponent<PlayerInputReader>(), "PlayerInputReader");
                context.Look = CaptureRequired(accumulator, "scene/player-components", "PlayerLook", context.Player.GetComponent<PlayerLook>(), "PlayerLook");
                context.CameraFeedback = CaptureRequired(accumulator, "scene/player-components", "PlayerCameraFeedback", context.Player.GetComponent<PlayerCameraFeedback>(), "PlayerCameraFeedback");
                context.Launcher = CaptureRequired(accumulator, "scene/player-components", "RocketLauncher", context.Player.GetComponent<RocketLauncher>(), "RocketLauncher");
                context.Kick = CaptureRequired(accumulator, "scene/player-components", "BallKick", context.Player.GetComponent<BallKick>(), "BallKick");
                context.Camera = CaptureRequired(accumulator, "scene/player-components", "PlayerCamera", context.Player.GetComponentInChildren<Camera>(true), "Player camera");
                if (context.Camera != null)
                    context.QualityRuntime = CaptureRequired(accumulator, "scene/player-components", "GraphicsQualityRuntime", context.Camera.GetComponent<GraphicsQualityRuntime>(), "GraphicsQualityRuntime");
                accumulator.Capture("scene/player-components", "character-controller", () => Require(context.Player.GetComponent<CharacterController>(), "Player CharacterController"));
                accumulator.Capture("scene/player-contract", "spawn", () =>
                {
                    if (Vector3.Distance(context.Player.transform.position, new Vector3(PlayerSpawnOffset, 0f, 0f)) > 0.001f ||
                        Vector3.Dot(context.Player.transform.forward, Vector3.left) < 0.999f)
                        throw new InvalidOperationException("Player spawn must be neutral midfield offset on goal axis facing centered ball.");
                });
                context.Head = context.Player.transform.Find("Head");
                context.RocketMuzzle = context.Player.transform.Find("Head/Camera/RocketMuzzle");
            }

            if (context.Camera != null)
            {
                accumulator.Capture("scene/render", "gameplay-camera", () =>
                {
                    var camera = context.Camera;
                    if (camera.clearFlags != CameraClearFlags.SolidColor || Mathf.Abs(camera.backgroundColor.r - 0.72f) > 0.001f ||
                        Mathf.Abs(camera.backgroundColor.g - 0.88f) > 0.001f || Mathf.Abs(camera.backgroundColor.b - 0.96f) > 0.001f ||
                        Mathf.Abs(camera.fieldOfView - 75f) > 0.001f || Mathf.Abs(camera.farClipPlane - 180f) > 0.01f)
                        throw new InvalidOperationException("Gameplay camera bright-scene contract invalid.");
                });
            }
            accumulator.Capture("scene/render", "scene-environment", () =>
            {
                if (RenderSettings.skybox == null || RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Skybox ||
                    !RenderSettings.fog || Mathf.Abs(RenderSettings.fogStartDistance - 75f) > 0.01f ||
                    Mathf.Abs(RenderSettings.fogEndDistance - 170f) > 0.01f)
                    throw new InvalidOperationException("Scene environment contract invalid.");
            });

            if (context.Ball != null)
            {
                context.BallMotor = CaptureRequired(accumulator, "scene/ball-components", "BallMotor", context.Ball.GetComponent<BallMotor>(), "BallMotor");
                context.BallBody = CaptureRequired(accumulator, "scene/ball-components", "BallRigidbody", context.Ball.GetComponent<Rigidbody>(), "Ball Rigidbody");
                context.BallCollider = CaptureRequired(accumulator, "scene/ball-components", "BallCollider", context.Ball.GetComponent<Collider>(), "Ball collider");
                context.BallSurface = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallSurfacePath);
                if (context.BallBody != null)
                    accumulator.Capture("scene/ball-contract", "rigidbody", () =>
                    {
                        if (context.BallBody.isKinematic || !context.BallBody.useGravity || context.BallBody.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
                            throw new InvalidOperationException("Ball Rigidbody must be dynamic, gravity-enabled, and ContinuousDynamic.");
                    });
                if (context.BallCollider != null)
                    accumulator.Capture("scene/ball-contract", "surface", () =>
                    {
                        if (context.BallSurface == null || context.BallCollider.sharedMaterial != context.BallSurface)
                            throw new InvalidOperationException("Ball collider is missing shared BallSurface material.");
                    });
                accumulator.Capture("scene/ball-contract", "spawn", () =>
                {
                    if (Vector3.Distance(context.Ball.transform.position, new Vector3(0f, BallSpawnHeight, 0f)) > 0.001f)
                        throw new InvalidOperationException("Ball spawn/reset height must match the enlarged ball radius.");
                });
            }

            if (context.ExplosionObject != null)
            {
                context.Resolver = CaptureRequired(accumulator, "scene/gameplay-components", "ExplosionResolver", context.ExplosionObject.GetComponent<ExplosionResolver>(), "ExplosionResolver");
                context.ExplosionVfxSpawner = CaptureRequired(accumulator, "scene/gameplay-components", "ExplosionVfxSpawner", context.ExplosionObject.GetComponent<ExplosionVfxSpawner>(), "ExplosionVfxSpawner");
            }
            if (context.ShieldSetObject != null)
                context.GoalShieldSet = CaptureRequired(accumulator, "scene/gameplay-components", "GoalShieldSet", context.ShieldSetObject.GetComponent<GoalShieldSet>(), "GoalShieldSet");
            if (context.MatchObject != null)
                context.Match = CaptureRequired(accumulator, "scene/gameplay-components", "MatchController", context.MatchObject.GetComponent<MatchController>(), "MatchController");
            if (context.HudObject != null)
                context.Hud = CaptureRequired(accumulator, "scene/gameplay-components", "MovementDebugHud", context.HudObject.GetComponent<MovementDebugHud>(), "MovementDebugHud");

            if (context.Resolver != null)
            {
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.blastRadius", context.Resolver, "blastRadius", BlastRadius);
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.underfootForwardImpulseScale", context.Resolver, "underfootForwardImpulseScale", UnderfootForwardImpulseScale);
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.underfootUpwardImpulseScale", context.Resolver, "underfootUpwardImpulseScale", UnderfootUpwardImpulseScale);
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.underfootHighSpeedVerticalRedirect", context.Resolver, "underfootHighSpeedVerticalRedirect", UnderfootHighSpeedVerticalRedirect);
            }

            var goals = UnityEngine.Object.FindObjectsByType<GoalTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            accumulator.Capture("scene/goals", "count", () =>
            {
                if (goals.Length != 2) throw new InvalidOperationException("MovementLab must contain exactly two GoalTrigger components.");
            });
            for (var i = 0; i < goals.Length; i++)
            {
                var trigger = goals[i];
                if (trigger == null) continue;
                var triggerCollider = CaptureRequired(accumulator, "scene/goals", trigger.name + ".collider", trigger.GetComponent<Collider>(), trigger.name + " goal collider");
                if (triggerCollider != null)
                    accumulator.Capture("scene/goals", trigger.name + ".trigger", () =>
                    {
                        if (!triggerCollider.isTrigger) throw new InvalidOperationException(trigger.name + " goal plane must be a trigger collider.");
                    });
                if (trigger.Side == GoalTrigger.GoalSide.North)
                {
                    if (context.North != null) accumulator.Add("scene/goals", "duplicate-north", "Duplicate North goal.");
                    else context.North = trigger;
                }
                else
                {
                    if (context.South != null) accumulator.Add("scene/goals", "duplicate-south", "Duplicate South goal.");
                    else context.South = trigger;
                }
            }
            context.North = CaptureRequired(accumulator, "scene/goals", "north", context.North, "North goal");
            context.South = CaptureRequired(accumulator, "scene/goals", "south", context.South, "South goal");
            if (context.North != null && context.South != null)
            {
                accumulator.Capture("scene/goals", "orientation", () =>
                {
                    if (Vector3.Distance(context.North.transform.position, new Vector3(-GoalAxisPosition, 0f, 0f)) > 0.01f ||
                        Vector3.Distance(context.South.transform.position, new Vector3(GoalAxisPosition, 0f, 0f)) > 0.01f ||
                        Vector3.Dot(context.North.transform.forward, Vector3.left) < 0.999f ||
                        Vector3.Dot(context.South.transform.forward, Vector3.right) < 0.999f)
                        throw new InvalidOperationException("Goals must face across longest arena axis at opposite furthest walls.");
                });
                CaptureSerializedVector(accumulator, "scene/goals", "NorthGoal.planeNormal", context.North, "planeNormal", Vector3.right);
                CaptureSerializedVector(accumulator, "scene/goals", "SouthGoal.planeNormal", context.South, "planeNormal", Vector3.right);
                var northShieldTransform = CaptureRequired(accumulator, "scene/goals", "north-shield-transform", context.North.transform.Find("ShieldCollider"), "North goal ShieldCollider");
                var southShieldTransform = CaptureRequired(accumulator, "scene/goals", "south-shield-transform", context.South.transform.Find("ShieldCollider"), "South goal ShieldCollider");
                if (northShieldTransform != null)
                    context.NorthShield = CaptureRequired(accumulator, "scene/goals", "north-shield-collider", northShieldTransform.GetComponent<Collider>(), "North goal shield collider");
                if (southShieldTransform != null)
                    context.SouthShield = CaptureRequired(accumulator, "scene/goals", "south-shield-collider", southShieldTransform.GetComponent<Collider>(), "South goal shield collider");
                if (context.NorthShield != null && context.SouthShield != null)
                    accumulator.Capture("scene/goals", "shield-colliders", () =>
                    {
                        if (context.NorthShield.isTrigger || context.SouthShield.isTrigger)
                            throw new InvalidOperationException("Goal shields must block player/rocket with non-trigger colliders.");
                    });
            }
            return context;
        }

        private static void ValidateGameplayAndSerializedWiring(ValidationContext context,
            MovementLabValidationAccumulator accumulator)
        {
            if (context == null || !context.SceneReady) return;
            if (context.BallMotor != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.body", context.BallMotor, "body", context.BallBody);
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.ballCollider", context.BallMotor, "ballCollider", context.BallCollider);
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.player", context.BallMotor, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.goalShieldSet", context.BallMotor, "goalShieldSet", context.GoalShieldSet);
            }
            if (context.GoalShieldSet != null && context.NorthShield != null && context.SouthShield != null)
                accumulator.Capture("gameplay/wiring", "GoalShieldSet.colliders", () => ValidateArrayContains(context.GoalShieldSet, "colliders", context.NorthShield, context.SouthShield, "GoalShieldSet.colliders"));
            if (context.Input != null)
                CaptureReference(accumulator, "gameplay/wiring", "PlayerInputReader.actions", context.Input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath));
            if (context.PlayerMotor != null) CaptureReference(accumulator, "gameplay/wiring", "PlayerMotor.input", context.PlayerMotor, "input", context.Input);
            if (context.Look != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "PlayerLook.input", context.Look, "input", context.Input);
                CaptureReference(accumulator, "gameplay/wiring", "PlayerLook.head", context.Look, "head", context.Head);
            }
            if (context.Launcher != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.input", context.Launcher, "input", context.Input);
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.look", context.Launcher, "look", context.Look);
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.aimCamera", context.Launcher, "aimCamera", context.Camera);
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.spawnPoint", context.Launcher, "spawnPoint", context.RocketMuzzle);
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.projectilePrefab", context.Launcher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath));
                CaptureReference(accumulator, "gameplay/wiring", "RocketLauncher.explosionResolver", context.Launcher, "explosionResolver", context.Resolver);
            }
            if (context.CameraFeedback != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "PlayerCameraFeedback.player", context.CameraFeedback, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "PlayerCameraFeedback.targetCamera", context.CameraFeedback, "targetCamera", context.Camera);
                var viewmodelsObject = context.Player != null ? context.Player.transform.Find("Head/Camera/Viewmodels")?.gameObject : null;
                var crosshairObject = context.Player != null ? context.Player.transform.Find("Head/Camera/CrosshairCanvas")?.gameObject : null;
                CaptureReference(accumulator, "gameplay/wiring", "PlayerCameraFeedback.viewmodels", context.CameraFeedback, "viewmodels", viewmodelsObject);
                CaptureReference(accumulator, "gameplay/wiring", "PlayerCameraFeedback.crosshairCanvas", context.CameraFeedback, "crosshairCanvas", crosshairObject);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitRadius", context.CameraFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitHeight", context.CameraFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationLookHeight", context.CameraFeedback, "celebrationLookHeight", CelebrationLookHeight);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitDegrees", context.CameraFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationFov", context.CameraFeedback, "celebrationFov", CelebrationFov);
            }
            if (context.QualityRuntime != null)
                CaptureReference(accumulator, "gameplay/wiring", "GraphicsQualityRuntime.targetCamera", context.QualityRuntime, "targetCamera", context.Camera);
            if (context.Kick != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "BallKick.input", context.Kick, "input", context.Input);
                CaptureReference(accumulator, "gameplay/wiring", "BallKick.player", context.Kick, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "BallKick.look", context.Kick, "look", context.Look);
                CaptureReference(accumulator, "gameplay/wiring", "BallKick.aimCamera", context.Kick, "aimCamera", context.Camera);
                CaptureReference(accumulator, "gameplay/wiring", "BallKick.ball", context.Kick, "ball", context.BallMotor);
            }
            if (context.Resolver != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "ExplosionResolver.goalShieldSet", context.Resolver, "goalShieldSet", context.GoalShieldSet);
                CaptureReference(accumulator, "gameplay/wiring", "ExplosionResolver.explosionVfxSpawner", context.Resolver, "explosionVfxSpawner", context.ExplosionVfxSpawner);
                if (context.ExplosionVfxSpawner != null)
                    accumulator.Capture("gameplay/wiring", "ExplosionVfxSpawner.explosionVfxPrefab", () => ValidatePrefabReference(context.ExplosionVfxSpawner, "explosionVfxPrefab", ExplosionPrefabPath, "ExplosionVfxSpawner.explosionVfxPrefab"));
            }

            if (context.North != null)
            {
                CaptureReference(accumulator, "gameplay/goals", "NorthGoal.ball", context.North, "ball", context.BallMotor);
                CaptureReference(accumulator, "gameplay/goals", "NorthGoal.planeReference", context.North, "planeReference", context.North.transform);
                CaptureReference(accumulator, "gameplay/goals", "NorthGoal.openingTrigger", context.North, "openingTrigger", context.North.GetComponent<Collider>());
            }
            if (context.South != null)
            {
                CaptureReference(accumulator, "gameplay/goals", "SouthGoal.ball", context.South, "ball", context.BallMotor);
                CaptureReference(accumulator, "gameplay/goals", "SouthGoal.planeReference", context.South, "planeReference", context.South.transform);
                CaptureReference(accumulator, "gameplay/goals", "SouthGoal.openingTrigger", context.South, "openingTrigger", context.South.GetComponent<Collider>());
            }
            if (context.Match != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.input", context.Match, "input", context.Input);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.player", context.Match, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.playerLook", context.Match, "playerLook", context.Look);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.cameraFeedback", context.Match, "cameraFeedback", context.CameraFeedback);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.ball", context.Match, "ball", context.BallMotor);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.launcher", context.Match, "launcher", context.Launcher);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.kick", context.Match, "kick", context.Kick);
                if (context.North != null && context.South != null)
                {
                    CaptureReference(accumulator, "gameplay/wiring", "MatchController.northGoal", context.Match, "northGoal", context.North);
                    CaptureReference(accumulator, "gameplay/wiring", "MatchController.southGoal", context.Match, "southGoal", context.South);
                }
                accumulator.Capture("gameplay/contract", "GoalTrigger.event-owner", () =>
                {
                    if (typeof(GoalTrigger).GetEvent("GoalCrossed") == null ||
                        typeof(GoalTrigger).GetField("match", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null)
                        throw new InvalidOperationException("GoalTrigger event-owner contract invalid.");
                });
                CaptureSerialized(accumulator, "gameplay/serialized", "MatchController.goalFreezeDuration", context.Match, "goalFreezeDuration", GoalFreezeDuration);
                CaptureSerializedVector(accumulator, "gameplay/serialized", "MatchController.ballResetPosition", context.Match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f));
                CaptureSerializedVector(accumulator, "gameplay/serialized", "MatchController.playerResetPosition", context.Match, "playerResetPosition", new Vector3(PlayerSpawnOffset, 0f, 0f));
            }
            if (context.Hud != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "HUD.player", context.Hud, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.ball", context.Hud, "ball", context.BallMotor);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.launcher", context.Hud, "launcher", context.Launcher);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.kick", context.Hud, "kick", context.Kick);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.match", context.Hud, "match", context.Match);
            }
        }

        private static void ValidateImportedVisualAndAnimatorContracts(ValidationContext context,
            MovementLabValidationAccumulator accumulator, ISet<string> availableAssets)
        {
            if (availableAssets != null && availableAssets.Contains(RocketPrefabPath))
                accumulator.Capture("visual/prefab", "RocketTrail", () => MovementLabPrefabPipeline.ValidateTrail(AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath)));
            if (availableAssets != null && availableAssets.Contains(ExplosionPrefabPath))
                accumulator.Capture("visual/prefab", "ExplosionPrefab", () => MovementLabPrefabPipeline.ValidateExplosionPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath)));
            if (context == null || context.Player == null) return;
            context.Presentation = CaptureRequired(accumulator, "visual/root", "PlayerPresentation", context.Player.GetComponent<PlayerPresentation>(), "PlayerPresentation");
            context.WorldVisual = CaptureRequired(accumulator, "visual/root", "WorldVisual", context.Player.transform.Find("WorldVisual"), "Player WorldVisual");
            if (context.WorldVisual != null)
                context.WorldAnimator = CaptureRequired(accumulator, "visual/animator", "WorldAnimator", context.WorldVisual.GetComponent<Animator>(), "World Animator");
            if (context.Camera != null)
            {
                context.Viewmodels = CaptureRequired(accumulator, "visual/root", "Viewmodels", context.Camera.transform.Find("Viewmodels"), "Viewmodels");
                if (context.Viewmodels != null)
                {
                    context.WeaponVisual = CaptureRequired(accumulator, "visual/root", "WeaponVisual", context.Viewmodels.Find("WeaponVisual"), "WeaponVisual");
                    context.FpsVisual = CaptureRequired(accumulator, "visual/root", "FpsKickVisual", context.Viewmodels.Find("FpsKickVisual"), "FpsKickVisual");
                    if (context.FpsVisual != null)
                        context.FpsAnimator = CaptureRequired(accumulator, "visual/animator", "FpsAnimator", context.FpsVisual.GetComponent<Animator>(), "FPS Animator");
                }
            }
            if (context.Presentation != null)
            {
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.kick", context.Presentation, "kick", context.Kick);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.motor", context.Presentation, "motor", context.PlayerMotor);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.launcher", context.Presentation, "launcher", context.Launcher);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.worldAnimator", context.Presentation, "worldAnimator", context.WorldAnimator);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.fpsKickAnimator", context.Presentation, "fpsKickAnimator", context.FpsAnimator);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.weaponVisual", context.Presentation, "weaponVisual", context.WeaponVisual);
            }
            if (context.WorldAnimator != null)
            {
                accumulator.Capture("visual/animator", "root-motion", () =>
                {
                    if (context.WorldAnimator.applyRootMotion)
                        throw new InvalidOperationException("Player visual animators must not apply root motion.");
                });
                accumulator.Capture("visual/animator", "avatars", () =>
                {
                    if (context.WorldAnimator.avatar == null)
                        throw new InvalidOperationException("World animator must have an imported avatar.");
                });
                if (availableAssets != null && availableAssets.Contains(CharacterModelPath))
                    accumulator.Capture("visual/imported", "WorldVisual", () => MovementLabPrefabPipeline.ValidateImportedVisual(context.WorldVisual.gameObject, CharacterModelPath, "WorldVisual"));
                if (availableAssets != null && availableAssets.Contains(WorldControllerPath) && availableAssets.Contains(CharacterModelPath))
                    accumulator.Capture("visual/animator", "WorldController", () => MovementLabAnimatorPipeline.ValidateWorldAnimatorController(context.WorldAnimator, WorldControllerPath, CharacterModelPath));
            }
            if (context.WeaponVisual != null)
            {
                if (availableAssets != null && availableAssets.Contains(WeaponModelPath))
                    accumulator.Capture("visual/imported", "WeaponVisual", () => MovementLabPrefabPipeline.ValidateImportedVisual(context.WeaponVisual.gameObject, WeaponModelPath, "WeaponVisual"));
                accumulator.Capture("visual/material", "WeaponMaterials", () => MovementLabMaterialPipeline.ValidateWeaponMaterials(context.WeaponVisual.gameObject));
                accumulator.Capture("visual/physics", "WeaponVisual", () => MovementLabPrefabPipeline.ValidateNoPhysics(context.WeaponVisual.gameObject, "WeaponVisual"));
            }
            if (context.FpsVisual != null && context.FpsAnimator != null)
            {
                accumulator.Capture("visual/animator", "fps-root-motion", () =>
                {
                    if (context.FpsAnimator.applyRootMotion)
                        throw new InvalidOperationException("FPS animator must not apply root motion.");
                });
                accumulator.Capture("visual/animator", "fps-avatar", () =>
                {
                    if (context.FpsAnimator.avatar == null)
                        throw new InvalidOperationException("FPS animator must have an imported avatar.");
                });
                if (availableAssets != null && availableAssets.Contains(FpsKickModelPath))
                    accumulator.Capture("visual/imported", "FpsKickVisual", () => MovementLabPrefabPipeline.ValidateImportedVisual(context.FpsVisual.gameObject, FpsKickModelPath, "FpsKickVisual"));
                accumulator.Capture("visual/physics", "FpsKickVisual", () => MovementLabPrefabPipeline.ValidateNoPhysics(context.FpsVisual.gameObject, "FpsKickVisual"));
                if (availableAssets != null && availableAssets.Contains(FpsControllerPath) && availableAssets.Contains(FpsKickModelPath))
                    accumulator.Capture("visual/animator", "FpsController", () => MovementLabPrefabPipeline.ValidateAnimatorController(context.FpsAnimator, FpsControllerPath, FpsKickModelPath));
            }
            if (context.Camera != null)
            {
                var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
                accumulator.Capture("visual/camera", "LocalPlayerHidden", () =>
                {
                    if (hiddenLayer < 0 || (context.Camera.cullingMask & (1 << hiddenLayer)) != 0)
                        throw new InvalidOperationException("LocalPlayerHidden layer must be excluded from player camera culling.");
                });
                if (context.WorldVisual != null)
                    accumulator.Capture("visual/layers", "WorldVisual", () => MovementLabPrefabPipeline.ValidateLayerRecursively(context.WorldVisual.gameObject, hiddenLayer, "WorldVisual"));
                if (context.Viewmodels != null)
                    accumulator.Capture("visual/layers", "Viewmodels", () => MovementLabPrefabPipeline.ValidateLayerExcluded(context.Viewmodels.gameObject, hiddenLayer, "Viewmodels"));
                accumulator.Capture("visual/camera", "Crosshair", () => MovementLabPrefabPipeline.ValidateCrosshair(context.Camera));
            }
        }

        private static void ValidateMaterialImporterAndPrefabContracts(ValidationContext context,
            MovementLabValidationAccumulator accumulator, ISet<string> availableAssets)
        {
            if (availableAssets != null && availableAssets.Contains(PrefabPath))
                accumulator.Capture("prefab", "Player", () => MovementLabPrefabPipeline.ValidatePrefab(PrefabPath, "Player", false, context?.BallSurface));
            if (availableAssets != null && availableAssets.Contains(BallPrefabPath))
                accumulator.Capture("prefab", "Ball", () => MovementLabPrefabPipeline.ValidatePrefab(BallPrefabPath, "Ball", true, context?.BallSurface));
            if (availableAssets != null && availableAssets.Contains(RocketPrefabPath))
                accumulator.Capture("prefab", "Rocket", () => MovementLabPrefabPipeline.ValidatePrefab(RocketPrefabPath, "Rocket", false, null));
            if (availableAssets != null && availableAssets.Contains(PrefabPath) &&
                availableAssets.Contains(BallPrefabPath) && availableAssets.Contains(ExplosionPrefabPath))
                accumulator.Capture("prefab", "required-components", () => MovementLabPrefabPipeline.Validate());
            accumulator.Capture("importer", "texture-contracts", () => MovementLabImportPipeline.ValidateTextureImporterContracts());
            accumulator.Capture("importer", "animator-contracts", () => MovementLabAnimatorPipeline.Validate());
            accumulator.Capture("material", "opaque-references", () => MovementLabMaterialPipeline.ValidateOpaqueMaterialReferences());
            if (context?.SceneReady == true)
                accumulator.Capture("render", "pipeline-settings", () => MovementLabSceneComposer.ValidateRenderPipelineSettings());
        }

        private static void ValidateArenaContracts(ValidationContext context, MovementLabValidationAccumulator accumulator)
        {
            if (context?.Arena == null) return;
            accumulator.Capture("arena", "materials", () => MovementLabArenaPipeline.ValidateArenaMaterials(context.Arena, context.BallSurface));
            accumulator.Capture("arena", "architecture", () => MovementLabArenaPipeline.ValidateArenaArchitecture(context.Arena));
            accumulator.Capture("arena", "required-children", () => MovementLabArenaPipeline.Validate());
        }

        private static void ValidateLightingAndProjectContracts(ValidationContext context, bool includeBakedLighting,
            MovementLabValidationAccumulator accumulator)
        {
            if (context?.SceneReady == true && context.Arena != null)
                accumulator.Capture("lighting", "scene-environment", () => MovementLabLightingPipeline.ValidateSceneEnvironment(context.Scene, context.Arena, includeBakedLighting));
            accumulator.Capture("settings", "physics-build", () => MovementLabSceneComposer.ValidatePhysicsAndBuildSettings());
        }

        private static T CaptureRequired<T>(MovementLabValidationAccumulator accumulator, string scope, string check,
            T value, string label) where T : UnityEngine.Object
        {
            T result = null;
            accumulator.Capture(scope, check, () => result = Require(value, label));
            return result;
        }

        private static void CaptureReference(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, UnityEngine.Object expected)
        {
            accumulator.Capture(scope, check, () => ValidateReference(target, property, expected, check));
        }

        private static void CaptureSerialized(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, float expected)
        {
            accumulator.Capture(scope, check, () => ValidateSerializedFloat(target, property, expected, check));
        }

        private static void CaptureSerializedVector(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, Vector3 expected)
        {
            accumulator.Capture(scope, check, () => ValidateSerializedVector3(target, property, expected, check));
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

                private static void ValidateCustomShaders(MovementLabValidationAccumulator accumulator)
                {
                    for (var shaderIndex = 0; shaderIndex < RequiredCustomShaderPaths.Length; shaderIndex++)
                    {
                        var path = RequiredCustomShaderPaths[shaderIndex];
                        accumulator.Capture("assets/shaders", "contract:" + path, () => ValidateCustomShader(path));
                    }
                }

                private static void ValidateNoMissingComponents(Scene scene, MovementLabValidationAccumulator accumulator)
                {
                    var roots = scene.GetRootGameObjects();
                    for (var i = 0; i < roots.Length; i++)
                    {
                        var root = roots[i];
                        var components = root.GetComponentsInChildren<Component>(true);
                        for (var j = 0; j < components.Length; j++)
                        {
                            if (components[j] == null)
                                accumulator.Add("missing-components", root.name + ":" + j, "Missing script/component under " + root.name);
                        }
                    }
                }

                private static void ValidateCustomShaders()
                {
                    for (var shaderIndex = 0; shaderIndex < RequiredCustomShaderPaths.Length; shaderIndex++)
                    {
                        var path = RequiredCustomShaderPaths[shaderIndex];
                        ValidateCustomShader(path);
                    }
                }

                private static void ValidateCustomShader(string path)
                {
                    var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (shader == null)
                        throw new InvalidOperationException("MovementLab custom shader is missing; bake/capture blocked: " + path);
                    if (!shader.isSupported)
                        throw new InvalidOperationException("MovementLab custom shader is unsupported; bake/capture blocked: " + path);

                    var messages = ShaderUtil.GetShaderMessages(shader);
                    if (messages == null) return;
                    var errors = new List<string>();
                    for (var messageIndex = 0; messageIndex < messages.Length; messageIndex++)
                    {
                        var message = messages[messageIndex];
                        var severityField = message.GetType().GetField("severity");
                        var severity = severityField?.GetValue(message);
                        if (severity == null)
                        {
                            var severityProperty = message.GetType().GetProperty("severity");
                            severity = severityProperty?.GetValue(message, null);
                        }
                        if (severity != null && severity.ToString().IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                            errors.Add(message.ToString());
                    }
                    if (errors.Count > 0)
                        throw new InvalidOperationException("MovementLab custom shader compiler errors block bake/capture: " + path + ": " + string.Join(" | ", errors.ToArray()));
                }

    }
}
