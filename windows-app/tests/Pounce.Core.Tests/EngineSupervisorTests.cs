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
        Assert.Equal(@"C:\Program Files\Pounce", launch.WorkingDirectory);
        Assert.Equal(["-jar", @"engine\pounce-engine.jar", @"--pounce.model-dir=models\bge-small-en-v1.5"], launch.Arguments.TakeLast(3));
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce", launch.Environment["POUNCE_HOME"]);
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
    public void The_bundled_engine_sits_next_to_pounce_exe_and_runs_there()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce\", @"C:\Users\me\AppData\Local\Pounce", _ => false);

        Assert.Equal(new EngineLayout(@"C:\Program Files\Pounce\", @"C:\Users\me\AppData\Local\Pounce"), layout);
        Assert.Equal(@"C:\Program Files\Pounce\runtime\bin\java.exe", layout.Java);
        Assert.Equal(@"C:\Program Files\Pounce\engine\pounce-engine.jar", layout.Jar);
        Assert.Equal(@"C:\Program Files\Pounce\", layout.WorkingDirectory);
    }

    [Fact]
    public void The_jvm_gets_the_same_flags_as_on_macos_and_only_paths_relative_to_the_install_folder()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", @"C:\Users\me\AppData\Local\Pounce", _ => false);

        Assert.Equal(
            [
                "-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
                "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
                "-Dpounce.parent-pid=4242",
                "-jar", @"engine\pounce-engine.jar",
                @"--pounce.model-dir=models\bge-small-en-v1.5",
            ],
            layout.ArgumentsFor(4242));
    }

    [Theory]
    [InlineData(@"C:\Users\Рашид Əli\AppData\Local\Pounce")]
    [InlineData(@"C:\Users\Łukasz\AppData\Local\Pounce")]
    [InlineData(@"C:\Users\山田\AppData\Local\Pounce")]
    public void Paths_that_name_the_user_travel_in_the_environment_never_on_the_command_line(string home)
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", home, _ => true);

        // java.exe reads its command line in the ANSI code page, which may lack these letters; its environment is Unicode.
        Assert.All(layout.ArgumentsFor(4242), argument => Assert.True(argument.All(char.IsAscii), argument));
        Assert.DoesNotContain(layout.ArgumentsFor(4242), argument => argument.Contains("Users", StringComparison.Ordinal));
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["POUNCE_HOME"] = home,
                // Spring reads it as a URL: forward slashes throughout.
                ["SPRING_CONFIG_ADDITIONALLOCATION"] = "optional:file:" + home.Replace('\\', '/') + "/pounce.yml",
            },
            layout.Environment);
    }

    [Fact]
    public void The_settings_file_is_a_url_with_forward_slashes()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", @"C:\Users\me\AppData\Local\Pounce", _ => false);

        Assert.Equal("optional:file:C:/Users/me/AppData/Local/Pounce/pounce.yml", layout.Environment["SPRING_CONFIG_ADDITIONALLOCATION"]);
    }

    [Fact]
    public void Onnx_runtime_loads_its_dlls_from_the_bundle_when_the_bundle_has_them()
    {
        var asked = new List<string>();
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce", @"C:\Users\me\AppData\Local\Pounce",
            folder => { asked.Add(folder); return true; });

        Assert.Equal([@"C:\Program Files\Pounce\native\onnxruntime"], asked);
        Assert.True(layout.BundlesOnnxRuntime);
        Assert.Equal(
            [
                "-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
                "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
                "-Dpounce.parent-pid=4242",
                // Relative: ONNX Runtime makes it absolute against the working directory before it loads the DLLs.
                @"-Donnxruntime.native.path=native\onnxruntime",
                "-jar", @"engine\pounce-engine.jar",
                @"--pounce.model-dir=models\bge-small-en-v1.5",
            ],
            layout.ArgumentsFor(4242));
    }

    [Fact]
    public void Without_the_bundled_dlls_onnx_runtime_unpacks_its_own()
    {
        var layout = EngineLayout.Bundled(@"C:\Program Files\Pounce\", @"C:\Users\me\AppData\Local\Pounce", _ => false);

        Assert.False(layout.BundlesOnnxRuntime);
        Assert.DoesNotContain(layout.ArgumentsFor(4242), argument => argument.Contains("onnxruntime", StringComparison.Ordinal));
    }

    [Fact]
    public void The_engine_watches_this_process()
    {
        var layout = EngineLayout.Bundled(@"C:\Pounce", @"C:\Data");

        Assert.Contains($"-Dpounce.parent-pid={Environment.ProcessId}", layout.Arguments);
    }
}

/// <summary>A JVM crash leaves its report in the install folder or the temp folder; the Launcher moves it to the logs.</summary>
public sealed class EngineCrashFilesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Moves_the_crash_files_of_that_process_into_the_logs()
    {
        string app = _temp.Folder("app");
        string tmp = _temp.Folder("tmp");
        string logs = Path.Combine(_temp.Root, "home", "logs");
        _temp.File("app/hs_err_pid4242.log", "crash");
        _temp.File("app/replay_pid4242.log", "replay");
        _temp.File("tmp/hs_err_pid4242.mdmp", "dump");
        _temp.File("app/hs_err_pid4243.log", "another process");
        _temp.File("app/pounce-engine.log", "not a crash file");

        IReadOnlyList<string> moved = EngineCrashFiles.MoveToLogs(4242, [app, tmp], logs);

        Assert.Equal(
            [Path.Combine(logs, "hs_err_pid4242.log"), Path.Combine(logs, "replay_pid4242.log"), Path.Combine(logs, "hs_err_pid4242.mdmp")],
            moved);
        Assert.Equal("crash", File.ReadAllText(Path.Combine(logs, "hs_err_pid4242.log")));
        Assert.Equal(["hs_err_pid4243.log", "pounce-engine.log"],
            Directory.GetFiles(app).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(tmp));
    }

    [Fact]
    public void A_report_of_an_earlier_process_with_the_same_id_is_replaced()
    {
        string app = _temp.Folder("app");
        string logs = _temp.Folder("logs");
        _temp.File("logs/hs_err_pid7.log", "old");
        _temp.File("app/hs_err_pid7.log", "new");

        EngineCrashFiles.MoveToLogs(7, [app], logs);

        Assert.Equal("new", File.ReadAllText(Path.Combine(logs, "hs_err_pid7.log")));
    }

    [Fact]
    public void No_crash_and_missing_folders_move_nothing()
    {
        string logs = Path.Combine(_temp.Root, "logs");

        Assert.Empty(EngineCrashFiles.MoveToLogs(7, [_temp.Folder("app"), Path.Combine(_temp.Root, "nowhere")], logs));
        Assert.False(Directory.Exists(logs));
    }
}

public class PounceHomeTests
{
    private static Func<string, string?> Variables(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    private static string NotAsked() => throw new InvalidOperationException("not asked");

    [Fact]
    public void Defaults_to_pounce_in_local_app_data()
    {
        PounceHome home = PounceHome.Resolve(Variables(("LOCALAPPDATA", @"C:\Users\me\AppData\Local")), NotAsked, NotAsked);

        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce", home.Folder);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\bootstrap-secret", home.SecretFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\pounce.yml", home.ConfigFile);
        Assert.Equal(@"C:\Users\me\AppData\Local\Pounce\logs\engine.log", home.EngineLog);
    }

    [Fact]
    public void A_full_pounce_home_wins_as_it_is()
    {
        PounceHome home = PounceHome.Resolve(Variables(("POUNCE_HOME", @"D:\smoke\home"), ("LOCALAPPDATA", @"C:\Local")), NotAsked, NotAsked);

        Assert.Equal(@"D:\smoke\home", home.Folder);
        Assert.Equal(@"D:\smoke\home\bootstrap-secret", home.SecretFile);
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("smoke/home")]
    [InlineData("../smoke")]
    public void A_relative_pounce_home_is_made_full_against_the_current_directory_so_the_engine_gets_the_same(string relative)
    {
        // The real Path.GetFullPath of this system: on Windows the Launcher runs it with Windows rules.
        string current = Path.Combine(Path.GetTempPath(), "work", "dir");
        string chosen = relative.Replace('/', Path.DirectorySeparatorChar);

        PounceHome home = PounceHome.Resolve(Variables(("POUNCE_HOME", chosen)), NotAsked, () => current);

        Assert.Equal(Path.GetFullPath(Path.Combine(current, chosen)), home.Folder);
        Assert.True(Path.IsPathFullyQualified(home.Folder), home.Folder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pounce_home_is_ignored(string blank)
    {
        PounceHome home = PounceHome.Resolve(Variables(("POUNCE_HOME", blank), ("LOCALAPPDATA", @"C:\Local")), NotAsked, NotAsked);

        Assert.Equal(@"C:\Local\Pounce", home.Folder);
    }

    /// <summary>
    /// The Windows rows of the table every Pounce program checks itself against, so the Launcher, the Engine and the
    /// <c>pounce</c> command agree on the home. Format in the file's first line.
    /// </summary>
    [Theory]
    [MemberData(nameof(SharedWindowsCases))]
    public void Agrees_with_the_shared_cases(string userHome, string? pounceHome, string? localAppData, string? xdgDataHome, string expected)
    {
        var variables = new Dictionary<string, string?>
        {
            ["POUNCE_HOME"] = pounceHome,
            ["LOCALAPPDATA"] = localAppData,
            ["XDG_DATA_HOME"] = xdgDataHome,
        };

        PounceHome home = PounceHome.Resolve(name => variables.GetValueOrDefault(name), () => userHome, NotAsked);

        Assert.Equal(expected.Replace('\\', '/'), home.Folder.Replace('\\', '/'));
    }

    public static TheoryData<string, string?, string?, string?, string> SharedWindowsCases()
    {
        var cases = new TheoryData<string, string?, string?, string?, string>();
        foreach (string line in File.ReadAllLines(SharedCasesFile()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            string?[] fields = line.Split(',').Select(field => field switch { "" => null, "<blank>" => " ", _ => field }).ToArray();
            Assert.Equal(6, fields.Length);
            if (fields[0]!.StartsWith("Windows", StringComparison.Ordinal))
            {
                cases.Add(fields[1]!, fields[2], fields[3], fields[4], fields[5]!);
            }
        }
        Assert.NotEmpty(cases);
        return cases;
    }

    /// <summary>The table, found by walking up from the test's folder to the repository root.</summary>
    private static string SharedCasesFile()
    {
        string relative = Path.Combine("pounce-platform", "src", "test", "resources", "pounce-home-cases.csv");
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string candidate = Path.Combine(folder.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException($"{relative} not found above {AppContext.BaseDirectory}");
    }
}

public class ConfigurationTests
{
    private static Configuration From(params (string Name, string Value)[] variables) => Configuration.FromEnvironment(
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault(),
        () => @"C:\Users\me",
        () => @"C:\Program Files\Pounce");

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
    public sealed record Launch(string Executable, string WorkingDirectory, IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string> Environment);

    public List<Launch> Started { get; } = [];
    public List<FakeProcess> Processes { get; } = [];
    public Exception? Failure { get; set; }

    public IEngineProcess Start(string executable, string workingDirectory, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, Action<int> onExit)
    {
        if (Failure is not null)
        {
            throw Failure;
        }
        Started.Add(new Launch(executable, workingDirectory, arguments, environment));
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
