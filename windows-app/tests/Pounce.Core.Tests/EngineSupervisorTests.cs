using static Pounce.Core.Tests.Waits;

namespace Pounce.Core.Tests;

/// <summary>The Launcher keeps the bundled Engine running: start it, reuse one already running, restart once, stop on quit.</summary>
public class EngineSupervisorTests
{
    private readonly EngineLayout _layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", @"C:\Users\me\AppData\Local\Pounce");
    private readonly FakeStarter _starter = new();

    [Fact]
    public void Starts_the_bundled_engine_when_none_is_running() => SingleThreadContext.Run(async _ =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false));

        await supervisor.StartAsync();

        Assert.Equal(EngineState.Running, supervisor.State);
        FakeStarter.Launch launch = Assert.Single(_starter.Started);
        Assert.Equal(@"C:\Program Files\Pounce\runtime\bin\java.exe", launch.Executable);
        Assert.Equal(
            [
                "-jar", @"C:\Program Files\Pounce\engine\pounce-engine.jar",
                @"--pounce.home=C:\Users\me\AppData\Local\Pounce",
                @"--pounce.model-dir=C:\Program Files\Pounce\models\bge-small-en-v1.5",
            ],
            launch.Arguments.TakeLast(4));
        Assert.Empty(launch.Environment);
    });

    [Fact]
    public void Reuses_an_engine_that_is_already_running() => SingleThreadContext.Run(async _ =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: true));

        await supervisor.StartAsync();

        Assert.Equal(EngineState.Reusing, supervisor.State);
        Assert.Empty(_starter.Started);
    });

    [Fact]
    public void Restarts_the_engine_once_after_a_crash_then_reports_it() => SingleThreadContext.Run(async _ =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false));
        await supervisor.StartAsync();

        _starter.Processes[0].Exit(1);
        Assert.Equal(2, _starter.Started.Count);
        Assert.Equal(EngineState.Running, supervisor.State);

        _starter.Processes[1].Exit(1);
        Assert.Equal(2, _starter.Started.Count);
        Assert.Equal(EngineState.Failed("Pounce's search engine stopped unexpectedly (exit code 1)."), supervisor.State);
    });

    [Fact]
    public void Stopping_terminates_the_engine_without_restarting_it() => SingleThreadContext.Run(async _ =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false));
        await supervisor.StartAsync();

        supervisor.Stop();
        _starter.Processes[0].Exit(143);

        Assert.True(_starter.Processes[0].Terminated);
        Assert.Single(_starter.Started);
        Assert.Equal(EngineState.Stopped, supervisor.State);
    });

    [Fact]
    public void An_engine_that_cannot_start_is_reported() => SingleThreadContext.Run(async _ =>
    {
        _starter.Failure = new FileNotFoundException("java.exe is missing");
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false));

        await supervisor.StartAsync();

        Assert.Equal(EngineStatus.Failed, supervisor.State.Status);
        Assert.Equal("Pounce couldn't start its search engine: java.exe is missing", supervisor.State.Problem);
        Assert.Equal(supervisor.State.Problem, supervisor.State.Description);
    });

    [Fact]
    public void Stopping_while_the_probe_runs_starts_nothing() => SingleThreadContext.Run(async _ =>
    {
        var probe = new FakeProbe(alive: false, answered: new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        var supervisor = new EngineSupervisor(_layout, _starter, probe);

        Task starting = supervisor.StartAsync();
        supervisor.Stop();
        probe.Answered!.SetResult(false);
        await starting;

        Assert.Empty(_starter.Started);
        Assert.Equal(EngineState.Stopped, supervisor.State);
    });

    [Fact]
    public void State_changes_are_announced_on_the_ui_thread_even_when_the_exit_is_reported_elsewhere() => SingleThreadContext.Run(async ui =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false), maxRestarts: 0);
        var states = new List<EngineState>();
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.StateChanged += (_, _) =>
        {
            Assert.True(ui.IsCurrent, "StateChanged raised off the UI thread");
            states.Add(supervisor.State);
            if (supervisor.State.Status == EngineStatus.Failed)
            {
                failed.TrySetResult();
            }
        };
        await supervisor.StartAsync();

        // A real process reports its exit on a thread-pool thread.
        await Task.Run(() => _starter.Processes[0].Exit(3));
        await failed.Task.WaitAsync(Patience);

        Assert.Equal([EngineState.Running, EngineState.Failed("Pounce's search engine stopped unexpectedly (exit code 3).")], states);
    });

    [Fact]
    public void The_exit_of_a_process_already_replaced_is_ignored() => SingleThreadContext.Run(async _ =>
    {
        var supervisor = new EngineSupervisor(_layout, _starter, new FakeProbe(alive: false));
        await supervisor.StartAsync();
        supervisor.Stop();
        await supervisor.StartAsync();

        _starter.Processes[0].Exit(143);

        Assert.Equal(2, _starter.Started.Count);
        Assert.Equal(EngineState.Running, supervisor.State);
    });

    [Fact]
    public void The_menu_line_says_what_the_engine_is_doing()
    {
        Assert.Equal("Search engine running", EngineState.Running.Description);
        Assert.Equal("Search engine running", EngineState.Reusing.Description);
        Assert.Equal("Search engine stopped", EngineState.Stopped.Description);
        Assert.Equal("It broke.", EngineState.Failed("It broke.").Description);
    }
}

public class EngineLayoutTests
{
    [Fact]
    public void The_bundled_engine_sits_next_to_pounce_exe()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce\", @"C:\Users\me\AppData\Local\Pounce");

        Assert.Equal(
            new EngineLayout(
                @"C:\Program Files\Pounce\runtime\bin\java.exe",
                @"C:\Program Files\Pounce\engine\pounce-engine.jar",
                @"C:\Program Files\Pounce\models\bge-small-en-v1.5",
                @"C:\Users\me\AppData\Local\Pounce"),
            layout);
    }

    [Fact]
    public void The_jvm_gets_the_same_arguments_as_on_macos_with_windows_paths()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", @"C:\Users\me\AppData\Local\Pounce");

        Assert.Equal(
            [
                "-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
                "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
                "-Dspring.config.additional-location=optional:file:C:/Users/me/AppData/Local/Pounce/pounce.yml",
                "-Dpounce.parent-pid=4242",
                "-jar", @"C:\Program Files\Pounce\engine\pounce-engine.jar",
                @"--pounce.home=C:\Users\me\AppData\Local\Pounce",
                @"--pounce.model-dir=C:\Program Files\Pounce\models\bge-small-en-v1.5",
            ],
            layout.ArgumentsFor(4242));
    }

    [Fact]
    public void The_engine_watches_this_process()
    {
        var layout = EngineLayout.Bundled(@"C:\Pounce", @"C:\Data");

        Assert.Contains($"-Dpounce.parent-pid={Environment.ProcessId}", layout.Arguments);
        Assert.Empty(layout.Environment);
    }
}

public class PounceHomeTests
{
    private static Func<string, string?> Variables(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Fact]
    public void Defaults_to_pounce_in_local_app_data()
    {
        PounceHome home = PounceHome.Resolve(Variables(), () => @"C:\Users\me\AppData\Local");

        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce", home.Folder);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\bootstrap-secret", home.SecretFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\pounce.yml", home.ConfigFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\logs\engine.log", home.EngineLog);
    }

    [Fact]
    public void Pounce_home_wins_when_set()
    {
        PounceHome home = PounceHome.Resolve(Variables(("POUNCE_HOME", @"D:\smoke\home")), () => throw new InvalidOperationException("not asked"));

        Assert.Equal(@"D:\smoke\home", home.Folder);
        Assert.Equal(@"D:\smoke\home\bootstrap-secret", home.SecretFile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pounce_home_is_ignored(string blank)
    {
        PounceHome home = PounceHome.Resolve(Variables(("POUNCE_HOME", blank)), () => @"C:\Local");

        Assert.Equal(@"C:\Local\Pounce", home.Folder);
    }
}

public class ConfigurationTests
{
    private static Configuration From(params (string Name, string Value)[] variables) => Configuration.FromEnvironment(
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault(),
        () => @"C:\Users\me\AppData\Local");

    [Fact]
    public void Defaults_without_any_variables()
    {
        Configuration configuration = From();

        Assert.Equal(new Uri("http://127.0.0.1:7431"), configuration.EngineUrl);
        Assert.Equal(new PounceHome(@"C:\Users\me\AppData\Local\Pounce"), configuration.Home);
        Assert.Equal(HotKeySpec.Default, configuration.HotKey);
    }

    [Fact]
    public void The_variables_override_the_defaults()
    {
        Configuration configuration = From(("POUNCE_URL", "http://127.0.0.1:9000"), ("POUNCE_HOME", @"D:\home"), ("POUNCE_HOTKEY", "ctrl-alt-space"));

        Assert.Equal(new Uri("http://127.0.0.1:9000"), configuration.EngineUrl);
        Assert.Equal(@"D:\home", configuration.Home.Folder);
        Assert.Equal("control-alt-space", configuration.HotKey.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://127.0.0.1:7431")]
    [InlineData("/relative")]
    public void An_unusable_url_falls_back_to_the_default(string url)
    {
        Assert.Equal(Configuration.DefaultEngineUrl, From(("POUNCE_URL", url)).EngineUrl);
    }

    [Fact]
    public void The_client_reads_the_secret_from_home()
    {
        using HttpEngineClient client = From(("POUNCE_URL", "http://127.0.0.1:9000")).CreateEngineClient();

        Assert.Equal(new Uri("http://127.0.0.1:9000"), client.BaseUrl);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\bootstrap-secret", client.SecretFile);
    }
}

public class HttpEngineProbeTests
{
    private static CancellationToken TestCancelled => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_engine_that_answers_401_is_alive()
    {
        await using var engine = StandInEngine.Start();
        using var probe = new HttpEngineProbe(engine.Url);

        Assert.True(await probe.IsAliveAsync(TestCancelled));
    }

    [Fact]
    public async Task Nothing_listening_is_not_alive()
    {
        using var probe = new HttpEngineProbe(new Uri($"http://127.0.0.1:{StandInEngine.ClosedPort()}"));

        Assert.False(await probe.IsAliveAsync(TestCancelled));
    }

    [Fact]
    public async Task A_listener_that_never_answers_is_not_alive_after_the_timeout()
    {
        await using var engine = StandInEngine.Start();
        engine.NeverAnswer = true;
        using var probe = new HttpEngineProbe(engine.Url, TimeSpan.FromMilliseconds(100));

        Assert.False(await probe.IsAliveAsync(TestCancelled));
    }
}

// fakes

internal sealed class FakeProbe(bool alive, TaskCompletionSource<bool>? answered = null) : IEngineProbe
{
    public TaskCompletionSource<bool>? Answered => answered;

    public Task<bool> IsAliveAsync(CancellationToken cancellationToken = default) => answered?.Task ?? Task.FromResult(alive);
}

internal sealed class FakeStarter : IProcessStarter
{
    public sealed record Launch(string Executable, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

    public List<Launch> Started { get; } = [];
    public List<FakeProcess> Processes { get; } = [];
    public Exception? Failure { get; set; }

    public IEngineProcess Start(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, Action<int> onExit)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        Started.Add(new Launch(executable, arguments, environment));
        var process = new FakeProcess(onExit);
        Processes.Add(process);
        return process;
    }
}

internal sealed class FakeProcess(Action<int> onExit) : IEngineProcess
{
    public bool Terminated { get; private set; }

    public void Terminate() => Terminated = true;

    public void Exit(int code) => onExit(code);
}
