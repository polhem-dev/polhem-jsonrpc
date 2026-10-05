using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

/// <summary>
/// The wire format is the one the Polhem framework wrote before the payload code moved into this package
/// (maintainers/adr/adr-002-payload-packages.md, decision 2). Every vector here was produced by the Polhem
/// implementation (polhem-dev/polhem at 57607b6) and is never regenerated from this package: a vector that has to
/// change means the wire changed. ADR-003 changed it once: the HMAC of an encrypted payload also covers the method and
/// the direction, so Polhem's encrypted vector no longer authenticates, and the writer's HMAC is checked against that
/// rule without this package's code.
/// </summary>
public class PayloadWireVectorTests
{
    // The bytes 0 to 63: the AES key followed by the HMAC key.
    private static readonly byte[] s_key = [.. Enumerable.Range(0, 64).Select(i => (byte)i)];

    private const string Plaintext = "Polhem payload vector";

    [Fact(DisplayName = "Wire vector: a frame is a version byte, then the timestamp and the sequence as big-endian 64-bit integers")]
    public void Frame_Prepend_MatchesPolhemBytes()
    {
        var framed = new PayloadFrame(1_700_000_000_123, 42).Prepend([1, 2, 3]);

        Assert.Equal("010000018BCFE5687B000000000000002A010203", Convert.ToHexString(framed));
    }

    [Fact(DisplayName = "Wire vector: a frame written by Polhem reads back with its timestamp, sequence and body")]
    public void Frame_Extract_ReadsPolhemBytes()
    {
        var frame = PayloadFrame.Extract(Convert.FromHexString("010000018BCFE5687B000000000000002A010203"), out var body);

        Assert.Equal(1_700_000_000_123, frame.TimestampMs);
        Assert.Equal(42, frame.Sequence);
        Assert.Equal([1, 2, 3], body);
    }

    [Fact(DisplayName = "Wire vector: ciphertext written by Polhem's AES-CBC-HMAC decrypts")]
    public void AesCbcHmacDecrypt_PolhemCiphertext_ReturnsPlaintext()
    {
        var ciphertext = Convert.FromBase64String(
            "EAAAAOZlb3niIpxk4e6PozqJ4ckgAAAAn5a9ie5RdLHoyhKGHZ5Zbxd8Bal2MtBC3gGCF0FZ7wihs3jmYn2N6yauIdmTCNlVKhgm/BjqaMAiQCXOevcRhQ==");

        var plain = new AesCbcHmacPayloadEncryptor().Decrypt(ciphertext, s_key);

        Assert.Equal(Plaintext, Encoding.UTF8.GetString(plain));
    }

    [Fact(DisplayName = "Wire vector: a body compressed by Polhem decompresses")]
    public void GzipDecompress_PolhemBytes_ReturnsPlaintext()
    {
        var plain = new GzipPayloadCompressor().Decompress(
            Convert.FromBase64String("H4sIAAAAAAAEEwvIz8lIzVUoSKzMyU9MUShLTS7JLwIAVAejaRUAAAA="));

        Assert.Equal(Plaintext, Encoding.UTF8.GetString(plain));
    }

    [Fact(DisplayName = "Wire vector: an encoded envelope written by Polhem opens into the body type")]
    public void Open_PolhemEncodedEnvelope_ReturnsValue()
    {
        const string json = """{"format":1,"value":"H4sIAAAAAAAEE6tWSs7JTM0r8UvMTVWyUipLTS7JL1LSUSopSkxO9UxRslIq0TVU0lEqSCxKzE0tSS0qVrKKjq0FADnUwRE3AAAA","type":"Polhem.Api.Core.Messages.System.PingRequest, Polhem.Api.Core","codec":"json"}""";

        var ping = Assert.IsType<VectorPing>(CreateProcessor(requireFrame: false).Unwrap(Parse(json)));

        Assert.Equal("vector", ping.ClientName);
        Assert.Equal("t-1", ping.TraceId);
    }

    [Fact(DisplayName = "Wire vector: an encrypted envelope written by Polhem before ADR-003 keeps its layout, but its HMAC binds no method, so it is refused")]
    public void Open_PolhemEncryptedFramedEnvelope_RefusedWithoutBinding()
    {
        const string json = """{"format":2,"value":"EAAAAP+MfBW24ORVEBRAil6/IOFgAAAAsjNkN/1BqmCCUJfqODQU/QfeBevI++phd9Qx7vGAurn2Mv05Asg3S32frwAvaVKYXr6mJHmE+IrqNK+ns3UfUyZRGyaOuoFgc9KOe8J+GwGsIz4l4M3IgwQkWhNskSvlMFwQ1PCSA3cnPK9PtKWBj+gqgj6iX0MflH1l1hJwXVc=","type":"Polhem.Api.Core.Messages.System.PingRequest, Polhem.Api.Core","codec":"json"}""";
        var envelope = PayloadEnvelope.Read(Parse(json));

        var framed = new AesCbcHmacPayloadEncryptor().Decrypt(envelope.Body!, s_key);
        var frame = PayloadFrame.Extract(framed, out _);

        Assert.Equal(1_700_000_000_123, frame.TimestampMs);
        Assert.Equal(7, frame.Sequence);
        Assert.Throws<CryptographicException>(() =>
            CreateProcessor(requireFrame: true).OpenRequest(envelope, typeof(VectorPing), s_key, "System.Ping", out _));
    }

    [Fact(DisplayName = "Wire vector: a plain envelope is written exactly as Polhem wrote it")]
    public void Wrap_Plain_MatchesPolhemEnvelope()
    {
        var element = CreateProcessor(requireFrame: false)
            .Wrap(new VectorPing { ClientName = "vector", TraceId = "t-1" }, PayloadFormat.Plain);

        Assert.Equal("""{"format":0,"value":{"clientName":"vector","traceId":"t-1"},"type":""}""", element.GetRawText());
    }

    [Fact(DisplayName = "Wire vector: an encoded envelope is written with Polhem's members and a gzip body of Polhem's JSON bytes")]
    public void Wrap_Encoded_WritesPolhemEnvelopeAndBody()
    {
        var element = CreateWriter(requireFrame: false, clock: null)
            .Wrap(new WriterPing { ClientName = "vector", TraceId = "t-1" }, PayloadFormat.Encoded, codec: "json");

        Assert.Equal(["format", "value", "type", "codec"], element.EnumerateObject().Select(member => member.Name));
        Assert.Equal(1, element.GetProperty("format").GetInt32());
        Assert.Equal(VectorPing.PolhemTypeName, element.GetProperty("type").GetString());
        Assert.Equal("json", element.GetProperty("codec").GetString());
        Assert.Equal(PolhemBody, Encoding.UTF8.GetString(Gunzip(element.GetProperty("value").GetBytesFromBase64())));
    }

    [Fact(DisplayName = "Wire vector: an encrypted, framed body is laid out as Polhem lays it out, with an HMAC over the direction and the method after the ciphertext, read without this package's code")]
    public void Wrap_EncryptedFramed_WritesPolhemLayout()
    {
        var element = CreateWriter(requireFrame: true, clock: new FixedClock(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123)))
            .WrapRequest("System.Ping", new WriterPing { ClientName = "vector", TraceId = "t-1" }, PayloadFormat.Encrypted, codec: "json", key: s_key, sequence: 7);
        var data = element.GetProperty("value").GetBytesFromBase64();

        // IV length and IV, cipher length and ciphertext, then the HMAC of everything before it followed by the binding:
        // 0x01 for a request, then the method in UTF-8 (ADR-003).
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(data));
        var iv = data[4..20];
        var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
        Assert.Equal(data.Length, 24 + cipherLength + 32);
        byte[] binding = [0x01, .. Encoding.UTF8.GetBytes("System.Ping")];
        Assert.Equal(HMACSHA256.HashData(s_key[32..], (byte[])[.. data[..(24 + cipherLength)], .. binding]), data[(24 + cipherLength)..]);

        using var aes = Aes.Create();
        aes.Key = s_key[..32];
        var framed = aes.DecryptCbc(data.AsSpan(24, cipherLength), iv, PaddingMode.PKCS7);

        Assert.Equal("010000018BCFE5687B0000000000000007", Convert.ToHexString(framed[..17]));
        Assert.Equal(PolhemBody, Encoding.UTF8.GetString(Gunzip(framed[17..])));
    }

    // The enum behind the direction byte is not used here: its values are the wire format other implementations read.
    // The second method name is not ASCII, so an encoding other than UTF-8 gives other bytes.
    [Theory(DisplayName = "Wire vector: the HMAC of encrypted parameters covers 0x01 and of an encrypted result 0x02, then the method in UTF-8")]
    [InlineData(1, "System.Ping")]
    [InlineData(2, "System.Ping")]
    [InlineData(1, "Système.Ping")]
    [InlineData(2, "Système.Ping")]
    public void WrapRequestAndSealResponse_Encrypted_HmacCoversDirectionAndUtf8Method(byte direction, string method)
    {
        var writer = CreateWriter(requireFrame: true, clock: new FixedClock(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123)));
        var value = new WriterPing { ClientName = "vector", TraceId = "t-1" };
        var element = direction == 1
            ? writer.WrapRequest(method, value, PayloadFormat.Encrypted, codec: "json", key: s_key, sequence: 7)
            : writer.SealResponse(method, value, PayloadFormat.Encrypted, codec: "json", key: s_key).ToElement();
        var data = element.GetProperty("value").GetBytesFromBase64();

        var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
        byte[] binding = [direction, .. Encoding.UTF8.GetBytes(method)];
        Assert.Equal(HMACSHA256.HashData(s_key[32..], (byte[])[.. data[..(24 + cipherLength)], .. binding]), data[(24 + cipherLength)..]);
    }

    // Fixed vectors for other implementations of ADR-003 (polhem-connector-js): the key is the bytes 0 to 63, the plaintext
    // UTF-8 "Polhem ADR-003 binding vector", and the binding a direction byte followed by the method in UTF-8.
    [Theory(DisplayName = "Wire vector: ciphertext bound to a direction and a method decrypts with that binding only")]
    [InlineData(1, "EAAAAHYX5QeZX7jgLkq0NikmnpwgAAAAlY+Koj/+oH9tjwsGhMTJvpfpitn868rVNk1xXDCE6rttN7ChlpKWSuNVBWzvWb6tOEsV+p2NWiJMaEN5NLjfvw==")]
    [InlineData(2, "EAAAAMMx+unW7jBIKA21I71abasgAAAA1wEXFaPoQxELAAFE/So1ZQRKNvzKJPspzQ9cb2SF8KIJvAM2Wt+jZpY0oUygUhM5JS6OpQbxsKD6IeqdHKA9Ew==")]
    public void AesCbcHmacDecrypt_BoundVector_OpensWithItsBindingOnly(byte direction, string base64)
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var ciphertext = Convert.FromBase64String(base64);
        byte[] binding = [direction, .. Encoding.UTF8.GetBytes("Employee.GetList")];
        byte[] otherDirection = [(byte)(3 - direction), .. Encoding.UTF8.GetBytes("Employee.GetList")];

        Assert.Equal("Polhem ADR-003 binding vector", Encoding.UTF8.GetString(encryptor.Decrypt(ciphertext, s_key, binding)));
        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(ciphertext, s_key, otherDirection));
        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(ciphertext, s_key));
    }

    // The body inside Polhem's encoded vector above, decompressed.
    private const string PolhemBody = """{"clientName":"vector","traceId":"t-1","parameters":[]}""";

    private static byte[] Gunzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static PayloadProcessor CreateWriter(bool requireFrame, TimeProvider? clock) => new(new PayloadOptions
    {
        RequireFrame = requireFrame,
        TimeProvider = clock ?? TimeProvider.System,
        TypeResolver = new PayloadTypeRegistry().Register(typeof(WriterPing), VectorPing.PolhemTypeName),
    });

    /// <summary>The members of Polhem's <c>PingRequest</c>, in its order, so the written body can be compared byte for byte.</summary>
    private sealed class WriterPing
    {
        public string? ClientName { get; set; }

        public string? TraceId { get; set; }

        public string[] Parameters { get; set; } = [];
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PayloadProcessor CreateProcessor(bool requireFrame)
    {
        var options = new PayloadOptions
        {
            RequireFrame = requireFrame,
            TypeResolver = new PayloadTypeRegistry().Register(typeof(VectorPing), VectorPing.PolhemTypeName),
        };
        return new PayloadProcessor(options);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
