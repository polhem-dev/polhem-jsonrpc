namespace Polhem.JsonRpc.Server;

/// <summary>
/// The kinds of transport that deliver a request to the dispatcher.
/// </summary>
public enum JsonRpcTransportKind
{
    /// <summary>
    /// The request was delivered in process, by <see cref="InProcessTransport"/>.
    /// </summary>
    InProcess,

    /// <summary>
    /// The request arrived over HTTP.
    /// </summary>
    Http,

    /// <summary>
    /// The request arrived over a transport the host implements itself.
    /// </summary>
    Custom,
}
