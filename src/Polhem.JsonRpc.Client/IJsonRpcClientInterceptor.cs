namespace Polhem.JsonRpc.Client;

/// <summary>
/// Sees every request before it is sent and every response before its result is read.
/// </summary>
/// <remarks>
/// An interceptor can rewrite <see cref="JsonRpcRequest.Params"/> before sending (for example to encrypt it) and
/// <see cref="JsonRpcResponse.Result"/> after receiving (for example to decrypt it). HTTP headers belong to the
/// transport: add them with a <see cref="DelegatingHandler"/> on the <see cref="HttpClient"/>.
/// </remarks>
public interface IJsonRpcClientInterceptor
{
    /// <summary>
    /// Runs before a request is sent.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the interceptor is done.</returns>
    ValueTask OnRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <summary>
    /// Runs after a response is received, before its result is read or its error thrown.
    /// </summary>
    /// <param name="request">The request the response answers.</param>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>A task that completes when the interceptor is done.</returns>
    ValueTask OnResponseAsync(JsonRpcRequest request, JsonRpcResponse response, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
