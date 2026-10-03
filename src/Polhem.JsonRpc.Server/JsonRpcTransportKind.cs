namespace Polhem.JsonRpc.Server;

/// <summary>
/// The kinds of transport that deliver a request to the dispatcher.
/// </summary>
/// <remarks>
/// IMPORTANT: <see cref="InProcess"/> is not the default value. A host may grant in-process calls more than remote ones,
/// so a transport that forgets to set the kind, or passes <c>default</c>, must land on a remote kind.
/// <c>TransportKind_Default_IsNotInProcess</c> in the unit tests holds the values to this.
/// </remarks>
public enum JsonRpcTransportKind
{
    /// <summary>
    /// The request arrived over HTTP. It is the default value.
    /// </summary>
    Http = 0,

    /// <summary>
    /// The request was delivered in process, by <see cref="InProcessTransport"/>.
    /// </summary>
    InProcess = 1,

    /// <summary>
    /// The request arrived over a transport the host implements itself.
    /// </summary>
    Custom = 2,
}
