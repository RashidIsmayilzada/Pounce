namespace Pounce.Core;

/// <summary>
/// Whether the Engine the Launcher is waiting for answers yet. The bundled Engine needs about half a minute after it
/// is launched before it answers (it loads its model and opens its Index; its web server binds last), while
/// <see cref="EngineSupervisor"/> reports <see cref="EngineStatus.Running"/> as soon as the process exists. Meanwhile
/// <see cref="IsStarting"/> is true, so the panel can say "starting" instead of "isn't running".
/// </summary>
/// <remarks>
/// <para>Feed it the supervisor's states with <see cref="Follow"/>: <see cref="EngineStatus.Running"/> starts a wait
/// that asks the probe every <see cref="DefaultInterval"/> until the Engine answers; <see cref="EngineStatus.Reusing"/>
/// means one already answers; <see cref="EngineStatus.Failed"/> and <see cref="EngineStatus.Stopped"/> end the wait.
/// Before the supervisor has decided anything, call <see cref="Expect"/>. A wait gives up after
/// <see cref="DefaultMaxWait"/>, so an Engine that never answers does not leave the panel "starting" forever.</para>
/// <para>Threading: create and use it on the UI thread, like <see cref="EngineSupervisor"/>. The probe and the delay
/// are awaited on the captured <see cref="SynchronizationContext"/>, so <see cref="IsStarting"/> changes and
/// <see cref="Changed"/> is raised only on that thread.</para>
/// </remarks>
public sealed class EngineReadiness
{
    /// <summary>How long to wait between two probes that found no Engine (the probe itself gives up after 1 second).</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long after a launch to stop waiting and let the real problem show.</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromMinutes(3);

    private readonly IEngineProbe _probe;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _maxWait;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan> _clock;
    private CancellationTokenSource? _wait;
    private EngineStatus? _followed;

    /// <summary>Waits for the Engine that <paramref name="probe"/> asks.</summary>
    /// <param name="probe">Tells whether the Engine answers.</param>
    /// <param name="interval">The pause between probes; <see cref="DefaultInterval"/> when null.</param>
    /// <param name="maxWait">When to give up; <see cref="DefaultMaxWait"/> when null.</param>
    /// <param name="delay">Waits for a time (for tests); <see cref="Task.Delay(TimeSpan, CancellationToken)"/> when null.</param>
    /// <param name="clock">A monotonic clock (for tests); <see cref="Environment.TickCount64"/> when null.</param>
    public EngineReadiness(IEngineProbe probe, TimeSpan? interval = null, TimeSpan? maxWait = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<TimeSpan>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        _interval = interval ?? DefaultInterval;
        _maxWait = maxWait ?? DefaultMaxWait;
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    /// <summary>Raised on the UI thread after <see cref="IsStarting"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// True from <see cref="Expect"/> or the start of a wait until the Engine first answers, the supervisor reports
    /// that it reuses, failed or stopped, or the wait is cancelled or gives up.
    /// </summary>
    public bool IsStarting { get; private set; }

    /// <summary>How long the last wait took until the Engine answered; null when it did not end that way (yet).</summary>
    public TimeSpan? ReadyAfter { get; private set; }

    /// <summary>How long the last wait went on before it gave up; null when it did not give up.</summary>
    public TimeSpan? GaveUpAfter { get; private set; }

    /// <summary>
    /// The supervisor is still finding out whether an Engine already runs, and has launched nothing yet: starting,
    /// without probing (the supervisor does that). The next <see cref="Follow"/> decides what comes next.
    /// </summary>
    public void Expect()
    {
        _followed = null;
        if (_wait is null)
        {
            ReadyAfter = null;
            GaveUpAfter = null;
            SetStarting(true);
        }
    }

    /// <summary>
    /// Follows the supervisor's state: <see cref="EngineStatus.Running"/> starts waiting (unless already waiting for
    /// it), <see cref="EngineStatus.Reusing"/> is ready at once, <see cref="EngineStatus.Failed"/> and
    /// <see cref="EngineStatus.Stopped"/> end any wait. The same status twice in a row changes nothing.
    /// </summary>
    public void Follow(EngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_followed == state.Status)
        {
            return;
        }
        _followed = state.Status;
        if (state.Status == EngineStatus.Running)
        {
            _ = WaitAsync();
        }
        else
        {
            // Reusing: it answers already. Failed or Stopped: there is nothing to wait for, so the real problem shows.
            Abandon();
        }
    }

    /// <summary>
    /// An Engine was just launched: waits until it answers. Starts a new wait (with its own clock) even when one is
    /// running already, which then ends quietly.
    /// </summary>
    /// <returns>True when the Engine answered; false when the wait was abandoned, cancelled or gave up.</returns>
    public async Task<bool> WaitAsync(CancellationToken cancellationToken = default)
    {
        // Never disposed: it has no timer, and its cancel may still be running callbacks when this wait ends.
        var wait = new CancellationTokenSource();
        CancellationTokenSource? previous = _wait;
        // Replaced before the cancel: a continuation of the previous wait that runs inline must see it is no longer current.
        _wait = wait;
        previous?.Cancel();
        ReadyAfter = null;
        GaveUpAfter = null;
        TimeSpan started = _clock();
        SetStarting(true);
        using CancellationTokenRegistration link = cancellationToken.Register(static state => ((CancellationTokenSource)state!).Cancel(), wait);
        try
        {
            while (true)
            {
                bool alive;
                try
                {
                    alive = await _probe.IsAliveAsync(wait.Token);
                }
                catch (Exception) when (!wait.IsCancellationRequested)
                {
                    alive = false; // A probe that fails counts as an Engine that doesn't answer yet.
                }
                wait.Token.ThrowIfCancellationRequested();
                TimeSpan waited = _clock() - started;
                if (alive)
                {
                    End(wait, ready: waited, gaveUp: null);
                    return true;
                }
                if (waited >= _maxWait)
                {
                    End(wait, ready: null, gaveUp: waited);
                    return false;
                }
                await _delay(_interval, wait.Token);
            }
        }
        catch (Exception)
        {
            // Cancelled (by the caller, by Abandon or by a newer wait), or the delay failed: this wait is over.
            End(wait, ready: null, gaveUp: null);
            return false;
        }
    }

    /// <summary>Stops waiting: the Engine answers already, or there is no Engine to wait for. Not starting any more.</summary>
    public void Abandon()
    {
        CancellationTokenSource? wait = _wait;
        _wait = null;
        wait?.Cancel();
        SetStarting(false);
    }

    private void End(CancellationTokenSource wait, TimeSpan? ready, TimeSpan? gaveUp)
    {
        if (_wait != wait)
        {
            return;
        }
        _wait = null;
        ReadyAfter = ready;
        GaveUpAfter = gaveUp;
        SetStarting(false);
    }

    private void SetStarting(bool starting)
    {
        if (starting == IsStarting)
        {
            return;
        }
        IsStarting = starting;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
