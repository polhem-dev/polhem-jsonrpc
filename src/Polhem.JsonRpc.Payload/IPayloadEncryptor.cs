namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Encrypts the framed body of an encrypted payload, and decrypts it.
/// </summary>
/// <remarks>
/// Both ends of a connection must use the same encryptor; the envelope does not name it. Decryption must authenticate
/// the data before it returns anything.
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
}
