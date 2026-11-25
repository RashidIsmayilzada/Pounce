namespace Pounce.Core.Tests;

/// <summary>Apps come from the shortcuts in the Start Menu's Programs folders.</summary>
public sealed class StartMenuAppSourceTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Lists_shortcuts_in_the_folders_and_all_their_subfolders()
    {
        string user = _temp.Folder("user/Programs");
        _temp.File("user/Programs/Slack.lnk");
        _temp.File("user/Programs/Visual Studio Code/Visual Studio Code.lnk");
        _temp.File("user/Programs/Tools/Deep/Deeper/Paint.NET.lnk");
        _temp.File("user/Programs/Notes.txt");
        _temp.File("user/Programs/Website.url");
        _temp.File("user/Programs/desktop.ini");

        IReadOnlyList<AppEntry> apps = new StartMenuAppSource([user]).ListApps();

        Assert.Equal(["Paint.NET", "Slack", "Visual Studio Code"], apps.Select(app => app.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Path.Combine(user, "Slack.lnk"), apps.Single(app => app.Name == "Slack").Path);
    }

    [Fact]
    public void The_extension_matches_in_any_case()
    {
        string user = _temp.Folder("Programs");
        _temp.File("Programs/Shouting.LNK");

        Assert.Equal(["Shouting"], new StartMenuAppSource([user]).ListApps().Select(app => app.Name));
    }

    [Fact]
    public void A_name_in_both_folders_is_listed_once_from_the_first_folder_ignoring_case()
    {
        string user = _temp.Folder("user");
        string allUsers = _temp.Folder("all");
        string mine = _temp.File("user/Firefox.lnk");
        _temp.File("all/firefox.lnk");
        _temp.File("all/Mozilla/FIREFOX.lnk");
        _temp.File("all/Excel.lnk");

        IReadOnlyList<AppEntry> apps = new StartMenuAppSource([user, allUsers]).ListApps();

        Assert.Equal(["Excel", "Firefox"], apps.Select(app => app.Name).Order(StringComparer.Ordinal));
        Assert.Equal(mine, apps.Single(app => app.Name == "Firefox").Path);
    }

    [Fact]
    public void Shortcuts_that_are_not_apps_are_left_out()
    {
        string user = _temp.Folder("Programs");
        foreach (string name in new[]
        {
            "Uninstall Foo", "Foo Uninstaller", "Foo Help", "Help", "Read Me", "README", "Foo ReadMe First", "License",
            "Foo Licence Agreement", "Release Notes", "Foo Website", "Visit the Foo Web Site", "Foo",
        })
        {
            _temp.File($"Programs/Foo/{name}.lnk");
        }

        Assert.Equal(["Foo"], new StartMenuAppSource([user]).ListApps().Select(app => app.Name));
    }

    [Theory]
    [InlineData("Helper Tool", true)]
    [InlineData("Help Viewer", true)]
    [InlineData("Microsoft Edge", true)]
    [InlineData("Python 3.12 Module Docs", true)]
    [InlineData("HTML Help", false)]
    [InlineData("Désinstaller Uninstall", false)]
    public void Only_the_documented_words_mark_a_shortcut_as_not_an_app(string name, bool isApp)
    {
        Assert.Equal(isApp, StartMenuAppSource.LooksLikeApp(name));
    }

    [Fact]
    public void Missing_folders_add_no_apps()
    {
        string user = _temp.Folder("Programs");
        _temp.File("Programs/Slack.lnk");

        IReadOnlyList<AppEntry> apps = new StartMenuAppSource([Path.Combine(_temp.Root, "nowhere"), user]).ListApps();

        Assert.Equal(["Slack"], apps.Select(app => app.Name));
    }

    [Fact]
    public void No_apps_lists_nothing()
    {
        Assert.Empty(new NoApps().ListApps());
    }
}
