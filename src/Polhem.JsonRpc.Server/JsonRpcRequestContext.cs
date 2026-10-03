using System.Text.Json;

namespace Polhem.JsonRpc.Server;

/// <summary>
/// One call as it passes through the dispatcher: the request, what the transport knows about it, the resolved
/// method and, once it has run, the result.
/// </summary>
public sealed class JsonRpcRequestContext
{
    private Dictionary<string, object?>? _items;

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
    /// Gets or sets the value the method returned, before it is serialized. It is set when the method returns.
    /// </summary>
    /// <remarks>
    /// A filter that writes the answer in its own form, such as a host's payload envelope, reads this value after
    /// calling <c>next</c> and sets <see cref="Result"/>; the value is then never serialized with
    /// <see cref="JsonRpcServerOptions.SerializerOptions"/>.
    /// </remarks>
    public object? ReturnValue { get; set; }

    /// <summary>
    /// Gets or sets the declared type of <see cref="ReturnValue"/> (the result type of a task), or <c>null</c> when
    /// the method returns nothing.
    /// </summary>
    public Type? ReturnType { get; set; }

    /// <summary>
    /// Gets or sets the result of the call as JSON. A filter sets it to answer in its own form; when no filter does,
    /// <see cref="ReturnValue"/> is serialized after the filters have run.
    /// </summary>
    /// <remarks>
    /// A filter that needs the serialized result after calling <c>next</c>, for example to encrypt it, calls
    /// <see cref="GetResult"/> instead of reading this property.
    /// </remarks>
    public JsonElement? Result { get; set; }

    internal JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets the result as JSON: <see cref="Result"/> when it is set, otherwise <see cref="ReturnValue"/> serialized
    /// with <see cref="JsonRpcServerOptions.SerializerOptions"/>. The serialized value is kept in
    /// <see cref="Result"/>.
    /// </summary>
    /// <returns>The result, or <c>null</c> when the method returned nothing.</returns>
    public JsonElement? GetResult()
    {
        if (Result is not null || ReturnValue is null || ReturnType is null) { return Result; }
        var options = SerializerOptions ?? throw new InvalidOperationException("The context has no serializer options.");
        Result = JsonSerializer.SerializeToElement(ReturnValue, options.GetTypeInfo(ReturnType));
        return Result;
    }

    /// <summary>
    /// Gets values that filters and the object factory share during this call. It starts with a copy of
    /// <see cref="JsonRpcTransportInfo.Items"/>.
    /// </summary>
    public IDictionary<string, object?> Items => _items ??= new Dictionary<string, object?>(Transport.Items, StringComparer.Ordinal);
}
