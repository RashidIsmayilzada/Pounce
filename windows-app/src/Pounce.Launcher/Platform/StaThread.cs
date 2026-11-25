using System.Windows.Threading;

namespace Pounce.Launcher.Platform;

/// <summary>
/// One long-lived single-threaded-apartment thread for the shell's COM objects, shared by
/// <see cref="WindowsAppSource"/> and <see cref="ShellIconProvider"/>. Shell objects want an STA, and the callers are
/// the UI thread (which must not block) and thread-pool threads (which are MTA).
/// </summary>
/// <remarks>
/// The thread runs a WPF <see cref="Dispatcher"/>, which pumps window messages, so shell objects that rely on
/// messages, cross-apartment calls into this apartment and the release of any COM object left to the finalizer all
/// work. It is a background thread: it never keeps Pounce running on quit. Work runs by priority, then in order; an
/// exception in one item is logged and the thread goes on with the next.
/// </remarks>
internal sealed class StaThread
{
    private static readonly Lazy<StaThread> SharedThread = new(() => new StaThread("Pounce shell (STA)"));

    private readonly Dispatcher dispatcher;

    private StaThread(string name)
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            created.UnhandledException += (_, e) =>
            {
                Log.Warn("Shell thread: unexpected error", e.Exception);
                e.Handled = true;
            };
            ready.Set();
            while (!created.HasShutdownStarted)
            {
                try
                {
                    Dispatcher.Run();
                }
                catch (Exception e)
                {
                    Log.Warn("Shell thread: the message loop failed; restarting it", e);
                }
            }
        })
        {
            IsBackground = true,
            Name = name,
        };
        // Before Start: the runtime then initializes COM on the thread as an STA (CoInitializeEx with COINIT_APARTMENTTHREADED).
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        dispatcher = created!;
    }

    /// <summary>The shared thread, started on first use.</summary>
    public static StaThread Shared => SharedThread.Value;

    /// <summary>Runs <paramref name="action"/> on the thread, later. Never throws; an exception it throws is logged.</summary>
    public void Post(Action action, DispatcherPriority priority)
    {
        dispatcher.BeginInvoke(priority, new Action(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warn("Shell thread: a work item failed", e);
            }
        }));
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the thread and completes with its result or exception. The continuation never
    /// runs inline on this thread.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> work, DispatcherPriority priority)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                result.SetResult(work());
            }
            catch (Exception e)
            {
                result.SetException(e);
            }
        }, priority);
        return result.Task;
    }
}
