using System.Security.Cryptography;
using System.Text;
using Polhem.JsonRpc;
using Polhem.JsonRpc.Server;

namespace ApiKey;

/// <summary>
/// Rejects a call unless its X-Api-Key header holds the expected key.
/// </summary>
public sealed class ApiKeyFilter(string expectedKey) : IJsonRpcFilter
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(expectedKey);

    public ValueTask InvokeAsync(JsonRpcRequestContext context, JsonRpcFilterDelegate next)
    {
        context.Transport.Headers.TryGetValue("X-Api-Key", out var key);

        // Compared in constant time, so the answer time does not reveal how much of a guess was right.
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key ?? string.Empty), _expected))
        {
            throw new JsonRpcErrorException(-32001, "Unauthorized");
        }
        return next(context);
    }
}
