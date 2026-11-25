using System.Collections.Concurrent;

namespace Pounce.Core.Tests;

/// <summary>
/// A UI thread for tests: one thread runs every posted continuation in order, like WPF's Dispatcher, so the search
/// model's threading contract holds and the tests are deterministic.
/// </summary>
internal sealed class SingleThreadContext : SynchronizationContext
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(5);

    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

    private SingleThreadContext()
    {
    }

    /// <summary>The managed thread id of the "UI thread".</summary>
    public int ThreadId { get; } = Environment.CurrentManagedThreadId;

    /// <summary>True when called on the "UI thread".</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == ThreadId;

    /// <summary>Runs <paramref name="body"/> on this thread as its UI thread, until the task it returns completes.</summary>
    public static void Run(Func<SingleThreadContext, Task> body)
    {
        SynchronizationContext? previous = Current;
        var context = new SingleThreadContext();
        SetSynchronizationContext(context);
        try
        {
            Task task = body(context);
            task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
            while (true)
            {
                if (!context._queue.TryTake(out var item, Idle))
                {
                    if (context._queue.IsCompleted)
                    {
                        break;
                    }
                    throw new TimeoutException($"The test did nothing for {Idle.TotalSeconds} seconds.");
                }
                item.Callback(item.State);
            }
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        try
        {
            _queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            // The test has finished; a late continuation has nothing left to update.
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("Nothing in Pounce.Core may block on the UI thread.");

    public override SynchronizationContext CreateCopy() => this;
}
