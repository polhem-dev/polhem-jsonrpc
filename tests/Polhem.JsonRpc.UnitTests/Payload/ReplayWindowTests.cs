using System.Collections.Concurrent;
using System.ComponentModel;
using Polhem.JsonRpc.Payload.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

/// <summary>
/// The sliding window behind <see cref="MemoryPayloadReplayStore"/>: the last 64 numbers below the highest one seen,
/// with forward jumps limited to 1,000,000. Moved from the Polhem framework's tests with the code.
/// </summary>
public class ReplayWindowTests
{
    private const int WindowSize = 64;
    private const long MaxForwardJump = 1_000_000;
    private const string Scope = "session";

    [Fact]
    [DisplayName("Replay window: the first sequence number is accepted whatever its size and becomes the baseline")]
    public async Task FirstSequence_IsAccepted()
    {
        Assert.True(await new MemoryPayloadReplayStore().TryAcceptAsync(Scope, 5_000));
    }

    [Fact]
    [DisplayName("Replay window: increasing sequence numbers are all accepted")]
    public async Task IncreasingSequences_AllAccepted()
    {
        var store = new MemoryPayloadReplayStore();
        for (long i = 1; i <= 500; i++)
        {
            Assert.True(await store.TryAcceptAsync(Scope, i), $"sequence {i} should be accepted");
        }
    }

    [Fact]
    [DisplayName("Replay window: a forward jump beyond the window width clears it, so earlier numbers can no longer be used")]
    public async Task JumpBeyondWindow_ClearsEarlierSlots()
    {
        var store = new MemoryPayloadReplayStore();
        await store.TryAcceptAsync(Scope, 1);
        await store.TryAcceptAsync(Scope, 2);

        Assert.True(await store.TryAcceptAsync(Scope, 2 + WindowSize));
        Assert.False(await store.TryAcceptAsync(Scope, 2));
    }

    [Fact]
    [DisplayName("Replay window: a forward jump exactly at the limit is accepted")]
    public async Task JumpExactlyAtLimit_IsAccepted()
    {
        var store = new MemoryPayloadReplayStore();
        await store.TryAcceptAsync(Scope, 1);

        Assert.True(await store.TryAcceptAsync(Scope, 1 + MaxForwardJump));
    }

    [Fact]
    [DisplayName("Replay window: a jump beyond the limit is refused and leaves the window usable")]
    public async Task JumpBeyondLimit_IsRefusedWithoutLockingTheScope()
    {
        // Without a limit, one arithmetic mistake on a client that sends a number near `long.MaxValue` would put every
        // later request of that session outside the window, all failing with a valid key, which is hard to diagnose.
        var store = new MemoryPayloadReplayStore();
        await store.TryAcceptAsync(Scope, 1);

        Assert.False(await store.TryAcceptAsync(Scope, long.MaxValue));
        Assert.True(await store.TryAcceptAsync(Scope, 2));
    }

    [Fact]
    [DisplayName("Replay window: the same sequence number sent concurrently is accepted exactly once")]
    public void SameSequenceConcurrently_AcceptedExactlyOnce()
    {
        // Concurrent requests of the same session share the window, so the read-modify-write must be atomic.
        var store = new MemoryPayloadReplayStore();
        var results = new ConcurrentBag<bool>();

        Parallel.For(0, 200, _ => results.Add(store.TryAcceptAsync(Scope, 7).AsTask().GetAwaiter().GetResult()));

        Assert.Single(results, accepted => accepted);
    }

    [Fact]
    [DisplayName("Replay window: distinct numbers within the window width sent concurrently are all accepted")]
    public void DistinctSequencesConcurrently_AllAccepted()
    {
        var store = new MemoryPayloadReplayStore();
        var results = new ConcurrentBag<bool>();

        Parallel.For(0, WindowSize, i => results.Add(store.TryAcceptAsync(Scope, i).AsTask().GetAwaiter().GetResult()));

        Assert.Equal(WindowSize, results.Count(accepted => accepted));
    }

    [Fact]
    [DisplayName("Replay window: a cancelled call throws and records nothing")]
    public async Task CancelledCall_RecordsNothing()
    {
        var store = new MemoryPayloadReplayStore();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryAcceptAsync(Scope, 3, cancelled.Token).AsTask());

        Assert.True(await store.TryAcceptAsync(Scope, 3));
    }

    [Fact]
    [DisplayName("Replay window: a scope in continuous use is never forgotten, so replay protection is not silently reset")]
    public async Task ScopeInContinuousUse_IsKept()
    {
        var clock = new ManualClock();
        var store = new MemoryPayloadReplayStore(TimeSpan.FromMinutes(10), clock);
        await store.TryAcceptAsync(Scope, 1);

        for (var minute = 0; minute < 30; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await store.TryAcceptAsync(Scope, 100 + minute);
        }

        Assert.False(await store.TryAcceptAsync(Scope, 129));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
