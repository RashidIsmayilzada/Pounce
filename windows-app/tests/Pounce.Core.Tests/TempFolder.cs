namespace Pounce.Core.Tests;

/// <summary>A fresh folder under the system temp folder, deleted with everything in it on dispose.</summary>
/// <remarks>
/// The paths it returns use this system's separator throughout, as <see cref="Directory.EnumerateFiles(string)"/>
/// and the other <see cref="System.IO"/> calls do, so a test can compare them with what those calls return as plain
/// strings, on Windows as on Linux.
/// </remarks>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Root = Path.Combine(Path.GetTempPath(), "pounce-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>Creates the folder at <paramref name="relative"/> ("/"-separated) and returns its full path.</summary>
    public string Folder(string relative)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates the file at <paramref name="relative"/> ("/"-separated), and its folders, and returns its full path.</summary>
    public string File(string relative, string content = "")
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort (on Windows a file still open can't be deleted): the temp folder is cleaned by the system anyway.
        }
    }

    /// <summary>The full path of <paramref name="relative"/>, with this system's separator in place of every "/".</summary>
    private string PathOf(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
}
