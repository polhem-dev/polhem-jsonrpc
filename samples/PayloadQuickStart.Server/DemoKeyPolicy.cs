using Polhem.JsonRpc.Payload.Server;
using Polhem.JsonRpc.Server;

namespace PayloadQuickStart.Server;

/// <summary>
/// Answers the payload filter's questions with one key shared with the client, and the client's <c>X-Client-Id</c>
/// header as the scope a sequence number must be unique in.
/// </summary>
/// <remarks>
/// A real application gives each session its own key, agreed at sign-in (the Polhem framework wraps it with RSA), and
/// uses the session as the replay scope. How keys are agreed is outside the payload packages.
/// </remarks>
public sealed class DemoKeyPolicy(byte[] key) : IPayloadServerPolicy
{
    public ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context) => ValueTask.FromResult<byte[]?>(key);

    public string? GetReplayScope(JsonRpcRequestContext context)
        => context.Transport.Headers.TryGetValue("X-Client-Id", out var clientId) ? clientId : null;

    public bool RequiresUniqueSequence(JsonRpcRequestContext context) => true;

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
