using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
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
    private readonly byte[] _demoKey = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);
    private readonly WebApplicationFactory<PayloadQuickStart.Server.Calculator> _factory;

    public PayloadQuickStartSampleTests()
    {
        var key = Convert.ToBase64String(_demoKey);
        _factory = new WebApplicationFactory<PayloadQuickStart.Server.Calculator>()
            .WithWebHostBuilder(builder => builder.UseSetting("PayloadDemoKey", key));
    }

    public void Dispose() => _factory.Dispose();

    // Derives the key as PayloadQuickStart.Client does.
    private (JsonRpcConnector Rpc, byte[] Key) Connect(string? clientId = null)
    {
        clientId ??= Guid.NewGuid().ToString("N");
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Client-Id", clientId);
        var key = HMACSHA512.HashData(_demoKey, Encoding.UTF8.GetBytes(clientId));
        return (new JsonRpcConnector(new HttpTransport(http, new Uri("/api", UriKind.Relative))), key);
    }

    private static PayloadProcessor CreateProcessor() => new(new PayloadOptions { RequireFrame = true });

    [Fact]
    [DisplayName("PayloadQuickStart sample: an encrypted Calculator.Add is answered encrypted")]
    public async Task EncryptedAdd_ReturnsSum()
    {
        var payload = CreateProcessor();
        var (rpc, key) = Connect();

        var result = await rpc.InvokeAsync<JsonElement>("Calculator.Add",
            payload.Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1));

        Assert.Equal(PayloadFormat.Encrypted, PayloadEnvelope.ReadFormat(result));
        Assert.Equal(3, payload.Unwrap<AddResponse>(result, key)!.Sum);
    }

    [Fact]
    [DisplayName("PayloadQuickStart sample: sending the same call twice is refused with -32005")]
    public async Task ReplayedCall_IsRefused()
    {
        var (rpc, key) = Connect();
        var parameters = CreateProcessor().Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
        await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters));

        Assert.Equal(-32005, ex.Code);
    }

    [Fact]
    [DisplayName("PayloadQuickStart sample: a call replayed under another client id does not decrypt, so it is refused")]
    public async Task ReplayedUnderAnotherClientId_IsRefused()
    {
        var (rpc, key) = Connect();
        var parameters = CreateProcessor().Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Encrypted, key: key, sequence: 1);
        await rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters);
        var (other, _) = Connect();

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => other.InvokeAsync<JsonElement>("Calculator.Add", parameters));

        Assert.Equal(JsonRpcErrorCodes.InternalError, ex.Code);
    }

    [Fact]
    [DisplayName("PayloadQuickStart sample: a plain call is refused with -32602, because the sample requires encryption")]
    public async Task PlainCall_IsRefused()
    {
        var (rpc, _) = Connect();
        var parameters = CreateProcessor().Wrap(new AddRequest { A = 1, B = 2 }, PayloadFormat.Plain);

        var ex = await Assert.ThrowsAsync<JsonRpcErrorException>(() => rpc.InvokeAsync<JsonElement>("Calculator.Add", parameters));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
    }
}
