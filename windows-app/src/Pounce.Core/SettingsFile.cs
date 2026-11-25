using System.Text;

namespace Pounce.Core;

/// <summary>
/// The text of the <c>pounce.yml</c> that Pounce writes the first time it runs for a user: a short comment, then the
/// user's Watched Folders as a YAML list, which replaces the Engine's built-in defaults.
/// </summary>
/// <remarks>
/// The Launcher asks Windows where Documents, Desktop and Downloads really are (they are often moved into OneDrive or
/// renamed by a company policy), which the Engine cannot see from its environment variables alone.
/// </remarks>
public static class SettingsFile
{
    /// <summary>Line ends of the file: Windows ones, since the user edits it in Notepad.</summary>
    public const string NewLine = "\r\n";

    private static readonly string[] Header =
    [
        "# Pounce settings. Pounce reads this file when it starts: after editing it, quit Pounce from its",
        "# icon in the notification area and start it again.",
        "#",
        "# watched-folders: the folders Pounce indexes and searches, one per line. Keep each path in",
        "# single quotes; a ' inside a path is written twice ('').",
    ];

    /// <summary>
    /// The file's text with <paramref name="watchedFolders"/> as the Watched Folders. With none, the list is written as
    /// a commented-out example, so the Engine keeps its own defaults.
    /// </summary>
    /// <param name="watchedFolders">Full paths, in the order to list them.</param>
    public static string Create(IReadOnlyList<string> watchedFolders)
    {
        ArgumentNullException.ThrowIfNull(watchedFolders);
        var text = new StringBuilder();
        foreach (string line in Header)
        {
            text.Append(line).Append(NewLine);
        }
        text.Append(NewLine);
        if (watchedFolders.Count == 0)
        {
            text.Append("# pounce:").Append(NewLine)
                .Append("#   watched-folders:").Append(NewLine)
                .Append("#     - 'C:\\Users\\me\\Documents'").Append(NewLine);
            return text.ToString();
        }
        text.Append("pounce:").Append(NewLine)
            .Append("  watched-folders:").Append(NewLine);
        foreach (string folder in watchedFolders)
        {
            text.Append("    - ").Append(Quote(folder)).Append(NewLine);
        }
        return text.ToString();
    }

    /// <summary>
    /// <paramref name="value"/> as a single-quoted YAML scalar: taken literally (a <c>\</c> is just a <c>\</c>), with
    /// each <c>'</c> doubled.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// The candidates that are set and exist, each folder once, in order. Windows paths: two candidates are the same
    /// folder when they differ only in case or in a trailing <c>\</c> or <c>/</c>.
    /// </summary>
    /// <param name="candidates">Paths; null or blank ones (a folder Windows could not name) are skipped.</param>
    /// <param name="exists">Whether a folder exists.</param>
    public static IReadOnlyList<string> ExistingOnce(IEnumerable<string?> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new List<string>();
        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }
            string folder = candidate.Trim();
            if (!seen.Contains(folder.TrimEnd('\\', '/')) && exists(folder))
            {
                seen.Add(folder.TrimEnd('\\', '/'));
                folders.Add(folder);
            }
        }
        return folders;
    }
}
