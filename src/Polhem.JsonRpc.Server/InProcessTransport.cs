namespace Polhem.JsonRpc.Server;

/// <summary>
/// Delivers requests straight to a dispatcher in the same process, without HTTP and without JSON text.
/// </summary>
/// <remarks>
/// Every call it delivers is marked <see cref="JsonRpcTransportKind.InProcess"/>.
/// <para>
/// The request is not serialized, so the server's filters see the same <see cref="JsonRpcRequest"/> object the
/// client's interceptors produced; a filter that changes it changes the caller's object.
/// </para>
/// </remarks>
public sealed class InProcessTransport : IJsonRpcTransport
{
    private readonly JsonRpcDispatcher _dispatcher;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessTransport"/> class.
    /// </summary>
    /// <param name="dispatcher">The dispatcher that runs the calls.</param>
    /// <param name="services">The services the calls see, or <c>null</c>.</param>
    public InProcessTransport(JsonRpcDispatcher dispatcher, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _services = services;
    }

    /// <summary>
    /// Gets values passed to every call as <see cref="JsonRpcTransportInfo.Items"/>, such as a credential the
    /// caller holds.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<JsonRpcResponse?> SendAsync(JsonRpcRequest request, CancellationToken cancellationToken = default)
        => _dispatcher.DispatchAsync(request, CreateTransportInfo(), cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<JsonRpcResponse>> SendBatchAsync(IReadOnlyList<JsonRpcRequest> requests, CancellationToken cancellationToken = default)
        => _dispatcher.DispatchBatchAsync(requests, CreateTransportInfo(), cancellationToken);

    private JsonRpcTransportInfo CreateTransportInfo() => new(
        JsonRpcTransportKind.InProcess,
        _services,
        items: new Dictionary<string, object?>(Items, StringComparer.Ordinal));
}
