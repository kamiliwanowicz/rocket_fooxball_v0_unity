using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            public string reviewerIdentity;
            public string checkpoint;
            public string checkpointId;
            public string reviewCheckpoint;
            public string completedUtc;
        }

        private sealed class ValidatedReviewMarker
        {
            internal string Digest;
            internal string Checkpoint;
            internal string Reviewer;
        }

        [Serializable]
        private sealed class PassRecord
        {
            public int schemaVersion = 1;
            public string gitSha;
            public string unityVersion;
            public string lightingInputDigest;
            public string reviewMarkerPath;
            public string reviewMarkerDigest;
            public string checkpoint;
            public string reviewer;
            public string passedUtc;
            public string[] validatedStages;
        }

        internal static string ValidateAndWritePassRecord()
        {
            ValidatePersistedNonLightingState();
            MovementLabValidator.ValidatePreBakeSemantics();
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
            var review = ValidateReviewMarker(reviewMarkerPath, gitSha, projectRoot);

            var passDirectory = Path.Combine(evidenceRoot, "passes");
            Directory.CreateDirectory(passDirectory);
            var passPath = Path.Combine(passDirectory, gitSha + "-" + probe.LightingInputDigest + ".json");
            var record = new PassRecord
            {
                gitSha = gitSha,
                unityVersion = Application.unityVersion,
                lightingInputDigest = probe.LightingInputDigest,
                reviewMarkerPath = reviewMarkerPath,
                reviewMarkerDigest = review.Digest,
                checkpoint = review.Checkpoint,
                reviewer = review.Reviewer,
                passedUtc = DateTime.UtcNow.ToString("O"),
                validatedStages = required.Select(stage => stage.ToString()).ToArray()
            };
            WriteOutsideProjectAtomic(passPath, JsonUtility.ToJson(record, true) + "\n", projectRoot);
            return passPath;
        }

        internal static void RevalidatePassRecord(string passPath, bool allowBakedOutputDrift = false)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var fullPassPath = Path.GetFullPath(passPath ?? string.Empty);
            EnsureDurableEvidencePath(fullPassPath, projectRoot);
            if (!File.Exists(fullPassPath)) throw new InvalidOperationException("MovementLab pre-bake pass record is missing: " + fullPassPath);

            PassRecord pass;
            try
            {
                pass = JsonUtility.FromJson<PassRecord>(Encoding.UTF8.GetString(File.ReadAllBytes(fullPassPath)));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("MovementLab pre-bake pass record could not be parsed: " + exception.Message, exception);
            }

            var gitSha = RunGit(projectRoot, "rev-parse HEAD");
            if (gitSha.Length != 40 || pass == null || pass.schemaVersion != 1 ||
                !string.Equals(pass.gitSha, gitSha, StringComparison.Ordinal) ||
                !string.Equals(pass.unityVersion, Application.unityVersion, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(pass.lightingInputDigest) || string.IsNullOrWhiteSpace(pass.reviewMarkerPath) ||
                string.IsNullOrWhiteSpace(pass.reviewMarkerDigest) || string.IsNullOrWhiteSpace(pass.checkpoint) ||
                string.IsNullOrWhiteSpace(pass.reviewer))
            {
                throw new InvalidOperationException("MovementLab pre-bake pass record is stale or incomplete; exact Git SHA required.");
            }

            ValidateSourceCheckpoint(projectRoot);
            var commonGitDirectory = ResolveCommonGitDirectory(projectRoot);
            var expectedPassPath = Path.GetFullPath(Path.Combine(commonGitDirectory, "architecture-evidence", "movement-lab-prebake", "passes", gitSha + "-" + pass.lightingInputDigest + ".json"));
            if (!string.Equals(fullPassPath, expectedPassPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake pass path is not bound to exact current Git SHA and lighting digest.");
            }

            var expectedReviewPath = Path.GetFullPath(Path.Combine(commonGitDirectory, "architecture-evidence", "movement-lab-prebake", "reviews", gitSha + ".json"));
            EnsureDurableEvidencePath(expectedReviewPath, projectRoot);
            var passReviewPath = Path.GetFullPath(pass.reviewMarkerPath);
            if (!string.Equals(passReviewPath, expectedReviewPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab pre-bake review marker path is not bound to exact current Git SHA.");
            }

            var review = ValidateReviewMarker(expectedReviewPath, gitSha, projectRoot);
            if (!string.Equals(review.Digest, pass.reviewMarkerDigest, StringComparison.Ordinal) ||
                !string.Equals(review.Checkpoint, pass.checkpoint, StringComparison.Ordinal) ||
                !string.Equals(review.Reviewer, pass.reviewer, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab pre-bake review marker changed after pass validation.");
            }

            var probe = MovementLabStageGraph.Probe(true, allowBakedOutputDrift);
            var required = new[] { MovementLabStage.Importer, MovementLabStage.MaterialPrefab, MovementLabStage.GameplayScene, MovementLabStage.Quality };
            var stale = required.Where(probe.IsStale).Select(stage => stage.ToString()).ToArray();
            if (stale.Length > 0 || !string.Equals(probe.LightingInputDigest, pass.lightingInputDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab pre-bake finalized lighting digest or non-lighting state changed after pass validation.");
            }
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

        private static ValidatedReviewMarker ValidateReviewMarker(string path, string gitSha, string projectRoot)
        {
            EnsureDurableEvidencePath(Path.GetFullPath(path), projectRoot);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker is missing: " + path);
            }
            ReviewMarker marker;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
                marker = JsonUtility.FromJson<ReviewMarker>(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker could not be parsed: " + exception.Message, exception);
            }
            var reviewer = ResolveMarkerIdentity(marker?.reviewer, marker?.reviewerIdentity, null, "reviewer");
            var checkpoint = ResolveMarkerIdentity(marker?.checkpoint, marker?.checkpointId, marker?.reviewCheckpoint, "checkpoint");
            if (marker == null || marker.schemaVersion != 1 || !string.Equals(marker.gitSha, gitSha, StringComparison.Ordinal) ||
                !marker.sourceReviewCompleted || !marker.criticalHighFixesApplied ||
                string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(checkpoint) ||
                !DateTime.TryParse(marker.completedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker is stale or incomplete: " + path);
            }

            return new ValidatedReviewMarker
            {
                Digest = HashBytes(bytes),
                Checkpoint = checkpoint,
                Reviewer = reviewer
            };
        }

        private static string ResolveMarkerIdentity(string primary, string alias, string thirdAlias, string label)
        {
            if (!string.IsNullOrWhiteSpace(primary) && !string.IsNullOrWhiteSpace(alias) &&
                !string.Equals(primary, alias, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker has conflicting " + label + " identities.");
            }

            if (!string.IsNullOrWhiteSpace(thirdAlias) &&
                ((!string.IsNullOrWhiteSpace(primary) && !string.Equals(primary, thirdAlias, StringComparison.Ordinal)) ||
                 (!string.IsNullOrWhiteSpace(alias) && !string.Equals(alias, thirdAlias, StringComparison.Ordinal))))
            {
                throw new InvalidOperationException("MovementLab pre-bake source review marker has conflicting " + label + " identities.");
            }

            var value = string.IsNullOrWhiteSpace(primary) ? alias : primary;
            if (string.IsNullOrWhiteSpace(value)) value = thirdAlias;
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            {
                return null;
            }

            return value;
        }

        private static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes ?? Array.Empty<byte>())).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static void EnsureDurableEvidencePath(string path, string projectRoot)
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
            var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Unable to resolve pre-bake evidence directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory, ".tmp-" + Guid.NewGuid().ToString("N"));
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
