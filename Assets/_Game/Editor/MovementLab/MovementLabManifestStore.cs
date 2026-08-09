using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace RocketFooxball.Editor
{
    internal static class MovementLabManifestStore
    {
        internal static MovementLabGeneratedState ReadOrNull()
        {
            var path = ResolveProjectPath(MovementLabContract.ManifestPath);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<MovementLabGeneratedState>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("build manifest could not be parsed: " + exception.Message, exception);
            }
        }

        internal static void WriteAtomic(MovementLabGeneratedState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
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
    }
}
