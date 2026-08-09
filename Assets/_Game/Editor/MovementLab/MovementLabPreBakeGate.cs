using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RocketFooxball.Editor
{
    internal static class MovementLabPreBakeGate
    {
        [Serializable]
        private sealed class ReviewMarker
        {
            public int schemaVersion;
            public string gitSha;
            public bool sourceReviewCompleted;
            public bool criticalHighFixesApplied;
            public string reviewer;
            public string completedUtc;
        }

        [Serializable]
        private sealed class PassRecord
        {
            public int schemaVersion = 1;
            public string gitSha;
            public string unityVersion;
            public string lightingInputDigest;
            public string reviewMarkerPath;
            public string passedUtc;
            public string[] validatedStages;
        }

        internal static string ValidateAndWritePassRecord()
        {
            ValidatePersistedNonLightingState();
            MovementLabBuilder.ValidateMovementLabPreBakeSemantics();
            var probe = MovementLabStageGraph.Probe(true);
            var required = new[] { MovementLabStage.Importer, MovementLabStage.MaterialPrefab, MovementLabStage.GameplayScene, MovementLabStage.Quality };
            var stale = required.Where(probe.IsStale).Select(stage => stage.ToString()).ToArray();
            if (stale.Length > 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake gate failed; stale non-lighting stages: " + string.Join(", ", stale));
            }

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var gitSha = RunGit(projectRoot, "rev-parse HEAD");
            if (gitSha.Length != 40) throw new InvalidOperationException("MovementLab pre-bake gate could not resolve exact 40-character Git SHA.");
            ValidateSourceCheckpoint(projectRoot);
            var commonGitDirectory = ResolveCommonGitDirectory(projectRoot);
            var evidenceRoot = Path.Combine(commonGitDirectory, "architecture-evidence", "movement-lab-prebake");
            var reviewMarkerPath = Path.Combine(evidenceRoot, "reviews", gitSha + ".json");
            ValidateReviewMarker(reviewMarkerPath, gitSha);

            var passDirectory = Path.Combine(evidenceRoot, "passes");
            Directory.CreateDirectory(passDirectory);
            var passPath = Path.Combine(passDirectory, gitSha + "-" + probe.LightingInputDigest + ".json");
            var record = new PassRecord
            {
                gitSha = gitSha,
                unityVersion = Application.unityVersion,
                lightingInputDigest = probe.LightingInputDigest,
                reviewMarkerPath = reviewMarkerPath,
                passedUtc = DateTime.UtcNow.ToString("O"),
                validatedStages = required.Select(stage => stage.ToString()).ToArray()
            };
            WriteOutsideProjectAtomic(passPath, JsonUtility.ToJson(record, true) + "\n", projectRoot);
            return passPath;
        }

        private static void ValidatePersistedNonLightingState()
        {
            ValidateAssetAndMetaCoverage(MovementLabContract.ImportedAssetPaths);
            ValidateAssetAndMetaCoverage(MovementLabContract.MaterialPrefabOutputs);
            ValidateAssetAndMetaCoverage(MovementLabContract.QualityOutputs.Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)).ToArray());
            for (var i = 0; i < MovementLabContract.GameplaySceneOutputs.Length; i++)
            {
                var path = MovementLabContract.GameplaySceneOutputs[i];
                if (!File.Exists(MovementLabManifestStore.ResolveProjectPath(path)))
                {
                    throw new InvalidOperationException("MovementLab pre-bake output is missing: " + path);
                }
                if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                    !File.Exists(MovementLabManifestStore.ResolveProjectPath(path + ".meta")))
                {
                    throw new InvalidOperationException("MovementLab pre-bake output meta is missing: " + path + ".meta");
                }
            }

            var scene = EditorSceneManager.OpenScene(MovementLabContract.ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid() || !string.Equals(scene.path, MovementLabContract.ScenePath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab scene failed to reopen: " + MovementLabContract.ScenePath);
            }

            var volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.InstanceID);
            var persistedVolumeCount = 0;
            for (var i = 0; i < volumes.Length; i++)
            {
                var volume = volumes[i];
                if (volume == null || volume.sharedProfile == null) continue;
                persistedVolumeCount++;
                if (!EditorUtility.IsPersistent(volume.sharedProfile))
                {
                    throw new InvalidOperationException("MovementLab Volume must use a persisted sharedProfile.");
                }
                var components = volume.sharedProfile.components;
                for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
                {
                    var component = components[componentIndex];
                    if (component == null || !EditorUtility.IsPersistent(component) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out _, out long localId) || localId == 0)
                    {
                        throw new InvalidOperationException("MovementLab VolumeProfile component is not a persistent subasset.");
                    }
                }
            }
            if (persistedVolumeCount == 0) throw new InvalidOperationException("MovementLab persisted shared VolumeProfile is missing.");

            ValidateLitMaterialPersistence();
        }

        private static void ValidateAssetAndMetaCoverage(string[] paths)
        {
            for (var i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (AssetDatabase.LoadMainAssetAtPath(path) == null) throw new InvalidOperationException("MovementLab pre-bake asset is missing: " + path);
                if (!File.Exists(MovementLabManifestStore.ResolveProjectPath(path + ".meta")))
                {
                    throw new InvalidOperationException("MovementLab pre-bake asset meta is missing: " + path + ".meta");
                }
            }
        }

        private static void ValidateLitMaterialPersistence()
        {
            var materialPaths = MovementLabContract.MaterialPrefabOutputs.Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase));
            foreach (var path in materialPaths)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null) throw new InvalidOperationException("MovementLab material failed persisted reload: " + path);
                if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null && !material.IsKeywordEnabled("_NORMALMAP"))
                {
                    throw new InvalidOperationException("MovementLab material normal-map keyword mismatch: " + path);
                }
                if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null && !material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"))
                {
                    throw new InvalidOperationException("MovementLab material metallic-map keyword mismatch: " + path);
                }
            }
        }

        private static void ValidateReviewMarker(string path, string gitSha)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker is missing: " + path);
            }
            ReviewMarker marker;
            try
            {
                marker = JsonUtility.FromJson<ReviewMarker>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker could not be parsed: " + exception.Message, exception);
            }
            if (marker == null || marker.schemaVersion != 1 || !string.Equals(marker.gitSha, gitSha, StringComparison.Ordinal) ||
                !marker.sourceReviewCompleted || !marker.criticalHighFixesApplied || string.IsNullOrWhiteSpace(marker.reviewer) ||
                !DateTime.TryParse(marker.completedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker is stale or incomplete: " + path);
            }
        }

        private static string ResolveCommonGitDirectory(string projectRoot)
        {
            var value = RunGit(projectRoot, "rev-parse --git-common-dir");
            var path = Path.IsPathRooted(value) ? value : Path.Combine(projectRoot, value);
            return Path.GetFullPath(path);
        }

        private static void ValidateSourceCheckpoint(string projectRoot)
        {
            var changed = SplitLines(RunGit(projectRoot, "diff --name-only HEAD"))
                .Concat(SplitLines(RunGit(projectRoot, "ls-files --others --exclude-standard")))
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !IsGeneratedOutput(path))
                .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (changed.Length > 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake source checkpoint is dirty relative to HEAD: " + string.Join(", ", changed));
            }
        }

        private static IEnumerable<string> SplitLines(string value)
        {
            return (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsGeneratedOutput(string path)
        {
            if (string.Equals(path, MovementLabContract.ManifestPath, StringComparison.Ordinal) ||
                string.Equals(path, MovementLabContract.ManifestPath + ".meta", StringComparison.Ordinal)) return true;
            if (MatchesOutput(path, MovementLabContract.MaterialPrefabOutputs) ||
                MatchesOutput(path, MovementLabContract.GameplaySceneOutputs) ||
                MatchesOutput(path, MovementLabContract.QualityOutputs) ||
                MatchesOutput(path, MovementLabContract.BakedOutputPaths)) return true;
            for (var i = 0; i < MovementLabContract.ImportedAssetPaths.Length; i++)
            {
                if (string.Equals(path, MovementLabContract.ImportedAssetPaths[i] + ".meta", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool MatchesOutput(string path, string[] outputs)
        {
            for (var i = 0; i < outputs.Length; i++)
            {
                if (string.Equals(path, outputs[i], StringComparison.Ordinal) || string.Equals(path, outputs[i] + ".meta", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string RunGit(string projectRoot, string arguments)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Unable to start Git for MovementLab pre-bake gate.");
                var output = process.StandardOutput.ReadToEnd().Trim();
                var error = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("MovementLab pre-bake Git query failed: " + error);
                return output;
            }
        }

        private static void WriteOutsideProjectAtomic(string path, string content, string projectRoot)
        {
            var fullPath = Path.GetFullPath(path);
            var projectPrefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence must be outside worktree: " + fullPath);
            }
            if (fullPath.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullPath.IndexOf(Path.DirectorySeparatorChar + "Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("MovementLab pre-bake evidence path is not durable: " + fullPath);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Unable to resolve pre-bake evidence directory."));
            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
