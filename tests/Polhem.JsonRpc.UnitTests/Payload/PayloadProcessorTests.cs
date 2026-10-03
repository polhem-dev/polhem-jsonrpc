using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

public class PayloadProcessorTests
{
    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

    [Theory]
    [DisplayName("Processor: a value wrapped in each format unwraps to an equal value")]
    [InlineData(PayloadFormat.Encoded, false)]
    [InlineData(PayloadFormat.Encoded, true)]
    [InlineData(PayloadFormat.Encrypted, false)]
    [InlineData(PayloadFormat.Encrypted, true)]
    public void WrapUnwrap_EncodedFormats_RoundTrip(PayloadFormat format, bool requireFrame)
    {
        var processor = CreateProcessor(requireFrame);

        var element = processor.Wrap(new VectorPing { ClientName = "a", TraceId = "b" }, format, key: s_key, sequence: 3);
        var ping = Assert.IsType<VectorPing>(processor.Unwrap(element, s_key));

        Assert.Equal("a", ping.ClientName);
        Assert.Equal("b", ping.TraceId);
    }

    [Fact]
    [DisplayName("Processor: a plain envelope unwraps to its JSON value for the caller to bind")]
    public void Unwrap_Plain_ReturnsJsonElement()
    {
        var processor = CreateProcessor(requireFrame: true);

        var value = processor.Unwrap(processor.Wrap(new VectorPing { ClientName = "a" }, PayloadFormat.Plain));

        var element = Assert.IsType<JsonElement>(value);
        Assert.Equal("a", element.GetProperty("clientName").GetString());
    }

    [Fact]
    [DisplayName("Processor: the frame carries the sequence the writer passed")]
    public void Open_RequireFrame_ReturnsWriterSequence()
    {
        var processor = CreateProcessor(requireFrame: true);
        var envelope = processor.Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 99);

        processor.Open(envelope, typeof(VectorPing), s_key, out var frame);

        Assert.Equal(99, frame!.Sequence);
    }

    [Fact]
    [DisplayName("Processor: an encrypted payload cannot be sealed without a key")]
    public void Seal_EncryptedWithoutKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateProcessor(false).Seal(new VectorPing(), PayloadFormat.Encrypted));
    }

    [Fact]
    [DisplayName("Processor: an encoded payload cannot carry a null value")]
    public void Seal_EncodedNull_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CreateProcessor(false).Seal(null, PayloadFormat.Encoded));
    }

    [Fact]
    [DisplayName("Processor: a server refuses a body whose type name differs from the type it decodes into")]
    public void Open_ServerTypeMismatch_Throws()
    {
        var processor = CreateProcessor(false);
        var envelope = processor.Seal(new VectorPing(), PayloadFormat.Encoded);

        Assert.Throws<InvalidOperationException>(() => processor.Open(envelope, typeof(string), null, out _));
    }

    [Fact]
    [DisplayName("Processor: a client refuses a type name that is not allowed, without loading it")]
    public void Open_ClientUnknownTypeName_Throws()
    {
        var processor = CreateProcessor(false);
        var sealedEnvelope = processor.Seal(new VectorPing(), PayloadFormat.Encoded);
        var forged = new PayloadEnvelope
        {
            Format = PayloadFormat.Encoded,
            Body = sealedEnvelope.Body,
            TypeName = "System.IO.FileInfo, System.Runtime",
        };

        Assert.Throws<InvalidOperationException>(() => processor.Open(forged, null, out _));
    }

    [Fact]
    [DisplayName("Processor: a tampered ciphertext fails authentication")]
    public void Open_TamperedCiphertext_ThrowsCryptographicException()
    {
        var processor = CreateProcessor(false);
        var envelope = processor.Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key);
        envelope.Body![^40] ^= 0x01;

        Assert.Throws<CryptographicException>(() => processor.Open(envelope, typeof(VectorPing), s_key, out _));
    }

    [Fact]
    [DisplayName("Processor: a reader that requires a frame refuses a body written without one")]
    public void Open_RequiredFrameMissing_ThrowsReplayRejected()
    {
        var envelope = CreateProcessor(requireFrame: false).Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key);

        Assert.Throws<ReplayRejectedException>(
            () => CreateProcessor(requireFrame: true).Open(envelope, typeof(VectorPing), s_key, out _));
    }

    [Fact]
    [DisplayName("Processor: an envelope names the codec it was written with, and the reader uses that codec")]
    public void Seal_NamedCodec_IsWrittenAndHonoured()
    {
        var options = CreateOptions(false);
        options.RegisterCodec(new ReversingCodec());
        var processor = new PayloadProcessor(options);

        var element = processor.Wrap(new VectorPing { ClientName = "a" }, PayloadFormat.Encoded, codec: ReversingCodec.CodecName);

        Assert.Equal(ReversingCodec.CodecName, element.GetProperty("codec").GetString());
        Assert.Equal("a", Assert.IsType<VectorPing>(processor.Unwrap(element)).ClientName);
    }

    [Fact]
    [DisplayName("Processor: an envelope on the default codec leaves the codec member out")]
    public void Wrap_DefaultCodec_OmitsCodecMember()
    {
        var element = CreateProcessor(false).Wrap(new VectorPing(), PayloadFormat.Encoded);

        Assert.False(element.TryGetProperty("codec", out _));
    }

    [Fact]
    [DisplayName("Processor: the unencrypted encryptor is refused unless it is allowed explicitly")]
    public void Seal_NoEncryptorNotAllowed_Throws()
    {
        var options = CreateOptions(false);
        options.Encryptor = NoPayloadEncryptor.Instance;

        Assert.Throws<InvalidOperationException>(
            () => new PayloadProcessor(options).Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key));

        options.AllowNoEncryption = true;
        Assert.NotNull(new PayloadProcessor(options).Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key));
    }

    [Fact]
    [DisplayName("Processor: a body that cannot be decoded is reported as one decoding error")]
    public void Open_UndecodableBody_ThrowsInvalidOperationWithInner()
    {
        var processor = CreateProcessor(false);
        var envelope = new PayloadEnvelope
        {
            Format = PayloadFormat.Encoded,
            Body = [1, 2, 3],
            TypeName = VectorPing.PolhemTypeName,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Open(envelope, typeof(VectorPing), null, out _));
        Assert.NotNull(ex.InnerException);
    }

    private static PayloadOptions CreateOptions(bool requireFrame) => new()
    {
        RequireFrame = requireFrame,
        TypeResolver = new PayloadTypeRegistry().Register(typeof(VectorPing), VectorPing.PolhemTypeName),
    };

    private static PayloadProcessor CreateProcessor(bool requireFrame) => new(CreateOptions(requireFrame));

    private sealed class ReversingCodec : IPayloadCodec
    {
        public const string CodecName = "reversed-json";

        private static readonly JsonPayloadCodec s_json = new();

        public string Name => CodecName;

        public byte[] Serialize(object value, Type type) => [.. s_json.Serialize(value, type).Reverse()];

        public object? Deserialize(byte[] bytes, Type type) => s_json.Deserialize([.. bytes.Reverse()], type);
    }
}
