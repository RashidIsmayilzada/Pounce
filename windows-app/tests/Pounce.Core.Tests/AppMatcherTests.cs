namespace Pounce.Core.Tests;

/// <summary>The ranking rules on their own; <see cref="AppSearchTests"/> covers them through the search model.</summary>
public class AppMatcherTests
{
    private static readonly AppEntry[] Apps =
        [.. new[] { "Café Notes", "Émulateur", "ÅngströmView", "Ωmega Tool", "Paint", "PowerShell 7 (x64)", "7-Zip File Manager" }
            .Select(name => new AppEntry(name, $@"C:\Start Menu\{name}.lnk"))];

    [Theory]
    [InlineData("cafe", "Café Notes")]
    [InlineData("CAFÉ", "Café Notes")]
    [InlineData("emul", "Émulateur")]
    [InlineData("angstrom", "ÅngströmView")]
    [InlineData("ωmega", "Ωmega Tool")]
    [InlineData("powershell 7", "PowerShell 7 (x64)")]
    [InlineData("zip", "7-Zip File Manager")]
    [InlineData("7zfm", "7-Zip File Manager")]
    public void Case_and_accents_do_not_matter(string query, string expected)
    {
        Assert.Equal([expected], AppMatcher.Rank(query, Apps).Select(app => app.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_query_matches_no_app(string query)
    {
        Assert.Empty(AppMatcher.Rank(query, Apps));
    }

    [Fact]
    public void Anywhere_in_the_name_needs_three_letters_and_ranks_last()
    {
        AppEntry[] apps = [new("Spaint", "a"), new("Paint", "b"), new("Paintbrush", "c")];

        Assert.Equal(["Paint", "Paintbrush", "Spaint"], AppMatcher.Rank("pai", apps).Select(app => app.Name));
        Assert.Equal(["Paint", "Paintbrush"], AppMatcher.Rank("pa", apps).Select(app => app.Name));
    }

    [Fact]
    public void Equal_scores_go_to_the_shorter_name_then_in_ordinal_order()
    {
        AppEntry[] apps = [new("Notes B", "1"), new("Notes A", "2"), new("Notes", "3"), new("Notes Long", "4")];

        Assert.Equal(["Notes", "Notes A", "Notes B"], AppMatcher.Rank("note", apps).Select(app => app.Name));
    }

    [Fact]
    public void Turkish_dotted_capital_i_folds_to_plain_i()
    {
        AppEntry[] apps = [new("İstanbul Maps", "1")];

        Assert.Single(AppMatcher.Rank("istanbul", apps));
    }
}
