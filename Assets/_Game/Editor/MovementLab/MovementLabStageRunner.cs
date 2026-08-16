using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RocketFooxball.Editor
{
    /// <summary>
    /// Executes only stale non-lighting stages. Each stage has a persisted
    /// save/import/reload barrier and its own atomic manifest merge.
    /// </summary>
    internal static class MovementLabStageRunner
    {
        private const int ProbeSchemaVersion = 1;
        private const string ProbeArgument = "-movementLabProbePath";

        [Serializable]
        private sealed class StageProbeJson
        {
            public int schemaVersion;
            public string gitSha;
            public string unityVersion;
            public string manifestStatus;
            public string[] staleStages;
            public string[] staleReasons;
            public string lightingInputDigest;
            public string sourceSignature;
            public string outputFingerprint;
            public string bakedProfile;
            public string[] fingerprintPaths;
            public string[] fingerprintHashes;
        }

        internal static MovementLabStageProbe RunSelective()
        {
            return Run();
        }

        internal static void WriteProbeIfRequested(MovementLabStageProbe probe)
        {
            var path = GetProbePath();
            if (string.IsNullOrWhiteSpace(path)) return;
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            var state = probe.CurrentState ?? MovementLabStageGraph.CaptureBakedState();
            var payload = new StageProbeJson
            {
                schemaVersion = ProbeSchemaVersion,
                gitSha = MovementLabManifestStore.GetCurrentGitSha(),
                unityVersion = Application.unityVersion,
                manifestStatus = probe.ManifestStatus,
                staleStages = probe.StaleStages.Select(stage => stage.ToString()).ToArray(),
                staleReasons = probe.StaleReasons,
                lightingInputDigest = probe.LightingInputDigest ?? string.Empty,
                sourceSignature = state.sourceSignature ?? string.Empty,
                outputFingerprint = state.generatedOutputFingerprint ?? string.Empty,
                bakedProfile = state.bakedProfile ?? "none",
                fingerprintPaths = state.fingerprintPaths ?? Array.Empty<string>(),
                fingerprintHashes = state.fingerprintHashes ?? Array.Empty<string>()
            };
            WriteOutsideProjectAtomic(path, JsonUtility.ToJson(payload, true) + "\n");
        }

        private static MovementLabStageProbe Run()
        {
            MovementLabStageGraph.RunCanonicalSceneInvariantSelfCheck();
            // A development/intermediate bake may legitimately remove or
            // replace baked files. Keep those records stale for callers, but
            // let non-lighting closure continue without authorizing a bake.
            var initial = MovementLabStageGraph.Probe(stopOnOutputDrift: true, allowBakedOutputDrift: true);
            var stages = MovementLabStageGraph.NonLightingGenerationOrder;
            var current = initial;
            // Legacy manifests stay resumable until every initial non-lighting
            // stage has persisted and reloaded its outputs. Upgrade manifest
            // schema only after that uninterrupted pass completes.
            var deferManifestMigrationUntilPassCompletes = initial.ManifestReadStatus != MovementLabManifestReadStatus.Current;
            var sawWork = false;
            var visitedStates = new HashSet<string>(StringComparer.Ordinal);
            var maxIterations = Math.Max(4, stages.Length * 4);
            MovementLabManifestStore.EnsureWriteAuthorization();
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                // Output hashes provide provenance, not a rebuild trigger.
                // `missing:` and every non-output-drift reason still execute
                // their owning stage; only changed-output-only records remain
                // informational until a real stage input changes.
                var stale = stages.Where(stage => current.IsStale(stage) && !current.IsRawOutputDriftOnly(stage)).ToArray();
                if (stale.Length == 0) break;

                var stateKey = string.Join(",", stale.Select(stage => stage.ToString()).ToArray()) + ":" +
                               (current.LightingInputDigest ?? string.Empty) + ":" +
                               (current.CurrentState?.generatedOutputFingerprint ?? string.Empty);
                if (!visitedStates.Add(stateKey))
                {
                    throw new InvalidOperationException("MovementLab non-lighting stage closure detected a cycle: " + stateKey);
                }

                var executedThisPass = false;
                for (var i = 0; i < stages.Length; i++)
                {
                    var stage = stages[i];
                    if (!current.IsStale(stage) || current.IsRawOutputDriftOnly(stage)) continue;
                    if (!ShouldSkipPrefabOnlyGameplayRefresh(stage, current))
                    {
                        ExecuteStage(stage);
                    }
                    PersistAndReload(stage);

                    if (deferManifestMigrationUntilPassCompletes)
                    {
                        executedThisPass = true;
                        sawWork = true;
                        continue;
                    }

                    var merged = MovementLabStageGraph.MergeStageRecord(stage);
                    // Current manifests retain atomic recovery after each
                    // stage; migration writes one complete live snapshot below.
                    MovementLabManifestStore.WriteAtomic(merged);
                    AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);

                    // Recompute immediately after every current-schema write.
                    current = MovementLabStageGraph.Probe(stopOnOutputDrift: true, allowBakedOutputDrift: true);
                    executedThisPass = true;
                    sawWork = true;
                }

                if (!executedThisPass)
                {
                    throw new InvalidOperationException("MovementLab non-lighting stage closure made no progress; stale stages remain: " +
                        string.Join(",", stale.Select(stage => stage.ToString()).ToArray()));
                }

                if (deferManifestMigrationUntilPassCompletes)
                {
                    var migrationState = MovementLabStageGraph.MergeStageRecord(stages[stages.Length - 1]);
                    MovementLabManifestStore.WriteAtomic(migrationState);
                    AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
                    current = MovementLabStageGraph.Probe(stopOnOutputDrift: true, allowBakedOutputDrift: true);
                    deferManifestMigrationUntilPassCompletes = false;
                }
            }

            var final = MovementLabStageGraph.Probe(stopOnOutputDrift: true, allowBakedOutputDrift: true);
            var unresolved = stages.Where(stage => final.IsStale(stage) && !final.IsRawOutputDriftOnly(stage)).ToArray();
            if (unresolved.Length > 0)
            {
                throw new InvalidOperationException("MovementLab non-lighting stage closure did not converge: " +
                    string.Join(",", unresolved.Select(stage => stage.ToString()).ToArray()));
            }

            var finalState = MovementLabStageGraph.MarkCurrent(final);
            if (sawWork)
            {
                MovementLabManifestStore.WriteAtomic(finalState);
                AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);
            }

            WriteProbeIfRequested(final);
            return final;
        }

        private static bool ShouldSkipPrefabOnlyGameplayRefresh(MovementLabStage stage, MovementLabStageProbe probe)
        {
            if (stage != MovementLabStage.GameplayScene || probe == null ||
                !probe.TryGetStaleReason(stage, out var reason) || string.IsNullOrWhiteSpace(reason)) return false;

            // Prefab/controller outputs are referenced by stable GUIDs from
            // the scene. Refreshing those assets does not require recreating
            // scene objects (which would change baked object identities), so
            // close this dependency-only stale record without touching scene
            // bytes or lighting bindings. Any wiring/contract key still runs
            // the full gameplay composer.
            var reasons = reason.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            return reasons.Length > 0 && reasons.All(value => value == "dependency-state-changed");
        }

        private static void ExecuteStage(MovementLabStage stage)
        {
            switch (stage)
            {
                case MovementLabStage.Quality:
                    MovementLabSceneComposer.AssembleQualityStage();
                    break;
                case MovementLabStage.Importer:
                    MovementLabSceneComposer.AssembleImporterStage();
                    break;
                case MovementLabStage.MaterialPrefab:
                    MovementLabSceneComposer.AssembleMaterialPrefabStage();
                    break;
                case MovementLabStage.GameplayScene:
                    MovementLabSceneComposer.AssembleGameplaySceneStage();
                    break;
                default:
                    throw new InvalidOperationException("Stage runner cannot write lighting stages: " + stage);
            }
        }

        private static void PersistAndReload(MovementLabStage stage)
        {
            var owned = MovementLabStageGraph.GetOwnedOutputs(stage);
            SaveOwnedAssets(owned);
            for (var i = 0; i < owned.Length; i++)
            {
                var path = owned[i];
                if (path.EndsWith(".meta", StringComparison.Ordinal) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(MovementLabManifestStore.ResolveProjectPath(path))) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (stage == MovementLabStage.GameplayScene && File.Exists(MovementLabManifestStore.ResolveProjectPath(MovementLabContract.ScenePath)))
            {
                EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            }
            for (var i = 0; i < owned.Length; i++)
            {
                var path = owned[i];
                if (path.EndsWith(".meta", StringComparison.Ordinal) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                AssetDatabase.LoadMainAssetAtPath(path);
            }
        }

        private static void SaveOwnedAssets(string[] owned)
        {
            for (var i = 0; i < (owned ?? Array.Empty<string>()).Length; i++)
            {
                var path = owned[i];
                if (path.EndsWith(".meta", StringComparison.Ordinal) || path.EndsWith(".unity", StringComparison.Ordinal) ||
                    (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("ProjectSettings/", StringComparison.Ordinal))) continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null && EditorUtility.IsPersistent(asset)) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        private static string GetProbePath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], ProbeArgument, StringComparison.Ordinal))
                {
                    var value = args[i + 1];
                    if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(ProbeArgument + " requires a path.");
                    return ValidateProbePath(value);
                }
            }
            return null;
        }

        private static string ValidateProbePath(string value)
        {
            if (!Path.IsPathRooted(value)) throw new InvalidOperationException("MovementLab probe path must be absolute: " + value);
            var path = Path.GetFullPath(value);
            var root = Path.GetFullPath(MovementLabContractCatalog.ResolveProjectRoot().FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("MovementLab probe must be outside the project tree: " + path);
            if (path.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(Path.DirectorySeparatorChar + "Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("MovementLab probe path is not durable: " + path);
            }
            return path;
        }

        private static void WriteOutsideProjectAtomic(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("MovementLab probe directory is unavailable.");
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) MovementLabAtomicFile.ReplaceAtomicWithRetry(temporary, path);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

    }
}
