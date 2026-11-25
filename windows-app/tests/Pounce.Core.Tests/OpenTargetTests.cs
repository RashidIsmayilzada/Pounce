namespace Pounce.Core.Tests;

/// <summary>Markdown inside an Obsidian vault opens in Obsidian; everything else opens in its default app.</summary>
public sealed class OpenTargetTests : IDisposable
{
    private readonly TempFolder _root = new();

    public OpenTargetTests()
    {
        _root.Folder("Vault/.obsidian");
        _root.Folder("Vault/wiki/my notes");
        _root.Folder("Elsewhere");
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public void Markdown_inside_a_vault_opens_through_obsidian()
    {
        string note = Path.Combine(_root.Root, "Vault", "wiki", "my notes", "TLS renewal.md");

        OpenTarget target = OpenTarget.For(note, obsidianInstalled: true);

        Assert.Equal(OpenTargetKind.ObsidianLink, target.Kind);
        var link = new Uri(target.Target);
        Assert.Equal("obsidian", link.Scheme);
        Assert.Equal("open", link.Host);
        Assert.StartsWith("?path=", link.Query, StringComparison.Ordinal);
        Assert.Equal(note, Uri.UnescapeDataString(link.Query["?path=".Length..]));
        Assert.DoesNotContain(' ', target.Target);
    }

    [Fact]
    public void Every_character_of_the_path_is_percent_encoded()
    {
        string note = Path.Combine(_root.Root, "Vault", "a&b=c #1.MARKDOWN");

        OpenTarget target = OpenTarget.For(note, obsidianInstalled: true);

        Assert.Equal("obsidian://open?path=" + Uri.EscapeDataString(note), target.Target);
        Assert.DoesNotContain('&', target.Target["obsidian://open?path=".Length..]);
    }

    [Fact]
    public void Markdown_outside_any_vault_opens_in_the_default_app()
    {
        string note = Path.Combine(_root.Root, "Elsewhere", "readme.md");

        Assert.Equal(new OpenTarget(OpenTargetKind.File, note), OpenTarget.For(note, obsidianInstalled: true));
    }

    [Fact]
    public void Other_files_inside_a_vault_open_in_the_default_app()
    {
        string pdf = Path.Combine(_root.Root, "Vault", "wiki", "paper.pdf");

        Assert.Equal(new OpenTarget(OpenTargetKind.File, pdf), OpenTarget.For(pdf, obsidianInstalled: true));
    }

    [Fact]
    public void Without_obsidian_installed_markdown_opens_in_the_default_app()
    {
        string note = Path.Combine(_root.Root, "Vault", "wiki", "note.md");

        Assert.Equal(new OpenTarget(OpenTargetKind.File, note), OpenTarget.For(note, obsidianInstalled: false));
    }

    [Fact]
    public void A_bare_file_name_is_opened_as_a_file()
    {
        Assert.Equal(new OpenTarget(OpenTargetKind.File, "note.md"), OpenTarget.For("note.md", obsidianInstalled: true));
    }
}
