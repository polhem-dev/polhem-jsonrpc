using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadPrimitiveTests
{
    [Fact(DisplayName = "Gzip: decompression stops at the size limit")]
    public void Gzip_DecompressBeyondLimit_Throws()
    {
        var compressed = new GzipPayloadCompressor().Compress(new byte[4096]);

        Assert.Throws<InvalidDataException>(() => new GzipPayloadCompressor(1024).Decompress(compressed));
    }

    [Fact(DisplayName = "Gzip: a body without the gzip header is read as it is, so a writer may leave it uncompressed")]
    public void Gzip_DecompressUncompressedBody_ReturnsItAsIs()
    {
        var json = Encoding.UTF8.GetBytes("""{"clientName":"vector"}""");

        Assert.Equal(json, new GzipPayloadCompressor().Decompress(json));
    }

    [Fact(DisplayName = "Gzip: a budget larger than the compressor's own limit does not lift that limit")]
    public void Gzip_DecompressWithLargerBudget_KeepsOwnLimit()
    {
        var compressed = new GzipPayloadCompressor().Compress(new byte[4096]);

        Assert.Throws<InvalidDataException>(() => new GzipPayloadCompressor(1024).Decompress(compressed, long.MaxValue));
    }

    [Fact(DisplayName = "Gzip: a body that compresses very well decompresses up to the size limit")]
    public void Gzip_DecompressHighlyCompressible_Succeeds()
    {
        var zeros = new byte[4 * 1024 * 1024];

        Assert.Equal(zeros.Length, new GzipPayloadCompressor().Decompress(new GzipPayloadCompressor().Compress(zeros)).Length);
    }

    [Fact(DisplayName = "Gzip: a large JSON body of ordinary records round-trips")]
    public void Gzip_DecompressLargeJson_Succeeds()
    {
        var json = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Range(0, 100_000)
            .Select(i => $$"""{"id":{{i}},"name":"Item {{i}}","price":{{i * 1.25}},"active":{{(i % 3 == 0 ? "true" : "false")}}}"""))
            + "]");
        var compressed = new GzipPayloadCompressor().Compress(json);

        Assert.True(json.Length > 1024 * 1024);
        Assert.Equal(json, new GzipPayloadCompressor().Decompress(compressed));
    }

    [Fact(DisplayName = "AES-CBC-HMAC: the same data encrypts differently each time, because the IV is random")]
    public void AesCbcHmac_Encrypt_UsesFreshIv()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

        Assert.NotEqual(encryptor.Encrypt([1, 2, 3], key), encryptor.Encrypt([1, 2, 3], key));
    }

    [Theory(DisplayName = "AES-CBC-HMAC: a key that is not 64 bytes is refused")]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(65)]
    public void AesCbcHmac_WrongKeySize_Throws(int size)
    {
        Assert.Throws<CryptographicException>(() => new AesCbcHmacPayloadEncryptor().Encrypt([1], new byte[size]));
    }

    [Fact(DisplayName = "AES-CBC-HMAC: data encrypted with one key fails authentication under another")]
    public void AesCbcHmac_WrongKey_FailsAuthentication()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var encrypted = encryptor.Encrypt([1, 2, 3], RandomNumberGenerator.GetBytes(64));

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, RandomNumberGenerator.GetBytes(64)));
    }

    [Fact(DisplayName = "AES-CBC-HMAC: a change to the last byte of the HMAC fails authentication, so the whole tag is compared")]
    public void AesCbcHmac_TamperedTagEnd_FailsAuthentication()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(64);
        var encrypted = encryptor.Encrypt(new byte[64], key);
        encrypted[^1] ^= 0x01;

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, key));
    }

    // Were the data decrypted before the HMAC is checked, a change to the last block would fail on its padding, with a
    // message of its own: a padding oracle. The HMAC failure is the only answer to any change.
    [Fact(DisplayName = "AES-CBC-HMAC: a change to the last block is refused by the HMAC check, before any padding is read")]
    public void AesCbcHmac_TamperedLastBlock_RefusedByHmacFirst()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(64);
        var encrypted = encryptor.Encrypt(new byte[64], key);
        // The byte of the block before the last one at the place of the last padding byte: a change there always breaks
        // the padding, so a decryption before the HMAC check would fail on it every time.
        encrypted[^49] ^= 0x01;

        var ex = Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, key));

        Assert.Equal("HMAC validation failed.", ex.Message);
    }

    // Without the HMAC, a change to the IV or to a block before the last one still decrypts with valid padding, so only
    // the HMAC can refuse it; a change to the last block would also be refused by the padding check.
    [Theory(DisplayName = "AES-CBC-HMAC: a change to the IV or to the first block of ciphertext fails authentication")]
    [InlineData(4)]
    [InlineData(24)]
    public void AesCbcHmac_TamperedBeforeLastBlock_FailsAuthentication(int offset)
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(64);
        var encrypted = encryptor.Encrypt(new byte[64], key);
        encrypted[offset] ^= 0x01;

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, key));
    }

    [Theory(DisplayName = "AES-CBC-HMAC: data whose length fields do not fit it is refused as a cryptographic error, never an out-of-range")]
    [InlineData("shorter than the minimum")]
    [InlineData("last byte missing")]
    [InlineData("IV length 0")]
    [InlineData("IV length 17")]
    [InlineData("IV length beyond the data")]
    [InlineData("cipher length 0")]
    [InlineData("cipher length negative")]
    [InlineData("cipher length beyond the data")]
    public void AesCbcHmacDecrypt_MalformedLayout_ThrowsCryptographicException(string malformation)
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);
        // Layout: IV length (4, little-endian), IV, cipher length (4, little-endian), cipher, HMAC (32).
        var data = encryptor.Encrypt(new byte[100], key);
        const int CipherLengthOffset = 4 + 16;
        var malformed = malformation switch
        {
            "shorter than the minimum" => data[..40],
            "last byte missing" => data[..^1],
            "IV length 0" => WithInt32(data, 0, 0),
            "IV length 17" => WithInt32(data, 0, 17),
            "IV length beyond the data" => WithInt32(data, 0, 1000),
            "cipher length 0" => WithInt32(data, CipherLengthOffset, 0),
            "cipher length negative" => WithInt32(data, CipherLengthOffset, -1),
            "cipher length beyond the data" => WithInt32(data, CipherLengthOffset, int.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(malformation)),
        };

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(malformed, key));
    }

    [Fact(DisplayName = "AES-CBC-HMAC: an IV length of 17 to 32 with a valid HMAC is refused for its IV length, not decrypted at a shifted offset")]
    public void AesCbcHmacDecrypt_AuthenticatedOddIvLength_ThrowsForIvLength()
    {
        var key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);
        // Layout with a 17-byte IV and a 16-byte cipher, signed with the key as only its holder could.
        var data = new byte[4 + 17 + 4 + 16 + 32];
        BinaryPrimitives.WriteInt32LittleEndian(data, 17);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4 + 17), 16);
        HMACSHA256.HashData(key[32..], data.AsSpan(0, 4 + 17 + 4 + 16), data.AsSpan(4 + 17 + 4 + 16));

        var ex = Assert.Throws<CryptographicException>(() => new AesCbcHmacPayloadEncryptor().Decrypt(data, key));

        Assert.Contains("IV length", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Frame: data shorter than a frame or of another version is refused")]
    public void FrameExtract_Invalid_ThrowsReplayRejected()
    {
        Assert.Throws<ReplayRejectedException>(() => PayloadFrame.Extract(new byte[16], out _));
        var otherVersion = new PayloadFrame(0, 0).Prepend([]);
        otherVersion[0] = 2;
        Assert.Throws<ReplayRejectedException>(() => PayloadFrame.Extract(otherVersion, out _));
    }

    [Fact(DisplayName = "Type registry: a name or a type registers once")]
    public void TypeRegistry_Conflict_Throws()
    {
        var registry = new PayloadTypeRegistry().Register<VectorPing>();

        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(string), registry.GetTypeName(typeof(VectorPing))));
        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(VectorPing), "other"));
        Assert.Equal("Polhem.JsonRpc.UnitTests.Payload.VectorPing, Polhem.JsonRpc.UnitTests", registry.GetTypeName(typeof(VectorPing)));
    }

    [Fact(DisplayName = "Type registry: an unregistered type is named by default and accepted only as the type the reader chose")]
    public void TypeRegistry_Unregistered_NamedButNotResolved()
    {
        var registry = new PayloadTypeRegistry();
        var name = registry.GetTypeName(typeof(VectorPing));

        Assert.Equal("Polhem.JsonRpc.UnitTests.Payload.VectorPing, Polhem.JsonRpc.UnitTests", name);
        Assert.True(registry.IsNameOf(name, typeof(VectorPing)));
        Assert.False(registry.IsNameOf(name, typeof(string)));
        Assert.False(registry.TryResolveType(name, out _));
    }

    [Fact(DisplayName = "Type registry: a type registered under its own name is accepted only under that name")]
    public void TypeRegistry_CustomName_ReplacesDefaultName()
    {
        var registry = new PayloadTypeRegistry().Register(typeof(VectorPing), VectorPing.PolhemTypeName);

        Assert.True(registry.IsNameOf(VectorPing.PolhemTypeName, typeof(VectorPing)));
        Assert.False(registry.IsNameOf("Polhem.JsonRpc.UnitTests.Payload.VectorPing, Polhem.JsonRpc.UnitTests", typeof(VectorPing)));
    }

    private static byte[] WithInt32(byte[] data, int offset, int value)
    {
        var copy = (byte[])data.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(offset), value);
        return copy;
    }
}
