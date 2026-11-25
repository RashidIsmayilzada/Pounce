namespace Pounce.Core;

/// <summary>
/// Finds the apps in Start Menu <c>Programs</c> folders: every <c>.lnk</c> shortcut in them and their subfolders,
/// named by the file name without <c>.lnk</c>. Shortcuts that are not apps (see <see cref="LooksLikeApp"/>) are left
/// out, and a name that appears more than once (ignoring case) is kept from the first folder that has it, so list
/// the user's own folder before the all-users one. Hidden and system files are skipped, like Explorer does.
/// </summary>
/// <remarks>Uses only <see cref="System.IO"/>; Store apps come from another source.</remarks>
public sealed class StartMenuAppSource : IAppSource
{
    private static readonly string[] NotAppWords =
        ["uninstall", "readme", "read me", "license", "licence", "release notes", "website", "web site"];

    /// <summary>A source over <paramref name="folders"/>, most important first.</summary>
    public StartMenuAppSource(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        Folders = [.. folders];
    }

    /// <summary>The folders searched, most important first.</summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>
    /// The current user's Start Menu <c>Programs</c> folder, then the all-users one
    /// (<c>%APPDATA%\Microsoft\Windows\Start Menu\Programs</c> and <c>%ProgramData%\Microsoft\Windows\Start Menu\Programs</c>).
    /// </summary>
    public static StartMenuAppSource ForCurrentUser() => new(
        new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms }
            .Select(Environment.GetFolderPath)
            .Where(path => path.Length > 0));

    /// <inheritdoc/>
    public IReadOnlyList<AppEntry> ListApps()
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
        };
        var found = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppEntry>();
        foreach (string folder in Folders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, "*.lnk", options))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (LooksLikeApp(name) && !found.ContainsKey(name))
                    {
                        var app = new AppEntry(name, file);
                        found.Add(name, app);
                        apps.Add(app);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that vanished or can't be read just adds no apps.
            }
        }
        return apps;
    }

    /// <summary>
    /// False for shortcuts that installers put next to an app but that are not one. The rule, ignoring case and
    /// accents: the name contains "uninstall", "readme", "read me", "license", "licence", "release notes", "website" or
    /// "web site", or is "Help" or ends with " Help".
    /// </summary>
    public static bool LooksLikeApp(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string n = TextFolding.Fold(name);
        if (Array.Exists(NotAppWords, word => n.Contains(word, StringComparison.Ordinal)))
        {
            return false;
        }
        return n != "help" && !n.EndsWith(" help", StringComparison.Ordinal);
    }
}
