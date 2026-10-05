namespace Polhem.JsonRpc.Payload.Client;

/// <summary>
/// Settings for <see cref="PayloadConnector"/>.
/// </summary>
/// <remarks>
/// The connector reads them when it is created; later changes to an options instance do not reach a connector that
/// already exists. The key is the exception: <see cref="KeyProvider"/> is asked on every call, so a key that changes
/// after a sign-in is picked up.
/// </remarks>
public sealed class PayloadConnectorOptions
{
    /// <summary>
    /// Gets or sets the format of a call that does not name one. The default is <see cref="PayloadFormat.Encrypted"/>.
    /// </summary>
    public PayloadFormat Format { get; set; } = PayloadFormat.Encrypted;

    /// <summary>
    /// Gets or sets the codec the parameters are encoded with, or <see langword="null"/> for
    /// <see cref="PayloadOptions.DefaultCodec"/>. The server answers in the same codec.
    /// </summary>
    public string? Codec { get; set; }

    /// <summary>
    /// Gets or sets a function that returns the key of an encrypted call, asked on every encrypted call.
    /// </summary>
    /// <remarks>
    /// An encrypted call fails with <see cref="InvalidOperationException"/> while it is <see langword="null"/> or
    /// returns no key. The connector never falls back to a lower format.
    /// </remarks>
    public Func<byte[]?>? KeyProvider { get; set; }

    /// <summary>
    /// Gets or sets a function that returns the sequence number of the next call, or <see langword="null"/> to number
    /// the calls of the connector 1, 2, 3 and so on.
    /// </summary>
    /// <remarks>
    /// A number is taken only when <see cref="PayloadOptions.RequireFrame"/> is on and the call is encoded or encrypted.
    /// Set it when the numbers must outlive the connector: a server that checks sequence numbers refuses one its replay
    /// scope has already seen, so a connector that starts again at 1 under the same scope is refused.
    /// </remarks>
    public Func<long>? SequenceGenerator { get; set; }
}
