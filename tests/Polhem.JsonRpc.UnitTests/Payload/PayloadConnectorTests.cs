using System.Security.Cryptography;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Client;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadConnectorTests
{
    private const string Subtract = "Spec.Subtract";
    private const int ReplayRejected = -32010;

    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

    [Theory(DisplayName = "Payload connector: a call in each format returns the result, opened into the requested type")]
    [InlineData(PayloadFormat.Plain)]
    [InlineData(PayloadFormat.Encoded)]
    [InlineData(PayloadFormat.Encrypted)]
    public async Task InvokeAsync_EachFormat_ReturnsResult(PayloadFormat format)
    {
        var (rpc, sent) = Create(new PayloadConnectorOptions { Format = format, KeyProvider = () => s_key });

        var result = await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3));

        Assert.Equal(2, result!.Difference);
        Assert.Equal([format], sent.Formats);
    }

    [Fact(DisplayName = "Payload connector: a call that names no format is encrypted by default")]
    public async Task InvokeAsync_DefaultOptions_SendsEncrypted()
    {
        var (rpc, sent) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key });

        await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3));

        Assert.Equal([PayloadFormat.Encrypted], sent.Formats);
    }

    [Fact(DisplayName = "Payload connector: the format passed to a call overrides the default")]
    public async Task InvokeAsync_FormatPassed_OverridesDefault()
    {
        var (rpc, sent) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key });

        var result = await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3), PayloadFormat.Encoded, CancellationToken.None);

        Assert.Equal(2, result!.Difference);
        Assert.Equal([PayloadFormat.Encoded], sent.Formats);
    }

    [Fact(DisplayName = "Payload connector: an encrypted call without a key fails before anything is sent")]
    public async Task InvokeAsync_EncryptedWithoutKey_ThrowsBeforeSending()
    {
        var (rpc, sent) = Create(new PayloadConnectorOptions { KeyProvider = () => null });

        await Assert.ThrowsAsync<InvalidOperationException>(() => rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3)));

        Assert.Empty(sent.Formats);
    }

    [Fact(DisplayName = "Payload connector: the key is asked for on every encrypted call, so a new key is used from the next call")]
    public async Task InvokeAsync_KeyChanges_UsesCurrentKey()
    {
        var key = s_key;
        var policy = new ConnectorPolicy();
        var (rpc, _) = Create(new PayloadConnectorOptions { KeyProvider = () => key }, policy);
        await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3));

        key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);
        policy.Key = key;
        var result = await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(9, 3));

        Assert.Equal(6, result!.Difference);
    }

    [Fact(DisplayName = "Payload connector: each call takes the next sequence number, so a server that refuses repeated numbers accepts them all")]
    public async Task InvokeAsync_ConsecutiveCalls_AreNotReplays()
    {
        var (rpc, _) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key }, new ConnectorPolicy { ReplayScope = "session-1" });

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(i, (await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(i, 0)))!.Difference);
        }
    }

    [Fact(DisplayName = "Payload connector: a sequence generator that repeats a number is refused as a replay")]
    public async Task InvokeAsync_SequenceGeneratorRepeats_IsRefused()
    {
        var (rpc, _) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key, SequenceGenerator = () => 1 },
            new ConnectorPolicy { ReplayScope = "session-1" });
        await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3));

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3)));

        Assert.Equal(ReplayRejected, ex.Code);
    }

    [Fact(DisplayName = "Payload connector: a plain call takes no sequence number")]
    public async Task InvokeAsync_PlainCall_TakesNoSequence()
    {
        var taken = 0;
        var (rpc, _) = Create(new PayloadConnectorOptions { Format = PayloadFormat.Plain, SequenceGenerator = () => ++taken });

        await rpc.InvokeAsync<SubtractResponse>(Subtract, new SubtractRequest(5, 3));

        Assert.Equal(0, taken);
    }

    [Fact(DisplayName = "Payload connector: object as the result type opens the result into the type its envelope names")]
    public async Task InvokeAsync_ObjectResult_ResolvesNamedType()
    {
        var (rpc, _) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key });

        var result = await rpc.InvokeAsync<object>(Subtract, new SubtractRequest(5, 3));

        Assert.Equal(2, Assert.IsType<SubtractResponse>(result).Difference);
    }

    [Fact(DisplayName = "Payload connector: the non-generic InvokeAsync waits for the method, whose result type is not registered")]
    public async Task InvokeAsync_VoidCall_RunsMethod()
    {
        var marker = Guid.NewGuid().ToString();
        var (rpc, sent) = Create(new PayloadConnectorOptions { KeyProvider = () => s_key });

        await rpc.InvokeAsync("Spec.Update", new UpdateRequest(marker), CancellationToken.None);

        Assert.Contains(marker, SpecTarget.Updates);
        Assert.Equal([PayloadFormat.Encrypted], sent.Formats);
    }

    private static (PayloadConnector Rpc, SentFormats Sent) Create(PayloadConnectorOptions options, ConnectorPolicy? policy = null)
    {
        var payloadOptions = new PayloadOptions
        {
            RequireFrame = true,
            TypeResolver = new PayloadTypeRegistry().Register<SubtractRequest>().Register<SubtractResponse>(),
        };
        var dispatcher = DispatcherFixture.Create(server =>
        {
            server.UsePayload(payloadOptions, policy ?? new ConnectorPolicy());
            server.ExceptionMapper = (exception, _) => exception is ReplayRejectedException
                ? new JsonRpcError(ReplayRejected, "Replay rejected")
                : null;
        });
        var sent = new SentFormats();
        var clientOptions = new JsonRpcClientOptions();
        clientOptions.Interceptors.Add(sent);
        var connector = new JsonRpcConnector(new InProcessTransport(dispatcher), clientOptions);
        return (new PayloadConnector(connector, new PayloadProcessor(payloadOptions), options), sent);
    }

    private sealed class SentFormats : IJsonRpcClientInterceptor
    {
        public List<PayloadFormat> Formats { get; } = [];

        public ValueTask OnRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            Formats.Add(PayloadEnvelope.ReadFormat(request.Params));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConnectorPolicy : IPayloadServerPolicy
    {
        public byte[] Key { get; set; } = s_key;

        public string? ReplayScope { get; init; }

        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(Key);

        public string? GetReplayScope(JsonRpcRequestContext context) => ReplayScope;

        public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
    }
}
