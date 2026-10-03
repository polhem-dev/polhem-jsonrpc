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

    [Theory(DisplayName = "Payload server: a call in each format is answered in the same format and opens on the client")]
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

    [Fact(DisplayName = "Payload server: the result names the codec the request named")]
    public async Task Call_NamedCodec_ResultNamesSameCodec()
    {
        var (rpc, client) = Create(new TestPolicy());

        var result = await rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encoded, codec: "json"));

        Assert.Equal("json", result.GetProperty("codec").GetString());
    }

    [Fact(DisplayName = "Payload server: a sequence number the scope already used is rejected")]
    public async Task Call_RepeatedSequence_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy { ReplayScope = "session-1", UniqueSequence = true });
        var parameters = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 10);

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));
        Assert.Equal(ReplayRejected, ex.Code);
    }

    [Fact(DisplayName = "Payload server: a repeated sequence number passes when the method does not require unique ones")]
    public async Task Call_RepeatedSequenceNotRequired_IsAccepted()
    {
        var (rpc, client) = Create(new TestPolicy { ReplayScope = "session-1", UniqueSequence = false });
        var parameters = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 10);

        await rpc.InvokeAsync<JsonElement>(Subtract, parameters);
        var again = await rpc.InvokeAsync<JsonElement>(Subtract, parameters);

        Assert.Equal(2, Assert.IsType<SubtractResponse>(client.Unwrap(again, s_key)).Difference);
    }

    [Fact(DisplayName = "Payload server: a frame whose timestamp is outside the tolerance is rejected")]
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

    [Theory(DisplayName = "Payload server: a frame is accepted up to the timestamp tolerance on either side of the server clock, and refused beyond it")]
    [InlineData(-299, true)]
    [InlineData(299, true)]
    [InlineData(-301, false)]
    [InlineData(301, false)]
    public async Task Call_TimestampAtToleranceEdge_IsAcceptedOrRejected(int clientClockOffsetSeconds, bool accepted)
    {
        var serverNow = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var (rpc, _) = Create(new TestPolicy(), new FixedClock(serverNow));
        var client = new PayloadProcessor(new PayloadOptions
        {
            RequireFrame = true,
            TypeResolver = Registry(),
            TimeProvider = new FixedClock(serverNow.AddSeconds(clientClockOffsetSeconds)),
        });
        var parameters = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        if (accepted)
        {
            var result = await rpc.InvokeAsync<JsonElement>(Subtract, parameters);
            Assert.Equal(2, Assert.IsType<SubtractResponse>(client.Unwrap(result, s_key)).Difference);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));
            Assert.Equal(ReplayRejected, ex.Code);
        }
    }

    [Theory(DisplayName = "Payload server: a call the method policy refuses is answered -32601 without fetching a key or decrypting")]
    [InlineData("Spec.Subtract", 1)]
    [InlineData("Spec.Echo", 0)]
    public async Task Call_PolicyRefusedMethod_IsNotDecrypted(string method, int expectedDecryptions)
    {
        var encryptor = new CountingEncryptor();
        var policy = new TestPolicy();
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), Encryptor = encryptor };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, policy));
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));
        var parameters = new PayloadProcessor(payloadOptions)
            .Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        if (expectedDecryptions == 0)
        {
            var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(method, parameters));
            Assert.Equal(JsonRpcErrorCodes.MethodNotFound, ex.Code);
        }
        else
        {
            await rpc.InvokeAsync<JsonElement>(method, parameters);
        }

        Assert.Equal(expectedDecryptions, encryptor.Decryptions);
        Assert.Equal(expectedDecryptions, policy.KeyRequests);
    }

    [Theory(DisplayName = "Payload server: a call with no value to bind is invalid params, as an absent params is without the payload packages")]
    [InlineData(null)]
    [InlineData("""{"format": 0}""")]
    [InlineData("""{"format": 0, "value": null}""")]
    public async Task Call_NoValue_ReturnsInvalidParams(string? parameters)
    {
        var (rpc, _) = Create(new TestPolicy());

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            parameters is null ? null : DispatcherFixture.Element(parameters)));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
    }

    [Theory(DisplayName = "Payload server: a plain value of the wrong shape is invalid params, as it is without the payload packages")]
    [InlineData("Spec.Numbers", "[5, 3]")]
    [InlineData("Spec.Subtract", """{"minuend": "not a number"}""")]
    [InlineData("Spec.Abstract", """{"text": "x"}""")]
    public async Task Call_PlainParamsOfWrongShape_ReturnsInvalidParamsLikeDefaultBinder(string method, string value)
    {
        var (rpc, _) = Create(new TestPolicy());
        var plain = new JsonRpcConnector(new InProcessTransport(DispatcherFixture.Create()));
        var envelope = DispatcherFixture.Element($$"""{"format": 0, "value": {{value}}}""");

        var withPayload = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(method, envelope));
        var without = await Assert.ThrowsAsync<JsonRpcErrorException>(
            () => plain.InvokeAsync<JsonElement>(method, DispatcherFixture.Element(value)));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, without.Code);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, withPayload.Code);
    }

    [Theory(DisplayName = "Payload server: a call below the method's minimum format is refused with -32602 before a key is asked for")]
    [InlineData(PayloadFormat.Plain, false)]
    [InlineData(PayloadFormat.Encoded, false)]
    [InlineData(PayloadFormat.Encrypted, true)]
    public async Task Call_BelowMinimumFormat_IsRefused(PayloadFormat format, bool accepted)
    {
        var policy = new TestPolicy { MinimumFormat = PayloadFormat.Encrypted };
        var (rpc, client) = CreateWithoutMapper(policy);
        var parameters = client.Wrap(new SubtractRequest(5, 3), format, key: s_key, sequence: 1);

        if (accepted)
        {
            await rpc.InvokeAsync<JsonElement>(Subtract, parameters);
            Assert.Equal(1, policy.KeyRequests);
        }
        else
        {
            var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, parameters));
            Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
            Assert.Equal(0, policy.KeyRequests);
        }
    }

    [Fact(DisplayName = "Payload server: without a mapper of the host's, a malformed envelope is -32602")]
    public async Task Call_MalformedEnvelopeWithoutHostMapper_ReturnsInvalidParams()
    {
        var (rpc, _) = CreateWithoutMapper(new TestPolicy());

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            DispatcherFixture.Element("""{"format": 1, "value": "not Base64!"}""")));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
    }

    [Fact(DisplayName = "Payload server: without a mapper of the host's, a failed HMAC and a replay both answer the same -32603")]
    public async Task Call_SecurityFailuresWithoutHostMapper_AreIndistinguishable()
    {
        var (rpc, client) = CreateWithoutMapper(new TestPolicy { ReplayScope = "s", UniqueSequence = true });
        var forged = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted,
            key: RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize), sequence: 1);
        var genuine = client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 2);
        await rpc.InvokeAsync<JsonElement>(Subtract, genuine);

        var mac = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, forged));
        var replay = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract, genuine));

        Assert.Equal((JsonRpcErrorCodes.InternalError, "Internal error"), (mac.Code, mac.Message));
        Assert.Equal((mac.Code, mac.Message), (replay.Code, replay.Message));
    }

    [Theory(DisplayName = "Payload server: a host mapper set before UsePayload answers first, and what it leaves is mapped by the package")]
    [InlineData(true, -32099)]
    [InlineData(false, JsonRpcErrorCodes.InvalidParams)]
    public async Task Call_HostMapperBeforeUsePayload_AnswersFirst(bool hostMapsIt, int expectedCode)
    {
        var dispatcher = DispatcherFixture.Create(options =>
        {
            options.ExceptionMapper = (exception, _) =>
                hostMapsIt && exception is InvalidPayloadException ? new JsonRpcError(-32099, "Host") : null;
            options.UsePayload(new PayloadOptions { TypeResolver = Registry() }, new TestPolicy());
        });
        var rpc = new JsonRpcConnector(new InProcessTransport(dispatcher));

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            DispatcherFixture.Element("""{"format": 7}""")));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact(DisplayName = "Payload server: an encrypted call fails when the application has no key for the caller")]
    public async Task Call_NoKey_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy { Key = null });

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new SubtractRequest(5, 3), PayloadFormat.Encrypted, key: s_key, sequence: 1)));
        Assert.Equal(Refused, ex.Code);
    }

    [Fact(DisplayName = "Payload server: a body naming another type than the parameter is rejected")]
    public async Task Call_ForeignTypeName_IsRejected()
    {
        var (rpc, client) = Create(new TestPolicy());

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>(Subtract,
            client.Wrap(new UpperRequest("a"), PayloadFormat.Encoded, sequence: 1)));
        Assert.Equal(Refused, ex.Code);
    }

    [Fact(DisplayName = "Payload server: a filter added after the payload sees the opened request and can replace the result")]
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

    private static (JsonRpcConnector Rpc, PayloadProcessor Client) CreateWithoutMapper(TestPolicy policy)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry() };
        var dispatcher = DispatcherFixture.Create(options => options.UsePayload(payloadOptions, policy));
        return (new JsonRpcConnector(new InProcessTransport(dispatcher)), new PayloadProcessor(payloadOptions));
    }

    private static (JsonRpcConnector Rpc, PayloadProcessor Client) Create(TestPolicy policy, TimeProvider? clock = null)
    {
        var payloadOptions = new PayloadOptions { RequireFrame = true, TypeResolver = Registry(), TimeProvider = clock ?? TimeProvider.System };
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

        public PayloadFormat MinimumFormat { get; init; }

        public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => MinimumFormat;

        public int KeyRequests { get; private set; }

        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context)
        {
            KeyRequests++;
            return ValueTask.FromResult(Key);
        }

        public string? GetReplayScope(JsonRpcRequestContext context) => ReplayScope;

        public bool RequiresUniqueSequence(JsonRpcRequestContext context) => UniqueSequence;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CountingEncryptor : IPayloadEncryptor
    {
        private readonly AesCbcHmacPayloadEncryptor _inner = new();

        public int Decryptions { get; private set; }

        public string Name => _inner.Name;

        public byte[] Encrypt(byte[] bytes, byte[] key) => _inner.Encrypt(bytes, key);

        public byte[] Decrypt(byte[] bytes, byte[] key)
        {
            Decryptions++;
            return _inner.Decrypt(bytes, key);
        }
    }

    private sealed class DelegateFilter(Func<JsonRpcRequestContext, JsonRpcFilterDelegate, ValueTask> invoke) : IJsonRpcFilter
    {
        public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next) => invoke(context, next);
    }
}
