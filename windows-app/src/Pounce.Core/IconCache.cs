namespace Pounce.Core;

/// <summary>
/// Icons by key (see <see cref="IconKey"/>): a bounded memory cache in front of a slow loader that runs on one worker
/// thread. Requests for a key that is already loading share that load. Waiting loads run newest first, and only the
/// newest <c>maxWaiting</c> of them are kept: while the user types, rows that have already been replaced stop
/// competing with the rows on screen. "No icon" (a null image) is cached like any other answer.
/// </summary>
/// <remarks>
/// Thread-safe. The cache drops its least recently used entry when it is full. Tasks complete asynchronously, never
/// inline on the worker thread, so the caller's continuation runs on its own synchronization context.
/// </remarks>
/// <typeparam name="T">The image type.</typeparam>
public sealed class IconCache<T>
    where T : class
{
    private readonly int _capacity;
    private readonly int _maxWaiting;
    private readonly Action<Action> _schedule;
    private readonly Action<string, Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, T? Image)>> _cached = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, T? Image)> _recent = new();
    private readonly Dictionary<string, TaskCompletionSource<T?>> _loading = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, Func<T?> Load)> _waiting = new();
    private int _scheduled;

    /// <summary>A cache of at most <paramref name="capacity"/> icons.</summary>
    /// <param name="capacity">How many answers to keep in memory.</param>
    /// <param name="maxWaiting">How many loads may wait for the worker; the oldest is dropped beyond that.</param>
    /// <param name="schedule">
    /// Runs the given action once on the worker thread, later. It is called at most once per waiting load, so
    /// what it queues stays bounded too.
    /// </param>
    /// <param name="onError">
    /// Told when a loader or <paramref name="schedule"/> throws; that request then completes with null, which is not
    /// cached.
    /// </param>
    public IconCache(int capacity, int maxWaiting, Action<Action> schedule, Action<string, Exception>? onError = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWaiting, 1);
        ArgumentNullException.ThrowIfNull(schedule);
        _capacity = capacity;
        _maxWaiting = maxWaiting;
        _schedule = schedule;
        _onError = onError;
    }

    /// <summary>How many answers are cached.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _cached.Count;
            }
        }
    }

    /// <summary>How many loads wait for the worker.</summary>
    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count;
            }
        }
    }

    /// <summary>True, with the image (null for "no icon"), when <paramref name="key"/> is cached. Never waits for a load.</summary>
    public bool TryGet(string key, out T? image)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (_cached.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null;
        return false;
    }

    /// <summary>
    /// The icon for <paramref name="key"/>: at once when cached; otherwise from <paramref name="load"/>, run on the
    /// worker unless a load for the same key is already on its way. Completes with null when the load fails or is
    /// dropped as stale. Never throws from the task.
    /// </summary>
    public Task<T?> GetAsync(string key, Func<T?> load)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(load);
        TaskCompletionSource<T?>? dropped = null;
        TaskCompletionSource<T?> request;
        bool schedule = false;
        lock (_gate)
        {
            if (_cached.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return Task.FromResult(node.Value.Image);
            }
            if (_loading.TryGetValue(key, out var existing))
            {
                PromoteWaiting(key);
                return existing.Task;
            }
            request = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _loading.Add(key, request);
            _waiting.AddFirst((key, load));
            if (_waiting.Count > _maxWaiting)
            {
                string stale = _waiting.Last!.Value.Key;
                _waiting.RemoveLast();
                dropped = _loading[stale];
                _loading.Remove(stale);
            }
            if (_scheduled < _waiting.Count)
            {
                _scheduled++;
                schedule = true;
            }
        }
        dropped?.TrySetResult(null);
        if (schedule)
        {
            try
            {
                _schedule(RunNewest);
            }
            catch (Exception e)
            {
                Withdraw(key, request);
                Report(key, e);
            }
        }
        return request.Task;
    }

    /// <summary>Takes back a request the worker will never see, so its key is not stuck as "loading".</summary>
    private void Withdraw(string key, TaskCompletionSource<T?> request)
    {
        lock (_gate)
        {
            _scheduled--;
            for (var node = _waiting.First; node is not null; node = node.Next)
            {
                if (string.Equals(node.Value.Key, key, StringComparison.Ordinal))
                {
                    _waiting.Remove(node);
                    break;
                }
            }
            if (_loading.TryGetValue(key, out var current) && current == request)
            {
                _loading.Remove(key);
            }
        }
        request.TrySetResult(null);
    }

    private void Report(string key, Exception e)
    {
        try
        {
            _onError?.Invoke(key, e);
        }
        catch (Exception)
        {
            // A failing error handler must not take the worker down.
        }
    }

    /// <summary>Runs the newest waiting load. Called on the worker thread, once per scheduled action.</summary>
    private void RunNewest()
    {
        string key;
        Func<T?> load;
        lock (_gate)
        {
            _scheduled--;
            if (_waiting.First is not { } first)
            {
                return;
            }
            (key, load) = first.Value;
            _waiting.RemoveFirst();
        }

        T? image = null;
        bool ok;
        try
        {
            image = load();
            ok = true;
        }
        catch (Exception e)
        {
            ok = false;
            Report(key, e);
        }

        TaskCompletionSource<T?>? request;
        lock (_gate)
        {
            _loading.Remove(key, out request);
            if (ok)
            {
                Store(key, image);
            }
        }
        request?.TrySetResult(image);
    }

    /// <summary>Moves a waiting load to the front: it was asked for again, so it is current.</summary>
    private void PromoteWaiting(string key)
    {
        for (var node = _waiting.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value.Key, key, StringComparison.Ordinal))
            {
                _waiting.Remove(node);
                _waiting.AddFirst(node);
                return;
            }
        }
    }

    private void Store(string key, T? image)
    {
        if (_cached.Remove(key, out var old))
        {
            _recent.Remove(old);
        }
        _cached.Add(key, _recent.AddFirst((key, image)));
        while (_cached.Count > _capacity)
        {
            var last = _recent.Last!;
            _recent.RemoveLast();
            _cached.Remove(last.Value.Key);
        }
    }
}
