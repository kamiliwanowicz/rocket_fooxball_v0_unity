using System;
using System.Diagnostics;
using System.IO;
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

        internal static MovementLabGeneratedState ReadOrNull()
        {
            var result = Read();
            if (result.Status == MovementLabManifestReadStatus.Unreadable)
            {
                throw new InvalidOperationException("build manifest could not be parsed: " + result.Error);
            }

            return result.State;
        }

        internal static bool IsCurrentAndReadable(MovementLabGeneratedState state)
        {
            if (state == null || state.schemaVersion != MovementLabContract.ManifestSchemaVersion || state.stages == null ||
                string.IsNullOrWhiteSpace(state.sourceSignature) || string.IsNullOrWhiteSpace(state.generatedOutputFingerprint) ||
                string.IsNullOrWhiteSpace(state.unityVersion) || state.fingerprintPaths == null)
            {
                return false;
            }

            var fingerprintPaths = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < state.fingerprintPaths.Length; i++)
            {
                try
                {
                    var normalized = NormalizeRepositoryPath(state.fingerprintPaths[i]);
                    if (!string.Equals(normalized, state.fingerprintPaths[i], StringComparison.Ordinal) || !fingerprintPaths.Add(normalized)) return false;
                }
                catch (InvalidOperationException)
                {
                    return false;
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
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < state.stages.Length; i++)
            {
                var record = state.stages[i];
                if (record == null || string.IsNullOrWhiteSpace(record.stage) || !Enum.IsDefined(typeof(MovementLabStage), record.stage) || !seen.Add(record.stage) ||
                    record.schemaVersion != MovementLabContract.ManifestSchemaVersion ||
                    string.IsNullOrWhiteSpace(record.contractVersion) || string.IsNullOrWhiteSpace(record.inputDigest) ||
                    string.IsNullOrWhiteSpace(record.outputDigest) || record.predecessorDigests == null || record.outputs == null)
                {
                    return false;
                }

                for (var outputIndex = 0; outputIndex < record.outputs.Length; outputIndex++)
                {
                    var output = record.outputs[outputIndex];
                    if (output == null || string.IsNullOrWhiteSpace(output.path) || (!output.missing && string.IsNullOrWhiteSpace(output.digest)))
                    {
                        return false;
                    }
                }
            }

            for (var i = 0; i < requiredStages.Length; i++)
            {
                if (!seen.Contains(requiredStages[i])) return false;
            }

            return true;
        }

        internal static void WriteAtomic(MovementLabGeneratedState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
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

                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
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
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
