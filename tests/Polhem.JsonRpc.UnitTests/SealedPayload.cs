using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Polhem.JsonRpc.UnitTests;

/// <summary>
/// A stand-in for a host's payload protection: gzip, then AES, then base64 inside a JSON object. It proves that a
/// filter or an interceptor can replace params and results with something that is not the plain value.
/// </summary>
internal static class SealedPayload
{
    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(32);

    public static JsonElement Seal(JsonElement value)
    {
        using var aes = Aes.Create();
        aes.Key = s_key;
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest))
        {
            gzip.Write(JsonSerializer.SerializeToUtf8Bytes(value));
        }
        var cipher = aes.EncryptCbc(compressed.ToArray(), aes.IV);
        return JsonSerializer.SerializeToElement(new { iv = Convert.ToBase64String(aes.IV), data = Convert.ToBase64String(cipher) });
    }

    public static JsonElement Open(JsonElement sealedValue)
    {
        using var aes = Aes.Create();
        aes.Key = s_key;
        var iv = Convert.FromBase64String(sealedValue.GetProperty("iv").GetString()!);
        var plain = aes.DecryptCbc(Convert.FromBase64String(sealedValue.GetProperty("data").GetString()!), iv);
        using var gzip = new GZipStream(new MemoryStream(plain), CompressionMode.Decompress);
        return JsonDocument.Parse(gzip).RootElement.Clone();
    }
}
