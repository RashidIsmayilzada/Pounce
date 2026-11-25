namespace Pounce.Core;

/// <summary>An installed application.</summary>
/// <param name="Name">The name shown and matched, such as "Visual Studio Code".</param>
/// <param name="Path">
/// What the shell launches: a <c>.lnk</c> file path, or <c>shell:AppsFolder\&lt;AppUserModelId&gt;</c> for a Store
/// app. Also the app's identity.
/// </param>
public sealed record AppEntry(string Name, string Path)
{
    /// <summary>Identifies the app: its path.</summary>
    public string Id => Path;
}

/// <summary>Lists the installed applications.</summary>
public interface IAppSource
{
    /// <summary>
    /// Lists the applications now installed. May be slow (it reads the disk): the search model calls it on a
    /// thread-pool thread, so it must not touch the UI.
    /// </summary>
    IReadOnlyList<AppEntry> ListApps();
}

/// <summary>No applications, for when app search is off.</summary>
public sealed class NoApps : IAppSource
{
    /// <inheritdoc/>
    public IReadOnlyList<AppEntry> ListApps() => [];
}

/// <summary>
/// Ranks apps against a Query the way a launcher should: exact name, then the start of the name, then the start of
/// any word, then initials, then anywhere in the name. One letter only matches the start of a name. Case and accents
/// don't matter ("cafe" finds "Café"), on any machine and in any culture.
/// </summary>
public static class AppMatcher
{
    /// <summary>At most this many apps are shown.</summary>
    public const int Limit = 3;

    /// <summary>The best matches for <paramref name="query"/>, best first; none for a blank Query.</summary>
    public static IReadOnlyList<AppEntry> Rank(string query, IReadOnlyList<AppEntry> apps)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(apps);
        string q = TextFolding.Fold(query.Trim());
        if (q.Length == 0)
        {
            return [];
        }
        return apps
            .Select(app => (App: app, Score: Score(q, app.Name)))
            .Where(match => match.Score is not null)
            .OrderBy(match => match.Score)
            .ThenBy(match => match.App.Name.Length)
            .ThenBy(match => match.App.Name, StringComparer.Ordinal)
            .Take(Limit)
            .Select(match => match.App)
            .ToArray();
    }

    private static int? Score(string q, string name)
    {
        string n = TextFolding.Fold(name);
        if (n == q)
        {
            return 0;
        }
        if (n.StartsWith(q, StringComparison.Ordinal))
        {
            return 1;
        }
        if (q.Length <= 1)
        {
            return null;
        }
        List<string> words = Words(n);
        string[] queryWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.All(part => words.Exists(word => word.StartsWith(part, StringComparison.Ordinal))))
        {
            return 2;
        }
        if (new string([.. words.Select(word => word[0])]).StartsWith(q, StringComparison.Ordinal))
        {
            return 3;
        }
        if (q.Length >= 3 && n.Contains(q, StringComparison.Ordinal))
        {
            return 4;
        }
        return null;
    }

    /// <summary>The runs of letters and digits: "visual studio code", "7-zip file manager" → 7, zip, file, manager.</summary>
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool inWord = i < text.Length && (char.IsLetterOrDigit(text[i]) || char.IsNumber(text[i]) || char.IsSurrogate(text[i]));
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
        }
        return words;
    }
}
