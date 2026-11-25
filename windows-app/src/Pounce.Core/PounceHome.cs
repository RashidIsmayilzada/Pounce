namespace Pounce.Core;

/// <summary>The folder where Pounce keeps its data (the Index, settings, logs), and the files the Launcher needs in it.</summary>
/// <param name="Folder">The folder, such as <c>C:\Users\me\AppData\Local\Pounce</c>.</param>
public sealed record PounceHome(string Folder)
{
    /// <summary>The file holding the bootstrap secret the Engine writes for the Launcher.</summary>
    public string SecretFile => WindowsPath.Join(Folder, "bootstrap-secret");

    /// <summary>The settings the user can edit, <c>pounce.yml</c>.</summary>
    public string ConfigFile => WindowsPath.Join(Folder, "pounce.yml");

    /// <summary>Where the Engine's output goes: <c>logs\engine.log</c>.</summary>
    public string EngineLog => WindowsPath.Join(Folder, "logs", "engine.log");

    /// <summary>Resolves the home of this process (see <see cref="Resolve(Func{string, string?}, Func{string}, Func{string})"/>).</summary>
    public static PounceHome Resolve() => Resolve(
        Environment.GetEnvironmentVariable,
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Directory.GetCurrentDirectory);

    /// <summary>
    /// Finds the home by the rules every Pounce program shares (the Engine, the <c>pounce</c> command; the cases are in
    /// <c>pounce-platform/src/test/resources/pounce-home-cases.csv</c>), so they always agree on it:
    /// <c>POUNCE_HOME</c> when it is set and not blank (used by smoke checks to keep away from real folders), made full
    /// against the current directory when it is relative, so the Engine, which runs elsewhere, gets the same folder;
    /// else <c>%LOCALAPPDATA%\Pounce</c>; else, when <c>LOCALAPPDATA</c> is unset or not a full path,
    /// <c>AppData\Local\Pounce</c> in the user's profile.
    /// </summary>
    /// <param name="environment">Looks up an environment variable; null when unset.</param>
    /// <param name="userHome">The user's profile folder, such as <c>C:\Users\me</c>.</param>
    /// <param name="currentDirectory">This process's current directory, for a relative <c>POUNCE_HOME</c>.</param>
    public static PounceHome Resolve(Func<string, string?> environment, Func<string> userHome, Func<string> currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(userHome);
        ArgumentNullException.ThrowIfNull(currentDirectory);
        string? chosen = environment("POUNCE_HOME");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            // Path.GetFullPath alone would treat C:\... as relative when the tests run on Linux.
            return new PounceHome(IsFullyQualified(chosen) ? chosen : Path.GetFullPath(chosen, currentDirectory()));
        }
        string? localAppData = environment("LOCALAPPDATA");
        return new PounceHome(localAppData is not null && IsFullyQualified(localAppData)
            ? WindowsPath.Join(localAppData, "Pounce")
            : WindowsPath.Join(userHome(), "AppData", "Local", "Pounce"));
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a full Windows path: <c>C:\</c> or <c>C:/</c>, or a <c>\\server\share</c>
    /// network path. Checked on the text, as the Engine's <c>OperatingSystem.isAbsolute</c> does, so the answer is the
    /// same on any OS.
    /// </summary>
    private static bool IsFullyQualified(string path) =>
        (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        || path.StartsWith(@"\\", StringComparison.Ordinal);
}
