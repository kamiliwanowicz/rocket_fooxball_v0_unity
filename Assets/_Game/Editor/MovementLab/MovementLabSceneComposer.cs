using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using static RocketFooxball.Editor.MovementLabMaterialPipeline;
using static RocketFooxball.Editor.MovementLabPrefabPipeline;
using static RocketFooxball.Editor.MovementLabArenaPipeline;
using static RocketFooxball.Editor.MovementLabLightingPipeline;
namespace RocketFooxball.Editor
{
    internal static partial class MovementLabSceneComposer
    {
                internal const float MatchDuration = 300f;
                internal const float GoalSummaryDuration = 3f;
                internal const float KickoffCountdownDuration = 3f;

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
                    var teamBlueMaterial = GetOrCreateRetroMaterial("TeamBlue", new Color(0.08f, 0.35f, 1.00f, 1f), null, Vector2.one);
                    var teamRedMaterial = GetOrCreateRetroMaterial("TeamRed", new Color(1.00f, 0.12f, 0.10f, 1f), null, Vector2.one);
                    var healthPickupMaterial = GetOrCreateHealthPickupMaterial();
                    var ammoShellMaterial = GetOrCreateAmmoShellMaterial();
                    GetOrCreateShieldMaterial("TeamBlueShield", new Color(0.10f, 0.50f, 1.00f, 1f), new Color(0.30f, 0.90f, 1.00f, 1f));
                    GetOrCreateShieldMaterial("TeamRedShield", new Color(1.00f, 0.22f, 0.20f, 1f), new Color(1.00f, 0.55f, 0.45f, 1f));
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
                    BuildHealthPickupPrefab(healthPickupMaterial);
                    BuildShotgunPickupPrefab(LoadRequiredAsset<Material>(ShotgunMetalMaterialPath), LoadRequiredAsset<Material>(ShotgunDarkMaterialPath),
                        LoadRequiredAsset<Material>(ShotgunAccentMaterialPath), teamBlueMaterial, teamRedMaterial);
                    BuildAmmoPickupPrefab(ammoShellMaterial, teamBlueMaterial, teamRedMaterial);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(HealthPickupPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(ShotgunPickupPrefabPath, ImportAssetOptions.ForceSynchronousImport);
                    AssetDatabase.ImportAsset(AmmoPickupPrefabPath, ImportAssetOptions.ForceSynchronousImport);
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
                    var teamBlueMaterial = LoadRequiredAsset<Material>(TeamBlueMaterialPath);
                    var teamRedMaterial = LoadRequiredAsset<Material>(TeamRedMaterialPath);
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
                        EnsureGameplayLayersAndCollisionMatrix();
                        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                        var defaultCamera = Camera.main;
                        if (defaultCamera != null) UnityEngine.Object.DestroyImmediate(defaultCamera.gameObject);
                        var arena = BuildArena(floorMaterial, wallMaterial, markingMaterial, frameMaterial, shieldMaterial, ballSurface, arenaPrimaryMaterial, arenaTrimMaterial, arenaHazardMaterial, arenaGlowMaterial, gridCeilingMaterial, gridLongWallMaterial, gridEndWallMaterial, shieldRedMaterial, shieldBlueMaterial, teamBlueMaterial, teamRedMaterial);
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
                         SetFloat(explosionResolver, "enemyRocketImpulseMultiplier", 1f);
                        SetFloat(explosionResolver, "cameraFeedbackScale", 0.8f);
                        var participantStates = BuildParticipantRoster(playerPrefab);
                        var localParticipant = participantStates[0];
                        var player = localParticipant.gameObject;
                        var spawnSet = BuildParticipantSpawnSet(arena);
                        var ball = (GameObject)PrefabUtility.InstantiatePrefab(ballPrefab);
                        ball.name = "Ball";
                        ball.transform.SetPositionAndRotation(new Vector3(0f, BallSpawnHeight, 0f), Quaternion.identity);
                        var playerMotor = localParticipant.Motor;
                        var playerInput = localParticipant.Input;
                        var playerLook = localParticipant.Look;
                        var cameraFeedback = localParticipant.CameraFeedback;
                        var launcher = localParticipant.Launcher;
                        var kick = localParticipant.Kick;
                        var ballMotor = ball.GetComponent<BallMotor>();
                        var ballBody = ball.GetComponent<Rigidbody>();
                        var ballCollider = ball.GetComponent<Collider>();
                        SetObjectReference(ballMotor, "body", ballBody);
                        SetObjectReference(ballMotor, "ballCollider", ballCollider);
                        SetObjectArray(ballMotor, "participants", participantStates.Cast<UnityEngine.Object>().ToArray());
                        SetObjectReference(ballMotor, "goalShieldSet", goalShieldSet);
                        for (var participantIndex = 0; participantIndex < participantStates.Length; participantIndex++)
                        {
                            var participant = participantStates[participantIndex];
                            SetObjectReference(participant.Kick, "ball", ballMotor);
                            SetObjectReference(participant.Launcher, "explosionResolver", explosionResolver);
                            SetObjectReference(participant.Shotgun, "ball", ballMotor);
                            SetObjectReference(participant.Shotgun, "ownerParticipant", participant);
                            SetLayerMask(participant.Shotgun, "hitMask", ~(1 << LayerMask.NameToLayer(MovementLabContract.ProjectilesLayerName)));
                        }
                        SetObjectReference(arena.NorthGoal.Trigger, "ball", ballMotor);
                        SetObjectReference(arena.SouthGoal.Trigger, "ball", ballMotor);
                        SetObjectReference(arena.NorthGoal.Trigger, "planeReference", arena.NorthGoal.Root.transform);
                        SetObjectReference(arena.SouthGoal.Trigger, "planeReference", arena.SouthGoal.Root.transform);
                        SetEnum(arena.NorthGoal.Trigger, "defendingTeam", "Red");
                        SetEnum(arena.SouthGoal.Trigger, "defendingTeam", "Blue");
                        var matchObject = new GameObject("MatchController");
                        var match = matchObject.AddComponent<MatchController>();
                        SetObjectArray(match, "participants", participantStates.Cast<UnityEngine.Object>().ToArray());
                        SetObjectReference(match, "localParticipant", localParticipant);
                        SetObjectReference(match, "spawnSet", spawnSet);
                        SetObjectReference(match, "cameraFeedback", cameraFeedback);
                        SetObjectReference(match, "ball", ballMotor);
                        SetObjectReference(match, "northGoal", arena.NorthGoal.Trigger);
                        SetObjectReference(match, "southGoal", arena.SouthGoal.Trigger);
                         SetFloat(match, "matchDuration", MatchDuration);
                         SetFloat(match, "goalCelebrationOrbitDuration", GoalSummaryDuration);
                        SetFloat(match, "kickoffCountdownDuration", KickoffCountdownDuration);
                         SetVector3(match, "ballResetPosition", new Vector3(0f, BallSpawnHeight, 0f));
                         SetVector3(match, "resetLookTarget", Vector3.zero);
                         WirePresentationSceneReferences(participantStates, localParticipant, match);
                        var healthPickups = BuildHealthPickupInstances(LoadRequiredAsset<GameObject>(HealthPickupPrefabPath), match);
                        var shotgunPickups = BuildShotgunPickupInstances(LoadRequiredAsset<GameObject>(ShotgunPickupPrefabPath), match);
                        var ammoPickups = BuildAmmoPickupInstances(LoadRequiredAsset<GameObject>(AmmoPickupPrefabPath), match);
                        var pickups = healthPickups.Cast<ArenaPickup>().Concat(shotgunPickups).Concat(ammoPickups).ToArray();
                        MovementLabBotPipeline.ComposeScene(participantStates, match, ballMotor, pickups,
                            arena.NorthGoal.Trigger, arena.SouthGoal.Trigger, arena.NorthGoal.Shield, arena.SouthGoal.Shield);
                        var hud = new GameObject("DebugHUD");
                        var hudComponent = hud.AddComponent<MovementDebugHud>();
                        SetObjectReference(hudComponent, "player", playerMotor);
                        SetObjectReference(hudComponent, "ball", ballMotor);
                        SetObjectReference(hudComponent, "launcher", launcher);
                        SetObjectReference(hudComponent, "kick", kick);
                        SetObjectReference(hudComponent, "match", match);
                        var matchHudObject = new GameObject("MatchHUD");
                        var matchHud = matchHudObject.AddComponent<MatchHud>();
                        SetObjectReference(matchHud, "match", match);
                        SetObjectReference(matchHud, "localParticipant", localParticipant);
                        SetObjectReference(matchHud, "input", playerInput);
                        new GameObject(GetBuildMarkerName(builderSignature));
                        BindSceneEnvironment(scene, arena);
                        EditorSceneManager.SaveScene(scene, ScenePath);
                        RestoreLightmapSettingsDocument(preservedLightmapSettings);
                        SaveGameplayProjectSettings();
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

                private static HealthPickup[] BuildHealthPickupInstances(GameObject healthPickupPrefab, MatchController match)
                {
                    if (healthPickupPrefab == null) throw new InvalidOperationException("Health pickup prefab is required for scene composition.");
                    if (match == null) throw new InvalidOperationException("MatchController is required for health pickup scene wiring.");
                    var root = new GameObject(HealthPickupsRootName);
                    var result = new HealthPickup[HealthPickupSpawns.Length];
                    for (var i = 0; i < HealthPickupSpawns.Length; i++)
                    {
                        var definition = HealthPickupSpawns[i];
                        var instance = PrefabUtility.InstantiatePrefab(healthPickupPrefab) as GameObject;
                        if (instance == null) throw new InvalidOperationException("Failed to instantiate health pickup prefab: " + definition.Name);
                        instance.name = definition.Name;
                        instance.transform.SetParent(root.transform, false);
                        instance.transform.SetPositionAndRotation(definition.Position, definition.Rotation);
                        var pickup = instance.GetComponent<HealthPickup>();
                        if (pickup == null) throw new InvalidOperationException("Health pickup prefab has no HealthPickup component: " + definition.Name);
                        SetObjectReference(pickup, "match", match);
                        result[i] = pickup;
                    }
                    return result;
                }

                private static ShotgunPickup[] BuildShotgunPickupInstances(GameObject shotgunPickupPrefab, MatchController match)
                {
                    if (shotgunPickupPrefab == null) throw new InvalidOperationException("Shotgun pickup prefab is required for scene composition.");
                    if (match == null) throw new InvalidOperationException("MatchController is required for shotgun pickup scene wiring.");
                    var root = new GameObject(ShotgunPickupsRootName);
                    var result = new ShotgunPickup[ShotgunPickupSpawns.Length];
                    for (var i = 0; i < ShotgunPickupSpawns.Length; i++)
                    {
                        var definition = ShotgunPickupSpawns[i];
                        var instance = PrefabUtility.InstantiatePrefab(shotgunPickupPrefab) as GameObject;
                        if (instance == null) throw new InvalidOperationException("Failed to instantiate shotgun pickup prefab: " + definition.Name);
                        instance.name = definition.Name;
                        instance.transform.SetParent(root.transform, false);
                        instance.transform.SetPositionAndRotation(definition.Position, definition.Rotation);
                        var pickup = instance.GetComponent<ShotgunPickup>();
                        if (pickup == null) throw new InvalidOperationException("Shotgun pickup prefab has no ShotgunPickup component: " + definition.Name);
                        SetObjectReference(pickup, "match", match);
                        result[i] = pickup;
                    }
                    return result;
                }

                private static AmmoPickup[] BuildAmmoPickupInstances(GameObject ammoPickupPrefab, MatchController match)
                {
                    if (ammoPickupPrefab == null) throw new InvalidOperationException("Ammo pickup prefab is required for scene composition.");
                    if (match == null) throw new InvalidOperationException("MatchController is required for ammo pickup scene wiring.");
                    var root = new GameObject(AmmoPickupsRootName);
                    var result = new AmmoPickup[AmmoPickupSpawns.Length];
                    for (var i = 0; i < AmmoPickupSpawns.Length; i++)
                    {
                        var definition = AmmoPickupSpawns[i];
                        var instance = PrefabUtility.InstantiatePrefab(ammoPickupPrefab) as GameObject;
                        if (instance == null) throw new InvalidOperationException("Failed to instantiate ammo pickup prefab: " + definition.Name);
                        instance.name = definition.Name;
                        instance.transform.SetParent(root.transform, false);
                        instance.transform.SetPositionAndRotation(definition.Position, definition.Rotation);
                        var pickup = instance.GetComponent<AmmoPickup>();
                        if (pickup == null) throw new InvalidOperationException("Ammo pickup prefab has no AmmoPickup component: " + definition.Name);
                        SetObjectReference(pickup, "match", match);
                        result[i] = pickup;
                    }
                    return result;
                }

                private static ParticipantState[] BuildParticipantRoster(GameObject playerPrefab)
                {
                    if (playerPrefab == null) throw new InvalidOperationException("Player prefab is required for six-slot roster composition.");
                    if (ParticipantSlots == null || ParticipantSlots.Length != 6) throw new InvalidOperationException("Participant slot catalog must contain exactly six entries.");

                    var roster = new ParticipantState[ParticipantSlots.Length];
                    var hiddenLayer = EnsureLocalPlayerHiddenLayer();
                    for (var i = 0; i < ParticipantSlots.Length; i++)
                    {
                        var slot = ParticipantSlots[i];
                        var instance = PrefabUtility.InstantiatePrefab(playerPrefab) as GameObject;
                        if (instance == null) throw new InvalidOperationException("Unable to instantiate Player prefab for slot " + slot.SlotId + ".");
                        instance.name = slot.DisplayName;
                        instance.transform.SetPositionAndRotation(slot.Position, slot.Rotation);
                        var state = instance.GetComponent<ParticipantState>();
                        if (state == null) throw new InvalidOperationException("Player prefab missing ParticipantState for slot " + slot.SlotId + ".");

                        SetInteger(state, "slotId", slot.SlotId);
                        SetString(state, "displayName", slot.DisplayName);
                        SetEnum(state, "team", slot.Team == ParticipantTeam.Blue ? "Blue" : "Red");
                         SetBool(state, "localParticipant", slot.IsLocal);
                         SetFloat(state, "deathWait", slot.IsLocal ? LocalRespawnDelay : BotRespawnDelay);
                        SetObjectReference(state.Presentation, "participant", state);
                        SetObjectReference(state.CameraFeedback, "participant", state);
                        SetObjectReference(state.Launcher, "ownerParticipant", state);
                        state.Presentation.ConfigureSlot(state);
                        state.Presentation.SetLocalMode(slot.IsLocal);
                        state.Presentation.SetAlive(true);
                        state.Presentation.SetImmune(false);
                        if (state.Input != null) state.Input.enabled = slot.IsLocal;
                        if (state.Look != null) state.Look.enabled = slot.IsLocal;
                        if (state.CameraFeedback != null) state.CameraFeedback.enabled = slot.IsLocal;
                        var camera = instance.transform.Find("Head/Camera")?.GetComponent<Camera>();
                        var listener = instance.transform.Find("Head/Camera")?.GetComponent<AudioListener>();
                        if (camera != null) camera.enabled = slot.IsLocal;
                        if (listener != null) listener.enabled = slot.IsLocal;
                        var viewmodels = instance.transform.Find("Head/Camera/Viewmodels");
                        if (viewmodels != null) viewmodels.gameObject.SetActive(slot.IsLocal);
                        var crosshair = instance.transform.Find("Head/Camera/CrosshairCanvas");
                        if (crosshair != null) crosshair.gameObject.SetActive(slot.IsLocal);
                        var worldVisual = instance.transform.Find("WorldVisual");
                        if (worldVisual != null) SetLayerRecursively(worldVisual.gameObject, slot.IsLocal ? hiddenLayer : 0);
                        roster[i] = state;
                    }
                     return roster;
                 }

                 private static void WirePresentationSceneReferences(ParticipantState[] roster, ParticipantState localParticipant, MatchController match)
                 {
                     if (roster == null || localParticipant == null || match == null)
                         throw new InvalidOperationException("Presentation scene wiring requires the complete roster, local participant, and match.");

                     var localCamera = localParticipant.transform.Find("Head/Camera")?.GetComponent<Camera>();
                     if (localCamera == null)
                         throw new InvalidOperationException("Presentation scene wiring requires the local participant camera.");

                     for (var i = 0; i < roster.Length; i++)
                     {
                         var participant = roster[i];
                         if (participant == null || participant.Presentation == null)
                             throw new InvalidOperationException("Presentation scene wiring requires every participant presentation.");

                         var isEnemy = participant.Team != localParticipant.Team;
                         SetObjectReference(participant.Presentation, "localParticipant", localParticipant);
                         SetObjectReference(participant.Presentation, "match", match);
                         SetObjectReference(participant.Presentation, "nicknameCamera", localCamera);
                         SetBool(participant.Presentation, "showNickname", isEnemy);
                         SetBool(participant.Presentation, "spawnCorpseOnDeath", isEnemy);
                         var nameplate = participant.transform.Find("Nameplate")?.GetComponent<TextMesh>();
                         if (nameplate == null)
                             throw new InvalidOperationException("Presentation scene wiring requires Nameplate TextMesh: " + participant.DisplayName);
                         nameplate.text = participant.DisplayName;
                         participant.Presentation.ConfigureSlot(participant);
                     }
                 }

                private static ParticipantSpawnSet BuildParticipantSpawnSet(ArenaBuild arena)
                {
                    if (arena == null || arena.NorthGoal == null || arena.SouthGoal == null)
                        throw new InvalidOperationException("Arena goals are required for participant spawn set.");

                    var root = new GameObject("ParticipantSpawnSet");
                    var spawnSet = root.AddComponent<ParticipantSpawnSet>();
                    var blue = new Transform[3];
                    var red = new Transform[3];
                    var teamBlueMaterial = LoadRequiredAsset<Material>(TeamBlueMaterialPath);
                    var teamRedMaterial = LoadRequiredAsset<Material>(TeamRedMaterialPath);
                    for (var i = 0; i < 3; i++)
                    {
                        var blueSlot = ParticipantSlots[i];
                        var blueObject = new GameObject("BlueSpawn_" + i.ToString());
                        blueObject.transform.SetParent(root.transform, false);
                        blueObject.transform.SetPositionAndRotation(blueSlot.Position, blueSlot.Rotation);
                        var blueCue = CreateShapeCue("BlueCircleCue", false, teamBlueMaterial, new Vector3(0f, 0.02f, 0f));
                        blueCue.transform.SetParent(blueObject.transform, false);
                        blueCue.transform.localScale = Vector3.one * 1.4f;
                        blue[i] = blueObject.transform;

                        var redSlot = ParticipantSlots[i + 3];
                        var redObject = new GameObject("RedSpawn_" + i.ToString());
                        redObject.transform.SetParent(root.transform, false);
                        redObject.transform.SetPositionAndRotation(redSlot.Position, redSlot.Rotation);
                        var redCue = CreateShapeCue("RedTriangleCue", true, teamRedMaterial, new Vector3(0f, 0.02f, 0f));
                        redCue.transform.SetParent(redObject.transform, false);
                        redCue.transform.localScale = Vector3.one * 1.4f;
                        red[i] = redObject.transform;
                    }
                    SetObjectArray(spawnSet, "blueCandidates", blue.Cast<UnityEngine.Object>().ToArray());
                    SetObjectArray(spawnSet, "redCandidates", red.Cast<UnityEngine.Object>().ToArray());
                    SetObjectReference(spawnSet, "blueEnemyGoal", arena.NorthGoal.Root.transform);
                    SetObjectReference(spawnSet, "redEnemyGoal", arena.SouthGoal.Root.transform);
                    var participantsLayer = LayerMask.NameToLayer("Participants");
                    var projectilesLayer = LayerMask.NameToLayer("Projectiles");
                    if (participantsLayer < 0 || projectilesLayer < 0) throw new InvalidOperationException("Participants and Projectiles layers must exist before spawn-set composition.");
                    SetLayerMask(spawnSet, "visibilityMask", ~(1 << participantsLayer | 1 << projectilesLayer));
                     SetFloat(spawnSet, "eyeHeight", 2.4f);
                    SetFloat(spawnSet, "occupiedRadius", 2f);
                    SetFloat(spawnSet, "ballDistanceWeight", 1f);
                    SetFloat(spawnSet, "enemyGoalDistanceWeight", 0.5f);
                    SetFloat(spawnSet, "nearestEnemyDistanceWeight", 1f);
                    SetFloat(spawnSet, "noVisibleEnemyBonus", 4f);
                    SetFloat(spawnSet, "visibleEnemyCountPenalty", 2f);
                    SetFloat(spawnSet, "occupiedFallbackPenalty", 8f);
                    SetFloat(spawnSet, "ballDistanceCap", 30f);
                    SetFloat(spawnSet, "enemyGoalDistanceCap", 30f);
                    SetFloat(spawnSet, "enemyDistanceCap", 30f);
                    return spawnSet;
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

                // GameplayScene owns TagManager/DynamicsManager repair. This
                // runs before scene composition so stale project settings are
                // fixed in same authoritative rebuild as scene wiring.
                internal static void EnsureGameplayLayersAndCollisionMatrix()
                {
                    var participantsLayer = EnsureGameplayLayer(MovementLabContract.ParticipantsLayerName);
                    var projectilesLayer = EnsureGameplayLayer(MovementLabContract.ProjectilesLayerName);
                    var hiddenLayer = EnsureGameplayLayer(MovementLabContract.LocalPlayerHiddenLayerName);
                    if (participantsLayer < 0 || projectilesLayer < 0 || hiddenLayer < 0)
                    {
                        throw new InvalidOperationException("Gameplay layers could not be resolved.");
                    }

                    var settings = AssetDatabase.LoadAllAssetsAtPath(MovementLabContract.DynamicsManagerPath);
                    if (settings.Length == 0) throw new InvalidOperationException("DynamicsManager.asset unavailable.");

                    // Unity 6000.5 does not expose m_LayerCollisionMatrix as a
                    // writable SerializedProperty. Use the supported API, then
                    // persist its PhysicsManager changes with other assets.
                    UnityEngine.Physics.IgnoreLayerCollision(participantsLayer, participantsLayer, false);
                    UnityEngine.Physics.IgnoreLayerCollision(participantsLayer, projectilesLayer, false);
                    UnityEngine.Physics.IgnoreLayerCollision(projectilesLayer, projectilesLayer, false);
                    EditorUtility.SetDirty(settings[0]);
                    AssetDatabase.SaveAssets();
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
