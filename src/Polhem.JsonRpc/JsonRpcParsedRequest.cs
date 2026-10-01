using System.Diagnostics.CodeAnalysis;

namespace Polhem.JsonRpc;

/// <summary>
/// One entry of an incoming message: a valid request, or the error that answers an invalid one.
/// </summary>
public sealed class JsonRpcParsedRequest
{
    private JsonRpcParsedRequest(JsonRpcRequest? request, JsonRpcError? error, JsonRpcId errorId)
    {
        Request = request;
        Error = error;
        ErrorId = errorId;
    }

    /// <summary>
    /// Gets the request, or <c>null</c> when the entry is invalid.
    /// </summary>
    public JsonRpcRequest? Request { get; }

    /// <summary>
    /// Gets the error that answers an invalid entry, or <c>null</c> when the entry is a valid request.
    /// </summary>
    public JsonRpcError? Error { get; }

    /// <summary>
    /// Gets the id to answer an invalid entry with: the id of the entry when it could be read, otherwise
    /// <see cref="JsonRpcId.Null"/>.
    /// </summary>
    public JsonRpcId ErrorId { get; }

    /// <summary>
    /// Gets a value indicating whether the entry is a valid request.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Request))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsValid => Request is not null;

    /// <summary>
    /// Creates a valid entry.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The entry.</returns>
    public static JsonRpcParsedRequest Valid(JsonRpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request, null, JsonRpcId.None);
    }

    /// <summary>
    /// Creates an invalid entry.
    /// </summary>
    /// <param name="error">The error that answers it.</param>
    /// <param name="id">The id of the entry, or <see cref="JsonRpcId.Null"/> when it could not be read.</param>
    /// <returns>The entry.</returns>
    public static JsonRpcParsedRequest Invalid(JsonRpcError error, JsonRpcId id)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(null, error, id.IsNone ? JsonRpcId.Null : id);
    }
}
