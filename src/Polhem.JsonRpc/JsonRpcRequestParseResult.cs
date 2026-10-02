namespace Polhem.JsonRpc;

/// <summary>
/// The result of reading an incoming message: a single request or a batch.
/// </summary>
public sealed class JsonRpcRequestParseResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcRequestParseResult"/> class.
    /// </summary>
    /// <param name="isBatch">Whether the message is a batch.</param>
    /// <param name="entries">The entries, in the order they appear.</param>
    public JsonRpcRequestParseResult(bool isBatch, IReadOnlyList<JsonRpcParsedRequest> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        IsBatch = isBatch;
        Entries = entries;
    }

    /// <summary>
    /// Gets a value indicating whether the message is a batch, which is answered with an array.
    /// </summary>
    /// <remarks>
    /// Invalid JSON and an empty array are not batches: the specification answers each with a single error object.
    /// </remarks>
    public bool IsBatch { get; }

    /// <summary>
    /// Gets the entries, in the order they appear.
    /// </summary>
    public IReadOnlyList<JsonRpcParsedRequest> Entries { get; }
}
