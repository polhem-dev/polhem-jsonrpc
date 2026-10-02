namespace Polhem.JsonRpc.Server;

/// <summary>
/// The answer to an incoming message: the responses, and whether they form a batch.
/// </summary>
public sealed class JsonRpcDispatchResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcDispatchResult"/> class.
    /// </summary>
    /// <param name="isBatch">Whether the message was a batch.</param>
    /// <param name="responses">The responses.</param>
    public JsonRpcDispatchResult(bool isBatch, IReadOnlyList<JsonRpcResponse> responses)
    {
        ArgumentNullException.ThrowIfNull(responses);
        IsBatch = isBatch;
        Responses = responses;
    }

    /// <summary>
    /// Gets a value indicating whether the message was a batch, answered with an array.
    /// </summary>
    public bool IsBatch { get; }

    /// <summary>
    /// Gets the responses. It is empty when the message held only notifications, which are not answered.
    /// </summary>
    public IReadOnlyList<JsonRpcResponse> Responses { get; }

    /// <summary>
    /// Gets a value indicating whether there is anything to send back.
    /// </summary>
    public bool HasContent => Responses.Count > 0;

    /// <summary>
    /// Serializes the answer: an object for a single request, an array for a batch.
    /// </summary>
    /// <param name="options">How to write the responses, or <c>null</c> for the defaults.</param>
    /// <returns>The answer as UTF-8 JSON, or <c>null</c> when there is nothing to send back.</returns>
    public byte[]? Serialize(JsonRpcWriteOptions? options = null)
    {
        if (!HasContent) { return null; }
        return IsBatch
            ? JsonRpcSerializer.SerializeResponses(Responses, options)
            : JsonRpcSerializer.SerializeResponse(Responses[0], options);
    }
}
