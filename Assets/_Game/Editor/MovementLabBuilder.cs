using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    /// <summary>Public command facade. Domain ownership stays in pipeline modules.</summary>
    public static class MovementLabBuilder
    {
        internal const string ProductionBakeSkippedMarker = "[MovementLab] production bake skipped: lighting inputs current (digest ";
        private const string PrepareProductionArgument = "-movementLabPrepareProduction";
        private const string LightingEvidenceArgument = "-movementLabEvidencePath";

        [Serializable]
        private sealed class LightingAuthorIdempotencyEvidence
        {
            public int schemaVersion = 1;
            public MovementLabLightingPipeline.VolumeProfileAuthorSnapshot before;
            public MovementLabLightingPipeline.VolumeProfileAuthorSnapshot first;
            public MovementLabLightingPipeline.VolumeProfileAuthorSnapshot second;
            public MovementLabLightingPipeline.VolumeProfileAuthorSnapshot reloaded;
            public bool sameHashes;
            public bool sameIdentities;
            public bool exactComponentCounts;
            public bool observableResult;
            public bool pass;
            public string result;
            public string error;
        }

        [MenuItem("Rocket Fooxball/Build Movement Lab")]
        public static void BuildMovementLab()
        {
            MovementLabFastModeSession.RestoreIfActive();
            AssembleMovementLab();
            ValidateMovementLabPreBake();
            // Probe without throwing so a development bake can be rejected by
            // its typed profile tag before normal production stale checks.
            var probe = MovementLabStageGraph.Probe(false);
            MovementLabStageRunner.WriteProbeIfRequested(probe);
            if (!string.Equals(probe.CurrentState?.bakedProfile ?? "none", MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab production lighting is not valid (profile is " + (probe.CurrentState?.bakedProfile ?? "none") + "). Run 'Rocket Fooxball/Bake Movement Lab Lighting' explicitly.");
            }
            if (probe.IsStale(MovementLabStage.Lighting) || probe.IsStale(MovementLabStage.BakedOutput))
            {
                Debug.LogWarning("Rocket Fooxball Movement Lab stale lighting is informational; semantic validation and bake remain usable.");
            }
        }

        [MenuItem("Rocket Fooxball/Assemble Movement Lab")]
        public static void AssembleMovementLab()
        {
            MovementLabFastModeSession.RestoreIfActive();
            var probe = MovementLabStageRunner.RunSelective();
            MovementLabStageRunner.WriteProbeIfRequested(probe);
            Debug.Log(probe.StaleStages.Length == 0
                ? "Rocket Fooxball Movement Lab assembly reused generated state: " + MovementLabContract.ScenePath
                : "Rocket Fooxball Movement Lab assembled without lighting bake: " + MovementLabContract.ScenePath);
        }

        // Narrow weapon import command. This is intentionally separate from
        // the full assembly pipeline so weapon source art cannot touch
        // unrelated importer metas or generated outputs.
        public static void ImportWeaponVisualAssets()
        {
            MovementLabImportPipeline.ImportWeaponVisualAssets();
            Debug.Log("Rocket Fooxball weapon visual assets imported: " + MovementLabContractCatalog.WeaponModelPath);
        }

        // Compatibility entry point retained for existing T3 automation.
        public static void ImportLauncherVisualAssets() => ImportWeaponVisualAssets();

        [MenuItem("Rocket Fooxball/Authorize Movement Lab Manifest Migration")]
        public static void AuthorizeMovementLabManifestMigration()
        {
            var path = MovementLabManifestStore.AuthorizeManifestMigration();
            Debug.Log("Rocket Fooxball Movement Lab manifest migration authorized for current Git SHA: " + path);
        }

        [MenuItem("Rocket Fooxball/Validate Movement Lab Pre-Bake")]
        public static void ValidateMovementLabPreBake()
        {
            MovementLabFastModeSession.RestoreIfActive();
            // Gate owns semantic validation, stale-stage checks, and pass-record write.
            var passPath = MovementLabPreBakeGate.ValidateAndWritePassRecord();
            Debug.Log("Rocket Fooxball Movement Lab pre-bake gate passed: " + passPath);
        }

        [MenuItem("Rocket Fooxball/Probe Movement Lab Generated State")]
        public static void ProbeMovementLabGeneratedState()
        {
            MovementLabFastModeSession.RestoreIfActive();
            // Development output is an intentional, bounded intermediate. Let
            // the profile gate report the explicit production-only rejection
            // before fail-closed stale-output validation.
            var probe = MovementLabStageGraph.Probe(false);
            MovementLabStageRunner.WriteProbeIfRequested(probe);
            Debug.Log("Rocket Fooxball Movement Lab stale stages: " +
                (probe.StaleStages.Length == 0 ? "none" : string.Join(", ", probe.StaleStages)) +
                "; lighting input digest: " + probe.LightingInputDigest);
        }

        [MenuItem("Rocket Fooxball/Bake Movement Lab Lighting")]
        public static void BakeMovementLabLighting()
        {
            MovementLabFastModeSession.RestoreIfActive();
            MovementLabLightingPipeline.AuthorPersistedVolumeProfile();
            if (IsProductionPreparationRequested())
            {
                // The workflow keeps this with the bake in one batch Editor
                // process; the later validation process still proves reload.
                AssembleMovementLab();
            }
            var probe = MovementLabStageGraph.Probe(false);
            var productionProfile = string.Equals(probe.CurrentState?.bakedProfile ?? "none", MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase);
            if (productionProfile &&
                !probe.IsStale(MovementLabStage.Lighting) &&
                !probe.IsStale(MovementLabStage.BakedOutput))
            {
                Debug.Log(ProductionBakeSkippedMarker + probe.LightingInputDigest + ")");
                MovementLabStageRunner.WriteProbeIfRequested(probe);
                return;
            }

            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.EnsurePersistedProductionSettings();
            // Always prepare the selected profile first. This makes a
            // production bake valid even when the persisted scene currently
            // contains the development intermediate.
            MovementLabLightingProfiles.PrepareScene(scene, MovementLabLightingProfiles.ProfileId.Production);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Production);
            EditorSceneManager.SaveScene(scene, MovementLabContract.ScenePath);
            scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Production);
            var passPath = MovementLabPreBakeGate.ValidateAndWritePassRecord(MovementLabLightingProfiles.ProfileId.Production);
            scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Production);
            scene = MovementLabLightingPipeline.BakeSceneLighting(scene, passPath, MovementLabLightingProfiles.ProfileId.Production);
            EditorSceneManager.SaveScene(scene, MovementLabContract.ScenePath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MovementLabLightingProfiles.WriteManifest(MovementLabLightingProfiles.ProfileId.Production, MovementLabStageGraph.Probe(false, allowBakedOutputDrift: true).LightingInputDigest);
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureBakedState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            MovementLabStageRunner.WriteProbeIfRequested(MovementLabStageGraph.Probe(true));
            Debug.Log("Rocket Fooxball Movement Lab lighting baked explicitly: " + MovementLabContract.ScenePath);
        }

        private static bool IsProductionPreparationRequested()
        {
            return Environment.GetCommandLineArgs().Any(argument =>
                string.Equals(argument, PrepareProductionArgument, StringComparison.Ordinal));
        }

        [MenuItem("Rocket Fooxball/Validate Lighting Author Idempotency")]
        public static void ValidateLightingAuthorIdempotency()
        {
            var evidencePath = GetRequiredLightingEvidencePath();
            var evidence = new LightingAuthorIdempotencyEvidence();
            try
            {
                MovementLabFastModeSession.RestoreIfActive();
                evidence.before = MovementLabLightingPipeline.CapturePersistedVolumeProfileSnapshot();
                evidence.first = MovementLabLightingPipeline.AuthorPersistedVolumeProfile();
                evidence.second = MovementLabLightingPipeline.AuthorPersistedVolumeProfile();
                AssetDatabase.ImportAsset(MovementLabContract.VolumeProfilePath, ImportAssetOptions.ForceSynchronousImport);
                evidence.reloaded = MovementLabLightingPipeline.CapturePersistedVolumeProfileSnapshot();
                evidence.sameHashes = string.Equals(evidence.first.persistedHash, evidence.second.persistedHash, StringComparison.Ordinal) &&
                                      string.Equals(evidence.second.persistedHash, evidence.reloaded.persistedHash, StringComparison.Ordinal);
                evidence.sameIdentities = IdentitiesEqual(evidence.before.identity, evidence.first.identity) &&
                                          IdentitiesEqual(evidence.first.identity, evidence.second.identity) &&
                                          IdentitiesEqual(evidence.second.identity, evidence.reloaded.identity);
                evidence.exactComponentCounts = HasExactComponentCounts(evidence.before.identity) &&
                                                HasExactComponentCounts(evidence.first.identity) &&
                                                HasExactComponentCounts(evidence.second.identity) &&
                                                HasExactComponentCounts(evidence.reloaded.identity);
                evidence.observableResult = evidence.sameHashes && evidence.sameIdentities && evidence.exactComponentCounts;
                evidence.pass = evidence.observableResult;
                evidence.result = evidence.pass ? "pass" : "fail";
                if (!evidence.observableResult)
                    throw new InvalidOperationException("Lighting author idempotency proof failed: persisted bytes, identities, or component counts changed.");
                WriteLightingEvidence(evidencePath, evidence);
                Debug.Log("Rocket Fooxball lighting author idempotency passed: " + evidencePath);
            }
            catch (Exception exception)
            {
                evidence.observableResult = false;
                evidence.pass = false;
                evidence.result = "fail";
                evidence.error = exception.Message;
                WriteLightingEvidence(evidencePath, evidence);
                throw;
            }
        }

        private static bool HasExactComponentCounts(MovementLabLightingPipeline.VolumeProfileIdentitySnapshot identity)
        {
            return identity != null && identity.componentCount == 3 && identity.tonemappingCount == 1 &&
                   identity.bloomCount == 1 && identity.colorAdjustmentsCount == 1;
        }

        private static bool IdentitiesEqual(MovementLabLightingPipeline.VolumeProfileIdentitySnapshot left,
            MovementLabLightingPipeline.VolumeProfileIdentitySnapshot right)
        {
            return left != null && right != null && left.profileGuid == right.profileGuid && left.profileLocalId == right.profileLocalId &&
                   left.componentCount == right.componentCount && left.tonemappingCount == right.tonemappingCount &&
                   left.bloomCount == right.bloomCount && left.colorAdjustmentsCount == right.colorAdjustmentsCount &&
                   (left.componentTypes ?? Array.Empty<string>()).SequenceEqual(right.componentTypes ?? Array.Empty<string>(), StringComparer.Ordinal) &&
                   (left.componentGuids ?? Array.Empty<string>()).SequenceEqual(right.componentGuids ?? Array.Empty<string>(), StringComparer.Ordinal) &&
                   (left.componentLocalIds ?? Array.Empty<long>()).SequenceEqual(right.componentLocalIds ?? Array.Empty<long>());
        }

        private static string GetRequiredLightingEvidencePath()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (!string.Equals(arguments[i], LightingEvidenceArgument, StringComparison.Ordinal)) continue;
                var value = arguments[i + 1];
                if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                    throw new InvalidOperationException(LightingEvidenceArgument + " requires an absolute path.");
                return Path.GetFullPath(value);
            }
            throw new InvalidOperationException("Lighting author idempotency requires " + LightingEvidenceArgument + ".");
        }

        private static void WriteLightingEvidence(string path, LightingAuthorIdempotencyEvidence evidence)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Lighting evidence directory is unavailable: " + path);
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true) + "\n");
        }

        [MenuItem("Rocket Fooxball/Validate Movement Lab")]
        public static void ValidateMovementLab()
        {
            MovementLabFastModeSession.RestoreIfActive();
            var accumulator = new MovementLabValidationAccumulator();
            // Read the typed profile before fail-closed stale checks so a
            // development bake reports the intended production-only rejection.
            var probe = MovementLabStageGraph.Probe(false, allowBakedOutputDrift: false, accumulator: accumulator);
            var productionProfile = string.Equals(probe.CurrentState?.bakedProfile ?? "none", MovementLabLightingProfiles.Production.Tag, StringComparison.OrdinalIgnoreCase);
            if (!productionProfile)
                accumulator.Add("profile", "production-bake", "MovementLab validation requires a production lighting bake. Run 'Rocket Fooxball/Bake Movement Lab Lighting' explicitly; current profile=" + (probe.CurrentState?.bakedProfile ?? "none") + ".");

            var staleNonLighting = probe.StaleStages.Where(stage =>
                stage != MovementLabStage.Lighting &&
                stage != MovementLabStage.BakedOutput &&
                !probe.IsRawOutputDriftOnly(stage)).ToArray();
            if (staleNonLighting.Length > 0)
                accumulator.Add("generated-state", "stale-non-lighting", "MovementLab generated state is stale: " + string.Join(", ", staleNonLighting));
            var staleRaw = probe.StaleStages.Where(probe.IsRawOutputDriftOnly).ToArray();
            if (staleRaw.Length > 0)
                Debug.Log("Rocket Fooxball Movement Lab validation proceeding with informational raw output drift: " + string.Join(", ", staleRaw));
            var staleLighting = probe.StaleStages.Where(stage =>
                stage == MovementLabStage.Lighting || stage == MovementLabStage.BakedOutput).ToArray();
            if (staleLighting.Length > 0)
                Debug.LogWarning("Rocket Fooxball Movement Lab validation proceeding with stale lighting stages: " + string.Join(", ", staleLighting));

            // Invalid profile still runs non-baked semantic coverage; valid
            // production profile includes baked checks.
            MovementLabValidator.Validate(accumulator, includeBakedLighting: productionProfile, logSuccess: true);
            if (!accumulator.HasViolations)
                MovementLabStageRunner.WriteProbeIfRequested(probe);
            accumulator.ThrowIfAny("MovementLab validation");
        }

        [MenuItem("Rocket Fooxball/Enter Movement Lab Fast Preview")]
        public static void EnterMovementLabFastMode() => MovementLabFastModeSession.Enter();

        [MenuItem("Rocket Fooxball/Exit Movement Lab Fast Preview")]
        public static void ExitMovementLabFastMode() => MovementLabFastModeSession.RestoreIfActive();

        [MenuItem("Rocket Fooxball/Build Movement Lab Fast")]
        public static void BuildMovementLabFast()
        {
            MovementLabFastModeSession.RestoreIfActive();
            MovementLabLightingPipeline.AuthorPersistedVolumeProfile();
            var accumulator = new MovementLabValidationAccumulator();
            try
            {
                AssembleMovementLab();
                // Fast mode intentionally accepts the bounded Development
                // lighting intermediate, but still proves persisted semantic
                // state without review/pass/baked-output/capture work.
                MovementLabValidator.ValidateFastPersistedSemantics(accumulator);
                accumulator.Capture("quality", "graphics-quality", () => GraphicsQualityConfigurator.Validate());
                var probe = MovementLabStageGraph.Probe(true, allowBakedOutputDrift: true, accumulator: accumulator);
                if (probe.IsStale(MovementLabStage.Lighting) || probe.IsStale(MovementLabStage.BakedOutput))
                    Debug.Log("Rocket Fooxball fast build: production lighting stale; preview remains available (no bake/pass/full proof).");

                if (!accumulator.HasViolations)
                    MovementLabStageRunner.WriteProbeIfRequested(probe);
                accumulator.ThrowIfAny("MovementLab fast build semantic validation");

                MovementLabFastModeSession.Enter();
                MovementLabFastModeSession.AssertAppliedState();
                Debug.Log("Rocket Fooxball fast build preview state applied: detached lightmap indices, Iteration quality, transient ambient/post/reflection settings.");
                if (Application.isBatchMode) MovementLabFastModeSession.RestoreIfActive();
            }
            catch
            {
                MovementLabFastModeSession.RestoreIfActive();
                throw;
            }
        }

        /// <summary>
        /// Read-only persisted proof used after Fast assembly. The scene is
        /// explicitly reopened in a new Editor process by the workflow, then
        /// semantic, quality, and generated-state checks run without saving.
        /// Lighting and baked-output staleness are allowed for Fast preview;
        /// non-lighting raw-output-only drift is informational, while every
        /// other stale stage remains a failure.
        /// </summary>
        public static void ValidateMovementLabFastPersisted()
        {
            MovementLabFastModeSession.RestoreIfActive();
            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
                throw new InvalidOperationException("Fast persisted validation could not reopen MovementLab scene: " + scene.path);

            var accumulator = new MovementLabValidationAccumulator();
            try
            {
                MovementLabValidator.ValidateFastPersistedSemantics(accumulator);
                accumulator.Capture("quality", "graphics-quality", () => GraphicsQualityConfigurator.Validate());
                var probe = MovementLabStageGraph.Probe(true, allowBakedOutputDrift: true, accumulator: accumulator);
                var disallowedStale = probe.StaleStages.Where(stage =>
                    stage != MovementLabStage.Lighting &&
                    stage != MovementLabStage.BakedOutput &&
                    !probe.IsRawOutputDriftOnly(stage)).ToArray();
                if (disallowedStale.Length > 0)
                {
                    accumulator.Add("generated-state", "fast-persisted-stale-non-lighting",
                        "Fast persisted validation rejected stale non-lighting stages: " + string.Join(", ",
                            disallowedStale.Select(stage => stage + "=" +
                                (probe.TryGetStaleReason(stage, out var reason) ? reason : "stale"))));
                }
                var informationalRawDrift = probe.StaleStages.Where(stage =>
                    stage != MovementLabStage.Lighting &&
                    stage != MovementLabStage.BakedOutput &&
                    probe.IsRawOutputDriftOnly(stage)).ToArray();
                if (informationalRawDrift.Length > 0)
                    Debug.Log("Rocket Fooxball fast persisted validation proceeding with informational raw output drift: " + string.Join(", ",
                        informationalRawDrift.Select(stage => stage + "=" +
                            (probe.TryGetStaleReason(stage, out var reason) ? reason : "stale"))));
                var permittedProductionStale = probe.StaleStages.Where(stage =>
                    stage == MovementLabStage.Lighting ||
                    stage == MovementLabStage.BakedOutput).ToArray();
                if (permittedProductionStale.Length > 0)
                    Debug.Log("Rocket Fooxball fast persisted validation: production lighting stages are stale but permitted for Fast preview when outputs are present: " +
                        string.Join(", ", permittedProductionStale.Select(stage => stage + "=" +
                            (probe.TryGetStaleReason(stage, out var reason) ? reason : "stale"))));
                accumulator.ThrowIfAny("MovementLab fast persisted validation");
                Debug.Log("Rocket Fooxball fast persisted validation passed after scene reopen; no project save performed.");
            }
            finally
            {
                MovementLabFastModeSession.RestoreIfActive();
            }
        }

        [MenuItem("Rocket Fooxball/Bake Movement Lab Lighting Development")]
        public static void BakeMovementLabLightingDevelopment()
        {
            MovementLabFastModeSession.RestoreIfActive();
            MovementLabLightingPipeline.AuthorPersistedVolumeProfile();
            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.EnsurePersistedDevelopmentSettings();
            MovementLabLightingProfiles.PrepareScene(scene, MovementLabLightingProfiles.ProfileId.Development);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Development);
            EditorSceneManager.SaveScene(scene, MovementLabContract.ScenePath);
            scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Development);
            var passPath = MovementLabPreBakeGate.ValidateAndWritePassRecord(MovementLabLightingProfiles.ProfileId.Development);
            scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabLightingProfiles.ValidatePreparedScene(MovementLabLightingProfiles.ProfileId.Development);
            scene = MovementLabLightingPipeline.BakeSceneLighting(scene, passPath, MovementLabLightingProfiles.ProfileId.Development);
            EditorSceneManager.SaveScene(scene, MovementLabContract.ScenePath);
            MovementLabLightingProfiles.WriteManifest(MovementLabLightingProfiles.ProfileId.Development, MovementLabStageGraph.CaptureCurrentRecord(MovementLabStage.Lighting).inputDigest);
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureBakedState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            MovementLabStageRunner.WriteProbeIfRequested(MovementLabStageGraph.Probe(true, allowBakedOutputDrift: true));
            Debug.Log("Rocket Fooxball Movement Lab development lighting baked explicitly: profile=development; production validation will reject this output.");
        }
    }
}
