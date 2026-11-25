using static Pounce.Core.Tests.Waits;

namespace Pounce.Core.Tests;

/// <summary>Apps come first, like Spotlight: matched by name, word starts and initials, at most three, before any files.</summary>
public class AppSearchTests
{
    private readonly FakeEngine _engine = new();
    private readonly FixedApps _apps = new("Safari", "Slack", "Calculator", "Visual Studio Code", "Notes", "Notes Exporter",
        "Sticky Notes", "System Settings", "Spotify", "Screenshot");

    private async Task<SearchModel> Model()
    {
        var model = new SearchModel(_engine, _apps);
        await model.ReloadAppsAsync();
        return model;
    }

    [Fact]
    public void Typing_the_start_of_an_app_name_puts_that_app_first() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();

        model.Query = "saf";

        Assert.Equal(["Safari"], model.Apps.Select(app => app.Name));
        Assert.Equal(new AppItem(_apps.Named("Safari")), model.Selected);
    });

    [Fact]
    public void The_start_of_any_word_and_initials_also_match() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();

        model.Query = "code";
        Assert.Equal(["Visual Studio Code"], model.Apps.Select(app => app.Name));

        model.Query = "vsc";
        Assert.Equal(["Visual Studio Code"], model.Apps.Select(app => app.Name));
    });

    [Fact]
    public void An_exact_name_beats_a_name_start_which_beats_a_word_start() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();

        model.Query = "notes";

        Assert.Equal(["Notes", "Notes Exporter", "Sticky Notes"], model.Apps.Select(app => app.Name));
    });

    [Fact]
    public void A_single_letter_matches_only_the_start_of_names_and_at_most_three_apps_show() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();

        model.Query = "s";

        Assert.Equal(3, model.Apps.Count);
        Assert.All(model.Apps, app => Assert.StartsWith("s", app.Name, StringComparison.OrdinalIgnoreCase));
    });

    [Fact]
    public void Files_follow_the_apps_and_selection_moves_from_apps_into_files() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();
        model.Query = "notes";
        await _engine.WhenSearching("notes");
        _engine.Send(SearchPhase.Final([Item("meeting-notes.md")]), "notes");
        await model.Until(m => m.IsFinal);

        Assert.Equal(4, model.Items.Count);
        model.MoveSelection(3);
        Assert.Equal(new FileItem(Item("meeting-notes.md")), model.Selected);
    });

    [Fact]
    public void A_matching_app_means_no_no_results_message() => SingleThreadContext.Run(async _ =>
    {
        SearchModel model = await Model();
        model.Query = "spotify";
        await _engine.WhenSearching("spotify");
        _engine.Send(SearchPhase.Final([]), "spotify");
        await model.Until(m => m.IsFinal);

        Assert.Equal(["Spotify"], model.Apps.Select(app => app.Name));
        Assert.Null(model.Message);
    });

    [Fact]
    public void Apps_are_listed_off_the_ui_thread_and_ranked_for_the_query_already_typed() => SingleThreadContext.Run(async ui =>
    {
        var model = new SearchModel(_engine, _apps);
        model.Query = "calc";
        Assert.Empty(model.Apps);

        await model.ReloadAppsAsync();

        Assert.NotEqual(ui.ThreadId, _apps.ListedOnThread);
        Assert.True(ui.IsCurrent);
        Assert.Equal(["Calculator"], model.Apps.Select(app => app.Name));
        Assert.Equal(new AppItem(_apps.Named("Calculator")), model.Items[0]);
    });

    [Fact]
    public void A_reload_keeps_the_selection_on_the_same_app() => SingleThreadContext.Run(async _ =>
    {
        var apps = new ChangingApps("Notes Exporter", "Sticky Notes");
        var model = new SearchModel(_engine, apps);
        await model.ReloadAppsAsync();
        model.Query = "notes";
        model.MoveSelection(1);
        Assert.Equal("Sticky Notes", model.Selected?.Name);

        // A better match was installed meanwhile: it ranks first and pushes the selected app down a row.
        apps.Names = ["Notes", "Notes Exporter", "Sticky Notes"];
        await model.ReloadAppsAsync();

        Assert.Equal(["Notes", "Notes Exporter", "Sticky Notes"], model.Apps.Select(app => app.Name));
        Assert.Equal(2, model.Selection);
        Assert.Equal("Sticky Notes", model.Selected?.Name);
    });

    [Fact]
    public void A_reload_that_removes_the_selected_app_selects_a_row_that_exists() => SingleThreadContext.Run(async _ =>
    {
        var apps = new ChangingApps("Notes", "Notes Exporter", "Sticky Notes");
        var model = new SearchModel(_engine, apps);
        await model.ReloadAppsAsync();
        model.Query = "notes";
        model.MoveSelection(2);
        Assert.Equal("Sticky Notes", model.Selected?.Name);
        var seen = new List<(int Selection, int Count)>();
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchModel.Items))
            {
                seen.Add((model.Selection, model.Items.Count));
            }
        };

        apps.Names = ["Notes"];
        await model.ReloadAppsAsync();

        Assert.Equal(0, model.Selection);
        Assert.Equal("Notes", model.Selected?.Name);
        // Listeners never see the selection point past the rows, not even while the change is announced.
        Assert.Equal([(0, 1)], seen);
    });

    [Fact]
    public void A_reload_that_removes_every_match_leaves_nothing_selected() => SingleThreadContext.Run(async _ =>
    {
        var apps = new ChangingApps("Notes", "Sticky Notes");
        var model = new SearchModel(_engine, apps);
        await model.ReloadAppsAsync();
        model.Query = "notes";
        model.MoveSelection(1);

        apps.Names = ["Calculator"];
        await model.ReloadAppsAsync();

        Assert.Empty(model.Items);
        Assert.Equal(0, model.Selection);
        Assert.Null(model.Selected);
    });

    /// <summary>Apps that a test can install and remove between listings.</summary>
    private sealed class ChangingApps(params string[] names) : IAppSource
    {
        public string[] Names { get; set; } = names;

        public IReadOnlyList<AppEntry> ListApps() =>
            [.. Names.Select(name => new AppEntry(name, $@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\{name}.lnk"))];
    }
}
