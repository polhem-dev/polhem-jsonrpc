using System.Text.Json;

namespace Polhem.JsonRpc;

/// <summary>
/// The <c>error</c> member of a JSON-RPC response.
/// </summary>
public sealed class JsonRpcError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JsonRpcError"/> class.
    /// </summary>
    /// <param name="code">The error code; see <see cref="JsonRpcErrorCodes"/>.</param>
    /// <param name="message">A short description of the error.</param>
    /// <param name="data">Additional information about the error, or <c>null</c>.</param>
    public JsonRpcError(int code, string message, JsonElement? data = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
        Data = data;
    }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Gets a short description of the error.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets additional information about the error, or <c>null</c> when the member is absent.
    /// </summary>
    public JsonElement? Data { get; }
}
