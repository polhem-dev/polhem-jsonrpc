using System.Text.Json;

namespace Polhem.JsonRpc;

/// <summary>
/// A JSON-RPC 2.0 request or notification.
/// </summary>
public sealed class JsonRpcRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcRequest"/> class.
    /// </summary>
    /// <param name="method">The name of the method to invoke.</param>
    /// <param name="parameters">The <c>params</c> member: an object, an array, or <c>null</c> when absent.</param>
    /// <param name="id">The request id, or <see cref="JsonRpcId.None"/> for a notification.</param>
    public JsonRpcRequest(string method, JsonElement? parameters = null, JsonRpcId id = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        Method = method;
        Params = parameters;
        Id = id;
    }

    /// <summary>
    /// Gets the name of the method to invoke.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets or sets the <c>params</c> member, or <c>null</c> when it is absent.
    /// </summary>
    /// <remarks>
    /// Settable so that a server filter or a client interceptor can rewrite the parameters, for example to decrypt
    /// or encrypt them, before they are bound or sent.
    /// </remarks>
    public JsonElement? Params { get; set; }

    /// <summary>
    /// Gets the request id, or <see cref="JsonRpcId.None"/> for a notification.
    /// </summary>
    public JsonRpcId Id { get; }

    /// <summary>
    /// Gets a value indicating whether the request is a notification, which receives no response.
    /// </summary>
    public bool IsNotification => Id.IsNone;
}
