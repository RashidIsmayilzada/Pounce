using System.IO;
using System.Windows;
using System.Windows.Threading;
using Pounce.Core;
using Pounce.Launcher.Platform;

namespace Pounce.Launcher;

/// <summary>
/// Pounce for Windows: a tray app that shows the search panel on Alt+Space and keeps the bundled Engine running.
/// <code>
/// Pounce.exe          run as the Launcher (a second launch shows the running one's panel)
/// Pounce.exe --quit   ask the running Launcher to quit, and wait until it has
/// </code>
/// Honours POUNCE_URL, POUNCE_HOME, POUNCE_HOTKEY and POUNCE_NO_DIALOGS (see <see cref="FailureNotice"/>).
/// </summary>
/// <remarks>
/// <para>Failing loudly: a tray app that fails silently is the worst case, because it keeps holding the
/// single-instance mutex and every later launch just signals it and exits. So the log starts before anything else;
/// a startup that throws is logged, undone (Engine stopped, tray icon, hotkey and mutex released) and reported in one
/// message box, and the process exits with code 1; and an exception on the UI thread is only survivable once startup
/// has completed. Past that, see <see cref="OnDispatcherUnhandledException"/>.</para>
/// </remarks>
public partial class App : Application
{
    private SingleInstance? instance;
    private EngineSupervisor? supervisor;
    private HttpEngineClient? engine;
    private GlobalHotKey? hotKey;
    private TrayIcon? tray;
    private LauncherWindow? panel;
    private bool started;
    private int failing;
    private long crashBurstStart;
    private int crashBurstCount;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The log first, so that whatever happens next is written down.
        StartLog();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        if (e.Args.Contains("--quit"))
        {
            // Never a dialog here: --quit is for scripts and installers.
            Shutdown(Quit());
            return;
        }
        try
        {
            Start();
            started = true;
        }
        catch (Exception ex)
        {
            FailStartup(ex);
        }
    }

    private void Start()
    {
        instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            Log.Info($"Pounce is already running; asking it to show its panel (process {Environment.ProcessId} exits)");
            SingleInstance.SignalShow();
            Shutdown();
            return;
        }
        Log.Info($"Starting Pounce (process {Environment.ProcessId})");

        var configuration = Configuration.FromEnvironment();
        FirstRun.EnsureSettings(configuration.Home);
        StartBundledEngine(configuration);

        var shell = new ShellActions();
        engine = configuration.CreateEngineClient();
        var model = new SearchModel(engine, new WindowsAppSource());
        LauncherWindow window = panel = new LauncherWindow(model, shell, new ShellIconProvider());

        hotKey = new GlobalHotKey(configuration.HotKey);
        hotKey.Pressed += (_, _) => window.Toggle();
        tray = new TrayIcon(new TrayMenu(
            supervisor, hotKey.Registered, new LoginItem(),
            // Deferred until the tray menu has closed and handed back the focus, so the panel is shown after that.
            Search: () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(window.ShowPanel)),
            EditSettings: () => shell.OpenInEditor(configuration.Home.ConfigFile),
            Quit: Shutdown));

        instance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(new Action(window.ShowPanel));
        instance.QuitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown));
        // The first listing, ready before the first show; shows list again at most every 30 seconds.
        window.ReloadApps();
    }

    /// <summary>Asks the running Launcher to quit and waits for it: exit code 0 when none runs any more, 1 otherwise.</summary>
    private static int Quit()
    {
        Log.Info($"--quit: asking the running Pounce to quit (process {Environment.ProcessId})");
        try
        {
            bool done = SingleInstance.SignalQuit(TimeSpan.FromSeconds(20));
            Log.Info(done ? "--quit: no Pounce is running any more" : "--quit: the running Pounce did not quit within 20 seconds");
            return done ? 0 : 1;
        }
        catch (Exception e)
        {
            Log.Warn("--quit failed", e);
            return 1;
        }
    }

    /// <summary>Undoes whatever the startup got to, tells the user in one message box, and exits with code 1.</summary>
    private void FailStartup(Exception e)
    {
        bool first = Interlocked.Exchange(ref failing, 1) == 0;
        Log.Warn("Pounce couldn't start", e);
        CleanUp();
        if (first)
        {
            Tell(FailureNotice.CouldNotStart(e.Message, Log.Location));
        }
        Shutdown(1);
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        CleanUp();
        base.OnExit(e);
    }

    /// <summary>
    /// Stops the Engine this Launcher started and releases the tray icon, the hotkey and the single-instance mutex,
    /// whichever exist. Safe to call more than once and after a partial startup; a failing step does not stop the rest.
    /// </summary>
    private void CleanUp()
    {
        TrayIcon? oldTray = Interlocked.Exchange(ref tray, null);
        Quietly("Removing the tray icon", () => oldTray?.Dispose());
        GlobalHotKey? oldHotKey = Interlocked.Exchange(ref hotKey, null);
        Quietly("Releasing the hotkey", () => oldHotKey?.Dispose());
        EngineSupervisor? oldSupervisor = Interlocked.Exchange(ref supervisor, null);
        Quietly("Stopping the Engine", () => oldSupervisor?.Stop());
        HttpEngineClient? oldEngine = Interlocked.Exchange(ref engine, null);
        Quietly("Closing the Engine client", () => oldEngine?.Dispose());
        SingleInstance? oldInstance = Interlocked.Exchange(ref instance, null);
        Quietly("Releasing the single-instance mutex", () => oldInstance?.Dispose());
    }

    private static void Quietly(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Warn($"{what} failed", e);
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> in an error message box, on top of other windows, and waits until it is closed.
    /// Without an interactive session, or with POUNCE_NO_DIALOGS set (unattended runs: nobody would close it), the
    /// text is only logged.
    /// </summary>
    private static void Tell(string text)
    {
        if (!FailureNotice.MayShowDialog(Environment.UserInteractive, Environment.GetEnvironmentVariable(FailureNotice.NoDialogsVariable)))
        {
            Log.Info($"No dialog (non-interactive session or {FailureNotice.NoDialogsVariable} set): {text.ReplaceLineEndings(" ")}");
            return;
        }
        try
        {
            if (!WindowNative.ShowError(FailureNotice.Title, text))
            {
                Log.Warn("The error message box could not be shown");
            }
        }
        catch (Exception e)
        {
            Log.Warn("The error message box could not be shown", e);
        }
    }

    /// <summary>The log goes next to the Engine's, in the Pounce home; it must not fail, whatever the environment says.</summary>
    private static void StartLog()
    {
        string folder;
        try
        {
            folder = Path.GetDirectoryName(PounceHome.Resolve().EngineLog)!;
        }
        catch (Exception)
        {
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pounce", "logs");
        }
        Log.Start(Path.Combine(folder, "launcher.log"));
    }

    /// <summary>Next to a bundled Engine the Launcher owns it; run from a build folder, the Engine is started by hand.</summary>
    private void StartBundledEngine(Configuration configuration)
    {
        var layout = EngineLayout.Bundled(AppContext.BaseDirectory, configuration.Home.Folder);
        if (!File.Exists(layout.Jar))
        {
            return;
        }
        supervisor = new EngineSupervisor(layout, new EngineProcessStarter(configuration.Home.EngineLog),
            configuration.CreateEngineProbe());
        Observe(supervisor.StartAsync(), "Starting the Engine");
    }

    /// <summary>Logs the failure of a task nobody awaits. Async void on purpose: it catches everything itself.</summary>
    private static async void Observe(Task task, string what)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Log.Warn($"{what} failed", e);
        }
    }

    /// <summary>
    /// An exception on the UI thread that nothing caught. Once startup has completed it is logged and the app keeps
    /// running, since a tray app that vanishes over one failed click or bad result is worse than one that logs it.
    /// Exceptions coming back at once, over and over (a broken layout or render pass would), are not survivable that
    /// way: after 10 within 10 seconds, the next one ends the process, after one message box saying so. Before
    /// startup has completed nothing is survivable. Exceptions that come while the process is on its way out (its
    /// message box open) are only logged.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (Volatile.Read(ref failing) != 0)
        {
            Log.Warn("Unhandled exception on the UI thread while closing", e.Exception);
            e.Handled = true;
            return;
        }
        long now = Environment.TickCount64;
        if (now - crashBurstStart > 10_000)
        {
            crashBurstStart = now;
            crashBurstCount = 0;
        }
        crashBurstCount++;
        e.Handled = started && crashBurstCount <= 10;
        if (e.Handled)
        {
            Log.Warn("Unhandled exception on the UI thread; carrying on", e.Exception);
            return;
        }
        Die(started ? "Unhandled exceptions keep coming; quitting" : "Unhandled exception during startup; quitting", e.Exception);
    }

    /// <summary>
    /// Ends the process after a failure it cannot survive: logged, cleaned up (the Engine stopped, the tray icon
    /// removed), one message box, then exit code 1. Exits directly rather than through Shutdown, whose callback would
    /// wait behind a layout or render pass that keeps failing.
    /// </summary>
    private void Die(string why, Exception exception)
    {
        Interlocked.Exchange(ref failing, 1);
        // Handled from here on: the process ends below, after the message box, not by WPF rethrowing.
        Log.Warn(why, exception);
        CleanUp();
        Tell(FailureNotice.MustClose(exception.Message, Log.Location));
        Environment.Exit(1);
    }

    /// <summary>
    /// An exception on another thread: the runtime ends the process after this. The log says why, and, unless the
    /// app is already on its way out with its own message box, the user is told in one too.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Warn($"Unhandled exception (terminating: {e.IsTerminating})", exception);
        if (e.IsTerminating && Interlocked.Exchange(ref failing, 1) == 0)
        {
            Tell(FailureNotice.MustClose(exception?.Message, Log.Location));
        }
    }

    /// <summary>A failed task that nobody awaited: logged and marked observed.</summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Warn("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
