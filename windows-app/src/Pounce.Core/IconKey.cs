namespace Pounce.Core;

/// <summary>
/// Which rows share an icon. Search results are mostly ordinary documents, whose icon depends only on their type, so
/// every <c>.md</c> (or <c>.pdf</c>, <c>.txt</c> …) shares one icon. Types whose icon belongs to the file itself
/// (programs, shortcuts, icon files, files without an extension) get one per path, and so does every application.
/// </summary>
public static class IconKey
{
    /// <summary>The extensions whose icon is the file's own, not its type's (lower case, with the dot).</summary>
    public static IReadOnlySet<string> IndividualExtensions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ".exe", ".lnk", ".ico", ".url", ".appref-ms", ".cur", ".ani", ".scr", ".cpl", ".pif", ".website",
    };

    /// <summary>
    /// The cache key for <paramref name="item"/>: <c>app:</c> and the app's path for an application;
    /// <c>type:</c> and the lower-case extension for an ordinary file; <c>file:</c> and the lower-case path otherwise.
    /// </summary>
    public static string For(SearchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item is AppItem ? "app:" + item.Path : ForFile(item.Path);
    }

    /// <summary>The cache key for the file at <paramref name="path"/>; see <see cref="For(SearchItem)"/>.</summary>
    public static string ForFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Extension(path);
        return extension.Length == 0 || IndividualExtensions.Contains(extension)
            ? "file:" + path.ToLowerInvariant()
            : "type:" + extension;
    }

    /// <summary>
    /// The lower-case extension of the last part of a Windows path, with the dot (<c>.pdf</c>); empty when the name
    /// has none or ends with a dot. Both <c>\</c> and <c>/</c> separate folders, on every OS.
    /// </summary>
    public static string Extension(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int name = path.LastIndexOfAny(['\\', '/', ':']) + 1;
        int dot = path.LastIndexOf('.');
        if (dot < name || dot == path.Length - 1)
        {
            return "";
        }
        return path[dot..].ToLowerInvariant();
    }
}
