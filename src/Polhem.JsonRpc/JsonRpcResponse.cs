using System.Text.Json;

namespace Polhem.JsonRpc;

/// <summary>
/// A JSON-RPC 2.0 response: either a result or an error.
/// </summary>
public sealed class JsonRpcResponse
{
    private JsonRpcResponse(JsonRpcId id, JsonElement? result, JsonRpcError? error)
    {
        Id = id;
        Result = result;
        Error = error;
    }

    /// <summary>
    /// Gets the id of the request this response answers. It is <see cref="JsonRpcId.Null"/> when the id of the
    /// request could not be determined.
    /// </summary>
    public JsonRpcId Id { get; }

    /// <summary>
    /// Gets or sets the result of a successful call. A method that returns nothing has a JSON <c>null</c> result.
    /// </summary>
    /// <remarks>
    /// Settable so that a client interceptor can rewrite the result, for example to decrypt it, before it is
    /// deserialized.
    /// </remarks>
    public JsonElement? Result { get; set; }

    /// <summary>
    /// Gets the error of a failed call, or <c>null</c> on success.
    /// </summary>
    public JsonRpcError? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the call succeeded.
    /// </summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// Gets additional members written next to <c>jsonrpc</c>, <c>result</c>, <c>error</c> and <c>id</c>, or
    /// <c>null</c> when there are none.
    /// </summary>
    /// <remarks>
    /// The specification defines only the four members above. Additional members exist so that a host can keep an
    /// older wire format; a new protocol should not need them.
    /// </remarks>
    public IDictionary<string, JsonElement>? AdditionalMembers { get; set; }

    /// <summary>
    /// Creates a success response.
    /// </summary>
    /// <param name="id">The id of the request.</param>
    /// <param name="result">The result, or <c>null</c> to write a JSON <c>null</c>.</param>
    /// <returns>The response.</returns>
    public static JsonRpcResponse Success(JsonRpcId id, JsonElement? result) => new(id, result, null);

    /// <summary>
    /// Creates an error response.
    /// </summary>
    /// <param name="id">The id of the request, or <see cref="JsonRpcId.Null"/> when it could not be determined.</param>
    /// <param name="error">The error.</param>
    /// <returns>The response.</returns>
    public static JsonRpcResponse Failure(JsonRpcId id, JsonRpcError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(id, null, error);
    }
}
