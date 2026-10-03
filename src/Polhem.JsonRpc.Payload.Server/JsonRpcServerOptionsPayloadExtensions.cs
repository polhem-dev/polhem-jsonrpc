using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// Adds the payload envelope to a server.
/// </summary>
public static class JsonRpcServerOptionsPayloadExtensions
{
    /// <summary>
    /// Appends <see cref="PayloadFilter"/> to the filters and sets <see cref="PayloadParameterBinder"/> as the parameter
    /// binder.
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
    /// </remarks>
    public static JsonRpcServerOptions UsePayload(this JsonRpcServerOptions options, PayloadOptions payloadOptions,
        IPayloadServerPolicy policy, IPayloadReplayStore? replayStore = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Filters.Add(new PayloadFilter(payloadOptions, policy, replayStore));
        options.ParameterBinder = new PayloadParameterBinder(payloadOptions);
        return options;
    }
}
