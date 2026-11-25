using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// Opens and reveals through the Windows shell. Every action runs on its own short-lived STA thread (the shell wants
/// COM in a single-threaded apartment, and a slow network drive or file handler can block for seconds), so the UI
/// thread never waits. Problems are shown in a message box (only logged in an unattended run); nothing is thrown to
/// the caller.
/// </summary>
public sealed class ShellActions : IShellActions
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: the user closed the shell's own dialog
    private static readonly TimeSpan ObsidianCheckLifetime = TimeSpan.FromSeconds(30);

    private readonly Lock obsidianGate = new();
    private bool obsidianInstalled;
    private long obsidianCheckedAt = long.MinValue;

    /// <inheritdoc/>
    public void Open(SearchItem item)
    {
        switch (item)
        {
            case AppItem app:
                RunOnShellThread($"open {app.Name}", () => OpenApp(app));
                break;
            case FileItem file:
                RunOnShellThread($"open {file.Path}", () => OpenFile(file.Path));
                break;
            default:
                break;
        }
    }

    /// <inheritdoc/>
    public void Reveal(SearchItem item)
    {
        if (item is null)
        {
            return;
        }
        RunOnShellThread($"reveal {item.Path}", () => RevealPath(item));
    }

    /// <inheritdoc/>
    public void OpenInEditor(string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return;
        }
        RunOnShellThread($"edit {file}", () =>
        {
            if (!File.Exists(file) && string.Equals(Path.GetFileName(file), "pounce.yml", StringComparison.OrdinalIgnoreCase))
            {
                FirstRun.EnsureSettings(new PounceHome(Path.GetDirectoryName(Path.GetFullPath(file))!));
            }
            // Explicitly Notepad: .yml usually has no association. Windows 11 lets the user remove Notepad; then the
            // shell opens the file, offering "Open with" when nothing handles .yml.
            string notepadExe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            if (!File.Exists(notepadExe))
            {
                Log.Warn($"{notepadExe} is missing; opening {file} through the shell");
                ShellOpen(file, errorDialog: true);
                return;
            }
            var info = new ProcessStartInfo(notepadExe) { UseShellExecute = false };
            info.ArgumentList.Add(file);
            using Process? notepad = Process.Start(info);
        });
    }

    private static void OpenApp(AppItem app)
    {
        string path = app.App.Path;
        if (IsShellLocation(path))
        {
            try
            {
                ShellOpen(path, errorDialog: false);
            }
            catch (Win32Exception e)
            {
                // Some shells only resolve shell:AppsFolder\<AppUserModelId> through Explorer's own parser.
                Log.Warn($"Opening {path} through the shell failed ({e.NativeErrorCode}); trying Explorer", e);
                var info = new ProcessStartInfo(Path.Combine(WindowsFolder(), "explorer.exe")) { UseShellExecute = false };
                info.ArgumentList.Add(path);
                using Process? explorer = Process.Start(info);
            }
            return;
        }
        if (!File.Exists(path))
        {
            Problem($"“{app.Name}” can't be opened: its shortcut {path} no longer exists.");
            return;
        }
        ShellOpen(path, errorDialog: true);
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Problem($"“{Path.GetFileName(path)}” can't be opened: it is no longer at {path}.");
            return;
        }
        OpenTarget target = OpenTarget.For(path, ObsidianInstalled());
        ShellOpen(target.Target, errorDialog: true);
    }

    /// <summary>
    /// Opens <paramref name="target"/> as a double-click would (<c>ShellExecuteEx</c>). With
    /// <paramref name="errorDialog"/> the shell itself shows any error, or "How do you want to open this file?" for a
    /// type without an app (no <c>SEE_MASK_FLAG_NO_UI</c>), so a failure is only logged here, never shown twice.
    /// Without it, failures throw.
    /// </summary>
    private static void ShellOpen(string target, bool errorDialog)
    {
        var info = new ProcessStartInfo(target) { UseShellExecute = true, ErrorDialog = errorDialog };
        try
        {
            using Process? started = Process.Start(info);
        }
        catch (Win32Exception e) when (errorDialog)
        {
            Log.Warn(e.NativeErrorCode == ErrorCancelled
                ? $"Opening {target}: cancelled in the shell's dialog"
                : $"Opening {target} failed; the shell showed the error", e);
        }
    }

    private static void RevealPath(SearchItem item)
    {
        string path = item.Path;
        if (item is FileItem && !File.Exists(path) && !Directory.Exists(path))
        {
            Problem($"“{Path.GetFileName(path)}” can't be shown: it is no longer at {path}.");
            return;
        }
        if (item is AppItem && !IsShellLocation(path) && !File.Exists(path))
        {
            Log.Info($"Not revealing {path}: the shortcut no longer exists");
            return;
        }
        // The item's absolute ID list, then Explorer opens its folder with just that item selected, reusing a
        // window already showing the folder. Works for any name and for shell:AppsFolder\<AppUserModelId> too;
        // when Windows can't parse a Store app's location there is nothing sensible to show, so nothing happens.
        int hresult = Win32.SHParseDisplayName(path, IntPtr.Zero, out IntPtr pidl, 0, out _);
        if (hresult < 0)
        {
            Log.Warn($"SHParseDisplayName({path}) failed: {Win32.Describe(hresult)}");
            if (item is FileItem)
            {
                Problem($"Pounce couldn't show “{Path.GetFileName(path)}” in File Explorer.");
            }
            return;
        }
        try
        {
            hresult = Win32.SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
            if (hresult < 0)
            {
                Log.Warn($"SHOpenFolderAndSelectItems({path}) failed: {Win32.Describe(hresult)}");
                if (item is FileItem)
                {
                    Problem($"Pounce couldn't show “{Path.GetFileName(path)}” in File Explorer.");
                }
            }
        }
        finally
        {
            Win32.ILFree(pidl);
        }
    }

    /// <summary>
    /// Whether the <c>obsidian:</c> link protocol has a handler: <c>HKCU\Software\Classes\obsidian</c> (a per-user
    /// install, the usual one) or <c>HKCR\obsidian</c> (which also covers a machine-wide one). Remembered for 30 s.
    /// </summary>
    private bool ObsidianInstalled()
    {
        lock (obsidianGate)
        {
            long now = Environment.TickCount64;
            if (obsidianCheckedAt != long.MinValue && now - obsidianCheckedAt < (long)ObsidianCheckLifetime.TotalMilliseconds)
            {
                return obsidianInstalled;
            }
            try
            {
                using RegistryKey? user = Registry.CurrentUser.OpenSubKey(@"Software\Classes\obsidian");
                using RegistryKey? classes = user is null ? Registry.ClassesRoot.OpenSubKey("obsidian") : null;
                obsidianInstalled = user is not null || classes is not null;
            }
            catch (Exception e)
            {
                Log.Warn("Could not look up the obsidian: protocol", e);
                obsidianInstalled = false;
            }
            obsidianCheckedAt = now;
            return obsidianInstalled;
        }
    }

    /// <summary>A <c>shell:</c> location, such as <c>shell:AppsFolder\&lt;AppUserModelId&gt;</c> for a Store app.</summary>
    private static bool IsShellLocation(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    private static string WindowsFolder() =>
        Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windows ? windows : @"C:\Windows";

    /// <summary>Runs <paramref name="action"/> on a new background STA thread; logs and shows whatever it throws.</summary>
    private static void RunOnShellThread(string what, Action action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Warn($"Could not {what}", e);
                Problem($"Pounce couldn't {what}: {e.Message}");
            }
        })
        {
            IsBackground = true,
            Name = "Shell action",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// Tells the user, in a message box on the calling (shell) thread, so the UI thread is never blocked. By then the
    /// panel has hidden and another program may have the foreground, so the box has no owner, asks for the foreground
    /// and is topmost: it never opens unseen behind that program. In an unattended run (<see cref="DialogsAllowed"/>
    /// false) the problem is only logged.
    /// </summary>
    private static void Problem(string message)
    {
        if (!DialogsAllowed())
        {
            Log.Warn("Not shown (no dialogs): " + message);
            return;
        }
        try
        {
            if (Win32.MessageBox(IntPtr.Zero, message, "Pounce",
                Win32.MB_ICONWARNING | Win32.MB_SETFOREGROUND | Win32.MB_TOPMOST) == 0)
            {
                Log.Warn($"Could not show a message ({Win32.LastError()}): {message}");
            }
        }
        catch (Exception e)
        {
            Log.Warn("Could not show a message: " + message, e);
        }
    }

    /// <summary>
    /// False in an unattended run, where nobody would close a dialog: when the process is not interactive
    /// (<see cref="Environment.UserInteractive"/>) or the environment variable <c>POUNCE_NO_DIALOGS</c> is set.
    /// </summary>
    private static bool DialogsAllowed() =>
        Environment.UserInteractive && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POUNCE_NO_DIALOGS"));
}
