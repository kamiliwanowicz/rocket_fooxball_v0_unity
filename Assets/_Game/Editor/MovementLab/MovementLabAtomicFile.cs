using System;
using System.IO;

namespace RocketFooxball.Editor
{
    internal static class MovementLabAtomicFile
    {
        internal static void WriteAllBytesAtomic(byte[] bytes, string destinationPath)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (string.IsNullOrEmpty(destinationPath)) throw new ArgumentException("A destination path is required.", nameof(destinationPath));

            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Unable to resolve destination directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(destinationPath)) ReplaceAtomicWithRetry(temporaryPath, destinationPath);
                else File.Move(temporaryPath, destinationPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        internal static void ReplaceAtomicWithRetry(string temporaryPath, string destinationPath)
        {
            var retryDelaysMilliseconds = new[] { 50, 150, 300 };
            for (var attempt = 0; attempt <= retryDelaysMilliseconds.Length; attempt++)
            {
                try
                {
                    File.Replace(temporaryPath, destinationPath, null);
                    return;
                }
                catch (IOException exception)
                {
                    var win32Error = exception.HResult & 0xFFFF;
                    var retryable = win32Error == 32 || win32Error == 33 || win32Error == 1175;
                    if (!retryable || attempt == retryDelaysMilliseconds.Length)
                    {
                        throw;
                    }

                    System.Threading.Thread.Sleep(retryDelaysMilliseconds[attempt]);
                    if (!File.Exists(destinationPath) || !File.Exists(temporaryPath))
                    {
                        throw;
                    }
                }
            }
        }
    }
}
