using System.Runtime.InteropServices;

namespace Pounce.Launcher.Platform;

// The shell and GDI declarations behind WindowsAppSource and ShellIconProvider, copied from the Windows SDK
// 10.0.26100 headers (ShObjIdl_core.h, ShlObj_core.h, ShlGuid.h, KnownFolders.h, shellapi.h, wingdi.h, WinUser.h).
//
// COM interfaces use classic interop ([ComImport]): the runtime calls QueryInterface, AddRef and Release, so each
// interface declares only its own methods, in the header's order, which is the vtable order. All three derive
// directly from IUnknown. Every method keeps its HRESULT ([PreserveSig]) so a failure is a value to log, not an
// exception. The P/Invokes that pass COM interfaces need [DllImport]; the plain ones use [LibraryImport].
//
// Ownership: an interface the runtime hands out is an RCW, released with Marshal.ReleaseComObject on the thread that
// got it (the STA worker), not left to the finalizer. A string from GetDisplayName is freed with CoTaskMemFree
// (Marshal.FreeCoTaskMem). An HBITMAP from GetImage is freed with DeleteObject. An HICON from SHGetFileInfo is freed
// with DestroyIcon. A DC from GetDC is given back with ReleaseDC.

/// <summary>SIGDN (ShObjIdl_core.h): which name of a shell item to get.</summary>
internal enum Sigdn : uint
{
    /// <summary>SIGDN_NORMALDISPLAY: the name shown to the user.</summary>
    NormalDisplay = 0x00000000,

    /// <summary>SIGDN_PARENTRELATIVEPARSING: the parsing name relative to the parent folder.</summary>
    ParentRelativeParsing = 0x80018001,
}

/// <summary>SIIGBF flags (ShObjIdl_core.h, <c>typedef int SIIGBF</c>) for <see cref="IShellItemImageFactory.GetImage"/>.</summary>
[Flags]
internal enum Siigbf
{
    /// <summary>SIIGBF_RESIZETOFIT: shrink the image to fit the size asked for, keeping its aspect ratio.</summary>
    ResizeToFit = 0x0,

    /// <summary>SIIGBF_BIGGERSIZEOK: a bigger image than asked for is fine.</summary>
    BiggerSizeOk = 0x1,

    /// <summary>SIIGBF_MEMORYONLY: only what is in memory.</summary>
    MemoryOnly = 0x2,

    /// <summary>SIIGBF_ICONONLY: the icon, never a thumbnail.</summary>
    IconOnly = 0x4,

    /// <summary>SIIGBF_THUMBNAILONLY: a thumbnail, never an icon.</summary>
    ThumbnailOnly = 0x8,

    /// <summary>SIIGBF_INCACHEONLY: only from the thumbnail cache.</summary>
    InCacheOnly = 0x10,
}

/// <summary>SIZE (windef.h): two LONGs, passed by value.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeSize
{
    public int Cx;
    public int Cy;
}

/// <summary>IShellItem (ShObjIdl_core.h), derives from IUnknown.</summary>
[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    /// <summary>HRESULT BindToHandler(IBindCtx *pbc, REFGUID bhid, REFIID riid, void **ppv). Only used with
    /// <c>riid</c> = IID_IEnumShellItems, so <c>ppv</c> is declared as that interface.</summary>
    [PreserveSig]
    int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IEnumShellItems? ppv);

    /// <summary>HRESULT GetParent(IShellItem **ppsi). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? ppsi);

    /// <summary>HRESULT GetDisplayName(SIGDN sigdnName, LPWSTR *ppszName). The caller frees the string with CoTaskMemFree.</summary>
    [PreserveSig]
    int GetDisplayName(Sigdn sigdnName, out IntPtr ppszName);

    /// <summary>HRESULT GetAttributes(SFGAOF sfgaoMask, SFGAOF *psfgaoAttribs). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

    /// <summary>HRESULT Compare(IShellItem *psi, SICHINTF hint, int *piOrder). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem psi, uint hint, out int piOrder);
}

/// <summary>IEnumShellItems (ShObjIdl_core.h), derives from IUnknown.</summary>
[ComImport]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEnumShellItems
{
    /// <summary>HRESULT Next(ULONG celt, IShellItem **rgelt, ULONG *pceltFetched). Called with celt = 1 only, so
    /// <c>rgelt</c> is a single interface. S_OK (0) with one item; S_FALSE (1) at the end.</summary>
    [PreserveSig]
    int Next(uint celt, [MarshalAs(UnmanagedType.Interface)] out IShellItem? rgelt, out uint pceltFetched);

    /// <summary>HRESULT Skip(ULONG celt). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int Skip(uint celt);

    /// <summary>HRESULT Reset(void). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int Reset();

    /// <summary>HRESULT Clone(IEnumShellItems **ppenum). Unused; declared for the vtable.</summary>
    [PreserveSig]
    int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems? ppenum);
}

/// <summary>IShellItemImageFactory (ShObjIdl_core.h), derives from IUnknown.</summary>
[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    /// <summary>HRESULT GetImage(SIZE size, SIIGBF flags, HBITMAP *phbm). The caller frees the bitmap with DeleteObject.</summary>
    [PreserveSig]
    int GetImage(NativeSize size, Siigbf flags, out IntPtr phbm);
}

/// <summary>BITMAP (wingdi.h), default packing: 32 bytes on x64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBitmap
{
    public int BmType;
    public int BmWidth;
    public int BmHeight;
    public int BmWidthBytes;
    public ushort BmPlanes;
    public ushort BmBitsPixel;
    public IntPtr BmBits;
}

/// <summary>
/// BITMAPINFO (wingdi.h) for a 32-bit BI_RGB request: BITMAPINFOHEADER (40 bytes, default packing) followed by room
/// for colour masks. A 32-bit BI_RGB DIB has no colour table; the spare room only guards against GDI writing one.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeBitmapInfo
{
    public uint BiSize;
    public int BiWidth;
    public int BiHeight;
    public ushort BiPlanes;
    public ushort BiBitCount;
    public uint BiCompression;
    public uint BiSizeImage;
    public int BiXPelsPerMeter;
    public int BiYPelsPerMeter;
    public uint BiClrUsed;
    public uint BiClrImportant;
    public uint Spare0;
    public uint Spare1;
    public uint Spare2;
    public uint Spare3;
}

/// <summary>SHFILEINFOW (shellapi.h): natural packing on 64-bit Windows (pshpack1 applies to 32-bit only).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeFileInfo
{
    public IntPtr HIcon;
    public int IIcon;
    public uint DwAttributes;
    public fixed ushort SzDisplayName[260];
    public fixed ushort SzTypeName[80];
}

/// <summary>The functions, constants and GUIDs.</summary>
internal static partial class ShellNative
{
    public const int SOk = 0;

    /// <summary>BI_RGB (wingdi.h).</summary>
    public const uint BiRgb = 0;

    /// <summary>DIB_RGB_COLORS (wingdi.h).</summary>
    public const uint DibRgbColors = 0;

    /// <summary>SHGFI_ICON (shellapi.h).</summary>
    public const uint ShgfiIcon = 0x000000100;

    /// <summary>SHGFI_LARGEICON (shellapi.h).</summary>
    public const uint ShgfiLargeIcon = 0x000000000;

    /// <summary>SHGFI_USEFILEATTRIBUTES (shellapi.h): describe a file of this name and attributes; it need not exist.</summary>
    public const uint ShgfiUseFileAttributes = 0x000000010;

    /// <summary>FILE_ATTRIBUTE_NORMAL (winnt.h).</summary>
    public const uint FileAttributeNormal = 0x00000080;

    /// <summary>KF_FLAG_DEFAULT (ShlObj_core.h).</summary>
    public const uint KfFlagDefault = 0;

    /// <summary>FOLDERID_AppsFolder (KnownFolders.h): the virtual folder of everything Start lists under "All apps".</summary>
    public static readonly Guid FolderIdAppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    /// <summary>BHID_EnumItems (ShlGuid.h): bind a folder item to an IEnumShellItems over its children.</summary>
    public static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    public static readonly Guid IidIShellItem = typeof(IShellItem).GUID;
    public static readonly Guid IidIEnumShellItems = typeof(IEnumShellItems).GUID;
    public static readonly Guid IidIShellItemImageFactory = typeof(IShellItemImageFactory).GUID;

    /// <summary>STDAPI SHGetKnownFolderItem(REFKNOWNFOLDERID rfid, KNOWN_FOLDER_FLAG flags, HANDLE hToken, REFIID riid,
    /// void **ppv) (ShlObj_core.h). Declared for riid = IID_IShellItem.</summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    public static extern int SHGetKnownFolderItem(
        ref Guid rfid, uint flags, IntPtr hToken, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    /// <summary>SHSTDAPI SHCreateItemFromParsingName(PCWSTR pszPath, IBindCtx *pbc, REFIID riid, void **ppv)
    /// (ShObjIdl_core.h). Declared for riid = IID_IShellItem.</summary>
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int SHCreateShellItem(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);

    /// <summary>The same function, declared for riid = IID_IShellItemImageFactory.</summary>
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int SHCreateImageFactory(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    /// <summary>DWORD_PTR SHGetFileInfoW(LPCWSTR pszPath, DWORD dwFileAttributes, SHFILEINFOW *psfi, UINT cbFileInfo,
    /// UINT uFlags) (shellapi.h). With SHGFI_ICON the caller frees <c>psfi->hIcon</c> with DestroyIcon.</summary>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static unsafe partial IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, NativeFileInfo* psfi, uint cbFileInfo, uint uFlags);

    /// <summary>int GetObjectW(HANDLE h, int c, LPVOID pv) (wingdi.h).</summary>
    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static unsafe partial int GetObject(IntPtr h, int c, NativeBitmap* pv);

    /// <summary>int GetDIBits(HDC hdc, HBITMAP hbm, UINT start, UINT cLines, LPVOID lpvBits, LPBITMAPINFO lpbmi, UINT usage)
    /// (wingdi.h). Returns the number of scan lines copied; 0 on failure.</summary>
    [LibraryImport("gdi32.dll")]
    public static unsafe partial int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, void* lpvBits, NativeBitmapInfo* lpbmi, uint usage);

    /// <summary>BOOL DeleteObject(HGDIOBJ ho) (wingdi.h).</summary>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr ho);

    /// <summary>HDC GetDC(HWND hWnd) (WinUser.h): with NULL, the screen's DC. Give it back with ReleaseDC.</summary>
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetDC(IntPtr hWnd);

    /// <summary>int ReleaseDC(HWND hWnd, HDC hDC) (WinUser.h).</summary>
    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>BOOL DestroyIcon(HICON hIcon) (WinUser.h).</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr hIcon);

    /// <summary>"0x80070002": an HRESULT as Windows prints it, for the log.</summary>
    public static string Hex(int hresult) => "0x" + hresult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Releases an RCW now, on this thread, instead of leaving it to the finalizer. Null does nothing.</summary>
    public static void Release(object? rcw)
    {
        if (rcw is not null && Marshal.IsComObject(rcw))
        {
            Marshal.ReleaseComObject(rcw);
        }
    }
}
