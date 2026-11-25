namespace Pounce.Core.Tests;

/// <summary>The real HTTP client against a stand-in Engine on a real local socket.</summary>
public class HttpEngineClientTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    private static CancellationToken TestCancelled => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Streams_early_then_final_results()
    {
        await using var engine = StandInEngine.Start();
        engine.EarlyJson = """[{"path":"/n/a.md","name":"a.md","snippet":{"text":"early","highlights":[]}}]""";
        engine.FinalJson = """[{"path":"/n/b.md","name":"b.md","snippet":{"text":"the canary","highlights":[{"start":4,"end":10}]}}]""";
        using var client = engine.Client();

        List<SearchPhase> phases = await Collect(client.SearchAsync("canary", 8, TestCancelled));

        Assert.Equal(
            [
                SearchPhase.Early([new ResultItem("/n/a.md", "a.md", new Snippet("early", []))]),
                SearchPhase.Final([new ResultItem("/n/b.md", "b.md", new Snippet("the canary", [new Highlight(4, 10)]))]),
            ],
            phases);
    }

    [Fact]
    public async Task Sends_the_query_and_limit_with_a_bearer_token_and_logs_in_only_once()
    {
        await using var engine = StandInEngine.Start();
        using var client = engine.Client();

        await Collect(client.SearchAsync("canary rollout", 3, TestCancelled));
        await Collect(client.SearchAsync("helm", 8, TestCancelled));

        Assert.Equal(["q=canary rollout&limit=3", "q=helm&limit=8"], engine.Searches);
        Assert.Equal(1, engine.TokenRequests);
    }

    [Fact]
    public async Task Characters_that_mean_something_in_a_url_reach_the_engine_unchanged()
    {
        await using var engine = StandInEngine.Start();
        using var client = engine.Client();

        await Collect(client.SearchAsync("a&b=c #1 100% café+", 8, TestCancelled));

        Assert.Equal(["q=a&b=c #1 100% café+&limit=8"], engine.Searches);
    }

    [Fact]
    public async Task Keeps_one_connection_alive_for_the_login_and_every_finished_search()
    {
        await using var engine = StandInEngine.Start();
        using var client = engine.Client();

        for (int i = 0; i < 5; i++)
        {
            await Collect(client.SearchAsync($"query {i}", 8, TestCancelled));
        }

        Assert.Equal(1, engine.Connections);
    }

    [Fact]
    public async Task Logs_in_again_when_the_token_has_expired()
    {
        await using var engine = StandInEngine.Start();
        using var client = engine.Client();
        await Collect(client.SearchAsync("canary", 8, TestCancelled));

        engine.ExpireTokens();
        List<SearchPhase> phases = await Collect(client.SearchAsync("canary", 8, TestCancelled));

        Assert.Equal(2, engine.TokenRequests);
        Assert.Equal(2, phases.Count);
    }

    [Fact]
    public async Task Logs_in_again_only_once_when_even_a_fresh_token_is_refused()
    {
        await using var engine = StandInEngine.Start();
        engine.RefuseSearches = true;
        using var client = engine.Client();

        var error = await Assert.ThrowsAsync<EngineException>(() => Collect(client.SearchAsync("canary", 8, TestCancelled)));

        Assert.Equal(EngineProblem.Unexpected, error.Problem);
        Assert.Equal("Pounce answered with HTTP 401.", error.Message);
        Assert.Equal(2, engine.TokenRequests);
    }

    [Fact]
    public async Task Says_pounce_is_not_running_when_nothing_listens()
    {
        await using var engine = StandInEngine.Start();
        var url = new Uri($"http://127.0.0.1:{StandInEngine.ClosedPort()}");
        using var client = new HttpEngineClient(url, engine.SecretFile("s"));

        var error = await Assert.ThrowsAsync<EngineException>(() => Collect(client.SearchAsync("canary", 8, TestCancelled)));

        Assert.Equal(EngineProblem.NotRunning, error.Problem);
        Assert.Equal(url, error.EngineUrl);
        Assert.Equal($"Pounce isn't running at {url.OriginalString}. Start Pounce and try again.", error.Message);
    }

    [Fact]
    public async Task Says_the_secret_file_is_missing()
    {
        await using var engine = StandInEngine.Start();
        string missing = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}", "bootstrap-secret");
        using var client = new HttpEngineClient(engine.Url, missing);

        var error = await Assert.ThrowsAsync<EngineException>(() => Collect(client.SearchAsync("canary", 8, TestCancelled)));

        Assert.Equal(EngineProblem.SecretMissing, error.Problem);
        Assert.Equal(missing, error.SecretFile);
        Assert.Equal($"Can't read {missing}. Start Pounce once to create it.", error.Message);
    }

    [Fact]
    public async Task Says_the_secret_was_refused()
    {
        await using var engine = StandInEngine.Start();
        string wrong = engine.SecretFile("a-stale-secret");
        using var client = new HttpEngineClient(engine.Url, wrong);

        var error = await Assert.ThrowsAsync<EngineException>(() => Collect(client.SearchAsync("canary", 8, TestCancelled)));

        Assert.Equal(EngineProblem.SecretRefused, error.Problem);
        Assert.Equal(wrong, error.SecretFile);
        Assert.Equal($"Pounce refused the secret in {wrong}. Is it from another Pounce install?", error.Message);
    }

    [Fact]
    public async Task Says_what_the_engine_answered_when_it_is_not_a_result_stream()
    {
        await using var engine = StandInEngine.Start();
        engine.SearchStatus = 500;
        using var client = engine.Client();

        var error = await Assert.ThrowsAsync<EngineException>(() => Collect(client.SearchAsync("canary", 8, TestCancelled)));

        Assert.Equal(EngineProblem.Unexpected, error.Problem);
        Assert.Equal(500, error.Status);
    }

    [Fact]
    public async Task Cancelling_mid_stream_closes_the_connection_at_once()
    {
        await using var engine = StandInEngine.Start();
        engine.HoldAfterEarly = true;
        using var client = engine.Client();
        using var keystroke = CancellationTokenSource.CreateLinkedTokenSource(TestCancelled);
        var phases = new List<SearchPhase>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (SearchPhase phase in client.SearchAsync("canary", 8, keystroke.Token))
            {
                phases.Add(phase);
                await keystroke.CancelAsync();
            }
        });

        await engine.ClientLeft.Task.WaitAsync(Patience, TestCancelled);
        Assert.Equal([SearchPhase.Early([])], phases);
    }

    [Fact]
    public async Task Leaving_the_loop_mid_stream_closes_the_connection_too()
    {
        await using var engine = StandInEngine.Start();
        engine.HoldAfterEarly = true;
        using var client = engine.Client();

        await foreach (SearchPhase phase in client.SearchAsync("canary", 8, TestCancelled))
        {
            break;
        }

        await engine.ClientLeft.Task.WaitAsync(Patience, TestCancelled);
    }

    [Fact]
    public async Task A_cancelled_search_does_not_spoil_the_next_one()
    {
        await using var engine = StandInEngine.Start();
        engine.HoldAfterEarly = true;
        using var client = engine.Client();
        await foreach (SearchPhase phase in client.SearchAsync("can", 8, TestCancelled))
        {
            break;
        }
        await engine.ClientLeft.Task.WaitAsync(Patience, TestCancelled);

        engine.HoldAfterEarly = false;
        List<SearchPhase> phases = await Collect(client.SearchAsync("canary", 8, TestCancelled));

        Assert.Equal(2, phases.Count);
        Assert.True(phases[1].IsFinal);
        Assert.Equal(1, engine.TokenRequests);
    }

    [Fact]
    public void The_connection_ignores_any_system_proxy()
    {
        using var handler = HttpEngineClient.CreateHandler();

        Assert.False(handler.UseProxy);
    }

    [Theory]
    [InlineData("http://127.0.0.1:7431", "http://127.0.0.1:7431/search")]
    [InlineData("http://127.0.0.1:7431/", "http://127.0.0.1:7431/search")]
    [InlineData("http://host/pounce", "http://host/pounce/search")]
    public void Endpoints_keep_the_base_path(string baseUrl, string expected)
    {
        Assert.Equal(new Uri(expected), HttpEngineClient.Endpoint(new Uri(baseUrl), "search"));
    }

    internal static async Task<List<SearchPhase>> Collect(IAsyncEnumerable<SearchPhase> stream)
    {
        var phases = new List<SearchPhase>();
        await foreach (SearchPhase phase in stream)
        {
            phases.Add(phase);
        }
        return phases;
    }
}
