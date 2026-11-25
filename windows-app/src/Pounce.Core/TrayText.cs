namespace Pounce.Core;

/// <summary>The words of the tray icon: its tooltip, the lines of its menu and its notification.</summary>
public static class TrayText
{
    /// <summary>What Windows calls the package that holds <c>msvcp140_1.dll</c>, which the Engine needs.</summary>
    public const string Redistributable = "Microsoft Visual C++ Redistributable (x64)";

    /// <summary>The menu line when no shortcut could be registered.</summary>
    public const string NoHotKey = "No free keyboard shortcut — open from this icon";

    /// <summary>The tooltip: "Pounce", and the shortcut when there is one ("Pounce (Alt+Space)").</summary>
    public static string ToolTip(HotKeySpec? registered) =>
        registered is { } hotKey ? $"Pounce ({hotKey.DisplayName})" : "Pounce";

    /// <summary>
    /// The disabled status line of the menu. Without a supervisor (<paramref name="state"/> null) the Engine was
    /// started by hand. A failure while the Visual C++ runtime is missing names the missing package rather than an
    /// exit code, because that is what the user can fix.
    /// </summary>
    /// <param name="state">The supervisor's state; null when there is no supervisor.</param>
    /// <param name="redistributableMissing">True when <c>msvcp140_1.dll</c> is not installed.</param>
    public static string Status(EngineState? state, bool redistributableMissing)
    {
        if (state is null)
        {
            return "Search engine started by hand";
        }
        if (state.Status == EngineStatus.Failed && redistributableMissing)
        {
            return $"Pounce needs the {Redistributable}";
        }
        return state.Description;
    }

    /// <summary>
    /// The notification to show once when the Engine fails: the problem, or how to fix a missing Visual C++ runtime.
    /// Null when <paramref name="state"/> is not a failure.
    /// </summary>
    public static string? FailureNotice(EngineState? state, bool redistributableMissing)
    {
        if (state is not { Status: EngineStatus.Failed })
        {
            return null;
        }
        if (redistributableMissing)
        {
            return $"Pounce's search engine needs the {Redistributable}. Install it from Microsoft, then start Pounce again.";
        }
        return state.Description;
    }

    /// <summary>
    /// The disabled menu line about the shortcut: none could be registered, or a fallback is used because the
    /// preferred one is taken ("Alt+Space is taken — using Ctrl+Alt+Space"). Null when the preferred one works.
    /// </summary>
    /// <param name="preferred">The shortcut asked for (<c>POUNCE_HOTKEY</c>, or Alt+Space).</param>
    /// <param name="registered">The shortcut that was registered; null when none.</param>
    public static string? HotKeyNote(HotKeySpec preferred, HotKeySpec? registered)
    {
        if (registered is not { } hotKey)
        {
            return NoHotKey;
        }
        return hotKey == preferred ? null : $"{preferred.DisplayName} is taken — using {hotKey.DisplayName}";
    }
}
