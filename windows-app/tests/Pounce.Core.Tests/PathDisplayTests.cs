namespace Pounce.Core.Tests;

public class PathDisplayTests
{
    [Theory]
    [InlineData(@"C:\Users\me\Documents\a\b.md", @"C:\Users\me", @"Documents\a\b.md")]
    [InlineData(@"c:\users\ME\Documents\b.md", @"C:\Users\me", @"Documents\b.md")]
    [InlineData(@"C:\Users\me\b.md", @"C:\Users\me\", @"b.md")]
    [InlineData(@"C:/Users/me/Documents/b.md", @"C:\Users\me", @"Documents/b.md")]
    [InlineData(@"C:\Users\me\Documents\b.md", @"C:/Users/me/", @"Documents\b.md")]
    [InlineData(@"\\server\share\me\b.md", @"\\server\share\me", @"b.md")]
    [InlineData(@"C:\Users\Émile\b.md", @"C:\Users\émile", @"b.md")]
    public void A_path_inside_the_profile_is_shown_relative_to_it(string path, string profile, string expected)
    {
        Assert.Equal(expected, PathDisplay.Abbreviate(path, profile));
    }

    [Theory]
    [InlineData(@"C:\Users\meg\b.md", @"C:\Users\me")]
    [InlineData(@"D:\Projects\b.md", @"C:\Users\me")]
    [InlineData(@"C:\Users\me", @"C:\Users\me")]
    [InlineData(@"C:\Users\me\", @"C:\Users\me")]
    [InlineData(@"C:\Users", @"C:\Users\me")]
    [InlineData(@"C:\Users\me\b.md", "")]
    [InlineData(@"C:\Users\me\b.md", null)]
    public void Anything_else_is_shown_in_full(string path, string? profile)
    {
        Assert.Equal(path, PathDisplay.Abbreviate(path, profile));
    }
}
