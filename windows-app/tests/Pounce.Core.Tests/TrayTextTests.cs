namespace Pounce.Core.Tests;

/// <summary>What the tray icon says about the Engine and the shortcut.</summary>
public class TrayTextTests
{
    private static readonly HotKeySpec AltSpace = HotKeySpec.Default;
    private static readonly HotKeySpec CtrlAltSpace = new(HotKeyModifiers.Control | HotKeyModifiers.Alt, HotKeyKey.Space);

    [Fact]
    public void Without_a_supervisor_the_engine_was_started_by_hand()
    {
        Assert.Equal("Search engine started by hand", TrayText.Status(null, redistributableMissing: true));
    }

    [Fact]
    public void Otherwise_the_status_is_the_supervisors_description()
    {
        Assert.Equal("Search engine running", TrayText.Status(EngineState.Running, redistributableMissing: false));
        Assert.Equal("Search engine running", TrayText.Status(EngineState.Reusing, redistributableMissing: true));
        Assert.Equal("Search engine stopped", TrayText.Status(EngineState.Stopped, redistributableMissing: true));
        Assert.Equal("Pounce's search engine stopped unexpectedly (exit code 1).", TrayText.Status(
            EngineState.Failed("Pounce's search engine stopped unexpectedly (exit code 1)."), redistributableMissing: false));
    }

    [Fact]
    public void A_failure_without_the_visual_cpp_runtime_names_what_to_install()
    {
        EngineState failed = EngineState.Failed("Pounce's search engine stopped unexpectedly (exit code 1).");

        Assert.Equal("Pounce needs the Microsoft Visual C++ Redistributable (x64)", TrayText.Status(failed, redistributableMissing: true));
        string? notice = TrayText.FailureNotice(failed, redistributableMissing: true);
        Assert.NotNull(notice);
        Assert.Contains("Microsoft Visual C++ Redistributable (x64)", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void The_notice_is_the_problem_and_only_for_a_failure()
    {
        EngineState failed = EngineState.Failed("Pounce couldn't start its search engine: file not found");

        Assert.Equal("Pounce couldn't start its search engine: file not found", TrayText.FailureNotice(failed, redistributableMissing: false));
        Assert.Null(TrayText.FailureNotice(EngineState.Running, redistributableMissing: true));
        Assert.Null(TrayText.FailureNotice(EngineState.Stopped, redistributableMissing: true));
        Assert.Null(TrayText.FailureNotice(null, redistributableMissing: true));
    }

    [Fact]
    public void The_tooltip_names_the_shortcut_when_there_is_one()
    {
        Assert.Equal("Pounce (Alt+Space)", TrayText.ToolTip(AltSpace));
        Assert.Equal("Pounce (Ctrl+Alt+Space)", TrayText.ToolTip(CtrlAltSpace));
        Assert.Equal("Pounce", TrayText.ToolTip(null));
    }

    [Fact]
    public void No_note_when_the_preferred_shortcut_is_registered()
    {
        Assert.Null(TrayText.HotKeyNote(AltSpace, AltSpace));
        Assert.Null(TrayText.HotKeyNote(CtrlAltSpace, CtrlAltSpace));
    }

    [Fact]
    public void A_fallback_in_use_is_named()
    {
        Assert.Equal("Alt+Space is taken — using Ctrl+Alt+Space", TrayText.HotKeyNote(AltSpace, CtrlAltSpace));
    }

    [Fact]
    public void Without_any_shortcut_the_icon_is_the_way_in()
    {
        Assert.Equal("No free keyboard shortcut — open from this icon", TrayText.HotKeyNote(AltSpace, null));
    }
}
