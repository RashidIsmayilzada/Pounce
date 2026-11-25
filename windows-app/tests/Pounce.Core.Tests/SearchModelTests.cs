using static Pounce.Core.Tests.Waits;

namespace Pounce.Core.Tests;

/// <summary>Search-as-you-type, with a fake Engine the test drives by hand, on a single "UI thread".</summary>
public class SearchModelTests
{
    private readonly FakeEngine _engine = new();

    [Fact]
    public void Typing_shows_early_results_then_replaces_them_with_final_results() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);

        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Early([Item("early-guess.md")]), "canary");
        await model.Until(m => m.Results.Select(r => r.Name).SequenceEqual(["early-guess.md"]));
        Assert.False(model.IsFinal);
        _engine.Send(SearchPhase.Final([Item("rollout.md"), Item("runbook.md")]), "canary");

        await model.Until(m => m.Results.Select(r => r.Name).SequenceEqual(["rollout.md", "runbook.md"]));
        Assert.True(model.IsFinal);
        Assert.Equal("canary", model.ShownQuery);
    });

    [Fact]
    public void While_a_new_query_runs_the_shown_results_are_marked_as_belonging_to_the_old_one() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);
        model.Query = "can";
        await _engine.WhenSearching("can");
        _engine.Send(SearchPhase.Final([Item("cannon.md")]), "can");
        await model.Until(m => m.IsFinal);

        model.Query = "canary";

        Assert.False(model.IsFinal);
        Assert.True(model.IsSearching);
        Assert.Equal("can", model.ShownQuery);
        Assert.Equal(["cannon.md"], model.Results.Select(r => r.Name));
    });

    [Fact]
    public void A_new_keystroke_drops_the_results_of_the_previous_query() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);

        model.Query = "can";
        await _engine.WhenSearching("can");
        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Final([Item("stale.md")]), "can");
        _engine.Send(SearchPhase.Final([Item("rollout.md")]), "canary");

        await model.Until(m => m.Results.Select(r => r.Name).SequenceEqual(["rollout.md"]));
        await _engine.WhenEnded("can");
        Assert.True(_engine.WasCancelled("can"));
        Assert.Equal(["rollout.md"], model.Results.Select(r => r.Name));
    });

    [Fact]
    public void Late_results_of_a_cancelled_slower_search_are_ignored() => SingleThreadContext.Run(async _ =>
    {
        _engine.HonoursCancellation = false;
        var model = new SearchModel(_engine);
        model.Query = "can";
        await _engine.WhenSearching("can");
        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Final([Item("rollout.md")]), "canary");
        await model.Until(m => m.IsFinal);

        // The slower search ignored its cancel and answers after the newer one: the model must drop it.
        _engine.Send(SearchPhase.Early([Item("stale-early.md")]), "can");
        await _engine.WhenEnded("can");

        Assert.True(_engine.WasCancelled("can"));
        Assert.Equal(["rollout.md"], model.Results.Select(r => r.Name));
        Assert.Equal("canary", model.ShownQuery);
        Assert.True(model.IsFinal);
    });

    [Fact]
    public void Clearing_the_box_clears_the_results() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);
        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Final([Item("rollout.md")]), "canary");
        await model.Until(m => m.Results.Count > 0);

        model.Query = "   ";

        Assert.Empty(model.Results);
        Assert.Empty(model.Items);
        Assert.Null(model.Message);
        Assert.True(model.IsFinal);
        Assert.False(model.IsSearching);
    });

    [Fact]
    public void An_engine_problem_is_shown_as_a_message_instead_of_results() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);

        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Fail("canary", EngineException.NotRunning(new Uri("http://127.0.0.1:7431")));

        await model.Until(m => m.Message is not null);
        Assert.Equal("Pounce isn't running at http://127.0.0.1:7431. Start Pounce and try again.", model.Message);
        Assert.Empty(model.Results);
        Assert.True(model.IsFinal);
    });

    [Fact]
    public void A_search_with_no_matches_says_so() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);

        model.Query = "xyzzyplugh";
        await _engine.WhenSearching("xyzzyplugh");
        _engine.Send(SearchPhase.Final([]), "xyzzyplugh");

        await model.Until(m => m.Message is not null);
        Assert.Equal("No results for “xyzzyplugh”", model.Message);
    });

    [Fact]
    public void The_query_is_sent_trimmed_with_the_limit() => SingleThreadContext.Run(async _ =>
    {
        var engine = new RecordingEngine();
        var model = new SearchModel(engine, limit: 5);

        model.Query = "  canary rollout ";
        await engine.Searched.Task.WaitAsync(Patience);

        Assert.Equal(("canary rollout", 5), engine.Searched.Task.Result);
    });

    [Fact]
    public void Every_change_is_announced_on_the_model_thread() => SingleThreadContext.Run(async ui =>
    {
        var model = new SearchModel(_engine);
        var changes = new List<string>();
        model.PropertyChanged += (_, e) =>
        {
            Assert.True(ui.IsCurrent, $"{e.PropertyName} changed off the UI thread");
            changes.Add(e.PropertyName!);
        };

        model.Query = "canary";
        await _engine.WhenSearching("canary");
        await Task.Run(() => _engine.Send(SearchPhase.Early([Item("a.md")]), "canary"));
        await model.Until(m => m.Results.Count == 1);
        await Task.Run(() => _engine.Send(SearchPhase.Final([Item("a.md"), Item("b.md")]), "canary"));
        await model.Until(m => m.IsFinal);

        Assert.Contains(nameof(SearchModel.Results), changes);
        Assert.Contains(nameof(SearchModel.Items), changes);
        Assert.Contains(nameof(SearchModel.IsFinal), changes);
        Assert.Contains(nameof(SearchModel.IsSearching), changes);
        Assert.Contains(nameof(SearchModel.ShownQuery), changes);
        Assert.True(changes.LastIndexOf(nameof(SearchModel.Items)) < changes.LastIndexOf(nameof(SearchModel.Selected)));
    });

    [Fact]
    public void The_selection_moves_within_the_rows_and_starts_at_the_top_for_new_rows() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);
        Assert.Null(model.Selected);
        model.MoveSelection(1);
        Assert.Equal(0, model.Selection);

        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Early([Item("a.md"), Item("b.md"), Item("c.md")]), "canary");
        await model.Until(m => m.Items.Count == 3);

        model.MoveSelection(5);
        Assert.Equal(2, model.Selection);
        Assert.Equal(new FileItem(Item("c.md")), model.Selected);
        model.MoveSelection(-1);
        Assert.Equal(1, model.Selection);
        model.MoveSelection(-9);
        Assert.Equal(0, model.Selection);

        model.MoveSelection(2);
        _engine.Send(SearchPhase.Final([Item("d.md"), Item("e.md")]), "canary");
        await model.Until(m => m.IsFinal);
        Assert.Equal(0, model.Selection);
        Assert.Equal(new FileItem(Item("d.md")), model.Selected);
    });

    [Fact]
    public void Without_rows_nothing_is_selected() => SingleThreadContext.Run(_ =>
    {
        var model = new SearchModel(_engine) { Selection = -1 };

        Assert.Equal(0, model.Selection);
        Assert.Null(model.Selected);
        return Task.CompletedTask;
    });

    [Fact]
    public void A_selection_outside_the_rows_is_clamped_to_the_nearest_row() => SingleThreadContext.Run(async _ =>
    {
        var model = new SearchModel(_engine);
        model.Query = "canary";
        await _engine.WhenSearching("canary");
        _engine.Send(SearchPhase.Final([Item("a.md"), Item("b.md")]), "canary");
        await model.Until(m => m.IsFinal);

        model.Selection = 7;
        Assert.Equal(1, model.Selection);
        Assert.Equal(new FileItem(Item("b.md")), model.Selected);

        model.Selection = -3;
        Assert.Equal(0, model.Selection);
    });

    // ---- While the Engine starts ----

    [Fact]
    public void While_the_engine_starts_a_query_is_held_with_the_matching_apps_and_a_starting_message() => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var apps = new FixedApps("Calculator", "Calendar", "Notepad");
        var model = new SearchModel(engine, apps) { EngineStarting = true };
        await model.ReloadAppsAsync();

        model.Query = "ca";
        model.Query = "cal";

        Assert.Equal(0, engine.Calls);
        Assert.Equal(SearchModel.StartingMessage, model.Message);
        Assert.Equal("Pounce is starting. Your results will appear in a moment.", model.Message);
        Assert.Equal(["Calculator", "Calendar"], model.Apps.Select(a => a.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, model.Items.Count);
        Assert.Empty(model.Results);
        Assert.True(model.IsSearching);
        Assert.False(model.IsFinal);
        Assert.Equal("cal", model.ShownQuery);
    });

    [Fact]
    public void Once_the_engine_answers_the_held_query_is_searched_once() => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine) { EngineStarting = true };
        model.Query = "can";
        model.Query = "canary";

        model.EngineStarting = false;
        model.EngineStarting = true;
        model.EngineStarting = false;

        QueuedEngine.Call call = await engine.NextCall();
        Assert.Equal("canary", call.Query);
        Assert.Equal(1, engine.Calls);
        Assert.True(model.IsSearching);
        call.Send(SearchPhase.Final([Item("rollout.md")]));
        await model.Until(m => m.IsFinal);
        Assert.Equal(["rollout.md"], model.Results.Select(r => r.Name));
        Assert.Null(model.Message);
        Assert.Equal(1, engine.Calls);
    });

    [Fact]
    public void A_query_typed_after_the_engine_answers_is_not_cancelled_by_the_held_one() => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine) { EngineStarting = true };
        model.Query = "can";
        model.EngineStarting = false;
        QueuedEngine.Call held = await engine.NextCall();

        model.Query = "canary";
        QueuedEngine.Call typed = await engine.NextCall();
        model.EngineStarting = true;
        model.EngineStarting = false;

        Assert.True(held.Cancelled);
        Assert.False(typed.Cancelled);
        Assert.Equal(2, engine.Calls);
        typed.Send(SearchPhase.Final([Item("rollout.md")]));
        await model.Until(m => m.IsFinal);
        Assert.Equal("canary", model.ShownQuery);
    });

    [Fact]
    public void A_box_cleared_while_waiting_searches_nothing_when_the_engine_answers() => SingleThreadContext.Run(_ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine) { EngineStarting = true };
        model.Query = "canary";
        model.Query = "";

        model.EngineStarting = false;

        Assert.Equal(0, engine.Calls);
        Assert.Null(model.Message);
        Assert.False(model.IsSearching);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(EngineProblem.NotRunning)]
    [InlineData(EngineProblem.SecretMissing)]
    public void A_search_that_finds_no_engine_yet_waits_for_it_while_it_starts(EngineProblem problem) => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine);
        model.Query = "canary";
        QueuedEngine.Call first = await engine.NextCall();

        model.EngineStarting = true;
        first.Fail(Problem(problem));
        await model.Until(m => m.Message is not null);

        Assert.Equal(SearchModel.StartingMessage, model.Message);
        Assert.True(model.IsSearching);
        model.EngineStarting = false;
        QueuedEngine.Call again = await engine.NextCall();
        Assert.Equal("canary", again.Query);
        again.Send(SearchPhase.Final([Item("rollout.md")]));
        await model.Until(m => m.IsFinal);
        Assert.Null(model.Message);
    });

    [Theory]
    [InlineData(EngineProblem.SecretRefused, "Pounce refused the secret in C:\\Pounce\\bootstrap-secret. Is it from another Pounce install?")]
    [InlineData(EngineProblem.Unexpected, "Pounce answered with HTTP 500.")]
    public void Other_problems_keep_their_own_text_while_the_engine_starts(EngineProblem problem, string text) => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine);
        model.Query = "canary";
        QueuedEngine.Call call = await engine.NextCall();

        model.EngineStarting = true;
        call.Fail(Problem(problem));
        await model.Until(m => m.Message is not null);

        Assert.Equal(text, model.Message);
        Assert.True(model.IsFinal);
        Assert.False(model.IsSearching);
        model.EngineStarting = false;
        Assert.Equal(1, engine.Calls);
    });

    [Fact]
    public void A_search_that_succeeds_while_the_engine_starts_is_shown_as_usual() => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine);
        model.Query = "canary";
        QueuedEngine.Call call = await engine.NextCall();

        model.EngineStarting = true;
        call.Send(SearchPhase.Final([Item("rollout.md")]));
        await model.Until(m => m.IsFinal);

        Assert.Equal(["rollout.md"], model.Results.Select(r => r.Name));
        Assert.Null(model.Message);
        model.EngineStarting = false;
        Assert.Equal(1, engine.Calls);
    });

    [Fact]
    public void When_the_engine_is_not_starting_its_absence_is_reported() => SingleThreadContext.Run(async _ =>
    {
        var engine = new QueuedEngine();
        var model = new SearchModel(engine) { EngineStarting = true };
        model.Query = "canary";

        // The wait was abandoned (the Engine failed): the held Query is searched and the real problem shows.
        model.EngineStarting = false;
        (await engine.NextCall()).Fail(Problem(EngineProblem.NotRunning));
        await model.Until(m => m.IsFinal);

        Assert.Equal("Pounce isn't running at http://127.0.0.1:7431. Start Pounce and try again.", model.Message);
        Assert.False(model.IsSearching);
    });

    private static EngineException Problem(EngineProblem problem) => problem switch
    {
        EngineProblem.NotRunning => EngineException.NotRunning(new Uri("http://127.0.0.1:7431")),
        EngineProblem.SecretMissing => EngineException.SecretMissing(@"C:\Pounce\bootstrap-secret"),
        EngineProblem.SecretRefused => EngineException.SecretRefused(@"C:\Pounce\bootstrap-secret"),
        _ => EngineException.Unexpected(500),
    };

    /// <summary>
    /// Every search is a new call the test answers by hand. A call is counted the moment the model asks, before
    /// anything is awaited, so "no call" can be checked right after setting the Query.
    /// </summary>
    private sealed class QueuedEngine : ISearchEngine
    {
        private readonly System.Threading.Channels.Channel<Call> _calls = System.Threading.Channels.Channel.CreateUnbounded<Call>();
        private int _count;

        public int Calls => Volatile.Read(ref _count);

        public async IAsyncEnumerable<SearchPhase> SearchAsync(string query, int limit,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            var call = new Call(query, cancellationToken);
            _calls.Writer.TryWrite(call);
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            await foreach (SearchPhase phase in call.Phases.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return phase;
            }
        }

        public async Task<Call> NextCall() => await _calls.Reader.ReadAsync().AsTask().WaitAsync(Patience);

        public sealed class Call(string query, CancellationToken cancelled)
        {
            public string Query => query;

            public bool Cancelled => cancelled.IsCancellationRequested;

            public System.Threading.Channels.Channel<SearchPhase> Phases { get; } = System.Threading.Channels.Channel.CreateUnbounded<SearchPhase>();

            public void Send(SearchPhase phase)
            {
                Phases.Writer.TryWrite(phase);
                if (phase.IsFinal)
                {
                    Phases.Writer.TryComplete();
                }
            }

            public void Fail(Exception error) => Phases.Writer.TryComplete(error);
        }
    }

    /// <summary>Records the one search it gets, and never answers.</summary>
    private sealed class RecordingEngine : ISearchEngine
    {
        public TaskCompletionSource<(string Query, int Limit)> Searched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<SearchPhase> SearchAsync(string query, int limit,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Searched.TrySetResult((query, limit));
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            yield break;
        }
    }
}
