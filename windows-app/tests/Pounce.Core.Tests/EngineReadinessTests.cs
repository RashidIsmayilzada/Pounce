using static Pounce.Core.Tests.Waits;

namespace Pounce.Core.Tests;

/// <summary>Waiting for a launched Engine to answer, with a probe the test answers by hand and a fake clock.</summary>
public class EngineReadinessTests
{
    private readonly ManualProbe _probe = new();
    private readonly FakeClock _clock = new();

    private EngineReadiness Readiness(TimeSpan? maxWait = null) =>
        new(_probe, maxWait: maxWait, delay: _clock.Delay, clock: () => _clock.Now);

    [Fact]
    public void Nothing_is_starting_before_it_is_told_anything() => SingleThreadContext.Run(_ =>
    {
        EngineReadiness readiness = Readiness();

        Assert.False(readiness.IsStarting);
        Assert.Equal(0, _probe.Asked);
        return Task.CompletedTask;
    });

    [Fact]
    public void A_launched_engine_is_starting_until_the_probe_first_succeeds() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        var changes = new List<bool>();
        readiness.Changed += (_, _) => changes.Add(readiness.IsStarting);

        readiness.Follow(EngineState.Running);
        Assert.True(readiness.IsStarting);
        (await _probe.NextAsk()).SetResult(false);
        (await _probe.NextAsk()).SetResult(false);
        (await _probe.NextAsk()).SetResult(true);
        await readiness.Until(r => !r.IsStarting);

        Assert.Equal([true, false], changes);
        Assert.Equal(TimeSpan.FromSeconds(1), readiness.ReadyAfter);
        Assert.Null(readiness.GaveUpAfter);
        Assert.Equal([EngineReadiness.DefaultInterval, EngineReadiness.DefaultInterval], _clock.Delays);
        Assert.Equal(3, _probe.Asked);
    });

    [Fact]
    public void Changes_are_announced_on_the_ui_thread_when_the_probe_answers_elsewhere() => SingleThreadContext.Run(async ui =>
    {
        EngineReadiness readiness = Readiness();
        var threads = new List<bool>();
        readiness.Changed += (_, _) => threads.Add(ui.IsCurrent);

        readiness.Follow(EngineState.Running);
        TaskCompletionSource<bool> ask = await _probe.NextAsk();
        await Task.Run(() => ask.SetResult(true));
        await readiness.Until(r => !r.IsStarting);

        Assert.Equal([true, true], threads);
    });

    [Theory]
    [InlineData(EngineStatus.Failed)]
    [InlineData(EngineStatus.Stopped)]
    public void A_failed_or_stopped_engine_ends_the_wait_and_the_polling(EngineStatus status) => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        Task<bool> waiting = readiness.WaitAsync();
        TaskCompletionSource<bool> ask = await _probe.NextAsk();
        int asked = _probe.Asked;

        readiness.Follow(status == EngineStatus.Failed ? EngineState.Failed("It broke.") : EngineState.Stopped);

        Assert.False(readiness.IsStarting);
        Assert.False(await waiting.WaitAsync(Patience));
        Assert.True(ask.Task.IsCanceled);
        Assert.Equal(asked, _probe.Asked);
        Assert.Null(readiness.ReadyAfter);
        Assert.Null(readiness.GaveUpAfter);
    });

    [Fact]
    public void An_engine_already_running_is_ready_at_once() => SingleThreadContext.Run(_ =>
    {
        EngineReadiness readiness = Readiness();
        readiness.Expect();

        readiness.Follow(EngineState.Reusing);

        Assert.False(readiness.IsStarting);
        Assert.Equal(0, _probe.Asked);
        return Task.CompletedTask;
    });

    [Fact]
    public void While_the_supervisor_decides_it_is_starting_without_probing() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();

        readiness.Expect();
        Assert.True(readiness.IsStarting);
        Assert.Equal(0, _probe.Asked);

        readiness.Follow(EngineState.Running);
        (await _probe.NextAsk()).SetResult(true);
        await readiness.Until(r => !r.IsStarting);
        Assert.Equal(TimeSpan.Zero, readiness.ReadyAfter);
    });

    [Fact]
    public void A_start_that_ends_without_a_decision_is_not_starting_any_more() => SingleThreadContext.Run(_ =>
    {
        // The supervisor was stopped while it probed: its state stays Stopped, with no StateChanged.
        EngineReadiness readiness = Readiness();
        readiness.Expect();

        readiness.Follow(EngineState.Stopped);

        Assert.False(readiness.IsStarting);
        return Task.CompletedTask;
    });

    [Fact]
    public void An_engine_launched_again_is_waited_for_again() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        readiness.Follow(EngineState.Running);
        (await _probe.NextAsk()).SetResult(true);
        await readiness.Until(r => !r.IsStarting);
        readiness.Follow(EngineState.Failed("It broke."));

        _clock.Now += TimeSpan.FromMinutes(5);
        readiness.Follow(EngineState.Running);

        Assert.True(readiness.IsStarting);
        Assert.Null(readiness.ReadyAfter);
        (await _probe.NextAsk()).SetResult(false);
        (await _probe.NextAsk()).SetResult(true);
        await readiness.Until(r => !r.IsStarting);
        Assert.Equal(EngineReadiness.DefaultInterval, readiness.ReadyAfter);
    });

    [Fact]
    public void The_same_state_twice_does_not_start_a_second_wait() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        readiness.Follow(EngineState.Running);
        TaskCompletionSource<bool> ask = await _probe.NextAsk();

        readiness.Follow(EngineState.Running);

        Assert.False(ask.Task.IsCompleted);
        Assert.Equal(1, _probe.Asked);
    });

    [Fact]
    public void A_new_wait_replaces_the_running_one_without_a_blink() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        int changes = 0;
        readiness.Changed += (_, _) => changes++;
        Task<bool> first = readiness.WaitAsync();
        TaskCompletionSource<bool> firstAsk = await _probe.NextAsk();

        Task<bool> second = readiness.WaitAsync();

        Assert.False(await first.WaitAsync(Patience));
        Assert.True(firstAsk.Task.IsCanceled);
        Assert.True(readiness.IsStarting);
        (await _probe.NextAsk()).SetResult(true);
        Assert.True(await second.WaitAsync(Patience));
        Assert.Equal(2, changes);
    });

    [Fact]
    public void It_gives_up_after_the_longest_wait() => SingleThreadContext.Run(async _ =>
    {
        var never = new AnsweringProbe(_ => false);
        var readiness = new EngineReadiness(never, maxWait: TimeSpan.FromSeconds(2), delay: _clock.Delay, clock: () => _clock.Now);

        Assert.False(await readiness.WaitAsync().WaitAsync(Patience));

        Assert.False(readiness.IsStarting);
        Assert.Equal(TimeSpan.FromSeconds(2), readiness.GaveUpAfter);
        Assert.Null(readiness.ReadyAfter);
        Assert.Equal(5, never.Asked);
    });

    [Fact]
    public void A_probe_that_throws_counts_as_not_answering_yet() => SingleThreadContext.Run(async _ =>
    {
        var flaky = new AnsweringProbe(call => call < 2 ? throw new InvalidOperationException("boom") : true);
        var readiness = new EngineReadiness(flaky, delay: _clock.Delay, clock: () => _clock.Now);

        Assert.True(await readiness.WaitAsync().WaitAsync(Patience));

        Assert.False(readiness.IsStarting);
        Assert.Equal(3, flaky.Asked);
    });

    [Fact]
    public void Cancelling_the_wait_ends_it() => SingleThreadContext.Run(async _ =>
    {
        EngineReadiness readiness = Readiness();
        using var cancel = new CancellationTokenSource();
        Task<bool> waiting = readiness.WaitAsync(cancel.Token);
        TaskCompletionSource<bool> ask = await _probe.NextAsk();

        await cancel.CancelAsync();

        Assert.False(await waiting.WaitAsync(Patience));
        Assert.False(readiness.IsStarting);
        Assert.True(ask.Task.IsCanceled);
    });

    /// <summary>Every question waits for the test to answer it; honours cancellation.</summary>
    private sealed class ManualProbe : IEngineProbe
    {
        private readonly System.Threading.Channels.Channel<TaskCompletionSource<bool>> _asks =
            System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource<bool>>();

        public int Asked { get; private set; }

        public Task<bool> IsAliveAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken));
            _asks.Writer.TryWrite(answer);
            return answer.Task;
        }

        /// <summary>The next question the readiness asks, once it has asked it.</summary>
        public async Task<TaskCompletionSource<bool>> NextAsk() => await _asks.Reader.ReadAsync().AsTask().WaitAsync(Patience);
    }

    /// <summary>Answers at once with <c>answer(call number)</c>, counting from 0.</summary>
    private sealed class AnsweringProbe(Func<int, bool> answer) : IEngineProbe
    {
        public int Asked { get; private set; }

        public Task<bool> IsAliveAsync(CancellationToken cancellationToken = default) => Task.FromResult(answer(Asked++));
    }

    /// <summary>Time that only moves when a delay is asked for: each delay completes at once and moves the clock on.</summary>
    private sealed class FakeClock
    {
        public TimeSpan Now { get; set; } = TimeSpan.FromHours(1);

        public List<TimeSpan> Delays { get; } = [];

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            Now += delay;
            return Task.CompletedTask;
        }
    }
}

internal static class ReadinessWaits
{
    /// <summary>Completes on the readiness's thread once <paramref name="condition"/> holds, checked after each change.</summary>
    public static Task Until(this EngineReadiness readiness, Func<EngineReadiness, bool> condition)
    {
        if (condition(readiness))
        {
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? check = null;
        check = (_, _) =>
        {
            if (condition(readiness))
            {
                readiness.Changed -= check;
                done.TrySetResult();
            }
        };
        readiness.Changed += check;
        return done.Task.WaitAsync(Patience);
    }
}
