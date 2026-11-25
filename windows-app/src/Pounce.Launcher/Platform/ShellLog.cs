using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// Logs a shell failure once per distinct cause: the same HRESULT from the same call for every row of every search
/// would otherwise flood the log.
/// </summary>
internal static class ShellLog
{
    private static readonly OncePerCause Seen = new(limit: 200);

    /// <summary>Logs <paramref name="message"/> the first time <paramref name="cause"/> is seen.</summary>
    public static void WarnOnce(string cause, string message, Exception? exception = null)
    {
        if (Seen.IsFirst(cause))
        {
            Log.Warn(message + " (logged once)", exception);
        }
    }

    /// <summary>Logs an exception once per call site and exception type.</summary>
    public static void WarnOnce(string where, Exception exception) =>
        WarnOnce(where + " " + exception.GetType().FullName + " " + ShellNative.Hex(exception.HResult), where + " failed", exception);
}
