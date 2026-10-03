using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Polhem.JsonRpc.Payload;

/// <summary>
/// Encrypts payload bodies with AES-256-CBC and PKCS#7 padding, authenticated by HMAC-SHA256 (encrypt-then-MAC).
/// </summary>
/// <remarks>
/// The key is 64 bytes: the AES key followed by the HMAC key. The output is laid out as the IV length (32-bit
/// little-endian), a random 16-byte IV, the ciphertext length (32-bit little-endian), the ciphertext, and the HMAC of
/// everything before it. This layout is part of the wire format that other clients implement; changing it is a
/// breaking change of the protocol.
/// </remarks>
public sealed class AesCbcHmacPayloadEncryptor : IPayloadEncryptor
{
    /// <summary>The size of the combined key, in bytes.</summary>
    public const int KeySize = 64;

    private const int AesKeySize = 32;
    private const int LengthPrefixSize = sizeof(int);
    private const int IvSize = 16;
    private const int HmacSize = 32;

    // The IV length, a 16-byte IV, the ciphertext length, one AES block of ciphertext and the HMAC.
    private const int MinimumSize = LengthPrefixSize + IvSize + LengthPrefixSize + 16 + HmacSize;

    /// <inheritdoc/>
    public string Name => "aes-cbc-hmac";

    /// <inheritdoc/>
    /// <exception cref="CryptographicException">The key is not <see cref="KeySize"/> bytes.</exception>
    public byte[] Encrypt(byte[] bytes, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var (aesKey, hmacKey) = SplitKey(key);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        Span<byte> iv = stackalloc byte[IvSize];
        RandomNumberGenerator.Fill(iv);

        int cipherLength = aes.GetCiphertextLengthCbc(bytes.Length, PaddingMode.PKCS7);
        int headerLength = LengthPrefixSize + IvSize + LengthPrefixSize;
        var result = new byte[headerLength + cipherLength + HmacSize];
        BinaryPrimitives.WriteInt32LittleEndian(result, IvSize);
        iv.CopyTo(result.AsSpan(LengthPrefixSize));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(LengthPrefixSize + IvSize), cipherLength);
        aes.EncryptCbc(bytes, iv, result.AsSpan(headerLength, cipherLength), PaddingMode.PKCS7);
        HMACSHA256.HashData(hmacKey, result.AsSpan(0, headerLength + cipherLength),
            result.AsSpan(headerLength + cipherLength, HmacSize));
        return result;
    }

    /// <inheritdoc/>
    /// <exception cref="CryptographicException">
    /// The key is not <see cref="KeySize"/> bytes, the data is malformed, or its HMAC does not match.
    /// </exception>
    public byte[] Decrypt(byte[] bytes, byte[] key)
    {
        var (aesKey, hmacKey) = SplitKey(key);
        if (bytes == null || bytes.Length < MinimumSize)
            throw new CryptographicException("Invalid encrypted data.");

        // The IV length is read from the data, so it is bounded before it is used as an offset.
        int ivLength = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (ivLength < IvSize || ivLength > 32)
            throw new CryptographicException("Invalid IV length.");

        int cipherLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(LengthPrefixSize + ivLength));
        if (cipherLength <= 0 || cipherLength > bytes.Length - ivLength - (2 * LengthPrefixSize) - HmacSize)
            throw new CryptographicException("Invalid cipher data length.");

        int headerLength = LengthPrefixSize + ivLength + LengthPrefixSize;
        Span<byte> computed = stackalloc byte[HmacSize];
        HMACSHA256.HashData(hmacKey, bytes.AsSpan(0, headerLength + cipherLength), computed);
        if (!CryptographicOperations.FixedTimeEquals(bytes.AsSpan(headerLength + cipherLength, HmacSize), computed))
            throw new CryptographicException("HMAC validation failed.");

        // The bound above admits 17 to 32 bytes, which AES-CBC cannot use as an IV. Rejected only after the HMAC check,
        // so a forged length is reported the same way as any other forgery.
        if (ivLength != IvSize)
            throw new CryptographicException("Invalid IV length.");

        using var aes = Aes.Create();
        aes.Key = aesKey;
        return aes.DecryptCbc(bytes.AsSpan(headerLength, cipherLength), bytes.AsSpan(LengthPrefixSize, IvSize),
            PaddingMode.PKCS7);
    }

    private static (byte[] AesKey, byte[] HmacKey) SplitKey(byte[] key)
    {
        if (key == null || key.Length != KeySize)
            throw new CryptographicException($"The key must be {KeySize} bytes.");
        return (key[..AesKeySize], key[AesKeySize..]);
    }
}
