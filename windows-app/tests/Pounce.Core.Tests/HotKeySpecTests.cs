namespace Pounce.Core.Tests;

public class HotKeySpecTests
{
    [Theory]
    [InlineData("alt-space", HotKeyModifiers.Alt)]
    [InlineData("control-space", HotKeyModifiers.Control)]
    [InlineData("control-alt-space", HotKeyModifiers.Control | HotKeyModifiers.Alt)]
    [InlineData("alt-shift-space", HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData("win-alt-space", HotKeyModifiers.Win | HotKeyModifiers.Alt)]
    [InlineData("Ctrl-Option-SPACE", HotKeyModifiers.Control | HotKeyModifiers.Alt)]
    [InlineData("option-space", HotKeyModifiers.Alt)]
    [InlineData("  Alt+Shift+Space ", HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    [InlineData("shift-alt-space", HotKeyModifiers.Alt | HotKeyModifiers.Shift)]
    public void Reads_the_shortcut_names_in_any_case_with_aliases(string name, HotKeyModifiers modifiers)
    {
        Assert.True(HotKeySpec.TryParse(name, out HotKeySpec spec));
        Assert.Equal(new HotKeySpec(modifiers, HotKeyKey.Space), spec);
        Assert.Equal(spec, HotKeySpec.Parse(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("space")]
    [InlineData("alt-enter")]
    [InlineData("hyper-space")]
    [InlineData("alt-space-alt")]
    [InlineData("command-option-space")]
    public void Unknown_names_fall_back_to_alt_space(string? name)
    {
        Assert.False(HotKeySpec.TryParse(name, out _));
        Assert.Equal(HotKeySpec.Default, HotKeySpec.Parse(name));
        Assert.Equal(new HotKeySpec(HotKeyModifiers.Alt, HotKeyKey.Space), HotKeySpec.Default);
    }

    [Theory]
    [InlineData("alt-space", "alt-space", "Alt+Space")]
    [InlineData("control-space", "control-space", "Ctrl+Space")]
    [InlineData("alt-control-space", "control-alt-space", "Ctrl+Alt+Space")]
    [InlineData("shift-alt-space", "alt-shift-space", "Alt+Shift+Space")]
    [InlineData("alt-win-space", "win-alt-space", "Win+Alt+Space")]
    public void Names_and_menu_names_are_canonical(string name, string canonical, string display)
    {
        HotKeySpec spec = HotKeySpec.Parse(name);

        Assert.Equal(canonical, spec.Name);
        Assert.Equal(canonical, spec.ToString());
        Assert.Equal(display, spec.DisplayName);
    }

    [Fact]
    public void Fallbacks_are_alt_space_then_control_alt_space_then_alt_shift_space()
    {
        Assert.Equal(["alt-space", "control-alt-space", "alt-shift-space"], HotKeySpec.Fallbacks.Select(spec => spec.Name));
    }

    [Fact]
    public void The_preferred_shortcut_is_tried_first_then_the_other_fallbacks()
    {
        Assert.Equal(["win-alt-space", "alt-space", "control-alt-space", "alt-shift-space"],
            HotKeySpec.Parse("win-alt-space").WithFallbacks().Select(spec => spec.Name));
        Assert.Equal(["control-alt-space", "alt-space", "alt-shift-space"],
            HotKeySpec.Parse("ctrl-alt-space").WithFallbacks().Select(spec => spec.Name));
    }

    [Fact]
    public void Modifier_and_key_values_are_the_win32_ones()
    {
        Assert.Equal(0x1, (int)HotKeyModifiers.Alt);
        Assert.Equal(0x2, (int)HotKeyModifiers.Control);
        Assert.Equal(0x4, (int)HotKeyModifiers.Shift);
        Assert.Equal(0x8, (int)HotKeyModifiers.Win);
        Assert.Equal(0x20, (int)HotKeyKey.Space);
    }
}
