using System.Globalization;
using System.IO;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The Launcher's own log: <c>logs\launcher.log</c> in the Pounce home, next to the Engine's log.
/// Safe from any thread, and never throws: a log that cannot be written is not worth crashing for.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly Lock Gate = new();
    private static string? file;

    /// <summary>Sets where the log goes; until then, messages are dropped. Starts a new file once it passes 1 MB.</summary>
    public static void Start(string logFile)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
                if (File.Exists(logFile) && new FileInfo(logFile).Length > MaxBytes)
                {
                    File.Delete(logFile);
                }
                file = logFile;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                file = null;
            }
        }
    }

    /// <summary>Writes one line.</summary>
    public static void Info(string message) => Write("INFO", message, null);

    /// <summary>Writes one line, with the exception when there is one.</summary>
    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Gate)
        {
            if (file is null)
            {
                return;
            }
            try
            {
                var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var line = exception is null ? $"{time} {level} {message}" : $"{time} {level} {message}: {exception}";
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Nothing sensible to do.
            }
        }
    }
}
