using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The Win32 functions the integration layer calls, as documented on learn.microsoft.com. BOOLs are marshalled as
/// 4-byte <c>bool</c>; HRESULTs are returned as <c>int</c> and checked by the caller; every function whose failure
/// is reported through <c>GetLastError</c> sets <c>SetLastError</c>, so <see cref="LastError"/> reads it.
/// </summary>
internal static partial class Win32
{
    /// <summary><c>WM_HOTKEY</c>: posted to the window that registered a hot key when it is pressed.</summary>
    public const int WM_HOTKEY = 0x0312;

    /// <summary><c>MOD_NOREPEAT</c>: holding the keys down does not repeat <c>WM_HOTKEY</c>.</summary>
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary><c>HWND_MESSAGE</c>: the parent that makes a window message-only (no UI, no broadcasts).</summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary><c>ASFW_ANY</c>: lets any process take the foreground (used when the other's id is unknown).</summary>
    public const uint ASFW_ANY = unchecked((uint)-1);

    /// <summary><c>KF_FLAG_DONT_VERIFY</c>: return the path without checking that the folder exists (it may be on
    /// a slow network share; the caller checks, with a time limit).</summary>
    public const uint KF_FLAG_DONT_VERIFY = 0x00004000;

    /// <summary><c>MB_ICONWARNING</c> (WinUser.h: <c>MB_ICONEXCLAMATION</c>, 0x00000030L): the warning icon. With
    /// none of the MB_OK… button flags (all 0 is <c>MB_OK</c>), one OK button.</summary>
    public const uint MB_ICONWARNING = 0x00000030;

    /// <summary><c>MB_SETFOREGROUND</c> (WinUser.h, 0x00010000L): the box asks to become the foreground window.</summary>
    public const uint MB_SETFOREGROUND = 0x00010000;

    /// <summary><c>MB_TOPMOST</c> (WinUser.h, 0x00040000L): the box is topmost, so it shows even when the foreground
    /// lock keeps it from the foreground.</summary>
    public const uint MB_TOPMOST = 0x00040000;

    /// <summary><c>FOLDERID_Downloads</c>, from KnownFolders.h.</summary>
    public static readonly Guid FOLDERID_Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary><c>BOOL RegisterHotKey(HWND hWnd, int id, UINT fsModifiers, UINT vk)</c>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    /// <summary><c>BOOL UnregisterHotKey(HWND hWnd, int id)</c>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary><c>BOOL AllowSetForegroundWindow(DWORD dwProcessId)</c>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    /// <summary>
    /// <c>int MessageBoxW(HWND hWnd, LPCWSTR lpText, LPCWSTR lpCaption, UINT uType)</c>. Modal on the calling thread;
    /// returns the button pressed, 0 on failure.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    /// <summary>
    /// <c>HRESULT SHGetKnownFolderPath(REFKNOWNFOLDERID rfid, DWORD dwFlags, HANDLE hToken, PWSTR *ppszPath)</c>.
    /// <paramref name="ppszPath"/> must be freed with <c>CoTaskMemFree</c> whether the call succeeds or not.
    /// </summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    /// <summary>
    /// <c>HRESULT SHParseDisplayName(PCWSTR pszName, IBindCtx *pbc, PIDLIST_ABSOLUTE *ppidl, SFGAOF sfgaoIn,
    /// SFGAOF *psfgaoOut)</c>. <paramref name="ppidl"/> is freed with <see cref="ILFree"/>.
    /// </summary>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    /// <summary>
    /// <c>HRESULT SHOpenFolderAndSelectItems(PCIDLIST_ABSOLUTE pidlFolder, UINT cidl, PCUITEMID_CHILD_ARRAY apidl,
    /// DWORD dwFlags)</c>. With <paramref name="cidl"/> 0, <paramref name="pidlFolder"/> is the one item to select.
    /// Needs COM initialized on the calling thread.
    /// </summary>
    [LibraryImport("shell32.dll")]
    public static partial int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr apidl, uint dwFlags);

    /// <summary><c>void ILFree(PIDLIST_RELATIVE pidl)</c>.</summary>
    [LibraryImport("shell32.dll")]
    public static partial void ILFree(IntPtr pidl);

    /// <summary>The last Win32 error of this thread's last P/Invoke, as "1409 (Hot key is already registered.)".</summary>
    public static string LastError()
    {
        int code = Marshal.GetLastPInvokeError();
        return $"{code} ({new Win32Exception(code).Message})";
    }

    /// <summary>An HRESULT as "0x80070002 (The system cannot find the file specified.)".</summary>
    public static string Describe(int hresult) =>
        $"0x{hresult:X8} ({Marshal.GetExceptionForHR(hresult)?.Message ?? "unknown error"})";

    /// <summary>
    /// The path of a known folder (<c>FOLDERID_*</c>), as the shell has it for this user, redirection included;
    /// null when it has none. Does not check that the folder exists.
    /// </summary>
    public static string? KnownFolderPath(Guid folderId)
    {
        int hresult = SHGetKnownFolderPath(folderId, KF_FLAG_DONT_VERIFY, IntPtr.Zero, out IntPtr path);
        try
        {
            if (hresult < 0)
            {
                Log.Warn($"SHGetKnownFolderPath({folderId}) failed: {Describe(hresult)}");
                return null;
            }
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path); // CoTaskMemFree; does nothing for null.
        }
    }
}
