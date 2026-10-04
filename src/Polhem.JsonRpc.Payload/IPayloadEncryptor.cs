namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Encrypts the framed body of an encrypted payload, and decrypts it.
/// </summary>
/// <remarks>
/// Both ends of a connection must use the same encryptor; the envelope does not name it. Decryption must authenticate
/// the data before it returns anything.
/// <para>
/// <see cref="PayloadProcessor"/> encrypts and decrypts with the overloads that take associated data, which the
/// authentication must cover: the method and direction of the call (ADR-003). Their default implementations
/// throw, so an encryptor written before them refuses to run rather than leave the binding unchecked.
/// </para>
/// </remarks>
public interface IPayloadEncryptor
{
    /// <summary>Gets the name of the encryption method.</summary>
    string Name { get; }

    /// <summary>Encrypts data.</summary>
    /// <param name="bytes">The data.</param>
    /// <param name="key">The key of the call.</param>
    /// <returns>The encrypted data.</returns>
    byte[] Encrypt(byte[] bytes, byte[] key);

    /// <summary>Decrypts data.</summary>
    /// <param name="bytes">The encrypted data.</param>
    /// <param name="key">The key of the call.</param>
    /// <returns>The data.</returns>
    byte[] Decrypt(byte[] bytes, byte[] key);

    /// <summary>Encrypts data, authenticating associated data with it.</summary>
    /// <param name="bytes">The data.</param>
    /// <param name="key">The key of the call.</param>
    /// <param name="associatedData">Data the authentication covers but the output does not carry.</param>
    /// <returns>The encrypted data.</returns>
    /// <exception cref="NotSupportedException">The encryptor does not implement associated data.</exception>
    byte[] Encrypt(byte[] bytes, byte[] key, ReadOnlySpan<byte> associatedData)
        => throw new NotSupportedException($"The payload encryptor '{Name}' does not authenticate associated data.");

    /// <summary>Decrypts data, checking that it was encrypted with the same associated data.</summary>
    /// <param name="bytes">The encrypted data.</param>
    /// <param name="key">The key of the call.</param>
    /// <param name="associatedData">The associated data the data was encrypted with.</param>
    /// <returns>The data.</returns>
    /// <exception cref="NotSupportedException">The encryptor does not implement associated data.</exception>
    byte[] Decrypt(byte[] bytes, byte[] key, ReadOnlySpan<byte> associatedData)
        => throw new NotSupportedException($"The payload encryptor '{Name}' does not authenticate associated data.");
}
