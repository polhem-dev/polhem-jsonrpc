using Polhem.JsonRpc.Server;

namespace Polhem.JsonRpc.Payload.Server;

/// <summary>
/// What <see cref="PayloadFilter"/> asks the application about a call: the key, the lowest format it accepts, the
/// replay rules and the type to decode into.
/// </summary>
/// <remarks>
/// Every member receives the request context, whose <see cref="JsonRpcRequestContext.Services"/>
/// and <see cref="JsonRpcRequestContext.Items"/> carry what an earlier filter or the object
/// factory established about the caller.
/// <para>
/// <see cref="GetKeyAsync"/> is asked only for an encrypted call. When frames are required,
/// <see cref="RequiresUniqueSequence"/> is asked about a plain or encoded call too, before any key, and
/// <see cref="GetReplayScope"/> only when it answers <see langword="true"/>; for an encrypted call both are asked after
/// the key. Answer them from the context alone, not from state <see cref="GetKeyAsync"/> leaves behind
/// (<c>PayloadServerTests.Call_UniqueSequenceRequiredButNotEncrypted_IsRefused</c>).
/// </para>
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
    /// <remarks>
    /// IMPORTANT: take the scope from the caller's session as an earlier filter or the object factory authenticated it,
    /// the session whose key encrypts the caller's calls, and read it from the context, not from <see cref="GetKeyAsync"/>:
    /// it is asked about plain and encoded calls, for which no key is asked. A scope read from an unauthenticated header
    /// lets a captured call be replayed under a new scope, where its sequence number has not been seen.
    /// </remarks>
    /// <param name="context">The request context.</param>
    /// <returns>The scope, or <see langword="null"/> when sequence numbers are not checked for this caller.</returns>
    string? GetReplayScope(JsonRpcRequestContext context) => null;

    /// <summary>
    /// Gets the lowest format the method accepts. A call in a lower format is refused before a key is asked for or its
    /// body is read.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The format. The default is <see cref="PayloadFormat.Plain"/>, which accepts every format.</returns>
    /// <remarks>
    /// The frame and its replay checks only bind a caller that has to use them. A plain call carries no frame, and an
    /// encoded call carries one that anybody can write, so only <see cref="PayloadFormat.Encrypted"/> makes them
    /// impossible to skip.
    /// </remarks>
    PayloadFormat GetMinimumFormat(JsonRpcRequestContext context) => PayloadFormat.Plain;

    /// <summary>Says whether the method rejects a sequence number the scope already used.</summary>
    /// <param name="context">The request context.</param>
    /// <returns><see langword="true"/> to check the sequence number. The default checks none.</returns>
    /// <remarks>
    /// Sequence numbers are checked only where there is something to check: <see cref="PayloadOptions.RequireFrame"/> is
    /// on and <see cref="GetReplayScope"/> answers a scope. Then a method that answers <see langword="true"/> checks the
    /// sequence number of an encrypted call, and refuses a plain or encoded one with an
    /// <see cref="InvalidPayloadException"/>, because only an encrypted frame is covered by the HMAC. Without frames or a
    /// scope nothing is checked, and a repeated call runs again.
    /// </remarks>
    bool RequiresUniqueSequence(JsonRpcRequestContext context) => false;

    /// <summary>
    /// Gets the type an encoded body is decoded into. The <c>type</c> member of the request is only checked against it.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>The type. The default is the parameter type of the method.</returns>
    Type GetPayloadType(JsonRpcRequestContext context)
        => (context.Method ?? throw new InvalidOperationException("The method is not resolved.")).ParameterType;
}
