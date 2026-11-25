namespace Pounce.Core;

/// <summary>How a path is shown next to a file name in the panel.</summary>
public static class PathDisplay
{
    /// <summary>
    /// A path inside the user's profile folder is shown relative to it (<c>C:\Users\me\Documents\a\b.md</c> →
    /// <c>Documents\a\b.md</c>); anything else, and the profile folder itself, is shown in full. Letters are compared
    /// ignoring case, and <c>\</c> and <c>/</c> count as the same separator. Works on plain strings, on any OS.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="userProfile">The profile folder, such as <c>C:\Users\me</c> (<c>%USERPROFILE%</c>); blank shows every path in full.</param>
    public static string Abbreviate(string path, string? userProfile)
    {
        ArgumentNullException.ThrowIfNull(path);
        ReadOnlySpan<char> profile = (userProfile ?? "").AsSpan().TrimEnd(@"\/");
        if (profile.IsEmpty || path.Length <= profile.Length + 1 || !IsSeparator(path[profile.Length]))
        {
            return path;
        }
        for (int i = 0; i < profile.Length; i++)
        {
            char a = path[i];
            char b = profile[i];
            bool same = IsSeparator(a) ? IsSeparator(b) : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
            if (!same)
            {
                return path;
            }
        }
        return path[(profile.Length + 1)..];
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
