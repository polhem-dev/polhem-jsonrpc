namespace Polhem.JsonRpc.AspNetCore;

/// <summary>
/// Settings for <see cref="JsonRpcHttpHandler"/>.
/// </summary>
public sealed class JsonRpcHttpOptions
{
    /// <summary>
    /// Gets or sets the largest request body accepted, in bytes. A larger body is answered with HTTP 413. The
    /// default is 4 MiB.
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a function that chooses the HTTP status code of the answer to a single request, or <c>null</c>
    /// to answer every call with 200.
    /// </summary>
    /// <remarks>
    /// The JSON-RPC error is in the body whatever the status is. A host uses this to keep an older wire format that
    /// answers some errors with 4xx or 5xx. A batch is always answered with 200, because its responses may disagree.
    /// </remarks>
    public Func<JsonRpcResponse, int>? StatusCodeSelector { get; set; }

    /// <summary>
    /// Gets or sets how responses are written. The defaults follow the specification; a host sets
    /// <see cref="JsonRpcWriteOptions.OmitNullId"/> to keep an older wire format that leaves a null id out.
    /// </summary>
    public JsonRpcWriteOptions WriteOptions { get; set; } = new();
}
