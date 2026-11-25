namespace Pounce.Core.Tests;

/// <summary>Which rows share an icon.</summary>
public class IconKeyTests
{
    private static FileItem File(string path) =>
        new(new ResultItem(path, path[(path.LastIndexOf('\\') + 1)..], Snippet.Empty));

    [Theory]
    [InlineData(@"C:\Notes\Plan.md", "type:.md")]
    [InlineData(@"C:\Notes\Other plan.MD", "type:.md")]
    [InlineData(@"D:\Papers\paper.pdf", "type:.pdf")]
    [InlineData(@"C:\Notes\archive.tar.gz", "type:.gz")]
    [InlineData(@"C:\Code\.gitignore", "type:.gitignore")]
    [InlineData("C:/Mixed/slashes.txt", "type:.txt")]
    public void Ordinary_documents_share_one_icon_per_extension_ignoring_case(string path, string expected)
    {
        Assert.Equal(expected, IconKey.For(File(path)));
    }

    [Theory]
    [InlineData(@"C:\Tools\App.exe")]
    [InlineData(@"C:\Users\me\Desktop\Code.LNK")]
    [InlineData(@"C:\Icons\cat.ico")]
    [InlineData(@"C:\Links\Site.url")]
    [InlineData(@"C:\Apps\Tool.appref-ms")]
    [InlineData(@"C:\Notes\Makefile")]
    [InlineData(@"C:\Notes\trailing.")]
    [InlineData(@"C:\folder.with.dots\README")]
    public void Files_with_their_own_icon_are_keyed_by_their_lower_case_path(string path)
    {
        Assert.Equal("file:" + path.ToLowerInvariant(), IconKey.For(File(path)));
    }

    [Fact]
    public void The_same_individual_file_in_another_case_shares_the_key()
    {
        Assert.Equal(IconKey.For(File(@"C:\Tools\App.exe")), IconKey.For(File(@"c:\tools\APP.EXE")));
    }

    [Fact]
    public void Apps_are_keyed_by_their_path_even_when_it_looks_like_a_document()
    {
        var calculator = new AppItem(new AppEntry("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        var shortcut = new AppItem(new AppEntry("Notes", @"C:\Start Menu\Notes.txt"));

        Assert.Equal(@"app:shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", IconKey.For(calculator));
        Assert.Equal(@"app:C:\Start Menu\Notes.txt", IconKey.For(shortcut));
    }

    [Theory]
    [InlineData(@"C:\a\b.PDF", ".pdf")]
    [InlineData(@"C:\a.b\c", "")]
    [InlineData(@"C:\a\c.", "")]
    [InlineData("C:file.txt", ".txt")]
    [InlineData("", "")]
    [InlineData("noext", "")]
    public void The_extension_is_that_of_the_last_part_of_the_path(string path, string expected)
    {
        Assert.Equal(expected, IconKey.Extension(path));
    }
}
