using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Polhem.JsonRpc.Client;
using Polhem.JsonRpc.Payload;
using QuickStart.Contracts;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// Runs the PayloadQuickStart server sample and calls it the way the PayloadQuickStart client sample does, so that the
/// samples cannot drift away from the API.
/// </summary>
public sealed class PayloadQuickStartSampleTests : IDisposable
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);
    private readonly WebApplicationFactory<PayloadQuickStart.Server.Calculator> _factory;

    public PayloadQuickStartSampleTests()
    {
        var key = Convert.ToBase64String(_key);
        _factory = new WebApplicationFactory<PayloadQuickStart.Server.Calculator>()
            .WithWebHostBuilder(builder => builder.UseSetting("PayloadDemoKey", key));
    }

    public void Dispose() => _factory.Dispose();

    private JsonRpcConnector Connect()
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Client-Id", Guid.NewGuid().ToString("N"));
        return new JsonRpcConnector(new HttpTransport(http, new Uri("/api", UriKind.Relative)));
    }

    private static PayloadProcessor CreateProcessor() => new(new PayloadOptions
    {
        RequireFrame = true,
        TypeResolver = new PayloadTypeRegistry().Register<AddRequest>().Register<AddResponse>(),
    });

    [Fact]
    [DisplayName("PayloadQuickStart sample: an encrypted Calculator.Add is answered encrypted")]
    public async Task EncryptedAdd_ReturnsSum()
    {
        var payload = CreateProcessor();

        var result = await Connect().InvokeAsync<JsonElement>("Calculator.Add",
            payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: _key, sequence: 1));

        Assert.Equal(PayloadFormat.Encrypted, PayloadEnvelope.ReadFormat(result));
        Assert.Equal(3, Assert.IsType<AddResponse>(payload.Unwrap(result, _key)).Sum);
    }

    [Fact]
    [DisplayName("PayloadQuickStart sample: sending the same call twice is refused with -32005")]
    public async Task ReplayedCall_IsRefused()
    {
        var rpc = Connect();
        var parameters = CreateProcessor().Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: _key, sequence: 1);
        await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters));

        Assert.Equal(-32005, ex.Code);
    }
}
