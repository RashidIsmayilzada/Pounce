namespace Pounce.Core.Tests;

/// <summary>The icon cache: coalescing, newest first, bounded waiting and bounded memory.</summary>
public class IconCacheTests
{
    /// <summary>A worker thread that runs only when the test says so.</summary>
    private sealed class ManualWorker
    {
        private readonly Queue<Action> _queue = new();

        public int Queued => _queue.Count;

        public void Schedule(Action action) => _queue.Enqueue(action);

        public void RunAll()
        {
            while (_queue.TryDequeue(out Action? action))
            {
                action();
            }
        }

        public void RunOne() => _queue.Dequeue()();
    }

    private readonly ManualWorker _worker = new();
    private readonly List<string> _loaded = [];

    private IconCache<string> NewCache(int capacity = 10, int maxWaiting = 10, Action<string, Exception>? onError = null) =>
        new(capacity, maxWaiting, _worker.Schedule, onError);

    private Func<string?> Load(string key) => () =>
    {
        _loaded.Add(key);
        return "icon of " + key;
    };

    [Fact]
    public async Task A_load_runs_on_the_worker_and_is_then_cached()
    {
        IconCache<string> cache = NewCache();

        Task<string?> icon = cache.GetAsync("type:.md", Load("type:.md"));
        Assert.False(icon.IsCompleted);
        Assert.False(cache.TryGet("type:.md", out _));

        _worker.RunAll();

        Assert.Equal("icon of type:.md", await icon);
        Assert.True(cache.TryGet("type:.md", out string? cached));
        Assert.Equal("icon of type:.md", cached);
        Assert.Equal("icon of type:.md", await cache.GetAsync("type:.md", Load("again")));
        Assert.Equal(["type:.md"], _loaded);
    }

    [Fact]
    public async Task Requests_for_a_key_that_is_loading_share_the_load()
    {
        IconCache<string> cache = NewCache();

        Task<string?>[] icons = [.. Enumerable.Range(0, 5).Select(_ => cache.GetAsync("type:.pdf", Load("type:.pdf")))];
        _worker.RunAll();

        foreach (Task<string?> icon in icons)
        {
            Assert.Equal("icon of type:.pdf", await icon);
        }
        Assert.Equal(["type:.pdf"], _loaded);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void The_newest_request_is_loaded_first()
    {
        IconCache<string> cache = NewCache();
        _ = cache.GetAsync("a", Load("a"));
        _ = cache.GetAsync("b", Load("b"));
        _ = cache.GetAsync("c", Load("c"));

        _worker.RunAll();

        Assert.Equal(["c", "b", "a"], _loaded);
    }

    [Fact]
    public void Asking_again_for_a_waiting_key_makes_it_current()
    {
        IconCache<string> cache = NewCache();
        _ = cache.GetAsync("a", Load("a"));
        _ = cache.GetAsync("b", Load("b"));
        _ = cache.GetAsync("a", Load("a"));

        _worker.RunAll();

        Assert.Equal(["a", "b"], _loaded);
    }

    [Fact]
    public async Task Beyond_the_waiting_limit_the_oldest_request_is_dropped_with_no_icon_and_not_cached()
    {
        IconCache<string> cache = NewCache(maxWaiting: 2);
        Task<string?> oldest = cache.GetAsync("a", Load("a"));
        Task<string?> middle = cache.GetAsync("b", Load("b"));
        Task<string?> newest = cache.GetAsync("c", Load("c"));

        Assert.Null(await oldest);
        Assert.Equal(2, cache.Waiting);
        Assert.Equal(2, _worker.Queued);
        _worker.RunAll();

        Assert.Equal("icon of c", await newest);
        Assert.Equal("icon of b", await middle);
        Assert.Equal(["c", "b"], _loaded);
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal("icon of a", await RunAndGet(cache.GetAsync("a", Load("a"))));
    }

    [Fact]
    public void The_worker_queue_stays_bounded_however_many_requests_come()
    {
        IconCache<string> cache = NewCache(maxWaiting: 3);

        for (int i = 0; i < 1000; i++)
        {
            _ = cache.GetAsync("key " + i, Load("key " + i));
        }

        Assert.Equal(3, cache.Waiting);
        Assert.Equal(3, _worker.Queued);
        _worker.RunAll();
        Assert.Equal(["key 999", "key 998", "key 997"], _loaded);
        Assert.Equal(0, cache.Waiting);
    }

    [Fact]
    public async Task A_cached_no_icon_answer_is_not_loaded_again()
    {
        IconCache<string> cache = NewCache();
        int loads = 0;

        Task<string?> first = cache.GetAsync("file:c:\\gone.exe", () =>
        {
            loads++;
            return null;
        });
        _worker.RunAll();

        Assert.Null(await first);
        Assert.True(cache.TryGet("file:c:\\gone.exe", out string? image));
        Assert.Null(image);
        Assert.Null(await cache.GetAsync("file:c:\\gone.exe", () => throw new InvalidOperationException("not again")));
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task A_failing_load_gives_no_icon_is_reported_and_is_tried_again_later()
    {
        var errors = new List<string>();
        IconCache<string> cache = NewCache(onError: (key, e) => errors.Add(key + ": " + e.Message));

        Task<string?> failed = cache.GetAsync("type:.xyz", () => throw new InvalidOperationException("boom"));
        _worker.RunAll();

        Assert.Null(await failed);
        Assert.Equal(["type:.xyz: boom"], errors);
        Assert.False(cache.TryGet("type:.xyz", out _));
        Assert.Equal("icon of type:.xyz", await RunAndGet(cache.GetAsync("type:.xyz", Load("type:.xyz"))));
    }

    [Fact]
    public async Task A_failing_error_handler_does_not_break_the_worker()
    {
        IconCache<string> cache = NewCache(onError: (_, _) => throw new InvalidOperationException("handler"));

        Task<string?> failed = cache.GetAsync("a", () => throw new InvalidOperationException("boom"));
        _worker.RunAll();

        Assert.Null(await failed);
    }

    [Fact]
    public async Task A_worker_that_cannot_be_reached_gives_no_icon_and_the_key_can_be_asked_again()
    {
        var errors = new List<string>();
        bool reachable = false;
        var cache = new IconCache<string>(10, 10, work =>
        {
            if (!reachable)
            {
                throw new InvalidOperationException("no worker");
            }
            _worker.Schedule(work);
        }, (key, e) => errors.Add(key + ": " + e.Message));

        Assert.Null(await cache.GetAsync("a", Load("a")));
        Assert.Equal(["a: no worker"], errors);
        Assert.Equal(0, cache.Waiting);

        reachable = true;
        Assert.Equal("icon of a", await RunAndGet(cache.GetAsync("a", Load("a"))));
    }

    [Fact]
    public void The_least_recently_used_icon_is_evicted_when_full()
    {
        IconCache<string> cache = NewCache(capacity: 2);
        _ = cache.GetAsync("a", Load("a"));
        _worker.RunAll();
        _ = cache.GetAsync("b", Load("b"));
        _worker.RunAll();
        Assert.True(cache.TryGet("a", out _));

        _ = cache.GetAsync("c", Load("c"));
        _worker.RunAll();

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public async Task Continuations_do_not_run_inline_on_the_worker()
    {
        IconCache<string> cache = NewCache();
        int workerThread = Environment.CurrentManagedThreadId;
        bool working = false;
        Task<string?> icon = cache.GetAsync("a", Load("a"));
        var ranInline = new TaskCompletionSource<bool>();
        _ = icon.ContinueWith(
            _ => ranInline.SetResult(Volatile.Read(ref working) && Environment.CurrentManagedThreadId == workerThread),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Volatile.Write(ref working, true);
        _worker.RunAll();
        Volatile.Write(ref working, false);

        Assert.False(await ranInline.Task);
    }

    [Fact]
    public async Task Loads_from_many_threads_at_once_each_run_once()
    {
        int loads = 0;
        var cache = new IconCache<string>(capacity: 100, maxWaiting: 100, action => Task.Run(action));

        // Task.Run unwraps the task GetAsync returns: each one completes with the icon.
        string?[] results = await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            cache.GetAsync("key " + (i % 10), () =>
            {
                Interlocked.Increment(ref loads);
                Thread.Sleep(5);
                return "icon";
            }))));

        Assert.All(results, result => Assert.Equal("icon", result));
        Assert.Equal(10, loads);
    }

    private async Task<string?> RunAndGet(Task<string?> icon)
    {
        _worker.RunAll();
        return await icon;
    }
}
