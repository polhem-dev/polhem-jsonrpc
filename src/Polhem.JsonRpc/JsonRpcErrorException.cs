using System.Text.Json;

namespace Polhem.JsonRpc;

/// <summary>
/// An exception that carries a JSON-RPC error.
/// </summary>
/// <remarks>
/// On the server, a method or a filter throws it to answer with a specific error; its code, message and data reach
/// the caller as they are. On the client, the connector throws it when the server answers with an error.
/// </remarks>
public class JsonRpcErrorException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcErrorException"/> class.
    /// </summary>
    /// <param name="code">The error code; see <see cref="JsonRpcErrorCodes"/>.</param>
    /// <param name="message">A short description of the error, sent to the caller.</param>
    /// <param name="data">Additional information about the error, or <c>null</c>.</param>
    public JsonRpcErrorException(int code, string message, JsonElement? data = null)
        : this(new JsonRpcError(code, message, data))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcErrorException"/> class from an error.
    /// </summary>
    /// <param name="error">The error.</param>
    public JsonRpcErrorException(JsonRpcError error)
        : base(error?.Message)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>
    /// Gets the error.
    /// </summary>
    public JsonRpcError Error { get; }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public int Code => Error.Code;
}
