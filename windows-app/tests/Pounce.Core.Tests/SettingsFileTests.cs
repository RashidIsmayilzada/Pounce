namespace Pounce.Core.Tests;

/// <summary>The pounce.yml written on first run lists the user's real folders, in a form the Engine binds.</summary>
public class SettingsFileTests
{
    [Fact]
    public void Lists_the_folders_as_single_quoted_yaml_under_pounce_watched_folders()
    {
        string text = SettingsFile.Create([@"C:\Users\me\OneDrive - Contoso\Documents", @"C:\Users\me\Downloads"]);

        string[] lines = text.Split("\r\n");
        int start = Array.IndexOf(lines, "pounce:");
        Assert.True(start > 0, text);
        Assert.Equal(
            ["pounce:", "  watched-folders:", @"    - 'C:\Users\me\OneDrive - Contoso\Documents'", @"    - 'C:\Users\me\Downloads'", ""],
            lines[start..]);
    }

    [Fact]
    public void Starts_with_a_comment_that_says_what_the_file_is_and_how_to_apply_changes()
    {
        string text = SettingsFile.Create([@"C:\Users\me\Documents"]);

        string[] header = text.Split("\r\n").TakeWhile(line => line.Length > 0).ToArray();
        Assert.NotEmpty(header);
        Assert.All(header, line => Assert.StartsWith("#", line, StringComparison.Ordinal));
        string comment = string.Join(' ', header);
        Assert.Contains("Pounce settings", comment, StringComparison.Ordinal);
        Assert.Contains("quit Pounce", comment, StringComparison.Ordinal);
        Assert.Contains("start it again", comment, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_windows_line_ends_only()
    {
        string text = SettingsFile.Create([@"C:\a"]);

        Assert.Equal(text.Split('\n').Length - 1, text.Split("\r\n").Length - 1);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\Users\me\Documents", @"'C:\Users\me\Documents'")]
    [InlineData(@"C:\Users\O'Brien\Desktop", @"'C:\Users\O''Brien\Desktop'")]
    [InlineData(@"D:\'quoted'", @"'D:\''quoted'''")]
    [InlineData(@"C:\# not a comment: really", @"'C:\# not a comment: really'")]
    [InlineData(@"C:\trailing space ", @"'C:\trailing space '")]
    [InlineData("", "''")]
    public void Quotes_paths_literally_with_apostrophes_doubled(string path, string quoted)
    {
        Assert.Equal(quoted, SettingsFile.Quote(path));
    }

    [Fact]
    public void Without_folders_the_list_is_only_an_example_so_the_engine_keeps_its_defaults()
    {
        string text = SettingsFile.Create([]);

        Assert.All(text.Split("\r\n").Where(line => line.Length > 0),
            line => Assert.StartsWith("#", line, StringComparison.Ordinal));
        Assert.Contains("#   watched-folders:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_folders_that_exist_once_each_in_order()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Users\me\OneDrive\Documents", @"C:\Users\me\Desktop", @"C:\Users\me\Downloads",
        };

        IReadOnlyList<string> folders = SettingsFile.ExistingOnce(
        [
            @"C:\Users\me\OneDrive\Documents",
            null,
            "  ",
            @"C:\Users\me\Desktop",
            @"c:\users\me\onedrive\documents\",
            @"C:\Users\me\Missing",
            @"C:\Users\me\Downloads",
            @"C:\Users\me\Downloads",
        ], folder => existing.Contains(folder.TrimEnd('\\')));

        Assert.Equal([@"C:\Users\me\OneDrive\Documents", @"C:\Users\me\Desktop", @"C:\Users\me\Downloads"], folders);
    }

    [Fact]
    public void A_folder_that_exists_is_asked_about_at_most_once()
    {
        int asked = 0;

        SettingsFile.ExistingOnce([@"C:\a", @"C:\A\", @"C:\a"], _ =>
        {
            asked++;
            return true;
        });

        Assert.Equal(1, asked);
    }
}
