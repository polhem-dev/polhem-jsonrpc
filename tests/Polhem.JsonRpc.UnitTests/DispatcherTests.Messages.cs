using System.Text.Json;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests;

public partial class DispatcherTests
{
    [Fact(DisplayName = "A batch larger than MaxBatchSize is answered with a single -32600")]
    public async Task DispatchMessageAsync_BatchTooLarge_ReturnsSingleInvalidRequest()
    {
        var dispatcher = DispatcherFixture.Create(o => o.MaxBatchSize = 2);
        const string Call = """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 1, "subtrahend": 1}, "id": 1}""";

        using var answer = await DispatcherFixture.RunAsync(dispatcher, $"[{Call},{Call},{Call}]");

        Assert.Equal(JsonValueKind.Object, answer!.RootElement.ValueKind);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, answer.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory(DisplayName = "DispatchBatchAsync answers an empty batch or one larger than MaxBatchSize with a single -32600")]
    [InlineData(0)]
    [InlineData(3)]
    public async Task DispatchBatchAsync_EmptyOrTooLarge_ReturnsSingleInvalidRequest(int count)
    {
        var dispatcher = DispatcherFixture.Create(o => o.MaxBatchSize = 2);
        var requests = Enumerable.Range(1, count)
            .Select(i => new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(i)))
            .ToList();

        var responses = await dispatcher.DispatchBatchAsync(requests, DispatcherFixture.Http());

        var response = Assert.Single(responses);
        Assert.Equal(JsonRpcErrorCodes.InvalidRequest, response.Error!.Code);
        Assert.Equal(JsonRpcId.Null, response.Id);
    }

    [Fact(DisplayName = "A message nested deeper than the JSON reader allows is answered with -32700")]
    public async Task DispatchMessageAsync_NestedTooDeep_ReturnsParseError()
    {
        var nested = new string('[', 70) + new string(']', 70);
        var json = $$"""{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": {{nested}}}, "id": 1}""";

        using var answer = await DispatcherFixture.RunAsync(DispatcherFixture.Create(), json);

        Assert.Equal(JsonRpcErrorCodes.ParseError, answer!.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.RootElement.GetProperty("id").ValueKind);
    }

    [Fact(DisplayName = "A call the caller cancelled propagates the cancellation instead of answering -32603")]
    public async Task DispatchAsync_CallerCancelled_Throws()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancellationObservingFilter()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var request = new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(request, DispatcherFixture.Http(), cancelled.Token));
    }

    [Fact(DisplayName = "A cancellation the caller did not ask for is an internal error like any other exception")]
    public async Task DispatchAsync_CancellationNotRequested_ReturnsInternalError()
    {
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancellationObservingFilter()));

        var response = await CallAsync(dispatcher, "Spec.Subtract", """{"minuend": 1, "subtrahend": 1}""");

        Assert.Equal(JsonRpcErrorCodes.InternalError, response.Error!.Code);
    }

    [Theory(DisplayName = "The calls of a batch share MessageItems, and calls sent one at a time do not")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MessageItems_BatchOrSingle_SharedOnlyWithinTheMessage(bool asBatch)
    {
        var seen = new List<int>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new MessageCountingFilter(seen)));
        const string Call = """{"jsonrpc": "2.0", "method": "Spec.Subtract", "params": {"minuend": 1, "subtrahend": 1}, "id": 1}""";

        if (asBatch)
        {
            await DispatcherFixture.RunAsync(dispatcher, $"[{Call},{Call},{Call}]");
        }
        else
        {
            for (var i = 0; i < 3; i++) { await DispatcherFixture.RunAsync(dispatcher, Call); }
        }

        Assert.Equal(asBatch ? [1, 2, 3] : [1, 1, 1], seen);
    }

    [Fact(DisplayName = "A transport that leaves the kind at its default is HTTP, never in-process")]
    public void TransportKind_Default_IsNotInProcess()
    {
        Assert.Equal(JsonRpcTransportKind.Http, default);
        Assert.NotEqual(JsonRpcTransportKind.InProcess, new JsonRpcTransportInfo(default).Kind);
    }

    [Fact(DisplayName = "InProcessTransport marks its calls as in-process")]
    public async Task InProcessTransport_SendAsync_MarksInProcess()
    {
        var seen = new List<JsonRpcTransportKind>();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new KindRecordingFilter(seen)));
        var request = new JsonRpcRequest("Spec.Subtract", DispatcherFixture.Element("""{"minuend": 1, "subtrahend": 1}"""), JsonRpcId.FromNumber(1));

        await new InProcessTransport(dispatcher).SendAsync(request);

        Assert.Equal([JsonRpcTransportKind.InProcess], seen);
    }

    [Theory(DisplayName = "A batch whose caller cancels stops before its next call")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchBatch_CancelledMidway_StopsBeforeNextCall(bool asMessage)
    {
        using var cancellation = new CancellationTokenSource();
        var dispatcher = DispatcherFixture.Create(o => o.Filters.Add(new CancelAfterCallFilter(cancellation)));
        var first = Guid.NewGuid().ToString();
        var second = Guid.NewGuid().ToString();
        var requests = new[] { first, second }
            .Select((text, i) => new JsonRpcRequest("Spec.Update", DispatcherFixture.Element($$"""{"text": "{{text}}"}"""), JsonRpcId.FromNumber(i)))
            .ToList();

        Task run = asMessage
            ? dispatcher.DispatchMessageAsync(JsonRpcSerializer.SerializeRequests(requests), DispatcherFixture.Http(), cancellation.Token)
            : dispatcher.DispatchBatchAsync(requests, DispatcherFixture.Http(), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Contains(first, SpecTarget.Updates);
        Assert.DoesNotContain(second, SpecTarget.Updates);
    }

    private sealed class MessageCountingFilter(List<int> seen) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
        {
            var count = (context.MessageItems.TryGetValue("count", out var value) ? (int)value! : 0) + 1;
            context.MessageItems["count"] = count;
            seen.Add(count);
            return next(context);
        }
    }
}
