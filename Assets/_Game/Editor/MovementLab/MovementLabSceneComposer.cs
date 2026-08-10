using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
using static RocketFooxball.Editor.MovementLabPrefabPipeline;
using static RocketFooxball.Editor.MovementLabArenaPipeline;
using static RocketFooxball.Editor.MovementLabLightingPipeline;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabSceneComposer
    {
                // Stage-local entry points. The legacy monolithic method below is
                // retained for compatibility/forced comparison; normal assembly
                // uses these bounded operations through MovementLabStageRunner.
                internal static void AssembleQualityStage()
                {
                    EnsureFolders();
                    GraphicsQualityConfigurator.Configure();
                }

                internal static void AssembleImporterStage()
                {
                    EnsureFolders();
                    MovementLabImportPipeline.Apply();
                }

                internal static void AssembleMaterialPrefabStage()
                {
                    EnsureFolders();

                    var ballSurface = GetOrCreatePhysicMaterial();
                    var floorMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Floor", LoadTexture(GrassTexturePath), LoadTexture(GrassNormalTexturePath), LoadTexture(GrassMetallicTexturePath), LoadTexture(GrassOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(32.5f, 22.5f), Color.white, Color.clear, 0f, 1f, 1f, 0.75f, 0.65f));
                    var wallMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Wall", LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(8f, 2f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.80f));
                    var trimMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Trim", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 1f));
                    var hazardMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Hazard", LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.75f));
                    var markingMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Marking", null, null, null, null, null, null, Vector2.one, new Color(1.00f, 0.96f, 0.78f, 1f), Color.clear, 0f, 0f, 0.5f, 1f, 1f));
                    var ballMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Ball", LoadTexture(BallTexturePath), LoadTexture(BallNormalTexturePath), LoadTexture(BallMetallicTexturePath), LoadTexture(BallOcclusionTexturePath), null, null, Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.65f, 0.45f));
                    var rocketMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Rocket", LoadTexture(RocketTexturePath), LoadTexture(RocketNormalTexturePath), LoadTexture(RocketMetallicTexturePath), LoadTexture(RocketOcclusionTexturePath), null, null, Vector2.one, RocketBaseColor, Color.clear, 0f, 1f, 1f, 0.85f, 0.80f));
                    var rocketHotMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("RocketHot", LoadTexture(RocketTexturePath), LoadTexture(RocketNormalTexturePath), LoadTexture(RocketMetallicTexturePath), LoadTexture(RocketOcclusionTexturePath), LoadTexture(RocketEmissionTexturePath), null, Vector2.one, Color.white, RocketEmissionColor, RocketEmissionStrength, 1f, 1f, 0.85f, 0.80f));
                    var projectileGlowMaterial = GetOrCreateAdditiveParticleMaterial("ProjectileGlow", Color.white, LoadTexture(RocketGlowTexturePath), 2.5f);
                    var frameMaterial = trimMaterial;
                    var shieldMaterial = GetOrCreateShieldMaterial("Shield", new Color(0.10f, 0.75f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    var arenaPrimaryMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaPrimary", LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.80f));
                    var arenaTrimMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaTrim", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 1f));
                    var arenaHazardMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaHazard", LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.75f));
                    var arenaGlowMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaGlow", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, new Color(0.10f, 0.95f, 0.88f, 1f), 2.0f, 1f, 1f, 0.80f, 1f));
                    var gridCeilingMaterial = GetOrCreateGridMaterial("ContainmentGridCeiling", new Vector2(32.5f, 22.5f));
                    var gridLongWallMaterial = GetOrCreateGridMaterial("ContainmentGridLongWall", new Vector2(32.5f, 10f));
                    var gridEndWallMaterial = GetOrCreateGridMaterial("ContainmentGridEndWall", new Vector2(22.5f, 10f));
                    var shieldBlueMaterial = GetOrCreateShieldMaterial("ShieldBlue", new Color(0.10f, 0.50f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    var shieldRedMaterial = GetOrCreateShieldMaterial("ShieldRed", new Color(1.00f, 0.22f, 0.20f, 1f), new Color(1.00f, 0.55f, 0.45f, 1f));
                    MovementLabMaterialPipeline.ValidateCatalog(floorMaterial, wallMaterial, trimMaterial, hazardMaterial, markingMaterial, ballMaterial, rocketMaterial);

                    var rocketPrefab = BuildRocketPrefab(rocketMaterial, rocketHotMaterial, projectileGlowMaterial);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(RocketPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                    rocketPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RocketPrefabPath);
                    var ballPrefab = BuildBallPrefab(ballMaterial, ballSurface);
                    BuildPlayerPrefab(rocketPrefab);
                    BuildExplosionVfxPrefab();
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(ExplosionPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                    var explosionRootAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ExplosionPrefabPath);
                    var explosionAssetComponent = explosionRootAsset != null ? explosionRootAsset.GetComponent<ExplosionVfx>() : null;
                    if (explosionAssetComponent == null) throw new InvalidOperationException("Explosion VFX prefab failed to import.");
                    if (!EditorUtility.IsPersistent(explosionAssetComponent)) throw new InvalidOperationException("Explosion VFX component is not a persistent prefab asset.");
                    MovementLabMaterialPipeline.FinalizeGeneratedMaterialPersistence();
                }

                internal static void AssembleGameplaySceneStage()
                {
                    EnsureFolders();
                    // Rebuilding gameplay/wiring creates a fresh scene. Keep
                    // the serialized bake document for continuity, while the
                    // lighting key records object identities and renderer
                    // bindings so recreation cannot falsely reuse a bake.
                    var preservedLightmapSettings = CaptureExistingLightmapSettingsDocument();
                    var builderSignature = ComputeBuilderSignature();
                    var ballSurface = LoadRequiredAsset<PhysicsMaterial>(BallSurfacePath);
                    var floorMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/Floor.mat");
                    var wallMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/Wall.mat");
                    var markingMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/Marking.mat");
                    var frameMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/Trim.mat");
                    var shieldMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/Shield.mat");
                    var arenaPrimaryMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ArenaPrimary.mat");
                    var arenaTrimMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ArenaTrim.mat");
                    var arenaHazardMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ArenaHazard.mat");
                    var arenaGlowMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ArenaGlow.mat");
                    var gridCeilingMaterial = LoadRequiredAsset<Material>(GridCeilingMaterialPath);
                    var gridLongWallMaterial = LoadRequiredAsset<Material>(GridLongWallMaterialPath);
                    var gridEndWallMaterial = LoadRequiredAsset<Material>(GridEndWallMaterialPath);
                    var shieldBlueMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ShieldBlue.mat");
                    var shieldRedMaterial = LoadRequiredAsset<Material>(MaterialsPath + "/ShieldRed.mat");
                    var rocketPrefab = LoadRequiredAsset<GameObject>(RocketPrefabPath);
                    var ballPrefab = LoadRequiredAsset<GameObject>(BallPrefabPath);
                    var playerPrefab = LoadRequiredAsset<GameObject>(PrefabPath);
                    var explosionRootAsset = LoadRequiredAsset<GameObject>(ExplosionPrefabPath);
                    var explosionPrefab = GetSerializablePrefabComponent<ExplosionVfx>(explosionRootAsset, out var explosionPrefabProbe);
                    try
                    {
                        if (explosionPrefab == null) throw new InvalidOperationException("Explosion VFX prefab source component could not be resolved.");
                        RegisterBuildScene();
                        UnityEngine.Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
                        SetProjectFixedTimestep();
                        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                        var defaultCamera = Camera.main;
                        if (defaultCamera != null) UnityEngine.Object.DestroyImmediate(defaultCamera.gameObject);
                        var arena = BuildArena(floorMaterial, wallMaterial, markingMaterial, frameMaterial, shieldMaterial, ballSurface, arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial, gridCeilingMaterial, gridLongWallMaterial, gridEndWallMaterial, shieldBlueMaterial, shieldRedMaterial);
                        var shieldSetObject = new GameObject("GoalShieldSet");
                        var goalShieldSet = shieldSetObject.AddComponent<GoalShieldSet>();
                        SetObjectArray(goalShieldSet, "colliders", arena.Shields);
                        var explosionObject = new GameObject("ExplosionResolver");
                        var explosionResolver = explosionObject.AddComponent<ExplosionResolver>();
                        var explosionVfxSpawner = explosionObject.AddComponent<ExplosionVfxSpawner>();
                        SetObjectReference(explosionResolver, "goalShieldSet", goalShieldSet);
                        SetObjectReference(explosionResolver, "explosionVfxSpawner", explosionVfxSpawner);
                        SetObjectReference(explosionVfxSpawner, "explosionVfxPrefab", explosionPrefab);
                        SetFloat(explosionResolver, "blastRadius", BlastRadius);
                        SetFloat(explosionResolver, "playerImpulseStrength", 24f);
                        SetFloat(explosionResolver, "ballImpulseStrength", 16f);
                        SetFloat(explosionResolver, "occludedForce", 0.25f);
                        SetFloat(explosionResolver, "playerUpBias", 0.18f);
                        SetFloat(explosionResolver, "underfootForwardImpulseScale", UnderfootForwardImpulseScale);
                        SetFloat(explosionResolver, "underfootUpwardImpulseScale", UnderfootUpwardImpulseScale);
                        SetFloat(explosionResolver, "underfootHighSpeedVerticalRedirect", UnderfootHighSpeedVerticalRedirect);
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
                        SetObjectReference(ballMotor, "goalShieldSet", goalShieldSet);
                        SetObjectReference(kick, "ball", ballMotor);
                        SetObjectReference(launcher, "explosionResolver", explosionResolver);
                        SetObjectReference(cameraFeedback, "player", playerMotor);
                        SetObjectReference(cameraFeedback, "targetCamera", player.GetComponentInChildren<Camera>(true));
                        SetObjectReference(cameraFeedback, "viewmodels", player.transform.Find("Head/Camera/Viewmodels").gameObject);
                        SetObjectReference(cameraFeedback, "crosshairCanvas", player.transform.Find("Head/Camera/CrosshairCanvas").gameObject);
                        SetObjectReference(arena.NorthGoal.Trigger, "ball", ballMotor);
                        SetObjectReference(arena.SouthGoal.Trigger, "ball", ballMotor);
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
                        var hud = new GameObject("DebugHUD");
                        var hudComponent = hud.AddComponent<MovementDebugHud>();
                        SetObjectReference(hudComponent, "player", playerMotor);
                        SetObjectReference(hudComponent, "ball", ballMotor);
                        SetObjectReference(hudComponent, "launcher", launcher);
                        SetObjectReference(hudComponent, "kick", kick);
                        SetObjectReference(hudComponent, "match", match);
                        new GameObject(GetBuildMarkerName(builderSignature));
                        BindSceneEnvironment(scene, arena);
                        EditorSceneManager.SaveScene(scene, ScenePath);
                        RestoreLightmapSettingsDocument(preservedLightmapSettings);
                        SaveGameplayProjectSettings();
                        NormalizeGameplayYamlWhitespace();
                    }
                    finally
                    {
                        if (explosionPrefabProbe != null) UnityEngine.Object.DestroyImmediate(explosionPrefabProbe);
                    }
                }

                private static string CaptureExistingLightmapSettingsDocument()
                {
                    var path = MovementLabManifestStore.ResolveProjectPath(ScenePath);
                    if (!File.Exists(path)) return null;
                    var normalized = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace("\r", "\n");
                    var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
                    var trailingNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
                    var contentLineCount = lines.Length - (trailingNewline ? 1 : 0);
                    for (var start = 0; start < contentLineCount; start++)
                    {
                        if (!lines[start].StartsWith("--- !u!157 ", StringComparison.Ordinal)) continue;
                        var end = start + 1;
                        while (end < contentLineCount && !lines[end].StartsWith("--- !u!", StringComparison.Ordinal)) end++;
                        var document = string.Join("\n", lines, start, end - start);
                        if (document.IndexOf("LightmapSettings:", StringComparison.Ordinal) >= 0) return document;
                        start = end - 1;
                    }
                    return null;
                }

                private static void RestoreLightmapSettingsDocument(string preservedDocument)
                {
                    if (string.IsNullOrWhiteSpace(preservedDocument)) return;
                    var path = MovementLabManifestStore.ResolveProjectPath(ScenePath);
                    if (!File.Exists(path)) return;
                    var normalized = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n").Replace("\r", "\n");
                    var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
                    var trailingNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
                    var contentLineCount = lines.Length - (trailingNewline ? 1 : 0);
                    var output = new List<string>();
                    var replaced = false;
                    for (var start = 0; start < contentLineCount;)
                    {
                        if (!lines[start].StartsWith("--- !u!", StringComparison.Ordinal))
                        {
                            output.Add(lines[start++]);
                            continue;
                        }

                        var end = start + 1;
                        while (end < contentLineCount && !lines[end].StartsWith("--- !u!", StringComparison.Ordinal)) end++;
                        var document = string.Join("\n", lines, start, end - start);
                        if (lines[start].StartsWith("--- !u!157 ", StringComparison.Ordinal) && document.IndexOf("LightmapSettings:", StringComparison.Ordinal) >= 0)
                        {
                            if (!replaced)
                            {
                                output.Add(preservedDocument);
                                replaced = true;
                            }
                        }
                        else
                        {
                            output.Add(document);
                        }
                        start = end;
                    }

                    if (!replaced) output.Add(preservedDocument);
                    var restored = string.Join("\n", output) + (trailingNewline ? "\n" : string.Empty);
                    File.WriteAllText(path, restored, new UTF8Encoding(false));
                }

                private static void NormalizeGameplayYamlWhitespace()
                {
                    // Gameplay owns only the scene and its wiring-related
                    // project settings. Do not normalize lighting/material
                    // assets here: even byte-level cleanup would violate the
                    // gameplay stage's read-only asset contract.
                    for (var i = 0; i < MovementLabContract.GameplaySceneOutputs.Length; i++)
                    {
                        NormalizeYamlFile(MovementLabContract.GameplaySceneOutputs[i]);
                    }
                }

                private static void SaveGameplayProjectSettings()
                {
                    for (var i = 0; i < MovementLabContract.GameplaySceneOutputs.Length; i++)
                    {
                        var path = MovementLabContract.GameplaySceneOutputs[i];
                        if (!path.StartsWith("ProjectSettings/", StringComparison.Ordinal)) continue;
                        var asset = AssetDatabase.LoadMainAssetAtPath(path);
                        if (asset != null && EditorUtility.IsPersistent(asset)) AssetDatabase.SaveAssetIfDirty(asset);
                    }
                }

                private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
                {
                    var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                    if (asset == null) throw new InvalidOperationException("Required stage asset is missing: " + path);
                    return asset;
                }

                internal static void AssembleMovementLabUnstaged()
                {
                    var builderSignature = ComputeBuilderSignature();

                    EnsureFolders();

                    // Quality assets must settle before importer, material, scene, or bake
                    // writes. Build fingerprint includes these outputs, so valid state
                    // returns above without touching project settings.
                    GraphicsQualityConfigurator.Configure();

                    MovementLabImportPipeline.Apply();

                    var ballSurface = GetOrCreatePhysicMaterial();
                    var floorMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Floor", LoadTexture(GrassTexturePath), LoadTexture(GrassNormalTexturePath), LoadTexture(GrassMetallicTexturePath), LoadTexture(GrassOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(32.5f, 22.5f), Color.white, Color.clear, 0f, 1f, 1f, 0.75f, 0.65f));
                    var wallMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Wall", LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(8f, 2f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.80f));
                    var trimMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Trim", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 1f));
                    var hazardMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Hazard", LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), new Vector2(4f, 1f), Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.75f));
                    var markingMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Marking", null, null, null, null, null, null, Vector2.one, new Color(1.00f, 0.96f, 0.78f, 1f), Color.clear, 0f, 0f, 0.5f, 1f, 1f));
                    var ballMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Ball", LoadTexture(BallTexturePath), LoadTexture(BallNormalTexturePath), LoadTexture(BallMetallicTexturePath), LoadTexture(BallOcclusionTexturePath), null, null, Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.65f, 0.45f));
                    var rocketMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("Rocket", LoadTexture(RocketTexturePath), LoadTexture(RocketNormalTexturePath), LoadTexture(RocketMetallicTexturePath), LoadTexture(RocketOcclusionTexturePath), null, null, Vector2.one, RocketBaseColor, Color.clear, 0f, 1f, 1f, 0.85f, 0.80f));
                    var rocketHotMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("RocketHot", LoadTexture(RocketTexturePath), LoadTexture(RocketNormalTexturePath), LoadTexture(RocketMetallicTexturePath), LoadTexture(RocketOcclusionTexturePath), LoadTexture(RocketEmissionTexturePath), null, Vector2.one, Color.white, RocketEmissionColor, RocketEmissionStrength, 1f, 1f, 0.85f, 0.80f));
                    var projectileGlowMaterial = GetOrCreateAdditiveParticleMaterial("ProjectileGlow", Color.white, LoadTexture(RocketGlowTexturePath), 2.5f);
                    var frameMaterial = trimMaterial;
                    var shieldMaterial = GetOrCreateShieldMaterial("Shield", new Color(0.10f, 0.75f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    var arenaPrimaryMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaPrimary", LoadTexture(WallTexturePath), LoadTexture(WallNormalTexturePath), LoadTexture(WallMetallicTexturePath), LoadTexture(WallOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.80f));
                    var arenaTrimMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaTrim", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 1f));
                    var arenaHazardMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaHazard", LoadTexture(HazardTexturePath), LoadTexture(HazardNormalTexturePath), LoadTexture(HazardMetallicTexturePath), LoadTexture(HazardOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, Color.clear, 0f, 1f, 1f, 0.80f, 0.75f));
                    var arenaGlowMaterial = GetOrCreateLitMaterial(new PbrMaterialSpecification("ArenaGlow", LoadTexture(TrimTexturePath), LoadTexture(TrimNormalTexturePath), LoadTexture(TrimMetallicTexturePath), LoadTexture(TrimOcclusionTexturePath), null, LoadTexture(DetailNormalTexturePath), Vector2.one, Color.white, new Color(0.10f, 0.95f, 0.88f, 1f), 2.0f, 1f, 1f, 0.80f, 1f));
                    var gridCeilingMaterial = GetOrCreateGridMaterial("ContainmentGridCeiling", new Vector2(32.5f, 22.5f));
                    var gridLongWallMaterial = GetOrCreateGridMaterial("ContainmentGridLongWall", new Vector2(32.5f, 10f));
                    var gridEndWallMaterial = GetOrCreateGridMaterial("ContainmentGridEndWall", new Vector2(22.5f, 10f));
                    var shieldBlueMaterial = GetOrCreateShieldMaterial("ShieldBlue", new Color(0.10f, 0.50f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    var shieldRedMaterial = GetOrCreateShieldMaterial("ShieldRed", new Color(1.00f, 0.22f, 0.20f, 1f), new Color(1.00f, 0.55f, 0.45f, 1f));
                    MovementLabMaterialPipeline.ValidateCatalog(floorMaterial, wallMaterial, trimMaterial, hazardMaterial, markingMaterial, ballMaterial, rocketMaterial);

                    var rocketPrefab = BuildRocketPrefab(rocketMaterial, rocketHotMaterial, projectileGlowMaterial);
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
                    UnityEngine.Physics.gravity = Vector3.down * GamePhysicsSettings.GravityMagnitude;
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

                    var arena = BuildArena(floorMaterial, wallMaterial, markingMaterial, frameMaterial, shieldMaterial, ballSurface, arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial, gridCeilingMaterial, gridLongWallMaterial, gridEndWallMaterial, shieldBlueMaterial, shieldRedMaterial);
                    var shieldSetObject = new GameObject("GoalShieldSet");
                    var goalShieldSet = shieldSetObject.AddComponent<GoalShieldSet>();
                    SetObjectArray(goalShieldSet, "colliders", arena.Shields);
                    var explosionObject = new GameObject("ExplosionResolver");
                    var explosionResolver = explosionObject.AddComponent<ExplosionResolver>();
                    var explosionVfxSpawner = explosionObject.AddComponent<ExplosionVfxSpawner>();
                    SetObjectReference(explosionResolver, "goalShieldSet", goalShieldSet);
                    SetObjectReference(explosionResolver, "explosionVfxSpawner", explosionVfxSpawner);
                    SetObjectReference(explosionVfxSpawner, "explosionVfxPrefab", explosionPrefab);
                    SetFloat(explosionResolver, "blastRadius", BlastRadius);
                    SetFloat(explosionResolver, "playerImpulseStrength", 24f);
                    SetFloat(explosionResolver, "ballImpulseStrength", 16f);
                    SetFloat(explosionResolver, "occludedForce", 0.25f);
                    SetFloat(explosionResolver, "playerUpBias", 0.18f);
                    SetFloat(explosionResolver, "underfootForwardImpulseScale", UnderfootForwardImpulseScale);
                    SetFloat(explosionResolver, "underfootUpwardImpulseScale", UnderfootUpwardImpulseScale);
                    SetFloat(explosionResolver, "underfootHighSpeedVerticalRedirect", UnderfootHighSpeedVerticalRedirect);
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
                    SetObjectReference(ballMotor, "goalShieldSet", goalShieldSet);
                    SetObjectReference(kick, "ball", ballMotor);
                    SetObjectReference(launcher, "explosionResolver", explosionResolver);
                    SetObjectReference(cameraFeedback, "player", playerMotor);
                    SetObjectReference(cameraFeedback, "targetCamera", player.GetComponentInChildren<Camera>(true));
                    SetObjectReference(cameraFeedback, "viewmodels", player.transform.Find("Head/Camera/Viewmodels").gameObject);
                    SetObjectReference(cameraFeedback, "crosshairCanvas", player.transform.Find("Head/Camera/CrosshairCanvas").gameObject);

                    SetObjectReference(arena.NorthGoal.Trigger, "ball", ballMotor);
                    SetObjectReference(arena.SouthGoal.Trigger, "ball", ballMotor);
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

                    var hud = new GameObject("DebugHUD");
                    var hudComponent = hud.AddComponent<MovementDebugHud>();
                    SetObjectReference(hudComponent, "player", playerMotor);
                    SetObjectReference(hudComponent, "ball", ballMotor);
                    SetObjectReference(hudComponent, "launcher", launcher);
                    SetObjectReference(hudComponent, "kick", kick);
                    SetObjectReference(hudComponent, "match", match);

                    new GameObject(GetBuildMarkerName(builderSignature));

                    BindSceneEnvironment(scene, arena);
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
                    MovementLabMaterialPipeline.FinalizeGeneratedMaterialPersistence();
                    NormalizeGeneratedYamlWhitespace();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }

                internal static void RegisterBuildScene()
                {
                    EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                }

                internal static PhysicsMaterial GetOrCreatePhysicMaterial()
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

                internal static void SetProjectFixedTimestep()
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

                internal static void ValidatePhysicsAndBuildSettings()
                {
                    if (Mathf.Abs(Time.fixedDeltaTime - GamePhysicsSettings.FixedDeltaTime) > 0.00001f)
                    {
                        throw new InvalidOperationException("Fixed timestep is not 60 Hz.");
                    }
                    if (Mathf.Abs(UnityEngine.Physics.gravity.y + GamePhysicsSettings.GravityMagnitude) > 0.0001f || Mathf.Abs(UnityEngine.Physics.gravity.x) > 0.0001f || Mathf.Abs(UnityEngine.Physics.gravity.z) > 0.0001f)
                    {
                        throw new InvalidOperationException("Physics gravity does not match shared GamePhysicsSettings.");
                    }
                    var scenes = EditorBuildSettings.scenes;
                    if (scenes.Length != 1 || scenes[0].path != ScenePath || !scenes[0].enabled)
                    {
                        throw new InvalidOperationException("MovementLab must be the sole enabled build scene.");
                    }
                }

                internal static void ValidateRenderPipelineSettings()
                {
                    GraphicsQualityConfigurator.Validate();
                    if (QualitySettings.GetQualityLevel() != GraphicsQualityConfigurator.HighQualityIndex)
                    {
                        throw new InvalidOperationException("MovementLab quality index must be High (index 0).");
                    }

                    var camera = Camera.main;
                    var runtime = camera != null ? camera.GetComponent<GraphicsQualityRuntime>() : null;
                    if (runtime == null || runtime.TargetCamera != camera)
                    {
                        throw new InvalidOperationException("Gameplay camera must own GraphicsQualityRuntime with self target.");
                    }

                    // Validation is inspection-only. Generation persists High/Low assets;
                    // camera state is checked without changing quality or runtime state.
                    if (camera == null || !camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData) ||
                        !camera.allowHDR || !cameraData.renderPostProcessing ||
                        cameraData.antialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing)
                    {
                        throw new InvalidOperationException("High quality camera state contract invalid.");
                    }
                }

                internal static void NormalizeGeneratedYamlWhitespace()
                {
                    // Unity emits empty serialized fields as `key: `; trim
                    // trailing spaces while preserving YAML structure/GUIDs.
                    for (var i = 0; i < GeneratedYamlAssetPaths.Length; i++)
                    {
                        NormalizeYamlFile(GeneratedYamlAssetPaths[i]);
                        NormalizeYamlFile(GeneratedYamlAssetPaths[i] + ".meta");
                    }
                    // Importer metadata is protected input/output state. Do
                    // not canonicalize it during bake/preview lifecycle work;
                    // Unity may rewrite empty fields and create false drift.
                }

                internal static void NormalizeYamlFile(string path)
                {
                    if (!File.Exists(path)) return;
                    if (!HasRecognizedTextYamlHeader(path))
                    {
                        Debug.LogWarning("Skipping YAML whitespace normalization for binary or unrecognized file: " + path);
                        return;
                    }

                    var source = File.ReadAllText(path, Encoding.UTF8);
                    // Empty YAML sequence entries are serialized as an indented
                    // `- ` line. Keep that marker (and its indentation) intact;
                    // trimming it makes Unity's YAML parser reject the document.
                    var normalized = Regex.Replace(source, @"^(?![ \t]*-[ \t]*\r?$)(.*?)[ \t]+(?=\r?$)", match => match.Groups[1].Value, RegexOptions.Multiline);
                    if (!string.Equals(source, normalized, StringComparison.Ordinal))
                    {
                        File.WriteAllText(path, normalized, new UTF8Encoding(false));
                    }
                }

                private static bool HasRecognizedTextYamlHeader(string path)
                {
                    var yamlHeader = new byte[] { (byte)'%', (byte)'Y', (byte)'A', (byte)'M', (byte)'L' };
                    var metaHeader = new byte[]
                    {
                        (byte)'f', (byte)'i', (byte)'l', (byte)'e', (byte)'F', (byte)'o', (byte)'r', (byte)'m',
                        (byte)'a', (byte)'t', (byte)'V', (byte)'e', (byte)'r', (byte)'s', (byte)'i', (byte)'o',
                        (byte)'n', (byte)':'
                    };
                    var probe = new byte[3 + metaHeader.Length];
                    int bytesRead;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        bytesRead = 0;
                        while (bytesRead < probe.Length)
                        {
                            var read = stream.Read(probe, bytesRead, probe.Length - bytesRead);
                            if (read == 0) break;
                            bytesRead += read;
                        }
                    }

                    var offset = bytesRead >= 3 && probe[0] == 0xef && probe[1] == 0xbb && probe[2] == 0xbf ? 3 : 0;
                    if (HasBytePrefix(probe, bytesRead, offset, yamlHeader)) return true;
                    return path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                           HasBytePrefix(probe, bytesRead, offset, metaHeader);
                }

                private static bool HasBytePrefix(byte[] source, int sourceLength, int offset, byte[] expected)
                {
                    if (sourceLength - offset < expected.Length) return false;
                    for (var i = 0; i < expected.Length; i++)
                    {
                        if (source[offset + i] != expected[i]) return false;
                    }
                    return true;
                }

                internal static void EnsureFolders()
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
                    EnsureFolder(LightingPath);
                }

                internal static void EnsureFolder(string path)
                {
                    if (!AssetDatabase.IsValidFolder(path))
                    {
                        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
                    }
                }

    }
}
