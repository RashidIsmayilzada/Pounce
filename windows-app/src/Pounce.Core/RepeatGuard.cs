namespace Pounce.Core;

/// <summary>
/// Lets one event through, then drops any that follow within <see cref="Interval"/> of the last one let through.
/// A held shortcut repeats, and a toggle that flips on every repeat makes the panel flicker: only the first press
/// counts. Uses a monotonic clock the caller passes in (such as <see cref="Environment.TickCount64"/>), so it is
/// unaffected by changes to the wall clock. Not thread-safe: use it on one thread.
/// </summary>
/// <param name="interval">How long after an accepted event further events are dropped.</param>
public sealed class RepeatGuard(TimeSpan interval)
{
    private TimeSpan? _last;

    /// <summary>The time after an accepted event during which further events are dropped.</summary>
    public TimeSpan Interval { get; } = interval;

    /// <summary>True when the event at <paramref name="now"/> should be handled; false when it is a repeat.</summary>
    /// <param name="now">The time of the event on a monotonic clock.</param>
    public bool Accept(TimeSpan now)
    {
        if (_last is { } last && now - last < Interval && now >= last)
        {
            return false;
        }
        _last = now;
        return true;
    }
}
