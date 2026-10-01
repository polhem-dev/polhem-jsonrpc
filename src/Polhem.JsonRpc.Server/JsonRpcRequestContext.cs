using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// One call as it passes through the dispatcher: the request, what the transport knows about it, the resolved
/// method and, once it has run, the result.
/// </summary>
public sealed class JsonRpcRequestContext
{
    private Dictionary<string, object?>? _items;
    private Dictionary<string, JsonElement>? _responseMembers;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcRequestContext"/> class.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="transport">What the transport knows about the call.</param>
    /// <param name="cancellationToken">A token that cancels the call.</param>
    public JsonRpcRequestContext(JsonRpcRequest request, JsonRpcTransportInfo transport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(transport);
        Request = request;
        Transport = transport;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Gets the request. A filter may rewrite its <see cref="JsonRpcRequest.Params"/>.
    /// </summary>
    public JsonRpcRequest Request { get; }

    /// <summary>
    /// Gets what the transport knows about the call.
    /// </summary>
    public JsonRpcTransportInfo Transport { get; }

    /// <summary>
    /// Gets the services of the call's scope, or <c>null</c> when there are none.
    /// </summary>
    public IServiceProvider? Services => Transport.Services;

    /// <summary>
    /// Gets a token that cancels the call.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the resolved method, or <c>null</c> before resolution.
    /// </summary>
    public JsonRpcMethod? Method { get; internal set; }

    /// <summary>
    /// Gets or sets the result of the call. It is set when the method returns; a filter may rewrite it after
    /// calling <c>next</c>.
    /// </summary>
    public JsonElement? Result { get; set; }

    /// <summary>
    /// Gets values that filters and the object factory share during this call. It starts with a copy of
    /// <see cref="JsonRpcTransportInfo.Items"/>.
    /// </summary>
    public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(Transport.Items, StringComparer.Ordinal);

    /// <summary>
    /// Gets members to add to the response next to <c>jsonrpc</c>, <c>result</c>, <c>error</c> and <c>id</c>.
    /// </summary>
    /// <remarks>
    /// They exist so that a host can keep an older wire format; see <see cref="JsonRpcResponse.AdditionalMembers"/>.
    /// They are written on success and on error.
    /// </remarks>
    public IDictionary<string, JsonElement> ResponseMembers => _responseMembers ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    internal bool HasResponseMembers => _responseMembers is { Count: > 0 };
}
