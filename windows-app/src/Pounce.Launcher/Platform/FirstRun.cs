using System.IO;
using System.Text;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>What Pounce sets up the first time it runs for a user.</summary>
public static class FirstRun
{
    /// <summary>
    /// How long finding the folders may take. Redirected folders can sit on a network share that is slow or offline;
    /// rather than hold up the start, Pounce then writes nothing, the Engine uses its own defaults, and the next start
    /// tries again.
    /// </summary>
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(2);

    /// <summary>Creates the settings file with this user's real Documents, Desktop and Downloads folders
    /// as Watched Folders when there is no settings file yet. Never throws.</summary>
    public static void EnsureSettings(PounceHome home)
    {
        try
        {
            if (File.Exists(home.ConfigFile))
            {
                return;
            }
            Task<IReadOnlyList<string>> finding = Task.Run(FindFolders);
            if (!finding.Wait(TimeLimit))
            {
                Log.Warn($"Finding the user's folders took over {TimeLimit.TotalSeconds:0} s; {home.ConfigFile} not written");
                return;
            }
            IReadOnlyList<string> folders = finding.Result;
            Directory.CreateDirectory(home.Folder);
            // CreateNew: never overwrite a file that appeared meanwhile (another Pounce, or the user).
            using (var file = new FileStream(home.ConfigFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(SettingsFile.Create(folders));
            }
            Log.Info($"Wrote {home.ConfigFile} watching: {(folders.Count == 0 ? "the Engine's defaults" : string.Join(", ", folders))}");
        }
        catch (Exception e)
        {
            Log.Warn($"Could not write {home.ConfigFile}", e);
        }
    }

    /// <summary>
    /// Documents, Desktop and Downloads as the shell knows them (OneDrive and Group Policy redirection included),
    /// those that exist, each once.
    /// </summary>
    private static IReadOnlyList<string> FindFolders()
    {
        string?[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify),
            Win32.KnownFolderPath(Win32.FOLDERID_Downloads),
        ];
        return SettingsFile.ExistingOnce(candidates, Directory.Exists);
    }
}
