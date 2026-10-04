namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Which way an encrypted payload travels. It is part of what the HMAC binds, so a response cannot be sent back as a
/// request.
/// </summary>
internal enum PayloadDirection
{
    /// <summary>The parameters of a call, from the client to the server.</summary>
    Request = 1,

    /// <summary>The result of a call, from the server to the client.</summary>
    Response = 2,
}
