using System.Runtime.InteropServices;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The Win32 and DWM calls the search panel needs: monitors and DPI, position and z-order, window styles, the
/// foreground window, and the Windows 11 look (rounded corners, acrylic backdrop, dark frame).
/// </summary>
/// <remarks>
/// Every call is cheap (no disk, no blocking on other processes) and every failure is reported to the caller,
/// never thrown. Signatures follow the documented C declarations: BOOL is a 4-byte int (marshalled as
/// <see cref="UnmanagedType.Bool"/>), HRESULT an int, DWORD/UINT a uint, HWND/HMONITOR a pointer.
/// GetWindowLongW/SetWindowLongW (not the ...Ptr variants) are used on purpose: they exist on 32- and 64-bit Windows
/// alike and are correct for GWL_STYLE and GWL_EXSTYLE, whose values are 32-bit.
/// </remarks>
internal static partial class WindowNative
{
    // Window styles (WinUser.h).
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_SYSMENU = 0x0008_0000;
    private const int WS_MINIMIZEBOX = 0x0002_0000;
    private const int WS_MAXIMIZEBOX = 0x0001_0000;
    private const int WS_EX_TOOLWINDOW = 0x0000_0080;
    private const int WS_EX_APPWINDOW = 0x0004_0000;

    // SetWindowPos.
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    // Monitors.
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    // DWM (dwmapi.h). DWMWA_USE_IMMERSIVE_DARK_MODE was 19 before Windows 10 20H1 (build 19041).
    private const uint DWMWA_TRANSITIONS_FORCEDISABLED = 3;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;

    /// <summary>The first Windows 11 build with DWMWA_SYSTEMBACKDROP_TYPE (22H2).</summary>
    private const int SystemBackdropBuild = 22621;

    private const uint ASFW_ANY = 0xFFFF_FFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>MONITORINFO: 40 bytes; cbSize must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    /// <summary>The monitor a window is placed on: its work area in physical pixels and its effective DPI.</summary>
    /// <param name="WorkArea">The monitor's area without the taskbar, in physical pixels.</param>
    /// <param name="Dpi">The monitor's effective DPI; 96 is 100 %.</param>
    public readonly record struct MonitorArea(PixelRect WorkArea, double Dpi);

    /// <summary>
    /// The monitor with the mouse pointer (the primary monitor when the pointer cannot be read), or null when
    /// Windows reports no monitor information. The process is per-monitor-v2 DPI aware, so every value is in
    /// physical pixels and the DPI is that monitor's own.
    /// </summary>
    public static MonitorArea? MonitorWithPointer()
    {
        IntPtr monitor = GetCursorPos(out POINT cursor)
            ? MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST)
            : MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(monitor, ref info))
        {
            return null;
        }
        // Shcore.dll is in every Windows 8.1 and later; a failure leaves 96, which only affects the results' cap.
        double dpi = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) >= 0 && dpiX > 0 ? dpiX : 96;
        RECT work = info.rcWork;
        return new MonitorArea(new PixelRect(work.Left, work.Top, work.Right, work.Bottom), dpi);
    }

    /// <summary>The window's outer rectangle in physical pixels, or null on failure.</summary>
    public static PixelRect? WindowBounds(IntPtr hwnd) =>
        GetWindowRect(hwnd, out RECT r) ? new PixelRect(r.Left, r.Top, r.Right, r.Bottom) : null;

    /// <summary>Moves the window's top-left corner to (x, y) in physical pixels and puts it at the top of the topmost
    /// windows, without resizing or activating it. False on failure.</summary>
    public static bool MoveTopmost(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    /// <summary>
    /// Keeps the window out of the taskbar and Alt+Tab (WS_EX_TOOLWINDOW, without WS_EX_APPWINDOW) and drops the
    /// system menu and minimize/maximize boxes, so Alt+Space and a lone Alt do nothing in the panel. Call it before
    /// the window is first shown and again before each show (WPF may rewrite the styles); it only touches the
    /// window when something changed. False when Windows refused a change.
    /// </summary>
    public static bool ApplyPanelStyles(IntPtr hwnd)
    {
        bool ok = true;
        bool changed = false;
        int style = GetWindowLongW(hwnd, GWL_STYLE);
        int wantedStyle = style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        if (style != 0 && wantedStyle != style)
        {
            ok &= SetWindowLongW(hwnd, GWL_STYLE, wantedStyle) != 0 || Marshal.GetLastPInvokeError() == 0;
            changed = true;
        }
        int exStyle = GetWindowLongW(hwnd, GWL_EXSTYLE);
        int wantedExStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        if (wantedExStyle != exStyle)
        {
            // SetWindowLong returns the previous value, which may legitimately be 0; the source-generated stub clears
            // the last error before the call, so 0 with no error is a success.
            ok &= SetWindowLongW(hwnd, GWL_EXSTYLE, wantedExStyle) != 0 || Marshal.GetLastPInvokeError() == 0;
            changed = true;
        }
        if (changed)
        {
            // Style changes take effect for the frame only after SWP_FRAMECHANGED.
            ok &= SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
        return ok;
    }

    /// <summary>Switches off the window open/close animation: the panel must appear at once. Returns the HRESULT.</summary>
    public static int DisableTransitions(IntPtr hwnd) => SetAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, 1);

    /// <summary>Asks for Windows 11's rounded corners. Fails harmlessly (E_INVALIDARG) on Windows 10. Returns the HRESULT.</summary>
    public static int RoundCorners(IntPtr hwnd) => SetAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);

    /// <summary>
    /// Makes the frame, the shadow and the system backdrop dark or light. Uses attribute 20, and 19 on Windows 10
    /// builds before 20H1 that only know the older number. Returns the HRESULT of the last attempt.
    /// </summary>
    public static int SetDarkFrame(IntPtr hwnd, bool dark)
    {
        int hr = SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
        if (hr < 0)
        {
            hr = SetAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, dark ? 1 : 0);
        }
        return hr;
    }

    /// <summary>
    /// Turns on the system acrylic backdrop (the material of Windows 11 flyouts and menus) behind the whole window.
    /// Only on Windows 11 22H2 (build 22621) and later, the first with a documented DWMWA_SYSTEMBACKDROP_TYPE;
    /// Windows 10 and Windows 11 21H2 are not even asked. Returns false when the backdrop is not available or a call
    /// failed: the caller must then paint an opaque background, or the see-through window would show black.
    /// The caller must also make WPF render with a transparent background (HwndTarget.BackgroundColor).
    /// </summary>
    public static bool TryApplyAcrylic(IntPtr hwnd, out string detail)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, SystemBackdropBuild))
        {
            detail = $"Windows build {Environment.OSVersion.Version.Build} is older than {SystemBackdropBuild}";
            return false;
        }
        // The backdrop shows through the client area only where the frame is extended into it: all of it.
        var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
        int hr = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (hr < 0)
        {
            detail = $"DwmExtendFrameIntoClientArea failed: 0x{hr:X8}";
            return false;
        }
        hr = SetAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, DWMSBT_TRANSIENTWINDOW);
        if (hr < 0)
        {
            detail = $"DWMWA_SYSTEMBACKDROP_TYPE failed: 0x{hr:X8}";
            return false;
        }
        detail = "acrylic";
        return true;
    }

    /// <summary>True when <paramref name="hwnd"/> is the foreground window.</summary>
    public static bool IsForeground(IntPtr hwnd) => GetForegroundWindow() == hwnd;

    /// <summary>Who owns the foreground window.</summary>
    public static ForegroundOwner CurrentForegroundOwner()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return ForegroundOwner.None;
        }
        _ = GetWindowThreadProcessId(foreground, out uint pid);
        return pid == (uint)Environment.ProcessId ? ForegroundOwner.ThisProcess : ForegroundOwner.OtherProcess;
    }

    /// <summary>
    /// Makes <paramref name="hwnd"/> the foreground window when a plain SetForegroundWindow was refused.
    /// </summary>
    /// <remarks>
    /// Windows only lets a process take the foreground in some situations (it received the last input event, such as
    /// its registered hotkey or a click on its tray menu; or the foreground process allowed it). When it is refused,
    /// the documented way to share the foreground right is to attach this thread's input to the foreground window's
    /// thread for the duration of the call (AttachThreadInput): both threads then share one input state, and
    /// SetForegroundWindow succeeds. The attachment is undone at once. True when the window is now in the foreground.
    /// </remarks>
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (SetForegroundWindow(hwnd) && IsForeground(hwnd))
        {
            return true;
        }
        IntPtr foreground = GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        uint thisThread = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != thisThread
            && AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
        return IsForeground(hwnd);
    }

    /// <summary>
    /// Lets whichever process comes next take the foreground (ASFW_ANY): called just before the shell opens an item,
    /// while Pounce still has the foreground right from the key press or click, so the app that opens comes to the
    /// front instead of flashing in the taskbar. False when Windows refused (Pounce had no right to pass on).
    /// </summary>
    public static bool AllowAnyProcessToTakeForeground() => AllowSetForegroundWindow(ASFW_ANY);

    private static int SetAttribute(IntPtr hwnd, uint attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    // BOOL GetCursorPos(LPPOINT lpPoint);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    // HMONITOR MonitorFromPoint(POINT pt, DWORD dwFlags); POINT is passed by value.
    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    // BOOL GetMonitorInfoW(HMONITOR hMonitor, LPMONITORINFO lpmi);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    // HRESULT GetDpiForMonitor(HMONITOR hmonitor, MONITOR_DPI_TYPE dpiType, UINT *dpiX, UINT *dpiY);
    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // BOOL GetWindowRect(HWND hWnd, LPRECT lpRect);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // BOOL SetWindowPos(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, UINT uFlags);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    // LONG GetWindowLongW(HWND hWnd, int nIndex);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    // LONG SetWindowLongW(HWND hWnd, int nIndex, LONG dwNewLong);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    // HRESULT DwmSetWindowAttribute(HWND hwnd, DWORD dwAttribute, LPCVOID pvAttribute, DWORD cbAttribute);
    // Every attribute used here is a 4-byte BOOL or enum, so pvAttribute is a pointer to an int.
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

    // HRESULT DwmExtendFrameIntoClientArea(HWND hWnd, const MARGINS *pMarInset);
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    // HWND GetForegroundWindow(void);
    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    // BOOL SetForegroundWindow(HWND hWnd);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    // BOOL BringWindowToTop(HWND hWnd);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    // DWORD GetWindowThreadProcessId(HWND hWnd, LPDWORD lpdwProcessId); returns the thread id, 0 on failure.
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // DWORD GetCurrentThreadId(void);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    // BOOL AttachThreadInput(DWORD idAttach, DWORD idAttachTo, BOOL fAttach);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    // BOOL AllowSetForegroundWindow(DWORD dwProcessId);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint dwProcessId);
}
