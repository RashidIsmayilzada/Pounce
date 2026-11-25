using System.Diagnostics;
using System.IO;
using System.Text;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// Starts the Engine's <c>java.exe</c> without a console window, with its output (stdout and stderr) written to the
/// Engine log. The first start of a Launcher run begins a fresh log; a restart after a crash appends to it, so the
/// crash stays readable.
/// </summary>
public sealed class EngineProcessStarter : IProcessStarter
{
    private readonly string logFile;
    private bool started;

    /// <summary>The Engine's output goes to <paramref name="logFile"/>.</summary>
    public EngineProcessStarter(string logFile) => this.logFile = logFile;

    /// <inheritdoc/>
    public IEngineProcess Start(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment, Action<int> onExit)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(onExit);

        string folder = Path.GetDirectoryName(Path.GetFullPath(logFile))!;
        Directory.CreateDirectory(folder);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true, // CREATE_NO_WINDOW: java.exe is a console program; no window may flash
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // The log folder was just created, so it surely exists; it is not on a drive the user may eject.
            WorkingDirectory = folder,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument); // quoted by .NET as the C runtime expects
        }
        foreach ((string name, string value) in environment)
        {
            info.Environment[name] = value; // starts as a copy of this process's environment
        }

        Stream log = OpenLog();
        try
        {
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            var engine = new EngineProcess(process, log, onExit);
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"{executable} did not start");
            }
            started = true;
            engine.Started();
            Log.Info($"Engine started: pid {process.Id}, output in {logFile}");
            return engine;
        }
        catch (Exception e)
        {
            log.Dispose();
            Log.Warn($"Could not start {executable}", e);
            throw;
        }
    }

    /// <summary>
    /// The log, fresh for the first start and appended to (after a line saying so) for a restart. Readable by others
    /// while it is written. Without a log the Engine still runs: its output is then dropped.
    /// </summary>
    private Stream OpenLog()
    {
        try
        {
            var log = new FileStream(logFile, started ? FileMode.Append : FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            if (started)
            {
                log.Write(Encoding.UTF8.GetBytes(
                    $"{Environment.NewLine}--- {DateTime.Now:yyyy-MM-dd HH:mm:ss} Pounce restarts its search engine ---{Environment.NewLine}"));
                log.Flush();
            }
            return log;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not open {logFile}; the Engine's output is dropped", e);
            return Stream.Null;
        }
    }

    /// <summary>
    /// A running Engine: copies its output to the log and reports its exit once.
    /// </summary>
    /// <remarks>
    /// <para>Stopping. Windows has no SIGTERM for a windowless child, and <c>Kill</c> (TerminateProcess) would stop
    /// the Engine mid-write, before it commits its Index. The Engine was given this Launcher's process id
    /// (<c>-Dpounce.parent-pid</c>) and shuts itself down cleanly, through Spring, as soon as that process ends — be it
    /// a normal quit or a forced kill. So <see cref="Terminate"/> does not stop the Engine itself; it only arms a
    /// deadline: when the Launcher exits (the normal case, right after <c>EngineSupervisor.Stop</c> in
    /// <c>App.OnExit</c>), the deadline dies with it and the Engine closes on its own. If the Launcher is still
    /// running when the deadline passes (a Terminate without a quit, or a quit that hangs), the Engine and anything
    /// it started are killed, so no Engine is ever left running forever.</para>
    /// <para>Output. Two background threads copy stdout and stderr, as raw bytes, to the log and flush after every
    /// read, so the pipes never fill up (a chatty Engine never blocks) and a crash's last words are on disk. The log is
    /// closed once both pipes have ended, and the exit is reported after that (or after 2 s at most).</para>
    /// </remarks>
    private sealed class EngineProcess : IEngineProcess
    {
        /// <summary>How long a terminated Engine may keep running while the Launcher has not exited.</summary>
        private static readonly TimeSpan KillDeadline = TimeSpan.FromSeconds(10);

        /// <summary>How long the exit waits for the last output to reach the log.</summary>
        private static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(2);

        private readonly Process process;
        private readonly Stream log;
        private readonly Action<int> onExit;
        private readonly Lock gate = new();
        private readonly ManualResetEventSlim drained = new();
        private int pipesOpen = 2;
        private int exitReported;
        private int terminated;

        public EngineProcess(Process process, Stream log, Action<int> onExit)
        {
            this.process = process;
            this.log = log;
            this.onExit = onExit;
            process.Exited += OnExited;
        }

        /// <summary>Starts copying the output; call right after <see cref="Process.Start()"/>.</summary>
        public void Started()
        {
            Copy(process.StandardOutput.BaseStream, "stdout");
            Copy(process.StandardError.BaseStream, "stderr");
        }

        public void Terminate()
        {
            if (Interlocked.Exchange(ref terminated, 1) == 1 || Volatile.Read(ref exitReported) == 1)
            {
                return;
            }
            Log.Info($"Engine (pid {SafeId()}) asked to stop: it shuts down when the Launcher exits, or is killed in {KillDeadline.TotalSeconds:0} s");
            var deadline = new Thread(() =>
            {
                try
                {
                    if (process.WaitForExit(KillDeadline))
                    {
                        return;
                    }
                    Log.Warn("Engine still running after the deadline; killing it");
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e)
                {
                    Log.Warn("Could not stop the Engine", e); // already exited, or access denied
                }
            })
            {
                IsBackground = true, // never keeps the Launcher alive: exiting is how the Engine is asked to stop
                Name = "Engine stop deadline",
            };
            deadline.Start();
        }

        private void OnExited(object? sender, EventArgs e)
        {
            if (Interlocked.Exchange(ref exitReported, 1) == 1)
            {
                return;
            }
            int code;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                code = -1;
            }
            // The last words first: the log is complete before anyone reacts, and a restart appends after them.
            if (!drained.Wait(DrainLimit))
            {
                Log.Warn("The Engine's output is still open after it exited (a process it started may hold it)");
            }
            Log.Info($"Engine exited with code {code}");
            try
            {
                onExit(code);
            }
            catch (Exception exception)
            {
                Log.Warn("Handling the Engine's exit failed", exception);
            }
        }

        private void Copy(Stream source, string name)
        {
            var thread = new Thread(() =>
            {
                byte[] buffer = new byte[8192];
                try
                {
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        lock (gate)
                        {
                            try
                            {
                                log.Write(buffer, 0, read);
                                log.Flush();
                            }
                            catch (Exception e) when (e is IOException or ObjectDisposedException)
                            {
                                // A full disk must not stop the draining, or the Engine would block on a full pipe.
                            }
                        }
                    }
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                    Log.Warn($"Reading the Engine's {name} failed", e);
                }
                finally
                {
                    source.Dispose();
                    if (Interlocked.Decrement(ref pipesOpen) == 0)
                    {
                        lock (gate)
                        {
                            log.Dispose();
                        }
                        drained.Set();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Engine " + name,
            };
            thread.Start();
        }

        private string SafeId()
        {
            try
            {
                return process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidOperationException)
            {
                return "?";
            }
        }
    }
}
