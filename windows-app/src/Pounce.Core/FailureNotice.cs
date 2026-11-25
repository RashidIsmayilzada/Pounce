using System.Text;

namespace Pounce.Core;

/// <summary>
/// What the Launcher tells the user when it cannot go on (it could not start, or keeps failing and has to close), and
/// whether it may say so in a dialog at all. Text only: showing it is the Launcher's job.
/// </summary>
public static class FailureNotice
{
    /// <summary>
    /// Set (to anything) to never show a dialog, for unattended runs such as CI smoke tests: a dialog nobody can close
    /// would keep the process, and the run, waiting forever. What the dialog would say is still logged.
    /// </summary>
    public const string NoDialogsVariable = "POUNCE_NO_DIALOGS";

    /// <summary>The dialog's title.</summary>
    public const string Title = "Pounce";

    /// <summary>A problem longer than this is cut off in the dialog; the log has all of it.</summary>
    public const int MaxProblemLength = 400;

    /// <summary>
    /// True when a dialog may be shown: the process runs in an interactive session
    /// (<see cref="Environment.UserInteractive"/>) and <see cref="NoDialogsVariable"/> is not set.
    /// </summary>
    /// <param name="userInteractive">The value of <see cref="Environment.UserInteractive"/>.</param>
    /// <param name="noDialogs">The value of <see cref="NoDialogsVariable"/>; null when unset.</param>
    public static bool MayShowDialog(bool userInteractive, string? noDialogs) => userInteractive && string.IsNullOrEmpty(noDialogs);

    /// <summary>The text for a startup that failed.</summary>
    /// <param name="problem">What went wrong (an exception's message), or null.</param>
    /// <param name="logFile">The full path of the log with the details, or null when there is no log.</param>
    public static string CouldNotStart(string? problem, string? logFile) =>
        Compose("Pounce couldn't start.", problem, logFile);

    /// <summary>The text for a failure that ends a running Launcher.</summary>
    /// <param name="problem">What went wrong (an exception's message), or null.</param>
    /// <param name="logFile">The full path of the log with the details, or null when there is no log.</param>
    public static string MustClose(string? problem, string? logFile) =>
        Compose("Pounce ran into a problem and has to close.", problem, logFile);

    private static string Compose(string headline, string? problem, string? logFile)
    {
        var text = new StringBuilder(headline);
        string trimmed = problem?.Trim() ?? "";
        if (trimmed.Length > 0)
        {
            if (trimmed.Length > MaxProblemLength)
            {
                trimmed = string.Concat(trimmed.AsSpan(0, MaxProblemLength), "…");
            }
            text.Append("\n\n").Append(trimmed);
        }
        text.Append("\n\n").Append(string.IsNullOrEmpty(logFile)
            ? "No log could be written."
            : "The details are in the log:\n" + logFile);
        return text.ToString();
    }
}
