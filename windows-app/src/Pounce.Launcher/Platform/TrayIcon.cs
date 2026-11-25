namespace Pounce.Launcher.Platform;

/// <summary>Placeholder: shows no icon yet.</summary>
public sealed class TrayIcon : IDisposable
{
    /// <summary>Shows the icon in the notification area with the given menu.</summary>
    public TrayIcon(TrayMenu menu) => _ = menu;

    /// <inheritdoc/>
    public void Dispose() { }
}
