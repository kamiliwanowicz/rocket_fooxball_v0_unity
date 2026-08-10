using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
            return Run(forceAllNonLighting: false);
        }

        internal static MovementLabStageProbe RunForceAllNonLighting()
        {
            return Run(forceAllNonLighting: true);
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

        private static MovementLabStageProbe Run(bool forceAllNonLighting)
        {
            MovementLabStageGraph.RunCanonicalSceneInvariantSelfCheck();
            var initial = MovementLabStageGraph.Probe(stopOnOutputDrift: true);
            var stages = MovementLabStageGraph.NonLightingGenerationOrder;
            var before = forceAllNonLighting ? CaptureNonLightingHashes() : null;
            var current = initial;
            var forcePending = forceAllNonLighting;
            var sawWork = false;
            var visitedStates = new HashSet<string>(StringComparer.Ordinal);
            var maxIterations = Math.Max(4, stages.Length * 4);
            for (var iteration = 0; iteration < maxIterations; iteration++)
            {
                var stale = stages.Where(current.IsStale).ToArray();
                if (!forcePending && stale.Length == 0) break;

                var stateKey = string.Join(",", stale.Select(stage => stage.ToString()).ToArray()) + ":" +
                               (current.LightingInputDigest ?? string.Empty) + ":" +
                               (current.CurrentState?.generatedOutputFingerprint ?? string.Empty);
                if (!visitedStates.Add(stateKey))
                {
                    throw new InvalidOperationException("MovementLab non-lighting stage closure detected a cycle: " + stateKey);
                }

                var executedThisPass = false;
                MovementLabManifestStore.EnsureWriteAuthorization();
                for (var i = 0; i < stages.Length; i++)
                {
                    var stage = stages[i];
                    if (!forcePending && !current.IsStale(stage)) continue;
                    if (!ShouldSkipPrefabOnlyGameplayRefresh(stage, current, forcePending))
                    {
                        ExecuteStage(stage);
                    }
                    PersistAndReload(stage);
                    var merged = MovementLabStageGraph.MergeStageRecord(stage);
                    // Intermediate merges preserve atomic recovery state; the
                    // externally returned/probed state is written only after
                    // the closure below proves no non-lighting stage remains.
                    MovementLabManifestStore.WriteAtomic(merged);
                    AssetDatabase.ImportAsset(MovementLabContract.ManifestPath, ImportAssetOptions.ForceSynchronousImport);

                    // Recompute immediately after every write. This closes
                    // newly-stale downstream stages in topological order and
                    // prevents returning a probe based only on the initial DAG.
                    current = MovementLabStageGraph.Probe(stopOnOutputDrift: true);
                    executedThisPass = true;
                    sawWork = true;
                }

                forcePending = false;
                if (!executedThisPass)
                {
                    throw new InvalidOperationException("MovementLab non-lighting stage closure made no progress; stale stages remain: " +
                        string.Join(",", stale.Select(stage => stage.ToString()).ToArray()));
                }
            }

            var final = MovementLabStageGraph.Probe(stopOnOutputDrift: true);
            var unresolved = stages.Where(final.IsStale).ToArray();
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

            if (forceAllNonLighting)
            {
                var after = CaptureNonLightingHashes();
                AssertExactEquality(before, after);
            }

            WriteProbeIfRequested(final);
            return final;
        }

        private static bool ShouldSkipPrefabOnlyGameplayRefresh(MovementLabStage stage, MovementLabStageProbe probe, bool forcePending)
        {
            if (forcePending || stage != MovementLabStage.GameplayScene || probe == null ||
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

        private static Dictionary<string, string> CaptureNonLightingHashes()
        {
            var paths = MovementLabStageGraph.NonLightingGenerationOrder
                .SelectMany(MovementLabStageGraph.GetOwnedOutputs)
                .Where(path => !string.Equals(path, MovementLabContract.ManifestPath, StringComparison.Ordinal))
                .Select(MovementLabManifestStore.NormalizeRepositoryPath)
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var absolute = MovementLabManifestStore.ResolveProjectPath(path);
                result[path] = File.Exists(absolute) ? HashFile(absolute) : HashText("missing:" + path);
            }
            return result;
        }

        private static void AssertExactEquality(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            var paths = (before.Keys.Union(after.Keys, StringComparer.Ordinal)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var differences = new List<string>();
            for (var i = 0; i < paths.Length; i++)
            {
                before.TryGetValue(paths[i], out var oldHash);
                after.TryGetValue(paths[i], out var newHash);
                if (!string.Equals(oldHash, newHash, StringComparison.Ordinal)) differences.Add(paths[i]);
            }
            if (differences.Count > 0)
            {
                throw new InvalidOperationException("Forced full non-lighting rebuild is not deterministic: " + string.Join(", ", differences.ToArray()));
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
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string HashText(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty))).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
