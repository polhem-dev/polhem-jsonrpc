namespace Polhem.JsonRpc.Server;

/// <summary>
/// What the transport knows about a call: which transport delivered it, and the headers, address and services that
/// came with it.
/// </summary>
/// <remarks>
/// IMPORTANT: only transport code should create this object, and it must never take <see cref="Kind"/> from the
/// request itself. A host may grant in-process calls more than remote ones, so a remote caller must not be able to
/// claim to be in process through a header or a parameter. The built-in transports set the kind themselves;
/// <c>HttpHandlerTests.Post_HeaderClaimsInProcess_StillMarkedHttp</c> holds the HTTP one to it.
/// </remarks>
public sealed class JsonRpcTransportInfo
{
    private static readonly IReadOnlyDictionary<string, string> s_noHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, object?> s_noItems =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcTransportInfo"/> class.
    /// </summary>
    /// <param name="kind">The transport that delivered the call.</param>
    /// <param name="services">The services of the call's scope, or <c>null</c> when there are none.</param>
    /// <param name="headers">
    /// The headers that came with the call, or <c>null</c>. Pass a dictionary that compares names case-insensitively, as
    /// the HTTP transport does, because header names are case-insensitive.
    /// </param>
    /// <param name="remoteAddress">The address of the caller, or <c>null</c> when unknown.</param>
    /// <param name="items">Values the transport passes to filters and the object factory, or <c>null</c>.</param>
    public JsonRpcTransportInfo(
        JsonRpcTransportKind kind,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, string>? headers = null,
        string? remoteAddress = null,
        IReadOnlyDictionary<string, object?>? items = null)
    {
        Kind = kind;
        Services = services;
        Headers = headers ?? s_noHeaders;
        RemoteAddress = remoteAddress;
        Items = items ?? s_noItems;
    }

    /// <summary>
    /// Gets the transport that delivered the call.
    /// </summary>
    public JsonRpcTransportKind Kind { get; }

    /// <summary>
    /// Gets the services of the call's scope, or <c>null</c> when there are none.
    /// </summary>
    public IServiceProvider? Services { get; }

    /// <summary>
    /// Gets the headers that came with the call. An in-process call has none.
    /// </summary>
    /// <remarks>
    /// The built-in transports compare names ignoring case; a custom transport decides with the dictionary it passes.
    /// The HTTP handler joins the values of a header sent more than once with commas,
    /// so code that expects a single value, such as a token, should refuse one that contains a comma.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets the address of the caller, or <c>null</c> when unknown.
    /// </summary>
    public string? RemoteAddress { get; }

    /// <summary>
    /// Gets values the transport passes to filters and the object factory, such as a credential an in-process caller
    /// holds.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Items { get; }
}
