using System.Globalization;
using System.IO;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The Launcher's own log: <c>logs\launcher.log</c> in the Pounce home, next to the Engine's log.
/// Safe from any thread, and never throws: a log that cannot be written is not worth crashing for.
/// </summary>
/// <remarks>
/// Bounded: every <see cref="WritesPerSizeCheck"/> writes (and at the first one) the size is checked, and a file past
/// 1 MB is moved to <c>launcher.old.log</c> (replacing the one before) and a new one started; and an exception whose
/// stack trace repeats the previous one is written as one line with a repeat count (<see cref="RepeatedTraces"/>).
/// So a failure that recurs many times a second costs a line each, and the logs never take more than about 2 MB.
/// A second Launcher (a second launch, or <c>--quit</c>) writes its few lines to the same file; each write opens and
/// closes it, so they interleave rather than clash, and a write that meets the file busy is dropped.
/// </remarks>
public static class Log
{
    private const long MaxBytes = 1_000_000;
    private const int WritesPerSizeCheck = 100;
    private static readonly Lock Gate = new();
    private static readonly RepeatedTraces Traces = new();
    private static string? file;
    private static int writesSinceSizeCheck;

    /// <summary>The full path of the log file, or null before <see cref="Start"/> or when it could not be set up.</summary>
    public static string? Location
    {
        get
        {
            lock (Gate)
            {
                return file;
            }
        }
    }

    /// <summary>Sets where the log goes; until then, messages are dropped.</summary>
    public static void Start(string logFile)
    {
        lock (Gate)
        {
            try
            {
                string full = Path.GetFullPath(logFile);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                file = full;
                writesSinceSizeCheck = WritesPerSizeCheck; // check at the first write
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
            if (++writesSinceSizeCheck >= WritesPerSizeCheck)
            {
                writesSinceSizeCheck = 0;
                RotateIfFull(file);
            }
            try
            {
                var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                var line = exception is null
                    ? $"{time} {level} {message}"
                    : $"{time} {level} {message}: {Traces.Describe(exception.ToString(), $"{exception.GetType().FullName}: {exception.Message}")}";
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Nothing sensible to do (the file is busy or gone, or the exception could not describe itself).
            }
        }
    }

    /// <summary>Moves a log past <see cref="MaxBytes"/> to <c>&lt;name&gt;.old.log</c>, replacing the previous one.</summary>
    private static void RotateIfFull(string logFile)
    {
        try
        {
            var info = new FileInfo(logFile);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(logFile, Path.ChangeExtension(logFile, ".old.log"), overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Busy (another Launcher writing): tried again at the next check.
        }
    }
}
