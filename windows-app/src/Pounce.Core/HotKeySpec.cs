namespace Pounce.Core;

/// <summary>The modifier keys of a shortcut. The values are Win32's <c>MOD_*</c> flags, so they can be passed to <c>RegisterHotKey</c> as they are.</summary>
[Flags]
public enum HotKeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Alt (<c>MOD_ALT</c>).</summary>
    Alt = 0x1,

    /// <summary>Ctrl (<c>MOD_CONTROL</c>).</summary>
    Control = 0x2,

    /// <summary>Shift (<c>MOD_SHIFT</c>).</summary>
    Shift = 0x4,

    /// <summary>The Windows key (<c>MOD_WIN</c>).</summary>
    Win = 0x8,
}

/// <summary>The key of a shortcut. The values are Win32 virtual-key codes.</summary>
public enum HotKeyKey
{
    /// <summary>The space bar (<c>VK_SPACE</c>).</summary>
    Space = 0x20,
}

/// <summary>
/// A system-wide shortcut, as plain data. Named like <c>alt-space</c> (the default), <c>control-space</c>,
/// <c>control-alt-space</c>, <c>alt-shift-space</c> or <c>win-alt-space</c>, as in <c>POUNCE_HOTKEY</c>.
/// </summary>
/// <param name="Modifiers">The modifier keys held down.</param>
/// <param name="Key">The key pressed.</param>
public readonly record struct HotKeySpec(HotKeyModifiers Modifiers, HotKeyKey Key)
{
    /// <summary>Alt+Space, the default.</summary>
    public static HotKeySpec Default { get; } = new(HotKeyModifiers.Alt, HotKeyKey.Space);

    /// <summary>What to try, in order, when the preferred shortcut is taken by another program: Alt+Space, Ctrl+Alt+Space, Alt+Shift+Space.</summary>
    public static IReadOnlyList<HotKeySpec> Fallbacks { get; } =
    [
        Default,
        new(HotKeyModifiers.Control | HotKeyModifiers.Alt, HotKeyKey.Space),
        new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, HotKeyKey.Space),
    ];

    /// <summary>The name, such as <c>control-alt-space</c>: modifiers in the order win, control, alt, shift.</summary>
    public string Name => Join('-', "win", "control", "alt", "shift", "space");

    /// <summary>The name for menus, such as "Ctrl+Alt+Space".</summary>
    public string DisplayName => Join('+', "Win", "Ctrl", "Alt", "Shift", "Space");

    /// <summary>
    /// The shortcut named <paramref name="name"/>, or <see cref="Default"/> when it is null, blank or unknown.
    /// </summary>
    public static HotKeySpec Parse(string? name) => TryParse(name, out HotKeySpec spec) ? spec : Default;

    /// <summary>
    /// Reads a name such as <c>control-alt-space</c>: modifiers in any order, then the key, separated by <c>-</c> or
    /// <c>+</c>, in any case. <c>ctrl</c> means control and <c>option</c> means alt. At least one modifier is needed.
    /// </summary>
    public static bool TryParse(string? name, out HotKeySpec spec)
    {
        spec = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        string[] parts = name.Trim().ToLowerInvariant().Split('-', '+');
        if (parts[^1].Trim() != "space")
        {
            return false;
        }
        HotKeyModifiers modifiers = HotKeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            HotKeyModifiers? modifier = part.Trim() switch
            {
                "alt" or "option" or "opt" => HotKeyModifiers.Alt,
                "control" or "ctrl" or "ctl" => HotKeyModifiers.Control,
                "shift" => HotKeyModifiers.Shift,
                "win" or "windows" => HotKeyModifiers.Win,
                _ => null,
            };
            if (modifier is null)
            {
                return false;
            }
            modifiers |= modifier.Value;
        }
        if (modifiers == HotKeyModifiers.None)
        {
            return false;
        }
        spec = new HotKeySpec(modifiers, HotKeyKey.Space);
        return true;
    }

    /// <summary>This shortcut first, then the <see cref="Fallbacks"/> that differ from it: the order to try registering them.</summary>
    public IReadOnlyList<HotKeySpec> WithFallbacks()
    {
        HotKeySpec self = this;
        return [self, .. Fallbacks.Where(fallback => fallback != self)];
    }

    /// <summary>The <see cref="Name"/>.</summary>
    public override string ToString() => Name;

    private string Join(char separator, string win, string control, string alt, string shift, string space)
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            parts.Add(win);
        }
        if (Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            parts.Add(control);
        }
        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            parts.Add(alt);
        }
        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            parts.Add(shift);
        }
        parts.Add(Key == HotKeyKey.Space ? space : Key.ToString());
        return string.Join(separator, parts);
    }
}
