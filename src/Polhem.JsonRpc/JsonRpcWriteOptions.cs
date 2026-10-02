namespace Polhem.JsonRpc;

/// <summary>
/// How <see cref="JsonRpcSerializer"/> writes responses.
/// </summary>
/// <remarks>
/// The defaults follow the specification. The options exist so that a server can keep the exact shape of an older
/// wire format; a new protocol should not need them.
/// </remarks>
public sealed class JsonRpcWriteOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether a response whose id is null leaves the <c>id</c> member out instead
    /// of writing <c>"id": null</c>. The specification requires the member; the default is <c>false</c>.
    /// </summary>
    public bool OmitNullId { get; set; }
}
