using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RocketFooxball.Editor
{
    internal enum MovementLabManifestReadStatus
    {
        Missing,
        Current,
        Legacy,
        Unreadable
    }

    internal sealed class MovementLabManifestReadResult
    {
        internal MovementLabManifestReadResult(MovementLabManifestReadStatus status, MovementLabGeneratedState state, string error)
        {
            Status = status;
            State = state;
            Error = error;
        }

        internal MovementLabManifestReadStatus Status { get; }
        internal MovementLabGeneratedState State { get; }
        internal string Error { get; }
    }

    internal static class MovementLabManifestStore
    {
        private const int AuthorizationSchemaVersion = 1;
        private const string AuthorizationFileName = "movement-lab-manifest-rebuild-authorization.json";
        private static bool rebuildAuthorizationLease;

        private sealed class ManifestValidation
        {
            internal readonly List<string> StructuralViolations = new List<string>();
            internal readonly List<string> DerivedViolations = new List<string>();

            internal bool IsStructurallyValid => StructuralViolations.Count == 0;
            internal bool IsFullyValid => IsStructurallyValid && DerivedViolations.Count == 0;
        }

        [Serializable]
        private sealed class RebuildAuthorization
        {
            public int schemaVersion;
            public int manifestSchemaVersion;
            public string gitSha;
            public string reason;
            public string issuedUtc;
        }

        internal static MovementLabManifestReadResult Read()
        {
            var path = ResolveProjectPath(MovementLabContract.ManifestPath);
            if (!File.Exists(path))
            {
                return new MovementLabManifestReadResult(MovementLabManifestReadStatus.Missing, null, null);
            }

            try
            {
                var state = JsonUtility.FromJson<MovementLabGeneratedState>(File.ReadAllText(path));
                if (state == null)
                {
                    return new MovementLabManifestReadResult(MovementLabManifestReadStatus.Unreadable, null, "manifest JSON produced no state");
                }

                var status = state.schemaVersion == MovementLabContract.ManifestSchemaVersion
                    ? MovementLabManifestReadStatus.Current
                    : MovementLabManifestReadStatus.Legacy;
                return new MovementLabManifestReadResult(status, state, null);
            }
            catch (Exception exception)
            {
                return new MovementLabManifestReadResult(MovementLabManifestReadStatus.Unreadable, null, exception.Message);
            }
        }

        internal static bool IsCurrentAndReadable(MovementLabGeneratedState state)
        {
            return Validate(state).IsFullyValid;
        }

        private static ManifestValidation Validate(MovementLabGeneratedState state)
        {
            var validation = new ManifestValidation();
            if (state == null)
            {
                validation.StructuralViolations.Add("state is null");
                return validation;
            }

            if (state.schemaVersion != MovementLabContract.ManifestSchemaVersion)
            {
                validation.StructuralViolations.Add("schemaVersion is not current");
            }
            if (state.stages == null)
            {
                validation.StructuralViolations.Add("stages are missing");
            }
            if (string.IsNullOrWhiteSpace(state.sourceSignature))
            {
                validation.StructuralViolations.Add("sourceSignature is missing");
            }
            if (string.IsNullOrWhiteSpace(state.generatedOutputFingerprint))
            {
                validation.StructuralViolations.Add("generatedOutputFingerprint is missing");
            }
            if (string.IsNullOrWhiteSpace(state.unityVersion))
            {
                validation.StructuralViolations.Add("unityVersion is missing");
            }
            if (string.IsNullOrWhiteSpace(state.gitSha) || state.gitSha.Length != 40)
            {
                validation.StructuralViolations.Add("gitSha is not a 40-character value");
            }
            if (string.IsNullOrWhiteSpace(state.manifestStatus) ||
                (state.manifestStatus != "current" && state.manifestStatus != "stale"))
            {
                validation.StructuralViolations.Add("manifestStatus is invalid");
            }
            if (string.IsNullOrWhiteSpace(state.bakedProfile))
            {
                validation.StructuralViolations.Add("bakedProfile is missing");
            }
            if (state.fingerprintPaths == null || state.fingerprintHashes == null)
            {
                validation.StructuralViolations.Add("fingerprint arrays are missing");
            }
            else if (state.fingerprintPaths.Length != state.fingerprintHashes.Length)
            {
                validation.StructuralViolations.Add("fingerprint array counts differ");
            }
            if (state.staleStages == null || state.staleReasons == null)
            {
                validation.StructuralViolations.Add("stale arrays are missing");
            }
            else if (state.staleStages.Length != state.staleReasons.Length)
            {
                validation.StructuralViolations.Add("stale array counts differ");
            }

            if (state.staleStages != null && state.staleReasons != null && state.staleStages.Length == state.staleReasons.Length)
            {
                if (state.manifestStatus == "current" && state.staleStages.Length != 0)
                {
                    validation.StructuralViolations.Add("current manifest has stale stages");
                }
                var staleStageNames = new HashSet<string>(StringComparer.Ordinal);
                for (var staleIndex = 0; staleIndex < state.staleStages.Length; staleIndex++)
                {
                    if (string.IsNullOrWhiteSpace(state.staleStages[staleIndex]) ||
                        !Enum.IsDefined(typeof(MovementLabStage), state.staleStages[staleIndex]) ||
                        !staleStageNames.Add(state.staleStages[staleIndex]) ||
                        string.IsNullOrWhiteSpace(state.staleReasons[staleIndex]))
                    {
                        validation.StructuralViolations.Add("stale stage/reason entry is invalid at index " + staleIndex);
                    }
                }
            }

            var fingerprintPaths = new HashSet<string>(StringComparer.Ordinal);
            if (state.fingerprintPaths != null && state.fingerprintHashes != null &&
                state.fingerprintPaths.Length == state.fingerprintHashes.Length)
            {
                for (var i = 0; i < state.fingerprintPaths.Length; i++)
                {
                    try
                    {
                        var normalized = NormalizeRepositoryPath(state.fingerprintPaths[i]);
                        if (!string.Equals(normalized, state.fingerprintPaths[i], StringComparison.Ordinal) ||
                            !fingerprintPaths.Add(normalized) || string.IsNullOrWhiteSpace(state.fingerprintHashes[i]))
                        {
                            validation.StructuralViolations.Add("fingerprint path/hash entry is invalid at index " + i);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        validation.StructuralViolations.Add("fingerprint path is unsafe at index " + i);
                    }
                }
            }

            // Assemble manifests intentionally stop at Quality; baked manifests include all stages.
            var requiredStages = new[]
            {
                MovementLabStage.Importer.ToString(),
                MovementLabStage.MaterialPrefab.ToString(),
                MovementLabStage.GameplayScene.ToString(),
                MovementLabStage.Quality.ToString()
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var outputPathDigests = new Dictionary<string, MovementLabPathDigest>(StringComparer.Ordinal);
            if (state.stages != null)
            {
                for (var i = 0; i < state.stages.Length; i++)
                {
                    var record = state.stages[i];
                    if (record == null || string.IsNullOrWhiteSpace(record.stage) ||
                        !Enum.IsDefined(typeof(MovementLabStage), record.stage) || !seen.Add(record.stage) ||
                        record.schemaVersion != MovementLabContract.ManifestSchemaVersion ||
                        string.IsNullOrWhiteSpace(record.contractVersion) || string.IsNullOrWhiteSpace(record.inputDigest) ||
                        string.IsNullOrWhiteSpace(record.repositoryInputDigest) || string.IsNullOrWhiteSpace(record.dependencyDigest) ||
                        string.IsNullOrWhiteSpace(record.outputDigest) || record.unityVersion == null ||
                        record.predecessorDigests == null || record.outputs == null)
                    {
                        validation.StructuralViolations.Add("stage record is invalid at index " + i);
                        continue;
                    }

                    var stageOutputPaths = new HashSet<string>(StringComparer.Ordinal);
                    for (var outputIndex = 0; outputIndex < record.outputs.Length; outputIndex++)
                    {
                        var output = record.outputs[outputIndex];
                        string normalizedOutputPath = null;
                        try
                        {
                            normalizedOutputPath = output == null ? null : NormalizeRepositoryPath(output.path);
                        }
                        catch (InvalidOperationException)
                        {
                            validation.StructuralViolations.Add("stage output path is unsafe at " + record.stage + ":" + outputIndex);
                        }
                        if (output == null || string.IsNullOrWhiteSpace(output.path) ||
                            !stageOutputPaths.Add(output.path) ||
                            !string.Equals(normalizedOutputPath, output.path, StringComparison.Ordinal) ||
                            (!output.missing && string.IsNullOrWhiteSpace(output.digest)))
                        {
                            validation.StructuralViolations.Add("stage output entry is invalid at " + record.stage + ":" + outputIndex);
                            continue;
                        }

                        // Shared output paths are valid; later baked ownership supersedes earlier ownership.
                        outputPathDigests[output.path] = output;
                    }
                }
            }

            for (var i = 0; i < requiredStages.Length; i++)
            {
                if (!seen.Contains(requiredStages[i]))
                {
                    validation.StructuralViolations.Add("required stage is missing: " + requiredStages[i]);
                }
            }

            // These checks are intentionally derived. Existing trusted reads remain strict,
            // but outgoing writes report drift without making byte equality a gate.
            if (state.fingerprintPaths != null && state.fingerprintHashes != null &&
                state.fingerprintPaths.Length == state.fingerprintHashes.Length)
            {
                var expectedPaths = outputPathDigests.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
                if (!expectedPaths.SequenceEqual(state.fingerprintPaths, StringComparer.Ordinal))
                {
                    validation.DerivedViolations.Add("fingerprint path union differs from stage outputs");
                }
                var comparableCount = Math.Min(expectedPaths.Length, state.fingerprintHashes.Length);
                for (var i = 0; i < comparableCount; i++)
                {
                    if (!outputPathDigests.TryGetValue(expectedPaths[i], out var output)) continue;
                    var expectedHash = output.missing ? Sha256Text("missing:" + output.path + "\n") : output.digest;
                    if (!string.Equals(expectedHash, state.fingerprintHashes[i], StringComparison.Ordinal))
                    {
                        validation.DerivedViolations.Add("fingerprint hash mismatch at " + expectedPaths[i]);
                    }
                }
            }

            if (state.stages != null)
            {
                var expectedSource = Sha256Text(string.Join("\n", state.stages.Where(record => record != null)
                    .Select(record => record.stage + ":" + record.inputDigest)
                    .OrderBy(value => value, StringComparer.Ordinal)) + "\n");
                var expectedOutput = Sha256Text(string.Join("\n", state.stages.Where(record => record != null)
                    .Select(record => record.stage + ":" + record.outputDigest)
                    .OrderBy(value => value, StringComparer.Ordinal)) + "\n");
                if (!string.Equals(expectedSource, state.sourceSignature, StringComparison.Ordinal))
                {
                    validation.DerivedViolations.Add("source signature differs from stage inputs");
                }
                if (!string.Equals(expectedOutput, state.generatedOutputFingerprint, StringComparison.Ordinal))
                {
                    validation.DerivedViolations.Add("generated output fingerprint differs from stage outputs");
                }
            }

            return validation;
        }

        internal static void WriteAtomic(MovementLabGeneratedState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var validation = Validate(state);
            if (!validation.IsStructurallyValid)
            {
                throw new InvalidOperationException("MovementLab manifest write rejected: structural/schema/path/stage violations: " +
                    string.Join("; ", validation.StructuralViolations));
            }
            for (var i = 0; i < validation.DerivedViolations.Count; i++)
            {
                UnityEngine.Debug.LogWarning("MovementLab manifest derived consistency warning: " + validation.DerivedViolations[i]);
            }
            var read = Read();
            if (read.Status != MovementLabManifestReadStatus.Current || !IsCurrentAndReadable(read.State))
            {
                if (!rebuildAuthorizationLease)
                {
                    throw new InvalidOperationException("MovementLab manifest is unreadable, legacy, or structurally invalid. Explicit full-rebuild authorization required before writing.");
                }
            }

            var path = ResolveProjectPath(MovementLabContract.ManifestPath);
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Unable to resolve build manifest directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(state, true) + "\n");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) MovementLabAtomicFile.ReplaceAtomicWithRetry(temporaryPath, path);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            rebuildAuthorizationLease = false;
        }

        internal static void EnsureWriteAuthorization()
        {
            var read = Read();
            if (read.Status == MovementLabManifestReadStatus.Current && IsCurrentAndReadable(read.State))
            {
                return;
            }

            var authorizationPath = ResolveAuthorizationPath();
            if (!File.Exists(authorizationPath))
            {
                throw new InvalidOperationException("MovementLab manifest is unreadable, legacy, or structurally invalid. Run 'Rocket Fooxball/Authorize Movement Lab Manifest Migration' before writing.");
            }

            RebuildAuthorization authorization;
            try
            {
                authorization = JsonUtility.FromJson<RebuildAuthorization>(File.ReadAllText(authorizationPath));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("MovementLab manifest rebuild authorization could not be parsed: " + exception.Message, exception);
            }

            var currentSha = ReadGitSha();
            if (authorization == null || authorization.schemaVersion != AuthorizationSchemaVersion ||
                authorization.manifestSchemaVersion != MovementLabContract.ManifestSchemaVersion ||
                !string.Equals(authorization.gitSha, currentSha, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(authorization.reason) ||
                !DateTime.TryParse(authorization.issuedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _))
            {
                throw new InvalidOperationException("MovementLab manifest rebuild authorization is stale or incomplete; exact current Git SHA and manifest schema required.");
            }

            // Consume before any generated writer. Failed rebuilds require fresh explicit authorization.
            File.Delete(authorizationPath);
            rebuildAuthorizationLease = true;
        }

        internal static string AuthorizeManifestMigration(string reason = "explicit full rebuild")
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Authorization reason is required.", nameof(reason));
            var path = ResolveAuthorizationPath();
            var authorization = new RebuildAuthorization
            {
                schemaVersion = AuthorizationSchemaVersion,
                manifestSchemaVersion = MovementLabContract.ManifestSchemaVersion,
                gitSha = ReadGitSha(),
                reason = reason.Trim(),
                issuedUtc = DateTime.UtcNow.ToString("O")
            };
            WriteOutsideProjectAtomic(path, JsonUtility.ToJson(authorization, true) + "\n");
            return path;
        }

        internal static string GetCurrentGitSha()
        {
            return ReadGitSha();
        }

        internal static string ResolveAuthorizationPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var commonDirectoryText = RunGit(projectRoot, "rev-parse --git-common-dir");
            var commonDirectory = Path.IsPathRooted(commonDirectoryText)
                ? commonDirectoryText
                : Path.Combine(projectRoot, commonDirectoryText);
            var evidenceDirectory = Path.GetFullPath(Path.Combine(commonDirectory, "architecture-evidence"));
            var path = Path.GetFullPath(Path.Combine(evidenceDirectory, AuthorizationFileName));
            EnsureOutsideProject(path, projectRoot);
            return path;
        }

        internal static string ResolveProjectPath(string repositoryRelativePath)
        {
            var normalized = NormalizeRepositoryPath(repositoryRelativePath);
            var root = Directory.GetParent(Application.dataPath);
            if (root == null) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var absolute = Path.GetFullPath(Path.Combine(root.FullName, normalized.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = Path.GetFullPath(root.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unsafe MovementLab path: " + repositoryRelativePath);
            }
            return absolute;
        }

        internal static string NormalizeRepositoryPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.IndexOf(':') >= 0)
            {
                throw new InvalidOperationException("Unsafe MovementLab path: " + path);
            }
            var normalized = path.Replace('\\', '/');
            var segments = normalized.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                {
                    throw new InvalidOperationException("Unsafe MovementLab path: " + path);
                }
            }
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) &&
                !normalized.StartsWith("ProjectSettings/", StringComparison.Ordinal) &&
                !normalized.StartsWith("Packages/", StringComparison.Ordinal) &&
                !normalized.StartsWith("Tools/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unsafe MovementLab path: " + path);
            }
            return normalized;
        }

        private static string ReadGitSha()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            var sha = RunGit(projectRoot, "rev-parse HEAD");
            if (sha.Length != 40) throw new InvalidOperationException("MovementLab manifest authorization requires exact 40-character Git SHA.");
            return sha;
        }

        private static string Sha256Text(string value)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)))
                    .Replace("-", string.Empty).ToLowerInvariant();
            }
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
                if (process == null) throw new InvalidOperationException("Unable to start Git for MovementLab manifest authorization.");
                var output = process.StandardOutput.ReadToEnd().Trim();
                var error = process.StandardError.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("MovementLab manifest authorization Git query failed: " + error);
                return output;
            }
        }

        private static void EnsureOutsideProject(string path, string projectRoot)
        {
            var fullPath = Path.GetFullPath(path);
            var projectPrefix = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("MovementLab manifest authorization must be outside worktree: " + fullPath);
            }
            if (fullPath.IndexOf(Path.DirectorySeparatorChar + "Library" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullPath.IndexOf(Path.DirectorySeparatorChar + "Temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException("MovementLab manifest authorization path is not durable: " + fullPath);
            }
        }

        private static void WriteOutsideProjectAtomic(string path, string content)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) throw new InvalidOperationException("Unable to resolve Unity project root.");
            EnsureOutsideProject(path, projectRoot);
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Unable to resolve MovementLab manifest authorization directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) MovementLabAtomicFile.ReplaceAtomicWithRetry(temporaryPath, path);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
