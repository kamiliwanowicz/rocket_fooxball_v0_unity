using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    internal enum MovementLabStage
    {
        Importer,
        MaterialPrefab,
        GameplayScene,
        Quality,
        Lighting,
        BakedOutput
    }

    internal sealed class MovementLabStageProbe
    {
        private readonly HashSet<MovementLabStage> staleStages;
        private readonly Dictionary<MovementLabStage, string> staleReasonMap;

        internal MovementLabStageProbe(IEnumerable<MovementLabStage> staleStages,
            IDictionary<MovementLabStage, string> staleReasons,
            string lightingInputDigest,
            MovementLabGeneratedState state,
            MovementLabManifestReadStatus manifestReadStatus)
        {
            this.staleStages = new HashSet<MovementLabStage>(staleStages ?? Array.Empty<MovementLabStage>());
            staleReasonMap = new Dictionary<MovementLabStage, string>(staleReasons ?? new Dictionary<MovementLabStage, string>());
            LightingInputDigest = lightingInputDigest ?? string.Empty;
            CurrentState = state;
            ManifestReadStatus = manifestReadStatus;
            ManifestStatus = this.staleStages.Count == 0 && manifestReadStatus == MovementLabManifestReadStatus.Current ? "current" : "stale";
        }

        internal string LightingInputDigest { get; }
        internal MovementLabGeneratedState CurrentState { get; }
        internal MovementLabManifestReadStatus ManifestReadStatus { get; }
        internal string ManifestStatus { get; }
        internal bool IsStale(MovementLabStage stage) => staleStages.Contains(stage);
        internal MovementLabStage[] StaleStages => staleStages.OrderBy(stage => (int)stage).ToArray();
        internal string[] StaleReasons => StaleStages.Select(stage => staleReasonMap.TryGetValue(stage, out var reason) ? reason : "stale").ToArray();

        internal bool TryGetStaleReason(MovementLabStage stage, out string reason) => staleReasonMap.TryGetValue(stage, out reason);

        internal bool IsRawOutputDriftOnly(MovementLabStage stage)
        {
            if (!TryGetStaleReason(stage, out var reason) || string.IsNullOrWhiteSpace(reason)) return false;
            var tokens = reason.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length > 0 && tokens.All(token => token.StartsWith("changed:", StringComparison.Ordinal));
        }
    }

    internal static class MovementLabStageGraph
    {
        private const string ImporterContract = "importer-contract:3";
        private const string MaterialContract = "material-prefab-contract:11";
        // GameplayScene owns TagManager/DynamicsManager layer and collision
        // repair, plus six-slot roster wiring.
        private const string GameplayContract = "gameplay-scene-contract:15";
        // T5 adds the persisted Iteration profile and its URP assets.
        private const string QualityContract = "quality-contract:3";
        private const string LightingContract = "lighting-contract:5";
        private const string BakedContract = "baked-output-contract:5";

        // Unity can deterministically omit unused lightmap variants 2/3/4 in
        // either bake profile; keep their exact optional identities in the
        // output fingerprint without treating an intact absent pair as damage.
        private static readonly HashSet<string> OptionalBakedLightmapOutputPaths =
            new HashSet<string>(WithMetas(new[]
            {
                MovementLabContract.BakedLightingPath + "/Lightmap-2_comp_dir.png",
                MovementLabContract.BakedLightingPath + "/Lightmap-2_comp_light.exr",
                MovementLabContract.BakedLightingPath + "/Lightmap-2_comp_shadowmask.png",
                MovementLabContract.BakedLightingPath + "/Lightmap-3_comp_dir.png",
                MovementLabContract.BakedLightingPath + "/Lightmap-3_comp_light.exr",
                MovementLabContract.BakedLightingPath + "/Lightmap-3_comp_shadowmask.png",
                MovementLabContract.BakedLightingPath + "/Lightmap-4_comp_dir.png",
                MovementLabContract.BakedLightingPath + "/Lightmap-4_comp_light.exr",
                MovementLabContract.BakedLightingPath + "/Lightmap-4_comp_shadowmask.png"
            }), StringComparer.Ordinal);

        // Ordering predecessors document writer sequencing. Staleness is driven
        // only by each stage's explicit keys and digest predecessors so a
        // dynamic/prefab-only change cannot invalidate lighting by transitively
        // inheriting unrelated output bytes.
        // Matrix: runtime behavior -> no stage; gameplay wiring -> GameplayScene;
        // prefab/material/importer -> explicit upstream keys; static render/light/
        // probe state -> Lighting/Baked; quality -> Quality only.
        private static readonly StageDefinition[] Definitions =
        {
            new StageDefinition(MovementLabStage.Importer, Array.Empty<MovementLabStage>(), Array.Empty<MovementLabStage>(), ImporterContract,
                Concat(MovementLabContract.ImporterContractInputs, new[] { "Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs" }), Array.Empty<string>(),
                MovementLabContractCatalog.GeneratedImporterMetadataPaths, includeUnityVersion: true),
            new StageDefinition(MovementLabStage.MaterialPrefab, new[] { MovementLabStage.Importer }, Array.Empty<MovementLabStage>(),
                MaterialContract + ";serialized:" + MovementLabContract.SerializedContractVersion,
                Concat(new[] { "Tools/Blender/generate_retro_textures.py" }, WithMetas(new[]
                {
                    MovementLabContract.InputActionsPath,
                    MovementLabContract.ShadersPath + "/RetroToonLit.shader", MovementLabContract.ShadersPath + "/RetroParticle.shader",
                    MovementLabContract.ShadersPath + "/RetroAdditiveParticle.shader", MovementLabContract.ShadersPath + "/RetroPowerGrid.shader",
                     MovementLabContract.ShadersPath + "/RetroShield.shader", MovementLabContract.ShadersPath + "/SunnyArenaSky.shader",
                     "Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabContract.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs",
                     "Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs",
                     "Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs",
                      "Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs",
                      "Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs",
                     "Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs",
                     "Assets/_Game/Scripts/Runtime/Feedback/RocketTrailVfx.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/ArenaPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/HealthPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/ShotgunPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/AmmoPickup.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotNavigator.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs"
                     ,"Assets/_Game/Scripts/Runtime/Bots/BotController.cs"
                     ,"Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs"
                     ,"Assets/_Game/Scripts/Runtime/Movement/PlayerLook.cs"
                     ,"Assets/_Game/Scripts/Runtime/Ball/BallKick.cs"
                      ,"Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs"
                      ,"Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs"
                      ,"Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfx.cs"
                      ,"Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs"
                  })), MovementLabContract.ImportedAssetPaths,
                WithMetas(MovementLabContract.MaterialPrefabOutputs), includeUnityVersion: false),
            new StageDefinition(MovementLabStage.GameplayScene, new[] { MovementLabStage.MaterialPrefab }, Array.Empty<MovementLabStage>(),
                GameplayContract + ";serialized:" + MovementLabContract.SerializedContractVersion,
                 Concat(new[]
                 {
                      "Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs",
                      "Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs",
                      "Assets/_Game/Editor/MovementLab/MovementLabContract.cs",
                      "Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs",
                      "Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs",
                      "Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs",
                      "Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs",
                      "Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs",
                      "Assets/_Game/Scripts/Runtime/Weapons/ParticipantRelationship.cs",
                      "Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs",
                     "Assets/_Game/Scripts/Runtime/Participants/ParticipantSpawnSet.cs",
                     "Assets/_Game/Scripts/Runtime/Match/GoalTrigger.cs",
                     "Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs"
                 }, WithMetas(new[]
                 {
                     "Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs",
                     "Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs",
                     "Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs",
                     "Assets/_Game/Scripts/Runtime/Match/MatchController.cs",
                     "Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs",
                     "Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/ArenaPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/HealthPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/ShotgunPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Pickups/AmmoPickup.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotNavigator.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotController.cs",
                     "Assets/_Game/Scripts/Runtime/Bots/BotNavigationRules.cs",
                     "Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs",
                     "Assets/_Game/Scripts/Runtime/Movement/PlayerLook.cs",
                     "Assets/_Game/Scripts/Runtime/Ball/BallKick.cs",
                      "Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs"
                      ,"Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs"
                      ,"Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfxSpawner.cs"
                      ,"Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs"
                      ,"Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs"
                      ,"Assets/_Game/Scripts/Runtime/Participants/ParticipantSpawnSet.cs"
                      ,"Assets/_Game/Scripts/Runtime/Bots/BotController.cs"
                      ,"Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs"
                 })),
                new[]
                {
                    MovementLabContract.PlayerPrefabPath, MovementLabContract.BallPrefabPath,
                    MovementLabContract.RocketPrefabPath, MovementLabContract.ExplosionPrefabPath,
                    MovementLabContract.HealthPickupPrefabPath, MovementLabContract.ShotgunPickupPrefabPath,
                    MovementLabContract.AmmoPickupPrefabPath
                },
                WithAssetMetasOnly(MovementLabContract.GameplaySceneOutputs), includeUnityVersion: false),
            new StageDefinition(MovementLabStage.Quality, Array.Empty<MovementLabStage>(), Array.Empty<MovementLabStage>(), QualityContract,
                new[]
                {
                    "Packages/manifest.json", "Packages/packages-lock.json",
                    "Assets/_Game/Editor/GraphicsQualityConfigurator.cs"
                }, Array.Empty<string>(), WithAssetMetasOnly(new[]
                {
                    GraphicsQualityConfigurator.HighPipelinePath, GraphicsQualityConfigurator.HighRendererPath,
                    GraphicsQualityConfigurator.LowPipelinePath, GraphicsQualityConfigurator.LowRendererPath,
                    GraphicsQualityConfigurator.IterationPipelinePath, GraphicsQualityConfigurator.IterationRendererPath,
                    GraphicsQualityConfigurator.QualitySettingsPath, GraphicsQualityConfigurator.ProjectSettingsPath
                }), includeUnityVersion: false),
            new StageDefinition(MovementLabStage.Lighting,
                new[] { MovementLabStage.MaterialPrefab, MovementLabStage.GameplayScene, MovementLabStage.Quality }, Array.Empty<MovementLabStage>(), LightingContract,
                new[]
                {
                    MovementLabContract.LightingSettingsPath, MovementLabContract.LightingSettingsPath + ".meta",
                    MovementLabLightingProfiles.DevelopmentSettingsPath, MovementLabLightingProfiles.DevelopmentSettingsPath + ".meta",
                     MovementLabContract.VolumeProfilePath, MovementLabContract.VolumeProfilePath + ".meta",
                     "Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs",
                     "Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs"
                 }, Array.Empty<string>(),
                WithMetas(new[] { MovementLabContract.LightingSettingsPath, MovementLabContract.VolumeProfilePath }), includeUnityVersion: true),
            new StageDefinition(MovementLabStage.BakedOutput, new[] { MovementLabStage.Lighting }, new[] { MovementLabStage.Lighting }, BakedContract,
                new[]
                {
                    "Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs",
                    MovementLabContract.BakedLightingPath + "/LightingData.asset",
                    MovementLabContract.LightingManifestPath
                }, Array.Empty<string>(), WithMetas(MovementLabContract.BakedOutputPaths), includeUnityVersion: false)
        };

        internal static readonly MovementLabStage[] NonLightingGenerationOrder =
        {
            MovementLabStage.Quality, MovementLabStage.Importer, MovementLabStage.MaterialPrefab, MovementLabStage.GameplayScene
        };
        internal static MovementLabStageProbe Probe(bool stopOnOutputDrift, bool allowBakedOutputDrift = false)
        {
            var accumulator = new MovementLabValidationAccumulator();
            var probe = Probe(stopOnOutputDrift, allowBakedOutputDrift, accumulator);
            accumulator.ThrowIfAny("MovementLab generated-state probe");
            return probe;
        }

        internal static MovementLabStageProbe Probe(bool stopOnOutputDrift, bool allowBakedOutputDrift,
            MovementLabValidationAccumulator accumulator)
        {
            if (accumulator == null) throw new System.ArgumentNullException(nameof(accumulator));
            // Changed-byte drift remains informational. Missing-output drift is
            // blocking when requested. All stage families scan before terminal throw.
            _ = allowBakedOutputDrift;
            var manifestRead = MovementLabManifestStore.Read();
            if (manifestRead.Status == MovementLabManifestReadStatus.Unreadable)
            {
                throw new InvalidOperationException("MovementLab manifest corruption: " + manifestRead.Error);
            }

            var manifest = manifestRead.State;
            var schemaCurrent = manifestRead.Status == MovementLabManifestReadStatus.Current;
            if (schemaCurrent && !MovementLabManifestStore.IsCurrentAndReadable(manifest))
            {
                throw new InvalidOperationException("MovementLab manifest is structurally invalid; refusing to overwrite trusted state.");
            }

            var currentRecords = CaptureLiveRecords();
            var stale = new HashSet<MovementLabStage>();
            var reasons = new Dictionary<MovementLabStage, string>();

            for (var i = 0; i < Definitions.Length; i++)
            {
                var definition = Definitions[i];
                var current = currentRecords[definition.Stage];
                var prior = schemaCurrent ? manifest.Find(definition.Stage.ToString()) : null;
                var stageReasons = new List<string>();
                current.priorInputDigest = prior?.inputDigest ?? string.Empty;
                current.priorOutputDigest = prior?.outputDigest ?? string.Empty;

                if (prior == null)
                {
                    stageReasons.Add("missing-record");
                }
                else
                {
                    var drift = FindOutputDrift(definition.Stage, prior.outputs, current.outputs, definition.Outputs);
                    var identityViolations = FindOutputIdentityViolations(definition, prior, current);
                    if (prior.schemaVersion != MovementLabContract.ManifestSchemaVersion) stageReasons.Add("schema-mismatch");
                    if (!string.Equals(prior.contractVersion, current.contractVersion, StringComparison.Ordinal)) stageReasons.Add("contract-changed");
                    if (definition.IncludeUnityVersion && !string.Equals(prior.unityVersion, current.unityVersion, StringComparison.Ordinal)) stageReasons.Add("unity-version-changed");
                    if (!string.Equals(prior.repositoryInputDigest, current.repositoryInputDigest, StringComparison.Ordinal)) stageReasons.Add("input-source-changed");
                    if (!string.Equals(prior.dependencyDigest, current.dependencyDigest, StringComparison.Ordinal)) stageReasons.Add("dependency-state-changed");
                    if (!string.Equals(prior.inputDigest, current.inputDigest, StringComparison.Ordinal)) stageReasons.Add("input-changed");
                    if (!SequenceEqual(prior.predecessorDigests, current.predecessorDigests)) stageReasons.Add("digest-predecessor-changed");
                    if (!string.Equals(prior.profile ?? string.Empty, current.profile ?? string.Empty, StringComparison.Ordinal)) stageReasons.Add("profile-changed");

                    if (drift.Count > 0)
                    {
                        // Raw owned-output bytes are observed drift, not a
                        // builder hard-stop. Stage remains stale and next
                        // successful write refreshes recorded hashes. Semantic
                        // validation still enforces required assets, metadata,
                        // GUID/local IDs, and persisted references.

                        for (var driftIndex = 0; driftIndex < drift.Count; driftIndex++) stageReasons.Add(drift[driftIndex]);
                        if (stopOnOutputDrift)
                        {
                            for (var driftIndex = 0; driftIndex < drift.Count; driftIndex++)
                            {
                                var driftToken = drift[driftIndex];
                                if (driftToken.StartsWith("missing:", StringComparison.Ordinal))
                                    accumulator.Add("stage-output", definition.Stage + ":" + driftToken, driftToken);
                            }
                        }
                    }

                    for (var identityIndex = 0; identityIndex < identityViolations.Count; identityIndex++)
                        stageReasons.Add(identityViolations[identityIndex]);

                    for (var identityIndex = 0; identityIndex < identityViolations.Count; identityIndex++)
                    {
                        var identity = identityViolations[identityIndex];
                        accumulator.Add("stage-identity", definition.Stage + ":" + identity, identity);
                    }
                }

                if (stageReasons.Count > 0)
                {
                    stale.Add(definition.Stage);
                    reasons[definition.Stage] = string.Join(";", stageReasons.Distinct(StringComparer.Ordinal));
                    current.staleReasons = stageReasons.Distinct(StringComparer.Ordinal).ToArray();
                }
            }

            var lightingDigest = currentRecords.TryGetValue(MovementLabStage.Lighting, out var lighting)
                ? lighting.inputDigest : string.Empty;
            var currentState = BuildState(currentRecords.Values, manifestStatus: stale.Count == 0 && schemaCurrent ? "current" : "stale", stale, reasons);
            return new MovementLabStageProbe(stale, reasons, lightingDigest, currentState, manifestRead.Status);
        }

        internal static MovementLabGeneratedState CaptureBakedState()
        {
            var state = CaptureStateThrough(MovementLabStage.BakedOutput);
            EnsureTrustedOutputIdentityBeforeManifestRefresh();
            return state;
        }

        private static void EnsureTrustedOutputIdentityBeforeManifestRefresh()
        {
            var manifestRead = MovementLabManifestStore.Read();
            if (manifestRead.Status != MovementLabManifestReadStatus.Current ||
                !MovementLabManifestStore.IsCurrentAndReadable(manifestRead.State)) return;

            var live = CaptureLiveRecords();
            for (var i = 0; i < Definitions.Length; i++)
            {
                var definition = Definitions[i];
                var prior = manifestRead.State.Find(definition.Stage.ToString());
                if (prior == null || !live.TryGetValue(definition.Stage, out var current)) continue;
                var drift = FindOutputDrift(definition.Stage, prior.outputs, current.outputs, definition.Outputs);
                var identityViolations = FindOutputIdentityViolations(definition, prior, current);
                if (IsBlockingOutputDrift(drift, identityViolations))
                {
                    throw new InvalidOperationException("MovementLab trusted output identity violation; refusing to refresh manifest: " +
                        string.Join(";", drift.Concat(identityViolations).Distinct(StringComparer.Ordinal).ToArray()));
                }
            }
        }

        internal static MovementLabGeneratedState MergeStageRecord(MovementLabStage stage)
        {
            var priorRead = MovementLabManifestStore.Read();
            var live = CaptureLiveRecords();
            var records = new Dictionary<MovementLabStage, MovementLabStageRecord>();
            var priorIsTrusted = priorRead.Status == MovementLabManifestReadStatus.Current && MovementLabManifestStore.IsCurrentAndReadable(priorRead.State);
            if (priorIsTrusted && priorRead.State.stages != null)
            {
                for (var i = 0; i < priorRead.State.stages.Length; i++)
                {
                    var record = priorRead.State.stages[i];
                    if (record == null || !Enum.TryParse(record.stage, out MovementLabStage parsed)) continue;
                    records[parsed] = record;
                }
            }
            else
            {
                // Migration starts with a complete live snapshot so every
                // intermediate atomic write still satisfies the manifest schema.
                foreach (var item in live) records[item.Key] = item.Value;
            }

            // Replace only the stage that completed. Downstream lighting/baked
            // records remain trusted until Probe proves their explicit key stale.
            records[stage] = live[stage];
            return BuildState(records.Values, "stale", null, null);
        }

        internal static MovementLabGeneratedState MarkCurrent(MovementLabStageProbe probe)
        {
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            var state = probe.CurrentState ?? CaptureBakedState();
            state.manifestStatus = probe.ManifestStatus;
            state.staleStages = probe.StaleStages.Select(stage => stage.ToString()).ToArray();
            state.staleReasons = probe.StaleReasons;
            return state;
        }

        internal static string[] GetOwnedOutputs(MovementLabStage stage)
        {
            var definition = Definitions.First(item => item.Stage == stage);
            return definition.Outputs.ToArray();
        }

        internal static MovementLabStageRecord CaptureCurrentRecord(MovementLabStage stage)
        {
            var records = CaptureLiveRecords();
            return records[stage];
        }

        internal static void RunCanonicalSceneInvariantSelfCheck()
        {
            RunRepositoryInputDigestInvariantSelfCheck();
            var absolute = MovementLabManifestStore.ResolveProjectPath(MovementLabContract.ScenePath);
            if (!File.Exists(absolute)) return;
            var source = File.ReadAllText(absolute, Encoding.UTF8);
            var canonical = CanonicalizeGameplayScene(source);
            var parsed = ParseSceneDocuments(source, out var preamble, out var trailingNewline);
            if (parsed.Count < 2) return;

            var reordered = parsed.AsEnumerable().Reverse().ToArray();
            var reorderedHash = HashBytes(Encoding.UTF8.GetBytes(CanonicalizeGameplayScene(ComposeSceneDocuments(preamble, reordered, trailingNewline))));
            var canonicalHash = HashBytes(Encoding.UTF8.GetBytes(canonical));
            if (!string.Equals(reorderedHash, canonicalHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Canonical scene self-check failed: document order changed digest.");
            }

            var lightmapIndex = Array.FindIndex(parsed.ToArray(), document => document.IndexOf("LightmapSettings:", StringComparison.Ordinal) >= 0);
            if (lightmapIndex >= 0)
            {
                var lightmapMutated = parsed.ToArray();
                lightmapMutated[lightmapIndex] += "\n  m_CanonicalSelfCheck: 1";
                var lightmapHash = HashBytes(Encoding.UTF8.GetBytes(CanonicalizeGameplayScene(ComposeSceneDocuments(preamble, lightmapMutated, trailingNewline))));
                if (!string.Equals(lightmapHash, canonicalHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Canonical scene self-check failed: LightmapSettings mutation changed digest.");
                }
            }

            var gameplayMutated = parsed.ToArray();
            gameplayMutated[0] += "\n  m_CanonicalSelfCheckFileId: 9001";
            var gameplayHash = HashBytes(Encoding.UTF8.GetBytes(CanonicalizeGameplayScene(ComposeSceneDocuments(preamble, gameplayMutated, trailingNewline))));
            if (string.Equals(gameplayHash, canonicalHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Canonical scene self-check failed: non-lighting content mutation was ignored.");
            }
        }

        private static MovementLabGeneratedState CaptureStateThrough(MovementLabStage lastStage)
        {
            var records = CaptureLiveRecords().Values.Where(record => (int)Enum.Parse(typeof(MovementLabStage), record.stage) <= (int)lastStage);
            return BuildState(records, "current", null, null);
        }

        private static Dictionary<MovementLabStage, MovementLabStageRecord> CaptureLiveRecords()
        {
            var records = new Dictionary<MovementLabStage, MovementLabStageRecord>();
            for (var i = 0; i < Definitions.Length; i++)
            {
                var definition = Definitions[i];
                records[definition.Stage] = CaptureRecord(definition, GetPredecessorDigests(definition, records));
            }
            return records;
        }

        private static MovementLabGeneratedState BuildState(IEnumerable<MovementLabStageRecord> sourceRecords, string manifestStatus,
            IEnumerable<MovementLabStage> staleStages, IDictionary<MovementLabStage, string> staleReasons)
        {
            var records = sourceRecords.Where(record => record != null)
                .OrderBy(record => (int)Enum.Parse(typeof(MovementLabStage), record.stage)).ToArray();
            var pathDigests = new Dictionary<string, MovementLabPathDigest>(StringComparer.Ordinal);
            for (var recordIndex = 0; recordIndex < records.Length; recordIndex++)
            {
                var outputs = records[recordIndex].outputs ?? Array.Empty<MovementLabPathDigest>();
                for (var outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
                {
                    var output = outputs[outputIndex];
                    // Later baked ownership supersedes canonical gameplay ownership
                    // for the shared scene path in the top-level fingerprint union.
                    pathDigests[output.path] = output;
                }
            }

            var orderedPaths = pathDigests.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var hashes = orderedPaths.Select(path =>
            {
                var digest = pathDigests[path];
                return digest.missing ? HashText(new[] { "missing:" + path }) : digest.digest;
            }).ToArray();
            var stale = (staleStages ?? Array.Empty<MovementLabStage>()).OrderBy(stage => (int)stage).ToArray();
            var staleReasonValues = stale.Select(stage => staleReasons != null && staleReasons.TryGetValue(stage, out var reason) ? reason : "stale").ToArray();
            var lighting = records.FirstOrDefault(record => string.Equals(record.stage, MovementLabStage.Lighting.ToString(), StringComparison.Ordinal));
            return new MovementLabGeneratedState
            {
                schemaVersion = MovementLabContract.ManifestSchemaVersion,
                gitSha = MovementLabManifestStore.GetCurrentGitSha(),
                manifestStatus = manifestStatus,
                sourceSignature = HashText(records.Select(record => record.stage + ":" + record.inputDigest)),
                generatedOutputFingerprint = HashText(records.Select(record => record.stage + ":" + record.outputDigest)),
                unityVersion = Application.unityVersion,
                lightingInputDigest = lighting?.inputDigest ?? string.Empty,
                bakedProfile = ReadBakedProfile(),
                staleStages = stale.Select(stage => stage.ToString()).ToArray(),
                staleReasons = staleReasonValues,
                fingerprintPaths = orderedPaths,
                fingerprintHashes = hashes,
                stages = records
            };
        }

        private static MovementLabStageRecord CaptureRecord(StageDefinition definition, string[] predecessorDigests)
        {
            var repositoryParts = new List<string> { "contract:" + definition.ContractVersion };
            AddRepositoryDigests(repositoryParts, definition.RepositoryInputs);
            if (definition.Stage == MovementLabStage.Lighting)
            {
                repositoryParts.AddRange(CaptureLightingSceneState(includeBakedRendererBindings: false));
            }
            if (definition.Stage == MovementLabStage.BakedOutput)
            {
                repositoryParts.AddRange(CaptureLightingSceneState(includeBakedRendererBindings: true));
            }
            if (definition.IncludeUnityVersion) repositoryParts.Add("unity:" + Application.unityVersion);
            var dependencyParts = new List<string>();
            AddDependencyDigests(dependencyParts, definition.ObservedDependencyInputs);
            var inputParts = new List<string>(repositoryParts);
            inputParts.AddRange(dependencyParts);
            for (var i = 0; i < predecessorDigests.Length; i++) inputParts.Add("predecessor:" + predecessorDigests[i]);

            var outputs = CaptureOutputs(definition.OwnedOutputs, definition.Stage);
            return new MovementLabStageRecord
            {
                stage = definition.Stage.ToString(),
                schemaVersion = MovementLabContract.ManifestSchemaVersion,
                contractVersion = definition.ContractVersion,
                // Record the observed editor version for every stage; Probe only
                // compares it when the stage opts into Unity-version invalidation.
                unityVersion = Application.unityVersion,
                inputDigest = HashText(inputParts),
                repositoryInputDigest = HashText(repositoryParts),
                dependencyDigest = HashText(dependencyParts),
                outputDigest = HashText(outputs.Select(output => output.path + ":" + (output.missing ? "missing" : output.digest))),
                predecessorDigests = predecessorDigests,
                profile = definition.Stage == MovementLabStage.BakedOutput ? ReadBakedProfile() : string.Empty,
                outputs = outputs
            };
        }

        private static MovementLabPathDigest[] CaptureOutputs(string[] paths, MovementLabStage stage)
        {
            var normalizedPaths = paths.Select(MovementLabManifestStore.NormalizeRepositoryPath)
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var result = new MovementLabPathDigest[normalizedPaths.Length];
            for (var i = 0; i < normalizedPaths.Length; i++)
            {
                var path = normalizedPaths[i];
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                var missing = !File.Exists(absolute);
                result[i] = new MovementLabPathDigest
                {
                    path = path,
                    missing = missing,
                    digest = missing ? string.Empty : HashOutputFile(path, absolute, stage)
                };
            }
            return result;
        }

        private static List<string> FindOutputIdentityViolations(StageDefinition definition,
            MovementLabStageRecord prior, MovementLabStageRecord current)
        {
            var violations = new List<string>();
            if (definition == null || prior == null) return violations;

            var contractPaths = new HashSet<string>(definition.Outputs ?? Array.Empty<string>(), StringComparer.Ordinal);
            var priorByPath = (prior.outputs ?? Array.Empty<MovementLabPathDigest>())
                .Where(item => item != null && !string.IsNullOrEmpty(item.path))
                .ToDictionary(item => item.path, StringComparer.Ordinal);
            var currentByPath = (current?.outputs ?? Array.Empty<MovementLabPathDigest>())
                .Where(item => item != null && !string.IsNullOrEmpty(item.path))
                .ToDictionary(item => item.path, StringComparer.Ordinal);
            var checkedPairs = new HashSet<string>(StringComparer.Ordinal);

            foreach (var path in priorByPath.Keys.OrderBy(value => value, StringComparer.Ordinal))
            {
                // Prior paths removed by current contract are authorized output-set changes.
                if (!contractPaths.Contains(path)) continue;
                priorByPath.TryGetValue(path, out var priorValue);
                currentByPath.TryGetValue(path, out var currentValue);

                if (currentValue == null)
                {
                    violations.Add("identity:missing-output:" + path);
                    continue;
                }

                if (path.EndsWith(".meta", StringComparison.Ordinal))
                {
                    var stableMissingPair = IsOptionalMissingBakedOutput(definition.Stage, path, currentValue);
                    // Legacy byte digests migrate once; stable GUID identities remain protected.
                    if (!stableMissingPair &&
                        priorValue != null && !priorValue.missing && currentValue.missing)
                    {
                        violations.Add("identity:missing-meta:" + path);
                    }
                    else if (!stableMissingPair && priorValue != null && !priorValue.missing && !currentValue.missing)
                    {
                        var isLegacyToGuidMigration = IsHexDigest(priorValue, 64) && IsHexDigest(currentValue, 32);
                        var isStableGuidPair = IsHexDigest(priorValue, 32) && IsHexDigest(currentValue, 32);
                        if (isStableGuidPair && !string.Equals(priorValue.digest, currentValue.digest, StringComparison.Ordinal))
                        {
                            violations.Add("identity:changed-meta:" + path);
                        }
                        else if (!isLegacyToGuidMigration && !isStableGuidPair)
                        {
                            violations.Add("identity:invalid-meta-digest:" + path);
                        }
                    }

                    var assetPath = path.Substring(0, path.Length - ".meta".Length);
                    if (checkedPairs.Add(assetPath))
                    {
                        var pairViolation = FindAssetMetaPairViolation(assetPath, definition.Stage);
                        if (!string.IsNullOrEmpty(pairViolation)) violations.Add(pairViolation);
                    }
                }
                else if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    contractPaths.Contains(path + ".meta") && checkedPairs.Add(path))
                {
                    var pairViolation = FindAssetMetaPairViolation(path, definition.Stage);
                    if (!string.IsNullOrEmpty(pairViolation)) violations.Add(pairViolation);
                }
            }

            return violations.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string FindAssetMetaPairViolation(string assetPath, MovementLabStage stage)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)) return null;
            var assetAbsolute = MovementLabManifestStore.ResolveProjectPath(assetPath);
            var metaPath = assetPath + ".meta";
            var metaAbsolute = MovementLabManifestStore.ResolveProjectPath(metaPath);
            var currentMeta = File.Exists(metaAbsolute);
            var currentAssetExists = File.Exists(assetAbsolute);

            if (stage == MovementLabStage.BakedOutput &&
                !currentAssetExists && !currentMeta &&
                OptionalBakedLightmapOutputPaths.Contains(assetPath) &&
                OptionalBakedLightmapOutputPaths.Contains(metaPath)) return null;

            if (!currentAssetExists || !currentMeta)
                return "identity:broken-pair:" + assetPath;

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var metaGuid = ReadMetaGuid(metaAbsolute);
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(metaGuid) || !string.Equals(guid, metaGuid, StringComparison.Ordinal))
                return "identity:guid-meta-mismatch:" + assetPath;

            return null;
        }

        private static bool IsHexDigest(MovementLabPathDigest value, int length)
        {
            if (value == null || value.missing || string.IsNullOrEmpty(value.digest) || value.digest.Length != length) return false;
            for (var i = 0; i < value.digest.Length; i++)
            {
                var character = value.digest[i];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') ||
                    (character >= 'A' && character <= 'F'))) return false;
            }
            return true;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            if (!File.Exists(metaPath)) return string.Empty;
            return File.ReadLines(metaPath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("guid:", StringComparison.Ordinal))
                .Select(line => line.Substring("guid:".Length).Trim())
                .FirstOrDefault() ?? string.Empty;
        }

        private static bool IsBlockingOutputDrift(IEnumerable<string> drift, IEnumerable<string> identityViolations)
        {
            return (drift ?? Array.Empty<string>()).Any(token => token.StartsWith("missing:", StringComparison.Ordinal)) ||
                (identityViolations ?? Array.Empty<string>()).Any();
        }

        private static string HashOutputFile(string repositoryPath, string absolutePath, MovementLabStage stage)
        {
            if (repositoryPath.EndsWith(".meta", StringComparison.Ordinal))
            {
                return ReadMetaGuid(absolutePath);
            }

            // Gameplay owns scene content except bake-owned LightmapSettings;
            // BakedOutput owns the raw post-bake scene and LightingData.
            if (stage == MovementLabStage.GameplayScene && string.Equals(repositoryPath, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                return HashGameplaySceneWithoutLightmapSettings(absolutePath);
            }
            if (stage == MovementLabStage.BakedOutput && string.Equals(repositoryPath, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                return HashBakedSceneLightingDocument(absolutePath);
            }

            return HashFile(absolutePath);
        }

        private static string HashBakedSceneLightingDocument(string path)
        {
            var source = File.ReadAllText(path, Encoding.UTF8);
            var documents = ParseSceneDocuments(source, out _, out _);
            var lightingDocument = documents.FirstOrDefault(document => document.StartsWith("--- !u!157 ", StringComparison.Ordinal) &&
                document.IndexOf("LightmapSettings:", StringComparison.Ordinal) >= 0);
            return HashBytes(Encoding.UTF8.GetBytes(NormalizeLineEndings(lightingDocument ?? "missing-lightmap-settings")));
        }

        private static string HashGameplaySceneWithoutLightmapSettings(string path)
        {
            return HashBytes(Encoding.UTF8.GetBytes(CanonicalizeGameplayScene(File.ReadAllText(path, Encoding.UTF8))));
        }

        private static string CanonicalizeGameplayScene(string source)
        {
            var normalized = NormalizeLineEndings(source);
            var documents = ParseSceneDocuments(normalized, out var preamble, out var trailingNewline);
            documents = documents.Where(document => document.IndexOf("LightmapSettings:", StringComparison.Ordinal) < 0 ||
                !document.StartsWith("--- !u!157 ", StringComparison.Ordinal)).ToList();
            documents.Sort(StringComparer.Ordinal);
            return ComposeSceneDocuments(preamble, documents.ToArray(), trailingNewline);
        }

        private static List<string> ParseSceneDocuments(string source, out string preamble, out bool trailingNewline)
        {
            var normalized = NormalizeLineEndings(source);
            var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None);
            trailingNewline = normalized.EndsWith("\n", StringComparison.Ordinal);
            var contentLineCount = lines.Length - (trailingNewline ? 1 : 0);
            var firstDocument = 0;
            while (firstDocument < contentLineCount && !IsYamlDocumentHeader(lines[firstDocument])) firstDocument++;
            preamble = string.Join("\n", lines, 0, firstDocument);
            var documents = new List<string>();
            var index = firstDocument;
            while (index < contentLineCount)
            {
                var next = index + 1;
                while (next < contentLineCount && !IsYamlDocumentHeader(lines[next])) next++;
                documents.Add(string.Join("\n", lines, index, next - index));
                index = next;
            }
            return documents;
        }

        private static string ComposeSceneDocuments(string preamble, string[] documents, bool trailingNewline)
        {
            var canonical = new StringBuilder();
            canonical.Append(preamble ?? string.Empty);
            if (documents != null && documents.Length > 0)
            {
                if (canonical.Length > 0) canonical.Append('\n');
                canonical.Append(string.Join("\n", documents));
            }
            if (trailingNewline) canonical.Append('\n');
            return canonical.ToString();
        }

        private static bool IsYamlDocumentHeader(string line) => line.StartsWith("--- !u!", StringComparison.Ordinal);

        private static string NormalizeLineEndings(string value) => (value ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");

        private static void RunRepositoryInputDigestInvariantSelfCheck()
        {
            var lf = Encoding.UTF8.GetBytes("first\nsecond\n");
            var crlf = Encoding.UTF8.GetBytes("first\r\nsecond\r\n");
            var changed = Encoding.UTF8.GetBytes("first\nchanged\n");
            if (!string.Equals(HashRepositoryInputBytes(lf), HashRepositoryInputBytes(crlf), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Repository input digest self-check failed: line endings changed digest.");
            }
            if (string.Equals(HashRepositoryInputBytes(lf), HashRepositoryInputBytes(changed), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Repository input digest self-check failed: content mutation was ignored.");
            }
        }

        private static void AddRepositoryDigests(List<string> parts, string[] paths)
        {
            var expanded = ExpandRepositoryPaths(paths);
            foreach (var path in expanded.Select(MovementLabManifestStore.NormalizeRepositoryPath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                parts.Add("file:" + path + ":" + (File.Exists(absolute) ? HashRepositoryInputFile(absolute) : "missing"));
            }
        }

        private static string HashRepositoryInputFile(string path) => HashRepositoryInputBytes(File.ReadAllBytes(path));

        private static string HashRepositoryInputBytes(byte[] bytes)
        {
            bytes = bytes ?? Array.Empty<byte>();
            // Git treats NUL-containing inputs as binary; keep their exact bytes
            // while canonicalizing checkout-specific line endings for text.
            if (Array.IndexOf(bytes, (byte)0) >= 0 || Array.IndexOf(bytes, (byte)'\r') < 0) return HashBytes(bytes);

            var normalized = new List<byte>(bytes.Length);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\r')
                {
                    normalized.Add((byte)'\n');
                    if (i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') i++;
                }
                else
                {
                    normalized.Add(bytes[i]);
                }
            }
            return HashBytes(normalized.ToArray());
        }

        private static IEnumerable<string> ExpandRepositoryPaths(string[] paths)
        {
            for (var i = 0; i < (paths ?? Array.Empty<string>()).Length; i++)
            {
                var path = paths[i];
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                if (Directory.Exists(absolute))
                {
                    foreach (var file in Directory.GetFiles(absolute, "*", SearchOption.AllDirectories)
                        .Select(file => file.Replace('\\', '/'))
                        .Select(file => file.Substring(MovementLabContractCatalog.ResolveProjectRoot().FullName.Replace('\\', '/').TrimEnd('/').Length + 1))
                        .OrderBy(file => file, StringComparer.Ordinal))
                    {
                        yield return file;
                    }
                }
                else
                {
                    yield return path;
                }
            }
        }

        private static void AddDependencyDigests(List<string> parts, string[] paths)
        {
            foreach (var path in (paths ?? Array.Empty<string>()).Select(MovementLabManifestStore.NormalizeRepositoryPath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                var digest = File.Exists(absolute) ? AssetDatabase.GetAssetDependencyHash(path).ToString() : "missing";
                parts.Add("asset:" + path + ":" + digest);
            }
        }

        private static IEnumerable<string> CaptureLightingSceneState(bool includeBakedRendererBindings)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MovementLabContract.ScenePath) == null)
            {
                return new[] { "lighting-scene:missing" };
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            }

            var parts = new List<string>();
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(renderer => renderer != null && renderer.gameObject.scene == scene && IsLightingStatic(renderer.gameObject))
                .OrderBy(renderer => GetHierarchyPath(renderer.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                var rendererPart = "renderer:" + GetHierarchyPath(renderer.transform) + ":global=" + PersistentObjectIdentity(renderer.gameObject) +
                    ":component=" + PersistentObjectIdentity(renderer) + ":prefab=" + PrefabSourceIdentity(renderer.gameObject) +
                    ":" + TransformDigest(renderer.transform) +
                    ":static=" + (int)GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) +
                    ":mesh=" + AssetDependencyIdentity(mesh) +
                    ":materials=" + string.Join(",", renderer.sharedMaterials.Select(AssetDependencyIdentity)) +
                    ":shadow=" + renderer.shadowCastingMode + ":receive=" + renderer.receiveShadows +
                    ":lightProbe=" + renderer.lightProbeUsage + ":reflectionProbe=" + renderer.reflectionProbeUsage +
                    (includeBakedRendererBindings
                        ? ":lightmapIndex=" + renderer.lightmapIndex + ":realtimeLightmapIndex=" + renderer.realtimeLightmapIndex +
                          ":lightmapScaleOffset=" + Vector4Digest(renderer.lightmapScaleOffset) +
                          ":realtimeLightmapScaleOffset=" + Vector4Digest(renderer.realtimeLightmapScaleOffset)
                        : string.Empty);
                parts.Add(rendererPart);
            }

            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .Where(light => light != null && light.gameObject.scene == scene)
                .OrderBy(light => GetHierarchyPath(light.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                parts.Add("light:" + GetHierarchyPath(light.transform) + ":global=" + PersistentObjectIdentity(light.gameObject) +
                    ":component=" + PersistentObjectIdentity(light) + ":prefab=" + PrefabSourceIdentity(light.gameObject) + ":" + TransformDigest(light.transform) +
                    ":type=" + light.type + ":color=" + ColorDigest(light.color) + ":intensity=" + F(light.intensity) +
                    ":range=" + F(light.range) + ":spot=" + F(light.spotAngle) + ":shadows=" + light.shadows +
                    ":shadowStrength=" + F(light.shadowStrength) + ":shadowBias=" + F(light.shadowBias) + ":shadowNormalBias=" + F(light.shadowNormalBias) +
                    ":bake=" + light.lightmapBakeType + ":culling=" + light.cullingMask +
                    ":urp=" + PublicAdditionalLightDigest(light.GetComponent<UniversalAdditionalLightData>()));
            }

            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)
                .Where(probe => probe != null && probe.gameObject.scene == scene)
                .OrderBy(probe => GetHierarchyPath(probe.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < probes.Length; i++)
            {
                var probe = probes[i];
                parts.Add("reflection-probe:" + GetHierarchyPath(probe.transform) + ":global=" + PersistentObjectIdentity(probe.gameObject) +
                    ":component=" + PersistentObjectIdentity(probe) + ":prefab=" + PrefabSourceIdentity(probe.gameObject) + ":" + TransformDigest(probe.transform) +
                    ":center=" + VectorDigest(probe.center) + ":size=" + VectorDigest(probe.size) +
                    ":resolution=" + probe.resolution + ":mode=" + probe.mode + ":importance=" + probe.importance +
                    ":box=" + probe.boxProjection + ":blend=" + F(probe.blendDistance) + ":culling=" + probe.cullingMask +
                    ":hdr=" + probe.hdr + ":intensity=" + F(probe.intensity) + ":near=" + F(probe.nearClipPlane) +
                    ":far=" + F(probe.farClipPlane));
            }

            var probeGroups = UnityEngine.Object.FindObjectsByType<LightProbeGroup>(FindObjectsSortMode.None)
                .Where(group => group != null && group.gameObject.scene == scene)
                .OrderBy(group => GetHierarchyPath(group.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < probeGroups.Length; i++)
            {
                parts.Add("light-probe-group:" + GetHierarchyPath(probeGroups[i].transform) + ":global=" + PersistentObjectIdentity(probeGroups[i].gameObject) +
                    ":component=" + PersistentObjectIdentity(probeGroups[i]) + ":prefab=" + PrefabSourceIdentity(probeGroups[i].gameObject) + ":" + TransformDigest(probeGroups[i].transform) + ":" +
                    string.Join(";", (probeGroups[i].probePositions ?? Array.Empty<Vector3>()).Select(VectorDigest)));
            }

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)
                .Where(volume => volume != null && volume.gameObject.scene == scene)
                .OrderBy(volume => GetHierarchyPath(volume.transform), StringComparer.Ordinal).ToArray();
            for (var i = 0; i < volumes.Length; i++)
            {
                var volume = volumes[i];
                var profile = volume.sharedProfile;
                var components = profile == null ? "missing" : string.Join(";", profile.components.Where(component => component != null)
                    .Select(component => component.GetType().FullName + ":" + AssetIdentity(component)));
                parts.Add("volume:" + GetHierarchyPath(volume.transform) + ":global=" + PersistentObjectIdentity(volume.gameObject) +
                    ":component=" + PersistentObjectIdentity(volume) + ":prefab=" + PrefabSourceIdentity(volume.gameObject) + ":" + TransformDigest(volume.transform) +
                    ":isGlobal=" + volume.isGlobal + ":blend=" + F(volume.blendDistance) + ":weight=" + F(volume.weight) +
                    ":priority=" + F(volume.priority) + ":profile=" + AssetIdentity(profile) + ":components=" + components);
            }

            parts.Add("render-settings:sun=" + PersistentObjectIdentity(RenderSettings.sun) + ":sky=" + AssetDependencyIdentity(RenderSettings.skybox) + ":ambientMode=" + RenderSettings.ambientMode +
                ":ambientIntensity=" + F(RenderSettings.ambientIntensity) + ":fog=" + RenderSettings.fog +
                ":fogColor=" + ColorDigest(RenderSettings.fogColor) + ":fogStart=" + F(RenderSettings.fogStartDistance) +
                ":fogEnd=" + F(RenderSettings.fogEndDistance) + ":reflectionMode=" + RenderSettings.defaultReflectionMode +
                ":reflectionResolution=" + RenderSettings.defaultReflectionResolution + ":reflectionBounces=" + RenderSettings.reflectionBounces +
                ":reflectionIntensity=" + F(RenderSettings.reflectionIntensity));
            return parts;
        }

        private static string PublicAdditionalLightDigest(UniversalAdditionalLightData data)
        {
            if (data == null) return "missing";
            var parts = new List<string>();
            var flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var field in typeof(UniversalAdditionalLightData).GetFields(flags).OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (field.IsSpecialName) continue;
                var value = field.GetValue(data);
                if (value == null || value.GetType().IsPrimitive || value is Enum || value is string || value is Vector4 || value is Vector3 || value is Color)
                {
                    parts.Add(field.Name + "=" + (value ?? "null"));
                }
            }
            foreach (var property in typeof(UniversalAdditionalLightData).GetProperties(flags).Where(property => property.CanRead && property.GetIndexParameters().Length == 0).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                try
                {
                    var value = property.GetValue(data, null);
                    if (value == null || value.GetType().IsPrimitive || value is Enum || value is string || value is Vector4 || value is Vector3 || value is Color)
                    {
                        parts.Add(property.Name + "=" + (value ?? "null"));
                    }
                }
                catch
                {
                    parts.Add(property.Name + "=<unreadable>");
                }
            }
            return string.Join(",", parts);
        }

        private static string AssetDependencyIdentity(UnityEngine.Object asset)
        {
            var identity = AssetIdentity(asset);
            var path = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                identity += ":dependency=" + AssetDatabase.GetAssetDependencyHash(path);
            }
            return identity;
        }

        private static string AssetIdentity(UnityEngine.Object asset)
        {
            if (asset == null) return "null";
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out var guid, out long localId)) return guid + ":" + localId;
            if (asset is Mesh mesh)
            {
                return "builtin-mesh:" + mesh.name + ":vertices=" + mesh.vertexCount + ":submeshes=" + mesh.subMeshCount + ":bounds=" + VectorDigest(mesh.bounds.size);
            }
            return asset.GetType().FullName + ":" + asset.name;
        }

        private static string PersistentObjectIdentity(UnityEngine.Object value)
        {
            if (value == null) return "null";
            try
            {
                var global = GlobalObjectId.GetGlobalObjectIdSlow(value);
                if (global.identifierType != 0) return global.ToString();
            }
            catch
            {
                // Unsaved transient objects can lack a GlobalObjectId. The
                // fallback still records a stable serialized asset identity.
            }

            return AssetIdentity(value);
        }

        private static string PrefabSourceIdentity(GameObject gameObject)
        {
            if (gameObject == null) return "null";
            try
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                return source == null ? "none" : AssetIdentity(source);
            }
            catch
            {
                return "unavailable";
            }
        }

        private static bool IsLightingStatic(GameObject gameObject)
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(gameObject);
            return (flags & (StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic)) != 0;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent) names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private static string TransformDigest(Transform transform) => VectorDigest(transform.localPosition) + ":" + QuaternionDigest(transform.localRotation) + ":" + VectorDigest(transform.localScale);
        private static string VectorDigest(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
        private static string Vector4Digest(Vector4 value) => F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);
        private static string QuaternionDigest(Quaternion value) => F(value.x) + "," + F(value.y) + "," + F(value.z) + "," + F(value.w);
        private static string ColorDigest(Color value) => F(value.r) + "," + F(value.g) + "," + F(value.b) + "," + F(value.a);
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static string[] GetPredecessorDigests(StageDefinition definition, Dictionary<MovementLabStage, MovementLabStageRecord> records)
        {
            var result = new string[definition.DigestPredecessors.Length];
            for (var i = 0; i < definition.DigestPredecessors.Length; i++)
            {
                var predecessor = definition.DigestPredecessors[i];
                result[i] = predecessor + ":" + records[predecessor].outputDigest;
            }
            return result;
        }

        private static List<string> FindOutputDrift(MovementLabStage stage, MovementLabPathDigest[] expected,
            MovementLabPathDigest[] actual, string[] contractOutputs)
        {
            var drift = new List<string>();
            var expectedByPath = (expected ?? Array.Empty<MovementLabPathDigest>()).Where(item => item != null).ToDictionary(item => item.path, StringComparer.Ordinal);
            var actualByPath = (actual ?? Array.Empty<MovementLabPathDigest>()).Where(item => item != null).ToDictionary(item => item.path, StringComparer.Ordinal);
            var contractPathSet = new HashSet<string>(contractOutputs ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (var path in expectedByPath.Keys.Union(actualByPath.Keys, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (expectedByPath.ContainsKey(path) && !contractPathSet.Contains(path)) continue;
                expectedByPath.TryGetValue(path, out var oldValue);
                actualByPath.TryGetValue(path, out var newValue);
                if (oldValue == null) continue; // Contract-authorized new output path; stage contract/input staleness drives writer.
                if (newValue == null || newValue.missing)
                {
                    if (IsOptionalMissingBakedOutput(stage, path, newValue)) continue;
                    drift.Add("missing:" + path);
                }
                else if (oldValue == null || oldValue.missing || !string.Equals(oldValue.digest, newValue.digest, StringComparison.Ordinal)) drift.Add("changed:" + path);
            }
            return drift;
        }

        private static bool IsOptionalMissingBakedOutput(MovementLabStage stage, string path, MovementLabPathDigest current)
        {
            var isBakedOutput = stage == MovementLabStage.BakedOutput;
            var currentMissing = current != null && current.missing;
            var isAllowlistedPath = !string.IsNullOrEmpty(path) && OptionalBakedLightmapOutputPaths.Contains(path);
            return isBakedOutput && currentMissing && isAllowlistedPath;
        }

        private static bool SequenceEqual(string[] left, string[] right) => (left ?? Array.Empty<string>()).SequenceEqual(right ?? Array.Empty<string>(), StringComparer.Ordinal);

        private static string ReadBakedProfile()
        {
            var absolute = MovementLabManifestStore.ResolveProjectPath(MovementLabContract.LightingManifestPath);
            if (!File.Exists(absolute)) return "none";
            var text = File.ReadAllText(absolute, Encoding.UTF8);
            try
            {
                var typed = JsonUtility.FromJson<MovementLabLightingManifestState>(text);
                if (typed != null)
                {
                    if (!string.IsNullOrWhiteSpace(typed.profileId)) return typed.profileId;
                    if (!string.IsNullOrWhiteSpace(typed.profileTag)) return typed.profileTag;
                }
            }
            catch
            {
                // Probe remains able to report a profile for legacy manifests;
                // typed validation owns corruption failure at the bake gate.
            }
            var match = Regex.Match(text, "\\\"(?:profile|tag|profileId)\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["value"].Value : "production";
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return ToHex(sha.ComputeHash(stream));
        }

        private static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes));
        }

        private static string HashText(IEnumerable<string> values)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", (values ?? Array.Empty<string>()).OrderBy(value => value, StringComparer.Ordinal)) + "\n");
                return ToHex(sha.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();

        private static string[] WithMetas(string[] paths)
        {
            var result = new List<string>();
            for (var i = 0; i < (paths ?? Array.Empty<string>()).Length; i++)
            {
                result.Add(paths[i]);
                result.Add(paths[i] + ".meta");
            }
            return result.ToArray();
        }

        private static string[] WithAssetMetasOnly(string[] paths)
        {
            var result = new List<string>();
            for (var i = 0; i < (paths ?? Array.Empty<string>()).Length; i++)
            {
                result.Add(paths[i]);
                if (paths[i].StartsWith("Assets/", StringComparison.Ordinal)) result.Add(paths[i] + ".meta");
            }
            return result.ToArray();
        }

        private static string[] Concat(string[] first, string[] second)
        {
            return (first ?? Array.Empty<string>()).Concat(second ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
        }

        private sealed class StageDefinition
        {
            internal readonly MovementLabStage Stage;
            internal readonly MovementLabStage[] OrderingPredecessors;
            internal readonly MovementLabStage[] DigestPredecessors;
            internal readonly string ContractVersion;
            internal readonly string[] RepositoryInputs;
            internal readonly string[] DependencyInputs;
            internal readonly string[] ObservedDependencyInputs;
            internal readonly string[] Outputs;
            internal readonly string[] OwnedOutputs;
            internal readonly bool IncludeUnityVersion;

            internal StageDefinition(MovementLabStage stage, MovementLabStage[] orderingPredecessors, MovementLabStage[] digestPredecessors,
                string contractVersion, string[] repositoryInputs, string[] dependencyInputs, string[] outputs, bool includeUnityVersion)
            {
                Stage = stage;
                OrderingPredecessors = (orderingPredecessors ?? Array.Empty<MovementLabStage>()).Clone() as MovementLabStage[];
                DigestPredecessors = (digestPredecessors ?? Array.Empty<MovementLabStage>()).Clone() as MovementLabStage[];
                ContractVersion = contractVersion;
                RepositoryInputs = (repositoryInputs ?? Array.Empty<string>()).Clone() as string[];
                DependencyInputs = (dependencyInputs ?? Array.Empty<string>()).Clone() as string[];
                ObservedDependencyInputs = DependencyInputs;
                Outputs = (outputs ?? Array.Empty<string>()).Clone() as string[];
                OwnedOutputs = Outputs;
                IncludeUnityVersion = includeUnityVersion;
            }
        }
    }
}
