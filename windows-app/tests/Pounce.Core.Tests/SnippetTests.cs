namespace Pounce.Core.Tests;

public class SnippetTests
{
    [Fact]
    public void Highlight_offsets_count_utf16_units_like_the_java_engine()
    {
        // "🚀" is two UTF-16 units, so "canary" starts at offset 3 in Java, and in C# too.
        var snippet = new Snippet("🚀 canary rollout", [new Highlight(3, 9)]);

        Assert.Equal(["🚀 ", "canary", " rollout"], snippet.Runs.Select(r => r.Text));
        Assert.Equal([false, true, false], snippet.Runs.Select(r => r.Highlighted));
    }

    [Fact]
    public void Offsets_outside_the_text_are_ignored_instead_of_crashing()
    {
        var snippet = new Snippet("short", [new Highlight(3, 99), new Highlight(40, 50)]);

        Assert.Equal(["sho", "rt"], snippet.Runs.Select(r => r.Text));
    }

    [Fact]
    public void Highlights_in_any_order_or_overlapping_give_runs_in_text_order()
    {
        var snippet = new Snippet("one two three", [new Highlight(8, 13), new Highlight(0, 3), new Highlight(2, 5), new Highlight(-4, -1)]);

        Assert.Equal(
            [new SnippetRun("one", true), new SnippetRun(" t", true), new SnippetRun("wo ", false), new SnippetRun("three", true)],
            snippet.Runs);
    }

    [Fact]
    public void A_snippet_without_highlights_is_one_plain_run_and_an_empty_one_has_none()
    {
        Assert.Equal([new SnippetRun("plain", false)], new Snippet("plain", []).Runs);
        Assert.Empty(Snippet.Empty.Runs);
    }

    [Fact]
    public void Results_and_phases_compare_by_value()
    {
        var a = new ResultItem("/n/a.md", "a.md", new Snippet("x", [new Highlight(0, 1)]));
        var same = new ResultItem("/n/a.md", "a.md", new Snippet("x", [new Highlight(0, 1)]));

        Assert.Equal(a, same);
        Assert.Equal(a.GetHashCode(), same.GetHashCode());
        Assert.Equal(SearchPhase.Final([a]), SearchPhase.Final([same]));
        Assert.NotEqual(SearchPhase.Early([a]), SearchPhase.Final([a]));
        Assert.Equal("/n/a.md", a.Id);
    }
}

public class SnippetTextTests
{
    [Theory]
    [InlineData("a    b", "a b")]
    [InlineData("col1\t\t col2", "col1 col2")]
    [InlineData("line\r\nnext", "line next")]
    [InlineData("  padded  ", " padded ")]
    [InlineData("tab\there", "tab here")]
    [InlineData("no break", "no break")]
    [InlineData("", "")]
    public void Every_run_of_whitespace_becomes_one_space(string text, string expected)
    {
        Assert.Equal(expected, SnippetText.Clean(text));
    }

    [Fact]
    public void Text_that_needs_no_change_is_returned_as_is()
    {
        string text = "already clean text";

        Assert.Same(text, SnippetText.Clean(text));
    }
}
