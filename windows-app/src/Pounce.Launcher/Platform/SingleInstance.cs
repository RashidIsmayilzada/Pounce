namespace Pounce.Launcher.Platform;

/// <summary>
/// One Pounce per signed-in user. A second launch asks the running one to show its panel and exits;
/// <c>Pounce.exe --quit</c> asks the running one to quit. Placeholder: always the first instance.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private SingleInstance() { }

    /// <summary>Raised (on any thread) when another launch asks this one to show the panel.</summary>
    public event EventHandler? ShowRequested { add { } remove { } }

    /// <summary>Raised (on any thread) when <c>Pounce.exe --quit</c> asks this one to quit.</summary>
    public event EventHandler? QuitRequested { add { } remove { } }

    /// <summary>Becomes the running instance, or returns null when another Pounce already is.</summary>
    public static SingleInstance? TryAcquire() => new();

    /// <summary>Asks the running instance to show its panel. Does nothing when none is running.</summary>
    public static void SignalShow() { }

    /// <summary>Asks the running instance to quit and waits until it has exited or the time is up.
    /// True when no instance is running afterwards.</summary>
    public static bool SignalQuit(TimeSpan timeout) => timeout >= TimeSpan.Zero;

    /// <inheritdoc/>
    public void Dispose() { }
}
