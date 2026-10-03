using Polhem.JsonRpc.Client;

namespace Polhem.JsonRpc.AotSmoke;

/// <summary>Counts the requests it sees.</summary>
internal sealed class CountingInterceptor : IJsonRpcClientInterceptor
{
    public int Requests { get; private set; }

    public ValueTask OnRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        return ValueTask.CompletedTask;
    }

    public ValueTask OnResponseAsync(JsonRpcRequest request, JsonRpcResponse response, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
