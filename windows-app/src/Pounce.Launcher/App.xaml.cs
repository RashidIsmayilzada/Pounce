using System.IO;
using System.Windows;
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

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
            Search: panel.ShowPanel,
            EditSettings: () => shell.OpenInEditor(configuration.Home.ConfigFile),
            Quit: Shutdown));

        instance.ShowRequested += (_, _) => Dispatcher.BeginInvoke(new Action(panel.ShowPanel));
        instance.QuitRequested += (_, _) => Dispatcher.BeginInvoke(new Action(Shutdown));
        _ = model.ReloadAppsAsync();
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
        _ = supervisor.StartAsync();
    }
}
