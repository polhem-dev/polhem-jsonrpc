using System.Text;
using System.Text.Json;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public partial class PayloadServerTests
{
    private const string SubtractJson = """{"minuend":5,"subtrahend":3}""";

    [Theory(DisplayName = "Payload server: the policy is asked in the order IPayloadServerPolicy describes, and a plain or encoded call never for a key")]
    [InlineData(PayloadFormat.Encoded, true, true, "s", "GetMinimumFormat,RequiresUniqueSequence,GetReplayScope")]
    [InlineData(PayloadFormat.Plain, true, true, null, "GetMinimumFormat,RequiresUniqueSequence,GetReplayScope")]
    [InlineData(PayloadFormat.Encoded, true, false, "s", "GetMinimumFormat,RequiresUniqueSequence")]
    [InlineData(PayloadFormat.Encoded, false, true, "s", "GetMinimumFormat")]
    [InlineData(PayloadFormat.Encrypted, true, true, "s", "GetMinimumFormat,GetKeyAsync,GetReplayScope,RequiresUniqueSequence")]
    [InlineData(PayloadFormat.Encrypted, true, true, null, "GetMinimumFormat,GetKeyAsync,GetReplayScope")]
    public async Task Call_PolicyQuestions_AskedInDocumentedOrder(PayloadFormat format, bool requireFrame, bool uniqueSequence, string? scope, string expected)
    {
        var policy = new TestPolicy { ReplayScope = scope, UniqueSequence = uniqueSequence };
        var payloadOptions = new PayloadOptions { RequireFrame = requireFrame, TypeResolver = Registry() };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, policy));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var client = new PayloadProcessor(payloadOptions);

        try
        {
            await rpc.InvokeAsync<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), format, key: s_key, sequence: 1));
        }
        catch (JsonRpcErrorException)
        {
            // A refused call is asked the same questions up to the refusal; the order is what is checked.
        }

        Assert.Equal(expected, string.Join(',', policy.Asked));
    }

    [Fact(DisplayName = "Payload server: an encrypted call replayed under another scope that shares its key is accepted there, which is why the scope must cover every holder of the key")]
    public async Task Call_ReplayedUnderAnotherScopeSharingTheKey_IsAccepted()
    {
        var policy = new TestPolicy { ReplayScope = "session-a", UniqueSequence = true };
        var (rpc, client) = CreateWithoutMapper(policy);
        var captured = client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1);
        await rpc.InvokeAsync<JsonElement>(Subtract, captured);
        await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, captured));

        policy.ReplayScope = "session-b";
        var replayed = await rpc.InvokeAsync<JsonElement>(Subtract, captured);

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.UnwrapResult(Subtract, PayloadFormat.Encrypted, replayed, s_key)).Difference);
    }

    [Fact(DisplayName = "Payload server: a body sent uncompressed is opened whatever the decompression limits say, the message's and the compressor's own")]
    public async Task Call_UncompressedBody_IgnoresDecompressionLimits()
    {
        // Both limits are far below the body's length.
        var (rpc, _) = CreateWithBudget(1, new GzipPayloadCompressor(8));

        var result = await rpc.InvokeAsync<JsonElement>(Subtract, UncompressedEnvelope(sequence: 1));

        Assert.Equal(PayloadFormat.Encoded, PayloadEnvelope.Read(result).Format);
    }

    [Theory(DisplayName = "Payload server: a body sent uncompressed takes exactly its length from the budget, so a compressed call of the same length after it fits only if the budget holds both")]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task Batch_UncompressedBody_DrawsItsLength(int spare, bool fits)
    {
        // The compressed call decompresses to the same JSON, so the two together need twice the length.
        var length = Encoding.UTF8.GetByteCount(SubtractJson);
        var (rpc, client) = CreateWithBudget((2 * length) + spare);
        var batch = rpc.CreateBatch();
        var uncompressed = batch.Add<JsonElement>(Subtract, UncompressedEnvelope(sequence: 1));
        var compressed = batch.Add<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encoded, sequence: 2));

        await batch.SendAsync();

        await uncompressed;
        if (fits)
        {
            Assert.Equal(2, Assert.IsType<SubtractResponse>(client.UnwrapResult(Subtract, PayloadFormat.Encoded, await compressed)).Difference);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => compressed);
            Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
        }
    }

    [Fact(DisplayName = "Payload server: a plain call draws nothing from the budget, so a compressed call after it still fits")]
    public async Task Batch_PlainCall_DrawsNothing()
    {
        var (rpc, client) = CreateWithBudget(Encoding.UTF8.GetByteCount(SubtractJson));
        var batch = rpc.CreateBatch();
        var plain = batch.Add<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Plain));
        var compressed = batch.Add<JsonElement>(Subtract, client.WrapRequest(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encoded, sequence: 2));

        await batch.SendAsync();

        await plain;
        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.UnwrapResult(Subtract, PayloadFormat.Encoded, await compressed)).Difference);
    }

    private static (JsonRpcConnector Rpc, PayloadProcessor Client) CreateWithBudget(long budget, IPayloadCompressor? compressor = null)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), MaxDecompressedBytesPerMessage = budget };
        if (compressor is not null) { payloadOptions.Compressor = compressor; }
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, new TestPolicy()));
        return (new JsonRpcConnector(new InProcessTransport(dispatcher)), new PayloadProcessor(payloadOptions));
    }

    private static JsonElement UncompressedEnvelope(long sequence) => new PayloadEnvelope
    {
        Format = PayloadFormat.Encoded,
        Body = new PayloadFrame(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), sequence).Prepend(Encoding.UTF8.GetBytes(SubtractJson)),
        TypeName = Registry().GetTypeName(typeof(SubtractRequest)),
    }.ToElement();

    private sealed class TestPolicy : IPayloadServerPolicy
    {
        public byte[]? Key { get; init; } = s_key;

        public string? ReplayScope { get; set; }

        public bool UniqueSequence { get; init; }

        public PayloadFormat MinimumFormat { get; init; }

        public List<string> Asked { get; } = [];

        public int KeyRequests { get; private set; }

        public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context)
        {
            Asked.Add(nameof(GetMinimumFormat));
            return MinimumFormat;
        }

        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context)
        {
            Asked.Add(nameof(GetKeyAsync));
            KeyRequests++;
            return ValueTask.FromResult(Key);
        }

        public string? GetReplayScope(JsonRpcRequestContext context)
        {
            Asked.Add(nameof(GetReplayScope));
            return ReplayScope;
        }

        public bool RequiresUniqueSequence(JsonRpcRequestContext context)
        {
            Asked.Add(nameof(RequiresUniqueSequence));
            return UniqueSequence;
        }
    }
}
