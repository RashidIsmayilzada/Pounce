using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>What Pounce sets up the first time it runs for a user. Placeholder: nothing yet.</summary>
public static class FirstRun
{
    /// <summary>Creates the settings file with this user's real Documents, Desktop and Downloads folders
    /// as Watched Folders when there is no settings file yet. Never throws.</summary>
    public static void EnsureSettings(PounceHome home) => _ = home;
}
