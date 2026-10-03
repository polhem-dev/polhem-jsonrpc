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
        var parameters = client.Wrap(new SubtractRequest(5, 3), format, sequence: 1);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        Assert.Equal(0, policy.KeyRequests);
    }

    [Fact(DisplayName = "Payload server: an encoded call is accepted when the method does not require unique sequence numbers")]
    public async Task Call_EncodedWithoutUniqueSequence_IsAccepted()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "session-1", UniqueSequence = false });

        var result = await rpc.InvokeAsync<JsonElement>(Subtract, client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encoded, sequence: 1));

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.Unwrap(result)).Difference);
    }

    [Fact(DisplayName = "Payload server: the calls of a batch share one decompression budget, and calls sent one at a time each get their own")]
    public async Task Batch_DecompressionBudget_IsSharedByTheMessage()
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), MaxDecompressedBytesPerMessage = 1024 * 1024 };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);
        // Each body decompresses to about 700 KB, so one fits the budget and two do not.
        JsonElement Large(int sequence) => client.Wrap(new UpdateRequest(new string('a', 700 * 1024)), PayloadFormat.Encoded, sequence: sequence);

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
}
