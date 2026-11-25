namespace Pounce.Core;

/// <summary>Where the bundled Engine's pieces are, and where it keeps its data.</summary>
/// <param name="Java">The Java runtime's <c>java.exe</c>.</param>
/// <param name="Jar">The Engine's jar.</param>
/// <param name="ModelDir">The embedding model's folder.</param>
/// <param name="Home">The data folder (see <see cref="PounceHome"/>).</param>
public sealed record EngineLayout(string Java, string Jar, string ModelDir, string Home)
{
    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>();

    /// <summary>
    /// Next to <c>Pounce.exe</c>: <c>runtime\bin\java.exe</c>, <c>engine\pounce-engine.jar</c> and
    /// <c>models\bge-small-en-v1.5</c> under <paramref name="appFolder"/>; data in <paramref name="home"/>.
    /// </summary>
    /// <param name="appFolder">The folder holding <c>Pounce.exe</c> (<c>AppContext.BaseDirectory</c>).</param>
    /// <param name="home">The data folder, usually <see cref="PounceHome.Folder"/>.</param>
    public static EngineLayout Bundled(string appFolder, string home) => new(
        WindowsPath.Join(appFolder, "runtime", "bin", "java.exe"),
        WindowsPath.Join(appFolder, "engine", "pounce-engine.jar"),
        WindowsPath.Join(appFolder, "models", "bge-small-en-v1.5"),
        home);

    /// <summary>
    /// The JVM and Engine arguments, one string each (pass them through <c>ProcessStartInfo.ArgumentList</c>; nothing is
    /// quoted). Small heap, low idle footprint and Lucene's SIMD path (see docs/research); settings the user can edit
    /// live in <c>pounce.yml</c> next to the Index. The Engine exits when this process does, even if it is killed.
    /// </summary>
    public IReadOnlyList<string> Arguments => ArgumentsFor(System.Environment.ProcessId);

    /// <summary>Extra environment variables for the Engine: none on Windows (the Engine makes no network calls by itself).</summary>
    public IReadOnlyDictionary<string, string> Environment => NoVariables;

    /// <summary>The arguments, with the Engine watching the process <paramref name="parentProcessId"/>.</summary>
    internal IReadOnlyList<string> ArgumentsFor(int parentProcessId) =>
    [
        "-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
        "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
        // Spring reads this as a URL, so the whole path takes forward slashes: optional:file:C:/Users/me/...
        "-Dspring.config.additional-location=optional:file:" + new PounceHome(Home).ConfigFile.Replace('\\', '/'),
        $"-Dpounce.parent-pid={parentProcessId}",
        "-jar", Jar,
        $"--pounce.home={Home}",
        $"--pounce.model-dir={ModelDir}",
    ];
}

/// <summary>A running Engine process.</summary>
public interface IEngineProcess
{
    /// <summary>Stops the process (and anything it started). Called on the UI thread; must not block for long.</summary>
    void Terminate();
}

/// <summary>Starts the Engine's process. The real one is Windows-specific and lives in the shell.</summary>
public interface IProcessStarter
{
    /// <summary>
    /// Starts <paramref name="executable"/> with <paramref name="arguments"/> (one string each, for
    /// <c>ProcessStartInfo.ArgumentList</c>) and <paramref name="environment"/> added to this process's own.
    /// Throws if it can't start.
    /// </summary>
    /// <param name="executable">The program to run.</param>
    /// <param name="arguments">Its arguments, unquoted.</param>
    /// <param name="environment">Variables to add or override.</param>
    /// <param name="onExit">Called once with the exit code when the process ends, on any thread.</param>
    IEngineProcess Start(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment,
        Action<int> onExit);
}

/// <summary>Answers whether an Engine is already listening (for example one left from an earlier run).</summary>
public interface IEngineProbe
{
    /// <summary>True when an Engine answers. Never throws for an Engine that isn't there.</summary>
    Task<bool> IsAliveAsync(CancellationToken cancellationToken = default);
}

/// <summary>The kinds of <see cref="EngineState"/>.</summary>
public enum EngineStatus
{
    /// <summary>Not started yet, or stopped on request.</summary>
    Stopped,

    /// <summary>An Engine was already answering, so the Launcher uses that one.</summary>
    Reusing,

    /// <summary>The Launcher started the Engine and it is running.</summary>
    Running,

    /// <summary>The Engine could not start, or stopped unexpectedly too often; see <see cref="EngineState.Problem"/>.</summary>
    Failed,
}

/// <summary>What the supervisor knows about the Engine.</summary>
/// <param name="Status">The kind of state.</param>
/// <param name="Problem">For <see cref="EngineStatus.Failed"/>: what went wrong, in words for the user.</param>
public sealed record EngineState(EngineStatus Status, string? Problem = null)
{
    /// <summary>Not started, or stopped.</summary>
    public static EngineState Stopped { get; } = new(EngineStatus.Stopped);

    /// <summary>Using an Engine that was already running.</summary>
    public static EngineState Reusing { get; } = new(EngineStatus.Reusing);

    /// <summary>Running the bundled Engine.</summary>
    public static EngineState Running { get; } = new(EngineStatus.Running);

    /// <summary>Failed, with a message for the user.</summary>
    public static EngineState Failed(string problem) => new(EngineStatus.Failed, problem);

    /// <summary>
    /// A line for the tray menu: "Search engine running", "Search engine stopped", or the problem. (Without a
    /// supervisor, because the Engine was started by hand, the menu says "Search engine started by hand".)
    /// </summary>
    public string Description => Status switch
    {
        EngineStatus.Running or EngineStatus.Reusing => "Search engine running",
        EngineStatus.Failed => Problem ?? "Search engine failed",
        _ => "Search engine stopped",
    };
}

/// <summary>
/// Keeps the bundled Engine running while the Launcher runs: reuses one that is already answering, otherwise
/// starts it; restarts it once if it dies unexpectedly; stops it when asked.
/// </summary>
/// <remarks>
/// Threading: create and use it on the UI thread. <see cref="State"/> changes and <see cref="StateChanged"/> events
/// happen only on that thread: the probe is awaited on the captured <see cref="SynchronizationContext"/>, and a
/// process exit reported on another thread is posted back to it. Without a context (no UI thread) they happen on
/// whichever thread reports them.
/// </remarks>
public sealed class EngineSupervisor
{
    private readonly EngineLayout _layout;
    private readonly IProcessStarter _starter;
    private readonly IEngineProbe _probe;
    private readonly int _maxRestarts;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private int _restarts;
    private IEngineProcess? _process;
    private int _launch;
    private bool _stopping;

    /// <summary>A supervisor for the Engine in <paramref name="layout"/>.</summary>
    /// <param name="layout">Where the Engine is.</param>
    /// <param name="starter">Starts the Engine's process.</param>
    /// <param name="probe">Tells whether an Engine already answers.</param>
    /// <param name="maxRestarts">How many unexpected exits are followed by a restart.</param>
    public EngineSupervisor(EngineLayout layout, IProcessStarter starter, IEngineProbe probe, int maxRestarts = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(starter);
        ArgumentNullException.ThrowIfNull(probe);
        _layout = layout;
        _starter = starter;
        _probe = probe;
        _maxRestarts = maxRestarts;
    }

    /// <summary>Raised on the UI thread after <see cref="State"/> changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>The Engine's current state; starts as <see cref="EngineState.Stopped"/>.</summary>
    public EngineState State { get; private set; } = EngineState.Stopped;

    /// <summary>Reuses an Engine that already answers, otherwise starts the bundled one. Completes once decided.</summary>
    public async Task StartAsync()
    {
        _stopping = false;
        bool alive = await _probe.IsAliveAsync();
        if (_stopping)
        {
            return; // Stop() was called while the probe ran.
        }
        if (alive)
        {
            SetState(EngineState.Reusing);
            return;
        }
        Launch();
    }

    /// <summary>Stops the Engine this supervisor started (one it reused is left running) and doesn't restart it.</summary>
    public void Stop()
    {
        _stopping = true;
        _process?.Terminate();
        _process = null;
        SetState(EngineState.Stopped);
    }

    private void Launch()
    {
        int launch = ++_launch;
        try
        {
            _process = _starter.Start(_layout.Java, _layout.Arguments, _layout.Environment, code => OnExit(launch, code));
            SetState(EngineState.Running);
        }
        catch (Exception e)
        {
            SetState(EngineState.Failed($"Pounce couldn't start its search engine: {e.Message}"));
        }
    }

    private void OnExit(int launch, int code)
    {
        if (_context is null || _context == SynchronizationContext.Current)
        {
            Exited(launch, code);
        }
        else
        {
            _context.Post(_ => Exited(launch, code), null);
        }
    }

    private void Exited(int launch, int code)
    {
        if (launch != _launch)
        {
            return; // An earlier process, already replaced.
        }
        _process = null;
        if (_stopping)
        {
            return;
        }
        if (_restarts < _maxRestarts)
        {
            _restarts++;
            Launch();
        }
        else
        {
            SetState(EngineState.Failed($"Pounce's search engine stopped unexpectedly (exit code {code})."));
        }
    }

    private void SetState(EngineState state)
    {
        if (state == State)
        {
            return;
        }
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>An Engine is alive if its API answers at all (401 without a token counts). Asks without any proxy.</summary>
public sealed class HttpEngineProbe : IEngineProbe, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _url;

    /// <summary>A probe for the Engine at <paramref name="baseUrl"/>, giving up after <paramref name="timeout"/> (1 second by default).</summary>
    public HttpEngineProbe(Uri baseUrl, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        _url = HttpEngineClient.Endpoint(baseUrl, "search");
        _http = new HttpClient(HttpEngineClient.CreateHandler(), disposeHandler: true) { Timeout = timeout ?? TimeSpan.FromSeconds(1) };
    }

    /// <inheritdoc/>
    public async Task<bool> IsAliveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when ((e is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Releases the connections.</summary>
    public void Dispose() => _http.Dispose();
}
