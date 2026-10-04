using System.Text.Json;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    [Theory(DisplayName = "Payload server: a method that requires unique sequence numbers refuses plain and encoded calls, whose frame anybody can write")]
    [InlineData(PayloadFormat.Plain)]
    [InlineData(PayloadFormat.Encoded)]
    public async Task Call_UniqueSequenceRequiredButNotEncrypted_IsRefused(PayloadFormat format)
    {
        var policy = new TestPolicy { ReplayScope = "session-1", UniqueSequence = true };
        var (rpc, client) = CreateWithoutMapper(policy);
        var parameters = client.WrapRequest(Subtract, new SubtractRequest(5, 3), format, sequence: 1);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        Assert.Equal(0, policy.KeyRequests);
    }

    [Fact(DisplayName = "Payload server: an encoded call is accepted when the method does not require unique sequence numbers")]
    public async Task Call_EncodedWithoutUniqueSequence_IsAccepted()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "session-1", UniqueSequence = false });

        var result = await rpc.InvokeAsync<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encoded, sequence: 1));

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.UnwrapResult(Subtract, result)).Difference);
    }

    [Fact(DisplayName = "Payload server: the calls of a batch share one decompression budget, and calls sent one at a time each get their own")]
    public async Task Batch_DecompressionBudget_IsSharedByTheMessage()
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), MaxDecompressedBytesPerMessage = 1024 * 1024 };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);
        // Each body decompresses to about 700 KB, so one fits the budget and two do not.
        JsonElement Large(int sequence) => client.WrapRequest("Spec.Update", new UpdateRequest(new string('a', 700 * 1024)), PayloadFormat.Encoded, sequence: sequence);

        await rpc.InvokeAsync<JsonElement>("Spec.Update", Large(1));
        await rpc.InvokeAsync<JsonElement>("Spec.Update", Large(2));
        var batch = rpc.CreateBatch();
        var first = batch.Add<JsonElement>("Spec.Update", Large(3));
        var second = batch.Add<JsonElement>("Spec.Update", Large(4));
        await batch.SendAsync();

        await first;
        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => second);
        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
    }

    [Fact(DisplayName = "Payload server: the per-message decompression limit must be positive")]
    public void MaxDecompressedBytesPerMessage_NotPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PayloadOptions { MaxDecompressedBytesPerMessage = 0 });
    }

    [Theory(DisplayName = "Payload server: without frames or a replay scope nothing is checked, so a plain or encoded call to a method that requires unique sequence numbers runs, as in 1.0")]
    [InlineData(false, "session-1", PayloadFormat.Plain)]
    [InlineData(true, null, PayloadFormat.Plain)]
    [InlineData(false, "session-1", PayloadFormat.Encoded)]
    [InlineData(true, null, PayloadFormat.Encoded)]
    public async Task Call_UniqueSequenceWithoutFramesOrScope_IsAccepted(bool requireFrame, string? scope, PayloadFormat format)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = requireFrame, TypeResolver = Registry() };
        var dispatcher = DispatcherFixture.Create(options =>
            options.UsePayload(payloadOptions, new TestPolicy { ReplayScope = scope, UniqueSequence = true }));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));

        var client = new PayloadProcessor(payloadOptions);

        var result = await rpc.InvokeAsync<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), format, sequence: 1));

        var difference = client.UnwrapResult(Subtract, result) switch
        {
            JsonElement plain => plain.GetProperty("difference").GetInt32(),
            var value => Assert.IsType<SubtractResponse>(value).Difference,
        };
        Assert.Equal(2, difference);
    }

    [Theory(DisplayName = "Payload server: frames required after UsePayload still make a method that requires unique sequence numbers refuse plain and encoded calls")]
    [InlineData(PayloadFormat.Plain)]
    [InlineData(PayloadFormat.Encoded)]
    public async Task Call_RequireFrameSetAfterUsePayload_RefusesUnencrypted(PayloadFormat format)
    {
        var payloadOptions = new PayloadOptions { TypeResolver = Registry() };
        var policy = new TestPolicy { ReplayScope = "session-1", UniqueSequence = true };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, policy));
        payloadOptions.RequireFrame = true;
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() =>
            rpc.InvokeAsync<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), format, sequence: 1)));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        Assert.Equal(0, policy.KeyRequests);
    }

    [Fact(DisplayName = "Payload server: a body that fails past the decompression budget uses up the rest of it, so the next compressed call of the batch is refused")]
    public async Task Batch_BodyFailingPastBudget_ExhaustsTheBudget()
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), MaxDecompressedBytesPerMessage = 1024 * 1024 };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);
        var batch = rpc.CreateBatch();
        var tooLarge = batch.Add<JsonElement>("Spec.Update",
            client.WrapRequest("Spec.Update", new UpdateRequest(new string('a', 1536 * 1024)), PayloadFormat.Encoded, sequence: 1));
        var small = batch.Add<JsonElement>("Spec.Update",
            client.WrapRequest("Spec.Update", new UpdateRequest("small"), PayloadFormat.Encoded, sequence: 2));

        await batch.SendAsync();

        await Assert.ThrowsAsync<JsonRpcErrorException>(() => tooLarge);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => small);
    }

    [Fact(DisplayName = "Payload server: a body whose decompression fails with any exception uses up the whole rest of the budget, however much is left")]
    public async Task Batch_DecompressionThrowsOtherException_ExhaustsTheBudget()
    {
        // The budget is far larger than what fails, so a fixed charge would leave room for the next call; only
        // charging the whole rest refuses it.
        var payloadOptions = new PayloadOptions
        {
            RequireFrame = true,
            TypeResolver = Registry(),
            Compressor = new FailingFirstCompressor(),
            MaxDecompressedBytesPerMessage = 16 * 1024 * 1024,
        };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);
        var batch = rpc.CreateBatch();
        var failing = batch.Add<JsonElement>("Spec.Update",
            client.WrapRequest("Spec.Update", new UpdateRequest("first"), PayloadFormat.Encoded, sequence: 1));
        var next = batch.Add<JsonElement>("Spec.Update",
            client.WrapRequest("Spec.Update", new UpdateRequest("second"), PayloadFormat.Encoded, sequence: 2));

        await batch.SendAsync();

        await Assert.ThrowsAsync<JsonRpcErrorException>(() => failing);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => next);
    }

    // Fails its first decompression with an exception that is not InvalidDataException, then decompresses as gzip.
    private sealed class FailingFirstCompressor : IPayloadCompressor
    {
        private readonly GzipPayloadCompressor _gzip = new();
        private int _calls;

        public string Name => _gzip.Name;

        public byte[] Compress(byte[] bytes) => _gzip.Compress(bytes);

        public byte[] Decompress(byte[] bytes) => Decompress(bytes, long.MaxValue);

        public byte[] Decompress(byte[] bytes, long maxDecompressedBytes) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("The first body fails.")
                : _gzip.Decompress(bytes, maxDecompressedBytes);
    }
}
