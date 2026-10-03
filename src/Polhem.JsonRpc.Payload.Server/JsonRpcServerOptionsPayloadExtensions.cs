using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Adds the payload envelope to a server.
/// </summary>
public static class JsonRpcServerOptionsPayloadExtensions
{
    /// <summary>
    /// Appends <see cref="PayloadFilter"/> to the filters, sets <see cref="PayloadParameterBinder"/> as the parameter
    /// binder, and answers <see cref="InvalidPayloadException"/> with <see cref="JsonRpcErrorCodes.InvalidParams"/>.
    /// </summary>
    /// <param name="options">The server options.</param>
    /// <param name="payloadOptions">The payload settings shared with the clients.</param>
    /// <param name="policy">The application's answers about each call.</param>
    /// <param name="replayStore">Where sequence numbers are remembered; an in-memory store when <see langword="null"/>.</param>
    /// <returns>The server options.</returns>
    /// <remarks>
    /// Filters added before this call run before the envelope is opened, which is where an access check belongs; filters
    /// added after it see the opened <see cref="PayloadRequest"/> and the method's own return value. Set another
    /// parameter binder afterwards to replace <see cref="PayloadParameterBinder"/>.
    /// <para>
    /// The <see cref="JsonRpcServerOptions.ExceptionMapper"/> set before this call keeps answering first; only an
    /// exception it leaves (returns <see langword="null"/> for) is mapped here. Every other payload failure, a replayed
    /// frame, a failed HMAC, a foreign type name or a missing key alike, is left to the default
    /// <see cref="JsonRpcErrorCodes.InternalError"/>, so the answer does not tell a caller which check failed. A mapper set
    /// after this call replaces the mapping.
    /// </para>
    /// </remarks>
    public static JsonRpcServerOptions UsePayload(this JsonRpcServerOptions options, PayloadOptions payloadOptions,
        IPayloadServerPolicy policy, IPayloadReplayStore? replayStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Filters.Add(new PayloadFilter(payloadOptions, policy, replayStore));
        options.ParameterBinder = new PayloadParameterBinder(payloadOptions);

        // The host's mapper answers first and sees the payload's own exception types, so a host with its own error
        // contract keeps its codes.
        var hostMapper = options.ExceptionMapper;
        options.ExceptionMapper = (exception, context) => hostMapper?.Invoke(exception, context) ?? MapPayloadException(exception);
        return options;
    }

    private static JsonRpcError? MapPayloadException(Exception exception) => exception is InvalidPayloadException
        ? new JsonRpcError(JsonRpcErrorCodes.InvalidParams, "Invalid params")
        : null;
}
