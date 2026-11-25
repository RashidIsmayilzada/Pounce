using System.ComponentModel;

namespace Pounce.Core;

/// <summary>One row of the panel: an application (<see cref="AppItem"/>) or a Result (<see cref="FileItem"/>).</summary>
public abstract record SearchItem
{
    private protected SearchItem()
    {
    }

    /// <summary>The row's title: the app's or the file's name.</summary>
    public abstract string Name { get; }

    /// <summary>What to open or reveal: the app's launch path, or the Result's file path.</summary>
    public abstract string Path { get; }
}

/// <summary>A row for an application.</summary>
/// <param name="App">The application.</param>
public sealed record AppItem(AppEntry App) : SearchItem
{
    /// <inheritdoc/>
    public override string Name => App.Name;

    /// <inheritdoc/>
    public override string Path => App.Path;
}

/// <summary>A row for a Result.</summary>
/// <param name="Result">The Result.</param>
public sealed record FileItem(ResultItem Result) : SearchItem
{
    /// <inheritdoc/>
    public override string Name => Result.Name;

    /// <inheritdoc/>
    public override string Path => Result.Path;
}

/// <summary>
/// Search-as-you-type: every change to <see cref="Query"/> cancels the previous search and starts a new one, whose
/// Early Results are replaced by its Final Results. Problems and empty searches become a <see cref="Message"/>.
/// Matching applications come first, like Spotlight.
/// </summary>
/// <remarks>
/// <para>Threading: create and use it on one thread, the UI thread, which must have a
/// <see cref="SynchronizationContext"/> (WPF's Dispatcher provides one). Every state change and every
/// <see cref="PropertyChanged"/> event happens on that thread: work elsewhere (the Engine, listing apps) is awaited
/// on the captured context, so no event is ever raised from a thread-pool thread and the UI thread is never blocked.
/// Results of a search that a newer <see cref="Query"/> has superseded are never shown, even if they arrive late.</para>
/// <para>Binding: lists are replaced, never changed in place, and announced with <see cref="PropertyChanged"/>, so
/// bind <c>ItemsSource</c> to <see cref="Items"/> (or to <see cref="Apps"/> and <see cref="Results"/>) and
/// <c>SelectedIndex</c> to <see cref="Selection"/>. A change to <see cref="Items"/> is announced before the
/// <see cref="Selection"/> that goes with it.</para>
/// <para>Starting: while <see cref="EngineStarting"/> is true the Engine is not answering yet, so a Query is not sent
/// at all (each would only wait for a refused connection). It is held, with <see cref="StartingMessage"/> and the
/// matching apps shown and <see cref="IsSearching"/> true, and searched once <see cref="EngineStarting"/> turns false.</para>
/// </remarks>
public sealed class SearchModel : INotifyPropertyChanged
{
    /// <summary>The <see cref="Message"/> while the Engine is starting and a Query waits for it.</summary>
    public const string StartingMessage = "Pounce is starting. Your results will appear in a moment.";

    private readonly ISearchEngine _engine;
    private readonly IAppSource _appSource;
    private readonly int _limit;
    private IReadOnlyList<AppEntry> _catalog = [];
    private string _query = "";
    private IReadOnlyList<ResultItem> _results = [];
    private bool _isFinal;
    private string _shownQuery = "";
    private string? _message;
    private IReadOnlyList<AppEntry> _apps = [];
    private IReadOnlyList<SearchItem> _items = [];
    private int _selection;
    private CancellationTokenSource? _search;
    private int _generation;
    private bool _engineStarting;
    private bool _waitingForEngine;

    /// <summary>A model that searches <paramref name="engine"/> and, optionally, the apps from <paramref name="apps"/>.</summary>
    /// <param name="engine">Answers the Queries.</param>
    /// <param name="apps">The installed applications; none when null.</param>
    /// <param name="limit">How many Results to ask the Engine for.</param>
    public SearchModel(ISearchEngine engine, IAppSource? apps = null, int limit = 8)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _appSource = apps ?? new NoApps();
        _limit = limit;
    }

    /// <summary>Raised on the UI thread whenever a property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The text in the search box. Setting a different value starts a new search.</summary>
    public string Query
    {
        get => _query;
        set
        {
            value ??= "";
            if (string.Equals(value, _query, StringComparison.Ordinal))
            {
                return;
            }
            _query = value;
            Raise(nameof(Query));
            Raise(nameof(IsSearching));
            Start();
        }
    }

    /// <summary>
    /// True while the Engine has been started but does not answer yet. Then a new Query is held instead of sent, and a
    /// search that fails because the Engine isn't there yet (<see cref="EngineProblem.NotRunning"/>, or
    /// <see cref="EngineProblem.SecretMissing"/> before the Engine has written its secret) is held too; either way
    /// <see cref="Message"/> is <see cref="StartingMessage"/>. Setting it to false searches a held Query, once.
    /// </summary>
    public bool EngineStarting
    {
        get => _engineStarting;
        set
        {
            if (value == _engineStarting)
            {
                return;
            }
            _engineStarting = value;
            Raise(nameof(EngineStarting));
            if (!value && _waitingForEngine)
            {
                // As if the Query had just been typed. Nothing runs to cancel: a held Query has no search.
                Start();
            }
        }
    }

    /// <summary>The Results shown, best first (Early Results until the Final Results arrive).</summary>
    public IReadOnlyList<ResultItem> Results => _results;

    /// <summary>True once the Final Results for the current <see cref="Query"/> are shown; false while a newer search is running.</summary>
    public bool IsFinal => _isFinal;

    /// <summary>True while a search for a non-empty <see cref="Query"/> is still running: show a progress indicator.</summary>
    public bool IsSearching => _query.Length > 0 && !_isFinal;

    /// <summary>The Query the shown Results belong to (lags behind <see cref="Query"/> while a new search runs).</summary>
    public string ShownQuery => _shownQuery;

    /// <summary>A problem, or "No results for “…”", shown instead of Results; null when there is nothing to say.</summary>
    public string? Message => _message;

    /// <summary>Applications whose names match the Query, best first; shown before the Results, like Spotlight.</summary>
    public IReadOnlyList<AppEntry> Apps => _apps;

    /// <summary>Apps first, then Results: the order of the rows and of the selection.</summary>
    public IReadOnlyList<SearchItem> Items => _items;

    /// <summary>
    /// The index of the selected row in <see cref="Items"/>: back to 0 for every new search, kept on the same item
    /// when <see cref="ReloadAppsAsync"/> re-ranks the apps. Always inside <see cref="Items"/> (0 when there are no
    /// rows): a value outside is clamped to the nearest row.
    /// </summary>
    public int Selection
    {
        get => _selection;
        set
        {
            value = Clamp(value, _items.Count);
            if (value == _selection)
            {
                return;
            }
            _selection = value;
            Raise(nameof(Selection));
            Raise(nameof(Selected));
        }
    }

    /// <summary>The selected row, or null when there are no rows.</summary>
    public SearchItem? Selected => _selection >= 0 && _selection < _items.Count ? _items[_selection] : null;

    /// <summary>Moves the selection by <paramref name="step"/> rows (−1 up, 1 down), stopping at the first and last row.</summary>
    public void MoveSelection(int step)
    {
        if (_items.Count == 0)
        {
            return;
        }
        Selection = Math.Clamp(_selection + step, 0, _items.Count - 1);
    }

    /// <summary>
    /// Re-reads the installed applications on a thread-pool thread, then re-ranks them for the current Query on the
    /// UI thread. Call it when the panel opens, to pick up apps installed or removed since. If the app source fails,
    /// the task fails and the previous apps stay. The selection stays on the same item when that is still listed
    /// (the user may have moved it while the listing ran); otherwise it moves to the nearest row.
    /// </summary>
    public async Task ReloadAppsAsync()
    {
        IAppSource source = _appSource;
        IReadOnlyList<AppEntry> catalog = await Task.Run(source.ListApps);
        _catalog = catalog;
        SetApps(AppMatcher.Rank(_query, _catalog), keep: Selected);
    }

    private void Start()
    {
        _search?.Cancel();
        _search = null;
        _waitingForEngine = false;
        int generation = ++_generation;
        SetApps(AppMatcher.Rank(_query, _catalog));
        Selection = 0;
        string text = _query.Trim();
        if (text.Length == 0)
        {
            Show([], "", final: true, message: null);
            return;
        }
        if (_engineStarting)
        {
            WaitForEngine(text);
            return;
        }
        SetIsFinal(false);
        var search = new CancellationTokenSource();
        _search = search;
        _ = RunAsync(text, generation, search);
    }

    /// <summary>Runs one search on the UI thread's context; every await comes back to it before touching state.</summary>
    private async Task RunAsync(string text, int generation, CancellationTokenSource search)
    {
        try
        {
            await foreach (SearchPhase phase in _engine.SearchAsync(text, _limit, search.Token))
            {
                // A superseded search may still deliver a phase that was already on its way: never show it.
                if (generation != _generation)
                {
                    return;
                }
                string? message = phase.IsFinal && phase.Items.Count == 0 && _apps.Count == 0
                    ? $"No results for “{text}”"
                    : null;
                Show(phase.Items, text, phase.IsFinal, message);
            }
        }
        catch (Exception e)
        {
            if (generation == _generation)
            {
                // While the Engine starts, "isn't running" and "can't read the secret" only mean "not yet".
                if (_engineStarting && e is EngineException { Problem: EngineProblem.NotRunning or EngineProblem.SecretMissing })
                {
                    WaitForEngine(text);
                }
                else
                {
                    Show([], text, final: true, e.Message);
                }
            }
        }
        finally
        {
            if (_search == search)
            {
                _search = null;
            }
            search.Dispose();
        }
    }

    /// <summary>Holds <paramref name="text"/> until the Engine answers: no Results, the starting message, still searching.</summary>
    private void WaitForEngine(string text)
    {
        _waitingForEngine = true;
        Show([], text, final: false, StartingMessage);
    }

    private void Show(IReadOnlyList<ResultItem> items, string query, bool final, string? message)
    {
        _results = items;
        Raise(nameof(Results));
        RebuildItems();
        if (!string.Equals(_shownQuery, query, StringComparison.Ordinal))
        {
            _shownQuery = query;
            Raise(nameof(ShownQuery));
        }
        SetIsFinal(final);
        if (!string.Equals(_message, message, StringComparison.Ordinal))
        {
            _message = message;
            Raise(nameof(Message));
        }
        Selection = 0;
    }

    private void SetApps(IReadOnlyList<AppEntry> apps, SearchItem? keep = null)
    {
        _apps = apps;
        Raise(nameof(Apps));
        RebuildItems(keep);
    }

    private void SetIsFinal(bool final)
    {
        if (final == _isFinal)
        {
            return;
        }
        _isFinal = final;
        Raise(nameof(IsFinal));
        Raise(nameof(IsSearching));
    }

    /// <summary>
    /// Rebuilds <see cref="Items"/>. The selection moves to <paramref name="keep"/> when it is among the new rows,
    /// and is otherwise clamped to them; it is updated before <see cref="Items"/> is announced, so a listener never
    /// sees it point past the end.
    /// </summary>
    private void RebuildItems(SearchItem? keep = null)
    {
        var items = new SearchItem[_apps.Count + _results.Count];
        for (int i = 0; i < _apps.Count; i++)
        {
            items[i] = new AppItem(_apps[i]);
        }
        for (int i = 0; i < _results.Count; i++)
        {
            items[_apps.Count + i] = new FileItem(_results[i]);
        }
        _items = items;
        int kept = keep is null ? -1 : Array.IndexOf(items, keep);
        int selection = kept >= 0 ? kept : Clamp(_selection, items.Length);
        bool selectionChanged = selection != _selection;
        _selection = selection;
        Raise(nameof(Items));
        if (selectionChanged)
        {
            Raise(nameof(Selection));
        }
        Raise(nameof(Selected));
    }

    private static int Clamp(int selection, int count) => count == 0 ? 0 : Math.Clamp(selection, 0, count - 1);

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
