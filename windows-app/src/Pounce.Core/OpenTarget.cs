namespace Pounce.Core;

/// <summary>How a Result is opened.</summary>
public enum OpenTargetKind
{
    /// <summary>The file itself, in its default app.</summary>
    File,

    /// <summary>An <c>obsidian://</c> link, which opens the note in Obsidian.</summary>
    ObsidianLink,
}

/// <summary>
/// What opening a Result should open: Markdown inside an Obsidian vault goes through Obsidian's own link, so it
/// opens in the right vault instead of whatever app is the default for <c>.md</c>; everything else opens as a file.
/// </summary>
/// <param name="Kind">Whether <paramref name="Target"/> is a file path or a link.</param>
/// <param name="Target">What to hand to the shell (<c>ShellExecute</c>, or <c>Process.Start</c> with <c>UseShellExecute</c>).</param>
public sealed record OpenTarget(OpenTargetKind Kind, string Target)
{
    /// <summary>What to open for the file at <paramref name="path"/>; checks the disk for a vault above it.</summary>
    /// <param name="path">The Result's path.</param>
    /// <param name="obsidianInstalled">Whether the <c>obsidian:</c> link protocol has a handler on this machine.</param>
    public static OpenTarget For(string path, bool obsidianInstalled)
    {
        ArgumentNullException.ThrowIfNull(path);
        string extension = Path.GetExtension(path);
        bool markdown = extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase);
        if (!obsidianInstalled || !markdown || !IsInsideVault(path))
        {
            return new OpenTarget(OpenTargetKind.File, path);
        }
        return new OpenTarget(OpenTargetKind.ObsidianLink, "obsidian://open?path=" + Uri.EscapeDataString(path));
    }

    /// <summary>A vault is any folder above the file that holds a <c>.obsidian</c> folder.</summary>
    private static bool IsInsideVault(string file)
    {
        for (string? folder = Path.GetDirectoryName(file); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (Directory.Exists(Path.Combine(folder, ".obsidian")))
            {
                return true;
            }
        }
        return false;
    }
}
