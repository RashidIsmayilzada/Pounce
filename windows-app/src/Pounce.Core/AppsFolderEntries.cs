namespace Pounce.Core;

/// <summary>
/// Turns the items of the shell's Apps folder (<c>shell:AppsFolder</c>, the list Start shows under "All apps") into
/// apps. Each item has a display name and a parsing name relative to the folder: an AppUserModelId for a Store app
/// (<c>Microsoft.WindowsCalculator_8wekyb3d8bbwe!App</c>), or a path-like id for a desktop app
/// (<c>{6D809377-6AF0-444B-8957-A3773F02200E}\7-Zip\7zFM.exe</c>).
/// </summary>
public static class AppsFolderEntries
{
    /// <summary>What goes before the parsing name to make an app's <see cref="AppEntry.Path"/>.</summary>
    public const string Prefix = @"shell:AppsFolder\";

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".chm", ".hlp", ".txt", ".rtf", ".pdf", ".htm", ".html", ".url", ".website", ".md", ".doc", ".docx", ".xps",
    };

    /// <summary>
    /// The apps among <paramref name="items"/>, in their order. An item is left out when its name or parsing name is
    /// blank, when its name does not look like an app (<see cref="StartMenuAppSource.LooksLikeApp"/>), when its parsing
    /// name is a web address (contains <c>://</c>), or when it is a path to a document such as a help file
    /// (<c>.chm</c>, <c>.txt</c>, <c>.pdf</c>, <c>.html</c>, <c>.url</c> …). A name that appears again, ignoring case,
    /// is kept from its first item. The path is <c>shell:AppsFolder\</c> and the parsing name: the shell launches it
    /// and makes a shell item (for the icon) from it.
    /// </summary>
    public static IReadOnlyList<AppEntry> ToApps(IEnumerable<(string Name, string ParsingName)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<AppEntry>();
        foreach (var (rawName, rawParsingName) in items)
        {
            string name = rawName?.Trim() ?? "";
            string parsingName = rawParsingName?.Trim() ?? "";
            if (name.Length == 0 || parsingName.Length == 0 || !StartMenuAppSource.LooksLikeApp(name)
                || IsDocumentOrLink(parsingName) || !seen.Add(name))
            {
                continue;
            }
            apps.Add(new AppEntry(name, Prefix + parsingName));
        }
        return apps;
    }

    private static bool IsDocumentOrLink(string parsingName) =>
        parsingName.Contains("://", StringComparison.Ordinal)
        || (parsingName.Contains('\\', StringComparison.Ordinal) && DocumentExtensions.Contains(IconKey.Extension(parsingName)));
}
