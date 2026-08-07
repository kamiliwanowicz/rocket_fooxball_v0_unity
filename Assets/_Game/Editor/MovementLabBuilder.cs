using System;
using System.IO;
using System.Text.RegularExpressions;
using RocketFooxball;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
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
        private const string ScenePath = "Assets/_Game/Scenes/MovementLab.unity";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MaterialsPath = "Assets/_Game/Materials";
        private const string BallSurfacePath = MaterialsPath + "/BallSurface.physicMaterial";
        private const string BuildMarkerName = "MovementLabGeneratedT3";

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
            BallSurfacePath
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

            var ballSurface = GetOrCreatePhysicMaterial();
            var floorMaterial = GetOrCreateMaterial("Floor", new Color(0.16f, 0.32f, 0.19f));
            var wallMaterial = GetOrCreateMaterial("Wall", new Color(0.16f, 0.22f, 0.34f));
            var markingMaterial = GetOrCreateMaterial("Marking", new Color(0.9f, 0.9f, 0.9f));
            var ballMaterial = GetOrCreateMaterial("Ball", new Color(0.95f, 0.53f, 0.08f));
            var rocketMaterial = GetOrCreateMaterial("Rocket", new Color(0.95f, 0.23f, 0.08f));
            var frameMaterial = GetOrCreateMaterial("GoalFrame", new Color(0.14f, 0.65f, 0.9f));
            var shieldMaterial = GetOrCreateMaterial("Shield", new Color(0.15f, 0.8f, 0.95f));

            var rocketPrefab = BuildRocketPrefab(rocketMaterial);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RocketPrefabPath, ImportAssetOptions.ForceSynchronousImport);
            rocketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath);
            var ballPrefab = BuildBallPrefab(ballMaterial, ballSurface);
            var playerPrefab = BuildPlayerPrefab(rocketPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Unity assigns new local file IDs when a scene is recreated. Reuse an
            // already generated T3 scene on later builder invocations so a clean
            // second build is byte-stable while first build remains authoritative.
            if (TryOpenExistingGeneratedScene())
            {
                RegisterBuildScene();
                Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
                SetProjectFixedTimestep();
                AssetDatabase.SaveAssets();
                NormalizeGeneratedYamlWhitespace();
                Debug.Log("Rocket Fooxball Movement Lab built: " + ScenePath + " (existing T3 scene reused)");
                return;
            }

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

            new GameObject(BuildMarkerName);

            ConfigureSceneLight();
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterBuildScene();
            Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
            SetProjectFixedTimestep();
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
            Require(GameObject.Find(BuildMarkerName), "T3 build marker");

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
            SetFloat(kick, "coneTotalDegrees", 35f);
            SetFloat(kick, "cooldown", 0.40f);
            SetFloat(kick, "inputBuffer", 0.50f);
            SetFloat(kick, "speedFraction", 0.70f);
            SetFloat(kick, "playerMomentumShare", 0.20f);
            SetFloat(feedback, "baseFov", 75f);
            SetFloat(feedback, "maxFov", 84f);

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
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.name = "Rocket";
            root.transform.localScale = Vector3.one * 0.24f;
            root.GetComponent<Renderer>().sharedMaterial = rocketMaterial;
            var collider = root.GetComponent<SphereCollider>();
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
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RocketPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
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

        private static bool TryOpenExistingGeneratedScene()
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
            return active.IsValid() && GameObject.Find(BuildMarkerName) != null;
        }

        private static void RegisterBuildScene()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            var path = MaterialsPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new InvalidOperationException("URP Lit shader is unavailable.");
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
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
                    ValidateReference(prefabLauncher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "Player prefab RocketLauncher.projectilePrefab");
                    ValidateReference(prefabLauncher, "spawnPoint", root.transform.Find("Head/Camera/RocketMuzzle"), "Player prefab RocketLauncher.spawnPoint");
                    ValidateReference(prefabFeedback, "targetCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab PlayerCameraFeedback.targetCamera");
                    ValidateReference(prefabKick, "aimCamera", root.transform.Find("Head/Camera").GetComponent<Camera>(), "Player prefab BallKick.aimCamera");
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
                }
                else if (path == RocketPrefabPath)
                {
                    var body = Require(root.GetComponent<Rigidbody>(), "Rocket prefab Rigidbody");
                    var collider = Require(root.GetComponent<Collider>(), "Rocket prefab collider");
                    var projectile = Require(root.GetComponent<RocketProjectile>(), "Rocket prefab RocketProjectile");
                    ValidateReference(projectile, "body", body, "Rocket prefab RocketProjectile.body");
                    ValidateReference(projectile, "projectileCollider", collider, "Rocket prefab RocketProjectile.projectileCollider");
                    if (!body.isKinematic || body.useGravity || body.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
                    {
                        throw new InvalidOperationException("Rocket prefab Rigidbody settings invalid.");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
            EnsureFolder("Assets/_Game/Prefabs");
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
