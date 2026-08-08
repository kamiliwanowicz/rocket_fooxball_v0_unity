using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RocketFooxball;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Sole authority for generated MovementLab assets and scene wiring.
    /// Build and validation stay deterministic so serialized state remains reviewable.
    /// </summary>
    public static class MovementLabBuilder
    {
        private const string PrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        private const string BallPrefabPath = "Assets/_Game/Prefabs/Ball.prefab";
        private const string RocketPrefabPath = "Assets/_Game/Prefabs/Rocket.prefab";
        private const string RocketModelPath = "Assets/_Game/Models/LowPolyRocket.fbx";
        private const string CharacterModelPath = "Assets/_Game/Models/LowPolyCharacter.fbx";
        private const string FpsKickModelPath = "Assets/_Game/Models/FpsKickRig.fbx";
        private const string WeaponModelPath = "Assets/_Game/Models/FpsRocketLauncher.fbx";
        private const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MaterialsPath = "Assets/_Game/Materials";
        private const string TexturesPath = "Assets/_Game/Textures";
        private const string ShadersPath = "Assets/_Game/Shaders";
        private const string AnimationsPath = "Assets/_Game/Animations";
        private const string ExplosionPrefabPath = "Assets/_Game/Prefabs/ExplosionVfx.prefab";
        private const string WorldControllerPath = AnimationsPath + "/WorldCharacter.controller";
        private const string FpsControllerPath = AnimationsPath + "/FpsKick.controller";
        private const string GrassTexturePath = TexturesPath + "/RetroGrass.png";
        private const string BallTexturePath = TexturesPath + "/RetroBall.png";
        private const string ExplosionTexturePath = TexturesPath + "/RetroExplosion.png";
        private const string SmokeTexturePath = TexturesPath + "/RetroSmoke.png";
        private const string ToonShaderPath = ShadersPath + "/RetroToonLit.shader";
        private const string ParticleShaderPath = ShadersPath + "/RetroParticle.shader";
        private const string BallSurfacePath = MaterialsPath + "/BallSurface.physicMaterial";
        private const string BuilderSourcePath = "Assets/_Game/Editor/MovementLabBuilder.cs";
        private const string BuildMarkerPrefix = "MovementLabGeneratedT5_";

        // Keep this list limited to assets authored by this builder. Unity can
        // serialize empty fields with trailing spaces in both the asset and
        // paired .meta YAML, so normalize every generated file after saving.
        private static readonly string[] GeneratedYamlAssetPaths =
        {
            PrefabPath,
            BallPrefabPath,
            RocketPrefabPath,
            ScenePath,
            MaterialsPath + "/Floor.mat",
            MaterialsPath + "/Wall.mat",
            MaterialsPath + "/Marking.mat",
            MaterialsPath + "/Ball.mat",
            MaterialsPath + "/Rocket.mat",
            MaterialsPath + "/GoalFrame.mat",
            MaterialsPath + "/Shield.mat",
            BallSurfacePath,
            MaterialsPath + "/Explosion.mat",
            MaterialsPath + "/Smoke.mat",
            MaterialsPath + "/CharacterRed.mat",
            MaterialsPath + "/CharacterBlack.mat",
            MaterialsPath + "/CharacterCream.mat",
            MaterialsPath + "/CharacterEye.mat",
            MaterialsPath + "/WeaponMetal.mat",
            MaterialsPath + "/WeaponDark.mat",
            MaterialsPath + "/WeaponAccent.mat",
            WorldControllerPath,
            FpsControllerPath,
            ExplosionPrefabPath
        };

        private sealed class GoalBuild
        {
            public GameObject Root;
            public GoalTrigger Trigger;
            public Collider Shield;
        }

        private sealed class ArenaBuild
        {
            public GameObject Root;
            public GoalBuild NorthGoal;
            public GoalBuild SouthGoal;
            public Collider[] Shields;
        }

        [MenuItem("Rocket Fooxball/Build Movement Lab")]
        public static void BuildMovementLab()
        {
            EnsureFolders();

            ConfigureTextureImporters();
            ConfigureModelImporters();

            var ballSurface = GetOrCreatePhysicMaterial();
            var floorMaterial = GetOrCreateRetroMaterial("Floor", new Color(0.30f, 0.42f, 0.17f), AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath), new Vector2(32.5f, 22.5f));
            var wallMaterial = GetOrCreateRetroMaterial("Wall", new Color(0.16f, 0.22f, 0.34f), null, Vector2.one);
            var markingMaterial = GetOrCreateRetroMaterial("Marking", new Color(0.92f, 0.84f, 0.66f), null, Vector2.one);
            var ballMaterial = GetOrCreateRetroMaterial("Ball", new Color(0.95f, 0.53f, 0.08f), AssetDatabase.LoadAssetAtPath<Texture2D>(BallTexturePath), Vector2.one);
            var rocketMaterial = GetOrCreateRetroMaterial("Rocket", new Color(0.95f, 0.23f, 0.08f), null, Vector2.one);
            var frameMaterial = GetOrCreateRetroMaterial("GoalFrame", new Color(0.82f, 0.70f, 0.48f), null, Vector2.one);
            var shieldMaterial = GetOrCreateRetroMaterial("Shield", new Color(0.15f, 0.8f, 0.95f), null, Vector2.one);

            var rocketPrefab = BuildRocketPrefab(rocketMaterial);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RocketPrefabPath, ImportAssetOptions.ForceSynchronousImport);
            rocketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath);
            var ballPrefab = BuildBallPrefab(ballMaterial, ballSurface);
            var playerPrefab = BuildPlayerPrefab(rocketPrefab);
            BuildExplosionVfxPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ExplosionPrefabPath, ImportAssetOptions.ForceSynchronousImport);
            var explosionRootAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);
            var explosionAssetComponent = explosionRootAsset != null ? explosionRootAsset.GetComponent<ExplosionVfx>() : null;
            if (explosionAssetComponent == null) throw new InvalidOperationException("Explosion VFX prefab failed to import.");
            if (!EditorUtility.IsPersistent(explosionAssetComponent)) throw new InvalidOperationException("Explosion VFX component is not a persistent prefab asset.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var builderSignature = ComputeBuilderSignature();
            RegisterBuildScene();
            Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
            SetProjectFixedTimestep();

            // Reuse only a scene carrying the current builder signature and passing
            // the full generated-scene validator. Any source change or stale/missing
            // generated requirement falls through to authoritative scene rebuild.
            if (TryOpenExistingGeneratedScene(builderSignature))
            {
                AssetDatabase.SaveAssets();
                NormalizeGeneratedYamlWhitespace();
                Debug.Log("Rocket Fooxball Movement Lab built: " + ScenePath + " (existing T5 scene reused)");
                return;
            }

            GameObject explosionPrefabProbe = null;
            try
            {
            var explosionPrefab = GetSerializablePrefabComponent<ExplosionVfx>(explosionRootAsset, out explosionPrefabProbe);
            if (explosionPrefab == null) throw new InvalidOperationException("Explosion VFX prefab source component could not be resolved.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var defaultCamera = Camera.main;
            if (defaultCamera != null)
            {
                UnityEngine.Object.DestroyImmediate(defaultCamera.gameObject);
            }

            var arena = BuildArena(floorMaterial, wallMaterial, markingMaterial, frameMaterial, shieldMaterial, ballSurface);
            var explosionObject = new GameObject("ExplosionResolver");
            var explosionResolver = explosionObject.AddComponent<ExplosionResolver>();
            SetObjectArray(explosionResolver, "goalShieldColliders", arena.Shields);
            SetObjectReference(explosionResolver, "explosionVfxPrefab", explosionPrefab);
            SetFloat(explosionResolver, "blastRadius", 4.5f);
            SetFloat(explosionResolver, "playerImpulseStrength", 24f);
            SetFloat(explosionResolver, "ballImpulseStrength", 16f);
            SetFloat(explosionResolver, "occludedForce", 0.25f);
            SetFloat(explosionResolver, "playerUpBias", 0.18f);
            SetFloat(explosionResolver, "cameraFeedbackScale", 0.8f);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.SetPositionAndRotation(new Vector3(0f, 0f, 3f), Quaternion.LookRotation(Vector3.back, Vector3.up));

            var ball = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab);
            ball.name = "Ball";
            ball.transform.SetPositionAndRotation(new Vector3(0f, 0.72f, 0f), Quaternion.identity);

            var playerMotor = player.GetComponent<PlayerMotor>();
            var playerInput = player.GetComponent<PlayerInputReader>();
            var playerLook = player.GetComponent<PlayerLook>();
            var cameraFeedback = player.GetComponent<PlayerCameraFeedback>();
            var launcher = player.GetComponent<RocketLauncher>();
            var kick = player.GetComponent<BallKick>();
            var ballMotor = ball.GetComponent<BallMotor>();
            var ballBody = ball.GetComponent<Rigidbody>();
            var ballCollider = ball.GetComponent<Collider>();

            SetObjectReference(ballMotor, "body", ballBody);
            SetObjectReference(ballMotor, "ballCollider", ballCollider);
            SetObjectReference(ballMotor, "player", playerMotor);
            SetObjectArray(ballMotor, "goalShieldColliders", arena.Shields);
            SetObjectReference(kick, "ball", ballMotor);
            SetObjectReference(launcher, "explosionResolver", explosionResolver);
            SetObjectReference(cameraFeedback, "player", playerMotor);
            SetObjectReference(cameraFeedback, "targetCamera", player.GetComponentInChildren<Camera>(true));

            SetObjectReference(arena.NorthGoal.Trigger, "ball", ballMotor);
            SetObjectReference(arena.NorthGoal.Trigger, "match", null);
            SetObjectReference(arena.SouthGoal.Trigger, "ball", ballMotor);
            SetObjectReference(arena.SouthGoal.Trigger, "match", null);
            SetObjectReference(arena.NorthGoal.Trigger, "planeReference", arena.NorthGoal.Root.transform);
            SetObjectReference(arena.SouthGoal.Trigger, "planeReference", arena.SouthGoal.Root.transform);

            var matchObject = new GameObject("MatchController");
            var match = matchObject.AddComponent<MatchController>();
            SetObjectReference(match, "input", playerInput);
            SetObjectReference(match, "player", playerMotor);
            SetObjectReference(match, "playerLook", playerLook);
            SetObjectReference(match, "cameraFeedback", cameraFeedback);
            SetObjectReference(match, "ball", ballMotor);
            SetObjectReference(match, "launcher", launcher);
            SetObjectReference(match, "kick", kick);
            SetObjectReference(match, "northGoal", arena.NorthGoal.Trigger);
            SetObjectReference(match, "southGoal", arena.SouthGoal.Trigger);
            SetFloat(match, "goalFreezeDuration", 5f);
            SetVector3(match, "ballResetPosition", Vector3.zero);
            SetVector3(match, "playerResetPosition", new Vector3(0f, 0f, 3f));
            SetVector3(match, "resetLookTarget", Vector3.zero);
            arena.NorthGoal.Trigger.SetMatch(match);
            arena.SouthGoal.Trigger.SetMatch(match);
            SetObjectReference(arena.NorthGoal.Trigger, "match", match);
            SetObjectReference(arena.SouthGoal.Trigger, "match", match);

            var hud = new GameObject("DebugHUD");
            var hudComponent = hud.AddComponent<MovementDebugHud>();
            SetObjectReference(hudComponent, "player", playerMotor);
            SetObjectReference(hudComponent, "ball", ballMotor);
            SetObjectReference(hudComponent, "launcher", launcher);
            SetObjectReference(hudComponent, "kick", kick);
            SetObjectReference(hudComponent, "match", match);

            new GameObject(GetBuildMarkerName(builderSignature));

            ConfigureSceneLight();
            EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                if (explosionPrefabProbe != null)
                {
                    UnityEngine.Object.DestroyImmediate(explosionPrefabProbe);
                }
            }
            AssetDatabase.SaveAssets();
            NormalizeGeneratedYamlWhitespace();
            Debug.Log("Rocket Fooxball Movement Lab built: " + ScenePath);
        }

        /// <summary>Reopens and checks generated assets without writing project state.</summary>
        [MenuItem("Rocket Fooxball/Validate Movement Lab")]
        public static void ValidateMovementLab()
        {
            EnsureAssetExists(PrefabPath);
            EnsureAssetExists(BallPrefabPath);
            EnsureAssetExists(RocketPrefabPath);
            EnsureAssetExists(RocketModelPath);
            EnsureAssetExists(CharacterModelPath);
            EnsureAssetExists(FpsKickModelPath);
            EnsureAssetExists(WeaponModelPath);
            EnsureAssetExists(GrassTexturePath);
            EnsureAssetExists(BallTexturePath);
            EnsureAssetExists(ExplosionTexturePath);
            EnsureAssetExists(SmokeTexturePath);
            EnsureAssetExists(ToonShaderPath);
            EnsureAssetExists(ParticleShaderPath);
            EnsureAssetExists(WorldControllerPath);
            EnsureAssetExists(FpsControllerPath);
            EnsureAssetExists(ExplosionPrefabPath);
            EnsureAssetExists(ScenePath);
            EnsureAssetExists(BallSurfacePath);

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
            Require(GameObject.Find(GetBuildMarkerName(ComputeBuilderSignature())), "T5 build marker");

            var playerMotor = Require(player.GetComponent<PlayerMotor>(), "PlayerMotor");
            var input = Require(player.GetComponent<PlayerInputReader>(), "PlayerInputReader");
            var look = Require(player.GetComponent<PlayerLook>(), "PlayerLook");
            var cameraFeedback = Require(player.GetComponent<PlayerCameraFeedback>(), "PlayerCameraFeedback");
            var launcher = Require(player.GetComponent<RocketLauncher>(), "RocketLauncher");
            var kick = Require(player.GetComponent<BallKick>(), "BallKick");
            var camera = Require(player.GetComponentInChildren<Camera>(true), "Player camera");
            Require(player.GetComponent<CharacterController>(), "Player CharacterController");
            if (Vector3.Distance(player.transform.position, new Vector3(0f, 0f, 3f)) > 0.001f || Vector3.Dot(player.transform.forward, Vector3.back) < 0.999f)
            {
                throw new InvalidOperationException("Player spawn must be neutral midfield offset facing centered ball.");
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

            var resolver = Require(explosionObject.GetComponent<ExplosionResolver>(), "ExplosionResolver");
            var match = Require(matchObject.GetComponent<MatchController>(), "MatchController");
            var hud = Require(hudObject.GetComponent<MovementDebugHud>(), "MovementDebugHud");
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

            var northShield = Require(north.transform.Find("Shield"), "North goal Shield").GetComponent<Collider>();
            var southShield = Require(south.transform.Find("Shield"), "South goal Shield").GetComponent<Collider>();
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
            ValidateReference(launcher, "input", input, "RocketLauncher.input");
            ValidateReference(launcher, "look", look, "RocketLauncher.look");
            ValidateReference(launcher, "aimCamera", camera, "RocketLauncher.aimCamera");
            ValidateReference(launcher, "spawnPoint", player.transform.Find("Head/Camera/RocketMuzzle"), "RocketLauncher.spawnPoint");
            ValidateReference(launcher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "RocketLauncher.projectilePrefab");
            ValidateReference(launcher, "explosionResolver", resolver, "RocketLauncher.explosionResolver");
            ValidateReference(cameraFeedback, "player", playerMotor, "PlayerCameraFeedback.player");
            ValidateReference(cameraFeedback, "targetCamera", camera, "PlayerCameraFeedback.targetCamera");
            ValidateReference(kick, "input", input, "BallKick.input");
            ValidateReference(kick, "player", playerMotor, "BallKick.player");
            ValidateReference(kick, "look", look, "BallKick.look");
            ValidateReference(kick, "aimCamera", camera, "BallKick.aimCamera");
            ValidateReference(kick, "ball", ballMotor, "BallKick.ball");
            ValidateArrayContains(resolver, "goalShieldColliders", northShield, southShield, "ExplosionResolver.goalShieldColliders");
            ValidatePrefabReference(resolver, "explosionVfxPrefab", ExplosionPrefabPath, "ExplosionResolver.explosionVfxPrefab");

            var presentation = Require(player.GetComponent<PlayerPresentation>(), "PlayerPresentation");
            ValidateReference(presentation, "kick", kick, "PlayerPresentation.kick");
            var worldVisual = Require(player.transform.Find("WorldVisual"), "Player WorldVisual");
            var worldAnimator = Require(worldVisual.GetComponent<Animator>(), "World Animator");
            ValidateReference(presentation, "worldAnimator", worldAnimator, "PlayerPresentation.worldAnimator");
            var viewmodels = Require(camera.transform.Find("Viewmodels"), "Viewmodels");
            var weaponVisual = Require(viewmodels.Find("WeaponVisual"), "WeaponVisual");
            var fpsVisual = Require(viewmodels.Find("FpsKickVisual"), "FpsKickVisual");
            var fpsAnimator = Require(fpsVisual.GetComponent<Animator>(), "FPS Animator");
            ValidateReference(presentation, "fpsKickAnimator", fpsAnimator, "PlayerPresentation.fpsKickAnimator");
            if (worldAnimator.applyRootMotion || fpsAnimator.applyRootMotion)
            {
                throw new InvalidOperationException("Player visual animators must not apply root motion.");
            }
            if (worldAnimator.avatar == null || fpsAnimator.avatar == null)
            {
                throw new InvalidOperationException("World/FPS animators must have imported avatars.");
            }
            ValidateImportedVisual(worldVisual.gameObject, CharacterModelPath, "WorldVisual");
            ValidateImportedVisual(weaponVisual.gameObject, WeaponModelPath, "WeaponVisual");
            ValidateImportedVisual(fpsVisual.gameObject, FpsKickModelPath, "FpsKickVisual");
            ValidateNoPhysics(weaponVisual.gameObject, "WeaponVisual");
            ValidateNoPhysics(fpsVisual.gameObject, "FpsKickVisual");
            ValidateAnimatorController(worldAnimator, WorldControllerPath, CharacterModelPath);
            ValidateAnimatorController(fpsAnimator, FpsControllerPath, FpsKickModelPath);
            var hiddenLayer = LayerMask.NameToLayer("LocalPlayerHidden");
            if (hiddenLayer < 0 || (camera.cullingMask & (1 << hiddenLayer)) != 0)
            {
                throw new InvalidOperationException("LocalPlayerHidden layer must be excluded from player camera culling.");
            }
            ValidateLayerRecursively(worldVisual.gameObject, hiddenLayer, "WorldVisual");
            ValidateLayerExcluded(viewmodels.gameObject, hiddenLayer, "Viewmodels");
            ValidateCrosshair(camera);
            ValidateTrail(AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath));
            ValidateExplosionPrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath));

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
            ValidateReference(hud, "player", playerMotor, "HUD.player");
            ValidateReference(hud, "ball", ballMotor, "HUD.ball");
            ValidateReference(hud, "launcher", launcher, "HUD.launcher");
            ValidateReference(hud, "kick", kick, "HUD.kick");
            ValidateReference(hud, "match", match, "HUD.match");

            ValidatePrefab(PrefabPath, "Player", false, ballSurface);
            ValidatePrefab(BallPrefabPath, "Ball", true, ballSurface);
            ValidatePrefab(RocketPrefabPath, "Rocket", false, null);
            ValidateArenaMaterials(arena, ballSurface);
            ValidateTextureImporterContracts();
            ValidateModelImporterContracts();
            ValidatePhysicsAndBuildSettings();
            ValidateNoMissingComponents(scene);

            Debug.Log("Rocket Fooxball Movement Lab validation succeeded: " + ScenePath);
        }

        private static GameObject BuildPlayerPrefab(GameObject rocketPrefab)
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
            camera.gameObject.AddComponent<AudioListener>();
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
            worldAnimator.runtimeAnimatorController = EnsureAnimatorController(WorldControllerPath, CharacterModelPath);
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
            var weaponVisual = InstantiateImportedVisual(weaponModel, "WeaponVisual", viewmodels, new Vector3(0.28f, -0.22f, 0.55f), Quaternion.identity, Vector3.one);
            var weaponMetal = GetOrCreateRetroMaterial("WeaponMetal", new Color(0.38f, 0.055f, 0.045f), null, Vector2.one);
            var weaponDark = GetOrCreateRetroMaterial("WeaponDark", new Color(0.018f, 0.012f, 0.014f), null, Vector2.one);
            var weaponAccent = GetOrCreateRetroMaterial("WeaponAccent", new Color(0.82f, 0.70f, 0.48f), null, Vector2.one);
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
            SetObjectReference(look, "input", input);
            SetObjectReference(look, "head", head);
            SetObjectReference(feedback, "player", motor);
            SetObjectReference(feedback, "targetCamera", camera);
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
            SetFloat(kick, "kickRange", 1.35f);
            SetFloat(kick, "contactReachPadding", 0.20f);
            SetFloat(kick, "coneTotalDegrees", 35f);
            SetFloat(kick, "cooldown", 0.40f);
            SetFloat(kick, "inputBuffer", 0.50f);
            SetFloat(kick, "speedFraction", 0.70f);
            SetFloat(kick, "playerMomentumShare", 0.20f);
            SetFloat(feedback, "baseFov", 75f);
            SetFloat(feedback, "maxFov", 84f);
            SetObjectReference(presentation, "kick", kick);
            SetObjectReference(presentation, "worldAnimator", worldAnimator);
            SetObjectReference(presentation, "fpsKickAnimator", fpsAnimator);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static GameObject BuildBallPrefab(Material ballMaterial, PhysicsMaterial ballSurface)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Ball";
            root.transform.localScale = Vector3.one * 1.44f;
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

        private static GameObject BuildRocketPrefab(Material rocketMaterial)
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
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("Rocket model contains no renderers: " + RocketModelPath);
            }
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].sharedMaterial = rocketMaterial;
            }
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

            var smokeMaterial = GetOrCreateParticleMaterial("Smoke", new Color(0.42f, 0.40f, 0.36f, 0.64f), AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath));
            var smokeTrail = new GameObject("SmokeTrail");
            smokeTrail.transform.SetParent(root.transform, false);
            smokeTrail.transform.localPosition = new Vector3(0f, 0f, -0.16f);
            var smokeSystem = smokeTrail.AddComponent<ParticleSystem>();
            var smokeMain = smokeSystem.main;
            smokeMain.loop = true;
            smokeMain.simulationSpace = ParticleSystemSimulationSpace.World;
            smokeMain.startLifetime = 0.45f;
            smokeMain.startSpeed = 0f;
            smokeMain.startSize = 0.22f;
            smokeMain.startColor = new Color(0.42f, 0.40f, 0.36f, 0.64f);
            smokeMain.maxParticles = 48;
            var smokeEmission = smokeSystem.emission;
            smokeEmission.rateOverTime = 0f;
            smokeEmission.rateOverDistance = 2f;
            var smokeSize = smokeSystem.sizeOverLifetime;
            smokeSize.enabled = true;
            var smokeCurve = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0.38f)));
            smokeSize.size = smokeCurve;
            var smokeColor = smokeSystem.colorOverLifetime;
            smokeColor.enabled = true;
            var smokeGradient = new Gradient();
            smokeGradient.SetKeys(new[] { new GradientColorKey(new Color(0.42f, 0.40f, 0.36f), 0f), new GradientColorKey(new Color(0.30f, 0.28f, 0.25f), 1f) }, new[] { new GradientAlphaKey(0.56f, 0f), new GradientAlphaKey(0f, 1f) });
            smokeColor.color = smokeGradient;
            var smokeRenderer = smokeTrail.GetComponent<ParticleSystemRenderer>();
            smokeRenderer.material = smokeMaterial;
            smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            var trailVfx = smokeTrail.AddComponent<RocketTrailVfx>();
            SetObjectArray(trailVfx, "particleSystems", new UnityEngine.Object[] { smokeSystem });
            SetObjectReference(projectile, "trailVfx", trailVfx);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        private static ExplosionVfx BuildExplosionVfxPrefab()
        {
            var root = new GameObject("ExplosionVfx");
            var explosionMaterial = GetOrCreateParticleMaterial("Explosion", new Color(1f, 0.24f, 0.06f, 0.90f), AssetDatabase.LoadAssetAtPath<Texture2D>(ExplosionTexturePath));
            var smokeMaterial = GetOrCreateParticleMaterial("Smoke", new Color(0.42f, 0.40f, 0.36f, 0.64f), AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath));
            var systems = new List<ParticleSystem>();

            var flash = CreateExplosionSystem(root.transform, "Flash", explosionMaterial, 1, 0.10f, 0.10f, 2.4f, 0f, 0, 0f, 0f);
            systems.Add(flash);
            var fire = CreateExplosionSystem(root.transform, "FireChunks", explosionMaterial, 10, 0.325f, 0.40f, 0.58f, 5f, 10, 3f, 7f);
            systems.Add(fire);
            var sparks = CreateExplosionSystem(root.transform, "Sparks", explosionMaterial, 18, 0.265f, 0.35f, 0.08f, 10f, 18, 7f, 13f);
            systems.Add(sparks);
            var smoke = CreateExplosionSystem(root.transform, "Smoke", smokeMaterial, 8, 0.825f, 1.10f, 1.40f, 1f, 8, 0.5f, 2f);
            var smokeSize = smoke.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.60f), new Keyframe(1f, 2.20f)));
            systems.Add(smoke);

            var effect = root.AddComponent<ExplosionVfx>();
            SetObjectArray(effect, "particleSystems", systems.ToArray());
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab.GetComponent<ExplosionVfx>();
        }

        private static ParticleSystem CreateExplosionSystem(Transform parent, string name, Material material, int burstCount, float minLifetime, float maxLifetime, float size, float speed, int maxParticles, float minSpeed, float maxSpeed)
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
            return system;
        }

        private static ArenaBuild BuildArena(Material floorMaterial, Material wallMaterial, Material markingMaterial, Material frameMaterial, Material shieldMaterial, PhysicsMaterial ballSurface)
        {
            var arena = new GameObject("Arena");
            CreateSolid("Floor", arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 90f), floorMaterial, ballSurface);
            const float sideSpan = 46.5f;
            CreateSolid("NorthWallWest", arena.transform, new Vector3(-41.75f, 4f, -44.5f), new Vector3(sideSpan, 8f, 1f), wallMaterial, ballSurface);
            CreateSolid("NorthWallEast", arena.transform, new Vector3(41.75f, 4f, -44.5f), new Vector3(sideSpan, 8f, 1f), wallMaterial, ballSurface);
            CreateSolid("SouthWallWest", arena.transform, new Vector3(-41.75f, 4f, 44.5f), new Vector3(sideSpan, 8f, 1f), wallMaterial, ballSurface);
            CreateSolid("SouthWallEast", arena.transform, new Vector3(41.75f, 4f, 44.5f), new Vector3(sideSpan, 8f, 1f), wallMaterial, ballSurface);
            CreateSolid("EastWall", arena.transform, new Vector3(64.5f, 4f, 0f), new Vector3(1f, 8f, 88f), wallMaterial, ballSurface);
            CreateSolid("WestWall", arena.transform, new Vector3(-64.5f, 4f, 0f), new Vector3(1f, 8f, 88f), wallMaterial, ballSurface);

            CreateSolid("RampWest", arena.transform, new Vector3(-31f, 2.1f, 2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(-15f, 0f, 0f));
            CreateSolid("RampEast", arena.transform, new Vector3(31f, 2.1f, -2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(15f, 0f, 0f));

            var markings = new GameObject("Markings").transform;
            markings.SetParent(arena.transform, false);
            CreateMarking("CenterLine", markings, Vector3.zero, new Vector3(0.25f, 0.02f, 88f), markingMaterial);
            CreateMarking("NorthBox", markings, new Vector3(0f, 0.015f, -29f), new Vector3(36f, 0.02f, 0.25f), markingMaterial);
            CreateMarking("SouthBox", markings, new Vector3(0f, 0.015f, 29f), new Vector3(36f, 0.02f, 0.25f), markingMaterial);
            CreateMarking("CenterSpot", markings, new Vector3(0f, 0.015f, 0f), new Vector3(1f, 0.02f, 1f), markingMaterial);

            var north = BuildGoal("NorthGoal", GoalTrigger.GoalSide.North, -44f, frameMaterial, shieldMaterial, wallMaterial, ballSurface);
            var south = BuildGoal("SouthGoal", GoalTrigger.GoalSide.South, 44f, frameMaterial, shieldMaterial, wallMaterial, ballSurface);
            north.Root.transform.SetParent(arena.transform, true);
            south.Root.transform.SetParent(arena.transform, true);

            var containment = new GameObject("Containment").transform;
            containment.SetParent(arena.transform, false);
            CreateContainment("FloorContainment", containment, new Vector3(0f, -4f, 0f), new Vector3(140f, 1f, 120f), ballSurface);
            CreateContainment("CeilingContainment", containment, new Vector3(0f, 14f, 0f), new Vector3(140f, 1f, 120f), ballSurface);
            CreateContainment("EastContainment", containment, new Vector3(69f, 5f, 0f), new Vector3(1f, 20f, 120f), ballSurface);
            CreateContainment("WestContainment", containment, new Vector3(-69f, 5f, 0f), new Vector3(1f, 20f, 120f), ballSurface);
            CreateContainment("NorthContainment", containment, new Vector3(0f, 5f, -59f), new Vector3(140f, 20f, 1f), ballSurface);
            CreateContainment("SouthContainment", containment, new Vector3(0f, 5f, 59f), new Vector3(140f, 20f, 1f), ballSurface);
            CreateContainment("NorthGoalOpeningContainment", containment, new Vector3(0f, 3.5f, -57f), new Vector3(38f, 8f, 1f), ballSurface);
            CreateContainment("SouthGoalOpeningContainment", containment, new Vector3(0f, 3.5f, 57f), new Vector3(38f, 8f, 1f), ballSurface);

            return new ArenaBuild
            {
                Root = arena,
                NorthGoal = north,
                SouthGoal = south,
                Shields = new[] { north.Shield, south.Shield }
            };
        }

        private static GoalBuild BuildGoal(string name, GoalTrigger.GoalSide side, float z, Material frameMaterial, Material shieldMaterial, Material wallMaterial, PhysicsMaterial ballSurface)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(0f, 0f, z);
            var triggerCollider = root.AddComponent<BoxCollider>();
            var trigger = root.AddComponent<GoalTrigger>();
            triggerCollider.isTrigger = true;
            triggerCollider.center = new Vector3(0f, 3.5f, 0f);
            triggerCollider.size = new Vector3(36f, 7f, 0.5f);
            SetEnum(trigger, "goalSide", side == GoalTrigger.GoalSide.North ? "North" : "South");
            SetVector3(trigger, "planeNormal", Vector3.forward);
            SetFloat(trigger, "openingHalfWidth", 18f);
            SetFloat(trigger, "openingMinHeight", 0f);
            SetFloat(trigger, "openingMaxHeight", 7f);
            SetFloat(trigger, "rearmDistance", 0.5f);
            SetObjectReference(trigger, "planeReference", root.transform);
            SetObjectReference(trigger, "openingTrigger", triggerCollider);

            var shield = CreateSolid("Shield", root.transform, new Vector3(0f, 3.5f, 0f), new Vector3(36f, 7f, 0.4f), shieldMaterial, ballSurface);
            shield.GetComponent<Renderer>().sharedMaterial = shieldMaterial;
            CreateSolid("FrameWest", root.transform, new Vector3(-18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
            CreateSolid("FrameEast", root.transform, new Vector3(18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
            CreateSolid("FrameTop", root.transform, new Vector3(0f, 7.5f, 0f), new Vector3(38f, 1f, 1f), frameMaterial, ballSurface);
            CreateSolid("RecessWest", root.transform, new Vector3(-18.5f, 3.5f, side == GoalTrigger.GoalSide.North ? -4.5f : 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
            CreateSolid("RecessEast", root.transform, new Vector3(18.5f, 3.5f, side == GoalTrigger.GoalSide.North ? -4.5f : 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
            CreateSolid("RecessFloor", root.transform, new Vector3(0f, -0.25f, side == GoalTrigger.GoalSide.North ? -4.5f : 4.5f), new Vector3(37f, 0.5f, 9f), wallMaterial, ballSurface);
            CreateSolid("RecessBack", root.transform, new Vector3(0f, 3.5f, side == GoalTrigger.GoalSide.North ? -9f : 9f), new Vector3(37f, 7f, 1f), wallMaterial, ballSurface);
            return new GoalBuild { Root = root, Trigger = trigger, Shield = shield.GetComponent<Collider>() };
        }

        private static GameObject CreateSolid(string name, Transform parent, Vector3 position, Vector3 size, Material material, PhysicsMaterial ballSurface, Quaternion rotation = default)
        {
            var solid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            solid.name = name;
            solid.transform.SetParent(parent, false);
            solid.transform.localPosition = position;
            solid.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            solid.transform.localScale = size;
            solid.GetComponent<Renderer>().sharedMaterial = material;
            var collider = solid.GetComponent<Collider>();
            collider.sharedMaterial = ballSurface;
            return solid;
        }

        private static void CreateContainment(string name, Transform parent, Vector3 position, Vector3 size, PhysicsMaterial ballSurface)
        {
            var containment = new GameObject(name);
            containment.transform.SetParent(parent, false);
            containment.transform.localPosition = position;
            var collider = containment.AddComponent<BoxCollider>();
            collider.size = size;
            collider.sharedMaterial = ballSurface;
        }

        private static void CreateMarking(string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var marking = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marking.name = name;
            marking.transform.SetParent(parent, false);
            marking.transform.localPosition = position;
            marking.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(marking.GetComponent<Collider>());
            marking.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void ConfigureSceneLight()
        {
            var light = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                return;
            }
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        private static bool TryOpenExistingGeneratedScene(string builderSignature)
        {
            if (!File.Exists(ScenePath))
            {
                return false;
            }

            var active = SceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                active = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            if (!active.IsValid() || GameObject.Find(GetBuildMarkerName(builderSignature)) == null)
            {
                return false;
            }

            // Validation is the stale-content gate. It checks every generated
            // object, reference, prefab, material, physics setting, and build
            // scene before allowing byte-stable reuse.
            try
            {
                ValidateMovementLab();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ComputeBuilderSignature()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Unable to resolve Unity project root.");
            }

            var sourcePaths = new[]
            {
                BuilderSourcePath,
                ToonShaderPath,
                ParticleShaderPath,
                "Assets/_Game/Scripts/Runtime/ExplosionVfx.cs",
                "Assets/_Game/Scripts/Runtime/RocketTrailVfx.cs",
                "Assets/_Game/Scripts/Runtime/PlayerPresentation.cs",
                "Assets/_Game/Scripts/Runtime/BallKick.cs",
                "Assets/_Game/Scripts/Runtime/ExplosionResolver.cs",
                "Assets/_Game/Scripts/Runtime/RocketProjectile.cs",
                "Tools/Blender/generate_retro_textures.py",
                "Tools/Blender/generate_low_poly_character.py",
                "Tools/Blender/generate_fps_kick_rig.py",
                "Tools/Blender/generate_fps_rocket_launcher.py"
            };
            Array.Sort(sourcePaths, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                for (var i = 0; i < sourcePaths.Length; i++)
                {
                    var relativePath = sourcePaths[i].Replace('\\', '/');
                    var absolutePath = Path.Combine(projectRoot.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absolutePath)) throw new InvalidOperationException("Missing signature source: " + relativePath);
                    var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath + "\n");
                    sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);
                    var bytes = File.ReadAllBytes(absolutePath);
                    sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string GetBuildMarkerName(string builderSignature)
        {
            return BuildMarkerPrefix + builderSignature;
        }

        private static void RegisterBuildScene()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Material GetOrCreateRetroMaterial(string name, Color color, Texture2D texture, Vector2 textureScale)
        {
            var path = MaterialsPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("RocketFooxball/RetroToonLit");
            if (shader == null)
            {
                throw new InvalidOperationException("RocketFooxball/RetroToonLit shader is unavailable.");
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
            material.SetColor("_ShadowColor", new Color(0.08f, 0.12f, 0.24f, 1f));
            material.SetFloat("_LightSteps", 3f);
            material.SetTexture("_BaseMap", texture != null ? texture : Texture2D.whiteTexture);
            material.SetTextureScale("_BaseMap", textureScale);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateParticleMaterial(string name, Color color, Texture2D texture)
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

        private static void ConfigureTextureImporters()
        {
            ConfigureTextureImporter(GrassTexturePath, true);
            ConfigureTextureImporter(BallTexturePath, true);
            ConfigureTextureImporter(ExplosionTexturePath, false);
            ConfigureTextureImporter(SmokeTexturePath, false);
        }

        private static void ConfigureTextureImporter(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Missing texture importer: " + path);
            }

            var changed = false;
            if (!importer.sRGBTexture) { importer.sRGBTexture = true; changed = true; }
            if (!importer.mipmapEnabled) { importer.mipmapEnabled = true; changed = true; }
            if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; changed = true; }
            if (importer.anisoLevel != 0) { importer.anisoLevel = 0; changed = true; }
            var wrap = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (importer.wrapMode != wrap) { importer.wrapMode = wrap; changed = true; }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureModelImporters()
        {
            ConfigureRigModelImporter(CharacterModelPath, true);
            ConfigureRigModelImporter(FpsKickModelPath, false);
            ConfigureStaticModelImporter(WeaponModelPath);
        }

        private static void ConfigureRigModelImporter(string path, bool character)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Missing model importer: " + path);
            }

            var changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic) { importer.animationType = ModelImporterAnimationType.Generic; changed = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
            if (Mathf.Abs(importer.globalScale - 1f) > 0.0001f) { importer.globalScale = 1f; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }

            var sourceClips = importer.clipAnimations;
            var syntheticClips = false;
            if (sourceClips == null || sourceClips.Length == 0)
            {
                sourceClips = importer.defaultClipAnimations;
            }
            if (sourceClips == null || sourceClips.Length == 0)
            {
                // Unity may not expose FBX takes through defaultClipAnimations
                // until clipAnimations is explicitly seeded. Use deterministic
                // source ranges authored by the generators as a fallback.
                var idle = new ModelImporterClipAnimation { name = "Idle", takeName = "Idle", firstFrame = 1f, lastFrame = character ? 30f : 31f };
                var kick = new ModelImporterClipAnimation { name = "Kick", takeName = "Kick", firstFrame = 1f, lastFrame = character ? 12f : 11f };
                sourceClips = new[] { idle, kick };
                syntheticClips = true;
            }

            var clips = new List<ModelImporterClipAnimation>();
            for (var i = 0; i < sourceClips.Length; i++)
            {
                var source = sourceClips[i];
                var sourceName = source.name ?? string.Empty;
                var clipName = sourceName.IndexOf("Kick", StringComparison.OrdinalIgnoreCase) >= 0 ? "Kick" : sourceName.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0 ? "Idle" : string.Empty;
                if (!syntheticClips && clipName.Length == 0)
                {
                    continue;
                }
                if (syntheticClips) clipName = i == 0 ? "Idle" : "Kick";
                source.name = clipName;
                source.takeName = clipName;
                source.loopTime = clipName == "Idle";
                source.lockRootRotation = true;
                source.keepOriginalOrientation = true;
                source.lockRootHeightY = true;
                source.keepOriginalPositionY = true;
                source.lockRootPositionXZ = true;
                source.keepOriginalPositionXZ = true;
                source.heightFromFeet = false;
                source.hasAdditiveReferencePose = false;
                var duplicate = false;
                for (var j = 0; j < clips.Count; j++) duplicate |= clips[j].name == source.name;
                if (!duplicate) clips.Add(source);
            }
            if (clips.Count != 2)
            {
                clips.Clear();
                var idle = new ModelImporterClipAnimation { name = "Idle", takeName = "Idle", firstFrame = 1f, lastFrame = character ? 30f : 31f, loopTime = true };
                var kick = new ModelImporterClipAnimation { name = "Kick", takeName = "Kick", firstFrame = 1f, lastFrame = character ? 12f : 11f, loopTime = false };
                clips.Add(idle);
                clips.Add(kick);
            }
            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var configured = clips.ToArray();
            if (!ClipsEqual(importer.clipAnimations, configured))
            {
                importer.clipAnimations = configured;
                changed = true;
            }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static bool ClipsEqual(ModelImporterClipAnimation[] a, ModelImporterClipAnimation[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].name != b[i].name || a[i].loopTime != b[i].loopTime || a[i].lockRootRotation != b[i].lockRootRotation || a[i].lockRootHeightY != b[i].lockRootHeightY || a[i].lockRootPositionXZ != b[i].lockRootPositionXZ)
                {
                    return false;
                }
            }
            return true;
        }

        private static void ConfigureStaticModelImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("Missing model importer: " + path);
            }
            var changed = false;
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; changed = true; }
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (Mathf.Abs(importer.globalScale - 1f) > 0.0001f) { importer.globalScale = 1f; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static GameObject InstantiateImportedVisual(GameObject source, string name, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = localRotation;
            visual.transform.localScale = localScale;
            return visual;
        }

        private static void AssignImportedMaterials(GameObject visual, params Material[] materials)
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
                    else if (slotName.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0 || slotName.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0) chosen = materials.Length > 0 ? materials[0] : null;
                    else if (slotName.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 1) chosen = materials[1];
                    else if (slotName.IndexOf("Cream", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                    if (chosen != null) slots[j] = chosen;
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static void RemovePhysicsComponents(GameObject visual)
        {
            var colliders = visual.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++) UnityEngine.Object.DestroyImmediate(colliders[i]);
            var bodies = visual.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++) UnityEngine.Object.DestroyImmediate(bodies[i]);
        }

        private static Transform FindNamedTransform(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamedTransform(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static Avatar FindImportedAvatar(string modelPath)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar) return avatar;
            }
            return null;
        }

        private static RuntimeAnimatorController EnsureAnimatorController(string path, string modelPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            var idle = FindImportedClip(modelPath, "Idle");
            var kick = FindImportedClip(modelPath, "Kick");
            if (idle == null || kick == null)
            {
                throw new InvalidOperationException("Missing imported Idle/Kick clips for " + modelPath);
            }

            AnimatorState idleState = null;
            AnimatorState kickState = null;
            AnimatorStateTransition anyToKick = null;
            AnimatorStateTransition kickToIdle = null;
            var rebuild = controller == null;
            if (!rebuild)
            {
                var stateCount = 0;
                var transitionCount = 0;
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                for (var i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is AnimatorState) stateCount++;
                    if (subAssets[i] is AnimatorStateTransition) transitionCount++;
                }

                var existingStateMachine = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;
                var states = existingStateMachine != null ? existingStateMachine.states : Array.Empty<ChildAnimatorState>();
                for (var i = 0; i < states.Length; i++)
                {
                    if (states[i].state != null && states[i].state.name == "Idle") idleState = states[i].state;
                    if (states[i].state != null && states[i].state.name == "Kick") kickState = states[i].state;
                }

                if (existingStateMachine != null && idleState != null && kickState != null && existingStateMachine.anyStateTransitions.Length == 1)
                {
                    anyToKick = existingStateMachine.anyStateTransitions[0];
                }
                if (kickState != null && kickState.transitions.Length == 1)
                {
                    kickToIdle = kickState.transitions[0];
                }

                rebuild = stateCount != 2 || transitionCount != 2 || states.Length != 2 ||
                    idleState == null || kickState == null || anyToKick == null || kickToIdle == null ||
                    anyToKick.destinationState != kickState || kickToIdle.destinationState != idleState ||
                    idleState.transitions.Length != 0;
            }

            if (rebuild)
            {
                if (controller != null && !AssetDatabase.DeleteAsset(path))
                {
                    throw new InvalidOperationException("Failed to rebuild stale Animator controller: " + path);
                }
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                var newStateMachine = controller.layers[0].stateMachine;
                idleState = newStateMachine.AddState("Idle");
                kickState = newStateMachine.AddState("Kick");
                anyToKick = newStateMachine.AddAnyStateTransition(kickState);
                kickToIdle = kickState.AddTransition(idleState);
            }

            if (controller.parameters.Length != 1 || controller.parameters[0].name != "Kick" || controller.parameters[0].type != AnimatorControllerParameterType.Trigger)
            {
                while (controller.parameters.Length > 0) controller.RemoveParameter(0);
                controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);
            }

            var stateMachine = controller.layers[0].stateMachine;
            idleState.motion = idle;
            kickState.motion = kick;
            stateMachine.defaultState = idleState;
            anyToKick.hasExitTime = false;
            anyToKick.duration = 0.02f;
            anyToKick.canTransitionToSelf = false;
            anyToKick.conditions = Array.Empty<AnimatorCondition>();
            anyToKick.AddCondition(AnimatorConditionMode.If, 0f, "Kick");
            kickToIdle.hasExitTime = true;
            kickToIdle.exitTime = 1f;
            kickToIdle.duration = 0.02f;
            kickToIdle.conditions = Array.Empty<AnimatorCondition>();
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(idleState);
            EditorUtility.SetDirty(kickState);
            EditorUtility.SetDirty(anyToKick);
            EditorUtility.SetDirty(kickToIdle);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimationClip FindImportedClip(string modelPath, string name)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);

            // Prefer an exact imported take name. Model importers commonly prefix
            // clips with the source model name, so a broad substring match can
            // bind e.g. "FpsKickRig|Idle" to the Kick state just because the
            // model prefix contains "Kick".
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is AnimationClip clip && string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase)) return clip;
            }

            // Fall back to a delimiter-safe take suffix ("|Idle", "@Kick",
            // etc.). The character before the take must be a non-alphanumeric
            // delimiter; this excludes model-prefix substrings such as
            // "FpsKickRig|Idle" when looking for Kick.
            for (var i = 0; i < assets.Length; i++)
            {
                if (!(assets[i] is AnimationClip clip)) continue;
                var clipName = clip.name;
                if (clipName.Length <= name.Length || !clipName.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                var delimiter = clipName[clipName.Length - name.Length - 1];
                if (!char.IsLetterOrDigit(delimiter)) return clip;
            }
            return null;
        }

        private static void BuildCrosshair(Camera camera)
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

        private static void AddCrosshairImage(Transform parent, string name, Color color, Vector2 size, Vector2 pivot, Vector2 position)
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

        private static int EnsureLocalPlayerHiddenLayer()
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

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            for (var i = 0; i < root.transform.childCount; i++) SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }

        private static void ValidateLayerRecursively(GameObject root, int expectedLayer, string label)
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

        private static void ValidateLayerExcluded(GameObject root, int excludedLayer, string label)
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

        private static PhysicsMaterial GetOrCreatePhysicMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(BallSurfacePath);
            if (material == null)
            {
                material = new PhysicsMaterial("BallSurface");
                AssetDatabase.CreateAsset(material, BallSurfacePath);
            }
            material.dynamicFriction = 0.08f;
            material.staticFriction = 0.08f;
            material.bounciness = 0.65f;
            material.frictionCombine = PhysicsMaterialCombine.Minimum;
            material.bounceCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetProjectFixedTimestep()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (settings.Length == 0)
            {
                throw new InvalidOperationException("Unable to load ProjectSettings/TimeManager.asset.");
            }
            var serializedSettings = new SerializedObject(settings[0]);
            var fixedTimestep = serializedSettings.FindProperty("Fixed Timestep");
            if (fixedTimestep == null)
            {
                throw new InvalidOperationException("TimeManager 'Fixed Timestep' property is unavailable.");
            }
            var count = fixedTimestep.FindPropertyRelative("m_Count");
            var rate = fixedTimestep.FindPropertyRelative("m_Rate");
            var denominator = rate?.FindPropertyRelative("m_Denominator");
            var numerator = rate?.FindPropertyRelative("m_Numerator");
            if (count == null || denominator == null || numerator == null || denominator.longValue == 0)
            {
                throw new InvalidOperationException("TimeManager fixed-step RationalTime fields are unavailable.");
            }
            count.longValue = Convert.ToInt64(Math.Round((double)numerator.longValue / denominator.longValue * GamePhysicsSettings.FixedDeltaTime));
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings[0]);
        }

        private static void ValidatePhysicsAndBuildSettings()
        {
            if (Mathf.Abs(Time.fixedDeltaTime - GamePhysicsSettings.FixedDeltaTime) > 0.00001f)
            {
                throw new InvalidOperationException("Fixed timestep is not 60 Hz.");
            }
            if (Mathf.Abs(Physics.gravity.y + GamePhysicsSettings.GravityMagnitude) > 0.0001f || Mathf.Abs(Physics.gravity.x) > 0.0001f || Mathf.Abs(Physics.gravity.z) > 0.0001f)
            {
                throw new InvalidOperationException("Physics gravity does not match -16.875 m/s².");
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath || !scenes[0].enabled)
            {
                throw new InvalidOperationException("MovementLab must be the sole enabled build scene.");
            }
        }

        private static void ValidateArenaMaterials(GameObject arena, PhysicsMaterial ballSurface)
        {
            var floor = Require(arena.transform.Find("Floor"), "Arena Floor");
            var floorRenderer = Require(floor.GetComponent<Renderer>(), "Arena Floor renderer");
            ValidateRetroMaterial(floorRenderer.sharedMaterial, AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath), new Vector2(32.5f, 22.5f), "Floor");
            var colliders = arena.GetComponentsInChildren<Collider>(true);
            var relevant = 0;
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.isTrigger || collider.name == "Shield")
                {
                    continue;
                }
                relevant++;
                if (collider.sharedMaterial != ballSurface)
                {
                    throw new InvalidOperationException("Arena collider missing shared BallSurface: " + collider.name);
                }
            }
            if (relevant < 12)
            {
                throw new InvalidOperationException("Arena has too few shared-surface colliders.");
            }
            Require(arena.transform.Find("RampWest"), "West ramp");
            Require(arena.transform.Find("RampEast"), "East ramp");
            Require(arena.transform.Find("NorthGoal"), "North goal root");
            Require(arena.transform.Find("SouthGoal"), "South goal root");
            Require(arena.transform.Find("Containment"), "Containment root");
        }

        private static void ValidateRetroMaterial(Material material, Texture2D texture, Vector2 scale, string label)
        {
            if (material == null || material.shader == null || material.shader.name != "RocketFooxball/RetroToonLit") throw new InvalidOperationException(label + " must use RetroToonLit.");
            if (material.GetTexture("_BaseMap") != (texture != null ? texture : Texture2D.whiteTexture)) throw new InvalidOperationException(label + " base texture mismatch.");
            if (material.GetTextureScale("_BaseMap") != scale) throw new InvalidOperationException(label + " texture scale mismatch.");
            if (Mathf.Abs(material.GetFloat("_LightSteps") - 3f) > 0.001f) throw new InvalidOperationException(label + " light step contract mismatch.");
        }

        private static void ValidateTextureImporterContracts()
        {
            ValidateTextureImporter(GrassTexturePath, true);
            ValidateTextureImporter(BallTexturePath, true);
            ValidateTextureImporter(ExplosionTexturePath, false);
            ValidateTextureImporter(SmokeTexturePath, false);
        }

        private static void ValidateTextureImporter(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !importer.sRGBTexture || !importer.mipmapEnabled || importer.filterMode != FilterMode.Bilinear || importer.anisoLevel != 0 || importer.wrapMode != (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp))
            {
                throw new InvalidOperationException("Texture importer contract invalid: " + path);
            }
        }

        private static void ValidateModelImporterContracts()
        {
            ValidateRigImporter(CharacterModelPath);
            ValidateRigImporter(FpsKickModelPath);
            var weapon = AssetImporter.GetAtPath(WeaponModelPath) as ModelImporter;
            if (weapon == null || weapon.animationType != ModelImporterAnimationType.None || weapon.importAnimation || weapon.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(weapon.globalScale - 1f) > 0.0001f)
            {
                throw new InvalidOperationException("Weapon importer contract invalid.");
            }
        }

        private static void ValidateRigImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.importAnimation || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
            {
                throw new InvalidOperationException("Rig importer contract invalid: " + path);
            }
            var clips = importer.clipAnimations;
            var idle = false;
            var kick = false;
            for (var i = 0; clips != null && i < clips.Length; i++)
            {
                idle |= clips[i].name == "Idle" && clips[i].loopTime;
                kick |= clips[i].name == "Kick" && !clips[i].loopTime;
            }
            if (!idle || !kick) throw new InvalidOperationException("Rig importer missing Idle/Kick clip contract: " + path);
        }

        private static void ValidatePrefab(string path, string expectedName, bool dynamicBody, PhysicsMaterial ballSurface)
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
                    ValidateReference(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath), "PlayerInputReader.actions");
                    var prefabLauncher = Require(root.GetComponent<RocketLauncher>(), "Player prefab RocketLauncher");
                    var prefabKick = Require(root.GetComponent<BallKick>(), "Player prefab BallKick");
                    var prefabFeedback = Require(root.GetComponent<PlayerCameraFeedback>(), "Player prefab PlayerCameraFeedback");
                    var prefabPresentation = Require(root.GetComponent<PlayerPresentation>(), "Player prefab PlayerPresentation");
                    ValidateReference(prefabLauncher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "Player prefab RocketLauncher.projectilePrefab");
                    ValidateReference(prefabLauncher, "spawnPoint", root.transform.Find("Head/Camera/RocketMuzzle"), "Player prefab RocketLauncher.spawnPoint");
                    ValidateReference(prefabFeedback, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab PlayerCameraFeedback.targetCamera");
                    ValidateReference(prefabKick, "aimCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab BallKick.aimCamera");
                    ValidateReference(prefabPresentation, "kick", prefabKick, "Player prefab PlayerPresentation.kick");
                    var prefabCamera = root.transform.Find("Head/Camera").GetComponent<Camera>();
                    ValidateCrosshair(prefabCamera);
                    ValidateNoPhysics(root.transform.Find("Head/Camera/Viewmodels/WeaponVisual").gameObject, "Player prefab WeaponVisual");
                    ValidateNoPhysics(root.transform.Find("Head/Camera/Viewmodels/FpsKickVisual").gameObject, "Player prefab FpsKickVisual");
                }
                else if (path == BallPrefabPath)
                {
                    var body = Require(root.GetComponent<Rigidbody>(), "Ball prefab Rigidbody");
                    var collider = Require(root.GetComponent<Collider>(), "Ball prefab collider");
                    var motor = Require(root.GetComponent<BallMotor>(), "Ball prefab BallMotor");
                    ValidateReference(motor, "body", body, "Ball prefab BallMotor.body");
                    ValidateReference(motor, "ballCollider", collider, "Ball prefab BallMotor.ballCollider");
                    if (body.isKinematic != !dynamicBody || body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic || collider.sharedMaterial != ballSurface)
                    {
                        throw new InvalidOperationException("Ball prefab Rigidbody/collider settings invalid.");
                    }
                    var renderer = Require(root.GetComponent<Renderer>(), "Ball prefab renderer");
                    var material = renderer.sharedMaterial;
                    ValidateRetroMaterial(material, AssetDatabase.LoadAssetAtPath<Texture2D>(BallTexturePath), Vector2.one, "Ball");
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
                    for (var i = 0; i < meshFilters.Length; i++)
                    {
                        var mesh = meshFilters[i].sharedMesh;
                        if (mesh == null || AssetDatabase.GetAssetPath(mesh) != RocketModelPath)
                        {
                            throw new InvalidOperationException("Rocket prefab Visual must use imported rocket mesh.");
                        }
                        Require(meshFilters[i].GetComponent<Renderer>(), "Rocket prefab imported mesh renderer");
                    }
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

        private static void ValidateRocketVisualForward(Transform rocketRoot, MeshFilter[] meshFilters)
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

        private static void ValidateImportedVisual(GameObject visual, string sourcePath, string label)
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

        private static void ValidateNoPhysics(GameObject root, string label)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length > 0 || root.GetComponentsInChildren<Rigidbody>(true).Length > 0)
            {
                throw new InvalidOperationException(label + " must not contain physics components.");
            }
        }

        private static void ValidateAnimatorController(Animator animator, string path, string modelPath)
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

        private static void ValidateCrosshair(Camera camera)
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

        private static void ValidateTrail(GameObject rocketPrefab)
        {
            if (rocketPrefab == null) throw new InvalidOperationException("Rocket prefab unavailable for trail validation.");
            var trail = Require(rocketPrefab.GetComponentInChildren<RocketTrailVfx>(true), "RocketTrailVfx");
            var systems = trail.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length != 1) throw new InvalidOperationException("Rocket trail must contain one particle system.");
            var main = systems[0].main;
            if (main.maxParticles > 48 || main.simulationSpace != ParticleSystemSimulationSpace.World || Mathf.Abs(main.startLifetime.constantMax - 0.45f) > 0.01f) throw new InvalidOperationException("Rocket trail particle contract invalid.");
            var projectile = Require(rocketPrefab.GetComponent<RocketProjectile>(), "RocketProjectile");
            ValidateReference(projectile, "trailVfx", trail, "RocketProjectile.trailVfx");
        }

        private static void ValidateExplosionPrefab(GameObject prefab)
        {
            if (prefab == null) throw new InvalidOperationException("Explosion prefab unavailable.");
            var effect = Require(prefab.GetComponent<ExplosionVfx>(), "ExplosionVfx");
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length != 4) throw new InvalidOperationException("Explosion VFX must contain Flash/FireChunks/Sparks/Smoke systems.");
            var emitted = 0;
            for (var i = 0; i < systems.Length; i++)
            {
                var emission = systems[i].emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                for (var j = 0; j < bursts.Length; j++) emitted += bursts[j].maxCount;
                if (systems[i].main.maxParticles > 40) throw new InvalidOperationException("Explosion particle max exceeds POC budget.");
            }
            if (emitted != 37) throw new InvalidOperationException("Explosion burst count must total 37.");
            var renderer = prefab.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderer.Length; i++) if (renderer[i].GetComponent<Collider>() != null || renderer[i].GetComponent<Rigidbody>() != null) throw new InvalidOperationException("Explosion VFX must not contain physics.");
            var serialized = new SerializedObject(effect);
            var configured = serialized.FindProperty("particleSystems");
            if (configured == null || !configured.isArray || configured.arraySize != 4) throw new InvalidOperationException("ExplosionVfx.particleSystems must contain four systems.");
        }

        private static void ValidateNoMissingComponents(Scene scene)
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

        private static void EnsureAssetExists(string path)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("Missing generated asset: " + path);
            }
        }

        // Unity emits empty serialized fields as `key: `, which trips git
        // whitespace checks. Trim trailing spaces only; preserve YAML structure,
        // line endings, file IDs, and GUIDs.
        private static void NormalizeGeneratedYamlWhitespace()
        {
            for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
            {
                NormalizeYamlFile(GeneratedYamlAssetPaths[i]);
                NormalizeYamlFile(GeneratedYamlAssetPaths[i] + ".meta");
            }
        }

        private static void NormalizeYamlFile(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            var source = File.ReadAllText(path);
            var normalized = Regex.Replace(source, @"[ \t]+(?=\r?$)", string.Empty, RegexOptions.Multiline);
            if (!string.Equals(source, normalized, StringComparison.Ordinal))
            {
                File.WriteAllText(path, normalized, new System.Text.UTF8Encoding(false));
            }
        }

        private static T Require<T>(T value, string label) where T : UnityEngine.Object
        {
            if (value == null)
            {
                throw new InvalidOperationException("Missing required " + label + ".");
            }
            return value;
        }

        private static void ValidateReference(UnityEngine.Object target, string propertyName, UnityEngine.Object expected, string label)
        {
            if (target == null || expected == null)
            {
                throw new InvalidOperationException(label + " reference is null.");
            }
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != expected)
            {
                throw new InvalidOperationException(label + " reference is broken.");
            }
        }

        private static void ValidatePrefabReference(UnityEngine.Object target, string propertyName, string prefabPath, string label)
        {
            if (target == null) throw new InvalidOperationException(label + " target is null.");
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null)
            {
                throw new InvalidOperationException(label + " reference is null.");
            }
            var component = property.objectReferenceValue as Component;
            var sourcePath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
            if (string.IsNullOrEmpty(sourcePath))
            {
                var source = component != null ? PrefabUtility.GetCorrespondingObjectFromSource(component) : PrefabUtility.GetCorrespondingObjectFromSource(property.objectReferenceValue);
                sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty;
            }
            if (sourcePath != prefabPath)
            {
                throw new InvalidOperationException(label + " prefab provenance mismatch: " + sourcePath);
            }
        }

        private static T GetSerializablePrefabComponent<T>(GameObject prefabAsset, out GameObject instance) where T : Component
        {
            instance = null;
            if (prefabAsset == null)
            {
                return null;
            }

            instance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (instance != null)
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
            }
            var instanceComponent = instance != null ? instance.GetComponent<T>() : null;
            return instanceComponent != null ? PrefabUtility.GetCorrespondingObjectFromSource(instanceComponent) : null;
        }

        private static void ValidateArrayContains(UnityEngine.Object target, string propertyName, UnityEngine.Object first, UnityEngine.Object second, string label)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray || property.arraySize != 2)
            {
                throw new InvalidOperationException(label + " must contain two colliders.");
            }
            var a = property.GetArrayElementAtIndex(0).objectReferenceValue;
            var b = property.GetArrayElementAtIndex(1).objectReferenceValue;
            if (!((a == first && b == second) || (a == second && b == first)))
            {
                throw new InvalidOperationException(label + " does not contain both goal shields.");
            }
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized field '" + propertyName + "'.");
            }
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(UnityEngine.Object target, string propertyName, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || !property.isArray)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized array '" + propertyName + "'.");
            }
            property.arraySize = values == null ? 0 : values.Length;
            for (var i = 0; values != null && i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized float '" + propertyName + "'.");
            }
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector3(UnityEngine.Object target, string propertyName, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized Vector3 '" + propertyName + "'.");
            }
            property.vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(UnityEngine.Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Enum)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no enum '" + propertyName + "'.");
            }
            var index = Array.IndexOf(property.enumDisplayNames, value);
            if (index < 0)
            {
                throw new InvalidOperationException("Unknown enum value " + value + " for " + propertyName + ".");
            }
            property.enumValueIndex = index;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/_Game");
            EnsureFolder(MaterialsPath);
            EnsureFolder(TexturesPath);
            EnsureFolder(ShadersPath);
            EnsureFolder(AnimationsPath);
            EnsureFolder("Assets/_Game/Prefabs");
            EnsureFolder("Assets/_Game/Models");
            EnsureFolder("Assets/_Game/Scenes");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }
    }
}
