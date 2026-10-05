using System.Text.Json;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    [Fact(DisplayName = "Payload server: the replay store passed to UsePayload is the one asked, with the scope, the sequence number and the call's cancellation token")]
    public async Task Call_ReplayStorePassedToUsePayload_IsAsked()
    {
        var store = new RecordingReplayStore();
        var (rpc, client) = CreateWithStore(store, TimeProvider.System);
        var parameters = client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 7);
        using var cancellation = new CancellationTokenSource();

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters, cancellation.Token);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters, cancellation.Token));

        Assert.Equal([("session-1", 7L, true), ("session-1", 7L, true)], store.Asked);
    }

    [Fact(DisplayName = "Payload server: a frame outside the timestamp tolerance is refused before the replay store is asked")]
    public async Task Call_StaleTimestamp_StoreNotAsked()
    {
        var store = new RecordingReplayStore();
        var serverNow = DateTimeOffset.UtcNow;
        var (rpc, _) = CreateWithStore(store, new FixedClock(serverNow));
        var stale = new PayloadProcessor(new PayloadOptions
        {
            RequireFrame = true,
            TypeResolver = Registry(),
            TimeProvider = new FixedClock(serverNow.AddHours(-1)),
        }).WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, stale));

        Assert.Empty(store.Asked);
    }

    [Fact(DisplayName = "Payload server: the clock that checks request timestamps is the one set when UsePayload ran, not one set later")]
    public async Task Call_ClockChangedAfterUsePayload_KeepsTheFirstClock()
    {
        var serverNow = DateTimeOffset.UtcNow;
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), TimeProvider = new FixedClock(serverNow) };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        payloadOptions.TimeProvider = new FixedClock(serverNow.AddHours(1));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), TimeProvider = new FixedClock(serverNow) });

        var result = await rpc.InvokeAsync<JsonElement>(Subtract,
            client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1));

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.UnwrapResult(Subtract, PayloadFormat.Encrypted, result, s_key)).Difference);
    }

    [Fact(DisplayName = "Payload server: the built-in replay store remembers a scope for twice the timestamp tolerance, so a frame stamped ahead of the server is not accepted again once the scope is idle")]
    public async Task Call_FrameAheadReplayedAfterScopeIdle_IsRejected()
    {
        var serverClock = new ManualClock(DateTimeOffset.UtcNow);
        var (rpc, _) = Create(new TestPolicy { ReplayScope = "session-1", UniqueSequence = true }, serverClock);
        var tolerance = new PayloadOptions().FrameTimestampTolerance;
        var client = new PayloadProcessor(new PayloadOptions
        {
            RequireFrame = true,
            TypeResolver = Registry(),
            TimeProvider = new FixedClock(serverClock.GetUtcNow() + tolerance),
        });
        var parameters = client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters);
        serverClock.Advance(tolerance * 2);

        // The frame is still within the tolerance on the server's side, so only the store can refuse it; a new sequence
        // number with the same timestamp is accepted, which shows the refusal is the store's and not the timestamp's.
        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));
        Assert.Equal(ReplayRejected, ex.Code);
        await rpc.InvokeAsync<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 2));
    }

    private static (JsonRpcConnector Rpc, PayloadProcessor Client) CreateWithStore(IPayloadReplayStore store, TimeProvider clock)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), TimeProvider = clock };
        var policy = new TestPolicy { ReplayScope = "session-1", UniqueSequence = true };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, policy, store));
        return (new JsonRpcConnector(new InProcessTransport(dispatcher)), new PayloadProcessor(payloadOptions));
    }

    // Records what it is asked and remembers sequence numbers as the in-memory store does.
    private sealed class RecordingReplayStore : IPayloadReplayStore
    {
        private readonly MemoryPayloadReplayStore _inner = new(TimeSpan.FromMinutes(10), TimeProvider.System);

        public List<(string Scope, long Sequence, bool Cancellable)> Asked { get; } = [];

        public ValueTask<bool> TryAcceptAsync(string scope, long sequence, CancellationToken cancellationToken = default)
        {
            Asked.Add((scope, sequence, cancellationToken.CanBeCanceled));
            return _inner.TryAcceptAsync(scope, sequence, cancellationToken);
        }
    }

    // A clock the test moves, for both the time of day and the timestamps the store measures idle time with.
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            _now += by;
            _timestamp += by.Ticks;
        }
    }
}
