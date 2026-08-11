using System;
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
            MovementLabLightingPipeline.NormalizePostBakeYamlWhitespace();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MovementLabLightingProfiles.WriteManifest(MovementLabLightingProfiles.ProfileId.Production, MovementLabStageGraph.Probe(false, allowBakedOutputDrift: true).LightingInputDigest);
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureBakedState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            MovementLabStageRunner.WriteProbeIfRequested(MovementLabStageGraph.Probe(true));
            Debug.Log("Rocket Fooxball Movement Lab lighting baked explicitly: " + MovementLabContract.ScenePath);
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

        [MenuItem("Rocket Fooxball/Bake Movement Lab Lighting Development")]
        public static void BakeMovementLabLightingDevelopment()
        {
            MovementLabFastModeSession.RestoreIfActive();
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
            MovementLabLightingPipeline.NormalizePostBakeYamlWhitespace();
            MovementLabLightingProfiles.WriteManifest(MovementLabLightingProfiles.ProfileId.Development, MovementLabStageGraph.Probe(false, allowBakedOutputDrift: true).LightingInputDigest);
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureBakedState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            MovementLabStageRunner.WriteProbeIfRequested(MovementLabStageGraph.Probe(true, allowBakedOutputDrift: true));
            Debug.Log("Rocket Fooxball Movement Lab development lighting baked explicitly: profile=development; production validation will reject this output.");
        }
    }
}
