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
        [MenuItem("Rocket Fooxball/Build Movement Lab")]
        public static void BuildMovementLab()
        {
            AssembleMovementLab();
            ValidateMovementLabPreBake();
            var probe = MovementLabStageGraph.Probe(true);
            if (probe.IsStale(MovementLabStage.Lighting) || probe.IsStale(MovementLabStage.BakedOutput))
            {
                throw new InvalidOperationException("MovementLab lighting is stale. Run 'Rocket Fooxball/Bake Movement Lab Lighting' explicitly.");
            }
        }

        [MenuItem("Rocket Fooxball/Assemble Movement Lab")]
        public static void AssembleMovementLab()
        {
            var probe = MovementLabStageGraph.Probe(true);
            var generationStages = new[]
            {
                MovementLabStage.Importer,
                MovementLabStage.MaterialPrefab,
                MovementLabStage.GameplayScene,
                MovementLabStage.Quality
            };
            if (!generationStages.Any(probe.IsStale))
            {
                Debug.Log("Rocket Fooxball Movement Lab assembly reused generated state: " + MovementLabContract.ScenePath);
                return;
            }

            MovementLabManifestStore.EnsureWriteAuthorization();
            MovementLabSceneComposer.AssembleMovementLabUnstaged();
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureAssembledState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Rocket Fooxball Movement Lab assembled without lighting bake: " + MovementLabContract.ScenePath);
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
            // Gate owns semantic validation, stale-stage checks, and pass-record write.
            var passPath = MovementLabPreBakeGate.ValidateAndWritePassRecord();
            Debug.Log("Rocket Fooxball Movement Lab pre-bake gate passed: " + passPath);
        }

        [MenuItem("Rocket Fooxball/Probe Movement Lab Generated State")]
        public static void ProbeMovementLabGeneratedState()
        {
            var probe = MovementLabStageGraph.Probe(true);
            Debug.Log("Rocket Fooxball Movement Lab stale stages: " +
                (probe.StaleStages.Length == 0 ? "none" : string.Join(", ", probe.StaleStages)) +
                "; lighting input digest: " + probe.LightingInputDigest);
        }

        [MenuItem("Rocket Fooxball/Bake Movement Lab Lighting")]
        public static void BakeMovementLabLighting()
        {
            var passPath = MovementLabPreBakeGate.ValidateAndWritePassRecord();
            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            MovementLabPreBakeGate.RevalidatePassRecord(passPath);
            MovementLabLightingPipeline.BakeSceneLighting(scene, passPath);
            EditorSceneManager.SaveScene(scene, MovementLabContract.ScenePath);
            AssetDatabase.SaveAssets();
            MovementLabSceneComposer.NormalizeGeneratedYamlWhitespace();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            MovementLabPreBakeGate.RevalidatePassRecord(passPath, allowBakedOutputDrift: true);
            MovementLabManifestStore.WriteAtomic(MovementLabStageGraph.CaptureBakedState());
            AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Rocket Fooxball Movement Lab lighting baked explicitly: " + MovementLabContract.ScenePath);
        }

        [MenuItem("Rocket Fooxball/Validate Movement Lab")]
        public static void ValidateMovementLab()
        {
            var probe = MovementLabStageGraph.Probe(true);
            if (probe.StaleStages.Length > 0)
            {
                throw new InvalidOperationException("MovementLab generated state is stale: " + string.Join(", ", probe.StaleStages));
            }
            MovementLabValidator.Validate(includeBakedLighting: true, logSuccess: true);
        }
    }
}
