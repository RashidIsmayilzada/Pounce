using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Pounce.Launcher.Platform;

/// <summary>
/// One Pounce per signed-in user. A second launch asks the running one to show its panel and exits;
/// <c>Pounce.exe --quit</c> asks the running one to quit.
/// </summary>
/// <remarks>
/// Kernel objects in the session's <c>Local\</c> namespace, so each signed-in session (fast user switching, Remote
/// Desktop) has its own Pounce and the names hold nothing about the user:
/// <list type="bullet">
/// <item><c>Local\Pounce.Launcher</c>, a mutex owned by the running instance. A killed instance leaves it abandoned
/// or, with no handle left, gone; either way the next launch takes it.</item>
/// <item><c>Local\Pounce.Launcher.Show</c> and <c>Local\Pounce.Launcher.Quit</c>, auto-reset events another launch
/// sets. Both sides create-or-open them, so the order of start-up does not matter.</item>
/// <item><c>Local\Pounce.Launcher.Process</c>, 4 bytes of shared memory holding the running instance's process id,
/// so that another launch can let it take the foreground and wait for its process to end.</item>
/// </list>
/// The objects have the creating token's default security. One made by a Pounce run as administrator cannot be opened
/// by a normal launch (<see cref="UnauthorizedAccessException"/>): that launch treats it as another Pounce that runs
/// and cannot be signalled, logs so, and neither starts a second one nor waits for it. The objects' security is left
/// as it is.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Pounce.Launcher";
    private const string ShowName = @"Local\Pounce.Launcher.Show";
    private const string QuitName = @"Local\Pounce.Launcher.Quit";
    private const string ProcessName = @"Local\Pounce.Launcher.Process";

    /// <summary>What to do about a running Pounce this launch may not reach.</summary>
    private const string Elevated = "Quit that one from its tray icon (or from Task Manager), then start Pounce again.";

    private readonly Mutex mutex;
    private readonly MemoryMappedFile? processId;
    private readonly EventWaitHandle show;
    private readonly EventWaitHandle quit;
    private readonly ManualResetEvent stop = new(false);
    private readonly Thread listener;
    private readonly Lock gate = new();
    private EventHandler? showRequested;
    private EventHandler? quitRequested;
    private bool showPending;
    private bool quitPending;
    private int disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle show, EventWaitHandle quit)
    {
        this.mutex = mutex;
        this.show = show;
        this.quit = quit;
        processId = PublishProcessId();
        listener = new Thread(Listen) { IsBackground = true, Name = "Single instance" };
        listener.Start();
    }

    /// <summary>Raised (on any thread) when another launch asks this one to show the panel. A request that came
    /// before anyone subscribed is raised to the first subscriber, on the subscribing thread.</summary>
    public event EventHandler? ShowRequested
    {
        add => Subscribe(ref showRequested, ref showPending, value);
        remove
        {
            lock (gate)
            {
                showRequested -= value;
            }
        }
    }

    /// <summary>Raised (on any thread) when <c>Pounce.exe --quit</c> asks this one to quit. A request that came
    /// before anyone subscribed is raised to the first subscriber, on the subscribing thread.</summary>
    public event EventHandler? QuitRequested
    {
        add => Subscribe(ref quitRequested, ref quitPending, value);
        remove
        {
            lock (gate)
            {
                quitRequested -= value;
            }
        }
    }

    /// <summary>Becomes the running instance, or returns null when another Pounce already is.</summary>
    /// <remarks>If the kernel objects cannot be made at all, this launch runs rather than nothing running. If they
    /// exist but may not be opened, another Pounce runs (as administrator): null.</remarks>
    public static SingleInstance? TryAcquire()
    {
        EventWaitHandle? show = null;
        EventWaitHandle? quit = null;
        Mutex? mutex = null;
        try
        {
            // The events first: whoever sees the mutex owned can be sure its owner already listens to them.
            show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
            quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName);
            mutex = new Mutex(initiallyOwned: false, MutexName);
            bool owned;
            try
            {
                owned = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                owned = true; // the previous Pounce was killed: the mutex is ours now
                Log.Info("Took over the mutex of a Pounce that ended without releasing it");
            }
            if (!owned)
            {
                mutex.Dispose();
                show.Dispose();
                quit.Dispose();
                return null;
            }
            // Drop a request left by a launch that raced the previous instance's exit.
            show.Reset();
            quit.Reset();
            return new SingleInstance(mutex, show, quit);
        }
        catch (UnauthorizedAccessException e)
        {
            // The objects exist but this token may not open them: a Pounce runs under another token, which with
            // these default-security objects means one run as administrator. Running anyway would start a second
            // launcher, tray icon and Engine on the same home and port.
            Log.Warn($"Another Pounce, probably run as administrator, is already running and this one may not open "
                + $"its single-instance objects; not starting a second one. {Elevated}", e);
            mutex?.Dispose();
            show?.Dispose();
            quit?.Dispose();
            return null;
        }
        catch (Exception e)
        {
            // Without the kernel objects one Pounce cannot be told from two; running beats not running.
            Log.Warn("Could not set up the single-instance objects; running without them", e);
            mutex?.Dispose();
            show?.Dispose();
            quit?.Dispose();
            return new SingleInstance(new Mutex(initiallyOwned: true), new EventWaitHandle(false, EventResetMode.AutoReset),
                new EventWaitHandle(false, EventResetMode.AutoReset));
        }
    }

    /// <summary>Asks the running instance to show its panel. Does nothing when none is running. Never throws.</summary>
    public static void SignalShow()
    {
        try
        {
            // A window may only take the foreground when the foreground process allows it. This launch was just
            // started by the user, so it holds that right and passes it on to the running instance.
            uint target = ReadProcessId() is int id and > 0 ? (uint)id : Win32.ASFW_ANY;
            if (!Win32.AllowSetForegroundWindow(target))
            {
                Log.Warn($"AllowSetForegroundWindow({(int)target}) failed: error {Win32.LastError()}");
            }
            // The event exists while the running instance holds it; with none running, setting it reaches nobody and
            // the event vanishes when closed here.
            using var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
            show.Set();
        }
        catch (UnauthorizedAccessException e)
        {
            Log.Warn($"Another Pounce, probably run as administrator, is running and cannot be asked to show its panel. {Elevated}", e);
        }
        catch (Exception e)
        {
            Log.Warn("Could not ask the running Pounce to show its panel", e);
        }
    }

    /// <summary>Asks the running instance to quit and waits until it has exited or the time is up.
    /// True when no instance is running afterwards; false at once for one this launch may not signal, and false when
    /// it cannot be told whether one still runs. Never throws.</summary>
    public static bool SignalQuit(TimeSpan timeout)
    {
        try
        {
            int? id = ReadProcessId();
            if (id is null && !MutexExists())
            {
                return true;
            }
            // Looked up before asking; should it exit before WaitForExit opens its handle, that counts as exited.
            using Process? running = OpenProcess(id);
            if (id is not null && running is null)
            {
                return !MutexExists(); // the published process is gone already
            }
            using (var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitName))
            {
                quit.Set();
            }
            if (running is not null)
            {
                bool exited = running.WaitForExit(timeout); // the process handle: really gone, not just closing
                Log.Info(exited ? $"Pounce (pid {id}) has quit" : $"Pounce (pid {id}) still running after {timeout.TotalSeconds:0} s");
                return exited;
            }
            return WaitForMutex(timeout); // an instance that has not published its id yet
        }
        catch (UnauthorizedAccessException e)
        {
            // It cannot be told to quit, so waiting for it would only use up the time.
            Log.Warn($"Another Pounce, probably run as administrator, is running and cannot be asked to quit. {Elevated}", e);
            return false;
        }
        catch (Exception e)
        {
            Log.Warn("Could not ask the running Pounce to quit", e);
            try
            {
                return !MutexExists();
            }
            catch (Exception again)
            {
                // Unknown whether one still runs: not "none runs".
                Log.Warn("Could not tell whether a Pounce is still running", again);
                return false;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }
        stop.Set();
        if (Thread.CurrentThread != listener)
        {
            listener.Join(TimeSpan.FromSeconds(1));
        }
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread (Dispose from another thread): closing the handle below abandons it, which
            // the next launch handles.
        }
        mutex.Dispose();
        processId?.Dispose();
        show.Dispose();
        quit.Dispose();
        stop.Dispose();
    }

    private void Listen()
    {
        WaitHandle[] handles = [stop, show, quit];
        while (true)
        {
            int signalled;
            try
            {
                signalled = WaitHandle.WaitAny(handles);
            }
            catch (Exception e) when (e is ObjectDisposedException or AbandonedMutexException)
            {
                return;
            }
            if (signalled is not (1 or 2))
            {
                return; // stop: disposing
            }
            Log.Info(signalled == 1 ? "Another launch asked for the panel" : "Asked to quit by Pounce.exe --quit");
            EventHandler? handler;
            lock (gate)
            {
                // During start-up App has not subscribed yet: keep the request for its first subscriber.
                if (signalled == 1)
                {
                    handler = showRequested;
                    showPending |= handler is null;
                }
                else
                {
                    handler = quitRequested;
                    quitPending |= handler is null;
                }
            }
            Raise(handler);
        }
    }

    private void Subscribe(ref EventHandler? handlers, ref bool pending, EventHandler? value)
    {
        bool deliver;
        lock (gate)
        {
            handlers += value;
            deliver = pending && value is not null;
            if (deliver)
            {
                pending = false;
            }
        }
        if (deliver)
        {
            Raise(value);
        }
    }

    private void Raise(EventHandler? handler)
    {
        try
        {
            handler?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Warn("Handling another launch's request failed", e);
        }
    }

    /// <summary>True when some process holds the mutex open: a Pounce runs (or is just starting or ending).</summary>
    private static bool MutexExists()
    {
        if (!Mutex.TryOpenExisting(MutexName, out Mutex? existing))
        {
            return false;
        }
        existing.Dispose();
        return true;
    }

    /// <summary>Waits until the mutex is free: the fallback when the running instance's process id is unknown.</summary>
    private static bool WaitForMutex(TimeSpan timeout)
    {
        if (!Mutex.TryOpenExisting(MutexName, out Mutex? existing))
        {
            return true;
        }
        using (existing)
        {
            try
            {
                if (!existing.WaitOne(timeout))
                {
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // The owner ended: free, and now ours.
            }
            existing.ReleaseMutex();
            return true;
        }
    }

    private static MemoryMappedFile? PublishProcessId()
    {
        try
        {
            var memory = MemoryMappedFile.CreateOrOpen(ProcessName, sizeof(int), MemoryMappedFileAccess.ReadWrite);
            using MemoryMappedViewAccessor view = memory.CreateViewAccessor(0, sizeof(int));
            view.Write(0, Environment.ProcessId);
            return memory;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not publish this Pounce's process id", e);
            return null;
        }
    }

    /// <summary>The running instance's process id; null when it has not published one.</summary>
    private static int? ReadProcessId()
    {
        try
        {
            using var memory = MemoryMappedFile.OpenExisting(ProcessName, MemoryMappedFileRights.Read);
            using MemoryMappedViewAccessor view = memory.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Read);
            int id = view.ReadInt32(0);
            return id > 0 ? id : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Process? OpenProcess(int? id)
    {
        if (id is not { } pid || pid == Environment.ProcessId)
        {
            return null;
        }
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null; // already gone
        }
    }
}
