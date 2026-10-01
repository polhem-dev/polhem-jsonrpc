namespace Polhem.JsonRpc;

/// <summary>
/// The error codes the JSON-RPC 2.0 specification defines.
/// </summary>
public static class JsonRpcErrorCodes
{
    /// <summary>
    /// Invalid JSON was received.
    /// </summary>
    public const int ParseError = -32700;

    /// <summary>
    /// The JSON sent is not a valid request object.
    /// </summary>
    public const int InvalidRequest = -32600;

    /// <summary>
    /// The method does not exist or is not available.
    /// </summary>
    public const int MethodNotFound = -32601;

    /// <summary>
    /// Invalid method parameters.
    /// </summary>
    public const int InvalidParams = -32602;

    /// <summary>
    /// Internal JSON-RPC error.
    /// </summary>
    public const int InternalError = -32603;

    /// <summary>
    /// The lowest code of the range the specification reserves for implementation-defined server errors.
    /// </summary>
    public const int ServerErrorRangeStart = -32099;

    /// <summary>
    /// The highest code of the range the specification reserves for implementation-defined server errors.
    /// </summary>
    public const int ServerErrorRangeEnd = -32000;
}
