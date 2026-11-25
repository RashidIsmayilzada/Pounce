using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Pounce.Core;
using Pounce.Launcher.Platform;

namespace Pounce.Launcher.Themes;

/// <summary>The panel's colours in the light app theme.</summary>
public partial class LightTheme : ResourceDictionary
{
    /// <summary>Loads the dictionary.</summary>
    public LightTheme() => InitializeComponent();
}

/// <summary>The panel's colours in the dark app theme.</summary>
public partial class DarkTheme : ResourceDictionary
{
    /// <summary>Loads the dictionary.</summary>
    public DarkTheme() => InitializeComponent();
}

/// <summary>The Windows app theme and accent colour, as the panel follows them.</summary>
/// <param name="Light">True for the light app theme.</param>
/// <param name="Accent">The system accent colour, opaque.</param>
internal readonly record struct SystemTheme(bool Light, Color Accent)
{
    /// <summary>Windows 11's default accent, for when the system's cannot be read.</summary>
    private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    /// <summary>
    /// Reads the current theme: two small values from the user's registry hive, which Windows keeps in memory.
    /// Never throws; light and the default blue when nothing can be read.
    /// </summary>
    public static SystemTheme Read()
    {
        bool light = WindowsTheme.IsLight(ReadValue(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme"));
        Color accent = WindowsTheme.AccentFromAbgr(ReadValue(@"Software\Microsoft\Windows\DWM", "AccentColor")) is { } rgb
            ? Color.FromRgb(rgb.R, rgb.G, rgb.B)
            : FallbackAccent();
        return new SystemTheme(light, accent);
    }

    private static object? ReadValue(string key, string name)
    {
        try
        {
            using RegistryKey? subKey = Registry.CurrentUser.OpenSubKey(key);
            return subKey?.GetValue(name);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            Log.Warn($"Cannot read HKCU\\{key}\\{name}", e);
            return null;
        }
    }

    /// <summary>The DWM colourisation colour (usually the accent) made opaque, or Windows' default blue.</summary>
    private static Color FallbackAccent()
    {
        Color glass = SystemParameters.WindowGlassColor;
        return glass.A == 0 ? DefaultAccent : Color.FromRgb(glass.R, glass.G, glass.B);
    }
}
