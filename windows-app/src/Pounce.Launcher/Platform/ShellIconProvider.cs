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
/// </remarks>
public sealed class ShellIconProvider : IIconProvider
{
    /// <summary>The size asked of the shell: 32 DIP at 200 %. Smaller screens scale it down.</summary>
    public const int PixelSize = 64;

    /// <summary>The size, in DIP, every image reports.</summary>
    private const double DipSize = 32;

    private readonly IconCache<ImageSource> cache = new(
        capacity: 400,
        maxWaiting: 32,
        schedule: work => StaThread.Shared.Post(work, DispatcherPriority.Background),
        onError: (_, e) => ShellLog.WarnOnce("Icon load", e));

    /// <inheritdoc/>
    public ImageSource? Cached(SearchItem item)
    {
        if (item is null)
        {
            return null;
        }
        try
        {
            return cache.TryGet(IconKey.For(item), out ImageSource? image) ? image : null;
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
            bool isApp = item is AppItem;
            return cache.GetAsync(IconKey.For(item), () => Load(path, isApp));
        }
        catch (Exception e)
        {
            ShellLog.WarnOnce("Icon request", e);
            return Task.FromResult<ImageSource?>(null);
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
}
