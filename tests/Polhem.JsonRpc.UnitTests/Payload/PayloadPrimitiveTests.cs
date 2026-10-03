using System.Buffers.Binary;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadPrimitiveTests
{
    [Fact]
    [DisplayName("Gzip: decompression stops at the size limit")]
    public void Gzip_DecompressBeyondLimit_Throws()
    {
        var compressed = new GzipPayloadCompressor().Compress(new byte[4096]);

        Assert.Throws<InvalidDataException>(() => new GzipPayloadCompressor(1024).Decompress(compressed));
    }

    [Fact]
    [DisplayName("Gzip: a body that expands more than the ratio allows is refused below the size limit")]
    public void Gzip_DecompressBeyondRatio_Throws()
    {
        var compressed = new GzipPayloadCompressor().Compress(new byte[4 * 1024 * 1024]);

        Assert.True(compressed.Length * GzipPayloadCompressor.DefaultMaxCompressionRatio < 4 * 1024 * 1024);
        Assert.Throws<InvalidDataException>(() => new GzipPayloadCompressor().Decompress(compressed));
    }

    [Fact]
    [DisplayName("Gzip: a body of up to 1 MiB is not refused for its ratio")]
    public void Gzip_DecompressSmallBodyBeyondRatio_Succeeds()
    {
        var compressed = new GzipPayloadCompressor().Compress(new byte[1024 * 1024]);

        Assert.Equal(1024 * 1024, new GzipPayloadCompressor().Decompress(compressed).Length);
    }

    [Fact]
    [DisplayName("Gzip: a large JSON body of ordinary records is within the ratio")]
    public void Gzip_DecompressLargeJson_Succeeds()
    {
        var json = Encoding.UTF8.GetBytes("[" + string.Join(",", Enumerable.Range(0, 100_000)
            .Select(i => $$"""{"id":{{i}},"name":"Item {{i}}","price":{{i * 1.25}},"active":{{(i % 3 == 0 ? "true" : "false")}}}"""))
            + "]");
        var compressed = new GzipPayloadCompressor().Compress(json);

        Assert.True(json.Length > 1024 * 1024);
        Assert.Equal(json, new GzipPayloadCompressor().Decompress(compressed));
    }

    [Fact]
    [DisplayName("Gzip: a ratio limit that is not positive is refused")]
    public void Gzip_NotPositiveRatio_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GzipPayloadCompressor(1024, 0));
    }

    [Fact]
    [DisplayName("AES-CBC-HMAC: the same data encrypts differently each time, because the IV is random")]
    public void AesCbcHmac_Encrypt_UsesFreshIv()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

        Assert.NotEqual(encryptor.Encrypt([1, 2, 3], key), encryptor.Encrypt([1, 2, 3], key));
    }

    [Theory]
    [DisplayName("AES-CBC-HMAC: a key that is not 64 bytes is refused")]
    [InlineData(0)]
    [InlineData(32)]
    public void AesCbcHmac_WrongKeySize_Throws(int size)
    {
        Assert.Throws<CryptographicException>(() => new AesCbcHmacPayloadEncryptor().Encrypt([1], new byte[size]));
    }

    [Fact]
    [DisplayName("AES-CBC-HMAC: data encrypted with one key fails authentication under another")]
    public void AesCbcHmac_WrongKey_FailsAuthentication()
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var encrypted = encryptor.Encrypt([1, 2, 3], RandomNumberGenerator.GetBytes(64));

        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, RandomNumberGenerator.GetBytes(64)));
    }

    [Theory]
    [DisplayName("AES-CBC-HMAC: data whose length fields do not fit it is refused as a cryptographic error, never an out-of-range")]
    [InlineData("shorter than the minimum")]
    [InlineData("last byte missing")]
    [InlineData("IV length 0")]
    [InlineData("IV length 17")]
    [InlineData("IV length beyond the data")]
    [InlineData("cipher length 0")]
    [InlineData("cipher length negative")]
    [InlineData("cipher length beyond the data")]
    public void AesCbcHmac_Decrypt_MalformedLayout_ThrowsCryptographicException(string malformation)
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

    [Fact]
    [DisplayName("Frame: data shorter than a frame or of another version is refused")]
    public void Frame_Extract_Invalid_ThrowsReplayRejected()
    {
        Assert.Throws<ReplayRejectedException>(() => PayloadFrame.Extract(new byte[16], out _));
        var otherVersion = new PayloadFrame(0, 0).Prepend([]);
        otherVersion[0] = 2;
        Assert.Throws<ReplayRejectedException>(() => PayloadFrame.Extract(otherVersion, out _));
    }

    [Fact]
    [DisplayName("Type registry: a name or a type registers once")]
    public void TypeRegistry_Conflict_Throws()
    {
        var registry = new PayloadTypeRegistry().Register<VectorPing>();

        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(string), registry.GetTypeName(typeof(VectorPing))));
        Assert.Throws<InvalidOperationException>(() => registry.Register(typeof(VectorPing), "other"));
        Assert.Equal("Polhem.JsonRpc.UnitTests.Payload.VectorPing, Polhem.JsonRpc.UnitTests", registry.GetTypeName(typeof(VectorPing)));
    }

    [Fact]
    [DisplayName("Type registry: an unregistered type is named by default and accepted only as the type the reader chose")]
    public void TypeRegistry_Unregistered_NamedButNotResolved()
    {
        var registry = new PayloadTypeRegistry();
        var name = registry.GetTypeName(typeof(VectorPing));

        Assert.Equal("Polhem.JsonRpc.UnitTests.Payload.VectorPing, Polhem.JsonRpc.UnitTests", name);
        Assert.True(registry.IsNameOf(name, typeof(VectorPing)));
        Assert.False(registry.IsNameOf(name, typeof(string)));
        Assert.False(registry.TryResolveType(name, out _));
    }

    [Fact]
    [DisplayName("Type registry: a type registered under its own name is accepted only under that name")]
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
