using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>The applications Start lists. Placeholder: the Start Menu shortcuts only.</summary>
public sealed class WindowsAppSource : IAppSource
{
    private readonly StartMenuAppSource startMenu = StartMenuAppSource.ForCurrentUser();

    /// <inheritdoc/>
    public IReadOnlyList<AppEntry> ListApps() => startMenu.ListApps();
}
