using System.Windows;
using Pounce.Core;
using Pounce.Launcher.Platform;

namespace Pounce.Launcher;

/// <summary>The search panel. Placeholder: an empty window.</summary>
public sealed class LauncherWindow : Window, ILauncherPanel
{
    private readonly SearchModel model;

    /// <summary>Creates the panel, hidden.</summary>
    public LauncherWindow(SearchModel model, IShellActions shell, IIconProvider icons)
    {
        this.model = model;
        _ = shell;
        _ = icons;
        Title = "Pounce";
        Width = 680;
        Height = 56;
    }

    /// <inheritdoc/>
    public bool IsShown => IsVisible;

    /// <inheritdoc/>
    public void ShowPanel()
    {
        Show();
        Activate();
    }

    /// <inheritdoc/>
    public void HidePanel()
    {
        Hide();
        model.Query = "";
    }

    /// <inheritdoc/>
    public void Toggle()
    {
        if (IsShown) HidePanel(); else ShowPanel();
    }
}
