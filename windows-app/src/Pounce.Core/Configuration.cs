namespace Pounce.Core;

/// <summary>
/// Where the Engine is and how the Launcher is set up: <c>POUNCE_URL</c>, <c>POUNCE_HOME</c> and <c>POUNCE_HOTKEY</c>
/// override the defaults, as for the <c>pounce</c> command.
/// </summary>
/// <param name="EngineUrl">The Engine's address: <c>POUNCE_URL</c>, or <see cref="DefaultEngineUrl"/>.</param>
/// <param name="Home">The data folder: <c>POUNCE_HOME</c> (made full), or <c>%LOCALAPPDATA%\Pounce</c>.</param>
/// <param name="HotKey">The shortcut that opens the panel: <c>POUNCE_HOTKEY</c>, or <see cref="HotKeySpec.Default"/>.</param>
public sealed record Configuration(Uri EngineUrl, PounceHome Home, HotKeySpec HotKey)
{
    /// <summary>The Engine's default address, <c>http://127.0.0.1:7431</c>.</summary>
    public static Uri DefaultEngineUrl { get; } = new("http://127.0.0.1:7431");

    /// <summary>The configuration from this process's environment.</summary>
    public static Configuration FromEnvironment() => FromEnvironment(
        Environment.GetEnvironmentVariable,
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Directory.GetCurrentDirectory);

    /// <summary>
    /// The configuration from <paramref name="environment"/>. A blank or invalid <c>POUNCE_URL</c> (it must be an
    /// absolute http or https address) gives the default, and so does an unknown <c>POUNCE_HOTKEY</c>.
    /// </summary>
    /// <param name="environment">Looks up an environment variable; null when unset.</param>
    /// <param name="userHome">The user's profile folder (see <see cref="PounceHome.Resolve(Func{string, string?}, Func{string}, Func{string})"/>).</param>
    /// <param name="currentDirectory">This process's current directory, for a relative <c>POUNCE_HOME</c>.</param>
    public static Configuration FromEnvironment(Func<string, string?> environment, Func<string> userHome, Func<string> currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(environment);
        string? url = environment("POUNCE_URL")?.Trim();
        Uri engineUrl = Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed
            : DefaultEngineUrl;
        return new Configuration(engineUrl, PounceHome.Resolve(environment, userHome, currentDirectory),
            HotKeySpec.Parse(environment("POUNCE_HOTKEY")));
    }

    /// <summary>
    /// The bundled Engine next to <c>Pounce.exe</c> in <paramref name="appFolder"/>, set up to listen where
    /// <see cref="EngineUrl"/> says: its port, and its address when that is a loopback address other than the Engine's
    /// own <c>127.0.0.1</c>. Null when <see cref="EngineUrl"/> is not a plain http address on this machine's loopback
    /// (an Engine on another machine, or behind something that speaks https): then the Launcher uses that one and
    /// starts none of its own.
    /// </summary>
    /// <param name="appFolder">The folder holding <c>Pounce.exe</c> (<c>AppContext.BaseDirectory</c>).</param>
    public EngineLayout? BundledEngine(string appFolder) => BundledEngine(appFolder, Directory.Exists);

    /// <summary><see cref="BundledEngine(string)"/>, asking <paramref name="folderExists"/> instead of the disk.</summary>
    internal EngineLayout? BundledEngine(string appFolder, Func<string, bool> folderExists)
    {
        if (EngineUrl.Scheme != Uri.UriSchemeHttp || !EngineUrl.IsLoopback)
        {
            return null;
        }
        // localhost reaches the Engine's 127.0.0.1 too (the client tries each address it resolves to).
        string? address = EngineUrl.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && EngineUrl.DnsSafeHost != "127.0.0.1"
            ? EngineUrl.DnsSafeHost
            : null;
        return EngineLayout.Bundled(appFolder, Home.Folder, folderExists) with { Port = EngineUrl.Port, Address = address };
    }

    /// <summary>A client for the Engine, logging in with the secret in <see cref="Home"/>. Keep it for the life of the app.</summary>
    public HttpEngineClient CreateEngineClient() => new(EngineUrl, Home.SecretFile);

    /// <summary>A probe for the Engine, for the <see cref="EngineSupervisor"/>.</summary>
    public HttpEngineProbe CreateEngineProbe() => new(EngineUrl);
}
