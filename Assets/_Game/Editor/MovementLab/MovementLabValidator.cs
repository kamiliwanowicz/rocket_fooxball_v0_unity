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
using RocketFooxball.Runtime.Hud;
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
            internal ParticipantState[] Participants;
            internal ParticipantSpawnSet SpawnSet;
            internal GameObject Ball;
            internal GameObject MatchObject;
            internal GameObject ExplosionObject;
            internal GameObject ShieldSetObject;
            internal GameObject HudObject;
            internal GameObject MatchHudObject;
            internal PlayerMotor PlayerMotor;
            internal PlayerInputReader Input;
            internal PlayerLook Look;
            internal PlayerCameraFeedback CameraFeedback;
            internal RocketLauncher Launcher;
            internal ShotgunWeapon Shotgun;
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
            internal MatchHud MatchHud;
            internal GameObject HealthPickupsRoot;
            internal HealthPickup[] HealthPickups;
            internal GameObject ShotgunPickupsRoot;
            internal ShotgunPickup[] ShotgunPickups;
            internal GameObject AmmoPickupsRoot;
            internal AmmoPickup[] AmmoPickups;
            internal GoalTrigger North;
            internal GoalTrigger South;
            internal Collider NorthShield;
            internal Collider SouthShield;
            internal PlayerPresentation Presentation;
            internal Transform Head;
            internal Transform RocketMuzzle;
            internal Transform WorldVisual;
            internal Transform WorldShotgunMount;
            internal Transform WorldShotgunVisual;
            internal Animator WorldAnimator;
            internal Transform Viewmodels;
            internal Transform WeaponVisual;
            internal Transform FpsShotgunVisual;
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
            if (context.SceneReady)
            {
                accumulator.Capture("bots", "composition", () =>
                {
                    var pickups = new ArenaPickup[5];
                    pickups[0] = context.HealthPickups?.FirstOrDefault(item => item != null && item.name == HealthPickupWestNorthName);
                    pickups[1] = context.HealthPickups?.FirstOrDefault(item => item != null && item.name == HealthPickupEastSouthName);
                    pickups[2] = context.ShotgunPickups?.FirstOrDefault(item => item != null && item.name == ShotgunPickupName);
                    pickups[3] = context.AmmoPickups?.FirstOrDefault(item => item != null && item.name == AmmoPickupWestNorthName);
                    pickups[4] = context.AmmoPickups?.FirstOrDefault(item => item != null && item.name == AmmoPickupEastSouthName);
                    MovementLabBotPipeline.ValidateScene(context.Scene, context.Participants, context.Match, context.BallMotor, pickups,
                        context.North, context.South, context.NorthShield, context.SouthShield);
                });
            }
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
                FpsKickModelPath, WeaponModelPath, FpsShotgunModelPath, ShotgunModelPath, GrassTexturePath, GrassNormalTexturePath, GrassMetallicTexturePath,
                LauncherBaseColorTexturePath, LauncherNormalTexturePath, LauncherMetallicTexturePath, LauncherOcclusionTexturePath, LauncherEmissionTexturePath,
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
                HealthPickupPrefabPath, HealthPickupMaterialPath, ShotgunPickupPrefabPath, AmmoPickupPrefabPath, AmmoShellMaterialPath,
                RocketHotMaterialPath, ProjectileGlowMaterialPath, ExplosionAdditiveMaterialPath, ExplosionSparksMaterialPath,
                GridCeilingMaterialPath, GridLongWallMaterialPath, GridEndWallMaterialPath, SkyMaterialPath,
                 VolumeProfilePath, LightingSettingsPath, MovementLabLightingProfiles.DevelopmentSettingsPath, LightingManifestPath,
                 TeamBlueMaterialPath, TeamRedMaterialPath, TeamBlueShieldMaterialPath, TeamRedShieldMaterialPath,
                 TeamBlueTrailMaterialPath, TeamRedTrailMaterialPath, ShotgunMetalMaterialPath, ShotgunDarkMaterialPath,
                 WeaponAccentMaterialPath, WeaponAccentCoreMaterialPath, ShotgunAccentMaterialPath, ShotgunAccentCoreMaterialPath,
                 BlueCircleCueMeshPath, RedTriangleCueMeshPath
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
            context.Participants = new ParticipantState[ParticipantSlots.Length];
            for (var slotIndex = 0; slotIndex < ParticipantSlots.Length; slotIndex++)
            {
                var slot = ParticipantSlots[slotIndex];
                var participantObject = GameObject.Find(slot.DisplayName);
                context.Participants[slotIndex] = CaptureRequired(accumulator, "scene/roster", "slot:" + slot.SlotId, participantObject != null ? participantObject.GetComponent<ParticipantState>() : null, "Participant slot " + slot.SlotId);
            }
            context.Player = context.Participants.Length > 0 && context.Participants[0] != null ? context.Participants[0].gameObject : null;
            context.SpawnSet = CaptureRequired(accumulator, "scene/roster", "spawn-set", GameObject.Find("ParticipantSpawnSet")?.GetComponent<ParticipantSpawnSet>(), "ParticipantSpawnSet");
            context.Ball = CaptureRequired(accumulator, "scene/root", "Ball", GameObject.Find("Ball"), "Ball root");
            context.MatchObject = CaptureRequired(accumulator, "scene/root", "MatchController", GameObject.Find("MatchController"), "MatchController root");
            var healthPickupRoots = context.Scene.GetRootGameObjects().Where(root => root != null && root.name == HealthPickupsRootName).ToArray();
            accumulator.Capture("scene/health-pickups", "root-count", () =>
            {
                if (healthPickupRoots.Length != 1) throw new InvalidOperationException("MovementLab must contain exactly one HealthPickups root.");
            });
            context.HealthPickupsRoot = healthPickupRoots.Length == 1 ? healthPickupRoots[0] : null;
            context.HealthPickups = UnityEngine.Object.FindObjectsByType<HealthPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            accumulator.Capture("scene/health-pickups", "component-count", () =>
            {
                if (context.HealthPickups.Length != HealthPickupSpawns.Length) throw new InvalidOperationException("MovementLab must contain exactly two HealthPickup components, including inactive instances.");
            });
            if (context.HealthPickupsRoot != null && context.Arena != null)
                accumulator.Capture("scene/health-pickups", "root-parent", () =>
                {
                    if (context.HealthPickupsRoot.isStatic || context.HealthPickupsRoot.transform.IsChildOf(context.Arena.transform)) throw new InvalidOperationException("HealthPickups root must remain dynamic and outside static Arena hierarchy.");
                });
            var shotgunPickupRoots = context.Scene.GetRootGameObjects().Where(root => root != null && root.name == ShotgunPickupsRootName).ToArray();
            accumulator.Capture("scene/shotgun-pickups", "root-count", () =>
            {
                if (shotgunPickupRoots.Length != 1) throw new InvalidOperationException("MovementLab must contain exactly one ShotgunPickups root.");
            });
            context.ShotgunPickupsRoot = shotgunPickupRoots.Length == 1 ? shotgunPickupRoots[0] : null;
            context.ShotgunPickups = UnityEngine.Object.FindObjectsByType<ShotgunPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            accumulator.Capture("scene/shotgun-pickups", "component-count", () =>
            {
                if (context.ShotgunPickups.Length != ShotgunPickupSpawns.Length) throw new InvalidOperationException("MovementLab must contain exactly one ShotgunPickup component, including inactive instances.");
            });
            if (context.ShotgunPickupsRoot != null && context.Arena != null)
                accumulator.Capture("scene/shotgun-pickups", "root-parent", () =>
                {
                    if (context.ShotgunPickupsRoot.isStatic || context.ShotgunPickupsRoot.transform.IsChildOf(context.Arena.transform)) throw new InvalidOperationException("ShotgunPickups root must remain dynamic and outside static Arena hierarchy.");
                });
            var ammoPickupRoots = context.Scene.GetRootGameObjects().Where(root => root != null && root.name == AmmoPickupsRootName).ToArray();
            accumulator.Capture("scene/ammo-pickups", "root-count", () =>
            {
                if (ammoPickupRoots.Length != 1) throw new InvalidOperationException("MovementLab must contain exactly one AmmoPickups root.");
            });
            context.AmmoPickupsRoot = ammoPickupRoots.Length == 1 ? ammoPickupRoots[0] : null;
            context.AmmoPickups = UnityEngine.Object.FindObjectsByType<AmmoPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            accumulator.Capture("scene/ammo-pickups", "component-count", () =>
            {
                if (context.AmmoPickups.Length != AmmoPickupSpawns.Length) throw new InvalidOperationException("MovementLab must contain exactly two AmmoPickup components, including inactive instances.");
            });
            if (context.AmmoPickupsRoot != null && context.Arena != null)
                accumulator.Capture("scene/ammo-pickups", "root-parent", () =>
                {
                    if (context.AmmoPickupsRoot.isStatic || context.AmmoPickupsRoot.transform.IsChildOf(context.Arena.transform)) throw new InvalidOperationException("AmmoPickups root must remain dynamic and outside static Arena hierarchy.");
                });
            context.ExplosionObject = CaptureRequired(accumulator, "scene/root", "ExplosionResolver", GameObject.Find("ExplosionResolver"), "ExplosionResolver root");
            context.ShieldSetObject = CaptureRequired(accumulator, "scene/root", "GoalShieldSet", GameObject.Find("GoalShieldSet"), "GoalShieldSet root");
            context.HudObject = CaptureRequired(accumulator, "scene/root", "DebugHUD", GameObject.Find("DebugHUD"), "DebugHUD root");
            var matchHudRoots = context.Scene.GetRootGameObjects().Where(root => root != null && root.name == "MatchHUD").ToArray();
            accumulator.Capture("scene/root", "MatchHUD.count", () =>
            {
                if (matchHudRoots.Length != 1) throw new InvalidOperationException("MovementLab must contain exactly one MatchHUD root.");
            });
            context.MatchHudObject = CaptureRequired(accumulator, "scene/root", "MatchHUD", matchHudRoots.Length == 1 ? matchHudRoots[0] : null, "MatchHUD root");
            var matchHudComponents = UnityEngine.Object.FindObjectsByType<MatchHud>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            accumulator.Capture("scene/root", "MatchHUD.component-count", () =>
            {
                if (matchHudComponents.Length != 1) throw new InvalidOperationException("MovementLab must contain exactly one MatchHud component.");
            });
            if (context.MatchHudObject != null)
                context.MatchHud = CaptureRequired(accumulator, "scene/gameplay-components", "MatchHud", context.MatchHudObject.GetComponent<MatchHud>(), "MatchHud");
            accumulator.Capture("scene/root", "build-marker", () => Require(GameObject.Find(GetBuildMarkerName(builderSignature)), "MovementLab build marker"));

            ValidateParticipantRoster(context, accumulator);

            if (context.Player != null)
            {
                context.PlayerMotor = CaptureRequired(accumulator, "scene/player-components", "PlayerMotor", context.Player.GetComponent<PlayerMotor>(), "PlayerMotor");
                context.Input = CaptureRequired(accumulator, "scene/player-components", "PlayerInputReader", context.Player.GetComponent<PlayerInputReader>(), "PlayerInputReader");
                context.Look = CaptureRequired(accumulator, "scene/player-components", "PlayerLook", context.Player.GetComponent<PlayerLook>(), "PlayerLook");
                context.CameraFeedback = CaptureRequired(accumulator, "scene/player-components", "PlayerCameraFeedback", context.Player.GetComponent<PlayerCameraFeedback>(), "PlayerCameraFeedback");
                context.Launcher = CaptureRequired(accumulator, "scene/player-components", "RocketLauncher", context.Player.GetComponent<RocketLauncher>(), "RocketLauncher");
                context.Shotgun = CaptureRequired(accumulator, "scene/player-components", "ShotgunWeapon", context.Player.GetComponent<ShotgunWeapon>(), "ShotgunWeapon");
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
                context.WorldShotgunMount = CaptureRequired(accumulator, "visual/root", "WorldShotgunMount",
                    MovementLabPrefabPipeline.FindNamedTransform(context.Player.transform.Find("WorldVisual"), "WorldShotgunMount"), "WorldShotgunMount");
                context.WorldShotgunVisual = CaptureRequired(accumulator, "visual/root", "WorldShotgunVisual",
                    MovementLabPrefabPipeline.FindNamedTransform(context.WorldShotgunMount, "WorldShotgunVisual"), "WorldShotgunVisual");
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
                if (RenderSettings.skybox == null || RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight ||
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
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.directRocketDamage", context.Resolver, "directRocketDamage", ExplosionResolver.DefaultDirectRocketDamage);
                CaptureSerialized(accumulator, "gameplay/serialized", "ExplosionResolver.enemyRocketImpulseMultiplier", context.Resolver, "enemyRocketImpulseMultiplier", ExplosionResolver.DefaultEnemyRocketImpulseMultiplier);
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
                accumulator.Capture("scene/goals", trigger.name + ".identity", () => ValidatePersistentIdentity(trigger, trigger.name + " goal"));
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
                accumulator.Capture("scene/goals", "defending-teams", () =>
                {
                    if (context.North.DefendingTeam != ParticipantTeam.Red || context.South.DefendingTeam != ParticipantTeam.Blue)
                        throw new InvalidOperationException("North goal must defend Red and South goal must defend Blue.");
                });
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
            accumulator.Capture("input", "dash-kick-binding", ValidateDashKickInputAsset);
            accumulator.Capture("input", "shotgun-binding", ValidateShotgunInputAsset);
            accumulator.Capture("input", "match-table-binding", ValidateMatchTableInputAsset);
            accumulator.Capture("gameplay/contract", "dash-public-surface", ValidateDashPublicSurface);
            accumulator.Capture("gameplay/contract", "shotgun-public-surface", ValidateShotgunRuntimeSurface);
            if (context.BallMotor != null)
            {
                accumulator.Capture("gameplay/contract", "BallMotor.touch-roster", ValidateBallTouchRosterContract);
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.body", context.BallMotor, "body", context.BallBody);
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.ballCollider", context.BallMotor, "ballCollider", context.BallCollider);
                CaptureObjectArray(accumulator, "gameplay/wiring", "BallMotor.participants", context.BallMotor, "participants", context.Participants.Cast<UnityEngine.Object>().ToArray());
                CaptureReference(accumulator, "gameplay/wiring", "BallMotor.goalShieldSet", context.BallMotor, "goalShieldSet", context.GoalShieldSet);
                CaptureSerialized(accumulator, "gameplay/serialized", "BallMotor.meaningfulContactSpeedThreshold", context.BallMotor, "meaningfulContactSpeedThreshold", 1f);
            }
            if (context.Participants != null)
            {
                for (var participantIndex = 0; participantIndex < context.Participants.Length; participantIndex++)
                {
                    var participant = context.Participants[participantIndex];
                    if (participant == null) continue;
                    CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Launcher.explosionResolver", participant.Launcher, "explosionResolver", context.Resolver);
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Launcher.projectilePrefab", participant.Launcher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath));
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Kick.ball", participant.Kick, "ball", context.BallMotor);
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Kick.ownerParticipant", participant.Kick, "ownerParticipant", participant);
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Shotgun.ball", participant.Shotgun, "ball", context.BallMotor);
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Shotgun.ownerParticipant", participant.Shotgun, "ownerParticipant", participant);
                     CaptureSerializedInteger(accumulator, "gameplay/serialized", "Participant[" + participantIndex + "].Shotgun.pelletCount", participant.Shotgun, "pelletCount", ShotgunDamageRules.DefaultPelletCount);
                     CaptureSerialized(accumulator, "gameplay/serialized", "Participant[" + participantIndex + "].Shotgun.pumpDelay", participant.Shotgun, "pumpDelay", ShotgunDamageRules.DefaultPumpDelay);
                     accumulator.Capture("gameplay/wiring", "Participant[" + participantIndex + "].Shotgun.hitMask", () =>
                     {
                         var projectilesLayer = LayerMask.NameToLayer(MovementLabContract.ProjectilesLayerName);
                         if (projectilesLayer < 0 || (participant.Shotgun.HitMask.value & (1 << projectilesLayer)) != 0)
                             throw new InvalidOperationException("Shotgun hit mask must exclude Projectiles: " + participant.DisplayName);
                     });
                     CaptureReference(accumulator, "gameplay/wiring", "Participant[" + participantIndex + "].Presentation.cameraFeedback", participant.Presentation, "cameraFeedback", participant.CameraFeedback);
                    CaptureDashTuning(accumulator, participant, participantIndex);
                }
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
                CaptureReference(accumulator, "gameplay/wiring", "PlayerCameraFeedback.participant", context.CameraFeedback, "participant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitRadius", context.CameraFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitHeight", context.CameraFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight);
                CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationLookHeight", context.CameraFeedback, "celebrationLookHeight", CelebrationLookHeight);
                 CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationOrbitDegrees", context.CameraFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees);
                 CaptureSerialized(accumulator, "gameplay/serialized", "PlayerCameraFeedback.celebrationFov", context.CameraFeedback, "celebrationFov", CelebrationFov);
                 CaptureSerializedVector(accumulator, "gameplay/serialized", "PlayerCameraFeedback.spectatorOffset", context.CameraFeedback, "spectatorOffset", PlayerCameraFeedback.ExpectedSpectatorOffset);
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
                 CaptureReference(accumulator, "gameplay/wiring", "BallKick.ownerParticipant", context.Kick, "ownerParticipant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
             }
             if (context.Shotgun != null)
             {
                 CaptureReference(accumulator, "gameplay/wiring", "ShotgunWeapon.input", context.Shotgun, "input", context.Input);
                 CaptureReference(accumulator, "gameplay/wiring", "ShotgunWeapon.look", context.Shotgun, "look", context.Look);
                 CaptureReference(accumulator, "gameplay/wiring", "ShotgunWeapon.aimCamera", context.Shotgun, "aimCamera", context.Camera);
                 CaptureReference(accumulator, "gameplay/wiring", "ShotgunWeapon.ownerParticipant", context.Shotgun, "ownerParticipant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
                 CaptureReference(accumulator, "gameplay/wiring", "ShotgunWeapon.ball", context.Shotgun, "ball", context.BallMotor);
                 var projectilesLayer = LayerMask.NameToLayer(MovementLabContract.ProjectilesLayerName);
                 accumulator.Capture("gameplay/wiring", "ShotgunWeapon.hitMask", () =>
                 {
                     if (projectilesLayer < 0 || (context.Shotgun.HitMask.value & (1 << projectilesLayer)) != 0)
                         throw new InvalidOperationException("ShotgunWeapon.hitMask must exclude Projectiles.");
                 });
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.pelletDamage", context.Shotgun, "pelletDamage", ShotgunDamageRules.DefaultPelletDamage);
                 CaptureSerializedInteger(accumulator, "gameplay/serialized", "ShotgunWeapon.pelletCount", context.Shotgun, "pelletCount", ShotgunDamageRules.DefaultPelletCount);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.spreadAngleDegrees", context.Shotgun, "spreadAngleDegrees", ShotgunDamageRules.DefaultSpreadAngleDegrees);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.fullDamageRange", context.Shotgun, "fullDamageRange", ShotgunDamageRules.DefaultFullDamageRange);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.mediumRange", context.Shotgun, "mediumRange", ShotgunDamageRules.DefaultMediumRange);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.maxRange", context.Shotgun, "maxRange", ShotgunDamageRules.DefaultMaxRange);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.mediumMultiplier", context.Shotgun, "mediumMultiplier", ShotgunDamageRules.DefaultMediumMultiplier);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.farMultiplier", context.Shotgun, "farMultiplier", ShotgunDamageRules.DefaultFarMultiplier);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.pumpDelay", context.Shotgun, "pumpDelay", ShotgunDamageRules.DefaultPumpDelay);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.ballImpulsePerPellet", context.Shotgun, "ballImpulsePerPellet", ShotgunDamageRules.DefaultPerPelletBallImpulse);
                 CaptureSerialized(accumulator, "gameplay/serialized", "ShotgunWeapon.ballImpulseCap", context.Shotgun, "ballImpulseCap", ShotgunDamageRules.DefaultBallImpulseCap);
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
                accumulator.Capture("gameplay/contract", "MatchController.identity", () => ValidatePersistentIdentity(context.Match, "MatchController"));
                CaptureObjectArray(accumulator, "gameplay/wiring", "MatchController.participants", context.Match, "participants", context.Participants.Cast<UnityEngine.Object>().ToArray());
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.localParticipant", context.Match, "localParticipant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.spawnSet", context.Match, "spawnSet", context.SpawnSet);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.cameraFeedback", context.Match, "cameraFeedback", context.CameraFeedback);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.ball", context.Match, "ball", context.BallMotor);
                if (context.North != null && context.South != null)
                {
                    CaptureReference(accumulator, "gameplay/wiring", "MatchController.northGoal", context.Match, "northGoal", context.North);
                    CaptureReference(accumulator, "gameplay/wiring", "MatchController.southGoal", context.Match, "southGoal", context.South);
                }
                var botSystems = GameObject.Find(MovementLabBotPipeline.SystemsRootName);
                CaptureReference(accumulator, "gameplay/wiring", "MatchController.botSystemsRoot", context.Match, "botSystemsRoot", botSystems);
                accumulator.Capture("gameplay/contract", "GoalTrigger.event-owner", () =>
                {
                    var goalType = typeof(GoalTrigger);
                    var hasMatchReference = goalType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        .Any(field => typeof(MatchController).IsAssignableFrom(field.FieldType)) ||
                        goalType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                            .Any(property => typeof(MatchController).IsAssignableFrom(property.PropertyType));
                    if (goalType.GetEvent("GoalCrossed") == null || hasMatchReference)
                        throw new InvalidOperationException("GoalTrigger event-owner contract invalid.");
                });
                accumulator.Capture("gameplay/contract", "MatchController.public-surface", () => ValidateMatchPublicContract(context.Match));
                CaptureSerialized(accumulator, "gameplay/serialized", "MatchController.matchDuration", context.Match, "matchDuration", MovementLabSceneComposer.MatchDuration);
                CaptureSerialized(accumulator, "gameplay/serialized", "MatchController.goalCelebrationOrbitDuration", context.Match, "goalCelebrationOrbitDuration", MovementLabSceneComposer.GoalSummaryDuration);
                CaptureSerialized(accumulator, "gameplay/serialized", "MatchController.kickoffCountdownDuration", context.Match, "kickoffCountdownDuration", MovementLabSceneComposer.KickoffCountdownDuration);
                CaptureSerialized(accumulator, "gameplay/serialized", "MatchController.participantRecoveryThreshold", context.Match, "participantRecoveryThreshold", ParticipantRecoveryThreshold);
                accumulator.Capture("gameplay/serialized", "MatchController.botsEnabledByDefault", () => ValidateSerializedBool(context.Match, "botsEnabledByDefault", BotsEnabledByDefault, "MatchController.botsEnabledByDefault"));
                CaptureSerializedVector(accumulator, "gameplay/serialized", "MatchController.ballResetPosition", context.Match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f));
                CaptureSerializedVector(accumulator, "gameplay/serialized", "MatchController.resetLookTarget", context.Match, "resetLookTarget", Vector3.zero);
            }
            if (context.Hud != null)
            {
                CaptureReference(accumulator, "gameplay/wiring", "HUD.player", context.Hud, "player", context.PlayerMotor);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.ball", context.Hud, "ball", context.BallMotor);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.launcher", context.Hud, "launcher", context.Launcher);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.kick", context.Hud, "kick", context.Kick);
                CaptureReference(accumulator, "gameplay/wiring", "HUD.match", context.Hud, "match", context.Match);
            }
            if (context.MatchHud != null)
            {
                accumulator.Capture("gameplay/contract", "MatchHUD.identity", () => ValidatePersistentIdentity(context.MatchHudObject, "MatchHUD root"));
                accumulator.Capture("gameplay/contract", "MatchHUD.component-identity", () => ValidatePersistentIdentity(context.MatchHud, "MatchHud component"));
                CaptureReference(accumulator, "gameplay/wiring", "MatchHUD.match", context.MatchHud, "match", context.Match);
                CaptureReference(accumulator, "gameplay/wiring", "MatchHUD.localParticipant", context.MatchHud, "localParticipant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
                CaptureReference(accumulator, "gameplay/wiring", "MatchHUD.input", context.MatchHud, "input", context.Input);
                accumulator.Capture("gameplay/contract", "MatchHUD.serialized-surface", ValidateMatchHudSerializedSurface);
                accumulator.Capture("gameplay/contract", "MatchHUD.screen-policy", ValidateMatchHudScreenPolicy);
            }
            accumulator.Capture("gameplay/contract", "health-pickup-runtime-surface", ValidateHealthPickupRuntimeSurface);
            ValidateHealthPickupSceneContracts(context, accumulator);
            ValidateShotgunPickupSceneContracts(context, accumulator);
            ValidateAmmoPickupSceneContracts(context, accumulator);
        }

        private static void ValidateHealthPickupRuntimeSurface()
        {
            var restore = typeof(ParticipantState).GetMethod("TryRestoreHealth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(float) }, null);
            var readModel = typeof(ParticipantState).GetProperty("ReadModel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (restore == null || restore.ReturnType != typeof(bool) || readModel == null || readModel.PropertyType != typeof(ParticipantReadModel) || !readModel.CanRead)
                throw new InvalidOperationException("Participant health restore/read-model surface is missing or changed.");

            var resetEvent = typeof(MatchController).GetEvent("CoordinatedResetRequested", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (resetEvent == null || resetEvent.EventHandlerType != typeof(Action<MatchResetReason>))
                throw new InvalidOperationException("Match coordinated reset event surface is missing or changed.");
            var pickupTypes = new[] { typeof(ArenaPickup), typeof(HealthPickup), typeof(ShotgunPickup), typeof(AmmoPickup) };
            var hasPickupField = typeof(MatchController).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Any(field => pickupTypes.Any(type => type.IsAssignableFrom(field.FieldType)));
            var hasPickupProperty = typeof(MatchController).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Any(property => pickupTypes.Any(type => type.IsAssignableFrom(property.PropertyType)));
            if (hasPickupField || hasPickupProperty)
                throw new InvalidOperationException("MatchController must not own health pickup references.");

            ValidateShotgunAmmoPickupType(typeof(ShotgunPickup), nameof(ParticipantState.TryCollectShotgun));
            ValidateShotgunAmmoPickupType(typeof(AmmoPickup), nameof(ParticipantState.TryCollectShotgunAmmo));
        }

        private static void ValidateShotgunAmmoPickupType(Type pickupType, string participantMethodName)
        {
            if (pickupType == null || !typeof(ArenaPickup).IsAssignableFrom(pickupType))
                throw new InvalidOperationException("Shotgun and ammo pickups must derive from ArenaPickup.");
            var grant = pickupType.GetProperty("Grant", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (grant == null || grant.PropertyType != typeof(int) || !grant.CanRead)
                throw new InvalidOperationException(pickupType.Name + " must expose a public integer Grant property.");
            var method = typeof(ParticipantState).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .FirstOrDefault(candidate => candidate.Name == participantMethodName && candidate.ReturnType == typeof(bool) &&
                    candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(int));
            if (method == null)
                throw new InvalidOperationException("ParticipantState shotgun pickup method is missing: " + participantMethodName);
        }

        private static void ValidateHealthPickupSceneContracts(ValidationContext context,
            MovementLabValidationAccumulator accumulator)
        {
            if (context == null || !context.SceneReady || context.HealthPickupsRoot == null || context.HealthPickups == null) return;
            accumulator.Capture("scene/health-pickups", "root-children", () =>
            {
                if (context.HealthPickupsRoot.transform.childCount != HealthPickupSpawns.Length)
                    throw new InvalidOperationException("HealthPickups root must contain exactly two pickup instances.");
            });
            var expected = new HashSet<string>(HealthPickupSpawns.Select(definition => definition.Name), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            GameObject firstPrefabSource = null;
            HealthPickup firstComponentSource = null;
            for (var i = 0; i < context.HealthPickups.Length; i++)
            {
                var pickup = context.HealthPickups[i];
                if (pickup == null) continue;
                var definition = HealthPickupSpawns.FirstOrDefault(item => item.Name == pickup.name);
                accumulator.Capture("scene/health-pickups", pickup.name + ".name", () =>
                {
                    if (!expected.Contains(pickup.name) || !seen.Add(pickup.name)) throw new InvalidOperationException("Health pickup names must be unique and match the two authored definitions.");
                });
                accumulator.Capture("scene/health-pickups", pickup.name + ".identity", () => ValidatePersistentIdentity(pickup, pickup.name + " HealthPickup"));
                accumulator.Capture("scene/health-pickups", pickup.name + ".parent", () =>
                {
                    if (pickup.transform.parent != context.HealthPickupsRoot.transform || !pickup.gameObject.activeSelf)
                        throw new InvalidOperationException(pickup.name + " must be an active direct child of HealthPickups.");
                });
                accumulator.Capture("scene/health-pickups", pickup.name + ".transform", () =>
                {
                    if (Vector3.Distance(pickup.transform.position, definition.Position) > 0.001f ||
                        Quaternion.Angle(pickup.transform.rotation, definition.Rotation) > 0.1f ||
                        Vector3.Distance(pickup.transform.lossyScale, Vector3.one) > 0.001f)
                        throw new InvalidOperationException(pickup.name + " transform does not match the authored spawn definition.");
                });
                var sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(pickup.gameObject);
                var sourceComponent = PrefabUtility.GetCorrespondingObjectFromSource(pickup);
                accumulator.Capture("scene/health-pickups", pickup.name + ".prefab-provenance", () =>
                {
                    if (sourceRoot == null || AssetDatabase.GetAssetPath(sourceRoot) != HealthPickupPrefabPath ||
                        sourceComponent == null || AssetDatabase.GetAssetPath(sourceComponent) != HealthPickupPrefabPath)
                        throw new InvalidOperationException(pickup.name + " must remain connected to HealthPickup.prefab.");
                    if (firstPrefabSource == null)
                    {
                        firstPrefabSource = sourceRoot;
                        firstComponentSource = sourceComponent;
                    }
                    else if (sourceRoot != firstPrefabSource || sourceComponent != firstComponentSource)
                    {
                        throw new InvalidOperationException("Health pickup instances must share one prefab root/component source.");
                    }
                    ValidatePersistentIdentity(sourceRoot, pickup.name + " prefab source");
                    ValidatePersistentIdentity(sourceComponent, pickup.name + " prefab component source");
                });
                var trigger = pickup.GetComponent<SphereCollider>();
                var body = pickup.GetComponent<Rigidbody>();
                var visualRoot = pickup.transform.Find("VisualRoot");
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".pickupTrigger", pickup, "pickupTrigger", trigger);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".visualRoot", pickup, "visualRoot", visualRoot != null ? visualRoot.gameObject : null);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".match", pickup, "match", context.Match);
                CaptureSerialized(accumulator, "gameplay/serialized", pickup.name + ".respawnDelay", pickup, "respawnDelay", MovementLabContract.HealthPickupRespawnDelay);
                CaptureSerialized(accumulator, "gameplay/serialized", pickup.name + ".restoreFraction", pickup, "restoreFraction", MovementLabContract.HealthPickupRestoreFraction);
                accumulator.Capture("scene/health-pickups", pickup.name + ".physics", () =>
                {
                    if (trigger == null || !trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.HealthPickupTriggerRadius) > 0.001f ||
                        body == null || !body.isKinematic || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        pickup.GetComponentsInChildren<Collider>(true).Length != 1 || pickup.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                        throw new InvalidOperationException(pickup.name + " collider/body contract invalid.");
                });
                accumulator.Capture("scene/health-pickups", pickup.name + ".visual", () =>
                {
                    if (visualRoot == null || visualRoot.parent != pickup.transform || visualRoot.childCount != 3 || visualRoot.GetComponentsInChildren<Collider>(true).Length != 0)
                        throw new InvalidOperationException(pickup.name + " visual hierarchy contract invalid.");
                    var material = AssetDatabase.LoadAssetAtPath<Material>(HealthPickupMaterialPath);
                    var renderers = visualRoot.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 3 || renderers.Any(renderer => renderer.sharedMaterials == null || renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != material ||
                        renderer.lightProbeUsage != LightProbeUsage.BlendProbes || renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes))
                        throw new InvalidOperationException(pickup.name + " visual render/material contract invalid.");
                    foreach (var transform in pickup.GetComponentsInChildren<Transform>(true))
                        if (transform.gameObject.isStatic) throw new InvalidOperationException(pickup.name + " pickup hierarchy must remain nonstatic.");
                });
            }
            accumulator.Capture("scene/health-pickups", "point-mirror", () =>
            {
                var west = context.HealthPickups.FirstOrDefault(pickup => pickup != null && pickup.name == HealthPickupWestNorthName);
                var east = context.HealthPickups.FirstOrDefault(pickup => pickup != null && pickup.name == HealthPickupEastSouthName);
                var mirroredEast = west != null ? new Vector3(-west.transform.position.x, west.transform.position.y, -west.transform.position.z) : Vector3.zero;
                if (west == null || east == null || Vector3.Distance(east.transform.position, mirroredEast) > 0.001f ||
                    Quaternion.Angle(east.transform.rotation, MovementLabContract.HealthPickupEastSouthRotation) > 0.1f)
                    throw new InvalidOperationException("Health pickup placements must be distinct point mirrors across the arena origin.");
            });
        }

        private static void ValidateShotgunPickupSceneContracts(ValidationContext context,
            MovementLabValidationAccumulator accumulator)
        {
            if (context == null || !context.SceneReady || context.ShotgunPickupsRoot == null || context.ShotgunPickups == null) return;
            accumulator.Capture("scene/shotgun-pickups", "root-children", () =>
            {
                if (context.ShotgunPickupsRoot.transform.childCount != ShotgunPickupSpawns.Length)
                    throw new InvalidOperationException("ShotgunPickups root must contain exactly one pickup instance.");
            });
            var expected = new HashSet<string>(ShotgunPickupSpawns.Select(definition => definition.Name), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            GameObject firstPrefabSource = null;
            ShotgunPickup firstComponentSource = null;
            for (var i = 0; i < context.ShotgunPickups.Length; i++)
            {
                var pickup = context.ShotgunPickups[i];
                if (pickup == null) continue;
                var definition = ShotgunPickupSpawns.FirstOrDefault(item => item.Name == pickup.name);
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".name", () =>
                {
                    if (!expected.Contains(pickup.name) || !seen.Add(pickup.name)) throw new InvalidOperationException("Shotgun pickup name must match the authored neutral definition.");
                });
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".identity", () => ValidatePersistentIdentity(pickup, pickup.name + " ShotgunPickup"));
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".parent", () =>
                {
                    if (pickup.transform.parent != context.ShotgunPickupsRoot.transform || !pickup.gameObject.activeSelf)
                        throw new InvalidOperationException(pickup.name + " must be an active direct child of ShotgunPickups.");
                });
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".transform", () =>
                {
                    if (Vector3.Distance(pickup.transform.position, definition.Position) > 0.001f ||
                        Quaternion.Angle(pickup.transform.rotation, definition.Rotation) > 0.1f ||
                        Vector3.Distance(pickup.transform.lossyScale, Vector3.one) > 0.001f)
                        throw new InvalidOperationException(pickup.name + " transform does not match the authored neutral spawn definition.");
                });
                var sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(pickup.gameObject);
                var sourceComponent = PrefabUtility.GetCorrespondingObjectFromSource(pickup);
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".prefab-provenance", () =>
                {
                    if (sourceRoot == null || AssetDatabase.GetAssetPath(sourceRoot) != ShotgunPickupPrefabPath ||
                        sourceComponent == null || AssetDatabase.GetAssetPath(sourceComponent) != ShotgunPickupPrefabPath)
                        throw new InvalidOperationException(pickup.name + " must remain connected to ShotgunPickup.prefab.");
                    if (firstPrefabSource == null)
                    {
                        firstPrefabSource = sourceRoot;
                        firstComponentSource = sourceComponent;
                    }
                    else if (sourceRoot != firstPrefabSource || sourceComponent != firstComponentSource)
                    {
                        throw new InvalidOperationException("Shotgun pickup instances must share one prefab root/component source.");
                    }
                    ValidatePersistentIdentity(sourceRoot, pickup.name + " prefab source");
                    ValidatePersistentIdentity(sourceComponent, pickup.name + " prefab component source");
                });
                var trigger = pickup.GetComponent<SphereCollider>();
                var body = pickup.GetComponent<Rigidbody>();
                var visualRoot = pickup.transform.Find("VisualRoot");
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".pickupTrigger", pickup, "pickupTrigger", trigger);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".visualRoot", pickup, "visualRoot", visualRoot != null ? visualRoot.gameObject : null);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".match", pickup, "match", context.Match);
                CaptureSerialized(accumulator, "gameplay/serialized", pickup.name + ".respawnDelay", pickup, "respawnDelay", MovementLabContract.ShotgunPickupRespawnDelay);
                CaptureSerializedInteger(accumulator, "gameplay/serialized", pickup.name + ".grant", pickup, "grant", MovementLabContract.ShotgunPickupGrant);
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".physics", () =>
                {
                    if (context.Match == null || trigger == null || !trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.ShotgunPickupTriggerRadius) > 0.001f ||
                        body == null || !body.isKinematic || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        pickup.GetComponentsInChildren<Collider>(true).Length != 1 || pickup.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                        throw new InvalidOperationException(pickup.name + " collider/body/match contract invalid.");
                });
                accumulator.Capture("scene/shotgun-pickups", pickup.name + ".visual", () =>
                {
                    if (visualRoot == null || visualRoot.parent != pickup.transform || visualRoot.childCount != 3)
                        throw new InvalidOperationException(pickup.name + " visual hierarchy contract invalid.");
                    var model = visualRoot.Find("ShotgunModel");
                    if (model == null) throw new InvalidOperationException(pickup.name + " imported shotgun model is missing.");
                    MovementLabMaterialPipeline.ValidateShotgunMaterials(model.gameObject);
                    MovementLabPrefabPipeline.ValidateImportedVisual(model.gameObject, ShotgunModelPath, pickup.name + " imported shotgun model");
                    MovementLabPrefabPipeline.ValidateWeaponVisualContract(model.gameObject, pickup.name + " imported shotgun model", WorldShotgunBoundsMin, WorldShotgunBoundsMax);
                    MovementLabPrefabPipeline.ValidateNoPhysics(model.gameObject, pickup.name + " imported shotgun model");
                    MovementLabPrefabPipeline.ValidateNoAnimators(model.gameObject, pickup.name + " imported shotgun model");
                    MovementLabPrefabPipeline.ValidateImportedVisualForward(model, pickup.name + " imported shotgun model");
                    ValidateScenePickupCuePair(visualRoot, pickup.name);
                    if (visualRoot.GetComponentsInChildren<Collider>(true).Length != 0 || visualRoot.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<Light>(true).Length != 0 || visualRoot.GetComponentsInChildren<ParticleSystem>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<Animator>(true).Length != 0 || visualRoot.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                        throw new InvalidOperationException(pickup.name + " visual hierarchy contains forbidden components.");
                    MovementLabPrefabPipeline.ValidateDynamicHierarchy(pickup.gameObject, pickup.name);
                });
            }
            accumulator.Capture("scene/shotgun-pickups", "shell-capacity", () =>
            {
                if (context.Participants == null || context.Participants.Any(participant => participant == null || participant.ShotgunShellCapacity != MovementLabContract.ShotgunShellCapacity))
                    throw new InvalidOperationException("Shotgun pickup scene must preserve the sixteen-shell participant capacity.");
            });
        }

        private static void ValidateAmmoPickupSceneContracts(ValidationContext context,
            MovementLabValidationAccumulator accumulator)
        {
            if (context == null || !context.SceneReady || context.AmmoPickupsRoot == null || context.AmmoPickups == null) return;
            accumulator.Capture("scene/ammo-pickups", "root-children", () =>
            {
                if (context.AmmoPickupsRoot.transform.childCount != AmmoPickupSpawns.Length)
                    throw new InvalidOperationException("AmmoPickups root must contain exactly two pickup instances.");
            });
            var expected = new HashSet<string>(AmmoPickupSpawns.Select(definition => definition.Name), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            GameObject firstPrefabSource = null;
            AmmoPickup firstComponentSource = null;
            for (var i = 0; i < context.AmmoPickups.Length; i++)
            {
                var pickup = context.AmmoPickups[i];
                if (pickup == null) continue;
                var definition = AmmoPickupSpawns.FirstOrDefault(item => item.Name == pickup.name);
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".name", () =>
                {
                    if (!expected.Contains(pickup.name) || !seen.Add(pickup.name)) throw new InvalidOperationException("Ammo pickup names must be unique and match the two authored definitions.");
                });
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".identity", () => ValidatePersistentIdentity(pickup, pickup.name + " AmmoPickup"));
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".parent", () =>
                {
                    if (pickup.transform.parent != context.AmmoPickupsRoot.transform || !pickup.gameObject.activeSelf)
                        throw new InvalidOperationException(pickup.name + " must be an active direct child of AmmoPickups.");
                });
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".transform", () =>
                {
                    if (Vector3.Distance(pickup.transform.position, definition.Position) > 0.001f ||
                        Quaternion.Angle(pickup.transform.rotation, definition.Rotation) > 0.1f ||
                        Vector3.Distance(pickup.transform.lossyScale, Vector3.one) > 0.001f)
                        throw new InvalidOperationException(pickup.name + " transform does not match the authored ammo spawn definition.");
                });
                var sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(pickup.gameObject);
                var sourceComponent = PrefabUtility.GetCorrespondingObjectFromSource(pickup);
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".prefab-provenance", () =>
                {
                    if (sourceRoot == null || AssetDatabase.GetAssetPath(sourceRoot) != AmmoPickupPrefabPath ||
                        sourceComponent == null || AssetDatabase.GetAssetPath(sourceComponent) != AmmoPickupPrefabPath)
                        throw new InvalidOperationException(pickup.name + " must remain connected to AmmoPickup.prefab.");
                    if (firstPrefabSource == null)
                    {
                        firstPrefabSource = sourceRoot;
                        firstComponentSource = sourceComponent;
                    }
                    else if (sourceRoot != firstPrefabSource || sourceComponent != firstComponentSource)
                    {
                        throw new InvalidOperationException("Ammo pickup instances must share one prefab root/component source.");
                    }
                    ValidatePersistentIdentity(sourceRoot, pickup.name + " prefab source");
                    ValidatePersistentIdentity(sourceComponent, pickup.name + " prefab component source");
                });
                var trigger = pickup.GetComponent<SphereCollider>();
                var body = pickup.GetComponent<Rigidbody>();
                var visualRoot = pickup.transform.Find("VisualRoot");
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".pickupTrigger", pickup, "pickupTrigger", trigger);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".visualRoot", pickup, "visualRoot", visualRoot != null ? visualRoot.gameObject : null);
                CaptureReference(accumulator, "gameplay/wiring", pickup.name + ".match", pickup, "match", context.Match);
                CaptureSerialized(accumulator, "gameplay/serialized", pickup.name + ".respawnDelay", pickup, "respawnDelay", MovementLabContract.AmmoPickupRespawnDelay);
                CaptureSerializedInteger(accumulator, "gameplay/serialized", pickup.name + ".grant", pickup, "grant", MovementLabContract.AmmoPickupGrant);
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".physics", () =>
                {
                    if (context.Match == null || trigger == null || !trigger.enabled || !trigger.isTrigger || Mathf.Abs(trigger.radius - MovementLabContract.AmmoPickupTriggerRadius) > 0.001f ||
                        body == null || !body.isKinematic || body.useGravity || body.constraints != RigidbodyConstraints.FreezeAll ||
                        pickup.GetComponentsInChildren<Collider>(true).Length != 1 || pickup.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                        throw new InvalidOperationException(pickup.name + " collider/body/match contract invalid.");
                });
                accumulator.Capture("scene/ammo-pickups", pickup.name + ".visual", () =>
                {
                    if (visualRoot == null || visualRoot.parent != pickup.transform || visualRoot.childCount != 4)
                        throw new InvalidOperationException(pickup.name + " visual hierarchy contract invalid.");
                    var shellMaterial = AssetDatabase.LoadAssetAtPath<Material>(AmmoShellMaterialPath);
                    for (var shellIndex = 0; shellIndex < 2; shellIndex++)
                    {
                        var shellName = shellIndex == 0 ? "ShellLeft" : "ShellRight";
                        var shell = visualRoot.Find(shellName);
                        var renderer = shell != null ? shell.GetComponent<MeshRenderer>() : null;
                        var filter = shell != null ? shell.GetComponent<MeshFilter>() : null;
                        var expectedPosition = shellIndex == 0 ? MovementLabContract.AmmoShellLeftPosition : MovementLabContract.AmmoShellRightPosition;
                        if (shell == null || filter == null || renderer == null || filter.sharedMesh == null || filter.sharedMesh.name != "Capsule" ||
                            shell.localPosition != expectedPosition || shell.localScale != MovementLabContract.AmmoShellScale || renderer.sharedMaterials == null ||
                            renderer.sharedMaterials.Length != 1 || renderer.sharedMaterial != shellMaterial || shell.GetComponents<MonoBehaviour>().Length != 0)
                            throw new InvalidOperationException(pickup.name + " shell visual contract invalid: " + shellName);
                    }
                    ValidateScenePickupCuePair(visualRoot, pickup.name);
                    if (visualRoot.GetComponentsInChildren<Collider>(true).Length != 0 || visualRoot.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<Light>(true).Length != 0 || visualRoot.GetComponentsInChildren<ParticleSystem>(true).Length != 0 ||
                        visualRoot.GetComponentsInChildren<Animator>(true).Length != 0 || visualRoot.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                        throw new InvalidOperationException(pickup.name + " visual hierarchy contains forbidden components.");
                    MovementLabPrefabPipeline.ValidateDynamicHierarchy(pickup.gameObject, pickup.name);
                });
            }
            accumulator.Capture("scene/ammo-pickups", "point-mirror", () =>
            {
                var west = context.AmmoPickups.FirstOrDefault(pickup => pickup != null && pickup.name == AmmoPickupWestNorthName);
                var east = context.AmmoPickups.FirstOrDefault(pickup => pickup != null && pickup.name == AmmoPickupEastSouthName);
                var mirroredEast = west != null ? new Vector3(-west.transform.position.x, west.transform.position.y, -west.transform.position.z) : Vector3.zero;
                if (west == null || east == null || Vector3.Distance(east.transform.position, mirroredEast) > 0.001f ||
                    Quaternion.Angle(east.transform.rotation, MovementLabContract.AmmoPickupEastSouthRotation) > 0.1f)
                    throw new InvalidOperationException("Ammo pickup placements must be distinct point mirrors across the arena origin.");
            });
        }

        private static void ValidateScenePickupCuePair(Transform visualRoot, string label)
        {
            var blue = visualRoot.Find("BlueCircleCue");
            var red = visualRoot.Find("RedTriangleCue");
            if (blue == null || red == null)
                throw new InvalidOperationException(label + " cue pair is incomplete.");
            MovementLabPrefabPipeline.ValidateShapeCue(blue, BlueCircleCueMeshPath, label + " BlueCircleCue");
            MovementLabPrefabPipeline.ValidateShapeCue(red, RedTriangleCueMeshPath, label + " RedTriangleCue");
            var blueRenderer = blue.GetComponent<MeshRenderer>();
            var redRenderer = red.GetComponent<MeshRenderer>();
            if (blueRenderer.sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(TeamBlueMaterialPath) ||
                redRenderer.sharedMaterial != AssetDatabase.LoadAssetAtPath<Material>(TeamRedMaterialPath) ||
                blue.localScale != MovementLabContract.PickupCueScale || red.localScale != MovementLabContract.PickupCueScale ||
                blue.localPosition != MovementLabContract.PickupCueBluePosition || red.localPosition != MovementLabContract.PickupCueRedPosition ||
                blue.GetComponents<MonoBehaviour>().Length != 0 || red.GetComponents<MonoBehaviour>().Length != 0)
                throw new InvalidOperationException(label + " cue pair contract invalid.");
        }

        private static void ValidateBallTouchRosterContract()
        {
            var ballType = typeof(BallMotor);
            var rosterProperty = ballType.GetProperty(nameof(BallMotor.Participants), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var touchProperty = ballType.GetProperty(nameof(BallMotor.LastTouchParticipant), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (rosterProperty == null || !rosterProperty.CanRead || touchProperty == null || !touchProperty.CanRead)
                throw new InvalidOperationException("BallMotor must expose participant roster and last-touch read references.");
        }

        private static void ValidateDashKickInputAsset()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null) throw new InvalidOperationException("Dash-kick input asset is missing: " + InputActionsPath);
            var map = actions.FindActionMap("Player", false);
            var action = map != null ? map.FindAction("Kick", false) : null;
            if (action == null) throw new InvalidOperationException("Player/Kick input action is missing.");

            const string keyboardBindingId = "e7a4b39f-0bf4-49a6-85be-d871f11eb875";
            const string gamepadBindingId = "31f91925-cf1d-4aa2-b3ca-c7200dd7781c";
            var keyboardBindingGuid = Guid.Parse(keyboardBindingId);
            var gamepadBindingGuid = Guid.Parse(gamepadBindingId);
            var keyboardPathCount = 0;
            var gamepadPathCount = 0;
            var keyboardBindingCount = 0;
            var gamepadBindingCount = 0;
            for (var i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];
                if (string.Equals(binding.path, "<Mouse>/rightButton", StringComparison.Ordinal))
                    throw new InvalidOperationException("Player/Kick must not retain the RMB binding.");
                if (string.Equals(binding.path, "<Keyboard>/f", StringComparison.Ordinal)) keyboardPathCount++;
                if (string.Equals(binding.path, "<Gamepad>/buttonWest", StringComparison.Ordinal)) gamepadPathCount++;
                if (binding.id == keyboardBindingGuid)
                {
                    keyboardBindingCount++;
                    if (binding.path != "<Keyboard>/f" || binding.groups != ";Keyboard&Mouse" || binding.action != "Kick")
                        throw new InvalidOperationException("Player/Kick keyboard binding contract changed.");
                }
                if (binding.id == gamepadBindingGuid)
                {
                    gamepadBindingCount++;
                    if (binding.path != "<Gamepad>/buttonWest" || binding.groups != ";Gamepad" || binding.action != "Kick")
                        throw new InvalidOperationException("Player/Kick gamepad binding contract changed.");
                }
            }
            if (keyboardPathCount != 1 || keyboardBindingCount != 1)
                throw new InvalidOperationException("Player/Kick must contain exactly one preserved Keyboard F binding.");
            if (gamepadPathCount != 1 || gamepadBindingCount != 1)
                throw new InvalidOperationException("Player/Kick must contain exactly one preserved Gamepad west binding.");
        }

        private static void ValidateMatchTableInputAsset()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null) throw new InvalidOperationException("Match-table input asset is missing: " + InputActionsPath);
            var map = actions.FindActionMap("Player", false);
            if (map == null) throw new InvalidOperationException("Player input map is missing.");

            var matchingActions = map.actions.Where(action => action != null && action.name == "MatchTable").ToArray();
            if (matchingActions.Length != 1) throw new InvalidOperationException("Player/MatchTable must exist exactly once.");
            var action = matchingActions[0];
            if (action.type != InputActionType.Button)
                throw new InvalidOperationException("Player/MatchTable must be a Button action.");

            var expectedActionId = Guid.Parse("c15f83ad-0f95-44f2-9f5e-4cfe9a0f2bd5");
            if (action.id != expectedActionId)
                throw new InvalidOperationException("Player/MatchTable action GUID changed.");

            var tabPathBindings = map.bindings.Where(binding => string.Equals(binding.path, "<Keyboard>/tab", StringComparison.Ordinal)).ToArray();
            if (tabPathBindings.Length != 1 || !string.Equals(tabPathBindings[0].groups, ";Keyboard&Mouse", StringComparison.Ordinal))
                throw new InvalidOperationException("Player must contain exactly one Keyboard&Mouse Tab binding.");

            var binding = tabPathBindings[0];
            var expectedBindingId = Guid.Parse("b8a8bc7b-14f4-4c5c-bd8c-02c4cb726104");
            if (binding.id != expectedBindingId || binding.action != "MatchTable")
                throw new InvalidOperationException("Player/MatchTable Tab binding GUID or action changed.");

            var readerProperty = typeof(PlayerInputReader).GetProperty(
                nameof(PlayerInputReader.MatchTableHeld),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (readerProperty == null || !readerProperty.CanRead || readerProperty.PropertyType != typeof(bool))
                throw new InvalidOperationException("PlayerInputReader.MatchTableHeld public bool read surface is missing.");
        }

        private static void ValidateMatchHudSerializedSurface()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var fields = typeof(MatchHud).GetFields(flags)
                .Where(field => field.IsDefined(typeof(SerializeField), true))
                .ToArray();
            var expected = new Dictionary<string, Type>
            {
                { "match", typeof(MatchController) },
                { "localParticipant", typeof(ParticipantState) },
                { "input", typeof(PlayerInputReader) }
            };
            if (fields.Length != expected.Count)
                throw new InvalidOperationException("MatchHud serialized dependency surface must contain exactly match, localParticipant, and input.");
            foreach (var field in fields)
            {
                if (!expected.TryGetValue(field.Name, out var expectedType) || field.FieldType != expectedType)
                    throw new InvalidOperationException("MatchHud has an unexpected serialized dependency: " + field.Name + ".");
            }
        }

        private static void ValidateMatchHudScreenPolicy()
        {
            ExpectMatchHudScreen("Final dominates all flags", MatchController.MatchState.Final, false, true, true, MatchHudScreen.Final);
            ExpectMatchHudScreen("GoalFreeze dominates death/Tab/GO", MatchController.MatchState.GoalFreeze, false, true, true, MatchHudScreen.GoalSummary);
            ExpectMatchHudScreen("OpeningCountdown dominates death/Tab/GO", MatchController.MatchState.OpeningCountdown, false, true, true, MatchHudScreen.OpeningRulesCountdown);
            ExpectMatchHudScreen("KickoffCountdown dominates death/Tab/GO", MatchController.MatchState.KickoffCountdown, false, true, true, MatchHudScreen.KickoffCountdown);
            ExpectMatchHudScreen("Reset dominates all flags", MatchController.MatchState.Reset, false, true, true, MatchHudScreen.Resetting);
            ExpectMatchHudScreen("GO follows Playing", MatchController.MatchState.Playing, true, false, true, MatchHudScreen.Go);
            ExpectMatchHudScreen("Local death dominates Tab", MatchController.MatchState.Playing, false, true, false, MatchHudScreen.LocalDeath);
            ExpectMatchHudScreen("Alive Tab opens table", MatchController.MatchState.Playing, true, true, false, MatchHudScreen.MatchTable);
            ExpectMatchHudScreen("Playing fallback is live", MatchController.MatchState.Playing, true, false, false, MatchHudScreen.Live);
        }

        private static void ExpectMatchHudScreen(string label, MatchController.MatchState state,
            bool localAlive, bool matchTableHeld, bool goVisible, MatchHudScreen expected)
        {
            var actual = MatchHudScreenPolicy.Resolve(state, localAlive, matchTableHeld, goVisible);
            if (actual != expected)
                throw new InvalidOperationException("MatchHud screen policy row failed (" + label + "): expected " + expected + ", got " + actual + ".");
        }

        private static void ValidateDashPublicSurface()
        {
            const System.Reflection.BindingFlags publicInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            var motorType = typeof(PlayerMotor);
            var requiredProperties = new[]
            {
                nameof(PlayerMotor.IsDashing), nameof(PlayerMotor.DashRemaining), nameof(PlayerMotor.DashElapsed),
                nameof(PlayerMotor.DashDirection), nameof(PlayerMotor.AirDashAvailable)
            };
            for (var i = 0; i < requiredProperties.Length; i++)
            {
                var property = motorType.GetProperty(requiredProperties[i], publicInstance);
                if (property == null || !property.CanRead) throw new InvalidOperationException("PlayerMotor dash diagnostic is missing: " + requiredProperties[i]);
            }
            var requiredMethods = new[] { nameof(PlayerMotor.TryStartDash), nameof(PlayerMotor.SetDashAim), nameof(PlayerMotor.EndDash) };
            for (var i = 0; i < requiredMethods.Length; i++)
                if (motorType.GetMethod(requiredMethods[i], publicInstance) == null) throw new InvalidOperationException("PlayerMotor dash operation is missing: " + requiredMethods[i]);
            if (typeof(BallKick).GetEvent(nameof(BallKick.DashStarted), publicInstance) == null)
                throw new InvalidOperationException("BallKick.DashStarted event is missing.");
        }

        private static void CaptureDashTuning(MovementLabValidationAccumulator accumulator, ParticipantState participant, int participantIndex)
        {
            if (participant == null || participant.Motor == null || participant.Kick == null || participant.CameraFeedback == null)
                return;

            var label = "Participant[" + participantIndex + "]";
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerMotor.dashBurstSpeed", participant.Motor, "dashBurstSpeed", PlayerMotorDefaults.DashBurstSpeed);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerMotor.dashDuration", participant.Motor, "dashDuration", PlayerMotorDefaults.DashDuration);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerMotor.dashSteerRateDegrees", participant.Motor, "dashSteerRateDegrees", PlayerMotorDefaults.DashSteerRateDegrees);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerMotor.dashSpeedCap", participant.Motor, "dashSpeedCap", PlayerMotorDefaults.DashSpeedCap);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.dashContactStartDelay", participant.Kick, "dashContactStartDelay", BallKickDefaults.DashContactStartDelay);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.dashContactReach", participant.Kick, "dashContactReach", BallKickDefaults.DashContactReach);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.dashContactRadiusPadding", participant.Kick, "dashContactRadiusPadding", BallKickDefaults.DashContactRadiusPadding);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.cooldown", participant.Kick, "cooldown", BallKickDefaults.Cooldown);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.speedFraction", participant.Kick, "speedFraction", BallKickDefaults.SpeedFraction);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.playerMomentumShare", participant.Kick, "playerMomentumShare", BallKickDefaults.PlayerMomentumShare);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.enemyContactDamage", participant.Kick, "enemyContactDamage", BallKickDefaults.EnemyContactDamage);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.enemyShoveImpulse", participant.Kick, "enemyShoveImpulse", BallKickDefaults.EnemyShoveImpulse);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".BallKick.enemyDashRetention", participant.Kick, "enemyDashRetention", BallKickDefaults.EnemyDashRetention);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerCameraFeedback.dashKickImpulse", participant.CameraFeedback, "dashKickImpulse", PlayerCameraFeedback.DefaultDashKickImpulse);
            CaptureSerialized(accumulator, "gameplay/serialized", label + ".PlayerCameraFeedback.dashKickImpulseDuration", participant.CameraFeedback, "dashKickImpulseDuration", PlayerCameraFeedback.DefaultDashKickImpulseDuration);
        }

        private static void ValidateMatchPublicContract(MatchController match)
        {
            if (match == null)
                throw new InvalidOperationException("MatchController public contract requires a reopened MatchController instance.");

            var matchType = typeof(MatchController);
            var publicInstance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            var compatibilityProperties = new[]
            {
                nameof(MatchController.State),
                nameof(MatchController.NorthScore),
                nameof(MatchController.SouthScore),
                nameof(MatchController.FreezeRemaining)
            };
            for (var i = 0; i < compatibilityProperties.Length; i++)
            {
                var property = matchType.GetProperty(compatibilityProperties[i], publicInstance);
                if (property == null || !property.CanRead)
                    throw new InvalidOperationException("MatchController diagnostics compatibility property missing: " + compatibilityProperties[i] + ".");
            }

            var recoveryThreshold = matchType.GetProperty(nameof(MatchController.ParticipantRecoveryThreshold), publicInstance);
            if (recoveryThreshold == null || recoveryThreshold.PropertyType != typeof(float) || !recoveryThreshold.CanRead)
                throw new InvalidOperationException("MatchController participant recovery threshold surface is missing or changed.");

            if (!Enum.IsDefined(typeof(MatchController.MatchState), MatchController.MatchState.GoalFreeze))
                throw new InvalidOperationException("MatchController.MatchState.GoalFreeze compatibility value is missing.");

            var teamReadProperties = new[]
            {
                nameof(MatchController.BlueGoals),
                nameof(MatchController.RedGoals),
                nameof(MatchController.BlueTeamFrags),
                nameof(MatchController.RedTeamFrags)
            };
            for (var i = 0; i < teamReadProperties.Length; i++)
            {
                var property = matchType.GetProperty(teamReadProperties[i], publicInstance);
                if (property == null || !property.CanRead)
                    throw new InvalidOperationException("MatchController Blue/Red read property missing: " + teamReadProperties[i] + ".");
            }

            var botReadProperties = new[]
            {
                nameof(MatchController.SelectedBotsEnabled), nameof(MatchController.LockedBotsEnabled), nameof(MatchController.BotsEnabled),
                nameof(MatchController.ConfigurationLocked), nameof(MatchController.DifficultyLocked),
                nameof(MatchController.SelectedEnemyDifficulty), nameof(MatchController.LockedEnemyDifficulty)
            };
            for (var i = 0; i < botReadProperties.Length; i++)
            {
                var property = matchType.GetProperty(botReadProperties[i], publicInstance);
                if (property == null || !property.CanRead)
                    throw new InvalidOperationException("MatchController bot setup read property missing: " + botReadProperties[i] + ".");
            }

            if (match.SelectedBotsEnabled != BotsEnabledByDefault)
                throw new InvalidOperationException("MatchController SelectedBotsEnabled must default to true.");
            if (match.LockedBotsEnabled != BotsEnabledByDefault)
                throw new InvalidOperationException("MatchController LockedBotsEnabled must default to true.");
            if (match.BotsEnabled != BotsEnabledByDefault)
                throw new InvalidOperationException("MatchController BotsEnabled must default to true.");
            if (match.ConfigurationLocked)
                throw new InvalidOperationException("MatchController ConfigurationLocked must default to false.");
            if (match.DifficultyLocked)
                throw new InvalidOperationException("MatchController DifficultyLocked must default to false.");
            if (match.SelectedEnemyDifficulty != BotDifficulty.Medium)
                throw new InvalidOperationException("MatchController SelectedEnemyDifficulty must default to Medium.");
            if (match.LockedEnemyDifficulty != BotDifficulty.Medium)
                throw new InvalidOperationException("MatchController LockedEnemyDifficulty must default to Medium.");

            var resetEvent = matchType.GetEvent(nameof(MatchController.CoordinatedResetRequested), publicInstance);
            if (resetEvent == null)
                throw new InvalidOperationException("MatchController.CoordinatedResetRequested event is missing.");
            var rematchMethod = matchType.GetMethod(nameof(MatchController.TryStartRematch), publicInstance, null, Type.EmptyTypes, null);
            var exitMethod = matchType.GetMethod(nameof(MatchController.RequestExit), publicInstance, null, Type.EmptyTypes, null);
            if (rematchMethod == null || rematchMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("MatchController.TryStartRematch public method is missing.");
            if (exitMethod == null || exitMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("MatchController.RequestExit public method is missing.");
        }

        private static void ValidateParticipantRoster(ValidationContext context, MovementLabValidationAccumulator accumulator)
        {
            if (context == null || !context.SceneReady || context.Participants == null) return;
            accumulator.Capture("scene/roster", "count", () =>
            {
                var all = UnityEngine.Object.FindObjectsByType<ParticipantState>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (all.Length != ParticipantSlots.Length) throw new InvalidOperationException("MovementLab must contain exactly six ParticipantState components.");
            });

            var participantLayer = LayerMask.NameToLayer(MovementLabContract.ParticipantsLayerName);
            var projectilesLayer = LayerMask.NameToLayer(MovementLabContract.ProjectilesLayerName);
            var hiddenLayer = LayerMask.NameToLayer(MovementLabContract.LocalPlayerHiddenLayerName);
            var blueCount = 0;
            var redCount = 0;
            var localCount = 0;
            accumulator.Capture("scene/layers", "names", () =>
            {
                if (participantLayer < 0 || projectilesLayer < 0 || hiddenLayer < 0)
                    throw new InvalidOperationException("Participants, Projectiles, and LocalPlayerHidden layers are required.");
                if (Physics.GetIgnoreLayerCollision(participantLayer, participantLayer) || Physics.GetIgnoreLayerCollision(participantLayer, projectilesLayer) || Physics.GetIgnoreLayerCollision(projectilesLayer, projectilesLayer))
                    throw new InvalidOperationException("Participants/Projectiles collision matrix must remain enabled.");
            });

             var localCameraCount = 0;
             var localAudioCount = 0;
             var localParticipant = context.Participants.FirstOrDefault(item => item != null && item.IsLocalParticipant);
             var localCamera = localParticipant != null ? localParticipant.GetComponentInChildren<Camera>(true) : null;
             var sceneMatch = context.Match != null ? context.Match : context.MatchObject != null ? context.MatchObject.GetComponent<MatchController>() : null;
             for (var i = 0; i < context.Participants.Length; i++)
            {
                var participant = context.Participants[i];
                var expected = ParticipantSlots[i];
                if (participant == null) continue;
                if (participant.Team == ParticipantTeam.Blue) blueCount++;
                if (participant.Team == ParticipantTeam.Red) redCount++;
                if (participant.IsLocalParticipant) localCount++;
                accumulator.Capture("scene/roster", "identity:" + expected.SlotId, () =>
                {
                    ValidatePersistentIdentity(participant, expected.DisplayName);
                    if (participant.SlotId != expected.SlotId || participant.DisplayName != expected.DisplayName || participant.Team != expected.Team || participant.IsLocalParticipant != expected.IsLocal)
                        throw new InvalidOperationException("Participant slot identity mismatch: " + expected.SlotId);
                    if (Vector3.Distance(participant.transform.position, expected.Position) > 0.01f || Vector3.Dot(participant.transform.forward, expected.Rotation * Vector3.forward) < 0.999f)
                        throw new InvalidOperationException("Participant authored transform mismatch: " + expected.DisplayName);
                    if (participantLayer < 0 || participant.gameObject.layer != participantLayer)
                        throw new InvalidOperationException("Participant root must use Participants layer: " + expected.DisplayName);
                    var source = PrefabUtility.GetCorrespondingObjectFromSource(participant.gameObject);
                    if (source == null || AssetDatabase.GetAssetPath(source) != PrefabPath)
                        throw new InvalidOperationException("Participant scene instance prefab provenance mismatch: " + expected.DisplayName);
                });
                accumulator.Capture("scene/roster", "refs:" + expected.SlotId, () =>
                {
                    ValidateReference(participant, "motor", participant.Motor, expected.DisplayName + ".motor");
                    ValidateReference(participant, "characterController", participant.CharacterController, expected.DisplayName + ".characterController");
                    ValidateReference(participant, "presentation", participant.Presentation, expected.DisplayName + ".presentation");
                    ValidateReference(participant, "cameraFeedback", participant.CameraFeedback, expected.DisplayName + ".cameraFeedback");
                    ValidateReference(participant.Launcher, "ownerParticipant", participant, expected.DisplayName + ".launcher.ownerParticipant");
                     ValidateReference(participant.CameraFeedback, "participant", participant, expected.DisplayName + ".cameraFeedback.participant");
                     ValidateReference(participant.Presentation, "participant", participant, expected.DisplayName + ".presentation.participant");
                      ValidateReference(participant, "shotgun", participant.Shotgun, expected.DisplayName + ".shotgun");
                      ValidateReference(participant.Shotgun, "ownerParticipant", participant, expected.DisplayName + ".shotgun.ownerParticipant");
                     var participantCamera = participant.GetComponentInChildren<Camera>(true);
                     ValidateReference(participant.Kick, "aimCamera", participantCamera, expected.DisplayName + ".kick.aimCamera");
                     ValidateReference(participant.Presentation, "gameplayCamera", participantCamera, expected.DisplayName + ".presentation.gameplayCamera");
                     ValidateReference(participant.CameraFeedback, "targetCamera", participantCamera, expected.DisplayName + ".cameraFeedback.targetCamera");
                     var participantFpsShotgun = participant.transform.Find("Head/Camera/Viewmodels/FpsShotgunVisual");
                     var participantWorldVisual = participant.transform.Find("WorldVisual");
                     var participantWorldMount = MovementLabPrefabPipeline.FindNamedTransform(participantWorldVisual, "WorldShotgunMount");
                     var participantWorldShotgun = MovementLabPrefabPipeline.FindNamedTransform(participantWorldMount, "WorldShotgunVisual");
                      ValidateReference(participant.Presentation, "fpsShotgunVisual", participantFpsShotgun, expected.DisplayName + ".presentation.fpsShotgunVisual");
                      ValidateReference(participant.Presentation, "worldShotgunVisual", participantWorldShotgun, expected.DisplayName + ".presentation.worldShotgunVisual");
                      ValidateReference(participant.Presentation, "shotgun", participant.Shotgun, expected.DisplayName + ".presentation.shotgun");
                      ValidateSceneParticipantComposition(participant, localParticipant, sceneMatch, localCamera, expected.DisplayName);
                      MovementLabPrefabPipeline.ValidateImportedVisual(participantFpsShotgun.gameObject, FpsShotgunModelPath, expected.DisplayName + ".FpsShotgunVisual");
                     MovementLabPrefabPipeline.ValidateImportedVisual(participantWorldShotgun.gameObject, ShotgunModelPath, expected.DisplayName + ".WorldShotgunVisual");
                     MovementLabMaterialPipeline.ValidateShotgunMaterials(participantFpsShotgun.gameObject);
                     MovementLabMaterialPipeline.ValidateShotgunMaterials(participantWorldShotgun.gameObject);
                 });
                 CaptureSerialized(accumulator, "scene/roster", "health:" + expected.SlotId, participant, "maxHealth", ParticipantState.DefaultMaxHealth);
                  CaptureSerialized(accumulator, "scene/roster", "death-wait:" + expected.SlotId, participant, "deathWait", expected.IsLocal ? LocalRespawnDelay : BotRespawnDelay);
                 CaptureSerialized(accumulator, "scene/roster", "immunity:" + expected.SlotId, participant, "immunityDuration", ParticipantState.DefaultImmunityDuration);
                 CaptureSerializedInteger(accumulator, "scene/roster", "shotgun-capacity:" + expected.SlotId, participant, "shotgunShellCapacity", ParticipantState.DefaultShotgunShellCapacity);
                var camera = participant.GetComponentInChildren<Camera>(true);
                var listener = participant.GetComponentInChildren<AudioListener>(true);
                if (camera != null && camera.enabled) localCameraCount++;
                if (listener != null && listener.enabled) localAudioCount++;
                accumulator.Capture("scene/roster", "local-mode:" + expected.SlotId, () =>
                {
                    if (camera == null || listener == null || participant.Input == null || participant.Look == null || participant.CameraFeedback == null)
                        throw new InvalidOperationException("Participant local-control references missing: " + expected.DisplayName);
                    if (camera.enabled != expected.IsLocal || listener.enabled != expected.IsLocal || participant.Input.enabled != expected.IsLocal || participant.Look.enabled != expected.IsLocal || participant.CameraFeedback.enabled != expected.IsLocal)
                        throw new InvalidOperationException("Participant local-control mode mismatch: " + expected.DisplayName);
                    var viewmodels = participant.transform.Find("Head/Camera/Viewmodels");
                    var crosshair = participant.transform.Find("Head/Camera/CrosshairCanvas");
                    if (viewmodels == null || crosshair == null || viewmodels.gameObject.activeSelf != expected.IsLocal || crosshair.gameObject.activeSelf != expected.IsLocal)
                        throw new InvalidOperationException("Participant FPS-only presentation mode mismatch: " + expected.DisplayName);
                     var worldVisual = participant.transform.Find("WorldVisual");
                     if (worldVisual == null) throw new InvalidOperationException("Participant WorldVisual missing: " + expected.DisplayName);
                     var fpsShotgun = participant.transform.Find("Head/Camera/Viewmodels/FpsShotgunVisual");
                     var worldMount = MovementLabPrefabPipeline.FindNamedTransform(worldVisual, "WorldShotgunMount");
                     var worldShotgun = MovementLabPrefabPipeline.FindNamedTransform(worldMount, "WorldShotgunVisual");
                      if (fpsShotgun == null || worldMount == null || worldShotgun == null || fpsShotgun.gameObject.activeSelf != expected.IsLocal || !worldShotgun.gameObject.activeSelf)
                         throw new InvalidOperationException("Participant shotgun presentation mode mismatch: " + expected.DisplayName);
                     MovementLabPrefabPipeline.ValidateShotgunPresentation(participant.gameObject, camera, fpsShotgun, worldVisual, worldMount, worldShotgun, expected.DisplayName);
                     MovementLabPrefabPipeline.ValidateTeamTintRenderers(participant.Presentation, worldVisual, worldMount,
                         MovementLabPrefabPipeline.FindRendererByName(worldShotgun.gameObject, "WeaponAccent"),
                         MovementLabPrefabPipeline.FindRendererByName(worldShotgun.gameObject, "WeaponAccentCore"),
                         expected.DisplayName + ".presentation.teamTintRenderers");
                     var worldLayers = worldVisual.GetComponentsInChildren<Transform>(true);
                    for (var layerIndex = 0; layerIndex < worldLayers.Length; layerIndex++)
                    {
                        var expectedLayer = expected.IsLocal ? hiddenLayer : 0;
                        if (worldLayers[layerIndex].gameObject.layer != expectedLayer) throw new InvalidOperationException("Participant WorldVisual layer mismatch: " + expected.DisplayName);
                    }
                });
            }
            accumulator.Capture("scene/roster", "team-balance", () =>
            {
                if (blueCount != 3 || redCount != 3 || localCount != 1 || context.Participants[0] == null || context.Participants[0].Team != ParticipantTeam.Blue)
                    throw new InvalidOperationException("MovementLab roster must contain three Blue, three Red, and one local Blue participant.");
            });
            accumulator.Capture("scene/roster", "local-camera-count", () => { if (localCameraCount != 1) throw new InvalidOperationException("Exactly one enabled gameplay Camera is required."); });
            accumulator.Capture("scene/roster", "local-audio-count", () => { if (localAudioCount != 1) throw new InvalidOperationException("Exactly one enabled gameplay AudioListener is required."); });

            if (context.SpawnSet != null)
            {
                 CaptureSpawnSetContracts(context, accumulator, participantLayer, projectilesLayer);
             }
         }

         private static void ValidateSceneParticipantComposition(ParticipantState participant,
             ParticipantState localParticipant, MatchController match, Camera localCamera, string label)
         {
             if (participant == null || localParticipant == null || match == null || localCamera == null || participant.Presentation == null)
                 throw new InvalidOperationException(label + " presentation composition dependencies are missing.");

             if (Vector3.Distance(participant.transform.localScale, Vector3.one) > 0.001f)
                 throw new InvalidOperationException(label + " root scale must remain unit scale.");

             var controller = participant.GetComponent<CharacterController>();
             if (controller == null || Mathf.Abs(controller.radius - PlayerControllerRadius) > 0.001f ||
                 Mathf.Abs(controller.height - PlayerControllerHeight) > 0.001f ||
                 Vector3.Distance(controller.center, PlayerControllerCenter) > 0.001f ||
                 Mathf.Abs(controller.skinWidth - PlayerControllerSkinWidth) > 0.001f)
                 throw new InvalidOperationException(label + " CharacterController composition mismatch.");

             var head = participant.transform.Find("Head");
             var worldVisual = participant.transform.Find("WorldVisual");
             if (head == null || Vector3.Distance(head.localPosition, new Vector3(0f, PlayerHeadHeight, 0f)) > 0.001f)
                 throw new InvalidOperationException(label + " Head height must match the enlarged player contract.");
             if (worldVisual == null || Vector3.Distance(worldVisual.localScale, Vector3.one * WorldVisualScale) > 0.001f)
                 throw new InvalidOperationException(label + " WorldVisual scale must be doubled.");

             var blueCue = participant.transform.Find("BlueCircleCue");
             var redCue = participant.transform.Find("RedTriangleCue");
             var blueShield = participant.transform.Find("ImmunityShield/BlueImmunityShield");
             var redShield = participant.transform.Find("ImmunityShield/RedImmunityShield");
             if (blueCue == null || redCue == null || blueShield == null || redShield == null ||
                 Vector3.Distance(blueCue.localScale, new Vector3(0.84f, 0.84f, 2f)) > 0.001f ||
                 Vector3.Distance(redCue.localScale, Vector3.one * 2f) > 0.001f ||
                 Vector3.Distance(blueShield.localScale, new Vector3(2.4f, 4f, 2.4f)) > 0.001f ||
                 Vector3.Distance(redShield.localScale, new Vector3(2.4f, 4f, 2.4f)) > 0.001f)
                 throw new InvalidOperationException(label + " team cue/immunity scale contract invalid.");

             var isEnemy = participant.Team != localParticipant.Team;
             if (blueCue.gameObject.activeSelf != (participant.Team == ParticipantTeam.Blue) ||
                 redCue.gameObject.activeSelf != (participant.Team == ParticipantTeam.Red))
                 throw new InvalidOperationException(label + " team cue activation mismatch.");

             var nameplate = participant.transform.Find("Nameplate");
             var nameplateText = nameplate != null ? nameplate.GetComponent<TextMesh>() : null;
             if (nameplate == null || nameplateText == null ||
                 Vector3.Distance(nameplate.localPosition, new Vector3(0f, NameplateHeight, 0f)) > 0.001f ||
                 nameplate.GetComponentsInChildren<Collider>(true).Length != 0 ||
                 nameplateText.text != participant.DisplayName)
                 throw new InvalidOperationException(label + " Nameplate must be collider-free, raised, and use immutable DisplayName.");

             var presentation = participant.Presentation;
             ValidateReference(presentation, "localParticipant", localParticipant, label + ".presentation.localParticipant");
             ValidateReference(presentation, "match", match, label + ".presentation.match");
             ValidateReference(presentation, "nicknameCamera", localCamera, label + ".presentation.nicknameCamera");
             ValidateReference(presentation, "nicknameVisual", nameplate.gameObject, label + ".presentation.nicknameVisual");
             ValidateReference(presentation, "nicknameText", nameplateText, label + ".presentation.nicknameText");
             var serialized = new SerializedObject(presentation);
             var showNickname = serialized.FindProperty("showNickname");
             var spawnCorpse = serialized.FindProperty("spawnCorpseOnDeath");
             var corpseLifetime = serialized.FindProperty("corpseLifetime");
             if (showNickname == null || showNickname.propertyType != SerializedPropertyType.Boolean || showNickname.boolValue != isEnemy ||
                 spawnCorpse == null || spawnCorpse.propertyType != SerializedPropertyType.Boolean || spawnCorpse.boolValue != isEnemy ||
                 corpseLifetime == null || corpseLifetime.propertyType != SerializedPropertyType.Float || Mathf.Abs(corpseLifetime.floatValue - 30f) > 0.001f ||
                 nameplate.gameObject.activeSelf != isEnemy)
                 throw new InvalidOperationException(label + " nickname/corpse enemy-only policy mismatch.");
         }

         private static void CaptureSpawnSetContracts(ValidationContext context, MovementLabValidationAccumulator accumulator, int participantLayer, int projectilesLayer)
        {
            var spawnSet = context.SpawnSet;
            accumulator.Capture("scene/spawn-set", "arrays", () =>
            {
                if (spawnSet.BlueCandidates == null || spawnSet.BlueCandidates.Count != 3 || spawnSet.RedCandidates == null || spawnSet.RedCandidates.Count != 3)
                    throw new InvalidOperationException("ParticipantSpawnSet requires three Blue and three Red candidates.");
                for (var i = 0; i < 3; i++)
                {
                    if (spawnSet.BlueCandidates[i] == null || spawnSet.RedCandidates[i] == null) throw new InvalidOperationException("ParticipantSpawnSet candidate is null.");
                    if (Vector3.Distance(spawnSet.BlueCandidates[i].position, ParticipantSlots[i].Position) > 0.01f || Vector3.Distance(spawnSet.RedCandidates[i].position, ParticipantSlots[i + 3].Position) > 0.01f)
                        throw new InvalidOperationException("ParticipantSpawnSet candidate transform mismatch.");
                    ValidateRecoverySpawnGeometry(spawnSet.BlueCandidates[i], "BlueSpawn_" + i);
                    ValidateRecoverySpawnGeometry(spawnSet.RedCandidates[i], "RedSpawn_" + i);
                    var blueCue = spawnSet.BlueCandidates[i].Find("BlueCircleCue");
                    var redCue = spawnSet.RedCandidates[i].Find("RedTriangleCue");
                    if (blueCue == null || redCue == null || blueCue.GetComponent<MeshFilter>()?.sharedMesh == null || redCue.GetComponent<MeshFilter>()?.sharedMesh == null || AssetDatabase.GetAssetPath(blueCue.GetComponent<MeshFilter>().sharedMesh) != BlueCircleCueMeshPath || AssetDatabase.GetAssetPath(redCue.GetComponent<MeshFilter>().sharedMesh) != RedTriangleCueMeshPath)
                        throw new InvalidOperationException("ParticipantSpawnSet shape cue missing.");
                }
                if (spawnSet.BlueEnemyGoal == null || spawnSet.RedEnemyGoal == null || spawnSet.BlueEnemyGoal.name != "NorthGoal" || spawnSet.RedEnemyGoal.name != "SouthGoal")
                    throw new InvalidOperationException("ParticipantSpawnSet enemy-goal mapping mismatch.");
                var expectedMask = ~(1 << participantLayer | 1 << projectilesLayer);
                if (spawnSet.VisibilityMask.value != expectedMask) throw new InvalidOperationException("ParticipantSpawnSet visibility mask must exclude Participants and Projectiles.");
            });
            CaptureSerialized(accumulator, "scene/spawn-set", "eyeHeight", spawnSet, "eyeHeight", ParticipantSpawnSet.ExpectedEyeHeight);
             CaptureSerialized(accumulator, "scene/spawn-set", "occupiedRadius", spawnSet, "occupiedRadius", ParticipantSpawnSet.ExpectedOccupiedRadius);
            CaptureSerialized(accumulator, "scene/spawn-set", "ballDistanceWeight", spawnSet, "ballDistanceWeight", ParticipantSpawnSet.ExpectedBallDistanceWeight);
            CaptureSerialized(accumulator, "scene/spawn-set", "enemyGoalDistanceWeight", spawnSet, "enemyGoalDistanceWeight", ParticipantSpawnSet.ExpectedEnemyGoalDistanceWeight);
            CaptureSerialized(accumulator, "scene/spawn-set", "nearestEnemyDistanceWeight", spawnSet, "nearestEnemyDistanceWeight", ParticipantSpawnSet.ExpectedNearestEnemyDistanceWeight);
            CaptureSerialized(accumulator, "scene/spawn-set", "noVisibleEnemyBonus", spawnSet, "noVisibleEnemyBonus", ParticipantSpawnSet.ExpectedNoVisibleEnemyBonus);
            CaptureSerialized(accumulator, "scene/spawn-set", "visibleEnemyCountPenalty", spawnSet, "visibleEnemyCountPenalty", ParticipantSpawnSet.ExpectedVisibleEnemyCountPenalty);
            CaptureSerialized(accumulator, "scene/spawn-set", "occupiedFallbackPenalty", spawnSet, "occupiedFallbackPenalty", ParticipantSpawnSet.ExpectedOccupiedFallbackPenalty);
            CaptureSerialized(accumulator, "scene/spawn-set", "ballDistanceCap", spawnSet, "ballDistanceCap", ParticipantSpawnSet.ExpectedBallDistanceCap);
            CaptureSerialized(accumulator, "scene/spawn-set", "enemyGoalDistanceCap", spawnSet, "enemyGoalDistanceCap", ParticipantSpawnSet.ExpectedEnemyGoalDistanceCap);
            CaptureSerialized(accumulator, "scene/spawn-set", "enemyDistanceCap", spawnSet, "enemyDistanceCap", ParticipantSpawnSet.ExpectedEnemyDistanceCap);
        }

        private static void ValidateRecoverySpawnGeometry(Transform candidate, string label)
        {
            if (candidate == null || !ParticipantRecoveryRules.IsValidDestination(candidate.position, ParticipantRecoveryThreshold))
                throw new InvalidOperationException("Participant recovery spawn is below the configured threshold: " + label);

            var capsuleBottom = candidate.position.y + PlayerControllerCenter.y - PlayerControllerHeight * 0.5f;
            var capsuleTop = candidate.position.y + PlayerControllerCenter.y + PlayerControllerHeight * 0.5f;
            if (capsuleBottom < PlayableFloorTop - PlayerControllerSkinWidth || capsuleTop <= capsuleBottom ||
                Mathf.Abs(candidate.position.x) + PlayerControllerRadius > 65f - PlayerControllerSkinWidth ||
                Mathf.Abs(candidate.position.z) + PlayerControllerRadius > 45f - PlayerControllerSkinWidth)
            {
                throw new InvalidOperationException("Participant recovery spawn capsule clearance invalid: " + label);
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
                    context.FpsShotgunVisual = CaptureRequired(accumulator, "visual/root", "FpsShotgunVisual", context.Viewmodels.Find("FpsShotgunVisual"), "FpsShotgunVisual");
                    context.FpsVisual = CaptureRequired(accumulator, "visual/root", "FpsKickVisual", context.Viewmodels.Find("FpsKickVisual"), "FpsKickVisual");
                    if (context.FpsVisual != null)
                        context.FpsAnimator = CaptureRequired(accumulator, "visual/animator", "FpsAnimator", context.FpsVisual.GetComponent<Animator>(), "FPS Animator");
                }
            }
            if (context.Presentation != null)
            {
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.kick", context.Presentation, "kick", context.Kick);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.motor", context.Presentation, "motor", context.PlayerMotor);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.cameraFeedback", context.Presentation, "cameraFeedback", context.CameraFeedback);
                 CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.launcher", context.Presentation, "launcher", context.Launcher);
                 CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.shotgun", context.Presentation, "shotgun", context.Shotgun);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.worldAnimator", context.Presentation, "worldAnimator", context.WorldAnimator);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.fpsKickAnimator", context.Presentation, "fpsKickAnimator", context.FpsAnimator);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.weaponVisual", context.Presentation, "weaponVisual", context.WeaponVisual);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.fpsShotgunVisual", context.Presentation, "fpsShotgunVisual", context.FpsShotgunVisual);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.worldShotgunVisual", context.Presentation, "worldShotgunVisual", context.WorldShotgunVisual);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.gameplayCamera", context.Presentation, "gameplayCamera", context.Camera);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.audioListener", context.Presentation, "audioListener", context.Camera != null ? context.Camera.GetComponent<AudioListener>() : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.participant", context.Presentation, "participant", context.Participants != null && context.Participants.Length > 0 ? context.Participants[0] : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.blueTeamCue", context.Presentation, "blueTeamCue", context.Player != null ? context.Player.transform.Find("BlueCircleCue")?.gameObject : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.redTeamCue", context.Presentation, "redTeamCue", context.Player != null ? context.Player.transform.Find("RedTriangleCue")?.gameObject : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.immunityShield", context.Presentation, "immunityShield", context.Player != null ? context.Player.transform.Find("ImmunityShield")?.gameObject : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.blueImmunityShield", context.Presentation, "blueImmunityShield", context.Player != null ? context.Player.transform.Find("ImmunityShield/BlueImmunityShield")?.gameObject : null);
                CaptureReference(accumulator, "visual/wiring", "PlayerPresentation.redImmunityShield", context.Presentation, "redImmunityShield", context.Player != null ? context.Player.transform.Find("ImmunityShield/RedImmunityShield")?.gameObject : null);
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
                accumulator.Capture("visual/geometry", "WeaponBoundsAndIslands", () => MovementLabPrefabPipeline.ValidateWeaponVisualContract(context.WeaponVisual.gameObject, "WeaponVisual", LauncherWeaponBoundsMin, LauncherWeaponBoundsMax));
                accumulator.Capture("visual/physics", "WeaponVisual", () => MovementLabPrefabPipeline.ValidateNoPhysics(context.WeaponVisual.gameObject, "WeaponVisual"));
                accumulator.Capture("visual/animator", "WeaponVisual", () => MovementLabPrefabPipeline.ValidateNoAnimators(context.WeaponVisual.gameObject, "WeaponVisual"));
            }
            if (context.FpsShotgunVisual != null && context.WorldShotgunVisual != null && context.WorldShotgunMount != null && context.WorldVisual != null && context.Camera != null)
            {
                accumulator.Capture("visual/imported", "FpsShotgunVisual", () => MovementLabPrefabPipeline.ValidateImportedVisual(context.FpsShotgunVisual.gameObject, FpsShotgunModelPath, "FpsShotgunVisual"));
                accumulator.Capture("visual/imported", "WorldShotgunVisual", () => MovementLabPrefabPipeline.ValidateImportedVisual(context.WorldShotgunVisual.gameObject, ShotgunModelPath, "WorldShotgunVisual"));
                accumulator.Capture("visual/material", "FpsShotgunMaterials", () => MovementLabMaterialPipeline.ValidateShotgunMaterials(context.FpsShotgunVisual.gameObject));
                accumulator.Capture("visual/material", "WorldShotgunMaterials", () => MovementLabMaterialPipeline.ValidateShotgunMaterials(context.WorldShotgunVisual.gameObject));
                accumulator.Capture("visual/geometry", "FpsShotgunBoundsAndIslands", () => MovementLabPrefabPipeline.ValidateWeaponVisualContract(context.FpsShotgunVisual.gameObject, "FpsShotgunVisual", FpsShotgunBoundsMin, FpsShotgunBoundsMax));
                accumulator.Capture("visual/geometry", "WorldShotgunBoundsAndIslands", () => MovementLabPrefabPipeline.ValidateWeaponVisualContract(context.WorldShotgunVisual.gameObject, "WorldShotgunVisual", WorldShotgunBoundsMin, WorldShotgunBoundsMax));
                accumulator.Capture("visual/physics", "FpsShotgunVisual", () => MovementLabPrefabPipeline.ValidateNoPhysics(context.FpsShotgunVisual.gameObject, "FpsShotgunVisual"));
                accumulator.Capture("visual/physics", "WorldShotgunVisual", () => MovementLabPrefabPipeline.ValidateNoPhysics(context.WorldShotgunVisual.gameObject, "WorldShotgunVisual"));
                accumulator.Capture("visual/animator", "ShotgunAnimators", () =>
                {
                    MovementLabPrefabPipeline.ValidateNoAnimators(context.FpsShotgunVisual.gameObject, "FpsShotgunVisual");
                    MovementLabPrefabPipeline.ValidateNoAnimators(context.WorldShotgunVisual.gameObject, "WorldShotgunVisual");
                });
                accumulator.Capture("visual/presentation", "ShotgunAxes", () => MovementLabPrefabPipeline.ValidateShotgunPresentation(
                    context.Player, context.Camera, context.FpsShotgunVisual, context.WorldVisual, context.WorldShotgunMount, context.WorldShotgunVisual, "Scene Player"));
                accumulator.Capture("visual/presentation", "ShotgunTeamTint", () => MovementLabPrefabPipeline.ValidateTeamTintRenderers(
                    context.Presentation, context.WorldVisual, context.WorldShotgunMount,
                    MovementLabPrefabPipeline.FindRendererByName(context.WorldShotgunVisual.gameObject, "WeaponAccent"),
                    MovementLabPrefabPipeline.FindRendererByName(context.WorldShotgunVisual.gameObject, "WeaponAccentCore"),
                    "Scene Player PlayerPresentation.teamTintRenderers"));
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
                if (availableAssets != null && availableAssets.Contains(FpsControllerPath) && availableAssets.Contains(FpsKickModelPath) &&
                    availableAssets.Contains(WorldControllerPath) && availableAssets.Contains(CharacterModelPath))
                    accumulator.Capture("visual/animator", "dash-kick-compatibility", MovementLabPrefabPipeline.ValidateDashAnimationCompatibility);
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
            if (availableAssets != null && availableAssets.Contains(HealthPickupPrefabPath))
                accumulator.Capture("prefab", "HealthPickup", () => MovementLabPrefabPipeline.ValidatePrefab(HealthPickupPrefabPath, "HealthPickup", false, null));
            if (availableAssets != null && availableAssets.Contains(ShotgunPickupPrefabPath))
                accumulator.Capture("prefab", "ShotgunPickup", () => MovementLabPrefabPipeline.ValidatePrefab(ShotgunPickupPrefabPath, "ShotgunPickup", false, null));
            if (availableAssets != null && availableAssets.Contains(AmmoPickupPrefabPath))
                accumulator.Capture("prefab", "AmmoPickup", () => MovementLabPrefabPipeline.ValidatePrefab(AmmoPickupPrefabPath, "AmmoPickup", false, null));
            if (availableAssets != null && availableAssets.Contains(PrefabPath) &&
                availableAssets.Contains(BallPrefabPath) && availableAssets.Contains(ExplosionPrefabPath) &&
                availableAssets.Contains(HealthPickupPrefabPath) && availableAssets.Contains(ShotgunPickupPrefabPath) &&
                availableAssets.Contains(AmmoPickupPrefabPath))
                accumulator.Capture("prefab", "required-components", () => MovementLabPrefabPipeline.Validate());
            accumulator.Capture("importer", "texture-contracts", () => MovementLabImportPipeline.ValidateTextureImporterContracts());
            accumulator.Capture("importer", "animator-contracts", () => MovementLabAnimatorPipeline.Validate());
            accumulator.Capture("material", "opaque-references", () => MovementLabMaterialPipeline.ValidateOpaqueMaterialReferences());
            accumulator.Capture("material", "health-pickup", () => MovementLabMaterialPipeline.ValidateHealthPickupMaterial(AssetDatabase.LoadAssetAtPath<Material>(HealthPickupMaterialPath)));
            accumulator.Capture("material", "ammo-shell", () => MovementLabMaterialPipeline.ValidateAmmoShellMaterial(AssetDatabase.LoadAssetAtPath<Material>(AmmoShellMaterialPath)));
            accumulator.Capture("material", "team-references", ValidateTeamMaterialContracts);
            if (context?.SceneReady == true)
                accumulator.Capture("render", "pipeline-settings", () => MovementLabSceneComposer.ValidateRenderPipelineSettings());
        }

        private static void ValidateArenaContracts(ValidationContext context, MovementLabValidationAccumulator accumulator)
        {
            if (context?.Arena == null) return;
            accumulator.Capture("arena", "materials", () => MovementLabArenaPipeline.ValidateArenaMaterials(context.Arena, context.BallSurface));
            accumulator.Capture("arena", "collision-geometry", () => MovementLabArenaPipeline.ValidatePrimaryCollisionGeometry(context.Arena, context.BallSurface));
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

        private static void ValidateTeamMaterialContracts()
        {
            var blue = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueMaterialPath);
            var red = AssetDatabase.LoadAssetAtPath<Material>(TeamRedMaterialPath);
            var blueShield = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueShieldMaterialPath);
            var redShield = AssetDatabase.LoadAssetAtPath<Material>(TeamRedShieldMaterialPath);
            var blueTrail = AssetDatabase.LoadAssetAtPath<Material>(TeamBlueTrailMaterialPath);
            var redTrail = AssetDatabase.LoadAssetAtPath<Material>(TeamRedTrailMaterialPath);
            if (blue == null || red == null || blue.shader == null || red.shader == null || blue.shader.name != LitShaderName || red.shader.name != LitShaderName)
                throw new InvalidOperationException("Team avatar materials must use URP Lit.");
            if (blueShield == null || redShield == null || blueShield.shader == null || redShield.shader == null || blueShield.shader.name != "RocketFooxball/RetroShield" || redShield.shader.name != "RocketFooxball/RetroShield")
                throw new InvalidOperationException("Team immunity shield materials must use RetroShield.");
            if (blueTrail == null || redTrail == null || blueTrail.shader == null || redTrail.shader == null || blueTrail.shader.name != "RocketFooxball/RetroParticle" || redTrail.shader.name != "RocketFooxball/RetroParticle")
                throw new InvalidOperationException("Team trail materials must use RetroParticle.");
        }

        private static void CaptureObjectArray(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, UnityEngine.Object[] expected)
        {
            accumulator.Capture(scope, check, () =>
            {
                var serialized = new SerializedObject(target);
                var array = serialized.FindProperty(property);
                if (array == null || !array.isArray || array.arraySize != (expected == null ? 0 : expected.Length))
                    throw new InvalidOperationException(check + " array size mismatch.");
                for (var i = 0; expected != null && i < expected.Length; i++)
                {
                    var actual = array.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (actual != expected[i]) throw new InvalidOperationException(check + " array reference mismatch at " + i + ".");
                    ValidatePersistentIdentity(actual, check + "[" + i + "]");
                }
            });
        }

        private static void CaptureSerialized(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, float expected)
        {
            accumulator.Capture(scope, check, () => ValidateSerializedFloat(target, property, expected, check));
        }

        private static void ValidateSerializedBool(UnityEngine.Object target, string propertyName, bool expected, string label)
        {
            if (target == null) throw new InvalidOperationException(label + " target is null.");
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean || property.boolValue != expected)
                throw new InvalidOperationException(label + " serialized value mismatch.");
        }

        private static void ValidateShotgunRuntimeSurface()
        {
            if ((int)ParticipantDamageCause.Shotgun != 6 || (int)ParticipantDeathCause.Shotgun != 5)
                throw new InvalidOperationException("Shotgun damage/death causes must append after the existing ordinals.");

            var stateType = typeof(ParticipantState);
            var hasShotgun = stateType.GetProperty(nameof(ParticipantState.HasShotgun), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var shells = stateType.GetProperty(nameof(ParticipantState.ShotgunShells), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var capacity = stateType.GetProperty(nameof(ParticipantState.ShotgunShellCapacity), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var shotgun = stateType.GetProperty(nameof(ParticipantState.Shotgun), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (hasShotgun == null || hasShotgun.PropertyType != typeof(bool) || shells == null || shells.PropertyType != typeof(int) ||
                capacity == null || capacity.PropertyType != typeof(int) || shotgun == null || shotgun.PropertyType != typeof(ShotgunWeapon))
                throw new InvalidOperationException("ParticipantState shotgun inventory surface is missing or changed.");

            var weaponType = typeof(ShotgunWeapon);
            var fire = weaponType.GetMethod(nameof(ShotgunWeapon.RequestFire), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, new[] { typeof(Vector3), typeof(Vector3) }, null);
            var reset = weaponType.GetMethod(nameof(ShotgunWeapon.ResetState), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (fire == null || reset == null || weaponType.GetEvent(nameof(ShotgunWeapon.ShotFired)) == null || weaponType.GetEvent(nameof(ShotgunWeapon.HitConfirmed)) == null)
                throw new InvalidOperationException("ShotgunWeapon public request/reset/event surface is missing.");
        }

        private static void ValidateShotgunInputAsset()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null) throw new InvalidOperationException("Shotgun input asset is missing: " + InputActionsPath);
            var map = actions.FindActionMap("Player", false);
            var action = map != null ? map.FindAction("ShotgunFire", false) : null;
            if (action == null || action.type != InputActionType.Button)
                throw new InvalidOperationException("Player/ShotgunFire must be a Button action.");

            var expectedActionId = Guid.Parse("f75e2f4a-0f80-4d83-9a8d-c73a5d6d14cf");
            if (action.id != expectedActionId)
                throw new InvalidOperationException("Player/ShotgunFire action GUID changed.");

            var mouseBindings = action.bindings.Where(binding => string.Equals(binding.path, "<Mouse>/rightButton", StringComparison.Ordinal)).ToArray();
            var gamepadBindings = action.bindings.Where(binding => string.Equals(binding.path, "<Gamepad>/leftTrigger", StringComparison.Ordinal)).ToArray();
            if (mouseBindings.Length != 1 || gamepadBindings.Length != 1 ||
                mouseBindings[0].groups != ";Keyboard&Mouse" || gamepadBindings[0].groups != ";Gamepad" ||
                mouseBindings[0].action != "ShotgunFire" || gamepadBindings[0].action != "ShotgunFire")
                throw new InvalidOperationException("Player/ShotgunFire must contain RMB and Gamepad left-trigger bindings.");

            if (mouseBindings[0].id != Guid.Parse("2f8d6ac8-3d13-4db4-9b2b-e3aa4fef11aa") ||
                gamepadBindings[0].id != Guid.Parse("4c686716-8ce5-4c46-99ef-1538d0c2411b"))
                throw new InvalidOperationException("Player/ShotgunFire binding GUID changed.");

            var readerMethod = typeof(PlayerInputReader).GetMethod(nameof(PlayerInputReader.ConsumeShotgunPressed),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                null, Type.EmptyTypes, null);
            if (readerMethod == null || readerMethod.ReturnType != typeof(bool))
                throw new InvalidOperationException("PlayerInputReader.ConsumeShotgunPressed public bool surface is missing.");
        }

        private static void CaptureSerializedInteger(MovementLabValidationAccumulator accumulator, string scope, string check,
            UnityEngine.Object target, string property, int expected)
        {
            accumulator.Capture(scope, check, () => ValidateSerializedInteger(target, property, expected, check));
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
