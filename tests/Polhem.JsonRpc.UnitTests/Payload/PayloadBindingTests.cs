using System.Security.Cryptography;
using Polhem.JsonRpc.Payload;

namespace Polhem.JsonRpc.UnitTests.Payload;

/// <summary>
/// The HMAC of an encrypted payload covers the method and the direction of the call (ADR-003), so a captured payload
/// opens only as what it was written for.
/// </summary>
public class PayloadBindingTests
{
    private static readonly byte[] s_key = RandomNumberGenerator.GetBytes(AesCbcHmacPayloadEncryptor.KeySize);

    private const string GetData = "Form.GetData";
    private const string Delete = "Form.Delete";

    [Fact(DisplayName = "Binding: the parameters of a call open only for the method they were written for")]
    public void OpenRequest_OtherMethod_FailsAuthentication()
    {
        var processor = CreateProcessor();
        var envelope = PayloadEnvelope.Read(processor.WrapRequest(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 1));

        Assert.NotNull(processor.OpenRequest(envelope, typeof(VectorPing), s_key, GetData, out _));
        Assert.Throws<CryptographicException>(() => processor.OpenRequest(envelope, typeof(VectorPing), s_key, Delete, out _));
    }

    [Fact(DisplayName = "Binding: a result does not open as the parameters of a call, and parameters do not open as a result")]
    public void Open_OtherDirection_FailsAuthentication()
    {
        var processor = CreateProcessor();
        var response = processor.SealResponse(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key);
        var request = processor.WrapRequest(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        Assert.Throws<CryptographicException>(() => processor.OpenRequest(response, typeof(VectorPing), s_key, GetData, out _));
        Assert.Throws<CryptographicException>(() => processor.UnwrapResult<VectorPing>(GetData, PayloadFormat.Encrypted, request, s_key));
    }

    [Fact(DisplayName = "Binding: a result opens only for the method of the request it answers")]
    public void UnwrapResult_OtherMethod_FailsAuthentication()
    {
        var processor = CreateProcessor();
        var result = processor.SealResponse(GetData, new VectorPing { ClientName = "a" }, PayloadFormat.Encrypted, key: s_key).ToElement();

        Assert.Equal("a", processor.UnwrapResult<VectorPing>(GetData, PayloadFormat.Encrypted, result, s_key)!.ClientName);
        Assert.Throws<CryptographicException>(() => processor.UnwrapResult<VectorPing>(Delete, PayloadFormat.Encrypted, result, s_key));
    }

    [Fact(DisplayName = "Binding: the methods that take no method name refuse an encrypted payload, in both directions")]
    public void UnboundMethods_Encrypted_Throw()
    {
        var processor = CreateProcessor();
        var bound = processor.WrapRequest(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 1);

        Assert.Throws<InvalidOperationException>(() => processor.Wrap(new VectorPing(), PayloadFormat.Encrypted, key: s_key));
        Assert.Throws<InvalidOperationException>(() => processor.Seal(new VectorPing(), PayloadFormat.Encrypted, key: s_key));
        Assert.Throws<InvalidOperationException>(() => processor.Unwrap(bound, s_key));
        Assert.Throws<InvalidOperationException>(() => processor.Unwrap<VectorPing>(bound, s_key));
        Assert.Throws<InvalidOperationException>(() => processor.OpenRequest(PayloadEnvelope.Read(bound), typeof(VectorPing), s_key, out _));
    }

    [Fact(DisplayName = "Binding: the methods that take no method name still seal and open plain and encoded payloads")]
    public void UnboundMethods_Encoded_RoundTrip()
    {
        var processor = CreateProcessor();

        var element = processor.Wrap(new VectorPing { ClientName = "a" }, PayloadFormat.Encoded, sequence: 1);

        Assert.Equal("a", processor.Unwrap<VectorPing>(element)!.ClientName);
    }

    [Fact(DisplayName = "Binding: an encryptor that does not implement associated data is refused rather than left unbound")]
    public void WrapRequest_EncryptorWithoutAssociatedData_Throws()
    {
        var processor = new PayloadProcessor(new PayloadOptions { RequireFrame = true, Encryptor = new UnboundEncryptor() });

        Assert.Throws<NotSupportedException>(
            () => processor.WrapRequest(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 1));
    }

    [Fact(DisplayName = "Binding: an encryptor that does not implement associated data cannot open the parameters of a call, so the method does not run unbound")]
    public void OpenRequest_EncryptorWithoutAssociatedData_Throws()
    {
        var envelope = PayloadEnvelope.Read(CreateProcessor().WrapRequest(GetData, new VectorPing(), PayloadFormat.Encrypted, key: s_key, sequence: 1));
        var server = new PayloadProcessor(new PayloadOptions
        {
            RequireFrame = true,
            Encryptor = new UnboundEncryptor(),
            TypeResolver = new PayloadTypeRegistry().Register<VectorPing>(),
        });

        Assert.Throws<NotSupportedException>(() => server.OpenRequest(envelope, typeof(VectorPing), s_key, GetData, out _));
    }

    [Theory(DisplayName = "AES-CBC-HMAC: data encrypted with associated data decrypts only with the same associated data")]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 1, 3 })]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 2, 2 })]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 1, 2, 0 })]
    [InlineData(new byte[] { 1, 2 }, new byte[0])]
    public void AesCbcHmac_OtherAssociatedData_FailsAuthentication(byte[] written, byte[] read)
    {
        var encryptor = new AesCbcHmacPayloadEncryptor();
        var encrypted = encryptor.Encrypt([1, 2, 3], s_key, written);

        Assert.Equal([1, 2, 3], encryptor.Decrypt(encrypted, s_key, written));
        Assert.Throws<CryptographicException>(() => encryptor.Decrypt(encrypted, s_key, read));
    }

    private static PayloadProcessor CreateProcessor() => new(new PayloadOptions
    {
        RequireFrame = true,
        TypeResolver = new PayloadTypeRegistry().Register<VectorPing>(),
    });

    // An encryptor written against 1.0: it implements only the overloads without associated data.
    private sealed class UnboundEncryptor : IPayloadEncryptor
    {
        private readonly AesCbcHmacPayloadEncryptor _inner = new();

        public string Name => "unbound";

        public byte[] Encrypt(byte[] bytes, byte[] key) => _inner.Encrypt(bytes, key);

        public byte[] Decrypt(byte[] bytes, byte[] key) => _inner.Decrypt(bytes, key);
    }
}
