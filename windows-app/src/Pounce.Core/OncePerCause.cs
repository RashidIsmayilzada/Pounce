namespace Pounce.Core;

/// <summary>
/// Says yes the first time it sees a cause and no after that, so a failure that repeats for every row or every
/// keystroke is logged once instead of flooding the log. Remembers at most a fixed number of causes; past that, it
/// says no to everything, so a stream of distinct causes cannot flood the log either. Thread-safe.
/// </summary>
public sealed class OncePerCause
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly int _limit;

    /// <summary>Remembers up to <paramref name="limit"/> causes.</summary>
    public OncePerCause(int limit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        _limit = limit;
    }

    /// <summary>True the first time <paramref name="cause"/> is seen (until the limit is reached); false afterwards.</summary>
    public bool IsFirst(string cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        lock (_gate)
        {
            return _seen.Count < _limit && _seen.Add(cause);
        }
    }
}
