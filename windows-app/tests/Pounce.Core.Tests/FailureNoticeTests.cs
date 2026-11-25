namespace Pounce.Core.Tests;

/// <summary>What the Launcher says when it cannot go on, and when it may not say it in a dialog.</summary>
public class FailureNoticeTests
{
    [Fact]
    public void A_failed_start_says_so_with_the_problem_and_where_the_log_is()
    {
        string text = FailureNotice.CouldNotStart("Access to the path is denied.", @"C:\Users\ada\AppData\Local\Pounce\logs\launcher.log");

        Assert.Equal(
            "Pounce couldn't start.\n\nAccess to the path is denied.\n\nThe details are in the log:\nC:\\Users\\ada\\AppData\\Local\\Pounce\\logs\\launcher.log",
            text);
    }

    [Fact]
    public void A_fatal_failure_says_pounce_has_to_close()
    {
        string text = FailureNotice.MustClose("Layout measurement override returned NaN.", @"C:\Pounce\logs\launcher.log");

        Assert.StartsWith("Pounce ran into a problem and has to close.\n\nLayout measurement override returned NaN.", text);
        Assert.EndsWith(@"C:\Pounce\logs\launcher.log", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_a_problem_text_only_the_headline_and_the_log_are_shown(string? problem)
    {
        Assert.Equal("Pounce couldn't start.\n\nThe details are in the log:\nC:\\l.log", FailureNotice.CouldNotStart(problem, @"C:\l.log"));
    }

    [Fact]
    public void Without_a_log_the_text_says_there_is_none()
    {
        Assert.EndsWith("No log could be written.", FailureNotice.CouldNotStart("boom", null));
    }

    [Fact]
    public void A_very_long_problem_is_cut_off()
    {
        string text = FailureNotice.CouldNotStart(new string('x', 5000), @"C:\l.log");

        Assert.Contains(new string('x', FailureNotice.MaxProblemLength) + "…", text);
        Assert.DoesNotContain(new string('x', FailureNotice.MaxProblemLength + 1), text);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "1", false)]
    [InlineData(true, "0", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "1", false)]
    public void Dialogs_need_an_interactive_session_and_no_opt_out(bool interactive, string? noDialogs, bool expected)
    {
        Assert.Equal(expected, FailureNotice.MayShowDialog(interactive, noDialogs));
    }
}
