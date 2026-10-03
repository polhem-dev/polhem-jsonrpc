using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadServerTests
{
    private const string Subtract = "Spec.Subtract";

    // The test maps each exception type to its own code, so a test that expects a rejection sees which one it got.
    private const int ReplayRejected = -32010;
    private const int Refused = -32011;

    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

    [Theory]
    [DisplayName("Payload server: a call in each format is answered in the same format and opens on the client")]
    [InlineData(PayloadFormat.Plain)]
    [InlineData(PayloadFormat.Encoded)]
    [InlineData(PayloadFormat.Encrypted)]
    public async Task Call_EachFormat_AnswersInSameFormat(PayloadFormat format)
    {
        var (rpc, client) = Create(new TestPolicy());

        var result = await rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new SubtractRequest(5, 3), format, key: s_key, sequence: 1));

        Assert.Equal(format, PayloadEnvelope.ReadFormat(result));
        var response = client.Unwrap(result, s_key);
        var difference = format == PayloadFormat.Plain
            ? ((JsonElement)response!).GetProperty("difference").GetInt32()
            : Assert.IsType<SubtractResponse>(response).Difference;
        Assert.Equal(2, difference);
    }

    [Fact]
    [DisplayName("Payload server: the result names the codec the request named")]
    public async Task Call_NamedCodec_ResultNamesSameCodec()
    {
        var (rpc, client) = Create(new TestPolicy());

        var result = await rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encoded, codec: "json"));

        Assert.Equal("json", result.GetProperty("codec").GetString());
    }

    [Fact]
    [DisplayName("Payload server: a sequence number the scope already used is rejected")]
    public async Task Call_RepeatedSequence_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy { ReplayScope = "session-1", UniqueSequence = true });
        var parameters = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 10);

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));
        Assert.Equal(ReplayRejected, ex.Code);
    }

    [Fact]
    [DisplayName("Payload server: a repeated sequence number passes when the method does not require unique ones")]
    public async Task Call_RepeatedSequenceNotRequired_IsAccepted()
    {
        var (rpc, client) = Create(new TestPolicy { ReplayScope = "session-1", UniqueSequence = false });
        var parameters = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 10);

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters);
        var again = await rpc.InvokeAsync<JsonElement>(Subtract, parameters);

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.Unwrap(again, s_key)).Difference);
    }

    [Fact]
    [DisplayName("Payload server: a frame whose timestamp is outside the tolerance is rejected")]
    public async Task Call_StaleTimestamp_IsRejected()
    {
        var (rpc, _) = Create(new TestPolicy());
        var body = new GzipPayloadCompressor().Compress(Encoding.UTF8.GetBytes("""{"minuend":5,"subtrahend":3}"""));
        var stale = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var envelope = new PayloadEnvelope
        {
            Format = PayloadFormat.Encrypted,
            Body = new AesCbcHmacPayloadEncryptor().Encrypt(new PayloadFrame(stale, 1).Prepend(body), s_key),
            TypeName = Registry().GetTypeName(typeof(SubtractRequest)),
        };

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, envelope.ToElement()));
        Assert.Equal(ReplayRejected, ex.Code);
    }

    [Fact]
    [DisplayName("Payload server: an encrypted call fails when the application has no key for the caller")]
    public async Task Call_NoKey_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy { Key = null });

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1)));
        Assert.Equal(Refused, ex.Code);
    }

    [Fact]
    [DisplayName("Payload server: a body naming another type than the parameter is rejected")]
    public async Task Call_ForeignTypeName_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy());

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new UpperRequest("a"), PayloadFormat.Encoded, sequence: 1)));
        Assert.Equal(Refused, ex.Code);
    }

    [Fact]
    [DisplayName("Payload server: a filter added after the payload sees the opened request and can replace the result")]
    public async Task Call_InnerFilter_SeesRequestAndReplacesResult()
    {
        var payloadOptions = new PayloadOptions { TypeResolver = Registry() };
        PayloadRequest? seen = null;
        var dispatcher = DispatcherFixture.Create(options =>
        {
            options.UsePayload(payloadOptions, new TestPolicy());
            options.Filters.Add(new DelegateFilter(async (context, next) =>
            {
                seen = PayloadRequest.Find(context);
                await next(context);
                context.ReturnValue = new SubtractResponse(42);
            }));
        });
        var client = new PayloadProcessor(payloadOptions);
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));

        var result = await rpc.InvokeAsync<JsonElement>(Subtract, client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encoded));

        Assert.Equal(5, Assert.IsType<SubtractRequest>(seen!.Value).Minuend);
        Assert.Equal(42, Assert.IsType<SubtractResponse>(client.Unwrap(result)).Difference);
    }

    private static PayloadTypeRegistry Registry() => new PayloadTypeRegistry()
        .Register<SubtractRequest>().Register<SubtractResponse>().Register<UpperRequest>();

    private static (JsonRpcConnector Rpc, PayloadProcessor Client) Create(TestPolicy policy)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry() };
        var dispatcher = DispatcherFixture.Create(options =>
        {
            options.UsePayload(payloadOptions, policy);
            options.ExceptionMapper = (exception, _) => exception switch
            {
                ReplayRejectedException => new JsonRpcError(ReplayRejected, "Replay rejected"),
                InvalidOperationException => new JsonRpcError(Refused, "Refused"),
                _ => null,
            };
        });
        return (new JsonRpcConnector(new InProcessTransport(dispatcher)), new PayloadProcessor(payloadOptions));
    }

    private sealed class TestPolicy : IPayloadServerPolicy
    {
        public byte[]? Key { get; init; } = s_key;

        public string? ReplayScope { get; init; }

        public bool UniqueSequence { get; init; }

        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult(Key);

        public string? GetReplayScope(JsonRpcRequestContext context) => ReplayScope;

        public bool RequiresUniqueSequence(JsonRpcRequestContext context) => UniqueSequence;
    }

    private sealed class DelegateFilter(Func<JsonRpcRequestContext, JsonRpcFilterDelegate, ValueTask> invoke) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next) => invoke(context, next);
    }
}
