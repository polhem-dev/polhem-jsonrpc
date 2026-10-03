namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Leaves payload bodies unencrypted while still marking them as encrypted. Meant for development only.
/// </summary>
/// <remarks>
/// <see cref="PayloadProcessor"/> refuses to use it unless <see cref="PayloadOptions.AllowNoEncryption"/> is set, so a
/// deployment cannot switch encryption off by changing only the encryptor.
/// </remarks>
public sealed class NoPayloadEncryptor : IPayloadEncryptor
{
    /// <summary>Gets the shared instance.</summary>
    public static NoPayloadEncryptor Instance { get; } = new();

    private NoPayloadEncryptor() { }

    /// <inheritdoc/>
    public string Name => "none";

    /// <inheritdoc/>
    public byte[] Encrypt(byte[] bytes, byte[] key) => bytes ?? throw new ArgumentNullException(nameof(bytes));

    /// <inheritdoc/>
    public byte[] Decrypt(byte[] bytes, byte[] key) => bytes ?? throw new ArgumentNullException(nameof(bytes));
}
