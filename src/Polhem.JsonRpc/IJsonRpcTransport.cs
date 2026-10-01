namespace Polhem.JsonRpc;

/// <summary>
/// Delivers requests to a JSON-RPC server and returns its responses.
/// </summary>
/// <remarks>
/// <c>Polhem.JsonRpc.Client</c> sends requests over HTTP; <c>Polhem.JsonRpc.Server</c> provides an in-process
/// transport that calls the dispatcher directly. A host can implement this interface for another channel.
/// </remarks>
public interface IJsonRpcTransport
{
    /// <summary>
    /// Sends one request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>The response, or <c>null</c> for a notification.</returns>
    Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a batch of requests.
    /// </summary>
    /// <param name="requests">The requests.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    /// <returns>
    /// The responses, in any order; match them to the requests by id. Notifications have no response, so a batch
    /// of notifications only returns an empty list.
    /// </returns>
    Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, CancellationToken cancellationToken = default);
}
