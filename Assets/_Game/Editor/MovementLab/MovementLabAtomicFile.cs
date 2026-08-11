using System.IO;

namespace RocketFooxball.Editor
{
    internal static class MovementLabAtomicFile
    {
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
