using System.ComponentModel;
using System.Security.Cryptography;
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
}
