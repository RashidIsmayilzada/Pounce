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

    /// <summary>Resolves the home of this process: <c>POUNCE_HOME</c> if set, else <c>%LOCALAPPDATA%\Pounce</c>.</summary>
    public static PounceHome Resolve() =>
        Resolve(Environment.GetEnvironmentVariable, () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// <c>POUNCE_HOME</c> when it is set and not blank (used by smoke checks to keep away from real folders), else
    /// <c>Pounce</c> in the local application data folder.
    /// </summary>
    /// <param name="environment">Looks up an environment variable; null when unset.</param>
    /// <param name="localAppData">The local application data folder (<c>%LOCALAPPDATA%</c>).</param>
    public static PounceHome Resolve(Func<string, string?> environment, Func<string> localAppData)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(localAppData);
        string? home = environment("POUNCE_HOME");
        return new PounceHome(string.IsNullOrWhiteSpace(home) ? WindowsPath.Join(localAppData(), "Pounce") : home);
    }
}
