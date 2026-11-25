using System.Windows.Interop;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// A system-wide shortcut through <c>RegisterHotKey</c>, owned by a message-only window on the UI thread: works from
/// any program and needs no keyboard hook. Alt+Space is also the window menu's key, and other tools (PowerToys Run,
/// ChatGPT, Copilot) may hold it, so the fallbacks of the preferred shortcut are tried in order.
/// </summary>
public sealed class GlobalHotKey : IGlobalHotKey
{
    /// <summary>Our id for the one hot key of the window (applications use 0x0000 to 0xBFFF).</summary>
    private const int Id = 0x504F; // "PO"

    private HwndSource? window;

    /// <summary>Registers <paramref name="preferred"/>, or the first of its fallbacks that is free. Call it on the UI thread.</summary>
    public GlobalHotKey(HotKeySpec preferred)
    {
        try
        {
            // A message-only window: never shown, gets no broadcasts, only the WM_HOTKEY posted to it.
            window = new HwndSource(new HwndSourceParameters("Pounce hot key")
            {
                WindowStyle = 0,
                ParentWindow = Win32.HWND_MESSAGE,
            });
            window.AddHook(WndProc);
        }
        catch (Exception e)
        {
            Log.Warn("Could not create the hot key's window", e);
            window = null;
            return;
        }
        foreach (HotKeySpec candidate in preferred.WithFallbacks())
        {
            if (Win32.RegisterHotKey(window.Handle, Id, (uint)candidate.Modifiers | Win32.MOD_NOREPEAT, (uint)candidate.Key))
            {
                Registered = candidate;
                Log.Info(candidate == preferred
                    ? $"Shortcut {candidate.DisplayName} registered"
                    : $"Shortcut {candidate.DisplayName} registered in place of {preferred.DisplayName}");
                return;
            }
            Log.Warn($"Shortcut {candidate.DisplayName} not registered: error {Win32.LastError()}");
        }
        Log.Warn("No keyboard shortcut is free; Pounce opens from its tray icon only");
    }

    /// <inheritdoc/>
    public HotKeySpec? Registered { get; private set; }

    /// <inheritdoc/>
    public event EventHandler? Pressed;

    /// <inheritdoc/>
    public void Dispose()
    {
        HwndSource? source = window;
        if (source is null)
        {
            return;
        }
        if (!source.Dispatcher.CheckAccess())
        {
            source.Dispatcher.Invoke(Dispose);
            return;
        }
        window = null;
        try
        {
            if (Registered is not null && !Win32.UnregisterHotKey(source.Handle, Id))
            {
                Log.Warn($"UnregisterHotKey failed: error {Win32.LastError()}");
            }
            Registered = null;
            source.RemoveHook(WndProc);
            source.Dispose(); // destroys the window, which would also free the hot key
        }
        catch (Exception e)
        {
            Log.Warn("Could not release the keyboard shortcut", e);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && wParam.ToInt64() == Id)
        {
            handled = true;
            try
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                Log.Warn("The shortcut's action failed", e);
            }
        }
        return IntPtr.Zero;
    }
}
