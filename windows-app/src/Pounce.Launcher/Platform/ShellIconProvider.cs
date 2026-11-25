using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pounce.Core;

namespace Pounce.Launcher.Platform;

/// <summary>
/// The icons Explorer shows, for the rows of the panel. Icons are loaded on the shared <see cref="StaThread"/> through
/// <c>IShellItemImageFactory</c>, icon only (never a thumbnail, which would read the file and can run third-party
/// handlers for a long time), at <see cref="PixelSize"/> pixels so they stay sharp at 32 DIP on a 200 % screen.
/// </summary>
/// <remarks>
/// <para>Every image is frozen and reports a size of 32 × 32 DIP, whatever its pixel size.</para>
/// <para>Caching and queueing follow <see cref="IconKey"/> and <see cref="IconCache{T}"/>: one icon per document type,
/// one per path for programs, shortcuts and apps; loads for the same key are shared; the newest request is served
/// first and stale ones are dropped. A file that no longer exists, or that the shell cannot make an item for, gets
/// the generic icon of its type (<c>SHGetFileInfo</c>, which needs no file on disk). Failures are logged once per
/// cause and give no icon.</para>
/// <para>Every load runs on that one thread, so one slow shell call holds up every icon and the Apps-folder listing.
/// A file on a volume that may be slow (a UNC path, or a network, removable or optical drive: see
/// <see cref="VolumePath"/>) is therefore never touched: it gets its type's icon, cached under the type key. Which
/// kind a drive is comes from <c>GetDriveTypeW</c>, asked off the UI and shell threads (<see cref="DriveSpeeds"/>).
/// Apps are always loaded from their shell item.</para>
/// </remarks>
public sealed class ShellIconProvider : IIconProvider
{
    /// <summary>The size asked of the shell: 32 DIP at 200 %. Smaller screens scale it down.</summary>
    public const int PixelSize = 64;

    /// <summary>The size, in DIP, every image reports.</summary>
    private const double DipSize = 32;

    /// <summary>How long one icon request waits for a drive's type before treating the drive as slow.</summary>
    private static readonly TimeSpan DriveTypeWait = TimeSpan.FromSeconds(1);

    private readonly IconCache<ImageSource> cache = new(
        capacity: 400,
        maxWaiting: 32,
        schedule: work => StaThread.Shared.Post(work, DispatcherPriority.Background),
        onError: (_, e) => ShellLog.WarnOnce("Icon load", e));

    private readonly DriveSpeeds drives = new();

    /// <inheritdoc/>
    public ImageSource? Cached(SearchItem item)
    {
        if (item is null)
        {
            return null;
        }
        try
        {
            string? key = item is AppItem ? IconKey.For(item) : KnownFileKey(item.Path);
            return key is not null && cache.TryGet(key, out ImageSource? image) ? image : null;
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Icon lookup", e);
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<ImageSource?> LoadAsync(SearchItem item)
    {
        if (item is null)
        {
            return Task.FromResult<ImageSource?>(null);
        }
        try
        {
            string path = item.Path;
            if (item is AppItem)
            {
                return cache.GetAsync(IconKey.For(item), () => Load(path, isApp: true));
            }
            PathVolume volume = VolumePath.Of(path);
            return KnownSlow(volume) is { } slow ? LoadFile(path, slow) : LoadAfterDriveTypeAsync(path, volume.DriveRoot!);
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Icon request", e);
            return Task.FromResult<ImageSource?>(null);
        }
    }

    /// <summary>
    /// Whether a volume may be slow, when that can be told without asking Windows now: a UNC path, or anything that
    /// is not a drive letter, is slow; a drive's answer is known for a while once <see cref="DriveSpeeds"/> has asked.
    /// Null when the drive must be asked first. Immediate.
    /// </summary>
    private bool? KnownSlow(PathVolume volume) =>
        volume.Kind == VolumeKind.Drive ? drives.Known(volume.DriveRoot!) : true;

    /// <summary>The cache key of a file, when its volume's speed is known (<see cref="KnownSlow"/>); null otherwise.</summary>
    private string? KnownFileKey(string path) =>
        KnownSlow(VolumePath.Of(path)) switch
        {
            null => null,
            true => VolumePath.SlowFileIconKey(path),
            false => IconKey.ForFile(path),
        };

    /// <summary>
    /// A file's icon, keyed as <see cref="VolumePath"/> says. A file on a slow volume gets its type's icon and the
    /// volume is never touched; any other file gets its own icon from the shell.
    /// </summary>
    private Task<ImageSource?> LoadFile(string path, bool slow) =>
        slow
            ? cache.GetAsync(VolumePath.SlowFileIconKey(path), () => FromFileType(path))
            : cache.GetAsync(IconKey.ForFile(path), () => Load(path, isApp: false));

    /// <summary>
    /// <see cref="LoadFile"/> once the drive's type is known; a drive that does not say within
    /// <see cref="DriveTypeWait"/> is treated as slow for this request. Never throws.
    /// </summary>
    private async Task<ImageSource?> LoadAfterDriveTypeAsync(string path, string root)
    {
        try
        {
            bool slow;
            try
            {
                slow = await drives.AskAsync(root).WaitAsync(DriveTypeWait).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ShellLog.WarnOnce("drive type slow " + root, $"Icons: Windows took over {DriveTypeWait.TotalSeconds:0} s to say what kind of drive {root} is; treating it as slow");
                slow = true;
            }
            return await LoadFile(path, slow).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Icon request", e);
            return null;
        }
    }

    /// <summary>The icon for a path. Runs on the STA thread; never throws.</summary>
    private static ImageSource? Load(string path, bool isApp)
    {
        ImageSource? icon = FromShellItem(path, isApp);
        if (icon is not null || isApp)
        {
            // An app is a shell:AppsFolder item or a Start Menu shortcut: a generic icon would say nothing.
            return icon;
        }
        return FromFileType(path);
    }

    /// <summary>The shell item's own icon: <c>SHCreateItemFromParsingName</c>, then <c>IShellItemImageFactory::GetImage</c>.</summary>
    private static BitmapSource? FromShellItem(string path, bool isApp)
    {
        string kind = isApp ? "app" : "file " + IconKey.Extension(path);
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            Guid factoryId = ShellNative.IidIShellItemImageFactory;
            int hr = ShellNative.SHCreateImageFactory(path, IntPtr.Zero, ref factoryId, out factory);
            if (hr < 0 || factory is null)
            {
                // 0x80070002 and 0x80070003 (not found) are expected for files deleted since they were indexed.
                ShellLog.WarnOnce("SHCreateItemFromParsingName " + ShellNative.Hex(hr),
                    $"Icons: no shell item for a path ({kind}): SHCreateItemFromParsingName {ShellNative.Hex(hr)}");
                return null;
            }
            var size = new NativeSize { Cx = PixelSize, Cy = PixelSize };
            // SIIGBF_ICONONLY with SIIGBF_RESIZETOFIT (0): the icon, never a thumbnail, at most PixelSize square.
            hr = factory.GetImage(size, Siigbf.IconOnly | Siigbf.ResizeToFit, out bitmap);
            if (hr < 0 || bitmap == IntPtr.Zero)
            {
                ShellLog.WarnOnce("GetImage " + ShellNative.Hex(hr),
                    $"Icons: IShellItemImageFactory.GetImage {ShellNative.Hex(hr)} ({kind})");
                return null;
            }
            return FromHBitmap(bitmap);
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Shell item icon", e);
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                ShellNative.DeleteObject(bitmap);
            }
            ShellNative.Release(factory);
        }
    }

    /// <summary>
    /// A 32-bit HBITMAP as a frozen image, alpha kept. GDI copies the pixels into a top-down 32-bit buffer (a negative
    /// height asks for top-down rows, whatever the bitmap's own row order). The caller still owns the HBITMAP.
    /// </summary>
    private static unsafe BitmapSource? FromHBitmap(IntPtr bitmap)
    {
        NativeBitmap header;
        if (ShellNative.GetObject(bitmap, sizeof(NativeBitmap), &header) == 0)
        {
            ShellLog.WarnOnce("GetObject", "Icons: GetObject failed on the shell's bitmap");
            return null;
        }
        int width = header.BmWidth;
        int height = Math.Abs(header.BmHeight);
        if (width is <= 0 or > 1024 || height is <= 0 or > 1024)
        {
            ShellLog.WarnOnce("bitmap size", $"Icons: the shell's bitmap is {width} x {height}");
            return null;
        }

        var info = new NativeBitmapInfo
        {
            BiSize = 40, // sizeof(BITMAPINFOHEADER), not of this struct
            BiWidth = width,
            BiHeight = -height,
            BiPlanes = 1,
            BiBitCount = 32,
            BiCompression = ShellNative.BiRgb,
        };
        byte[] pixels = new byte[width * height * 4];
        IntPtr screen = ShellNative.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            ShellLog.WarnOnce("GetDC", "Icons: GetDC failed");
            return null;
        }
        int lines;
        try
        {
            fixed (byte* bits = pixels)
            {
                lines = ShellNative.GetDIBits(screen, bitmap, 0, (uint)height, bits, &info, ShellNative.DibRgbColors);
            }
        }
        finally
        {
            ShellNative.ReleaseDC(IntPtr.Zero, screen);
        }
        if (lines != height)
        {
            ShellLog.WarnOnce("GetDIBits", $"Icons: GetDIBits copied {lines} of {height} rows");
            return null;
        }
        return Frozen(width, height, pixels, header.BmBitsPixel == 32);
    }

    /// <summary>The generic icon of the file's type, from its extension alone: no file is read.</summary>
    private static unsafe BitmapSource? FromFileType(string path)
    {
        string extension = IconKey.Extension(path);
        var info = default(NativeFileInfo);
        IntPtr found = ShellNative.SHGetFileInfo("file" + extension, ShellNative.FileAttributeNormal, &info,
            (uint)sizeof(NativeFileInfo), ShellNative.ShgfiIcon | ShellNative.ShgfiLargeIcon | ShellNative.ShgfiUseFileAttributes);
        if (found == IntPtr.Zero || info.HIcon == IntPtr.Zero)
        {
            ShellLog.WarnOnce("SHGetFileInfo", $"Icons: SHGetFileInfo found no icon for type {extension}");
            return null;
        }
        try
        {
            BitmapSource icon = Imaging.CreateBitmapSourceFromHIcon(info.HIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(icon, PixelFormats.Pbgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            return Frozen(width, height, pixels, hasAlpha: true);
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Generic icon", e);
            return null;
        }
        finally
        {
            ShellNative.DestroyIcon(info.HIcon);
        }
    }

    /// <summary>
    /// A frozen image of 32-bit BGRA rows, reporting 32 × 32 DIP. The shell's icons carry premultiplied alpha; a
    /// bitmap without alpha (every alpha byte 0, or not 32-bit to begin with) is made opaque instead of invisible, and
    /// one whose colours exceed their alpha cannot be premultiplied, so it is read as straight alpha.
    /// </summary>
    private static BitmapSource Frozen(int width, int height, byte[] pixels, bool hasAlpha)
    {
        bool anyAlpha = false;
        bool premultiplied = true;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            anyAlpha |= alpha != 0;
            premultiplied &= pixels[i] <= alpha && pixels[i + 1] <= alpha && pixels[i + 2] <= alpha;
        }
        if (!hasAlpha || !anyAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }
            premultiplied = true;
        }
        double dpi = 96.0 * Math.Max(width, height) / DipSize;
        PixelFormat format = premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
        var image = BitmapSource.Create(width, height, dpi, dpi, format, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Whether each drive letter may be slow (<see cref="VolumePath.MayBeSlow"/>), asked of Windows at most once per
    /// root per <see cref="Lifetime"/>, on a thread-pool thread. Thread-safe.
    /// </summary>
    /// <remarks>
    /// <see cref="DriveInfo.DriveType"/> is <c>GetDriveTypeW</c> on the root (<c>Z:\</c>) and nothing else (checked
    /// in the .NET 10 runtime's IL; no error-mode change, no volume query). For a drive-letter root Windows answers
    /// from the session's device map, from the type of the device the letter links to, so a mapped drive is
    /// <c>DRIVE_REMOTE</c> without the server being contacted, connected or not, and a letter that links to nothing
    /// is <c>DRIVE_NO_ROOT_DIR</c>. That is how Windows behaves as far as known, not something tested here, so
    /// nothing relies on it: should a drive take long to answer, only its own files' icons wait, at most
    /// <c>DriveTypeWait</c> before getting their type's icon, with one question per root at a time and never on the
    /// UI or the shell thread.
    /// </remarks>
    private sealed class DriveSpeeds
    {
        /// <summary>How long an answer is trusted: a stick can be plugged in or a share mapped to a letter meanwhile.</summary>
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);

        private readonly Lock gate = new();
        private readonly Dictionary<string, (Task<bool> Answer, long AskedAt)> answers = new(StringComparer.Ordinal);

        /// <summary>The answer for <paramref name="root"/> when it is in and still fresh; null otherwise. Immediate.</summary>
        public bool? Known(string root)
        {
            lock (gate)
            {
                return answers.TryGetValue(root, out var entry) && Fresh(entry.AskedAt) && entry.Answer.IsCompletedSuccessfully
                    ? entry.Answer.Result
                    : null;
            }
        }

        /// <summary>
        /// The answer for <paramref name="root"/>: the fresh one, the question already on its way, or a new question
        /// asked on a thread-pool thread. The task never faults.
        /// </summary>
        public Task<bool> AskAsync(string root)
        {
            lock (gate)
            {
                if (answers.TryGetValue(root, out var entry) && (Fresh(entry.AskedAt) || !entry.Answer.IsCompleted))
                {
                    return entry.Answer;
                }
                Task<bool> asking = Task.Run(() => Ask(root));
                answers[root] = (asking, Environment.TickCount64);
                return asking;
            }
        }

        private static bool Fresh(long askedAt) => Environment.TickCount64 - askedAt < (long)Lifetime.TotalMilliseconds;

        /// <summary>Asks Windows. A drive whose type cannot be read counts as slow. Never throws.</summary>
        private static bool Ask(string root)
        {
            try
            {
                DriveType type = new DriveInfo(root).DriveType;
                bool slow = VolumePath.MayBeSlow(type);
                if (slow)
                {
                    ShellLog.WarnOnce("slow drive " + root + " " + type,
                        $"Icons: {root} is a drive of type {type}; its files get their type's icon without touching it");
                }
                return slow;
            }
            catch (Exception e)
            {
                ShellLog.WarnOnce("Drive type", e);
                return true;
            }
        }
    }
}
