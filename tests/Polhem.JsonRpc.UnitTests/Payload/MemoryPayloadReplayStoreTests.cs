using System.ComponentModel;
using Polhem.JsonRpc.Payload.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class MemoryPayloadReplayStoreTests
{
    [Fact]
    [DisplayName("Replay store: a number is accepted once per scope")]
    public async Task TryAccept_SameNumber_AcceptedOncePerScope()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.True(await store.TryAcceptAsync("a", 5));
        Assert.False(await store.TryAcceptAsync("a", 5));
        Assert.True(await store.TryAcceptAsync("b", 5));
    }

    [Fact]
    [DisplayName("Replay store: numbers may arrive out of order within the window, but not behind it")]
    public async Task TryAccept_OutOfOrder_WithinWindowOnly()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.True(await store.TryAcceptAsync("a", 100));
        Assert.True(await store.TryAcceptAsync("a", 99));
        Assert.True(await store.TryAcceptAsync("a", 37));
        Assert.False(await store.TryAcceptAsync("a", 36));
    }

    [Fact]
    [DisplayName("Replay store: a negative number or an excessive forward jump is refused")]
    public async Task TryAccept_NegativeOrHugeJump_Refused()
    {
        var store = new MemoryPayloadReplayStore();

        Assert.False(await store.TryAcceptAsync("a", -1));
        Assert.True(await store.TryAcceptAsync("a", 0));
        Assert.False(await store.TryAcceptAsync("a", 1_000_001));
    }

    [Fact]
    [DisplayName("Replay store: a scope idle longer than its lifetime is forgotten")]
    public async Task TryAccept_IdleScope_IsSwept()
    {
        var clock = new ManualClock();
        var store = new MemoryPayloadReplayStore(TimeSpan.FromMinutes(10), clock);
        await store.TryAcceptAsync("a", 1);

        clock.Advance(TimeSpan.FromMinutes(11));
        await store.TryAcceptAsync("b", 1);

        Assert.Equal(1, store.Count);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
