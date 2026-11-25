namespace Pounce.Core;

/// <summary>Where the bundled Engine's pieces are, and where it keeps its data.</summary>
/// <remarks>
/// <para>No path that depends on the user travels on the Engine's command line. <c>java.exe</c> reads its command line
/// in the system's ANSI code page (JDK-4488646), so a character that code page lacks, such as the Cyrillic, Greek,
/// Chinese or Azerbaijani letters of many user names, arrives as <c>?</c>, and the Engine would never find its home.
/// Environment variables reach Java as Unicode, so the home and the settings file go there (<see cref="Environment"/>),
/// and the Engine runs in <see cref="AppFolder"/> with every path into the bundle relative to it
/// (<see cref="Arguments"/>). Only the install folder itself must be representable, which the Java runtime inside it
/// needs anyway.</para>
/// <para>The JVM writes its crash report (<c>hs_err_pid*.log</c>) into its working directory, here the install folder,
/// or into the temp folder when it may not write there; <see cref="EngineCrashFiles"/> moves it into the logs.</para>
/// </remarks>
/// <param name="AppFolder">
/// The folder holding <c>Pounce.exe</c> and the bundle (<c>AppContext.BaseDirectory</c>), which is the Engine's working
/// directory.
/// </param>
/// <param name="Home">The data folder (see <see cref="PounceHome"/>), a full path.</param>
/// <param name="BundlesOnnxRuntime">
/// Whether the bundle holds ONNX Runtime's DLLs (<c>onnxruntime.dll</c>, <c>onnxruntime4j_jni.dll</c>,
/// <c>onnxruntime_providers_shared.dll</c>) in <see cref="OnnxRuntimeNativesPath"/>; if not, ONNX Runtime unpacks them
/// from its jar into <c>%TEMP%</c> as it does by default. Loading them from the install folder leaves no DLLs behind in
/// <c>%TEMP%</c> (a loaded DLL can't be deleted, so each start would leave about 20 MB there) and works where policy
/// forbids loading DLLs from it.
/// </param>
public sealed record EngineLayout(string AppFolder, string Home, bool BundlesOnnxRuntime = false)
{
    /// <summary>The Engine's jar, relative to <see cref="AppFolder"/>.</summary>
    public const string JarPath = @"engine\pounce-engine.jar";

    /// <summary>The embedding model's folder, relative to <see cref="AppFolder"/>.</summary>
    public const string ModelPath = @"models\bge-small-en-v1.5";

    /// <summary>The folder of ONNX Runtime's DLLs, relative to <see cref="AppFolder"/>.</summary>
    public const string OnnxRuntimeNativesPath = @"native\onnxruntime";

    /// <summary>
    /// Next to <c>Pounce.exe</c>: <c>runtime\bin\java.exe</c>, <c>engine\pounce-engine.jar</c>,
    /// <c>models\bge-small-en-v1.5</c> and, when the build put them there, ONNX Runtime's DLLs in
    /// <c>native\onnxruntime</c>, under <paramref name="appFolder"/>; data in <paramref name="home"/>.
    /// </summary>
    /// <param name="appFolder">The folder holding <c>Pounce.exe</c> (<c>AppContext.BaseDirectory</c>).</param>
    /// <param name="home">The data folder, usually <see cref="PounceHome.Folder"/>.</param>
    public static EngineLayout Bundled(string appFolder, string home) => Bundled(appFolder, home, Directory.Exists);

    /// <summary><see cref="Bundled(string, string)"/>, asking <paramref name="folderExists"/> instead of the disk.</summary>
    internal static EngineLayout Bundled(string appFolder, string home, Func<string, bool> folderExists) =>
        new(appFolder, home, folderExists(WindowsPath.Join(appFolder, OnnxRuntimeNativesPath)));

    /// <summary>The Java runtime's <c>java.exe</c>, as a full path: the program to start.</summary>
    public string Java => WindowsPath.Join(AppFolder, "runtime", "bin", "java.exe");

    /// <summary>The Engine's jar, as a full path (to check that the bundle has it).</summary>
    public string Jar => WindowsPath.Join(AppFolder, JarPath);

    /// <summary>The folder the Engine runs in, <see cref="AppFolder"/>: the paths in <see cref="Arguments"/> are relative to it.</summary>
    public string WorkingDirectory => AppFolder;

    /// <summary>
    /// The JVM and Engine arguments, one string each (pass them through <c>ProcessStartInfo.ArgumentList</c>; nothing is
    /// quoted). Small heap, low idle footprint and Lucene's SIMD path (see docs/research). The Engine exits when this
    /// process does, even if it is killed. Every path is relative to <see cref="WorkingDirectory"/>; none names the user.
    /// </summary>
    public IReadOnlyList<string> Arguments => ArgumentsFor(System.Environment.ProcessId);

    /// <summary>
    /// The variables that carry the paths that depend on the user, as Unicode, overriding any the Launcher was started
    /// with: <c>POUNCE_HOME</c>, the data folder (Spring binds it to <c>pounce.home</c>), and
    /// <c>SPRING_CONFIG_ADDITIONALLOCATION</c> (Spring Boot's name for <c>spring.config.additional-location</c>): the
    /// settings the user can edit, <c>pounce.yml</c> next to the Index.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["POUNCE_HOME"] = Home,
        // Spring reads this as a URL, so the whole path takes forward slashes: optional:file:C:/Users/me/...
        ["SPRING_CONFIG_ADDITIONALLOCATION"] = "optional:file:" + new PounceHome(Home).ConfigFile.Replace('\\', '/'),
    };

    /// <summary>The arguments, with the Engine watching the process <paramref name="parentProcessId"/>.</summary>
    internal IReadOnlyList<string> ArgumentsFor(int parentProcessId) =>
    [
        "-Xmx256m", "-XX:+UseG1GC", "-XX:+UseCompactObjectHeaders", "-XX:G1PeriodicGCInterval=60000",
        "--add-modules", "jdk.incubator.vector", "--enable-native-access=ALL-UNNAMED",
        $"-Dpounce.parent-pid={parentProcessId}",
        // ONNX Runtime makes this absolute, against the working directory, and loads its DLLs from there instead of
        // unpacking them into %TEMP% (and fails to start if onnxruntime.dll or onnxruntime4j_jni.dll is missing there).
        .. (BundlesOnnxRuntime ? [$"-Donnxruntime.native.path={OnnxRuntimeNativesPath}"] : (string[])[]),
        "-jar", JarPath,
        $"--pounce.model-dir={ModelPath}",
    ];
}

/// <summary>
/// The JVM's crash files for one Engine process: the report (<c>hs_err_pid&lt;pid&gt;.log</c>), the minidump Windows
/// gets with it (<c>hs_err_pid&lt;pid&gt;.mdmp</c>) and the JIT compiler's replay file (<c>replay_pid&lt;pid&gt;.log</c>).
/// The JVM writes them into its working directory, the install folder, or into the temp folder when it may not write
/// there; when it can write neither, it prints the report to its output, which goes to the Engine log anyway.
/// </summary>
public static class EngineCrashFiles
{
    /// <summary>
    /// Moves the crash files of process <paramref name="processId"/> from <paramref name="folders"/> into
    /// <paramref name="logFolder"/>, replacing older ones of the same name, and returns their new paths. A file that
    /// can't be moved stays where it is. Never throws for a missing file or folder.
    /// </summary>
    /// <param name="processId">The Engine's process id.</param>
    /// <param name="folders">Where the JVM may have written them: its working directory and the temp folder.</param>
    /// <param name="logFolder">The logs folder in the home.</param>
    public static IReadOnlyList<string> MoveToLogs(int processId, IEnumerable<string> folders, string logFolder)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(logFolder);
        var moved = new List<string>();
        foreach (string folder in folders)
        {
            foreach (string name in NamesFor(processId))
            {
                string from = Path.Combine(folder, name);
                string to = Path.Combine(logFolder, name);
                try
                {
                    if (!File.Exists(from))
                    {
                        continue;
                    }
                    Directory.CreateDirectory(logFolder);
                    File.Move(from, to, overwrite: true);
                    moved.Add(to);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Still open, or not ours to move: it stays where the JVM said it is (in the Engine log).
                }
            }
        }
        return moved;
    }

    /// <summary>The names the JVM gives the crash files of process <paramref name="processId"/>.</summary>
    public static IReadOnlyList<string> NamesFor(int processId) =>
        [$"hs_err_pid{processId}.log", $"hs_err_pid{processId}.mdmp", $"replay_pid{processId}.log"];
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
    /// Starts <paramref name="executable"/> in <paramref name="workingDirectory"/> with <paramref name="arguments"/> (one
    /// string each, for <c>ProcessStartInfo.ArgumentList</c>) and <paramref name="environment"/> added to this process's
    /// own. Throws if it can't start.
    /// </summary>
    /// <param name="executable">The program to run, a full path.</param>
    /// <param name="workingDirectory">The folder it runs in; relative paths in its arguments are relative to it.</param>
    /// <param name="arguments">Its arguments, unquoted.</param>
    /// <param name="environment">Variables to add or override.</param>
    /// <param name="onExit">Called once with the exit code when the process ends, on any thread.</param>
    IEngineProcess Start(string executable, string workingDirectory, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, Action<int> onExit);
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
            _process = _starter.Start(_layout.Java, _layout.WorkingDirectory, _layout.Arguments, _layout.Environment,
                code => OnExit(launch, code));
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
