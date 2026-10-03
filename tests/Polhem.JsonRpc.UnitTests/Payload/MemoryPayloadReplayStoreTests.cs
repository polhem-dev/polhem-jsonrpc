using Polhem.JsonRpc.Payload.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class MemoryPayloadReplayStoreTests
{
    [Fact(DisplayName = "Replay store: a number is accepted once per scope")]
    public async Task TryAccept_SameNumber_AcceptedOncePerScope()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.True(await store.TryAcceptAsync("a", 5));
        Assert.False(await store.TryAcceptAsync("a", 5));
        Assert.True(await store.TryAcceptAsync("b", 5));
    }

    [Fact(DisplayName = "Replay store: numbers may arrive out of order within the window, but not behind it")]
    public async Task TryAccept_OutOfOrder_WithinWindowOnly()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.True(await store.TryAcceptAsync("a", 100));
        Assert.True(await store.TryAcceptAsync("a", 99));
        Assert.True(await store.TryAcceptAsync("a", 37));
        Assert.False(await store.TryAcceptAsync("a", 36));
    }

    [Fact(DisplayName = "Replay store: a negative number or an excessive forward jump is refused")]
    public async Task TryAccept_NegativeOrHugeJump_Refused()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.False(await store.TryAcceptAsync("a", -1));
        Assert.True(await store.TryAcceptAsync("a", 0));
        Assert.False(await store.TryAcceptAsync("a", 1_000_001));
    }

    [Fact(DisplayName = "Replay store: a scope idle longer than its lifetime is forgotten")]
    public async Task TryAccept_IdleScope_IsSwept()
    {
        var clock = new ManualClock();
        var store = new MemoryPayloadReplayStore(TimeSpan.FromMinutes(10), clock);
        await store.TryAcceptAsync("a", 1);

        clock.Advance(TimeSpan.FromMinutes(11));
        await store.TryAcceptAsync("b", 1);

        Assert.Equal(1, store.Count);
    }

    [Fact(DisplayName = "Replay store: a number accepted while a sweep forgets its idle scope is still refused when sent again")]
    public async Task TryAccept_DuringSweepOfIdleScope_ReplayStillRefused()
    {
        for (var round = 0; round < 2000; round++)
        {
            var clock = new ManualClock();
            var store = new MemoryPayloadReplayStore(TimeSpan.FromMinutes(1), clock);
            await store.TryAcceptAsync("idle", 1);
            clock.Advance(TimeSpan.FromMinutes(2));

            // The first call into "other" sweeps "idle" while the second call accepts a number into it.
            using var start = new Barrier(2);
            var sweep = Task.Run(() => { start.SignalAndWait(); return store.TryAcceptAsync("other", 1).AsTask(); });
            var accept = Task.Run(() => { start.SignalAndWait(); return store.TryAcceptAsync("idle", 5).AsTask(); });
            await Task.WhenAll(sweep, accept);

            Assert.False(await store.TryAcceptAsync("idle", 5), $"replay accepted in round {round}");
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
