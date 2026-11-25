namespace Pounce.Core;

/// <summary>
/// Keeps a log from filling up with the same stack trace: an exception logged again right after an identical one
/// (the same type, message and stack trace) is written as one short line with a repeat count instead of in full.
/// A different exception in between starts over. Not thread-safe: the log calls it under its own lock.
/// </summary>
public sealed class RepeatedTraces
{
    private string? _last;
    private int _repeats;

    /// <summary>
    /// The text to log for an exception: <paramref name="fullText"/> (such as <see cref="Exception.ToString"/>) the
    /// first time, then <paramref name="summary"/> with the repeat count while the same full text comes back.
    /// </summary>
    /// <param name="fullText">Everything about the exception, stack trace included.</param>
    /// <param name="summary">A one-line description, such as the type and the message.</param>
    public string Describe(string fullText, string summary)
    {
        ArgumentNullException.ThrowIfNull(fullText);
        ArgumentNullException.ThrowIfNull(summary);
        if (string.Equals(fullText, _last, StringComparison.Ordinal))
        {
            _repeats++;
            return $"{summary} (same stack trace as before, repeat {_repeats})";
        }
        _last = fullText;
        _repeats = 0;
        return fullText;
    }
}
