using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Pounce.Core.Tests;

/// <summary>
/// An <see cref="ISearchEngine"/> whose streams the test feeds by hand. Like the real client it works off the caller's
/// thread, so its phases reach the model from the thread pool. Each Query may be searched once per fake.
/// </summary>
internal sealed class FakeEngine : ISearchEngine
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Search> _searches = [];

    /// <summary>False makes the fake behave like a slow Engine that keeps going after a cancel.</summary>
    public bool HonoursCancellation { get; set; } = true;

    public async IAsyncEnumerable<SearchPhase> SearchAsync(string query, int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Search search = For(query);
        search.Cancelled = cancellationToken;
        search.Started.TrySetResult();
        try
        {
            CancellationToken token = HonoursCancellation ? cancellationToken : CancellationToken.None;
            await foreach (SearchPhase phase in search.Phases.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                yield return phase;
            }
        }
        finally
        {
            search.Ended.TrySetResult();
        }
    }

    /// <summary>Completes once the model has asked for <paramref name="query"/>.</summary>
    public Task WhenSearching(string query) => For(query).Started.Task.WaitAsync(Waits.Patience);

    /// <summary>Completes once the model has stopped reading the stream for <paramref name="query"/>.</summary>
    public Task WhenEnded(string query) => For(query).Ended.Task.WaitAsync(Waits.Patience);

    /// <summary>Whether the model cancelled the search for <paramref name="query"/>.</summary>
    public bool WasCancelled(string query) => For(query).Cancelled.IsCancellationRequested;

    /// <summary>Sends a phase; a Final phase also ends the stream.</summary>
    public void Send(SearchPhase phase, string query)
    {
        Search search = For(query);
        search.Phases.Writer.TryWrite(phase);
        if (phase.IsFinal)
        {
            search.Phases.Writer.TryComplete();
        }
    }

    public void Fail(string query, Exception error) => For(query).Phases.Writer.TryComplete(error);

    private Search For(string query)
    {
        lock (_lock)
        {
            if (!_searches.TryGetValue(query, out Search? search))
            {
                search = new Search();
                _searches.Add(query, search);
            }
            return search;
        }
    }

    private sealed class Search
    {
        public Channel<SearchPhase> Phases { get; } = Channel.CreateUnbounded<SearchPhase>();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Cancelled { get; set; }
    }
}

/// <summary>A fixed list of apps, for tests; remembers which thread listed them.</summary>
internal sealed class FixedApps(params string[] names) : IAppSource
{
    private readonly AppEntry[] _entries = [.. names.Select(name => new AppEntry(name, $@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\{name}.lnk"))];

    public int? ListedOnThread { get; private set; }

    public IReadOnlyList<AppEntry> ListApps()
    {
        ListedOnThread = Environment.CurrentManagedThreadId;
        return _entries;
    }

    public AppEntry Named(string name) => _entries.Single(entry => entry.Name == name);
}

internal static class Waits
{
    /// <summary>How long a test waits for something that should happen at once.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    /// <summary>Completes on the model's thread once <paramref name="condition"/> holds, checked after each change.</summary>
    public static Task Until(this SearchModel model, Func<SearchModel, bool> condition)
    {
        if (condition(model))
        {
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? check = null;
        check = (_, _) =>
        {
            if (condition(model))
            {
                model.PropertyChanged -= check;
                done.TrySetResult();
            }
        };
        model.PropertyChanged += check;
        return done.Task.WaitAsync(Patience);
    }

    public static ResultItem Item(string name) => new($"/notes/{name}", name, Snippet.Empty);
}
