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
/// Honours POUNCE_URL, POUNCE_HOME and POUNCE_HOTKEY.
/// </summary>
public partial class App : Application
{
    private SingleInstance? instance;
    private EngineSupervisor? supervisor;
    private HttpEngineClient? engine;
    private GlobalHotKey? hotKey;
    private TrayIcon? tray;
    private LauncherWindow? panel;
    private long crashBurstStart;
    private int crashBurstCount;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Registered first; messages are dropped until Log.Start below.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        if (e.Args.Contains("--quit"))
        {
            Shutdown(SingleInstance.SignalQuit(TimeSpan.FromSeconds(20)) ? 0 : 1);
            return;
        }
        instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalShow();
            Shutdown();
            return;
        }

        var configuration = Configuration.FromEnvironment();
        Log.Start(Path.Combine(Path.GetDirectoryName(configuration.Home.EngineLog)!, "launcher.log"));
        FirstRun.EnsureSettings(configuration.Home);
        StartBundledEngine(configuration);

        var shell = new ShellActions();
        engine = configuration.CreateEngineClient();
        var model = new SearchModel(engine, new WindowsAppSource());
        panel = new LauncherWindow(model, shell, new ShellIconProvider());

        hotKey = new GlobalHotKey(configuration.HotKey);
        hotKey.Pressed += (_, _) => panel.Toggle();
        tray = new TrayIcon(new TrayMenu(
            supervisor, hotKey.Registered, new LoginItem(),
            // Deferred until the tray menu has closed and handed back the focus, so the panel is shown after that.
            Search: () => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(panel.ShowPanel)),
            EditSettings: () => shell.OpenInEditor(configuration.Home.ConfigFile),
            Quit: Shutdown));

        instance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(new Action(panel.ShowPanel));
        instance.QuitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown));
        // The first listing, ready before the first show; shows list again at most every 30 seconds.
        panel.ReloadApps();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        hotKey?.Dispose();
        supervisor?.Stop();
        engine?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
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
    /// An exception on the UI thread that nothing caught: logged, and the app keeps running, since a tray app that
    /// vanishes over one failed click or bad result is worse than one that logs it. Exceptions coming back at once,
    /// over and over (a broken layout or render pass would), are not survivable that way: after 10 within 10
    /// seconds, the next one ends the process as WPF would by default.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        long now = Environment.TickCount64;
        if (now - crashBurstStart > 10_000)
        {
            crashBurstStart = now;
            crashBurstCount = 0;
        }
        crashBurstCount++;
        e.Handled = crashBurstCount <= 10;
        Log.Warn(e.Handled ? "Unhandled exception on the UI thread; carrying on" : "Unhandled exceptions keep coming; quitting",
            e.Exception);
    }

    /// <summary>An exception on another thread: the runtime ends the process after this; the log says why.</summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Warn($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);

    /// <summary>A failed task that nobody awaited: logged and marked observed.</summary>
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Warn("Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
