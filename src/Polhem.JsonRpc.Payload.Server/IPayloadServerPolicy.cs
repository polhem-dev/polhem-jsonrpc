using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// What <see cref="PayloadFilter"/> asks the application about a call: the key, the replay rules and the type to decode
/// into.
/// </summary>
/// <remarks>
/// Every member receives the request context, whose <see cref="JsonRpcRequestContext.Services"/>
/// and <see cref="JsonRpcRequestContext.Items"/> carry what an earlier filter or the object
/// factory established about the caller.
/// </remarks>
public interface IPayloadServerPolicy
{
    /// <summary>Gets the key of an encrypted call.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>The key, or <see langword="null"/> when the caller has none; the call then fails.</returns>
    ValueTask<byte[]?> GetKeyAsync(JsonRpcRequestContext context);

    /// <summary>
    /// Gets the scope a sequence number must be unique in, usually the caller's session.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The scope, or <see langword="null"/> when sequence numbers are not checked for this caller.</returns>
    string? GetReplayScope(JsonRpcRequestContext context) => null;

    /// <summary>Says whether the method rejects a sequence number the scope already used.</summary>
    /// <param name="context">The request context.</param>
    /// <returns><see langword="true"/> to check the sequence number. The default checks none.</returns>
    bool RequiresUniqueSequence(JsonRpcRequestContext context) => false;

    /// <summary>
    /// Gets the type an encoded body is decoded into. The <c>type</c> member of the request is only checked against it.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The type. The default is the parameter type of the method.</returns>
    Type GetPayloadType(JsonRpcRequestContext context)
        => (context.Method ?? throw new InvalidOperationException("The method is not resolved.")).ParameterType;
}
