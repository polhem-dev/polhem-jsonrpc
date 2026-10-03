using System.Security.Cryptography;
using System.Text;
using Polhem.JsonRpc.Payload;
using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace PayloadQuickStart.Server;

/// <summary>
/// Answers the payload filter's questions with a key for each client, derived from one demo key and the client's
/// <c>X-Client-Id</c> header, and that client id as the scope a sequence number must be unique in.
/// </summary>
/// <remarks>
/// The replay scope has to cover every holder of the key. Here the client id picks the key, so each scope has a key of
/// its own, and a call replayed under another client id fails its HMAC instead of starting over in a fresh scope. That
/// holds only because every call is encrypted (<see cref="GetMinimumFormat"/>): a plain or encoded call has no HMAC, so
/// with a lower minimum format a caller could drop the header, and its call would be accepted without any sequence
/// check. A real application gives each
/// session its own key, agreed at sign-in (the Polhem framework wraps it with RSA), and uses the session as the replay
/// scope. How keys are agreed is outside the payload packages.
/// </remarks>
public sealed class DemoKeyPolicy(byte[] demoKey) : IPayloadServerPolicy
{
    private const string ClientIdHeader = "X-Client-Id";

    public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context)
        => ValueTask.FromResult(ClientId(context) is { } clientId ? ClientKey(demoKey, clientId) : null);

    public string? GetReplayScope(JsonRpcRequestContext context) => ClientId(context);

    public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;

    // Only an encrypted call has a frame the caller cannot forge, so only it is accepted.
    public PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => PayloadFormat.Encrypted;

    /// <summary>Derives a client's key from the demo key, as the client sample does.</summary>
    /// <param name="demoKey">The demo key.</param>
    /// <param name="clientId">The client id.</param>
    /// <returns>A 64-byte key for that client.</returns>
    public static byte[] ClientKey(byte[] demoKey, string clientId)
        => HMACSHA512.HashData(demoKey, Encoding.UTF8.GetBytes(clientId));

    private static string? ClientId(JsonRpcRequestContext context)
        => context.Transport.Headers.TryGetValue(ClientIdHeader, out var clientId) && clientId.Length > 0 ? clientId : null;

    /// <summary>Reads the 64-byte demo key from its Base64 form.</summary>
    /// <param name="base64">The key, from the <c>PayloadDemoKey</c> setting or environment variable.</param>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">The key is missing or not 64 bytes.</exception>
    public static byte[] ReadKey(string? base64)
    {
        var key = string.IsNullOrEmpty(base64) ? [] : Convert.FromBase64String(base64);
        return key.Length == 64
            ? key
            : throw new InvalidOperationException(
                "Set PayloadDemoKey to 64 random bytes in Base64, for example: export PayloadDemoKey=$(openssl rand -base64 64 | tr -d '\\n')");
    }
}
