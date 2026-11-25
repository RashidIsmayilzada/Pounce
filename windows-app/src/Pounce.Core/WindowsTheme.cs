namespace Pounce.Core;

/// <summary>Reads the values Windows stores for the app theme and the accent colour. Plain data, no registry access.</summary>
public static class WindowsTheme
{
    /// <summary>
    /// True for the light app theme. <paramref name="appsUseLightTheme"/> is the <c>AppsUseLightTheme</c> DWORD under
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c>: 0 is dark, anything else light.
    /// Missing or of another type (older Windows, a damaged value) counts as light, Windows' default.
    /// </summary>
    public static bool IsLight(object? appsUseLightTheme) => appsUseLightTheme is not int value || value != 0;

    /// <summary>
    /// The red, green and blue of the <c>AccentColor</c> DWORD under <c>HKCU\Software\Microsoft\Windows\DWM</c>, which
    /// Windows stores as 0xAABBGGRR (red in the lowest byte). Null when the value is missing or not a DWORD.
    /// </summary>
    public static (byte R, byte G, byte B)? AccentFromAbgr(object? accentColor)
    {
        if (accentColor is not int value)
        {
            return null;
        }
        uint abgr = unchecked((uint)value);
        return ((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
    }
}
