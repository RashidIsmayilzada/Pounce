namespace Pounce.Core.Tests;

/// <summary>A fresh folder under the system temp folder, deleted with everything in it on dispose.</summary>
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
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Creates the file at <paramref name="relative"/> ("/"-separated), and its folders, and returns its full path.</summary>
    public string File(string relative, string content = "")
    {
        string path = Path.Combine(Root, relative);
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
        catch (IOException)
        {
            // Best effort: the temp folder is cleaned by the system anyway.
        }
    }
}
