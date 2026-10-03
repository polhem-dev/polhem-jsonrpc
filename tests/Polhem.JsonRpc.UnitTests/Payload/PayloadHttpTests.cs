using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Polhem.JsonRpc.AspNetCore;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.UnitTests.Payload;

/// <summary>
/// An encrypted, framed call over HTTP, from the client package to the ASP.NET Core endpoint and back, the way an
/// application deploys the payload packages.
/// </summary>
public sealed class PayloadHttpTests : IAsyncLifetime
{
    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

    private readonly PayloadOptions _payloadOptions = new()
    {
        RequireFrame = true,
        TypeResolver = new PayloadTypeRegistry().Register<SubtractRequest>().Register<SubtractResponse>(),
    };

    private WebApplication? _app;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddJsonRpcServer(options =>
        {
            options.ObjectFactory = new TestObjectFactory();
            options.UsePayload(_payloadOptions, new SessionPolicy());
        });
        _app = builder.Build();
        _app.MapJsonRpc("/api");
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) { await _app.DisposeAsync(); }
    }

    [Fact]
    [DisplayName("Payload over HTTP: an encrypted, framed call is answered encrypted and opens on the client")]
    public async Task Post_EncryptedCall_RoundTrips()
    {
        var rpc = new JsonRpcConnector(new HttpTransport(_app!.GetTestClient(), new Uri("/api", UriKind.Relative)));
        var payload = new PayloadProcessor(_payloadOptions);

        var result = await rpc.InvokeAsync<JsonElement>("Spec.Subtract",
            payload.Wrap(new SubtractRequest(9, 4), PayloadFormat.Encrypted, key: s_key, sequence: 1));

        Assert.Equal(PayloadFormat.Encrypted, PayloadEnvelope.ReadFormat(result));
        Assert.Equal(5, Assert.IsType<SubtractResponse>(payload.Unwrap(result, s_key)).Difference);
    }

    private sealed class SessionPolicy : IPayloadServerPolicy
    {
        public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(s_key);

        public string? GetReplayScope(JsonRpcRequestContext context) => "session";

        public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;
    }
}
