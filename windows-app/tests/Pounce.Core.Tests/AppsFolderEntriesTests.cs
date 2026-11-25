namespace Pounce.Core.Tests;

/// <summary>Turning the items of the shell's Apps folder into apps.</summary>
public class AppsFolderEntriesTests
{
    [Fact]
    public void Each_item_becomes_an_app_launched_through_the_apps_folder()
    {
        IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps(
        [
            ("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            ("7-Zip File Manager", @"{6D809377-6AF0-444B-8957-A3773F02200E}\7-Zip\7zFM.exe"),
        ]);

        Assert.Equal(
            [
                new AppEntry("Calculator", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
                new AppEntry("7-Zip File Manager", @"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\7-Zip\7zFM.exe"),
            ],
            apps);
    }

    [Fact]
    public void A_name_seen_before_is_skipped_ignoring_case()
    {
        IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps(
        [
            ("Firefox", "308046B0AF4A39CB"),
            ("firefox", @"{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}\Mozilla Firefox\firefox.exe"),
            ("Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"),
        ]);

        Assert.Equal(["Firefox", "Terminal"], apps.Select(app => app.Name));
        Assert.Equal(@"shell:AppsFolder\308046B0AF4A39CB", apps[0].Path);
    }

    [Fact]
    public void Uninstallers_help_and_web_links_are_left_out()
    {
        IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps(
        [
            ("Uninstall Foo", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\uninst.exe"),
            ("Foo Help", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\help.exe"),
            ("Foo Website", "https://foo.example/"),
            ("Foo Home Page", "https://foo.example/home"),
            ("Foo Manual", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\Manual.CHM"),
            ("Foo Changes", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\changes.txt"),
            ("Foo Guide", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\guide.pdf"),
            ("Foo", @"{6D809377-6AF0-444B-8957-A3773F02200E}\Foo\foo.exe"),
            ("Office Word", "Microsoft.Office.WINWORD.EXE.15"),
        ]);

        Assert.Equal(["Foo", "Office Word"], apps.Select(app => app.Name));
    }

    [Fact]
    public void A_dotted_app_id_without_a_folder_is_not_mistaken_for_a_document()
    {
        IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps([("Reader", "Contoso.Reader.pdf")]);

        Assert.Equal(["Reader"], apps.Select(app => app.Name));
    }

    [Fact]
    public void Blank_names_and_ids_are_skipped_and_names_are_trimmed()
    {
        IReadOnlyList<AppEntry> apps = AppsFolderEntries.ToApps(
        [
            ("", "Some.App!App"),
            ("   ", "Other.App!App"),
            ("No Id", " "),
            ("  Paint  ", "Microsoft.Paint_8wekyb3d8bbwe!App"),
        ]);

        Assert.Equal([new AppEntry("Paint", @"shell:AppsFolder\Microsoft.Paint_8wekyb3d8bbwe!App")], apps);
    }

    [Fact]
    public void A_folder_that_yields_nothing_gives_no_apps()
    {
        Assert.Empty(AppsFolderEntries.ToApps([]));
    }
}
