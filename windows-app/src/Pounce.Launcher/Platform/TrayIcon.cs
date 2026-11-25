using Pounce.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The notification-area icon: a left click opens the search panel; the menu shows the Engine's state and offers
/// "Launch at login", the settings file and Quit, like the macOS menu-bar icon. Create and dispose it on the UI thread.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly TrayMenu menu;
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ContextMenuStrip contextMenu;
    private readonly Drawing.Icon? ownIcon;
    private readonly Forms.ToolStripMenuItem status;
    private readonly Forms.ToolStripMenuItem loginItem;
    private bool failureShown;
    private bool disposed;

    /// <summary>Shows the icon in the notification area with the given menu.</summary>
    public TrayIcon(TrayMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        this.menu = menu;
        ownIcon = LoadOwnIcon();

        var search = new Forms.ToolStripMenuItem("Search…", null, (_, _) => Safely("Search", menu.Search));
        if (menu.HotKey is { } hotKey)
        {
            search.ShortcutKeyDisplayString = hotKey.DisplayName; // shown only; the global shortcut does the work
        }
        search.Font = new Drawing.Font(search.Font, Drawing.FontStyle.Bold); // the left-click action
        status = new Forms.ToolStripMenuItem { Enabled = false };
        loginItem = new Forms.ToolStripMenuItem("Launch at login", null, (_, _) => ToggleLogin());
        contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(search);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(status);
        if (TrayText.HotKeyNote(Configuration.FromEnvironment().HotKey, menu.HotKey) is { } note)
        {
            contextMenu.Items.Add(new Forms.ToolStripMenuItem(note) { Enabled = false });
        }
        contextMenu.Items.Add(loginItem);
        contextMenu.Items.Add(new Forms.ToolStripMenuItem("Settings file…", null, (_, _) => Safely("Settings file", menu.EditSettings)));
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(new Forms.ToolStripMenuItem("Quit Pounce", null, (_, _) => Safely("Quit", menu.Quit)));
        contextMenu.Opening += (_, _) => Safely("Refresh the menu", Refresh);

        icon = new Forms.NotifyIcon
        {
            Icon = ownIcon ?? Drawing.SystemIcons.Application,
            Text = TrayText.ToolTip(menu.HotKey),
            ContextMenuStrip = contextMenu,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                Safely("Search", menu.Search);
            }
        };
        Refresh();
        icon.Visible = true;

        if (menu.Supervisor is { } supervisor)
        {
            supervisor.StateChanged += OnStateChanged;
            if (Prerequisites.RedistributableMissing)
            {
                Log.Warn($"msvcp140_1.dll is missing: the search engine needs the {TrayText.Redistributable}");
            }
            OnStateChanged(supervisor, EventArgs.Empty); // in case it failed before the icon existed
        }
    }

    /// <summary>Removes the icon from the notification area at once (no ghost icon left behind).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (menu.Supervisor is { } supervisor)
        {
            supervisor.StateChanged -= OnStateChanged;
        }
        try
        {
            icon.Visible = false; // NIM_DELETE now, whatever happens next
            icon.Dispose();
            contextMenu.Dispose();
            ownIcon?.Dispose();
        }
        catch (Exception e)
        {
            Log.Warn("Could not remove the tray icon", e);
        }
    }

    /// <summary>Reads the state each time the menu opens, as the macOS menu does.</summary>
    private void Refresh()
    {
        status.Text = TrayText.Status(menu.Supervisor?.State, RedistributableMissingIfFailed());
        loginItem.Checked = menu.LoginItem.IsEnabled;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        Safely("Show the Engine's state", () =>
        {
            if (disposed)
            {
                return;
            }
            EngineState? state = menu.Supervisor?.State;
            bool missing = RedistributableMissingIfFailed();
            status.Text = TrayText.Status(state, missing);
            string? notice = TrayText.FailureNotice(state, missing);
            if (notice is null)
            {
                failureShown = false; // a later failure is shown again
                return;
            }
            if (failureShown)
            {
                return;
            }
            failureShown = true;
            Log.Warn($"Engine failed: {notice}");
            // Windows 10 and 11 show this as a notification (toast). Its text is limited to 255 characters.
            icon.ShowBalloonTip(10_000, "Pounce", notice.Length > 255 ? notice[..254] + "…" : notice, Forms.ToolTipIcon.Error);
        });
    }

    /// <summary>Only looks at the disk when it matters: when the Engine has failed.</summary>
    private bool RedistributableMissingIfFailed() =>
        menu.Supervisor?.State.Status == EngineStatus.Failed && Prerequisites.RedistributableMissing;

    private void ToggleLogin()
    {
        bool enable = !menu.LoginItem.IsEnabled;
        try
        {
            menu.LoginItem.SetEnabled(enable);
        }
        catch (Exception e)
        {
            Log.Warn("Could not change Launch at login", e);
            WpfMessageBox.Show(
                $"Pounce couldn't {(enable ? "turn on" : "turn off")} Launch at login.\n\n{e.Message}",
                "Pounce", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>The icon embedded in Pounce.exe, at the small-icon size of this display; null when unavailable.</summary>
    private static Drawing.Icon? LoadOwnIcon()
    {
        string? exe = Environment.ProcessPath;
        if (exe is null)
        {
            return null;
        }
        try
        {
            return Drawing.Icon.ExtractIcon(exe, 0, Forms.SystemInformation.SmallIconSize.Width)
                ?? Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch (Exception e)
        {
            Log.Warn($"Could not load the icon of {exe}", e);
            return null;
        }
    }

    private static void Safely(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Log.Warn($"Tray icon: {what} failed", e);
        }
    }
}
