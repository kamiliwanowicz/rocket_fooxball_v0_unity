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
using UnityEngine.Rendering;

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
        private const string ArenaKitModelPath = "Assets/_Game/Models/ArenaKit.fbx";
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
        private const string WeaponMetalTexturePath = TexturesPath + "/RetroWeaponMetal.png";
        private const string WeaponDarkTexturePath = TexturesPath + "/RetroWeaponDark.png";
        private const string WeaponAccentTexturePath = TexturesPath + "/RetroWeaponAccent.png";
        private const string ExplosionTexturePath = TexturesPath + "/RetroExplosion.png";
        private const string SmokeTexturePath = TexturesPath + "/RetroSmoke.png";
        private const string WallTexturePath = TexturesPath + "/RetroWall.png";
        private const string TrimTexturePath = TexturesPath + "/RetroTrim.png";
        private const string HazardTexturePath = TexturesPath + "/RetroHazard.png";
        private const string ShieldTexturePath = TexturesPath + "/RetroShield.png";
        private const string ToonShaderPath = ShadersPath + "/RetroToonLit.shader";
        private const string ParticleShaderPath = ShadersPath + "/RetroParticle.shader";
        private const string ShieldShaderPath = ShadersPath + "/RetroShield.shader";
        private const string BallSurfacePath = MaterialsPath + "/BallSurface.physicMaterial";
        private const string BuilderSourcePath = "Assets/_Game/Editor/MovementLabBuilder.cs";
        private const string RocketLauncherSourcePath = "Assets/_Game/Scripts/Runtime/RocketLauncher.cs";
        private const string RocketGeneratorSourcePath = "Tools/Blender/generate_low_poly_rocket.py";
        private const string ManifestPath = "Assets/_Game/Generated/MovementLabBuildManifest.json";
        private const int ManifestSchemaVersion = 1;
        private const string BuildMarkerPrefix = "MovementLabGeneratedT5_";
        private const float BallPrefabScale = 4.32f;
        private const float BallRadius = 2.16f;
        private const float BallSpawnHeight = BallRadius;
        private const float BlastRadius = 5.85f;
        private const float BlastVisualScale = 1.30f;
        private const float GoalAxisPosition = 64f;
        private const float PlayerSpawnOffset = 12f;
        private const float GoalFreezeDuration = 5f;
        private const float CelebrationOrbitRadius = 5.5f;
        private const float CelebrationOrbitHeight = 2.5f;
        private const float CelebrationLookHeight = 1.05f;
        private const float CelebrationOrbitDegrees = 360f;
        private const float CelebrationFov = 60f;
        private const float RocketTrailLifetime = 0.55f;
        private const float RocketTrailRateOverDistance = 5f;
        private const float RocketTrailStartSize = 0.34f;
        private static readonly Color RocketTrailStartColor = new Color(0.48f, 0.45f, 0.40f, 0.82f);
        private static readonly Color RocketTrailEndColor = new Color(0.24f, 0.22f, 0.20f, 1f);
        private static readonly Color RocketEmissionColor = new Color(1.00f, 0.20f, 0.04f, 1f);
        private const float RocketEmissionStrength = 0.80f;
        private static readonly Color RocketBaseColor = new Color(1.00f, 0.22f, 0.20f, 1f);
        private static readonly Color RocketLightColor = new Color(1.00f, 0.28f, 0.08f, 1f);
        private const float RocketLightIntensity = 1.80f;
        private const float RocketLightRange = 4.50f;
        private static readonly Color WeaponMetalBaseColor = new Color(0.95f, 0.86f, 0.70f, 1f);
        private static readonly Color WeaponDarkBaseColor = new Color(0.88f, 0.90f, 0.92f, 1f);
        private static readonly Color WeaponAccentBaseColor = new Color(0.95f, 0.56f, 0.38f, 1f);
        private static readonly Color ExplosionFireMaterialColor = new Color(1.00f, 0.28f, 0.04f, 0.98f);
        private static readonly Color ExplosionSmokeMaterialColor = new Color(0.38f, 0.36f, 0.33f, 0.72f);

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
            MaterialsPath + "/Trim.mat",
            MaterialsPath + "/Hazard.mat",
            MaterialsPath + "/Marking.mat",
            MaterialsPath + "/Ball.mat",
            MaterialsPath + "/Rocket.mat",
            MaterialsPath + "/GoalFrame.mat",
            MaterialsPath + "/Shield.mat",
            MaterialsPath + "/ShieldBlue.mat",
            MaterialsPath + "/ShieldRed.mat",
            MaterialsPath + "/ArenaPrimary.mat",
            MaterialsPath + "/ArenaTrim.mat",
            MaterialsPath + "/ArenaHazard.mat",
            MaterialsPath + "/ArenaGlow.mat",
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

        // Fingerprint every builder-owned output and the importer/project state
        // that can change how those outputs are interpreted. The manifest and
        // its .meta are intentionally excluded to avoid a self-hash loop.
        private static readonly string[] GeneratedFingerprintPaths = CreateGeneratedFingerprintPaths();
        private static readonly string[] GeneratedImporterMetadataPaths =
        {
            RocketModelPath + ".meta", ArenaKitModelPath + ".meta", CharacterModelPath + ".meta", FpsKickModelPath + ".meta", WeaponModelPath + ".meta",
            GrassTexturePath + ".meta", WallTexturePath + ".meta", TrimTexturePath + ".meta", HazardTexturePath + ".meta", ShieldTexturePath + ".meta",
            BallTexturePath + ".meta", WeaponMetalTexturePath + ".meta", WeaponDarkTexturePath + ".meta", WeaponAccentTexturePath + ".meta",
            ExplosionTexturePath + ".meta", SmokeTexturePath + ".meta"
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

        private readonly struct MaterialSpecification
        {
            public readonly string Name;
            public readonly string ShaderName;
            public readonly Texture2D Texture;
            public readonly Vector2 TextureScale;
            public readonly Color BaseColor;
            public readonly Color ShadowColor;
            public readonly Color AmbientColor;
            public readonly float AmbientStrength;
            public readonly Color RimColor;
            public readonly float RimPower;
            public readonly float RimStrength;
            public readonly Color EmissionColor;
            public readonly float EmissionStrength;

            public MaterialSpecification(string name, string shaderName, Texture2D texture, Vector2 textureScale,
                Color baseColor, Color shadowColor, Color ambientColor, float ambientStrength,
                Color rimColor, float rimPower, float rimStrength, Color emissionColor, float emissionStrength)
            {
                Name = name;
                ShaderName = shaderName;
                Texture = texture;
                TextureScale = textureScale;
                BaseColor = baseColor;
                ShadowColor = shadowColor;
                AmbientColor = ambientColor;
                AmbientStrength = ambientStrength;
                RimColor = rimColor;
                RimPower = rimPower;
                RimStrength = rimStrength;
                EmissionColor = emissionColor;
                EmissionStrength = emissionStrength;
            }
        }

        [Serializable]
        private sealed class MovementLabBuildManifest
        {
            public int schemaVersion;
            public string sourceSignature;
            public string generatedOutputFingerprint;
            public string unityVersion;
            public string[] fingerprintPaths;
        }

        private static string[] CreateGeneratedFingerprintPaths()
        {
            var paths = new List<string>();
            for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
            {
                paths.Add(GeneratedYamlAssetPaths[i]);
            }

            var generatedSourcePaths = new[]
            {
                RocketModelPath,
                CharacterModelPath,
                FpsKickModelPath,
                WeaponModelPath,
                ArenaKitModelPath,
                GrassTexturePath,
                WallTexturePath,
                TrimTexturePath,
                HazardTexturePath,
                ShieldTexturePath,
                BallTexturePath,
                WeaponMetalTexturePath,
                WeaponDarkTexturePath,
                WeaponAccentTexturePath,
                ExplosionTexturePath,
                SmokeTexturePath
            };
            for (var i = 0; i < generatedSourcePaths.Length; i++)
            {
                paths.Add(generatedSourcePaths[i]);
                paths.Add(generatedSourcePaths[i] + ".meta");
            }

            paths.Add("ProjectSettings/EditorBuildSettings.asset");
            paths.Add("ProjectSettings/DynamicsManager.asset");
            paths.Add("ProjectSettings/TimeManager.asset");
            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        [MenuItem("Rocket Fooxball/Build Movement Lab")]
        public static void BuildMovementLab()
        {
            var builderSignature = ComputeBuilderSignature();
            if (TryReuseGeneratedState(builderSignature))
            {
                return;
            }

            EnsureFolders();

            ConfigureTextureImporters();
            ConfigureModelImporters();

            var ballSurface = GetOrCreatePhysicMaterial();
            var shadow = new Color(0.32f, 0.44f, 0.62f, 1f);
            var ambient = new Color(0.72f, 0.86f, 1.00f, 1f);
            var rim = new Color(0.20f, 0.86f, 0.92f, 1f);
            var floorMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Floor", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath), new Vector2(32.5f, 22.5f), new Color(0.96f, 1f, 1f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var wallMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Wall", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(WallTexturePath), new Vector2(8f, 2f), new Color(0.90f, 0.95f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var trimMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Trim", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), new Vector2(4f, 1f), new Color(0.10f, 0.75f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, new Color(0.10f, 0.85f, 1f, 1f), 0.35f));
            var hazardMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Hazard", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(HazardTexturePath), new Vector2(4f, 1f), new Color(1.00f, 0.72f, 0.12f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var markingMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Marking", "RocketFooxball/RetroToonLit", null, Vector2.one, new Color(1.00f, 0.96f, 0.78f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var ballMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Ball", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(BallTexturePath), Vector2.one, new Color(1f, 1f, 1f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var rocketMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Rocket", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), Vector2.one, RocketBaseColor, shadow, ambient, 0.55f, rim, 3f, 0.12f, RocketEmissionColor, RocketEmissionStrength));
            var frameMaterial = trimMaterial;
            var shieldMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("Shield", "RocketFooxball/RetroToonLit", null, Vector2.one, new Color(0.10f, 0.75f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var arenaPrimaryMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("ArenaPrimary", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(WallTexturePath), Vector2.one, new Color(0.90f, 0.95f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var arenaTrimMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("ArenaTrim", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), Vector2.one, new Color(0.10f, 0.75f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, new Color(0.10f, 0.85f, 1f, 1f), 0.40f));
            var arenaHazardMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("ArenaHazard", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(HazardTexturePath), Vector2.one, new Color(1.00f, 0.72f, 0.12f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, Color.clear, 0f));
            var arenaGlowMaterial = GetOrCreateRetroMaterial(new MaterialSpecification("ArenaGlow", "RocketFooxball/RetroToonLit", AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), Vector2.one, new Color(0.10f, 0.85f, 1.00f, 1f), shadow, ambient, 0.55f, rim, 3f, 0.12f, new Color(0.10f, 0.95f, 0.88f, 1f), 0.65f));
            var shieldBlueMaterial = GetOrCreateShieldMaterial("ShieldBlue", new Color(0.10f, 0.50f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
            var shieldRedMaterial = GetOrCreateShieldMaterial("ShieldRed", new Color(1.00f, 0.22f, 0.20f, 1f), new Color(1.00f, 0.55f, 0.45f, 1f));

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

            RegisterBuildScene();
            Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
            SetProjectFixedTimestep();

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

            var arena = BuildArena(floorMaterial, wallMaterial, markingMaterial, frameMaterial, shieldMaterial, ballSurface, arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial, shieldBlueMaterial, shieldRedMaterial);
            var explosionObject = new GameObject("ExplosionResolver");
            var explosionResolver = explosionObject.AddComponent<ExplosionResolver>();
            SetObjectArray(explosionResolver, "goalShieldColliders", arena.Shields);
            SetObjectReference(explosionResolver, "explosionVfxPrefab", explosionPrefab);
            SetFloat(explosionResolver, "blastRadius", BlastRadius);
            SetFloat(explosionResolver, "playerImpulseStrength", 24f);
            SetFloat(explosionResolver, "ballImpulseStrength", 16f);
            SetFloat(explosionResolver, "occludedForce", 0.25f);
            SetFloat(explosionResolver, "playerUpBias", 0.18f);
            SetFloat(explosionResolver, "underfootForwardImpulseScale", 0.5625f);
            SetFloat(explosionResolver, "underfootUpwardImpulseScale", 1f);
            SetFloat(explosionResolver, "cameraFeedbackScale", 0.8f);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.SetPositionAndRotation(new Vector3(PlayerSpawnOffset, 0f, 0f), Quaternion.LookRotation(Vector3.left, Vector3.up));

            var ball = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab);
            ball.name = "Ball";
            ball.transform.SetPositionAndRotation(new Vector3(0f, BallSpawnHeight, 0f), Quaternion.identity);

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
            SetFloat(match, "goalFreezeDuration", GoalFreezeDuration);
            SetVector3(match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f));
            SetVector3(match, "playerResetPosition", new Vector3(PlayerSpawnOffset, 0f, 0f));
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
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateMovementLabInternal(false, builderSignature, false);
            var generatedOutputFingerprint = ComputeGeneratedOutputFingerprint();
            WriteBuildManifest(builderSignature, generatedOutputFingerprint);
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            ValidateMovementLabInternal(true, builderSignature, false);
            Debug.Log("Rocket Fooxball Movement Lab built: " + ScenePath);
        }

        /// <summary>Reopens and checks generated assets without writing project state.</summary>
        [MenuItem("Rocket Fooxball/Validate Movement Lab")]
        public static void ValidateMovementLab()
        {
            ValidateMovementLabInternal(true, ComputeBuilderSignature(), true);
        }

        private static void ValidateMovementLabInternal(bool includeManifest, string builderSignature, bool logSuccess)
        {
            if (includeManifest)
            {
                ValidateManifestAndFingerprint(builderSignature);
            }

            EnsureAssetExists(PrefabPath);
            EnsureAssetExists(BallPrefabPath);
            EnsureAssetExists(RocketPrefabPath);
            EnsureAssetExists(RocketModelPath);
            EnsureAssetExists(ArenaKitModelPath);
            EnsureAssetExists(CharacterModelPath);
            EnsureAssetExists(FpsKickModelPath);
            EnsureAssetExists(WeaponModelPath);
            EnsureAssetExists(GrassTexturePath);
            EnsureAssetExists(BallTexturePath);
            EnsureAssetExists(WeaponMetalTexturePath);
            EnsureAssetExists(WeaponDarkTexturePath);
            EnsureAssetExists(WeaponAccentTexturePath);
            EnsureAssetExists(ExplosionTexturePath);
            EnsureAssetExists(SmokeTexturePath);
            EnsureAssetExists(ToonShaderPath);
            EnsureAssetExists(ParticleShaderPath);
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
            if (camera.clearFlags != CameraClearFlags.SolidColor || Mathf.Abs(camera.backgroundColor.r - 0.72f) > 0.001f || Mathf.Abs(camera.backgroundColor.g - 0.88f) > 0.001f || Mathf.Abs(camera.backgroundColor.b - 0.96f) > 0.001f || Mathf.Abs(camera.fieldOfView - 75f) > 0.001f || Mathf.Abs(camera.farClipPlane - 180f) > 0.01f) throw new InvalidOperationException("Gameplay camera bright-scene contract invalid.");
            if (RenderSettings.skybox != null || RenderSettings.ambientMode != UnityEngine.Rendering.AmbientMode.Trilight || !RenderSettings.fog || Mathf.Abs(RenderSettings.fogStartDistance - 75f) > 0.01f || Mathf.Abs(RenderSettings.fogEndDistance - 170f) > 0.01f) throw new InvalidOperationException("Scene environment contract invalid.");
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
            ValidateReference(launcher, "input", input, "RocketLauncher.input");
            ValidateReference(launcher, "look", look, "RocketLauncher.look");
            ValidateReference(launcher, "aimCamera", camera, "RocketLauncher.aimCamera");
            ValidateReference(launcher, "spawnPoint", player.transform.Find("Head/Camera/RocketMuzzle"), "RocketLauncher.spawnPoint");
            ValidateReference(launcher, "projectilePrefab", AssetDatabase.LoadAssetAtPath<RocketProjectile>(RocketPrefabPath), "RocketLauncher.projectilePrefab");
            ValidateReference(launcher, "explosionResolver", resolver, "RocketLauncher.explosionResolver");
            ValidateReference(cameraFeedback, "player", playerMotor, "PlayerCameraFeedback.player");
            ValidateReference(cameraFeedback, "targetCamera", camera, "PlayerCameraFeedback.targetCamera");
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
            ValidateImportedVisual(worldVisual.gameObject, CharacterModelPath, "WorldVisual");
            ValidateImportedVisual(weaponVisual.gameObject, WeaponModelPath, "WeaponVisual");
            ValidateWeaponMaterials(weaponVisual.gameObject);
            ValidateImportedVisual(fpsVisual.gameObject, FpsKickModelPath, "FpsKickVisual");
            ValidateNoPhysics(weaponVisual.gameObject, "WeaponVisual");
            ValidateNoPhysics(fpsVisual.gameObject, "FpsKickVisual");
            ValidateWorldAnimatorController(worldAnimator, WorldControllerPath, CharacterModelPath);
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
            ValidateSerializedFloat(match, "goalFreezeDuration", GoalFreezeDuration, "MatchController.goalFreezeDuration");
            ValidateSerializedVector3(match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f), "MatchController.ballResetPosition");
            ValidateSerializedVector3(match, "playerResetPosition", new Vector3(PlayerSpawnOffset, 0f, 0f), "MatchController.playerResetPosition");
            ValidateReference(hud, "player", playerMotor, "HUD.player");
            ValidateReference(hud, "ball", ballMotor, "HUD.ball");
            ValidateReference(hud, "launcher", launcher, "HUD.launcher");
            ValidateReference(hud, "kick", kick, "HUD.kick");
            ValidateReference(hud, "match", match, "HUD.match");

            ValidatePrefab(PrefabPath, "Player", false, ballSurface);
            ValidatePrefab(BallPrefabPath, "Ball", true, ballSurface);
            ValidatePrefab(RocketPrefabPath, "Rocket", false, null);
            ValidateArenaMaterials(arena, ballSurface);
            ValidateArenaArchitecture(arena);
            ValidateTextureImporterContracts();
            ValidateModelImporterContracts();
            ValidateRenderPipelineSettings();
            ValidatePhysicsAndBuildSettings();
            ValidateNoMissingComponents(scene);

            if (logSuccess)
            {
                Debug.Log("Rocket Fooxball Movement Lab validation succeeded: " + ScenePath);
            }
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
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.72f, 0.88f, 0.96f, 1f);
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
            var weaponMetal = GetOrCreateRetroMaterial("WeaponMetal", WeaponMetalBaseColor, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponMetalTexturePath), Vector2.one);
            var weaponDark = GetOrCreateRetroMaterial("WeaponDark", WeaponDarkBaseColor, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponDarkTexturePath), Vector2.one);
            var weaponAccent = GetOrCreateRetroMaterial("WeaponAccent", WeaponAccentBaseColor, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponAccentTexturePath), Vector2.one);
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
            SetFloat(motor, "jumpVelocity", 6.75f);
            SetInteger(motor, "jumpsToHardCap", 4);
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

        private static GameObject BuildBallPrefab(Material ballMaterial, PhysicsMaterial ballSurface)
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

            var glowObject = new GameObject("GlowLight");
            glowObject.transform.SetParent(root.transform, false);
            glowObject.transform.localPosition = new Vector3(0f, 0f, 0.18f);
            var glowLight = glowObject.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = RocketLightColor;
            glowLight.intensity = RocketLightIntensity;
            glowLight.range = RocketLightRange;
            glowLight.shadows = LightShadows.None;

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
            smokeMain.maxParticles = 48;
            var smokeEmission = smokeSystem.emission;
            smokeEmission.rateOverTime = 0f;
            smokeEmission.rateOverDistance = RocketTrailRateOverDistance;
            var smokeSize = smokeSystem.sizeOverLifetime;
            smokeSize.enabled = true;
            var smokeCurve = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 0.52f)));
            smokeSize.size = smokeCurve;
            var smokeColor = smokeSystem.colorOverLifetime;
            smokeColor.enabled = true;
            var smokeGradient = new Gradient();
            smokeGradient.SetKeys(new[] { new GradientColorKey(RocketTrailStartColor, 0f), new GradientColorKey(RocketTrailEndColor, 1f) }, new[] { new GradientAlphaKey(0.82f, 0f), new GradientAlphaKey(0f, 1f) });
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
            // Keep blast readability aligned with the 30% gameplay radius increase.
            root.transform.localScale = Vector3.one * BlastVisualScale;
            var explosionMaterial = GetOrCreateParticleMaterial("Explosion", ExplosionFireMaterialColor, AssetDatabase.LoadAssetAtPath<Texture2D>(ExplosionTexturePath));
            var smokeMaterial = GetOrCreateParticleMaterial("Smoke", ExplosionSmokeMaterialColor, AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath));
            var systems = new List<ParticleSystem>();

            var flash = CreateExplosionSystem(root.transform, "Flash", explosionMaterial, 1, 0.10f, 0.10f, 2.4f, 0f, 0, 0f, 0f);
            systems.Add(flash);
            var fire = CreateExplosionSystem(root.transform, "FireChunks", explosionMaterial, 14, 0.34f, 0.52f, 1.10f, 5f, 14, 3f, 7f);
            var fireColor = fire.colorOverLifetime;
            fireColor.enabled = true;
            var fireGradient = new Gradient();
            fireGradient.SetKeys(new[] { new GradientColorKey(new Color(1.00f, 0.92f, 0.42f, 1f), 0f), new GradientColorKey(new Color(1.00f, 0.16f, 0.02f, 1f), 0.72f), new GradientColorKey(new Color(0.52f, 0.03f, 0.01f, 1f), 1f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.92f, 0.62f), new GradientAlphaKey(0f, 1f) });
            fireColor.color = fireGradient;
            systems.Add(fire);
            var sparks = CreateExplosionSystem(root.transform, "Sparks", explosionMaterial, 16, 0.265f, 0.35f, 0.08f, 10f, 16, 7f, 13f);
            systems.Add(sparks);
            var smoke = CreateExplosionSystem(root.transform, "Smoke", smokeMaterial, 6, 0.68f, 0.90f, 0.82f, 1f, 8, 0.5f, 2f);
            var smokeSize = smoke.sizeOverLifetime;
            smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.48f), new Keyframe(1f, 1.55f)));
            var smokeColor = smoke.colorOverLifetime;
            smokeColor.enabled = true;
            var smokeGradient = new Gradient();
            smokeGradient.SetKeys(new[] { new GradientColorKey(new Color(0.34f, 0.32f, 0.29f, 1f), 0f), new GradientColorKey(new Color(0.16f, 0.15f, 0.14f, 1f), 1f) }, new[] { new GradientAlphaKey(0.44f, 0f), new GradientAlphaKey(0f, 1f) });
            smokeColor.color = smokeGradient;
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
            var sheet = system.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f)));
            return system;
        }

        private static ArenaBuild BuildArena(Material floorMaterial, Material wallMaterial, Material markingMaterial, Material frameMaterial, Material shieldMaterial, PhysicsMaterial ballSurface, Material arenaPrimaryMaterial, Material arenaTrimMaterial, Material arenaHazardMaterial, Material arenaGlowMaterial, Material northShieldMaterial, Material southShieldMaterial)
        {
            var arena = new GameObject("Arena");
            CreateSolid("Floor", arena.transform, new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 90f), floorMaterial, ballSurface);
            // Longest arena axis runs along X. Goals occupy opposite X ends;
            // north/south walls therefore remain solid while end walls split
            // around each goal opening.
            CreateSolid("NorthWall", arena.transform, new Vector3(0f, 4f, -44.5f), new Vector3(130f, 8f, 1f), wallMaterial, ballSurface);
            CreateSolid("SouthWall", arena.transform, new Vector3(0f, 4f, 44.5f), new Vector3(130f, 8f, 1f), wallMaterial, ballSurface);
            const float endWallSegmentSpan = 26.5f;
            CreateSolid("WestWallNorth", arena.transform, new Vector3(-64.5f, 4f, -31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
            CreateSolid("WestWallSouth", arena.transform, new Vector3(-64.5f, 4f, 31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
            CreateSolid("EastWallNorth", arena.transform, new Vector3(64.5f, 4f, -31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);
            CreateSolid("EastWallSouth", arena.transform, new Vector3(64.5f, 4f, 31.75f), new Vector3(1f, 8f, endWallSegmentSpan), wallMaterial, ballSurface);

            // Each ramp rises from midfield toward its nearest X-axis goal.
            CreateSolid("RampWest", arena.transform, new Vector3(-22f, 2.1f, 2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(-15f, -90f, 0f));
            CreateSolid("RampEast", arena.transform, new Vector3(22f, 2.1f, -2f), new Vector3(18f, 0.5f, 20f), wallMaterial, ballSurface, Quaternion.Euler(-15f, 90f, 0f));

            var markings = new GameObject("Markings").transform;
            markings.SetParent(arena.transform, false);
            CreateMarking("CenterLine", markings, Vector3.zero, new Vector3(126f, 0.02f, 0.25f), markingMaterial);
            CreateMarking("WestBox", markings, new Vector3(-29f, 0.015f, 0f), new Vector3(0.25f, 0.02f, 36f), markingMaterial);
            CreateMarking("EastBox", markings, new Vector3(29f, 0.015f, 0f), new Vector3(0.25f, 0.02f, 36f), markingMaterial);
            CreateMarking("CenterSpot", markings, new Vector3(0f, 0.015f, 0f), new Vector3(1f, 0.02f, 1f), markingMaterial);

            var north = BuildGoal("NorthGoal", GoalTrigger.GoalSide.North, new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), frameMaterial, northShieldMaterial, wallMaterial, ballSurface);
            var south = BuildGoal("SouthGoal", GoalTrigger.GoalSide.South, new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), frameMaterial, southShieldMaterial, wallMaterial, ballSurface);
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
            CreateContainment("WestGoalOpeningContainment", containment, new Vector3(-67f, 3.5f, 0f), new Vector3(1f, 8f, 38f), ballSurface);
            CreateContainment("EastGoalOpeningContainment", containment, new Vector3(67f, 3.5f, 0f), new Vector3(1f, 8f, 38f), ballSurface);

            BuildArenaArchitecture(arena.transform, north, south, new[] { arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial });

            return new ArenaBuild
            {
                Root = arena,
                NorthGoal = north,
                SouthGoal = south,
                Shields = new[] { north.Shield, south.Shield }
            };
        }

        private static GoalBuild BuildGoal(string name, GoalTrigger.GoalSide side, Vector3 position, Quaternion rotation, Material frameMaterial, Material shieldMaterial, Material wallMaterial, PhysicsMaterial ballSurface)
        {
            var root = new GameObject(name);
            root.transform.SetPositionAndRotation(position, rotation);
            var triggerCollider = root.AddComponent<BoxCollider>();
            var trigger = root.AddComponent<GoalTrigger>();
            triggerCollider.isTrigger = true;
            triggerCollider.center = new Vector3(0f, 3.5f, 0f);
            triggerCollider.size = new Vector3(36f, 7f, 0.5f);
            SetEnum(trigger, "goalSide", side == GoalTrigger.GoalSide.North ? "North" : "South");
            SetVector3(trigger, "planeNormal", Vector3.right);
            SetFloat(trigger, "openingHalfWidth", 18f);
            SetFloat(trigger, "openingMinHeight", 0f);
            SetFloat(trigger, "openingMaxHeight", 7f);
            SetFloat(trigger, "rearmDistance", 0.5f);
            SetObjectReference(trigger, "planeReference", root.transform);
            SetObjectReference(trigger, "openingTrigger", triggerCollider);

            var shield = new GameObject("ShieldCollider");
            shield.transform.SetParent(root.transform, false);
            shield.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            var shieldCollider = shield.AddComponent<BoxCollider>();
            shieldCollider.size = new Vector3(36f, 7f, 0.4f);
            shieldCollider.sharedMaterial = ballSurface;
            var shieldVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shieldVisual.name = "ShieldVisual";
            shieldVisual.transform.SetParent(root.transform, false);
            shieldVisual.transform.localPosition = new Vector3(0f, 3.5f, -0.22f);
            shieldVisual.transform.localScale = new Vector3(36f, 7f, 1f);
            UnityEngine.Object.DestroyImmediate(shieldVisual.GetComponent<Collider>());
            shieldVisual.GetComponent<Renderer>().sharedMaterial = shieldMaterial;
            var frameWest = CreateSolid("FrameWest", root.transform, new Vector3(-18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
            var frameEast = CreateSolid("FrameEast", root.transform, new Vector3(18.5f, 3.5f, 0f), new Vector3(1f, 7f, 1f), frameMaterial, ballSurface);
            var frameTop = CreateSolid("FrameTop", root.transform, new Vector3(0f, 7.5f, 0f), new Vector3(38f, 1f, 1f), frameMaterial, ballSurface);
            frameWest.GetComponent<Renderer>().enabled = false;
            frameEast.GetComponent<Renderer>().enabled = false;
            frameTop.GetComponent<Renderer>().enabled = false;
            // Local +Z points outward for both rotated goal roots.
            CreateSolid("RecessWest", root.transform, new Vector3(-18.5f, 3.5f, 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
            CreateSolid("RecessEast", root.transform, new Vector3(18.5f, 3.5f, 4.5f), new Vector3(1f, 7f, 9f), wallMaterial, ballSurface);
            CreateSolid("RecessFloor", root.transform, new Vector3(0f, -0.25f, 4.5f), new Vector3(37f, 0.5f, 9f), wallMaterial, ballSurface);
            var recessBack = CreateSolid("RecessBack", root.transform, new Vector3(0f, 3.5f, 9f), new Vector3(37f, 7f, 1f), wallMaterial, ballSurface);
            var recesses = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < recesses.Length; i++)
            {
                if (recesses[i].gameObject.name.StartsWith("Recess", StringComparison.Ordinal)) recesses[i].enabled = false;
            }
            return new GoalBuild { Root = root, Trigger = trigger, Shield = shieldCollider };
        }

        private static void BuildArenaArchitecture(Transform arenaRoot, GoalBuild northGoal, GoalBuild southGoal, Material[] arenaMaterials)
        {
            var architecture = new GameObject("Architecture").transform;
            architecture.SetParent(arenaRoot, false);

            var northShell = CreateArenaKitVisual(architecture, "NorthGoalShell", "ArenaGoalShell", new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f), arenaMaterials);
            var southShell = CreateArenaKitVisual(architecture, "SouthGoalShell", "ArenaGoalShell", new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), arenaMaterials);
            northShell.transform.localScale = Vector3.one;
            southShell.transform.localScale = Vector3.one;

            // A symmetric nine-per-wall truss cadence keeps the complete
            // loaded scene inside the strict MeshRenderer budget.
            for (var x = -48f; x <= 48f; x += 12f)
            {
                CreateArenaKitVisual(architecture, "NorthTruss_" + x.ToString("0"), "ArenaPerimeterTruss", new Vector3(x, 9.0f, -45.0f), Quaternion.identity, arenaMaterials);
                CreateArenaKitVisual(architecture, "SouthTruss_" + x.ToString("0"), "ArenaPerimeterTruss", new Vector3(x, 9.0f, 45.0f), Quaternion.identity, arenaMaterials);
            }

            CreateArenaKitVisual(architecture, "NorthScoreboard", "ArenaScoreboard", new Vector3(-64f, 12f, -2.5f), Quaternion.Euler(0f, -90f, 0f), arenaMaterials);
            CreateArenaKitVisual(architecture, "SouthScoreboard", "ArenaScoreboard", new Vector3(64f, 12f, 2.5f), Quaternion.Euler(0f, 90f, 0f), arenaMaterials);
        }

        private static GameObject CreateArenaKitVisual(Transform parent, string name, string meshName, Vector3 localPosition, Quaternion localRotation, Material[] arenaMaterials)
        {
            var mesh = FindArenaKitMesh(meshName);
            var visual = new GameObject(name);
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = localRotation;
            visual.transform.localScale = Vector3.one;
            var filter = visual.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = ResolveArenaKitMaterials(meshName, arenaMaterials);
            visual.isStatic = true;
            return visual;
        }

        private static Mesh FindArenaKitMesh(string meshName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
            for (var i = 0; i < assets.Length; i++)
            {
                var mesh = assets[i] as Mesh;
                if (mesh != null && string.Equals(mesh.name, meshName + "Mesh", StringComparison.Ordinal)) return mesh;
                if (mesh != null && string.Equals(mesh.name, meshName, StringComparison.Ordinal)) return mesh;
            }
            var names = new List<string>();
            for (var i = 0; i < assets.Length; i++) if (assets[i] != null) names.Add(assets[i].name + "[" + assets[i].GetType().Name + "]");
            throw new InvalidOperationException("Missing ArenaKit mesh subasset: " + meshName + "; imported assets=" + string.Join(",", names.ToArray()));
        }

        private static Material[] ResolveArenaKitMaterials(string meshName, Material[] allMaterials)
        {
            if (allMaterials == null || allMaterials.Length != 4) throw new InvalidOperationException("ArenaKit material palette is incomplete.");
            if (meshName == "ArenaWallPylon" || meshName == "ArenaScoreboard") return new[] { allMaterials[0], allMaterials[1], allMaterials[3] };
            if (meshName == "ArenaPerimeterTruss") return new[] { allMaterials[0], allMaterials[1] };
            return new[] { allMaterials[0], allMaterials[1], allMaterials[2], allMaterials[3] };
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
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.88f, 1.00f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.52f, 0.68f, 0.82f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.38f, 0.48f, 1f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.72f, 0.88f, 0.96f, 1f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 75f;
            RenderSettings.fogEndDistance = 170f;
            var light = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                return;
            }
            light.type = LightType.Directional;
            light.color = new Color(1.00f, 0.96f, 0.90f, 1f);
            light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            light.shadows = LightShadows.None;
        }

        private static bool TryReuseGeneratedState(string builderSignature)
        {
            try
            {
                ValidateManifestAndFingerprint(builderSignature);
                var active = SceneManager.GetActiveScene();
                if (active.path != ScenePath)
                {
                    active = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
                if (!active.IsValid())
                {
                    return RejectGeneratedReuse("generated scene is invalid");
                }
                if (GameObject.Find(GetBuildMarkerName(builderSignature)) == null)
                {
                    return RejectGeneratedReuse("scene marker is missing or stale");
                }

                // Full validation is intentionally read-only. A successful
                // result permits the caller to return before every write path.
                ValidateMovementLabInternal(true, builderSignature, false);
                Debug.Log("Rocket Fooxball Movement Lab reused generated state: " + ScenePath);
                return true;
            }
            catch (Exception exception)
            {
                return RejectGeneratedReuse(exception.Message);
            }
        }

        private static bool RejectGeneratedReuse(string reason)
        {
            var detail = string.IsNullOrEmpty(reason) ? "unknown stale state" : reason;
            Debug.Log("Rocket Fooxball Movement Lab generated state stale; rebuilding: " + detail);
            return false;
        }

        private static void ValidateManifestAndFingerprint(string builderSignature)
        {
            var projectRoot = ResolveProjectRoot();
            var manifestAbsolutePath = GetAbsoluteProjectPath(projectRoot, ManifestPath);
            if (!File.Exists(manifestAbsolutePath))
            {
                throw new InvalidOperationException("build manifest is missing: " + ManifestPath);
            }

            MovementLabBuildManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<MovementLabBuildManifest>(File.ReadAllText(manifestAbsolutePath));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("build manifest could not be parsed: " + exception.Message);
            }

            if (manifest == null)
            {
                throw new InvalidOperationException("build manifest is empty");
            }
            if (manifest.schemaVersion != ManifestSchemaVersion)
            {
                throw new InvalidOperationException("build manifest schema is stale");
            }
            if (!string.Equals(manifest.sourceSignature, builderSignature, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("build manifest source signature is stale");
            }
            if (!string.Equals(manifest.unityVersion, Application.unityVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("build manifest Unity version is stale");
            }
            if (manifest.fingerprintPaths == null || manifest.fingerprintPaths.Length != GeneratedFingerprintPaths.Length)
            {
                throw new InvalidOperationException("build manifest fingerprint path list is stale");
            }
            for (var i = 0; i < GeneratedFingerprintPaths.Length; i++)
            {
                var expectedPath = GeneratedFingerprintPaths[i];
                var manifestPath = manifest.fingerprintPaths[i];
                if (!string.Equals(manifestPath, expectedPath, StringComparison.Ordinal) ||
                    !string.Equals(NormalizeRepositoryRelativePath(manifestPath), manifestPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("build manifest fingerprint path list is stale");
                }
            }

            var actualFingerprint = ComputeGeneratedOutputFingerprint();
            if (!string.Equals(manifest.generatedOutputFingerprint, actualFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("generated output fingerprint is stale");
            }
        }

        private static string ComputeGeneratedOutputFingerprint()
        {
            var projectRoot = ResolveProjectRoot();
            using (var sha = SHA256.Create())
            {
                for (var i = 0; i < GeneratedFingerprintPaths.Length; i++)
                {
                    var relativePath = NormalizeRepositoryRelativePath(GeneratedFingerprintPaths[i]);
                    var absolutePath = GetAbsoluteProjectPath(projectRoot, relativePath);
                    if (!File.Exists(absolutePath))
                    {
                        throw new InvalidOperationException("Missing generated fingerprint file: " + relativePath);
                    }

                    var pathBytes = System.Text.Encoding.UTF8.GetBytes(relativePath + "\n");
                    sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);
                    var bytes = File.ReadAllBytes(absolutePath);
                    sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    var separator = new byte[] { 0 };
                    sha.TransformBlock(separator, 0, separator.Length, separator, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void WriteBuildManifest(string builderSignature, string generatedOutputFingerprint)
        {
            var projectRoot = ResolveProjectRoot();
            var manifestAbsolutePath = GetAbsoluteProjectPath(projectRoot, ManifestPath);
            var manifestDirectory = Path.GetDirectoryName(manifestAbsolutePath);
            if (string.IsNullOrEmpty(manifestDirectory))
            {
                throw new InvalidOperationException("Unable to resolve build manifest directory.");
            }
            Directory.CreateDirectory(manifestDirectory);

            var manifest = new MovementLabBuildManifest
            {
                schemaVersion = ManifestSchemaVersion,
                sourceSignature = builderSignature,
                generatedOutputFingerprint = generatedOutputFingerprint,
                unityVersion = Application.unityVersion,
                fingerprintPaths = (string[])GeneratedFingerprintPaths.Clone()
            };
            var json = JsonUtility.ToJson(manifest, true);
            var bytes = new System.Text.UTF8Encoding(false).GetBytes(json + "\n");
            var temporaryPath = manifestAbsolutePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(manifestAbsolutePath))
                {
                    File.Replace(temporaryPath, manifestAbsolutePath, null);
                }
                else
                {
                    File.Move(temporaryPath, manifestAbsolutePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static DirectoryInfo ResolveProjectRoot()
        {
            var projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
            {
                throw new InvalidOperationException("Unable to resolve Unity project root.");
            }
            return projectRoot;
        }

        private static string GetAbsoluteProjectPath(DirectoryInfo projectRoot, string repositoryRelativePath)
        {
            var normalizedPath = NormalizeRepositoryRelativePath(repositoryRelativePath);
            return Path.Combine(projectRoot.FullName, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string NormalizeRepositoryRelativePath(string repositoryRelativePath)
        {
            if (string.IsNullOrEmpty(repositoryRelativePath) || Path.IsPathRooted(repositoryRelativePath) || repositoryRelativePath.IndexOf(':') >= 0)
            {
                throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
            }

            var normalizedPath = repositoryRelativePath.Replace('\\', '/');
            var segments = normalizedPath.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                {
                    throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
                }
            }
            if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) && !normalizedPath.StartsWith("ProjectSettings/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsafe generated fingerprint path: " + repositoryRelativePath);
            }
            return normalizedPath;
        }

        private static string ComputeBuilderSignature()
        {
            var projectRoot = ResolveProjectRoot();

            var sourcePaths = new[]
            {
                BuilderSourcePath,
                ToonShaderPath,
                ParticleShaderPath,
                ShieldShaderPath,
                "Assets/_Game/Scripts/Runtime/ExplosionVfx.cs",
                "Assets/_Game/Scripts/Runtime/RocketTrailVfx.cs",
                "Assets/_Game/Scripts/Runtime/PlayerMotor.cs",
                "Assets/_Game/Scripts/Runtime/PlayerPresentation.cs",
                "Assets/_Game/Scripts/Runtime/BallKick.cs",
                "Assets/_Game/Scripts/Runtime/BallMotor.cs",
                "Assets/_Game/Scripts/Runtime/ExplosionResolver.cs",
                "Assets/_Game/Scripts/Runtime/RocketProjectile.cs",
                RocketLauncherSourcePath,
                "Tools/Blender/generate_retro_textures.py",
                "Tools/Blender/generate_arena_kit.py",
                "Tools/Blender/generate_low_poly_character.py",
                "Tools/Blender/generate_fps_kick_rig.py",
                "Tools/Blender/generate_fps_rocket_launcher.py",
                RocketGeneratorSourcePath
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
                var generatedSourcePaths = new[]
                {
                    ArenaKitModelPath, RocketModelPath, GrassTexturePath, WallTexturePath,
                    TrimTexturePath, HazardTexturePath, ShieldTexturePath, BallTexturePath,
                    WeaponMetalTexturePath, WeaponDarkTexturePath, WeaponAccentTexturePath,
                    ExplosionTexturePath, SmokeTexturePath
                };
                Array.Sort(generatedSourcePaths, StringComparer.Ordinal);
                for (var i = 0; i < generatedSourcePaths.Length; i++)
                {
                    var relativePath = generatedSourcePaths[i].Replace('\\', '/');
                    var absolutePath = Path.Combine(projectRoot.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(absolutePath)) throw new InvalidOperationException("Missing generated signature source: " + relativePath);
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
            return GetOrCreateRetroMaterial(new MaterialSpecification(name, "RocketFooxball/RetroToonLit", texture, textureScale,
                color, new Color(0.32f, 0.44f, 0.62f, 1f), new Color(0.72f, 0.86f, 1f, 1f), 0.55f,
                new Color(0.20f, 0.86f, 0.92f, 1f), 3f, 0.12f, Color.clear, 0f));
        }

        private static Material GetOrCreateRetroMaterial(MaterialSpecification specification)
        {
            var path = MaterialsPath + "/" + specification.Name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find(specification.ShaderName);
            if (shader == null) throw new InvalidOperationException("Missing material shader: " + specification.ShaderName);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", specification.BaseColor);
            material.SetColor("_ShadowColor", specification.ShadowColor);
            material.SetFloat("_LightSteps", 3f);
            material.SetColor("_AmbientColor", specification.AmbientColor);
            material.SetFloat("_AmbientStrength", specification.AmbientStrength);
            material.SetColor("_RimColor", specification.RimColor);
            material.SetFloat("_RimPower", specification.RimPower);
            material.SetFloat("_RimStrength", specification.RimStrength);
            material.SetColor("_EmissionColor", specification.EmissionColor);
            material.SetFloat("_EmissionStrength", specification.EmissionStrength);
            material.SetTexture("_BaseMap", specification.Texture != null ? specification.Texture : Texture2D.whiteTexture);
            material.SetTextureScale("_BaseMap", specification.TextureScale);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateShieldMaterial(string name, Color baseColor, Color emissionColor)
        {
            var path = MaterialsPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("RocketFooxball/RetroShield");
            if (shader == null) throw new InvalidOperationException("RocketFooxball/RetroShield shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ShieldTexturePath));
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_EmissionColor", emissionColor);
            material.SetFloat("_PulseSpeed", 1.2f);
            material.SetFloat("_ScanScale", 3f);
            material.SetFloat("_Alpha", 0.52f);
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
            ConfigureTextureImporter(WallTexturePath, true);
            ConfigureTextureImporter(TrimTexturePath, true);
            ConfigureTextureImporter(HazardTexturePath, true);
            ConfigureTextureImporter(BallTexturePath, true);
            ConfigureTextureImporter(WeaponMetalTexturePath, true);
            ConfigureTextureImporter(WeaponDarkTexturePath, true);
            ConfigureTextureImporter(WeaponAccentTexturePath, true);
            ConfigureTextureImporter(ShieldTexturePath, false);
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
            var authoredWidth = importer.maxTextureSize;
            if (path == GrassTexturePath || path == WallTexturePath || path == TrimTexturePath || path == HazardTexturePath || path == ShieldTexturePath || path == ExplosionTexturePath || path == SmokeTexturePath) authoredWidth = 128;
            if (path == BallTexturePath) authoredWidth = 256;
            if (path == WeaponMetalTexturePath || path == WeaponDarkTexturePath || path == WeaponAccentTexturePath) authoredWidth = 256;
            if (importer.maxTextureSize != authoredWidth) { importer.maxTextureSize = authoredWidth; changed = true; }
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (!settings.ignoreMipmapLimit) { settings.ignoreMipmapLimit = true; importer.SetTextureSettings(settings); changed = true; }
            var platform = importer.GetDefaultPlatformTextureSettings();
            if (platform.maxTextureSize != authoredWidth || platform.overridden)
            {
                platform.maxTextureSize = authoredWidth;
                platform.overridden = false;
                importer.SetPlatformTextureSettings(platform);
                changed = true;
            }
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
            ConfigureStaticModelImporter(RocketModelPath);
            ConfigureStaticModelImporter(ArenaKitModelPath);
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

            var expectedNames = character ? new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" } : new[] { "Idle", "Kick" };
            var expectedLoops = character ? new[] { true, true, false, false, false, false } : new[] { true, false };
            var expectedStarts = character ? new[] { 1f, 1f, 1f, 1f, 1f, 1f } : new[] { 1f, 1f };
            var expectedEnds = character ? new[] { 30f, 20f, 12f, 15f, 10f, 12f } : new[] { 31f, 11f };
            var clips = new List<ModelImporterClipAnimation>();
            for (var expectedIndex = 0; expectedIndex < expectedNames.Length; expectedIndex++)
            {
                ModelImporterClipAnimation source = null;
                for (var sourceIndex = 0; sourceIndex < sourceClips.Length; sourceIndex++)
                {
                    var sourceName = sourceClips[sourceIndex].name ?? string.Empty;
                    if (string.Equals(sourceName, expectedNames[expectedIndex], StringComparison.OrdinalIgnoreCase) ||
                        (sourceName.EndsWith(expectedNames[expectedIndex], StringComparison.OrdinalIgnoreCase) && sourceName.Length > expectedNames[expectedIndex].Length && !char.IsLetterOrDigit(sourceName[sourceName.Length - expectedNames[expectedIndex].Length - 1])))
                    {
                        source = sourceClips[sourceIndex];
                        break;
                    }
                }
                if (source == null)
                {
                    source = new ModelImporterClipAnimation();
                }
                source.name = expectedNames[expectedIndex];
                source.takeName = expectedNames[expectedIndex];
                source.firstFrame = expectedStarts[expectedIndex];
                source.lastFrame = expectedEnds[expectedIndex];
                source.loopTime = expectedLoops[expectedIndex];
                source.lockRootRotation = true;
                source.keepOriginalOrientation = true;
                source.lockRootHeightY = true;
                source.keepOriginalPositionY = true;
                source.lockRootPositionXZ = true;
                source.keepOriginalPositionXZ = true;
                source.heightFromFeet = false;
                source.hasAdditiveReferencePose = false;
                clips.Add(source);
            }
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
                if (a[i].name != b[i].name || Mathf.Abs(a[i].firstFrame - b[i].firstFrame) > 0.001f || Mathf.Abs(a[i].lastFrame - b[i].lastFrame) > 0.001f || a[i].loopTime != b[i].loopTime || a[i].lockRootRotation != b[i].lockRootRotation || a[i].lockRootHeightY != b[i].lockRootHeightY || a[i].lockRootPositionXZ != b[i].lockRootPositionXZ)
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
                    else if (slotName.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                    else if (slotName.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 1) chosen = materials[1];
                    else if (slotName.IndexOf("Cream", StringComparison.OrdinalIgnoreCase) >= 0 && materials.Length > 2) chosen = materials[2];
                    else if (slotName.IndexOf("Armor", StringComparison.OrdinalIgnoreCase) >= 0 || slotName.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0) chosen = materials.Length > 0 ? materials[0] : null;
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

        private static RuntimeAnimatorController EnsureWorldAnimatorController(string path, string modelPath)
        {
            var clipNames = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
            var clips = new AnimationClip[clipNames.Length];
            for (var i = 0; i < clipNames.Length; i++)
            {
                clips[i] = FindImportedClip(modelPath, clipNames[i]);
                if (clips[i] == null) throw new InvalidOperationException("Missing imported world clip " + clipNames[i] + " for " + modelPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                RebuildWorldAnimatorController(controller, clips);
            }
            else if (!IsWorldAnimatorControllerExact(controller, clips, out _))
            {
                // Repair in place. Deleting/recreating the main asset changes
                // its GUID and leaves stale controller subassets behind.
                RebuildWorldAnimatorController(controller, clips);
            }
            return controller;
        }

        private static void RebuildWorldAnimatorController(AnimatorController controller, AnimationClip[] clips)
        {
            if (controller == null || clips == null || clips.Length != 6) throw new InvalidOperationException("World animator rebuild inputs are invalid.");

            while (controller.layers.Length > 1) controller.RemoveLayer(controller.layers.Length - 1);
            if (controller.layers.Length == 0) controller.AddLayer("Base Layer");

            var layer = controller.layers[0];
            layer.name = "Base Layer";
            var stateMachine = layer.stateMachine;
            if (stateMachine == null)
            {
                controller.RemoveLayer(0);
                controller.AddLayer("Base Layer");
                layer = controller.layers[0];
                stateMachine = layer.stateMachine;
            }
            if (stateMachine == null) throw new InvalidOperationException("World animator base state machine is unavailable.");

            var anyTransitions = stateMachine.anyStateTransitions;
            for (var i = 0; i < anyTransitions.Length; i++) stateMachine.RemoveAnyStateTransition(anyTransitions[i]);
            var existingStates = stateMachine.states;
            for (var i = 0; i < existingStates.Length; i++)
            {
                var transitions = existingStates[i].state != null ? existingStates[i].state.transitions : Array.Empty<AnimatorStateTransition>();
                for (var j = 0; j < transitions.Length; j++) existingStates[i].state.RemoveTransition(transitions[j]);
                if (existingStates[i].state != null) stateMachine.RemoveState(existingStates[i].state);
            }
            var childStateMachines = stateMachine.stateMachines;
            for (var i = 0; i < childStateMachines.Length; i++) stateMachine.RemoveStateMachine(childStateMachines[i].stateMachine);

            while (controller.parameters.Length > 0) controller.RemoveParameter(0);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);

            var idle = stateMachine.AddState("Idle");
            var run = stateMachine.AddState("Run");
            var jump = stateMachine.AddState("Jump");
            var fall = stateMachine.AddState("Fall");
            var land = stateMachine.AddState("Land");
            var kick = stateMachine.AddState("Kick");
            stateMachine.defaultState = idle;
            idle.motion = clips[0];
            run.motion = clips[1];
            jump.motion = clips[2];
            fall.motion = clips[3];
            land.motion = clips[4];
            kick.motion = clips[5];

            AddAnimatorConditionTransition(idle, run, AnimatorConditionMode.Greater, 0.30f, "Speed");
            AddAnimatorConditionTransition(run, idle, AnimatorConditionMode.Less, 0.20f, "Speed");
            AddAirTransitions(idle, jump, fall);
            AddAirTransitions(run, jump, fall);
            AddAirTransitions(land, jump, fall);
            AddAnimatorConditionTransition(jump, fall, AnimatorConditionMode.Less, 0f, "VerticalSpeed");
            AddAnimatorConditionTransition(jump, land, AnimatorConditionMode.If, 0f, "Grounded");
            var fallToJump = AddAnimatorConditionTransition(fall, jump, AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed");
            fallToJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            var fallToLand = AddAnimatorConditionTransition(fall, land, AnimatorConditionMode.If, 0f, "Grounded");
            var landToIdle = AddAnimatorConditionTransition(land, idle, AnimatorConditionMode.Less, 0.20f, "Speed");
            landToIdle.hasExitTime = true;
            landToIdle.exitTime = 0.65f;
            var landToRun = AddAnimatorConditionTransition(land, run, AnimatorConditionMode.Greater, 0.20f, "Speed");
            landToRun.hasExitTime = true;
            landToRun.exitTime = 0.65f;
            var anyKick = stateMachine.AddAnyStateTransition(kick);
            anyKick.hasExitTime = false;
            anyKick.exitTime = 0f;
            anyKick.duration = 0.02f;
            anyKick.offset = 0f;
            anyKick.canTransitionToSelf = false;
            anyKick.AddCondition(AnimatorConditionMode.If, 0f, "Kick");
            var kickToIdle = AddAnimatorConditionTransition(kick, idle, AnimatorConditionMode.If, 0f, "Grounded");
            kickToIdle.hasExitTime = true;
            kickToIdle.exitTime = 1f;
            kickToIdle.AddCondition(AnimatorConditionMode.Less, 0.20f, "Speed");
            var kickToRun = AddAnimatorConditionTransition(kick, run, AnimatorConditionMode.If, 0f, "Grounded");
            kickToRun.hasExitTime = true;
            kickToRun.exitTime = 1f;
            kickToRun.AddCondition(AnimatorConditionMode.Greater, 0.20f, "Speed");
            var kickToJump = AddAnimatorConditionTransition(kick, jump, AnimatorConditionMode.IfNot, 0f, "Grounded");
            kickToJump.hasExitTime = true;
            kickToJump.exitTime = 1f;
            kickToJump.AddCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed");
            var kickToFall = AddAnimatorConditionTransition(kick, fall, AnimatorConditionMode.IfNot, 0f, "Grounded");
            kickToFall.hasExitTime = true;
            kickToFall.exitTime = 1f;
            kickToFall.AddCondition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed");

            controller.layers[0] = layer;
            CleanupWorldAnimatorSubassets(controller, stateMachine);
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
        }

        private static void CleanupWorldAnimatorSubassets(AnimatorController controller, AnimatorStateMachine stateMachine)
        {
            var keep = new HashSet<UnityEngine.Object> { controller, stateMachine };
            var states = stateMachine.states;
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].state == null) continue;
                keep.Add(states[i].state);
                var transitions = states[i].state.transitions;
                for (var j = 0; j < transitions.Length; j++) keep.Add(transitions[j]);
            }
            var anyTransitions = stateMachine.anyStateTransitions;
            for (var i = 0; i < anyTransitions.Length; i++) keep.Add(anyTransitions[i]);

            var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            for (var i = 0; i < subassets.Length; i++)
            {
                var asset = subassets[i];
                if (asset == null || keep.Contains(asset)) continue;
                if (asset is AnimatorState || asset is AnimatorStateTransition || asset is AnimatorStateMachine)
                {
                    UnityEngine.Object.DestroyImmediate(asset, true);
                }
            }
        }

        private static AnimatorStateTransition AddAirTransitions(AnimatorState source, AnimatorState jump, AnimatorState fall)
        {
            var toJump = AddAnimatorConditionTransition(source, jump, AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed");
            toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            var toFall = AddAnimatorConditionTransition(source, fall, AnimatorConditionMode.Less, 0.05f, "VerticalSpeed");
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            return toFall;
        }

        private static AnimatorStateTransition AddAnimatorConditionTransition(AnimatorState source, AnimatorState destination, AnimatorConditionMode mode, float threshold, string parameter)
        {
            var transition = source.AddTransition(destination);
            transition.hasExitTime = false;
            transition.exitTime = 0f;
            transition.duration = 0.02f;
            transition.offset = 0f;
            transition.canTransitionToSelf = true;
            transition.conditions = Array.Empty<AnimatorCondition>();
            transition.AddCondition(mode, threshold, parameter);
            return transition;
        }

        private static void ValidateWorldAnimatorController(Animator animator, string path, string modelPath)
        {
            var controller = animator.runtimeAnimatorController as AnimatorController;
            var clipNames = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
            var clips = new AnimationClip[clipNames.Length];
            for (var i = 0; i < clipNames.Length; i++) clips[i] = FindImportedClip(modelPath, clipNames[i]);
            if (!IsWorldAnimatorControllerExact(controller, clips, out var reason))
            {
                throw new InvalidOperationException("World animator controller contract invalid: " + path + "; " + reason);
            }
        }

        private readonly struct WorldAnimatorConditionSpecification
        {
            public readonly AnimatorConditionMode Mode;
            public readonly float Threshold;
            public readonly string Parameter;

            public WorldAnimatorConditionSpecification(AnimatorConditionMode mode, float threshold, string parameter)
            {
                Mode = mode;
                Threshold = threshold;
                Parameter = parameter;
            }
        }

        private readonly struct WorldAnimatorTransitionSpecification
        {
            public readonly string Source;
            public readonly string Destination;
            public readonly bool AnyState;
            public readonly bool HasExitTime;
            public readonly float ExitTime;
            public readonly float Duration;
            public readonly bool CanTransitionToSelf;
            public readonly WorldAnimatorConditionSpecification[] Conditions;

            public WorldAnimatorTransitionSpecification(string source, string destination, bool anyState, bool hasExitTime, float exitTime, float duration, bool canTransitionToSelf, params WorldAnimatorConditionSpecification[] conditions)
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

        private static WorldAnimatorConditionSpecification WorldCondition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            return new WorldAnimatorConditionSpecification(mode, threshold, parameter);
        }

        private static WorldAnimatorTransitionSpecification[] GetWorldAnimatorTransitionSpecifications()
        {
            const float blend = 0.02f;
            return new[]
            {
                new WorldAnimatorTransitionSpecification("Idle", "Run", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.30f, "Speed")),
                new WorldAnimatorTransitionSpecification("Run", "Idle", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                new WorldAnimatorTransitionSpecification("Idle", "Jump", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Idle", "Fall", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Run", "Jump", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Run", "Fall", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Jump", "Fall", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0f, "VerticalSpeed")),
                new WorldAnimatorTransitionSpecification("Jump", "Land", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.If, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Fall", "Jump", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Fall", "Land", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.If, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Land", "Jump", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Land", "Fall", false, false, 0f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed"), WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded")),
                new WorldAnimatorTransitionSpecification("Land", "Idle", false, true, 0.65f, blend, true, WorldCondition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                new WorldAnimatorTransitionSpecification("Land", "Run", false, true, 0.65f, blend, true, WorldCondition(AnimatorConditionMode.Greater, 0.20f, "Speed")),
                new WorldAnimatorTransitionSpecification("Kick", "Idle", false, true, 1f, blend, true, WorldCondition(AnimatorConditionMode.If, 0f, "Grounded"), WorldCondition(AnimatorConditionMode.Less, 0.20f, "Speed")),
                new WorldAnimatorTransitionSpecification("Kick", "Run", false, true, 1f, blend, true, WorldCondition(AnimatorConditionMode.If, 0f, "Grounded"), WorldCondition(AnimatorConditionMode.Greater, 0.20f, "Speed")),
                new WorldAnimatorTransitionSpecification("Kick", "Jump", false, true, 1f, blend, true, WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded"), WorldCondition(AnimatorConditionMode.Greater, 0.05f, "VerticalSpeed")),
                new WorldAnimatorTransitionSpecification("Kick", "Fall", false, true, 1f, blend, true, WorldCondition(AnimatorConditionMode.IfNot, 0f, "Grounded"), WorldCondition(AnimatorConditionMode.Less, 0.05f, "VerticalSpeed")),
                new WorldAnimatorTransitionSpecification("AnyState", "Kick", true, false, 0f, blend, false, WorldCondition(AnimatorConditionMode.If, 0f, "Kick"))
            };
        }

        private static bool IsWorldAnimatorControllerExact(AnimatorController controller, AnimationClip[] clips, out string reason)
        {
            reason = null;
            if (controller == null)
            {
                reason = "controller missing";
                return false;
            }
            if (clips == null || clips.Length != 6)
            {
                reason = "clip set incomplete";
                return false;
            }
            if (controller.parameters.Length != 4)
            {
                reason = "parameter count";
                return false;
            }
            var expectedParameters = new[] { "Speed", "Grounded", "VerticalSpeed", "Kick" };
            var expectedTypes = new[] { AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Bool, AnimatorControllerParameterType.Float, AnimatorControllerParameterType.Trigger };
            for (var i = 0; i < expectedParameters.Length; i++)
            {
                var parameter = controller.parameters[i];
                if (parameter.name != expectedParameters[i] || parameter.type != expectedTypes[i] || Mathf.Abs(parameter.defaultFloat) > 0.0001f || parameter.defaultInt != 0 || parameter.defaultBool)
                {
                    reason = "parameter contract: " + expectedParameters[i];
                    return false;
                }
            }
            if (controller.layers.Length != 1 || controller.layers[0].stateMachine == null || controller.layers[0].name != "Base Layer")
            {
                reason = "base layer contract";
                return false;
            }

            var stateMachine = controller.layers[0].stateMachine;
            var expectedStates = new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" };
            if (stateMachine.states.Length != expectedStates.Length || stateMachine.stateMachines.Length != 0 || stateMachine.entryTransitions.Length != 0 || stateMachine.anyStateTransitions.Length != 1 || stateMachine.defaultState == null || stateMachine.defaultState.name != "Idle")
            {
                reason = "state machine count/default";
                return false;
            }

            var namedStates = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            var boundClips = new HashSet<AnimationClip>();
            for (var i = 0; i < stateMachine.states.Length; i++)
            {
                var state = stateMachine.states[i].state;
                if (state == null || namedStates.ContainsKey(state.name))
                {
                    reason = "state identity";
                    return false;
                }
                namedStates.Add(state.name, state);
            }
            for (var i = 0; i < expectedStates.Length; i++)
            {
                if (!namedStates.TryGetValue(expectedStates[i], out var state) || state.motion != clips[i] || state.motion == null || !boundClips.Add(state.motion as AnimationClip))
                {
                    reason = "state motion: " + expectedStates[i];
                    return false;
                }
            }

            var specifications = GetWorldAnimatorTransitionSpecifications();
            var transitionCount = stateMachine.anyStateTransitions.Length;
            for (var i = 0; i < stateMachine.states.Length; i++) transitionCount += stateMachine.states[i].state.transitions.Length;
            if (transitionCount != specifications.Length)
            {
                reason = "transition count";
                return false;
            }

            for (var i = 0; i < specifications.Length; i++)
            {
                var specification = specifications[i];
                var transitions = specification.AnyState ? stateMachine.anyStateTransitions : namedStates[specification.Source].transitions;
                AnimatorStateTransition found = null;
                for (var j = 0; j < transitions.Length; j++)
                {
                    var candidate = transitions[j];
                    if (candidate != null && candidate.destinationState != null && candidate.destinationState.name == specification.Destination && MatchesWorldAnimatorTransition(candidate, specification))
                    {
                        if (found != null)
                        {
                            reason = "duplicate transition: " + specification.Source + " -> " + specification.Destination;
                            return false;
                        }
                        found = candidate;
                    }
                }
                if (found == null)
                {
                    reason = "transition semantics: " + specification.Source + " -> " + specification.Destination;
                    return false;
                }
            }

            var subassets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            var stateCount = 0;
            var transitionSubassetCount = 0;
            for (var i = 0; i < subassets.Length; i++)
            {
                if (subassets[i] is AnimatorState) stateCount++;
                if (subassets[i] is AnimatorStateTransition) transitionSubassetCount++;
            }
            if (stateCount != 6 || transitionSubassetCount != 19)
            {
                reason = "subasset count";
                return false;
            }
            return true;
        }

        private static bool MatchesWorldAnimatorTransition(AnimatorStateTransition transition, WorldAnimatorTransitionSpecification specification)
        {
            if (transition.destinationStateMachine != null || transition.isExit || transition.hasExitTime != specification.HasExitTime || Mathf.Abs(transition.exitTime - specification.ExitTime) > 0.0001f || Mathf.Abs(transition.duration - specification.Duration) > 0.0001f || Mathf.Abs(transition.offset) > 0.0001f || transition.canTransitionToSelf != specification.CanTransitionToSelf || !transition.hasFixedDuration || transition.mute || transition.solo || transition.interruptionSource != TransitionInterruptionSource.None || !transition.orderedInterruption)
            {
                return false;
            }
            var conditions = transition.conditions;
            if (conditions == null || conditions.Length != specification.Conditions.Length)
            {
                return false;
            }
            for (var i = 0; i < conditions.Length; i++)
            {
                var expected = specification.Conditions[i];
                if (conditions[i].mode != expected.Mode || conditions[i].parameter != expected.Parameter || Mathf.Abs(conditions[i].threshold - expected.Threshold) > 0.0001f)
                {
                    return false;
                }
            }
            return true;
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
                throw new InvalidOperationException("Physics gravity does not match shared GamePhysicsSettings.");
            }
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ScenePath || !scenes[0].enabled)
            {
                throw new InvalidOperationException("MovementLab must be the sole enabled build scene.");
            }
        }

        private static void ValidateRenderPipelineSettings()
        {
            if (QualitySettings.GetQualityLevel() != 0)
            {
                throw new InvalidOperationException("MovementLab quality index must be 0.");
            }

            var qualityAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityAssets == null || qualityAssets.Length == 0)
            {
                throw new InvalidOperationException("QualitySettings asset could not be loaded.");
            }

            var qualityObject = new SerializedObject(qualityAssets[0]);
            var qualityLevels = qualityObject.FindProperty("m_QualitySettings");
            if (qualityLevels == null || !qualityLevels.isArray || qualityLevels.arraySize <= 0)
            {
                throw new InvalidOperationException("QualitySettings quality-level array is unavailable.");
            }

            var pcQuality = qualityLevels.GetArrayElementAtIndex(0);
            var customPipeline = pcQuality.FindPropertyRelative("customRenderPipeline");
            var pcPipeline = customPipeline != null ? customPipeline.objectReferenceValue as RenderPipelineAsset : null;
            if (pcPipeline == null || AssetDatabase.GetAssetPath(pcPipeline) != "Assets/Settings/PC_RPAsset.asset")
            {
                throw new InvalidOperationException("Quality index 0 must resolve Assets/Settings/PC_RPAsset.asset.");
            }

            var pipelineObject = new SerializedObject(pcPipeline);
            var renderScale = pipelineObject.FindProperty("m_RenderScale");
            var supportsHdr = pipelineObject.FindProperty("m_SupportsHDR");
            var msaa = pipelineObject.FindProperty("m_MSAA");
            var mainLightShadows = pipelineObject.FindProperty("m_MainLightShadowsSupported");
            var additionalLights = pipelineObject.FindProperty("m_AdditionalLightsRenderingMode");
            var useSrpBatcher = pipelineObject.FindProperty("m_UseSRPBatcher");
            var defaultRendererIndex = pipelineObject.FindProperty("m_DefaultRendererIndex");
            if (renderScale == null || supportsHdr == null || msaa == null || mainLightShadows == null || additionalLights == null || useSrpBatcher == null || defaultRendererIndex == null ||
                Mathf.Abs(renderScale.floatValue - 0.8f) > 0.0001f || supportsHdr.boolValue || msaa.intValue != 1 || mainLightShadows.boolValue || additionalLights.intValue != 0 || !useSrpBatcher.boolValue || defaultRendererIndex.intValue != 0)
            {
                throw new InvalidOperationException("PC URP asset quality contract invalid.");
            }

            var rendererDataList = pipelineObject.FindProperty("m_RendererDataList");
            if (rendererDataList == null || !rendererDataList.isArray || rendererDataList.arraySize == 0 || rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue == null)
            {
                throw new InvalidOperationException("PC URP renderer data is missing.");
            }

            var rendererData = rendererDataList.GetArrayElementAtIndex(0).objectReferenceValue;
            if (rendererData == null || AssetDatabase.GetAssetPath(rendererData) != "Assets/Settings/PC_Renderer.asset")
            {
                throw new InvalidOperationException("PC URP renderer data type is invalid.");
            }

            var rendererObject = new SerializedObject(rendererData);
            var rendererFeatures = rendererObject.FindProperty("m_RendererFeatures");
            if (rendererFeatures == null || !rendererFeatures.isArray)
            {
                throw new InvalidOperationException("PC URP renderer feature list is unavailable.");
            }
            for (var i = 0; i < rendererFeatures.arraySize; i++)
            {
                var feature = rendererFeatures.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature == null || feature.name.IndexOf("ScreenSpaceAmbientOcclusion", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var featureObject = new SerializedObject(feature);
                var active = featureObject.FindProperty("m_Active");
                if (active == null || active.boolValue)
                {
                    throw new InvalidOperationException("PC URP SSAO renderer feature must remain inactive.");
                }
            }
        }

        private static void ValidateArenaMaterials(GameObject arena, PhysicsMaterial ballSurface)
        {
            var floor = Require(arena.transform.Find("Floor"), "Arena Floor");
            var floorRenderer = Require(floor.GetComponent<Renderer>(), "Arena Floor renderer");
            ValidateRetroMaterial(floorRenderer.sharedMaterial, AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexturePath), new Vector2(32.5f, 22.5f), "Floor");
            ValidateRetroMaterial(Require(arena.transform.Find("NorthWall").GetComponent<Renderer>(), "NorthWall renderer").sharedMaterial, AssetDatabase.LoadAssetAtPath<Texture2D>(WallTexturePath), new Vector2(8f, 2f), "Wall");
            ValidateRetroMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Trim.mat"), AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), new Vector2(4f, 1f), "Trim");
            ValidateRetroMaterial(AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/Hazard.mat"), AssetDatabase.LoadAssetAtPath<Texture2D>(HazardTexturePath), new Vector2(4f, 1f), "Hazard");
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

        private static void ValidateArenaArchitecture(GameObject arena)
        {
            var sceneRendererCount = 0;
            var sceneRoots = arena.scene.GetRootGameObjects();
            for (var i = 0; i < sceneRoots.Length; i++) sceneRendererCount += sceneRoots[i].GetComponentsInChildren<MeshRenderer>(true).Length;
            if (sceneRendererCount > 80)
            {
                throw new InvalidOperationException("Complete MovementLab MeshRenderer budget exceeded: " + sceneRendererCount);
            }
            Debug.Log("Rocket Fooxball Movement Lab MeshRenderer total: " + sceneRendererCount);

            var architecture = Require(arena.transform.Find("Architecture"), "Arena Architecture");
            var renderers = architecture.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0 || renderers.Length > 80) throw new InvalidOperationException("Arena architecture renderer budget invalid: " + renderers.Length);
            var triangleCount = 0;
            var palette = new[]
            {
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaPrimary.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaTrim.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaHazard.mat"),
                AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/ArenaGlow.mat")
            };
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var filter = Require(renderer.GetComponent<MeshFilter>(), "Architecture MeshFilter");
                var mesh = Require(filter.sharedMesh, "Architecture mesh");
                if (mesh.name == "ArenaRampRails" || mesh.name == "ArenaWallPylon") throw new InvalidOperationException("Removed arena architecture is still present: " + renderer.name);
                if (AssetDatabase.GetAssetPath(mesh) != ArenaKitModelPath) throw new InvalidOperationException("Architecture mesh provenance mismatch: " + renderer.name);
                if (renderer.GetComponentsInChildren<Collider>(true).Length != 0 || renderer.GetComponent<Rigidbody>() != null) throw new InvalidOperationException("Architecture visual must remain renderer-only: " + renderer.name);
                var materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0) throw new InvalidOperationException("Architecture material slots missing: " + renderer.name);
                for (var j = 0; j < materials.Length; j++) if (materials[j] == null) throw new InvalidOperationException("Architecture material slot null: " + renderer.name);
                var expectedMaterials = ResolveArenaKitMaterials(mesh.name, palette);
                if (materials.Length != expectedMaterials.Length) throw new InvalidOperationException("Architecture material slot count mismatch: " + renderer.name);
                for (var j = 0; j < materials.Length; j++) if (materials[j] != expectedMaterials[j]) throw new InvalidOperationException("Architecture material slot order mismatch: " + renderer.name);
                triangleCount += mesh.triangles.Length / 3;
            }
            if (triangleCount > 50000) throw new InvalidOperationException("Arena architecture triangle budget exceeded: " + triangleCount);
            ValidateArenaKitModel();
            ValidateArchitectureTransform(architecture, "NorthGoalShell", new Vector3(-GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, -90f, 0f));
            ValidateArchitectureTransform(architecture, "SouthGoalShell", new Vector3(GoalAxisPosition, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            ValidateShieldVisual(arena.transform.Find("NorthGoal"), "NorthGoal");
            ValidateShieldVisual(arena.transform.Find("SouthGoal"), "SouthGoal");
        }

        private static void ValidateArchitectureTransform(Transform architecture, string name, Vector3 position, Quaternion rotation)
        {
            var item = Require(architecture.Find(name), "Architecture " + name);
            if (Vector3.Distance(item.localPosition, position) > 0.01f || Quaternion.Angle(item.localRotation, rotation) > 0.1f || Vector3.Distance(item.localScale, Vector3.one) > 0.001f) throw new InvalidOperationException("Architecture transform mismatch: " + name);
            if (!item.gameObject.isStatic) throw new InvalidOperationException("Architecture visual must be static: " + name);
        }

        private static void ValidateShieldVisual(Transform goal, string label)
        {
            var collider = Require(goal != null ? goal.Find("ShieldCollider") : null, label + " ShieldCollider").GetComponent<BoxCollider>();
            var visual = Require(goal != null ? goal.Find("ShieldVisual") : null, label + " ShieldVisual");
            if (collider == null || collider.isTrigger || visual.GetComponent<Collider>() != null || visual.GetComponent<MeshRenderer>() == null) throw new InvalidOperationException(label + " shield collider/render split invalid.");
            var material = visual.GetComponent<MeshRenderer>().sharedMaterial;
            if (material == null || material.shader == null || material.shader.name != "RocketFooxball/RetroShield" || Mathf.Abs(material.GetFloat("_Alpha") - 0.52f) > 0.001f) throw new InvalidOperationException(label + " shield material contract invalid.");
        }

        private static void ValidateArenaKitModel()
        {
            var importer = AssetImporter.GetAtPath(ArenaKitModelPath) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.None || importer.importAnimation || importer.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(importer.globalScale - 1f) > 0.0001f) throw new InvalidOperationException("ArenaKit importer contract invalid.");
            var expected = new[] { "ArenaGoalShell", "ArenaRampRails", "ArenaWallPylon", "ArenaPerimeterTruss", "ArenaScoreboard" };
            var assets = AssetDatabase.LoadAllAssetsAtPath(ArenaKitModelPath);
            for (var i = 0; i < expected.Length; i++)
            {
                Mesh found = null;
                for (var j = 0; j < assets.Length; j++) if (assets[j] is Mesh mesh && mesh.name == expected[i]) found = mesh;
                if (found == null || AssetDatabase.GetAssetPath(found) != ArenaKitModelPath || found.subMeshCount < 1)
                {
                    var importedNames = new List<string>();
                    for (var k = 0; k < assets.Length; k++) if (assets[k] != null) importedNames.Add(assets[k].name + "[" + assets[k].GetType().Name + "]");
                    throw new InvalidOperationException("ArenaKit named mesh missing/provenance invalid: " + expected[i] + "; imported assets=" + string.Join(",", importedNames.ToArray()));
                }
            }
        }

        private static void ValidateRetroMaterial(Material material, Texture2D texture, Vector2 scale, string label)
        {
            if (material == null || material.shader == null || material.shader.name != "RocketFooxball/RetroToonLit") throw new InvalidOperationException(label + " must use RetroToonLit.");
            if (material.GetTexture("_BaseMap") != (texture != null ? texture : Texture2D.whiteTexture)) throw new InvalidOperationException(label + " base texture mismatch.");
            if (material.GetTextureScale("_BaseMap") != scale) throw new InvalidOperationException(label + " texture scale mismatch.");
            if (Mathf.Abs(material.GetFloat("_LightSteps") - 3f) > 0.001f) throw new InvalidOperationException(label + " light step contract mismatch.");
        }

        private static void ValidateBallMaterial(Material material)
        {
            ValidateRetroMaterial(material, AssetDatabase.LoadAssetAtPath<Texture2D>(BallTexturePath), Vector2.one, "Ball");
            var baseColor = material.GetColor("_BaseColor");
            if (Vector3.Distance(new Vector3(baseColor.r, baseColor.g, baseColor.b), Vector3.one) > 0.001f || Mathf.Abs(baseColor.a - 1f) > 0.001f)
            {
                throw new InvalidOperationException("Ball material must preserve neutral black/white texture color.");
            }
            var emissionColor = material.GetColor("_EmissionColor");
            if (emissionColor.maxColorComponent > 0.001f || Mathf.Abs(material.GetFloat("_EmissionStrength")) > 0.001f)
            {
                throw new InvalidOperationException("Ball material must not add a tinted emission.");
            }
        }

        private static void ValidateWeaponMaterials(GameObject visual)
        {
            var metal = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponMetal.mat");
            var dark = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponDark.mat");
            var accent = AssetDatabase.LoadAssetAtPath<Material>(MaterialsPath + "/WeaponAccent.mat");
            ValidateWeaponMaterial(metal, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponMetalTexturePath), WeaponMetalBaseColor, "WeaponMetal");
            ValidateWeaponMaterial(dark, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponDarkTexturePath), WeaponDarkBaseColor, "WeaponDark");
            ValidateWeaponMaterial(accent, AssetDatabase.LoadAssetAtPath<Texture2D>(WeaponAccentTexturePath), WeaponAccentBaseColor, "WeaponAccent");

            var seenMetal = false;
            var seenDark = false;
            var seenAccent = false;
            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var expected = metal;
                if (renderer.name.IndexOf("Dark", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    expected = dark;
                    seenDark = true;
                }
                else if (renderer.name.IndexOf("Accent", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    expected = accent;
                    seenAccent = true;
                }
                else if (renderer.name.IndexOf("Metal", StringComparison.OrdinalIgnoreCase) >= 0 || renderer.name.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    seenMetal = true;
                }

                var slots = renderer.sharedMaterials;
                if (slots == null || slots.Length == 0) throw new InvalidOperationException("Weapon renderer has no material slots: " + renderer.name);
                for (var j = 0; j < slots.Length; j++)
                {
                    if (slots[j] != expected) throw new InvalidOperationException("Weapon material slot mapping mismatch: " + renderer.name + "/" + j);
                }
            }
            if (!seenMetal || !seenDark || !seenAccent)
            {
                throw new InvalidOperationException("Weapon material slots must contain Metal, Dark, and Accent parts.");
            }
        }

        private static void ValidateWeaponMaterial(Material material, Texture2D texture, Color baseColor, string label)
        {
            ValidateRetroMaterial(material, texture, Vector2.one, label);
            var actual = material.GetColor("_BaseColor");
            if (Vector4.Distance(actual, baseColor) > 0.001f)
            {
                throw new InvalidOperationException(label + " base color contract mismatch.");
            }
        }

        private static void ValidateRocketMaterial(Material material)
        {
            ValidateRetroMaterial(material, AssetDatabase.LoadAssetAtPath<Texture2D>(TrimTexturePath), Vector2.one, "Rocket");
            if (Vector4.Distance(material.GetColor("_BaseColor"), RocketBaseColor) > 0.001f ||
                Vector4.Distance(material.GetColor("_EmissionColor"), RocketEmissionColor) > 0.001f ||
                Mathf.Abs(material.GetFloat("_EmissionStrength") - RocketEmissionStrength) > 0.001f)
            {
                throw new InvalidOperationException("Rocket toon emission material contract invalid.");
            }
        }

        private static void ValidateTextureImporterContracts()
        {
            ValidateTextureImporter(GrassTexturePath, true);
            ValidateTextureImporter(WallTexturePath, true);
            ValidateTextureImporter(TrimTexturePath, true);
            ValidateTextureImporter(HazardTexturePath, true);
            ValidateTextureImporter(BallTexturePath, true);
            ValidateTextureImporter(WeaponMetalTexturePath, true);
            ValidateTextureImporter(WeaponDarkTexturePath, true);
            ValidateTextureImporter(WeaponAccentTexturePath, true);
            ValidateTextureImporter(ShieldTexturePath, false);
            ValidateTextureImporter(ExplosionTexturePath, false);
            ValidateTextureImporter(SmokeTexturePath, false);
        }

        private static void ValidateTextureImporter(string path, bool repeat)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var expectedSize = path == BallTexturePath || path == WeaponMetalTexturePath || path == WeaponDarkTexturePath || path == WeaponAccentTexturePath ? 256 : 128;
            var settings = new TextureImporterSettings();
            if (importer != null) importer.ReadTextureSettings(settings);
            var platform = importer != null ? importer.GetDefaultPlatformTextureSettings() : default(TextureImporterPlatformSettings);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (importer == null || texture == null || texture.width > 256 || texture.height > 256 || !importer.sRGBTexture || !importer.mipmapEnabled || importer.filterMode != FilterMode.Bilinear || importer.anisoLevel != 0 || importer.wrapMode != (repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp) || importer.maxTextureSize != expectedSize || !settings.ignoreMipmapLimit || platform.overridden || platform.maxTextureSize != expectedSize)
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
            var rocket = AssetImporter.GetAtPath(RocketModelPath) as ModelImporter;
            if (rocket == null || rocket.animationType != ModelImporterAnimationType.None || rocket.importAnimation || rocket.materialImportMode != ModelImporterMaterialImportMode.None || Mathf.Abs(rocket.globalScale - 1f) > 0.0001f) throw new InvalidOperationException("Rocket importer contract invalid.");
            ValidateArenaKitModel();
        }

        private static void ValidateRigImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null || importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.importAnimation || Mathf.Abs(importer.globalScale - 1f) > 0.0001f)
            {
                throw new InvalidOperationException("Rig importer contract invalid: " + path);
            }
            var clips = importer.clipAnimations;
            var expected = path == CharacterModelPath ? new[] { "Idle", "Run", "Jump", "Fall", "Land", "Kick" } : new[] { "Idle", "Kick" };
            var loops = path == CharacterModelPath ? new[] { true, true, false, false, false, false } : new[] { true, false };
            if (clips == null || clips.Length != expected.Length) throw new InvalidOperationException("Rig importer clip count mismatch: " + path);
            for (var i = 0; i < expected.Length; i++) if (clips[i].name != expected[i] || clips[i].loopTime != loops[i]) throw new InvalidOperationException("Rig importer clip contract invalid: " + path + "/" + expected[i]);
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
                    var prefabMotor = Require(root.GetComponent<PlayerMotor>(), "Player prefab PlayerMotor");
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
                    ValidateSerializedFloat(prefabFeedback, "celebrationOrbitRadius", CelebrationOrbitRadius, "Player prefab PlayerCameraFeedback.celebrationOrbitRadius");
                    ValidateSerializedFloat(prefabFeedback, "celebrationOrbitHeight", CelebrationOrbitHeight, "Player prefab PlayerCameraFeedback.celebrationOrbitHeight");
                    ValidateSerializedFloat(prefabFeedback, "celebrationLookHeight", CelebrationLookHeight, "Player prefab PlayerCameraFeedback.celebrationLookHeight");
                    ValidateSerializedFloat(prefabFeedback, "celebrationOrbitDegrees", CelebrationOrbitDegrees, "Player prefab PlayerCameraFeedback.celebrationOrbitDegrees");
                    ValidateSerializedFloat(prefabFeedback, "celebrationFov", CelebrationFov, "Player prefab PlayerCameraFeedback.celebrationFov");
                    ValidateSerializedFloat(prefabMotor, "jumpVelocity", 6.75f, "Player prefab PlayerMotor.jumpVelocity");
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
                        var renderer = Require(meshFilters[i].GetComponent<Renderer>(), "Rocket prefab imported mesh renderer");
                        ValidateRocketMaterial(renderer.sharedMaterial);
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
            var system = systems[0];
            var main = system.main;
            var emission = system.emission;
            var color = system.colorOverLifetime;
            if (main.maxParticles > 48 || main.simulationSpace != ParticleSystemSimulationSpace.World ||
                Mathf.Abs(main.startLifetime.constantMax - RocketTrailLifetime) > 0.01f ||
                Mathf.Abs(main.startSize.constantMax - RocketTrailStartSize) > 0.01f ||
                Mathf.Abs(emission.rateOverDistance.constantMax - RocketTrailRateOverDistance) > 0.01f ||
                main.startColor.color.a < RocketTrailStartColor.a - 0.01f ||
                !color.enabled || color.color.gradient == null || color.color.gradient.alphaKeys.Length < 2 || color.color.gradient.alphaKeys[0].alpha < 0.75f)
            {
                throw new InvalidOperationException("Rocket trail particle visibility contract invalid.");
            }
            var projectile = Require(rocketPrefab.GetComponent<RocketProjectile>(), "RocketProjectile");
            ValidateReference(projectile, "trailVfx", trail, "RocketProjectile.trailVfx");
            ValidateRocketLight(rocketPrefab);
        }

        private static void ValidateRocketLight(GameObject rocketPrefab)
        {
            var lights = rocketPrefab.GetComponentsInChildren<Light>(true);
            if (lights.Length != 1)
            {
                throw new InvalidOperationException("Rocket prefab must contain exactly one projectile point light.");
            }

            var light = lights[0];
            if (light.type != LightType.Point || Vector4.Distance(light.color, RocketLightColor) > 0.001f ||
                Mathf.Abs(light.intensity - RocketLightIntensity) > 0.001f || Mathf.Abs(light.range - RocketLightRange) > 0.001f ||
                light.shadows != LightShadows.None)
            {
                throw new InvalidOperationException("Rocket projectile light contract invalid.");
            }

            var serialized = new SerializedObject(light);
            if (serialized.FindProperty("m_Type") == null || serialized.FindProperty("m_Color") == null ||
                serialized.FindProperty("m_Intensity") == null || serialized.FindProperty("m_Range") == null ||
                (serialized.FindProperty("m_Shadows.m_Type") == null && serialized.FindProperty("m_Shadows") == null))
            {
                throw new InvalidOperationException("Rocket projectile light serialized fields are missing.");
            }
        }

        private static void ValidateExplosionPrefab(GameObject prefab)
        {
            if (prefab == null) throw new InvalidOperationException("Explosion prefab unavailable.");
            if (Vector3.Distance(prefab.transform.localScale, Vector3.one * BlastVisualScale) > 0.001f)
            {
                throw new InvalidOperationException("Explosion VFX scale must track the enlarged blast radius.");
            }
            var effect = Require(prefab.GetComponent<ExplosionVfx>(), "ExplosionVfx");
            var systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length != 4) throw new InvalidOperationException("Explosion VFX must contain Flash/FireChunks/Sparks/Smoke systems.");
            var emitted = 0;
            ParticleSystem fire = null;
            ParticleSystem smoke = null;
            var fireBurstCount = 0;
            var smokeBurstCount = 0;
            for (var i = 0; i < systems.Length; i++)
            {
                var system = systems[i];
                if (system.name == "FireChunks") fire = system;
                if (system.name == "Smoke") smoke = system;
                var emission = system.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                for (var j = 0; j < bursts.Length; j++)
                {
                    emitted += bursts[j].maxCount;
                    if (system.name == "FireChunks") fireBurstCount += bursts[j].maxCount;
                    if (system.name == "Smoke") smokeBurstCount += bursts[j].maxCount;
                }
                var main = system.main;
                if (main.maxParticles > 40 || main.duration + main.startLifetime.constantMax > 1.25f) throw new InvalidOperationException("Explosion particle lifetime/max contract invalid: " + system.name);
                var sheet = system.textureSheetAnimation;
                if (!sheet.enabled || sheet.numTilesX != 4 || sheet.numTilesY != 4 || sheet.animation != ParticleSystemAnimationType.WholeSheet) throw new InvalidOperationException("Explosion texture-sheet contract invalid: " + system.name);
            }
            if (emitted != 37) throw new InvalidOperationException("Explosion burst count must total 37.");
            if (fire == null || smoke == null || fireBurstCount <= smokeBurstCount ||
                fire.main.startSize.constantMax <= smoke.main.startSize.constantMax ||
                !fire.colorOverLifetime.enabled || fire.colorOverLifetime.color.gradient == null ||
                !smoke.colorOverLifetime.enabled || smoke.colorOverLifetime.color.gradient == null ||
                fire.colorOverLifetime.color.gradient.alphaKeys.Length < 2 || smoke.colorOverLifetime.color.gradient.alphaKeys.Length < 2 ||
                fire.colorOverLifetime.color.gradient.alphaKeys[0].alpha <= smoke.colorOverLifetime.color.gradient.alphaKeys[0].alpha)
            {
                throw new InvalidOperationException("Explosion VFX must keep fire more visible than smoke.");
            }
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
            for (var i = 0; i < GeneratedImporterMetadataPaths.Length; i++) NormalizeYamlFile(GeneratedImporterMetadataPaths[i]);
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

        private static void ValidateSerializedFloat(UnityEngine.Object target, string propertyName, float expected, string label)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Float || Mathf.Abs(property.floatValue - expected) > 0.001f)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
            }
        }

        private static void ValidateSerializedInteger(UnityEngine.Object target, string propertyName, int expected, string label)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Integer || property.intValue != expected)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
            }
        }

        private static void ValidateSerializedVector3(UnityEngine.Object target, string propertyName, Vector3 expected, string label)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Vector3 || Vector3.Distance(property.vector3Value, expected) > 0.001f)
            {
                throw new InvalidOperationException(label + " tuning mismatch.");
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

        private static void SetInteger(UnityEngine.Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.propertyType != SerializedPropertyType.Integer)
            {
                throw new InvalidOperationException(target.GetType().Name + " has no serialized integer '" + propertyName + "'.");
            }
            property.intValue = value;
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
            EnsureFolder("Assets/_Game/Generated");
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
