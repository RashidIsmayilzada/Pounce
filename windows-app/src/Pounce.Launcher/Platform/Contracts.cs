using System.Windows.Media;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

// What the parts of the shell expect from each other. App.xaml.cs wires the implementations together.
// Everything here is used on the UI thread unless a member says otherwise.

/// <summary>The search panel, as the hotkey, the tray icon and a second launch see it.</summary>
public interface ILauncherPanel
{
    /// <summary>True while the panel is on screen.</summary>
    bool IsShown { get; }

    /// <summary>Shows the panel on the screen with the mouse, focused, with an empty search field.</summary>
    void ShowPanel();

    /// <summary>Hides the panel and clears the Query, so the next open starts fresh.</summary>
    void HidePanel();

    /// <summary>Shows the panel when hidden, hides it when shown.</summary>
    void Toggle();
}

/// <summary>A system-wide keyboard shortcut that works from any program.</summary>
public interface IGlobalHotKey : IDisposable
{
    /// <summary>The shortcut that was registered: the preferred one, or the first fallback that was free;
    /// null when every candidate is taken by another program.</summary>
    HotKeySpec? Registered { get; }

    /// <summary>Raised on the UI thread each time the shortcut is pressed (not while it is held).</summary>
    event EventHandler? Pressed;
}

/// <summary>Opening things through the Windows shell.</summary>
public interface IShellActions
{
    /// <summary>Launches an application, or opens a file the way double-clicking it would
    /// (Markdown inside an Obsidian vault opens in Obsidian). Never throws; never blocks the UI thread for long.</summary>
    void Open(SearchItem item);

    /// <summary>Opens File Explorer with the file selected. For an application, its shortcut or folder.</summary>
    void Reveal(SearchItem item);

    /// <summary>Opens a text file (the settings file) in the user's editor.</summary>
    void OpenInEditor(string file);
}

/// <summary>Icons for the rows of the panel, as Explorer shows them.</summary>
public interface IIconProvider
{
    /// <summary>The icon when it is already loaded; null otherwise. Immediate: never touches the disk.</summary>
    ImageSource? Cached(SearchItem item);

    /// <summary>Loads the icon off the UI thread and returns it frozen; null when there is none.
    /// The continuation runs on the caller's thread.</summary>
    Task<ImageSource?> LoadAsync(SearchItem item);
}

/// <summary>Whether Pounce starts when the user signs in.</summary>
public interface ILoginItem
{
    /// <summary>True when Pounce is registered to start at sign-in.</summary>
    bool IsEnabled { get; }

    /// <summary>Registers or removes the sign-in entry for this Pounce.exe.</summary>
    void SetEnabled(bool enabled);
}

/// <summary>What the tray icon's menu shows and does.</summary>
/// <param name="Supervisor">The bundled Engine's supervisor; null when the Engine is started by hand.</param>
/// <param name="HotKey">The registered shortcut, for the menu text; null when none could be registered.</param>
/// <param name="LoginItem">The sign-in entry behind "Launch at login".</param>
/// <param name="Search">Opens the search panel.</param>
/// <param name="EditSettings">Opens the settings file.</param>
/// <param name="Quit">Quits Pounce.</param>
public sealed record TrayMenu(
    EngineSupervisor? Supervisor,
    HotKeySpec? HotKey,
    ILoginItem LoginItem,
    Action Search,
    Action EditSettings,
    Action Quit);
