namespace Pounce.Core.Tests;

public class PanelPlacementTests
{
    [Fact]
    public void The_panel_is_centred_with_its_top_a_quarter_down_the_work_area()
    {
        // 1920×1080 at 100 %, taskbar at the bottom.
        var work = new PixelRect(0, 0, 1920, 1032);
        int width = PanelPlacement.ToPixels(PanelPlacement.WidthDip, 96);

        Assert.Equal(680, width);
        Assert.Equal((620, 258), PanelPlacement.Origin(work, width));
    }

    [Fact]
    public void A_monitor_left_of_and_above_the_primary_has_negative_coordinates()
    {
        // 2560×1440 at 150 %, left of the primary monitor and 200 px higher.
        var work = new PixelRect(-2560, -200, 0, 1192);
        int width = PanelPlacement.ToPixels(PanelPlacement.WidthDip, 144);

        Assert.Equal(1020, width);
        Assert.Equal((-1790, 148), PanelPlacement.Origin(work, width));
    }

    [Fact]
    public void A_work_area_narrower_than_the_panel_puts_it_at_the_left_edge()
    {
        var work = new PixelRect(100, 0, 600, 800);

        Assert.Equal((100, 200), PanelPlacement.Origin(work, 680));
    }

    [Theory]
    [InlineData(680, 96, 680)]
    [InlineData(680, 120, 850)]
    [InlineData(680, 144, 1020)]
    [InlineData(56, 168, 98)]
    [InlineData(56, 0, 56)]
    public void Dips_become_pixels_at_the_monitors_dpi(double dip, double dpi, int expected)
    {
        Assert.Equal(expected, PanelPlacement.ToPixels(dip, dpi));
    }

    [Fact]
    public void The_results_area_ends_above_the_bottom_of_the_work_area()
    {
        // 1366×768 laptop at 100 %: 728 px of work area, top edge at 182.
        var work = new PixelRect(0, 0, 1366, 728);

        double max = PanelPlacement.MaxResultsHeightDip(work, 182, 96);

        Assert.Equal(728 - 182 - 56 - 1 - PanelPlacement.BottomMarginDip, max);
    }

    [Fact]
    public void The_results_area_is_measured_in_the_monitors_dips()
    {
        var work = new PixelRect(0, 0, 2880, 1800);

        double max = PanelPlacement.MaxResultsHeightDip(work, 450, 192);

        Assert.Equal((1800 - 450) / 2.0 - 56 - 1 - PanelPlacement.BottomMarginDip, max);
    }

    [Fact]
    public void A_tiny_work_area_still_leaves_room_for_some_results()
    {
        var work = new PixelRect(0, 0, 800, 200);

        Assert.Equal(PanelPlacement.MinResultsHeightDip, PanelPlacement.MaxResultsHeightDip(work, 50, 96));
    }

    [Fact]
    public void While_a_search_runs_the_panel_grows_but_does_not_shrink()
    {
        // Early Results with two rows after Final Results with eight: the height stays.
        Assert.Equal(600, PanelPlacement.WindowHeightDip(600, 180, searching: true));
        // More rows than before: it grows.
        Assert.Equal(700, PanelPlacement.WindowHeightDip(600, 700, searching: true));
        // Nothing below the search bar yet: it stays too.
        Assert.Equal(600, PanelPlacement.WindowHeightDip(600, PanelPlacement.BarHeightDip, searching: true));
    }

    [Fact]
    public void Once_the_search_is_final_the_panel_fits_its_content()
    {
        Assert.Equal(180, PanelPlacement.WindowHeightDip(600, 180, searching: false));
        Assert.Equal(700, PanelPlacement.WindowHeightDip(600, 700, searching: false));
        // A cleared Query: back to the search bar.
        Assert.Equal(PanelPlacement.BarHeightDip, PanelPlacement.WindowHeightDip(600, PanelPlacement.BarHeightDip, searching: false));
    }

    [Theory]
    [InlineData(600, 0, false)]
    [InlineData(600, -5, false)]
    [InlineData(0, 0, true)]
    [InlineData(double.NaN, 20, true)]
    [InlineData(double.NaN, double.NaN, false)]
    [InlineData(40, double.PositiveInfinity, true)]
    public void The_panel_is_never_shorter_than_the_search_bar_nor_unsized(double current, double content, bool searching)
    {
        double height = PanelPlacement.WindowHeightDip(current, content, searching);

        Assert.True(double.IsFinite(height));
        Assert.True(height >= PanelPlacement.BarHeightDip);
    }
}

public class RepeatGuardTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Repeats_within_the_interval_of_the_first_press_are_dropped()
    {
        var guard = new RepeatGuard(Ms(350));

        Assert.True(guard.Accept(Ms(1000)));
        Assert.False(guard.Accept(Ms(1030)));
        Assert.False(guard.Accept(Ms(1349)));
        Assert.True(guard.Accept(Ms(1350)));
    }

    [Fact]
    public void A_dropped_repeat_does_not_extend_the_interval()
    {
        var guard = new RepeatGuard(Ms(350));

        Assert.True(guard.Accept(Ms(0)));
        Assert.False(guard.Accept(Ms(300)));
        Assert.True(guard.Accept(Ms(400)));
    }

    [Fact]
    public void A_clock_that_goes_backwards_does_not_block_the_next_press()
    {
        var guard = new RepeatGuard(Ms(350));

        Assert.True(guard.Accept(Ms(5000)));
        Assert.True(guard.Accept(Ms(100)));
    }
}

public class PanelFocusPolicyTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Switching_to_another_program_hides_the_panel()
    {
        Assert.Equal(FocusLossAction.Hide, PanelFocusPolicy.Decide(true, false, Ms(5000), ForegroundOwner.OtherProcess));
        Assert.Equal(FocusLossAction.Hide, PanelFocusPolicy.Decide(true, false, Ms(10), ForegroundOwner.OtherProcess));
    }

    [Theory]
    [InlineData(ForegroundOwner.ThisProcess)]
    [InlineData(ForegroundOwner.None)]
    public void Losing_the_focus_to_pounce_itself_right_after_showing_takes_it_back(ForegroundOwner owner)
    {
        Assert.Equal(FocusLossAction.Reactivate, PanelFocusPolicy.Decide(true, false, Ms(100), owner));
    }

    [Theory]
    [InlineData(ForegroundOwner.ThisProcess)]
    [InlineData(ForegroundOwner.None)]
    public void Later_any_focus_loss_hides_the_panel(ForegroundOwner owner)
    {
        Assert.Equal(FocusLossAction.Hide, PanelFocusPolicy.Decide(true, false, Ms(2000), owner));
    }

    [Fact]
    public void A_hidden_or_active_panel_is_left_alone()
    {
        Assert.Equal(FocusLossAction.Ignore, PanelFocusPolicy.Decide(false, false, Ms(10), ForegroundOwner.OtherProcess));
        Assert.Equal(FocusLossAction.Ignore, PanelFocusPolicy.Decide(true, true, Ms(10), ForegroundOwner.OtherProcess));
    }
}

public class PanelSectionsTests
{
    [Fact]
    public void Apps_and_files_each_get_a_title()
    {
        Assert.Equal([PanelSections.Applications, null, PanelSections.Files, null],
            Enumerable.Range(0, 4).Select(i => PanelSections.TitleAbove(i, 2)));
    }

    [Fact]
    public void Files_alone_get_no_title()
    {
        Assert.Equal([null, null], Enumerable.Range(0, 2).Select(i => PanelSections.TitleAbove(i, 0)));
    }

    [Fact]
    public void Apps_alone_get_the_applications_title_only()
    {
        Assert.Equal([PanelSections.Applications, null], Enumerable.Range(0, 2).Select(i => PanelSections.TitleAbove(i, 2)));
    }
}

public class WindowsThemeTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(null, true)]
    [InlineData("0", true)]
    public void The_app_theme_is_light_unless_the_value_says_dark(object? value, bool light)
    {
        Assert.Equal(light, WindowsTheme.IsLight(value));
    }

    [Fact]
    public void The_accent_colour_is_stored_with_red_in_the_lowest_byte()
    {
        // Windows' default blue, #0078D4, as the registry holds it: 0xFFD47800.
        Assert.Equal(((byte)0x00, (byte)0x78, (byte)0xD4), WindowsTheme.AccentFromAbgr(unchecked((int)0xFFD47800)));
    }

    [Fact]
    public void A_missing_accent_colour_is_null()
    {
        Assert.Null(WindowsTheme.AccentFromAbgr(null));
        Assert.Null(WindowsTheme.AccentFromAbgr("blue"));
    }
}
