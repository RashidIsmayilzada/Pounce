namespace Pounce.Core;

/// <summary>A rectangle in physical pixels, as Win32 reports it (<c>RECT</c>): right and bottom are exclusive.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Right">One past the right edge.</param>
/// <param name="Bottom">One past the bottom edge.</param>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The width in pixels.</summary>
    public int Width => Right - Left;

    /// <summary>The height in pixels.</summary>
    public int Height => Bottom - Top;
}

/// <summary>
/// Where the search panel goes, like Spotlight: horizontally centred on the monitor with the mouse, its top edge a
/// quarter of the way down that monitor's work area. Works in the target monitor's physical pixels, so it is right on
/// mixed-DPI setups; DIPs (1/96 inch) are converted with that monitor's DPI.
/// </summary>
public static class PanelPlacement
{
    /// <summary>The panel's width in DIPs.</summary>
    public const double WidthDip = 680;

    /// <summary>The search bar's height in DIPs: the panel's height with nothing below the search field.</summary>
    public const double BarHeightDip = 56;

    /// <summary>The space kept free below the panel when it is as tall as it gets, in DIPs.</summary>
    public const double BottomMarginDip = 24;

    /// <summary>The results area never gets shorter than this, in DIPs, even on a very small work area.</summary>
    public const double MinResultsHeightDip = 120;

    /// <summary>DIPs to physical pixels at <paramref name="dpi"/> (96 is 100 %), rounded to the nearest pixel.</summary>
    public static int ToPixels(double dip, double dpi) => (int)Math.Round(dip * Scale(dpi), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The panel's top-left corner in pixels: centred horizontally in <paramref name="workArea"/> (kept inside it
    /// when it fits), top edge a quarter of the way down.
    /// </summary>
    /// <param name="workArea">The monitor's work area (without the taskbar), in pixels.</param>
    /// <param name="widthPx">The panel's width in pixels at that monitor's DPI.</param>
    public static (int X, int Y) Origin(PixelRect workArea, int widthPx)
    {
        int x = workArea.Left + (workArea.Width - widthPx) / 2;
        if (widthPx <= workArea.Width)
        {
            x = Math.Clamp(x, workArea.Left, workArea.Right - widthPx);
        }
        else
        {
            x = workArea.Left;
        }
        int y = workArea.Top + workArea.Height / 4;
        return (x, y);
    }

    /// <summary>
    /// How tall the results area below the search bar may get, in DIPs, so that a panel whose top edge is at
    /// <paramref name="topPx"/> ends <see cref="BottomMarginDip"/> above the bottom of <paramref name="workArea"/>.
    /// Never less than <see cref="MinResultsHeightDip"/>.
    /// </summary>
    /// <param name="workArea">The monitor's work area, in pixels.</param>
    /// <param name="topPx">The panel's top edge, in pixels.</param>
    /// <param name="dpi">The monitor's DPI (96 is 100 %).</param>
    /// <param name="dividerDip">The height of the line between the search bar and the results, in DIPs.</param>
    public static double MaxResultsHeightDip(PixelRect workArea, int topPx, double dpi, double dividerDip = 1)
    {
        double below = (workArea.Bottom - topPx) / Scale(dpi);
        return Math.Max(MinResultsHeightDip, below - BarHeightDip - dividerDip - BottomMarginDip);
    }

    private static double Scale(double dpi) => dpi > 0 ? dpi / 96.0 : 1.0;
}
